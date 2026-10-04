using HultUI.Core.Styling;
using HultUI.Text;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Seam T1 (docs/design/TERMINAL.md): the generic monospace family, family lists, a font's
/// metrics and its glyph coverage, against the platform's real fonts.
/// </summary>
public class FontFamilyAndMetricsTests
{
    private const string Missing = "No Such Font 4711";

    private static ResolvedInheritedStyle Style(string family, float size = 13f) => new(
        FontFamily: family, FontSize: size, FontWeight: FontWeight.Regular,
        TextColor: new(0, 0, 0, 255), TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private static TextPipeline Pipeline() => new(new SkiaFontCollection(), new SkiaTextShaper());

    // ──── Families ────

    [Fact]
    public void Monospace_IsAFixedPitchFace_OnEveryPlatform()
    {
        // DirectWrite does not know "monospace": asked by name it answered Segoe UI, so every
        // .FontFamily("monospace") on Windows drew proportionally.
        var font = new SkiaFontCollection().Resolve(FontFamilies.Monospace, FontWeight.Regular, FontSlant.Normal);
        Assert.True(font.IsFixedPitch, $"monospace resolved to '{font.FamilyName}'");
        if (OperatingSystem.IsWindows())
            Assert.Contains(font.FamilyName, new[] { "Cascadia Mono", "Consolas", "Courier New" });
    }

    [Fact]
    public void Monospace_KeepsTheWeight()
    {
        var fonts = new SkiaFontCollection();
        var regular = (SkiaSharp.SKTypeface)fonts.Resolve(FontFamilies.Monospace, FontWeight.Regular, FontSlant.Normal).NativeHandle;
        var bold = (SkiaSharp.SKTypeface)fonts.Resolve(FontFamilies.Monospace, FontWeight.Bold, FontSlant.Normal).NativeHandle;
        Assert.True(bold.FontWeight > regular.FontWeight, $"{regular.FontWeight} then {bold.FontWeight}");
    }

    [Fact]
    public void AList_TakesTheFirstFamilyInstalled()
    {
        var fonts = new SkiaFontCollection();
        var mono = fonts.Resolve(FontFamilies.Monospace, FontWeight.Regular, FontSlant.Normal).FamilyName;

        Assert.Equal(mono, fonts.Resolve($"{Missing}, monospace", FontWeight.Regular, FontSlant.Normal).FamilyName);
        Assert.Equal(mono, fonts.Resolve($"'{Missing}' , \"Monospace\"", FontWeight.Regular, FontSlant.Normal).FamilyName);
        // An installed first family wins over the rest.
        Assert.Equal(mono, fonts.Resolve($"{mono}, {Missing}", FontWeight.Regular, FontSlant.Normal).FamilyName);
    }

    [Fact]
    public void AListWithNothingInstalled_DrawsAsItsFirstNameAlone()
    {
        var fonts = new SkiaFontCollection();
        var alone = fonts.Resolve(Missing, FontWeight.Regular, FontSlant.Normal).FamilyName;
        Assert.Equal(alone, fonts.Resolve($"{Missing}, Another Missing Font", FontWeight.Regular, FontSlant.Normal).FamilyName);
    }

    // ──── Metrics ────

    [Fact]
    public void MonospaceMetrics_AgreeWithWhatThePipelineDraws()
    {
        var pipeline = Pipeline();
        var style = Style(FontFamilies.Monospace);
        var m = pipeline.MeasureFont(style);

        Assert.True(m.IsFixedPitch);
        Assert.Equal(pipeline.Fonts.Resolve(FontFamilies.Monospace, FontWeight.Regular, FontSlant.Normal).FamilyName, m.FamilyName);
        // A grid stepping by the character width stays on the glyphs: any ten characters span ten cells.
        Assert.Equal(10 * m.CharacterWidth, pipeline.Layout("0123456789", style, float.PositiveInfinity, 1).Size.Width, 2);
        Assert.Equal(10 * m.CharacterWidth, pipeline.Layout("iiiiMMMMWW", style, float.PositiveInfinity, 1).Size.Width, 2);
        // A line is as tall as the metrics say.
        Assert.Equal(m.LineHeight, pipeline.Layout("0", style, float.PositiveInfinity, 1).Size.Height, 2);

        Assert.True(m.Ascent > 0 && m.Descent > 0 && m.LineGap >= 0, m.ToString());
        Assert.True(m.UnderlinePosition > 0 && m.UnderlinePosition < m.Descent, m.ToString());
        Assert.True(m.UnderlineThickness > 0, m.ToString());
        Assert.True(m.StrikeoutPosition < 0 && -m.StrikeoutPosition < m.Ascent, m.ToString());
        Assert.True(m.StrikeoutThickness > 0, m.ToString());
    }

    [Fact]
    public void Metrics_ScaleWithTheSize()
    {
        var pipeline = Pipeline();
        var small = pipeline.MeasureFont(Style(FontFamilies.Monospace, 12f));
        var large = pipeline.MeasureFont(Style(FontFamilies.Monospace, 24f));
        Assert.Equal(2 * small.CharacterWidth, large.CharacterWidth, 1);
        Assert.Equal(2 * small.LineHeight, large.LineHeight, 1);
        Assert.Equal(2 * small.UnderlineThickness, large.UnderlineThickness, 1);
    }

    [Fact]
    public void AProportionalFace_SaysSo()
    {
        Assert.False(Pipeline().MeasureFont(Style(FontFamilies.SansSerif)).IsFixedPitch);
    }

    // ──── Glyphs ────

    [Fact]
    public void HasGlyph_IsTheFacesOwn_NotAFallbacks()
    {
        var pipeline = Pipeline();
        var mono = Style(FontFamilies.Monospace);
        Assert.True(pipeline.HasGlyph(mono, 'A'));
        Assert.True(pipeline.HasGlyph(mono, '0'));
        // Text still draws these, from a fallback font; the face itself has neither.
        Assert.False(pipeline.HasGlyph(mono, 0x1F600));  // an emoji
        Assert.False(pipeline.HasGlyph(mono, 0x10FFFD)); // the last private-use code point
    }
}
