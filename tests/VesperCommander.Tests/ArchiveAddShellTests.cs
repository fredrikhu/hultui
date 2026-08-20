using System.IO.Compression;
using HultUI.Core.Events;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// F5, F6 and paste with an archive as the destination, driven through the app. These used to be
/// refused outright by InArchiveBlock — "Can't write inside an archive — F5 copies out" — so the
/// only way to add to an archive was to rebuild it from the Pack dialog.
/// </summary>
public class ArchiveAddShellTests
{
    [Fact]
    public void F5AddsTheSelectionToTheArchiveInTheOtherPane()
    {
        using var t = new Shell();
        t.MakeZip("assets.zip", ("readme.txt", "hello"));
        t.WriteSource("stone.png", "STONE");
        t.Open();
        t.DescendRight("assets.zip", until: "readme.txt");

        t.MarkLeft("stone.png");
        t.Press(KeyCode.F5);
        t.AcceptDestination();
        t.SettleAdded();

        Assert.Equal(new[] { "readme.txt", "stone.png" }, t.Entries("assets.zip"));
        Assert.Equal("STONE", t.EntryText("assets.zip", "stone.png"));
    }

    /// <summary>It lands in the folder the pane is showing inside the archive, not at its root.</summary>
    [Fact]
    public void AddingWhileInsideAFolderPutsItInThatFolder()
    {
        using var t = new Shell();
        t.MakeZip("assets.zip", ("textures/palette.png", "PNG"));
        t.WriteSource("stone.png", "STONE");
        t.Open();
        t.DescendRight("assets.zip", until: "textures");
        t.DescendRight("textures", until: "palette.png");

        t.MarkLeft("stone.png");
        t.Press(KeyCode.F5);
        t.AcceptDestination();
        t.SettleAdded();

        Assert.Equal(new[] { "textures/palette.png", "textures/stone.png" }, t.Entries("assets.zip"));
    }

    [Fact]
    public void AMemberOfThatNameStopsToAskAndOverwriteReplacesIt()
    {
        using var t = new Shell();
        t.MakeZip("assets.zip", ("notes.txt", "old"));
        t.WriteSource("notes.txt", "new");
        t.Open();
        t.DescendRight("assets.zip", until: "notes.txt");

        t.MarkLeft("notes.txt");
        t.Press(KeyCode.F5);
        t.AcceptDestination();
        t.WaitForOverwritePrompt();

        // Nothing has been written while the question is on screen.
        Assert.Equal("old", t.EntryText("assets.zip", "notes.txt"));

        t.Decide(overwrite: true, applyToRest: false);
        t.SettleAdded();

        Assert.Equal(new[] { "notes.txt" }, t.Entries("assets.zip"));
        Assert.Equal("new", t.EntryText("assets.zip", "notes.txt"));
    }

    [Fact]
    public void SkipLeavesTheMemberAloneAndAddsNothing()
    {
        using var t = new Shell();
        t.MakeZip("assets.zip", ("notes.txt", "old"));
        t.WriteSource("notes.txt", "new");
        t.Open();
        t.DescendRight("assets.zip", until: "notes.txt");

        t.MarkLeft("notes.txt");
        t.Press(KeyCode.F5);
        t.AcceptDestination();
        t.WaitForOverwritePrompt();
        t.Decide(overwrite: false, applyToRest: false);
        t.SettleNoticeStarting("Nothing added");

        Assert.Equal(new[] { "notes.txt" }, t.Entries("assets.zip"));
        Assert.Equal("old", t.EntryText("assets.zip", "notes.txt"));
    }

    /// <summary>The overwrite dialog compares source against target, and an archive member has
    /// nothing on disk to stat — so the conflict has to carry the member's real size for the
    /// dialog to have anything truthful to show. Otherwise every member reads as absent and the
    /// choice is made blind.
    /// <para>This asserts the size was <em>recorded</em>, which is where it can go missing.
    /// That the dialog then renders it is a one-line read, checked by eye.</para></summary>
    [Fact]
    public void TheConflictRecordsTheMembersRealSizeForThePrompt()
    {
        using var t = new Shell();
        t.MakeZip("assets.zip", ("notes.txt", "0123456789"));    // ten bytes
        t.WriteSource("notes.txt", "new");
        t.Open();
        t.DescendRight("assets.zip", until: "notes.txt");

        t.MarkLeft("notes.txt");
        t.Press(KeyCode.F5);
        t.AcceptDestination();
        t.WaitForOverwritePrompt();

        Assert.Equal(10, t.PromptTargetSize());
        t.Decide(overwrite: false, applyToRest: false);
        t.SettleNoticeStarting("Nothing added");
    }

    // ── F6 ───────────────────────────────────────────────────────────────
    /// <summary>A move into an archive deletes the original — so it asks first, like any move.
    /// The question is the destination dialog now, showing the archive folder it would pack
    /// into, rather than a fixed yes/no.</summary>
    [Fact]
    public void F6AsksBeforeMovingIntoAnArchive()
    {
        using var t = new Shell();
        t.MakeZip("assets.zip", ("readme.txt", "hello"));
        t.WriteSource("stone.png", "STONE");
        t.Open();
        t.DescendRight("assets.zip", until: "readme.txt");

        t.MarkLeft("stone.png");
        t.Press(KeyCode.F6);

        Assert.NotNull(t.Prompt);
        Assert.Equal(Path.Combine(t.Left, "assets.zip"), t.PromptValue);
        Assert.True(File.Exists(t.SourcePath("stone.png")), "the original went before anyone agreed");
        Assert.Equal(new[] { "readme.txt" }, t.Entries("assets.zip"));
    }

    [Fact]
    public void F6MovesTheItemInAndTakesTheOriginalWithIt()
    {
        using var t = new Shell();
        t.MakeZip("assets.zip", ("readme.txt", "hello"));
        t.WriteSource("stone.png", "STONE");
        t.Open();
        t.DescendRight("assets.zip", until: "readme.txt");

        t.MarkLeft("stone.png");
        t.Press(KeyCode.F6);
        t.Press(KeyCode.Enter);                  // confirm
        t.SettleAdded();

        Assert.Equal(new[] { "readme.txt", "stone.png" }, t.Entries("assets.zip"));
        Assert.False(File.Exists(t.SourcePath("stone.png")), "the original survived a move");
    }

    /// <summary>The case that actually risks data: a batch where one item lands and another is
    /// skipped. The move goes ahead — so the deletion loop runs — and it must take only what
    /// arrived. Skipping every item returns before the deletion is even reached, so an
    /// all-skipped test cannot tell a per-item check from no check at all.</summary>
    [Fact]
    public void AMoveThatSkipsOneItemStillKeepsThatOnesOriginal()
    {
        using var t = new Shell();
        t.MakeZip("assets.zip", ("notes.txt", "old"));
        t.WriteSource("notes.txt", "new");        // clashes — will be skipped
        t.WriteSource("stone.png", "STONE");      // free — will land
        t.Open();
        t.DescendRight("assets.zip", until: "notes.txt");

        t.MarkLeft("notes.txt");
        t.MarkLeft("stone.png");
        t.Press(KeyCode.F6);
        t.Press(KeyCode.Enter);                   // confirm the move
        t.WaitForOverwritePrompt();
        t.Decide(overwrite: false, applyToRest: false);
        t.SettleAdded();

        // The one that landed moved; the one that was skipped did not.
        Assert.Equal(new[] { "notes.txt", "stone.png" }, t.Entries("assets.zip"));
        Assert.Equal("old", t.EntryText("assets.zip", "notes.txt"));
        Assert.False(File.Exists(t.SourcePath("stone.png")), "the item that landed kept its original");
        Assert.True(File.Exists(t.SourcePath("notes.txt")), "a skipped item lost its original anyway");
    }

    /// <summary>A skipped item was never added, so its original must stay — a move only moves
    /// what arrived.</summary>
    [Fact]
    public void ASkippedItemKeepsItsOriginalAfterAMove()
    {
        using var t = new Shell();
        t.MakeZip("assets.zip", ("notes.txt", "old"));
        t.WriteSource("notes.txt", "new");
        t.Open();
        t.DescendRight("assets.zip", until: "notes.txt");

        t.MarkLeft("notes.txt");
        t.Press(KeyCode.F6);
        t.Press(KeyCode.Enter);                  // confirm the move
        t.WaitForOverwritePrompt();
        t.Decide(overwrite: false, applyToRest: false);
        t.SettleNoticeStarting("Nothing added");

        Assert.Equal("old", t.EntryText("assets.zip", "notes.txt"));
        Assert.True(File.Exists(t.SourcePath("notes.txt")), "a skipped item lost its original anyway");
    }

    /// <summary>Moving *out* of an archive stays refused: it would mean extracting and then
    /// deleting members, and F5 already copies out.</summary>
    [Fact]
    public void MovingOutOfAnArchiveIsStillRefused()
    {
        using var t = new Shell();
        t.MakeZip("assets.zip", ("readme.txt", "hello"));
        t.Open();
        t.DescendLeft("assets.zip", until: "readme.txt");

        t.PutCursorLeft("readme.txt");
        t.Press(KeyCode.F6);

        Assert.Null(t.Confirm);
        Assert.Contains("Can't write inside an archive", t.Notice);
    }

    // ── paste ────────────────────────────────────────────────────────────
    [Fact]
    public void PastingIntoAnArchiveAddsToIt()
    {
        using var t = new Shell();
        t.MakeZip("assets.zip", ("readme.txt", "hello"));
        t.WriteSource("stone.png", "STONE");
        t.Open();

        t.MarkLeft("stone.png");
        t.Press(KeyCode.C, KeyModifiers.Ctrl);
        t.DescendLeft("assets.zip", until: "readme.txt");
        t.Press(KeyCode.V, KeyModifiers.Ctrl);
        t.SettleAdded();

        Assert.Equal(new[] { "readme.txt", "stone.png" }, t.Entries("assets.zip"));
    }

    // ── refusals ─────────────────────────────────────────────────────────
    /// <summary>Inside a nested archive the pane is showing a temp extraction, so an add would go
    /// into the copy and leave the real archive untouched.</summary>
    [Fact]
    public void AddingIntoANestedArchiveIsRefused()
    {
        using var t = new Shell();
        var inner = Path.Combine(Path.GetTempPath(), "vaa-inner-" + Guid.NewGuid().ToString("N") + ".zip");
        using (var z = ZipFile.Open(inner, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(z.CreateEntry("deep.txt").Open());
            w.Write("buried");
        }
        using (var outer = ZipFile.Open(Path.Combine(t.Left, "outer.zip"), ZipArchiveMode.Create))
            outer.CreateEntryFromFile(inner, "inner.zip");
        File.Delete(inner);

        t.WriteSource("stone.png", "STONE");
        t.Open();
        t.DescendLeft("outer.zip", until: "inner.zip");
        t.DescendLeft("inner.zip", until: "deep.txt");

        t.PutCursorLeft("deep.txt");
        t.Press(KeyCode.V, KeyModifiers.Ctrl);   // nothing on the clipboard is fine; the block is first

        Assert.Contains("nested archive", t.Notice);
    }

    // ── harness ──────────────────────────────────────────────────────────
    private sealed class Shell : IDisposable
    {
        public readonly DiffViewTests.Fixture Fixture = new();
        public string Left => Fixture.Left;
        public string Right => Fixture.Right;
        private HultUI.Headless.HeadlessSession Session => Fixture.Session;

        public void Dispose() => Fixture.Dispose();
        public void Open() => Fixture.Open();
        public void Press(KeyCode k, KeyModifiers m = KeyModifiers.None) => Fixture.Press(k, m);
        public string Notice => Fixture.Field<string?>("_notice") ?? "";
        public object? Confirm => Fixture.Field<object?>("_confirm");

        /// <summary>The F5 / F6 destination dialog, and the path it is showing.</summary>
        public object? Prompt => Fixture.Field<object?>("_namePrompt");
        public string PromptValue => Fixture.Field<string>("_namePromptValue");

        /// <summary>F5 / F6 stop at the destination dialog now. Accepting what it offers — the
        /// other pane's folder — is what the key used to do on its own.</summary>
        public void AcceptDestination()
        {
            Assert.NotNull(Prompt);
            Press(KeyCode.Enter);
        }

        /// <summary>The archive lives in the left pane's folder; sources do too, so a copy to the
        /// right pane crosses into the archive rather than between two real folders.</summary>
        public void MakeZip(string name, params (string Entry, string Body)[] members)
        {
            using var zip = ZipFile.Open(Path.Combine(Left, name), ZipArchiveMode.Create);
            foreach (var (entry, body) in members)
            {
                using var w = new StreamWriter(zip.CreateEntry(entry).Open());
                w.Write(body);
            }
        }

        public void WriteSource(string name, string text) => Fixture.WriteLeft(name, text);
        public string SourcePath(string name) => Path.Combine(Left, name);

        public void MarkLeft(string name)
        {
            Fixture.PutCursorOn('L', name);
            Fixture.Mark('L', name);
        }

        public void PutCursorLeft(string name) => Fixture.PutCursorOn('L', name);

        /// <summary>Send the right pane into an archive (or a folder inside one) by navigating,
        /// then wait for the level's listing — the pane keeps showing the old rows while the
        /// archive is read, so "has rows" is satisfied by the wrong ones.</summary>
        public void DescendRight(string name, string until) => Descend('R', name, until);
        public void DescendLeft(string name, string until) => Descend('L', name, until);

        private void Descend(char side, string name, string until)
        {
            string from = Fixture.Dir(side);
            string target;
            if (ArchivePath.InArchive(from))
            {
                // Already inside: the folder is a virtual path, and combining onto it is how the
                // app itself descends a level.
                var ap = ArchivePath.TryParse(from)!.Value;
                target = ArchivePath.Combine(ap.ArchiveFile, Path.Combine(ap.Internal, name));
            }
            else
            {
                // Every archive and source this harness makes lives in the left folder, whichever
                // pane is being sent into it — resolving against the pane's own folder would send
                // the right pane at a path that was never created.
                target = Path.Combine(Left, name);
            }
            Fixture.Invoke("NavigateTo", side, target, null, false);
            Session.PumpUntil(() => Fixture.Rows(side).Any(e => e.Name == until),
                TimeSpan.FromSeconds(20));
            Assert.True(ArchivePath.InArchive(Fixture.Dir(side)),
                $"the {side} pane did not end up inside the archive");
        }

        public void WaitForOverwritePrompt() =>
            Session.PumpUntil(() => Fixture.Field<bool>("_overwriteOpen"), TimeSpan.FromSeconds(20));

        /// <summary>Answer the overwrite prompt the way its buttons do.</summary>
        public void Decide(bool overwrite, bool applyToRest)
        {
            Fixture.Invoke("OverwriteDecision", overwrite, applyToRest);
            Session.PumpFrame();
        }

        /// <summary>The size the prompt is showing for what is already in the archive — read off
        /// the op rather than the pixels, since the number is the point and the glyphs are not.</summary>
        public long PromptTargetSize()
        {
            var op = Fixture.Field<object>("_op");
            var targets = op.GetType().GetField("ArchiveTargets",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!.GetValue(op)!;
            var index = (int)op.GetType().GetField("Index",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!.GetValue(op)!;
            var map = (System.Collections.IDictionary)targets;
            var entry = map[index]!;
            return (long)entry.GetType().GetField("Item1")!.GetValue(entry)!;
        }

        public void SettleAdded() => SettleNoticeStarting("Added");

        public void SettleNoticeStarting(string prefix)
        {
            Session.PumpUntil(() => Notice.StartsWith(prefix) || Notice.StartsWith("Add failed")
                                 || Notice.StartsWith("Add cancelled"),
                              TimeSpan.FromSeconds(30));
            Assert.StartsWith(prefix, Notice);
        }

        public string[] Entries(string archive)
        {
            using var zip = ZipFile.OpenRead(Path.Combine(Left, archive));
            return [.. zip.Entries.Select(e => e.FullName).Order()];
        }

        public string EntryText(string archive, string entry)
        {
            using var zip = ZipFile.OpenRead(Path.Combine(Left, archive));
            using var r = new StreamReader(zip.GetEntry(entry)!.Open());
            return r.ReadToEnd();
        }
    }
}
