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
    // Colored ImGui.TextColored does not wrap on its own. Keep long explanatory
    // text inside cards, even when the dashboard is at minimum width.
    private static void ColoredWrapped(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        try { ImGui.TextWrapped(text); }
        finally { ImGui.PopStyleColor(); }
    }

    // Gold-lined cards; ImGui children own clipping and interaction, the drawlist only paints.
    private static void Card(string id, float height, Action content)
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = Math.Max(100f,ImGui.GetContentRegionAvail().X);
        var d = ImGui.GetWindowDrawList();
        d.AddRectFilled(pos+new Vector2(0,3),pos+new Vector2(width,height+3),Ui(new Vector4(0,0,0,.23f)),12f);
        d.AddRectFilled(pos,pos+new Vector2(width,height),Ui(new Vector4(.096f,.085f,.071f,.98f)),12f);
        // Rounded inset accent avoids painting square corners over the card.
        d.AddRectFilled(pos + new Vector2(2f,2f), pos + new Vector2(width-2f,76f),
            Ui(new Vector4(.20f,.16f,.10f,.27f)),10f);
        d.AddRect(pos,pos+new Vector2(width,height),Ui(GoldLine),12f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,new Vector2(19,16));
        try { Child(id,new Vector2(0,height),Vector4.Zero,content,false,true); }
        finally { ImGui.PopStyleVar(); }
    }

    private static bool ReduceHeroMotion;

    private static void Hero(string kicker, string headline, string description, string glyph, float height = 156f)
    {
        var pos = ImGui.GetCursorScreenPos();
        var w = Math.Max(180f, ImGui.GetContentRegionAvail().X);
        var d = ImGui.GetWindowDrawList();
        var max = pos + new Vector2(w, height);
        d.AddRectFilled(pos, max, Ui(new Vector4(.15f,.136f,.108f,1)), 14f);
        d.AddRectFilled(pos + new Vector2(2f,2f), max - new Vector2(2f,2f),
            Ui(new Vector4(.22f,.18f,.13f,.72f)), 12f);

        // DVD-logo behaviour: independent ping-pong reflection against ALL four
        // card edges, not a diagonal lerp and not a slow circle around one point.
        // Draw the decoration BEFORE text, at low opacity, clipped inside the card.
        const float radius = 36f;
        var time = (float)ImGui.GetTime();
        var xTravel = Math.Max(0f, w - radius * 2f - 24f);
        var yTravel = Math.Max(0f, height - radius * 2f - 20f);
        var x = 12f + radius + xTravel * (ReduceHeroMotion ? .5f : PingPong01(time * .19f + .11f));
        var y = 10f + radius + yTravel * (ReduceHeroMotion ? .5f : PingPong01(time * .31f + .29f));
        var orb = pos + new Vector2(x, y);
        d.PushClipRect(pos + new Vector2(4f,4f), max - new Vector2(4f,4f), true);
        try
        {
            d.AddCircleFilled(orb, radius + 5f, Ui(new Vector4(.61f,.42f,.14f,.030f)), 64);
            d.AddCircle(orb, radius, Ui(new Vector4(.88f,.67f,.36f,.11f)), 64, 1.2f);
            d.AddCircle(orb, radius * .68f, Ui(new Vector4(.88f,.67f,.36f,.07f)), 64, 1f);
            // Small accompanying triangles stay BEHIND controls and headings.
            var phase = ReduceHeroMotion ? 0f : time;
            DrawMiniTriangle(d, orb + new Vector2(-12f, -8f), 4f, phase * .38f,
                new Vector4(.96f,.74f,.34f,.13f));
        }
        finally { d.PopClipRect(); }
        var center = pos + new Vector2(53f, height * .5f);
        d.AddCircleFilled(center,31f,Ui(DeepGold),48);
        d.AddCircle(center,31f,Ui(Mint),48,1.2f);
        var g = ImGui.CalcTextSize(glyph);
        d.AddText(center - g * .5f, Ui(White), glyph);
        var tx = pos.X + 104f;
        d.AddText(new Vector2(tx,pos.Y+height*.27f),Ui(Mint),kicker.ToUpperInvariant());
        d.AddText(new Vector2(tx,pos.Y+height*.46f),Ui(White),headline);
        d.AddText(new Vector2(tx,pos.Y+height*.67f),Ui(Muted),description);
        d.AddRect(pos,max,Ui(GoldLine),14f);
        ImGui.Dummy(new Vector2(w,height));
    }

    private static void SummaryCard(string id,float width,string symbol,string label,string value,string explanation,Vector4 accent)
    {
        var pos=ImGui.GetCursorScreenPos();
        var d=ImGui.GetWindowDrawList();
        d.AddRectFilled(pos,pos+new Vector2(width,123),Ui(Panel),12f);
        d.AddRect(pos,pos+new Vector2(width,123),Ui(new Vector4(.32f,.26f,.17f,1f)),12f);
        d.AddCircleFilled(pos+new Vector2(30,35),18,Ui(accent),32);
        d.AddText(pos+new Vector2(23,27),Ui(White),symbol);
        d.AddText(pos+new Vector2(58,19),Ui(White),value);
        d.AddText(pos+new Vector2(58,39),Ui(Muted),label);
        d.AddText(pos+new Vector2(14,81),Ui(Muted),explanation);
        ImGui.Dummy(new Vector2(width,123));
    }

    private static void StatusPill(string text,Vector4 tint)
    {
        var pos=ImGui.GetCursorScreenPos();
        var width=ImGui.CalcTextSize(text).X+34;
        var d=ImGui.GetWindowDrawList();
        d.AddRectFilled(pos,pos+new Vector2(width,25),Ui(Vector4.Lerp(Nav,tint,.22f)),13f);
        d.AddRect(pos,pos+new Vector2(width,25),Ui(Vector4.Lerp(Nav,tint,.57f)),13f);
        d.AddCircleFilled(pos+new Vector2(12,12),3.5f,Ui(tint));
        d.AddText(pos+new Vector2(22,5),Ui(tint),text);
        ImGui.Dummy(new Vector2(width,25));
    }

    // Reusable bespoke button, shared by all editor pages. It deliberately uses
    // ImGui InvisibleButton for interaction but draws its own complete visual.
    private static bool StyledButton(string label, Vector2 requested = default)
    {
        var visible = label.Split(new[] { "##" }, 2, StringSplitOptions.None)[0];
        var measure = ImGui.CalcTextSize(visible);
        var w = requested.X < 0 ? ImGui.GetContentRegionAvail().X :
            requested.X > 0 ? requested.X : Math.Max(112f,measure.X+42f);
        var h = requested.Y > 0 ? requested.Y : 39f;
        var size = new Vector2(Math.Max(28,Math.Min(w, ImGui.GetContentRegionAvail().X)),h);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(label,size);
        var hover = ImGui.IsItemHovered();
        var down = ImGui.IsItemActive();
        var danger = visible.StartsWith("Remove",StringComparison.OrdinalIgnoreCase)
                  || visible.StartsWith("Delete",StringComparison.OrdinalIgnoreCase);
        var special = visible.StartsWith("+",StringComparison.Ordinal)
                   || visible.StartsWith("Edit",StringComparison.Ordinal)
                   || visible.StartsWith("Bind",StringComparison.Ordinal)
                   || visible.StartsWith("Confirm",StringComparison.Ordinal)
                   || visible.StartsWith("Save",StringComparison.Ordinal);
        var normal = danger ? new Vector4(.28f,.13f,.12f,1f) :
            special ? new Vector4(.30f,.23f,.13f,1f) : Surface2;
        var fill = down ? Vector4.Lerp(normal,Mint,.25f) :
                   hover ? Vector4.Lerp(normal,Selected,.55f) : normal;
        var d=ImGui.GetWindowDrawList();
        d.AddRectFilled(pos+new Vector2(0,2),pos+size+new Vector2(0,2),Ui(new Vector4(0,0,0,.20f)),9f);
        d.AddRectFilled(pos,pos+size,Ui(fill),9f);
        d.AddRect(pos,pos+size,Ui(special ? Mint : danger ? Warning : GoldLine),9f);
        d.AddText(pos + new Vector2(15,(h-measure.Y)*.5f),Ui(danger?Warning:White),visible);
        if (size.X >= measure.X+52)
            d.AddText(pos + new Vector2(size.X-17,(h-measure.Y)*.5f),Ui(hover ? Mint : Muted),">");
        return ImGui.IsItemClicked();
    }

    // Epithet-rendered controls: ImGui only handles hit testing, focus and text entry.
    // No native ImGui button/checkbox/slider/selector chrome is visible.
    private static bool StudioToggle(string id, string label, ref bool value)
    {
        var start=ImGui.GetCursorScreenPos();
        var w=Math.Max(160f,ImGui.GetContentRegionAvail().X);
        ImGui.InvisibleButton(id,new Vector2(w,43));
        var hover=ImGui.IsItemHovered();
        var changed=ImGui.IsItemClicked();
        if(changed) value=!value;
        var d=ImGui.GetWindowDrawList();
        d.AddRectFilled(start,start+new Vector2(w,43),Ui(hover?Surface2:Panel),9f);
        d.AddRect(start,start+new Vector2(w,43),Ui(GoldLine),9f);
        var x=start.X+w-52;
        d.AddRectFilled(new Vector2(x,start.Y+12),new Vector2(x+38,start.Y+31),
            Ui(value?new Vector4(.47f,.34f,.15f,1f):new Vector4(.23f,.23f,.24f,1f)),12f);
        d.AddCircleFilled(new Vector2(x+(value?27:11),start.Y+21.5f),7.4f,Ui(value?Mint:Muted),30);
        d.AddText(start+new Vector2(14,12),Ui(White),label);
        return changed;
    }

    private static bool StudioInput(string id, string label, ref string value, int limit=65)
    {
        ImGui.TextColored(Muted,label.ToUpperInvariant());
        var pos=ImGui.GetCursorScreenPos();
        var width=Math.Max(120f,ImGui.GetContentRegionAvail().X);
        var d=ImGui.GetWindowDrawList();
        d.AddRectFilled(pos,pos+new Vector2(width,39),Ui(Nav),8f);
        d.AddRect(pos,pos+new Vector2(width,39),Ui(GoldLine),8f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg,Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered,Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive,Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize,0f);
        ImGui.SetNextItemWidth(width);
        try { return ImGui.InputText(id,ref value,limit); }
        finally { ImGui.PopStyleVar();ImGui.PopStyleColor(3); }
    }

    private static bool StudioSearch(string id,string hint,ref string value,int limit=65)
    {
        var pos=ImGui.GetCursorScreenPos();
        var width=Math.Max(120f,ImGui.GetContentRegionAvail().X);
        var d=ImGui.GetWindowDrawList();
        d.AddRectFilled(pos,pos+new Vector2(width,39),Ui(Nav),9f);
        d.AddRect(pos,pos+new Vector2(width,39),Ui(GoldLine),9f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg,Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered,Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive,Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize,0f);
        ImGui.SetNextItemWidth(width);
        try { return ImGui.InputTextWithHint(id,hint,ref value,limit); }
        finally { ImGui.PopStyleVar();ImGui.PopStyleColor(3); }
    }

    private static bool StudioSelect(string id,string value,string[] choices,out int selected)
    {
        var pickedIndex = -1;
        var width=Math.Max(130f,ImGui.GetContentRegionAvail().X);
        var pos=ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id,new Vector2(width,41));
        var hover=ImGui.IsItemHovered();
        if(ImGui.IsItemClicked()) ImGui.OpenPopup(id+"_popup");
        var d=ImGui.GetWindowDrawList();
        d.AddRectFilled(pos,pos+new Vector2(width,41),Ui(hover?Surface2:Nav),9f);
        d.AddRect(pos,pos+new Vector2(width,41),Ui(GoldLine),9f);
        d.AddText(pos+new Vector2(14,12),Ui(White),value);
        d.AddText(pos+new Vector2(width-22,12),Ui(Mint),"v");
        ImGui.PushStyleColor(ImGuiCol.PopupBg,Panel);
        ImGui.PushStyleColor(ImGuiCol.Border,GoldLine);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,new Vector2(8,9));
        try
        {
            ImGui.SetNextWindowSizeConstraints(new Vector2(Math.Max(220f,width), 0f),
                new Vector2(Math.Max(320f,width+10f), Math.Min(430f, ImGui.GetIO().DisplaySize.Y * .62f)));
            if(ImGui.BeginPopup(id+"_popup"))
            {
                try
                {
                    var childHeight = Math.Min(360f, Math.Max(74f, choices.Length * 40f + 8f));
                    Child("##select_scroll_"+id, new Vector2(Math.Max(220f,width+8f), childHeight), Vector4.Zero, () =>
                    {
                        foreach(var (choice,index) in choices.Select((c,i)=>(c,i)))
                        {
                            if(StyledButton(choice+"##"+id+index,new Vector2(-1,36)))
                            {
                                pickedIndex=index;
                                ImGui.CloseCurrentPopup();
                            }
                        }
                    }, false, true);
                }
                finally { ImGui.EndPopup(); }
            }
        }
        finally { ImGui.PopStyleVar();ImGui.PopStyleColor(2); }
        selected = pickedIndex;
        return selected>=0;
    }

    private static bool StudioSliderInt(string id,string label,ref int value,int min,int max)
    {
        ImGui.TextColored(Muted,label.ToUpperInvariant());
        var pos=ImGui.GetCursorScreenPos();
        var w=Math.Max(120f,ImGui.GetContentRegionAvail().X);
        ImGui.InvisibleButton(id,new Vector2(w,42));
        var changed=false;
        if(ImGui.IsItemActive()&&ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var t=Math.Clamp((ImGui.GetIO().MousePos.X-pos.X-13)/Math.Max(1,w-26),0f,1f);
            var n=(int)MathF.Round(min+(max-min)*t);
            if(n!=value){value=n;changed=true;}
        }
        var d=ImGui.GetWindowDrawList();
        d.AddRectFilled(pos,pos+new Vector2(w,42),Ui(Nav),8f);
        d.AddRect(pos,pos+new Vector2(w,42),Ui(GoldLine),8f);
        var a=pos.X+13;
        var b=pos.X+w-13;
        var t2=Math.Clamp((value-min)/(float)Math.Max(1,max-min),0f,1f);
        var knobX=a+(b-a)*t2;
        var trackY=pos.Y+28f;
        d.AddRectFilled(new Vector2(a,trackY-3),new Vector2(b,trackY+3),Ui(new Vector4(.30f,.22f,.12f,1f)),3f);
        d.AddRectFilled(new Vector2(a,trackY-3),new Vector2(knobX,trackY+3),Ui(new Vector4(.95f,.74f,.33f,1f)),3f);
        d.AddCircleFilled(new Vector2(knobX,trackY),8,Ui(White),28);
        d.AddCircleFilled(new Vector2(knobX,trackY),5,Ui(new Vector4(.90f,.66f,.24f,1f)),28);
        var text=value.ToString();
        var textWidth=ImGui.CalcTextSize(text).X;
        var textX=MathF.Min(pos.X+w-textWidth-11f,knobX-textWidth-10f);
        textX=MathF.Max(pos.X+11f,textX);
        d.AddText(new Vector2(textX,pos.Y+4),Ui(White),text);
        return changed;
    }

    private static float PingPong01(float value)
    {
        var wrap = value % 2f;
        if (wrap < 0f) wrap += 2f;
        return wrap <= 1f ? wrap : 2f - wrap;
    }

    private static void DrawMiniTriangle(ImDrawListPtr draw, Vector2 center, float radius, float rotation, Vector4 tint)
    {
        var a = center + Rotate(new Vector2(0f, -radius), rotation);
        var b = center + Rotate(new Vector2(radius * .86f, radius * .62f), rotation);
        var c = center + Rotate(new Vector2(-radius * .86f, radius * .62f), rotation);
        draw.AddTriangleFilled(a, b, c, Ui(tint));
    }

    private static Vector2 Rotate(Vector2 v, float angle)
    {
        var s = MathF.Sin(angle);
        var c = MathF.Cos(angle);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    private static readonly string[] JobChoices =
    {
        "Any job", "Paladin", "Warrior", "Dark Knight", "Gunbreaker", "White Mage", "Scholar", "Astrologian", "Sage",
        "Monk", "Dragoon", "Ninja", "Samurai", "Reaper", "Viper", "Bard", "Machinist", "Dancer",
        "Black Mage", "Summoner", "Red Mage", "Pictomancer", "Blue Mage", "Carpenter", "Blacksmith", "Armorer",
        "Goldsmith", "Leatherworker", "Weaver", "Alchemist", "Culinarian", "Miner", "Botanist", "Fisher"
    };

    // These map to ClassJob row IDs, not the presentation order in JobChoices.
    // Do not infer a ClassJob ID from a dropdown index.
    private static readonly uint[] JobIds =
    {
        0, 19, 21, 32, 37, 24, 28, 33, 40,
        20, 22, 30, 34, 39, 41, 23, 31, 38,
        25, 27, 35, 42, 36, 8, 9, 10,
        11, 12, 13, 14, 15, 16, 17, 18
    };

    private static string JobNameById(uint id)
    {
        var index = Array.IndexOf(JobIds, id);
        return index >= 0 && index < JobChoices.Length ? JobChoices[index] : $"Job #{id}";
    }


    private static void Caption(string heading, string subheading)
    {
        ImGui.TextColored(White, heading);
        ImGui.TextColored(Muted, subheading);
        ImGui.Spacing();
    }
}
