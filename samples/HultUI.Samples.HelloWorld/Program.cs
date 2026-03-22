using HultUI;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Rendering;
using HultUI.Core.Styling;

// State
var isDark = false;

// Elements we'll recolor on theme toggle
BoxElement? footerBar = null;
BoxElement? sidebarFill = null;
var sidebarItems = new List<(BoxElement item, string lightHex, string darkHex)>();

// Theme toggle button (wired after app creation)
var toggleBtn = Box.New().Width(120).Padding(12)
    .Background(Color.FromHex("#F59E0B")).CornerRadius(8f)
    .TextColor(Color.FromHex("#FFFFFF")).FontWeight(FontWeight.Bold)
    .Cursor(CursorType.Pointer)
    .AccessibleName("Toggle theme")
    .Children(Text.New("Dark Mode"));
var toggleLabel = (TextElement)toggleBtn.Children[0];

var root = Flex.Column(gap: 16).Padding(24).AccessibleRole(AccessibilityRole.Application).AccessibleName("HultUI Demo").Children(

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
        Flex.Column(gap: 8).Width(200).AccessibleRole(AccessibilityRole.Group).AccessibleName("Navigation").Children(
            MakeSidebarItem("#1E293B", "#1E293B", "Dashboard"),
            MakeSidebarItem("#334155", "#334155", "Components"),
            MakeSidebarItem("#475569", "#475569", "Settings"),
            sidebarFill = Box.New().FlexGrow(1).Background(Color.FromHex("#0F172A")).CornerRadius(6f)
        ),

        // Content area
        Flex.Column(gap: 16).FlexGrow(1).Children(

            // Reactive counter component
            new CounterComponent().Mount(),

            // Input controls row
            MakeInputControlsDemo(),

            // Scrollable list + canvas demo side by side
            Flex.Row(gap: 16).FlexGrow(1).Children(

                // Scrollable list
                ScrollView.New().FlexGrow(1).CornerRadius(8f).Background(Color.FromHex("#F8FAFC")).Children(
                    MakeScrollableList()
                ),

                // Canvas drawing demo
                Canvas.New(DrawCanvasDemo)
                    .Width(200).FlexShrink(0)
                    .Background(Color.FromHex("#0F172A")).CornerRadius(8f)
            )
        )
    ),

    // Footer
    footerBar = Box.New().Height(40).FlexShrink(0).Padding(10).Background(Color.FromHex("#1E293B")).CornerRadius(8f)
        .TextColor(Color.FromHex("#64748B")).FontSize(12f)
        .Children(Text.New("Built with HultUI — Press F12 for DevTools"))
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

    if (footerBar is not null)
    {
        footerBar.BackgroundColor = theme.Surface;
        footerBar.Inherited.TextColor = theme.Secondary;
    }
    foreach (var (item, lightHex, darkHex) in sidebarItems)
        item.BackgroundColor = Color.FromHex(isDark ? darkHex : lightHex);
});

app.Run();
return;

// ──── Static helper functions (top-level local functions) ────

static Element[] MakeScrollableList()
{
    string[] colors = ["#4F46E5", "#7C3AED", "#06B6D4", "#14B8A6", "#22C55E", "#EAB308", "#F97316", "#EF4444",
                        "#EC4899", "#8B5CF6", "#3B82F6", "#10B981", "#F59E0B", "#DC2626", "#6366F1", "#0EA5E9"];
    var items = new Element[colors.Length];
    for (int i = 0; i < colors.Length; i++)
    {
        var hex = colors[i];
        var baseColor = Color.FromHex(hex);
        var hoverColor = new Color(
            (byte)(baseColor.R * 0.85f), (byte)(baseColor.G * 0.85f),
            (byte)(baseColor.B * 0.85f), baseColor.A);

        var item = Flex.Row(gap: 12).Padding(12).AlignItems(AlignItems.Center)
            .Background(i % 2 == 0 ? Color.Transparent : Color.FromHex("#F1F5F9"))
            .Cursor(CursorType.Pointer)
            .Children(
                Box.New().Width(32).Height(32).Background(baseColor).CornerRadius(6f),
                Text.New($"Item {i + 1} — Color {hex}")
            );

        item.OnPointerEnter(_ => item.BackgroundColor = hoverColor);
        item.OnPointerLeave(_ => item.BackgroundColor = i % 2 == 0 ? Color.Transparent : Color.FromHex("#F1F5F9"));
        item.OnClick(_ => Console.WriteLine($"List item clicked: {i + 1}"));

        items[i] = item;
    }
    return items;
}

static void DrawCanvasDemo(ICanvasDrawingContext ctx)
{
    var w = ctx.Bounds.Width;
    var h = ctx.Bounds.Height;

    // Background grid
    var gridColor = new Color(255, 255, 255, 20);
    for (float y = 0; y < h; y += 20)
        ctx.DrawLine(new Point(0, y), new Point(w, y), gridColor, 1);
    for (float x = 0; x < w; x += 20)
        ctx.DrawLine(new Point(x, 0), new Point(x, h), gridColor, 1);

    // Decorative circles
    ctx.DrawCircle(new Point(w / 2, h * 0.3f), 40, new Color(79, 70, 229, 180));
    ctx.DrawCircleStroke(new Point(w / 2, h * 0.3f), 55, new Color(99, 102, 241, 100), 2);
    ctx.DrawCircle(new Point(w * 0.3f, h * 0.6f), 25, new Color(6, 182, 212, 150));
    ctx.DrawCircle(new Point(w * 0.7f, h * 0.55f), 30, new Color(34, 197, 94, 150));

    // Title
    ctx.DrawText("Canvas", new Point(w / 2 - 30, h * 0.75f), new Color(255, 255, 255), 18);

    // Rounded rect border
    ctx.DrawRoundedRectStroke(
        new Rect(10, 10, w - 20, h - 20),
        new CornerRadius(8), new Color(255, 255, 255, 40), 1);
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
        .Cursor(CursorType.Pointer)
        .AccessibleName(label)
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
        .Cursor(CursorType.Pointer)
        .AccessibleName(label)
        .Children(Text.New(label));

    sidebarItems.Add((item, lightHex, darkHex));

    item.OnPointerEnter(_ => item.BackgroundColor = hoverColor);
    item.OnPointerLeave(_ => item.BackgroundColor = Color.FromHex(isDark ? darkHex : lightHex));
    item.OnClick(_ => Console.WriteLine($"Sidebar clicked: {label}"));

    return item;
}

static Element MakeInputControlsDemo()
{
    var statusLabel = Text.New("Type in the text field, toggle checkboxes, or select a radio option.");

    return Flex.Column(gap: 12).FlexShrink(0).Padding(16)
        .Background(Color.FromHex("#F8FAFC")).CornerRadius(8f).Children(

        // Text input
        Flex.Row(gap: 12).AlignItems(AlignItems.Center).Children(
            Text.New("Name:").FontSize(14f),
            TextInput.New()
                .PlaceholderText("Enter your name...")
                .OnTextChanged(text => statusLabel.Content = $"Hello, {text}!")
                .Width(250).Height(32)
                .Background(Color.FromHex("#FFFFFF")).CornerRadius(4f)
                .BorderColor(Color.FromHex("#CBD5E1"))
        ),

        // Checkboxes + Radio buttons side by side
        Flex.Row(gap: 32).AlignItems(AlignItems.FlexStart).Children(

            // Checkboxes
            Flex.Column(gap: 8).Children(
                Text.New("Options:").FontSize(14f).FontWeight(FontWeight.SemiBold),
                Flex.Row(gap: 8).AlignItems(AlignItems.Center).Children(
                    Checkbox.New().OnCheckedChanged(v => Console.WriteLine($"Notifications: {v}")),
                    Text.New("Enable notifications").FontSize(13f)
                ),
                Flex.Row(gap: 8).AlignItems(AlignItems.Center).Children(
                    Checkbox.New(true).OnCheckedChanged(v => Console.WriteLine($"Dark sidebar: {v}")),
                    Text.New("Dark sidebar").FontSize(13f)
                ),
                Flex.Row(gap: 8).AlignItems(AlignItems.Center).Children(
                    Checkbox.New().OnCheckedChanged(v => Console.WriteLine($"Compact mode: {v}")),
                    Text.New("Compact mode").FontSize(13f)
                )
            ),

            // Radio buttons
            Flex.Column(gap: 8).Children(
                Text.New("Theme:").FontSize(14f).FontWeight(FontWeight.SemiBold),
                Flex.Row(gap: 8).AlignItems(AlignItems.Center).Children(
                    RadioButton.New("theme", true),
                    Text.New("System").FontSize(13f)
                ),
                Flex.Row(gap: 8).AlignItems(AlignItems.Center).Children(
                    RadioButton.New("theme"),
                    Text.New("Light").FontSize(13f)
                ),
                Flex.Row(gap: 8).AlignItems(AlignItems.Center).Children(
                    RadioButton.New("theme"),
                    Text.New("Dark").FontSize(13f)
                )
            )
        ),

        // Status line
        statusLabel.FontSize(12f).TextColor(Color.FromHex("#64748B"))
    );
}

// ──── Reactive Counter Component ────
// Demonstrates the Component model with [Reactive] source generator.
// The [Reactive] attribute generates a Signal<int> backing field automatically.

partial class CounterComponent : Component
{
    [Reactive] public partial int Count { get; set; }

    public override Element Build()
    {
        var count = Count; // reads the generated Signal<int> — tracked by the Effect
        var label = count == 0 ? "Clicked 0 times" : $"Clicked {count} time{(count == 1 ? "" : "s")}";

        return Flex.Row(gap: 12).FlexShrink(0).AlignItems(AlignItems.Center).Children(
            MakeButton("#4F46E5", "Click me!", _ => Count++),
            MakeButton("#DC2626", "Reset", _ => Count = 0),
            Box.New().Padding(12).Background(Color.FromHex("#F1F5F9")).CornerRadius(8f)
                .TextColor(Color.FromHex("#334155")).FontSize(16f)
                .Children(Text.New(label))
        );
    }

    private static BoxElement MakeButton(string hex, string label, Action<ClickEvent> onClick)
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
            .Cursor(CursorType.Pointer)
            .AccessibleName(label)
            .Children(Text.New(label));

        btn.OnPointerEnter(_ => btn.BackgroundColor = hoverColor);
        btn.OnPointerLeave(_ => btn.BackgroundColor = baseColor);
        btn.OnPointerDown(_ => btn.BackgroundColor = pressColor);
        btn.OnPointerUp(_ => btn.BackgroundColor = hoverColor);
        btn.OnClick(onClick);

        return btn;
    }
}
