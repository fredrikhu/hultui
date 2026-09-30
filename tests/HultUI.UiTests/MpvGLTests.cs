using HultUI.Core.Rendering;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Media;
using HultUI.Media.Mpv;
using HultUI.Media.Testing;
using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// The mpv backend on a real window: the first draw decides the GPU mode, mpv
/// creates its GL render context inside the frame, loads the clip, and renders into
/// the sink's framebuffer, which Skia composites. A two-tone clip (bright top half,
/// dark bottom) read back from the front buffer proves the orientation.
/// </summary>
[Collection("ui")]
public sealed class MpvGLTests
{
    /// <summary>Skips unless both a display and a libmpv are available.</summary>
    private sealed class MpvUiFactAttribute : FactAttribute
    {
        public MpvUiFactAttribute()
        {
            if (!UiTestGate.Enabled) Skip = UiTestGate.Reason;
            else if (!MpvBackend.IsAvailable) Skip = "libmpv not found (HULTUI_MPV_PATH).";
        }
    }

    [MpvUiFact]
    public void AClip_RendersThroughMpvsGLPath_TheRightWayUp()
    {
        string clip = TestClip.WriteTempY4m(64, 36, fps: 10, frames: 20, splitHalves: true);
        try
        {
            using var app = new Application(new SdlWindowingPlatform(), "mpv", 320, 200);
            var backend = new MpvBackend();
            using var player = new MediaPlayer(backend);
            app.SetRoot(Box.New().Background(Color.White).Width(320f).Height(200f).Children(
                Video.New(player).Width(320f).Height(180f)));
            player.Open(MediaSource.FromFile(clip));

            var deadline = Stopwatch.StartNew();
            while (player.State != PlaybackState.Paused && deadline.Elapsed < TimeSpan.FromSeconds(20))
                app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
            Assert.Equal(PlaybackState.Paused, player.State);
            Assert.Equal(GpuSinkMode.Gpu, player.SinkMode);
            Assert.Equal(new Size(64, 36), player.VideoSize);

            // A few more frames so mpv's first render has happened and been presented.
            for (int i = 0; i < 10; i++) app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);

            var gl = new GL();
            var viewport = new int[4];
            gl.GetIntegerv(0x0BA2, viewport);
            int w = viewport[2], h = viewport[3];
            float sx = w / 320f, sy = h / 200f;
            gl.ReadBuffer(0x0404 /* GL_FRONT */);
            var top = new byte[4];
            var bottom = new byte[4];
            gl.ReadPixels((int)(160 * sx), h - 1 - (int)(40 * sy), 1, 1, 0x1908, 0x1401, top);
            gl.ReadPixels((int)(160 * sx), h - 1 - (int)(140 * sy), 1, 1, 0x1908, 0x1401, bottom);
            Assert.True(top[1] > 180, $"top of the picture was ({top[0]},{top[1]},{top[2]}), expected bright");
            Assert.True(bottom[1] < 60, $"bottom of the picture was ({bottom[0]},{bottom[1]},{bottom[2]}), expected dark");
        }
        finally
        {
            File.Delete(clip);
        }
    }

    /// <summary>
    /// After every load, mpv's core waits up to 200 ms for its first frame to be rendered, and on
    /// a window the thread that renders is the UI thread. Without a size that thread could not
    /// take the frame, and the size is read from the core, which was waiting. mpv's timeout ended
    /// it, and mpv counts a frame that timed out as dropped. The file is opened after the render
    /// context exists, as a second file is, so the UI thread is free when the frame arrives.
    /// </summary>
    [MpvUiFact]
    public void Opening_AndReopening_TakesTheFirstFrameAtOnce()
    {
        string clip = TestClip.WriteTempY4m(64, 36, fps: 10, frames: 20);
        try
        {
            using var app = new Application(new SdlWindowingPlatform(), "mpv", 320, 200);
            var backend = new MpvBackend();
            using var player = new MediaPlayer(backend);
            app.SetRoot(Box.New().Background(Color.White).Width(320f).Height(200f).Children(
                Video.New(player).Width(320f).Height(180f)));
            for (int i = 0; i < 3; i++) app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);

            for (int open = 1; open <= 2; open++)
            {
                player.Open(MediaSource.FromFile(clip));
                RunUntil(app, () => player.State == PlaybackState.Paused);
                for (int i = 0; i < 5; i++) app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
                Assert.True(backend.Peek("frame-drop-count") == 0, $"open {open}: mpv timed out waiting for its first frame");
            }
        }
        finally
        {
            File.Delete(clip);
        }
    }

    /// <summary>
    /// After every seek mpv's core waits for the first frame to be rendered, by the UI thread
    /// here. A seek that waited for the core therefore waited for the UI thread itself, until
    /// mpv's 200 ms timeout. Scrubbing at the rate a mouse drag sends seeks froze the window
    /// for 200 ms on nearly every one. No frame is drawn between these seeks, so from the second
    /// on, the core is waiting whenever one is sent.
    /// </summary>
    [MpvUiFact]
    public void Seeks_NeverWaitForMpvsCore()
    {
        string clip = TestClip.WriteTempY4m(64, 36, fps: 10, frames: 20);
        try
        {
            using var app = new Application(new SdlWindowingPlatform(), "mpv", 320, 200);
            var backend = new MpvBackend();
            using var player = new MediaPlayer(backend);
            app.SetRoot(Box.New().Background(Color.White).Width(320f).Height(200f).Children(
                Video.New(player).Width(320f).Height(180f)));
            player.Open(MediaSource.FromFile(clip));
            RunUntil(app, () => player.State == PlaybackState.Paused);
            for (int i = 0; i < 5; i++) app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);

            var slowest = TimeSpan.Zero;
            for (int i = 0; i < 8; i++)
            {
                long start = Stopwatch.GetTimestamp();
                player.Seek(TimeSpan.FromSeconds(0.3 + i * 0.1));
                var took = Stopwatch.GetElapsedTime(start);
                if (took > slowest) slowest = took;
                Thread.Sleep(30);
            }
            Assert.True(slowest < TimeSpan.FromMilliseconds(100), $"the slowest seek took {slowest.TotalMilliseconds:F0} ms");

            // And the last one lands once frames are drawn again.
            RunUntil(app, () => Math.Abs(backend.Peek("time-pos") - 1.0) < 0.15);
        }
        finally
        {
            File.Delete(clip);
        }
    }

    private static void RunUntil(Application app, Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition() && deadline.Elapsed < TimeSpan.FromSeconds(20))
            app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        Assert.True(condition(), "timed out");
    }

    /// <summary>GL 1.1 entry points, which opengl32.dll exports directly; they act on the current context.</summary>
    private sealed class GL
    {
        public GL()
        {
            var opengl32 = NativeLibrary.Load("opengl32.dll");
            T Load<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(opengl32, name));
            ReadBuffer = Load<ReadBufferFn>("glReadBuffer");
            ReadPixels = Load<ReadPixelsFn>("glReadPixels");
            GetIntegerv = Load<GetIntegervFn>("glGetIntegerv");
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ReadBufferFn(uint mode);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ReadPixelsFn(int x, int y, int width, int height, uint format, uint type, byte[] pixels);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GetIntegervFn(uint pname, int[] data);

        public ReadBufferFn ReadBuffer;
        public ReadPixelsFn ReadPixels;
        public GetIntegervFn GetIntegerv;
    }
}
