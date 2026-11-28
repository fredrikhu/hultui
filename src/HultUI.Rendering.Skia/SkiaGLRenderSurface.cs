using HultUI.Core.Rendering;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

public sealed class SkiaGLRenderSurface : IRenderSurface, ISkiaExternalSurface, ISkiaFrameCapture
{
    private readonly GRGlInterface _glInterface;
    private readonly GRContext _grContext;
    private readonly Func<string, IntPtr> _glGetProcAddress;
    private readonly SkiaRenderContext _renderContext = new();
    private readonly SkiaTextServices _text;
    private readonly bool _ownsText;

    private GRBackendRenderTarget? _renderTarget;
    private SKSurface? _surface;
    private int _width;
    private int _height;
    private float _scaleFactor = 1f;

    public IRenderContext RenderContext => _renderContext;
    public IMeasureContext MeasureContext => _text.MeasureContext;

    /// <param name="glGetProcAddress">Resolver for GL entry points, from the windowing layer.</param>
    /// <param name="text">Text pipeline to use instead of building one. Nothing in
    /// it needs GL, so a caller that started the driver loading on another thread can
    /// have built (and measured with) it already, and the windows of an application share
    /// one — see <see cref="SkiaTextServices"/>. The caller keeps it and disposes it; the
    /// surface does not. Null builds a fresh one, which the surface disposes with itself.</param>
    public SkiaGLRenderSurface(Func<string, IntPtr> glGetProcAddress, SkiaTextServices? text = null)
    {
        // A GL ES context (Android: SDL3Window asks for ES 3.0 there) needs Skia's GLES interface,
        // which resolves the ES entry points and knows the ES feature set.
        _glInterface = (OperatingSystem.IsAndroid()
                ? GRGlInterface.CreateGles(name => glGetProcAddress(name))
                : GRGlInterface.CreateOpenGl(name => glGetProcAddress(name)))
            ?? throw new InvalidOperationException("Failed to create SkiaSharp GL interface");
        _grContext = GRContext.CreateGl(_glInterface)
            ?? throw new InvalidOperationException("Failed to create SkiaSharp GR context");

        _glGetProcAddress = glGetProcAddress;
        _ownsText = text is null;
        _text = text ?? new SkiaTextServices();
        _renderContext.SetPipeline(_text.Pipeline);
        _renderContext.SetGpuContext(_grContext);
        _renderContext.SetGLSurface(this);
    }

    /// <inheritdoc />
    public Func<string, nint> GetProcAddress => _glGetProcAddress;

    /// <inheritdoc />
    public void RunExternalGL(Action draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        // Everything Skia has recorded so far reaches the GPU before the foreign
        // code binds anything of its own …
        _surface?.Canvas.Flush();
        _grContext.Flush();
        try
        {
            draw();
        }
        finally
        {
            // … and afterwards Skia forgets every binding it believed it had, so the
            // next draw re-issues them instead of trusting a texture unit or
            // framebuffer the foreign code left pointing elsewhere.
            _grContext.ResetContext(GRGlBackendState.All);
        }
    }

    /// <summary>
    /// Wraps a texture a source rendered as an image the canvas can draw. No copy: the
    /// image reads the texture as it is when the frame flushes. The caller owns the
    /// returned image's lifetime; the texture stays the source's.
    /// </summary>
    SKImage? ISkiaExternalSurface.WrapFrame(in GpuFrame frame) => WrapTexture(frame);

    /// <summary>
    /// Lends nothing, but the render context keeps a wrapper over each source's texture, keyed by
    /// the source: a source that goes for good lets that go too, or the window keeps the source,
    /// and through it its renderer and its frame buffers, for as long as the window lives.
    /// </summary>
    void IExternalGLSurface.ReleaseFrameTarget(object owner)
    {
        if (owner is IGpuImageSource source) _renderContext.ReleaseGpuSource(source);
        if (PqOutput) _released.Enqueue(owner);
    }

    // SPIKE: lent targets given back, deleted on the render thread at the next present.
    private readonly System.Collections.Concurrent.ConcurrentQueue<object> _released = new();

    private void DeleteReleasedTargets()
    {
        while (_released.TryDequeue(out var owner))
            if (_lent.Remove(owner, out var lent) && _gl is { } gl)
            {
                ForgetLentImage(lent.Texture);
                gl.DeleteFramebuffers(1, ref lent.Framebuffer);
                gl.DeleteTextures(1, ref lent.Texture);
                _grContext.ResetContext(GRGlBackendState.All);
            }
    }

    internal SKImage? WrapTexture(in GpuFrame frame)
    {
        const uint GL_TEXTURE_2D = 0x0DE1;
        var info = new GRGlTextureInfo(GL_TEXTURE_2D, frame.TextureId, frame.InternalFormat);
        // Skia copies what it needs out of the backend texture; left to the finalizer otherwise.
        using var texture = new GRBackendTexture(frame.Width, frame.Height, mipmapped: false, info);
        // SPIKE (TV-VIDEO.md §4): a lent 10-bit target holds the producer's PQ picture, tagged as the
        // surface is so Skia copies it unconverted. The wrapper is the surface's, kept with the target:
        // the caller does not dispose a renderer-owned frame's image, and one made per frame went to
        // the finalizer, which freed Skia's GPU objects off the GL thread (a fault in the next flush).
        if (frame.InternalFormat == GlRgb10A2)
        {
            if (_lentImages.TryGetValue(frame.TextureId, out var kept) && kept.Width == frame.Width && kept.Height == frame.Height)
                return kept;
            kept?.Dispose();
            var made = SKImage.FromTexture(_grContext, texture,
                frame.BottomLeftOrigin ? GRSurfaceOrigin.BottomLeft : GRSurfaceOrigin.TopLeft,
                SKColorType.Rgba1010102, SKAlphaType.Premul, PqSpace);
            if (made is null) _lentImages.Remove(frame.TextureId);
            else _lentImages[frame.TextureId] = made;
            return made;
        }
        return SKImage.FromTexture(_grContext, texture,
            frame.BottomLeftOrigin ? GRSurfaceOrigin.BottomLeft : GRSurfaceOrigin.TopLeft,
            SKColorType.Rgba8888, SKAlphaType.Premul);
    }

    // ──── SPIKE: HDR10 output on Android (docs/design/TV-VIDEO.md §4), HULTUI_ANDROID_HDR=pq ────

    private const uint GlRgb10A2 = 0x8059;

    /// <summary>SPIKE: the window's surface is 10-bit BT.2020 PQ (SDL3Window asked EGL for it), so
    /// Skia draws into it in that space: sRGB white lands at 203 nits, BT.2408's reference white.</summary>
    internal static bool PqOutput { get; set; }

    private static readonly SKColorSpace PqSpace = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Pq, SKColorSpaceXyz.Rec2020);

    private readonly Dictionary<object, (uint Texture, uint Framebuffer, int Width, int Height)> _lent = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<uint, SKImage> _lentImages = [];
    private GLFunctions? _gl;

    private void ForgetLentImage(uint texture)
    {
        if (_lentImages.Remove(texture, out var image)) image.Dispose();
    }

    /// <summary>SPIKE: on a PQ surface each source gets a 10-bit target of its own, so a film keeps
    /// its range (an 8-bit one bands in PQ).</summary>
    bool IExternalGLSurface.TryAcquireFrameTarget(object owner, int width, int height, out GpuFrameTarget target)
    {
        target = default;
        if (!PqOutput) return false;
        _gl ??= new GLFunctions(_glGetProcAddress);
        var gl = _gl;
        if (!_lent.TryGetValue(owner, out var lent) || lent.Width != width || lent.Height != height)
        {
            if (lent.Texture == 0)
            {
                gl.GenTextures(1, out lent.Texture);
                gl.GenFramebuffers(1, out lent.Framebuffer);
            }
            else ForgetLentImage(lent.Texture);
            gl.BindTexture(GLFunctions.GL_TEXTURE_2D, lent.Texture);
            gl.TexImage2D(GLFunctions.GL_TEXTURE_2D, 0, (int)GlRgb10A2, width, height, 0,
                GLFunctions.GL_RGBA, 0x8368 /* GL_UNSIGNED_INT_2_10_10_10_REV */, IntPtr.Zero);
            gl.TexParameteri(GLFunctions.GL_TEXTURE_2D, GLFunctions.GL_TEXTURE_MIN_FILTER, GLFunctions.GL_LINEAR);
            gl.TexParameteri(GLFunctions.GL_TEXTURE_2D, GLFunctions.GL_TEXTURE_MAG_FILTER, GLFunctions.GL_LINEAR);
            gl.BindFramebuffer(GLFunctions.GL_FRAMEBUFFER, lent.Framebuffer);
            gl.FramebufferTexture2D(GLFunctions.GL_FRAMEBUFFER, GLFunctions.GL_COLOR_ATTACHMENT0,
                GLFunctions.GL_TEXTURE_2D, lent.Texture, 0);
            lent.Width = width;
            lent.Height = height;
            _lent[owner] = lent;
        }
        target = new GpuFrameTarget(lent.Framebuffer, lent.Texture, GlRgb10A2);
        return true;
    }

    public void Resize(int widthPx, int heightPx, float scaleFactor)
    {
        if (widthPx == _width && heightPx == _height && Math.Abs(scaleFactor - _scaleFactor) < 0.001f)
            return;

        _width = widthPx;
        _height = heightPx;
        _scaleFactor = scaleFactor;

        _surface?.Dispose();
        _renderTarget?.Dispose();

        // Target the default framebuffer (FBO 0)
        var fbInfo = new GRGlFramebufferInfo(0, PqOutput ? 0x881Au /* GL_RGBA16F */ : 0x8058); // GL_RGBA8
        _renderTarget = new GRBackendRenderTarget(widthPx, heightPx, 0, 8, fbInfo);
        _surface = (PqOutput
                ? SKSurface.Create(_grContext, _renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.RgbaF16, PqSpace)
                : SKSurface.Create(_grContext, _renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888))
            ?? throw new InvalidOperationException($"Failed to create SkiaSharp surface ({widthPx}x{heightPx})");

        // Tell Skia its cached view state (viewport, scissor) is stale — it
        // will re-issue glViewport on the next draw using its own loader,
        // sized from the GRBackendRenderTarget.
        _grContext.ResetContext(GRGlBackendState.View);

        _renderContext.SetCanvas(_surface.Canvas);
    }

    public void Present()
    {
        _surface?.Canvas.Flush();
        _grContext.Flush();
        if (!_released.IsEmpty) DeleteReleasedTargets();
    }

    /// <inheritdoc />
    /// <remarks>Read from the back buffer before the swap, after which its contents are undefined.
    /// The window's back buffer is its own under a compositor, so a window behind others reads
    /// whole.</remarks>
    public SKImage PresentCapturing()
    {
        try
        {
            return _surface is null
                ? throw new InvalidOperationException("Surface not initialized — call Resize first.")
                : SkiaFrameCapture.Copy(_surface, _width, _height);
        }
        finally
        {
            Present();
        }
    }

    /// <summary>
    /// The system's low-memory signal (docs/design/APP-LIFECYCLE.md): the text caches always, and
    /// with <paramref name="gpu"/> every GPU resource Skia keeps unlocked (the textures of pictures,
    /// glyph atlases' spare pages, scratch), which come back as they are next drawn. The GL context
    /// must be current for <paramref name="gpu"/>.
    /// </summary>
    internal void TrimMemory(bool gpu)
    {
        _text.TrimCaches();
        if (gpu) _grContext.PurgeUnlockedResources(scratchResourcesOnly: false);
    }

    public void Dispose()
    {
        _renderContext.Dispose();
        _surface?.Dispose();
        _renderTarget?.Dispose();
        _grContext.Dispose();
        _glInterface.Dispose();
        if (_ownsText) _text.Dispose();
    }
}
