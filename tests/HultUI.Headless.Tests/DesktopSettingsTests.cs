using HultUI.Core.Elements;
using HultUI.Core.Platform;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// The desktop's light or dark preference: a headless session starts with the one its options
/// give, and <see cref="HeadlessSession.SetColorScheme"/> changes it the way the desktop does,
/// told from the next pump through <c>Application.DesktopSettingsChanged</c>.
/// </summary>
public sealed class DesktopSettingsTests
{
    [Fact]
    public void ASession_StartsWithNoPreference()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);

        Assert.Equal(ColorScheme.NoPreference, session.Application.DesktopColorScheme);
    }

    [Fact]
    public void ASession_StartsWithTheSchemeItsOptionsGive()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300,
            options: new HeadlessOptions { ColorScheme = ColorScheme.Dark });

        Assert.Equal(ColorScheme.Dark, session.Application.DesktopColorScheme);
    }

    [Fact]
    public void AChange_IsToldOnTheNextPump_AlreadyRead()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        int uiThread = Environment.CurrentManagedThreadId;
        var told = new List<(ColorScheme Scheme, int Thread)>();
        session.Application.DesktopSettingsChanged +=
            () => told.Add((session.Application.DesktopColorScheme, Environment.CurrentManagedThreadId));

        session.SetColorScheme(ColorScheme.Dark);
        Assert.Empty(told);   // told from the event pump, not from inside SetColorScheme
        session.PumpFrame();

        Assert.Equal([(ColorScheme.Dark, uiThread)], told);
    }

    [Fact]
    public void TheSameScheme_TellsNothing()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300,
            options: new HeadlessOptions { ColorScheme = ColorScheme.Light });
        int changes = 0;
        session.Application.DesktopSettingsChanged += () => changes++;

        session.SetColorScheme(ColorScheme.Light);
        session.PumpFrame();

        Assert.Equal(0, changes);
    }

    [Fact]
    public void FollowingTheDesktop_RestylesLive()
    {
        using var session = HeadlessSession.Create(width: 400, height: 300);
        var app = session.Application;
        void FollowDesktop() => app.Theme = app.DesktopColorScheme == ColorScheme.Dark ? Theme.Dark : Theme.Light;
        FollowDesktop();
        app.DesktopSettingsChanged += FollowDesktop;
        var text = TextElement.New("Files");
        session.Window.SetRoot(text);
        session.PumpFrame();
        Assert.Equal(Theme.Light.TextColor, text.ResolvedInherited.TextColor);

        session.SetColorScheme(ColorScheme.Dark);
        session.PumpFrame();

        Assert.Equal(Theme.Dark.TextColor, text.ResolvedInherited.TextColor);
    }
}
