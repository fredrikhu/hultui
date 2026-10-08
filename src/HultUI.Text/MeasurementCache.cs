using HultUI.Core.Layout;
using HultUI.Core.Styling;

namespace HultUI.Text;

/// <summary>Key for a fully laid-out, width-dependent text result.</summary>
internal readonly record struct MeasurementKey(
    string Text, string FontFamily, float FontSize, FontWeight FontWeight, FontSlant FontSlant,
    float MaxWidth, int MaxLines, TextOverflow Overflow, float LineHeight, float LetterSpacing,
    // Layout-relevant digest of metric-affecting spans; 0 for plain text, so plain
    // keys and hit rates are exactly what they were before spans could shape.
    int SpanDigest = 0,
    // Paragraph direction: an RTL-style layout of the same string reorders runs, so
    // the two must not share a cache entry. Ltr default keeps existing keys intact.
    TextDirection Direction = TextDirection.Ltr);

/// <summary>
/// Key for the width-independent shaping of a string. Shaping (itemization +
/// glyph shaping) does not depend on the available width — only line breaking
/// does — so this deliberately omits MaxWidth/MaxLines. Slant, line height and
/// letter spacing DO change the shaped result, so they are part of the key.
/// </summary>
internal readonly record struct ShapeKey(
    string Text, string FontFamily, float FontSize, FontWeight FontWeight, FontSlant FontSlant,
    float LineHeight, float LetterSpacing);

/// <summary>Key for the word shape cache: a space or non-space segment of a run, and the font
/// instance and size it was shaped in.</summary>
internal readonly record struct WordShapeKey(string Segment, IResolvedFont Font, float FontSize);

/// <summary><see cref="WordShapeKey"/> for a segment still inside the text it was cut from, so a
/// lookup makes no string; only a miss copies the segment out, to keep it as the key.
/// <paramref name="fontHash"/> is <see cref="WordShapeKeyComparer.FontHash"/> of the font and
/// size, worked out once for all the segments of a run.</summary>
internal readonly ref struct WordSpanKey(ReadOnlySpan<char> segment, IResolvedFont font, float fontSize, int fontHash)
{
    public readonly ReadOnlySpan<char> Segment = segment;
    public readonly IResolvedFont Font = font;
    public readonly float FontSize = fontSize;
    public readonly int FontHash = fontHash;
}

/// <summary>
/// <see cref="WordShapeKey"/>'s equality, and the same for a <see cref="WordSpanKey"/>: a span
/// hashes as the string of its characters does (<see cref="string.GetHashCode(ReadOnlySpan{char})"/>),
/// so it finds that string's entry. Fonts compare as instances — an <see cref="IFontCollection"/>
/// hands out one per face, and no <see cref="IResolvedFont"/> defines equality of its own — which
/// is what the record's equality came to, without a virtual call.
/// </summary>
internal sealed class WordShapeKeyComparer
    : IEqualityComparer<WordShapeKey>, IAlternateEqualityComparer<WordSpanKey, WordShapeKey>
{
    public static readonly WordShapeKeyComparer Instance = new();

    /// <summary>The font and size's share of a key's hash, the same for every word of a run.</summary>
    public static int FontHash(IResolvedFont font, float fontSize) =>
        HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(font), fontSize);

    public bool Equals(WordShapeKey x, WordShapeKey y) =>
        ReferenceEquals(x.Font, y.Font) && x.FontSize.Equals(y.FontSize) && string.Equals(x.Segment, y.Segment);

    public int GetHashCode(WordShapeKey key) => key.Segment.GetHashCode() ^ FontHash(key.Font, key.FontSize);

    public bool Equals(WordSpanKey alternate, WordShapeKey other) =>
        ReferenceEquals(alternate.Font, other.Font) && alternate.FontSize.Equals(other.FontSize)
        && alternate.Segment.SequenceEqual(other.Segment);

    public int GetHashCode(WordSpanKey alternate) => string.GetHashCode(alternate.Segment) ^ alternate.FontHash;

    public WordShapeKey Create(WordSpanKey alternate) =>
        new(alternate.Segment.ToString(), alternate.Font, alternate.FontSize);
}

/// <summary>Simple LRU cache used for text measurement and shaping results.</summary>
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _map;
    private readonly LinkedList<CacheEntry> _order = new();

    public LruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        _capacity = capacity;
        // Grow on demand rather than pre-allocating the full capacity — a large
        // eviction bound shouldn't cost memory in apps with few distinct strings.
        _map = new(comparer);
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

    /// <summary>
    /// The cache looked up by keys of another type, which its comparer also implements
    /// <see cref="IAlternateEqualityComparer{TAlternate, TOther}"/> for — a segment of a longer
    /// string, found without copying it out. Made once and kept: making it checks the comparer.
    /// </summary>
    public InPlace<TAlternate> LookedUpBy<TAlternate>() where TAlternate : notnull, allows ref struct =>
        new(this);

    public readonly struct InPlace<TAlternate> where TAlternate : notnull, allows ref struct
    {
        private readonly LruCache<TKey, TValue> _cache;
        private readonly Dictionary<TKey, LinkedListNode<CacheEntry>>.AlternateLookup<TAlternate> _lookup;

        internal InPlace(LruCache<TKey, TValue> cache)
        {
            _cache = cache;
            _lookup = cache._map.GetAlternateLookup<TAlternate>();
        }

        /// <summary><see cref="TryGet(TKey, out TValue)"/>, by the other key.</summary>
        public bool TryGet(TAlternate key, out TValue value)
        {
            if (_lookup.TryGetValue(key, out var node))
            {
                _cache._order.Remove(node);
                _cache._order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }
    }

    public void Put(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            _order.Remove(existing);
            _map.Remove(key);
        }

        _map[key] = FirstNode(key, value);
    }

    /// <summary>
    /// <see cref="Put"/> for a key that <see cref="TryGet"/> has just failed to find: it skips
    /// looking the key up again. On a path that meets most strings once, the lookups of a
    /// TryGet-then-Put pair are a measurable part of what the cache costs.
    /// </summary>
    public void Add(TKey key, TValue value) => _map.Add(key, FirstNode(key, value));

    // A new entry, most recently used. In a full cache it takes the node of the least recently
    // used one, which it evicts: a full cache inserts without allocating.
    private LinkedListNode<CacheEntry> FirstNode(TKey key, TValue value)
    {
        if (_map.Count < _capacity)
            return _order.AddFirst(new CacheEntry(key, value));

        var last = _order.Last!;
        _order.RemoveLast();
        _map.Remove(last.Value.Key);
        last.Value = new CacheEntry(key, value);
        _order.AddFirst(last);
        return last;
    }

    public void Clear()
    {
        _map.Clear();
        _order.Clear();
    }

    private readonly record struct CacheEntry(TKey Key, TValue Value);
}
