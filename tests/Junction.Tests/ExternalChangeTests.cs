using System.Collections;
using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// What a change on disk does to the selection in each view, hosted headlessly.
///
/// <para>The watcher's callback is invoked directly rather than through a
/// <c>FileSystemWatcher</c>: the debounce is real time and the events are the OS's, and
/// neither is what is under test. Everything from the callback on — the generation bump,
/// the reload, the callback that picks a selection — is the production path.</para>
///
/// <para>A "refused" reload stands for the case that moved the selection in the field:
/// <c>git diff</c> dying with "cannot hash" on a file its writer still had open. Locking a
/// file only refuses git on Windows, so the tests refuse it portably with a broken
/// <c>.git/config</c> instead — every command fails the same way, and the fix does not care
/// which one did.</para>
/// </summary>
public class ExternalChangeTests
{
    private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    // ── History ──────────────────────────────────────────────────────────

    [Fact]
    public void AnExternalCommitKeepsTheSelectedCommit()
    {
        Hosted(repo =>
        {
            Commit(repo, "second");
            Commit(repo, "third");
        }, (repo, session, app) =>
        {
            var selected = OpenHistoryOnTheSecondCommit(repo, session, app);

            Commit(repo, "fourth");
            ExternalChange(app, repo);
            PumpUntil(session, () => Commits(app, repo.Path) == 4, "the external commit never showed");

            Assert.Equal(selected, SelectedCommit(app));
        });
    }

    [Fact]
    public void ARefusedReloadKeepsTheHistoryAndItsSelectionUntilGitAnswersAgain()
    {
        Hosted(repo =>
        {
            Commit(repo, "second");
            Commit(repo, "third");
        }, (repo, session, app) =>
        {
            var selected = OpenHistoryOnTheSecondCommit(repo, session, app);

            Commit(repo, "fourth");
            RefuseGit(repo);
            ExternalChange(app, repo);
            session.PumpFrames(6);

            // The log git refused is not an empty log: the three commits stay up and the
            // selection with them. (It used to land as "no commits", drop the selection,
            // and let the next load open on the newest commit.)
            Assert.Equal(3, Commits(app, repo.Path));
            Assert.Equal(selected, SelectedCommit(app));
            Assert.True(Shows(session, "third"), "the commit list blanked on a refused reload");

            // Once git answers again the refused load is retried on its own — the event
            // that raced the writer may have been the last one.
            RestoreGit(repo);
            PumpUntil(session, () => Commits(app, repo.Path) == 4, "the refused load was never retried");
            Assert.Equal(selected, SelectedCommit(app));
        });
    }

    // ── Changes ──────────────────────────────────────────────────────────

    [Fact]
    public void AWriteElsewhereKeepsTheSelectedChange()
    {
        Hosted(repo =>
        {
            repo.Write("a.txt", "a\n");
            repo.Write("b.txt", "b\n");
        }, (repo, session, app) =>
        {
            OpenChangesOnTheSecondFile(session, app);

            repo.Write("c.txt", "c\n");
            ExternalChange(app, repo);
            PumpUntil(session, () => Unstaged(app, repo.Path) == 3, "the new file never showed");

            Assert.Equal("b.txt", CaretPath(app));
        });
    }

    [Fact]
    public void ARefusedReloadKeepsTheSelectedChangeUntilGitAnswersAgain()
    {
        Hosted(repo =>
        {
            repo.Write("a.txt", "a\n");
            repo.Write("b.txt", "b\n");
        }, (repo, session, app) =>
        {
            OpenChangesOnTheSecondFile(session, app);

            repo.Write("c.txt", "c\n");
            RefuseGit(repo);
            ExternalChange(app, repo);
            session.PumpFrames(6);

            Assert.Equal(2, Unstaged(app, repo.Path));
            Assert.Equal("b.txt", CaretPath(app));

            RestoreGit(repo);
            PumpUntil(session, () => Unstaged(app, repo.Path) == 3, "the refused load was never retried");
            Assert.Equal("b.txt", CaretPath(app));
        });
    }

    // ── Files ────────────────────────────────────────────────────────────

    [Fact]
    public void AChangeOnDiskLeavesTheFilesTreeAsItWas()
    {
        Hosted(repo =>
        {
            repo.Write("kept.cs", "class Kept { }\n");
            repo.Write("sub/nested.cs", "class Nested { }\n");
            Commit(repo, "three paths");
        }, (repo, session, app) =>
        {
            session.Window.PressKey(KeyCode.Digit7);   // Files
            PumpUntil(session, () => FilesSelection(app) != null, "the Files view never loaded a tree");

            // Park the cursor on the folder (folders sort first) and open it, so both the
            // cursor and an expansion are there to lose.
            session.Window.PressKey(KeyCode.Home);
            session.PumpFrame();
            Assert.Equal("sub", FilesCursor(app));
            session.Window.PressKey(KeyCode.Right);
            session.PumpFrame();
            var rows = FileRows(app);
            Assert.Contains("sub/nested.cs", rows);
            var cursor = FilesCursor(app);
            var selected = FilesSelection(app);
            Assert.Equal("sub", cursor);

            repo.Write("kept.cs", "class Kept { int x; }\n");
            ExternalChange(app, repo);

            // Frame by frame through the reload: the tree it had at every one of them,
            // never an empty one while the new listing is on its way. A fresh loader slot
            // per generation used to blank it for a frame, and that frame reset the cursor
            // and closed the folder.
            for (var frame = 0; frame < 8; frame++)
            {
                session.PumpFrame();
                Assert.Equal(rows, FileRows(app));
                Assert.Equal(cursor, FilesCursor(app));
                Assert.Equal(selected, FilesSelection(app));
            }
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private static string OpenHistoryOnTheSecondCommit(TempRepo repo, HeadlessSession session, JunctionApp app)
    {
        session.Window.PressKey(KeyCode.Digit2);
        Assert.Equal(JunctionView.History, app.View);
        PumpUntil(session, () => Commits(app, repo.Path) == 3 && Shows(session, "third"),
            "the History view never loaded the log");
        session.Window.PressKey(KeyCode.Down);
        session.PumpFrame();
        var selected = SelectedCommit(app);
        Assert.NotNull(selected);
        Assert.Equal(repo.Git("rev-parse", "HEAD~1").Stdout.Trim(), selected);
        return selected!;
    }

    private static void OpenChangesOnTheSecondFile(HeadlessSession session, JunctionApp app)
    {
        session.Window.PressKey(KeyCode.Digit4);
        Assert.Equal(JunctionView.Changes, app.View);
        PumpUntil(session, () => CaretPath(app) != null, "the Changes view never loaded a file list");
        session.Window.PressKey(KeyCode.Down);
        session.PumpFrame();
        Assert.Equal("b.txt", CaretPath(app));
    }

    private static void Commit(TempRepo repo, string subject)
    {
        repo.Write($"{subject}.txt", $"{subject}\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", subject);
    }

    /// <summary>What the watcher does when the repository changed on disk.</summary>
    private static void ExternalChange(JunctionApp app, TempRepo repo) =>
        typeof(JunctionApp).GetMethod("OnExternalChange", Priv)!.Invoke(app, [repo.Path]);

    private const string BrokenConfig = "\n[broken\n";

    private static string ConfigPath(TempRepo repo) => Path.Combine(repo.Path, ".git", "config");

    /// <summary>Makes git refuse every command in the repository ("bad config line").</summary>
    private static void RefuseGit(TempRepo repo) => File.AppendAllText(ConfigPath(repo), BrokenConfig);

    private static void RestoreGit(TempRepo repo) =>
        File.WriteAllText(ConfigPath(repo), File.ReadAllText(ConfigPath(repo)).Replace(BrokenConfig, ""));

    private static object? Field(JunctionApp app, string name) =>
        typeof(JunctionApp).GetField(name, Priv)!.GetValue(app);

    private static string? SelectedCommit(JunctionApp app) => (string?)Field(app, "_selectedCommitHash");
    private static string? CaretPath(JunctionApp app) => (string?)Field(app, "_changeSelPath");
    private static string? FilesSelection(JunctionApp app) => (string?)Field(app, "_filesSelPath");
    private static string? FilesCursor(JunctionApp app) => (string?)Field(app, "_filesCursorPath");

    /// <summary>The Files tree's visible rows, top to bottom.</summary>
    private static List<string> FileRows(JunctionApp app) =>
        ((IEnumerable)Field(app, "_filesFlat")!).Cast<object>()
            .Select(n => (string)n.GetType().GetProperty("Path")!.GetValue(n)!)
            .ToList();

    private static object LoaderValue(JunctionApp app, string loaders, string path)
    {
        var keyed = Field(app, loaders)!;
        var loader = keyed.GetType().GetMethod("For")!.Invoke(keyed, [path])!;
        return loader.GetType().GetProperty("Value")!.GetValue(loader)!;
    }

    private static int Count(object value, string member) =>
        ((ICollection)value.GetType().GetProperty(member)!.GetValue(value)!).Count;

    private static int Commits(JunctionApp app, string path) =>
        Count(LoaderValue(app, "_historyLoaders", path), "Commits");

    private static int Unstaged(JunctionApp app, string path) =>
        Count(LoaderValue(app, "_changesLoaders", path), "Unstaged");

    private static bool Shows(HeadlessSession session, string subject) =>
        LayoutProbe.Descendants<TextElement>(Root(session),
            t => t.Content == subject && t.Layout.AbsoluteBorderBox.Height > 0).Count > 0;

    private static Element? Root(HeadlessSession session) =>
        (Element?)typeof(HultUI.Window).GetField("_root", Priv)!.GetValue(session.Window.Window);

    private static void PumpUntil(HeadlessSession session, Func<bool> condition, string message)
    {
        try
        {
            session.PumpUntil(condition, TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException e)
        {
            throw new Xunit.Sdk.XunitException($"{message}.\n{e.Message}\n"
                + session.Window.DumpAccessibilityTree());
        }
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
