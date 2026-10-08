using Xunit;

namespace HultUI.Text.Tests;

/// <summary>
/// A run shaped the way TextPipeline shaped it before its word cache was looked up by span and
/// its glyphs went into an array of the exact size: every space / non-space segment cut out as
/// a string and shaped on its own, the glyphs collected in a list, each moved along by the
/// widths of the segments before it, then letter spacing applied as ShapeWithSpacing applies it.
/// A run with no space is its one segment as shaped; a right-to-left run is shaped whole. Built
/// from the shaper alone, with no cache: the pipeline's runs must be this, to the bit.
/// </summary>
internal static class WordModel
{
    public static (GlyphPosition[] Glyphs, float Width) Run(ITextShaper shaper, string text, IResolvedFont font,
        float fontSize, float letterSpacing, bool rtl = false)
    {
        GlyphPosition[] glyphs;
        float width;
        if (rtl || text.IndexOf(' ') < 0)
        {
            var whole = shaper.Shape(text, font, fontSize, rtl);
            (glyphs, width) = (whole.Glyphs, whole.Width);
        }
        else
        {
            var list = new List<GlyphPosition>(text.Length);
            float x = 0;
            for (int i = 0; i < text.Length;)
            {
                bool isSpace = text[i] == ' ';
                int start = i;
                while (i < text.Length && (text[i] == ' ') == isSpace) i++;
                var segment = shaper.Shape(text[start..i], font, fontSize);
                foreach (var g in segment.Glyphs)
                    list.Add(g with { X = g.X + x });
                x += segment.Width;
            }
            (glyphs, width) = (list.ToArray(), x);
        }

        if (letterSpacing == 0f || glyphs.Length == 0)
            return (glyphs, width);
        var spaced = new GlyphPosition[glyphs.Length];
        float shift = 0;
        for (int i = 0; i < glyphs.Length; i++)
        {
            var g = glyphs[i];
            spaced[i] = g with { X = g.X + shift, Advance = g.Advance + letterSpacing };
            shift += letterSpacing;
        }
        return (spaced, width + letterSpacing * glyphs.Length);
    }

    /// <summary>The run's glyphs and width are the model's, every float to the bit.</summary>
    public static void Same((GlyphPosition[] Glyphs, float Width) expected, ShapedRun actual, string context)
    {
        Assert.True(Bits(expected.Width) == Bits(actual.Width), $"{context}: width {actual.Width}, expected {expected.Width}");
        Assert.True(expected.Glyphs.Length == actual.Glyphs.Length,
            $"{context}: {actual.Glyphs.Length} glyphs, expected {expected.Glyphs.Length}");
        for (int g = 0; g < expected.Glyphs.Length; g++)
        {
            var e = expected.Glyphs[g];
            var a = actual.Glyphs[g];
            Assert.True(e.GlyphId == a.GlyphId && Bits(e.X) == Bits(a.X) && Bits(e.Y) == Bits(a.Y) && Bits(e.Advance) == Bits(a.Advance),
                $"{context}, glyph {g}: {a}, expected {e}");
        }
    }

    public static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
}
