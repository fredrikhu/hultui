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
/// A TextArea in a face proven monospace for printable ASCII takes a line of it as fitting,
/// without measuring it, when one character more would fit
/// (<see cref="IRenderContext.MonospaceAsciiAdvance"/>), and breaks a row of a longer one by its
/// count instead of searching measured prefixes. With real fonts: the rows, the caret and
/// the selection are the ones measuring every line gives, at widths that land on a character's
/// edge and a hair either side of one, for lines of every length, with tabs, carriage returns
/// and characters outside printable ASCII among them.
/// </summary>
public class TextAreaFitsByCountTests
{
    private static string Document()
    {
        var sb = new StringBuilder();
        const string ascii = "!\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~ ";
        for (int n = 1; n <= 160; n++)
        {
            for (int i = 0; i < n; i++) sb.Append(ascii[(i * 7 + n) % ascii.Length]);
            if (n % 9 == 0) sb.Append('\t').Append("tab");
            if (n % 13 == 0) sb.Append(" café — 東");
            if (n % 17 == 0) sb.Append('\r');
            sb.Append('\n');
            sb.Append("  \"key").Append(n).Append("\": [1, 2.5, \"=> != -> www fi\", null],\n");
        }
        return sb.ToString();
    }

    // Two windows of one session (one application a process), the same text in each: one area
    // fitting by count, one measuring every line. Pinned: the bundled DejaVu faces, drawn as
    // outlines, the same pixels from draw to draw (system glyph masks can differ between two draws
    // in one process once Skia purges its strikes).
    private static (HeadlessSession Session, HeadlessWindow Second, TextAreaElement Counted, TextAreaElement Measured)
        Open(string text, int width, bool pinned = false)
    {
        var session = HeadlessSession.Create(width, 600, options: pinned ? HeadlessOptions.Golden() : null);
        var second = session.CreateWindow(width: width, height: 600);
        var counted = Area(text, fitsByCount: true);
        var measured = Area(text, fitsByCount: false);
        session.Window.SetRoot(Flex.Column().FlexGrow(1).Padding(8).Children(counted));
        second.SetRoot(Flex.Column().FlexGrow(1).Padding(8).Children(measured));
        session.PumpFrame();
        return (session, second, counted, measured);
    }

    private static TextAreaElement Area(string text, bool fitsByCount)
    {
        var area = TextArea.New(text).FlexGrow(1);
        area.FitsByCount = fitsByCount;
        area.FontFamily(FontFamilies.Monospace).FontSize(12f).LineHeight(1.5f);
        return area;
    }

    private static IRenderContext ContextOf(TextAreaElement area) => (IRenderContext)typeof(TextAreaElement)
        .GetField("_renderCtx", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(area)!;

    private static readonly FieldInfo LinesValid =
        typeof(TextAreaElement).GetField("_linesValid", BindingFlags.NonPublic | BindingFlags.Instance)!;

    // The rows at `width`, wrapped for it: GetLineStarts hands back the rows it has for any width
    // within half a pixel of theirs, so a width a hair from the last would get the last one's.
    private static int[] Rows(TextAreaElement area, float width, ResolvedInheritedStyle style)
    {
        LinesValid.SetValue(area, false);
        return area.GetLineStarts(width, style).ToArray();
    }

    /// <summary>
    /// Lines far longer than a row, as a minified script or a JSON response on one line is: code
    /// of printable ASCII, a run with no space to break at, and one with characters outside
    /// printable ASCII every so often (accents, dashes, arrows, Han, a combining mark, an emoji,
    /// a tab, a carriage return) — rows of which break by count and rows of which are searched.
    /// </summary>
    private static string LongLines()
    {
        var rng = new Random(17);
        var sb = new StringBuilder();
        string[] tokens = ["function ", "return ", "var ", "a", "bC", "x=", "(e,t){", "};", "\"red pony\",", "===", "&&", "!0", "[1,2]", ".push(", ")", "null", "0x1f", " "];
        for (int i = 0; i < 1_500; i++) sb.Append(tokens[rng.Next(tokens.Length)]);
        sb.Append('\n');
        for (int i = 0; i < 1_200; i++) sb.Append((char)('a' + i % 26));
        sb.Append('\n');
        string[] odd = ["é", "—", "→", "東", "x́", char.ConvertFromUtf32(0x1F600), "\t", "ü"];
        for (int i = 0; i < 1_400; i++)
        {
            sb.Append(tokens[rng.Next(tokens.Length)]);
            if (rng.Next(40) == 0) sb.Append(odd[rng.Next(odd.Length)]);
        }
        sb.Append("\r\n");
        for (int i = 0; i < 600; i++) sb.Append(tokens[rng.Next(tokens.Length)]);
        return sb.ToString();
    }

    [Fact]
    public void TheRows_AreTheMeasuredPaths_AtEveryWidth()
    {
        string text = Document();
        var (session, _, counted, measured) = Open(text, 900);
        using var owned = session;
        var style = counted.ResolvedInherited;
        float advance = ContextOf(counted).MonospaceAsciiAdvance(style);
        Assert.True(advance > 0f, "the platform's monospace face was not proven monospace");

        int compared = 0, wrapped = 0;
        foreach (float chars in new[] { 1f, 7f, 20f, 39.5f, 40f, 41f, 64f, 80f, 99f, 100f, 101f, 120f, 159f, 160f, 161f, 200f })
            foreach (float nudge in new[] { -0.01f, 0f, 0.01f })
            {
                float width = chars * advance + nudge;
                var expected = Rows(measured, width, style);
                var actual = Rows(counted, width, style);
                Assert.True(expected.AsSpan().SequenceEqual(actual), $"at {width} ({chars} characters' width {nudge:+0.00;-0.00;0}) the rows differ");
                compared++;
                wrapped += expected.Length;
            }
        Assert.Equal(48, compared);
        Assert.True(wrapped > 48 * 330, $"only {wrapped} rows: too few lines wrapped");
    }

    /// <summary>
    /// Long lines break by count where the measured path's search breaks them: in the platform's
    /// monospace face at four sizes (and Consolas on Windows), at random widths and at widths on
    /// a multiple of the advance and a ten-thousandth to a hundredth of a pixel either side.
    /// </summary>
    [Fact]
    public void LongLines_BreakWhereTheMeasuredPathBreaks_OnAndNearMultiplesOfTheAdvance()
    {
        string text = LongLines() + "\n" + Document();
        var faces = new List<(string Family, float Size)>
        {
            (FontFamilies.Monospace, 12f), (FontFamilies.Monospace, 13f), (FontFamilies.Monospace, 13.333333f), (FontFamilies.Monospace, 10.666667f),
        };
        if (OperatingSystem.IsWindows()) faces.Add(("Consolas", 12f));
        var rng = new Random(23);
        int compared = 0;
        foreach (var (family, size) in faces)
        {
            using var session = HeadlessSession.Create(900, 600);
            var second = session.CreateWindow(width: 900, height: 600);
            var counted = TextArea.New(text).FontFamily(family).FontSize(size).FlexGrow(1);
            var measured = TextArea.New(text).FontFamily(family).FontSize(size).FlexGrow(1);
            measured.FitsByCount = false;
            session.Window.SetRoot(Flex.Column().FlexGrow(1).Padding(8).Children(counted));
            second.SetRoot(Flex.Column().FlexGrow(1).Padding(8).Children(measured));
            session.PumpFrame();
            var style = counted.ResolvedInherited;
            Assert.Equal(size, style.FontSize);
            float advance = ContextOf(counted).MonospaceAsciiAdvance(style);
            Assert.True(advance > 0f, $"{family} {size} was not proven monospace");

            var widths = new List<float>();
            for (int i = 0; i < 12; i++) widths.Add(40f + (float)rng.NextDouble() * 1_360f);
            for (int k = 6; k < 200; k += 17)
                foreach (float d in new[] { -0.01f, -0.001f, -0.0001f, 0f, 0.0001f, 0.001f, 0.01f })
                    widths.Add(k * advance + d);
            foreach (float width in widths)
            {
                var expected = Rows(measured, width, style);
                var actual = Rows(counted, width, style);
                Assert.True(expected.AsSpan().SequenceEqual(actual), $"{family} {size} at {width} ({width / advance} characters' width) the rows differ");
                compared++;
            }
        }
        Assert.Equal(faces.Count * (12 + 12 * 7), compared);
    }

    /// <summary>The win: a long line of printable ASCII wrapped by count allocates a fraction of
    /// what the measured path's search does, which builds a prefix table for every row and reads
    /// the words its probes cut off their cuts.</summary>
    [Fact]
    public void ALongAsciiLine_WrapsByCount_WithoutShapingIt()
    {
        var rng = new Random(29);
        var sb = new StringBuilder();
        string[] tokens = ["function ", "return ", "var ", "a", "bC", "x=", "(e,t){", "};", "\"red pony\",", "===", "&&", "!0", "[1,2]", ".push("];
        while (sb.Length < 100_000) sb.Append(tokens[rng.Next(tokens.Length)]);
        string text = sb.ToString();
        var (session, _, counted, measured) = Open(text, 900);
        using var owned = session;
        var style = counted.ResolvedInherited;
        float advance = ContextOf(counted).MonospaceAsciiAdvance(style);
        Assert.True(advance > 0f, "the platform's monospace face was not proven monospace");
        float width = 70.5f * advance;   // half a character from a multiple: every row by count

        // The counted area first, while the prefixes and words at this width are not cached: a
        // search there would pay for them (1,074 KB, and 75 KB since its tables read the words they
        // cut off cuts made once, plan 17 round 13), where breaking by count costs the rows (16 KB).
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        var byCount = Rows(counted, width, style);
        long countedBytes = GC.GetAllocatedBytesForCurrentThread() - a0;
        a0 = GC.GetAllocatedBytesForCurrentThread();
        var searched = Rows(measured, width, style);
        long measuredBytes = GC.GetAllocatedBytesForCurrentThread() - a0;

        Assert.True(searched.AsSpan().SequenceEqual(byCount), "the rows differ");
        Assert.True(byCount.Length > 1_400, $"only {byCount.Length} rows");
        Assert.True(countedBytes < 64 * 1024 && countedBytes * 3 < measuredBytes,
            $"by count {countedBytes / 1024.0:F1} KB, searched {measuredBytes / 1024.0:F1} KB: not a third");
    }

    /// <summary>The frames are the same: the rows, the caret and a selection over several
    /// rows, at three widths, one of them a character's edge.</summary>
    [Fact]
    public void TheFrames_AreTheMeasuredPaths()
    {
        string text = Document();
        foreach (int width in new[] { 300, 517, 900 })
        {
            var (session, second, counted, measured) = Open(text, width, pinned: true);
            using var owned = session;
            Assert.True(ContextOf(counted).MonospaceAsciiAdvance(counted.ResolvedInherited) > 0f,
                "the pinned monospace face was not proven monospace");
            foreach (var (window, area) in new[] { (session.Window, counted), (second, measured) })
            {
                window.Click(area);
                session.PumpFrame();
                area.CursorPosition = text.IndexOf('\n', 2_000) + 5;
                session.PumpFrame();
                for (int i = 0; i < 4; i++) window.PressKey(KeyCode.Down, KeyModifiers.Shift);
                session.PumpFrame();
            }
            Assert.True(counted.HasSelection && counted.SelectionStart == measured.SelectionStart
                && counted.SelectionEnd == measured.SelectionEnd, "the two areas' selections differ");
            using var a = session.Window.CapturePixels();
            using var b = second.CapturePixels();
            using var pa = SKBitmap.FromImage(a);
            using var pb = SKBitmap.FromImage(b);
            Assert.True(pa.GetPixelSpan().SequenceEqual(pb.GetPixelSpan()), $"the frames differ at {width} px");
        }
    }

    /// <summary>Long lines broken by count draw as the measured path's: where a click on a
    /// wrapped row puts the caret, a selection dragged by keys across rows, and find highlights
    /// over row ends, at three widths, one of them a character's edge.</summary>
    [Fact]
    public void TheFramesOfLongLines_AreTheMeasuredPaths_CaretHitsSelectionAndHighlights()
    {
        string text = LongLines();
        var highlights = new List<TextHighlight>();
        for (int at = text.IndexOf("pony", StringComparison.Ordinal); at >= 0; at = text.IndexOf("pony", at + 4, StringComparison.Ordinal))
            highlights.Add(new TextHighlight(at - 5, 9, new Color(255, 200, 0, 255)));
        foreach (int width in new[] { 300, 517, 900 })
        {
            var (session, second, counted, measured) = Open(text, width, pinned: true);
            using var owned = session;
            Assert.True(ContextOf(counted).MonospaceAsciiAdvance(counted.ResolvedInherited) > 0f,
                "the pinned monospace face was not proven monospace");
            var carets = new List<int>[2];
            int side = 0;
            foreach (var (window, area) in new[] { (session.Window, counted), (second, measured) })
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
                for (int i = 0; i < 5; i++) window.PressKey(KeyCode.Up, KeyModifiers.Shift);
                session.PumpFrame();
            }
            Assert.Equal(carets[1], carets[0]);
            Assert.True(counted.HasSelection && counted.SelectionStart == measured.SelectionStart
                && counted.SelectionEnd == measured.SelectionEnd, "the two areas' selections differ");
            using var a = session.Window.CapturePixels();
            using var b = second.CapturePixels();
            using var pa = SKBitmap.FromImage(a);
            using var pb = SKBitmap.FromImage(b);
            Assert.True(pa.GetPixelSpan().SequenceEqual(pb.GetPixelSpan()), $"the frames differ at {width} px");
        }
    }
}
