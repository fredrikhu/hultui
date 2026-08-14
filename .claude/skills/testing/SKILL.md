---
name: testing
description: How to test HultUI code — choosing the right test layer (unit, headless app, real-window UIA), running the suites, hosting a full sample app headlessly, and the gotchas that waste hours (unscrolled layout, session globals, SendKeys is forbidden).
---

# Testing HultUI

The framework has three test layers plus ad-hoc verification. Pick the lowest layer that can observe the behavior; escalate only when the bug lives in a seam the lower layer fakes away.

## Test map

| Project | Layer | Covers |
|---|---|---|
| `tests/HultUI.Core.Tests` | unit | elements, layout, events, reactivity, styling, dialogs, a11y tree — no session, `StubMeasureContext` for text |
| `tests/HultUI.Headless.Tests` | headless integration | HeadlessSession itself + framework behaviors needing real layout/render (e.g. `ScrollIntoViewTests`, `GeometryQueryTests`) |
| `tests/HultUI.Samples.TodoList.Tests` | component | sample app logic via direct tree-walk + `EventDispatcher.Dispatch` (no session) |
| `tests/Junction.Tests` | app | `apps/Junction`: pure logic (diff/conflict/patch/graph parsing), `GitRepository` against real temp repos, plus the whole app hosted headless |
| `tests/VesperCommander.Tests` | app | `apps/VesperCommander`: engines (find/sync/rename/diff/archive) plus headless hosting |
| `tests/HultUI.Text.Tests`, `HultUI.Rendering.Skia.Tests`, `HultUI.SourceGenerators.Tests`, `HultUI.Platform.Accessibility.Linux.Tests` | subsystem unit | text pipeline, Skia surfaces, reactive generator, AT-SPI constants |
| `tests/HultUI.UiTests` | real window (Windows-only, **opt-in**) | FlaUI/UIA3 launches sample **exes**, proves UIA providers work for real AT clients. Skipped unless `HULTUI_UITESTS=1` |

## Running tests

```powershell
# Everything CI runs. The real-window UiTests skip themselves:
dotnet test HultUI.slnx

# Cross-platform subset (what the Linux CI leg runs):
dotnet test HultUI.CrossPlatform.slnf

# One project / one test:
dotnet test tests/HultUI.Headless.Tests
dotnet test tests/HultUI.Core.Tests --filter "FullyQualifiedName~PatchFrom_Preserves"

# The real-window UiTests, which take over the foreground (Windows, desktop session):
$env:HULTUI_UITESTS = "1"; dotnet test tests/HultUI.UiTests
```

**`HULTUI_UITESTS` is the gate for the real-window suite.** Without it every UiTest
reports as *skipped* — deliberately visible, rather than silently absent the way the old
`--filter "FullyQualifiedName!~HultUI.UiTests"` left them. Don't reintroduce that filter:
the gate is the one mechanism, and a name filter also swallows any future test whose name
happens to contain the string.

- `dotnet build`/`dotnet test` take **one** project per invocation (MSB1008) — run them separately or use the solution.
- MSB3027 "file locked" usually means a previously launched sample app still holds its exe — find and `Stop-Process` it.
- Reference CI: `.github/workflows/junction-installer.yml` (`test` on windows-latest runs the whole slnx and lets the UiTests skip themselves; `test-linux` on ubuntu-latest with the slnf).

## Choosing a layer

- **Element/layout/event logic** → Core.Tests. Build elements directly, dispatch with `EventDispatcher.Dispatch(new ClickEvent(), target)`, measure text with `StubMeasureContext` (7px/char, 14px lines). Target-phase dispatch runs *all* handlers in registration order; `StopPropagation` breaks the loop.
- **Anything needing a laid-out frame, focus, input funnel, scrolling, animations, or a11y queries** → HultUI.Headless. Full guide: `docs/HEADLESS.md`. Runs on any CI runner, no display.
- **Sample-app behavior** → host the whole app headlessly (below), or tree-walk like TodoList tests if no layout is needed.
- **"Does a real screen reader / UIA client see it?"** → HultUI.UiTests with FlaUI. Windows only, launches real windows that take the foreground — run locally with `HULTUI_UITESTS=1`, never in CI.
- **Perf/GL** → real window with tracing (`HULTUI_TRACE=<path>`, `SDL_WINDOW_ACTIVATE_WHEN_SHOWN=0`), or `samples/HultUI.Samples.Benchmark --auto|--profile`, or the live **F12 HUD** (below). Never use the real window for *correctness* checks.

## Headless quick reference

```csharp
using var session = HeadlessSession.Create(width: 400, height: 300);
session.Window.SetRoot(root);
session.PumpFrame();                        // layout + first render — nothing happens between pumps

session.Window.GetByText("Save").Click();   // input is synchronous; visuals need a pump
session.Window.PressKey(KeyCode.Tab, KeyModifiers.Shift);
session.Window.TypeText("hello");           // into the focused element
session.PumpFrame(TimeSpan.FromMilliseconds(600));  // virtual time: tooltips, animations, dbl-click window
```

- Queries (`GetByText`, `FindByRole`, `FindByAutomationId`) read a fresh a11y snapshot — every query doubles as an accessibility check. Re-query after interacting; snapshot props are frozen.
- **`Find`/`FindByText` silently drop a matching node whose element cannot be resolved** — including the recycled rows of a `VirtualList`. A row visible in `DumpAccessibilityTree()` can still come back null. When asserting on virtualized content, walk `window.AccessibilityTree` yourself (see `JunctionAppHostTests.Contains`). `FindByText` is also exact-match: a row's accessible name is its children concatenated (`"subject, author, hash"`), so substring matching is usually what you want.
- `PumpUntil`/`PumpUntilCompleted` are the *only* real-time escape — use exactly at async boundaries. **`PumpFrame()` does not wait**, so a `for` loop of them advances almost no wall-clock and background work (git subprocesses, `AsyncLoader` producers) never finishes — a debug probe built that way reported a "permanently stuck loader" that was simply never given time. Wait with `PumpUntil`, or pump with a real sleep.
- Render regressions: `HeadlessOptions.DeterministicRecording()` → assert on `window.RenderLog` text baselines (readable, git-diffable). `FixedMetricsMeasureContext` makes them cross-OS.
- `window.CapturePixels()` for pixels, but **never** pixel-exact text asserts across OSes (system fonts differ).

## Hosting a full sample app headlessly

The reliable way to verify keyboard-driven flows in a real app (proven on the Junction palette-scroll bug). No test project needed for a one-off — a scratchpad console referencing `HultUI.Headless` + the sample csproj suffices; promote to a test if it covers a framework feature.

```csharp
using var session = HeadlessSession.Create(1200, 800);
var app = new JunctionApp(session.Application, session.Application.PrimaryWindow, settings);
session.Window.SetRoot(app.Mount());
session.PumpFrame();

session.Window.PressKey(KeyCode.K, KeyModifiers.Ctrl);   // open palette
session.PumpFrame();
// Assert on live elements: el.Layout.AbsoluteBorderBox, scroll.ScrollOffsetY, …
// Private Ref<T> fields the app doesn't expose: read via reflection.
```

## Reading the F12 perf HUD

**F12** on any running sample cycles the HUD: enabled → enabled + continuous redraw (forces a frame every loop, so FPS reflects raw throughput) → off. It splits each rendered frame into timed spans plus a residual; all seven time rows sum to the frame total.

| Row | Wraps | Notes |
|---|---|---|
| Style | `StyleResolver.Resolve` | only runs when style-dirty |
| Layout | `LayoutEngine.PerformLayout` | only runs when layout-dirty |
| Render | `BeginFrame` clear + `ElementRenderer.RenderTree` | **command recording only** — no pixels yet |
| Flush | `Canvas.Flush()` (`ctx.EndFrame`) | GPU rasterizes/submits the recorded display list; usually the bulk of the frame |
| Present | `Surface.Present` + `SwapBuffers` | near-free with vsync off |
| Overlay | element-count walk + the HUD's own draw | **observer tax** — nonzero only while the HUD is open |
| Other | `Frame − Σ(above)` | residual: `ClearAllFlags` tree walks, caption-height, action-router drain |
| Elements | full-tree count | HUD-only walk |

Each row shows the last frame's value plus a rolling 5s peak. Reading them:

- **Render ≠ GPU work.** Render is Skia *recording* draw commands; the actual rasterization is **Flush**. A frame that looks cheap in Render can be Flush-bound under GPU contention — don't conflate them.
- **The HUD measures itself.** `Overlay` can be your biggest row, and it vanishes in production. The app's real per-frame cost is `Render + Flush + Present + Other`; subtract Overlay before quoting a number.
- **One-frame lag on the tail rows.** Flush/Present/Overlay/Other close *after* the HUD draws, so they display the *previous* frame's value — inherent (a span can't show its own result), and consistent across those four.
- Counters live in `src/HultUI.Core/DevTools/PerformanceCounters.cs`; HUD layout in `DevToolsOverlay.RenderPerformanceHud`. A new row = a `Begin/End` span pair (or a computed residual) + a rolling peak + one `Line(...)`, and bump `hudH`.

## Gotchas (each of these has burned a session)

1. **Layout is UNSCROLLED.** `Layout.AbsoluteBorderBox` is content-space; `ScrollOffsetY` is a render-time translate. Scroll-into-view math must NOT add the current offset (doing so makes up-scroll silently never fire). Use the framework helpers `ScrollViewElement.ScrollIntoView(child)` / `ScrollRangeIntoView(top, height)` — tested by `ScrollIntoViewTests`.
2. **One session at a time.** A HeadlessSession owns process-global state (`Application.Current`, animation list, ReactiveRuntime, SynchronizationContext). Every session-using assembly needs `[assembly: CollectionBehavior(DisableTestParallelization = true)]` and per-test `using var session`.
3. **Never drive the real window with SendKeys.** `SetForegroundWindow` from a background shell often fails to raise the SDL window, so keystrokes leak into whatever the user is actually using; SDL also drops posted keydowns to unfocused windows. There is no safe headful keyboard path — use headless `PressKey`. Real windows are for perf traces and read-only UIA.
4. **`TimeSpan.FromSeconds` rounds to ms** — use `FromTicks` for exact frame deltas.
5. **Bare `Text` inside `HultUI.*` namespaces** binds to the `HultUI.Text` namespace — add `using Text = HultUI.Core.Builders.Text;` *inside* the namespace.
6. **Raw-string baselines carry the source file's line endings** (autocrlf ⇒ CRLF on Windows, LF on Linux CI). Normalise with `ReplaceLineEndings("\n")`; `RenderLog.ToString()` and a11y dumps always emit `\n`.
7. **`PatchFrom` on `TextInputElement`** preserves caret/selection only when text is unchanged — rebuild-heavy UIs rely on this; there are regression tests in `TextInputElementTests`.
8. **Flex rows inside a scrolled column** need `FlexShrink(0)` in tests only if you built them before 2026-07-18's shrink fix — new tests use `Height(h).FlexShrink(0)` for explicit fixed rows (see `ScrollIntoViewTests.BuildList`).
9. **An element's box is not what the text drew.** A stretched `TextElement` always fills its cell, so `Layout.AbsoluteBorderBox.Width` tells you nothing about how much text was laid out — a test asserting "the line reaches the cell edge" against it passes even when the line stops a third of the way in. The laid-out extent is in `TextElement._lines` (`TextLineBox[]`, populated when the label is `Selectable`), or assert at the `TextPipeline` level with a stub font of fixed character width (`TextPipelineTests`).
10. **A view that replaces (not layers over) the tree can be keyboard-dead.** `Ref<T>.Current` is never cleared on unmount and `Element.Focus()` accepts a detached element, so `myRef.Current?.Focus()` after a rebuild that dropped the element focuses a corpse — keys then dispatch to a one-element parent chain and never reach the root handler. Test any full-screen mode by pressing the key that *leaves* it, and then a plain key to prove the restored tree got the keyboard back (`DiffViewTests`). This is invisible to a test that only asserts the view opened.

## Polling hides bugs that only an idle app has

`PumpUntil(() => { session.PumpFrame(); return tree.Contains("…"); })` keeps rebuilding until the content shows up. That is exactly what a **stuck** UI needs to un-stick itself, so a test written this way passes while the real app hangs.

It happened: a pane whose data arrived but whose view never re-rendered (the loader wasn't subscribed) looked fine under every polling test, and only showed up as "stays on *Loading…* until I switch tabs". The idle app produces no rebuilds; the test produced dozens.

So when the thing under test is **whether an update propagates**, assert the mechanism, not the eventual pixels:

- assert the state that proves the wiring (a tracked signal incremented, a handler ran, a subscription exists) — that is a single deterministic check, not a race;
- or pump a *bounded* number of frames after the async work has demonstrably landed, so nothing else can nudge the UI;
- keep `PumpUntil` for genuinely waiting on async completion, which is what it is for.

Same shape as gotcha 2: incidental rebuilds also come from a `GitWatcher` firing, a toast timer, or hover — none of which the idle app is doing while the user stares at a spinner.

## Assert against what is actually rendered

a11y dumps carry the *rendered* string, not your source literal. Section headers upper-case their labels (`SectionHeader` → `"HIDDEN"`, `"INCOMING · THEIRS"`), so `Contains("Hidden")` silently never matches. Dump the tree once and grep it before writing the assertion. Likewise, reflection into a sample app: `[Reactive]` members are **properties**, so `GetField("View")` returns null — use `GetProperty`.

## Sample-app fixtures: git will merge your test data

Building a repo for a conflict or multi-hunk test, two edits placed a line or two apart come back as **one** region — git coalesces nearby changes. Two tests asserted "2 hunks" and got 1 until the edits were separated by ~12 untouched lines. If a fixture must produce N distinct hunks or conflict regions, space the edits out and assert the count before relying on it.

## Verify like you mean it

When a test guards a fixed bug, run a **negative control**: temporarily reintroduce the bug and confirm the test fails with the same signature as the original report (e.g. re-adding `+ cur` to the scroll clamp fails 3 of 4 ScrollIntoView tests — down-scroll still passes, matching "down works, up broken"). Then revert. A regression test that never failed proves nothing.

The control also tells you how much of the suite actually covers the bug, which is the real signal. Reverting `ForceRebuild` to its untracked form failed 2 of 4 re-tracking tests — and revealed a second symptom nobody had reported (stale subscriptions causing *spurious* rebuilds). Swapping the list's chord check for a naive "any modifier" failed **1 of 11** keyboard tests: without that single NumLock case the regression would have shipped looking green.
