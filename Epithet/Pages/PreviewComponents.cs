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
    private void DrawPreview(CharacterProfile p)
    {
        var t = p.SelectedTitle;
        var timestamp = (float)((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 3_600_000L) / 1000.0)
                        * Math.Clamp(p.AnimationSpeed, .25f, 6f);
        if (t is { IsPrefix: true })
        {
            DrawTitleText(t, timestamp);
            ImGui.Spacing();
        }
        if (t is not null)
        {
            if (t.UseGlow && ColorUtils.TryParseHex(t.GlowColor, out _))
                ImGui.TextColored(Mint, "Title edge: ON  /  " + t.GlowColor.ToUpperInvariant());
            else
                ImGui.TextColored(Warning, "Title edge: OFF  -  enable it in Color Design");
        }
        // Crucial design constraint: the name ALWAYS uses the neutral preview color;
        // the in-game renderer never writes to h.Name.
        ImGui.TextColored(White, p.CharacterName);
        if (t is { IsPrefix: false })
        {
            ImGui.Spacing();
            DrawTitleText(t, timestamp);
        }
        ImGui.TextColored(Muted, t is { Enabled: false }
            ? "Editing disabled preset (not displayed in game)" : p.WorldName);
    }

    private static void DrawTitleText(TitlePreset t, float seconds)
    {
        ColorUtils.TryParseHex(t.StartColor, out var start);
        ColorUtils.TryParseHex(t.MiddleColor, out var middle);
        ColorUtils.TryParseHex(t.EndColor, out var end);
        var title = "<" + t.Text + ">";
        if (title.Length < 2) return;
        var draw=ImGui.GetWindowDrawList();
        var cursor=ImGui.GetCursorScreenPos();
        var x=0f;
        var height=ImGui.GetTextLineHeight();
        Vector3 edge = Vector3.Zero;
        var hasEdge = t.UseGlow && ColorUtils.TryParseHex(t.GlowColor, out edge);
        for (int i=0;i<title.Length;i++)
        {
            var ch=title[i].ToString();
            var pos=cursor+new Vector2(x,1f);
            var color=ColorUtils.TitleColor(start,middle,end,t.ColorCount,t.Effect,i,title.Length,seconds,t.MiddlePosition,t.StartPosition,t.EndPosition);
            if (hasEdge)
            {
                // Preview a visible title outline. Native FFXIV nameplate text
                // uses SeString EdgeColor; a true blurred bloom is not available here.
                var shade=Ui(new Vector4(edge, 1f));
                // Eight opaque neighboring glyphs create a deliberately
                // legible SHARP native-edge approximation at small font sizes.
                // No fake blur in the preview: the real NamePlate cannot bloom.
                foreach (var offset in new [] { new Vector2(-1.25f,0), new Vector2(1.25f,0),
                    new Vector2(0,-1.25f), new Vector2(0,1.25f),
                    new Vector2(-1,-1), new Vector2(1,-1),
                    new Vector2(-1,1), new Vector2(1,1) })
                    draw.AddText(pos+offset,shade,ch);
            }
            draw.AddText(pos,Ui(new Vector4(color,1f)),ch);
            x+=ImGui.CalcTextSize(ch).X;
        }
        ImGui.Dummy(new Vector2(Math.Max(1,x),height+3));
    }

}