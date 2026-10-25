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

    /// <summary>
    /// A GPU source that signals only by posting — any <c>IGpuImageSource</c> outside HultUI.Media
    /// and WebView, which never ask — is drawn for its waiting post on every live frame, as SDL's
    /// timer drew it before requests were answered; it froze for the whole move or held drag once a
    /// window answering requests stopped drawing for posted work. Posted work is no reason to draw
    /// only while sources are asking for their frames themselves: up to 100 ms after the last frame
    /// drawn for a request.
    /// </summary>
    [Fact]
    public void ALiveFrame_DrawsForPostedWork_ExceptWhileASourceAsksForItsFrames()
    {
        using var session = HeadlessSession.Create(200, 100);
        int frames = 0;
        session.Application.SetRoot(Box.New().Children(Canvas.New(_ => frames++).Width(1f).Height(1f)));
        session.PumpFrame();
        var live = session.Window.Window.PlatformWindow.LiveResizeFrame
            ?? throw new InvalidOperationException("the application set no live frame");

        // The window is in the size/move loop: requests are answered.
        LiveFrameRequests.SetPoster(() => { });
        try
        {
            // A post the held loop cannot run: drawn, and again on the next live frame while it waits.
            Application.InvokeOnMainThread(() => { });
            int before = frames;
            live();
            Assert.Equal(before + 1, frames);
            session.AdvanceTime(TimeSpan.FromMilliseconds(16));
            live();
            Assert.Equal(before + 2, frames);

            // A source asks: its frame draws, and for 100 ms the post is no reason to draw again.
            LiveFrameRequests.Request();
            live();
            Assert.Equal(before + 3, frames);
            session.AdvanceTime(TimeSpan.FromMilliseconds(50));
            live();
            Assert.Equal(before + 3, frames);
            session.AdvanceTime(TimeSpan.FromMilliseconds(49));
            live();
            Assert.Equal(before + 3, frames);

            // It stopped asking (the video paused): the post draws again.
            session.AdvanceTime(TimeSpan.FromMilliseconds(2));
            live();
            Assert.Equal(before + 4, frames);
        }
        finally
        {
            LiveFrameRequests.SetPoster(null);
            session.PumpFrame();   // runs the post
        }
    }
}
