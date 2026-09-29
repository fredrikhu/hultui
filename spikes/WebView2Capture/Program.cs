using System.Diagnostics;
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Microsoft.Web.WebView2.Core;
using Text = HultUI.Core.Builders.Text;

namespace WebView2Capture;

internal static class Program
{
    private static HultUI.Core.Elements.TextElement _status = null!;

    [STAThread]
    private static void Main(string[] args)
    {
        string mode = args.FirstOrDefault() ?? "measure";

        using var app = new Application("WebView2 capture spike", 1100, 760);
        var sink = new PixelFrameSink();
        var page = new PageElement { Source = sink, Fit = ImageFit.Fill };
        page.FlexGrow(1).FlexBasis(0).MinHeight(0);
        var status = Text.New("starting…");
        _status = status;

        var root = Flex.Column().Children(
            Flex.Row(gap: 12).Padding(8).AlignItems(AlignItems.Center).FlexShrink(0).Children(
                Text.New("HultUI toolbar"),
                Dropdown.New(new[] { "An overlay over the page", "Second", "Third" }, selectedIndex: 0),
                status),
            page);
        app.SetRoot(root);
        sink.FramePublished += _ => Application.InvokeOnMainThread(page.MarkPaintDirty);

        var host = new Host { Window = app.PrimaryWindow, Page = page, Sink = sink };
        Application.InvokeOnMainThread(() => _ = RunAsync(app, host, mode));
        app.Run();
    }

    private static void Log(string s)
    {
        Console.WriteLine(s);
        _status.Content = s.Length > 90 ? s[..90] + "…" : s;
    }

    private static async Task RunAsync(Application app, Host host, string mode)
    {
        try
        {
            var total = Stopwatch.StartNew();
            await host.InitAsync();
            Log($"environment + composition controller: {total.ElapsedMilliseconds} ms; runtime {host.Env.BrowserVersionString}; scale {host.Scale}");

            await Probes.Placement(host, Log);
            if (mode == "interactive")
            {
                host.Web.Navigate("https://en.wikipedia.org/wiki/Special:Random");
                Log("interactive: click, scroll, type (try an IME), open the toolbar dropdown over the page");
                return;
            }

            await Probes.Latency(host, Log);
            await Probes.Animation(host, Log);
            await Probes.Windowed(host, Log);
            await Probes.FocusWindows(host, Log);
            await Probes.Accessibility(host, Log);
            await Probes.SelectPopup(host, Log);
            Log("done");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            Log("FAILED: " + ex.Message);
        }
        if (mode != "interactive" && mode != "keep")
            app.PrimaryWindow.Close();
    }
}
