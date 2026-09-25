using System.Runtime.InteropServices;
using HultUI.Core.Primitives;
using HultUI.Headless;
using HultUI.Media.Testing;
using Xunit;

namespace HultUI.Media.Tests;

/// <summary>
/// <see cref="MediaPlayer"/> over the fake backend: commands reach the backend, its
/// events become reactive state, frames become versions, and an event raised from
/// another thread lands on the next frame rather than on the thread that raised it.
/// </summary>
public class MediaPlayerTests
{
    private static readonly MediaSource Clip = MediaSource.FromFile(@"C:\clips\intro.mp4");

    private static (MediaPlayer player, FakeMediaBackend backend) NewPlayer()
    {
        var backend = new FakeMediaBackend();
        return (new MediaPlayer(backend), backend);
    }

    [Fact]
    public void BeforeOpening_ThereIsNothing()
    {
        var (player, backend) = NewPlayer();
        using (player)
        {
            Assert.Equal(PlaybackState.NoMedia, player.State);
            Assert.Null(player.Source);
            Assert.Null(player.Duration);
            Assert.False(player.HasVideo);
            Assert.False(player.Frames.IsLoaded);
            Assert.Equal(["Attach"], backend.Calls);
        }
    }

    [Fact]
    public void Open_ReportsWhatTheBackendFound_AndPaintsAFirstFrame()
    {
        var (player, backend) = NewPlayer();
        using (player)
        {
            backend.Duration = TimeSpan.FromSeconds(90);
            backend.VideoWidth = 1440;
            backend.VideoHeight = 1080;
            backend.SampleAspect = 4.0 / 3.0;   // anamorphic: shown wider than coded
            backend.AudioTracks.Add(new MediaTrack(1, "English", "en"));
            backend.SubtitleTracks.Add(new MediaTrack(7, "Svenska", "sv"));

            player.Open(Clip);

            Assert.Same(Clip, player.Source);
            Assert.Equal(PlaybackState.Paused, player.State);
            Assert.Equal(TimeSpan.FromSeconds(90), player.Duration);
            Assert.Equal(new Size(1920, 1080), player.VideoSize);
            Assert.True(player.HasVideo);
            Assert.Equal(1, player.SelectedAudioTrack);
            Assert.Null(player.SelectedSubtitleTrack);
            Assert.Single(player.SubtitleTracks);
            Assert.True(player.Frames.IsLoaded);
            Assert.Equal(1, player.Frames.Version);
            Assert.Equal(new Size(1440, 1080), player.Frames.IntrinsicSize);
        }
    }

    [Fact]
    public void Open_ResetsWhatThePreviousMediaLeft()
    {
        var (player, backend) = NewPlayer();
        using (player)
        {
            backend.AudioTracks.Add(new MediaTrack(1, "English"));
            player.Open(Clip);
            player.Play();
            backend.Advance(TimeSpan.FromSeconds(3));
            backend.AudioTracks.Clear();

            backend.FailNextOpenWith = "no such file";
            player.Open(MediaSource.FromFile("missing.mp4"));

            Assert.Equal(PlaybackState.Failed, player.State);
            Assert.Equal("no such file", player.Error);
            Assert.Equal(TimeSpan.Zero, player.Position);
            Assert.Null(player.Duration);
            Assert.Empty(player.AudioTracks);
            Assert.False(player.HasMedia);

            player.Open(Clip);
            Assert.Null(player.Error);
            Assert.Equal(PlaybackState.Paused, player.State);
        }
    }

    [Fact]
    public void ARepaintRequest_FromTheRenderersThread_PostsFromElsewhere_AndCoalesces()
    {
        // The renderer's thread must return at once and never touch the platform: a
        // decoder calls this from a callback its own Render waits on, and a post from
        // there can deadlock under live resize (see MediaPlayer.OnRepaintRequested).
        using var session = HeadlessSession.Create(200, 100);
        var backend = new FakeMediaBackend { GpuRenderer = new NullRenderer() };
        using var player = new MediaPlayer(backend);
        int uiThread = Environment.CurrentManagedThreadId;
        var arrivedOn = new List<int>();
        player.FrameArrived += () => arrivedOn.Add(Environment.CurrentManagedThreadId);

        var requester = new Thread(() =>
        {
            for (int i = 0; i < 50; i++) backend.Sink!.RequestRepaint();
        });
        requester.Start();
        requester.Join();
        Assert.Empty(arrivedOn);   // nothing ran on the requesting thread

        session.PumpUntil(() => arrivedOn.Count > 0, TimeSpan.FromSeconds(5));
        session.PumpFrames(3);
        Assert.Equal([uiThread], arrivedOn);   // fifty requests, one frame

        backend.Sink!.RequestRepaint();        // from the UI thread: immediate, as before
        Assert.Equal([uiThread, uiThread], arrivedOn);
    }

    private sealed class NullRenderer : IGpuFrameRenderer
    {
        public void OnModeDecided(VideoSinkMode mode) { }
        public void PrepareGL(Func<string, nint> getProcAddress) { }
        public bool TryGetFrameSize(out int width, out int height) { width = height = 0; return false; }
        public bool RendersBottomUp => false;
        public bool Render(Func<string, nint> getProcAddress, uint framebuffer, int width, int height,
            uint internalFormat) => false;
        public void ReleaseGL() { }
    }

    [Fact]
    public void Open_WithAutoPlay_StartsOnceLoaded_NotBefore()
    {
        var (player, backend) = NewPlayer();
        using (player)
        {
            // The fake loads synchronously, so "once loaded" and "at once" coincide here;
            // what the test pins is that the play comes from the load, not from Open.
            player.Open(Clip, autoPlay: true);
            Assert.True(player.IsPlaying);
            Assert.Equal([@"Open(file:C:\clips\intro.mp4)", "Play"], backend.Calls.Where(c => c.StartsWith("Open") || c == "Play"));

            backend.FailNextOpenWith = "nope";
            player.Open(Clip, autoPlay: true);
            Assert.Equal(PlaybackState.Failed, player.State);
            Assert.Equal(1, backend.Calls.Count(c => c == "Play"));   // a failed load never plays
        }
    }

    [Fact]
    public void PlayPause_AreTheBackendsWord()
    {
        var (player, backend) = NewPlayer();
        using (player)
        {
            player.Play();                        // nothing open: not forwarded
            Assert.DoesNotContain("Play", backend.Calls);

            player.Open(Clip);
            player.TogglePlay();
            Assert.True(player.IsPlaying);
            player.TogglePlay();
            Assert.Equal(PlaybackState.Paused, player.State);
            Assert.Equal(["Play", "Pause"], backend.Calls.Where(c => c is "Play" or "Pause"));
        }
    }

    [Fact]
    public void Advancing_MovesThePlayhead_AndPublishesTheFramesDue()
    {
        var (player, backend) = NewPlayer();
        using (player)
        {
            backend.FrameRate = 10;
            player.Open(Clip);
            int arrived = 0;
            player.FrameArrived += () => arrived++;
            player.Play();

            backend.Advance(TimeSpan.FromMilliseconds(250));

            Assert.Equal(TimeSpan.FromMilliseconds(250), player.Position);
            Assert.Equal(2, arrived);                       // frames at 100 ms and 200 ms
            Assert.Equal(3, player.Frames.Version);         // plus the one open painted
        }
    }

    [Fact]
    public void Rate_ScalesHowFarAdvanceGoes()
    {
        var (player, backend) = NewPlayer();
        using (player)
        {
            player.Open(Clip);
            player.Rate = 2.0;
            player.Play();

            backend.Advance(TimeSpan.FromSeconds(1));

            Assert.Equal(TimeSpan.FromSeconds(2), player.Position);
            Assert.Contains("SetRate(2)", backend.Calls);
            Assert.Throws<ArgumentOutOfRangeException>(() => player.Rate = 0);
        }
    }

    [Fact]
    public void Seek_MovesPositionAtOnce_AndIsClampedToTheMedia()
    {
        var (player, backend) = NewPlayer();
        using (player)
        {
            player.Open(Clip);

            player.Seek(TimeSpan.FromSeconds(4));
            Assert.Equal(TimeSpan.FromSeconds(4), player.Position);
            Assert.Contains("Seek(00:00:04.000, precise)", backend.Calls);

            player.SeekBy(TimeSpan.FromSeconds(30));      // past the 10 s duration
            Assert.Equal(TimeSpan.FromSeconds(10), player.Position);
            Assert.Contains("Seek(00:00:10.000, keyframe)", backend.Calls);

            player.Seek(TimeSpan.FromSeconds(-5));
            Assert.Equal(TimeSpan.Zero, player.Position);
        }
    }

    [Fact]
    public void ReachingTheEnd_EndsPlayback_AndPlayStartsOver()
    {
        var (player, backend) = NewPlayer();
        using (player)
        {
            backend.Duration = TimeSpan.FromSeconds(2);
            player.Open(Clip);
            player.Play();

            backend.Advance(TimeSpan.FromSeconds(5));

            Assert.Equal(PlaybackState.Ended, player.State);
            Assert.Equal(TimeSpan.FromSeconds(2), player.Position);
            Assert.True(player.HasMedia);

            player.Play();
            Assert.True(player.IsPlaying);
            Assert.Equal(TimeSpan.Zero, player.Position);
        }
    }

    [Fact]
    public void VolumeAndMute_AreForwarded_AndVolumeIsClamped()
    {
        var (player, backend) = NewPlayer();
        using (player)
        {
            player.Volume = 1.7f;
            player.Muted = true;

            Assert.Equal(1f, player.Volume);
            Assert.True(player.Muted);
            Assert.Equal(1f, backend.Volume);
            Assert.True(backend.Muted);
        }
    }

    [Fact]
    public void Tracks_CanBeSelected_AndTheBackendConfirms()
    {
        var (player, backend) = NewPlayer();
        using (player)
        {
            backend.SubtitleTracks.Add(new MediaTrack(3, "English", "en"));
            player.Open(Clip);

            player.SelectSubtitleTrack(3);
            Assert.Equal(3, player.SelectedSubtitleTrack);
            Assert.Equal(3, backend.SelectedSubtitle);

            player.SelectSubtitleTrack(null);
            Assert.Null(player.SelectedSubtitleTrack);
            Assert.Contains("SelectSubtitleTrack(none)", backend.Calls);
        }
    }

    [Fact]
    public void Buffering_IsAFractionWhileWaiting_AndNullAfter()
    {
        var (player, backend) = NewPlayer();
        using (player)
        {
            player.Open(Clip);
            backend.ReportBuffering(0.4f);
            Assert.Equal(0.4f, player.Buffering);
            backend.ReportBuffering(null);
            Assert.Null(player.Buffering);
        }
    }

    [Fact]
    public void TheFrameTheSinkHolds_IsTheFrameTheBackendPublished()
    {
        var (player, backend) = NewPlayer();
        using (player)
        {
            backend.VideoWidth = 2;
            backend.VideoHeight = 2;
            backend.FrameRate = 1;
            player.Open(Clip);
            player.Play();
            backend.Advance(TimeSpan.FromSeconds(3));      // frames 1, 2, 3

            Assert.True(player.Frames.TryAcquire(out var frame));
            var bytes = new byte[frame.Stride * frame.Height];
            Marshal.Copy(frame.Pixels, bytes, 0, bytes.Length);
            player.Frames.Release(frame);

            Assert.All(bytes, b => Assert.Equal(3, b));
        }
    }

    [Fact]
    public void AnEventFromAnotherThread_LandsOnTheNextFrame()
    {
        using var session = HeadlessSession.Create();
        var (player, backend) = NewPlayer();
        using (player)
        {
            player.Open(Clip);

            var thread = new Thread(() => backend.Raise(new BackendEvent.Failed("decoder died")));
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)));

            // Not yet: the reactive graph is the UI thread's, so the event waits for it.
            Assert.Equal(PlaybackState.Paused, player.State);

            session.PumpFrame();
            Assert.Equal(PlaybackState.Failed, player.State);
            Assert.Equal("decoder died", player.Error);
        }
    }

    [Fact]
    public void Dispose_DisposesTheBackend_AndDropsLateEvents()
    {
        var (player, backend) = NewPlayer();
        player.Open(Clip);

        player.Dispose();

        Assert.True(backend.Disposed);
        backend.Raise(new BackendEvent.StateChanged(PlaybackState.Playing));
        Assert.Equal(PlaybackState.Paused, player.State);
    }
}
