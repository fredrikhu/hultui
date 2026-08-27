using HultUI.Core.Animation;
using Xunit;

namespace HultUI.Core.Tests.Animation;

/// <summary>
/// Repeating tweens — what a busy indicator needs: a motion that runs until the work it
/// stands for finishes, rather than for a duration chosen up front.
///
/// <para>The property worth pinning is that a repeat holds its <i>period</i>. A frame
/// almost never lands exactly on the end of a cycle, so a restart that dropped whatever
/// the frame overshot by would quantise every cycle up to a whole number of frames — a
/// spinner built that way visibly slows down and drifts out of step. The phase tests
/// below are that bug.</para>
///
/// <para><see cref="AnimationManager"/>'s tween list is process-global and its
/// <c>Reset</c> is internal, so every test here cancels what it started: an endless loop
/// left running would tick its way through every test that follows.</para>
/// </summary>
[Collection("Animation")]
public class TweenRepeatTests : IDisposable
{
    private readonly List<TweenHandle> _started = [];

    private TweenHandle Track(TweenHandle handle)
    {
        _started.Add(handle);
        return handle;
    }

    public void Dispose()
    {
        foreach (var handle in _started) handle.Cancel();
        // Cancelled tweens are dropped on the next tick, not on cancel.
        AnimationManager.Tick(0f);
    }

    [Fact]
    public void ARepeatRunsForItsCyclesThenCompletesOnTheEndValue()
    {
        int completed = 0;
        float value = 0f;
        Track(Animate.Value(0f, 1f, v => value = v)
            .Duration(1f).Ease(Easing.Linear).Repeat(3)
            .OnComplete(() => completed++)
            .Start());

        // Two cycles in: still running, and back near the start of the third rather
        // than parked at the end of the second.
        for (int i = 0; i < 21; i++) AnimationManager.Tick(0.1f);
        Assert.Equal(0.1f, value, 0.01f);
        Assert.Equal(0, completed);

        for (int i = 0; i < 20; i++) AnimationManager.Tick(0.1f);
        Assert.Equal(1f, value);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void ALoopKeepsGoingAndRestartsFromTheBeginning()
    {
        float value = -1f;
        var handle = Track(Animate.Value(0f, 100f, v => value = v)
            .Duration(1f).Ease(Easing.Linear).Loop().Start());

        AnimationManager.Tick(0.5f);
        Assert.Equal(50f, value, 0.01f);

        // Past the end of the first cycle: back near the start, not stuck at 100.
        AnimationManager.Tick(0.75f);
        Assert.Equal(25f, value, 0.01f);

        for (int i = 0; i < 100; i++) AnimationManager.Tick(0.1f);
        Assert.False(handle.IsComplete);
    }

    [Theory]
    // Tick sizes that never divide the cycle evenly, so every boundary overshoots.
    // The expected phase is simply the fraction of elapsed cycles — which is what a
    // restart that dropped the overshoot would NOT produce (it would quantise each
    // cycle up to four ticks and read 0.9 and 0.3 here respectively).
    [InlineData(11, 0.3f)]
    [InlineData(53, 0.9f)]
    public void ALoopHoldsItsPhaseAcrossManyCycles(int ticks, float expected)
    {
        float value = 0f;
        Track(Animate.Value(0f, 1f, v => value = v)
            .Duration(1f).Ease(Easing.Linear).Loop().Start());

        for (int i = 0; i < ticks; i++) AnimationManager.Tick(0.3f);

        Assert.Equal(expected, value, 0.02f);
    }

    [Fact]
    public void ALoopedTweenStopsWhenItsHandleIsCancelled()
    {
        float value = 0f;
        var handle = Track(Animate.Value(0f, 100f, v => value = v)
            .Duration(1f).Ease(Easing.Linear).Loop().Start());

        AnimationManager.Tick(0.5f);
        handle.Cancel();
        AnimationManager.Tick(0.5f);

        Assert.Equal(50f, value, 0.01f);
        Assert.True(handle.IsComplete);
    }

    [Fact]
    public void AYoyoRunsEverySecondCycleBackwards()
    {
        float value = 0f;
        Track(Animate.Value(0f, 100f, v => value = v)
            .Duration(1f).Ease(Easing.Linear).Repeat(2).Yoyo().Start());

        AnimationManager.Tick(0.5f);
        Assert.Equal(50f, value, 0.01f);

        // Second cycle, a quarter in — running back down from 100.
        AnimationManager.Tick(0.75f);
        Assert.Equal(75f, value, 0.01f);

        AnimationManager.Tick(0.5f);
        Assert.Equal(25f, value, 0.01f);
    }

    [Fact]
    public void TheDelayAppliesOnceRatherThanBeforeEveryCycle()
    {
        float value = -1f;
        Track(Animate.Value(0f, 100f, v => value = v)
            .Duration(1f).Delay(1f).Ease(Easing.Linear).Loop().Start());

        AnimationManager.Tick(0.5f);
        Assert.Equal(-1f, value);           // still waiting out the delay

        AnimationManager.Tick(1f);          // 0.5s into the first cycle
        Assert.Equal(50f, value, 0.01f);

        AnimationManager.Tick(1f);          // 0.5s into the second — no second wait
        Assert.Equal(50f, value, 0.01f);
    }

    [Fact]
    public void AZeroDurationRepeatCompletesInsteadOfSpinningForever()
    {
        int ticks = 0;
        var handle = Track(Animate.Value(0f, 1f, _ => ticks++).Duration(0f).Repeat(2).Start());

        AnimationManager.Tick(0.016f);
        AnimationManager.Tick(0.016f);

        Assert.Equal(2, ticks);
        Assert.True(handle.IsComplete);
    }
}
