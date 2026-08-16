using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// BiDi stage 2 (docs/plans/14-bidi.md): the offset↔x mapping a caret, a click
/// and a selection highlight all go through. Inside a right-to-left run a logical
/// prefix is a VISUAL SUFFIX, and a logical range that crosses a direction
/// boundary is visually discontiguous — neither is true of the prefix-width
/// arithmetic these three used to do. Text with no RTL in it must be untouched by
/// all of this, which the Latin cases below pin.
/// </summary>
public class BidiCaretMappingTests
{
    private static ResolvedInheritedStyle Style(TextDirection direction = TextDirection.Ltr) => new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: Color.Black, TextDirection: direction, Cursor: CursorType.Default);

    private static TextLine Line(string text, TextDirection direction = TextDirection.Ltr)
    {
        var pipeline = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        return pipeline.Layout(text, Style(direction), 10_000, null).Lines[0];
    }

    [Fact]
    public void CaretInsideAnRtlRun_MovesRightToLeft()
    {
        var line = Line("אבג", TextDirection.Rtl);

        // Logical offset 0 is the START of the reading order, which in a
        // right-to-left run is the RIGHT edge; the end of the text is at x = 0.
        Assert.Equal(line.Width, line.CaretXAt(0), 1);
        Assert.Equal(0f, line.CaretXAt(3), 1);
        for (int k = 1; k <= 3; k++)
            Assert.True(line.CaretXAt(k) < line.CaretXAt(k - 1),
                $"caret {k} at {line.CaretXAt(k)} should be left of caret {k - 1} at {line.CaretXAt(k - 1)}");
    }

    [Fact]
    public void CaretInLatinText_IsStillThePrefixWidth()
    {
        const string text = "hello world";
        var pipeline = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        var line = pipeline.Layout(text, Style(), 10_000, null).Lines[0];

        for (int k = 0; k <= text.Length; k++)
        {
            float prefix = pipeline.Layout(text[..k], Style(), 10_000, null).Size.Width;
            Assert.Equal(prefix, line.CaretXAt(k), 1);
        }
    }

    [Fact]
    public void HitTestingAMixedLine_RoundTripsEveryCaretPosition()
    {
        const string text = "abc אבג def";
        var line = Line(text);

        // At a boundary between two direction runs, two logical offsets share one
        // visual x, so a round trip may legitimately return the other one — what
        // must hold is that the caret lands back on the same pixel.
        for (int k = 0; k <= line.DrawnLength; k++)
        {
            float x = line.CaretXAt(k);
            int back = line.OffsetAtX(x);
            Assert.Equal(x, line.CaretXAt(back), 1);
        }
    }

    [Fact]
    public void HitTestingLatinText_RoundTripsToTheExactOffset()
    {
        var line = Line("hello world");
        for (int k = 0; k <= line.DrawnLength; k++)
            Assert.Equal(k, line.OffsetAtX(line.CaretXAt(k)));
    }

    [Fact]
    public void ClickingTheLeftHalfOfTheFirstCharacter_PutsTheCaretBeforeIt()
    {
        var line = Line("hello");
        Assert.Equal(0, line.OffsetAtX(line.CaretXAt(1) * 0.25f));
        Assert.Equal(1, line.OffsetAtX(line.CaretXAt(1) * 0.75f));
    }

    [Fact]
    public void ASelectionCrossingADirectionBoundary_IsTwoSeparateRects()
    {
        // "abc אבג def" — selecting from inside "abc" to inside the Hebrew covers
        // "bc " on the left and the RIGHT part of the Hebrew run, with the rest of
        // the Hebrew visually in between. One rect would highlight text that is
        // not selected.
        const string text = "abc אבג def";
        var line = Line(text);

        var ranges = line.VisualRanges(1, 6);
        Assert.Equal(2, ranges.Count);
        Assert.True(ranges[0].X1 < ranges[1].X0, "the two pieces must not touch");

        // The unselected Hebrew character sits in the gap between them.
        float gapMiddle = (ranges[0].X1 + ranges[1].X0) / 2;
        int offsetInGap = line.OffsetAtX(gapMiddle);
        Assert.True(offsetInGap >= 6, $"expected an unselected offset in the gap, got {offsetInGap}");
    }

    [Fact]
    public void ASelectionInLatinText_IsExactlyOneRect()
    {
        var line = Line("hello world");
        var ranges = line.VisualRanges(2, 7);
        Assert.Single(ranges);
        Assert.Equal(line.CaretXAt(2), ranges[0].X0, 1);
        Assert.Equal(line.CaretXAt(7), ranges[0].X1, 1);
    }

    // ── Through the render context, the way TextInput and TextArea see it ──

    [Fact]
    public void TheRenderContext_ExposesTheSameMappingItPaintsWith()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(300, 60, 1f);
        var ctx = surface.RenderContext;
        const string text = "abc אבג def";

        var line = Line(text);
        for (int k = 0; k <= line.DrawnLength; k++)
            Assert.Equal(line.CaretXAt(k), ctx.TextCaretX(text, Style(), k), 1);

        var ranges = ctx.TextSelectionRanges(text, Style(), 1, 6);
        Assert.Equal(2, ranges.Count);
        Assert.Equal(6, ctx.TextOffsetAtX(text, Style(), line.CaretXAt(6)));
    }

    [Fact]
    public void TheRenderContextMapping_MatchesPrefixWidthsForLatinText()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(300, 60, 1f);
        var ctx = surface.RenderContext;
        const string text = "hello world";

        for (int k = 0; k <= text.Length; k++)
            Assert.Equal(ctx.MeasureText(text[..k], Style()).Width, ctx.TextCaretX(text, Style(), k), 1);

        var ranges = ctx.TextSelectionRanges(text, Style(), 0, text.Length);
        Assert.Single(ranges);
        Assert.Equal(0f, ranges[0].X0, 1);
        Assert.Equal(ctx.MeasureText(text, Style()).Width, ranges[0].X1, 1);
    }
}
