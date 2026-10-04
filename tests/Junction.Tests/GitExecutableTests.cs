using Junction.Git;
using Xunit;

namespace Junction.Tests;

/// <summary>
/// Which program a git command starts. Git for Windows puts a launcher first on PATH that
/// starts the real <c>git.exe</c> as a second process, so going around it halves the cost of
/// every call. Only that launcher is replaced, and only when the real program is where the
/// installation puts it — anything else first on PATH is left to the operating system.
/// The installations are described rather than needed, so these run anywhere.
/// </summary>
public class GitExecutableTests
{
    private static readonly string Root = Path.GetPathRoot(Path.GetTempPath())!;
    private static readonly string Git = Path.Combine(Root, "Program Files", "Git");
    private static readonly string Launcher = Path.Combine(Git, "cmd", "git.exe");
    private static readonly string Real = Path.Combine(Git, "mingw64", "bin", "git.exe");

    private static string Resolve(string path, params string[] files) =>
        GitProcess.ResolveExecutable(path, f => files.Contains(f));

    private static string PathOf(params string[] dirs) => string.Join(';', dirs);

    [Fact]
    public void TheInstallersLauncherIsReplacedByTheGitItLaunches()
    {
        var system = Path.Combine(Root, "Windows", "System32");
        Assert.Equal(Real, Resolve(PathOf(system, Path.Combine(Git, "cmd")), Launcher, Real));
    }

    [Fact]
    public void AnArm64InstallationIsFoundToo()
    {
        var arm = Path.Combine(Git, "clangarm64", "bin", "git.exe");
        Assert.Equal(arm, Resolve(PathOf(Path.Combine(Git, "cmd")), Launcher, arm));
    }

    // PATH entries arrive quoted and with trailing separators often enough to matter.
    [Fact]
    public void AQuotedOrSlashTerminatedEntryIsStillRecognized()
    {
        var entry = "\"" + Path.Combine(Git, "cmd") + Path.DirectorySeparatorChar + "\"";
        Assert.Equal(Real, Resolve(entry, Launcher, Real));
    }

    // A launcher whose program is missing is a broken or unusual installation; starting it
    // the old way is the only safe guess.
    [Fact]
    public void ALauncherWithoutItsProgramIsLeftAlone()
    {
        Assert.Equal("git", Resolve(PathOf(Path.Combine(Git, "cmd")), Launcher));
    }

    // Git Bash puts mingw64\bin first; a package manager puts its shim first. Either way the
    // first git.exe on PATH is what Windows would start, and it is not the installer's launcher.
    [Fact]
    public void WhateverElseComesFirstOnPathIsLeftToTheOperatingSystem()
    {
        var shims = Path.Combine(Root, "Users", "someone", "scoop", "shims");
        Assert.Equal("git", Resolve(PathOf(shims, Path.Combine(Git, "cmd")),
            Path.Combine(shims, "git.exe"), Launcher, Real));
        Assert.Equal("git", Resolve(PathOf(Path.Combine(Git, "mingw64", "bin"), Path.Combine(Git, "cmd")),
            Launcher, Real));
    }

    [Fact]
    public void NoGitOnPathKeepsThePlainName()
    {
        Assert.Equal("git", Resolve(PathOf(Path.Combine(Root, "Windows")), Real));
        Assert.Equal("git", GitProcess.ResolveExecutable(null, _ => true));
    }

    // Whatever was chosen on this machine has to be a git that works: every other test in the
    // suite goes through it, but this one says so by name.
    [Fact]
    public void TheChosenGitRuns()
    {
        var version = GitProcess.Run("", "--version");
        Assert.True(version.Success, version.Stderr);
        Assert.StartsWith("git version", version.Stdout);
    }
}
