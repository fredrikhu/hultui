using System.Runtime.CompilerServices;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using SkiaSharp;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// The render context keeps a wrapper over a GPU source's own texture from frame to frame, keyed
/// by the source. A source that goes for good (its surface's <c>ReleaseFrameTarget</c>) lets the
/// wrapper go as the next frame begins; kept, the wrapper's key held the source, and through it
/// the source's renderer and frame buffers, for as long as the window lived (plan 17 round 15: a
/// player disposed on the GL renderer stayed reachable, one a cycle). A GL surface the tests could
/// draw with needs a window: the external surface here wraps a raster image instead, and
/// HultUI.UiTests has the real one (GpuFrameSinkGLTests).
/// </summary>
public class GpuSourceWrapperReleaseTests
{
    [Fact]
    public void AReleasedSource_LosesItsWrapper_AsTheNextFrameBegins_AndIsNotKept()
    {
        using var target = SKSurface.Create(new SKImageInfo(50, 50));
        var surface = new WrappingSurface();
        using var ctx = new SkiaRenderContext();
        ctx.SetCanvas(target.Canvas);
        ctx.SetGLSurface(surface);

        var source = DrawTwiceAndRelease(ctx);

        // Released but not yet dropped: that waits for the render thread's next frame.
        Assert.Equal(1, ctx.WrappedTextureCount);
        ctx.BeginFrame(50, 50, 1f, Color.White);
        ctx.EndFrame();

        Assert.Equal(0, ctx.WrappedTextureCount);
        Assert.Single(surface.Made);
        Assert.Equal(IntPtr.Zero, surface.Made[0].Handle);   // the wrapper was disposed
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(source.TryGetTarget(out _), "the context still holds the released source");
    }

    [Fact]
    public void ASourceThatOnlyStopsDrawing_KeepsItsWrapper()
    {
        using var target = SKSurface.Create(new SKImageInfo(50, 50));
        var surface = new WrappingSurface();
        using var ctx = new SkiaRenderContext();
        ctx.SetCanvas(target.Canvas);
        ctx.SetGLSurface(surface);
        var source = new TextureSource();

        ctx.BeginFrame(50, 50, 1f, Color.White);
        ctx.DrawImage(source, new Rect(0, 0, 50, 50), ImageFit.Fill);
        ctx.EndFrame();
        ctx.BeginFrame(50, 50, 1f, Color.White);
        ctx.EndFrame();

        // Hidden for a while is not gone: the wrapper is a view of a texture the source keeps.
        Assert.Equal(1, ctx.WrappedTextureCount);
        Assert.NotEqual(IntPtr.Zero, surface.Made[0].Handle);
        GC.KeepAlive(source);
    }

    // Its own method, so no local of the caller's frame keeps the source alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<TextureSource> DrawTwiceAndRelease(SkiaRenderContext ctx)
    {
        var source = new TextureSource();
        for (int i = 0; i < 2; i++)
        {
            ctx.BeginFrame(50, 50, 1f, Color.White);
            ctx.DrawImage(source, new Rect(0, 0, 50, 50), ImageFit.Fill);
            ctx.EndFrame();
        }
        Assert.Equal(1, ctx.WrappedTextureCount);
        ctx.ReleaseGpuSource(source);
        return new WeakReference<TextureSource>(source);
    }

    /// <summary>A source drawing into a texture of its own every frame: the same texture, so the
    /// context wraps it once and keeps the wrapper.</summary>
    private sealed class TextureSource : IGpuImageSource
    {
        public Size IntrinsicSize => new(4, 4);
        public bool IsLoaded => true;
        public string? AlternateText => null;

        public bool TryRenderFrame(IExternalGLSurface surface, out GpuFrame frame)
        {
            frame = new GpuFrame(7, 4, 4, false);
            return true;
        }
    }

    /// <summary>An external surface whose "texture" is a raster image, and which remembers every
    /// wrapper it made.</summary>
    private sealed class WrappingSurface : ISkiaExternalSurface
    {
        public List<SKImage> Made { get; } = [];
        public IRenderContext RenderContext => throw new NotSupportedException();
        public IMeasureContext MeasureContext => throw new NotSupportedException();
        public void Resize(int widthPx, int heightPx, float scaleFactor) { }
        public void Present() { }
        public void Dispose() { }
        public Func<string, nint> GetProcAddress => _ => 0;
        public void RunExternalGL(Action draw) => draw();

        public SKImage? WrapFrame(in GpuFrame frame)
        {
            using var bitmap = new SKBitmap(frame.Width, frame.Height);
            bitmap.Erase(SKColors.Blue);
            var image = SKImage.FromBitmap(bitmap);
            Made.Add(image);
            return image;
        }
    }
}
