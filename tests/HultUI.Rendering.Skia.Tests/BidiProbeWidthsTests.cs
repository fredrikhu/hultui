using HultUI.Core.Layout;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A TextArea searches a line that no prefix table covers (BiDi text: right to left, emoji, Thai,
/// marks) one probe at a time, each the width of a prefix of the line on one unbounded line
/// (<see cref="TextPipeline.MeasureWidth"/>). Such a line never breaks, so the BiDi path builds it
/// without finding ICU's breaks and measuring the segments between them: the widths and lines are
/// those of the loop it skips, which runs at any finite width (float.MaxValue here), to the bit.
/// </summary>
public class BidiProbeWidthsTests
{
    private static readonly ResolvedInheritedStyle Sans = new(
        FontFamily: "sans-serif", FontSize: 13f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static string Cp(int codepoint) => char.ConvertFromUtf32(codepoint);

    private static string Thrice(string line) => string.Join(' ', line, line, line);

    // Arabic, Hebrew and Thai as a translated README has them (with Latin commands among them), and
    // English with emoji, a ZWJ family and a flag.
    public static readonly string[] Paragraphs =
    [
        Thrice("يشرح هذا الملف كيف تبني التطبيق وتشغله على ويندوز ولينكس، مع أمثلة للأوامر مثل dotnet build و dotnet test، وإعدادات الخطوط والألوان واتجاه النص من اليمين إلى اليسار في نافذة يتغير عرضها."),
        Thrice("הקובץ הזה מסביר איך לבנות את היישום ולהריץ אותו, עם דוגמאות לפקודות כמו dotnet run ולהגדרות של גופנים, צבעים וכיוון הטקסט מימין לשמאל בחלון שרוחבו משתנה."),
        Thrice("ไฟล์นี้อธิบายวิธีสร้างและเรียกใช้แอปพลิเคชันบนวินโดวส์และลินุกซ์ พร้อมตัวอย่างคำสั่งเช่น dotnet build และการตั้งค่าแบบอักษร สี และทิศทางของข้อความในหน้าต่างที่เปลี่ยนความกว้าง"),
        Thrice("Release notes " + Cp(0x1F680) + " faster text, " + Cp(0x2728) + " new themes, " + Cp(0x1F41B)
            + " the caret on wrapped lines fixed, " + Cp(0x1F468) + Cp(0x200D) + Cp(0x1F469) + Cp(0x200D) + Cp(0x1F467)
            + " for families and " + Cp(0x1F1F8) + Cp(0x1F1EA) + " for flags, " + Cp(0x2764) + Cp(0xFE0F) + " to everyone who tested."),
    ];

    private static readonly float[] WrapWidths = [110f, 150f, 190f, 240f, 310f];

    [Fact]
    public void TheProbesOfAWrappedBidiLine_AreTheLoopsWidthsAndLines_ToTheBit()
    {
        var pipeline = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        var reference = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        var styles = new[] { Sans, Sans with { FontSize = 15f }, Sans with { TextDirection = TextDirection.Rtl },
            Sans with { LetterSpacing = 0.5f } };
        int compared = 0;
        foreach (var style in styles)
        foreach (string text in Paragraphs)
        foreach (float wrapWidth in WrapWidths)
        {
            var probes = new List<(int Start, int Chars)>();
            Wrap(text, wrapWidth, (start, chars) =>
            {
                probes.Add((start, chars));
                return pipeline.MeasureWidth(text.AsSpan(start, chars), style);
            });
            foreach (var (start, chars) in probes)
            {
                string probe = text.Substring(start, chars);
                float width = pipeline.MeasureWidth(probe, style);
                var unbounded = pipeline.Layout(probe, style, float.PositiveInfinity, maxLines: 1);
                var looped = reference.Layout(probe, style, float.MaxValue, maxLines: 1);
                AssertSame(looped, unbounded, $"'{probe}' in {style.FontSize} {style.TextDirection}");
                Assert.Equal(BitConverter.SingleToInt32Bits(looped.Size.Width), BitConverter.SingleToInt32Bits(width));
                compared++;
            }
        }
        Assert.True(compared > 1_000, $"only {compared} probes compared");
    }

    [Fact]
    public void AnUnboundedBidiLine_IsTheLoopsLine_WithAndWithoutALineLimit()
    {
        var pipeline = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        var reference = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        foreach (string text in Paragraphs)
        foreach (int? maxLines in new int?[] { null, 1, 0 })
        foreach (var overflow in new[] { TextOverflow.Clip, TextOverflow.Ellipsis })
        {
            // Twice: the second layout of a string is made from its memo.
            for (int pass = 0; pass < 2; pass++)
                AssertSame(reference.Layout(text, Sans, float.MaxValue, maxLines, overflow),
                    pipeline.Layout(text, Sans, float.PositiveInfinity, maxLines, overflow), $"{maxLines} lines, {overflow}, pass {pass}");
        }
        // A newline is a break the loop takes.
        string twoLines = Paragraphs[0] + "\n" + Paragraphs[1];
        var two = pipeline.Layout(twoLines, Sans, float.PositiveInfinity, maxLines: null);
        Assert.Equal(2, two.Lines.Count);
        AssertSame(reference.Layout(twoLines, Sans, float.MaxValue, maxLines: null), two, "two lines");
    }

    [Fact]
    public void ThePrefixesOfAnUnboundedBidiLine_ShapeTheLineOnly()
    {
        // The loop shaped each segment between ICU's breaks to measure it, then the line: measured
        // on Windows, 671 shaper calls against the loop's 2,854.
        var shaper = new CountingShaper(new SkiaTextShaper());
        var loopShaper = new CountingShaper(new SkiaTextShaper());
        var pipeline = new TextPipeline(new SkiaFontCollection(), shaper);
        var reference = new TextPipeline(new SkiaFontCollection(), loopShaper);
        foreach (string text in Paragraphs)
        foreach (float wrapWidth in WrapWidths)
        {
            Wrap(text, wrapWidth, (start, chars) => pipeline.MeasureWidth(text.AsSpan(start, chars), Sans));
            Wrap(text, wrapWidth, (start, chars) => reference.Layout(text.Substring(start, chars), Sans, float.MaxValue, 1).Size.Width);
        }
        Assert.True(shaper.Calls * 2 < loopShaper.Calls,$"{shaper.Calls} shaper calls against the loop's {loopShaper.Calls}");

        // Searched again at the same widths, every probe is found in the width cache.
        int calls = shaper.Calls, entries = pipeline.WidthCacheUse.Entries;
        foreach (string text in Paragraphs)
        foreach (float wrapWidth in WrapWidths)
            Wrap(text, wrapWidth, (start, chars) => pipeline.MeasureWidth(text.AsSpan(start, chars), Sans));
        Assert.Equal(calls, shaper.Calls);
        Assert.Equal(entries, pipeline.WidthCacheUse.Entries);
        Assert.True(entries > 200, $"only {entries} widths kept");
    }

    private static void AssertSame(TextLayout expected, TextLayout actual, string what)
    {
        Assert.True(BitConverter.SingleToInt32Bits(expected.Size.Width) == BitConverter.SingleToInt32Bits(actual.Size.Width)
                    && BitConverter.SingleToInt32Bits(expected.Size.Height) == BitConverter.SingleToInt32Bits(actual.Size.Height),
            $"{what}: {actual.Size} where the loop gives {expected.Size}");
        Assert.Equal(expected.IsRtlParagraph, actual.IsRtlParagraph);
        Assert.Equal(expected.Lines.Count, actual.Lines.Count);
        for (int i = 0; i < expected.Lines.Count; i++)
        {
            var (e, a) = (expected.Lines[i], actual.Lines[i]);
            Assert.Equal((e.Start, e.Length, e.DrawnLength), (a.Start, a.Length, a.DrawnLength));
            Assert.Equal(BitConverter.SingleToInt32Bits(e.Width), BitConverter.SingleToInt32Bits(a.Width));
            Assert.Equal(BitConverter.SingleToInt32Bits(e.OffsetY), BitConverter.SingleToInt32Bits(a.OffsetY));
            Assert.Equal(e.Runs.Count, a.Runs.Count);
            for (int r = 0; r < e.Runs.Count; r++)
            {
                Assert.Equal(e.Runs[r].Text, a.Runs[r].Text);
                Assert.Equal(BitConverter.SingleToInt32Bits(e.Runs[r].Width), BitConverter.SingleToInt32Bits(a.Runs[r].Width));
                Assert.Equal(e.Runs[r].Glyphs, a.Runs[r].Glyphs);
            }
        }
    }

    // The row starts of `text` wrapped at `wrapWidth` as TextAreaElement.FindLineBreak breaks a line
    // without a prefix table: a guess at the row's length in code points (the TextArea counts
    // clusters), then probes around it, each measured by `measure(start, chars)`, broken at a space
    // where there is one.
    private static List<int> Wrap(string text, float wrapWidth, Func<int, int, float> measure)
    {
        var rows = new List<int> { 0 };
        float charWidth = Sans.FontSize * 0.55f;
        for (int start = 0; start < text.Length;)
        {
            int end = FindLineBreak(text, start, wrapWidth, ref charWidth, measure);
            if (end >= text.Length) break;
            rows.Add(end);
            start = end;
        }
        return rows;
    }

    private static int FindLineBreak(string text, int start, float wrapWidth, ref float charWidth,
        Func<int, int, float> measure)
    {
        // ends[k]: where the first k code points from start end.
        var ends = new List<int> { start };
        for (int i = start; i < text.Length; i += char.IsSurrogatePair(text, i) ? 2 : 1)
            ends.Add(i + (char.IsSurrogatePair(text, i) ? 2 : 1));
        int count = ends.Count - 1;
        int lo = 1, hi = int.MaxValue;
        bool bounded = false;
        float loWidth = 0;
        int probe = Math.Min(Math.Max((int)(wrapWidth / Math.Max(charWidth, 0.01f)), 1), count);
        int step = 0;
        for (int round = 0; ; round++)
        {
            int end = ends[probe];
            float width = measure(start, end - start);
            if (probe >= 8) charWidth = width / probe;
            if (width <= wrapWidth)
            {
                if (probe == count) return text.Length;
                lo = probe;
                loWidth = width;
            }
            else
            {
                hi = probe - 1;
                bounded = true;
            }
            if (lo >= hi) break;
            int predicted = lo + (int)((wrapWidth - loWidth) / Math.Max(charWidth, 0.01f));
            if (!bounded)
            {
                probe = Math.Min(Math.Max(predicted + 1, lo + 1) + step, count);
                step = step == 0 ? Math.Max(1, lo / 8) : step * 2;
            }
            else
                probe = round % 2 == 0 ? Math.Clamp(predicted, lo + 1, hi) : (lo + hi + 1) / 2;
        }
        int breakAt = ends[lo];
        while (breakAt > start && !char.IsWhiteSpace(text[breakAt - 1]))
            breakAt--;
        return breakAt > start ? breakAt : ends[lo];
    }

    // Counts the calls into a shaper.
    private sealed class CountingShaper(SkiaTextShaper inner) : ITextShaper, IShapesWithCuts
    {
        public int Calls;

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            Calls++;
            return inner.Shape(text, font, fontSize, rtl);
        }

        ShapedRun IShapesWithCuts.Shape(string text, IResolvedFont font, float fontSize, out RunCuts? cuts)
        {
            Calls++;
            return ((IShapesWithCuts)inner).Shape(text, font, fontSize, out cuts);
        }
    }
}
