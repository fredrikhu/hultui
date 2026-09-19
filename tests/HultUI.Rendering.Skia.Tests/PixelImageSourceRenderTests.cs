using System.Runtime.InteropServices;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Rendering.Skia;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// <see cref="IPixelImageSource"/> is the seam a video frame comes through: bytes the
/// producer owns, drawn without a copy. These prove the Skia context draws them
/// with the right channel order, treats the "x" formats as opaque, and — the part
/// that keeps a producer from tearing or freeing what Skia has yet to read — holds
/// the borrow until the frame is flushed.
/// </summary>
public class PixelImageSourceRenderTests
{
    private static readonly Rect Dest = new(10, 10, 50, 50);

    private static SkiaRasterRenderSurface NewSurface()
    {
        var surface = new SkiaRasterRenderSurface();
        surface.Resize(100, 100, 1f);
        return surface;
    }

    [Fact]
    public void DrawsTheFrameBytes_Rgba()
    {
        using var source = TestPixelSource.Solid(2, 2, PixelFormat.Rgba8888, r: 0, g: 200, b: 0, a: 255);
        using var surface = NewSurface();
        var ctx = surface.RenderContext;

        ctx.BeginFrame(100, 100, 1f, Color.White);
        ctx.DrawImage(source, Dest, ImageFit.Fill);
        ctx.EndFrame();
        surface.Present();

        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);
        AssertPixel(pixels, rowBytes, 20, 20, 0, 200, 0);
        AssertPixel(pixels, rowBytes, 55, 55, 0, 200, 0);
        AssertPixel(pixels, rowBytes, 5, 5, 255, 255, 255);
        AssertPixel(pixels, rowBytes, 70, 70, 255, 255, 255);
    }

    [Fact]
    public void Bgra_IsReadInItsOwnChannelOrder()
    {
        // Red in BGRA memory order is 0,0,255,255 — drawn as RGBA it would come
        // out blue.
        using var source = TestPixelSource.FromBytes(1, 1, PixelFormat.Bgra8888, [0, 0, 255, 255]);
        using var surface = NewSurface();
        var ctx = surface.RenderContext;

        ctx.BeginFrame(100, 100, 1f, Color.White);
        ctx.DrawImage(source, Dest, ImageFit.Fill);
        ctx.EndFrame();
        surface.Present();

        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);
        AssertPixel(pixels, rowBytes, 30, 30, 255, 0, 0);
    }

    [Theory]
    [InlineData(PixelFormat.Rgbx8888, new byte[] { 0, 0, 255, 0 })]
    [InlineData(PixelFormat.Bgrx8888, new byte[] { 255, 0, 0, 0 })]
    public void XFormats_IgnoreTheFourthByte(PixelFormat format, byte[] blueWithZeroAlpha)
    {
        // A decoder's rgb0/bgr0 leaves that byte undefined; here it is zero, which a
        // premultiplied read would render as fully transparent (the white clear).
        using var source = TestPixelSource.FromBytes(1, 1, format, blueWithZeroAlpha);
        using var surface = NewSurface();
        var ctx = surface.RenderContext;

        ctx.BeginFrame(100, 100, 1f, Color.White);
        ctx.DrawImage(source, Dest, ImageFit.Fill);
        ctx.EndFrame();
        surface.Present();

        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);
        AssertPixel(pixels, rowBytes, 30, 30, 0, 0, 255);
        // And the destination is opaque: Skia's opaque-image blit would otherwise copy
        // the zero byte straight into the surface's alpha, which a capture shows as
        // nothing at all.
        Assert.Equal(255, pixels[30 * rowBytes + 30 * 4 + 3]);
    }

    [Fact]
    public void TheBorrow_IsHeldUntilEndFrame()
    {
        using var source = TestPixelSource.Solid(4, 4, PixelFormat.Rgba8888, 255, 0, 0, 255);
        using var surface = NewSurface();
        var ctx = surface.RenderContext;

        ctx.BeginFrame(100, 100, 1f, Color.White);
        ctx.DrawImage(source, Dest, ImageFit.Contain);

        Assert.Equal(1, source.Acquires);
        Assert.Equal(0, source.Releases);   // Skia may not have read the bytes yet

        ctx.EndFrame();

        Assert.Equal(1, source.Releases);   // flushed, so the producer may write again
    }

    [Fact]
    public void EveryFrame_BorrowsAgain()
    {
        // No cross-frame caching of the wrapper: the bytes are re-read on every
        // draw, which is what a changing frame needs and what keeps the borrow
        // bounded to one frame.
        using var source = TestPixelSource.Solid(4, 4, PixelFormat.Rgba8888, 255, 0, 0, 255);
        using var surface = NewSurface();
        var ctx = surface.RenderContext;

        for (int i = 0; i < 3; i++)
        {
            ctx.BeginFrame(100, 100, 1f, Color.White);
            ctx.DrawImage(source, Dest, ImageFit.Contain);
            ctx.EndFrame();
        }

        Assert.Equal(3, source.Acquires);
        Assert.Equal(3, source.Releases);
    }

    [Fact]
    public void NoFrameYet_DrawsNothing_AndReleasesNothing()
    {
        using var source = TestPixelSource.Solid(4, 4, PixelFormat.Rgba8888, 255, 0, 0, 255);
        source.HasFrame = false;
        using var surface = NewSurface();
        var ctx = surface.RenderContext;

        ctx.BeginFrame(100, 100, 1f, Color.White);
        ctx.DrawImage(source, Dest, ImageFit.Fill);
        ctx.EndFrame();
        surface.Present();

        Assert.Equal(0, source.Releases);
        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);
        AssertPixel(pixels, rowBytes, 30, 30, 255, 255, 255);
    }

    [Fact]
    public void DisposingTheSurface_ReleasesAHeldBorrow()
    {
        // A frame abandoned between a draw and its EndFrame (an exception in a later
        // element's render) must not leave the producer's buffer borrowed forever.
        using var source = TestPixelSource.Solid(4, 4, PixelFormat.Rgba8888, 255, 0, 0, 255);
        var surface = NewSurface();
        var ctx = surface.RenderContext;

        ctx.BeginFrame(100, 100, 1f, Color.White);
        ctx.DrawImage(source, Dest, ImageFit.Fill);
        surface.Dispose();

        Assert.Equal(1, source.Releases);
    }

    [Fact]
    public void TheNextBeginFrame_ReleasesAnAbandonedBorrow()
    {
        using var source = TestPixelSource.Solid(4, 4, PixelFormat.Rgba8888, 255, 0, 0, 255);
        using var surface = NewSurface();
        var ctx = surface.RenderContext;

        ctx.BeginFrame(100, 100, 1f, Color.White);
        ctx.DrawImage(source, Dest, ImageFit.Fill);
        // No EndFrame: the frame died mid-render.

        ctx.BeginFrame(100, 100, 1f, Color.White);
        Assert.Equal(1, source.Releases);
        ctx.EndFrame();
    }

    private static void AssertPixel(byte[] pixels, int rowBytes, int x, int y, byte r, byte g, byte b)
    {
        int i = y * rowBytes + x * 4;
        Assert.True(pixels[i] == r && pixels[i + 1] == g && pixels[i + 2] == b,
            $"pixel ({x},{y}) was ({pixels[i]},{pixels[i + 1]},{pixels[i + 2]}), expected ({r},{g},{b})");
    }
}

/// <summary>A one-buffer pixel source over unmanaged memory that counts its borrows.</summary>
internal sealed class TestPixelSource : IPixelImageSource, IDisposable
{
    private readonly nint _pixels;
    private readonly int _width, _height;
    private readonly PixelFormat _format;

    public int Acquires { get; private set; }
    public int Releases { get; private set; }
    public bool HasFrame { get; set; } = true;
    public long Version { get; set; } = 1;

    private TestPixelSource(int width, int height, PixelFormat format, byte[] bytes)
    {
        _width = width;
        _height = height;
        _format = format;
        _pixels = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, _pixels, bytes.Length);
    }

    public static TestPixelSource FromBytes(int width, int height, PixelFormat format, byte[] bytes)
        => new(width, height, format, bytes);

    public static TestPixelSource Solid(int width, int height, PixelFormat format, byte r, byte g, byte b, byte a)
    {
        var bytes = new byte[width * height * 4];
        for (int i = 0; i < bytes.Length; i += 4)
        {
            bytes[i] = r; bytes[i + 1] = g; bytes[i + 2] = b; bytes[i + 3] = a;
        }
        return new TestPixelSource(width, height, format, bytes);
    }

    public Size IntrinsicSize => new(_width, _height);
    public bool IsLoaded => HasFrame;
    public string? AlternateText => null;

    public bool TryAcquire(out PixelFrame frame)
    {
        if (!HasFrame)
        {
            frame = default;
            return false;
        }
        Acquires++;
        frame = new PixelFrame(_pixels, _width, _height, _width * 4, _format, Slot: 0);
        return true;
    }

    public void Release(in PixelFrame frame) => Releases++;

    public void Dispose() => Marshal.FreeHGlobal(_pixels);
}
