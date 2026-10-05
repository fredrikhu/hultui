using Xunit;

namespace HultUI.Text.Tests;

/// <summary>Two layouts the same to the bit: lines, runs, glyphs, every float.</summary>
internal static class LayoutAssert
{
    public static void Same(TextLayout expected, TextLayout actual, string context)
    {
        Assert.True(Bits(expected.Size.Width) == Bits(actual.Size.Width) && Bits(expected.Size.Height) == Bits(actual.Size.Height),
            $"{context}: size {actual.Size}, expected {expected.Size}");
        Assert.True(expected.IsRtlParagraph == actual.IsRtlParagraph, $"{context}: direction");
        Assert.True(expected.Lines.Count == actual.Lines.Count, $"{context}: {actual.Lines.Count} lines, expected {expected.Lines.Count}");
        for (int l = 0; l < expected.Lines.Count; l++)
        {
            var e = expected.Lines[l];
            var a = actual.Lines[l];
            string at = $"{context}, line {l}";
            Assert.True((e.Start, e.Length, e.DrawnLength) == (a.Start, a.Length, a.DrawnLength),
                $"{at}: [{a.Start}+{a.Length}, drawn {a.DrawnLength}], expected [{e.Start}+{e.Length}, drawn {e.DrawnLength}]");
            Assert.True(Bits(e.Width) == Bits(a.Width) && Bits(e.OffsetY) == Bits(a.OffsetY),
                $"{at}: width {a.Width} at y {a.OffsetY}, expected {e.Width} at y {e.OffsetY}");
            Assert.True(Bits(e.Ascent) == Bits(a.Ascent) && Bits(e.Descent) == Bits(a.Descent) && Bits(e.Leading) == Bits(a.Leading),
                $"{at}: metrics");
            Assert.True(e.Runs.Count == a.Runs.Count, $"{at}: {a.Runs.Count} runs, expected {e.Runs.Count}");
            for (int r = 0; r < e.Runs.Count; r++)
            {
                var er = e.Runs[r];
                var ar = a.Runs[r];
                string rat = $"{at}, run {r}";
                Assert.True(er.Text == ar.Text, $"{rat}: \"{ar.Text}\", expected \"{er.Text}\"");
                Assert.True(ReferenceEquals(er.Font, ar.Font), $"{rat}: font {ar.Font.FamilyName}, expected {er.Font.FamilyName}");
                Assert.True(Bits(er.FontSize) == Bits(ar.FontSize) && Bits(er.Width) == Bits(ar.Width) && Bits(er.OffsetX) == Bits(ar.OffsetX),
                    $"{rat}: width {ar.Width} at x {ar.OffsetX}, expected {er.Width} at x {er.OffsetX}");
                Assert.True(er.Level == ar.Level, $"{rat}: level");
                Assert.True(er.Glyphs.Length == ar.Glyphs.Length, $"{rat}: {ar.Glyphs.Length} glyphs, expected {er.Glyphs.Length}");
                for (int g = 0; g < er.Glyphs.Length; g++)
                {
                    var eg = er.Glyphs[g];
                    var ag = ar.Glyphs[g];
                    Assert.True(eg.GlyphId == ag.GlyphId && Bits(eg.X) == Bits(ag.X) && Bits(eg.Y) == Bits(ag.Y) && Bits(eg.Advance) == Bits(ag.Advance),
                        $"{rat}, glyph {g}: {ag}, expected {eg}");
                }
            }
        }
    }

    private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
}
