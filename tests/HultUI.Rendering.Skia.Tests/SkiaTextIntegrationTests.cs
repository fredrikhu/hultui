using HultUI.Core.Styling;
using HultUI.Rendering.Skia;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

public class SkiaTextIntegrationTests
{
    private static readonly ResolvedInheritedStyle DefaultStyle = new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static TextPipeline CreatePipeline()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        return new TextPipeline(fonts, shaper);
    }

    // ── Font resolution ──

    [Fact]
    public void FontCollection_ResolveDefaultFont_ReturnsNonNull()
    {
        var fonts = new SkiaFontCollection();
        var font = fonts.Resolve("sans-serif", FontWeight.Regular, FontSlant.Normal);
        Assert.NotNull(font);
        Assert.True(font.LineHeight > 0);
    }

    [Fact]
    public void FontCollection_ResolvedFont_HasPositiveMetrics()
    {
        var fonts = new SkiaFontCollection();
        var font = fonts.Resolve("sans-serif", FontWeight.Regular, FontSlant.Normal);
        // Ascent is negative in Skia convention (above baseline), Descent positive
        Assert.True(font.Ascent < 0, "Ascent should be negative (Skia convention)");
        Assert.True(font.Descent > 0, "Descent should be positive");
        Assert.True(font.LineHeight > 0);
    }

    [Fact]
    public void FontCollection_HasGlyphForAscii()
    {
        var fonts = new SkiaFontCollection();
        var font = fonts.Resolve("sans-serif", FontWeight.Regular, FontSlant.Normal);
        Assert.True(font.HasGlyph('A'));
        Assert.True(font.HasGlyph('z'));
        Assert.True(font.HasGlyph('0'));
    }

    [Fact]
    public void FontCollection_BoldDifferentFromRegular()
    {
        var fonts = new SkiaFontCollection();
        var regular = fonts.Resolve("sans-serif", FontWeight.Regular, FontSlant.Normal);
        var bold = fonts.Resolve("sans-serif", FontWeight.Bold, FontSlant.Normal);
        // Both should resolve successfully (may or may not be same family depending on system)
        Assert.NotNull(regular);
        Assert.NotNull(bold);
    }

    // ── Text shaping ──

    [Fact]
    public void TextShaper_ShapesAsciiText()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        var font = fonts.Resolve("sans-serif", FontWeight.Regular, FontSlant.Normal);
        var result = shaper.Shape("Hello", font, 16f);

        Assert.Equal("Hello", result.Text);
        Assert.Equal(5, result.Glyphs.Length);
        Assert.True(result.Width > 0);
    }

    [Fact]
    public void TextShaper_EmptyString_ReturnsZeroWidth()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        var font = fonts.Resolve("sans-serif", FontWeight.Regular, FontSlant.Normal);
        var result = shaper.Shape("", font, 16f);

        Assert.Equal(0, result.Width);
        Assert.Empty(result.Glyphs);
    }

    [Fact]
    public void TextShaper_GlyphAdvances_ArePositive()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        var font = fonts.Resolve("sans-serif", FontWeight.Regular, FontSlant.Normal);
        var result = shaper.Shape("Test", font, 16f);

        foreach (var glyph in result.Glyphs)
            Assert.True(glyph.Advance > 0, $"Glyph advance should be positive, got {glyph.Advance}");
    }

    [Fact]
    public void TextShaper_LargerFontSize_ProducesWiderText()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        var font = fonts.Resolve("sans-serif", FontWeight.Regular, FontSlant.Normal);
        var small = shaper.Shape("Hello", font, 12f);
        var large = shaper.Shape("Hello", font, 24f);

        Assert.True(large.Width > small.Width, "Larger font should produce wider text");
    }

    // ── Full pipeline integration ──

    [Fact]
    public void Pipeline_SingleLine_ProducesValidLayout()
    {
        var pipeline = CreatePipeline();
        var layout = pipeline.Layout("Hello World", DefaultStyle, 500, null);

        Assert.Single(layout.Lines);
        Assert.True(layout.Size.Width > 0);
        Assert.True(layout.Size.Height > 0);
    }

    [Fact]
    public void Pipeline_LineWrapping_ProducesMultipleLines()
    {
        var pipeline = CreatePipeline();
        // Use a very narrow width to force wrapping
        var layout = pipeline.Layout("Hello World Test", DefaultStyle, 50, null);

        Assert.True(layout.Lines.Count >= 2, $"Expected wrapping, got {layout.Lines.Count} lines");
    }

    [Fact]
    public void Pipeline_NewlineBreaks()
    {
        var pipeline = CreatePipeline();
        var layout = pipeline.Layout("Line one\nLine two", DefaultStyle, 500, null);

        Assert.Equal(2, layout.Lines.Count);
    }

    [Fact]
    public void Pipeline_MaxLines_Truncates()
    {
        var pipeline = CreatePipeline();
        var layout = pipeline.Layout("Line1\nLine2\nLine3\nLine4", DefaultStyle, 500, maxLines: 2);

        Assert.Equal(2, layout.Lines.Count);
    }

    [Fact]
    public void Pipeline_MeasureContext_ReturnsNonZeroSize()
    {
        var pipeline = CreatePipeline();
        var ctx = new SkiaMeasureContext(pipeline);

        var size = ctx.MeasureText("Hello", DefaultStyle, 500, null);
        Assert.True(size.Width > 0);
        Assert.True(size.Height > 0);
    }

    [Fact]
    public void Pipeline_EmptyText_MeasureContext_ReturnsZero()
    {
        var pipeline = CreatePipeline();
        var ctx = new SkiaMeasureContext(pipeline);

        var size = ctx.MeasureText("", DefaultStyle, 500, null);
        Assert.Equal(0, size.Width);
        Assert.Equal(0, size.Height);
    }

    [Fact]
    public void Pipeline_CachingWorks_SameResultForSameInput()
    {
        var pipeline = CreatePipeline();
        var layout1 = pipeline.Layout("Cache test", DefaultStyle, 200, null);
        var layout2 = pipeline.Layout("Cache test", DefaultStyle, 200, null);
        Assert.Same(layout1, layout2);
    }

    // ── Font fallback ──

    [Fact]
    public void FontCollection_MatchCharacter_ReturnsFont()
    {
        var fonts = new SkiaFontCollection();
        // 'A' should be found in some font
        var font = fonts.MatchCharacter('A', FontWeight.Regular, FontSlant.Normal);
        Assert.NotNull(font);
    }

    [Fact]
    public void FontCollection_MatchCharacter_SameFace_IsTheSameFont()
    {
        // FontItemizer splits runs on font identity. A wrapper per codepoint made every
        // fallback letter its own run — only visible where the primary font lacks the
        // script (Linux: Hebrew), so the bidi tests alone pass on Windows without this.
        var fonts = new SkiaFontCollection();
        var alef = fonts.MatchCharacter(0x5D0, FontWeight.Regular, FontSlant.Normal);
        var bet = fonts.MatchCharacter(0x5D1, FontWeight.Regular, FontSlant.Normal);

        Assert.NotNull(alef);
        Assert.NotNull(bet);
        Assert.Equal(alef.NativeHandle, bet.NativeHandle);
        Assert.Same(alef, bet);
    }

    [Fact]
    public void FontCollection_MatchCharacter_FallsBackToAFaceMadeForTheScript()
    {
        // Asked only "who has U+05D0", fontconfig answered Liberation Mono on Fedora, so
        // Hebrew in a sans-serif UI came out monospaced beside an installed Noto Sans
        // Hebrew. The script's language is the hint that fixes it. Like the bidi tests,
        // this assumes the platform ships a proportional Hebrew face, which every desktop
        // does.
        var fonts = new SkiaFontCollection();
        var alef = fonts.MatchCharacter(0x5D0, FontWeight.Regular, FontSlant.Normal);

        Assert.NotNull(alef);
        var typeface = (SkiaSharp.SKTypeface)alef.NativeHandle;
        Assert.False(typeface.IsFixedPitch, $"Hebrew fell back to monospaced '{typeface.FamilyName}'");
    }
}
