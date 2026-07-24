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

    private static void AssertPixel(byte[] pixels, int rowBytes, int x, int y, byte r, byte g, byte b)
    {
        int i = y * rowBytes + x * 4;
        Assert.True(pixels[i] == r && pixels[i + 1] == g && pixels[i + 2] == b,
            $"Pixel ({x},{y}) was rgba({pixels[i]},{pixels[i + 1]},{pixels[i + 2]},{pixels[i + 3]}), expected rgb({r},{g},{b})");
    }
}
