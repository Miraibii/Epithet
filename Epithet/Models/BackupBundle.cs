using System.Collections.Generic;

namespace Epithet.Models;

/// <summary>Portable JSON backup of all local title profiles and saved palettes.</summary>
public sealed class BackupBundle
{
    public int FormatVersion = 1;
    public List<CharacterProfile> Profiles = new();
    public List<TitlePalette> Palettes = new();
}
