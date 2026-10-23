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
    private void DrawCharacters()
    {
        Hero("CHARACTER PROFILES","Unique titles for every adventurer.",
            "Manage individual titles and effects for each real character.","P",152f);
        ImGui.Spacing();
        var visible=cfg.Profiles.Where(IsRealProfile)
            .Where(x=>string.IsNullOrWhiteSpace(profileSearch)
                || x.DisplayName.Contains(profileSearch,StringComparison.OrdinalIgnoreCase)).ToArray();
        Card("##profile_library",Math.Clamp(145+visible.Length*45,215,355),()=>
        {
            ImGui.TextColored(Mint,"CHARACTER PROFILES");
            ImGui.Spacing();
            if(StyledButton("Bind logged-in character",new Vector2(210,37))) BindCurrentCharacter();
            ImGui.SameLine();
            if(StyledButton("+ Add profile",new Vector2(138,37)))
            {
                var p=new CharacterProfile { CharacterName="New Character",WorldName="Local" };
                cfg.Profiles.Add(p);cfg.SelectedProfileId=p.Id;MarkDirty();
            }
            ImGui.Spacing();
            ImGui.SetNextItemWidth(Math.Min(335,ImGui.GetContentRegionAvail().X));
            StudioSearch("##character_search","Search characters...",ref profileSearch,65);
            ImGui.Spacing();
            if(visible.Length==0)
            {
                ImGui.TextColored(Muted,"No character profiles to display.");
                ImGui.TextWrapped("Bind your logged-in character or create a profile. Unused Adventurer @ Local placeholders are hidden.");
            }
            else
            {
                foreach(var p in visible)
                {
                    ImGui.PushID(p.Id);
                    try
                    {
                        var label=(cfg.SelectedProfileId==p.Id?"●  ":"◯  ")+p.DisplayName;
                        if(StyledButton(label,new Vector2(-1,38)))
                        {
                            cfg.SelectedProfileId=p.Id;MarkDirty();
                        }
                    }
                    finally { ImGui.PopID(); }
                }
            }
        });
        ImGui.Spacing();
        var current=cfg.Profiles.FirstOrDefault(x=>x.Id==cfg.SelectedProfileId && IsRealProfile(x))
                    ?? cfg.Profiles.FirstOrDefault(IsRealProfile);
        if(current==null) return;
        Card("##profile_editor",495,()=>
        {
            ImGui.TextColored(White,current.DisplayName);
            StatusPill(current.Enabled?"PROFILE ENABLED":"PROFILE DISABLED",current.Enabled?Green:Muted);
            ImGui.Spacing();
            if(StudioInput("##char_name", "Character name",ref current.CharacterName,65)) MarkDirty();
            if(StudioInput("##char_world", "Home world / server",ref current.WorldName,65)) MarkDirty();
            ColoredWrapped(Muted, "Examples: Raiden, Odin, Shiva, Phoenix. When you bind the logged-in character, Epithet tries to fill the real home world automatically.");
            var availableTitles = current.Titles.ToArray();
            if (availableTitles.Length > 0)
            {
                ImGui.TextColored(Muted, "ASSIGNED TITLE PRESET");
                var choices = availableTitles.Select(x => x.Text).ToArray();
                if (StudioSelect("##char_active_title", current.SelectedTitle?.Text ?? "None", choices, out var idx)
                    && idx >= 0 && idx < availableTitles.Length)
                {
                    current.ActiveTitleId = availableTitles[idx].Id;
                    MarkDirty();
                }
            }

            if(StudioToggle("##profile_enabled", "Profile enabled",ref current.Enabled)) MarkDirty();
            if(StudioToggle("##hide_vanilla", "Hide vanilla title when Epithet is inactive",ref current.HideVanillaTitle)) MarkDirty();
            ImGui.Spacing();
            if(StyledButton("Remove profile",new Vector2(172,33)))
            {
                cfg.Profiles.Remove(current);
                cfg.SelectedProfileId=cfg.Profiles.FirstOrDefault(IsRealProfile)?.Id??"";
                MarkDirty();
            }
        });
        ImGui.Spacing();
        Card("##char_notice",99,()=>
        {
            ImGui.TextColored(Mint,"PERSONALIZE EACH CHARACTER");
            ImGui.TextWrapped("Each profile can use its own title, colors and effects. Player names retain their original FFXIV appearance.");
        });
        // Intentionally NO nameplate preview on Characters.
    }
}
