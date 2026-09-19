using System.Runtime.InteropServices;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Media;
using HultUI.Media.Testing;
using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// <see cref="GpuFrameSink"/> against a real GL context: the first draw decides the
/// GPU mode, the sink builds a texture and framebuffer of the renderer's size, the
/// renderer's clear lands in it, and Skia composites the result — read back from
/// the front buffer. This is the path the mpv backend takes on a window, exercised
/// without mpv.
/// </summary>
[Collection("ui")]
public sealed class GpuFrameSinkGLTests
{
    [UiFact]
    public void TheFirstDraw_DecidesGpu_AndTheRenderersFramebufferIsComposited()
    {
        using var app = new Application(new SdlWindowingPlatform(), "gpu-sink", 320, 200);
        var backend = new ClearingRenderer();
        using var player = new MediaPlayer(new FakeMediaBackend { GpuRenderer = backend });
        app.SetRoot(Box.New().Background(Color.White).Width(320f).Height(200f).Children(
            Video.New(player).Width(320f).Height(200f)));
        player.Open(MediaSource.FromFile("clip.mp4"));   // the fake reports 320x180 and publishes a pixel frame

        Assert.True(app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true));
        Assert.True(app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true));

        Assert.Equal(VideoSinkMode.Gpu, player.SinkMode);
        Assert.Equal(VideoSinkMode.Gpu, backend.DecidedMode);
        Assert.Equal(1, backend.Prepared);
        Assert.True(backend.Renders >= 2, $"rendered {backend.Renders} times");
        Assert.Equal((320, 180), backend.LastSize);

        var gl = backend.GL!;
        var viewport = new int[4];
        gl.GetIntegerv(0x0BA2, viewport);
        int w = viewport[2], h = viewport[3];
        float sx = w / 320f, sy = h / 200f;
        gl.ReadBuffer(0x0404 /* GL_FRONT */);
        var px = new byte[4];
        // Contain-fit 16:9 in 320x200 leaves 10 px bars; the middle is the picture.
        gl.ReadPixels((int)(160 * sx), h - 1 - (int)(100 * sy), 1, 1, 0x1908, 0x1401, px);
        Assert.True(px[0] < 8 && Math.Abs(px[1] - 128) <= 8 && px[2] > 247,
            $"picture pixel was ({px[0]},{px[1]},{px[2]}), expected (0,128,255)");
    }

    /// <summary>A renderer that clears the sink's framebuffer to one colour.</summary>
    private sealed class ClearingRenderer : IGpuFrameRenderer
    {
        public VideoSinkMode? DecidedMode { get; private set; }
        public int Renders { get; private set; }
        public (int, int) LastSize { get; private set; }
        public GL? GL { get; private set; }

        public void OnModeDecided(VideoSinkMode mode) => DecidedMode = mode;
        public void PrepareGL(Func<string, nint> getProcAddress) => Prepared++;
        public int Prepared { get; private set; }
        public bool TryGetFrameSize(out int width, out int height) { width = 320; height = 180; return true; }
        public bool RendersBottomUp => true;

        public bool Render(Func<string, nint> getProcAddress, uint framebuffer, int width, int height)
        {
            GL ??= new GL(getProcAddress);
            GL.BindFramebuffer(0x8D40, framebuffer);
            GL.Viewport(0, 0, width, height);
            GL.ClearColor(0, 0.5f, 1, 1);
            GL.Clear(0x4000);
            Renders++;
            LastSize = (width, height);
            return true;
        }

        public void ReleaseGL() { }
    }

    /// <summary>GL entry points the test needs, resolved through the surface.</summary>
    public sealed class GL
    {
        public GL(Func<string, nint> resolve)
        {
            T Load<T>(string name) where T : Delegate
            {
                var p = resolve(name);
                if (p == 0) throw new InvalidOperationException($"{name} not found");
                return Marshal.GetDelegateForFunctionPointer<T>(p);
            }
            BindFramebuffer = Load<BindFramebufferFn>("glBindFramebuffer");
            Viewport = Load<ViewportFn>("glViewport");
            ClearColor = Load<ClearColorFn>("glClearColor");
            Clear = Load<ClearFn>("glClear");
            ReadBuffer = Load<ReadBufferFn>("glReadBuffer");
            ReadPixels = Load<ReadPixelsFn>("glReadPixels");
            GetIntegerv = Load<GetIntegervFn>("glGetIntegerv");
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void BindFramebufferFn(uint target, uint framebuffer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ViewportFn(int x, int y, int width, int height);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ClearColorFn(float r, float g, float b, float a);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ClearFn(uint mask);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ReadBufferFn(uint mode);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ReadPixelsFn(int x, int y, int width, int height, uint format, uint type, byte[] pixels);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GetIntegervFn(uint pname, int[] data);

        public BindFramebufferFn BindFramebuffer;
        public ViewportFn Viewport;
        public ClearColorFn ClearColor;
        public ClearFn Clear;
        public ReadBufferFn ReadBuffer;
        public ReadPixelsFn ReadPixels;
        public GetIntegervFn GetIntegerv;
    }
}
