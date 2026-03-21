using HultUI.Core.Reactivity;
using Xunit;

namespace HultUI.Core.Tests.Reactivity;

[Collection("Reactive")]
public class UntrackedTests
{
    [Fact]
    public void Untracked_Action_DoesNotTrack()
    {
        var signal = new Signal<int>(1);
        int runCount = 0;

        var effect = new Effect(() =>
        {
            runCount++;
            ReactiveScope.Untracked(() =>
            {
                _ = signal.Value; // read inside Untracked
            });
        });

        Assert.Equal(1, runCount);
        signal.Value = 2; // should NOT trigger effect
        Assert.Equal(1, runCount);
    }

    [Fact]
    public void Untracked_Func_ReturnsValue()
    {
        var signal = new Signal<int>(42);
        var result = ReactiveScope.Untracked(() => signal.Value);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Untracked_Nested_InEffect()
    {
        var tracked = new Signal<int>(1);
        var untracked = new Signal<int>(100);
        int runCount = 0;
        int observed = -1;

        var effect = new Effect(() =>
        {
            runCount++;
            var t = tracked.Value;
            var u = ReactiveScope.Untracked(() => untracked.Value);
            observed = t + u;
        });

        Assert.Equal(1, runCount);
        Assert.Equal(101, observed);

        untracked.Value = 200; // should NOT re-trigger
        Assert.Equal(1, runCount);

        tracked.Value = 5; // should re-trigger, picks up latest untracked
        Assert.Equal(2, runCount);
        Assert.Equal(205, observed);
    }
}
