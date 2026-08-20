using HultUI.Core.Events;
using System.IO.Compression;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// F7 inside an archive. A zip has no directories: a folder in one is either implied by a
/// member's path or spelled out by a zero-length entry whose name ends in "/". An empty folder
/// has no member to imply it, so the marker is the only way to say it is there — which is why
/// this needs a write of its own rather than falling out of the add path.
/// </summary>
public class ArchiveFolderTests
{
    private static string MakeZip(string dir, string name, params string[] entries)
    {
        var path = Path.Combine(dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var e in entries)
            using (var w = new StreamWriter(zip.CreateEntry(e).Open())) w.Write("x");
        return path;
    }

    private static string[] Entries(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        return zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }

    // ── The write itself ─────────────────────────────────────────────────
    [Fact]
    public void AFolderIsStoredAsADirectoryMarker()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vcfolder_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var zip = MakeZip(dir, "a.zip", "keep.txt");

            Assert.Null(ArchiveVfs.CreateFolder(zip, "", "notes", null, sevenZip: false));

            Assert.Equal(["keep.txt", "notes/"], Entries(zip));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>And the listing reads it back as a folder — otherwise it would be created and
    /// then invisible, which is worse than refusing.</summary>
    [Fact]
    public void TheNewFolderShowsInTheListing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vcfolder_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var zip = MakeZip(dir, "a.zip", "keep.txt");
            ArchiveVfs.CreateFolder(zip, "", "notes", null, sevenZip: false);

            var listing = ArchiveVfs.ListEntries(zip, "", null, sevenZip: false);

            Assert.Contains(listing.Entries, e => e.Name == "notes" && e.IsDir);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void AFolderInsideAFolderIsStoredUnderIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vcfolder_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var zip = MakeZip(dir, "a.zip", "docs/keep.txt");

            Assert.Null(ArchiveVfs.CreateFolder(zip, "docs", "draft", null, sevenZip: false));

            Assert.Contains("docs/draft/", Entries(zip));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void AnExistingNameIsRefused()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vcfolder_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var zip = MakeZip(dir, "a.zip", "docs/keep.txt", "taken.txt");

            Assert.NotNull(ArchiveVfs.CreateFolder(zip, "", "docs", null, sevenZip: false));
            Assert.NotNull(ArchiveVfs.CreateFolder(zip, "", "taken.txt", null, sevenZip: false));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>A name is checked on its own before it is joined, so ".." in it is refused
    /// rather than quietly resolving against the folder the pane is showing — the same rule
    /// that keeps a hostile member from being extracted outside its destination.</summary>
    [Theory]
    [InlineData("../escape")]
    [InlineData(@"..\escape")]
    [InlineData("/rooted")]
    [InlineData(@"C:\rooted")]
    [InlineData("   ")]
    public void ANameThatIsNotARelativeFolderIsRefused(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "vcfolder_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var zip = MakeZip(dir, "a.zip", "keep.txt");

            Assert.NotNull(ArchiveVfs.CreateFolder(zip, "docs", name, null, sevenZip: false));
            Assert.Equal(["keep.txt"], Entries(zip));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ── Through the app ──────────────────────────────────────────────────
    /// <summary>F7 used to be refused outright inside an archive. Driven end to end: the real
    /// chord, the real prompt, the real Enter.</summary>
    [Fact]
    public void F7InsideAnArchiveCreatesTheFolder()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = MakeZip(t.Left, "assets.zip", "keep.txt");
        t.Open();

        t.PutCursorOn('L', "assets.zip");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "keep.txt"), TimeSpan.FromSeconds(10));

        t.Press(KeyCode.F7);
        t.Session.Window.TypeText("notes");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "notes"), TimeSpan.FromSeconds(10));

        Assert.Contains("notes/", Entries(zip));
        Assert.Null(t.Field<object?>("_namePrompt"));    // the dialog closed on success
    }

    /// <summary>The pane shows a temp extraction of a nested archive, so a write there would
    /// go into the copy and leave the real archive untouched. Refused, with a reason.</summary>
    [Fact]
    public void F7InsideANestedArchiveIsRefusedWithAReason()
    {
        using var t = new DiffViewTests.Fixture();
        var inner = MakeZip(t.Left, "inner.zip", "hello.txt");
        var outer = Path.Combine(t.Left, "outer.zip");
        using (var z = ZipFile.Open(outer, ZipArchiveMode.Create))
            z.CreateEntryFromFile(inner, "inner.zip");
        File.Delete(inner);
        t.Open();

        t.PutCursorOn('L', "outer.zip");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "inner.zip"), TimeSpan.FromSeconds(10));
        t.PutCursorOn('L', "inner.zip");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "hello.txt"), TimeSpan.FromSeconds(10));

        t.Press(KeyCode.F7);

        Assert.Null(t.Field<object?>("_namePrompt"));    // never opened
        Assert.Contains("nested archive", t.Field<string?>("_notice") ?? "");
    }
}
