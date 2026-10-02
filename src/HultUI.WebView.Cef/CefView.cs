using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using HultUI.Core.Events;
using HultUI.Core.Rendering;
using HultUI.WebView.Cef.Interop;

namespace HultUI.WebView.Cef;

/// <summary>
/// One CEF page, windowless: rendered off-screen and drawn by its <see cref="WebViewElement"/>.
/// Created by <see cref="CefEngine"/>; driven by its <see cref="WebViewController"/>.
/// </summary>
/// <remarks>
/// <para><b>Life.</b> Nothing is created until the first placement in a window: it says which
/// window system CEF must draw for, and CEF starts then if it has not already. The browser is
/// created asynchronously; once it exists and its DevTools session is set up (the
/// <c>window.chrome.webview</c> bridge), <see cref="WebEngineEvent.Ready"/> follows.</para>
/// <para><b>Threads.</b> The controller calls on HultUI's UI thread; every call into CEF is
/// posted to CEF's UI thread (<c>CrBrowserMain</c>), where every callback also arrives, so the
/// UI thread never waits for CEF. Events are raised there, and pixels written into the sink
/// from there; the controller marshals both.</para>
/// <para><b>Pixels.</b> <c>on_paint</c> gives the whole view as premultiplied BGRA at the
/// placement's scale; it is written into the host's sink as it is.</para>
/// </remarks>
internal sealed unsafe class CefView : IWebEngineView
{
    private enum Phase { Waiting, Starting, Ready, Failed, Disposed }

    private readonly WebViewOptions _options;
    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly object _gate = new();
    private IWebViewHost? _host;
    private Phase _phase;
    private WebViewPlacement _placement;
    private bool _hasPlacement;
    private bool _visible = true;

    // CEF's UI thread only, from here on.
    private cef_client_t* _client;
    private cef_render_handler_t* _render;
    private cef_life_span_handler_t* _lifeSpan;
    private cef_display_handler_t* _display;
    private cef_load_handler_t* _load;
    private cef_browser_t* _browser;
    private cef_browser_host_t* _browserHost;
    private DevToolsSession? _devTools;
    private readonly Dictionary<int, Uri?> _contextOrigins = [];
    private float _appliedScale;

    /// <summary>The name the page's script reaches the host through; the shim hides it.</summary>
    internal const string Binding = "__hultuiPost";

    /// <summary>
    /// <c>window.chrome.webview</c>, as WebView2 has it, built on the binding on every new
    /// document before the page's own script: <c>postMessage</c> (any value, sent as JSON) and
    /// <c>message</c> events with the host's messages.
    /// </summary>
    internal const string Shim = """
        (() => {
          const post = globalThis.__hultuiPost;
          if (typeof post !== 'function') return;
          delete globalThis.__hultuiPost;
          const target = new EventTarget();
          const webview = {
            postMessage: m => post(JSON.stringify(m)),
            addEventListener: (t, f, o) => target.addEventListener(t, f, o),
            removeEventListener: (t, f, o) => target.removeEventListener(t, f, o),
          };
          Object.defineProperty(webview, '__deliver', {
            value: json => target.dispatchEvent(new MessageEvent('message', { data: JSON.parse(json) })),
          });
          globalThis.chrome = globalThis.chrome || {};
          globalThis.chrome.webview = webview;
        })();
        """;

    /// <summary>What a string document's address reads as, as on WebView2 and the fake engine.</summary>
    private static readonly Uri AboutBlank = new("about:blank");

    public CefView(WebViewOptions options) => _options = options;

    public WebEngineCapabilities Capabilities => WebEngineCapabilities.None;

    public event Action<WebEngineEvent>? Event;

    /// <summary>For tests: CEF has just read the view's size, and whether the view was ready
    /// then. CEF's UI thread.</summary>
    internal event Action<bool>? ViewRectRead;

    private void Raise(WebEngineEvent e)
    {
        try { Event?.Invoke(e); }
        catch (Exception ex) { CefLog.Error("an event handler", ex); }
    }

    public void Attach(IWebViewHost host) => _host = host;

    // ──── Creation ────

    public void SetPlacement(in WebViewPlacement placement)
    {
        Phase phase;
        lock (_gate)
        {
            _placement = placement;
            _hasPlacement = true;
            phase = _phase;
            if (phase == Phase.Waiting && !placement.Bounds.IsEmpty) _phase = Phase.Starting;
        }
        if (phase == Phase.Waiting && !placement.Bounds.IsEmpty)
            CefRuntime.Start(placement.WindowSystem, _options.UserDataFolder).ContinueWith(t =>
            {
                if (t.Result is { } error) Fail(WebFailure.InitializationFailed, error);
                else if (CefRuntime.ResolveFolder(_options.UserDataFolder) is { Error: { } refused }) Fail(WebFailure.InitializationFailed, refused);
                else CreateBrowser();
            }, TaskScheduler.Default);
        else if (phase == Phase.Ready)
            OnUi(Resize);
    }

    private void Fail(WebFailure kind, string message)
    {
        lock (_gate)
        {
            if (_phase is Phase.Failed or Phase.Disposed) return;
            _phase = Phase.Failed;
        }
        Raise(new WebEngineEvent.Failed(kind, message));
    }

    private void OnUi(Action work)
    {
        if (!CefRuntime.Post(() =>
            {
                try { work(); }
                catch (Exception ex) { CefLog.Error("posted work", ex); }
            }))
            CefLog.Note("CEF is not running; a call to the page was dropped.");
    }

    private void CreateBrowser() => OnUi(() =>
    {
        lock (_gate) if (_phase != Phase.Starting) return;
        _client = CefObject.New<cef_client_t>(this);
        _client->get_render_handler = &GetRenderHandler;
        _client->get_life_span_handler = &GetLifeSpanHandler;
        _client->get_display_handler = &GetDisplayHandler;
        _client->get_load_handler = &GetLoadHandler;

        _render = CefObject.New<cef_render_handler_t>(this);
        _render->get_view_rect = &GetViewRect;
        _render->get_screen_info = &GetScreenInfo;
        _render->on_paint = &OnPaint;

        _lifeSpan = CefObject.New<cef_life_span_handler_t>(this);
        _lifeSpan->on_after_created = &OnAfterCreated;
        _lifeSpan->on_before_close = &OnBeforeClose;
        _lifeSpan->on_before_popup = &OnBeforePopup;

        _display = CefObject.New<cef_display_handler_t>(this);
        _display->on_title_change = &OnTitleChange;
        _display->on_address_change = &OnAddressChange;

        _load = CefObject.New<cef_load_handler_t>(this);
        _load->on_loading_state_change = &OnLoadingStateChange;

        var info = new cef_window_info_t { size = (ulong)sizeof(cef_window_info_t) };
        info.windowless_rendering_enabled = 1;
        info.runtime_style = cef_runtime_style_t.CEF_RUNTIME_STYLE_ALLOY;
        var settings = new cef_browser_settings_t { size = (ulong)sizeof(cef_browser_settings_t) };
        settings.windowless_frame_rate = 60;
        var url = CefString.Make("about:blank");
        _appliedScale = ScaleNow();
        var context = CefRuntime.ContextFor(CefRuntime.ResolveFolder(_options.UserDataFolder).ChildPath);
        if (context != null) CefObject.AddRef(context);   // the call takes a reference; the runtime keeps its own
        int ok = Capi.cef_browser_host_create_browser(&info, CefObject.Give(_client), &url, &settings, null, context);
        CefString.Clear(&url);
        if (ok != 1) Fail(WebFailure.InitializationFailed, "CEF could not create a page.");
    });

    /// <summary>The browser exists: set up the bridge, then say ready. CEF's UI thread.</summary>
    private void Created(cef_browser_t* browser)
    {
        bool disposed;
        lock (_gate) disposed = _phase == Phase.Disposed;
        if (_browser != null) return;
        _browser = CefObject.AddRef(browser);
        if (disposed)
        {
            // Disposed while CEF was creating it: close it now; on_before_close releases.
            var host = browser->get_host(browser);
            host->close_browser(host, 1);
            CefObject.Release(host);
            return;
        }
        _browserHost = browser->get_host(browser);
        if (!_visible) _browserHost->was_hidden(_browserHost, 1);
        _devTools = new DevToolsSession(_browserHost);
        _devTools.EventReceived += OnDevToolsEvent;
        _ = SetUpBridge();
    }

    private Task SetUpBridge() => CefScript.SetUpBridge(_devTools!).ContinueWith(t =>
    {
        if (t.Exception is { } ex)
        {
            Fail(WebFailure.InitializationFailed, "The page's script bridge could not be set up: " + ex.InnerException?.Message);
            return;
        }
        lock (_gate)
        {
            if (_phase != Phase.Starting) return;
            _phase = Phase.Ready;
        }
        // A placement that changed while the page was being created posted nothing (the view
        // was not ready), and CEF read the size at creation: apply the current one now.
        OnUi(Resize);
        Raise(new WebEngineEvent.Ready());
    }, TaskScheduler.Default);

    // ──── Placement ────

    private (int Width, int Height, float Scale) ViewNow()
    {
        lock (_gate)
        {
            if (!_hasPlacement || _placement.Bounds.IsEmpty) return (1, 1, 1f);
            float scale = _placement.Scale > 0 ? _placement.Scale : 1f;
            // The view in DIP, rounded up: CEF paints it at the scale, so the frame covers the
            // element's pixels; the element draws from its top-left and clips the rest.
            return (Math.Max(1, (int)Math.Ceiling(_placement.Bounds.Width / scale)),
                    Math.Max(1, (int)Math.Ceiling(_placement.Bounds.Height / scale)), scale);
        }
    }

    private float ScaleNow() => ViewNow().Scale;

    /// <summary>CEF's UI thread: the placement changed.</summary>
    private void Resize()
    {
        if (_browserHost == null) return;
        float scale = ScaleNow();
        if (scale != _appliedScale)
        {
            _appliedScale = scale;
            _browserHost->notify_screen_info_changed(_browserHost);
        }
        _browserHost->was_resized(_browserHost);
    }

    public void SetVisible(bool visible)
    {
        lock (_gate) _visible = visible;
        OnUi(() =>
        {
            if (_browserHost != null) _browserHost->was_hidden(_browserHost, visible ? 0 : 1);
        });
    }

    // ──── Navigation ────

    private void OnMainFrame(Action<nint> act) => OnUi(() =>
    {
        if (_browser == null) return;
        var frame = _browser->get_main_frame(_browser);
        try { act((nint)frame); }
        finally { CefObject.Release(frame); }
    });

    public void Navigate(Uri uri) => Load(uri.AbsoluteUri);

    public void NavigateToString(string html) => Load(StringPages.Add(_id, html).AbsoluteUri);

    private void Load(string url) => OnMainFrame(f =>
    {
        var frame = (cef_frame_t*)f;
        var u = CefString.Make(url);
        frame->load_url(frame, &u);
        CefString.Clear(&u);
    });

    public void GoBack() => OnUi(() => { if (_browser != null) _browser->go_back(_browser); });
    public void GoForward() => OnUi(() => { if (_browser != null) _browser->go_forward(_browser); });
    public void Reload() => OnUi(() => { if (_browser != null) _browser->reload(_browser); });
    public void Stop() => OnUi(() => { if (_browser != null) _browser->stop_load(_browser); });

    // ──── Script and messages ────

    public Task<string> ExecuteScriptAsync(string script)
        => _devTools is { } devTools ? CefScript.Evaluate(devTools, script) : Task.FromException<string>(new InvalidOperationException("The page is not ready."));

    /// <summary>
    /// <c>Runtime.evaluate</c>'s result as WebView2 gives it: the value as JSON, <c>null</c> for
    /// undefined; a script that threw faults the task with its message.
    /// </summary>
    internal static string ScriptResult(JsonElement result)
    {
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("exceptionDetails", out var ex))
        {
            var message = ex.TryGetProperty("exception", out var e) && e.TryGetProperty("description", out var d)
                ? d.GetString() : ex.TryGetProperty("text", out var t) ? t.GetString() : "The script threw.";
            throw new InvalidOperationException(message);
        }
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("result", out var value)
            && value.TryGetProperty("value", out var v))
            return v.GetRawText();
        return "null";
    }

    public void PostMessage(string json)
    {
        var devTools = _devTools;
        if (devTools is null) return;
        var expression = "globalThis.chrome && chrome.webview && chrome.webview.__deliver(" + JsString(json) + ")";
        _ = devTools.Send("Runtime.evaluate", w => w.WriteString("expression", expression))
            .ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>A JavaScript string literal holding <paramref name="text"/>.</summary>
    internal static string JsString(string text)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer)) w.WriteStringValue(text);
        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>CEF's UI thread: what the page's DevTools session reports.</summary>
    private void OnDevToolsEvent(string method, JsonElement p)
    {
        switch (method)
        {
            case "Runtime.executionContextCreated":
                var context = p.GetProperty("context");
                _contextOrigins[context.GetProperty("id").GetInt32()] =
                    OriginOf(context.TryGetProperty("origin", out var o) ? o.GetString() : null);
                break;
            case "Runtime.executionContextDestroyed":
                if (p.TryGetProperty("executionContextId", out var gone)) _contextOrigins.Remove(gone.GetInt32());
                break;
            case "Runtime.executionContextsCleared":
                _contextOrigins.Clear();
                break;
            case "Runtime.bindingCalled" when p.GetProperty("name").GetString() == Binding:
                var json = p.GetProperty("payload").GetString() ?? "null";
                _contextOrigins.TryGetValue(p.GetProperty("executionContextId").GetInt32(), out var origin);
                Raise(new WebEngineEvent.MessageReceived(json, origin));
                break;
        }
    }

    /// <summary>
    /// A context's origin as the contract has it: <c>scheme://host[:port]</c>, or null for an
    /// opaque one (a string document, a <c>data:</c> URL), which DevTools reports as "null" or
    /// "://".
    /// </summary>
    internal static Uri? OriginOf(string? origin)
        => Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Scheme is not ("null" or "about" or "data") && uri.Host.Length > 0
            ? uri : null;

    // ──── Input, focus: step 5.3 ────

    public void SendPointer(in WebPointerInput input) { }
    public void SendKey(in WebKeyInput input) { }
    public void SendText(in WebTextInput input) { }
    public void Focus(FocusReason reason) { }
    public void Blur() { }

    // ──── Closing ────

    public void Dispose()
    {
        lock (_gate)
        {
            if (_phase == Phase.Disposed) return;
            _phase = Phase.Disposed;
        }
        StringPages.Forget(_id);
        OnUi(() =>
        {
            _devTools?.Dispose();
            _devTools = null;
            if (_browserHost != null) _browserHost->close_browser(_browserHost, 1);
        });
    }

    /// <summary>CEF's UI thread: the browser is gone; give back what this view held.</summary>
    private void Closed()
    {
        _devTools?.Dispose();
        _devTools = null;
        if (_browserHost != null) { CefObject.Release(_browserHost); _browserHost = null; }
        if (_browser != null) { CefObject.Release(_browser); _browser = null; }
        CefObject.Release(_client); _client = null;
        CefObject.Release(_render); _render = null;
        CefObject.Release(_lifeSpan); _lifeSpan = null;
        CefObject.Release(_display); _display = null;
        CefObject.Release(_load); _load = null;
    }

    // ──── Callbacks: every one on CEF's UI thread; every struct CEF hands us is released, and
    // ──── nothing escapes one (an exception leaving an [UnmanagedCallersOnly] method ends the process)

    private static CefView? Of(void* self) => CefObject.Owner<CefView>(self);

    [UnmanagedCallersOnly]
    private static cef_render_handler_t* GetRenderHandler(cef_client_t* s)
    {
        try { return Of(s) is { } v ? CefObject.Give(v._render) : null; }
        catch (Exception ex) { CefLog.Error("get_render_handler", ex); return null; }
    }

    [UnmanagedCallersOnly]
    private static cef_life_span_handler_t* GetLifeSpanHandler(cef_client_t* s)
    {
        try { return Of(s) is { } v ? CefObject.Give(v._lifeSpan) : null; }
        catch (Exception ex) { CefLog.Error("get_life_span_handler", ex); return null; }
    }

    [UnmanagedCallersOnly]
    private static cef_display_handler_t* GetDisplayHandler(cef_client_t* s)
    {
        try { return Of(s) is { } v ? CefObject.Give(v._display) : null; }
        catch (Exception ex) { CefLog.Error("get_display_handler", ex); return null; }
    }

    [UnmanagedCallersOnly]
    private static cef_load_handler_t* GetLoadHandler(cef_client_t* s)
    {
        try { return Of(s) is { } v ? CefObject.Give(v._load) : null; }
        catch (Exception ex) { CefLog.Error("get_load_handler", ex); return null; }
    }

    [UnmanagedCallersOnly]
    private static void GetViewRect(cef_render_handler_t* s, cef_browser_t* b, cef_rect_t* rect)
    {
        try
        {
            var view = Of(s);
            var (w, h, _) = view?.ViewNow() ?? (1, 1, 1f);
            *rect = new cef_rect_t { x = 0, y = 0, width = w, height = h };
            if (view?.ViewRectRead is { } read)
            {
                bool ready;
                lock (view._gate) ready = view._phase == Phase.Ready;
                read(ready);
            }
        }
        catch (Exception ex) { CefLog.Error("get_view_rect", ex); *rect = new cef_rect_t { width = 1, height = 1 }; }
        finally { CefObject.Release(b); }
    }

    [UnmanagedCallersOnly]
    private static int GetScreenInfo(cef_render_handler_t* s, cef_browser_t* b, cef_screen_info_t* info)
    {
        try
        {
            var (w, h, scale) = Of(s)?.ViewNow() ?? (1, 1, 1f);
            info->device_scale_factor = scale;
            info->depth = 24;
            info->depth_per_component = 8;
            info->rect = new cef_rect_t { width = w, height = h };
            info->available_rect = info->rect;
            return 1;
        }
        catch (Exception ex) { CefLog.Error("get_screen_info", ex); return 0; }
        finally { CefObject.Release(b); }
    }

    [UnmanagedCallersOnly]
    private static void OnPaint(cef_render_handler_t* s, cef_browser_t* b, cef_paint_element_type_t type,
        ulong dirtyCount, cef_rect_t* dirty, void* buffer, int width, int height)
    {
        try
        {
            if (type == cef_paint_element_type_t.PET_VIEW && Of(s)?._host is { } host && width > 0 && height > 0)
                host.Pixels.Write(width, height, PixelFormat.Bgra8888,
                    new ReadOnlySpan<byte>(buffer, width * height * 4), width * 4);
        }
        catch (Exception ex) { CefLog.Error("on_paint", ex); }
        finally { CefObject.Release(b); }
    }

    [UnmanagedCallersOnly]
    private static void OnAfterCreated(cef_life_span_handler_t* s, cef_browser_t* b)
    {
        try { Of(s)?.Created(b); }
        catch (Exception ex) { CefLog.Error("on_after_created", ex); }
        finally { CefObject.Release(b); }
    }

    [UnmanagedCallersOnly]
    private static void OnBeforeClose(cef_life_span_handler_t* s, cef_browser_t* b)
    {
        try { Of(s)?.Closed(); }
        catch (Exception ex) { CefLog.Error("on_before_close", ex); }
        finally { CefObject.Release(b); }
    }

    /// <summary>New windows are refused here, always, as on WebView2; step 5.4 tells the
    /// application.</summary>
    [UnmanagedCallersOnly]
    private static int OnBeforePopup(cef_life_span_handler_t* s, cef_browser_t* b, cef_frame_t* f, int popupId,
        cef_string_utf16_t* url, cef_string_utf16_t* name, cef_window_open_disposition_t disposition, int userGesture,
        cef_popup_features_t* features, cef_window_info_t* info, cef_client_t** client, cef_browser_settings_t* settings,
        cef_dictionary_value_t** extra, int* noJavascriptAccess)
    {
        try { return 1; }
        catch (Exception ex) { CefLog.Error("on_before_popup", ex); return 1; }
        finally
        {
            CefObject.Release(b);
            CefObject.Release(f);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnTitleChange(cef_display_handler_t* s, cef_browser_t* b, cef_string_utf16_t* title)
    {
        try { Of(s)?.Raise(new WebEngineEvent.TitleChanged(CefString.Read(title))); }
        catch (Exception ex) { CefLog.Error("on_title_change", ex); }
        finally { CefObject.Release(b); }
    }

    [UnmanagedCallersOnly]
    private static void OnAddressChange(cef_display_handler_t* s, cef_browser_t* b, cef_frame_t* f, cef_string_utf16_t* url)
    {
        try
        {
            if (f->is_main(f) != 0 && Uri.TryCreate(CefString.Read(url), UriKind.Absolute, out var uri))
                Of(s)?.Raise(new WebEngineEvent.SourceChanged(StringPages.IsStringPage(uri) ? AboutBlank : uri));
        }
        catch (Exception ex) { CefLog.Error("on_address_change", ex); }
        finally
        {
            CefObject.Release(b);
            CefObject.Release(f);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnLoadingStateChange(cef_load_handler_t* s, cef_browser_t* b, int isLoading, int canGoBack, int canGoForward)
    {
        try { Of(s)?.Raise(new WebEngineEvent.HistoryChanged(canGoBack != 0, canGoForward != 0)); }
        catch (Exception ex) { CefLog.Error("on_loading_state_change", ex); }
        finally { CefObject.Release(b); }
    }
}
