# Headless Testing

`HultUI.Headless` runs a full HultUI application with no window, GL context, or display — rendering into a CPU Skia surface, driven by a manually pumped frame loop with virtual time. Tests launch an app, click, type, query, and assert, then run identically on a developer machine and a display-less CI runner (Windows or Linux).

The same `Application`, `Window`, elements, and components run headless as on screen; only the platform backend is swapped. Nothing in app code changes.

---

## Quick Start

```csharp
using HultUI.Headless;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;

[Fact]
public void ClickingIncrementsCounter()
{
    using var session = HeadlessSession.Create(width: 400, height: 300);

    int count = 0;
    var label = Text.New("Count: 0");
    var button = Button.New("Increment");
    button.OnClick(_ => { count++; label.Content = $"Count: {count}"; });
    session.Window.SetRoot(Flex.Column(gap: 8).Children(button, label));
    session.PumpFrame();   // layout + first render

    session.Window.FindByRole(AccessibilityRole.Button, "Increment")!.Click();
    session.PumpFrame();

    Assert.NotNull(session.Window.FindByText("Count: 1"));
}
```

---

## The Pump

Nothing happens between pumps. `PumpFrame()` runs one iteration of the same loop `Application.Run` uses:

1. advance virtual time (default: one 60 Hz frame),
2. drain main-thread work — `Application.InvokeOnMainThread` and `await` continuations,
3. tick animations by the virtual delta,
4. render every open window that needs a frame,
5. clean up closed windows.

`PumpFrame` returns whether anything rendered, and **respects dirty tracking**: an idle UI renders nothing. A change that doesn't repaint on screen won't repaint headless either, so missing-dirty-flag bugs fail tests instead of hiding. Pass `forceRender: true` to render regardless.

Input injection (`Click`, `TypeText`, …) is synchronous — handlers have run when the call returns — but the *visual* result needs a pump. Assert on state immediately, on rendering after `PumpFrame()`.

```csharp
session.PumpFrame(TimeSpan.FromMilliseconds(450));  // explicit delta
session.PumpFrames(30);                             // n default frames
session.AdvanceTime(TimeSpan.FromSeconds(2));       // clock only, no frame
```

## Virtual Time

The session clock only moves when you move it. Everything time-dependent is driven by it:

- **Animations / tooltip delays** advance by the pumped delta. A 1 s tween completes after exactly 1 s of pumped virtual time — machine speed is irrelevant.
- **Double-click detection** (400 ms window) reads the session clock: two `Click`s with no `AdvanceTime` between them are always a double-click; `AdvanceTime(401 ms)` between them never is.

```csharp
window.MoveMouse(50, 30);                          // hover starts tooltip timer
session.PumpFrame(TimeSpan.FromMilliseconds(600)); // past ShowDelay (0.5 s)
Assert.NotEmpty(window.OverlayContents);           // tooltip is open
```

**The one escape from determinism:** `PumpUntil(condition)` / `PumpUntilCompleted(task)` pump repeatedly with a *real-time* timeout (default 5 s), yielding briefly between pumps so background work (`Task.Run`, `AsyncLoader`) can finish and post back to the UI queue. Use them exactly at async boundaries; keep everything else on explicit pumps.

The corollary: a bare `for (…) session.PumpFrame();` loop is **not** a way to wait. It yields nothing and advances almost no wall-clock, so a `Task.Run` producer may not have finished by the last iteration — which reads as data that never arrives. Passing a `TimeSpan` advances *virtual* time for animations and timers; it does not give background threads real time either. Wait with `PumpUntil`, or sleep between pumps.

## Input Driver

All coordinates are layout units — the space element bounds live in.

```csharp
window.Click(element);                    // center of laid-out bounds
window.Click(x, y, MouseButton.Right);
window.DoubleClick(element);
window.Drag(from, to, steps: 8);          // fires drag threshold + per-move events
window.Wheel(x, y, 0, -3);                // negative deltaY scrolls down
window.PressKey(KeyCode.Tab, KeyModifiers.Shift);
window.TypeText("hello");                 // into the focused element
```

Events enter through the same single funnel as real SDL input, so hit-testing, capture, hover enter/leave, focus-on-click, drag thresholds, and click synthesis all exercise production code paths. `window.Clipboard` and `window.TextInputHost` are in-memory recorders — Ctrl+C lands in `Clipboard.Text`, and `TextInputHost.IsTextInputActive` asserts the IME was armed on focus.

## Queries

Queries read a fresh **accessibility-tree snapshot** per call — the same tree a screen reader walks. What a test can find is exactly what assistive technology can find, which makes every query a soft accessibility check: an unnamed, role-less element is invisible to both.

```csharp
window.GetByText("Save");                              // throws with a tree dump if absent
window.FindByRole(AccessibilityRole.Checkbox)?.IsToggled;
window.FindByAutomationId("search-box");
window.FindAll(n => n.Role == AccessibilityRole.ListItem);
window.DumpAccessibilityTree();                        // debugging
```

A handle pairs the snapshot (`Name`, `Value`, `Role`, `Bounds`, `IsToggled`, …) with the live element. Interactions (`handle.Click()`) inject input at the element's *current* position; snapshot properties are frozen at query time — re-query after interacting.

To test the accessibility *pipeline* itself (the per-frame build/diff that feeds UIA/AT-SPI), install a recording bridge:

```csharp
var bridge = window.InstallAccessibilityBridge();
session.PumpFrame();               // full tree arrives as the first diff
label.Content = "changed";
session.PumpFrame();               // property change arrives as a diff
Assert.NotEmpty(bridge.Diffs);
```

## Display-List Snapshots

Recording captures the drawing calls a frame issues, as text — a snapshot baseline you can actually read:

```csharp
using var session = HeadlessSession.Create(300, 160,
    options: HeadlessOptions.DeterministicRecording());
session.Window.SetRoot(BuildCard());
session.PumpFrame();

Assert.Equal(expected, session.Window.RenderLog.ToString().TrimEnd('\n'));
```

```
BeginFrame 300x160 scale=1 clear=#FFFFFF
  DrawText "Title" (20,20 260x24) size=20 font=sans-serif weight=Bold color=#000000
  DrawRoundedRect (20,60 260x43.2) r=8 fill=#4F46E5
  DrawText "Click me" (32,72 236x19.2) size=16 font=sans-serif weight=Bold color=#FFFFFF
EndFrame
```

This covers the regressions semantic assertions miss — layout shifts, draw order, colors, clipping — while a failing diff says *what* changed instead of handing you two images. Text baselines also delta-compress in git, unlike PNGs. Clips and transforms bracket their children via indentation, so nesting is visible.

Recording decorates the real render context rather than replacing it: the frame still renders, so `CapturePixels()` keeps working alongside it. Cost is about 2.4× the frame time, paid only when enabled.

Beyond whole-log comparison, the log is queryable:

```csharp
window.RenderLog.Named("DrawText");           // ops by name
window.RenderLog.Contains("#4F46E5");         // substring across ops
window.RenderLog.Clear();                     // scope the log to the next frame
window.RenderLog.Ops;                         // RenderOp { Name, Args, Depth }
```

**Determinism.** `DeterministicRecording()` also swaps in `FixedMetricsMeasureContext`, which derives text geometry from font size alone (0.5× width, 1.2× height) instead of real font data. Baselines then match across operating systems — at the cost of positions no longer reflecting real rendering. Use it for layout/paint regression baselines committed to a repo that CI runs on another OS. Use plain `new HeadlessOptions { RecordDrawCalls = true }` to keep real metrics when a test is pinned to one platform.

**Element attribution is not implemented** — ops carry no element identity, so "what did *this* element draw?" means matching on coordinates or text.

## Pixels

```csharp
using var image = window.CapturePixels();   // SKImage, layout size × scale
```

Sessions support DPI testing: `HeadlessSession.Create(scale: 2f)` or `window.SetScale(1.5f)`.

**Do not assert pixel-exact text across OSes.** Fonts come from the system (`SKFontManager.Default`), so glyphs and text metrics differ between Windows and Linux. Assert on behavior, semantics, and your own drawing; treat text positions as approximate unless the test is pinned to one platform. (Byte-stable golden images need an embedded font — a planned follow-up.)

## Test Isolation

A session owns process-global state: `Application.Current`, the animation list, and the thread's `SynchronizationContext`. Two live sessions would corrupt each other, so:

- **one session at a time**, created and disposed per test (`using var session = …`),
- **disable test parallelization** in the assembly:

```csharp
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

`Dispose` resets everything — animations are cancelled, `Application.Current` clears, and the previous `SynchronizationContext` (xunit's own, under xunit) is restored.

## CI

Headless tests need no display server, GPU, or GL — a stock `ubuntu-latest`/`windows-latest` runner works as-is. The repo's `test-linux` job in `.github/workflows/junction-installer.yml` is the reference setup. A headless session never loads the SDL3 native library (there's a test asserting exactly that).
