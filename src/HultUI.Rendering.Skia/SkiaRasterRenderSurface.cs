using HultUI.Core.Rendering;
using HultUI.Text;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

/// <summary>
/// CPU-backed render surface: draws into a raster <see cref="SKSurface"/> with
/// no GL context, window, or display. Reuses the exact same render context,
/// measure context, and text pipeline as <see cref="SkiaGLRenderSurface"/>, so
/// layout and drawing behave identically to the on-screen backend. Used by
/// HultUI.Headless; also suitable for server-side or golden-image rendering —
/// <see cref="Snapshot"/> and <see cref="ReadPixels"/> expose the frame's pixels.
/// </summary>
public sealed class SkiaRasterRenderSurface : IRenderSurface
{
    private readonly SkiaRenderContext _renderContext = new();
    private readonly SkiaMeasureContext _measureContext;
    private readonly TextPipeline _pipeline;

    private SKSurface? _surface;
    private int _width;
    private int _height;
    private float _scaleFactor = 1f;

    public IRenderContext RenderContext => _renderContext;
    public IMeasureContext MeasureContext => _measureContext;

    /// <summary>Framebuffer width in physical pixels.</summary>
    public int PixelWidth => _width;

    /// <summary>Framebuffer height in physical pixels.</summary>
    public int PixelHeight => _height;

    public SkiaRasterRenderSurface()
    {
        var fontCollection = new SkiaFontCollection();
        var textShaper = new SkiaTextShaper();
        _pipeline = new TextPipeline(fontCollection, textShaper);
        _measureContext = new SkiaMeasureContext(_pipeline);
        _renderContext.SetPipeline(_pipeline);
    }

    public void Resize(int widthPx, int heightPx, float scaleFactor)
    {
        if (widthPx == _width && heightPx == _height && Math.Abs(scaleFactor - _scaleFactor) < 0.001f)
            return;
        if (widthPx <= 0 || heightPx <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthPx),
                $"Surface size must be positive ({widthPx}x{heightPx}).");

        _width = widthPx;
        _height = heightPx;
        _scaleFactor = scaleFactor;

        _surface?.Dispose();
        // Premul RGBA to match the GL backend's pixel format.
        var info = new SKImageInfo(widthPx, heightPx, SKColorType.Rgba8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException($"Failed to create raster surface ({widthPx}x{heightPx})");

        _renderContext.SetCanvas(_surface.Canvas);
    }

    public void Present()
    {
        _surface?.Canvas.Flush();
    }

    /// <summary>
    /// Immutable snapshot of the current frame's pixels. The caller owns the
    /// returned image and must dispose it.
    /// </summary>
    public SKImage Snapshot()
    {
        if (_surface is null)
            throw new InvalidOperationException("Surface not initialized — call Resize first.");
        return _surface.Snapshot();
    }

    /// <summary>
    /// Copies the current frame into a new RGBA8888 byte array (row-major,
    /// <paramref name="rowBytes"/> = width * 4).
    /// </summary>
    public byte[] ReadPixels(out int width, out int height, out int rowBytes)
    {
        if (_surface is null)
            throw new InvalidOperationException("Surface not initialized — call Resize first.");

        width = _width;
        height = _height;
        rowBytes = _width * 4;

        var info = new SKImageInfo(_width, _height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var buffer = new byte[rowBytes * _height];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer,
            System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            if (!_surface.ReadPixels(info, handle.AddrOfPinnedObject(), rowBytes, 0, 0))
                throw new InvalidOperationException("ReadPixels failed.");
        }
        finally
        {
            handle.Free();
        }
        return buffer;
    }

    public void Dispose()
    {
        _renderContext.Dispose();
        _surface?.Dispose();
        _surface = null;
    }
}
