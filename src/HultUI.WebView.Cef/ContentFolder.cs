using System.Runtime.InteropServices;
using System.Text;
using HultUI.WebView.Cef.Interop;

namespace HultUI.WebView.Cef;

/// <summary>
/// <see cref="WebViewOptions.ContentFolder"/>: the requesting page's folder, served at
/// <see cref="WebViewOptions.AppOrigin"/> by a scheme handler registered once, like
/// <see cref="StringPages"/> (decided 2026-10-03). The setting is the page's, so the handler
/// finds the view by its browser; a page without a folder gets no handler, and the address
/// fails as it does on WebView2.
/// </summary>
/// <remarks>
/// <para>A request with no page (a service worker's) is served the folder when every live page
/// that has one has the same; otherwise it gets none.</para>
/// <para>The URL's path, percent-decoded, is resolved inside the folder; a path that leaves it
/// (<c>..</c> in any spelling, a symbolic link pointing out) is answered 403, a missing file or
/// a folder 404. No CORS headers, as WebView2's <c>DENY_CORS</c>: another origin may use the
/// files as an image or script source but cannot read them. A single byte range is answered
/// (206), so video can seek; the file is read as it is sent, not held.</para>
/// </remarks>
internal static unsafe partial class ContentFolder
{
    public static readonly string Host = new Uri(WebViewOptions.AppOrigin).Host;

    private static cef_scheme_handler_factory_t* _factory;

    /// <summary>For tests: no stricter policy without the sandbox, to see the request filter alone.</summary>
    internal static bool WithoutPolicyForTests;

    /// <summary>For tests: more hosts served the same way (the <c>.local</c> timing).</summary>
    internal static readonly List<string> ExtraHosts = [];

    /// <summary>CEF's UI thread, after the context is up: on the global request context.</summary>
    public static void Register()
    {
        _factory = CefObject.New<cef_scheme_handler_factory_t>();
        _factory->create = &Create;
        foreach (var host in Hosts())
        {
            var scheme = CefString.Make("https");
            var domain = CefString.Make(host);
            Capi.cef_register_scheme_handler_factory(&scheme, &domain, CefObject.Give(_factory));
            CefString.Clear(&scheme);
            CefString.Clear(&domain);
        }
    }

    /// <summary>CEF's UI thread: on a request context of its own, which does not inherit the
    /// global registration.</summary>
    public static void RegisterOn(cef_request_context_t* context)
    {
        foreach (var host in Hosts())
        {
            var scheme = CefString.Make("https");
            var domain = CefString.Make(host);
            context->register_scheme_handler_factory(context, &scheme, &domain, CefObject.Give(_factory));
            CefString.Clear(&scheme);
            CefString.Clear(&domain);
        }
    }

    private static IEnumerable<string> Hosts() => [Host, .. ExtraHosts];

    /// <summary>The MIME type for a file's extension, with a charset for text; octet-stream for
    /// any other.</summary>
    internal static (string Mime, string? Charset) TypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => ("text/html", "utf-8"),
        ".css" => ("text/css", "utf-8"),
        ".js" or ".mjs" => ("text/javascript", "utf-8"),
        ".json" => ("application/json", "utf-8"),
        ".svg" => ("image/svg+xml", "utf-8"),
        ".txt" => ("text/plain", "utf-8"),
        ".png" => ("image/png", null),
        ".jpg" or ".jpeg" => ("image/jpeg", null),
        ".gif" => ("image/gif", null),
        ".webp" => ("image/webp", null),
        ".ico" => ("image/x-icon", null),
        ".woff" => ("font/woff", null),
        ".woff2" => ("font/woff2", null),
        ".wasm" => ("application/wasm", null),
        ".mp4" => ("video/mp4", null),
        ".webm" => ("video/webm", null),
        ".mp3" => ("audio/mpeg", null),
        ".ogg" => ("audio/ogg", null),
        _ => ("application/octet-stream", null),
    };

    /// <summary>What a request's path is in <paramref name="folder"/> (absolute, existing): the
    /// file, or the status to answer instead (403 for a path that leaves the folder, 404 for a
    /// missing file or a folder).</summary>
    internal static (string? File, int Status) Resolve(string folder, string urlPath)
    {
        string relative;
        try { relative = Uri.UnescapeDataString(urlPath.Split('?', '#')[0]).TrimStart('/'); }
        catch (UriFormatException) { return (null, 404); }
        if (relative.Contains('\0')) return (null, 403);
        // `..` in any spelling has been decoded by now; refuse it outright rather than let a
        // path that climbs and comes back in pass.
        if (relative.Split('/').Any(s => s == "..")) return (null, 403);
        var root = Path.TrimEndingDirectorySeparator(folder);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (full == root) return (null, 404);   // the folder's own address, as any folder's
        if (!Inside(root, full)) return (null, 403);
        if (Directory.Exists(full) || !File.Exists(full)) return (null, 404);
        // A symbolic link anywhere on the way may point out: compare where it really is.
        if (RealPath(root) is not { } realRoot || RealPath(full) is not { } realFile || !Inside(realRoot, realFile))
            return (null, 403);
        return (full, 200);
    }

    private static bool Inside(string root, string path) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static string? RealPath(string path)
    {
        var resolved = realpath(path, 0);
        if (resolved == 0) return null;
        try { return Marshal.PtrToStringUTF8(resolved); }
        finally { free(resolved); }
    }

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint realpath(string path, nint resolved);

    [LibraryImport("libc")]
    private static partial void free(nint p);

    /// <summary>
    /// A request's single byte range (<c>bytes=first-last</c>, <c>bytes=first-</c>,
    /// <c>bytes=-suffix</c>) in a file of <paramref name="total"/> bytes: its bounds, or null for
    /// none (no header, or one this answers whole, as several ranges). Unsatisfiable: First past
    /// the end.
    /// </summary>
    internal static (long First, long Last)? RangeOf(string? header, long total)
    {
        if (header is null || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return null;
        var spec = header[6..].Trim();
        if (spec.Contains(',')) return null;
        int dash = spec.IndexOf('-');
        if (dash < 0) return null;
        var (a, b) = (spec[..dash].Trim(), spec[(dash + 1)..].Trim());
        if (a.Length == 0)
        {
            if (!long.TryParse(b, out var suffix) || suffix <= 0) return null;
            return (Math.Max(0, total - suffix), total - 1);
        }
        if (!long.TryParse(a, out var first) || first < 0) return null;
        long last = total - 1;
        if (b.Length > 0 && (!long.TryParse(b, out last) || last < first)) return null;
        return (first, Math.Min(last, total - 1));
    }

    /// <summary>A response: a file, read as it is sent, or a short text for an error.</summary>
    private sealed class Response(int status, string mime, string? charset, string? policy)
    {
        public int Status = status;
        public readonly string Mime = mime;
        public readonly string? Charset = charset;
        public readonly string? Policy = policy;
        public Stream? Body;
        /// <summary>What is sent: the file's length, or the range's.</summary>
        public long Length;
        /// <summary>How much of it is still to send.</summary>
        public long Remaining;
        public string? ContentRange;
        /// <summary>A range this handler answers, the body already at its start.</summary>
        public bool Ranged;
    }

    [UnmanagedCallersOnly]
    private static cef_resource_handler_t* Create(cef_scheme_handler_factory_t* self, cef_browser_t* browser, cef_frame_t* frame,
        cef_string_utf16_t* scheme, cef_request_t* request)
    {
        try
        {
            var view = CefView.ForBrowser(browser);
            var folder = browser != null ? view?.ContentFolder : CefView.SoleContentFolder();
            if (folder is null) return null;   // no folder: the address is nothing, as on WebView2
            var url = CefString.Take(request->get_url(request));
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            var (file, status) = Resolve(folder, uri.AbsolutePath);
            if (status == 403) CefLog.Note($"content folder: refused {url}, which leaves {folder}");
            // Without the sandbox, the stricter policy string documents get, for the same reason.
            bool strict = CefRuntime.IsSandboxed == false && view is not { OwnContentOnly: false } && !WithoutPolicyForTests;
            var policy = strict ? StringPages.WithoutSandbox : null;
            Response response;
            if (file is not null)
            {
                var (mime, charset) = TypeOf(file);
                var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
                response = new Response(200, mime, charset, policy) { Body = stream, Length = stream.Length };
                // A range, so video can seek: answered here, whole. CEF's own help is partial
                // (measured on 146: it skips to a range's first byte but answers 200 with the rest
                // of the file, does not skip for a suffix range, and fails one past the end), so
                // the body starts at the range here and CEF's skip is taken as done.
                if (RangeOf(Header(request, "Range"), stream.Length) is var (first, last))
                {
                    if (first >= stream.Length)
                    {
                        response = new Response(416, "text/plain", "utf-8", policy) { Body = new MemoryStream(), ContentRange = $"bytes */{stream.Length}", Ranged = true };
                        stream.Dispose();
                    }
                    else
                    {
                        stream.Position = first;
                        response.Ranged = true;
                        response.Status = 206;
                        response.Length = last - first + 1;
                        response.ContentRange = $"bytes {first}-{last}/{stream.Length}";
                    }
                }
            }
            else
            {
                var text = Encoding.UTF8.GetBytes(status == 403 ? "Forbidden" : "Not found");
                response = new Response(status, "text/plain", "utf-8", policy) { Body = new MemoryStream(text), Length = text.Length };
            }
            response.Remaining = response.Length;
            var h = CefObject.New<cef_resource_handler_t>(response);
            h->open = &Open;
            h->get_response_headers = &Headers;
            h->read = &Read;
            h->skip = &Skip;
            h->cancel = &Cancel;
            return h;   // our creation reference is the one CEF receives
        }
        catch (Exception ex) { CefLog.Error("content folder: create", ex); return null; }
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
        catch (Exception ex) { CefLog.Error("content folder: open", ex); return 0; }
        finally
        {
            CefObject.Release(request);
            CefObject.Release(callback);
        }
    }

    private static string? Header(cef_request_t* request, string name)
    {
        var n = CefString.Make(name);
        try
        {
            var value = CefString.Take(request->get_header_by_name(request, &n));
            return value.Length == 0 ? null : value;
        }
        finally { CefString.Clear(&n); }
    }

    private static void Header(cef_response_t* response, string name, string value)
    {
        var n = CefString.Make(name);
        var v = CefString.Make(value);
        response->set_header_by_name(response, &n, &v, 1);
        CefString.Clear(&n);
        CefString.Clear(&v);
    }

    [UnmanagedCallersOnly]
    private static void Headers(cef_resource_handler_t* self, cef_response_t* response, long* length, cef_string_utf16_t* redirect)
    {
        try
        {
            var r = CefObject.Owner<Response>(self)!;
            response->set_status(response, r.Status);
            var mime = CefString.Make(r.Mime);
            response->set_mime_type(response, &mime);
            CefString.Clear(&mime);
            if (r.Charset is { } c)
            {
                var charset = CefString.Make(c);
                response->set_charset(response, &charset);
                CefString.Clear(&charset);
            }
            if (r.Status is 200 or 206) Header(response, "Accept-Ranges", "bytes");
            if (r.ContentRange is { } range) Header(response, "Content-Range", range);
            if (r.Policy is { } policy) Header(response, "Content-Security-Policy", policy);
            *length = r.Length;
        }
        catch (Exception ex)
        {
            CefLog.Error("content folder: headers", ex);
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
            int n = r.Body?.Read(new Span<byte>(dataOut, (int)Math.Min(bytesToRead, r.Remaining))) ?? 0;
            r.Remaining -= n;
            *bytesRead = n;
            if (n > 0) return 1;
            Close(r);
            return 0;
        }
        catch (Exception ex) { CefLog.Error("content folder: read", ex); *bytesRead = 0; return 0; }
        finally { CefObject.Release(callback); }
    }

    [UnmanagedCallersOnly]
    private static int Skip(cef_resource_handler_t* self, long bytesToSkip, long* bytesSkipped, cef_resource_skip_callback_t* callback)
    {
        try
        {
            var r = CefObject.Owner<Response>(self)!;
            if (r.Ranged) { *bytesSkipped = bytesToSkip; return 1; }   // the body is at the range already
            if (r.Body is not { } body) { *bytesSkipped = -2; return 0; }
            long n = Math.Min(bytesToSkip, body.Length - body.Position);
            body.Position += n;
            *bytesSkipped = n;
            return 1;
        }
        catch (Exception ex) { CefLog.Error("content folder: skip", ex); *bytesSkipped = -2; return 0; }
        finally { CefObject.Release(callback); }
    }

    [UnmanagedCallersOnly]
    private static void Cancel(cef_resource_handler_t* self)
    {
        try { if (CefObject.Owner<Response>(self) is { } r) Close(r); }
        catch (Exception ex) { CefLog.Error("content folder: cancel", ex); }
    }

    private static void Close(Response r)
    {
        var body = Interlocked.Exchange(ref r.Body, null);
        body?.Dispose();
    }
}
