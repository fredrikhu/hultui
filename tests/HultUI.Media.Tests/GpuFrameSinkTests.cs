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
        // The request posts through the thread pool (see MediaPlayer.OnRepaintRequested),
        // so the post may land a moment after the requesting thread has returned.
        session.PumpUntil(() => arrived == 1, TimeSpan.FromSeconds(10));

        Assert.Equal(1, arrived);
    }

    /// <summary>
    /// A renderer that cannot composite a plain GL texture lends one of its own, and what it
    /// lends is what the producer draws into — framebuffer, texture and internal format all
    /// the renderer's. This is the Vulkan case: phase 4 of docs/design/HDR.md.
    ///
    /// <para>The sink resolves none of its own GL here, which the fake proves by resolving
    /// every name to zero. A sink that built its texture functions anyway would throw.</para>
    /// </summary>
    [Fact]
    public void WhenTheSurfaceLendsATarget_TheProducerDrawsIntoIt_AndTheFrameSaysSo()
    {
        var renderer = new CountingRenderer();
        var sink = new GpuFrameSink(new PixelFrameSink(), renderer);
        var surface = new LendingSurface(framebuffer: 7, texture: 9, internalFormat: 0x881A);

        Assert.True(sink.TryRenderFrame(surface, out var frame));

        Assert.Equal(7u, renderer.LastFramebuffer);
        Assert.Equal(0x881Au, renderer.LastInternalFormat);
        Assert.True(frame.RendererOwned);
        Assert.Equal(9u, frame.TextureId);
        Assert.Equal(0x881Au, frame.InternalFormat);
    }

    /// <summary>
    /// The other half of the same seam: a surface that lends nothing leaves the sink to make
    /// its own RGBA8 texture, which is what every GL surface does and what this did before.
    /// </summary>
    [Fact]
    public void WhenTheSurfaceLendsNothing_TheFrameIsNotRendererOwned()
    {
        var renderer = new CountingRenderer();
        var sink = new GpuFrameSink(new PixelFrameSink(), renderer);
        var surface = new LendingSurface(framebuffer: 0, texture: 0, internalFormat: 0, lends: false);

        // The sink reaches for its own GL, which this fake cannot resolve — the failure is
        // the evidence that the two branches really are different.
        Assert.Throws<InvalidOperationException>(() => sink.TryRenderFrame(surface, out _));
    }

    /// <summary>A surface that hands out a target of its own, resolving no GL at all.</summary>
    private sealed class LendingSurface : IExternalGLSurface
    {
        private readonly uint _framebuffer, _texture, _internalFormat;
        private readonly bool _lends;

        public LendingSurface(uint framebuffer, uint texture, uint internalFormat, bool lends = true)
        {
            _framebuffer = framebuffer;
            _texture = texture;
            _internalFormat = internalFormat;
            _lends = lends;
        }

        public Func<string, nint> GetProcAddress => _ => 0;
        public void RunExternalGL(Action draw) => draw();

        public bool TryAcquireFrameTarget(int width, int height, out GpuFrameTarget target)
        {
            target = new GpuFrameTarget(_framebuffer, _texture, _internalFormat);
            return _lends;
        }

        public IRenderContext RenderContext => throw new NotSupportedException();
        public IMeasureContext MeasureContext => throw new NotSupportedException();
        public void Resize(int widthPx, int heightPx, float scaleFactor) { }
        public void Present() { }
        public void Dispose() { }
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
        public uint LastInternalFormat { get; private set; }
        public uint LastFramebuffer { get; private set; }
        public bool Render(Func<string, nint> getProcAddress, uint framebuffer, int width, int height,
            uint internalFormat)
        {
            GLRenders++;
            LastFramebuffer = framebuffer;
            LastInternalFormat = internalFormat;
            return true;
        }
        public void ReleaseGL() { }
    }
}
