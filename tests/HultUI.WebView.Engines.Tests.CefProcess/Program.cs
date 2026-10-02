using HultUI.WebView.Cef;

// Starts CEF and returns from Main, so the test can watch the process exit. Prints what it
// sees for the test to check; exit code 3 means there is no CEF here.
var engine = new CefEngine();
if (!engine.Availability.IsAvailable)
{
    Console.WriteLine("unavailable: " + engine.Availability.Reason);
    return 3;
}

CefRuntime.ShuttingDown += () => Console.WriteLine("process exit: shutting down");
CefRuntime.ShutDown += took => Console.WriteLine($"shut down in {took.TotalMilliseconds:F0} ms on '{Thread.CurrentThread.Name}'");

// An application's own SIGTERM handler, made before CEF starts as an application's would be,
// which Chromium's had replaced: it runs, and exiting from it stops CEF cleanly.
using var onTerm = args.Contains("--on-term") ? System.Runtime.InteropServices.PosixSignalRegistration.Create(
    System.Runtime.InteropServices.PosixSignal.SIGTERM, context =>
    {
        Console.WriteLine("the application's SIGTERM handler ran");
        context.Cancel = true;
        Environment.Exit(0);
    }) : null;
if (args.Contains("--signals"))
{
    static string Caught() => File.ReadLines("/proc/self/status").First(l => l.StartsWith("SigCgt:"))[7..].Trim();
    static string Name(int n) => n switch
    {
        1 => "HUP", 2 => "INT", 3 => "QUIT", 4 => "ILL", 6 => "ABRT", 7 => "BUS", 8 => "FPE", 11 => "SEGV", 13 => "PIPE",
        15 => "TERM", 17 => "CHLD", 18 => "CONT", 28 => "WINCH", _ => n.ToString(),
    };
    static string Names(string mask)
    {
        var bits = Convert.ToUInt64(mask, 16);
        return string.Join(" ", Enumerable.Range(1, 64).Where(n => (bits & (1UL << (n - 1))) != 0).Select(Name));
    }
    // .NET's SIGCHLD handler arrives with the first process it starts.
    using (var early = System.Diagnostics.Process.Start("true")) early!.WaitForExit();
    var caught = Convert.ToUInt64(Caught(), 16);
    // Each caught signal's handler: the first field of its sigaction, sa_handler/sa_sigaction.
    // Caught by someone else is still caught, so whose handler it is must be compared.
    var handlers = Enumerable.Range(1, 64).Where(n => (caught & (1UL << (n - 1))) != 0)
        .ToDictionary(n => n, n => BitConverter.ToUInt64(CefRuntime.SaveSignal(n), 0));
    void Compare(string when)
    {
        var changed = handlers.Where(h => BitConverter.ToUInt64(CefRuntime.SaveSignal(h.Key), 0) != h.Value)
            .Select(h => Name(h.Key)).ToList();
        Console.WriteLine($"handlers changed {when}: " + (changed.Count == 0 ? "none" : string.Join(" ", changed))
            + $" (of {handlers.Count}: {string.Join(" ", handlers.Keys.Select(Name))})");
    }
    Console.WriteLine("before CEF: " + Names(Caught()));
    var started = CefRuntime.Start(windowSystem: null).Wait(TimeSpan.FromSeconds(20));
    Console.WriteLine("after CEF:  " + Names(Caught()) + (started ? "" : " (did not start)"));
    Compare("by CEF's start");
    var view = engine.CreateView(new HultUI.WebView.WebViewOptions());   // a renderer starts too
    view.Attach(new Host());
    view.SetPlacement(new HultUI.WebView.WebViewPlacement(0, new HultUI.WebView.PixelRect(0, 0, 200, 100), 1f));
    Thread.Sleep(1500);
    Compare("by a page");
    using (var p = System.Diagnostics.Process.Start("true")!)
        Console.WriteLine("a process started after CEF is seen to exit: " + p.WaitForExit(5000));
    try { object? o = null; _ = o!.ToString(); }
    catch (NullReferenceException) { Console.WriteLine("a null dereference is a NullReferenceException"); }
    Console.Out.Flush();
    return 0;
}

var folders = Array.IndexOf(args, "--folders");
if (folders >= 0) return await Folders(engine, args[folders + 1]);

var error = CefRuntime.Start(windowSystem: null).Wait(TimeSpan.FromSeconds(20)) ? CefRuntime.Error : "timed out";
if (error is not null)
{
    Console.WriteLine("did not start: " + error);
    return 1;
}
Console.WriteLine("started: " + CefRuntime.Description);
Console.WriteLine("profile: " + CefRuntime.Profile);

if (args.Contains("--second-engine"))
{
    engine.Dispose();
    using var second = new CefEngine();
    Console.WriteLine($"a second engine after disposing the first: {second.Availability.IsAvailable}");
}
var hold = Array.IndexOf(args, "--hold");
if (hold >= 0) Thread.Sleep(int.Parse(args[hold + 1]));
Console.Out.Flush();
return 0;

// The owner's UserDataFolder rule, in a process of its own (the profile is decided once per
// process): the first page's folder is the profile, one inside it is a request context of its
// own, any other is refused.
static async Task<int> Folders(CefEngine engine, string root)
{
    async Task<string> Page(string? folder, string html)
    {
        var view = engine.CreateView(new HultUI.WebView.WebViewOptions { UserDataFolder = folder });
        var events = new System.Collections.Concurrent.ConcurrentQueue<HultUI.WebView.WebEngineEvent>();
        view.Event += events.Enqueue;
        view.Event += e => { if (e is HultUI.WebView.WebEngineEvent.NavigationStarting s) s.Request.Complete(); };   // as the controller does
        view.Attach(new Host());
        view.SetPlacement(new HultUI.WebView.WebViewPlacement(0, new HultUI.WebView.PixelRect(0, 0, 200, 100), 1f));
        for (int i = 0; i < 1500 && !events.Any(e => e is HultUI.WebView.WebEngineEvent.Ready or HultUI.WebView.WebEngineEvent.Failed); i++)
            await Task.Delay(20);
        if (events.OfType<HultUI.WebView.WebEngineEvent.Failed>().FirstOrDefault() is { } failed) return "failed: " + failed.Message;
        if (!events.Any(e => e is HultUI.WebView.WebEngineEvent.Ready)) return "timed out";
        view.NavigateToString(html);
        for (int i = 0; i < 500 && !events.OfType<HultUI.WebView.WebEngineEvent.TitleChanged>().Any(t => t.Title == "loaded"); i++)
            await Task.Delay(20);
        return events.OfType<HultUI.WebView.WebEngineEvent.TitleChanged>().Any(t => t.Title == "loaded") ? "loaded" : "ready, but the page did not load";
    }

    const string html = "<title>loaded</title><script>localStorage.setItem('k', 'v')</script>";
    Console.WriteLine("root: " + await Page(root, html));
    Console.WriteLine("profile: " + CefRuntime.Profile);
    var inside = Path.Combine(root, "inner");
    Console.WriteLine("inside: " + await Page(inside, html));
    Console.WriteLine("none: " + await Page(null, html));
    var outside = Path.Combine(Path.GetTempPath(), "hultui-elsewhere-" + Guid.NewGuid().ToString("N"));
    Console.WriteLine("outside: " + await Page(outside, html));
    await Task.Delay(1500);   // let the inner context write its files
    Console.WriteLine("inner folder has files: " + (Directory.Exists(inside) && Directory.EnumerateFileSystemEntries(inside).Any()));
    Console.WriteLine("outside folder made: " + Directory.Exists(outside));
    Console.Out.Flush();
    return 0;
}

sealed class Host : HultUI.WebView.IWebViewHost
{
    public HultUI.Core.Rendering.PixelFrameSink Pixels { get; } = new();
    public void UseGpuRenderer(HultUI.Core.Rendering.IGpuFrameRenderer renderer) { }
    public void RequestRepaint() { }
    public void Post(Action action) => ThreadPool.QueueUserWorkItem(_ => action());
}
