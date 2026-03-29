using HultUI.Core.Animation;
using Xunit;

namespace HultUI.Core.Tests.Animation;

[Collection("Animation")]
public class AnimationManagerTests
{
    [Fact]
    public void Tick_AdvancesTweens()
    {
        float result = 0f;
        Animate.Value(0f, 100f, v => result = v).Duration(1f).Ease(Easing.Linear).Start();

        AnimationManager.Tick(0.5f);
        Assert.Equal(50f, result, 0.01f);
        Assert.True(AnimationManager.HasActiveAnimations);

        AnimationManager.Tick(0.6f);
        Assert.Equal(100f, result);
        Assert.False(AnimationManager.HasActiveAnimations);
    }

    [Fact]
    public void Tick_RemovesCompletedTweens()
    {
        Animate.Value(0f, 1f, _ => { }).Duration(0.1f).Start();
        Assert.True(AnimationManager.HasActiveAnimations);

        AnimationManager.Tick(0.2f);
        Assert.False(AnimationManager.HasActiveAnimations);
    }

    [Fact]
    public void Cancel_ViaHandle()
    {
        float result = 0f;
        var handle = Animate.Value(0f, 100f, v => result = v).Duration(1f).Ease(Easing.Linear).Start();

        AnimationManager.Tick(0.5f);
        Assert.Equal(50f, result, 0.01f);

        handle.Cancel();
        AnimationManager.Tick(0.5f); // should clean up cancelled tween
        Assert.Equal(50f, result, 0.01f); // no further update
        Assert.False(AnimationManager.HasActiveAnimations);
    }

    [Fact]
    public void OnComplete_CanStartNewAnimation()
    {
        float first = 0f;
        float second = 0f;

        Animate.Value(0f, 1f, v => first = v)
            .Duration(0.5f)
            .Ease(Easing.Linear)
            .OnComplete(() =>
            {
                Animate.Value(0f, 1f, v => second = v)
                    .Duration(0.5f)
                    .Ease(Easing.Linear)
                    .Start();
            })
            .Start();

        AnimationManager.Tick(0.6f); // completes first, starts second
        Assert.Equal(1f, first);
        Assert.True(AnimationManager.HasActiveAnimations);

        AnimationManager.Tick(0.6f); // completes second
        Assert.Equal(1f, second);
        Assert.False(AnimationManager.HasActiveAnimations);
    }
}
