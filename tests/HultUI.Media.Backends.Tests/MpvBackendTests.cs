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

    /// <summary>
    /// Whether a video's picture is HDR comes from its own transfer, never from what mpv is asked to
    /// write: a Vulkan window draws through an 8-bit swapchain, whose target asks for an encoded
    /// picture, until it learns an HDR picture is on screen. Here the target is the pixel path's,
    /// always encoded, and a clip tagged PQ (by mpv's format filter, as the HDR checks tag a Y4M,
    /// which carries no colour tags) still says HDR; the same clip untagged says SDR. Not known
    /// before anything is open, or while it loads.
    /// </summary>
    [MpvFact]
    public void AVideosPictureRange_ComesFromItsOwnTransfer_NotFromTheTarget()
    {
        Assert.Equal(PictureRange.Sdr, RangeOfClip(null));
        Assert.Equal(PictureRange.Hdr, RangeOfClip("vf=format=colormatrix=bt.2020-ncl:primaries=bt.2020:gamma=pq"));
    }

    private PictureRange RangeOfClip(string? mpvOptions)
    {
        // Read once, in the constructor: this collection runs alone, so setting it around that is safe.
        string? before = Environment.GetEnvironmentVariable("HULTUI_MPV_OPTIONS");
        Environment.SetEnvironmentVariable("HULTUI_MPV_OPTIONS", mpvOptions);
        MpvBackend backend;
        try { backend = new MpvBackend(); }
        finally { Environment.SetEnvironmentVariable("HULTUI_MPV_OPTIONS", before); }

        using var session = HeadlessSession.Create(300, 200);
        using var player = new MediaPlayer(backend);
        session.Application.SetRoot(Box.New().Children(Video.New(player)));
        session.PumpFrame();
        var picture = (IPictureRangeSource)backend;
        Assert.Equal(PictureRange.Unknown, picture.PictureRange);

        player.Open(Clip, autoPlay: true);
        session.PumpUntil(() => picture.PictureRange != PictureRange.Unknown, Wait);
        Assert.Equal(GpuSinkMode.Pixels, player.SinkMode);
        return picture.PictureRange;
    }

    /// <summary>
    /// A paused player's event thread sleeps until mpv has something to say. It waited half a
    /// second at a time, so an open player woke twice a second at rest (Reel paused on a clip:
    /// 2.0–2.2 a second, the only thread of the app's own awake). Without the timeout, Dispose
    /// still ends the thread at once: it wakes the wait.
    /// </summary>
    [MpvFact]
    public void APausedPlayersEventThread_SleepsUntilAnEvent_AndDisposeStillEndsItAtOnce()
    {
        var f = new Fixture();
        try
        {
            f.Player.Open(Clip);
            f.Until(() => f.Player.State == PlaybackState.Paused);
            Thread.Sleep(300);   // what the load still has to say

            int before = f.Backend.EventWaits;
            Thread.Sleep(2000);
            int woke = f.Backend.EventWaits - before;
            Assert.True(woke <= 1, $"the event thread woke {woke} times in 2 s at rest");
        }
        finally
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            f.Dispose();
            Assert.False(f.Backend.EventThreadAlive);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"Dispose took {clock.Elapsed.TotalMilliseconds:0} ms");
        }
    }

    /// <summary>A player opened and closed again and again leaves no thread behind: each event thread
    /// ends with its Dispose, and none of them waits out the join's timeout.</summary>
    [MpvFact]
    public void PlayersOpenedAndClosed_LeaveNoEventThreadBehind()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int threadsBefore = System.Diagnostics.Process.GetCurrentProcess().Threads.Count;
        for (int i = 0; i < 8; i++)
        {
            var backend = new MpvBackend();
            Thread.Sleep(50);   // the thread is in its wait
            backend.Dispose();
            Assert.False(backend.EventThreadAlive);
        }
        int threadsAfter = System.Diagnostics.Process.GetCurrentProcess().Threads.Count;
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), $"eight players took {clock.Elapsed.TotalMilliseconds:0} ms");
        // mpv starts several threads of its own per handle; eight leaked players would show.
        Assert.True(threadsAfter - threadsBefore < 8, $"{threadsBefore} threads before, {threadsAfter} after");
    }

    [Fact]
    public void OnlyPqAndHlg_AreHighDynamicRangeTransfers()
    {
        Assert.Equal(PictureRange.Hdr, MpvBackend.RangeOfTransfer("pq"));
        Assert.Equal(PictureRange.Hdr, MpvBackend.RangeOfTransfer("hlg"));
        foreach (var sdr in new[] { "bt.1886", "srgb", "linear", "gamma2.2", "auto" })
            Assert.Equal(PictureRange.Sdr, MpvBackend.RangeOfTransfer(sdr));
    }
}
