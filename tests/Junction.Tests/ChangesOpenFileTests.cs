using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Opening the selected file itself — the Changes list's "Open file" entry and Ctrl+O — and
/// the choice of sides Ctrl+D hands the external diff.
///
/// <para>Nothing here starts a process. What can actually break is upstream of the launch:
/// whether the entry is offered, whether the key reaches the file the caret is on, and which
/// two things get compared. The unstaged comparison is the one worth pinning — its right-hand
/// side has to be the file in the repository, not a temp copy, or an edit made while reading
/// the diff is deleted along with the temp directory when the editor closes.</para>
/// </summary>
[Collection("JunctionDiff")]
public class ChangesOpenFileTests
{
    private static T Get<T>(JunctionApp app, string field) =>
        (T)typeof(JunctionApp).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;

    private static object? Invoke(JunctionApp app, string name, params object?[] args) =>
        typeof(JunctionApp).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, args);

    // ── The menu entry ───────────────────────────────────────────────────

    [Fact]
    public void TheChangesMenuOffersOpeningTheFileWithItsShortcut()
    {
        Hosted((repo, session, app) =>
        {
            var file = new GitFileChange("tracked.txt", null, FileChangeKind.Modified, 1, 1);
            var items = (IReadOnlyList<ContextMenuItem>)Invoke(
                app, "ChangeFileMenuItems", file, false)!;

            var open = Assert.Single(items, i => i.Label == "Open file");
            // The key is advertised where the action is, so it is learnable at all.
            Assert.Equal("Ctrl O", open.ShortcutHint);
            Assert.False(open.IsDisabled);
        });
    }

    // ── Ctrl+O ───────────────────────────────────────────────────────────

    [Fact]
    public void CtrlOWithNothingSelectedSaysSoRatherThanLaunchingAnything()
    {
        Hosted((repo, session, app) =>
        {
            typeof(JunctionApp).GetField("_changeSelPath", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(app, null);

            session.Window.PressKey(KeyCode.O, KeyModifiers.Ctrl);
            session.PumpFrames(2);

            Assert.Equal("Select a file to open first", Get<string?>(app, "_toast"));
        });
    }

    /// <summary>The Commit view lists a commit's files, and a commit is not a folder. A row
    /// for a file the working tree no longer has must say so rather than handing the desktop
    /// a path that is not there.</summary>
    [Fact]
    public void CtrlOOnAFileTheWorkingTreeNoLongerHasSaysSo()
    {
        Hosted((repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit3);   // Commit
            session.PumpFrames(4);
            typeof(JunctionApp).GetField("_selectedFilePath", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(app, "deleted-since.txt");

            session.Window.PressKey(KeyCode.O, KeyModifiers.Ctrl);
            session.PumpUntil(() => Get<string?>(app, "_toast") != null, TimeSpan.FromSeconds(20));

            Assert.Equal("deleted-since.txt isn't in the working tree", Get<string?>(app, "_toast"));
        });
    }

    // ── Which two things the external diff compares ──────────────────────

    [Fact]
    public void TheUnstagedComparisonPointsAtTheFileInTheRepository()
    {
        Hosted((repo, session, app) =>
        {
            repo.Write("tracked.txt", "edited in the worktree\n");

            var request = BuildRequest(repo, "tracked.txt", "Unstaged");

            Assert.Equal(Path.Combine(repo.Path, "tracked.txt"), request.RightPath);
            Assert.True(File.Exists(request.RightPath));
            // The content still travels with the request; the launcher only prefers the path
            // when it is really there, so a deleted file still has a right-hand side.
            Assert.Equal("edited in the worktree\n", request.RightContent.ReplaceLineEndings("\n"));
        });
    }

    [Fact]
    public void TheStagedAndCommitComparisonsStayOnTempCopies()
    {
        Hosted((repo, session, app) =>
        {
            // Neither side of these is the working tree, so neither may be edited in place:
            // "index" and "HEAD" are not files anyone can save over.
            Assert.Null(BuildRequest(repo, "tracked.txt", "Staged").RightPath);
            Assert.Null(BuildRequest(repo, "tracked.txt", "Commit", repo.Head).RightPath);
        });
    }

    /// <summary>A file that git knows about and the disk does not. The path is still handed
    /// over — the launcher is what decides it is not there — so this pins that the decision
    /// is left to the one place that can check.</summary>
    [Fact]
    public void ADeletedFileStillCarriesItsPathAndAnEmptyRightHandSide()
    {
        Hosted((repo, session, app) =>
        {
            File.Delete(Path.Combine(repo.Path, "tracked.txt"));

            var request = BuildRequest(repo, "tracked.txt", "Unstaged");

            Assert.Equal(Path.Combine(repo.Path, "tracked.txt"), request.RightPath);
            Assert.False(File.Exists(request.RightPath));
            Assert.Equal("", request.RightContent);
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    /// <summary>Calls the app's own side-picking, which is private and nested-enum-typed —
    /// hence the name lookup for the mode.</summary>
    private static ExternalDiffRequest BuildRequest(
        TempRepo repo, string path, string mode, string? commitHash = null)
    {
        var modeType = typeof(JunctionApp).GetNestedType("ExternalDiffMode",
            BindingFlags.NonPublic)!;
        var build = typeof(JunctionApp).GetMethod("BuildExternalDiffRequest",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return (ExternalDiffRequest)build.Invoke(null,
            [repo.Repo, path, Enum.Parse(modeType, mode), commitHash, null])!;
    }

    private static void Hosted(Action<TempRepo, HeadlessSession, JunctionApp> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("tracked.txt", "one\ntwo\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "a starting point");

        var settings = new JunctionSettingsData
        {
            RepositoryPaths = [repo.Path],
            ActiveRepoPath = repo.Path,
            Workspaces = [new WorkspaceData { Id = "w1", Name = "Tests", RepoPaths = [repo.Path] }],
            ActiveWorkspaceId = "w1",
        };

        using var session = HeadlessSession.Create(1440, 900);
        JunctionApp? app = null;
        try
        {
            app = new JunctionApp(session.Application, session.Window.Window, settings);
            session.Application.SetRoot(app.Mount());
            session.PumpFrame();
            session.Window.PressKey(KeyCode.Digit4);   // Changes
            session.PumpFrames(6);
            body(repo, session, app);
        }
        finally { app?.Dispose(); }
    }
}
