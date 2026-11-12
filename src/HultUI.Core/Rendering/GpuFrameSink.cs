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
    private readonly bool _asks;

    public GpuFrameSink(PixelFrameSink pixels, IGpuFrameRenderer renderer)
        : this(pixels, renderer, asksForLiveFrames: false)
    {
    }

    /// <summary>A sink whose owner also asks for its frames inside Win32's size/move loop
    /// (<see cref="LiveFrameRequests"/>), as HultUI.Media's player and the web view do, rather than
    /// signalling them only by a post; see <see cref="MaySignalOnlyByPosting"/>.</summary>
    internal GpuFrameSink(PixelFrameSink pixels, IGpuFrameRenderer renderer, bool asksForLiveFrames)
    {
        _pixels = pixels;
        _renderer = renderer;
        _renderInFrame = RenderInFrame;
        _prepareInFrame = PrepareInFrame;
        _skipInFrame = SkipInFrame;
        _releaseInFrame = ReleaseInFrame;
        _asks = asksForLiveFrames;
        Interlocked.Increment(ref s_live);
        if (_asks) Interlocked.Increment(ref s_asking);
    }

    // In the whole process: sinks made and not yet disposed, those of them whose owner asks for
    // its frames, and image elements holding a GPU source of another kind, which have no Dispose
    // to count by.
    private static int s_live, s_asking, s_otherKind;

    /// <summary>
    /// Whether any image can hold a GPU source whose new frames are signalled only by a post to the
    /// UI thread: a sink whose owner does not ask for its frames (<see cref="LiveFrameRequests"/>),
    /// or a GPU source of another kind. Inside Win32's size/move loop such a post waits for the loop
    /// to end, and a source that keeps one post in flight posts no more until it runs, so the post
    /// that waits is all a live frame can go on: while this is true it draws for waiting posts on
    /// every event, as before; while it is false only for a post made since its last frame
    /// (Application.RenderLiveResizeFrame). As with <see cref="MayNeedUndrawnService"/>, a sink
    /// nobody disposes, or an element dropped still holding a source, keeps it true.
    /// </summary>
    internal static bool MaySignalOnlyByPosting =>
        Volatile.Read(ref s_live) > Volatile.Read(ref s_asking) || Volatile.Read(ref s_otherKind) > 0;

    /// <summary>
    /// Whether any image can hold a GPU source to serve while its window is not drawn
    /// (<see cref="IGpuImageSource.ServiceUndrawn"/>): a sink is alive, or an image element
    /// holds a GPU source of another kind. A minimized window that is dirty looks for them
    /// through its whole tree on every pass of the loop, and asks this first. A sink nobody
    /// disposes, or an element dropped still holding a source, keeps it true, which costs that
    /// walk and nothing else.
    /// </summary>
    internal static bool MayNeedUndrawnService =>
        Volatile.Read(ref s_live) > 0 || Volatile.Read(ref s_otherKind) > 0;

    /// <summary>An image element's source went from <paramref name="from"/> to
    /// <paramref name="to"/>; see <see cref="MayNeedUndrawnService"/>.</summary>
    internal static void ImageSourceChanged(IImageSource? from, IImageSource? to)
    {
        if (from is IGpuImageSource and not GpuFrameSink) Interlocked.Decrement(ref s_otherKind);
        if (to is IGpuImageSource and not GpuFrameSink) Interlocked.Increment(ref s_otherKind);
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

    /// <summary>The surface of the frame being drawn or served, and the one the target it lent
    /// is given back to when the sink goes.</summary>
    private IExternalGLSurface? _surface;

    // The frame's GL work runs through three delegates made once, with its inputs and results in
    // the fields below, so a video frame allocates nothing here. Lambdas over the method's locals
    // cost a closure and a delegate every frame (144 bytes), made as the method was entered, so a
    // frame with nothing to draw paid for them too.
    private readonly Action _renderInFrame, _prepareInFrame, _skipInFrame, _releaseInFrame;
    private int _frameWidth, _frameHeight;
    private bool _frameDrew, _frameRendererOwned;
    private uint _frameFramebuffer, _frameTexture, _frameFormat;

    public bool TryRenderFrame(IExternalGLSurface surface, out GpuFrame frame)
    {
        frame = default;
        // Drawn, so on screen: whatever it was to give back while hidden, it keeps.
        if (_releaseWanted) KeepWhileShown();
        if (_disposed) return false;
        // Remembered so the target the surface lent this sink can be given back when it goes.
        _surface = surface;
        // A surface whose swapchain follows its pictures hears what this one is on every draw,
        // before there is anything to draw: a video still loading says it does not know yet.
        if (surface is IPictureRangeSurface pictures)
            pictures.PictureDrawn(_renderer is IPictureRangeSource source ? source.PictureRange : PictureRange.Sdr);
        if (_mode == GpuSinkMode.Undecided)
        {
            Decide(GpuSinkMode.Gpu);
            // The renderer sets up inside the frame, before there is anything to draw:
            // that is what lets a decoder create its GL context ahead of loading.
            surface.RunExternalGL(_prepareInFrame);
        }
        if (!_renderer.TryGetFrameSize(out int width, out int height) || width <= 0 || height <= 0)
        {
            // Nothing to draw at, but possibly a frame to hand over: a producer that waits for
            // it would otherwise wait out its own timeout before it can tell anyone the size.
            if (_renderer.FrameMayBeWaiting)
                surface.RunExternalGL(_skipInFrame);
            return false;
        }

        _frameWidth = width;
        _frameHeight = height;
        _frameDrew = _frameRendererOwned = false;
        _frameFramebuffer = _frameTexture = 0;
        _frameFormat = GpuFrame.Rgba8;
        surface.RunExternalGL(_renderInFrame);

        // The target was given back while the picture was hidden and made again just now: it
        // holds nothing until the renderer draws into it.
        if (_pictureLost)
        {
            if (!_frameDrew) return false;
            _pictureLost = false;
        }

        // A renderer with nothing new keeps the last frame: the target still holds it.
        if (!_frameDrew && !_frameRendererOwned && _texture == 0) return false;
        if (_frameFramebuffer == 0 && _frameTexture == 0) return false;

        frame = new GpuFrame(_frameTexture, width, height, _renderer.RendersBottomUp, _frameFormat,
            _frameRendererOwned, _renderer.ReferenceWhiteNits);
        return true;
    }

    /// <summary>One frame's GL work, inside <see cref="IExternalGLSurface.RunExternalGL"/>: the
    /// target, then the renderer's draw into it.</summary>
    private void RenderInFrame()
    {
        var surface = _surface!;
        int width = _frameWidth, height = _frameHeight;
        GpuFrameTarget target;

        // The renderer first: one that cannot composite a plain GL texture lends its own
        // target, and rendering into that is what keeps the frame on the GPU.
        //
        // The sink's own GL entry points are resolved only on the branch that uses them.
        // A renderer that lends a target needs none of them — and on a Vulkan surface
        // there is no guarantee the nine functions a texture needs are even reachable
        // through its resolver.
        if (surface.TryAcquireFrameTarget(this, width, height, out var lent))
        {
            _frameRendererOwned = true;
            target = lent;
            _frameFramebuffer = lent.Framebuffer;
            _frameTexture = lent.TextureId;
            _frameFormat = lent.InternalFormat;
            ReleaseTarget();
        }
        else
        {
            _gl ??= new GLFunctions(surface.GetProcAddress);
            EnsureTarget(width, height);
            _frameFramebuffer = _framebuffer;
            _frameTexture = _texture;
            // The sink's own texture is an ordinary 8-bit encoded one, and saying so
            // explicitly keeps the renderer from being handed a default that claims more.
            target = new GpuFrameTarget(_framebuffer, _texture, GpuFrame.Rgba8);
        }

        _frameDrew = _renderer.Render(surface.GetProcAddress, target, width, height);
    }

    private void PrepareInFrame() => _renderer.PrepareGL(_surface!.GetProcAddress);

    private void SkipInFrame() => _renderer.SkipFrame(_surface!.GetProcAddress);

    /// <summary>
    /// What a draw would do, less the drawing, for a window that is not being drawn: decides
    /// the mode and lets the renderer set up (a decoder loads nothing until it has), then takes
    /// a waiting frame without drawing it. The first draw after the window comes back draws
    /// the frame the renderer is on.
    /// </summary>
    public void ServiceUndrawn(IExternalGLSurface? surface)
    {
        if (_disposed) return;
        if (surface is null)
        {
            Decide(GpuSinkMode.Pixels);
            return;
        }
        _surface = surface;
        if (_mode == GpuSinkMode.Undecided)
        {
            Decide(GpuSinkMode.Gpu);
            surface.RunExternalGL(_prepareInFrame);
        }
        if (_mode == GpuSinkMode.Gpu && _renderer.FrameMayBeWaiting)
            surface.RunExternalGL(_skipInFrame);
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

    // ──── Off screen ────

    // Sinks waiting for a frame of their window to give their GL objects back in, for every
    // window of the process: each frame takes those of its own surface. The count is read by
    // every frame without the lock.
    private static readonly List<GpuFrameSink> s_toRelease = [];
    private static int s_toReleaseCount;

    /// <summary>In <see cref="s_toRelease"/>: asked to give its GL objects back, not drawn since.</summary>
    private bool _releaseWanted;

    /// <summary>The target the surface lent was given back: the next is a new one, which holds no
    /// picture until the renderer draws one into it.</summary>
    private bool _pictureLost;

    /// <summary>Whether any sink waits for a frame to give its GL objects back in
    /// (<see cref="ReleaseWhileHidden"/>). Every frame of every window asks.</summary>
    internal static bool AnyToRelease => Volatile.Read(ref s_toReleaseCount) > 0;

    /// <summary>Whether this sink waits to give its GL objects back. For tests.</summary>
    internal bool IsReleaseWanted => _releaseWanted;

    /// <summary>Whether this sink gave back the target it was lent and has not drawn a picture
    /// since. For tests.</summary>
    internal bool IsReleased => _pictureLost;

    /// <summary>
    /// The owner's picture left the screen. In the next frame its window draws, with that
    /// window's GL context current, the renderer frees its GL objects
    /// (<see cref="IGpuFrameRenderer.ReleaseGL"/>) and the surface the target it lent
    /// (<see cref="IExternalGLSurface.ReleaseFrameTarget"/>: on Vulkan an image of its own). A draw
    /// before then keeps them all. The first draw after makes them again. A lent target comes back
    /// new and empty, and the sink shows nothing until the renderer has drawn a frame into it; the
    /// sink's own texture, on a GL surface, is kept with the last picture in it (ReleaseInFrame).
    /// <para>Only for a renderer that makes its GL objects again when it is asked to render after
    /// <see cref="IGpuFrameRenderer.ReleaseGL"/>, and that delivers a frame once it is back on
    /// screen even for a still picture: the web view asks it for WebView2, which sends the page's
    /// frame when the page is shown. UI thread.</para>
    /// </summary>
    internal void ReleaseWhileHidden()
    {
        if (_disposed || _releaseWanted || _pictureLost || _mode != GpuSinkMode.Gpu || _surface is null) return;
        _releaseWanted = true;
        lock (s_toRelease)
        {
            s_toRelease.Add(this);
            Volatile.Write(ref s_toReleaseCount, s_toRelease.Count);
        }
    }

    private void KeepWhileShown()
    {
        _releaseWanted = false;
        lock (s_toRelease)
        {
            s_toRelease.Remove(this);
            Volatile.Write(ref s_toReleaseCount, s_toRelease.Count);
        }
    }

    /// <summary>
    /// The sinks that last drew on <paramref name="surface"/> and wait to give their GL objects
    /// back (<see cref="ReleaseWhileHidden"/>) give them back now. The window calls it inside its
    /// frame, after drawing, where its context is current: a sink drawn in this frame has already
    /// said it keeps them.
    /// </summary>
    internal static void ReleaseHidden(IExternalGLSurface surface)
    {
        // Made only when a sink of this surface is due, so a frame with none allocates nothing.
        List<GpuFrameSink>? due = null;
        lock (s_toRelease)
        {
            for (int i = s_toRelease.Count - 1; i >= 0; i--)
            {
                var sink = s_toRelease[i];
                if (!ReferenceEquals(sink._surface, surface)) continue;
                sink._releaseWanted = false;
                s_toRelease.RemoveAt(i);
                (due ??= []).Add(sink);
            }
            Volatile.Write(ref s_toReleaseCount, s_toRelease.Count);
        }
        if (due is null) return;
        // Outside the lock: the renderers' GL work is theirs.
        foreach (var sink in due)
        {
            surface.RunExternalGL(sink._releaseInFrame);
            // The surface's own image for it: on Vulkan an allocation of its own, made again
            // when the sink next asks.
            if (sink._pictureLost) surface.ReleaseFrameTarget(sink);
        }
    }

    private void ReleaseInFrame()
    {
        if (_disposed) return;
        _renderer.ReleaseGL();
        // A target the surface lent goes back (ReleaseHidden). The sink's own texture, on a GL
        // surface, stays, with the last picture in it to show until a new frame comes: deleting it
        // freed nothing of this process's GPU memory (plan 17 round 9, NVIDIA, a page of 2160×779
        // px: hidden 72.2 MB either way).
        _pictureLost = _texture == 0;
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
        if (_releaseWanted) KeepWhileShown();
        Interlocked.Decrement(ref s_live);
        if (_asks) Interlocked.Decrement(ref s_asking);
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
