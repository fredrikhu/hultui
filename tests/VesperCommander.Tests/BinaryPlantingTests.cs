using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// A binary planted where the OS would find it before the real one.
///
/// <para><c>CreateProcess</c> searches the calling process's current directory ahead of
/// <c>PATH</c>, and Vesper's current directory is whatever folder it was launched from — so a
/// bare program name let a file dropped in that folder decide what ran. The 7-Zip probe runs
/// unprompted from the app's constructor, which made it a startup problem. <see cref="ProgramPath"/>
/// does the lookup itself; these keep it doing so.</para>
/// </summary>
public class BinaryPlantingTests
{
    private static string Scratch([System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var dir = Path.Combine(Path.GetTempPath(), "vesper-hostile", name);
        try { Directory.Delete(dir, true); } catch { }
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Runs <paramref name="body"/> with the process sitting in <paramref name="dir"/>.
    /// The suite disables parallelization, so moving the process is safe here.</summary>
    private static void InDirectory(string dir, Action body)
    {
        var previous = Directory.GetCurrentDirectory();
        try { Directory.SetCurrentDirectory(dir); body(); }
        finally { Directory.SetCurrentDirectory(previous); }
    }

    /// <summary>A real executable to plant. Copied rather than fabricated so that anything
    /// which does launch it actually runs — a zero-byte file would fail for the wrong reason
    /// and the test would pass without proving anything.</summary>
    private static string PlantExecutable(string dir, string name)
    {
        var source = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.SystemDirectory, "hostname.exe")
            : "/bin/echo";
        Assert.True(File.Exists(source), $"the probe needs {source}");
        var planted = Path.Combine(dir, name);
        File.Copy(source, planted, overwrite: true);
        return planted;
    }
    /// <summary>
    /// The whole point of <see cref="ProgramPath"/>. <c>CreateProcess</c> searches the calling
    /// process's current directory before PATH, and Vesper's current directory is whatever
    /// folder it was launched from — so handing <c>Process.Start</c> a bare name lets a file
    /// in that folder decide what runs.
    /// </summary>
    [Fact]
    public void ABareNameIsNeverResolvedFromTheCurrentDirectory()
    {
        var dir = Scratch();
        var planted = PlantExecutable(dir, OperatingSystem.IsWindows() ? "zzvesperplant.exe" : "zzvesperplant");

        InDirectory(dir, () =>
        {
            Assert.Null(ProgramPath.Resolve("zzvesperplant"));
            Assert.Null(ProgramPath.Resolve("zzvesperplant.exe"));
            // Nor by pointing at it the long way round.
            Assert.Null(ProgramPath.Resolve("./zzvesperplant.exe"));
        });
        Assert.True(File.Exists(planted), "the plant should still be there — this test never runs it");
    }

    /// <summary>A relative PATH entry resolves against the current directory too, so it is
    /// dropped for the same reason the current directory itself is.</summary>
    [Fact]
    public void RelativePathEntriesAreNotSearched()
    {
        var dir = Scratch();
        PlantExecutable(dir, OperatingSystem.IsWindows() ? "zzvesperplant.exe" : "zzvesperplant");
        var realPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "." + Path.PathSeparator + "some-relative-dir");
            InDirectory(dir, () => Assert.Null(ProgramPath.Resolve("zzvesperplant")));
        }
        finally { Environment.SetEnvironmentVariable("PATH", realPath); }
    }

    /// <summary>The lookup still has to work, or the fix would just be "never launch
    /// anything" — and on Windows it has to find a name whose extension came from PATHEXT.</summary>
    [Fact]
    public void AProgramOnPathResolvesToAFullPath()
    {
        string bare = OperatingSystem.IsWindows() ? "hostname" : "sh";
        var resolved = ProgramPath.Resolve(bare);
        Assert.NotNull(resolved);
        Assert.True(Path.IsPathRooted(resolved), $"'{resolved}' is not a full path");
        Assert.True(File.Exists(resolved), $"'{resolved}' does not exist");
    }

    /// <summary>An explicit path is a deliberate choice in a settings template — the archiver
    /// or editor the user typed in — so it is honoured even though a bare name isn't.</summary>
    [Fact]
    public void AnExplicitPathIsHonoured()
    {
        var dir = Scratch();
        var planted = PlantExecutable(dir, OperatingSystem.IsWindows() ? "zzvesperplant.exe" : "zzvesperplant");
        Assert.Equal(planted, ProgramPath.Resolve(planted));
        Assert.Null(ProgramPath.Resolve(Path.Combine(dir, "not-here.exe")));
    }

    /// <summary>
    /// The one that made this urgent: <c>SevenZip.Available()</c> runs from the
    /// <c>CommanderApp</c> constructor, so a 7z.exe in the launch folder used to be executed
    /// at startup with no user action at all.
    /// </summary>
    [Fact]
    public void SevenZipNeverResolvesToABinaryInTheCurrentDirectory()
    {
        var dir = Scratch();
        var planted = PlantExecutable(dir, OperatingSystem.IsWindows() ? "7z.exe" : "7z");
        var configured = SevenZip.ExePath;
        try
        {
            SevenZip.ExePath = "";   // "find it yourself" — the default
            InDirectory(dir, () =>
            {
                var resolved = SevenZip.ResolvePath();
                // Compared by *directory*, not by path string: PATHEXT hands back "7z.EXE"
                // where the plant is "7z.exe", and a string comparison would call that a pass
                // while happily launching the plant.
                if (resolved != null)
                {
                    Assert.False(
                        string.Equals(Path.GetDirectoryName(resolved), Path.GetDirectoryName(planted),
                            StringComparison.OrdinalIgnoreCase),
                        $"resolved '{resolved}' out of the current directory");
                    // Never a bare name handed back for the OS to resolve later.
                    Assert.True(Path.IsPathRooted(resolved), $"'{resolved}' is not a full path");
                    Assert.True(File.Exists(resolved));
                }
            });
        }
        finally { SevenZip.ExePath = configured; }
    }
}
