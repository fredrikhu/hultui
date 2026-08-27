using HultUI.Core.Accessibility;
using HultUI.Core.Events;
using HultUI.Headless;
using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The four operations that stop on a conflict and wait — merge, rebase, cherry-pick,
/// revert — and the one resolution UI they all reach.
///
/// <para>Junction used to ask only <c>IsMerging()</c>, so a rebase that stopped on a
/// conflict looked like an ordinary working tree full of modified files: no banner, no
/// way to continue or abort, and the resolution pane never opened. These tests pin the
/// state detection per operation, and then that the Changes view actually offers the
/// resolution UI for a rebase.</para>
///
/// <para>The side identities get their own tests because a rebase <b>inverts</b> them:
/// git replays your commit onto the other branch, so the other branch is "ours" and your
/// own work is "theirs". A UI that labelled that the merge way round would be inviting
/// people to discard exactly the commit they are trying to keep.</para>
/// </summary>
public class SequencerTests
{
    // ── Fixtures ─────────────────────────────────────────────────────────

    /// <summary>Two branches that edit the same line, so any replay of one onto the
    /// other conflicts. Leaves <c>main</c> checked out.</summary>
    private static TempRepo Diverged()
    {
        var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("file.txt", "base\n");
        repo.Git("add", "-A");
        repo.Git("commit", "-m", "base");

        repo.Git("checkout", "-b", "feature");
        repo.Write("file.txt", "feature\n");
        repo.Git("commit", "-am", "feature edit");

        repo.Git("checkout", "main");
        repo.Write("file.txt", "main\n");
        repo.Git("commit", "-am", "main edit");
        return repo;
    }

    // ── State detection ──────────────────────────────────────────────────

    [Fact]
    public void ACleanRepositoryIsInNoOperation()
    {
        using var t = TempRepo.CreateWithInitialCommit();

        Assert.Equal(SequencerOp.None, t.Repo.GetSequencerOp());
        Assert.Null(t.Repo.GetSequencerState());
    }

    [Fact]
    public void AConflictedMergeReportsItselfAsAMerge()
    {
        using var t = Diverged();
        t.Repo.Merge("feature");

        var state = t.Repo.GetSequencerState();
        Assert.NotNull(state);
        Assert.Equal(SequencerOp.Merge, state!.Op);
        Assert.Equal("main", state.OursLabel);
        Assert.Equal("feature", state.TheirsLabel);
        // A merge applies one commit; there is nothing to skip past.
        Assert.False(state.CanSkip);
    }

    [Fact]
    public void AConflictedRebaseReportsItselfAsARebaseAndNotAsAMerge()
    {
        using var t = Diverged();
        t.Git("checkout", "feature");
        t.Repo.Rebase("main");   // conflicts: both edited file.txt

        Assert.Equal(SequencerOp.Rebase, t.Repo.GetSequencerOp());
        // The old detection asked this, and it is false throughout a rebase — which is
        // the whole reason the resolution UI never appeared.
        Assert.False(t.Repo.IsMerging());

        var state = t.Repo.GetSequencerState();
        Assert.NotNull(state);
        Assert.Equal(SequencerOp.Rebase, state!.Op);
        Assert.Equal("feature", state.Branch);
        Assert.True(state.CanSkip);
    }

    [Fact]
    public void RebaseSidesAreTheOppositeWayRoundFromAMerge()
    {
        using var t = Diverged();
        t.Git("checkout", "feature");
        t.Repo.Rebase("main");

        var state = t.Repo.GetSequencerState()!;

        // "ours" is the branch being replayed ONTO — main, the side already in the
        // working tree — and "theirs" is the user's own commit.
        Assert.Contains("main", state.OursLabel, StringComparison.Ordinal);
        Assert.Contains("feature", state.TheirsLabel, StringComparison.Ordinal);

        // And the labels agree with what git will actually hand over: --ours here yields
        // main's line, not feature's.
        t.Repo.CheckoutOurs("file.txt");
        Assert.Equal("main\n", t.Read("file.txt"));
    }

    [Fact]
    public void RebaseProgressCountsTheCommitsBeingReplayed()
    {
        using var t = Diverged();
        t.Git("checkout", "feature");
        // A second, conflict-free commit on top, so the replay is a run rather than one.
        t.Write("other.txt", "second\n");
        t.Git("add", "-A");
        t.Git("commit", "-m", "second feature edit");
        t.Repo.Rebase("main");

        var state = t.Repo.GetSequencerState()!;
        Assert.Equal(2, state.Total);
        Assert.Equal(1, state.Step);
        Assert.Equal("1 of 2", state.Progress);
    }

    [Fact]
    public void AConflictedCherryPickReportsItselfAsACherryPick()
    {
        using var t = Diverged();
        var feature = t.Repo.GetCommit("feature")!;
        t.Repo.CherryPick(feature.Hash);

        var state = t.Repo.GetSequencerState();
        Assert.NotNull(state);
        Assert.Equal(SequencerOp.CherryPick, state!.Op);
        Assert.Equal("main", state.OursLabel);
        Assert.True(state.CanSkip);
    }

    [Fact]
    public void AConflictedRevertReportsItselfAsARevert()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        t.Write("file.txt", "one\n");
        t.Git("add", "-A");
        t.Git("commit", "-m", "add");
        t.Write("file.txt", "two\n");
        t.Git("commit", "-am", "change");
        var target = t.Repo.GetCommit("HEAD")!;
        // Rewriting the same line again makes the reverse patch fail to apply.
        t.Write("file.txt", "three\n");
        t.Git("commit", "-am", "change again");

        t.Repo.Revert(target.Hash);

        var state = t.Repo.GetSequencerState();
        Assert.NotNull(state);
        Assert.Equal(SequencerOp.Revert, state!.Op);
    }

    // ── Continue / abort / skip ──────────────────────────────────────────

    [Fact]
    public void ContinueFinishesARebaseOnceEveryConflictIsStaged()
    {
        using var t = Diverged();
        t.Git("checkout", "feature");
        t.Repo.Rebase("main");
        Assert.Equal(SequencerOp.Rebase, t.Repo.GetSequencerOp());

        t.Write("file.txt", "resolved\n");
        t.Repo.Stage("file.txt");

        // No editor is configured and there is no terminal behind a desktop app, so this
        // is also the test that `git rebase --continue` does not sit waiting on one.
        var result = t.Repo.SequencerContinue(SequencerOp.Rebase);

        Assert.True(result.Success, result.Stderr + result.Stdout);
        Assert.Equal(SequencerOp.None, t.Repo.GetSequencerOp());
        Assert.Equal("resolved\n", t.Read("file.txt"));
    }

    [Fact]
    public void SkipDropsTheConflictingCommitAndEndsTheRebase()
    {
        using var t = Diverged();
        t.Git("checkout", "feature");
        t.Repo.Rebase("main");

        var result = t.Repo.SequencerSkip(SequencerOp.Rebase);

        Assert.True(result.Success, result.Stderr + result.Stdout);
        Assert.Equal(SequencerOp.None, t.Repo.GetSequencerOp());
        // feature's edit was the only commit being replayed; skipping it leaves main's.
        Assert.Equal("main\n", t.Read("file.txt"));
    }

    [Fact]
    public void AbortPutsARebaseBackWhereItStarted()
    {
        using var t = Diverged();
        t.Git("checkout", "feature");
        t.Repo.Rebase("main");

        var result = t.Repo.SequencerAbort(SequencerOp.Rebase);

        Assert.True(result.Success, result.Stderr + result.Stdout);
        Assert.Equal(SequencerOp.None, t.Repo.GetSequencerOp());
        Assert.Equal("feature", t.Repo.GetCurrentBranch());
        Assert.Equal("feature\n", t.Read("file.txt"));
    }

    [Fact]
    public void SkipIsRefusedForAMergeWhichHasNothingToSkip()
    {
        using var t = Diverged();
        t.Repo.Merge("feature");

        var result = t.Repo.SequencerSkip(SequencerOp.Merge);

        Assert.False(result.Success);
        // Refused without running anything: the merge is untouched.
        Assert.Equal(SequencerOp.Merge, t.Repo.GetSequencerOp());
    }

    // ── The Changes view ─────────────────────────────────────────────────

    /// <summary>
    /// The bug this whole change exists for: a rebase stopped on a conflict has to show
    /// the banner and the resolution pane, not a list of ordinary unstaged edits.
    /// </summary>
    [Fact]
    public void ARebaseConflictShowsTheBannerAndTheResolutionPane()
    {
        Hosted(
            repo =>
            {
                repo.Write("file.txt", "base\n");
                repo.Git("add", "-A");
                repo.Git("commit", "-m", "base");
                repo.Git("checkout", "-b", "feature");
                repo.Write("file.txt", "feature\n");
                repo.Git("commit", "-am", "feature edit");
                repo.Git("checkout", "main");
                repo.Write("file.txt", "main\n");
                repo.Git("commit", "-am", "main edit");
                repo.Git("checkout", "feature");
                repo.Repo.Rebase("main");
                Assert.Equal(SequencerOp.Rebase, repo.Repo.GetSequencerOp());
            },
            (repo, session, app) =>
            {
                session.Window.PressKey(KeyCode.Digit4);   // Changes
                PumpUntilVisible(session, "Rebase");

                // The banner names the operation and counts what is left, and the pane
                // opens on the conflicted file with both sides identified.
                Assert.True(Contains(session, "conflict"),
                    "the Changes view showed no conflict state for a stopped rebase.\n"
                    + session.Window.DumpAccessibilityTree());
                Assert.True(Contains(session, "Continue"),
                    "there was no way to continue the rebase.\n"
                    + session.Window.DumpAccessibilityTree());
                Assert.True(Contains(session, "Skip"),
                    "a rebase can skip the conflicting commit, but nothing offered it.\n"
                    + session.Window.DumpAccessibilityTree());
                // The side labels, the right way round for a rebase.
                Assert.True(Contains(session, "YOUR COMMIT"),
                    "the incoming side was not identified as the user's own commit.\n"
                    + session.Window.DumpAccessibilityTree());
                Assert.True(Contains(session, "NEW BASE"),
                    "the checked-out side was not identified as the rebase base.\n"
                    + session.Window.DumpAccessibilityTree());
            });
    }

    // ── Harness ──────────────────────────────────────────────────────────

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

    private static void PumpUntilVisible(HeadlessSession session, string text)
    {
        try
        {
            session.PumpUntil(() => Contains(session, text), TimeSpan.FromSeconds(20));
        }
        catch (TimeoutException)
        {
            throw new Xunit.Sdk.XunitException(
                $"\"{text}\" never appeared.\n{session.Window.DumpAccessibilityTree()}");
        }
    }

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
}
