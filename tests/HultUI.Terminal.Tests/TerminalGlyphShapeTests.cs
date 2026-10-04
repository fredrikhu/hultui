using System.Globalization;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Terminal.Tests;

/// <summary>
/// Shapes the terminal draws itself and glyphs it fits: rounded box corners as arcs, the Powerline
/// separators a prompt uses (no common face has them), and a glyph from a fallback font fitted to
/// its cells rather than cut off at their edge.
/// </summary>
public class TerminalGlyphShapeTests
{
    /// <summary>How much of the foreground a pixel shows, 0 to 255, on the default black.</summary>
    private static int Lit(SkiaSharp.SKBitmap bitmap, float x, float y)
    {
        var c = bitmap.GetPixel((int)x, (int)y);
        return Math.Max(c.Red, Math.Max(c.Green, c.Blue));
    }

    private static int MaxLit(SkiaSharp.SKBitmap bitmap, float x0, float y0, float x1, float y1)
    {
        int most = 0;
        for (int y = (int)y0; y <= (int)y1; y++)
            for (int x = (int)x0; x <= (int)x1; x++)
                most = Math.Max(most, Lit(bitmap, x, y));
        return most;
    }

    [Fact]
    public void ARoundedCorner_IsAnArc_ThatMeetsItsArms()
    {
        using var t = Term.Shown();
        t.Emit("┌ ╭"); // ┌ and ╭, a cell apart
        t.Session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        using var image = t.Window.CapturePixels();
        using var bitmap = SkiaSharp.SKBitmap.FromImage(image);
        var box = t.Element!.ScreenContentBox;
        var (w, h) = t.Element.CellSize;
        // The centres the lines are drawn on, snapped to whole pixels as the renderer snaps them
        // (scale 1): the join is the pixel either side of each.
        float cy = MathF.Round(MathF.Round(box.Y) + h / 2);
        float square = MathF.Round(MathF.Round(box.X) + w / 2), round = MathF.Round(MathF.Round(box.X + 2 * w) + w / 2);

        // Where the arms join: lit in the square corner, clear in the round one.
        Assert.True(MaxLit(bitmap, square - 1, cy - 1, square, cy) > 128, "the square corner's join");
        Assert.True(MaxLit(bitmap, round - 1, cy - 1, round, cy) < 64, "the round corner keeps off its join");
        // The arc's arms still reach the cell's edges, where the next cells' lines go on.
        Assert.True(MaxLit(bitmap, round - 1, box.Y + h - 1, round + 1, box.Y + h - 1) > 128, "the arm down");
        Assert.True(MaxLit(bitmap, box.X + 3 * w - 1, cy - 1, box.X + 3 * w - 1, cy + 1) > 128, "the arm right");
    }

    [Fact]
    public void PowerlineSeparators_AreDrawn_FillingTheirCells()
    {
        using var t = Term.Shown(recording: true);
        t.Window.RenderLog.Clear();
        t.Emit("\x1b[31m\x1b[0m"); // a solid triangle and a solid half-circle, in red
        t.Session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        Assert.DoesNotContain(t.Window.RenderLog.Named("DrawText"), o => o.Args.Contains('') || o.Args.Contains(''));
        Assert.True(t.Window.RenderLog.Named("FillPolygon").Count() >= 2, "drawn as shapes");

        using var image = t.Window.CapturePixels();
        using var bitmap = SkiaSharp.SKBitmap.FromImage(image);
        var box = t.Element!.ScreenContentBox;
        var (w, h) = t.Element.CellSize;
        float cy = box.Y + h / 2;
        for (int cell = 0; cell < 2; cell++)
        {
            float left = box.X + cell * w;
            Assert.True(Lit(bitmap, left + 1, cy) > 128, $"cell {cell}: filled by its flat side");
            Assert.True(Lit(bitmap, left + w - 1, box.Y + 1) < 64, $"cell {cell}: clear in the far corner");
        }
        Assert.True(Lit(bitmap, box.X + 2 * w - 1, cy) > 128, "the half-circle reaches the cell's far edge");
    }

    [Fact]
    public void AWideGlyph_WiderThanItsCells_IsDrawnSmaller()
    {
        using var t = Term.Shown(recording: true);
        var options = t.Controller.Options;
        var style = TextMeasure.StyleFor(options.FontSize, options.ResolvedFontFamily);
        float glyph = TextMeasure.MeasureWidth("\U0001F600", style); // 😀, from the emoji font
        var (w, _) = t.Element!.CellSize;

        t.Window.RenderLog.Clear();
        t.Emit("\U0001F600");
        float size = SizeOf(t, "\U0001F600");

        // Clipped to its two cells at its own size, its right edge was cut off.
        if (glyph > 2 * w) Assert.Equal(options.FontSize * 2 * w / glyph, size, 0.05f);
        else Assert.Equal(options.FontSize, size, 0.01f);
    }

    [Fact]
    public void AOneCellGlyph_WiderThanItsCell_SpillsIntoABlankCell_AndShrinksBeforeText()
    {
        using var t = Term.Shown(recording: true);
        var options = t.Controller.Options;
        var style = TextMeasure.StyleFor(options.FontSize, options.ResolvedFontFamily);
        var (w, _) = t.Element!.CellSize;
        // A character the face lacks whose fallback is wider than a cell; which one depends on the
        // platform's fonts.
        string? wide = new[] { "⇒", "★", "∀", "ༀ" }
            .FirstOrDefault(c => TextMeasure.HasGlyph(style, char.ConvertToUtf32(c, 0)) == false && TextMeasure.MeasureWidth(c, style) > w);
        if (wide is null) return; // nothing here to show it with

        t.Window.RenderLog.Clear();
        t.Emit(wide + " \r\n" + wide + "x");
        var sizes = t.Window.RenderLog.Named("DrawText").Where(o => o.Args.StartsWith("\"" + wide + "\"", StringComparison.Ordinal))
            .Select(o => Size(o.Args)).ToList();

        Assert.Equal(2, sizes.Count);
        Assert.Equal(options.FontSize, sizes[0], 0.01f); // room after it: its own size
        Assert.True(sizes[1] < options.FontSize, "text after it: smaller, to fit");
    }

    private static float SizeOf(Term t, string text) =>
        Size(t.Window.RenderLog.Named("DrawText").First(o => o.Args.StartsWith("\"" + text + "\"", StringComparison.Ordinal)).Args);

    private static float Size(string args)
    {
        int at = args.IndexOf("size=", StringComparison.Ordinal) + 5;
        int end = args.IndexOf(' ', at);
        return float.Parse(args[at..end], CultureInfo.InvariantCulture);
    }
}
