using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Epithet.UI;

namespace Epithet.Windows;

/// <summary>A real second, compact window instead of shrinking the dashboard layout to 64px.</summary>
public sealed class MinimizedLauncher : Window
{
    private readonly Plugin owner;
    private bool movedDuringPress;
    public MinimizedLauncher(Plugin plugin) : base("Epithet compact###EpithetMinimized")
    {
        owner = plugin;
        IsOpen = false;
        Size = new Vector2(228, 58);
        SizeCondition = ImGuiCond.Always;
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoDocking |
                ImGuiWindowFlags.NoBackground;
        ShowCloseButton = false;
        AllowPinning = false;
    }

    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 12f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(3);
    }

    public override void Draw() => DrawCompactSurface();

    private void DrawCompactSurface()
    {
        var p = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var d = ImGui.GetWindowDrawList();
        // The graphic is inset 1px and sized from the real window, not a
        // hard-coded 220x51 rectangle that can protrude past the window bounds.
        d.AddRectFilled(p + Vector2.One, p + size - Vector2.One,
            ImGui.GetColorU32(new Vector4(.065f,.059f,.052f,1)), 11f);
        d.AddRect(p + new Vector2(1f,1f), p + size - new Vector2(1f,1f),
            ImGui.GetColorU32(new Vector4(.8f,.61f,.26f,.7f)), 11f);
        StudioAssets.Draw("Brand", p + new Vector2(8,8), new Vector2(40,40));
        d.AddText(p + new Vector2(54,20), ImGui.GetColorU32(new Vector4(.98f,.89f,.72f,1)), "EPITHET");
        // Drag zone occupies the logo/title; restore has its own small button.
        // Using IsItemClicked on a full-width surface would restore on mouse-down
        // and make dragging impossible.
        ImGui.SetCursorPos(new Vector2(4, 4));
        ImGui.InvisibleButton("##ep_minimized_drag", new Vector2(size.X - 51f, size.Y - 8f));
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            movedDuringPress = true;
            var next = ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta;
            // Avoid shifting an already-submitted draw list mid-frame.
            Position = next;
            PositionCondition = ImGuiCond.Always;
        }
        if (ImGui.IsItemDeactivated())
        {
            if (!movedDuringPress) owner.RestoreUi();
            movedDuringPress = false;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("Drag to move · use arrow to restore");
            ImGui.EndTooltip();
        }
        var restorePos = p + new Vector2(size.X - 41f, 12f);
        ImGui.SetCursorPos(new Vector2(size.X - 44f, 9f));
        ImGui.InvisibleButton("##restore_epithet", new Vector2(34, 37));
        var restoreHover = ImGui.IsItemHovered();
        d.AddRectFilled(restorePos, restorePos + new Vector2(28, 31),
            ImGui.GetColorU32(restoreHover ? new Vector4(.25f, .18f, .10f, 1f) : new Vector4(.20f, .16f, .10f, 1f)), 8f);
        d.AddRect(restorePos, restorePos + new Vector2(28, 31),
            ImGui.GetColorU32(new Vector4(.78f, .60f, .25f, .68f)), 8f);
        // Draw vector icon in absolute button coordinates, no font/"+" baseline offset.
        // One small overlapping-window outline + compact expansion cross.
        var icon = ImGui.GetColorU32(restoreHover
            ? new Vector4(.98f, .92f, .80f, 1f)
            : new Vector4(.94f, .86f, .70f, 1f));
        d.AddRect(restorePos + new Vector2(6f, 7f), restorePos + new Vector2(18f, 19f), icon, 3f);
        var glyphCenter = restorePos + new Vector2(18.5f, 19.5f);
        d.AddLine(glyphCenter + new Vector2(-3.5f, 0f), glyphCenter + new Vector2(3.5f, 0f), icon, 1.8f);
        d.AddLine(glyphCenter + new Vector2(0f, -3.5f), glyphCenter + new Vector2(0f, 3.5f), icon, 1.8f);
        if (restoreHover) { ImGui.BeginTooltip(); ImGui.Text("Restore Epithet Studio"); ImGui.EndTooltip(); }
        if (ImGui.IsItemClicked()) owner.RestoreUi();
    }

}
