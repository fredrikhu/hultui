using System.Numerics;
using HultUI.Core.Rendering;
using HultUI.WebView.WebView2.Interop;

namespace HultUI.WebView.WebView2;

/// <summary>
/// The page's pixels: a <c>Windows.UI.Composition</c> container visual that WebView2 renders
/// into (its <c>RootVisualTarget</c>), captured with <c>Windows.Graphics.Capture</c>, each
/// frame copied through a staging texture into the page's <see cref="PixelFrameSink"/> —
/// phase 1 of docs/design/WEBVIEW.md.
/// </summary>
/// <remarks>
/// <para>Spike W0 established what this relies on: a visual attached to no window at all
/// renders and captures; the page always sits at the visual's origin, whatever
/// <c>Bounds.X/Y</c> says, so nothing is cropped; frames arrive only when the page changes.</para>
/// <para><b>Threads.</b> Created and resized on the UI thread; frames arrive on a thread-pool
/// thread (the frame pool is free-threaded) and are copied there. The D3D11 device is
/// multithread-protected and its immediate context is used only on the frame thread after
/// creation. The frame thread publishes into the sink, whose subscriber marshals to the UI
/// thread — it never waits for the UI thread, so it never deadlocks with a frame.</para>
/// </remarks>
internal sealed unsafe class VisualCapture : IDisposable
{
    [ThreadStatic] private static bool t_dispatcherQueue;

    private readonly PixelFrameSink _sink;
    private readonly object _gate = new();
    private nint _device, _context, _winrtDevice;
    private nint _compositor, _container, _visual;
    private nint _item, _pool, _session;
    private ComHandler? _frameHandler;
    private long _frameToken;
    private nint _staging;
    private int _stagingWidth, _stagingHeight;
    private int _width, _height;
    private bool _disposed;

    public VisualCapture(PixelFrameSink sink)
    {
        _sink = sink;
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

    /// <summary>Frame thread. Copies the newest frame into the sink.</summary>
    private void OnFrameArrived()
    {
        lock (_gate)
        {
            if (_disposed || _pool == 0) return;
            nint frame = 0, surface = 0, access = 0, texture = 0;
            try
            {
                frame = Com.GetPtr(_pool, IDirect3D11CaptureFramePool.TryGetNextFrame, "TryGetNextFrame");
                if (frame == 0) return;
                var content = Com.Get<Native.SizeInt32>(frame, IDirect3D11CaptureFrame.get_ContentSize, "ContentSize");
                surface = Com.GetPtr(frame, IDirect3D11CaptureFrame.get_Surface, "Surface");
                access = Com.QueryInterface(surface, Com.IID_IDirect3DDxgiInterfaceAccess);
                Guid textureIid = ID3D11Texture2D.IID;
                nint t;
                Com.Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)access)[3])(access, &textureIid, &t), "GetInterface(ID3D11Texture2D)");
                texture = t;

                Native.D3D11_TEXTURE2D_DESC desc;
                ((delegate* unmanaged[Stdcall]<nint, Native.D3D11_TEXTURE2D_DESC*, void>)(*(nint**)texture)[ID3D11Texture2D.GetDesc])(texture, &desc);
                int w = Math.Min(content.Width, (int)desc.Width), h = Math.Min(content.Height, (int)desc.Height);
                if (w <= 0 || h <= 0) return;
                Copy(texture, w, h);
            }
            finally
            {
                Com.Release(ref texture);
                Com.Release(ref access);
                Com.Release(ref surface);
                if (frame != 0)
                {
                    // Close hands the buffer back to the pool now rather than at the last release.
                    var closable = Com.TryQueryInterface(frame, IClosable.IID);
                    if (closable != 0) { Com.Call(closable, IClosable.Close); Com.Release(ref closable); }
                    Com.Release(ref frame);
                }
            }
        }
    }

    private void Copy(nint texture, int width, int height)
    {
        EnsureStaging(width, height);
        Com.CopySubresourceRegion(_context, _staging, texture,
            new Native.D3D11_BOX { Left = 0, Top = 0, Front = 0, Right = (uint)width, Bottom = (uint)height, Back = 1 });
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
