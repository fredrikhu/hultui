using System.IO.Compression;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Adding to an existing archive — the write archives did not support. Reading out of one and
/// deleting members were already there; a copy aimed into one was refused outright.
/// </summary>
public class ArchiveAddTests : IDisposable
{
    private readonly string _dir;

    public ArchiveAddTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vcadd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Zip(string name, params (string Entry, string Text)[] entries)
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, text) in entries)
        {
            using var s = new StreamWriter(zip.CreateEntry(entry).Open());
            s.Write(text);
        }
        return path;
    }

    private string Source(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static List<string> EntriesOf(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        return [.. zip.Entries.Select(e => e.FullName).Order()];
    }

    private static string ReadEntry(string archive, string entry)
    {
        using var zip = ZipFile.OpenRead(archive);
        using var r = new StreamReader(zip.GetEntry(entry)!.Open());
        return r.ReadToEnd();
    }

    // ── AllMembers ───────────────────────────────────────────────────────
    /// <summary>The conflict check needs every depth, not the one level the pane shows: adding a
    /// folder brings members the pane never listed, and any of those can collide.</summary>
    [Fact]
    public void AllMembersReachesEveryDepth()
    {
        var archive = Zip("deep.zip", ("a.txt", "1"), ("t/b.txt", "2"), ("t/hi/c.txt", "3"));

        var members = ArchiveVfs.AllMembers(archive, null, sevenZip: false);

        Assert.NotNull(members);
        Assert.Equal(new[] { "a.txt", "t/b.txt", "t/hi/c.txt" }, members!.Keys.Order());
    }

    [Fact]
    public void AllMembersIsNullForAnArchiveThatIsNotThere() =>
        Assert.Null(ArchiveVfs.AllMembers(Path.Combine(_dir, "nope.zip"), null, sevenZip: false));

    /// <summary>Which backend writes is decided from the format and the password, in one place,
    /// because the UI has to predict it to know what kind of progress it will get.</summary>
    [Theory]
    [InlineData("a.zip", null, false)]
    [InlineData("a.zip", "secret", true)]     // encryption is beyond the BCL
    [InlineData("a.7z", null, true)]
    [InlineData("a.tar.gz", null, true)]
    public void TheWriterIsChosenByFormatAndPassword(string name, string? password, bool expected) =>
        Assert.Equal(expected, ArchiveVfs.WritesThroughSevenZip(name, password));

    // ── AddMembers ───────────────────────────────────────────────────────
    [Fact]
    public void AddingToTheRootKeepsWhatWasAlreadyThere()
    {
        var archive = Zip("bundle.zip", ("keep.txt", "precious"));
        var src = Source("new.txt", "fresh");

        var err = ArchiveVfs.AddMembers(archive, "", [(src, "new.txt")], null, sevenZip: false);

        Assert.Null(err);
        Assert.Equal(new[] { "keep.txt", "new.txt" }, EntriesOf(archive));
        Assert.Equal("precious", ReadEntry(archive, "keep.txt"));
        Assert.Equal("fresh", ReadEntry(archive, "new.txt"));
    }

    /// <summary>The whole point of the internal path: what you add lands in the folder you were
    /// looking at inside the archive, not at its root.</summary>
    [Fact]
    public void AddingInsideAFolderStoresItUnderThatFolder()
    {
        var archive = Zip("bundle.zip", ("textures/old.png", "OLD"));
        var src = Source("stone.png", "STONE");

        var err = ArchiveVfs.AddMembers(archive, "textures", [(src, "stone.png")], null, sevenZip: false);

        Assert.Null(err);
        Assert.Equal(new[] { "textures/old.png", "textures/stone.png" }, EntriesOf(archive));
    }

    /// <summary>A pane's internal path is backslash-separated; zip members are not. Mixing the
    /// two produces a member called "textures\stone.png" — one entry whose name merely looks
    /// like a path, which no other archive tool will read as being in a folder.</summary>
    [Fact]
    public void ABackslashInternalPathStillStoresAForwardSlashMember()
    {
        var archive = Zip("bundle.zip", ("a.txt", "1"));
        var src = Source("stone.png", "STONE");

        ArchiveVfs.AddMembers(archive, "textures\\hi_res", [(src, "stone.png")], null, sevenZip: false);

        Assert.Contains("textures/hi_res/stone.png", EntriesOf(archive));
    }

    [Fact]
    public void AddingOverAMemberReplacesItRatherThanDuplicatingIt()
    {
        var archive = Zip("bundle.zip", ("dup.txt", "old"), ("other.txt", "kept"));
        var src = Source("dup.txt", "new");

        var err = ArchiveVfs.AddMembers(archive, "", [(src, "dup.txt")], null, sevenZip: false);

        Assert.Null(err);
        Assert.Equal(new[] { "dup.txt", "other.txt" }, EntriesOf(archive));
        Assert.Equal("new", ReadEntry(archive, "dup.txt"));
        Assert.Equal("kept", ReadEntry(archive, "other.txt"));
    }

    /// <summary>Progress is reported the same way a pack reports it, so the overlay works for
    /// both without knowing which it is watching.</summary>
    [Fact]
    public void TheAddReportsItsEntriesAndBytes()
    {
        var archive = Zip("bundle.zip", ("keep.txt", "k"));
        var a = Source("a.txt", new string('x', 100));
        var b = Source("b.txt", new string('y', 40));
        var events = new List<string>();
        long bytes = 0;

        ArchiveVfs.AddMembers(archive, "", [(a, "a.txt"), (b, "b.txt")], null, sevenZip: false,
            new PackProgress
            {
                EntryStart = (i, len) => events.Add($"start:{i}:{len}"),
                Bytes = n => bytes += n,
                EntryDone = (i, ok) => events.Add($"done:{i}:{ok}"),
            });

        Assert.Equal(new[] { "start:0:100", "done:0:True", "start:1:40", "done:1:True" }, events);
        Assert.Equal(140, bytes);
    }

    [Fact]
    public void CancellingAnAddStopsItAndSaysSoDistinctly()
    {
        var archive = Zip("bundle.zip", ("keep.txt", "k"));
        var src = Source("a.txt", "aaaa");

        var err = ArchiveVfs.AddMembers(archive, "", [(src, "a.txt")], null, sevenZip: false,
            new PackProgress { Cancelled = () => true });

        Assert.Equal(ArchiveVfs.CancelledError, err);
        // The archive it was told to leave alone is still readable and still has its member.
        Assert.Equal(new[] { "keep.txt" }, EntriesOf(archive));
    }

    // ── refusals ─────────────────────────────────────────────────────────
    [Fact]
    public void RarIsRefusedEvenWithSevenZip()
    {
        var fake = Path.Combine(_dir, "x.rar");
        File.WriteAllText(fake, "not really a rar");

        var err = ArchiveVfs.AddMembers(fake, "", [(Source("a.txt", "a"), "a.txt")], null, sevenZip: true);

        Assert.Contains("read-only", err);
    }

    [Fact]
    public void AFormatNeedingSevenZipSaysSoWhenItIsNotConfigured()
    {
        var fake = Path.Combine(_dir, "x.7z");
        File.WriteAllText(fake, "not really a 7z");

        var err = ArchiveVfs.AddMembers(fake, "", [(Source("a.txt", "a"), "a.txt")], null, sevenZip: false);

        Assert.Contains("needs 7-Zip", err);
    }

    [Fact]
    public void AnArchiveThatIsNoLongerThereIsRefusedRatherThanCreated()
    {
        var missing = Path.Combine(_dir, "gone.zip");

        var err = ArchiveVfs.AddMembers(missing, "", [(Source("a.txt", "a"), "a.txt")], null, sevenZip: false);

        Assert.NotNull(err);
        Assert.False(File.Exists(missing), "an add conjured up an archive that was not there");
    }

    [Fact]
    public void AddingNothingIsNotAnError()
    {
        var archive = Zip("bundle.zip", ("keep.txt", "k"));

        Assert.Null(ArchiveVfs.AddMembers(archive, "", [], null, sevenZip: false));
        Assert.Equal(new[] { "keep.txt" }, EntriesOf(archive));
    }

    // ── the 7-Zip detour's command line ─────────────────────────────────
    // No 7-Zip on this machine, so what is asserted is the shape of what it would be told.

    /// <summary>7-Zip cannot remap an entry name while adding, so an add into a folder puts the
    /// sources in at the root and renames them into place. The pairs are member paths read out of
    /// an archive, so they must land after the "--" that ends switch parsing.</summary>
    [Fact]
    public void TheRenameThatMovesAddedItemsIntoPlacePassesPathsAsData()
    {
        var args = SevenZip.RenameArgs("out.7z", [("stone.png", "textures/stone.png")], null);

        Assert.Equal("rn", args[0]);
        int end = Array.IndexOf(args, "--");
        Assert.True(end > 0, "no -- to end switch parsing");
        Assert.True(Array.IndexOf(args, "stone.png") > end, "the from-path was left where a switch could be read");
        Assert.True(Array.IndexOf(args, "textures/stone.png") > end, "the to-path was left where a switch could be read");
    }

    [Fact]
    public void TheRenamePassesEveryPairInOrder()
    {
        var args = SevenZip.RenameArgs("out.7z", [("a", "t/a"), ("b", "t/b")], null);
        int end = Array.IndexOf(args, "--");

        // archive, then from/to, from/to.
        Assert.Equal(new[] { "out.7z", "a", "t/a", "b", "t/b" }, args[(end + 1)..]);
    }
}
