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
/// The controls that measured with theme sizes the other way round marked layout on every
/// theme applied, sizes changed or not; and an expander marked only itself, so its header kept
/// its old box.
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

    // The controls that measure with sizes of their own from the theme, or set their parts' sizes
    // from it. Each style pass applies the theme again, so a switch that keeps every size (light to
    // dark) must lay none of them out; one that changes a size must lay out what it sizes.
    public static TheoryData<string> ThemedSizes => new()
    {
        "number", "progress", "splitter", "calendar", "time", "expander", "toast",
    };

    private static Element Make(string kind) => kind switch
    {
        "number" => NumericUpDown.New(5),
        "progress" => ProgressBar.New(0.5),
        "splitter" => new SplitterHandleElement(SplitterAxis.Horizontal),
        "calendar" => MonthCalendar.New(new DateOnly(2026, 10, 11)),
        "time" => TimePicker.New(new TimeOnly(9, 30)),
        "expander" => Expander.New("Group", TextElement.New("inside")),
        _ => ToastElement.New("Saved", ToastSeverity.Success),
    };

    private static IEnumerable<Element> All(Element e) => e.Children.SelectMany(All).Prepend(e);

    [Theory]
    [MemberData(nameof(ThemedSizes))]
    public void ALightToDarkSwitch_LaysNothingOut(string kind)
    {
        var (session, control) = Mounted(Make(kind));
        using var _ = session;
        foreach (var e in All(control)) e.DirtyFlags = DirtyFlags.None;

        control.ApplyTheme(Theme.Dark);

        // Nor does a toast put its row together again, which told the accessibility tree its
        // children had changed.
        Assert.All(All(control), e =>
            Assert.Equal(DirtyFlags.None, e.DirtyFlags & (DirtyFlags.Layout | DirtyFlags.Children)));
    }

    [Theory]
    [MemberData(nameof(ThemedSizes))]
    public void AThemeThatChangesASize_LaysOutWhatItSizes(string kind)
    {
        var (session, control) = Mounted(Make(kind));
        using var _ = session;
        var (theme, size, grows) = kind switch
        {
            "number" => (Theme.Light.With(new NumericUpDownTheme { ButtonWidth = 40f }), (Func<float>)(() => control.Layout.Width), 18f),
            "progress" => (Theme.Light.With(new ProgressBarTheme { Height = 10f }), () => control.Layout.Height, 6f),
            "splitter" => (Theme.Light.With(new SplitterTheme { Thickness = 12f }), () => control.Layout.Width, 6f),
            "calendar" => (Theme.Light.With(new CalendarTheme { CellWidth = 40f }), () => control.Layout.Width, 42f),
            "time" => (Theme.Light.With(new TimeFieldTheme { PaddingX = 20f }), () => control.Layout.Width, 28f),
            // The header's own box: the expander was marked, its header was not, and kept its height.
            "expander" => (Theme.Light.With(new ExpanderTheme { HeaderMinHeight = 48f }), () => control.Children[0].Layout.Height, 16f),
            // The bar's width, which the row reads from the bar's style as it lays it out.
            _ => (Theme.Light.With(new ToastTheme { AccentWidth = 8f }), () => control.Children[0].Layout.Width, 5f),
        };
        float before = size();

        session.Application.Theme = theme;
        session.PumpFrames(2);

        Assert.Equal(before + grows, size(), 3);
    }
}
