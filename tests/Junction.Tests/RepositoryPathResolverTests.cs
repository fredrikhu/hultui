using Xunit;

namespace Junction.Tests;

/// <summary>
/// Turning whatever was passed on the command line into a repository root. Junction is
/// launched from a shell inside a working tree as often as from a shortcut, so a path
/// well below the root — or a file rather than a directory — has to resolve.
/// </summary>
public class RepositoryPathResolverTests : IDisposable
{
    private readonly string _root;

    public RepositoryPathResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "junction-resolve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "repo", ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "repo", "src", "deep"));
        Directory.CreateDirectory(Path.Combine(_root, "not-a-repo"));
        File.WriteAllText(Path.Combine(_root, "repo", "src", "file.cs"), "// x");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best-effort temp cleanup */ }
    }

    [Fact]
    public void TheRootItselfResolves()
    {
        var repo = Path.Combine(_root, "repo");
        Assert.Equal(repo, RepositoryPathResolver.FindGitRoot(repo));
    }

    [Fact]
    public void ANestedDirectoryWalksUpToTheRoot()
    {
        var repo = Path.Combine(_root, "repo");
        Assert.Equal(repo, RepositoryPathResolver.FindGitRoot(Path.Combine(repo, "src", "deep")));
    }

    [Fact]
    public void AFilePathResolvesToItsRepository()
    {
        // `junction src/file.cs` should open the repo, not fail on "that is not a
        // directory".
        var repo = Path.Combine(_root, "repo");
        Assert.Equal(repo, RepositoryPathResolver.FindGitRoot(Path.Combine(repo, "src", "file.cs")));
    }

    [Fact]
    public void ASiblingOfARepositoryDoesNotResolveToIt()
    {
        // The search walks up, never sideways. (It may still find some enclosing repo
        // above the temp directory on a developer's machine, so the assertion is that
        // it is not *this* one rather than that it is null.)
        var found = RepositoryPathResolver.FindGitRoot(Path.Combine(_root, "not-a-repo"));

        Assert.NotEqual(Path.Combine(_root, "repo"), found);
        Assert.NotEqual(Path.Combine(_root, "not-a-repo"), found);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankInputResolvesToNothing(string path)
    {
        Assert.Null(RepositoryPathResolver.FindGitRoot(path));
    }

    [Fact]
    public void AMalformedPathIsSwallowedRatherThanThrown()
    {
        // Program.cs feeds raw argv straight in; an unparseable path must not take the
        // process down before the window exists.
        Assert.Null(RepositoryPathResolver.FindGitRoot("\0invalid"));
    }
}
