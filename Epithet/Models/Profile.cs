using System;
using System.Collections.Generic;
using System.Linq;

namespace Epithet.Models;

[Serializable]
public sealed class TitlePreset
{
    public string Id = Guid.NewGuid().ToString("N");
    public string Text = "Wanderer";
    public bool IsPrefix = true;
    public string StartColor = "#8DB7FF";
    public string EndColor = "#7FE0D5";
    public string MiddleColor = "#FFD45F";
    // Position of the middle stop in a three-color gradient (0..1, clamped away from the ends).
    public float MiddlePosition = 0.5f;
    public float StartPosition = 0f;
    public float EndPosition = 1f;
    // One solid color, two end stops, or a three-stop title gradient.
    public int ColorCount = 2;
    public bool UseGlow;
    public string GlowColor = "#BCA7FF";
    public string Effect = "Static";
    public bool Enabled = true;
    // Optional conditions: zero or empty means any.
    public uint JobId = 0;
    public ushort TerritoryId = 0;
    public bool OnlyInCombat;
    public bool OnlyOutOfCombat;
    public bool Rotate = false;
    public int RotationSeconds = 30;
}

[Serializable]
public sealed class CharacterProfile
{
    public string Id = Guid.NewGuid().ToString("N");
    public string CharacterName = "Adventurer";
    public string WorldName = "Local";
    public uint HomeWorldId;
    public bool Enabled = true;
    public string ActiveTitleId = "";
    public List<TitlePreset> Titles = new();
    // Controls title animation only; the game character name is NEVER modified.
    public float AnimationSpeed = 1.5f;
    public bool HideVanillaTitle;

    public string DisplayName => $"{CharacterName} @ {WorldName}";
    // Editing selection must never jump away when a preset is disabled.
    public TitlePreset? SelectedTitle => Titles?.FirstOrDefault(t => t.Id == ActiveTitleId)
                                         ?? Titles?.FirstOrDefault();
    public TitlePreset? ActiveTitle => Titles?.FirstOrDefault(t => t.Id == ActiveTitleId && t.Enabled)
                                       ?? Titles?.FirstOrDefault(t => t.Enabled);
}


[Serializable]
public sealed class TitlePalette
{
    public string Id = Guid.NewGuid().ToString("N");
    public string Name = "Custom palette";
    public int ColorCount = 2;
    public string StartColor = "#FF4141";
    public string MiddleColor = "#FFE55D";
    public string EndColor = "#FF9B35";
    public float MiddlePosition = 0.5f;
    public float StartPosition = 0f;
    public float EndPosition = 1f;
    public bool IsBuiltIn;

    public static TitlePalette Create(string name, int count, string start, string middle, string end, bool builtIn = false, float middlePosition = 0.5f, float startPosition = 0f, float endPosition = 1f)
        => new() { Name = name, ColorCount = count, StartColor = start, MiddleColor = middle, EndColor = end, MiddlePosition = Math.Clamp(middlePosition, 0.05f, 0.95f), StartPosition = Math.Clamp(startPosition, 0f, 0.94f), EndPosition = Math.Clamp(endPosition, 0.06f, 1f), IsBuiltIn = builtIn };
}
