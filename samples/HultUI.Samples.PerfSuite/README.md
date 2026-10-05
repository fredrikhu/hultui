# Scenario performance suite

The framework's features one at a time, each timed the same way: build the UI in a
headless session, then time one operation from the mutation to the rendered frame,
with the frame split into **build / style / layout / render / other**.

It sits next to two narrower benchmarks:

| | What it covers | Where it runs |
|---|---|---|
| [`HultUI.Samples.Benchmark`](../HultUI.Samples.Benchmark/README.md) | One workload in depth: the js-framework-benchmark table, comparable to a browser | A real window, GPU, end to end |
| [`HultUI.Samples.TextBench`](../HultUI.Samples.TextBench/README.md) | The text pipeline, per call, in nanoseconds | No UI at all |
| **this suite** | The breadth: 64 scenarios over every feature area | Headless, CPU raster, any OS |

Use it to show that a change to one feature costs the others nothing, and to follow a
scenario over time.

```bash
dotnet run -c Release --project samples/HultUI.Samples.PerfSuite
dotnet run -c Release --project samples/HultUI.Samples.PerfSuite -- --filter grid,text/wrap
dotnet run -c Release --project samples/HultUI.Samples.PerfSuite -- --json captures/mine.json --label "my change"
dotnet run -c Release --project samples/HultUI.Samples.PerfSuite -- --compare captures/before.json captures/mine.json
dotnet run -c Release --project samples/HultUI.Samples.PerfSuite -- --verify
```

The full suite takes about 25 s; a filtered run at least 5 s, which is the pre-warm (see
below). `--list` prints every scenario, `--help` every option.

## Groups

| Group | What its scenarios time |
|---|---|
| `layout/` | Resize drags over long lists, deep nesting, flex wrap, baseline alignment, aspect-ratio tiles, overfull rows and columns (flex shrink); building 300 cards |
| `grid/` | Creating, resizing and editing a 1,800-cell grid with `auto` and `fr` columns |
| `text/` | Re-wrapping paragraphs, cold labels (shaping), ellipsis truncation, paint-only and metric rich-text spans |
| `scroll/` | Wheel and jumps through a 5,000-row list, sticky section headers, nested scroll chaining |
| `virtual/`, `table/` | `VirtualListElement` with 100,000 items; `TableView` with 10,000 rows: sort, wheel, column resize |
| `component/` | Keyed and unkeyed rebuilds of 1,000 rows, reverse, a small component in a big tree, 300 mounts, a signal read by 500 effects |
| `input/` | Hover, checkbox click, typing in `TextInput` and a 2,000-line `TextArea`, undo/redo, Tab through a form, drag to reorder |
| `overlay/` | Opening a popover, dialog, menu bar menu, dropdown, tooltip; filtering a command palette of 2,000 commands |
| `controls/`, `icons/` | Mounting a control screen, tabs, an animated expander, a slider drag, 600 icons |
| `theme/` | Light ⇄ dark, and cycling the `HultUI.Themes` catalogue, on the control screen |
| `anim/` | One frame of 60 looping tweens, of spinners and progress bars, and a hover transition |
| `paint/` | The same 400 tiles plain, rounded, shadowed, gradient-filled, transformed: the price of each effect |
| `a11y/` | Creates, edits and Tab with an accessibility client attached: the tree build and diff |

## Reading the output

```
scenario                      elems      min   median   build   style  layout  render   other     alloc
                                          ms       ms      ms      ms      ms      ms      ms     KB/op
layout/resize 2,000-row list  12002    5.271    5.723    0.11    0.00    4.85    0.69    0.01     4,169
```

- **min** is the best of all samples (5 rounds × 5 steps), **median** the typical one. As
  in TextBench, compare the min between captures: it is the cost when nothing interfered.
  The median says the min was not one lucky sample.
- **build** is the mutation itself — for a component, its whole rebuild and reconcile,
  because a `[Reactive]` write rebuilds synchronously. **style / layout / render** are
  the window's own pass timers, summed over the step's frames. **other** is the rest of
  the frame: the accessibility tree build and diff, overlay layout and paint (an overlay
  is laid out outside the Layout pass), and frame bookkeeping.
- **render is CPU rasterization.** Headless draws into a CPU Skia surface, so render
  times are not what a GPU window pays. Read them relatively (one effect against
  another, one capture against another). Build, style and layout run the same code as a
  real window.
- **elems** is the tree's element count after the steps, overlays not included. A
  `TextArea` counts as one element however many lines it holds.
- **alloc** is the managed bytes the UI thread allocated per step, mutation and frames
  together. It is far steadier than time, though not exact: most rows repeat within
  0.5 KB, rows that create or rebuild elements vary by up to ~1 %, and the theme switches
  by ~5 %. A change of a few KB shows in one pair of runs; a change in time needs
  interleaved runs.
- A row that ends in `! n/m steps rendered nothing` timed nothing: the scenario's change
  never dirtied anything. Fix the scenario.

## What the harness does, and why

Each rule is here because its absence produced a wrong number first.

- **Pre-warm for at least 5 seconds**, in passes over every selected scenario. Tiered
  compilation with dynamic PGO reaches the steady state after seconds of work, not after a
  count of calls. With one pass and a sleep, a filtered run of one scenario read 30 ms
  where the full suite reads 5 ms; over repeated rounds it converged in about 2.5 s.
- **Round-robin rounds**: round *n* of every scenario before round *n + 1* of any, so
  drift lands on every scenario alike.
- **A fresh session per round** (a session owns process-wide state), and a forced GC
  before the timed steps.
- **Pin to the performance cores** of a hybrid CPU. On this repo's i7-14700K an
  efficiency core runs these scenarios ~1.8× slower, and the scheduler moves a lone busy
  thread between the two kinds, so short runs read up to twice their full-suite number.
  Windows reads each core's efficiency class, Linux each core's maximum frequency.
  `--no-pin` turns it off.
- **High process priority**, invariant culture.
- **A check after every untimed step** (`Op.Check`): the dialog really opened, the
  character reached the `TextArea`, the wheel really scrolled. An input that misses its
  target still renders a frame and times something; only the check tells. `--verify`
  runs every check without timing anything — run it after changing a scenario.

## Captures and comparing them

`--json PATH` writes a capture: every row, plus the commit (`git describe`, with
`-dirty` for uncommitted work), machine, CPU, OS, runtime and pinning. `--compare BASE
CURRENT` prints both side by side and calls a row **slower** or **faster** only when its
min *and* median moved past the threshold (10 % by default, `--threshold`) in the same
direction.

Only compare captures from the same machine, configuration and power plan. Captures
live in [`captures/`](captures/); add one when a change moves numbers on purpose.

**A "before" for a change.** Check the old commit out in a second worktree, copy this
project into it, delete the scenarios whose APIs it lacks, and capture both sides
alternately (base, current, base, current…) — the interleaving spreads any drift in the
machine over both. Scenario names are the join key, so keep a name once captures use it.

### The captures so far

All on one machine: i7-14700K (pinned to its 8 performance cores), RTX 4080, Windows 11,
.NET 10, Release.

| File | What it is |
|---|---|
| `2026-10-05-e5a2206-before.json` | Before the 35-merge feature batch of 2026-10-04/05: e5a2206, median of 3 runs. The 11 scenarios whose APIs postdate it are absent. |
| `2026-10-05-90730ee-after.json` | After it: 90730ee, median of 3 runs interleaved with the line above. Compare these two. |
| `2026-10-05-f84891b.json` | The first full capture, all 64 scenarios, one later run. It reads 3–5 % above the pair on every row, paint included — drift between sessions, which is why a before/after must be interleaved. |
| `2026-10-05-16fc7f6-zorder-before.json` | Before the z-order closure fix: 16fc7f6 (main), median of 3 runs. All 64 scenarios. |
| `2026-10-05-f184580-zorder-after.json` | After it: f184580, median of 3 runs interleaved with the line above. Compare these two. Both read 5–24 % slower than the 90730ee pair (median 9 %): another session and the tabs and focus commits apart, so compare them with each other only. |

**The feature batch, before → after** (undo, scroll chaining, baseline alignment, find
bar, menu bar, expander, popover, table, aspect ratio, align-content, sticky, the
single-line cross clamp and more): 51 shared scenarios, none past ±10 %. The two that
moved consistently in every run are flex relayouts — `layout/resize 2,000-row list` +7 %
and `layout/deep nesting leaf edit` +12 % on the minimum. A sweep over the merges
that touched layout code puts no single cause behind it: align-content (189d176) +6 %,
aspect ratio (82377dc) +3 %, baseline (a0dd054) and the JustifyContent fix (b5c8916)
+2 % each. Grid, text, scroll, virtual lists, components, input, overlays, themes,
animation, paint and accessibility were flat. Allocation did move: every rendered frame
allocated 3–6 KB more since the sticky merge (b8102e4), and a keystroke in a `TextInput`
28 KB more — one cause, fixed in f184580 (below).

**The z-order closure, before → after.** Sticky gave `ZOrderHelper.GetChildrenInZOrder`
a stable sort whose comparison captures the local child list. C# allocates a captured
local's closure where the local's scope begins — the method's first line — so every call
paid 24 bytes, the no-z-index fast path included, and the renderer and hit tester call it
for every element of every frame. The `TextInput` keystroke repaints 1,204 elements:
1,204 × 24 B is the whole 28 KB, so undo recording costs under 0.1 KB a keystroke. With
the sort in a method of its own, every scenario that only re-lays out or repaints
allocates what it did at e5a2206, within 0.2 KB:

| Scenario | e5a2206 | 16fc7f6 | f184580 |
|---|---:|---:|---:|
| `layout/deep nesting leaf edit` | 333.5 | 364.6 | 333.5 |
| `grid/resize 300x6` | 16.8 | 22.4 | 16.8 |
| `input/type in TextInput` | 46.4 | 74.6 | 46.4 |
| `anim/hover transitions` | 36.6 | 55.6 | 36.6 |
| `paint/tiles plain` | 5.8 | 9.2 | 5.8 |

KB allocated per step: 24 bytes less for each element drawn or hit-tested in a frame —
1.5 KB on the lightest step, 31 KB on the 1,323 elements of deep nesting, up to 58 % of
a light step — and no time moved past ±10 %. The rows still above e5a2206 are those that
create elements, 8 bytes per element: `Element.OverlayOrigin` (popover, 3f14903) took
`BoxElement` from 600 to 608 B.

**What the suite turned up on its first run**, for follow-up:

- **Opacity is expensive to paint.** `SkiaRenderContext.PushOpacity` opens its
  `SaveLayer` with no bounds, so every element under 100 % opacity gets an offscreen
  layer the size of the window. `paint/tiles transform + opacity` renders in ~132 ms
  against 0.3 ms plain, and `anim/60 looping tweens frame` (a quarter of them fading)
  in ~27 ms.
- **Ellipsis truncation re-shapes on every width.** `text/ellipsis 1,000 labels resize`
  costs ~53 ms and 71 MB per frame, against ~5 ms and 4 MB for 2,000 untruncated rows.
- **Shadows are the dearest common effect**: 400 shadowed tiles paint in 4.5 ms against
  0.9 ms rounded-with-border and 1.4 ms gradient (CPU raster; the order is the signal).
- **Opening a dialog costs ~3 ms**, almost all of it overlay layout and paint ("other"),
  against ~0.5 ms for a popover, menu or dropdown.

## Adding a scenario

```csharp
yield return new Scenario("group/what it does",
    "One sentence: the UI, and the operation a step times",
    ctx =>
    {
        var list = /* build the UI */;
        ctx.Mount(ScrollView.New().FlexGrow(1).Children(list));   // sets the root, pumps 2 frames
        return new Op
        {
            Reset = step => { /* untimed: undo the last step (close, clear) */ },
            Run = step => { /* timed: the mutation; the harness pumps the frame */ },
            Check = step => /* null when the step did what it says */ null,
        };
    });
```

- **Make every step cost the same.** Alternating two different operations (open, close)
  gives a min of one and a median of the other. Time one; undo it in `Reset`.
- **Make every step do new work.** A resize that alternates between two widths times
  measure-cache hits; `Ui.DragWidth(step)` walks 40 distinct widths. Toggling a value back
  to what it was may dirty nothing.
- **Live property changes may not dirty anything.** Layout and inherited-text setters
  (`.Width()`, `.MaxLines()`, `.TextColor()`) are builder state; after mount, call
  `MarkLayoutDirty()` / `MarkStyleDirty()` yourself, as an app would.
- **Aim input with `WindowBorderBox`**, never `Layout` boxes, which ignore scrolling.
- **Give it a `Check`**, and see it fail once: make `Run` do nothing and run `--verify`.
