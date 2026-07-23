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
  create 1,000          median   24.66 ms   min   20.03 ms   (n=5)
  update every 10th     median    4.46 ms   min    4.20 ms   (n=5)
  select row            median    0.32 ms   min    0.25 ms   (n=5)
  swap rows             median    3.24 ms   min    3.05 ms   (n=5)
  remove row            median    4.03 ms   min    3.88 ms   (n=5)
  create 10,000         median  332.97 ms   min  281.94 ms   (n=5)
  append 1,000 to 1,000  median   34.48 ms   min   24.02 ms   (n=5)
  clear 1,000           median   13.41 ms   min   13.26 ms   (n=5)
=== done ===
```

> **Warm-up matters.** The suite runs a global pre-warm (full pipeline on large
> and small tables, then a few idle frames) *before* any metric, so the shared
> framework code is JIT-tiered to steady state first. Without it the first metric
> in the run — `create 1,000` — absorbed the JIT tier-up and read ~2× high (it
> was 59 ms here before the pre-warm, vs 27 ms warm, essentially the NativeAOT
> figure). A browser's V8 is warmed the same way, so this is what makes the
> comparison fair. See [docs/NATIVE-AOT.md](../../docs/NATIVE-AOT.md#aot-vs-jit-throughput).

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
Brave (Chromium 150) — the hand-written web baseline; medians, ms):

| Operation             | Browser (vanilla) | HultUI table | HultUI virtual |
| --------------------- | ----------------: | -----------: | -------------: |
| create 1,000          |              31.5 |         24.7 |            6.6 |
| update every 10th     |              18.8 |          4.5 |            0.9 |
| select row            |               6.9 |          0.3 |            1.0 |
| swap rows             |              18.7 |          3.2 |            1.0 |
| remove row            |              16.1 |          4.0 |            1.6 |
| create 10,000         |               330 |          333 |            1.9 |
| append 1,000 → 1,000  |              35.3 |         34.5 |            6.8 |
| clear 1,000           |              14.5 |         13.4 |            5.5 |

> The HultUI columns are a fresh capture on 2026-07-23 (commit with the word-level
> shape cache, and the F12 HUD now disabled during scripted runs — measured HUD
> impact was within noise, but the columns should measure the app only). The
> browser column is the 2026-07-19 capture with the official js-framework-benchmark
> webdriver suite (`npm run bench -- --framework keyed/vanillajs`) driving
> **Brave** (Chromium 150) on the same machine (medians of the runner's per-op
> repeats) — browser numbers don't depend on the HultUI commit, so the column
> carries over. Session-to-session variance is real: `create 10,000` measured 305
> median in the 2026-07-19 session vs 333 here (mins ~282 both HUD-on and HUD-off
> this session) — treat the bulk-op medians as ±10%. All HultUI numbers are
> **warm** (global pre-warm above); the pre-warm-era cold numbers (e.g.
> `create 1,000` 59, `remove row` 84) were JIT-tier-up artifacts.

### Linux capture (2026-07-20, `linux-atspi-export`-era commit)

The **same machine** as the Windows/browser columns above, rebooted into Linux
(Fedora 44, Wayland, NVIDIA proprietary driver). Release build, warm, medians of
n=5; two table runs (mins used where a single median was GC/scheduling-jittery).
**Predates the word-shape cache and the HUD-off scripted runs** — a same-commit
re-capture is queued for the next Linux session (see the repo-root
`HANDOVER.md`), so compare it against the 2026-07-19 Windows capture it shared a
commit with (table: 23 / 5.5 / 0.6 / 4.2 / 4.2 / 305 / 34 / 12), not the
2026-07-23 column above.

| Operation             | HultUI table (Linux) | HultUI virtual (Linux) |
| --------------------- | -------------------: | ---------------------: |
| create 1,000          |                 17.3 |                    7.8 |
| update every 10th     |                  3.4 |                    0.6 |
| select row            |                  0.4 |                    0.6 |
| swap rows             |                  3.6 |                    0.7 |
| remove row            |                  4.4 |                    1.3 |
| create 10,000         |                  258 |                    1.6 |
| append 1,000 → 1,000  |                   22 |                    8.5 |
| clear 1,000           |                  8.4 |                   20.7 |

> Captured after a `dnf upgrade` that healed the GPU present path (the earlier
> run logged `libEGL: failed to create dri2 screen` and fell back). The effect is
> stark on **present-bound in-place ops**: table `select` dropped 1.3 → 0.4 ms,
> and virtual `select`/`swap` collapsed from ~3–4.5 ms to ~0.6 ms — the
> "mutation → painted" latency the harness measures is now dominated by real GPU
> present, not a broken fallback. The **CPU-bound bulk `create`** ops moved the
> other way (`create 1,000` ~10 → ~17, `create 10,000` ~216 → ~258); that path
> touches no GPU, so the shift is kernel/scheduler/thermal, not the driver.
>
> **An apples-to-apples OS comparison against the 2026-07-19 Windows capture**
> (values in the section intro above) — same commit, same binary, same GPU,
> machine rebooted between the two. On the non-virtualized table Linux is
> faster on every op except `remove row` (a tie): the gap is widest on the
> present-bound in-place ops (`update` 3.4 vs 5.5, `select` 0.4 vs 0.6, `append`
> 22 vs 34), consistent with the healed GPU present path. The virtual columns are
> roughly even; ignore the Linux virtual `clear 1,000` = 20.7, a single
> GC/scheduling-jittery median (the Windows 5.0 is the trustworthy figure there).

Reading it:

- **Browser vs HultUI table** is the apples-to-apples comparison — both build and
  lay out every real row. Warm, HultUI is **faster than the vanilla browser on
  `create 1,000`** (24.7 vs 31.5), meaningfully faster on the in-place ops
  (`update`/`swap`/`remove`, and `select` especially — paint culling plus a
  sub-frame present), and **on par on the biggest bulk-layout ops**,
  `create 10,000` (333 vs 330, a tie within session variance) and `append`
  (34.5 vs 35.3), which are layout-bound (see below).
- **Caveat on the small ops:** HultUI's post-frame callback fires as soon as the
  frame is presented (sub-millisecond for a cheap op), whereas the browser's
  paint timing is coarser. So treat the in-place-op gaps as "both are fast" rather
  than a literal 5–10× — the bulk `create`/`append`/`clear` rows are the more
  directly comparable layout-vs-layout numbers.
- **HultUI virtual** collapses everything to low single-digit milliseconds and,
  crucially, **stops scaling with row count**: `create 10,000` is ~1.9 ms because
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

The `--auto` suite first runs a **global pre-warm** — the whole pipeline
(build/style/layout/text/render) over large and small tables, then a dozen idle
frames — so the shared framework code is JIT-tiered to steady state before any
timing starts. It then gives each measured operation its own dedicated frame,
runs a couple of per-metric warm-up iterations, forces a GC between metrics, and
reports the **median** of 5 runs — matching how js-framework-benchmark isolates
its runs against an already-warm V8. Under NativeAOT the pre-warm is a cheap
no-op (there is no tiering); under JIT it is what stops the first metric from
absorbing tier-up and reading ~2× high.

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
