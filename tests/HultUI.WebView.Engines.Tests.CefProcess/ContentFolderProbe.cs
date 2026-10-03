using System.Collections.Concurrent;
using System.Diagnostics;
using HultUI.WebView;
using HultUI.WebView.Cef;

/// <summary>
/// <c>--content-folder &lt;what&gt;</c>: <see cref="WebViewOptions.ContentFolder"/> served at
/// <see cref="WebViewOptions.AppOrigin"/>, in a process of its own with a profile of its own.
/// <c>serve</c>: a page with its stylesheet, JSON and message; every listed file type; a range;
/// the ways out of the folder; two pages with folders of their own; a page with none.
/// <c>own</c> and <c>own-sandboxed</c>: a page that frames the internet, opens a WebSocket and
/// has a service worker fetch from the internet, without the sandbox and with it;
/// <c>own-filter</c> the same without the stricter policy, to see the request filter alone.
/// <c>timing</c>: navigations to the origin against the same folder under <c>app.local</c>.
/// </summary>
internal static class ContentFolderProbe
{
    private sealed record Page(CefView View, ConcurrentQueue<WebEngineEvent> Events);

    public static int Run(string what)
    {
        var options = what is "own" or "own-filter" ? new CefEngineOptions { Sandbox = false } : new CefEngineOptions();
        using var engine = new CefEngine(options);
        Console.WriteLine($"available: {engine.Availability.IsAvailable} {engine.Availability.Reason}");
        if (!engine.Availability.IsAvailable) return 0;
        if (what == "timing") ContentFolder.ExtraHosts.Add("app.local");
        if (what == "own-filter") ContentFolder.WithoutPolicyForTests = true;

        var temp = Directory.CreateTempSubdirectory("hultui-content-").FullName;
        var profile = Path.Combine(Path.GetTempPath(), $"hultui-content-profile-{Environment.ProcessId}");
        CefRuntime.ShutDown += _ =>
        {
            try { Directory.Delete(profile, recursive: true); } catch (IOException) { }
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { }
        };

        Page Open(string? folder)
        {
            var events = new ConcurrentQueue<WebEngineEvent>();
            var view = (CefView)engine.CreateView(new WebViewOptions { UserDataFolder = profile, ContentFolder = folder });
            view.Event += events.Enqueue;
            view.Event += e => { if (e is WebEngineEvent.NavigationStarting s) s.Request.Complete(); };
            view.Attach(new Host());
            view.SetPlacement(new WebViewPlacement(0, new PixelRect(0, 0, 400, 300), 1f));
            Until(() => events.Any(e => e is WebEngineEvent.Ready or WebEngineEvent.Failed));
            return new Page(view, events);
        }

        return what switch
        {
            "serve" => Serve(temp, Open),
            "timing" => Timing(temp, Open),
            _ => Own(temp, Open),
        };
    }

    private static bool Until(Func<bool> done, int ms = 20_000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (!done() && DateTime.UtcNow < end) Thread.Sleep(20);
        return done();
    }

    private static string? Message(Page p, int ms = 15_000)
    {
        Until(() => p.Events.Any(e => e is WebEngineEvent.MessageReceived), ms);
        return p.Events.OfType<WebEngineEvent.MessageReceived>().FirstOrDefault() is { } m ? $"{m.Json} from {m.Origin}" : null;
    }

    private static WebEngineEvent.NavigationCompleted? Completed(Page p, long after = 0)
    {
        Until(() => p.Events.OfType<WebEngineEvent.NavigationCompleted>().Any(c => c.NavigationId > after));
        return p.Events.OfType<WebEngineEvent.NavigationCompleted>().LastOrDefault(c => c.NavigationId > after);
    }

    /// <summary>The page reports with the bridge once its stylesheet and its JSON are there.</summary>
    private const string IndexHtml = """
        <html><head><link rel=stylesheet href=style.css></head><body>
        <script>
          fetch('data.json').then(r => r.json()).then(d =>
            chrome.webview.postMessage(getComputedStyle(document.body).backgroundColor + ' ' + d.answer));
        </script></body></html>
        """;

    private static int Serve(string temp, Func<string?, Page> open)
    {
        var a = Directory.CreateDirectory(Path.Combine(temp, "a")).FullName;
        var b = Directory.CreateDirectory(Path.Combine(temp, "b")).FullName;
        File.WriteAllText(Path.Combine(a, "index.html"), IndexHtml);
        File.WriteAllText(Path.Combine(a, "style.css"), "body { background: rgb(10, 200, 30); }");
        File.WriteAllText(Path.Combine(a, "data.json"), "{\"answer\":42}");
        File.WriteAllText(Path.Combine(b, "index.html"), IndexHtml);
        File.WriteAllText(Path.Combine(b, "style.css"), "body { background: rgb(200, 10, 30); }");
        File.WriteAllText(Path.Combine(b, "data.json"), "{\"answer\":7}");
        // Each listed type, and one more.
        string[] types = ["html", "css", "js", "mjs", "json", "svg", "png", "jpeg", "jpg", "gif", "webp", "ico",
                          "woff", "woff2", "wasm", "mp4", "webm", "mp3", "ogg", "txt", "bin"];
        Directory.CreateDirectory(Path.Combine(a, "types"));
        foreach (var t in types) File.WriteAllText(Path.Combine(a, "types", "f." + t), "x");
        File.WriteAllBytes(Path.Combine(a, "range.bin"), Enumerable.Range(0, 1000).Select(i => (byte)(i % 256)).ToArray());
        Directory.CreateDirectory(Path.Combine(a, "sub"));
        File.WriteAllText(Path.Combine(a, "sub", "inner.txt"), "inner");
        // Outside the folder, and links inside it pointing there.
        File.WriteAllText(Path.Combine(temp, "outside.txt"), "outside");
        File.CreateSymbolicLink(Path.Combine(a, "link.txt"), Path.Combine(temp, "outside.txt"));
        File.CreateSymbolicLink(Path.Combine(a, "linkdir"), temp);
        File.CreateSymbolicLink(Path.Combine(a, "inlink.txt"), Path.Combine(a, "sub", "inner.txt"));

        // Two pages, each with a folder of its own, at the same address.
        var pa = open(a);
        var pb = open(b);
        pa.View.Navigate(new Uri(WebViewOptions.AppOrigin + "/index.html"));
        pb.View.Navigate(new Uri(WebViewOptions.AppOrigin + "/index.html"));
        var ma = Message(pa);
        var mb = Message(pb);
        Console.WriteLine("page a: " + (ma ?? "no message"));
        Console.WriteLine("page b: " + (mb ?? "no message"));
        var origin = pa.Events.OfType<WebEngineEvent.MessageReceived>().FirstOrDefault()?.Origin;
        Console.WriteLine("accepted by default: " + new WebViewOptions().AcceptsMessagesFrom(origin));

        // From inside page a: each type's header, a range, and the ways out.
        string Fetch(string script) => pa.View.ExecuteScriptAsync(script).Result;
        Console.WriteLine("types: " + Fetch($$"""
            Promise.all({{System.Text.Json.JsonSerializer.Serialize(types)}}.map(t =>
              fetch('types/f.' + t).then(r => t + '=' + r.headers.get('content-type')))).then(a => a.join(' '))
            """));
        Console.WriteLine("ranges: " + Fetch("""
            Promise.all(['bytes=10-19', 'bytes=990-', 'bytes=-5', 'bytes=995-2000', 'bytes=1000-', 'bytes=0-1,5-6'].map(h =>
              fetch('range.bin', { headers: { Range: h } }).then(async r => {
                const b = new Uint8Array(await r.arrayBuffer());
                return h + ':' + r.status + ',' + r.headers.get('content-range') + ',' + b.length + ',' + (b.length ? b[0] + '..' + b[b.length - 1] : '-');
              }, e => h + ':error'))).then(a => a.join(' '))
            """));
        Console.WriteLine("paths: " + Fetch("""
            Promise.all(['/..%2foutside.txt', '/%2e%2e%2foutside.txt', '/%2e%2e/outside.txt', '/sub/..%2f..%2foutside.txt',
                         '/link.txt', '/linkdir/outside.txt', '/inlink.txt', '/sub/inner.txt', '/sub%2finner.txt',
                         '/sub/', '/', '/missing.txt'].map(p =>
              fetch(p).then(r => p + '=' + r.status, e => p + '=error'))).then(a => a.join(' '))
            """));
        // Another origin (a string document in a page with the folder) may use a file as a
        // script, but not read one: no CORS headers.
        pa.View.NavigateToString("""
            <title>elsewhere</title><script>
            window.c = { script: 'waiting', fetch: 'waiting' };
            fetch('https://app.example/data.json').then(() => c.fetch = 'read', () => c.fetch = 'refused');
            </script>
            <script src="https://app.example/types/f.js" onload="c.script = 'loaded'" onerror="c.script = 'failed'"></script>
            """);
        Until(() => pa.Events.OfType<WebEngineEvent.TitleChanged>().Any(t => t.Title == "elsewhere"));
        Thread.Sleep(1000);
        Console.WriteLine("from another origin: " + pa.View.ExecuteScriptAsync("JSON.stringify(c)").Result);

        // A page without a folder: the address is nothing.
        var none = open(null);
        none.View.Navigate(new Uri(WebViewOptions.AppOrigin + "/index.html"));
        var c = Completed(none);
        Console.WriteLine("without a folder: " + (c is null ? "nothing" : c.IsSuccess ? "loaded" : "failed " + c.Error));

        foreach (var p in new[] { pa, pb, none }) p.View.Dispose();
        Thread.Sleep(500);
        Console.Out.Flush();
        return 0;
    }

    private static int Own(string temp, Func<string?, Page> open)
    {
        File.WriteAllText(Path.Combine(temp, "style.css"), "body { background: rgb(10, 200, 30); }");
        File.WriteAllText(Path.Combine(temp, "data.json"), "{\"answer\":42}");
        File.WriteAllText(Path.Combine(temp, "sw.js"), """
            self.addEventListener('install', () => self.skipWaiting());
            self.addEventListener('activate', e => e.waitUntil(clients.claim()));
            self.addEventListener('message', e => {
              fetch('https://example.org/from-a-service-worker', { mode: 'no-cors' })
                .then(() => e.source.postMessage('loaded'), () => e.source.postMessage('blocked'));
            });
            """);
        File.WriteAllText(Path.Combine(temp, "index.html"), """
            <html><head><title>own</title><link rel=stylesheet href=style.css></head><body>
            <script>
              window.r = { data: 'waiting', socket: 'waiting', worker: 'waiting', violations: [] };
              document.addEventListener('securitypolicyviolation', e => r.violations.push(e.blockedURI));
              fetch('data.json').then(x => x.json()).then(d => r.data = getComputedStyle(document.body).backgroundColor + ' ' + d.answer);
              try {
                const ws = new WebSocket('wss://ws.postman-echo.com/raw');
                ws.onopen = () => { r.socket = 'open'; ws.close(); };
                ws.onerror = () => r.socket = 'error';
              } catch (e) { r.socket = 'threw ' + e.name; }
              navigator.serviceWorker.register('sw.js').then(() => navigator.serviceWorker.ready).then(reg => {
                navigator.serviceWorker.onmessage = e => r.worker = e.data;
                reg.active.postMessage('go');
              }).catch(e => r.worker = 'threw ' + e.message);
            </script>
            <iframe src="https://example.org/frame"></iframe>
            </body></html>
            """);
        var page = open(temp);
        page.View.Navigate(new Uri(WebViewOptions.AppOrigin + "/index.html"));
        var c = Completed(page);
        Console.WriteLine("own page: " + (c is null ? "nothing" : c.IsSuccess ? "loaded" : "failed " + c.Error));
        Until(() => page.View.ExecuteScriptAsync("r.worker").Result != "\"waiting\"", 8000);
        Thread.Sleep(1000);
        Console.WriteLine("own page saw: " + page.View.ExecuteScriptAsync("JSON.stringify(r)").Result);
        Console.WriteLine("sandboxed: " + CefRuntime.IsSandboxed);
        page.View.Dispose();
        Thread.Sleep(500);
        Console.Out.Flush();
        return 0;
    }

    /// <summary>The same page under AppOrigin and under app.local, each navigation timed.</summary>
    private static int Timing(string temp, Func<string?, Page> open)
    {
        File.WriteAllText(Path.Combine(temp, "index.html"), "<html><body>timing</body></html>");
        var page = open(temp);
        foreach (var origin in new[] { WebViewOptions.AppOrigin, "https://app.local" })
        {
            var times = new List<long>();
            for (int i = 0; i < 6; i++)
            {
                long before = page.Events.OfType<WebEngineEvent.NavigationCompleted>().Select(n => n.NavigationId).DefaultIfEmpty(0).Max();
                var clock = Stopwatch.StartNew();
                page.View.Navigate(new Uri($"{origin}/index.html?{i}"));
                var c = Completed(page, before);
                times.Add(c is { IsSuccess: true } ? clock.ElapsedMilliseconds : -1);
            }
            Console.WriteLine($"{origin}: {string.Join(" ", times)} ms");
        }
        page.View.Dispose();
        Thread.Sleep(500);
        Console.Out.Flush();
        return 0;
    }
}
