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

    /// <param name="text">The text services the surface draws and measures with: the
    /// platform's, which every window of the session shares. The platform disposes it.</param>
    public HeadlessPlatformWindow(string title, int width, int height, float scale, WindowStyle style,
        SkiaTextServices text, HeadlessOptions? options = null)
    {
        if (scale <= 0f) throw new ArgumentOutOfRangeException(nameof(scale));
        Title = title;
        Style = style;
        _layoutWidth = width;
        _layoutHeight = height;
        _scale = scale;
        PinnedText = options?.PinnedText == true;
        _raster = PinnedText ? PinnedFonts.CreateSurface(text) : new SkiaRasterRenderSurface(text);

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

    /// <summary>Whether text draws in the bundled fonts, as outlines (<see cref="HeadlessOptions.PinnedText"/>).</summary>
    public bool PinnedText { get; }

    /// <summary>The captured display list, or null when recording is off.</summary>
    public RenderLog? RenderLog { get; }

    public string Title { get; set; }
    public WindowStyle Style { get; }
    public bool IsOpen { get; private set; }
    public bool IsMaximized { get; private set; }

    /// <summary>
    /// Recorded rather than acted on: a headless surface is a fixed-size buffer, and
    /// resizing it to "the display" would mean inventing one. The flag is what an app's
    /// fullscreen wiring is tested against — that F reaches the window at all, and that
    /// the app stops drawing its own title bar while it is set.
    /// </summary>
    public bool IsFullscreen { get; private set; }

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
    public float RefreshRate => 0f;   // no display; the session drives frames itself

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

    public void SetFullscreen(bool fullscreen) => IsFullscreen = fullscreen;

    private WindowPlacement? _placement;

    /// <summary>
    /// Recorded, not applied, for the reason <see cref="IsFullscreen"/> is: there is no desktop
    /// to place the window on, and resizing the surface to saved bounds would make a test's
    /// geometry depend on whatever the app last saved. What it gives a test is the app's wiring
    /// — that a saved placement reaches the window and the window's comes back out to be saved.
    /// The maximized flag is applied, as <see cref="Maximize"/> applies it. Before anything is
    /// set it reports the client area at the origin.
    /// </summary>
    public WindowPlacement? Placement
    {
        get => (_placement ?? new WindowPlacement(0, 0, PixelWidth, PixelHeight, false))
            with { Maximized = IsMaximized };
        set
        {
            if (value is not { IsUsable: true }) return;
            _placement = value;
            IsMaximized = value.Maximized;
        }
    }

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
