using HultUI.Core.Reactivity;
using Xunit;

namespace HultUI.Core.Tests.Reactivity;

[Collection("Reactive")]
public class SignalTests
{
    [Fact]
    public void InitialValue_IsAccessible()
    {
        var signal = new Signal<int>(42);
        Assert.Equal(42, signal.Value);
    }

    [Fact]
    public void SetValue_UpdatesValue()
    {
        var signal = new Signal<string>("hello");
        signal.Value = "world";
        Assert.Equal("world", signal.Value);
    }

    [Fact]
    public void SetSameValue_DoesNotNotify()
    {
        var signal = new Signal<int>(5);
        int notifyCount = 0;
        var effect = new Effect(() =>
        {
            _ = signal.Value;
            notifyCount++;
        });

        Assert.Equal(1, notifyCount); // initial run
        signal.Value = 5; // same value
        Assert.Equal(1, notifyCount); // no re-run
    }

    [Fact]
    public void Peek_DoesNotTrackDependency()
    {
        var signal = new Signal<int>(1);
        int runCount = 0;
        var effect = new Effect(() =>
        {
            _ = signal.Peek();
            runCount++;
        });

        Assert.Equal(1, runCount);
        signal.Value = 2;
        Assert.Equal(1, runCount); // effect not re-triggered
    }

    [Fact]
    public void Signal_NotifiesEffect_OnChange()
    {
        var signal = new Signal<int>(0);
        int observed = -1;
        var effect = new Effect(() => observed = signal.Value);

        Assert.Equal(0, observed);
        signal.Value = 10;
        Assert.Equal(10, observed);
        signal.Value = 20;
        Assert.Equal(20, observed);
    }

    [Fact]
    public void ToString_ShowsValue()
    {
        var signal = new Signal<int>(99);
        Assert.Equal("Signal(99)", signal.ToString());
    }
}
