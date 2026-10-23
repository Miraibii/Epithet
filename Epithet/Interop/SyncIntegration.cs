using System;
using System.Linq;
using Dalamud.Plugin;
using Epithet.Rendering;

namespace Epithet.Interop;

/// <summary>
/// Discovers loaded sync clients and announces Epithet's local Honorific-compatible
/// title endpoint. Lightless is known to consume Honorific 3.x IPC. Snowcloak and
/// PlayerSync are detected for diagnostics; they only carry Epithet titles if their
/// installed versions actually relay the Honorific title JSON unchanged.
///
/// This is NOT a direct connection to their private servers or a pairing bypass.
/// The sync client owns networking, pairing, consent and connection state.
/// </summary>
public sealed class SyncIntegration
{
    // Pairing and remote transport are managed by that plugin, not Epithet.
    private readonly IDalamudPluginInterface pi;
    private readonly HonorificBridge bridge;
    private readonly EpithetApi api;
    private readonly NameplateRenderer renderer;
    private DateTime lastScanUtc = DateTime.MinValue;
    private DateTime lastAnnounceUtc = DateTime.MinValue;
    private volatile bool publicationRequested;

    public bool LightlessLoaded { get; private set; }
    public bool SnowcloakLoaded { get; private set; }
    public bool PlayerSyncLoaded { get; private set; }
    public long AutomaticAnnouncements { get; private set; }
    public DateTime? LastAnnouncementUtc { get; private set; }
    public string DetectionStatus { get; private set; } = "Waiting for installed plugin scan";

    public bool AnySyncClientLoaded => LightlessLoaded || SnowcloakLoaded || PlayerSyncLoaded;

    public SyncIntegration(IDalamudPluginInterface pluginInterface, HonorificBridge compatibility,
        EpithetApi epithetApi, NameplateRenderer nameplateRenderer)
    {
        pi = pluginInterface;
        bridge = compatibility;
        api = epithetApi;
        renderer = nameplateRenderer;
        // IMPORTANT: Dalamud constructs plugins asynchronously, potentially off
        // the main thread. Pulse/GetLocalJson must only run from framework.Update.
        // NameplateRenderer.OnFrameworkUpdate starts the scan after initialization.
    }

    /// <summary>Queue user-requested reannouncements for the framework thread.</summary>
    public void RequestPublication() => publicationRequested = true;

    private static string Canonical(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// Lightweight 3-second scan, with a low-frequency fallback reannouncement
    /// for a sync service that reconnects without reloading its Dalamud plugin.
    /// It cannot assert remote delivery or pair status.
    /// </summary>
    public void Pulse(DateTime nowUtc, bool territoryChanged = false)
    {
        if (!territoryChanged && !publicationRequested
            && nowUtc - lastScanUtc < TimeSpan.FromSeconds(3)) return;
        lastScanUtc = nowUtc;

        var wasLightless = LightlessLoaded;
        var wasSnowcloak = SnowcloakLoaded;
        var wasPlayerSync = PlayerSyncLoaded;
        try
        {
            var loaded = pi.InstalledPlugins.Where(p => p.IsLoaded)
                .Select(p => Canonical(p.InternalName + " " + p.Name)).ToArray();
            LightlessLoaded = loaded.Any(n => n.Contains("lightless", StringComparison.Ordinal));
            SnowcloakLoaded = loaded.Any(n => n.Contains("snowcloak", StringComparison.Ordinal));
            PlayerSyncLoaded = loaded.Any(n => n.Contains("playersync", StringComparison.Ordinal));
            var count = (LightlessLoaded ? 1 : 0) + (SnowcloakLoaded ? 1 : 0) + (PlayerSyncLoaded ? 1 : 0);
            DetectionStatus = count > 1
                ? "Multiple sync clients loaded. For a controlled transport test use ONLY ONE shared sync service on BOTH PCs; loaded does not mean paired."
                : count == 1
                    ? "Sync plugin detected. Verify BOTH players have an active pair and title sync is enabled in that same service."
                    : "No supported sync client is currently loaded.";
        }
        catch (Exception ex)
        {
            DetectionStatus = "Unable to read installed sync plugins: " + ex.GetType().Name;
            return;
        }

        // Reconcile provider ownership after a plugin reload or a user toggle.
        // Never replace a provider registered by another plugin.
        bridge.RefreshEnabledState();
        if (!Plugin.CurrentConfig.EnableHonorificCompatibility)
        {
            publicationRequested = false;
            return;
        }

        // Manual reannouncement may be requested even when no transport is loaded.
        if (!AnySyncClientLoaded && !publicationRequested) return;
        var newlyLoaded = (LightlessLoaded && !wasLightless)
            || (SnowcloakLoaded && !wasSnowcloak)
            || (PlayerSyncLoaded && !wasPlayerSync);
        var dueForRetry = nowUtc - lastAnnounceUtc >= TimeSpan.FromMinutes(2);
        if (!newlyLoaded && !territoryChanged && !dueForRetry && !publicationRequested) return;
        publicationRequested = false;

        // Honorific.Ready is a notification, not a network connection. It helps
        // consumers that subscribe after Epithet initialized discover the API.
        // Re-check availability on manual refresh, zone changes and retry too.
        // A sync plugin may have reconnected without reloading its DLL.
        bridge.AnnounceReady();
        PublishNow(nowUtc);
    }

    // Only called from Pulse -> NameplateRenderer.OnFrameworkUpdate (game main thread).
    private void PublishNow(DateTime nowUtc)
    {
        if (!Plugin.CurrentConfig.EnableHonorificCompatibility) return;
        // No user title: publish the empty string to clear stale remote titles.
        bridge.NotifyLocalChange(renderer.GetLocalJson(), force: true);
        api.NotifyLocalChange(renderer.GetLocalEpithetJson(), force: true);
        lastAnnounceUtc = nowUtc;
        LastAnnouncementUtc = nowUtc;
        AutomaticAnnouncements++;
    }
}
