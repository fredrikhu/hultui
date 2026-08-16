using HultUI.Core.Primitives;
using HultUI.Rendering.Skia;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// The canvas drawing context is one reusable instance drawing through the render
/// context's pooled Skia objects. That buys a zero-allocation commit graph and two new
/// ways to be wrong, both covered here: a stale Bounds surviving into the next canvas,
/// and stroke state leaking from one pooled draw into a fill.
/// </summary>
public class SkiaCanvasDrawingContextTests
{
    [Fact]
    public void ConsecutiveCanvases_EachDrawAtTheirOwnBounds()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(100, 100, 1f);
        var ctx = surface.RenderContext;

        ctx.BeginFrame(100, 100, 1f, Color.White);
        // Two canvas elements in one frame, same local rect, different bounds —
        // the shape of two commit-graph gutters in one History render.
        var left = ctx.CreateCanvasContext(new Rect(0, 0, 50, 100));
        left.DrawRect(new Rect(10, 10, 20, 20), Color.Red);
        var right = ctx.CreateCanvasContext(new Rect(50, 0, 50, 100));
        right.DrawRect(new Rect(10, 10, 20, 20), Color.Blue);
        ctx.EndFrame();
        surface.Present();

        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);
        AssertPixel(pixels, rowBytes, x: 20, y: 20, r: 255, g: 0, b: 0);    // left canvas
        AssertPixel(pixels, rowBytes, x: 70, y: 20, r: 0, g: 0, b: 255);    // right canvas, offset by its bounds
        AssertPixel(pixels, rowBytes, x: 20, y: 60, r: 255, g: 255, b: 255);
    }

    [Fact]
    public void StrokeStateFromOneDraw_DoesNotLeakIntoTheNextFill()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(100, 100, 1f);
        var ctx = surface.RenderContext;

        ctx.BeginFrame(100, 100, 1f, Color.White);
        var canvas = ctx.CreateCanvasContext(new Rect(0, 0, 100, 100));
        // Each fill-path draw comes DIRECTLY after a stroked one — nothing in between
        // that would scrub the pooled paint on its own. If IsStroke/StrokeWidth
        // survived, the circle and rect would render as outlines with hollow middles.
        canvas.DrawLine(new Point(0, 90), new Point(100, 90), Color.Black, 2f);
        canvas.DrawCircle(new Point(75, 40), 10f, Color.Blue);
        canvas.DrawCircleStroke(new Point(75, 40), 14f, Color.Black, 1f);
        canvas.DrawRect(new Rect(20, 20, 40, 40), Color.Red);
        ctx.EndFrame();
        surface.Present();

        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);
        AssertPixel(pixels, rowBytes, x: 75, y: 40, r: 0, g: 0, b: 255);    // circle interior is filled
        AssertPixel(pixels, rowBytes, x: 40, y: 40, r: 255, g: 0, b: 0);    // rect interior is filled
    }

    private static void AssertPixel(byte[] pixels, int rowBytes, int x, int y, byte r, byte g, byte b)
    {
        int i = y * rowBytes + x * 4;
        Assert.True(pixels[i] == r && pixels[i + 1] == g && pixels[i + 2] == b,
            $"Pixel ({x},{y}) was rgba({pixels[i]},{pixels[i + 1]},{pixels[i + 2]},{pixels[i + 3]}), expected rgb({r},{g},{b})");
    }
}
