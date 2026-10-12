using System.Diagnostics;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Platform.SDL3;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.UiTests;

/// <summary>
/// The real loop renders at most once per display interval. With the swap interval at 0
/// a tween used to render as fast as the GPU returned (a 300 ms fade: two thousand
/// frames), and a 1000 Hz mouse made every report a frame; now both render about one
/// frame per interval, measured against the display's own rate.
/// </summary>
[Collection("ui")]
public sealed class FramePacingTests(ITestOutputHelper output)
{
    [UiFact]
    public void ATween_RendersAboutOneFramePerDisplayInterval()
    {
        var run = RunTween(eventStorm: false);
        AssertPaced(run);
    }

    [UiFact]
    public void AnEventEveryMillisecond_DoesNotMakeEveryEventAFrame()
    {
        // Each event ends the loop's wait early, as a mouse report does; the render must
        // still wait its turn, and at the display's rate, not a millisecond early each
        // time (which read 136 on a 120 Hz display before the deadline followed the
        // schedule). Wake is the cheapest event another thread can raise.
        var run = RunTween(eventStorm: true);
        AssertPaced(run);
        Assert.True(run.EventsRaised > run.Frames * 3,
            $"the storm raised {run.EventsRaised} events, not enough to test {run.Frames} frames against");
    }

    [UiFact]
    public void UnlimitedPacing_RendersAsFastAsTheGpuReturns()
    {
        // The measuring mode: what continuous redraw does on its own, opted into by an app.
        var run = RunTween(eventStorm: false, pacing: FramePacing.Unlimited);
        output.WriteLine($"unpaced: {run.Frames} frames in {run.ElapsedMs} ms on a {run.Hz} Hz display");
        Assert.True(run.Frames > run.Seconds * run.Hz * 3,
            $"{run.Frames} frames in {run.Seconds} s unpaced is no faster than the display's {run.Hz} Hz");
    }

    [UiFact]
    public void LeavingUnlimitedPacing_RendersAgainAtOnce()
    {
        // Continuous redraw is unpaced; when it is switched off the next paced frame
        // must come within an interval, not once a schedule advanced by thousands of
        // unpaced frames has caught up (the window froze for tens of seconds).
        using var guard = ForegroundGuard.Begin();
        var platform = new SdlWindowingPlatform();
        using var app = new Application(platform, "pacing", 200, 100);
        int frames = 0;
        var box = Box.New().Width(200f).Height(100f);
        var counter = Canvas.New(_ => frames++).Width(1f).Height(1f);
        app.SetRoot(Box.New().Children(box, counter));
        app.RunFrame(TimeSpan.Zero, forceRender: true);
        float hz = app.PrimaryWindow.RefreshRate;
        if (hz <= 0f) hz = 60f;

        int pacedFrames = 0;
        app.FramePacing = FramePacing.Unlimited;
        Animate.Value(0f, 1f, v => { box.Background(new Color((byte)(v * 255), 0, 0)); box.MarkPaintDirty(); })
            .Duration(0.5f)
            .OnComplete(() =>
            {
                app.FramePacing = FramePacing.Display;
                frames = 0;
                Animate.Value(0f, 1f, v => { box.Background(new Color(0, (byte)(v * 255), 0)); box.MarkPaintDirty(); })
                    .Duration(0.5f)
                    .OnComplete(() => { pacedFrames = frames; app.PrimaryWindow.Close(); })
                    .Start();
            })
            .Start();
        app.Run();

        output.WriteLine($"after unlimited: {pacedFrames} paced frames in 0.5 s on a {hz} Hz display");
        Assert.InRange(pacedFrames, 0.5f * hz * 0.5f, 0.5f * hz * 1.1f);
    }

    private void AssertPaced((int Frames, float Hz, float Seconds, long ElapsedMs, int EventsRaised) run)
    {
        // The frame count over the display's rate: at least half (the loop also pumps,
        // drains and ticks, and may miss a deadline), at most a tenth over (the schedule
        // is anchored, so slack shifts the phase and only timer granularity is left).
        float expected = run.Seconds * run.Hz;
        output.WriteLine($"paced: {run.Frames} frames in {run.ElapsedMs} ms on a {run.Hz} Hz display ({run.EventsRaised} events)");
        Assert.InRange(run.Frames, expected * 0.5f, expected * 1.1f);
        Assert.InRange(run.ElapsedMs, run.Seconds * 800, run.Seconds * 2000);
    }

    private static (int Frames, float Hz, float Seconds, long ElapsedMs, int EventsRaised) RunTween(
        bool eventStorm, FramePacing pacing = FramePacing.Display)
    {
        using var guard = ForegroundGuard.Begin();
        var platform = new SdlWindowingPlatform();
        using var app = new Application(platform, "pacing", 200, 100);
        app.FramePacing = pacing;
        int frames = 0;
        float shade = 0f;
        var box = Box.New().Width(200f).Height(100f);
        var counter = Canvas.New(_ => frames++).Width(1f).Height(1f);
        app.SetRoot(Box.New().Children(box, counter));
        app.RunFrame(TimeSpan.Zero, forceRender: true);

        float hz = app.PrimaryWindow.RefreshRate;
        if (hz <= 0f) hz = 60f;   // what the loop assumes when the platform does not know
        const float seconds = 1f;

        int eventsRaised = 0;
        bool stop = false;
        var storm = new Thread(() =>
        {
            while (!Volatile.Read(ref stop)) { platform.Wake(); eventsRaised++; Thread.Sleep(1); }
        });

        Animate.Value(0f, 1f, v => { shade = v; box.Background(new Color((byte)(v * 255), 0, 0)); box.MarkPaintDirty(); })
            .Duration(seconds)
            .OnComplete(() => app.PrimaryWindow.Close())   // on the UI thread, inside the tick
            .Start();

        frames = 0;
        var clock = Stopwatch.StartNew();
        if (eventStorm) storm.Start();
        app.Run();
        long elapsed = clock.ElapsedMilliseconds;
        Volatile.Write(ref stop, true);
        if (eventStorm) storm.Join();

        Assert.Equal(1f, shade);
        return (frames, hz, seconds, elapsed, eventsRaised);
    }
}
