using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Epithet.Models;
using Newtonsoft.Json;

namespace Epithet.Interop;

/// <summary>
/// Honorific 3.2 TitleData JSON envelope. Unknown JSON properties are ignored by
/// stock Honorific; a compatible sync client can forward the entire JSON blob.
/// No Honorific source is copied here.
/// </summary>
public sealed class HonorificTitle
{
    public string Title { get; set; } = "";
    public bool IsPrefix { get; set; } = true;
    public bool IsOriginal { get; set; }
    public Vector3? Color { get; set; }
    public Vector3? Glow { get; set; }
    public Vector3? Color3 { get; set; }
    public int? GradientColourSet { get; set; }
    public int? GradientAnimationStyle { get; set; }

    // Optional Epithet-specific payload; never required to interpret a normal
    // Honorific title. Receivers without Epithet simply ignore this property.
    [JsonProperty("Epithet", NullValueHandling = NullValueHandling.Ignore)]
    public EpithetExtension? Extension { get; set; }

    public static HonorificTitle FromPreset(TitlePreset preset, CharacterProfile? profile = null)
    {
        ColorUtils.TryParseHex(preset.StartColor, out var color);
        Vector3? glow = null;
        if (preset.UseGlow && ColorUtils.TryParseHex(preset.GlowColor, out var g)) glow = g;

        // Honorific understands custom TWO-color gradients natively using
        // GradientColourSet = -1, Glow = stop A, Color3 = stop B.
        // This is intentionally separate from Epithet's 1/2/3-stop extension.
        // A stock Honorific client cannot display an arbitrary middle stop or
        // the exact Epithet speed setting; it can display a two-color approximation.
        var data = new HonorificTitle
        {
            Title = preset.Text,
            IsPrefix = preset.IsPrefix,
            Color = color,
            Glow = glow,
        };
        if (preset.Effect == "Rainbow")
        {
            // Standard Honorific Pride Rainbow with its own timing.
            data.GradientColourSet = 0;
            data.GradientAnimationStyle = 1; // Wave
        }
        else if (preset.ColorCount >= 2 && !preset.UseGlow)
        {
            ColorUtils.TryParseHex(preset.EndColor, out var tail);
            data.GradientColourSet = -1;
            data.Glow = color;  // Honorific's first custom gradient stop
            data.Color3 = tail; // Honorific's second custom gradient stop
            data.GradientAnimationStyle = preset.Effect switch
            {
                "Pulse" => 0, // Honorific.Pulse
                "Wave" or "Shift" => 1, // Honorific.Wave
                _ => 2 // Honorific.Static
            };
        }
        if (profile != null)
        {
            data.Extension = new EpithetExtension
            {
                Version = 1,
                AnimationSpeed = Math.Clamp(profile.AnimationSpeed, .25f, 6f),
                TitleEffect = EpithetExtension.ValidEffect(preset.Effect),
                TitleStartColor = preset.StartColor,
                TitleEndColor = preset.EndColor,
                TitleMiddleColor = preset.MiddleColor,
                TitleMiddlePosition = Math.Clamp(preset.MiddlePosition, 0.05f, 0.95f),
                TitleStartPosition = preset.StartPosition,
                TitleEndPosition = preset.EndPosition,
                TitleColorCount = Math.Clamp(preset.ColorCount, 1, 3),
                TitleGlowEnabled = preset.UseGlow,
                TitleGlowColor = preset.GlowColor,
            };
        }
        return data;
    }

    private static bool SafeVector(Vector3? value) => !value.HasValue ||
        (float.IsFinite(value.Value.X) && float.IsFinite(value.Value.Y) && float.IsFinite(value.Value.Z) &&
         value.Value.X >= 0 && value.Value.X <= 1 && value.Value.Y >= 0 && value.Value.Y <= 1 &&
         value.Value.Z >= 0 && value.Value.Z <= 1);

    public bool IsSafe() => !IsOriginal && !string.IsNullOrWhiteSpace(Title)
        && Title.Length <= 32 && Title.All(c => !char.IsControl(c))
        && SafeVector(Color) && SafeVector(Glow) && SafeVector(Color3)
        && (!GradientColourSet.HasValue || GradientColourSet.Value is >= -1 and <= 2048)
        && (!GradientAnimationStyle.HasValue || GradientAnimationStyle.Value is >= 0 and <= 2)
        && (Extension == null || Extension.IsSafe());
}

/// <summary>
/// Optional metadata transported inside an Honorific JSON message. Epithet peers
/// can render animated titles and names; Honorific peers continue to see the base
/// title. This works only if the existing sync client forwards unknown JSON fields.
/// </summary>
public sealed class EpithetExtension
{
    public int Version { get; set; } = 1;
    public float AnimationSpeed { get; set; } = 1.5f;
    public string TitleEffect { get; set; } = "Static";
    public string TitleStartColor { get; set; } = "#FFFFFF";
    public string TitleEndColor { get; set; } = "#FFFFFF";
    public string TitleMiddleColor { get; set; } = "#FFD45F";
    public float TitleMiddlePosition { get; set; } = 0.5f;
    public float? TitleStartPosition { get; set; }
    public float? TitleEndPosition { get; set; }
    public int TitleColorCount { get; set; } = 2;
    // Separate title outline from Honorific's overloaded Glow field (which can
    // represent the first stop of a dual-color gradient). Nullable for backward
    // compatibility with Epithet peers running the older extension.
    public bool? TitleGlowEnabled { get; set; }
    public string? TitleGlowColor { get; set; }

    public static string ValidEffect(string? value) => value is "Static" or "Gradient" or "Rainbow" or "Pulse" or "Wave" or "Shift" ? value : "Static";

    public bool IsSafe()
    {
        return Version == 1 && float.IsFinite(AnimationSpeed) && AnimationSpeed >= .25f && AnimationSpeed <= 6f
            && TitleEffect == ValidEffect(TitleEffect)
            && TitleColorCount is >= 1 and <= 3
            && ColorUtils.TryParseHex(TitleStartColor, out _)
            && ColorUtils.TryParseHex(TitleMiddleColor, out _)
            && float.IsFinite(TitleMiddlePosition) && TitleMiddlePosition is >= 0.05f and <= 0.95f
            && (!TitleStartPosition.HasValue || (float.IsFinite(TitleStartPosition.Value) && TitleStartPosition.Value >= 0f && TitleStartPosition.Value <= (TitleColorCount == 3 ? TitleMiddlePosition - .03f : .97f)))
            && (!TitleEndPosition.HasValue || (float.IsFinite(TitleEndPosition.Value) && TitleEndPosition.Value <= 1f && TitleEndPosition.Value >= (TitleColorCount == 3 ? TitleMiddlePosition + .03f : (TitleStartPosition ?? 0f) + .03f)))
            && ColorUtils.TryParseHex(TitleEndColor, out _)
            && (TitleGlowColor == null || ColorUtils.TryParseHex(TitleGlowColor, out _))
            && (TitleGlowEnabled is not true || ColorUtils.TryParseHex(TitleGlowColor, out _));
    }
}
