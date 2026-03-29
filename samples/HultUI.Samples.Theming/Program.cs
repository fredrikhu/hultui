using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

// ── Define custom themes ──

var themes = new Dictionary<string, Theme>
{
    ["Light"] = Theme.Light,
    ["Dark"] = Theme.Dark,
    ["Ocean"] = new Theme
    {
        FontFamily = "sans-serif",
        FontSize = 15f,
        TextColor = new Color(224, 242, 254),              // sky-100
        Background = new Color(12, 74, 110),               // sky-800
        Surface = new Color(7, 89, 133),                   // sky-700
        SurfaceVariant = new Color(14, 116, 144),          // cyan-700
        Primary = new Color(34, 211, 238),                 // cyan-400
        PrimaryText = new Color(8, 51, 68),                // cyan-950
        Secondary = new Color(125, 211, 252),              // sky-300
        SecondaryText = new Color(12, 74, 110),            // sky-800
        Border = new Color(14, 116, 144),                  // cyan-700
        Danger = new Color(251, 146, 60),                  // orange-400
        DangerText = new Color(67, 20, 7),                 // deep brown
        FocusRing = new Color(34, 211, 238, 180),          // cyan-400
        Button = new ButtonTheme
        {
            CornerRadius = new CornerRadius(20f),          // pill buttons
            Padding = new Thickness(10, 24, 10, 24),
            FontSize = 14f,
            FontWeight = FontWeight.Bold,
        },
        TextInput = new TextInputTheme
        {
            CornerRadius = new CornerRadius(12f),
            Padding = new Thickness(8, 14, 8, 14),
            BorderWidth = new Thickness(2f),
            Background = new Color(7, 89, 133),            // sky-700
            BorderColor = new Color(14, 116, 144),         // cyan-700
            FocusBorderColor = new Color(34, 211, 238),    // cyan-400
            PlaceholderColor = new Color(56, 189, 248),    // sky-400
        },
        Checkbox = new CheckboxTheme
        {
            Size = 22f,
            CornerRadius = new CornerRadius(6f),
            CheckedBackground = new Color(34, 211, 238),   // cyan-400
            CheckMarkColor = new Color(8, 51, 68),         // cyan-950
        },
        RadioButton = new RadioButtonTheme
        {
            Size = 22f,
            SelectedColor = new Color(34, 211, 238),       // cyan-400
            DotColor = new Color(8, 51, 68),               // cyan-950
        },
    },
    ["Forest"] = new Theme
    {
        FontFamily = "serif",
        FontSize = 16f,
        TextColor = new Color(236, 253, 245),              // emerald-50
        Background = new Color(20, 83, 45),                // green-800
        Surface = new Color(22, 101, 52),                  // green-700
        SurfaceVariant = new Color(21, 128, 61),           // green-600
        Primary = new Color(234, 179, 8),                  // yellow-500
        PrimaryText = new Color(26, 46, 5),                // dark olive
        Secondary = new Color(163, 230, 53),               // lime-400
        SecondaryText = new Color(20, 83, 45),             // green-800
        Border = new Color(21, 128, 61),                   // green-600
        Danger = new Color(239, 68, 68),                   // red-500
        DangerText = Color.White,
        FocusRing = new Color(234, 179, 8, 180),           // yellow-500
        Button = new ButtonTheme
        {
            CornerRadius = new CornerRadius(4f),           // subtle rounding
            Padding = new Thickness(10, 20, 10, 20),
            FontSize = 15f,
            FontWeight = FontWeight.Bold,
        },
        TextInput = new TextInputTheme
        {
            CornerRadius = new CornerRadius(4f),
            Padding = new Thickness(8, 12, 8, 12),
            BorderWidth = new Thickness(2f),
            Background = new Color(22, 101, 52),           // green-700
            BorderColor = new Color(21, 128, 61),          // green-600
            FocusBorderColor = new Color(234, 179, 8),     // yellow-500
            PlaceholderColor = new Color(74, 222, 128),    // green-400
        },
        Checkbox = new CheckboxTheme
        {
            Size = 20f,
            CornerRadius = new CornerRadius(3f),
            CheckedBackground = new Color(234, 179, 8),    // yellow-500
            CheckMarkColor = new Color(26, 46, 5),         // dark olive
        },
        RadioButton = new RadioButtonTheme
        {
            Size = 20f,
            SelectedColor = new Color(234, 179, 8),        // yellow-500
            DotColor = new Color(26, 46, 5),               // dark olive
        },
    },
};

var themeNames = themes.Keys.ToArray();

using var app = new Application("Custom Theming", 520, 520);
app.SetRoot(new ThemingDemo(app, themes, themeNames).Mount());
app.Run();

// ── Demo component ──

partial class ThemingDemo : Component
{
    private readonly Application _app;
    private readonly Dictionary<string, Theme> _themes;
    private readonly string[] _themeNames;

    [Reactive] public partial int ThemeIndex { get; set; }
    [Reactive] public partial bool CheckboxValue { get; set; }
    [Reactive] public partial string RadioValue { get; set; }

    public ThemingDemo(Application app, Dictionary<string, Theme> themes, string[] themeNames)
    {
        _app = app;
        _themes = themes;
        _themeNames = themeNames;
        RadioValue = "Option A";
    }

    public override Element Build()
    {
        var themeIndex = ThemeIndex;
        var currentName = _themeNames[themeIndex];
        var isChecked = CheckboxValue;
        var radioValue = RadioValue;

        // Apply the selected theme
        _app.Theme = _themes[currentName];

        return Flex.Column(gap: 0).Children(

            // ── Header ──
            Box.New().Padding(new Thickness(20, 24, 16, 24))
                .Background(_app.Theme.Primary)
                .TextColor(_app.Theme.PrimaryText)
                .Children(
                    Flex.Column(gap: 4).Children(
                        Text.New("Custom Theming").FontSize(22f).FontWeight(FontWeight.Bold),
                        Text.New($"Current: {currentName}").FontSize(13f)
                    )
                ),

            // ── Theme selector ──
            Flex.Row(gap: 8).Padding(16).FlexShrink(0).FlexWrap(FlexWrap.Wrap)
                .Children(BuildThemeButtons(themeIndex)),

            // ── Component showcase ──
            ScrollView.New().FlexGrow(1).Children(
                Flex.Column(gap: 20).Padding(new Thickness(0, 16, 0, 16)).Children(

                    // Buttons section
                    Section("Buttons",
                        Flex.Row(gap: 8).FlexWrap(FlexWrap.Wrap).Children(
                            Button.New("Primary"),
                            Button.New("Secondary", ButtonVariant.Secondary),
                            Button.New("Danger", ButtonVariant.Danger)
                        )
                    ),

                    // Text input section
                    Section("Text Input",
                        Flex.Column(gap: 8).Children(
                            TextInput.New("").PlaceholderText("Type something...").Height(40),
                            TextInput.New("Prefilled content").Height(40)
                        )
                    ),

                    // Checkbox section
                    Section("Checkbox",
                        Checkbox.New(isChecked, isChecked ? "Checked" : "Unchecked")
                            .OnCheckedChanged(v => CheckboxValue = v)
                    ),

                    // Radio buttons section
                    Section("Radio Buttons",
                        Flex.Column(gap: 10).Children(
                            RadioButton.New("theme-radio", radioValue == "Option A", "Option A")
                                .OnRadioChanged(_ => RadioValue = "Option A"),
                            RadioButton.New("theme-radio", radioValue == "Option B", "Option B")
                                .OnRadioChanged(_ => RadioValue = "Option B"),
                            RadioButton.New("theme-radio", radioValue == "Option C", "Option C")
                                .OnRadioChanged(_ => RadioValue = "Option C")
                        )
                    ),

                    // Color palette display
                    Section("Theme Colors",
                        Flex.Row(gap: 6).FlexWrap(FlexWrap.Wrap).Children(
                            ColorSwatch("Primary", _app.Theme.Primary),
                            ColorSwatch("Secondary", _app.Theme.Secondary),
                            ColorSwatch("Danger", _app.Theme.Danger),
                            ColorSwatch("Surface", _app.Theme.Surface),
                            ColorSwatch("Border", _app.Theme.Border),
                            ColorSwatch("FocusRing", _app.Theme.FocusRing)
                        )
                    )
                )
            )
        );
    }

    private Element[] BuildThemeButtons(int currentIndex)
    {
        var buttons = new Element[_themeNames.Length];
        for (int i = 0; i < _themeNames.Length; i++)
        {
            var index = i;
            var name = _themeNames[i];
            var isActive = i == currentIndex;
            var btn = Button.New(name, isActive ? ButtonVariant.Primary : ButtonVariant.Secondary);
            btn.OnClick(_ => ThemeIndex = index);
            buttons[i] = btn;
        }
        return buttons;
    }

    private static Element Section(string title, Element content)
    {
        return Flex.Column(gap: 8).Padding(new Thickness(0, 16, 0, 16)).FlexShrink(0).Children(
            Text.New(title).FontSize(13f).FontWeight(FontWeight.SemiBold),
            content
        );
    }

    private static Element ColorSwatch(string label, Color color)
    {
        return Flex.Column(gap: 4).AlignItems(AlignItems.Center).Children(
            Box.New().Width(48).Height(48).Background(color).CornerRadius(8f),
            Text.New(label).FontSize(10f)
        );
    }
}
