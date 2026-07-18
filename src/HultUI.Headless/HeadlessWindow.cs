using HultUI.Core.Elements;
using HultUI.Core.Styling;
using SkiaSharp;

namespace HultUI.Headless;

/// <summary>
/// Headless driver for a single window: content setup, window-state control,
/// and pixel capture. Input injection and element queries build on this in
/// the same type (see the input-driver and query members).
/// </summary>
public sealed partial class HeadlessWindow
{
    private readonly HeadlessSession _session;
    private readonly HeadlessPlatformWindow _platform;

    internal HeadlessWindow(HeadlessSession session, Window window, HeadlessPlatformWindow platform)
    {
        _session = session;
        Window = window;
        _platform = platform;
    }

    /// <summary>The framework window under test.</summary>
    public Window Window { get; }

    /// <summary>The window's in-memory clipboard.</summary>
    public HeadlessClipboard Clipboard => (HeadlessClipboard)_platform.Clipboard;

    /// <summary>The window's recorded text-input host (IME arm/disarm state).</summary>
    public HeadlessTextInputHost TextInputHost => (HeadlessTextInputHost)_platform.TextInputHost;

    /// <summary>The last cursor the framework requested (e.g. the text I-beam over an input).</summary>
    public CursorType LastCursor => _platform.LastCursor;

    /// <summary>Snapshot of the open overlay content roots (tooltips, context menus, dialogs), in z-order.</summary>
    public IReadOnlyList<Element> OverlayContents => Window.OverlayContents.ToList();

    /// <summary>
    /// Installs a recording accessibility bridge, turning on the window's real
    /// per-frame a11y build/diff path (normally gated on an assistive
    /// technology being attached). Pump a frame afterwards to get the initial
    /// full-tree diff.
    /// </summary>
    public HeadlessAccessibilityBridge InstallAccessibilityBridge()
    {
        var bridge = new HeadlessAccessibilityBridge();
        Window.SetAccessibilityBridge(bridge);
        return bridge;
    }

    public void SetRoot(Element root) => Window.SetRoot(root);

    /// <summary>
    /// Resizes the client area, in layout units, and relayouts immediately —
    /// no resize throttling headless, so the new layout is visible to the very
    /// next assertion.
    /// </summary>
    public void SetClientSize(int layoutWidth, int layoutHeight)
    {
        _platform.SetClientSize(layoutWidth, layoutHeight);
        Window.OnWindowResized();
    }

    /// <summary>Changes the display scale (DPI), keeping the layout-unit size constant.</summary>
    public void SetScale(float scale)
    {
        _platform.SetScale(scale);
        Window.OnWindowResized();
    }

    /// <summary>
    /// Renders a fresh frame and returns its pixels. The caller owns the
    /// returned image and must dispose it. Pixel dimensions are layout size ×
    /// scale. Cross-machine caution: text glyphs come from system fonts, so
    /// assert on your own drawing, not text rasterization, if the test must
    /// pass on more than one OS.
    /// </summary>
    public SKImage CapturePixels()
    {
        _session.RenderNow();
        return _platform.RasterSurface.Snapshot();
    }
}
