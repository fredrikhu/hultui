using System.Globalization;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Headless;
using HultUI.Terminal.Rendering;
using HultUI.Testing;
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
        // The cell's last column: its right edge snapped the way the renderer snaps cells. Taken
        // from the unsnapped edge, this read a column short wherever the edge rounds up.
        float edge = MathF.Round(box.X + 3 * w);
        Assert.True(MaxLit(bitmap, edge - 1, cy - 1, edge - 1, cy + 1) > 128, "the arm right");
    }

    /// <summary>
    /// Lines and a rounded corner in cells an odd number of pixels wide, drawn into fixed cells so
    /// the answer does not hang on the platform's monospace face. DejaVu Sans Mono, Linux's, has
    /// 7.82px cells, 7 or 8 pixels once snapped; the test above failed only there.
    /// <para>
    /// A one-pixel line belongs in the middle column, with three pixels either side. Snapping the
    /// cell's middle (19.5 → 20) before taking half the line off it moved the line a pixel right.
    /// The arc, given half the cell as its radius, then turned straight half a pixel past the
    /// cell's edge, so it was still curving where the next cell's line began: a notch in every
    /// join.
    /// </para>
    /// </summary>
    [Fact]
    public void InACellAnOddNumberOfPixelsWide_TheLineIsCentred_AndTheArcIsStraightByTheEdge()
    {
        using var session = HeadlessSession.Create(48, 24);
        // │ in [0,7), ╭ in [16,23) and ─ after it in [23,30): seven pixels wide, sixteen high.
        session.Window.SetRoot(new BoxDrawn(
            (0x2502, new Rect(0, 0, 7, 16)), (0x256D, new Rect(16, 0, 7, 16)), (0x2500, new Rect(23, 0, 7, 16)))
            .Width(48).Height(24));
        session.PumpFrame();
        using var image = session.Window.CapturePixels();
        using var bitmap = SkiaSharp.SKBitmap.FromImage(image);

        Assert.True(Lit(bitmap, 3, 12) > 200, "│ is in the cell's middle column");
        Assert.True(Lit(bitmap, 2, 12) < 64 && Lit(bitmap, 4, 12) < 64, "and only there");

        // The corner's straight parts are the same pixels as the lines they join: its down arm in
        // │'s column, its right arm on ─'s row, and nearly straight already in the cell's last
        // column — a curve still bending there leaves a step where ─ begins.
        Assert.True(Lit(bitmap, 16 + 3, 15) > 200, "the arm down is in the middle column");
        Assert.True(Lit(bitmap, 23, 8) > 200, "─ runs on ╭'s row");
        Assert.True(Lit(bitmap, 22, 8) > 200, "the arc is straight by the cell's edge");
        Assert.True(Lit(bitmap, 19, 8) < 64, "and keeps off the square corner's join");
    }

    /// <summary>Box-drawing characters drawn straight into the given cells, at scale 1.</summary>
    private sealed class BoxDrawn : BoxElement
    {
        private readonly (int CodePoint, Rect Cell)[] _cells;

        public BoxDrawn(params (int CodePoint, Rect Cell)[] cells)
        {
            _cells = cells;
            BackgroundColor = Color.Black;
        }

        public override void Render(IRenderContext ctx)
        {
            base.Render(ctx);
            var origin = Layout.AbsoluteContentBox;
            foreach (var (cp, cell) in _cells)
                Assert.True(BoxDrawing.TryDraw(ctx, cp,
                    cell with { X = origin.X + cell.X, Y = origin.Y + cell.Y }, Color.White, 1f));
        }
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

    /// <summary>
    /// Every Powerline separator on screen is drawn again on every frame the terminal draws, a
    /// keystroke's echo included. Each was a new <c>SKPath</c>, and each half-circle a new array of
    /// its 25 points as well. The shapes are built in the render context's one kept path, the
    /// half-circle's points on the stack.
    /// </summary>
    [Fact]
    public void PowerlineSeparators_AllocateNothingPerFrame()
    {
        using var surface = new HultUI.Rendering.Skia.SkiaRasterRenderSurface();
        surface.Resize(200, 20, 1f);
        var ctx = surface.RenderContext;
        bool drawn = true;

        void Frame()
        {
            ctx.BeginFrame(200, 20, 1f, Color.Black);
            // Every separator: solid and thin, triangles, half-circles and diagonals.
            for (int cp = 0xE0B0; cp <= 0xE0BF; cp++)
                drawn &= BoxDrawing.TryDraw(ctx, cp, new Rect((cp - 0xE0B0) * 12, 1, 9, 18), Color.White, 1f);
            ctx.EndFrame();
        }

        Frame();
        Frame();
        double perFrame = Allocations.PerCall(50, Frame);

        Assert.True(drawn);
        Assert.True(perFrame < 1, $"drawing the 16 Powerline separators allocated {perFrame:0.#} B per frame");
    }

    /// <summary>A terminal frame with six Powerline prompt lines on screen allocates no more than
    /// the same lines with each separator a plain character. With the demo shell's boxes page on
    /// screen six times, a frame was 12.6 KB; it is 1.7 KB, against 1.8 KB with the separators as
    /// text (plan 17, round 3). What is left is the rows' text.</summary>
    [Fact]
    public void AFrameOfPowerlinePromptLines_AllocatesNoMoreThanTheSameLinesInText()
    {
        const string Prompt =
            "\x1b[97;44m user \x1b[34;46m\uE0B0\x1b[30m ~/src \x1b[36;49m\uE0B0\x1b[0m \uE0B1 \uE0B3 " +
            "\x1b[35m\uE0B6\x1b[97;45m 12:44 \x1b[35;49m\uE0B4\x1b[0m \x1b[32m\uE0BA\x1b[30;42m main \x1b[32;49m\uE0B8\x1b[0m " +
            "\uE0B2 \uE0B5 \uE0B7 \uE0B9 \uE0BB \uE0BC \uE0BD \uE0BE \uE0BF\r\n";
        string text = Prompt;
        for (char separator = '\uE0B0'; separator <= '\uE0BF'; separator++)
            text = text.Replace(separator, '>');

        double powerline = PerFrame(Prompt), plain = PerFrame(text);

        Assert.True(powerline <= plain + 64,
            $"a frame of six Powerline prompt lines allocated {powerline:0.#} B; in text, {plain:0.#} B");

        static double PerFrame(string line)
        {
            using var t = Term.Shown(width: 800);
            t.Emit(string.Concat(Enumerable.Repeat(line, 6)));
            Assert.Equal(6, t.Screen().Split('\n').Count(row => row.Contains("main", StringComparison.Ordinal)));
            var frame = TimeSpan.FromMilliseconds(16);
            for (int i = 0; i < 4; i++) t.Session.PumpFrame(frame, forceRender: true);
            return Allocations.PerCall(50, () => t.Session.PumpFrame(frame, forceRender: true));
        }
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
