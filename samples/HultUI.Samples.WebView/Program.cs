using System.Diagnostics;
using HultUI.WebView;
using HultUI.WebView.Testing;
using HultUI.WebView.Cef;
using HultUI.WebView.WebView2;

namespace HultUI.Samples.WebView;

internal static class Program
{
    // WebView2 must be created on a single-threaded apartment, and the UI thread is it.
    [STAThread]
    private static int Main(string[] args)
    {
        using var app = new Application("HultUI - Web view", 1100, 760);
        using var engine = CreateEngine();
        using var sample = new WebViewSampleApp(engine);
        app.SetRoot(sample.Mount());
        if (args.Contains("--check")) return Check(app, sample);
        if (args.Contains("--bench")) Application.InvokeOnMainThread(() => _ = Bench(app, sample));
        app.Run();
        return 0;
    }

    private const string AnimatedPage = """
        <html><head><title>Bench</title><style>
        body { margin:0; height:100vh; overflow:hidden;
               background: linear-gradient(90deg, #203040, #406080, #203040); background-size: 400% 100%;
               animation: pan 3s linear infinite; }
        .spin { position:absolute; left:40%; top:30%; width:200px; height:200px; background:#e0a020;
                animation: spin 1s linear infinite; }
        @keyframes pan { from { background-position: 0% 0 } to { background-position: 400% 0 } }
        @keyframes spin { to { transform: rotate(360deg) } }
        </style></head><body><div class="spin"></div></body></html>
        """;

    /// <summary>
    /// <c>--bench</c>: a page that animates every frame, in the app's own loop, for five
    /// seconds: this process's CPU per second and the page frames drawn per second. The page
    /// is the one spike W0 measured the pixel path with (about 300 ms/s at 1650×1072), so the
    /// two numbers compare. <c>HULTUI_WEBVIEW2_INTEROP=0</c> measures the upload fallback.
    /// </summary>
    private static async Task Bench(Application app, WebViewSampleApp sample)
    {
        var web = sample.Web;
        while (web.State == WebViewState.Initializing) await Task.Delay(50);
        web.NavigateToString(AnimatedPage);
        while (web.Title != "Bench") await Task.Delay(50);
        await Task.Delay(1500);

        int frames = 0;
        void Count() => frames++;
        web.FrameArrived += Count;
        var process = Process.GetCurrentProcess();
        var cpu0 = process.TotalProcessorTime;
        var clock = Stopwatch.StartNew();
        await Task.Delay(5000);
        process.Refresh();
        double seconds = clock.Elapsed.TotalSeconds;
        double cpu = (process.TotalProcessorTime - cpu0).TotalMilliseconds / seconds;
        web.FrameArrived -= Count;

        var size = web.Frames.IntrinsicSize;
        Console.WriteLine($"bench: {size.Width}x{size.Height}, {web.SinkMode} mode, " +
            $"{frames / seconds:F1} page frames/s, CPU {cpu:F0} ms/s in this process");
        app.PrimaryWindow.Close();
    }

    /// <summary>
    /// WebView2 on Windows and CEF on Linux, where they are installed; otherwise the fake
    /// engine, whose pages are solid colours: the window still works — navigation, history,
    /// focus, the menu over the page — but shows no web content. <c>--fake</c> asks for the
    /// fake anywhere.
    /// </summary>
    private static IWebEngine CreateEngine()
    {
        if (System.Environment.GetCommandLineArgs().Contains("--fake")) return new FakeWebEngine();
        IWebEngine? engine = OperatingSystem.IsWindows() ? new WebView2Engine()
            : OperatingSystem.IsLinux() ? new CefEngine()
            : null;
        if (engine is null) return new FakeWebEngine();
        if (engine.Availability.IsAvailable) return engine;
        Console.Error.WriteLine($"{engine.Name}: {engine.Availability.Reason} Using the fake engine.");
        engine.Dispose();
        return new FakeWebEngine();
    }

    /// <summary>
    /// <c>--check</c>: shows the window, waits for the home page to load and draw, asks it
    /// for its title by script, sends it a message and waits for the page's answer, then
    /// exits 0 — or 1 with what did not happen. For proving a build (NativeAOT included)
    /// works end to end without anyone looking at it.
    /// </summary>
    private static int Check(Application app, WebViewSampleApp sample)
    {
        var web = sample.Web;
        var clock = Stopwatch.StartNew();
        bool Pump(Func<bool> done)
        {
            while (!done())
            {
                if (clock.Elapsed > TimeSpan.FromSeconds(30)) return false;
                app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
                Thread.Sleep(1);
            }
            return true;
        }
        int Fail(string what)
        {
            Console.Error.WriteLine($"check failed after {clock.ElapsedMilliseconds} ms: {what} (state {web.State}, {web.Error})");
            return 1;
        }

        if (!Pump(() => web.State != WebViewState.Initializing)) return Fail("the engine never became ready");
        if (web.State != WebViewState.Ready) return Fail("the engine failed");
        if (!Pump(() => web.Title == "Home" && web.Frames.IsLoaded)) return Fail("the home page never loaded and drew");
        var title = web.ExecuteScriptAsync("document.title");
        if (!Pump(() => title.IsCompleted) || title.Result != "\"Home\"") return Fail("script did not answer");

        // The home page shows what the app posts; have it send that text straight back.
        // The listener must be in place before the message is sent: script and messages are
        // separate trips to the page, and nothing orders one after the other.
        var listening = web.ExecuteScriptAsync("chrome.webview.addEventListener('message', e => chrome.webview.postMessage({ echo: e.data }))");
        if (!Pump(() => listening.IsCompleted)) return Fail("the listener script never ran");
        web.PostMessage("{\"ping\":1}");
        if (!Pump(() => sample.LastMessage.Value?.Contains("\"echo\"") == true)) return Fail("the page never answered a message");

        Console.WriteLine($"check passed in {clock.ElapsedMilliseconds} ms: {engineName(web)} drew {web.Frames.IntrinsicSize}, answered script and messages");
        return 0;

        static string engineName(WebViewController w) => w.View.GetType().Name;
    }
}
