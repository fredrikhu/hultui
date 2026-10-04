using XTerm.Options;

namespace XTerm.Tests;

/// <summary>
/// The string-terminated sequences — DCS, and SOS/PM/APC — are consumed whole and answered by nobody. What
/// matters is that the parser LEAVES them.
///
/// <para>SosPmApcString had no handler at all: it was entered and never exited, so an APC sequence put the
/// parser in a state where it discarded every byte that followed, for the life of the session. One kitty
/// graphics query and the terminal stopped answering anything — including the cursor position reports a
/// program was waiting on, which is a hang rather than a glitch.</para>
/// </summary>
public class StringSequenceTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";

    private static Terminal Fresh() => new(new TerminalOptions { Cols = 40, Rows = 5 });

    private static string ScreenTop(Terminal t)
    {
        var line = t.Buffer.Lines[0]!;
        return string.Concat(Enumerable.Range(0, 12).Select(i => line[i].Content)).TrimEnd();
    }

    /// <summary>Every string sequence must hand the parser back afterwards.</summary>
    [Theory]
    [InlineData("\u001b_Gi=31,s=1,v=1,a=q,t=d,f=24;AAAA", "APC — kitty graphics")]
    [InlineData("\u001b^some private message", "PM")]
    [InlineData("\u001bXsome string", "SOS")]
    [InlineData("\u001bP$qm", "DCS — DECRQSS")]
    [InlineData("\u001bP+q544e", "DCS — XTGETTCAP")]
    public void Text_after_a_string_sequence_still_prints(string sequence, string what)
    {
        var terminal = Fresh();
        terminal.Write(sequence + St + "OK");

        Assert.True(ScreenTop(terminal) == "OK",
            $"after {what} the parser never came back — it swallowed everything after it. Saw: '{ScreenTop(terminal)}'");
    }

    /// <summary>
    /// The case that hung: a query, then a cursor position request. The request has to be answered.
    /// </summary>
    [Theory]
    [InlineData("\u001b_Gi=31,a=q;AAAA", "APC")]
    [InlineData("\u001b^private", "PM")]
    [InlineData("\u001bXstring", "SOS")]
    [InlineData("\u001bP$qm", "DCS")]
    public void A_cursor_report_after_a_string_sequence_is_answered(string sequence, string what)
    {
        var terminal = Fresh();
        string? reply = null;
        terminal.DataReceived += (_, e) => reply = e.Data;

        terminal.Write(sequence + St + Esc + "[6n");

        Assert.True(reply is not null,
            $"no cursor report after {what}: a program that queries and waits would wait for ever");
    }

    /// <summary>
    /// The terminator is two bytes, and both belong to it. Leaving the second to be printed put a stray
    /// backslash on screen after every DCS a program sent.
    /// </summary>
    [Fact]
    public void The_backslash_of_a_two_byte_terminator_is_not_printed()
    {
        var terminal = Fresh();
        terminal.Write(Esc + "P$qm" + St);

        Assert.Equal("", ScreenTop(terminal));
    }

    /// <summary>A single-byte ST ends it too.</summary>
    [Fact]
    public void A_single_byte_terminator_also_ends_the_sequence()
    {
        var terminal = Fresh();
        terminal.Write(Esc + "_apc\u009c" + "OK");

        Assert.Equal("OK", ScreenTop(terminal));
    }
}
