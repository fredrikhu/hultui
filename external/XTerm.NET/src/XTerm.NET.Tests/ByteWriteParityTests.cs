using System.Text;
using XTerm;
using XTerm.Options;
using Xunit;

namespace XTerm.Tests;

/// <summary>
/// Write(ReadOnlySpan&lt;byte&gt;) must land the same buffer as Write(string).
///
/// The byte entry is a second decoder, not a wrapper — it scans printable ASCII in bulk, decodes
/// multi-byte sequences itself, and carries a partial sequence across calls. So it can disagree with
/// the string path in ways nothing else would catch, and the split-sequence cases are the whole
/// reason the entry is worth having: a PTY read boundary lands mid-codepoint routinely, and a caller
/// that decodes each read on its own corrupts that character every time.
/// </summary>
public class ByteWriteParityTests
{
    private const int Cols = 20;
    private const int Rows = 5;

    public static TheoryData<string, string> Cases() => new()
    {
        { "ascii", "hello world" },
        { "wrapping ascii", new string('a', Cols * 3 + 5) },
        { "controls", "abc\r\ndef\tghi" },
        { "sgr", "\u001b[31mred\u001b[0m plain" },
        { "two byte utf8", "café naïve" },
        { "three byte utf8", "世界こんにちは" },
        { "four byte utf8", "\U0001F600\U0001F601 emoji" },
        { "mixed", "abc世界\U0001F600def\u001b[32mghi" },
        { "combining", "éà" },
        { "zwj sequence", "\U0001F468‍\U0001F469‍\U0001F467" },
        { "wrap onto multibyte", new string('x', Cols - 1) + "世界" },
        { "osc", "\u001b]0;a titletail" },
    };

    /// <summary>
    /// Half a codepoint held from the byte entry cannot be completed by UTF-16 input, so switching
    /// entries abandons it -- and abandoning it must SAY so.
    /// </summary>
    /// <remarks>
    /// Mixing the two entries mid-sequence is a caller error. It should still not make characters
    /// disappear: a byte quietly dropped shortens the stream, which is corruption a caller cannot
    /// see, while U+FFFD is the standard way of saying something was there and could not be read.
    /// </remarks>
    [Fact]
    public void Bytes_held_across_a_switch_to_the_string_entry_become_a_replacement()
    {
        var terminal = NewTerminal();

        terminal.Write(new byte[] { 0xE4, 0xB8 });   // two thirds of a three-byte codepoint
        terminal.Write("ok");

        Assert.Equal("\uFFFDok", FirstRow(terminal));
    }

    /// <summary>The mirror: a high surrogate held from the string entry, abandoned by byte input.</summary>
    [Fact]
    public void A_surrogate_held_across_a_switch_to_the_byte_entry_becomes_a_replacement()
    {
        var terminal = NewTerminal();

        terminal.Write("\uD83D");                    // the high half of an emoji, alone
        terminal.Write(new byte[] { (byte)'o', (byte)'k' });

        Assert.Equal("\uFFFDok", FirstRow(terminal));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Byte_and_string_writes_agree(string name, string input)
    {
        var viaString = RunString(input);
        var viaBytes = RunBytes(input, chunkSize: 0);

        Assert.True(viaString == viaBytes,
            $"'{name}' diverged.\n--- string ---\n{viaString}\n--- bytes ---\n{viaBytes}");
    }

    /// <summary>
    /// The same input delivered one byte at a time. Every multi-byte sequence is therefore split
    /// across calls, which is what a PTY read boundary does and what the carry exists to survive.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Splitting_a_sequence_across_writes_changes_nothing(string name, string input)
    {
        var whole = RunBytes(input, chunkSize: 0);
        var byteAtATime = RunBytes(input, chunkSize: 1);

        Assert.True(whole == byteAtATime,
            $"'{name}' diverged when split.\n--- one write ---\n{whole}\n--- byte at a time ---\n{byteAtATime}");
    }

    /// <summary>Every possible split point of a short multi-byte string, not just the pathological one.</summary>
    [Fact]
    public void Every_split_point_agrees()
    {
        const string input = "a世b\U0001F600c";
        var expected = RunBytes(input, chunkSize: 0);
        var bytes = Encoding.UTF8.GetBytes(input);

        for (var split = 0; split <= bytes.Length; split++)
        {
            var terminal = NewTerminal();
            terminal.Write(bytes.AsSpan(0, split));
            terminal.Write(bytes.AsSpan(split));

            Assert.True(expected == Describe(terminal), $"split after {split} of {bytes.Length} bytes diverged");
        }
    }

    private static Terminal NewTerminal() => new(new TerminalOptions { Cols = Cols, Rows = Rows });

    /// <summary>The written part of the top row, trailing blanks trimmed.</summary>
    private static string FirstRow(Terminal terminal)
    {
        var line = terminal.Buffer.Lines[terminal.Buffer.YBase]!;
        var text = string.Concat(Enumerable.Range(0, Cols).Select(c => line[c].Content));
        return text.TrimEnd('\0', ' ');
    }

    private static string RunString(string input)
    {
        var terminal = NewTerminal();
        terminal.Write(input);
        return Describe(terminal);
    }

    private static string RunBytes(string input, int chunkSize)
    {
        var terminal = NewTerminal();
        var bytes = Encoding.UTF8.GetBytes(input);

        if (chunkSize <= 0)
        {
            terminal.Write(bytes.AsSpan());
        }
        else
        {
            for (var i = 0; i < bytes.Length; i += chunkSize)
                terminal.Write(bytes.AsSpan(i, Math.Min(chunkSize, bytes.Length - i)));
        }

        return Describe(terminal);
    }

    private static string Describe(Terminal terminal)
    {
        var sb = new StringBuilder();
        var buffer = terminal.Buffer;

        sb.Append("cursor=").Append(buffer.X).Append(',').Append(buffer.Y)
          .Append(" yBase=").Append(buffer.YBase).AppendLine();

        for (var y = 0; y < buffer.Lines.Length; y++)
        {
            var line = buffer.Lines[y];
            if (line == null) { sb.AppendLine($"{y}: <null>"); continue; }

            sb.Append(y).Append(line.IsWrapped ? "w: " : " : ");
            for (var x = 0; x < line.Length; x++)
            {
                var cell = line[x];
                sb.Append(cell.CodePoint == 0 ? "." : cell.Content)
                  .Append('/').Append(cell.Width).Append(' ');
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
