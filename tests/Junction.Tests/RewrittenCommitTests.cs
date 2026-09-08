using System.Collections;
using System.Reflection;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Headless;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The History selection follows a commit through a rewrite. A rebase, amend or
/// cherry-pick gives the commit a new hash but keeps its author and author timestamp, so
/// the selection is moved to the commit that just appeared with the same author, second
/// and subject — or, when two appeared in the same second, the one with the same patch id.
/// A commit the rewrite dropped has no copy, and the selection falls back to the top.
///
/// <para>The rewrites are done by git outside the app, then reported the way the watcher
/// would (see <see cref="ExternalChangeTests"/>).</para>
/// </summary>
public class RewrittenCommitTests
{
    private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void ARebaseMovesTheSelectionToTheCommitsNewHash()
    {
        Hosted(repo =>
        {
            Commit(repo, "a", "2026-01-01T00:00:01+00:00");
            Commit(repo, "b", "2026-01-01T00:00:02+00:00");
            Commit(repo, "c", "2026-01-01T00:00:03+00:00");
        }, (repo, session, app) =>
        {
            OpenHistory(session, app, repo, commits: 4);
            SelectSubject(session, app, repo, "b");
            var old = SelectedCommit(app)!;

            RebaseOntoANewBase(repo, "main~3");
            var rewritten = repo.Git("rev-parse", "main~1").Stdout.Trim();
            Assert.NotEqual(old, rewritten);

            ExternalChange(app, repo);
            PumpUntil(session, () => SelectedCommit(app) != old, "the selection never left the old hash");
            Assert.Equal(rewritten, SelectedCommit(app));
            Assert.Equal(["b"], Subjects(app, SelectedHashes(app), repo));
        });
    }

    [Fact]
    public void AnAmendKeepsTheSelectionOnTheRewordedCommit()
    {
        Hosted(repo =>
        {
            Commit(repo, "tip", "2026-01-01T00:00:01+00:00");
            // A second branch whose commit sorts above the amended one (the log is in
            // committer-date order), so that falling back to the top of the list would land
            // somewhere else — the amended commit has to be found, not stumbled on.
            repo.Git("branch", "other", "main~1");
            repo.Git("checkout", "-q", "other");
            Commit(repo, "other", "2026-01-01T00:00:01+00:00");
            repo.Git("checkout", "-q", "main");
        }, (repo, session, app) =>
        {
            OpenHistory(session, app, repo, commits: 3);
            SelectSubject(session, app, repo, "tip");
            var old = SelectedCommit(app)!;

            // A reword: author and second survive, the subject does not. Committed "in the
            // past" so it sorts below the other branch's commit.
            var amend = Junction.Git.GitProcess.RunWithEnv(repo.Path,
                new() { ["GIT_COMMITTER_DATE"] = "2020-01-01T00:00:00+00:00" },
                "commit", "--amend", "-m", "tip, reworded");
            Assert.True(amend.Success, amend.Stderr);
            ExternalChange(app, repo);

            PumpUntil(session, () => SelectedCommit(app) != old, "the selection never left the old hash");
            Assert.Equal(repo.Head, SelectedCommit(app));
            Assert.Equal("other", Subject(repo, TopCommit(app, repo.Path)));
        });
    }

    [Fact]
    public void TwoCommitsInTheSameSecondAreToldApartByTheirContent()
    {
        Hosted(repo =>
        {
            // Same author, same second, same subject: only the change differs.
            Commit(repo, "tick", "2026-01-01T00:00:01+00:00", file: "first.txt");
            Commit(repo, "tick", "2026-01-01T00:00:01+00:00", file: "second.txt");
        }, (repo, session, app) =>
        {
            OpenHistory(session, app, repo, commits: 3);
            session.Window.PressKey(KeyCode.Down);   // the older of the two
            session.PumpFrame();
            var old = SelectedCommit(app)!;
            Assert.Equal(["first.txt"], repo.Repo.GetCommitFiles(old).Select(f => f.Path));

            RebaseOntoANewBase(repo, "main~2");
            ExternalChange(app, repo);

            PumpUntil(session, () => SelectedCommit(app) != old, "the selection never left the old hash");
            Assert.Equal(["first.txt"], repo.Repo.GetCommitFiles(SelectedCommit(app)!).Select(f => f.Path));
        });
    }

    [Fact]
    public void ADroppedCommitSendsTheSelectionToTheTop()
    {
        Hosted(repo =>
        {
            Commit(repo, "a", "2026-01-01T00:00:01+00:00");
            Commit(repo, "b", "2026-01-01T00:00:02+00:00");
            Commit(repo, "c", "2026-01-01T00:00:03+00:00");
        }, (repo, session, app) =>
        {
            OpenHistory(session, app, repo, commits: 4);
            SelectSubject(session, app, repo, "b");
            var old = SelectedCommit(app)!;

            // Drop b: c is rebased onto a, so c is rewritten and b has no copy.
            repo.Git("rebase", "--onto", "main~2", "main~1", "main");
            ExternalChange(app, repo);

            PumpUntil(session, () => SelectedCommit(app) != old, "the selection never left the old hash");
            Assert.Equal(repo.Head, SelectedCommit(app));
        });
    }

    [Fact]
    public void AMultiSelectionFollowsEveryMemberItCan()
    {
        Hosted(repo =>
        {
            Commit(repo, "a", "2026-01-01T00:00:01+00:00");
            Commit(repo, "b", "2026-01-01T00:00:02+00:00");
            Commit(repo, "c", "2026-01-01T00:00:03+00:00");
        }, (repo, session, app) =>
        {
            OpenHistory(session, app, repo, commits: 4);
            // c is selected on load; Shift+Down twice takes the range down to a.
            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.Window.PressKey(KeyCode.Down, KeyModifiers.Shift);
            session.PumpFrame();
            Assert.Equal(["a", "b", "c"], Subjects(app, SelectedHashes(app), repo));
            var old = SelectedCommit(app)!;

            RebaseOntoANewBase(repo, "main~3");
            ExternalChange(app, repo);

            PumpUntil(session, () => SelectedCommit(app) != old, "the selection never left the old hash");
            Assert.Equal(["a", "b", "c"], Subjects(app, SelectedHashes(app), repo));
            Assert.Equal(repo.Git("rev-parse", "main~2").Stdout.Trim(), SelectedCommit(app));   // the lead was a
            Assert.Equal(repo.Head, Anchor(app));                                                // the anchor c
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private static void Commit(TempRepo repo, string subject, string date, string? file = null)
    {
        repo.Write(file ?? $"{subject}.txt", $"{subject} {Guid.NewGuid():N}\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", subject, "--date", date);
    }

    /// <summary>Rewrites everything above <paramref name="oldBase"/> onto a fresh commit
    /// forked from it, so every commit above gets a new hash and nothing else changes.</summary>
    private static void RebaseOntoANewBase(TempRepo repo, string oldBase)
    {
        repo.Git("branch", "base2", oldBase);
        repo.Git("checkout", "-q", "base2");
        repo.Write("base.txt", "base\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "base");
        repo.Git("checkout", "-q", "main");
        repo.Git("rebase", "--onto", "base2", oldBase, "main");
    }

    private static void OpenHistory(HeadlessSession session, JunctionApp app, TempRepo repo, int commits)
    {
        session.Window.PressKey(KeyCode.Digit2);
        Assert.Equal(JunctionView.History, app.View);
        PumpUntil(session, () => Commits(app, repo.Path) == commits && SelectedCommit(app) != null,
            "the History view never loaded the log");
    }

    /// <summary>Arrows down from the top until the commit with <paramref name="subject"/> is
    /// the selection.</summary>
    private static void SelectSubject(HeadlessSession session, JunctionApp app, TempRepo repo, string subject)
    {
        for (var i = 0; i < 10; i++)
        {
            if (Subject(repo, SelectedCommit(app)!) == subject) return;
            session.Window.PressKey(KeyCode.Down);
            session.PumpFrame();
        }
        throw new Xunit.Sdk.XunitException($"no commit \"{subject}\" within reach");
    }

    private static string Subject(TempRepo repo, string hash) =>
        repo.Git("log", "-1", "--format=%s", hash).Stdout.Trim();

    private static List<string> Subjects(JunctionApp app, IEnumerable<string> hashes, TempRepo repo) =>
        hashes.Select(h => Subject(repo, h)).OrderBy(s => s, StringComparer.Ordinal).ToList();

    private static void ExternalChange(JunctionApp app, TempRepo repo) =>
        typeof(JunctionApp).GetMethod("OnExternalChange", Priv)!.Invoke(app, [repo.Path]);

    private static object? Field(JunctionApp app, string name) =>
        typeof(JunctionApp).GetField(name, Priv)!.GetValue(app);

    private static string? SelectedCommit(JunctionApp app) => (string?)Field(app, "_selectedCommitHash");
    private static string? Anchor(JunctionApp app) => (string?)Field(app, "_selectionAnchorHash");

    private static List<string> SelectedHashes(JunctionApp app) =>
        ((IEnumerable)Field(app, "_selectedCommitHashes")!).Cast<string>().ToList();

    private static IList LoadedCommits(JunctionApp app, string path)
    {
        var keyed = Field(app, "_historyLoaders")!;
        var loader = keyed.GetType().GetMethod("For")!.Invoke(keyed, [path])!;
        var value = loader.GetType().GetProperty("Value")!.GetValue(loader)!;
        return (IList)value.GetType().GetProperty("Commits")!.GetValue(value)!;
    }

    private static int Commits(JunctionApp app, string path) => LoadedCommits(app, path).Count;

    private static string TopCommit(JunctionApp app, string path) =>
        ((Junction.Git.GitCommit)LoadedCommits(app, path)[0]!).Hash;

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
