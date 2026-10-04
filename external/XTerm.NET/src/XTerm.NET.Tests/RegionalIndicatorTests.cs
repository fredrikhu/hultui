using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// Two regional indicator symbols form one flag, and they arrive in separate <c>Print</c> calls.
///
/// <para>They used to be measured as width 0 — the pairing test lived in <c>GetStringCellWidth</c>, which is
/// called once per printed character, so the count was always 1, always odd, and the answer was always
/// zero. Width 0 leaves the cursor standing still, so the next character overwrote the indicator: a flag did
/// not render oddly, it vanished from the buffer entirely and took the column alignment of the rest of the
/// line with it.</para>
/// </summary>
public class RegionalIndicatorTests
{
    private const string RegionalU = "\U0001F1FA";
    private const string RegionalS = "\U0001F1F8";
    private const string RegionalG = "\U0001F1EC";
    private const string RegionalB = "\U0001F1E7";
    private const string FlagUs = RegionalU + RegionalS;

    private static Terminal Write(string text, int cols = 20)
    {
        var terminal = new Terminal(new TerminalOptions { Cols = cols, Rows = 5 });
        terminal.Write(text);
        return terminal;
    }

    /// <summary>The reported bug: a flag and everything after it on the line.</summary>
    [Fact]
    public void A_flag_is_one_double_width_cell_and_does_not_eat_what_follows()
    {
        var terminal = Write(FlagUs + "XXX");
        var line = terminal.Buffer.Lines[0]!;

        Assert.Equal(FlagUs, line[0].Content);
        Assert.Equal(2, line[0].Width);
        Assert.Equal(0, line[1].Width);

        // The whole point. These used to land at 0, 1, 2, on top of the flag.
        Assert.Equal("X", line[2].Content);
        Assert.Equal("X", line[3].Content);
        Assert.Equal("X", line[4].Content);
        Assert.Equal(5, terminal.Buffer.X);
    }

    /// <summary>
    /// A lone indicator is a valid character in its own right, and it carries emoji presentation — so it
    /// occupies TWO columns, the same as the flag a pair would make, rather than being erased by whatever
    /// comes next.
    /// </summary>
    /// <remarks>
    /// One column was the wrong answer and it showed: ucs-detect's standalone-indicator test expects two,
    /// and scored zero against a terminal that said one.
    /// </remarks>
    [Fact]
    public void A_lone_indicator_is_two_columns_wide()
    {
        var terminal = Write(RegionalU + "X");
        var line = terminal.Buffer.Lines[0]!;

        Assert.Equal(RegionalU, line[0].Content);
        Assert.Equal(2, line[0].Width);
        Assert.Equal(0, line[1].Width);
        Assert.Equal("X", line[2].Content);
        Assert.Equal(3, terminal.Buffer.X);
    }

    /// <summary>
    /// Indicators pair from the left and do not accumulate, so a third starts a new pair rather than
    /// joining the flag beside it.
    /// </summary>
    [Fact]
    public void A_third_indicator_starts_a_new_pair()
    {
        var terminal = Write(RegionalU + RegionalS + RegionalG);
        var line = terminal.Buffer.Lines[0]!;

        Assert.Equal(FlagUs, line[0].Content);
        Assert.Equal(2, line[0].Width);
        Assert.Equal(RegionalG, line[2].Content);
        Assert.Equal(2, line[2].Width);
        Assert.Equal(4, terminal.Buffer.X);
    }

    /// <summary>And a fourth completes that second pair.</summary>
    [Fact]
    public void Four_indicators_are_two_flags()
    {
        var terminal = Write(RegionalG + RegionalB + RegionalU + RegionalS);
        var line = terminal.Buffer.Lines[0]!;

        Assert.Equal(RegionalG + RegionalB, line[0].Content);
        Assert.Equal(2, line[0].Width);
        Assert.Equal(FlagUs, line[2].Content);
        Assert.Equal(2, line[2].Width);
        Assert.Equal(4, terminal.Buffer.X);
    }

    /// <summary>
    /// The halves arriving in separate writes is the normal case, not the exotic one: a pty hands over
    /// whatever the read returned, so the boundary falls mid-flag whenever it happens to.
    /// </summary>
    [Fact]
    public void The_halves_pair_across_separate_writes()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 5 });
        terminal.Write(RegionalU);
        terminal.Write(RegionalS);

        var line = terminal.Buffer.Lines[0]!;
        Assert.Equal(FlagUs, line[0].Content);
        Assert.Equal(2, line[0].Width);
        Assert.Equal(2, terminal.Buffer.X);
    }

    /// <summary>
    /// Anything that moves the cursor between them leaves two separate characters. They are only a flag
    /// because they were adjacent, and a cursor address means they are not.
    /// </summary>
    [Fact]
    public void Indicators_separated_by_a_cursor_move_do_not_pair()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 5 });
        terminal.Write(RegionalU);
        terminal.Write("[1;10H");     // somewhere else entirely
        terminal.Write(RegionalS);

        var line = terminal.Buffer.Lines[0]!;
        Assert.Equal(RegionalU, line[0].Content);
        Assert.Equal(2, line[0].Width);
        Assert.Equal(RegionalS, line[9].Content);
        Assert.Equal(2, line[9].Width);
    }

    /// <summary>Nor across a newline, for the same reason.</summary>
    [Fact]
    public void Indicators_separated_by_a_newline_do_not_pair()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 20, Rows = 5 });
        terminal.Write(RegionalU);
        terminal.Write("\r\n");
        terminal.Write(RegionalS);

        Assert.Equal(RegionalU, terminal.Buffer.Lines[0]![0].Content);
        Assert.Equal(RegionalS, terminal.Buffer.Lines[1]![0].Content);
    }

}
