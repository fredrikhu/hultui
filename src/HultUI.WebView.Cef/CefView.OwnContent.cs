using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using HultUI.WebView.Cef.Interop;

namespace HultUI.WebView.Cef;

/// <summary>
/// Running without the sandbox (decided 2026-10-03): the engine then loads only the
/// application's own content, unless the application allows untrusted content
/// (<see cref="CefEngineOptions.AllowUntrustedContentWithoutSandbox"/>). Every request of the
/// page, in every frame and of every kind, passes the resource request handler, which cancels
/// what is not the application's own; a main-frame navigation is refused before that, in
/// <c>on_before_browse</c>, and completes as failed. The developer tools' window has its own
/// client and is not limited.
/// </summary>
/// <remarks>
/// What a page's handler does not see (the review of 2026-10-03, measured on 146 and 154):
/// <list type="bullet">
/// <item>Requests with no page, a service worker's: CEF asks the request context instead, so
/// such views get a context of their own with <see cref="OwnContentContextHandler"/>, the same
/// filter (<see cref="CefRuntime.ContextFor"/>). A dedicated worker's requests are the page's.</item>
/// <item>WebSockets: the handshake passes no handler, and one opened. The pages refuse them
/// themselves: without the sandbox their documents come with a stricter
/// <c>Content-Security-Policy</c> (<see cref="StringPages.WithoutSandbox"/>).</item>
/// <item>WebRTC, over UDP: the process runs with <c>webrtc-ip-handling-policy=disable_non_proxied_udp</c>
/// (<see cref="CefRuntime.Switches"/>), and a peer connection gathers no candidates.</item>
/// </list>
/// </remarks>
internal sealed unsafe partial class CefView
{
    /// <summary>The error a navigation refused for this reason completes with.</summary>
    internal const string BlockedWithoutSandbox = "BlockedWithoutSandbox";

    /// <summary>Whether this view loads only the application's own content.</summary>
    private readonly bool _ownContentOnly;

    private cef_resource_request_handler_t* _resources;

    /// <summary>Each refused URL is logged once.</summary>
    private static readonly ConcurrentDictionary<string, byte> RefusedOnce = new();

    /// <summary>The request contexts' handler and its filter, one for the process.</summary>
    private static cef_request_context_handler_t* _contextHandler;
    private static cef_resource_request_handler_t* _contextResources;
    private static readonly object ContextHandlerGate = new();

    /// <summary>
    /// The application's own content: <see cref="WebViewOptions.AppOrigin"/>, the string pages'
    /// host, and <c>data:</c>, <c>blob:</c> and <c>about:blank</c> (and a frame's
    /// <c>about:srcdoc</c>). Anything else (the internet, the local network, <c>file:</c>, another
    /// scheme) is not.
    /// </summary>
    internal static bool IsOwnContent(string url)
    {
        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return true;
        if (url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) return true;
        if (url is "about:blank" or "about:srcdoc") return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return SameOrigin(uri, AppOrigin) || SameOrigin(uri, StringOrigin);
    }

    private static readonly Uri AppOrigin = new(WebViewOptions.AppOrigin);
    private static readonly Uri StringOrigin = new(StringPages.Origin);

    private static bool SameOrigin(Uri uri, Uri origin) =>
        string.Equals(uri.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.Host, origin.Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == origin.Port;

    private static void Refused(string url)
    {
        if (RefusedOnce.TryAdd(url, 0))
            CefLog.Note($"refused without Chromium's sandbox (not the application's own content): {url}");
    }

    private void CreateOwnContentHandler()
    {
        if (!_ownContentOnly) return;
        _resources = CefObject.New<cef_resource_request_handler_t>(this);
        _resources->on_before_resource_load = &OnBeforeResourceLoad;
        _resources->on_resource_redirect = &OnResourceRedirect;
        _request->get_resource_request_handler = &GetResourceRequestHandler;
    }

    private void ReleaseOwnContentHandler()
    {
        CefObject.Release(_resources);
        _resources = null;
    }

    /// <summary>
    /// A new reference to the handler for the request contexts of views that load only own
    /// content. CEF asks it for requests no page's client answers: a service worker's, say.
    /// </summary>
    internal static cef_request_context_handler_t* OwnContentContextHandler()
    {
        lock (ContextHandlerGate)
        {
            if (_contextHandler == null)
            {
                _contextResources = CefObject.New<cef_resource_request_handler_t>();
                _contextResources->on_before_resource_load = &OnBeforeResourceLoad;
                _contextResources->on_resource_redirect = &OnResourceRedirect;
                _contextHandler = CefObject.New<cef_request_context_handler_t>();
                _contextHandler->get_resource_request_handler = &ContextResourceRequestHandler;
            }
            return CefObject.Give(_contextHandler);
        }
    }

    // ──── Callbacks: on CEF's IO thread ────

    [UnmanagedCallersOnly]
    private static cef_resource_request_handler_t* GetResourceRequestHandler(cef_request_handler_t* s, cef_browser_t* b,
        cef_frame_t* f, cef_request_t* request, int isNavigation, int isDownload, cef_string_utf16_t* initiator, int* disableDefault)
    {
        try { return Of(s) is { _resources: not null } v ? CefObject.Give(v._resources) : null; }
        catch (Exception ex) { CefLog.Error("get_resource_request_handler", ex); return null; }
        finally
        {
            CefObject.Release(b);
            CefObject.Release(f);
            CefObject.Release(request);
        }
    }

    [UnmanagedCallersOnly]
    private static cef_resource_request_handler_t* ContextResourceRequestHandler(cef_request_context_handler_t* s,
        cef_browser_t* b, cef_frame_t* f, cef_request_t* request, int isNavigation, int isDownload,
        cef_string_utf16_t* initiator, int* disableDefault)
    {
        try { return CefObject.Give(_contextResources); }
        catch (Exception ex) { CefLog.Error("request context: get_resource_request_handler", ex); return null; }
        finally
        {
            CefObject.Release(b);
            CefObject.Release(f);
            CefObject.Release(request);
        }
    }

    [UnmanagedCallersOnly]
    private static cef_return_value_t OnBeforeResourceLoad(cef_resource_request_handler_t* s, cef_browser_t* b,
        cef_frame_t* f, cef_request_t* request, cef_callback_t* callback)
    {
        try
        {
            var url = CefString.Take(request->get_url(request));
            if (IsOwnContent(url)) return cef_return_value_t.RV_CONTINUE;
            Refused(url);
            return cef_return_value_t.RV_CANCEL;
        }
        catch (Exception ex)
        {
            CefLog.Error("on_before_resource_load", ex);
            return cef_return_value_t.RV_CANCEL;   // when in doubt, not without the sandbox
        }
        finally
        {
            CefObject.Release(b);
            CefObject.Release(f);
            CefObject.Release(request);
            CefObject.Release(callback);
        }
    }

    /// <summary>A redirect from the application's own content elsewhere goes nowhere instead.</summary>
    [UnmanagedCallersOnly]
    private static void OnResourceRedirect(cef_resource_request_handler_t* s, cef_browser_t* b, cef_frame_t* f,
        cef_request_t* request, cef_response_t* response, cef_string_utf16_t* newUrl)
    {
        try
        {
            var url = CefString.Read(newUrl);
            if (IsOwnContent(url)) return;
            Refused(url);
            CefString.Set(newUrl, "data:,");
        }
        catch (Exception ex) { CefLog.Error("on_resource_redirect", ex); }
        finally
        {
            CefObject.Release(b);
            CefObject.Release(f);
            CefObject.Release(request);
            CefObject.Release(response);
        }
    }
}
