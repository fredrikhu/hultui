using System.IO;
using System.IO.Compression;
using System.Linq;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// F8 while browsing inside an archive: the one write archives support. Driven the way a
/// user drives it — descend into the zip, mark, F8, Enter through the confirm — and
/// asserted against the bytes on disk, not just the pane.
/// </summary>
public class ArchiveDeleteShellTests
{
    [Fact]
    public void DeletingAMarkedMemberRewritesTheArchiveAndRelists()
    {
        using var t = new Shell();
        t.MakeZip("assets.zip", ("readme.txt", "hello"), ("textures/palette.png", "PNG"));
        t.Open();

        t.Descend("assets.zip", until: "readme.txt");
        t.Fixture.PutCursorOn('L', "readme.txt");
        t.Fixture.Press(KeyCode.Space);              // mark it
        t.Fixture.Press(KeyCode.F8);

        Assert.NotNull(t.Fixture.Field<object?>("_confirm"));
        // Nothing happened yet: the confirm is holding the delete.
        Assert.Contains("readme.txt", t.Entries("assets.zip"));

        t.Fixture.Press(KeyCode.Enter);
        t.SettleRemoved();

        Assert.DoesNotContain("readme.txt", t.Entries("assets.zip"));
        Assert.Contains("textures/palette.png", t.Entries("assets.zip"));
        // The pane re-listed without it.
        t.Session.PumpUntil(() => t.Fixture.Rows('L').All(e => e.Name != "readme.txt"),
            System.TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void DeletingAFolderTakesItsSubtree()
    {
        using var t = new Shell();
        t.MakeZip("assets.zip", ("readme.txt", "hello"),
            ("textures/palette.png", "PNG"), ("textures/hi_res/stone.png", "STONE"));
        t.Confirmless();
        t.Open();

        t.Descend("assets.zip", until: "textures");
        t.Fixture.PutCursorOn('L', "textures");
        t.Fixture.Press(KeyCode.F8);                 // no confirm: acts immediately
        t.SettleRemoved();

        Assert.Equal(new[] { "readme.txt" }, t.Entries("assets.zip"));
    }

    /// <summary>Inside a nested archive the pane is showing a temp extraction — a delete
    /// there would edit the copy and silently leave the real archive intact, so it is
    /// refused with a notice instead.</summary>
    [Fact]
    public void DeletingInsideANestedArchiveIsRefused()
    {
        using var t = new Shell();
        var inner = Path.Combine(Path.GetTempPath(), "vad-inner-" + System.Guid.NewGuid().ToString("N") + ".zip");
        using (var z = ZipFile.Open(inner, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(z.CreateEntry("deep.txt").Open());
            w.Write("nested");
        }
        using (var outer = ZipFile.Open(Path.Combine(t.Fixture.Left, "outer.zip"), ZipArchiveMode.Create))
            outer.CreateEntryFromFile(inner, "inner.zip");
        File.Delete(inner);
        t.Confirmless();
        t.Open();

        t.Descend("outer.zip", until: "inner.zip");
        t.Descend("inner.zip", until: "deep.txt");
        t.Fixture.PutCursorOn('L', "deep.txt");
        t.Fixture.Press(KeyCode.F8);

        Assert.Contains("nested archive", t.Fixture.Field<string?>("_notice") ?? "");
        Assert.Null(t.Fixture.Field<object?>("_confirm"));
        Assert.Equal(new[] { "inner.zip" }, t.Entries("outer.zip"));
        ArchiveVfs.CleanupTemp();
    }

    // ── harness ──────────────────────────────────────────────────────────
    private sealed class Shell : System.IDisposable
    {
        public readonly DiffViewTests.Fixture Fixture = new();
        public HultUI.Headless.HeadlessSession Session => Fixture.Session;

        public void MakeZip(string name, params (string Entry, string Body)[] members)
        {
            using var zip = ZipFile.Open(Path.Combine(Fixture.Left, name), ZipArchiveMode.Create);
            foreach (var (entry, body) in members)
            {
                using var w = new StreamWriter(zip.CreateEntry(entry).Open());
                w.Write(body);
            }
        }

        /// <summary>The app loads the developer's own settings, so pin the one under test:
        /// whether F8 stops at a confirm (true unless <see cref="Confirmless"/> was called).</summary>
        public void Open()
        {
            Fixture.Open();
            Fixture.Field<Settings>("_settings").ConfirmDelete = !_confirmless;
        }

        /// <summary>"Confirm before delete" off — F8 acts without the dialog.</summary>
        public void Confirmless() => _confirmless = true;
        private bool _confirmless;

        /// <summary>Enter the row named <paramref name="name"/> and wait until the archive
        /// listing behind it shows <paramref name="until"/>.</summary>
        public void Descend(string name, string until)
        {
            Fixture.PutCursorOn('L', name);
            Fixture.Press(KeyCode.Enter);
            Session.PumpUntil(() => Fixture.Rows('L').Any(e => e.Name == until),
                System.TimeSpan.FromSeconds(10));
        }

        /// <summary>Wait for the background rewrite to post its result.</summary>
        public void SettleRemoved()
        {
            Session.PumpUntil(() =>
            {
                var n = Fixture.Field<string?>("_notice") ?? "";
                return n.StartsWith("Removed") || n.StartsWith("Delete failed");
            }, System.TimeSpan.FromSeconds(30));
            Assert.StartsWith("Removed", Fixture.Field<string?>("_notice") ?? "");
        }

        /// <summary>The entry names in an archive in the left pane's folder.</summary>
        public string[] Entries(string archive)
        {
            using var zip = ZipFile.OpenRead(Path.Combine(Fixture.Left, archive));
            return zip.Entries.Select(e => e.FullName).Order().ToArray();
        }

        public void Dispose() => Fixture.Dispose();
    }
}
