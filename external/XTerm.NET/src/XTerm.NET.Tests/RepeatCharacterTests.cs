using XTerm.Options;
using Xunit;

namespace XTerm.Tests;

/// <summary>
/// REP (<c>CSI Pn b</c>) — repeat the preceding graphic character.
///
/// <para>"Preceding" is meant literally, and that is most of the behaviour: it repeats the last
/// character printed, and only while the cursor is still where printing it left the cursor.</para>
/// </summary>
public class RepeatCharacterTests
{
    private const string Esc = "\u001b";

    private static Terminal Fresh(int cols = 20, int rows = 5)
        => new(new TerminalOptions { Cols = cols, Rows = rows });

    private static string Row(Terminal terminal, int row = 0)
    {
        var line = terminal.Buffer.Lines[terminal.Buffer.YBase + row]!;
        var text = string.Concat(Enumerable.Range(0, terminal.Cols).Select(c => line[c].Content));
        return text.TrimEnd('\0', ' ');
    }

    [Fact]
    public void It_repeats_the_character_before_it()
    {
        var terminal = Fresh();
        terminal.Write($"a{Esc}[4b");

        Assert.Equal("aaaaa", Row(terminal));
    }

    /// <summary>No parameter means once, as every CSI with an omitted count does.</summary>
    [Fact]
    public void An_omitted_count_repeats_once()
    {
        var terminal = Fresh();
        terminal.Write($"x{Esc}[b");

        Assert.Equal("xx", Row(terminal));
    }

    /// <summary>It repeats only the last character, not the run before it.</summary>
    [Fact]
    public void Only_the_last_character_repeats()
    {
        var terminal = Fresh();
        terminal.Write($"ab{Esc}[3b");

        Assert.Equal("abbbb", Row(terminal));
    }

    /// <summary>
    /// A cursor move in between means there is no preceding character, so this does nothing rather
    /// than repeating whatever happens to be nearby.
    /// </summary>
    [Fact]
    public void A_cursor_move_in_between_cancels_it()
    {
        var terminal = Fresh();
        terminal.Write($"abc{Esc}[1;1H{Esc}[5b");

        Assert.Equal("abc", Row(terminal));
    }

    [Fact]
    public void With_nothing_printed_yet_it_does_nothing()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[5b");

        Assert.Equal("", Row(terminal));
    }

    /// <summary>A newline moves the cursor, so it cancels it too.</summary>
    [Fact]
    public void A_newline_cancels_it()
    {
        var terminal = Fresh();
        terminal.Write($"a\r\n{Esc}[3b");

        Assert.Equal("a", Row(terminal, 0));
        Assert.Equal("", Row(terminal, 1));
    }

    /// <summary>The repeated character carries the attributes in force, as printing it again would.</summary>
    [Fact]
    public void The_repeat_takes_the_current_attributes()
    {
        var terminal = Fresh();
        terminal.Write($"{Esc}[31mr{Esc}[2b");

        var line = terminal.Buffer.Lines[terminal.Buffer.YBase]!;
        Assert.Equal("rrr", Row(terminal));
        for (var c = 0; c < 3; c++)
            Assert.Equal(line[0].Attributes, line[c].Attributes);
    }

    /// <summary>It wraps and scrolls like ordinary printing, because it goes through the same path.</summary>
    [Fact]
    public void It_wraps_at_the_edge()
    {
        var terminal = Fresh(cols: 5, rows: 3);
        terminal.Write($"z{Esc}[6b");

        Assert.Equal("zzzzz", Row(terminal, 0));
        Assert.Equal("zz", Row(terminal, 1));
    }

    /// <summary>
    /// A count from a hosted program is untrusted, so it is clamped to a screenful — past which
    /// every extra repeat only scrolls the earlier ones away and the screen looks identical.
    /// </summary>
    /// <remarks>
    /// The assertion is the clamp, stated exactly: 10 x 4 gives 40 repeats, which with the original
    /// character is 41 cells. That is four full rows and one over, so the screen scrolls once and
    /// the last row holds the single leftover. Reaching this assertion at all is the real subject —
    /// unclamped, the write never returns.
    /// </remarks>
    [Fact]
    public void An_enormous_count_is_clamped_to_a_screenful()
    {
        var terminal = Fresh(cols: 10, rows: 4);
        terminal.Write($"q{Esc}[2000000000b");

        Assert.Equal("qqqqqqqqqq", Row(terminal, 0));
        Assert.Equal("qqqqqqqqqq", Row(terminal, 2));
        Assert.Equal("q", Row(terminal, 3));
    }

    /// <summary>
    /// A multi-codepoint cluster repeats whole. It is stored as an interned id rather than in the
    /// cell, so this is the case that would come back empty if REP read the wrong field.
    /// </summary>
    [Fact]
    public void A_combining_cluster_repeats_whole()
    {
        var terminal = Fresh();
        terminal.Write($"e\u0301{Esc}[2b");

        Assert.Equal("e\u0301e\u0301e\u0301", Row(terminal));
    }

    /// <summary>
    /// The batched writer bypasses Print, so it keeps REP's record itself. Without that, the same
    /// input would repeat or not depending on which writer took it.
    /// </summary>
    [Fact]
    public void It_works_after_a_batched_run_and_agrees_with_the_slow_path()
    {
        var batched = Fresh();
        batched.Write($"hello{Esc}[3b");

        var perCharacter = Fresh();
        perCharacter.UseRunPrinting = false;
        perCharacter.Write($"hello{Esc}[3b");

        Assert.Equal("helloooo", Row(batched));
        Assert.Equal(Row(perCharacter), Row(batched));
    }

    /// <summary>And the same through the byte entry, which is a third writer again.</summary>
    /// <summary>
    /// A cursor moved away and BACK still cancels it. Position equality alone cannot see the
    /// excursion — this is what the dispatch-point clearing exists for, and it is how xterm.js
    /// behaves: any sequence between the print and the REP means there is no preceding
    /// character, whether or not the cursor ended up where it started.
    /// </summary>
    [Fact]
    public void A_cursor_move_that_returns_still_cancels_it()
    {
        var terminal = Fresh();
        terminal.Write($"a{Esc}[D{Esc}[C{Esc}[5b");

        Assert.Equal("a", Row(terminal));
    }

    /// <summary>Any sequence cancels it, not just movement — SGR leaves the cursor alone and still counts.</summary>
    [Fact]
    public void An_SGR_in_between_cancels_it()
    {
        var terminal = Fresh();
        terminal.Write($"a{Esc}[31m{Esc}[5b");

        Assert.Equal("a", Row(terminal));
    }

    /// <summary>An OSC in between cancels it too — the rule is any sequence, not any CSI.</summary>
    [Fact]
    public void An_OSC_in_between_cancels_it()
    {
        var terminal = Fresh();
        terminal.Write($"a{Esc}]0;title{Esc}\\{Esc}[5b");

        Assert.Equal("a", Row(terminal));
    }

    /// <summary>
    /// A chain of REPs keeps repeating: after a REP, the character it printed IS the preceding
    /// graphic character. This is ECMA-48's reading and the one deliberate divergence from
    /// xterm.js, whose parser happens to clear its record after every handler including REP's.
    /// </summary>
    [Fact]
    public void A_second_REP_repeats_again()
    {
        var terminal = Fresh();
        terminal.Write($"a{Esc}[2b{Esc}[2b");

        Assert.Equal("aaaaa", Row(terminal));
    }

    /// <summary>
    /// An emoji-presentation selector widens the previous cell and moves the cursor with it —
    /// and the record has to be taken AFTER that adjustment. Taken before, the saved position
    /// went stale and REP right after the selector silently did nothing.
    /// </summary>
    [Fact]
    public void It_repeats_a_cluster_widened_by_a_variation_selector()
    {
        var terminal = Fresh();
        terminal.Write("❤️");          // heart + VS16: width 1 becomes width 2
        terminal.Write($"{Esc}[2b");

        var line = terminal.Buffer.Lines[terminal.Buffer.YBase]!;
        Assert.Equal("❤️", line[0].Content);
        Assert.Equal("❤️", line[2].Content);
        Assert.Equal("❤️", line[4].Content);
        Assert.Equal(2, line[0].Width);
        Assert.Equal(6, terminal.Buffer.X);
    }

    [Fact]
    public void It_works_after_a_byte_run()
    {
        var terminal = Fresh();
        terminal.Write(System.Text.Encoding.UTF8.GetBytes($"hi{Esc}[3b"));

        Assert.Equal("hiiii", Row(terminal));
    }
}
