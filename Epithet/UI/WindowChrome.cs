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
    private static uint Ui(Vector4 c) => ImGui.GetColorU32(new Vector4(c.X,c.Y,c.Z,c.W * PageVisualAlpha));

    private void DrawWindowBackdrop(Vector2 pos, Vector2 size)
    {
        var draw = ImGui.GetWindowDrawList();
        var time = cfg.ReduceUiMotion ? 0f : (float)ImGui.GetTime();
        draw.AddRectFilled(pos, pos + size, Ui(Bg), 13f);
        // Keep the entire fill inside the rounded outer silhouette: a full
        // rect-filled multicolor quad would paint square pixels at the corners.
        draw.AddRectFilled(pos + new Vector2(2f, 2f), pos + size - new Vector2(2f, 2f),
            Ui(new Vector4(.067f, .065f, .070f, 1f)), 11f);
        // Independent, slowly drifting ambient orbs from the reference video.
        // Pure draw-list shapes: no textures, no network dependencies, no input capture.
        var orbs = new []
        {
            (new Vector2(.18f,.78f), .32f, new Vector4(.48f,.34f,.11f,.04f), 0f),
            (new Vector2(.75f,.31f), .30f, new Vector4(.28f,.26f,.42f,.045f), 1.9f),
            (new Vector2(.86f,.88f), .22f, new Vector4(.17f,.39f,.33f,.025f), 3.2f),
        };
        // ImGui's drawlist primitives do not clip to a rounded mask. Confine
        // every decorative circle to a safe interior rectangle so no ambient
        // pixel can extend beyond the rounded external frame.
        draw.PushClipRect(pos + new Vector2(15f, 71f), pos + size - new Vector2(15f, 37f), true);
        try
        {
            foreach (var (anchor, radius, color, phase) in orbs)
            {
                var p = pos + new Vector2(size.X * anchor.X + MathF.Sin(time * .17f + phase) * 26f,
                    size.Y * anchor.Y + MathF.Cos(time * .11f + phase) * 20f);
                var r = MathF.Min(size.X,size.Y) * radius;
                for (int layer=9; layer>=1; layer--)
                {
                    var alpha = color.W * (10-layer) * .047f;
                    draw.AddCircleFilled(p, r * layer / 9f, Ui(new Vector4(color.X,color.Y,color.Z,alpha)), 60);
                }
                draw.AddCircle(p, r*.85f, Ui(new Vector4(color.X,color.Y,color.Z,.032f)), 60, .8f);
            }
            for (var i = 0; i < 7; i++)
            {
                var drift = cfg.ReduceUiMotion ? i / 7f : PingPong01(time * (.05f + i * .007f) + i * .23f);
                var rise = cfg.ReduceUiMotion ? .25f : PingPong01(time * (.07f + i * .006f) + i * .41f);
                var p = pos + new Vector2(36f + drift * (size.X - 72f), 86f + rise * (size.Y - 164f));
                var scale = 6f + (cfg.ReduceUiMotion ? .8f : (MathF.Sin(time * 1.2f + i) + 1.4f) * 2.3f);
                DrawMiniTriangle(draw, p, scale, time * (.52f + i * .03f), new Vector4(.92f, .74f, .34f, .05f + i * .006f));
            }
        }
        finally { draw.PopClipRect(); }
        // Inset decorations and footer respect the main frame's curvature.
        draw.AddLine(pos + new Vector2(14f, size.Y - 34f),
            pos + new Vector2(size.X - 14f, size.Y - 34f), Ui(GoldLine), 1f);
        draw.AddRect(pos + new Vector2(.5f,.5f), pos + size-new Vector2(.5f,.5f), Ui(GoldLine), 14f);
    }

    private void DrawCustomHeader(Vector2 size)
    {
        var draw = ImGui.GetWindowDrawList();
        var p = ImGui.GetWindowPos();
        // Inset header fill so it cannot square off the outer top corners.
        draw.AddRectFilled(p + new Vector2(2f,2f), p + new Vector2(size.X - 2f,57f),
            Ui(new Vector4(.14f,.12f,.095f,1f)), 12f);
        draw.AddLine(p+new Vector2(0,58),p+new Vector2(size.X,58),Ui(GoldLine),1.3f);
        var center = p+new Vector2(29,29);
        draw.AddCircleFilled(center,18,Ui(DeepGold),40);
        draw.AddCircle(center,18,Ui(Mint),40,1.1f);
        if (!StudioAssets.Draw("Brand", center - new Vector2(22,22), new Vector2(44,44)))
            draw.AddText(center-new Vector2(6.5f,8),Ui(White),"✦");
        draw.AddText(p+new Vector2(58,10),Ui(White),"EPITHET");
        draw.AddText(p+new Vector2(58,30),Ui(Muted),"Epithet Title Studio");
        if(size.X>760)
        {
            var chip=p+new Vector2(size.X-336,15);
            draw.AddRectFilled(chip,chip+new Vector2(186,29),Ui(new Vector4(.24f,.065f,.075f,.98f)),15f);
            draw.AddRect(chip,chip+new Vector2(186,29),Ui(new Vector4(.77f,.19f,.22f,.70f)),15f);
            draw.AddCircleFilled(chip+new Vector2(12,14),4,Ui(Warning),24);
            draw.AddText(chip+new Vector2(23,6),Ui(Warning),"FREE EDITION");
        }
        // Minimize opens a separate 220px launcher. The dashboard never collapses its layout.
        ImGui.SetCursorPos(new Vector2(6,7));
        ImGui.InvisibleButton("##ep_title_drag",new Vector2(Math.Max(60,size.X-121),44));
        if(ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            // Apply via the Dalamud Window host on the NEXT ImGui.Begin.
            // Never change the actual window origin midway through Draw():
            // the already-painted backdrop would lag behind the widgets.
            Position = ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta;
            PositionCondition = ImGuiCond.Always;
        }
        DrawHeaderAction("##ep_minimize",size.X-96,false,()=>plugin.MinimizeUi());
        DrawHeaderAction("##ep_close",size.X-48,true,()=>plugin.CloseUi());
    }

    private static void DrawHeaderAction(string id, float offsetX, bool closeAction, Action click)
    {
        ImGui.SetCursorPos(new Vector2(offsetX, 12));
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(36, 36);
        ImGui.InvisibleButton(id, size);
        var hover = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var draw = ImGui.GetWindowDrawList();
        var fill = closeAction
            ? (hover ? new Vector4(.34f, .10f, .10f, 1f) : new Vector4(.20f, .10f, .09f, 1f))
            : (hover ? Selected : new Vector4(.18f, .15f, .10f, 1f));
        var stroke = closeAction ? new Vector4(.88f, .32f, .32f, .78f) : GoldLine;
        var ink = closeAction ? Warning : (hover ? White : new Vector4(.94f, .88f, .74f, 1f));
        draw.AddRectFilled(pos, pos + size, Ui(fill), 9f);
        draw.AddRect(pos, pos + size, Ui(stroke), 9f);
        var center = pos + size * .5f;
        if (closeAction)
        {
            draw.AddLine(center + new Vector2(-6.5f, -6.5f), center + new Vector2(6.5f, 6.5f), Ui(ink), active ? 2.2f : 2f);
            draw.AddLine(center + new Vector2(6.5f, -6.5f), center + new Vector2(-6.5f, 6.5f), Ui(ink), active ? 2.2f : 2f);
        }
        else
        {
            draw.AddLine(center + new Vector2(-7f, 0f), center + new Vector2(7f, 0f), Ui(ink), active ? 2.6f : 2.2f);
        }
        if (ImGui.IsItemClicked()) click();
    }

    private void DrawCornerResizeGrip(Vector2 size)
    {
        // Native left/right/top border resize is disabled. Only this small
        // bottom-right hit target can resize; its mouse delta cannot move the
        // whole window and dimensions stay within the visible viewport.
        const float grip = 27f;
        // This lives in the 39px footer, never in the scrolling content.
        ImGui.SetCursorPos(new Vector2(size.X - grip - 12f, size.Y - grip - 6f));
        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##ep_safe_resize", new Vector2(grip, grip));
        var draw = ImGui.GetWindowDrawList();
        var hot = ImGui.IsItemHovered() || ImGui.IsItemActive();
        draw.AddRectFilled(pos, pos + new Vector2(grip, grip),
            Ui(hot ? Selected : Surface2), 7f);
        draw.AddRect(pos, pos + new Vector2(grip, grip), Ui(hot ? Mint : GoldLine), 7f);
        var ink = Ui(hot ? Mint : White);
        draw.AddLine(pos + new Vector2(20, 7), pos + new Vector2(7, 20), ink, 1.7f);
        draw.AddLine(pos + new Vector2(20, 14), pos + new Vector2(14, 20), ink, 1.7f);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("Drag to resize Epithet (bottom-right corner only)");
            ImGui.EndTooltip();
        }
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var io = ImGui.GetIO();
            // A very small viewport should never produce a max < min clamp.
            var maxWidth = MathF.Max(850f, MathF.Min(1920f, io.DisplaySize.X - ImGui.GetWindowPos().X - 12f));
            var maxHeight = MathF.Max(630f, MathF.Min(1200f, io.DisplaySize.Y - ImGui.GetWindowPos().Y - 12f));
            var next = new Vector2(
                Math.Clamp(size.X + io.MouseDelta.X, 850f, maxWidth),
                Math.Clamp(size.Y + io.MouseDelta.Y, 630f, maxHeight));
            // Persist only dimensions, never mutate the drag position here.
            // Defer window-size changes for the same reason as window moves.
            // Background, child panels and resize handle use one size per frame.
            Size = next;
            SizeCondition = ImGuiCond.Always;
        }
    }

    private static void Child(string id, Vector2 size, Vector4 bg, Action content, bool border = true, bool padded = false)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, bg);
        try
        {
            var visible = ImGui.BeginChild(id, size, border,
                padded ? ImGuiWindowFlags.AlwaysUseWindowPadding : ImGuiWindowFlags.None);
            try { if (visible) content(); }
            finally { ImGui.EndChild(); }
        }
        finally { ImGui.PopStyleColor(); }
    }
}
