using SkiaSharp;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A fact that needs a font which shapes Devanagari, because it needs glyphs whose X goes
/// back: the vowel sign i (ि) is drawn before the consonant it follows in the text, so a
/// shaped run of कि has its glyphs out of X order. Without such a font the text falls back to
/// .notdef boxes laid out in text order, and nothing in the corpus goes back. The Linux CI
/// runner has DejaVu and the CJK fonts only, so a test that asserts the corpus reaches runs
/// whose X goes back failed there on a machine where nothing was wrong.
///
/// <para>As with <see cref="HanFontsFactAttribute"/>, the precondition is stated rather than
/// assumed: with no such font the test skips, visibly, and says what to install.</para>
/// </summary>
public sealed class DevanagariFontsFactAttribute : FactAttribute
{
    public DevanagariFontsFactAttribute()
    {
        if (!DevanagariFonts.Installed)
            Skip = "No installed font covers Devanagari, so no shaped run has glyphs whose X goes back. "
                 + "Install a font that does — on Debian/Ubuntu: apt install fonts-noto-core.";
    }
}

internal static class DevanagariFonts
{
    /// <summary>क (U+0915) and the vowel sign i (U+093F) that is drawn before it.</summary>
    private static readonly int[] Probes = [0x0915, 0x093F];

    public static bool Installed { get; } = Probe();

    private static bool Probe()
    {
        // Not disposed, for the reason HanFonts gives: the wrapper may be shared.
        foreach (int codepoint in Probes)
            if (SKFontManager.Default.MatchCharacter(codepoint) is null) return false;
        return true;
    }
}
