using XTerm;
using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// The XTWINOPS 22/23 title stack, title modes, and what RIS does and does not reset.
/// The stack model is xterm's: one stack, each entry snapshotting both titles.
/// </summary>
public class TitleStackTests
{
    private const string EscChar = "\u001b";

    private static Terminal NewTerminal()
    {
        var options = new TerminalOptions { Cols = 80, Rows = 24 };
        options.WindowOptions.GetWinTitle = true;
        options.WindowOptions.GetIconTitle = true;
        return new Terminal(options);
    }

    [Fact]
    public void PopOfEitherPart_ConsumesTheWholeEntry()
    {
        var terminal = NewTerminal();
        terminal.Write($"{EscChar}]0;first");    // both titles
        terminal.Write($"{EscChar}[22;0t");            // push both
        terminal.Write($"{EscChar}]0;x");

        terminal.Write($"{EscChar}[23;1t");            // pop just the icon
        Assert.Equal("first", terminal.IconTitle);
        Assert.Equal("x", terminal.Title);

        terminal.Write($"{EscChar}[23;2t");            // entry is gone: nothing to restore
        Assert.Equal("x", terminal.Title);
    }

    [Fact]
    public void EachPush_SnapshotsBothTitles()
    {
        var terminal = NewTerminal();
        terminal.Write($"{EscChar}]2;win{EscChar}]1;icon");
        terminal.Write($"{EscChar}[22;1t");            // "push icon"
        terminal.Write($"{EscChar}[22;2t");            // "push window"
        terminal.Write($"{EscChar}]2;y{EscChar}]1;z");

        terminal.Write($"{EscChar}[23;0t");            // one pop-both restores both
        Assert.Equal("win", terminal.Title);
        Assert.Equal("icon", terminal.IconTitle);
    }

    [Fact]
    public void LabelReports_UseSt_AndHonourTheHexQueryMode()
    {
        var terminal = NewTerminal();
        var replies = new List<string>();
        terminal.DataReceived += (_, e) => replies.Add(e.Data);

        terminal.Write($"{EscChar}]2;ab{EscChar}[21t");
        Assert.Equal($"{EscChar}]lab{EscChar}\\", replies[^1]);

        terminal.Write($"{EscChar}[>1t{EscChar}[21t"); // query-hex title mode on
        Assert.Equal($"{EscChar}]l6162{EscChar}\\", replies[^1]);

        terminal.Write($"{EscChar}c{EscChar}[21t");    // RIS resets the title mode, keeps the title
        Assert.Equal($"{EscChar}]lab{EscChar}\\", replies[^1]);
    }

    [Fact]
    public void Ris_ResetsDeccolm_AndBlanksTheAlternateScreen()
    {
        var terminal = NewTerminal();
        terminal.Write($"{EscChar}[?40h{EscChar}[?3h");
        Assert.Equal(132, terminal.Cols);

        terminal.Write($"{EscChar}[?47h" + "a");
        terminal.Write($"{EscChar}c");
        Assert.Equal(80, terminal.Cols);

        terminal.Write($"{EscChar}[?47h");             // back onto the alt screen: it must be blank
        Assert.True(string.IsNullOrEmpty(terminal.Buffer.Lines[0]![0].Content)
                    || terminal.Buffer.Lines[0]![0].Content == " ");
    }
}
