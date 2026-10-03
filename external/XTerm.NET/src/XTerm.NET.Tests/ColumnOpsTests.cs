using XTerm.Options;

namespace XTerm.Tests;

/// <summary>DECIC, DECDC, DECBI, DECFI -- columns sliding sideways within the scrolling region.</summary>
public class ColumnOpsTests
{
    private const string Esc = "\u001b";

    private static Terminal NewTerminal(int cols = 10, int rows = 5) =>
        new(new TerminalOptions { Cols = cols, Rows = rows });

    private static string Row(Terminal t, int row, int count)
    {
        var line = t.Buffer.Lines[row]!;
        return string.Concat(Enumerable.Range(0, count)
            .Select(i => string.IsNullOrEmpty(line[i].Content) ? " " : line[i].Content));
    }

    [Fact]
    public void Insert_opens_blanks_at_the_cursor_on_every_region_row()
    {
        var terminal = NewTerminal();
        terminal.Write($"abcdef\r\nABCDEF{Esc}[1;2H{Esc}[2'}}");

        Assert.Equal("a  bcde", Row(terminal, 0, 7));
        Assert.Equal("A  BCDE", Row(terminal, 1, 7));
    }

    [Fact]
    public void Delete_pulls_the_region_leftward_and_blanks_the_tail()
    {
        var terminal = NewTerminal(cols: 6);
        terminal.Write($"abcdef{Esc}[1;2H{Esc}[2'~");

        Assert.Equal("adef  ", Row(terminal, 0, 6));
    }

    [Fact]
    public void Column_ops_do_nothing_for_a_cursor_outside_the_region()
    {
        var terminal = NewTerminal();
        terminal.Write($"abcdef{Esc}[?69h{Esc}[3;5s{Esc}[1;1H{Esc}[1'}}");

        Assert.Equal("abcdef", Row(terminal, 0, 6));
    }

    [Fact]
    public void Back_index_at_the_left_margin_slides_the_region_right()
    {
        var terminal = NewTerminal(cols: 6);
        terminal.Write($"abcdef{Esc}[1;1H{Esc}6");

        Assert.Equal(" abcde", Row(terminal, 0, 6));
        Assert.Equal(0, terminal.Buffer.X);           // the cursor stays put
    }

    [Fact]
    public void Forward_index_at_the_right_margin_slides_the_region_left()
    {
        var terminal = NewTerminal(cols: 6);
        terminal.Write($"abcdef{Esc}[1;6H{Esc}9");

        Assert.Equal("bcdef ", Row(terminal, 0, 6));
    }

    [Fact]
    public void Forward_index_outside_the_margins_just_moves()
    {
        // DEC STD 070: outside the pane, DECFI is a motion, and may pass the pane it was never in.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[?69h{Esc}[3;5s{Esc}[1;6H{Esc}9");

        Assert.Equal(6, terminal.Buffer.X);
    }
}
