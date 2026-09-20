using System.Runtime.InteropServices;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Headless;
using HultUI.Media;
using HultUI.Samples.ProceduralVideo;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace HultUI.Samples.ProceduralVideo.Tests;

/// <summary>
/// The algorithms are pure functions of time, and the backend built on them plays,
/// seeks and swaps algorithms like a real source would.
/// </summary>
public class ProceduralVideoTests
{
    [Theory]
    [InlineData(typeof(Plasma))]
    [InlineData(typeof(JuliaSet))]
    [InlineData(typeof(GameOfLife))]
    public void AnAlgorithm_IsAFunctionOfTime(Type algorithm)
    {
        var a = (IFrameAlgorithm)Activator.CreateInstance(algorithm)!;
        var b = (IFrameAlgorithm)Activator.CreateInstance(algorithm)!;
        const int w = 64, h = 36, stride = w * 4;
        var first = new byte[stride * h];
        var again = new byte[stride * h];
        var later = new byte[stride * h];

        a.Render(first, w, h, stride, 3.0);
        b.Render(later, w, h, stride, 7.5);
        b.Render(again, w, h, stride, 3.0);     // after a later moment: still the same frame

        Assert.Equal(first, again);
        Assert.NotEqual(first, later);
        Assert.All(Enumerable.Range(0, w * h), p => Assert.Equal(255, first[p * 4 + 3]));
    }

    [Fact]
    public void TheBackend_OpensPaused_WithAFirstFrame_AndSeeksByRecomputing()
    {
        using var session = HeadlessSession.Create(400, 300);
        var backend = new ProceduralBackend(new Plasma());
        using var player = new MediaPlayer(backend);
        session.Application.SetRoot(Box.New().Children(Video.New(player)));
        session.PumpFrame();

        player.Open(MediaSource.FromFile("x"));
        session.PumpFrame();
        Assert.Equal(PlaybackState.Paused, player.State);
        Assert.Equal(TimeSpan.FromMinutes(5), player.Duration);
        Assert.True(player.Frames.IsLoaded);
        var atZero = Snapshot(player);

        player.Seek(TimeSpan.FromSeconds(42));
        session.PumpFrame();
        Assert.Equal(TimeSpan.FromSeconds(42), player.Position);
        Assert.NotEqual(atZero, Snapshot(player));

        player.Seek(TimeSpan.Zero);
        session.PumpFrame();
        Assert.Equal(atZero, Snapshot(player));   // the same moment again: the same pixels
    }

    [Fact]
    public void Playing_PublishesFramesInRealTime()
    {
        using var session = HeadlessSession.Create(400, 300);
        var backend = new ProceduralBackend(new Plasma());
        using var player = new MediaPlayer(backend);
        session.Application.SetRoot(Box.New().Children(Video.New(player)));
        player.Open(MediaSource.FromFile("x"));
        session.PumpFrame();
        long before = player.Frames.Version;

        player.Play();
        session.PumpUntil(() => player.Frames.Version >= before + 5 && player.Position > TimeSpan.FromMilliseconds(200),
            TimeSpan.FromSeconds(10));
        player.Pause();
        session.PumpFrame();
        Assert.Equal(PlaybackState.Paused, player.State);
    }

    [Fact]
    public void SwappingTheAlgorithm_RedrawsTheCurrentMoment()
    {
        using var session = HeadlessSession.Create(400, 300);
        var backend = new ProceduralBackend(new Plasma());
        using var player = new MediaPlayer(backend);
        session.Application.SetRoot(Box.New().Children(Video.New(player)));
        player.Open(MediaSource.FromFile("x"));
        session.PumpFrame();
        var plasma = Snapshot(player);

        backend.Algorithm = new JuliaSet();
        session.PumpFrame();

        Assert.NotEqual(plasma, Snapshot(player));
        Assert.Equal(PlaybackState.Paused, player.State);
    }

    [Fact]
    public void TheFrameRate_SetsHowOftenFramesArePublished()
    {
        using var session = HeadlessSession.Create(400, 300);
        var backend = new ProceduralBackend(new Plasma());
        using var player = new MediaPlayer(backend);
        session.Application.SetRoot(Box.New().Children(Video.New(player)));
        player.Open(MediaSource.FromFile("x"));
        session.PumpFrame();

        Assert.Equal(30, backend.FrameRate);
        backend.FrameRate = 1000;
        Assert.Equal(ProceduralBackend.MaxFrameRate, backend.FrameRate);   // clamped
        backend.FrameRate = 0;
        Assert.Equal(ProceduralBackend.MinFrameRate, backend.FrameRate);

        // Generous bounds: this is a worker thread sleeping on a real clock.
        Assert.InRange(FramesInHalfASecond(session, player, backend, fps: 100), 25, 80);
        Assert.InRange(FramesInHalfASecond(session, player, backend, fps: 4), 0, 6);
    }

    private static long FramesInHalfASecond(HeadlessSession session, MediaPlayer player, ProceduralBackend backend, double fps)
    {
        backend.FrameRate = fps;
        player.Play();
        session.PumpFrame();
        long before = player.Frames.Version;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        session.PumpUntil(() => clock.ElapsedMilliseconds >= 500, TimeSpan.FromSeconds(5));
        player.Pause();
        session.PumpFrame();
        return player.Frames.Version - before;
    }

    [Fact]
    public void TheApp_HostsAPlayerWithTheAlgorithmPicker()
    {
        using var session = HeadlessSession.Create(1000, 680);
        using var app = new ProceduralVideoApp(session.Application, session.Window.Window);
        session.Application.SetRoot(app.Mount());
        session.PumpFrames(2);

        Assert.NotNull(session.Window.FindByRole(AccessibilityRole.Button, "Play"));
        Assert.NotNull(session.Window.FindByText("Plasma"));
        Assert.Equal(PlaybackState.Paused, app.Player.State);

        // The frame-rate slider drives the backend and its label.
        var slider = (SliderElement)session.Window.FindByAutomationId("frame-rate")!.Element;
        Assert.Equal(30, slider.Value);
        Assert.NotNull(session.Window.FindByText("30 fps"));
        ((IRangeValueElement)slider).SetAccessibleValue(120);
        session.PumpFrame();
        Assert.Equal(120, app.Backend.FrameRate);
        Assert.NotNull(session.Window.FindByText("120 fps"));
    }

    private static byte[] Snapshot(MediaPlayer player)
    {
        Assert.True(player.Frames.TryAcquire(out var frame));
        var bytes = new byte[frame.Stride * frame.Height];
        Marshal.Copy(frame.Pixels, bytes, 0, bytes.Length);
        player.Frames.Release(frame);
        return bytes;
    }
}
