using HultUI.Core.Rendering;
using Xunit;

namespace HultUI.Core.Tests.Rendering;

/// <summary>
/// A GPU source's request for a frame reaches the platform only while it has set a poster (its
/// window is in Win32's modal size/move loop), and leaves a request waiting for the next frame drawn
/// there. The real window half is <c>LiveResizeFrameTests</c> in HultUI.UiTests.
/// </summary>
public class LiveFrameRequestsTests
{
    [Fact]
    public void WithoutAPoster_ARequestDoesNothing()
    {
        LiveFrameRequests.SetPoster(null);
        LiveFrameRequests.Request();
        Assert.False(LiveFrameRequests.TakeWaiting());
    }

    [Fact]
    public void WithAPoster_EachRequestPosts_AndOneFrameTakesThemAll()
    {
        int posted = 0;
        LiveFrameRequests.SetPoster(() => posted++);
        try
        {
            LiveFrameRequests.Request();
            LiveFrameRequests.Request();
            Assert.Equal(2, posted);
            Assert.True(LiveFrameRequests.TakeWaiting());
            Assert.False(LiveFrameRequests.TakeWaiting());
        }
        finally
        {
            LiveFrameRequests.SetPoster(null);
        }
    }

    [Fact]
    public void LeavingTheLoop_DropsAWaitingRequest()
    {
        LiveFrameRequests.SetPoster(() => { });
        LiveFrameRequests.Request();
        LiveFrameRequests.SetPoster(null);
        Assert.False(LiveFrameRequests.TakeWaiting());
    }
}
