using HultUI.Core.Accessibility;
using HultUI.Core.Primitives;
using HultUI.Headless;
using HultUI.Media;
using HultUI.Media.Testing;
using HultUI.Samples.VideoPlayer;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace HultUI.Samples.VideoPlayer.Tests;

/// <summary>
/// The player window over the fake backend: files reach it from the command line and
/// from a drop, it says so in its title, and without a backend it explains itself.
/// </summary>
public class VideoPlayerSampleTests
{
    [Fact]
    public void AFileFromTheCommandLine_OpensAndPlays()
    {
        using var session = HeadlessSession.Create(1100, 700);
        var backend = new FakeMediaBackend();
        using var app = new VideoPlayerApp(session.Application, session.Window.Window, () => backend, @"C:\clips\talk.mkv");
        session.Application.SetRoot(app.Mount());
        session.PumpFrames(2);

        Assert.Equal(@"C:\clips\talk.mkv", app.CurrentFile);
        Assert.Contains("Open(file:C:\\clips\\talk.mkv)", backend.Calls);
        Assert.True(app.Player!.IsPlaying);
        Assert.NotNull(session.Window.FindByText("talk.mkv"));
        Assert.Contains("talk.mkv", session.Window.Window.Title);
        Assert.NotNull(session.Window.FindByRole(AccessibilityRole.Button, "Pause"));
        Assert.NotNull(session.Window.FindByRole(AccessibilityRole.Button, "Open file"));
    }

    [Fact]
    public void ADroppedFile_Opens()
    {
        using var session = HeadlessSession.Create(1100, 700);
        var backend = new FakeMediaBackend();
        using var app = new VideoPlayerApp(session.Application, session.Window.Window, () => backend);
        session.Application.SetRoot(app.Mount());
        session.PumpFrames(2);
        Assert.Null(app.CurrentFile);

        session.Window.DropFiles(new Point(500, 350), [@"D:\video\clip.mp4"]);
        session.PumpFrame();

        Assert.Equal(@"D:\video\clip.mp4", app.CurrentFile);
        Assert.Contains("Open(file:D:\\video\\clip.mp4)", backend.Calls);
    }

    [Fact]
    public void WithoutABackend_TheWindowExplains()
    {
        using var session = HeadlessSession.Create(1100, 700);
        using var app = new VideoPlayerApp(session.Application, session.Window.Window, backends: null);
        session.Application.SetRoot(app.Mount());
        session.PumpFrame();

        Assert.Null(app.Player);
        Assert.NotNull(session.Window.FindByText("No libmpv found"));
        Assert.Null(session.Window.FindByRole(AccessibilityRole.Button, "Play"));
    }
}
