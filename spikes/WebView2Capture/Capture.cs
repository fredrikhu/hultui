using System.Diagnostics;
using System.Runtime.InteropServices;
using HultUI.Core.Rendering;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using CompVisual = Windows.UI.Composition.Visual;

namespace WebView2Capture;

/// <summary>
/// Windows.Graphics.Capture on a composition visual, each frame copied through a
/// staging texture into a <see cref="PixelFrameSink"/> — the design's phase-1 path.
/// </summary>
internal sealed unsafe class VisualCapture : IDisposable
{
    private static readonly Guid IidDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid IidTexture2D = typeof(ID3D11Texture2D).GUID;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDirect3DDevice _winrtDevice;
    private readonly PixelFrameSink _sink;
    private readonly object _gate = new();

    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private ID3D11Texture2D? _staging;
    private int _stagingW, _stagingH;

    // The crop the frame is cut to: where the page sits inside the captured visual.
    public int CropX, CropY, CropW, CropH;

    // Statistics, written on the capture thread and read under _gate.
    public long Frames;
    public double CopyMsTotal, CopyMsMax;
    public long LastFrameTicks;
    public uint LastCenterBgra;
    public int LastContentW, LastContentH;
    public List<(long Ticks, uint Bgra)> CenterLog { get; } = new();
    public bool LogCenter;

    /// <summary>Set to receive the whole mapped frame once, on the capture thread.</summary>
    public Action<nint, int, int, int>? InspectNext;

    public VisualCapture(PixelFrameSink sink)
    {
        _sink = sink;
        FeatureLevel[] levels = { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
        D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            levels, out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        _device = device;
        _context = context;
        using (var mt = _device.QueryInterface<ID3D11Multithread>())
            mt.SetMultithreadProtected(true);

        using var dxgi = _device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(Native.CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out var inspectable));
        _winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        Marshal.Release(inspectable);
    }

    public void Start(CompVisual visual, int width, int height)
    {
        _item = GraphicsCaptureItem.CreateFromVisual(visual);
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, new SizeInt32 { Width = width, Height = height });
        _pool.FrameArrived += OnFrameArrived;
        _session = _pool.CreateCaptureSession(_item);
        _session.StartCapture();
    }

    public void Resize(int width, int height)
    {
        _pool?.Recreate(_winrtDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2,
            new SizeInt32 { Width = width, Height = height });
    }

    public SizeInt32 ItemSize => _item?.Size ?? default;

    private void OnFrameArrived(Direct3D11CaptureFramePool pool, object args)
    {
        using var frame = pool.TryGetNextFrame();
        if (frame is null) return;
        long arrived = Stopwatch.GetTimestamp();

        var content = frame.ContentSize;
        nint surface = ((IWinRTObject)frame.Surface).NativeObject.ThisPtr;
        Guid iidAccess = IidDxgiInterfaceAccess;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(surface, in iidAccess, out var access));
        nint texPtr;
        try
        {
            var getInterface = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)(*(nint**)access)[3];
            Guid iidTex = IidTexture2D;
            Marshal.ThrowExceptionForHR(getInterface(access, &iidTex, &texPtr));
        }
        finally { Marshal.Release(access); }

        using var texture = new ID3D11Texture2D(texPtr);
        var desc = texture.Description;

        int x = Math.Clamp(CropX, 0, (int)desc.Width), y = Math.Clamp(CropY, 0, (int)desc.Height);
        int w = CropW > 0 ? CropW : content.Width - x;
        int h = CropH > 0 ? CropH : content.Height - y;
        w = Math.Min(w, (int)desc.Width - x);
        h = Math.Min(h, (int)desc.Height - y);
        if (w <= 0 || h <= 0) return;

        var sw = Stopwatch.StartNew();
        EnsureStaging(w, h);
        _context.CopySubresourceRegion(_staging!, 0, 0, 0, 0, texture, 0,
            new Box(x, y, 0, x + w, y + h, 1));
        var mapped = _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        uint center;
        try
        {
            byte* src = (byte*)mapped.DataPointer;
            int pitch = (int)mapped.RowPitch;
            center = *(uint*)(src + (h / 2) * pitch + (w / 2) * 4);

            var inspect = Interlocked.Exchange(ref InspectNext, null);
            inspect?.Invoke((nint)src, w, h, pitch);

            using var writer = _sink.BeginWrite(w, h, PixelFormat.Bgra8888);
            var dst = writer.Pixels;
            int rowBytes = w * 4;
            for (int row = 0; row < h; row++)
                new ReadOnlySpan<byte>(src + row * pitch, rowBytes).CopyTo(dst.Slice(row * writer.Stride, rowBytes));
            writer.Publish();
        }
        finally { _context.Unmap(_staging!, 0); }
        double ms = sw.Elapsed.TotalMilliseconds;

        lock (_gate)
        {
            Frames++;
            CopyMsTotal += ms;
            CopyMsMax = Math.Max(CopyMsMax, ms);
            LastFrameTicks = arrived;
            LastCenterBgra = center;
            LastContentW = content.Width;
            LastContentH = content.Height;
            if (LogCenter) CenterLog.Add((arrived, center));
        }
    }

    public (long Frames, double CopyMsTotal, double CopyMsMax) TakeStats()
    {
        lock (_gate)
        {
            var r = (Frames, CopyMsTotal, CopyMsMax);
            Frames = 0; CopyMsTotal = 0; CopyMsMax = 0;
            return r;
        }
    }

    public List<(long, uint)> SnapshotCenterLog() { lock (_gate) return new(CenterLog); }
    public void ClearCenterLog() { lock (_gate) CenterLog.Clear(); }
    public (int W, int H) LastContent { get { lock (_gate) return (LastContentW, LastContentH); } }

    private void EnsureStaging(int w, int h)
    {
        if (_staging is not null && _stagingW == w && _stagingH == h) return;
        _staging?.Dispose();
        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)w, Height = (uint)h, MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read, BindFlags = BindFlags.None,
        });
        _stagingW = w; _stagingH = h;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _pool?.Dispose();
        _staging?.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
