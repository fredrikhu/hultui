using HultUI.Core.Rendering;
using SkiaSharp;

namespace HultUI.Rendering.Skia;

public sealed class SkiaGLRenderSurface : IRenderSurface, ISkiaExternalSurface, ISkiaFrameCapture, IPictureRangeSurface
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
    /// returned image's lifetime; the texture stays the source's. Except a target this surface
    /// lent (<see cref="IExternalGLSurface.TryAcquireFrameTarget"/>), whose image is the surface's.
    /// </summary>
    SKImage? ISkiaExternalSurface.WrapFrame(in GpuFrame frame) =>
        frame.RendererOwned ? WrapLent(frame) : WrapTexture(frame);

    /// <summary>
    /// The render context keeps a wrapper over each source's texture, keyed by the source: a source
    /// that goes for good lets that go too, or the window keeps the source, and through it its
    /// renderer and its frame buffers, for as long as the window lives. And the target this surface
    /// lent it, if it did, goes at the next present.
    /// </summary>
    void IExternalGLSurface.ReleaseFrameTarget(object owner)
    {
        if (owner is IGpuImageSource source) _renderContext.ReleaseGpuSource(source);
        if (HdrOutput is not null) _released.Enqueue(owner);
    }

    internal SKImage? WrapTexture(in GpuFrame frame)
    {
        const uint GL_TEXTURE_2D = 0x0DE1;
        var info = new GRGlTextureInfo(GL_TEXTURE_2D, frame.TextureId, frame.InternalFormat);
        // Skia copies what it needs out of the backend texture; left to the finalizer otherwise.
        using var texture = new GRBackendTexture(frame.Width, frame.Height, mipmapped: false, info);
        return SKImage.FromTexture(_grContext, texture,
            frame.BottomLeftOrigin ? GRSurfaceOrigin.BottomLeft : GRSurfaceOrigin.TopLeft,
            SKColorType.Rgba8888, SKAlphaType.Premul);
    }

    public void Resize(int widthPx, int heightPx, float scaleFactor)
    {
        if (widthPx == _width && heightPx == _height && Math.Abs(scaleFactor - _scaleFactor) < 0.001f)
            return;

        _width = widthPx;
        _height = heightPx;
        _scaleFactor = scaleFactor;
        MakeSurface();
    }

    /// <summary>Skia's surface over the window's framebuffer (FBO 0), in the colour space the
    /// frame is drawn in.</summary>
    private void MakeSurface()
    {
        _surface?.Dispose();
        _renderTarget?.Dispose();

        // Half float on an Android TV that takes HDR10 (see HdrOutput), 8-bit everywhere else.
        bool halfFloat = HdrOutput is not null;
        var fbInfo = new GRGlFramebufferInfo(0, halfFloat ? GlRgba16f : GlRgba8);
        _renderTarget = new GRBackendRenderTarget(_width, _height, 0, 8, fbInfo);
        _surface = (halfFloat
                ? SKSurface.Create(_grContext, _renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.RgbaF16,
                    _pq ? PqSpace : SKColorSpace.CreateSrgb())
                : SKSurface.Create(_grContext, _renderTarget, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888))
            ?? throw new InvalidOperationException($"Failed to create SkiaSharp surface ({_width}x{_height})");

        // Tell Skia its cached view state (viewport, scissor) is stale — it
        // will re-issue glViewport on the next draw using its own loader,
        // sized from the GRBackendRenderTarget.
        _grContext.ResetContext(GRGlBackendState.View);

        _renderContext.SetCanvas(_surface.Canvas);
    }

    bool IRenderSurface.BeginFrame()
    {
        _pictures.BeginFrame();
        _frameMetadata = null;
        return true;
    }

    public void Present()
    {
        _surface?.Canvas.Flush();
        _grContext.Flush();
        if (!_released.IsEmpty) DeleteReleasedTargets();
        if (HdrOutput is { } output) FollowPictures(output);
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
        foreach (var image in _lentImages.Values) image.Dispose();
        _lentImages.Clear();
        _renderContext.Dispose();
        _surface?.Dispose();
        _renderTarget?.Dispose();
        _grContext.Dispose();
        _glInterface.Dispose();
        if (_ownsText) _text.Dispose();
    }

    // ──── HDR10 on an Android TV (docs/design/TV-VIDEO.md §4) ────

    private const uint GlRgba8 = 0x8058, GlRgba16f = 0x881A, GlRgb10A2 = 0x8059;

    /// <summary>BT.2100 PQ: BT.2020 primaries, the PQ transfer. Skia puts linear 1.0, so sRGB white,
    /// at 203 nits in it, BT.2408's reference white.</summary>
    private static readonly SKColorSpace PqSpace =
        SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Pq, SKColorSpaceXyz.Rec2020);

    /// <summary>
    /// The window's half of HDR10 output, set by the platform before the first frame where the
    /// window's framebuffer is half float and its display takes HDR10 (an Android TV's: a Tegra X1
    /// has no 10-bit surface). Null everywhere else, and the surface draws 8-bit as ever.
    ///
    /// <para>With it the surface follows the pictures on screen as a Vulkan one does
    /// (<see cref="HdrPictureFollower"/>): the frame is drawn in sRGB until an HDR video is shown,
    /// then in PQ, with the video in a 10-bit target of its own in PQ, untouched; and in sRGB again
    /// once it goes. The window tags its buffers to match from the next present, and the display
    /// switches with them.</para>
    /// </summary>
    internal IGlHdrOutput? HdrOutput { get; set; }

    private readonly HdrPictureFollower _pictures = new();
    private HdrMetadata? _frameMetadata, _shownMetadata;
    private bool _pq, _frameOwed;

    void IPictureRangeSurface.PictureDrawn(PictureRange range) => _pictures.Drawn(range);

    void IPictureRangeSurface.HdrMetadataDrawn(HdrMetadata metadata) => _frameMetadata = metadata;

    bool IPictureRangeSurface.TakeOwedFrame()
    {
        bool owed = _frameOwed;
        _frameOwed = false;
        return owed;
    }

    /// <summary>
    /// After a frame: an HDR picture came on screen or left it, so the next frame is drawn in the
    /// other space, the window tags its buffers to match, and that frame is owed; or the metadata of
    /// the picture shown changed. A frame whose video still loads keeps what was shown.
    /// </summary>
    private void FollowPictures(IGlHdrOutput output)
    {
        bool changed = _pictures.EndFrame(SkiaVulkanRenderSurface.HdrVideoEnabled && output.CanShowPq);
        var metadata = _pictures.Shown ? _frameMetadata ?? _shownMetadata : null;
        if (!changed && metadata == _shownMetadata) return;
        _shownMetadata = metadata;
        output.SetPq(_pictures.Shown, metadata);
        if (!changed) return;

        _pq = _pictures.Shown;
        if (!_pq) ReleaseLentTargets();
        MakeSurface();
        _frameOwed = true;
    }

    private readonly Dictionary<object, LentTarget> _lent = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<uint, SKImage> _lentImages = [];
    private readonly System.Collections.Concurrent.ConcurrentQueue<object> _released = new();
    private GLFunctions? _gl;

    private struct LentTarget
    {
        public uint Texture, Framebuffer;
        public int Width, Height;
    }

    /// <summary>
    /// While the frame is drawn in PQ, each GPU source draws into a 10-bit target of its own, told
    /// to write PQ (<see cref="GpuFrameTarget.Pq"/>): an 8-bit one bands in PQ. Otherwise nothing is
    /// lent, and the source draws into its own 8-bit texture as on any GL surface.
    /// </summary>
    bool IExternalGLSurface.TryAcquireFrameTarget(object owner, int width, int height, out GpuFrameTarget target)
    {
        target = default;
        if (!_pq) return false;
        var gl = _gl ??= new GLFunctions(_glGetProcAddress);
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
                GLFunctions.GL_RGBA, GlUnsignedInt2101010Rev, IntPtr.Zero);
            gl.TexParameteri(GLFunctions.GL_TEXTURE_2D, GLFunctions.GL_TEXTURE_MIN_FILTER, GLFunctions.GL_LINEAR);
            gl.TexParameteri(GLFunctions.GL_TEXTURE_2D, GLFunctions.GL_TEXTURE_MAG_FILTER, GLFunctions.GL_LINEAR);
            gl.BindFramebuffer(GLFunctions.GL_FRAMEBUFFER, lent.Framebuffer);
            gl.FramebufferTexture2D(GLFunctions.GL_FRAMEBUFFER, GLFunctions.GL_COLOR_ATTACHMENT0,
                GLFunctions.GL_TEXTURE_2D, lent.Texture, 0);
            lent.Width = width;
            lent.Height = height;
            _lent[owner] = lent;
        }
        target = new GpuFrameTarget(lent.Framebuffer, lent.Texture, GlRgb10A2) { Pq = true };
        return true;
    }

    private const uint GlUnsignedInt2101010Rev = 0x8368;

    /// <summary>
    /// A lent target's image, tagged PQ as the frame is, so Skia copies it unconverted. Kept with the
    /// target, not made per frame: the caller does not dispose a renderer-owned frame's image, and
    /// one made each frame went to the finalizer, which freed Skia's GPU objects off the GL thread.
    /// Skia then faulted in its next flush, and the runtime's signal handler returned into the fault:
    /// the UI thread spun in the kernel with the picture frozen (about 55 s into a 4K HDR10 film).
    /// </summary>
    private SKImage? WrapLent(in GpuFrame frame)
    {
        if (_lentImages.TryGetValue(frame.TextureId, out var kept) && kept.Width == frame.Width && kept.Height == frame.Height)
            return kept;
        kept?.Dispose();
        const uint GL_TEXTURE_2D = 0x0DE1;
        var info = new GRGlTextureInfo(GL_TEXTURE_2D, frame.TextureId, frame.InternalFormat);
        using var texture = new GRBackendTexture(frame.Width, frame.Height, mipmapped: false, info);
        var made = SKImage.FromTexture(_grContext, texture,
            frame.BottomLeftOrigin ? GRSurfaceOrigin.BottomLeft : GRSurfaceOrigin.TopLeft,
            SKColorType.Rgba1010102, SKAlphaType.Premul, PqSpace);
        if (made is null) _lentImages.Remove(frame.TextureId);
        else _lentImages[frame.TextureId] = made;
        return made;
    }

    private void ForgetLentImage(uint texture)
    {
        if (_lentImages.Remove(texture, out var image)) image.Dispose();
    }

    /// <summary>Targets their sources gave back (any thread), deleted on the render thread.</summary>
    private void DeleteReleasedTargets()
    {
        while (_released.TryDequeue(out var owner))
            if (_lent.Remove(owner, out var lent)) DeleteTarget(lent);
    }

    /// <summary>The frame left PQ: every lent target goes, and its source draws into its own
    /// again.</summary>
    private void ReleaseLentTargets()
    {
        foreach (var lent in _lent.Values) DeleteTarget(lent);
        _lent.Clear();
    }

    private void DeleteTarget(LentTarget lent)
    {
        if (_gl is not { } gl) return;
        ForgetLentImage(lent.Texture);
        gl.DeleteFramebuffers(1, ref lent.Framebuffer);
        gl.DeleteTextures(1, ref lent.Texture);
        _grContext.ResetContext(GRGlBackendState.All);
    }
}

/// <summary>
/// The window's half of HDR10 output on GL (docs/design/TV-VIDEO.md §4): a half-float framebuffer
/// whose buffers it tags, for the compositor and the display, as SDR or as BT.2100 PQ.
/// </summary>
internal interface IGlHdrOutput
{
    /// <summary>Whether the window can tag its buffers PQ. Until it can, the frame stays sRGB and an
    /// HDR video is tone-mapped as on any SDR surface.</summary>
    bool CanShowPq { get; }

    /// <summary>
    /// From the next frame presented, the window's buffers carry BT.2100 PQ with this metadata (null:
    /// none stated, so the display assumes its own), or, with <paramref name="pq"/> false, sRGB.
    /// Render thread, after the frame drawn in the old space was flushed and before it is swapped.
    /// </summary>
    void SetPq(bool pq, HdrMetadata? metadata);
}
