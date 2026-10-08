using HultUI.Core.Styling;
using HultUI.Text;
using HultUI.Text.Tests;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// The real-font twin of HultUI.Text.Tests' WordShapeLookupTests: a run's words are found in the
/// word cache by a span of its text, its glyphs go into an array of the exact size, and a width
/// is added up without glyphs — and every run laid out is still each of its words shaped on its
/// own by HarfBuzz, moved along by the widths before it (<see cref="WordModel"/>), to the bit:
/// letter spacing, the digit table, runs in fallback fonts (a monospace face without IPA, Greek,
/// a Han ideograph every CJK face has), and the left-to-right runs of text on the BiDi path.
/// </summary>
public class WordGlyphsTests
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly ResolvedInheritedStyle[] Styles =
    [
        Base,
        Base with { LetterSpacing = 0.6f },
        Base with { FontSize = 13f, LetterSpacing = -0.35f },
        Base with { FontSize = 13f, FontWeight = FontWeight.Bold },
        Base with { FontFamily = FontFamilies.Monospace },
        Base with { FontFamily = FontFamilies.Monospace, LetterSpacing = 1.25f },
    ];

    private const string Han = "東 east 東京 west 說";

    private static readonly string[] Texts =
    [
        "The quick brown fox jumps over the lazy dog",
        "AVA To Wa Ty Yo office affine flow fjord",
        "double  spaced   words  ",
        "12345 67890 1029384756",
        "Item 4711 - 815 units, 3.5% of 100",
        "cafe" + (char)0x301 + " re" + (char)0x301 + "sume" + (char)0x301,
        "ʃɪp ðə ŋ ipa words",
        "Ελληνικό κείμενο εδώ",
        Han,
        "emoji " + char.ConvertFromUtf32(0x1F600) + " in the " + char.ConvertFromUtf32(0x1F680) + " middle and more words",
        "commit שלום merged by אבג into main 42",
    ];

    [Fact]
    public void EveryRunLaidOut_IsItsWordsShapedApart_ToTheBit()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        var pipeline = new TextPipeline(fonts, shaper);
        int runs = 0, fallbackRuns = 0, hanFallbackRuns = 0;

        // The first pass shapes the words; the second lays out the same words in another order,
        // new strings that find every word in the cache, in place.
        foreach (bool reversed in new[] { false, true })
        foreach (var style in Styles)
        foreach (var source in Texts)
        {
            string text = reversed ? string.Join(' ', source.Split(' ').Reverse()) : source;
            var primary = fonts.Resolve(style.FontFamily, style.FontWeight, style.FontSlant);
            foreach (var line in pipeline.Layout(text, style, float.PositiveInfinity, null).Lines)
            foreach (var run in line.Runs)
            {
                var expected = WordModel.Run(shaper, run.Text, run.Font, run.FontSize, style.LetterSpacing, run.IsRtl);
                WordModel.Same(expected, run, $"\"{run.Text}\" of \"{text}\" in {run.Font.FamilyName} {style}");
                runs++;
                if (!ReferenceEquals(run.Font, primary))
                {
                    fallbackRuns++;
                    if (run.Text.Contains('東')) hanFallbackRuns++;
                }
            }
        }

        Assert.True(runs > 150, $"compared only {runs} runs");
        Assert.True(fallbackRuns > 0, "no run fell back to another font");
        if (HanFonts.Installed)
            Assert.True(hanFallbackRuns > 0, "東 never fell back to a CJK face");
    }

    [Fact]
    public void AMeasuredString_IsTheWidthOfItsLaidOutLine_ToTheBit()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        var pipeline = new TextPipeline(fonts, shaper);
        int measured = 0;
        foreach (var style in Styles)
        foreach (var text in Texts.Where(t => !t.Any(TextPipeline.MayNeedBidi)))
        {
            // Laid out first, so the measurement has the same paragraph (and Han context).
            var line = Assert.Single(pipeline.Layout(text, style, float.PositiveInfinity, null).Lines);
            float expected = 0;
            foreach (var run in line.Runs)
                expected += WordModel.Run(shaper, run.Text, run.Font, run.FontSize, style.LetterSpacing).Width;
            float actual = pipeline.MeasureString(text, style);
            Assert.True(WordModel.Bits(expected) == WordModel.Bits(actual) && WordModel.Bits(line.Width) == WordModel.Bits(actual),
                $"\"{text}\" {style}: measured {actual}, laid out {line.Width}, model {expected}");
            measured++;
        }
        Assert.True(measured >= 50, $"measured only {measured} strings");
    }
}
