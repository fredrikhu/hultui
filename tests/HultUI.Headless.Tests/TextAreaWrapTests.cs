using System.Globalization;
using System.Reflection;
using System.Text;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Rendering;
using HultUI.Testing;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A TextArea wraps each line after the longest run of whole user-perceived characters that
/// fits, then back to the last space. The search for that run starts from a guess at a line's
/// length instead of measuring the whole rest of the line and bisecting all of it — which made
/// a 900-line document take 30 ms to open. These hold it to the definition, measured one
/// cluster at a time, with real fonts over many documents and widths.
///
/// <para>Whole clusters, because a cut inside one measures nonsense: half of 👩‍💻 draws as a
/// woman and a laptop, wider than the whole. Over plain text every char is a cluster and this
/// is the answer the old bisection gave.</para>
/// </summary>
public class TextAreaWrapTests
{
    private static readonly string[] Words =
    [
        "the", "a", "wrap", "measure", "line", "of", "paragraph", "café", "cafe\u0301", "naïve",
        "Ünïcödé", "東京", "漢字かな交じり文", "😀", "👩‍💻", "🇸🇪", "❤️", "\u1100\u1161\u11A8", "x",
        "supercalifragilisticexpialidocious", "—", "…", "→", "WWWWWW", "iiiiii", "fi", "ffl", "AV", "To",
    ];

    // The words the plain path lays out: no emoji, joiner, variation selector or jamo, which
    // keep their text on the BiDi path. Han before kana in one word: the prefixes that stop
    // short of the kana are paragraphs without it.
    private static readonly string[] PlainWords =
    [
        .. Words.Where(w => w.All(c => c < 0x1100 || c is >= (char)0x2010 and <= (char)0x2BFF or >= (char)0x3000 and <= (char)0x9FFF)),
        "한국어", "東京に行きます", "1234", "office", "Wolf", "AVATAR",
    ];

    private static string Document(Random rng, int lines, string[]? vocabulary = null)
    {
        var words = vocabulary ?? Words;
        var sb = new StringBuilder();
        for (int i = 0; i < lines; i++)
        {
            if (i > 0) sb.Append('\n');
            int count = rng.Next(5) switch { 0 => 0, 1 => rng.Next(1, 6), 2 => rng.Next(6, 30), _ => rng.Next(30, 260) };
            for (int w = 0; w < count; w++)
            {
                if (w > 0) sb.Append(rng.Next(12) == 0 ? "  " : " ");
                sb.Append(words[rng.Next(words.Length)]);
            }
            // Now and then a run with no space in it, which only a break between characters splits.
            if (rng.Next(9) == 0) sb.Append(' ').Append(new string('m', rng.Next(40, 400)));
            if (vocabulary is null && rng.Next(9) == 0) sb.Append(' ').Append(string.Concat(Enumerable.Repeat("👩‍💻", rng.Next(10, 60))));
        }
        return sb.ToString();
    }

    // The definition: every whole-cluster prefix measured, the longest that fits kept (one
    // cluster at least), then back to the last space if there is one.
    private static List<int> Reference(string text, float wrapWidth, Func<string, float> measure)
    {
        var starts = new List<int> { 0 };
        if (text.Length == 0) return starts;
        int pos = 0;
        while (pos < text.Length)
        {
            int hardEnd = text.IndexOf('\n', pos);
            if (hardEnd < 0) hardEnd = text.Length;
            int segStart = pos;
            while (segStart < hardEnd)
            {
                int lineEnd = Break(segStart, hardEnd);
                if (lineEnd >= hardEnd) break;
                starts.Add(lineEnd);
                segStart = lineEnd;
            }
            pos = hardEnd + 1;
            if (pos <= text.Length) starts.Add(pos);
        }
        return starts;

        int Break(int start, int hardEnd)
        {
            var ends = new List<int>();
            for (int i = start; i < hardEnd;)
            {
                i += Math.Max(1, StringInfo.GetNextTextElementLength(text.AsSpan(i, hardEnd - i)));
                ends.Add(i);
            }
            int fitEnd = ends[0];
            foreach (int end in ends)
                if (measure(text[start..end]) <= wrapWidth) fitEnd = end;
                else break;
            if (fitEnd == hardEnd) return hardEnd;
            int breakAt = fitEnd;
            while (breakAt > start && !char.IsWhiteSpace(text[breakAt - 1])) breakAt--;
            return breakAt > start ? breakAt : fitEnd;
        }
    }

    [Fact]
    public void TheLineStartsAreTheOnesMeasuringEveryClusterFinds()
        => AssertTheReferenceLineStarts(vocabulary: null, letterSpacing: 0f, seed: 20261006);

    /// <summary>
    /// Text the plain path lays out has each line's widths read off one table of its prefixes
    /// (<see cref="IRenderContext.MeasureTextPrefixes"/>) instead of measuring each — still the
    /// reference's widths, to the bit, letter-spaced or not.
    /// </summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.6f)]
    public void TheLineStartsAreTheSame_WhenTheWidthsComeFromATableOfPrefixes(float letterSpacing)
        => AssertTheReferenceLineStarts(PlainWords, letterSpacing, seed: 20261007);

    private static void AssertTheReferenceLineStarts(string[]? vocabulary, float letterSpacing, int seed)
    {
        using var session = HeadlessSession.Create(900, 700);
        var area = TextArea.New("").FlexGrow(1).LetterSpacing(letterSpacing);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(area));
        session.PumpFrame();   // the area takes its render context from the first frame

        var ctx = (IRenderContext)typeof(TextAreaElement)
            .GetField("_renderCtx", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(area)!;
        var style = area.ResolvedInherited;
        Assert.Equal(letterSpacing, style.LetterSpacing);
        var rng = new Random(seed);
        int compared = 0, wrapped = 0;
        for (int doc = 0; doc < 8; doc++)
        {
            area.Text = Document(rng, 30, vocabulary);
            // The plain documents' lines do get a table: the comparison is of it.
            if (vocabulary is not null)
                Assert.NotNull(ctx.MeasureTextPrefixes(area.Text.Split('\n').MaxBy(l => l.Length)!, style));
            foreach (float width in new[] { 12f, 47.5f, 90f, 233.3f, 612f, 1300f })
            {
                var expected = Reference(area.Text, width, s => ctx.MeasureText(s, style).Width);
                var actual = area.GetLineStarts(width, style);
                Assert.True(expected.SequenceEqual(actual), Describe(area.Text, width, expected, actual));
                compared++;
                wrapped += expected.Count - (area.Text.Count(c => c == '\n') + 1);
            }
        }
        // The documents did wrap, and at every width: an all-fitting run would prove nothing.
        Assert.Equal(8 * 6, compared);
        Assert.True(wrapped > 5_000, $"only {wrapped} wrapped lines");
    }

    /// <summary>
    /// An edit changes a line or two, and a long line it leaves alone keeps where it broke the
    /// last time, remembered by its text — at that width and in that style only. After every
    /// edit, in whichever of two widths and two styles comes next, the breaks are still the
    /// reference's.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AfterEveryEdit_TheLineStartsAreStillTheReferences(bool plain)
    {
        using var session = HeadlessSession.Create(900, 700);
        var area = TextArea.New("").FlexGrow(1);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(area));
        session.PumpFrame();

        var ctx = (IRenderContext)typeof(TextAreaElement)
            .GetField("_renderCtx", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(area)!;
        var styles = new[] { area.ResolvedInherited, area.ResolvedInherited with { FontSize = area.ResolvedInherited.FontSize + 3 } };
        var widths = new[] { 233.3f, 612f };
        var words = plain ? PlainWords : Words;
        var rng = new Random(plain ? 20261009 : 20261008);
        area.Text = Document(rng, 30, plain ? PlainWords : null);
        int wrapped = 0;
        for (int edit = 0; edit < 80; edit++)
        {
            // Mostly the same width and style, as when typing; now and then the other.
            var style = styles[rng.Next(6) == 0 ? 1 : 0];
            float width = widths[rng.Next(6) == 0 ? 1 : 0];
            var expected = Reference(area.Text, width, s => ctx.MeasureText(s, style).Width);
            var actual = area.GetLineStarts(width, style);
            Assert.True(expected.SequenceEqual(actual), $"after {edit} edits: " + Describe(area.Text, width, expected, actual));
            wrapped += expected.Count - (area.Text.Count(c => c == '\n') + 1);

            // A word typed in, a run deleted, a line split or two joined. Never inside a
            // surrogate pair: that is no edit a TextArea makes.
            var text = area.Text;
            int at = rng.Next(text.Length + 1);
            while (at > 0 && at < text.Length && char.IsLowSurrogate(text[at])) at--;
            int end = Math.Min(text.Length, at + rng.Next(1, 12));
            while (end < text.Length && char.IsLowSurrogate(text[end])) end++;
            area.Text = rng.Next(4) switch
            {
                0 => text.Insert(at, words[rng.Next(words.Length)] + " "),
                1 when end > at => text.Remove(at, end - at),
                2 => text.Insert(at, "\n"),
                _ => text.IndexOf('\n', at) is var nl and >= 0 ? text.Remove(nl, 1) : text + " x",
            };
        }
        Assert.True(wrapped > 5_000, $"only {wrapped} wrapped lines");
    }

    /// <summary>
    /// The break search reads each wrapped line's widths off a table of a range of the document
    /// (<see cref="IRenderContext.MeasureTextPrefixes(string, int, int, Core.Styling.ResolvedInheritedStyle)"/>),
    /// a view in the render context's scratch: wrapping every line again at a new width — a step
    /// of a resize drag — allocates nothing once the words are known. A table of each range's own
    /// (its string, an exact copy of its segments, its runs and the objects holding them) was
    /// 1.3 MB a step over a 900-line document.
    /// </summary>
    [Fact]
    public void WrappingAgainAtANewWidth_AllocatesNothing()
    {
        string[] vocabulary = ["the", "a", "wrap", "measure", "line", "of", "paragraph", "café", "naïve", "office",
            "Wolf", "AVATAR", "1234", "—", "→", "x", "supercalifragilisticexpialidocious"];
        using var session = HeadlessSession.Create(900, 700);
        var area = TextArea.New(Document(new Random(5), 120, vocabulary)).FlexGrow(1);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(area));
        session.PumpFrame();   // the area takes its render context from the first frame
        var style = area.ResolvedInherited;
        int hardLines = area.Text.Count(c => c == '\n') + 1;

        int rowsNarrow = area.GetLineStarts(233.3f, style).Count;
        int rowsWide = area.GetLineStarts(612f, style).Count;
        Assert.True(rowsNarrow > hardLines + 1_000 && rowsWide > hardLines + 200,
            $"{hardLines} lines wrapped to {rowsNarrow} and {rowsWide} rows: too few to search");
        Assert.Equal(0, Allocations.Least(() =>
        {
            area.GetLineStarts(233.3f, style);
            area.GetLineStarts(612f, style);
        }));
    }

    private static string Describe(string text, float width, List<int> expected, List<int> actual)
    {
        int k = 0;
        while (k < expected.Count && k < actual.Count && expected[k] == actual[k]) k++;
        int from = k > 0 ? expected[k - 1] : 0;
        return $"at width {width}, from {from}: expected a break at {(k < expected.Count ? expected[k] : -1)}, "
            + $"got {(k < actual.Count ? actual[k] : -1)} in \"{text[from..Math.Min(text.Length, from + 60)]}\"";
    }
}
