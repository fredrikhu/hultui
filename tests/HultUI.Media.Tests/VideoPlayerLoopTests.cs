using HultUI.Core.Builders;
using HultUI.Headless;
using HultUI.Media.Testing;
using Xunit;

namespace HultUI.Media.Tests;

/// <summary>
/// The transport bar under the application's own loop (<see cref="HeadlessSession.RunLoop"/>,
/// on the virtual clock) with an event every millisecond, as a playing video and a moving mouse
/// wake it. The loop steps the bar's hide timer and fade once per frame it may draw (plan 17,
/// reactivity-tween-tick-per-frame); they still end on time, in a window that draws and in one
/// that is minimized, which draws nothing.
/// </summary>
public class VideoPlayerLoopTests
{
    private sealed class Fixture : IDisposable
    {
        private int _frames;
        public HeadlessSession Session { get; } = HeadlessSession.Create(640, 400);
        public FakeMediaBackend Backend { get; } = new();
        public MediaPlayer Player { get; }
        public VideoPlayer VideoPlayer { get; }
        public int Frames => _frames;

        public Fixture()
        {
            Player = new MediaPlayer(Backend);
            VideoPlayer = new VideoPlayer(Player)
            {
                AutoHideDelay = TimeSpan.FromSeconds(1),
                FadeDuration = TimeSpan.FromMilliseconds(300),
            };
            Session.Application.SetRoot(Box.New().Children(
                VideoPlayer.Mount().Width(640f).Height(398f),
                Canvas.New(_ => _frames++).Width(1f).Height(1f)));
            Player.Open(MediaSource.FromFile("clip.mp4"));
            Session.PumpFrames(2);
        }

        /// <summary>Plays, and runs the loop until the bar has gone; the virtual milliseconds that took.</summary>
        public double PlayUntilTheBarHides()
        {
            long start = Session.Time.GetTimestamp();
            Player.Play();
            Session.RunLoop(() => !VideoPlayer.ControlsVisible, eventEvery: TimeSpan.FromMilliseconds(1),
                onEvent: () => { }, limit: TimeSpan.FromSeconds(5));
            return Session.Time.GetElapsedTime(start).TotalMilliseconds;
        }

        public void Dispose()
        {
            Player.Dispose();
            Session.Dispose();
        }
    }

    // The hide timer (1 s of delay and 10 ms) ends on the first step at or past 1.01 s, and the
    // fade on the first at or past 300 ms after that: each at most a display interval (16.7 ms
    // here) late.
    private const double Due = 1310, Late = 2 * 1000.0 / 60 + 1;

    [Fact]
    public void TheBar_HidesOnTime_WhileEventsArriveEveryMillisecond()
    {
        using var f = new Fixture();
        int frames = f.Frames;

        double took = f.PlayUntilTheBarHides();

        Assert.InRange(took, Due, Due + Late);
        // The fade was drawn: a frame for each interval of it, give or take the first and last.
        Assert.True(f.Frames - frames >= 16, $"{f.Frames - frames} frames drawn");
    }

    [Fact]
    public void TheBar_HidesOnTime_WhileTheWindowIsMinimized()
    {
        using var f = new Fixture();
        f.Session.Window.Minimized = true;
        int frames = f.Frames;

        double took = f.PlayUntilTheBarHides();

        Assert.InRange(took, Due, Due + Late);
        Assert.Equal(frames, f.Frames);   // nothing drawn while minimized
    }
}
