using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// Cases taken from vttest 2.7 (20230201), run against this emulator through a pty.
///
/// <para>Every one of these is a case vttest JUDGES ITSELF -- it prints the reply it received and
/// says what it expected -- which is what makes them portable into tests at all. The visual tests
/// in vttest carry no machine-checkable oracle: their expectation is a sentence telling a human
/// what the screen should look like, and porting those means writing the expected buffer yourself,
/// which asserts today's behaviour rather than correctness.</para>
///
/// <para>The menu path each case came from is on the test, so a failure can be reproduced by hand:
/// run vttest in the terminal and walk that path.</para>
///
/// <para>vttest is Copyright 1996-2022 by Thomas E. Dickey, under an X11-style licence -- use,
/// copy, modify and distribute permitted with the copyright notice retained. Nothing of its source
/// is copied here; these are its cases restated as sequences and expected replies.</para>
/// </summary>
public class VtTestConformanceTests
{
    private const string Esc = "\u001b";

    private static (Terminal Terminal, List<string> Replies) Listening()
    {
        var terminal = new Terminal(new TerminalOptions { Cols = 80, Rows = 24 });

        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);
        return (terminal, replies);
    }

    /// <summary>
    /// DECCOLM clears the screen in both directions. vttest menu 1 (132-column section) and menu 9
    /// bugs C and D.
    /// </summary>
    /// <remarks>
    /// Passes today, and is here because it nearly went the other way: driving vttest without
    /// propagating the emulator's resize to the pty leaves the application drawing at 80 columns
    /// into a 132-column grid, which looks exactly like a clearing bug. This is the same question
    /// asked with nothing in between.
    /// </remarks>
    [Fact]
    public void Switching_column_mode_clears_the_screen()
    {
        var (terminal, _) = Listening();

        terminal.Write($"{Esc}[?40h");                  // Allow80To132, the gate DECCOLM swings on
        terminal.Write("HELLO AT THE TOP\r\nSECOND LINE\r\n");

        terminal.Write($"{Esc}[?3h");                   // -> 132
        Assert.Equal(132, terminal.Cols);
        Assert.All(Enumerable.Range(0, 24), row => Assert.Equal(string.Empty, terminal.GetLine(row)));

        terminal.Write(new string('X', 132));
        Assert.Equal(132, terminal.GetLine(0).Length);

        terminal.Write($"{Esc}[?3l");                   // -> 80
        Assert.Equal(80, terminal.Cols);
        Assert.All(Enumerable.Range(0, 24), row => Assert.Equal(string.Empty, terminal.GetLine(row)));
    }

    /// <summary>
    /// Tertiary device attributes. vttest menu 6 -> 6, which prints the heading and nothing else.
    /// </summary>
    /// <remarks>
    /// xterm answers DA3 with a DECRPSS-framed unit id, conventionally all zeroes when there is no
    /// real one to report. See tomlm/XTerm.NET#123.
    /// </remarks>
    [Fact(Skip = "Unfixed: DA3 has no entry in the CSI identifier table. tomlm/XTerm.NET#123")]
    public void Tertiary_device_attributes_are_answered()
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[=c");

        var reply = Assert.Single(replies);
        Assert.StartsWith($"{Esc}P!|", reply, StringComparison.Ordinal);
        Assert.EndsWith($"{Esc}\\", reply, StringComparison.Ordinal);
    }

    /// <summary>
    /// DECREQTPARM. vttest menu 6 -> 7, which paints nothing at all.
    /// </summary>
    /// <remarks>
    /// CSI 0 x reports with sol=2, CSI 1 x with sol=3. See tomlm/XTerm.NET#124.
    /// </remarks>
    [Fact]
    public void Request_terminal_parameters_is_answered()
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[0x");

        var reply = Assert.Single(replies);
        Assert.StartsWith($"{Esc}[2;", reply, StringComparison.Ordinal);
        Assert.EndsWith("x", reply, StringComparison.Ordinal);
    }

    /// <summary>
    /// DECRQM answers for a mode it does not recognise. vttest menu 11 -> 8 -> 2 -> 2, where modes
    /// 2 and 10-13 come back as "failed" -- vttest's way of saying no reply arrived.
    /// </summary>
    /// <remarks>
    /// Ps=0 is the defined "mode not recognized" value. Silence is not an answer: a client that
    /// blocks on the report hangs. See tomlm/XTerm.NET#125.
    /// </remarks>
    [Fact]
    public void Request_mode_answers_even_for_an_unrecognised_mode()
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[?2$p");                  // DECANM, which the mode table does not carry

        Assert.Equal($"{Esc}[?2;0$y", Assert.Single(replies));
    }

    /// <summary>
    /// A DECRQM mode the table DOES carry still answers, so the test above is about the missing
    /// reply and not about DECRQM generally.
    /// </summary>
    [Fact]
    public void Request_mode_answers_for_a_known_mode()
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc}[?7$p");                  // DECAWM, set by default

        Assert.Equal($"{Esc}[?7;1$y", Assert.Single(replies));
    }

    /// <summary>
    /// Window size in pixels, with the report enabled and no host handler to answer it. vttest
    /// menu 11 -> 8 -> 9, where five of six reports answer and this one does not.
    /// </summary>
    /// <remarks>
    /// The neighbouring reports already decided this: CSI 18 t answers from the emulator's own
    /// rows and columns, and CSI 13 t falls back to the position winop 3 last set. See
    /// tomlm/XTerm.NET#126.
    /// </remarks>
    [Fact]
    public void Window_size_in_pixels_is_answered_without_a_host_handler()
    {
        var (terminal, replies) = Listening();
        terminal.Options.WindowOptions.GetWinSizePixels = true;

        terminal.Write($"{Esc}[14t");

        var reply = Assert.Single(replies);
        Assert.StartsWith($"{Esc}[4;", reply, StringComparison.Ordinal);
        Assert.EndsWith("t", reply, StringComparison.Ordinal);
    }

    /// <summary>
    /// The report next to it, which does answer -- the contrast the test above rests on.
    /// </summary>
    [Fact]
    public void Text_area_size_in_characters_is_answered_without_a_host_handler()
    {
        var (terminal, replies) = Listening();
        terminal.Options.WindowOptions.GetWinSizeChars = true;

        terminal.Write($"{Esc}[18t");

        Assert.Equal($"{Esc}[8;24;80t", Assert.Single(replies));
    }

    /// <summary>
    /// Erasing the display puts double-width/height lines back to normal. vttest menu 4, reported
    /// from the Avalonia terminal as "stuck in double-size mode".
    /// </summary>
    /// <remarks>
    /// vttest erases the display between screens, so a line attribute that survives an erase
    /// survives the rest of the session. See tomlm/XTerm.NET#129.
    /// </remarks>
    [Fact]
    public void Erasing_the_display_clears_line_attributes()
    {
        var (terminal, _) = Listening();

        terminal.Write($"{Esc}[1;1H{Esc}#6WIDE");
        Assert.Equal(XTerm.Buffer.LineAttribute.DoubleWidth, terminal.Buffer.Lines[0]!.LineAttribute);

        terminal.Write($"{Esc}[2J");

        Assert.Equal(XTerm.Buffer.LineAttribute.Normal, terminal.Buffer.Lines[0]!.LineAttribute);
    }

    /// <summary>
    /// The per-line escape still works, so the test above is about the erase and not about DECSWL.
    /// </summary>
    [Fact]
    public void DECSWL_puts_a_double_width_line_back()
    {
        var (terminal, _) = Listening();

        terminal.Write($"{Esc}[1;1H{Esc}#6WIDE");
        terminal.Write($"{Esc}[1;1H{Esc}#5");

        Assert.Equal(XTerm.Buffer.LineAttribute.Normal, terminal.Buffer.Lines[0]!.LineAttribute);
    }

    /// <summary>
    /// After S8C1T the terminal's own replies use the 8-bit CSI. vttest menu 11 -> 1 -> 3, where
    /// both halves of the test currently return the same 7-bit reply.
    /// </summary>
    /// <remarks>See tomlm/XTerm.NET#130.</remarks>
    [Fact]
    public void Eight_bit_controls_change_the_reply_prefix()
    {
        var (terminal, replies) = Listening();

        terminal.Write($"{Esc} G");                     // S8C1T
        terminal.Write($"{Esc}[6n");                    // DSR - cursor position

        Assert.StartsWith("", Assert.Single(replies), StringComparison.Ordinal);
    }

    /// <summary>
    /// G2 designated as DEC Special Graphics and invoked with a locking shift maps like G0 does.
    /// </summary>
    /// <remarks>
    /// vttest's single-shift test passes without this working, because it runs with G2 and G3 as
    /// ISO Latin-1 whose mapping is the identity. See tomlm/XTerm.NET#131.
    /// </remarks>
    [Fact]
    public void A_locking_shift_reaches_G2()
    {
        var (terminal, _) = Listening();

        terminal.Write($"{Esc}*0");                     // G2 = DEC Special Graphics
        terminal.Write($"{Esc}n");                      // LS2
        terminal.Write("aaa");

        Assert.Equal("▒▒▒", terminal.GetLine(0));
    }

    /// <summary>
    /// The same character set through G0, which does work -- the contrast the test above rests on.
    /// </summary>
    [Fact]
    public void G0_reaches_the_special_graphics_set()
    {
        var (terminal, _) = Listening();

        terminal.Write($"{Esc}(0aaa{Esc}(B");

        Assert.Equal("▒▒▒", terminal.GetLine(0));
    }

    /// <summary>
    /// A national replacement set remaps the positions it is defined to remap. French is the case
    /// vttest exercises once NRC mode is enabled.
    /// </summary>
    /// <remarks>
    /// The primary DA advertises feature 9 while only UK is implemented. See tomlm/XTerm.NET#132.
    /// </remarks>
    [Fact]
    public void A_national_replacement_set_remaps_its_positions()
    {
        var (terminal, _) = Listening();

        terminal.Write($"{Esc}[?42h");                  // DECNRCM
        terminal.Write($"{Esc}(R#{Esc}(B");             // French G0, then the position it remaps

        Assert.Equal("£", terminal.GetLine(0));    // pound sign
    }

    /// <summary>
    /// UK is implemented, and remaps the one position it defines -- the contrast for the test above.
    /// </summary>
    [Fact]
    public void The_UK_set_remaps_its_one_position()
    {
        var (terminal, _) = Listening();

        terminal.Write($"{Esc}(A#{Esc}(B");

        Assert.Equal("£", terminal.GetLine(0));
    }
}
