using HultUI.WebView.Cef;

// Starts CEF and returns from Main, so the test can watch the process exit. Prints what it
// sees for the test to check; exit code 3 means there is no CEF here.
// Without the sandbox, the engine is made with options of its own, and a default engine may
// well be unavailable (Ubuntu without a profile): before the default's check.
var withoutSandbox = Array.IndexOf(args, "--without-sandbox");
if (withoutSandbox >= 0) return WithoutSandbox.Run(args[withoutSandbox + 1]);
var contentFolder = Array.IndexOf(args, "--content-folder");
if (contentFolder >= 0) return ContentFolderProbe.Run(args[contentFolder + 1]);

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
    // A page, drawn: its browser, renderer and GPU process are all up, and the engine's restore
    // after the first frame has run (review of 5.5, item 1).
    using var restored = new ManualResetEventSlim();
    CefRuntime.FirstPageRestored += put => { Console.WriteLine("put back after the first page: " + (put.Count == 0 ? "nothing" : string.Join(" ", put.Select(Name)))); restored.Set(); };
    var host = new Host();
    var view = engine.CreateView(new HultUI.WebView.WebViewOptions());
    view.Attach(host);
    view.SetPlacement(new HultUI.WebView.WebViewPlacement(0, new HultUI.WebView.PixelRect(0, 0, 200, 100), 1f));
    for (int i = 0; i < 1000 && !host.Pixels.IsLoaded; i++) Thread.Sleep(20);
    Console.WriteLine("a page drew: " + host.Pixels.IsLoaded + ", the restore after it ran: " + restored.Wait(5000));
    Thread.Sleep(1000);   // and whatever the page's processes do after their first frame
    Compare("by a page");
    using (var p = System.Diagnostics.Process.Start("true")!)
        Console.WriteLine("a process started after CEF is seen to exit: " + p.WaitForExit(5000));
    try { object? o = null; _ = o!.ToString(); }
    catch (NullReferenceException) { Console.WriteLine("a null dereference is a NullReferenceException"); }
    Console.Out.Flush();
    return 0;
}

if (args.Contains("--first-child-while-cef-starts"))
{
    // .NET installs its SIGCHLD handler at the first Process.Start: here that comes after the
    // engine saved the signals and before it put Chromium's changes back.
    var starting = CefRuntime.Start(windowSystem: null);
    Thread.Sleep(20);
    using (var first = System.Diagnostics.Process.Start("true")!) first.WaitForExit(5000);
    Console.WriteLine("CEF started: " + (starting.Wait(TimeSpan.FromSeconds(20)) && CefRuntime.Error is null));
    using (var later = System.Diagnostics.Process.Start("true")!)
        Console.WriteLine("a process started after CEF is seen to exit: " + later.WaitForExit(5000));
    return 0;
}

var quick = Array.IndexOf(args, "--quick-dispose");
if (quick >= 0)
{
    // Pages placed and disposed at once, while CEF creates their browsers, then the process ends.
    CefRuntime.Start(windowSystem: null).Wait(TimeSpan.FromSeconds(20));
    int rounds = args.Length > quick + 2 ? int.Parse(args[quick + 2]) : 1;
    for (int i = 0; i < 2 * rounds; i++)
    {
        using var v = engine.CreateView(new HultUI.WebView.WebViewOptions());
        v.Attach(new Host());
        v.SetPlacement(new HultUI.WebView.WebViewPlacement(0, new HultUI.WebView.PixelRect(0, 0, 300, 200), 1f) { WindowSystem = args[quick + 1] });
        Thread.Sleep(Random.Shared.Next(0, 30));
    }
    Thread.Sleep(3000);
    Console.WriteLine("disposed");
    return 0;
}

var devToolsWindow = Array.IndexOf(args, "--devtools-window");
if (devToolsWindow >= 0)
{
    // The developer tools in a native window of their own, on the platform given ("wayland",
    // "x11"), opened <how>: "call" (OpenDevTools), "f12", "ctrl-shift-i", "f12-off" (F12 with the
    // option off: nothing should open), or "inspect" (the context menu's entry). Then held for
    // a look at the compositor's windows, then the page goes, then held again.
    var system = args[devToolsWindow + 1];
    var how = args[devToolsWindow + 2];
    int held = args.Length > devToolsWindow + 3 ? int.Parse(args[devToolsWindow + 3]) : 4000;
    // A profile of its own: the lifetime tests, run beside these, need the default one free.
    var profile = Path.Combine(Path.GetTempPath(), $"hultui-devtools-{Environment.ProcessId}");
    // Removed once CEF has shut down and let go of it, as the engine's own temporary profile is.
    CefRuntime.ShutDown += _ => { try { Directory.Delete(profile, recursive: true); } catch (IOException) { } };
    var started = CefRuntime.Start(windowSystem: system, profile).Wait(TimeSpan.FromSeconds(20)) ? CefRuntime.Error : "timed out";
    if (started is not null) { Console.WriteLine("did not start: " + started); return 1; }
    var page = (CefView)engine.CreateView(new HultUI.WebView.WebViewOptions { DevTools = how != "f12-off", UserDataFolder = profile });
    var ready = new ManualResetEventSlim();
    var menu = new ManualResetEventSlim();
    page.Event += e =>
    {
        if (e is HultUI.WebView.WebEngineEvent.Ready) ready.Set();
        if (e is HultUI.WebView.WebEngineEvent.ContextMenuRequested m)
        {
            Console.WriteLine("menu: " + string.Join(", ", m.Request.Items.Select(i => i.Name)));
            if (m.Request.Items.FirstOrDefault(i => i.Name == "inspectElement") is { } inspect) m.Request.Select(inspect);
            else m.Request.Dismiss();
            menu.Set();
        }
    };
    page.Attach(new Host());
    page.SetPlacement(new HultUI.WebView.WebViewPlacement(0, new HultUI.WebView.PixelRect(0, 0, 400, 300), 1f) { WindowSystem = system });
    Console.WriteLine("ready: " + ready.Wait(20_000));
    page.Focus(HultUI.Core.Events.FocusReason.Pointer);
    Thread.Sleep(300);
    void Press(HultUI.Core.Events.KeyCode key, HultUI.Core.Events.KeyModifiers modifiers)
    {
        page.SendKey(new HultUI.WebView.WebKeyInput(true, key, modifiers));
        page.SendKey(new HultUI.WebView.WebKeyInput(false, key, modifiers));
    }
    switch (how)
    {
        case "call": page.OpenDevTools(); break;
        case "f12" or "f12-off": Press(HultUI.Core.Events.KeyCode.F12, HultUI.Core.Events.KeyModifiers.None); break;
        case "ctrl-shift-i": Press(HultUI.Core.Events.KeyCode.I, HultUI.Core.Events.KeyModifiers.Ctrl | HultUI.Core.Events.KeyModifiers.Shift); break;
        case "inspect":
            page.SendPointer(new HultUI.WebView.WebPointerInput(HultUI.WebView.WebPointerAction.Move, 50, 50));
            page.SendPointer(new HultUI.WebView.WebPointerInput(HultUI.WebView.WebPointerAction.Down, 50, 50,
                HultUI.WebView.WebPointerButtons.Right, HultUI.WebView.WebPointerButtons.Right, 1));
            page.SendPointer(new HultUI.WebView.WebPointerInput(HultUI.WebView.WebPointerAction.Up, 50, 50,
                HultUI.WebView.WebPointerButtons.Right, HultUI.WebView.WebPointerButtons.None, 1));
            Console.WriteLine("menu asked: " + menu.Wait(10_000));
            break;
    }
    Thread.Sleep(held / 2);
    var open = new TaskCompletionSource<bool>();
    CefRuntime.Post(() => open.TrySetResult(page.HasDevToolsWindow));
    Console.WriteLine("devtools open: " + (open.Task.Wait(5000) && open.Task.Result));
    Console.WriteLine("holding");
    Console.Out.Flush();
    Thread.Sleep(held / 2);
    page.Dispose();
    Thread.Sleep(1500);
    Console.WriteLine("disposed");
    Console.Out.Flush();
    Thread.Sleep(held / 2);
    return 0;
}

var zeroCopy = Array.IndexOf(args, "--zero-copy");
if (zeroCopy >= 0)
{
    var error0 = CefRuntime.Start(windowSystem: "wayland").Wait(TimeSpan.FromSeconds(20)) ? CefRuntime.Error : "timed out";
    if (error0 is not null) { Console.WriteLine("did not start: " + error0); return 1; }
    return ZeroCopy.Run(engine, int.Parse(args[zeroCopy + 1]), int.Parse(args[zeroCopy + 2]), int.Parse(args[zeroCopy + 3]));
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
