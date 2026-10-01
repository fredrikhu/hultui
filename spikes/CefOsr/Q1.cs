using CefOsr.Capi;
using static CefOsr.Native;

namespace CefOsr;

/// <summary>
/// Question 1: does Fedora's libcef load and start, windowless, from a .NET program? Starts
/// CEF with the options, opens one windowless browser on a data: page, then chrome://gpu,
/// and reports the children, their sandbox, the Ozone platform and the GL in use.
/// </summary>
internal static unsafe class Q1
{
    public static int Run(Options o)
    {
        long t0 = Clock.ElapsedMilliseconds;
        if (Host.Initialize([], o) != 1) return 1;
        Host.Pump(() => Host.ContextReady, 15000);
        Log($"context ready {Clock.ElapsedMilliseconds - t0} ms after start");

        int paints = 0;
        bool loaded = false;
        string title = "";
        Host.Paint += (b, type, w, h, buf, n, d) =>
        {
            if (paints++ == 0) Log($"first on_paint: {type} {w}x{h}");
        };
        Host.LoadEnd += (b, f, status) => { loaded = true; Log($"on_load_end: HTTP {status}"); };
        Host.Title += (b, t) => title = t;

        long tc = Clock.ElapsedMilliseconds;
        var browser = Host.CreateBrowser(Host.DataUrl("<title>Q1</title><body style='background:#2a6'>hello</body>"));
        Host.Pump(() => loaded && paints > 0, 15000);
        Log($"page loaded and painted {Clock.ElapsedMilliseconds - tc} ms after create_browser; title '{title}'");

        // What the page's GL is: WebGL's unmasked renderer, reported through the console.
        // (chrome://gpu would say more, but in a windowless Alloy browser it never loads: CEF
        // logs "Timeout of new browser info response" for its frame.)
        string gl = "";
        Host.ConsoleMessage += (b, m, src, line) => { if (m.StartsWith("GL:")) gl = m; };
        var frame = browser->get_main_frame(browser);
        var probeUrl = Make(Host.DataUrl("<script>var c=document.createElement('canvas');" +
            "var g=c.getContext('webgl2')||c.getContext('webgl');" +
            "var e=g&&g.getExtension('WEBGL_debug_renderer_info');" +
            "console.log('GL: '+(g?(e?g.getParameter(e.UNMASKED_VENDOR_WEBGL)+' | '+g.getParameter(e.UNMASKED_RENDERER_WEBGL):'no debug info'):'no WebGL'));</script>"));
        frame->load_url(frame, &probeUrl);
        Release(frame);
        Host.Pump(() => gl != "", 10000);
        Log(gl == "" ? "WebGL probe: no answer" : "WebGL probe: " + gl);

        Log("children launched:");
        foreach (var c in Host.ChildLaunches)
            Console.WriteLine("    " + Shorten(c));
        Log("children now:");
        foreach (var line in Procs.Report()) Console.WriteLine("    " + line);

        Host.Close(browser);
        Shutdown();
        return 0;
    }

    public static string Shorten(string cmd)
    {
        // The interesting part: the executable, the type, and switches that say how it runs.
        var parts = cmd.Split(' ');
        var keep = parts.Where((p, i) => i == 0 || p.StartsWith("--type") || p.Contains("sandbox") ||
            p.StartsWith("--ozone") || p.StartsWith("--use-gl") || p.StartsWith("--use-angle") ||
            p.StartsWith("--gpu-preferences") == false && p.StartsWith("--enable-features") ||
            p.StartsWith("--change-stack") || p.StartsWith("--render-node") || p.StartsWith("--lang") ||
            p.StartsWith("--resources-dir") || p.StartsWith("--locales-dir") || p.StartsWith("--utility-sub-type"));
        return string.Join(' ', keep);
    }

    public static void Shutdown()
    {
        long t = Clock.ElapsedMilliseconds;
        Cef.cef_shutdown();
        Log($"cef_shutdown returned after {Clock.ElapsedMilliseconds - t} ms; our refs: {RefReport()}");
        System.Threading.Thread.Sleep(500);
        var left = Procs.Children();
        Log(left.Count == 0 ? "no children left" : "children left: " + string.Join("; ", left.Select(c => $"{c.Pid} {c.Type}")));
    }
}
