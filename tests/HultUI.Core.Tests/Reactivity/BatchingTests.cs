using HultUI.Core.Reactivity;
using Xunit;

namespace HultUI.Core.Tests.Reactivity;

[Collection("Reactive")]
public class BatchingTests
{
    [Fact]
    public void Batch_CoalescesMultipleChanges()
    {
        var a = new Signal<int>(1);
        var b = new Signal<int>(2);
        int runCount = 0;
        int observedSum = -1;

        var effect = new Effect(() =>
        {
            runCount++;
            observedSum = a.Value + b.Value;
        });

        Assert.Equal(1, runCount);
        Assert.Equal(3, observedSum);

        ReactiveRuntime.Batch(() =>
        {
            a.Value = 10;
            b.Value = 20;
        });

        // Effect should have run only once more, not twice
        Assert.Equal(2, runCount);
        Assert.Equal(30, observedSum);
    }

    [Fact]
    public void Batch_NestedBatches_FlushOnOuterComplete()
    {
        var signal = new Signal<int>(0);
        int runCount = 0;

        var effect = new Effect(() =>
        {
            _ = signal.Value;
            runCount++;
        });

        Assert.Equal(1, runCount);

        ReactiveRuntime.Batch(() =>
        {
            signal.Value = 1;
            ReactiveRuntime.Batch(() =>
            {
                signal.Value = 2;
            });
            // inner batch ended but outer is still open — no flush yet
            Assert.Equal(1, runCount);
            signal.Value = 3;
        });

        // outer batch ended — flush
        Assert.Equal(2, runCount);
    }

    [Fact]
    public void WithoutBatch_EffectsRunImmediately()
    {
        var signal = new Signal<int>(0);
        var log = new List<int>();

        var effect = new Effect(() => log.Add(signal.Value));

        Assert.Equal([0], log);

        signal.Value = 1;
        signal.Value = 2;
        signal.Value = 3;

        // Without batching, each change triggers immediately
        Assert.Equal([0, 1, 2, 3], log);
    }

    [Fact]
    public void DiamondDependency_EffectRunsOnce()
    {
        //     a
        //    / \
        //   b   c
        //    \ /
        //   effect
        var a = new Signal<int>(1);
        var b = new Computed<int>(() => a.Value * 2);
        var c = new Computed<int>(() => a.Value * 3);

        int runCount = 0;
        int observedSum = -1;

        var effect = new Effect(() =>
        {
            runCount++;
            observedSum = b.Value + c.Value;
        });

        Assert.Equal(1, runCount);
        Assert.Equal(5, observedSum); // 2+3

        ReactiveRuntime.Batch(() =>
        {
            a.Value = 2;
        });

        Assert.Equal(2, runCount); // only one re-run despite two paths
        Assert.Equal(10, observedSum); // 4+6
    }

    [Fact]
    public void CycleDetection_ThrowsAfterLimit()
    {
        // Two effects that ping-pong: effect1 forwards a→b, effect2 forwards b→a.
        // After both are fully constructed, setting a triggers an infinite loop.
        var a = new Signal<int>(0);
        var b = new Signal<int>(0);

        var effect1 = new Effect(() => b.Value = a.Value + 1);
        var effect2 = new Effect(() => a.Value = b.Value + 1);

        // Both effects are now constructed and subscribed.
        // Triggering a should cause an infinite ping-pong.
        Assert.Throws<InvalidOperationException>(() => a.Value = 100);

        effect1.Dispose();
        effect2.Dispose();
    }

    [Fact]
    public void SelfWritingEffect_DoesNotCycle()
    {
        // A single effect writing to its own dependency should NOT cycle
        // because _executing flag prevents re-scheduling during execution
        var signal = new Signal<int>(0);

        var effect = new Effect(() =>
        {
            var v = signal.Value;
            if (v == 0)
                signal.Value = 42;
        });

        Assert.Equal(42, signal.Peek());
        effect.Dispose();
    }
}
