using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// The interactive-rebase todo Junction writes, against a real repository.
///
/// <para>The case that matters here is the empty plan. Dropped commits are simply left out
/// of the todo, so dropping every commit leaves nothing in it — and git answers an empty
/// todo with <c>error: nothing to do</c> instead of doing the obvious thing. The plan has
/// to say <c>noop</c>, which is git's own way of writing "replay nothing".</para>
/// </summary>
public class InteractiveRebaseTests
{
    /// <summary>Three commits on top of the initial one, oldest first.</summary>
    private static List<GitCommit> ThreeCommits(TempRepo t)
    {
        foreach (var name in new[] { "a", "b", "c" })
        {
            t.Write($"{name}.txt", $"{name}\n");
            t.Git("add", "-A");
            t.Git("commit", "-m", name);
        }
        // GetLog is newest-first; a todo runs oldest-first.
        var log = t.Repo.GetLog(10).Take(3).Reverse().ToList();
        Assert.Equal(["a", "b", "c"], log.Select(c => c.Subject));
        return log;
    }

    private static RebaseEntry Entry(GitCommit c, RebaseOp op) => new()
    {
        Hash = c.Hash,
        ShortHash = c.ShortHash,
        Subject = c.Subject,
        Op = op,
    };

    [Fact]
    public void DroppingOneCommitLeavesTheOthersInOrder()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        var log = ThreeCommits(t);

        var result = t.Repo.InteractiveRebase("HEAD~3",
            [Entry(log[0], RebaseOp.Pick), Entry(log[1], RebaseOp.Drop), Entry(log[2], RebaseOp.Pick)]);

        Assert.True(result.Success, result.Stderr);
        Assert.Equal(["c", "a"], t.Repo.GetLog(2).Select(c => c.Subject));
    }

    /// <summary>The regression: an all-dropped plan used to write an empty todo and come
    /// back with "nothing to do" — the branch untouched and an error where a rewind belonged.</summary>
    [Fact]
    public void DroppingEveryCommitRewindsToTheBaseInsteadOfFailing()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        var log = ThreeCommits(t);
        var baseHash = t.Repo.GetLog(10)[3].Hash;   // the initial commit, under all three

        var result = t.Repo.InteractiveRebase("HEAD~3", [.. log.Select(c => Entry(c, RebaseOp.Drop))]);

        Assert.True(result.Success, result.Stderr);
        Assert.DoesNotContain("nothing to do", result.Stderr, StringComparison.OrdinalIgnoreCase);
        var head = t.Repo.GetLog(5);
        Assert.Equal("initial", head[0].Subject);
        Assert.Equal(baseHash, head[0].Hash);
    }

    [Fact]
    public void RewordRewritesJustThatCommitsMessage()
    {
        using var t = TempRepo.CreateWithInitialCommit();
        var log = ThreeCommits(t);

        var plan = new List<RebaseEntry>
        {
            Entry(log[0], RebaseOp.Pick),
            Entry(log[1], RebaseOp.Pick),
            Entry(log[2], RebaseOp.Reword),
        };
        plan[2].NewMessage = "c, renamed";

        var result = t.Repo.InteractiveRebase("HEAD~3", plan);

        Assert.True(result.Success, result.Stderr);
        Assert.Equal(["c, renamed", "b", "a"], t.Repo.GetLog(3).Select(c => c.Subject));
    }
}
