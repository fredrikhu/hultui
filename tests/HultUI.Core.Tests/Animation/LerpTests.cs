using HultUI.Core.Animation;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Animation;

public class LerpTests
{
    [Fact]
    public void Float_Midpoint()
    {
        Assert.Equal(50f, Lerp.Float(0f, 100f, 0.5f));
    }

    [Fact]
    public void Float_Boundaries()
    {
        Assert.Equal(10f, Lerp.Float(10f, 20f, 0f));
        Assert.Equal(20f, Lerp.Float(10f, 20f, 1f));
    }

    [Fact]
    public void Color_Midpoint()
    {
        var a = new Color(0, 0, 0);
        var b = new Color(200, 100, 50);
        var mid = Lerp.Color(a, b, 0.5f);
        Assert.Equal(100, mid.R);
        Assert.Equal(50, mid.G);
        Assert.Equal(25, mid.B);
        Assert.Equal(255, mid.A);
    }

    [Fact]
    public void Thickness_Midpoint()
    {
        var a = new Thickness(0f);
        var b = new Thickness(10f, 20f, 30f, 40f);
        var mid = Lerp.Thickness(a, b, 0.5f);
        Assert.Equal(5f, mid.Top);
        Assert.Equal(10f, mid.Right);
        Assert.Equal(15f, mid.Bottom);
        Assert.Equal(20f, mid.Left);
    }

    [Fact]
    public void CornerRadius_Midpoint()
    {
        var a = new CornerRadius(0f);
        var b = new CornerRadius(10f, 20f, 30f, 40f);
        var mid = Lerp.CornerRadius(a, b, 0.5f);
        Assert.Equal(5f, mid.TopLeft);
        Assert.Equal(10f, mid.TopRight);
        Assert.Equal(15f, mid.BottomRight);
        Assert.Equal(20f, mid.BottomLeft);
    }

    [Fact]
    public void Dimension_PxToPx()
    {
        var a = Dimension.Px(0);
        var b = Dimension.Px(100);
        var mid = Lerp.Dimension(a, b, 0.5f);
        Assert.Equal(DimensionUnit.Pixels, mid.Unit);
        Assert.Equal(50f, mid.Value);
    }

    [Fact]
    public void Dimension_DifferentUnits_SnapsAtEnd()
    {
        var a = Dimension.Px(50);
        var b = Dimension.Auto;
        Assert.Equal(a, Lerp.Dimension(a, b, 0.5f)); // still a before t=1
        Assert.Equal(b, Lerp.Dimension(a, b, 1f));    // snaps to b at t=1
    }
}
