using SkiaSharp;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A fact that needs a variable outline font: one whose bounding box Skia marks as a guess,
/// because a variable font's box is its default instance's. Its spanned lines take each run's
/// extent from the run's own glyphs. Windows has Cascadia Mono (its <c>monospace</c>), Segoe UI
/// Variable and Bahnschrift; Fedora has Noto Sans. The Linux CI runner has DejaVu and the CJK
/// fonts, none of them variable.
///
/// <para>As with <see cref="HanFontsFactAttribute"/>, the precondition is stated rather than
/// assumed: with no such font the test skips, visibly, and says what to install.</para>
/// </summary>
public sealed class VariableFontFactAttribute : FactAttribute
{
    public VariableFontFactAttribute()
    {
        if (VariableFonts.Family is null)
            Skip = "No installed outline font has a bounding box Skia marks as a guess (a variable font). "
                 + "Install one: Cascadia Code or Cascadia Mono, or Fedora's Noto Sans.";
    }
}

/// <summary>The same precondition for a theory.</summary>
public sealed class VariableFontTheoryAttribute : TheoryAttribute
{
    public VariableFontTheoryAttribute()
    {
        if (VariableFonts.Family is null)
            Skip = "No installed outline font has a bounding box Skia marks as a guess (a variable font). "
                 + "Install one: Cascadia Code or Cascadia Mono, or Fedora's Noto Sans.";
    }
}

internal static class VariableFonts
{
    private static readonly string[] Candidates =
        ["Cascadia Mono", "Cascadia Code", "Segoe UI Variable Text", "Bahnschrift", "Noto Sans", "Noto Sans Mono", "Inter", "Roboto Flex"];

    /// <summary>The first installed family of <see cref="Candidates"/> whose regular face has
    /// outlines, an fvar table and a box Skia marks as a guess; null when none is.</summary>
    public static string? Family { get; } = Probe();

    private static string? Probe()
    {
        using var font = new SKFont { LinearMetrics = true, Size = 2048f };
        foreach (var family in Candidates)
        {
            // Not disposed, for the reason HanFonts gives: the wrapper may be shared. A font
            // manager may answer with another family; only the one asked for counts.
            var typeface = SKFontManager.Default.MatchFamily(family);
            if (typeface is null || !string.Equals(typeface.FamilyName, family, StringComparison.OrdinalIgnoreCase)) continue;
            if (typeface.GetTableSize(Tag("fvar")) == 0) continue;
            if (typeface.GetTableSize(Tag("glyf")) == 0 && typeface.GetTableSize(Tag("CFF2")) == 0) continue;
            font.Typeface = typeface;
            font.GetFontMetrics(out var metrics);
            // SkFontMetrics' first field is its flags; kBoundsInvalid_Flag is bit 4.
            uint flags = System.Runtime.CompilerServices.Unsafe.As<SKFontMetrics, uint>(ref metrics);
            if ((flags & (1u << 4)) != 0) return family;
        }
        return null;
    }

    private static uint Tag(string t) => (uint)(t[0] << 24 | t[1] << 16 | t[2] << 8 | t[3]);
}
