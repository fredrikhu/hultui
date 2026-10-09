# Scenario performance suite

The framework's features one at a time, each timed the same way: build the UI in a
headless session, then time one operation from the mutation to the rendered frame,
with the frame split into **build / style / layout / render / other**.

It sits next to two narrower benchmarks:

| | What it covers | Where it runs |
|---|---|---|
| [`HultUI.Samples.Benchmark`](../HultUI.Samples.Benchmark/README.md) | One workload in depth: the js-framework-benchmark table, comparable to a browser | A real window, GPU, end to end |
| [`HultUI.Samples.TextBench`](../HultUI.Samples.TextBench/README.md) | The text pipeline, per call, in nanoseconds | No UI at all |
| **this suite** | The breadth: 94 scenarios over every feature area (88 off Windows) | Headless, CPU raster, any OS |

Use it to show that a change to one feature costs the others nothing, and to follow a
scenario over time.

```bash
dotnet run -c Release --project samples/HultUI.Samples.PerfSuite
dotnet run -c Release --project samples/HultUI.Samples.PerfSuite -- --filter grid,text/wrap
dotnet run -c Release --project samples/HultUI.Samples.PerfSuite -- --json captures/mine.json --label "my change"
dotnet run -c Release --project samples/HultUI.Samples.PerfSuite -- --compare captures/before.json captures/mine.json
dotnet run -c Release --project samples/HultUI.Samples.PerfSuite -- --verify
```

The full suite takes about 28 s; a filtered run at least 5 s, which is the pre-warm (see
below). `--list` prints every scenario, `--help` every option.

## Groups

| Group | What its scenarios time |
|---|---|
| `layout/` | Resize drags over long lists, deep nesting, flex wrap, baseline alignment, aspect-ratio tiles, overfull rows and columns (flex shrink); building 300 cards |
| `grid/` | Creating, resizing and editing a 1,800-cell grid with `auto` and `fr` columns |
| `text/` | Re-wrapping paragraphs, cold labels (shaping), ellipsis truncation, paint-only and metric rich-text spans; wrapping and truncation again over Hebrew mixed with Latin (`text/bidi …`), which the BiDi path lays out; long documents in a `TextArea`, among them Meridian's response viewer opening and wheeling a 19,000-line pretty-printed JSON response with its 23,000 colour spans (`… highlighted JSON …`; the spans are the ones HultUI.Syntax gives it, written out) |
| `scroll/` | Wheel and jumps through a 5,000-row list, sticky section headers, nested scroll chaining |
| `virtual/`, `table/` | `VirtualListElement` with 100,000 items; a key walk down 1,000 two-line rows whose selection the owning component's Build reads, as Junction's History is walked (`virtual/selection walk …`: each Down rebuilds the owner and the list realizes its rows again in the render pass); `TableView` with 10,000 rows: sort, wheel, column resize |
| `component/` | Keyed and unkeyed rebuilds of 1,000 rows, reverse, a small component in a big tree, 300 mounts, a signal read by 500 effects |
| `input/` | Hover, checkbox click, typing in `TextInput` and a 2,000-line `TextArea`, undo/redo, Tab through a form, drag to reorder |
| `richtext/` | The rich text editor on long documents: opening 5,000 blocks, a keystroke and Enter in them, the wheel, Ctrl+A, a resize of 1,000, a paragraph of 600 spans, a 200-cell table, and an editor that grows with its document inside a scroll view. The editor lays out inside its paint, so its layout shows under **render** |
| `overlay/` | Opening a popover, dialog, menu bar menu, dropdown, tooltip; filtering a command palette of 2,000 commands |
| `window/` | Opening a second window beside a busy one: its platform window, surface and tree under **build**, its first frame under style, layout and render (`Op.TimedWindow`) |
| `controls/`, `icons/` | Mounting a control screen, tabs, an animated expander, a slider drag, 600 icons |
| `theme/` | Light ⇄ dark, and cycling the `HultUI.Themes` catalogue, on the control screen |
| `anim/` | One frame of 60 looping tweens, of spinners and progress bars, and a hover transition |
| `paint/` | The same 400 tiles plain, rounded, shadowed, gradient-filled, transformed: the price of each effect |
| `a11y/` | Creates, edits, Tab and typing in a 5,000-block rich text document with an accessibility client attached: the tree build and diff, through a recording bridge; and the selection walk, whose list publishes a node for each of its 1,000 items on every rebuild. On Windows, six rows (`…, UIA`) put the real UI Automation bridge behind a checkbox click, a keystroke, a label edit, a Tab, a wheel turn and the selection walk, on a hidden window that never takes focus: its wrapper upkeep and the events it raises, under **other** (a Tab's focus event under **build**, where the key is handled). The wheel row and the selection walks space their frames 120 ms apart, past the 100 ms a scroll's accessibility refresh waits, so every step rebuilds |

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
| `2026-10-06-58f0abd-prefixes-before.json` | Before a TextArea read a line's widths off a table of its prefixes: main at 58f0abd with `input/type in a wrapped 900-line TextArea` added, median of 3 idle-gated runs … |
| `2026-10-06-5568977-prefixes-after.json` | … and after it, with long lines' breaks remembered across edits: 5568977, interleaved with the line above. Compare these two: 0 slower, 3 faster (the long document −43 %, typing in it −21 %). |
| `2026-10-07-2128054-richtext-before.json` | Before the rich text editor's stage 8 (docs/plans/16-rich-text-editor.md): 2128054 with the `richtext/` scenarios added, all 82, median of 3 idle-gated runs … |
| `2026-10-07-stage8-richtext-after.json` | … and after it, interleaved with the line above. Compare these two: 0 slower, 11 faster (see below). |
| `2026-10-08-9ae8f9e-textdraw-before.json` | Before text drawing stopped allocating per frame (plan 17): 9ae8f9e, median of 3 idle-gated runs … |
| `2026-10-08-9ae8f9e-textdraw-after.json` | … and after it, interleaved with the line above. Compare these two: 0 slower, 0 faster in time; allocation down ≥10 % on 43 rows (dense text 18.1 → 0.8 KB/op, typing in a `TextInput` 44.9 → 2.5). |
| `2026-10-08-9ae8f9e-textdraw-aot-before.json`, `…-aot-after.json` | The same pair as a NativeAOT publish, where the JIT cannot hide an allocation on the stack: allocation down ≥10 % on 44 rows (dense text 29.8 → 12.5 KB/op). |
| `2026-10-08-28b23fb-tracename-before.json`, `…-after.json` | Before and after the layout and render passes stopped building each element's trace name when no trace records (plan 17): 28b23fb, median of 3 idle-gated runs each, alternated. Compare these two: 0 slower, 0 faster; allocation −0.1 to −0.2 KB/op on the per-frame rows, because the JIT keeps `Type.Name`. |
| `2026-10-08-28b23fb-tracename-aot-before.json`, `…-aot-after.json` | The same pair as a NativeAOT publish (median of 2), where `Type.Name` builds a new string on every call: allocation down ≥10 % on 40 rows, none up (resize 2,000 rows 685.6 → 2.9 KB/op, typing in a `TextInput` 59.0 → 2.4, 400 tiles 7.7 → 0.7); 0 slower, 3 faster in time (resize 2,000 rows −16 %). |
| `2026-10-08-7096431-uiarebase-before.json`, `…-after.json` | Before and after the UIA bridge pointed every wrapper at the current tree after each diff, with no per-diff index (plan 17): 7096431, which adds the three `…, UIA` rows, median of 3 idle-gated runs each, alternated. Compare these two: 0 slower, 0 faster; allocation down on the three `…, UIA` rows and no other (a checkbox click 341 → 241 KB/op, a keystroke 211 → 164, a label edit in 1,000 rows 695 → 485). |
| `2026-10-08-7096431-uiarebase-aot-before.json`, `…-aot-after.json` | The `a11y/` rows of the same pair as a NativeAOT publish (median of 2): the same allocation to the byte, and the label edit −6 % in time. |
| `2026-10-08-2101bbf-framefloor-before.json`, `…-after.json` | Before and after the frame-floor series (plan 17): frame counters on rings, no delegate per loop pass, tree walks by index, Tab without a list or a sort, hover without a set, no closure per pointer move, sliders and number fields painted without allocating. 2101bbf, median of 3 idle-gated runs each, alternated. Compare these two: 0 slower, 0 faster; allocation down ≥10 % on 32 rows, none up (Tab through 300 fields 28.4 → 0.3 KB/op, light ⇄ dark 22.7 → 8.7, hover 1.2 → 0.2, tiles and dense text 0.6–0.7 → 0.0); component rebuilds −5.6 % in allocation and −5 to −6 % in time. |
| `2026-10-08-2101bbf-framefloor-aot-before.json`, `…-aot-after.json` | The same pair as a NativeAOT publish (median of 2): the same 32 rows down, none up, the same bytes as JIT after (Tab 34.1 → 0.3 KB/op); 0 slower, 3 faster in time (grid resize and edit −12 % in both runs, icons mount −13 % from one slow run before). |
| `2026-10-08-b277691-worddiet-before.json`, `…-after.json` | Before and after the text pipeline stopped making a string per word and a list per run (plan 17): word-cache lookups by a span of the text, glyphs into an array of the exact size, widths added up without glyphs, a kept list for a measurement's font runs, a full cache's evicted node reused. b277691, median of 3 idle-gated runs each, alternated. Compare these two: 0 slower, 1 faster (a long document in a `TextArea` −10 %); allocation down ≥10 % on 17 rows, none up (`text/ellipsis` 7,261 → 3,418 KB/op, `text/wrap` 3,093 → 1,572, `richtext/open 5,000 blocks` 18,172 → 11,211, `richtext/type in a paragraph of 600 spans` 1,924 → 776, `layout/cards create 300` 7,609 → 4,455); in time also `richtext/open` −26 % on the minimum, `text/wrap`, `text/ellipsis` and `text/styled ellipsis` −7 %, cards −6 %. |
| `2026-10-08-b277691-worddiet-aot-before.json`, `…-aot-after.json` | The same pair as a NativeAOT publish (median of 3): the same 17 rows down, to within 0.3 % of the JIT bytes, none up; 0 slower (largest +3 %), 0 faster past the flag (`richtext/open` −26 % on the minimum, cards −7 %, a long document in a `TextArea` −6 %). |
| `2026-10-08-e962738-textservices-before.json`, `…-after.json` | Before and after the windows of an application shared one set of text services, disposed with the application (plan 17): e962738, which adds `window/open a second window`, median of 3 idle-gated runs each, alternated. Compare these two: 0 slower, 1 faster (opening a second window −47 % on the minimum, 262 → 132 KB/op); allocation unchanged on every other row. |
| `2026-10-08-752ea95-a11ydiet-before.json`, `…-after.json` | Before and after the accessibility tree's build and diff stopped leaving garbage per node (plan 17): each node's children in one array of the exact length, leaves sharing the empty one; children walked by index; the differ pairing children by position while their ids agree; one owner-slot lookup per virtual list. 752ea95, median of 3 idle-gated runs each, alternated. Compare these two: 0 slower, 0 faster at the flag; allocation down on the four rows that rebuild a tree on every step and on no other (edit every 10th of 1,000 568 → 368 KB/op, a checkbox click with UIA 240 → 142, a keystroke with UIA 163 → 87, a label edit in 1,000 rows with UIA 483 → 283); in time the edit every 10th −10 % and the label edit with UIA −12 % on the minimum, and −9 % and −10 % in a filtered rerun of 4 pairs. |
| `2026-10-08-752ea95-a11ydiet-aot-before.json`, `…-aot-after.json` | The `a11y/` rows of the same pair as a NativeAOT publish (median of 3): the same bytes as JIT to 0.3 KB, the edit every 10th −10 % and the label edit with UIA −8 % on the minimum. |
| `2026-10-08-93847ae-focusmove-before.json`, `…-after.json` | Before and after a focus move updated the focus state at once (plan 17): 93847ae, which adds `a11y/tab through 300 fields, UIA` and `a11y/wheel 1,000 rows, UIA`; the `a11y/` rows and `input/tab` only, median of 3 idle-gated runs each, alternated. Compare these two: 0 slower, 0 faster, allocation unchanged on every row (Tab with UIA 0.8 KB/op, which now raises two HasKeyboardFocus events with its focus event: 0.351 → 0.350 ms). |
| `2026-10-08-70d68c1-scrollevent-before.json`, `…-after.json` | Before and after a scroll became the scroll view's change, as browsers report it (plan 17): one ScrollPercent event on the view per rebuild in place of a BoundingRectangle event per node on screen. 70d68c1; the `a11y/` and `scroll/` rows only, median of 3 idle-gated runs each, alternated. Compare these two: 0 slower, 1 faster (`a11y/wheel 1,000 rows, UIA` 1.344 → 0.541 ms, −60 %, 433.5 → 425.1 KB/op); every other row within ±4 %. Tab with UIA reads 0.3 → 0.4 KB/op in this pair and 0.3 on both builds in a run of the UIA rows alone: the JIT, not the change. |
| `2026-10-08-43c0e05-clampbasis-before.json`, `…-after.json` | Before and after a one-line truncating label in a row took its untruncated width, capped at the room, as its flex base size, instead of being truncated at the room for it (plan 17): 43c0e05, median of 3 idle-gated runs each, alternated. Compare these two: 0 slower, 5 faster, the five ellipsis rows, which truncate each label once a step instead of twice (`text/ellipsis` −30 %, 3,418 → 1,887 KB/op; `text/multi-font ellipsis` −42 %, 7,949 → 3,848; `text/styled bidi ellipsis` −30 %, 10,654 → 7,061); allocation unchanged on every other row. |
| `2026-10-08-43c0e05-clampbasis-aot-before.json`, `…-aot-after.json` | The same pair as a NativeAOT publish (median of 3): the same five rows faster (`text/ellipsis` −34 %, `text/multi-font ellipsis` −43 %) with the same bytes as JIT to 0.3 %, none slower (no row up by 4 % or more). |
| `2026-10-08-6891682-minzero-before.json`, `…-after.json` | Before and after a one-line truncating label's min-content width became 0, so it may shrink to nothing rather than to its widest word (plan 17; a correctness change): 6891682, median of 3 idle-gated runs each, alternated. Compare these two: 0 slower, 0 faster. `text/spans bold` reads +7 % on the minimum (+2 % median), untouched by the change: a filtered rerun of 4 alternated pairs reads 1.421 → 1.411 ms. Allocation: `text/ellipsis` 1,887 → 1,859 KB/op (its labels' widest words are no longer shaped), the rest within noise. |
| `2026-10-08-6891682-minzero-aot-before.json`, `…-aot-after.json` | The same pair as a NativeAOT publish (median of 3): 0 slower, 0 faster, no row up by 5 % or more; `text/ellipsis` 1,889 → 1,860 KB/op. |
| `2026-10-09-1c8ee98-scenarios12.json`, `…-aot.json` | The five rows 1c8ee98 adds (plan 17 item 12: the selection walks and the highlighted JSON response), JIT and NativeAOT, 2 idle-gated runs each, min the lower and the rest their mean. Only those rows. The "before" for the round 2 items that change them; see below. |
| `2026-10-09-ff7a970-sdlloop-before.json`, `…-after.json` | Before and after the SDL platform's event pump stopped making a closure on every pass of the loop and its short waits parked for their whole timeout on Windows (plan 17, round 2): 059316a and ff7a970, 2 idle-gated runs each, alternated, min the lower and the rest their mean. The suite runs headless, where neither change reaches: 0 slower, 0 faster, allocation unchanged on every row (the largest move −0.2 %). What they change, in a real window, is in commits 0ceecea and ff7a970: 88 B a loop pass → 0, and ~240 passes a frame → 2.1 while animating. |

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

**A TextArea reads a line's widths off one table of its prefixes, before → after** (5568977). The
break search measured three or so prefixes of each wrapped line, each a new string laid out whole
and cached. `IRenderContext.MeasureTextPrefixes` gives every prefix's width from one itemizing of
the text and the word cache, to the bit what measuring each would give. Built anew where the old
probes found the caches warm, the tables made typing in a document of long paragraphs 1.0 → 3.1 ms
(a scenario added for it), so the TextArea also remembers where each long line broke, by its text,
and an edit searches only the line it changed. Minimum, median of 3 runs:

| Scenario | 58f0abd | 5568977 | | allocated |
|---|---:|---:|---:|---|
| `text/open a long document in a TextArea` | 8.152 | 4.646 | −43 % | 13.5 → 7.1 MB |
| `input/type in a wrapped 900-line TextArea` | 1.022 | 0.812 | −21 % | 0.93 → 0.49 MB |
| `input/undo-redo in TextArea` | 0.604 | 0.614 | +2 % | 469 → 484 KB |

Nothing else moved past ±3 % but `icons/mount 600` (−3 %). Undo and redo swap one long line between
two texts and the memo keeps only the current one, so each step builds that line's table again.
Junction's TODO.md frame does not move (17.3–17.6 → 16.8–17.4 ms): 900 of its 917 lines fit, and
each is measured once, whole, as before.

**Two app workloads** (1c8ee98). The app survey of plan 17 found two costs no row timed: a key
walk down a list whose owner rebuilds on each press (Junction's History, Commit and Files views,
Vesper's panes), and Meridian opening a large JSON response. Minimum, KB a step, JIT / AOT:

| Scenario | min ms | render (median) | other (median) | KB/op |
|---|---:|---:|---:|---:|
| `virtual/selection walk 1,000 rows` | 0.466 / 0.482 | 0.48 / 0.49 | 0.00 | 108 |
| `a11y/selection walk 1,000 rows, bridge on` | 0.534 / 0.582 | 0.48 / 0.49 | 0.07 / 0.10 | 412 / 411 |
| `a11y/selection walk 1,000 rows, UIA` | 0.565 / 0.623 | 0.48 / 0.50 | 0.11 / 0.15 | 412 / 411 |
| `text/open highlighted JSON in a TextArea` | 10.74 / 12.49 | 20.3 / 23.8 | 0.01 | 10,811 |
| `text/wheel highlighted JSON in a TextArea` | 0.939 / 1.556 | 0.98 / 1.61 | 0.00 | 14 / 16 |

- **The walk's rebuild is cheap; its rows are not kept.** The pane rebuilds in 0.01 ms, and the
  list, patched, throws its 19 realized rows away and makes them again from the template while it
  paints: 95 elements a step, most of the 108 KB. A temporary probe round the list's realization
  put it at ~0.05 ms of the 0.48 ms render (~10 %; the rest is CPU raster). The app survey
  measured 12–19 % in Junction and Vesper, whose rows are heavier than this one's five elements.
- **An accessibility client adds ~300 KB and 0.07 ms a step** (UIA 0.11 ms): the tree rebuilt with
  a node, and a label string, for each of the 1,000 items, on screen or not — about 300 B an item.
  12 % of the step, 18 % with UIA; the survey measured 13–30 %.
- **Opening the response measures each of its 19,013 lines once** (a substring and a layout each),
  10.8 MB a step on both runtimes. The median is twice the minimum: the same three of the five
  timed steps of every round take a garbage collection, 7–16 ms longer than the steps without
  one (why a collection costs that much here is not profiled).
- **The wheel is the span scan.** Each of the ~50 lines drawn walks the response's 22,968 spans
  from the first, through an interface enumerator, to the ones on its line: ~11,500 a line in the
  middle of the response. AOT reads 66 % slower than JIT here; dynamic PGO can devirtualize that
  enumerator and AOT cannot (not profiled).

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

**The rich text editor, before → after stage 8.** The plan named a linear scan of a long
paragraph's spans as the risk; profiled, it came third. Typing in a paragraph of 600 spans
spent most of its layout asking Skia, a character at a time, whether a face had a glyph, and
most of its paint in three passes over every span for every line. A face now remembers its
answers for the Basic Multilingual Plane (two bits a character), the span lookups binary-search,
and the editor lays out only the blocks an edit replaced, draws only what the clip leaves, and
measures an auto-height editor from that same layout:

| Scenario | before (min) | after | |
|---|---:|---:|---:|
| `richtext/type in a paragraph of 600 spans` | 19.24 ms | 2.69 ms | −86 % |
| `richtext/type in an auto-height editor` | 3.35 ms | 0.81 ms | −76 %, alloc −98 % |
| `richtext/type in 5,000 blocks` | 1.00 ms | 0.87 ms | −14 %, alloc −73 % |
| `a11y/type in rich text, bridge on` | 1.57 ms | 1.14 ms | −27 %, alloc −52 % |
| `text/open a long document in a TextArea` | 8.73 ms | 6.33 ms | −27 % |
| `text/multi-font ellipsis 1,000 labels resize` | 10.23 ms | 8.19 ms | −20 % |
| `text/wrap 150 paragraphs resize` | 2.85 ms | 2.38 ms | −17 % |

The `text/` rows are the glyph answers: every label is itemized. Blobs are now kept by the
glyphs they draw rather than by run, so a paragraph laid out again keeps the blobs of its
unchanged lines; the hashing that costs every drawn run shows as `paint/dense text screen`
+1 %, within noise. The 600-span row reads 7.4 ms before in a filtered run and 19 ms in the full
suite — the glyph calls cost more there — and 2.6 ms after in both.

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
