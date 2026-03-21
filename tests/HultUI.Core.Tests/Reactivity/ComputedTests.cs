using HultUI.Core.Reactivity;
using Xunit;

namespace HultUI.Core.Tests.Reactivity;

[Collection("Reactive")]
public class ComputedTests
{
    [Fact]
    public void Computed_DerivesFromSignal()
    {
        var a = new Signal<int>(3);
        var doubled = new Computed<int>(() => a.Value * 2);
        Assert.Equal(6, doubled.Value);
    }

    [Fact]
    public void Computed_UpdatesWhenDependencyChanges()
    {
        var a = new Signal<int>(1);
        var doubled = new Computed<int>(() => a.Value * 2);
        Assert.Equal(2, doubled.Value);

        a.Value = 5;
        Assert.Equal(10, doubled.Value);
    }

    [Fact]
    public void Computed_IsLazy_DoesNotRecomputeUntilRead()
    {
        int computeCount = 0;
        var a = new Signal<int>(1);
        var doubled = new Computed<int>(() =>
        {
            computeCount++;
            return a.Value * 2;
        });

        Assert.Equal(0, computeCount); // not yet read
        _ = doubled.Value;
        Assert.Equal(1, computeCount);
        _ = doubled.Value; // cached, no recompute
        Assert.Equal(1, computeCount);

        a.Value = 2; // marks dirty but doesn't recompute
        Assert.Equal(1, computeCount);
        _ = doubled.Value; // now recomputes
        Assert.Equal(2, computeCount);
    }

    [Fact]
    public void Computed_ChainsWork()
    {
        var a = new Signal<int>(2);
        var b = new Computed<int>(() => a.Value * 3);
        var c = new Computed<int>(() => b.Value + 10);

        Assert.Equal(16, c.Value); // 2*3+10
        a.Value = 4;
        Assert.Equal(22, c.Value); // 4*3+10
    }

    [Fact]
    public void Computed_DynamicDependencies()
    {
        var flag = new Signal<bool>(true);
        var a = new Signal<int>(1);
        var b = new Signal<int>(2);
        var result = new Computed<int>(() => flag.Value ? a.Value : b.Value);

        Assert.Equal(1, result.Value);

        // Changing b shouldn't trigger recompute since flag=true reads a
        b.Value = 20;
        // result is still 1 (b is not a dependency when flag=true)

        flag.Value = false;
        Assert.Equal(20, result.Value);

        // Now a is not a dependency
        a.Value = 100;
        Assert.Equal(20, result.Value); // still reads b
    }

    [Fact]
    public void Computed_DoesNotPropagateIfValueUnchanged()
    {
        var a = new Signal<int>(5);
        var clamped = new Computed<int>(() => Math.Min(a.Value, 10));
        int effectRunCount = 0;
        var effect = new Effect(() =>
        {
            _ = clamped.Value;
            effectRunCount++;
        });

        Assert.Equal(1, effectRunCount);
        a.Value = 3; // clamped changes from 5 to 3
        Assert.Equal(2, effectRunCount);
        a.Value = 7; // clamped changes from 3 to 7
        Assert.Equal(3, effectRunCount);
        a.Value = 15; // clamped stays 10
        Assert.Equal(4, effectRunCount);
        a.Value = 20; // clamped stays 10 — should NOT re-run effect
        Assert.Equal(4, effectRunCount);
    }

    [Fact]
    public void Peek_DoesNotTrackOrRecompute()
    {
        var a = new Signal<int>(3);
        var doubled = new Computed<int>(() => a.Value * 2);
        _ = doubled.Value; // initial compute

        a.Value = 10;
        Assert.Equal(6, doubled.Peek()); // stale cached value, no recompute
    }
}
