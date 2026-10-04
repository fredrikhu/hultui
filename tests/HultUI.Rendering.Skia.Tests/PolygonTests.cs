using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Filled polygons and stroked polylines: the shapes a terminal's Powerline separators are made
/// of. Coverage is read off the green channel of red on white: 255 fully red, 0 untouched.
/// </summary>
public class PolygonTests
{
    private static Func<int, int, int> Draw(Action<Core.Rendering.IRenderContext> draw, float scale = 1f)
    {
        int px = (int)(40 * scale);
        var surface = new SkiaRasterRenderSurface();
        surface.Resize(px, px, scale);
        var ctx = surface.RenderContext;
        ctx.BeginFrame(40, 40, scale, Color.White);
        draw(ctx);
        ctx.EndFrame();
        surface.Present();
        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);
        surface.Dispose();
        // Coverage at a point given in layout units.
        return (x, y) => 255 - pixels[(int)(y * scale) * rowBytes + (int)(x * scale) * 4 + 1];
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    public void APolygon_IsFilledInside_AndNowhereElse(float scale)
    {
        var at = Draw(ctx => ctx.FillPolygon([new(10, 10), new(30, 20), new(10, 30)], Color.Red), scale);

        Assert.Equal(255, at(12, 20));      // inside, by the flat side
        Assert.Equal(255, at(24, 20));      // inside, near the point
        Assert.Equal(0, at(28, 12));        // beside the slope
        Assert.Equal(0, at(12, 6));         // above
        Assert.Equal(0, at(32, 20));        // past the point
    }

    [Fact]
    public void APolyline_IsStrokedAlongItsSegments_AndNotFilled()
    {
        var at = Draw(ctx => ctx.DrawPolyline([new(10, 10), new(20, 30), new(30, 10)], Color.Red, 3f));

        Assert.True(at(20, 28) > 200, "the stroke at the join");
        Assert.True(at(15, 20) > 200, "the stroke along the first segment");
        Assert.Equal(0, at(20, 15));        // inside the V: a polyline is not closed or filled
        Assert.Equal(0, at(20, 6));         // the open side
    }

    [Fact]
    public void TooFewPoints_DrawNothing()
    {
        var at = Draw(ctx =>
        {
            ctx.FillPolygon([new(10, 10), new(30, 30)], Color.Red);
            ctx.DrawPolyline([new(10, 10)], Color.Red, 3f);
        });
        Assert.Equal(0, at(10, 10));
        Assert.Equal(0, at(20, 20));
    }
}
