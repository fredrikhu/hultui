using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using SkiaSharp;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// Baseline alignment with real fonts, checked where it matters: in the pixels. The layout
/// tests prove the arithmetic with a made-up font; this proves the baseline the layout works
/// from is the one the text is drawn on. Capitals with flat bottoms (H, E, F, T, L, I) end
/// exactly on the baseline — no overshoot, no descenders — so the lowest dark row under each
/// word is where its baseline was drawn, and a baseline-aligned row puts them all on one.
/// </summary>
public class BaselineAlignPixelTests
{
    private static TextElement Word(string text, float size)
    {
        var word = TextElement.New(text);
        word.Inherited.FontSize = size;
        return word;
    }

    /// <summary>The lowest row inside <paramref name="area"/> holding a dark pixel.</summary>
    private static int LowestInk(SKBitmap bitmap, Rect area)
    {
        for (int y = (int)MathF.Floor(area.Bottom) - 1; y >= (int)area.Y; y--)
            for (int x = (int)area.X; x < (int)area.Right; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Red + c.Green + c.Blue < 3 * 110) return y;
            }
        return -1;
    }

    [Fact]
    public void TwoSizesOfTextAndAFieldSitOnOnePixelRow()
    {
        using var session = HeadlessSession.Create(700, 200);
        var big = Word("HEFT", 30);
        var small = Word("LIFT", 12);
        var field = TextInput.New("TILE").Width(160f).Height(44f);
        var row = Flex.Row(gap: 24).Padding(20f).AlignItems(AlignItems.Baseline).Children(big, small, field);
        session.Window.SetRoot(Box.New().Children(row));
        session.PumpFrame();

        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        int bigBottom = LowestInk(bitmap, big.ScreenBorderBox);
        int smallBottom = LowestInk(bitmap, small.ScreenBorderBox);
        // Inside the field's content box, clear of its border.
        int fieldBottom = LowestInk(bitmap, field.ScreenContentBox);

        Assert.True(bigBottom > 0 && smallBottom > 0 && fieldBottom > 0, "a word drew no ink");
        // Antialiasing can leave the last partly covered row on either side of the line.
        Assert.InRange(smallBottom, bigBottom - 1, bigBottom + 1);
        Assert.InRange(fieldBottom, bigBottom - 1, bigBottom + 1);
    }
}
