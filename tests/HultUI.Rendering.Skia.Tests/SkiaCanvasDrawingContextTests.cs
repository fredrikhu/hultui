using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using HultUI.Rendering.Skia;
using Xunit;

namespace HultUI.Rendering.Skia.Tests;

/// <summary>
/// The canvas drawing context is one reusable instance drawing through the render
/// context's pooled Skia objects. That buys a zero-allocation commit graph and two new
/// ways to be wrong, both covered here: a stale Bounds surviving into the next canvas,
/// and stroke state leaking from one pooled draw into a fill.
/// </summary>
public class SkiaCanvasDrawingContextTests
{
    [Fact]
    public void ConsecutiveCanvases_EachDrawAtTheirOwnBounds()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(100, 100, 1f);
        var ctx = surface.RenderContext;

        ctx.BeginFrame(100, 100, 1f, Color.White);
        // Two canvas elements in one frame, same local rect, different bounds —
        // the shape of two commit-graph gutters in one History render.
        var left = ctx.CreateCanvasContext(new Rect(0, 0, 50, 100));
        left.DrawRect(new Rect(10, 10, 20, 20), Color.Red);
        var right = ctx.CreateCanvasContext(new Rect(50, 0, 50, 100));
        right.DrawRect(new Rect(10, 10, 20, 20), Color.Blue);
        ctx.EndFrame();
        surface.Present();

        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);
        AssertPixel(pixels, rowBytes, x: 20, y: 20, r: 255, g: 0, b: 0);    // left canvas
        AssertPixel(pixels, rowBytes, x: 70, y: 20, r: 0, g: 0, b: 255);    // right canvas, offset by its bounds
        AssertPixel(pixels, rowBytes, x: 20, y: 60, r: 255, g: 255, b: 255);
    }

    [Fact]
    public void StrokeStateFromOneDraw_DoesNotLeakIntoTheNextFill()
    {
        using var surface = new SkiaRasterRenderSurface();
        surface.Resize(100, 100, 1f);
        var ctx = surface.RenderContext;

        ctx.BeginFrame(100, 100, 1f, Color.White);
        var canvas = ctx.CreateCanvasContext(new Rect(0, 0, 100, 100));
        // Each fill-path draw comes DIRECTLY after a stroked one — nothing in between
        // that would scrub the pooled paint on its own. If IsStroke/StrokeWidth
        // survived, the circle and rect would render as outlines with hollow middles.
        canvas.DrawLine(new Point(0, 90), new Point(100, 90), Color.Black, 2f);
        canvas.DrawCircle(new Point(75, 40), 10f, Color.Blue);
        canvas.DrawCircleStroke(new Point(75, 40), 14f, Color.Black, 1f);
        canvas.DrawRect(new Rect(20, 20, 40, 40), Color.Red);
        ctx.EndFrame();
        surface.Present();

        var pixels = surface.ReadPixels(out _, out _, out int rowBytes);
        AssertPixel(pixels, rowBytes, x: 75, y: 40, r: 0, g: 0, b: 255);    // circle interior is filled
        AssertPixel(pixels, rowBytes, x: 40, y: 40, r: 255, g: 0, b: 0);    // rect interior is filled
    }

    private static ResolvedInheritedStyle Style(Color textColor) => new(
        FontFamily: "sans-serif", FontSize: 16f, FontWeight: FontWeight.Regular,
        TextColor: textColor, TextDirection: TextDirection.Ltr, Cursor: CursorType.Default);

    private const string Line = "The quick brown fox, 0x7F3A";

    private static readonly TextSpan[] Spans =
    [
        new(4, 5, new Color(200, 0, 0)),
        new(10, 5, Color.Transparent, new Color(255, 255, 0)),
        new(16, 3, new Color(0, 0, 200), Decoration: TextDecoration.Underline),
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawTextLine_PaintsWhatALeftAlignedLabelPaints_AndReturnsItsWidth(bool spanned)
    {
        var style = Style(Color.Black) with { TextAlign = TextAlign.Left };
        var spans = spanned ? Spans : null;

        float width = 0;
        var line = Render(ctx =>
        {
            var canvas = ctx.CreateCanvasContext(new Rect(10, 5, 300, 50));
            width = canvas.DrawTextLine(Line, new Point(7, 3), style, spans);
        }, out var surface);
        var label = Render(ctx =>
            ctx.DrawText(Line, new Rect(17, 8, 2000, 40), style, TextOverflow.Clip, 1, spans), out _);

        Assert.Equal(label, line);
        Assert.Equal(surface.MeasureContext.MeasureText(Line, style, 2000, 1).Width, width);
        surface.Dispose();
    }

    [Fact]
    public void DrawTextLine_ABackgroundSpan_PaintsOnlyUnderItsRange()
    {
        // Transparent text: every painted pixel is the span's background.
        var style = Style(Color.Transparent);
        const string text = "0123456789";
        var spans = new[] { new TextSpan(5, 5, Color.Transparent, Color.Red) };

        var pixels = Render(ctx =>
            ctx.CreateCanvasContext(new Rect(0, 0, 300, 60)).DrawTextLine(text, new Point(0, 0), style, spans),
            out var surface, out int rowBytes);

        float boundary = surface.MeasureContext.MeasureText("01234", style, 280, null).Width;
        float total = surface.MeasureContext.MeasureText(text, style, 280, null).Width;
        surface.Dispose();
        AssertPixel(pixels, rowBytes, x: (int)((boundary + total) / 2), y: 10, r: 255, g: 0, b: 0);
        AssertPixel(pixels, rowBytes, x: (int)(boundary / 2), y: 10, r: 255, g: 255, b: 255);
        AssertPixel(pixels, rowBytes, x: (int)total + 10, y: 10, r: 255, g: 255, b: 255);
    }

    [Fact]
    public void DrawTextLine_CentredRightToLeft_StillDrawsFromTheOrigin_AndReturnsAFiniteWidth()
    {
        // An infinite box would put a centred or right-to-left line at infinity: there is none.
        var style = Style(Color.Black) with { TextAlign = TextAlign.Center, TextDirection = TextDirection.Rtl };

        float width = 0;
        var pixels = Render(ctx =>
            width = ctx.CreateCanvasContext(new Rect(0, 0, 300, 60)).DrawTextLine("abc def", new Point(40, 10), style),
            out var surface, out int rowBytes);
        surface.Dispose();

        Assert.True(float.IsFinite(width) && width > 0, $"width {width}");
        int left = int.MaxValue, right = -1;
        for (int y = 0; y < 60; y++)
            for (int x = 0; x < 300; x++)
                if (pixels[y * rowBytes + x * 4] < 128) { left = Math.Min(left, x); right = Math.Max(right, x); }
        Assert.InRange(left, 40, 44);
        Assert.InRange(right, 40, 40 + (int)Math.Ceiling(width));
    }

    [Fact]
    public void DrawTextLine_APieceEndingInBlanks_ChainsWithTheNext_AndItsBlanksTakeABackground()
    {
        // A hex row drawn in pieces: the blank after the address belongs to the address, and a
        // selection over trailing blanks is painted under them.
        var style = Style(Color.Black);
        var highlight = new[] { new TextSpan(4, 2, Color.Transparent, Color.Red) };
        float address = 0, bytes = 0, joined = 0, blanks = 0, bare = 0;
        var pixels = Render(ctx =>
        {
            var canvas = ctx.CreateCanvasContext(new Rect(0, 0, 300, 60));
            address = canvas.DrawTextLine("0000: ", new Point(0, 0), style);
            bytes = canvas.DrawTextLine("7F", new Point(address, 0), style);
            joined = MeasureLine(canvas, "0000: 7F", style);
            blanks = canvas.DrawTextLine("0000  ", new Point(0, 20), style, highlight);
            bare = MeasureLine(canvas, "0000", style);
        }, out var surface, out int rowBytes);
        surface.Dispose();

        Assert.Equal(joined, address + bytes, 2);
        Assert.True(blanks > bare, $"blanks {blanks}, bare {bare}");
        int red = 0;
        for (int x = (int)bare; x < (int)blanks; x++)
        {
            int i = 30 * rowBytes + x * 4;
            if (pixels[i] > 200 && pixels[i + 1] < 50) red++;
        }
        Assert.True(red >= (int)(blanks - bare) - 2, $"{red} red pixels under the blanks");
    }

    // Drawn off the surface, for its width alone.
    private static float MeasureLine(ICanvasDrawingContext canvas, string text, ResolvedInheritedStyle style) =>
        canvas.DrawTextLine(text, new Point(0, 1000), style);

    [Fact]
    public void DrawTextLine_InATransparentStyle_DrawsNothing_ButReturnsTheWidth()
    {
        float invisible = 0, visible = 0;
        var pixels = Render(ctx =>
        {
            var canvas = ctx.CreateCanvasContext(new Rect(0, 0, 300, 60));
            invisible = canvas.DrawTextLine("hidden column", new Point(0, 0), Style(Color.Transparent));
        }, out var surface);
        Render(ctx => visible = ctx.CreateCanvasContext(new Rect(0, 0, 300, 60))
            .DrawTextLine("hidden column", new Point(0, 0), Style(Color.Black)), out var other);
        surface.Dispose();
        other.Dispose();

        Assert.DoesNotContain(pixels, b => b < 255);
        Assert.True(invisible > 0);
        Assert.Equal(visible, invisible);
    }

    private static byte[] Render(Action<IRenderContext> draw, out SkiaRasterRenderSurface surface) =>
        Render(draw, out surface, out _);

    private static byte[] Render(Action<IRenderContext> draw, out SkiaRasterRenderSurface surface, out int rowBytes)
    {
        surface = new SkiaRasterRenderSurface();
        surface.Resize(300, 60, 1f);
        var ctx = surface.RenderContext;
        ctx.BeginFrame(300, 60, 1f, Color.White);
        draw(ctx);
        ctx.EndFrame();
        surface.Present();
        return surface.ReadPixels(out _, out _, out rowBytes);
    }

    private static void AssertPixel(byte[] pixels, int rowBytes, int x, int y, byte r, byte g, byte b)
    {
        int i = y * rowBytes + x * 4;
        Assert.True(pixels[i] == r && pixels[i + 1] == g && pixels[i + 2] == b,
            $"Pixel ({x},{y}) was rgba({pixels[i]},{pixels[i + 1]},{pixels[i + 2]},{pixels[i + 3]}), expected rgb({r},{g},{b})");
    }
}
