using Xunit;

namespace Junction.Tests;

/// <summary>
/// The fixtures clean up after themselves.
/// <para>
/// They did not, for about a week: the delete was tried once and its failure swallowed, so
/// every run that lost the race with a lingering file handle left an empty directory in the
/// temp folder — nearly three thousand of them by the time anyone looked. The tests below
/// cover the two halves of the fix, and the one way it could do harm.
/// </para>
/// </summary>
public class TempRepoCleanupTests
{
    [Fact]
    public void DisposingARepositoryTakesItOffDisk()
    {
        string path;
        using (var repo = TempRepo.CreateWithInitialCommit())
        {
            path = repo.Path;
            Assert.True(Directory.Exists(path));
        }

        Assert.False(Directory.Exists(path),
            "the fixture outlived its using block — the leak this guards against");
    }

    /// <summary>Disposal has to survive a repository git has packed: the pack files come back
    /// read-only, which is what the attribute pass exists for.</summary>
    [Fact]
    public void APackedRepositoryIsRemovedDespiteItsReadOnlyPackFiles()
    {
        string path;
        using (var repo = TempRepo.CreateWithInitialCommit())
        {
            path = repo.Path;
            repo.Git("gc", "--aggressive", "--prune=now");
            Assert.Contains(Directory.EnumerateFiles(System.IO.Path.Combine(path, ".git"), "*.pack",
                SearchOption.AllDirectories), f => new FileInfo(f).IsReadOnly);
        }

        Assert.False(Directory.Exists(path));
    }

    [Fact]
    public void AFixtureLeftByAnEarlierRunIsSweptUp()
    {
        var stale = NewFixtureDir();
        File.WriteAllText(System.IO.Path.Combine(stale, "leftover.txt"), "x");
        Age(stale, TimeSpan.FromHours(3));

        TempRepo.SweepStaleFixtures();

        Assert.False(Directory.Exists(stale), "a fixture from an earlier run was left behind");
    }

    /// <summary>
    /// The property that keeps the sweep from being worse than the leak. Test assemblies run
    /// as separate processes and can overlap, so a fixture that is only minutes old may belong
    /// to a run happening right now — deleting the repository out from under it would fail
    /// that run with something that looks nothing like the real cause.
    /// </summary>
    [Fact]
    public void AFixtureFromARunInFlightIsLeftAlone()
    {
        var fresh = NewFixtureDir();
        File.WriteAllText(System.IO.Path.Combine(fresh, "in-use.txt"), "x");

        try
        {
            TempRepo.SweepStaleFixtures();

            Assert.True(Directory.Exists(fresh),
                "the sweep deleted a fixture new enough to belong to a concurrent run");
        }
        finally { Directory.Delete(fresh, recursive: true); }
    }

    // ── harness ──────────────────────────────────────────────────────────

    /// <summary>A directory named the way the fixtures are, so the sweep considers it.</summary>
    private static string NewFixtureDir()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "junction-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Age(string path, TimeSpan by) =>
        Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow - by);
}
