using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// Character protection, both flavours: DECSCA against the selective erases, SPA/EPA against the
/// ordinary ones -- independent bits, independently honoured.
/// </summary>
public class ProtectionTests
{
    private const string Esc = "\u001b";

    private static Terminal NewTerminal(int cols = 10, int rows = 4) =>
        new(new TerminalOptions { Cols = cols, Rows = rows });

    private static string Row(Terminal t, int row, int count)
    {
        var line = t.Buffer.Lines[row]!;
        return string.Concat(Enumerable.Range(0, count)
            .Select(i => string.IsNullOrEmpty(line[i].Content) ? " " : line[i].Content));
    }

    [Fact]
    public void A_selective_erase_walks_around_DECSCA_protected_text()
    {
        var terminal = NewTerminal();
        terminal.Write($"ab{Esc}[1\"qCD{Esc}[0\"qef");
        terminal.Write($"{Esc}[1;1H{Esc}[?2K");   // DECSEL: erase the line, selectively

        Assert.Equal("  CD  ", Row(terminal, 0, 6));
    }

    [Fact]
    public void An_ordinary_erase_ignores_DECSCA()
    {
        var terminal = NewTerminal();
        terminal.Write($"ab{Esc}[1\"qCD{Esc}[0\"qef");
        terminal.Write($"{Esc}[1;1H{Esc}[2K");    // plain EL

        Assert.Equal("      ", Row(terminal, 0, 6));
    }

    [Fact]
    public void An_ordinary_erase_walks_around_the_ISO_guard()
    {
        var terminal = NewTerminal();
        terminal.Write($"ab{Esc}VCD{Esc}Wef");    // SPA ... EPA
        terminal.Write($"{Esc}[1;1H{Esc}[2K");

        Assert.Equal("  CD  ", Row(terminal, 0, 6));
    }

    [Fact]
    public void ECH_respects_the_guard_but_not_DECSCA()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[1\"qab{Esc}[0\"q{Esc}VCD{Esc}W");
        terminal.Write($"{Esc}[1;1H{Esc}[4X");

        Assert.Equal("  CD", Row(terminal, 0, 4));
    }

    [Fact]
    public void A_selective_full_erase_spares_protected_cells_on_every_row()
    {
        var terminal = NewTerminal();
        terminal.Write($"ab\r\n{Esc}[1\"qXY{Esc}[0\"qcd");
        terminal.Write($"{Esc}[?2J");             // DECSED 2

        Assert.Equal("  ", Row(terminal, 0, 2));
        Assert.Equal("XY  ", Row(terminal, 1, 4));
    }

    [Fact]
    public void The_status_string_reports_the_protection_in_force()
    {
        var terminal = NewTerminal();
        string? reply = null;
        terminal.DataReceived += (_, e) => reply = e.Data;
        terminal.Write($"{Esc}[1\"q");
        terminal.Write($"{Esc}P$q\"q{Esc}\\");

        Assert.Contains("1\"q", reply);
    }

    [Fact]
    public void A_soft_reset_stops_protecting()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[1\"q{Esc}[!p");
        terminal.Write("X");
        terminal.Write($"{Esc}[1;1H{Esc}[?2K");   // selective erase takes the unprotected X

        Assert.Equal(" ", Row(terminal, 0, 1));
    }
}
