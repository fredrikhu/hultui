using System.Diagnostics;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// The real loop renders at most once per display interval. With the swap interval at 0
/// a tween used to render as fast as the GPU returned (a 300 ms fade: two thousand
/// frames), and a 1000 Hz mouse made every report a frame; now both render about one
/// frame per interval, measured against the display's own rate.
/// </summary>
[Collection("ui")]
public sealed class FramePacingTests
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
        // still wait its turn. Wake is the cheapest event another thread can raise.
        var run = RunTween(eventStorm: true);
        AssertPaced(run);
        Assert.True(run.EventsRaised > run.Frames * 3,
            $"the storm raised {run.EventsRaised} events, not enough to test {run.Frames} frames against");
    }

    private static void AssertPaced((int Frames, float Hz, float Seconds, long ElapsedMs, int EventsRaised) run)
    {
        // The frame count over the display's rate: at least half (the loop also pumps,
        // drains and ticks), at most ~1.3x (a millisecond of slack per interval and
        // integer-millisecond waits both land slightly early).
        float expected = run.Seconds * run.Hz;
        Assert.InRange(run.Frames, expected * 0.5f, expected * 1.3f);
        Assert.InRange(run.ElapsedMs, run.Seconds * 800, run.Seconds * 2000);
    }

    private static (int Frames, float Hz, float Seconds, long ElapsedMs, int EventsRaised) RunTween(bool eventStorm)
    {
        var platform = new SdlWindowingPlatform();
        using var app = new Application(platform, "pacing", 200, 100);
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
