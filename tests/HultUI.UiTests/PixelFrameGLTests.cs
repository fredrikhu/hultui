using HultUI.Core.Builders;
using HultUI.Core.Rendering;
using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// The raster suite proves what a pixel frame draws; this proves the GPU backend
/// accepts the uncopied wrapper at all — a 1080p frame through a real GL context,
/// twice, with the borrow returned each time. Pixels are not read back (the GL
/// surface has no read path); an exception anywhere in upload or flush is the
/// failure this guards against.
/// </summary>
[Collection("ui")]
public sealed class PixelFrameGLTests
{
    [UiFact]
    public void A1080pFrame_RendersThroughTheGLSurface()
    {
        using var app = new Application(new SdlWindowingPlatform(), "frames", 320, 200);
        using var sink = new PixelFrameSink();
        var image = Image.New(sink).Width(300f).Height(180f);
        app.SetRoot(Box.New().Children(image));

        for (int i = 1; i <= 2; i++)
        {
            using (var writer = sink.BeginWrite(1920, 1080, PixelFormat.Bgrx8888))
            {
                writer.Pixels.Fill((byte)(i * 60));
                writer.Publish();
            }
            image.MarkLayoutDirty();
            // Forced: a second SDL application in one test process can report its first
            // unforced frame as not rendered, and that event-loop quirk is not what this
            // test is about.
            Assert.True(app.RunFrame(TimeSpan.FromMilliseconds(16), forceRender: true));
        }

        // Both borrows came back, so the ring has a free slot for a third frame.
        sink.Write(2, 2, PixelFormat.Rgba8888, new byte[16], stride: 8);
        Assert.Equal(3, sink.Version);
    }
}
