using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Text.Tests;

public class TextPipelineTests
{
    private static readonly ResolvedInheritedStyle DefaultStyle = new(
        FontFamily: "TestFont", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static TextPipeline CreatePipeline(float charWidth = 10f)
    {
        var fonts = new StubFontCollection(charWidth);
        var shaper = new StubTextShaper();
        return new TextPipeline(fonts, shaper);
    }

    // ── Basic single-line ──

    [Fact]
    public void EmptyText_ReturnsEmptyLayout()
    {
        var pipeline = CreatePipeline();
        var layout = pipeline.Layout("", DefaultStyle, 200, null);
        Assert.Empty(layout.Lines);
        Assert.Equal(0, layout.Size.Width);
        Assert.Equal(0, layout.Size.Height);
    }

    [Fact]
    public void SingleWord_FitsInOneLine()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout("Hello", DefaultStyle, 200, null);
        Assert.Single(layout.Lines);
        Assert.Equal(50, layout.Lines[0].Width, 0.1f);
    }

    [Fact]
    public void SingleLine_WidthMatchesTextWidth()
    {
        var pipeline = CreatePipeline(charWidth: 8);
        var layout = pipeline.Layout("Test", DefaultStyle, 500, null);
        Assert.Single(layout.Lines);
        Assert.Equal(32, layout.Lines[0].Width, 0.1f); // 4 chars × 8px
    }

    [Fact]
    public void SingleLine_HasCorrectMetrics()
    {
        var pipeline = CreatePipeline();
        var layout = pipeline.Layout("Hi", DefaultStyle, 500, null);
        Assert.Single(layout.Lines);
        var line = layout.Lines[0];
        Assert.True(line.Ascent > 0);
        Assert.True(line.Height > 0);
    }

    // ── Line wrapping ──

    [Fact]
    public void LongText_WrapsAtMaxWidth()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        // "Hello World" = 11 chars × 10 = 110px, maxWidth = 60px
        var layout = pipeline.Layout("Hello World", DefaultStyle, 60, null);
        Assert.True(layout.Lines.Count >= 2, $"Expected at least 2 lines, got {layout.Lines.Count}");
    }

    [Fact]
    public void ExplicitNewline_BreaksLine()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout("Line1\nLine2", DefaultStyle, 500, null);
        Assert.Equal(2, layout.Lines.Count);
    }

    [Fact]
    public void MultipleNewlines_CreateMultipleLines()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout("A\nB\nC", DefaultStyle, 500, null);
        Assert.Equal(3, layout.Lines.Count);
    }

    // ── MaxLines ──

    [Fact]
    public void MaxLines_TruncatesOutput()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout("A\nB\nC\nD", DefaultStyle, 500, maxLines: 2);
        Assert.Equal(2, layout.Lines.Count);
    }

    [Fact]
    public void MaxLines_One_ReturnsSingleLine()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout("Hello World Foo Bar", DefaultStyle, 60, maxLines: 1);
        Assert.Single(layout.Lines);
    }

    // ── Vertical positioning ──

    [Fact]
    public void MultiLine_LinesHaveIncreasingOffsetY()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout("Line1\nLine2\nLine3", DefaultStyle, 500, null);
        Assert.Equal(3, layout.Lines.Count);
        Assert.Equal(0, layout.Lines[0].OffsetY);
        Assert.True(layout.Lines[1].OffsetY > 0);
        Assert.True(layout.Lines[2].OffsetY > layout.Lines[1].OffsetY);
    }

    [Fact]
    public void MultiLine_TotalHeight_EqualsSum()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout("A\nB\nC", DefaultStyle, 500, null);
        float expectedHeight = 0;
        foreach (var line in layout.Lines)
            expectedHeight += line.Height;
        Assert.Equal(expectedHeight, layout.Size.Height, 0.1f);
    }

    // ── Caching ──

    [Fact]
    public void SameInput_ReturnsCachedResult()
    {
        var pipeline = CreatePipeline();
        var layout1 = pipeline.Layout("Cached", DefaultStyle, 200, null);
        var layout2 = pipeline.Layout("Cached", DefaultStyle, 200, null);
        Assert.Same(layout1, layout2);
    }

    [Fact]
    public void DifferentMaxWidth_ReturnsNewLayout()
    {
        var pipeline = CreatePipeline();
        var layout1 = pipeline.Layout("Text", DefaultStyle, 200, null);
        var layout2 = pipeline.Layout("Text", DefaultStyle, 100, null);
        Assert.NotSame(layout1, layout2);
    }

    [Fact]
    public void DifferentText_ReturnsNewLayout()
    {
        var pipeline = CreatePipeline();
        var layout1 = pipeline.Layout("Foo", DefaultStyle, 200, null);
        var layout2 = pipeline.Layout("Bar", DefaultStyle, 200, null);
        Assert.NotSame(layout1, layout2);
    }

    // ── Runs ──

    [Fact]
    public void SingleFont_ProducesSingleRun()
    {
        var pipeline = CreatePipeline();
        var layout = pipeline.Layout("Hello", DefaultStyle, 200, null);
        Assert.Single(layout.Lines);
        Assert.Single(layout.Lines[0].Runs);
        Assert.Equal("Hello", layout.Lines[0].Runs[0].Text);
    }

    // ── Size ──

    [Fact]
    public void Layout_Size_WidthMatchesWidestLine()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        // "LongerLine" = 10 chars = 100px, "Short" = 5 chars = 50px
        var layout = pipeline.Layout("LongerLine\nShort", DefaultStyle, 500, null);
        Assert.Equal(2, layout.Lines.Count);
        Assert.Equal(100, layout.Size.Width, 0.1f);
    }
}

// ── Stub implementations for unit testing ──

internal sealed class StubResolvedFont : IResolvedFont
{
    private readonly float _charWidth;
    public string FamilyName => "TestFont";
    // Metrics at reference size 1 (pipeline scales by fontSize)
    public float Ascent => -0.75f;
    public float Descent => 0.25f;
    public float Leading => 0f;
    public float LineHeight => Math.Abs(Ascent) + Descent + Leading;
    public object NativeHandle => this;
    public float CharWidth => _charWidth;

    public StubResolvedFont(float charWidth = 10f)
    {
        _charWidth = charWidth;
    }

    public bool HasGlyph(int codepoint) => true;
    public void Dispose() { }
}

internal sealed class StubFontCollection : IFontCollection
{
    private readonly StubResolvedFont _font;

    public StubFontCollection(float charWidth = 10f)
    {
        _font = new StubResolvedFont(charWidth);
    }

    public IResolvedFont Resolve(string familyName, FontWeight weight, FontSlant slant) => _font;
    public IResolvedFont? MatchCharacter(int codepoint, FontWeight weight, FontSlant slant) => _font;
}

internal sealed class StubTextShaper : ITextShaper
{
    public ShapedRun Shape(string text, IResolvedFont font, float fontSize)
    {
        var stubFont = (StubResolvedFont)font;
        float charWidth = stubFont.CharWidth;
        float totalWidth = text.Length * charWidth;

        var glyphs = new GlyphPosition[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            glyphs[i] = new GlyphPosition(
                GlyphId: (uint)text[i],
                X: i * charWidth,
                Y: 0,
                Advance: charWidth);
        }

        return new ShapedRun(text, font, fontSize, totalWidth, glyphs);
    }
}
