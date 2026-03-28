using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

using var app = new Application("Todo List", 500, 600);
app.SetRoot(new TodoApp().Mount());
app.Run();

// ──── Data model ────

record TodoItem(string Text, bool Done);

// ──── Main app component ────

partial class TodoApp : Component
{
    [Reactive] public partial int Version { get; set; }

    private readonly List<TodoItem> _items =
    [
        new("Learn HultUI basics", true),
        new("Build a todo app", false),
        new("Add more features", false),
    ];

    private string _inputText = "";

    public override Element Build()
    {
        // Read Version to track changes (forces rebuild when items mutate)
        _ = Version;

        var doneCount = _items.Count(i => i.Done);
        var totalCount = _items.Count;

        return Flex.Column(gap: 0).Children(

            // Header
            Box.New().Padding(new Thickness(20, 24, 12, 24))
                .Background(Color.FromHex("#4F46E5"))
                .TextColor(Color.White)
                .Children(
                    Flex.Column(gap: 4).Children(
                        Text.New("Todo List").FontSize(24f).FontWeight(FontWeight.Bold),
                        Text.New($"{doneCount}/{totalCount} completed").FontSize(13f)
                            .TextColor(new Color(199, 210, 254))
                    )
                ),

            // Input row
            Flex.Row(gap: 8).Padding(16).FlexShrink(0).AlignItems(AlignItems.Center).Children(
                TextInput.New()
                    .PlaceholderText("What needs to be done?")
                    .OnTextChanged(text => _inputText = text)
                    .FlexGrow(1).Height(36)
                    .Background(Color.White).CornerRadius(6f)
                    .BorderColor(Color.FromHex("#CBD5E1")),
                MakeButton("Add", Color.FromHex("#4F46E5"), AddItem)
            ),

            // Item list (scrollable)
            ScrollView.New().FlexGrow(1).Children(
                BuildItemList()
            ),

            // Footer with clear button
            Flex.Row(gap: 8).Padding(12).FlexShrink(0)
                .Background(Color.FromHex("#F8FAFC"))
                .JustifyContent(JustifyContent.FlexEnd).Children(
                MakeButton("Clear completed", Color.FromHex("#DC2626"), ClearCompleted)
            )
        );
    }

    private Element[] BuildItemList()
    {
        var elements = new Element[_items.Count];
        for (int i = 0; i < _items.Count; i++)
        {
            var index = i;
            var item = _items[i];
            var strikeColor = item.Done ? new Color(148, 163, 184) : new Color(30, 41, 59);

            var row = Flex.Row(gap: 12).Padding(new Thickness(10, 16, 10, 16))
                .AlignItems(AlignItems.Center)
                .Background(i % 2 == 0 ? Color.Transparent : Color.FromHex("#F8FAFC"));
            row.Key = $"todo-{index}";
            elements[i] = row.Children(
                    Checkbox.New(item.Done).OnCheckedChanged(done => ToggleItem(index, done)),
                    Text.New(item.Text).FlexGrow(1).TextColor(strikeColor),
                    MakeIconButton("x", Color.FromHex("#EF4444"), () => RemoveItem(index))
                );
        }
        return elements;
    }

    private void AddItem()
    {
        var text = _inputText.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _items.Add(new TodoItem(text, false));
        _inputText = "";
        Version++;
    }

    private void ToggleItem(int index, bool done)
    {
        _items[index] = _items[index] with { Done = done };
        Version++;
    }

    private void RemoveItem(int index)
    {
        _items.RemoveAt(index);
        Version++;
    }

    private void ClearCompleted()
    {
        _items.RemoveAll(i => i.Done);
        Version++;
    }

    private static BoxElement MakeButton(string label, Color color, Action onClick)
    {
        var hoverColor = new Color(
            (byte)(color.R * 0.85f), (byte)(color.G * 0.85f),
            (byte)(color.B * 0.85f), color.A);

        var btn = Box.New().Padding(new Thickness(8, 16, 8, 16))
            .Background(color).CornerRadius(6f)
            .TextColor(Color.White).FontSize(13f).FontWeight(FontWeight.SemiBold)
            .Cursor(CursorType.Pointer)
            .Children(Text.New(label));

        btn.OnPointerEnter(_ => btn.BackgroundColor = hoverColor);
        btn.OnPointerLeave(_ => btn.BackgroundColor = color);
        btn.OnClick(_ => onClick());
        return btn;
    }

    private static BoxElement MakeIconButton(string label, Color color, Action onClick)
    {
        var btn = Box.New().Padding(new Thickness(4, 8, 4, 8))
            .CornerRadius(4f).Cursor(CursorType.Pointer)
            .TextColor(color).FontSize(12f).FontWeight(FontWeight.Bold)
            .Children(Text.New(label));

        btn.OnPointerEnter(_ => btn.BackgroundColor = new Color(color.R, color.G, color.B, 30));
        btn.OnPointerLeave(_ => btn.BackgroundColor = Color.Transparent);
        btn.OnClick(_ => onClick());
        return btn;
    }
}
