using System.Threading;
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

// Demonstrates HultUI's accessibility support. Run it and attach the Windows
// SDK inspect.exe or turn on Narrator: the whole tree below is exposed to
// UI Automation — control types, names, bounds — and the button, checkbox and
// text field are navigable and actionable. On Windows the providers are served
// as native COM objects from the window procedure, so it works for both the
// custom-frame (borderless) window here and normal windows.

using var app = new Application("HultUI Accessibility", 720, 540, WindowStyle.Borderless);
app.Theme = Theme.Dark;
var demo = new AccessibilityDemo(app.PrimaryWindow);
app.SetRoot(demo.Mount());

// Simulate content that loads after the window is up (like a commit list): the
// rows appear a moment later. They must show up in the accessibility tree even
// if an assistive tech attached while the list was still empty.
using var loader = new Timer(_ => Application.InvokeOnMainThread(() => demo.RowCount = 6),
    null, 1500, Timeout.Infinite);

app.Run();

partial class AccessibilityDemo : Component
{
    private readonly Window _window;

    [Reactive] public partial int ClickCount { get; set; }
    [Reactive] public partial bool Checked { get; set; }
    [Reactive] public partial int RowCount { get; set; }

    public AccessibilityDemo(Window window) => _window = window;

    public override Element Build()
    {
        int count = ClickCount;

        return Flex.Column(gap: 0).Children(
            Flex.Row(gap: 0).WindowDrag()
                .AlignItems(AlignItems.Center)
                .Height(Dimension.Px(40))
                .Background(new Color(24, 24, 36))
                .Padding(new Thickness(4, 12, 4, 12))
                .Children(
                    Text.New("Accessibility").FontSize(13f).FontWeight(FontWeight.SemiBold),
                    Box.New().FlexGrow(1),
                    CloseButton()
                ),
            ScrollView.New().FlexGrow(1).Children(
                Flex.Column(gap: 16).Padding(24).FlexShrink(0).Children(
                    Text.New("Accessible UI").FontSize(22f).FontWeight(FontWeight.Bold),
                    Text.New("Attach inspect.exe or Narrator. Every element here is exposed "
                        + "to UI Automation and the controls below are navigable and actionable.")
                        .FontSize(13f).TextColor(new Color(160, 160, 180)),

                    Flex.Row(gap: 12).AlignItems(AlignItems.Center).Children(
                        Box.New()
                            .Padding(new Thickness(8, 18, 8, 18))
                            .Background(new Color(99, 102, 241))
                            .CornerRadius(6f)
                            .Cursor(CursorType.Pointer)
                            .OnClick(_ => ClickCount++)
                            .Children(Text.New("Click Me").FontSize(13f).FontWeight(FontWeight.SemiBold)),
                        Text.New($"Clicked {count} time(s)").FontSize(13f).TextColor(new Color(160, 160, 180))
                    ),

                    Checkbox.New(Checked, "Enable the thing").OnCheckedChanged(v => Checked = v),

                    TextInput.New().PlaceholderText("Type your name").Width(Dimension.Px(240)),

                    Text.New(RowCount == 0 ? "Loading commits…" : "Commits")
                        .FontSize(15f).FontWeight(FontWeight.SemiBold),
                    BuildCommitList()
                )
            )
        );
    }

    private Element BuildCommitList()
    {
        var list = Flex.Column(gap: 4);
        for (int i = 0; i < RowCount; i++)
            list.Children(Text.New($"commit #{i + 1}: did a thing").FontSize(12f)
                .TextColor(new Color(150, 160, 190)));
        return list;
    }

    private Element CloseButton()
    {
        var b = Box.New()
            .Width(Dimension.Px(36)).Height(Dimension.Px(28))
            .CornerRadius(4f).Cursor(CursorType.Pointer);
        b.Flex.JustifyContent = JustifyContent.Center;
        b.Flex.AlignItems = AlignItems.Center;
        b.ReplaceChildren(Text.New("×").FontSize(14f).TextColor(new Color(180, 180, 200)));
        b.On<ClickEvent>(_ => _window.Close());
        return b;
    }
}
