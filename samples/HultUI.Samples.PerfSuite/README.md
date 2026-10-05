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
| `text/` | Re-wrapping paragraphs, cold labels (shaping), ellipsis truncation, paint-only and metric rich-text spans; wrapping and truncation again over Hebrew mixed with Latin (`text/bidi …`), which the BiDi path lays out |
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
- **alloc can move with no change to the scenario's code.** .NET 10's JIT keeps some
  short-lived objects off the heap when inlining lets it see they do not escape, and what
  it inlines follows the profile the run built up. `input/tab through 300 fields` read
  33.0 KB at e5a2206, 29.9 at 16fc7f6, 28.2 at f184580 and 33.0 again at e91d72f, while
  nothing a Tab does changed: with `DOTNET_JitObjectStackAllocation=0` f184580 reads 34,
  and filtered runs read 33 on f184580 and on the opacity merge after it alike. Before
  calling a few KB a regression, check it in a filtered run and with that switch off.
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
- **An idle machine**, which the harness cannot enforce. One run taken while another
  session's tests ran (64 % CPU) read medians 20–55 % high on every row and flagged 18
  rows slower; the interleaved A/B on an idle machine put all 18 within ±4 % on the
  minimum. Check the load first (no test runs, builds or games), and wait it out rather
  than capture through it.
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
| `2026-10-05-f84891b.json` | The first full capture, all 64 scenarios, one later run. It reads 3–5 % above the pair on every row, paint included. A game was running on the machine for part of that evening, the likely cause; the quiet recheck below shows no such shift. Use its allocations, not its times. |
| `2026-10-05-16fc7f6-zorder-before.json` | Before the z-order closure fix: 16fc7f6 (main), median of 3 runs. All 64 scenarios. |
| `2026-10-05-f184580-zorder-after.json` | After it: f184580, median of 3 runs interleaved with the line above. Compare these two. Both read 5–24 % slower than the 90730ee pair (median 9 %). The quiet recheck below puts main at the 90730ee times, so it was the machine — a game was running for part of that evening — not those commits; compare the pair with each other only. |
| `2026-10-05-f184580-opacity-ellipsis-before.json` | Before the opacity-group and truncation fixes: f184580 again, median of 3 runs, each started after 60 s of idle (under 12 % CPU). |
| `2026-10-05-e91d72f-opacity-ellipsis-after.json` | After them: e91d72f, median of 3 runs interleaved with the line above under the same idle rule. Compare these two: 0 slower, 6 faster. |
| `2026-10-05-0091c80-bidi-before.json` | Before the BiDi path remembered anything between widths: 0091c80, which adds the `text/bidi` scenarios. `text/` and `layout/resize` only; the second of two runs. |
| `2026-10-05-7d2d341-bidi-after.json` | After it: 7d2d341, interleaved with the line above. Compare these two: 0 slower, 2 faster. |
| `2026-10-06-9c579ca-styled-before.json` | Before the styled path remembered anything between widths: 9c579ca, which adds the `text/styled` scenarios. `text/` and `layout/resize` only; the second of two runs. |
| `2026-10-06-a68de67-styled-after.json` | After it: a68de67, interleaved with the line above. Compare these two: 0 slower, 4 faster. |
| `2026-10-06-4ba5c4f-multifont-before.json` | Before multi-font labels took the one-line ellipsis shortcut: 4ba5c4f, which adds `text/multi-font ellipsis`. `text/` and `layout/resize` only; the second of two runs. |
| `2026-10-06-7b9835c-multifont-after.json` | After it: 7b9835c, interleaved with the line above. Compare these two: 0 slower, 1 faster. |
| `2026-10-06-e5a2206-recheck.json` | **Quiet recheck**, three points measured together: e5a2206 (60 scenarios; the 11 whose APIs postdate it absent) … |
| `2026-10-06-90730ee-recheck.json` | … 90730ee, after the feature batch (71 scenarios) … |
| `2026-10-06-449ecd0-recheck.json` | … and main at 449ecd0, after the fixes above. Median of 3 runs each, interleaved across the three, every run started on an idle machine (under 8 % CPU, no test host, no game running). The reference for "before the batch" and "now". |

**The feature batch, before → after** (undo, scroll chaining, baseline alignment, find
bar, menu bar, expander, popover, table, aspect ratio, align-content, sticky, the
single-line cross clamp and more): 51 shared scenarios, none past ±10 %. The two that
moved consistently in every run are flex relayouts — `layout/resize 2,000-row list` +7 %
and `layout/deep nesting leaf edit` +12 % on the minimum; the quiet recheck puts them at
+13 % and +12 %, and has them unchanged on main. A sweep over the merges
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

**The BiDi path, before → after.** Text with any character at or above U+0590 — Hebrew
and Arabic, but also an em dash, an arrow or an emoji — is laid out by the BiDi path, whose
right-to-left runs bypass the word cache, so every measure and line there was HarfBuzz
shaping from scratch at every width (~4 µs for one Hebrew word, ~25 µs for a label). The
levels, breaks, segment widths, ellipsis probes and lines are remembered now:
`text/bidi wrap 150 paragraphs resize` 37.6 → 3.6 ms and `text/bidi ellipsis 1,000 labels
resize` 214 → 17 ms (90 → 7.6 MB a frame). The rest of `text/` and `layout/resize` stayed
within ±8 %; `text/create 2,000 unique labels` — whose em dash puts every label on this
path, each laid out once — went through a version where making the memo for a string cost
it +10 %, which is why a string now gets one only at its second layout.

**The styled path, before → after.** Metric spans — bold, italic, a size — send text
to the styled path, or to the BiDi path with its spans; neither kept anything between
widths. Both now share the BiDi memo, keyed by the spans too: `text/styled wrap` 6.4 →
2.1 ms, `text/styled ellipsis` 56.7 → 17.9 ms, `text/styled bidi wrap` 44.4 → 4.7 ms,
`text/styled bidi ellipsis` 388 → 41 ms (178 → 22 MB a frame); the rest within ±5 %.

**Multi-font labels, before → after.** A label of several font runs on the plain path —
here monospace text with IPA, which Cascadia Mono lacks — missed the one-line ellipsis
shortcut, which needs one run, and measured every candidate cut from scratch. Prefix widths
now cover any number of runs, and such a label runs the wrap pass with measured lines that
are never shaped: `text/multi-font ellipsis` 41.4 → 11.8 ms (−81 % allocated); the rest
within ±6 %.

**Before the batch → now: the quiet recheck** (e5a2206 → 90730ee → 449ecd0, minimums,
ms). What the day bought, and the one cost still open:

| Scenario | e5a2206 | 90730ee | 449ecd0 | Total |
|---|---:|---:|---:|---:|
| `anim/60 looping tweens frame` | 27.5 | 27.3 | 0.21 | −99 % |
| `paint/tiles transform + opacity` | 135.2 | 134.6 | 2.10 | −98 % |
| `text/bidi ellipsis 1,000 labels resize` | 220.7 | 220.2 | 16.9 | −92 % |
| `text/ellipsis 1,000 labels resize` | 51.9 | 52.0 | 7.5 | −85 % |
| `text/styled ellipsis 1,000 labels resize` | 76.3 | 76.9 | 17.4 | −77 % |
| `text/wrap 150 paragraphs resize` | 8.81 | 8.87 | 3.00 | −66 % |
| `text/create 2,000 unique labels` | 16.5 | 16.9 | 9.2 | −45 % |
| `layout/cards create 300` | 8.74 | 8.88 | 5.34 | −39 % |
| `layout/shrink 300 overfull rows resize` | 7.03 | 7.31 | 5.66 | −20 % |
| `layout/resize 2,000-row list` | 4.83 | 5.45 | 5.44 | **+13 %** |
| `layout/deep nesting leaf edit` | 0.553 | 0.621 | 0.620 | **+12 %** |
| `controls/tabs switch` | 1.26 | 1.36 | 1.33 | +6 % |

The rest of `text/` fell too (BiDi wrap −91 %, multi-font ellipsis −83 %, styled wrap
−71 %, styled BiDi −29 to −37 %); the other 43 shared rows are within ±7 %, and every
rendered frame allocates what it did at e5a2206 again. The flex-relayout cost the batch
added is the one thing main has not won back.

**What the suite turned up on its first run**, for follow-up:

- **Opacity was expensive to paint — fixed in 945931c.** `SkiaRenderContext.PushOpacity`
  opened its `SaveLayer` with no bounds, so every element under 100 % opacity got an
  offscreen layer the size of the window. The group is now sized to what the subtree
  paints: `paint/tiles transform + opacity` 134.9 → 2.1 ms, `anim/60 looping tweens
  frame` (a quarter of them fading) 27.6 → 0.21 ms.
- **Ellipsis truncation measured every candidate cut from scratch — fixed in 66c2fc5.**
  Each prefix the search tried was itemized and assembled anew, and one-line ellipsis
  opened an ICU line breaker only to replace the line. `text/ellipsis 1,000 labels
  resize` 54.7 → 7.8 ms and 71 → 7.8 MB per frame; the same commit took `text/wrap 150
  paragraphs resize` 9.1 → 6.7 ms, `layout/shrink 300 overfull rows resize` 7.5 →
  5.8 ms (8.2 → 4.0 MB) and `layout/cards create 300` 9.0 → 7.0 ms.
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
