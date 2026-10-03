using XTerm;
using XTerm.Buffer;
using XTerm.Options;

namespace XTerm.Tests.Buffer;

/// <summary>
/// Resize and reflow edge cases: one-column terminals, buffers at capacity, and zero-row buffers.
/// </summary>
/// <remarks>
/// Every one of these failed before the fix that accompanies it, and each failed LOUDLY -- two
/// IndexOutOfRangeException, one OutOfMemoryException after a hang, one out-of-bounds cursor, one
/// viewport pointing at rows the user cannot see, and one buffer that could never be written to.
/// None is exotic: they are what a one-column pane, a full scrollback, or a shrinking window
/// produce on their own.
/// </remarks>
public class ResizeEdgeCaseTests
{
    private static void SetCell(BufferLine line, int col, string content, int width = 1)
    {
        var cell = new BufferCell(content, width, AttributeData.Default);
        line.SetCell(col, ref cell);
    }

    private static void SetWideCell(BufferLine line, int col, string content)
    {
        SetCell(line, col, content, 2);
        SetCell(line, col + 1, "", 0);
    }

    // One-column reflow with a wide boundary
    [Fact]
    public void ShrinkToOneColumn_WithWideChars_Terminates()
    {
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.Resize(6, 10);
        buffer.SetCursorRaw(0, 5);

        for (var col = 0; col < 6; col += 2)
        {
            SetWideCell(buffer.Lines[0]!, col, "漢");
            SetWideCell(buffer.Lines[1]!, col, "漢");
        }

        buffer.Lines[1]!.IsWrapped = true;

        // Hung, then threw OutOfMemoryException: a zero-length row made no progress, so the loop
        // appended rows until the list could not grow.
        buffer.Resize(1, 10);
    }

    // The viewport adjustment pops rows the outer loop is still walking
    [Fact]
    public void ShrinkWithFullLastRow_AndCursorAtTop_DoesNotThrow()
    {
        // Constructed at 10x10 directly. Resizing an 80x24 buffer down leaves 24 LINES, not 10, so
        // the last row is not row 9 and the Pop path never runs -- which is why the first attempt at
        // this probe passed while proving nothing.
        var buffer = new TerminalBuffer(10, 10, 1000);
        buffer.SetCursorRaw(0, 0);

        for (var col = 0; col < 10; col++)
        {
            SetCell(buffer.Lines[9]!, col, "x");
        }

        var ex = Record.Exception(() => buffer.Resize(2, 10));
        Assert.Null(ex);
    }

    // A negative cursor must not survive a resize
    [Fact]
    public void NegativeCursor_IsNotPreservedAcrossResize()
    {
        var buffer = new TerminalBuffer(80, 24, 1000);
        buffer.Resize(20, 10);

        buffer.SetCursorRaw(-5, -5);
        buffer.Resize(10, 5);

        Assert.True(buffer.X >= 0, $"X was {buffer.X}");
        Assert.True(buffer.Y >= 0, $"Y was {buffer.Y}");
    }

    // A line expanding past the remaining capacity
    [Fact]
    public void ExpansionBeyondCapacity_DoesNotThrow()
    {
        var buffer = new TerminalBuffer(80, 2, 1);
        buffer.SetCursorRaw(0, 0);

        for (var col = 0; col < 80; col++)
        {
            SetCell(buffer.Lines[1]!, col, "x");
        }

        var ex = Record.Exception(() => buffer.Resize(2, 2));
        Assert.Null(ex);
    }

    // The viewport after a capacity trim
    [Fact]
    public void ViewportFollowsTheBottom_AfterCapacityTrim()
    {
        // Fill past capacity so the buffer is at MaxLength and following the bottom, then shrink the
        // row count enough that capacity has to trim.
        var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 5, Scrollback = 5 });
        for (var i = 0; i < 12; i++)
        {
            terminal.Write($"line{i}\r\n");
        }

        var before = terminal.Buffer;
        Assert.Equal(before.YBase, before.YDisp);

        terminal.Resize(20, 3);

        var after = terminal.Buffer;
        Assert.Equal(
            after.Lines.Length - 3,
            after.YBase);
    }

    // A zero-row buffer brought to life by a later resize
    [Fact]
    public void ZeroRowBuffer_IsUsableAfterResize()
    {
        var buffer = new TerminalBuffer(80, 0, 1000);

        buffer.Resize(80, 24);

        Assert.True(buffer.Lines.Length > 0, $"Lines.Length was {buffer.Lines.Length}");
    }

    /// <summary>
    /// A shrink that was following the tail keeps following it, with nothing stranded below.
    /// </summary>
    /// <remarks>
    /// The screen is the last `rows` lines of the buffer, so a shrink has to move the difference
    /// into scrollback. Shifting only far enough to bring the cursor back on screen left lines below
    /// the screen — and the viewport tops out at YBase, so scrolling could never reach them again.
    /// Caught in review on this PR.
    /// </remarks>
    [Fact]
    public void ShrinkingRows_LeavesNothingStrandedBelowTheScreen()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        for (var i = 0; i < 20; i++)
            terminal.Write($"line {i}\r\n");
        terminal.Write("prompt$ ");

        // Park the cursor with blank rows below it, which is what leaves room to strand.
        terminal.Write("\u001b[24;1H");
        terminal.Write("\u001b[6A");

        var contentRow = terminal.Buffer.YBase + terminal.Buffer.Y;

        terminal.Resize(40, 10);

        // The cursor is still on its line...
        Assert.Equal(contentRow, terminal.Buffer.YBase + terminal.Buffer.Y);

        // ...and the screen reaches the end of the buffer, so nothing is below it.
        Assert.Equal(terminal.Buffer.Lines.Length, terminal.Buffer.YBase + terminal.Rows);

        // A viewport that was at the tail is still at the tail.
        Assert.Equal(terminal.Buffer.YBase, terminal.Buffer.ViewportY);
    }

    /// <summary>
    /// A zero-row resize has no viewport to overflow out of, and must not scroll the buffer.
    /// </summary>
    /// <remarks>
    /// The bottom row of a zero-row viewport is not -1, and treating it as such makes the overflow
    /// one line too large -- so a resize that shows nothing still moved the cursor's content row,
    /// and the line that came back at the top when rows were restored was the wrong one. Zero rows
    /// is a real case here: a buffer can be built with none and brought to life by a later resize.
    /// </remarks>
    [Fact]
    public void ZeroRowResize_DoesNotScrollTheBuffer()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        for (var i = 0; i < 20; i++)
            terminal.Write($"line {i}\r\n");
        terminal.Write("prompt$ ");

        var contentRow = terminal.Buffer.YBase + terminal.Buffer.Y;

        terminal.Resize(40, 0);
        Assert.Equal(contentRow, terminal.Buffer.YBase + terminal.Buffer.Y);

        terminal.Resize(40, 24);
        Assert.Equal(contentRow, terminal.Buffer.YBase + terminal.Buffer.Y);
    }

    /// <summary>
    /// A resize must not move the cursor off the line it is on. Its position is YBase + Y, and both
    /// halves of a resize used to change one without the other.
    /// </summary>
    /// <remarks>
    /// <para>The consequence is silent corruption rather than a crash, which is why it survived: the
    /// cursor lands on earlier content and the next write destroys a line the application never
    /// touched. A shell hides its own damage, because it redraws its prompt on every SIGWINCH and
    /// repaints what it just overwrote. Anything that does NOT repaint -- a Sixel picture, a
    /// full-screen TUI mid-frame -- keeps the evidence.</para>
    /// <para>Both directions are tested, because they fail through different mechanisms and fixing
    /// one leaves the other.</para>
    /// </remarks>
    [Fact]
    public void ShrinkingRows_KeepsTheCursorOnItsLine()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        for (var i = 0; i < 20; i++)
            terminal.Write($"line {i}\r\n");
        terminal.Write("prompt$ ");

        var contentRow = terminal.Buffer.YBase + terminal.Buffer.Y;

        terminal.Resize(40, 8);

        Assert.Equal(contentRow, terminal.Buffer.YBase + terminal.Buffer.Y);
    }

    [Fact]
    public void GrowingRows_KeepsTheCursorOnItsLine()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        for (var i = 0; i < 20; i++)
            terminal.Write($"line {i}\r\n");
        terminal.Write("prompt$ ");

        terminal.Resize(40, 8);
        var contentRow = terminal.Buffer.YBase + terminal.Buffer.Y;

        terminal.Resize(40, 24);

        Assert.Equal(contentRow, terminal.Buffer.YBase + terminal.Buffer.Y);
    }

    /// <summary>
    /// The live case: a drag is many resize events, and a shell writes between them. What the cursor
    /// slides over is what gets destroyed, so the round trip is asserted on CONTENT and not only on
    /// coordinates.
    /// </summary>
    [Fact]
    public void ResizeLadderWithRedraws_LeavesEarlierLinesIntact()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 24, Scrollback = 200 });
        for (var i = 0; i < 20; i++)
            terminal.Write($"line {i}\r\n");
        terminal.Write("prompt$ ");

        for (var rows = 20; rows >= 6; rows -= 4)
        {
            terminal.Resize(40, rows);
            terminal.Write("\rprompt$ ");
        }

        for (var rows = 10; rows <= 24; rows += 4)
        {
            terminal.Resize(40, rows);
            terminal.Write("\rprompt$ ");
        }

        // Every "line N" written before the drag must still read back exactly.
        for (var i = 0; i < 20; i++)
        {
            var line = terminal.Buffer.Lines[i];
            Assert.NotNull(line);
            Assert.Equal($"line {i}", line!.TranslateToString(true).TrimEnd());
        }
    }
}
