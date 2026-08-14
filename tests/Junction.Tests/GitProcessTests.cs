using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// How git is launched. The repository is passed as <c>-C &lt;path&gt;</c> rather than as the
/// process's working directory, because Windows pins a running process's current directory:
/// while a call was in flight the repository could not be deleted, moved or renamed, and
/// Junction's loaders outlive the views that start them.
/// <para>
/// The tests below cover what that swap could plausibly have broken. It is the kind of change
/// that looks free — <c>-C</c> is defined as changing to the directory first — right up until
/// a relative path argument resolves somewhere else.
/// </para>
/// </summary>
public class GitProcessTests
{
    [Fact]
    public void ARelativePathArgumentResolvesAgainstTheRepository()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        repo.Write("nested/deep.txt", "x\n");

        var add = GitProcess.Run(repo.Path, "add", "nested/deep.txt");

        Assert.True(add.Success, add.Stderr);
        Assert.Contains("nested/deep.txt",
            GitProcess.Run(repo.Path, "diff", "--cached", "--name-only").Stdout);
    }

    [Fact]
    public void CommandsActOnTheRepositoryRatherThanWhereTheProcessRuns()
    {
        using var repo = TempRepo.CreateWithInitialCommit();

        var top = GitProcess.Run(repo.Path, "rev-parse", "--show-toplevel");

        Assert.True(top.Success, top.Stderr);
        // Compared by leaf: the temp folder is reached through a symlink on some systems, so
        // git's spelling of the path and ours need not match character for character.
        Assert.Equal(
            System.IO.Path.GetFileName(repo.Path),
            System.IO.Path.GetFileName(top.Stdout.Trim()));
    }

    /// <summary>The one deliberate behaviour change. A working directory that does not exist
    /// used to throw out of Process.Start; as an argument it is git's problem, and comes back
    /// as the ordinary failure every caller already handles.</summary>
    [Fact]
    public void AMissingRepositoryIsAFailedResultRatherThanAnException()
    {
        var gone = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "junction-absent-" + Guid.NewGuid().ToString("N"));

        var res = GitProcess.Run(gone, "status");

        Assert.False(res.Success);
    }

    /// <summary>Stdin still reaches git — the patch-application path, which builds its
    /// arguments the same way.</summary>
    [Fact]
    public void StdinStillReachesGit()
    {
        using var repo = TempRepo.CreateWithInitialCommit();
        var hash = GitProcess.RunWithStdin(repo.Path, "some content\n", "hash-object", "-w", "--stdin");

        Assert.True(hash.Success, hash.Stderr);
        Assert.Equal("some content\n",
            GitProcess.Run(repo.Path, "cat-file", "-p", hash.Stdout.Trim()).Stdout);
    }
}
