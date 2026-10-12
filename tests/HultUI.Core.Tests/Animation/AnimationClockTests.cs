using HultUI.Core.Animation;
using Xunit;

namespace HultUI.Core.Tests.Animation;

/// <summary>
/// What the frame loop asks of the animations now that it waits for its next deadline instead of
/// coming round every 16 ms (plan 17, round 3): when a step can next change anything, and a
/// clock that starts at a tween's first step.
/// </summary>
[Collection("Animation")]
public class AnimationClockTests
{
    [Fact]
    public void NextChangeIn_IsNothing_WithNoTween()
    {
        AnimationManager.Reset();
        Assert.Equal(float.PositiveInfinity, AnimationManager.NextChangeIn);
        Assert.False(AnimationManager.HasFreshTweens);
    }

    [Fact]
    public void NextChangeIn_IsNow_ForATweenNotYetStepped_AndForARunningOne()
    {
        AnimationManager.Reset();
        Animate.Value(0f, 1f, _ => { }).Duration(1f).Start();
        Assert.True(AnimationManager.HasFreshTweens);
        Assert.Equal(0f, AnimationManager.NextChangeIn);

        AnimationManager.Tick(0.1f, 0f);
        Assert.False(AnimationManager.HasFreshTweens);
        Assert.Equal(0f, AnimationManager.NextChangeIn);
        AnimationManager.Reset();
    }

    [Fact]
    public void NextChangeIn_IsWhatIsLeftOfTheShortestDelay()
    {
        AnimationManager.Reset();
        Animate.After(2f, () => { });
        Animate.After(0.5f, () => { });
        AnimationManager.Tick(0.016f, 0f);   // their first step: the clock starts here

        Assert.Equal(0.5f, AnimationManager.NextChangeIn, 0.0001f);
        AnimationManager.Tick(0.2f, 0f);
        Assert.Equal(0.3f, AnimationManager.NextChangeIn, 0.0001f);
        AnimationManager.Reset();
    }

    [Fact]
    public void ATimer_FiresWhenItsDelayIsOut_OnOneStep()
    {
        AnimationManager.Reset();
        int fired = 0;
        Animate.After(1.7f, () => fired++);
        AnimationManager.Tick(0f, 0f);

        AnimationManager.Tick(AnimationManager.NextChangeIn, 0f);

        Assert.Equal(1, fired);
        Assert.False(AnimationManager.HasActiveAnimations);
    }

    /// <summary>The frame loop's step bounds a new tween's first step: bounded at 0, a tween that
    /// was not stepped yet starts its clock at it, and one started after the loop waited minutes
    /// does not jump to its end.</summary>
    [Fact]
    public void TheLoopsStep_StartsANewTweensClockAtIt()
    {
        AnimationManager.Reset();
        float value = -1f;
        Animate.Value(0f, 1f, v => value = v).Duration(0.3f).Ease(Easing.Linear).Start();

        AnimationManager.Tick(5f, 0f);
        Assert.Equal(0f, value);

        AnimationManager.Tick(0.15f, 0f);
        Assert.Equal(0.5f, value, 0.001f);
        AnimationManager.Reset();
    }

    /// <summary>The loop's own bound, one display interval: a tween started after minutes of
    /// idle is stepped by one interval on its first step, so its first frame neither jumps to
    /// its end nor shows its start value again; one started less than an interval after the
    /// last step is stepped by that time.</summary>
    [Fact]
    public void TheLoopsFirstStep_IsTheTimeSinceItsLastStep_AtMostAnInterval()
    {
        AnimationManager.Reset();
        const float interval = 1f / 60;
        float afterIdle = -1f, soon = -1f;
        Animate.Value(0f, 1f, v => afterIdle = v).Duration(0.3f).Ease(Easing.Linear).Start();

        AnimationManager.Tick(300f, interval);
        Assert.Equal(interval / 0.3f, afterIdle, 0.0001f);

        Animate.Value(0f, 1f, v => soon = v).Duration(0.3f).Ease(Easing.Linear).Start();
        AnimationManager.Tick(0.006f, interval);
        Assert.Equal(0.006f / 0.3f, soon, 0.0001f);
        Assert.Equal((interval + 0.006f) / 0.3f, afterIdle, 0.0001f);
        AnimationManager.Reset();
    }

    /// <summary>A timer's clock starts at the loop's first step of it: credited the time since the
    /// loop's last step, it would end up to an interval early (a tooltip shown before its delay).</summary>
    [Fact]
    public void ADelayInTheLoop_StartsItsClockAtItsFirstStep()
    {
        AnimationManager.Reset();
        int fired = 0;
        Animate.After(0.5f, () => fired++);

        AnimationManager.Tick(10f, 1f / 60);
        Assert.Equal(0.5f, AnimationManager.NextChangeIn, 0.0001f);
        Assert.Equal(0, fired);
        AnimationManager.Reset();
    }

    /// <summary>A host or a test stepping by the time since the last step, a tween's start
    /// included, still steps a new tween by all of it.</summary>
    [Fact]
    public void ThePublicStep_StepsANewTweenByItsDelta()
    {
        AnimationManager.Reset();
        float value = -1f;
        Animate.Value(0f, 1f, v => value = v).Duration(0.3f).Ease(Easing.Linear).Start();

        AnimationManager.Tick(0.15f);

        Assert.Equal(0.5f, value, 0.001f);
        AnimationManager.Reset();
    }

    [Fact]
    public void ATweenAddedByAStep_IsFresh()
    {
        AnimationManager.Reset();
        Animate.After(0.1f, () => Animate.Value(0f, 1f, _ => { }).Duration(1f).Start());
        AnimationManager.Tick(0f, 0f);
        Assert.False(AnimationManager.HasFreshTweens);

        AnimationManager.Tick(0.1f, 0f);

        Assert.True(AnimationManager.HasFreshTweens);
        AnimationManager.Reset();
    }
}
