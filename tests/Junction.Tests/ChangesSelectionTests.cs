using System.Collections;
using System.Reflection;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Multi-selection in the Changes file list, hosted headlessly against a real repository.
///
/// <para>Two things are under test and they fail differently. The selection state machine
/// (what Shift, Ctrl and the arrows do to the set) is asserted directly off the app's own
/// state — a rendered highlight would prove the paint, not the model, and the model is what
/// the staging actions read. The actions themselves are then run through the real context
/// menu and asserted against git, because "stage three files" is only true if the index
/// says so afterwards.</para>
///
/// <para>The selection is deliberately confined to ONE section: staged, unstaged and
/// conflicted rows never select together, because a set that straddles the staged line has
/// no single staging direction. Several tests below pin that boundary.</para>
/// </summary>
public class ChangesSelectionTests
{
    // ── The selection state machine ──────────────────────────────────────

    [Fact]
    public void ShiftDownExtendsTheSelectionFromTheAnchorAndCarriesTheCaret()
    {
        Hosted(repo =>
        {
            repo.Write("a.txt", "a\n");
            repo.Write("b.txt", "b\n");
            repo.Write("c.txt", "c\n");
        }, (repo, session, app) =>
        {
            var unstaged = OpenChanges(repo, session, app);
            Assert.Equal(3, unstaged.Count);

            // The first file is selected on load; Shift+Down twice takes the run to three.
            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.PumpFrame();

            Assert.Equal(unstaged.Select(f => f.Path).OrderBy(p => p),
                SelectedPaths(app).OrderBy(p => p));
            // The caret follows the far end of the range — that is the file the diff
            // pane shows, and with a multi-selection it must not go blank.
            Assert.Equal(unstaged[2].Path, CaretPath(app));
        });
    }

    [Fact]
    public void ShiftEndStopsAtTheEndOfTheAnchorsSection()
    {
        Hosted(repo =>
        {
            repo.Write("staged-1.txt", "1\n");
            repo.Write("staged-2.txt", "2\n");
            repo.Git("add", "-A");
            repo.Write("loose-1.txt", "3\n");
            repo.Write("loose-2.txt", "4\n");
        }, (repo, session, app) =>
        {
            OpenChanges(repo, session, app);
            // The load selects the first staged file, so the anchor is in Staged.
            Assert.Equal("staged-1.txt", CaretPath(app));

            session.Window.PressKey(KeyCode.End, KeyModifiers.Shift);
            session.PumpFrame();

            // End would run to the last unstaged row; the extend stops at the section
            // edge instead of dragging two worktree files into a staged selection.
            Assert.Equal(new[] { "staged-1.txt", "staged-2.txt" }, SelectedPaths(app));
            Assert.Equal("staged-2.txt", CaretPath(app));
        });
    }

    [Fact]
    public void APlainArrowCrossesIntoTheNextSectionAsAFreshSingleSelection()
    {
        Hosted(repo =>
        {
            repo.Write("staged-1.txt", "1\n");
            repo.Git("add", "-A");
            repo.Write("loose-1.txt", "2\n");
        }, (repo, session, app) =>
        {
            OpenChanges(repo, session, app);
            Assert.Equal("staged-1.txt", CaretPath(app));

            session.Window.PressKey(KeyCode.Down);
            session.PumpFrame();

            // Crossing is allowed — it just can't carry the old section's rows along.
            Assert.Equal("loose-1.txt", Assert.Single(SelectedPaths(app)));
        });
    }

    [Fact]
    public void CtrlArrowsMoveTheCaretWithoutDisturbingTheSelection()
    {
        Hosted(repo =>
        {
            repo.Write("a.txt", "a\n");
            repo.Write("b.txt", "b\n");
        }, (repo, session, app) =>
        {
            var unstaged = OpenChanges(repo, session, app);

            session.Window.PressKey(KeyCode.Down, KeyModifiers.Ctrl);
            session.PumpFrame();

            Assert.Equal(unstaged[0].Path, Assert.Single(SelectedPaths(app)));
            Assert.Equal(unstaged[1].Path, CaretPath(app));
        });
    }

    [Fact]
    public void CtrlSpaceTogglesTheFocusedRowAndMayEmptyTheSelection()
    {
        Hosted(repo =>
        {
            repo.Write("a.txt", "a\n");
            repo.Write("b.txt", "b\n");
        }, (repo, session, app) =>
        {
            var unstaged = OpenChanges(repo, session, app);

            // Ctrl+↓ then Ctrl+Space adds the second file without losing the first.
            session.Window.PressKey(KeyCode.Down, KeyModifiers.Ctrl);
            session.Window.PressKey(KeyCode.Space, KeyModifiers.Ctrl);
            session.PumpFrame();
            Assert.Equal(2, SelectedPaths(app).Count);

            // Toggling it back off leaves the caret where it is — an empty selection is
            // a legitimate state here, exactly as in a Windows list.
            session.Window.PressKey(KeyCode.Space, KeyModifiers.Ctrl);
            session.PumpFrame();
            Assert.Equal(unstaged[0].Path, Assert.Single(SelectedPaths(app)));
            Assert.Equal(unstaged[1].Path, CaretPath(app));
        });
    }

    // ── The actions the selection drives ─────────────────────────────────

    [Fact]
    public void TheContextMenuStagesEverySelectedFileAndPluralizesItsLabel()
    {
        Hosted(repo =>
        {
            repo.Write("a.txt", "a\n");
            repo.Write("b.txt", "b\n");
            repo.Write("c.txt", "c\n");
        }, (repo, session, app) =>
        {
            var unstaged = OpenChanges(repo, session, app);
            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.PumpFrame();

            var stage = MenuItem(app, unstaged[1], isStaged: false, "Stage");
            Assert.Equal("Stage 3 files", stage.Label);

            stage.Action!();
            PumpUntilGit(session, () => repo.Repo.GetStagedChanges().Count == 3,
                "the three selected files never reached the index");
            Assert.Empty(repo.Repo.GetUnstagedChanges());

            // The selection follows the files into the staged list rather than emptying.
            // Waiting for the reloaded list to show three staged files is what makes
            // this the real claim: the selection has to survive the reload that moved
            // them, not merely the instant before it.
            PumpUntilGit(session, () => Contains(session, "STAGED · 3"),
                "the reloaded list never showed three staged files");
            Assert.Equal(3, SelectedPaths(app).Count);
        });
    }

    [Fact]
    public void TheContextMenuUnstagesEverySelectedFile()
    {
        Hosted(repo =>
        {
            repo.Write("a.txt", "a\n");
            repo.Write("b.txt", "b\n");
            repo.Git("add", "-A");
        }, (repo, session, app) =>
        {
            var staged = repo.Repo.GetStagedChanges();
            OpenChanges(repo, session, app);
            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.PumpFrame();
            Assert.Equal(2, SelectedPaths(app).Count);

            var unstage = MenuItem(app, staged[0], isStaged: true, "Unstage");
            Assert.Equal("Unstage 2 files", unstage.Label);

            unstage.Action!();
            PumpUntilGit(session, () => repo.Repo.GetStagedChanges().Count == 0,
                "the selected files were never unstaged");
            Assert.Equal(2, repo.Repo.GetUnstagedChanges().Count);
        });
    }

    /// <summary>
    /// Discard is the destructive one. It must name the count before it runs — a
    /// multi-selection discard that fired straight off the menu would be the worst
    /// accident this list can produce, and git has nothing to restore from.
    /// </summary>
    [Fact]
    public void DiscardingASelectionConfirmsWithTheCountBeforeTouchingTheWorkingTree()
    {
        Hosted(repo =>
        {
            repo.Write("tracked-1.txt", "one\n");
            repo.Write("tracked-2.txt", "two\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", "two tracked files");
            repo.Write("tracked-1.txt", "one edited\n");
            repo.Write("tracked-2.txt", "two edited\n");
        }, (repo, session, app) =>
        {
            var unstaged = OpenChanges(repo, session, app);
            Assert.Equal(2, unstaged.Count);
            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.PumpFrame();

            var discard = MenuItem(app, unstaged[0], isStaged: false, "Discard");
            Assert.Equal("Discard changes in 2 files", discard.Label);

            discard.Action!();
            session.PumpFrame();

            // Nothing has happened yet: the prompt is up and the edits are still on disk.
            Assert.Equal("one edited\n", repo.Read("tracked-1.txt"));
            Assert.True(Contains(session, "2 files"),
                "the confirm dialog never said how many files it was about to discard.\n"
                + session.Window.DumpAccessibilityTree());

            session.Window.PressKey(KeyCode.Enter);
            PumpUntilGit(session, () => repo.Repo.GetUnstagedChanges().Count == 0,
                "confirming the dialog did not discard both files");
            Assert.Equal("one\n", repo.Read("tracked-1.txt"));
            Assert.Equal("two\n", repo.Read("tracked-2.txt"));
        });
    }

    [Fact]
    public void EscapeLeavesTheConfirmDialogAndTheWorkingTreeAlone()
    {
        Hosted(repo =>
        {
            repo.Write("tracked.txt", "one\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", "a tracked file");
            repo.Write("tracked.txt", "edited\n");
        }, (repo, session, app) =>
        {
            var unstaged = OpenChanges(repo, session, app);

            var discard = MenuItem(app, unstaged[0], isStaged: false, "Discard");
            Assert.Equal("Discard changes", discard.Label);   // singular, one file selected

            discard.Action!();
            session.PumpFrame();
            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(3);

            Assert.Equal("edited\n", repo.Read("tracked.txt"));
        });
    }

    // ── Delete discards ──────────────────────────────────────────────────

    /// <summary>
    /// Delete is the key every file list uses to get rid of what the caret is on, and on the
    /// Changes list "get rid of it" means discard. It goes through the same confirm the menu
    /// does — a keystroke that destroyed a selection outright would be the worst thing on
    /// this screen — and it aims at the whole selection, as the menu does.
    /// </summary>
    [Fact]
    public void DeleteDiscardsTheWholeSelectionThroughTheSameConfirm()
    {
        Hosted(repo =>
        {
            repo.Write("tracked-1.txt", "one\n");
            repo.Write("tracked-2.txt", "two\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", "two tracked files");
            repo.Write("tracked-1.txt", "one edited\n");
            repo.Write("tracked-2.txt", "two edited\n");
        }, (repo, session, app) =>
        {
            Assert.Equal(2, OpenChanges(repo, session, app).Count);
            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.PumpFrame();

            session.Window.PressKey(KeyCode.Delete);
            session.PumpFrame();

            // Still only a question: the edits are on disk until the dialog is answered.
            Assert.Equal("one edited\n", repo.Read("tracked-1.txt"));
            Assert.True(Contains(session, "2 files"),
                "Delete did not raise the discard confirm naming both files.\n"
                + session.Window.DumpAccessibilityTree());

            session.Window.PressKey(KeyCode.Enter);
            PumpUntilGit(session, () => repo.Repo.GetUnstagedChanges().Count == 0,
                "confirming the Delete prompt did not discard both files");
            Assert.Equal("one\n", repo.Read("tracked-1.txt"));
            Assert.Equal("two\n", repo.Read("tracked-2.txt"));
        });
    }

    /// <summary>Escape backs out of it, leaving the worktree untouched — the same escape
    /// hatch the menu route has, since it is the same dialog.</summary>
    [Fact]
    public void EscapingTheDeletePromptLeavesTheWorkingTreeAlone()
    {
        Hosted(repo =>
        {
            repo.Write("tracked.txt", "one\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", "a tracked file");
            repo.Write("tracked.txt", "edited\n");
        }, (repo, session, app) =>
        {
            OpenChanges(repo, session, app);

            session.Window.PressKey(KeyCode.Delete);
            session.PumpFrame();
            session.Window.PressKey(KeyCode.Escape);
            session.PumpFrames(3);

            Assert.Equal("edited\n", repo.Read("tracked.txt"));
        });
    }

    /// <summary>Discard is defined against the worktree, so a staged row says to unstage
    /// first rather than silently doing nothing — and above all does not discard.</summary>
    [Fact]
    public void DeleteOnAStagedRowExplainsItselfInsteadOfDiscarding()
    {
        Hosted(repo =>
        {
            repo.Write("staged.txt", "one\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", "a tracked file");
            repo.Write("staged.txt", "edited\n");
            repo.Git("add", "-A");
        }, (repo, session, app) =>
        {
            OpenChanges(repo, session, app);
            Assert.Equal("staged.txt", CaretPath(app));

            session.Window.PressKey(KeyCode.Delete);
            session.PumpFrames(3);

            Assert.Equal("edited\n", repo.Read("staged.txt"));
            Assert.Single(repo.Repo.GetStagedChanges());
            Assert.True(Contains(session, "Unstage it first"),
                "Delete on a staged row said nothing about why it did not discard.\n"
                + session.Window.DumpAccessibilityTree());
        });
    }

    /// <summary>
    /// Conflicts are the third section of the same list, so they select like the rest —
    /// and taking one whole side generalizes to the selection the way staging does.
    /// </summary>
    [Fact]
    public void ConflictedFilesSelectTogetherAndResolveAsASet()
    {
        Hosted(repo =>
        {
            repo.Write("one.txt", "base\n");
            repo.Write("two.txt", "base\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", "base");
            repo.Git("checkout", "-b", "feature");
            repo.Write("one.txt", "theirs\n");
            repo.Write("two.txt", "theirs\n");
            repo.Git("commit", "-am", "feature edits");
            repo.Git("checkout", "main");
            repo.Write("one.txt", "ours\n");
            repo.Write("two.txt", "ours\n");
            repo.Git("commit", "-am", "main edits");
            repo.Repo.Merge("feature");   // conflicts on both files; a failing merge is the point
            Assert.True(repo.Repo.IsMerging());
            Assert.Equal(2, repo.Repo.GetConflictedFiles().Count);
        }, (repo, session, app) =>
        {
            var conflicted = repo.Repo.GetConflictedFiles();
            session.Window.PressKey(KeyCode.Digit4);
            session.PumpUntil(() => CaretPath(app) != null, TimeSpan.FromSeconds(20));
            // The load prefers a conflict, so the caret starts in that section.
            Assert.Equal(conflicted[0].Path, CaretPath(app));

            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.PumpFrame();
            Assert.Equal(2, SelectedPaths(app).Count);

            var build = typeof(JunctionApp).GetMethod("ConflictFileMenuItems",
                BindingFlags.NonPublic | BindingFlags.Instance)!;
            var items = (IReadOnlyList<ContextMenuItem>)build.Invoke(app, [conflicted[0]])!;
            var ours = items.First(i => i.Label.StartsWith("Use ours", StringComparison.Ordinal));
            Assert.Equal("Use ours (current) for 2 files", ours.Label);

            ours.Action!();
            PumpUntilGit(session, () => repo.Repo.GetConflictedFiles().Count == 0,
                "taking ours for the selection left a conflict behind");
            Assert.Equal("ours\n", repo.Read("one.txt"));
            Assert.Equal("ours\n", repo.Read("two.txt"));
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    /// <summary>Switches to the Changes view and waits for its (background) load to land,
    /// returning the repository's unstaged files in list order.</summary>
    private static IReadOnlyList<GitFileChange> OpenChanges(
        TempRepo repo, HeadlessSession session, JunctionApp app)
    {
        session.Window.PressKey(KeyCode.Digit4);
        Assert.Equal(JunctionView.Changes, app.View);
        // A file is selected only once the load has produced a list, so the caret is
        // the signal that the view is real rather than an empty first frame.
        try
        {
            session.PumpUntil(() => CaretPath(app) != null, TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException e)
        {
            throw new Xunit.Sdk.XunitException(
                $"the Changes view never loaded a file list.\n{e.Message}\n"
                + session.Window.DumpAccessibilityTree());
        }
        return repo.Repo.GetUnstagedChanges();
    }

    /// <summary>Waits for a git-visible effect of an action the app ran, then gives the
    /// app a couple of frames to catch up with it.</summary>
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

    /// <summary>The real context-menu item a right-click on <paramref name="file"/> would
    /// offer, so the label and the action under test are the ones a user gets.</summary>
    private static ContextMenuItem MenuItem(JunctionApp app, GitFileChange file, bool isStaged, string startsWith)
    {
        var build = typeof(JunctionApp).GetMethod("ChangeFileMenuItems",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var items = (IReadOnlyList<ContextMenuItem>)build.Invoke(app, [file, isStaged])!;
        return items.First(i => i.Label.StartsWith(startsWith, StringComparison.Ordinal));
    }

    private static string? CaretPath(JunctionApp app) => (string?)Field(app, "_changeSelPath");

    /// <summary>The selection set's paths. ChangeRef is a private nested type, so the
    /// rows are read reflectively rather than cast.</summary>
    private static List<string> SelectedPaths(JunctionApp app) =>
        ((IEnumerable)Field(app, "_changeSelection")!)
            .Cast<object>()
            .Select(r => (string)r.GetType().GetProperty("Path")!.GetValue(r)!)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

    private static object? Field(JunctionApp app, string name) =>
        typeof(JunctionApp).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app);

    private static bool Contains(HeadlessSession session, string text) =>
        Walk(session.Window.AccessibilityTree).Any(n =>
            n.Name?.Contains(text, StringComparison.Ordinal) == true
            || n.Value?.Contains(text, StringComparison.Ordinal) == true);

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child))
                yield return descendant;
    }

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
