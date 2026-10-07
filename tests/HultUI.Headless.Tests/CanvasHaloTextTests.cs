using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using SkiaSharp;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Canvas text over a halo, and canvas text measuring: what labels drawn over an image need to
/// stay legible on any background and to be placed clear of each other.
/// </summary>
public class CanvasHaloTextTests
{
    private const int Size = 120;

    private static HeadlessSession Draw(Action<ICanvasDrawingContext> draw)
    {
        var session = HeadlessSession.Create(Size, Size);
        session.Application.SetRoot(Box.New().Width(Size).Height(Size).Background(new Color(255, 255, 255))
            .Children(Canvas.New(draw).Width(Size).Height(Size)));
        session.PumpFrame();
        return session;
    }

    private static int CountDark(HeadlessSession session)
    {
        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        int dark = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Red < 80 && p.Green < 80 && p.Blue < 80) dark++;
            }
        return dark;
    }

    private static int CountYellow(HeadlessSession session)
    {
        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        int n = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.Red > 200 && p.Green > 180 && p.Blue < 90) n++;
            }
        return n;
    }

    [Fact]
    public void HaloText_PutsADarkRimAroundTheGlyphs_AndKeepsTheirColour()
    {
        var yellow = new Color(255, 220, 40);
        int plainDark;
        using (var plain = Draw(ctx => ctx.DrawText("M1 42 mm", new Point(10, 40), yellow, 16f)))
            plainDark = CountDark(plain);
        using var haloed = Draw(ctx => ctx.DrawText("M1 42 mm", new Point(10, 40), yellow, 16f, new Color(0, 0, 0), 3f));

        // On white, plain yellow text leaves nothing dark; the halo is a dark rim around it.
        Assert.Equal(0, plainDark);
        Assert.True(CountDark(haloed) > 60, $"halo pixels: {CountDark(haloed)}");
        // The glyphs themselves are still drawn in their colour, on top of the rim.
        Assert.True(CountYellow(haloed) > 20, $"text pixels: {CountYellow(haloed)}");
    }

    [Fact]
    public void HaloText_WithATransparentHalo_IsPlainText()
    {
        var yellow = new Color(255, 220, 40);
        using var haloed = Draw(ctx => ctx.DrawText("Text", new Point(10, 40), yellow, 16f, new Color(0, 0, 0, 0), 3f));
        Assert.Equal(0, CountDark(haloed));
        Assert.True(CountYellow(haloed) > 10);
    }

    [Fact]
    public void MeasureText_GrowsWithTheText_AndTheSize()
    {
        float shortWidth = 0, longWidth = 0, bigWidth = 0, empty = -1;
        using var session = Draw(ctx =>
        {
            shortWidth = ctx.MeasureText("12 mm", 14f);
            longWidth = ctx.MeasureText("L1 123.4 mm", 14f);
            bigWidth = ctx.MeasureText("12 mm", 28f);
            empty = ctx.MeasureText("", 14f);
        });

        Assert.True(shortWidth > 10, $"width {shortWidth}");
        Assert.True(longWidth > shortWidth * 1.5f);
        Assert.InRange(bigWidth / shortWidth, 1.8f, 2.2f);
        Assert.Equal(0, empty);
    }
}
