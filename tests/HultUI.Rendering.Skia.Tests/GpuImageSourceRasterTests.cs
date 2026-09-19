using System.Runtime.InteropServices;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Rendering.Skia;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A GPU source on a surface with no GL: it is never asked to render, and it is drawn
/// through its pixel path when it has one. The GL side is proven in HultUI.UiTests,
/// where there is a real context to render into.
/// </summary>
public class GpuImageSourceRasterTests
{
    [Fact]
    public void ARasterSurface_NeverAsksAGpuSourceToRender()
    {
        var source = new GpuOnlySource();
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(50, 50, 1f);
        var ctx = surface.RenderContext;

        ctx.BeginFrame(50, 50, 1f, Color.White);
        ctx.DrawImage(source, new Rect(0, 0, 50, 50), ImageFit.Fill);
        ctx.EndFrame();
        surface.Present();

        Assert.Equal(0, source.RenderRequests);
        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);
        Assert.Equal(255, pixels[25 * rowBytes + 25 * 4]);   // still the white clear
    }

    [Fact]
    public void ADualSource_FallsBackToItsPixels_OnARasterSurface()
    {
        using var source = new DualSource();
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(50, 50, 1f);
        var ctx = surface.RenderContext;

        ctx.BeginFrame(50, 50, 1f, Color.White);
        ctx.DrawImage(source, new Rect(0, 0, 50, 50), ImageFit.Fill);
        ctx.EndFrame();
        surface.Present();

        Assert.Equal(0, source.RenderRequests);
        Assert.Equal(1, source.PixelBorrows);
        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);
        int i = 25 * rowBytes + 25 * 4;
        Assert.Equal((0, 0, 255), (pixels[i], pixels[i + 1], pixels[i + 2]));
    }

    private class GpuOnlySource : IGpuImageSource
    {
        public int RenderRequests;
        public Size IntrinsicSize => new(4, 4);
        public bool IsLoaded => true;
        public string? AlternateText => null;

        public bool TryRenderFrame(IExternalGLSurface surface, out GpuFrame frame)
        {
            RenderRequests++;
            frame = default;
            return false;
        }
    }

    private sealed class DualSource : GpuOnlySource, IPixelImageSource, IDisposable
    {
        private readonly nint _pixels = Marshal.AllocHGlobal(4 * 4 * 4);
        public int PixelBorrows;
        public long Version => 1;

        public DualSource()
        {
            var blue = new byte[4 * 4 * 4];
            for (int i = 0; i < blue.Length; i += 4) { blue[i + 2] = 255; blue[i + 3] = 255; }
            Marshal.Copy(blue, 0, _pixels, blue.Length);
        }

        public bool TryAcquire(out PixelFrame frame)
        {
            PixelBorrows++;
            frame = new PixelFrame(_pixels, 4, 4, 16, PixelFormat.Rgba8888, 0);
            return true;
        }

        public void Release(in PixelFrame frame) { }
        public void Dispose() => Marshal.FreeHGlobal(_pixels);
    }
}
