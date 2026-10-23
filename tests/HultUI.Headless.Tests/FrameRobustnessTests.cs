using HultUI.Core.Builders;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using SkiaSharp;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// One bad frame is one bad frame, and a frame is never drawn inside another of the same window:
/// what a draw that throws, or that waits and so pumps the live-resize loop's messages, must
/// leave behind.
/// </summary>
public sealed class FrameRobustnessTests
{
    private static SKColor PixelAt(HeadlessSession session, int x, int y)
    {
        using var image = session.Window.CapturePixels();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(x, y);
    }

    [Fact]
    public void AFrameThatThrowsInsideAClip_LeavesTheNextFrameWhole()
    {
        using var session = HeadlessSession.Create(200, 100);
        bool throwOnce = false;
        var right = new Color(255, 0, 0);
        session.Application.SetRoot(Flex.Row().Children(
            // The throwing draw sits inside an overflow-hidden box: its clip is pushed when it throws.
            Box.New().Width(100).Height(100).Overflow(Overflow.Hidden).Children(
                Canvas.New(_ =>
                {
                    if (!throwOnce) return;
                    throwOnce = false;
                    throw new InvalidOperationException("A frame is already being written");
                }).Width(100).Height(100)),
            // Outside that clip, and a new colour in each frame.
            Canvas.New(c => c.DrawRect(new Rect(0, 0, 100, 100), right)).Width(100).Height(100)));
        session.PumpFrame();
        Assert.Equal(new SKColor(255, 0, 0), PixelAt(session, 150, 50));

        throwOnce = true;
        Assert.Throws<InvalidOperationException>(() => session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true));

        right = new Color(0, 128, 0);
        session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        Assert.Equal(new SKColor(0, 128, 0), PixelAt(session, 150, 50));
    }

    [Fact]
    public void ALiveResizeFrameAskedForInsideAFrame_IsNotDrawnInsideIt_ButAfter()
    {
        using var session = HeadlessSession.Create(200, 100);
        var window = session.Window.Window;
        var platform = window.PlatformWindow;
        int depth = 0, deepest = 0, draws = 0;
        bool pump = false;
        session.Application.SetRoot(Canvas.New(_ =>
        {
            draws++;
            depth++;
            deepest = Math.Max(deepest, depth);
            try
            {
                // What a Parallel.For on the UI thread does inside a draw on Windows: the size
                // loop's messages are dispatched, and SDL's watcher asks for a live-resize frame.
                if (pump)
                {
                    pump = false;
                    platform.ResizePending = true;
                    platform.LiveResizeFrame!.Invoke();
                }
            }
            finally { depth--; }
        }).Width(200).Height(100));
        session.PumpFrame();

        pump = true;
        draws = 0;
        session.PumpFrame(TimeSpan.FromMilliseconds(16), forceRender: true);
        Assert.Equal(1, deepest);
        Assert.Equal(1, draws);
        Assert.False(window.IsDrawingFrame);
        Assert.True(platform.ResizePending, "the nested request waits for the next frame");

        // The drag's next event draws the new size.
        platform.LiveResizeFrame!.Invoke();
        Assert.Equal(2, draws);
        Assert.False(platform.ResizePending);
    }
}
