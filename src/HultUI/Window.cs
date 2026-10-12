using HultUI.Core.Accessibility;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.DevTools;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Core.Threading;

namespace HultUI;

public sealed class Window : IOverlayHost, IDisposable, IAccessibilityAnnouncer, IPreFrameHost
{
    private readonly LayoutEngine _layoutEngine;
    private readonly FocusManager _focusManager = new();
    private readonly PointerState _pointerState;
    private readonly PerformanceCounters _perf = new();
    private readonly FrameTracer _tracer = new();
    private readonly DevToolsOverlay _devTools;
    private Element? _root;
    private Element? _lastHoveredElement;
    // The cursor the pointer move last took from the hovered element's style, and whether
    // the cursor on screen is still that one (not a scrollbar's, a drag's, an autoscroll's).
    // Lets a style change show without waiting for the next move.
    private CursorType _hoverCursor;
    private bool _cursorFollowsHover;
    // The element that captured the pointer on its press (PointerDownEvent.CapturePointer),
    // and the overlay offset its coordinates carry; moves and the release go to it directly.
    private Element? _pointerCaptureTarget;
    private Point _pointerCaptureOffset;
    private IScrollbarDraggable? _scrollbarDragTarget;
    private IScrollbarDraggable? _scrollbarHoverTarget;

    // Whether the keyboard is in use: set by Tab and any other key press, cleared by a pointer
    // press. The focus visual is gated on it: clicking a control focuses it too, and a ring
    // that lights up on every click reads as something being stuck rather than as an aid.
    private bool _focusFromKeyboard;
    // Middle-button autoscroll, the way Windows does it: the press anchors an origin and
    // the pointer's distance from it becomes a speed, so the view keeps moving while the
    // button is held and nothing else has to happen. The view is captured on press rather
    // than re-hit-tested per frame, so a pointer dragged out over another list (or off the
    // window entirely) keeps scrolling the one the gesture started in.
    private IAutoScrollable? _autoScrollTarget;
    private Point _autoScrollOrigin;
    private Point _autoScrollPointer;
    private long _autoScrollLastMs;

    // Which scroller the current run of wheel events belongs to (see WheelGesture).
    private readonly WheelGesture _wheelGesture = new();

    /// <summary>How far the pointer must leave the origin before anything moves. Also the
    /// radius the anchor marker is drawn at, so the zone you have to come back into to stop
    /// is the circle you can see.</summary>
    private const float AutoScrollDeadZone = 14f;

    /// <summary>Layout units per second of scroll, per unit of pointer displacement past the
    /// dead zone. Linear rather than one of the accelerating curves: it is the one shape
    /// where the distance you are holding the pointer at predicts the speed you get.</summary>
    private const float AutoScrollGain = 9f;
    private TextInputElement? _textInputDragTarget;
    private TextAreaElement? _textAreaDragTarget;
    private TextElement? _textElementDragTarget;
    // Cross-label (group) text selection: a drag that spans several selectable labels
    // sharing a SelectionGroup token (e.g. a diff's per-line code labels).
    // The anchor lives in the group, in model coordinates, not as a reference to the label
    // the press landed in — a virtualized list recycles that label the moment it scrolls out
    // of view, and an element-anchored drag stops extending when it does.
    private TextSelectionGroup? _groupDragToken;   // non-null while a group drag is in progress
    private TextSelectionGroup? _activeTextGroup;  // group holding a live selection (cleared on outside press)
    private float _groupDragX;                     // X the drag started at, for the gutter re-probe
    private SelectionUnit _groupDragUnit;          // character / word / line, set by the press that started the drag
    private int _groupDragAnchorIndex = -1;        // line the drag started on
    private (int Start, int End) _groupDragAnchorRange; // the unit it started on, within that line
    private bool _disposed;

    // ──── Post-frame callbacks ────
    // One-shot actions run immediately after the next frame is presented. Used to
    // measure end-to-end update latency (mutation → painted) and to react once a
    // frame is on screen. Registering one guarantees a frame renders.
    private readonly List<Action> _postFrameCallbacks = [];
    // Work run at the start of the next frame, before style and layout (IPreFrameHost): the
    // drags and hovers that follow the pointer once a frame. Two lists, swapped for the run, so
    // a frame allocates nothing for them and one asked for during the run waits for the next.
    private List<Action> _preFrameCallbacks = [], _preFrameRunning = [];
    /// <summary>The button whose release is dropped: its press only closed an anchored overlay
    /// from that overlay's own anchor. -1 for none.</summary>
    private int _swallowedRelease = -1;

    // ──── Overlay state ────
    private readonly List<OverlayEntry> _overlays = [];
    private int _nextOverlayId = 1;

    // ──── Tooltip state ────
    private Element? _tooltipOwner;
    private TweenHandle? _tooltipDelayTween;
    private OverlayHandle _tooltipOverlay;

    // ──── Context menu state ────
    private OverlayHandle _contextMenuOverlay;
    private ContextMenuElement? _contextMenuElement;
    private Action<MenuCloseReason>? _contextMenuClosed;
    // ← and → for the menu's owner when the menu has no use for them (OpenMenu).
    private Action<int>? _contextMenuNavigate;
    // One level of nesting: the flyout for a root item with Children.
    private OverlayHandle _contextSubmenuOverlay;
    private ContextMenuElement? _contextSubmenuElement;

    // ──── Accessibility state ────
    private IAccessibilityBridge _accessibilityBridge = NullAccessibilityBridge.Instance;
    private AccessibilityNode? _previousA11yTree;
    // The element whose node _previousA11yTree marks focused: the focused one when the tree
    // was built, then each focus move's (see MoveA11yFocus). Null for none.
    private ElementId? _a11yFocusId;
    // The element the focus moved onto while it had no node in the applied tree — mounted in the
    // same handler, as Ctrl+F mounts a find bar and focuses its field. The bridge was told the
    // focus went nowhere; it is told where once the next rebuild has the node.
    private ElementId? _a11yPendingFocusId;
    private bool _a11yWasActive;

    /// <summary>Whether the last frame found the bridge active and built (or kept) a tree for it.
    /// UI thread.</summary>
    internal bool AccessibilityTreeBuilt => _a11yWasActive;

    /// <summary>How many accessibility trees this window's frames have built for a listening
    /// bridge (a patched selection move builds none). UI thread.</summary>
    internal int AccessibilityTreesBuilt { get; private set; }
    // Set when an element reports an a11y-visible property change (a text label's
    // content, a checkbox toggle, …). Such changes aren't structural, so they
    // force the next frame to rebuild+diff the tree — that regenerates the node
    // snapshots (Name, bounds, …) and emits precise property-changed events.
    private bool _a11yPropertyDirty;
    // Lists whose selection moved since the last frame, while a client listened. A move changes
    // the selected state of two item nodes, which the frame marks on the applied tree in place
    // and tells the bridge, as a focus move does (TryPatchA11ySelection), unless the frame
    // rebuilds the tree anyway; it no longer forces a rebuild of the whole tree on its own.
    private readonly List<Element> _a11ySelectionMoved = [];
    // Kept for the patch: the moved lists' nodes in the applied tree, and the changes found.
    private readonly List<(AccessibilityNode Node, IAccessibleItemSource Source)> _a11ySelectionLists = [];
    private readonly List<PropertyChange> _a11ySelectionChanges = [];
    /// <summary>Announcements made since the last frame, handed to the bridge once the tree
    /// holding their elements is applied (<see cref="Element.Announce"/>).</summary>
    private readonly List<(Element Source, string Text, AnnouncementPoliteness Politeness)> _announcements = [];
    // Layout-only changes (resize, scroll) refresh the a11y tree's bounds, but
    // rebuilding the whole snapshot every frame of a drag floods assistive tech
    // and is O(total items) for a virtualized list. These changes are coalesced:
    // rebuild on the leading edge, then at most every A11yCoalesceMs, then once on
    // the trailing edge — the batching browsers apply to a11y location updates.
    // Timed on the platform's clock, so a headless session's virtual time drives it.
    private bool _a11yLayoutRefreshPending;
    private long _lastA11yRebuildMs;
    private const long A11yCoalesceMs = 100;
    // A value in a range (a slider's, a progress bar's) can move on every frame: a drag, a
    // download, a playing video's scrubber, a key held down. Each refresh rebuilds the whole tree,
    // so such changes are told on the leading edge, then at most every A11yRangeCoalesceMs, then
    // once on the trailing edge with the value the range came to rest at. 250 ms is how often
    // Chromium moves a playing video's scrubber (HTMLMediaElement's timeupdate,
    // kMaxTimeupdateEventFrequency), whose aria-valuetext it leaves alone while playing.
    private bool _a11yRangeRefreshPending;
    private long _lastA11yRangeMs = long.MinValue / 2;
    private const long A11yRangeCoalesceMs = 250;

    private long A11yClockMs
    {
        get
        {
            var clock = Application.Platform.TimeProvider;
            return clock.GetTimestamp() * 1000 / clock.TimestampFrequency;
        }
    }
    private readonly AccessibilityObserverAdapter _a11yObserver;
    private UiaHostBridge? _uiaHost;
    private AccessibilityActionRouter? _actionRouter;

    // ──── Drag & Drop state ────
    private const float DragThreshold = 5f;
    private Element? _potentialDragSource;
    private Element? _dragSource;
    private Point _dragStartPos;
    private bool _isDragging;
    private DragData? _dragData;
    private Element? _dragHoveredElement;
    private Element? _dragVisual;
    private bool _dragVisualLayoutDone;
    private Point _lastDragPos;
    private bool _dragAccepted;
    private bool _isExternalDrag;
    // Modifiers as of the last drag input. Sampled per event rather than captured at
    // the start, because Shift/Ctrl are routinely pressed *during* a drag to change
    // what dropping means — and an OS drag delivers no key events at all, so its
    // modifiers only ever arrive attached to the drop-position updates.
    private KeyModifiers _dragModifiers;
    // Set while DragFilesOut is running its modal loop. The internal drag that was starting
    // when the handler called it is abandoned on the way back out: the OS owned the mouse for
    // the whole gesture, so there is no release left for EndInternalDrag to see.
    private bool _nativeDragRan;

    internal IPlatformWindow PlatformWindow { get; }

    // Custom-chrome capability of the platform window (resize borders, drag
    // regions, native-frame caption). Null when the platform has no chrome
    // (headless) — all chrome wiring is skipped in that case.
    private readonly IWindowChrome? _chrome;
    public long ResizeThrottleMs { get; set; } = 16;
    internal Application Application { get; }

    /// <summary>
    /// The native window title, live: assigning re-titles the window the OS already
    /// shows. A borderless window has one too — the task bar and Alt+Tab read it even
    /// though no caption paints it, so an app with custom chrome should still keep it
    /// current (and draw its own caption text from the same string).
    /// </summary>
    public string Title
    {
        get => PlatformWindow.Title;
        set => PlatformWindow.Title = value;
    }

    public IClipboard Clipboard { get; }

    /// <summary>The OS file and folder dialogs, parented to this window.</summary>
    public IFileDialogs FileDialogs { get; }
    public ITextInputHost TextInputHost { get; }
    /// <summary>The element with keyboard focus, or null. Keyboard events route here.</summary>
    public Element? FocusedElement => _focusManager.FocusedElement;

    /// <summary>Open overlay content roots (tooltips, context menus, dialogs), in z-order. For test harnesses.</summary>
    internal IEnumerable<Element> OverlayContents
    {
        get
        {
            foreach (var overlay in _overlays)
                yield return overlay.Content;
        }
    }

    public PerformanceCounters Performance => _perf;
    public FrameTracer Tracer => _tracer;
    public DevToolsOverlay DevTools => _devTools;
    public bool IsOpen => PlatformWindow.IsOpen && !_disposed;
    public WindowStyle Style => PlatformWindow.Style;

    /// <summary>
    /// True for the DevTools inspector's own window. Such a window is not
    /// inspectable (the panel would change as a result of being looked at) and does
    /// not keep <see cref="Application.Run"/> alive on its own.
    /// </summary>
    public bool IsDevToolsWindow { get; internal set; }

    /// <summary>
    /// Raised after this window presents a frame. Used by the DevTools inspector to
    /// re-read the tree it is describing; it fires on the UI thread and must stay
    /// cheap, since it runs once per rendered frame.
    /// </summary>
    internal event Action? FrameRendered;

    // ──── Window management (custom chrome) ────
    public bool IsMaximized => PlatformWindow.IsMaximized;

    /// <summary>The refresh rate of the display this window is on, in Hz; 0 when the platform
    /// does not know. The frame loop renders at most this often.</summary>
    public float RefreshRate => PlatformWindow.RefreshRate;
    public void Minimize() => PlatformWindow.Minimize();
    public void Maximize() => PlatformWindow.Maximize();
    public void Restore() => PlatformWindow.Restore();
    /// <summary>Bring the window to the front (e.g. when a second launch is folded in).</summary>
    public void Raise() => PlatformWindow.Raise();
    public void ToggleMaximize() { if (IsMaximized) Restore(); else Maximize(); }

    /// <summary>
    /// True while the window covers its whole display with no chrome — the borderless
    /// kind games call windowed fullscreen. Not the same as <see cref="IsMaximized"/>,
    /// which fills the work area and leaves the task bar showing.
    /// </summary>
    public bool IsFullscreen => PlatformWindow.IsFullscreen;

    /// <summary>
    /// Takes the window fullscreen or brings it back. The display mode is never changed,
    /// so there is no black flash on the way in and alt-tab stays instant.
    ///
    /// <para>The chrome is the window's, not the app's: an app that draws its own title
    /// bar has to stop drawing it while this is true.</para>
    /// </summary>
    public void SetFullscreen(bool fullscreen) => PlatformWindow.SetFullscreen(fullscreen);

    /// <summary>
    /// Where the window is and whether it is maximized — what to save as the app closes and
    /// give back as it starts, so the window opens where the person left it.
    /// </summary>
    /// <remarks>
    /// <para>Set it before <see cref="Application.Run"/>: the window is hidden until its first
    /// frame, so it opens in place, already maximized if it was, rather than appearing at its
    /// default size and jumping. Read it in a <see cref="Closed"/> handler, where the native
    /// window still exists. Null on set does nothing, so a first run with nothing saved needs
    /// no special case:</para>
    /// <code>
    /// window.Placement = settings.Placement;
    /// window.Closed += () => { settings.Placement = window.Placement; settings.Save(); };
    /// </code>
    /// <para>The values belong to this platform and this machine — see
    /// <see cref="WindowPlacement"/>. Bounds left off screen by a monitor that has gone are
    /// brought back within reach.</para>
    /// </remarks>
    public WindowPlacement? Placement
    {
        get => PlatformWindow.Placement;
        set => PlatformWindow.Placement = value;
    }

    public void ToggleFullscreen() => SetFullscreen(!IsFullscreen);

    /// <summary>Current viewport width in density-independent (layout) units.</summary>
    public float LayoutWidth => PlatformWindow.LayoutWidth;

    /// <summary>Current viewport height in density-independent (layout) units.</summary>
    public float LayoutHeight => PlatformWindow.LayoutHeight;

    /// <summary>
    /// Device pixels per layout unit (1.0 at 100%, 1.5 at 150%). Apps that rasterize
    /// their own artwork need this: a bitmap produced at layout size is resampled on the
    /// way to the screen, and the visible result is a soft or aliased icon.
    /// </summary>
    public float DisplayScale => PlatformWindow.DisplayScale;

    /// <summary>
    /// The operating system's window behind this one: the HWND on Windows, zero on other
    /// platforms and in a headless session. For an element that parents something native
    /// to the window — a browser engine needs a window to create its view under and to
    /// place its own popups against.
    /// </summary>
    public nint NativeHandle => PlatformWindow is IWin32WindowHandle win32 ? win32.Hwnd : 0;

    /// <summary>
    /// The window system this window is on: "wayland", "x11", "windows", "cocoa"; null in a
    /// headless session. A browser engine on Linux needs it before it starts, to draw for the
    /// same display server and, on Wayland, to share GPU buffers with this window.
    /// </summary>
    public string? WindowSystem => PlatformWindow.WindowSystem;

    /// <summary>
    /// Where <paramref name="element"/>'s content box is in this window, in layout units:
    /// <see cref="Element.ScreenContentBox"/> — every ancestor scroll applied — plus the
    /// position of the overlay the element is in, if it is in one: its
    /// <see cref="Element.WindowContentBox"/>. Multiply by <see cref="DisplayScale"/> for
    /// device pixels.
    /// </summary>
    public Rect ContentBoxInWindow(Element element) => element.WindowContentBox;

    /// <summary>
    /// The user's zoom on top of the OS scaling: 1.0 is "as the system says", 1.25 draws
    /// everything a quarter larger and fits correspondingly less in the window. This is the
    /// honest form of "bigger text" for an app laid out in fixed units — growing only the
    /// font leaves every row height, gutter and icon behind, and the result is cramped text
    /// in the same-sized furniture. Clamped by the platform to a usable range; the window
    /// relayouts on the next frame, on the same path a resize takes.
    /// </summary>
    public float UserScale
    {
        get => PlatformWindow.UserScale;
        set => PlatformWindow.UserScale = value;
    }

    /// <summary>
    /// Raised after the window's size changes (during and after an interactive
    /// resize). Fires on the UI thread; handlers see the updated
    /// <see cref="LayoutWidth"/>/<see cref="LayoutHeight"/>. Used by apps whose
    /// layout depends on the viewport size (e.g. responsive chrome) — the reactive
    /// build doesn't re-run on resize on its own.
    /// </summary>
    public event Action? Resized;

    /// <summary>
    /// Sets the smallest size the user may resize the window to, in
    /// density-independent (layout) units. Pass 0 for a dimension to leave it
    /// unconstrained.
    /// </summary>
    public void SetMinimumSize(int width, int height) => PlatformWindow.SetMinimumSize(width, height);

    /// <summary>
    /// Sets the window/taskbar icon from an image file (PNG etc.). On Windows
    /// the exe's embedded .ico usually already covers the taskbar; on Linux this
    /// is how the window manager gets an icon. No-op when the file is missing or
    /// can't be decoded — a broken icon shouldn't stop the app from starting.
    /// </summary>
    public void SetIcon(string path)
    {
        if (!File.Exists(path)) return;
        using var bitmap = SkiaSharp.SKBitmap.Decode(path);
        if (bitmap is null) return;

        // SKColor is unpremultiplied; repack to the byte-order RGBA SDL expects.
        var colors = bitmap.Pixels;
        var rgba = new byte[colors.Length * 4];
        for (int i = 0; i < colors.Length; i++)
        {
            var c = colors[i];
            rgba[i * 4 + 0] = c.Red;
            rgba[i * 4 + 1] = c.Green;
            rgba[i * 4 + 2] = c.Blue;
            rgba[i * 4 + 3] = c.Alpha;
        }
        PlatformWindow.SetIcon(bitmap.Width, bitmap.Height, rgba);
    }

    /// <summary>Thickness (in density-independent units) of the invisible resize border for borderless windows.</summary>
    public int ResizeBorderWidth
    {
        get => _resizeBorderWidth;
        set { _resizeBorderWidth = value; if (_chrome is not null) _chrome.ResizeBorderDip = value; }
    }
    private int _resizeBorderWidth = 6;

    /// <summary>
    /// Which edges a borderless window can be resized from. Corners are enabled
    /// whenever both adjacent edges are. Defaults to all four edges; set it to
    /// <see cref="ResizeEdges.SidesAndBottom"/> to drop top-edge resize when a
    /// custom title bar sits flush against the top. On Windows this drives the
    /// native frame's resize hit-testing; elsewhere the SDL hit-test path.
    /// </summary>
    public ResizeEdges ResizableEdges
    {
        get => _resizableEdges;
        set { _resizableEdges = value; if (_chrome is not null) _chrome.ResizableEdges = value; }
    }
    private ResizeEdges _resizableEdges = ResizeEdges.All;

    public Theme? ActiveTheme => Application.Theme;

    public event Action? Closed;

    /// <summary>
    /// Raised when the OS makes this window the foreground one — the user clicked it, or
    /// alt-tabbed back to it. Fires on the UI thread, once per activation.
    ///
    /// <para>This is the <em>window</em> becoming active, not an element taking keyboard
    /// focus (<see cref="Element.Focus"/>). Apps use it to re-check the world they left
    /// behind: a file another program may have changed while they were in the background,
    /// a folder that may have been written to, a clipboard that may have new contents.</para>
    /// </summary>
    public event Action? Activated;

    internal void RaiseActivated() => Activated?.Invoke();

    // ──── Env-driven tracing (HULTUI_TRACE=<path>) ────
    // When set, recording starts at window creation and the trace is written to
    // the given path when the window is disposed. Lets benchmarks and automation
    // capture a full-session trace without needing the F11 hotkey (which requires
    // keyboard focus). F11 still works and takes precedence: pressing it stops
    // the env-driven recording and saves to the default path.
    private readonly string? _envTracePath = Environment.GetEnvironmentVariable("HULTUI_TRACE");

    internal Window(Application application, IPlatformWindow platformWindow)
    {
        Application = application;
        PlatformWindow = platformWindow;
        _chrome = platformWindow as IWindowChrome;
        _pointerState = new PointerState(application.Platform.TimeProvider);
        if (!string.IsNullOrEmpty(_envTracePath))
            _tracer.StartRecording();
        _layoutEngine = new LayoutEngine(platformWindow.Surface.MeasureContext, _tracer);
        // Expose the surface's measure context as ambient state so Build()-time code
        // can measure text (sizing, budgeting, path collapse) without a layout pass.
        TextMeasure.Context = platformWindow.Surface.MeasureContext;
        Clipboard = platformWindow.Clipboard;
        FileDialogs = platformWindow.FileDialogs;
        TextInputHost = platformWindow.TextInputHost;
        _devTools = new DevToolsOverlay(_perf);
        _a11yObserver = new AccessibilityObserverAdapter(this);
        PlatformWindow.InputReceived += FrameLog.On ? HandleInputEventLogged : HandleInputEvent;

        if (platformWindow.Style == WindowStyle.Borderless && _chrome is { } chrome)
        {
            chrome.ResizeBorderDip = _resizeBorderWidth;
            chrome.ResizableEdges = _resizableEdges;

            if (chrome.UsesWindowsNativeFrame)
            {
                // Native Windows frame: Windows resizes/moves the window itself
                // via WM_NCHITTEST; it only needs to know the drag regions and the
                // maximize button (so it can show the Snap Layouts flyout).
                chrome.IsWindowDragPoint = IsWindowDragRegion;
                chrome.IsWindowMaximizePoint = IsWindowMaximizeRegion;
                chrome.GetWindowMaximizeButtonBounds = () => FindMaximizeButton()?.Layout.AbsoluteBorderBox;
                chrome.OnMaximizeButtonHover = SetMaximizeButtonHover;
            }
            else
                chrome.HitTestHandler = HandleWindowHitTest;
        }

        _focusManager.FocusChanged += OnFocusChanged;

        // Platform accessibility bridge: inactive until an AT probes. On
        // Windows this wires an SDL message hook for WM_GETOBJECT; on Linux
        // it attempts a connection to the AT-SPI bus. Either way it falls
        // back to NullAccessibilityBridge if the platform can't be reached.
        _accessibilityBridge = AccessibilityBridgeFactory.Create(this);

        // One-line graphics diagnostic: the renderer/driver/vsync config the
        // platform *actually* gave us. Cross-OS perf comparisons are meaningless
        // without it — a llvmpipe fallback, an unrequested MSAA framebuffer, or
        // a silently vsync'd swap all read as "slow rendering" in a trace.
        // Stamped into env-driven traces for the same reason (null headless).
        if (platformWindow.DescribeGraphics() is { } gfx)
        {
            string osDesc = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
            Console.WriteLine($"[HultUI] gfx: {gfx} os=\"{osDesc}\"");
            if (_tracer.IsRecording)
                _tracer.Annotate("Graphics", $"{gfx} os=\"{osDesc}\"");
        }
    }

    internal void SetUiaHostBridge(UiaHostBridge host) => _uiaHost = host;

    /// <summary>
    /// Registers the platform-neutral action router whose queued invoke/toggle/
    /// select/set-value actions are drained on the UI thread each frame. Set by
    /// the accessibility factory on every platform that has an active bridge.
    /// </summary>
    internal void SetActionRouter(AccessibilityActionRouter router) => _actionRouter = router;

    /// <summary>
    /// Builds a fresh accessibility-tree snapshot of the current content, or
    /// null when no root is set. This is the same tree assistive technologies
    /// see; the headless query API reads it on demand, independent of the
    /// per-frame build that only runs while a bridge is active.
    /// </summary>
    internal AccessibilityNode? BuildAccessibilityTree()
        => _root is null ? null : BuildAccessibilityTreeWithOverlays(_root);

    /// <summary>
    /// The window's content, plus whatever is open over it.
    ///
    /// <para>Overlays live outside the root element tree, so a snapshot of the root alone
    /// leaves out every context menu, dropdown list and dialog — the parts of a window a
    /// screen reader most needs when one is open, because they are also the parts that
    /// have just taken the keyboard. Tooltips stay out: they are not hit-testable, they
    /// belong to the element they describe, and announcing one as a sibling of the window
    /// would read it twice.</para>
    /// </summary>
    private AccessibilityNode BuildAccessibilityTreeWithOverlays(Element root)
    {
        var tree = AccessibilityTreeBuilder.Build(root, _focusManager);
        for (int i = 0; i < _overlays.Count; i++)
        {
            var overlay = _overlays[i];
            if (!overlay.IsHitTestable) continue;
            tree.AddChild(AccessibilityTreeBuilder.Build(overlay.Content, _focusManager, overlay.X, overlay.Y));
        }
        return tree;
    }

    /// <summary>
    /// Replaces the platform accessibility bridge. Used by headless sessions to
    /// install a capturing bridge that exercises the per-frame build/diff path.
    /// </summary>
    internal void SetAccessibilityBridge(IAccessibilityBridge bridge)
        => _accessibilityBridge = bridge;

    /// <summary>Moves keyboard focus programmatically (same path as click/Tab focus).</summary>
    internal void SetFocus(Element? element) => _focusManager.SetFocus(element);

    /// <summary>
    /// Moves keyboard focus one step through the tree, exactly as Tab and Shift+Tab do
    /// — the focus ring is shown, and a newly focused text element is given the
    /// clipboard. Returns false when focus did not move.
    /// </summary>
    /// <param name="forward">Tab order direction; false is Shift+Tab.</param>
    /// <param name="wrap">
    /// Whether the end of the tree returns to the other end. True is a window's
    /// behaviour, and the default, because Tab has nowhere else to go. A host embedding
    /// this tree in its own tab order passes false and reads the return value: false
    /// means the tree is exhausted and focus should leave for whatever the host has
    /// next.
    /// </param>
    public bool MoveFocus(bool forward, bool wrap = true)
    {
        if (FocusScope() is not { } scope) return false;
        // An overlay is a closed loop: there is no host tab order to hand over to from inside
        // a dialog, so its end always returns to its start.
        if (scope != _root) wrap = true;

        bool moved = forward
            ? _focusManager.MoveFocusNext(scope, wrap)
            : _focusManager.MoveFocusPrevious(scope, wrap);
        if (!moved) return false;

        _focusFromKeyboard = true;

        // Text elements reach the clipboard through the window, so hand it over as they
        // gain focus rather than making every one of them find it.
        switch (_focusManager.FocusedElement)
        {
            case TextInputElement input: input.Clipboard = Clipboard; break;
            case TextAreaElement area: area.Clipboard = Clipboard; break;
            case ITextServicesClient client: client.Clipboard = Clipboard; break;
            case TextElement { Selectable: true } label: label.Clipboard = Clipboard; break;
        }
        return true;
    }

    /// <summary>
    /// Gives focus to the first (or last) focusable element, for a host handing keyboard
    /// focus into an embedded tree. Returns false when nothing in the tree is focusable.
    /// </summary>
    public bool FocusEdge(bool first)
    {
        if (_root is null) return false;
        _focusManager.SetFocus(null);
        return MoveFocus(forward: first, wrap: true);
    }

    /// <summary>Drops keyboard focus, so no element keeps a caret or focus ring — what a
    /// host calls when its control loses focus to something else in the form.</summary>
    public void ClearFocus() => _focusManager.ClearFocus();

    /// <summary>
    /// Walks the element tree looking for an element with the given
    /// <see cref="ElementId"/>. Used by the accessibility host bridge to
    /// route UIA actions back to the originating element. Returns null if
    /// the element has been removed.
    /// </summary>
    /// <summary>
    /// The element with this id, anywhere in the window — including inside an open
    /// overlay. Accessibility actions arrive as an id and nothing else, and what assistive
    /// tech acts on is usually whatever just opened over the window: a menu item, a field
    /// in a dialog. Searching the root alone made every one of those a silent no-op.
    /// </summary>
    internal Element? FindElement(ElementId id)
    {
        if (_root is not null && FindInTree(_root, id) is { } found) return found;
        for (int i = 0; i < _overlays.Count; i++)
            if (FindInTree(_overlays[i].Content, id) is { } inOverlay) return inOverlay;
        return null;
    }

    private static Element? FindInTree(Element e, ElementId id)
    {
        if (e.Id == id) return e;
        var children = e.Children;   // by index: foreach over the interface boxes an enumerator
        for (int i = 0; i < children.Count; i++)
        {
            var found = FindInTree(children[i], id);
            if (found is not null) return found;
        }
        return null;
    }

    private void OnFocusChanged(Element? element)
    {
        if (element is TextInputElement textInput)
        {
            textInput.Clipboard = Clipboard;
            textInput.TextInputHost = TextInputHost;
            TextInputHost.StartTextInput(textInput.InputKind);
        }
        else if (element is TextAreaElement textArea)
        {
            textArea.Clipboard = Clipboard;
            textArea.TextInputHost = TextInputHost;
            TextInputHost.StartTextInput();
        }
        else if (element is ITextServicesClient client)
        {
            client.Clipboard = Clipboard;
            client.TextInputHost = TextInputHost;
            TextInputHost.StartTextInput();
        }
        else if (element is { WantsTextInput: true })
        {
            TextInputHost.StartTextInput();
        }
        else
        {
            TextInputHost.StopTextInput();
        }

        if (_accessibilityBridge.IsActive)
        {
            var (previous, focused) = MoveA11yFocus(element);
            _accessibilityBridge.NotifyFocusChanged(focused, previous);
        }
    }

    /// <summary>
    /// A focus move rebuilds no accessibility tree, so the applied one still marks the element
    /// that had the focus when it was built. Marks the move on it in place instead: the node
    /// that had the focus and the one that has it now, found in one walk. A reader asking
    /// either is then answered as of now, and the next rebuild's diff, which compares against
    /// this tree, does not report the move again, late. Returns both nodes; null where the
    /// element has none, or the tree marked no node. An element with no node yet is remembered
    /// (<see cref="NotifyPendingA11yFocus"/>).
    /// </summary>
    private (AccessibilityNode? Previous, AccessibilityNode? Focused) MoveA11yFocus(Element? element)
    {
        var tree = _previousA11yTree;
        var previousId = _a11yFocusId;
        _a11yFocusId = element?.Id;
        _a11yPendingFocusId = null;
        if (tree is null)
        {
            _a11yPendingFocusId = element?.Id;
            return (null, null);
        }

        AccessibilityNode? previous = null, focused = null;
        FindNodes(tree, previousId, element?.Id, ref previous, ref focused);
        if (previous is not null && (ReferenceEquals(previous, focused) || !previous.IsFocused))
            previous = null;
        if (previous is not null) previous.IsFocused = false;
        if (focused is not null) focused.IsFocused = true;
        else if (element is not null) _a11yPendingFocusId = element.Id;
        return (previous, focused);
    }

    /// <summary>
    /// After a rebuild: tells the bridge of a focus move onto an element that had no node when it
    /// moved, now that the tree has one and marks it focused. The move itself told the bridge the
    /// focus went nowhere (and the element that lost it), so a reader got no focus event and
    /// UIA's GetFocus answered nothing until the next move. Nothing when the focus has moved on,
    /// or the element still has no node (it is not one a reader sees).
    /// </summary>
    private void NotifyPendingA11yFocus(AccessibilityNode tree)
    {
        if (_a11yPendingFocusId is not { } id) return;
        _a11yPendingFocusId = null;
        if (id != _a11yFocusId) return;
        if (FindById(tree, id) is { IsFocused: true } node)
            _accessibilityBridge.NotifyFocusChanged(node, previous: null);
    }

    /// <summary>
    /// A move of a list's selection, told without a rebuild: marks the selected state of the item
    /// nodes it changed on the applied tree in place, and hands the bridge those changes, which
    /// are what the next rebuild's diff would have reported (0912a5e made every move force that
    /// rebuild: a whole tree built and diffed for two states, on every key of a walk). The next
    /// diff compares against the marked tree, so it does not report them again. Whatever else the
    /// move changed is told on its own schedule: a scroll by the coalesced layout refresh, as it
    /// always was. False when a list cannot be patched, and the tree must be rebuilt: no applied
    /// tree, a list with no node in it (mounted since), or a list whose items are not its node's
    /// (<see cref="AccessibilityTreeBuilder.ItemSelectionChanges"/>). Nothing is marked then.
    /// </summary>
    private bool TryPatchA11ySelection()
    {
        if (_previousA11yTree is not { } tree) return false;
        var lists = _a11ySelectionLists;
        var changes = _a11ySelectionChanges;
        try
        {
            // In tree order, as a diff lists them.
            FindMovedLists(tree);
            if (lists.Count < _a11ySelectionMoved.Count) return false;
            foreach (var (node, source) in lists)
                if (!AccessibilityTreeBuilder.ItemSelectionChanges(node, source, changes)) return false;
            if (changes.Count == 0) return true;   // moved back, or served on demand: nothing to tell
            var patch = new TreeDiff();
            foreach (var change in changes)
            {
                change.Node.IsSelected = change.Node.IsSelected != true;
                patch.PropertyChanged.Add(change);
            }
            _accessibilityBridge.ApplyPatchedProperties(patch, tree);
            return true;
        }
        finally
        {
            lists.Clear();
            changes.Clear();
        }
    }

    /// <summary>Adds the nodes of the lists whose selection moved to <see cref="_a11ySelectionLists"/>,
    /// in tree order. Stops once it has them all, and does not walk into a list's items.</summary>
    private bool FindMovedLists(AccessibilityNode node)
    {
        var moved = _a11ySelectionMoved;
        for (int i = 0; i < moved.Count; i++)
        {
            if (moved[i].Id != node.ElementId) continue;
            _a11ySelectionLists.Add((node, (IAccessibleItemSource)moved[i]));
            return _a11ySelectionLists.Count == moved.Count;
        }
        // By index: foreach over the interface boxes an enumerator per node.
        var children = node.Children;
        for (int i = 0; i < children.Count; i++)
            if (FindMovedLists(children[i])) return true;
        return false;
    }

    /// <summary>Finds the nodes of two elements in one walk, which stops once it has both.</summary>
    private static bool FindNodes(AccessibilityNode node, ElementId? a, ElementId? b,
        ref AccessibilityNode? foundA, ref AccessibilityNode? foundB)
    {
        if (a is { } ia && node.ElementId == ia) foundA = node;
        if (b is { } ib && node.ElementId == ib) foundB = node;
        if ((a is null || foundA is not null) && (b is null || foundB is not null)) return true;
        // By index: foreach over the interface boxes an enumerator per node.
        var children = node.Children;
        for (int i = 0; i < children.Count; i++)
            if (FindNodes(children[i], a, b, ref foundA, ref foundB)) return true;
        return false;
    }

    /// <summary>
    /// Locates the AccessibilityNode matching the given element in the last
    /// applied tree snapshot. Returns null if the element isn't represented
    /// (e.g. it was flattened out as a decorative container) or no snapshot
    /// has been built yet.
    /// </summary>
    private AccessibilityNode? FindA11yNode(Element? element)
    {
        if (element is null || _previousA11yTree is null) return null;
        return FindById(_previousA11yTree, element.Id);
    }

    private static AccessibilityNode? FindById(AccessibilityNode node, ElementId id)
    {
        if (node.ElementId == id) return node;
        // By index: this walks the whole accessibility tree on every focus move while a
        // client listens, and foreach over the interface boxes an enumerator per node.
        var children = node.Children;
        for (int i = 0; i < children.Count; i++)
        {
            var found = FindById(children[i], id);
            if (found is not null) return found;
        }
        return null;
    }

    public void SetRoot(Element root)
    {
        // The old tree is gone; anything held from it is a reference to a discarded element.
        // Releasing its host is what lets an element still animating in it (a looping
        // progress sweep) notice on its next tick — see Element.IsHosted.
        if (_root is not null && !ReferenceEquals(_root, root))
            _root.OverlayHost = null;
        _root = root;
        _maxButtonCache = null;
        SetScrollbarHover(null, default);
        root.OverlayHost = this;
        root.FocusManager = _focusManager;
        root.AccessibilityObserver = _a11yObserver;
        _previousA11yTree = null;
    }

    internal Element? Root => _root;

    /// <summary>
    /// True when the element is currently part of this window's live tree — reachable
    /// from the root by walking <see cref="Element.Parent"/>. A <see cref="Core.Reactivity.Ref{T}"/>
    /// keeps pointing at an element after a rebuild unmounts it, so a non-null ref is
    /// not proof the element is on screen; anything holding work for an element across
    /// frames (a deferred scroll, a focus) should check this before spending it.
    /// </summary>
    public bool IsInTree(Element element) => IsAttachedToRoot(element);

    internal void MarkStyleDirty()
    {
        _root?.MarkStyleDirty();
    }

    /// <summary>
    /// Registers an action to run exactly once, immediately after the next frame
    /// is presented (after present + buffer swap). Runs on the UI thread. This
    /// forces a frame to render even if nothing else is dirty, so the callback is
    /// guaranteed to fire on the following loop iteration — or, for a minimized window,
    /// which is not drawn, on the first frame after it is restored. Useful for measuring
    /// how long a state change takes to reach the screen, or for reacting once
    /// content is actually painted.
    /// </summary>
    public void RequestPostFrameCallback(Action callback)
    {
        _postFrameCallbacks.Add(callback ?? throw new ArgumentNullException(nameof(callback)));
        // From a callback the loop's wait dispatched, nothing else would end the wait for it.
        UIThread.WakeIfParked();
    }

    /// <summary>
    /// Runs <paramref name="callback"/> at the start of the next frame, before style and layout,
    /// and asks for that frame. A callback already waiting is not added twice: a drag asks on every
    /// pointer move and is applied once per frame (<see cref="PointerFollow"/>).
    /// </summary>
    void IPreFrameHost.RunBeforeNextFrame(Action callback)
    {
        foreach (var waiting in _preFrameCallbacks)
            if (ReferenceEquals(waiting, callback)) return;
        _preFrameCallbacks.Add(callback);
        // From a callback the loop's wait dispatched, nothing else would end the wait for it.
        UIThread.WakeIfParked();
    }

    /// <summary>The <see cref="IPreFrameHost.RunBeforeNextFrame"/> work, in one batch, as an
    /// event's handlers run: what it writes rebuilds once when it is done.</summary>
    private void RunPreFrameCallbacks()
    {
        if (_preFrameCallbacks.Count == 0) return;
        (_preFrameCallbacks, _preFrameRunning) = (_preFrameRunning, _preFrameCallbacks);
        bool batch = ReactiveRuntime.AutoBatch;
        if (batch) ReactiveRuntime.BeginBatch();
        try
        {
            for (int i = 0; i < _preFrameRunning.Count; i++)
                _preFrameRunning[i]();
        }
        finally
        {
            _preFrameRunning.Clear();
            if (batch) ReactiveRuntime.EndBatch();
        }
    }

    /// <summary>DevTools' continuous-redraw mode (F12 twice): render every iteration, unpaced.</summary>
    internal bool ContinuousRedraw => _devTools.ContinuousRedraw;

    internal bool NeedsRender => NeedsDraw || NeedsAccessibilityFrame;

    /// <summary>
    /// Whether the window has something to draw: what <see cref="NeedsRender"/> asks a frame for,
    /// less what only accessibility asks one for (<see cref="NeedsAccessibilityFrame"/>), which
    /// needs the frame's accessibility phase and nothing it draws.
    /// </summary>
    internal bool NeedsDraw => (_root is not null && _root.DirtyFlags != DirtyFlags.None)
        // DevTools continuous-redraw mode (F12 second press): render every
        // iteration so the HUD shows raw render throughput.
        || _devTools.ContinuousRedraw
        // Overlay trees are separate roots, so their dirty flags don't propagate
        // to _root. Show/close/move already mark _root dirty; this catches
        // mutations inside an open overlay (hover highlights in a context menu).
        // A static overlay (a tooltip) costs nothing once painted.
        || AnyOverlayContentDirty()
        || _postFrameCallbacks.Count > 0
        // A drag or a hover that follows the pointer once a frame has a position to apply.
        || _preFrameCallbacks.Count > 0
        // An autoscroll moves the view with no input arriving at all, so it has to ask for
        // the frames itself — otherwise holding the pointer still would stop the scroll.
        || _autoScrollTarget is not null
        // An accessibility action (invoke/toggle/set-value/focus) queued from an
        // AT thread must run even when the app is idle; draining happens in the
        // render pass, so a pending action forces a frame. Platform-neutral: the
        // router is set on any platform with an active bridge. What it changes is drawn.
        || (_actionRouter?.HasPendingActions ?? false);

    /// <summary>
    /// A frame that only accessibility asks for: a tree to build when a client attaches, a change
    /// a reader must hear that draws nothing, a coalesced refresh coming due. Such a frame runs
    /// the accessibility phase and draws nothing (<see cref="RenderFrame"/>).
    /// </summary>
    private bool NeedsAccessibilityFrame => (
        // Build once when an assistive tech attaches, so the accessibility tree
        // gets built even if the app is otherwise idle (the build runs in the
        // frame). After that frame _a11yWasActive is set and this clears.
        !_a11yWasActive
        // An a11y-visible property changed (e.g. a label's text) with no visual
        // change of its own; the tree refresh happens in the frame.
        || _a11yPropertyDirty
        // A list's selection moved: the frame tells it (the list has paint dirt of its own anyway).
        || _a11ySelectionMoved.Count > 0
        // A coalesced layout-driven a11y refresh is due — force the trailing frame
        // so the last resize/scroll position reaches the tree once the drag stops
        // (during the drag, layout dirty already keeps frames coming).
        || (_a11yLayoutRefreshPending && A11yClockMs - _lastA11yRebuildMs >= A11yCoalesceMs)
        // The same for a range value's trailing edge: the value it came to rest at.
        || (_a11yRangeRefreshPending && A11yClockMs - _lastA11yRangeMs >= A11yRangeCoalesceMs))
        // Asked last: on Windows it asks UI Automation whether a client listens.
        && _accessibilityBridge.IsActive;

    /// <summary>
    /// For tests: told, at the end of a frame whose layout pass would have refreshed the
    /// accessibility tree before bounds were refreshed only for what moved (plan 17, round 3,
    /// step 15) and that leaves nothing pending, how a tree built afresh then differs from the
    /// applied one. The shadow that proves the skipped refreshes would have told nothing. Null
    /// (the default) builds nothing.
    /// </summary>
    internal Action<TreeDiff>? SkippedA11yRefreshAudit { get; set; }

    /// <summary>
    /// Whether a box the accessibility tree reads moved since the last look, which takes them as
    /// they are: the window's elements and those of the overlays in the tree (a menu, a dialog).
    /// </summary>
    private bool TakeMovedA11yBoxes()
    {
        bool moved = _root!.TakeMovedBoxes();
        for (int i = 0; i < _overlays.Count; i++)
            if (_overlays[i].IsHitTestable && _overlays[i].Content.TakeMovedBoxes())
                moved = true;
        return moved;
    }

    private void AuditSkippedA11yRefresh(bool a11yActive, bool layoutWasDirty, bool rebuilt)
    {
        if (SkippedA11yRefreshAudit is not { } audit || !a11yActive || !layoutWasDirty || rebuilt
            || _a11yLayoutRefreshPending || _a11yRangeRefreshPending || _a11yPropertyDirty
            || _a11ySelectionMoved.Count > 0 || _previousA11yTree is null || _root is null)
            return;
        audit(AccessibilityTreeDiffer.Diff(_previousA11yTree, BuildAccessibilityTreeWithOverlays(_root)));
    }

    /// <summary>
    /// Folds into <paramref name="at"/> the earliest moment, on the platform's clock, this window
    /// owes a frame while it needs none now: the trailing edge of a coalesced accessibility
    /// refresh (layout bounds, a range's value), and a resize the throttle held back. The frame
    /// loop waits until then (<c>Application.RunUntil</c>), so each must be named here; it no
    /// longer comes round every 16 ms to find them due.
    /// </summary>
    internal void NextDeadline(ref long at, ref IdleDeadline deadline)
    {
        var clock = Application.Platform.TimeProvider;
        long frequency = clock.TimestampFrequency;
        // Only while a client listens: otherwise the frame that clears them never comes, and
        // the loop would wake for nothing.
        if ((_a11yLayoutRefreshPending || _a11yRangeRefreshPending) && _accessibilityBridge.IsActive)
        {
            if (_a11yLayoutRefreshPending)
                Application.Earliest(ref at, ref deadline, (_lastA11yRebuildMs + A11yCoalesceMs) * frequency / 1000,
                    IdleDeadline.A11yLayout);
            if (_a11yRangeRefreshPending)
                Application.Earliest(ref at, ref deadline, (_lastA11yRangeMs + A11yRangeCoalesceMs) * frequency / 1000,
                    IdleDeadline.A11yRange);
        }
        // Throttled on Environment.TickCount64 (ProcessPendingResizes): how long is left, from now.
        // Only reaches here for a minimized window; a shown one is dirty until the resize runs.
        if (PlatformWindow.ResizePending)
        {
            long leftMs = Math.Max(0, PlatformWindow.LastResizeRenderTick + ResizeThrottleMs - Environment.TickCount64);
            Application.Earliest(ref at, ref deadline, clock.GetTimestamp() + leftMs * frequency / 1000,
                IdleDeadline.Resize);
        }
    }

    private bool AnyOverlayContentDirty()
    {
        for (int i = 0; i < _overlays.Count; i++)
            if (_overlays[i].Content.DirtyFlags != DirtyFlags.None) return true;
        return false;
    }

    internal void OnWindowResized()
    {
        if (_root is null) return;
        _root.MarkLayoutDirty();
        _root.MarkPaintDirty();
        Resized?.Invoke();
    }

    private readonly List<IGpuImageSource> _undrawnSources = [];

    /// <summary>How many times this window has looked through its tree for GPU sources to serve
    /// while it was not drawn. Tests read it: the walk is what a window with none is spared.</summary>
    internal int UndrawnSourceWalks { get; private set; }

    /// <summary>
    /// Serves the GPU image sources of a window that is not being drawn (minimized), through
    /// <see cref="IGpuImageSource.ServiceUndrawn"/>. Such a source can be a producer that
    /// stops without its draws: mpv's render thread is this one, and its decoder waits up to
    /// 200 ms for every frame to be taken. Measured in Reel minimized: every frame dropped after
    /// that wait, and the next file in the folder never started (no render context until a
    /// draw), so the sound stopped at the end of the file until the window was restored.
    /// </summary>
    private void ServiceUndrawnSources()
    {
        // The walk visits every element, on every pass a minimized window is dirty, and most
        // windows have no GPU source for it to find: a looping tween over 2,015 elements was
        // 123,000 visits a second. Only a live sink, or an image holding a source of another
        // kind, can be found.
        if (!GpuFrameSink.MayNeedUndrawnService) return;
        UndrawnSourceWalks++;
        CollectGpuSources(_root!, _undrawnSources);
        for (int i = 0; i < _overlays.Count; i++)
            CollectGpuSources(_overlays[i].Content, _undrawnSources);
        if (_undrawnSources.Count == 0) return;

        // Where foreign GL cannot run, the source settles on its other path, as a draw would.
        var surface = PlatformWindow.Surface as IExternalGLSurface;
        if (surface is not null && !surface.SupportsExternalGL) surface = null;
        if (surface is not null) PlatformWindow.MakeCurrent();
        try
        {
            foreach (var source in _undrawnSources) source.ServiceUndrawn(surface);
        }
        finally
        {
            _undrawnSources.Clear();
        }
    }

    private static void CollectGpuSources(Element element, List<IGpuImageSource> into)
    {
        if (element is ImageElement { Source: IGpuImageSource source } && !into.Contains(source))
            into.Add(source);
        var children = element.Children;
        for (int i = 0; i < children.Count; i++) CollectGpuSources(children[i], into);
    }

    /// <param name="evenIfMinimized">Draw a minimized window too, as a forced frame does.</param>
    /// <param name="drawClean">Draw even with nothing dirty: a live resize holds the loop, and a
    /// video's next frame, posted and not yet run, is taken from its decoder by the draw.</param>
    /// <returns>Whether a frame was drawn and presented.</returns>
    internal bool RenderFrame(bool evenIfMinimized = false, bool drawClean = false)
    {
        if (_root is null) return false;

        // Drain queued UI-thread actions from the accessibility action router.
        // AT callbacks (UIA worker threads, the AT-SPI D-Bus receive thread) run
        // off-thread and enqueue here; this is where they run against the tree.
        _actionRouter?.DrainPendingActions();

        _tracer.MarkFrame();
        using var _frameTrace = _tracer.Begin("Frame", "Frame");

        // Before style and layout, so this frame paints where the autoscroll has reached
        // rather than a frame behind it.
        TickAutoScroll();

        // The same for a splitter, a pan or a slider following the pointer: the moves since the
        // last frame recorded where it is, and this applies the latest, once (PointerFollow).
        RunPreFrameCallbacks();

        // Safety net: run any rebuild that was deferred because it was requested during
        // a reconcile (e.g. MarkDirty from a BlurEvent when a focused element was
        // removed). A rebuild triggered via ForceRebuild drains itself, but one whose
        // outer reconcile was signal-driven has no such caller — this catches it before
        // style/layout so its effect is visible this frame.
        TreeDiffer.DrainDeferred();

        // Whether this frame has anything to draw. A frame only accessibility asked for (a
        // coalesced refresh coming due between keys of a walk, a client attaching) runs the
        // accessibility phase and stops there, as a minimized window's does: drawing it painted
        // the whole window again for nothing.
        bool draws = evenIfMinimized || drawClean || NeedsDraw;

        var theme = Application.Theme;

        // Capture the layout-dirty state now: the layout pass below clears
        // DirtyFlags.Layout per element, so by the accessibility phase it's gone.
        // A layout change (window/pane resize) moves elements without adding or
        // removing any, and their bounding rects must be re-snapshotted for UIA.
        // The flag tests in this method are written out rather than HasFlag: optimized code
        // treats HasFlag as an intrinsic, but unoptimized code (tier 0, which the first frames
        // of a window run) boxes both operands, 192 bytes a frame here.
        bool layoutWasDirty = (_root.DirtyFlags & DirtyFlags.Layout) != 0;
        bool styleWasDirty = (_root.DirtyFlags & DirtyFlags.Style) != 0;

        // Style resolution (only if any element has style changes)
        _perf.BeginStyle();
        using (_tracer.Begin("Phase", "Style"))
        {
            if ((_root.DirtyFlags & DirtyFlags.Style) != 0)
            {
                StyleResolver.Resolve(_root, theme);
                ClearFlags(_root, DirtyFlags.Style | DirtyFlags.StyleSubtree);
                ReapplyHoverCursor();
            }
        }
        _perf.EndStyle();

        // Layout (only if any element has layout changes)
        _perf.BeginLayout();
        using (_tracer.Begin("Phase", "Layout"))
        {
            if ((_root.DirtyFlags & DirtyFlags.Layout) != 0)
                _layoutEngine.PerformLayout(_root, PlatformWindow.LayoutWidth, PlatformWindow.LayoutHeight);
        }
        _perf.EndLayout();

        // The maximize button's laid-out position is now known; keep the Windows
        // caption region tall enough to contain it (for Snap Layouts when restored).
        UpdateCaptionHeight();

        // Accessibility: rebuild + diff when the tree changed and something is
        // actually listening. The Null bridge's IsActive is false, so this is a
        // single boolean read for apps with no AT attached. The first frame after
        // an assistive tech attaches also forces a full build (nothing is dirty
        // by then, but the client has nothing yet). A layout change is included
        // as well as a structural (Children) one: resizing the window or a pane
        // moves elements without adding/removing any, and their bounding rects
        // must be re-snapshotted so UIA reports current positions (the differ
        // already emits BoundingRect changes). This rebuild only runs while an AT
        // is attached, so idle apps pay nothing.
        bool a11yActive = _accessibilityBridge.IsActive;
        // Structural (Children), property/focus, and first-attach changes refresh
        // immediately. Layout-only changes are coalesced (see A11yCoalesceMs): a
        // continuous resize/scroll otherwise rebuilds the whole tree every frame.
        bool a11yImmediate = a11yActive && (!_a11yWasActive || _a11yPropertyDirty
            || (_root.DirtyFlags & DirtyFlags.Children) != 0);
        // Bounds are refreshed when the layout moved or resized an element, not whenever it ran: a
        // component rebuilt on every frame of a tween patches its buttons, which mark their layout,
        // and a reader listening cost a whole tree every 100 ms for an empty diff (26 trees per 150
        // frames, 8.7 KB a frame, with 300 rows). The look takes the boxes as they are; one that
        // moved keeps the refresh pending until it comes.
        if (a11yActive && TakeMovedA11yBoxes())
            _a11yLayoutRefreshPending = true;
        long nowMs = A11yClockMs;
        bool a11yLayoutDue = a11yActive && _a11yLayoutRefreshPending
            && nowMs - _lastA11yRebuildMs >= A11yCoalesceMs;
        bool a11yRangeDue = a11yActive && _a11yRangeRefreshPending
            && nowMs - _lastA11yRangeMs >= A11yRangeCoalesceMs;
        bool a11yRebuild = a11yImmediate || a11yLayoutDue || a11yRangeDue;
        // A list's selection moved and nothing else asks for a rebuild: the two items are marked
        // on the applied tree and told from it. A rebuild in this frame tells them in its diff.
        if (a11yActive && !a11yRebuild && _a11ySelectionMoved.Count > 0)
        {
            using (_tracer.Begin("Phase", "A11y"))
                a11yRebuild = !TryPatchA11ySelection();
        }

        if (a11yRebuild)
        {
            // Whatever the rebuild was for, it tells a range's value as it is now.
            if (_a11yRangeRefreshPending)
            {
                _a11yRangeRefreshPending = false;
                _lastA11yRangeMs = nowMs;
            }
            using (_tracer.Begin("Phase", "A11y"))
            {
                var newTree = BuildAccessibilityTreeWithOverlays(_root);
                AccessibilityTreesBuilt++;
                var diff = AccessibilityTreeDiffer.Diff(_previousA11yTree, newTree);
                if (!diff.IsEmpty)
                    _accessibilityBridge.ApplyChanges(diff, newTree);
                _previousA11yTree = newTree;
                _a11yFocusId = _focusManager.FocusedElement?.Id; // what the builder marked
                NotifyPendingA11yFocus(newTree);
            }
            _a11yLayoutRefreshPending = false;
            _lastA11yRebuildMs = nowMs;
        }
        else if (!a11yActive && _a11yWasActive)
        {
            // Client detached — drop the snapshot so the next attach rebuilds fresh.
            _previousA11yTree = null;
            _a11yPendingFocusId = null;
            _a11yLayoutRefreshPending = false;
            _a11yRangeRefreshPending = false;
        }
        if (_announcements.Count > 0)
        {
            // After the rebuild, so an element that announces as it appears has its node.
            if (a11yActive)
                foreach (var (source, text, politeness) in _announcements)
                    _accessibilityBridge.Announce(FindA11yNode(source), text, politeness);
            _announcements.Clear();
        }
        _a11yPropertyDirty = false;
        _a11ySelectionMoved.Clear();
        _a11yWasActive = a11yActive;
        // Note: the Children flag is cleared with everything else by ClearAllFlags at the end
        // of the frame, below, or where a minimized window stops.

        // Nothing to draw: the tree is built and told, and the window shows what it showed.
        // Junction's History walked a key every 60 ms: a refresh came due between keys on every
        // other key, and each drew the window again, 0.5 frames a key on top of the keys' own.
        if (!draws)
        {
            AuditSkippedA11yRefresh(a11yActive, layoutWasDirty, a11yRebuild);
            return false;
        }

        // Keep the maximize button's non-client hover highlight alive across rebuilds.
        ReassertMaximizeButtonHover();

        // Damage instrumentation: measured here, after layout (bounds are current)
        // and before the paint flags are consumed. One null-check when nothing is
        // attached and no trace is recording.
        CollectDamage(layoutWasDirty, styleWasDirty);

        // Render (user content only — DevTools excluded from timing). The Render
        // span includes BeginFrame's full-surface clear: that clear is part of
        // producing this frame's pixels, not a separate phase, so folding it in
        // keeps the ledger honest without inventing a row for it.
        // A backend that has to acquire a target before it can be drawn on says so here.
        // Skipping is normal — a resize in flight, or a window with no area — and the next
        // frame will find one.
        //
        // A minimized window stops here too. Nothing would show its frame; on Windows a Vulkan
        // surface has no area to draw into anyway; and GL drew every frame of an animation or
        // a video nobody could see. Everything above has run — assistive technology's actions,
        // style, layout, the accessibility tree — and the paint dirt is kept, so the first frame
        // after the restore draws it. A video still has to be served (ServiceUndrawnSources).
        if (!evenIfMinimized && PlatformWindow.IsMinimized)
        {
            // A change that was only structure, style or layout has no paint dirt of its own,
            // and none of it is on screen: the root takes some for the restore. The structural
            // flag goes, as at the end of a drawn frame. Its one reader, the accessibility check
            // above, has had its answer; left set, it rebuilt the whole tree on every pass of the
            // loop until the restore — one element added to 2,016 while minimized, a UIA client
            // listening: a rebuild on each of 61 passes a second, 10.8 MB/s.
            if (layoutWasDirty || styleWasDirty || (_root.DirtyFlags & DirtyFlags.Children) != 0)
                _root.MarkPaintDirty();
            ClearFlags(_root, DirtyFlags.Children);
            ServiceUndrawnSources();
            AuditSkippedA11yRefresh(a11yActive, layoutWasDirty, a11yRebuild);
            return false;
        }
        if (!PlatformWindow.Surface.BeginFrame())
        {
            AuditSkippedA11yRefresh(a11yActive, layoutWasDirty, a11yRebuild);
            return false;
        }

        var ctx = PlatformWindow.Surface.RenderContext;
        _perf.BeginRender();
        using (_tracer.Begin("Phase", "Render"))
        {
            ctx.BeginFrame(PlatformWindow.LayoutWidth, PlatformWindow.LayoutHeight, PlatformWindow.DisplayScale, theme.Background);
            ElementRenderer.RenderTree(_root, ctx, _tracer);
        }
        _perf.EndRender();

        // Drag ghost overlay
        if (_isDragging && !_isExternalDrag)
            RenderDragGhost(ctx);

        if (_autoScrollTarget is not null)
            RenderAutoScrollAnchor(ctx);

        // Overlay layer (tooltips, context menus, popups)
        RenderOverlays(ctx);

        // After the overlays: a focused control can live inside one, and the ring belongs on
        // top of everything either way — it is drawn outside the control's own bounds, so a
        // sibling painted later would otherwise cover half of it.
        RenderFocusVisual(ctx);

        // DevTools overlay (second pass). This is pure observer tax — the element
        // count is a full tree walk and the HUD records its own draw commands —
        // so time it as "Overlay" to quantify how much the measurement perturbs
        // the frame. Non-zero only while the HUD is up (see PerformanceCounters).
        _perf.BeginOverlay();
        if (_devTools.IsEnabled)
            _perf.ElementCount = PerformanceCounters.CountElements(_root);
        _devTools.Render(ctx, _root, PlatformWindow.LayoutWidth, PlatformWindow.LayoutHeight);
        _perf.EndOverlay();

        // Flush the recorded draw commands to the GPU. Under GPU contention
        // (another 3D app, compositor pressure) this call can stall for tens of
        // milliseconds — time it as its own phase so frame-time analysis can
        // attribute (and exclude) GPU-bound waits instead of leaving them as
        // unaccounted time inside the Frame scope. This is where the display list
        // recorded during Render actually rasterizes, so it is usually the bulk
        // of the gap between Render and the total frame time.
        _perf.BeginFlush();
        using (_tracer.Begin("Phase", "Flush"))
            ctx.EndFrame();
        _perf.EndFlush();

        // A virtual list makes and places its rows as it is drawn: what that moved is told by the
        // coalesced refresh, which this asks for (the frame's look came before the draw).
        if (a11yActive && TakeMovedA11yBoxes())
            _a11yLayoutRefreshPending = true;
        AuditSkippedA11yRefresh(a11yActive, layoutWasDirty, a11yRebuild);

        // Clear remaining dirty flags after full render
        ClearAllFlags(_root);
        foreach (var overlay in _overlays)
            ClearAllFlags(overlay.Content);

        // Present + swap
        _perf.BeginPresent();
        using (_tracer.Begin("Phase", "Present"))
        {
            PlatformWindow.Surface.Present();
            PlatformWindow.SwapBuffers();
        }
        _perf.EndPresent();

        // Fire one-shot post-frame callbacks now that this frame is on screen.
        // Swap to a local list first so a callback that registers another one
        // schedules it for the *next* frame rather than reentering this loop.
        if (_postFrameCallbacks.Count > 0)
        {
            var due = _postFrameCallbacks.ToArray();
            _postFrameCallbacks.Clear();
            foreach (var cb in due)
                cb();
        }

        FrameRendered?.Invoke();
        return true;
    }

    /// <summary>
    /// Gives the DevTools inspector first refusal on an input event. Returns true
    /// when the event was consumed and must not reach the app.
    ///
    /// Two distinct jobs live here. In the inspector's own window, navigation keys
    /// drive the panel. In an inspected window with the picker armed, pointer input
    /// selects elements instead of operating them — that is the whole point of a
    /// picker, and it is why the interception has to happen before dispatch rather
    /// than as a handler somewhere in the tree.
    /// </summary>
    private bool TryHandleDevToolsInput(InputEvent evt)
    {
        var host = Application.InspectorHost;

        if (IsDevToolsWindow)
        {
            return host is not null
                   && evt is InputEvent.KeyInput { IsDown: true } panelKey
                   && host.HandleKey(panelKey.KeyCode, panelKey.Modifiers);
        }

        if (Application.DevToolsHotkeysEnabled
            && evt is InputEvent.KeyInput { IsDown: true } key
            && key.Modifiers.HasFlag(KeyModifiers.Ctrl) && key.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            // Ctrl+Shift+I only. Browsers also bind Ctrl+Shift+C to "inspect", but a
            // browser is the devtools host — a general UI framework claiming that
            // chord takes it from the app underneath, silently and before dispatch.
            // The first real app this shipped against already owned it ("copy as
            // path"), so the picker is armed from the panel's Pick button or from
            // OpenInspector(startPicking: true) instead.
            if (key.KeyCode == KeyCode.I)
            {
                Application.ToggleInspector(this, startPicking: false);
                return true;
            }
        }

        if (_devTools.Inspector is not { IsPicking: true } inspector) return false;

        switch (evt)
        {
            case InputEvent.MouseMoveInput move:
                inspector.SetHovered(PickTargetAt(new Point(move.X, move.Y)));
                // The preview moved, so the highlight has to be repainted even
                // though nothing in the app tree changed.
                _root!.MarkPaintDirty();
                return true;

            case InputEvent.MouseButtonInput { IsDown: true } press:
                inspector.Pick(PickTargetAt(new Point(press.X, press.Y)));
                _root!.MarkPaintDirty();
                return true;

            // Swallow the matching release so the app never sees a lone button-up
            // (which reads as the end of a drag it never started).
            case InputEvent.MouseButtonInput:
                return true;

            case InputEvent.KeyInput { IsDown: true, KeyCode: KeyCode.Escape }:
                inspector.SetPicking(false);
                _root!.MarkPaintDirty();
                return true;

            // Wheel events fall through on purpose: scrolling the app to reach an
            // element that is off-screen is part of picking it.
            default:
                return false;
        }
    }

    /// <summary>The element the picker would select at <paramref name="pos"/> (overlays first, then the main tree).</summary>
    private Element? PickTargetAt(Point pos)
    {
        var (overlayTarget, _) = HitTestOverlays(pos);
        return overlayTarget ?? (_root is null ? null : HitTester.HitTest(_root, pos));
    }

    /// <summary>A modifier on its own is not keyboard use, nor a chord with Ctrl, Alt or Super:
    /// Ctrl+C after a click is the mouse user's shortcut, not navigation.</summary>
    private static bool IsKeyboardUse(InputEvent.KeyInput key) =>
        key.KeyCode is not (KeyCode.ShiftLeft or KeyCode.ShiftRight or KeyCode.CtrlLeft or KeyCode.CtrlRight
            or KeyCode.AltLeft or KeyCode.AltRight or KeyCode.SuperLeft or KeyCode.SuperRight
            or KeyCode.CapsLock or KeyCode.NumLock or KeyCode.ScrollLock)
        && (key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Super)) == 0;

    private static KeyDownEvent NewKeyDown(InputEvent.KeyInput key) => new()
    {
        Key = key.KeyCode,
        Modifiers = key.Modifiers,
        IsRepeat = key.IsRepeat,
        ScanCode = key.ScanCode,
        LayoutKey = key.LayoutKey,
        ModifierKeys = key.ModifierKeys,
        Text = key.Text,
    };

    /// <summary><see cref="HandleInputEvent"/>, counted with its bytes for <see cref="FrameLog"/>.</summary>
    private void HandleInputEventLogged(InputEvent evt)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        HandleInputEvent(evt);
        FrameLog.Input(GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private void HandleInputEvent(InputEvent evt)
    {
        if (_root is null) return;

        if (TryHandleDevToolsInput(evt)) return;

        // Snapshot state that decides whether a mouse move needs a frame (see
        // the tail of this method). Everything a move can visually change marks
        // itself dirty (interaction styles, scrollbar/text-selection capture
        // drags, tooltip overlays) — these are the remaining signals.
        long handlersBefore = EventDispatcher.HandlerInvocationCount;
        var hoveredBefore = _lastHoveredElement;

        switch (evt)
        {
            case InputEvent.MouseMoveInput move:
            {
                var pos = new Point(move.X, move.Y);

                // An element that captured the pointer on its press gets every move,
                // wherever the pointer is, and nothing else hears about the pointer
                // (no hover changes, no drag start) until the release.
                if (_pointerCaptureTarget is { } captured)
                {
                    if (captured.Parent is null && !ReferenceEquals(captured, _root))
                    {
                        _pointerCaptureTarget = null;   // rebuilt away mid-drag: the capture ends with it
                    }
                    else
                    {
                        EventDispatcher.DispatchPointerMove(captured,
                            new Point(pos.X - _pointerCaptureOffset.X, pos.Y - _pointerCaptureOffset.Y),
                            new Point(move.DeltaX, move.DeltaY), move.Modifiers);
                        break;
                    }
                }

                // If dragging a scrollbar, route directly to the drag target
                if (_scrollbarDragTarget is not null)
                {
                    _scrollbarDragTarget.UpdateScrollbarDrag(pos);
                    break;
                }

                // Autoscroll owns the pointer while the middle button is down. A move only
                // moves the aiming point — the scrolling itself is per frame, in
                // TickAutoScroll, because the view has to keep moving while the pointer is
                // held still.
                if (_autoScrollTarget is not null)
                {
                    _autoScrollPointer = pos;
                    break;
                }

                // If dragging in a text input, route move to the capture target
                if (_textInputDragTarget is not null)
                {
                    _textInputDragTarget.UpdateDragPosition(pos);
                    break;
                }

                if (_textAreaDragTarget is not null)
                {
                    _textAreaDragTarget.UpdateDragPosition(pos);
                    break;
                }

                if (_groupDragToken is not null)
                {
                    UpdateGroupDrag(pos);
                    break;
                }

                if (_textElementDragTarget is not null)
                {
                    _textElementDragTarget.UpdateDragPosition(pos);
                    break;
                }

                // If actively dragging (internal), update drag hover
                if (_isDragging && !_isExternalDrag)
                {
                    UpdateDragHover(pos, move.Modifiers);
                    break;
                }

                // Check if a potential drag should activate
                if (_potentialDragSource is not null)
                {
                    float dx = pos.X - _dragStartPos.X;
                    float dy = pos.Y - _dragStartPos.Y;
                    if (dx * dx + dy * dy >= DragThreshold * DragThreshold)
                    {
                        StartInternalDrag(pos, move.Modifiers);
                        break;
                    }
                }

                // Check overlays first (topmost layer), then main tree
                var (overlayMoveTarget, overlayMoveOffset) = HitTestOverlays(pos);
                var target = overlayMoveTarget ?? HitTester.HitTest(_root, pos);
                // The Windows custom frame owns the maximize button as non-client
                // (HTMAXBUTTON); keep the client pipeline out of it.
                if (overlayMoveTarget is null && IsNonClientMaximizeButton(target)) target = null;
                var effectivePos = overlayMoveTarget is not null
                    ? new Point(pos.X - overlayMoveOffset.X, pos.Y - overlayMoveOffset.Y)
                    : pos;
                _pointerState.UpdateHover(target, effectivePos, move.Modifiers);
                _lastHoveredElement = target;

                // Update cursor based on hovered element's resolved style. A scrollbar
                // track overrides to the default arrow, so a scrollable element's own
                // cursor (e.g. a TextArea's text caret) doesn't bleed onto its bar.
                var cursor = target?.ResolvedInherited.Cursor ?? CursorType.Default;
                IScrollbarDraggable? overScrollbar = null;
                for (var el = target; el is not null; el = el.Parent)
                {
                    if (el is IScrollbarDraggable sb && sb.IsPointOverScrollbar(pos))
                    {
                        cursor = CursorType.Default;
                        overScrollbar = sb;
                        break;
                    }
                }
                PlatformWindow.SetCursor(cursor);
                _hoverCursor = cursor;
                _cursorFollowsHover = overScrollbar is null;
                SetScrollbarHover(overScrollbar, pos);

                // Tooltip tracking
                UpdateTooltipHover(target);

                // Update DevTools inspector
                if (_devTools.IsEnabled)
                    _devTools.HoveredElement = target;

                // The event is made only when something on the path listens for moves.
                if (target is not null)
                    EventDispatcher.DispatchPointerMove(target, effectivePos, new Point(move.DeltaX, move.DeltaY), move.Modifiers);
                break;
            }

            case InputEvent.MouseButtonInput button:
            {
                var pos = new Point(button.X, button.Y);

                // Hide tooltip on any mouse button press
                if (button.IsDown)
                    HideTooltip();

                // The release of a press that only closed an anchored overlay goes nowhere.
                if (!button.IsDown && button.Button == _swallowedRelease)
                {
                    _swallowedRelease = -1;
                    break;
                }

                // The release ends a pointer capture: it goes to the captured element
                // (with the click, if that element was also pressed), then hover catches
                // up with wherever the pointer is now.
                if (!button.IsDown && _pointerCaptureTarget is { } captured)
                {
                    _pointerCaptureTarget = null;
                    var capturedPos = new Point(pos.X - _pointerCaptureOffset.X, pos.Y - _pointerCaptureOffset.Y);
                    var (overlayHover, overlayHoverOffset) = HitTestOverlays(pos);
                    var nowUnder = overlayHover ?? HitTester.HitTest(_root, pos);
                    var (capUp, capClick, capDbl) = _pointerState.HandlePointerUp(captured, capturedPos, button.Button, button.Modifiers);
                    EventDispatcher.Dispatch(capUp, captured);
                    // A click needs the release over the element (or inside it), as it
                    // would without capture; letting go elsewhere is how a press is abandoned.
                    bool releasedOver = false;
                    for (var el = nowUnder; el is not null; el = el.Parent)
                        if (ReferenceEquals(el, captured)) { releasedOver = true; break; }
                    if (releasedOver && capClick is not null) EventDispatcher.Dispatch(capClick, captured);
                    if (releasedOver && capDbl is not null) EventDispatcher.Dispatch(capDbl, captured);

                    var hoverPos = overlayHover is not null
                        ? new Point(pos.X - overlayHoverOffset.X, pos.Y - overlayHoverOffset.Y) : pos;
                    _pointerState.UpdateHover(nowUnder, hoverPos, button.Modifiers);
                    _lastHoveredElement = nowUnder;
                    break;
                }

                // End internal drag on button up
                if (!button.IsDown && _isDragging && !_isExternalDrag)
                {
                    EndInternalDrag(pos, button.Modifiers);
                    break;
                }

                // Cancel potential drag on button up (didn't reach threshold)
                if (!button.IsDown && _potentialDragSource is not null)
                    _potentialDragSource = null;

                // End scrollbar drag on button up
                if (!button.IsDown && _scrollbarDragTarget is not null)
                {
                    _scrollbarDragTarget.EndScrollbarDrag();
                    _scrollbarDragTarget = null;
                    break;
                }

                // End the autoscroll. Nothing has to be suppressed: a click is the primary
                // button's alone (see PointerState), so the middle button never synthesizes
                // one however far it travelled. The release is still dispatched, so nothing
                // is left holding a pressed :active style.
                if (!button.IsDown && button.Button == 1 && _autoScrollTarget is not null)
                {
                    _autoScrollTarget = null;
                    _root!.MarkPaintDirty();   // take the anchor marker off the screen
                    PlatformWindow.SetCursor(_lastHoveredElement?.ResolvedInherited.Cursor ?? CursorType.Default);
                }

                // End text input drag on button up
                if (!button.IsDown && _textInputDragTarget is not null)
                {
                    _textInputDragTarget.EndDrag();
                    _textInputDragTarget = null;
                    // Don't break — still dispatch PointerUp/Click to the target
                }

                if (!button.IsDown && _textAreaDragTarget is not null)
                {
                    _textAreaDragTarget.EndDrag();
                    _textAreaDragTarget = null;
                }

                if (!button.IsDown && _textElementDragTarget is not null)
                {
                    _textElementDragTarget.EndDrag();
                    _textElementDragTarget = null;
                }

                // End a group drag but keep the selection so Ctrl+C / Copy can use it.
                // Clear the anchor label's own drag flag — group mode never routed through
                // _textElementDragTarget, so without this its OnPointerMove would keep
                // extending the selection on a plain hover after the button is released.
                if (!button.IsDown && _groupDragToken is not null)
                {
                    foreach (var label in CollectGroupLabels(_groupDragToken))
                        label.EndDrag();
                    _groupDragToken = null;
                }

                // Check overlays first, then main tree
                var (overlayTarget, overlayOffset) = HitTestOverlays(pos);
                var target = overlayTarget ?? HitTester.HitTest(_root, pos);
                if (target is null) break;

                bool isOverlayHit = overlayTarget is not null;
                var effectiveBtnPos = isOverlayHit
                    ? new Point(pos.X - overlayOffset.X, pos.Y - overlayOffset.Y)
                    : pos;

                if (button.IsDown)
                {
                    // A press is the pointer taking over, whatever it lands on: the focus that
                    // follows is not keyboard focus, and the ring stays down until the keyboard is used.
                    _focusFromKeyboard = false;

                    // Dismiss overlays when clicking outside them. A press in an overlay is
                    // outside the anchored overlays above it (a popover opened from a dialog
                    // closes on a press elsewhere in the dialog); the others keep the old rule
                    // and close only on a press in the main tree.
                    if (_overlays.Count > 0)
                    {
                        int hitLevel = isOverlayHit ? OverlayIndexOf(overlayTarget!) : -1;
                        bool onAnchor = PressIsOnAnchorAbove(pos, hitLevel);
                        DismissOverlaysOnOutsideClick(hitLevel);
                        // A press on the anchor of the overlay it closed does nothing else: the
                        // button that opened the overlay would open it again on the click.
                        if (onAnchor)
                        {
                            _swallowedRelease = button.Button;
                            break;
                        }
                    }

                    // Check if click is on a scrollbar — walk up from target to find a ScrollViewElement
                    if (!isOverlayHit && button.Button == 0 && TryStartScrollbarDrag(target, pos))
                        break;

                    // Middle button in anything that can scroll: anchor an autoscroll here.
                    // Claimed before the press is dispatched, but the press still goes out —
                    // a middle click that never leaves the dead zone has to keep meaning
                    // whatever the app made it mean.
                    if (button.Button == 1 && FindAutoScrollAncestor(target) is { } scroller)
                    {
                        _autoScrollTarget = scroller;
                        _autoScrollOrigin = pos;
                        _autoScrollPointer = pos;
                        _autoScrollLastMs = Application.Platform.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds();
                        PlatformWindow.SetCursor(CursorType.Move);
                    }

                    // Move focus to a focusable element under the pointer. For the
                    // main tree this always runs (clicking empty space clears focus).
                    // For overlays we only take focus when the hit is itself focusable
                    // — a dialog's text input should focus, but a transient popup like
                    // a context menu opened on a TextInput must keep the underlying
                    // element's focus and selection.
                    var focusTarget = FindFocusableAncestor(target);
                    if (!isOverlayHit || focusTarget is not null)
                    {
                        _focusManager.SetFocus(focusTarget, FocusReason.Pointer);

                        // Provide clipboard to text inputs when focused
                        if (focusTarget is TextInputElement textInput)
                            textInput.Clipboard = Clipboard;
                        else if (focusTarget is TextAreaElement textArea)
                            textArea.Clipboard = Clipboard;
                        else if (focusTarget is ITextServicesClient client)
                            client.Clipboard = Clipboard;
                        else if (focusTarget is TextElement selectableLabel && selectableLabel.Selectable)
                            selectableLabel.Clipboard = Clipboard;
                    }

                    var downEvt = _pointerState.HandlePointerDown(target, effectiveBtnPos, button.Button, button.Modifiers);
                    EventDispatcher.Dispatch(downEvt, target);

                    // A handler that captured the pointer owns the gesture from here: no
                    // text drag, no drag source, no context menu comes of this press.
                    if (downEvt.CaptureTarget is { } capture)
                    {
                        _pointerCaptureTarget = capture;
                        _pointerCaptureOffset = isOverlayHit ? overlayOffset : default;
                        break;
                    }

                    // A press outside the labels of a group that holds a live selection
                    // clears it (so a stale multi-line highlight doesn't linger).
                    if (_activeTextGroup is not null &&
                        !(target is TextElement at && ReferenceEquals(at.SelectionGroup, _activeTextGroup)))
                    {
                        _activeTextGroup.Clear();
                        RepaintGroup(_activeTextGroup);
                        _activeTextGroup = null;
                    }

                    // Start pointer capture for text input drag selection
                    if (!isOverlayHit && button.Button == 0 && target is TextInputElement dragInput && dragInput.IsDragging)
                        _textInputDragTarget = dragInput;
                    else if (!isOverlayHit && button.Button == 0 && target is TextAreaElement dragArea && dragArea.IsDragging)
                        _textAreaDragTarget = dragArea;
                    else if (!isOverlayHit && button.Button == 0 && target is TextElement dragLabel && dragLabel.IsDragging)
                    {
                        if (dragLabel.SelectionGroup is { } grp && dragLabel.SelectionIndex >= 0)
                        {
                            // Group drag: the window drives selection across every label
                            // sharing this token, so the highlight and copy span lines.
                            _groupDragToken = grp;
                            _activeTextGroup = grp;
                            _groupDragX = dragLabel.Layout.AbsoluteContentBox.X + 2f;
                            _groupDragUnit = dragLabel.DragUnit;
                            _groupDragAnchorIndex = dragLabel.SelectionIndex;
                            _groupDragAnchorRange = dragLabel.DragAnchorRange;
                            // Shift keeps the existing anchor and moves only the loose end,
                            // so a click then a shift-click covers the span between them —
                            // and a drag from there carries on extending from the same anchor.
                            if (button.Modifiers.HasFlag(KeyModifiers.Shift) && grp.FocusIndex >= 0
                                && _groupDragUnit == SelectionUnit.Character)
                            {
                                grp.ExtendTo(dragLabel.SelectionIndex, dragLabel.CaretOffset);
                            }
                            else
                            {
                                // A double or triple press starts with its whole word or line
                                // already selected, which is the range the label worked out.
                                grp.Anchor(dragLabel.SelectionIndex, _groupDragAnchorRange.Start);
                                if (_groupDragUnit != SelectionUnit.Character)
                                    grp.ExtendTo(dragLabel.SelectionIndex, _groupDragAnchorRange.End);
                            }
                            RepaintGroup(grp);
                        }
                        else
                        {
                            _textElementDragTarget = dragLabel;
                        }
                    }

                    // Record potential drag source if the element (or ancestor) is draggable
                    if (!isOverlayHit && button.Button == 0 && _textInputDragTarget is null && _textAreaDragTarget is null && _textElementDragTarget is null && _groupDragToken is null)
                    {
                        var draggable = FindDraggableAncestor(target);
                        if (draggable is not null)
                        {
                            _potentialDragSource = draggable;
                            _dragStartPos = pos;
                        }
                    }

                    // Right-click: show context menu. Allowed on overlay hits too so a
                    // text input inside a dialog gets its Cut/Copy/Paste menu; the menu
                    // is itself an overlay and stacks above the dialog.
                    if (button.Button == 2)
                        TryShowContextMenu(target, pos);
                }
                else
                {
                    var (upEvt, clickEvt, dblClickEvt) = _pointerState.HandlePointerUp(target, effectiveBtnPos, button.Button, button.Modifiers);
                    EventDispatcher.Dispatch(upEvt, target);
                    if (clickEvt is not null)
                        EventDispatcher.Dispatch(clickEvt, target);
                    if (dblClickEvt is not null)
                        EventDispatcher.Dispatch(dblClickEvt, target);
                }
                break;
            }

            case InputEvent.PointerLeftWindowInput:
            {
                // Out of the window, the pointer is over nothing of ours: leave the hover
                // chain as a move onto empty space would, or a button at the edge (or a web
                // page) keeps its hover state until the pointer comes back. Not while a press
                // holds the pointer — the platform keeps sending that element moves outside.
                if (_pointerCaptureTarget is not null || _isDragging || _autoScrollTarget is not null) break;
                _pointerState.UpdateHover(null, default, KeyModifiers.None);
                _lastHoveredElement = null;
                _cursorFollowsHover = false;
                SetScrollbarHover(null, default);
                UpdateTooltipHover(null);
                break;
            }

            case InputEvent.MouseWheelInput wheel:
            {
                var pos = new Point(wheel.MouseX, wheel.MouseY);
                // Overlays first, as a press does: a wheel over a dialog or a popup list is
                // that overlay's, not the page's underneath it.
                var (overlayTarget, overlayOffset) = HitTestOverlays(pos);
                var target = overlayTarget ?? HitTester.HitTest(_root, pos);
                if (target is not null)
                {
                    var at = overlayTarget is not null
                        ? new Point(pos.X - overlayOffset.X, pos.Y - overlayOffset.Y)
                        : pos;
                    long now = Application.Platform.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds();
                    var scroll = new ScrollEvent
                    {
                        Position = at, DeltaX = wheel.DeltaX, DeltaY = wheel.DeltaY, Modifiers = wheel.Modifiers,
                        Latched = _wheelGesture.LatchFor(target, now),
                    };
                    EventDispatcher.Dispatch(scroll, target);
                    _wheelGesture.Dispatched(scroll, now);
                }
                break;
            }

            case InputEvent.KeyInput key:
            {
                // Any key that means something on its own is the keyboard taking over, as a
                // browser's :focus-visible has it: the focus shows its ring from now on, wherever
                // a handler moves it — an arrow through a tab strip, not only Tab.
                if (key.IsDown && IsKeyboardUse(key))
                    _focusFromKeyboard = true;

                // F12 cycles DevTools: enabled → enabled + continuous redraw → disabled
                if (key.IsDown && key.KeyCode == KeyCode.F12 && Application.DevToolsHotkeysEnabled)
                {
                    if (!_devTools.IsEnabled)
                    {
                        _devTools.IsEnabled = true;
                    }
                    else if (!_devTools.ContinuousRedraw)
                    {
                        _devTools.ContinuousRedraw = true;
                    }
                    else
                    {
                        _devTools.IsEnabled = false;
                        _devTools.ContinuousRedraw = false;
                        _devTools.HoveredElement = null;
                    }
                    _devTools.ShowBoundingBoxes = _devTools.IsEnabled;
                    break;
                }

                // Trace recording is a developer surface like the HUD, so the same
                // switch governs it. The HULTUI_TRACE environment variable still
                // works either way, which is what benchmark automation uses.
                if (key.IsDown && key.KeyCode == KeyCode.F11 && Application.DevToolsHotkeysEnabled)
                {
                    if (!Tracer.IsRecording)
                    {
                        Tracer.StartRecording();
                        // Same graphics stamp the env-driven path gets at startup.
                        if (PlatformWindow.DescribeGraphics() is { } gfx)
                            Tracer.Annotate("Graphics", gfx);
                    }
                    else
                        Tracer.StopRecording(FrameTracer.DefaultPath);
                }

                // Context menu keyboard navigation. An open flyout captures
                // Up/Down/Enter; → opens a flyout, ← / Escape close one level.
                if (key.IsDown && _contextMenuElement is not null)
                {
                    var activeMenu = _contextSubmenuElement ?? _contextMenuElement;
                    switch (key.KeyCode)
                    {
                        case KeyCode.Up:
                            activeMenu.MoveSelection(-1);
                            break;
                        case KeyCode.Down:
                            activeMenu.MoveSelection(1);
                            break;
                        case KeyCode.Right:
                            if (_contextSubmenuElement is null
                                && _contextMenuElement.ItemAt(_contextMenuElement.HoveredIndex) is { HasChildren: true })
                            {
                                OnContextMenuHover(_contextMenuElement, _contextMenuElement.HoveredIndex);
                                _contextSubmenuElement?.MoveSelection(1);
                            }
                            // Nothing to open here: a menu bar moves to the next menu.
                            else _contextMenuNavigate?.Invoke(1);
                            break;
                        case KeyCode.Left:
                            if (_contextSubmenuElement is not null) CloseContextSubmenu();
                            else _contextMenuNavigate?.Invoke(-1);
                            break;
                        case KeyCode.Enter:
                        {
                            var hadSubmenu = _contextSubmenuElement is not null;
                            activeMenu.ActivateSelected();
                            // Enter on a parent item opened its flyout — land on its first entry.
                            if (!hadSubmenu && _contextSubmenuElement is not null)
                                _contextSubmenuElement.MoveSelection(1);
                            break;
                        }
                        case KeyCode.Escape:
                            if (_contextSubmenuElement is not null) CloseContextSubmenu();
                            else CloseContextMenu(MenuCloseReason.Escape);
                            break;
                        default:
                            goto normalKeyHandling;
                    }
                    break;
                }
                normalKeyHandling:

                // Tab / Shift+Tab focus traversal. Dispatch the key to the focused
                // element first so an app can observe or intercept Tab; a handler
                // that sets StopPropagation cancels the default traversal (mirrors a
                // browser's preventDefault on a Tab keydown). Without this the key
                // never reached KeyDown handlers at all, so app-level Tab bindings
                // were silently dead.
                if (key.IsDown && key.KeyCode == KeyCode.Tab)
                {
                    var tabTarget = KeyTarget();
                    var tabEvent = NewKeyDown(key);
                    EventDispatcher.Dispatch(tabEvent, tabTarget);
                    if (tabEvent.StopPropagation)
                        break;

                    MoveFocus(forward: !key.Modifiers.HasFlag(KeyModifiers.Shift));
                    break;
                }

                var target = KeyTarget();
                // Ctrl/Cmd+C over a grouped label copies the whole cross-label selection
                // (the label's own handler skips grouped copy, so there's no double).
                // Keyed off the group holding the selection rather than off the focused
                // element: in a virtualized list the label the user pressed in may have been
                // recycled by the time they hit Ctrl+C, and the copy has to work anyway.
                // The text comes from the group's model, so it spans lines that were never
                // realized — copying 900 lines does not need 900 live labels.
                if (key.IsDown && key.KeyCode == KeyCode.C
                    && (key.Modifiers.HasFlag(KeyModifiers.Ctrl) || key.Modifiers.HasFlag(KeyModifiers.Super))
                    && _activeTextGroup is { HasSelection: true } copyGroup)
                {
                    var text = copyGroup.SelectedText;
                    if (text.Length > 0) Clipboard?.SetText(text);
                }
                if (key.IsDown)
                {
                    // Shift+navigation moves the loose end of a group selection, which
                    // changes the highlight on labels other than the focused one — so the
                    // whole realized group repaints, exactly as it does after a drag.
                    int before = _activeTextGroup?.Version ?? 0;
                    EventDispatcher.Dispatch(NewKeyDown(key), target);
                    if (_activeTextGroup is { } keyed && keyed.Version != before)
                        RepaintGroup(keyed);
                }
                else
                {
                    EventDispatcher.Dispatch(
                        new KeyUpEvent { Key = key.KeyCode, Modifiers = key.Modifiers, ScanCode = key.ScanCode },
                        target);
                }
                break;
            }

            case InputEvent.TextInputInput textInput:
            {
                var target = KeyTarget();
                EventDispatcher.Dispatch(new TextInputEvent { Text = textInput.Text }, target);
                break;
            }

            case InputEvent.TextEditingInput editInput:
            {
                var target = KeyTarget();
                EventDispatcher.Dispatch(new TextEditingEvent
                {
                    Text = editInput.Text,
                    SegmentStart = editInput.SegmentStart,
                    SegmentLength = editInput.SegmentLength,
                }, target);
                break;
            }

            // ──── OS-level drag & drop ────
            case InputEvent.DropBeginInput begin:
            {
                _isExternalDrag = true;
                _isDragging = true;
                _dragData = new DragData { NativeData = begin.NativeData };
                _dragHoveredElement = null;
                _dragAccepted = false;
                _dragModifiers = KeyModifiers.None;
                break;
            }

            case InputEvent.DropPositionInput dropPos:
            {
                if (!_isExternalDrag) break;
                var pos = new Point(dropPos.X, dropPos.Y);
                UpdateDragHover(pos, dropPos.Modifiers);
                if (dropPos.Feedback is { } feedback) feedback.Accepted = _dragAccepted;
                break;
            }

            case InputEvent.DropFileInput dropFile:
            {
                if (_dragData is not null)
                    _dragData.Files.Add(dropFile.Path);
                break;
            }

            case InputEvent.DropTextInput dropText:
            {
                if (_dragData is not null)
                    _dragData.Text = dropText.Text;
                break;
            }

            case InputEvent.DropCompleteInput complete:
            {
                bool dropped = EndExternalDrag(complete.Modifiers);
                if (complete.Feedback is { } feedback) feedback.Accepted = dropped;
                break;
            }

            case InputEvent.DropCancelInput:
            {
                if (_isExternalDrag) CancelExternalDrag();
                break;
            }
        }

        // Any input event may have triggered visual changes via handlers, so as
        // a safety net force a render — except for mouse moves, the one
        // high-frequency event. A move over static content (no handler ran, no
        // hover change, no active drag) changes nothing visual: interaction
        // styles, capture drags, and overlays all mark dirty themselves, and a
        // handler that did run is caught by the invocation counter (handlers may
        // mutate without marking, per the imperative-mutation convention's
        // safety net). This turns pointer motion over an idle window from a
        // full frame per event into no work at all.
        // Note: DevTools needs no clause of its own — its element inspector
        // renders from the hovered element only (not the raw pointer position),
        // so the hover-changed condition already covers it. Forcing a render per
        // move while the overlay is open just spins the FPS counter.
        if (evt is not InputEvent.MouseMoveInput
            || EventDispatcher.HandlerInvocationCount != handlersBefore // a handler ran — may have mutated silently
            || !ReferenceEquals(_lastHoveredElement, hoveredBefore)     // hover chain changed (enter/leave side effects)
            || _isDragging)                                             // drag ghost follows the cursor
        {
            _root.MarkPaintDirty();
        }
    }

    // ── Cross-label (group) text selection ──

    /// <summary>Extends the active group selection to the pointer position.</summary>
    private void UpdateGroupDrag(Point pos)
    {
        if (_groupDragToken is null) return;
        var focus = FindGroupLabelAt(pos, _groupDragToken);
        if (focus is null || focus.SelectionIndex < 0) return;
        int offset = focus.OffsetAt(pos);
        if (_groupDragUnit == SelectionUnit.Character)
        {
            _groupDragToken.ExtendTo(focus.SelectionIndex, offset);
        }
        else
        {
            // Word- or line-granular drag: the pointer's unit is taken whole, and which end
            // of the anchor unit stays put depends on which side of it the pointer is on —
            // compared by (line, offset), since the drag runs across labels.
            var hit = focus.UnitRangeAt(offset, _groupDragUnit);
            bool before = focus.SelectionIndex < _groupDragAnchorIndex
                || (focus.SelectionIndex == _groupDragAnchorIndex && hit.Start < _groupDragAnchorRange.Start);
            _groupDragToken.Anchor(_groupDragAnchorIndex,
                before ? _groupDragAnchorRange.End : _groupDragAnchorRange.Start);
            _groupDragToken.ExtendTo(focus.SelectionIndex, before ? hit.Start : hit.End);
        }
        RepaintGroup(_groupDragToken);
    }

    /// <summary>
    /// Marks every realized label of a group for repaint. The labels derive their highlight
    /// from the group, so this is all that is needed after the selection moves — and the
    /// labels that are not realized need nothing, which is the point.
    /// </summary>
    private void RepaintGroup(TextSelectionGroup group)
    {
        foreach (var label in CollectGroupLabels(group))
            label.MarkPaintDirty();
    }

    /// <summary>Every realized label of <paramref name="token"/>. Order does not matter: each
    /// label knows its own model index, so the highlight no longer depends on tree order.</summary>
    private List<TextElement> CollectGroupLabels(TextSelectionGroup token)
    {
        var result = new List<TextElement>();
        Collect(_root);
        return result;

        void Collect(Element? el)
        {
            if (el is null) return;
            if (el is TextElement te && te.Selectable && ReferenceEquals(te.SelectionGroup, token))
                result.Add(te);
            var children = el.Children;   // by index: foreach over the interface boxes an enumerator
            for (int i = 0; i < children.Count; i++) Collect(children[i]);
        }
    }

    /// <summary>The group label at <paramref name="pos"/>. Uses <see cref="HitTester"/> so it
    /// stays correct inside a scrolled list (row layout is unscrolled; the renderer applies the
    /// scroll offset, so raw layout coordinates can't be compared to the screen point). If the
    /// pointer is over the gutter/gap beside the code, it re-probes the code column (the anchor's
    /// X) at the pointer's Y so the drag still lands on the right line.</summary>
    private TextElement? FindGroupLabelAt(Point pos, TextSelectionGroup token)
    {
        if (_root is null) return null;
        if (GroupLabelFromHit(HitTester.HitTest(_root, pos), token) is { } direct)
            return direct;
        // Re-probe at the X the drag started from — remembered as a coordinate rather than as
        // the anchor label, which a virtualized list may already have recycled. The column
        // does not move, so the coordinate stays good for the life of the drag.
        return _groupDragX > 0
            ? GroupLabelFromHit(HitTester.HitTest(_root, new Point(_groupDragX, pos.Y)), token)
            : null;
    }

    private static TextElement? GroupLabelFromHit(Element? hit, TextSelectionGroup token)
    {
        for (var e = hit; e is not null; e = e.Parent)
            if (e is TextElement te && te.Selectable && ReferenceEquals(te.SelectionGroup, token))
                return te;
        return null;
    }

    /// <summary>
    /// Walks from the hit target up through ancestors to find the nearest focusable element.
    /// Returns null if no focusable ancestor exists (clicking on non-interactive area clears focus).
    /// </summary>
    private static Element? FindFocusableAncestor(Element? target)
    {
        var current = target;
        while (current is not null)
        {
            if (current.IsFocusable)
                return current;
            current = current.Parent;
        }
        return null;
    }

    /// <summary>The nearest scrollable surface above <paramref name="target"/> that opts into
    /// middle-click autoscroll and has somewhere left to scroll. Nested lists resolve
    /// inwards, which is what a pointer inside the inner one means.</summary>
    /// <remarks>Matched on <see cref="IAutoScrollable"/> rather than on a scroll view, because
    /// a text area scrolls its own wrapped rows without being one. Looking for the class meant
    /// the gesture worked on every list in an app and did nothing on the wall of text those
    /// lists were there to open — Meridian's response body, most visibly.</remarks>
    private static IAutoScrollable? FindAutoScrollAncestor(Element? target)
    {
        for (var current = target; current is not null; current = current.Parent)
            if (current is IAutoScrollable { MiddleClickAutoScroll: true, CanAutoScroll: true } sv)
                return sv;
        return null;
    }

    /// <summary>
    /// One frame of autoscroll. The pointer's offset from the anchor is a speed, not a
    /// distance, so the view keeps moving while the button is held even though no further
    /// input arrives — which is the whole difference between this and dragging the content
    /// around by hand.
    /// </summary>
    /// <remarks>The target was captured on press and a rebuild in between can reconcile it
    /// away; writing a scroll offset on that corpse lands on state nothing consumes (and
    /// throws under strict lifetime checks), so a detached one is re-resolved from the
    /// pointer and the gesture carries on.</remarks>
    private void TickAutoScroll()
    {
        if (_autoScrollTarget is null || _root is null) return;

        // Two ways the captured view stops being the live one, and IsDetached only covers the
        // first. A patch marks the *incoming* element detached and keeps the retained one, so
        // a target that was patched is still the right object. A rebuild that *replaces* the
        // element instead drops the old one from the tree without marking anything — it looks
        // perfectly alive, and scrolling it moves something nobody can see. Junction's diff
        // does exactly that, once, part way through a gesture: the scroll stopped dead and the
        // view sat at the top.
        var captured = (Element)_autoScrollTarget;
        if (captured.IsDetached || !IsAttachedToRoot(captured))
        {
            // Overlays first, the same order the press used to find the target. A dropdown's
            // popup list is a real overlay: hit-testing only the main tree would re-resolve to
            // whatever sits *underneath* it and scroll that instead.
            var (overlay, _) = HitTestOverlays(_autoScrollPointer);
            var live = FindAutoScrollAncestor(overlay ?? HitTester.HitTest(_root, _autoScrollPointer));
            // Nothing yet: the replacement is built but this tick runs before layout, so on
            // its first frame it has no content size and cannot say whether it scrolls. Keep
            // the old reference and ask again next frame rather than giving up — dropping it
            // here ended the gesture for good, one frame after any rebuild.
            if (live is null) return;
            // Carry the position over, so a rebuild during the gesture does not throw the
            // reader back to the top. The same promise PatchFrom already keeps for a patched
            // scroll view; a replaced one had nobody keeping it.
            live.ScrollOffsetY = _autoScrollTarget.ScrollOffsetY;
            _autoScrollTarget = live;
        }

        long now = Application.Platform.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        float dt = Math.Clamp((now - _autoScrollLastMs) / 1000f, 0f, 0.25f);
        _autoScrollLastMs = now;

        float vx = Ramp(_autoScrollPointer.X - _autoScrollOrigin.X);
        float vy = Ramp(_autoScrollPointer.Y - _autoScrollOrigin.Y);
        if (vx == 0f && vy == 0f) return;   // back inside the dead zone: parked, not finished

        _autoScrollTarget.ScrollBy(vx * dt, vy * dt);

        // Speed in layout units per second from the pointer's displacement on one axis,
        // measured past the dead zone so the first unit of movement outside it crawls
        // rather than jumping.
        static float Ramp(float displacement)
        {
            float magnitude = Math.Abs(displacement) - AutoScrollDeadZone;
            return magnitude <= 0f ? 0f : Math.Sign(displacement) * magnitude * AutoScrollGain;
        }
    }

    /// <summary>The anchor an autoscroll is measured from: a ring at the dead zone's radius,
    /// so the circle you have to bring the pointer back into to stop is the one on screen.
    /// Drawn by the window rather than by the list, because it belongs to the gesture.</summary>
    private void RenderAutoScrollAnchor(IRenderContext ctx)
    {
        var theme = Application.Theme;
        float r = AutoScrollDeadZone;
        var box = new Rect(_autoScrollOrigin.X - r, _autoScrollOrigin.Y - r, r * 2, r * 2);
        ctx.DrawRoundedRect(box, new CornerRadius(r), theme.Surface, theme.Border, new Thickness(1f));
        ctx.DrawRoundedRect(
            new Rect(_autoScrollOrigin.X - 2f, _autoScrollOrigin.Y - 2f, 4f, 4f),
            new CornerRadius(2f), theme.TextColor, Color.Transparent, default);
    }

    /// <summary>
    /// After a style pass: the hovered element may want a different cursor now — a web page
    /// reports the one it wants just after the move that caused it — and the pointer need not
    /// move again for it to show. Only while the cursor on screen came from that element.
    /// </summary>
    private void ReapplyHoverCursor()
    {
        if (!_cursorFollowsHover || _lastHoveredElement is not { } hovered) return;
        if (_pointerCaptureTarget is not null || _isDragging || _autoScrollTarget is not null) return;
        var cursor = hovered.ResolvedInherited.Cursor;
        if (cursor == _hoverCursor) return;
        _hoverCursor = cursor;
        PlatformWindow.SetCursor(cursor);
    }

    /// <summary>
    /// Walks from the hit target up through ancestors looking for a ScrollViewElement
    /// whose scrollbar was clicked. Returns true if a drag was started or track was clicked.
    /// </summary>
    /// <summary>
    /// Moves the "pointer is on the bar" flag from whichever scroll view had it to this one.
    ///
    /// <para>One at a time: the bars do not overlap, and the walk above stops at the innermost
    /// match. Tracking the flag here rather than letting each view work it out is what makes
    /// the *leaving* case reliable — a view whose bar the pointer has left is, by definition,
    /// no longer on the path being walked, so it will never hear about it on its own.</para>
    /// </summary>
    private void SetScrollbarHover(IScrollbarDraggable? next, Point pos)
    {
        if (!ReferenceEquals(_scrollbarHoverTarget, next))
        {
            _scrollbarHoverTarget?.SetScrollbarHover(null);
            _scrollbarHoverTarget = next;
        }
        // Told on every move, not only on the change: an element with more than one bar needs
        // the position to know which of them the pointer is on now.
        next?.SetScrollbarHover(pos);
    }

    private bool TryStartScrollbarDrag(Element target, Point pos)
    {
        Element? current = target;
        while (current is not null)
        {
            if (current is IScrollbarDraggable sb && sb.TryStartScrollbarDrag(pos))
            {
                if (sb.IsDraggingScrollbar)
                    _scrollbarDragTarget = sb;
                return true;
            }
            current = current.Parent;
        }
        return false;
    }

    // ──── Drag ghost rendering ────

    private void RenderDragGhost(IRenderContext ctx)
    {
        if (_dragVisual is not null)
        {
            // Custom visual: resolve styles + layout once, then render at cursor
            if (!_dragVisualLayoutDone)
            {
                StyleResolver.Resolve(_dragVisual, Application.Theme);
                _layoutEngine.PerformLayout(_dragVisual, PlatformWindow.LayoutWidth, PlatformWindow.LayoutHeight, forceRootFill: false);
                _dragVisualLayoutDone = true;
            }

            float offsetX = _lastDragPos.X - _dragVisual.Layout.Width / 2;
            float offsetY = _lastDragPos.Y - _dragVisual.Layout.Height / 2;
            ctx.PushTranslate(offsetX, offsetY);
            ctx.PushOpacity(0.8f);
            ElementRenderer.RenderTree(_dragVisual, ctx);
            ctx.Pop();
            ctx.Pop();
        }
        else if (_dragSource is not null)
        {
            // Default: re-render the source element at cursor position
            float offsetX = _lastDragPos.X - _dragSource.Layout.AbsoluteX - _dragSource.Layout.Width / 2;
            float offsetY = _lastDragPos.Y - _dragSource.Layout.AbsoluteY - _dragSource.Layout.Height / 2;
            ctx.PushTranslate(offsetX, offsetY);
            ctx.PushOpacity(0.7f);
            ElementRenderer.RenderTree(_dragSource, ctx);
            ctx.Pop();
            ctx.Pop();
        }
    }

    // ──── Drag & Drop helpers ────

    private static Element? FindDraggableAncestor(Element? target)
    {
        var current = target;
        while (current is not null)
        {
            if (current.Draggable)
                return current;
            current = current.Parent;
        }
        return null;
    }

    private void StartInternalDrag(Point pos, KeyModifiers modifiers)
    {
        _dragSource = _potentialDragSource!;
        _potentialDragSource = null;
        _isDragging = true;
        _isExternalDrag = false;
        _dragData = new DragData();
        _dragHoveredElement = null;
        _dragAccepted = false;
        _dragModifiers = modifiers;

        var startEvt = new DragStartEvent { Position = pos, DragData = _dragData, Modifiers = modifiers };
        EventDispatcher.DispatchDirect(startEvt, _dragSource);

        // A DragStart handler is allowed to escalate the gesture to an OS-wide drag (see
        // DragFilesOut). That runs modally and consumes the rest of it, so there is nothing
        // left here to ghost, hover or end.
        if (_nativeDragRan)
        {
            _nativeDragRan = false;
            ResetDragState();
            // The modal loop swallowed the button release, so the press is still armed and
            // its :active styling still on — drop it rather than leave the row lit.
            _pointerState.CancelPress();
            PlatformWindow.SetCursor(CursorType.Default);
            return;
        }

        // Capture drag visual (custom or source element as default)
        _dragVisual = startEvt.DragVisual;
        _dragVisualLayoutDone = false;

        PlatformWindow.SetCursor(CursorType.Move);
        UpdateDragHover(pos, modifiers);
    }

    /// <summary>
    /// Hand <paramref name="files"/> to the rest of the desktop as a drag other applications
    /// can accept, and block until the drop resolves. Returns what the target did — or
    /// <see cref="DragEffect.None"/> when the drag was cancelled, or when the platform cannot
    /// start one (see <see cref="CanDragFilesOut"/>).
    /// </summary>
    /// <remarks>
    /// <para>Call it from a <c>DragStart</c> handler. That is the moment the framework has
    /// decided a drag is under way and the mouse button is still down, which is what the OS
    /// requires; calling it from anywhere else starts a drag with nothing holding it. Doing so
    /// replaces the in-window drag rather than running alongside it — no ghost is drawn, and no
    /// DragOver/Drop/DragEnd fires — because the OS owns the pointer for the whole gesture.</para>
    /// <para>Passing <see cref="DragEffect.Move"/> in <paramref name="allowed"/> promises to
    /// delete the source files if Move comes back: the target will not have done it. An app
    /// that does not want to make that promise should offer Copy (and Link) only.</para>
    /// </remarks>
    public DragEffect DragFilesOut(IReadOnlyList<string> files,
        DragEffect allowed = DragEffect.Copy | DragEffect.Link)
    {
        if (PlatformWindow is not INativeDragSource source || !source.CanDragFiles) return DragEffect.None;
        if (files.Count == 0) return DragEffect.None;

        _nativeDragRan = true;
        try { return source.DragFiles(files, allowed); }
        finally
        {
            // A drag started from anywhere but DragStart has no internal drag to hand back to,
            // so it tidies up here; StartInternalDrag clears the flag itself when it does.
            if (!_isDragging) _nativeDragRan = false;
        }
    }

    /// <summary>True when this platform can start a drag other applications can accept
    /// (Windows today). An app can hide the affordance where it reports false.</summary>
    public bool CanDragFilesOut => PlatformWindow is INativeDragSource { CanDragFiles: true };

    private void UpdateDragHover(Point pos, KeyModifiers modifiers)
    {
        if (_root is null || _dragData is null) return;
        _lastDragPos = pos;
        _dragModifiers = modifiers;

        var target = HitTester.HitTest(_root, pos);

        // Fire DragLeave / DragEnter when the hovered element changes
        if (target != _dragHoveredElement)
        {
            if (_dragHoveredElement is not null)
                EventDispatcher.Dispatch(new DragLeaveEvent { Position = pos, DragData = _dragData, Modifiers = modifiers }, _dragHoveredElement);

            _dragHoveredElement = target;
            _dragAccepted = false;

            if (target is not null)
                EventDispatcher.Dispatch(new DragEnterEvent { Position = pos, DragData = _dragData, Modifiers = modifiers }, target);
        }

        // Fire DragOver (bubbles) so ancestors can accept
        if (target is not null)
        {
            var overEvt = new DragOverEvent { Position = pos, DragData = _dragData, Modifiers = modifiers };
            EventDispatcher.Dispatch(overEvt, target);
            _dragAccepted = overEvt.Accepted;
        }
        else
        {
            _dragAccepted = false;
        }

        // A handler is allowed to rebuild — highlighting the row under the cursor is the
        // obvious thing to do with a DragEnter — and a rebuild detaches the element we just
        // hit. Re-resolve so the remembered hover stays a live node: everything that fires
        // later against it (the DragLeave pairing this enter, above all) reaches the tree
        // rather than an orphan whose parent chain leads nowhere.
        if (_dragHoveredElement is { Parent: null } && !ReferenceEquals(_dragHoveredElement, _root))
            _dragHoveredElement = HitTester.HitTest(_root, pos);

        PlatformWindow.SetCursor(_dragAccepted ? CursorType.Move : CursorType.NotAllowed);
    }

    private void EndInternalDrag(Point pos, KeyModifiers modifiers)
    {
        if (_root is null || _dragData is null) { ResetDragState(); return; }
        _dragModifiers = modifiers;

        var target = HitTester.HitTest(_root, pos);

        // Fire final DragOver to confirm acceptance
        bool accepted = false;
        if (target is not null)
        {
            var overEvt = new DragOverEvent { Position = pos, DragData = _dragData, Modifiers = modifiers };
            EventDispatcher.Dispatch(overEvt, target);
            accepted = overEvt.Accepted;

            if (accepted)
                EventDispatcher.Dispatch(new DropEvent { Position = pos, DragData = _dragData, Modifiers = modifiers }, target);
        }

        // Fire DragLeave on previously hovered element
        if (_dragHoveredElement is not null)
            EventDispatcher.Dispatch(new DragLeaveEvent { Position = pos, DragData = _dragData, Modifiers = modifiers }, _dragHoveredElement);

        // Fire DragEnd on the source
        if (_dragSource is not null)
            EventDispatcher.DispatchDirect(new DragEndEvent { Position = pos, DragData = _dragData, Modifiers = modifiers, WasAccepted = accepted }, _dragSource);

        // Restore cursor
        var cursorTarget = HitTester.HitTest(_root, pos);
        PlatformWindow.SetCursor(cursorTarget?.ResolvedInherited.Cursor ?? CursorType.Default);

        ResetDragState();
    }

    /// <summary>
    /// Finish an OS drag. Deliberately the same shape as <see cref="EndInternalDrag"/>: the
    /// drop target is hit-tested afresh at the last position and re-asked whether it accepts,
    /// rather than trusting the element remembered from the last hover.
    ///
    /// <para>That element is not reliably still in the tree. A DragEnter/DragOver handler is
    /// allowed to rebuild — highlighting the folder under the cursor is the obvious thing to
    /// do with one — and a rebuild replaces the element the hover recorded. Dispatching the
    /// drop at that detached node used to deliver it to an orphan, where it bubbled through a
    /// parentless chain and reached none of the live ancestors that had accepted it: the drag
    /// visibly highlighted its target and then dropped into nothing.</para>
    /// </summary>
    /// <summary>The drag left the window, or was called off: the hovered element is told it
    /// left, and nobody gets a drop.</summary>
    private void CancelExternalDrag()
    {
        if (_dragHoveredElement is not null && _dragData is not null)
            EventDispatcher.Dispatch(new DragLeaveEvent { Position = _lastDragPos, DragData = _dragData, Modifiers = _dragModifiers }, _dragHoveredElement);
        PlatformWindow.SetCursor(CursorType.Default);
        ResetDragState();
    }

    /// <summary>Delivers an OS drop; true when an element took it.</summary>
    private bool EndExternalDrag(KeyModifiers modifiers)
    {
        bool dropped = false;
        // The last drop-position update is what carried a modifier sample; a platform
        // that reports none on the release itself keeps whatever the drag was holding.
        if (modifiers == KeyModifiers.None) modifiers = _dragModifiers;

        if (_root is not null && _dragData is not null)
        {
            var target = HitTester.HitTest(_root, _lastDragPos);
            if (target is not null)
            {
                var overEvt = new DragOverEvent { Position = _lastDragPos, DragData = _dragData, Modifiers = modifiers };
                EventDispatcher.Dispatch(overEvt, target);
                if (overEvt.Accepted)
                {
                    EventDispatcher.Dispatch(new DropEvent { Position = _lastDragPos, DragData = _dragData, Modifiers = modifiers }, target);
                    dropped = true;
                }
            }

            // Leave goes to whatever the hover last recorded — that is the element whose
            // enter it paired with, even if a rebuild has since detached it.
            if (_dragHoveredElement is not null)
                EventDispatcher.Dispatch(new DragLeaveEvent { Position = _lastDragPos, DragData = _dragData, Modifiers = modifiers }, _dragHoveredElement);
        }

        PlatformWindow.SetCursor(CursorType.Default);
        ResetDragState();
        return dropped;
    }

    private void ResetDragState()
    {
        _potentialDragSource = null;
        _dragSource = null;
        _isDragging = false;
        _isExternalDrag = false;
        _dragData = null;
        _dragHoveredElement = null;
        _dragVisual = null;
        _dragVisualLayoutDone = false;
        _dragAccepted = false;
        _dragModifiers = KeyModifiers.None;
    }

    // Dirty flags always propagate upward (Mark*Dirty walks to the root, and
    // MarkStyleDirtyDown flags the node it starts from too), so a clean element
    // can never have a dirty descendant. Both clears prune on that invariant,
    // making the per-frame cost O(dirty subtree) instead of O(whole tree).
    private static void ClearFlags(Element element, DirtyFlags flags)
    {
        if ((element.DirtyFlags & flags) == 0) return;
        element.DirtyFlags &= ~flags;
        // By index: Children is an interface, and foreach over it boxes an enumerator,
        // 40 bytes per dirty container per frame (plan 17, runtime-frame-floor).
        var children = element.Children;
        for (int i = 0; i < children.Count; i++)
            ClearFlags(children[i], flags);
    }

    /// <summary>
    /// Best-case damage for this frame: the union of the deepest paint-dirty elements
    /// (flags propagate to the root, so the deepest carriers are the ones actually
    /// marked — see <see cref="DamageProbe"/> for why this overstates what damage
    /// rects could save, never understates it). Runs only when a probe is attached
    /// or a trace is recording.
    /// </summary>
    private void CollectDamage(bool layoutWasDirty, bool styleWasDirty)
    {
        var probe = DamageProbe.OnFrame;
        if ((probe is null && !_tracer.IsRecording) || _root is null) return;

        float ux0 = float.MaxValue, uy0 = float.MaxValue, ux1 = float.MinValue, uy1 = float.MinValue;
        float rectArea = 0f;
        int count = 0;

        void Add(Rect r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            ux0 = Math.Min(ux0, r.X); uy0 = Math.Min(uy0, r.Y);
            ux1 = Math.Max(ux1, r.Right); uy1 = Math.Max(uy1, r.Bottom);
            rectArea += r.Width * r.Height;
            count++;
        }

        void Visit(Element e, float dx, float dy)
        {
            if ((e.DirtyFlags & DirtyFlags.Paint) == 0) return;
            bool descended = false;
            foreach (var child in e.Children)
                if ((child.DirtyFlags & DirtyFlags.Paint) != 0)
                {
                    Visit(child, dx, dy);
                    descended = true;
                }
            if (!descended)
            {
                var b = e.Layout.AbsoluteBorderBox;
                Add(new Rect(b.X + dx, b.Y + dy, b.Width, b.Height));
            }
        }

        Visit(_root, 0, 0);
        foreach (var overlay in _overlays)
            Visit(overlay.Content, overlay.X, overlay.Y);

        float unionArea = count == 0 ? 0 : (ux1 - ux0) * (uy1 - uy0);
        var frame = new DamageProbe.DamageFrame(
            PlatformWindow.LayoutWidth * PlatformWindow.LayoutHeight,
            unionArea, rectArea, count, layoutWasDirty, styleWasDirty);

        probe?.Invoke(frame);
        if (_tracer.IsRecording)
            _tracer.Annotate("Damage",
                $"union={frame.UnionFraction:P1} rects={count} rectArea={frame.RectFraction:P1}"
                + (layoutWasDirty ? " layout" : "") + (styleWasDirty ? " style" : ""));
    }

    private static void ClearAllFlags(Element element)
    {
        if (element.DirtyFlags == DirtyFlags.None) return;
        element.DirtyFlags = DirtyFlags.None;
        var children = element.Children;   // by index, as in ClearFlags
        for (int i = 0; i < children.Count; i++)
            ClearAllFlags(children[i]);
    }

    public void Close()
    {
        PlatformWindow.Close();
        Closed?.Invoke();
    }

    internal void NotifyClosed() => Close();

    // ──── Overlay system (IOverlayHost) ────

    public OverlayHandle ShowOverlay(Element content, float x, float y, bool dismissOnOutsideClick = true, bool hitTestable = true, Action? onDismissed = null, bool modal = false) =>
        Show(new OverlayEntry
        {
            Id = _nextOverlayId++,
            Content = content,
            X = x,
            Y = y,
            DismissOnOutsideClick = dismissOnOutsideClick,
            IsHitTestable = hitTestable,
            IsModal = modal,
            OnDismissed = onDismissed,
            FocusBefore = _focusManager.FocusedElement,
        });

    public OverlayHandle ShowAnchoredOverlay(Element content, Element anchor, Func<Size, Size, Point> place,
        bool dismissOnOutsideClick = true, Action? onDismissed = null, bool modal = false) =>
        Show(new OverlayEntry
        {
            Id = _nextOverlayId++,
            Content = content,
            DismissOnOutsideClick = dismissOnOutsideClick,
            IsModal = modal,
            OnDismissed = onDismissed,
            FocusBefore = _focusManager.FocusedElement,
            Anchor = anchor,
            Place = place,
        });

    private OverlayHandle Show(OverlayEntry entry)
    {
        var content = entry.Content;

        // Set overlay host so elements inside the overlay can show/close overlays
        content.OverlayHost = this;
        // And the window's focus, as the root has it, so Focus() works inside: a dialog's field
        // can take the keyboard. (Style resolution hands it down to the content's children.)
        content.FocusManager = _focusManager;

        // Resolve style and layout immediately so we can clamp to viewport
        var theme = Application.Theme;
        StyleResolver.Resolve(content, theme);
        entry.StyleResolved = true;

        _layoutEngine.PerformLayout(content, PlatformWindow.LayoutWidth, PlatformWindow.LayoutHeight, forceRootFill: false);
        entry.LayoutDone = true;

        if (entry.Place is not null)
        {
            PlaceOverlay(entry);
        }
        else
        {
            // Clamp to viewport so the overlay stays on screen
            float vw = PlatformWindow.LayoutWidth;
            float vh = PlatformWindow.LayoutHeight;
            float x = entry.X + content.Layout.Width > vw ? Math.Max(0, vw - content.Layout.Width) : entry.X;
            float y = entry.Y + content.Layout.Height > vh ? Math.Max(0, vh - content.Layout.Height) : entry.Y;
            MoveOverlay(entry, x, y);
        }

        _overlays.Add(entry);
        _root?.MarkPaintDirty();
        return new OverlayHandle { Id = entry.Id };
    }

    /// <summary>Puts an overlay at a point, and tells its tree where that is, so the window boxes
    /// of the elements in it (<see cref="Element.WindowBorderBox"/>) are where they are drawn.</summary>
    private static void MoveOverlay(OverlayEntry entry, float x, float y)
    {
        entry.X = x;
        entry.Y = y;
        entry.Content.OverlayOrigin = new Point(x, y);
    }

    /// <summary>Asks an anchored overlay where it goes now: its anchor may have scrolled, the
    /// window may have changed size, or its own content may have grown.</summary>
    private void PlaceOverlay(OverlayEntry entry)
    {
        var content = entry.Content;
        var at = entry.Place!(new Size(content.Layout.Width, content.Layout.Height),
            new Size(PlatformWindow.LayoutWidth, PlatformWindow.LayoutHeight));
        MoveOverlay(entry, at.X, at.Y);
    }

    public void CloseOverlay(OverlayHandle handle)
    {
        for (int i = _overlays.Count - 1; i >= 0; i--)
        {
            if (_overlays[i].Id == handle.Id)
            {
                RemoveOverlayAt(i);
                _root?.MarkPaintDirty();
                return;
            }
        }
    }

    /// <summary>
    /// Removes an overlay. If the focus was inside it, it goes back to what had it when the
    /// overlay opened, while that is still in the window; otherwise nowhere. Left on an element
    /// that has gone, the keys would go to it and reach nothing.
    /// </summary>
    private void RemoveOverlayAt(int index)
    {
        var entry = _overlays[index];
        _overlays.RemoveAt(index);
        if (_focusManager.FocusedElement is not { } focused || TopOf(focused) != entry.Content) return;
        _focusManager.SetFocus(entry.FocusBefore is { } before && IsInWindow(before) ? before : null);

        static Element TopOf(Element element)
        {
            while (element.Parent is { } parent) element = parent;
            return element;
        }

        bool IsInWindow(Element element)
        {
            var top = TopOf(element);
            return top == _root || _overlays.Exists(o => o.Content == top);
        }
    }

    // ──── Where the keyboard is ────
    //
    // A modal overlay holds the keyboard: keys go to it, and Tab walks only its controls. Any
    // other overlay is a scope for Tab once it holds the focus — Tab from a field in a popover
    // cycles in the popover rather than jumping to the first control of the page underneath —
    // and otherwise keys simply follow the focus, as they always have.

    /// <summary>Index of the overlay whose tree holds <paramref name="element"/>; -1 for the
    /// main tree, and -2 for an element in neither, which the window no longer shows.</summary>
    private int OverlayIndexOf(Element element)
    {
        var top = element;
        while (top.Parent is { } parent) top = parent;
        if (top == _root) return -1;
        for (int i = _overlays.Count - 1; i >= 0; i--)
            if (_overlays[i].Content == top) return i;
        return -2;
    }

    private int TopmostModalIndex()
    {
        for (int i = _overlays.Count - 1; i >= 0; i--)
            if (_overlays[i].IsModal) return i;
        return -1;
    }

    /// <summary>The tree Tab walks: the overlay holding the focus, unless a modal one is above
    /// it; else the topmost modal overlay; else the main tree.</summary>
    private Element? FocusScope()
    {
        int modal = TopmostModalIndex();
        int holding = _focusManager.FocusedElement is { } focused ? OverlayIndexOf(focused) : -1;
        if (holding >= 0 && holding >= modal) return _overlays[holding].Content;
        if (modal >= 0) return _overlays[modal].Content;
        return _root;
    }

    /// <summary>Where a key goes: the focused element — unless a modal overlay is open and the
    /// focus is not in it or above it (nothing focused, or something behind it focused from
    /// code), in which case the modal overlay has it, and nothing behind the dialog acts on it.</summary>
    private Element? KeyTarget()
    {
        var focused = _focusManager.FocusedElement;
        int modal = TopmostModalIndex();
        if (modal >= 0 && (focused is null || OverlayIndexOf(focused) < modal))
            return _overlays[modal].Content;
        return focused ?? _root;
    }

    public void UpdateOverlayPosition(OverlayHandle handle, float x, float y)
    {
        for (int i = 0; i < _overlays.Count; i++)
        {
            if (_overlays[i].Id == handle.Id)
            {
                MoveOverlay(_overlays[i], x, y);
                _root?.MarkPaintDirty();
                return;
            }
        }
    }

    /// <summary>
    /// The two-ring focus visual around whatever has focus, if it asked for one.
    ///
    /// <para>Drawn by the window rather than by the element, because it sits <i>outside</i> the
    /// element's bounds: an element painting it into its own subtree would have it clipped by
    /// any scroll view or overflow-hidden ancestor, and covered by whatever is painted after it.
    /// Everything else about it is the theme's business.</para>
    ///
    /// <para>Rings are drawn from the control's edge outwards — the thin inner one first, then
    /// the thick outer one around it. Skia centres a stroke on the path, so each rect is
    /// inflated to put the band where it belongs, and the corner radius is inflated with it so
    /// the rings stay concentric with the control instead of squaring off at the corners.</para>
    /// </summary>
    private void RenderFocusVisual(IRenderContext ctx)
    {
        if (_focusManager.FocusedElement is not { } focused || !focused.ShowsFocusVisual)
            return;

        var fv = Application.Theme.FocusVisual;
        if (fv.KeyboardOnly && !_focusFromKeyboard) return;

        var box = focused.WindowBorderBox;
        if (box.Width <= 0 || box.Height <= 0) return;

        float radius = focused is BoxElement b ? b.CornerRadius.TopLeft : 0f;

        float innerMid = fv.Margin + fv.InnerThickness / 2f;
        float outerMid = fv.Margin + fv.InnerThickness + fv.OuterThickness / 2f;

        Draw(innerMid, fv.InnerThickness, fv.InnerColor);
        Draw(outerMid, fv.OuterThickness, fv.OuterColor);

        void Draw(float inflate, float thickness, Color color)
        {
            var rect = new Rect(
                box.X - inflate, box.Y - inflate,
                box.Width + inflate * 2f, box.Height + inflate * 2f);
            ctx.DrawRoundedRect(rect, new CornerRadius(radius > 0 ? radius + inflate : 0f),
                Color.Transparent, color, new Thickness(thickness));
        }
    }

    private void RenderOverlays(IRenderContext ctx)
    {
        var theme = Application.Theme;
        foreach (var overlay in _overlays)
        {
            var content = overlay.Content;

            if (!overlay.StyleResolved || (content.DirtyFlags & DirtyFlags.Style) != 0)
            {
                StyleResolver.Resolve(content, theme);
                overlay.StyleResolved = true;
            }

            if (!overlay.LayoutDone || (content.DirtyFlags & DirtyFlags.Layout) != 0)
            {
                _layoutEngine.PerformLayout(content,
                    PlatformWindow.LayoutWidth, PlatformWindow.LayoutHeight, forceRootFill: false);
                overlay.LayoutDone = true;
            }

            // Every frame, not only on a relayout: the anchor moves with the page under it.
            if (overlay.Place is not null)
                PlaceOverlay(overlay);

            ctx.PushTranslate(overlay.X, overlay.Y);
            ElementRenderer.RenderTree(content, ctx);
            ctx.Pop();
        }
    }

    private (Element? element, Point offset) HitTestOverlays(Point pos)
    {
        // Test back-to-front: last overlay = topmost, checked first
        for (int i = _overlays.Count - 1; i >= 0; i--)
        {
            var overlay = _overlays[i];
            if (!overlay.IsHitTestable) continue;
            var localPoint = new Point(pos.X - overlay.X, pos.Y - overlay.Y);
            var hit = HitTester.HitTest(overlay.Content, localPoint);
            if (hit is not null)
                return (hit, new Point(overlay.X, overlay.Y));
        }
        return (null, default);
    }

    /// <summary>Whether <paramref name="pos"/> is on the anchor of a light-dismiss overlay above
    /// level <paramref name="above"/> (-1 for the main tree).</summary>
    private bool PressIsOnAnchorAbove(Point pos, int above)
    {
        for (int i = _overlays.Count - 1; i > above; i--)
        {
            var overlay = _overlays[i];
            if (overlay.DismissOnOutsideClick && overlay.Anchor is { IsHosted: true } anchor
                && anchor.WindowBorderBox.Contains(pos))
                return true;
        }
        return false;
    }

    /// <summary>Closes the light-dismiss overlays above level <paramref name="above"/>: all of
    /// them for a press in the main tree (-1), only anchored ones for a press in an overlay.</summary>
    private void DismissOverlaysOnOutsideClick(int above = -1)
    {
        for (int i = _overlays.Count - 1; i > above; i--)
        {
            // A dismissal callback can close other overlays.
            if (i >= _overlays.Count) continue;
            if (_overlays[i].DismissOnOutsideClick && (above < 0 || _overlays[i].Anchor is not null))
            {
                // Clear context menu state if this overlay is the context menu
                if (_overlays[i].Id == _contextMenuOverlay.Id)
                {
                    _contextMenuOverlay = default;
                    _contextMenuElement = null;
                    _contextMenuNavigate = null;
                    NotifyContextMenuClosed(MenuCloseReason.Dismissed);
                }
                if (_overlays[i].Id == _contextSubmenuOverlay.Id)
                {
                    _contextSubmenuOverlay = default;
                    _contextSubmenuElement = null;
                }
                var dismissed = _overlays[i].OnDismissed;
                RemoveOverlayAt(i);
                _root?.MarkPaintDirty();
                dismissed?.Invoke();
            }
        }
    }

    // ──── Tooltip ────

    private void UpdateTooltipHover(Element? hitTarget)
    {
        var (owner, text) = FindTooltipOwner(hitTarget);

        // Same owner — nothing to do
        if (owner == _tooltipOwner) return;

        // Owner changed — hide current tooltip/cancel pending timer
        HideTooltip();

        if (owner is null || text is null) return;

        ScheduleTooltip(owner, text);
    }

    // A method of its own: the show callback captures owner and text, and C# allocates a
    // captured local's closure where the local's scope begins. In UpdateTooltipHover that was
    // its first line, which every pointer move runs, tooltip or not.
    private void ScheduleTooltip(Element owner, string text)
    {
        // Start show delay
        _tooltipOwner = owner;
        var theme = Application.Theme;
        float delay = theme.Tooltip.ShowDelay;
        // A timer: the loop wakes once for it, not every frame interval of the delay.
        _tooltipDelayTween = Animate.After(delay, () => ShowTooltipOverlay(owner, text));
    }

    private void ShowTooltipOverlay(Element owner, string text)
    {
        _tooltipDelayTween = null;

        // Owner may have been removed from tree during the delay
        if (owner.OverlayHost is null) return;

        var theme = Application.Theme;
        var tt = theme.Tooltip;

        var tooltip = BoxElement.New()
            .Background(tt.Background)
            .CornerRadius(tt.CornerRadius)
            .Padding(tt.Padding)
            .BorderColor(tt.BorderColor ?? Color.Transparent)
            .BorderWidth(tt.BorderColor is null ? 0f : tt.BorderWidth)
            .Children(
                TextElement.New(text)
                    .FontSize(tt.FontSize)
                    .TextColor(tt.TextColor)
            );

        // Position below the owner element. Window box rather than layout box: a tooltip on a
        // row in a scrolled list would otherwise appear beside where that row would be if
        // nothing had scrolled, and one in a popover where the popover's corner is.
        var ownerBox = owner.WindowContentBox;
        float x = ownerBox.X;
        float y = ownerBox.Y + owner.Layout.Height + tt.Gap;

        _tooltipOverlay = ShowOverlay(tooltip, x, y, dismissOnOutsideClick: false, hitTestable: false);
    }

    private void HideTooltip()
    {
        if (_tooltipDelayTween is { } tween)
        {
            tween.Cancel();
            _tooltipDelayTween = null;
        }

        if (_tooltipOverlay.IsValid)
        {
            CloseOverlay(_tooltipOverlay);
            _tooltipOverlay = default;
        }

        _tooltipOwner = null;
    }

    private static (Element? owner, string? text) FindTooltipOwner(Element? element)
    {
        var current = element;
        while (current is not null)
        {
            if (current.TooltipText is not null)
                return (current, current.TooltipText);
            current = current.Parent;
        }
        return (null, null);
    }

    // ──── Context Menu ────

    /// <summary>Open <paramref name="target"/>'s context menu at <paramref name="pos"/>
    /// programmatically (e.g. from a left-click dropdown affordance), as if it had been
    /// right-clicked. No-op if the target (or an ancestor) has no context menu.</summary>
    public void OpenContextMenu(Element target, Point pos) => TryShowContextMenu(target, pos);

    /// <summary>
    /// Open a context menu of <paramref name="items"/> at <paramref name="pos"/>, for content
    /// that decides its menu itself and after the click that asked for it — a web page's
    /// engine answers a right-click a moment later, with the page's own items.
    /// </summary>
    /// <param name="items">What to show. Nothing opens for an empty list, and
    /// <paramref name="onClosed"/> still runs.</param>
    /// <param name="pos">Top-left corner, in window coordinates.</param>
    /// <param name="onClosed">Runs once, when the menu has closed for any reason — an item
    /// chosen, a click outside, Escape, another menu opening — and after the chosen item's
    /// action, so an owner waiting for a choice can treat a close with none as a dismissal.</param>
    public void OpenContextMenu(IReadOnlyList<ContextMenuItem> items, Point pos, Action? onClosed = null)
    {
        if (items.Count == 0)
        {
            if (onClosed is not null) Application.InvokeOnMainThread(onClosed);
            return;
        }
        ShowContextMenu(items, pos, onClosed is null ? null : _ => onClosed());
    }

    /// <inheritdoc/>
    /// <remarks>The menu is a context menu with an owner: it is told why the menu closed, and
    /// gets ← and → when the menu has no submenu to close or open — which is how a menu bar
    /// walks from one menu to the next.</remarks>
    public void OpenMenu(IReadOnlyList<ContextMenuItem> items, Point position, MenuRequest? request = null)
    {
        if (items.Count == 0)
        {
            if (request?.Closed is { } closed) Application.InvokeOnMainThread(() => closed(MenuCloseReason.Dismissed));
            return;
        }
        ShowContextMenu(items, position, request?.Closed);
        _contextMenuNavigate = request?.Navigate;
        if (request?.HighlightFirst == true) _contextMenuElement?.MoveSelection(1);
    }

    /// <inheritdoc/>
    public void CloseMenu() => CloseContextMenu(MenuCloseReason.Dismissed);

    private void TryShowContextMenu(Element target, Point pos)
    {
        var (owner, itemsProvider) = FindContextMenuOwner(target);
        if (owner is null || itemsProvider is null) return;

        var items = itemsProvider();
        if (items.Count == 0) return;
        ShowContextMenu(items, pos, onClosed: null);
    }

    private void ShowContextMenu(IReadOnlyList<ContextMenuItem> items, Point pos, Action<MenuCloseReason>? onClosed)
    {
        // Close any existing context menu first
        CloseContextMenu(MenuCloseReason.Replaced);
        _contextMenuClosed = onClosed;

        var theme = Application.Theme;
        var menuElement = new ContextMenuElement(items, theme.ContextMenu, () => CloseContextMenu(MenuCloseReason.Chosen));
        menuElement.HoverChanged = index => OnContextMenuHover(menuElement, index);
        _contextMenuElement = menuElement;
        _contextMenuOverlay = ShowOverlay(menuElement, pos.X, pos.Y, dismissOnOutsideClick: true, hitTestable: true);
    }

    /// <summary>Hover moved to a new root-menu item: open its flyout when it has
    /// children (anchored next to the item), close any open flyout otherwise.</summary>
    private void OnContextMenuHover(ContextMenuElement menu, int index)
    {
        if (!ReferenceEquals(menu, _contextMenuElement)) return;

        var item = menu.ItemAt(index);
        if (item is not { HasChildren: true })
        {
            CloseContextSubmenu();
            return;
        }
        if (menu.PinnedIndex == index) return;   // this item's flyout is already open

        CloseContextSubmenu();
        var theme = Application.Theme;
        var sub = new ContextMenuElement(item.Children!, theme.ContextMenu, () => CloseContextMenu(MenuCloseReason.Chosen));
        _contextSubmenuElement = sub;
        menu.PinnedIndex = index;

        float rootX = 0, rootY = 0;
        foreach (var o in _overlays)
            if (o.Id == _contextMenuOverlay.Id) { rootX = o.X; rootY = o.Y; break; }
        // Slight overlap keeps the pointer inside a menu while crossing over;
        // the first child row lines up with the parent item.
        _contextSubmenuOverlay = ShowOverlay(sub,
            rootX + menu.Layout.Width - 4,
            rootY + menu.ItemTop(index) - theme.ContextMenu.Padding.Top,
            dismissOnOutsideClick: true, hitTestable: true);
    }

    private void CloseContextSubmenu()
    {
        if (_contextSubmenuOverlay.IsValid)
        {
            CloseOverlay(_contextSubmenuOverlay);
            _contextSubmenuOverlay = default;
        }
        _contextSubmenuElement = null;
        if (_contextMenuElement is not null)
            _contextMenuElement.PinnedIndex = -1;
    }

    private void CloseContextMenu(MenuCloseReason reason = MenuCloseReason.Dismissed)
    {
        CloseContextSubmenu();
        if (_contextMenuOverlay.IsValid)
        {
            CloseOverlay(_contextMenuOverlay);
            _contextMenuOverlay = default;
        }
        _contextMenuElement = null;
        _contextMenuNavigate = null;
        NotifyContextMenuClosed(reason);
    }

    /// <summary>
    /// Tells the owner of the menu that just closed. Posted, not called: a chosen item closes
    /// the menu first and runs its action after, and the owner must see the action before the
    /// close, or it would take every choice for a dismissal.
    /// </summary>
    private void NotifyContextMenuClosed(MenuCloseReason reason)
    {
        var closed = _contextMenuClosed;
        _contextMenuClosed = null;
        if (closed is not null) Application.InvokeOnMainThread(() => closed(reason));
    }

    private static (Element? owner, Func<IReadOnlyList<ContextMenuItem>>? itemsProvider) FindContextMenuOwner(Element? element)
    {
        var current = element;
        while (current is not null)
        {
            if (current.ContextMenuItems is not null)
                return (current, current.ContextMenuItems);
            current = current.Parent;
        }
        return (null, null);
    }

    // ──── Window chrome hit test ────

    private int HandleWindowHitTest(int x, int y)
    {
        // x, y arrive already converted to layout units (see SDL3Window's hit
        // test callback), so compare against the layout viewport, not pixels.
        int w = (int)PlatformWindow.LayoutWidth;
        int h = (int)PlatformWindow.LayoutHeight;
        int border = ResizeBorderWidth;

        // Don't show resize handles when maximized
        if (!IsMaximized)
        {
            var edges = _resizableEdges;
            bool left = (edges & ResizeEdges.Left) != 0 && x < border;
            bool right = (edges & ResizeEdges.Right) != 0 && x >= w - border;
            bool top = (edges & ResizeEdges.Top) != 0 && y < border;
            bool bottom = (edges & ResizeEdges.Bottom) != 0 && y >= h - border;

            if (top && left) return 2;     // SDL_HITTEST_RESIZE_TOPLEFT
            if (top && right) return 4;    // SDL_HITTEST_RESIZE_TOPRIGHT
            if (bottom && left) return 8;  // SDL_HITTEST_RESIZE_BOTTOMLEFT
            if (bottom && right) return 6; // SDL_HITTEST_RESIZE_BOTTOMRIGHT
            if (top) return 3;             // SDL_HITTEST_RESIZE_TOP
            if (bottom) return 7;          // SDL_HITTEST_RESIZE_BOTTOM
            if (left) return 9;            // SDL_HITTEST_RESIZE_LEFT
            if (right) return 5;           // SDL_HITTEST_RESIZE_RIGHT
        }

        return IsWindowDragRegion(x, y) ? 1 /* DRAGGABLE */ : 0 /* NORMAL */;
    }

    /// <summary>
    /// True when the given layout-unit point falls in a window drag region and
    /// not over an interactive element. Used by both the SDL hit-test path and
    /// the Windows native frame to decide the caption/drag area. If the hit
    /// element itself has event handlers (e.g. a button in the title bar), the
    /// click belongs to it, not to window dragging.
    /// </summary>
    private bool IsWindowDragRegion(int x, int y)
    {
        if (_root is null) return false;
        var hit = HitTester.HitTest(_root, new Point(x, y));
        return hit is not null && !hit.HasAnyEventHandlers && HasWindowDragAncestor(hit);
    }

    /// <summary>True when the layout-unit point falls on the element flagged as the
    /// window maximize button. The native frame reports HTMAXBUTTON there so Windows
    /// shows its Snap Layouts flyout on hover.</summary>
    private bool IsWindowMaximizeRegion(int x, int y) => HitMaximizeButton(x, y) is not null;

    private Element? HitMaximizeButton(int x, int y)
    {
        if (_root is null) return null;
        var hit = HitTester.HitTest(_root, new Point(x, y));
        for (var el = hit; el is not null; el = el.Parent)
            if (el.IsWindowMaximizeButton) return el;
        return null;
    }

    // Cached result of the last full-tree search. The button is looked up every
    // frame (caption height) and from non-client callbacks, so an O(n) walk per
    // call adds up in large trees. The cache is trusted only while the element
    // still carries the flag and is still attached under the current root —
    // reconciliation can detach or repurpose it at any time.
    private Element? _maxButtonCache;

    private Element? FindMaximizeButton()
    {
        if (_root is null) return null;
        if (_maxButtonCache is { IsWindowMaximizeButton: true } cached && IsAttachedToRoot(cached))
            return cached;
        _maxButtonCache = FindMaximizeButton(_root);
        return _maxButtonCache;
    }

    private bool IsAttachedToRoot(Element e)
    {
        for (Element? cur = e; cur is not null; cur = cur.Parent)
            if (ReferenceEquals(cur, _root)) return true;
        return false;
    }

    private static Element? FindMaximizeButton(Element e)
    {
        if (e.IsWindowMaximizeButton) return e;
        var children = e.Children;   // by index: foreach over the interface boxes an enumerator
        for (int i = 0; i < children.Count; i++)
            if (FindMaximizeButton(children[i]) is { } found) return found;
        return null;
    }

    // The maximize button lives in the non-client area (it returns HTMAXBUTTON), so
    // Windows sends WM_NCMOUSEMOVE there instead of client moves — the normal hover
    // pipeline never sees it. Drive the button's hover highlight from the frame's NC
    // tracking by feeding the pointer state directly, so HoverBg still lights up.
    private Element? _maxButtonHovered;
    private Color? _maxButtonHoverBg;   // the hover background the app applied, re-asserted each frame

    // Drive the maximize button's hover directly (enter/leave dispatched to the
    // button), NOT through _pointerState. The button is non-client, so the shared
    // pointer-hover state belongs to the client pipeline; mixing the two made the
    // client's hover updates fire leave on the button (blanking the highlight while
    // _maxButtonHovered stayed set) and popped the app tooltip.
    private void SetMaximizeButtonHover(bool hovered)
    {
        if (hovered)
        {
            if (FindMaximizeButton() is not BoxElement btn || ReferenceEquals(btn, _maxButtonHovered)) return;
            _maxButtonHovered = btn;
            btn.SetInteractionHovered(true);
            // Both, and in this order. Declarative hover styling (.OnHover, which is what
            // HoverBg compiles to) is applied by the interaction state, not by handlers,
            // so dispatching the event alone silently stopped highlighting this button
            // when HoverBg went declarative. Apps with imperative OnPointerEnter handlers
            // still need the event.
            EventDispatcher.DispatchDirect(new PointerEnterEvent { Position = default, Modifiers = KeyModifiers.None }, btn);
            // Capture the colour the app's hover styling just applied so we can
            // re-assert it after rebuilds (see ReassertMaximizeButtonHover).
            _maxButtonHoverBg = btn.BackgroundColor;
        }
        else
        {
            if (_maxButtonHovered is not { } old) return;
            _maxButtonHovered = null;
            _maxButtonHoverBg = null;
            old.SetInteractionHovered(false);
            EventDispatcher.DispatchDirect(new PointerLeaveEvent { Position = default, Modifiers = KeyModifiers.None }, old);
        }
        _root?.MarkPaintDirty();
    }

    /// <summary>True when the element is (or is inside) the maximize button while the
    /// Windows custom frame owns it as non-client. The client pointer pipeline skips
    /// it so it doesn't double-drive the hover or pop the app tooltip over the flyout.</summary>
    private bool IsNonClientMaximizeButton(Element? el)
    {
        if (_chrome is not { UsesWindowsNativeFrame: true }) return false;
        for (var e = el; e is not null; e = e.Parent)
            if (e.IsWindowMaximizeButton) return true;
        return false;
    }

    // Keeps the Windows custom frame's caption region tall enough to contain the
    // maximize button, so Windows offers Snap Layouts when the window isn't
    // maximized. Recomputed from the button's laid-out bounds; the platform side
    // no-ops when the value is unchanged or the frame isn't the Windows one.
    private void UpdateCaptionHeight()
    {
        if (_chrome is not { UsesWindowsNativeFrame: true } chrome) return;
        if (FindMaximizeButton() is not { } btn) return;
        var bottomDip = btn.Layout.AbsoluteBorderBox.Bottom;
        if (bottomDip <= 0) return;
        int px = (int)MathF.Ceiling(bottomDip * PlatformWindow.DisplayScale) + 1;
        chrome.SetCaptionHeightPx(px);
    }

    /// <summary>Re-applies the maximize button's hover background each rendered frame
    /// while it's hovered in the non-client area. A rebuild's PatchFrom resets the
    /// button's background to its built (transparent) value, and — with the pointer
    /// held still over the button to summon Snap Layouts — no further NC mouse-move
    /// arrives to restore it, so the highlight would blink off. Called before paint.</summary>
    private void ReassertMaximizeButtonHover()
    {
        if (_maxButtonHovered is null || _maxButtonHoverBg is not Color bg) return;
        if (FindMaximizeButton() is BoxElement btn && btn.BackgroundColor != bg)
        {
            btn.BackgroundColor = bg;
            btn.MarkPaintDirty();
        }
    }

    private static bool HasWindowDragAncestor(Element element)
    {
        // Walk from the hit element up toward the root. If we find an element with
        // event handlers before finding a drag region, the click belongs to that
        // interactive element (e.g. a button inside the title bar), not the drag region.
        Element? current = element;
        while (current is not null)
        {
            if (current.IsWindowDragRegion)
                return true;
            if (current.HasAnyEventHandlers)
                return false;
            current = current.Parent;
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_tracer.IsRecording && !string.IsNullOrEmpty(_envTracePath))
            _tracer.StopRecording(_envTracePath);
        PlatformWindow.InputReceived -= HandleInputEvent;
        (_accessibilityBridge as IDisposable)?.Dispose();
        PlatformWindow.Dispose();
    }

    /// <summary>
    /// Bridges per-element accessibility notifications into the Window. Elements
    /// fire these when an a11y-visible property changes (a checkbox toggled, a
    /// label's text replaced) without any structural change. We flag the tree for
    /// a rebuild+diff next frame rather than patching a single node: the rebuild
    /// regenerates every snapshot from current state (so derived values like a
    /// button's name-from-content stay correct) and the diff emits exactly the
    /// property-changed events that platforms forward to assistive tech. A list's selection move
    /// is the exception: its two items' selected state is marked on the applied tree in place
    /// (<see cref="TryPatchA11ySelection"/>), as a focus move's is.
    /// </summary>
    void IAccessibilityAnnouncer.Announce(Element source, string text, AnnouncementPoliteness politeness)
    {
        // Nobody to hear it: nothing is kept for a reader that starts later.
        if (!_accessibilityBridge.IsActive) return;
        _announcements.Add((source, text, politeness));
        _a11yPropertyDirty = true; // a frame, and a tree that holds the element
    }

    private sealed class AccessibilityObserverAdapter(Window window) : IElementPropertyObserver
    {
        public void OnElementPropertyChanged(Element element, string propertyName)
        {
            if (!window._accessibilityBridge.IsActive) return;
            // Bounds alone are a layout-only change: a scroll moves everything under the view
            // and changes nothing else. They go on the coalesced schedule a resize gets — a
            // property change rebuilt the whole tree on every frame of a wheel turn, 0.7 ms a
            // frame of a 1.9 ms frame in a scrolling History list, and up to 9 ms.
            if (propertyName == AccessibilityProperties.BoundingRect)
                window._a11yLayoutRefreshPending = true;
            // A range's value can move every frame (a drag, a playback): on its own schedule.
            else if (propertyName == AccessibilityProperties.RangeValue)
                window._a11yRangeRefreshPending = true;
            // A list's selection moved: two items' selected state, which the frame marks on the
            // applied tree and tells without a rebuild (TryPatchA11ySelection). A rebuild for it
            // built and diffed the whole tree on every key of a walk: Junction's History list, a
            // key every 60 ms, 25 rebuilds per 40 keys became 41, its a11y share 7 % became 30 %.
            else if (propertyName == AccessibilityProperties.IsSelected
                && element is IAccessibleItemSource { PublishesItemNodes: true })
            {
                if (!window._a11ySelectionMoved.Contains(element)) window._a11ySelectionMoved.Add(element);
            }
            else
                window._a11yPropertyDirty = true;
        }
    }
}
