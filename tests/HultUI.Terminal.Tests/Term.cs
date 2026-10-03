using System.Text;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Headless;
using HultUI.Terminal.Emulation;
using HultUI.Terminal.Testing;

namespace HultUI.Terminal.Tests;

/// <summary>
/// A terminal under a headless session: a scripted program on a <see cref="FakeConnection"/>,
/// its controller, and (when shown) the element filling the window.
/// </summary>
internal sealed class Term : IDisposable
{
    private long _emitted;

    private Term(HeadlessSession session, FakeConnection connection, TerminalController controller, TerminalElement? element)
    {
        Session = session;
        Connection = connection;
        Controller = controller;
        Element = element;
    }

    public HeadlessSession Session { get; }
    public FakeConnection Connection { get; }
    public TerminalController Controller { get; }
    public TerminalElement? Element { get; }
    public HeadlessWindow Window => Session.Window;

    /// <summary>A controller with no element, at <paramref name="columns"/> × <paramref name="rows"/>.</summary>
    public static Term Headless(TerminalOptions? options = null, int columns = 80, int rows = 24)
    {
        var session = HeadlessSession.Create(400, 300);
        var connection = new FakeConnection();
        var controller = new TerminalController(connection, options);
        controller.Resize(columns, rows);
        return new Term(session, connection, controller, null);
    }

    /// <summary>The element filling a <paramref name="width"/> × <paramref name="height"/> window,
    /// its grid sized from it, and focused.</summary>
    public static Term Shown(TerminalOptions? options = null, int width = 400, int height = 300,
        bool recording = false, Func<TerminalElement, Element>? around = null,
        TerminalConnectionTraits traits = TerminalConnectionTraits.None)
    {
        var session = HeadlessSession.Create(width, height,
            options: recording ? new HeadlessOptions { RecordDrawCalls = true } : null);
        var connection = new FakeConnection(traits);
        var controller = new TerminalController(connection, options);
        var element = TerminalElement.New(controller).FlexGrow(1);
        session.Window.SetRoot(around?.Invoke(element) ?? Flex.Column().Children(element));
        // One frame measures the cell and asks for the grid; the next applies it.
        session.PumpFrames(2);
        element.Focus();
        session.PumpFrame();
        var term = new Term(session, connection, controller, element);
        term.Connection.TakeReceived(); // the focus report, if any
        return term;
    }

    /// <summary>The program writes <paramref name="text"/>; returns once the emulator has it and
    /// the UI thread has handled what it raised, with a frame drawn after.</summary>
    public void Emit(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        _emitted += bytes.Length;
        Connection.Emit(bytes);
        Session.PumpUntil(() => Controller.BytesRead >= _emitted && !Controller.WakePending, TimeSpan.FromSeconds(10));
        Session.PumpFrame();
    }

    /// <summary>Waits for the writer thread to deliver <paramref name="expected"/>, then takes
    /// everything received.</summary>
    public string Expect(string expected)
    {
        Session.PumpUntil(() => Connection.Received.Contains(expected, StringComparison.Ordinal), TimeSpan.FromSeconds(10));
        return Connection.TakeReceived();
    }

    /// <summary>Everything the terminal sent within a few frames; for asserting what was not sent.</summary>
    public string Settle()
    {
        Session.PumpFrames(3);
        Thread.Sleep(50);
        return Connection.TakeReceived();
    }

    public TerminalSnapshot Snapshot()
    {
        var s = new TerminalSnapshot();
        Controller.CopySnapshot(s);
        return s;
    }

    /// <summary>A row of the visible screen, trailing blanks trimmed.</summary>
    public string Row(int row) => RowText(Snapshot(), row);

    /// <summary>The visible screen, one line per row, trailing blanks and blank rows trimmed.</summary>
    public string Screen()
    {
        var s = Snapshot();
        var lines = Enumerable.Range(0, s.Rows).Select(r => RowText(s, r)).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return string.Join('\n', lines);
    }

    public static string RowText(TerminalSnapshot s, int row)
    {
        var sb = new StringBuilder();
        foreach (var cell in s.Row(row))
        {
            if (cell.Width == 0) continue;
            if (cell.Cluster is { } cluster) sb.Append(cluster);
            else if (cell.CodePoint == 0) sb.Append(' ');
            else sb.Append(char.ConvertFromUtf32(cell.CodePoint));
        }
        return sb.ToString().TrimEnd(' ');
    }

    /// <summary>The window point at the middle of a cell.</summary>
    public (float X, float Y) CellCentre(int column, int row)
    {
        var box = Element!.ScreenContentBox;
        var (w, h) = Element.CellSize;
        return (box.X + (column + 0.5f) * w, box.Y + (row + 0.5f) * h);
    }

    public void Dispose()
    {
        Controller.Dispose();
        Session.Dispose();
    }
}
