using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

namespace HultUI.Samples.Fluent;

// Inside the namespace on purpose. This assembly sits under HultUI, so an unqualified `Text`
// here binds to the HultUI.Text namespace (the shaping engine) rather than to the label
// builder — and an alias declared above the namespace loses to that. Declared in the
// namespace body, it wins.
using Text = HultUI.Core.Builders.Text;

public enum Page { Home, Personalization, Gallery, About }

/// <summary>
/// A Windows 11 Settings-shaped app: a custom title bar, a NavigationView down the left, and a
/// content frame with its top-left corner rounded off.
///
/// <para>The whole point of the sample is that none of it is special-cased in the framework.
/// The window is borderless and the caption is HultUI elements; the nav pane is a column of
/// boxes; the content frame is one box with a single rounded corner. What makes it read as
/// WinUI rather than as a dark-grey app is the arithmetic — 32px controls, 4px radii, 68px
/// setting rows, a 3x16 selection pill — and the fact that the framework's own widgets are
/// pointed at the same palette instead of being left in their defaults.</para>
/// </summary>
public sealed partial class FluentApp : Component
{
    private readonly Application _app;
    private readonly Window _window;

    [Reactive] public partial Page Current { get; set; }
    [Reactive] public partial bool Dark { get; set; }
    [Reactive] public partial int AccentIndex { get; set; }
    [Reactive] public partial bool SearchFocused { get; set; }

    // Personalization
    [Reactive] public partial bool Transparency { get; set; }
    [Reactive] public partial bool Animations { get; set; }
    [Reactive] public partial double TextScale { get; set; }
    [Reactive] public partial bool ShowAccentOnSurfaces { get; set; }

    // Gallery
    [Reactive] public partial bool ToggleA { get; set; }
    [Reactive] public partial bool ToggleB { get; set; }
    [Reactive] public partial bool CheckA { get; set; }
    [Reactive] public partial bool CheckB { get; set; }
    [Reactive] public partial int RadioChoice { get; set; }
    [Reactive] public partial double Volume { get; set; }
    [Reactive] public partial int ComboChoice { get; set; }
    [Reactive] public partial bool ExpanderOpen { get; set; }
    [Reactive] public partial bool InfoBarShown { get; set; }
    [Reactive] public partial int ClickCount { get; set; }

    public FluentApp(Application app, Window window)
    {
        _app = app;
        _window = window;

        Current = Page.Home;
        Dark = true;
        AccentIndex = 0;
        Transparency = true;
        Animations = true;
        TextScale = 125;
        ToggleA = true;
        Volume = 62;
        ComboChoice = 1;
        InfoBarShown = true;

        // Set here as well as in Program.cs, so anything that hosts this component directly —
        // a test, the screenshot driver — gets the window the user sees rather than a frame
        // with the framework's stock light widgets scattered through a dark page. Junction and
        // Meridian do the same for the same reason.
        ApplyTheme();
    }

    public FluentPalette Palette => FluentPalette.For(Dark, AccentPreset.All[AccentIndex]);

    public void SetDark(bool dark) { Dark = dark; ApplyTheme(); }

    public void SetAccent(int index) { AccentIndex = index; ApplyTheme(); }

    private void ApplyTheme() => _app.Theme = Palette.ToHultTheme();

    public override Element Build()
    {
        var p = Palette;

        return Flex.Column(gap: 0)
            .Background(p.Backdrop)
            .Children(
                TitleBar(p),
                Flex.Row(gap: 0).FlexGrow(1).Children(
                    NavPane(p),
                    ContentFrame(p)));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Title bar
    // ──────────────────────────────────────────────────────────────────────

    private Element TitleBar(FluentPalette p)
    {
        var caption = Flex.Row(gap: 0).FlexShrink(0).Children(
            CaptionButton(p, Icons.Minimize, "Minimize", () => _window.Minimize()),
            CaptionButton(p, _window.IsMaximized ? Icons.Restore : Icons.Maximize,
                _window.IsMaximized ? "Restore" : "Maximize", () => _window.ToggleMaximize(), isMaximize: true),
            CaptionButton(p, Icons.Close, "Close", () => _window.Close(), isClose: true));

        return Flex.Row(gap: 0).FlexShrink(0)
            .Height(Fluent.TitleBarHeight)
            .AlignItems(AlignItems.Center)
            .WindowDrag()
            .Children(
                Box.New().Width(4f).FlexShrink(0),
                F.SubtleIconButton(p, Icons.Back, "Back", () => Current = Page.Home, 40f, 16f, p.TextPrimary),
                Box.New().Width(8f).FlexShrink(0),
                IconRenderer.Draw(Icons.Personalize, 16f, p.Accent),
                Box.New().Width(12f).FlexShrink(0),
                Text.New("Fluent Sample").FontSize(Fluent.CaptionSize).TextColor(p.TextPrimary),
                Box.New().FlexGrow(1),
                caption);
    }

    /// <summary>
    /// A caption button: 46x48 and square-cornered, because that is the Windows caption band
    /// and anything rounded there looks like a web page pretending to be a window. Close is
    /// the one control in the system that gets a red hover fill and white glyph on top of it.
    /// </summary>
    private Element CaptionButton(
        FluentPalette p, Icon icon, string name, Action onClick, bool isMaximize = false, bool isClose = false)
    {
        var hover = isClose ? Color.FromHex("#C42B1C") : p.SubtleHover;
        var glyph = IconRenderer.Draw(icon, 10f, p.TextPrimary);

        var btn = Box.New().FlexShrink(0)
            .Width(46f).Height(Fluent.TitleBarHeight)
            .Background(Color.Transparent)
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .OnHover(b => b.Background(hover))
            .OnClick(_ => onClick())
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(name)
            .Children(glyph);

        // Tells the Windows frame this is HTMAXBUTTON, which is what makes the Snap Layouts
        // flyout appear on hover. Nothing happens off Windows.
        if (isMaximize) btn.WindowMaximizeButton();
        return btn;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Navigation pane
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The pane's search field, magnifier and all.
    ///
    /// <para>Composed rather than themed: the box, the glyph and the spacing are this sample's,
    /// and the framework's input sits inside it with <c>Chromeless()</c> so it draws no box of
    /// its own. Focus has to be carried in app state because the ring belongs to the wrapper and
    /// an element only gets focus styles for its own focus — two rebuilds per interaction, which
    /// is why the text is left on the element rather than mirrored into a signal that would
    /// rebuild the pane on every keystroke.</para>
    /// </summary>
    private Element SearchBox(FluentPalette p)
    {
        var input = TextInput.New("")
            .Chromeless()
            .PlaceholderText("Find a setting")
            .Padding(Thickness.Zero)
            .FlexGrow(1)
            .AccessibleName("Find a setting");
        input.On<FocusEvent>(_ => SearchFocused = true);
        input.On<BlurEvent>(_ => SearchFocused = false);

        // Two boxes for one border, the same trick the buttons use — a box takes a single border
        // colour, and a text field's bottom edge is a different colour from its sides. Here it is
        // not a subtle two-tone either: at rest the bottom is ControlStrong against #4C4C4C
        // sides, and on focus it grows to a 2px accent while the sides stay put.
        var inner = Flex.Row(gap: 8f).FlexGrow(1)
            .AlignItems(AlignItems.Center)
            .Padding(new Thickness(0, 10, 0, 10))
            .CornerRadius(Fluent.ControlRadius)
            .Border(new Thickness(1, 1, 0, 1), p.ControlStroke)
            .Children(
                IconRenderer.Draw(Icons.Search, 16f, p.TextSecondary),
                input);

        return Box.New().FlexShrink(0)
            .Height(Fluent.ControlHeight)
            .CornerRadius(Fluent.ControlRadius)
            .Background(SearchFocused ? p.ControlInputActive : p.ControlFill)
            .Border(new Thickness(0, 0, SearchFocused ? 2f : 1f, 0),
                SearchFocused ? p.Accent : p.ControlStrong)
            .Margin(new Thickness(0, 0, 8, 0))
            .Children(inner);
    }

    private Element NavPane(FluentPalette p)
    {
        var search = SearchBox(p);

        return Flex.Column(gap: 4)
            .Width(Fluent.NavPaneWidth).FlexShrink(0)
            .Padding(new Thickness(0, 12, 12, 12))
            .Children(
                search,
                F.NavItem(p, Icons.Home, "Home", Current == Page.Home, () => Current = Page.Home),
                F.NavItem(p, Icons.Personalize, "Personalization", Current == Page.Personalization,
                    () => Current = Page.Personalization),
                F.NavItem(p, Icons.Gallery, "Controls gallery", Current == Page.Gallery,
                    () => Current = Page.Gallery),
                Box.New().FlexGrow(1),
                F.NavItem(p, Icons.About, "About", Current == Page.About, () => Current = Page.About));
    }

    // ──────────────────────────────────────────────────────────────────────
    // Content frame
    // ──────────────────────────────────────────────────────────────────────

    private Element ContentFrame(FluentPalette p)
    {
        var page = Current switch
        {
            Page.Personalization => Pages.Personalization(this, p),
            Page.Gallery => Pages.Gallery(this, p),
            Page.About => Pages.About(this, p),
            _ => Pages.Home(this, p),
        };

        // The Windows 11 bar: a 2px hairline with no track while you read, widening to a 12px
        // bar with a track when the pointer comes near. The hairline is only usable because the
        // region that counts as "on the bar" is the expanded width, not the drawn one.
        var body = ScrollView.New().FlexGrow(1)
            // Right padding rather than a framework knob: the bar is drawn at the inner edge of
            // the content box, so padding insets it. WinUI leaves the same few pixels, and
            // without them the pill's right half is clipped off by the window edge.
            .Padding(new Thickness(0, 3, 0, 0));
        body.ScrollbarWidth = 2f;                       // the resting indicator
        body.ScrollbarExpandedWidth = 12f;              // and what it grows to under the pointer
        body.ScrollbarColor = Color.Transparent;        // no track until then
        body.ScrollbarTrackHoverColor = p.ControlFill;
        body.ScrollbarThumbColor = p.ControlStrong;
        body.Children(
            Flex.Column(gap: 0).FlexShrink(0)
                // Everything on the 4px grid the Spacing page insists on — 28 was not.
                .Padding(new Thickness(32, 36, 36, 36))
                .MaxWidth(1064f)
                .Children(page));

        // One rounded corner and two borders. This is the NavigationView content frame, and
        // it is doing a surprising amount of the work: it is what separates "a window with a
        // sidebar" from "a WinUI app".
        return Box.New().FlexGrow(1)
            .Background(p.Layer)
            .CornerRadius(new CornerRadius(Fluent.OverlayRadius, 0, 0, 0))
            .Border(new Thickness(1, 0, 0, 1), p.CardStroke)
            .Overflow(Overflow.Hidden)
            .Children(body);
    }
}
