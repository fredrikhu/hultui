using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// Covers the reconciliation wiring for the box visual effects (shadow, gradient):
/// the fluent builders populate the element, and PatchFrom carries them onto the
/// live element so a rebuilt tree keeps its shadows/gradient.
/// </summary>
public class BoxVisualTests
{
    [Fact]
    public void BoxShadow_Fluent_AppendsShadows()
    {
        var box = Box.New()
            .BoxShadow(0, 2, 8, new Color(0, 0, 0, 60))
            .BoxShadow(0, 8, 24, 2f, new Color(0, 0, 0, 40));

        Assert.NotNull(box.BoxShadows);
        Assert.Equal(2, box.BoxShadows!.Count);
        Assert.Equal(8f, box.BoxShadows[0].Blur);
        Assert.Equal(2f, box.BoxShadows[1].Spread);
    }

    [Fact]
    public void BackgroundGradient_Fluent_SetsGradient()
    {
        var box = Box.New().Background(Gradient.Vertical(Color.White, Color.Black));

        Assert.NotNull(box.BackgroundGradient);
        Assert.Equal(GradientKind.Linear, box.BackgroundGradient!.Kind);
        Assert.Equal(90f, box.BackgroundGradient.AngleDegrees);
        Assert.Equal(2, box.BackgroundGradient.Stops.Count);
    }

    [Fact]
    public void SolidBackground_Overload_LeavesGradientNull()
    {
        var box = Box.New().Background(Color.Red);
        Assert.Null(box.BackgroundGradient);
        Assert.Equal(Color.Red, box.BackgroundColor);
    }

    [Fact]
    public void PatchFrom_CopiesShadowsAndGradient()
    {
        var live = Box.New();
        var incoming = Box.New()
            .BoxShadow(0, 4, 12, new Color(0, 0, 0, 80))
            .Background(Gradient.Horizontal(Color.Red, Color.Blue));

        live.PatchFrom(incoming);

        Assert.Same(incoming.BoxShadows, live.BoxShadows);
        Assert.Same(incoming.BackgroundGradient, live.BackgroundGradient);
    }
}
