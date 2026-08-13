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
  create 1,000          median   11.66 ms   min    6.43 ms   (n=5)
  update every 10th     median    2.82 ms   min    2.75 ms   (n=5)
  select row            median    0.29 ms   min    0.28 ms   (n=5)
  swap rows             median    1.71 ms   min    1.60 ms   (n=5)
  remove row            median    2.58 ms   min    2.36 ms   (n=5)
  create 10,000         median  177.83 ms   min  131.89 ms   (n=5)
  append 1,000 to 1,000  median   17.70 ms   min    8.06 ms   (n=5)
  clear 1,000           median    0.56 ms   min    0.37 ms   (n=5)
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
| create 1,000          |              31.5 |         11.9 |            1.5 |
| update every 10th     |              18.8 |          2.9 |            1.0 |
| select row            |               6.9 |         0.31 |            1.2 |
| swap rows             |              18.7 |          1.7 |            1.0 |
| remove row            |              16.1 |          2.6 |            1.4 |
| create 10,000         |               330 |          177 |            1.7 |
| append 1,000 → 1,000  |              35.3 |         20.2 |            1.8 |
| clear 1,000           |              14.5 |          0.5 |            0.7 |

> The HultUI columns are a fresh capture on 2026-08-13 (idle machine, medians of
> two suite runs), the first after the **multi-entry measure cache** below — the
> in-place ops carry its 2–3.5× (`update` 4.8 → 2.9, `swap` 3.7 → 1.7, `remove`
> 4.4 → 2.6 vs the 2026-07-23 capture). Bulk creates moved less vs July
> (`create 10,000` 165 → 177, `append` 18 → 20): a same-day run of the
> pre-cache commit sat well *above* the July column (create-10k 232, swap 6.3).
> Bisected 2026-08-13: that interim regression was f4389c3 ("Measure a flex
> item's cross size at the width it ends up with", 2026-08-11) — a correctness
> fix for flex-basis-0 items measured at zero width, whose remeasure pass both
> added measure work and thrashed the then-single-slot measure cache (swap
> 3.2 → 6.7 ms at that commit). The multi-entry cache below repaid it with
> interest on warm trees; the few ms left on the bulk creates is that fix's
> honest cold-tree cost. The cache's own effect is the interleaved same-commit
> comparison below, not the July delta. Note this machine runs a background UIA client, so the
> HultUI columns *include* an active accessibility bridge (tree build + diff
> each frame — worth a few ms on the bulk ops); a browser typically benches
> with accessibility off. The browser column is the 2026-07-19 capture with the
> official js-framework-benchmark webdriver suite (`npm run bench -- --framework
> keyed/vanillajs`) driving **Brave** (Chromium 150) on the same machine
> (medians of the runner's per-op repeats) — browser numbers don't depend on the
> HultUI commit, so the column carries over. Treat bulk-op medians as ±10%
> session variance. All HultUI numbers are **warm** (global pre-warm above); the
> pre-warm-era cold numbers (e.g. `create 1,000` 59, `remove row` 84) were
> JIT-tier-up artifacts. The 2026-07-23 capture this replaces, for history:
> table 12.3 / 4.8 / 0.5 / 3.7 / 4.4 / 165 / 18.0 / 0.5, virtual 1.4 / 0.9 /
> 0.9 / 1.0 / 1.4 / 1.7 / 1.8 / 0.7 — it also had the word-level shape cache,
> UIA coalescing, and the F12 HUD disabled during scripted runs.

> **2026-08-13 — multi-entry measure cache** (`MeasureCache.cs`, commit 8805531):
> the layout engine's per-element measure memo went from one slot (which thrashed
> under flex's alternating measure inputs) to a 4-slot ring, so clean rows now keep
> their measured size across frames. Same-machine interleaved before/after medians
> (machine under load, so ratios are the signal, not the absolutes): **in-place ops
> 2–3.5× faster** — `swap` 6.9 → 2.0 ms, `remove` 7.5 → 3.1 ms, `update` 8.1 →
> 4.0 ms — and the bulk ops 20–40% (`append` 32.7 → 19.6 ms, `create 10,000`
> −23%); `clear` and `select` unchanged. (These interleaved same-commit ratios are
> the cache's isolated effect; the columns above were re-recorded on an idle
> machine the same day.)

### Linux capture (2026-07-23, same commit as the Windows column + AT-SPI coalescing)

The **same machine** as the Windows/browser columns above, rebooted into Linux
(Fedora 44, **KDE Plasma / Wayland**, NVIDIA proprietary 610.43.03, RTX 4080).
Release build, warm, medians of n=5. This is the same-commit re-capture the old
2026-07-20 section was waiting for — and it settled the "why is the Linux
benchmark slower?" question the repo-root `HANDOVER.md` was opened to chase.

**The whole gap was the accessibility bridge, not the framework or the GPU.** On
any live desktop the AT-SPI bus is reachable, so HultUI's Linux a11y bridge
eager-connects and does per-frame work during the benchmark — whereas a headless
CI box (no bus) benches a11y-free. The Windows column likewise runs with an
active (background-UIA) bridge. So the honest comparison needs the a11y state
pinned; the new **`HULTUI_A11Y=0`** master switch (works on every platform) does
that. Three columns, one machine:

| Operation             | Linux, a11y active (coalesced) | Linux, `HULTUI_A11Y=0` (framework floor) | Windows, a11y active (coalesced) |
| --------------------- | -----------------------------: | ---------------------------------------: | -------------------------------: |
| create 1,000          |                           17.9 |                                      5.3 |                             12.3 |
| update every 10th     |                            3.9 |                                      2.2 |                              4.8 |
| select row            |                           0.38 |                                     0.21 |                              0.5 |
| swap rows             |                            3.4 |                                      2.0 |                              3.7 |
| remove row            |                            4.4 |                                      3.0 |                              4.4 |
| create 10,000         |                     218 (min 204) |                            126 (min 110) |                              165 |
| append 1,000 → 1,000  |                             24 |                                     13.5 |                             18.0 |
| clear 1,000           |                           2.85 |                                     0.15 |                              0.5 |

> **Framework CPU: Linux is faster than Windows.** Compare the floors — Linux
> `HULTUI_A11Y=0` create-10,000 is **110–126 ms** vs the Windows bridge-off floor
> of **146 ms** (see the coalescing commit) — so the `.NET` + framework path is
> genuinely quicker on Linux, confirming the handover's premise. The in-place ops
> agree (Linux floors sit at/under Windows).
>
> **With a11y active, Linux `create`/`append` cost more, and that's a protocol
> difference, not a regression.** AT-SPI is *push*-based: every added node is sent
> to the client's cache via one `Cache.AddAccessible` signal (real work that makes
> screen-reader navigation instant), whereas UIA is *pulled* on demand, so Windows
> pays nothing per node on a create. Even after the coalescing below, Linux
> create-10,000 keeps that ~60 ms cache-push cost. It is the right cost to keep.
>
> **The event coalescing this capture includes.** The Linux bridge used to emit
> one `object:children-changed` per added/removed node too — pure redundancy on a
> bulk change (the per-node cache signals already carry the node set, and a client
> re-reads a parent's children from the fresh snapshot). It now coalesces to **one
> children-changed per affected parent**, verified against a live `pyatspi` client
> (Orca's stack) that tracked a 0⇄200 cycle exactly while receiving one signal per
> bulk op. Effect: table `clear 1,000` **9.3 → 2.85 ms**, `create 10,000` shed its
> ~27 ms children-changed storm (down to the cache-push floor), and the old
> virtual `clear` jitter (20.7 ms) is gone (now 3.4 ms).
>
> **GL / present: the ~2× gap the handover chased no longer reproduces.** A 5 s
> continuous-frame Junction trace (~15 k frames, `HULTUI_TRACE_MS`) gives
> steady-state **Render 0.18 ms** (median; the handover measured 1–1.5 ms),
> **Flush 0.09 ms**, **Present 0.02 ms** — Present nowhere near a 16.6/8.3 ms
> vsync wait, so `swapInterval=0` genuinely holds, and Render now matches the
> Windows reference (0.2–0.3 ms). ~2,990 fps uncapped. The earlier gap was the
> broken `dri2`/EGL fallback the old note mentioned; a driver/stack update healed
> it. gfx line: `video=wayland renderer="NVIDIA GeForce RTX 4080…" gl="3.3.0
> NVIDIA 610.43.03" swapInterval=0 samples=0`.

Reading it:

- **Browser vs HultUI table** is the apples-to-apples comparison — both build and
  lay out every real row. Warm, HultUI is **faster than the vanilla browser on
  every operation**, by ~2× or better on the bulk ops (`create 1,000` 11.9 vs
  31.5, `create 10,000` 177 vs 330, `append` 20 vs 35.3, `clear` 0.5 vs 14.5)
  and by 6–11× on the in-place ops (`update` 2.9 vs 18.8, `swap` 1.7 vs 18.7,
  `remove` 2.6 vs 16.1, and `select` especially — paint culling plus a
  sub-frame present).
- **Caveat on the small ops:** HultUI's post-frame callback fires as soon as the
  frame is presented (sub-millisecond for a cheap op), whereas the browser's
  paint timing is coarser. So treat the in-place-op gaps as "both are fast" rather
  than a literal 5–10× — the bulk `create`/`append`/`clear` rows are the more
  directly comparable layout-vs-layout numbers.
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
