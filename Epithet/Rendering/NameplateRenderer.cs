using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.Plugin.Services;
using Epithet.Interop;
using Epithet.Models;
using Lumina.Text;
using Lumina.Text.ReadOnly;
using Dalamud.Utility;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

using Newtonsoft.Json;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Epithet.Rendering;

/// <summary>
/// Standalone nameplate renderer with managed major updates and carefully scoped
/// native animated-title writes. No hooks or signature scans; animated title
/// buffers require unsafe FFXIVClientStructs and must be tested per game patch.
/// </summary>
public sealed class NameplateRenderer : IDisposable
{
    private readonly Plugin plugin;
    private readonly INamePlateGui namePlates;
    private readonly IFramework framework;
    private readonly IAddonLifecycle addonLifecycle;
    // Any UI/command thread may request a refresh, but only the framework
    // thread may interact with the game's nameplate services.
    private int pendingExplicitRedraw;
    private readonly Dictionary<uint, (HonorificTitle Data, DateTime Seen, string CharacterName, uint HomeWorldId)> foreignTitles = new();
    // Cache actual native title bytes per live game object. A game nameplate has
    // a backing text buffer separate from Dalamud's editable input string array.
    // Never retain the one-frame handler or native pointer in this dictionary.
    private readonly Dictionary<ulong, byte[]> lastNativeTitleByActor = new();
    public long AntiFlashApplied { get; private set; }
    public long AntiFlashErrors { get; private set; }
    public long OutlineFlagUpdates { get; private set; }
    public long NativeTitleUpdates { get; private set; }
    public long NativeTitleUnchanged { get; private set; }
    public long NativeTitleErrors { get; private set; }
    public string NativeAnimationStatus { get; private set; } = "Waiting for an animated player title";
    private DateTime lastRedraw = DateTime.MinValue;
    private DateTime started = DateTime.UtcNow;
    // Counters allow comparing clock activity, DataUpdate callbacks and actual writes.
    // A preview animation is not proof that a nameplate was updated by the game.
    public long DataUpdateCalls { get; private set; }
    public long MajorUpdateCalls { get; private set; }
    public long AnimatedPasses { get; private set; }
    public long AnimatedTitleWrites { get; private set; }
    public long AnimationFrameWrites { get; private set; }
    public long LastObservedAnimationSecond { get; private set; }
    public string LastAnimationPath { get; private set; } = "Waiting for nameplate data";
    public long LocalTitleWrites { get; private set; }
    public long LocalMajorApplyPasses { get; private set; }
    public DateTime? LastLocalMajorApplyUtc { get; private set; }
    public long RemoteTitleWrites { get; private set; }
    public string ActiveLocalEffect => plugin.ActiveLocalProfile is { } p
        ? ResolveTitle(p)?.Effect ?? "No active title" : "No bound character profile";
    public string LastAnimatedColor { get; private set; } = "(no animation rendered yet)";
    private uint lastTerritory;
    private string lastLocalTitle = "";
    private uint lastLocalJob;
    private bool lastCombat;

    public int RemoteTitleCount => foreignTitles.Count;

    public NameplateRenderer(Plugin owner, INamePlateGui service, IFramework frames, IAddonLifecycle lifecycle)
    {
        plugin = owner;
        namePlates = service;
        framework = frames;
        addonLifecycle = lifecycle;
        // Delay ALL framework/nameplate subscriptions until Plugin constructed its
        // APIs and sync fields. Dalamud may create plugins off the framework thread.
        // Registering callbacks prematurely also risks racing Plugin.Sync initialization.
    }

    private bool isStarted;

    public void Start()
    {
        if (isStarted) return;
        // No player/game-object data is accessed here. The actual reads happen
        // inside OnFrameworkUpdate, which Dalamud invokes on its game framework thread.
        addonLifecycle.RegisterListener(AddonEvent.PreRequestedUpdate, "NamePlate", OnNameplateRequestedUpdate);
        namePlates.OnNamePlateUpdate += OnNameplateUpdate;
        namePlates.OnPostDataUpdate += OnAnimationDataUpdate;
        framework.Update += OnFrameworkUpdate;
        isStarted = true;
    }

    // These counters separate an IPC call from a successfully resolved game object
    // and from a later nameplate-render callback. None proves on-screen visibility.
    public long RemoteAssignmentsAccepted { get; private set; }
    public long RemoteAssignmentsRejected { get; private set; }
    public long RemoteMajorApplyPasses { get; private set; }
    public long SyntheticMajorApplyPasses { get; private set; }
    private uint syntheticTargetEntityId;
    public DateTime? LastRemoteMajorApplyUtc { get; private set; }
    public string LastRemoteAssignmentStatus { get; private set; } = "No remote IPC assignment received";

    public bool SetRemote(int index, HonorificTitle? value, bool synthetic = false)
    {
        if (index < 0 || index >= Plugin.Objects.Length || Plugin.Objects[index] is not IPlayerCharacter pc)
        {
            if (!synthetic) RemoteAssignmentsRejected++;
            LastRemoteAssignmentStatus = "Rejected: target object index is not a visible player";
            return false;
        }
        if (Plugin.Objects.LocalPlayer?.EntityId == pc.EntityId)
        {
            if (!synthetic) RemoteAssignmentsRejected++;
            LastRemoteAssignmentStatus = "Rejected: sync tried to assign a remote title to the local player";
            return false;
        }
        if (value is { } title && !title.IsSafe())
        {
            if (!synthetic) RemoteAssignmentsRejected++;
            LastRemoteAssignmentStatus = "Rejected: invalid remote title payload";
            return false;
        }
        if (value is { } accepted)
        {
            foreignTitles[pc.EntityId] = (accepted, DateTime.UtcNow, pc.Name.TextValue, pc.HomeWorld.RowId);
            LastRemoteAssignmentStatus = accepted.Extension is null
                ? "Accepted target and basic title; waiting for nameplate callback"
                : "Accepted target and extended title; waiting for nameplate callback";
        }
        else
        {
            foreignTitles.Remove(pc.EntityId);
            LastRemoteAssignmentStatus = "Remote title cleared for a visible target";
        }
        if (synthetic)
            syntheticTargetEntityId = value is null ? 0 : pc.EntityId;
        else
        {
            RemoteAssignmentsAccepted++;
            if (syntheticTargetEntityId == pc.EntityId) syntheticTargetEntityId = 0;
        }
        lastNativeTitleByActor.Clear();
        namePlates.RequestRedraw();
        return true;
    }

    public string GetTitleForIndex(int index)
    {
        if (index < 0 || index >= Plugin.Objects.Length) return string.Empty;
        if (Plugin.Objects[index] is not IPlayerCharacter pc) return string.Empty;
        var own = Plugin.Objects.LocalPlayer;
        if (own is not null && own.EntityId == pc.EntityId) return GetLocalJson();
        return foreignTitles.TryGetValue(pc.EntityId, out var remote)
            && remote.CharacterName == pc.Name.TextValue && remote.HomeWorldId == pc.HomeWorld.RowId
            ? JsonConvert.SerializeObject(remote.Data) : string.Empty;
    }

    public string GetLocalJson()
    {
        var p = plugin.ActiveLocalProfile;
        var t = p is null ? null : ResolveTitle(p);
        return t is null ? string.Empty : JsonConvert.SerializeObject(
            HonorificTitle.FromPreset(t, plugin.Config.UseLegacyHonorificWireFormat ? null : p));
    }

    public string GetLocalEpithetJson()
    {
        var profile = plugin.ActiveLocalProfile;
        var title = profile is null ? null : ResolveTitle(profile);
        return title is null ? string.Empty : JsonConvert.SerializeObject(EpithetTitleData.FromPreset(title, profile));
    }

    public string GetEpithetTitleForIndex(int index)
    {
        if (index < 0 || index >= Plugin.Objects.Length || Plugin.Objects[index] is not IPlayerCharacter pc)
            return string.Empty;
        if (Plugin.Objects.LocalPlayer?.EntityId == pc.EntityId) return GetLocalEpithetJson();
        var legacyJson = GetTitleForIndex(index);
        if (string.IsNullOrWhiteSpace(legacyJson)) return string.Empty;
        try
        {
            var payload = JsonConvert.DeserializeObject<HonorificTitle>(legacyJson);
            return payload is null ? string.Empty : JsonConvert.SerializeObject(EpithetTitleData.FromInterop(payload));
        }
        catch { return string.Empty; }
    }

    public TitlePreset? ResolveTitle(CharacterProfile p)
    {
        if (!p.Enabled || p.Titles is null) return null;
        var player = Plugin.Objects.LocalPlayer;
        if (player is null) return null;
        var candidates = p.Titles.Where(t => t.Enabled && !string.IsNullOrWhiteSpace(t.Text)
             && t.Text.Length <= 32 && !t.Text.Any(char.IsControl)
             && (t.JobId == 0 || t.JobId == player.ClassJob.RowId)
             && (t.TerritoryId == 0 || t.TerritoryId == Plugin.ClientState.TerritoryType)
             && (!t.OnlyInCombat || Plugin.Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat])
             && (!t.OnlyOutOfCombat || !Plugin.Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat]))
             .ToList();
        if (candidates.Count == 0) return null;
        var preferred = candidates.FirstOrDefault(t => t.Id == p.ActiveTitleId) ?? candidates[0];
        var rotation = candidates.Where(t => t.Rotate).ToArray();
        if (!preferred.Rotate || rotation.Length == 0) return preferred;
        var secs = Math.Clamp(preferred.RotationSeconds, 5, 3600);
        var slot = (int)((DateTime.UtcNow - started).TotalSeconds / secs) % rotation.Length;
        return rotation[slot];
    }

    private void OnFrameworkUpdate(IFramework frame)
    {
        var now = DateTime.UtcNow;
        // Only use the framework tick to detect *actual title/configuration* changes.
        // Never request a full nameplate redraw simply because time has advanced.
        if (now - lastRedraw < TimeSpan.FromMilliseconds(100)) return;
        lastRedraw = now;
        // IPC getters can read the object table. Only expose them after the first
        // framework-thread callback, never from plugin constructors/background load.
        plugin.Api.StartOnFrameworkThread();
        // Local test shares the real receiver IPC callback but never leaves this client.
        plugin.Compatibility.PulseLocalLoopback(now);
        var player = Plugin.Objects.LocalPlayer;
        var territoryChanged = Plugin.ClientState.TerritoryType != lastTerritory;
        if (territoryChanged || player is null)
        {
            foreignTitles.Clear();
            syntheticTargetEntityId = 0;
            lastNativeTitleByActor.Clear();
            lastTerritory = Plugin.ClientState.TerritoryType;
        }
        // Remote IPC titles are often published on *change*, not on a 20-minute
        // heartbeat. Expiring valid titles here would make them silently disappear.
        // Zone transitions clear this cache; identity is verified on every render.

        var localJson = GetLocalJson();
        var combat = Plugin.Conditions[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat];
        var job = player?.ClassJob.RowId ?? 0;
        var forceRedraw = System.Threading.Interlocked.Exchange(ref pendingExplicitRedraw, 0) != 0;
        if (forceRedraw || lastLocalTitle != localJson || lastCombat != combat || lastLocalJob != job)
        {
            lastLocalTitle = localJson;
            lastCombat = combat;
            lastLocalJob = job;
            plugin.Compatibility.NotifyLocalChange(localJson);
            plugin.Api.NotifyLocalChange(GetLocalEpithetJson());
            namePlates.RequestRedraw();
        }
        // Retry a throttled IPC publication if the user edited while cooldown
        // was active; this does not cause a game nameplate redraw.
        plugin.Compatibility.NotifyLocalChange(localJson);
        plugin.Api.NotifyLocalChange(GetLocalEpithetJson());
        // Detect newly loaded sync clients, and refresh the shared title after
        // zone changes or a long-lived sync service reconnects.
        plugin.Sync.Pulse(now, territoryChanged);
    }

    /// <summary>
    /// Prevent the NamePlate addon from temporarily hiding its text nodes while
    /// rebuilding the plate after a title-color animation. This is based on the
    /// well-established Honorific PreRequestedUpdate anti-flashing behavior.
    /// The fallback can be switched off in Compatibility if future game updates
    /// change the native NamePlate layout.
    /// </summary>
    private unsafe void OnNameplateRequestedUpdate(AddonEvent type, AddonArgs args)
    {
        if (!plugin.Config.RenderInGame) return;
        try
        {
            var addon = (AtkUnitBase*)args.Addon.Address;
            if (addon == null) return;

            // Anti-flicker is optional; outline support is not coupled to it. The old
            // early-return skipped title-node flags whenever the addon was already in
            // update mode, which could leave the title without an edge renderer.
            if (plugin.Config.EnableAntiFlashing)
            {
                var stage = AtkStage.Instance();
                if (stage != null)
                {
                    var arrays = stage->GetNumberArrayData();
                    if (arrays != null && arrays[5] != null && arrays[5]->IntArray != null)
                    {
                        var mode = &arrays[5]->IntArray[3];
                        if (*mode != 1) { *mode = 1; AntiFlashApplied++; }
                    }
                }
            }

            foreach (var node in addon->UldManager.Nodes)
            {
                if (node.Value == null || node.Value->Type != (NodeType)1001 || !node.Value->IsVisible())
                    continue;
                var component = ((AtkComponentNode*)node.Value)->Component;
                if (component == null) continue;

                // Text node 2 is the title field in the current NamePlate component.
                // Enable edge/glare drawing there; the SeString EdgeColor payload sets
                // the title-only color, so the character-name color is not overwritten.
                var titleNode = component->GetTextNodeById(2);
                if (titleNode != null && titleNode->IsVisible())
                {
                    var flags = titleNode->TextFlags;
                    titleNode->TextFlags |= TextFlags.Edge | TextFlags.Glare;
                    if (flags != titleNode->TextFlags) OutlineFlagUpdates++;
                }

                // Preserve the existing anti-flash flags on the regular text node.
                var textNode = component->GetTextNodeById(3);
                if (textNode == null || !textNode->IsVisible()) continue;
                if (plugin.Config.EnableAntiFlashing)
                    textNode->TextFlags = TextFlags.Edge | TextFlags.Glare | TextFlags.MultiLine |
                        (TextFlags)0x1000 | (TextFlags)0x4000 | (TextFlags)0x8000;
                else
                    textNode->TextFlags |= TextFlags.Edge;
            }
        }
        catch (Exception ex)
        {
            AntiFlashErrors++;
            // Never let the mitigation throw into the game's UI lifecycle.
            if (AntiFlashErrors <= 3)
                Plugin.Log.Warning(ex, "Epithet NamePlate anti-flashing update failed");
        }
    }

    private void OnAnimationDataUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        if (!plugin.Config.RenderInGame) return;
        if (plugin.Config.DisableNativeAnimationWriter)
        {
            // Safe mode: do not mutate native Title buffers or mark plates dirty.
            // Normal major updates still provide a title but motion may freeze.
            LastAnimationPath = "Native animation disabled by user for stability testing";
            return;
        }
        DataUpdateCalls++;
        AnimatedPasses++;
        LastObservedAnimationSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Animation path ONLY updates native DisplayTitle when the color actually
        // changes. It must never reassign managed TitleParts or the draw flags:
        // doing that each frame provokes additional NamePlate string-array updates
        // and makes both the title and the real player name blink.
        var writes = RenderHandlers(handlers, animatedOnly: true);
        if (writes > 0) AnimationFrameWrites++;
        LastAnimationPath = $"PostDataUpdate: {handlers.Count} plates, {writes} native title frames changed";
    }

    private void OnNameplateUpdate(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        MajorUpdateCalls++;
        // Major updates use the cooperative Dalamud API. Invalidate the native
        // title cache because the game may have recreated the plate buffer.
        foreach (var handler in handlers)
            lastNativeTitleByActor.Remove(handler.GameObjectId);
        if (plugin.Config.RenderInGame) RenderHandlers(handlers, animatedOnly: false);
    }

    private int RenderHandlers(IReadOnlyList<INamePlateUpdateHandler> handlers, bool animatedOnly)
    {
        // Sample a common wall-clock phase rather than a local plugin-start clock:
        // different Epithet clients then advance their palettes at the same rate.
        // Modulo keeps float precision; casting Unix seconds directly to float
        // would lose the low bits and freeze the visible animation.
        var hz = Math.Clamp(plugin.Config.AnimationFps, 8, 60);
        // Use a bounded UTC wall-clock phase so long-running sessions do not lose
        // animation precision. Both Epithet clients can sample the same phase.
        var millis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 600_000L;
        var seconds = (float)(Math.Floor(millis * hz / 1000.0) / hz);
        var own = Plugin.Objects.LocalPlayer;
        var localProfile = plugin.ActiveLocalProfile;
        var changedFrames = 0;
        foreach (var h in handlers)
        {
            try
            {
                if (h.PlayerCharacter is not { } player) continue;
                var self = own is not null && player.EntityId == own.EntityId;
                if (self && localProfile is not null)
                {
                    var title = ResolveTitle(localProfile);
                    if (animatedOnly && title is not null && !IsAnimated(title.Effect)) continue;
                    if (title is not null)
                    {
                        var animated = IsAnimated(title.Effect);
                        var updated = ApplyTitle(h, title.Text, title.IsPrefix, title.StartColor, title.MiddleColor, title.EndColor, title.ColorCount, title.MiddlePosition, title.StartPosition, title.EndPosition, title.Effect, seconds * localProfile.AnimationSpeed,
                            title.UseGlow && ColorUtils.TryParseHex(title.GlowColor, out var g) ? g : null,
                            animated, !animatedOnly);
                        LocalTitleWrites++;
                        if (!animatedOnly)
                        {
                            LocalMajorApplyPasses++;
                            LastLocalMajorApplyUtc = DateTime.UtcNow;
                        }
                        if (animatedOnly && updated)
                        {
                            AnimatedTitleWrites++;
                            changedFrames++;
                            LastAnimatedColor = FirstAnimatedColor(title.StartColor, title.MiddleColor, title.EndColor,
                                title.ColorCount, title.Effect, seconds * localProfile.AnimationSpeed, title.MiddlePosition, title.StartPosition, title.EndPosition);
                        }
                    }
                    else if (!animatedOnly && localProfile.HideVanillaTitle) h.DisplayTitle = false;
                }
                else if (!self && plugin.Config.ShowRemoteTitles && foreignTitles.TryGetValue(player.EntityId, out var remote)
                    && remote.CharacterName == player.Name.TextValue && remote.HomeWorldId == player.HomeWorld.RowId)
                {
                    var isNativeAnimated = remote.Data.GradientColourSet == 0 ||
                        (remote.Data.GradientColourSet == -1 && remote.Data.GradientAnimationStyle is 0 or 1);
                    // The Epithet extension is authoritative when present. A
                    // static 3-stop title must NOT accidentally animate due to
                    // its legacy Honorific compatibility fields.
                    if (animatedOnly && !(remote.Data.Extension is { } e
                            ? IsAnimated(e.TitleEffect) : isNativeAnimated)) continue;
                    var c = remote.Data.Color ?? Vector3.One;
                    var color = ColorUtils.ToHex(c);
                    var ext = remote.Data.Extension;
                    var nativeDual = ext is null && remote.Data.GradientColourSet == -1
                                     && remote.Data.Glow.HasValue && remote.Data.Color3.HasValue;
                    var from = nativeDual ? ColorUtils.ToHex(remote.Data.Glow!.Value) : color;
                    var to = nativeDual ? ColorUtils.ToHex(remote.Data.Color3!.Value) : color;
                    var nativeEffect = remote.Data.GradientColourSet == 0 ? "Rainbow" :
                        remote.Data.GradientAnimationStyle switch
                        {
                            0 when nativeDual => "Pulse",
                            1 when nativeDual => "Wave",
                            _ => nativeDual ? "Gradient" : "Static"
                        };
                    var chosenEffect = ext?.TitleEffect ?? nativeEffect;
                    // Honorific's Glow field is also reused as the FIRST color
                    // stop for custom dual-color gradients. Epithet v1.8.0 sends
                    // separate extension fields so glow and 1/2/3 palette stops
                    // no longer fight over the same wire field.
                    Vector3? remoteOutline = remote.Data.GradientColourSet == -1 ? null : remote.Data.Glow;
                    if (ext?.TitleGlowEnabled is true &&
                        ColorUtils.TryParseHex(ext.TitleGlowColor, out var parsedOutline))
                        remoteOutline = parsedOutline;
                    else if (ext?.TitleGlowEnabled is false)
                        remoteOutline = null;
                    var updated = ApplyTitle(h, remote.Data.Title, remote.Data.IsPrefix,
                        ext?.TitleStartColor ?? from, ext?.TitleMiddleColor ?? from, ext?.TitleEndColor ?? to,
                        ext?.TitleColorCount ?? (nativeDual ? 2 : 1), ext?.TitleMiddlePosition ?? 0.5f,
                        ext?.TitleStartPosition ?? 0f, ext?.TitleEndPosition ?? 1f, chosenEffect,
                        seconds * (ext?.AnimationSpeed ?? 1f), remoteOutline,
                        IsAnimated(chosenEffect), !animatedOnly);
                    RemoteTitleWrites++;
                    if (!animatedOnly)
                    {
                        if (player.EntityId == syntheticTargetEntityId)
                            SyntheticMajorApplyPasses++;
                        else
                        {
                            RemoteMajorApplyPasses++;
                            LastRemoteMajorApplyUtc = DateTime.UtcNow;
                        }
                    }
                    if (animatedOnly && updated) { AnimatedTitleWrites++; changedFrames++; }
                }
                else if (!animatedOnly && !self && plugin.Config.HideVanillaOther) h.DisplayTitle = false;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "Epithet nameplate rendering failed for a character");
            }
        }
        return changedFrames;
    }

    private static bool IsAnimated(string? effect) => effect is "Rainbow" or "Pulse" or "Wave" or "Shift";

    private static string FirstAnimatedColor(string start, string middle, string end,
        int count, string effect, float seconds, float middlePosition, float startPosition, float endPosition)
    {
        ColorUtils.TryParseHex(start, out var a);
        ColorUtils.TryParseHex(middle, out var m);
        ColorUtils.TryParseHex(end, out var z);
        return ColorUtils.ToHex(ColorUtils.TitleColor(a, m, z, count, effect, 0, 10, seconds, middlePosition, startPosition, endPosition));
    }

    /// <summary>
    /// Writes the Dalamud field and (for animated titles) the cached native
    /// display buffer. Without the second write, the game can retain the last
    /// rendered animation frame indefinitely even though callbacks continue.
    /// This uses the FFXIVClientStructs nameplate structure, exactly at the
    /// current frame's address. No pointers survive the callback.
    /// </summary>
    private bool ApplyTitle(INamePlateUpdateHandler h, string text, bool prefix, string start, string middle, string end,
        int colorCount, float middlePosition, float startPosition, float endPosition, string effect, float time, Vector3? glow, bool animated, bool majorUpdate)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 32) return false;
        // Do not update the Dalamud input string array or draw flags inside
        // OnPostDataUpdate: it runs each frame, not just on major plate changes.
        // In particular, re-setting DisplayTitle/TitleParts each animation tick
        // can cause the entire nameplate (including the character name) to blink.
        if (majorUpdate)
        {
            h.IsPrefixTitle = prefix;
            h.DisplayTitle = true;
            h.TitleParts.Text = BuildColored(text, start, middle, end, colorCount, middlePosition, startPosition, endPosition, effect, time, glow).ToDalamudString();
            return false;
        }
        // Static titles are fully handled by the official managed callback.
        if (!animated || !h.DisplayTitle) return false;
        // During animation ONLY write the native cached title bytes. The real
        // player name, quote flags and visibility flags are left untouched.
        var nativeText = BuildColored("《" + text + "》", start, middle, end, colorCount, middlePosition, startPosition, endPosition, effect, time, glow)
            .ToDalamudString().EncodeWithNullTerminator();
        return WriteAnimatedNativeTitle(h, nativeText);
    }

    private unsafe bool WriteAnimatedNativeTitle(INamePlateUpdateHandler h, byte[] bytes)
    {
        if (h.NamePlateInfoAddress == nint.Zero) return false;
        var id = h.GameObjectId;
        if (lastNativeTitleByActor.TryGetValue(id, out var previous) && previous.AsSpan().SequenceEqual(bytes))
        {
            NativeTitleUnchanged++;
            return false;
        }
        try
        {
            // Only changed native title bytes cause a dirty plate. The addon
            // anti-flashing mitigation avoids temporarily hiding text on update.
            // Never request a full redraw for an animation tick.
            var info = (RaptureAtkModule.NamePlateInfo*)h.NamePlateInfoAddress;
            info->DisplayTitle.SetString(bytes);
            // Prefix/suffix is established on MAJOR update via the supported API.
            // Do not toggle prefix, visibility or nameplate flags each frame.
            info->IsDirty = true;
            lastNativeTitleByActor[id] = bytes;
            NativeTitleUpdates++;
            NativeAnimationStatus = "Native DisplayTitle updated and plate marked dirty";
            return true;
        }
        catch (Exception ex)
        {
            NativeTitleErrors++;
            NativeAnimationStatus = "Native writer error: " + ex.GetType().Name;
            // Do not let an animation exception break Dalamud's nameplate event.
            Plugin.Log.Warning(ex, "Epithet native title update failed");
            return false;
        }
    }

    private static ReadOnlySeString BuildColored(string text, string start, string middle, string end,
        int colorCount, float middlePosition, float startPosition, float endPosition, string effect, float time, Vector3? glow)
    {
        ColorUtils.TryParseHex(start, out var first);
        ColorUtils.TryParseHex(middle, out var mid);
        ColorUtils.TryParseHex(end, out var last);
        var b = new SeStringBuilder();
        // Follow Honorific's tested Lumina color-payload order: open a normal
        // foreground color for a glyph, then open its independent edge color,
        // append the glyph, close edge, then close foreground. This is not a
        // post-processing bloom, only the game's native font edge.
        // Unlike setting h.EdgeColor, these macros never recolor the entire
        // nameplate (and thus never change the actual character name's edge).
        for (var i = 0; i < text.Length; i++)
        {
            var color = ColorUtils.TitleColor(first, mid, last, colorCount,
                EpithetExtension.ValidEffect(effect), i, text.Length, time, middlePosition, startPosition, endPosition);
            b.PushColorRgba(new Vector4(color, 1f));
            if (glow.HasValue)
                b.PushEdgeColorRgba(new Vector4(glow.Value, 1f));
            b.Append(text[i].ToString());
            if (glow.HasValue)
                b.PopEdgeColor();
            b.PopColor();
        }
        return b.ToReadOnlySeString();
    }

    // SeString Color and EdgeColor expect 0xAARRGGBB for FFXIV nameplates.
    // The old implementation emitted 0xAABBGGRR, swapping red and blue locally.
    // The Honorific wire payload itself contains normalized RGB Vector3 values.
    internal static uint PackGameColor(Vector3 color)
    {
        uint r = (uint)Math.Clamp((int)MathF.Round(color.X * 255f), 0, 255);
        uint g = (uint)Math.Clamp((int)MathF.Round(color.Y * 255f), 0, 255);
        uint b = (uint)Math.Clamp((int)MathF.Round(color.Z * 255f), 0, 255);
        return 0xFF000000u | (r << 16) | (g << 8) | b;
    }

    public void RequestRedraw() => System.Threading.Interlocked.Exchange(ref pendingExplicitRedraw, 1);

    public void Dispose()
    {
        namePlates.OnNamePlateUpdate -= OnNameplateUpdate;
        namePlates.OnPostDataUpdate -= OnAnimationDataUpdate;
        framework.Update -= OnFrameworkUpdate;
        addonLifecycle.UnregisterListener(AddonEvent.PreRequestedUpdate, "NamePlate", OnNameplateRequestedUpdate);
        foreignTitles.Clear();
        syntheticTargetEntityId = 0;
        lastNativeTitleByActor.Clear();
        namePlates.RequestRedraw();
    }
}
