using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Epithet.Rendering;
using Newtonsoft.Json;

namespace Epithet.Interop;

/// <summary>
/// Epithet's independent local IPC API. It has no Honorific dependency and no network
/// backend; sync plugins may bridge these local events between clients if they support it.
/// </summary>
public sealed class EpithetApi : IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly NameplateRenderer renderer;
    private ICallGateProvider<(uint, uint)>? version;
    private ICallGateProvider<int, string, object>? set;
    private ICallGateProvider<int, object>? clear;
    private ICallGateProvider<int, string>? get;
    private ICallGateProvider<string>? getLocal;
    private ICallGateProvider<string, object>? changed;
    private ICallGateProvider<object>? ready;
    private ICallGateProvider<object>? disposing;
    private bool ownVersion, ownSet, ownClear, ownGet, ownGetLocal;
    private DateTime lastEvent = DateTime.MinValue;
    private string previousPayload = "";

    public bool OwnsIpc { get; private set; }
    public string LastStatus { get; private set; } = "Not initialized";
    public long LocalPublishEvents { get; private set; }
    public long IncomingTitleUpdates { get; private set; }
    public long IncomingRejectedUpdates { get; private set; }
    public DateTime? LastIncomingTitleUtc { get; private set; }

    public EpithetApi(IDalamudPluginInterface pluginInterface, NameplateRenderer renderer)
    {
        pi = pluginInterface;
        this.renderer = renderer;
        LastStatus = "Waiting for first framework update before publishing IPC.";
    }

    private bool initializationAttempted;

    public void StartOnFrameworkThread()
    {
        if (initializationAttempted) return;
        initializationAttempted = true;
        TryInitialize();
    }

    private void TryInitialize()
    {
        try
        {
            var existing = pi.GetIpcSubscriber<(uint, uint)>("Epithet.ApiVersion").InvokeFunc();
            if (existing.Item1 > 0)
            {
                LastStatus = $"Another Epithet API provider is already active (v{existing.Item1}.{existing.Item2}).";
                return;
            }
        }
        catch { /* no provider registered */ }

        try
        {
            version = pi.GetIpcProvider<(uint, uint)>("Epithet.ApiVersion");
            version.RegisterFunc(() => (1u, 0u)); ownVersion = true;
            set = pi.GetIpcProvider<int, string, object>("Epithet.SetCharacterTitle");
            set.RegisterAction((index, json) =>
            {
                try
                {
                    EpithetTitleData? title = null;
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        if (json.Length >= 4096)
                        {
                            LastStatus = "Rejected oversized Epithet title payload.";
                            IncomingRejectedUpdates++;
                            return;
                        }
                        title = JsonConvert.DeserializeObject<EpithetTitleData>(json);
                        if (title is null)
                        {
                            LastStatus = "Rejected empty/invalid Epithet title payload.";
                            IncomingRejectedUpdates++;
                            return;
                        }
                    }
                    if (title is not null && !title.IsSafe())
                    {
                        LastStatus = "Rejected an invalid Epithet title payload.";
                        IncomingRejectedUpdates++;
                        return;
                    }
                    if (!renderer.SetRemote(index, title?.ToInteropTitle()))
                    {
                        IncomingRejectedUpdates++;
                        LastStatus = renderer.LastRemoteAssignmentStatus;
                        return;
                    }
                    IncomingTitleUpdates++;
                    LastIncomingTitleUtc = DateTime.UtcNow;
                    LastStatus = title is null ? "Remote title cleared through Epithet API." : "Remote title accepted through Epithet API.";
                }
                catch (Exception ex)
                {
                    LastStatus = "Invalid Epithet title payload: " + ex.GetType().Name;
                    IncomingRejectedUpdates++;
                    Plugin.Log.Warning(ex, "Epithet API rejected a remote title payload");
                }
            }); ownSet = true;
            clear = pi.GetIpcProvider<int, object>("Epithet.ClearCharacterTitle");
            clear.RegisterAction(index => renderer.SetRemote(index, null)); ownClear = true;
            get = pi.GetIpcProvider<int, string>("Epithet.GetCharacterTitle");
            get.RegisterFunc(renderer.GetEpithetTitleForIndex); ownGet = true;
            getLocal = pi.GetIpcProvider<string>("Epithet.GetLocalCharacterTitle");
            getLocal.RegisterFunc(renderer.GetLocalEpithetJson); ownGetLocal = true;
            changed = pi.GetIpcProvider<string, object>("Epithet.LocalCharacterTitleChanged");
            ready = pi.GetIpcProvider<object>("Epithet.Ready");
            disposing = pi.GetIpcProvider<object>("Epithet.Disposing");
            OwnsIpc = true;
            LastStatus = "Epithet API v1.0 active locally; no network sync is built in.";
            try { ready.SendMessage(); }
            catch (Exception announceEx) { Plugin.Log.Warning(announceEx, "Epithet Ready subscriber failed"); }
        }
        catch (Exception ex)
        {
            LastStatus = "Epithet API could not register: " + ex.GetType().Name;
            Plugin.Log.Warning(ex, "Epithet local IPC registration failed");
            DisposeProviders();
        }
    }

    public void NotifyLocalChange(string json, bool force = false)
    {
        if (!OwnsIpc) return;
        if (!force && json == previousPayload) return;
        if (!force && DateTime.UtcNow - lastEvent < TimeSpan.FromMilliseconds(450)) return;
        try
        {
            changed?.SendMessage(json);
            previousPayload = json;
            lastEvent = DateTime.UtcNow;
            LocalPublishEvents++;
        }
        catch (Exception ex) { Plugin.Log.Warning(ex, "Failed to publish Epithet local IPC event"); }
    }

    public string RunLocalDiagnostics()
    {
        if (!OwnsIpc) return LastStatus;
        try
        {
            var v = pi.GetIpcSubscriber<(uint, uint)>("Epithet.ApiVersion").InvokeFunc();
            var json = pi.GetIpcSubscriber<string>("Epithet.GetLocalCharacterTitle").InvokeFunc();
            if (v.Item1 != 1) return "Unexpected Epithet API version.";
            if (string.IsNullOrWhiteSpace(json)) return "Epithet API reachable; no active custom title is configured.";
            var data = JsonConvert.DeserializeObject<EpithetTitleData>(json);
            return data is { } title && title.IsSafe() ? "Epithet API local self-check passed. Other clients still need a supported sync transport." : "Epithet API returned invalid title data.";
        }
        catch (Exception ex) { return "Epithet API diagnostics failed: " + ex.GetType().Name; }
    }

    private void DisposeProviders()
    {
        try { if (ownVersion) version?.UnregisterFunc(); } catch { }
        try { if (ownSet) set?.UnregisterAction(); } catch { }
        try { if (ownClear) clear?.UnregisterAction(); } catch { }
        try { if (ownGet) get?.UnregisterFunc(); } catch { }
        try { if (ownGetLocal) getLocal?.UnregisterFunc(); } catch { }
        ownVersion = ownSet = ownClear = ownGet = ownGetLocal = false;
        OwnsIpc = false;
        version = null; set = null; clear = null; get = null; getLocal = null;
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
