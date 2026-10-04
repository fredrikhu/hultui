using System.Globalization;
using System.Text;
using HultUI.Core.Primitives;
using Xunit;
using static HultUI.Terminal.Tests.TerminalControllerTests;

namespace HultUI.Terminal.Tests;

/// <summary>
/// Pictures a program shows (Sixel, the kitty graphics protocol): laid on the grid in the cells
/// they took, at the engine's cell size the element sets, under or over the text by kitty's z,
/// cut at the grid's edge, held only while on screen, animated by the frame clock.
/// </summary>
public class TerminalPictureTests
{
    private const string Esc = "\x1b", St = "\x1b\\";

    private static string Apc(string control, string payload = "") =>
        payload.Length == 0 ? $"{Esc}_G{control}{St}" : $"{Esc}_G{control};{payload}{St}";

    /// <summary>A solid block of RGBA, as kitty's f=32 carries it.</summary>
    private static string Rgba(int width, int height, byte r, byte g, byte b)
    {
        var bytes = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++) (bytes[i * 4], bytes[i * 4 + 1], bytes[i * 4 + 2], bytes[i * 4 + 3]) = (r, g, b, 255);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>A solid red Sixel picture, <paramref name="width"/> by a multiple of six.</summary>
    private static string Sixel(int width, int bands)
    {
        var sb = new StringBuilder($"{Esc}Pq\"1;1;{width};{bands * 6}#0;2;100;0;0#0");
        for (int b = 0; b < bands; b++) sb.Append(b > 0 ? "-" : "").Append('!').Append(width).Append('~');
        return sb.Append(St).ToString();
    }

    /// <summary>The engine's cell in pixels, as the element set it: the cell at scale 1, rounded.</summary>
    private static (int W, int H) EngineCell(Term t)
    {
        var (w, h) = t.Element!.CellSize;
        return ((int)Math.Round(w), (int)Math.Round(h));
    }

    private static List<string> Images(Term t)
    {
        t.Window.RenderLog.Clear();
        t.Session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        return t.Window.RenderLog.Named("DrawImage").Select(o => o.Args).ToList();
    }

    private static string R(float v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

    [Fact]
    public void ASixelPicture_FillsTheCellsItTook()
    {
        using var t = Term.Shown(recording: true);
        var (cw, ch) = EngineCell(t);
        // Three cells wide at the engine's cell size: three cells on screen, whatever the cell's
        // fraction of a pixel. Left at the engine's default 10 x 20, it took 3 x 10 / cw cells.
        t.Emit(Sixel(3 * cw, 1));
        var drawn = Assert.Single(Images(t));

        var box = t.Element!.ScreenContentBox;
        var (w, h) = t.Element.CellSize;
        Assert.StartsWith($"({R(box.X)},{R(box.Y)} {R(3 * w)}x{R(6f / ch * h)})", drawn);
        Assert.Contains($"source=(0,0 {3 * cw}x6)", drawn);
    }

    [Fact]
    public void APicture_IsDrawn_ThePixelsAreThere()
    {
        using var t = Term.Shown();
        var (cw, _) = EngineCell(t);
        t.Emit(Sixel(4 * cw, 2));
        t.Session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);

        using var image = t.Window.CapturePixels();
        using var bitmap = SkiaSharp.SKBitmap.FromImage(image);
        var box = t.Element!.ScreenContentBox;
        var inside = bitmap.GetPixel((int)(box.X + 2), (int)(box.Y + 2));
        Assert.Equal((255, 0, 0), (inside.Red, inside.Green, inside.Blue));
    }

    [Fact]
    public void AKittyPicture_UnderTheText_IsDrawnBeforeIt_OverIt_After()
    {
        using var t = Term.Shown(recording: true);
        t.Emit(Apc("a=T,i=1,f=32,s=8,v=8,z=-1,C=1,q=2", Rgba(8, 8, 0, 255, 0)) + "text");
        Assert.True(Order(t) < 0); // the picture, then the row's text

        t.Emit("\x1b[2J\x1b[H" + Apc("a=T,i=2,f=32,s=8,v=8,z=1,C=1,q=2", Rgba(8, 8, 0, 255, 0)) + "text");
        Assert.True(Order(t) > 0);

        static int Order(Term t)
        {
            t.Window.RenderLog.Clear();
            t.Session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
            var calls = t.Window.RenderLog.Ops.Select(c => c.Name).ToList();
            int image = calls.IndexOf("DrawImage"), text = calls.IndexOf("DrawText");
            Assert.True(image >= 0 && text >= 0);
            return image - text;
        }
    }

    [Fact]
    public void APicture_IsCutAtTheGridsRightEdge()
    {
        using var t = Term.Shown(recording: true);
        var (cw, _) = EngineCell(t);
        int cols = t.Controller.Columns;
        t.Emit($"\x1b[1;{cols - 1}H" + Sixel(4 * cw, 1)); // four cells wide, two columns left
        var drawn = Assert.Single(Images(t));

        var box = t.Element!.ScreenContentBox;
        var (w, _) = t.Element.CellSize;
        Assert.StartsWith($"({R(box.X + (cols - 2) * w)},", drawn);
        Assert.Contains($" {R(2 * w)}x", drawn);           // two cells drawn
        Assert.Contains($"source=(0,0 {2 * cw}x6)", drawn); // half the picture
    }

    [Fact]
    public void APictureCut_ShowsItsLeftPart_NotAllOfItSquashed()
    {
        // Recording too: the recorder forwards the crop, or every recorded test drew squashed.
        using var t = Term.Shown(recording: true);
        var (cw, _) = EngineCell(t);
        int cols = t.Controller.Columns;
        // Four cells wide, red on the left two, blue on the right two; two columns left.
        var bytes = new byte[4 * cw * 6 * 4];
        for (int i = 0; i < 4 * cw * 6; i++)
            (bytes[i * 4], bytes[i * 4 + 2], bytes[i * 4 + 3]) = i % (4 * cw) < 2 * cw ? ((byte)255, (byte)0, (byte)255) : ((byte)0, (byte)255, (byte)255);
        t.Emit($"\x1b[1;{cols - 1}H" + Apc($"a=T,f=32,s={4 * cw},v=6,q=2", Convert.ToBase64String(bytes)) + "\x1b[5;5H");
        t.Session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);

        using var image = t.Window.CapturePixels();
        using var bitmap = SkiaSharp.SKBitmap.FromImage(image);
        var box = t.Element!.ScreenContentBox;
        var (w, h) = t.Element.CellSize;
        var last = bitmap.GetPixel((int)(box.X + (cols - 0.5f) * w), (int)(box.Y + 6f / EngineCell(t).H * h / 2));
        Assert.Equal((255, 0, 0), (last.Red, last.Green, last.Blue));
    }

    [Fact]
    public void AnITerm2Picture_IsDrawn()
    {
        using var t = Term.Shown();
        var (cw, ch) = EngineCell(t);
        string png;
        using (var bitmap = new SkiaSharp.SKBitmap(3 * cw, ch))
        {
            bitmap.Erase(new SkiaSharp.SKColor(0, 0, 255));
            using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            png = Convert.ToBase64String(data.ToArray());
        }
        t.Emit($"\x1b]1337;File=inline=1;size={png.Length}:{png}\a\x1b[5;5H");
        t.Session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);

        using var image = t.Window.CapturePixels();
        using var pixels = SkiaSharp.SKBitmap.FromImage(image);
        var box = t.Element!.ScreenContentBox;
        var inside = pixels.GetPixel((int)(box.X + 2), (int)(box.Y + 2));
        Assert.Equal((0, 0, 255), (inside.Red, inside.Green, inside.Blue));
    }

    [Fact]
    public void AKittyPlaceholder_DrawsItsTileInEachCell()
    {
        // What tmux and image.nvim send: the picture named by the foreground colour, one cell a
        // placeholder character, each cell its own tile.
        using var t = Term.Shown(recording: true);
        var (cw, ch) = EngineCell(t);
        t.Emit(Apc($"a=t,i=7,f=32,s={2 * cw},v={ch},q=2", Rgba(2 * cw, ch, 0, 0, 255))
            + "\x1b[38;2;0;0;7m\U0010EEEE\U0010EEEE\x1b[39m");
        var drawn = Images(t);

        Assert.Equal(2, drawn.Count);
        var box = t.Element!.ScreenContentBox;
        var (w, _) = t.Element.CellSize;
        Assert.StartsWith($"({R(box.X)},", drawn[0]);
        Assert.StartsWith($"({R(box.X + w)},", drawn[1]);
        Assert.Contains($"source=({cw},0 {cw}x{ch})", drawn[1]);
        Assert.DoesNotContain("\U0010EEEE", t.Controller.ReadScreen());
    }

    [Fact]
    public void APictureOffTheScreen_IsLetGo()
    {
        using var t = Term.Shown(recording: true);
        var (cw, _) = EngineCell(t);
        t.Emit(Sixel(2 * cw, 1));
        Images(t);
        Assert.Equal(1, t.Element!.HeldPictures);

        t.Emit("\x1b[2J");
        Assert.Empty(Images(t));
        Assert.Equal(0, t.Element.HeldPictures);
    }

    [Fact]
    public void AnAnimation_IsMovedOnByTheFrameClock()
    {
        using var t = Term.Shown(recording: true);
        t.Emit(Apc("a=t,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 255, 0, 0))
            + Apc("a=p,i=1,C=1,q=2")
            + Apc("a=f,i=1,c=1,z=100,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0))
            + Apc("a=a,i=1,r=1,z=100,q=2")
            + Apc("a=a,i=1,s=3,q=2")
            + "\x1b[5;5H"); // the cursor off the picture: C=1 left it on the picture's cell
        Images(t);
        Assert.True(t.Element!.PictureClockRunning);

        using (var first = t.Window.CapturePixels())
        using (var later = Frame(t, 150)) // the second frame: 100 ms on, before the loop comes round
        {
            Assert.NotEqual(Pixel(t, first), Pixel(t, later)); // red, then green
        }

        t.Emit(Apc("a=a,i=1,s=1,q=2")); // stopped
        Images(t);
        Assert.False(t.Element.PictureClockRunning);

        static SkiaSharp.SKImage Frame(Term t, int ms)
        {
            for (int i = 0; i < ms / 16 + 1; i++) t.Session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
            return t.Window.CapturePixels();
        }

        static (byte, byte, byte) Pixel(Term t, SkiaSharp.SKImage image)
        {
            using var bitmap = SkiaSharp.SKBitmap.FromImage(image);
            var box = t.Element!.ScreenContentBox;
            var c = bitmap.GetPixel((int)box.X + 1, (int)box.Y + 1);
            return (c.Red, c.Green, c.Blue);
        }
    }

    [Fact]
    public void AnAnimationOffTheScreen_StopsTheClock_AndGoesOnWhenShown()
    {
        using var t = Term.Shown(recording: true);
        t.Emit(Apc("a=t,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 255, 0, 0))
            + Apc("a=f,i=1,f=32,s=4,v=4,q=2", Rgba(4, 4, 0, 255, 0))
            + Apc("a=a,i=1,s=3,q=2")); // running, never placed
        Images(t);
        Assert.False(t.Element!.PictureClockRunning);

        t.Emit(Apc("a=p,i=1,C=1,q=2"));
        Images(t);
        Assert.True(t.Element.PictureClockRunning);

        t.Emit("\x1b[2J"); // cleared off while it still runs
        Images(t);
        Assert.False(t.Element.PictureClockRunning);
    }

    [Fact]
    public void WithPicturesOff_NoneIsDrawn_AndNoneIsOffered()
    {
        using var t = Term.Shown(new TerminalOptions { Images = false }, recording: true);
        var (cw, _) = EngineCell(t);
        t.Emit(Sixel(2 * cw, 1));
        Assert.Empty(Images(t));

        t.Emit("\x1b[c"); // DA1
        Assert.DoesNotContain(";4", t.Expect("c"));
    }

    [Fact]
    public void Sixel_IsOfferedByDefault()
    {
        using var t = Term.Shown();
        t.Emit("\x1b[c");
        Assert.Contains("4;", t.Expect("c"));
    }
}
