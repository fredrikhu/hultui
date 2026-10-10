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

### The real loop, on virtual time

A pump is one pass of the loop that always may render, so it cannot show what the loop does
between frames. `RunLoop(until)` runs `Application.Run`'s own loop instead, until the
condition holds (asked once per pass) or no window is open. Where the real loop parks the
thread, between frames and while it waits out the display's interval, virtual time moves on,
so a second of it takes milliseconds and repeats exactly. Windows render at 60 Hz.

`eventEvery` ends every wait after that much virtual time, as arriving events do (a 1,000 Hz
mouse, a worker posting to the UI thread, a video's frames), and `onEvent` runs at each one:
post from it with `Application.InvokeOnMainThread`, as a worker would.

```csharp
int rebuilds = 0, frames = 0;                    // counted by the test's component and a Canvas
session.RunLoop(() => done, eventEvery: TimeSpan.FromMilliseconds(1),
    onEvent: () => Application.InvokeOnMainThread(post));
Assert.True(rebuilds <= frames + 1);             // a tween's writes: once per frame drawn
```

A run past `limit` (default 60 s of virtual time) throws `TimeoutException`.

## Virtual Time

The session clock only moves when you move it. Everything time-dependent is driven by it:

- **Animations / tooltip delays** advance by the pumped delta. A 1 s tween completes after exactly 1 s of pumped virtual time — machine speed is irrelevant.
- **Double-click detection** (400 ms window) reads the session clock: two `Click`s with no `AdvanceTime` between them are always a double-click; `AdvanceTime(401 ms)` between them never is.

```csharp
window.MoveMouse(50, 30);                          // hover starts tooltip timer
session.PumpFrame(TimeSpan.FromMilliseconds(600)); // past ShowDelay (0.5 s)
Assert.NotEmpty(window.OverlayContents);           // tooltip is open
```

**`AsyncLoader` is deterministic under a session.** Its thread-pool hop becomes a queue the pump drains at the top of each frame: a load requested during frame N's build runs its producer at the start of frame N+1 and its value is applied in that same frame — one frame later, every run, regardless of machine load. So a chain of loads needs exactly one `PumpFrame()` per stage (tree → selection → content is three pumps), and no `PumpUntil`. The producer is *not* run inline during `Load` — that was tried and broke code that depends on the value arriving on a later frame. The producers a frame drains run concurrently on the thread pool, as they would in an app (an app opening five views waits for the slowest load, not all five in a row), and the drain waits for all of them; their values are applied in the order the loads were requested, not the order they finished. A producer is therefore never on the test thread: one that touches UI state fails headless as it would in the app.

**The one escape from determinism:** `PumpUntil(condition)` / `PumpUntilCompleted(task)` pump repeatedly with a *real-time* timeout (default 5 s), yielding briefly between pumps so background work the session does not control (raw `Task.Run`, real I/O posting back with `InvokeOnMainThread`) can finish. Use them exactly at those boundaries; keep everything else — `AsyncLoader` included — on explicit pumps.

The corollary: a bare `for (…) session.PumpFrame();` loop is **not** a way to wait *on raw background threads*. It yields nothing and advances almost no wall-clock, so a `Task.Run` producer may not have finished by the last iteration — which reads as data that never arrives. Passing a `TimeSpan` advances *virtual* time for animations and timers; it does not give background threads real time either. Wait with `PumpUntil`, or sleep between pumps.

## Input Driver

All coordinates are layout units — the space element bounds live in.

An element-targeted click aims at `Element.WindowBorderBox`, not at the layout box. A scroll
view does not move its children's boxes; it offsets them when painting and hit-testing, so the
centre of a laid-out box inside a scrolled view is not where that element is on screen. A
click that misses hits nothing and reports nothing, so getting this wrong shows up as a test
that passes with its handler never having run — scroll the target into view, then click it.
Inside a popover the overlay's own place is added too, which `ScreenBorderBox` (the space the
element's pointer events arrive in) leaves out. When a test needs the point itself rather than
the click, read `WindowBorderBox` too.

```csharp
window.Click(element);                    // center of laid-out bounds, less ancestor scroll
window.Click(x, y, MouseButton.Right);
window.DoubleClick(element);
window.Drag(from, to, steps: 8);          // fires drag threshold + per-move events
window.Wheel(x, y, 0, -3);                // negative deltaY scrolls down
window.PressKey(KeyCode.Tab, KeyModifiers.Shift);
window.TypeText("hello");                 // into the focused element
// A key as SDL describes it on a QWERTZ keyboard: the layout's character, which Ctrl key,
// and the text it typed (empty: none). Text that is not empty is also sent after the key.
window.PressKey(KeyCode.Y, KeyModifiers.Ctrl, layoutKey: 'z', modifierKeys: ModifierKeys.CtrlLeft, text: "");
```

`PressKey` without those leaves them unknown (0, `None`, null), which is what the WinForms
host gives; code that reads them must fall back, and a test of that code should cover both.

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

**A selectable or linked label logs as `DrawParagraph`, not `DrawText`.** It keeps the paragraph it hit-tests clicks against and draws from it, so its op reads `DrawParagraph "text" at x,y width=w …` with the content box's corner and width. A query for a selectable label's text asks for both: `Named("DrawText").Concat(Named("DrawParagraph"))`.

## Pixels

```csharp
using var image = window.CapturePixels();   // SKImage, layout size × scale
```

Sessions support DPI testing: `HeadlessSession.Create(scale: 2f)` or `window.SetScale(1.5f)`.

**Without golden options, do not assert pixel-exact text across OSes.** Fonts come from the system (`SKFontManager.Default`), so glyphs and text metrics differ between Windows and Linux. Assert on behavior, semantics, and your own drawing — or use golden images, below, which pin the fonts.

## Golden Images

A golden image is a frame compared against a PNG committed next to the test:

```csharp
using var session = HeadlessSession.Create(480, 360, options: HeadlessOptions.Golden());
session.Window.SetRoot(MyPanel());
session.PumpFrames(2);
GoldenImage.Match(session.Window, "my-panel");   // Golden/my-panel.png beside this .cs file
```

**Making and accepting baselines.** Run with `HULTUI_UPDATE_GOLDENS=1`: a missing baseline is written from the frame, and one that differs beyond the tolerance is rewritten (one within it is left alone, so tolerated noise never churns the repo). Look at every PNG in the diff before committing it — a baseline is only as right as the frame it was made from. Without the variable, a missing baseline fails.

**On a mismatch** the test throws `GoldenImageMismatchException`, naming how many pixels differ, by how much at most, and where, and writes three files to `bin/<config>/<tfm>/golden-failures/` (`HULTUI_GOLDEN_FAILURES` overrides): `<name>.actual.png`, `<name>.expected.png`, and `<name>.diff.png` — differing pixels red over a faded copy of the baseline, near-misses within tolerance pale orange. A later pass deletes them. CI uploads the folder as the `golden-failures-windows`/`-linux` artifact when a run fails.

**Why the frames match across OSes.** `HeadlessOptions.Golden()` sets `PinnedText`:

- **Pinned fonts.** Every family name — `Segoe UI`, `sans-serif`, one nobody has — resolves to DejaVu Sans (regular, bold, oblique, bold oblique) and `monospace` to DejaVu Sans Mono, embedded in `HultUI.Headless` (`SkiaFontCollection.Pinned`). Nothing falls back to an installed font: a character DejaVu lacks (CJK, emoji) draws as the missing-glyph box on every machine. Measurement uses the same faces, so layout matches too — the font file decides the metrics and HarfBuzz's advances, not the OS.
- **Text as outlines.** Even from one font file, DirectWrite (Windows) and FreeType (Linux) shade glyphs differently — thousands of pixels over a few lines, hinted or not. `SkiaRasterRenderSurface.TextAsOutlines` draws each glyph's outline through Skia's own path rasteriser instead, which comes out byte for byte the same. The five control goldens in `tests/HultUI.Headless.Tests/Golden/` render to identical PNG bytes on Windows and Linux.

**Tolerance.** `GoldenTolerance.Default` lets any channel of a pixel be 2 apart and no pixel beyond that — room for Skia's SIMD paths to round a blend differently on another CPU, none for a moved edge or a changed colour (a 1 px change to the button corner radius moves ~200 pixels by up to 98). Pass `new GoldenTolerance(Channel: 4, Pixels: 20)` to loosen one test, or `GoldenTolerance.Exact`.

**Traps:**
- **`Match` refuses a window without pinned text** — a baseline from system fonts would fail on the other OS. `Match(SKImage, name)` takes any image and does not check.
- **Baselines are found from `[CallerFilePath]`**, so the source tree must be on the machine running the tests, built without a `PathMap` that rewrites source paths (`ContinuousIntegrationBuild` does). Names are one portable file name: letters, digits, `-`, `_`, `.`.
- **Unhinted, unpinned-looking text is expected.** Outlines carry no hinting, and DejaVu is not the app's font. A golden pins *what changed*, not what a user sees; for the real look, use the `/run-hultui-app` driver.
- **Settle animations first.** Pump enough virtual time for transitions (an expander opening, a toggle sliding) to finish, and keep a focused text input out of a golden or pin the caret's blink phase — the frame is whatever the clock says.
- **Keep scenes small.** Each baseline is a binary blob in git history forever; a few hundred pixels square, under 25 KB each, is the scale `ControlGoldenTests` uses.

## Test Isolation

A session owns process-global state: `Application.Current`, the animation list, and the thread's `SynchronizationContext`. Two live sessions would corrupt each other, so:

- **one session at a time**, created and disposed per test (`using var session = …`),
- **disable test parallelization** in the assembly:

```csharp
[assembly: CollectionBehavior(DisableTestParallelization = true)]
```

`Dispose` resets everything — animations are cancelled, `Application.Current` clears, and the previous `SynchronizationContext` (xunit's own, under xunit) is restored.

## CI

Headless tests need no display server, GPU, or GL — a stock `ubuntu-latest`/`windows-latest` runner works as-is, golden images included (they bring their own fonts). The repo's `test-linux` job in `.github/workflows/junction-installer.yml` is the reference setup. A headless session never loads the SDL3 native library (there's a test asserting exactly that).
