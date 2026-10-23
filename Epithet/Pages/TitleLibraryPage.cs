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
    private void DrawTitleStudio()
    {
        Hero("TITLE LIBRARY", "Your titles, beautifully organized.",
            "Select, rename, randomize or remove titles. Design effects under Colors & Motion.", "T", 125f);
        ImGui.Spacing();
        if (!cfg.Profiles.Any(IsRealProfile))
        {
            Card("##no_character",135,()=>
            {
                Caption("NO CHARACTER LINKED", "Bind your logged-in character to manage a title library.");
                if(StyledButton("Bind current character")) BindCurrentCharacter();
            });
            return;
        }
        var p=cfg.SelectedProfile;
        Card("##titles_list", 475, () =>
        {
            Caption("SAVED TITLES", "Choose your active title. Only title selection and basic options live here.");
            if(StyledButton("+ New title",new Vector2(146,36)))
            {
                var newTitle=new TitlePreset { Text="New Title" };
                p.Titles.Add(newTitle);p.ActiveTitleId=newTitle.Id;MarkDirty();
            }
            ImGui.SameLine();
            if(StyledButton("Randomize",new Vector2(132,36)))
            {
                var candidates=p.Titles.Where(x=>x.Enabled).ToArray();
                if(candidates.Length == 0) notice="No enabled titles to randomize.";
                else
                {
                    var other=candidates.Where(x=>x.Id!=p.ActiveTitleId).ToArray();
                    var pool=other.Length>0?other:candidates;
                    p.ActiveTitleId=pool[Random.Shared.Next(pool.Length)].Id;
                    notice="Random title selected.";MarkDirty();
                }
            }
            ImGui.Spacing();
            var listHeight=Math.Clamp(p.Titles.Count*48f+12f,100f,208f);
            Child("##title_rows",new Vector2(0,listHeight),Nav,()=>
            {
                if(p.Titles.Count==0) ImGui.TextColored(Muted,"No titles yet. Create your first title above.");
                foreach(var title in p.Titles.ToArray())
                {
                    ImGui.PushID(title.Id);
                    try
                    {
                        var marker=p.ActiveTitleId==title.Id?"●":"○";
                        var role=title.IsPrefix?"PREFIX":"SUFFIX";
                        var display=$"{marker}  {title.Text}   ·   {role}{(title.Enabled?"":"   ·   DISABLED")}";
                        if(StyledButton(display+"##select",new Vector2(-1,40)))
                        {
                            p.ActiveTitleId=title.Id;MarkDirty();
                        }
                    }
                    finally { ImGui.PopID(); }
                }
            });
            var t=p.SelectedTitle;
            if(t != null)
            {
                ImGui.Spacing();
                if(StudioInput("##library_name","Selected title name",ref t.Text,33)) MarkDirty();
                if(StyledButton(t.IsPrefix?"● Prefix##title_prefix":"○ Prefix##title_prefix",new Vector2(123,33)))
                { t.IsPrefix=true;MarkDirty(); }
                ImGui.SameLine();
                if(StyledButton(!t.IsPrefix?"● Suffix##title_suffix":"○ Suffix##title_suffix",new Vector2(123,33)))
                { t.IsPrefix=false;MarkDirty(); }
                ImGui.SameLine();
                if(StyledButton(t.Enabled?"Disable##library_disable":"Enable##library_enable",new Vector2(110,33)))
                { t.Enabled=!t.Enabled;MarkDirty(); }
                ImGui.Spacing();
                if(StyledButton("Remove selected title",new Vector2(220,32)))
                {
                    p.Titles.Remove(t);RepairTitleSelection(p);MarkDirty();
                }
                ImGui.SameLine();
                if(StyledButton("Edit colors & motion",new Vector2(220,32))) Navigate(2);
            }
        });
        ImGui.Spacing();
        if(StyledButton("Remove duplicate titles",new Vector2(235,32)))
        {
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var removed=p.Titles.RemoveAll(x=>!seen.Add($"{x.Text.Trim()}|{x.IsPrefix}|{x.ColorCount}|{x.StartColor}|{x.MiddleColor}|{x.EndColor}|{x.MiddlePosition:0.000}|{x.StartPosition:0.000}|{x.EndPosition:0.000}|{x.Effect}"));
            RepairTitleSelection(p);MarkDirty();notice=$"Removed {removed} duplicate titles.";
        }
        ImGui.SameLine();
        if(StyledButton("Remove all Wanderer titles",new Vector2(240,32))) confirmDeleteWanderer=!confirmDeleteWanderer;
        ImGui.SameLine();
        if(StyledButton("Remove ALL titles",new Vector2(195,32))) confirmDeleteAll=!confirmDeleteAll;
        if(confirmDeleteAll || confirmDeleteWanderer)
        {
            ImGui.TextColored(Warning,"Confirm deletion. This cannot be undone.");
            if(StyledButton("Confirm",new Vector2(110,32)))
            {
                var count=confirmDeleteAll?p.Titles.Count:p.Titles.Count(x=>x.Text.Trim().Equals("Wanderer",StringComparison.OrdinalIgnoreCase));
                if(confirmDeleteAll) p.Titles.Clear();
                else p.Titles.RemoveAll(x=>x.Text.Trim().Equals("Wanderer",StringComparison.OrdinalIgnoreCase));
                RepairTitleSelection(p);MarkDirty();
                notice=$"Removed {count} titles.";
                confirmDeleteAll=confirmDeleteWanderer=false;
            }
            ImGui.SameLine();
            if(StyledButton("Cancel",new Vector2(100,32))) confirmDeleteAll=confirmDeleteWanderer=false;
        }
    }
}
