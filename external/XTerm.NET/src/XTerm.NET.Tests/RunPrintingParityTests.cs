using System.Text;
using XTerm;
using XTerm.Options;
using Xunit;

namespace XTerm.Tests;

/// <summary>
/// The batched ASCII path must be indistinguishable from printing one character at a time.
///
/// PrintAsciiRun does not merely call Print in a loop — it reimplements autowrap, the wrapped-line
/// flag and the cursor advance so it can write a span of cells in one go. That is exactly the kind
/// of duplicated logic that drifts, and the existing suite mostly writes short strings that never
/// straddle a line boundary, so it would not notice.
///
/// Every case here is run through both paths and the resulting buffers compared cell by cell, with
/// UseRunPrinting as the only difference between them.
/// </summary>
public class RunPrintingParityTests
{
    private const int Cols = 20;
    private const int Rows = 5;

    public static TheoryData<string, string> Cases() => new()
    {
        { "short", "hello" },
        { "exactly one line", new string('a', Cols) },
        { "one past a line", new string('b', Cols + 1) },
        { "several lines", new string('c', Cols * 3 + 7) },
        { "more than the screen", new string('d', Cols * Rows * 2 + 3) },
        { "run then control", new string('e', Cols - 2) + "\r\n" + "tail" },
        { "control mid-run", "abc\rdef" },
        { "newline mid-run", "abc\ndef" },
        { "tab mid-run", "abc\tdef" },
        { "backspace mid-run", "abcd\b\bxy" },
        { "sgr between runs", "aaa\u001b[31mbbb\u001b[0mccc" },
        { "wrap exactly at sgr", new string('f', Cols) + "\u001b[32m" + new string('g', 5) },
        { "cursor move mid-run", "aaaa\u001b[3;5Hbbbb" },
        { "DEL and C1 in a run", "abcdef" },
        { "non-ascii splits the run", "abc世界def" },
        { "emoji splits the run", "abc\U0001F600def" },
        { "combining after a run", "abcéfg" },
        { "scroll region", "\u001b[2;4r" + new string('h', Cols * 6) },
        { "wraparound off", "\u001b[?7l" + new string('i', Cols * 2) },
        { "wraparound back on", "\u001b[?7l" + new string('j', Cols + 5) + "\u001b[?7h" + new string('k', Cols + 5) },
        { "insert mode", "\u001b[4h" + "abcdef" + "\u001b[2G" + "XYZ" },
        { "line drawing charset", "\u001b(0" + "abcdefg" + "\u001b(B" + "hijk" },
        { "shift out and in", "abcdefghi" },
        { "empty write", "" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Batched_and_per_character_printing_agree(string name, string input)
    {
        var batched = Run(input, useRunPrinting: true);
        var perChar = Run(input, useRunPrinting: false);

        Assert.True(batched == perChar, $"'{name}' diverged.\n--- batched ---\n{batched}\n--- per character ---\n{perChar}");
    }

    /// <summary>
    /// Also feed each case in one-character writes. Chunk boundaries fall in different places than a
    /// single write, so a run that assumed it saw a whole line at once would show up here.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Chunking_does_not_change_the_result(string name, string input)
    {
        var whole = Run(input, useRunPrinting: true);
        var chunked = Run(input, useRunPrinting: true, chunkSize: 1);

        Assert.True(whole == chunked, $"'{name}' diverged on chunking.\n--- one write ---\n{whole}\n--- char at a time ---\n{chunked}");
    }

    private static string Run(string input, bool useRunPrinting, int chunkSize = 0)
    {
        var terminal = new Terminal(new TerminalOptions { Cols = Cols, Rows = Rows })
        {
            UseRunPrinting = useRunPrinting,
        };

        if (chunkSize <= 0)
        {
            terminal.Write(input);
        }
        else
        {
            for (var i = 0; i < input.Length; i += chunkSize)
                terminal.Write(input.Substring(i, Math.Min(chunkSize, input.Length - i)));
        }

        return Describe(terminal);
    }

    /// <summary>
    /// Everything the two paths could disagree about: cell contents and attributes, the wrapped flag
    /// each line carries, and where the cursor ended up.
    /// </summary>
    private static string Describe(Terminal terminal)
    {
        var sb = new StringBuilder();
        var buffer = terminal.Buffer;

        sb.Append("cursor=").Append(buffer.X).Append(',').Append(buffer.Y)
          .Append(" yBase=").Append(buffer.YBase)
          .Append(" lines=").Append(buffer.Lines.Length).AppendLine();

        for (var y = 0; y < buffer.Lines.Length; y++)
        {
            var line = buffer.Lines[y];
            if (line == null) { sb.AppendLine($"{y}: <null>"); continue; }

            sb.Append(y).Append(line.IsWrapped ? "w: " : " : ");
            for (var x = 0; x < line.Length; x++)
            {
                var cell = line[x];
                sb.Append(cell.CodePoint == 0 ? "." : cell.Content)
                  .Append('/').Append(cell.Width)
                  .Append('/').Append(cell.Attributes.GetHashCode())
                  .Append(' ');
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
