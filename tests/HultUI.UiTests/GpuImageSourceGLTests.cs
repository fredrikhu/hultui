using System.Runtime.InteropServices;
using HultUI.Core.Builders;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// The external GL seam against a real context: a source renders into its own
/// texture through a framebuffer inside the frame, Skia composites that texture, and
/// Skia's own drawing before and after it survives the foreign GL state. Proven by
/// reading the front buffer back after the frame is presented.
/// </summary>
[Collection("ui")]
public sealed class GpuImageSourceGLTests
{
    [UiFact]
    public void ATextureRenderedInsideTheFrame_IsComposited_AndSkiaStillDrawsAroundIt()
    {
        using var guard = ForegroundGuard.Begin();
        using var app = new Application(new SdlWindowingPlatform(), "gpu", 320, 200);
        var source = new SplitClearSource();
        // A column: the image on top, a red strip under it. The strip is Skia drawing
        // after the foreign GL ran; the clear colour is Skia drawing before it.
        app.SetRoot(Box.New().Background(Color.White).Width(320f).Height(200f).Children(
            Image.New(source).Width(320f).Height(160f).ImageFit(ImageFit.Fill),
            Box.New().Width(320f).Height(40f).Background(Color.Red)));

        Assert.True(app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true));
        Assert.True(app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true));
        // At least once per frame; the framework may paint a frame more than once
        // (the first shown frame does), and that is its business.
        Assert.True(source.Renders >= 2, $"rendered {source.Renders} times");

        var gl = source.GL!;
        var viewport = new int[4];
        gl.GetIntegerv(0x0BA2 /* GL_VIEWPORT */, viewport);
        int w = viewport[2], h = viewport[3];
        Assert.True(w >= 320 && h >= 200, $"viewport {w}x{h}");
        float sx = w / 320f, sy = h / 200f;

        gl.ReadBuffer(0x0404 /* GL_FRONT */);
        // The source's texture is blue on its GL-top half and green on its GL-bottom
        // half; with a bottom-left origin the blue must land at the top of the image.
        AssertPixel(gl, (int)(160 * sx), h - 1 - (int)(40 * sy), 0, 0, 255, "top of the image (blue)");
        AssertPixel(gl, (int)(160 * sx), h - 1 - (int)(120 * sy), 0, 255, 0, "bottom of the image (green)");
        AssertPixel(gl, (int)(160 * sx), h - 1 - (int)(180 * sy), 255, 0, 0, "the strip Skia drew after the foreign GL");
    }

    private static void AssertPixel(GL gl, int x, int y, byte r, byte g, byte b, string what)
    {
        var px = new byte[4];
        gl.ReadPixels(x, y, 1, 1, 0x1908 /* GL_RGBA */, 0x1401 /* GL_UNSIGNED_BYTE */, px);
        bool ok = Math.Abs(px[0] - r) <= 8 && Math.Abs(px[1] - g) <= 8 && Math.Abs(px[2] - b) <= 8;
        Assert.True(ok, $"{what} at ({x},{y}) was ({px[0]},{px[1]},{px[2]}), expected ({r},{g},{b})");
    }

    /// <summary>Renders a two-colour texture through an FBO on every frame.</summary>
    private sealed class SplitClearSource : IGpuImageSource
    {
        private const int W = 64, H = 64;
        private uint _texture, _fbo;
        public GL? GL { get; private set; }
        public int Renders { get; private set; }

        public Size IntrinsicSize => new(W, H);
        public bool IsLoaded => true;
        public string? AlternateText => null;

        public bool TryRenderFrame(IExternalGLSurface surface, out GpuFrame frame)
        {
            GL ??= new GL(surface.GetProcAddress);
            var gl = GL;
            surface.RunExternalGL(() =>
            {
                if (_texture == 0)
                {
                    gl.GenTextures(1, out _texture);
                    gl.BindTexture(0x0DE1, _texture);
                    gl.TexImage2D(0x0DE1, 0, 0x8058, W, H, 0, 0x1908, 0x1401, IntPtr.Zero);
                    gl.TexParameteri(0x0DE1, 0x2801 /* MIN_FILTER */, 0x2601 /* LINEAR */);
                    gl.TexParameteri(0x0DE1, 0x2800 /* MAG_FILTER */, 0x2601);
                    gl.GenFramebuffers(1, out _fbo);
                    gl.BindFramebuffer(0x8D40, _fbo);
                    gl.FramebufferTexture2D(0x8D40, 0x8CE0 /* COLOR_ATTACHMENT0 */, 0x0DE1, _texture, 0);
                }
                gl.BindFramebuffer(0x8D40, _fbo);
                gl.Viewport(0, 0, W, H);
                gl.Enable(0x0C11 /* SCISSOR_TEST */);
                gl.Scissor(0, 0, W, H / 2);            // GL-bottom half
                gl.ClearColor(0, 1, 0, 1);
                gl.Clear(0x4000);
                gl.Scissor(0, H / 2, W, H / 2);        // GL-top half
                gl.ClearColor(0, 0, 1, 1);
                gl.Clear(0x4000);
                gl.Disable(0x0C11);
                // Deliberately left bound: the seam must not depend on the callee tidying up.
            });
            Renders++;
            frame = new GpuFrame(_texture, W, H, BottomLeftOrigin: true);
            return true;
        }
    }

    /// <summary>The handful of GL entry points the test needs, resolved through the surface.</summary>
    private sealed class GL
    {
        public GL(Func<string, nint> resolve)
        {
            T Load<T>(string name) where T : Delegate
            {
                var p = resolve(name);
                if (p == 0) throw new InvalidOperationException($"{name} not found");
                return Marshal.GetDelegateForFunctionPointer<T>(p);
            }
            GenTextures = Load<GenTexturesFn>("glGenTextures");
            BindTexture = Load<BindTextureFn>("glBindTexture");
            TexImage2D = Load<TexImage2DFn>("glTexImage2D");
            TexParameteri = Load<TexParameteriFn>("glTexParameteri");
            GenFramebuffers = Load<GenFramebuffersFn>("glGenFramebuffers");
            BindFramebuffer = Load<BindFramebufferFn>("glBindFramebuffer");
            FramebufferTexture2D = Load<FramebufferTexture2DFn>("glFramebufferTexture2D");
            Viewport = Load<ViewportFn>("glViewport");
            Scissor = Load<ScissorFn>("glScissor");
            Enable = Load<EnableFn>("glEnable");
            Disable = Load<EnableFn>("glDisable");
            ClearColor = Load<ClearColorFn>("glClearColor");
            Clear = Load<ClearFn>("glClear");
            ReadBuffer = Load<ReadBufferFn>("glReadBuffer");
            ReadPixels = Load<ReadPixelsFn>("glReadPixels");
            GetIntegerv = Load<GetIntegervFn>("glGetIntegerv");
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GenTexturesFn(int n, out uint textures);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void BindTextureFn(uint target, uint texture);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void TexImage2DFn(uint target, int level, int internalFormat, int width, int height, int border, uint format, uint type, IntPtr pixels);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void TexParameteriFn(uint target, uint pname, int param);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GenFramebuffersFn(int n, out uint framebuffers);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void BindFramebufferFn(uint target, uint framebuffer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void FramebufferTexture2DFn(uint target, uint attachment, uint textarget, uint texture, int level);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ViewportFn(int x, int y, int width, int height);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ScissorFn(int x, int y, int width, int height);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void EnableFn(uint cap);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ClearColorFn(float r, float g, float b, float a);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ClearFn(uint mask);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ReadBufferFn(uint mode);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ReadPixelsFn(int x, int y, int width, int height, uint format, uint type, byte[] pixels);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GetIntegervFn(uint pname, int[] data);

        public GenTexturesFn GenTextures;
        public BindTextureFn BindTexture;
        public TexImage2DFn TexImage2D;
        public TexParameteriFn TexParameteri;
        public GenFramebuffersFn GenFramebuffers;
        public BindFramebufferFn BindFramebuffer;
        public FramebufferTexture2DFn FramebufferTexture2D;
        public ViewportFn Viewport;
        public ScissorFn Scissor;
        public EnableFn Enable, Disable;
        public ClearColorFn ClearColor;
        public ClearFn Clear;
        public ReadBufferFn ReadBuffer;
        public ReadPixelsFn ReadPixels;
        public GetIntegervFn GetIntegerv;
    }
}
