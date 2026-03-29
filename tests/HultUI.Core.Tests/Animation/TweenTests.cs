using HultUI.Core.Animation;
using Xunit;

namespace HultUI.Core.Tests.Animation;

public class TweenTests
{
    [Fact]
    public void Tween_ReachesTargetValue()
    {
        float result = 0f;
        var tween = new Tween<float>(0f, 100f, 1f, 0f, Easing.Linear, Lerp.Float, v => result = v, null, null);

        tween.Tick(1f); // full duration
        Assert.Equal(100f, result);
        Assert.True(tween.IsComplete);
    }

    [Fact]
    public void Tween_InterpolatesAtMidpoint()
    {
        float result = 0f;
        var tween = new Tween<float>(0f, 100f, 1f, 0f, Easing.Linear, Lerp.Float, v => result = v, null, null);

        tween.Tick(0.5f);
        Assert.Equal(50f, result, 0.01f);
        Assert.False(tween.IsComplete);
    }

    [Fact]
    public void Tween_DelayPostponesStart()
    {
        float result = -1f;
        var tween = new Tween<float>(0f, 100f, 1f, 0.5f, Easing.Linear, Lerp.Float, v => result = v, null, null);

        tween.Tick(0.3f); // still in delay
        Assert.Equal(-1f, result); // setter not called

        tween.Tick(0.7f); // 0.5s delay passed, 0.5s into 1s duration
        Assert.Equal(50f, result, 0.01f);
    }

    [Fact]
    public void Tween_CallsOnComplete()
    {
        bool completed = false;
        var tween = new Tween<float>(0f, 1f, 0.5f, 0f, Easing.Linear, Lerp.Float, _ => { }, () => completed = true, null);

        tween.Tick(0.3f);
        Assert.False(completed);

        tween.Tick(0.3f); // total 0.6s > 0.5s duration
        Assert.True(completed);
    }

    [Fact]
    public void Tween_CallsMarkDirty()
    {
        int dirtyCount = 0;
        var tween = new Tween<float>(0f, 1f, 1f, 0f, Easing.Linear, Lerp.Float, _ => { }, null, () => dirtyCount++);

        tween.Tick(0.1f);
        Assert.Equal(1, dirtyCount);

        tween.Tick(0.1f);
        Assert.Equal(2, dirtyCount);
    }

    [Fact]
    public void Tween_Cancel_StopsUpdates()
    {
        float result = 0f;
        var tween = new Tween<float>(0f, 100f, 1f, 0f, Easing.Linear, Lerp.Float, v => result = v, null, null);

        tween.Tick(0.5f);
        Assert.Equal(50f, result, 0.01f);

        tween.Cancel();
        Assert.True(tween.IsComplete);

        tween.Tick(0.5f); // should not update
        Assert.Equal(50f, result, 0.01f);
    }

    [Fact]
    public void Tween_OvershotTime_ClampsToOne()
    {
        float result = 0f;
        var tween = new Tween<float>(0f, 100f, 0.5f, 0f, Easing.Linear, Lerp.Float, v => result = v, null, null);

        tween.Tick(10f); // way past duration
        Assert.Equal(100f, result);
        Assert.True(tween.IsComplete);
    }
}
