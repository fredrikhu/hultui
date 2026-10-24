using HultUI.Core.Rendering;
using HultUI.Rendering.Skia;
using SkiaSharp;

namespace VesperCommander;

/// <summary>Off-thread thumbnail decode + bounded LRU cache for the tile grid. Tiles call
/// <see cref="Get"/> during Build; a miss enqueues a background decode and returns
/// <see cref="Status.Pending"/>, and when the decode lands the supplied repaint callback
/// marshals a rebuild so the tile swaps the thumbnail in. Decoded bitmaps are downscaled to
/// the tile's target size (not full-resolution) so memory stays bounded; the LRU evicts and
/// disposes the least-recently-used entries past the cap. Only visible tiles call Get (the
/// grid is virtualized), so in-flight work is naturally windowed to the viewport.</summary>
public sealed class ThumbnailCache : IDisposable
{
    public enum Status { Pending, Ready, Failed }

    private sealed class Entry { public IImageSource? Image; public Status Status; }

    // Files past this size aren't decoded (matches the preview panel's cap) — the tile
    // falls back to the image-kind icon.
    private const long MaxBytes = 24L * 1024 * 1024;

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _map = new();
    private readonly LinkedList<string> _lru = new();   // front = oldest
    private readonly HashSet<string> _inFlight = new();
    private readonly Action _onDecoded;
    private readonly int _cap;
    private bool _disposed;

    // How many decodes may run at once. Half the cores, at least two: the work is CPU-bound
    // and each one holds a bitmap, so the cap bounds peak memory as much as it does CPU.
    private readonly SemaphoreSlim _slots = new(Math.Max(2, Environment.ProcessorCount / 2));

    public ThumbnailCache(Action onDecoded, int capacity = 512)
    {
        _onDecoded = onDecoded;
        _cap = capacity;
    }

    /// <summary>Number of decodes currently running (for the "decoding n…" footer).</summary>
    public int PendingCount { get { lock (_lock) return _inFlight.Count; } }

    /// <summary>How many decodes have landed, ever. Counted before the repaint callback runs, so a
    /// build that the callback asks for sees it moved: the grid keeps its rows of tiles between
    /// builds, and a tile still showing the placeholder is made again only when this changes.</summary>
    public int Landed => Volatile.Read(ref _landed);
    private int _landed;

    /// <summary>The thumbnail for a file at a target box size, or (null, Pending) while a
    /// background decode runs. Keyed by path + mtime + size + target so an edited file or a
    /// density change re-decodes.</summary>
    public (IImageSource? Image, Status Status) Get(string path, long size, DateTime mtime, int targetPx)
    {
        string key = $"{path}|{mtime.Ticks}|{size}|{targetPx}";
        lock (_lock)
        {
            if (_disposed) return (null, Status.Failed);
            if (_map.TryGetValue(key, out var e)) { Touch(key); return (e.Image, e.Status); }
            if (!_inFlight.Add(key)) return (null, Status.Pending);
        }
        _ = DecodeQueued(key, path, size, targetPx);
        return (null, Status.Pending);
    }

    /// <summary>Waits for a decode slot before taking a thread. Every visible tile calls
    /// <see cref="Get"/> on the same frame, so without this a screenful of images is a
    /// screenful of simultaneous decodes — and even a bounded one is megabytes. Awaited
    /// rather than blocked so queued work doesn't hold thread-pool threads while it waits.</summary>
    private async Task DecodeQueued(string key, string path, long size, int targetPx)
    {
        await _slots.WaitAsync().ConfigureAwait(false);
        try { await Task.Run(() => Decode(key, path, size, targetPx)).ConfigureAwait(false); }
        finally { _slots.Release(); }
    }

    private void Decode(string key, string path, long size, int targetPx)
    {
        IImageSource? img = null;
        var status = Status.Failed;
        try
        {
            if (size > 0 && size <= MaxBytes)
            {
                // Decoded straight to (about) the tile size where the format allows it, and
                // refused outright when the full frame would be enormous — a 1.5 MB PNG can
                // still be 1.6 GB of pixels. Failed here shows the image-kind icon.
                using var full = ImageDecode.Bounded(path, targetPx);
                if (full != null)
                {
                    var scaled = Downscale(full, targetPx);
                    img = new SkiaImageSource(scaled);
                    status = Status.Ready;
                }
            }
        }
        catch { status = Status.Failed; }

        lock (_lock)
        {
            if (_disposed) { (img as IDisposable)?.Dispose(); return; }
            _map[key] = new Entry { Image = img, Status = status };
            _lru.AddLast(key);
            _inFlight.Remove(key);
            Trim();
        }
        Interlocked.Increment(ref _landed);
        _onDecoded();   // caller marshals to the UI + coalesces the repaint
    }

    /// <summary>Fit the bitmap inside a <paramref name="target"/>×target box, preserving
    /// aspect. Images already smaller are kept as-is.</summary>
    private static SKBitmap Downscale(SKBitmap src, int target)
    {
        int w = src.Width, h = src.Height;
        float scale = target / (float)Math.Max(w, h);
        if (scale >= 1f) return src.Copy();
        int nw = Math.Max(1, (int)(w * scale)), nh = Math.Max(1, (int)(h * scale));
        return src.Resize(new SKImageInfo(nw, nh), SKSamplingOptions.Default) ?? src.Copy();
    }

    private void Touch(string key)
    {
        _lru.Remove(key);
        _lru.AddLast(key);
    }

    private void Trim()
    {
        while (_map.Count > _cap && _lru.First is { } node)
        {
            _lru.RemoveFirst();
            if (_map.Remove(node.Value, out var e)) (e.Image as IDisposable)?.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            foreach (var e in _map.Values) (e.Image as IDisposable)?.Dispose();
            _map.Clear();
            _lru.Clear();
        }
        // _slots is deliberately not disposed. Disposing a SemaphoreSlim with waiters can
        // leave them never completing, and a decode queued behind one would then never see
        // the _disposed flag that tells it to free its own bitmap. It holds no unmanaged
        // handle — that is allocated only by AvailableWaitHandle, which nothing here uses.
    }
}
