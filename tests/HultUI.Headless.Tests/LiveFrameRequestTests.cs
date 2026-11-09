using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
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

    /// <summary>An app's own GPU source: not a sink, so it can only signal its frames by posting.</summary>
    private sealed class OwnGpuSource : IGpuImageSource
    {
        public Size IntrinsicSize => new(4, 4);
        public bool IsLoaded => true;
        public string? AlternateText => null;
        public bool TryRenderFrame(IExternalGLSurface surface, out GpuFrame frame)
        {
            frame = default;
            return false;
        }
    }

    /// <summary>A producer that never has a frame, for a sink to stand in front of.</summary>
    private sealed class IdleRenderer : IGpuFrameRenderer
    {
        public void OnModeDecided(GpuSinkMode mode) { }
        public void PrepareGL(Func<string, nint> getProcAddress) { }
        public bool TryGetFrameSize(out int width, out int height)
        {
            width = height = 0;
            return false;
        }
        public bool RendersBottomUp => true;
        public bool Render(Func<string, nint> getProcAddress, in GpuFrameTarget target, int width, int height) => false;
        public void ReleaseGL() { }
    }

    /// <summary>
    /// A post the held loop cannot run — the web view's placement, queued at a drag's first step
    /// and waiting for its end, or an await's continuation — changes nothing by waiting. It drew the
    /// same picture again on every live frame: SDL's timer, 64 to 65 frames a second while an edge
    /// was held, and a second frame at every step (plan 17 round 9, gl-vk-live-resize-stuck-post).
    /// Now a post draws once, at the first live frame after it was made, while no GPU source that
    /// signals only by posting can be on screen; the sinks of a player and a web view ask for
    /// their frames instead.
    /// </summary>
    [Fact]
    public void ALiveFrame_DrawsForAWaitingPost_Once()
    {
        Assert.False(GpuFrameSink.MaySignalOnlyByPosting,
            "an earlier test left a GpuFrameSink undisposed, or an image holding a GPU source");
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
            int before = frames;
            Application.InvokeOnMainThread(() => { });
            live();
            Assert.Equal(before + 1, frames);
            for (int i = 0; i < 10; i++)
            {
                session.AdvanceTime(TimeSpan.FromMilliseconds(16));
                live();
            }
            Assert.Equal(before + 1, frames);   // waiting since: nothing new

            // Another post: once more.
            Application.InvokeOnMainThread(() => { });
            live();
            live();
            Assert.Equal(before + 2, frames);

            // A player's or a web view's sink on screen: it asks for its frames, so still once.
            using (new GpuFrameSink(new PixelFrameSink(), new IdleRenderer(), asksForLiveFrames: true))
            {
                live();
                Assert.Equal(before + 2, frames);
            }

            // A sink an app made for its own renderer may signal only by posting: every live frame.
            using (new GpuFrameSink(new PixelFrameSink(), new IdleRenderer()))
            {
                live();
                live();
                Assert.Equal(before + 4, frames);
            }

            // The posts ran: the next ones are new, though as many wait as when the last frame drew.
            session.PumpFrame();
            before = frames;
            Application.InvokeOnMainThread(() => { });
            Application.InvokeOnMainThread(() => { });
            live();
            Assert.Equal(before + 1, frames);
        }
        finally
        {
            LiveFrameRequests.SetPoster(null);
            session.PumpFrame();   // runs the posts
        }
    }

    /// <summary>
    /// A GPU source that signals only by posting — any <c>IGpuImageSource</c> outside HultUI.Media
    /// and WebView, which never ask — is drawn for its waiting post on every live frame, as SDL's
    /// timer drew it before requests were answered; it froze for the whole move or held drag once a
    /// window answering requests stopped drawing for posted work. It keeps one post in flight, so
    /// no new post comes while that one waits. Posted work is no reason to draw only while sources
    /// are asking for their frames themselves: up to 100 ms after the last frame drawn for a
    /// request.
    /// </summary>
    [Fact]
    public void ALiveFrame_DrawsForASourceThatOnlyPosts_ExceptWhileASourceAsksForItsFrames()
    {
        using var session = HeadlessSession.Create(200, 100);
        int frames = 0;
        var image = ImageElement.New(new OwnGpuSource());
        session.Application.SetRoot(Box.New().Children(Canvas.New(_ => frames++).Width(1f).Height(1f), image));
        session.PumpFrame();
        var live = session.Window.Window.PlatformWindow.LiveResizeFrame
            ?? throw new InvalidOperationException("the application set no live frame");

        // The window is in the size/move loop: requests are answered.
        LiveFrameRequests.SetPoster(() => { });
        try
        {
            // The source's post, which the held loop cannot run: drawn, and again on the next live
            // frame while it waits.
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
            // An image holding one counts as a source that only posts until it lets go.
            image.Source = null;
        }
        Assert.False(GpuFrameSink.MaySignalOnlyByPosting);
    }
}
