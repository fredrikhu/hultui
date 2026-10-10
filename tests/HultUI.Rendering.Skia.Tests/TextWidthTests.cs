using HultUI.Core.Layout;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// <see cref="TextPipeline.MeasureWidth"/> through HarfBuzz and the system's fonts: every string,
/// in every style, measures what laying it out on one line does, to the bit — kerning, digits
/// from the digit table, tabs, letter spacing, fallback faces for Han in and out of a kana
/// context, and the text that is laid out after all (right-to-left script, emoji, a newline).
/// A TextArea decides from these numbers whether each of its lines fits, where it used to lay
/// each line out; its line starts are the old ones only because these are the same numbers.
/// </summary>
public class TextWidthTests
{
    private static readonly ResolvedInheritedStyle Base = new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly ResolvedInheritedStyle[] Styles =
    [
        Base,
        Base with { LetterSpacing = 0.6f },
        Base with { LetterSpacing = -0.35f, FontSize = 12.5f },
        Base with { FontSize = 13f, FontWeight = FontWeight.Bold },
        Base with { FontFamily = FontFamilies.Monospace, FontSize = 12f },
        Base with { FontSlant = FontSlant.Italic, FontSize = 11.5f },
        Base with { TextDirection = TextDirection.Rtl },
    ];

    internal static readonly string[] Texts =
    [
        .. PlainPathEquivalenceTests.Texts,
        "AVAWAY To. Wolf office affluent fjord fi ffl",
        "1234567890 digits 3.14159 and v2.0.1",
        "      \"id\": 100005,",
        "      \"created_at\": \"2026-03-14T09:26:53Z\",",
        "\tindented\twith\ttabs",
        "a\tb",
        "東",
        "東京",
        "東京に行きます",
        "漢字 and 한국어",
        "cafe" + (char)0x0301 + " na" + (char)0x0308 + "ive",
        "soft" + (char)0x00AD + "hyphen",
        "lone " + (char)0xD83D + " surrogate",
        "emoji 😀 and 👩‍💻 and 🇸🇪",
        "שלום עולם",
        "commit שלום merged by אבג into main",
        "مرحبا بالعالم",
        "two\nlines",
        "  leading and trailing  ",
        " ",
        "x",
    ];

    [Fact]
    public void EveryString_MeasuresWhatItsLayoutDoes_ToTheBit()
    {
        var fonts = new SkiaFontCollection();
        var shaper = new SkiaTextShaper();
        var widths = new TextPipeline(fonts, shaper);
        var reference = new TextPipeline(fonts, shaper);
        int compared = 0;
        foreach (var style in Styles)
            foreach (var text in Texts)
            {
                float expected = reference.Layout(text, style, float.PositiveInfinity, maxLines: 1).Size.Width;
                // As a line of a longer text, three times: measured, measured and kept, found.
                string document = "before\n" + text + "\nafter";
                for (int time = 0; time < 3; time++)
                {
                    float actual = widths.MeasureWidth(document.AsSpan(7, text.Length), style);
                    Assert.True(BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual),
                        $"\"{text}\" in {style.FontFamily} {style.FontSize} spaced {style.LetterSpacing} "
                        + $"{style.TextDirection}, time {time + 1}: laid out {expected}, measured {actual}");
                    compared++;
                }
            }
        Assert.Equal(Styles.Length * Texts.Length * 3, compared);
    }

    /// <summary>A Skia render context answers <see cref="IRenderContext.MeasureTextWidth"/> from its
    /// pipeline, the same number its <see cref="IRenderContext.MeasureText"/> gives.</summary>
    [Fact]
    public void TheRenderContext_MeasuresAWidthAsItMeasuresTheText()
    {
        var services = new SkiaTextServices();
        using var ctx = new SkiaRenderContext();
        ctx.SetPipeline(services.Pipeline);
        IRenderContext context = ctx;
        foreach (var text in Texts)
            Assert.Equal(BitConverter.SingleToInt32Bits(context.MeasureText(text, Base).Width),
                BitConverter.SingleToInt32Bits(context.MeasureTextWidth(text, Base)));
        Assert.Equal(0f, context.MeasureTextWidth("", Base));
    }

    /// <summary>
    /// A width is a number, not a layout: measuring a document's lines keeps none of them in the
    /// shape or layout cache, where laying each out kept each line's glyphs. Right-to-left text
    /// is still laid out, and kept as a layout.
    /// </summary>
    [Fact]
    public void MeasuringWidths_KeepsNoShaping()
    {
        var pipeline = new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper());
        for (int i = 0; i < 2_000; i++)
            pipeline.MeasureWidth($"      \"field_{i}\": \"value {i * 7919 % 10_007} with words\",", Base);
        Assert.Equal(0, pipeline.ShapeCacheUse.Entries);
        Assert.Equal(0, pipeline.LayoutCacheUse.Entries);

        pipeline.MeasureWidth("שלום עולם", Base);
        Assert.Equal(1, pipeline.LayoutCacheUse.Entries);

        // What laying them out kept, for scale.
        for (int i = 0; i < 2_000; i++)
            pipeline.Layout($"      \"field_{i}\": \"value {i * 7919 % 10_007} with words\",", Base, float.PositiveInfinity, 1);
        Assert.Equal(2_000, pipeline.ShapeCacheUse.Entries);
    }
}
