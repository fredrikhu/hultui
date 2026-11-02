using System.Runtime.InteropServices;
using HultUI.Core.Primitives;

namespace HultUI.Core.Rendering;

/// <summary>
/// The ready-made <see cref="IPixelImageSource"/>: a ring of frame buffers the sink
/// owns, written by a producer through a <see cref="PixelFrameWriter"/> over a
/// <see cref="Span{T}"/>, and drawn by the renderer without a copy. An application
/// never handles a raw pointer — the sink pins its buffers and hands the renderer
/// the address.
/// </summary>
/// <remarks>
/// <para><b>Slots.</b> Three by default: one the renderer may be holding, one just
/// published and waiting to be drawn, one the producer is writing. With fewer, a
/// producer that outruns the display would have to wait for the frame on screen or
/// overwrite it. Buffers are pinned managed arrays, grown per slot when a larger
/// frame arrives and kept for reuse otherwise, so steady-state playback allocates
/// nothing.</para>
///
/// <para><b>Threads.</b> One producer thread at a time (a second concurrent
/// <see cref="BeginWrite"/> throws); the renderer borrows on the UI thread. State
/// changes take a lock — four uncontended acquisitions per frame, not a hot path —
/// while the pixel writing itself happens outside it.</para>
///
/// <para><b>Telling the UI.</b> The sink does not know which element draws it or
/// how to reach the UI thread. <see cref="FramePublished"/> is raised on the
/// producer's thread; the subscriber marshals (<c>Application.InvokeOnMainThread</c>)
/// and marks the element paint-dirty, plus layout-dirty when the size changed.</para>
///
/// <para><b>Disposal.</b> Only blocks new writes. The buffers are garbage-collected,
/// and a frame the renderer still holds keeps the sink reachable until it is
/// released, so a sink disposed mid-frame frees nothing Skia has yet to read.</para>
/// </remarks>
public sealed class PixelFrameSink : IPixelImageSource, IDisposable
{
    private sealed class Slot
    {
        public byte[] Buffer = [];
        public int Width, Height, Stride;
        public PixelFormat Format;
    }

    private readonly Slot[] _slots;
    private readonly object _gate = new();
    private int _published = -1;
    private int _borrowed = -1;
    private int _borrowCount;
    private int _writing = -1;
    private long _version;
    private bool _disposed;

    /// <param name="slots">Buffers in the ring; at least three (see the remarks).</param>
    public PixelFrameSink(int slots = 3)
    {
        if (slots < 3)
            throw new ArgumentOutOfRangeException(nameof(slots), slots,
                "A frame ring needs at least three slots: one held by the renderer, one published, one being written.");
        _slots = new Slot[slots];
        for (int i = 0; i < slots; i++) _slots[i] = new Slot();
    }

    /// <summary>Raised on the producer's thread after every publish. See the remarks on threads.</summary>
    public event Action<PixelFrameSink>? FramePublished;

    public string? AlternateText { get; set; }

    /// <summary>Size of the most recently published frame; zero before the first.</summary>
    public Size IntrinsicSize
    {
        get
        {
            lock (_gate)
            {
                if (_published < 0) return Size.Zero;
                var slot = _slots[_published];
                return new Size(slot.Width, slot.Height);
            }
        }
    }

    /// <summary>True once a frame has been published.</summary>
    public bool IsLoaded => Volatile.Read(ref _published) >= 0;

    /// <summary>Number of frames published so far; the latest frame's version.</summary>
    public long Version => Volatile.Read(ref _version);

    /// <summary>
    /// Starts a frame. The returned writer's <see cref="PixelFrameWriter.Pixels"/> spans
    /// <c>height</c> rows of <see cref="PixelFrameWriter.Stride"/> bytes; fill it, then
    /// <see cref="PixelFrameWriter.Publish"/>. Disposing the writer without publishing
    /// abandons the frame. Producer thread.
    /// </summary>
    public PixelFrameWriter BeginWrite(int width, int height, PixelFormat format)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"Frame size must be positive ({width}x{height}).");

        int index;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_writing >= 0)
                throw new InvalidOperationException("A frame is already being written; publish or dispose that writer first.");
            index = FreeSlot();
            _writing = index;
        }

        var slot = _slots[index];
        int stride = checked(width * format.BytesPerPixel());
        int length = checked(stride * height);
        if (slot.Buffer.Length < length)
            slot.Buffer = GC.AllocateUninitializedArray<byte>(length, pinned: true);
        slot.Width = width;
        slot.Height = height;
        slot.Stride = stride;
        slot.Format = format;

        return new PixelFrameWriter(this, index, new Span<byte>(slot.Buffer, 0, length),
            Marshal.UnsafeAddrOfPinnedArrayElement(slot.Buffer, 0), width, height, stride, format);
    }

    /// <summary>
    /// Copies a whole frame in and publishes it: the convenience for a producer that
    /// already has the pixels in a buffer of its own. <paramref name="stride"/> is the
    /// source's row pitch, which may exceed a row's bytes (<c>width</c> times the format's
    /// <see cref="PixelFormats.BytesPerPixel"/>).
    /// </summary>
    public void Write(int width, int height, PixelFormat format, ReadOnlySpan<byte> pixels, int stride)
    {
        int rowBytes = checked(width * format.BytesPerPixel());
        if (stride < rowBytes)
            throw new ArgumentOutOfRangeException(nameof(stride), stride, "Stride is shorter than a row.");
        if (pixels.Length < checked(stride * (height - 1) + rowBytes))
            throw new ArgumentException("Pixel buffer is shorter than the frame it describes.", nameof(pixels));

        using var writer = BeginWrite(width, height, format);
        for (int y = 0; y < height; y++)
            pixels.Slice(y * stride, rowBytes).CopyTo(writer.Pixels.Slice(y * writer.Stride, rowBytes));
        writer.Publish();
    }

    // Called under _gate.
    private int FreeSlot()
    {
        for (int i = 0; i < _slots.Length; i++)
            if (i != _published && i != _borrowed)
                return i;
        // Unreachable with three or more slots: only two are ever excluded.
        throw new InvalidOperationException("No free frame slot.");
    }

    internal void Publish(int index)
    {
        lock (_gate)
        {
            if (_writing != index) return;  // abandoned or already published
            _writing = -1;
            _published = index;
            _version++;
        }
        FramePublished?.Invoke(this);
    }

    internal void Abandon(int index)
    {
        lock (_gate)
        {
            if (_writing == index) _writing = -1;
        }
    }

    /// <summary>
    /// Borrows the latest published frame. While a frame is borrowed, a second borrow
    /// (two elements drawing one sink in the same frame) returns that same frame, so
    /// one render pass never mixes two versions and the ring never has two slots
    /// held at once.
    /// </summary>
    public bool TryAcquire(out PixelFrame frame)
    {
        lock (_gate)
        {
            int index = _borrowed >= 0 ? _borrowed : _published;
            if (index < 0)
            {
                frame = default;
                return false;
            }
            var slot = _slots[index];
            _borrowed = index;
            _borrowCount++;
            frame = new PixelFrame(
                Marshal.UnsafeAddrOfPinnedArrayElement(slot.Buffer, 0),
                slot.Width, slot.Height, slot.Stride, slot.Format, index);
            return true;
        }
    }

    public void Release(in PixelFrame frame)
    {
        lock (_gate)
        {
            if (_borrowed != frame.Slot || _borrowCount == 0) return;
            if (--_borrowCount == 0) _borrowed = -1;
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
    }
}

/// <summary>
/// One frame being written into a <see cref="PixelFrameSink"/>. Fill
/// <see cref="Pixels"/> row by row (<see cref="Stride"/> bytes apart), then
/// <see cref="Publish"/>; disposing without publishing abandons the frame and frees
/// the slot. A <c>ref struct</c>, so it cannot outlive the method that began it.
/// </summary>
public ref struct PixelFrameWriter
{
    private readonly PixelFrameSink _sink;
    private readonly int _slot;
    private bool _done;

    internal PixelFrameWriter(PixelFrameSink sink, int slot, Span<byte> pixels, nint address,
        int width, int height, int stride, PixelFormat format)
    {
        _sink = sink;
        _slot = slot;
        Pixels = pixels;
        Address = address;
        Width = width;
        Height = height;
        Stride = stride;
        Format = format;
    }

    /// <summary>The frame's bytes: <see cref="Height"/> rows of <see cref="Stride"/> bytes.</summary>
    public Span<byte> Pixels { get; }

    /// <summary>
    /// The same bytes as an address, for a native decoder that writes through a
    /// pointer. Pinned for the writer's lifetime; managed producers use
    /// <see cref="Pixels"/> and never need this.
    /// </summary>
    public nint Address { get; }
    public int Width { get; }
    public int Height { get; }
    /// <summary>Bytes from one row to the next: <c>Width</c> times the format's
    /// <see cref="PixelFormats.BytesPerPixel"/> for this sink.</summary>
    public int Stride { get; }
    public PixelFormat Format { get; }

    /// <summary>Makes this frame the one the renderer draws next. Once only.</summary>
    public void Publish()
    {
        if (_done) throw new InvalidOperationException("This frame was already published or abandoned.");
        _done = true;
        _sink.Publish(_slot);
    }

    /// <summary>Abandons the frame if it was not published.</summary>
    public void Dispose()
    {
        if (_done) return;
        _done = true;
        _sink.Abandon(_slot);
    }
}
