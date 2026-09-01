using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Headless.Recording;
using HultUI.Rendering.Skia;

namespace HultUI.Headless;

/// <summary>
/// A platform window with no OS window behind it: a raster Skia surface,
/// manual input injection, and recorded (rather than applied) window state.
/// Deliberately does not implement <see cref="IWindowChrome"/> — the framework
/// Window skips all native-frame wiring for chromeless platforms.
/// </summary>
internal sealed class HeadlessPlatformWindow : IPlatformWindow
{
    private readonly SkiaRasterRenderSurface _raster;
    private readonly IRenderSurface _surface;
    private float _layoutWidth;
    private float _layoutHeight;
    private float _scale;
    private bool _disposed;

    public HeadlessPlatformWindow(string title, int width, int height, float scale, WindowStyle style,
        HeadlessOptions? options = null)
    {
        if (scale <= 0f) throw new ArgumentOutOfRangeException(nameof(scale));
        Title = title;
        Style = style;
        _layoutWidth = width;
        _layoutHeight = height;
        _scale = scale;
        _raster = new SkiaRasterRenderSurface();

        // Recording wraps the raster surface rather than replacing it, so the
        // frame still renders and pixel capture keeps working.
        if (options?.RecordDrawCalls == true)
        {
            var recording = new RecordingRenderSurface(_raster, options.MeasureContextOverride);
            RenderLog = recording.Log;
            _surface = recording;
        }
        else
        {
            _surface = _raster;
        }

        _surface.Resize(PixelWidth, PixelHeight, scale);
        IsOpen = true;
    }

    /// <summary>The captured display list, or null when recording is off.</summary>
    public RenderLog? RenderLog { get; }

    public string Title { get; set; }
    public WindowStyle Style { get; }
    public bool IsOpen { get; private set; }
    public bool IsMaximized { get; private set; }

    /// <summary>True after the first presented frame revealed the window (mirrors the real platform's hidden-until-first-frame behavior).</summary>
    public bool IsShown { get; private set; }

    /// <summary>The last cursor the framework asked for — assertable in tests (e.g. the text I-beam over an input).</summary>
    public CursorType LastCursor { get; private set; } = CursorType.Default;

    public IRenderSurface Surface => _surface;

    /// <summary>The raster surface, typed — for pixel readback.</summary>
    internal SkiaRasterRenderSurface RasterSurface => _raster;

    // The client's pixel size is what stays fixed under a zoom, as it is on a real window:
    // zooming in makes everything bigger and leaves fewer layout units on screen.
    public float LayoutWidth => _layoutWidth / _userScale;
    public float LayoutHeight => _layoutHeight / _userScale;
    public float DisplayScale => _scale * _userScale;

    private float _userScale = 1f;

    /// <inheritdoc/>
    public float UserScale
    {
        get => _userScale;
        set
        {
            float clamped = Math.Clamp(value, 0.5f, 3f);
            if (Math.Abs(clamped - _userScale) < 0.0001f) return;
            _userScale = clamped;
            _surface.Resize(PixelWidth, PixelHeight, DisplayScale);
            ResizePending = true;
        }
    }

    public int PixelWidth => (int)MathF.Round(_layoutWidth * _scale);
    public int PixelHeight => (int)MathF.Round(_layoutHeight * _scale);

    public IClipboard Clipboard { get; } = new HeadlessClipboard();
    public IFileDialogs FileDialogs { get; } = new HeadlessFileDialogs();
    public ITextInputHost TextInputHost { get; } = new HeadlessTextInputHost();

    public event Action<InputEvent>? InputReceived;

    public bool ResizePending { get; set; }
    public bool ActivatedPending { get; set; }
    public long LastResizeRenderTick { get; set; }
    public Action? LiveResizeFrame { get; set; }

    /// <summary>Delivers an input event to the framework Window, synchronously, in layout units.</summary>
    public void RaiseInput(InputEvent evt) => InputReceived?.Invoke(evt);

    /// <summary>Resizes the client area, in layout units. The surface resizes immediately.</summary>
    public void SetClientSize(float layoutWidth, float layoutHeight)
    {
        if (layoutWidth <= 0 || layoutHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(layoutWidth),
                $"Client size must be positive ({layoutWidth}x{layoutHeight}).");
        _layoutWidth = layoutWidth;
        _layoutHeight = layoutHeight;
        _surface.Resize(PixelWidth, PixelHeight, DisplayScale);
    }

    /// <summary>Changes the display scale, keeping the layout-unit size constant (the OS DPI-change behavior).</summary>
    public void SetScale(float scale)
    {
        if (scale <= 0f) throw new ArgumentOutOfRangeException(nameof(scale));
        _scale = scale;
        _surface.Resize(PixelWidth, PixelHeight, DisplayScale);
    }

    public void MakeCurrent() { }

    public void EnsureShown() => IsShown = true;

    public void SwapBuffers() { }

    public void Close() => IsOpen = false;

    public void Minimize() { }

    public void Maximize() => IsMaximized = true;

    public void Restore() => IsMaximized = false;
    public void Raise() { }

    public void SetMinimumSize(int layoutWidth, int layoutHeight) { }

    public void SetIcon(int width, int height, ReadOnlySpan<byte> rgbaPixels) { }

    public void SetCursor(CursorType cursor) => LastCursor = cursor;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        IsOpen = false;
        _surface.Dispose();
    }
}
