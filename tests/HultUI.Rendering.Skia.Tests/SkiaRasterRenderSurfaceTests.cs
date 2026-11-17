using HultUI.Core.Primitives;
using HultUI.Rendering.Skia;
using HultUI.Testing;
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

    /// <summary>
    /// Skia's CPU rasterizer is fast only into its native order: into RGBA8888 the same frames
    /// cost about twice as much on Windows and three to eight times on Linux (2026-11-17). Both
    /// OSes' native order is BGRA, so a surface back on RGBA fails here first.
    /// </summary>
    [Fact]
    public void TheFrame_IsInSkiasNativeOrder_BgraOnTheDesktop()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(4, 4, 1f);
        Assert.Equal(SkiaSharp.SKImageInfo.PlatformColorType, surface.PixelColorType);
        Assert.Equal(SkiaSharp.SKColorType.Bgra8888, surface.PixelColorType);
        using var image = surface.Snapshot();
        Assert.Equal(SkiaSharp.SKColorType.Bgra8888, image.ColorType);
    }

    /// <summary>
    /// What the software window copies to the screen each frame: the frame's own pixels, read
    /// again every time. A draw after a Snapshot moves them, so an address kept from an earlier
    /// frame would show a stale frame.
    /// </summary>
    [Fact]
    public void PeekPixels_IsTheCurrentFrame_EvenAfterASnapshotMovedIt()
    {
        using var surface = new SkiaRasterRenderSurface();
        Assert.False(surface.PeekPixels(out _, out _));   // nothing to show before the first Resize
        surface.Resize(20, 10, 1f);

        DrawFrame(surface, Color.Red);
        Assert.True(surface.PeekPixels(out nint pixels, out int rowBytes));
        Assert.Equal(80, rowBytes);
        AssertBgraPixel(PixelsAt(pixels, rowBytes, 10), rowBytes, x: 5, y: 5, r: 255, g: 0, b: 0);

        using (surface.Snapshot())
            DrawFrame(surface, Color.Blue);
        Assert.True(surface.PeekPixels(out pixels, out rowBytes));
        AssertBgraPixel(PixelsAt(pixels, rowBytes, 10), rowBytes, x: 5, y: 5, r: 0, g: 0, b: 255);

        // Every frame, so it allocates nothing.
        long bytes = Allocations.Least(() => surface.PeekPixels(out _, out _));
        Assert.Equal(0, bytes);
    }

    /// <summary>
    /// What a software window draws with, so no frame is copied: memory the surface does not own,
    /// in the target's byte order. A window gets new memory when it is resized, and the surface
    /// follows the target; a Snapshot copies rather than moving the frame elsewhere.
    /// </summary>
    [Fact]
    public void WithATarget_ItDrawsIntoTheTargetsMemory_AndFollowsIt()
    {
        nint small = System.Runtime.InteropServices.Marshal.AllocHGlobal(4 * 16);
        nint big = System.Runtime.InteropServices.Marshal.AllocHGlobal(6 * 32);
        try
        {
            SkiaRasterRenderSurface.PixelTarget? target = new(small, 4, 4, 16, SkiaSharp.SKColorType.Bgra8888);
            using var surface = new SkiaRasterRenderSurface(null, () => target);
            Assert.True(surface.DrawsIntoTarget);
            surface.Resize(100, 100, 1f);   // only the scale: the target says the size

            Assert.True(surface.BeginFrame());
            Assert.Equal((4, 4), (surface.PixelWidth, surface.PixelHeight));
            DrawFrame(surface, Color.Red);
            var bgra = PixelsAt(small, 16, 4);
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, bgra[(3 * 16 + 3 * 4)..(3 * 16 + 3 * 4 + 4)]);

            // Resized: new memory, another size and byte order.
            target = new(big, 8, 6, 32, SkiaSharp.SKColorType.Rgba8888);
            Assert.True(surface.BeginFrame());
            Assert.Equal((8, 6), (surface.PixelWidth, surface.PixelHeight));
            DrawFrame(surface, Color.Blue);
            AssertPixel(PixelsAt(big, 32, 6), 32, x: 7, y: 5, r: 0, g: 0, b: 255);

            using (surface.Snapshot())
            {
                Assert.True(surface.BeginFrame());
                DrawFrame(surface, Color.Green);
            }
            AssertPixel(PixelsAt(big, 32, 6), 32, x: 7, y: 5, r: 0, g: 128, b: 0);

            // Every frame, so a steady one allocates nothing.
            Assert.Equal(0, Allocations.Least(() => surface.BeginFrame()));

            target = null;   // nothing to draw into, as for a minimized window
            Assert.False(surface.BeginFrame());
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal(small);
            System.Runtime.InteropServices.Marshal.FreeHGlobal(big);
        }
    }

    private static void DrawFrame(SkiaRasterRenderSurface surface, Color color)
    {
        var ctx = surface.RenderContext;
        ctx.BeginFrame(surface.PixelWidth, surface.PixelHeight, 1f, color);
        ctx.EndFrame();
        surface.Present();
    }

    private static byte[] PixelsAt(nint pixels, int rowBytes, int height)
    {
        var copy = new byte[rowBytes * height];
        System.Runtime.InteropServices.Marshal.Copy(pixels, copy, 0, copy.Length);
        return copy;
    }

    // RGBA8888, as ReadPixels gives them.
    private static void AssertPixel(byte[] pixels, int rowBytes, int x, int y, byte r, byte g, byte b)
    {
        int i = y * rowBytes + x * 4;
        Assert.True(pixels[i] == r && pixels[i + 1] == g && pixels[i + 2] == b,
            $"Pixel ({x},{y}) was rgba({pixels[i]},{pixels[i + 1]},{pixels[i + 2]},{pixels[i + 3]}), expected rgb({r},{g},{b})");
    }

    // BGRA8888, the order PeekPixels hands out on the desktop OSes (PixelColorType).
    private static void AssertBgraPixel(byte[] pixels, int rowBytes, int x, int y, byte r, byte g, byte b)
    {
        int i = y * rowBytes + x * 4;
        Assert.True(pixels[i + 2] == r && pixels[i + 1] == g && pixels[i] == b,
            $"Pixel ({x},{y}) was bgra({pixels[i]},{pixels[i + 1]},{pixels[i + 2]},{pixels[i + 3]}), expected rgb({r},{g},{b})");
    }
}
