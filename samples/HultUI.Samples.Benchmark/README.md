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

### Framework feature batch, before and after (2026-10-05, rechecked 2026-10-06)

The 35 merges from e5a2206 (2026-10-04) to 90730ee (2026-10-05): undo, scroll
chaining, dialog Esc/focus, baseline alignment, find bar, settings, window
placement, single instance, shortcuts, command palette, menu bar, expander,
popover, table, aspect ratio, align-content, sticky, the single-line cross clamp,
golden images. Then main at 449ecd0, after the evening's performance fixes
(opacity groups, z-order, truncation, BiDi/styled/multi-font memos).

Measured on 2026-10-06 with the machine otherwise idle: all three built Release,
run in turn 6 times each on the 8 performance cores (see below), every run started
only once the machine was under 8 % CPU with no test host or game running. Median
of each run's minimum (median of each run's median), ms, a11y off:

| Operation             |      e5a2206 |      90730ee |   449ecd0 | batch |  since |
| --------------------- | -----------: | -----------: | --------: | ----: | -----: |
| create 1,000          | 11.37 (15.6) |  9.59 (15.2) | 7.81 (13.7) | −16 % | −19 % |
| update every 10th     |  1.08 (1.18) |  0.96 (1.20) | 0.97 (1.24) | −11 % |  +1 % |
| select row            |  0.30 (0.39) |  0.29 (0.37) | 0.34 (0.86) |  −3 % | +16 % |
| swap rows             |  0.54 (0.64) |  0.55 (0.63) | 0.53 (1.17) |  +2 % |  −5 % |
| remove row            |  1.70 (1.94) |  1.73 (1.89) | 1.64 (2.12) |  +2 % |  −5 % |
| create 10,000         |   130 (167)  |   139 (169)  |  141 (171)  |  +7 % |  +1 % |
| append 1,000 → 1,000  |  7.21 (7.81) |  7.79 (8.64) | 7.79 (8.38) |  +8 % |   0 % |
| clear 1,000           |  0.52 (3.23) |  0.62 (2.83) | 0.57 (2.98) | +19 % |  −7 % |

The batch added 7–8 % to the layout-bound bulk operations, as the first,
evening measurement (8 pinned runs per side) had found: create 10,000 +6 %,
append +8 %. The scenario suite traces that to flex relayout, +12–13 %, spread
over align-content, aspect ratio, baseline alignment and the JustifyContent fix
([its README](../HultUI.Samples.PerfSuite/README.md)). Main has not won it back;
its fixes went to text and paint, which this table barely exercises — except
create 1,000, whose style pass fell from 1.4 to 0.4 ms (`--profile`). The
sub-millisecond rows (select, swap, clear) are within their noise.

With accessibility on (this machine's background UIA client attached), the bulk
rows read alike (create 10,000: 167 / 162 / 161 ms) and the in-place rows 7–32 %
lower after the batch. The evening table that stood here first agreed on the
bulk rows but not on every small one; a game was open on the machine for part of
that evening, so this quiet table replaces it.

**Won back, 2026-10-06** (4f8a3fc, a leaf measured once per width instead of once
per pass): against main at f586c39, 14 interleaved runs each, a11y off —
create 10,000 141 → 131 ms (−7 %), append 7.6 → 6.5 ms (−15 %), remove row 1.77 →
1.25 ms (−29 %), create 1,000 −7 %; the sub-millisecond rows unchanged on their
minimums. Create 10,000 is back at its pre-batch e5a2206 level (130 ms).

### A measurement holds for a range of heights (2026-10-06), and the harness fixed

Container measurements are now shared between the measure and layout passes too
(d6c0bd6, 5decd7f, 66e06f7 — see the scenario suite's README). Measuring them showed
two flaws in this harness, both making a faster build read slower, fixed in f30d02c:
the pre-warm was three fixed passes, so a faster build ran its first metrics on code
still being JIT-promoted; and since the loop was paced to the display (b46e1cc,
2026-09-20) a measured frame waited for its display slot whenever the setup frame
before it fitted inside one interval. **Numbers before f30d02c are not comparable
with numbers after it.** With the fixed harness in both builds, 8 interleaved runs
each, a11y off, minimum (median), ms:

| Operation             |  49a9b8b        |  c80711c        | min    |
| --------------------- | --------------: | --------------: | -----: |
| create 1,000          |   5.12 (9.39)   |   4.55 (4.96)   | −11 %  |
| update every 10th     |   0.65 (0.72)   |   0.61 (0.66)   |  −6 %  |
| select row            |   0.22 (0.28)   |   0.20 (0.27)   |  −9 %  |
| swap rows             |   0.35 (0.41)   |   0.35 (0.38)   |  +1 %  |
| remove row            |   1.00 (1.12)   |   0.62 (0.70)   | −38 %  |
| create 10,000         |  125.4 (157.3)  |   97.1 (122.3)  | −23 %  |
| append 1,000 → 1,000  |   6.02 (6.73)   |   5.72 (12.64)  |  −5 %  |
| clear 1,000           |   0.20 (0.26)   |   0.20 (0.22)   |  −3 %  |

Create 10,000's layout pass went from 71 to 39 ms. Append's median is the one row
that rose: its layout is a quarter faster (4.6 → 3.5 ms), but layout no longer makes
garbage, so the gen-0 collection that used to run during the setup frame now lands
inside the measured append, copying both tables' fresh rows. With a gen-0 budget
large enough that no collection runs mid-operation (`DOTNET_GCgen0size=0x10000000`),
append reads 6.30 → 5.14 ms (−18 %) and every other row's minimum is level or better.

### The layout pass takes the measure pass's flex resolution (2026-10-06)

6df8757 and 0bf5823 (see the scenario suite's README), the same harness, 8 interleaved runs each,
a11y off, minimum: append 1,000 → 1,000 6.04 → 5.44 ms (−10 %), create 10,000 105.6 → 103.4 ms
(−2 %), every other row within ±4 %. These operations make rows, so most of their layout pass
is measuring new text for the first time, which this does not touch: their layout phase is about
5 % shorter (create 10,000: 41.7 → 39.7 ms, 3 runs). The scenario suite's resize of the same rows,
which only lays them out again, is −10 % overall.

### A measurement holds for a range of widths (2026-10-06)

91f9e4b, 1b49032 and 98e0581 (see the scenario suite's README), 8 interleaved runs each, a11y off,
minimum: remove row 0.62 → 0.37 ms (−41 %), create 1,000 4.77 → 4.11 ms (−14 %), append 1,000 →
1,000 5.54 → 4.79 ms (−14 %), create 10,000 100.2 → 99.4 ms (−1 %); update, swap, select and clear
within two hundredths of a millisecond. A table row's 64 px id and 24 px remove button now hold
their measurement and their layout at any width they are offered, and its label at any width it
fits in.

## How the measurement works

HultUI's whole pipeline — component rebuild → style → layout → render → present —
runs synchronously on the UI thread inside one frame. The harness times from just
before the mutation to just after that frame is presented, using
`Window.RequestPostFrameCallback`, which fires once the frame is on screen. That
span is the same "click → painted" interval the web benchmark records with
`PerformanceObserver`.

The `--auto` suite first runs a **global pre-warm** — every measured operation
over large and small tables, in passes until at least three are done and three
seconds have passed, then a 500 ms pause — so the shared framework code is
JIT-tiered to steady state before any timing starts (by time, not by a count of
passes: since f30d02c, see above). Scripted runs render **unpaced**, so a measured
frame renders as soon as its mutation is made instead of waiting for its display
slot. It then gives each measured operation its own dedicated frame,
runs a couple of per-metric warm-up iterations, forces a GC between metrics, and
reports the **median** of 5 runs — matching how js-framework-benchmark isolates
its runs against an already-warm V8. Under NativeAOT the pre-warm is a cheap
no-op (there is no tiering); under JIT it is what stops the first metric from
absorbing tier-up and reading ~2× high.

`--auto` and `--profile` **pin the process to the performance cores** of a hybrid
CPU (since 2026-10-05; `--no-pin` turns it off). This machine's i7-14700K has 8
performance and 12 efficiency cores; an efficiency core runs the UI thread ~1.8×
slower, and the scheduler moves a lone busy thread between the two kinds, so an
unpinned run mixes both speeds into its samples. The pinning code is shared with
the [scenario suite](../HultUI.Samples.PerfSuite/README.md).

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
