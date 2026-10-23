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
        _platform.Entering ??= session.Enter;
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

    /// <summary>
    /// Stands in for the OS minimizing the window (true) and restoring it (false). A minimized
    /// window is not drawn: its frames stop before painting and keep their dirt for the first
    /// frame after the restore, as on a real platform, and its animations still run.
    /// <see cref="Window.Minimize"/> does not set it: there is no task bar to restore from, and
    /// an app's own minimize button would leave a test with a window that never draws.
    /// </summary>
    public bool Minimized
    {
        get => _platform.IsMinimized;
        set => _platform.IsMinimized = value;
    }

    /// <summary>
    /// The display refresh rate the window reports, in Hz. 0, the default, is unknown, which the
    /// loop takes as 60 Hz. Set it to run <see cref="HeadlessSession.RunLoop"/> as on a faster
    /// display; a pump draws whenever it is asked, whatever this says.
    /// </summary>
    public float RefreshRate
    {
        get => _platform.RefreshRate;
        set => _platform.RefreshRate = value;
    }

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
    /// How many accessibility trees the window's frames have built for a listening bridge. A
    /// tree is built when the structure or a property changed, and at most every 100 ms for a
    /// scroll or a layout change; a list's selection move is patched on the applied tree and
    /// builds none. The count says how often that work ran, whatever the trees held.
    /// </summary>
    public int AccessibilityTreesBuilt => Window.AccessibilityTreesBuilt;

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

    public void SetRoot(Element root)
    {
        _session.Application.Enter();
        Window.SetRoot(root);
    }

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
    /// Raised from the event pump (the next <see cref="HeadlessSession.PumpFrame()"/>) after the
    /// window's outer position changed — placed, maximized, restored, moved by the person or off
    /// a monitor that went — as SDL's window-moved event is. Windows-like windows only
    /// (<see cref="HeadlessOptions.WindowsFrame"/>).
    /// </summary>
    public event Action? Moved
    {
        add => _platform.Moved += value;
        remove => _platform.Moved -= value;
    }

    /// <summary>
    /// Where the window's content (client area) is, in screen pixels: what <c>ClientToScreen</c>
    /// and <c>GetClientRect</c> give on Windows. Smaller than <see cref="Window.Placement"/> by the
    /// invisible resize borders. Windows-like windows only (<see cref="HeadlessOptions.WindowsFrame"/>).
    /// </summary>
    public Core.Primitives.Rect ContentBounds => _platform.ContentBounds;

    /// <summary>
    /// Stands in for the person moving or resizing the window by its title bar or borders (or
    /// snapping it): the outer rectangle, in pixels, is set as given, without fitting, and a
    /// maximized window comes out of maximized. <see cref="Moved"/> follows from the next pump.
    /// Windows-like windows only (<see cref="HeadlessOptions.WindowsFrame"/>).
    /// </summary>
    public void MoveByUser(int x, int y, int width, int height) =>
        _platform.MoveByUser(new Core.Platform.WindowPlacement(x, y, width, height, false));

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
