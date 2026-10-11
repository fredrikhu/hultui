using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Media;
using HultUI.Media.Mpv;
using HultUI.WebView;
using HultUI.WebView.Cef;
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
// should give back. The pages are WebView2's on Windows and CEF's on Linux, where they are GPU
// sources only on Wayland (step 5.5 of the Linux engine).
//
// --page-and-video shows a page (green, left) and a video playing through MpvBackend
// (magenta, right): the web engine's interop and mpv's hidden GL context sharing one Vulkan
// window. The clip is av://lavfi:color=c=magenta:s=320x240 unless --clip=<path or URL> says
// otherwise (any file must be one the libmpv here decodes). Exits 4 when there is no libmpv.
//
// --resizes=N resizes the window N times between two sizes, without activating it, and prints
// the median and longest time a resize took to be drawn (Windows). With --new-sizes every
// resize is to a size the window has not had yet, as in a drag.
//
// --minimize minimizes the window without activating it, forces frames while it is minimized,
// then runs the real loop: minimized with one change waiting, restored, and resized, counting
// loop passes and the frames drawn in each part (Windows).
//
// --minimized-video --clip=<file> plays the clip through MpvBackend, minimizes the window without
// activating it, and runs the real loop: 1.5 s minimized, then a second player opened as Reel
// opens the next file in a folder, 1.5 s more, then restored. It prints the frames mpv dropped
// while minimized, how far the second player got, and the frames drawn in the second after the
// restore (Windows). Exits 4 when there is no libmpv.
//
// No window made here may become the foreground, whatever it does (Windows): see Foreground.

internal static class Program
{
    // WebView2 wants the UI thread to be a single-threaded apartment.
    [STAThread]
    private static int Main(string[] args)
    {
        Foreground.Keep();
        var renderer = args.Contains("--renderer=default") ? RendererKind.Default : RendererKind.Vulkan;
        if (args.Contains("--minimize")) return MinimizeAndRestore(renderer);
        if (args.Contains("--minimized-video"))
            return MinimizedVideo(renderer, args.First(a => a.StartsWith("--clip="))["--clip=".Length..]);
        if (args.Contains("--webviews")) return TwoPages(renderer);
        if (args.FirstOrDefault(a => a.StartsWith("--resizes=")) is { } resizes)
            return Resizes(renderer, int.Parse(resizes["--resizes=".Length..]), args.Contains("--new-sizes"));
        if (args.Contains("--page-and-video"))
            return PageAndVideo(renderer, args.FirstOrDefault(a => a.StartsWith("--clip="))?["--clip=".Length..]
                ?? "av://lavfi:color=c=magenta:s=320x240");

        int frames = args.FirstOrDefault(a => a.StartsWith("--frames=")) is { } f ? int.Parse(f["--frames=".Length..]) : 12;

        using var app = Foreground.NoActivate(new Application("vulkan-probe", 320, 200, renderer: renderer));
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
        using var app = Foreground.NoActivate(new Application("vulkan-probe", 480, 240, renderer: renderer));
        using IWebEngine engine = OperatingSystem.IsWindows() ? new WebView2Engine() : new CefEngine();
        if (!engine.Availability.IsAvailable) { Console.WriteLine("probe: no engine: " + engine.Availability.Reason); return 3; }
        var options = OperatingSystem.IsWindows()
            ? new WebViewOptions { UserDataFolder = Path.Combine(Path.GetTempPath(), "hultui-webview2-tests") }
            : new WebViewOptions();
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
        // A page's first frame can be the blank one before its document (CEF's is): a second of
        // frames as the pages ask for them lets their own arrive before the capture is forced.
        clock.Restart();
        while (clock.Elapsed.TotalSeconds < 1) app.RunFrame(TimeSpan.FromMilliseconds(16));

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

    private static int PageAndVideo(RendererKind renderer, string clip)
    {
        if (!MpvBackend.IsAvailable) { Console.WriteLine("probe: no libmpv"); return 4; }
        using var app = Foreground.NoActivate(new Application("vulkan-probe", 480, 240, renderer: renderer));
        using IWebEngine engine = OperatingSystem.IsWindows() ? new WebView2Engine() : new CefEngine();
        if (!engine.Availability.IsAvailable) { Console.WriteLine("probe: no engine: " + engine.Availability.Reason); return 3; }
        var options = OperatingSystem.IsWindows()
            ? new WebViewOptions { UserDataFolder = Path.Combine(Path.GetTempPath(), "hultui-webview2-tests") }
            : new WebViewOptions();
        using var page = new WebViewController(engine, options);
        using var player = new MediaPlayer(new MpvBackend());
        bool pageReady = false;
        page.MessageReceived += _ => pageReady = true;
        page.NavigateToString(Page("#00FF00"));
        player.Open(MediaSource.FromUri(clip), autoPlay: true);
        app.SetRoot(Flex.Row().Width(480f).Height(240f).Children(
            WebViewElement.New(page).FlexGrow(1).FlexBasis(0),
            VideoElement.New(player).FlexGrow(1).FlexBasis(0)));

        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!(pageReady && page.Frames.IsLoaded && player.HasVideo && player.Frames.IsLoaded))
        {
            if (player.Error is { } error) { Console.WriteLine("probe: the video failed: " + error); return 2; }
            if (clock.Elapsed.TotalSeconds > 20)
            {
                Console.WriteLine($"probe: never both drew (page ready {pageReady}, loaded {page.Frames.IsLoaded}; " +
                    $"video {player.State}, {player.VideoSize}, loaded {player.Frames.IsLoaded})");
                return 2;
            }
            app.RunFrame(TimeSpan.FromMilliseconds(16));
        }
        Console.WriteLine($"probe: the page and the video drew after {clock.ElapsedMilliseconds} ms " +
            $"(video {player.VideoSize}, {player.SinkMode} mode)");
        // As in the two-page mode: the page's own frame may come after its first.
        clock.Restart();
        while (clock.Elapsed.TotalSeconds < 1) app.RunFrame(TimeSpan.FromMilliseconds(16));

        string? capture = Environment.GetEnvironmentVariable("HULTUI_VULKAN_CAPTURE");
        clock.Restart();
        while (capture is not null && !File.Exists(capture) && clock.Elapsed.TotalSeconds < 20)
            app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        Console.WriteLine("probe: done");
        return 0;
    }

    /// <summary>
    /// Every new size rebuilds the swapchain, so this is <paramref name="count"/> rebuilds. The
    /// resize is SetWindowPos on this thread, never activating the window: as in a drag, the
    /// window procedure draws the new size before SetWindowPos returns, so its time is a
    /// rebuild plus its frames — and whatever the first present after it waits for.
    /// </summary>
    private static int Resizes(RendererKind renderer, int count, bool newSizes)
    {
        using var app = Foreground.NoActivate(new Application("vulkan-probe", 640, 400, renderer: renderer));
        int frames = 0;
        app.SetRoot(Flex.Row().FlexGrow(1).Children(
            Box.New().FlexGrow(1).Background(Color.FromHex("#FF0000")),
            Box.New().FlexGrow(1).Background(Color.FromHex("#0000FF")),
            Canvas.New(_ => frames++).Width(1f).Height(1f)));
        for (int i = 0; i < 12; i++)
            app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);

        nint hwnd = app.PrimaryWindow.NativeHandle;
        var times = new double[count];
        int inside = 0, without = 0;
        for (int i = 0; i < count; i++)
        {
            int width = newSizes ? 1000 + 24 * (i + 1) : i % 2 == 0 ? 1100 : 1000;
            int height = newSizes ? 700 + 16 * (i + 1) : i % 2 == 0 ? 760 : 700;
            int before = frames;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            SetWindowPos(hwnd, 0, 0, 0, width, height, NoMove | NoZOrder | NoActivate);
            times[i] = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            inside += frames - before;
            if (frames == before) without++;
            for (int f = 0; f < 2; f++)
                app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        }

        Array.Sort(times);
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"probe: {count} resizes, median {times[count / 2]:0.0} ms, max {times[^1]:0.0} ms"));
        // Drawn from the event watch, before SetWindowPos returned.
        Console.WriteLine($"probe: {inside} frames inside {count} resizes, {without} without one");
        Console.WriteLine("probe: done");
        return 0;
    }

    /// <summary>
    /// A window that draws while minimized, in the real loop. On Windows a minimized Vulkan
    /// surface has no area: a frame drawn then found its swapchain out of date, the rebuild could
    /// make none, and the old one was dropped without being destroyed. The window never drew
    /// again, a resize failed with ErrorNativeWindowInUseKHR, and the loop retried a frame every
    /// display interval until the restore. The forced frames below take that path; then the loop
    /// runs 1.5 s minimized with one change waiting, 1 s restored and 0.5 s resized, the last two
    /// with a change every 33 ms, as a video would make.
    /// </summary>
    private static int MinimizeAndRestore(RendererKind renderer)
    {
        var platform = new CountingPlatform(new HultUI.Platform.SDL3.SdlWindowingPlatform(renderer));
        using var app = Foreground.NoActivate(new Application(platform, "vulkan-probe", 640, 400));
        int frames = 0;
        var changing = Box.New().FlexGrow(1).Background(Color.FromHex("#0000FF"));
        app.SetRoot(Flex.Row().FlexGrow(1).Children(
            Box.New().FlexGrow(1).Background(Color.FromHex("#FF0000")),
            changing,
            Canvas.New(_ => frames++).Width(1f).Height(1f)));
        for (int i = 0; i < 12; i++)
            app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);

        nint hwnd = app.PrimaryWindow.NativeHandle;
        ShowWindow(hwnd, ShowMinNoActive);
        if (!IsIconic(hwnd)) { Console.WriteLine("probe: the window did not minimize"); return 2; }
        for (int i = 0; i < 3; i++)
            app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        changing.MarkPaintDirty();

        bool ticking = false;
        new Thread(() =>
        {
            while (true)
            {
                Thread.Sleep(33);
                if (Volatile.Read(ref ticking)) Application.InvokeOnMainThread(changing.MarkPaintDirty);
            }
        }) { IsBackground = true }.Start();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        int stage = 0, stageFrames = frames;
        long stageStart = 0;
        platform.Passes = 0;
        platform.OnPass = () =>
        {
            long ms = clock.ElapsedMilliseconds;
            int[] ends = [1500, 2500, 3000];
            if (stage >= ends.Length || ms < ends[stage]) return;
            double seconds = (ms - stageStart) / 1000.0;
            string what = stage switch { 0 => "minimized", 1 => "restored", _ => "resized" };
            Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"probe: {what} {platform.Passes / seconds:0} passes/s, {frames - stageFrames} frames in {seconds:0.0} s " +
                $"on a {app.PrimaryWindow.RefreshRate:0} Hz display"));
            if (stage == 0)
            {
                ShowWindow(hwnd, ShowNoActivate);
                Volatile.Write(ref ticking, true);
            }
            else if (stage == 1)
            {
                SetWindowPos(hwnd, 0, 0, 0, 700, 460, NoMove | NoZOrder | NoActivate);
            }
            else
            {
                app.PrimaryWindow.Close();
            }
            stage++;
            stageStart = ms;
            stageFrames = frames;
            platform.Passes = 0;
        };
        app.Run();
        Console.WriteLine("probe: done");
        return 0;
    }

    /// <summary>
    /// A video in a minimized window, in the real loop. Nothing draws a minimized window, and
    /// the UI thread is mpv's render thread, so mpv waited 200 ms for every frame to be taken
    /// and then dropped it; and a player opened meanwhile never loaded its file, because its
    /// render context is made by a draw. In Reel the sound stopped at the end of a file until
    /// the window was restored. The window serves its video without drawing it instead.
    /// </summary>
    private static int MinimizedVideo(RendererKind renderer, string clip)
    {
        if (!MpvBackend.IsAvailable) { Console.WriteLine("probe: no libmpv"); return 4; }
        using var app = Foreground.NoActivate(new Application("vulkan-probe", 480, 270, renderer: renderer));
        int frames = 0;
        HultUI.Core.Elements.Element Show(MediaPlayer player) => Flex.Row().FlexGrow(1).Children(
            VideoElement.New(player).FlexGrow(1),
            Canvas.New(_ => Interlocked.Increment(ref frames)).Width(1f).Height(1f));

        var firstBackend = new MpvBackend();
        var first = new MediaPlayer(firstBackend);
        app.SetRoot(Show(first));
        first.Open(MediaSource.FromFile(clip), autoPlay: true);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!(first.IsPlaying && firstBackend.Peek("time-pos") >= 0.3))
        {
            if (clock.Elapsed.TotalSeconds > 20) { Console.WriteLine($"probe: the video never played ({first.State}, {first.Error})"); return 2; }
            app.RunFrame(TimeSpan.FromMilliseconds(16));
        }

        nint hwnd = app.PrimaryWindow.NativeHandle;
        ShowWindow(hwnd, ShowMinNoActive);
        if (!IsIconic(hwnd)) { Console.WriteLine("probe: the window did not minimize"); return 2; }

        MediaPlayer? second = null;
        MpvBackend? secondBackend = null;
        new Thread(() =>
        {
            double drops = firstBackend.Peek("frame-drop-count"), position = firstBackend.Peek("time-pos");
            Thread.Sleep(1500);
            Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"probe: minimized 1.5 s, {firstBackend.Peek("frame-drop-count") - drops:0} frames dropped, " +
                $"played {firstBackend.Peek("time-pos") - position:0.00} s"));

            // As Reel opens the next file in the folder when one ends: a new player and element.
            Application.InvokeOnMainThread(() =>
            {
                first.Dispose();
                secondBackend = new MpvBackend();
                second = new MediaPlayer(secondBackend);
                app.SetRoot(Show(second));
                second.Open(MediaSource.FromFile(clip), autoPlay: true);
            });
            Thread.Sleep(1500);
            double reached = Volatile.Read(ref secondBackend) is { } opened ? opened.Peek("time-pos") : double.NaN;
            Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"probe: opened while minimized, at {(double.IsNaN(reached) ? -1 : reached):0.00} s, still minimized {IsIconic(hwnd)}"));

            int before = Volatile.Read(ref frames);
            Application.InvokeOnMainThread(() => ShowWindow(hwnd, ShowNoActivate));
            Thread.Sleep(1000);
            Console.WriteLine($"probe: restored, {Volatile.Read(ref frames) - before} frames in 1 s");
            Application.InvokeOnMainThread(() => app.PrimaryWindow.Close());
        }) { IsBackground = true }.Start();
        app.Run();
        second?.Dispose();
        Console.WriteLine("probe: done");
        return 0;
    }

    /// <summary>The SDL platform, counting the loop's passes.</summary>
    private sealed class CountingPlatform(IWindowingPlatform inner) : IWindowingPlatform
    {
        public long Passes;
        public Action? OnPass;

        public IPlatformWindow CreateWindow(string title, int width, int height, WindowStyle style)
            => inner.CreateWindow(title, width, height, style);

        public bool PumpEvents(Action<IPlatformWindow>? onWindowClose)
        {
            Passes++;
            OnPass?.Invoke();
            return inner.PumpEvents(onWindowClose);
        }

        public void WaitForEvents(int timeoutMs) => inner.WaitForEvents(timeoutMs);
        public void Wake() => inner.Wake();
        public TimeProvider TimeProvider => inner.TimeProvider;
        public void Dispose() => inner.Dispose();
    }

    private const uint NoMove = 0x0002, NoZOrder = 0x0004, NoActivate = 0x0010;
    private const int ShowMinNoActive = 7, ShowNoActivate = 4;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hwnd, int command);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);

    private static string Page(string colour) =>
        $"<html><body style='margin:0;background:{colour}'><script>chrome.webview.postMessage('ready');</script></body></html>";
}

/// <summary>
/// No window this probe makes may become the foreground: the tests run on a desktop someone is
/// using. <c>SDL_WINDOW_ACTIVATE_WHEN_SHOWN=0</c> in the environment was not enough on its own
/// (2026-10-11, plan 17: a probe window took the foreground four times). So SDL is told in
/// process before any window exists; every window is <c>WS_EX_NOACTIVATE</c>, which neither a
/// click nor Windows' choice of a window to activate when the foreground one closes can
/// override; and a watcher hands the foreground straight back and ends the run with exit code 5
/// if this process ever holds it, which fails the test that started it.
/// </summary>
internal static class Foreground
{
    private static nint _previous;

    public static void Keep()
    {
        SDL.SDL3.SDL_SetHint(SDL.SDL3.SDL_HINT_WINDOW_ACTIVATE_WHEN_SHOWN, "0");
        if (!OperatingSystem.IsWindows()) return;
        _previous = GetForegroundWindow();
        new Thread(() =>
        {
            uint self = (uint)Environment.ProcessId;
            while (true)
            {
                nint foreground = GetForegroundWindow();
                if (foreground != 0)
                {
                    GetWindowThreadProcessId(foreground, out uint owner);
                    if (owner != self) _previous = foreground;
                    else
                    {
                        if (_previous != 0) SetForegroundWindow(_previous);
                        Console.WriteLine("probe: took the foreground; handed it back");
                        Console.Out.Flush();
                        Environment.Exit(5);
                    }
                }
                Thread.Sleep(5);
            }
        }) { IsBackground = true, Name = "foreground watcher" }.Start();
    }

    public static Application NoActivate(Application app)
    {
        if (OperatingSystem.IsWindows())
        {
            nint hwnd = app.PrimaryWindow.NativeHandle;
            SetWindowLongPtrW(hwnd, ExStyle, GetWindowLongPtrW(hwnd, ExStyle) | ExNoActivate);
        }
        return app;
    }

    private const int ExStyle = -20;           // GWL_EXSTYLE
    private const nint ExNoActivate = 0x08000000;   // WS_EX_NOACTIVATE

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetWindowLongPtrW(nint hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
}
