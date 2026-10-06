using System.Numerics;
using HultUI.Core.Builders;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Rendering;
using SkiaSharp;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The pixels a canvas gets from <see cref="ICanvasDrawingContext.DrawImage"/>: a Gray8 frame
/// drawn magnified, cut to a source rectangle, resampled as asked, and placed by a transform.
/// What an image viewer for inspecting pixels is built from, checked on the frame itself.
/// </summary>
public class ImageSamplingPixelTests
{
    private const int Size = 160;

    /// <summary>A 2x2 grey checker: black, white / white, black.</summary>
    private static PixelFrameSink Checker()
    {
        var sink = new PixelFrameSink();
        sink.Write(2, 2, PixelFormat.Gray8, [0, 255, 255, 0], stride: 2);
        return sink;
    }

    private static SKColor PixelAt(HeadlessSession session, int x, int y)
    {
        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(x, y);
    }

    private static HeadlessSession Draw(Action<ICanvasDrawingContext> draw)
    {
        var session = HeadlessSession.Create(Size, Size);
        session.Application.SetRoot(Box.New().Width(Size).Height(Size).Background(new Color(0, 0, 255))
            .Children(Canvas.New(draw).Width(Size).Height(Size)));
        session.PumpFrame();
        return session;
    }

    [Fact]
    public void NearestSampling_KeepsMagnifiedPixelsHardEdged()
    {
        using var sink = Checker();
        using var session = Draw(ctx => ctx.DrawImage(sink, new Rect(0, 0, Size, Size), new Rect(0, 0, 2, 2), ImageSampling.Nearest));

        // Each source pixel is an 80x80 block; just either side of the seam the levels are pure.
        Assert.Equal(new SKColor(0, 0, 0), PixelAt(session, 78, 40));
        Assert.Equal(new SKColor(255, 255, 255), PixelAt(session, 81, 40));
        Assert.Equal(new SKColor(255, 255, 255), PixelAt(session, 40, 120));
        Assert.Equal(new SKColor(0, 0, 0), PixelAt(session, 120, 120));
    }

    [Fact]
    public void LinearSampling_BlendsAcrossTheSeam()
    {
        using var sink = Checker();
        using var session = Draw(ctx => ctx.DrawImage(sink, new Rect(0, 0, Size, Size), new Rect(0, 0, 2, 2), ImageSampling.Linear));

        var atSeam = PixelAt(session, 80, 40);
        Assert.InRange(atSeam.Red, 40, 215);           // a blend, neither black nor white
        Assert.Equal(atSeam.Red, atSeam.Green);         // and still grey
        Assert.Equal(atSeam.Red, atSeam.Blue);
    }

    [Fact]
    public void ASourceRect_CutsTheFrame()
    {
        using var sink = Checker();
        // Only the top-right pixel (white), stretched over the whole canvas.
        using var session = Draw(ctx => ctx.DrawImage(sink, new Rect(0, 0, Size, Size), new Rect(1, 0, 1, 1), ImageSampling.Nearest));

        Assert.Equal(new SKColor(255, 255, 255), PixelAt(session, 5, 5));
        Assert.Equal(new SKColor(255, 255, 255), PixelAt(session, Size - 5, Size - 5));
    }

    [Fact]
    public void PushTransform_WorksInCanvasCoordinates_AndPopAllUnwindsALeak()
    {
        using var sink = Checker();
        var mirror = Matrix3x2.CreateScale(-1f, 1f, new Vector2(Size / 2f, Size / 2f));
        using var session = Draw(ctx =>
        {
            ctx.PushTransform(mirror);   // deliberately never popped
            ctx.DrawImage(sink, new Rect(0, 0, Size, Size), new Rect(0, 0, 2, 2), ImageSampling.Nearest);
        });

        // Mirrored: the top-left block is now the source's top-right pixel, white.
        Assert.Equal(new SKColor(255, 255, 255), PixelAt(session, 40, 40));
        Assert.Equal(new SKColor(0, 0, 0), PixelAt(session, 120, 40));
    }

    [Fact]
    public void PushClip_LimitsTheDrawing()
    {
        using var sink = Checker();
        using var session = Draw(ctx =>
        {
            ctx.PushClip(new Rect(0, 0, Size / 2f, Size));
            ctx.DrawImage(sink, new Rect(0, 0, Size, Size), new Rect(1, 0, 1, 1), ImageSampling.Nearest);
            ctx.Pop();
        });

        Assert.Equal(new SKColor(255, 255, 255), PixelAt(session, 20, 20));
        Assert.Equal(new SKColor(0, 0, 255), PixelAt(session, Size - 20, 20));   // the background shows
    }

    [Fact]
    public void AGrayFrame_DrawnByAnImageElement_IsOpaqueGrey()
    {
        using var session = HeadlessSession.Create(64, 64);
        using var sink = new PixelFrameSink();
        sink.Write(1, 1, PixelFormat.Gray8, [128], stride: 1);
        session.Application.SetRoot(Box.New().Background(new Color(255, 0, 0))
            .Children(Image.New(sink).Width(64f).Height(64f).ImageFit(ImageFit.Fill)));
        session.PumpFrame();

        var c = PixelAt(session, 32, 32);
        Assert.Equal(c.Red, c.Green);
        Assert.Equal(c.Green, c.Blue);
        Assert.InRange(c.Red, 120, 136);
        Assert.Equal(255, c.Alpha);
    }

    [Fact]
    public void CanvasImageDraws_AreRecordedWithTheirSampling()
    {
        using var session = HeadlessSession.Create(64, 64, options: new HeadlessOptions { RecordDrawCalls = true });
        using var sink = Checker();
        session.Application.SetRoot(Canvas.New(ctx =>
            ctx.DrawImage(sink, new Rect(0, 0, 64, 64), new Rect(0, 0, 2, 2), ImageSampling.Cubic)).Width(64f).Height(64f));
        session.PumpFrame();

        var log = session.Window.RenderLog.ToString();
        Assert.Contains("Canvas.DrawImage", log);
        Assert.Contains("sampling=Cubic", log);
        Assert.Contains("version=1", log);
    }
}
