using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Testing;
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

    /// <summary>A polygon or a polyline is built in the context's one kept path, emptied before
    /// each. A new <c>SKPath</c> per shape was ~80 B a shape, and the shapes are drawn again every
    /// frame: a terminal's Powerline separators, a rich text editor's ticks (plan 17, round 3).</summary>
    [Fact]
    public void PolygonsAndPolylines_AllocateNothingPerFrame()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(40, 40, 1f);
        var ctx = surface.RenderContext;

        void Frame()
        {
            ctx.BeginFrame(40, 40, 1f, Color.White);
            for (int i = 0; i < 10; i++)
            {
                ctx.FillPolygon([new(10, 10), new(30, 20), new(10, 30)], Color.Red);
                ctx.DrawPolyline([new(10, 10), new(20, 30), new(30, 10)], Color.Blue, 3f);
            }
            ctx.EndFrame();
        }

        Frame();
        Frame();
        double perFrame = Allocations.PerCall(50, Frame);

        Assert.True(perFrame < 1, $"10 polygons and 10 polylines allocated {perFrame:0.#} B per frame");
    }

    /// <summary>The kept path starts each shape empty: a shape drawn after others — polygons,
    /// polylines, the rounded corners of a border with sides of its own, which clip through the
    /// same path — looks exactly as it does on a fresh surface, with none of their points in it.</summary>
    [Theory]
    [InlineData(false, 1f)]
    [InlineData(true, 1f)]
    [InlineData(false, 1.5f)]
    [InlineData(true, 1.5f)]
    public void AShapeDrawnAfterOthers_HoldsOnlyItsOwnPoints(bool polyline, float scale)
    {
        void Shape(Core.Rendering.IRenderContext ctx)
        {
            if (polyline) ctx.DrawPolyline([new(4, 30), new(14, 8), new(24, 30), new(34, 8)], Color.Red, 2f);
            else ctx.FillPolygon([new(6, 6), new(34, 12), new(26, 34), new(8, 26)], Color.Red);
        }

        int px = (int)(40 * scale);
        using var fresh = new SkiaRasterRenderSurface();
        fresh.Resize(px, px, scale);
        fresh.RenderContext.BeginFrame(40, 40, scale, Color.White);
        Shape(fresh.RenderContext);
        fresh.RenderContext.EndFrame();
        fresh.Present();
        var expected = fresh.ReadPixels(out _, out _, out _);

        using var used = new SkiaRasterRenderSurface();
        used.Resize(px, px, scale);
        var ctx = used.RenderContext;
        ctx.BeginFrame(40, 40, scale, Color.White);
        ctx.FillPolygon([new(2, 2), new(38, 4), new(20, 38)], Color.Blue);
        ctx.DrawPolyline([new(38, 38), new(2, 20), new(38, 2)], Color.Green, 3f);
        ctx.DrawBorder(new Rect(4, 4, 32, 32), new CornerRadius(6), Color.Black, new Thickness(1, 2, 3, 4),
            BorderStyle.Solid);
        Shape(ctx);
        ctx.EndFrame();
        // The shape again, alone: what is left in the path from the shapes before it would show.
        ctx.BeginFrame(40, 40, scale, Color.White);
        Shape(ctx);
        ctx.EndFrame();
        used.Present();

        Assert.Equal(expected, used.ReadPixels(out _, out _, out _));
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
