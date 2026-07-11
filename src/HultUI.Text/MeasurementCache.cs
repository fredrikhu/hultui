using HultUI.Core.Styling;

namespace HultUI.Text;

/// <summary>Key for a fully laid-out, width-dependent text result.</summary>
internal readonly record struct MeasurementKey(
    string Text, string FontFamily, float FontSize, FontWeight FontWeight,
    float MaxWidth, int MaxLines);

/// <summary>
/// Key for the width-independent shaping of a string. Shaping (itemization +
/// glyph shaping) does not depend on the available width — only line breaking
/// does — so this deliberately omits MaxWidth/MaxLines.
/// </summary>
internal readonly record struct ShapeKey(
    string Text, string FontFamily, float FontSize, FontWeight FontWeight);

/// <summary>Simple LRU cache used for text measurement and shaping results.</summary>
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _map;
    private readonly LinkedList<CacheEntry> _order = new();

    public LruCache(int capacity)
    {
        _capacity = capacity;
        // Grow on demand rather than pre-allocating the full capacity — a large
        // eviction bound shouldn't cost memory in apps with few distinct strings.
        _map = new();
    }

    public bool TryGet(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            // Move to front (most recently used)
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        value = default!;
        return false;
    }

    public void Put(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            _order.Remove(existing);
            _map.Remove(key);
        }

        if (_map.Count >= _capacity)
        {
            // Evict least recently used
            var last = _order.Last!;
            _order.RemoveLast();
            _map.Remove(last.Value.Key);
        }

        var node = _order.AddFirst(new CacheEntry(key, value));
        _map[key] = node;
    }

    private readonly record struct CacheEntry(TKey Key, TValue Value);
}
