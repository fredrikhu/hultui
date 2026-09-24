using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Core.Tests.Elements;

public class SliderElementTests
{
    [Fact]
    public void New_ClampsInitialValueToRange()
    {
        Assert.Equal(100, SliderElement.New(150, 0, 100).Value);
        Assert.Equal(0, SliderElement.New(-5, 0, 100).Value);
    }

    [Fact]
    public void Value_SnapsToStep()
    {
        var s = SliderElement.New(0, 0, 100);
        s.Step = 10;
        s.Value = 23;
        Assert.Equal(20, s.Value);
        s.Value = 27;
        Assert.Equal(30, s.Value);
    }

    [Fact]
    public void SetAccessibleValue_ClampsAndFiresChanged()
    {
        var s = SliderElement.New(0, 0, 100);
        double? changed = null;
        s.OnChanged = v => changed = v;

        ((IRangeValueElement)s).SetAccessibleValue(75);

        Assert.Equal(75, s.Value);
        Assert.Equal(75, changed);
    }

    [Fact]
    public void HoveringReportsTheValueUnderThePointerWithoutChangingIt()
    {
        var s = SliderElement.New(0, 0, 100);
        s.Step = 0;                                   // read the track, not a snapped value
        s.ThumbRadius = 0;                            // the whole width is track
        new HultUI.Core.Layout.LayoutEngine(new StubMeasureContext()).PerformLayout(s, 200, 20);

        var seen = new List<double?>();
        s.OnHoverValue = v => seen.Add(v);

        // LocalPosition is derived from the target's content box, so the window position
        // is what an event carries.
        HultUI.Core.Events.EventDispatcher.DispatchDirect(
            new HultUI.Core.Events.PointerMoveEvent { Position = new HultUI.Core.Primitives.Point(100, 10) }, s);
        HultUI.Core.Events.EventDispatcher.DispatchDirect(
            new HultUI.Core.Events.PointerLeaveEvent { Position = new HultUI.Core.Primitives.Point(0, 0) }, s);

        Assert.Equal([50d, null], seen);
        // Pointing is not setting: the value only moves on a press or a drag.
        Assert.Equal(0, s.Value);
    }

    [Fact]
    public void ChapterMarksCutTheTrackIntoPieces()
    {
        var s = SliderElement.New(0, 0, 100);
        s.ThumbRadius = 0;
        s.Segments = [25d, 50d];
        Layout(s, 200);

        var ctx = new TrackRecordingContext();
        s.Render(ctx);

        // Three pieces, left to right, with a gap taken out before each cut — so the two
        // that are followed by one are shorter than their share, and the last is not.
        Assert.Equal(3, ctx.Tracks.Count);
        Assert.True(ctx.Tracks[0].X < ctx.Tracks[1].X && ctx.Tracks[1].X < ctx.Tracks[2].X);
        Assert.True(ctx.Tracks[0].Width < 50f, $"{ctx.Tracks[0].Width} kept its gap");
        Assert.True(ctx.Tracks[1].Width < 50f, $"{ctx.Tracks[1].Width} kept its gap");
        Assert.Equal(100f, ctx.Tracks[2].Width, 1);
        Assert.True(ctx.Tracks[0].Right < ctx.Tracks[1].X, "the pieces run together");
        // A mark outside the range is not a cut.
        s.Segments = [25d, 500d];
        var second = new TrackRecordingContext();
        s.Render(second);
        Assert.Equal(2, second.Tracks.Count);
    }

    [Fact]
    public void NoMarksIsOneContinuousTrackAsItAlwaysWas()
    {
        var s = SliderElement.New(40, 0, 100);
        s.ThumbRadius = 0;
        Layout(s, 200);

        var ctx = new TrackRecordingContext();
        s.Render(ctx);

        Assert.Single(ctx.Tracks);
        Assert.Equal(200f, ctx.Tracks[0].Width, 1);
    }

    [Fact]
    public void PointingAtTheTrackFillsItToThePointerOnlyWhenAsked()
    {
        var s = SliderElement.New(10, 0, 100);
        s.ThumbRadius = 0;
        s.Step = 0;
        Layout(s, 200);
        Hover(s, 150);                       // three quarters along

        var off = new TrackRecordingContext();
        s.Render(off);
        // Track plus the played fill, and nothing for the pointer.
        Assert.Equal(2, off.Fills.Count);

        s.ShowHoverFill = true;
        Hover(s, 150);
        var on = new TrackRecordingContext();
        s.Render(on);

        // Track, hover fill to the pointer, then the played fill over it.
        Assert.Equal(3, on.Fills.Count);
        Assert.Equal(150f, on.Fills[1].Width, 1);
        Assert.Equal(20f, on.Fills[2].Width, 1);
        Assert.Equal(10, s.Value);           // pointing is not setting
    }

    private static void Layout(SliderElement s, float width) =>
        new HultUI.Core.Layout.LayoutEngine(new StubMeasureContext()).PerformLayout(s, width, 20);

    private static void Hover(SliderElement s, float x) =>
        HultUI.Core.Events.EventDispatcher.DispatchDirect(
            new HultUI.Core.Events.PointerMoveEvent { Position = new HultUI.Core.Primitives.Point(x, 10) }, s);

    /// <summary>Records the rounded rectangles the slider paints, in order.</summary>
    private sealed class TrackRecordingContext : HultUI.Core.Tests.Elements.NullRenderContext
    {
        public List<HultUI.Core.Primitives.Rect> Fills { get; } = [];

        /// <summary>The unfilled pieces: one per segment, each drawn before its fills.</summary>
        public List<HultUI.Core.Primitives.Rect> Tracks { get; } = [];

        public override void DrawRoundedRect(HultUI.Core.Primitives.Rect rect,
            HultUI.Core.Primitives.CornerRadius radius, HultUI.Core.Primitives.Color fill,
            HultUI.Core.Primitives.Color stroke, HultUI.Core.Primitives.Thickness strokeWidth)
        {
            // The thumb is a circle at the end; the track pieces are the wide flat ones.
            // A test that zeroes the thumb radius still draws it, as an empty rectangle.
            if (rect.Height > 8f || rect.Width <= 0 || rect.Height <= 0) return;
            Fills.Add(rect);
            if (fill.A == 255 && fill.R == 90 && fill.G == 90 && fill.B == 100) Tracks.Add(rect);
        }
    }

    [Fact]
    public void RangeValue_ExposesBounds()
    {
        var s = SliderElement.New(40, 10, 90);
        s.Step = 5;
        var r = (IRangeValueElement)s;
        Assert.Equal(40, r.Value);
        Assert.Equal(10, r.Minimum);
        Assert.Equal(90, r.Maximum);
        Assert.Equal(5, r.SmallChange);
        Assert.False(r.IsReadOnly);
    }
}
