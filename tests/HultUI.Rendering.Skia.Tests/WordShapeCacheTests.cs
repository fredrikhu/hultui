using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Equivalence coverage for the word-level shape cache: TextPipeline shapes a
/// run as space-delimited segments through an LRU cache and concatenates, which
/// must be glyph-for-glyph identical to shaping the whole run directly. The one
/// designed-in exception is a font that kerns across a letter↔space boundary
/// (UI fonts essentially never do) — if these tests ever fail on a platform,
/// that platform's font does exactly that, and the tradeoff documented in
/// TextPipeline stops being free.
/// </summary>
public class WordShapeCacheTests
{
    private static readonly ResolvedInheritedStyle DefaultStyle = new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static void AssertMatchesDirectShaping(TextPipeline pipeline, SkiaFontCollection fonts,
        SkiaTextShaper shaper, string text)
    {
        var layout = pipeline.Layout(text, DefaultStyle, maxWidth: 10_000, maxLines: null);
        var line = Assert.Single(layout.Lines);
        var run = Assert.Single(line.Runs); // ASCII input → a single font run

        var font = fonts.Resolve(DefaultStyle.FontFamily, DefaultStyle.FontWeight, DefaultStyle.FontSlant);
        var direct = shaper.Shape(text, font, DefaultStyle.FontSize);

        Assert.Equal(direct.Width, run.Width, 2);
        Assert.Equal(direct.Glyphs.Length, run.Glyphs.Length);
        for (int i = 0; i < direct.Glyphs.Length; i++)
        {
            Assert.Equal(direct.Glyphs[i].GlyphId, run.Glyphs[i].GlyphId);
            Assert.Equal(direct.Glyphs[i].X, run.Glyphs[i].X, 2);
            Assert.Equal(direct.Glyphs[i].Y, run.Glyphs[i].Y, 2);
            Assert.Equal(direct.Glyphs[i].Advance, run.Glyphs[i].Advance, 2);
        }
    }

    [Theory]
    [InlineData("pretty red table")]     // recurring-vocabulary shape
    [InlineData("10234 56789")]          // digit segments
    [InlineData("AV To Wa")]             // kern pairs *within* words must survive
    [InlineData("double  spaced")]       // a multi-space run is its own segment
    [InlineData("61001")]                // single segment: pure cache passthrough
    public void SegmentedShaping_MatchesWholeStringShaping(string text)
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        var pipeline = new TextPipeline(fonts, shaper);

        AssertMatchesDirectShaping(pipeline, fonts, shaper, text);
    }

    [Fact]
    public void WordsCachedFromOtherStrings_ComposeIdentically()
    {
        // The hit path: every word of the final string enters the cache via a
        // DIFFERENT string first, so its shaping is reused, offset, and
        // concatenated — and must still match shaping the string directly.
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        var pipeline = new TextPipeline(fonts, shaper);

        pipeline.Layout("alpha beta", DefaultStyle, 10_000, null);
        pipeline.Layout("gamma delta", DefaultStyle, 10_000, null);

        AssertMatchesDirectShaping(pipeline, fonts, shaper, "alpha delta");
    }

    [Fact]
    public void RepeatedLayouts_AreStableAcrossCacheHits()
    {
        // Same text through cold caches, then warm word cache (distinct
        // pipeline defeats the whole-string cache): identical width both times.
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();

        var cold = new TextPipeline(fonts, shaper)
            .Layout("cached words stay stable", DefaultStyle, 10_000, null);

        var warmPipeline = new TextPipeline(fonts, shaper);
        warmPipeline.Layout("cached words stay stable", DefaultStyle, 10_000, null);
        var warm = warmPipeline.Layout("stable words cached stay", DefaultStyle, 10_000, null);

        Assert.Equal(cold.Size.Width, warm.Size.Width, 2);
    }
}
