using HultUI.Core.Primitives;
using HultUI.Rendering.Skia;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Proves the raster surface draws real pixels with no window, GL context, or
/// display — the backend HultUI.Headless renders through.
/// </summary>
public class SkiaRasterRenderSurfaceTests
{
    [Fact]
    public void DrawsRectIntoReadablePixels()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(100, 100, 1f);

        var ctx = surface.RenderContext;
        ctx.BeginFrame(100, 100, 1f, Color.White);
        ctx.DrawRect(new Rect(10, 10, 50, 50), Color.Red);
        ctx.EndFrame();
        surface.Present();

        var pixels = surface.ReadPixels(out int width, out int height, out int rowBytes);

        Assert.Equal(100, width);
        Assert.Equal(100, height);
        Assert.Equal(400, rowBytes);

        // Inside the rect: red. RGBA8888 byte order.
        AssertPixel(pixels, rowBytes, x: 20, y: 20, r: 255, g: 0, b: 0);
        // Outside the rect: the white clear color.
        AssertPixel(pixels, rowBytes, x: 5, y: 5, r: 255, g: 255, b: 255);
        AssertPixel(pixels, rowBytes, x: 90, y: 90, r: 255, g: 255, b: 255);
    }

    [Fact]
    public void ScaleFactorMapsLayoutUnitsToPixels()
    {
        using var surface = new SkiaRasterRenderSurface();
        // 2x scale: 100x100 layout units backed by 200x200 pixels.
        surface.Resize(200, 200, 2f);

        var ctx = surface.RenderContext;
        ctx.BeginFrame(100, 100, 2f, Color.White);
        ctx.DrawRect(new Rect(10, 10, 50, 50), Color.Blue);
        ctx.EndFrame();
        surface.Present();

        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);

        // Layout (10,10) is pixel (20,20); layout 60 (rect end) is pixel 120.
        AssertPixel(pixels, rowBytes, x: 30, y: 30, r: 0, g: 0, b: 255);
        AssertPixel(pixels, rowBytes, x: 110, y: 110, r: 0, g: 0, b: 255);
        AssertPixel(pixels, rowBytes, x: 130, y: 130, r: 255, g: 255, b: 255);
    }

    [Fact]
    public void ResizeRecreatesSurfaceAtNewSize()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(50, 50, 1f);
        surface.Resize(80, 40, 1f);

        var ctx = surface.RenderContext;
        ctx.BeginFrame(80, 40, 1f, Color.Green);
        ctx.EndFrame();
        surface.Present();

        surface.ReadPixels(out int width, out int height, out _);
        Assert.Equal(80, width);
        Assert.Equal(40, height);
    }

    [Fact]
    public void SnapshotReturnsImageOfCurrentFrame()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(10, 10, 1f);

        var ctx = surface.RenderContext;
        ctx.BeginFrame(10, 10, 1f, Color.Black);
        ctx.EndFrame();
        surface.Present();

        using var image = surface.Snapshot();
        Assert.Equal(10, image.Width);
        Assert.Equal(10, image.Height);
    }

    private static void AssertPixel(byte[] pixels, int rowBytes, int x, int y, byte r, byte g, byte b)
    {
        int i = y * rowBytes + x * 4;
        Assert.True(pixels[i] == r && pixels[i + 1] == g && pixels[i + 2] == b,
            $"Pixel ({x},{y}) was rgba({pixels[i]},{pixels[i + 1]},{pixels[i + 2]},{pixels[i + 3]}), expected rgb({r},{g},{b})");
    }
}
