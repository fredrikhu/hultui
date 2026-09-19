using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Headless;
using HultUI.Media.Mpv;
using Xunit;

namespace HultUI.Media.Backends.Tests;

/// <summary>
/// The mpv backend against real media, with no file on disk: mpv's own <c>lavfi</c>
/// source generates a test pattern of a known size and length. Under the headless
/// host the sink is a raster one, so frames come through mpv's software renderer
/// into the pixel ring — the path a decoder shares with every other headless test.
/// </summary>
public class MpvBackendTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    /// <summary>Two seconds of a 64×36 test pattern at 10 fps, and a 440 Hz tone.</summary>
    private static MediaSource Pattern() =>
        MediaSource.FromUri("av://lavfi:testsrc=duration=2:size=64x36:rate=10");

    private sealed class Fixture : IDisposable
    {
        public HeadlessSession Session { get; } = HeadlessSession.Create(300, 200);
        public MpvBackend Backend { get; } = new();
        public MediaPlayer Player { get; }
        public VideoElement Video { get; }

        public Fixture()
        {
            Player = new MediaPlayer(Backend);
            Video = HultUI.Media.Video.New(Player);
            Session.Application.SetRoot(Box.New().Children(Video));
            Session.PumpFrame();
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

        f.Player.Open(Pattern());
        f.Until(() => f.Player.State == PlaybackState.Paused);

        Assert.InRange(f.Player.Duration!.Value.TotalSeconds, 1.9, 2.1);
        Assert.Equal(new Size(64, 36), f.Player.VideoSize);
    }

    [MpvFact]
    public void Playing_PublishesFramesThroughTheSoftwareRenderer_AndMovesThePlayhead()
    {
        using var f = new Fixture();
        f.Player.Open(Pattern());
        f.Until(() => f.Player.State == PlaybackState.Paused);

        f.Player.Play();
        f.Until(() => f.Player.IsPlaying);
        f.Until(() => f.Player.Frames.Version >= 3 && f.Player.Position > TimeSpan.FromMilliseconds(300));

        Assert.Equal(VideoSinkMode.Pixels, f.Player.SinkMode);
        Assert.Equal(new Size(64, 36), f.Player.Frames.IntrinsicSize);
    }

    [MpvFact]
    public void Seek_MovesThePlayhead_WhilePaused()
    {
        using var f = new Fixture();
        f.Player.Open(Pattern());
        f.Until(() => f.Player.State == PlaybackState.Paused);

        f.Player.Seek(TimeSpan.FromSeconds(1.5));
        f.Until(() => Math.Abs((f.Player.Position - TimeSpan.FromSeconds(1.5)).TotalSeconds) < 0.15);
        Assert.Equal(PlaybackState.Paused, f.Player.State);
    }

    [MpvFact]
    public void ReachingTheEnd_EndsPlayback_AndPlayStartsOver()
    {
        using var f = new Fixture();
        f.Player.Open(Pattern());
        f.Until(() => f.Player.State == PlaybackState.Paused);
        f.Player.Seek(TimeSpan.FromSeconds(1.8));
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

    [MpvFact]
    public void RateVolumeAndMute_ReachMpv()
    {
        using var f = new Fixture();
        f.Player.Open(Pattern());
        f.Until(() => f.Player.State == PlaybackState.Paused);

        f.Player.Rate = 2;
        f.Player.Volume = 0.5f;
        f.Player.Muted = true;

        Assert.Equal(2, f.Backend.Peek("speed"), 0.01);
        Assert.Equal(50, f.Backend.Peek("volume"), 0.01);
        Assert.Equal("yes", f.Backend.PeekString("mute"));
    }
}
