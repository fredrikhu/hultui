using Meridian.Core;
using Xunit;

namespace Meridian.Tests;

public class CollectionWatcherTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-watch-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void WritingAReqFileFiresOneDebouncedCallback()
    {
        using var fired = new ManualResetEventSlim();
        int count = 0;
        using var watcher = new CollectionWatcher(_root, () => { Interlocked.Increment(ref count); fired.Set(); }, debounceMs: 80);

        // A burst of writes must coalesce into one callback.
        for (int i = 0; i < 5; i++)
            File.WriteAllText(Path.Combine(_root, "new.req"), $"GET https://x/{i}\n");

        Assert.True(fired.Wait(TimeSpan.FromSeconds(10)), "the watcher never fired");
        // Give a would-be second callback time to arrive, then insist there wasn't one.
        Thread.Sleep(300);
        Assert.Equal(1, count);
    }

    [Fact]
    public void AnIrrelevantFileDoesNotFire()
    {
        using var fired = new ManualResetEventSlim();
        using var watcher = new CollectionWatcher(_root, fired.Set, debounceMs: 40);

        File.WriteAllText(Path.Combine(_root, "notes.txt"), "not collection content");

        Assert.False(fired.Wait(500), "a .txt write should not refresh the collection");
    }

    // The path filter is pure — test the matrix directly rather than racing the OS.
    [Theory]
    [InlineData(@"work\api\get.req", true)]
    [InlineData(@"work\envs\local.env", true)]
    [InlineData(@"work\api", true)]                      // directory event: reshape
    [InlineData(@"work\notes.txt", false)]
    [InlineData(@"work\.git\objects\ab.req", false)]
    [InlineData(@"work\.meridian\history.log", false)]
    public void RelevanceFiltersPathsAndDotDirectories(string relative, bool expected)
    {
        var full = Path.Combine("C:", relative.Replace('\\', Path.DirectorySeparatorChar));
        Assert.Equal(expected, CollectionWatcher.Relevant(full));
    }
}
