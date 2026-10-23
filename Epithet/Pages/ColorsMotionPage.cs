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
    private void DrawColorsAndMotion()
    {
        if(!cfg.Profiles.Any(IsRealProfile))
        {
            Hero("COLORS & MOTION","Color your next adventure.","Bind a character and create a title to start.","✦");
            ImGui.Spacing();
            if(StyledButton("Bind current character")) BindCurrentCharacter();
            return;
        }
        var p=cfg.SelectedProfile;
        var t=p.SelectedTitle;
        Hero("COLORS & MOTION","Colors bring your title to life.",
            "Design gradients, vibrant palettes and title animations.","✦",151f);
        ImGui.Spacing();
        if(t is null)
        {
            Card("##color_empty",105,()=>Caption("CREATE A TITLE FIRST","Open Title Library to add a title."));
            return;
        }
        var wide=ImGui.GetContentRegionAvail().X>=760;
        var half=(ImGui.GetContentRegionAvail().X-12f)*.5f;
        if(wide)
        {
            Child("##color_left",new Vector2(half,0),Vector4.Zero,()=>
            {
                Card("##color_palette",590,()=>
                {
                    Caption("COLOR DESIGN","Choose 1, 2 or 3 colors for your title.");
                    PaletteControls(t);
                });
                ImGui.Spacing();
                Card("##saved_palette_note",245,()=>
                {
                    ImGui.TextColored(Mint,"SAVED PALETTES");
                    ImGui.TextWrapped("Choose a preset, save your current colors under a name, or delete a custom palette. Everything stays local.");
                    ImGui.Spacing();
                    DrawPaletteLibrary(t);
                });
            },false);
            ImGui.SameLine(0,12);
            Child("##color_right",Vector2.Zero,Vector4.Zero,()=>
            {
                Card("##color_live",252,()=>
                {
                    Caption("LIVE NAMEPLATE PREVIEW","Only your title is colorized; your name stays original.");
                    ImGui.Spacing(); DrawPreview(p);
                });
                ImGui.Spacing();
                DrawMotionEditor(p,t);
            },false);
        }
        else
        {
            Card("##color_palette_small",590,()=>{Caption("COLOR DESIGN","Choose up to three colors.");PaletteControls(t);});
            ImGui.Spacing();
            Card("##saved_palette_small",245,()=>
            {
                ImGui.TextColored(Mint,"SAVED PALETTES");
                ImGui.TextWrapped("Choose a preset, save your current colors under a name, or delete a custom palette. Everything stays local.");
                ImGui.Spacing();
                DrawPaletteLibrary(t);
            });
            ImGui.Spacing();
            Card("##color_live_small",200,()=>{Caption("LIVE NAMEPLATE PREVIEW","Title only — normal character name preserved.");DrawPreview(p);});
            ImGui.Spacing();
            DrawMotionEditor(p,t);
        }
    }

    private void DrawMotionEditor(CharacterProfile profile,TitlePreset title)
    {
        Card("##motion_editor",340,()=>
        {
            Caption("MOTION & EFFECTS","Bring your title to life with animated colors.");
            EffectCombo(title);
            AnimationSlider(profile);
            var fps=cfg.AnimationFps;
            if(StudioSliderInt("##smoothness", "Smoothness / FPS",ref fps,8,60))
            {
                cfg.AnimationFps=fps;MarkDirty();
            }
            ImGui.TextColored(Muted,"The normal FFXIV character name is never recolored.");
        });
        ImGui.Spacing();
        if(StyledButton(showAdvancedConditions ? "Hide title conditions" : "Show title conditions",new Vector2(250,35)))
            showAdvancedConditions=!showAdvancedConditions;
        if(showAdvancedConditions)
        {
            ImGui.Spacing();
            Card("##motion_conditions", 430, () =>
            {
                Caption("ADVANCED TITLE RULES","Optional restrictions for the selected title.");
                ImGui.TextColored(Muted, "JOB RESTRICTION");
                var jobLabel = JobNameById(title.JobId);
                if (StudioSelect("##condition_job_combo", jobLabel, JobChoices, out var jobIndex))
                {
                    title.JobId = JobIds[jobIndex];
                    MarkDirty();
                }
                var zone=title.TerritoryId.ToString();
                if(StudioInput("##condition_zone","Territory / zone ID (0 = anywhere)",ref zone,7)
                    && ushort.TryParse(zone,out var zoneValue))
                {title.TerritoryId=zoneValue;MarkDirty();}
                ColoredWrapped(Muted, $"Current rule summary: {JobNameById(title.JobId)} · Territory {(title.TerritoryId == 0 ? "anywhere" : "#" + title.TerritoryId)}");
                ColoredWrapped(Muted, "Territory means an in-game zone or map, not your home world. World / server belongs to the Character Profiles page.");
                if(StudioToggle("##combat_only", "Only in combat",ref title.OnlyInCombat))
                {if(title.OnlyInCombat)title.OnlyOutOfCombat=false;MarkDirty();}
                if(StudioToggle("##outside_combat", "Only outside combat",ref title.OnlyOutOfCombat))
                {if(title.OnlyOutOfCombat)title.OnlyInCombat=false;MarkDirty();}
                if(StudioToggle("##rotate_titles", "Include in title rotation",ref title.Rotate)) MarkDirty();
                if(title.Rotate && StudioSliderInt("##rotation_interval","Rotation interval (seconds)",ref title.RotationSeconds,5,180)) MarkDirty();
            });
        }
    }

    private void SetPalette(TitlePreset t, string first, string mid, string last, float middlePosition, float startPosition, float endPosition)
    {
        t.ColorCount = 3; t.StartColor = first; t.MiddleColor = mid; t.EndColor = last;
        t.MiddlePosition = Math.Clamp(middlePosition, 0.05f, 0.95f);
        t.StartPosition = Math.Clamp(startPosition, 0f, .97f);
        t.EndPosition = Math.Clamp(endPosition, t.StartPosition + .03f, 1f);
        t.Effect = "Gradient"; MarkDirty();
    }

    private void PaletteControls(TitlePreset t)
    {
        var count = Math.Clamp(t.ColorCount, 1, 3);
        ImGui.Spacing();
        ImGui.TextColored(Muted, "COLOR STOPS  /  MAXIMUM 3");
        for (var i = 1; i <= 3; i++)
        {
            if (i > 1) ImGui.SameLine();
            var pressed = count == i;
            if (pressed) ImGui.PushStyleColor(ImGuiCol.Button, Selected);
            try
            {
                if (StyledButton(i + (i == 1 ? " Solid##c1" : i == 2 ? " Colors##c2" : " Colors##c3"), new Vector2(112, 30)))
                {
                    t.ColorCount = i; count = i;
                    currentPaletteId = "";
                    if (i > 1 && t.Effect == "Static") t.Effect = "Gradient";
                    MarkDirty();
                }
            }
            finally { if (pressed) ImGui.PopStyleColor(); }
        }
        ImGui.Spacing();
        ColorEditor("Front / Start", t.Id + "first", ref t.StartColor);
        if (count == 3) ColorEditor("Middle", t.Id + "middle", ref t.MiddleColor);
        if (count >= 2) ColorEditor("Back / End", t.Id + "end", ref t.EndColor);
        ImGui.Spacing();
        DrawGradientEditor(t);
        ImGui.Spacing();
        if (StudioToggle("##outline", "Enable title outline (native edge)", ref t.UseGlow)) MarkDirty();
        if (t.UseGlow)
        {
            ColorEditor("Outline color", t.Id + "glow", ref t.GlowColor);
            ColoredWrapped(Muted, "Native FFXIV edge only. Soft bloom needs a separate overlay renderer.");
            if (t.ColorCount >= 2)
                ColoredWrapped(Warning, "Legacy Honorific compatibility may not preserve every color and outline setting; Epithet clients can.");
        }
    }

    private void DrawPaletteLibrary(TitlePreset t)
    {
        cfg.Palettes ??= new List<TitlePalette>();
        var selected = cfg.Palettes.FirstOrDefault(x => x.Id == currentPaletteId);
        ImGui.TextColored(Muted, "CHOOSE OR MANAGE A PALETTE");
        var paletteChoices=cfg.Palettes.Select(x=>x.Name+"  /  "+x.ColorCount+" colors").ToArray();
        if (paletteChoices.Length>0 && StudioSelect("##ep_saved_palette",
            selected?.Name ?? "Choose a palette",paletteChoices,out var selectedIndex))
        {
            var pal=cfg.Palettes[selectedIndex];
            SetPalette(t,pal.StartColor,pal.MiddleColor,pal.EndColor,pal.MiddlePosition,pal.StartPosition,pal.EndPosition);
            t.ColorCount=pal.ColorCount;
            t.Effect=pal.ColorCount == 1 ? "Static" : "Gradient";
            currentPaletteId=pal.Id;MarkDirty();
        }
        else if(paletteChoices.Length==0) ImGui.TextColored(Muted,"No palettes saved yet.");
        ImGui.Spacing();
        ImGui.SetNextItemWidth(Math.Min(270f, Math.Max(150f, ImGui.GetContentRegionAvail().X - 220)));
        StudioSearch("##palette_name_draft", "Palette name...", ref paletteNameDraft, 43);
        ImGui.SameLine();
        if (StyledButton("+ Save palette", new Vector2(135, 32)))
        {
            var name = paletteNameDraft.Trim();
            if (name.Length is > 0 and <= 42 && cfg.Palettes.Count < 120)
            {
                var existing = cfg.Palettes.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (existing is { IsBuiltIn: true }) notice = "That name belongs to a built-in preset. Choose another name.";
                else if (existing is not null)
                {
                    existing.ColorCount = Math.Clamp(t.ColorCount, 1, 3);
                    existing.StartColor = t.StartColor; existing.MiddleColor = t.MiddleColor;
                    existing.EndColor = t.EndColor; existing.MiddlePosition = t.MiddlePosition;
                    existing.StartPosition = t.StartPosition; existing.EndPosition = t.EndPosition;
                    currentPaletteId = existing.Id;
                    notice = $"Updated saved palette '{name}'.";
                    MarkDirty();
                }
                else
                {
                    var pal = TitlePalette.Create(name, Math.Clamp(t.ColorCount, 1, 3),
                        t.StartColor, t.MiddleColor, t.EndColor, false, t.MiddlePosition, t.StartPosition, t.EndPosition);
                    cfg.Palettes.Add(pal);
                    currentPaletteId = pal.Id;
                    notice = $"Saved palette '{name}' ({pal.ColorCount} colors).";
                    MarkDirty();
                }
            }
            else notice = "Enter a name (1-42 characters), maximum 120 saved palettes.";
        }
        ImGui.Spacing();
        Child("##palette_browser", new Vector2(0, Math.Min(220f, 48f + cfg.Palettes.Count * 39f)), Nav, () =>
        {
            foreach (var palette in cfg.Palettes.ToArray())
            {
                ImGui.PushID(palette.Id);
                try
                {
                    var label = (currentPaletteId == palette.Id ? "●  " : "○  ") + palette.Name + $"   ·   {palette.ColorCount} color" + (palette.ColorCount == 1 ? string.Empty : "s") + (palette.IsBuiltIn ? "   ·   BUILT-IN" : string.Empty);
                    if (StyledButton(label + "##palette_pick", new Vector2(-1, 36)))
                    {
                        SetPalette(t,palette.StartColor,palette.MiddleColor,palette.EndColor,palette.MiddlePosition,palette.StartPosition,palette.EndPosition);
                        t.ColorCount=palette.ColorCount;
                        t.Effect=palette.ColorCount == 1 ? "Static" : "Gradient";
                        currentPaletteId=palette.Id;MarkDirty();
                    }
                    if (ImGui.BeginPopupContextItem("##palette_context"))
                    {
                        try
                        {
                            if (!palette.IsBuiltIn && ImGui.MenuItem("Delete palette"))
                            {
                                if (cfg.ConfirmPaletteDeletion) pendingPaletteDeleteId = palette.Id;
                                else
                                {
                                    cfg.Palettes.RemoveAll(x => x.Id == palette.Id);
                                    if (currentPaletteId == palette.Id) currentPaletteId = "";
                                    notice = "Custom palette deleted.";
                                    MarkDirty();
                                }
                            }
                        }
                        finally { ImGui.EndPopup(); }
                    }
                }
                finally { ImGui.PopID(); }
            }
        }, false, true);
        if (!string.IsNullOrEmpty(pendingPaletteDeleteId))
        {
            var deleteTarget = cfg.Palettes.FirstOrDefault(x => x.Id == pendingPaletteDeleteId);
            if (deleteTarget is not null)
            {
                ImGui.Spacing();
                ColoredWrapped(Warning, $"Delete palette '{deleteTarget.Name}'? This cannot be undone.");
                if (StyledButton("Delete palette", new Vector2(150, 32)))
                {
                    cfg.Palettes.RemoveAll(x => x.Id == deleteTarget.Id);
                    if (currentPaletteId == deleteTarget.Id) currentPaletteId = "";
                    pendingPaletteDeleteId = "";
                    notice = "Custom palette deleted.";
                    MarkDirty();
                }
                ImGui.SameLine();
                if (StyledButton("Cancel", new Vector2(110, 32))) pendingPaletteDeleteId = "";
            }
            else pendingPaletteDeleteId = "";
        }
        ColoredWrapped(Muted, "Right-click a custom palette in the list to delete it. Built-in presets are protected.");
        ImGui.Spacing();
    }

    private int draggingGradientStop = -1;

    private void DrawGradientEditor(TitlePreset t)
    {
        ColorUtils.TryParseHex(t.StartColor, out var first);
        ColorUtils.TryParseHex(t.MiddleColor, out var mid);
        ColorUtils.TryParseHex(t.EndColor, out var end);
        var pos = ImGui.GetCursorScreenPos();
        var width = Math.Max(140f, ImGui.GetContentRegionAvail().X - 8f);
        const float height = 48f;
        const float stopPad = 10f;
        const float barTop = 5f;
        const float barBottom = 30f;
        var trackLeft = pos.X + stopPad;
        var trackRight = pos.X + width - stopPad;
        var trackWidth = Math.Max(1f, trackRight - trackLeft);
        ImGui.InvisibleButton("##gradient_editor_" + t.Id, new Vector2(width, height));
        var mouse = ImGui.GetIO().MousePos;
        var positions = new[] { t.StartPosition, t.MiddlePosition, t.EndPosition };
        var handles = t.ColorCount == 3 ? new[] { 0, 1, 2 } : new[] { 0, 2 };
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            draggingGradientTitleId = t.Id;
            draggingGradientStop = -1;
            var nearestDistance = 14f;
            foreach (var handle in handles)
            {
                var distance = MathF.Abs(mouse.X - (trackLeft + positions[handle] * trackWidth));
                if (distance <= nearestDistance)
                {
                    nearestDistance = distance;
                    draggingGradientStop = handle;
                }
            }
            // Clicking elsewhere continues to move the middle stop for 3-color mode.
            if (draggingGradientStop == -1 && t.ColorCount == 3) draggingGradientStop = 1;
        }
        if (draggingGradientTitleId == t.Id)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left) && draggingGradientStop != -1)
            {
                var x = Math.Clamp((mouse.X - trackLeft) / trackWidth, 0f, 1f);
                var old = draggingGradientStop switch
                {
                    0 => t.StartPosition,
                    1 => t.MiddlePosition,
                    _ => t.EndPosition
                };
                var next = draggingGradientStop switch
                {
                    0 => Math.Clamp(x, 0f, (t.ColorCount == 3 ? t.MiddlePosition : t.EndPosition) - .03f),
                    1 => Math.Clamp(x, t.StartPosition + .03f, t.EndPosition - .03f),
                    _ => Math.Clamp(x, (t.ColorCount == 3 ? t.MiddlePosition : t.StartPosition) + .03f, 1f)
                };
                if (MathF.Abs(old - next) > .0001f)
                {
                    switch (draggingGradientStop)
                    {
                        case 0: t.StartPosition = next; break;
                        case 1: t.MiddlePosition = next; break;
                        case 2: t.EndPosition = next; break;
                    }
                    currentPaletteId = "";
                    MarkDirty();
                }
            }
            else if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                draggingGradientTitleId = "";
                draggingGradientStop = -1;
            }
        }
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + new Vector2(width, height), Ui(Nav), 8f);
        const int segments = 96;
        for (var i = 0; i < segments; i++)
        {
            var c = ColorUtils.SamplePalette(first, mid, end, t.ColorCount,
                i / (float)(segments - 1), t.MiddlePosition, t.StartPosition, t.EndPosition);
            draw.AddRectFilled(new Vector2(trackLeft + trackWidth * i / segments, pos.Y + barTop),
                new Vector2(trackLeft + trackWidth * (i + 1) / segments + 1f, pos.Y + barBottom),
                ImGui.GetColorU32(new Vector4(c, 1f)));
        }
        draw.AddRect(pos + new Vector2(1, 1), pos + new Vector2(width - 1, height - 1), Ui(GoldLine), 8f, 0, 1.4f);
        DrawStop(t.StartPosition, Ui(White));
        DrawStop(t.EndPosition, Ui(White));
        if (t.ColorCount == 3) DrawStop(t.MiddlePosition, Ui(new Vector4(.98f, .72f, .29f, 1f)));
        ColoredWrapped(Muted, "Drag the white start/end markers to adjust the gradient range; the gold marker adjusts the middle.");

        void DrawStop(float x, uint color)
        {
            var center = new Vector2(trackLeft + trackWidth * x, pos.Y + 17f);
            draw.AddCircleFilled(center, 8f, Ui(Panel), 24);
            draw.AddCircleFilled(center, 4.8f, color, 24);
            draw.AddCircle(center, 9f, Ui(GoldLine), 24, 1.5f);
        }
    }

    private void EffectCombo(TitlePreset t)
    {
        var index = Math.Max(0, Array.IndexOf(Effects, t.Effect));
        ImGui.TextColored(Muted, "EFFECT TYPE");
        if (StudioSelect("##effect_selector", Effects[index], Effects, out var next))
        {
            t.Effect = Effects[next]; MarkDirty();
        }
    }

    private void AnimationSlider(CharacterProfile profile)
    {
        ImGui.TextColored(Muted, "MOTION SPEED");
        var pos = ImGui.GetCursorScreenPos();
        var width = Math.Clamp(ImGui.GetContentRegionAvail().X, 160f, 490f);
        var sz = new Vector2(width, 44f);
        ImGui.InvisibleButton("##ep_motion_speed", sz);
        if (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var px = (ImGui.GetIO().MousePos.X - pos.X - 12f) / Math.Max(1f, width - 24f);
            var next = .25f + Math.Clamp(px, 0f, 1f) * 5.75f;
            next = MathF.Round(next, 2);
            if (MathF.Abs(profile.AnimationSpeed - next) > .001f)
            {
                profile.AnimationSpeed = next;
                MarkDirty();
            }
        }
        var hovering = ImGui.IsItemHovered();
        var d = ImGui.GetWindowDrawList();
        d.AddRectFilled(pos, pos + sz, Ui(Nav), 9f);
        d.AddRect(pos, pos + sz, Ui(GoldLine), 9f);
        var value = Math.Clamp((profile.AnimationSpeed-.25f)/5.75f,0f,1f);
        var left = pos.X + 12f;
        var right = pos.X + width - 12f;
        var cy = pos.Y + 29f;
        d.AddRectFilled(new Vector2(left,cy-3),new Vector2(right,cy+3),Ui(new Vector4(.27f,.20f,.12f,1f)),3f);
        d.AddRectFilled(new Vector2(left,cy-3),new Vector2(left+(right-left)*value,cy+3),Ui(new Vector4(.95f,.74f,.33f,1f)),3f);
        d.AddCircleFilled(new Vector2(left+(right-left)*value,cy),8f,Ui(White),20);
        d.AddCircleFilled(new Vector2(left+(right-left)*value,cy),5f,Ui(new Vector4(.90f,.66f,.24f,1f)),20);
        d.AddText(pos+new Vector2(13,5),Ui(Muted),"0.25×");
        var speedText = $"{profile.AnimationSpeed:0.00}×";
        var speedWidth = ImGui.CalcTextSize(speedText).X;
        d.AddText(pos+new Vector2(width-speedWidth-13,5),Ui(hovering ? Mint : White),speedText);
        ColoredWrapped(Muted, "Drag to adjust · 0.25×–6.00× · Changes apply automatically");
    }
}
