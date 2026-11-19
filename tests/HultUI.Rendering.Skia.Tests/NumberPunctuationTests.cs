using HultUI.Core.Layout;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Numbers with punctuation in them (<c>-38913</c>, <c>1,234.56</c>, <c>12:30</c>) composed from
/// the digit table (docs/plans/19-run-shaping.md) lay out as the reference shapes them, in the
/// system's own faces: the same glyphs, each where HarfBuzz put it to within the digit proof's
/// tolerance. Where a face fails a mark's proof, the reference's own output, to the bit.
/// </summary>
public class NumberPunctuationTests(ITestOutputHelper output)
{
    private static ResolvedInheritedStyle Style(string family, float size) => new(
        FontFamily: family, FontSize: size, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly string[] Texts =
    [
        "-38913", "1,234.56", "12:30", "50%", "+1.5", "2026-11-19", "−7", "3/4", "1.2.3", "100,000,000",
        "-0.25", "99.9%", "+46-70-123", "08:00-17:30", "1/2/3", "--5", "5--", "1..2", "0,5", "7::8",
        "Item 1000001 — -38913 units", "Total 1,234.56 at 12:30 (50 %) on 2026-11-19, -7 °C",
        "Version 4.156.1 / 14.4.0.101: +14 % and -58 %",
    ];

    [Fact]
    public void NumbersWithPunctuation_LayOutAsTheReferenceShapesThem()
    {
        var fonts = new SkiaFontCollection();
        using var shaperA = new SkiaTextShaper();
        using var shaperB = new SkiaTextShaper();
        var families = new List<string> { "sans-serif", "monospace" };
        if (OperatingSystem.IsWindows()) families.Add("Arial");   // fails the digits' own proof
        int runs = 0;
        foreach (string family in families)
            foreach (float size in new[] { 11f, 13f, 17.5f })
            {
                var reference = new TextPipeline(fonts, shaperA) { ComposesNumberPunctuation = false };
                var composed = new TextPipeline(fonts, shaperB);
                var style = Style(family, size);
                foreach (string text in Texts)
                {
                    var a = reference.Layout(text, style, 10_000, null);
                    var b = composed.Layout(text, style, 10_000, null);
                    Assert.Equal(a.Size.Width, b.Size.Width, 0.02f);
                    Assert.Equal(a.Lines.Count, b.Lines.Count);
                    for (int l = 0; l < a.Lines.Count; l++)
                    {
                        Assert.Equal(a.Lines[l].Runs.Count, b.Lines[l].Runs.Count);
                        for (int r = 0; r < a.Lines[l].Runs.Count; r++)
                        {
                            runs++;
                            var ga = a.Lines[l].Runs[r].Glyphs;
                            var gb = b.Lines[l].Runs[r].Glyphs;
                            Assert.Equal(ga.Length, gb.Length);
                            for (int g = 0; g < ga.Length; g++)
                            {
                                string where = $"\"{text}\" in {family} {size}px, glyph {g}: {ga[g]} against {gb[g]}";
                                Assert.True(ga[g].GlyphId == gb[g].GlyphId, where);
                                Assert.True(Math.Abs(ga[g].X - gb[g].X) <= 0.02f, where);
                                Assert.True(Math.Abs(ga[g].Y - gb[g].Y) <= 0.01f, where);
                                Assert.True(Math.Abs(ga[g].Advance - gb[g].Advance) <= 0.02f, where);
                            }
                        }
                    }
                }
            }
        output.WriteLine($"{runs} runs compared");
    }

    [Fact]
    public void InSegoeUi_ANewSignedNumber_CostsNoShaperCall()
    {
        var fonts = new SkiaFontCollection();
        var style = Style("sans-serif", 13f);
        if (fonts.Resolve(style.FontFamily, style.FontWeight, style.FontSlant).FamilyName != "Segoe UI")
            return;   // the faces elsewhere are not known to pass; the test above holds them anyway
        using var inner = new SkiaTextShaper();
        var shaper = new CountingShaper(inner);
        var pipeline = new TextPipeline(fonts, shaper);

        pipeline.Layout("Item 1 — -2 units, 3.4 % at 5:06 on 7/8 +9 −1", style, 10_000, null);   // proofs and words
        int before = shaper.Calls;
        for (int i = 0; i < 50; i++)
            pipeline.Layout($"Item {1_000_000 + i} — -{38_913 + 7 * i} units, {i}.{i} % at {i}:{i:00} on {i}/{i} +{i} −{i}",
                style, 10_000, null);
        Assert.Equal(before, shaper.Calls);
    }

    private sealed class CountingShaper(ITextShaper inner) : ITextShaper
    {
        public int Calls { get; private set; }

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            Calls++;
            return inner.Shape(text, font, fontSize, rtl);
        }
    }
}
