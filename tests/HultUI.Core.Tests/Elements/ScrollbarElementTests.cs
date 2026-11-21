using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using Xunit;
// The builder: this assembly also sees the internal Elements.Scrollbar that paints it.
using ScrollbarBuilder = HultUI.Core.Builders.Scrollbar;

namespace HultUI.Core.Tests.Elements;

public class ScrollbarElementTests
{
    private const long FiftyGiB = 50L << 30;

    [Fact]
    public void Lengths_ClampAtZero_AndValue_ClampsToMaxValue()
    {
        var bar = ScrollbarElement.New();
        bar.ContentLength = -5;
        bar.ViewportLength = -5;
        Assert.Equal(0, bar.ContentLength);
        Assert.Equal(0, bar.ViewportLength);

        bar.ContentLength = 100;
        bar.ViewportLength = 10;
        bar.Value = 1000;
        Assert.Equal(90, bar.MaxValue);
        Assert.Equal(90, bar.Value);
        bar.Value = -1;
        Assert.Equal(0, bar.Value);
    }

    [Fact]
    public void ShrinkingTheContent_ReclampsTheValue()
    {
        var bar = ScrollbarBuilder.Vertical(80, 100, 10);
        Assert.Equal(80, bar.Value);

        bar.ContentLength = 50;

        Assert.Equal(40, bar.Value);
    }

    [Fact]
    public void TheBuilder_SetsTheLengthsBeforeTheValue()
    {
        var bar = ScrollbarBuilder.Horizontal(FiftyGiB - 4096, FiftyGiB, 4096);

        Assert.Equal(ScrollbarAxis.Horizontal, bar.Axis);
        Assert.Equal(FiftyGiB - 4096, bar.Value);
    }

    [Fact]
    public void LargeChangeZero_MeansTheViewportLength()
    {
        var bar = ScrollbarBuilder.Vertical(0, 1000, 37);
        Assert.Equal(37, ((IRangeValueElement)bar).LargeChange);

        bar.LargeChange = 5;
        Assert.Equal(5, ((IRangeValueElement)bar).LargeChange);
    }

    [Fact]
    public void AVerticalBar_MeasuresItsHitWidthAcross()
    {
        var bar = ScrollbarBuilder.Vertical(0, 1000, 10);
        var row = HultUI.Core.Builders.Flex.Row();
        row.AddChild(bar);
        new LayoutEngine(new StubMeasureContext()).PerformLayout(row, 100, 300);

        // The expanded width even at rest: a hairline bar is a hairline target.
        Assert.Equal(12f, bar.Layout.AbsoluteBorderBox.Width);
        Assert.Equal(300f, bar.Layout.AbsoluteBorderBox.Height);
    }

    [Fact]
    public void AtMaxValue_TheThumbEndsAtTheTracksEnd_OverFiftyGiB()
    {
        var bar = Laid(ScrollbarBuilder.Vertical(0, FiftyGiB, 4096));
        bar.Value = bar.MaxValue;

        var ctx = new DrawnRects();
        bar.Render(ctx);

        Assert.Equal(2, ctx.Rects.Count);
        var thumb = ctx.Rects[1];
        Assert.Equal(300f, thumb.Y + thumb.Height, 3);
        // A visible share of 4 KiB in 50 GiB is far under a pixel: the shortest thumb.
        Assert.Equal(20f, thumb.Height);
    }

    [Fact]
    public void NothingToScroll_DrawsNothing()
    {
        var bar = Laid(ScrollbarBuilder.Vertical(0, 10, 10));
        var ctx = new DrawnRects();

        bar.Render(ctx);

        Assert.Empty(ctx.Rects);
    }

    [Fact]
    public void ATrackClick_AtTheEnd_IsMaxValueExactly_AndAtTheStart_Zero()
    {
        var bar = Laid(ScrollbarBuilder.Vertical(0, FiftyGiB, 4096));
        var seen = new List<long>();
        bar.OnChanged = seen.Add;

        Press(bar, 299);
        Release(bar, 299);
        Press(bar, 1);
        Release(bar, 1);

        Assert.Equal([bar.MaxValue, 0L], seen);
        Assert.False(bar.IsDragging);
    }

    [Fact]
    public void ADragToTheEnd_OfTheLargestRange_IsMaxValue_WithoutOverflow()
    {
        var bar = Laid(ScrollbarBuilder.Vertical(0, long.MaxValue, 1));
        long? last = null;
        bar.OnChanged = v => last = v;

        Press(bar, 5);
        Assert.True(bar.IsDragging);
        Move(bar, 150);
        Assert.InRange(last!.Value, 1, bar.MaxValue - 1);
        Move(bar, 10_000);
        Release(bar, 10_000);

        Assert.Equal(long.MaxValue - 1, bar.MaxValue);
        Assert.Equal(bar.MaxValue, last);
        Assert.Equal(bar.MaxValue, bar.Value);
    }

    [Fact]
    public void ATrackShorterThanTheShortestThumb_KeepsTheValue_WhenDragged()
    {
        var bar = ScrollbarBuilder.Vertical(500, FiftyGiB, 4096);
        new LayoutEngine(new StubMeasureContext()).PerformLayout(bar, 12, 15);
        int calls = 0;
        bar.OnChanged = _ => calls++;

        Press(bar, 5);                               // the thumb is the whole track
        Move(bar, 12);
        Release(bar, 12);

        Assert.Equal(500, bar.Value);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void AValueABuildSets_DoesNotCallOnChanged()
    {
        var bar = ScrollbarBuilder.Vertical(0, 1000, 10);
        int calls = 0;
        bar.OnChanged = _ => calls++;

        bar.Value = 500;

        Assert.Equal(0, calls);
    }

    [Fact]
    public void PatchFrom_CopiesTheLengthsBeforeTheValue()
    {
        var live = ScrollbarBuilder.Vertical(0, 10, 0);
        var next = ScrollbarBuilder.Vertical(900, 1000, 10);
        Action<long> onChanged = _ => { };
        next.OnChanged = onChanged;
        next.SmallChange = 3;

        live.PatchFrom(next);

        Assert.Equal(1000, live.ContentLength);
        Assert.Equal(10, live.ViewportLength);
        Assert.Equal(900, live.Value);
        Assert.Equal(3, live.SmallChange);
        Assert.Same(onChanged, live.OnChanged);
    }

    [Fact]
    public void APatchDuringADrag_KeepsTheDraggedValue()
    {
        var live = Laid(ScrollbarBuilder.Vertical(0, 1000, 100));
        Press(live, 5);
        Move(live, 150);
        long dragged = live.Value;
        Assert.NotEqual(0, dragged);

        // A rebuild that has not seen OnChanged yet still says 0.
        live.PatchFrom(ScrollbarBuilder.Vertical(0, 1000, 100));

        Assert.Equal(dragged, live.Value);
        Release(live, 150);
        live.PatchFrom(ScrollbarBuilder.Vertical(0, 1000, 100));
        Assert.Equal(0, live.Value);
    }

    [Fact]
    public void RangeValue_ReportsTheRange()
    {
        var bar = ScrollbarBuilder.Vertical(40, 1000, 100);
        bar.SmallChange = 7;
        var r = (IRangeValueElement)bar;

        Assert.Equal(40, r.Value);
        Assert.Equal(0, r.Minimum);
        Assert.Equal(900, r.Maximum);
        Assert.Equal(7, r.SmallChange);
        Assert.Equal(100, r.LargeChange);
        Assert.False(r.IsReadOnly);
    }

    [Fact]
    public void SetAccessibleValue_ClampsToMaxValue_AndCallsOnChanged_ButIgnoresNaN()
    {
        var bar = ScrollbarBuilder.Vertical(0, FiftyGiB, 4096);
        var seen = new List<long>();
        bar.OnChanged = seen.Add;

        ((IRangeValueElement)bar).SetAccessibleValue(1e30);
        ((IRangeValueElement)bar).SetAccessibleValue(double.NaN);

        Assert.Equal([bar.MaxValue], seen);
        Assert.Equal(bar.MaxValue, bar.Value);
    }

    [Fact]
    public void Keys_StepAndSaturate_OnlyWhileFocused()
    {
        var bar = ScrollbarBuilder.Vertical(0, long.MaxValue, 1);
        bar.IsFocusable = true;

        Key(bar, KeyCode.Down);
        Assert.Equal(0, bar.Value);                  // not focused: the document owns the keys

        EventDispatcher.DispatchDirect(new FocusEvent(), bar);
        Key(bar, KeyCode.Down);
        Assert.Equal(1, bar.Value);
        bar.LargeChange = long.MaxValue;
        Key(bar, KeyCode.End);
        Key(bar, KeyCode.PageDown);                  // past the end saturates instead of wrapping
        Assert.Equal(bar.MaxValue, bar.Value);
        Key(bar, KeyCode.PageUp);
        Assert.Equal(0, bar.Value);
        Key(bar, KeyCode.Left);                      // not this bar's axis
        Assert.Equal(0, bar.Value);
    }

    private static ScrollbarElement Laid(ScrollbarElement bar)
    {
        new LayoutEngine(new StubMeasureContext()).PerformLayout(bar, 12, 300);
        return bar;
    }

    private static void Press(ScrollbarElement bar, float y) =>
        EventDispatcher.DispatchDirect(
            new PointerDownEvent { Position = new Point(6, y), Button = PointerEvent.LeftButton }, bar);

    private static void Move(ScrollbarElement bar, float y) =>
        EventDispatcher.DispatchDirect(new PointerMoveEvent { Position = new Point(6, y) }, bar);

    private static void Release(ScrollbarElement bar, float y) =>
        EventDispatcher.DispatchDirect(
            new PointerUpEvent { Position = new Point(6, y), Button = PointerEvent.LeftButton }, bar);

    private static void Key(ScrollbarElement bar, KeyCode key) =>
        EventDispatcher.DispatchDirect(new KeyDownEvent { Key = key }, bar);

    /// <summary>Records the rects the bar draws: the track, then the thumb.</summary>
    private sealed class DrawnRects : NullRenderContext
    {
        public readonly List<Rect> Rects = [];
        public override void DrawRoundedRect(Rect rect, CornerRadius radius, Color fill, Color stroke, Thickness strokeWidth) =>
            Rects.Add(rect);
    }
}
