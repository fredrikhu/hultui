using System.Text;
using XTerm.Input;
using XTerm.Options;

namespace XTerm.Tests.Buffer;

/// <summary>
/// <see cref="TerminalOptions.ReflowCursorLine"/>: the wrapped line holding the cursor reflows like
/// every other, and the cursor moves with its text. Plus the capacity-keeping line resize it is
/// measured beside, SGR 53/55, and the legacy key encodings fixed in the same change.
/// </summary>
public class CursorLineReflowTests
{
    private const string Esc = "\u001b";

    private static Terminal Make(int cols, int rows, bool reflowCursorLine = true, int scrollback = 100) =>
        new(new TerminalOptions { Cols = cols, Rows = rows, Scrollback = scrollback, ReflowCursorLine = reflowCursorLine });

    /// <summary>The buffer as logical lines: wrapped rows joined at their wrapped length (which
    /// leaves out the blank a wide character that moved to the next row leaves behind), each
    /// line right-trimmed.</summary>
    private static string Logical(Terminal t)
    {
        var sb = new StringBuilder();
        var lines = t.Buffer.Lines;
        for (var start = 0; start < lines.Length;)
        {
            var group = new List<XTerm.Buffer.BufferLine> { lines[start]! };
            while (start + group.Count < lines.Length && lines[start + group.Count]!.IsWrapped)
                group.Add(lines[start + group.Count]!);
            if (start > 0) sb.Append('\n');
            for (var r = 0; r < group.Count; r++)
            {
                var length = XTerm.Buffer.BufferReflow.GetWrappedLineTrimmedLength(group, r, t.Cols);
                sb.Append(group[r].TranslateToString(false, 0, length));
            }
            start += group.Count;
        }
        return sb.ToString().TrimEnd('\n');
    }

    private static (int X, int Y) Cursor(Terminal t) => (t.Buffer.X, t.Buffer.Y);

    [Fact]
    public void Off_TheCursorLineIsCut_AsXtermJsDoes()
    {
        var t = Make(80, 10, reflowCursorLine: false);
        t.Write(new string('x', 100) + "$ cmd");
        t.Resize(50, 10);
        Assert.Equal(70, Logical(t).Count(c => c == 'x'));
    }

    [Fact]
    public void Narrowing_KeepsTheWholeLine_AndTheCursorAfterIt()
    {
        var t = Make(80, 10);
        var text = new string('x', 100) + "$ cmd";
        t.Write(text);
        Assert.Equal((25, 1), Cursor(t));

        t.Resize(50, 10);

        Assert.Contains(text, Logical(t));
        Assert.Equal((5, 2), Cursor(t));
        t.Write("!");
        Assert.Contains(text + "!", Logical(t));
    }

    [Fact]
    public void WideningBack_RejoinsTheLine_AndTheCursorFollows()
    {
        var t = Make(80, 10);
        var text = new string('x', 100) + "$ cmd";
        t.Write(text);
        t.Resize(50, 10);
        t.Resize(80, 10);

        Assert.Contains(text, Logical(t));
        Assert.Equal((25, 1), Cursor(t));
    }

    [Fact]
    public void CursorInTheMiddleOfTheGroup_StaysOnItsCharacter()
    {
        var t = Make(80, 10);
        t.Write(new string('x', 95) + "ABCDE");
        t.Write($"{Esc}[5D");                      // back onto 'A', logical offset 95
        Assert.Equal((15, 1), Cursor(t));

        t.Resize(50, 10);

        Assert.Equal((45, 1), Cursor(t));
        Assert.Equal("A", t.Buffer.Lines[t.Buffer.YBase + 1]![45].Content);
        Assert.Contains(new string('x', 95) + "ABCDE", Logical(t));
    }

    [Fact]
    public void GroupAtTheBottomOfAFullScreen_ScrollsRatherThanLosingItsLastRow()
    {
        var t = Make(40, 5);
        t.Write("one\r\ntwo\r\nthree\r\n");
        var command = new string('c', 70);         // two rows at 40 columns, the bottom two rows
        t.Write(command);
        Assert.Equal(0, t.Buffer.YBase);
        Assert.Equal(4, t.Buffer.Y);

        t.Resize(20, 5);                            // four rows now: the screen has to scroll

        var logical = Logical(t);
        Assert.Contains(command, logical);
        Assert.Contains("one\ntwo\nthree", logical);
        Assert.Equal(10, t.Buffer.X);               // 70 = 3 x 20 + 10
        Assert.Equal("c", t.Buffer.Lines[t.Buffer.YBase + t.Buffer.Y]![9].Content);
    }

    [Fact]
    public void ScrolledBuffer_KeepsFollowingTheBottom()
    {
        var t = Make(60, 10);
        for (var i = 0; i < 40; i++) t.Write($"line {i}\r\n");
        var command = "$ " + new string('k', 100);
        t.Write(command);

        t.Resize(30, 10);

        Assert.Contains(command, Logical(t));
        Assert.True(t.Buffer.IsAtBottom);
        Assert.Equal(102 % 30, t.Buffer.X);
        Assert.Equal('k', t.Buffer.Lines[t.Buffer.YBase + t.Buffer.Y]![t.Buffer.X - 1].Content[0]);
    }

    [Fact]
    public void WideCharacterAtTheWrapPoint_MovesWhole_OnTheCursorLine()
    {
        var t = Make(81, 4);
        t.Write(new string('a', 79) + "東東");
        t.Resize(80, 4);

        var row0 = t.Buffer.Lines[t.Buffer.YBase]!;
        Assert.NotEqual(2, row0[79].Width);         // nothing hangs off the right edge
        Assert.Contains(new string('a', 79) + "東東", Logical(t));
        Assert.Equal((4, 1), Cursor(t));            // after both, on the second row
    }

    [Fact]
    public void AlternateScreen_IsUnaffected()
    {
        var t = Make(80, 5);
        t.Write($"{Esc}[?1049h" + new string('y', 100));
        t.Resize(40, 5);
        Assert.Equal(40, t.GetLine(t.Buffer.YBase).TrimEnd().Length);
    }

    [Fact]
    public void NarrowThenWiden_UncoversBlanks_NotTheCellsTheNarrowingHid()
    {
        // The line keeps its array as capacity across a narrowing; the widening must fill what it
        // uncovers rather than show the old cells again. The alternate screen does not reflow, so
        // this is the line resize alone.
        var t = Make(80, 5);
        t.Write($"{Esc}[?1049h" + new string('z', 80));
        t.Resize(40, 5);
        t.Resize(80, 5);

        var line = t.Buffer.Lines[t.Buffer.YBase]!;
        Assert.Equal(new string('z', 40), line.TranslateToString(trimRight: true));
        Assert.Equal(80, line.Length);
        Assert.Equal(80, line.Count());              // the enumerator yields the line, not the array
    }

    [Fact]
    public void Sgr53And55_SetAndClearOverline()
    {
        var t = Make(80, 5);
        t.Write($"{Esc}[53mA{Esc}[55mB");
        var line = t.Buffer.Lines[0]!;
        Assert.True(line[0].Attributes.IsOverline());
        Assert.False(line[1].Attributes.IsOverline());
    }

    [Theory]
    [InlineData(Key.Enter, KeyModifiers.Alt, "\u001b\r")]
    [InlineData(Key.Backspace, KeyModifiers.Alt, "\u001b\u007f")]
    [InlineData(Key.Backspace, KeyModifiers.Control, "\b")]
    [InlineData(Key.Backspace, KeyModifiers.Control | KeyModifiers.Alt, "\u001b\b")]
    [InlineData(Key.Backspace, KeyModifiers.None, "\u007f")]
    [InlineData(Key.Enter, KeyModifiers.None, "\r")]
    public void LegacyKeys_AltPrefixesEnterAndBackspace_CtrlBackspaceIsBs(Key key, KeyModifiers mods, string expected)
    {
        var t = Make(80, 5);
        Assert.Equal(expected, t.GenerateKeyInput(key, mods));
    }

    [Fact]
    public void CtrlSlash_IsUnitSeparator()
    {
        var t = Make(80, 5);
        Assert.Equal("\u001f", t.GenerateCharInput('/', KeyModifiers.Control));
    }
}
