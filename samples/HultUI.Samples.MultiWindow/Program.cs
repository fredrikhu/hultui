using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

// Shared state: a color that both windows reflect
var currentColor = Color.FromHex("#4F46E5");
var colorSwatch = Box.New().Width(80).Height(80).Background(currentColor).CornerRadius(12f);
var colorLabel = Text.New("#4F46E5").FontSize(14f);

// ── Main window ──
var mainRoot = Flex.Column(gap: 16).Padding(24).Children(
    Text.New("Multi-Window Demo").FontSize(24f).FontWeight(FontWeight.Bold),
    Text.New("This is the main window. Click the button to open a color picker window.").FontSize(14f),
    Flex.Row(gap: 16).AlignItems(AlignItems.Center).Children(
        colorSwatch,
        Flex.Column(gap: 4).Children(
            Text.New("Current Color").FontSize(12f).FontWeight(FontWeight.SemiBold),
            colorLabel
        )
    )
);

using var app = new Application("Main Window", 500, 300);
app.SetRoot(mainRoot);

// "Open Picker" button — added after app creation so we can call CreateWindow
Window? pickerWindow = null;

var openButton = Button.New("Open Color Picker");
openButton.OnClick(_ =>
{
    if (pickerWindow is not null && pickerWindow.IsOpen) return;
    pickerWindow = CreatePickerWindow(app);
});

mainRoot.AddChild(openButton);

app.Run();

// ── Color picker window factory ──
Window CreatePickerWindow(Application application)
{
    string[] colors =
    [
        "#4F46E5", "#7C3AED", "#EC4899", "#EF4444",
        "#F97316", "#EAB308", "#22C55E", "#06B6D4",
        "#3B82F6", "#8B5CF6", "#14B8A6", "#64748B"
    ];

    var swatches = new Element[colors.Length];
    for (int i = 0; i < colors.Length; i++)
    {
        var hex = colors[i];
        var color = Color.FromHex(hex);

        var swatch = Box.New()
            .Width(48).Height(48)
            .Background(color).CornerRadius(8f)
            .Cursor(CursorType.Pointer);

        swatch.OnPointerEnter(_ => swatch.Box.BorderWidth = new Thickness(2f));
        swatch.OnPointerEnter(_ => swatch.BorderColor = Color.White);
        swatch.OnPointerLeave(_ => swatch.Box.BorderWidth = default);

        swatch.OnClick(_ =>
        {
            currentColor = color;
            colorSwatch.BackgroundColor = color;
            colorLabel.Content = hex;
        });

        swatches[i] = swatch;
    }

    var grid = Flex.Row(gap: 8).FlexWrap(FlexWrap.Wrap).Children(swatches);

    var pickerRoot = Flex.Column(gap: 12).Padding(16).Children(
        Text.New("Pick a Color").FontSize(18f).FontWeight(FontWeight.Bold),
        Text.New("Click a swatch to update the main window.").FontSize(13f),
        grid
    );

    var win = application.CreateWindow("Color Picker", 280, 260);
    win.SetRoot(pickerRoot);
    return win;
}
