using System.Diagnostics;
using HultUI.Core.Events;
using HultUI.Terminal.Emulation;
using HultUI.Terminal.Testing;
using Xunit;

namespace HultUI.Terminal.Tests;

/// <summary>
/// <see cref="TerminalController"/> against a scripted program: output reaching the screen, input
/// reaching the program, what the program asks for, scrollback, and ending.
/// </summary>
public class TerminalControllerTests
{
    // ──── Output and input ────

    [Fact]
    public void Output_ReachesTheScreen()
    {
        using var t = Term.Headless();
        t.Emit("hello\r\nworld");
        Assert.Equal("hello\nworld", t.Screen());
    }

    [Fact]
    public void ALongLine_WrapsAtTheGridsWidth()
    {
        using var t = Term.Headless(columns: 10, rows: 5);
        t.Emit("0123456789abc");
        Assert.Equal("0123456789\nabc", t.Screen());
    }

    [Fact]
    public void Text_JoinsWrappedRows_AndDropsTrailingBlanks()
    {
        using var t = Term.Headless(columns: 10, rows: 5);
        t.Emit("0123456789abc\r\nnext   ");
        var text = t.Controller.GetText(new TerminalPosition(0, 0), new TerminalPosition(2, 10));
        Assert.Equal("0123456789abc\nnext", text);
    }

    [Fact]
    public void Send_ReachesTheProgram_AsUtf8()
    {
        using var t = Term.Headless();
        t.Controller.Send("ls -la åäö\r");
        Assert.Equal("ls -la åäö\r", t.Expect("\r"));
    }

    [Fact]
    public void AQuery_IsAnsweredByTheWriter_WithoutTheUiThread()
    {
        using var t = Term.Headless();
        // Cursor position report: the program asks, the emulator answers. No frame is pumped, so
        // the answer must not need the UI thread.
        t.Connection.Emit("abc\x1b[6n");
        Assert.True(SpinWait.SpinUntil(() => t.Connection.Received.Contains("\x1b[1;4R"), TimeSpan.FromSeconds(10)),
            $"no cursor report; received {Escape(t.Connection.Received)}");
    }

    [Fact]
    public void AnUnreadInput_DoesNotBlockTheCaller()
    {
        // A writer thread of its own: a paste far larger than any pipe buffer returns at once
        // even though nothing reads it yet (the spike deadlocked at 256 KiB without one).
        using var t = Term.Headless();
        var big = new string('x', 1 << 20);
        var sw = Stopwatch.StartNew();
        t.Controller.Send(big);
        Assert.True(sw.ElapsedMilliseconds < 500, $"Send took {sw.ElapsedMilliseconds} ms");
        t.Session.PumpUntil(() => t.Connection.Received.Length == big.Length, TimeSpan.FromSeconds(10));
    }

    // ──── What the program asks for ────

    [Fact]
    public void TheTitle_IsASignal_AndRaisesTitleChanged()
    {
        using var t = Term.Headless();
        var seen = new List<string>();
        t.Controller.TitleChanged += seen.Add;
        t.Emit("\x1b]0;build — 3 errors\x07");
        Assert.Equal("build — 3 errors", t.Controller.Title);
        Assert.Equal(["build — 3 errors"], seen);
    }

    [Fact]
    public void TheWorkingDirectory_ComesFromOsc7()
    {
        using var t = Term.Headless();
        t.Emit("\x1b]7;file://host/home/fredrik/src\x07");
        Assert.Equal("/home/fredrik/src", t.Controller.WorkingDirectory);
    }

    [Fact]
    public void TheBell_IsRaisedOnTheUiThread()
    {
        using var t = Term.Headless();
        int rings = 0;
        bool onUi = false;
        t.Controller.Bell += () => { rings++; onUi = HultUI.Core.Threading.UIThread.IsCurrent; };
        t.Emit("\a");
        Assert.Equal(1, rings);
        Assert.True(onUi);
    }

    [Fact]
    public void AClipboardWrite_CanBeRefused()
    {
        using var t = Term.Headless();
        var written = new List<string>();
        t.Controller.ClipboardWrite += written.Add;
        t.Controller.ClipboardWriteRequested += r => r.Cancel = r.Text == "secret";

        t.Emit("\x1b]52;c;" + Convert.ToBase64String("secret"u8) + "\x07");
        t.Emit("\x1b]52;c;" + Convert.ToBase64String("fine"u8) + "\x07");

        Assert.Equal(["fine"], written);
    }

    [Fact]
    public void APaste_IsBracketed_WhenTheProgramAsks_AndCannotEndTheBracketEarly()
    {
        using var t = Term.Headless();
        t.Controller.Paste("plain");
        Assert.Equal("plain", t.Expect("plain"));

        t.Emit("\x1b[?2004h");
        t.Controller.Paste("rm -rf /\x1b[201~oops");
        var got = t.Expect("\x1b[201~");
        Assert.StartsWith("\x1b[200~", got);
        Assert.EndsWith("\x1b[201~", got);
        Assert.Equal(1, CountOf(got, "\x1b[201~"));
    }

    // ──── Grid ────

    [Fact]
    public void Resize_TellsTheProgram_AndUpdatesTheSignals()
    {
        using var t = Term.Headless(columns: 80, rows: 24);
        t.Controller.Resize(100, 30);
        Assert.Equal((100, 30), t.Connection.Resizes[^1]);
        Assert.Equal(100, t.Controller.Columns);
        Assert.Equal(30, t.Controller.Rows);
        var s = t.Snapshot();
        Assert.Equal((100, 30), (s.Columns, s.Rows));
    }

    [Fact]
    public void Resize_ToTheSameSize_TellsNobody()
    {
        using var t = Term.Headless(columns: 80, rows: 24);
        int before = t.Connection.Resizes.Count;
        t.Controller.Resize(80, 24);
        Assert.Equal(before, t.Connection.Resizes.Count);
    }

    // ──── Scrollback ────

    [Fact]
    public void Scrollback_DefaultsTo5000_AndIsConfigurable()
    {
        Assert.Equal(5000, new TerminalOptions().Scrollback);
        Assert.Equal(200, new TerminalOptions { Scrollback = 200 }.Scrollback);
        Assert.Equal(TerminalOptions.MaxScrollback, new TerminalOptions { Scrollback = int.MaxValue }.Scrollback);
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalOptions { Scrollback = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalOptions { MaxScrollbackBytes = -1 });
    }

    [Fact]
    public void Scrollback_KeepsAtMostTheLinesAskedFor()
    {
        using var t = Term.Headless(new TerminalOptions { Scrollback = 10 }, columns: 20, rows: 5);
        t.Emit(Lines(100));
        Assert.Equal(10, t.Snapshot().ScrollbackLines);
    }

    [Fact]
    public void Scrollback_ChangesTakeEffectAtOnce()
    {
        using var t = Term.Headless(new TerminalOptions { Scrollback = 50 }, columns: 20, rows: 5);
        t.Emit(Lines(100));
        Assert.Equal(50, t.Snapshot().ScrollbackLines);

        t.Controller.Scrollback = 20;
        Assert.Equal(20, t.Snapshot().ScrollbackLines);
        Assert.Equal("line 99", t.Screen().Split('\n')[^1]); // the newest lines stay

        t.Controller.Scrollback = 0;
        Assert.Equal(0, t.Snapshot().ScrollbackLines);
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Controller.Scrollback = -5);
    }

    [Fact]
    public void AByteBudget_CutsTheLinesKept_ByTheWidth()
    {
        // 80 columns × 25 bytes a cell = 2000 bytes a line; 7 lines' worth.
        using var t = Term.Headless(new TerminalOptions { Scrollback = 1000, MaxScrollbackBytes = 7 * 2000 }, columns: 80, rows: 5);
        Assert.Equal(7, t.Controller.EffectiveScrollbackLines);
        t.Emit(Lines(50));
        Assert.Equal(7, t.Snapshot().ScrollbackLines);

        t.Controller.Resize(40, 5);
        Assert.Equal(14, t.Controller.EffectiveScrollbackLines);

        t.Controller.MaxScrollbackBytes = null;
        Assert.Equal(1000, t.Controller.EffectiveScrollbackLines);
    }

    [Fact]
    public void LinesDropped_CountsWhatScrolledOffTheTop()
    {
        using var t = Term.Headless(new TerminalOptions { Scrollback = 10 }, columns: 20, rows: 5);
        t.Emit(Lines(100));
        var s = t.Snapshot();
        // 100 lines and an empty one for the cursor: 101 - 5 on screen - 10 kept.
        Assert.Equal(86, s.LinesDropped);
    }

    [Fact]
    public void TheAlternateScreen_KeepsTheNormalScreensScrollback()
    {
        using var t = Term.Headless(new TerminalOptions { Scrollback = 100 }, columns: 20, rows: 5);
        t.Emit(Lines(30));
        int before = t.Snapshot().ScrollbackLines;

        t.Emit("\x1b[?1049h" + Lines(30));
        Assert.Equal(TerminalModes.AlternateScreen, t.Snapshot().Modes & TerminalModes.AlternateScreen);
        t.Emit("\x1b[?1049l");

        Assert.Equal(before, t.Snapshot().ScrollbackLines);
        Assert.Equal("line 29", t.Row(t.Snapshot().CursorRow - 1));
    }

    [Fact]
    public void Scrollback_SetOnTheAlternateScreen_AppliesWhenTheProgramReturns()
    {
        using var t = Term.Headless(new TerminalOptions { Scrollback = 100 }, columns: 20, rows: 5);
        t.Emit(Lines(60));
        t.Emit("\x1b[?1049h");

        t.Controller.Scrollback = 10;
        t.Emit("\x1b[?1049l");

        Assert.Equal(10, t.Snapshot().ScrollbackLines);
    }

    // ──── Ending ────

    [Fact]
    public void TheProgramEnding_RaisesExitedOnce_WithItsCode()
    {
        using var t = Term.Headless();
        var codes = new List<int?>();
        t.Controller.Exited += codes.Add;

        t.Connection.End(3);
        t.Session.PumpUntil(() => t.Controller.State == TerminalState.Exited, TimeSpan.FromSeconds(10));
        t.Session.PumpFrames(3);

        Assert.Equal(3, t.Controller.ExitCode);
        Assert.Equal([3], codes);
    }

    [Fact]
    public void AnExit_WhileSomethingHoldsTheOutputOpen_IsReportedAnyway()
    {
        // `sleep 60 &` then `exit 4`: the shell is gone, the background job still holds the
        // pseudoterminal, and the output never ends.
        using var t = Term.Headless();
        var codes = new List<int?>();
        t.Controller.Exited += codes.Add;
        var clock = Stopwatch.StartNew();

        t.Connection.ExitKeepingOutputOpen(4);
        t.Session.PumpUntil(() => t.Controller.State == TerminalState.Exited, TimeSpan.FromSeconds(10));

        Assert.Equal(4, t.Controller.ExitCode);
        Assert.Equal([4], codes);
        Assert.True(clock.Elapsed >= TerminalController.OpenOutputGrace - TimeSpan.FromMilliseconds(50));
        // The input stays open to whatever still reads it.
        t.Controller.Send("still there");
        t.Expect("still there");

        t.Connection.End(4); // the job ends too: no second report
        t.Session.PumpFrames(5);
        Assert.Equal([4], codes);
    }

    [Fact]
    public void AProgramThatEndedBeforeItsController_StillReportsItsExit()
    {
        // `cmd /c echo hi` is gone in 60 ms, before the controller made for it listens.
        using var session = Headless.HeadlessSession.Create(200, 100);
        var connection = new FakeConnection();
        connection.Emit("hi");
        connection.End(5);

        using var controller = new TerminalController(connection);
        session.PumpUntil(() => controller.State == TerminalState.Exited, TimeSpan.FromSeconds(10));
        Assert.Equal(5, controller.ExitCode);
    }

    [Fact]
    public void ConnectionExit_IsRaisedOnce_AndALateHandlerHearsIt()
    {
        var exit = new ConnectionExit();
        var heard = new List<int?>();
        exit.Add(heard.Add);
        Assert.True(exit.Raise(3));
        Assert.False(exit.Raise(4));
        exit.Add(heard.Add); // after the end: called at once
        Assert.Equal([3, 3], heard);
        Assert.True(exit.HasExited);
        Assert.Equal(3, exit.ExitCode);
    }

    [Fact]
    public void ADisposedController_ReportsNoExit()
    {
        using var session = Headless.HeadlessSession.Create(200, 100);
        var connection = new FakeConnection();
        var controller = new TerminalController(connection);
        int exits = 0;
        controller.Exited += _ => exits++;
        controller.Dispose(); // ends the program, whose exit the application already knows of
        session.PumpFrames(5);
        Thread.Sleep(100);
        session.PumpFrames(5);
        Assert.Equal(0, exits);
    }

    [Fact]
    public void ReadScreen_IsTheVisibleText()
    {
        using var t = Term.Headless(columns: 10, rows: 5);
        t.Emit("abc   \r\n\r\n日本x\x1b[1mbold\x1b[0m");
        Assert.Equal("abc\n\n日本xbold", t.Controller.ReadScreen());
    }

    [Fact]
    public void OutputEndingWithoutAnExitCode_EndsTheSession_WithNoCode()
    {
        using var session = Headless.HeadlessSession.Create(200, 100);
        var output = new TerminalPipe();
        using var controller = new TerminalController(new StreamConnection(output, new TerminalPipe()));
        output.Write("bye"u8);
        output.Complete();

        session.PumpUntil(() => controller.State == TerminalState.Exited, TimeSpan.FromSeconds(10));
        Assert.Null(controller.ExitCode);
    }

    [Fact]
    public void Dispose_EndsTheProgram_ThenClosesTheConnection_WithoutHanging()
    {
        using var session = Headless.HeadlessSession.Create(200, 100);
        var connection = new FakeConnection();
        var controller = new TerminalController(connection);
        controller.Send("pending");

        var sw = Stopwatch.StartNew();
        controller.Dispose();
        Assert.True(sw.ElapsedMilliseconds < 1500, $"Dispose took {sw.ElapsedMilliseconds} ms");
        Assert.True(connection.IsTerminated);
        Assert.True(connection.IsDisposed);
        Assert.Equal("pending", connection.Received); // the writer finished before the program ended
    }

    [Fact]
    public void AfterDispose_EverythingIsInert()
    {
        using var session = Headless.HeadlessSession.Create(200, 100);
        var controller = new TerminalController(new FakeConnection());
        var s = new TerminalSnapshot();
        controller.CopySnapshot(s);
        controller.Dispose();

        controller.Send("x");
        controller.Paste("x");
        controller.Resize(10, 10);
        controller.ScrollViewport(-3);
        controller.ScrollToBottom();
        controller.Scrollback = 10;
        controller.CopySnapshot(s);
        Assert.Null(controller.EncodeKey(new TerminalKey(KeyCode.Enter, KeyModifiers.None)));
        Assert.Equal("", controller.GetText(new TerminalPosition(0, 0), new TerminalPosition(0, 5)));
        controller.Dispose();
    }

    // ──── Helpers ────

    internal static string Lines(int count) =>
        string.Concat(Enumerable.Range(0, count).Select(i => $"line {i}\r\n"));

    private static int CountOf(string text, string part)
    {
        int n = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }

    internal static string Escape(string s) => s.Replace("\x1b", "\\e").Replace("\r", "\\r").Replace("\n", "\\n");
}
