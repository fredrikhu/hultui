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
| `tests/HultUI.Text.Tests`, `HultUI.Rendering.Skia.Tests`, `HultUI.SourceGenerators.Tests`, `HultUI.Platform.Accessibility.Linux.Tests` | subsystem unit | text pipeline, Skia surfaces, reactive generator, AT-SPI constants |
| `tests/HultUI.UiTests` | real window (Windows-only, local-only) | FlaUI/UIA3 launches sample **exes**, proves UIA providers work for real AT clients. Excluded from CI |

## Running tests

```powershell
# Everything CI runs (excludes real-window UiTests):
dotnet test HultUI.slnx --filter "FullyQualifiedName!~HultUI.UiTests"

# Cross-platform subset (what the Linux CI leg runs):
dotnet test HultUI.CrossPlatform.slnf

# One project / one test:
dotnet test tests/HultUI.Headless.Tests
dotnet test tests/HultUI.Core.Tests --filter "FullyQualifiedName~PatchFrom_Preserves"
```

- `dotnet build`/`dotnet test` take **one** project per invocation (MSB1008) — run them separately or use the solution.
- MSB3027 "file locked" usually means a previously launched sample app still holds its exe — find and `Stop-Process` it.
- Reference CI: `.github/workflows/junction-installer.yml` (`test` on windows-latest with the UiTests filter; `test-linux` on ubuntu-latest with the slnf).

## Choosing a layer

- **Element/layout/event logic** → Core.Tests. Build elements directly, dispatch with `EventDispatcher.Dispatch(new ClickEvent(), target)`, measure text with `StubMeasureContext` (7px/char, 14px lines). Target-phase dispatch runs *all* handlers in registration order; `StopPropagation` breaks the loop.
- **Anything needing a laid-out frame, focus, input funnel, scrolling, animations, or a11y queries** → HultUI.Headless. Full guide: `docs/HEADLESS.md`. Runs on any CI runner, no display.
- **Sample-app behavior** → host the whole app headlessly (below), or tree-walk like TodoList tests if no layout is needed.
- **"Does a real screen reader / UIA client see it?"** → HultUI.UiTests with FlaUI. Windows only, launches real windows — run locally, never in CI.
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
- `PumpUntil`/`PumpUntilCompleted` are the *only* real-time escape — use exactly at async boundaries.
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

## Verify like you mean it

When a test guards a fixed bug, run a **negative control**: temporarily reintroduce the bug and confirm the test fails with the same signature as the original report (e.g. re-adding `+ cur` to the scroll clamp fails 3 of 4 ScrollIntoView tests — down-scroll still passes, matching "down works, up broken"). Then revert. A regression test that never failed proves nothing.
