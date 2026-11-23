using HultUI.Core.Layout;
using HultUI.Core.Styling;
using HultUI.Text;
using HultUI.Text.Tests;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// A layout on the BiDi path is kept for the widths at which its break pass takes the decisions it took, as a plain
/// one is (<see cref="WrapRangeTests"/>; TextPipeline.BidiLayout): inside that range a width is answered with the kept
/// layout and no layout is made, past either end the text is broken again, and at every width of a drag the layout is
/// the reference's (<c>KeepsWrapRanges = false</c>) to the bit, through HarfBuzz and the system's fonts. Right-to-left,
/// Thai, emoji and mixed paragraphs (numbers and brackets inside right to left, Latin runs, marks, a token too long for
/// a line, trailing spaces), in paragraphs of each direction.
/// </summary>
public class BidiWrapRangeTests
{
    private static readonly ResolvedInheritedStyle Sans = new(
        FontFamily: "sans-serif", FontSize: 13f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly ResolvedInheritedStyle[] Styles =
    [
        Sans,
        Sans with { TextDirection = TextDirection.Rtl },
        Sans with { FontFamily = FontFamilies.Monospace, FontSize = 11.5f, TextDirection = TextDirection.Auto },
        Sans with { FontWeight = FontWeight.Bold, FontSize = 15f, LetterSpacing = 0.3f },
    ];

    // Numbers, dates and brackets inside right-to-left text, a Latin run, Hebrew points and Arabic vowel marks, emoji
    // with a skin tone, a flag and a ZWJ family, a hash too long for a line (cut between characters), and trailing spaces.
    private static readonly string[] Mixed =
    [
        "גרסה 2.4.1 (בטא) שוחררה ב-12/03/2026 עם 1,234 תיקונים — ראו את [הערות השחרור] ואת הקובץ CHANGELOG.md לפרטים, כולל 3 שינויים שוברים ועוד 17 תיקוני באגים קטנים.   ",
        "مَرْحَبًا بِالْعَالَمِ! الإصدار 3.0 (تجريبي) يضيف 42 ميزة جديدة، منها {الاتجاه التلقائي} و\"النص المختلط\" مع English words in the middle وأرقام ١٢٣٤٥ عربية في نهاية الفقرة.",
        "שָׁלוֹם עוֹלָם 👋🏽 עם אימוג'י 🇮🇱 ומשפחה 👨‍👩‍👧 בתוך פסקה מימין לשמאל, ומספרים 3.14159 ו-+972 בסוף, ועוד כמה מילים כדי שהפסקה תישבר לכמה שורות.",
        "המזהה של הקומיט הוא 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef והוא ארוך מכדי להיכנס לשורה אחת בחלון צר.",
        "Mixed paragraph starting in English then עברית באמצע ואחר כך العربية أيضاً and back to English with numbers 1234 and (brackets) at the end.",
        // Words that each take a line of their own at the widths below, or more than one: a line whose width is its
        // first word's alone, which only the test at a line's start measures.
        "אבגדהוזחטיכלמנסעפצקרשתאבגדהוזחטיכלמנ 0123456789abcdef0123456789abcdef0123 مرحبابالعالمالجديدوالقديموالمستقبلوالحاضر 0123456789abcdef0123456789abcdef4567",
    ];

    private static readonly string[] Paragraphs = [.. BidiProbeWidthsTests.Paragraphs, .. Mixed];

    private static readonly (int? MaxLines, TextOverflow Overflow)[] Modes =
    [
        (null, TextOverflow.Clip),
        (2, TextOverflow.Clip),
        (2, TextOverflow.Ellipsis),
        (3, TextOverflow.Clip),
    ];

    [Fact]
    public void ABidiParagraph_InsideItsRange_IsAnsweredWithoutALayout()
    {
        foreach (var style in new[] { Styles[1], Styles[0] })
        {
            var pipeline = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
            string paragraph = BidiProbeWidthsTests.Paragraphs[0];   // Arabic, with Latin commands
            var first = pipeline.Layout(paragraph, style, 300f, null);
            Assert.True(first.Lines.Count > 1);
            Assert.True(pipeline.KeptWrapRange(paragraph, style, null, out float from, out float below),
                $"no range kept ({style.TextDirection})");
            Assert.True(from <= 300f && 300f < below, $"[{from:R}, {below:R}) leaves out 300");
            var (layouts, _) = pipeline.LayoutCacheUse;

            // A drag a quarter pixel at a time, as long as the lines stay, twice: no layout is made, nothing is
            // allocated the second time; the one kept is the answer.
            for (int pass = 0; pass < 2; pass++)
            {
                int hits = pipeline.WrapRangeHits, steps = 0;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (float w = 300f - 0.25f; w >= from; w -= 0.25f, steps++)
                    Assert.Same(first, pipeline.Layout(paragraph, style, w, null));
                Assert.Same(first, pipeline.Layout(paragraph, style, MathF.BitDecrement(below), null));
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Equal(hits + steps + 1, pipeline.WrapRangeHits);
                if (pass == 1) Assert.Equal(0, bytes);
            }
            Assert.Equal(layouts, pipeline.LayoutCacheUse.Entries);

            // Past it the text is broken again, and that layout is kept for its own range.
            var narrower = pipeline.Layout(paragraph, style, MathF.BitDecrement(from), null);
            Assert.NotSame(first, narrower);
            Assert.True(pipeline.KeptWrapRange(paragraph, style, null, out _, out float narrowerBelow));
            Assert.True(narrowerBelow <= from);
        }
    }

    [Fact]
    public void ABidiRange_HoldsToItsEnds_AndJustPastEitherEnd_TheTextIsBrokenAgainAsAFreshLayoutIs()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        int ranges = 0, probes = 0;
        foreach (int? maxLines in new int?[] { null, 2 })
        foreach (var style in Styles)
        foreach (float start in new[] { 320f, 233.33f })
        {
            var reference = new TextPipeline(fonts, shaper) { KeepsWrapRanges = false };
            foreach (var text in Paragraphs)
            {
                // A pipeline of its own per text: a range is the latest of its text's.
                var keeps = new TextPipeline(fonts, shaper);
                var first = keeps.Layout(text, style, start, maxLines);
                Assert.True(keeps.KeptWrapRange(text, style, maxLines, out float from, out float below),
                    $"\"{text[..20]}\" lines={maxLines} {style}: no range kept at {start:R}");
                ranges++;
                Assert.True(from <= start && start < below, $"[{from:R}, {below:R}) leaves out {start:R}");
                string at = $"\"{text[..20]}\" [{from:R}, {below:R}) lines={maxLines} {style}";

                // Inside: the kept layout itself, which is the reference's there.
                float mid = float.IsPositiveInfinity(below) ? from + 100f : from + (below - from) / 2f;
                if (mid >= below) mid = from;   // a range an ulp wide
                foreach (float t in new[] { from, MathF.BitDecrement(below), mid })
                {
                    int hits = keeps.WrapRangeHits;
                    var x = keeps.Layout(text, style, t, maxLines);
                    Assert.Same(first, x);
                    Assert.Equal(hits + 1, keeps.WrapRangeHits);
                    LayoutAssert.Same(reference.Layout(text, style, t, maxLines), x, $"{at} at {t:R}");
                    probes++;
                }
                // Just past either end: laid out again, as the reference lays it out.
                foreach (float t in new[] { MathF.BitDecrement(from), below })
                {
                    if (float.IsInfinity(t)) continue;
                    var x = keeps.Layout(text, style, t, maxLines);
                    Assert.NotSame(first, x);
                    LayoutAssert.Same(reference.Layout(text, style, t, maxLines), x, $"{at} past an end at {t:R}");
                    probes++;
                }
            }
        }
        Assert.True(ranges >= 2 * 4 * 2 * 10, $"only {ranges} ranges kept");
        Assert.True(probes > 4 * ranges, $"only {probes} probes");
    }

    [Fact]
    public void MixedDirectionAndEmojiParagraphs_AreTheReferenceLayout_AtEveryWidthOfADrag()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        int compared = 0, kept = 0, wrapped = 0;
        foreach (var style in Styles)
        {
            // Pipelines per style, shared by every mode and text, as an application's is.
            var keeps = new TextPipeline(fonts, shaper);
            var reference = new TextPipeline(fonts, shaper) { KeepsWrapRanges = false };
            foreach (var (maxLines, overflow) in Modes)
            {
                int modeHits = keeps.WrapRangeHits;
                foreach (float width in WrapRangeTests.Drag(320f))
                {
                    int before = keeps.WrapRangeHits;
                    foreach (var text in Paragraphs)
                    {
                        var expected = reference.Layout(text, style, width, maxLines, overflow);
                        var actual = keeps.Layout(text, style, width, maxLines, overflow);
                        LayoutAssert.Same(expected, actual, $"\"{text[..20]}\" w={width:R} lines={maxLines} {overflow} {style}");
                        compared++;
                        if (expected.Lines.Count > 1 && overflow == TextOverflow.Clip) wrapped++;
                    }
                    kept += keeps.WrapRangeHits - before;
                }
                // A truncated layout is neither kept nor answered with a kept one: its ellipsis reads the width.
                if (overflow == TextOverflow.Ellipsis)
                    Assert.Equal(modeHits, keeps.WrapRangeHits);
            }
        }
        Assert.True(compared > 4_000, $"compared only {compared} layouts");
        // Most steps of the drag keep a paragraph's lines; with nothing kept the comparisons above would hold the
        // reference to itself.
        Assert.True(kept * 2 > wrapped, $"kept {kept} of {wrapped} wrapped layouts");
    }

    [Fact]
    public void TheSameTextInAnotherDirection_IsNotAnsweredWithTheOthersRange()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        var reference = new TextPipeline(fonts, shaper) { KeepsWrapRanges = false };
        var pipeline = new TextPipeline(fonts, shaper);
        var rtl = Sans with { TextDirection = TextDirection.Rtl };
        var auto = Sans with { TextDirection = TextDirection.Auto };
        // Hebrew first: on the BiDi path in every direction. Latin: on the plain path but in an Rtl paragraph.
        foreach (var text in new[] { Mixed[0], Mixed[4], WrapRangeTests.Latin[0] })
        {
            var kept = pipeline.Layout(text, rtl, 300f, null);
            Assert.True(pipeline.KeptWrapRange(text, rtl, null, out float from, out float below));
            float inside = from < 300f ? from : MathF.BitDecrement(below);
            foreach (var style in new[] { Sans, auto })
            {
                var other = pipeline.Layout(text, style, inside, null);
                Assert.NotSame(kept, other);
                LayoutAssert.Same(reference.Layout(text, style, inside, null), other, $"\"{text[..20]}\" {style.TextDirection}");
            }
            Assert.Same(kept, pipeline.Layout(text, rtl, inside, null));
        }
    }

    [Fact]
    public void ASizeAsk_AtAWidthLaidOutBefore_PutsTheBidiLayoutsRangeBack_AndALayoutAskDoesNot()
    {
        var pipeline = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        var style = Styles[2];
        string paragraph = BidiProbeWidthsTests.Paragraphs[1];   // Hebrew
        var laidOut = pipeline.Layout(paragraph, style, 640f, null);
        Assert.True(pipeline.KeptWrapRange(paragraph, style, null, out float from, out float below));
        var (_, counted) = pipeline.WrapRangeUse;
        // Sized far from there: the range kept is that width's.
        pipeline.WrappedSize(paragraph, style, 400f, null);
        Assert.True(pipeline.KeptWrapRange(paragraph, style, null, out float sizedFrom, out float sizedBelow));
        Assert.True(sizedBelow <= from, $"[{sizedFrom:R}, {sizedBelow:R}) meets [{from:R}, {below:R})");

        // A layout asked at 640 is the one kept for that width, and leaves the range as it is.
        Assert.Same(laidOut, pipeline.Layout(paragraph, style, 640f, null));
        Assert.True(pipeline.KeptWrapRange(paragraph, style, null, out float f, out float b));
        Assert.Equal((sizedFrom, sizedBelow), (f, b));

        // A size asked there puts the layout's own range back, allocating nothing, and the next width of a drag in it
        // is answered from it, with that layout.
        long before = GC.GetAllocatedBytesForCurrentThread();
        var size = pipeline.WrappedSize(paragraph, style, 640f, null);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(laidOut.Size, size);
        Assert.True(pipeline.KeptWrapRange(paragraph, style, null, out f, out b));
        Assert.Equal((from, below), (f, b));
        Assert.Equal(counted, pipeline.WrapRangeUse.Bytes);
        float inside = from < 640f ? from : MathF.BitDecrement(below);
        Assert.NotEqual(640f, inside);
        int hits = pipeline.WrapRangeHits;
        Assert.Same(laidOut, pipeline.Layout(paragraph, style, inside, null));
        Assert.Equal(hits + 1, pipeline.WrapRangeHits);
    }
}
