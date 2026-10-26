using HultUI.Core.Layout;
using HultUI.Core.Styling;
using HultUI.Text;
using HultUI.Text.Tests;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// The real-font twin of HultUI.Text.Tests' GlyphsOnFirstReadTests: a run of several words makes
/// its glyph array the first time its glyphs are read, and that array is the one the reference
/// path (<c>ComposeGlyphsWhenRead = false</c>) made with the run, to the bit — through HarfBuzz and
/// the system's fonts, with styled spans, BiDi text, astral emoji, letter spacing, fallback faces,
/// ellipsis and line limits, read straight away or after the word cache let every word go.
/// </summary>
public class GlyphsOnFirstReadTests
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly ResolvedInheritedStyle[] Styles =
    [
        Base,
        Base with { LetterSpacing = 0.6f },
        Base with { FontSize = 13f, FontWeight = FontWeight.Bold, LineHeight = 20f },
        Base with { FontFamily = FontFamilies.Monospace, FontSize = 12f },
        Base with { FontFamily = "serif", FontSize = 17.5f },
        Base with { TextDirection = TextDirection.Rtl },
    ];

    private static readonly string[] Texts =
    [
        "Pack my box with five dozen liquor jugs. How vexingly quick daft zebras jump! The five boxing wizards jump quickly.",
        "AVA To Wa Ty Yo office affine flow fjord and the final office",
        "Item 4711 - 815 units, 3.5% of 100 and 12345 67890",
        "double  spaced   words  here   and there  ",
        "café résumé naïve words in a row",
        "ʃɪp ðə ŋ ipa words beside Ελληνικό κείμενο εδώ and Привет, мир",
        "Mixed English — 東京 → ∞ and 100 € £ ¥ then more words",
        "中文文本测试，标点符号。还有更多的文字在这里。 and some latin words",
        "commit שלום merged by אבג into main, then (דהו) 123 and more words",
        "مرحبا بالعالم and some english words هذا نص عربي",
        "emoji " + char.ConvertFromUtf32(0x1F600) + " in the " + char.ConvertFromUtf32(0x1F680) + " middle and "
            + char.ConvertFromUtf32(0x1F469) + "‍" + char.ConvertFromUtf32(0x1F4BB) + " more words",
        "first line of words\nsecond line is longer than the first one",
    ];

    private static readonly (int? MaxLines, TextOverflow Overflow)[] Modes =
    [
        (null, TextOverflow.Clip),
        (1, TextOverflow.Ellipsis),
        (2, TextOverflow.Ellipsis),
        (3, TextOverflow.Clip),
    ];

    [Fact]
    public void GlyphsReadLater_AreTheGlyphsMadeWithTheRun_ToTheBit()
    {
        var fonts = new SkiaFontCollection();
        int compared = 0, readLater = 0;
        foreach (bool flushFirst in new[] { false, true })
        {
            var reference = new TextPipeline(fonts, new SkiaTextShaper()) { ComposeGlyphsWhenRead = false };
            var lazy = new TextPipeline(fonts, new SkiaTextShaper());
            var laidOut = new List<(TextLayout Expected, TextLayout Actual, string Context)>();

            foreach (var style in Styles)
            foreach (var text in Texts)
            foreach (var spans in TestSpans.Variants(text).Prepend(null))
            foreach (var (maxLines, overflow) in Modes)
            for (float w = 30f; w < 900f; w += 61.5f)
                laidOut.Add((reference.Layout(text, style, w, maxLines, overflow, spans),
                    lazy.Layout(text, style, w, maxLines, overflow, spans),
                    $"\"{text}\" spans {spans?.Length ?? 0} w={w} lines={maxLines} {overflow} {style}"));

            // Every word of the corpus out of the word cache (8,192 entries) before a glyph is read.
            if (flushFirst)
                for (int i = 0; i < 2 * 8192; i++)
                    lazy.Layout($"w{i:x5}", Base, float.PositiveInfinity, null);

            foreach (var (expected, actual, context) in laidOut)
            {
                readLater += actual.Lines.SelectMany(l => l.Runs).Count(r => !r.GlyphsComposed);
                LayoutAssert.Same(expected, actual, (flushFirst ? "after a flush: " : "") + context);
                compared++;
            }
        }
        Assert.True(compared > 5_000, $"compared only {compared} layouts");
        Assert.True(readLater > 5_000, $"only {readLater} runs made their glyphs when read");
    }
}
