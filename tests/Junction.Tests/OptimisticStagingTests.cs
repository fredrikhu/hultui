using System.Reflection;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Staging shows before git is asked, and is corrected if git disagrees.
///
/// <para>The list used to wait for <c>git add</c> and the status read behind it, which on a
/// large repository is long enough that a click looked like it had done nothing. The row now
/// moves first and the command runs behind it — so what these check is that the move is
/// visible <i>without pumping a single frame</i>, and that a command which fails puts the row
/// back where git actually has it.</para>
/// </summary>
[Collection("JunctionDiff")]
public class OptimisticStagingTests
{
    [Fact]
    public void TheRowMovesToStagedBeforeGitHasAnswered()
    {
        Hosted((session, app, repo) =>
        {
            Assert.Contains("work.txt", Unstaged(app));

            Stage(app, "work.txt");

            // No pump: the overlay is what the next build would draw, and the git call is
            // still in flight on another thread.
            Assert.Contains("work.txt", Staged(app));
            Assert.DoesNotContain("work.txt", Unstaged(app));
        });
    }

    [Fact]
    public void GitCatchesUpAndTheRowStaysWhereItWasPut()
    {
        Hosted((session, app, repo) =>
        {
            Stage(app, "work.txt");

            // The command lands, the status is re-read, and the row is still staged — this
            // is the case where the overlay has to be retired without the row flicking back.
            session.PumpUntil(() => IsStagedInGit(repo, "work.txt"), TimeSpan.FromSeconds(30));
            session.PumpFrames(6);

            Assert.Contains("work.txt", Staged(app));
            Assert.DoesNotContain("work.txt", Unstaged(app));
            Assert.Empty(Pending(app));
        });
    }

    [Fact]
    public void UnstagingMovesTheRowBackTheSameWay()
    {
        Hosted((session, app, repo) =>
        {
            repo.Git("add", "work.txt");
            Refresh(app);
            session.PumpUntil(() => Staged(app).Contains("work.txt"), TimeSpan.FromSeconds(30));

            Unstage(app, "work.txt");

            Assert.Contains("work.txt", Unstaged(app));
            Assert.DoesNotContain("work.txt", Staged(app));
        });
    }

    /// <summary>A command that cannot succeed puts the row back. The file is gone from disk
    /// and out of the index, so there is nothing for git to stage — and the optimistic move
    /// must not survive that.</summary>
    [Fact]
    public void AFailedStageSpringsTheRowBack()
    {
        Hosted((session, app, repo) =>
        {
            // Nothing by this name is in the working copy, so `git add` answers with an error.
            Stage(app, "no-such-file.txt");
            Assert.Single(Pending(app));

            session.PumpUntil(() => Pending(app).Count == 0, TimeSpan.FromSeconds(30));
            Assert.DoesNotContain("no-such-file.txt", Staged(app));
        });
    }

    /// <summary>A stage that comes back after the same file was unstaged does not take the row
    /// back. Every successful command settled each file still pending, whichever move had put
    /// it there, so the reload behind the stage retired the unstage too: the row sprang back to
    /// Staged while <c>git reset</c> was still waiting its turn. On a busy machine the same race
    /// let a test unstage files that a late reload then showed staged again. The two commands
    /// are held on gates here, so the window stays open for as long as the test needs.</summary>
    [Fact]
    public void AStageThatComesBackAfterAnUnstage_DoesNotTakeTheRowBack()
    {
        Hosted((session, app, repo) =>
        {
            using var stageGate = new ManualResetEventSlim();
            using var unstageGate = new ManualResetEventSlim();
            try
            {
                RunStaging(app, "work.txt", toStaged: true, () =>
                {
                    stageGate.Wait();
                    return GitProcess.Run(repo.Path, "add", "--", "work.txt").Success;
                });
                RunStaging(app, "work.txt", toStaged: false, () =>
                {
                    unstageGate.Wait();
                    return GitProcess.Run(repo.Path, "reset", "-q", "--", "work.txt").Success;
                });
                Assert.Contains("work.txt", Unstaged(app));

                // The stage comes back and the reload behind it reads the file staged in git;
                // the unstage is still waiting on its gate.
                stageGate.Set();
                session.PumpUntil(() => Section(Raw(app), "Staged").Contains("work.txt"), TimeSpan.FromSeconds(30));
                session.PumpFrames(4);
                Assert.Contains("work.txt", Unstaged(app));
                Assert.DoesNotContain("work.txt", Staged(app));
                Assert.Single(Pending(app));

                // The unstage comes back: git agrees, and the reload retires the move.
                unstageGate.Set();
                session.PumpUntil(() => Pending(app).Count == 0, TimeSpan.FromSeconds(30));
                Assert.False(IsStagedInGit(repo, "work.txt"));
                Assert.Contains("work.txt", Unstaged(app));
            }
            finally
            {
                stageGate.Set();
                unstageGate.Set();
            }
        });
    }

    // ── Reaching the app's state ─────────────────────────────────────────

    /// <summary>A staging move whose git command is <paramref name="command"/>, the way the app
    /// asks for one (optimistic overlay first, the command on the staging chain).</summary>
    private static void RunStaging(JunctionApp app, string path, bool toStaged, Func<bool> command) =>
        typeof(JunctionApp).GetMethod("RunStaging", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, [(IReadOnlyList<string>)[path], toStaged, command, null, "failed"]);

    /// <summary>What git last said, without the moves still in flight.</summary>
    private static object Raw(JunctionApp app)
    {
        var loader = typeof(JunctionApp).GetProperty("_changes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;
        return loader.GetType().GetProperty("Value")!.GetValue(loader)!;
    }

    private static void Stage(JunctionApp app, params string[] paths) =>
        typeof(JunctionApp).GetMethod("StagePaths", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, [(IReadOnlyList<string>)paths]);

    private static void Unstage(JunctionApp app, params string[] paths) =>
        typeof(JunctionApp).GetMethod("UnstagePaths", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, [(IReadOnlyList<string>)paths]);

    private static void Refresh(JunctionApp app) =>
        typeof(JunctionApp).GetMethod("RefreshAll", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, null);

    /// <summary>The Changes view as it would be drawn right now — git's answer with every
    /// asked-for move already applied.</summary>
    private static object View(JunctionApp app) =>
        typeof(JunctionApp).GetProperty("ChangesView", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;

    private static List<string> Section(JunctionApp app, string name) => Section(View(app), name);

    private static List<string> Section(object data, string name)
    {
        var files = (System.Collections.IEnumerable)data.GetType()
            .GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(data)!;
        var paths = new List<string>();
        foreach (var f in files)
            paths.Add((string)f.GetType().GetProperty("Path")!.GetValue(f)!);
        return paths;
    }

    private static List<string> Staged(JunctionApp app) => Section(app, "Staged");
    private static List<string> Unstaged(JunctionApp app) => Section(app, "Unstaged");

    private static System.Collections.ICollection Pending(JunctionApp app) =>
        (System.Collections.ICollection)typeof(JunctionApp)
            .GetField("_pendingStage", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(app)!;

    private static bool IsStagedInGit(TempRepo repo, string path) =>
        GitProcess.Run(repo.Path, "diff", "--cached", "--name-only").Stdout
            .Contains(path, StringComparison.Ordinal);

    /// <summary>The Changes view on a repository with one modified file waiting.</summary>
    private static void Hosted(Action<HeadlessSession, JunctionApp, TempRepo> body)
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("work.txt", "one\n");

        using var session = HeadlessSession.Create(1400, 900);
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
            session.PumpFrames(3);

            session.Window.PressKey(HultUI.Core.Events.KeyCode.Digit4);   // Changes
            session.PumpUntil(() => app!.View == JunctionView.Changes, TimeSpan.FromSeconds(20));
            // The status read is off-thread; wait for the row rather than a frame count.
            session.PumpUntil(() => Unstaged(app!).Contains("work.txt"), TimeSpan.FromSeconds(30));

            body(session, app, repo);
        }
        finally { app?.Dispose(); }
    }
}
