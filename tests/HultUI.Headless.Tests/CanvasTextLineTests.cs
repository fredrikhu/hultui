using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;
using SkiaSharp;
using Xunit;

namespace HultUI.Headless.Tests;

using Text = HultUI.Core.Builders.Text;

/// <summary>
/// <see cref="ICanvasDrawingContext.DrawTextLine"/>: a long document's visible lines drawn on a
/// canvas through the same pipeline as labels, recorded in the render log like every canvas op,
/// and pinned for golden images as labels are.
/// </summary>
public class CanvasTextLineTests
{
    [Fact]
    public void ACanvasDrawingThreeLines_RecordsThreeDrawTextLineOps()
    {
        using var session = HeadlessSession.Create(300, 100, options: HeadlessOptions.DeterministicRecording());
        var style = TextMeasure.StyleFor(13f, FontFamilies.Monospace) with { TextColor = new Color(0, 0, 0) };
        var spans = new[] { new TextSpan(0, 4, new Color(200, 0, 0)), new TextSpan(5, 3, Color.Transparent, new Color(255, 255, 0)) };
        session.Window.SetRoot(Canvas.New(ctx =>
        {
            ctx.DrawTextLine("0000 de ad be ef", new Point(0, 0), style, spans);
            ctx.DrawTextLine("0010 ca fe", new Point(0, 16), style);
            ctx.DrawTextLine("0020 00 01", new Point(-8, 32), style, []);
        }).Width(300f).Height(100f));
        session.PumpFrame();

        var lines = session.Window.RenderLog.Named("Canvas.DrawTextLine").Select(o => o.Args).ToList();

        Assert.Equal(
        [
            "\"0000 de ad be ef\" (0,0) size=13 #000000 spans=2",
            "\"0010 ca fe\" (0,16) size=13 #000000 spans=0",
            "\"0020 00 01\" (-8,32) size=13 #000000 spans=0",
        ], lines);
    }

    [Fact]
    public void UnderPinnedText_ALineDrawsInThePinnedFace_AsALabelDoes()
    {
        // The canvas's own DrawText draws in a pinned typeface of its own (CanvasTypeface); a text
        // line goes through the label pipeline instead, so it must be pinned the way labels are:
        // pixel for pixel the label's.
        const string text = "Pinned 0x7F3A Åäö";
        var style = TextMeasure.StyleFor(16f) with { TextColor = new Color(0, 0, 0) };

        byte[] label = Pixels(Box.New().Width(300f).Height(60f).Background(new Color(255, 255, 255)).Padding(10f)
            .Children(Text.New(text).FontSize(16f).TextColor(new Color(0, 0, 0))));
        byte[] line = Pixels(Box.New().Width(300f).Height(60f).Background(new Color(255, 255, 255)).Padding(10f)
            .Children(Canvas.New(ctx => ctx.DrawTextLine(text, new Point(0, 0), style)).Width(280f).Height(40f)));

        Assert.Contains(label, b => b < 100);       // the label drew ink: two blank frames prove nothing
        Assert.Equal(label, line);
    }

    private static byte[] Pixels(Core.Elements.Element root)
    {
        using var session = HeadlessSession.Create(300, 60, options: HeadlessOptions.Golden());
        session.Window.SetRoot(root);
        session.PumpFrame();
        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.Bytes;
    }
}
