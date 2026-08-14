using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// <see cref="GitRepository"/> against a real repository. Junction shells out to the git
/// CLI, so these are the tests that would catch a changed porcelain format, a flag that
/// stopped meaning what it did, or an argument order git tightened — none of which a
/// test over canned output can see. They need git on PATH; that is not a hardship for
/// a git client's test suite.
/// </summary>
public class GitRepositoryTests
{
    [Fact]
    public void AFreshRepositoryReportsItsBranchAndAnEmptyStatus()
    {
        using var t = TempRepo.CreateWithInitialCommit();

        Assert.Equal("main", t.Repo.GetCurrentBranch());
        Assert.Empty(t.Repo.GetStatus());
    }

    [Fact]
    public void StatusDistinguishesUntrackedStagedAndWorkingTreeChanges()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        t.Write("new.txt", "brand new\n");
        t.Write("README.md", "hello\nmore\n");
        t.Write("staged.txt", "staged\n");
        t.Git("add", "staged.txt");

        var status = t.Repo.GetStatus().ToDictionary(c => c.Path, c => c.Kind);

        Assert.Equal(FileChangeKind.Untracked, status["new.txt"]);
        Assert.Equal(FileChangeKind.Modified, status["README.md"]);
        Assert.Equal(FileChangeKind.Added, status["staged.txt"]);
    }

    [Fact]
    public void StagedAndUnstagedChangeListsAreSeparate()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        t.Write("a.txt", "a\n");
        t.Write("b.txt", "b\n");
        t.Git("add", "a.txt");

        Assert.Equal("a.txt", Assert.Single(t.Repo.GetStagedChanges()).Path);
        Assert.Equal("b.txt", Assert.Single(t.Repo.GetUnstagedChanges()).Path);
    }

    [Fact]
    public void StageAndUnstageMoveAFileBetweenTheTwoLists()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        t.Write("a.txt", "a\n");

        Assert.True(t.Repo.Stage("a.txt"));
        Assert.Equal("a.txt", Assert.Single(t.Repo.GetStagedChanges()).Path);

        Assert.True(t.Repo.Unstage("a.txt"));
        Assert.Empty(t.Repo.GetStagedChanges());
        Assert.Equal("a.txt", Assert.Single(t.Repo.GetUnstagedChanges()).Path);
    }

    [Fact]
    public void StageAndUnstageTakeSeveralFilesAtOnce()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        t.Write("a.txt", "a\n");
        t.Write("b.txt", "b\n");
        t.Write("c.txt", "c\n");

        // Two of the three: the batch has to name its files, not sweep the worktree
        // the way `git add -A` would.
        Assert.True(t.Repo.Stage(["a.txt", "b.txt"]));
        Assert.Equal(["a.txt", "b.txt"], t.Repo.GetStagedChanges().Select(c => c.Path).Order());
        Assert.Equal("c.txt", Assert.Single(t.Repo.GetUnstagedChanges()).Path);

        Assert.True(t.Repo.Unstage(["a.txt", "b.txt"]));
        Assert.Empty(t.Repo.GetStagedChanges());
    }

    [Fact]
    public void DiscardingASetRestoresTrackedFilesAndDeletesUntrackedOnes()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        t.Write("tracked.txt", "committed\n");
        t.Git("add", "-A");
        t.Git("commit", "-m", "add tracked");
        t.Write("tracked.txt", "edited\n");
        t.Write("fresh.txt", "never committed\n");

        // The two kinds need different git commands; discarding a mixed set has to do
        // both, which a single `git restore` over every path silently would not.
        Assert.True(t.Repo.DiscardChanges(t.Repo.GetUnstagedChanges()));

        Assert.Equal("committed\n", t.Read("tracked.txt"));
        Assert.False(File.Exists(System.IO.Path.Combine(t.Path, "fresh.txt")));
        Assert.Empty(t.Repo.GetUnstagedChanges());
    }

    [Fact]
    public void ARenameIsReportedAsOneChangeWithBothPaths()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        t.Write("original.txt", string.Join("\n", Enumerable.Range(0, 40).Select(i => $"line {i}")) + "\n");
        t.Git("add", "-A");
        t.Git("commit", "-m", "add file");

        t.Git("mv", "original.txt", "renamed.txt");

        var change = Assert.Single(t.Repo.GetStagedChanges(), c => c.Kind == FileChangeKind.Renamed);
        Assert.Equal("renamed.txt", change.Path);
        Assert.Equal("original.txt", change.OldPath);
    }

    [Fact]
    public void TheLogCarriesSubjectsAndParentLinks()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        t.Write("a.txt", "a\n");
        t.Git("add", "-A");
        t.Git("commit", "-m", "second commit");

        var log = t.Repo.GetLog();

        Assert.Equal(2, log.Count);
        Assert.Equal("second commit", log[0].Subject);
        Assert.Equal("initial", log[1].Subject);
        // Newest first, and the newest commit's parent is the one below it.
        Assert.Equal(log[1].Hash, Assert.Single(log[0].ParentHashes));
        Assert.Empty(log[1].ParentHashes);
    }

    [Fact]
    public void AMergeCommitHasTwoParentsAndTheGraphAgrees()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        t.Git("checkout", "-b", "topic");
        t.Write("topic.txt", "topic\n");
        t.Git("add", "-A");
        t.Git("commit", "-m", "topic work");

        t.Git("checkout", "main");
        t.Write("main.txt", "main\n");
        t.Git("add", "-A");
        t.Git("commit", "-m", "main work");
        t.Git("merge", "--no-ff", "topic", "-m", "merge topic");

        var log = t.Repo.GetLog();
        var merge = log.First(c => c.Subject == "merge topic");
        Assert.Equal(2, merge.ParentHashes.Length);

        // The layout runs on exactly this list, so a merge here must draw as one.
        var rows = GitGraphLayout.Compute(log);
        Assert.True(rows[merge.Hash].IsMerge);
        Assert.True(rows[merge.Hash].LaneCount >= 2);
    }

    [Fact]
    public void TheWorkingDiffLineNumbersMatchTheFile()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        t.Write("a.txt", "one\ntwo\nthree\nfour\nfive\n");
        t.Git("add", "-A");
        t.Git("commit", "-m", "add a");

        t.Write("a.txt", "one\ntwo\nCHANGED\nfour\nfive\n");

        var file = Assert.Single(t.Repo.GetWorkingDiff(staged: false));
        Assert.Equal("a.txt", file.Path);

        var deletion = file.Hunks.SelectMany(h => h.Lines).Single(l => l.Kind == DiffLineKind.Deletion);
        var addition = file.Hunks.SelectMany(h => h.Lines).Single(l => l.Kind == DiffLineKind.Addition);

        Assert.Equal("three", deletion.Content);
        Assert.Equal(3, deletion.OldLineNumber);
        Assert.Equal("CHANGED", addition.Content);
        Assert.Equal(3, addition.NewLineNumber);
    }

    [Fact]
    public void APartialSelectionPatchActuallyApplies()
    {
        // The whole point of GitPatchBuilder: staging one line of a multi-line change.
        // Synthesising a patch git rejects is the failure mode, and only a real
        // `git apply` proves it does not happen.
        using var t = TempRepo.CreateWithInitialCommit();
        t.Write("a.txt", "one\ntwo\nthree\n");
        t.Git("add", "-A");
        t.Git("commit", "-m", "add a");

        t.Write("a.txt", "one\nADDED A\ntwo\nADDED B\nthree\n");

        var file = Assert.Single(t.Repo.GetWorkingDiff(staged: false));
        var additions = file.Hunks.SelectMany(h => h.Lines)
            .Where(l => l.Kind == DiffLineKind.Addition)
            .ToList();
        Assert.Equal(2, additions.Count);

        var patch = GitPatchBuilder.BuildSelectionPatch(
            file, StageOp.Stage, new HashSet<GitDiffLine> { additions[0] });
        Assert.NotNull(patch);

        var applied = t.Repo.ApplyPatch(patch!, cached: true, unidiffZero: false);
        Assert.True(applied.Success, applied.Stderr);

        // The index now holds the first added line and not the second.
        var staged = t.Repo.GetIndexFileText("a.txt");
        Assert.Equal("one\nADDED A\ntwo\nthree\n", staged);
    }

    [Fact]
    public void DiscardingAPartialSelectionRevertsOnlyThatLine()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        t.Write("a.txt", "one\ntwo\nthree\n");
        t.Git("add", "-A");
        t.Git("commit", "-m", "add a");

        t.Write("a.txt", "one\nADDED A\ntwo\nADDED B\nthree\n");

        var file = Assert.Single(t.Repo.GetWorkingDiff(staged: false));
        var additions = file.Hunks.SelectMany(h => h.Lines)
            .Where(l => l.Kind == DiffLineKind.Addition)
            .ToList();

        var patch = GitPatchBuilder.BuildSelectionPatch(
            file, StageOp.Discard, new HashSet<GitDiffLine> { additions[1] })!;
        var applied = t.Repo.ApplyPatch(patch, reverse: true, unidiffZero: false);
        Assert.True(applied.Success, applied.Stderr);

        Assert.Equal("one\nADDED A\ntwo\nthree\n", t.Read("a.txt"));
    }

    [Fact]
    public void TheInitialCommitsDiffIsTheWholeTree()
    {
        // diff-tree emits nothing for a parentless commit without --root, which used to
        // show the first commit in a repository's history as an empty change set.
        using var t = TempRepo.CreateWithInitialCommit();
        var root = t.Repo.GetLog().Single();

        var files = t.Repo.GetCommitFiles(root.Hash);

        Assert.Equal("README.md", Assert.Single(files).Path);
        Assert.NotEmpty(t.Repo.GetCommitDiff(root.Hash));
    }

    [Fact]
    public void BranchesReportTheirUpstreamlessState()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        Assert.True(t.Repo.CreateBranch("feature/x").Success);

        var branches = t.Repo.GetBranches();
        var current = Assert.Single(branches, b => b.IsCurrent);

        Assert.Equal("main", current.Name);
        Assert.Contains(branches, b => b.Name == "feature/x");
        // No remote in this repository, so nothing tracks anything or is ahead of it.
        Assert.All(branches, b =>
        {
            Assert.Null(b.TrackingBranch);
            Assert.Equal((0, 0), (b.AheadCount, b.BehindCount));
        });
    }

    [Fact]
    public void CheckoutMovesHeadAndTheWorkingTree()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        t.Git("checkout", "-b", "topic");
        t.Write("only-on-topic.txt", "x\n");
        t.Git("add", "-A");
        t.Git("commit", "-m", "topic file");

        Assert.True(t.Repo.Checkout("main").Success);

        Assert.Equal("main", t.Repo.GetCurrentBranch());
        Assert.False(File.Exists(Path.Combine(t.Path, "only-on-topic.txt")));
    }

    [Fact]
    public void GetFileTextReadsTheCommittedVersionNotTheWorkingOne()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        t.Write("README.md", "edited in the working tree\n");

        Assert.Equal("hello\n", t.Repo.GetFileText("HEAD", "README.md"));
        Assert.Equal("edited in the working tree\n", t.Repo.GetWorkingFileText("README.md"));
    }

    [Fact]
    public void DiscardChangesRestoresATrackedFile()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        t.Write("README.md", "wrecked\n");

        var change = Assert.Single(t.Repo.GetUnstagedChanges());
        Assert.True(t.Repo.DiscardChanges(change));

        Assert.Equal("hello\n", t.Read("README.md"));
        Assert.Empty(t.Repo.GetStatus());
    }

    [Fact]
    public void CommitWritesTheMessageAndAdvancesHead()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        var before = t.Head;
        t.Write("a.txt", "a\n");
        t.Repo.StageAll();

        Assert.True(t.Repo.Commit("a subject line").Success);

        Assert.NotEqual(before, t.Head);
        Assert.Equal("a subject line", t.Repo.GetLastCommitMessage()?.Trim());
    }
}

/// <summary>
/// A real merge conflict, end to end: git writes the markers, Junction parses them,
/// the user picks a side, and the result has to satisfy git again. Every step here is
/// exercised elsewhere against canned text — this is the one that proves the canned
/// text matches what git actually writes.
/// </summary>
public class ConflictWorkflowTests
{
    private static TempRepo RepoWithConflict()
    {
        var t = TempRepo.CreateEmpty();
        t.Write("shared.txt", "top\nmiddle\nbottom\n");
        t.Git("add", "-A");
        t.Git("commit", "-m", "base");

        t.Git("checkout", "-b", "theirs");
        t.Write("shared.txt", "top\nTHEIR CHANGE\nbottom\n");
        t.Git("add", "-A");
        t.Git("commit", "-m", "their change");

        t.Git("checkout", "main");
        t.Write("shared.txt", "top\nOUR CHANGE\nbottom\n");
        t.Git("add", "-A");
        t.Git("commit", "-m", "our change");

        // Expected to fail — that is the point.
        GitProcess.Run(t.Path, "merge", "theirs");
        return t;
    }

    [Fact]
    public void AConflictedMergeIsDetectedAndTheFileIsListed()
    {
        using var t = RepoWithConflict();

        Assert.True(t.Repo.IsMerging());
        Assert.Equal("theirs", t.Repo.GetMergingBranchName());
        Assert.Equal("shared.txt", Assert.Single(t.Repo.GetConflictedFiles()).Path);
        Assert.Equal(FileChangeKind.Conflicted, t.Repo.GetStatus().Single().Kind);
    }

    [Fact]
    public void GitsOwnMarkersParseIntoOneHunkWithBothSides()
    {
        using var t = RepoWithConflict();

        var doc = ConflictDocument.Parse(t.Read("shared.txt"));
        var hunk = Assert.Single(doc.Hunks);

        Assert.Equal("OUR CHANGE", Assert.Single(hunk.Ours));
        Assert.Equal("THEIR CHANGE", Assert.Single(hunk.Theirs));
        // Context either side of the conflicted region is untouched.
        Assert.Contains(doc.Segments, s => s.Context is not null && s.Context.Contains("top"));
        Assert.Contains(doc.Segments, s => s.Context is not null && s.Context.Contains("bottom"));
    }

    [Fact]
    public void ResolvingAndStagingClearsTheConflict()
    {
        using var t = RepoWithConflict();

        var doc = ConflictDocument.Parse(t.Read("shared.txt"));
        t.Write("shared.txt", doc.Render(new Dictionary<int, ConflictChoice> { [0] = ConflictChoice.Theirs }));

        Assert.Equal("top\nTHEIR CHANGE\nbottom\n", t.Read("shared.txt"));

        t.Repo.Stage("shared.txt");
        Assert.Empty(t.Repo.GetConflictedFiles());

        Assert.True(t.Repo.MergeContinue().Success);
        Assert.False(t.Repo.IsMerging());
        Assert.Equal(2, t.Repo.GetLog()[0].ParentHashes.Length);
    }

    [Fact]
    public void AbortingRestoresTheUnmergedSide()
    {
        using var t = RepoWithConflict();

        Assert.True(t.Repo.MergeAbort().Success);

        Assert.False(t.Repo.IsMerging());
        Assert.Equal("top\nOUR CHANGE\nbottom\n", t.Read("shared.txt"));
    }
}
