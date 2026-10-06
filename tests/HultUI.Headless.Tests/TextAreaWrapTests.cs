using System.Globalization;
using System.Reflection;
using System.Text;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Rendering;
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
        "the", "a", "wrap", "measure", "line", "of", "paragraph", "café", "café", "naïve",
        "Ünïcödé", "東京", "漢字かな交じり文", "😀", "👩‍💻", "🇸🇪", "❤️", "각", "x",
        "supercalifragilisticexpialidocious", "—", "…", "→", "WWWWWW", "iiiiii", "fi", "ffl", "AV", "To",
    ];

    private static string Document(Random rng, int lines)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines; i++)
        {
            if (i > 0) sb.Append('\n');
            int words = rng.Next(5) switch { 0 => 0, 1 => rng.Next(1, 6), 2 => rng.Next(6, 30), _ => rng.Next(30, 260) };
            for (int w = 0; w < words; w++)
            {
                if (w > 0) sb.Append(rng.Next(12) == 0 ? "  " : " ");
                sb.Append(Words[rng.Next(Words.Length)]);
            }
            // Now and then a run with no space in it, which only a break between characters splits.
            if (rng.Next(9) == 0) sb.Append(' ').Append(new string('m', rng.Next(40, 400)));
            if (rng.Next(9) == 0) sb.Append(' ').Append(string.Concat(Enumerable.Repeat("👩‍💻", rng.Next(10, 60))));
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
    {
        using var session = HeadlessSession.Create(900, 700);
        var area = TextArea.New("").FlexGrow(1);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Children(area));
        session.PumpFrame();   // the area takes its render context from the first frame

        var ctx = (IRenderContext)typeof(TextAreaElement)
            .GetField("_renderCtx", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(area)!;
        var style = area.ResolvedInherited;
        var rng = new Random(20261006);
        int compared = 0, wrapped = 0;
        for (int doc = 0; doc < 8; doc++)
        {
            area.Text = Document(rng, 30);
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

    private static string Describe(string text, float width, List<int> expected, List<int> actual)
    {
        int k = 0;
        while (k < expected.Count && k < actual.Count && expected[k] == actual[k]) k++;
        int from = k > 0 ? expected[k - 1] : 0;
        return $"at width {width}, from {from}: expected a break at {(k < expected.Count ? expected[k] : -1)}, "
            + $"got {(k < actual.Count ? actual[k] : -1)} in \"{text[from..Math.Min(text.Length, from + 60)]}\"";
    }
}
