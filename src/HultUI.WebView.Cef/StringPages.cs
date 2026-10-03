using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using HultUI.WebView.Cef.Interop;

namespace HultUI.WebView.Cef;

/// <summary>
/// <see cref="IWebEngineView.NavigateToString"/>'s documents, served by a scheme handler from a
/// host of their own, <see cref="Origin"/>, with a <c>Content-Security-Policy: sandbox</c>
/// header that leaves out <c>allow-same-origin</c>.
/// </summary>
/// <remarks>
/// <para>Not a <c>data:</c> URL: a <c>srcdoc</c> iframe inside one crashes the renderer, on 146
/// and on 154 (spike L0, question 6). Not <see cref="WebViewOptions.AppOrigin"/> either: there an untrusted string (a
/// mail body, rendered Markdown) would be same-origin with the application's own content. The
/// sandbox makes the document's origin opaque, as the contract says and WebView2 gives, so its
/// messages arrive with a null origin (decided 2026-10-02).</para>
/// <para>A page stays served for its view's life, so back, forward and reload find it again.
/// The handler is registered once, when CEF's context is up, on CEF's UI thread.</para>
/// </remarks>
internal static unsafe class StringPages
{
    public const string Host = "hultui-string.invalid";
    public static readonly string Origin = "https://" + Host;

    /// <summary>The CSP sandbox: everything a string document may need but its own origin.</summary>
    public const string Sandbox = "sandbox allow-scripts allow-forms allow-popups allow-modals";

    /// <summary>How many of a view's documents are kept: the newest, enough for back and
    /// forward through recent ones. An older one gets the "no longer available" page, so an
    /// application that renders one mail after another into a view holds a few, not all.</summary>
    public const int KeptPerView = 8;

    private static readonly ConcurrentDictionary<string, byte[]> Pages = new();
    private static readonly ConcurrentDictionary<string, Queue<string>> ByOwner = new();
    private static long _next;
    private static cef_scheme_handler_factory_t* _factory;

    /// <summary>Stores a document; its address. <paramref name="owner"/> names the view, so
    /// <see cref="Forget"/> can drop its pages.</summary>
    public static Uri Add(string owner, string html)
    {
        var id = $"{owner}/{Interlocked.Increment(ref _next)}";
        Pages[id] = Encoding.UTF8.GetBytes(html);
        var kept = ByOwner.GetOrAdd(owner, _ => new Queue<string>());
        lock (kept)
        {
            kept.Enqueue(id);
            while (kept.Count > KeptPerView) Pages.TryRemove(kept.Dequeue(), out _);
        }
        return new Uri($"{Origin}/{id}");
    }

    /// <summary>Whether the document at <paramref name="uri"/> is still served.</summary>
    internal static bool IsServed(Uri uri) => Pages.ContainsKey(uri.AbsolutePath.TrimStart('/'));

    /// <summary>Whether an address is one of these documents'.</summary>
    public static bool IsStringPage(Uri uri) => uri.Scheme == "https" && uri.Host == Host;

    public static void Forget(string owner)
    {
        if (!ByOwner.TryRemove(owner, out var kept)) return;
        lock (kept)
            foreach (var id in kept) Pages.TryRemove(id, out _);
    }

    /// <summary>CEF's UI thread, after the context is up: on the global request context.</summary>
    public static void Register()
    {
        _factory = CefObject.New<cef_scheme_handler_factory_t>();
        _factory->create = &Create;
        var scheme = CefString.Make("https");
        var domain = CefString.Make(Host);
        Capi.cef_register_scheme_handler_factory(&scheme, &domain, CefObject.Give(_factory));
        CefString.Clear(&scheme);
        CefString.Clear(&domain);
    }

    /// <summary>CEF's UI thread: on a request context of its own (a page's profile folder
    /// inside the process's), which does not inherit the global registration.</summary>
    public static void RegisterOn(cef_request_context_t* context)
    {
        var scheme = CefString.Make("https");
        var domain = CefString.Make(Host);
        context->register_scheme_handler_factory(context, &scheme, &domain, CefObject.Give(_factory));
        CefString.Clear(&scheme);
        CefString.Clear(&domain);
    }

    private sealed class Response(byte[] body, int status)
    {
        public readonly byte[] Body = body;
        public readonly int Status = status;
        public int Offset;
    }

    [UnmanagedCallersOnly]
    private static cef_resource_handler_t* Create(cef_scheme_handler_factory_t* self, cef_browser_t* browser, cef_frame_t* frame,
        cef_string_utf16_t* scheme, cef_request_t* request)
    {
        try
        {
            var url = CefString.Take(request->get_url(request));
            var id = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath.TrimStart('/') : "";
            var response = Pages.TryGetValue(id, out var body)
                ? new Response(body, 200)
                : new Response(Encoding.UTF8.GetBytes("This page is no longer available."), 404);
            var h = CefObject.New<cef_resource_handler_t>(response);
            h->open = &Open;
            h->get_response_headers = &Headers;
            h->read = &Read;
            h->skip = &Skip;
            h->cancel = &Cancel;
            return h;   // our creation reference is the one CEF receives
        }
        catch (Exception ex) { CefLog.Error("string page: create", ex); return null; }
        finally
        {
            CefObject.Release(browser);
            CefObject.Release(frame);
            CefObject.Release(request);
        }
    }

    [UnmanagedCallersOnly]
    private static int Open(cef_resource_handler_t* self, cef_request_t* request, int* handleRequest, cef_callback_t* callback)
    {
        try
        {
            *handleRequest = 1;
            return 1;
        }
        catch (Exception ex) { CefLog.Error("string page: open", ex); return 0; }
        finally
        {
            CefObject.Release(request);
            CefObject.Release(callback);
        }
    }

    [UnmanagedCallersOnly]
    private static void Headers(cef_resource_handler_t* self, cef_response_t* response, long* length, cef_string_utf16_t* redirect)
    {
        try
        {
            var r = CefObject.Owner<Response>(self)!;
            response->set_status(response, r.Status);
            var mime = CefString.Make(r.Status == 200 ? "text/html" : "text/plain");
            var charset = CefString.Make("utf-8");
            var csp = CefString.Make("Content-Security-Policy");
            var sandbox = CefString.Make(Sandbox);
            response->set_mime_type(response, &mime);
            response->set_charset(response, &charset);
            response->set_header_by_name(response, &csp, &sandbox, 1);
            CefString.Clear(&mime);
            CefString.Clear(&charset);
            CefString.Clear(&csp);
            CefString.Clear(&sandbox);
            *length = r.Body.Length;
        }
        catch (Exception ex)
        {
            CefLog.Error("string page: headers", ex);
            response->set_status(response, 500);
            *length = 0;
        }
        finally { CefObject.Release(response); }
    }

    [UnmanagedCallersOnly]
    private static int Read(cef_resource_handler_t* self, void* dataOut, int bytesToRead, int* bytesRead, cef_resource_read_callback_t* callback)
    {
        try
        {
            var r = CefObject.Owner<Response>(self)!;
            int n = Math.Min(bytesToRead, r.Body.Length - r.Offset);
            if (n <= 0) { *bytesRead = 0; return 0; }
            Marshal.Copy(r.Body, r.Offset, (nint)dataOut, n);
            r.Offset += n;
            *bytesRead = n;
            return 1;
        }
        catch (Exception ex) { CefLog.Error("string page: read", ex); *bytesRead = 0; return 0; }
        finally { CefObject.Release(callback); }
    }

    [UnmanagedCallersOnly]
    private static int Skip(cef_resource_handler_t* self, long bytesToSkip, long* bytesSkipped, cef_resource_skip_callback_t* callback)
    {
        try
        {
            var r = CefObject.Owner<Response>(self)!;
            int n = (int)Math.Min(bytesToSkip, r.Body.Length - r.Offset);
            r.Offset += n;
            *bytesSkipped = n;
            return 1;
        }
        catch (Exception ex) { CefLog.Error("string page: skip", ex); *bytesSkipped = -2; return 0; }
        finally { CefObject.Release(callback); }
    }

    [UnmanagedCallersOnly]
    private static void Cancel(cef_resource_handler_t* self)
    {
        try { if (CefObject.Owner<Response>(self) is { } r) r.Offset = r.Body.Length; }   // nothing more to read
        catch (Exception ex) { CefLog.Error("string page: cancel", ex); }
    }
}
