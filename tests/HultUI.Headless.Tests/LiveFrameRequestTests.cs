using HultUI.Core.Builders;
using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The application's half of a GPU source's request for a frame inside Win32's modal size/move
/// loop: the platform calls the window's live frame for it, and the frame draws though nothing is
/// dirty and nothing is posted, as a decoder's newest picture is taken by the draw itself. Once:
/// the next live frame with nothing new draws nothing. The real window half is
/// <c>LiveResizeFrameTests</c> in HultUI.UiTests; headless platforms never call the live frame on
/// their own.
/// </summary>
public class LiveFrameRequestTests
{
    [Fact]
    public void ALiveFrame_DrawsForARequest_ThoughNothingIsDirty_AndOnlyOnce()
    {
        using var session = HeadlessSession.Create(200, 100);
        int frames = 0;
        session.Application.SetRoot(Box.New().Children(Canvas.New(_ => frames++).Width(1f).Height(1f)));
        session.PumpFrame();
        var live = session.Window.Window.PlatformWindow.LiveResizeFrame
            ?? throw new InvalidOperationException("the application set no live frame");

        int before = frames;
        live();
        Assert.Equal(before, frames);   // nothing new: nothing drawn

        LiveFrameRequests.SetPoster(() => { });
        try
        {
            LiveFrameRequests.Request();
            live();
            Assert.Equal(before + 1, frames);
            live();
            Assert.Equal(before + 1, frames);
        }
        finally
        {
            LiveFrameRequests.SetPoster(null);
        }
    }
}
