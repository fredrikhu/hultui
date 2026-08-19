using HultUI.Core.Primitives;

namespace HultUI.Themes;

/// <summary>
/// A nine-step tonal scale of one hue, indexed the way CSS design systems number them:
/// <c>100</c> at one end, <c>900</c> at the other.
/// </summary>
/// <remarks>
/// <para><b>Which end is which depends on the theme, and that is the point.</b> Step 100 is
/// always the step <i>furthest</i> from the app's background and step 900 the one closest to
/// it. On a dark theme that runs light → dark; on a light theme it runs dark → light. Call
/// sites can then mean one thing everywhere: <c>Neutral900</c> is the hover tint that barely
/// lifts off the ground, <c>Neutral300</c> is text you can read, <c>Accent900</c> is the
/// backing behind a selected row and <c>Accent200</c> is the label on it. Number the ramp the
/// other way for a light theme and every one of those inverts — a selected row comes out
/// near-black with near-black text.</para>
///
/// <para>The profiles below are measured off the hand-tuned blurple ramp the Nocturne design
/// system shipped with: lightness falls across the steps while saturation peaks in the middle
/// and eases off at both ends, which is what keeps a scale from going chalky as it approaches
/// white and muddy as it approaches black. Reproducing that curve for an arbitrary hue is the
/// whole job here.</para>
/// </remarks>
public static class Ramp
{
    public const int Steps = 9;

    private static readonly double[] Lightness = [0.97, 0.945, 0.90, 0.825, 0.71, 0.59, 0.45, 0.32, 0.205];
    private static readonly double[] Saturation = [0.85, 0.90, 0.95, 1.0, 1.0, 1.0, 0.95, 0.80, 0.60];

    /// <summary>
    /// Nine steps of <paramref name="baseColor"/>'s hue, running away from the theme's
    /// background. <paramref name="isDark"/> picks the direction — see the type's remarks.
    /// </summary>
    public static Color[] Generate(Color baseColor, bool isDark)
    {
        var (h, s, _) = ColorMath.ToHsl(baseColor);
        var ramp = new Color[Steps];
        for (int i = 0; i < Steps; i++)
        {
            // A light theme walks the same curve from the other end, so index 100 stays the
            // high-contrast step and 900 the one that sits down near the page.
            int step = isDark ? i : Steps - 1 - i;
            ramp[i] = ColorMath.FromHsl(h, Math.Clamp(s * Saturation[step], 0, 1), Lightness[step]);
        }
        return ramp;
    }

    /// <summary>
    /// Nine steps that end on the theme's own ground rather than on the curve's darkest
    /// point. Used for the neutral scale, whose last steps are hover and row tints: those
    /// have to sit a hair off the background the app actually paints, and a generic hue at
    /// 20% lightness lands somewhere else entirely on a theme whose ground is #1d2021.
    /// </summary>
    public static Color[] GenerateNeutral(Color baseColor, Color background, bool isDark)
    {
        var ramp = Generate(baseColor, isDark);
        // Pull the tail toward the ground: step 900 lands just off it, 800 half way back.
        ramp[8] = ColorMath.Mix(background, ramp[8], 0.22);
        ramp[7] = ColorMath.Mix(background, ramp[7], 0.55);
        return ramp;
    }
}
