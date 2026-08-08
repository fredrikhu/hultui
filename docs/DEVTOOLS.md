# DevTools

HultUI ships two debugging surfaces. The **performance HUD** draws over your app (F12). The **element inspector** opens in a window of its own, so it never takes space from — or z-order in — the application it is inspecting.

---

## Quick Start

Run any HultUI app and press:

| Chord | What it does |
|-------|--------------|
| `Ctrl+Shift+I` | Open / close the inspector |
| `Ctrl+Shift+C` | Open the inspector with the picker armed |
| `F12` | Cycle the performance HUD: on → on + continuous redraw → off |

With the picker armed, moving the pointer over the app outlines the element under it, and clicking selects it. The click never reaches the app — clicking a button selects it rather than pressing it.

---

## The inspector window

```
┌──────────────────────────────────────────────────────────────┐
│ ⊹ Pick │ filter…            │ Expand │ Collapse │ Copy       │
├──────────────────────────────────────────────────────────────┤
│ Box › Flex › ScrollView › Button                             │  ← ancestors, clickable
├───────────────────────────────┬──────────────────────────────┤
│ ▾ Box #app-root      900×620  │ Element                      │
│   ▾ Flex             900×48   │   Type        ButtonElement  │
│     ▸ Text "Title"   120×20   │   Name        save           │
│     · Button "Save"   72×28   │ Layout                       │
│   ▸ ScrollView       900×540  │   Position    24, 12         │
│                               │   Size        72 × 28        │
│                               │ Flex …                       │
├───────────────────────────────┴──────────────────────────────┤
│ 214 nodes shown · ↑↓ move · ←→ collapse/expand · Esc clears  │
└──────────────────────────────────────────────────────────────┘
```

**Tree pane.** The live element tree, virtualized, so a 10,000-element app scrolls without cost. Click a row to select it; click the twisty to fold a subtree. Rows show the type, an identifying descriptor (`#name`, `#automation-id`, or the element's text), and the laid-out size.

**Box model.** At the top of the property pane, the nested-bands diagram every browser inspector draws — margin outside border outside padding outside content, each band labelled with its four resolved edges and the content box giving its measured size. The numeric rows say the same thing, but only the diagram answers *which box is eating my space* without being read.

The bands keep the conventional tan / khaki / olive / blue rather than following the app theme. They are a legend as much as a drawing — the same four colours in every devtools anyone has used — so re-tinting them per theme would cost that recognition for nothing.

**Property pane.** Everything the selected element resolves to, grouped: Element, Layout, Box, Paint, Flex, Position, Grid item, Text, State, Accessibility. Rows left at their framework default are omitted, so the pane reads as *what this element does* rather than a wall of `auto`. **Layout is the exception** — measured geometry is always shown, because "it laid out to zero" is the answer to most questions that bring anyone to an inspector.

**Filter.** Type in the toolbar box to match type names and descriptors. Matches appear with the ancestors needed to reach them, regardless of what is expanded.

### Keyboard

| Key | Action |
|-----|--------|
| `↑` / `↓` | Move through the visible tree in document order |
| `→` | Expand a collapsed node, else step into its first child |
| `←` | Collapse an expanded node, else step out to its parent |
| `PageUp` / `PageDown` | Move ten rows |
| `Ctrl+C` | Copy the selection's properties to the clipboard as text |
| `Esc` | Cancel picking, or clear the selection |

Typing in the filter box keeps plain keys and caret movement; navigation applies when the tree is what you are driving.

---

## What the highlight shows

The inspected window draws a box-model overlay over whatever is selected — content area, then padding in green, then margin in orange — with a label giving the element's summary and size. A **live pick preview** uses a green tint, the **committed selection** a blue one, so the two are never confused mid-pick.

The highlight is independent of the F12 HUD: inspecting an element does not drag the FPS readout along with it.

---

## Programmatic access

```csharp
using var app = new Application("My App", 900, 600);
app.SetRoot(BuildUi());

// Open it yourself — e.g. from a menu item, or a chord of your own.
app.OpenInspector();                       // primary window
app.OpenInspector(startPicking: true);     // armed
app.OpenInspector(someOtherWindow);        // any window the app owns

app.CloseInspector();
bool open = app.IsInspectorOpen;
```

If your app already binds `Ctrl+Shift+I` or `Ctrl+Shift+C`, turn the built-in chords off and route the inspector from wherever suits you:

```csharp
app.DevToolsHotkeysEnabled = false;
```

---

## Overlays are inspectable

Context menus, tooltips, and dialogs are separate element trees, not children of the window root. The inspector includes them among its roots — without that, the one element you opened the inspector to look at is often exactly the one you cannot reach.

---

## Notes and limits

- **Only one panel per application.** Opening the inspector against a different window re-targets the existing panel rather than opening a second one.
- **The inspector cannot inspect itself.** Its window is flagged as a DevTools window: it is excluded from picking, and it does not keep `Application.Run()` alive once your own windows are gone.
- **The tree is re-read on a ~100 ms timer**, not every frame. An app mid-animation renders 60 times a second, and rebuilding the panel that often would make the inspector the most expensive thing on screen. Selection changes bypass the timer, so anything you do feels immediate.
- **A removed element stays selected**, flagged as stale. Its last known property values still read fine; the panel says so rather than implying it is live.
- **Describing an element is a pure read.** It allocates no style objects on the element and marks nothing dirty — a debugger that perturbs the tree it describes is worse than no debugger.
- **The panel follows the application theme**, but reads only three tokens from it: `Background`, `TextColor`, and `Primary`. Surfaces, borders, hover tints, and muted text are blended between them. `Theme` is a record whose every token defaults to a light value, and apps typically override only the few they care about — a dark theme that never assigns `SurfaceVariant` or `Border` still reports slate-100 and slate-200 for them. Deriving instead of reading means the panel stays internally consistent on a partially specified theme, which is the common case.

---

## Performance HUD (F12)

Separate from the inspector, and unchanged:

- First press: bounding boxes on every element, hover inspection, and the counter panel (FPS, per-phase timings with rolling peaks, element count).
- Second press: adds continuous redraw, so the FPS number reflects raw render throughput instead of sitting idle between changes.
- Third press: off.

Per-phase timings (Style, Layout, Render, Flush, Present, Overlay, Other) come from the same `PerformanceCounters` the tracer records; `F11` toggles a full trace to disk.

---

## Key Imports

| Namespace | What it provides |
|-----------|-----------------|
| `HultUI` | `Application.OpenInspector`, `Window.DevTools` |
| `HultUI.Core.DevTools` | `ElementInspector`, `ElementProperties`, `DevToolsOverlay`, `PerformanceCounters` |
