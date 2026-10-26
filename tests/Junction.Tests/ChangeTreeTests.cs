using System.Collections;
using System.Reflection;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The folder grouping in the two lists of changed files — the Commit view's "files
/// changed" and the Changes view's staged/unstaged lists.
///
/// <para>Both lists were flat, each row carrying its directory as a grey second line, and
/// the claims worth pinning are the ones that made grouping worth doing: a directory is
/// said once, chains of single-child folders don't cost a row each, and a folder row is a
/// real row — it takes the caret, it takes a range, and staging it stages what is under
/// it. The last of those is asserted against git, because "stage this directory" is only
/// true if the index says so afterwards.</para>
/// </summary>
public class ChangeTreeTests
{
    // ── The Commit view's list ───────────────────────────────────────────

    [Fact]
    public void ACommitsFilesGroupIntoFoldersAndSayTheirDirectoryOnce()
    {
        Hosted(repo =>
        {
            repo.Write("src/app/main.cs", "one\n");
            repo.Write("src/app/util.cs", "two\n");
            repo.Write("docs/readme.md", "three\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", "three files in two places");
        }, (repo, session, app) =>
        {
            var rows = OpenCommit(session, app);

            // Folders first, then files, each named by its own segment — `src/app` is one
            // row rather than two, because `src` holds nothing but `app`.
            Assert.Equal(new[] { "docs", "readme.md", "src/app", "main.cs", "util.cs" },
                rows.Select(r => r.Segment));
            Assert.Equal(new[] { true, false, true, false, false }, rows.Select(r => r.IsFolder));
            // The rows under a folder are indented by exactly one level, however many
            // segments the folder's label joined.
            Assert.Equal(new[] { 0, 1, 0, 1, 1 }, rows.Select(r => r.Depth));
        });
    }

    [Fact]
    public void CollapsingAFolderTakesItsFilesOffTheList()
    {
        Hosted(repo =>
        {
            repo.Write("src/app/main.cs", "one\n");
            repo.Write("src/app/util.cs", "two\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", "two files under one folder");
        }, (repo, session, app) =>
        {
            var rows = OpenCommit(session, app);
            Assert.Equal(3, rows.Count);

            // Home puts the cursor on the folder row; ← closes it, the conventional key.
            session.Window.PressKey(KeyCode.Home);
            session.Window.PressKey(KeyCode.Left);
            session.PumpFrames(2);

            Assert.Equal(new[] { "src/app" }, CommitRows(app).Select(r => r.Segment));
            // → opens it again rather than stepping anywhere, so nothing is lost.
            session.Window.PressKey(KeyCode.Right);
            session.PumpFrames(2);
            Assert.Equal(3, CommitRows(app).Count);
        });
    }

    /// <summary>
    /// The reason the cursor and the open file are two different things. Arrowing down a
    /// grouped list crosses folder rows constantly; if the diff pane followed the cursor
    /// onto them it would blank on every other keystroke.
    /// </summary>
    [Fact]
    public void TheCursorCrossesAFolderWithoutBlankingTheDiff()
    {
        Hosted(repo =>
        {
            repo.Write("a/one.cs", "one\n");
            repo.Write("b/two.cs", "two\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", "one file in each of two folders");
        }, (repo, session, app) =>
        {
            var rows = OpenCommit(session, app);
            Assert.Equal(new[] { "a", "one.cs", "b", "two.cs" }, rows.Select(r => r.Segment));

            // Down twice from the first file: onto the second folder row.
            session.Window.PressKey(KeyCode.Home);
            session.Window.PressKey(KeyCode.Down);
            Assert.Equal("a/one.cs", SelectedFile(app));
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(2);

            Assert.Equal("b", Cursor(app));
            Assert.Equal("a/one.cs", SelectedFile(app));   // the pane held its ground

            // One more step lands on a file, and the pane follows again.
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrames(2);
            Assert.Equal("b/two.cs", SelectedFile(app));
        });
    }

    /// <summary>
    /// A commit of many files is a virtual list: only the rows on screen are elements, a key
    /// makes the two rows the cursor left and reached and keeps the rest, End brings the last
    /// row on screen, a collapse takes its rows off, folders are tree items with their open
    /// state, and every row is exactly as tall as it lays out — the heights the list is told
    /// are the ones the rows had in the scroll view it replaced (2 MB a key on 382 rows).
    /// </summary>
    [Fact]
    public void AManyFileCommitIsAVirtualListOfTheRowsOnScreen()
    {
        Hosted(repo =>
        {
            for (int i = 0; i < 160; i++) repo.Write($"src/mod{i % 8}/part{i % 5}/file{i:000}.cs", $"{i}\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-q", "-m", "many files");
        }, (repo, session, app) =>
        {
            var rows = OpenCommit(session, app);
            session.PumpFrames(2);
            Assert.Equal(1 + 8 + 40 + 160, rows.Count);
            var list = TreeList(app);
            Assert.True(list.Children.Count < 40, $"{list.Children.Count} of {rows.Count + 1} rows are elements");
            AssertRowsAreTheirOwnHeight(list);

            // The cursor starts on the open file, the first of src/mod0/part0's four; three keys
            // walk the others, each making the row left and the row reached.
            Assert.Equal("src/mod0/part0/file000.cs", Cursor(app));
            for (int i = 0; i < 3; i++)
            {
                var before = Realized(list);
                int made = app.CommitTreeRowsMade, builds = app.ChromeBuilds;
                session.Window.PressKey(KeyCode.Down);
                session.PumpFrames(2);
                Assert.Equal(2, app.CommitTreeRowsMade - made);
                Assert.Equal(before.Count - 2, Realized(list).Intersect(before, ReferenceEqualityComparer.Instance).Count());
                Assert.Equal(builds, app.ChromeBuilds);
            }
            Assert.Equal("src/mod0/part0/file120.cs", SelectedFile(app));

            // End: the last row is the cursor's, and on screen.
            session.Window.PressKey(KeyCode.End);
            session.PumpFrames(3);
            var last = rows[^1].Path;
            Assert.Equal(last, Cursor(app));
            var lastRow = Realized(list).Single(r => r.Key is string k && k.StartsWith(last, StringComparison.Ordinal));
            var view = list.Layout.AbsoluteContentBox;
            var box = lastRow.Layout.AbsoluteBorderBox;
            Assert.True(list.ScrollOffsetY > 0);
            Assert.InRange(box.Y - list.ScrollOffsetY, view.Y - 0.01f, view.Bottom - box.Height + 0.01f);
            AssertRowsAreTheirOwnHeight(list);

            // Home and ← close the top folder: one row left, then → brings them all back.
            session.Window.PressKey(KeyCode.Home);
            session.Window.PressKey(KeyCode.Left);
            session.PumpFrames(2);
            Assert.Single(CommitRows(app));
            Assert.Equal(3, list.Children.Count);   // the spacer, the header and the folder
            session.Window.PressKey(KeyCode.Right);
            session.PumpFrames(2);
            Assert.Equal(rows.Count, CommitRows(app).Count);

            // A reader still finds the folders as tree items, open, in a scroll view.
            var tree = Walk(session.Window.AccessibilityTree).Single(n => n.Children.Any(c => c.Name == "160 FILES CHANGED"));
            Assert.Equal(HultUI.Core.Accessibility.AccessibilityRole.ScrollView, tree.Role);
            Assert.Contains(tree.Children, c => c.Role == HultUI.Core.Accessibility.AccessibilityRole.TreeItem && c.Expanded == true);
            Assert.Contains(tree.Children, c => c.Name == "A file000.cs");   // added, the status letter first
        });
    }

    private static VirtualListElement<JunctionApp.CommitTreeRowItem> TreeList(JunctionApp app) =>
        (VirtualListElement<JunctionApp.CommitTreeRowItem>)((HultUI.Core.Reactivity.Ref<ScrollViewElement>)Field(app, "_fileScrollRef")!).Current!;

    /// <summary>The list's realized rows (its first child is the spacer).</summary>
    private static List<Element> Realized(Element list) => list.Children.Skip(1).ToList();

    /// <summary>Each realized row is its padding and its name's line: the height the list gave
    /// it is the one it lays out to.</summary>
    private static void AssertRowsAreTheirOwnHeight(Element list)
    {
        foreach (var row in Realized(list))
        {
            var text = row as TextElement ?? LayoutProbe.Descendants<TextElement>(row, _ => true)
                .OrderByDescending(t => t.Layout.AbsoluteBorderBox.Height).First();
            float natural = text == row
                ? text.Layout.AbsoluteContentBox.Height + 12f
                : text.Layout.AbsoluteBorderBox.Height + 12f;
            Assert.Equal(natural, row.Layout.AbsoluteBorderBox.Height, 0.001f);
        }
    }

    // ── The Changes view's lists ─────────────────────────────────────────

    [Fact]
    public void AFolderRowStagesEveryFileUnderIt()
    {
        Hosted(repo =>
        {
            repo.Write("src/app/main.cs", "one\n");
            repo.Write("src/app/util.cs", "two\n");
            repo.Write("loose.txt", "three\n");
        }, (repo, session, app) =>
        {
            OpenChanges(session, app);

            // The folder's checkbox says what it is about to do, and does it in one call.
            var check = Checkbox(session, "Stage 2 files");
            session.Window.Click(check);
            PumpUntilGit(session, () => repo.Repo.GetStagedChanges().Count == 2,
                "the folder's checkbox did not stage the files under it");

            Assert.Equal(new[] { "src/app/main.cs", "src/app/util.cs" },
                repo.Repo.GetStagedChanges().Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal));
            // Only what was under it: the file beside the folder is untouched.
            Assert.Equal("loose.txt", Assert.Single(repo.Repo.GetUnstagedChanges()).Path);
        });
    }

    [Fact]
    public void SelectingAFolderRowSelectsWhatItStandsFor()
    {
        Hosted(repo =>
        {
            repo.Write("src/app/main.cs", "one\n");
            repo.Write("src/app/util.cs", "two\n");
        }, (repo, session, app) =>
        {
            OpenChanges(session, app);

            // Clicking the folder row selects it (and closes it, as trees do).
            session.Window.Click(FolderRow(session, "Unstaged:dir:src/app"));
            session.PumpFrames(2);
            Assert.Equal("src/app", Assert.Single(SelectedPaths(app)));

            // The staging menu of a file row is not what is under test here — the folder
            // row's own is, and it counts the files rather than the row.
            var stage = FolderMenuItem(app, "src/app", staged: false, "Stage");
            Assert.Equal("Stage 2 files", stage.Label);
            stage.Action!();
            PumpUntilGit(session, () => repo.Repo.GetStagedChanges().Count == 2,
                "staging the folder did not reach both files");
        });
    }

    /// <summary>
    /// A folder row has to resolve to its <i>files</i>, not to its own path. Staging hides
    /// the difference — <c>git add</c> takes a directory perfectly happily — but discard
    /// does not: it looks the paths up in the list it is about to restore, and a directory
    /// path matches nothing there. A folder that silently discarded nothing would be the
    /// worst kind of quiet failure, so this is where the expansion is pinned.
    /// </summary>
    [Fact]
    public void DiscardingAFolderNamesAndRestoresTheFilesUnderIt()
    {
        Hosted(repo =>
        {
            repo.Write("src/app/main.cs", "one\n");
            repo.Write("src/app/util.cs", "two\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", "two tracked files");
            repo.Write("src/app/main.cs", "one edited\n");
            repo.Write("src/app/util.cs", "two edited\n");
        }, (repo, session, app) =>
        {
            OpenChanges(session, app);
            // The folder is deliberately left out of the selection — the caret is on the
            // first file — so the action resolves the row itself rather than the selection.
            Assert.DoesNotContain("src/app", SelectedPaths(app));

            var discard = FolderMenuItem(app, "src/app", staged: false, "Discard");
            Assert.Equal("Discard changes in 2 files", discard.Label);

            discard.Action!();
            session.PumpFrames(2);
            Assert.True(Contains(session, "2 files"),
                "the confirm dialog never said how many files the folder was about to discard.\n"
                + session.Window.DumpAccessibilityTree());

            session.Window.PressKey(KeyCode.Enter);
            PumpUntilGit(session, () => repo.Repo.GetUnstagedChanges().Count == 0,
                "discarding the folder left changes behind");
            Assert.Equal("one\n", repo.Read("src/app/main.cs"));
            Assert.Equal("two\n", repo.Read("src/app/util.cs"));
        });
    }

    /// <summary>
    /// A Shift range that sweeps up a folder and the files inside it must not name any of
    /// them twice — the count on the label is what the user is trusting before they let a
    /// destructive action run.
    /// </summary>
    [Fact]
    public void ARangeOverAFolderAndItsFilesCountsEachFileOnce()
    {
        Hosted(repo =>
        {
            repo.Write("src/app/main.cs", "one\n");
            repo.Write("src/app/util.cs", "two\n");
        }, (repo, session, app) =>
        {
            var unstaged = OpenChanges(session, app);
            Assert.Equal(2, unstaged.Count);

            // The load selects the first file; Home takes the caret to the folder row and
            // Shift+End sweeps the folder and both files into the selection.
            session.Window.PressKey(KeyCode.Home);
            session.Window.PressKey(KeyCode.End, KeyModifiers.Shift);
            session.PumpFrames(2);
            Assert.Equal(3, SelectedPaths(app).Count);   // the folder row and two files

            var stage = MenuItem(app, unstaged[0], isStaged: false, "Stage");
            Assert.Equal("Stage 2 files", stage.Label);
            stage.Action!();
            PumpUntilGit(session, () => repo.Repo.GetStagedChanges().Count == 2,
                "the range did not stage both files");
        });
    }

    /// <summary>
    /// Collapsing a folder hides rows; it must not quietly drop the files under it from the
    /// selection. Pruning runs when a load lands and asks whether a row still exists — a
    /// question about the repository, not about what is currently on screen.
    /// </summary>
    [Fact]
    public void CollapsingAFolderKeepsTheFilesUnderItSelected()
    {
        Hosted(repo =>
        {
            repo.Write("src/app/main.cs", "one\n");
            repo.Write("src/app/util.cs", "two\n");
        }, (repo, session, app) =>
        {
            OpenChanges(session, app);

            // Select both files, then close the folder over them from its own menu (a
            // click on the row would move the selection as well as collapsing).
            session.Window.PressKey(KeyCode.End, KeyModifiers.Shift);
            session.PumpFrames(2);
            Assert.Equal(2, SelectedPaths(app).Count);

            FolderMenuItem(app, "src/app", staged: false, "Collapse").Action!();
            session.PumpFrames(2);
            Assert.Equal(new[] { "src/app" }, VisibleChangeRows(app));

            // Force the reload that prunes, and the selection is still both files.
            Invoke(app, "RefreshAll");
            session.PumpFrames(6);
            Assert.Equal(new[] { "src/app/main.cs", "src/app/util.cs" }, SelectedPaths(app));
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    /// <summary>Opens the Commit view on HEAD and waits for its file tree, returning the
    /// visible rows.</summary>
    private static List<TreeNode<GitFileChange>> OpenCommit(HeadlessSession session, JunctionApp app)
    {
        session.Window.PressKey(KeyCode.Digit3);
        try
        {
            session.PumpUntil(() => CommitRows(app).Count > 0, TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException e)
        {
            throw new Xunit.Sdk.XunitException(
                $"the Commit view never built a file tree.\n{e.Message}\n"
                + session.Window.DumpAccessibilityTree());
        }
        return CommitRows(app);
    }

    /// <summary>Opens the Changes view and waits for its load, returning the repository's
    /// unstaged files.</summary>
    private static IReadOnlyList<GitFileChange> OpenChanges(HeadlessSession session, JunctionApp app)
    {
        session.Window.PressKey(KeyCode.Digit4);
        try
        {
            session.PumpUntil(() => Field(app, "_changeSelPath") != null, TimeSpan.FromSeconds(20));
            // The working tree is preloaded at startup, so the selection can already be set
            // when the key goes down and the wait above returns before the view has had a
            // frame. The rows are what the tests click; wait for them to have bounds.
            session.PumpUntil(() => app.View == JunctionView.Changes
                && LayoutProbe.Descendants<BoxElement>(Root(session),
                    e => e.Key is string k && e.Layout.AbsoluteBorderBox.Height > 0
                         && (k.StartsWith("u:", StringComparison.Ordinal)
                             || k.StartsWith("Unstaged:dir:", StringComparison.Ordinal))).Any(),
                TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException e)
        {
            throw new Xunit.Sdk.XunitException(
                $"the Changes view never loaded a file list.\n{e.Message}\n"
                + session.Window.DumpAccessibilityTree());
        }
        // The app's own list, in the order it shows: the tests index into it the way the
        // rows do.
        var data = ChangesValue(app);
        return (IReadOnlyList<GitFileChange>)data.GetType().GetProperty("Unstaged")!.GetValue(data)!;
    }

    private static void PumpUntilGit(HeadlessSession session, Func<bool> done, string message)
    {
        try
        {
            session.PumpUntil(done, TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException e)
        {
            throw new Xunit.Sdk.XunitException($"{message}.\n{e.Message}");
        }
    }

    private static List<TreeNode<GitFileChange>> CommitRows(JunctionApp app) =>
        (List<TreeNode<GitFileChange>>)Field(app, "_commitFileRows")!;

    private static string? Cursor(JunctionApp app) => (string?)Field(app, "_commitCursorPath");

    private static string? SelectedFile(JunctionApp app) => (string?)Field(app, "_selectedFilePath");

    /// <summary>The Changes list's visible rows, as their keys read on screen. Folder rows
    /// are the ones whose key carries the directory, so a collapsed list is legible here
    /// without reaching into the private row model.</summary>
    private static string[] VisibleChangeRows(JunctionApp app)
    {
        var rows = (IEnumerable)Invoke(app, "ChangeRows", ChangesValue(app))!;
        return rows.Cast<object>()
            .Select(r => (string)r.GetType().GetProperty("Path")!.GetValue(r)!)
            .ToArray();
    }

    /// <summary>The loaded ChangesData. <c>_changes</c> is a property over the per-repo
    /// loader, not a field.</summary>
    private static object ChangesValue(JunctionApp app)
    {
        var changes = typeof(JunctionApp)
            .GetProperty("_changes", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;
        return changes.GetType().GetProperty("Value")!.GetValue(changes)!;
    }

    private static List<string> SelectedPaths(JunctionApp app) =>
        ((IEnumerable)Field(app, "_changeSelection")!)
            .Cast<object>()
            .Select(r => (string)r.GetType().GetProperty("Path")!.GetValue(r)!)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

    /// <summary>A folder row's context menu, built the way a right-click builds it.</summary>
    private static ContextMenuItem FolderMenuItem(JunctionApp app, string folderPath, bool staged, string startsWith)
    {
        var section = SectionValue(staged ? "Staged" : "Unstaged");
        var files = (IReadOnlyList<GitFileChange>)Invoke(app, "SectionFiles", ChangesValue(app), section)!;
        var rows = (IEnumerable)Invoke(app, "ChangeTreeRows", section, files)!;
        var node = rows.Cast<TreeNode<GitFileChange>>().First(n => n.IsFolder && n.Path == folderPath);
        var items = (IReadOnlyList<ContextMenuItem>)Invoke(app, "ChangeFolderMenuItems", section, node)!;
        return items.First(i => i.Label.StartsWith(startsWith, StringComparison.Ordinal));
    }

    private static ContextMenuItem MenuItem(JunctionApp app, GitFileChange file, bool isStaged, string startsWith)
    {
        var items = (IReadOnlyList<ContextMenuItem>)Invoke(app, "ChangeFileMenuItems", file, isStaged)!;
        return items.First(i => i.Label.StartsWith(startsWith, StringComparison.Ordinal));
    }

    /// <summary>The private ChangeSection enum's member by name.</summary>
    private static object SectionValue(string name)
    {
        var type = typeof(JunctionApp).GetNestedType("ChangeSection", BindingFlags.NonPublic)!;
        return Enum.Parse(type, name);
    }

    private static Element FolderRow(HeadlessSession session, string keyPrefix)
    {
        var row = LayoutProbe.Descendants<BoxElement>(Root(session),
            e => e.Key is string k && k.StartsWith(keyPrefix, StringComparison.Ordinal)).FirstOrDefault();
        Assert.True(row is not null,
            $"no folder row keyed \"{keyPrefix}\".\n{session.Window.DumpAccessibilityTree()}");
        return row!;
    }

    private static Element Checkbox(HeadlessSession session, string tooltip)
    {
        var box = LayoutProbe.Descendants<BoxElement>(Root(session), e => e.TooltipText == tooltip)
            .FirstOrDefault();
        Assert.True(box is not null,
            $"no row offered \"{tooltip}\".\n{session.Window.DumpAccessibilityTree()}");
        return box!;
    }

    private static bool Contains(HeadlessSession session, string text) =>
        Walk(session.Window.AccessibilityTree).Any(n =>
            n.Name?.Contains(text, StringComparison.Ordinal) == true
            || n.Value?.Contains(text, StringComparison.Ordinal) == true);

    private static IEnumerable<HultUI.Core.Accessibility.AccessibilityNode> Walk(
        HultUI.Core.Accessibility.AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window)
            .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session.Window.Window);

    private static object? Field(JunctionApp app, string name) =>
        typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app);

    private static object? Invoke(JunctionApp app, string name, params object?[] args) =>
        typeof(JunctionApp)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .First(m => m.Name == name && m.GetParameters().Length == args.Length)
            .Invoke(app, args);

    private static void Hosted(Action<TempRepo> arrange, Action<TempRepo, HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        arrange(repo);

        using var session = HeadlessSession.Create(1440, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, new JunctionSettingsData
            {
                RepositoryPaths = [repo.Path],
                ActiveRepoPath = repo.Path,
                Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
                ActiveWorkspaceId = "w1",
            });
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            body(repo, session, app);
        }
        finally { app?.Dispose(); }
    }
}
