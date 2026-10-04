using XTerm;
using XTerm.Buffer;
using XTerm.Options;

namespace XTerm.Tests.Buffer;

/// <summary>
/// Regression tests for shrinking a buffer that contains a wrapped group with no content in it.
/// </summary>
/// <remarks>
/// Such a group produces an empty result from ReflowSmallerGetNewLineLengths, which the reflow then
/// indexed at [Length - 1]. Only a ONE-ROW group can be empty, because every row of a group except
/// the last counts as a full row of cells regardless of what is in it -- so this needs a
/// continuation row at index 0 with an unwrapped row beneath, which is what the scrollback leaves
/// behind once the row being continued is trimmed away.
/// </remarks>
public class ReflowEmptyGroupTests
{
    [Fact]
    public void Shrink_WithBlankWrappedRowAtTop_DoesNotThrow()
    {
        // Twelve spaces at six columns wrap, so the tail row is both blank and wrapped. Two more
        // lines push the head of that pair out of a one-row scrollback, leaving the blank
        // continuation at index 0 with an unwrapped line under it.
        var terminal = new Terminal(new TerminalOptions { Cols = 6, Rows = 2, Scrollback = 1 });

        terminal.Write(new string(' ', 12));
        terminal.Write("\r\nx");
        terminal.Write("\r\ny");

        Assert.True(terminal.Buffer.Lines[0]!.IsWrapped, "precondition: the top row is a continuation");
        Assert.Equal(0, terminal.Buffer.Lines[0]!.GetTrimmedLength());
        Assert.False(terminal.Buffer.Lines[1]!.IsWrapped, "precondition: the row beneath starts fresh");

        Assert.Null(Record.Exception(() => terminal.Resize(4, 2)));
    }

    [Fact]
    public void Shrink_WithBlankWrappedRowAtTop_KeepsTheRemainingContent()
    {
        // Not throwing is not enough: the rows that DO have content still have to survive.
        var terminal = new Terminal(new TerminalOptions { Cols = 6, Rows = 2, Scrollback = 1 });

        terminal.Write(new string(' ', 12));
        terminal.Write("\r\nx");
        terminal.Write("\r\ny");

        terminal.Resize(4, 2);

        var text = new List<string>();
        for (var i = 0; i < terminal.Buffer.Lines.Length; i++)
        {
            text.Add(terminal.Buffer.Lines[i]!.TranslateToString(trimRight: true));
        }

        Assert.Contains("x", text);
        Assert.Contains("y", text);
    }

    [Fact]
    public void Shrink_WithBlankWrappedRowAtTop_DoesNotThrow_ConstructedDirectly()
    {
        // The same shape built by hand, so the regression stays pinned even if the terminal-level
        // route above stops producing this layout.
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.Resize(6, 10);
        buffer.SetCursorRaw(0, 5);
        buffer.Lines[0]!.IsWrapped = true;

        Assert.Null(Record.Exception(() => buffer.Resize(4, 10)));
    }
}
