using HultUI.Core.Layout;
using HultUI.Core.Styling;
using HultUI.Text;
using HultUI.Text.Tests;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Left-to-right text with chars at or above U+0590 — an em dash, an arrow, CJK — used to
/// take the BiDi path whatever it held; it now takes the plain path unless something in it
/// can need the BiDi one. Broken into lines, the two lay it out the same, to the bit, through
/// HarfBuzz and the system's fonts: every width, plain and styled, kept to a line count or not —
/// except where the BiDi path broke a line that fits, which the plain path keeps
/// (<see cref="SameOrFitsWhereBidiBrokeEarly"/>).
///
/// <para>Truncated, they do not, and are not meant to: the plain path cuts a clipped line at a
/// character, keeps none when none fits, and shapes the ellipsis with the text before it; the
/// BiDi path does each its own way. "Item 4 — 7 units" now truncates the way "Item 4 - 7 units"
/// always has. WidthChangeEquivalenceTests holds the plain path's truncation of this corpus to
/// its from-scratch reference.</para>
/// </summary>
public class PlainPathEquivalenceTests
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly ResolvedInheritedStyle[] Styles =
    [
        Base,
        Base with { LetterSpacing = 0.6f },
        Base with { LineHeight = 24f },
        Base with { FontSize = 13f, FontWeight = FontWeight.Bold },
        Base with { FontFamily = FontFamilies.Monospace },
        Base with { TextDirection = TextDirection.Auto, FontSize = 13f },
    ];

    internal static readonly string[] Texts =
    [
        "Item 4 — 7 units",
        "a – b — c … “quoted” ‘single’ • bullet ‰ ′ ″",
        "→ ← ↑ ↓ ⇒ ∞ ≤ ≥ ≠ ± × ÷ √ ∑ ∫",
        "┌─┬─┐ │ │ └─┴─┘ ▲ ▼ ● ○ ■ □",
        "★ ☆ ✓ ✔ ✗ ✘ ☐ ☑ ♥ ♦",
        "word—word—word—word—word—word",
        "Merged by Ann — fixes the layout cache → 40 % faster … see “notes”",
        "東京に行きます 今日は良い天気です。",
        "日本語のテキストを表示",
        "中文文本测试，标点符号。还有更多的文字在这里。",
        "한국어 텍스트 테스트 문장입니다",
        "ｶﾀｶﾅ ＡＢＣ　全角スペース",
        "Ἀθῆναι καὶ Σπάρτη — Ελληνικά",
        "Mixed English — 東京 → ∞ and 100 € £ ¥",
        "first line — x\nsecond → y is longer",
        "W—",
        "—",
    ];

    // Broken into lines, kept to a count of them or not; truncation is the paths' own business.
    private static readonly (int? MaxLines, TextOverflow Overflow)[] Modes =
    [
        (null, TextOverflow.Clip),
        (3, TextOverflow.Clip),
        (2, TextOverflow.Clip),
    ];

    [Fact]
    public void TheCorpusTakesThePlainPath()
    {
        foreach (var text in Texts)
            Assert.False(text.Any(TextPipeline.MayNeedBidi), $"\"{text}\" would still take the BiDi path");
    }

    /// <summary>The plain path tells a one-line layout's caller the width it holds from — its
    /// own — and the BiDi path does not know it, so the answer shows which path laid it out.</summary>
    [Fact]
    public void ALineOfIt_IsLaidOutOnThePlainPath_AndRightToLeftTextStillIsNot()
    {
        var pipeline = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        foreach (var text in Texts.Where(t => !t.Contains('\n')))
        {
            var layout = pipeline.Layout(text, Base, float.PositiveInfinity, null, TextOverflow.Clip, out float from);
            Assert.True(from == layout.Size.Width, $"\"{text}\" held from {from}, not from its own width");
        }
        foreach (var text in new[] { "commit שלום merged", "مرحبا", "emoji 😀 here", "e\u0301 ok \u200D" })
        {
            pipeline.Layout(text, Base, float.PositiveInfinity, null, TextOverflow.Clip, out float from);
            Assert.True(float.IsNaN(from), $"\"{text}\" left the BiDi path");
        }
    }

    /// <summary>
    /// Where the two paths part on breaking: full-width text in Noto Sans CJK, which spaces
    /// full-width forms by their neighbours. The BiDi path decides a line fits by adding up its
    /// break segments, each shaped alone, and with that spacing they come to more than the line
    /// shaped whole — on Fedora it wrapped this one at widths up to two pixels past the width it
    /// measured for it. The plain path decides from the line as it is drawn.
    /// </summary>
    [Fact]
    public void ALineThatFitsStaysOneLine_FullWidthFormsIncluded()
    {
        var pipeline = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        const string text = "ｶﾀｶﾅ ＡＢＣ　全角スペース";
        Assert.False(text.Any(TextPipeline.MayNeedBidi));
        foreach (var style in Styles)
        {
            float natural = pipeline.Layout(text, style, float.PositiveInfinity, null).Size.Width;
            for (float w = natural; w < natural + 4f; w += 0.25f)
                Assert.Single(pipeline.Layout(text, style, w, null).Lines);
        }
    }

    [Fact]
    public void ThePlainPath_LaysOutWhatTheBidiPathDid()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        int compared = 0;
        foreach (var (maxLines, overflow) in Modes)
        {
            var bidi = new TextPipeline(fonts, shaper) { PlainPathForLtr = false };
            var plain = new TextPipeline(fonts, shaper);
            foreach (var style in Styles)
            foreach (var text in Texts)
            {
                float natural = bidi.Layout(text, style, float.PositiveInfinity, null).Size.Width;
                var widths = new List<float> { -1f, 0f };
                for (float w = 0.5f; w < natural + 3f; w += 2.3f) widths.Add(w);
                foreach (var w in widths)
                {
                    var expected = bidi.Layout(text, style, w, maxLines, overflow);
                    var actual = plain.Layout(text, style, w, maxLines, overflow);
                    SameOrFitsWhereBidiBrokeEarly(expected, actual, w, $"\"{text}\" w={w} lines={maxLines} {overflow} {style}");
                    compared++;
                }
            }
        }
        Assert.True(compared > 3_000, $"compared only {compared} layouts");
    }

    [Fact]
    public void StyledText_LaysOutOnThePlainPathAsOnTheBidiPath()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        int compared = 0;
        foreach (var (maxLines, overflow) in Modes)
        {
            var bidi = new TextPipeline(fonts, shaper) { PlainPathForLtr = false };
            var plain = new TextPipeline(fonts, shaper);
            foreach (var style in Styles)
            foreach (var text in Texts)
            foreach (var spans in TestSpans.Variants(text))
            {
                float natural = bidi.Layout(text, style, float.PositiveInfinity, null, TextOverflow.Clip, spans).Size.Width;
                var widths = new List<float> { -1f, 0f };
                for (float w = 0.5f; w < natural + 3f; w += 4.7f) widths.Add(w);
                foreach (var w in widths)
                {
                    var expected = bidi.Layout(text, style, w, maxLines, overflow, spans);
                    var actual = plain.Layout(text, style, w, maxLines, overflow, spans);
                    SameOrFitsWhereBidiBrokeEarly(expected, actual, w, $"\"{text}\" spans {spans.Length} w={w} lines={maxLines} {overflow} {style}");
                    compared++;
                }
            }
        }
        Assert.True(compared > 3_000, $"compared only {compared} layouts");
    }

    /// <summary>
    /// The same layout, to the bit — or the one way the paths part: the BiDi path decides a
    /// line fits by adding up its break segments shaped alone, which can come to more than the
    /// line shaped whole, and breaks a line the plain path keeps. On Fedora, with Noto Sans CJK
    /// spacing full-width forms by their neighbours and with letter spacing, that happened within
    /// a few pixels past the measured width (198 of 828,252 layouts, every difference there was).
    /// The plain path's lines must then all fit, and be fewer.
    /// </summary>
    private static void SameOrFitsWhereBidiBrokeEarly(TextLayout bidi, TextLayout plain, float width, string context)
    {
        try { LayoutAssert.Same(bidi, plain, context); }
        catch (Xunit.Sdk.XunitException) when (
            plain.Lines.Count < bidi.Lines.Count && plain.Lines.All(line => line.Width <= width)) { }
    }
}
