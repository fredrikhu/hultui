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
        // Built with the host's separator: on Linux a backslash is an ordinary character,
        // so Path.GetFileName would leave a whole "C:\clips\talk.mkv" as the name.
        var talk = Path.Combine(Path.GetTempPath(), "clips", "talk.mkv");
        using var app = new VideoPlayerApp(session.Application, session.Window.Window, () => backend, talk);
        session.Application.SetRoot(app.Mount());
        session.PumpFrames(2);

        Assert.Equal(talk, app.CurrentFile);
        Assert.Contains($"Open(file:{talk})", backend.Calls);
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

        var clip = Path.Combine(Path.GetTempPath(), "video", "clip.mp4");
        session.Window.DropFiles(new Point(500, 350), [clip]);
        session.PumpFrame();

        Assert.Equal(clip, app.CurrentFile);
        Assert.Contains($"Open(file:{clip})", backend.Calls);
        Assert.NotNull(session.Window.FindByText("clip.mp4"));
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
