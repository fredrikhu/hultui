using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CefOsr.Capi;
using static CefOsr.Native;

namespace CefOsr;

/// <summary>
/// Question 6: script results and page messages with no code of ours in the renderer, through
/// the DevTools protocol from the browser process: Runtime.evaluate for ExecuteScriptAsync,
/// Runtime.addBinding + Runtime.bindingCalled for the page's postMessage, and the posting
/// frame's origin. Messages go out as raw JSON (send_dev_tools_message) and come back through a
/// cef_dev_tools_message_observer_t.
/// </summary>
internal static unsafe class Q6
{
    static cef_browser_host_t* H;
    static int _nextId = 1;
    static readonly ConcurrentDictionary<int, string> Results = new();
    static readonly ConcurrentDictionary<int, long> SentAt = new();
    static readonly ConcurrentQueue<double> Latencies = new();
    static readonly ConcurrentQueue<(string method, string json)> DevEvents = new();
    static readonly ConcurrentQueue<string> PageLog = new();

    // The page side: window.chrome.webview, as WebView2 has it, on top of one binding.
    const string Shim = """
        (() => {
          const post = globalThis.__hultuiPost;
          if (!post) { console.log('shim: no binding in ' + location.href.slice(0, 40)); return; }
          DELETE_BINDING
          const target = new EventTarget();
          const webview = {
            postMessage: m => post(JSON.stringify(m)),
            addEventListener: (t, f, o) => target.addEventListener(t, f, o),
            removeEventListener: (t, f, o) => target.removeEventListener(t, f, o),
          };
          Object.defineProperty(webview, '__deliver', { value: json => target.dispatchEvent(new MessageEvent('message', { data: JSON.parse(json) })) });
          globalThis.chrome = globalThis.chrome || {};
          globalThis.chrome.webview = webview;
        })();
        """;

    public static int Run(Options o)
    {
        if (Host.Initialize([], o) != 1) return 1;
        Host.Pump(() => Host.ContextReady, 15000);

        nint browser = 0;
        bool loaded = false;
        Host.AfterCreated += b => { AddRef((void*)b); browser = b; };
        Host.LoadEnd += (b, f, s) => loaded = true;
        Host.ConsoleMessage += (b, m, src, line) => PageLog.Enqueue(m);

        if (o.Rest.Contains("--srcdoc-only"))
        {
            // No DevTools at all: does a srcdoc iframe in a data: page crash the renderer by itself?
            bool crashed = false;
            Host.RenderProcessTerminated += (b, st, code, text) => { crashed = true; Log($"on_render_process_terminated: {st} {code} {text}"); };
            Host.ConsoleMessage += (b, m, src, line) => Log("console: " + m);
            var page = o.Rest.Contains("--https") ? "https://example.com/" : Host.DataUrl("<title>srcdoc</title><iframe srcdoc=\"<script>console.log('in srcdoc')</script>\"></iframe>");
            Host.CreateBrowser(page, sync: false);
            Host.Pump(() => crashed || (browser != 0 && loaded), 10000);
            if (o.Rest.Contains("--https"))
            {
                loaded = false;
                var bb = (cef_browser_t*)browser;
                var mf = bb->get_main_frame(bb);
                var js = Make("document.body.insertAdjacentHTML('beforeend', `<iframe srcdoc=\"<script>console.log('in srcdoc')<\\/script>\"></iframe>`)");
                var empty = Make("");
                mf->execute_java_script(mf, &js, &empty, 0);
                Release(mf);
            }
            Host.Wait(2000);
            Log($"crashed: {crashed}, loaded: {loaded}");
            Host.Close((cef_browser_t*)browser);
            Q1.Shutdown();
            return 0;
        }
        Host.CreateBrowser(Host.DataUrl("<title>start</title>"), sync: false);
        Host.Pump(() => browser != 0 && loaded, 15000);
        var b0 = (cef_browser_t*)browser;
        H = b0->get_host(b0);

        var observer = New<cef_dev_tools_message_observer_t>("devtools_observer");
        observer->on_dev_tools_message = &OnMessage;
        observer->on_dev_tools_method_result = &OnResult;
        observer->on_dev_tools_event = &OnEvent;
        var registration = H->add_dev_tools_message_observer(H, Give(observer));
        Log($"add_dev_tools_message_observer → registration {(nint)registration:x}");

        // 1. Runtime.evaluate, as ExecuteScriptAsync.
        Log("--- Runtime.evaluate (returnByValue, awaitPromise)");
        foreach (var expr in new[] { "1 + 2", "document.title", "({ a: [1, 'two', null], b: { c: true } })",
                                     "undefined", "new Promise(r => setTimeout(() => r('later'), 50))",
                                     "throw new Error('boom')", "window" })
        {
            var sw = Stopwatch.StartNew();
            var r = Call("Runtime.evaluate", new { expression = expr, returnByValue = true, awaitPromise = true });
            Console.WriteLine($"      {expr,-48} → {Trim(r)}  ({sw.Elapsed.TotalMilliseconds:F1} ms)");
        }
        var times = new List<double>();
        for (int i = 0; i < 50; i++)
        {
            var sw = Stopwatch.StartNew();
            Call("Runtime.evaluate", new { expression = "document.title", returnByValue = true });
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        var inner = Latencies.ToArray()[^50..];
        Array.Sort(inner);
        Log($"round trip, 50 × Runtime.evaluate: send on CEF's thread → result callback median {inner[25]:F2} ms, max {inner[^1]:F2} ms; " +
            $"as our thread sees it (posting, and polling every 5 ms) median {times[25]:F2} ms");

        // 2. A binding, without Runtime.enable.
        Log("--- Runtime.addBinding('__hultuiPost') without Runtime.enable");
        Log("addBinding → " + Trim(Call("Runtime.addBinding", new { name = "__hultuiPost" })));
        Call("Runtime.evaluate", new { expression = "__hultuiPost('{\"from\":\"existing page\"}')" });
        Host.Wait(300);
        Drain();

        // 3. The shim on every new document, then a navigation, and a post from the new page.
        Log("--- Page.addScriptToEvaluateOnNewDocument(shim), Runtime.enable, navigate");
        var shim = Shim.Replace("DELETE_BINDING", o.Rest.Contains("--shim-delete") ? "delete globalThis.__hultuiPost;" : "");
        Log("addScript → " + Trim(Call("Page.addScriptToEvaluateOnNewDocument", new { source = shim, runImmediately = true })));
        if (!o.Rest.Contains("--no-page-enable"))
            Log("Page.enable → " + Trim(Call("Page.enable", new { })));
        Log("Runtime.enable → " + Trim(Call("Runtime.enable", new { })));
        Host.Wait(200);
        Drain();
        loaded = false;
        Navigate(b0, Host.DataUrl("""
            <title>poster</title><body>
            SRCDOC
            <script>
              chrome.webview.addEventListener('message', e => console.log('page got ' + JSON.stringify(e.data)));
              chrome.webview.postMessage({ from: 'main frame', n: 1 });
            </script></body>
            """.Replace("SRCDOC", o.Rest.Contains("--srcdoc")
                ? "<iframe id=\"same\" srcdoc=\"<script>chrome.webview.postMessage({from:'srcdoc iframe'})</script>\"></iframe>" : "")));
        Host.Pump(() => loaded, 10000);
        Host.Wait(500);
        Drain();

        // 4. Host → page.
        Log("--- host → page: Runtime.evaluate(chrome.webview.__deliver(json))");
        Call("Runtime.evaluate", new { expression = "chrome.webview.__deliver(" + JsonSerializer.Serialize("{\"hello\":\"from host\"}") + ")" });
        Host.Wait(200);
        Drain();

        // 5. Real origins: a page on example.com, then a cross-origin iframe from example.org.
        Log("--- https://example.com, a post from it, then an iframe from https://example.org (network)");
        string address = "";
        Host.Address += (b, url) => address = url;
        Navigate(b0, "https://example.com/");
        Host.Pump(() => address.StartsWith("https://example.com"), 15000);
        Host.Wait(1000);
        Drain();
        Call("Runtime.evaluate", new { expression = "chrome.webview.postMessage({ from: 'example.com' })" });
        Host.Wait(300);
        Drain();
        Log("add the iframe → " + Trim(Call("Runtime.evaluate", new { expression = """
            (() => { const f = document.createElement('iframe'); f.src = 'https://example.org/';
              f.onload = () => console.log('iframe loaded'); document.body.appendChild(f); return 'added'; })()
            """, returnByValue = true }), 400));
        Host.Pump(() => PageLog.Contains("iframe loaded"), 8000);
        Host.Wait(500);
        Log("iframe in the DOM: " + Trim(Call("Runtime.evaluate", new { expression = "[...document.querySelectorAll('iframe')].map(f => f.src + ' ' + f.offsetWidth + 'x' + f.offsetHeight).join()", returnByValue = true })));
        Log("frame tree: " + Trim(Call("Page.getFrameTree", new { }), 900));
        Log("after the iframe loaded, events in our (top-level) session:");
        Drain();
        Log("--- Target.setAutoAttach(flatten): the iframe as its own target");
        Log("setAutoAttach → " + Trim(Call("Target.setAutoAttach", new { autoAttach = true, waitForDebuggerOnStart = false, flatten = true })));
        Host.Wait(500);
        string? session = null;
        foreach (var e in DevEvents.ToArray())
            if (e.method == "Target.attachedToTarget")
            {
                using var d = JsonDocument.Parse(e.json);
                session = d.RootElement.GetProperty("sessionId").GetString();
                var ti = d.RootElement.GetProperty("targetInfo");
                Console.WriteLine($"      attachedToTarget: type {ti.GetProperty("type")}, url {ti.GetProperty("url")}, session {session?[..8]}…");
            }
        Drain();
        if (session != null)
        {
            Log("in the iframe's session: typeof chrome.webview → " +
                Trim(Call("Runtime.evaluate", new { expression = "typeof (globalThis.chrome && chrome.webview)", returnByValue = true }, sessionId: session)));
            Log("addBinding + the shim in that session, then a post from the iframe:");
            Call("Runtime.addBinding", new { name = "__hultuiPost" }, sessionId: session);
            Call("Runtime.evaluate", new { expression = Shim.Replace("DELETE_BINDING", "") + "; chrome.webview.postMessage({ from: 'example.org iframe' })" }, sessionId: session);
            Host.Wait(300);
            Drain();
        }

        Release(registration);
        Release(H);
        Host.Close(b0);
        Q1.Shutdown();
        return 0;
    }

    static void Navigate(cef_browser_t* b, string url)
    {
        var f = b->get_main_frame(b);
        var u = Make(url);
        f->load_url(f, &u);
        Release(f);
    }

    static string Call(string method, object args, int timeoutMs = 5000, string? sessionId = null)
    {
        int id = Interlocked.Increment(ref _nextId);
        var json = sessionId == null
            ? JsonSerializer.Serialize(new { id, method, @params = args })
            : JsonSerializer.Serialize(new { id, method, @params = args, sessionId });
        var bytes = Encoding.UTF8.GetBytes(json);
        // Only on CEF's UI thread does send_dev_tools_message say whether it took the message;
        // from any other it posts it anyway and returns 0.
        int ok = -1;
        var host = H;
        Host.PostUi(() =>
        {
            SentAt[id] = Stopwatch.GetTimestamp();
            fixed (byte* p = bytes) ok = host->send_dev_tools_message(host, p, (ulong)bytes.Length);
        });
        Host.Pump(() => ok != -1, timeoutMs);
        if (ok != 1) return $"send_dev_tools_message returned {ok}";
        Host.Pump(() => Results.ContainsKey(id), timeoutMs);
        return Results.TryRemove(id, out var r) ? r : "(no result)";
    }

    static string Trim(string s, int max = 160) => s.Length <= max ? s : s[..max] + "…";

    static void Drain()
    {
        while (DevEvents.TryDequeue(out var e))
        {
            if (e.method == "Runtime.consoleAPICalled") continue;
            string detail = e.json;
            if (e.method == "Runtime.executionContextCreated")
            {
                using var d = JsonDocument.Parse(e.json);
                var c = d.RootElement.GetProperty("context");
                detail = $"id {c.GetProperty("id")}, origin {c.GetProperty("origin")}, " +
                         $"frame {c.GetProperty("auxData").GetProperty("frameId").GetString()?[..8]}…, " +
                         $"default {c.GetProperty("auxData").GetProperty("isDefault")}";
            }
            Console.WriteLine($"      [event] {e.method}: {Trim(detail, 220)}");
        }
        while (PageLog.TryDequeue(out var m)) Console.WriteLine($"      [console] {m}");
    }

    // Raw messages: return 0 so CEF also parses them into the two calls below.
    [UnmanagedCallersOnly]
    static int OnMessage(cef_dev_tools_message_observer_t* self, cef_browser_t* b, void* message, ulong size)
    {
        // Messages of a child session (Target.setAutoAttach, flatten) carry "sessionId".
        var raw = Encoding.UTF8.GetString((byte*)message, (int)size);
        if (raw.Contains("\"sessionId\""))
        {
            using var d = JsonDocument.Parse(raw);
            if (d.RootElement.TryGetProperty("id", out var id))
                Results[id.GetInt32()] = "[session] " + raw;
            else if (!raw.Contains("Target.attachedToTarget"))
                DevEvents.Enqueue(("[session raw]", raw));
        }
        Release(b);
        return 0;
    }

    [UnmanagedCallersOnly]
    static void OnResult(cef_dev_tools_message_observer_t* self, cef_browser_t* b, int id, int success, void* result, ulong size)
    {
        var json = Encoding.UTF8.GetString((byte*)result, (int)size);
        if (SentAt.TryRemove(id, out var t0)) Latencies.Enqueue(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
        Results[id] = (success != 0 ? "" : "FAILED ") + json;
        Release(b);
    }

    [UnmanagedCallersOnly]
    static void OnEvent(cef_dev_tools_message_observer_t* self, cef_browser_t* b, cef_string_utf16_t* method, void* p, ulong size)
    {
        var m = Get(method);
        var json = Encoding.UTF8.GetString((byte*)p, (int)size);
        if (m == "Runtime.bindingCalled") json = $"{json} (on {Native.Thread()})";
        DevEvents.Enqueue((m, json));
        Release(b);
    }
}
