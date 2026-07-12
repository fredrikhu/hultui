using HultUI;
using HultUI.Core.Animation;
using HultUI.Core.Builders;
using HultUI.Core.Dialogs;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

using var app = new Application("HultUI - Dialogs", 720, 520);

var status = Text.New("Click a button to open a dialog.")
    .FontSize(13f).TextColor(Color.FromHex("#64748B"));

IOverlayHost Host() => app.PrimaryWindow;

var root = Flex.Column(gap: 22).Padding(36).Background(Color.FromHex("#EEF2F7")).Children(
    Text.New("Dialogs").FontSize(28f).FontWeight(FontWeight.Bold).TextColor(Color.FromHex("#1E293B")),

    Flex.Row(gap: 12).FlexShrink(0).Children(
        // Convenience: MessageBox-style confirm with a result.
        LauncherButton("Confirm", async () =>
        {
            var ok = await Dialog.Confirm(Host(), "Delete branch?",
                "This will permanently delete the branch. This cannot be undone.",
                confirmText: "Delete", cancelText: "Cancel", destructive: true);
            status.Content = ok ? "Confirmed — branch deleted." : "Cancelled.";
        }),

        // Convenience: single-button alert.
        LauncherButton("Alert", async () =>
        {
            await Dialog.Alert(Host(), "Heads up", "Your changes have been saved.");
            status.Content = "Alert acknowledged.";
        }),

        // Fully-fledged: arbitrary content with live data binding + a typed result.
        LauncherButton("Custom (data-bound)", async () =>
        {
            var name = await Dialog.Show<string?>(Host(), BuildNameForm);
            status.Content = name is null ? "Custom dialog cancelled." : $"Hello, {name}!";
        })),

    status
);

app.SetRoot(root);
app.Run();
return;

// A custom dialog whose content updates live as the user types — proof that
// ordinary data binding works inside a dialog. Closes with the entered name.
Element BuildNameForm(IDialogController<string?> ctrl)
{
    var theme = app.Theme;
    string current = "";

    var preview = Text.New("Type your name above.").FontSize(12f).TextColor(theme.Secondary);
    var input = TextInput.New()
        .PlaceholderText("Your name")
        .Width(300).Height(34)
        .Background(Color.White).CornerRadius(6f).BorderColor(theme.Border).BorderWidth(1f)
        .OnTextChanged(t =>
        {
            current = t;
            preview.Content = string.IsNullOrWhiteSpace(t) ? "Type your name above." : $"Preview: Hello, {t}!";
        });

    return Box.New()
        .Width(360).Padding(20).CornerRadius(12f)
        .Background(theme.Surface).BorderColor(theme.Border).BorderWidth(1f)
        .BoxShadow(0, 18, 48, new Color(0, 0, 0, 90))
        .Children(Flex.Column(gap: 14).Children(
            Text.New("What's your name?").FontSize(16f).FontWeight(FontWeight.Bold).TextColor(theme.TextColor),
            input,
            preview,
            Flex.Row(gap: 8).JustifyContent(JustifyContent.FlexEnd).AlignItems(AlignItems.Center).Children(
                DialogButton(theme.SurfaceVariant, theme.TextColor, "Cancel", () => ctrl.Close(null)),
                DialogButton(theme.Primary, theme.PrimaryText, "OK", () => ctrl.Close(current)))));
}

static Element DialogButton(Color bg, Color fg, string label, Action onClick)
{
    var btn = Box.New()
        .Padding(new Thickness(8, 16, 8, 16)).CornerRadius(8f)
        .Background(bg).Cursor(CursorType.Pointer)
        .OnHover(b => b.Background(new Color((byte)(bg.R * 0.85f), (byte)(bg.G * 0.85f), (byte)(bg.B * 0.85f), bg.A)))
        .Transition(TransitionProperty.Background, 120f)
        .Children(Text.New(label).FontSize(13f).FontWeight(FontWeight.SemiBold).TextColor(fg));
    btn.On<ClickEvent>(e => { e.StopPropagation = true; onClick(); });
    return btn;
}

static BoxElement LauncherButton(string label, Action onClick)
{
    var btn = Flex.Column().Height(44).Padding(new Thickness(0, 20, 0, 20)).FlexShrink(0)
        .Background(Color.FromHex("#4F46E5")).CornerRadius(10f)
        .BoxShadow(0, 4, 12, new Color(15, 23, 42, 55)).Cursor(CursorType.Pointer)
        .AlignItems(AlignItems.Center).JustifyContent(JustifyContent.Center)
        .OnHover(b => b.Background(Color.FromHex("#4338CA")))
        .Transition(TransitionProperty.Background, 140f)
        .Children(Text.New(label).FontSize(14f).FontWeight(FontWeight.Bold).TextColor(Color.White));
    btn.On<ClickEvent>(_ => onClick());
    return btn;
}
