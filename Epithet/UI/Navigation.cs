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
    private void DrawNav()
    {
        // Icon rail, not a themed list of Dalamud buttons. Every hit region is invisible.
        var width = Math.Max(52f, ImGui.GetContentRegionAvail().X);
        // The app header already contains the Epithet logo. Keep the navigation rail focused on page icons.
        ImGui.Dummy(new Vector2(1, 10));
        for (var i = 0; i < Pages.Length; i++) DrawNavItem(i);
        ImGui.Dummy(new Vector2(1, 24));
        var text = "EPITHET";
        var footer = ImGui.GetCursorScreenPos();
        var d = ImGui.GetWindowDrawList();
        d.AddText(footer + new Vector2(Math.Max(0, (width-ImGui.CalcTextSize(text).X)/2),0), Ui(Green), text);
        ImGui.Dummy(new Vector2(1, 26));
    }

    private void DrawNavItem(int index)
    {
        var labels = new[] { "Overview", "Title Library", "Colors & Motion", "Characters", "Compatibility", "Local Backups" };
        var w = Math.Max(52f, ImGui.GetContentRegionAvail().X);
        var rect = new Vector2(w, 72f);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##ep_rail_" + index, rect);
        var hovered = ImGui.IsItemHovered();
        var active = index == page;
        var target = active ? 1f : (hovered ? .54f : 0f);
        navHover[index] += (target-navHover[index])*Math.Clamp(ImGui.GetIO().DeltaTime*(cfg.ReduceUiMotion ? 100f : 14f),0f,1f);
        var d = ImGui.GetWindowDrawList();
        var boxSize = 54f;
        var boxMin = pos + new Vector2((w-boxSize)/2, 7f);
        var boxMax = boxMin + new Vector2(boxSize, boxSize);
        if (navHover[index] > .01f)
        {
            d.AddRectFilled(pos, pos + rect,
                Ui(new Vector4(.44f,.32f,.13f,.30f * navHover[index])), 9f);
            if (active) d.AddRectFilled(pos, pos + new Vector2(3,rect.Y), Ui(Mint), 2f);
        }
        d.AddRectFilled(boxMin, boxMax, Ui(Vector4.Lerp(DeepGold,Selected,navHover[index])), 12f);
        d.AddRect(boxMin, boxMax, Ui(active ? Mint : GoldLine), 12f);
        var ink = Ui(active ? White : Muted);
        var center = boxMin + new Vector2(boxSize * 0.5f, boxSize * 0.5f);
        var iconSize = active ? 53f : hovered ? 52f : 51f;
        var iconMin = center - new Vector2(iconSize * 0.5f, iconSize * 0.5f);
        if (!StudioAssets.Draw(StudioAssets.Page(index), iconMin, new Vector2(iconSize, iconSize), active ? 1f : hovered ? 1f : .98f))
        switch(index)
        {
            case 0: // home
                d.AddLine(center+new Vector2(-11,-2),center+new Vector2(0,-11),ink,2.2f);
                d.AddLine(center+new Vector2(0,-11),center+new Vector2(11,-2),ink,2.2f);
                d.AddRect(center+new Vector2(-8,-2),center+new Vector2(8,10),ink,1f,0,2f);
                break;
            case 1: // typographic title
                d.AddLine(center+new Vector2(-9,-8),center+new Vector2(9,-8),ink,2f);
                d.AddLine(center+new Vector2(0,-8),center+new Vector2(0,9),ink,2.2f);
                d.AddLine(center+new Vector2(-5,9),center+new Vector2(5,9),ink,2f);
                break;
            case 2: // palette
                d.AddCircle(center,11f,ink,28,2f);
                d.AddCircleFilled(center+new Vector2(-4,-4),2.2f,Ui(new Vector4(.90f,.43f,.50f,1)),18);
                d.AddCircleFilled(center+new Vector2(4,-4),2.2f,Ui(new Vector4(.93f,.75f,.36f,1)),18);
                d.AddCircleFilled(center+new Vector2(-3,4),2.2f,Ui(new Vector4(.40f,.72f,.74f,1)),18);
                break;
            case 3: // grouped characters
                d.AddCircle(center+new Vector2(-3,-5),4.6f,ink,22,1.8f);
                d.AddCircle(center+new Vector2(7,-3),3.6f,ink,22,1.7f);
                d.AddLine(center+new Vector2(-11,8),center+new Vector2(-9,3),ink,2f);
                d.AddLine(center+new Vector2(-9,3),center+new Vector2(2,3),ink,2f);
                d.AddLine(center+new Vector2(2,3),center+new Vector2(6,9),ink,2f);
                break;
            case 4: // sync/link
                d.AddCircle(center+new Vector2(-7,-2),5.7f,ink,22,2f);
                d.AddCircle(center+new Vector2(7,2),5.7f,ink,22,2f);
                d.AddLine(center+new Vector2(-2,-1),center+new Vector2(2,1),ink,2.4f);
                break;
            case 5: // local database
                d.AddRect(center+new Vector2(-9,-8),center+new Vector2(9,-3),ink,2f,0,1.7f);
                d.AddRect(center+new Vector2(-9,0),center+new Vector2(9,5),ink,2f,0,1.7f);
                d.AddLine(center+new Vector2(-9,9),center+new Vector2(9,9),ink,2f);
                break;
        }
        if (hovered)
        {
            ImGui.BeginTooltip();
            try { ImGui.Text(labels[index]); }
            finally { ImGui.EndTooltip(); }
        }
        if (ImGui.IsItemClicked())
        {
            Navigate(index);
            confirmDeleteAll=confirmDeleteWanderer=false;
        }
        ImGui.Dummy(new Vector2(1,3));
    }

    private void Navigate(int destination)
    {
        if(destination == page) return;
        page = destination;
        pageTransition = 0f;
    }

    private void DrawContent()
    {
        if (drawnPage != page)
        {
            drawnPage = page;
            pageTransition = 0f;
        }
        pageTransition = cfg.ReduceUiMotion ? 1f : MathF.Min(1f, pageTransition + Math.Clamp(ImGui.GetIO().DeltaTime,0f,.05f)*7.4f);
        var eased=1f-MathF.Pow(1f-pageTransition,3f);
        ImGui.Spacing();
        ImGui.TextColored(Mint, "FFXIV  /  EPITHET STUDIO  /  " + Pages[page].ToUpperInvariant());
        ImGui.TextColored(White, page switch
        {
            0 => "Your title studio",
            1 => "Choose which title to display",
            2 => "The complete title design workshop",
            3 => "One profile for every character",
            4 => "Connections and diagnostics",
            _ => "Your data, your control"
        });
        ImGui.TextColored(Muted, "Epithet Title Studio");
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        // A short animated page entrance. Clip/interaction remain managed by ImGui.
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, Math.Clamp(eased,.14f,1f));
        PageVisualAlpha = Math.Clamp(eased,.14f,1f);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (1f-eased)*12f);
        try
        {
            switch(page)
            {
                case 0: DrawOverview(); break;
                case 1: DrawTitleStudio(); break;
                case 2: DrawColorsAndMotion(); break;
                case 3: DrawCharacters(); break;
                case 4: DrawCompatibility(); break;
                case 5: DrawBackups(); break;
            }
        }
        finally { PageVisualAlpha = 1f; ImGui.PopStyleVar(); }
        ImGui.Spacing(); ImGui.Separator();
        ImGui.TextColored(pendingSave ? Mint : Green, pendingSave ? "●  Saving…" : "●  Auto-saved");
        ImGui.TextWrapped(notice);
        // Avoid a full-window fade veil: it dims the nav and creates a flash each switch.
    }
}
