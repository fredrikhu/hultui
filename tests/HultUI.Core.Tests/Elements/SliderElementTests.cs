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
