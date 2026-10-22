using HultUI.Core.Events;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
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

    // text: the services the surface draws and measures with — the platform's, which every
    // window of the session shares and the platform disposes.
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

    /// <summary>Set by <see cref="HeadlessWindow.Minimized"/>; <see cref="Minimize"/> leaves it.</summary>
    public bool IsMinimized { get; set; }

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
    /// <summary>0, unknown, unless <see cref="HeadlessWindow.RefreshRate"/> set it: no display,
    /// and a pump draws whenever it is asked.</summary>
    public float RefreshRate { get; set; }

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
    public void RaiseInput(InputEvent evt)
    {
        Entering?.Invoke();
        InputReceived?.Invoke(evt);
    }

    /// <summary>Runs before injected input is dispatched: enters the session's application, so
    /// what its handlers start is that session's (sessions side by side).</summary>
    internal Action? Entering { get; set; }

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

    public void Maximize()
    {
        IsMaximized = true;
        ApplyDesktop();
    }

    public void Restore()
    {
        IsMaximized = false;
        ApplyDesktop();
    }

    public void SetFullscreen(bool fullscreen)
    {
        IsFullscreen = fullscreen;
        ApplyDesktop();
    }

    private WindowPlacement? _placement;

    /// <summary>
    /// Without simulated monitors: recorded, not applied, for the reason <see cref="IsFullscreen"/>
    /// is — there is no desktop to place the window on, and resizing the surface to saved bounds
    /// would make a test's geometry depend on whatever the app last saved. What it gives a test is
    /// the app's wiring: that a saved placement reaches the window and the window's comes back out
    /// to be saved. The maximized flag is applied, as <see cref="Maximize"/> applies it. Before
    /// anything is set it reports the client area at the origin.
    ///
    /// <para>With simulated monitors (<see cref="HeadlessOptions.Monitors"/>) there is a desktop,
    /// and the window lives on it the way a real one does: a placement is fitted to the work
    /// areas and applied, maximized fills its monitor's work area, full screen its bounds, and
    /// the window takes its monitor's scale. The surface is resized to match, so a frame
    /// captured afterwards is the size it would be on that monitor.</para>
    /// </summary>
    public WindowPlacement? Placement
    {
        get => (_placement ?? DefaultPlacement()) with { Maximized = IsMaximized };
        set
        {
            if (value is not { IsUsable: true }) return;
            var monitors = Monitors;
            _placement = monitors.Count > 0 ? value.FitTo(monitors.Select(m => m.WorkArea).ToList()) : value;
            IsMaximized = value.Maximized;
            if (monitors.Count > 0) IsFullscreen = false;
            ApplyDesktop();
        }
    }

    /// <summary>The simulated monitors this window lives on; null or empty for no desktop.</summary>
    internal Func<IReadOnlyList<MonitorInfo>>? Desktop { get; init; }

    private IReadOnlyList<MonitorInfo> Monitors => Desktop?.Invoke() ?? [];

    /// <summary>Before any placement: the client area at the top left of the primary monitor's
    /// work area, or at the origin with no desktop.</summary>
    private WindowPlacement DefaultPlacement()
    {
        var monitors = Monitors;
        var home = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        return home is null
            ? new WindowPlacement(0, 0, PixelWidth, PixelHeight, false)
            : new WindowPlacement((int)home.WorkArea.X, (int)home.WorkArea.Y, PixelWidth, PixelHeight, false);
    }

    /// <summary>The monitors changed: brought back within reach, as an OS moves a window whose
    /// monitor went, and re-fitted to its monitor when maximized or full screen.</summary>
    internal void OnDesktopChanged()
    {
        var monitors = Monitors;
        if (monitors.Count == 0) return;
        _placement = (_placement ?? DefaultPlacement()).FitTo(monitors.Select(m => m.WorkArea).ToList());
        ApplyDesktop();
    }

    /// <summary>
    /// With simulated monitors, sizes the surface to where the window now is: its monitor's
    /// bounds when full screen, the work area when maximized, the restored bounds otherwise, at
    /// the monitor's scale. Flags a resize so the next frame lays out at the new size. A no-op
    /// without monitors, which keeps the record-only behaviour.
    /// </summary>
    private void ApplyDesktop()
    {
        var monitors = Monitors;
        if (monitors.Count == 0) return;
        var restored = _placement ?? DefaultPlacement();
        var monitor = MonitorInfo.For(new Rect(restored.X, restored.Y, restored.Width, restored.Height), monitors)!;
        var area = IsFullscreen ? monitor.Bounds
            : IsMaximized ? monitor.WorkArea
            : new Rect(restored.X, restored.Y, restored.Width, restored.Height);
        _scale = monitor.Scale;
        _layoutWidth = Math.Max(1f, area.Width / _scale);
        _layoutHeight = Math.Max(1f, area.Height / _scale);
        _surface.Resize(PixelWidth, PixelHeight, DisplayScale);
        ResizePending = true;
        LastResizeRenderTick = 0;   // relayout on the very next frame: no real-time throttle headless
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
