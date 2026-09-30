using System.Runtime.InteropServices;
using HultUI.Core.Primitives;

namespace HultUI.Core.Rendering;

/// <summary>
/// The frame source for a backend with an <see cref="IGpuFrameRenderer"/>. On a surface
/// that runs external GL it asks the renderer to draw on every frame, into a target the
/// surface lends (the Vulkan case) or into a texture and framebuffer of its own (plain GL),
/// and hands the result to the surface to composite; on any other surface it is the
/// backend's pixel ring. Which of the two applies is decided by
/// whichever the surface asks for first, and told to the renderer once.
/// </summary>
public sealed class GpuFrameSink : IGpuImageSource, IPixelImageSource, IDisposable
{
    private readonly PixelFrameSink _pixels;
    private readonly IGpuFrameRenderer _renderer;
    private GLFunctions? _gl;
    private uint _texture, _framebuffer;
    private int _width, _height;
    private GpuSinkMode _mode;
    private bool _disposed;

    public GpuFrameSink(PixelFrameSink pixels, IGpuFrameRenderer renderer)
    {
        _pixels = pixels;
        _renderer = renderer;
    }

    public GpuSinkMode Mode => _mode;

    // IImageSource: in GPU mode the size is the renderer's; otherwise the ring's.
    public Size IntrinsicSize => _mode == GpuSinkMode.Gpu && _renderer.TryGetFrameSize(out int w, out int h)
        ? new Size(w, h)
        : _pixels.IntrinsicSize;

    public bool IsLoaded => _mode == GpuSinkMode.Gpu
        ? _renderer.TryGetFrameSize(out _, out _)
        : _pixels.IsLoaded;

    public string? AlternateText
    {
        get => _pixels.AlternateText;
        set => _pixels.AlternateText = value;
    }

    // ──── GPU path ────

    private IExternalGLSurface? _surface;

    public bool TryRenderFrame(IExternalGLSurface surface, out GpuFrame frame)
    {
        frame = default;
        if (_disposed) return false;
        // Remembered so the target the surface lent this sink can be given back when it goes.
        _surface = surface;
        if (_mode == GpuSinkMode.Undecided)
        {
            Decide(GpuSinkMode.Gpu);
            // The renderer sets up inside the frame, before there is anything to draw:
            // that is what lets a decoder create its GL context ahead of loading.
            surface.RunExternalGL(() => _renderer.PrepareGL(surface.GetProcAddress));
        }
        if (!_renderer.TryGetFrameSize(out int width, out int height) || width <= 0 || height <= 0)
        {
            // Nothing to draw at, but possibly a frame to hand over: a producer that waits for
            // it would otherwise wait out its own timeout before it can tell anyone the size.
            if (_renderer.FrameMayBeWaiting)
                surface.RunExternalGL(() => _renderer.SkipFrame(surface.GetProcAddress));
            return false;
        }

        bool drew = false;
        uint framebuffer = 0, texture = 0, format = GpuFrame.Rgba8;
        bool rendererOwned = false;
        GpuFrameTarget lentTarget = default;

        surface.RunExternalGL(() =>
        {
            // The renderer first: one that cannot composite a plain GL texture lends its own
            // target, and rendering into that is what keeps the frame on the GPU.
            //
            // The sink's own GL entry points are resolved only on the branch that uses them.
            // A renderer that lends a target needs none of them — and on a Vulkan surface
            // there is no guarantee the nine functions a texture needs are even reachable
            // through its resolver.
            if (surface.TryAcquireFrameTarget(this, width, height, out var lent))
            {
                rendererOwned = true;
                lentTarget = lent;
                framebuffer = lent.Framebuffer;
                texture = lent.TextureId;
                format = lent.InternalFormat;
                ReleaseTarget();
            }
            else
            {
                _gl ??= new GLFunctions(surface.GetProcAddress);
                EnsureTarget(width, height);
                framebuffer = _framebuffer;
                texture = _texture;
                // The sink's own texture is an ordinary 8-bit encoded one, and saying so
                // explicitly keeps the renderer from being handed a default that claims more.
                lentTarget = new GpuFrameTarget(framebuffer, texture, GpuFrame.Rgba8);
            }

            drew = _renderer.Render(surface.GetProcAddress, lentTarget, width, height);
        });

        // A renderer with nothing new keeps the last frame: the target still holds it.
        if (!drew && !rendererOwned && _texture == 0) return false;
        if (framebuffer == 0 && texture == 0) return false;

        frame = new GpuFrame(texture, width, height, _renderer.RendersBottomUp, format, rendererOwned,
            _renderer.ReferenceWhiteNits);
        return true;
    }

    /// <summary>Drops the sink's own texture and framebuffer, for when the renderer started
    /// lending one — a surface can begin doing so at any time, and keeping both would leak
    /// the one nobody draws.</summary>
    private void ReleaseTarget()
    {
        if (_texture == 0 || _gl is not { } gl) return;
        gl.DeleteFramebuffers(1, ref _framebuffer);
        gl.DeleteTextures(1, ref _texture);
        _texture = _framebuffer = 0;
        _width = _height = 0;
    }

    private void EnsureTarget(int width, int height)
    {
        var gl = _gl!;
        if (_texture != 0 && _width == width && _height == height) return;
        if (_texture == 0)
        {
            gl.GenTextures(1, out _texture);
            gl.GenFramebuffers(1, out _framebuffer);
        }
        gl.BindTexture(GLFunctions.GL_TEXTURE_2D, _texture);
        gl.TexImage2D(GLFunctions.GL_TEXTURE_2D, 0, (int)GpuFrame.Rgba8, width, height, 0,
            GLFunctions.GL_RGBA, GLFunctions.GL_UNSIGNED_BYTE, IntPtr.Zero);
        gl.TexParameteri(GLFunctions.GL_TEXTURE_2D, GLFunctions.GL_TEXTURE_MIN_FILTER, GLFunctions.GL_LINEAR);
        gl.TexParameteri(GLFunctions.GL_TEXTURE_2D, GLFunctions.GL_TEXTURE_MAG_FILTER, GLFunctions.GL_LINEAR);
        gl.BindFramebuffer(GLFunctions.GL_FRAMEBUFFER, _framebuffer);
        gl.FramebufferTexture2D(GLFunctions.GL_FRAMEBUFFER, GLFunctions.GL_COLOR_ATTACHMENT0,
            GLFunctions.GL_TEXTURE_2D, _texture, 0);
        _width = width;
        _height = height;
    }

    // ──── Pixel path ────

    public long Version => _pixels.Version;

    public bool TryAcquire(out PixelFrame frame)
    {
        Decide(GpuSinkMode.Pixels);
        return _pixels.TryAcquire(out frame);
    }

    public void Release(in PixelFrame frame) => _pixels.Release(frame);

    private void Decide(GpuSinkMode mode)
    {
        if (_mode != GpuSinkMode.Undecided) return;
        _mode = mode;
        _renderer.OnModeDecided(mode);
    }

    /// <summary>
    /// Frees the renderer's GL objects and then the sink's. Must run with the GL
    /// context current when the mode is <see cref="GpuSinkMode.Gpu"/> — on the UI
    /// thread, where the window's context lives between frames.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_mode == GpuSinkMode.Gpu) _renderer.ReleaseGL();
        // The surface's image for this sink: on Vulkan an allocation of its own, which would
        // otherwise live as long as the window.
        _surface?.ReleaseFrameTarget(this);
        _surface = null;
        if (_gl is { } gl && _texture != 0)
        {
            gl.DeleteFramebuffers(1, ref _framebuffer);
            gl.DeleteTextures(1, ref _texture);
            _texture = _framebuffer = 0;
        }
    }
}

/// <summary>The few GL entry points the sink needs, resolved through the surface.</summary>
internal sealed class GLFunctions
{
    public const uint GL_TEXTURE_2D = 0x0DE1, GL_RGBA = 0x1908, GL_UNSIGNED_BYTE = 0x1401,
        GL_TEXTURE_MIN_FILTER = 0x2801, GL_TEXTURE_MAG_FILTER = 0x2800, GL_FRAMEBUFFER = 0x8D40,
        GL_COLOR_ATTACHMENT0 = 0x8CE0;
    public const int GL_LINEAR = 0x2601;

    public GLFunctions(Func<string, nint> resolve)
    {
        T Load<T>(string name) where T : Delegate
        {
            var p = resolve(name);
            if (p == 0) throw new InvalidOperationException($"GL entry point {name} not found.");
            return Marshal.GetDelegateForFunctionPointer<T>(p);
        }
        GenTextures = Load<GenFn>("glGenTextures");
        DeleteTextures = Load<DeleteFn>("glDeleteTextures");
        BindTexture = Load<BindFn>("glBindTexture");
        TexImage2D = Load<TexImage2DFn>("glTexImage2D");
        TexParameteri = Load<TexParameteriFn>("glTexParameteri");
        GenFramebuffers = Load<GenFn>("glGenFramebuffers");
        DeleteFramebuffers = Load<DeleteFn>("glDeleteFramebuffers");
        BindFramebuffer = Load<BindFn>("glBindFramebuffer");
        FramebufferTexture2D = Load<FramebufferTexture2DFn>("glFramebufferTexture2D");
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void GenFn(int n, out uint names);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DeleteFn(int n, ref uint names);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void BindFn(uint target, uint name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void TexImage2DFn(uint target, int level, int internalFormat, int width, int height, int border, uint format, uint type, IntPtr pixels);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void TexParameteriFn(uint target, uint pname, int param);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void FramebufferTexture2DFn(uint target, uint attachment, uint textarget, uint texture, int level);

    public readonly GenFn GenTextures, GenFramebuffers;
    public readonly DeleteFn DeleteTextures, DeleteFramebuffers;
    public readonly BindFn BindTexture, BindFramebuffer;
    public readonly TexImage2DFn TexImage2D;
    public readonly TexParameteriFn TexParameteri;
    public readonly FramebufferTexture2DFn FramebufferTexture2D;
}
