# HultUI Benchmark

A HultUI implementation of the classic
[js-framework-benchmark](https://github.com/krausest/js-framework-benchmark)
table benchmark — the one every web UI framework (React, Vue, Svelte, SolidJS,
vanilla JS, …) reports numbers for. It exists so HultUI's performance can be
compared, operation for operation, against a browser running the same workload.

## What it does

It builds a **real, non-virtualized table**: one element subtree per row, just
like a browser materializes one DOM subtree per `<tr>`. Then it times each
standard operation *end to end* — from the state mutation to the resulting frame
being painted on screen — and reports the milliseconds.

| Operation           | What it measures                                    |
| ------------------- | --------------------------------------------------- |
| `create 1,000`      | Replace the table with 1,000 freshly built rows     |
| `create 10,000`     | Replace the table with 10,000 freshly built rows    |
| `append 1,000`      | Add 1,000 rows to an already-large table            |
| `update every 10th` | Append `" !!!"` to every 10th row's label (in place)|
| `select row`        | Highlight a row (in place)                           |
| `swap rows`         | Swap row 1 and row n-2                               |
| `remove row`        | Delete a single row                                  |
| `clear`             | Remove every row                                     |

The row data (`adjective colour noun` labels, monotonic ids) uses the exact word
lists from js-framework-benchmark, so the workload matches.

The default backend is the non-virtualized table (the js-framework-benchmark
workload). Passing `--virtual` runs the identical operations against a
`VirtualListElement` — same measurement, but only the visible rows are ever
materialized. See the three-way comparison below.

## Running it

Interactive — click the toolbar buttons, watch the per-operation latency in the
left panel, and the live FPS / frame-time HUD in the top-right:

```
dotnet run --project samples/HultUI.Samples.Benchmark
```

Scripted — run the full suite once (warm-up + repeats), print a summary table to
the console, and exit. This is the number to compare against a browser:

```
dotnet run --project samples/HultUI.Samples.Benchmark -- --auto
```

```
=== HultUI benchmark (non-virtualized): running suite ===
  create 1,000           median   59.1 ms   min   46.5 ms   (n=5)
  update every 10th      median   14.4 ms   min   14.2 ms   (n=5)
  select row             median    0.9 ms   min    0.9 ms   (n=5)
  swap rows              median   12.1 ms   min   11.5 ms   (n=5)
  remove row             median   83.8 ms   min   22.1 ms   (n=5)
  create 10,000          median  528.8 ms   min  486.6 ms   (n=5)
  append 1,000 to 1,000  median   70.1 ms   min   63.5 ms   (n=5)
  clear 1,000            median   16.2 ms   min   15.9 ms   (n=5)
=== done ===
```

The suite's operation *parameters* match js-framework-benchmark exactly — including
that "append rows to large table" appends 1,000 to a table of **1,000** rows, and
"clear rows" clears a table of **1,000** — so every row above lines up with a web
benchmark entry.

Add `--virtual` to run the **same operations against a `VirtualListElement`** —
which only materializes the visible rows — instead of a real table:

```
dotnet run --project samples/HultUI.Samples.Benchmark -- --auto --virtual
```

This is *not* a valid js-framework-benchmark entry (the benchmark forbids
virtualization), but it shows what a real app with a huge list actually gets —
see the comparison below.

(Numbers here are illustrative — they depend entirely on the machine and GPU.
Run the web benchmark on the *same machine* to get a meaningful comparison.)

## Non-virtualized vs virtualized vs browser

Same operations, three ways, all on one machine (browser = keyed `vanillajs` in
Chromium — the hand-written web baseline; medians, ms):

| Operation             | Browser (vanilla) | HultUI table | HultUI virtual |
| --------------------- | ----------------: | -----------: | -------------: |
| create 1,000          |                31 |           59 |            6.8 |
| update every 10th     |              17.5 |           14 |            1.5 |
| select row            |               6.3 |          0.9 |            1.1 |
| swap rows             |              18.5 |           12 |            1.2 |
| remove row            |              16.1 |     84 (min 22) |          1.3 |
| create 10,000         |               325 |          529 |            1.7 |
| append 1,000 → 1,000  |                35 |           70 |            9.9 |
| clear 1,000           |              13.9 |           16 |            9.3 |

Reading it:

- **Browser vs HultUI table** is the apples-to-apples comparison — both build and
  lay out every real row. HultUI is competitive on in-place ops (and faster on
  `select`, thanks to paint culling), ~1.6× the browser on bulk `create` (layout
  bound; see below).
- **HultUI virtual** collapses everything to low single-digit milliseconds and,
  crucially, **stops scaling with row count**: `create 10,000` is ~1.7 ms because
  layout and paint are O(visible ≈ 20 rows), not O(10,000). The only O(n) cost
  left is building the lightweight data models (~0.7 ms + ~1 MB for 10k). That is
  what a real 10k-row screen should use.
- The virtual column is **not comparable to the browser numbers** — the web
  benchmark deliberately measures the non-virtualized case. It's here to show the
  cost of the workload the benchmark *chooses* to measure vs. what an app pays in
  practice.

## How the measurement works

HultUI's whole pipeline — component rebuild → style → layout → render → present —
runs synchronously on the UI thread inside one frame. The harness times from just
before the mutation to just after that frame is presented, using
`Window.RequestPostFrameCallback`, which fires once the frame is on screen. That
span is the same "click → painted" interval the web benchmark records with
`PerformanceObserver`.

The `--auto` suite gives each measured operation its own dedicated frame, runs a
couple of warm-up iterations first, forces a GC between metrics, and reports the
**median** of 5 runs — matching how js-framework-benchmark isolates its runs.

## Reading the comparison fairly

- **Apples to apples:** the table is *not* virtualized. Both HultUI and the
  browser build and lay out every row, so `create`/`clear`/`append` genuinely
  stress the layout of thousands of real elements — that's the point.
- **Paint is culled to the viewport; layout is not.** Like a browser, HultUI
  skips painting rows scrolled out of view (`ElementRenderer` prunes subtrees
  whose on-screen box falls outside the scroll clip). So an in-place op like
  `select` re-paints only what's visible — ~1 ms regardless of how many rows
  exist. What remains O(total rows) is *layout*: `create`/`append`/`clear` still
  build and lay out every row — exactly as a browser reflows the whole table —
  so those numbers are layout-bound, which makes them a fair layout-vs-layout
  comparison rather than a paint artefact.
- **Machine-dependent:** GPU, driver, VSync, and display scale all move these
  numbers. Only compare runs captured on the same machine.

## Which web framework to compare against

The web benchmark has both hand-written and framework entries, and the right
comparison depends on how the *HultUI* side is written:

- **This sample is hand-written** — it holds a `List<RowView>` and mutates the
  element tree imperatively via `ReplaceChildren`. It does **not** go through
  HultUI's reactive path (`Component` + `[Reactive]` state + declarative
  `Build()`), so it never pays for component re-execution or tree
  reconciliation. That makes its natural counterpart the **keyed `vanillajs`**
  entry — also hand-written, no framework overhead. Compare against that.

- **Idiomatic HultUI would sit higher.** A real app writes a reactive
  `Component` whose `Build()` returns a fresh declarative tree on every state
  change; HultUI then reconciles it against the live tree with a keyed differ
  (`HultUI.Core/Components/TreeDiffer.cs`) — the same shape of work React's
  virtual-DOM reconciliation does. A version of this benchmark written that way
  would add that reconciliation cost on top of the raw layout/paint measured
  here, and its fair counterpart is **`react`** (or somewhere between vanilla
  and react, depending on how much the differ can patch in place vs. rebuild).

So: today's numbers vs. `vanillajs` isolate HultUI's *rendering engine* cost;
a reactive-component variant vs. `react` would isolate its *reconciler* cost.
Having both is the most honest picture — the same reason js-framework-benchmark
keeps vanilla and framework entries side by side.
