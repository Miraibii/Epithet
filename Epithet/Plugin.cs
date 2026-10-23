using System;
using System.Linq;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Epithet.Interop;
using Epithet.Models;
using Epithet.Rendering;
using Epithet.Windows;

namespace Epithet;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static ICondition Conditions { get; private set; } = null!;
    [PluginService] internal static INamePlateGui NamePlates { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static ITextureProvider Textures { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public static Configuration CurrentConfig { get; private set; } = null!;
    private readonly WindowSystem windows = new("Epithet");
    private readonly DashboardWindow dashboard;
    private readonly MinimizedLauncher minimized;
    public Configuration Config { get; }
    public HonorificBridge Compatibility { get; }
    public HonorificBridge Bridge => Compatibility;
    public EpithetApi Api { get; }
    public SyncIntegration Sync { get; }
    public NameplateRenderer Renderer { get; }
    public CharacterProfile? ActiveLocalProfile
    {
        get
        {
            var player = Objects.LocalPlayer;
            if (player is null) return null;
            // The world numeric ID is used to distinguish same-name characters across worlds.
            var found = Config.Profiles?.Where(x => x.Enabled
                && x.HomeWorldId == player.HomeWorld.RowId
                && x.CharacterName.Equals(player.Name.TextValue, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Titles?.Count ?? 0).FirstOrDefault();
            return found ?? (!Config.ApplyProfilesAutomatically && Config.Profiles is { Count: > 0 } && Config.SelectedProfile.Enabled ? Config.SelectedProfile : null);
        }
    }

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        CurrentConfig = Config;
        Config.Normalize();
        Renderer = new NameplateRenderer(this, NamePlates, Framework, AddonLifecycle);
        Api = new EpithetApi(PluginInterface, Renderer);
        Compatibility = new HonorificBridge(PluginInterface, Renderer);
        Sync = new SyncIntegration(PluginInterface, Compatibility, Api, Renderer);
        dashboard = new DashboardWindow(this);
        minimized = new MinimizedLauncher(this);
        windows.AddWindow(dashboard);
        windows.AddWindow(minimized);
        Commands.AddHandler("/epithet", new CommandInfo((_, args) =>
        {
            if (args.Trim().Equals("refresh", StringComparison.OrdinalIgnoreCase))
            {
                // Queue redraw/publication; player and object-table data is read
                // only from NameplateRenderer.OnFrameworkUpdate.
                Renderer.RequestRedraw();
                Sync.RequestPublication();
            }
            else Toggle();
        }) { HelpMessage = "Open Epithet (or /epithet refresh to update nameplates)." });
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += Toggle;
        // Last constructor step: all plugin dependencies are now valid.
        // Player data and initial IPC announcements run on framework.Update.
        Renderer.Start();
    }

    public void Toggle()
    {
        if (minimized.IsOpen) { RestoreUi(); return; }
        dashboard.IsOpen = !dashboard.IsOpen;
    }

    public void MinimizeUi()
    {
        dashboard.IsOpen = false;
        minimized.IsOpen = true;
    }

    public void RestoreUi()
    {
        minimized.IsOpen = false;
        dashboard.IsOpen = true;
    }

    public void CloseUi()
    {
        minimized.IsOpen = false;
        dashboard.IsOpen = false;
    }

    public void SaveAndRefresh()
    {
        Config.Save();
        // GUI callbacks are not guaranteed to run on the game framework thread.
        // Deferring these operations avoids ObjectTable.LocalPlayer exceptions.
        Renderer.RequestRedraw();
        Sync.RequestPublication();
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= Toggle;
        Commands.RemoveHandler("/epithet");
        Compatibility.Dispose();
        Api.Dispose();
        Renderer.Dispose();
        windows.RemoveAllWindows();
        Config.Save();
    }
}
