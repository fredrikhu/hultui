using HultUI.Core.Layout;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// What a wrap kept for a range of widths (TextPipeline.WrapRange) holds to, where a real font would
/// rarely show it: the range ends where the text fits one line, even when its break segments measured
/// apart add up to more than the text shaped whole (a face whose ideographs tighten in context, as Noto
/// Sans CJK's do), so a width past the last break the pass refused is still the one-line layout's.
/// </summary>
public class WrapRangeTests
{
    private static readonly ResolvedInheritedStyle Style = new(
        FontFamily: "TestFont", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    // Twelve ideographs: 10 px each alone, 4 px tighter for each pair in context, so 76 px as one line
    // and 120 px as break segments.
    private static readonly string Ideographs = string.Concat(Enumerable.Range(0, 12).Select(i => (char)(0x4E00 + i * 7)));

    [Fact]
    public void AKeptWrap_EndsWhereTheTextFitsOneLine()
    {
        var fonts = new StubFontCollection();
        var pipeline = new TextPipeline(fonts, new TightShaper());
        var reference = new TextPipeline(fonts, new TightShaper()) { KeepsWrapRanges = false };
        float natural = pipeline.Layout(Ideographs, Style, float.PositiveInfinity, null).Size.Width;
        Assert.Equal(76f, natural);

        // At 70 px seven segments fit and the eighth (80 px with them) does not: two lines. The pass
        // refused nothing narrower than 80 px, but from 76 px the text is one line.
        var wrapped = pipeline.Layout(Ideographs, Style, 70f, null);
        Assert.Equal(2, wrapped.Lines.Count);
        Assert.True(pipeline.KeptWrapRange(Ideographs, Style, null, out float from, out float below));
        Assert.Equal(70f, from);
        Assert.Equal(natural, below);

        Assert.Same(wrapped, pipeline.Layout(Ideographs, Style, 75.5f, null));
        LayoutAssert.Same(reference.Layout(Ideographs, Style, 75.5f, null), wrapped, "75.5 px");

        // With its shaping let go of (a document's worth of other text after it), the kept wrap is asked
        // before the text is shaped again, and must not answer at a width the text fits one line in.
        for (int i = 0; i < 600; i++)
            pipeline.Layout(new string((char)('a' + i % 26), 1_000) + i, Style, float.PositiveInfinity, 1);
        var oneLine = pipeline.Layout(Ideographs, Style, natural, null);
        Assert.Single(oneLine.Lines);
        LayoutAssert.Same(reference.Layout(Ideographs, Style, natural, null), oneLine, "76 px");
    }

    [Fact]
    public void AKeptRange_IsThePassesOwn_WhateverWasLaidOutBefore()
    {
        // One-line clips and truncations measure prefixes against their own widths in the same search
        // as a wrap does: a wrap laid out after them is kept for the widths of its own decisions.
        var fonts = new StubFontCollection();
        var fresh = new TextPipeline(fonts, new TightShaper());
        fresh.Layout(Ideographs, Style, 70f, null);
        Assert.True(fresh.KeptWrapRange(Ideographs, Style, null, out float from, out float below));

        var pipeline = new TextPipeline(fonts, new TightShaper());
        pipeline.Layout("a label clipped to one line", Style, 45f, 1);
        pipeline.Layout("a label truncated to one line", Style, 45f, 1, TextOverflow.Ellipsis);
        pipeline.Layout(Ideographs, Style, 70f, null);
        Assert.True(pipeline.KeptWrapRange(Ideographs, Style, null, out float after, out float afterBelow));
        Assert.Equal((from, below), (after, afterBelow));
    }

    [Fact]
    public void ATruncatedLayout_IsNotKept_NorAnsweredWithAKeptOne()
    {
        // Two lines of three: clipped, the second line ends at the box; truncated, it ends in an
        // ellipsis. The same text, lines and width either way, so only the overflow tells them apart.
        const string text = "one two three four five six seven eight nine ten eleven twelve";
        var fonts = new StubFontCollection();
        var reference = new TextPipeline(fonts, new StubTextShaper()) { KeepsWrapRanges = false };
        var clipped = reference.Layout(text, Style, 199.5f, 2, TextOverflow.Clip).Lines[^1];
        var truncated = reference.Layout(text, Style, 199.5f, 2, TextOverflow.Ellipsis).Lines[^1];
        Assert.NotEqual((clipped.Width, clipped.DrawnLength), (truncated.Width, truncated.DrawnLength));
        foreach (bool clipFirst in new[] { true, false })
        {
            var pipeline = new TextPipeline(fonts, new StubTextShaper());
            var (first, then) = clipFirst ? (TextOverflow.Clip, TextOverflow.Ellipsis) : (TextOverflow.Ellipsis, TextOverflow.Clip);
            pipeline.Layout(text, Style, 200f, 2, first);
            var second = pipeline.Layout(text, Style, 199.5f, 2, then);
            LayoutAssert.Same(reference.Layout(text, Style, 199.5f, 2, then), second, $"{first} then {then}");
        }
    }

    // StubTextShaper's 10 px a glyph, and 4 px less for each pair of ideographs side by side.
    private sealed class TightShaper : ITextShaper
    {
        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            var glyphs = new GlyphPosition[text.Length];
            float x = 0;
            for (int i = 0; i < text.Length; i++)
            {
                float advance = 10f;
                if (i + 1 < text.Length && text[i] >= 0x4E00 && text[i + 1] >= 0x4E00) advance -= 4f;
                glyphs[i] = new GlyphPosition(text[i], x, 0, advance);
                x += advance;
            }
            return new ShapedRun(text, font, fontSize, x, glyphs);
        }
    }
}
