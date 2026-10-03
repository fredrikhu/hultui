using System.Runtime.InteropServices;
using HultUI.Core.Rendering;
using HultUI.WebView.Cef.Interop;

namespace HultUI.WebView.Cef;

/// <summary>
/// The engine's GL: one surfaceless EGL context (GL ES 3) for the process, current on CEF's UI
/// thread, where every <c>on_accelerated_paint</c> arrives. Made at the first page that asks
/// for shared textures; never destroyed (CEF itself lives until the process ends).
/// </summary>
internal static unsafe class EngineGl
{
    private static bool? _ready;
    private static string? _why;
    public static nint Display { get; private set; }
    public static nint Context { get; private set; }
    public static GlCalls? Gl { get; private set; }

    /// <summary>Why the engine cannot share frames, or null when it can. CEF's UI thread.</summary>
    public static string? Start()
    {
        if (_ready is { } ready) return ready ? null : _why;
        _why = TryStart();
        _ready = _why is null;
        if (_why is { } why) CefLog.Note("zero-copy is off: " + why);
        return _why;
    }

    private static string? TryStart()
    {
        try
        {
            var display = Egl.eglGetPlatformDisplay(Egl.EGL_PLATFORM_SURFACELESS_MESA, 0, null);
            int major, minor;
            if (display == 0 || Egl.eglInitialize(display, &major, &minor) == 0)
                return $"no surfaceless EGL display (0x{Egl.eglGetError():X})";
            var ext = Egl.Extensions(display);
            foreach (var needed in new[] { "EGL_EXT_image_dma_buf_import", "EGL_EXT_image_dma_buf_import_modifiers",
                         "EGL_MESA_image_dma_buf_export", "EGL_KHR_gl_texture_2D_image", "EGL_KHR_no_config_context" })
                if (!Egl.Has(ext, needed)) return "EGL lacks " + needed;
            Egl.eglBindAPI(Egl.EGL_OPENGL_ES_API);
            int* attribs = stackalloc int[] { Egl.EGL_CONTEXT_MAJOR_VERSION, 3, Egl.EGL_NONE };
            var context = Egl.eglCreateContext(display, 0, 0, attribs);
            if (context == 0) return $"no GL ES 3 context (0x{Egl.eglGetError():X})";
            if (Egl.eglMakeCurrent(display, 0, 0, context) == 0) return $"the context cannot be made current (0x{Egl.eglGetError():X})";
            var gl = new GlCalls(Egl.eglGetProcAddress);
            if (!gl.Complete || gl.TexStorage2D == null) return "GL ES lacks a call zero-copy makes";
            Display = display;
            Context = context;
            Gl = gl;
            return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return "no libEGL: " + ex.Message;
        }
    }

    /// <summary>Makes the engine's context current on this thread, if it is not. CEF's UI thread.</summary>
    public static void MakeCurrent()
    {
        if (Egl.eglGetCurrentContext() != Context) Egl.eglMakeCurrent(Display, 0, 0, Context);
    }
}

/// <summary>
/// Step 5.5: a page's frames on the GPU, on Wayland. CEF hands each frame over as a dmabuf in
/// <c>on_accelerated_paint</c>, valid only until the callback returns; there, on CEF's thread,
/// it is imported and copied into one of three textures the engine owns, and fenced. Each of
/// the three was exported once as a dmabuf of its own and is imported once on HultUI's side, so
/// HultUI's frame blits the newest into the sink's target without a pixel crossing the CPU, on
/// the GL renderer and, through its GL context, on Vulkan.
/// </summary>
/// <remarks>
/// <para><b>The ring.</b> CEF writes a slot that is neither the newest nor the one HultUI's
/// last frame read; HultUI reads the newest. A read is fenced, and the fence waited for before
/// the slot is given back, so a write never overtakes a read still on the GPU.</para>
/// <para><b>When it cannot.</b> On a surface that wants pixels (a raster renderer), or when
/// HultUI's context cannot import the slots, each frame is read back on CEF's thread instead:
/// into the page's pixel sink, or into one the renderer uploads from. And when the engine has
/// no GL of its own the browser is made without shared textures, and its <c>on_paint</c>
/// pixels take that same upload.</para>
/// </remarks>
internal sealed unsafe class CefFrames : IGpuFrameRenderer
{
    private const int Slots = 3;

    private readonly IWebViewHost _host;
    private readonly Action _invalidate;
    private readonly object _gate = new();

    /// <summary>Frames to upload when HultUI cannot read the slots, or CEF paints pixels.</summary>
    public PixelFrameSink Cpu { get; } = new();

    public CefFrames(IWebViewHost host, Action invalidate)
    {
        _host = host;
        _invalidate = invalidate;
    }

    private volatile GpuSinkMode _mode;
    public GpuSinkMode Mode => _mode;

    /// <summary>Whether frames must be read back: the surface wants pixels, or HultUI's
    /// context cannot import the slots.</summary>
    private volatile bool _readBack;

    /// <summary>Whether HultUI draws from <see cref="Cpu"/>: the browser paints pixels, or frames are read back.</summary>
    public bool Uploading => _paintsPixels || _readBack;
    private volatile bool _paintsPixels;

    // ──── The ring: CEF's thread writes, HultUI's reads; under _gate ────

    /// <summary>One size of the ring: the three slots, each exported once.</summary>
    private sealed class Generation(int id, int width, int height)
    {
        public readonly int Id = id, Width = width, Height = height;
        public readonly uint[] Texture = new uint[Slots], Framebuffer = new uint[Slots];
        public readonly nint[] Image = new nint[Slots];
        public readonly Egl.Dmabuf?[] Export = new Egl.Dmabuf?[Slots];
    }

    private Generation? _ring;
    private int _newest = -1, _reading = -1;
    private bool _fresh;
    private int _nextGeneration;

    // ──── CEF's side (CEF's UI thread) ────

    private uint _source, _sourceFramebuffer;      // over CEF's dmabuf, re-pointed each frame
    private uint _viewCopy, _viewCopyFramebuffer;  // the view, kept while a popup is open
    private uint _popup, _popupFramebuffer;
    private int _popupWidth, _popupHeight;
    private bool _popupShown;
    private int _popupX, _popupY;                  // device pixels

    /// <summary>The browser was made without shared textures: frames come as pixels.</summary>
    public void PaintsPixels() => _paintsPixels = true;

    /// <summary>A shared frame, inside <c>on_accelerated_paint</c>. Returns once the copy is
    /// done on the GPU, because CEF reuses the buffer when the callback returns.</summary>
    public void Accelerated(cef_paint_element_type_t type, cef_accelerated_paint_info_t* info)
    {
        if (EngineGl.Gl is not { } gl || info->plane_count < 1) return;
        EngineGl.MakeCurrent();
        int w = info->extra.coded_size.width, h = info->extra.coded_size.height;
        if (w <= 0 || h <= 0) return;
        var planes = new Egl.Plane[Math.Min(info->plane_count, 4)];
        var p = &info->planes_0;
        for (int i = 0; i < planes.Length; i++) planes[i] = new Egl.Plane(p[i].fd, (int)p[i].offset, (int)p[i].stride);
        int fourcc = info->format == cef_color_type_t.CEF_COLOR_TYPE_RGBA_8888 ? Egl.DRM_FORMAT_ABGR8888 : Egl.DRM_FORMAT_ARGB8888;
        var image = Egl.Import(EngineGl.Display, new Egl.Dmabuf(w, h, fourcc, info->modifier, planes));
        if (image == 0)
        {
            CefLog.Note($"a shared frame could not be imported (EGL 0x{Egl.eglGetError():X})");
            return;
        }
        try
        {
            if (_source == 0) { uint t, f; gl.GenTextures(1, &t); gl.GenFramebuffers(1, &f); _source = t; _sourceFramebuffer = f; }
            gl.BindTexture(GlCalls.GL_TEXTURE_2D, _source);
            gl.EGLImageTargetTexture2DOES(GlCalls.GL_TEXTURE_2D, image);
            gl.BindFramebuffer(GlCalls.GL_FRAMEBUFFER, _sourceFramebuffer);
            gl.FramebufferTexture2D(GlCalls.GL_FRAMEBUFFER, GlCalls.GL_COLOR_ATTACHMENT0, GlCalls.GL_TEXTURE_2D, _source, 0);

            if (type == cef_paint_element_type_t.PET_POPUP)
            {
                PopupFrames++;
                if (_popup == 0 || _popupWidth != w || _popupHeight != h)
                {
                    gl.Delete(ref _popup, ref _popupFramebuffer);
                    (_popup, _popupFramebuffer) = gl.Target(w, h);
                    (_popupWidth, _popupHeight) = (w, h);
                }
                gl.Blit(_sourceFramebuffer, _popupFramebuffer, 0, 0, w, h, 0, 0);
                if (_ring is { } ring && _viewCopy != 0) Compose(gl, ring.Width, ring.Height, _viewCopyFramebuffer);
            }
            else if (_popupShown)
            {
                KeepView(gl, w, h, _sourceFramebuffer);
                Compose(gl, w, h, _viewCopyFramebuffer);
            }
            else
            {
                Compose(gl, w, h, _sourceFramebuffer);
            }
        }
        finally { Egl.DestroyImage(EngineGl.Display, image); }
    }

    private void KeepView(GlCalls gl, int w, int h, uint from)
    {
        if (_viewCopy == 0 || _ring is not { } ring || ring.Width != w || ring.Height != h)
        {
            gl.Delete(ref _viewCopy, ref _viewCopyFramebuffer);
            (_viewCopy, _viewCopyFramebuffer) = gl.Target(w, h);
        }
        gl.Blit(from, _viewCopyFramebuffer, 0, 0, w, h, 0, 0);
    }

    /// <summary>The view from <paramref name="view"/>, and the popup over it when one is open,
    /// into a free slot; fenced, then published.</summary>
    private void Compose(GlCalls gl, int w, int h, uint view)
    {
        var ring = EnsureRing(gl, w, h);
        int slot;
        lock (_gate) slot = Enumerable.Range(0, Slots).First(i => i != _newest && i != _reading);
        gl.Blit(view, ring.Framebuffer[slot], 0, 0, w, h, 0, 0);
        if (_popupShown && _popup != 0)
        {
            // Clipped to the view, as the pixel path clips it.
            int x0 = Math.Max(0, _popupX), y0 = Math.Max(0, _popupY);
            int x1 = Math.Min(w, _popupX + _popupWidth), y1 = Math.Min(h, _popupY + _popupHeight);
            if (x1 > x0 && y1 > y0)
                gl.Blit(_popupFramebuffer, ring.Framebuffer[slot], x0 - _popupX, y0 - _popupY, x1 - x0, y1 - y0, x0, y0);
        }
        // CEF's buffer goes back to its pool when the callback returns, and HultUI reads the
        // slot on another context: the copy must be finished, not only queued.
        gl.Fence();
        if (_readBack) ReadBack(gl, ring, slot);
        lock (_gate)
        {
            _newest = slot;
            _fresh = true;
        }
        _host.RequestRepaint();
    }

    private void ReadBack(GlCalls gl, Generation ring, int slot)
    {
        var sink = _mode == GpuSinkMode.Pixels ? _host.Pixels : Cpu;
        using var frame = sink.BeginWrite(ring.Width, ring.Height, PixelFormat.Rgba8888);
        gl.BindFramebuffer(GlCalls.GL_READ_FRAMEBUFFER, ring.Framebuffer[slot]);
        // The slot's row 0 is the page's top row, as the sink's is; rows are packed.
        if (frame.Stride == ring.Width * 4)
            gl.ReadPixels(0, 0, ring.Width, ring.Height, GlCalls.GL_RGBA, GlCalls.GL_UNSIGNED_BYTE, (void*)frame.Address);
        else
            for (int y = 0; y < ring.Height; y++)
                gl.ReadPixels(0, y, ring.Width, 1, GlCalls.GL_RGBA, GlCalls.GL_UNSIGNED_BYTE, (byte*)frame.Address + y * frame.Stride);
        frame.Publish();
        if (sink == Cpu) _host.RequestRepaint();
    }

    /// <summary>The ring at this size: three textures, each exported as a dmabuf once.</summary>
    private Generation EnsureRing(GlCalls gl, int w, int h)
    {
        if (_ring is { } current && current.Width == w && current.Height == h) return current;
        var ring = new Generation(++_nextGeneration, w, h);
        for (int i = 0; i < Slots; i++)
        {
            (ring.Texture[i], ring.Framebuffer[i]) = gl.Target(w, h);
            ring.Image[i] = Egl.Ext.CreateImage(EngineGl.Display, EngineGl.Context, Egl.EGL_GL_TEXTURE_2D_KHR, (nint)ring.Texture[i], null);
            ring.Export[i] = Export(ring.Image[i], w, h);
            if (ring.Export[i] is null) _readBack = true;   // nothing to hand over: pixels it is
        }
        Generation? old;
        lock (_gate)
        {
            old = _ring;
            _ring = ring;
            _newest = _reading = -1;
            _fresh = false;
        }
        // HultUI imports a generation under the lock, so once it is replaced no one reads its fds.
        if (old is not null) Free(gl, old);
        return ring;
    }

    private static Egl.Dmabuf? Export(nint image, int w, int h)
    {
        if (image == 0 || Egl.Ext.ExportQuery == null || Egl.Ext.Export == null) return null;
        int fourcc, count;
        ulong modifier;
        if (Egl.Ext.ExportQuery(EngineGl.Display, image, &fourcc, &count, &modifier) == 0 || count is < 1 or > 4) return null;
        int* fds = stackalloc int[4];
        int* strides = stackalloc int[4];
        int* offsets = stackalloc int[4];
        if (Egl.Ext.Export(EngineGl.Display, image, fds, strides, offsets) == 0) return null;
        var planes = new Egl.Plane[count];
        for (int i = 0; i < count; i++) planes[i] = new Egl.Plane(fds[i], offsets[i], strides[i]);
        return new Egl.Dmabuf(w, h, fourcc, modifier, planes);
    }

    private static void Free(GlCalls gl, Generation ring)
    {
        for (int i = 0; i < Slots; i++)
        {
            if (ring.Export[i] is { } export)
                foreach (var plane in export.Planes.DistinctBy(p => p.Fd)) close(plane.Fd);
            Egl.DestroyImage(EngineGl.Display, ring.Image[i]);
            gl.Delete(ref ring.Texture[i], ref ring.Framebuffer[i]);
        }
    }

    /// <summary>The popup opened or closed (CEF's UI thread). Opening keeps the view as the
    /// newest slot has it; closing draws the view alone again.</summary>
    public void PopupShown(bool shown)
    {
        if (EngineGl.Gl is not { } gl || _ring is not { } ring) { _popupShown = shown; return; }
        EngineGl.MakeCurrent();
        int newest;
        lock (_gate) newest = _newest;
        if (shown && newest >= 0) KeepView(gl, ring.Width, ring.Height, ring.Framebuffer[newest]);
        _popupShown = shown;
        if (!shown && _viewCopy != 0)
        {
            Compose(gl, ring.Width, ring.Height, _viewCopyFramebuffer);
            gl.Delete(ref _viewCopy, ref _viewCopyFramebuffer);
            gl.Delete(ref _popup, ref _popupFramebuffer);
        }
    }

    /// <summary>Where the popup is, in device pixels (CEF's UI thread).</summary>
    public void PopupAt(int x, int y) => (_popupX, _popupY) = (x, y);

    /// <summary>The browser is gone (CEF's UI thread): give back the engine's GL objects.</summary>
    public void Close()
    {
        if (EngineGl.Gl is not { } gl) return;
        EngineGl.MakeCurrent();
        Generation? ring;
        lock (_gate)
        {
            ring = _ring;
            _ring = null;
            _newest = _reading = -1;
        }
        if (ring is not null) Free(gl, ring);
        gl.Delete(ref _source, ref _sourceFramebuffer);
        gl.Delete(ref _viewCopy, ref _viewCopyFramebuffer);
        gl.Delete(ref _popup, ref _popupFramebuffer);
        Cpu.Dispose();
    }

    [DllImport("libc")]
    private static extern int close(int fd);

    // ──── HultUI's side (the render thread, its GL context current) ────

    private GlCalls? _gl;
    private nint _display;
    private int _importedGeneration;
    private readonly nint[] _images = new nint[Slots];
    private readonly uint[] _textures = new uint[Slots], _framebuffers = new uint[Slots];
    private nint _readFence;
    private uint _upload, _uploadFramebuffer;
    private int _uploadWidth, _uploadHeight;
    private long _uploadedVersion = -1;

    public void OnModeDecided(GpuSinkMode mode)
    {
        _mode = mode;
        if (mode == GpuSinkMode.Pixels) _readBack = true;
        _invalidate();   // a still page paints no other frame: ask for one in the new mode
    }

    public void PrepareGL(Func<string, nint> getProcAddress)
    {
        _gl ??= new GlCalls(getProcAddress);
        _display = Egl.eglGetCurrentDisplay();
        if (_display == 0 || !_gl.Complete)
        {
            // HultUI's context is not EGL (GLX), or lacks a call: frames are read back.
            CefLog.Note("HultUI's GL context cannot import the page's frames; they are read back and uploaded");
            _readBack = true;
            _invalidate();
        }
    }

    public bool TryGetFrameSize(out int width, out int height)
    {
        if (Uploading)
        {
            var size = Cpu.IntrinsicSize;
            (width, height) = ((int)size.Width, (int)size.Height);
            return width > 0 && height > 0;
        }
        lock (_gate)
        {
            if (_ring is { } ring && _newest >= 0)
            {
                (width, height) = (ring.Width, ring.Height);
                return true;
            }
        }
        width = height = 0;
        return false;
    }

    /// <summary>The page's top row lands in the target's row 0, as the Windows engine's does.</summary>
    public bool RendersBottomUp => false;

    public bool Render(Func<string, nint> getProcAddress, in GpuFrameTarget target, int width, int height)
    {
        if (_gl is null) PrepareGL(getProcAddress);
        var gl = _gl!;
        bool uploading = Uploading;
        bool drew = uploading ? Upload(gl, target, width, height) : Blit(gl, target, width, height);
        var path = !drew ? null : !uploading ? "zero-copy" : _paintsPixels ? "pixels, uploaded" : "read back, uploaded";
        if (path is not null && path != _toldPath)
        {
            _toldPath = path;
            CefLog.Info($"page frames: {path}, {width}x{height}");
        }
        return drew;
    }

    private string? _toldPath;

    /// <summary>For tests: how many of the popup's frames came as shared textures.</summary>
    internal int PopupFrames { get; private set; }

    /// <summary>For tests: how the last frame drawn travelled.</summary>
    internal string? Path => _toldPath;

    private bool Blit(GlCalls gl, in GpuFrameTarget target, int width, int height)
    {
        // The read before this one is finished on the GPU before its slot can be written again.
        if (_readFence != 0)
        {
            gl.ClientWaitSync(_readFence, GlCalls.GL_SYNC_FLUSH_COMMANDS_BIT, 1_000_000_000);
            gl.DeleteSync(_readFence);
            _readFence = 0;
        }
        int slot, w, h;
        lock (_gate)
        {
            if (_ring is not { } ring || _newest < 0) { _reading = -1; return false; }
            if (ring.Id != _importedGeneration && !Import(gl, ring))
            {
                _readBack = true;
                _invalidate();
                return false;
            }
            if (!_fresh) return false;   // the target still holds the newest
            slot = _reading = _newest;
            _fresh = false;
            (w, h) = (Math.Min(ring.Width, width), Math.Min(ring.Height, height));
        }
        gl.Blit(_framebuffers[slot], target.Framebuffer, 0, 0, w, h, 0, 0);
        _readFence = gl.FenceSync(GlCalls.GL_SYNC_GPU_COMMANDS_COMPLETE, 0);
        gl.Flush();
        return true;
    }

    /// <summary>The generation's three slots, imported once (under the gate: the engine
    /// closes a generation's fds only after replacing it there).</summary>
    private bool Import(GlCalls gl, Generation ring)
    {
        ReleaseImports(gl);
        for (int i = 0; i < Slots; i++)
        {
            if (ring.Export[i] is not { } export) return false;
            _images[i] = Egl.Import(_display, export);
            if (_images[i] == 0)
            {
                CefLog.Note($"HultUI's context could not import a frame slot (EGL 0x{Egl.eglGetError():X}); frames are read back");
                ReleaseImports(gl);
                return false;
            }
            (_textures[i], _framebuffers[i]) = gl.Over(_images[i]);
        }
        _importedGeneration = ring.Id;
        return true;
    }

    private void ReleaseImports(GlCalls gl)
    {
        for (int i = 0; i < Slots; i++)
        {
            gl.Delete(ref _textures[i], ref _framebuffers[i]);
            Egl.DestroyImage(_display, _images[i]);
            _images[i] = 0;
        }
        _importedGeneration = 0;
    }

    private bool Upload(GlCalls gl, in GpuFrameTarget target, int width, int height)
    {
        if (Cpu.Version == _uploadedVersion || !Cpu.TryAcquire(out var frame)) return false;
        try
        {
            if (_upload == 0 || _uploadWidth != frame.Width || _uploadHeight != frame.Height)
            {
                gl.Delete(ref _upload, ref _uploadFramebuffer);
                (_upload, _uploadFramebuffer) = gl.Target(frame.Width, frame.Height);
                (_uploadWidth, _uploadHeight) = (frame.Width, frame.Height);
            }
            gl.BindTexture(GlCalls.GL_TEXTURE_2D, _upload);
            // Rows are the frame's stride apart; the copy is made before the call returns.
            gl.PixelStorei(GlCalls.GL_UNPACK_ROW_LENGTH, frame.Stride / 4);
            gl.TexSubImage2D(GlCalls.GL_TEXTURE_2D, 0, 0, 0, frame.Width, frame.Height,
                frame.Format == PixelFormat.Bgra8888 ? GlCalls.GL_BGRA : GlCalls.GL_RGBA, GlCalls.GL_UNSIGNED_BYTE, (void*)frame.Pixels);
            gl.PixelStorei(GlCalls.GL_UNPACK_ROW_LENGTH, 0);
            _uploadedVersion = Cpu.Version;
        }
        finally { Cpu.Release(frame); }
        gl.Blit(_uploadFramebuffer, target.Framebuffer, 0, 0, Math.Min(_uploadWidth, width), Math.Min(_uploadHeight, height), 0, 0);
        return true;
    }

    public void ReleaseGL()
    {
        if (_gl is not { } gl) return;
        if (_readFence != 0) { gl.DeleteSync(_readFence); _readFence = 0; }
        ReleaseImports(gl);
        gl.Delete(ref _upload, ref _uploadFramebuffer);
        lock (_gate) _reading = -1;
    }
}
