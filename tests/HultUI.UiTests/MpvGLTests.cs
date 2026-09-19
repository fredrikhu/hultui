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
            Assert.Equal(VideoSinkMode.Gpu, player.SinkMode);
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
