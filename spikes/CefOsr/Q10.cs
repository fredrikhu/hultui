using System.Collections.Concurrent;
using System.Diagnostics;
using CefOsr.Capi;
using static CefOsr.Native;

namespace CefOsr;

/// <summary>
/// Question 10: two pages and the way out. Two browsers on one cef_initialize, close one,
/// close the other, cef_shutdown; what has to be released, in what order, for the process to
/// exit by itself. Variants: --hold-ref (keep a browser reference through cef_shutdown),
/// --no-close (cef_shutdown with a browser open), --kill-self (SIGKILL ourselves with both
/// open: what is left behind), --linger (watch the children for 10 s after cef_shutdown),
/// --no-shutdown (return from Main with the browsers closed but no cef_shutdown).
/// </summary>
internal static unsafe class Q10
{
    public static int Run(Options o)
    {
        bool holdRef = o.Rest.Contains("--hold-ref"), noClose = o.Rest.Contains("--no-close"),
             killSelf = o.Rest.Contains("--kill-self"), linger = o.Rest.Contains("--linger");
        if (Host.Initialize([], o) != 1) return 1;
        Host.Pump(() => Host.ContextReady, 15000);

        var browsers = new ConcurrentDictionary<int, nint>();
        var paints = new ConcurrentDictionary<int, int>();
        var closed = new ConcurrentBag<int>();
        Host.AfterCreated += b => { AddRef((void*)b); browsers[Host.Id((cef_browser_t*)b)] = b; };
        Host.Paint += (b, t, w, h, buf, n, d) => paints.AddOrUpdate(Host.Id((cef_browser_t*)b), 1, (_, v) => v + 1);
        Host.BeforeClose += b => closed.Add(Host.Id((cef_browser_t*)b));
        Host.ViewWidth = 400; Host.ViewHeight = 300; Host.Scale = 1f;

        Host.CreateBrowser(Host.DataUrl(Q3.AnimatedPage), sync: false);
        Host.CreateBrowser(Host.DataUrl(Q3.AnimatedPage.Replace("#e0a020", "#20a0e0")), sync: false);
        Host.Pump(() => browsers.Count == 2, 10000);
        Host.Wait(1500);
        string Census() => string.Join(", ", Procs.Children().GroupBy(c => c.Type).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}"));
        string Paints(int ms) { paints.Clear(); Host.Wait(ms); return string.Join(", ", browsers.Keys.OrderBy(k => k).Select(k => $"browser {k}: {paints.GetValueOrDefault(k) * 1000 / ms}/s")); }
        Log($"two browsers ({string.Join(", ", browsers.Keys)}): {Paints(1000)}; processes: {Census()}");

        if (killSelf)
        {
            Log($"SIGKILL to ourselves (pid {Environment.ProcessId}) with both open");
            Console.Out.Flush();
            Process.GetCurrentProcess().Kill();
        }

        var ids = browsers.Keys.OrderBy(k => k).ToArray();
        // Close the first: on_before_close, then drop our reference.
        var first = (cef_browser_t*)browsers[ids[0]];
        var sw = Stopwatch.StartNew();
        Host.WithHost(first, h => ((cef_browser_host_t*)h)->close_browser((cef_browser_host_t*)h, 0));
        Host.Pump(() => closed.Contains(ids[0]), 5000);
        Log($"close_browser(force 0) on browser {ids[0]}: on_before_close after {sw.ElapsedMilliseconds} ms");
        Release(first);
        browsers.TryRemove(ids[0], out _);
        Host.Wait(1000);
        Log($"one left: {Paints(1000)}; processes: {Census()}");

        var second = (cef_browser_t*)browsers[ids[1]];
        if (!noClose)
        {
            sw.Restart();
            Host.WithHost(second, h => ((cef_browser_host_t*)h)->close_browser((cef_browser_host_t*)h, 0));
            Host.Pump(() => closed.Contains(ids[1]), 5000);
            Log($"close_browser(force 0) on browser {ids[1]}: on_before_close after {sw.ElapsedMilliseconds} ms");
            if (!holdRef) Release(second);
            else Log("keeping our reference to it through cef_shutdown (--hold-ref)");
            Host.Wait(1000);
            Log($"none left: processes: {Census()}");
        }
        else Log("not closing the second browser before cef_shutdown (--no-close)");

        if (o.Rest.Contains("--no-shutdown"))
        {
            Log($"returning from Main without cef_shutdown (--no-shutdown); threads {Directory.GetDirectories("/proc/self/task").Length}");
            return 0;
        }
        sw.Restart();
        Cef.cef_shutdown();
        Log($"cef_shutdown returned after {sw.ElapsedMilliseconds} ms; refs: {RefReport()}");
        Log($"threads in our process: {Directory.GetDirectories("/proc/self/task").Length}; processes: {Census()}");
        if (linger)
            for (int s = 1; s <= 10; s++)
            {
                System.Threading.Thread.Sleep(1000);
                if (s is 1 or 3 or 10) Log($"{s} s after cef_shutdown: processes: {Census()}; " +
                    $"zombies: {Procs.Children().Count(c => File.ReadAllText($"/proc/{c.Pid}/stat").Contains(") Z "))}");
            }
        Log("returning from Main");
        return 0;
    }
}
