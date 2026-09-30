using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.WebView.WebView2.Interop;

namespace HultUI.WebView.WebView2;

/// <summary>
/// One WebView2 page in visual hosting. Created by <see cref="WebView2Engine"/>; driven by
/// its <see cref="WebViewController"/>.
/// </summary>
/// <remarks>
/// <para><b>Life.</b> WebView2 needs a parent window, so nothing is created until the first
/// placement names one: then the environment (shared per user data folder), then the
/// composition controller, then the capture. <see cref="WebEngineEvent.Ready"/> follows;
/// a failure on the way raises <see cref="WebEngineEvent.Failed"/> instead.</para>
/// <para><b>Threads.</b> Every WebView2 call and callback is on the UI thread, which must
/// be an STA with a message loop — SDL's is one. Only the capture's frames arrive
/// elsewhere.</para>
/// <para><b>Keys</b> are WebView2's own: in visual hosting it keeps a child window under the
/// parent for keyboard focus and the input method, so this view forwards pointer input only
/// (spike W0, and Microsoft's hosting guide).</para>
/// </remarks>
internal sealed unsafe class WebView2View : IWebEngineView
{
    private enum Phase { Waiting, Creating, Ready, Failed, Disposed }

    private readonly WebView2Engine _engine;
    private readonly WebViewOptions _options;
    private readonly List<(ComHandler Handler, nint Source, int RemoveSlot, long Token)> _subscriptions = [];
    private IWebViewHost? _host;
    private Phase _phase;
    private WebViewPlacement? _placement;
    private WebViewPlacement? _applied;
    private bool _visible;
    private FocusReason? _pendingFocus;
    private nint _composition, _controller, _controller3, _webview;
    private ComHandler? _creating;
    private VisualCapture? _capture;
    private CaptureRenderer? _renderer;
    private nint _environment;
    private readonly PageAccessibility _accessibility = new();
    private int _accessibilityLooksLeft = AccessibilityLooks;
    private Timer? _accessibilityRetry;
    private IDisposable? _parentWatch;

    /// <summary>How often a missing accessibility window is looked for again, and for how long: 12 seconds.</summary>
    private const int AccessibilityLookInterval = 300, AccessibilityLooks = 40;

    /// <summary>The windows the page's own UI Automation tree lives in, once WebView2 has made them.</summary>
    public NativeAccessibleContent AccessibleContent => _accessibility.Content;

    /// <summary>How the page's frames are delivered; decided by the element's first draw.</summary>
    internal GpuSinkMode FrameMode => _renderer?.Mode ?? GpuSinkMode.Undecided;

    /// <summary>Whether GL reads the captured frames in place, with no copy through the CPU.</summary>
    internal bool ZeroCopy => _capture?.ZeroCopy ?? false;

    public WebView2View(WebView2Engine engine, WebViewOptions options)
    {
        _engine = engine;
        _options = options;
    }

    public WebEngineCapabilities Capabilities => WebEngineCapabilities.NativeKeyboard | WebEngineCapabilities.DevTools;

    public event Action<WebEngineEvent>? Event;

    private void Raise(WebEngineEvent e) => Event?.Invoke(e);

    public void Attach(IWebViewHost host)
    {
        _host = host;
        _renderer = new CaptureRenderer(this);
        host.UseGpuRenderer(_renderer);
    }

    // ──── Creation ────

    public void SetPlacement(in WebViewPlacement placement)
    {
        _placement = placement;
        switch (_phase)
        {
            case Phase.Waiting when placement.Window != 0 && !placement.Bounds.IsEmpty:
                BeginCreate();
                break;
            case Phase.Ready:
                Apply(placement);
                break;
        }
    }

    private void BeginCreate()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            Fail(WebFailure.InitializationFailed,
                "WebView2 needs the UI thread to be a single-threaded apartment: put [STAThread] on Main.");
            return;
        }
        _phase = Phase.Creating;
        _engine.WithEnvironment(_options.UserDataFolder ?? WebView2Engine.DefaultUserDataFolder(), OnEnvironment);
    }

    private void OnEnvironment(nint environment, WebFailure failure, string? error)
    {
        if (_phase != Phase.Creating) return;
        if (environment == 0) { Fail(failure, error ?? "WebView2 would not start."); return; }
        Com.AddRef(environment);
        _environment = environment;

        var env3 = Com.TryQueryInterface(environment, ICoreWebView2Environment3.IID);
        if (env3 == 0) { Fail(WebFailure.InitializationFailed, "The installed WebView2 Runtime is too old for visual hosting."); return; }
        try
        {
            _creating = ComHandler.ForResult(ICoreWebView2CreateCoreWebView2CompositionControllerCompletedHandler.IID, OnControllerCreated);
            int hr = Com.Call(env3, ICoreWebView2Environment3.CreateCoreWebView2CompositionController, _placement!.Value.Window, _creating.Pointer);
            if (hr < 0)
            {
                _creating.Release();
                _creating = null;
                Fail(WebFailure.InitializationFailed, $"WebView2 could not create a page (0x{hr:X8}).");
            }
        }
        finally { Com.Release(ref env3); }
    }

    private void OnControllerCreated(int hr, nint composition)
    {
        _creating?.Release();
        _creating = null;
        if (_phase != Phase.Creating) return;          // disposed while it was on its way
        if (hr < 0 || composition == 0)
        {
            Fail(WebFailure.InitializationFailed, $"WebView2 could not create a page (0x{hr:X8}).");
            return;
        }

        try
        {
            Com.AddRef(composition);
            _composition = composition;
            _controller = Com.QueryInterface(composition, ICoreWebView2Controller.IID);
            _controller3 = Com.QueryInterface(composition, ICoreWebView2Controller3.IID);
            _webview = Com.GetPtr(_controller, ICoreWebView2Controller.get_CoreWebView2, "get_CoreWebView2");

            // We say where the page is and at what scale, in raw pixels; WebView2 must not
            // second-guess it from the monitor.
            Com.Check(Com.Call(_controller3, ICoreWebView2Controller3.put_BoundsMode, 0), "put_BoundsMode");
            Com.Check(Com.Call(_controller3, ICoreWebView2Controller3.put_ShouldDetectMonitorScaleChanges, 0), "put_ShouldDetectMonitorScaleChanges");

            ApplySettings();

            _capture = new VisualCapture(_host!.Pixels, _host.RequestRepaint) { Mode = _renderer!.Mode };
            Com.Check(Com.Call(_composition, ICoreWebView2CompositionController.put_RootVisualTarget, _capture.RootVisual), "put_RootVisualTarget");

            Subscribe();
            Apply(_placement!.Value);
            Com.Check(Com.Call(_controller, ICoreWebView2Controller.put_IsVisible, _visible ? 1 : 0), "put_IsVisible");
        }
        catch (Exception ex)
        {
            Fail(WebFailure.InitializationFailed, ex.Message);
            return;
        }

        _phase = Phase.Ready;
        AttachAccessibility();
        Raise(new WebEngineEvent.Ready());
        if (_pendingFocus is { } reason)
        {
            _pendingFocus = null;
            Focus(reason);
        }
    }

    private void ApplySettings()
    {
        var settings = Com.GetPtr(_webview, ICoreWebView2.get_Settings, "get_Settings");
        try
        {
            Com.Call(settings, ICoreWebView2Settings.put_AreDevToolsEnabled, _options.DevTools ? 1 : 0);
            if (_options.UserAgent is { } agent)
            {
                var settings2 = Com.TryQueryInterface(settings, ICoreWebView2Settings2.IID);
                if (settings2 != 0)
                {
                    Com.CallString(settings2, ICoreWebView2Settings2.put_UserAgent, agent);
                    Com.Release(ref settings2);
                }
            }
        }
        finally { Com.Release(ref settings); }
    }

    private void Apply(WebViewPlacement placement)
    {
        if (_applied == placement) return;
        var previous = _applied;
        _applied = placement;
        var b = placement.Bounds;

        if (previous is { } p && p.Window != placement.Window && placement.Window != 0)
            Com.Check(Com.Call(_controller, ICoreWebView2Controller.put_ParentWindow, placement.Window), "put_ParentWindow");
        if (previous?.Window != placement.Window)
        {
            _parentWatch?.Dispose();
            _parentWatch = placement.Window != 0 ? ParentWindowWatch.Watch(placement.Window, OnParentWindowMoved) : null;
            if (!_accessibility.Content.IsEmpty)
            {
                _accessibility.Clear();
                Raise(new WebEngineEvent.AccessibleContentChanged());
            }
        }
        if (previous?.Scale != placement.Scale)
            Com.Check(Com.Call(_controller3, ICoreWebView2Controller3.put_RasterizationScale, (double)placement.Scale), "put_RasterizationScale");

        // Bounds carry the page's true position in the window, though it renders at the
        // visual's origin whatever they say: the browser places its own popups (a <select>,
        // a date picker) and its input method against them.
        var rect = new Native.RECT { Left = b.X, Top = b.Y, Right = b.Right, Bottom = b.Bottom };
        Com.Check(Com.Call(_controller, ICoreWebView2Controller.put_Bounds, rect), "put_Bounds");
        _capture!.Resize(b.Width, b.Height);
        if (!_accessibility.IsComplete) AttachAccessibility();
    }

    private void Fail(WebFailure kind, string message)
    {
        if (_phase is Phase.Failed or Phase.Disposed) return;
        _phase = Phase.Failed;
        Raise(new WebEngineEvent.Failed(kind, message));
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (_phase == Phase.Ready)
            Com.Call(_controller, ICoreWebView2Controller.put_IsVisible, visible ? 1 : 0);
    }

    // ──── Events ────

    private void On(nint source, int addSlot, int removeSlot, Guid handlerIid, Action<nint, nint> handler, string what)
    {
        var h = ComHandler.ForEvent(handlerIid, handler);
        var token = Com.AddHandler(source, addSlot, h.Pointer, what);
        _subscriptions.Add((h, source, removeSlot, token));
    }

    private void Subscribe()
    {
        On(_webview, ICoreWebView2.add_NavigationStarting, ICoreWebView2.remove_NavigationStarting,
            ICoreWebView2NavigationStartingEventHandler.IID, OnNavigationStarting, "add_NavigationStarting");
        On(_webview, ICoreWebView2.add_NavigationCompleted, ICoreWebView2.remove_NavigationCompleted,
            ICoreWebView2NavigationCompletedEventHandler.IID, OnNavigationCompleted, "add_NavigationCompleted");
        On(_webview, ICoreWebView2.add_SourceChanged, ICoreWebView2.remove_SourceChanged,
            ICoreWebView2SourceChangedEventHandler.IID, (_, _) => Raise(new WebEngineEvent.SourceChanged(Source())), "add_SourceChanged");
        On(_webview, ICoreWebView2.add_DocumentTitleChanged, ICoreWebView2.remove_DocumentTitleChanged,
            ICoreWebView2DocumentTitleChangedEventHandler.IID,
            (_, _) => Raise(new WebEngineEvent.TitleChanged(Com.GetString(_webview, ICoreWebView2.get_DocumentTitle, "get_DocumentTitle") ?? "")),
            "add_DocumentTitleChanged");
        On(_webview, ICoreWebView2.add_HistoryChanged, ICoreWebView2.remove_HistoryChanged,
            ICoreWebView2HistoryChangedEventHandler.IID,
            (_, _) => Raise(new WebEngineEvent.HistoryChanged(
                Com.GetBool(_webview, ICoreWebView2.get_CanGoBack, "get_CanGoBack"),
                Com.GetBool(_webview, ICoreWebView2.get_CanGoForward, "get_CanGoForward"))),
            "add_HistoryChanged");
        On(_webview, ICoreWebView2.add_WebMessageReceived, ICoreWebView2.remove_WebMessageReceived,
            ICoreWebView2WebMessageReceivedEventHandler.IID, OnWebMessage, "add_WebMessageReceived");
        On(_webview, ICoreWebView2.add_NewWindowRequested, ICoreWebView2.remove_NewWindowRequested,
            ICoreWebView2NewWindowRequestedEventHandler.IID, OnNewWindow, "add_NewWindowRequested");
        On(_webview, ICoreWebView2.add_ProcessFailed, ICoreWebView2.remove_ProcessFailed,
            ICoreWebView2ProcessFailedEventHandler.IID, OnProcessFailed, "add_ProcessFailed");
        On(_composition, ICoreWebView2CompositionController.add_CursorChanged, ICoreWebView2CompositionController.remove_CursorChanged,
            ICoreWebView2CursorChangedEventHandler.IID,
            (_, _) => Raise(new WebEngineEvent.CursorChanged(CursorFor(
                Com.Get<uint>(_composition, ICoreWebView2CompositionController.get_SystemCursorId, "get_SystemCursorId"),
                Com.Get<nint>(_composition, ICoreWebView2CompositionController.get_Cursor, "get_Cursor")))),
            "add_CursorChanged");
        On(_controller, ICoreWebView2Controller.add_MoveFocusRequested, ICoreWebView2Controller.remove_MoveFocusRequested,
            ICoreWebView2MoveFocusRequestedEventHandler.IID, OnMoveFocusRequested, "add_MoveFocusRequested");
        On(_controller, ICoreWebView2Controller.add_AcceleratorKeyPressed, ICoreWebView2Controller.remove_AcceleratorKeyPressed,
            ICoreWebView2AcceleratorKeyPressedEventHandler.IID, OnAcceleratorKey, "add_AcceleratorKeyPressed");
    }

    private Uri? Source()
    {
        var text = Com.GetString(_webview, ICoreWebView2.get_Source, "get_Source");
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri : null;
    }

    private void OnNavigationStarting(nint sender, nint args)
    {
        var text = Com.GetString(args, ICoreWebView2NavigationStartingEventArgs.get_Uri, "get_Uri");
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return;   // nothing to ask about; allow
        long id = (long)Com.Get<ulong>(args, ICoreWebView2NavigationStartingEventArgs.get_NavigationId, "get_NavigationId");
        bool user = Com.GetBool(args, ICoreWebView2NavigationStartingEventArgs.get_IsUserInitiated, "get_IsUserInitiated");
        bool redirect = Com.GetBool(args, ICoreWebView2NavigationStartingEventArgs.get_IsRedirected, "get_IsRedirected");

        // WebView2 offers no deferral for this event: the answer has to be given before the
        // handler returns. It can be, because WebView2 raises it on the UI thread and the
        // controller answers there, synchronously, inside Raise.
        bool cancel = false;
        var request = new WebNavigationRequest(id, uri, user, redirect, allow => cancel = !allow);
        Raise(new WebEngineEvent.NavigationStarting(request));
        if (cancel) Com.Call(args, ICoreWebView2NavigationStartingEventArgs.put_Cancel, 1);
    }

    /// <summary>
    /// The window moved on screen. The browser places its popups and its input method against
    /// the page's screen position and reports it to assistive technology, and only learns the
    /// window's from this call.
    /// </summary>
    private void OnParentWindowMoved()
    {
        if (_phase == Phase.Ready)
            Com.Call(_controller, ICoreWebView2Controller.NotifyParentWindowPositionChanged);
    }

    /// <summary>
    /// Something happened that may have produced the page's accessibility windows
    /// (<see cref="PageAccessibility"/>): the page is ready, a navigation finished, the page
    /// took focus, it was placed. Look, and look again for a while if they are not there.
    /// </summary>
    private void AttachAccessibility()
    {
        _accessibilityLooksLeft = AccessibilityLooks;
        LookForAccessibilityWindows();
    }

    /// <summary>
    /// One look, and another in a moment if a window is still missing. WebView2 makes them in
    /// its own time and moves the browser's after the page's bounds, so the first look —
    /// made as the page is placed — usually finds the focus window and not the other, and a
    /// hit test over the page would be answered with the element's box until something else
    /// happened to look again. The looks are counted: a browser that never makes the windows
    /// is not asked for ever.
    /// </summary>
    private void LookForAccessibilityWindows()
    {
        if (_phase != Phase.Ready || _accessibility.IsComplete) return;

        if (FindAccessibilityWindows()) Raise(new WebEngineEvent.AccessibleContentChanged());
        if (_accessibility.IsComplete || _accessibilityLooksLeft-- <= 0) return;
        _accessibilityRetry ??= new Timer(_ => _host?.Post(LookForAccessibilityWindows));
        _accessibilityRetry.Change(AccessibilityLookInterval, Timeout.Infinite);
    }

    private bool FindAccessibilityWindows()
    {
        if (_applied is not { Window: var window and not 0 } placement) return false;

        var origin = new Native.POINT();
        Native.ClientToScreen(window, ref origin);
        var b = placement.Bounds;
        var onScreen = new Native.RECT
        {
            Left = origin.X + b.X, Top = origin.Y + b.Y, Right = origin.X + b.Right, Bottom = origin.Y + b.Bottom,
        };
        uint browser = 0;
        ((delegate* unmanaged[Stdcall]<nint, uint*, int>)(*(nint**)_webview)[ICoreWebView2.get_BrowserProcessId])(_webview, &browser);
        return _accessibility.Update(window, _composition, _environment, browser, onScreen);
    }

    private void OnNavigationCompleted(nint sender, nint args)
    {
        AttachAccessibility();
        bool ok = Com.GetBool(args, ICoreWebView2NavigationCompletedEventArgs.get_IsSuccess, "get_IsSuccess");
        long id = (long)Com.Get<ulong>(args, ICoreWebView2NavigationCompletedEventArgs.get_NavigationId, "get_NavigationId");
        int status = Com.Get<int>(args, ICoreWebView2NavigationCompletedEventArgs.get_WebErrorStatus, "get_WebErrorStatus");
        int? http = null;
        var args2 = Com.TryQueryInterface(args, ICoreWebView2NavigationCompletedEventArgs2.IID);
        if (args2 != 0)
        {
            int code = Com.Get<int>(args2, ICoreWebView2NavigationCompletedEventArgs2.get_HttpStatusCode, "get_HttpStatusCode");
            if (code != 0) http = code;
            Com.Release(ref args2);
        }
        Raise(new WebEngineEvent.NavigationCompleted(id, ok, http, ok ? null : WebErrorText(status)));
    }

    private void OnWebMessage(nint sender, nint args)
    {
        var json = Com.GetString(args, ICoreWebView2WebMessageReceivedEventArgs.get_WebMessageAsJson, "get_WebMessageAsJson");
        var source = Com.GetString(args, ICoreWebView2WebMessageReceivedEventArgs.get_Source, "get_Source");
        if (json is not null) Raise(new WebEngineEvent.MessageReceived(json, OriginOf(source)));
    }

    private void OnNewWindow(nint sender, nint args)
    {
        // Refused here, always; the app decides what the address deserves.
        Com.Call(args, ICoreWebView2NewWindowRequestedEventArgs.put_Handled, 1);
        var text = Com.GetString(args, ICoreWebView2NewWindowRequestedEventArgs.get_Uri, "get_Uri");
        bool user = Com.GetBool(args, ICoreWebView2NewWindowRequestedEventArgs.get_IsUserInitiated, "get_IsUserInitiated");
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri))
            Raise(new WebEngineEvent.NewWindowRequested(uri, user));
    }

    private void OnProcessFailed(nint sender, nint args)
    {
        switch (Com.Get<int>(args, ICoreWebView2ProcessFailedEventArgs.get_ProcessFailedKind, "get_ProcessFailedKind"))
        {
            case 0:   // BROWSER_PROCESS_EXITED: every view of this environment is gone
                Fail(WebFailure.EngineExited, "The WebView2 browser process exited.");
                break;
            case 1:   // RENDER_PROCESS_EXITED
                Raise(new WebEngineEvent.ProcessFailed("The page's process exited.", CanReload: true));
                break;
            case 2:   // RENDER_PROCESS_UNRESPONSIVE
                Raise(new WebEngineEvent.ProcessFailed("The page stopped responding.", CanReload: true));
                break;
            // Frame, utility, GPU and helper processes: WebView2 restarts those itself.
        }
    }

    private void OnMoveFocusRequested(nint sender, nint args)
    {
        int reason = Com.Get<int>(args, ICoreWebView2MoveFocusRequestedEventArgs.get_Reason, "get_Reason");
        if (reason is not (1 or 2)) return;                 // NEXT, PREVIOUS
        Com.Call(args, ICoreWebView2MoveFocusRequestedEventArgs.put_Handled, 1);
        Raise(new WebEngineEvent.FocusLeaving(Forward: reason == 1));
    }

    /// <summary>
    /// WebView2 offers keys it considers accelerators before the page acts on them; the
    /// shortcuts among them go to the app. Raised on the UI thread, answered inside it.
    /// </summary>
    private void OnAcceleratorKey(nint sender, nint args)
    {
        int kind = Com.Get<int>(args, ICoreWebView2AcceleratorKeyPressedEventArgs.get_KeyEventKind, "get_KeyEventKind");
        uint vk = Com.Get<uint>(args, ICoreWebView2AcceleratorKeyPressedEventArgs.get_VirtualKey, "get_VirtualKey");
        int lParam = Com.Get<int>(args, ICoreWebView2AcceleratorKeyPressedEventArgs.get_KeyEventLParam, "get_KeyEventLParam");
        var modifiers = CurrentModifiers();
        if (!Win32Keys.IsShortcut(vk, modifiers)) return;
        var key = Win32Keys.ToKeyCode(vk, (uint)(lParam >> 16) & 0xFF);
        if (key == KeyCode.Unknown) return;

        bool down = kind is 0 or 2;   // KEY_DOWN, SYSTEM_KEY_DOWN
        var request = new WebKeyRequest(key, modifiers, down, isRepeat: down && (lParam & (1 << 30)) != 0);
        Raise(new WebEngineEvent.KeyFromPage(request));
        if (request.Handled)
            Com.Call(args, ICoreWebView2AcceleratorKeyPressedEventArgs.put_Handled, 1);
    }

    /// <summary>The thread's modifier state: WebView2's focus window is on this thread.</summary>
    private static KeyModifiers CurrentModifiers()
    {
        static bool Down(int vk) => Native.GetKeyState(vk) < 0;
        var m = KeyModifiers.None;
        if (Down(0x10)) m |= KeyModifiers.Shift;
        if (Down(0x11)) m |= KeyModifiers.Ctrl;
        if (Down(0x12)) m |= KeyModifiers.Alt;
        if (Down(0x5B) || Down(0x5C)) m |= KeyModifiers.Super;
        return m;
    }

    // ──── Commands ────

    private void RequireReady()
    {
        if (_phase != Phase.Ready) throw new InvalidOperationException("The WebView2 page is not ready.");
    }

    public void Navigate(Uri uri)
    {
        RequireReady();
        Com.Check(Com.CallString(_webview, ICoreWebView2.Navigate, uri.AbsoluteUri), "Navigate " + uri);
    }

    public void NavigateToString(string html)
    {
        RequireReady();
        Com.Check(Com.CallString(_webview, ICoreWebView2.NavigateToString, html), "NavigateToString");
    }

    public void GoBack() { RequireReady(); Com.Call(_webview, ICoreWebView2.GoBack); }
    public void GoForward() { RequireReady(); Com.Call(_webview, ICoreWebView2.GoForward); }
    public void Reload() { RequireReady(); Com.Call(_webview, ICoreWebView2.Reload); }
    public void Stop() { RequireReady(); Com.Call(_webview, ICoreWebView2.Stop); }

    public Task<string> ExecuteScriptAsync(string script)
    {
        RequireReady();
        var result = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        ComHandler? handler = null;
        handler = ComHandler.ForResult(ICoreWebView2ExecuteScriptCompletedHandler.IID, (hr, json) =>
        {
            handler!.Release();
            if (hr < 0) result.TrySetException(new System.Runtime.InteropServices.COMException($"The script failed (0x{hr:X8}).", hr));
            else result.TrySetResult(json == 0 ? "null" : new string((char*)json));
        });
        int call = Com.CallString(_webview, ICoreWebView2.ExecuteScript, script, handler.Pointer);
        if (call < 0)
        {
            handler.Release();
            result.TrySetException(new System.Runtime.InteropServices.COMException($"ExecuteScript failed (0x{call:X8}).", call));
        }
        return result.Task;
    }

    public void PostMessage(string json)
    {
        RequireReady();
        Com.Check(Com.CallString(_webview, ICoreWebView2.PostWebMessageAsJson, json), "PostWebMessageAsJson");
    }

    // ──── Input ────

    public void SendPointer(in WebPointerInput input)
    {
        if (_phase != Phase.Ready) return;
        var (kind, data) = MouseEvent(input);
        if (kind == 0) return;
        var point = input.Action == WebPointerAction.Leave ? default : new Native.POINT { X = input.X, Y = input.Y };
        ((delegate* unmanaged[Stdcall]<nint, int, int, uint, Native.POINT, int>)(*(nint**)_composition)[ICoreWebView2CompositionController.SendMouseInput])(
            _composition, kind, VirtualKeys(input), data, point);
    }

    /// <summary>The COREWEBVIEW2_MOUSE_EVENT_KIND and mouse data for an input; kind 0 to skip.</summary>
    internal static (int Kind, uint Data) MouseEvent(in WebPointerInput input)
    {
        bool dbl = input.ClickCount == 2;
        uint x = input.Button switch { WebPointerButtons.Back => 1u, WebPointerButtons.Forward => 2u, _ => 0u };
        return input.Action switch
        {
            WebPointerAction.Move => (512, 0),
            WebPointerAction.Leave => (675, 0),
            WebPointerAction.Wheel when input.WheelY != 0 => (522, unchecked((uint)(int)MathF.Round(input.WheelY * 120))),
            // HorizontalWheel is positive to the right; HultUI's scroll sense is the reverse.
            WebPointerAction.Wheel when input.WheelX != 0 => (526, unchecked((uint)(int)MathF.Round(-input.WheelX * 120))),
            WebPointerAction.Down => input.Button switch
            {
                WebPointerButtons.Left => (dbl ? 515 : 513, 0),
                WebPointerButtons.Right => (dbl ? 518 : 516, 0),
                WebPointerButtons.Middle => (dbl ? 521 : 519, 0),
                WebPointerButtons.Back or WebPointerButtons.Forward => (dbl ? 525 : 523, x),
                _ => (0, 0),
            },
            WebPointerAction.Up => input.Button switch
            {
                WebPointerButtons.Left => (514, 0),
                WebPointerButtons.Right => (517, 0),
                WebPointerButtons.Middle => (520, 0),
                WebPointerButtons.Back or WebPointerButtons.Forward => (524, x),
                _ => (0, 0),
            },
            _ => (0, 0),
        };
    }

    internal static int VirtualKeys(in WebPointerInput input)
    {
        int keys = 0;
        if ((input.Held & WebPointerButtons.Left) != 0) keys |= 0x1;
        if ((input.Held & WebPointerButtons.Right) != 0) keys |= 0x2;
        if ((input.Modifiers & KeyModifiers.Shift) != 0) keys |= 0x4;
        if ((input.Modifiers & KeyModifiers.Ctrl) != 0) keys |= 0x8;
        if ((input.Held & WebPointerButtons.Middle) != 0) keys |= 0x10;
        if ((input.Held & WebPointerButtons.Back) != 0) keys |= 0x20;
        if ((input.Held & WebPointerButtons.Forward) != 0) keys |= 0x40;
        return keys;
    }

    // Keys and text reach the page natively (NativeKeyboard).
    public void SendKey(in WebKeyInput input) { }
    public void SendText(in WebTextInput input) { }

    public void Focus(FocusReason reason)
    {
        if (_phase != Phase.Ready) { _pendingFocus = reason; return; }
        AttachAccessibility();
        int code = reason switch { FocusReason.Next => 1, FocusReason.Previous => 2, _ => 0 };
        Com.Call(_controller, ICoreWebView2Controller.MoveFocus, code);
    }

    public void Blur()
    {
        _pendingFocus = null;
        if (_phase != Phase.Ready || _applied is not { Window: var window and not 0 }) return;
        // Take keyboard focus back from WebView2's focus window; a click on the parent's own
        // client area would not.
        var focused = Native.GetFocus();
        if (focused != 0 && focused != window && Native.IsChild(window, focused))
            Native.SetFocus(window);
    }

    // ──── Mapping ────

    /// <summary>A document's origin for message filtering; null for an opaque one.</summary>
    internal static Uri? OriginOf(string? source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https")) return null;
        return new Uri(uri.GetLeftPart(UriPartial.Authority));
    }

    /// <summary>The cursor for a Win32 IDC_* resource id. Id 0 is a custom cursor (a CSS
    /// <c>url()</c>, drawn as the arrow) — or, with no cursor handle at all, <c>cursor: none</c>.</summary>
    internal static CursorType CursorFor(uint id, nint handle = 1) => id switch
    {
        0 when handle == 0 => CursorType.None,
        32513 => CursorType.Text,
        32514 => CursorType.Wait,
        32650 => CursorType.Progress,
        32515 => CursorType.Crosshair,
        32649 => CursorType.Pointer,
        32644 => CursorType.ResizeEW,
        32645 => CursorType.ResizeNS,
        32642 => CursorType.ResizeNWSE,
        32643 => CursorType.ResizeNESW,
        32646 => CursorType.Move,
        32648 => CursorType.NotAllowed,
        _ => CursorType.Default,
    };

    private static readonly string[] WebErrors =
    [
        "Unknown", "CertificateCommonNameIsIncorrect", "CertificateExpired", "ClientCertificateContainsErrors",
        "CertificateRevoked", "CertificateIsInvalid", "ServerUnreachable", "Timeout", "HttpInvalidServerResponse",
        "ConnectionAborted", "ConnectionReset", "Disconnected", "CannotConnect", "HostNameNotResolved",
        "Cancelled", "RedirectFailed", "UnexpectedError", "ValidAuthenticationCredentialsRequired",
        "ValidProxyAuthenticationRequired",
    ];

    /// <summary>COREWEBVIEW2_WEB_ERROR_STATUS as text; OPERATION_CANCELED reads "Cancelled", as the fake says.</summary>
    internal static string WebErrorText(int status) =>
        (uint)status < (uint)WebErrors.Length ? WebErrors[status] : $"Error {status}";

    /// <summary>
    /// The page as a GPU frame producer: the frame sink's renderer, forwarding to the capture
    /// once it exists. The sink asks from the element's first draw, which can come before the
    /// engine has created anything — then there is simply nothing to draw yet.
    /// </summary>
    private sealed class CaptureRenderer(WebView2View view) : IGpuFrameRenderer
    {
        public GpuSinkMode Mode { get; private set; }

        public void OnModeDecided(GpuSinkMode mode)
        {
            Mode = mode;
            if (view._capture is { } capture) capture.Mode = mode;
        }

        public void PrepareGL(Func<string, nint> getProcAddress) => view._capture?.PrepareGL(getProcAddress);

        public bool TryGetFrameSize(out int width, out int height)
        {
            width = height = 0;
            return view._capture?.TryGetSize(out width, out height) ?? false;
        }

        /// <summary>The page's top row lands in the target's row 0, as mpv's does.</summary>
        public bool RendersBottomUp => false;

        public bool Render(Func<string, nint> getProcAddress, in GpuFrameTarget target, int width, int height) =>
            view._capture?.Render(getProcAddress, target, width, height) ?? false;

        public void ReleaseGL() => view._capture?.ReleaseGL();
    }

    public void Dispose()
    {
        if (_phase == Phase.Disposed) return;
        _phase = Phase.Disposed;
        foreach (var (handler, source, removeSlot, token) in _subscriptions)
        {
            Com.RemoveHandler(source, removeSlot, token);
            handler.Release();
        }
        _subscriptions.Clear();
        _parentWatch?.Dispose();
        _parentWatch = null;
        _accessibilityRetry?.Dispose();
        _accessibilityRetry = null;
        _accessibility.Clear();
        _capture?.Dispose();
        if (_controller != 0) Com.Call(_controller, ICoreWebView2Controller.Close);
        Com.Release(ref _webview);
        Com.Release(ref _controller3);
        Com.Release(ref _controller);
        Com.Release(ref _composition);
        Com.Release(ref _environment);
    }
}
