# Drag and Drop

HultUI supports **internal drag** (moving elements within the app), **OS-level drops** (files and text dragged in from the desktop), and **OS-level drags out** (handing files to another application). All three use the same event types and `DragData` payload.

---

## Quick Example -- Internal Drag

```csharp
using HultUI.Core.Builders;
using HultUI.Core.Events;

// Source: a draggable item
var item = Box.New()
    .Draggable()
    .OnDragStart(e => e.DragData.Set("fruit", "Apple"))
    .Children(Text.New("Apple"));

// Target: accepts drops
var dropZone = Box.New()
    .OnDragOver(e => e.Accepted = true)
    .OnDrop(e =>
    {
        if (e.DragData.TryGet<string>("fruit", out var name))
            Console.WriteLine($"Dropped: {name}");
    });
```

---

## Making Elements Draggable

Mark an element as draggable with the `.Draggable()` builder:

```csharp
var box = Box.New().Draggable();
```

When the user presses and moves the mouse more than 5 pixels from the press point, a drag begins. A `DragStartEvent` fires on the source element where you attach data to the drag payload.

---

## Drag Events

| Event | When it fires | Key properties |
|-------|---------------|----------------|
| `DragStartEvent` | Drag begins (source element) | `DragData`, `DragVisual`, `Modifiers` |
| `DragOverEvent` | Drag hovers over an element (bubbles) | `DragData`, `Accepted`, `Modifiers` |
| `DragEnterEvent` | Drag enters an element's bounds (bubbles) | `DragData`, `Modifiers` |
| `DragLeaveEvent` | Drag leaves an element's bounds (bubbles) | `DragData`, `Modifiers` |
| `DropEvent` | Data dropped on an accepting element (bubbles) | `DragData`, `Modifiers` |
| `DragEndEvent` | Drag ends (source element) | `DragData`, `Modifiers`, `WasAccepted` |

### Modifiers

Every drag event carries the keyboard modifiers held **at that moment** — not the ones held when the press landed. Shift-to-move is decided part-way through a drag, after you have aimed, so a value captured at the start would be the wrong one:

```csharp
target.OnDragOver(e =>
{
    e.Accepted = e.DragData.HasFiles;
    _hint = e.Modifiers.HasFlag(KeyModifiers.Shift) ? "Move here" : "Copy here";
});

target.OnDrop(e => Transfer(e.DragData.Files, move: e.Modifiers.HasFlag(KeyModifiers.Shift)));
```

During an **OS** drag the source application owns the input stream and no key event reaches you, so the modifiers are sampled from the live keyboard state instead and arrive attached to the drag events themselves. Reading them off `KeyDownEvent` would never see them.

### Accepting a Drop

A drop target must set `Accepted = true` in its `DragOverEvent` handler. Without this, `DropEvent` will not fire on that element:

```csharp
target.On<DragOverEvent>(e =>
{
    if (e.DragData.Has("item"))
        e.Accepted = true;
});
```

---

## DragData

`DragData` is a type-safe keyed container carried by all drag events:

```csharp
// Set data (in DragStartEvent handler)
e.DragData.Set("item-id", 42);
e.DragData.Set("source-list", myList);

// Read data (in DropEvent handler)
if (e.DragData.TryGet<int>("item-id", out var id)) { /* use id */ }
if (e.DragData.Has("item-id")) { /* check existence */ }
```

For OS-level drops, `DragData` also exposes:

```csharp
e.DragData.HasFiles   // true if files were dropped
e.DragData.Files      // List<string> of file paths
e.DragData.HasText    // true if text was dropped
e.DragData.Text       // the dropped text string
```

---

## OS File and Text Drops

Elements can receive files and text dragged from the desktop or file manager. No special setup is needed on the element -- just handle `DragOverEvent` and `DropEvent`:

```csharp
var dropZone = Box.New()
    .MinHeight(120).Padding(16)
    .OnDragOver(e => e.Accepted = true)
    .OnDrop(e =>
    {
        if (e.DragData.HasFiles)
        {
            foreach (var path in e.DragData.Files)
                Console.WriteLine($"File: {path}");
        }
        else if (e.DragData.HasText)
        {
            Console.WriteLine($"Text: {e.DragData.Text}");
        }
    });
```

---

## Dragging Files Out to Other Applications

`Window.DragFilesOut` hands a list of paths to the rest of the desktop as a file drag, so Explorer, an editor, or a mail composer can accept the drop:

```csharp
row.Draggable();
row.OnDragStart(_ =>
{
    var performed = window.DragFilesOut(SelectedPaths(), DragEffect.Copy | DragEffect.Link);
    if (performed == DragEffect.Move) DeleteSources();   // see the warning below
});
```

Call it from a `DragStart` handler. That is the moment the framework has decided a drag is under way and the mouse button is still down, which is what the OS requires. Doing so **replaces** the in-window drag rather than running alongside it — no ghost is drawn, and no `DragOver`/`Drop`/`DragEnd` fires — because the OS owns the pointer for the whole gesture.

**The call is modal.** Every OS implements an outbound drag as a nested loop that owns the mouse until the button comes up, so `DragFilesOut` does not return until the drop lands or is cancelled, and no frame renders meanwhile.

**Passing `DragEffect.Move` is a promise.** The target will not delete your files; returning `Move` means it expects *you* to. An app that does not want to make that promise on the strength of a value the target reported should offer `Copy` (and `Link`) only, and keep move on an explicit command.

### Availability

`Window.CanDragFilesOut` reports whether the platform can start one. Check it before arming the gesture rather than offering a drag that goes nowhere:

```csharp
if (window.CanDragFilesOut) row.Draggable();
```

It is false on Linux and macOS (SDL exposes no portable drag source), false headlessly, and — on Windows — false unless the process's entry point is marked `[STAThread]`. An outbound drag is an OLE operation, and OLE refuses to initialise on the MTA apartment .NET gives a main thread by default:

```csharp
internal static class Program
{
    [STAThread]                     // inert on Linux/macOS; required on Windows
    private static void Main(string[] args) { /* ... */ }
}
```

Top-level statements produce an entry point with no attribute, so an app that wants this needs an explicit `Main`.

---

## Visual Feedback

Use `DragEnterEvent` and `DragLeaveEvent` to highlight drop targets:

```csharp
var zone = Box.New().Background(new Color(30, 30, 40));

zone.On<DragEnterEvent>(_ => zone.BackgroundColor = new Color(45, 45, 65));
zone.On<DragLeaveEvent>(_ => zone.BackgroundColor = new Color(30, 30, 40));
zone.On<DropEvent>(_ => zone.BackgroundColor = new Color(30, 30, 40));
```

These events bubble, so a container's handlers fire even when the cursor is over a child element inside it.

A highlight handler that calls `MarkDirty()` is fine: rebuilding mid-drag replaces the element the hover landed on, and the framework re-resolves the target by hit-testing again — the drop still reaches the live tree rather than the orphan the rebuild left behind.

---

## Custom Drag Visual

By default, the source element is re-rendered as a semi-transparent ghost following the cursor. To provide a custom visual, set `DragVisual` in the `DragStartEvent` handler:

```csharp
box.On<DragStartEvent>(e =>
{
    e.DragData.Set("item", item);
    e.DragVisual = Box.New()
        .Padding(8).Background(new Color(99, 102, 241)).CornerRadius(6f)
        .Children(Text.New(item.Name).FontSize(12f));
});
```

The custom visual is laid out once and rendered at the cursor position during the drag.

---

## Keys and Dynamic Lists

When rendering a list of draggable items in a component's `Build()` method, set a unique `Key` on each element based on the item's identity. Without keys, the tree differ patches elements by position, so inserts and reorders bleed retained state (an in-progress drag, hover, focus) across items. Handlers themselves always follow the latest build — see "Event handlers follow the latest build" in REACTIVITY.md.

```csharp
for (int i = 0; i < items.Count; i++)
{
    var item = items[i];
    var box = Box.New().Draggable()
        .Children(Text.New(item.Name));
    box.Key = $"drag-{item.Id}";  // match by identity, not position
    box.On<DragStartEvent>(e => e.DragData.Set("item", item));
    children[i] = box;
}
```

See [Reactivity Guide -- Event handlers follow the latest build](REACTIVITY.md#event-handlers-follow-the-latest-build) for a detailed explanation.

---

## Fluent Builders

| Builder | Description |
|---------|-------------|
| `.Draggable()` | Mark element as a drag source |
| `.OnDragStart(handler)` | Handle drag start |
| `.OnDragOver(handler)` | Handle drag over (set `Accepted = true` to allow drop) |
| `.OnDrop(handler)` | Handle drop |
| `.OnDragEnter(handler)` | Handle drag enter |
| `.OnDragLeave(handler)` | Handle drag leave |
| `.OnDragEnd(handler)` | Handle drag end (on source) |

| Window member | Description |
|---------------|-------------|
| `Window.DragFilesOut(files, allowed)` | Start an OS-wide file drag; blocks, returns what the target did |
| `Window.CanDragFilesOut` | Whether this platform/process can start one |

---

## Key Imports

| Namespace | What it provides |
|-----------|-----------------|
| `HultUI.Core.Events` | `DragStartEvent`, `DragOverEvent`, `DropEvent`, `DragEnterEvent`, `DragLeaveEvent`, `DragEndEvent`, `DragData` |
| `HultUI.Core.Builders` | `.Draggable()`, `.OnDragStart()`, `.OnDrop()`, etc. |
| `HultUI.Core.Platform` | `DragEffect` (`Copy`, `Move`, `Link`), `INativeDragSource` |
