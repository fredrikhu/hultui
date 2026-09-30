using HultUI.Core.Rendering;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Headless;
using HultUI.Media.Mpv;
using HultUI.Media.Testing;
using Xunit;

namespace HultUI.Media.Backends.Tests;

/// <summary>
/// The mpv backend against real media: a two-second uncompressed clip written by
/// <see cref="TestClip"/>, so nothing but libmpv is needed. Under the headless host the
/// surface is a raster one, so frames come through mpv's software renderer into the
/// pixel ring — the path a decoder shares with every other headless test.
/// </summary>
public class MpvBackendTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);
    private readonly string _clip = TestClip.WriteTempY4m(64, 36, fps: 10, frames: 20);

    public void Dispose() => File.Delete(_clip);

    private MediaSource Clip => MediaSource.FromFile(_clip);

    private sealed class Fixture : IDisposable
    {
        public HeadlessSession Session { get; } = HeadlessSession.Create(300, 200);
        public MpvBackend Backend { get; } = new();
        public MediaPlayer Player { get; }

        public Fixture()
        {
            Player = new MediaPlayer(Backend);
            Session.Application.SetRoot(Box.New().Children(Video.New(Player)));
            Session.PumpFrame();   // the first draw decides the pixel mode, and mpv gets its render context
        }

        public void Until(Func<bool> condition) => Session.PumpUntil(condition, Wait);

        public void Dispose()
        {
            Player.Dispose();
            Session.Dispose();
        }
    }

    [MpvFact]
    public void Open_ReportsDurationSizeAndPaused()
    {
        using var f = new Fixture();

        f.Player.Open(Clip);
        f.Until(() => f.Player.State == PlaybackState.Paused);

        Assert.InRange(f.Player.Duration!.Value.TotalSeconds, 1.9, 2.1);
        Assert.Equal(new Size(64, 36), f.Player.VideoSize);
        Assert.Equal(GpuSinkMode.Pixels, f.Player.SinkMode);
    }

    [MpvFact]
    public void OpeningBeforeTheFirstDraw_StillLoads()
    {
        // The sample does this: opens in Mount, before anything has rendered. With
        // vo=libmpv a file cannot start without a render context, so the backend has
        // to hold the source until the first draw provides one.
        using var session = HeadlessSession.Create(300, 200);
        using var player = new MediaPlayer(new MpvBackend());
        player.Open(Clip);
        session.Application.SetRoot(Box.New().Children(Video.New(player)));

        session.PumpUntil(() => player.State == PlaybackState.Paused, Wait);
        Assert.Equal(new Size(64, 36), player.VideoSize);
    }

    [MpvFact]
    public void Playing_PublishesFramesThroughTheSoftwareRenderer_AndMovesThePlayhead()
    {
        using var f = new Fixture();
        f.Player.Open(Clip);
        f.Until(() => f.Player.State == PlaybackState.Paused);
        long before = f.Player.Frames.Version;

        f.Player.Play();
        f.Until(() => f.Player.IsPlaying);
        f.Until(() => f.Player.Frames.Version > before + 2 && f.Player.Position > TimeSpan.FromMilliseconds(300));

        Assert.Equal(new Size(64, 36), f.Player.Frames.IntrinsicSize);
    }

    [MpvFact]
    public void Seek_MovesThePlayhead_WhilePaused()
    {
        using var f = new Fixture();
        f.Player.Open(Clip);
        f.Until(() => f.Player.State == PlaybackState.Paused);

        f.Player.Seek(TimeSpan.FromSeconds(1.5));
        f.Until(() => Math.Abs((f.Player.Position - TimeSpan.FromSeconds(1.5)).TotalSeconds) < 0.15);
        Assert.Equal(PlaybackState.Paused, f.Player.State);
    }

    /// <summary>
    /// After every load mpv holds its core until the first frame has been taken, for up to
    /// 200 ms, and the size to draw that frame at only arrives once the core lets go. A frame
    /// taken without a size ends the wait at once. One left waiting times out, and mpv counts
    /// it as dropped. Both opens are checked, because a second file starts with the first one's
    /// size already forgotten.
    /// </summary>
    [MpvFact]
    public void Opening_DoesNotLeaveMpvWaitingForItsFirstFrame()
    {
        using var f = new Fixture();

        f.Player.Open(Clip);
        f.Until(() => f.Player.State == PlaybackState.Paused);
        Assert.Equal(0, f.Backend.Peek("frame-drop-count"));

        f.Player.Open(Clip);
        f.Until(() => f.Player.State == PlaybackState.Paused);
        Assert.Equal(0, f.Backend.Peek("frame-drop-count"));
    }

    [MpvFact]
    public void ReachingTheEnd_EndsPlayback_AndPlayStartsOver()
    {
        using var f = new Fixture();
        f.Player.Open(Clip);
        f.Until(() => f.Player.State == PlaybackState.Paused);
        f.Player.Seek(TimeSpan.FromSeconds(1.7));
        f.Player.Play();

        f.Until(() => f.Player.State == PlaybackState.Ended);

        f.Player.Play();
        f.Until(() => f.Player.IsPlaying && f.Player.Position < TimeSpan.FromSeconds(1));
    }

    [MpvFact]
    public void AMissingFile_Fails_WithMpvsReason()
    {
        using var f = new Fixture();

        f.Player.Open(MediaSource.FromFile(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid() + ".mkv")));
        f.Until(() => f.Player.State == PlaybackState.Failed);

        Assert.False(string.IsNullOrEmpty(f.Player.Error));
    }

    /// <summary>
    /// The trimmed LGPL build reports itself as such. Only asserted when the caller
    /// says it expects that build (HULTUI_MPV_EXPECT_LGPL=1, as the natives workflow
    /// does); against any other libmpv the test only prints what it found.
    /// </summary>
    [MpvFact]
    public void TheLoadedBuild_IsTheTrimmedLgplOne_WhenExpected()
    {
        using var f = new Fixture();
        string configuration = f.Backend.PeekString("mpv-configuration") ?? "";
        string version = f.Backend.PeekString("mpv-version") ?? "";
        Console.WriteLine($"{version}: {configuration}");

        if (Environment.GetEnvironmentVariable("HULTUI_MPV_EXPECT_LGPL") != "1") return;
        Assert.Contains("-Dgpl=false", configuration);
        Assert.Contains("-Dlua=disabled", configuration);
        Assert.Contains("-Dvulkan=disabled", configuration);
    }

    [MpvFact]
    public void RateVolumeAndMute_ReachMpv()
    {
        using var f = new Fixture();
        f.Player.Open(Clip);
        f.Until(() => f.Player.State == PlaybackState.Paused);

        f.Player.Rate = 2;
        f.Player.Volume = 0.5f;
        f.Player.Muted = true;

        // Queued, not waited for: a read can overtake them, so wait for the values to land.
        f.Until(() => Math.Abs(f.Backend.Peek("speed") - 2) < 0.01
            && Math.Abs(f.Backend.Peek("volume") - 50) < 0.01
            && f.Backend.PeekString("mute") == "yes");
    }
}
