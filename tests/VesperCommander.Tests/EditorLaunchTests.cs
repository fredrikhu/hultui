using System.Diagnostics;
using Xunit;

namespace VesperCommander.Tests;

/// <summary>
/// How F4 hands an editor to the OS.
///
/// <para>VS Code's <c>code</c> on PATH is <c>code.cmd</c>, a batch file — Windows runs one by
/// starting cmd.exe, which brings a console window with it. Opening a file therefore put a
/// terminal on screen next to the editor. Measured on Windows before the fix: a shell-executed
/// <c>.cmd</c> reports its own console as visible, and the same launch with the hidden window
/// style reports it as not visible, while a GUI program the batch file goes on to start still
/// shows normally (checked both shapes — <c>start "" app</c> and a direct invocation, which is
/// what code.cmd does).</para>
///
/// <para>Only batch files get the hidden style. It reaches a GUI program as its initial show
/// state, and one that honours it would start invisible.</para>
/// </summary>
public class EditorLaunchTests
{
    [Theory]
    [InlineData("code.cmd")]
    [InlineData(@"C:\Program Files\Thing\run.bat")]
    [InlineData("SHOUTY.CMD")]           // the extension is matched case-insensitively
    public void ABatchLauncherIsAskedToStartHidden(string program)
    {
        Assert.True(FileOps.IsBatchLauncher(program));
        Assert.Equal(ProcessWindowStyle.Hidden, FileOps.ShellLaunch(program, []).WindowStyle);
    }

    [Theory]
    [InlineData(@"C:\Windows\notepad.exe")]
    [InlineData(@"C:\tools\mspaint.exe")]
    [InlineData("code")]                  // no extension resolved: not known to be a batch file
    public void AProgramKeepsTheNormalWindowStyle(string program)
    {
        Assert.False(FileOps.IsBatchLauncher(program));
        Assert.Equal(ProcessWindowStyle.Normal, FileOps.ShellLaunch(program, []).WindowStyle);
    }

    /// <summary>Hiding the console must not cost the launch its arguments. They go in the
    /// ArgumentList so the launcher quotes each one, which is what keeps a path with spaces
    /// a single argument.</summary>
    [Fact]
    public void TheArgumentsSurviveOnTheirOwn()
    {
        var psi = FileOps.ShellLaunch("code.cmd", ["--goto", @"C:\my files\a.txt:12"]);

        Assert.True(psi.UseShellExecute);
        Assert.Equal(["--goto", @"C:\my files\a.txt:12"], psi.ArgumentList);
        Assert.Equal("", psi.Arguments);   // not smashed into one string
    }

    [Fact]
    public void AWorkingDirectoryIsPassedThroughWhenOneIsGiven()
    {
        Assert.Equal(@"C:\work", FileOps.ShellLaunch("app.exe", [], workingDir: @"C:\work").WorkingDirectory);
        Assert.Equal("", FileOps.ShellLaunch("app.exe", []).WorkingDirectory);
    }

    /// <summary>The template still resolves to a program plus separate arguments — the part
    /// that decides *what* runs, unchanged by how its window is asked to appear.</summary>
    [Fact]
    public void TheEditorTemplateStillResolvesToAProgramAndArguments()
    {
        var inv = FileOps.BuildEditorInvocation("code --goto {file}:{line}", @"C:\my files\a.txt", 12);

        Assert.NotNull(inv);
        Assert.Equal("code", inv!.Value.Exe);
        Assert.Equal(["--goto", @"C:\my files\a.txt:12"], inv.Value.Args);
    }
}
