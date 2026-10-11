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

/// <summary>Key for a string's width on one unbounded line (<see cref="TextPipeline.MeasureWidth"/>):
/// the string and what of the style a width depends on — the direction only for text on the BiDi
/// path, Ltr for any other. <see cref="Hash"/> is worked out once, from the span the string was cut
/// from (<see cref="WidthKeyComparer.HashOf"/>): a line can be a hundred characters, and hashing it
/// again to put it in the cache cost as much as the lookup.</summary>
internal readonly record struct WidthKey(
    string Text, string FontFamily, float FontSize, FontWeight FontWeight, FontSlant FontSlant, float LetterSpacing,
    TextDirection Direction, int Hash);

/// <summary><see cref="WidthKey"/> for a string still inside a longer text — a line of a
/// document — so a lookup makes no string; only a miss copies it out, to keep it as the key.</summary>
internal readonly ref struct WidthSpanKey(ReadOnlySpan<char> text, in ResolvedInheritedStyle style,
    TextDirection direction, int hash)
{
    public readonly ReadOnlySpan<char> Text = text;
    public readonly string FontFamily = style.FontFamily;
    public readonly float FontSize = style.FontSize;
    public readonly FontWeight FontWeight = style.FontWeight;
    public readonly FontSlant FontSlant = style.FontSlant;
    public readonly float LetterSpacing = style.LetterSpacing;
    public readonly TextDirection Direction = direction;
    public readonly int Hash = hash;
}

/// <summary><see cref="WidthKey"/>'s equality, and the same for a <see cref="WidthSpanKey"/>; both
/// hash as <see cref="HashOf"/> says, the span as the string of its characters would.</summary>
internal sealed class WidthKeyComparer : IEqualityComparer<WidthKey>, IAlternateEqualityComparer<WidthSpanKey, WidthKey>
{
    public static readonly WidthKeyComparer Instance = new();

    /// <summary>The style's share of a key's hash.</summary>
    public static int StyleHash(in ResolvedInheritedStyle style) =>
        HashCode.Combine(style.FontFamily, style.FontSize, style.FontWeight, style.FontSlant, style.LetterSpacing);

    /// <summary>A key's hash: the text's, as <see cref="string.GetHashCode()"/> has it, the style's,
    /// and the direction's in the top bits (none for Ltr).</summary>
    public static int HashOf(ReadOnlySpan<char> text, int styleHash, TextDirection direction) =>
        string.GetHashCode(text) ^ styleHash ^ ((int)direction << 30);

    public bool Equals(WidthKey x, WidthKey y) => x == y;

    public int GetHashCode(WidthKey key) => key.Hash;

    public bool Equals(WidthSpanKey alternate, WidthKey other) =>
        alternate.Hash == other.Hash && alternate.FontSize.Equals(other.FontSize)
        && alternate.FontWeight == other.FontWeight && alternate.FontSlant == other.FontSlant
        && alternate.LetterSpacing.Equals(other.LetterSpacing) && alternate.Direction == other.Direction
        && string.Equals(alternate.FontFamily, other.FontFamily) && alternate.Text.SequenceEqual(other.Text);

    public int GetHashCode(WidthSpanKey alternate) => alternate.Hash;

    public WidthKey Create(WidthSpanKey alternate) =>
        new(alternate.Text.ToString(), alternate.FontFamily, alternate.FontSize, alternate.FontWeight,
            alternate.FontSlant, alternate.LetterSpacing, alternate.Direction, alternate.Hash);
}

/// <summary>
/// Simple LRU cache used for text measurement and shaping results. Bounded by a number of
/// entries and, when given a <c>maxWeight</c>, by the sum of the weights its entries were put
/// with: the least recently used go first until both bounds hold. An entry counts for at most
/// half the weight bound, so one heavier than that — the layout of a whole document in one
/// label — still leaves room for others, rather than taking the cache alone and being pushed
/// out by the next entry, every frame that draws both.
/// </summary>
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly long _maxWeight;
    private long _weight;
    private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _map;
    private readonly LinkedList<CacheEntry> _order = new();

    public LruCache(int capacity, IEqualityComparer<TKey>? comparer = null, long maxWeight = long.MaxValue)
    {
        _capacity = capacity;
        _maxWeight = maxWeight;
        // Grow on demand rather than pre-allocating the full capacity — a large
        // eviction bound shouldn't cost memory in apps with few distinct strings.
        _map = new(comparer);
    }

    /// <summary>The number of entries.</summary>
    public int Count => _map.Count;

    /// <summary>The sum of the entries' weights, as counted: never above the bound.</summary>
    public long Weight => _weight;

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

    /// <summary>Puts <paramref name="value"/> under <paramref name="key"/>, replacing what was
    /// there, as the most recently used entry. <paramref name="weight"/> counts towards the
    /// cache's weight bound (the caller's estimate of the bytes the entry keeps alive).</summary>
    public void Put(TKey key, TValue value, int weight = 0)
    {
        weight = Counted(weight);
        if (_map.TryGetValue(key, out var existing))
        {
            _order.Remove(existing);
            _map.Remove(key);
            _weight -= existing.Value.Weight;
        }

        _map[key] = FirstNode(key, value, weight);
    }

    /// <summary>
    /// <see cref="Put"/> for a key that <see cref="TryGet"/> has just failed to find: it skips
    /// looking the key up again. On a path that meets most strings once, the lookups of a
    /// TryGet-then-Put pair are a measurable part of what the cache costs.
    /// </summary>
    public void Add(TKey key, TValue value, int weight = 0) => _map.Add(key, FirstNode(key, value, Counted(weight)));

    private int Counted(int weight) => weight <= _maxWeight / 2 ? weight : (int)(_maxWeight / 2);

    // A new entry, most recently used, after the least recently used ones it leaves no room
    // for. It takes the node of one it evicts, or of one evicted before: a full cache inserts
    // without allocating.
    private LinkedListNode<CacheEntry> FirstNode(TKey key, TValue value, int weight)
    {
        LinkedListNode<CacheEntry>? node = null;
        while (_order.Last is { } last && (_map.Count >= _capacity || _weight + weight > _maxWeight))
        {
            _order.RemoveLast();
            _map.Remove(last.Value.Key);
            _weight -= last.Value.Weight;
            if (node is null) node = last;
            else Keep(last);
        }
        node ??= TakeSpare();

        _weight += weight;
        var entry = new CacheEntry(key, value, weight);
        if (node is null)
            return _order.AddFirst(entry);
        node.Value = entry;
        _order.AddFirst(node);
        return node;
    }

    // Nodes evicted beyond the one the new entry took, emptied, for entries that evict none: a
    // cache full by weight takes a light entry in for nothing and a heavy one for several.
    private LinkedListNode<CacheEntry>?[]? _spares;
    private int _spareCount;

    private void Keep(LinkedListNode<CacheEntry> node)
    {
        _spares ??= new LinkedListNode<CacheEntry>?[16];
        if (_spareCount == _spares.Length) return;
        node.Value = default;
        _spares[_spareCount++] = node;
    }

    private LinkedListNode<CacheEntry>? TakeSpare()
    {
        if (_spareCount == 0) return null;
        var node = _spares![--_spareCount];
        _spares[_spareCount] = null;
        return node;
    }

    public void Clear()
    {
        _map.Clear();
        _order.Clear();
        _weight = 0;
    }

    private readonly record struct CacheEntry(TKey Key, TValue Value, int Weight);
}
