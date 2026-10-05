using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Styling;
using HultUI.Headless.Recording;
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

    /// <summary>
    /// This window's file dialogs. Nothing opens; queue what the user would have picked
    /// with <see cref="HeadlessFileDialogs.EnqueueResult"/> before triggering the action
    /// that opens one.
    /// </summary>
    public HeadlessFileDialogs FileDialogs => (HeadlessFileDialogs)_platform.FileDialogs;

    /// <summary>The window's recorded text-input host (IME arm/disarm state).</summary>
    public HeadlessTextInputHost TextInputHost => (HeadlessTextInputHost)_platform.TextInputHost;

    /// <summary>The last cursor the framework requested (e.g. the text I-beam over an input).</summary>
    public CursorType LastCursor => _platform.LastCursor;

    /// <summary>Whether this window's text is pinned to the bundled fonts — see <see cref="HeadlessOptions.PinnedText"/>.</summary>
    public bool PinnedText => _platform.PinnedText;

    /// <summary>
    /// The captured display list, when the session was created with
    /// <c>RecordDrawCalls</c>. Compare <c>RenderLog.ToString()</c> against a
    /// checked-in baseline for snapshot testing.
    /// </summary>
    public RenderLog RenderLog => _platform.RenderLog
        ?? throw new InvalidOperationException(
            "Draw-call recording is off. Create the session with " +
            "HeadlessOptions.DeterministicRecording() or { RecordDrawCalls = true }.");

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

    /// <summary>
    /// Installs a specific <see cref="IAccessibilityBridge"/> — for driving a
    /// real platform bridge (UIA / AT-SPI) through the window's true per-frame
    /// build/diff path with no display attached. Returns the bridge for chaining.
    /// </summary>
    public T InstallAccessibilityBridge<T>(T bridge) where T : IAccessibilityBridge
    {
        Window.SetAccessibilityBridge(bridge);
        return bridge;
    }

    /// <summary>
    /// Installs the platform-neutral accessibility action router and registers it
    /// with the window (so its queued invoke/toggle/select actions are drained on
    /// the pump thread each frame). Returns the router as an
    /// <see cref="IAccessibilityActionHost"/> for injecting into a platform bridge
    /// — lets a bridge's action path be driven under headless with no real window.
    /// </summary>
    public IAccessibilityActionHost InstallActionRouter()
    {
        var router = new AccessibilityActionRouter(Window);
        Window.SetActionRouter(router);
        return router;
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
    /// scale. Cross-machine caution: text glyphs come from system fonts unless
    /// the session was created with <see cref="HeadlessOptions.Golden"/>, so
    /// without it assert on your own drawing, not text rasterization, if the
    /// test must pass on more than one OS.
    /// </summary>
    public SKImage CapturePixels()
    {
        _session.RenderNow();
        return _platform.RasterSurface.Snapshot();
    }
}
