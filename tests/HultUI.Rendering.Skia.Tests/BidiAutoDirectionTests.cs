using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// BiDi stage 3 (docs/plans/14-bidi.md): a paragraph whose direction comes from
/// its own first strong character (UAX #9 P2/P3), alignment that follows the
/// direction the text resolved to, and arrow keys that move the caret by what is
/// next on screen rather than next in the string.
/// </summary>
public class BidiAutoDirectionTests
{
    private static ResolvedInheritedStyle Style(
        TextDirection direction = TextDirection.Ltr, TextAlign align = TextAlign.Start) => new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: Color.Black, TextDirection: direction, Cursor: CursorType.Default,
        TextAlign: align);

    private static TextPipeline Pipeline() => new(new SkiaFontCollection(), new SkiaTextShaper());

    // ── First-strong paragraph direction ──

    [Fact]
    public void AutoDirection_TakesRtlFromAHebrewFirstWord()
    {
        var layout = Pipeline().Layout("אבג abc דהו", Style(TextDirection.Auto), 10_000, null);
        Assert.True(layout.IsRtlParagraph);
    }

    [Fact]
    public void AutoDirection_TakesLtrFromALatinFirstWord()
    {
        var layout = Pipeline().Layout("abc אבג def", Style(TextDirection.Auto), 10_000, null);
        Assert.False(layout.IsRtlParagraph);
    }

    [Fact]
    public void AutoDirection_IgnoresLeadingNeutrals_AndFindsTheFirstStrongCharacter()
    {
        // Digits and punctuation are not strong: "123 - אבג" is a Hebrew paragraph.
        var layout = Pipeline().Layout("123 - אבג", Style(TextDirection.Auto), 10_000, null);
        Assert.True(layout.IsRtlParagraph);
    }

    [Fact]
    public void AutoDirection_OrdersRunsLikeAnExplicitlyRtlParagraph()
    {
        var pipeline = Pipeline();
        const string text = "אבג abc דהו";
        var auto = pipeline.Layout(text, Style(TextDirection.Auto), 10_000, null);
        var rtl = pipeline.Layout(text, Style(TextDirection.Rtl), 10_000, null);

        Assert.Equal(rtl.Lines[0].Runs.Count, auto.Lines[0].Runs.Count);
        for (int i = 0; i < rtl.Lines[0].Runs.Count; i++)
            Assert.Equal(rtl.Lines[0].Runs[i].OffsetX, auto.Lines[0].Runs[i].OffsetX, 1);
    }

    [Fact]
    public void AutoDirection_LeavesLatinTextOnTheUntouchedPath()
    {
        var pipeline = Pipeline();
        const string text = "hello wonderful world";
        var auto = pipeline.Layout(text, Style(TextDirection.Auto), 10_000, null);
        var ltr = pipeline.Layout(text, Style(), 10_000, null);

        Assert.False(auto.IsRtlParagraph);
        Assert.Equal(ltr.Size.Width, auto.Size.Width, 2);
    }

    // ── Alignment that follows the resolved direction ──

    [Theory]
    [InlineData(TextAlign.Start, false, 0f)]
    [InlineData(TextAlign.Start, true, 1f)]
    [InlineData(TextAlign.End, false, 1f)]
    [InlineData(TextAlign.End, true, 0f)]
    [InlineData(TextAlign.Left, true, 0f)]    // absolute: unaffected by direction
    [InlineData(TextAlign.Right, false, 1f)]
    [InlineData(TextAlign.Center, true, 0.5f)]
    public void AlignmentFactor_FollowsDirectionOnlyForStartAndEnd(
        TextAlign align, bool rtlParagraph, float expected)
        => Assert.Equal(expected, TextAlignment.Factor(align, rtlParagraph));

    [Fact]
    public void AnRtlParagraph_DrawsItsTextAgainstTheRightEdgeByDefault()
    {
        // Differential: the same Hebrew drawn into a box twice its width, once as
        // an Ltr paragraph and once as Rtl. Ink must move to the far side.
        float ltrCentre = InkCentreX("אבג", TextDirection.Ltr);
        float rtlCentre = InkCentreX("אבג", TextDirection.Rtl);
        Assert.True(rtlCentre > ltrCentre + 40f,
            $"Rtl ink centred at {rtlCentre} should sit well right of Ltr's {ltrCentre}");
    }

    [Fact]
    public void LatinText_DrawsInTheSamePlaceItAlwaysDid()
    {
        Assert.Equal(InkCentreX("abc", TextDirection.Ltr), InkCentreX("abc", TextDirection.Auto), 1);
    }

    private static float InkCentreX(string text, TextDirection direction)
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(300, 60, 1f);
        var ctx = surface.RenderContext;
        ctx.BeginFrame(300, 60, 1f, Color.White);
        ctx.DrawText(text, new Rect(0, 0, 280, 40), Style(direction), Core.Layout.TextOverflow.Clip, 1);
        ctx.EndFrame();
        surface.Present();

        var pixels = surface.ReadPixels(out int w, out _, out _);
        double sum = 0; int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 200 && pixels[i + 1] > 200 && pixels[i + 2] > 200) continue;
            sum += i / 4 % w;
            count++;
        }
        Assert.True(count > 0, "nothing was drawn");
        return (float)(sum / count);
    }

    // ── Visual caret movement ──

    [Fact]
    public void ArrowKeysOnAMixedLine_MoveTheCaretByWhatIsNextOnScreen()
    {
        const string text = "abc אבג def";
        var line = Pipeline().Layout(text, Style(), 10_000, null).Lines[0];

        // Walking right from the start of the line must never move the caret left.
        int offset = 0;
        float x = line.CaretXAt(0);
        for (int step = 0; step < line.DrawnLength; step++)
        {
            if (!line.TryMoveCaretVisually(offset, right: true, out int next)) break;
            float nextX = line.CaretXAt(next);
            Assert.True(nextX > x, $"step {step}: caret went from x={x} to x={nextX}");
            offset = next;
            x = nextX;
        }
        // And it reaches the far right edge of the line.
        Assert.Equal(line.Width, x, 1);
    }

    [Fact]
    public void ArrowKeysInsideHebrew_WalkTheTextInReadingOrder()
    {
        var line = Pipeline().Layout("אבג", Style(TextDirection.Rtl), 10_000, null).Lines[0];

        // The caret starts at the right (offset 0 in reading order). Pressing LEFT
        // advances through the Hebrew, one logical character at a time.
        Assert.True(line.TryMoveCaretVisually(0, right: false, out int after));
        Assert.Equal(1, after);
        Assert.True(line.TryMoveCaretVisually(1, right: false, out after));
        Assert.Equal(2, after);
    }

    [Fact]
    public void ALineWithNoRtl_DeclinesToMoveVisually()
    {
        // Latin text must keep the caller's own stepping — which knows about
        // surrogate pairs and clusters — rather than this geometry.
        var line = Pipeline().Layout("hello 👋 world", Style(), 10_000, null).Lines[0];
        Assert.False(line.TryMoveCaretVisually(3, right: true, out _));
    }
}
