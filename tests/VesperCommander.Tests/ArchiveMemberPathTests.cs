using System.IO.Compression;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// The paths this VFS hands around are not the paths a zip stored. Every listing runs a member
/// name through <see cref="ArchivePath.SafeMemberPath"/> first, which folds "." segments away
/// and turns every separator into "/" — so a zip that stored <c>.\notes\a.txt</c> is browsed as
/// <c>notes/a.txt</c>. Reading a member back used to go through <c>ZipArchive.GetEntry</c>,
/// which matches the *stored* name character for character, and so found nothing.
///
/// <para>Both shapes below are written by real zippers — the backslash form by older Windows
/// tools and by anything that hands a Windows path to a zip writer, the "./" form by tar-to-zip
/// conversions. The listing showed them, the extraction silently skipped them one member at a
/// time (reported per item as "archive member unreadable"), and because a folder's extraction
/// stops at its first failing member, everything after one in the same folder went missing too.
/// That is what "unzip missed some files" looks like from the outside.</para>
/// </summary>
public class ArchiveMemberPathTests : IDisposable
{
    private readonly string _dir;

    public ArchiveMemberPathTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vcmember_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        ArchiveVfs.CleanupTemp();
    }

    public void Dispose()
    {
        ArchiveVfs.CleanupTemp();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Zip(string name, params (string Entry, string Body)[] members)
    {
        var path = Path.Combine(_dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, body) in members)
        {
            using var w = new StreamWriter(zip.CreateEntry(entry).Open());
            w.Write(body);
        }
        return path;
    }

    private string Out([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var dir = Path.Combine(_dir, "out-" + name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Extract everything at the archive root the way F5 does — list, then hand each
    /// non-parent row to the transfer body — and return the paths that landed, relative to the
    /// destination.</summary>
    private List<string> ExtractAll(string archive, string dest)
    {
        var listing = ArchiveVfs.ListEntries(archive, "", null, sevenZip: false);
        foreach (var e in listing.Entries.Where(x => !x.IsParent))
        {
            var err = FileOps.CopyStreamed(e, dest, overwrite: true, _ => { }, () => false);
            Assert.Null(err);
        }
        return Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories)
            .Select(f => f[(dest.Length + 1)..].Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void MembersStoredWithBackslashesExtract()
    {
        var zip = Zip("back.zip", (@"notes\deep\a.txt", "A"), ("plain.txt", "P"));
        var landed = ExtractAll(zip, Out());
        Assert.Equal(["notes/deep/a.txt", "plain.txt"], landed);
    }

    [Fact]
    public void MembersStoredWithADotSegmentExtract()
    {
        var zip = Zip("dot.zip", ("./notes/a.txt", "A"), ("./b.txt", "B"));
        var landed = ExtractAll(zip, Out());
        Assert.Equal(["b.txt", "notes/a.txt"], landed);
    }

    /// <summary>One unreadable member used to end its folder's extraction, so the count that
    /// went missing was never one file — it was everything listed after it.</summary>
    [Fact]
    public void OneOddlyStoredMemberDoesNotStopTheRestOfItsFolder()
    {
        var zip = Zip("mixed.zip",
            (@"folder\odd.txt", "O"), ("folder/plain-a.txt", "A"), ("folder/plain-b.txt", "B"));
        var landed = ExtractAll(zip, Out());
        Assert.Equal(["folder/odd.txt", "folder/plain-a.txt", "folder/plain-b.txt"], landed);
    }

    /// <summary>A zip inside a zip is two different things depending on what you ask for. The
    /// nested walk hops into it for a *listing* — that is what browsing zip-in-zip means — but
    /// a read wants the member's own bytes. Hopping there unpacked it to temp and then looked
    /// for a member called "", so extracting an archive out of an archive failed outright.</summary>
    [Fact]
    public void AnArchiveStoredInsideAnArchiveExtractsAsAFile()
    {
        var inner = Zip("inner.zip", ("hello.txt", "HELLO"));
        var outer = Path.Combine(_dir, "outer.zip");
        using (var zip = ZipFile.Open(outer, ZipArchiveMode.Create))
            zip.CreateEntryFromFile(inner, "inner.zip");

        var dest = Out();
        Assert.Equal(["inner.zip"], ExtractAll(outer, dest));

        // Intact, not truncated or re-wrapped: it still opens as the archive it was.
        using var landed = ZipFile.OpenRead(Path.Combine(dest, "inner.zip"));
        using var r = new StreamReader(landed.GetEntry("hello.txt")!.Open());
        Assert.Equal("HELLO", r.ReadToEnd());
    }

    /// <summary>The other half of that behavior, which must not have been traded away for it:
    /// navigating *into* the nested archive still lists what is inside.</summary>
    [Fact]
    public void BrowsingIntoANestedArchiveStillListsItsContents()
    {
        var inner = Zip("inner.zip", ("hello.txt", "HELLO"));
        var outer = Path.Combine(_dir, "outer.zip");
        using (var zip = ZipFile.Open(outer, ZipArchiveMode.Create))
            zip.CreateEntryFromFile(inner, "inner.zip");

        var listing = ArchiveVfs.ListEntries(outer, "inner.zip", null, sevenZip: false);
        Assert.Equal(ArchiveStatus.Ok, listing.Status);
        Assert.Contains(listing.Entries, e => e.Name == "hello.txt");
    }

    /// <summary>Deleting matched on a separator swap alone, so a member the pane showed as
    /// "notes/a.txt" was not found when the archive had stored "./notes/a.txt".</summary>
    [Fact]
    public void DeletingFindsAMemberStoredWithADotSegment()
    {
        var zip = Zip("del.zip", ("./notes/a.txt", "A"), ("keep.txt", "K"));
        Assert.Null(ArchiveVfs.DeleteMembers(zip, ["notes/a.txt"], null, sevenZip: false));

        var listing = ArchiveVfs.ListEntries(zip, "", null, sevenZip: false);
        Assert.DoesNotContain(listing.Entries, e => e.Name == "notes");
        Assert.Contains(listing.Entries, e => e.Name == "keep.txt");
    }

    /// <summary>Long names were the first suspect and are not the fault — but they are cheap to
    /// hold still, and the destination here runs well past the old 260-character ceiling.</summary>
    [Fact]
    public void LongMemberNamesExtract()
    {
        string longName = new string('n', 180) + ".txt";
        string deep = string.Join('/', Enumerable.Repeat(new string('d', 40), 4)) + "/" + longName;
        var zip = Zip("long.zip", (longName, "L"), (deep, "D"));

        var dest = Out();
        var landed = ExtractAll(zip, dest);
        Assert.Equal(2, landed.Count);
        Assert.Contains(landed, f => Path.Combine(dest, f).Length > 260);
    }
}
