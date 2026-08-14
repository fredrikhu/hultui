using Xunit;

namespace Junction.Tests;

/// <summary>
/// The worktree layer, against real repositories. It shipped without tests, which is how
/// the two fragile parts of <c>GetWorktrees</c> could regress unnoticed: the Windows path
/// normalization (git prints forward slashes, the rest of the app stores OS-native paths,
/// and a mismatch makes the worktree you are standing in look like someone else's), and
/// the final flush for the last record, which has no trailing blank line to close it.
/// </summary>
public class WorktreeTests
{
    /// <summary>A sibling directory of the fixture, so the sweeper that cleans up
    /// <c>junction-git-*</c> reaches these too if a test dies mid-way.</summary>
    private static string LinkedPathFor(TempRepo repo, string suffix) =>
        Path.Combine(Path.GetDirectoryName(repo.Path)!, Path.GetFileName(repo.Path) + suffix);

    [Fact]
    public void AFreshRepositoryHasExactlyOneWorktreeAndItIsTheMainOne()
    {
        using var repo = TempRepo.CreateWithInitialCommit();

        var wt = Assert.Single(repo.Repo.GetWorktrees());

        Assert.True(wt.IsMain);
        Assert.Equal("main", wt.Branch);
        Assert.Equal("main", wt.RefLabel);
        Assert.False(wt.IsLocked);
        Assert.False(wt.Prunable);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(repo.Path)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(wt.Path)));
        // The normalization itself, which only bites where the two separators differ.
        if (Path.DirectorySeparatorChar != '/')
            Assert.DoesNotContain('/', wt.Path);
    }

    [Fact]
    public void AddingAWorktreeOnANewBranchListsBothAndOnlyTheFirstIsMain()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var linked = LinkedPathFor(repo, "-wt");

        try
        {
            var added = repo.Repo.AddWorktree(linked, newBranch: "feature/side");
            Assert.True(added.Success, added.Stderr);

            var all = repo.Repo.GetWorktrees();

            Assert.Equal(2, all.Count);
            Assert.True(all[0].IsMain);
            // The trailing record has no blank line after it; without the final flush
            // this is the entry that would silently go missing.
            Assert.False(all[1].IsMain);
            Assert.Equal("feature/side", all[1].Branch);
            Assert.Equal("feature/side", all[1].RefLabel);
            Assert.False(all[1].IsDetached);
        }
        finally
        {
            repo.Repo.RemoveWorktree(linked, force: true);
            if (Directory.Exists(linked)) Directory.Delete(linked, recursive: true);
        }
    }

    [Fact]
    public void AWorktreeCheckedOutAtACommitIsDetached()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var linked = LinkedPathFor(repo, "-det");

        try
        {
            Assert.True(repo.Repo.AddWorktree(linked, committish: repo.Head).Success);

            var detached = repo.Repo.GetWorktrees().Single(w => !w.IsMain);

            Assert.True(detached.IsDetached);
            Assert.Null(detached.Branch);
        }
        finally
        {
            repo.Repo.RemoveWorktree(linked, force: true);
            if (Directory.Exists(linked)) Directory.Delete(linked, recursive: true);
        }
    }

    [Fact]
    public void RemovingAWorktreeTakesItOffTheListAndOffDisk()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var linked = LinkedPathFor(repo, "-gone");

        Assert.True(repo.Repo.AddWorktree(linked, newBranch: "scratch").Success);
        Assert.Equal(2, repo.Repo.GetWorktrees().Count);

        var removed = repo.Repo.RemoveWorktree(linked);

        Assert.True(removed.Success, removed.Stderr);
        Assert.Single(repo.Repo.GetWorktrees());
        Assert.False(Directory.Exists(linked));
    }

    [Fact]
    public void AWorktreeWhoseDirectoryVanishedIsPrunableAndPruningClearsIt()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var linked = LinkedPathFor(repo, "-stale");

        Assert.True(repo.Repo.AddWorktree(linked, newBranch: "stale").Success);
        // Pulled out from under git — exactly what the "prunable" badge is there to report.
        Directory.Delete(linked, recursive: true);

        Assert.True(repo.Repo.GetWorktrees().Single(w => !w.IsMain).Prunable);

        Assert.True(repo.Repo.PruneWorktrees().Success);
        Assert.Single(repo.Repo.GetWorktrees());
    }
}
