using XTerm;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// The global protection gate xterm keeps beside the per-cell bits: SPA raises the ISO
/// discipline, DECSCA the DEC one, and a soft reset drops the gate so erases ignore whatever
/// bits are still in cells. Also covers XTSAVE/XTRESTORE, which ride the same CSI ? prefix.
/// </summary>
public class ProtectionModeTests
{
    private const string Esc = "\u001b";

    private static Terminal NewTerminal() =>
        new(new TerminalOptions { Cols = 80, Rows = 24 });

    private static string Row(Terminal t, int row, int count)
    {
        var line = t.Buffer.Lines[row]!;
        return string.Concat(Enumerable.Range(0, count)
            .Select(i => string.IsNullOrEmpty(line[i].Content) ? " " : line[i].Content));
    }

    [Fact]
    public void SoftReset_DropsTheGate_SoEdSweepsGuardedCells()
    {
        var terminal = NewTerminal();
        terminal.Write($"ab{Esc}Vc{Esc}W");        // 'c' ISO-guarded
        terminal.Write($"{Esc}[1;1H{Esc}[3X");
        Assert.Equal("  c", Row(terminal, 0, 3));  // ECH honours the guard while ISO is in force

        terminal.Write($"{Esc}[!p{Esc}[2J");       // DECSTR then ED 2 -- esctest's per-test reset
        Assert.Equal("   ", Row(terminal, 0, 3));  // gate is off: the guarded cell goes too
    }

    [Fact]
    public void Decsca_SelectsTheDecDiscipline_SoPlainErasesIgnoreGuards()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}Va{Esc}W");          // guarded under ISO
        terminal.Write($"{Esc}[0\"q");             // any DECSCA switches the gate to DEC
        terminal.Write($"{Esc}[1;1H{Esc}[1X");
        Assert.Equal(" ", Row(terminal, 0, 1));    // plain erase no longer honours the ISO guard
    }

    [Fact]
    public void XtermSaveAndRestore_RoundTripAPrivateMode()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[?7h{Esc}[?7s");     // autowrap on, saved
        terminal.Write($"{Esc}[?7l");
        Assert.False(terminal.Options.Wraparound);

        terminal.Write($"{Esc}[?7r");              // restored
        Assert.True(terminal.Options.Wraparound);
    }

    [Fact]
    public void ReverseIndex_OutsideTheMargins_NeitherScrollsNorClimbs()
    {
        var terminal = NewTerminal();
        terminal.Write($"{Esc}[2;5r{Esc}[?69h{Esc}[2;5s");
        terminal.Write($"{Esc}[5;3Hx");

        terminal.Write($"{Esc}[2;6H{Esc}M");       // at top margin, right of the region
        Assert.Equal(5, terminal.Buffer.X);
        Assert.Equal(1, terminal.Buffer.Y);
        Assert.Equal("x", terminal.Buffer.Lines[4]![2].Content);   // nothing scrolled

        terminal.Write($"{Esc}[1;6H{Esc}M");       // at the screen's top edge
        Assert.Equal(0, terminal.Buffer.Y);
    }
}
