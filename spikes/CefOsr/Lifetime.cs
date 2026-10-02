using CefOsr.Capi;
using static CefOsr.Native;

namespace CefOsr;

/// <summary>
/// For step 5.1: cef_initialize on a thread of its own, browsers driven from the main thread,
/// and cef_shutdown on that same thread when the process exits (AppDomain.ProcessExit), so
/// CEF's life is the process's whatever the application does with its own objects.
/// </summary>
internal static unsafe class Lifetime
{
    public static int Run(Options o)
    {
        int ok = -1;
        var shutdown = new ManualResetEventSlim();
        var done = new ManualResetEventSlim();
        var cefThread = new System.Threading.Thread(() =>
        {
            ok = Host.Initialize([], o);
            Log($"initialised on this thread: {ok}");
            shutdown.Wait();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Cef.cef_shutdown();
            Log($"cef_shutdown on the initialising thread: {sw.ElapsedMilliseconds} ms; refs {RefReport()}");
            done.Set();
        }) { Name = "CEF", IsBackground = true };
        cefThread.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Log("ProcessExit: asking the CEF thread to shut down");
            shutdown.Set();
            Log($"ProcessExit: shutdown {(done.Wait(5000) ? "done" : "TIMED OUT")}");
        };
        while (ok == -1) System.Threading.Thread.Sleep(5);
        if (ok != 1) return 1;
        Host.Pump(() => Host.ContextReady, 10000);

        nint browser = 0; int paints = 0;
        Host.AfterCreated += b => { AddRef((void*)b); browser = b; };
        Host.Paint += (b, t, w, h, buf, n, d) => paints++;
        Host.CreateBrowser(Host.DataUrl(Q3.AnimatedPage), sync: false);
        Host.Pump(() => browser != 0, 10000);
        Host.Wait(1000);
        Log($"main thread created a browser: {paints} paints in ~1 s");
        if (!o.Rest.Contains("--leave-open")) Host.Close((cef_browser_t*)browser);
        Log("returning from Main");
        return 0;
    }
}
