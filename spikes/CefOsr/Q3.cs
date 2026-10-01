using System.Runtime.InteropServices;
using CefOsr.Capi;
using static CefOsr.Native;

namespace CefOsr;

/// <summary>
/// Question 3: pixels. An 800×600 view at scale 1.5: does on_paint give BGRA at 1200×900?
/// A PNG of the first frame, the time to it, whether a still page stops painting, and the
/// paint rate of an animating page at several windowless_frame_rate values.
/// </summary>
internal static unsafe class Q3
{
    // The sample's bench page (samples/HultUI.Samples.WebView/Program.cs, AnimatedPage).
    public const string AnimatedPage = """
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

    // A red 100×100 CSS-pixel box at the top-left, a blue one at the bottom-right, white
    // elsewhere, and text: at scale 1.5 the boxes are 150 device pixels square.
    const string StillPage = """
        <html><head><title>Still</title><style>
        body { margin:0; background:#fff; font: 24px sans-serif; }
        #r { position:absolute; left:0; top:0; width:100px; height:100px; background:#f00; }
        #b { position:absolute; right:0; bottom:0; width:100px; height:100px; background:#00f; }
        p { position:absolute; left:120px; top:20px; margin:0; }
        </style></head><body><div id="r"></div><div id="b"></div><p>HultUI · CEF · 1.5×</p></body></html>
        """;

    public static int Run(Options o)
    {
        var outDir = o.Rest.FirstOrDefault(a => a.StartsWith("--out="))?[6..] ?? "results";
        if (Host.Initialize([], o) != 1) return 1;
        Host.Pump(() => Host.ContextReady, 15000);

        int paints = 0, popupPaints = 0;
        long firstPaintAt = -1;
        int lastW = 0, lastH = 0;
        long dirtyArea = 0;
        byte[]? firstFrame = null;
        var paintThreads = new HashSet<string>();
        Host.Paint += (b, type, w, h, buf, n, dirty) =>
        {
            if (type == cef_paint_element_type_t.PET_POPUP) { popupPaints++; return; }
            paints++;
            paintThreads.Add(Native.Thread());
            lastW = w; lastH = h;
            var d = (cef_rect_t*)dirty;
            for (int i = 0; i < n; i++) dirtyArea += (long)d[i].width * d[i].height;
            if (firstFrame == null && w > 0)
            {
                firstPaintAt = Clock.ElapsedMilliseconds;
                firstFrame = new byte[w * h * 4];
                Marshal.Copy(buf, firstFrame, 0, firstFrame.Length);
                var rects = string.Join(" ", Enumerable.Range(0, n).Select(i => $"({d[i].x},{d[i].y} {d[i].width}x{d[i].height})"));
                Log($"first on_paint: {w}x{h}, {n} dirty rect(s) {rects}");
            }
        };
        bool loaded = false;
        Host.LoadEnd += (b, f, s) => loaded = true;

        Host.ViewWidth = 800; Host.ViewHeight = 600; Host.Scale = 1.5f;
        long t0 = Clock.ElapsedMilliseconds;
        var browser = Host.CreateBrowser(Host.DataUrl(StillPage), frameRate: 60);
        Host.Pump(() => firstFrame != null && loaded, 15000);
        Log($"create_browser → first frame {firstPaintAt - t0} ms (load end seen: {loaded})");
        if (o.Rest.Contains("--notify"))
        {
            // Tell CEF the screen changed, as a host does when the window moves to another
            // monitor, and see whether the scale takes then.
            Host.WithHost(browser, h =>
            {
                var hh = (cef_browser_host_t*)h;
                hh->notify_screen_info_changed(hh);
                hh->was_resized(hh);
            });
            int before = paints;
            Host.Pump(() => paints > before && lastW != 800, 3000);
            Log($"after notify_screen_info_changed + was_resized: {lastW}x{lastH}");
        }
        Host.Pump(() => false, 300);   // let the text finish painting
        // The frame as it stands now: the first frame may precede the text.
        int W = lastW, H = lastH;
        byte[]? settled = null;
        Host.Paint += (b, type, w, h, buf, n, d) =>
        {
            if (type == cef_paint_element_type_t.PET_VIEW && settled == null && w == W)
            {
                settled = new byte[w * h * 4];
                Marshal.Copy(buf, settled, 0, settled.Length);
            }
        };
        Host.WithHost(browser, h => ((cef_browser_host_t*)h)->invalidate((cef_browser_host_t*)h, cef_paint_element_type_t.PET_VIEW));
        Host.Pump(() => settled != null, 5000);
        var frame = settled ?? firstFrame!;
        fixed (byte* p = frame) Png.SaveBgra(Path.Combine(outDir, "q3-still-1200x900.png"), (nint)p, W, H);
        string Px(int x, int y) { int i = (y * W + x) * 4; return $"({x},{y}) BGRA {frame[i]:X2} {frame[i + 1]:X2} {frame[i + 2]:X2} {frame[i + 3]:X2}"; }
        Log($"frame {W}x{H}; {Px(10, 10)}; {Px(149, 149)}; {Px(150, 150)}; {Px(W - 10, H - 10)}; {Px(W - 151, H - 151)}; {Px(W - 150, H - 150)}");

        // A still page: does it stop?
        paints = 0; dirtyArea = 0;
        Host.Pump(() => false, 2000);
        Log($"still page: {paints} paints in 2 s after settling");

        // An animating page at several frame rates.
        loaded = false;
        var main = browser->get_main_frame(browser);
        var u = Make(Host.DataUrl(AnimatedPage));
        main->load_url(main, &u);
        Release(main);
        Host.Pump(() => loaded, 10000);
        Host.Pump(() => false, 500);
        foreach (var rate in new[] { 60, 30, 120, 1 })
        {
            Host.WithHost(browser, h => ((cef_browser_host_t*)h)->set_windowless_frame_rate((cef_browser_host_t*)h, rate));
            Host.Pump(() => false, 500);
            paints = 0; dirtyArea = 0;
            long a = Clock.ElapsedMilliseconds;
            Host.Pump(() => false, 3000);
            double secs = (Clock.ElapsedMilliseconds - a) / 1000.0;
            int got = 0;
            Host.WithHost(browser, h => got = ((cef_browser_host_t*)h)->get_windowless_frame_rate((cef_browser_host_t*)h));
            Log($"animating, windowless_frame_rate {rate} (reads back {got}): {paints / secs:F1} paints/s, " +
                $"{lastW}x{lastH}, dirty {(paints == 0 ? 0 : dirtyArea / paints * 100 / (lastW * lastH))}% of the view per paint");
        }
        Log($"on_paint threads: {string.Join(", ", paintThreads)}; popup paints {popupPaints}");

        Host.Close(browser);
        Q1.Shutdown();
        return 0;
    }
}
