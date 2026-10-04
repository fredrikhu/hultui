using System.Text;
using XTerm.Parser;

namespace XTerm.Tests.Parser;

/// <summary>
/// APC was entered and thrown away, along with SOS and PM: the parser hunted for the terminator and
/// discarded every byte in between. That made the Kitty graphics protocol unreachable, since APC is
/// how it arrives.
///
/// <para>APC now streams its payload the way DCS does, while SOS and PM keep the old discard path —
/// there is nothing in either worth reading, and the sequence that used to wedge the parser is the
/// regression these tests exist alongside.</para>
/// </summary>
public class ApcSequenceTests
{
    private const string Esc = "\u001b";
    private const string St = Esc + "\\";

    /// <summary>Everything an APC sequence produced, in order.</summary>
    private sealed class Recorder
    {
        public int Hooks;
        public int Unhooks;
        public char Introducer;
        public bool? TerminatedCleanly;
        public readonly StringBuilder Payload = new();

        public static Recorder Of(string input)
        {
            var parser = new EscapeSequenceParser();
            var recorder = new Recorder();

            parser.ApcHook += (_, e) => { recorder.Hooks++; recorder.Introducer = e.Introducer; };
            parser.ApcPut += (_, e) => recorder.Payload.Append(e.Data.Span);
            parser.ApcUnhook += (_, e) =>
            {
                recorder.Unhooks++;
                recorder.TerminatedCleanly = e.TerminatedCleanly;
            };

            parser.Parse(input);
            return recorder;
        }
    }

    [Fact]
    public void An_apc_sequence_delivers_its_payload()
    {
        var recorded = Recorder.Of(Esc + "_Gi=1,a=T;AAAA" + St);

        Assert.Equal(1, recorded.Hooks);
        Assert.Equal(1, recorded.Unhooks);
        Assert.Equal('_', recorded.Introducer);
        Assert.Equal("Gi=1,a=T;AAAA", recorded.Payload.ToString());
    }

    /// <summary>
    /// SOS and PM have nothing worth reading, and the discard path they use is the fix for a hang.
    /// Leaving them alone is the point of routing only "ESC _" to the new state.
    /// </summary>
    [Theory]
    [InlineData("\u001b^private message", "PM")]
    [InlineData("\u001bXsome string", "SOS")]
    public void Sos_and_pm_are_still_discarded(string sequence, string what)
    {
        var recorded = Recorder.Of(sequence + St);

        Assert.True(recorded.Hooks == 0, $"{what} should not raise an APC hook");
        Assert.Equal("", recorded.Payload.ToString());
    }

    [Theory]
    [InlineData("\u001b\\", "two-byte ST")]
    [InlineData("\u009c", "single-byte ST")]
    public void A_string_terminator_ends_the_sequence_cleanly(string terminator, string what)
    {
        var recorded = Recorder.Of(Esc + "_Gdata" + terminator);

        Assert.Equal(1, recorded.Unhooks);
        Assert.True(recorded.TerminatedCleanly == true, $"{what} should end the sequence cleanly");
    }

    /// <summary>
    /// Half a transmission is not worth decoding, and the only way a handler can tell is if the
    /// parser distinguishes an abandoned sequence from a finished one.
    /// </summary>
    [Theory]
    [InlineData("\u0018", "CAN")]
    [InlineData("\u001a", "SUB")]
    [InlineData("\u001b[", "another escape sequence starting on top of it")]
    public void An_abandoned_sequence_is_reported_as_unclean(string interruption, string what)
    {
        var recorded = Recorder.Of(Esc + "_Gdata" + interruption);

        Assert.Equal(1, recorded.Unhooks);
        Assert.True(recorded.TerminatedCleanly == false,
            $"{what} abandons the sequence; calling it clean would let a truncated image be decoded");
    }

    /// <summary>
    /// A Kitty image runs to megabytes, so the payload is streamed in chunks. That seam is where a
    /// long transmission would lose or reorder bytes if the flush were wrong.
    /// </summary>
    [Fact]
    public void A_payload_longer_than_the_chunk_buffer_arrives_intact()
    {
        var payload = string.Concat(Enumerable.Range(0, 5000).Select(i => (char)('a' + i % 26)));

        var recorded = Recorder.Of(Esc + "_G" + payload + St);

        Assert.Equal("G" + payload, recorded.Payload.ToString());
    }

    /// <summary>The payload does not care where the write boundaries fall.</summary>
    [Fact]
    public void A_payload_split_across_writes_arrives_intact()
    {
        var parser = new EscapeSequenceParser();
        var payload = new StringBuilder();
        parser.ApcPut += (_, e) => payload.Append(e.Data.Span);

        parser.Parse(Esc + "_Ga=T,f=32");
        parser.Parse(",s=1,v=1;AA");
        parser.Parse("AA" + Esc);
        parser.Parse("\\");

        Assert.Equal("Ga=T,f=32,s=1,v=1;AAAA", payload.ToString());
    }

    [Fact]
    public void A_reset_mid_payload_closes_the_sequence()
    {
        var parser = new EscapeSequenceParser();
        bool? cleanly = null;
        int unhooks = 0;
        parser.ApcUnhook += (_, e) => { unhooks++; cleanly = e.TerminatedCleanly; };

        parser.Parse(Esc + "_Ghalf a transmission");
        parser.Reset();

        Assert.Equal(1, unhooks);
        Assert.True(cleanly == false,
            "a reset abandons whatever was arriving; a decoder left open would wait for ever");
    }

    [Fact]
    public void Two_sequences_in_a_row_are_kept_apart()
    {
        var parser = new EscapeSequenceParser();
        var payloads = new List<string>();
        var current = new StringBuilder();

        parser.ApcHook += (_, _) => current.Clear();
        parser.ApcPut += (_, e) => current.Append(e.Data.Span);
        parser.ApcUnhook += (_, _) => payloads.Add(current.ToString());

        parser.Parse(Esc + "_Gfirst" + St + Esc + "_Gsecond" + St);

        Assert.Equal(new[] { "Gfirst", "Gsecond" }, payloads);
    }

    /// <summary>
    /// A DCS and an APC must not close each other. Both resolve an ESC one character late, and
    /// sharing the flag that remembers it would let the wrong payload be unhooked.
    /// </summary>
    [Fact]
    public void A_dcs_and_an_apc_do_not_close_each_other()
    {
        var parser = new EscapeSequenceParser();
        var dcs = new StringBuilder();
        var apc = new StringBuilder();
        int dcsUnhooks = 0, apcUnhooks = 0;

        parser.DcsPut += (_, e) => dcs.Append(e.Data.Span);
        parser.DcsUnhook += (_, _) => dcsUnhooks++;
        parser.ApcPut += (_, e) => apc.Append(e.Data.Span);
        parser.ApcUnhook += (_, _) => apcUnhooks++;

        parser.Parse(Esc + "Pqsixel" + St + Esc + "_Gkitty" + St + Esc + "Pqmore" + St);

        Assert.Equal(1, apcUnhooks);
        Assert.Equal(2, dcsUnhooks);
        Assert.Equal("Gkitty", apc.ToString());
        Assert.Equal("sixelmore", dcs.ToString());
    }

    /// <summary>The regression this whole area exists to guard: the parser has to come back.</summary>
    [Fact]
    public void Text_after_an_apc_sequence_still_prints()
    {
        var parser = new EscapeSequenceParser();
        var printed = new StringBuilder();
        parser.Print += (_, e) => printed.Append(e.Data);

        parser.Parse(Esc + "_Gi=31,s=1,v=1,a=q,t=d,f=24;AAAA" + St + "OK");

        Assert.Equal("OK", printed.ToString());
    }

    /// <summary>And the backslash of the two-byte terminator is part of it, not text.</summary>
    [Fact]
    public void The_backslash_of_a_two_byte_terminator_is_not_printed()
    {
        var parser = new EscapeSequenceParser();
        var printed = new StringBuilder();
        parser.Print += (_, e) => printed.Append(e.Data);

        parser.Parse(Esc + "_Gdata" + St);

        Assert.Equal("", printed.ToString());
    }
}
