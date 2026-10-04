using System.Diagnostics;
using System.Text;
using Xunit;

namespace HultUI.Terminal.Pty.Tests;

/// <summary>
/// <see cref="PtyConnection"/> against real programs under a real pseudoterminal: ConPTY on
/// Windows, forkpty elsewhere. The test child (HultUI.Terminal.Pty.Tests.Child) runs everywhere,
/// so most of these depend on no shell.
/// </summary>
public class PtyConnectionTests
{
    // ──── Output and ending ────

    [Fact]
    public void TheProgramsOutput_Arrives_AndItsExitCodeEndsTheSession()
    {
        using var t = PtyTerm.Child("exit", "3");
        t.WaitForText("bye");
        var clock = Stopwatch.StartNew();
        t.WaitForExit();
        Assert.Equal(3, t.Controller.ExitCode);
        // ConPTY keeps the output open after the program exits; the connection closes it once
        // the last of the output is in. Without that the exit would wait for the controller's
        // one-second fallback for output something else holds open.
        Assert.True(clock.Elapsed < TerminalController.OpenOutputGrace * 0.7,
            $"the exit was reported {clock.ElapsedMilliseconds} ms after the program's last words");
    }

    [Fact]
    public void Arguments_ArriveExactlyAsGiven()
    {
        string[] arguments =
        [
            "plain", "two words", "say \"hi\"", @"trailing\", @"C:\dir with space\", "", @"a\\b", "a&b|c<d>",
            "ünï€",
        ];
        using var t = PtyTerm.Child(["args", .. arguments]);
        t.WaitForExit();

        var lines = t.Screen.Split('\n');
        Assert.Equal(arguments.Select(a => $"[{a}]"), lines.Where(l => l.StartsWith('[')));
    }

    [Fact]
    public void AResize_ReachesTheProgram()
    {
        using var t = PtyTerm.Child("size");
        t.WaitForText("READY");
        t.Controller.Resize(100, 30);
        t.Controller.Send("\r");
        t.WaitForText("SIZE 100 30");
    }

    [Fact]
    public void AMebibytePaste_ReachesTheProgram_ThroughTheWriter()
    {
        // The spike: written by the reader thread, a paste deadlocked for good at 256 KiB.
        const int lines = 16 * 1024;
        using var t = PtyTerm.Child("echo");
        t.WaitForText("READY");

        var text = new StringBuilder(lines * 64);
        for (int i = 0; i < lines; i++) text.Append(i.ToString("D8")).Append(' ', 54).Append('\r');
        var clock = Stopwatch.StartNew();
        t.Controller.Send(text.ToString());
        Assert.True(clock.ElapsedMilliseconds < 200, $"Send took {clock.ElapsedMilliseconds} ms");
        t.Controller.Send("END\r");

        t.WaitForText($"GOT END after {lines} lines", TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void AFlood_ArrivesWhole()
    {
        using var t = PtyTerm.Child("flood", "16");
        t.WaitForText("FLOOD-DONE");
        t.WaitForExit();
        Assert.Equal(0, t.Controller.ExitCode);
    }

    [Fact]
    public void ABackgroundProcessHoldingTheTerminal_DoesNotHoldTheExit()
    {
        // The shell exits; what it left running still holds the pseudoterminal open.
        var launch = OperatingSystem.IsWindows()
            ? new PtyLaunch("cmd.exe", "/c", "start /b ping -n 60 127.0.0.1 >nul & exit 4")
            : new PtyLaunch("/bin/sh", "-c", "sleep 60 & exit 4");
        using var t = PtyTerm.Start(launch);
        var clock = Stopwatch.StartNew();
        t.WaitForExit(TimeSpan.FromSeconds(10));
        Assert.Equal(4, t.Controller.ExitCode);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"the exit took {clock.Elapsed.TotalSeconds:F1} s");
    }

    // ──── Teardown ────

    [Fact]
    public void Dispose_WhileTheProgramFloods_DoesNotHang()
    {
        var t = PtyTerm.Child("flood", "4096");
        t.WaitFor(s => s.Contains("quick brown fox"), what: "the flood");
        int pid = t.Connection.ProcessId;

        var clock = Stopwatch.StartNew();
        t.Dispose();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Dispose took {clock.Elapsed.TotalSeconds:F1} s");
        AssertGone(pid);
    }

    [Fact]
    public void Dispose_WhileTheProgramWaitsForInput_EndsIt()
    {
        var t = PtyTerm.Child("wait");
        t.WaitForText("READY");
        int pid = t.Connection.ProcessId;

        var clock = Stopwatch.StartNew();
        t.Dispose();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Dispose took {clock.Elapsed.TotalSeconds:F1} s");
        AssertGone(pid);
    }

    private static void AssertGone(int pid)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) return;
            }
            catch (ArgumentException) { return; } // no such process
            Thread.Sleep(50);
        }
        Assert.Fail($"process {pid} is still running");
    }

    // ──── Starting ────

    [Fact]
    public void AMissingProgram_Throws_TheSameOnEveryPlatform() =>
        Assert.Throws<FileNotFoundException>(() => PtyConnection.Start(new PtyLaunch("hultui-no-such-program-4711")));

    [Fact]
    public void TheEnvironment_HasTheTerminalsName_AndWhatTheLaunchAdds()
    {
        var launch = OperatingSystem.IsWindows()
            ? new PtyLaunch("cmd.exe", "/c", "echo [%TERM%] [%COLORTERM%] [%HULTUI_PROBE%]")
            : new PtyLaunch("/bin/sh", "-c", "echo [$TERM] [$COLORTERM] [$HULTUI_PROBE]");
        using var t = PtyTerm.Start(new PtyLaunch(launch.Program, launch.Arguments)
        {
            Environment = new Dictionary<string, string?> { ["HULTUI_PROBE"] = "yes" },
        });
        t.WaitForText("[xterm-256color] [truecolor] [yes]");
    }

    /// <summary>
    /// A program that is over before anyone reads its output still gets it all read, and then
    /// the end. On a loaded machine the reader can start late — a cold process, thirty test
    /// assemblies at once — and the pseudoconsole used to be closed, and the pipe with it, once
    /// nothing had been read for 100 ms after the exit: `cmd /c echo` came out as an empty screen.
    /// Here the reader waits half a second, deliberately.
    /// </summary>
    [Fact]
    public void AQuickProgramsOutput_WaitsForAReaderThatComesLate()
    {
        var launch = OperatingSystem.IsWindows()
            ? new PtyLaunch("cmd.exe", "/c", "echo [late reader]")
            : new PtyLaunch("/bin/sh", "-c", "echo [late reader]");
        using var connection = PtyConnection.Start(launch);
        using var exited = new ManualResetEventSlim();
        connection.Exited += _ => exited.Set();
        Assert.True(exited.Wait(PtyTerm.Patience), "the program did not end");

        Thread.Sleep(500);

        var output = new MemoryStream();
        var read = Task.Run(() => connection.Output.CopyTo(output));
        Assert.True(read.Wait(PtyTerm.Patience), "the output never ended");
        Assert.Contains("[late reader]", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public void TheWorkingDirectory_IsTheLaunches()
    {
        var dir = Directory.CreateTempSubdirectory("hultui pty ").FullName;
        try
        {
            var launch = OperatingSystem.IsWindows()
                ? new PtyLaunch("cmd.exe", "/c", "cd") { WorkingDirectory = dir }
                : new PtyLaunch("/bin/sh", "-c", "pwd") { WorkingDirectory = dir };
            using var t = PtyTerm.Start(launch, columns: 200);
            t.WaitForExit();
            Assert.Contains(dir.TrimEnd(Path.DirectorySeparatorChar), t.Screen.Replace("\n", ""));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [ProgramFact("cmd", windowsOnly: true)]
    public void OnWindows_TheOutOfBandConsoleHostRuns()
    {
        // conpty.dll with no OpenConsole.exe beside it falls back to Windows' own console host
        // silently, and that one races a bare ESC (the spike). This build is portable (no
        // RuntimeIdentifier), the layout Porta.Pty's targets had to stage the host for.
        using var t = PtyTerm.Child("wait");
        t.WaitForText("READY");

        var ours = Process.GetProcessesByName("OpenConsole").Select(p =>
        {
            try { return p.MainModule?.FileName; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; }
            finally { p.Dispose(); }
        }).Where(path => path?.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase) == true).ToList();
        Assert.NotEmpty(ours);
    }

    // ──── Shells ────

    [Fact]
    public void TheDefaultShell_Runs_AndExitsWithItsCode()
    {
        var launch = PtyLaunch.DefaultShell();
        using var t = PtyTerm.Start(launch);
        var shell = Path.GetFileNameWithoutExtension(launch.Program).ToLowerInvariant();

        // Something the shell has to work out, so its answer is told from the typed command.
        var (command, answer) = shell switch
        {
            "pwsh" or "powershell" => ("'hultui-' + (6*7)", "hultui-42"),
            "cmd" => ("set /a 6*7", "\n42"),
            "fish" => ("echo hultui-(math 6 \\* 7)", "hultui-42"),
            _ => ("echo hultui-$((6*7))", "hultui-42"),
        };
        t.Controller.Send(command + "\r");
        t.WaitForText(answer);

        t.Controller.Send("exit 7\r");
        t.WaitForExit();
        Assert.Equal(7, t.Controller.ExitCode);
    }

    [ProgramFact("pwsh", windowsOnly: true)]
    public void ANarrowedPrompt_StaysWhole_AndTypingContinuesIt()
    {
        // Problem 1 of the design: ConPTY reflows its own copy and repaints nothing, so the
        // emulator must reflow the cursor's line itself (patch 0004, ReflowCursorLine).
        // At the root, as the spike ran it, so the prompt is short and the command is what wraps.
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        using var t = PtyTerm.Start(new PtyLaunch("pwsh", "-NoLogo", "-NoProfile") { WorkingDirectory = root }, columns: 120, rows: 30);
        t.WaitFor(s => s.Contains("PS "), what: "the prompt");

        var typed = "Write-Output " + string.Join("-", Enumerable.Range(1, 9).Select(i => $"segment{i}"));
        t.Controller.Send(typed);
        t.WaitFor(s => Joined(s).Contains(typed), what: "the typed command");

        t.Controller.Resize(60, 30);
        t.Session.PumpFrames(5);
        Thread.Sleep(500);
        t.Session.PumpFrames(5);
        Assert.Contains(typed, Joined(t.Screen));

        t.Controller.Send("XYZ");
        t.WaitFor(s => Joined(s).Contains(typed + "XYZ"), what: "the command continued");
    }

    private static string Joined(string screen) => screen.Replace("\n", "");

    // ──── Pieces ────

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("", "\"\"")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"trailing\", @"trailing\")]
    [InlineData(@"dir with\", "\"dir with\\\\\"")]
    [InlineData(@"a\\b", @"a\\b")]
    [InlineData("a\\\"b", "\"a\\\\\\\"b\"")]
    public void AWindowsArgument_IsQuotedAsTheCRuntimeParses(string argument, string quoted) =>
        Assert.Equal(quoted, WindowsCommandLine.Quote(argument));

    [Fact]
    public void FindProgram_SearchesPath_AndTakesAPathAsItIs()
    {
        var dotnet = PtyLaunch.FindProgram("dotnet");
        Assert.NotNull(dotnet);
        Assert.True(Path.IsPathFullyQualified(dotnet));
        Assert.Equal(dotnet, PtyLaunch.FindProgram(dotnet));
        Assert.Null(PtyLaunch.FindProgram("hultui-no-such-program-4711"));
        Assert.Null(PtyLaunch.FindProgram(Path.Combine(Path.GetTempPath(), "hultui-no-such-program-4711")));
    }
}
