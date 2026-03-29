using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

using var app = new Application("Drag & Drop Demo", 650, 600);
app.Theme = Theme.Dark;
app.SetRoot(new DragDropDemo().Mount());
app.Run();

// ── Demo component ──

partial class DragDropDemo : Component
{
    [Reactive] public partial string DropStatus { get; set; }
    [Reactive] public partial int DragCount { get; set; }

    private readonly ReactiveList<string> _leftItems = new(["Apple", "Banana", "Cherry", "Date", "Elderberry"]);
    private readonly ReactiveList<string> _rightItems = new(["Fig", "Grape"]);
    private readonly ReactiveList<string> _droppedFiles = new();

    public DragDropDemo()
    {
        DropStatus = "Drag items between lists, or drop files from your desktop.";
    }

    public override Element Build()
    {
        _ = DragCount; // track rebuilds

        return ScrollView.New().Children(
            Flex.Column(gap: 24).Padding(24).FlexShrink(0).Children(

                // Header
                Text.New("Drag & Drop").FontSize(22f).FontWeight(FontWeight.Bold),
                Text.New(DropStatus).FontSize(12f).TextColor(new Color(156, 163, 175)),

                // ── Internal drag: two lists ──
                Section("Internal Drag — Move Items Between Lists",
                    Flex.Row(gap: 24).Children(
                        BuildItemList("Left", _leftItems, _rightItems),
                        BuildItemList("Right", _rightItems, _leftItems)
                    )
                ),

                // ── File drop zone ──
                Section("OS File Drop",
                    BuildFileDropZone()
                )
            )
        );
    }

    private Element BuildItemList(string title, ReactiveList<string> items, ReactiveList<string> otherList)
    {
        var children = new Element[items.Count + 1];
        children[0] = Text.New(title).FontSize(14f).FontWeight(FontWeight.SemiBold)
            .Padding(new Thickness(8f));

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            children[i + 1] = BuildDraggableItem(item, items);
        }

        var container = Flex.Column(gap: 4)
            .Width(220).MinHeight(200)
            .Padding(new Thickness(8f))
            .Children(children);

        // Style as a drop zone
        var wrapper = Box.New()
            .CornerRadius(8f)
            .Background(new Color(30, 30, 40));
        wrapper.ReplaceChildren(container);

        // Accept drops from the other list
        wrapper.On<DragOverEvent>(e =>
        {
            if (e.DragData.Has("item"))
                e.Accepted = true;
        });

        wrapper.On<DragEnterEvent>(_ =>
        {
            wrapper.BackgroundColor = new Color(45, 45, 65);
        });

        wrapper.On<DragLeaveEvent>(_ =>
        {
            wrapper.BackgroundColor = new Color(30, 30, 40);
        });

        wrapper.On<DropEvent>(e =>
        {
            if (e.DragData.TryGet<string>("item", out var item) &&
                e.DragData.TryGet<ReactiveList<string>>("source-list", out var sourceList))
            {
                if (sourceList != items) // don't drop onto own list
                {
                    sourceList.Remove(item!);
                    items.Add(item!);
                    DropStatus = $"Moved \"{item}\" to {title}";
                    DragCount++;
                }
            }
            wrapper.BackgroundColor = new Color(30, 30, 40);
        });

        return wrapper;
    }

    private static Element BuildDraggableItem(string item, ReactiveList<string> sourceList)
    {
        var box = Box.New()
            .Padding(new Thickness(10f, 14f, 10f, 14f))
            .Background(new Color(99, 102, 241))
            .CornerRadius(6f)
            .Draggable()
            .Children(
                Text.New(item).FontSize(13f).FontWeight(FontWeight.SemiBold)
            );
        box.Inherited.Cursor = CursorType.Move;

        box.On<DragStartEvent>(e =>
        {
            e.DragData.Set("item", item);
            e.DragData.Set("source-list", sourceList);
        });

        return box;
    }

    private Element BuildFileDropZone()
    {
        var fileList = new Element[_droppedFiles.Count];
        for (int i = 0; i < _droppedFiles.Count; i++)
            fileList[i] = Text.New(_droppedFiles[i]).FontSize(12f).Padding(new Thickness(2f, 4f, 2f, 4f));

        var content = Flex.Column(gap: 4).Children(
            _droppedFiles.Count == 0
                ? new Element[]
                {
                    Text.New("Drop files here from your desktop").FontSize(14f)
                        .TextColor(new Color(120, 120, 140))
                }
                : fileList
        );

        var zone = Box.New()
            .MinHeight(120)
            .Padding(new Thickness(16f))
            .CornerRadius(8f)
            .Background(new Color(30, 30, 40));
        zone.Box.BorderWidth = new Thickness(2f);
        zone.BorderColor = new Color(70, 70, 90);
        zone.ReplaceChildren(content);

        zone.On<DragOverEvent>(e =>
        {
            // Accept any external drag (files or text)
            e.Accepted = true;
        });

        zone.On<DragEnterEvent>(_ =>
        {
            zone.BorderColor = new Color(99, 102, 241);
            zone.BackgroundColor = new Color(40, 40, 60);
        });

        zone.On<DragLeaveEvent>(_ =>
        {
            zone.BorderColor = new Color(70, 70, 90);
            zone.BackgroundColor = new Color(30, 30, 40);
        });

        zone.On<DropEvent>(e =>
        {
            if (e.DragData.HasFiles)
            {
                foreach (var file in e.DragData.Files)
                    _droppedFiles.Add(file);
                DropStatus = $"Dropped {e.DragData.Files.Count} file(s)";
            }
            else if (e.DragData.HasText)
            {
                _droppedFiles.Add($"[text] {e.DragData.Text}");
                DropStatus = "Dropped text";
            }
            DragCount++;
            zone.BorderColor = new Color(70, 70, 90);
            zone.BackgroundColor = new Color(30, 30, 40);
        });

        return zone;
    }

    private static Element Section(string title, params Element[] content)
    {
        var children = new Element[content.Length + 1];
        children[0] = Text.New(title).FontSize(15f).FontWeight(FontWeight.SemiBold);
        for (int i = 0; i < content.Length; i++)
            children[i + 1] = content[i];
        return Flex.Column(gap: 8).FlexShrink(0).Children(children);
    }
}
