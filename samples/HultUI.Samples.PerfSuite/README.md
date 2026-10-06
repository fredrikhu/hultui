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
| `2026-10-06-f586c39-leafcache-before.json` | Before a leaf was measured once per width: main at f586c39, all 71 scenarios, median of 3 runs, idle-gated … |
| `2026-10-06-4f8a3fc-leafcache-after.json` | … and after it, 4f8a3fc, interleaved with the line above. Compare these two: 0 slower, 11 faster. |
| `2026-10-06-49a9b8b-heightrange-before.json` | Before a measurement held for a range of offered heights: main at 49a9b8b, median of 3 idle-gated runs … |
| `2026-10-06-c80711c-heightrange-after.json` | … and after it, with the flex line pool and the packed cache entry: c80711c, interleaved with the line above. Compare these two: 0 slower, 10 faster. |
| `2026-10-06-cc29f80-flexplan-before.json` | Before the layout pass took the measure pass's flex resolution: main at cc29f80, median of 3 idle-gated runs … |
| `2026-10-06-0bf5823-flexplan-after.json` | … and after it, with the min-content width kept until it changes: 0bf5823, interleaved with the line above. Compare these two: 0 slower, 3 faster (resize 2,000 rows is −10 % on both, a hair under the flag). |
| `2026-10-06-4b4c733-widthrange-before.json` | Before a measurement held for a range of offered widths: main at 4b4c733, median of 3 idle-gated runs … |
| `2026-10-06-ca425da-mount-before.json` | Before a leaf of a set height held at any height: main at ca425da, median of 3 idle-gated runs … |
| `2026-10-06-da3c9d2-forcedwidth-before.json` | Before a forced width was the unforced measurement at that width: main at da3c9d2, median of 3 idle-gated runs … |
| `2026-10-06-8b2af12-forcedwidth-after.json` | … and after it: 8b2af12, interleaved with the line above. Compare these two: 0 slower, 1 faster (resize 2,000 rows −14 %; the rest of what moved is under the flag: see below). |
| `2026-10-06-5855df8-textarea-before.json` | Before a TextArea found each line break from a guess at a line's length: main at 5855df8 with `text/open a long document in a TextArea` added, median of 3 idle-gated runs … |
| `2026-10-06-ae037d5-textarea-after.json` | … and after it: ae037d5, interleaved with the line above. Compare these two: 0 slower, 1 faster (the long document −73 %). |
| `2026-10-06-b33f5f1-plainpath-before.json` | Before left-to-right text left the BiDi path: main at b33f5f1, median of 3 idle-gated runs … |
| `2026-10-06-2261567-plainpath-after.json` | … and after it: 2261567, interleaved with the line above. Compare these two: 0 slower, 1 faster (labels with an em dash −44 %). |
| `2026-10-06-4aa5653-patch-before.json` | Before a patch that changes nothing about the layout left it standing: main at 4aa5653, median of 3 idle-gated runs … |
| `2026-10-06-2688a99-patch-after.json` | … and after it, with a kept layout needing the same forced sizes exactly: 2688a99, interleaved with the line above. Compare these two: 0 slower, 3 faster. |
| `2026-10-06-c451515-mount-after.json` | … and after it, with range ends checked for rounding, far boxes laid out and controls that ignore the height saying so: c451515, interleaved with the line above. Compare these two: 0 slower, 0 faster past the flag (icons −10 %, gallery −6 %: see below). |
| `2026-10-06-98e0581-widthrange-after.json` | … and after it, with the layout pass keeping a set-size box's layout and the cache entry back at 28 bytes: 98e0581, interleaved with the line above. Compare these two: 0 slower, 4 faster. |

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
added is the one thing main had not won back — until the next entry.

**A leaf measured once per width, before → after** (4f8a3fc). A per-commit bisect put the
batch's flex cost on the aspect-ratio commit, but counting showed where layout time went:
a 2,000-row resize measured each label 11 times, 22,000 text measurements where 8,000 are
needed — the leaf cache keyed on the offered height, which text never reads, so the measure
and layout passes missed each other, and a laid-out leaf measured itself once more. Shared
now, with every element box of all 71 scenarios bit-identical to before:

| Scenario | f586c39 | 4f8a3fc | |
|---|---:|---:|---:|
| `layout/baseline rows resize` | 1.84 | 1.40 | −24 % |
| `layout/resize 2,000-row list` | 5.53 | 4.39 | −21 % |
| `layout/shrink 300 overfull rows resize` | 5.71 | 4.59 | −20 % |
| `component/reverse 1,000 keyed rows` | 4.28 | 3.55 | −17 % |
| `controls/expander toggle (animated)` | 4.92 | 4.11 | −16 % |
| `component/edit one of 1,000 keyed rows` | 4.15 | 3.49 | −16 % |
| `controls/tabs switch` | 1.30 | 1.13 | −13 % |
| `a11y/create 1,000 rows, bridge on` | 4.64 | 4.06 | −13 % |
| `text/ellipsis 1,000 labels resize` | 7.58 | 6.64 | −12 % |
| `layout/deep nesting leaf edit` | 0.639 | 0.575 | −10 % |

Nothing moved up past +3 %. Against e5a2206, before the batch, the flex relayouts now
read lower, not higher (`layout/resize 2,000-row list` 4.83 → 4.39). Aspect ratio stayed:
knocked out of the engine it costs 2–3 % (docs/DECISIONS.md).

**A measurement holds for a range of heights, before → after** (d6c0bd6, with the flex line
pool 5decd7f and the packed cache entry 66e06f7). Containers had the leaves' problem one level
up: a 2,000-row resize ran a container's flex measurement 22,001 times for 6,000 containers,
14,000 of them misses on the offered height alone that came out the same. Each cache entry now
records the least height its answer holds at (a row: where its children hold; a column: at or
above its items' total when nothing grows or shrinks), and the flex line lists are pooled
instead of allocated per pass. 8,001 measurements; every element box unchanged;
`LayoutPathIndependenceTests` lays 1,000 random trees out through growing and shrinking windows
against fresh layouts:

| Scenario | 49a9b8b | c80711c | |
|---|---:|---:|---:|
| `layout/resize 2,000-row list` | 4.27 | 3.12 | −27 % |
| `text/styled ellipsis 1,000 labels resize` | 16.57 | 12.27 | −26 % |
| `controls/expander toggle (animated)` | 4.06 | 3.11 | −23 % |
| `layout/deep nesting leaf edit` | 0.563 | 0.432 | −23 % |
| `layout/shrink 300 overfull rows resize` | 4.59 | 3.76 | −18 % |
| `component/reverse 1,000 keyed rows` | 3.58 | 2.98 | −17 % |
| `controls/tabs switch` | 1.11 | 0.94 | −15 % |
| `a11y/create 1,000 rows, bridge on` | 3.83 | 3.35 | −13 % |
| `layout/baseline rows resize` | 1.39 | 1.22 | −12 % |

Nothing moved up past +2 %, and a resize step of the 2,000-row list allocates 9 KB instead of
4.2 MB. What is left, measured: the layout pass re-runs each container's flex phases 1–4 after
the measure pass ran them at the same inputs, about a fifth of that resize's layout pass.

**The layout pass takes the measure pass's flex resolution, before → after** (6df8757, with the
min-content width kept 0bf5823). The layout pass ran each container's flex phases 1 to 3.5 again —
every child's measurement looked up, grow and shrink, the re-measure, the baselines — at the inputs
the measure pass had just used. The measure pass now files what it resolved per nowrap container,
and the layout pass takes it when its width matches and its height is in the measurement's range:
on the 2,000-row resize every container but the root takes one. Shrinking rows also asked each
label for its widest word on every pass, shaped again each time; that width reads no constraint
and is now kept until the label changes. Every element box unchanged; minimum, median of 3 runs:

| Scenario | cc29f80 | 6df8757 | 0bf5823 | |
|---|---:|---:|---:|---:|
| `text/styled ellipsis 1,000 labels resize` | 12.52 | 9.90 | 6.47 | −48 % |
| `text/styled bidi ellipsis 1,000 labels resize` | 36.60 | 31.23 | 28.60 | −22 % |
| `layout/baseline rows resize` | 1.24 | 1.08 | 1.07 | −14 % |
| `layout/resize 2,000-row list` | 3.16 | 2.82 | 2.83 | −10 % |
| `a11y/create 1,000 rows, bridge on` | 3.59 | 3.20 | 3.26 | −9 % |
| `layout/shrink 300 overfull rows resize` | 3.80 | 3.70 | 3.66 | −4 % |

Styled ellipsis allocates 6.3 MB a step instead of 13.1 MB. The cost: a plan is filed for every
container measured, and a row's item laid out at its natural width — narrower than the width it
was measured at — never takes it: `layout/wrap 1,000 chips resize` spends 0.03 ms more in layout
(+1 % of its frame), `layout/deep nesting leaf edit` +3 %. Making those hit takes a measurement
that holds for a range of widths, as one now does for heights — which would also stop a resize
measuring again every chip whose width never changes.

**A measurement holds for a range of widths, before → after** (91f9e4b, with the layout pass
keeping a set-size box's layout 1b49032 and the entry packed back 98e0581). A resize offered every
box a new width, and every box was measured and laid out again at it — the row's 64 px id and its
24 px glyph box as much as the label that grows. A cache entry now keeps the widths its answer
holds for: any, for a box whose width is set; for one sized from its content, those its content
holds at, which for text on one line is any width it fits in (the pipeline says so from its
single-line fast path). A row holds where its children do and its items fit without growing, a
column where its children do. And the layout pass keeps a clean box's layout, whatever it is
offered, on an axis whose size is set. Every element box unchanged; minimum, median of 3 runs:

| Scenario | 4b4c733 | 98e0581 | |
|---|---:|---:|---:|
| `layout/deep nesting leaf edit` | 0.466 | 0.282 | −39 % |
| `layout/resize 2,000-row list` | 2.83 | 1.97 | −30 % |
| `layout/wrap 1,000 chips resize` | 2.01 | 1.77 | −12 % |
| `layout/baseline rows resize` | 1.07 | 0.94 | −12 % |
| `grid/resize 300x6` | 2.76 | 2.55 | −7 % |
| `grid/create 300x6` | 3.76 | 3.54 | −6 % |

The resize's layout pass went from 2.25 to 1.38 ms, the chips' from 0.33 to 0.08. At 32 bytes an
entry (1b49032) every element was 16 bytes larger, a 1,000-row component rebuild allocated 94 KB
more and ran 3–5 % slower; the height measured at went, and allocation is back to main's in every
row. Left: `icons/mount 600` and `controls/gallery mount`, fresh tiles every step, are 3 % slower
(layout +0.02 to +0.06 ms), and stay so with the ranges switched off — put down to the shape of
the measuring code, wrongly: see below.

**Mounting fresh tiles, before → after** (14477aa, with 021b665, a25d84a, ad09d1d and c451515).
Timing the mount regression over 200 steps a build pinned it on the width ranges, and switching
parts of them off moved it only a little; counting what layout did per pass found it. Both builds
did the same work — and the work was twice what it should be: icons/mount 600 took no flex plan
at all, before the width ranges or after, because a 20 px icon held its measurement for the one
height it was measured at, and so did its tile's column, which the layout pass then offered
another. A leaf whose height is set now holds at any height, as a container did; controls that
never read the height say so (`Element.IgnoresAvailableHeight`); and a width range's ends step in
only where rounding needs it, which every padless button laid out at its own width missed by one
step. Content measurements per pass, icons: 1,031 → 516; gallery 387 → 215, its plans taken
11 → 43 of 44 (32 with the rounding fix alone; the 44th is the root, which no parent measures).
Minimum, median of 3 runs:

| Scenario | ca425da | c451515 | | layout |
|---|---:|---:|---:|---|
| `icons/mount 600` | 1.934 | 1.737 | −10 % | 0.392 → 0.236 ms |
| `controls/gallery mount` | 1.292 | 1.208 | −6 % | 0.226 → 0.158 ms |

Nothing else moved past ±3 %. Against 4b4c733, before the width ranges, icons mount lays out in
0.223 ms where it took 0.320 (200-step runs). Random trees of these controls
(`LayoutPathIndependenceTests`) then turned up a box moved out past an unbounded edge, to near
1.7E+38, and back by the difference, its descendants landing at 0: a box whose place is past
2^24 is now laid out where it is (ad09d1d).

**A patch that changes nothing about the layout leaves it standing, before → after** (2688a99,
with 50925d0). Counting per pass found the component edits laying the whole list out: editing one
row of 1,000 measured 5,000 labels and made 6,003 ComputeLayout calls, none of them kept, because
`Element.PatchFrom` marked every element the rebuilt component patched for layout. It now marks one
only when a layout style changed, for the kinds of element that mark their own layout when the rest
of what they copy changes (Box, Text, Image, ScrollView): an edit measures the row it changed — 2
labels — and keeps the other 999. Minimum, median of 3 runs:

| Scenario | 4aa5653 | 2688a99 | | layout |
|---|---:|---:|---:|---|
| `component/edit one of 1,000 keyed rows` | 2.998 | 1.885 | −37 % | 1.212 → 0.064 ms |
| `component/edit one of 1,000 unkeyed rows` | 2.932 | 1.839 | −37 % | 1.197 → 0.061 ms |
| `component/reverse 1,000 keyed rows` | 3.010 | 1.973 | −34 % | 1.192 → 0.109 ms |

Nothing else moved past ±3 %, and the window benchmark, which builds no components, is unchanged.
What is left of an edit is the rebuild (1.4 ms) and the style pass over every patched element
(0.2 ms). `PatchRelayoutTests` patches random trees with random changes through the differ and
compares them with the changed trees built fresh; it found the layout cache keeping a box laid out
unforced once forced to the size it came out at, which offers its content different room — a kept
layout now needs the same forced sizes, exactly (50925d0).

**A forced width is the unforced measurement at that width, before → after** (8b2af12). Counting text
measurements per step: a 2,000-row resize measured every growing label again at each step, though
each fits on one line and so holds its measurement for any width it fits in — the cache keyed the
measurement on the exact forced width, a new one each step. For a leaf sized from its content a
forced width changes nothing but the width, so the forced measurement is now the unforced one at
that width, kept by its width range. Text measurements a step: resize 2,000 → 0, baseline rows
500 → 0, ratio tiles and grid 600 → 0, wrapped paragraphs 300 → 150, overfull rows 5,867 → 3,467.
Minimum, median of 3 runs:

| Scenario | da3c9d2 | 8b2af12 | | layout |
|---|---:|---:|---:|---|
| `layout/resize 2,000-row list` | 2.035 | 1.758 | −14 % | 1.440 → 1.165 ms |
| `layout/shrink 300 overfull rows resize` | 3.498 | 3.223 | −8 % | 2.276 → 2.031 ms |
| `layout/aspect-ratio tiles resize` | 0.814 | 0.776 | −5 % | 0.189 → 0.140 ms |
| `text/wrap 150 paragraphs resize` | 2.985 | 2.852 | −4 % | 1.980 → 1.846 ms |

Nothing moved up past +2 %. In the window benchmark create 1,000 is −10 % and append −7 %.

**A TextArea finds each line break from a guess at a line's length, before → after** (ae037d5).
Junction's Files view drew its first frame of TODO.md (90 KB, lines up to 1,658 characters) in
33 ms. To wrap a line the TextArea measured the whole rest of it, then bisected all of it, a new
substring at every step — a document of 900 lines measured 930,000 characters to open. The search
now starts where the line should end, from the width so far, and settles between a probe that fits
and one that does not, counting whole grapheme clusters; a line that fits is measured once, as
before. Minimum, median of 3 runs:

| Scenario | 5855df8 | ae037d5 | | allocated |
|---|---:|---:|---:|---|
| `text/open a long document in a TextArea` | 31.645 | 8.477 | −73 % | 41.3 → 13.5 MB |
| `input/type in 2,000-line TextArea` | 0.617 | 0.600 | −3 % | |
| `input/undo-redo in TextArea` | 0.615 | 0.617 | 0 % | |

Nothing else moved past ±5 %. In Junction (Native AOT, the Files walk of the frame log) TODO.md's
frame is 33.6 → 20 ms; what is left is measuring its lines at all, most of which carry an em dash
or an arrow and so take the BiDi path.

**Left-to-right text takes the plain path unless it can need BiDi, before → after** (2261567). Any
char at or above U+0590 sent its text down the BiDi path — the em dash in `text/create`'s
"Item n — m units" included, and with it twice the time and memory of the same labels with a
hyphen. Now only text that can need that path takes it: a char that can be right-to-left or steers
direction, or one that joins its neighbours into one character (surrogates, marks, joiners,
variation selectors, Hangul jamo). Dashes, quotes, arrows, symbols, CJK, kana and Hangul syllables go
plain. Minimum, median of 3 runs:

| Scenario | b33f5f1 | 2261567 | | allocated |
|---|---:|---:|---:|---|
| `text/create 2,000 unique labels` | 8.632 | 4.852 | −44 % | 14.9 → 7.6 MB |

Nothing else moved past ±4 %. In Junction TODO.md's lines all leave the BiDi path (219 of 918 took
it), and its first frame is 18.8–20.4 → 17.1–18.1 ms: what is left is measuring 89,000 characters
nobody has measured before.

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
