using HultUI.WebView;
using HultUI.WebView.Testing;

namespace HultUI.Samples.WebView;

internal static class Program
{
    // WebView2 must be created on a single-threaded apartment, and the UI thread is it.
    [STAThread]
    private static void Main()
    {
        using var app = new Application("HultUI - Web view", 1100, 760);
        using var engine = CreateEngine();
        using var sample = new WebViewSampleApp(engine);
        app.SetRoot(sample.Mount());
        app.Run();
    }

    /// <summary>
    /// No engine package exists yet, so pages are the fake engine's solid colours: the
    /// window works — navigation, history, focus, the overlay — but shows no web content.
    /// </summary>
    private static IWebEngine CreateEngine() => new FakeWebEngine();
}
