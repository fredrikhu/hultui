using System.Text.RegularExpressions;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

namespace HultUI.WebView.Testing;

/// <summary>
/// A scripted engine for tests: no browser, no threads. Pages are solid colours with
/// titles; navigation commits and completes as soon as the controller allows it, with a
/// real back/forward history; every command and input is recorded on the view.
/// </summary>
/// <remarks>
/// <para>Like WebView2, a view is not ready until it is first placed with a size —
/// <see cref="ReadyOnPlacement"/> — so a controller whose element never drew stays
/// initializing, as it would for real. Set it false and call
/// <see cref="FakeWebEngineView.RaiseReady"/> to control the moment.</para>
/// <para>Events are raised synchronously on the calling thread, so under a headless
/// session a command's effects are visible as soon as it returns. The <c>Raise*</c>
/// methods inject what a page would do: post a message, open a window, change the
/// cursor, tab out, crash.</para>
/// </remarks>
public sealed class FakeWebEngine : IWebEngine
{
    public string Name => "Fake";

    public WebEngineAvailability Availability { get; set; } = WebEngineAvailability.Available;

    /// <summary>What views report. None by default: keys are forwarded through the element,
    /// as on Linux; add <see cref="WebEngineCapabilities.NativeKeyboard"/> to behave like WebView2.</summary>
    public WebEngineCapabilities Capabilities { get; set; }

    /// <summary>Become ready at the first placement with a size. Default true.</summary>
    public bool ReadyOnPlacement { get; set; } = true;

    /// <summary>Complete navigations at once. False leaves them loading until
    /// <see cref="FakeWebEngineView.CompleteNavigation"/>.</summary>
    public bool AutoComplete { get; set; } = true;

    /// <summary>A GPU renderer each view registers when attached, as a real engine's does —
    /// for testing the frame sink's wiring. Null (the default) delivers pixels only.</summary>
    public IGpuFrameRenderer? GpuRenderer { get; set; }

    /// <summary>Titles and colours by address. An address not listed loads with its host as
    /// the title and a colour derived from it.</summary>
    public Dictionary<Uri, FakePage> Pages { get; } = [];

    public List<FakeWebEngineView> Views { get; } = [];

    public bool IsDisposed { get; private set; }

    public IWebEngineView CreateView(WebViewOptions options)
    {
        var view = new FakeWebEngineView(this, options);
        Views.Add(view);
        return view;
    }

    public void Dispose() => IsDisposed = true;

    internal FakePage PageFor(Uri uri)
    {
        if (Pages.TryGetValue(uri, out var page)) return page;
        int hash = StringComparer.Ordinal.GetHashCode(uri.AbsoluteUri);
        var colour = new Color((byte)(64 + (hash & 0x7F)), (byte)(64 + ((hash >> 8) & 0x7F)), (byte)(64 + ((hash >> 16) & 0x7F)));
        return new FakePage(uri.Host, colour);
    }
}

/// <summary>A page the fake shows: its title and the colour its frames are filled with.</summary>
public sealed record FakePage(string Title, Color Colour);

/// <summary>One fake page. Everything the controller and element sent it is recorded.</summary>
public sealed class FakeWebEngineView : IWebEngineView
{
    private static readonly Uri Blank = new("about:blank");
    private readonly FakeWebEngine _engine;
    private readonly List<(Uri Uri, FakePage Page)> _history = [];
    private int _index = -1;
    private long _nextNavigation;
    private IWebViewHost? _host;
    private (long Id, Uri Uri, FakePage Page, bool Push)? _loading;

    internal FakeWebEngineView(FakeWebEngine engine, WebViewOptions options)
    {
        _engine = engine;
        Options = options;
    }

    public WebViewOptions Options { get; }
    public WebEngineCapabilities Capabilities => _engine.Capabilities;
    public event Action<WebEngineEvent>? Event;

    // ──── What was sent ────

    /// <summary>Commands in order, as text: <c>"Navigate https://a.test/"</c>, <c>"Script 1+1"</c>, <c>"Visible True"</c>.</summary>
    public List<string> Calls { get; } = [];
    public List<WebPointerInput> Pointers { get; } = [];
    public List<WebKeyInput> Keys { get; } = [];
    public List<WebTextInput> Texts { get; } = [];
    public List<string> PostedMessages { get; } = [];
    public WebViewPlacement? Placement { get; private set; }
    public bool IsVisible { get; private set; }
    public bool IsReady { get; private set; }
    public bool HasFocus { get; private set; }
    public FocusReason? LastFocusReason { get; private set; }
    public bool IsDisposed { get; private set; }

    /// <summary>What the view reports as the page's own accessibility tree; empty by default,
    /// like an engine without one. Setting it says so, as an engine must.</summary>
    public HultUI.Core.Accessibility.NativeAccessibleContent AccessibleContent
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            Raise(new WebEngineEvent.AccessibleContentChanged());
        }
    }

    /// <summary>The page's tree as data (seam W11); empty by default. Setting it says so.</summary>
    public IReadOnlyList<HultUI.Core.Accessibility.AccessibleContentNode> AccessibleTree
    {
        get;
        set
        {
            field = value;
            Raise(new WebEngineEvent.AccessibleTreeChanged());
        }
    } = [];

    /// <summary>Whether the controller has the page's tree on, as assistive technology listens.</summary>
    public bool AccessibilityEnabled { get; private set; }

    public void SetAccessibilityEnabled(bool enabled) => AccessibilityEnabled = enabled;

    /// <summary>What assistive technology asked of the page's nodes, in order.</summary>
    public List<(int Key, HultUI.Core.Accessibility.AccessibleContentAction Action, string? Value)> AccessibleActions { get; } = [];

    public void PerformAccessibleAction(int key, HultUI.Core.Accessibility.AccessibleContentAction action, string? value) =>
        AccessibleActions.Add((key, action, value));

    /// <summary>What <see cref="ExecuteScriptAsync"/> answers; returns <c>"null"</c> when unset.</summary>
    public Func<string, string>? Script { get; set; }

    /// <summary>The page on screen, or null before any navigation.</summary>
    public FakePage? CurrentPage => _index >= 0 ? _history[_index].Page : null;

    public long FramesPainted { get; private set; }

    // ──── IWebEngineView ────

    public void Attach(IWebViewHost host)
    {
        _host = host;
        Calls.Add("Attach");
        if (_engine.GpuRenderer is { } renderer) host.UseGpuRenderer(renderer);
    }

    /// <summary>What a GPU renderer's frame thread does when it has a frame: ask, from any thread.</summary>
    public void RequestRepaint() => _host?.RequestRepaint();

    public void SetPlacement(in WebViewPlacement placement)
    {
        var resized = Placement?.Bounds.Width != placement.Bounds.Width || Placement?.Bounds.Height != placement.Bounds.Height;
        Placement = placement;
        Calls.Add($"Placement {placement.Bounds.X},{placement.Bounds.Y} {placement.Bounds.Width}x{placement.Bounds.Height} @{placement.Scale}");
        if (!IsReady && _engine.ReadyOnPlacement && !placement.Bounds.IsEmpty) RaiseReady();
        else if (resized) Paint();
    }

    public void SetVisible(bool visible)
    {
        IsVisible = visible;
        Calls.Add($"Visible {visible}");
        if (visible) Paint();
    }

    public void Navigate(Uri uri)
    {
        Calls.Add($"Navigate {uri}");
        Start(uri, _engine.PageFor(uri), push: true);
    }

    public void NavigateToString(string html)
    {
        Calls.Add("NavigateToString");
        var title = Regex.Match(html, "<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        Start(Blank, new FakePage(title.Success ? title.Groups[1].Value : "", Color.White), push: true);
    }

    public void GoBack()
    {
        Calls.Add("GoBack");
        if (_index > 0) Move(_index - 1);
    }

    public void GoForward()
    {
        Calls.Add("GoForward");
        if (_index < _history.Count - 1) Move(_index + 1);
    }

    public void Reload()
    {
        Calls.Add("Reload");
        if (_index >= 0) Start(_history[_index].Uri, _history[_index].Page, push: false);
    }

    public void Stop()
    {
        Calls.Add("Stop");
        if (_loading is { } l)
        {
            _loading = null;
            Raise(new WebEngineEvent.NavigationCompleted(l.Id, false, null, "Aborted"));
        }
    }

    public Task<string> ExecuteScriptAsync(string script)
    {
        Calls.Add($"Script {script}");
        return Task.FromResult(Script?.Invoke(script) ?? "null");
    }

    public void PostMessage(string json)
    {
        Calls.Add($"PostMessage {json}");
        PostedMessages.Add(json);
    }

    public void SendPointer(in WebPointerInput input) => Pointers.Add(input);
    public void SendKey(in WebKeyInput input) => Keys.Add(input);
    public void SendText(in WebTextInput input) => Texts.Add(input);

    public void Focus(FocusReason reason)
    {
        HasFocus = true;
        LastFocusReason = reason;
        Calls.Add($"Focus {reason}");
    }

    public void Blur()
    {
        HasFocus = false;
        Calls.Add("Blur");
    }

    public void OpenDevTools() => Calls.Add("OpenDevTools");

    public void Dispose()
    {
        IsDisposed = true;
        Calls.Add("Dispose");
    }

    // ──── Navigation ────

    private void Start(Uri uri, FakePage page, bool push)
    {
        long id = ++_nextNavigation;
        var request = new WebNavigationRequest(id, uri, isUserInitiated: false, isRedirect: false, allow =>
        {
            if (!allow)
            {
                Calls.Add($"Cancelled {uri}");
                Raise(new WebEngineEvent.NavigationCompleted(id, false, null, "Cancelled"));
                return;
            }
            _loading = (id, uri, page, push);
            if (_engine.AutoComplete) CompleteNavigation();
        });
        Raise(new WebEngineEvent.NavigationStarting(request));
    }

    /// <summary>Finishes the navigation in flight: commits it, titles it, paints it.</summary>
    public void CompleteNavigation(bool success = true, int httpStatus = 200)
    {
        if (_loading is not { } l) return;
        _loading = null;
        if (!success)
        {
            Raise(new WebEngineEvent.NavigationCompleted(l.Id, false, httpStatus, "Failed"));
            return;
        }
        if (l.Push)
        {
            if (_index < _history.Count - 1) _history.RemoveRange(_index + 1, _history.Count - _index - 1);
            _history.Add((l.Uri, l.Page));
            _index = _history.Count - 1;
        }
        Commit(l.Id, httpStatus);
    }

    private void Move(int index)
    {
        _index = index;
        Commit(++_nextNavigation, 200);
    }

    private void Commit(long id, int httpStatus)
    {
        var (uri, page) = _history[_index];
        Raise(new WebEngineEvent.SourceChanged(uri));
        Raise(new WebEngineEvent.HistoryChanged(_index > 0, _index < _history.Count - 1));
        Raise(new WebEngineEvent.TitleChanged(page.Title));
        Paint();
        Raise(new WebEngineEvent.NavigationCompleted(id, true, httpStatus, null));
    }

    // ──── Frames ────

    /// <summary>Publishes a frame of the current page at the placed size, if visible.</summary>
    public void Paint()
    {
        if (_host is null || !IsVisible || Placement is not { Bounds: { IsEmpty: false } bounds }) return;
        var colour = CurrentPage?.Colour ?? Color.White;
        using var frame = _host.Pixels.BeginWrite(bounds.Width, bounds.Height, PixelFormat.Bgra8888);
        uint bgra = (uint)(colour.B | colour.G << 8 | colour.R << 16 | 0xFF << 24);
        System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(frame.Pixels).Fill(bgra);
        frame.Publish();
        FramesPainted++;
    }

    // ──── What a page or engine would do ────

    public void RaiseReady()
    {
        if (IsReady) return;
        IsReady = true;
        Raise(new WebEngineEvent.Ready());
        Paint();
    }

    public void RaiseFailed(WebFailure kind, string message) => Raise(new WebEngineEvent.Failed(kind, message));
    public void RaiseMessage(string json, Uri? origin) => Raise(new WebEngineEvent.MessageReceived(json, origin));
    public void RaiseNewWindow(Uri uri, bool userInitiated = true) => Raise(new WebEngineEvent.NewWindowRequested(uri, userInitiated));
    public void RaiseCursor(CursorType cursor) => Raise(new WebEngineEvent.CursorChanged(cursor));
    public void RaiseFocusLeaving(bool forward) => Raise(new WebEngineEvent.FocusLeaving(forward));
    public void RaiseInputArea(PixelRect area) => Raise(new WebEngineEvent.InputAreaChanged(area));

    /// <summary>A shortcut pressed in the page, as an engine with its own keyboard offers it.
    /// Returns the request, whose <see cref="WebKeyRequest.Handled"/> says whether the app took it.</summary>
    public WebKeyRequest RaiseShortcut(KeyCode key, KeyModifiers modifiers = KeyModifiers.None, bool isDown = true)
    {
        var request = new WebKeyRequest(key, modifiers, isDown);
        Raise(new WebEngineEvent.KeyFromPage(request));
        return request;
    }
    public void RaiseTitle(string title) => Raise(new WebEngineEvent.TitleChanged(title));

    /// <summary>The entries a fake page offers when none are given: the browser's own and a
    /// page's editing ones, in the order an engine lists them.</summary>
    public static IReadOnlyList<WebContextMenuItem> StandardEngineMenu { get; } =
    [
        new("back", "Back", WebContextMenuItemKind.Command, 1),
        new("reload", "Reload", WebContextMenuItemKind.Command, 2),
        WebContextMenuItem.Separator(),
        new("cut", "Cut", WebContextMenuItemKind.Command, 3) { ShortcutHint = "Ctrl+X" },
        new("copy", "Copy", WebContextMenuItemKind.Command, 4) { ShortcutHint = "Ctrl+C" },
        new("paste", "Paste", WebContextMenuItemKind.Command, 5) { ShortcutHint = "Ctrl+V" },
        WebContextMenuItem.Separator(),
        new("saveAs", "Save as", WebContextMenuItemKind.Command, 6),
        new("print", "Print", WebContextMenuItemKind.Command, 7),
        WebContextMenuItem.Separator(),
        new("inspectElement", "Inspect", WebContextMenuItemKind.Command, 8),
    ];

    /// <summary>Every drag step sent to the page.</summary>
    public List<WebDragInput> Drags { get; } = [];

    /// <summary>Whether the page takes drags; a page without a drop zone does not.</summary>
    public bool AcceptsDrops { get; set; } = true;

    public bool SendDrag(in WebDragInput input)
    {
        Drags.Add(input);
        Calls.Add($"Drag {input.Action}");
        return input.Action != WebDragAction.Leave && AcceptsDrops;
    }

    /// <summary>What the engine was told about each context menu: the entry chosen, or null for none.</summary>
    public List<WebContextMenuItem?> ContextMenuAnswers { get; } = [];

    /// <summary>A right-click in the page at device pixels (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public WebContextMenuRequest RaiseContextMenu(int x, int y, IReadOnlyList<WebContextMenuItem>? items = null,
        WebContextMenuTarget? target = null, bool devTools = false)
    {
        var request = new WebContextMenuRequest(x, y, target ?? new WebContextMenuTarget { PageUri = CurrentUri },
            items ?? StandardEngineMenu, ContextMenuAnswers.Add, devTools);
        Raise(new WebEngineEvent.ContextMenuRequested(request));
        return request;
    }

    /// <summary>What the page was told about each of its dialogs: accepted or not, and a prompt's text.</summary>
    public List<(bool Accepted, string? Text)> ScriptDialogAnswers { get; } = [];

    /// <summary>The page calls <c>alert</c>, <c>confirm</c> or <c>prompt</c>, or asks before unloading.</summary>
    public WebScriptDialogRequest RaiseScriptDialog(WebScriptDialogKind kind, string message, string? defaultText = null)
    {
        var request = new WebScriptDialogRequest(kind, message, defaultText, CurrentUri,
            (accepted, text) => ScriptDialogAnswers.Add((accepted, text)));
        Raise(new WebEngineEvent.ScriptDialogRequested(request));
        return request;
    }

    /// <summary>What the page was told about each permission it asked for.</summary>
    public List<(WebPermission Permission, bool Allowed)> PermissionAnswers { get; } = [];

    /// <summary>The page asks for a permission.</summary>
    public WebPermissionRequest RaisePermission(WebPermission permission, bool isUserInitiated = true)
    {
        var request = new WebPermissionRequest(permission, CurrentUri, isUserInitiated,
            allowed => PermissionAnswers.Add((permission, allowed)));
        Raise(new WebEngineEvent.PermissionRequested(request));
        return request;
    }

    /// <summary>Where each download was told to go; null for cancelled.</summary>
    public List<string?> DownloadAnswers { get; } = [];

    /// <summary>The page starts a download. Drive its progress through the request's <see cref="WebDownloadRequest.Download"/>.</summary>
    public WebDownloadRequest RaiseDownload(Uri uri, string suggestedFileName, long? totalBytes = null)
    {
        WebDownload? download = null;
        download = new WebDownload(() => download!.Finish(WebDownloadState.Cancelled));
        var request = new WebDownloadRequest(uri, suggestedFileName, null, totalBytes, download, DownloadAnswers.Add);
        Raise(new WebEngineEvent.DownloadRequested(request));
        return request;
    }

    private Uri? CurrentUri => _index >= 0 && _index < _history.Count ? _history[_index].Item1 : null;
    public void Crash(bool canReload = true) => Raise(new WebEngineEvent.ProcessFailed("The page's process exited.", canReload));

    /// <summary>Raises any event, from any thread — to test the controller's marshalling.</summary>
    public void Raise(WebEngineEvent e) => Event?.Invoke(e);
}
