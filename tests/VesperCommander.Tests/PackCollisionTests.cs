using System.IO;
using System.IO.Compression;
using System.Linq;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// Alt+F5 → Pack, and what it does when an archive of that name is already sitting in the
/// destination. The single-archive path has always stopped and offered a way out; "Put each
/// item in its own archive" went straight over the top of whatever it found, which is the one
/// shape of this dialog that can destroy data without saying anything.
/// </summary>
public class PackCollisionTests
{
    [Fact]
    public void PackingOneArchivePerItemStopsAtAnArchiveAlreadyThere()
    {
        using var t = new Pack();
        t.WriteLeft("notes.txt", "fresh");
        t.Existing("notes.zip", "keep-me.txt");
        t.Open();

        t.Commit(eachOwn: true, "notes.txt");

        Assert.True(t.DialogOpen, "the pack ran without asking");
        Assert.Contains("notes.zip", t.Error);
        Assert.Equal(new[] { "keep-me.txt" }, t.EntriesOf("notes.zip"));
    }

    /// <summary>The same refusal, worded for a whole batch rather than naming one file.</summary>
    [Fact]
    public void TheRefusalCountsThemWhenSeveralAreInTheWay()
    {
        using var t = new Pack();
        t.WriteLeft("a.txt", "fresh"); t.WriteLeft("b.txt", "fresh"); t.WriteLeft("c.txt", "fresh");
        t.Existing("a.zip", "keep-a.txt");
        t.Existing("b.zip", "keep-b.txt");
        t.Open();

        t.Commit(eachOwn: true, "a.txt", "b.txt", "c.txt");

        Assert.True(t.DialogOpen);
        Assert.Contains("2 of these archives already exist", t.Error);
        Assert.Equal(new[] { "keep-a.txt" }, t.EntriesOf("a.zip"));
        Assert.Equal(new[] { "keep-b.txt" }, t.EntriesOf("b.zip"));
        // Nothing at all ran: the one job with a free name did not slip through either.
        Assert.False(File.Exists(Path.Combine(t.Right, "c.zip")), "part of the batch was packed anyway");
    }

    [Fact]
    public void AddToThemLeavesWhatWasAlreadyInTheArchive()
    {
        using var t = new Pack();
        t.WriteLeft("notes.txt", "fresh");
        t.Existing("notes.zip", "keep-me.txt");
        t.Open();

        t.Commit(eachOwn: true, "notes.txt");
        t.Click("pack-add-existing");

        Assert.Equal(new[] { "keep-me.txt", "notes.txt" }, t.EntriesOf("notes.zip").Order());
    }

    [Fact]
    public void AutoRenamePacksBesideTheArchiveAlreadyThere()
    {
        using var t = new Pack();
        t.WriteLeft("notes.txt", "fresh");
        t.Existing("notes.zip", "keep-me.txt");
        t.Open();

        t.Commit(eachOwn: true, "notes.txt");
        t.Click("pack-auto-rename");

        Assert.Equal(new[] { "keep-me.txt" }, t.EntriesOf("notes.zip"));
        Assert.Equal(new[] { "notes.txt" }, t.EntriesOf("notes (2).zip"));
    }

    /// <summary>Two items whose names differ only by extension pack to one archive name. The
    /// collision is with another job in the same run rather than with the disk, and the disk
    /// cannot be consulted to find it — the first archive does not exist yet when the second
    /// job is planned.</summary>
    [Fact]
    public void TwoItemsThatWouldPackToTheSameNameDoNotOverwriteEachOther()
    {
        using var t = new Pack();
        t.WriteLeft("notes.txt", "one");
        t.WriteLeft("notes.md", "two");
        t.Open();

        t.Commit(eachOwn: true, "notes.txt", "notes.md");
        Assert.True(t.DialogOpen, "two items packing to notes.zip went through unremarked");

        t.Click("pack-auto-rename");
        // One item each, and between them both — which of the two got the plain name follows
        // the pane's sort order and is not the point.
        var first = t.EntriesOf("notes.zip");
        var second = t.EntriesOf("notes (2).zip");
        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(new[] { "notes.md", "notes.txt" }, first.Concat(second).Order());
    }

    // ── the single-archive path, which already had the prompt ────────────
    [Fact]
    public void OneArchiveForEverythingStillStopsAtAnArchiveAlreadyThere()
    {
        using var t = new Pack();
        t.WriteLeft("a.txt", "one"); t.WriteLeft("b.txt", "two");
        t.Existing("bundle.zip", "keep-me.txt");
        t.Open();

        t.Name("bundle.zip");
        t.Commit(eachOwn: false, "a.txt", "b.txt");

        Assert.True(t.DialogOpen);
        Assert.Contains("bundle.zip already exists", t.Error);
        Assert.Equal(new[] { "keep-me.txt" }, t.EntriesOf("bundle.zip"));
    }

    [Fact]
    public void AddToItMergesIntoTheOneArchive()
    {
        using var t = new Pack();
        t.WriteLeft("a.txt", "one"); t.WriteLeft("b.txt", "two");
        t.Existing("bundle.zip", "keep-me.txt");
        t.Open();

        t.Name("bundle.zip");
        t.Commit(eachOwn: false, "a.txt", "b.txt");
        t.Click("pack-add-existing");

        Assert.Equal(new[] { "a.txt", "b.txt", "keep-me.txt" }, t.EntriesOf("bundle.zip").Order());
    }

    /// <summary>A problem with no second reading — an empty name — is stated and nothing
    /// else. "Add to it" underneath it answers a question that was never asked, and clicking
    /// it only produces the same message again.</summary>
    [Fact]
    public void AProblemThatIsNotACollisionOffersNoWayOut()
    {
        using var t = new Pack();
        t.WriteLeft("a.txt", "one");
        t.Open();

        t.Name("");
        t.Commit(eachOwn: false, "a.txt");

        Assert.Contains("Enter an archive name", t.Error);
        Assert.Null(t.Session.Window.FindByAutomationId("pack-add-existing"));
        Assert.Null(t.Session.Window.FindByAutomationId("pack-auto-rename"));
    }

    // ── harness ──────────────────────────────────────────────────────────
    // Wraps the diff view's fixture: two real folders, a real app, real keystrokes. Packing
    // runs on a background thread, so every commit waits for the notice it posts back.
    private sealed class Pack : System.IDisposable
    {
        private readonly DiffViewTests.Fixture _t = new();
        private string? _name;

        public string Left => _t.Left;
        public string Right => _t.Right;
        public HultUI.Headless.HeadlessSession Session => _t.Session;

        public void WriteLeft(string name, string text) => _t.WriteLeft(name, text);

        /// <summary>An archive already in the destination pane, holding one entry whose
        /// survival is the whole assertion.</summary>
        public void Existing(string archive, string entry)
        {
            using var zip = ZipFile.Open(Path.Combine(Right, archive), ZipArchiveMode.Create);
            using var s = new StreamWriter(zip.CreateEntry(entry).Open());
            s.Write("older");
        }

        public void Open() => _t.Open();

        public bool DialogOpen => _t.Field<bool>("_packOpen");
        public string Error => _t.Field<string?>("_packError") ?? "";

        /// <summary>Applied after Alt+F5 rather than before it — opening the dialog seeds the
        /// name field, so anything set beforehand is thrown away.</summary>
        public void Name(string name) => _name = name;

        /// <summary>Mark the named items, open Pack, and press Enter — the keystrokes a user
        /// makes. "Put each item in its own archive" is a checkbox in the dialog; it is set on
        /// the app rather than clicked, since which control carries it is not under test.</summary>
        public void Commit(bool eachOwn, params string[] items)
        {
            _t.Mark('L', items);

            _t.Press(KeyCode.F5, KeyModifiers.Alt);
            Assert.True(DialogOpen, "Alt+F5 did not open the Pack dialog");
            _t.SetField("_packEachOwn", eachOwn);
            if (_name != null) _t.SetField("_packName", _name);

            _t.Press(KeyCode.Enter);
            Settle();
        }

        /// <summary>Click one of the collision dialog's two ways out and wait for the pack it
        /// starts. Reached through the accessibility tree, which is also how it is reached by
        /// someone driving the app without a mouse.</summary>
        public void Click(string automationId)
        {
            var button = Session.Window.FindByAutomationId(automationId)
                ?? throw new Xunit.Sdk.XunitException(
                    $"no \"{automationId}\" button on screen. Tree:\n{Session.Window.DumpAccessibilityTree()}");
            button.Click();
            Settle();
        }

        /// <summary>Wait for the background pack to post its result — or, when the dialog is
        /// still up, for the rebuild that shows what it is saying. A refusal starts no work to
        /// wait for.</summary>
        private void Settle()
        {
            if (DialogOpen) { Session.PumpFrame(); return; }
            Session.PumpUntil(() => Notice.StartsWith("Packed") || Notice.StartsWith("Pack failed"),
                              System.TimeSpan.FromSeconds(30));
            Assert.StartsWith("Packed", Notice);
        }

        private string Notice => _t.Field<string?>("_notice") ?? "";

        /// <summary>The entry names in an archive in the destination pane.</summary>
        public System.Collections.Generic.IEnumerable<string> EntriesOf(string archive)
        {
            var path = Path.Combine(Right, archive);
            Assert.True(File.Exists(path), $"{archive} is not in {Right}");
            using var zip = ZipFile.OpenRead(path);
            return zip.Entries.Select(e => e.FullName).ToList();
        }

        public void Dispose() => _t.Dispose();
    }
}
