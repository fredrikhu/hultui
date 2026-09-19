using HultUI.Core.Builders;
using HultUI.Core.Rendering;
using HultUI.Headless;
using HultUI.Media.Testing;
using Xunit;

namespace HultUI.Media.Tests;

/// <summary>
/// The sink a GPU-capable backend is drawn through, on a surface with no GL: the
/// first draw decides the pixel mode, tells the renderer once, and the renderer's GL
/// path is never touched. The GL half is proven in HultUI.UiTests.
/// </summary>
public class GpuFrameSinkTests
{
    [Fact]
    public void ABackendWithoutARenderer_IsDrawnFromThePixelRing()
    {
        using var player = new MediaPlayer(new FakeMediaBackend());
        Assert.IsType<PixelFrameSink>(player.Frames);
        Assert.Equal(VideoSinkMode.Pixels, player.SinkMode);
    }

    [Fact]
    public void RegisteringARenderer_SwitchesTheFramesToTheGpuSink_Undecided()
    {
        var renderer = new CountingRenderer();
        using var player = new MediaPlayer(new FakeMediaBackend { GpuRenderer = renderer });

        Assert.IsType<GpuFrameSink>(player.Frames);
        Assert.Equal(VideoSinkMode.Undecided, player.SinkMode);
        Assert.Null(renderer.DecidedMode);
    }

    [Fact]
    public void OnARasterSurface_TheFirstDraw_DecidesPixels_AndNeverRendersGL()
    {
        using var session = HeadlessSession.Create(200, 200,
            options: new HeadlessOptions { RecordDrawCalls = true });
        var renderer = new CountingRenderer();
        using var player = new MediaPlayer(new FakeMediaBackend { GpuRenderer = renderer });
        session.Application.SetRoot(Box.New().Children(Video.New(player)));
        player.Open(MediaSource.FromFile("clip.mp4"));
        session.PumpFrame();

        Assert.Equal(VideoSinkMode.Pixels, player.SinkMode);
        Assert.Equal(VideoSinkMode.Pixels, renderer.DecidedMode);
        Assert.Equal(0, renderer.GLRenders);
        Assert.Contains("gpu", session.Window.RenderLog.ToString());   // the log names the source kind
        Assert.Contains("version=1", session.Window.RenderLog.ToString());
    }

    [Fact]
    public void ARepaintRequest_ReachesTheElement()
    {
        using var session = HeadlessSession.Create(200, 200);
        var backend = new FakeMediaBackend { GpuRenderer = new CountingRenderer() };
        using var player = new MediaPlayer(backend);
        int arrived = 0;
        player.FrameArrived += () => arrived++;
        session.Application.SetRoot(Box.New().Children(Video.New(player)));
        session.PumpFrame();

        var thread = new Thread(() => backend.Sink!.RequestRepaint());
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        session.PumpFrame();

        Assert.Equal(1, arrived);
    }

    /// <summary>A GPU renderer that only counts.</summary>
    private sealed class CountingRenderer : IGpuFrameRenderer
    {
        public VideoSinkMode? DecidedMode { get; private set; }
        public int GLRenders { get; private set; }

        public void OnModeDecided(VideoSinkMode mode) => DecidedMode = mode;
        public void PrepareGL(Func<string, nint> getProcAddress) { }
        public bool TryGetFrameSize(out int width, out int height) { width = 320; height = 180; return true; }
        public bool RendersBottomUp => false;
        public bool Render(Func<string, nint> getProcAddress, uint framebuffer, int width, int height) { GLRenders++; return true; }
        public void ReleaseGL() { }
    }
}
