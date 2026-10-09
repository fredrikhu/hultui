using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Styling;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A checkbox, a radio button and a switch take their size from the theme, so switching to a
/// theme with bigger ones lays them out again. They copied the size in when the theme was
/// applied and marked nothing: the control drew its new box in the room the old one was given.
/// </summary>
public class ThemeSizeRelayoutTests
{
    private static (HeadlessSession Session, Element Control) Mounted(Element control)
    {
        var session = HeadlessSession.Create(300, 120);
        session.Application.SetRoot(Box.New().AlignItems(AlignItems.FlexStart).Padding(10f).Children(control));
        session.PumpFrames(2);
        return (session, control);
    }

    [Fact]
    public void ACheckboxTakesTheNewThemesSize()
    {
        var (session, box) = Mounted(Checkbox.New());
        using var _ = session;
        Assert.Equal(18f, box.Layout.Width);

        session.Application.Theme = Theme.Light.With(new CheckboxTheme { Size = 30f });
        session.PumpFrames(2);

        Assert.Equal(30f, box.Layout.Width);
        Assert.Equal(30f, box.Layout.Height);
    }

    [Fact]
    public void ARadioButtonTakesTheNewThemesSize()
    {
        var (session, radio) = Mounted(RadioButton.New("g"));
        using var _ = session;
        Assert.Equal(18f, radio.Layout.Width);

        session.Application.Theme = Theme.Light.With(new RadioButtonTheme { Size = 26f });
        session.PumpFrames(2);

        Assert.Equal(26f, radio.Layout.Width);
        Assert.Equal(26f, radio.Layout.Height);
    }

    [Fact]
    public void ASwitchTakesTheNewThemesTrack()
    {
        var (session, toggle) = Mounted(ToggleSwitch.New());
        using var _ = session;
        Assert.Equal(40f, toggle.Layout.Width);

        session.Application.Theme = Theme.Light.With(new ToggleSwitchTheme { TrackWidth = 60f, TrackHeight = 30f });
        session.PumpFrames(2);

        Assert.Equal(60f, toggle.Layout.Width);
        Assert.Equal(30f, toggle.Layout.Height);
    }

    [Fact]
    public void AThemeThatKeepsTheSizeLaysNothingOut()
    {
        // The other way round: re-applying a theme whose sizes are the same (any style pass does)
        // must not mark layout, or every theme switch and patch would lay the controls out again.
        var (session, box) = Mounted(Checkbox.New());
        using var _ = session;
        box.DirtyFlags = DirtyFlags.None;

        ((CheckboxElement)box).ApplyTheme(Theme.Light);

        Assert.Equal(DirtyFlags.None, box.DirtyFlags & DirtyFlags.Layout);
    }
}
