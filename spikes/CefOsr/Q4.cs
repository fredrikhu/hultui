using System.Collections.Concurrent;
using System.Diagnostics;
using CefOsr.Capi;
using static CefOsr.Native;

namespace CefOsr;

/// <summary>
/// Question 4: the message loop. Run with --loop=external (cef_do_message_loop_work when
/// on_schedule_message_pump_work asks), --loop=mt (CEF's own UI thread) or --loop=work (polled,
/// the reference). Which thread each callback arrives on, how often the pump is asked for and
/// run, what the process costs idle and animating, and whether everything exits.
/// </summary>
internal static unsafe class Q4
{
    static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> Seen = new();

    static void Saw(string what)
        => Seen.GetOrAdd(what, _ => new()).AddOrUpdate(Native.Thread(), 1, (_, v) => v + 1);

    public static int Run(Options o)
    {
        if (Host.Initialize([], o) != 1) return 1;
        Host.Pump(() => Host.ContextReady, 15000);

        // Pump requests: how many, and how far ahead they ask.
        var delays = new ConcurrentBag<long>();
        Host.PumpRequested += d => { delays.Add(d); Saw("on_schedule_message_pump_work"); };

        nint browser = 0;
        int paints = 0;
        string title = "";
        bool loaded = false, closed = false;
        var browses = new ConcurrentQueue<string>();
        Host.AfterCreated += b => { AddRef((void*)b); browser = b; Saw("on_after_created"); };
        Host.Paint += (b, t, w, h, buf, n, d) => { paints++; Saw("on_paint"); };
        Host.Title += (b, t) => { title = t; Saw("on_title_change"); };
        Host.LoadEnd += (b, f, s) => { loaded = true; Saw("on_load_end"); };
        Host.BeforeBrowse += (b, f, url, gesture) => { browses.Enqueue(url[..Math.Min(40, url.Length)]); Saw("on_before_browse"); return false; };
        Host.BeforeClose += b => { closed = true; Saw("on_before_close"); };

        Host.ViewWidth = 800; Host.ViewHeight = 600; Host.Scale = 1.5f;
        // Asynchronous creation: create_browser_sync may only be called on CEF's UI thread,
        // which with multi_threaded_message_loop is not ours.
        Host.CreateBrowser(Host.DataUrl("<title>first</title><body>first</body>"), sync: false);
        Host.Pump(() => browser != 0 && loaded, 15000);
        var b0 = (cef_browser_t*)browser;

        loaded = false;
        var main = b0->get_main_frame(b0);
        var second = Make(Host.DataUrl("<title>second</title><body>second</body>"));
        main->load_url(main, &second);
        Release(main);
        Host.Pump(() => loaded && title == "second", 10000);
        Log($"navigated: title '{title}', on_before_browse for {browses.Count} url(s)");

        // Idle: a still page. Then animating at 60.
        Host.Wait(3000);   // past start-up and the JIT's first compilations
        var (idleOurs, idleTheirs, idleRequests, idleCalls) = Measure(5000);
        loaded = false;
        main = b0->get_main_frame(b0);
        var anim = Make(Host.DataUrl(Q3.AnimatedPage));
        main->load_url(main, &anim);
        Release(main);
        Host.Pump(() => loaded, 10000);
        Host.Wait(500);
        paints = 0;
        var (animOurs, animTheirs, animRequests, animCalls) = Measure(3000);
        Log($"idle 5 s: our process {idleOurs:F0} ms/s CPU, CEF's children {idleTheirs:F0} ms/s; " +
            $"pump asked {idleRequests:F0}/s, run {idleCalls:F0}/s");
        Log($"animating 3 s at 60: {paints / 3.0:F1} paints/s; our process {animOurs:F0} ms/s CPU, CEF's children {animTheirs:F0} ms/s; " +
            $"pump asked {animRequests:F0}/s, run {animCalls:F0}/s");
        if (!delays.IsEmpty)
        {
            var ds = delays.ToArray();
            Log($"pump delays asked for: {ds.Count(d => d <= 0)} immediate, {ds.Count(d => d > 0 && d <= 16)} ≤16 ms, " +
                $"{ds.Count(d => d > 16 && d < 1000)} longer, {ds.Count(d => d >= 1000)} ≥1 s; max {ds.Max()} ms");
        }

        // Shut down: close, wait for on_before_close, release, cef_shutdown.
        var h = b0->get_host(b0);
        long tc = Clock.ElapsedMilliseconds;
        h->close_browser(h, 1);
        Release(h);
        Host.Pump(() => closed, 10000);
        Log($"on_before_close {Clock.ElapsedMilliseconds - tc} ms after close_browser");
        Release(b0);

        Log("callbacks by thread:");
        foreach (var (what, threads) in Seen.OrderBy(kv => kv.Key))
            Console.WriteLine($"    {what,-32} {string.Join(", ", threads.Select(t => $"{t.Key} ×{t.Value}"))}");

        long ts = Clock.ElapsedMilliseconds;
        Cef.cef_shutdown();
        Log($"cef_shutdown returned after {Clock.ElapsedMilliseconds - ts} ms on {Native.Thread()}; refs: {RefReport()}");
        for (int i = 0; i < 30; i++)
        {
            var left = Procs.Children();
            if (left.Count == 0) { Log($"all children gone {Clock.ElapsedMilliseconds - ts} ms after cef_shutdown"); break; }
            if (i == 29)
                Log("after 3 s, children left: " + string.Join("; ", left.Select(c => $"{c.Pid} {c.Type} {State(c.Pid)}")));
            System.Threading.Thread.Sleep(100);
        }
        Log($"threads in our process now: {Directory.GetDirectories("/proc/self/task").Length}");
        return 0;
    }

    static string State(int pid)
    {
        try { return File.ReadAllText($"/proc/{pid}/stat").Split(") ")[1].Split(' ')[0]; }
        catch (Exception) { return "?"; }
    }

    static (double ours, double theirs, double requests, double calls) Measure(int ms)
    {
        var me = Process.GetCurrentProcess();
        double Theirs() => Procs.Children().Sum(c => CpuMs(c.Pid));
        var c0 = me.TotalProcessorTime; var t0 = Theirs();
        int r0 = Host.PumpRequests, p0 = Host.PumpCalls;
        var clock = Stopwatch.StartNew();
        Host.Wait(ms);
        me.Refresh();
        double s = clock.Elapsed.TotalSeconds;
        return ((me.TotalProcessorTime - c0).TotalMilliseconds / s, (Theirs() - t0) / s,
                (Host.PumpRequests - r0) / s, (Host.PumpCalls - p0) / s);
    }
}
