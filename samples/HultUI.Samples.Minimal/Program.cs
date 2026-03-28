using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

var clickCount = 0;
var label = Text.New("Click the button!");

var button = Box.New().Padding(12).Background(Color.FromHex("#4F46E5")).CornerRadius(8f)
    .TextColor(Color.White).FontWeight(FontWeight.Bold).Cursor(CursorType.Pointer)
    .Children(Text.New("Click me"));

button.OnPointerEnter(_ => button.BackgroundColor = Color.FromHex("#4338CA"));
button.OnPointerLeave(_ => button.BackgroundColor = Color.FromHex("#4F46E5"));
button.OnClick(_ =>
{
    clickCount++;
    label.Content = $"Clicked {clickCount} time{(clickCount == 1 ? "" : "s")}";
});

var root = Flex.Column(gap: 16).Padding(32).Children(
    Text.New("HultUI Minimal Example").FontSize(24f).FontWeight(FontWeight.Bold),
    Flex.Row(gap: 12).AlignItems(AlignItems.Center).Children(
        button,
        label
    )
);

using var app = new Application("Minimal Example", 500, 300);
app.SetRoot(root);
app.Run();
