using System.Diagnostics;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;
using HultUI.Platform.SDL3;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.UiTests;

/// <summary>
/// A pass of the application loop in a real SDL window. The headless suite's
/// FrameAllocationTests drive the same loop on the headless platform, whose pump and wait do
/// nothing, so neither what the SDL pump allocates nor how long the SDL wait parks shows up
/// there (plan 17, runtime-sdl-pump-closure and runtime-paced-wait-spin).
/// </summary>
[Collection("ui")] // one Application per process, and SDL is process-global
public sealed class SdlLoopPassTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    [UiFact]
    public void AnIdlePass_AllocatesNothing()
    {
        using var guard = ForegroundGuard.Begin();
        var platform = new SdlWindowingPlatform();
        using var app = new Application(platform, "idle pass", 300, 200);
        app.SetRoot(Flex.Column().Padding(8).Children(TextElement.New("Nothing changes here"), Button.New("Button")));
        for (int i = 0; i < 4; i++) app.RunFrame(Frame, forceRender: true);
        for (int i = 0; i < 200; i++) { _ = app.IsRunning; app.RunFrame(Frame); }

        // Counted per pass rather than in total: an event the OS sends the window meanwhile (the
        // pointer crossing it) is handled in the pass and may allocate, which is not what this
        // is about. A pass that allocates for itself allocates in every pass.
        const int passes = 500;
        int allocatingPasses = 0;
        long allocated = 0;
        bool running = true;
        for (int i = 0; i < passes; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            running &= app.IsRunning;
            app.RunFrame(Frame);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            if (bytes > 0) { allocatingPasses++; allocated += bytes; }
        }

        output.WriteLine($"{allocatingPasses} of {passes} idle passes allocated, {allocated} B in all");
        Assert.True(running);
        Assert.True(allocatingPasses <= passes / 10,
            $"{allocatingPasses} of {passes} idle passes allocated ({allocated} B in all)");
    }

    [UiFact]
    public void AShortWait_ParksForItsWholeTimeout()
    {
        using var guard = ForegroundGuard.Begin();
        // Run waits out the rest of a display interval with waits of a few milliseconds, the
        // last of them 1 ms. On Windows SDL rounded what was left of a wait down to whole
        // milliseconds after its own clock had started, so 4 ms parked for ~3 and 1 ms not at
        // all. Medians: the odd wait cut short by an OS event the window was sent meanwhile
        // does not count.
        var platform = new SdlWindowingPlatform();
        using var app = new Application(platform, "short wait", 300, 200);
        app.RunFrame(Frame, forceRender: true); // shown after its first frame
        for (int i = 0; i < 20; i++) { app.RunFrame(Frame); Thread.Sleep(5); }

        double one = MedianWait(platform, 1), four = MedianWait(platform, 4);

        output.WriteLine($"median wait for 1 ms: {one:F3} ms; for 4 ms: {four:F3} ms");
        Assert.True(one >= 0.5, $"a 1 ms wait parked for {one:F3} ms (median)");
        Assert.True(four >= 3.5, $"a 4 ms wait parked for {four:F3} ms (median)");
    }

    private static double MedianWait(SdlWindowingPlatform platform, int timeoutMs)
    {
        var times = new double[25];
        for (int i = 0; i < times.Length; i++)
        {
            platform.PumpEvents(null);
            long start = Stopwatch.GetTimestamp();
            platform.WaitForEvents(timeoutMs);
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        Array.Sort(times);
        return times[times.Length / 2];
    }

    [UiFact]
    public void ADirtyWindow_WaitsForItsFrame_RatherThanSpinning()
    {
        using var guard = ForegroundGuard.Begin();
        // While a tween runs the loop renders once per display interval and parks in between:
        // a pass that renders, a pass that waits out the rest of the interval, and now and then
        // one more. With a wait that returned at once it spun through the last millisecond of
        // every interval instead: ~240 passes a frame on a 200 Hz display, each pumping,
        // draining posts and ticking animations.
        var platform = new CountingPlatform(new SdlWindowingPlatform());
        using var app = new Application(platform, "paced passes", 200, 100);
        int frames = 0;
        var box = Box.New().Width(200f).Height(100f);
        app.SetRoot(Box.New().Children(box, Canvas.New(_ => frames++).Width(1f).Height(1f)));
        app.RunFrame(TimeSpan.Zero, forceRender: true);
        float hz = app.PrimaryWindow.RefreshRate;

        Animate.Value(0f, 1f, v => { box.Background(new Color((byte)(v * 255), 0, 0)); box.MarkPaintDirty(); })
            .Duration(1f)
            .OnComplete(() => app.PrimaryWindow.Close())
            .Start();
        frames = 0;
        platform.Passes = 0;
        app.Run();

        double perFrame = platform.Passes / (double)Math.Max(1, frames);
        output.WriteLine($"{platform.Passes} loop passes for {frames} frames ({perFrame:F2} a frame) on a {hz} Hz display");
        Assert.True(frames > 0);
        Assert.True(perFrame <= 6, $"{platform.Passes} loop passes for {frames} frames ({perFrame:F1} a frame)");
    }

    /// <summary>The SDL platform, counting the loop's passes.</summary>
    private sealed class CountingPlatform(IWindowingPlatform inner) : IWindowingPlatform
    {
        public int Passes;

        public IPlatformWindow CreateWindow(string title, int width, int height, WindowStyle style)
            => inner.CreateWindow(title, width, height, style);

        public bool PumpEvents(Action<IPlatformWindow>? onWindowClose)
        {
            Passes++;
            return inner.PumpEvents(onWindowClose);
        }

        public void WaitForEvents(int timeoutMs) => inner.WaitForEvents(timeoutMs);
        public void Wake() => inner.Wake();
        public TimeProvider TimeProvider => inner.TimeProvider;
        public void Dispose() => inner.Dispose();
    }
}
