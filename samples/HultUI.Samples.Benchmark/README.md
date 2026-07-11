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
=== HultUI benchmark: running suite ===
  create 1,000          median    81.4 ms   min   74.8 ms   (n=5)
  update every 10th     median    29.9 ms   min   27.2 ms   (n=5)
  select row            median     2.5 ms   min    2.4 ms   (n=5)
  swap rows             median    22.2 ms   min   21.9 ms   (n=5)
  remove row            median    95.0 ms   min   33.7 ms   (n=5)
  create 10,000         median   806.9 ms   min  797.3 ms   (n=5)
  append 1,000          median   563.1 ms   min  559.6 ms   (n=5)
  clear 10,000          median   158.4 ms   min  153.0 ms   (n=5)
=== done ===
```

(Numbers above are illustrative — they depend entirely on the machine and GPU.
Run the web benchmark on the *same machine* to get a meaningful comparison.)

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
- **One place HultUI is currently disadvantaged:** browsers cull *painting* to
  the visible viewport, but HultUI's renderer today walks the entire scroll
  content every paint (Skia's clip rejects the off-screen draw calls cheaply, but
  the per-element traversal still runs). So the render portion of the large
  `create`/`append` numbers is inflated relative to a browser. This is a real,
  known gap — viewport paint culling is a worthwhile follow-up, and this
  benchmark is exactly what makes it measurable. (For comparison, the
  `VirtualList` sample *does* only materialize visible rows.)
- **Machine-dependent:** GPU, driver, VSync, and display scale all move these
  numbers. Only compare runs captured on the same machine.
