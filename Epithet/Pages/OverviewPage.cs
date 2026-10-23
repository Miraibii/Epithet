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
    private void DrawOverview()
    {
        Hero("EPITHET / TITLE STUDIO","Make your title unmistakably yours.",
            "Customize beautiful colors, motion and effects. More features are on the way.","✦");
        ImGui.Spacing();
        var width=ImGui.GetContentRegionAvail().X;
        var profiles=cfg.Profiles.Count(IsRealProfile);
        var presetCount=cfg.Profiles.Where(IsRealProfile).Sum(x=>x.Titles.Count);
        var cols=width<690?2:4;
        var cardWidth=(width-(cols-1)*10)/cols;
        SummaryCard("##sum_profiles",cardWidth,"P","Profiles",profiles.ToString(),"Individual identities",new Vector4(.16f,.28f,.16f,1f));
        ImGui.SameLine(0,10);
        SummaryCard("##sum_titles",cardWidth,"T","Title presets",presetCount.ToString(),"Your saved titles",new Vector4(.33f,.24f,.11f,1f));
        if(cols==2) ImGui.Spacing(); else ImGui.SameLine(0,10);
        SummaryCard("##sum_colors",cardWidth,"✦","Color stops","UP TO 3","Custom gradients",new Vector4(.32f,.16f,.25f,1f));
        ImGui.SameLine(0,10);
        SummaryCard("##sum_fx",cardWidth,"✧","Animated effects","4+","Rainbow and more",new Vector4(.23f,.17f,.32f,1f));
        ImGui.Spacing();
        Card("##overview_actions",178,()=>
        {
            ImGui.TextColored(White,"✦  QUICK ACTIONS");
            ImGui.TextColored(Muted,"Jump straight into title creation and character settings.");
            ImGui.Spacing();
            if(StyledButton("Edit my title",new Vector2(147,38))) Navigate(1);
            ImGui.SameLine();
            if(StyledButton("Colors & effects",new Vector2(167,38))) Navigate(2);
            ImGui.SameLine();
            if(StyledButton("Character profiles",new Vector2(177,38))) Navigate(3);
            ImGui.Spacing();
            if(StyledButton("Bind current character",new Vector2(200,36))) BindCurrentCharacter();
        });
        ImGui.Spacing();
        Card("##overview_sync",111,()=>
        {
            ImGui.TextColored(Mint,"STANDALONE & SYNC COMPATIBLE");
            ImGui.TextWrapped("Epithet renders titles without Honorific. Other players need a compatible receiver and a connected sync service to see them.");
            ImGui.TextColored(Muted,"Full Epithet effect delivery still needs two-client verification.");
        });
        // Intentionally NO nameplate preview on Overview.
    }

    private static bool IsRealProfile(CharacterProfile p)
        => !(p.CharacterName.Equals("Adventurer",StringComparison.OrdinalIgnoreCase)
             && p.WorldName.Equals("Local",StringComparison.OrdinalIgnoreCase)
             && p.HomeWorldId==0 && p.Titles.Count==0);

    private void DrawStudioHero()
    {
        Hero("EPITHET / TITLE STUDIO","Make your title unmistakably yours.",
            "From quiet gradients to vivid motion. More features are on the way.","✦");
    }

    private static void StatColumn(string title,string value)
    {
        ImGui.Spacing(); ImGui.TextColored(Muted,"   "+title);
        ImGui.TextColored(White,"   "+value);
    }
}
