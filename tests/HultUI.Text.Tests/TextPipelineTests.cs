using System.Linq;
using HultUI.Core.Layout;
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
    public void DifferentMaxWidth_WhenWrappingChanges_ReturnsNewLayout()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        // "Hello World" = 110px: one line at 200, wraps at 60. Width changes the
        // result, so the layouts must differ.
        var layout1 = pipeline.Layout("Hello World", DefaultStyle, 200, null);
        var layout2 = pipeline.Layout("Hello World", DefaultStyle, 60, null);
        Assert.NotSame(layout1, layout2);
        Assert.Single(layout1.Lines);
        Assert.True(layout2.Lines.Count >= 2);
    }

    [Fact]
    public void SingleLineText_AcrossWidthsThatFit_ReturnsSameCachedLayout()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        // "Text" = 40px fits both widths. Shaping is width-independent, so the same
        // single-line layout is reused rather than re-shaped per width.
        var layout1 = pipeline.Layout("Text", DefaultStyle, 200, null);
        var layout2 = pipeline.Layout("Text", DefaultStyle, 100, null);
        Assert.Same(layout1, layout2);
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

    // ── Source character ranges (for selectable text) ──

    [Fact]
    public void SingleLine_CoversWholeString()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout("Hello", DefaultStyle, 500, null);
        Assert.Single(layout.Lines);
        Assert.Equal(0, layout.Lines[0].Start);
        Assert.Equal(5, layout.Lines[0].Length);
    }

    [Fact]
    public void ExplicitNewline_LinesHaveContiguousSourceRanges()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout("Line1\nLine2", DefaultStyle, 500, null);
        Assert.Equal(2, layout.Lines.Count);
        // First line spans "Line1"; the '\n' at index 5 sits between the lines.
        Assert.Equal(0, layout.Lines[0].Start);
        Assert.Equal(5, layout.Lines[0].Length);
        Assert.Equal(6, layout.Lines[1].Start);
        Assert.Equal(5, layout.Lines[1].Length);
    }

    [Fact]
    public void WrappedLines_SourceRangesAreContiguous()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        // Wraps into multiple lines; each line's Start should equal the previous
        // line's Start+Length (no gap, since there is no explicit newline).
        var layout = pipeline.Layout("aaa bbb ccc ddd", DefaultStyle, 40, null);
        Assert.True(layout.Lines.Count >= 2);
        for (int i = 1; i < layout.Lines.Count; i++)
            Assert.Equal(layout.Lines[i - 1].Start + layout.Lines[i - 1].Length, layout.Lines[i].Start);
        // The last line reaches the end of the string.
        var last = layout.Lines[^1];
        Assert.Equal(15, last.Start + last.Length);
    }

    // ── Ellipsis overflow ──

    private static string LineText(TextLine line) =>
        string.Concat(line.Runs.Select(r => r.Text));

    [Fact]
    public void Ellipsis_SingleLineTooWide_TruncatesWithEllipsis()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        // 19 chars × 10 = 190px natural; box is 60px. With a 10px ellipsis, only a
        // 50px (5-char) prefix fits before it.
        var layout = pipeline.Layout("Hello World Foo Bar", DefaultStyle, 60,
            maxLines: 1, TextOverflow.Ellipsis);

        Assert.Single(layout.Lines);
        var text = LineText(layout.Lines[0]);
        Assert.EndsWith("…", text);
        Assert.Equal("Hello…", text);
        Assert.True(layout.Lines[0].Width <= 60.5f, $"line width {layout.Lines[0].Width} exceeds box");
    }

    [Fact]
    public void Ellipsis_UnbreakableToken_TruncatesMidWord()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        // No break opportunities, so the whole token would overflow; ellipsis must
        // clip it mid-word rather than let it spill past the box.
        var layout = pipeline.Layout("Supercalifragilistic", DefaultStyle, 60,
            maxLines: 1, TextOverflow.Ellipsis);

        Assert.Single(layout.Lines);
        var text = LineText(layout.Lines[0]);
        Assert.EndsWith("…", text);
        Assert.Equal("Super…", text);
    }

    [Fact]
    public void Ellipsis_TextThatFits_IsUnchanged()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout("Hi", DefaultStyle, 200, maxLines: 1, TextOverflow.Ellipsis);

        Assert.Single(layout.Lines);
        Assert.Equal("Hi", LineText(layout.Lines[0]));
    }

    [Fact]
    public void Ellipsis_MultiLineClamp_EllipsizesLastVisibleLine()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        // Wraps to several lines but clamped to 2; the 2nd line gets the ellipsis
        // and earlier lines are untouched.
        var layout = pipeline.Layout("aaa bbb ccc ddd eee", DefaultStyle, 40,
            maxLines: 2, TextOverflow.Ellipsis);

        Assert.Equal(2, layout.Lines.Count);
        Assert.DoesNotContain("…", LineText(layout.Lines[0]));
        Assert.EndsWith("…", LineText(layout.Lines[1]));
    }

    [Fact]
    public void Clip_Overflow_HasNoEllipsisMarker()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout("Hello World Foo Bar", DefaultStyle, 60,
            maxLines: 1, TextOverflow.Clip);

        Assert.Single(layout.Lines);
        Assert.DoesNotContain("…", LineText(layout.Lines[0]));
    }

    [Fact]
    public void Clip_CutsAtACharacterNotAWordBoundary()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        // 60px holds six characters. Breaking at the last word boundary that fits would
        // stop after "ab" and leave two thirds of the box empty.
        var layout = pipeline.Layout("ab cdefghijklmnop", DefaultStyle, 60,
            maxLines: 1, TextOverflow.Clip);

        Assert.Equal("ab cde", LineText(Assert.Single(layout.Lines)));
    }

    [Fact]
    public void Clip_FillsTheBoxWhenThereIsNowhereToBreak()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout(new string('x', 5000), DefaultStyle, 95,
            maxLines: 1, TextOverflow.Clip);

        Assert.Equal(9, LineText(Assert.Single(layout.Lines)).Length);
    }

    [Fact]
    public void Clip_NeverDrawsPastTheBox()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        // DrawText does not clip to its bounds, so a glyph straddling the edge would
        // spill out of the element rather than being cut by it.
        for (float w = 1; w <= 120; w += 7)
        {
            var layout = pipeline.Layout("the quick brown fox jumps", DefaultStyle, w,
                maxLines: 1, TextOverflow.Clip);
            foreach (var line in layout.Lines)
                Assert.True(line.Width <= w + 0.01f, $"line is {line.Width}px wide in a {w}px box");
        }
    }

    // ── Character-level breaking ──
    // A run with no line-break opportunity in it — a hash, a base64 blob, a path with no
    // separators — used to be emitted as one line wider than the box and simply ran out of it.

    [Fact]
    public void Wrap_ARunWithNowhereToBreakIsCutByCharacter()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout(new string('x', 25), DefaultStyle, 60, maxLines: null);

        Assert.Equal(5, layout.Lines.Count);          // 6 + 6 + 6 + 6 + 1
        Assert.All(layout.Lines, l => Assert.True(l.Width <= 60.01f, $"a line is {l.Width}px wide in a 60px box"));
        Assert.Equal(new string('x', 25), string.Concat(layout.Lines.Select(LineText)));
    }

    [Fact]
    public void Wrap_BreaksAtWordsFirstAndOnlyThenAtCharacters()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout("ab " + new string('y', 14), DefaultStyle, 60, maxLines: null);

        // "ab" keeps its own line — the long run could not join it — then the run is cut.
        Assert.Equal("ab", LineText(layout.Lines[0]));
        Assert.All(layout.Lines, l => Assert.True(l.Width <= 60.01f));
        Assert.Equal(new string('y', 14), string.Concat(layout.Lines.Skip(1).Select(LineText)));
    }

    [Fact]
    public void Wrap_CharacterBreakingStillRespectsMaxLines()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout(new string('x', 100), DefaultStyle, 60, maxLines: 2);

        Assert.Equal(2, layout.Lines.Count);
    }

    [Fact]
    public void Wrap_ABoxTooNarrowForOneCharacterGivesOneCharacterPerLine()
    {
        // The floor that keeps the split from looping forever. Overflowing by a single
        // glyph is what a browser does with a box this size.
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout("abcd", DefaultStyle, 4, maxLines: null);

        Assert.Equal(4, layout.Lines.Count);
        Assert.Equal("abcd", string.Concat(layout.Lines.Select(LineText)));
    }

    [Fact]
    public void Wrap_TextThatBreaksNormallyIsUntouched()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout("hello world again", DefaultStyle, 60, maxLines: null);

        Assert.Equal(3, layout.Lines.Count);
        Assert.Equal("hello", LineText(layout.Lines[0]));
        Assert.Equal("world", LineText(layout.Lines[1]));
        Assert.Equal("again", LineText(layout.Lines[2]));
    }

    [Fact]
    public void Clip_KeepsTheSameLineHeightAsEveryOtherPath()
    {
        // Height feeds MeasureContent, so a clipped line that reports a different height
        // from a wrapped or ellipsized one silently resizes whatever contains it.
        var pipeline = CreatePipeline(charWidth: 10);
        var style = DefaultStyle with { LineHeight = 24f };

        var clip = pipeline.Layout("the quick brown fox jumps", style, 60, maxLines: 1, TextOverflow.Clip);
        var ellipsis = pipeline.Layout("the quick brown fox jumps", style, 60, maxLines: 1, TextOverflow.Ellipsis);
        var fits = pipeline.Layout("short", style, 600, maxLines: 1, TextOverflow.Clip);

        Assert.Equal(ellipsis.Size.Height, clip.Size.Height, 3);
        Assert.Equal(fits.Size.Height, clip.Size.Height, 3);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(3f)]     // narrower than one 10px character
    public void Clip_TooNarrowForAnyCharacterStillOccupiesALinesHeight(float width)
    {
        // Flex measures children at a zero basis routinely, so this is an ordinary layout
        // path, not a degenerate one. Reporting no height for a non-empty string collapses
        // whatever contains it — it shrank Junction's repository cards by 6px.
        var pipeline = CreatePipeline(charWidth: 10);
        var style = DefaultStyle with { LineHeight = 24f };

        var narrow = pipeline.Layout("hello", style, width, maxLines: 1, TextOverflow.Clip);
        var roomy = pipeline.Layout("hello", style, 600, maxLines: 1, TextOverflow.Clip);

        Assert.Equal(roomy.Size.Height, narrow.Size.Height, 3);
        Assert.Equal(0, narrow.Size.Width);
    }

    [Fact]
    public void Clip_TextThatFitsIsUnchanged()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var layout = pipeline.Layout("Hi", DefaultStyle, 200, maxLines: 1, TextOverflow.Clip);

        Assert.Equal("Hi", LineText(Assert.Single(layout.Lines)));
    }

    [Fact]
    public void Clip_MultiLineStillWraps()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        // The character-level cut is for a single line only; with room for more, the
        // text still breaks into words the way it always did.
        var layout = pipeline.Layout("Hello World", DefaultStyle, 60, maxLines: 2, TextOverflow.Clip);

        Assert.Equal(2, layout.Lines.Count);
        Assert.Equal("Hello", LineText(layout.Lines[0]));
        Assert.Equal("World", LineText(layout.Lines[1]));
    }

    [Fact]
    public void Ellipsis_AndClip_ProduceDistinctCachedLayouts()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var clip = pipeline.Layout("Hello World Foo Bar", DefaultStyle, 60, maxLines: 1, TextOverflow.Clip);
        var ellipsis = pipeline.Layout("Hello World Foo Bar", DefaultStyle, 60, maxLines: 1, TextOverflow.Ellipsis);
        Assert.NotSame(clip, ellipsis);
    }

    // ── Letter spacing ──

    [Fact]
    public void LetterSpacing_WidensLineByAdvancePerGlyph()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        // "Test" = 4 glyphs. Natural 40px; +5px after each glyph = 40 + 5*4 = 60.
        var style = DefaultStyle with { LetterSpacing = 5f };
        var layout = pipeline.Layout("Test", style, 500, null);

        Assert.Single(layout.Lines);
        Assert.Equal(60, layout.Lines[0].Width, 0.1f);
    }

    [Fact]
    public void LetterSpacing_ProducesDistinctLayoutFromDefault()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        var normal = pipeline.Layout("Test", DefaultStyle, 500, null);
        var spaced = pipeline.Layout("Test", DefaultStyle with { LetterSpacing = 5f }, 500, null);
        Assert.NotSame(normal, spaced);
        Assert.True(spaced.Size.Width > normal.Size.Width);
    }

    // ── Line height ──

    [Fact]
    public void LineHeight_SetsLineBoxHeightAndCentersText()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        // Stub metrics at 16px: ascent 12, descent 4, natural height 16. A 30px
        // line-height adds 14px split evenly: ascent 19, height 30.
        var layout = pipeline.Layout("Hi", DefaultStyle with { LineHeight = 30f }, 500, null);

        Assert.Single(layout.Lines);
        Assert.Equal(30, layout.Lines[0].Height, 0.1f);
        Assert.Equal(19, layout.Lines[0].Ascent, 0.1f);
    }

    [Fact]
    public void LineHeight_SmallerThanText_IsIgnored()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        // 5px < the 16px natural height, so the natural metrics stand.
        var layout = pipeline.Layout("Hi", DefaultStyle with { LineHeight = 5f }, 500, null);
        Assert.Equal(16, layout.Lines[0].Height, 0.1f);
    }

    // ── Italic / slant keying ──

    [Fact]
    public void Slant_ProducesDistinctCachedLayout()
    {
        var pipeline = CreatePipeline(charWidth: 10);
        // The stub ignores slant when shaping, but slant is part of the shape key,
        // so the italic layout must be a separately computed instance — proving the
        // slant is threaded through to the itemizer/shaper.
        var upright = pipeline.Layout("Test", DefaultStyle, 500, null);
        var italic = pipeline.Layout("Test", DefaultStyle with { FontSlant = FontSlant.Italic }, 500, null);
        Assert.NotSame(upright, italic);
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
