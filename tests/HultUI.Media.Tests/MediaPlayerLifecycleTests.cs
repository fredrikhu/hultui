using HultUI.Headless;
using HultUI.Media.Testing;
using Xunit;

namespace HultUI.Media.Tests;

/// <summary>
/// A player pauses when its app leaves the screen (docs/design/APP-LIFECYCLE.md §6: the user's
/// decision, 2026-11-25), and nothing resumes it on the return. Playing on in the background is
/// a later opt-in.
/// </summary>
public class MediaPlayerLifecycleTests
{
    private static readonly MediaSource Clip = MediaSource.FromFile(@"C:\clips\intro.mp4");

    [Fact]
    public void EnteringTheBackground_PausesPlayback_AndTheReturnLeavesItPaused()
    {
        using var session = HeadlessSession.Create(200, 100);
        var backend = new FakeMediaBackend();
        using var player = new MediaPlayer(backend);
        player.Open(Clip);
        player.Play();
        Assert.True(player.IsPlaying);

        session.EnterBackground();

        Assert.Equal(PlaybackState.Paused, player.State);
        session.EnterForeground();
        session.PumpFrame();
        Assert.Equal(PlaybackState.Paused, player.State);
    }

    [Fact]
    public void APausedPlayer_IsNotTouched()
    {
        using var session = HeadlessSession.Create(200, 100);
        var backend = new FakeMediaBackend();
        using var player = new MediaPlayer(backend);
        player.Open(Clip);
        backend.Calls.Clear();

        session.EnterBackground();

        Assert.Empty(backend.Calls);
    }

    [Fact]
    public void ADisposedPlayer_NoLongerFollowsTheApp()
    {
        using var session = HeadlessSession.Create(200, 100);
        var backend = new FakeMediaBackend();
        var player = new MediaPlayer(backend);
        player.Open(Clip);
        player.Play();
        player.Dispose();
        backend.Calls.Clear();

        session.EnterBackground();

        Assert.DoesNotContain("Pause", backend.Calls);
    }
}
