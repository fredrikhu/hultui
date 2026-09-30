using System.Numerics;
using HultUI.Core.Rendering;
using HultUI.WebView.WebView2.Interop;

namespace HultUI.WebView.WebView2;

/// <summary>
/// The page's frames: a <c>Windows.UI.Composition</c> container visual that WebView2 renders
/// into (its <c>RootVisualTarget</c>), captured with <c>Windows.Graphics.Capture</c> into D3D11
/// textures, and delivered one of two ways, decided by the first draw:
/// <list type="bullet">
/// <item><b>GPU</b> (a surface that runs external GL — both HultUI renderers): inside the
/// frame, the newest texture is copied on the GPU into one GL reads in place through
/// <c>WGL_NV_DX_interop2</c>, and blitted into the frame sink's framebuffer. No pixel crosses
/// the CPU. Where the interop is missing the same frame is read back and uploaded instead.</item>
/// <item><b>Pixels</b> (a raster surface): each frame is copied through a staging texture into
/// the page's <see cref="PixelFrameSink"/> on the capture's thread — phase 1.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>Spike W0 established what this relies on: a visual attached to no window at all
/// renders and captures; the page always sits at the visual's origin, whatever
/// <c>Bounds.X/Y</c> says, so nothing is cropped; frames arrive only when the page changes.
/// A frame nobody takes waits in the pool, which is how a still page's only frame survives
/// until the first draw decides where it goes.</para>
/// <para><b>Threads.</b> Created and resized on the UI thread. Frames are announced on a
/// thread-pool thread (the pool is free-threaded): in pixel mode they are copied there, in
/// GPU mode that thread only asks for a repaint and the UI thread takes the frame while it
/// draws. The D3D11 device is multithread-protected; <c>_gate</c> keeps a frame from being
/// taken twice. Nothing the capture thread does waits for the UI thread.</para>
/// </remarks>
internal sealed unsafe class VisualCapture : IDisposable
{
    [ThreadStatic] private static bool t_dispatcherQueue;

    private readonly PixelFrameSink _sink;
    private readonly Action _requestRepaint;
    private readonly object _gate = new();
    private volatile GpuSinkMode _mode;
    private nint _device, _context, _winrtDevice;
    private nint _compositor, _container, _visual;
    private nint _item, _pool, _session;
    private ComHandler? _frameHandler;
    private long _frameToken;
    private nint _staging;
    private int _stagingWidth, _stagingHeight;
    private int _width, _height;
    private bool _disposed;

    // GPU mode, UI thread with the GL context current.
    private GLInterop? _gl;
    private nint _dxDevice;                 // wglDXOpenDeviceNV
    private nint _interopTexture;           // D3D11 texture GL reads in place
    private nint _interopObject;            // wglDXRegisterObjectNV
    private uint _glTexture, _readFramebuffer;
    private int _interopWidth, _interopHeight;
    private bool _uploadInstead;            // no interop on this driver: read back and upload

    /// <param name="sink">Where frames go in pixel mode.</param>
    /// <param name="requestRepaint">Any thread: in GPU mode, a new frame is waiting.</param>
    public VisualCapture(PixelFrameSink sink, Action requestRepaint)
    {
        _sink = sink;
        _requestRepaint = requestRepaint;
        EnsureDispatcherQueue();

        // D3D11 first: the capture's frames are textures on this device.
        nint device, context;
        int level;
        Com.Check(Native.D3D11CreateDevice(0, Native.D3D_DRIVER_TYPE_HARDWARE, 0, Native.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            null, 0, Native.D3D11_SDK_VERSION, &device, &level, &context), "D3D11CreateDevice");
        _device = device;
        _context = context;
        var multithread = Com.QueryInterface(_device, ID3D11Multithread.IID);
        Com.Call(multithread, ID3D11Multithread.SetMultithreadProtected, 1);
        Com.Release(ref multithread);

        var dxgi = Com.QueryInterface(_device, IDXGIDevice.IID);
        try
        {
            nint inspectable;
            Com.Check(Native.CreateDirect3D11DeviceFromDXGIDevice(dxgi, &inspectable), "CreateDirect3D11DeviceFromDXGIDevice");
            _winrtDevice = inspectable;
        }
        finally { Com.Release(ref dxgi); }

        // The visual WebView2 renders into, attached to no window.
        var compositorInspectable = Com.ActivateInstance("Windows.UI.Composition.Compositor");
        _compositor = Com.QueryInterface(compositorInspectable, ICompositor.IID);
        Com.Release(ref compositorInspectable);
        _container = Com.GetPtr(_compositor, ICompositor.CreateContainerVisual, "CreateContainerVisual");
        _visual = Com.QueryInterface(_container, IVisual.IID);
    }

    /// <summary>What WebView2 takes as its <c>RootVisualTarget</c>: the container visual.</summary>
    public nint RootVisual => _container;

    /// <summary>The size frames are captured at, once capturing.</summary>
    public bool TryGetSize(out int width, out int height)
    {
        width = _width;
        height = _height;
        return _pool != 0 && width > 0 && height > 0;
    }

    /// <summary>Whether GL reads the frames in place (false: read back and uploaded, or not GPU yet).</summary>
    public bool ZeroCopy => _dxDevice != 0 && !_uploadInstead;

    /// <summary>
    /// How frames are delivered. Set when the first draw decides it; UI thread. Switching to
    /// pixels delivers the frame already waiting, since a still page sends no other.
    /// </summary>
    public GpuSinkMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            if (value == GpuSinkMode.Pixels) DeliverPixels();
            else if (value == GpuSinkMode.Gpu) _requestRepaint();
        }
    }

    /// <summary>
    /// Sizes the visual and the capture to the page, in device pixels. The first call
    /// starts capturing; later ones recreate the frame pool at the new size.
    /// </summary>
    public void Resize(int width, int height)
    {
        if (_disposed || width <= 0 || height <= 0 || (width == _width && height == _height)) return;
        _width = width;
        _height = height;
        Com.Check(Com.Call(_visual, IVisual.put_Size, new Vector2(width, height)), "Visual.Size");
        var size = new Native.SizeInt32 { Width = width, Height = height };

        if (_pool == 0)
        {
            Start(size);
            return;
        }
        Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, int, int, Native.SizeInt32, int>)(*(nint**)_pool)[IDirect3D11CaptureFramePool.Recreate])(
            _pool, _winrtDevice, Native.DirectXPixelFormat_B8G8R8A8UIntNormalized, 2, size), "FramePool.Recreate");
    }

    private void Start(Native.SizeInt32 size)
    {
        var itemStatics = Com.GetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem", IGraphicsCaptureItemStatics.IID);
        try
        {
            nint item;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)(*(nint**)itemStatics)[IGraphicsCaptureItemStatics.CreateFromVisual])(
                itemStatics, _visual, &item), "GraphicsCaptureItem.CreateFromVisual");
            _item = item;
        }
        finally { Com.Release(ref itemStatics); }

        var poolStatics = Com.GetActivationFactory("Windows.Graphics.Capture.Direct3D11CaptureFramePool", IDirect3D11CaptureFramePoolStatics2.IID);
        try
        {
            nint pool;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, nint, int, int, Native.SizeInt32, nint*, int>)(*(nint**)poolStatics)[IDirect3D11CaptureFramePoolStatics2.CreateFreeThreaded])(
                poolStatics, _winrtDevice, Native.DirectXPixelFormat_B8G8R8A8UIntNormalized, 2, size, &pool), "Direct3D11CaptureFramePool.CreateFreeThreaded");
            _pool = pool;
        }
        finally { Com.Release(ref poolStatics); }

        _frameHandler = ComHandler.ForEvent(FramePoolFrameArrivedHandler.IID, (_, _) => OnFrameArrived());
        _frameToken = Com.AddHandler(_pool, IDirect3D11CaptureFramePool.add_FrameArrived, _frameHandler.Pointer, "FramePool.FrameArrived");
        _session = Com.GetPtr(_pool, IDirect3D11CaptureFramePool.CreateCaptureSession, _item, "CreateCaptureSession");
        Com.Check(Com.Call(_session, IGraphicsCaptureSession.StartCapture), "StartCapture");
    }

    /// <summary>Capture thread. Pixel mode copies the frame now; otherwise it waits for the draw.</summary>
    private void OnFrameArrived()
    {
        if (_mode == GpuSinkMode.Pixels) DeliverPixels();
        else _requestRepaint();
    }

    /// <summary>Takes the newest waiting frame, if any, into the pixel sink.</summary>
    private void DeliverPixels()
    {
        lock (_gate)
        {
            if (_disposed || _pool == 0) return;
            var frame = TakeNewestFrame();
            if (frame == 0) return;
            nint texture = 0;
            try
            {
                if (FrameTexture(frame, out texture, out int w, out int h))
                    CopyToPixels(texture, w, h);
            }
            finally
            {
                Com.Release(ref texture);
                CloseFrame(ref frame);
            }
        }
    }

    /// <summary>The newest frame in the pool; older ones are closed unread. 0 when none.</summary>
    private nint TakeNewestFrame()
    {
        nint newest = 0;
        while (true)
        {
            var frame = Com.GetPtr(_pool, IDirect3D11CaptureFramePool.TryGetNextFrame, "TryGetNextFrame");
            if (frame == 0) return newest;
            CloseFrame(ref newest);
            newest = frame;
        }
    }

    /// <summary>A frame's D3D11 texture and the size of what it holds.</summary>
    private static bool FrameTexture(nint frame, out nint texture, out int width, out int height)
    {
        texture = 0;
        nint surface = 0, access = 0;
        try
        {
            var content = Com.Get<Native.SizeInt32>(frame, IDirect3D11CaptureFrame.get_ContentSize, "ContentSize");
            surface = Com.GetPtr(frame, IDirect3D11CaptureFrame.get_Surface, "Surface");
            access = Com.QueryInterface(surface, Com.IID_IDirect3DDxgiInterfaceAccess);
            Guid textureIid = ID3D11Texture2D.IID;
            nint t;
            Com.Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)access)[3])(access, &textureIid, &t), "GetInterface(ID3D11Texture2D)");
            texture = t;
            Native.D3D11_TEXTURE2D_DESC desc;
            ((delegate* unmanaged[Stdcall]<nint, Native.D3D11_TEXTURE2D_DESC*, void>)(*(nint**)texture)[ID3D11Texture2D.GetDesc])(texture, &desc);
            width = Math.Min(content.Width, (int)desc.Width);
            height = Math.Min(content.Height, (int)desc.Height);
            return width > 0 && height > 0;
        }
        finally
        {
            Com.Release(ref access);
            Com.Release(ref surface);
        }
    }

    private static void CloseFrame(ref nint frame)
    {
        if (frame == 0) return;
        // Close hands the buffer back to the pool now rather than at the last release.
        var closable = Com.TryQueryInterface(frame, IClosable.IID);
        if (closable != 0) { Com.Call(closable, IClosable.Close); Com.Release(ref closable); }
        Com.Release(ref frame);
    }

    // ──── GPU mode: UI thread, inside the frame, GL context current ────

    /// <summary>
    /// <c>HULTUI_WEBVIEW2_INTEROP=0</c> turns the in-place path off and uploads every frame —
    /// for a driver that mishandles the interop, as <c>HULTUI_VULKAN_INTEROP=0</c> is for its.
    /// </summary>
    private static readonly bool InteropEnabled = Environment.GetEnvironmentVariable("HULTUI_WEBVIEW2_INTEROP") != "0";

    /// <summary>Resolves GL and opens the interop device. At the first GPU draw.</summary>
    public void PrepareGL(Func<string, nint> resolve)
    {
        if (_gl is not null) return;
        _gl = new GLInterop(resolve);
        if (InteropEnabled && _gl.HasDxInterop) _dxDevice = _gl.DXOpenDevice((void*)_device);
        _uploadInstead = _dxDevice == 0;
    }

    /// <summary>
    /// Draws the newest frame into <paramref name="target"/>. False when no new frame is
    /// waiting — the target still holds the last one.
    /// </summary>
    public bool Render(Func<string, nint> resolve, in GpuFrameTarget target, int width, int height)
    {
        lock (_gate)
        {
            if (_disposed || _pool == 0) return false;
            PrepareGL(resolve);
            var frame = TakeNewestFrame();
            if (frame == 0) return false;
            nint texture = 0;
            try
            {
                if (!FrameTexture(frame, out texture, out int w, out int h)) return false;
                w = Math.Min(w, width);
                h = Math.Min(h, height);
                if (!_uploadInstead && EnsureInterop(width, height))
                    BlitInPlace(texture, target, w, h);
                else
                    Upload(texture, target, w, h);
                return true;
            }
            finally
            {
                Com.Release(ref texture);
                CloseFrame(ref frame);
            }
        }
    }

    /// <summary>A GPU copy into the interop texture, then a GL blit into the target. Rows keep
    /// their order: the target's row 0 is the page's top row, as mpv leaves it.</summary>
    private void BlitInPlace(nint texture, in GpuFrameTarget target, int width, int height)
    {
        var gl = _gl!;
        Com.CopySubresourceRegion(_context, _interopTexture, texture,
            new Native.D3D11_BOX { Right = (uint)width, Bottom = (uint)height, Back = 1 });
        nint obj = _interopObject;
        // The lock is also the synchronisation: GL waits here for the D3D copy above.
        if (gl.DXLockObjects(_dxDevice, 1, &obj) == 0)
        {
            _uploadInstead = true;
            Upload(texture, target, width, height);
            return;
        }
        try
        {
            gl.BindFramebuffer(GLInterop.GL_READ_FRAMEBUFFER, _readFramebuffer);
            gl.BindFramebuffer(GLInterop.GL_DRAW_FRAMEBUFFER, target.Framebuffer);
            gl.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, GLInterop.GL_COLOR_BUFFER_BIT, GLInterop.GL_NEAREST);
        }
        finally { gl.DXUnlockObjects(_dxDevice, 1, &obj); }
    }

    /// <summary>The fallback: read the frame back and upload it into the target's texture.</summary>
    private void Upload(nint texture, in GpuFrameTarget target, int width, int height)
    {
        var gl = _gl!;
        EnsureStaging(width, height);
        Com.CopySubresourceRegion(_context, _staging, texture,
            new Native.D3D11_BOX { Right = (uint)width, Bottom = (uint)height, Back = 1 });
        Com.Check(Com.Map(_context, _staging, out var mapped), "Map");
        try
        {
            gl.BindTexture(GLInterop.GL_TEXTURE_2D, target.TextureId);
            gl.PixelStorei(GLInterop.GL_UNPACK_ROW_LENGTH, (int)(mapped.RowPitch / 4));
            gl.TexSubImage2D(GLInterop.GL_TEXTURE_2D, 0, 0, 0, width, height, GLInterop.GL_BGRA, GLInterop.GL_UNSIGNED_BYTE, (void*)mapped.Data);
            gl.PixelStorei(GLInterop.GL_UNPACK_ROW_LENGTH, 0);
        }
        finally { Com.CallVoid(_context, ID3D11DeviceContext.Unmap, _staging, 0u); }
    }

    /// <summary>The interop texture at the target's size, registered with GL, with a read
    /// framebuffer over it. False when the driver refuses, which switches to uploading.</summary>
    private bool EnsureInterop(int width, int height)
    {
        if (_interopObject != 0 && _interopWidth == width && _interopHeight == height) return true;
        var gl = _gl!;
        ReleaseInterop();

        var desc = new Native.D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
            Format = Native.DXGI_FORMAT_B8G8R8A8_UNORM, SampleCount = 1,
            Usage = 0 /* DEFAULT */, BindFlags = 0x8 | 0x20 /* SHADER_RESOURCE | RENDER_TARGET */,
        };
        nint created;
        if (((delegate* unmanaged[Stdcall]<nint, Native.D3D11_TEXTURE2D_DESC*, nint, nint*, int>)(*(nint**)_device)[ID3D11Device.CreateTexture2D])(
                _device, &desc, 0, &created) < 0)
        {
            _uploadInstead = true;
            return false;
        }
        _interopTexture = created;

        uint name;
        gl.GenTextures(1, &name);
        _glTexture = name;
        _interopObject = gl.DXRegisterObject(_dxDevice, (void*)_interopTexture, _glTexture, GLInterop.GL_TEXTURE_2D, GLInterop.WGL_ACCESS_READ_ONLY_NV);
        if (_interopObject == 0)
        {
            ReleaseInterop();
            _uploadInstead = true;
            return false;
        }
        uint fbo;
        gl.GenFramebuffers(1, &fbo);
        _readFramebuffer = fbo;
        gl.BindFramebuffer(GLInterop.GL_READ_FRAMEBUFFER, _readFramebuffer);
        gl.FramebufferTexture2D(GLInterop.GL_READ_FRAMEBUFFER, GLInterop.GL_COLOR_ATTACHMENT0, GLInterop.GL_TEXTURE_2D, _glTexture, 0);
        _interopWidth = width;
        _interopHeight = height;
        return true;
    }

    private void ReleaseInterop()
    {
        if (_gl is { } gl)
        {
            if (_interopObject != 0) gl.DXUnregisterObject(_dxDevice, _interopObject);
            if (_readFramebuffer != 0) { uint f = _readFramebuffer; gl.DeleteFramebuffers(1, &f); }
            if (_glTexture != 0) { uint t = _glTexture; gl.DeleteTextures(1, &t); }
        }
        _interopObject = 0;
        _readFramebuffer = _glTexture = 0;
        Com.Release(ref _interopTexture);
        _interopWidth = _interopHeight = 0;
    }

    /// <summary>Frees the GL side. UI thread, with the context the objects were made in current.</summary>
    public void ReleaseGL()
    {
        lock (_gate)
        {
            ReleaseInterop();
            if (_gl is { } gl && _dxDevice != 0) gl.DXCloseDevice(_dxDevice);
            _dxDevice = 0;
            _gl = null;
        }
    }

    private void CopyToPixels(nint texture, int width, int height)
    {
        EnsureStaging(width, height);
        Com.CopySubresourceRegion(_context, _staging, texture,
            new Native.D3D11_BOX { Right = (uint)width, Bottom = (uint)height, Back = 1 });
        Com.Check(Com.Map(_context, _staging, out var mapped), "Map");
        try
        {
            using var writer = _sink.BeginWrite(width, height, PixelFormat.Bgra8888);
            var destination = writer.Pixels;
            int rowBytes = width * 4;
            byte* source = (byte*)mapped.Data;
            for (int row = 0; row < height; row++)
                new ReadOnlySpan<byte>(source + row * mapped.RowPitch, rowBytes)
                    .CopyTo(destination.Slice(row * writer.Stride, rowBytes));
            writer.Publish();
        }
        finally
        {
            Com.CallVoid(_context, ID3D11DeviceContext.Unmap, _staging, 0u);
        }
    }

    private void EnsureStaging(int width, int height)
    {
        if (_staging != 0 && _stagingWidth == width && _stagingHeight == height) return;
        Com.Release(ref _staging);
        var desc = new Native.D3D11_TEXTURE2D_DESC
        {
            Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
            Format = Native.DXGI_FORMAT_B8G8R8A8_UNORM, SampleCount = 1, SampleQuality = 0,
            Usage = Native.D3D11_USAGE_STAGING, BindFlags = 0, CPUAccessFlags = Native.D3D11_CPU_ACCESS_READ, MiscFlags = 0,
        };
        nint staging;
        Com.Check(((delegate* unmanaged[Stdcall]<nint, Native.D3D11_TEXTURE2D_DESC*, nint, nint*, int>)(*(nint**)_device)[ID3D11Device.CreateTexture2D])(
            _device, &desc, 0, &staging), "CreateTexture2D");
        _staging = staging;
        _stagingWidth = width;
        _stagingHeight = height;
    }

    /// <summary>
    /// A composition visual needs a DispatcherQueue on its thread; one per thread, made
    /// the first time. Pumped by the thread's ordinary message loop, which SDL runs.
    /// </summary>
    private static void EnsureDispatcherQueue()
    {
        if (t_dispatcherQueue) return;
        var options = new Native.DispatcherQueueOptions
        {
            Size = sizeof(Native.DispatcherQueueOptions),
            ThreadType = 2,       // DQTYPE_THREAD_CURRENT
            ApartmentType = 2,    // DQTAT_COM_STA
        };
        nint controller;
        // Fails if the thread already has one — someone else's is as good as ours.
        if (Native.CreateDispatcherQueueController(options, &controller) >= 0)
        {
            // Kept for the thread's life: releasing it would shut the queue down.
        }
        t_dispatcherQueue = true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Com.RemoveHandler(_pool, IDirect3D11CaptureFramePool.remove_FrameArrived, _frameToken);
        _frameHandler?.Release();
        // The GL names are ReleaseGL's, which needs their context; if it has not run by now
        // they go with that context, and the interop texture goes here with the device.
        Com.Release(ref _interopTexture);
        CloseAndRelease(ref _session);
        CloseAndRelease(ref _pool);
        Com.Release(ref _item);
        Com.Release(ref _visual);
        Com.Release(ref _container);
        Com.Release(ref _compositor);
        Com.Release(ref _staging);
        Com.Release(ref _winrtDevice);
        Com.Release(ref _context);
        Com.Release(ref _device);
    }

    private static void CloseAndRelease(ref nint obj)
    {
        var closable = Com.TryQueryInterface(obj, IClosable.IID);
        if (closable != 0) { Com.Call(closable, IClosable.Close); Com.Release(ref closable); }
        Com.Release(ref obj);
    }
}
