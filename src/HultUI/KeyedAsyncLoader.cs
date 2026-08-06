namespace HultUI;

/// <summary>
/// A bounded set of <see cref="AsyncLoader{T}"/> slots, one per key — the standard
/// way to cache per-entity async data (per repository, per file, per commit) without
/// growing forever.
/// </summary>
/// <remarks>
/// <para>Hand-rolling this as a plain <c>Dictionary&lt;string, AsyncLoader&lt;T&gt;&gt;</c>
/// works until you notice it never evicts: browsing a few hundred files leaves a few
/// hundred loaders alive, each pinning its last value (decoded images, whole file
/// contents). This keeps the <paramref name="capacity"/> most-recently-used slots and
/// drops the rest, optionally handing each evicted value to <c>onEvicted</c> so
/// unmanaged payloads can be released.</para>
///
/// <para><b>Two levels of key.</b> The slot key identifies the <i>entity</i> and picks
/// the loader; <see cref="AsyncLoader{T}.Load"/>'s own key identifies the <i>version</i>
/// of that entity's data and decides whether a reload is needed. Using the entity for
/// the slot and "entity + generation" for the load key means the last value stays on
/// screen while a newer one loads, and returning to an unchanged entity is a cache hit.
/// Passing the same composite string for both is also fine — that simply gives each
/// version its own slot.</para>
///
/// <para><b>Threading.</b> UI thread only, like <see cref="AsyncLoader{T}"/> itself.</para>
/// </remarks>
public sealed class KeyedAsyncLoader<TKey, T> where TKey : notnull
{
    private sealed class Slot
    {
        public required AsyncLoader<T> Loader;
        public long LastUsed;
    }

    private readonly Dictionary<TKey, Slot> _slots;
    private readonly T _empty;
    private readonly int _capacity;
    private readonly Action<T>? _onEvicted;
    private long _tick;

    /// <param name="empty">Seed value for a freshly created slot, shown until its first load lands.</param>
    /// <param name="capacity">How many slots to keep. Least-recently-used slots are evicted beyond this.</param>
    /// <param name="onEvicted">Optional; receives an evicted slot's last value so it can be disposed.</param>
    /// <param name="comparer">Optional key comparer (e.g. a case-insensitive path comparer).</param>
    public KeyedAsyncLoader(T empty, int capacity = 64, Action<T>? onEvicted = null,
        IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be at least 1.");
        _empty = empty;
        _capacity = capacity;
        _onEvicted = onEvicted;
        _slots = new Dictionary<TKey, Slot>(comparer);
    }

    /// <summary>Number of live slots.</summary>
    public int Count => _slots.Count;

    /// <summary>
    /// The loader for <paramref name="key"/>, created on first use. Marks the slot
    /// most-recently-used, so anything currently on screen is never the eviction
    /// candidate as long as the build reads it each frame.
    /// </summary>
    public AsyncLoader<T> For(TKey key)
    {
        if (_slots.TryGetValue(key, out var slot))
        {
            slot.LastUsed = ++_tick;
            return slot.Loader;
        }

        slot = new Slot { Loader = new AsyncLoader<T>(_empty), LastUsed = ++_tick };
        _slots[key] = slot;
        EvictIfNeeded(protect: key);
        return slot.Loader;
    }

    /// <summary>Convenience: fetch the slot's loader and start (or reuse) a load on it.
    /// Returns the loader so the caller can read <c>Value</c> / <c>IsLoading</c>.</summary>
    public AsyncLoader<T> Load(TKey key, object loadKey, Func<T> produce, Action<T>? onLoaded = null)
    {
        var loader = For(key);
        loader.Load(loadKey, produce, onLoaded);
        return loader;
    }

    /// <summary>Drops every slot (e.g. the underlying data source changed wholesale).</summary>
    public void Clear()
    {
        if (_onEvicted != null)
            foreach (var slot in _slots.Values)
                _onEvicted(slot.Loader.Value);
        _slots.Clear();
    }

    /// <summary>Drops one slot, if present.</summary>
    public bool Remove(TKey key)
    {
        if (!_slots.Remove(key, out var slot)) return false;
        _onEvicted?.Invoke(slot.Loader.Value);
        return true;
    }

    // Over capacity by at most one per insert, so a linear scan for the oldest slot
    // runs at most `capacity` comparisons per insertion — cheaper than maintaining an
    // intrusive LRU list, and this is not a hot path.
    private void EvictIfNeeded(TKey protect)
    {
        while (_slots.Count > _capacity)
        {
            TKey? oldestKey = default;
            long oldest = long.MaxValue;
            bool found = false;
            foreach (var (k, slot) in _slots)
            {
                // Never evict the slot just requested, even if capacity is 1.
                if (_slots.Comparer.Equals(k, protect)) continue;
                if (slot.LastUsed >= oldest) continue;
                oldest = slot.LastUsed;
                oldestKey = k;
                found = true;
            }
            if (!found) break;
            Remove(oldestKey!);
        }
    }
}
