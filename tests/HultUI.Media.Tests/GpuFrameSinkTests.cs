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
        Assert.Equal(GpuSinkMode.Pixels, player.SinkMode);
    }

    [Fact]
    public void RegisteringARenderer_SwitchesTheFramesToTheGpuSink_Undecided()
    {
        var renderer = new CountingRenderer();
        using var player = new MediaPlayer(new FakeMediaBackend { GpuRenderer = renderer });

        Assert.IsType<GpuFrameSink>(player.Frames);
        Assert.Equal(GpuSinkMode.Undecided, player.SinkMode);
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

        Assert.Equal(GpuSinkMode.Pixels, player.SinkMode);
        Assert.Equal(GpuSinkMode.Pixels, renderer.DecidedMode);
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
    /// Each sink asks for its target as itself, so a surface can give every GPU source an image
    /// of its own — on Vulkan a video and a page in one window each draw (seam W7) — and gives
    /// it back when it goes, or a window would keep every image it ever lent.
    /// </summary>
    [Fact]
    public void EachSink_AsksForItsOwnTarget_AndGivesItBackWhenDisposed()
    {
        var surface = new LendingSurface(framebuffer: 7, texture: 9, internalFormat: 0x881A);
        var video = new GpuFrameSink(new PixelFrameSink(), new CountingRenderer());
        var page = new GpuFrameSink(new PixelFrameSink(), new CountingRenderer());

        Assert.True(video.TryRenderFrame(surface, out _));
        Assert.True(page.TryRenderFrame(surface, out _));
        Assert.Equal([video, page], surface.Owners);
        Assert.Empty(surface.Released);

        page.Dispose();
        Assert.Equal([page], surface.Released);
        page.Dispose();
        Assert.Single(surface.Released);
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

    /// <summary>
    /// A renderer that can carry linear light is told so, and says what its 1.0 means — the
    /// two halves of phase 5's seam. Without the first the producer tone-maps HDR away before
    /// the renderer ever sees it; without the second the renderer cannot place the result at
    /// any particular luminance.
    /// </summary>
    [Fact]
    public void ALinearTarget_ReachesTheProducer_AndItsReferenceWhiteComesBack()
    {
        var renderer = new CountingRenderer();
        var sink = new GpuFrameSink(new PixelFrameSink(), renderer);
        var surface = new LendingSurface(framebuffer: 7, texture: 9, internalFormat: 0x881A,
            linear: true, peakNits: 1000f);

        Assert.True(sink.TryRenderFrame(surface, out var frame));

        Assert.True(renderer.LastWantedLinear);
        Assert.Equal(1000f, renderer.LastPeakNits);
        Assert.Equal(203f, frame.ReferenceWhiteNits);
        Assert.True(frame.IsLinearLight);
    }

    /// <summary>And an ordinary target still gets an ordinary encoded picture, which is what
    /// every GL surface asks for and what a frame meant before any of this.</summary>
    [Fact]
    public void AnEncodedTarget_LeavesTheFrameEncoded()
    {
        var renderer = new CountingRenderer();
        var sink = new GpuFrameSink(new PixelFrameSink(), renderer);
        var surface = new LendingSurface(framebuffer: 7, texture: 9, internalFormat: 0x8058);

        Assert.True(sink.TryRenderFrame(surface, out var frame));

        Assert.False(renderer.LastWantedLinear);
        Assert.Equal(0f, frame.ReferenceWhiteNits);
        Assert.False(frame.IsLinearLight);
    }

    /// <summary>
    /// Before the size is known there is nothing to draw at, but a producer that waits for its
    /// frame to be taken (mpv, after every load) must still be let go, or it waits out its own
    /// timeout before it can say what the size is. That costs entering GL, which on a GL surface
    /// is a flush, so the sink enters only when the renderer says a frame may be waiting.
    /// </summary>
    [Fact]
    public void WithoutASize_AWaitingFrameIsSkipped_AndGLIsEnteredOnlyForIt()
    {
        var renderer = new CountingRenderer { HasSize = false };
        var sink = new GpuFrameSink(new PixelFrameSink(), renderer);
        var surface = new LendingSurface(framebuffer: 7, texture: 9, internalFormat: 0x881A);
        Assert.False(sink.TryRenderFrame(surface, out _));   // decides the mode, prepares GL
        int entered = surface.GLEntries;

        Assert.False(sink.TryRenderFrame(surface, out _));
        Assert.Equal(entered, surface.GLEntries);
        Assert.Equal(0, renderer.Skips);

        renderer.Waiting = true;
        Assert.False(sink.TryRenderFrame(surface, out _));
        Assert.Equal(entered + 1, surface.GLEntries);
        Assert.Equal(1, renderer.Skips);
        Assert.Equal(0, renderer.GLRenders);
    }

    /// <summary>
    /// A minimized window is not drawn, and mpv, whose render thread is the UI thread, waits up
    /// to 200 ms for each frame to be taken. Undrawn, the sink takes a waiting frame without
    /// drawing it or asking for a target, and a sink nothing has drawn yet sets up as its first
    /// draw would: a decoder loads nothing until then.
    /// </summary>
    [Fact]
    public void Undrawn_ANewSinkSetsUp_AndAWaitingFrameIsTakenWithoutDrawing()
    {
        var renderer = new CountingRenderer();
        var sink = new GpuFrameSink(new PixelFrameSink(), renderer);
        var surface = new LendingSurface(framebuffer: 7, texture: 9, internalFormat: 0x881A);

        sink.ServiceUndrawn(surface);
        Assert.Equal(GpuSinkMode.Gpu, renderer.DecidedMode);
        Assert.Equal(1, renderer.Prepares);
        int entered = surface.GLEntries;

        // Nothing waiting: GL is not entered.
        sink.ServiceUndrawn(surface);
        Assert.Equal(entered, surface.GLEntries);
        Assert.Equal(0, renderer.Skips);

        renderer.Waiting = true;
        sink.ServiceUndrawn(surface);
        Assert.Equal(1, renderer.Skips);
        Assert.Equal(entered + 1, surface.GLEntries);
        Assert.Equal(1, renderer.Prepares);
        Assert.Equal(0, renderer.GLRenders);
        Assert.Empty(surface.Owners);
    }

    /// <summary>Where foreign GL cannot run, a draw would take the pixel path, and so does an
    /// undrawn sink: its renderer then delivers from a thread of its own.</summary>
    [Fact]
    public void Undrawn_WhereGLCannotRun_ANewSinkDecidesPixels()
    {
        var renderer = new CountingRenderer { Waiting = true };
        var sink = new GpuFrameSink(new PixelFrameSink(), renderer);

        sink.ServiceUndrawn(null);

        Assert.Equal(GpuSinkMode.Pixels, renderer.DecidedMode);
        Assert.Equal(0, renderer.Prepares);
        Assert.Equal(0, renderer.Skips);
    }

    /// <summary>
    /// The window half of the two above. A player opened while its window is minimized has
    /// never been drawn, and its decoder loads nothing until its sink has decided: the window
    /// serves the sink in its tree, here on a surface without GL, so it decides pixels as its
    /// first draw would. A minimized window looks for sources only while a sink is alive.
    /// </summary>
    [Fact]
    public void Undrawn_APlayerOpenedInAMinimizedWindow_IsServedBeforeItIsEverDrawn()
    {
        using var session = HeadlessSession.Create(200, 200);
        session.Window.Minimized = true;
        var renderer = new CountingRenderer();
        using var player = new MediaPlayer(new FakeMediaBackend { GpuRenderer = renderer });
        session.Application.SetRoot(Box.New().Children(Video.New(player)));
        player.Open(MediaSource.FromFile("clip.mp4"));

        Assert.False(session.PumpFrame(), "a minimized window drew a frame");
        Assert.Equal(GpuSinkMode.Pixels, player.SinkMode);
        Assert.Equal(GpuSinkMode.Pixels, renderer.DecidedMode);
    }

    /// <summary>
    /// A video frame drawn through a lent target allocates nothing in the sink, and nor does a
    /// frame with nothing to draw. The frame's GL work ran in a lambda over the method's locals:
    /// a closure and its delegate every frame, 144 bytes, made as the method was entered, so the
    /// early returns paid them too (plan 17 round 4, media-video-frame-bytes).
    /// </summary>
    [Fact]
    public void AFrame_AllocatesNothing_DrawnOrNot()
    {
        var renderer = new CountingRenderer();
        var sink = new GpuFrameSink(new PixelFrameSink(), renderer);
        var surface = new LendingSurface(framebuffer: 7, texture: 9, internalFormat: 0x881A, record: false);
        for (int i = 0; i < 100; i++) sink.TryRenderFrame(surface, out _);

        long drawn = HultUI.Testing.Allocations.Least(() =>
        {
            for (int i = 0; i < 100; i++) sink.TryRenderFrame(surface, out _);
        });
        Assert.True(renderer.GLRenders > 100, "the frames were not drawn");
        Assert.Equal(0, drawn);

        renderer.HasSize = false;
        renderer.Waiting = false;
        long sizeless = HultUI.Testing.Allocations.Least(() =>
        {
            for (int i = 0; i < 100; i++) sink.TryRenderFrame(surface, out _);
        });
        Assert.Equal(0, sizeless);
    }

    /// <summary>A surface that hands out a target of its own, resolving no GL at all.</summary>
    private sealed class LendingSurface : IExternalGLSurface
    {
        private readonly uint _framebuffer, _texture, _internalFormat;
        private readonly bool _lends, _linear, _record;
        private readonly float _peakNits;

        public LendingSurface(uint framebuffer, uint texture, uint internalFormat, bool lends = true,
            bool linear = false, float peakNits = 0f, bool record = true)
        {
            _framebuffer = framebuffer;
            _texture = texture;
            _internalFormat = internalFormat;
            _lends = lends;
            _linear = linear;
            _peakNits = peakNits;
            _record = record;
        }

        public Func<string, nint> GetProcAddress => _ => 0;
        public int GLEntries { get; private set; }
        public void RunExternalGL(Action draw)
        {
            GLEntries++;
            draw();
        }

        /// <summary>Who asked for a target, in order.</summary>
        public List<object> Owners { get; } = [];

        /// <summary>Who gave theirs back.</summary>
        public List<object> Released { get; } = [];

        public bool TryAcquireFrameTarget(object owner, int width, int height, out GpuFrameTarget target)
        {
            if (_record) Owners.Add(owner);
            target = new GpuFrameTarget(_framebuffer, _texture, _internalFormat, _linear, _peakNits);
            return _lends;
        }

        public void ReleaseFrameTarget(object owner) => Released.Add(owner);

        public IRenderContext RenderContext => throw new NotSupportedException();
        public IMeasureContext MeasureContext => throw new NotSupportedException();
        public void Resize(int widthPx, int heightPx, float scaleFactor) { }
        public void Present() { }
        public void Dispose() { }
    }

    /// <summary>A GPU renderer that only counts.</summary>
    private sealed class CountingRenderer : IGpuFrameRenderer
    {
        public GpuSinkMode? DecidedMode { get; private set; }
        public int GLRenders { get; private set; }

        public bool HasSize { get; set; } = true;
        public bool Waiting { get; set; }
        public int Skips { get; private set; }

        public void OnModeDecided(GpuSinkMode mode) => DecidedMode = mode;
        public int Prepares { get; private set; }
        public void PrepareGL(Func<string, nint> getProcAddress) => Prepares++;
        public bool TryGetFrameSize(out int width, out int height)
        {
            width = HasSize ? 320 : 0;
            height = HasSize ? 180 : 0;
            return HasSize;
        }
        public bool FrameMayBeWaiting => Waiting;
        public void SkipFrame(Func<string, nint> getProcAddress)
        {
            Skips++;
            Waiting = false;
        }
        public bool RendersBottomUp => false;
        public uint LastInternalFormat { get; private set; }
        public uint LastFramebuffer { get; private set; }
        public bool LastWantedLinear { get; private set; }
        public float LastPeakNits { get; private set; }
        public float ReferenceWhiteNits => LastWantedLinear ? 203f : 0f;
        public bool Render(Func<string, nint> getProcAddress, in GpuFrameTarget target, int width, int height)
        {
            GLRenders++;
            LastFramebuffer = target.Framebuffer;
            LastInternalFormat = target.InternalFormat;
            LastWantedLinear = target.WantsLinearLight;
            LastPeakNits = target.PeakNits;
            return true;
        }
        public void ReleaseGL() { }
    }
}
