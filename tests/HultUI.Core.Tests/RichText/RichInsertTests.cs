using System.Collections.Immutable;
using HultUI.Core.RichText;
using HultUI.Testing;
using Xunit;

namespace HultUI.Core.Tests.RichText;

/// <summary>
/// Text put into a block, as typing and a one-block paste put it: the text and spans that come
/// out, at a span's edges, inside one, between two, at the block's start and end and into an empty
/// one; the same block, text and spans as slicing the block at the offset and joining it around
/// the text gave, which <see cref="RichEditState"/> did until plan 17 round 6; and the one string
/// and one span array it makes for them.
/// </summary>
public class RichInsertTests
{
    private static readonly RichMarks Bold = new(InlineStyle.Bold);
    private static readonly RichMarks Italic = new(InlineStyle.Italic);

    private static RichEditState Edit(params RichBlock[] blocks) => new(new RichDocument(blocks));

    private static RichBlock P(string text, params RichSpan[] spans) => RichBlock.Paragraph(text, spans);

    private static void Caret(RichEditState s, int block, int offset) => s.Select(RichSelection.Caret(block, offset));

    private static void Type(RichEditState s, string text)
    {
        foreach (var rune in text.EnumerateRunes())
            s.InsertText(rune.ToString());
    }

    private static string Show(RichEditState s) => string.Join(" | ", s.Document.Blocks);

    private static void Paste(RichEditState s, RichBlock block) => s.InsertFragment(new RichFragment([block]));

    // ── What comes out ──

    [Fact]
    public void TypingInsideASpan_GrowsIt()
    {
        var s = Edit(P("a bold word", new RichSpan(2, 4, Bold)));
        Caret(s, 0, 4);
        Type(s, "xy");
        Assert.Equal("Paragraph \"a boxyld word\" {2+6:Bold}", Show(s));
    }

    [Fact]
    public void TypingAtASpansStart_IsPlain_AndMovesTheSpanOn()
    {
        var s = Edit(P("a bold word", new RichSpan(2, 4, Bold)));
        Caret(s, 0, 2);
        Type(s, "x");
        Assert.Equal("Paragraph \"a xbold word\" {3+4:Bold}", Show(s));
    }

    [Fact]
    public void TypingAtASpansEnd_GrowsIt_AndMovesTheNextOn()
    {
        var s = Edit(P("boldital", new RichSpan(0, 4, Bold), new RichSpan(4, 4, Italic)));
        Caret(s, 0, 4);
        Type(s, "er");
        Assert.Equal("Paragraph \"bolderital\" {0+6:Bold 6+4:Italic}", Show(s));
    }

    [Fact]
    public void TypingAtTheStart_TakesTheFirstCharactersMarks_AndMovesEverySpanOn()
    {
        var s = Edit(P("bold and it", new RichSpan(0, 4, Bold), new RichSpan(9, 2, Italic)));
        Caret(s, 0, 0);
        Type(s, "x");
        Assert.Equal("Paragraph \"xbold and it\" {0+5:Bold 10+2:Italic}", Show(s));

        var plain = Edit(P("a bold", new RichSpan(2, 4, Bold)));
        Caret(plain, 0, 0);
        Type(plain, "x");
        Assert.Equal("Paragraph \"xa bold\" {3+4:Bold}", Show(plain));
    }

    [Fact]
    public void TypingAtTheEnd_GrowsTheLastSpan()
    {
        var s = Edit(P("ab bold", new RichSpan(3, 4, Bold)));
        Caret(s, 0, 7);
        Type(s, "x");
        Assert.Equal("Paragraph \"ab boldx\" {3+5:Bold}", Show(s));
    }

    [Fact]
    public void TypingIntoAnEmptyParagraph_TakesThePendingMarks()
    {
        var s = Edit(P(""));
        Type(s, "a");
        Assert.Equal("Paragraph \"a\"", Show(s));

        var bold = Edit(P(""));
        Assert.True(bold.ToggleStyle(InlineStyle.Bold));
        Type(bold, "ab");
        Assert.Equal("Paragraph \"ab\" {0+2:Bold}", Show(bold));
    }

    [Fact]
    public void PastingIntoASpan_CutsItAroundThePaste_OrJoinsItWhenTheMarksAreTheSame()
    {
        var other = Edit(P("bold", new RichSpan(0, 4, Bold)));
        Caret(other, 0, 2);
        Paste(other, P("it", new RichSpan(0, 2, Italic)));
        Assert.Equal("Paragraph \"boitld\" {0+2:Bold 2+2:Italic 4+2:Bold}", Show(other));

        var plain = Edit(P("bold", new RichSpan(0, 4, Bold)));
        Caret(plain, 0, 2);
        Paste(plain, P("xx"));
        Assert.Equal("Paragraph \"boxxld\" {0+2:Bold 4+2:Bold}", Show(plain));

        var same = Edit(P("bold", new RichSpan(0, 4, Bold)));
        Caret(same, 0, 2);
        Paste(same, P("XX", new RichSpan(0, 2, Bold)));
        Assert.Equal("Paragraph \"boXXld\" {0+6:Bold}", Show(same));
    }

    [Fact]
    public void PastingMarkedTextIntoCode_DropsTheMarks()
    {
        var s = Edit(RichBlock.Of(BlockKind.CodeBlock, "ab"));
        Caret(s, 0, 1);
        Paste(s, P("XY", new RichSpan(0, 2, Bold)));
        Assert.Equal("CodeBlock \"aXYb\"", Show(s));
    }

    // ── The same as the slices and joins it replaced ──

    // What InsertInline was: the block sliced at the offset and joined around the text.
    private static RichBlock SlicedAndJoined(RichBlock block, int offset, string text, ImmutableArray<RichSpan> spans)
    {
        var head = block.Slice(0, offset);
        var tail = block.Slice(offset, block.Text.Length);
        var middle = block with { Text = text, Spans = block.IsPreformatted ? [] : spans };
        var joined = head.Append(middle);
        return tail.Text.Length == 0 ? joined : joined.Append(tail);
    }

    [Fact]
    public void InsertInline_IsTheBlockSlicedAndJoinedAroundTheText()
    {
        // Short blocks, so offsets land on span edges, inside spans and between them often; marks
        // that repeat, so neighbours merge; inserted spans that are plain, that touch, that carry
        // the marks on either side; nothing inserted; offsets past either end; code blocks.
        var rng = new Random(616);
        RichMarks[] marks = [RichMarks.None, Bold, Italic, new(InlineStyle.Bold | InlineStyle.Italic), new(Link: "u")];
        int compared = 0, kept = 0;
        for (int i = 0; i < 20_000; i++)
        {
            string text = Letters(rng, rng.Next(0, 12));
            var block = rng.Next(8) == 0
                ? RichBlock.Of(BlockKind.CodeBlock, text)
                : P(text, Spans(rng, text.Length, marks));
            string inserted = Letters(rng, rng.Next(0, 4));
            var insertedSpans = ImmutableArray.Create(Spans(rng, inserted.Length, marks));
            int offset = rng.Next(-1, text.Length + 2);

            var expected = SlicedAndJoined(block, offset, inserted, insertedSpans);
            var actual = RichEditState.InsertInline(block, offset, inserted, insertedSpans);

            string where = $"{block} at {offset} with \"{inserted}\" {{{string.Join(" ", insertedSpans)}}}";
            Assert.True(expected.ToString() == actual.ToString(), $"{where}: {actual}, expected {expected}");
            Assert.True(expected.Equals(actual) && expected.SameFormat(actual), where);
            Assert.True(ReferenceEquals(expected, block) == ReferenceEquals(actual, block), $"{where}: the block itself or a copy");
            compared++;
            if (ReferenceEquals(actual, block)) kept++;
        }
        Assert.True(kept > 50, $"the block itself came back only {kept} times of {compared}");
    }

    private static string Letters(Random rng, int n)
    {
        var chars = new char[n];
        for (int i = 0; i < n; i++) chars[i] = (char)('a' + rng.Next(4));
        return new string(chars);
    }

    // Up to three spans in [0, length), in order and apart or touching, any marks: as a block's
    // spans before normalizing, and as the spans handed in with the text.
    private static RichSpan[] Spans(Random rng, int length, RichMarks[] marks)
    {
        var spans = new List<RichSpan>();
        int at = 0;
        for (int k = rng.Next(0, 4); k > 0 && at < length; k--)
        {
            int start = at + rng.Next(0, Math.Min(3, length - at));
            int len = 1 + rng.Next(0, length - start);
            spans.Add(new RichSpan(start, len, marks[rng.Next(marks.Length)]));
            at = start + len;
        }
        return [.. spans];
    }

    // ── What it makes ──

    [Fact]
    public void TypingInAParagraphOfManySpans_MakesOneArrayOfThem()
    {
        // A key typed between marked words of a 2,400-character paragraph, with 300 or 600 spans
        // over it: the text, one array of the spans, and the block, so the 300 spans more cost 300
        // spans of one array (14,400 B). The block sliced and joined around the key made four span
        // builders, each copied out and the slices' grown by doubling: 92,400 B.
        var few = Marked(300);
        var many = Marked(600);
        int offset = few.Text.Length / 2 + 4;   // after a space, before a word
        Assert.Equal(RichMarks.None, many.MarksAt(offset - 1));

        object? keep = null;
        long fewBytes = Allocations.Least(() => keep = RichEditState.InsertInline(few, offset, "x", []));
        long manyBytes = Allocations.Least(() => keep = RichEditState.InsertInline(many, offset, "x", []));
        long arrays = Allocations.Least(() => keep = new RichSpan[600]) - Allocations.Least(() => keep = new RichSpan[300]);
        Assert.Equal(600, RichEditState.InsertInline(many, offset, "x", []).Spans.Length);
        Assert.Equal(arrays, manyBytes - fewBytes);
        GC.KeepAlive(keep);
    }

    // 600 words of three letters and a space, every (600 / spans)th word bold or italic.
    private static RichBlock Marked(int spans)
    {
        string text = string.Concat(Enumerable.Repeat("abc ", 600));
        int every = 600 / spans;
        var list = new List<RichSpan>();
        for (int w = 0; w < 600; w += every)
            list.Add(new RichSpan(w * 4, 3, w / every % 2 == 0 ? Bold : Italic));
        return P(text, [.. list]);
    }
}
