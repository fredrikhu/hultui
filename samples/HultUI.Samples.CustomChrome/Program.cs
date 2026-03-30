using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

using var app = new Application("Custom Chrome", 700, 500, WindowStyle.Borderless);
app.Theme = Theme.Dark;
var window = app.PrimaryWindow;
app.SetRoot(new CustomChromeDemo(window).Mount());
app.Run();

// ── Demo component ──

partial class CustomChromeDemo : Component
{
    private readonly Window _window;

    [Reactive] public partial int ClickCount { get; set; }

    public CustomChromeDemo(Window window)
    {
        _window = window;
    }

    public override Element Build()
    {
        var count = ClickCount;

        return Flex.Column().Children(
            BuildTitleBar(),
            BuildContent(count)
        );
    }

    private Element BuildTitleBar()
    {
        var titleBar = Flex.Row(gap: 0)
            .WindowDrag()
            .AlignItems(AlignItems.Center)
            .Height(Dimension.Px(40))
            .Background(new Color(24, 24, 36))
            .Padding(new Thickness(4, 12, 4, 12))
            .Children(
                // App icon + title
                Text.New("Custom Chrome Demo")
                    .FontSize(13f).FontWeight(FontWeight.SemiBold)
                    .TextColor(new Color(200, 200, 220)),

                // Spacer
                Box.New().FlexGrow(1),

                // Window controls
                WindowButton("\u2212", new Color(80, 80, 100), () => _window.Minimize()),
                WindowButton(_window.IsMaximized ? "\u25A1" : "\u25FB", new Color(80, 80, 100),
                    () => _window.ToggleMaximize()),
                WindowButton("\u00D7", new Color(200, 60, 60), () => _window.Close())
            );

        return titleBar;
    }

    private Element WindowButton(string label, Color hoverColor, Action onClick)
    {
        var btn = Box.New()
            .Width(Dimension.Px(36)).Height(Dimension.Px(28))
            .CornerRadius(4f)
            .Cursor(CursorType.Pointer);

        btn.Flex.JustifyContent = JustifyContent.Center;
        btn.Flex.AlignItems = AlignItems.Center;
        btn.ReplaceChildren(
            Text.New(label).FontSize(14f).TextColor(new Color(180, 180, 200))
        );

        btn.On<PointerEnterEvent>(_ => btn.BackgroundColor = hoverColor);
        btn.On<PointerLeaveEvent>(_ => btn.BackgroundColor = Color.Transparent);
        btn.On<ClickEvent>(_ => onClick());

        return btn;
    }

    private Element BuildContent(int count)
    {
        return ScrollView.New().FlexGrow(1).Children(
            Flex.Column(gap: 24).Padding(32).FlexShrink(0).Children(
                Text.New("Custom Window Chrome")
                    .FontSize(24f).FontWeight(FontWeight.Bold),

                Text.New("This window has no OS title bar. The title bar above is built "
                    + "entirely with HultUI elements. You can drag the window by the title bar, "
                    + "resize from any edge or corner, and use the window control buttons.")
                    .FontSize(13f).TextColor(new Color(160, 160, 180)),

                Section("Features",
                    Flex.Column(gap: 8).Children(
                        FeatureItem("Window dragging from custom title bar"),
                        FeatureItem("Edge and corner resize (6px invisible border)"),
                        FeatureItem("Minimize, maximize/restore, and close buttons"),
                        FeatureItem("Buttons inside drag region are still clickable")
                    )
                ),

                Section("Interactive Test",
                    Flex.Row(gap: 12).AlignItems(AlignItems.Center).Children(
                        Box.New()
                            .Padding(new Thickness(10, 20, 10, 20))
                            .Background(new Color(99, 102, 241))
                            .CornerRadius(8f)
                            .Cursor(CursorType.Pointer)
                            .OnClick(_ => ClickCount++)
                            .Children(Text.New("Click Me").FontSize(13f).FontWeight(FontWeight.SemiBold)),
                        Text.New($"Clicked {count} time(s)")
                            .FontSize(13f).TextColor(new Color(160, 160, 180))
                    )
                )
            )
        );
    }

    private static Element FeatureItem(string text)
    {
        return Flex.Row(gap: 8).AlignItems(AlignItems.Center).Children(
            Text.New("\u2022").FontSize(14f).TextColor(new Color(99, 102, 241)),
            Text.New(text).FontSize(13f)
        );
    }

    private static Element Section(string title, params Element[] content)
    {
        var children = new Element[content.Length + 1];
        children[0] = Text.New(title).FontSize(16f).FontWeight(FontWeight.SemiBold);
        for (int i = 0; i < content.Length; i++)
            children[i + 1] = content[i];
        return Flex.Column(gap: 8).FlexShrink(0).Children(children);
    }
}
