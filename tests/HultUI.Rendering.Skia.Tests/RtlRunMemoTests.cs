using HultUI.Core.Layout;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Right-to-left runs kept whole by the pipeline (docs/plans/19-run-shaping.md, item 3) lay out
/// to the bit as shaping every run again does, in the system's own faces: Hebrew, Arabic with its
/// joining and marks, numbers and Latin inside them, emoji, at several widths, truncated and not,
/// in paragraphs of either direction, and laid out a second time from the memo.
/// </summary>
public class RtlRunMemoTests(ITestOutputHelper output)
{
    private static readonly string[] Texts =
    [
        "שלום עולם, זהו ספר על בית ומחשב וחלון וקובץ ותיקייה ושורה וטקסט ומסך ועכבר",
        "שָׁלוֹם עוֹלָם עִם נִקּוּד וְטַעֲמִים",
        "مرحبا بالعالم، هذا نص عربي طويل يلتف على عدة أسطر عند العرض الضيق",
        "مَرْحَبًا بِالْعَالَمِ مَعَ التَّشْكِيلِ",
        "קובץ readme.md נשמר ב-12:30 עם 1,234 שורות",
        "الملف config.json فيه ١٢٣ سطرا و 456 حرفا",
        "שלום 👍🏽 עולם 🇸🇪 ספר ❤️",
        "Latin first, then עברית באמצע, then العربية too, and back to Latin",
    ];

    private static readonly float[] Widths = [10_000f, 420f, 300f, 220f, 160f, 300f, 10_000f];

    [Fact]
    public void RightToLeftRunsFromTheMemo_LayOutAsShapingThemAgainDoes()
    {
        var fonts = new SkiaFontCollection();
        using var shaperA = new SkiaTextShaper();
        using var shaperB = new SkiaTextShaper();
        int runs = 0;
        foreach (float size in new[] { 13f, 17.5f })
        {
            var reference = new TextPipeline(fonts, shaperA) { MemoizesRtlRuns = false };
            var memo = new TextPipeline(fonts, shaperB);
            foreach (var direction in new[] { TextDirection.Ltr, TextDirection.Rtl })
            {
                var style = new ResolvedInheritedStyle(
                    FontFamily: "sans-serif", FontSize: size, FontWeight: FontWeight.Regular,
                    TextColor: new(0, 0, 0, 255), TextDirection: direction, Cursor: CursorType.Default);
                foreach (float width in Widths)
                    foreach (string text in Texts)
                        foreach (var overflow in new[] { TextOverflow.Clip, TextOverflow.Ellipsis })
                        {
                            int? maxLines = overflow == TextOverflow.Ellipsis ? 1 : null;
                            var a = reference.Layout(text, style, width, maxLines, overflow);
                            var b = memo.Layout(text, style, width, maxLines, overflow);
                            runs += Compare(a, b, $"\"{text}\" {size}px {direction} at {width} ({overflow})");
                        }
            }
            Assert.True(memo.RtlRunCacheUse.Entries > 0, "nothing was kept");
        }
        output.WriteLine($"{runs} runs compared");
    }

    private static int Compare(TextLayout a, TextLayout b, string what)
    {
        Assert.True(a.Size == b.Size, $"{what}: {a.Size} against {b.Size}");
        Assert.Equal(a.Lines.Count, b.Lines.Count);
        int runs = 0;
        for (int l = 0; l < a.Lines.Count; l++)
        {
            var (la, lb) = (a.Lines[l], b.Lines[l]);
            Assert.True(la.Start == lb.Start && la.Length == lb.Length && la.Width == lb.Width, $"{what}, line {l}");
            Assert.Equal(la.Runs.Count, lb.Runs.Count);
            for (int r = 0; r < la.Runs.Count; r++)
            {
                runs++;
                var (ra, rb) = (la.Runs[r], lb.Runs[r]);
                string where = $"{what}, line {l}, run {r} \"{ra.Text}\"";
                Assert.True(ra.Text == rb.Text && ra.Width == rb.Width && ra.OffsetX == rb.OffsetX && ra.Level == rb.Level
                    && ReferenceEquals(ra.Font, rb.Font) && ra.FontSize == rb.FontSize, where);
                Assert.True(ra.Glyphs.AsSpan().SequenceEqual(rb.Glyphs), where);
            }
        }
        return runs;
    }
}
