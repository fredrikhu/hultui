using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

// Generate a dataset of 100,000 items
var items = Enumerable.Range(0, 100_000)
    .Select(i => new ItemData(i, $"Item #{i:N0}", GenerateColor(i)))
    .ToList();

var statusText = Text.New($"{items.Count:N0} items — scroll to explore");
var selectedText = Text.New("No selection");

var virtualList = VirtualListElement<ItemData>.New()
    .ItemHeight(48)
    .Items(items)
    .RenderWith(item => MakeRow(item))
    // 100k items — exercises the on-demand accessibility path (items exposed to a
    // screen reader without materializing them all).
    .AccessibleItemLabel(item => item.Label)
    .OnActivateItem(item => selectedText.Content = $"Selected {item.Label}")
    .FlexGrow(1);

var root = Flex.Column(gap: 0).Children(

    // Header
    Box.New().Padding(new Thickness(16, 20)).FlexShrink(0)
        .Background(Color.FromHex("#4F46E5"))
        .TextColor(Color.White)
        .Children(
            Flex.Column(gap: 4).Children(
                Text.New("VirtualList Demo").FontSize(22f).FontWeight(FontWeight.Bold),
                Text.New("100,000 items — only visible rows are materialized").FontSize(13f)
                    .TextColor(new Color(199, 210, 254))
            )
        ),

    // Controls row
    Flex.Row(gap: 12).Padding(12).FlexShrink(0).AlignItems(AlignItems.Center)
        .Background(Color.FromHex("#F8FAFC")).Children(
        Button.New("Scroll to top").OnClick(_ => ScrollTo(0)),
        Button.New("Scroll to middle").OnClick(_ => ScrollTo(50_000)),
        Button.New("Scroll to bottom").OnClick(_ => ScrollTo(99_999)),
        Box.New().FlexGrow(1),
        selectedText.FontSize(13f).TextColor(Color.FromHex("#64748B"))
    ),

    // Virtual list
    virtualList,

    // Footer
    Box.New().Padding(new Thickness(10, 16)).FlexShrink(0)
        .Background(Color.FromHex("#F1F5F9"))
        .Children(statusText.FontSize(12f).TextColor(Color.FromHex("#64748B")))
);

using var app = new Application("VirtualList Demo", 700, 600);
app.SetRoot(root);
app.Run();
return;

// ──── Helpers ────

Element MakeRow(ItemData item)
{
    var stripe = item.Index % 2 == 0
        ? Color.Transparent
        : Color.FromHex("#F8FAFC");
    var hoverColor = Color.FromHex("#EEF2FF");

    var row = Flex.Row(gap: 12).Padding(new Thickness(8, 16))
        .AlignItems(AlignItems.Center)
        .Background(stripe)
        .Cursor(CursorType.Pointer)
        .Children(
            // Index badge
            Box.New().Width(56).Height(32).CornerRadius(6f)
                .Background(item.Color)
                .Children(
                    Text.New($"#{item.Index}").FontSize(12f).FontWeight(FontWeight.Bold)
                        .TextColor(Color.White)
                )
                .Padding(new Thickness(6, 8))
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center),

            // Label
            Text.New(item.Label).FontSize(14f).FlexGrow(1),

            // Right-aligned detail
            Text.New($"Row {item.Index:N0}").FontSize(12f).TextColor(Color.FromHex("#94A3B8"))
        );

    row.OnPointerEnter(_ => row.BackgroundColor = hoverColor);
    row.OnPointerLeave(_ => row.BackgroundColor = stripe);
    row.OnClick(_ =>
    {
        selectedText.Content = $"Selected: {item.Label}";
    });

    row.ContextMenu(() =>
    [
        new ContextMenuItem { Label = "Copy label", Action = () => Console.WriteLine($"Copy: {item.Label}") },
        new ContextMenuItem { Label = "View details", Action = () => Console.WriteLine($"Details: {item}") },
        ContextMenuItem.Separator(),
        new ContextMenuItem { Label = $"Index: {item.Index}", IsDisabled = true },
    ]);

    return row;
}

void ScrollTo(int index)
{
    virtualList.ScrollOffsetY = index * 48f;
    statusText.Content = $"Scrolled to item #{index:N0}";
}

static Color GenerateColor(int index)
{
    // HSL-ish rotation for variety
    float hue = (index * 37) % 360;
    return HueToColor(hue);
}

static Color HueToColor(float hue)
{
    float h = hue / 60f;
    float x = 1f - Math.Abs(h % 2 - 1);
    var (r, g, b) = (int)h switch
    {
        0 => (1f, x, 0f),
        1 => (x, 1f, 0f),
        2 => (0f, 1f, x),
        3 => (0f, x, 1f),
        4 => (x, 0f, 1f),
        _ => (1f, 0f, x),
    };
    // Desaturate and darken slightly for pleasant badge colors
    float s = 0.7f, l = 0.45f;
    r = l + s * (r - 0.5f);
    g = l + s * (g - 0.5f);
    b = l + s * (b - 0.5f);
    return new Color(
        (byte)(Math.Clamp(r, 0, 1) * 255),
        (byte)(Math.Clamp(g, 0, 1) * 255),
        (byte)(Math.Clamp(b, 0, 1) * 255));
}

// ──── Data model ────

record ItemData(int Index, string Label, Color Color);
