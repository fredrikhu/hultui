using HultUI.Core.Styling;

namespace HultUI.Text;

internal readonly record struct MeasurementKey(
    string Text, string FontFamily, float FontSize, FontWeight FontWeight,
    float MaxWidth, int MaxLines);

internal sealed class MeasurementCache
{
    private readonly int _capacity;
    private readonly Dictionary<MeasurementKey, LinkedListNode<CacheEntry>> _map;
    private readonly LinkedList<CacheEntry> _order = new();

    public MeasurementCache(int capacity)
    {
        _capacity = capacity;
        _map = new(capacity);
    }

    public bool TryGet(MeasurementKey key, out TextLayout layout)
    {
        if (_map.TryGetValue(key, out var node))
        {
            // Move to front (most recently used)
            _order.Remove(node);
            _order.AddFirst(node);
            layout = node.Value.Layout;
            return true;
        }
        layout = null!;
        return false;
    }

    public void Put(MeasurementKey key, TextLayout layout)
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

        var node = _order.AddFirst(new CacheEntry(key, layout));
        _map[key] = node;
    }

    private readonly record struct CacheEntry(MeasurementKey Key, TextLayout Layout);
}
