using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// Pixel-level checks for <see cref="TextSpan.Background"/>: the highlight rect
/// paints under exactly the span's character range, and a transparent span
/// foreground keeps the element's text color instead of hiding the glyphs.
/// </summary>
public class SkiaTextSpanRenderTests
{
    private static ResolvedInheritedStyle Style(Color textColor) => new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: textColor, TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    [Fact]
    public void BackgroundSpan_PaintsRectUnderSpanRangeOnly()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(300, 60, 1f);

        // Transparent text color: no glyph ink, so every painted pixel is the
        // span background — the rect's horizontal extent can be asserted alone.
        var style = Style(Color.Transparent);
        const string text = "0123456789";
        var spans = new[] { new TextSpan(5, 5, Color.Transparent, Color.Red) };

        var ctx = surface.RenderContext;
        ctx.BeginFrame(300, 60, 1f, Color.White);
        ctx.DrawText(text, new Rect(0, 0, 280, 40), style, TextOverflow.Clip, null, spans);
        ctx.EndFrame();
        surface.Present();

        float boundary = surface.MeasureContext.MeasureText("01234", style, 280, null).Width;
        float total = surface.MeasureContext.MeasureText(text, style, 280, null).Width;
        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);

        // Middle of the highlighted half: red. Middle of the unhighlighted
        // half and past the text: the white clear color.
        AssertPixel(pixels, rowBytes, x: (int)((boundary + total) / 2), y: 10, r: 255, g: 0, b: 0);
        AssertPixel(pixels, rowBytes, x: (int)(boundary / 2), y: 10, r: 255, g: 255, b: 255);
        AssertPixel(pixels, rowBytes, x: (int)total + 10, y: 10, r: 255, g: 255, b: 255);
    }

    [Fact]
    public void TransparentSpanColor_KeepsElementTextColor()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(300, 60, 1f);

        var style = Style(Color.Black);
        const string text = "00000";
        // Background-only span: foreground transparent = "element text color".
        var spans = new[] { new TextSpan(0, text.Length, Color.Transparent, Color.Red) };

        var ctx = surface.RenderContext;
        ctx.BeginFrame(300, 60, 1f, Color.White);
        ctx.DrawText(text, new Rect(0, 0, 280, 40), style, TextOverflow.Clip, null, spans);
        ctx.EndFrame();
        surface.Present();

        float total = surface.MeasureContext.MeasureText(text, style, 280, null).Width;
        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);

        // Glyphs must still render: some pixel in the text area is dark.
        bool inkFound = false;
        for (int y = 0; y < 30 && !inkFound; y++)
            for (int x = 0; x < (int)total && !inkFound; x++)
            {
                int i = y * rowBytes + x * 4;
                inkFound = pixels[i] < 128 && pixels[i + 1] < 128 && pixels[i + 2] < 128;
            }
        Assert.True(inkFound, "Expected dark glyph pixels — a transparent span color must not hide the text.");
    }

    private static byte[] Draw(SkiaRasterRenderSurface surface, string text, ResolvedInheritedStyle style, TextSpan[] spans)
    {
        var ctx = surface.RenderContext;
        ctx.BeginFrame(400, 40, 1f, Color.White);
        ctx.DrawText(text, new Rect(0, 0, 390, 30), style, TextOverflow.Clip, 1, spans);
        ctx.EndFrame();
        surface.Present();
        return surface.ReadPixels(out _, out _, out _);
    }

    /// <summary>
    /// A span of transparent colour over coloured ones — a find match over the tokens of a
    /// TextArea, whose spans may overlap — leaves the text the colour they give it: the line
    /// draws as it does without that span, pixel for pixel, whether the span starts where a
    /// token starts (and comes first in the list, as an unstable sort by start may put it), sits
    /// inside one, or runs out of one, across a gap and into the next. Until round 6 of plan 17
    /// the renderer took it as the element's colour and painted those characters in it.
    /// </summary>
    [Fact]
    public void ATransparentSpanOverColouredOnes_LeavesThemTheirColour()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(400, 40, 1f);
        var style = Style(Color.Black);
        const string text = "keyword value other";
        TextSpan a = new(0, 7, Color.Red), b = new(8, 5, Color.Blue), c = new(14, 5, new Color(0, 160, 0, 255));
        var tokens = Draw(surface, text, style, [a, b, c]);

        foreach (var (start, length) in new[] { (0, 7), (3, 7), (8, 11), (2, 2), (6, 9) })
        {
            var over = new TextSpan(start, length, Color.Transparent);
            // In start order, the transparent span first among equal starts.
            TextSpan[] spans = [.. new[] { over, a, b, c }.OrderBy(s => s.Start)];
            var drawn = Draw(surface, text, style, spans);
            Assert.True(tokens.AsSpan().SequenceEqual(drawn),
                $"a transparent span over [{start}, {start + length}) changed the colour of the text under it");
        }
    }

    /// <summary>In a list that does not overlap, as a label's or a field's, a span of transparent
    /// colour draws as no span: the element's colour, between spans or after the last.</summary>
    [Fact]
    public void ATransparentSpanBesideColouredOnes_DrawsAsNoSpan()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(400, 40, 1f);
        var style = Style(Color.Black);
        const string text = "keyword value other";
        TextSpan a = new(0, 7, Color.Red), b = new(8, 5, Color.Blue);
        var alone = Draw(surface, text, style, [a, b]);
        var beside = Draw(surface, text, style,
            [a, new TextSpan(7, 1, Color.Transparent), b, new TextSpan(13, 6, Color.Transparent)]);
        Assert.True(alone.AsSpan().SequenceEqual(beside), "a transparent span between and after coloured ones changed the line");
    }

    private static void AssertPixel(byte[] pixels, int rowBytes, int x, int y, byte r, byte g, byte b)
    {
        int i = y * rowBytes + x * 4;
        Assert.True(pixels[i] == r && pixels[i + 1] == g && pixels[i + 2] == b,
            $"Pixel ({x},{y}) was rgba({pixels[i]},{pixels[i + 1]},{pixels[i + 2]},{pixels[i + 3]}), expected rgb({r},{g},{b})");
    }
}
