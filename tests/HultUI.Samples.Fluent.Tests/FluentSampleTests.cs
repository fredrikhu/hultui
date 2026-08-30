using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Headless;
using Xunit;

namespace HultUI.Samples.Fluent.Tests;

/// <summary>
/// The sample is a look, so the tests are about the look: the details that make a window read
/// as WinUI rather than as a dark-grey app, and that a refactor would quietly lose. None of
/// them assert pixels — every one names the design decision it is holding in place.
/// </summary>
public class FluentSampleTests
{
    private static (HeadlessSession Session, FluentApp App, Element Root) Start()
    {
        var session = HeadlessSession.Create(1280, 860);
        var app = new FluentApp(session.Application, session.Window.Window);
        Element root = app.Mount();
        session.Application.SetRoot(root);
        session.PumpFrames(3);
        return (session, app, root);
    }

    private static void GoTo(HeadlessSession session, string page)
    {
        session.Window.FindByRole(AccessibilityRole.ListItem, page)!.Click();
        session.PumpFrames(3);
    }

    // ──────────────────────────────────────────────────────────────────────
    // The palette reaches the framework's own widgets
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Constructing the app themes the Application, not only <c>Program.cs</c>. The framework
    /// draws the text boxes, checkboxes and sliders on these pages, and it reads
    /// <c>Application.Theme</c> rather than the palette this sample paints with — so without
    /// this every one of them comes up in the stock light theme in the middle of a dark window.
    /// </summary>
    [Fact]
    public void ConstructingTheAppThemesTheApplication()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        Assert.Equal(app.Palette.Layer, session.Application.Theme.Background);
        Assert.Equal(app.Palette.Accent, session.Application.Theme.Primary);
        Assert.Equal(app.Palette.TextPrimary, session.Application.Theme.TextColor);
    }

    /// <summary>And the consequence, on a widget the sample did not draw: the nav pane's
    /// search box is a framework <c>TextInputElement</c>, and it must be wearing the Fluent
    /// control fill rather than the framework's own.</summary>
    [Fact]
    public void AFrameworkWidgetTakesTheFluentColours()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        var input = FirstOfType<TextInputElement>(_root);

        Assert.NotNull(input);
        Assert.Equal(app.Palette.ControlFill, input!.BackgroundColor);
        Assert.Equal(app.Palette.ControlStroke, input.BorderColor);
    }

    // ──────────────────────────────────────────────────────────────────────
    // NavigationView
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void ANavItemSwitchesThePage()
    {
        var (session, _app, _root) = Start();
        using var s = session;

        Assert.NotNull(session.Window.FindByText("HULT-WORKSTATION"));

        GoTo(session, "Personalization");

        Assert.Null(session.Window.FindByText("HULT-WORKSTATION"));
        Assert.NotNull(session.Window.FindByText("Choose your mode"));
    }

    /// <summary>
    /// The selection indicator. WinUI marks the selected nav item with a 3x16 accent pill on
    /// its left edge and only a very faint fill behind it — a nav list that leans on the fill
    /// instead reads as some other toolkit however right the colours are. The pill is the
    /// first child of the item, and it is the assertion worth having.
    /// </summary>
    [Fact]
    public void OnlyTheSelectedNavItemHasAnAccentPill()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        GoTo(session, "Personalization");

        Assert.Equal(app.Palette.Accent, Pill(session, "Personalization").BackgroundColor);
        Assert.Equal(Color.Transparent, Pill(session, "Home").BackgroundColor);
        Assert.Equal(Color.Transparent, Pill(session, "About").BackgroundColor);
    }

    private static BoxElement Pill(HeadlessSession session, string item) =>
        (BoxElement)session.Window.FindByRole(AccessibilityRole.ListItem, item)!.Element.Children[0];

    // ──────────────────────────────────────────────────────────────────────
    // Controls
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The toggle switch flips, and its knob travels. The knob is moved by a transform rather
    /// than by relaying the row out, because a change of justification would snap where a
    /// transform can be transitioned — so the travel is worth asserting, not just the state.
    /// </summary>
    [Fact]
    public void TheToggleSwitchFlipsAndItsKnobTravels()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        GoTo(session, "Personalization");
        Assert.True(app.Transparency);
        Assert.Equal(20f, KnobOffset(session));

        session.Window.FindByRole(AccessibilityRole.Switch, "Transparency effects")!.Click();
        session.PumpFrames(40);   // past the 150ms transition, so the transform has settled

        Assert.False(app.Transparency);
        Assert.Equal(0f, KnobOffset(session));
    }

    private static float KnobOffset(HeadlessSession session)
    {
        var track = session.Window.FindByRole(AccessibilityRole.Switch, "Transparency effects")!.Element;
        return track.Children[0].Transform?.TranslateX ?? 0f;
    }

    /// <summary>
    /// Black text on a pale blue accent. In dark mode WinUI inverts the text on accent
    /// surfaces, and it is one of the most recognisable things about Windows 11 — an accent
    /// button with white text on it is the single fastest way to look like something else.
    /// </summary>
    [Fact]
    public void TheAccentButtonInvertsItsTextInDarkMode()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        GoTo(session, "Controls gallery");
        Assert.True(app.Dark);
        Assert.Equal(Color.Black, AccentButtonTextColor(session));

        app.SetDark(false);
        session.PumpFrames(3);

        Assert.Equal(Color.White, AccentButtonTextColor(session));
    }

    private static Color? AccentButtonTextColor(HeadlessSession session)
    {
        var button = session.Window.FindByRole(AccessibilityRole.Button, "Accent button")!.Element;
        return FirstOfType<TextElement>(button)!.Inherited.TextColor;
    }

    /// <summary>
    /// The two-tone control border. WinUI paints a control's 1px border as a vertical gradient
    /// that is darker along the bottom, and that one darker line is what makes a Fluent button
    /// look like it is resting on the page. A box takes a single border colour, so the button
    /// is two nested boxes — outer draws the bottom edge, inner draws the other three. Losing
    /// either half is invisible in a glance at a screenshot and obvious side by side with the
    /// real thing.
    /// </summary>
    [Fact]
    public void AStandardButtonKeepsItsTwoToneBorder()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        GoTo(session, "Controls gallery");

        var outer = (BoxElement)session.Window.FindByRole(AccessibilityRole.Button, "Standard button")!.Element;
        var inner = (BoxElement)outer.Children[0];

        Assert.Equal(new Thickness(0, 0, 1, 0), outer.Box.BorderWidth);
        Assert.Equal(app.Palette.ControlStrokeBottom, outer.BorderColor);

        Assert.Equal(new Thickness(1, 1, 0, 1), inner.Box.BorderWidth);
        Assert.Equal(app.Palette.ControlStroke, inner.BorderColor);

        // 1px in total either way — the two-tone must not have become a 2px border.
        Assert.Equal(1f, outer.Box.BorderWidth.Bottom + inner.Box.BorderWidth.Bottom);
    }

    /// <summary>
    /// The expander opens, and its header gives up its bottom corners when it does. That is
    /// what makes the header and the panel below it read as one object rather than as two
    /// stacked cards — a header that keeps all four corners open looks like a bug.
    /// </summary>
    [Fact]
    public void TheExpanderJoinsItsPanelWhenOpen()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        GoTo(session, "Controls gallery");

        // The expander is the last thing on a long page. Scroll to it rather than growing the
        // window: a click aimed below the viewport hits nothing and reports nothing, so a test
        // that skipped this would pass with the header never having been pressed.
        session.Window.Wheel(800, 600, 0, -20);
        session.PumpFrames(6);

        var closed = (BoxElement)session.Window.FindByRole(AccessibilityRole.Button, "Notifications")!.Element;
        Assert.Equal(new CornerRadius(4f), closed.CornerRadius);
        Assert.Null(session.Window.FindByRole(AccessibilityRole.Switch, "Mail"));

        session.Window.FindByRole(AccessibilityRole.Button, "Notifications")!.Click();
        session.PumpFrames(3);

        var open = (BoxElement)session.Window.FindByRole(AccessibilityRole.Button, "Notifications")!.Element;
        Assert.True(app.ExpanderOpen);
        Assert.Equal(new CornerRadius(4f, 4f, 0f, 0f), open.CornerRadius);
        Assert.NotNull(session.Window.FindByRole(AccessibilityRole.Switch, "Mail"));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Theming
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Picking an accent restyles the framework's widgets as well as the sample's own. The two
    /// are separate mechanisms — one is a palette this code paints from, the other is a
    /// <c>Theme</c> the framework reads — and a swatch that moves only one of them leaves a
    /// blue checkbox in a purple window.
    /// </summary>
    [Fact]
    public void AnAccentSwatchMovesBothPalettes()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        GoTo(session, "Personalization");
        session.Window.FindByRole(AccessibilityRole.Button, "Purple")!.Click();
        session.PumpFrames(3);

        Assert.Equal(AccentPreset.Purple.Dark, app.Palette.Accent);
        Assert.Equal(AccentPreset.Purple.Dark, session.Application.Theme.Primary);
        Assert.Equal(AccentPreset.Purple.Dark, session.Application.Theme.Checkbox.CheckedBackground);
        Assert.Equal(AccentPreset.Purple.Dark, session.Application.Theme.Slider.FillColor);
    }

    [Fact]
    public void SwitchingModeRepaintsEverything()
    {
        var (session, app, _root) = Start();
        using var _ = session;

        var dark = app.Palette;
        app.SetDark(false);
        session.PumpFrames(3);
        var light = app.Palette;

        Assert.NotEqual(dark.TextPrimary, light.TextPrimary);
        Assert.Equal(light.TextPrimary, session.Application.Theme.TextColor);
        Assert.Equal(light.Layer, session.Application.Theme.Background);

        // Both accents are the same colour by name and a different colour on screen — that is
        // the whole reason AccentPreset carries two values rather than deriving one.
        Assert.Equal(AccentPreset.Blue.Dark, dark.Accent);
        Assert.Equal(AccentPreset.Blue.Light, light.Accent);
    }

    // ──────────────────────────────────────────────────────────────────────

    private static T? FirstOfType<T>(Element root) where T : Element
    {
        if (root is T match) return match;
        foreach (var child in root.Children)
            if (FirstOfType<T>(child) is { } found)
                return found;
        return null;
    }
}
