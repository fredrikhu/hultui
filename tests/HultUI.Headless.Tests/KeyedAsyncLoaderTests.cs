using HultUI;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// KeyedAsyncLoader's job is to stop the hand-rolled
/// <c>Dictionary&lt;key, AsyncLoader&gt;</c> pattern from growing without bound while
/// keeping what's on screen alive. These cover the slot/eviction bookkeeping, which
/// needs no session — the async loading itself is AsyncLoader's own contract.
/// </summary>
public class KeyedAsyncLoaderTests
{
    [Fact]
    public void For_SameKey_ReturnsSameLoader()
    {
        var map = new KeyedAsyncLoader<string, int>(empty: 0);
        var a = map.For("x");
        var b = map.For("x");
        Assert.Same(a, b);
        Assert.Equal(1, map.Count);
    }

    [Fact]
    public void For_DifferentKeys_ReturnDifferentLoaders()
    {
        var map = new KeyedAsyncLoader<string, int>(empty: 0);
        Assert.NotSame(map.For("a"), map.For("b"));
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void ExceedingCapacity_EvictsLeastRecentlyUsed()
    {
        var evicted = new List<int>();
        var map = new KeyedAsyncLoader<string, int>(empty: -1, capacity: 2, onEvicted: evicted.Add);

        var a = map.For("a");
        var b = map.For("b");
        map.For("a");        // touch "a" so "b" is now the oldest
        map.For("c");        // over capacity -> evicts "b"

        // Assert before probing: For() on a dropped key recreates it, which would
        // itself evict something and confuse the counts.
        Assert.Equal(2, map.Count);
        Assert.Single(evicted);

        Assert.Same(a, map.For("a"));        // the touched slot survived, same loader
        Assert.NotSame(b, map.For("b"));     // "b" was dropped, so this is a new loader
    }

    [Fact]
    public void Eviction_HandsBackTheValueSoItCanBeDisposed()
    {
        var disposed = new List<string>();
        var map = new KeyedAsyncLoader<string, string>(empty: "", capacity: 1, onEvicted: disposed.Add);

        // Seed a value without going async: Load applies synchronously only via the
        // loader's own pipeline, so assert on the seeded empty value instead — the
        // contract under test is that eviction reports the slot's current value.
        map.For("first");
        map.For("second");   // evicts "first"

        Assert.Equal(1, map.Count);
        Assert.Single(disposed);
        Assert.Equal("", disposed[0]);
    }

    [Fact]
    public void JustRequestedSlot_IsNeverTheEvictionVictim()
    {
        var map = new KeyedAsyncLoader<string, int>(empty: 0, capacity: 1);
        var a = map.For("a");
        var b = map.For("b");   // "a" evicted, "b" must survive its own insertion

        Assert.Equal(1, map.Count);
        Assert.Same(b, map.For("b"));
        Assert.NotSame(a, map.For("a"));
    }

    [Fact]
    public void Remove_And_Clear_ReportEvictions()
    {
        var evicted = new List<int>();
        var map = new KeyedAsyncLoader<string, int>(empty: 7, capacity: 8, onEvicted: evicted.Add);
        map.For("a");
        map.For("b");

        Assert.True(map.Remove("a"));
        Assert.False(map.Remove("a"));
        Assert.Equal(1, map.Count);

        map.Clear();
        Assert.Equal(0, map.Count);
        Assert.Equal(2, evicted.Count);      // one from Remove, one from Clear
        Assert.All(evicted, v => Assert.Equal(7, v));
    }

    [Fact]
    public void Comparer_IsHonoured()
    {
        var map = new KeyedAsyncLoader<string, int>(empty: 0, comparer: StringComparer.OrdinalIgnoreCase);
        Assert.Same(map.For("Path/To/File"), map.For("path/to/file"));
        Assert.Equal(1, map.Count);
    }

    [Fact]
    public void Capacity_MustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeyedAsyncLoader<string, int>(0, capacity: 0));
    }
}
