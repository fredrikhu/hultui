using SkiaSharp;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A fact about which face Han text falls back to — and so one that needs a font covering
/// Han to be installed at all. A desktop always has one. A lean server container does not:
/// <c>libfontconfig1</c> brings DejaVu along, which has Hebrew and Arabic (so the bidi
/// tests run) and not one ideograph. There the font manager answers null for every Han
/// codepoint, and a test that asserts "four cultures, four faces" fails on a machine where
/// nothing is wrong — which kept the Linux CI job red from the day these tests landed.
///
/// <para>So the precondition is stated rather than assumed: with no such font the test
/// skips, visibly, and says what to install. It asks only whether <i>any</i> face covers
/// Han — not whether the per-language faces are there, which is what the tests themselves
/// are for; a machine with one CJK font and no others should fail them, not skip them.</para>
/// </summary>
public sealed class HanFontsFactAttribute : FactAttribute
{
    public HanFontsFactAttribute()
    {
        if (!HanFonts.Installed)
            Skip = "No installed font covers Han ideographs, so there is no fallback to choose between. "
                 + "Install a CJK font set — on Debian/Ubuntu: apt install fonts-noto-cjk.";
    }
}

internal static class HanFonts
{
    /// <summary>The two ideographs the fallback tests use: 東 (U+6771) and 說 (U+8AAA).
    /// Both are in every CJK language's fonts, so any CJK font answers for them.</summary>
    private static readonly int[] Probes = [0x6771, 0x8AAA];

    public static bool Installed { get; } = Probe();

    private static bool Probe()
    {
        // Not disposed: SkiaSharp hands back one managed wrapper per native typeface, so the
        // face found here may be the very object a font collection in another test is
        // holding, and disposing it would pull it out from under that test.
        foreach (int codepoint in Probes)
            if (SKFontManager.Default.MatchCharacter(codepoint) is null) return false;
        return true;
    }
}
