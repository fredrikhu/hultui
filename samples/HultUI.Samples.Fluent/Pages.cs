using HultUI.Core.Accessibility;
using HultUI.Icons;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

namespace HultUI.Samples.Fluent;

// Inside the namespace on purpose. This assembly sits under HultUI, so an unqualified `Text`
// here binds to the HultUI.Text namespace (the shaping engine) rather than to the label
// builder — and an alias declared above the namespace loses to that. Declared in the
// namespace body, it wins.
using Text = HultUI.Core.Builders.Text;

/// <summary>
/// The four pages. They are ordinary functions returning elements — there is no page type, no
/// registration and no frame: <see cref="FluentApp"/> switches on an enum and builds one.
/// </summary>
public static class Pages
{
    // ──────────────────────────────────────────────────────────────────────
    // Home
    // ──────────────────────────────────────────────────────────────────────

    public static Element Home(FluentApp app, FluentPalette p) =>
        Column(
            F.Title(p, "Home").Margin(new Thickness(0, 0, 24, 0)),

            HeroCard(p),

            Box.New().Height(24f).FlexShrink(0),

            // Two columns of tiles, the way the Windows 11 home page lays out. FlexBasis 0 on
            // both is what makes them share the width evenly regardless of what is in them —
            // with auto basis the taller column's content would win and the split would drift.
            Flex.Row(gap: 4f).FlexShrink(0).AlignItems(AlignItems.FlexStart).Children(
                Flex.Column(gap: 4f).FlexGrow(1).FlexBasis(0f).Children(
                    Tile(p, Icons.Personalize, "Personalization", "Background, colors, themes",
                        TileRow(p, Icons.Color, "Accent color", AccentPreset.All[app.AccentIndex].Name),
                        TileRow(p, Icons.Layers, "Transparency", app.Transparency ? "On" : "Off")),
                    Tile(p, Icons.Network, "Network and internet", "Wi-Fi, VPN, hotspot",
                        TileRow(p, Icons.Network, "Wi-Fi", "Connected, secured"),
                        TileRow(p, Icons.Bluetooth, "Bluetooth", "3 devices paired"))),
                Flex.Column(gap: 4f).FlexGrow(1).FlexBasis(0f).Children(
                    Tile(p, Icons.Storage, "System", "Display, sound, storage",
                        TileRow(p, Icons.Storage, "Local disk (C:)", "312 GB of 512 GB free"),
                        TileRow(p, Icons.Brightness, "Display", "2560 x 1440, 120 Hz")),
                    Tile(p, Icons.Update, "Windows Update", "Last checked: today",
                        TileRow(p, Icons.Success, "You are up to date", "Checked 12 minutes ago")))));

    /// <summary>
    /// The device card at the top of Settings. WinUI fills this with a Mica-and-accent wash
    /// behind the device name; a linear gradient in the accent is the honest approximation.
    /// </summary>
    private static Element HeroCard(FluentPalette p)
    {
        var wash = Box.New().FlexShrink(0)
            .Width(96f).Height(96f)
            .CornerRadius(Fluent.OverlayRadius)
            .Background(Gradient.Linear(135f, p.Accent, Mix(p.Accent, p.Card, 0.55f)))
            .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
            .Children(IconRenderer.Draw(Icons.Storage, 40f, p.TextOnAccent));

        return F.Card(p)
            .Padding(new Thickness(20, 20, 20, 20))
            .FlexDirection(FlexDirection.Row)
            .Gap(20f)
            .AlignItems(AlignItems.Center)
            .Children(
                wash,
                Flex.Column(gap: 4f).FlexGrow(1).Children(
                    F.Subtitle(p, "HULT-WORKSTATION"),
                    F.Body(p, "HultUI Fluent sample").TextColor(p.TextSecondary),
                    F.Caption(p, "Rendered by HultUI on Skia — no WinUI, no XAML, no WinRT")),
                F.Button(p, "Rename", () => { }));
    }

    private static Element Tile(FluentPalette p, Icon icon, string title, string subtitle, params Element[] rows)
    {
        var head = Flex.Row(gap: 16f).AlignItems(AlignItems.Center).Children(
            IconRenderer.Draw(icon, 20f, p.Accent),
            Flex.Column(gap: 2f).FlexGrow(1).Children(
                F.BodyStrong(p, title),
                F.Secondary(p, subtitle)));

        var children = new List<Element> { head };
        children.AddRange(rows);

        return F.Card(p)
            .Padding(new Thickness(16, 16, 16, 16))
            .Gap(4f)
            .Children([.. children]);
    }

    private static Element TileRow(FluentPalette p, Icon icon, string title, string detail) =>
        Box.New().FlexShrink(0)
            .Height(48f)
            .CornerRadius(Fluent.ControlRadius)
            .Padding(new Thickness(0, 8, 0, 8))
            .FlexDirection(FlexDirection.Row)
            .Gap(12f)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .OnHover(b => b.Background(p.SubtleHover))
            .Children(
                IconRenderer.Draw(icon, 16f, p.TextSecondary),
                Flex.Column(gap: 0f).FlexGrow(1).Children(
                    F.Body(p, title).MaxLines(1).TextOverflow(TextOverflow.Ellipsis),
                    F.Caption(p, detail).MaxLines(1).TextOverflow(TextOverflow.Ellipsis)),
                IconRenderer.Draw(Icons.ChevronRight, 12f, p.TextTertiary));

    // ──────────────────────────────────────────────────────────────────────
    // Personalization
    // ──────────────────────────────────────────────────────────────────────

    public static Element Personalization(FluentApp app, FluentPalette p)
    {
        var mode = Dropdown.New(["Light", "Dark"], app.Dark ? 1 : 0)
            .Changed(i => app.SetDark(i == 1));

        var textScale = Slider.New(app.TextScale, 100, 225).Step(25)
            .OnValueChanged(v => app.TextScale = v)
            .Width(220f);
        textScale.ThumbRadius = 10f;

        return Column(
            F.Title(p, "Personalization").Margin(new Thickness(0, 0, 24, 0)),

            F.SettingsCard(p, Icons.Brightness, "Choose your mode",
                "Changes the app palette, and with it every framework widget on the page", mode),

            F.SettingsCard(p, Icons.Layers, "Transparency effects",
                "Windows and surfaces appear translucent",
                F.ToggleSwitch(p, app.Transparency, v => app.Transparency = v, "Transparency effects")),

            F.SettingsCard(p, Icons.Startup, "Animation effects",
                "Controls animate on hover, press and selection",
                F.ToggleSwitch(p, app.Animations, v => app.Animations = v, "Animation effects")),

            AccentCard(app, p),

            F.SettingsCard(p, Icons.Font, "Text size", null,
                Flex.Row(gap: 12f).AlignItems(AlignItems.Center).Children(
                    textScale,
                    F.Body(p, $"{app.TextScale:0}%").TextColor(p.TextSecondary))),

            Box.New().Height(24f).FlexShrink(0),
            F.SectionHeader(p, "Related settings"),
            LinkCard(p, Icons.Accessibility, "Accessibility", "Contrast themes, narrator, magnifier"),
            LinkCard(p, Icons.Keyboard, "Keyboard", "Repeat delay, shortcuts, input language"));
    }

    /// <summary>The accent picker. Every swatch restyles the framework widgets on the page as
    /// well as the sample's own — the dropdown, the slider and the checkboxes all follow,
    /// because they read the same <c>Theme</c> the palette produces.</summary>
    private static Element AccentCard(FluentApp app, FluentPalette p)
    {
        var swatches = new List<Element>();
        for (int i = 0; i < AccentPreset.All.Length; i++)
        {
            int index = i;
            var preset = AccentPreset.All[i];
            bool selected = app.AccentIndex == i;
            var color = p.IsDark ? preset.Dark : preset.Light;

            // The selected swatch gets a ring around it: the accent again, held off the square
            // by a gap of the card behind it. As an outline rather than a thicker border,
            // because a border is drawn inside the box — the ring used to be a 3px border in
            // the *card* colour, which against a card background is invisible, so the only
            // thing selecting a swatch did was shrink its colour from 38px to 34px. Measured.
            // An outline also keeps both states the same size, and the row's 8px gap has room
            // for the 4px it reaches, since only one swatch is ever selected.
            swatches.Add(F.Focusable(Box.New().FlexShrink(0)
                .Width(40f).Height(40f)
                .CornerRadius(Fluent.ControlRadius)
                .Background(color)
                .Border(new Thickness(1), p.CardStroke)
                .Outline(selected ? 2f : 0f, color, offset: 2f)
                .Cursor(CursorType.Pointer)
                .Opacity(selected ? 1f : 0.9f)
                .OnHover(b => b.Opacity(1f))
                .OnClick(_ => app.SetAccent(index))
                .AccessibleRole(AccessibilityRole.Button)
                .AccessibleName(preset.Name)
                .Children(selected
                    ? IconRenderer.Draw(Icons.Success, 16f, p.TextOnAccent)
                    : Box.New()), () => app.SetAccent(index)));
        }

        return F.Card(p)
            .Padding(new Thickness(16, 16, 16, 16))
            .Gap(16f)
            .Children(
                Flex.Row(gap: 16f).AlignItems(AlignItems.Center).Children(
                    IconRenderer.Draw(Icons.Color, 20f, p.TextPrimary),
                    Flex.Column(gap: 2f).FlexGrow(1).Children(
                        F.Body(p, "Accent color"),
                        F.Secondary(p, AccentPreset.All[app.AccentIndex].Name))),
                Flex.Row(gap: 8f).FlexWrap(FlexWrap.Wrap).Children([.. swatches]));
    }

    private static Element LinkCard(FluentPalette p, Icon icon, string title, string description) =>
        F.Focusable(F.Card(p)
            .MinHeight(60f)
            .Padding(new Thickness(10, 16, 10, 16))
            .FlexDirection(FlexDirection.Row)
            .Gap(16f)
            .AlignItems(AlignItems.Center)
            .Cursor(CursorType.Pointer)
            .OnHover(b => b.Background(p.CardHover))
            .AccessibleRole(AccessibilityRole.Button)
            .AccessibleName(title)
            .Children(
                IconRenderer.Draw(icon, 20f, p.TextPrimary),
                Flex.Column(gap: 2f).FlexGrow(1).Children(
                    F.Body(p, title),
                    F.Secondary(p, description)),
                IconRenderer.Draw(Icons.ChevronRight, 12f, p.TextSecondary)), () => { });

    // ──────────────────────────────────────────────────────────────────────
    // Controls gallery
    // ──────────────────────────────────────────────────────────────────────

    public static Element Gallery(FluentApp app, FluentPalette p)
    {
        var input = TextInput.New("")
            .PlaceholderText("Type something")
            .Width(240f);

        var combo = Dropdown.New(["Never", "When plugged in", "Always"], app.ComboChoice)
            .Changed(i => app.ComboChoice = i);

        var volume = Slider.New(app.Volume, 0, 100)
            .OnValueChanged(v => app.Volume = v)
            .Width(260f);
        volume.ThumbRadius = 10f;

        var items = new List<Element>
        {
            F.Title(p, "Controls gallery").Margin(new Thickness(0, 0, 8, 0)),
            F.Body(p, "Everything below is either a HultUI widget pointed at the Fluent palette "
                    + "or a Fluent shape composed from boxes. Both kinds are on this page on purpose.")
                .TextColor(p.TextSecondary).Margin(new Thickness(0, 0, 24, 0)),
        };

        if (app.InfoBarShown)
        {
            items.Add(F.InfoBar(p, F.Severity.Informational, "Heads up.",
                "This InfoBar can be dismissed, and it will come back when you switch pages.",
                () => app.InfoBarShown = false));
            items.Add(Box.New().Height(8f).FlexShrink(0));
        }

        items.Add(F.InfoBar(p, F.Severity.Success, "Saved.", "Your changes were written to disk."));
        items.Add(F.InfoBar(p, F.Severity.Warning, "Low disk space.", "12 GB remaining on drive C."));
        items.Add(F.InfoBar(p, F.Severity.Error, "Could not connect.", "The host refused the connection."));

        items.Add(Section(p, "Buttons",
            Flex.Row(gap: 8f).FlexWrap(FlexWrap.Wrap).AlignItems(AlignItems.Center).Children(
                F.Button(p, "Accent button", () => app.ClickCount++, accent: true),
                F.Button(p, "Standard button", () => app.ClickCount++),
                F.Button(p, "With an icon", () => app.ClickCount++, icon: Icons.Update),
                F.SubtleIconButton(p, Icons.Search, "Search", () => app.ClickCount++),
                F.Body(p, $"Clicked {app.ClickCount} times").TextColor(p.TextSecondary))));

        items.Add(Section(p, "Toggles and selection",
            Flex.Row(gap: 40f).FlexWrap(FlexWrap.Wrap).Children(
                Flex.Column(gap: 12f).Children(
                    ToggleRow(p, "Wi-Fi", app.ToggleA, v => app.ToggleA = v),
                    ToggleRow(p, "Airplane mode", app.ToggleB, v => app.ToggleB = v)),
                Flex.Column(gap: 12f).Children(
                    Checkbox.New(app.CheckA, "Show hidden files").OnCheckedChanged(v => app.CheckA = v),
                    Checkbox.New(app.CheckB, "Use compact spacing").OnCheckedChanged(v => app.CheckB = v)),
                Flex.Column(gap: 12f).Children(
                    RadioButton.New("cadence", app.RadioChoice == 0, "Every hour")
                        .OnRadioChanged(_ => app.RadioChoice = 0),
                    RadioButton.New("cadence", app.RadioChoice == 1, "Every day")
                        .OnRadioChanged(_ => app.RadioChoice = 1),
                    RadioButton.New("cadence", app.RadioChoice == 2, "Never")
                        .OnRadioChanged(_ => app.RadioChoice = 2)))));

        items.Add(Section(p, "Text and lists",
            Flex.Row(gap: 12f).FlexWrap(FlexWrap.Wrap).AlignItems(AlignItems.Center).Children(
                input, combo)));

        items.Add(Section(p, "Ranges",
            Flex.Column(gap: 16f).Children(
                Flex.Row(gap: 16f).AlignItems(AlignItems.Center).Children(
                    volume,
                    F.Body(p, $"{app.Volume:0}").TextColor(p.TextSecondary)),
                Flex.Column(gap: 8f).Children(
                    F.Secondary(p, "Downloading update — 68%"),
                    F.ProgressBar(p, 0.68f)))));

        int notifying = (app.NotifyMail ? 1 : 0) + (app.NotifyCalendar ? 1 : 0) + (app.NotifyStore ? 1 : 0);
        items.Add(Box.New().Height(24f).FlexShrink(0));
        items.Add(F.SectionHeader(p, "Expander"));
        items.Add(F.Expander(p, Icons.Notifications, "Notifications",
            app.ExpanderOpen
                ? "Showing per-app settings"
                : $"{notifying} {(notifying == 1 ? "app" : "apps")} allowed to notify you",
            app.ExpanderOpen, v => app.ExpanderOpen = v,
            ToggleRow(p, "Mail", app.NotifyMail, v => app.NotifyMail = v),
            ToggleRow(p, "Calendar", app.NotifyCalendar, v => app.NotifyCalendar = v),
            ToggleRow(p, "Store", app.NotifyStore, v => app.NotifyStore = v)));

        return Column([.. items]);
    }

    private static Element ToggleRow(FluentPalette p, string label, bool on, Action<bool> onChanged) =>
        Flex.Row(gap: 12f).FlexShrink(0).AlignItems(AlignItems.Center).Children(
            F.ToggleSwitch(p, on, onChanged, label),
            F.Body(p, label));

    /// <summary>A gallery section: a bold label, then a card holding the specimens.</summary>
    private static Element Section(FluentPalette p, string title, Element content) =>
        Flex.Column(gap: 8f).FlexShrink(0).Margin(new Thickness(24, 0, 0, 0)).Children(
            F.SectionHeader(p, title),
            F.Card(p).Padding(new Thickness(20, 20, 20, 20)).Children(content));

    // ──────────────────────────────────────────────────────────────────────
    // About
    // ──────────────────────────────────────────────────────────────────────

    public static Element About(FluentApp app, FluentPalette p) =>
        Column(
            F.Title(p, "About").Margin(new Thickness(0, 0, 24, 0)),

            F.Card(p).Padding(new Thickness(20, 20, 20, 20)).Gap(12f).Children(
                F.Subtitle(p, "What this sample is"),
                F.Body(p,
                    "A HultUI app dressed in the Windows 11 design language. There is no WinUI, "
                    + "no XAML and no WinRT anywhere in it — the window is borderless, the "
                    + "caption bar is HultUI elements, and every surface below it is a box with "
                    + "a colour and a corner radius.")
                    .TextColor(p.TextSecondary).LineHeight(20f),
                F.Body(p,
                    "The values are Microsoft's published ones where they exist. Look at "
                    + "Fluent.cs and you will find CardBackgroundFillColorDefault and "
                    + "ControlStrokeColorSecondary under those names, so the sample can be "
                    + "checked against the real colour table rather than taken on trust.")
                    .TextColor(p.TextSecondary).LineHeight(20f)),

            Box.New().Height(24f).FlexShrink(0),
            F.SectionHeader(p, "Specifications"),
            SpecRow(p, Icons.Gallery, "Framework", "HultUI, retained mode, Skia renderer"),
            SpecRow(p, Icons.Font, "Type", IconRenderer.UseSegoe
                ? "Segoe UI Variable Text and Display"
                : "Platform default — Segoe UI Variable is a Windows font"),
            SpecRow(p, Icons.Color, "Icons", IconRenderer.UseSegoe
                ? "Segoe Fluent Icons"
                : "Phosphor — Segoe Fluent Icons was not found on this machine"),
            SpecRow(p, Icons.Layers, "Backdrop", "A flat colour standing in for Mica"),
            SpecRow(p, Icons.Personalize, "Accent", AccentPreset.All[app.AccentIndex].Name),

            Box.New().Height(24f).FlexShrink(0),
            F.InfoBar(p, F.Severity.Informational, "One thing is faked.",
                "Mica samples the desktop wallpaper and blurs it. Nothing here can see the "
                + "wallpaper, so the backdrop is the flat colour Mica lands on — measured off "
                + "the WinUI Gallery, where the whole top and left is a uniform #202020."));

    private static Element SpecRow(FluentPalette p, Icon icon, string label, string value) =>
        F.Card(p)
            .MinHeight(52f)
            .Padding(new Thickness(8, 16, 8, 16))
            .FlexDirection(FlexDirection.Row)
            .Gap(16f)
            .AlignItems(AlignItems.Center)
            .Children(
                IconRenderer.Draw(icon, 16f, p.TextSecondary),
                F.Body(p, label).Width(140f),
                F.Body(p, value).TextColor(p.TextSecondary).FlexGrow(1));

    // ──────────────────────────────────────────────────────────────────────

    /// <summary>The page column. 4px between cards is the Windows 11 Settings rhythm — close
    /// enough that a run of cards reads as one list, far enough that each keeps its own edge.</summary>
    private static Element Column(params Element[] children) =>
        Flex.Column(gap: 4f).FlexShrink(0).Children(children);

    private static Color Mix(Color a, Color b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t),
        a.A);
}
