using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

// ── Shared reactive state ──
// Both windows read/write these signals. When the picker sets a new color,
// the main window's component automatically rebuilds.
var selectedColor = new Signal<Color>(Color.FromHex("#4F46E5"));
var selectedHex = new Signal<string>("#4F46E5");

using var app = new Application("Main Window", 500, 350);
app.SetRoot(new MainWindow(selectedColor, selectedHex, app).Mount());
app.Run();

// ── Main window component ──
// Reads the shared signals in Build(), so it rebuilds whenever the color changes.

partial class MainWindow : Component
{
    private readonly Signal<Color> _color;
    private readonly Signal<string> _hex;
    private readonly Application _app;
    private Window? _pickerWindow;

    [Reactive] public partial bool IsPickerOpen { get; set; }

    public MainWindow(Signal<Color> color, Signal<string> hex, Application app)
    {
        _color = color;
        _hex = hex;
        _app = app;
    }

    public override Element Build()
    {
        // Reading .Value inside Build() creates reactive dependencies —
        // when either signal changes, this component auto-rebuilds.
        var color = _color.Value;
        var hex = _hex.Value;
        var isPickerOpen = IsPickerOpen;

        var openButton = Button.New("Open Color Picker");
        openButton.OnClick(_ => OpenPicker());

        return Flex.Column(gap: 16).Padding(24).Children(
            Text.New("Multi-Window Demo").FontSize(24f).FontWeight(FontWeight.Bold),
            Text.New("Shared reactive state across windows. Pick a color in the second window.")
                .FontSize(14f),

            // Color display — updates automatically when signals change
            Flex.Row(gap: 16).AlignItems(AlignItems.Center).Children(
                Box.New().Width(80).Height(80).Background(color).CornerRadius(12f),
                Flex.Column(gap: 4).Children(
                    Text.New("Selected Color").FontSize(12f).FontWeight(FontWeight.SemiBold),
                    Text.New(hex).FontSize(16f).FontWeight(FontWeight.Bold).TextColor(color)
                )
            ),

            openButton,

            isPickerOpen
                ? Text.New("Picker window is open").FontSize(12f)
                    .TextColor(new Color(100, 116, 139))
                : Text.New("Picker window is closed").FontSize(12f)
                    .TextColor(new Color(148, 163, 184))
        );
    }

    private void OpenPicker()
    {
        if (IsPickerOpen) return;

        _pickerWindow = _app.CreateWindow("Color Picker", 280, 300);
        _pickerWindow.SetRoot(new ColorPicker(_color, _hex).Mount());
        _pickerWindow.Closed += () => IsPickerOpen = false;
        IsPickerOpen = true;
    }
}

// ── Color picker component ──
// Writes to the shared signals when a swatch is clicked.

partial class ColorPicker : Component
{
    private static readonly string[] Palette =
    [
        "#4F46E5", "#7C3AED", "#EC4899", "#EF4444",
        "#F97316", "#EAB308", "#22C55E", "#06B6D4",
        "#3B82F6", "#8B5CF6", "#14B8A6", "#64748B"
    ];

    private readonly Signal<Color> _color;
    private readonly Signal<string> _hex;

    [Reactive] public partial string? HoveredHex { get; set; }

    public ColorPicker(Signal<Color> color, Signal<string> hex)
    {
        _color = color;
        _hex = hex;
    }

    public override Element Build()
    {
        var currentHex = _hex.Value;
        var hoveredHex = HoveredHex;

        var swatches = new Element[Palette.Length];
        for (int i = 0; i < Palette.Length; i++)
        {
            var hex = Palette[i];
            var color = Color.FromHex(hex);
            var isSelected = hex == currentHex;

            var swatch = Box.New()
                .Width(52).Height(52)
                .Background(color).CornerRadius(8f)
                .Cursor(CursorType.Pointer);

            if (isSelected)
            {
                swatch.Box.BorderWidth = new Thickness(3f);
                swatch.BorderColor = Color.White;
            }

            swatch.OnPointerEnter(_ => HoveredHex = hex);
            swatch.OnPointerLeave(_ => { if (HoveredHex == hex) HoveredHex = null; });
            swatch.OnClick(_ =>
            {
                _color.Value = color;
                _hex.Value = hex;
            });

            swatches[i] = swatch;
        }

        return Flex.Column(gap: 12).Padding(16).Children(
            Text.New("Pick a Color").FontSize(18f).FontWeight(FontWeight.Bold),
            Flex.Row(gap: 8).FlexWrap(FlexWrap.Wrap).Children(swatches),
            Text.New(hoveredHex ?? "Hover over a color")
                .FontSize(13f).TextColor(new Color(100, 116, 139))
        );
    }
}
