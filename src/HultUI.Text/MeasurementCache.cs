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

/// <summary>
/// The word shape cache: the shaped run of a word — a space or non-space segment of a run — in a
/// font instance at a size, found by a span of the text the word is in, so a lookup makes no
/// string; only a miss copies the word out, to keep it as the key. Fonts compare as instances: an
/// <see cref="IFontCollection"/> hands out one per face, and no <see cref="IResolvedFont"/> defines
/// equality of its own.
/// </summary>
/// <remarks>
/// <see cref="LruCache{TKey, TValue}"/>'s rules in a table of its own: at most <c>capacity</c>
/// entries and <c>maxWeight</c> of weight, an entry counting for at most half of that, the least
/// recently used out first until both hold, and a word put again replacing its entry. Chained
/// buckets over one array of entries, and the order of use as indices inside the entries. It was
/// a <see cref="Dictionary{TKey, TValue}"/> found through an <c>AlternateLookup</c>, and a
/// <see cref="LinkedList{T}"/> for the order. NativeAOT 11 calls that lookup's
/// <see cref="IAlternateEqualityComparer{TAlternate, TOther}"/> through two interface dispatches,
/// where 10 inlined it: a long document's 38,000 word lookups a step went from 15 to 27 ns each.
/// Here the hash and the comparison are this class's own code. An entry is 48 bytes and its
/// bucket 4, where the dictionary's entry, bucket and linked-list node came to about 124 and an
/// object.
/// </remarks>
internal sealed class WordCache(int capacity, long maxWeight)
{
    // Next chains a bucket, as an index + 1 (0 ends it), or, in a free slot, the free list. Newer
    // and Older are the entry's neighbours in the order of use (-1 past either end).
    private struct Entry
    {
        public string? Word;
        public IResolvedFont? Font;
        public ShapedRun? Run;
        public int Hash, Next, Newer, Older, Weight;
        public float Size;
    }

    private int[] _buckets = [];   // each bucket's first entry, as an index + 1; a power of two of them
    private Entry[] _entries = [];
    private int _used;             // slots that have held an entry: the free ones among them are listed from _free
    private int _free = -1, _freeCount;
    private int _newest = -1, _oldest = -1;
    private long _weight;

    /// <summary>The number of entries.</summary>
    public int Count => _used - _freeCount;

    /// <summary>The sum of the entries' weights, as counted: never above the bound.</summary>
    public long Weight => _weight;

    /// <summary>The slots the entries have, for the tests: a full cache takes new entries into the
    /// slots of those it evicts, so this stops growing.</summary>
    internal int Slots => _entries.Length;

    /// <summary>The font and size's share of a word's hash, the same for every word of a run: worked
    /// out once for them all.</summary>
    public static int FontHash(IResolvedFont font, float fontSize) =>
        HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(font), fontSize);

    /// <summary>The run of <paramref name="word"/> in <paramref name="font"/> at <paramref name="fontSize"/>,
    /// which becomes the most recently used. <paramref name="fontHash"/> is <see cref="FontHash"/>
    /// of the two.</summary>
    public bool TryGet(ReadOnlySpan<char> word, IResolvedFont font, float fontSize, int fontHash, out ShapedRun run)
    {
        int i = Find(word, font, fontSize, string.GetHashCode(word) ^ fontHash);
        if (i < 0)
        {
            run = null!;
            return false;
        }
        if (i != _newest)
        {
            Unlink(i);
            LinkNewest(i);
        }
        run = _entries[i].Run!;
        return true;
    }

    /// <summary>Puts <paramref name="run"/> under <paramref name="word"/>, <paramref name="font"/> and
    /// <paramref name="fontSize"/>, replacing what was there, as the most recently used entry, after
    /// the least recently used ones it leaves no room for. <paramref name="weight"/> counts towards
    /// the weight bound (the caller's estimate of the bytes the entry keeps alive).</summary>
    public void Put(string word, IResolvedFont font, float fontSize, int fontHash, ShapedRun run, int weight)
    {
        weight = weight <= maxWeight / 2 ? weight : (int)(maxWeight / 2);
        int hash = string.GetHashCode(word.AsSpan()) ^ fontHash;
        int existing = Find(word, font, fontSize, hash);
        if (existing >= 0)
            Remove(existing);
        while (_oldest >= 0 && (Count >= capacity || _weight + weight > maxWeight))
            Remove(_oldest);

        int slot;
        if (_free >= 0)
        {
            slot = _free;
            _free = _entries[slot].Next - 1;
            _freeCount--;
        }
        else
        {
            if (_used == _entries.Length)
                Grow();
            slot = _used++;
        }
        ref int bucket = ref _buckets[hash & (_buckets.Length - 1)];
        _entries[slot] = new Entry { Word = word, Font = font, Run = run, Hash = hash, Next = bucket, Weight = weight, Size = fontSize };
        bucket = slot + 1;
        LinkNewest(slot);
        _weight += weight;
    }

    // The entry of the word, font and size, whose hash is `hash`; -1 for none.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private int Find(ReadOnlySpan<char> word, IResolvedFont font, float fontSize, int hash)
    {
        var buckets = _buckets;
        if (buckets.Length == 0)
            return -1;
        var entries = _entries;
        for (int i = buckets[hash & (buckets.Length - 1)] - 1; (uint)i < (uint)entries.Length; i = entries[i].Next - 1)
        {
            ref var e = ref entries[i];
            if (e.Hash == hash && ReferenceEquals(e.Font, font) && e.Size.Equals(fontSize) && word.SequenceEqual(e.Word))
                return i;
        }
        return -1;
    }

    // Twice the slots, and the buckets with them. Only when no slot is free: every slot holds an entry.
    private void Grow()
    {
        int size = Math.Max(16, 2 * _entries.Length);
        Array.Resize(ref _entries, size);
        var buckets = new int[size];
        for (int i = 0; i < _used; i++)
        {
            ref var e = ref _entries[i];
            ref int bucket = ref buckets[e.Hash & (size - 1)];
            e.Next = bucket;
            bucket = i + 1;
        }
        _buckets = buckets;
    }

    // Takes entry i out of its bucket and the order, and lists its slot, emptied, as free.
    private void Remove(int i)
    {
        ref var e = ref _entries[i];
        ref int link = ref _buckets[e.Hash & (_buckets.Length - 1)];
        while (link - 1 != i)
            link = ref _entries[link - 1].Next;
        link = e.Next;
        Unlink(i);
        _weight -= e.Weight;
        e = new Entry { Next = _free + 1 };
        _free = i;
        _freeCount++;
    }

    // Inlined into a hit's move to the front: NativeAOT left both out of line.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void Unlink(int i)
    {
        ref var e = ref _entries[i];
        if (e.Newer >= 0) _entries[e.Newer].Older = e.Older;
        else _newest = e.Older;
        if (e.Older >= 0) _entries[e.Older].Newer = e.Newer;
        else _oldest = e.Newer;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private void LinkNewest(int i)
    {
        ref var e = ref _entries[i];
        e.Newer = -1;
        e.Older = _newest;
        if (_newest >= 0) _entries[_newest].Newer = i;
        else _oldest = i;
        _newest = i;
    }
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

    /// <summary>
    /// What the entry under <paramref name="key"/> counts for from now on, for an entry that
    /// keeps more alive than when it was put (a value that fills in later). It keeps its place
    /// in the order; the least recently used others go if it leaves the cache over its bound.
    /// Nothing happens without such an entry.
    /// </summary>
    public void Reweigh(TKey key, int weight)
    {
        if (!_map.TryGetValue(key, out var node)) return;
        weight = Counted(weight);
        _weight += weight - node.Value.Weight;
        node.Value = node.Value with { Weight = weight };
        for (var other = _order.Last; _weight > _maxWeight && other is not null;)
        {
            var newer = other.Previous;
            if (other != node)
            {
                _order.Remove(other);
                _map.Remove(other.Value.Key);
                _weight -= other.Value.Weight;
                Keep(other);
            }
            other = newer;
        }
    }

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
