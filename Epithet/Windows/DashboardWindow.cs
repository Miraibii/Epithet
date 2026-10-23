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

/// <summary>
/// Dedicated Epithet dashboard. Every ImGui style/child push is balanced in finally;
/// no changes are made to the game's global ImGui style or player nameplate text.
/// </summary>
public sealed partial class DashboardWindow : Window
{
    private static readonly string[] Pages = { "Overview", "Title Library", "Colors & Motion", "Characters", "Compatibility", "Local Backups" };
    private static readonly string[] Effects = { "Static", "Gradient", "Rainbow", "Pulse", "Wave", "Shift" };
    private readonly Plugin plugin;
    private readonly Configuration cfg;
    private readonly Dictionary<string, string> colorDrafts = new();
    private int page;
    private bool pendingSave, confirmDeleteAll, confirmDeleteWanderer;
    private DateTime lastChange;
    private string notice = "Free Edition active · More features coming soon.";

    private int drawnPage = -1;
    private float pageTransition = 1f;
    private bool showAdvancedConditions;
    private readonly float[] navHover = new float[6];
    private string paletteNameDraft = "My colors";
    private string currentPaletteId = "";
    private string draggingGradientTitleId = "";
    private string profileSearch = "";
    private string pendingPaletteDeleteId = "";
    private BackupBundle? pendingFullRestore;

    public DashboardWindow(Plugin owner) : base("Epithet###EpithetMain")
    {
        plugin = owner;
        cfg = owner.Config;
        Size = new Vector2(1100, 760);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            // ImGui handles resizing on every border and corner with native cursors.
            MinimumSize = new Vector2(850, 630),
            MaximumSize = new Vector2(1920, 1200)
        };
        // Hiding only the native title bar leaves native resize grips and resize
        // cursors enabled on EVERY edge. The header/actions are fully custom.
        // Disable native side-border resizing: on some clients the left edge could
        // expand the window indefinitely. The dedicated bottom-right grip below
        // has a clamped, predictable size and never moves the window.
        Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
                ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoBackground;
        ShowCloseButton = false;
        AllowPinning = false;
        AllowClickthrough = false;
    }

    // WindowHost calls PreDraw BEFORE ImGui.Begin. Styling the outer frame in
    // Draw() is too late: ImGui has already rendered its square native border.
    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 14f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(3);
    }

    public override void Draw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(12, 9));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(12, 10));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 14f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 12f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 12f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 12f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 12f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Bg);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Bg);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(.085f, .077f, .063f, 1));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Selected);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Selected);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.22f, .175f, .10f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Selected);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(.40f, .29f, .13f, 1));
        ImGui.PushStyleColor(ImGuiCol.Header, Selected);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Selected);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Selected);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Mint);
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(.31f, .25f, .15f, 1));
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Mint);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, White);
        try
        {
            var size = ImGui.GetWindowSize();
            var windowPos = ImGui.GetWindowPos();
            ReduceHeroMotion = cfg.ReduceUiMotion;
            DrawWindowBackdrop(windowPos, size);
            DrawCustomHeader(size);
            // Keep all child windows strictly inside the outer rounded frame.
            // The root is chrome only, never scrollable: otherwise the invisible
            // resize control generates an EXTRA scrollbar outside the border.
            const float bodyTop = 70f;
            const float footer = 39f;
            const float sideInset = 13f;
            const float navWidth = 66f;
            const float navGap = 12f;
            var usableHeight = Math.Max(50f, size.Y - bodyTop - footer);
            ImGui.SetCursorPos(new Vector2(sideInset, bodyTop));
            Child("##ep_nav", new Vector2(navWidth, usableHeight), Nav, DrawNav, false);
            ImGui.SetCursorPos(new Vector2(sideInset + navWidth + navGap, bodyTop));
            var mainWidth = Math.Max(120f, size.X - (2f * sideInset + navWidth + navGap));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(16f, 13f));
            try { Child("##ep_content", new Vector2(mainWidth, usableHeight), Vector4.Zero, DrawContent, false, true); }
            finally { ImGui.PopStyleVar(); }
            // Bottom footer is reserved for resizing; the grip is never covered
            // by a child panel, and it can be dragged without moving the window.
            DrawCornerResizeGrip(size);
        }
        finally
        {
            ImGui.PopStyleColor(15);
            ImGui.PopStyleVar(11);
        }
        if (pendingSave && DateTime.UtcNow - lastChange > TimeSpan.FromMilliseconds(350)) SaveNow(false);
    }

    public override void OnClose()
    {
        if (pendingSave) SaveNow(false);
        base.OnClose();
    }
}
