using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

// State
var clickCount = 0;
var isDark = false;
TextElement? counterText = null;

// Elements we'll recolor on theme toggle
BoxElement? counterDisplay = null;
BoxElement? footerBar = null;
BoxElement? sidebarFill = null;
var sidebarItems = new List<(BoxElement item, string lightHex, string darkHex)>();

// Theme toggle button (wired after app creation)
var toggleBtn = Box.New().Width(120).Padding(12)
    .Background(Color.FromHex("#F59E0B")).CornerRadius(8f)
    .TextColor(Color.FromHex("#FFFFFF")).FontWeight(FontWeight.Bold)
    .Children(Text.New("Dark Mode"));
var toggleLabel = (TextElement)toggleBtn.Children[0];

var root = Flex.Column(gap: 16).Padding(24).Children(

    // Header row
    Flex.Row(gap: 12).Height(60).FlexShrink(0).AlignItems(AlignItems.Center).Children(
        Box.New().FlexGrow(1).Padding(new Thickness(6, 16, 6, 16))
            .Background(Color.FromHex("#4F46E5")).CornerRadius(8f)
            .TextColor(Color.FromHex("#FFFFFF")).FontSize(24f).FontWeight(FontWeight.Bold)
            .Children(Text.New("HultUI")),
        MakeHeaderButton("#7C3AED", "Docs"),
        toggleBtn
    ),

    // Main content area
    Flex.Row(gap: 16).FlexGrow(1).Children(

        // Sidebar
        Flex.Column(gap: 8).Width(200).Children(
            MakeSidebarItem("#1E293B", "#1E293B", "Dashboard"),
            MakeSidebarItem("#334155", "#334155", "Components"),
            MakeSidebarItem("#475569", "#475569", "Settings"),
            sidebarFill = Box.New().FlexGrow(1).Background(Color.FromHex("#0F172A")).CornerRadius(6f)
        ),

        // Content area
        Flex.Column(gap: 16).FlexGrow(1).Children(

            // Interactive demo section
            Flex.Row(gap: 12).FlexShrink(0).AlignItems(AlignItems.Center).Children(
                MakeButton("#4F46E5", "Click me!", _ =>
                {
                    clickCount++;
                    if (counterText is not null)
                        counterText.Content = $"Clicked {clickCount} time{(clickCount == 1 ? "" : "s")}";
                }),
                MakeButton("#DC2626", "Reset", _ =>
                {
                    clickCount = 0;
                    if (counterText is not null)
                        counterText.Content = "Clicked 0 times";
                }),
                counterDisplay = Box.New().Padding(12).Background(Color.FromHex("#F1F5F9")).CornerRadius(8f)
                    .TextColor(Color.FromHex("#334155")).FontSize(16f)
                    .Children(counterText = Text.New("Clicked 0 times"))
            ),

            // Color card grid
            Flex.Row(gap: 12).FlexWrap(FlexWrap.Wrap).FlexGrow(1)
                .AlignContent(AlignContent.FlexStart)
                .Children(
                    MakeCard("#06B6D4", "Cyan"),
                    MakeCard("#14B8A6", "Teal"),
                    MakeCard("#22C55E", "Green"),
                    MakeCard("#EAB308", "Yellow"),
                    MakeCard("#F97316", "Orange"),
                    MakeCard("#EF4444", "Red")
                )
        )
    ),

    // Footer
    footerBar = Box.New().Height(40).FlexShrink(0).Padding(10).Background(Color.FromHex("#1E293B")).CornerRadius(8f)
        .TextColor(Color.FromHex("#64748B")).FontSize(12f)
        .Children(Text.New("Built with HultUI — a cross-platform C# UI framework"))
);

using var app = new Application("HultUI - Hello World", 900, 650);
app.SetRoot(root);

// Wire theme toggle now that app exists
var lightToggle = Color.FromHex("#F59E0B");
var darkToggle = Color.FromHex("#6366F1");

toggleBtn.OnPointerEnter(_ =>
{
    var c = isDark ? darkToggle : lightToggle;
    toggleBtn.BackgroundColor = new Color(
        (byte)(c.R * 0.85f), (byte)(c.G * 0.85f), (byte)(c.B * 0.85f), c.A);
});
toggleBtn.OnPointerLeave(_ => toggleBtn.BackgroundColor = isDark ? darkToggle : lightToggle);
toggleBtn.OnClick(_ =>
{
    isDark = !isDark;
    var theme = isDark ? Theme.Dark : Theme.Light;
    app.Theme = theme;

    toggleBtn.BackgroundColor = isDark ? darkToggle : lightToggle;
    toggleLabel.Content = isDark ? "Light Mode" : "Dark Mode";

    if (counterDisplay is not null)
    {
        counterDisplay.BackgroundColor = theme.SurfaceVariant;
        counterDisplay.Inherited.TextColor = isDark ? Color.FromHex("#E2E8F0") : Color.FromHex("#334155");
    }
    if (footerBar is not null)
    {
        footerBar.BackgroundColor = theme.Surface;
        footerBar.Inherited.TextColor = theme.Secondary;
    }
    foreach (var (item, lightHex, darkHex) in sidebarItems)
        item.BackgroundColor = Color.FromHex(isDark ? darkHex : lightHex);
});

app.Run();

static BoxElement MakeCard(string hex, string label)
{
    var baseColor = Color.FromHex(hex);
    var card = Flex.Column(gap: 0)
        .Width(160).Height(120)
        .Background(baseColor).CornerRadius(12f)
        .Padding(16)
        .JustifyContent(JustifyContent.FlexEnd)
        .TextColor(Color.FromHex("#FFFFFF")).FontSize(18f).FontWeight(FontWeight.Bold)
        .Children(Text.New(label));

    var darkColor = new Color(
        (byte)(baseColor.R * 0.8f), (byte)(baseColor.G * 0.8f),
        (byte)(baseColor.B * 0.8f), baseColor.A);

    card.OnPointerEnter(_ => card.BackgroundColor = darkColor);
    card.OnPointerLeave(_ => card.BackgroundColor = baseColor);
    card.OnClick(_ => Console.WriteLine($"Card clicked: {label}"));

    return card;
}

static BoxElement MakeButton(string hex, string label, Action<ClickEvent> onClick)
{
    var baseColor = Color.FromHex(hex);
    var hoverColor = new Color(
        (byte)(baseColor.R * 0.85f), (byte)(baseColor.G * 0.85f),
        (byte)(baseColor.B * 0.85f), baseColor.A);
    var pressColor = new Color(
        (byte)(baseColor.R * 0.7f), (byte)(baseColor.G * 0.7f),
        (byte)(baseColor.B * 0.7f), baseColor.A);

    var btn = Box.New().Padding(new Thickness(10, 20, 10, 20))
        .Background(baseColor).CornerRadius(8f)
        .TextColor(Color.FromHex("#FFFFFF")).FontSize(14f).FontWeight(FontWeight.Bold)
        .Children(Text.New(label));

    btn.OnPointerEnter(_ => btn.BackgroundColor = hoverColor);
    btn.OnPointerLeave(_ => btn.BackgroundColor = baseColor);
    btn.OnPointerDown(_ => btn.BackgroundColor = pressColor);
    btn.OnPointerUp(_ => btn.BackgroundColor = hoverColor);
    btn.OnClick(onClick);

    return btn;
}

static BoxElement MakeHeaderButton(string hex, string label)
{
    var baseColor = Color.FromHex(hex);
    var hoverColor = new Color(
        (byte)(baseColor.R * 0.85f), (byte)(baseColor.G * 0.85f),
        (byte)(baseColor.B * 0.85f), baseColor.A);

    var btn = Box.New().Width(120).Padding(12)
        .Background(baseColor).CornerRadius(8f)
        .TextColor(Color.FromHex("#FFFFFF"))
        .Children(Text.New(label));

    btn.OnPointerEnter(_ => btn.BackgroundColor = hoverColor);
    btn.OnPointerLeave(_ => btn.BackgroundColor = baseColor);
    btn.OnClick(_ => Console.WriteLine($"Header button clicked: {label}"));

    return btn;
}

BoxElement MakeSidebarItem(string lightHex, string darkHex, string label)
{
    var baseColor = Color.FromHex(lightHex);
    var hoverColor = Color.FromHex("#3B82F6");

    var item = Box.New().Height(40).Padding(10).Background(baseColor).CornerRadius(6f)
        .TextColor(Color.FromHex("#94A3B8")).FontSize(14f)
        .Children(Text.New(label));

    sidebarItems.Add((item, lightHex, darkHex));

    item.OnPointerEnter(_ => item.BackgroundColor = hoverColor);
    item.OnPointerLeave(_ => item.BackgroundColor = Color.FromHex(isDark ? darkHex : lightHex));
    item.OnClick(_ => Console.WriteLine($"Sidebar clicked: {label}"));

    return item;
}
