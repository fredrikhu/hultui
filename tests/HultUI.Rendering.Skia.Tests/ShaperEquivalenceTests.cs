using System.Globalization;
using System.Text;
using HultUI.Core.Layout;
using HultUI.Core.Styling;
using HultUI.Testing;
using HultUI.Text;
using HultUI.Text.Tests;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Xunit;
using Xunit.Abstractions;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// <see cref="SkiaTextShaper"/> drives HarfBuzz itself, with one buffer and one HarfBuzz font per
/// face kept between calls, where it used to go through SkiaSharp's <see cref="SKShaper"/> and
/// build everything per call (plan 17, round 2). It must hand back what SKShaper did, to the bit:
/// every glyph id, X, Y and advance, and every run's width, compared as raw float bits against
/// <see cref="SkShaperReference"/>, the old shaper kept here.
///
/// <para>Two passes over each corpus: every run a <see cref="TextPipeline"/> shapes while laying
/// the texts out (plain and BiDi paths, styles, letter spacing, wrapping, ellipsis, styled spans,
/// the fallback faces it picks), and then every text whole, in every face the first pass reached,
/// in both directions, at sizes from negative to large — all on one shaper, so a call that left
/// state behind for the next shows. The fonts are the system's, so the corpus covers what this
/// machine has: run it on Windows and on Linux.</para>
/// </summary>
public class ShaperEquivalenceTests(ITestOutputHelper output)
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: FontFamilies.SansSerif, FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly ResolvedInheritedStyle[] Styles =
    [
        Base,
        Base with { LetterSpacing = 0.6f },
        Base with { FontSize = 13f, FontWeight = FontWeight.Bold },
        Base with { FontSize = 12.5f, FontSlant = FontSlant.Italic },
        Base with { FontFamily = FontFamilies.Monospace },
        Base with { FontFamily = "serif", FontSize = 9.5f },
        Base with { TextDirection = TextDirection.Rtl },
        Base with { TextDirection = TextDirection.Auto, FontSize = 21.333334f },
    ];

    private static readonly float[] Sizes = [-1f, 0f, 1f, 7.5f, 12f, 13.333333f, 16f, 96f, float.NaN];

    /// <summary>A string from code points; a surrogate code point becomes that lone UTF-16 unit.</summary>
    private static string Cp(params int[] codePoints) =>
        string.Concat(codePoints.Select(c => c is >= 0xD800 and <= 0xDFFF ? ((char)c).ToString() : char.ConvertFromUtf32(c)));

    internal static readonly string[] Corpus =
    [
        // Latin: ligatures, kerning pairs, digits, punctuation, spaces, a tab.
        "Office affine fluffy fjord ffi ffl fi fl",
        "AVATAR Wave To Ty Yo LT P. V. W.",
        "The quick brown fox jumps over the lazy dog.",
        "0123456789 1,234.56 3/4 1st 2nd -42 +7",
        "(12) [34] {56} <78> \"quoted\" 'single'",
        "a\tb",
        "x",
        " ",
        "  leading and trailing  ",
        // Greek, Cyrillic, Vietnamese stacked marks, Georgian, Armenian, Ethiopic.
        "Ἀθῆναι καὶ Σπάρτη",
        "Привет, мир! Ёжик",
        "Tiếng Việt có dấu: ở ữ ặ ẫ",
        "ქართული ენა",
        "Հայերեն լեզու",
        "ኢትዮጵያ",
        // Combining marks, decomposed: acute, diaeresis + dot below, double underline, a pile, a
        // mark with nothing to sit on.
        Cp('e', 0x301, ' ', 'a', 0x308, 0x323, ' ', 'o', 0x332, 0x332),
        Cp('Z', 0x334, 0x322, 0x31B, 0x356, 'a', 0x337, 'l', 0x338, 'g', 0x335, 'o', 0x36D, 0x36E),
        Cp(0x301, 'x', ' ', 'i', 0x307, 0x328),
        // Hebrew with points; Arabic with harakat, lam-alef, Arabic-Indic digits; Persian, Urdu.
        "שלום עולם",
        "שָׁלוֹם בְּרֵאשִׁית",
        "עברית 123 (טקסט)",
        "commit שלום merged",
        "مرحبا بالعالم",
        "مَرْحَبًا بِكُمْ",
        "لا إله ﻻ",
        "العربية ١٢٣٤ (نص)",
        "فارسی گچپژ",
        "اردو زبان",
        // Neutrals only: what an RTL run of punctuation is.
        "!!! ... ?",
        "(1) - [2]",
        // Indic: Devanagari conjuncts and reph, Bengali, Tamil, Telugu, Gujarati, Gurmukhi,
        // Malayalam, Kannada, Sinhala.
        "नमस्ते दुनिया",
        "क्षत्रिय हिन्दी द्व्यक्ष र्क",
        "বাংলা ভাষা",
        "தமிழ் மொழி",
        "తెలుగు",
        "ગુજરાતી",
        "ਪੰਜਾਬੀ",
        "മലയാളം",
        "ಕನ್ನಡ",
        "සිංහල",
        // Southeast Asian: Thai stacked marks, Lao, Khmer coeng, Myanmar medials.
        "สวัสดีครับ",
        "น้ำ ที่ ปั้น ภาษาไทย",
        "ພາສາລາວ",
        "ភាសាខ្មែរ ស្រី",
        "မြန်မာဘာသာ ကျွန်ုပ်",
        // Tibetan stacks.
        "བོད་ཡིག",
        "སྐད་ཡིག་ ཨོཾ་མ་ཎི་པདྨེ་ཧཱུྃ",
        // Hangul syllables and conjoining jamo (한글 and 각 decomposed), compatibility jamo.
        "한국어 텍스트",
        Cp(0x1112, 0x1161, 0x11AB, 0x1100, 0x1173, 0x11AF),
        Cp(0x1100, 0x1161, 0x11A8),
        "ㅎㅏㄴ",
        // Emoji: ZWJ family, flags, rainbow flag, VS16 and VS15, skin tone, keycap, in text.
        Cp(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467, 0x200D, 0x1F466),
        Cp(0x1F1F8, 0x1F1EA, 0x1F1EF, 0x1F1F5),
        Cp(0x1F3F3, 0xFE0F, 0x200D, 0x1F308),
        Cp(0x2764, 0xFE0F, ' ', 0x2764, 0xFE0E, ' ', 0x263A, 0xFE0F),
        Cp(0x1F44D, 0x1F3FD, ' ', 0x1F44B, 0x1F3FF),
        Cp('1', 0xFE0F, 0x20E3, ' ', '#', 0xFE0F, 0x20E3),
        Cp('e', 'm', 'o', 'j', 'i', ' ', 0x1F600, ' ', 'h', 'e', 'r', 'e'),
        // Invisibles: ZWSP, ZWNJ, ZWJ, soft hyphen, isolates, NUL, BOM, replacement char,
        // private use, a noncharacter.
        Cp('a', 0x200B, 'b', ' ', 'a', 0x200C, 'b', ' ', 'a', 0x200D, 'b'),
        Cp('a', 0xAD, 'b', ' ', 0x2066, 'x', 0x2069),
        Cp('a', 0, 'b', ' ', 0xFEFF, 'c', ' ', 0xFFFD),
        Cp(0xE000, ' ', 0xFFFF),
        // Lone surrogates: high alone, low alone, high before a pair, at the end, reversed pair.
        Cp(0xD83D),
        Cp('a', 0xDE00, 'b'),
        Cp(0xD83D, 0xD83D, 0xDE00),
        Cp('e', 'n', 'd', 0xD83D),
        Cp(0xDE00, 0xD83D),
        // Supplementary letters: mathematical bold.
        Cp(0x1D400, 0x1D401, 0x1D402, ' ', 0x1D7CE),
        // Scripts mixed in one string.
        "Hello שלום مرحبا नमस्ते สวัสดี Привет",
    ];

    internal static readonly string[] CjkCorpus =
    [
        "東",
        "東京に行きます",
        "日本語のテキストを表示",
        "中文文本测试，标点符号。",
        "漢字かなカナ 說",
        "ｶﾀｶﾅ ＡＢＣ　全角スペース",
        "「引用」『二重』【括弧】",
        "Mixed English — 東京 → ∞ and 100 € £ ¥",
        "한국어와 日本語 東",
    ];

    [Fact]
    public void EveryRunOfTheCorpus_IsSKShapersToTheBit()
    {
        var stats = CompareCorpus(Corpus);

        Assert.True(stats.Calls > 20_000, $"compared only {stats.Calls} shaper calls");
        Assert.True(stats.RtlCalls > 2_000, $"only {stats.RtlCalls} right-to-left calls");
        Assert.True(stats.Faces >= 3, $"the corpus reached only {stats.Faces} faces");
        // Marks placed off the baseline: HarfBuzz's y offsets reached the positions compared.
        Assert.True(stats.RaisedGlyphs > 100, $"only {stats.RaisedGlyphs} glyphs off the baseline");
    }

    [HanFontsFact]
    public void EveryRunOfTheCjkCorpus_IsSKShapersToTheBit()
    {
        var stats = CompareCorpus(CjkCorpus);

        Assert.True(stats.Calls > 2_000, $"compared only {stats.Calls} shaper calls");
        // 東 reached a face that has it, so the comparison is of real glyphs.
        using var shaper = new SkiaTextShaper();
        var layout = new TextPipeline(new SkiaFontCollection(), shaper).Layout("東", Base, float.PositiveInfinity, null);
        var run = Assert.Single(Assert.Single(layout.Lines).Runs);
        Assert.NotEqual(0u, Assert.Single(run.Glyphs).GlyphId);
    }

    /// <summary>
    /// The comparisons above are only as good as the glyphs they compare. A machine without a
    /// script's fonts shapes its texts to missing glyphs, which both shapers agree on easily:
    /// that is all WSL Fedora compared for the Indic, Thai and emoji texts until it had Noto's
    /// faces. So wherever some installed face covers every character of a corpus text (as the
    /// OS's font manager answers, not the collection under test), laying it out must reach those
    /// faces: not one missing glyph. A text whose characters no face covers is left to the
    /// machine, and its count is printed.
    /// </summary>
    [Fact]
    public void WhereAFaceCoversAText_ItsRunsHaveNoMissingGlyphs()
    {
        var fonts = new SkiaFontCollection();
        var primary = fonts.Resolve(Base.FontFamily, Base.FontWeight, Base.FontSlant);
        using var shaper = new SkiaTextShaper();
        var pipeline = new TextPipeline(fonts, shaper);
        int covered = 0, uncovered = 0;
        foreach (var text in Corpus.Concat(CjkCorpus))
        {
            // What a face is expected to draw. Format characters (ZWJ, isolates, BOM) and
            // variation selectors are shaped away, so they are not asked about; a control, a
            // private-use or an unassigned code point is the missing glyph by design, so such a
            // text is not this test's.
            bool coverable = true;
            foreach (var rune in text.EnumerateRunes())
            {
                var category = Rune.GetUnicodeCategory(rune);
                if (category is UnicodeCategory.Control or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
                    coverable = false;
                else if (category is UnicodeCategory.Format || rune.Value is >= 0xFE00 and <= 0xFE0F)
                    continue;
                // Asked of the OS's font manager directly, not through the collection under test.
                // Its typefaces are shared, so never disposed here.
                else if (!primary.HasGlyph(rune.Value) && SKFontManager.Default.MatchCharacter(rune.Value) is null)
                    coverable = false;
            }
            if (!coverable)
            {
                uncovered++;
                output.WriteLine($"  no face for all of: {Escape(text)}");
                continue;
            }

            covered++;
            var runs = pipeline.Layout(text, Base, float.PositiveInfinity, null).Lines.SelectMany(l => l.Runs).ToArray();
            int missing = runs.Sum(r => r.Glyphs.Count(g => g.GlyphId == 0));
            Assert.True(missing == 0, $"{missing} missing glyphs in {Escape(text)}, laid out in "
                + string.Join(" + ", runs.Select(r => r.Font.FamilyName).Distinct()));
        }
        output.WriteLine($"{covered} texts covered by the installed faces, {uncovered} not");
        // Latin, Greek, Cyrillic and the neutrals are on any machine with a font at all.
        Assert.True(covered >= 15, $"only {covered} corpus texts had faces for every character");
    }

    /// <summary>Texts past the length the kept buffer takes shape in a buffer of their own, and
    /// the kept one goes on as before.</summary>
    [Fact]
    public void LongTexts_AndTheShortOnesAfterThem_AreSKShapers()
    {
        var fonts = new SkiaFontCollection();
        var font = fonts.Resolve(FontFamilies.SansSerif, FontWeight.Regular, FontSlant.Normal);
        using var shaper = new SkiaTextShaper();
        using var reference = new SkShaperReference();
        string unit = "Office affine " + "مرحبا " + Cp('e', 0x301) + " ";
        foreach (int length in new[] { 4095, 4096, 4097, 9000 })
        {
            string text = string.Concat(Enumerable.Repeat(unit, length / unit.Length + 1))[..length];
            foreach (bool rtl in new[] { false, true })
            {
                Same(reference.Shape(text, font, 15f, rtl), shaper.Shape(text, font, 15f, rtl), $"length {length} rtl={rtl}");
                Same(reference.Shape("affine", font, 15f, rtl), shaper.Shape("affine", font, 15f, rtl), $"after {length} rtl={rtl}");
            }
        }
    }

    /// <summary>
    /// What the change is for: a call allocates the run and its glyph array, nothing else — no
    /// buffer, font, paint, UTF-8 copy or result arrays. SKShaper's way took 1,960 B for this
    /// text against 400 B now, and a shaper that never kept its buffer would show here.
    /// </summary>
    [Fact]
    public void AShapeCall_AllocatesItsRunAndGlyphsOnly()
    {
        var fonts = new SkiaFontCollection();
        var font = fonts.Resolve(FontFamilies.SansSerif, FontWeight.Regular, FontSlant.Normal);
        using var shaper = new SkiaTextShaper();
        const string text = "Allocation, measured";
        var run = shaper.Shape(text, font, 14f);
        for (int i = 0; i < 200; i++)
        {
            shaper.Shape(text, font, 14f);
            shaper.Shape(text, font, 14f, rtl: true);
        }

        // ShapedRun: object header and method table, 3 references, 3 floats and a byte (56 B);
        // the glyph array: header, method table and length (24 B), 16 B a glyph.
        long expected = 56 + 24 + 16L * run.Glyphs.Length;

        int call = 0;
        long perCall = (long)Allocations.PerCall(1_000, () =>
        {
            shaper.Shape(text, font, call % 2 == 0 ? 14f : 15f, rtl: call % 3 == 0);
            call++;
        }, goal: expected);

        Assert.True(perCall <= expected, $"{perCall} B a call, expected {expected} B ({run.Glyphs.Length} glyphs)");
    }

    /// <summary>
    /// Text services are for one thread, but TextMeasure's context is process-wide: a call that
    /// finds the kept buffer in use shapes with its own, so calls from many threads at once still
    /// shape what one thread does.
    /// </summary>
    [Fact]
    public void CallsFromManyThreadsAtOnce_ShapeWhatOneThreadDoes()
    {
        var fonts = new SkiaFontCollection();
        var faces = new[]
        {
            fonts.Resolve(FontFamilies.SansSerif, FontWeight.Regular, FontSlant.Normal),
            fonts.Resolve(FontFamilies.SansSerif, FontWeight.Bold, FontSlant.Normal),
            fonts.Resolve(FontFamilies.Monospace, FontWeight.Regular, FontSlant.Normal),
        };
        var texts = Corpus.Take(30).ToArray();
        using var reference = new SkShaperReference();
        var expected = new ShapedRun[texts.Length, faces.Length, 2];
        for (int t = 0; t < texts.Length; t++)
        for (int f = 0; f < faces.Length; f++)
        for (int d = 0; d < 2; d++)
            expected[t, f, d] = reference.Shape(texts[t], faces[f], 14f, rtl: d == 1);

        using var shaper = new SkiaTextShaper();
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, worker =>
        {
            for (int k = 0; k < 3_000; k++)
            {
                int t = (worker * 7 + k) % texts.Length, f = (worker + k) % faces.Length, d = (k / 3) % 2;
                Same(expected[t, f, d], shaper.Shape(texts[t], faces[f], 14f, rtl: d == 1),
                    $"worker {worker} call {k} \"{texts[t]}\" rtl={d == 1}");
            }
        });
    }

    private (int Calls, int RtlCalls, int Faces, int RaisedGlyphs) CompareCorpus(string[] texts)
    {
        var fonts = new SkiaFontCollection();
        using var subject = new SkiaTextShaper();
        using var reference = new SkShaperReference();
        var comparing = new ComparingShaper(subject, reference);

        // Pass 1: what the pipeline shapes laying the corpus out.
        var pipeline = new TextPipeline(fonts, comparing);
        foreach (var style in Styles)
        foreach (var text in texts)
        {
            pipeline.Layout(text, style, float.PositiveInfinity, null);
            pipeline.Layout(text, style, 60f, null);
            pipeline.Layout(text, style, 23f, 3, TextOverflow.Ellipsis);
            pipeline.Layout(text, style, 41f, 1, TextOverflow.Ellipsis);
            foreach (var spans in TestSpans.Variants(text))
                pipeline.Layout(text, style, 55f, null, TextOverflow.Clip, spans);
        }
        int pipelineCalls = comparing.Calls;

        // Pass 2: every text whole, in every face pass 1 reached, both ways, at every size.
        var faces = comparing.Fonts.ToArray();
        foreach (var text in texts)
        foreach (var face in faces)
        foreach (float size in Sizes)
        foreach (bool rtl in new[] { false, true })
            comparing.Shape(text, face, size, rtl);

        output.WriteLine($"{pipelineCalls} calls from layouts, {comparing.Calls - pipelineCalls} whole texts; "
            + $"{comparing.RtlCalls} right to left; {comparing.RaisedGlyphs} glyphs off the baseline; {faces.Length} faces:");
        foreach (var face in faces.Select(f => f.FamilyName).Distinct().Order())
            output.WriteLine("  " + face);
        // Which face each text's first run fell to, and whether it had the glyphs.
        foreach (var text in texts)
        {
            var layout = new TextPipeline(fonts, subject).Layout(text, Base, float.PositiveInfinity, null);
            var runs = layout.Lines.SelectMany(l => l.Runs).ToArray();
            int notdef = runs.Sum(r => r.Glyphs.Count(g => g.GlyphId == 0));
            output.WriteLine($"  {string.Join(" + ", runs.Select(r => r.Font.FamilyName).Distinct())}: "
                + $"{notdef} of {runs.Sum(r => r.Glyphs.Length)} glyphs missing — {Escape(text)}");
        }
        return (comparing.Calls, comparing.RtlCalls, faces.Length, comparing.RaisedGlyphs);
    }

    private static string Escape(string text) =>
        string.Concat(text.Select(c => c < 0x20 || c is >= (char)0xD800 and <= (char)0xDFFF ? $"\\u{(int)c:X4}" : c.ToString()));

    private static int Bits(float value) => BitConverter.SingleToInt32Bits(value);

    internal static void Same(ShapedRun expected, ShapedRun actual, string context)
    {
        if (Bits(expected.Width) != Bits(actual.Width))
            Assert.Fail($"{context}: width {expected.Width:R} expected, {actual.Width:R}");
        if (expected.Glyphs.Length != actual.Glyphs.Length)
            Assert.Fail($"{context}: {expected.Glyphs.Length} glyphs expected, {actual.Glyphs.Length}");
        for (int i = 0; i < expected.Glyphs.Length; i++)
        {
            var e = expected.Glyphs[i];
            var a = actual.Glyphs[i];
            if (e.GlyphId != a.GlyphId || Bits(e.X) != Bits(a.X) || Bits(e.Y) != Bits(a.Y) || Bits(e.Advance) != Bits(a.Advance))
                Assert.Fail($"{context}: glyph {i} expected {e.GlyphId} at ({e.X:R}, {e.Y:R}) +{e.Advance:R}, "
                    + $"got {a.GlyphId} at ({a.X:R}, {a.Y:R}) +{a.Advance:R}");
        }
    }

    /// <summary>Shapes with the shaper under test and the reference, insists they agree, and
    /// counts what it saw.</summary>
    private sealed class ComparingShaper(ITextShaper subject, ITextShaper reference) : ITextShaper
    {
        public int Calls, RtlCalls, RaisedGlyphs;
        public readonly HashSet<IResolvedFont> Fonts = new(ReferenceEqualityComparer.Instance);

        public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
        {
            var actual = subject.Shape(text, font, fontSize, rtl);
            var expected = reference.Shape(text, font, fontSize, rtl);
            Same(expected, actual, $"\"{Escape(text)}\" in {font.FamilyName} at {fontSize:R} rtl={rtl}");
            Calls++;
            if (rtl) RtlCalls++;
            foreach (var glyph in actual.Glyphs)
                if (glyph.Y != 0) RaisedGlyphs++;
            Fonts.Add(font);
            return actual;
        }
    }
}

/// <summary>
/// <see cref="SkiaTextShaper"/> as it was until plan 17's round 2: SkiaSharp's
/// <see cref="SKShaper"/> per typeface, and per call a new <see cref="SKFont"/> and buffer — the
/// text added as UTF-8 left to right, as UTF-16 with the direction set right to left.
/// </summary>
internal sealed class SkShaperReference : ITextShaper, IDisposable
{
    private readonly Dictionary<SKTypeface, SKShaper> _shapers = new();

    public ShapedRun Shape(string text, IResolvedFont font, float fontSize, bool rtl = false)
    {
        if (string.IsNullOrEmpty(text))
            return new ShapedRun(text, font, fontSize, 0, []);

        var typeface = (SKTypeface)font.NativeHandle;
        if (!_shapers.TryGetValue(typeface, out var shaper))
            _shapers[typeface] = shaper = new SKShaper(typeface);

        using var skFont = new SKFont(typeface, fontSize);
        SKShaper.Result result;
        if (rtl)
        {
            using var buffer = new HarfBuzzSharp.Buffer();
            buffer.AddUtf16(text);
            buffer.GuessSegmentProperties();
            buffer.Direction = HarfBuzzSharp.Direction.RightToLeft;
            result = shaper.Shape(buffer, 0, 0, skFont);
        }
        else
        {
            result = shaper.Shape(text, skFont);
        }

        var glyphs = new GlyphPosition[result.Codepoints.Length];
        for (int i = 0; i < result.Codepoints.Length; i++)
        {
            float advance = i < result.Codepoints.Length - 1
                ? result.Points[i + 1].X - result.Points[i].X
                : result.Width - result.Points[i].X;
            glyphs[i] = new GlyphPosition(result.Codepoints[i], result.Points[i].X, result.Points[i].Y, advance);
        }
        return new ShapedRun(text, font, fontSize, result.Width, glyphs);
    }

    public void Dispose()
    {
        foreach (var shaper in _shapers.Values)
            shaper.Dispose();
        _shapers.Clear();
    }
}
