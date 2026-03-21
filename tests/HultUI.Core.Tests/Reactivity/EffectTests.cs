using HultUI.Core.Reactivity;
using Xunit;

namespace HultUI.Core.Tests.Reactivity;

[Collection("Reactive")]
public class EffectTests
{
    [Fact]
    public void Effect_RunsImmediately()
    {
        int ran = 0;
        var effect = new Effect(() => ran++);
        Assert.Equal(1, ran);
    }

    [Fact]
    public void Effect_RerunsOnDependencyChange()
    {
        var name = new Signal<string>("Alice");
        string observed = "";
        var effect = new Effect(() => observed = name.Value);

        Assert.Equal("Alice", observed);
        name.Value = "Bob";
        Assert.Equal("Bob", observed);
    }

    [Fact]
    public void Effect_Dispose_StopsRerunning()
    {
        var signal = new Signal<int>(0);
        int runCount = 0;
        var effect = new Effect(() =>
        {
            _ = signal.Value;
            runCount++;
        });

        Assert.Equal(1, runCount);
        signal.Value = 1;
        Assert.Equal(2, runCount);

        effect.Dispose();
        signal.Value = 2;
        Assert.Equal(2, runCount); // no more runs
    }

    [Fact]
    public void Effect_TracksComputedDependencies()
    {
        var a = new Signal<int>(2);
        var b = new Signal<int>(3);
        var sum = new Computed<int>(() => a.Value + b.Value);

        int observed = -1;
        var effect = new Effect(() => observed = sum.Value);

        Assert.Equal(5, observed);
        a.Value = 10;
        Assert.Equal(13, observed);
        b.Value = 7;
        Assert.Equal(17, observed);
    }

    [Fact]
    public void Effect_DynamicDependencies_Cleanup()
    {
        var useA = new Signal<bool>(true);
        var a = new Signal<int>(1);
        var b = new Signal<int>(2);
        int runCount = 0;

        var effect = new Effect(() =>
        {
            runCount++;
            if (useA.Value)
                _ = a.Value;
            else
                _ = b.Value;
        });

        Assert.Equal(1, runCount);

        a.Value = 10; // should trigger (useA=true, so a is tracked)
        Assert.Equal(2, runCount);

        useA.Value = false; // switch to b
        Assert.Equal(3, runCount);

        a.Value = 20; // should NOT trigger (a is no longer tracked)
        Assert.Equal(3, runCount);

        b.Value = 30; // should trigger (b is now tracked)
        Assert.Equal(4, runCount);
    }
}
