using System.Diagnostics;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;
using HultUI.Platform.SDL3;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// The real loop paces rendered frames to the display. With the swap interval at 0 a
/// tween used to render as fast as the GPU returned (a 300 ms fade: two thousand
/// frames); now it renders about one frame per display interval.
/// </summary>
[Collection("ui")]
public sealed class FramePacingTests
{
    [UiFact]
    public void ATween_RendersAboutOneFramePerDisplayInterval()
    {
        var platform = new SdlWindowingPlatform();
        using var app = new Application(platform, "pacing", 200, 100);
        int frames = 0;
        float shade = 0f;
        var box = Box.New().Width(200f).Height(100f);
        var counter = Canvas.New(_ => frames++).Width(1f).Height(1f);
        app.SetRoot(Box.New().Children(box, counter));
        app.RunFrame(TimeSpan.Zero, forceRender: true);

        const float seconds = 0.5f;
        Animate.Value(0f, 1f, v => { shade = v; box.Background(new Color((byte)(v * 255), 0, 0)); box.MarkPaintDirty(); })
            .Duration(seconds)
            .OnComplete(() => app.PrimaryWindow.Close())   // runs on the UI thread, inside the tick
            .Start();

        frames = 0;
        var clock = Stopwatch.StartNew();
        app.Run();

        // Enough frames to be the animation, few enough to be paced: no display here
        // runs above 240 Hz, and the unpaced loop managed ~2500 frames a second.
        Assert.InRange(frames, seconds * 60 * 0.4f, seconds * 240 * 1.5f);
        Assert.Equal(1f, shade);
        Assert.InRange(clock.ElapsedMilliseconds, 400, 2000);
    }
}
