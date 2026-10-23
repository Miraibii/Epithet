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
    private void BindCurrentCharacter()
    {
        var pc = Plugin.Objects.LocalPlayer;
        if (pc is null) { notice = "Log into a character first."; return; }
        var charName = pc.Name.TextValue;
        var homeWorldId = pc.HomeWorld.RowId;
        var world = pc.HomeWorld.ValueNullable;
        var resolvedWorldName = world.HasValue ? world.Value.Name.ExtractText() : string.Empty;
        if (string.IsNullOrWhiteSpace(resolvedWorldName)) resolvedWorldName = $"World #{homeWorldId}";

        // An older profile may have the correct character name but HomeWorldId = 0.
        // Prefer re-binding that profile rather than creating a new, empty one.
        var sameName = cfg.Profiles.Where(x => x.CharacterName.Equals(charName, StringComparison.OrdinalIgnoreCase)).ToArray();
        var exact = sameName.Where(x => x.HomeWorldId == homeWorldId)
            .OrderByDescending(x => x.Titles?.Count ?? 0).FirstOrDefault();
        var legacy = sameName.Where(x => x.HomeWorldId == 0 &&
                (string.Equals(x.WorldName, "Local", StringComparison.OrdinalIgnoreCase)
                || x.WorldName.StartsWith("World #", StringComparison.OrdinalIgnoreCase)
                || x.WorldName.Equals(resolvedWorldName, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.Titles?.Count ?? 0).FirstOrDefault();
        var profile = exact ?? legacy;
        if (profile is null)
        {
            profile = new CharacterProfile { CharacterName = charName, HomeWorldId = homeWorldId, WorldName = resolvedWorldName };
            cfg.Profiles.Add(profile);
        }
        // If a previous bind already created an empty exact-ID profile, restore
        // title presets from its unbound/legacy sibling without deleting either.
        if ((profile.Titles?.Count ?? 0) == 0 && legacy is not null && legacy.Titles is { Count: > 0 } && !ReferenceEquals(profile, legacy))
        {
            profile.Titles = legacy.Titles.Select(t =>
                JsonConvert.DeserializeObject<TitlePreset>(JsonConvert.SerializeObject(t))!).ToList();
            profile.ActiveTitleId = legacy.ActiveTitleId;
            profile.AnimationSpeed = legacy.AnimationSpeed;
            profile.HideVanillaTitle = legacy.HideVanillaTitle;
        }
        profile.Titles ??= new List<TitlePreset>();
        profile.Enabled = true;
        profile.CharacterName = charName;
        profile.HomeWorldId = homeWorldId;
        profile.WorldName = resolvedWorldName;
        RepairTitleSelection(profile);
        cfg.SelectedProfileId = profile.Id;
        notice = $"Bound {profile.DisplayName} with {profile.Titles.Count} saved title(s).";
        MarkDirty();
    }

    private static void RepairTitleSelection(CharacterProfile profile)
    {
        if (profile.Titles.All(t => t.Id != profile.ActiveTitleId))
            profile.ActiveTitleId = profile.Titles.FirstOrDefault()?.Id ?? "";
    }

    private void MarkDirty()
    {
        pendingSave = true;
        lastChange = DateTime.UtcNow;
        // Renderer checks for changed title configuration on the next framework
        // tick, issuing a single requested redraw. Avoid spamming redraws while
        // typing in fields or dragging color sliders.
    }

    private void SaveNow(bool notify)
    {
        plugin.SaveAndRefresh();
        pendingSave = false;
        if (notify) notice = "Saved and nameplates refreshed.";
    }
}
