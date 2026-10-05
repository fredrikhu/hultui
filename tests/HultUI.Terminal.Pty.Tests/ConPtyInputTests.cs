using System.Diagnostics;
using HultUI.Core.Builders;
using HultUI.Core.Events;
using HultUI.Headless;
using HultUI.Terminal.Emulation;
using Xunit;

namespace HultUI.Terminal.Pty.Tests;

/// <summary>
/// Keys and resizes through ConPTY, driven through the element as a user would, to a native
/// console program (the test child's <c>events</c> mode, which reports every input record) and
/// to Git for Windows' own programs.
/// </summary>
public class ConPtyInputTests
{
    /// <summary>The element on a real pseudoconsole, focused, in a headless window.</summary>
    private sealed class Shown : IDisposable
    {
        public Shown(PtyLaunch launch)
        {
            Session = HeadlessSession.Create(900, 600);
            // The program starts at the size it is shown at, once the element has laid out its
            // grid. Started at another and resized as it lays out, the resize lands while the
            // program starts, and Git for Windows' programs miss about one resize in six until the
            // next input arrives, throwing away its first key (problem 22). In vim that key was
            // the first round's A: `hello` ran as normal-mode commands, its o opened a line in
            // insert mode, and the file came out a hello short — on CI once, and 4 of 30 times
            // here with a resize forced into vim's start.
            Controller = new TerminalController(new DeferredConnection(
                async (size, ct) => await PtyConnection.StartAsync(launch, size.Columns, size.Rows, ct)));
            var element = TerminalElement.New(Controller).FlexGrow(1);
            Session.Window.SetRoot(Flex.Column().Children(element));
            Session.PumpFrames(2);
            element.Focus();
            Session.PumpFrame();
        }

        public HeadlessSession Session { get; }
        public TerminalController Controller { get; }
        public HeadlessWindow Window => Session.Window;
        public string Screen => Controller.ReadScreen();

        public void WaitFor(Func<string, bool> condition, string what)
        {
            try { Session.PumpUntil(() => condition(Screen), PtyTerm.Patience); }
            catch (TimeoutException) { Assert.Fail($"timed out waiting for {what}; the screen shows:\n{Screen}"); }
        }

        public bool Within(Func<string, bool> condition, int milliseconds)
        {
            try { Session.PumpUntil(() => condition(Screen), TimeSpan.FromMilliseconds(milliseconds)); return true; }
            catch (TimeoutException) { return false; }
        }

        public void Command(string command)
        {
            Window.TypeText(command);
            Window.PressKey(KeyCode.Enter, text: "");
        }

        /// <summary>Resizes the window between two sizes, and waits until the grid follows.</summary>
        public void Resize(int round)
        {
            int columns = Controller.Columns;
            Window.SetClientSize(round % 2 == 0 ? 700 : 900, round % 2 == 0 ? 450 : 600);
            Session.PumpUntil(() => Controller.Columns != columns, TimeSpan.FromSeconds(10));
        }

        public void AssertWin32InputMode()
        {
            var snapshot = new TerminalSnapshot();
            Controller.CopySnapshot(snapshot);
            Assert.True((snapshot.Modes & TerminalModes.Win32Input) != 0, "ConPTY did not ask for win32-input-mode");
        }

        public void Dispose()
        {
            Controller.Dispose();
            Session.Dispose();
        }
    }

    [ProgramFact("dotnet", windowsOnly: true)]
    public void ANativeConsoleProgram_ReadsTheAmbiguousKeysExactly()
    {
        // What a program reading console key events (PSReadLine, .NET's Console.ReadKey) gets. As
        // VT bytes, Ctrl+Space arrived as virtual key 0x40 with no character, and the others each
        // after a bare press of their modifier.
        using var t = new Shown(PtyTerm.ChildLaunch("events"));
        t.WaitFor(s => s.Contains("READY"), "the program");
        t.AssertWin32InputMode();
        string[] Keys() => t.Screen.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("KEY ")).ToArray();

        t.Window.PressKey(KeyCode.Space, KeyModifiers.Ctrl, layoutKey: ' ', modifierKeys: ModifierKeys.CtrlLeft, text: " ");
        t.Window.PressKey(KeyCode.Backspace, KeyModifiers.Ctrl, modifierKeys: ModifierKeys.CtrlLeft, text: "");
        t.Window.PressKey(KeyCode.A, KeyModifiers.Alt, layoutKey: 'a', modifierKeys: ModifierKeys.AltLeft, text: "");
        t.Window.PressKey(KeyCode.Escape, text: "");
        t.Window.TypeText("b");
        t.Within(_ => Keys().Length >= 5, (int)PtyTerm.Patience.TotalMilliseconds);

        // virtual key, scan code, character, modifier state: Ctrl+Space, Ctrl+Backspace, Alt+A,
        // Escape, b.
        string[] expected = ["KEY 32 57 32 8", "KEY 8 14 127 8", "KEY 65 30 97 2", "KEY 27 1 27 0", "KEY 66 48 98 0"];
        var read = Keys();
        Assert.True(read.SequenceEqual(expected), "the program read: " + string.Join(", ", read));
    }

    [ProgramFact("dotnet", windowsOnly: true)]
    public void ANativeConsoleProgram_SeesEveryResize_AndTheKeyAfterIt()
    {
        // ConPTY's side of problem 22: the resize reaches a native program at once and loses no
        // key. Git for Windows' programs lose one (GitsPrograms_LoseNoKeyAfterAResize).
        using var t = new Shown(PtyTerm.ChildLaunch("events"));
        t.WaitFor(s => s.Contains("READY"), "the program");
        string Last() => t.Screen.Split('\n').Select(l => l.TrimEnd()).LastOrDefault(l => l.Length > 0) ?? "";
        for (int round = 0; round < 20; round++)
        {
            t.Resize(round);
            var size = $"SIZE {t.Controller.Columns} {t.Controller.Rows}";
            Assert.True(t.Within(_ => Last().StartsWith(size), 1000), $"round {round}: no '{size}', the screen ends '{Last()}'");
            char key = (char)('a' + round % 16); // a to p: q ends the child
            t.Window.TypeText(key.ToString());
            Assert.True(t.Within(_ => Last().StartsWith("KEY ") && Last().Split(' ')[3] == ((int)key).ToString(), 2000),
                $"round {round}: '{key}' lost, the screen ends '{Last()}'");
        }
        t.Window.TypeText("q");
    }

    [GitVimFact]
    public void GitsVim_LeavesInsertModeOnEscape()
    {
        var file = Path.Combine(Path.GetTempPath(), $"hultui-vim-{Environment.ProcessId}.txt");
        File.Delete(file);
        // -n: no swap file, or a run that is killed leaves one that stops the next at a prompt.
        using var t = new Shown(new PtyLaunch(GitVimFactAttribute.Vim!, "-u", "NONE", "-i", "NONE", "-N", "-n", file));
        t.WaitFor(s => s.Split('\n').Count(l => l.TrimEnd() == "~") > 5, "vim");
        t.AssertWin32InputMode();

        for (int round = 0; round < 3; round++)
        {
            t.Window.TypeText("Ahello");
            t.WaitFor(s => s.Contains("-- INSERT --"), $"insert mode, round {round}");
            t.Window.PressKey(KeyCode.Escape, text: "");
            t.WaitFor(s => !s.Contains("-- INSERT --"), $"Escape to leave insert mode, round {round}");
        }
        t.Command(":wq");
        t.Session.PumpUntil(() => t.Controller.State == TerminalState.Exited, PtyTerm.Patience);
        Assert.Equal("hellohellohello", File.ReadAllText(file).Trim());
        File.Delete(file);
    }

    /// <summary>
    /// Problem 22, measured: Git for Windows' MSYS2 runtime (3.6.6 in Git 2.53) misses about one
    /// resize in six until the next input arrives, and throws away the first character of it. Not
    /// the terminal's to fix (docs/design/TERMINAL.md), so this runs only when asked
    /// (<c>HULTUI_TERMINAL_MEASURE=1</c>), to see whether a newer Git still loses keys.
    /// </summary>
    [GitVimFact(measurement: true)]
    public void GitsPrograms_LoseNoKeyAfterAResize()
    {
        var bash = Path.Combine(Path.GetDirectoryName(GitVimFactAttribute.Vim!)!, "bash.exe");
        using var t = new Shown(new PtyLaunch(bash, "--norc", "--noprofile", "-i"));
        t.WaitFor(s => s.Contains('$'), "bash");
        var lost = new List<int>();
        for (int round = 0; round < 30; round++)
        {
            t.Resize(round);
            t.Within(_ => false, 300);
            t.Command($"echo R{round}X");
            if (!t.Within(s => s.Contains($"\nR{round}X"), 3000)) lost.Add(round); // "cho: command not found"
            t.Command("clear");
            t.Within(_ => false, 200);
        }
        Assert.True(lost.Count == 0, $"the first key after the resize was lost in {lost.Count} of 30 rounds ({string.Join(", ", lost)})");
    }
}

/// <summary>Runs on Windows where Git for Windows' vim is installed; a measurement also only when
/// <c>HULTUI_TERMINAL_MEASURE=1</c>.</summary>
internal sealed class GitVimFactAttribute : FactAttribute
{
    public static readonly string? Vim = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetEnvironmentVariable("ProgramFiles") ?? @"C:\Program Files", "Git", "usr", "bin", "vim.exe") is var vim && File.Exists(vim) ? vim : null
        : null;

    public GitVimFactAttribute(bool measurement = false)
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows only: this is about ConPTY";
        else if (Vim is null) Skip = "Git for Windows' vim is not installed";
        else if (measurement && Environment.GetEnvironmentVariable("HULTUI_TERMINAL_MEASURE") != "1")
            Skip = "a measurement: set HULTUI_TERMINAL_MEASURE=1";
    }
}
