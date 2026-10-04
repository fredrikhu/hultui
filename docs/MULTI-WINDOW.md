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

### Placement

`Window.Placement` is where the window is and whether it is maximized: a `WindowPlacement` record to save as the app closes and give back as it starts, so the window opens where the person left it. Store it with the app's settings (it serializes through a source-generated `JsonSerializerContext` like any other property):

```csharp
window.Placement = settings.WindowPlacement;          // before app.Run(); null does nothing
window.Closed += () =>
{
    settings.WindowPlacement = window.Placement;      // the native window still exists here
    settings.Save();
};
```

- **Set it before `Run`.** The window is hidden until its first frame, so it opens in place instead of appearing at its default size and jumping. A maximized placement is maximized before that frame — on Windows the window is shown cloaked, drawn at its maximized size, then revealed — so the first thing on screen is the finished window.
- **The bounds are the restored ones.** A maximized, minimized or full-screen window reports the rectangle it goes back to, so un-maximizing after a restart lands where it did before. A window Aero Snap arranged reports where it is, since that is where it was last seen.
- **The numbers belong to the platform.** Windows uses the outer window rectangle in physical pixels; SDL elsewhere uses its window coordinates. Wayland takes the size and the maximized state but never a position — a client does not place its own windows there. Save and restore; do not compute with them.
- **A changed desktop is handled.** Bounds are fitted to the monitors there are now (`WindowPlacement.FitTo`): a window whose monitor is gone is centred on the nearest one, one hanging off an edge slides back, one larger than its monitor shrinks to it. A window counts as reachable while 100 px of its top edge, with the 32 px caption band below it, is on some monitor — a custom title bar out of reach cannot be dragged back.
- **Headless and hosted windows.** Under a `HeadlessSession` the placement is recorded and read back, not applied, so an app's save-and-restore wiring is testable without a desktop; the maximized flag is applied. A WinForms-hosted control returns null: its form owns its placement.

### Single instance

`SingleInstance` keeps one running copy of an app per person: a later launch hands its command line to the copy already running and exits. Claim before building anything, listen once the app exists:

```csharp
using var instance = SingleInstance.Claim("Reel");
if (!instance.IsFirst && instance.Forward(args)) return;   // handed over

using var app = new Application("Reel", 1280, 760);
// ...
instance.Listen(launch =>                                  // runs on the UI thread
{
    if (launch.Args.Count > 0) player.Open(launch.FullPath(launch.Args[0]));
    window.Raise();
});
app.Run();
```

- **Nothing is dropped.** The first copy listens from `Claim`, before it has a window, and keeps launches until `Listen`; a file manager opening ten selected files starts ten processes at once, nine of them while the first is still starting. `Forward` returns true only once the running copy has the message; false (it did not answer within 3 s) means carry on as a window of its own.
- **Paths are the launch's.** The working directory travels with the arguments; `launch.FullPath(arg)` resolves against it, not the running copy's.
- **`Raise` works on Windows.** A background process may not take the foreground; the forwarding launch, which the person just started, grants its right to the running copy before sending. Wayland has no equivalent.
- **Per person, and per session on Windows.** The claim is a locked file in the user's temp folder (`$XDG_RUNTIME_DIR` on Linux) and the channel a named pipe only the same user can open from either end. The claim goes when the process ends, however it ends.
- **Give each app its own id.** Tests should claim a fresh one (a GUID): a fixed id would hand their launches to the real app if it is running.

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
