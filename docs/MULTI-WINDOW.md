# Multi-Window Support

HultUI applications can open multiple windows from a single event loop. All windows share the application's theme and animation system, but have independent element trees, layout, and rendering.

---

## Quick Example

```csharp
using HultUI;
using HultUI.Core.Builders;
using HultUI.Core.Primitives;

using var app = new Application("Main Window", 800, 600);
app.Theme = Theme.Dark;
app.SetRoot(Text.New("Primary window").Padding(24));

// Open a second window
var secondary = app.CreateWindow("Inspector", 400, 300);
secondary.SetRoot(Text.New("Secondary window").Padding(24));

secondary.Closed += () => Console.WriteLine("Inspector closed");

app.Run();  // runs until all windows are closed
```

---

## Creating Windows

The `Application` constructor creates the primary window. Use `CreateWindow` for additional windows:

```csharp
// Primary window (created automatically)
using var app = new Application("My App", 800, 600);

// Additional windows
var settings = app.CreateWindow("Settings", 400, 300);
var preview = app.CreateWindow("Preview", 600, 400);
```

Each window gets its own element tree via `SetRoot`:

```csharp
app.SetRoot(new MainView().Mount());           // primary window
settings.SetRoot(new SettingsView().Mount());   // settings window
preview.SetRoot(new PreviewView().Mount());     // preview window
```

---

## Window Lifecycle

### Closing

Windows can be closed by the user (clicking the OS close button) or programmatically:

```csharp
window.Close();
```

Listen for the close event to clean up state:

```csharp
window.Closed += () =>
{
    // window is closing -- update your state
    isWindowOpen = false;
};
```

### Event Loop

`app.Run()` drives all windows from a single event loop. It continues until every window is closed or a quit event is received. Closed windows are automatically disposed and removed from the window list.

### Checking State

```csharp
window.IsOpen       // true if window is open and not disposed
window.IsMaximized  // true if maximized (useful for custom chrome)
```

### Title

The title passed at creation is not fixed — assigning `Window.Title` re-titles the window the OS is already showing, so it can track whatever the window currently has open:

```csharp
window.Title = $"{Path.GetFileName(folder)} — Vesper Commander";
```

A borderless window has one too. No caption paints it, but the task bar, Alt+Tab and every window switcher read it — so an app with custom chrome should still keep it current, and draw its own caption text from the same string. Assigning the value it already has is a no-op all the way down to the platform, which makes it safe to set once per build rather than tracking a separate invalidation path.

---

## Shared State Between Windows

Use reactive signals to share data across windows. When a signal changes, any component that reads it -- in any window -- rebuilds automatically:

```csharp
var selectedColor = new Signal<Color>(Color.White);

// Main window reads the signal
app.SetRoot(new MainView(selectedColor).Mount());

// Picker window writes to the same signal
var picker = app.CreateWindow("Color Picker", 280, 300);
picker.SetRoot(new ColorPicker(selectedColor).Mount());

// When ColorPicker updates selectedColor.Value,
// MainView rebuilds automatically
```

---

## Shared Theme

All windows use the application's theme. Changing the theme updates every window:

```csharp
app.Theme = Theme.Dark;  // all windows re-render with dark theme
```

There is no per-window theme override -- the theme is application-wide.

---

## Per-Window Resources

Each window has its own:

| Resource | Access |
|----------|--------|
| Clipboard | `window.Clipboard` |
| Performance counters | `window.Performance` |
| DevTools overlay | `window.DevTools` (F12 per window) |
| Element tree | `window.SetRoot(element)` |

The DevTools inspector is application-wide rather than per-window: one panel, re-targeted with `app.OpenInspector(window)`. Its own window is flagged `IsDevToolsWindow`, so it is excluded from inspection and does not keep `Run()` alive after your windows close. See [DEVTOOLS.md](DEVTOOLS.md).

---

## Window List

Access all windows via the application:

```csharp
app.PrimaryWindow           // the first window
app.Windows                 // IReadOnlyList<Window> of all windows
```

---

## Key Imports

| Namespace | What it provides |
|-----------|-----------------|
| `HultUI` | `Application`, `Window` |
| `HultUI.Core.Reactivity` | `Signal<T>`, `Computed<T>` for cross-window state |
