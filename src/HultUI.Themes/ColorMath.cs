using HultUI.Core.Primitives;

namespace HultUI.Themes;

/// <summary>
/// The colour arithmetic the ramps are built from: HSL round-tripping, opacity scaling,
/// and relative luminance.
/// </summary>
/// <remarks>
/// HSL rather than a perceptual space (OKLCH, LAB) on purpose. The ramp profiles in
/// <see cref="Ramp"/> were calibrated against a hand-tuned sRGB scale, so the numbers only
/// mean what they mean in the space they were measured in; swapping the space would need
/// the profiles re-derived, and the visible gain over a nine-step ramp of a single hue is
/// small. Anything that needs to be perceptually exact is a literal in the theme instead.
/// </remarks>
public static class ColorMath
{
    /// <summary>The colour at <paramref name="opacity"/> (0..1) — what CSS writes as
    /// <c>color-mix(in srgb, X N%, transparent)</c>, which for a source-over composite is
    /// exactly the colour at that alpha.</summary>
    public static Color Alpha(Color c, double opacity) =>
        new(c.R, c.G, c.B, (byte)Math.Clamp(opacity * 255.0, 0, 255));

    /// <summary>The colour re-lit to <paramref name="lightness"/> (0..1), keeping its hue.
    /// Saturation is scaled rather than kept: a hue held at full saturation goes chalky as it
    /// approaches white and muddy as it approaches black, so the extremes ease off.</summary>
    public static Color Tone(Color c, double lightness, double saturationScale = 1.0)
    {
        var (h, s, _) = ToHsl(c);
        return FromHsl(h, Math.Clamp(s * saturationScale, 0, 1), Math.Clamp(lightness, 0, 1));
    }

    /// <summary>Mixes <paramref name="amount"/> (0..1) of <paramref name="b"/> into
    /// <paramref name="a"/>, both taken as opaque.</summary>
    public static Color Mix(Color a, Color b, double amount)
    {
        double t = Math.Clamp(amount, 0, 1);
        return new(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>Relative luminance (0..1), sRGB-gamma corrected — the WCAG definition, so it
    /// can be compared against a contrast threshold rather than eyeballed.</summary>
    public static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>Whichever of <paramref name="dark"/> and <paramref name="light"/> reads
    /// better on <paramref name="background"/>.</summary>
    public static Color ReadableOn(Color background, Color dark, Color light) =>
        Luminance(background) > 0.45 ? dark : light;

    public static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, h = 0, s = 0, d = max - min;
        if (d > 1e-6)
        {
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }
        return (h, s, l);
    }

    public static Color FromHsl(double h, double s, double l)
    {
        double r, g, b;
        if (s < 1e-6) { r = g = b = l; }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3);
        }
        return new((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}
