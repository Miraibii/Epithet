using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Epithet.Models;
using Epithet.UI;
using Newtonsoft.Json;

namespace Epithet.Windows;

public sealed partial class DashboardWindow
{
    private void DrawCompatibility()
    {
        Hero("MULTIPLAYER SYNC", "Epithet-to-Epithet titles.",
            "Use your existing sync plugin; no Epithet server or Honorific installation needed.","↔",136f);
        ImGui.Spacing();
        Card("##compat_config", 830, () =>
        {
            Caption("NAMEPLATE DISPLAY", "Epithet integrates with Dalamud. No personal server is required.");
            if (StudioToggle("##render", "Enable Epithet renderer", ref cfg.RenderInGame)) MarkDirty();
            if (StudioToggle("##flicker", "Prevent animated nameplate blinking", ref cfg.EnableAntiFlashing)) MarkDirty();
            if (StudioToggle("##safe_native", "Safe renderer test (disable native animation writes)", ref cfg.DisableNativeAnimationWriter)) MarkDirty();
            if (cfg.DisableNativeAnimationWriter)
                ColoredWrapped(Warning, "Safe mode ON: static 1/2/3-color titles remain available; live nameplate animation may freeze. Use this to isolate flicker, not to verify animated playback.");
            if (StudioToggle("##automatch", "Automatically match character + home world", ref cfg.ApplyProfilesAutomatically)) MarkDirty();
            if (StudioToggle("##remote", "Display received titles", ref cfg.ShowRemoteTitles)) MarkDirty();
            if (StudioToggle("##hide_other", "Hide vanilla titles of others", ref cfg.HideVanillaOther)) MarkDirty();
            if (StudioToggle("##reduce_ui_motion", "Reduce interface animations", ref cfg.ReduceUiMotion)) MarkDirty();
            if (StudioToggle("##ipc", "Automatic sync compatibility (Lightless / compatible services)", ref cfg.EnableHonorificCompatibility)) MarkDirty();
            if (StudioToggle("##legacy_wire", "TEST: Legacy Honorific transport (no Epithet extension)", ref cfg.UseLegacyHonorificWireFormat))
            {
                MarkDirty();
                plugin.Sync.RequestPublication();
                notice = "Transport mode changed. New Honorific JSON will publish on the next framework update.";
            }
            ColoredWrapped(cfg.UseLegacyHonorificWireFormat ? Warning : Mint,
                cfg.UseLegacyHonorificWireFormat
                    ? "LEGACY TEST: IPC sends only standard Honorific fields. Local 3-color previews/effects are unchanged, but remote Epithet clients CANNOT receive the extra 3-stop settings in this mode."
                    : "FULL MODE: IPC includes the Epithet JSON extension for 3 color stops and animation settings, subject to sync transport support.");
            if (StudioToggle("##confirm_palette_delete", "Ask before deleting saved palettes", ref cfg.ConfirmPaletteDeletion)) MarkDirty();
            ImGui.TextWrapped("Both players need Epithet and a working pair/connection in the same sync service. The compatible Honorific IPC is provided by Epithet itself; you do NOT need to install Honorific.");
            ImGui.TextWrapped("Only the title JSON is supplied to the sync service. Epithet does not connect to the service's servers, bypass pairing, or share other player data.");
            ImGui.TextColored(plugin.Api.OwnsIpc ? Mint : Warning,
                plugin.Api.OwnsIpc ? "Epithet API v1.0 active" : plugin.Api.LastStatus);
            ImGui.TextWrapped("Epithet's own renderer and local title API work without Honorific. IPC is local to this game process; a separate sync transport is required for other players' clients.");
            ImGui.TextColored(plugin.Compatibility.OwnsIpc ? Mint : Warning,
                plugin.Compatibility.OwnsIpc ? "Automatic sync title endpoint active" : plugin.Compatibility.LastStatus);
            if (plugin.Compatibility.RealHonorific)
                ColoredWrapped(Warning, "Honorific already owns its IPC. For full Epithet-to-Epithet sync, unload Honorific and reload Epithet; otherwise its renderer/API may interfere.");
            ImGui.Spacing();
            Caption("DETECTED SYNC CLIENTS", "Detection confirms local plugin load only, NOT pairing or successful title transfer.");
            ImGui.TextColored(plugin.Sync.LightlessLoaded ? Mint : Warning,
                $"Lightless: {(plugin.Sync.LightlessLoaded ? "loaded (known Honorific IPC consumer)" : "not loaded")}");
            ImGui.TextColored(plugin.Sync.SnowcloakLoaded ? Mint : Warning,
                $"Snowcloak: {(plugin.Sync.SnowcloakLoaded ? "loaded (JSON relay not yet verified)" : "not loaded")}");
            ImGui.TextColored(plugin.Sync.PlayerSyncLoaded ? Mint : Warning,
                $"PlayerSync: {(plugin.Sync.PlayerSyncLoaded ? "loaded (JSON relay not yet verified)" : "not loaded")}");
            ImGui.TextWrapped(plugin.Sync.DetectionStatus);
            if ((plugin.Sync.LightlessLoaded ? 1 : 0) + (plugin.Sync.SnowcloakLoaded ? 1 : 0) + (plugin.Sync.PlayerSyncLoaded ? 1 : 0) > 1)
                ColoredWrapped(Warning, "More than one sync client is loaded. For the next test, use Lightless ONLY on both PCs and confirm pairing in Lightless. Loaded does NOT mean paired.");
            ImGui.TextWrapped("These services use their own pairing and network rules. Installing Epithet on both PCs does not automatically pair you.");
        });
        ImGui.Spacing();
        Card("##sync_transport_probe", 745, () =>
        {
            Caption("CROSS-CLIENT TITLE DIAGNOSTICS | BUILD 1.0.0.2", "Separate the LOCAL receiver probe from the EXTERNAL sync relay.");
            ImGui.TextColored(White, "1. HONORIFIC PROVIDER OWNERSHIP");
            if (plugin.Compatibility.RealHonorific)
                ColoredWrapped(Warning, "Conflict: Honorific owns the API. Do NOT use this state to verify Epithet relay. Unload Honorific on both Epithet clients, then reload Epithet.");
            else
                ColoredWrapped(plugin.Compatibility.OwnsIpc ? Mint : Warning,
                    plugin.Compatibility.OwnsIpc ? "Epithet owns the Honorific v3.2 IPC endpoint." : "Epithet does NOT own the Honorific IPC endpoint.");
            ImGui.TextWrapped(plugin.Compatibility.LastStatus);
            ImGui.Spacing();
            ImGui.TextColored(White, "2. SENDER - LOCAL IPC (NOT NETWORK CONFIRMATION)");
            ImGui.Text("Current transport: " + plugin.Compatibility.TransportMode);
            ImGui.Text($"Local IPC get requests: {plugin.Compatibility.LocalGetRequests} (Legacy: {plugin.Compatibility.OutgoingLegacyGetCalls} / Full: {plugin.Compatibility.OutgoingExtendedGetCalls})");
            ImGui.Text("Last getter JSON hash: " + plugin.Compatibility.LastGetterFingerprint);
            ImGui.TextWrapped("Last getter format: " + plugin.Compatibility.LastGetterFormat);
            ImGui.Text($"RAW incoming Set / Clear IPC calls: {plugin.Compatibility.IncomingRawSetCalls} / {plugin.Compatibility.IncomingRawClearCalls}");
            ImGui.Text($"RAW incoming with Epithet extension: {plugin.Compatibility.IncomingRawExtendedPayloads}");
            ImGui.TextWrapped("RAW counters count IPC calls before validation, but cannot independently prove a network origin. Local loopback is excluded.");
            ImGui.Text($"Ready announcements: {plugin.Compatibility.ReadyAnnouncements}");
            ImGui.Text($"Local title changed events: {plugin.Compatibility.LocalPublishEvents}");
            ImGui.Text($"Last published JSON fingerprint: {plugin.Compatibility.LastPublishedFingerprint}");
            ImGui.TextWrapped(plugin.Compatibility.LastPublicationMode);
            ImGui.Spacing();
            ImGui.TextColored(White, "3. RECEIVER - HONORIFIC IPC");
            ImGui.Text($"Accepted incoming updates: {plugin.Compatibility.IncomingTitleUpdates}");
            ImGui.Text($"Rejected incoming updates: {plugin.Compatibility.IncomingRejectedUpdates}");
            ImGui.Text($"Accepted Epithet extensions: {plugin.Compatibility.IncomingExtendedUpdates}");
            ImGui.Text($"Received JSON fingerprint: {plugin.Compatibility.LastIncomingFingerprint}");
            ImGui.TextWrapped("Last IPC outcome: " + plugin.Compatibility.LastReceivedOutcome);
            ImGui.Spacing();
            ImGui.TextColored(White, "LOCAL-ONLY IPC / RENDERER LOOPBACK (NOT NETWORK)");
            ImGui.Text($"Loopback accepted / rejected: {plugin.Compatibility.LocalLoopbackAccepted} / {plugin.Compatibility.LocalLoopbackRejected}");
            ImGui.Text($"Local-only nameplate render passes: {plugin.Renderer.SyntheticMajorApplyPasses}");
            ImGui.TextWrapped("Result: " + plugin.Compatibility.LocalLoopbackStatus);
            ImGui.TextWrapped("With another player nearby, this temporarily displays YOUR title over that player for 8 seconds ON YOUR CLIENT ONLY. It never sends data to the other player. Existing received titles are not overwritten.");
            if (StyledButton("Test local receiver + renderer (8s)", new Vector2(315, 33)))
            {
                plugin.Compatibility.RequestLocalLoopback();
                notice = "Local-only loopback queued; not proof of networking.";
            }
            if (plugin.Compatibility.IncomingRejectedUpdates > 0)
                ColoredWrapped(Warning, "Last rejection: " + plugin.Compatibility.LastRejectedReason);
            ImGui.Spacing();
            ImGui.TextColored(White, "4. LOCAL RENDERER CALLBACKS (NOT VISIBILITY PROOF)");
            ImGui.Text($"Remote assignments accepted / rejected: {plugin.Renderer.RemoteAssignmentsAccepted} / {plugin.Renderer.RemoteAssignmentsRejected}");
            ImGui.Text($"Local / remote major render passes: {plugin.Renderer.LocalMajorApplyPasses} / {plugin.Renderer.RemoteMajorApplyPasses}");
            ImGui.TextWrapped("Last target status: " + plugin.Renderer.LastRemoteAssignmentStatus);
            if (StyledButton("Copy diagnostic snapshot", new Vector2(220, 33)))
            {
                var snapshot = string.Join(Environment.NewLine, new[]
                {
                    "EPITHET 1.0.0.2 / CROSS CLIENT DIAGNOSIS (no character names or title JSON)",
                    "Transport mode: " + plugin.Compatibility.TransportMode,
                    "Local wire self-check: " + plugin.Compatibility.GetTransportSelfCheck(),
                    "Epithet owns Honorific IPC: " + plugin.Compatibility.OwnsIpc,
                    "Installed Honorific owns IPC: " + plugin.Compatibility.RealHonorific,
                    "Renderer enabled: " + cfg.RenderInGame,
                    "Safe renderer mode: " + cfg.DisableNativeAnimationWriter,
                    "Remote title display enabled: " + cfg.ShowRemoteTitles,
                    "Lightless loaded: " + plugin.Sync.LightlessLoaded,
                    "Snowcloak loaded: " + plugin.Sync.SnowcloakLoaded,
                    "PlayerSync loaded: " + plugin.Sync.PlayerSyncLoaded,
                    "Outgoing IPC get calls: " + plugin.Compatibility.LocalGetRequests,
                    "Legacy getter calls: " + plugin.Compatibility.OutgoingLegacyGetCalls,
                    "Extended getter calls: " + plugin.Compatibility.OutgoingExtendedGetCalls,
                    "Last getter JSON hash: " + plugin.Compatibility.LastGetterFingerprint,
                    "Last getter format: " + plugin.Compatibility.LastGetterFormat,
                    "Raw inbound Set IPC calls (no loopback): " + plugin.Compatibility.IncomingRawSetCalls,
                    "Raw inbound Clear IPC calls (no loopback): " + plugin.Compatibility.IncomingRawClearCalls,
                    "Raw inbound with Epithet extension (no loopback): " + plugin.Compatibility.IncomingRawExtendedPayloads,
                    "Outgoing IPC events: " + plugin.Compatibility.LocalPublishEvents,
                    "Outgoing JSON hash: " + plugin.Compatibility.LastPublishedFingerprint,
                    "Publication mode: " + plugin.Compatibility.LastPublicationMode,
                    "Loopback local accepted / rejected: " + plugin.Compatibility.LocalLoopbackAccepted + " / " + plugin.Compatibility.LocalLoopbackRejected,
                    "Loopback local status: " + plugin.Compatibility.LocalLoopbackStatus,
                    "Local-only loopback nameplate passes: " + plugin.Renderer.SyntheticMajorApplyPasses,
                    "Detected sync services (loaded != paired): " + ((plugin.Sync.LightlessLoaded ? 1 : 0) + (plugin.Sync.SnowcloakLoaded ? 1 : 0) + (plugin.Sync.PlayerSyncLoaded ? 1 : 0)),
                    "Incoming IPC accepted: " + plugin.Compatibility.IncomingTitleUpdates,
                    "Incoming IPC rejected: " + plugin.Compatibility.IncomingRejectedUpdates,
                    "Incoming Epithet extensions: " + plugin.Compatibility.IncomingExtendedUpdates,
                    "Incoming JSON hash: " + plugin.Compatibility.LastIncomingFingerprint,
                    "Last receive outcome: " + plugin.Compatibility.LastReceivedOutcome,
                    "Last rejection: " + plugin.Compatibility.LastRejectedReason,
                    "Remote target assignments accepted: " + plugin.Renderer.RemoteAssignmentsAccepted,
                    "Remote target assignments rejected: " + plugin.Renderer.RemoteAssignmentsRejected,
                    "Local major render passes: " + plugin.Renderer.LocalMajorApplyPasses,
                    "Remote major render passes: " + plugin.Renderer.RemoteMajorApplyPasses,
                    "Native animation writer errors: " + plugin.Renderer.NativeTitleErrors,
                    "IMPORTANT: Local counters and matching JSON hashes do not establish visual correctness or confirm pairing."
                });
                ImGui.SetClipboardText(snapshot);
                notice = "Diagnostic snapshot copied (no character names or raw title JSON).";
            }
        });
        ImGui.Spacing();
        Card("##compat_test", 700, () =>
        {
            Caption("MULTIPLAYER SYNC DIAGNOSTICS", "Check the sender's JSON fingerprint against the receiver's after a paired sync.");
            ImGui.Text($"Automatic title announcements: {plugin.Sync.AutomaticAnnouncements}");
            ImGui.Text($"Last automatic announcement (UTC): {plugin.Sync.LastAnnouncementUtc?.ToString("u") ?? "never"}");
            ImGui.Text($"Epithet API local publish events: {plugin.Api.LocalPublishEvents}");
            ImGui.Text($"Epithet API incoming title updates: {plugin.Api.IncomingTitleUpdates}");
            ImGui.TextWrapped(plugin.Api.LastStatus);
            ImGui.Text($"Remote titles: {plugin.Renderer.RemoteTitleCount}");
            ImGui.Text($"Incoming updates: {plugin.Compatibility.IncomingTitleUpdates} (clears: {plugin.Compatibility.IncomingClearEvents})");
            ImGui.Text($"Outgoing IPC events: {plugin.Compatibility.LocalPublishEvents} (forced: {plugin.Compatibility.ForcedLocalPublishEvents})");
            ImGui.Text($"Extended Epithet payloads: {plugin.Compatibility.IncomingExtendedUpdates}");
            ImGui.TextColored(plugin.Compatibility.IncomingExtendedUpdates > 0 ? Mint : Warning,
                plugin.Compatibility.IncomingExtendedUpdates > 0
                    ? "Epithet extension received (check matching title + effect below)"
                    : "NO Epithet extension received yet: exact 3 colors/speed not confirmed");
            ImGui.Text($"Last received format: {plugin.Compatibility.LastIncomingFormat}");
            ImGui.Text($"Last received animation: {plugin.Compatibility.LastIncomingEffect}");
            ImGui.Text($"Local JSON fingerprint: {plugin.Compatibility.LocalFingerprint}");
            ImGui.Text($"Last received JSON fingerprint: {plugin.Compatibility.LastIncomingFingerprint}");
            ImGui.TextWrapped("Compare the SENDER's Last getter JSON hash with the RECEIVER's Last received JSON fingerprint for the same title. A match supports JSON preservation at IPC handoff, NOT proof of network origin or correct nameplate rendering.");
            ImGui.Text($"Active local effect: {plugin.Renderer.ActiveLocalEffect}");
            ImGui.Text($"PostDataUpdate / major callbacks: {plugin.Renderer.DataUpdateCalls} / {plugin.Renderer.MajorUpdateCalls}");
            ImGui.Text($"Animated passes / title writes: {plugin.Renderer.AnimatedPasses} / {plugin.Renderer.AnimatedTitleWrites}");
            ImGui.Text($"Native animated frames: {plugin.Renderer.NativeTitleUpdates} (unchanged: {plugin.Renderer.NativeTitleUnchanged})");
            ImGui.Text($"Native writer errors: {plugin.Renderer.NativeTitleErrors}");
            ImGui.Text($"Anti-flicker mode applied: {plugin.Renderer.AntiFlashApplied} (errors: {plugin.Renderer.AntiFlashErrors})");
            ImGui.Text($"Title edge/glare flags prepared: {plugin.Renderer.OutlineFlagUpdates}");
            ImGui.TextWrapped(plugin.Renderer.NativeAnimationStatus);
            ImGui.Text($"Last animation callback: {plugin.Renderer.LastObservedAnimationSecond} (Unix UTC seconds)");
            ImGui.TextWrapped(plugin.Renderer.LastAnimationPath);
            ColoredWrapped(Warning, "To match 3 colors and exact Rainbow speed, BOTH players need Epithet, with extended JSON received through their sync service.");
            ImGui.Text($"Own / remote writes: {plugin.Renderer.LocalTitleWrites} / {plugin.Renderer.RemoteTitleWrites}");
            ImGui.Text($"Last animation sample: {plugin.Renderer.LastAnimatedColor}");
            ImGui.TextWrapped("Observed regression: 'New Title' was briefly visible only with Honorific also loaded and Epithet renderer enabled. Test with Honorific completely unloaded to isolate simultaneous nameplate writers.");
            ImGui.TextWrapped("Rainbow test: keep this page open for five seconds. DataUpdate / animated writes should rise continuously. If writes rise but the title stays static, capture the Dalamud log and check for another title renderer overwriting the nameplate. If no writes rise, check active character profile, effect, and plugin conflicts.");
            ImGui.TextWrapped("Stock Honorific: solid colors, two-color gradients and its own Rainbow/Wave/Pulse animation. Epithet peers: up to 3 colors and precise Epithet speed, ONLY if the sync service forwards custom extension data. Verify with two clients.");
            if (StyledButton("Run Epithet API self-check")) notice = plugin.Api.RunLocalDiagnostics();
            if (StyledButton("Run sync IPC self-check")) notice = plugin.Compatibility.RunLocalDiagnostics();
            if (StyledButton("Validate selected transport JSON")) notice = plugin.Compatibility.GetTransportSelfCheck();
            if (StyledButton("Publish active title now"))
            {
                plugin.Renderer.RequestRedraw();
                plugin.Sync.RequestPublication();
                notice = "Title publication queued for next framework tick. Check the other player's incoming counters and fingerprint; pairing is still required.";
            }
        });
    }
}
