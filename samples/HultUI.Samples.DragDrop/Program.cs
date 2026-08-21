using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Layout;
using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.Core.Reactivity;
using HultUI.Core.Styling;

// Explicit, and STA: an outbound file drag is an OLE operation, and OLE refuses to
// initialise on the MTA apartment .NET gives a main thread by default. The attribute is
// inert on Linux and macOS. Without it Window.CanDragFilesOut reports false and the
// "drag out" half of this demo disables itself.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var app = new Application("Drag & Drop Demo", 650, 700);
        app.Theme = Theme.Dark;
        app.SetRoot(new DragDropDemo(app.PrimaryWindow).Mount());
        app.Run();
    }
}

// ── Demo component ──

partial class DragDropDemo : Component
{
    [Reactive] public partial string DropStatus { get; set; }
    [Reactive] public partial int DragCount { get; set; }

    private readonly ReactiveList<string> _leftItems = new(["Apple", "Banana", "Cherry", "Date", "Elderberry"]);
    private readonly ReactiveList<string> _rightItems = new(["Fig", "Grape"]);
    private readonly ReactiveList<string> _droppedFiles = new();

    /// <summary>Files this demo offers to the rest of the desktop. Written to temp on first
    /// use so the drag has something real to hand over.</summary>
    private string[]? _exportable;

    private readonly Window _window;

    public DragDropDemo(Window window)
    {
        _window = window;
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
                Section("OS File Drop — Hold Shift While Dropping",
                    BuildFileDropZone()
                ),

                // ── Drag out to another application ──
                Section("OS File Drag — Out to Another Application",
                    BuildDragOutZone()
                )
            )
        );
    }

    /// <summary>The outbound half: press and drag this to Explorer, an editor, or a mail
    /// composer. The gesture is started from the DragStart handler — the moment the framework
    /// has decided a drag is under way and the button is still down, which is what the OS
    /// requires — and it runs modally until the drop lands or is cancelled.</summary>
    private Element BuildDragOutZone()
    {
        bool available = _window.CanDragFilesOut;
        var ground = available ? new Color(40, 40, 62) : new Color(26, 26, 32);
        var edge = available ? new Color(99, 102, 241) : new Color(60, 60, 75);
        var ink = available ? new Color(230, 230, 245) : new Color(120, 120, 140);

        var zone = Box.New()
            .MinHeight(72)
            .Padding(new Thickness(16f))
            .CornerRadius(8f)
            .Background(ground);
        zone.Box.BorderWidth = new Thickness(2f);
        zone.BorderColor = edge;
        zone.ReplaceChildren(Flex.Column(gap: 4).Children(
            Text.New(available ? "Drag this out to another application" : "Not available on this platform")
                .FontSize(14f).FontWeight(FontWeight.SemiBold).TextColor(ink),
            Text.New(available
                    ? "Two temp files are offered as a copy. Windows only, and only from an STA entry point."
                    : "Window.CanDragFilesOut is false: no drag source here, so the gesture stays unarmed.")
                .FontSize(11.5f).TextColor(new Color(140, 140, 165))));

        if (!available) return zone;

        zone.Inherited.Cursor = CursorType.Move;
        zone.Draggable();
        zone.On<DragStartEvent>(_ =>
        {
            // Copy and Link only. Move would oblige this demo to delete the originals on the
            // strength of a value the target returned — see DRAG-AND-DROP.md.
            var performed = _window.DragFilesOut(ExportableFiles(), DragEffect.Copy | DragEffect.Link);
            DropStatus = performed == DragEffect.None
                ? "The drag out was cancelled."
                : $"The target took the files as {performed}.";
            DragCount++;
        });
        return zone;
    }

    private string[] ExportableFiles()
    {
        if (_exportable is not null) return _exportable;
        var dir = Path.Combine(Path.GetTempPath(), "hultui-dragdrop-sample");
        Directory.CreateDirectory(dir);
        var files = new[] { Path.Combine(dir, "hello.txt"), Path.Combine(dir, "notes.md") };
        File.WriteAllText(files[0], "Dragged out of the HultUI drag & drop sample.\n");
        File.WriteAllText(files[1], "# Notes\n\nThis file was handed over by Window.DragFilesOut.\n");
        return _exportable = files;
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
                    sourceList?.Remove(item!);
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
        box.Key = $"drag-{item}";
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
            // Accept any external drag (files or text). The modifiers are the ones held right
            // now, not the ones held when the drag began — Shift is pressed part-way through.
            e.Accepted = true;
            var wanted = e.Modifiers.HasFlag(KeyModifiers.Shift)
                ? new Color(244, 114, 182)      // "move" — a different colour, so the key shows
                : new Color(99, 102, 241);
            if (zone.BorderColor != wanted) { zone.BorderColor = wanted; DragCount++; }
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
            var verb = e.Modifiers.HasFlag(KeyModifiers.Shift) ? "Moved" : "Copied";
            if (e.DragData.HasFiles)
            {
                foreach (var file in e.DragData.Files)
                    _droppedFiles.Add($"[{verb.ToLowerInvariant()}] {file}");
                DropStatus = $"{verb} {e.DragData.Files.Count} file(s) — Shift decides which";
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
