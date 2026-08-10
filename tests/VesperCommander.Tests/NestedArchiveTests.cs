using System.IO.Compression;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Browsing an archive inside an archive unpacks the inner one to <c>%TEMP%\vesper-nested</c>
/// and caches where it put it. Both panes list on their own background threads, so two of them
/// can arrive at the same nested member at the same moment — which used to mean two unguarded
/// writes to a plain Dictionary and two copies of the same file, one of them orphaned in
/// %TEMP% until the app exited.
/// </summary>
public class NestedArchiveTests : IDisposable
{
    private readonly string _dir;
    private readonly string _outer;

    public NestedArchiveTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vcnest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        // A megabyte of payload, so the copy takes long enough for concurrent callers to be
        // inside it together. With a few bytes they can finish one after another and the race
        // never opens.
        var innerPath = Path.Combine(_dir, "inner.zip");
        using (var inner = ZipFile.Open(innerPath, ZipArchiveMode.Create))
        {
            var e = inner.CreateEntry("hello.txt", CompressionLevel.NoCompression);
            using var s = e.Open();
            s.Write(new byte[1024 * 1024]);
        }

        _outer = Path.Combine(_dir, "outer.zip");
        using (var outer = ZipFile.Open(_outer, ZipArchiveMode.Create))
            outer.CreateEntryFromFile(innerPath, "inner.zip", CompressionLevel.NoCompression);

        // The cache and its folder are process-wide. Tests in this assembly don't run in
        // parallel, so clearing puts the count below on a known floor.
        ArchiveVfs.CleanupTemp();
    }

    public void Dispose()
    {
        ArchiveVfs.CleanupTemp();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void ANestedArchiveListsWhatIsInsideIt()
    {
        var listing = ArchiveVfs.ListEntries(_outer, "inner.zip", null, sevenZip: false);

        Assert.Equal(ArchiveStatus.Ok, listing.Status);
        Assert.Contains(listing.Entries, e => e.Name == "hello.txt");
    }

    /// <summary>Eight threads onto the same nested member at once. The listings all have to
    /// come back whole, and the member has to have been unpacked once — a second copy means
    /// two threads were both past the cache check, which is also when the dictionary itself
    /// was being written from two places at once.</summary>
    [Fact]
    public void ResolvingTheSameNestedArchiveFromEveryPaneAtOnceUnpacksItOnce()
    {
        const int threads = 8;
        using var gate = new Barrier(threads);
        var listings = new ArchiveListing[threads];

        var workers = new Thread[threads];
        for (int i = 0; i < threads; i++)
        {
            int slot = i;
            workers[i] = new Thread(() =>
            {
                gate.SignalAndWait();
                listings[slot] = ArchiveVfs.ListEntries(_outer, "inner.zip", null, sevenZip: false);
            });
            workers[i].Start();
        }
        foreach (var w in workers) Assert.True(w.Join(TimeSpan.FromSeconds(30)), "a listing thread never finished");

        foreach (var listing in listings)
        {
            Assert.Equal(ArchiveStatus.Ok, listing.Status);
            Assert.Contains(listing.Entries, e => e.Name == "hello.txt");
        }

        var unpacked = Directory.GetFiles(ArchiveVfs.NestedTempDir);
        Assert.Single(unpacked);
    }
}
