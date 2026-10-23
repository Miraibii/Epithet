using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Epithet.Windows;

public sealed partial class DashboardWindow
{
    // Picker sessions remain local to the editor. Changing the draft does not mutate
    // a profile until Apply/Done; Cancel and clicking outside simply discard it.
    private sealed class ColorPickerSession
    {
        public readonly string OriginalHex;
        public float Hue;
        public float Saturation;
        public float Value;
        public string HexDraft;
        public bool HexInvalid;

        public ColorPickerSession(string original)
        {
            OriginalHex = ColorUtils.TryParseHex(original, out var parsed) ? ColorUtils.ToHex(parsed) : "#FFFFFF";
            ColorUtils.TryParseHex(OriginalHex, out var rgb);
            (Hue, Saturation, Value) = ColorPickerMath.RgbToHsv(rgb);
            HexDraft = OriginalHex;
        }

        public Vector3 Rgb => ColorPickerMath.HsvToRgb(Hue, Saturation, Value);
        public string CurrentHex => ColorUtils.ToHex(Rgb);

        public void SetRgb(Vector3 rgb)
        {
            (Hue, Saturation, Value) = ColorPickerMath.RgbToHsv(rgb);
            HexDraft = ColorUtils.ToHex(rgb);
            HexInvalid = false;
        }

        public void SyncHex()
        {
            HexDraft = CurrentHex;
            HexInvalid = false;
        }
    }

    private readonly Dictionary<string, ColorPickerSession> colorPickers = new();

    private void ColorEditor(string label, string key, ref string hex)
    {
        if (!ColorUtils.TryParseHex(hex, out var rgb)) rgb = Vector3.One;
        ImGui.TextColored(Muted, label.ToUpperInvariant());
        var swatch = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##swatch_" + key, new Vector2(43, 39));
        var hovered = ImGui.IsItemHovered();
        if (ImGui.IsItemClicked())
        {
            colorPickers[key] = new ColorPickerSession(hex);
            ImGui.OpenPopup("##picker_" + key);
        }
        var d = ImGui.GetWindowDrawList();
        d.AddRectFilled(swatch, swatch + new Vector2(43, 39), ImGui.GetColorU32(new Vector4(rgb, 1f)), 9f);
        d.AddRect(swatch, swatch + new Vector2(43, 39), Ui(hovered ? Mint : GoldLine), 9f);
        ImGui.SameLine(0, 9);
        var draft = colorDrafts.TryGetValue(key, out var cur) ? cur : hex;
        var pos = ImGui.GetCursorScreenPos();
        var width = Math.Max(100f, Math.Min(190f, ImGui.GetContentRegionAvail().X));
        d.AddRectFilled(pos, pos + new Vector2(width, 39), Ui(Nav), 8f);
        d.AddRect(pos, pos + new Vector2(width, 39), Ui(GoldLine), 8f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        ImGui.SetNextItemWidth(width);
        try
        {
            if (ImGui.InputText("##hex_" + key, ref draft, 8))
            {
                colorDrafts[key] = draft;
                if (ColorUtils.TryParseHex(draft, out var parsed))
                {
                    hex = ColorUtils.ToHex(parsed);
                    currentPaletteId = "";
                    MarkDirty();
                }
            }
        }
        finally { ImGui.PopStyleVar(); ImGui.PopStyleColor(3); }

        ImGui.PushStyleColor(ImGuiCol.PopupBg, Panel);
        ImGui.PushStyleColor(ImGuiCol.Border, GoldLine);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(11f, 9f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 12f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(7f, 4f));
        try
        {
            if (ImGui.BeginPopup("##picker_" + key, ImGuiWindowFlags.AlwaysAutoResize))
            {
                try
                {
                    if (!colorPickers.TryGetValue(key, out var session))
                    {
                        session = new ColorPickerSession(hex);
                        colorPickers[key] = session;
                    }
                    DrawVisualColorPicker(label, key, session, ref hex);
                }
                finally { ImGui.EndPopup(); }
            }
        }
        finally { ImGui.PopStyleVar(3); ImGui.PopStyleColor(2); }
    }

    private void DrawVisualColorPicker(string label, string key, ColorPickerSession session, ref string hex)
    {
        ImGui.TextColored(White, "PICK COLOR  /  " + label.ToUpperInvariant());
        ImGui.TextColored(Muted, "Drag square / hue strip to select");

        // Separate controls with stable ImGui IDs enable continuous click-and-drag.
        const float square = 160f;
        const float barWidth = 18f;
        DrawSaturationValueSquare("##sv_" + key, session, square);
        ImGui.SameLine(0, 4f);
        DrawVerticalHue("##hue_" + key, session, barWidth, square);
        ImGui.SameLine(0, 9f);
        ImGui.BeginGroup();
        try
        {
            ImGui.TextColored(Muted, "CURRENT");
            DrawPickerSwatch("##current_" + key, session.Rgb, 110f, 27f, true);
            ImGui.TextColored(Muted, "ORIGINAL");
            ColorUtils.TryParseHex(session.OriginalHex, out var original);
            DrawPickerSwatch("##original_" + key, original, 110f, 27f, false);
            ImGui.TextColored(White, session.CurrentHex);
            if (StyledButton("Reset##picker_reset_" + key, new Vector2(110, 27)))
                session.SetRgb(original);
        }
        finally { ImGui.EndGroup(); }

        ImGui.Separator();
        DrawPickerFields(key, session);
        if (session.HexInvalid)
            ImGui.TextColored(Warning, "Enter a valid six-digit HEX color (example: #814728).");
        else
            ImGui.TextColored(Muted, "Apply saves the color · Cancel discards");
        if (StyledButton("Cancel##picker_cancel_" + key, new Vector2(92, 29)))
            ImGui.CloseCurrentPopup();
        ImGui.SameLine();
        if (StyledButton("Apply##picker_apply_" + key, new Vector2(98, 29)))
            ApplyPickerColor(session, key, ref hex);
        ImGui.SameLine();
        if (StyledButton("Done##picker_done_" + key, new Vector2(98, 29)))
        {
            ApplyPickerColor(session, key, ref hex);
            ImGui.CloseCurrentPopup();
        }
    }

    private void ApplyPickerColor(ColorPickerSession session, string key, ref string hex)
    {
        var selected = session.CurrentHex;
        if (selected == hex) return;
        hex = selected;
        colorDrafts[key] = selected;
        currentPaletteId = "";
        MarkDirty();
    }

    private static void DrawSaturationValueSquare(string id, ColorPickerSession session, float size)
    {
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, new Vector2(size, size));
        if (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var mouse = ImGui.GetIO().MousePos;
            session.Saturation = Math.Clamp((mouse.X - origin.X) / size, 0f, 1f);
            session.Value = 1f - Math.Clamp((mouse.Y - origin.Y) / size, 0f, 1f);
            session.SyncHex();
        }

        var d = ImGui.GetWindowDrawList();
        const int rows = 64;
        var hueRgb = ColorUtils.Hue(session.Hue);
        // Strip interpolation avoids a diagonal split from a two-triangle ImGui quad.
        for (var i = 0; i < rows; i++)
        {
            var y0 = (float)i / rows;
            var y1 = (float)(i + 1) / rows;
            var white0 = new Vector4(new Vector3(1f - y0), 1f);
            var white1 = new Vector4(new Vector3(1f - y1), 1f);
            var hue0 = new Vector4(hueRgb * (1f - y0), 1f);
            var hue1 = new Vector4(hueRgb * (1f - y1), 1f);
            d.AddRectFilledMultiColor(origin + new Vector2(0, size * y0),
                origin + new Vector2(size, size * y1),
                ImGui.GetColorU32(white0), ImGui.GetColorU32(hue0),
                ImGui.GetColorU32(hue1), ImGui.GetColorU32(white1));
        }
        d.AddRect(origin, origin + new Vector2(size, size), Ui(GoldLine), 5f, 0, 1.5f);
        var marker = origin + new Vector2(session.Saturation * size, (1f - session.Value) * size);
        d.AddCircleFilled(marker, 8f, ImGui.GetColorU32(new Vector4(0, 0, 0, .8f)), 24);
        d.AddCircle(marker, 7.2f, Ui(White), 28, 2.3f);
    }

    private static void DrawVerticalHue(string id, ColorPickerSession session, float width, float height)
    {
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, new Vector2(width, height));
        if (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            session.Hue = Math.Clamp((ImGui.GetIO().MousePos.Y - origin.Y) / height, 0f, .999999f);
            session.SyncHex();
        }
        var d = ImGui.GetWindowDrawList();
        const int bands = 72;
        for (var i = 0; i < bands; i++)
        {
            var top = ColorUtils.Hue((float)i / bands);
            var bottom = ColorUtils.Hue((float)(i + 1) / bands);
            d.AddRectFilledMultiColor(origin + new Vector2(0, height * i / bands),
                origin + new Vector2(width, height * (i + 1) / bands),
                ImGui.GetColorU32(new Vector4(top, 1f)), ImGui.GetColorU32(new Vector4(top, 1f)),
                ImGui.GetColorU32(new Vector4(bottom, 1f)), ImGui.GetColorU32(new Vector4(bottom, 1f)));
        }
        d.AddRect(origin, origin + new Vector2(width, height), Ui(GoldLine), 4f, 0, 1.5f);
        var y = origin.Y + session.Hue * height;
        d.AddRectFilled(new Vector2(origin.X - 3f, y - 2.5f),
            new Vector2(origin.X + width + 3f, y + 2.5f), Ui(White), 1.5f);
    }

    private static void DrawPickerSwatch(string id, Vector3 rgb, float width, float height, bool active)
    {
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new Vector2(width, height));
        var d = ImGui.GetWindowDrawList();
        d.AddRectFilled(origin, origin + new Vector2(width, height), ImGui.GetColorU32(new Vector4(rgb, 1f)), 7f);
        d.AddRect(origin, origin + new Vector2(width, height), Ui(active ? Mint : GoldLine), 7f, 0, active ? 2f : 1f);
    }

    // Active ImGui DragInt widgets capture the mouse; the outer window has no
    // implicit body-drag gesture. Double-click / Ctrl-click supports typed input.
    private static bool DragColorChannel(string id, string label, ref int value, int max)
    {
        ImGui.TextColored(Muted, label);
        ImGui.SameLine(0, 4f);
        ImGui.SetNextItemWidth(45f);
        return ImGui.DragInt(id, ref value, max == 359 ? .35f : .25f, 0, max);
    }

    private static void DrawPickerFields(string key, ColorPickerSession session)
    {
        var rgb = session.Rgb;
        var r = (int)MathF.Round(rgb.X * 255f);
        var g = (int)MathF.Round(rgb.Y * 255f);
        var b = (int)MathF.Round(rgb.Z * 255f);
        var h = (int)MathF.Round(session.Hue * 360f) % 360;
        var s = (int)MathF.Round(session.Saturation * 100f);
        var v = (int)MathF.Round(session.Value * 100f);
        var rgbChanged = false;
        var hsvChanged = false;

        ImGui.BeginGroup();
        try
        {
            ImGui.TextColored(Mint, "RGB  /  DRAG");
            rgbChanged |= DragColorChannel("##picker_r_" + key, "R", ref r, 255);
            ImGui.SameLine(0, 4f);
            rgbChanged |= DragColorChannel("##picker_g_" + key, "G", ref g, 255);
            ImGui.SameLine(0, 4f);
            rgbChanged |= DragColorChannel("##picker_b_" + key, "B", ref b, 255);
        }
        finally { ImGui.EndGroup(); }
        ImGui.SameLine(0, 12f);
        ImGui.BeginGroup();
        try
        {
            ImGui.TextColored(Mint, "HSV  /  DRAG");
            hsvChanged |= DragColorChannel("##picker_h_" + key, "H", ref h, 359);
            ImGui.SameLine(0, 4f);
            hsvChanged |= DragColorChannel("##picker_s_" + key, "S", ref s, 100);
            ImGui.SameLine(0, 4f);
            hsvChanged |= DragColorChannel("##picker_v_" + key, "V", ref v, 100);
        }
        finally { ImGui.EndGroup(); }

        if (rgbChanged)
            session.SetRgb(new Vector3(Math.Clamp(r, 0, 255) / 255f,
                Math.Clamp(g, 0, 255) / 255f, Math.Clamp(b, 0, 255) / 255f));
        else if (hsvChanged)
        {
            session.Hue = ((h % 360) + 360) % 360 / 360f;
            session.Saturation = Math.Clamp(s, 0, 100) / 100f;
            session.Value = Math.Clamp(v, 0, 100) / 100f;
            session.SyncHex();
        }
        ImGui.TextColored(Mint, "HEX   ·   double-click to type");
        ImGui.SetNextItemWidth(155f);
        if (ImGui.InputText("##picker_hex_" + key, ref session.HexDraft, 8))
        {
            if (ColorUtils.TryParseHex(session.HexDraft, out var parsed)) session.SetRgb(parsed);
            else session.HexInvalid = true;
        }
        ImGui.SameLine(0, 9);
        if (StyledButton("Copy HEX##copy_picker_" + key, new Vector2(93, 28)))
            ImGui.SetClipboardText(session.CurrentHex);
    }

}
