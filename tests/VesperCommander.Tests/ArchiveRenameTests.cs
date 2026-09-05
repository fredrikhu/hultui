using System.IO.Compression;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Shift+F6 inside an archive. A zip entry's name is part of the entry, so a rename through
/// the BCL is a copy to a new entry and a delete of the old one — which is why the bytes,
/// the stamp and the folder marker each get a test of their own: any of them is easy to
/// lose on the way across.
/// </summary>
public class ArchiveRenameTests
{
    private static string MakeZip(string dir, string name, params (string Entry, string Body)[] entries)
    {
        var path = Path.Combine(dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, body) in entries)
        {
            var e = zip.CreateEntry(entry);
            if (body.Length == 0) continue;
            using var w = new StreamWriter(e.Open());
            w.Write(body);
        }
        return path;
    }

    private static string[] Entries(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        return zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }

    private static string Body(string archive, string entry)
    {
        using var zip = ZipFile.OpenRead(archive);
        using var r = new StreamReader(zip.GetEntry(entry)!.Open());
        return r.ReadToEnd();
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vcrename_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string? Rename(string zip, string internalPath, string newName)
    {
        var err = ArchiveVfs.PlanRename(zip, internalPath, newName, null, sevenZip: false, out var plan);
        return err ?? ArchiveVfs.RenameMember(zip, plan, null, sevenZip: false);
    }

    // ── The write itself ─────────────────────────────────────────────────
    [Fact]
    public void AMemberKeepsItsBytesAndStampUnderTheNewName()
    {
        var dir = TempDir();
        try
        {
            var zip = MakeZip(dir, "a.zip", ("readme.txt", "hello"), ("keep.txt", "k"));
            DateTimeOffset stamp;
            using (var z = ZipFile.OpenRead(zip)) stamp = z.GetEntry("readme.txt")!.LastWriteTime;

            Assert.Null(Rename(zip, "readme.txt", "notes.txt"));

            Assert.Equal(["keep.txt", "notes.txt"], Entries(zip));
            Assert.Equal("hello", Body(zip, "notes.txt"));
            using var after = ZipFile.OpenRead(zip);
            Assert.Equal(stamp, after.GetEntry("notes.txt")!.LastWriteTime);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void AFolderCarriesItsSubtreeAndItsMarker()
    {
        var dir = TempDir();
        try
        {
            var zip = MakeZip(dir, "a.zip",
                ("docs/", ""), ("docs/a.txt", "A"), ("docs/sub/b.txt", "B"), ("other.txt", "O"));

            Assert.Null(Rename(zip, "docs", "notes"));

            Assert.Equal(["notes/", "notes/a.txt", "notes/sub/b.txt", "other.txt"], Entries(zip));
            Assert.Equal("B", Body(zip, "notes/sub/b.txt"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>The pane hands over backslash paths; inside the archive they are '/'.</summary>
    [Fact]
    public void ANestedMemberStaysInItsFolder()
    {
        var dir = TempDir();
        try
        {
            var zip = MakeZip(dir, "a.zip", ("docs/a.txt", "A"), ("docs/b.txt", "B"));

            Assert.Null(Rename(zip, "docs\\a.txt", "c.txt"));

            Assert.Equal(["docs/b.txt", "docs/c.txt"], Entries(zip));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>A member stored as "./notes/a.txt" is listed as "notes/a.txt", and has to
    /// answer to that name — the same normalization the delete matches through.</summary>
    [Fact]
    public void AMemberStoredWithADotSlashStillMatches()
    {
        var dir = TempDir();
        try
        {
            var zip = MakeZip(dir, "a.zip", ("./notes/a.txt", "A"));

            Assert.Null(Rename(zip, "notes\\a.txt", "b.txt"));

            Assert.Equal(["notes/b.txt"], Entries(zip));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ACaseOnlyRenameIsNotACollisionWithItself()
    {
        var dir = TempDir();
        try
        {
            var zip = MakeZip(dir, "a.zip", ("readme.txt", "R"));

            Assert.Null(Rename(zip, "readme.txt", "README.txt"));

            Assert.Equal(["README.txt"], Entries(zip));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ── Refusals, all before any write ───────────────────────────────────
    [Fact]
    public void ATakenNameIsRefusedInThePlan()
    {
        var dir = TempDir();
        try
        {
            var zip = MakeZip(dir, "a.zip", ("a.txt", "A"), ("B.txt", "B"));
            var before = File.GetLastWriteTimeUtc(zip);

            var err = ArchiveVfs.PlanRename(zip, "a.txt", "b.txt", null, false, out _);

            Assert.Contains("already", err);
            Assert.Equal(before, File.GetLastWriteTimeUtc(zip));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ANameTakenByAFolderIsRefusedToo()
    {
        var dir = TempDir();
        try
        {
            var zip = MakeZip(dir, "a.zip", ("a.txt", "A"), ("docs/x.txt", "X"));
            Assert.Contains("already", ArchiveVfs.PlanRename(zip, "a.txt", "docs", null, false, out _));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Theory]
    [InlineData("sub/name.txt")]
    [InlineData("sub\\name.txt")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    public void AnUnstorableNameIsRefused(string name)
    {
        var dir = TempDir();
        try
        {
            var zip = MakeZip(dir, "a.zip", ("a.txt", "A"));
            Assert.NotNull(ArchiveVfs.PlanRename(zip, "a.txt", name, null, false, out _));
            Assert.Equal(["a.txt"], Entries(zip));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void TheSameNameIsANoOp()
    {
        var dir = TempDir();
        try
        {
            var zip = MakeZip(dir, "a.zip", ("a.txt", "A"));
            Assert.Null(ArchiveVfs.PlanRename(zip, "a.txt", "a.txt", null, false, out var plan));
            Assert.Equal(plan.From, plan.To);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void RarIsReadOnly()
    {
        var dir = TempDir();
        try
        {
            var rar = Path.Combine(dir, "a.rar");
            File.WriteAllBytes(rar, [1, 2, 3]);
            Assert.Contains("read-only", ArchiveVfs.PlanRename(rar, "a.txt", "b.txt", null, true, out _));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>The 7-Zip path is <c>rn</c>, with from/to after the "--" so a member named
    /// like a switch stays data. No 7-Zip here to run it, so the shape is what is asserted.</summary>
    [Fact]
    public void SevenZipRenameIsOneRnPairAfterTheSwitches()
    {
        var args = SevenZip.RenameArgs("a.7z", [("docs/a.txt", "docs/b.txt")], null);
        int dash = Array.IndexOf(args, "--");
        Assert.Equal("rn", args[0]);
        Assert.True(dash > 0);
        Assert.Equal(["a.7z", "docs/a.txt", "docs/b.txt"], args[(dash + 1)..]);
    }

    // ── Through the app ──────────────────────────────────────────────────
    /// <summary>Shift+F6 used to be refused inside an archive. Driven end to end: the real
    /// chord, the real prompt, the real Enter, and the bytes on disk afterwards.</summary>
    [Fact]
    public void ShiftF6InsideAnArchiveRenamesTheMemberAndLandsOnIt()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = MakeZip(t.Left, "assets.zip", ("readme.txt", "hello"), ("keep.txt", "k"));
        t.Open();

        t.PutCursorOn('L', "assets.zip");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "readme.txt"), TimeSpan.FromSeconds(10));

        t.PutCursorOn('L', "readme.txt");
        t.Press(KeyCode.F6, KeyModifiers.Shift);
        Assert.NotNull(t.Field<object?>("_namePrompt"));
        t.SetField("_namePromptValue", "notes.txt");
        t.Press(KeyCode.Enter);

        Assert.Null(t.Field<object?>("_namePrompt"));    // the prompt closed; the write is under way
        t.Session.PumpUntil(() => (t.Field<string?>("_notice") ?? "").StartsWith("Renamed"), TimeSpan.FromSeconds(30));
        Assert.Equal(["keep.txt", "notes.txt"], Entries(zip));

        // Relisted with the new name, and the cursor followed it.
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "notes.txt"), TimeSpan.FromSeconds(10));
        Assert.Equal("notes.txt", t.Rows('L')[t.Cursor('L')].Name);
    }

    /// <summary>A taken name stays in the dialog, the way it does for a file on disk.</summary>
    [Fact]
    public void ATakenNameStaysInThePrompt()
    {
        using var t = new DiffViewTests.Fixture();
        var zip = MakeZip(t.Left, "assets.zip", ("readme.txt", "hello"), ("keep.txt", "k"));
        t.Open();

        t.PutCursorOn('L', "assets.zip");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "readme.txt"), TimeSpan.FromSeconds(10));

        t.PutCursorOn('L', "readme.txt");
        t.Press(KeyCode.F6, KeyModifiers.Shift);
        t.SetField("_namePromptValue", "keep.txt");
        t.Press(KeyCode.Enter);

        Assert.NotNull(t.Field<object?>("_namePrompt"));
        Assert.Contains("already", t.Field<string?>("_namePromptError"));
        Assert.Equal(["keep.txt", "readme.txt"], Entries(zip));
    }

    [Fact]
    public void ShiftF6InsideANestedArchiveIsRefusedWithAReason()
    {
        using var t = new DiffViewTests.Fixture();
        var inner = MakeZip(t.Left, "inner.zip", ("deep.txt", "nested"));
        var outer = Path.Combine(t.Left, "outer.zip");
        using (var z = ZipFile.Open(outer, ZipArchiveMode.Create)) z.CreateEntryFromFile(inner, "inner.zip");
        File.Delete(inner);
        t.Open();

        t.PutCursorOn('L', "outer.zip");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "inner.zip"), TimeSpan.FromSeconds(10));
        t.PutCursorOn('L', "inner.zip");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Rows('L').Any(r => r.Name == "deep.txt"), TimeSpan.FromSeconds(10));

        t.PutCursorOn('L', "deep.txt");
        t.Press(KeyCode.F6, KeyModifiers.Shift);

        Assert.Null(t.Field<object?>("_namePrompt"));
        Assert.Contains("nested archive", t.Field<string?>("_notice") ?? "");
        ArchiveVfs.CleanupTemp();
    }
}
