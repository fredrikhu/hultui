using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A frame source drawn by an <see cref="ImageElement"/> under the headless host: the
/// render log names the frame's version, which is how an application test asserts
/// "frame n reached the screen" without a decoder or a display.
/// </summary>
public class PixelFrameRecordingTests
{
    private static void Publish(PixelFrameSink sink, int width, int height, byte value)
    {
        using var writer = sink.BeginWrite(width, height, PixelFormat.Rgba8888);
        writer.Pixels.Fill(value);
        writer.Publish();
    }

    [Fact]
    public void EachPublishedFrame_IsPaintedWithItsVersion()
    {
        using var session = HeadlessSession.Create(200, 200,
            options: new HeadlessOptions { RecordDrawCalls = true });
        using var sink = new PixelFrameSink();
        var image = Image.New(sink).Width(100f).Height(100f);
        session.Application.SetRoot(Box.New().Children(image));

        // Nothing published yet: the element has no frame and draws nothing.
        session.PumpFrame();
        Assert.DoesNotContain("DrawImage", session.Window.RenderLog.ToString());

        Publish(sink, 4, 4, 1);
        image.MarkLayoutDirty();       // the intrinsic size went from zero to 4x4
        session.Window.RenderLog.Clear();
        session.PumpFrame();
        Assert.Contains("version=1", session.Window.RenderLog.ToString());

        Publish(sink, 4, 4, 2);
        image.MarkPaintDirty();        // what the producer's FramePublished handler does
        session.Window.RenderLog.Clear();
        session.PumpFrame();
        var log = session.Window.RenderLog.ToString();
        Assert.Contains("version=2", log);
        Assert.DoesNotContain("version=1", log);
    }

    [Fact]
    public void TheBorrow_IsReturnedByTheEndOfTheFrame()
    {
        using var session = HeadlessSession.Create(200, 200);
        using var sink = new PixelFrameSink();
        session.Application.SetRoot(Box.New().Children(Image.New(sink).Width(100f).Height(100f)));
        Publish(sink, 4, 4, 1);
        session.PumpFrame();

        // With the frame handed back, two more publishes fit in the ring and the
        // third reuses the released slot rather than failing for want of one.
        Publish(sink, 4, 4, 2);
        Publish(sink, 4, 4, 3);
        Publish(sink, 4, 4, 4);
        Assert.Equal(4, sink.Version);
    }
}
