using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// <see cref="Window.Owner"/>, <see cref="Window.ShowWithoutActivation"/> and
/// <see cref="Window.FrameBorderColor"/>: what the framework keeps itself — the owner's list, the
/// owned windows closing first — and what it hands the platform, which headless records.
/// </summary>
public class OwnedWindowTests
{
    private static HeadlessPlatformWindow Platform(HeadlessWindow w) => (HeadlessPlatformWindow)w.Window.PlatformWindow;

    [Fact]
    public void AnOwner_IsHandedToThePlatform_AndKeepsTheList()
    {
        using var session = HeadlessSession.Create(200, 200);
        var tool = session.CreateWindow("tool", 100, 100);

        tool.Window.Owner = session.Window.Window;

        Assert.Same(session.Window.Window.PlatformWindow, Platform(tool).Owner);
        Assert.Equal([tool.Window], session.Window.Window.OwnedWindows);

        tool.Window.Owner = null;

        Assert.Null(Platform(tool).Owner);
        Assert.Empty(session.Window.Window.OwnedWindows);
    }

    [Fact]
    public void ClosingTheOwner_ClosesWhatItOwns_First()
    {
        using var session = HeadlessSession.Create(200, 200);
        var tool = session.CreateWindow("tool", 100, 100);
        var detail = session.CreateWindow("detail", 100, 100);
        tool.Window.Owner = session.Window.Window;
        detail.Window.Owner = tool.Window;

        List<string> closed = [];
        session.Window.Window.Closed += () => closed.Add("main");
        tool.Window.Closed += () => closed.Add("tool " + (session.Window.Window.IsOpen ? "before main" : "after main"));
        detail.Window.Closed += () => closed.Add("detail");

        session.Window.Window.Close();

        // Down the line, each while its owner is still open.
        Assert.Equal(["detail", "tool before main", "main"], closed);
        Assert.False(tool.Window.IsOpen);
        Assert.False(detail.Window.IsOpen);
    }

    [Fact]
    public void AnOwnedWindowThatCloses_LeavesItsOwnersList()
    {
        using var session = HeadlessSession.Create(200, 200);
        var tool = session.CreateWindow("tool", 100, 100);
        tool.Window.Owner = session.Window.Window;

        tool.Window.Close();

        Assert.Empty(session.Window.Window.OwnedWindows);
        Assert.Null(tool.Window.Owner);
        Assert.True(session.Window.Window.IsOpen);
    }

    [Fact]
    public void AWindow_CannotOwnItself_NorAWindowThatOwnsIt()
    {
        using var session = HeadlessSession.Create(200, 200);
        var tool = session.CreateWindow("tool", 100, 100);
        tool.Window.Owner = session.Window.Window;

        Assert.Throws<ArgumentException>(() => session.Window.Window.Owner = session.Window.Window);
        Assert.Throws<ArgumentException>(() => session.Window.Window.Owner = tool.Window);
        Assert.Null(session.Window.Window.Owner);
    }

    [Fact]
    public void ShowWithoutActivation_AndTheBorderColour_ReachThePlatform()
    {
        using var session = HeadlessSession.Create(200, 200);
        var window = session.Window;

        window.Window.ShowWithoutActivation = true;
        window.Window.FrameBorderColor = new Color(52, 73, 94);

        Assert.True(Platform(window).ShowWithoutActivation);
        Assert.Equal(new Color(52, 73, 94), Platform(window).FrameBorderColor);

        window.Window.FrameBorderColor = null;
        Assert.Null(Platform(window).FrameBorderColor);
    }
}
