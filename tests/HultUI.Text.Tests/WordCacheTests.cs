using HultUI.Testing;
using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// The word shape cache's table (<see cref="WordCache"/>): <see cref="LruCache{TKey, TValue}"/>'s
/// rules — an entry bound, a weight bound, an entry counting for at most half of it, the least
/// recently used out first, a word put again replacing its entry — with a word found by a span of
/// a longer text, and slots of evicted entries taken by new ones.
/// </summary>
public class WordCacheTests
{
    private static readonly StubResolvedFont Font = new();
    private static readonly StubResolvedFont Other = new();
    private const float Size = 16f;
    private static readonly int Hash = WordCache.FontHash(Font, Size);

    private static ShapedRun Run(string word) => new(word, Font, Size, word.Length, []);

    private static ShapedRun Put(WordCache cache, string word, int weight = 0)
    {
        var run = Run(word);
        cache.Put(word, Font, Size, Hash, run, weight);
        return run;
    }

    // Found by a span of a longer text, as the pipeline finds a word.
    private static bool Has(WordCache cache, string word) =>
        cache.TryGet(("<" + word + ">").AsSpan(1, word.Length), Font, Size, Hash, out _);

    [Fact]
    public void AFullCache_EvictsTheLeastRecentlyUsed()
    {
        var cache = new WordCache(capacity: 3, maxWeight: long.MaxValue);
        var one = Put(cache, "one");
        Put(cache, "two");
        Put(cache, "three");
        Assert.True(Has(cache, "one"));          // one is now the most recent; two the least

        var four = Put(cache, "four");
        Assert.False(Has(cache, "two"));
        var five = Put(cache, "five");           // order now five, four, one, three: three goes
        Assert.False(Has(cache, "three"));
        Assert.Equal(3, cache.Count);
        foreach (var (word, run) in new[] { ("one", one), ("four", four), ("five", five) })
        {
            Assert.True(cache.TryGet(word, Font, Size, Hash, out var found));
            Assert.Same(run, found);
        }
    }

    [Fact]
    public void AWeightBound_EvictsTheLeastRecentlyUsed_UntilTheNewEntryFits()
    {
        var cache = new WordCache(capacity: 100, maxWeight: 100);
        Put(cache, "a", 30);
        Put(cache, "b", 30);
        Put(cache, "c", 30);
        Assert.Equal(90, cache.Weight);
        Assert.True(Has(cache, "a"));            // order now a, c, b

        Put(cache, "d", 40);                     // 130 > 100: b goes, and that is enough
        Assert.False(Has(cache, "b"));
        Assert.True(Has(cache, "a") && Has(cache, "c") && Has(cache, "d"));
        Assert.Equal(100, cache.Weight);
        Assert.Equal(3, cache.Count);

        Put(cache, "e", 50);                     // order d, c, a: a goes, then c, and d stays
        Assert.False(Has(cache, "a") || Has(cache, "c"));
        Assert.True(Has(cache, "d") && Has(cache, "e"));
        Assert.Equal(2, cache.Count);
        Assert.Equal(90, cache.Weight);
    }

    [Fact]
    public void AnEntryHeavierThanHalfTheBound_CountsForHalf_AndLeavesRoomForOthers()
    {
        var cache = new WordCache(capacity: 100, maxWeight: 100);
        for (int i = 0; i < 7; i++) Put(cache, "w" + i, 10);

        Put(cache, "heavy", 250);                // counts 50: two of the seven go
        Assert.Equal(6, cache.Count);
        Assert.Equal(100, cache.Weight);
        Assert.False(Has(cache, "w0") || Has(cache, "w1"));

        // Entries put after it while it is in use make room among the others first.
        for (int i = 10; i < 20; i++)
        {
            Assert.True(Has(cache, "heavy"));
            Put(cache, "w" + i, 10);
        }
        Assert.True(Has(cache, "heavy"));
        Assert.Equal(100, cache.Weight);

        Put(cache, "heavy", int.MaxValue);       // and again, as half
        Assert.Equal(100, cache.Weight);
    }

    [Fact]
    public void AWordPutAgain_TakesTheNewRunAndWeight_AsTheMostRecentlyUsed()
    {
        var cache = new WordCache(capacity: 3, maxWeight: 100);
        Put(cache, "a", 30);
        Put(cache, "b", 20);                     // order b, a

        var again = Put(cache, "a", 10);         // order a, b
        Assert.Equal(2, cache.Count);
        Assert.Equal(30, cache.Weight);
        Assert.True(cache.TryGet("a", Font, Size, Hash, out var found));
        Assert.Same(again, found);

        Put(cache, "c");                         // order c, a, b
        Put(cache, "d");                         // b is the least recently used, not a
        Assert.False(Has(cache, "b"));
        Assert.True(Has(cache, "a") && Has(cache, "c") && Has(cache, "d"));
        Assert.Equal(3, cache.Count);
        Assert.Equal(10, cache.Weight);
    }

    [Fact]
    public void AWordInALongerText_IsFoundAsItsString_AndOnlyInItsFontAndSize()
    {
        var cache = new WordCache(capacity: 100, maxWeight: long.MaxValue);
        string acute = ((char)0x301).ToString();
        string[] words =
        [
            "a", "pretty", "12345", "  ", " ", "Ελλη", "東京", "e" + acute,
            "x" + char.ConvertFromUtf32(0x1F600) + "y", (char)0xAD + "soft", new string('w', 300),
        ];
        foreach (var word in words)
        {
            var run = Put(cache, word);
            string text = "<<" + word + ">>";
            var segment = text.AsSpan(2, word.Length);
            Assert.Equal(word.GetHashCode(), string.GetHashCode(segment));
            Assert.True(cache.TryGet(segment, Font, Size, Hash, out var found), $"\"{word}\" in place is not \"{word}\"");
            Assert.Same(run, found);

            // The font instance and the size are the key's too; and the characters, not the length.
            Assert.False(cache.TryGet(segment, Other, Size, WordCache.FontHash(Other, Size), out _));
            Assert.False(cache.TryGet(segment, Font, 16.5f, WordCache.FontHash(Font, 16.5f), out _));
            string changed = text[..^3] + (char)(text[^3] + 1) + text[^2..];
            var changedSegment = changed.AsSpan(2, word.Length);
            Assert.False(cache.TryGet(changedSegment, Font, Size, Hash, out _), $"\"{changed[2..^2]}\" matched \"{word}\"");
            Assert.False(cache.TryGet(text.AsSpan(1, word.Length + 1), Font, Size, Hash, out _));

            // And each is compared, not only hashed: the same misses with hashes made to collide.
            Assert.False(cache.TryGet(segment, Other, Size, Hash, out _));
            Assert.False(cache.TryGet(segment, Font, 16.5f, Hash, out _));
            int colliding = Hash ^ string.GetHashCode(segment) ^ string.GetHashCode(changedSegment);
            Assert.False(cache.TryGet(changedSegment, Font, Size, colliding, out _), $"\"{changed[2..^2]}\" matched \"{word}\"");
        }
        Assert.Equal(words.Length, cache.Count);

        // A size equal to another is the same size: 0 and -0, as float.Equals has them.
        var zero = new WordCache(capacity: 10, maxWeight: long.MaxValue);
        var run0 = Run("word");
        zero.Put("word", Font, 0f, WordCache.FontHash(Font, 0f), run0, 0);
        Assert.True(zero.TryGet("a word".AsSpan(2), Font, -0f, WordCache.FontHash(Font, -0f), out var negative));
        Assert.Same(run0, negative);
    }

    [Fact]
    public void Growing_KeepsEveryEntry()
    {
        // From 16 slots to 2,048, the buckets made again at each step; and found again after.
        var cache = new WordCache(capacity: 10_000, maxWeight: long.MaxValue);
        var runs = new ShapedRun[1_500];
        for (int i = 0; i < runs.Length; i++)
        {
            runs[i] = Put(cache, "word" + i);
            if (i >= 16 && (i & (i - 1)) == 0)   // 17, 33, 65 ... entries put: the slots just doubled
                for (int k = 0; k <= i; k++)
                    Assert.True(Has(cache, "word" + k), $"word{k} lost at {i + 1} entries");
        }
        Assert.Equal(runs.Length, cache.Count);
        for (int i = 0; i < runs.Length; i++)
        {
            Assert.True(cache.TryGet("word" + i, Font, Size, Hash, out var found));
            Assert.Same(runs[i], found);
        }
    }

    [Fact]
    public void AFullCache_PutsIntoTheSlotsOfThoseItEvicts_WithoutAllocating()
    {
        // Full by count: each new word takes the slot of the one it evicts.
        var words = new string[64];
        var runs = new ShapedRun[words.Length];
        for (int i = 0; i < words.Length; i++) { words[i] = "word" + i; runs[i] = Run(words[i]); }
        var cache = new WordCache(capacity: 4, maxWeight: long.MaxValue);
        int next = 0;   // the words in turn: the next one was evicted long ago
        for (; next < 16; next++) cache.Put(words[next % 64], Font, Size, Hash, runs[next % 64], 0);
        int slots = cache.Slots;
        Assert.Equal(0, Allocations.Least(() =>
        {
            for (int n = 0; n < 1_000; n++, next++)
                if (!cache.TryGet(words[next % 64], Font, Size, Hash, out _))
                    cache.Put(words[next % 64], Font, Size, Hash, runs[next % 64], 0);
        }));
        Assert.Equal(4, cache.Count);
        Assert.Equal(slots, cache.Slots);

        // Full by weight, in weights that make a word evict none, one or several: the slots of the
        // several are taken by the words that evict none.
        int[] weights = [10, 30, 20, 10, 10, 45, 5, 5];
        var byWeight = new WordCache(capacity: 1_000, maxWeight: 100);
        next = 0;
        for (; next < 64; next++) byWeight.Put(words[next % 64], Font, Size, Hash, runs[next % 64], weights[next % 8]);
        slots = byWeight.Slots;
        Assert.Equal(0, Allocations.Least(() =>
        {
            for (int n = 0; n < 1_000; n++, next++)
                if (!byWeight.TryGet(words[next % 64], Font, Size, Hash, out _))
                    byWeight.Put(words[next % 64], Font, Size, Hash, runs[next % 64], weights[next % 8]);
        }));
        Assert.InRange(byWeight.Weight, 55, 100);
        Assert.True(byWeight.TryGet(words[(next - 1) % 64], Font, Size, Hash, out _));
        Assert.Equal(slots, byWeight.Slots);
    }

    [Fact]
    public void WhatItFindsAndEvicts_IsWhatLruCacheFindsAndEvicts_OpForOp()
    {
        // Random lookups and puts over two fonts, two sizes and a small vocabulary, against
        // LruCache keyed by (word, font, size) under the same bounds: the same hits, the same
        // runs, the same count and weight after every operation.
        var fonts = new IResolvedFont[] { Font, Other };
        float[] sizes = [16f, 13f];
        var vocabulary = new string[12];
        for (int i = 0; i < vocabulary.Length; i++) vocabulary[i] = new string((char)('a' + i), 1 + i % 3);
        var cache = new WordCache(capacity: 16, maxWeight: 300);
        var model = new LruCache<(string, IResolvedFont, float), ShapedRun>(capacity: 16, maxWeight: 300);
        var rng = new Random(17);
        int hits = 0, evicting = 0;
        for (int op = 0; op < 20_000; op++)
        {
            string word = vocabulary[rng.Next(vocabulary.Length)];
            var font = fonts[rng.Next(2)];
            float size = sizes[rng.Next(2)];
            int fontHash = WordCache.FontHash(font, size);
            if (rng.Next(10) < 6)
            {
                string text = "  " + word + ".";
                bool found = cache.TryGet(text.AsSpan(2, word.Length), font, size, fontHash, out var run);
                Assert.Equal(model.TryGet((word, font, size), out var expected), found);
                if (found) { Assert.Same(expected, run); hits++; }
            }
            else
            {
                int weight = rng.Next(20) == 0 ? 1_000 : rng.Next(60);
                var run = new ShapedRun(word, font, size, 1f, []);
                int before = cache.Count;
                cache.Put(new string(word.AsSpan()), font, size, fontHash, run, weight);
                model.Put((word, font, size), run, weight);
                if (cache.Count <= before) evicting++;
            }
            Assert.Equal(model.Count, cache.Count);
            Assert.Equal(model.Weight, cache.Weight);
        }
        Assert.True(hits > 1_000 && evicting > 1_000, $"{hits} hits, {evicting} puts that evicted or replaced");
    }
}
