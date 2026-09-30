using HultUI.Core.Events;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Core.Threading;

namespace HultUI.WebView;

/// <summary>
/// One page, and the reactive state the UI binds to. Every property is signal-backed, so
/// reading it inside a component's <c>Build</c> tracks it; every command runs on the UI
/// thread and is forwarded to the engine view, whose events — from whatever thread — are
/// applied on the UI thread.
/// </summary>
/// <remarks>
/// <para><b>Before the engine is ready</b>, navigation, script and messages are held and
/// sent in order once it is; a script awaited before then completes after. Some engines
/// are not ready until a <see cref="WebViewElement"/> has placed the page in a window, so a
/// controller whose element was never shown stays <see cref="WebViewState.Initializing"/>.
/// If the engine fails instead, held scripts fault and held commands are dropped.</para>
///
/// <para><b>One element per controller.</b> The element reports where the page is and
/// forwards input; two elements showing one controller would fight over its placement.
/// Unmounting the element hides the page but keeps it — a tab switch does not reload.</para>
///
/// <para><b>State is the engine's word.</b> <see cref="Navigate(Uri)"/> does not set
/// <see cref="Source"/>; the engine does when the navigation commits. The exception is
/// <see cref="IsLoading"/>, which follows the navigations the controller let through.</para>
/// </remarks>
public sealed class WebViewController : IDisposable
{
    private readonly IWebEngineView _view;
    private readonly PixelFrameSink _sink = new();
    private GpuFrameSink? _gpuSink;
    private int _repaintPending;
    private readonly List<(Action Run, Action<Exception>? Fail)> _pending = [];
    private bool _disposed;
    private WebViewPlacement? _placement;
    private bool? _visible;

    private readonly Signal<WebViewState> _state = new(WebViewState.Initializing);
    private readonly Signal<string?> _error = new(null);
    private readonly Signal<Uri?> _source = new(null);
    private readonly Signal<string> _title = new("");
    private readonly Signal<bool> _isLoading = new(false);
    private readonly Signal<bool> _canGoBack = new(false);
    private readonly Signal<bool> _canGoForward = new(false);

    /// <param name="engine">The engine to create the page in.</param>
    /// <param name="options">Fixed for the page's life; null takes the defaults.</param>
    public WebViewController(IWebEngine engine, WebViewOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Options = options ?? new WebViewOptions();

        var availability = engine.Availability;
        if (availability.IsAvailable)
        {
            _view = engine.CreateView(Options);
        }
        else
        {
            _view = new NoView();
            _state.Value = WebViewState.Failed;
            _error.Value = availability.Reason ?? $"{engine.Name} is not available.";
        }

        _view.Event += OnEngineEvent;
        _sink.FramePublished += OnFramePublished;
        _view.Attach(new Host(this));
    }

    /// <summary>The engine's view, for what only that engine offers. Cast it to the engine
    /// package's type; the controller's own members are the portable surface.</summary>
    public IWebEngineView View => _view;

    public WebViewOptions Options { get; }

    public WebEngineCapabilities Capabilities => _view.Capabilities;

    /// <summary>The page's frames, for <see cref="WebViewElement"/> to draw. Never null;
    /// empty until the engine's first frame. An engine with a GPU renderer is drawn through a
    /// <see cref="GpuFrameSink"/>, which is also the pixel ring on a surface without GL.</summary>
    public IPixelImageSource Frames => _gpuSink ?? (IPixelImageSource)_sink;

    /// <summary>How frames reach the screen; <see cref="GpuSinkMode.Pixels"/> for an engine
    /// with no GPU renderer.</summary>
    public GpuSinkMode SinkMode => _gpuSink?.Mode ?? GpuSinkMode.Pixels;

    /// <summary>A frame was published. UI thread. <see cref="WebViewElement"/> subscribes.</summary>
    public event Action? FrameArrived;

    // ──── State ────

    public WebViewState State => _state.Value;

    /// <summary>Why <see cref="State"/> is <see cref="WebViewState.Failed"/> or
    /// <see cref="WebViewState.Crashed"/>; null otherwise.</summary>
    public string? Error => _error.Value;

    /// <summary>The page's address, once a navigation has committed.</summary>
    public Uri? Source => _source.Value;

    /// <summary>The document's title; empty until it has one.</summary>
    public string Title => _title.Value;

    public bool IsLoading => _isLoading.Value;
    public bool CanGoBack => _canGoBack.Value;
    public bool CanGoForward => _canGoForward.Value;

    // ──── Application events (UI thread) ────

    /// <summary>
    /// A navigation is about to start. Call <see cref="WebNavigationRequest.Cancel"/> to
    /// stop it — inside the handler; the engine is answered as soon as the handlers return.
    /// </summary>
    public event Action<WebNavigationRequest>? NavigationStarting;

    public event Action<WebNavigationResult>? NavigationCompleted;

    /// <summary>The page posted a message from an origin <see cref="WebViewOptions.MessageOrigins"/>
    /// allows. Messages from any other origin are dropped.</summary>
    public event Action<WebMessage>? MessageReceived;

    /// <summary>The page asked for a new window (a <c>target=_blank</c> link,
    /// <c>window.open</c>). It has been refused; decide what the address deserves.</summary>
    public event Action<WebNewWindowRequest>? NewWindowRequested;

    // ──── Element plumbing ────

    /// <summary>The cursor the page last asked for.</summary>
    internal CursorType Cursor { get; private set; } = CursorType.Default;
    internal event Action<CursorType>? CursorChanged;
    internal event Action<bool>? FocusLeaving;
    internal event Action<WebKeyRequest>? KeyFromPage;
    internal event Action<string>? TitleChanged;

    /// <summary>The view's <see cref="IWebEngineView.AccessibleContent"/> changed. UI thread.</summary>
    internal event Action? AccessibleContentChanged;

    /// <summary>
    /// The page asked for a context menu. Handlers may change <see cref="WebContextMenuRequest.Items"/>
    /// — by default the editing and copying entries, none of a browser's — or set
    /// <see cref="WebContextMenuRequest.Handled"/> and answer it themselves. Otherwise the
    /// element opens a HultUI context menu where the page asked. UI thread.
    /// </summary>
    public event Action<WebContextMenuRequest>? ContextMenuRequested;

    /// <summary>
    /// The page opened one of its own dialogs, and its script waits. A handler may answer it
    /// (<see cref="WebScriptDialogRequest.Accept"/>, <see cref="WebScriptDialogRequest.Cancel"/>)
    /// or set <see cref="WebScriptDialogRequest.Handled"/> and answer later; otherwise the
    /// element shows it as a HultUI dialog over the page. UI thread.
    /// </summary>
    public event Action<WebScriptDialogRequest>? ScriptDialogRequested;

    /// <summary>
    /// The page started a download. It is cancelled unless a handler calls
    /// <see cref="WebDownloadRequest.SaveTo"/>, or <see cref="WebDownloadRequest.Defer"/> and
    /// answers later. UI thread.
    /// </summary>
    public event Action<WebDownloadRequest>? DownloadRequested;

    /// <summary>
    /// The page asked for a permission. It is denied unless a handler calls
    /// <see cref="WebPermissionRequest.Allow"/>, or <see cref="WebPermissionRequest.Defer"/> and
    /// answers later. UI thread.
    /// </summary>
    public event Action<WebPermissionRequest>? PermissionRequested;

    /// <summary>A context menu nobody took over, for the element to open. UI thread.</summary>
    internal event Action<WebContextMenuRequest>? ContextMenuToShow;

    /// <summary>A page dialog nobody answered, for the element to show. UI thread.</summary>
    internal event Action<WebScriptDialogRequest>? ScriptDialogToShow;

    // ──── Commands ────

    public void Navigate(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri) throw new ArgumentException("Navigate needs an absolute address.", nameof(uri));
        Run("Navigate", () => _view.Navigate(uri));
    }

    /// <summary>Navigates to an absolute address given as text.</summary>
    /// <exception cref="UriFormatException">The text is not an absolute address.</exception>
    public void Navigate(string url) => Navigate(new Uri(url, UriKind.Absolute));

    /// <summary>Shows a document given as a string. Its origin is opaque; messages from it
    /// are always accepted.</summary>
    public void NavigateToString(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        Run("NavigateToString", () => _view.NavigateToString(html));
    }

    public void GoBack() => Run("GoBack", () => _view.GoBack());
    public void GoForward() => Run("GoForward", () => _view.GoForward());
    public void Reload() => Run("Reload", () => _view.Reload());
    public void Stop() => Run("Stop", () => _view.Stop());

    /// <summary>Runs script in the page; the result is its value as JSON (<c>"null"</c>
    /// for none). Waits for the engine if it is not ready yet.</summary>
    /// <exception cref="InvalidOperationException">The engine failed (the task faults).</exception>
    public Task<string> ExecuteScriptAsync(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        UIThread.VerifyAccess("WebViewController.ExecuteScriptAsync");
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (State is WebViewState.Ready or WebViewState.Crashed)
            return _view.ExecuteScriptAsync(script);
        if (State == WebViewState.Failed)
            return Task.FromException<string>(new InvalidOperationException(Error));

        var held = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending.Add((() => ForwardResult(_view.ExecuteScriptAsync(script), held), ex => held.TrySetException(ex)));
        return held.Task;
    }

    /// <summary>Delivers a JSON message to the page's message listener.</summary>
    public void PostMessage(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        Run("PostMessage", () => _view.PostMessage(json));
    }

    private static async void ForwardResult(Task<string> task, TaskCompletionSource<string> to)
    {
        try { to.TrySetResult(await task.ConfigureAwait(false)); }
        catch (Exception ex) { to.TrySetException(ex); }
    }

    private void Run(string what, Action action)
    {
        UIThread.VerifyAccess("WebViewController." + what);
        ObjectDisposedException.ThrowIf(_disposed, this);
        switch (State)
        {
            case WebViewState.Ready or WebViewState.Crashed: action(); break;
            case WebViewState.Initializing: _pending.Add((action, null)); break;
            // Failed: nothing will ever carry it out, and State already says so.
        }
    }

    // ──── Element → engine ────

    internal void SetPlacement(in WebViewPlacement placement)
    {
        if (_disposed || _placement == placement) return;
        _placement = placement;
        _view.SetPlacement(placement);
    }

    internal void SetVisible(bool visible)
    {
        if (_disposed || _visible == visible) return;
        _visible = visible;
        _view.SetVisible(visible);
    }

    internal void SendPointer(in WebPointerInput input) { if (!_disposed) _view.SendPointer(input); }
    internal void SendKey(in WebKeyInput input) { if (!_disposed) _view.SendKey(input); }
    internal void SendText(in WebTextInput input) { if (!_disposed) _view.SendText(input); }
    internal bool SendDrag(in WebDragInput input) => !_disposed && _view.SendDrag(input);
    internal void Focus(FocusReason reason) { if (!_disposed) _view.Focus(reason); }
    internal void Blur() { if (!_disposed) _view.Blur(); }

    // ──── Engine → UI thread ────

    private void OnEngineEvent(WebEngineEvent e) => Dispatch(() => Apply(e));

    private void OnFramePublished(PixelFrameSink _) => Dispatch(() => FrameArrived?.Invoke());

    // A GPU renderer asks from a thread of the engine's. Posting from there could deadlock
    // with a live-resize frame (docs/THREADING.md: a thread the frame waits for must never
    // post), so the request flips a flag and a thread-pool item posts — as MediaPlayer does.
    private void OnRepaintRequested()
    {
        if (_disposed) return;
        if (UIThread.IsCurrent) { FrameArrived?.Invoke(); return; }
        if (Interlocked.Exchange(ref _repaintPending, 1) == 0)
            ThreadPool.UnsafeQueueUserWorkItem(static c => c.PostRepaint(), this, preferLocal: false);
    }

    private void PostRepaint() => Application.InvokeOnMainThread(() =>
    {
        Volatile.Write(ref _repaintPending, 0);
        if (!_disposed) FrameArrived?.Invoke();
    });

    /// <summary>
    /// Runs on the UI thread: now when this is it (WebView2 raises everything there),
    /// otherwise on the next frame, which the post wakes.
    /// </summary>
    private void Dispatch(Action apply)
    {
        if (_disposed) return;
        if (UIThread.IsCurrent) apply();
        else Application.InvokeOnMainThread(() => { if (!_disposed) apply(); });
    }

    private void Apply(WebEngineEvent e)
    {
        switch (e)
        {
            case WebEngineEvent.Ready:
                if (State != WebViewState.Initializing) return;
                _state.Value = WebViewState.Ready;
                FlushPending();
                break;

            case WebEngineEvent.Failed f:
                ReactiveRuntime.Batch(() =>
                {
                    _error.Value = f.Message;
                    _state.Value = WebViewState.Failed;
                    _isLoading.Value = false;
                });
                FailPending(new InvalidOperationException(f.Message));
                break;

            case WebEngineEvent.NavigationStarting s:
                try
                {
                    NavigationStarting?.Invoke(s.Request);
                    if (!s.Request.IsCancelled) _isLoading.Value = true;
                }
                finally
                {
                    // The engine waits for this; a throwing handler must not leave it waiting.
                    s.Request.Complete();
                }
                break;

            case WebEngineEvent.NavigationCompleted c:
                ReactiveRuntime.Batch(() =>
                {
                    _isLoading.Value = false;
                    if (c.IsSuccess && State == WebViewState.Crashed)
                    {
                        _state.Value = WebViewState.Ready;
                        _error.Value = null;
                    }
                });
                NavigationCompleted?.Invoke(new WebNavigationResult(Source, c.IsSuccess, c.HttpStatus, c.Error));
                break;

            case WebEngineEvent.SourceChanged s:
                _source.Value = s.Source;
                break;

            case WebEngineEvent.TitleChanged t:
                _title.Value = t.Title;
                TitleChanged?.Invoke(t.Title);
                break;

            case WebEngineEvent.AccessibleContentChanged:
                AccessibleContentChanged?.Invoke();
                break;

            case WebEngineEvent.ContextMenuRequested m:
                try { ContextMenuRequested?.Invoke(m.Request); }
                finally
                {
                    // The engine waits for the menu's answer; nothing may leave it waiting.
                    var r = m.Request;
                    if (!r.IsCompleted && !r.Handled)
                    {
                        if (r.Items.Count > 0 && ContextMenuToShow is { } show) show(r);
                        else r.Dismiss();
                    }
                }
                break;

            case WebEngineEvent.ScriptDialogRequested d:
                try { ScriptDialogRequested?.Invoke(d.Request); }
                finally
                {
                    // The page's script is stopped until this is answered.
                    var r = d.Request;
                    if (!r.IsCompleted && !r.Handled)
                    {
                        if (ScriptDialogToShow is { } show) show(r);
                        else r.Cancel();
                    }
                }
                break;

            case WebEngineEvent.PermissionRequested p:
                try { PermissionRequested?.Invoke(p.Request); }
                finally
                {
                    if (!p.Request.IsCompleted && !p.Request.IsDeferred) p.Request.Deny();
                }
                break;

            case WebEngineEvent.DownloadRequested l:
                try { DownloadRequested?.Invoke(l.Request); }
                finally
                {
                    if (!l.Request.IsCompleted && !l.Request.IsDeferred) l.Request.Cancel();
                }
                break;

            case WebEngineEvent.HistoryChanged h:
                ReactiveRuntime.Batch(() =>
                {
                    _canGoBack.Value = h.CanGoBack;
                    _canGoForward.Value = h.CanGoForward;
                });
                break;

            case WebEngineEvent.MessageReceived m:
                if (Options.AcceptsMessagesFrom(m.Origin))
                    MessageReceived?.Invoke(new WebMessage(m.Json, m.Origin));
                break;

            case WebEngineEvent.NewWindowRequested n:
                NewWindowRequested?.Invoke(new WebNewWindowRequest(n.Uri, n.IsUserInitiated));
                break;

            case WebEngineEvent.CursorChanged c:
                if (Cursor == c.Cursor) return;
                Cursor = c.Cursor;
                CursorChanged?.Invoke(c.Cursor);
                break;

            case WebEngineEvent.FocusLeaving l:
                FocusLeaving?.Invoke(l.Forward);
                break;

            case WebEngineEvent.KeyFromPage k:
                KeyFromPage?.Invoke(k.Request);
                break;

            case WebEngineEvent.ProcessFailed p:
                ReactiveRuntime.Batch(() =>
                {
                    _error.Value = p.Message;
                    _state.Value = p.CanReload ? WebViewState.Crashed : WebViewState.Failed;
                    _isLoading.Value = false;
                });
                break;
        }
    }

    private void FlushPending()
    {
        // By index: a held command can raise events that hold more (a navigation whose
        // handler posts a message), and those belong at the end, in order.
        for (int i = 0; i < _pending.Count; i++)
            _pending[i].Run();
        _pending.Clear();
    }

    private void FailPending(Exception ex)
    {
        var pending = _pending.ToArray();
        _pending.Clear();
        foreach (var (_, fail) in pending) fail?.Invoke(ex);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _view.Event -= OnEngineEvent;
        _sink.FramePublished -= OnFramePublished;
        FailPending(new ObjectDisposedException(nameof(WebViewController)));
        // The GPU sink first: it has the engine free its GL objects while what they were made
        // from still exists. UI thread, where the window's GL context lives between frames.
        _gpuSink?.Dispose();
        _view.Dispose();
        _sink.Dispose();
    }

    private sealed class Host(WebViewController owner) : IWebViewHost
    {
        public PixelFrameSink Pixels => owner._sink;

        public void UseGpuRenderer(IGpuFrameRenderer renderer)
        {
            if (owner._gpuSink is not null) throw new InvalidOperationException("A GPU renderer is already registered.");
            owner._gpuSink = new GpuFrameSink(owner._sink, renderer);
        }

        public void RequestRepaint() => owner.OnRepaintRequested();

        public void Post(Action action) =>
            Application.InvokeOnMainThread(() => { if (!owner._disposed) action(); });
    }

    /// <summary>Stands in for a view the engine could not create, so no member needs a
    /// null check.</summary>
    private sealed class NoView : IWebEngineView
    {
        public WebEngineCapabilities Capabilities => WebEngineCapabilities.None;
        public event Action<WebEngineEvent>? Event { add { } remove { } }
        public void Attach(IWebViewHost host) { }
        public void SetPlacement(in WebViewPlacement placement) { }
        public void SetVisible(bool visible) { }
        public void Navigate(Uri uri) { }
        public void NavigateToString(string html) { }
        public void GoBack() { }
        public void GoForward() { }
        public void Reload() { }
        public void Stop() { }
        public Task<string> ExecuteScriptAsync(string script) => Task.FromResult("null");
        public void PostMessage(string json) { }
        public void SendPointer(in WebPointerInput input) { }
        public void SendKey(in WebKeyInput input) { }
        public void SendText(in WebTextInput input) { }
        public void Focus(FocusReason reason) { }
        public void Blur() { }
        public void Dispose() { }
    }
}
