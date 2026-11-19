using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

// --marks: a keyboard cursor apart from the selection, and marks the app keeps (a file manager's
// shape). --count N: how many items (100,000 by default).
bool marksMode = args.Contains("--marks");
int countAt = Array.IndexOf(args, "--count");
int count = countAt >= 0 && countAt + 1 < args.Length && int.TryParse(args[countAt + 1], out int n) && n > 0 ? n : 100_000;

// Generate the dataset
var items = Enumerable.Range(0, count)
    .Select(i => new ItemData(i, $"Item #{i:N0}", GenerateColor(i)))
    .ToList();

var statusText = Text.New($"{items.Count:N0} items — scroll to explore");
var selectedText = Text.New("No selection");
var marks = new bool[items.Count];
int marked = 0;

var virtualList = VirtualListElement<ItemData>.New()
    .ItemHeight(48)
    .Items(items)
    .RenderWith(item => MakeRow(item))
    // 100k items — exercises the on-demand accessibility path (items exposed to a
    // screen reader without materializing them all).
    .AccessibleItemLabel(item => item.Label)
    .FlexGrow(1);
virtualList.OnItemActivate = item =>
{
    virtualList.SelectedIndex = item.Index;
    selectedText.Content = $"Selected {item.Label}";
};
if (marksMode)
{
    // The list takes the keyboard itself; the cursor moves with no rebuild, and each move makes
    // only the row it left and the row it reached again.
    virtualList.IsFocusable = true;
    virtualList.CurrentIndex(0).RenderWithState((item, state) => MakeStateRow(item, state, marks[item.Index]));
    virtualList.On<KeyDownEvent>(OnListKey);
    ShowCursor();
}

var root = Flex.Column(gap: 0).Children(

    // Header
    Box.New().Padding(new Thickness(16, 20)).FlexShrink(0)
        .Background(Color.FromHex("#4F46E5"))
        .TextColor(Color.White)
        .Children(
            Flex.Column(gap: 4).Children(
                Text.New("VirtualList Demo").FontSize(22f).FontWeight(FontWeight.Bold),
                Text.New($"{items.Count:N0} items — only visible rows are materialized").FontSize(13f)
                    .TextColor(new Color(199, 210, 254))
            )
        ),

    // Controls row
    Flex.Row(gap: 12).Padding(12).FlexShrink(0).AlignItems(AlignItems.Center)
        .Background(Color.FromHex("#F8FAFC")).Children(
        Button.New("Scroll to top").OnClick(_ => ScrollTo(0)),
        Button.New("Scroll to middle").OnClick(_ => ScrollTo(items.Count / 2)),
        Button.New("Scroll to bottom").OnClick(_ => ScrollTo(items.Count - 1)),
        // Rows, or columns that scroll sideways (a file manager's Brief view).
        Button.New("Columns").OnClick(_ =>
            virtualList.Flow(virtualList.Flow == ListFlow.Rows ? ListFlow.Columns : ListFlow.Rows, 220f)),
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
    // Where the item is, in either flow: its column in columns, its row in rows.
    var cell = virtualList.ItemRect(index);
    if (virtualList.Flow == ListFlow.Columns)
        virtualList.ScrollRangeIntoViewHorizontal(cell.X, cell.Width);
    else
        virtualList.ScrollRangeIntoView(cell.Y, cell.Height, ScrollAlignment.Start);
    statusText.Content = $"Scrolled to item #{index:N0}";
}

// A row drawn from its item, its state and its mark: a filled bar where the cursor is, ◆ where
// the item is marked. The marks are the app's, so a toggle makes the rows again (InvalidateRows).
Element MakeStateRow(ItemData item, ListItemState state, bool isMarked)
{
    var accent = Color.FromHex("#4F46E5");
    return Flex.Row(gap: 12).Padding(new Thickness(8, 16, 8, 0))
        .AlignItems(AlignItems.Center)
        .Background(state.IsCurrent ? Color.FromHex("#EEF2FF") : item.Index % 2 == 0 ? Color.Transparent : Color.FromHex("#F8FAFC"))
        .Children(
            Box.New().Width(4).Height(32).CornerRadius(2f).Background(state.IsCurrent ? accent : Color.Transparent),
            Text.New(isMarked ? "◆" : " ").FontSize(14f).TextColor(accent).Width(16),
            Box.New().Width(56).Height(32).CornerRadius(6f)
                .Background(item.Color)
                .Children(
                    Text.New($"#{item.Index}").FontSize(12f).FontWeight(FontWeight.Bold)
                        .TextColor(Color.White)
                )
                .Padding(new Thickness(6, 8))
                .JustifyContent(JustifyContent.Center).AlignItems(AlignItems.Center),
            Text.New(item.Label).FontSize(14f).FlexGrow(1)
        );
}

// Up/Down/PageUp/PageDown/Home/End move the cursor; Space marks the current item, Insert marks it
// and moves down. Chorded keys are someone else's.
void OnListKey(KeyDownEvent e)
{
    if ((e.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Super)) != 0) return;
    int current = virtualList.CurrentIndex, last = items.Count - 1;
    int page = Math.Max(1, (int)(virtualList.Layout.AbsoluteContentBox.Height / 48f) - 1);
    int next;
    switch (e.Key)
    {
        case KeyCode.Down: next = current + 1; break;
        case KeyCode.Up: next = current - 1; break;
        case KeyCode.PageDown: next = current + page; break;
        case KeyCode.PageUp: next = current - page; break;
        case KeyCode.Home: next = 0; break;
        case KeyCode.End: next = last; break;
        case KeyCode.Space: ToggleMark(current); next = current; break;
        case KeyCode.Insert: ToggleMark(current); next = current + 1; break;
        default: return;
    }
    next = Math.Clamp(next, 0, last);
    virtualList.CurrentIndex = next;
    virtualList.ScrollIndexIntoView(next);
    ShowCursor();
    e.StopPropagation = true;
}

void ToggleMark(int index)
{
    if (index < 0 || index >= marks.Length) return;
    marks[index] = !marks[index];
    marked += marks[index] ? 1 : -1;
    virtualList.InvalidateRows();
}

void ShowCursor() => statusText.Content = $"Item #{virtualList.CurrentIndex:N0} · {marked:N0} marked";

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
