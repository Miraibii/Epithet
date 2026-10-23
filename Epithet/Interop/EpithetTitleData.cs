using System;
using System.Linq;
using System.Numerics;
using Epithet.Models;

namespace Epithet.Interop;

/// <summary>
/// Epithet's own IPC contract. It works without Honorific and carries all Epithet
/// title properties; IPC is local to the current game process, not a network transport.
/// </summary>
public sealed class EpithetTitleData
{
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "";
    public bool IsPrefix { get; set; } = true;
    public string StartColor { get; set; } = "#8DB7FF";
    public string MiddleColor { get; set; } = "#FFD45F";
    public string EndColor { get; set; } = "#7FE0D5";
    public int ColorCount { get; set; } = 2;
    public float MiddlePosition { get; set; } = 0.5f;
    public float StartPosition { get; set; } = 0f;
    public float EndPosition { get; set; } = 1f;
    public string Effect { get; set; } = "Static";
    public float AnimationSpeed { get; set; } = 1.5f;
    public bool GlowEnabled { get; set; }
    public string GlowColor { get; set; } = "#BCA7FF";

    public bool IsSafe() => Version == 1
        && !string.IsNullOrWhiteSpace(Title) && Title.Length <= 32 && Title.All(c => !char.IsControl(c))
        && ColorCount is >= 1 and <= 3
        && float.IsFinite(MiddlePosition) && MiddlePosition is >= 0.05f and <= 0.95f
        && float.IsFinite(StartPosition) && StartPosition >= 0f && StartPosition <= (ColorCount == 3 ? MiddlePosition - .03f : .97f)
        && float.IsFinite(EndPosition) && EndPosition <= 1f && EndPosition >= (ColorCount == 3 ? MiddlePosition + .03f : StartPosition + .03f)
        && float.IsFinite(AnimationSpeed) && AnimationSpeed is >= 0.25f and <= 6f
        && (Effect is "Static" or "Gradient" or "Rainbow" or "Pulse" or "Wave" or "Shift")
        && ColorUtils.TryParseHex(StartColor, out _) && ColorUtils.TryParseHex(MiddleColor, out _)
        && ColorUtils.TryParseHex(EndColor, out _)
        && (!GlowEnabled || ColorUtils.TryParseHex(GlowColor, out _));

    public static EpithetTitleData FromPreset(TitlePreset preset, CharacterProfile? profile = null) => new()
    {
        Title = preset.Text,
        IsPrefix = preset.IsPrefix,
        StartColor = preset.StartColor,
        MiddleColor = preset.MiddleColor,
        EndColor = preset.EndColor,
        ColorCount = Math.Clamp(preset.ColorCount, 1, 3),
        MiddlePosition = Math.Clamp(preset.MiddlePosition, 0.05f, 0.95f),
        StartPosition = preset.StartPosition,
        EndPosition = preset.EndPosition,
        Effect = EpithetExtension.ValidEffect(preset.Effect),
        AnimationSpeed = Math.Clamp(profile?.AnimationSpeed ?? 1.5f, 0.25f, 6f),
        GlowEnabled = preset.UseGlow,
        GlowColor = preset.GlowColor,
    };

    public HonorificTitle ToInteropTitle()
    {
        ColorUtils.TryParseHex(StartColor, out var first);
        ColorUtils.TryParseHex(EndColor, out var last);
        Vector3? glow = GlowEnabled && ColorUtils.TryParseHex(GlowColor, out var edge) ? edge : null;
        return new HonorificTitle
        {
            Title = Title,
            IsPrefix = IsPrefix,
            Color = first,
            Color3 = last,
            Glow = glow,
            Extension = new EpithetExtension
            {
                Version = 1,
                AnimationSpeed = AnimationSpeed,
                TitleEffect = EpithetExtension.ValidEffect(Effect),
                TitleStartColor = StartColor,
                TitleMiddleColor = MiddleColor,
                TitleMiddlePosition = MiddlePosition,
                TitleStartPosition = StartPosition,
                TitleEndPosition = EndPosition,
                TitleEndColor = EndColor,
                TitleColorCount = ColorCount,
                TitleGlowEnabled = GlowEnabled,
                TitleGlowColor = GlowColor,
            }
        };
    }

    public static EpithetTitleData FromInterop(HonorificTitle payload)
    {
        var ext = payload.Extension;
        var start = ext?.TitleStartColor ?? ColorUtils.ToHex(payload.Color ?? Vector3.One);
        var end = ext?.TitleEndColor ?? (payload.Color3.HasValue ? ColorUtils.ToHex(payload.Color3.Value) : start);
        var middle = ext?.TitleMiddleColor ?? start;
        var count = ext?.TitleColorCount ?? (payload.GradientColourSet == -1 && payload.Color3.HasValue ? 2 : 1);
        var effect = ext?.TitleEffect ?? (payload.GradientColourSet == 0 ? "Rainbow" : payload.GradientAnimationStyle switch
        {
            0 => "Pulse",
            1 => "Wave",
            _ => count > 1 ? "Gradient" : "Static"
        });
        var glowEnabled = ext?.TitleGlowEnabled ?? (payload.Glow.HasValue && payload.GradientColourSet != -1);
        return new EpithetTitleData
        {
            Title = payload.Title,
            IsPrefix = payload.IsPrefix,
            StartColor = start,
            MiddleColor = middle,
            EndColor = end,
            ColorCount = Math.Clamp(count, 1, 3),
            MiddlePosition = Math.Clamp(ext?.TitleMiddlePosition ?? 0.5f, 0.05f, 0.95f),
            StartPosition = ext?.TitleStartPosition ?? 0f,
            EndPosition = ext?.TitleEndPosition ?? 1f,
            Effect = EpithetExtension.ValidEffect(effect),
            AnimationSpeed = Math.Clamp(ext?.AnimationSpeed ?? 1.5f, 0.25f, 6f),
            GlowEnabled = glowEnabled,
            GlowColor = ext?.TitleGlowColor ?? (payload.Glow.HasValue ? ColorUtils.ToHex(payload.Glow.Value) : "#BCA7FF"),
        };
    }
}
