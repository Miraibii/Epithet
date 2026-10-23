using System;
using System.Globalization;
using System.Numerics;

namespace Epithet;

internal static class ColorUtils
{
    public static bool TryParseHex(string? hex, out Vector3 color)
    {
        color = Vector3.One;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var raw = hex.Trim().TrimStart('#');
        if (raw.Length != 6 || !uint.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return false;
        color = new Vector3(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        return true;
    }

    public static string ToHex(Vector3 rgb) => $"#{(int)MathF.Round(Math.Clamp(rgb.X, 0, 1) * 255f):X2}{(int)MathF.Round(Math.Clamp(rgb.Y, 0, 1) * 255f):X2}{(int)MathF.Round(Math.Clamp(rgb.Z, 0, 1) * 255f):X2}";

    public static Vector3 Hue(float h)
    {
        var v = h - MathF.Floor(h);
        var r = Math.Clamp(MathF.Abs(v * 6 - 3) - 1, 0, 1);
        var g = Math.Clamp(2 - MathF.Abs(v * 6 - 2), 0, 1);
        var b = Math.Clamp(2 - MathF.Abs(v * 6 - 4), 0, 1);
        return new Vector3(r, g, b);
    }

    /// <summary>Exact title-only color sampling; never applies to nameplates' Name field.</summary>
    public static Vector3 TitleColor(Vector3 first, Vector3 middle, Vector3 last,
        int colorCount, string effect, int index, int count, float seconds, float middlePosition = 0.5f,
        float startPosition = 0f, float endPosition = 1f)
    {
        var x = count <= 1 ? 0f : (float)index / (count - 1);
        var baseColor = SamplePalette(first, middle, last, colorCount, x, middlePosition, startPosition, endPosition);
        return effect switch
        {
            // A full rainbow is intentionally independent of the chosen palette.
            "Rainbow" => Hue(x + seconds),
            "Pulse" => Vector3.Lerp(baseColor * .35f, baseColor, .5f + .5f * MathF.Sin(seconds * 6f)),
            "Wave" => SamplePalette(first, middle, last, colorCount,
                .5f + .5f * MathF.Sin(index * .55f - seconds * 6f), middlePosition, startPosition, endPosition),
            "Shift" => SamplePalette(first, middle, last, colorCount, x + seconds * .35f - MathF.Floor(x + seconds * .35f), middlePosition, startPosition, endPosition),
            "Static" => baseColor, // keep all selected palette stops, without motion
            _ => baseColor // Gradient
        };
    }

    public static Vector3 SamplePalette(Vector3 first, Vector3 middle, Vector3 last, int colorCount,
        float x, float middlePosition = 0.5f, float startPosition = 0f, float endPosition = 1f)
    {
        x = Math.Clamp(float.IsFinite(x) ? x : 0f, 0f, 1f);
        var mid = Math.Clamp(float.IsFinite(middlePosition) ? middlePosition : 0.5f, .05f, .95f);
        var start = Math.Clamp(float.IsFinite(startPosition) ? startPosition : 0f, 0f,
            colorCount == 3 ? mid - .03f : .97f);
        var end = Math.Clamp(float.IsFinite(endPosition) ? endPosition : 1f,
            colorCount == 3 ? mid + .03f : start + .03f, 1f);
        if (colorCount == 1) return first;
        if (x <= start) return first;
        if (x >= end) return last;
        if (colorCount == 3)
            return x <= mid
                ? Vector3.Lerp(first, middle, Math.Clamp((x - start) / (mid - start), 0f, 1f))
                : Vector3.Lerp(middle, last, Math.Clamp((x - mid) / (end - mid), 0f, 1f));
        return Vector3.Lerp(first, last, Math.Clamp((x - start) / (end - start), 0f, 1f));
    }
}
