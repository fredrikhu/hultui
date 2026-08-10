using System.IO.Compression;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Archives that are hostile rather than merely malformed: a member whose stored path walks
/// out of the folder it is extracted into, a member name 7-Zip would read as a switch, and the
/// Pack dialog's "Add to it" replacing the archive it offered to add to.
/// </summary>
public class ArchiveSafetyTests
{
    private static string Scratch([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var dir = Path.Combine(Path.GetTempPath(), "vesper-hostile", name);
        try { Directory.Delete(dir, true); } catch { }
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string MakeZip(string dir, string name, params (string Entry, string Body)[] members)
    {
        var path = Path.Combine(dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, body) in members)
        {
            using var w = new StreamWriter(zip.CreateEntry(entry).Open());
            w.Write(body);
        }
        return path;
    }
    /// <summary>
    /// The end-to-end case, run exactly as Alt+F9 runs it: list the archive root, take every
    /// non-parent member, hand each to the transfer body. <c>ZipFile.ExtractToDirectory</c>
    /// refuses this archive; Vesper never reaches that guard because it enumerates members and
    /// joins paths itself, so before the fix the payload landed two levels up.
    /// </summary>
    [Fact]
    public void ATraversingMemberIsNeverWrittenOutsideTheDestination()
    {
        var root = Scratch();
        var zip = MakeZip(root, "slip.zip",
            ("readme.txt", "ordinary"),
            ("../../ESCAPED.txt", "owned"));

        var dest = Path.Combine(root, "out", "nested");
        Directory.CreateDirectory(dest);

        foreach (var member in ArchiveVfs.ListEntries(zip, "", null, sevenZip: false)
                     .Entries.Where(e => !e.IsParent))
            FileOps.CopyStreamed(member, dest, overwrite: true, _ => { }, () => false);

        // Two "..", so before the fix it landed two levels above the destination.
        Assert.False(File.Exists(Path.Combine(root, "ESCAPED.txt")), "the payload escaped the destination");
        Assert.False(File.Exists(Path.Combine(root, "out", "ESCAPED.txt")));
        // …and the ordinary member still extracts, or the fix would just be "refuse archives".
        Assert.True(File.Exists(Path.Combine(dest, "readme.txt")));
    }

    /// <summary>The traversing member never becomes a row, so it can't be marked, opened, or
    /// swept into a transfer — and the phantom second ".." it used to add is gone.</summary>
    [Fact]
    public void ATraversingMemberIsNotListedAndIsCounted()
    {
        var root = Scratch();
        var zip = MakeZip(root, "slip.zip", ("ok.txt", "fine"), ("../../ESCAPED.txt", "owned"));

        var listing = ArchiveVfs.ListEntries(zip, "", null, sevenZip: false);
        Assert.Equal(1, listing.Unsafe);
        Assert.Single(listing.Entries, e => e.IsParent);   // exactly one, the real one
        Assert.DoesNotContain(listing.Entries, e => !e.IsParent && e.Name == "..");
        Assert.Contains(listing.Entries, e => e.Name == "ok.txt");
    }

    /// <summary>The other escape shape: a stored path with a drive on it. The drive-relative
    /// form is the nastier one — it resolves against the current directory of that drive.</summary>
    [Theory]
    [InlineData("C:/rooted.txt")]
    [InlineData("C:rooted.txt")]
    [InlineData("/etc/rooted.txt")]
    [InlineData("//server/share/rooted.txt")]
    [InlineData(@"..\..\backslash.txt")]
    [InlineData("nested/../../escape.txt")]
    public void RootedAndTraversingMemberPathsAreRefused(string stored)
    {
        Assert.Null(ArchivePath.SafeMemberPath(stored));
    }

    /// <summary>Ordinary members have to survive, including the "./" prefix that real tar
    /// archives carry and the doubled slashes some writers emit.</summary>
    [Theory]
    [InlineData("readme.txt", "readme.txt")]
    [InlineData("sub/readme.txt", "sub/readme.txt")]
    [InlineData(@"sub\readme.txt", "sub/readme.txt")]
    [InlineData("./readme.txt", "readme.txt")]
    [InlineData("./sub//readme.txt", "sub/readme.txt")]
    [InlineData("sub/", "sub/")]
    [InlineData("a..b/re..adme.txt", "a..b/re..adme.txt")]
    public void OrdinaryMemberPathsAreKept(string stored, string expected)
    {
        Assert.Equal(expected, ArchivePath.SafeMemberPath(stored));
    }

    /// <summary>
    /// The backstop at its call site, reached the only way it now can be: by handing the
    /// transfer body an entry the archive reader would never produce. Without this the two
    /// layers can't be told apart — the listing filter alone keeps the end-to-end test green,
    /// so deleting the containment checks would go unnoticed.
    /// </summary>
    [Fact]
    public void TheTransferBodyRefusesAnEscapingEntryEvenIfOneReachesIt()
    {
        var root = Scratch();
        var source = Path.Combine(root, "payload.txt");
        File.WriteAllText(source, "owned");
        var dest = Path.Combine(root, "out", "nested");
        Directory.CreateDirectory(dest);

        var escaping = new FileEntry(
            Name: Path.Combine("..", "..", "ESCAPED.txt"), FullPath: source,
            IsDir: false, Size: 5, Modified: DateTime.Now);

        Assert.NotNull(FileOps.CopyStreamed(escaping, dest, true, _ => { }, () => false));
        Assert.NotNull(FileOps.Copy(escaping, dest, overwrite: true));
        Assert.NotNull(FileOps.Move(escaping, dest, overwrite: true));
        Assert.False(File.Exists(Path.Combine(root, "ESCAPED.txt")));
        Assert.False(File.Exists(Path.Combine(root, "out", "ESCAPED.txt")));
    }

    /// <summary>The write-time backstop, on its own. The listing filter is the fix; this is
    /// what stops a future caller reopening the hole from another direction.</summary>
    [Fact]
    public void ContainedPathRefusesAnythingLeavingTheDestination()
    {
        var root = Scratch();
        var dest = Path.Combine(root, "dest");
        Directory.CreateDirectory(dest);

        Assert.Null(FileOps.ContainedPath(dest, ".." + Path.DirectorySeparatorChar + "out.txt"));
        Assert.Null(FileOps.ContainedPath(dest, Path.Combine("a", "..", "..", "out.txt")));
        Assert.Null(FileOps.ContainedPath(dest, @"C:\windows\out.txt"));
        // A sibling whose name merely starts with the destination's — the prefix test has to
        // compare whole path segments, not characters.
        Assert.Null(FileOps.ContainedPath(dest, Path.Combine("..", "dest-evil", "out.txt")));

        // And the shapes that must keep working: a plain name, and the relative path the sync
        // commit deliberately passes as an entry name.
        Assert.Equal(Path.Combine(dest, "out.txt"), FileOps.ContainedPath(dest, "out.txt"));
        Assert.Equal(Path.Combine(dest, "a", "b", "out.txt"),
            FileOps.ContainedPath(dest, Path.Combine("a", "b", "out.txt")));
    }

    // ── 7-Zip argument injection ─────────────────────────────────────────
    //
    // Asserted on the argument array rather than by running 7-Zip: this machine has no 7-Zip,
    // and the property that matters is ours anyway — that nothing an archive controls is
    // placed where 7-Zip would read it as a switch. That "--" ends switch parsing is 7-Zip's
    // documented behaviour, not something these tests can prove.

    /// <summary>A member path is read straight out of the archive, so an archive holding a
    /// member called "-y" would otherwise be picking 7-Zip's switches for us.</summary>
    [Fact]
    public void AMemberPathIsPassedAfterTheSwitchTerminator()
    {
        var args = SevenZip.ExtractArgs(@"C:\tmp\a.7z", "-y", password: null);
        int terminator = Array.IndexOf(args, "--");
        int member = Array.LastIndexOf(args, "-y");

        Assert.True(terminator >= 0, $"no switch terminator in [{string.Join(' ', args)}]");
        Assert.True(member > terminator,
            $"the member path is still parseable as a switch: [{string.Join(' ', args)}]");
        // The archive path is data too, and has to be on the same side of the fence.
        Assert.True(Array.IndexOf(args, @"C:\tmp\a.7z") > terminator);
    }

    /// <summary>The same for the other two command lines. A listing reads an archive path; a
    /// pack writes one the user typed, which can equally begin with a dash.</summary>
    [Fact]
    public void EveryCommandLinePutsItsPathsAfterTheTerminator()
    {
        var list = SevenZip.ListArgs("-weird-archive.7z", password: null);
        Assert.True(Array.IndexOf(list, "-weird-archive.7z") > Array.IndexOf(list, "--"));

        var create = SevenZip.CreateArgs(
            [(@"C:\tmp\-dashed.txt", "-dashed.txt")], "-out.7z",
            CompressionLevel.Optimal, password: null);
        int terminator = Array.IndexOf(create, "--");
        Assert.True(terminator >= 0);
        Assert.True(Array.IndexOf(create, "-out.7z") > terminator);
        Assert.True(Array.IndexOf(create, @"C:\tmp\-dashed.txt") > terminator);
        // The real switches still have to precede it, or 7-Zip would take them as file names.
        Assert.True(Array.IndexOf(create, "-mx=6") < terminator);
    }

    /// <summary>The password is a switch (<c>-p…</c>) and must stay on the switch side — this
    /// is the check that stops someone "fixing" the terminator by putting it first.</summary>
    [Fact]
    public void ThePasswordStaysOnTheSwitchSide()
    {
        var args = SevenZip.ExtractArgs(@"C:\tmp\a.7z", "inner.txt", "hunter2");
        Assert.True(Array.IndexOf(args, "-phunter2") < Array.IndexOf(args, "--"));
    }

    // ── Pack: "Add to it" ────────────────────────────────────────────────

    /// <summary>The Pack dialog offers "Add to it" when the archive already exists. The
    /// built-in zip writer replaced the archive regardless, so the button deleted the very
    /// thing it offered to add to.</summary>
    [Fact]
    public void AddingToAnExistingZipKeepsWhatWasAlreadyInIt()
    {
        var root = Scratch();
        var archive = MakeZip(root, "bundle.zip", ("keepme.txt", "precious"));
        var source = Path.Combine(root, "new.txt");
        File.WriteAllText(source, "added");

        var err = ArchiveVfs.Create([(source, "new.txt")], archive,
            CompressionLevel.Optimal, password: null, sevenZip: false, addToExisting: true);
        Assert.Null(err);

        using var zip = ZipFile.OpenRead(archive);
        Assert.Contains(zip.Entries, e => e.FullName == "keepme.txt");
        Assert.Contains(zip.Entries, e => e.FullName == "new.txt");
    }

    /// <summary>Adding the same name again replaces that entry rather than leaving the archive
    /// with two of it — Update mode keeps what is there, so the old one has to go explicitly.</summary>
    [Fact]
    public void AddingAnEntryThatAlreadyExistsReplacesItRatherThanDuplicating()
    {
        var root = Scratch();
        var archive = MakeZip(root, "bundle.zip", ("dup.txt", "old"), ("other.txt", "kept"));
        var source = Path.Combine(root, "dup.txt");
        File.WriteAllText(source, "new");

        Assert.Null(ArchiveVfs.Create([(source, "dup.txt")], archive,
            CompressionLevel.Optimal, null, sevenZip: false, addToExisting: true));

        using var zip = ZipFile.OpenRead(archive);
        Assert.Single(zip.Entries, e => e.FullName == "dup.txt");
        Assert.Contains(zip.Entries, e => e.FullName == "other.txt");
        using var reader = new StreamReader(zip.GetEntry("dup.txt")!.Open());
        Assert.Equal("new", reader.ReadToEnd());
    }

    /// <summary>Without the flag it still means create: packing over an existing archive
    /// replaces it, which is what every other path in the dialog assumes.</summary>
    [Fact]
    public void PackingWithoutAddingStillReplacesTheArchive()
    {
        var root = Scratch();
        var archive = MakeZip(root, "bundle.zip", ("gone.txt", "old"));
        var source = Path.Combine(root, "new.txt");
        File.WriteAllText(source, "added");

        Assert.Null(ArchiveVfs.Create([(source, "new.txt")], archive,
            CompressionLevel.Optimal, null, sevenZip: false, addToExisting: false));

        using var zip = ZipFile.OpenRead(archive);
        Assert.DoesNotContain(zip.Entries, e => e.FullName == "gone.txt");
        Assert.Single(zip.Entries);
    }
}
