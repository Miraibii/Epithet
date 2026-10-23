using System;
using System.Numerics;

namespace Epithet;

/// <summary>HSV editor coordinates, independent of the gameplay nameplate renderer.</summary>
internal static class ColorPickerMath
{
    public static Vector3 HsvToRgb(float hue, float saturation, float value)
    {
        hue = float.IsFinite(hue) ? hue : 0f;
        saturation = Math.Clamp(float.IsFinite(saturation) ? saturation : 0f, 0f, 1f);
        value = Math.Clamp(float.IsFinite(value) ? value : 0f, 0f, 1f);
        return Vector3.Lerp(Vector3.One, ColorUtils.Hue(hue), saturation) * value;
    }

    public static (float hue, float saturation, float value) RgbToHsv(Vector3 rgb)
    {
        var r = Math.Clamp(rgb.X, 0f, 1f);
        var g = Math.Clamp(rgb.Y, 0f, 1f);
        var b = Math.Clamp(rgb.Z, 0f, 1f);
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var delta = max - min;
        if (delta <= 0.000001f) return (0f, 0f, max);
        var hue = max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2f : (r - g) / delta + 4f;
        hue = (hue / 6f + 1f) % 1f;
        return (hue, max <= 0f ? 0f : delta / max, max);
    }
}
