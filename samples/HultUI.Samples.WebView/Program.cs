using System.Diagnostics;
using HultUI.WebView;
using HultUI.WebView.Testing;
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
        app.Run();
        return 0;
    }

    /// <summary>
    /// WebView2 where it runs; otherwise the fake engine, whose pages are solid colours: the
    /// window still works — navigation, history, focus, the menu over the page — but shows
    /// no web content. <c>--fake</c> asks for the fake anywhere.
    /// </summary>
    private static IWebEngine CreateEngine()
    {
        if (!System.Environment.GetCommandLineArgs().Contains("--fake") && OperatingSystem.IsWindows())
        {
            var webView2 = new WebView2Engine();
            if (webView2.Availability.IsAvailable) return webView2;
            Console.Error.WriteLine("WebView2: " + webView2.Availability.Reason + " Using the fake engine.");
            webView2.Dispose();
        }
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
