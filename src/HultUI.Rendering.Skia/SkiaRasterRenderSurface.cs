using HultUI.Core.Rendering;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

/// <summary>
/// CPU-backed render surface: draws into a raster <see cref="SKSurface"/> with
/// no GL context, window, or display. Reuses the exact same render context,
/// measure context, and text pipeline as <see cref="SkiaGLRenderSurface"/>, so
/// layout and drawing behave identically to the on-screen backend. Used by
/// HultUI.Headless; also suitable for server-side or golden-image rendering —
/// <see cref="Snapshot"/> and <see cref="ReadPixels"/> expose the frame's pixels. Given a
/// <see cref="PixelTarget"/>, it draws into memory a platform window shows instead of its own.
/// </summary>
public sealed class SkiaRasterRenderSurface : IRenderSurface, ISkiaFrameCapture
{
    private readonly SkiaRenderContext _renderContext = new();
    private readonly SkiaTextServices _text;
    private readonly bool _ownsText;

    private SKSurface? _surface;
    private readonly Func<PixelTarget?>? _target;
    // What _surface draws into while there is a target: made again when the target changes.
    private PixelTarget _drawingInto;
    // Filled by PeekPixels each frame, so that a window presenting every frame allocates nothing.
    private readonly SKPixmap _pixmap = new();
    private int _width;
    private int _height;
    private float _scaleFactor = 1f;

    public IRenderContext RenderContext => _renderContext;
    public IMeasureContext MeasureContext => _text.MeasureContext;

    /// <summary>Framebuffer width in physical pixels.</summary>
    public int PixelWidth => _width;

    /// <summary>Framebuffer height in physical pixels.</summary>
    public int PixelHeight => _height;

    /// <summary>
    /// The byte order of the frame's pixels, as <see cref="PeekPixels"/> and <see cref="Snapshot"/>
    /// hand them out: Skia's native order (<see cref="SKImageInfo.PlatformColorType"/>), which is
    /// <see cref="SKColorType.Bgra8888"/> on Windows, Linux and macOS. Premultiplied. A surface
    /// that draws into a <see cref="PixelTarget"/> draws in the target's order, and says
    /// <see cref="SKColorType.Unknown"/> until its first frame.
    ///
    /// <para>Not RGBA, though the GL backend's framebuffer is: Skia's CPU rasterizer takes its fast
    /// paths only into its native order. Into RGBA the same frame cost about twice as much on
    /// Windows and three to eight times on Linux, in Skia m119 and m156 alike, and m156 widened
    /// the gap (docs/DECISIONS.md, 2026-11-17). <see cref="ReadPixels"/> and
    /// <see cref="PresentCapturing"/> still give RGBA.</para>
    /// </summary>
    public SKColorType PixelColorType => _target is null ? ColorType : _drawingInto.ColorType;

    private static readonly SKColorType ColorType = SKImageInfo.PlatformColorType;

    /// <param name="text">Text services to use instead of building its own: one several
    /// surfaces share. The caller keeps it and disposes it; the surface does not. Null
    /// builds a fresh one, which the surface disposes with itself.</param>
    public SkiaRasterRenderSurface(SkiaTextServices? text = null)
    {
        _ownsText = text is null;
        _text = text ?? new SkiaTextServices();
        _renderContext.SetPipeline(_text.Pipeline);
    }

    /// <summary>
    /// A surface that draws into memory it does not own: a platform window's framebuffer, so a
    /// frame is drawn where the window shows it from and needs no copy.
    /// </summary>
    /// <param name="text">As for the other constructor.</param>
    /// <param name="target">Asked at the start of every frame where to draw it. A window gets new
    /// memory when it is resized, so the answer may change from frame to frame, and the surface
    /// follows it. Null skips the frame: there is nothing to draw into, as for a minimized
    /// window. <see cref="Resize"/> then only records the scale; the target says the size.</param>
    public SkiaRasterRenderSurface(SkiaTextServices? text, Func<PixelTarget?> target)
        : this(text)
    {
        _target = target;
    }

    /// <summary>
    /// Memory to draw a frame into: <paramref name="Width"/> by <paramref name="Height"/> pixels
    /// of 32 bits, <paramref name="RowBytes"/> apart, in <paramref name="ColorType"/>'s byte
    /// order (<see cref="SKColorType.Bgra8888"/> or <see cref="SKColorType.Rgba8888"/>).
    /// </summary>
    public readonly record struct PixelTarget(nint Pixels, int Width, int Height, int RowBytes, SKColorType ColorType);

    /// <summary>Whether frames are drawn into a target's memory rather than the surface's own.</summary>
    public bool DrawsIntoTarget => _target is not null;

    /// <summary>Draws text as outlines — see <see cref="SkiaRenderContext.TextAsOutlines"/>. With
    /// a pinned font collection, what is drawn is the same on every machine.</summary>
    public bool TextAsOutlines
    {
        get => _renderContext.TextAsOutlines;
        set => _renderContext.TextAsOutlines = value;
    }

    /// <summary>The face canvas-element text uses instead of the platform default.</summary>
    public SKTypeface? CanvasTypeface
    {
        get => _renderContext.CanvasTypeface;
        set => _renderContext.CanvasTypeface = value;
    }

    public void Resize(int widthPx, int heightPx, float scaleFactor)
    {
        if (_target is not null)
        {
            // The target says the size, at the start of each frame.
            _scaleFactor = scaleFactor;
            return;
        }
        if (widthPx == _width && heightPx == _height && Math.Abs(scaleFactor - _scaleFactor) < 0.001f)
            return;
        if (widthPx <= 0 || heightPx <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthPx),
                $"Surface size must be positive ({widthPx}x{heightPx}).");

        _width = widthPx;
        _height = heightPx;
        _scaleFactor = scaleFactor;

        _surface?.Dispose();
        // Skia's native order, premultiplied (see PixelColorType).
        var info = new SKImageInfo(widthPx, heightPx, ColorType, SKAlphaType.Premul);
        _surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException($"Failed to create raster surface ({widthPx}x{heightPx})");

        _renderContext.SetCanvas(_surface.Canvas);
    }

    /// <summary>
    /// With a target, points the canvas at it, made again whenever the target moved or changed
    /// size, and returns false when there is none this frame. Without one there is nothing to do.
    /// </summary>
    public bool BeginFrame()
    {
        if (_target is null) return true;
        if (_target() is not { } target) return false;
        if (_surface is null || target != _drawingInto)
        {
            _surface?.Dispose();
            var info = new SKImageInfo(target.Width, target.Height, target.ColorType, SKAlphaType.Premul);
            _surface = SKSurface.Create(info, target.Pixels, target.RowBytes)
                ?? throw new InvalidOperationException(
                    $"Failed to draw into {target.Width}x{target.Height} {target.ColorType} pixels.");
            _drawingInto = target;
            _width = target.Width;
            _height = target.Height;
            _renderContext.SetCanvas(_surface.Canvas);
        }
        return true;
    }

    public void Present()
    {
        _surface?.Canvas.Flush();
    }

    /// <inheritdoc />
    public SKImage PresentCapturing()
    {
        Present();
        if (_surface is null)
            throw new InvalidOperationException("Surface not initialized — call Resize first.");
        return SkiaFrameCapture.Copy(_surface, _width, _height);
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
    /// Where the current frame's pixels lie, without copying them, for a platform window that
    /// copies them to the screen: <see cref="PixelWidth"/> by <see cref="PixelHeight"/>, in
    /// <see cref="PixelColorType"/> premultiplied. Valid until the next draw or
    /// <see cref="Resize"/>, because a draw after a <see cref="Snapshot"/> moves them. False
    /// before the first Resize.
    /// </summary>
    public bool PeekPixels(out nint pixels, out int rowBytes)
    {
        if (_surface is null || !_surface.PeekPixels(_pixmap))
        {
            pixels = 0;
            rowBytes = 0;
            return false;
        }
        pixels = _pixmap.GetPixels();
        rowBytes = _pixmap.RowBytes;
        return true;
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
        _pixmap.Dispose();
        if (_ownsText) _text.Dispose();
    }
}
