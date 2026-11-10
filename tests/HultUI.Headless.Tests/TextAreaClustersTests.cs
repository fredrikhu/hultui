using System.Reflection;
using System.Text;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using SkiaSharp;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A TextArea finds where the user-perceived characters of a line with a joining character in
/// it end only for the rows it searches, from a boundary at or before each, as far as the search
/// reads (<see cref="TextAreaElement.ClustersByRow"/>); listing the whole line's before its first
/// row took 1.2 MB a key in a one-line JSON body with one emoji. With real fonts: the rows, the
/// caret, a click, a selection and highlights are the whole-line walk's.
/// </summary>
public class TextAreaClustersTests
{
    private static readonly string[] Joiners =
    [
        "\U0001F600", "é", "\U0001F468‍\U0001F469‍\U0001F467", "\U0001D538", " ́",
        "\U0001F1F8\U0001F1EA\U0001F1F3\U0001F1F4", "각", "कि क्ष",
        "\U0001F44D\U0001F3FD", "1️⃣", " ‍", string.Concat(Enumerable.Repeat("\U0001F600", 12)),
        "á̂̃",
    ];

    // Lines of code far longer than a row, with joiners at the start, the middle and the end of
    // each, one far into a line of printable ASCII, and every few words in another.
    private static string LongLines()
    {
        var rng = new Random(31);
        string[] tokens = ["function ", "return ", "var ", "a", "bC", "x=", "(e,t){", "};", "\"red pony\",", "===", "&&", "!0", "[1,2]", ".push(", ")", "null", "0x1f", " "];
        string Code(int count)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < count; i++) sb.Append(tokens[rng.Next(tokens.Length)]);
            return sb.ToString();
        }
        var lines = new List<string>();
        int k = 0;
        foreach (int count in new[] { 900, 300, 1_200 })
        {
            string line = Code(count);
            line = line.Insert(line.Length, Joiners[k++ % Joiners.Length]);
            line = line.Insert(line.Length / 2, Joiners[k++ % Joiners.Length]);
            lines.Add(Joiners[k++ % Joiners.Length] + line);
        }
        lines.Add(Code(1_000).Insert(2_900, "\U0001F600"));
        var dense = new StringBuilder();
        for (int i = 0; i < 120; i++) dense.Append(Code(rng.Next(2, 12))).Append(Joiners[i % Joiners.Length]);
        lines.Add(dense.ToString());
        lines.Add("short");
        return string.Join('\n', lines);
    }

    private static IRenderContext ContextOf(TextAreaElement area) => (IRenderContext)typeof(TextAreaElement)
        .GetField("_renderCtx", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(area)!;

    private static readonly FieldInfo LinesValid =
        typeof(TextAreaElement).GetField("_linesValid", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static int[] Rows(TextAreaElement area, float width, ResolvedInheritedStyle style)
    {
        LinesValid.SetValue(area, false);
        return area.GetLineStarts(width, style).ToArray();
    }

    /// <summary>
    /// Lines with joiners break where the whole-line walk breaks them: in the platform's monospace
    /// face (rows by count, the joiners' rows searched) and its sans-serif one (every row
    /// searched), and Consolas and Segoe UI on Windows, at random widths and at widths on and a
    /// hair either side of a multiple of the advance.
    /// </summary>
    [Fact]
    public void LinesWithJoiners_BreakWhereTheWholeLineWalkBreaks()
    {
        string text = LongLines();
        var faces = new List<(string Family, float Size)> { (FontFamilies.Monospace, 12f), (FontFamilies.Monospace, 13.333333f), (FontFamilies.SansSerif, 13f) };
        if (OperatingSystem.IsWindows()) faces.AddRange([("Consolas", 12f), ("Segoe UI", 12f)]);
        var rng = new Random(37);
        int compared = 0;
        foreach (var (family, size) in faces)
        {
            using var session = HeadlessSession.Create(900, 600);
            var second = session.CreateWindow(width: 900, height: 600);
            var byRow = TextArea.New(text).FontFamily(family).FontSize(size).FlexGrow(1);
            var wholeLine = TextArea.New(text).FontFamily(family).FontSize(size).FlexGrow(1);
            wholeLine.ClustersByRow = false;
            session.Window.SetRoot(Flex.Column().FlexGrow(1).Padding(8).Children(byRow));
            second.SetRoot(Flex.Column().FlexGrow(1).Padding(8).Children(wholeLine));
            session.PumpFrame();
            var style = byRow.ResolvedInherited;
            float advance = ContextOf(byRow).MonospaceAsciiAdvance(style);
            var widths = new List<float>();
            for (int i = 0; i < 16; i++) widths.Add(20f + (float)rng.NextDouble() * 1_380f);
            if (advance > 0f)
                for (int n = 6; n < 200; n += 23)
                    foreach (float d in new[] { -0.01f, -0.0001f, 0f, 0.0001f, 0.01f })
                        widths.Add(n * advance + d);
            foreach (float width in widths)
            {
                var expected = Rows(wholeLine, width, style);
                var actual = Rows(byRow, width, style);
                Assert.True(expected.AsSpan().SequenceEqual(actual), $"{family} {size} at {width} the rows differ");
                compared++;
            }
        }
        Assert.True(compared >= 3 * 16 + 2 * 9 * 5, $"only {compared} widths compared");
    }

    /// <summary>The frames are the same: where clicks on wrapped rows put the caret, the caret
    /// moved by characters across rows and clusters, a selection dragged by keys and find
    /// highlights over row ends, at three widths.</summary>
    [Fact]
    public void TheFramesOfLinesWithJoiners_AreTheWholeLineWalks_CaretHitsSelectionAndHighlights()
    {
        string text = LongLines();
        var highlights = new List<TextHighlight>();
        for (int at = text.IndexOf("pony", StringComparison.Ordinal); at >= 0; at = text.IndexOf("pony", at + 4, StringComparison.Ordinal))
            highlights.Add(new TextHighlight(at - 5, 9, new Color(255, 200, 0, 255)));
        foreach (int width in new[] { 300, 517, 900 })
        {
            using var session = HeadlessSession.Create(width, 600, options: HeadlessOptions.Golden());
            var second = session.CreateWindow(width: width, height: 600);
            var byRow = TextArea.New(text).FontFamily(FontFamilies.Monospace).FontSize(12f).LineHeight(1.5f).FlexGrow(1);
            var wholeLine = TextArea.New(text).FontFamily(FontFamilies.Monospace).FontSize(12f).LineHeight(1.5f).FlexGrow(1);
            wholeLine.ClustersByRow = false;
            session.Window.SetRoot(Flex.Column().FlexGrow(1).Padding(8).Children(byRow));
            second.SetRoot(Flex.Column().FlexGrow(1).Padding(8).Children(wholeLine));
            session.PumpFrame();
            var carets = new List<int>[2];
            int side = 0;
            foreach (var (window, area) in new[] { (session.Window, byRow), (second, wholeLine) })
            {
                area.Highlights = highlights;
                session.PumpFrame();
                var box = area.WindowContentBox;
                var hits = carets[side++] = [];
                foreach (var (fx, fy) in new[] { (0.1f, 0.05f), (0.97f, 0.1f), (0.5f, 0.3f), (0.99f, 0.55f), (0.02f, 0.8f), (0.6f, 0.95f) })
                {
                    window.Click(box.X + box.Width * fx, box.Y + box.Height * fy);
                    session.PumpFrame();
                    hits.Add(area.CursorPosition);
                }
                for (int i = 0; i < 60; i++) window.PressKey(KeyCode.Right);
                session.PumpFrame();
                hits.Add(area.CursorPosition);
                for (int i = 0; i < 5; i++) window.PressKey(KeyCode.Up, KeyModifiers.Shift);
                session.PumpFrame();
            }
            Assert.Equal(carets[1], carets[0]);
            Assert.True(byRow.HasSelection && byRow.SelectionStart == wholeLine.SelectionStart
                && byRow.SelectionEnd == wholeLine.SelectionEnd, "the two areas' selections differ");
            using var a = session.Window.CapturePixels();
            using var b = second.CapturePixels();
            using var pa = SKBitmap.FromImage(a);
            using var pb = SKBitmap.FromImage(b);
            Assert.True(pa.GetPixelSpan().SequenceEqual(pb.GetPixelSpan()), $"the frames differ at {width} px");
        }
    }

    /// <summary>The win: a key typed into a one-line 89 K character JSON body with one emoji in
    /// it allocates what one typed into the body without the emoji does (191 KB in Consolas,
    /// mostly the text), where listing the whole line's cluster ends at every key took 1.2 MB.</summary>
    [Fact]
    public void AKeyTypedIntoAOneLineBodyWithAnEmoji_AllocatesAsWithoutOne()
    {
        var rng = new Random(41);
        var sb = new StringBuilder("[");
        for (int i = 0; sb.Length < 89_000; i++)
            sb.Append("{\"id\":").Append(1_000 + i).Append(",\"name\":\"item ").Append(rng.Next(100_000)).Append("\",\"tags\":[\"a\",\"b\"]},");
        string plain = sb.Append("{}]").ToString();
        int at = plain.IndexOf("\"name\":\"", plain.Length * 3 / 4, StringComparison.Ordinal) + 8;
        string withEmoji = plain.Insert(at, "\U0001F600 ");
        long PerKey(string body)
        {
            using var session = HeadlessSession.Create(900, 600);
            var area = TextArea.New(body).FontFamily(FontFamilies.Monospace).FontSize(12f).FlexGrow(1);
            session.Window.SetRoot(Flex.Column().FlexGrow(1).Padding(8).Children(area));
            session.PumpFrame();
            Assert.True(ContextOf(area).MonospaceAsciiAdvance(area.ResolvedInherited) > 0f,
                "the platform's monospace face was not proven monospace");
            session.Window.Click(area);
            area.CursorPosition = body.Length / 4;
            session.PumpFrame();
            for (int i = 0; i < 4; i++) { session.Window.TypeText("x"); session.PumpFrame(); }
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 8; i++) { session.Window.TypeText(((char)('a' + i)).ToString()); session.PumpFrame(); }
            long bytes = (GC.GetAllocatedBytesForCurrentThread() - a0) / 8;
            Assert.Equal(body.Length + 12, area.Text.Length);
            return bytes;
        }
        long without = PerKey(plain), with = PerKey(withEmoji);
        Assert.True(with <= without + without / 8 + 32 * 1024,
            $"a key with an emoji in the body took {with / 1024.0:F1} KB, without one {without / 1024.0:F1} KB");
    }
}
