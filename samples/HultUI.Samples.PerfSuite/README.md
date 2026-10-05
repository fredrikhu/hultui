# Scenario performance suite

The framework's features one at a time, each timed the same way: build the UI in a
headless session, then time one operation from the mutation to the rendered frame,
with the frame split into **build / style / layout / render / other**.

It sits next to two narrower benchmarks:

| | What it covers | Where it runs |
|---|---|---|
| [`HultUI.Samples.Benchmark`](../HultUI.Samples.Benchmark/README.md) | One workload in depth: the js-framework-benchmark table, comparable to a browser | A real window, GPU, end to end |
| [`HultUI.Samples.TextBench`](../HultUI.Samples.TextBench/README.md) | The text pipeline, per call, in nanoseconds | No UI at all |
| **this suite** | The breadth: ~60 scenarios over every feature area | Headless, CPU raster, any OS |

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
| `layout/` | Resize drags over long lists, deep nesting, flex wrap, baseline alignment, aspect-ratio tiles; building 300 cards |
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
