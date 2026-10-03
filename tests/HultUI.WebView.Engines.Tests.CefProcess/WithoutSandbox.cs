using System.Collections.Concurrent;
using System.Net;
using System.Text;
using HultUI.WebView;
using HultUI.WebView.Cef;

/// <summary>
/// <c>--without-sandbox &lt;what&gt;</c>: CEF without Chromium's sandbox, in a process of its own
/// (CEF's mode is the process's). <c>default</c> prints a default engine's availability;
/// <c>page</c> runs an engine with <c>Sandbox = false</c>; <c>untrusted</c> one that also allows
/// untrusted content; <c>env</c> a default engine under <c>HULTUI_CEF_SWITCHES=no-sandbox</c>
/// (set by the caller); <c>conflict</c> a second, sandboxed engine beside an unsandboxed one.
/// The page loads an image, a script, a fetch and a frame from the internet, over https, and
/// says what loaded (the frame shows only in the engine's log of what it refused); then the
/// main frame is sent elsewhere. <c>sandboxed</c> runs the same with a default engine.
/// </summary>
internal static class WithoutSandbox
{
    public static int Run(string what)
    {
        var options = what switch
        {
            "page" or "conflict" => new CefEngineOptions { Sandbox = false },
            // "sandboxed": a default engine running the same page, for comparison.
            "untrusted" => new CefEngineOptions { Sandbox = false, AllowUntrustedContentWithoutSandbox = true },
            _ => new CefEngineOptions(),
        };
        using var engine = new CefEngine(options);
        Console.WriteLine($"available: {engine.Availability.IsAvailable} {engine.Availability.Reason}");
        if (what == "default" || !engine.Availability.IsAvailable) return 0;

        // A profile of its own: the lifetime tests, run beside these, need the default one free.
        var profile = Path.Combine(Path.GetTempPath(), $"hultui-nosandbox-{Environment.ProcessId}");
        CefRuntime.ShutDown += _ => { try { Directory.Delete(profile, recursive: true); } catch (IOException) { } };
        var events = new ConcurrentQueue<WebEngineEvent>();
        var view = (CefView)engine.CreateView(new WebViewOptions { UserDataFolder = profile });
        view.Event += events.Enqueue;
        view.Event += e => { if (e is WebEngineEvent.NavigationStarting s) s.Request.Complete(); };
        var host = new Host();
        view.Attach(host);
        view.SetPlacement(new WebViewPlacement(0, new PixelRect(0, 0, 400, 300), 1f));
        bool Until(Func<bool> done, int ms = 20_000)
        {
            var end = DateTime.UtcNow.AddMilliseconds(ms);
            while (!done() && DateTime.UtcNow < end) Thread.Sleep(20);
            return done();
        }
        if (!Until(() => events.Any(e => e is WebEngineEvent.Ready or WebEngineEvent.Failed)))
        { Console.WriteLine("never ready"); return 2; }
        if (events.OfType<WebEngineEvent.Failed>().FirstOrDefault() is { } failed)
        { Console.WriteLine("failed: " + failed.Message); return 0; }

        if (what == "conflict")
        {
            using var second = new CefEngine();
            Console.WriteLine($"second engine available: {second.Availability.IsAvailable} {second.Availability.Reason}");
            var other = second.CreateView(new WebViewOptions { UserDataFolder = profile });
            var otherEvents = new ConcurrentQueue<WebEngineEvent>();
            other.Event += otherEvents.Enqueue;
            other.Attach(new Host());
            other.SetPlacement(new WebViewPlacement(0, new PixelRect(0, 0, 200, 100), 1f));
            Until(() => otherEvents.Any(e => e is WebEngineEvent.Ready or WebEngineEvent.Failed));
            Console.WriteLine("second page: " + (otherEvents.OfType<WebEngineEvent.Failed>().FirstOrDefault() is { } f2
                ? $"failed {f2.Kind}: {f2.Message}" : "ready"));
            return 0;
        }

        // From the internet, over https (an http one would be mixed content, refused by
        // Chromium either way): an image, a script, a fetch and a frame.
        view.NavigateToString("""
            <title>own</title><script>
            window.r = { img: 'waiting', script: 'waiting', fetch: 'waiting' };
            fetch('https://example.org/', { mode: 'no-cors' }).then(() => r.fetch = 'loaded', () => r.fetch = 'blocked');
            </script>
            <img src="https://www.wikipedia.org/static/favicon/wikipedia.ico" onload="r.img='loaded'" onerror="r.img='blocked'">
            <script src="https://cdnjs.cloudflare.com/ajax/libs/jquery/3.7.1/jquery.min.js" onload="r.script='loaded'" onerror="r.script='blocked'"></script>
            <iframe src="https://example.org/frame"></iframe>
            """);
        Console.WriteLine("own page: " + (Until(() => events.OfType<WebEngineEvent.TitleChanged>().Any(t => t.Title == "own")) ? "loaded" : "not loaded"));
        Console.WriteLine("drew: " + Until(() => host.Pixels.IsLoaded));
        Thread.Sleep(1500);
        Console.WriteLine("page saw: " + view.ExecuteScriptAsync("JSON.stringify(r)").Result);
        Console.WriteLine("sandboxed: " + engine.IsSandboxed);
        Renderers();

        // The main frame sent elsewhere: refused without the sandbox, loaded with untrusted allowed.
        var target = "https://example.org/";
        view.Navigate(new Uri(target));
        Until(() => events.OfType<WebEngineEvent.NavigationCompleted>().Any(c => c.NavigationId > 1));
        var completed = events.OfType<WebEngineEvent.NavigationCompleted>().LastOrDefault();
        Console.WriteLine($"navigation to {target}: " + (completed is null ? "nothing" : completed.IsSuccess ? "loaded" : "failed " + completed.Error));
        view.Dispose();
        Thread.Sleep(500);
        Console.Out.Flush();
        return 0;
    }

    private static bool DescendsFromMe(int pid)
    {
        for (int i = 0; i < 16 && pid > 1; i++)
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            pid = int.Parse(stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[1]);   // the parent
            if (pid == Environment.ProcessId) return true;
        }
        return false;
    }

    /// <summary>Each renderer: in a user namespace of its own (the sandbox's) or this process's,
    /// and its seccomp mode (2: a filter; 0: none).</summary>
    private static void Renderers()
    {
        var myNs = new FileInfo("/proc/self/ns/user").LinkTarget;
        string Filters(string status) => File.ReadLines(status).FirstOrDefault(l => l.StartsWith("Seccomp_filters:"))?[16..].Trim() ?? "?";
        Console.WriteLine($"this process: seccomp filters {Filters("/proc/self/status")}");
        foreach (var dir in Directory.GetDirectories("/proc"))
        {
            try
            {
                var cmd = File.ReadAllText(Path.Combine(dir, "cmdline")).Replace('\0', ' ');
                if (!cmd.Contains("hultui-cef-helper") || !cmd.Contains("--type=renderer")) continue;
                // Ours only: this process's descendants (the zygote does not pass on its environment).
                if (!DescendsFromMe(int.Parse(Path.GetFileName(dir)))) continue;
                var ns = new FileInfo(Path.Combine(dir, "ns", "user")).LinkTarget;
                Console.WriteLine($"renderer: {(ns == myNs ? "this process's user namespace" : "a user namespace of its own")}, " +
                    $"seccomp filters {Filters(Path.Combine(dir, "status"))}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
