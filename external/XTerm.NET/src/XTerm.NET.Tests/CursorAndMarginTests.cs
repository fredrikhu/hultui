using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// Cursor motion, margins and tab stops against what xterm does. Each test names the program
/// behavior that goes wrong when the terminal disagrees.
/// </summary>
public class CursorAndMarginTests
{
    private static readonly string Esc = ((char)0x1B).ToString();

    private static Terminal NewTerminal(int cols = 20, int rows = 10) =>
        new(new TerminalOptions { Cols = cols, Rows = rows });

    private static string Row(Terminal t, int row, int count)
    {
        var line = t.Buffer.Lines[row]!;
        return string.Concat(Enumerable.Range(0, count)
            .Select(i => string.IsNullOrEmpty(line[i].Content) ? " " : line[i].Content));
    }

    [Fact]
    public void Cursor_up_stops_at_the_top_margin_when_it_starts_inside()
    {
        // A full-screen editor keeps its status line outside the region; a cursor walking out of
        // the region scrolls the wrong rows.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[3;8r");     // region rows 3..8 (0-based 2..7)
        terminal.Write($"{Esc}[5;1H");     // inside it
        terminal.Write($"{Esc}[10A");      // further up than the region is tall

        Assert.Equal(2, terminal.Buffer.Y);
    }

    [Fact]
    public void Cursor_down_stops_at_the_bottom_margin_when_it_starts_inside()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[3;8r");
        terminal.Write($"{Esc}[5;1H");
        terminal.Write($"{Esc}[10B");

        Assert.Equal(7, terminal.Buffer.Y);
    }

    [Fact]
    public void Cursor_up_from_outside_the_region_uses_the_screen_edge()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[3;8r");
        terminal.Write($"{Esc}[10;1H");    // below the region
        terminal.Write($"{Esc}[20A");

        Assert.Equal(0, terminal.Buffer.Y);
    }

    [Fact]
    public void Backspace_from_a_full_line_lands_on_the_last_column()
    {
        // Printing to the end leaves the cursor one PAST the last column. Counting back from that
        // phantom position put a shell's redraw one column right of where it meant.
        var terminal = NewTerminal(cols: 10);
        terminal.Write("0123456789");      // fills the line, pending wrap
        terminal.Write($"{Esc}[1D");       // CUB 1

        Assert.Equal(8, terminal.Buffer.X);
    }

    [Fact]
    public void With_wrapping_off_the_last_column_is_overwritten_not_dropped()
    {
        var terminal = NewTerminal(cols: 10);
        terminal.Write($"{Esc}[?7l");      // DECAWM off
        terminal.Write("0123456789ABC");

        Assert.Equal("012345678C", Row(terminal, 0, 10));
    }

    [Fact]
    public void An_explicit_zero_scroll_region_means_the_whole_screen()
    {
        // CSI 0;0r is how a program resets its region. It used to clamp to a single row.
        var terminal = NewTerminal(rows: 10);
        terminal.Write($"{Esc}[0;0r");

        Assert.Equal(0, terminal.Buffer.ScrollTop);
        Assert.Equal(9, terminal.Buffer.ScrollBottom);
    }

    [Fact]
    public void Insert_and_delete_line_move_the_cursor_to_the_left_margin()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[3;5H");
        terminal.Write($"{Esc}[L");
        Assert.Equal(0, terminal.Buffer.X);

        terminal.Write($"{Esc}[3;5H");
        terminal.Write($"{Esc}[M");
        Assert.Equal(0, terminal.Buffer.X);
    }

    [Fact]
    public void Save_and_restore_cursor_carry_the_charset()
    {
        // ESC ( 0 selects line drawing. A TUI that saves the cursor mid-border and restores it
        // expects to keep drawing lines, not letters.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}(0");        // G0 = line drawing
        terminal.Write($"{Esc}7");         // DECSC
        terminal.Write($"{Esc}(B");        // G0 = ASCII
        terminal.Write($"{Esc}8");         // DECRC
        terminal.Write("q");               // 'q' is a horizontal line in the DEC set

        Assert.Equal("\u2500", Row(terminal, 0, 1));
    }

    [Fact]
    public void A_save_inside_the_alternate_screen_does_not_disturb_the_normal_one()
    {
        // DECSC is per-screen: the rest of the saved state already lives on the buffer, so the
        // charset designations have to as well. Held on the input handler instead, a full-screen
        // program's save-and-restore inside the alternate buffer overwrote what the shell had
        // saved, and the line-drawing designation came back as ASCII after the program exited.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}(0");        // normal screen: G0 = line drawing
        terminal.Write($"{Esc}7");         // saved here, with line drawing designated

        terminal.Write($"{Esc}[?1049h");   // a full-screen program starts
        terminal.Write($"{Esc}(B");        // it wants ASCII
        terminal.Write($"{Esc}7");         // and saves and restores on its own redraws
        terminal.Write($"{Esc}8");
        terminal.Write($"{Esc}[?1049l");   // it exits

        terminal.Write($"{Esc}8");         // the shell restores what IT saved
        terminal.Write("q");

        Assert.Equal("\u2500", Row(terminal, 0, 1));
    }

    [Fact]
    public void A_program_can_set_and_clear_its_own_tab_stops()
    {
        // `tabs 4` writes stops with HTS. TBC used to acknowledge the request and do nothing.
        var terminal = NewTerminal(cols: 30);
        terminal.Write($"{Esc}[3g");       // clear every stop
        terminal.Write($"{Esc}[1;5H{Esc}H");   // HTS at column 4
        terminal.Write($"{Esc}[1;1H\t");

        Assert.Equal(4, terminal.Buffer.X);
    }

    [Fact]
    public void Clearing_all_stops_removes_the_defaults_too()
    {
        // The earlier test could not catch TBC doing nothing: its custom stop at column 4 merely
        // preceded the untouched default at 8, so a tab landed on 4 either way. This one asks
        // whether a DEFAULT stop was actually removed, which only passes if TBC works.
        var terminal = NewTerminal(cols: 40);
        terminal.Write($"{Esc}[3g");        // clear every stop
        terminal.Write($"{Esc}[1;1H" + "\t");

        // With no stops at all a tab goes to the last column, not to 8.
        Assert.Equal(39, terminal.Buffer.X);
    }

    [Fact]
    public void Backward_tab_uses_the_stops_a_program_set()
    {
        // CBT derived its answer arithmetically, so it ignored HTS stops and disagreed with
        // forward tab on the same screen: from column 6 with a stop at 4 it went to 0.
        var terminal = NewTerminal(cols: 40);
        terminal.Write($"{Esc}[3g");                    // no stops
        terminal.Write($"{Esc}[1;5H{Esc}H");            // HTS at column 4
        terminal.Write($"{Esc}[1;7H");                  // cursor at column 6
        terminal.Write($"{Esc}[Z");                     // CBT

        Assert.Equal(4, terminal.Buffer.X);
    }

    [Fact]
    public void Restoring_a_cursor_that_was_pending_a_wrap_still_wraps()
    {
        // The saved position is X == Cols, one past the last column. Restoring it through the
        // clamp put the cursor ON the last cell, so the next character overwrote that cell
        // instead of wrapping to the next row.
        var terminal = NewTerminal(cols: 10, rows: 4);
        terminal.Write("0123456789");       // fills the line; cursor pending wrap
        terminal.Write($"{Esc}7");          // DECSC
        terminal.Write($"{Esc}[3;1H");      // go elsewhere
        terminal.Write($"{Esc}8");          // DECRC
        terminal.Write("X");

        Assert.Equal("9", Row(terminal, 0, 10)[9..]);   // the last cell survived
        Assert.Equal("X", Row(terminal, 1, 1));         // and X wrapped
    }

    [Fact]
    public void Both_tab_motions_agree_on_the_same_screen()
    {
        // C0 HT hardcoded 8 while CHT honoured the option, so the two disagreed.
        var terminal = new Terminal(new TerminalOptions { Cols = 40, Rows = 5, TabStopWidth = 4 });
        terminal.Write("\t");
        var afterHt = terminal.Buffer.X;

        terminal.Write($"{Esc}[1;1H");
        terminal.Write($"{Esc}[1I");       // CHT 1
        Assert.Equal(afterHt, terminal.Buffer.X);
        Assert.Equal(4, afterHt);
    }

    [Fact]
    public void Insert_char_from_a_full_line_acts_on_the_last_column()
    {
        var terminal = NewTerminal(cols: 10);
        terminal.Write("0123456789");
        terminal.Write($"{Esc}[@");        // ICH 1

        Assert.Equal(" ", Row(terminal, 0, 10)[9..]);
    }

    [Fact]
    public void Hpa_and_vpr_move_the_cursor()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[5`");       // HPA to column 5
        Assert.Equal(4, terminal.Buffer.X);

        terminal.Write($"{Esc}[2e");       // VPR down 2
        Assert.Equal(2, terminal.Buffer.Y);
    }

    // ------------------------------------------------------------------ pending wrap is a FACT

    // SetCursorRaw used to set PendingWrap on EVERY print advance, under a contract that the flag
    // was "harmlessly stale" inside the margins because only the boundary column read it. The
    // moment CUB and the ICH/DCH/ECH settle step started reading it anywhere, every backward move
    // or edit issued right after a print acted one column LEFT of the cursor. On screen that was
    // asciiquarium leaving duplicated fragments behind left-moving sprites and eating characters
    // from right-moving ones -- rate-dependent only because it needed a print immediately followed
    // by a CUB or DCH in the same stream.

    [Fact]
    public void Delete_right_after_printing_deletes_at_the_cursor_not_one_left()
    {
        // The 16-byte repro the bug was cornered with: print AB, DCH 1. The cursor sits on the
        // cell after B, so the deletion must not touch B.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[5;1HAB{Esc}[1Ptail");

        Assert.Equal("ABtail", Row(terminal, 4, 6));
    }

    [Fact]
    public void Cursor_back_right_after_printing_counts_from_the_cursor_not_one_left()
    {
        // Print ABCD, CUB 2 -> the cursor is on C; DCH must eat C, not B.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[5;1HABCD{Esc}[2D{Esc}[1P");

        Assert.Equal("ABD ", Row(terminal, 4, 4));
    }

    [Fact]
    public void A_wrap_left_pending_on_another_line_does_not_shift_edits_after_a_move()
    {
        // Fill a line to the last column (a REAL pending wrap), address another line, print, edit.
        // The old flag survived the move and the settle step consumed it a screen away.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[1;14H{new string('X', 7)}");   // fills row 1 to column 20
        terminal.Write($"{Esc}[5;1HAB{Esc}[1Ptail");

        Assert.Equal("ABtail", Row(terminal, 4, 6));
    }

    [Fact]
    public void Printing_the_last_column_still_wraps_the_next_character()
    {
        // The guard for the fix itself: the flag must still be TRUE at the phantom column, or
        // autowrap dies. Fill the row exactly; the next character belongs at the start of row 2.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[1;1H{new string('X', 20)}Y");

        Assert.Equal("Y", terminal.Buffer.Lines[1]![0].Content);
    }

    [Fact]
    public void Insert_at_the_phantom_column_still_acts_on_the_last_column()
    {
        // What SettleForEditing exists for -- an editor that filled a line and inserted must see
        // the last column affected, not nothing. The fix must not regress it.
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[1;1H{new string('X', 20)}");   // pending wrap at the boundary
        terminal.Write($"{Esc}[1@");

        Assert.Equal(" ", string.IsNullOrEmpty(terminal.Buffer.Lines[0]![19].Content) ? " " : terminal.Buffer.Lines[0]![19].Content);
        Assert.Equal("X", terminal.Buffer.Lines[0]![18].Content);
    }

    // ------------------------------------------------------------------ reverse wrap, both flavours

    // xterm split reverse wraparound in 2023: mode 45 is INLINE -- backspace crosses onto the row
    // above only where the line actually wrapped, which is what erasing a wrapped command line
    // needs -- and mode 1045 is the CLASSIC behaviour, from any position, around the region's top.
    // Both require DECAWM, as they always have.

    [Fact]
    public void Inline_reverse_wrap_crosses_a_real_wrap()
    {
        var terminal = NewTerminal(cols: 10);
        terminal.Write($"{Esc}[?45h");
        terminal.Write(new string('x', 12));   // wraps onto row 2
        terminal.Write($"{Esc}[2;1H\b");

        Assert.Equal(0, terminal.Buffer.Y);
        Assert.Equal(9, terminal.Buffer.X);
    }

    [Fact]
    public void Inline_reverse_wrap_does_not_cross_where_nothing_wrapped()
    {
        // The cursor was put at the left margin by NEL/addressing; the row above is a different
        // line, and mode 45 has nothing to erase there.
        var terminal = NewTerminal(cols: 10);
        terminal.Write($"{Esc}[?45h");
        terminal.Write($"{Esc}[3;1H\b");

        Assert.Equal(2, terminal.Buffer.Y);
        Assert.Equal(0, terminal.Buffer.X);
    }

    [Fact]
    public void Reverse_wrap_requires_autowrap()
    {
        var terminal = NewTerminal(cols: 10);
        terminal.Write($"{Esc}[?7l");          // DECAWM off
        terminal.Write($"{Esc}[?1045h");
        terminal.Write($"{Esc}[3;1H\b");

        Assert.Equal(2, terminal.Buffer.Y);
        Assert.Equal(0, terminal.Buffer.X);
    }

    [Fact]
    public void Classic_reverse_wrap_carries_the_regions_top_around_to_its_bottom()
    {
        // The treatment xterm gave top/bottom margins in 2018, for consistency with left/right.
        var terminal = NewTerminal(cols: 10, rows: 8);
        terminal.Write($"{Esc}[?1045h");
        terminal.Write($"{Esc}[2;5r");         // region rows 2..5
        terminal.Write($"{Esc}[2;1H\b");

        Assert.Equal(4, terminal.Buffer.Y);    // bottom margin, 0-based
        Assert.Equal(9, terminal.Buffer.X);
    }

    [Fact]
    public void Classic_reverse_wrap_from_the_left_edge_lands_on_the_right_margin()
    {
        // A cursor LEFT of the left margin still backs into the pane, not past it: the row above
        // ends where the pane ends.
        var terminal = NewTerminal(cols: 20);
        terminal.Write($"{Esc}[?1045h{Esc}[?69h");
        terminal.Write($"{Esc}[5;12s");
        terminal.Write($"{Esc}[3;1H\b");

        Assert.Equal(1, terminal.Buffer.Y);
        Assert.Equal(11, terminal.Buffer.X);
    }

    // -------------------------------------------------------- the region is not the whole screen

    [Fact]
    public void A_line_feed_outside_the_side_margins_neither_scrolls_nor_leaves_the_region()
    {
        // The cursor is right of the region's columns, on its bottom row: the region's contents
        // are not its to scroll, and the bottom margin is not its to cross.
        var terminal = NewTerminal(cols: 10, rows: 8);
        terminal.Write($"{Esc}[2;5r{Esc}[?69h{Esc}[2;5s");
        terminal.Write($"{Esc}[5;3Hx");                    // inside: something to not-scroll
        terminal.Write($"{Esc}[5;7H\n");

        Assert.Equal(4, terminal.Buffer.Y);
        Assert.Equal("x", terminal.Buffer.Lines[4]![2].Content);
    }

    [Fact]
    public void An_alignment_pattern_resets_the_margins()
    {
        // DECALN exists for checking screen geometry, and it starts that geometry from scratch --
        // a surviving region would clip the very pattern.
        var terminal = NewTerminal(cols: 10, rows: 8);
        terminal.Write($"{Esc}[?69h{Esc}[2;3s{Esc}[4;5r");
        terminal.Write($"{Esc}#8");
        terminal.Write($"{Esc}[4;2H{Esc}[A");              // crossing the (former) top margin

        Assert.Equal(2, terminal.Buffer.Y);
        Assert.Equal(0, terminal.Buffer.ScrollTop);
        Assert.Equal(9, terminal.Buffer.ScrollRight);
    }

    [Fact]
    public void A_region_whose_top_is_not_above_its_bottom_is_refused_whole()
    {
        var terminal = NewTerminal(rows: 10);
        terminal.Write($"{Esc}[3;7r");
        terminal.Write($"{Esc}[3;3r");                     // invalid: ignored, not clamped

        Assert.Equal(2, terminal.Buffer.ScrollTop);
        Assert.Equal(6, terminal.Buffer.ScrollBottom);
    }

    [Fact]
    public void The_cursor_report_speaks_the_origin_modes_coordinates_in_both_axes()
    {
        var terminal = NewTerminal(cols: 20, rows: 10);
        string? reply = null;
        terminal.DataReceived += (_, e) => reply = e.Data;
        terminal.Write($"{Esc}[3;8r{Esc}[?69h{Esc}[5;12s{Esc}[?6h");
        terminal.Write($"{Esc}[2;3H");                     // region-relative addressing
        terminal.Write($"{Esc}[6n");

        Assert.Equal($"{Esc}[2;3R", reply);
    }

    [Fact]
    public void A_tab_stops_at_the_right_margin_for_a_cursor_inside_one()
    {
        var terminal = NewTerminal(cols: 40, rows: 5);
        terminal.Write($"{Esc}[?69h{Esc}[5;20s");
        terminal.Write($"{Esc}[1;7H\t\t\t");

        Assert.Equal(19, terminal.Buffer.X);               // the margin, not the next stop past it
    }

    // ---- Tabs vs margins, xterm's asymmetry --------------------------------------------------

    [Fact]
    public void ForwardTab_StartingLeftOfTheMargin_StillStopsAtTheRightMargin()
    {
        var terminal = NewTerminal(cols: 80, rows: 24);
        terminal.Write($"{Esc}[?69h{Esc}[5;30s");
        terminal.Write($"{Esc}[9;1H{Esc}[9I");     // from column 1, tab hard right

        Assert.Equal(30 - 1, terminal.Buffer.X);   // pinned at the right margin, not column 73
    }

    [Fact]
    public void BackwardTab_WalksStraightOutOfTheLeftMargin()
    {
        var terminal = NewTerminal(cols: 80, rows: 24);
        terminal.Write($"{Esc}[?69h{Esc}[5;30s");
        terminal.Write($"{Esc}[9;7H{Esc}[2Z");     // backward tabs ignore the region entirely

        Assert.Equal(0, terminal.Buffer.X);
    }

    // ---- Reverse wrap hygiene ------------------------------------------------------------------

    [Fact]
    public void ErasingALine_BreaksItsSoftWrapJoin()
    {
        var terminal = NewTerminal(cols: 80, rows: 24);
        terminal.Write($"{Esc}[?7h{Esc}[?45h");
        terminal.Write($"{Esc}[3;1H" + new string('*', 82));   // wraps: row 4 is a continuation
        Assert.True(terminal.Buffer.Lines[3]!.IsWrapped);

        terminal.Write($"{Esc}[3;40H{Esc}[K");                  // erase the tail of row 3
        Assert.False(terminal.Buffer.Lines[3]!.IsWrapped);

        // ...so reverse wrap now refuses the boundary the program erased -- and after ED 2
        // no boundary on the screen survives at all, which is what lets esctest's per-test
        // reset (DECSTR + ED 2) actually isolate consecutive reverse-wrap tests.
        terminal.Write($"{Esc}[4;1H{Esc}[5D");
        Assert.Equal(0, terminal.Buffer.X);
        Assert.Equal(3, terminal.Buffer.Y);
    }
}
