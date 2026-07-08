# Custom Window Chrome

HultUI can create **borderless windows** where your application provides its own title bar, window controls, and drag/resize behavior. The OS title bar is removed entirely and you build the chrome with regular HultUI elements.

---

## Quick Example

```csharp
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Core.Styling;

using var app = new Application("My App", 800, 600, WindowStyle.Borderless);
app.Theme = Theme.Dark;
var window = app.PrimaryWindow;

app.SetRoot(Flex.Column().Children(
    // Custom title bar -- draggable
    Flex.Row(gap: 8).WindowDrag()
        .AlignItems(AlignItems.Center)
        .Height(Dimension.Px(40))
        .Padding(new Thickness(4, 12, 4, 12))
        .Background(new Color(24, 24, 36))
        .Children(
            Text.New("My App").FlexGrow(1),
            CloseButton(window)
        ),
    // App content
    ScrollView.New().FlexGrow(1).Children(
        Text.New("Hello from a borderless window!").Padding(24)
    )
));

app.Run();

static Element CloseButton(Window window) =>
    Box.New().Padding(new Thickness(4, 12, 4, 12))
        .CornerRadius(4f).Cursor(CursorType.Pointer)
        .OnClick(_ => window.Close())
        .Children(Text.New("\u00D7").FontSize(14f));
```

---

## Creating a Borderless Window

Pass `WindowStyle.Borderless` when creating the application or a new window:

```csharp
// Primary window
using var app = new Application("Title", 800, 600, WindowStyle.Borderless);

// Additional windows
var settings = app.CreateWindow("Settings", 400, 300, WindowStyle.Borderless);
```

The window opens with no OS title bar or borders. You are responsible for providing window drag, resize, and close functionality.

---

## Window Drag Regions

Mark any element as a title bar drag region with `.WindowDrag()`:

```csharp
var titleBar = Flex.Row().WindowDrag()
    .Height(Dimension.Px(40))
    .Background(new Color(24, 24, 36))
    .Children(Text.New("My App"));
```

When the user clicks and drags on this element (or any child without event handlers), the OS moves the window. The `.WindowDrag()` marker applies to the element and all its descendants, so you can build a full title bar layout and it all becomes draggable.

### Buttons Inside the Drag Region

Interactive elements (those with event handlers like `OnClick`) inside a drag region remain clickable. The framework detects that the clicked element or a closer ancestor has event handlers and lets the click through to your handler instead of starting a window drag.

```csharp
Flex.Row().WindowDrag().Children(
    Text.New("My App").FlexGrow(1),
    // These buttons work normally despite being inside a drag region:
    Box.New().OnClick(_ => window.Minimize()).Children(Text.New("\u2212")),
    Box.New().OnClick(_ => window.ToggleMaximize()).Children(Text.New("\u25FB")),
    Box.New().OnClick(_ => window.Close()).Children(Text.New("\u00D7"))
)
```

---

## Edge Resize

Borderless windows automatically support edge and corner resizing. An invisible border (default 6 density-independent units) around the window edges lets the user grab and resize. This works without any element setup.

Adjust the border thickness:

```csharp
window.ResizeBorderWidth = 8;  // default is 6
```

### Which edges resize

By default a window resizes from **all four edges**. Control this per window with `ResizableEdges` -- for example, drop the top edge when a custom title bar sits flush against it:

```csharp
using HultUI.Core.Styling;

window.ResizableEdges = ResizeEdges.SidesAndBottom;           // no top-edge resize
window.ResizableEdges = ResizeEdges.Left | ResizeEdges.Right; // sides only
window.ResizableEdges = ResizeEdges.None;                     // fixed size
```

A corner is grabbable only when **both** of its adjacent edges are enabled (so `ResizeEdges.SidesAndBottom` gives the two bottom corners but not the top ones). Corners use a deeper grab zone than the flat edges, since they are the most common and hardest-to-hit resize target.

Resize handles are disabled while the window is maximized.

### Native frame on Windows

On Windows a borderless window keeps a real native frame with the title bar hidden, so it gets the **DWM drop shadow, the Windows 11 rounded corners and accent border, Aero Snap, and snap layouts** for free, and resizing/moving is handled natively by the OS. Because the native side and bottom resize borders sit a few pixels *outside* the visible content (as with any native window), you can grab a resize just past the window edge rather than having to land exactly on it. On other platforms the same behavior is provided through SDL's hit-testing. The public API is identical either way.

---

## Window Management

The `Window` class exposes methods for the standard window actions:

```csharp
window.Minimize();         // minimize to taskbar
window.Maximize();         // maximize to fill screen
window.Restore();          // restore from maximized/minimized
window.ToggleMaximize();   // maximize if normal, restore if maximized
window.Close();            // close the window

bool max = window.IsMaximized;  // query current state
```

Use these to wire up your custom window control buttons.

---

## Window Style Query

Check the window's style at runtime:

```csharp
if (window.Style == WindowStyle.Borderless)
{
    // show custom title bar
}
```

---

## Key Imports

| Namespace | What it provides |
|-----------|-----------------|
| `HultUI.Core.Styling` | `WindowStyle`, `ResizeEdges` enums |
| `HultUI.Core.Builders` | `.WindowDrag()` fluent builder |
| `HultUI` | `Window.Minimize()`, `Maximize()`, `Restore()`, `Close()`, etc. |
