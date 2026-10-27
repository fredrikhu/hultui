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
// --drag frames sixteen new sizes, then sixteen new places, in WM_ENTERSIZEMOVE and
// WM_EXITSIZEMOVE, as Win32's modal loop does for an edge drag and a title-bar move, and prints the
// swapchains built (from HULTUI_STARTUP=1's lines on its own stderr) and the frames drawn in each
// part (Windows).
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
// --disposed-images draws four 2048x2048 images (64 MB of textures), takes them off screen and
// disposes them, then draws a few more frames: with HULTUI_STARTUP=1 the purge after them says
// how much Skia still holds.
//
// --frame-counter draws each frame's own number, as sixteen black or white cells, over a red,
// green or blue background by the number mod 3. HULTUI_VULKAN_CAPTURE's file is looked for after
// every frame, decoded and deleted: it must show the frame just drawn. Each swapchain captures
// once, so the window is resized twice and minimized and restored to make new ones, then dragged
// (a swapchain kept across four sizes, and the exact one its end builds) (Windows). Prints one
// "probe: capture" line a capture.
//
// --frame-bytes repaints one box a frame and prints the managed bytes a frame allocates, the
// least of five rounds of 120 frames.
//
// --first-thread (with --renderer=default: GL) draws the first frame and runs the loop for 1 s,
// drawing only what it is asked to, then starts eight thread-pool workers, as an app's first
// request does, and prints the time of the two frames drawn after them, and when the loop drew a
// frame the probe did not ask for. NVIDIA's GL driver walks every thread on the system once a
// process, after the first present that follows a thread starting, and the frame after that
// present waits it out (~55 ms) unless the framework set the walk off before: once the window has
// settled (0.2 s after its first frame) and rests, with a frame of its own
// (SDL3Window.StartDriverThreadWalk). --first-thread-busy keeps the window drawing a new frame
// every pass for 0.8 s first, then lets it rest for 0.4 s, and prints the frames drawn unasked
// while it rested.
//
// --video-frame-bytes --clip=<file> plays the clip through MpvBackend in the real loop and prints
// the managed bytes the UI thread allocates per drawn frame, the least of five rounds of 60 frames,
// then the brightness of the video in two captures of the swapchain (HULTUI_VULKAN_CAPTURE, one
// per swapchain: the window is resized between them), two seconds or more apart (Windows). Exits 4
// when there is no libmpv.
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
        if (args.Contains("--disposed-images")) return DisposedImages(renderer);
        if (args.Contains("--frame-counter")) return FrameCounter(renderer);
        if (args.Contains("--frame-bytes")) return FrameBytes(renderer);
        if (args.Contains("--first-thread-busy")) return FirstThread(renderer, busyMs: 800);
        if (args.Contains("--first-thread")) return FirstThread(renderer, busyMs: 0);
        if (args.Contains("--video-frame-bytes"))
            return VideoFrameBytes(renderer, args.First(a => a.StartsWith("--clip="))["--clip=".Length..]);
        if (args.Contains("--minimize")) return MinimizeAndRestore(renderer);
        if (args.Contains("--minimized-video"))
            return MinimizedVideo(renderer, args.First(a => a.StartsWith("--clip="))["--clip=".Length..]);
        if (args.Contains("--webviews")) return TwoPages(renderer);
        if (args.FirstOrDefault(a => a.StartsWith("--resizes=")) is { } resizes)
            return Resizes(renderer, int.Parse(resizes["--resizes=".Length..]), args.Contains("--new-sizes"));
        if (args.Contains("--drag")) return DragAndMove(renderer);
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
    /// A drag and then a move, each framed as Win32's modal loop frames one: WM_ENTERSIZEMOVE, the
    /// window's new sizes (or places) by SetWindowPos, WM_EXITSIZEMOVE. Sent here, on the window's
    /// own thread, which is where Win32 sends them; the drag cannot be made without the mouse.
    /// Counts the swapchains built in each part from HULTUI_STARTUP=1's lines, which this process
    /// reads off its own stderr, and the frames drawn (Windows).
    /// </summary>
    private static int DragAndMove(RendererKind renderer)
    {
        var trace = new SwapchainLines(Console.Error);
        Console.SetError(trace);
        using var app = Foreground.NoActivate(new Application("vulkan-probe", 640, 400, renderer: renderer));
        int frames = 0;
        // The UI thread's allocation count as each frame draws: what one step of the drag costs.
        var allocated = new long[256];
        app.SetRoot(Flex.Row().FlexGrow(1).Children(
            Box.New().FlexGrow(1).Background(Color.FromHex("#FF0000")),
            Box.New().FlexGrow(1).Background(Color.FromHex("#0000FF")),
            Canvas.New(_ => { if (frames < allocated.Length) allocated[frames] = GC.GetAllocatedBytesForCurrentThread(); frames++; })
                .Width(1f).Height(1f)));
        for (int i = 0; i < 12; i++)
            app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);

        nint hwnd = app.PrimaryWindow.NativeHandle;
        const int steps = 16;
        int builds = trace.Builds, oneToOne = trace.OneToOne, before = frames, purges = trace.Purges;
        SendMessageW(hwnd, EnterSizeMove, 0, 0);
        for (int i = 0; i < steps; i++)
            SetWindowPos(hwnd, 0, 0, 0, 1000 + 24 * (i + 1), 700 + 16 * (i + 1), NoMove | NoZOrder | NoActivate);
        Console.WriteLine($"probe: drag {steps} steps, {trace.Builds - builds} builds ({trace.OneToOne - oneToOne} one-to-one), " +
            $"{frames - before} frames, {trace.Purges - purges} offscreens freed");
        // Back the way it came, in the swapchain the drag grew: no build, every image reused at a
        // new window size each step.
        builds = trace.Builds; before = frames;
        for (int i = steps - 2; i >= 0; i--)
            SetWindowPos(hwnd, 0, 0, 0, 1000 + 24 * (i + 1), 700 + 16 * (i + 1), NoMove | NoZOrder | NoActivate);
        var perStep = new List<long>();
        for (int f = before + 1; f < Math.Min(frames, allocated.Length); f++) perStep.Add(allocated[f] - allocated[f - 1]);
        perStep.Sort();
        Console.WriteLine($"probe: drag back {steps - 1} steps, {trace.Builds - builds} builds, {frames - before} frames, " +
            $"{(perStep.Count > 0 ? perStep[perStep.Count / 2] : -1)} bytes a step");
        builds = trace.Builds; oneToOne = trace.OneToOne; before = frames;
        SendMessageW(hwnd, ExitSizeMove, 0, 0);
        GetClientRect(hwnd, out var client);
        Console.WriteLine($"probe: drag ended, {trace.Builds - builds} builds ({trace.OneToOne - oneToOne} one-to-one, " +
            $"last {trace.Last}), {frames - before} frames, client {client.Right}x{client.Bottom}");
        for (int i = 0; i < 2; i++)
            app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);

        builds = trace.Builds; before = frames;
        SendMessageW(hwnd, EnterSizeMove, 0, 0);
        for (int i = 0; i < steps; i++)
            SetWindowPos(hwnd, 0, 100 + 10 * (i + 1), 100, 0, 0, NoSize | NoZOrder | NoActivate);
        SendMessageW(hwnd, ExitSizeMove, 0, 0);
        Console.WriteLine($"probe: move {steps} steps, {trace.Builds - builds} builds, {frames - before} frames");
        Console.WriteLine("probe: done");
        return 0;
    }

    /// <summary>stderr, counting the swapchain builds HULTUI_STARTUP=1 reports, and the scratch
    /// purges that follow an offscreen let go (one each, when no image was disposed).</summary>
    private sealed class SwapchainLines(TextWriter inner) : TextWriter
    {
        private static readonly System.Text.RegularExpressions.Regex Build = new(@"vulkan swapchain (\d+x\d+) x\d+");
        public int Builds, OneToOne, Purges;
        public string Last = "";
        public override System.Text.Encoding Encoding => inner.Encoding;
        public override void Write(char value) => inner.Write(value);
        public override void WriteLine(string? value)
        {
            if (value is not null && value.Contains("vulkan scratch purged", StringComparison.Ordinal)) Purges++;
            if (value is not null && Build.Match(value) is { Success: true } m)
            {
                Builds++;
                bool scaled = value.Contains("one-to-one", StringComparison.Ordinal);
                if (scaled) OneToOne++;
                Last = m.Groups[1].Value + (scaled ? " one-to-one" : " exact");
            }
            inner.WriteLine(value);
        }
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
        // Each stage ends on a post from a timer thread. It ended on the first loop pass past its
        // time, which an idle loop used to make every 16 ms; it now waits for its next deadline or
        // a wake, and a minimized window with a change waiting owns none (plan 17,
        // runtime-idle-wakeups), so the stage never ended.
        int[] ends = [1500, 2500, 3000];
        new Thread(() =>
        {
            foreach (int end in ends)
            {
                int wait = end - (int)clock.ElapsedMilliseconds;
                if (wait > 0) Thread.Sleep(wait);
                Application.InvokeOnMainThread(EndStage);
            }
        }) { IsBackground = true }.Start();

        void EndStage()
        {
            long ms = clock.ElapsedMilliseconds;
            if (stage >= ends.Length) return;
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
        }
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

    /// <summary>
    /// Images drawn, then disposed. A GPU context keeps the texture it uploaded for an image
    /// after the image is gone, as a spare for a later texture of exactly that size; the Vulkan
    /// surface purges those spares after the next frame. Each image here is 16 MB of texture.
    /// </summary>
    private static int DisposedImages(RendererKind renderer)
    {
        using var app = Foreground.NoActivate(new Application("vulkan-probe", 480, 300, renderer: renderer));
        var sources = new HultUI.Rendering.Skia.SkiaImageSource[4];
        string[] colours = ["#FF0000", "#00FF00", "#0000FF", "#FFFF00"];
        for (int i = 0; i < sources.Length; i++)
        {
            var bitmap = new SkiaSharp.SKBitmap(2048, 2048, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
            bitmap.Erase(SkiaSharp.SKColor.Parse(colours[i]));
            sources[i] = new HultUI.Rendering.Skia.SkiaImageSource(bitmap);
        }
        app.SetRoot(Flex.Row().FlexGrow(1).Children(
            sources.Select(s => (HultUI.Core.Elements.Element)Image.New(s).FlexGrow(1)).ToArray()));
        for (int i = 0; i < 6; i++)
            app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        Console.WriteLine("probe: drew the images");

        app.SetRoot(Box.New().FlexGrow(1).Background(Color.FromHex("#202020")));
        foreach (var source in sources) source.Dispose();
        Console.WriteLine("probe: disposed the images");
        for (int i = 0; i < 6; i++)
            app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        Console.WriteLine("probe: done");
        return 0;
    }

    /// <summary>
    /// Captures of reused swapchain images, each checked against the frame just drawn. The
    /// renderer keeps a render target per swapchain image, which is right only while the host
    /// hands every image back in the layout Skia left it in: kept without that, three images drew
    /// three frames and every frame after was black. A stale image shows an older number, and a
    /// target over the wrong image another frame's.
    /// </summary>
    private static int FrameCounter(RendererKind renderer)
    {
        string? capture = Environment.GetEnvironmentVariable("HULTUI_VULKAN_CAPTURE");
        if (capture is null) { Console.WriteLine("probe: HULTUI_VULKAN_CAPTURE is not set"); return 2; }
        using var app = Foreground.NoActivate(new Application("vulkan-probe", 480, 300, renderer: renderer));
        nint hwnd = app.PrimaryWindow.NativeHandle;
        int drawn = 0;

        void Look()
        {
            if (!File.Exists(capture)) return;
            var (width, height, pixels) = ReadPpm(capture);
            File.Delete(capture);
            int shown = 0;
            var bits = new System.Text.StringBuilder(16);
            for (int bit = 0; bit < 16; bit++)
            {
                int i = (10 * width + bit * (width / 16) + width / 32) * 3;
                bool white = pixels[i] > 200 && pixels[i + 1] > 200 && pixels[i + 2] > 200;
                bool black = pixels[i] < 40 && pixels[i + 1] < 40 && pixels[i + 2] < 40;
                bits.Append(white ? '1' : black ? '0' : '?');
                if (white) shown |= 1 << bit;
            }
            int b = ((height * 3 / 4) * width + width / 2) * 3;
            var background = (pixels[b], pixels[b + 1], pixels[b + 2]);
            int colour = background is ( > 200, < 60, < 60) ? 0 : background is ( < 60, > 200, < 60) ? 1
                : background is ( < 60, < 60, > 200) ? 2 : -1;
            bool right = !bits.ToString().Contains('?') && shown == (drawn & 0xFFFF) && colour == drawn % 3;
            Console.WriteLine($"probe: capture {(right ? "right" : "WRONG")}: drew {drawn}, showed {shown} " +
                $"(cells {bits}, background {background}) at {width}x{height}");
        }

        void Draw(int frames)
        {
            for (int f = 0; f < frames; f++)
            {
                drawn++;
                var cells = new HultUI.Core.Elements.Element[16];
                for (int bit = 0; bit < 16; bit++)
                    cells[bit] = Box.New().FlexGrow(1).FlexBasis(0).Height(24f)
                        .Background(((drawn >> bit) & 1) != 0 ? Color.White : Color.Black);
                var background = (drawn % 3) switch
                {
                    0 => Color.FromHex("#FF0000"),
                    1 => Color.FromHex("#00FF00"),
                    _ => Color.FromHex("#0000FF"),
                };
                app.SetRoot(Flex.Column().FlexGrow(1).Background(background)
                    .Children(Flex.Row().Height(24f).Children(cells)));
                app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
                Look();
            }
        }

        // HULTUI_VULKAN_CAPTURE_FRAME counts from each swapchain's build, frames drawn inside a
        // resize included; 60 frames after each change is past any sensible setting.
        int perSwapchain = int.Parse(Environment.GetEnvironmentVariable("HULTUI_VULKAN_CAPTURE_FRAME") ?? "4") + 20;
        Draw(perSwapchain);
        if (OperatingSystem.IsWindows())
        {
            foreach (var (width, height) in new[] { (560, 340), (420, 280) })
            {
                SetWindowPos(hwnd, 0, 0, 0, width, height, NoMove | NoZOrder | NoActivate);
                Look();
                Draw(perSwapchain);
            }
            ShowWindow(hwnd, ShowMinNoActive);
            for (int i = 0; i < 3; i++)
                app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
            ShowWindow(hwnd, ShowNoActivate);
            Draw(perSwapchain);

            // A drag: the first size builds an over-allocated swapchain, the next ones keep it and
            // draw into the same images at a new size, so its capture is of a reused image drawn
            // after a kept resize; the end of the drag builds an exact one, which captures again.
            SendMessageW(hwnd, EnterSizeMove, 0, 0);
            foreach (var (width, height) in new[] { (720, 480), (730, 486), (740, 492), (750, 498) })
            {
                SetWindowPos(hwnd, 0, 0, 0, width, height, NoMove | NoZOrder | NoActivate);
                Look();
                Draw(perSwapchain / 4);
            }
            SendMessageW(hwnd, ExitSizeMove, 0, 0);
            Look();
            Draw(perSwapchain);
        }
        Console.WriteLine($"probe: done after {drawn} frames");
        return 0;
    }

    /// <summary>
    /// What a steady frame allocates: one box repainted, nothing else changing. The display's
    /// colour comes by events, as in Reel and Aperture, so its poll (about 1.1 KB every 100 ms)
    /// is not counted. Least of five rounds: a round can catch a one-off, such as a buffer
    /// reaching its high-water mark.
    /// </summary>
    private static int FrameBytes(RendererKind renderer)
    {
        using var app = Foreground.NoActivate(new Application("vulkan-probe", 480, 300, renderer: renderer,
            displayColorEvents: true));
        var changing = Box.New().Width(40f).Height(20f).Background(Color.FromHex("#FF8800"));
        app.SetRoot(Flex.Column().FlexGrow(1).Padding(16f).Gap(8f).Background(Color.FromHex("#141418")).Children(
            Text.New("Steady frames").TextColor(Color.White).FontSize(20f),
            changing,
            Box.New().Width(200f).Height(80f).CornerRadius(8f).Background(Color.FromHex("#2B2F3A"))
                .BoxShadow(0f, 6f, 18f, new Color(0, 0, 0, 120))));

        void Frame()
        {
            changing.MarkPaintDirty();
            app.RunFrame(TimeSpan.FromMilliseconds(16));
        }

        for (int i = 0; i < 120; i++) Frame();
        double least = double.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 120; i++) Frame();
            least = Math.Min(least, (GC.GetAllocatedBytesForCurrentThread() - before) / 120.0);
        }
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"probe: {least:0.0} bytes a frame"));
        Console.WriteLine("probe: done");
        return 0;
    }

    private static int FirstThread(RendererKind renderer, int busyMs)
    {
        using var app = Foreground.NoActivate(new Application("vulkan-probe", 320, 200, renderer: renderer));
        var changing = Box.New().Width(40f).Height(20f).Background(Color.FromHex("#FF8800"));
        app.SetRoot(Flex.Column().FlexGrow(1).Padding(16f).Background(Color.FromHex("#141418")).Children(changing));

        // The first frame as Run draws it; with busyMs, a new frame every pass for that long; then
        // the loop until 1 s after the first frame (busy: 0.4 s after the work), drawing only what
        // it is asked to, so every frame it draws is one the probe did not ask for.
        app.RunFrame(TimeSpan.FromMilliseconds(16));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < busyMs)
        {
            changing.MarkPaintDirty();
            app.RunFrame(TimeSpan.FromMilliseconds(16));
            Thread.Sleep(5);
        }
        var unasked = new List<double>();
        long restUntil = busyMs > 0 ? busyMs + 400 : 1000;
        while (clock.ElapsedMilliseconds < restUntil)
        {
            if (app.RunFrame(TimeSpan.FromMilliseconds(16))) unasked.Add(clock.Elapsed.TotalMilliseconds);
            Thread.Sleep(5);
        }
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"probe: frames drawn unasked from {busyMs} ms: {unasked.Count}, at {string.Join(" ", unasked.Select(t => t.ToString("0", System.Globalization.CultureInfo.InvariantCulture)))} ms after the first"));

        // Eight pool workers, all started before the frames: an app's first request.
        ThreadPool.GetMinThreads(out int workers, out int io);
        ThreadPool.SetMinThreads(Math.Max(workers, 12), io);
        var started = new CountdownEvent(8);
        var release = new ManualResetEventSlim();
        for (int i = 0; i < 8; i++) ThreadPool.UnsafeQueueUserWorkItem(_ => { started.Signal(); release.Wait(); }, null);
        started.Wait();
        release.Set();

        var took = new double[2];
        for (int i = 0; i < took.Length; i++)
        {
            changing.MarkPaintDirty();
            long at = System.Diagnostics.Stopwatch.GetTimestamp();
            app.RunFrame(TimeSpan.FromMilliseconds(16));
            took[i] = System.Diagnostics.Stopwatch.GetElapsedTime(at).TotalMilliseconds;
            Thread.Sleep(8);
        }
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"probe: frames after the pool grew {took[0]:0.0} ms, {took[1]:0.0} ms"));
        Console.WriteLine("probe: done");
        return 0;
    }

    /// <summary>
    /// What a steady video frame allocates on the UI thread, and whether the picture keeps
    /// changing. A canvas drawn with every frame reads the thread's allocation count, so a round
    /// counts everything the thread did from the frame before it to its last: the draw, the
    /// player's posts, the loop. The Vulkan renderer keeps its wrapper over a video's shared image
    /// from frame to frame, and a wrapper that went stale would keep showing one picture: the clip
    /// the test writes gets brighter frame by frame, and a capture taken after the rounds, on the
    /// swapchain a resize builds, must be brighter than the one taken before them. Without a
    /// capture (or on GL, which has none) the rounds start after two seconds of playback.
    /// </summary>
    private static int VideoFrameBytes(RendererKind renderer, string clip)
    {
        if (!MpvBackend.IsAvailable) { Console.WriteLine("probe: no libmpv"); return 4; }
        string? capture = renderer == RendererKind.Vulkan ? Environment.GetEnvironmentVariable("HULTUI_VULKAN_CAPTURE") : null;
        using var app = Foreground.NoActivate(new Application("vulkan-probe", 480, 270, renderer: renderer,
            displayColorEvents: true));
        var allocated = new long[4096];
        int frames = 0;
        using var player = new MediaPlayer(new MpvBackend());
        app.SetRoot(Flex.Row().FlexGrow(1).Children(
            VideoElement.New(player).FlexGrow(1),
            Canvas.New(_ =>
            {
                int i = Volatile.Read(ref frames);
                if (i < allocated.Length) allocated[i] = GC.GetAllocatedBytesForCurrentThread();
                Volatile.Write(ref frames, i + 1);
            }).Width(1f).Height(1f)));
        player.Open(MediaSource.FromFile(clip), autoPlay: true);

        const int round = 60, rounds = 5;
        int measureFrom = -1;
        var brightness = new List<int>();
        string? failure = null;
        nint hwnd = app.PrimaryWindow.NativeHandle;

        // The video's brightness in the capture, once it has been written; the file is read again
        // until it is whole, and deleted.
        bool TakeCapture()
        {
            if (capture is null || !File.Exists(capture)) return false;
            try
            {
                var (width, height, pixels) = ReadPpm(capture);
                int i = ((height / 2) * width + width / 2) * 3;
                brightness.Add((pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3);
                File.Delete(capture);
                return true;
            }
            catch (Exception ex) when (ex is IOException or IndexOutOfRangeException or ArgumentException)
            {
                return false;
            }
        }

        static bool WaitFor(Func<bool> done, double seconds)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!done())
            {
                if (clock.Elapsed.TotalSeconds > seconds) return false;
                Thread.Sleep(10);
            }
            return true;
        }

        new Thread(() =>
        {
            try
            {
                if (!WaitFor(() => Volatile.Read(ref frames) > 0, 20)) { failure = "nothing was drawn"; return; }
                if (capture is null) Thread.Sleep(2000);
                else if (!WaitFor(TakeCapture, 20)) { failure = "the first capture never came"; return; }
                int from = Volatile.Read(ref frames) + 5;
                Volatile.Write(ref measureFrom, from);
                if (!WaitFor(() => Volatile.Read(ref frames) > from + round * rounds, 30))
                {
                    failure = $"the video stopped drawing ({player.State}, {player.Error})";
                    return;
                }
                if (capture is null) return;
                // A new swapchain, which captures again.
                Application.InvokeOnMainThread(() => SetWindowPos(hwnd, 0, 0, 0, 560, 315, NoMove | NoZOrder | NoActivate));
                if (!WaitFor(TakeCapture, 20)) failure = "the second capture never came";
            }
            catch (Exception ex) { failure = ex.ToString(); }
            finally { Application.InvokeOnMainThread(() => app.PrimaryWindow.Close()); }
        }) { IsBackground = true }.Start();
        app.Run();

        if (failure is not null) { Console.WriteLine("probe: " + failure); return 2; }
        int drawn = Math.Min(frames, allocated.Length);
        double least = double.MaxValue;
        for (int r = 0; r < rounds; r++)
        {
            int a = measureFrom + r * round, b = a + round;
            if (a >= 0 && b < drawn) least = Math.Min(least, (allocated[b] - allocated[a]) / (double)round);
        }
        Console.WriteLine($"probe: video {player.VideoSize}, {player.SinkMode} mode, {drawn} frames");
        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"probe: {least:0.0} bytes a video frame"));
        if (brightness.Count == 2) Console.WriteLine($"probe: video brightness {brightness[0]} then {brightness[1]}");
        Console.WriteLine("probe: done");
        return 0;
    }

    private static (int Width, int Height, byte[] Pixels) ReadPpm(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        int at = 0;
        string Token()
        {
            while (char.IsWhiteSpace((char)file[at])) at++;
            int from = at;
            while (!char.IsWhiteSpace((char)file[at])) at++;
            return System.Text.Encoding.ASCII.GetString(file, from, at - from);
        }
        Token();
        int width = int.Parse(Token()), height = int.Parse(Token());
        Token();
        return (width, height, file[(at + 1)..]);
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

    private const uint NoSize = 0x0001, NoMove = 0x0002, NoZOrder = 0x0004, NoActivate = 0x0010;
    private const int ShowMinNoActive = 7, ShowNoActivate = 4;
    private const uint EnterSizeMove = 0x0231, ExitSizeMove = 0x0232;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SendMessageW(nint hwnd, uint message, nint wParam, nint lParam);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Rect32 { public int Left, Top, Right, Bottom; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hwnd, out Rect32 rect);

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
