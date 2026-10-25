using HultUI.Core.Layout;
using HultUI.Core.Styling;
using HultUI.Text;
using SkiaSharp;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// <see cref="TextPipeline.MonospaceAsciiAdvance"/> through HarfBuzz and the system's fonts. A
/// TextArea takes a line of printable ASCII in a face given an advance as fitting, without
/// measuring it, when one character more would fit: every such line, measured, must come to
/// no more than its count and one times the advance. The platform's monospace faces get one;
/// a proportional face, and a face with programming ligatures where they change advances, do not.
/// </summary>
public class MonospaceAsciiAdvanceTests
{
    private static ResolvedInheritedStyle Style(string family, float size = 12f, FontWeight weight = FontWeight.Regular) => new(
        FontFamily: family, FontSize: size, FontWeight: weight,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static readonly string[] Pieces =
    [
        "{", "}", "\"id\": ", "\"name\": \"", "\",", "[", "]", " ", "  ", "    ", "true", "false", "null",
        "1234", "-0.5e+3", "=>", "!=", "->", "<=", ">=", "===", "www", "fi", "ffl", "AV", "To", "--", "__",
        "/*", "*/", "//", "#", "~", "`", "|", "&&", "||", "::", "...", "<-", "<<", ">>", "0x1F", "iiii", "WWWW",
    ];

    [Fact]
    public void TheMonospaceFace_IsProven_AndALineOfItNeverMeasuresPastItsCountAndOne()
    {
        var fonts = new SkiaFontCollection();
        var pipeline = new TextPipeline(fonts, new SkiaTextShaper());
        var reference = new TextPipeline(fonts, new SkiaTextShaper());
        int proven = 0;
        foreach (var style in new[] { Style(FontFamilies.Monospace), Style(FontFamilies.Monospace, 13.5f),
                     Style(FontFamilies.Monospace, 13.3f), Style(FontFamilies.Monospace, 32f / 3),
                     Style(FontFamilies.Monospace, 12f, FontWeight.Bold), Style("Cascadia Code"), Style("Consolas"),
                     Style("DejaVu Sans Mono", 11f) })
        {
            float advance = pipeline.MonospaceAsciiAdvance(style);
            var face = (SKTypeface)fonts.Resolve(style.FontFamily, style.FontWeight, style.FontSlant).NativeHandle;
            if (advance == 0f)
            {
                Assert.False(face.IsFixedPitch && style.FontFamily == FontFamilies.Monospace && style.FontWeight == FontWeight.Regular,
                    $"{face.FamilyName} {style.FontSize} says it is fixed pitch, and was not proven monospace");
                continue;
            }
            proven++;
            var rng = new Random(5);
            for (int i = 0; i < 2_000; i++)
            {
                var line = new System.Text.StringBuilder();
                for (int p = 0, count = 1 + rng.Next(40); p < count; p++) line.Append(Pieces[rng.Next(Pieces.Length)]);
                string text = line.ToString();
                float width = reference.Layout(text, style, float.PositiveInfinity, maxLines: 1).Size.Width;
                Assert.True(width <= (text.Length + 1) * advance,
                    $"\"{text}\" in {face.FamilyName} {style.FontSize}: {width}, more than {text.Length + 1} × {advance}");
                Assert.True(Math.Abs(width - text.Length * advance) < advance / 4,
                    $"\"{text}\" in {face.FamilyName} {style.FontSize}: {width}, not {text.Length} × {advance}");
            }
        }
        Assert.True(proven >= 2, $"only {proven} monospace faces proven");
    }

    [Theory]
    [InlineData("sans-serif")]
    [InlineData("serif")]
    public void AProportionalFace_IsNot(string family) =>
        Assert.Equal(0f, new TextPipeline(new SkiaFontCollection(), new SkiaTextShaper()).MonospaceAsciiAdvance(Style(family)));
}
