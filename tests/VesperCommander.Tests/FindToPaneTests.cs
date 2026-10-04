using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// "Feed to pane": after Alt+F7, Ctrl+L hands the hit list to the pane, which then browses it
/// as a virtual folder so the ordinary operations apply to results spanning many directories.
///
/// <para>The load-bearing thing here is <b>identity</b>. A result set lists files from
/// different folders, so two rows routinely share a name; anything that resolves a row by name
/// — the mark set above all — acts on the wrong file. These tests are mostly about proving
/// that every one of those paths goes through the entry's own full path.</para>
/// </summary>
public class FindToPaneTests
{
    // ── feeding ──────────────────────────────────────────────────────────
    [Fact]
    public void CtrlLPutsTheHitsInThePaneAsABrowsableListing()
    {
        using var t = new DiffViewTests.Fixture();
        WriteUnder(t.Left, "one/alpha.txt", "a");
        WriteUnder(t.Left, "two/deep/beta.txt", "b");
        WriteUnder(t.Left, "one/ignore.md", "c");
        t.Open();

        Search(t, ".txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);

        Assert.False(t.Field<bool>("_findOpen"), "feeding left the Find dialog open");
        Assert.Equal(
            new[] { Path.Combine(t.Left, "one", "alpha.txt"), Path.Combine(t.Left, "two", "deep", "beta.txt") },
            Named(t).Select(e => e.FullPath).Order().ToArray());
    }

    /// <summary>The tab keeps pointing at the folder the search ran under, so ".." has
    /// somewhere to go and everything that legitimately asks a pane for its folder — free
    /// space, Ctrl+G, the command line — still gets a real one.</summary>
    [Fact]
    public void TheTabStillNamesTheFolderTheSearchRanUnder()
    {
        using var t = new DiffViewTests.Fixture();
        WriteUnder(t.Left, "one/alpha.txt", "a");
        t.Open();

        Search(t, ".txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);

        Assert.Equal(t.Left, t.Dir('L'));
    }

    [Fact]
    public void TheBreadcrumbSaysThisIsNotAFolder()
    {
        using var t = new DiffViewTests.Fixture();
        WriteUnder(t.Left, "one/alpha.txt", "a");
        t.Open();

        Assert.NotNull(t.Session.Window.FindByAutomationId("crumb-strip-L"));

        Search(t, ".txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);

        Assert.NotNull(t.Session.Window.FindByAutomationId("results-crumb-L"));
        Assert.Null(t.Session.Window.FindByAutomationId("crumb-strip-L"));
    }

    // ── leaving ──────────────────────────────────────────────────────────
    [Fact]
    public void BackspaceReturnsToTheFolderTheSearchRanUnder()
    {
        using var t = new DiffViewTests.Fixture();
        WriteUnder(t.Left, "one/alpha.txt", "a");
        t.WriteLeft("top.txt", "t");
        t.Open();

        Search(t, ".txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);
        Assert.Equal(2, Named(t).Count);

        t.Press(KeyCode.Backspace);
        t.Session.PumpUntil(() => t.Session.Window.FindByAutomationId("crumb-strip-L") != null);

        // The folder itself, not the results: "one" is a subfolder here, alpha.txt is not.
        Assert.Equal(new[] { "one", "top.txt" }, Named(t).Select(e => e.Name).Order().ToArray());
        Assert.Null(t.Session.Window.FindByAutomationId("results-crumb-L"));
    }

    [Fact]
    public void EnterOnTheParentRowLeavesTheResultsToo()
    {
        using var t = new DiffViewTests.Fixture();
        WriteUnder(t.Left, "one/alpha.txt", "a");
        t.Open();

        Search(t, ".txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);
        t.PutCursorOn('L', "..");
        t.Press(KeyCode.Enter);
        t.Session.PumpUntil(() => t.Session.Window.FindByAutomationId("crumb-strip-L") != null);

        Assert.Equal(t.Left, t.Dir('L'));
        Assert.Null(t.Invoke("ResultsOf", 'L'));
    }

    [Fact]
    public void ClickingTheLeaveButtonExitsTheResults()
    {
        using var t = new DiffViewTests.Fixture();
        WriteUnder(t.Left, "one/alpha.txt", "a");
        t.Open();

        Search(t, ".txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);

        t.Session.Window.FindByAutomationId("results-leave-L")!.Click();
        t.Session.PumpFrame();

        Assert.Null(t.Invoke("ResultsOf", 'L'));
    }

    // ── identity: same name, different folders ───────────────────────────
    /// <summary>The case name-keyed marks got wrong. Two hits called the same thing: marking
    /// one has to mark that one, and the operation targets have to be that one file.</summary>
    [Fact]
    public void MarkingOneOfTwoSameNamedResultsSelectsExactlyThatFile()
    {
        using var t = new DiffViewTests.Fixture();
        string first = WriteUnder(t.Left, "one/notes.txt", "first");
        string second = WriteUnder(t.Left, "two/notes.txt", "second");
        t.Open();

        Search(t, "notes.txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);
        Assert.Equal(2, Named(t).Count);

        t.PutCursorOnPath('L', second);
        t.Press(KeyCode.Space);

        // What the next F5 / F6 / F8 would act on — the point of the whole exercise.
        Assert.Equal(new[] { second }, Targets(t).Select(e => e.FullPath).ToArray());
        Assert.Equal(new[] { second }, t.MarkSet('L').ToArray());
        Assert.DoesNotContain(first, t.MarkSet('L'));
    }

    /// <summary>And the row that draws as marked is that one row, not both of them.</summary>
    [Fact]
    public void OnlyTheMarkedRowRendersAsMarked()
    {
        using var t = new DiffViewTests.Fixture();
        WriteUnder(t.Left, "one/notes.txt", "first");
        string second = WriteUnder(t.Left, "two/notes.txt", "second");
        t.Open();

        Search(t, "notes.txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);
        t.PutCursorOnPath('L', second);
        t.Press(KeyCode.Space);

        var marks = t.MarkSet('L');
        Assert.Equal(1, Named(t).Count(e => marks.Contains(e.FullPath)));
    }

    /// <summary>Deleting acts on the row's own path, so the same-named file in the other
    /// folder is untouched — and the fed listing drops only the row that went.</summary>
    [Fact]
    public void DeletingOneOfTwoSameNamedResultsLeavesTheOtherAlone()
    {
        using var t = new DiffViewTests.Fixture();
        string first = WriteUnder(t.Left, "one/notes.txt", "first");
        string second = WriteUnder(t.Left, "two/notes.txt", "second");
        t.Open();
        PermanentDeleteWithoutConfirming(t);

        Search(t, "notes.txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);
        t.PutCursorOnPath('L', second);
        t.Press(KeyCode.Space);            // mark it, so the delete runs off the mark set
        Assert.Single(Targets(t));
        t.Press(KeyCode.F8);

        Assert.True(File.Exists(first), "deleting one result took the same-named file in the other folder");
        Assert.False(File.Exists(second));
        Assert.Equal(new[] { first }, Named(t).Select(e => e.FullPath).ToArray());
    }

    /// <summary>Each row shows the folder it came from, since the name alone can't say.</summary>
    [Fact]
    public void ARowShowsTheFolderItCameFrom()
    {
        using var t = new DiffViewTests.Fixture();
        WriteUnder(t.Left, "one/notes.txt", "first");
        WriteUnder(t.Left, "two/deep/notes.txt", "second");
        t.WriteLeft("notes.txt", "at the root");
        t.Open();

        Search(t, "notes.txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);

        var sources = Named(t).Select(e => Source(t, e)).Order().ToArray();
        // The root's own hit needs no qualifier; the others name their subfolder.
        Assert.Equal(new[] { "", "one", Path.Combine("two", "deep") }, sources);
    }

    // ── operations out of a result set ───────────────────────────────────
    [Fact]
    public void F5CopiesResultsFromSeveralFoldersIntoTheOtherPane()
    {
        using var t = new DiffViewTests.Fixture();
        WriteUnder(t.Left, "one/alpha.txt", "a");
        WriteUnder(t.Left, "two/deep/beta.txt", "b");
        t.Open();

        Search(t, ".txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);
        t.Press(KeyCode.A, KeyModifiers.Ctrl);          // mark them all
        t.Press(KeyCode.F5);
        t.Press(KeyCode.Enter);                          // the destination dialog
        t.Session.PumpUntil(() => t.Field<object?>("_op") == null, TimeSpan.FromSeconds(30));

        Assert.Equal("a", File.ReadAllText(Path.Combine(t.Right, "alpha.txt")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(t.Right, "beta.txt")));
        // The sources are untouched — a copy, and read out of the folders they really live in.
        Assert.True(File.Exists(Path.Combine(t.Left, "one", "alpha.txt")));
    }

    [Fact]
    public void F6MovesAResultOutOfTheFolderItActuallyLivesIn()
    {
        using var t = new DiffViewTests.Fixture();
        string src = WriteUnder(t.Left, "one/deep/alpha.txt", "a");
        t.Open();

        Search(t, ".txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);
        t.PutCursorOnPath('L', src);
        t.Press(KeyCode.F6);
        t.Press(KeyCode.Enter);                          // the move confirm
        t.Session.PumpUntil(() => t.Field<object?>("_op") == null, TimeSpan.FromSeconds(30));

        Assert.False(File.Exists(src), "the move resolved the row against the pane's folder");
        Assert.Equal("a", File.ReadAllText(Path.Combine(t.Right, "alpha.txt")));
        Assert.Empty(Named(t));                          // and the row left with the file
    }

    [Fact]
    public void RenamingAResultRenamesItWhereItLives()
    {
        using var t = new DiffViewTests.Fixture();
        string src = WriteUnder(t.Left, "one/deep/alpha.txt", "a");
        t.Open();

        Search(t, ".txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);
        t.PutCursorOnPath('L', src);
        t.Press(KeyCode.F6, KeyModifiers.Shift);
        t.SetField("_namePromptValue", "renamed.txt");
        t.Press(KeyCode.Enter);
        t.Session.PumpFrame();

        string moved = Path.Combine(t.Left, "one", "deep", "renamed.txt");
        Assert.True(File.Exists(moved), "the rename did not land in the file's own folder");
        Assert.Equal(new[] { moved }, Named(t).Select(e => e.FullPath).ToArray());
    }

    // ── writing *into* a result set is refused ───────────────────────────
    [Fact]
    public void NothingCanBeCopiedIntoAPaneShowingResults()
    {
        using var t = new DiffViewTests.Fixture();
        WriteUnder(t.Left, "one/alpha.txt", "a");
        t.WriteRight("r.txt", "r");
        t.Open();

        Search(t, ".txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);
        t.Press(KeyCode.Tab);                            // the other pane is now the source
        t.PutCursorOn('R', "r.txt");
        t.Press(KeyCode.F5);

        Assert.Null(t.Field<object?>("_op"));
        Assert.Contains("aren't a folder", t.Field<string?>("_notice") ?? "");
        Assert.False(File.Exists(Path.Combine(t.Left, "r.txt")));
    }

    [Fact]
    public void F7CannotCreateAFolderInAResultSet()
    {
        using var t = new DiffViewTests.Fixture();
        WriteUnder(t.Left, "one/alpha.txt", "a");
        t.Open();

        Search(t, ".txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);
        t.Press(KeyCode.F7);

        Assert.Null(t.Field<object?>("_namePrompt"));
        Assert.Contains("aren't a folder", t.Field<string?>("_notice") ?? "");
    }

    // ── refresh ──────────────────────────────────────────────────────────
    [Fact]
    public void CtrlRDropsResultsWhoseFileHasGone()
    {
        using var t = new DiffViewTests.Fixture();
        string kept = WriteUnder(t.Left, "one/alpha.txt", "a");
        string gone = WriteUnder(t.Left, "two/beta.txt", "b");
        t.Open();

        Search(t, ".txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);
        Assert.Equal(2, Named(t).Count);

        File.Delete(gone);                               // changed behind the app's back
        t.Press(KeyCode.R, KeyModifiers.Ctrl);

        Assert.Equal(new[] { kept }, Named(t).Select(e => e.FullPath).ToArray());
    }

    // ── archive members ──────────────────────────────────────────────────
    /// <summary>Archive members are left out of a fed set — move, delete and rename can't
    /// drive the archive VFS — and the pane says how many it dropped rather than quietly
    /// showing fewer hits than the search found.</summary>
    [Fact]
    public void ArchiveMembersAreLeftOutAndCounted()
    {
        using var t = new DiffViewTests.Fixture();
        string real = WriteUnder(t.Left, "one/notes.txt", "on disk");
        using (var zip = ZipFile.Open(Path.Combine(t.Left, "bundle.zip"), ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("notes.txt").Open()))
            w.Write("in the archive");
        t.Open();

        Search(t, "notes.txt", archives: true);
        Assert.Equal(2, t.Field<List<FindEngine.Hit>>("_findResults").Count);   // both were found

        t.Press(KeyCode.L, KeyModifiers.Ctrl);

        Assert.Equal(new[] { real }, Named(t).Select(e => e.FullPath).ToArray());
        Assert.Equal(1, Skipped(t));
        // And the pane does not think it is inside an archive, whatever its rows came from.
        Assert.False((bool)t.Invoke("InArchive", 'L')!);
    }

    // ── sorting and view modes still work ────────────────────────────────
    [Fact]
    public void TheResultSetSortsLikeAFolder()
    {
        using var t = new DiffViewTests.Fixture();
        WriteUnder(t.Left, "one/b.txt", "bb");
        WriteUnder(t.Left, "two/a.txt", "a");
        Directory.CreateDirectory(Path.Combine(t.Left, "three", "c.txt.d"));
        t.Open();

        Search(t, ".txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);
        Assert.Equal(new[] { "a.txt", "b.txt" }, Named(t).Select(e => e.Name).ToArray());

        t.Invoke("SetSort", 'L', SortMode.Name);          // same column → flip to descending
        t.Session.PumpFrame();

        Assert.Equal(new[] { "b.txt", "a.txt" }, Named(t).Select(e => e.Name).ToArray());
    }

    /// <summary>The source folder shares the name column, and wrapping is decided at measure
    /// time — where FlexShrink alone cannot stop it. A row that folded onto a second line
    /// would be clipped by the list's fixed item height rather than growing.</summary>
    [Fact]
    public void ALongSourceFolderDoesNotWrapARow()
    {
        using var t = new DiffViewTests.Fixture();
        WriteUnder(t.Left, "a-rather-deeply-nested/set-of-folders-with-long-names/inside-here/notes.txt", "x");
        WriteUnder(t.Left, "short/notes.txt", "y");
        t.Open();

        Search(t, "notes.txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);
        t.Session.PumpFrames(2);

        float rowH = (float)typeof(CommanderApp)
            .GetProperty("RowHeight", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(t.App)!;
        var rows = ListRows(t);
        Assert.True(rows.Count >= 3, $"only {rows.Count} rows were realized — the sweep proves nothing");

        // The labels, not the row: the list forces its item height, so a row whose text folded
        // would be clipped rather than tall, and asserting on the row box would pass anyway.
        int checkedLabels = 0;
        foreach (var row in rows)
            foreach (var label in LayoutProbe.Descendants<HultUI.Core.Elements.TextElement>(row))
            {
                checkedLabels++;
                Assert.True(label.Layout.AbsoluteBorderBox.Height <= rowH,
                    $"\"{label.Content}\" laid out {label.Layout.AbsoluteBorderBox.Height:0}px tall "
                    + $"in a {rowH:0}px row — it wrapped");
            }
        Assert.True(checkedLabels >= 6, $"only {checkedLabels} labels were checked");
    }

    // ── session ──────────────────────────────────────────────────────────
    /// <summary>A fed set is session-only: the tab persists the folder the search ran under,
    /// so a restore comes back to a real folder rather than to a week-old list of files.</summary>
    [Fact]
    public void ASessionSaveRecordsTheFolderRatherThanTheResultSet()
    {
        using var t = new DiffViewTests.Fixture();
        WriteUnder(t.Left, "one/alpha.txt", "a");
        t.Open();

        Search(t, ".txt");
        t.Press(KeyCode.L, KeyModifiers.Ctrl);
        t.Invoke("SaveSession");

        var saved = SessionStore.Load();
        Assert.NotNull(saved);
        Assert.Equal(t.Left, saved!.Left[0]);
    }

    /// <summary>The window opens where the last session closed it — with no tabs to restore,
    /// too: where the window sits is not part of the "restore tabs" choice. Not maximized,
    /// because every fixture's teardown saves the session and the next test would open so.</summary>
    [Fact]
    public void TheWindowOpensWhereTheLastSessionLeftIt_AndTheSessionRecordsItAgain()
    {
        var placement = new HultUI.Core.Platform.WindowPlacement(-2000, 500, 1000, 700, false);
        SessionStore.Save(new SessionData([], 0, [], 0, Window: placement));

        using var t = new DiffViewTests.Fixture();
        t.Open();
        var window = t.Session.Window.Window;
        Assert.Equal(placement, window.Placement);

        window.Placement = placement with { X = 40 };
        t.Invoke("SaveSession");
        Assert.Equal(placement with { X = 40 }, SessionStore.Load()!.Window);
    }

    // ── the sort helper the listing is built on (pure) ───────────────────
    [Fact]
    public void SortGroupsDirectoriesFirstAndDropsAnyParentRow()
    {
        var entries = new List<FileEntry>
        {
            new("b.txt", @"X:\two\b.txt", IsDir: false, 2, new DateTime(2026, 1, 2)),
            new("..", @"X:\", IsDir: true, 0, DateTime.Now, IsParent: true),
            new("a.txt", @"X:\one\a.txt", IsDir: false, 9, new DateTime(2026, 1, 1)),
            new("zed", @"X:\three\zed", IsDir: true, 0, new DateTime(2026, 1, 3)),
        };

        Assert.Equal(new[] { "zed", "a.txt", "b.txt" },
            FileSystem.Sort(entries).Select(e => e.Name).ToArray());
        Assert.Equal(new[] { "zed", "b.txt", "a.txt" },
            FileSystem.Sort(entries, SortMode.Name, dirsFirst: false, descending: true)
                .Select(e => e.Name).ToArray());
        Assert.Equal(new[] { "zed", "a.txt", "b.txt" },
            FileSystem.Sort(entries, SortMode.Size, dirsFirst: true, descending: true)
                .Select(e => e.Name).ToArray());
    }

    // ── helpers ──────────────────────────────────────────────────────────
    /// <summary>Write a file at a path relative to <paramref name="root"/>, creating the
    /// folders on the way. Returns its full path — the identity everything here is about.</summary>
    private static string WriteUnder(string root, string relative, string text)
    {
        var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return full;
    }

    /// <summary>Run a recursive Alt+F7 search over the left pane's folder and wait for it to
    /// settle. The pattern is set on the app rather than typed: which field carries it is not
    /// what these tests are about, but the Ctrl+L that follows is a real keystroke.</summary>
    private static void Search(DiffViewTests.Fixture t, string pattern, bool archives = false)
    {
        t.Press(KeyCode.F7, KeyModifiers.Alt);
        Assert.True(t.Field<bool>("_findOpen"), "Alt+F7 did not open the Find dialog");
        t.SetField("_findRecursive", true);
        t.SetField("_findArchives", archives);
        t.SetField("_findPattern", pattern);
        t.Invoke("RunFind");
        t.Session.PumpUntil(() => !t.Field<bool>("_findSearching"), TimeSpan.FromSeconds(30));
        t.Session.PumpFrames(2);   // the drain happens in the dialog's build
    }

    /// <summary>The fed rows, without the ".." that leads them.</summary>
    private static List<FileEntry> Named(DiffViewTests.Fixture t) =>
        t.Rows('L').Where(e => !e.IsParent).ToList();

    /// <summary>The Detail rows the left pane has actually realized, reached through the app's
    /// own Ref — these are plain Grid boxes, and the a11y queries drop what they can't
    /// resolve in a recycling VirtualList.</summary>
    private static IReadOnlyList<HultUI.Core.Elements.Element> ListRows(DiffViewTests.Fixture t)
    {
        var vlist = t.Field<HultUI.Core.Reactivity.Ref<HultUI.Core.Elements.VirtualListElement<FileEntry>>>("_vlistL")
            .Current ?? throw new Xunit.Sdk.XunitException("the left pane has no list");
        // The list's first child is the spacer standing in for the rows scrolled past above.
        return vlist.Children.Skip(1).ToList();
    }

    private static IReadOnlyList<FileEntry> Targets(DiffViewTests.Fixture t) =>
        (IReadOnlyList<FileEntry>)t.Invoke("OperationTargets", 'L')!;

    /// <summary>The source folder the pane shows beside a row's name.</summary>
    private static string Source(DiffViewTests.Fixture t, FileEntry e) =>
        (string)typeof(CommanderApp).GetMethod("ResultSource",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [t.Invoke("ResultsOf", 'L'), e])!;

    private static int Skipped(DiffViewTests.Fixture t)
    {
        var results = t.Invoke("ResultsOf", 'L')!;
        return (int)results.GetType()
            .GetField("SkippedInArchive", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!
            .GetValue(results)!;
    }

    /// <summary>Delete for real and without a dialog — these tests must not push the
    /// developer's Recycle Bin around, and the confirm is not what they are checking.</summary>
    private static void PermanentDeleteWithoutConfirming(DiffViewTests.Fixture t)
    {
        var s = t.Field<Settings>("_settings");
        s.ConfirmDelete = false;
        s.Recycle = false;
    }
}
