using HultUI.Core.Animation;
using Xunit;

namespace HultUI.Core.Tests.Animation;

public class EasingTests
{
    [Theory]
    [MemberData(nameof(AllEasings))]
    public void AllEasings_ReturnZeroAtZero(Func<float, float> easing)
    {
        Assert.Equal(0f, easing(0f), 0.001f);
    }

    [Theory]
    [MemberData(nameof(AllEasings))]
    public void AllEasings_ReturnOneAtOne(Func<float, float> easing)
    {
        Assert.Equal(1f, easing(1f), 0.001f);
    }

    [Fact]
    public void Linear_ReturnsMidpointAtHalf()
    {
        Assert.Equal(0.5f, Easing.Linear(0.5f), 0.001f);
    }

    [Fact]
    public void EaseOutBack_OvershotsMidpoint()
    {
        // EaseOutBack should exceed 1.0 before settling
        float mid = Easing.EaseOutBack(0.5f);
        Assert.True(mid > 1f);
    }

    [Fact]
    public void CubicBezier_Linear_MatchesLinear()
    {
        var bezier = Easing.CubicBezier(0f, 0f, 1f, 1f);
        Assert.Equal(0.5f, bezier(0.5f), 0.01f);
    }

    [Fact]
    public void CubicBezier_Boundaries()
    {
        var bezier = Easing.CubicBezier(0.25f, 0.1f, 0.25f, 1f); // CSS "ease"
        Assert.Equal(0f, bezier(0f), 0.001f);
        Assert.Equal(1f, bezier(1f), 0.001f);
    }

    public static TheoryData<Func<float, float>> AllEasings() => new()
    {
        Easing.Linear,
        Easing.EaseInQuad,
        Easing.EaseOutQuad,
        Easing.EaseInOutQuad,
        Easing.EaseInCubic,
        Easing.EaseOutCubic,
        Easing.EaseInOutCubic,
        Easing.EaseInExpo,
        Easing.EaseOutExpo,
        Easing.EaseOutBack,
        Easing.EaseOutElastic,
    };
}
