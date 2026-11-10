using System.Globalization;
using System.Reflection;
using System.Text;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// A searched row of a line with a joining character in it (an emoji, a mark, a ZWJ, any
/// surrogate) is cut only where a user-perceived character ends. Those ends are found for the
/// searched rows only, from a boundary at or before each, and only as far as its search reads:
/// listing the whole line's before its first row took 1.2 MB a key in a one-line 89 K character
/// JSON body with one emoji (plan 17, round 10). The rows are the whole-line walk's
/// (<see cref="TextAreaElement.ClustersByRow"/> false) at every width.
/// </summary>
public class TextAreaClustersTests
{
    private readonly LayoutEngine _layout = new(new StubMeasureContext());

    // Every printable ASCII character Advance wide, anything else one and a half (each half of a
    // surrogate pair, each mark): a cut inside a cluster moves a break.
    private sealed class Mono : NullRenderContext, IRenderContext
    {
        public const float Advance = 7f;

        private static float Width(ReadOnlySpan<char> text)
        {
            float w = 0;
            foreach (char c in text) w += c is >= ' ' and <= '~' ? Advance : 1.5f * Advance;
            return w;
        }

        public override Size MeasureText(string text, ResolvedInheritedStyle style) => new(Width(text), style.FontSize);

        public float MeasureTextWidth(ReadOnlySpan<char> text, ResolvedInheritedStyle style) => Width(text);

        public float MonospaceAsciiAdvance(ResolvedInheritedStyle style) => Advance;
    }

    private TextAreaElement Mount(string text, bool clustersByRow, bool fitsByCount)
    {
        var area = TextAreaElement.New(text);
        area.ClustersByRow = clustersByRow;
        area.FitsByCount = fitsByCount;
        area.Inherited.FontSize = 12;
        area.Box.Width = 700;
        area.Box.Height = 300;
        var root = BoxElement.New();
        root.Box.Width = 900;
        root.Box.Height = 400;
        root.AddChild(area);
        StyleResolver.Resolve(root, Theme.Light.ToResolvedStyle());
        _layout.PerformLayout(root, 900, 400);
        typeof(TextAreaElement).GetField("_renderCtx", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(area, new Mono());
        return area;
    }

    private static readonly FieldInfo LinesValid =
        typeof(TextAreaElement).GetField("_linesValid", BindingFlags.NonPublic | BindingFlags.Instance)!;

    // The rows at `width`, wrapped for it (GetLineStarts keeps its rows for a width within half a
    // pixel of theirs).
    private static int[] Rows(TextAreaElement area, float width, ResolvedInheritedStyle style)
    {
        LinesValid.SetValue(area, false);
        return area.GetLineStarts(width, style).ToArray();
    }

    // Clusters of every kind the line breaking meets: an emoji, a letter and a mark, a ZWJ family,
    // a surrogate pair that is no emoji, a space and a lone mark (a row cut after the space starts
    // inside a cluster), flags (regional indicators pair up), Hangul jamo, Devanagari with a
    // spacing mark and with a conjunct, a thumb with a skin tone, a keycap, a prepended number
    // sign before digits, a lone high surrogate, a space and a ZWJ, a carriage return mid-line, a
    // run of 40 emoji, a letter and 12 marks.
    private static readonly string[] Joiners =
    [
        "\U0001F600", "é", "\U0001F468‍\U0001F469‍\U0001F467", "\U0001D538", " ́",
        "\U0001F1F8\U0001F1EA\U0001F1F3\U0001F1F4", "각", "कि", "क्ष",
        "\U0001F44D\U0001F3FD", "1️⃣", "؀123", "\uD83Dx", " ‍", "a\r b",
        string.Concat(Enumerable.Repeat("\U0001F600", 40)), "a" + new string('́', 12),
    ];

    // Words of 1 to 14 printable ASCII characters with one space between, and now and then a
    // run of 150 with none.
    private static string Code(Random rng, int length)
    {
        var sb = new StringBuilder(length + 160);
        while (sb.Length < length)
        {
            if (sb.Length > 0) sb.Append(' ');
            int n = rng.Next(12) == 0 ? 150 : rng.Next(1, 15);
            for (int i = 0; i < n; i++) sb.Append((char)('!' + rng.Next(94)));
        }
        return sb.ToString();
    }

    private static string Document()
    {
        var rng = new Random(10);
        var lines = new List<string>();
        int k = 0;
        // Long lines of code with joiners at the start, the middle and the end.
        foreach (int length in new[] { 3_000, 800, 5_000, 400, 2_000 })
        {
            string line = Code(rng, length);
            line = line.Insert(line.Length, Joiners[k++ % Joiners.Length]);
            line = line.Insert(line.Length / 2, Joiners[k++ % Joiners.Length]);
            line = Joiners[k++ % Joiners.Length] + line;
            lines.Add(line);
        }
        // One joiner far into a line whose other rows break by their count.
        lines.Add(Code(rng, 4_000).Insert(3_210, "\U0001F600"));
        // Joiners every few words, every kind.
        var dense = new StringBuilder();
        for (int i = 0; dense.Length < 6_000; i++)
            dense.Append(Code(rng, rng.Next(5, 60))).Append(Joiners[i % Joiners.Length]);
        lines.Add(dense.ToString());
        // No pair of chars below U+0300 to start from: emoji only, Han only.
        lines.Add(string.Concat(Enumerable.Repeat("\U0001F600\U0001F44D\U0001F3FD", 300)));
        lines.Add(string.Concat(Enumerable.Repeat("東京タワー", 400)));
        lines.Add("short");
        lines.Add("");
        return string.Join('\n', lines);
    }

    /// <summary>
    /// The rows of lines with joiners are the whole-line walk's at every width: between multiples
    /// of the advance (rows by count, the joiners' rows searched), on and a hair either side of
    /// them (searched), under one cluster's width (a cluster a row), with rows breaking by count
    /// and every row searched.
    /// </summary>
    [Fact]
    public void TheRowsOfLinesWithJoiners_AreTheWholeLineWalks_AtEveryWidth()
    {
        string text = Document();
        int compared = 0, rows = 0;
        foreach (bool fitsByCount in new[] { true, false })
        {
            var area = Mount(text, clustersByRow: true, fitsByCount);
            var reference = Mount(text, clustersByRow: false, fitsByCount);
            var style = area.ResolvedInherited;
            foreach (float chars in new[] { 0.5f, 1f, 2f, 3f, 7f, 20f, 40f, 63f, 64f, 99f, 100f, 101f, 150f })
                foreach (float nudge in new[] { -0.5f, -0.01f, 0f, 0.01f, 0.5f })
                {
                    float width = chars * Mono.Advance + nudge;
                    var expected = Rows(reference, width, style);
                    var actual = Rows(area, width, style);
                    Assert.True(expected.AsSpan().SequenceEqual(actual),
                        $"fitsByCount {fitsByCount}, at {width} ({chars} characters' width {nudge:+0.##;-0.##;0}) the rows differ at row {FirstDifference(expected, actual)}");
                    compared++;
                    rows += expected.Length;
                }
        }
        Assert.Equal(2 * 13 * 5, compared);
        Assert.True(rows > 300_000, $"only {rows} rows: too few lines wrapped");
    }

    /// <summary>
    /// The walk itself, rows started anywhere — inside a cluster, past what was found, within it
    /// — finds after each start the ends the whole line's walk from its start does, as far as it
    /// is asked, and how many are left at the line's end. A row's start past what was found is
    /// walked from the nearest boundary before it, not from itself: from inside a cluster, a walk
    /// cuts it (a ZWJ family's second person is a cluster of its own walked from its joiner).
    /// </summary>
    [Fact]
    public void TheWalk_FromAnyRowStart_FindsTheWholeLineWalksEnds()
    {
        var type = typeof(TextAreaElement).GetNestedType("Clusters", BindingFlags.NonPublic)!;
        var walk = Activator.CreateInstance(type, nonPublic: true)!;
        var line = type.GetMethod("Line")!;
        var row = type.GetMethod("Row")!;
        var count = type.GetMethod("Count")!;
        var end = type.GetMethod("End")!;
        string text = Document();
        var rng = new Random(12);
        int checkedEnds = 0, insideClusters = 0;
        for (int start = 0; start < text.Length;)
        {
            int hardEnd = text.IndexOf('\n', start);
            if (hardEnd < 0) hardEnd = text.Length;
            var ends = new List<int>();
            for (int i = start; i < hardEnd;)
                ends.Add(i += Math.Max(1, StringInfo.GetNextTextElementLength(text.AsSpan(i, hardEnd - i))));
            // Every place in the line as the first row's start past the line's own (jump 0), then
            // rows one after another, a few chars apart to a few rows.
            foreach (int jump in new[] { 0, 3, 40, 400 })
            {
                line.Invoke(walk, [text, start, hardEnd, false]);
                for (int s = start; s < hardEnd; s += jump == 0 ? 1 : rng.Next(1, jump))
                {
                    if (jump == 0) line.Invoke(walk, [text, start, hardEnd, false]);
                    row.Invoke(walk, [s]);
                    int first = ends.FindIndex(e => e > s);
                    if (first > 0 && ends[first - 1] != s) insideClusters++;
                    int asked = rng.Next(1, 120);
                    int found = (int)count.Invoke(walk, [asked])!;
                    Assert.Equal(Math.Min(asked, ends.Count - first), found);
                    for (int k = 1; k <= found; k++, checkedEnds++)
                        if ((int)end.Invoke(walk, [k])! != ends[first + k - 1])
                            Assert.Fail($"from {s} in line [{start}..{hardEnd}) cluster {k} ends at {end.Invoke(walk, [k])}, not {ends[first + k - 1]}");
                }
                type.GetMethod("Done")!.Invoke(walk, null);
            }
            start = hardEnd + 1;
        }
        Assert.True(checkedEnds > 100_000 && insideClusters > 100, $"{checkedEnds} ends checked, {insideClusters} rows started inside a cluster");
    }

    private static int FirstDifference(int[] a, int[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i]) return i;
        return n;
    }

    /// <summary>
    /// What the walk starts from: between any two chars below U+0300 other than CR LF a cluster
    /// ends, whatever comes before them — a joiner, an emoji and a joiner, a regional indicator, a
    /// prepended sign, a mark, a Hangul letter. Pinned against the runtime's grapheme rules.
    /// </summary>
    [Fact]
    public void BetweenTwoCharsBelowU0300_OtherThanCrLf_AClusterEnds()
    {
        string[] before = ["", "‍", "\U0001F600‍", "\U0001F1F8", "؀", "é", "ᄀ", "\uD83D"];
        var buffer = new char[8];
        int checkedPairs = 0;
        foreach (string x in before)
        {
            x.CopyTo(buffer);
            for (int a = 0; a < 0x300; a++)
                for (int b = 0; b < 0x300; b++)
                {
                    if (a == '\r' && b == '\n') continue;
                    buffer[x.Length] = (char)a;
                    buffer[x.Length + 1] = (char)b;
                    var span = buffer.AsSpan(0, x.Length + 2);
                    // Walked from the start, a cluster ends just before b.
                    int at = 0;
                    while (at < x.Length + 1)
                        at += Math.Max(1, StringInfo.GetNextTextElementLength(span[at..]));
                    if (at != x.Length + 1)
                        Assert.Fail($"after \"{Escape(x)}\", U+{a:X4} U+{b:X4} are one cluster");
                    checkedPairs++;
                }
        }
        Assert.Equal(before.Length * (0x300 * 0x300 - 1), checkedPairs);
    }

    private static string Escape(string s) => string.Concat(s.Select(c => $"\\u{(int)c:X4}"));

    /// <summary>
    /// The win: wrapping a one-line 89 K character JSON body with one emoji in it again — at a new
    /// width, every row by its count but the emoji's — allocates what the same body without the
    /// emoji does, where listing the whole line's cluster ends took a 131,072-entry list and its
    /// doublings (some 1 MB) every time.
    /// </summary>
    [Fact]
    public void AOneLineBodyWithAnEmoji_WrapsAgainAsTheBodyWithoutOne()
    {
        var rng = new Random(11);
        var sb = new StringBuilder("[");
        for (int i = 0; sb.Length < 89_000; i++)
            sb.Append("{\"id\":").Append(1_000 + i).Append(",\"name\":\"").Append(Code(rng, rng.Next(4, 20))).Append("\",\"tags\":[\"a\",\"b\"]},");
        string plain = sb.Append("{}]").ToString();
        string withEmoji = plain.Insert(plain.Length * 3 / 4, "\U0001F600 ");
        long Bytes(string text)
        {
            var area = Mount(text, clustersByRow: true, fitsByCount: true);
            var style = area.ResolvedInherited;
            // Half a character from a multiple of the advance: the rows break by count.
            float[] widths = [100.5f * Mono.Advance, 90.5f * Mono.Advance];
            foreach (float w in widths) Rows(area, w, style);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 8; i++) area.GetLineStarts(widths[i % 2], style);
            return (GC.GetAllocatedBytesForCurrentThread() - a0) / 8;
        }
        long without = Bytes(plain), with = Bytes(withEmoji);
        Assert.True(with <= without + 16 * 1024,
            $"wrapping the body with an emoji again took {with / 1024.0:F1} KB, without one {without / 1024.0:F1} KB");
    }
}
