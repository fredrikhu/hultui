using HultUI.Core.Primitives;
using HultUI.Rendering.Skia;
using SkiaSharp;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A clip the surface puts on its canvas before the frame outlasts the render context's
/// <c>BeginFrame</c>, which restores the canvas only to the save count the surface handed it over
/// at. The Vulkan surface clips an over-allocated drag target (and the half-float offscreen) to the
/// window that way; restoring to 1 instead (plan 17, round 8) dropped the clip on every drag frame,
/// so the clear and the drawing went out to the allocated edge and a context menu culling by
/// <see cref="SkiaRenderContext.ClipBounds"/> drew rows nobody sees.
/// </summary>
public class SurfaceClipThroughBeginFrameTests
{
    /// <summary>A 2400×1600 canvas at 1.5, clipped to its top-left 1200×800 as the Vulkan
    /// surface clips a kept target, and handed to the context at the count the clip leaves.</summary>
    private static (SkiaRasterRenderSurface Surface, SkiaRenderContext Context, SKCanvas Canvas) ClippedCanvas()
    {
        var surface = new SkiaRasterRenderSurface();
        surface.Resize(2400, 1600, 1.5f);
        var context = (SkiaRenderContext)surface.RenderContext;
        var canvas = context.Canvas;
        // What was there before: white everywhere, so a clear outside the clip shows.
        canvas.Clear(SKColors.White);
        canvas.Save();
        canvas.ResetMatrix();
        canvas.ClipRect(SKRect.Create(1200, 800));
        context.SetCanvas(canvas, canvas.SaveCount);
        return (surface, context, canvas);
    }

    [Fact]
    public void TheSurfacesClip_OutlastsBeginFrame()
    {
        var (surface, context, canvas) = ClippedCanvas();
        using var owned = surface;

        context.BeginFrame(1600, 1067, 1.5f, new Color(255, 0, 0));

        Assert.Equal(2, canvas.SaveCount);
        Assert.Equal(new SKRectI(0, 0, 1200, 800), canvas.DeviceClipBounds);
        var bounds = context.ClipBounds!.Value;
        Assert.True(bounds.Right <= 801f && bounds.Bottom <= 534f,
            $"ClipBounds {bounds} in layout px, the window being 800×533");

        context.EndFrame();
        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);
        // The clear is inside the clip only.
        Assert.Equal((byte)255, pixels[100 * rowBytes + 100 * 4 + 0]);
        Assert.Equal((byte)0, pixels[100 * rowBytes + 100 * 4 + 1]);
        Assert.Equal((byte)255, pixels[100 * rowBytes + 1300 * 4 + 1]);
        Assert.Equal((byte)255, pixels[900 * rowBytes + 100 * 4 + 1]);
    }

    [Fact]
    public void AFrameThatThrowsWithItsSavesLeft_IsRestoredToTheSurfacesClip()
    {
        var (surface, context, canvas) = ClippedCanvas();
        using var owned = surface;

        // A frame that threw inside a clip, a layer and a translate: none of them popped.
        context.BeginFrame(1600, 1067, 1.5f, new Color(0, 0, 0));
        context.PushClip(new Rect(10, 10, 50, 50));
        context.PushOpacity(0.5f);
        context.PushTranslate(30, 30);
        Assert.True(canvas.SaveCount > 2);

        context.BeginFrame(1600, 1067, 1.5f, new Color(0, 0, 0));

        Assert.Equal(2, canvas.SaveCount);
        Assert.Equal(new SKRectI(0, 0, 1200, 800), canvas.DeviceClipBounds);
        context.EndFrame();
    }

    [Fact]
    public void ACanvasHandedOverWithNoSaves_IsRestoredToOne()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(200, 100, 1f);
        var context = (SkiaRenderContext)surface.RenderContext;
        var canvas = context.Canvas;
        context.BeginFrame(200, 100, 1f, new Color(0, 0, 0));
        context.PushClip(new Rect(0, 0, 20, 20));
        context.PushClip(new Rect(0, 0, 10, 10));

        context.BeginFrame(200, 100, 1f, new Color(0, 0, 0));

        Assert.Equal(1, canvas.SaveCount);
        Assert.Equal(new SKRectI(0, 0, 200, 100), canvas.DeviceClipBounds);
        context.EndFrame();
    }
}
