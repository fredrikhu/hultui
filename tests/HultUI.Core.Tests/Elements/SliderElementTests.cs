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
