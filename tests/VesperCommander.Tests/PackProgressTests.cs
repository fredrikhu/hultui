using System.IO.Compression;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// What a pack reports while it runs, and what it leaves behind when it is stopped part-way.
/// Packing used to be a single opaque call behind a transient "Packing…" notice: there was
/// nothing to report progress to, and no way to stop it once started.
/// </summary>
public class PackProgressTests : IDisposable
{
    private readonly string _dir;

    public PackProgressTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vcpack_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A recording <see cref="PackProgress"/> — the overlay's side of the contract,
    /// reduced to what can be asserted.</summary>
    private sealed class Recorder
    {
        public readonly List<string> Events = [];
        public long Bytes;
        public bool Cancel;
        public int PauseCalls;
        public double LastPercent = -1;

        public PackProgress Progress => new()
        {
            EntryStart = (i, len) => Events.Add($"start:{i}:{len}"),
            Bytes = n => Bytes += n,
            EntryDone = (i, ok) => Events.Add($"done:{i}:{(ok ? "ok" : "missing")}"),
            Percent = p => LastPercent = p,
            Cancelled = () => Cancel,
            WaitWhilePaused = () => PauseCalls++,
        };
    }

    // ── the built-in zip writer ──────────────────────────────────────────
    [Fact]
    public void EveryEntryIsAnnouncedBeforeItIsWrittenAndAfter()
    {
        var a = Write("a.txt", "aaaa");
        var b = Write("b.txt", "bbbbbbbb");
        var rec = new Recorder();

        var err = ArchiveVfs.Create([(a, "a.txt"), (b, "b.txt")], Path.Combine(_dir, "out.zip"),
            CompressionLevel.Optimal, null, sevenZip: false, addToExisting: false, progress: rec.Progress);

        Assert.Null(err);
        Assert.Equal(new[] { "start:0:4", "done:0:ok", "start:1:8", "done:1:ok" }, rec.Events);
    }

    /// <summary>The byte deltas add up to the sources, which is what makes the total bar mean
    /// something. Reported from the read side, so compression can't skew them.</summary>
    [Fact]
    public void TheBytesReportedAddUpToWhatWasPacked()
    {
        var a = Write("a.txt", new string('x', 5000));
        var b = Write("b.txt", new string('y', 3000));
        var rec = new Recorder();

        ArchiveVfs.Create([(a, "a.txt"), (b, "b.txt")], Path.Combine(_dir, "out.zip"),
            CompressionLevel.Optimal, null, sevenZip: false, addToExisting: false, progress: rec.Progress);

        Assert.Equal(8000, rec.Bytes);
    }

    /// <summary>A source that went missing between the selection and the write is reported as
    /// left out, rather than passed over in silence — its queue row has to say something, and
    /// its bytes have to be credited or the total bar can never fill.</summary>
    [Fact]
    public void AVanishedSourceIsReportedRatherThanSkippedQuietly()
    {
        var a = Write("a.txt", "aaaa");
        var gone = Path.Combine(_dir, "gone.txt");
        var rec = new Recorder();

        var err = ArchiveVfs.Create([(a, "a.txt"), (gone, "gone.txt")], Path.Combine(_dir, "out.zip"),
            CompressionLevel.Optimal, null, sevenZip: false, addToExisting: false, progress: rec.Progress);

        Assert.Null(err);
        Assert.Equal(new[] { "start:0:4", "done:0:ok", "done:1:missing" }, rec.Events);
    }

    [Fact]
    public void CancellingStopsTheRunAndSaysSoDistinctlyFromAFailure()
    {
        var a = Write("a.txt", "aaaa");
        var b = Write("b.txt", "bbbb");
        var rec = new Recorder { Cancel = true };

        var err = ArchiveVfs.Create([(a, "a.txt"), (b, "b.txt")], Path.Combine(_dir, "out.zip"),
            CompressionLevel.Optimal, null, sevenZip: false, addToExisting: false, progress: rec.Progress);

        Assert.Equal(ArchiveVfs.CancelledError, err);
        Assert.Empty(rec.Events);
    }

    /// <summary>Cancel has to land inside a member, not only between them: a pack of one large
    /// file would otherwise be uncancellable, which is exactly the pack that needs it.</summary>
    [Fact]
    public void CancelTakesEffectPartWayThroughASingleLargeMember()
    {
        // Larger than the writer's chunk, so there is a second read to be stopped at.
        var big = Write("big.bin", new string('z', 512 * 1024));
        var rec = new Recorder();
        var progress = new PackProgress
        {
            EntryStart = (_, _) => { },
            // Stop once some of it has moved: cancellation between chunks, mid-entry.
            Bytes = n => { rec.Bytes += n; rec.Cancel = true; },
            Cancelled = () => rec.Cancel,
        };

        var err = ArchiveVfs.Create([(big, "big.bin")], Path.Combine(_dir, "out.zip"),
            CompressionLevel.Optimal, null, sevenZip: false, addToExisting: false, progress: progress);

        Assert.Equal(ArchiveVfs.CancelledError, err);
        Assert.True(rec.Bytes > 0, "nothing was written before the cancel");
        Assert.True(rec.Bytes < 512 * 1024, "the whole member went in despite the cancel");
    }

    /// <summary>Pause is consulted, and in the same places as cancel — a pack held during a
    /// large member stops there rather than at the next entry boundary.</summary>
    [Fact]
    public void PauseIsConsultedInsideAMemberAsWellAsBetweenThem()
    {
        var big = Write("big.bin", new string('z', 512 * 1024));
        var rec = new Recorder();

        ArchiveVfs.Create([(big, "big.bin")], Path.Combine(_dir, "out.zip"),
            CompressionLevel.Optimal, null, sevenZip: false, addToExisting: false, progress: rec.Progress);

        // One for the entry boundary plus one per chunk of a 512 KB file at 128 KB a chunk.
        Assert.True(rec.PauseCalls > 1, $"pause was only checked {rec.PauseCalls} time(s)");
    }

    /// <summary>Streaming the entries by hand replaced <c>CreateEntryFromFile</c>, which set the
    /// stored timestamp for us. It still has to be set, and a date outside the zip format's
    /// 1980 epoch has to be clamped instead of throwing the whole pack away.</summary>
    [Fact]
    public void StoredTimestampsSurviveAndAnImpossibleOneIsClamped()
    {
        var normal = Write("normal.txt", "n");
        var ancient = Write("ancient.txt", "a");
        var when = new DateTime(2021, 3, 4, 5, 6, 7);
        File.SetLastWriteTime(normal, when);
        // Before zip's 1980 epoch but still a storable Win32 file time — the range where the
        // filesystem is happy and the archive format is not.
        File.SetLastWriteTime(ancient, new DateTime(1970, 1, 1, 12, 0, 0));

        var outZip = Path.Combine(_dir, "stamped.zip");
        var err = ArchiveVfs.Create([(normal, "normal.txt"), (ancient, "ancient.txt")], outZip,
            CompressionLevel.Optimal, null, sevenZip: false);

        Assert.Null(err);
        using var zip = ZipFile.OpenRead(outZip);
        // Zip stores DOS timestamps, which are good to two seconds — so this is "the mtime came
        // through", not an exact-equality check the format could never satisfy.
        var stored = zip.GetEntry("normal.txt")!.LastWriteTime.DateTime;
        Assert.True((stored - when).Duration() <= TimeSpan.FromSeconds(2), $"stored {stored}, expected ~{when}");
        Assert.Equal(1980, zip.GetEntry("ancient.txt")!.LastWriteTime.Year);
    }

    /// <summary>Progress is optional: the engine still packs for a caller that passes none.</summary>
    [Fact]
    public void PackingWithNobodyWatchingStillWorks()
    {
        var a = Write("a.txt", "aaaa");
        var outZip = Path.Combine(_dir, "quiet.zip");

        Assert.Null(ArchiveVfs.Create([(a, "a.txt")], outZip, CompressionLevel.Optimal, null, sevenZip: false));
        using var zip = ZipFile.OpenRead(outZip);
        Assert.Equal("a.txt", Assert.Single(zip.Entries).FullName);
    }

    // ── 7-Zip's progress output ──────────────────────────────────────────
    // This machine has no 7-Zip to run, so the parsing is tested against recorded output —
    // the same reason SevenZip.CreateArgs is asserted rather than executed.

    [Fact]
    public void ProgressSwitchesAreOnlyAddedWhenSomeoneIsWatching()
    {
        var items = new[] { ("C:\\src\\a.txt", "a.txt") };
        var quiet = SevenZip.CreateArgs(items, "out.7z", CompressionLevel.Optimal, null);
        var watched = SevenZip.CreateArgs(items, "out.7z", CompressionLevel.Optimal, null, reportProgress: true);

        Assert.DoesNotContain("-bsp1", quiet);
        Assert.Contains("-bsp1", watched);
        Assert.Contains("-bb1", watched);
        // Still switches, so still before the "--" that ends switch parsing.
        Assert.True(Array.IndexOf(watched, "-bsp1") < Array.IndexOf(watched, "--"));
        Assert.True(Array.IndexOf(watched, "-bb1") < Array.IndexOf(watched, "--"));
    }

    [Theory]
    [InlineData(" 45% 12 + dir/file.txt", 45)]
    [InlineData("  7%", 7)]
    [InlineData("100%", 100)]
    [InlineData("+ dir/file.txt", null)]
    [InlineData("Everything is Ok", null)]
    [InlineData("", null)]
    public void ThePercentageIsReadOffAProgressToken(string token, int? expected) =>
        Assert.Equal(expected, SevenZip.PackScan.Percent(token));

    [Theory]
    [InlineData("+ dir/file.txt", "dir/file.txt")]
    [InlineData(" 45% 12 + dir/file.txt", "dir/file.txt")]
    [InlineData("U existing.txt", "existing.txt")]
    [InlineData("Scanning the drive:", null)]
    [InlineData(" 45%", null)]
    public void TheAddedFileIsReadOffALogToken(string token, string? expected) =>
        Assert.Equal(expected, SevenZip.PackScan.LoggedPath(token));

    /// <summary>A '+' inside a name is not the start of a log entry — the marker is a word of
    /// its own.</summary>
    [Fact]
    public void APlusInsideAFilenameIsNotReadAsALogMarker() =>
        Assert.Equal("notes.txt", SevenZip.PackScan.LoggedPath("+ notes.txt"));

    /// <summary>The stream is consumed in fixed-size reads that land wherever they land, so a
    /// token split across two of them still has to be understood exactly once.</summary>
    [Fact]
    public void ATokenSplitAcrossTwoReadsIsStillUnderstood()
    {
        var items = new[] { ("C:\\src\\a.txt", "a.txt"), ("C:\\src\\b.txt", "b.txt") };
        var scan = new SevenZip.PackScan(items);
        var events = new List<string>();
        double percent = -1;
        var progress = new PackProgress
        {
            EntryStart = (i, _) => events.Add($"start:{i}"),
            EntryDone = (i, _) => events.Add($"done:{i}"),
            Percent = p => percent = p,
        };

        scan.Feed(" 20% + a.tx", progress);      // cut mid-name, mid-token
        Assert.Empty(events);                     // nothing acted on half a name
        scan.Feed("t\r 60% + b.txt\r", progress);
        scan.Finish(progress);

        Assert.Equal(new[] { "start:0", "done:0", "start:1", "done:1" }, events);
        Assert.Equal(100, percent);
    }

    /// <summary>7-Zip logs the <em>stored</em> path, so a file from inside a packed folder
    /// arrives as "folder\sub\file" while the item that produced it is the folder. Without the
    /// first-segment match, every file in a packed folder would map to nothing and the queue
    /// would never move.</summary>
    [Fact]
    public void AFileLoggedFromInsideAPackedFolderMapsToThatFolder()
    {
        var items = new[] { ("C:\\src\\assets", "assets"), ("C:\\src\\notes.txt", "notes.txt") };
        var scan = new SevenZip.PackScan(items);
        var events = new List<string>();
        var progress = new PackProgress
        {
            EntryStart = (i, _) => events.Add($"start:{i}"),
            EntryDone = (i, _) => events.Add($"done:{i}"),
        };

        scan.Feed("+ assets\\textures\\stone.png\r", progress);
        scan.Feed("+ assets\\textures\\wood.png\r", progress);   // same folder — same row
        scan.Feed("+ notes.txt\r", progress);
        scan.Finish(progress);

        Assert.Equal(new[] { "start:0", "done:0", "start:1", "done:1" }, events);
    }
}
