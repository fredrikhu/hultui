# DevTools

HultUI ships two debugging surfaces. The **performance HUD** draws over your app (F12). The **element inspector** opens in a window of its own, so it never takes space from — or z-order in — the application it is inspecting.

---

## Quick Start

Run any HultUI app and press:

| Chord | What it does |
|-------|--------------|
| `Ctrl+Shift+I` | Open / close the inspector |
| `F12` | Cycle the performance HUD: on → on + continuous redraw → off |

Then press **Pick** in the toolbar to arm the picker. Moving the pointer over the app outlines the element under it, and clicking selects it. The click never reaches the app — clicking a button selects it rather than pressing it.

`Ctrl+Shift+I` is the only chord the framework claims. Browsers also bind `Ctrl+Shift+C` to "inspect", but a browser *is* the devtools host; a general UI framework claiming that chord takes it from the app underneath, before dispatch and with no recourse. It is a commonly-used binding — Explorer's "copy as path", among others — so apps keep it. Arm the picker from code with `app.OpenInspector(startPicking: true)`.

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

**Created at.** The Element group carries the source file and line where the element was built — `CommanderApp.Pane.cs:214` — captured with `[CallerFilePath]`/`[CallerLineNumber]` on the element factories. It names *your* call site, not the builder that forwarded the call: `Flex.Row()` reports the line in your component, not `Flex.cs`.

This is compiled in for Debug and out for Release. See [Element origins](#element-origins) below.

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

`DevToolsHotkeysEnabled` governs every built-in developer chord — `Ctrl+Shift+I`, `F12`, and `F11` — as one switch. Turn it off when your app binds those keys itself, or in shipped builds where a developer surface has no business appearing:

```csharp
#if !DEBUG
app.DevToolsHotkeysEnabled = false;
#endif
```

It suppresses only the chords. `OpenInspector`, `window.DevTools`, and the `HULTUI_TRACE` environment variable keep working, so an app can still offer any of it on its own terms — and benchmark automation, which drives tracing through the environment variable, is unaffected.

Vesper Commander and Junction both do exactly the above, so their release installers ship with no inspector, no HUD, and no trace hotkey.

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

## Element origins

The "Created at" row is gated on the `HultUIElementOrigin` MSBuild property, defined in `Directory.Build.targets`. It defaults to **on for Debug, off for Release**, and overrides either way:

```bash
dotnet build -c Release -p:HultUIElementOrigin=true
```

`Element.Origin` is null when it's off, and the inspector says so rather than leaving the row's absence a mystery. `ElementProperties.OriginsAvailable` reports which kind of build you're in.

**What "off" actually costs you nothing of.** Only the *attributes* are conditional, never the parameters — the signatures are identical in both builds. Two consequences follow, and both are the point:

- **No source paths in the binary.** With no `[CallerFilePath]` present the compiler emits the parameter's default rather than a string literal. Verified on the HelloWorld sample: the release assembly contains zero UTF-16 occurrences of the repo path with origins off, and exactly one with them on — one interned string per source file, not per call site.
- **No binary-compatibility trap.** Because the signature doesn't change, an assembly built with origins on and one built with them off are interchangeable. You can't get a `MissingMethodException` by mixing them.

The per-element storage *is* conditional — two fields, compiled out entirely — because that's the part that costs memory on a 10k-element tree.

**Measured cost with origins on:** none detectable. Running the Benchmark sample's `--auto` suite in Release both ways, every operation landed inside run-to-run noise — origins-on posted the faster minimum on `create 10,000`. Nothing on the layout, style, render, or hit-test paths reads the fields.

**One caveat worth knowing.** Threading the caller info required collapsing `Checkbox.New(bool, string)` and `RadioButton.New(string, bool, string)` into single factories with an optional `label`. With two overloads, `Checkbox.New(true, "Enable")` would bind `"Enable"` to the captured file path — overload resolution prefers the candidate that omits fewer optional parameters, and it does so silently. All existing call sites compile and behave identically; the merge exists to make the silent misbinding impossible.

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
