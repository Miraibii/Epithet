using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using Epithet.Models;

namespace Epithet;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 11;
    public string SelectedProfileId = "";
    public bool ShowAnimationPreview = true;
    public bool ReduceUiMotion = false;
    public int AnimationFps = 30;
    public bool RenderInGame = true;
    // Workaround for the native NamePlate update animation blink.
    public bool EnableAntiFlashing = true;
    // Diagnostic safe mode: leave managed/static titles intact, disable direct native
    // per-frame nameplate writes (which can cause flicker after game changes).
    public bool DisableNativeAnimationWriter = false;
    // Automatic sync compatibility bridge for Lightless and other Honorific-capable transports.
    // The Honorific plugin itself is NOT required. Users may opt out of sync.
    public bool EnableHonorificCompatibility = true;
    // Test-only: send strict Honorific fields with no Epithet JSON extension.
    // This does not change saved title presets or the local Epithet renderer.
    public bool UseLegacyHonorificWireFormat = false;
    public bool ShowRemoteTitles = true;
    public bool ApplyProfilesAutomatically = true;
    public bool HideVanillaOther;
    public bool ConfirmPaletteDeletion = true;
    public List<TitlePalette> Palettes = new()
    {
        TitlePalette.Create("Emberglow", 3, "#FF4141", "#FFE55D", "#FF9B35", true),
        TitlePalette.Create("Ocean Glass", 3, "#548BFF", "#86CFFF", "#61E4CA", true),
        TitlePalette.Create("Lilac Haze", 3, "#9A7CFF", "#DAACFF", "#FF92C5", true),
        TitlePalette.Create("Crimson", 1, "#FF4949", "#FF4949", "#FF4949", true),
    };
    public List<CharacterProfile> Profiles = new();

    private static List<TitlePalette> DefaultPalettes() => new()
    {
        TitlePalette.Create("Emberglow", 3, "#FF4141", "#FFE55D", "#FF9B35", true),
        TitlePalette.Create("Ocean Glass", 3, "#548BFF", "#86CFFF", "#61E4CA", true),
        TitlePalette.Create("Lilac Haze", 3, "#9A7CFF", "#DAACFF", "#FF92C5", true),
        TitlePalette.Create("Crimson", 1, "#FF4949", "#FF4949", "#FF4949", true),
    };

    public CharacterProfile SelectedProfile
    {
        get
        {
            Profiles ??= new List<CharacterProfile>();
            if (Profiles.Count == 0) Profiles.Add(new CharacterProfile());
            var profile = Profiles.Find(p => p.Id == SelectedProfileId) ?? Profiles[0];
            SelectedProfileId = profile.Id;
            return profile;
        }
    }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    // Upgrade older profile lists without modifying user's actual title choices.
    // Deletion and bulk cleaning are intentional UI actions rather than a silent migration.
    public void Normalize()
    {
        Profiles ??= new List<CharacterProfile>();
        // New native-nameplate stability limit: the UI preview still animates at
        // the client's frame rate, but a full game nameplate refresh is capped.
        if (Version < 7 && AnimationFps < 16) AnimationFps = 30;
        AnimationFps = Math.Clamp(AnimationFps, 8, 60);
        // Clean up only EMPTY auto-generated placeholders. Any profile containing
        // user titles is deliberately retained, so migration never deletes content.
        Profiles.RemoveAll(p => p is not null && p.HomeWorldId == 0 && p.Titles is { Count: 0 }
            && p.CharacterName.Equals("Adventurer",StringComparison.OrdinalIgnoreCase)
            && p.WorldName.Equals("Local",StringComparison.OrdinalIgnoreCase));
        foreach (var profile in Profiles)
        {
            profile.Titles ??= new List<TitlePreset>();
            var usedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var preset in profile.Titles)
            {
                preset.ColorCount = Math.Clamp(preset.ColorCount, 1, 3);
                preset.MiddlePosition = Math.Clamp(float.IsFinite(preset.MiddlePosition) ? preset.MiddlePosition : 0.5f, 0.05f, 0.95f);
                preset.StartPosition = Math.Clamp(float.IsFinite(preset.StartPosition) ? preset.StartPosition : 0f,
                    0f, preset.ColorCount == 3 ? preset.MiddlePosition - .03f : .97f);
                preset.EndPosition = Math.Clamp(float.IsFinite(preset.EndPosition) ? preset.EndPosition : 1f,
                    preset.ColorCount == 3 ? preset.MiddlePosition + .03f : preset.StartPosition + .03f, 1f);
                if (!ColorUtils.TryParseHex(preset.StartColor, out _)) preset.StartColor = "#8DB7FF";
                if (!ColorUtils.TryParseHex(preset.MiddleColor, out _)) preset.MiddleColor = "#FFD45F";
                if (!ColorUtils.TryParseHex(preset.EndColor, out _)) preset.EndColor = "#7FE0D5";
                if (!ColorUtils.TryParseHex(preset.GlowColor, out _)) preset.GlowColor = "#BCA7FF";
                if (preset.Effect is not ("Static" or "Gradient" or "Rainbow" or "Pulse" or "Wave" or "Shift")) preset.Effect = "Static";
                if (string.IsNullOrWhiteSpace(preset.Id) || !usedIds.Add(preset.Id))
                {
                    preset.Id = Guid.NewGuid().ToString("N");
                    usedIds.Add(preset.Id);
                }
            }
            if (!float.IsFinite(profile.AnimationSpeed) || profile.AnimationSpeed < .25f || profile.AnimationSpeed > 6f)
                profile.AnimationSpeed = 1.5f;
            if (profile.Titles.Count > 0 && !profile.Titles.Exists(x => x.Id == profile.ActiveTitleId))
                profile.ActiveTitleId = profile.Titles[0].Id;
        }
        Palettes ??= new List<TitlePalette>();
        if (Palettes.Count == 0) Palettes.AddRange(DefaultPalettes());
        // Remove the visually redundant legacy preset and repair invalid entries before deduping.
        Palettes.RemoveAll(x => x is null || string.IsNullOrWhiteSpace(x.Name) ||
            !ColorUtils.TryParseHex(x.StartColor, out _) || !ColorUtils.TryParseHex(x.MiddleColor, out _) ||
            !ColorUtils.TryParseHex(x.EndColor, out _));
        Palettes.RemoveAll(x => x.IsBuiltIn && x.Name.Equals("Royal Violet", StringComparison.OrdinalIgnoreCase));
        foreach (var palette in Palettes)
        {
            palette.ColorCount = Math.Clamp(palette.ColorCount, 1, 3);
            palette.MiddlePosition = Math.Clamp(float.IsFinite(palette.MiddlePosition) ? palette.MiddlePosition : 0.5f, 0.05f, 0.95f);
            palette.StartPosition = Math.Clamp(float.IsFinite(palette.StartPosition) ? palette.StartPosition : 0f,
                0f, palette.ColorCount == 3 ? palette.MiddlePosition - .03f : .97f);
            palette.EndPosition = Math.Clamp(float.IsFinite(palette.EndPosition) ? palette.EndPosition : 1f,
                palette.ColorCount == 3 ? palette.MiddlePosition + .03f : palette.StartPosition + .03f, 1f);
            palette.Name = palette.Name.Trim();
            if (palette.Name.Length > 42) palette.Name = palette.Name[..42];
        }
        // Ensure the four distinct starter presets remain available on older configurations.
        foreach (var builtIn in DefaultPalettes())
            if (!Palettes.Any(p => p.IsBuiltIn && p.Name.Equals(builtIn.Name, StringComparison.OrdinalIgnoreCase)))
                Palettes.Add(builtIn);
        var uniquePaletteKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Palettes = Palettes.Where(p => uniquePaletteKeys.Add(string.Join("|", new[]
        {
            p.Name.Trim().ToUpperInvariant(),
            p.ColorCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            p.StartColor.ToUpperInvariant(), p.MiddleColor.ToUpperInvariant(), p.EndColor.ToUpperInvariant(),
            p.MiddlePosition.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            p.StartPosition.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            p.EndPosition.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
        }))).ToList();
        var usedPaletteIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var palette in Palettes)
        {
            if (string.IsNullOrWhiteSpace(palette.Id) || !usedPaletteIds.Add(palette.Id))
            {
                palette.Id = Guid.NewGuid().ToString("N");
                usedPaletteIds.Add(palette.Id);
            }
        }
        if (Version < 10) EnableHonorificCompatibility = true; // 1.9 introduces automatic sync compatibility, enabled by default.
        if (Version < 11) ConfirmPaletteDeletion = true;
        Version = 11;
    }
}
