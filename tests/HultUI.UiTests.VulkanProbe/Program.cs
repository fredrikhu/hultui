using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.WebView;
using HultUI.WebView.WebView2;

// Opens a window the way an app asks for Vulkan, in code, so a start that fails may fall back to
// GL. It draws a fixed number of forced frames and exits, so HULTUI_VULKAN_CAPTURE's default frame
// (the first to reuse a swapchain image) is always reached. The left half is red and the right
// half blue, so a capture can be checked for more than "not black".
//
// --renderer=default asks for nothing, --frames=N changes the frame count (12).
//
// --webviews shows two web pages side by side instead — green on the left, magenta on the right
// — each a GPU source of its own in one Vulkan window (seam W7). It waits for both to draw, then
// forces frames until HULTUI_VULKAN_CAPTURE has been written, so the capture's frame number only
// has to come after the pages are ready. Then it disposes the right page, whose image the window
// should give back.

internal static class Program
{
    // WebView2 wants the UI thread to be a single-threaded apartment.
    [STAThread]
    private static int Main(string[] args)
    {
        var renderer = args.Contains("--renderer=default") ? RendererKind.Default : RendererKind.Vulkan;
        if (args.Contains("--webviews")) return TwoPages(renderer);

        int frames = args.FirstOrDefault(a => a.StartsWith("--frames=")) is { } f ? int.Parse(f["--frames=".Length..]) : 12;

        using var app = new Application("vulkan-probe", 320, 200, renderer: renderer);
        app.SetRoot(Flex.Row().Width(320f).Height(200f).Children(
            Box.New().FlexGrow(1).Background(Color.FromHex("#FF0000")),
            Box.New().FlexGrow(1).Background(Color.FromHex("#0000FF"))));

        for (int i = 0; i < frames; i++)
            app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);

        Console.WriteLine("probe: done");
        return 0;
    }

    private static int TwoPages(RendererKind renderer)
    {
        using var app = new Application("vulkan-probe", 480, 240, renderer: renderer);
        using var engine = new WebView2Engine();
        var options = new WebViewOptions { UserDataFolder = Path.Combine(Path.GetTempPath(), "hultui-webview2-tests") };
        using var left = new WebViewController(engine, options);
        using var right = new WebViewController(engine, options);
        var ready = new HashSet<string>();
        left.MessageReceived += m => ready.Add("left");
        right.MessageReceived += m => ready.Add("right");
        left.NavigateToString(Page("#00FF00"));
        right.NavigateToString(Page("#FF00FF"));
        app.SetRoot(Flex.Row().Width(480f).Height(240f).Children(
            WebViewElement.New(left).FlexGrow(1).FlexBasis(0),
            WebViewElement.New(right).FlexGrow(1).FlexBasis(0)));

        // Frames only as the pages ask for them until both have drawn, so few are counted
        // before the capture's frame number starts to mean anything.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!(ready.Count == 2 && left.Frames.IsLoaded && right.Frames.IsLoaded))
        {
            if (clock.Elapsed.TotalSeconds > 20) { Console.WriteLine("probe: the pages never drew"); return 2; }
            app.RunFrame(TimeSpan.FromMilliseconds(16));
        }
        Console.WriteLine($"probe: both pages drew after {clock.ElapsedMilliseconds} ms");

        string? capture = Environment.GetEnvironmentVariable("HULTUI_VULKAN_CAPTURE");
        clock.Restart();
        while (capture is not null && !File.Exists(capture) && clock.Elapsed.TotalSeconds < 20)
            app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);

        // The right page goes away, and its shared image with it, while the left keeps drawing.
        app.SetRoot(Flex.Row().Width(480f).Height(240f).Children(WebViewElement.New(left).FlexGrow(1)));
        right.Dispose();
        for (int i = 0; i < 10; i++)
            app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        Console.WriteLine("probe: done");
        return 0;
    }

    private static string Page(string colour) =>
        $"<html><body style='margin:0;background:{colour}'><script>chrome.webview.postMessage('ready');</script></body></html>";
}
