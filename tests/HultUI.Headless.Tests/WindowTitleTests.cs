using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The window title is settable after creation, so an app can keep it tracking whatever
/// it currently has open. The headless platform records it rather than showing it, which
/// is enough to pin the contract the SDL3 window implements against SDL_SetWindowTitle.
/// </summary>
public class WindowTitleTests
{
    [Fact]
    public void Title_StartsAsTheCreationTitle()
    {
        using var session = HeadlessSession.Create(title: "Vesper Commander");
        Assert.Equal("Vesper Commander", session.Window.Window.Title);
    }

    [Fact]
    public void Title_CanBeChangedAfterCreation()
    {
        using var session = HeadlessSession.Create(title: "Vesper Commander");
        session.Window.Window.Title = "Vesper Commander — C:\\src";
        Assert.Equal("Vesper Commander — C:\\src", session.Window.Window.Title);
    }
}
