using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Epithet.Rendering;
using Newtonsoft.Json;

namespace Epithet.Interop;

/// <summary>
/// Compatibility implementation of Honorific's publicly documented v3 IPC shape.
/// Epithet supplies the same IPC surface to compatible sync clients, without
/// requiring the Honorific plugin. Lightless is known to consume these endpoints.
/// The sync client owns networking, pairing, and remote transport; Snowcloak and
/// PlayerSync require version-specific end-to-end verification.
/// </summary>
public sealed class HonorificBridge : IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly NameplateRenderer renderer;
    private ICallGateProvider<(uint, uint)>? api;
    private ICallGateProvider<int, string, object>? set;
    private ICallGateProvider<int, object>? clear;
    private ICallGateProvider<int, string>? get;
    private ICallGateProvider<string>? getLocal;
    private ICallGateProvider<string, object>? changed;
    private ICallGateProvider<object>? ready;
    private ICallGateProvider<object>? disposing;
    // Track registrations individually: never unregister another plugin's IPC.
    private bool ownVersion, ownSet, ownClear, ownGet, ownGetLocal;
    private DateTime lastEvent = DateTime.MinValue;
    private string previousPayload = "";
    public DateTime? LastSuccessfulPublishUtc { get; private set; }
    public long LocalPublishEvents { get; private set; }
    public long ForcedLocalPublishEvents { get; private set; }
    public DateTime? LastIncomingTitleUtc { get; private set; }
    public int IncomingTitleUpdates { get; private set; }
    public int IncomingExtendedUpdates { get; private set; }
    public int IncomingBasicUpdates { get; private set; }
    public int IncomingClearEvents { get; private set; }
    public int IncomingRejectedUpdates { get; private set; }
    public long LocalGetRequests { get; private set; }
    public long IncomingRawSetCalls { get; private set; }
    public long IncomingRawClearCalls { get; private set; }
    public long IncomingRawExtendedPayloads { get; private set; }
    public long OutgoingLegacyGetCalls { get; private set; }
    public long OutgoingExtendedGetCalls { get; private set; }
    public string LastGetterFingerprint { get; private set; } = "-";
    public string LastGetterFormat { get; private set; } = "No outgoing IPC getter invoked";
    public string TransportMode => Plugin.CurrentConfig.UseLegacyHonorificWireFormat
        ? "Legacy Honorific only (Epithet extension OMITTED)"
        : "Full Epithet extension (3-color/animation metadata INCLUDED)";
    public long ReadyAnnouncements { get; private set; }
    public string LastPublishedFingerprint { get; private set; } = "-";
    public string LastReceivedOutcome { get; private set; } = "No incoming IPC calls";
    public string LastPublicationMode { get; private set; } = "No publication attempted";
    public string LastRejectedReason { get; private set; } = "-";
    public string LastIncomingFingerprint { get; private set; } = "-";
    public string LastIncomingFormat { get; private set; } = "Nothing received";
    public string LastIncomingEffect { get; private set; } = "Nothing received";
    public bool OwnsIpc { get; private set; }
    public bool RealHonorific { get; private set; }
    public string LastStatus { get; private set; } = "Not initialized";
    // Explicit local-only diagnostic. Never count a loopback as remote network delivery.
    private int loopbackRequested;
    private bool handlingLoopback;
    private uint loopbackEntityId;
    private DateTime loopbackExpiresUtc;
    private string loopbackPayload = "";
    public long LocalLoopbackAccepted { get; private set; }
    public long LocalLoopbackRejected { get; private set; }
    public string LocalLoopbackStatus { get; private set; } = "Not run";
    public DateTime? LastRealIncomingUtc { get; private set; }


    public HonorificBridge(IDalamudPluginInterface pluginInterface, NameplateRenderer renderer)
    {
        pi = pluginInterface;
        this.renderer = renderer;
        // Do not publish IPC / send Honorific.Ready during Dalamud's async
        // constructor. The framework Update runs RefreshEnabledState later.
        LastStatus = Plugin.CurrentConfig.EnableHonorificCompatibility
            ? "Waiting for framework update before initializing compatibility IPC"
            : "Honorific API disabled in Epithet settings";
    }

    public void TryInitialize()
    {
        if (!Plugin.CurrentConfig.EnableHonorificCompatibility || OwnsIpc) return;
        if (RealHonorific)
        {
            // The competing provider may have been unloaded while Epithet stayed
            // active. Recover the fallback automatically, without stealing IPC.
            try
            {
                var active = pi.GetIpcSubscriber<(uint, uint)>("Honorific.ApiVersion").InvokeFunc();
                if (active.Item1 > 0) return;
            }
            catch { /* previous provider was removed */ }
            RealHonorific = false;
        }
        try
        {
            var v = pi.GetIpcSubscriber<(uint, uint)>("Honorific.ApiVersion").InvokeFunc();
            if (v.Item1 > 0)
            {
                RealHonorific = v.Item1 == 3;
                LastStatus = $"Honorific v{v.Item1}.{v.Item2} owns its IPC. Epithet cannot serve Lightless through that endpoint; running both renderers can overwrite titles.";
                return;
            }
        }
        catch { /* provider not registered */ }

        // Do not register handlers if another API provider already exists.
        // All registrations are disposed if any individual call throws.
        try
        {
            api = pi.GetIpcProvider<(uint, uint)>("Honorific.ApiVersion");
            api.RegisterFunc(() => (3u, 2u));
            ownVersion = true;
            set = pi.GetIpcProvider<int, string, object>("Honorific.SetCharacterTitle");
            set.RegisterAction((index, json) =>
            {
                // Record entry before JSON parsing/object validation. An IPC call
                // rejected by the renderer is still an incoming call.
                if (!handlingLoopback) IncomingRawSetCalls++;
                try
                {
                    HonorificTitle? title = null;
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        if (json.Length >= 4096)
                        {
                            MarkIncomingRejected("Rejected: JSON exceeds 4095 characters");
                            return;
                        }
                        title = JsonConvert.DeserializeObject<HonorificTitle>(json);
                        if (title is null)
                        {
                            MarkIncomingRejected("Rejected: empty/invalid JSON object");
                            return;
                        }
                    }
                    if (!handlingLoopback && title?.Extension != null)
                        IncomingRawExtendedPayloads++;
                    // Honorific's IsOriginal payload explicitly requests a
                    // reset to the vanilla title; treat it as a clear event.
                    if (title?.IsOriginal == true) title = null;
                    if (title is not null && !title.IsSafe())
                    {
                        MarkIncomingRejected("Rejected: unsafe or invalid title/extension values");
                        return;
                    }
                    // A network payload is not a successful delivery if Dalamud
                    // cannot resolve the destination object index to a player.
                    if (!handlingLoopback && loopbackEntityId != 0 &&
                        index >= 0 && index < Plugin.Objects.Length &&
                        Plugin.Objects[index] is Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter incomingPc &&
                        incomingPc.EntityId == loopbackEntityId)
                    {
                        // Do not delete a genuinely received title when the local test expires.
                        loopbackEntityId = 0;
                    }
                    if (!renderer.SetRemote(index, title, handlingLoopback))
                    {
                        MarkIncomingRejected(renderer.LastRemoteAssignmentStatus);
                        return;
                    }
                    if (handlingLoopback)
                    {
                        LocalLoopbackAccepted++;
                        LocalLoopbackStatus = "Local IPC loopback accepted by renderer; visual check required. NOT a network delivery.";
                        return;
                    }
                    // Receiving through IPC does not prove a remote network origin;
                    // only the sync client itself can confirm source/pairing.
                    LastRealIncomingUtc = DateTime.UtcNow;
                    LastIncomingFingerprint = Fingerprint(json);
                    LastIncomingTitleUtc = DateTime.UtcNow;
                    IncomingTitleUpdates++;
                    LastReceivedOutcome = title is null ? "Accepted remote clear" :
                        title.Extension is null ? "Accepted basic remote title" : "Accepted extended remote title";
                    if (title?.Extension != null)
                    {
                        IncomingExtendedUpdates++;
                        LastIncomingFormat = $"Epithet v{title.Extension.Version} - {title.Extension.TitleColorCount} colors";
                        LastIncomingEffect = $"{title.Extension.TitleEffect} / {title.Extension.AnimationSpeed:0.00}x";
                    }
                    else if (title != null)
                    {
                        IncomingBasicUpdates++;
                        LastIncomingFormat = "Basic Honorific only (extended colors lost or never sent)";
                        LastIncomingEffect = title.GradientColourSet is 0 ? "Honorific Rainbow" :
                            title.GradientColourSet is -1 ? "Honorific 2-color gradient" : "Static";
                    }
                    else
                    {
                        IncomingClearEvents++;
                        LastIncomingFormat = "Title cleared";
                        LastIncomingEffect = "None";
                    }
                }
                catch (Exception ex)
                {
                    MarkIncomingRejected("Rejected: IPC parsing failed (" + ex.GetType().Name + ")");
                    Plugin.Log.Warning(ex, "Invalid remote Honorific title payload");
                }
            });
            ownSet = true;
            clear = pi.GetIpcProvider<int, object>("Honorific.ClearCharacterTitle");
            clear.RegisterAction(index =>
            {
                if (!handlingLoopback) IncomingRawClearCalls++;
                if (!renderer.SetRemote(index, null, handlingLoopback))
                {
                    MarkIncomingRejected(renderer.LastRemoteAssignmentStatus);
                    return;
                }
                if (handlingLoopback)
                {
                    LocalLoopbackStatus = "Local test title cleared; original nameplate should return.";
                    return;
                }
                LastRealIncomingUtc = DateTime.UtcNow;
                IncomingTitleUpdates++;
                IncomingClearEvents++;
                LastReceivedOutcome = "Accepted remote clear through Honorific.ClearCharacterTitle";
                LastIncomingTitleUtc = DateTime.UtcNow;
                LastIncomingFingerprint = "empty";
                LastIncomingFormat = "Title cleared through sync IPC";
                LastIncomingEffect = "None";
            });
            ownClear = true;
            get = pi.GetIpcProvider<int, string>("Honorific.GetCharacterTitle");
            get.RegisterFunc(renderer.GetTitleForIndex);
            ownGet = true;
            getLocal = pi.GetIpcProvider<string>("Honorific.GetLocalCharacterTitle");
            getLocal.RegisterFunc(() =>
            {
                LocalGetRequests++;
                var wireJson = renderer.GetLocalJson();
                if (Plugin.CurrentConfig.UseLegacyHonorificWireFormat)
                    OutgoingLegacyGetCalls++;
                else
                    OutgoingExtendedGetCalls++;
                LastGetterFingerprint = Fingerprint(wireJson);
                LastGetterFormat = Plugin.CurrentConfig.UseLegacyHonorificWireFormat
                    ? "Legacy Honorific fields only" : "Extended Epithet JSON";
                return wireJson;
            });
            ownGetLocal = true;
            changed = pi.GetIpcProvider<string, object>("Honorific.LocalCharacterTitleChanged");
            ready = pi.GetIpcProvider<object>("Honorific.Ready");
            disposing = pi.GetIpcProvider<object>("Honorific.Disposing");
            OwnsIpc = true;
            LastStatus = "Automatic Honorific v3.2 sync compatibility active (no Honorific install needed)";
            // A faulty subscriber must not disable Epithet's registered IPC.
            try { ready.SendMessage(); ReadyAnnouncements++; }
            catch (Exception announceEx) { Plugin.Log.Warning(announceEx, "Honorific Ready subscriber failed"); }
        }
        catch (Exception ex)
        {
            LastStatus = "Compatibility API could not register: " + ex.GetType().Name;
            Plugin.Log.Warning(ex, "Epithet Honorific IPC registration failed");
            DisposeProviders();
        }
    }

    /// <summary>Apply the sync opt-in switch without a game restart.</summary>
    public void RefreshEnabledState()
    {
        if (Plugin.CurrentConfig.EnableHonorificCompatibility)
        {
            TryInitialize();
            return;
        }
        if (OwnsIpc)
        {
            try { changed?.SendMessage(""); disposing?.SendMessage(); }
            catch (Exception ex) { Plugin.Log.Warning(ex, "Could not announce sync IPC shutdown"); }
            DisposeProviders();
        }
        RealHonorific = false;
        LastStatus = "Automatic multiplayer compatibility disabled in settings";
    }

    /// <summary>Re-announce the local IPC to newly loaded sync clients.</summary>
    public void AnnounceReady()
    {
        if (!OwnsIpc) return;
        try { ready?.SendMessage(); ReadyAnnouncements++; }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Could not re-announce Epithet Honorific IPC"); }
    }

    private void MarkIncomingRejected(string reason)
    {
        if (handlingLoopback)
        {
            LocalLoopbackRejected++;
            LocalLoopbackStatus = "Local loopback rejected: " + reason;
            return;
        }
        IncomingRejectedUpdates++;
        LastRejectedReason = reason;
        LastReceivedOutcome = reason;
        LastStatus = reason;
    }

    /// <summary>Compare fingerprints on two PCs to prove byte-for-byte IPC JSON preservation.</summary>
    public static string Fingerprint(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "empty";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes)[..12];
    }
    public string LocalFingerprint => Fingerprint(renderer.GetLocalJson());

    public string GetTransportSelfCheck()
    {
        var json = renderer.GetLocalJson();
        if (string.IsNullOrWhiteSpace(json)) return "No active title to test";
        try
        {
            var title = JsonConvert.DeserializeObject<HonorificTitle>(json);
            if (title is null || !title.IsSafe()) return "Invalid local IPC payload";
            var hasExtension = title.Extension != null;
            if (Plugin.CurrentConfig.UseLegacyHonorificWireFormat && hasExtension)
                return "ERROR: Legacy mode unexpectedly contains Epithet data";
            if (!Plugin.CurrentConfig.UseLegacyHonorificWireFormat && !hasExtension)
                return "ERROR: Full mode unexpectedly lacks Epithet data";
            return $"{(hasExtension ? "Full Epithet" : "Legacy Honorific")} JSON valid; " +
                $"size={json.Length}, hash={Fingerprint(json)}. Does NOT prove network delivery.";
        }
        catch (Exception ex) { return "Transport JSON invalid: " + ex.GetType().Name; }
    }

    public bool IsAvailable() => OwnsIpc || RealHonorific;

    /// <summary>
    /// Queue a local-only, opt-in 8-second nameplate test on the first nearby player.
    /// This does not send any data to any sync server or other player.
    /// </summary>
    public void RequestLocalLoopback() => System.Threading.Interlocked.Exchange(ref loopbackRequested, 1);

    /// <summary>Called solely from framework.Update.</summary>
    public void PulseLocalLoopback(DateTime nowUtc)
    {
        if (loopbackEntityId != 0 && nowUtc >= loopbackExpiresUtc)
        {
            try
            {
                for (var i = 0; i < Plugin.Objects.Length; i++)
                {
                    if (Plugin.Objects[i] is not Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter pc ||
                        pc.EntityId != loopbackEntityId) continue;
                    // If another IPC provider replaced ours, leave the player alone.
                    if (OwnsIpc && renderer.GetTitleForIndex(i) == loopbackPayload)
                    {
                        handlingLoopback = true;
                        try { pi.GetIpcSubscriber<int, object>("Honorific.ClearCharacterTitle").InvokeAction(i); }
                        finally { handlingLoopback = false; }
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                LocalLoopbackStatus = "Loopback cleanup failed: " + ex.GetType().Name;
            }
            finally
            {
                loopbackEntityId = 0;
                loopbackPayload = "";
            }
        }
        if (System.Threading.Interlocked.Exchange(ref loopbackRequested, 0) == 0) return;
        if (!OwnsIpc)
        {
            LocalLoopbackRejected++;
            LocalLoopbackStatus = "Unavailable: Epithet must own the Honorific IPC.";
            return;
        }
        if (!Plugin.CurrentConfig.RenderInGame || !Plugin.CurrentConfig.ShowRemoteTitles)
        {
            LocalLoopbackRejected++;
            LocalLoopbackStatus = "Enable renderer and received titles before running the test.";
            return;
        }
        if (loopbackEntityId != 0 || renderer.RemoteTitleCount != 0)
        {
            LocalLoopbackRejected++;
            LocalLoopbackStatus = "Skipped: a local probe or remote title already exists; no existing title will be overwritten.";
            return;
        }
        var json = renderer.GetLocalJson();
        if (string.IsNullOrWhiteSpace(json))
        {
            LocalLoopbackRejected++;
            LocalLoopbackStatus = "No active local title. Select an active title before testing.";
            return;
        }
        var local = Plugin.Objects.LocalPlayer;
        if (local is null)
        {
            LocalLoopbackRejected++;
            LocalLoopbackStatus = "No local player available.";
            return;
        }
        for (var i = 0; i < Plugin.Objects.Length; i++)
        {
            if (Plugin.Objects[i] is not Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter target ||
                target.EntityId == local.EntityId) continue;
            try
            {
                handlingLoopback = true;
                try { pi.GetIpcSubscriber<int, string, object>("Honorific.SetCharacterTitle").InvokeAction(i, json); }
                finally { handlingLoopback = false; }
                if (renderer.GetTitleForIndex(i) != json)
                {
                    LocalLoopbackRejected++;
                    LocalLoopbackStatus = "Local IPC returned, but the target did not retain the test title.";
                    return;
                }
                loopbackEntityId = target.EntityId;
                loopbackPayload = json;
                loopbackExpiresUtc = nowUtc.AddSeconds(8);
                LocalLoopbackStatus = "Local title placed on nearby player's nameplate for 8 s (YOUR screen only). Verify visual output; not a network test.";
                return;
            }
            catch (Exception ex)
            {
                handlingLoopback = false;
                LocalLoopbackRejected++;
                LocalLoopbackStatus = "Local loopback IPC error: " + ex.GetType().Name;
                Plugin.Log.Warning(ex, "Epithet loopback failed");
                return;
            }
        }
        LocalLoopbackRejected++;
        LocalLoopbackStatus = "No other visible player found. Stand next to a player and retry.";
    }

    /// <summary>
    /// Checks local IPC wiring only. This cannot prove that a remote player received
    /// a title or that a particular sync client's backend supports Honorific.
    /// </summary>
    public string RunLocalDiagnostics()
    {
        if (!OwnsIpc) return "Epithet is not the active Honorific IPC provider: " + LastStatus;
        try
        {
            var version = pi.GetIpcSubscriber<(uint, uint)>("Honorific.ApiVersion").InvokeFunc();
            if (version.Item1 != 3 || version.Item2 < 1) return "Unexpected IPC version";
            var json = pi.GetIpcSubscriber<string>("Honorific.GetLocalCharacterTitle").InvokeFunc();
            if (string.IsNullOrWhiteSpace(json))
                return "IPC reachable (v3." + version.Item2 + "), but no active in-game character/title has been configured.";
            if (json.Length >= 4096) return "Title JSON exceeds the allowed IPC payload size.";
            var decoded = JsonConvert.DeserializeObject<HonorificTitle>(json);
            if (decoded is null || !decoded.IsSafe()) return "Local IPC returned an invalid title payload.";
            var extension = decoded.Extension is null ? "plain Honorific title" : "Epithet v1 extension present";
            return $"Local IPC checks passed (v3.{version.Item2}, {extension}). Online delivery remains unverified.";
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Epithet IPC self check failed");
            return "IPC diagnostics failed: " + ex.GetType().Name;
        }
    }

    public void NotifyLocalChange(string json, bool force = false)
    {
        if (!Plugin.CurrentConfig.EnableHonorificCompatibility) return;
        // A forced publication is deliberately NOT deduplicated: sync adapters
        // must be able to re-fetch a title after reconnecting or pairing.
        // The previous UI button called this method but was silently ignored
        // whenever the title text/JSON had not changed.
        if (!force && json == previousPayload) return;
        if (!force && DateTime.UtcNow - lastEvent < TimeSpan.FromMilliseconds(450)) return;
        try
        {
            if (OwnsIpc)
            {
                changed?.SendMessage(json);
                LastPublicationMode = "Epithet-owned Honorific.LocalCharacterTitleChanged event (delivery unverified)";
            }
            else if (RealHonorific)
            {
                // Another plugin owns the transport IPC. The previous behavior
                // assigned Epithet's title into Honorific's LOCAL object table,
                // causing conflicting nameplate writers and a misleading flash of
                // the title; it did NOT publish Epithet's extended JSON to the
                // remote sync transport. Do not inject local assignments here.
                LastPublicationMode = "Blocked: Honorific owns the IPC. Unload Honorific to test Epithet full-title transport";
                return;
            }
            if (!OwnsIpc && !RealHonorific)
            {
                LastPublicationMode = "No Honorific IPC provider available";
                return;
            }
            previousPayload = json;
            LastPublishedFingerprint = Fingerprint(json);
            lastEvent = DateTime.UtcNow;
            LastSuccessfulPublishUtc = lastEvent;
            LocalPublishEvents++;
            if (force) ForcedLocalPublishEvents++;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Failed to publish Epithet local title");
        }
    }

    public bool ApplyLocal(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter? localPlayer, Epithet.Models.TitlePreset? preset)
    {
        if (preset is null || localPlayer is null) return false;
        NotifyLocalChange(renderer.GetLocalJson());
        renderer.RequestRedraw();
        LastStatus = OwnsIpc ? "Epithet title updated, available via local sync IPC" :
            RealHonorific ? "Static title forwarded to installed Honorific" : "IPC disabled; title rendered locally";
        return true;
    }

    public void ClearLocal(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter? _) => NotifyLocalChange("");

    private void DisposeProviders()
    {
        try { if (ownVersion) api?.UnregisterFunc(); } catch { }
        try { if (ownSet) set?.UnregisterAction(); } catch { }
        try { if (ownClear) clear?.UnregisterAction(); } catch { }
        try { if (ownGet) get?.UnregisterFunc(); } catch { }
        try { if (ownGetLocal) getLocal?.UnregisterFunc(); } catch { }
        ownVersion = ownSet = ownClear = ownGet = ownGetLocal = false;
        OwnsIpc = false;
        api = null; set = null; clear = null; get = null; getLocal = null;
        changed = null; ready = null; disposing = null;
    }

    public void Dispose()
    {
        if (OwnsIpc)
        {
            try { changed?.SendMessage(""); disposing?.SendMessage(); } catch { }
        }
        DisposeProviders();
    }
}
