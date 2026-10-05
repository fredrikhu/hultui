using HultUI.Core.Layout;
using HultUI.Core.Styling;
using HultUI.Text;
using HultUI.Text.Tests;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// The real-font twin of HultUI.Text.Tests' WidthChangeEquivalenceTests: wrapping from a
/// string's kept break segments, truncation that measures prefixes from its own segments,
/// and one ellipsized line without the line breaker, against the reference path that worked
/// everything out from scratch at each width — through HarfBuzz and the system's fonts, with
/// their actual kerning and ligatures. Same output, to the bit, at every width.
/// </summary>
public class WidthChangeEquivalenceTests
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
        Base with { TextDirection = TextDirection.Rtl },
        Base with { TextDirection = TextDirection.Auto, FontSize = 13f },
    ];

    private static readonly string[] Texts =
    [
        "The quick brown fox jumps over the lazy dog",
        "Pack my box with five dozen liquor jugs. How vexingly quick daft zebras jump! The five boxing wizards jump quickly.",
        "AVA To Wa Ty Yo office affine flow fjord",   // kern pairs and ligatures, cut anywhere
        "Incomprehensibilities",
        "well-known x-ray https://example.com/a/b/c.txt",
        "C:\\Users\\someone\\source\\repos\\project\\File.cs",
        "   leading spaces then words",
        "trailing spaces   ",
        "\u00ADsoft hyphen first",
        "cafe\u0301 re\u0301sume\u0301 nai\u0308ve",
        "12345 67890 1029384756",
        "Ελληνικό κείμενο εδώ",
        "Привет, мир, как дела",
        "W",
        "Wi",
        "first line\nsecond line is longer",
        // The BiDi path in any style.
        "שלום עולם ספר בית מחשב חלון קובץ תיקייה שורה טקסט",
        "commit שלום merged by אבג into main, then (דהו) 123",
        "مرحبا بالعالم هذا نص عربي طويل للاختبار",
        "שורה ראשונה\nשורה שנייה ארוכה יותר",
        "日本語のテキストを表示",
        "commit שלום merged",
    ];

    private static readonly (int? MaxLines, TextOverflow Overflow)[] Modes =
    [
        (1, TextOverflow.Ellipsis),
        (2, TextOverflow.Ellipsis),
        (1, TextOverflow.Clip),
        (null, TextOverflow.Clip),
        (3, TextOverflow.Clip),
    ];

    [Fact]
    public void Reuse_MatchesTheReferencePath_AtEveryWidth()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        int compared = 0;
        foreach (var (maxLines, overflow) in Modes)
        {
            // Fresh pipelines per mode, so each string's first truncation (which builds its
            // tables) is part of what is compared, shared by every style, so a cache key that
            // left out a style field would hand one style another's result and fail here.
            var reference = new TextPipeline(fonts, shaper) { ReuseAcrossWidths = false };
            var reuse = new TextPipeline(fonts, shaper);

            foreach (var style in Styles)
            foreach (var text in Texts)
            {
                float natural = reference.Layout(text, style, float.PositiveInfinity, null).Size.Width;
                float step = text.Length <= 3 ? 0.25f : 1.3f;
                var widths = new List<float> { -1f, 0f };
                for (float w = 0.5f; w < natural + 3f; w += step) widths.Add(w);

                foreach (var w in widths.Concat(Enumerable.Reverse(widths)))
                {
                    var expected = reference.Layout(text, style, w, maxLines, overflow);
                    var actual = reuse.Layout(text, style, w, maxLines, overflow);
                    LayoutAssert.Same(expected, actual, $"\"{text}\" w={w} lines={maxLines} {overflow} {style}");
                    compared++;
                }
            }
        }
        Assert.True(compared > 10_000, $"compared only {compared} layouts");
    }

    [Fact]
    public void StyledReuse_MatchesTheReferencePath_AtEveryWidth()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        int compared = 0;
        foreach (var (maxLines, overflow) in Modes)
        {
            var reference = new TextPipeline(fonts, shaper) { ReuseAcrossWidths = false };
            var reuse = new TextPipeline(fonts, shaper);

            foreach (var style in Styles)
            foreach (var text in Texts)
            foreach (var spans in TestSpans.Variants(text))
            {
                float natural = reference.Layout(text, style, float.PositiveInfinity, null, TextOverflow.Clip, spans).Size.Width;
                var widths = new List<float> { -1f, 0f };
                for (float w = 0.5f; w < natural + 3f; w += 2.1f) widths.Add(w);

                foreach (var w in widths.Concat(Enumerable.Reverse(widths)))
                {
                    var expected = reference.Layout(text, style, w, maxLines, overflow, spans);
                    var actual = reuse.Layout(text, style, w, maxLines, overflow, spans);
                    LayoutAssert.Same(expected, actual, $"\"{text}\" spans {spans.Length} w={w} lines={maxLines} {overflow} {style}");
                    compared++;
                }
            }
        }
        Assert.True(compared > 10_000, $"compared only {compared} layouts");
    }
}
