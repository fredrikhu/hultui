using HultUI.Core.Builders;
using System.Reflection;
using HultUI.Core.Events;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The Changes view's composer: what the commit button and Ctrl+Enter actually do.
///
/// <para>Two of these change what an existing gesture means, which is why they are pinned
/// rather than left to the UI. Committing with nothing staged used to refuse; it now takes
/// the whole worktree, and the one case it must still refuse is a half-resolved conflict —
/// staging a conflicted file is how you declare it resolved, and a commit should not declare
/// that on your behalf. Amend rewrites HEAD rather than adding to it, so the test that
/// matters is that the commit <i>count</i> does not move.</para>
/// </summary>
public class ComposerTests
{
    // ── Reflection into the app's own state ──────────────────────────────

    private static void Set(JunctionApp app, string field, object? value) =>
        typeof(JunctionApp).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(app, value);

    private static T Get<T>(JunctionApp app, string field) =>
        (T)typeof(JunctionApp).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(app)!;

    private static void Invoke(JunctionApp app, string name, params object?[] args) =>
        typeof(JunctionApp).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(app, args);

    private static void Compose(JunctionApp app, string summary, string description = "")
    {
        Set(app, "_commitSummary", summary);
        Set(app, "_commitDescription", description);
    }

    private static int CommitCount(TempRepo repo) =>
        int.Parse(repo.Git("rev-list", "--count", "HEAD").Stdout.Trim());

    private static string HeadSubject(TempRepo repo) =>
        repo.Git("log", "-1", "--format=%s").Stdout.Trim();

    private static string HeadBody(TempRepo repo) =>
        repo.Git("log", "-1", "--format=%b").Stdout.Trim();

    // ── Committing everything when nothing is staged ─────────────────────

    [Fact]
    public void CommittingWithNothingStagedTakesTheWholeWorktree()
    {
        Hosted((repo, session, app) =>
        {
            repo.Write("tracked.txt", "edited\n");
            repo.Write("brand-new.txt", "fresh\n");   // untracked: the list shows it, so it goes
            Refresh(session, app);

            Compose(app, "everything at once");
            Invoke(app, "DoCommit");
            Committed(session, app);

            Assert.Equal("everything at once", HeadSubject(repo));
            // Both files landed, and nothing is left behind in the worktree.
            Assert.Equal("", repo.Git("status", "--porcelain").Stdout.Trim());
        });
    }

    [Fact]
    public void CommittingWithNothingStagedAndNothingChangedStillRefuses()
    {
        Hosted((repo, session, app) =>
        {
            int before = CommitCount(repo);

            Compose(app, "nothing to say");
            Invoke(app, "DoCommit");
            session.PumpFrames(3);

            Assert.Equal(before, CommitCount(repo));
            Assert.Equal("Nothing staged to commit", Get<ToastHost>(app, "_toasts").Message);
        });
    }

    /// <summary>Staging a conflicted file is how you tell git it is resolved. "Commit
    /// everything" must not do that for files nobody has looked at.</summary>
    [Fact]
    public void AHalfResolvedConflictIsNotSweptUpByCommittingEverything()
    {
        Hosted((repo, session, app) =>
        {
            repo.Write("file.txt", "base\n");
            repo.Git("add", "-A");
            repo.Git("commit", "-m", "base");
            repo.Git("checkout", "-b", "side");
            repo.Write("file.txt", "side\n");
            repo.Git("commit", "-am", "side edit");
            repo.Git("checkout", "main");
            repo.Write("file.txt", "main\n");
            repo.Git("commit", "-am", "main edit");
            // Not repo.Git: a conflicting merge exits non-zero, which that helper treats
            // as a broken fixture.
            GitProcess.Run(repo.Path, "merge", "side");
            int before = CommitCount(repo);
            Refresh(session, app);

            Compose(app, "sweep it under the rug");
            Invoke(app, "DoCommit");
            session.PumpFrames(3);

            Assert.Equal(before, CommitCount(repo));
            Assert.Equal("Resolve the conflicts first", Get<ToastHost>(app, "_toasts").Message);
        });
    }

    /// <summary>The gesture itself, through the real keyboard: Ctrl+Enter from the summary
    /// field is what most commits actually go through.</summary>
    [Fact]
    public void CtrlEnterCommitsTheWholeWorktreeToo()
    {
        Hosted((repo, session, app) =>
        {
            repo.Write("tracked.txt", "typed then committed\n");
            Refresh(session, app);

            // Typed into the real field, because that is where the shortcut has to work: the
            // handler sits on the composer and catches the key on its way down to the field.
            var summary = Get<HultUI.Core.Reactivity.Ref<HultUI.Core.Elements.TextInputElement>>(
                app, "_summaryRef").Current!;
            session.Window.Click(summary);
            session.Window.TypeText("via the keyboard");
            session.PumpFrames(2);
            Assert.Equal("via the keyboard", Get<string>(app, "_commitSummary"));

            session.Window.PressKey(KeyCode.Enter, KeyModifiers.Ctrl);
            Committed(session, app);

            Assert.Equal("via the keyboard", HeadSubject(repo));
        });
    }


    // ── Amend ────────────────────────────────────────────────────────────

    [Fact]
    public void SwitchingAmendOnFillsTheFieldsFromHead()
    {
        Hosted((repo, session, app) =>
        {
            repo.Git("commit", "--allow-empty", "-m", "the subject\n\nthe body\n");
            Refresh(session, app);

            Invoke(app, "ToggleAmend", true, "the subject\n\nthe body");

            Assert.Equal("the subject", Get<string>(app, "_commitSummary"));
            Assert.Equal("the body", Get<string>(app, "_commitDescription"));
        });
    }

    [Fact]
    public void SwitchingAmendBackOffTakesOutExactlyWhatItPutIn()
    {
        Hosted((_, _, app) =>
        {
            Invoke(app, "ToggleAmend", true, "the subject\n\nthe body");
            Invoke(app, "ToggleAmend", false, "the subject\n\nthe body");

            Assert.Equal("", Get<string>(app, "_commitSummary"));
            Assert.Equal("", Get<string>(app, "_commitDescription"));
        });
    }

    [Fact]
    public void AnEditMadeWhileAmendingSurvivesSwitchingItOff()
    {
        Hosted((_, _, app) =>
        {
            Invoke(app, "ToggleAmend", true, "the subject\n\nthe body");
            Set(app, "_commitSummary", "the subject, reworded");

            Invoke(app, "ToggleAmend", false, "the subject\n\nthe body");

            Assert.Equal("the subject, reworded", Get<string>(app, "_commitSummary"));
        });
    }

    [Fact]
    public void AMessageAlreadyTypedIsNotOverwrittenByHead()
    {
        Hosted((_, _, app) =>
        {
            Compose(app, "what I was about to write");

            Invoke(app, "ToggleAmend", true, "the subject\n\nthe body");

            Assert.Equal("what I was about to write", Get<string>(app, "_commitSummary"));
        });
    }

    [Fact]
    public void AmendingRewritesHeadInsteadOfAddingACommit()
    {
        Hosted((repo, session, app) =>
        {
            int before = CommitCount(repo);
            repo.Write("tracked.txt", "one more line\n");
            repo.Git("add", "-A");
            Refresh(session, app);

            Set(app, "_amendCommit", true);
            Compose(app, "rewritten subject", "rewritten body");
            Invoke(app, "DoCommit");
            session.PumpFrames(3);

            Assert.Equal(before, CommitCount(repo));
            Assert.Equal("rewritten subject", HeadSubject(repo));
            Assert.Equal("rewritten body", HeadBody(repo));
        });
    }

    [Fact]
    public void AmendingWithNothingStagedRewritesTheMessageAlone()
    {
        Hosted((repo, session, app) =>
        {
            int before = CommitCount(repo);
            Refresh(session, app);

            Set(app, "_amendCommit", true);
            Compose(app, "just a better subject");
            Invoke(app, "DoCommit");
            session.PumpFrames(3);

            Assert.Equal(before, CommitCount(repo));
            Assert.Equal("just a better subject", HeadSubject(repo));
        });
    }

    /// <summary>Amend is a decision about one commit. Left on, the very next commit would
    /// quietly rewrite the one just made instead of adding to it.</summary>
    [Fact]
    public void AmendSwitchesItselfOffAfterTheCommit()
    {
        Hosted((repo, session, app) =>
        {
            Refresh(session, app);
            Set(app, "_amendCommit", true);
            Compose(app, "amended once");
            Invoke(app, "DoCommit");
            // Waits on the summary clearing, which the same continuation does one line earlier —
            // so a version that finished the commit and forgot the flag still fails here.
            Committed(session, app);

            Assert.False(Get<bool>(app, "_amendCommit"));
        });
    }

    // ── Harness ──────────────────────────────────────────────────────────

    /// <summary>Reloads the Changes view against the current state of the repository —
    /// <c>DoCommit</c> reads the loaded lists, not git, so a test that edits the worktree
    /// has to let the load land first.</summary>
    private static void Refresh(HeadlessSession session, JunctionApp app)
    {
        Invoke(app, "RefreshAll");
        session.PumpFrames(6);
    }

    /// <summary>
    /// Waits for a commit to finish.
    ///
    /// <para>Since git left the UI thread the command runs on the pool and its result lands on
    /// the main thread once the subprocess returns. Pumping a fixed three frames does not wait
    /// for that — <c>PumpFrame</c> advances virtual time, not the clock the subprocess runs on —
    /// so the three tests that did it failed every time rather than intermittently.
    /// <c>PumpUntil</c> is the session's one deliberate real-time escape, and a git subprocess
    /// posting back through <c>InvokeOnMainThread</c> is exactly the boundary it exists for.</para>
    ///
    /// <para>The signal is the summary field emptying, not the commit appearing in git. Those
    /// are different moments: git has the commit as soon as the pooled command returns, while the
    /// field is cleared by the continuation on the UI thread. Waiting on the earlier one would
    /// let a test assert against state the app had not caught up with yet — which is the same
    /// race in a new place.</para>
    /// </summary>
    private static void Committed(HeadlessSession session, JunctionApp app) =>
        session.PumpUntil(() => Get<string>(app, "_commitSummary").Length == 0);

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
            session.Window.PressKey(KeyCode.Digit4);
            Assert.Equal(JunctionView.Changes, app.View);
            session.PumpFrames(6);
            body(repo, session, app);
        }
        finally { app?.Dispose(); }
    }
}
