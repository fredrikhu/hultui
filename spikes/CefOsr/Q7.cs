using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CefOsr.Capi;
using static CefOsr.Native;

namespace CefOsr;

/// <summary>
/// Question 7: policy hooks. on_before_browse cancelling a navigation, answered by our main
/// thread while CEF's waits; on_before_popup refusing a new window; on_render_process_terminated
/// when the renderer is killed; a scheme handler factory serving https://app.local/ from a
/// folder, and NavigateToString from memory through it. Run with --loop=mt.
/// </summary>
internal static unsafe class Q7
{
    static string _root = "";
    static readonly ConcurrentDictionary<string, byte[]> Strings = new();
    static readonly ConcurrentDictionary<nint, (byte[] data, int status, string mime, int offset)> Handlers = new();
    static readonly ConcurrentQueue<string> Seen = new();
    static int _created;

    public static int Run(Options o)
    {
        _root = Path.Combine(Path.GetTempPath(), "cefosr-app-" + Environment.ProcessId);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "index.html"), """
            <!doctype html><html><head><title>app.local</title><link rel="stylesheet" href="style.css"></head>
            <body><h1 id="h">Served from a folder</h1>
            <a id="blocked" href="/blocked.html">blocked</a> <a id="allowed" href="/other.html">allowed</a>
            <a id="newwin" href="https://example.org/popup" target="_blank"
               style="position:fixed; left:0; top:300px; width:200px; height:50px; display:block">new window</a>
            <button id="wo" onclick="window.open('https://example.org/opened-by-click')"
               style="position:fixed; left:300px; top:300px; width:200px; height:50px">window.open</button>
            <script>
              console.log('page origin ' + location.origin + ' secure ' + isSecureContext +
                ' h1 colour ' + getComputedStyle(h).color);
              fetch('/missing.txt').then(r => console.log('fetch /missing.txt → ' + r.status));
              fetch('/data.json').then(r => r.json()).then(j => console.log('fetch /data.json → ' + JSON.stringify(j)));
            </script></body></html>
            """);
        File.WriteAllText(Path.Combine(_root, "style.css"), "h1 { color: rgb(200, 30, 40); }");
        File.WriteAllText(Path.Combine(_root, "data.json"), "{\"ok\":true}");
        File.WriteAllText(Path.Combine(_root, "other.html"), "<title>other</title><script>console.log('other.html loaded')</script>");
        File.WriteAllText(Path.Combine(_root, "blocked.html"), "<title>blocked</title><script>console.log('blocked.html LOADED — should not')</script>");

        if (Host.Initialize([], o) != 1) return 1;
        Host.Pump(() => Host.ContextReady, 15000);

        var factory = New<cef_scheme_handler_factory_t>("scheme_handler_factory");
        factory->create = &Create;
        int reg = 0;
        Host.PostUi(() =>
        {
            var scheme = Make("https");
            var domain = Make("app.local");
            reg = Cef.cef_register_scheme_handler_factory(&scheme, &domain, Give(factory));
        });
        Host.Pump(() => reg != 0, 5000);
        Log($"cef_register_scheme_handler_factory(https, app.local) = {reg}");

        nint browser = 0;
        string title = "";
        Host.AfterCreated += b => { if (browser == 0) { AddRef((void*)b); browser = b; } Interlocked.Increment(ref _created); };
        Host.Title += (b, t) => title = t;
        Host.ConsoleMessage += (b, m, src, line) => Seen.Enqueue($"[console] {m}");
        Host.LoadError += (b, code, text, url) => Seen.Enqueue($"[load error] {code} {text} {url}");

        // on_before_browse: CEF's thread asks; our main thread answers, as the controller would.
        var pending = new BlockingCollection<(string url, ManualResetEventSlim done, bool[] cancel)>();
        Host.BeforeBrowse += (b, f, url, gesture) =>
        {
            var frame = (cef_frame_t*)f;
            bool main = frame->is_main(frame) != 0;
            var sw = Stopwatch.StartNew();
            var done = new ManualResetEventSlim();
            var cancel = new bool[1];
            pending.Add((url, done, cancel));
            done.Wait(5000);
            Seen.Enqueue($"[host] on_before_browse {Short(url)} main {main} gesture {gesture} on {Native.Thread()}: " +
                         $"{(cancel[0] ? "CANCEL" : "allow")}, answered by our thread after {sw.Elapsed.TotalMilliseconds:F0} ms");
            return cancel[0];
        };
        string? popupUrl = null;
        Host.BeforePopup += (b, url) => { popupUrl = url; Seen.Enqueue($"[host] on_before_popup {url} on {Native.Thread()}: refuse"); return true; };
        cef_termination_status_t? terminated = null;
        Host.RenderProcessTerminated += (b, st, code, text) => { terminated = st; Seen.Enqueue($"[host] on_render_process_terminated {st} code {code} '{text}' on {Native.Thread()}"); };

        bool Answer()
        {
            // Our main thread's side: decide each navigation 30 ms after it is asked.
            while (pending.TryTake(out var p))
            {
                System.Threading.Thread.Sleep(30);
                p.cancel[0] = p.url.Contains("blocked");
                p.done.Set();
            }
            return false;
        }
        void PumpAnswering(int ms)
        {
            var end = Clock.ElapsedMilliseconds + ms;
            Host.Pump(() => Answer() || Clock.ElapsedMilliseconds >= end, ms + 1000);
        }

        Host.CreateBrowser("https://app.local/index.html", sync: false);
        PumpAnswering(2500);
        Step($"https://app.local/index.html: title '{title}'");

        var b0 = (cef_browser_t*)browser;
        Eval(b0, "document.getElementById('blocked').click()");
        PumpAnswering(1500);
        Step($"click the link to /blocked.html: title now '{title}'");

        Eval(b0, "document.getElementById('allowed').click()");
        PumpAnswering(1500);
        Step($"click the link to /other.html: title now '{title}'");

        int before = _created;
        Eval(b0, "window.open('https://example.org/opened-by-script')");
        PumpAnswering(800);
        Navigate(b0, "https://app.local/index.html");
        PumpAnswering(1500);
        Eval(b0, "document.getElementById('newwin').click()");
        PumpAnswering(800);
        Step($"window.open and a target=_blank link from script (no user gesture): browsers created since {_created - before}");
        Click(b0, 100, 325);
        PumpAnswering(800);
        Click(b0, 400, 325);
        PumpAnswering(800);
        Step($"a real click on the target=_blank link, and on a button calling window.open: browsers created since {_created - before}, last popup url {popupUrl}");

        // NavigateToString through the handler, with the srcdoc iframe that crashes a data: page.
        Strings["1"] = Encoding.UTF8.GetBytes("""
            <!doctype html><title>from a string</title><p>héllo · 日本</p>
            <iframe srcdoc="<script>console.log('srcdoc inside a served string: ok')</script>"></iframe>
            <script>console.log('string page origin ' + location.origin + ', text ' + document.querySelector('p').textContent)</script>
            """);
        Navigate(b0, "https://app.local/__string/1");
        PumpAnswering(1500);
        Step($"NavigateToString as https://app.local/__string/1: title '{title}'");

        // Kill the renderer.
        var renderers = Procs.Children().Where(c => c.Type == "renderer").ToList();
        foreach (var r in renderers) Process.GetProcessById(r.Pid).Kill();
        Host.Pump(() => terminated != null, 5000);
        Step($"SIGKILL to {renderers.Count} renderer(s)");
        b0->reload(b0);
        PumpAnswering(2000);
        Step($"browser->reload after the crash: title '{title}'");

        Host.Close(b0);
        Log($"resource handlers made: {Handlers.Count}; references still held on them by CEF: " +
            Handlers.Keys.Sum(k => Refs.TryGetValue(k, out var v) ? v : 0));
        Q1.Shutdown();
        Directory.Delete(_root, true);
        return 0;
    }

    static string Short(string url) => url.Length > 60 ? url[..60] + "…" : url;

    static void Eval(cef_browser_t* b, string js)
    {
        var f = b->get_main_frame(b);
        var code = Make(js);
        var empty = Make("");
        f->execute_java_script(f, &code, &empty, 0);
        Release(f);
    }

    static void Click(cef_browser_t* b, int x, int y)
    {
        var h = b->get_host(b);
        var e = new cef_mouse_event_t { x = x, y = y };
        h->send_mouse_move_event(h, &e, 0);
        e.modifiers = (uint)cef_event_flags_t.EVENTFLAG_LEFT_MOUSE_BUTTON;
        h->send_mouse_click_event(h, &e, cef_mouse_button_type_t.MBT_LEFT, 0, 1);
        e.modifiers = 0;
        h->send_mouse_click_event(h, &e, cef_mouse_button_type_t.MBT_LEFT, 1, 1);
        Release(h);
    }

    static void Navigate(cef_browser_t* b, string url)
    {
        var f = b->get_main_frame(b);
        var u = Make(url);
        f->load_url(f, &u);
        Release(f);
    }

    static void Step(string what)
    {
        Console.WriteLine($"--- {what}");
        while (Seen.TryDequeue(out var e)) Console.WriteLine("      " + e);
    }

    // ---- the scheme handler: one resource handler per request ----

    [UnmanagedCallersOnly]
    static cef_resource_handler_t* Create(cef_scheme_handler_factory_t* self, cef_browser_t* b, cef_frame_t* f,
        cef_string_utf16_t* scheme, cef_request_t* request)
    {
        var url = new Uri(Take(request->get_url(request)));
        Release(b); Release(f); Release(request);
        var path = Uri.UnescapeDataString(url.AbsolutePath).TrimStart('/');
        byte[] data; int status = 200; string mime;
        if (path.StartsWith("__string/") && Strings.TryGetValue(path["__string/".Length..], out var s))
        {
            data = s; mime = "text/html";
        }
        else
        {
            var file = Path.GetFullPath(Path.Combine(_root, path == "" ? "index.html" : path));
            if (file.StartsWith(_root + Path.DirectorySeparatorChar) && File.Exists(file))
            {
                data = File.ReadAllBytes(file);
                mime = Path.GetExtension(file) switch
                {
                    ".html" => "text/html", ".css" => "text/css", ".js" => "text/javascript",
                    ".json" => "application/json", ".png" => "image/png", _ => "application/octet-stream",
                };
            }
            else { data = Encoding.UTF8.GetBytes("not found"); status = 404; mime = "text/plain"; }
        }
        Seen.Enqueue($"[handler] {url.AbsolutePath} → {status} {mime}, {data.Length} bytes, on {Native.Thread()}");
        var h = New<cef_resource_handler_t>("resource_handler");
        h->open = &Open;
        h->get_response_headers = &Headers;
        h->read = &Read;
        h->skip = &Skip;
        h->cancel = &Cancel;
        Handlers[(nint)h] = (data, status, mime, 0);
        return Give(h);
    }

    [UnmanagedCallersOnly]
    static int Open(cef_resource_handler_t* self, cef_request_t* request, int* handleRequest, cef_callback_t* callback)
    {
        *handleRequest = 1;   // answered now, on this thread
        Release(request); Release(callback);
        return 1;
    }

    [UnmanagedCallersOnly]
    static void Headers(cef_resource_handler_t* self, cef_response_t* response, long* length, cef_string_utf16_t* redirect)
    {
        var st = Handlers[(nint)self];
        response->set_status(response, st.status);
        var mime = Make(st.mime);
        response->set_mime_type(response, &mime);
        if (st.mime.StartsWith("text/"))
        {
            var cs = Make("utf-8");
            response->set_charset(response, &cs);
        }
        if (Environment.GetEnvironmentVariable("CEFOSR_CSP") is { Length: > 0 } csp && st.mime == "text/html")
        {
            var n = Make("Content-Security-Policy");
            var v = Make(csp);
            response->set_header_by_name(response, &n, &v, 1);
        }
        *length = st.data.Length;
        Release(response);
    }

    [UnmanagedCallersOnly]
    static int Read(cef_resource_handler_t* self, void* dataOut, int bytesToRead, int* bytesRead, cef_resource_read_callback_t* callback)
    {
        Release(callback);
        var st = Handlers[(nint)self];
        int n = Math.Min(bytesToRead, st.data.Length - st.offset);
        if (n <= 0) { *bytesRead = 0; return 0; }   // done
        Marshal.Copy(st.data, st.offset, (nint)dataOut, n);
        Handlers[(nint)self] = st with { offset = st.offset + n };
        *bytesRead = n;
        return 1;
    }

    [UnmanagedCallersOnly]
    static int Skip(cef_resource_handler_t* self, long bytesToSkip, long* bytesSkipped, cef_resource_skip_callback_t* callback)
    {
        Release(callback);
        var st = Handlers[(nint)self];
        int n = (int)Math.Min(bytesToSkip, st.data.Length - st.offset);
        Handlers[(nint)self] = st with { offset = st.offset + n };
        *bytesSkipped = n;
        return 1;
    }

    [UnmanagedCallersOnly]
    static void Cancel(cef_resource_handler_t* self) { }
}
