# Plan 17, all five rounds: the total improvement

What five rounds of the performance team changed, measured side by side on one machine on 2026-10-26/27, right after a
reboot: **baseline 520002e9** (main on 2026-10-07, just before the plan 17 brief) against **after** (round 5 released;
the code of main f31194ab). Every timed run was behind `scripts/perf/idle-gate.ps1`, pinned to the P-cores, with base and
after alternated, on JIT and on NativeAOT. Part A is the perf suite, part B the five desktop apps. The round-by-round
record, with each change's own numbers, is the Landed table in
[the ledger](../17-performance-team-ledger.md).

## In short

- **Nothing a user feels got worse.** No shared perf suite row is slower or allocates more, on JIT or AOT.
- **Apps, per key (AOT):** bytes per key fell 51–99 % on all 23 app walks (−90 % summed); UI-thread CPU per key fell
  ≥5 % on 20 of 23 walks (geometric mean −26 % AOT, −23 % JIT). In real windows the work per key fell 24–64 %
  (Junction History 3.70 → 1.35 ms, Vesper Detail −35 %, Meridian −24 %).
- **Apps, other:** an idle window went from about 75 UI-thread wake-ups a second to 0 in all five apps; memory after a
  walk is 10–22 % lower in Junction, Vesper and Meridian; typing in an 89,000-character Meridian body went from 127
  gen-2 collections to 1 (GC pause 100 → 29 ms). Start-up did not move (within ±13 ms of noise).
- **With a screen reader listening,** the baseline built a whole accessibility tree on half to all keys; now none on
  every walk but the review composer (Vesper Detail with a reader −51 % CPU per key).
- **Perf suite (83 rows in both suites):** summed time −25.5 % JIT, −28.7 % AOT; 39 rows ≥5 % faster on JIT, 62 on
  AOT; allocation summed over them −63…−64 %. Text −29 % time, layout −15 % on AOT, allocation −33…−98 % by area.
  NativeAOT no longer allocates more than JIT (it did on 79 of 83 rows; now 72 are within 1 KB).
- **The shared rows understate plan 17:** most of its work went into 68 rows it added (accessibility, virtual lists,
  input, real-loop rows), which have no baseline; part B's app walks cover that side.
- **Biggest cost left:** Junction's highlight prefetch, about 10 MB per History key on a worker in both trees
  (`apps-junction-highlight-prefetch`, Waiting). One small open question: Meridian History's headless AOT p90 rose
  2.69 → 3.47 ms (median flat, min −11 %, JIT and the real window both improved); not traced.

Captures: `samples/HultUI.Samples.PerfSuite/captures/*-total-*.json` (3c2afc67). Probe code and raw data for part B are
in the session scratchpad (`total/partB/`), not kept.

## Part A: the perf suite

**Baseline** 520002e9 (main on 2026-10-07, just before the plan 17 brief) against **after** 5b8ac7d8 (round 5 released; its code is main f31194ab). Measured 2026-10-26/27 on the i7-14700K after a reboot: 3 full runs per side, alternated base/after, each behind the idle gate, pinned to the P-cores, on JIT and on a NativeAOT publish. 83 rows exist in both suites and are compared; 68 rows were added during plan 17 and have no baseline.

**In one line:** over the 83 shared rows, nothing got slower; 39 rows are ≥5 % faster on JIT and 62 on NativeAOT; the rows' summed time fell 25.5 % (JIT) and 28.7 % (AOT), and the allocation summed over them fell 63–64 %.

### Headline per area

Each cell is the median of the rows' changes in that area, with the range in brackets. Time is the min per step (best of 75 samples: 3 runs × 5 rounds × 5 steps); allocation is KB per step, the mean of 3 runs. A minus is better.

| Area | Rows | JIT time, median (range) | AOT time, median (range) | JIT alloc, median (range) | AOT alloc, median (range) |
|---|---|---|---|---|---|
| Layout and grid | 11 | -1.5 % (-64 … +0) | -15.2 % (-78 … -3) | -75.3 % (-99 … -34) | -97.5 % (-100 … -36) |
| Text | 13 | -29.0 % (-66 … +1) | -28.6 % (-67 … +1) | -80.9 % (-95 … -56) | -81.2 % (-95 … -58) |
| Scroll, virtual lists, tables | 10 | -1.3 % (-8 … +2) | -5.8 % (-14 … -3) | -33.3 % (-73 … -11) | -44.2 % (-85 … -16) |
| Components and reactivity | 9 | -2.5 % (-30 … +0) | -10.7 % (-25 … -2) | -71.0 % (-100 … -15) | -83.8 % (-100 … -16) |
| Input | 8 | -3.4 % (-25 … -0) | -10.5 % (-29 … -5) | -74.5 % (-100 … -42) | -84.3 % (-100 … -48) |
| Rich text editor | 9 | -10.8 % (-39 … -1) | -10.1 % (-38 … -1) | -48.7 % (-98 … -10) | -55.1 % (-98 … -12) |
| Overlays | 6 | -3.0 % (-14 … +0) | -5.4 % (-13 … -1) | -46.7 % (-87 … -34) | -56.4 % (-94 … -44) |
| Controls, icons, themes | 7 | -4.1 % (-8 … -1) | -8.0 % (-17 … -5) | -55.6 % (-100 … -13) | -79.5 % (-100 … -20) |
| Paint | 6 | -0.2 % (-2 … +1) | -2.1 % (-5 … -1) | -100.0 % (-100 … -100) | -100.0 % (-100 … -100) |
| Accessibility | 4 | -9.5 % (-53 … -4) | -13.4 % (-57 … -6) | -81.3 % (-99 … -22) | -83.0 % (-99 … -27) |
| **All matched** | 83 | -3.8 % (-66 … +2) | -9.6 % (-78 … +1) | -72.9 % (-100 … -10) | -80.3 % (-100 … -12) |

- **JIT:** sum of the 83 mins 181.3 → 135.0 ms (-25.5 %); geometric mean of after/base 0.858 (-14.2 %); 39 rows ≥5 % faster, 19 rows ≥20 % faster, largest increase +1.8 %; allocation summed over the rows 144,250 → 53,301 KB/op (-63.0 %).
- **AOT:** sum of the 83 mins 214.6 → 153.1 ms (-28.7 %); geometric mean of after/base 0.817 (-18.3 %); 62 rows ≥5 % faster, 18 rows ≥20 % faster, largest increase +0.5 %; allocation summed over the rows 149,276 → 53,213 KB/op (-64.4 %).

What the areas say:

- **Time on JIT is flat on most rows that plan 17 did not aim at, and well down on the ones it did.** Grid, text truncation, rich text, text areas and component rebuilds moved 10–66 %; scroll, paint, plain layout resizes and overlays moved 0–8 %.
- **NativeAOT gained more and more broadly** (median −9.6 % against −3.8 %). Before plan 17 the AOT build allocated more than JIT on 79 of the 83 rows (layout/resize 2,000 rows: 691 KB per step on AOT, 8.5 on JIT), because every element's trace name was looked up per frame even with tracing off, and `Type.Name` builds a new string each call there. After, AOT is within 1 KB of JIT on 72 rows. Apps ship AOT, so the AOT column is the one apps feel.
- **Allocation fell on every row, on both builds** (no row up). Steady frames now allocate nothing: all six paint rows, hover, the tween and spinner frames and the light/dark switch read 0.0 KB per step.
- **Media, loop, window and terminal have no shared rows.** Their rows were all added during plan 17; see "Rows without a baseline".

### The biggest wins and what explains them

| Row | JIT min | AOT min | Alloc (JIT) | What explains it (round, commit) |
|---|---|---|---|---|
| grid/edit auto-column cell | 2.058 → 0.744 ms (−64 %) | 4.033 → 0.871 (−78 %) | 16.4 → 0.8 KB | R2 afbd13c, cc657e4: grid cells bucketed in one pass, track origins by prefix sums (was 1.6 M loop steps a step) |
| grid/resize 300x6, grid/create 300x6 | −52 %, −46 % | −71 %, −63 % | 16.7 → 2.3; 3,116 → 2,061 | same |
| text/spans recolor (paint only), text/spans bold (metric) | −66 %, −32 % | −67 %, −45 % | 161 → 71; 1,043 → 232 | R3 8ddc3eb, ea6c48e: each colour stretch draws a kept sub-blob of only its own glyphs (glyphs per frame −73…−91 %) |
| a11y/type in rich text, bridge on | 1.605 → 0.759 (−53 %) | 1.813 → 0.782 (−57 %) | 2,136 → 52 KB | R2 240d468, de851f6: the editor's accessible text is made when a reader asks, not per keystroke; ff94faa: no whole-document Value change for nodes that offer text |
| text ellipsis rows (multi-font, styled bidi, plain, styled, bidi) | −48, −46, −43, −41, −29 % | −48, −46, −42, −42, −29 % | 7,000–12,400 → 600–2,200 KB | R1 6891682: a one-line label starts from min(untruncated width, room) out of a pinned measure entry; R2 e21e376: HarfBuzz driven directly; allocation also R1 0178784, R5 91d48601 + cc6f8467 |
| richtext/open 5,000 blocks | 15.08 → 9.14 (−39 %) | 17.73 → 10.98 (−38 %) | 18,230 → 4,626 KB | R1 0178784 (word cache diet, −26 % then), R5 91d48601 (glyph arrays made on first draw, −11 %), plus R3 7d6cc85, R4 eb5a6ecd, R5 a1ff84a1 on bytes |
| component/edit one of 1,000 (keyed, unkeyed), reverse | −28, −30, −26 % | −24, −25, −24 % | 6,250 → 5,280 KB | R2 cc842ac (no hash set for a childless parent, −9…−17 %), R3 218f831 (style marks stop at an already marked subtree, keyed −15 %), R1 frame floor 7b095f0 |
| text/create 2,000 unique labels | 4.262 → 3.026 (−29 %) | −28 % | 7,640 → 3,099 KB | R2 e21e376 text-shaper-direct (−23 % then), R5 91d48601 |
| input/type in 2,000-line TextArea, undo-redo | −25 %, −25 % | −29 %, −27 % | 471 → 205; 484 → 205 | R2 fc00b75 + a9cc75f + 46a1219 + 9b801d1 (a hard line's width measured without layout, −22 %/−28 % then), R3 dc6c047 (incremental wrap) |
| text/open a long document in a TextArea | 3.484 → 2.631 (−24 %) | −20 % | 7,104 → 369 KB (−95 %) | R1 0178784 (−10 % then) and the shaping changes, not traced further; bytes R3 7d6cc85 and R4 d7c0c046 (prefix tables in kept lists) |
| layout/resize 2,000-row list, layout/deep nesting leaf edit (AOT) | −0.2 %, −1.5 % | −15 %, −18 % | AOT 691 → 2.3; 122 → 0.7 | R1 9669d6b: element trace names looked up only while recording (`Type.Name` allocates per call on AOT) |
| overlay/command palette filter 2,000 | −14 % | −13 % | 663 → 364 KB | R2 2317632, d1f118d: joined text kept while the palette is open, one array sort |
| theme switches, controls/expander, overlay/dropdown | −4…−7 % | −8…−13 % | light/dark 27.7 → 0.0 | R3 2af43b9 (controls mark layout in ApplyTheme only on a measured change), R2 7856452 (leaves ignore the offered height), R4 0ea667cf (menu rows outside the window not drawn) |
| Every steady frame (paint, hover, tweens, spinners) | flat | −1…−7 % | to 0.0 KB | R1 a2f6051 (zero-allocation text draw), R1 7b095f0 group (frame floor), R2 1d61775 (blur and gradient objects kept) |

Rows not traced to one commit: `layout/cards create 300` (−14 % JIT, −19 % AOT, 7,688 → 2,845 KB) and `layout/shrink 300 overfull rows` (1,343 → 332 KB) gained from several text and reconcile changes together.

### Rows that got slower

**None.** No matched row's min rose by 3 % or more on either build, and no row's allocation rose at all. The largest rises were +1.8 % (JIT, `table/TableView 10k sort`, median +0.3 %) and +0.5 % (`text/styled wrap 150 paragraphs resize`, both builds). The run-to-run spread of a row's min is 1–2 % (median) and 3–4 % (90th percentile), so these are noise.

A filtered, alternated, idle-gated recheck (2 runs per side, JIT) of the four flattest rows agreed:

| Row | Min base → after | Δ min | Δ median |
|---|---|---|---|
| text/styled wrap 150 paragraphs resize | 1.755 → 1.785 | +1.7 % | −0.8 % |
| scroll/nested chaining wheel | 0.280 → 0.281 | +0.4 % | −1.6 % |
| table/TableView 10k sort | 2.245 → 2.266 | +0.9 % | −2.2 % |
| overlay/dialog open | 3.058 → 3.040 | −0.6 % | −1.7 % |

No JIT move fell in the +6–12 % tiering-trap band, so no `DOTNET_TC_CallCountingDelayMs=0` or PGO-off recheck was needed. The suite's own `--compare` (10 % threshold on min and median) reads 0 slower on every pair (for example AOT run 1: 0 slower, 35 faster; JIT run 2: 0 slower, 29 faster).

### Full matched table

All 83 rows that exist in both suites, by area. Min in ms (lowest of 3 runs), allocation in KB per step (mean of 3 runs).

| Row | JIT min (ms) | Δ | AOT min (ms) | Δ | JIT KB/op | AOT KB/op | Δ KB (JIT / AOT) |
|---|---|---|---|---|---|---|---|
| layout/resize 2,000-row list | 1.688 → 1.685 | -0.2 % | 2.970 → 2.519 | -15.2 % | 8.5 → 2.3 | 691 → 2.3 | -73 / -100 % |
| layout/deep nesting leaf edit | 0.268 → 0.264 | -1.5 % | 0.416 → 0.342 | -18.0 % | 51.3 → 0.7 | 122 → 0.7 | -99 / -99 % |
| layout/wrap 1,000 chips resize | 1.776 → 1.770 | -0.3 % | 1.922 → 1.858 | -3.3 % | 15.7 → 2.3 | 80.0 → 2.3 | -85 / -97 % |
| layout/baseline rows resize | 0.944 → 0.942 | -0.2 % | 1.177 → 1.104 | -6.2 % | 7.3 → 2.3 | 129 → 2.3 | -68 / -98 % |
| layout/aspect-ratio tiles resize | 0.780 → 0.773 | -0.9 % | 0.963 → 0.891 | -7.4 % | 4.7 → 2.3 | 91.6 → 2.3 | -51 / -97 % |
| layout/shrink 300 overfull rows resize | 2.964 → 2.898 | -2.2 % | 3.952 → 3.561 | -9.9 % | 1,343 → 332 | 1,757 → 332 | -75 / -81 % |
| layout/shrink bounded columns resize | 1.373 → 1.376 | +0.2 % | 1.513 → 1.455 | -3.8 % | 31.3 → 2.3 | 83.2 → 2.3 | -93 / -97 % |
| layout/cards create 300 | 4.022 → 3.457 | -14.0 % | 5.075 → 4.102 | -19.2 % | 7,688 → 2,845 | 7,828 → 2,859 | -63 / -63 % |
| grid/create 300x6 | 3.319 → 1.794 | -45.9 % | 5.716 → 2.133 | -62.7 % | 3,116 → 2,061 | 3,218 → 2,061 | -34 / -36 % |
| grid/resize 300x6 | 2.506 → 1.197 | -52.3 % | 4.519 → 1.331 | -70.5 % | 16.7 → 2.3 | 112 → 2.3 | -86 / -98 % |
| grid/edit auto-column cell | 2.058 → 0.744 | -63.8 % | 4.033 → 0.871 | -78.4 % | 16.4 → 0.8 | 112 → 0.8 | -95 / -99 % |
| text/wrap 150 paragraphs resize | 2.319 → 2.131 | -8.1 % | 2.716 → 2.447 | -9.9 % | 3,096 → 397 | 3,104 → 397 | -87 / -87 % |
| text/create 2,000 unique labels | 4.262 → 3.026 | -29.0 % | 5.308 → 3.814 | -28.2 % | 7,640 → 3,099 | 7,739 → 3,099 | -59 / -60 % |
| text/ellipsis 1,000 labels resize | 5.043 → 2.881 | -42.9 % | 6.332 → 3.672 | -42.0 % | 7,296 → 609 | 7,491 → 609 | -92 / -92 % |
| text/bidi wrap 150 paragraphs resize | 3.288 → 3.101 | -5.7 % | 3.505 → 3.296 | -6.0 % | 1,204 → 239 | 1,214 → 239 | -80 / -80 % |
| text/bidi ellipsis 1,000 labels resize | 15.077 → 10.707 | -29.0 % | 16.442 → 11.740 | -28.6 % | 7,137 → 1,322 | 7,309 → 1,307 | -81 / -82 % |
| text/styled wrap 150 paragraphs resize | 1.752 → 1.762 | +0.5 % | 1.980 → 1.991 | +0.5 % | 613 → 155 | 626 → 155 | -75 / -75 % |
| text/styled ellipsis 1,000 labels resize | 5.783 → 3.397 | -41.3 % | 7.292 → 4.210 | -42.3 % | 6,320 → 1,206 | 6,486 → 1,191 | -81 / -82 % |
| text/styled bidi wrap 150 paragraphs resize | 4.145 → 3.696 | -10.8 % | 4.625 → 4.164 | -10.0 % | 1,619 → 309 | 1,635 → 309 | -81 / -81 % |
| text/styled bidi ellipsis 1,000 labels resize | 27.181 → 14.593 | -46.3 % | 30.311 → 16.476 | -45.6 % | 12,443 → 1,755 | 12,625 → 1,739 | -86 / -86 % |
| text/multi-font ellipsis 1,000 labels resize | 7.992 → 4.191 | -47.6 % | 10.079 → 5.257 | -47.8 % | 11,586 → 2,223 | 11,748 → 2,207 | -81 / -81 % |
| text/spans recolor (paint only) | 2.897 → 0.989 | -65.9 % | 3.129 → 1.022 | -67.3 % | 161 → 70.7 | 167 → 70.7 | -56 / -58 % |
| text/spans bold (metric) | 1.526 → 1.031 | -32.4 % | 1.951 → 1.065 | -45.4 % | 1,043 → 232 | 1,059 → 233 | -78 / -78 % |
| text/open a long document in a TextArea | 3.484 → 2.631 | -24.5 % | 4.355 → 3.491 | -19.8 % | 7,104 → 369 | 7,106 → 369 | -95 / -95 % |
| scroll/wheel 5,000-row list | 0.362 → 0.335 | -7.6 % | 0.446 → 0.384 | -14.1 % | 8.9 → 2.7 | 17.1 → 2.7 | -70 / -84 % |
| scroll/jump 5,000-row list | 0.332 → 0.331 | -0.2 % | 0.396 → 0.358 | -9.6 % | 16.5 → 10.5 | 26.9 → 10.5 | -36 / -61 % |
| scroll/sticky sections wheel | 0.264 → 0.260 | -1.4 % | 0.282 → 0.269 | -4.5 % | 10.0 → 2.7 | 18.1 → 2.7 | -73 / -85 % |
| scroll/nested chaining wheel | 0.281 → 0.282 | +0.4 % | 0.304 → 0.292 | -3.8 % | 9.3 → 3.5 | 17.8 → 3.5 | -62 / -80 % |
| virtual/list 100k wheel | 0.267 → 0.262 | -1.9 % | 0.294 → 0.277 | -5.8 % | 42.8 → 28.6 | 50.7 → 28.6 | -33 / -44 % |
| virtual/list 100k jump | 0.341 → 0.340 | -0.4 % | 0.395 → 0.379 | -4.2 % | 247 → 183 | 257 → 183 | -26 / -29 % |
| virtual/list 100k replace items | 0.314 → 0.308 | -2.0 % | 0.367 → 0.343 | -6.7 % | 176 → 143 | 191 → 143 | -19 / -25 % |
| table/TableView 10k sort | 2.230 → 2.269 | +1.8 % | 2.360 → 2.282 | -3.3 % | 420 → 372 | 441 → 372 | -11 / -16 % |
| table/TableView 10k wheel | 0.370 → 0.366 | -1.3 % | 0.404 → 0.380 | -5.8 % | 64.7 → 43.1 | 78.2 → 43.1 | -33 / -45 % |
| table/TableView 10k resize column | 0.438 → 0.423 | -3.4 % | 0.510 → 0.471 | -7.8 % | 301 → 253 | 323 → 253 | -16 / -22 % |
| component/edit one of 1,000 keyed rows | 1.604 → 1.157 | -27.9 % | 1.979 → 1.509 | -23.8 % | 6,248 → 5,280 | 6,303 → 5,280 | -15 / -16 % |
| component/edit one of 1,000 unkeyed rows | 1.565 → 1.103 | -29.6 % | 1.903 → 1.430 | -24.9 % | 6,118 → 5,151 | 6,174 → 5,151 | -16 / -17 % |
| component/reverse 1,000 keyed rows | 1.700 → 1.260 | -25.9 % | 2.118 → 1.605 | -24.2 % | 6,276 → 5,310 | 6,331 → 5,310 | -15 / -16 % |
| component/counter in a 6,000-element tree | 0.262 → 0.256 | -2.5 % | 0.292 → 0.274 | -6.2 % | 10.0 → 2.9 | 17.9 → 2.9 | -71 / -84 % |
| component/mount 300 components | 1.381 → 1.286 | -6.9 % | 1.816 → 1.569 | -13.6 % | 2,336 → 1,786 | 2,440 → 1,767 | -24 / -28 % |
| component/signal fan-out to 500 labels | 0.522 → 0.511 | -2.0 % | 0.642 → 0.573 | -10.7 % | 122 → 16.0 | 164 → 16.0 | -87 / -90 % |
| anim/60 looping tweens frame | 0.206 → 0.203 | -1.5 % | 0.217 → 0.212 | -2.1 % | 0.7 → 0.0 | 6.7 → 0.0 | -100 / -100 % |
| anim/spinners + progress frame | 0.530 → 0.527 | -0.5 % | 0.580 → 0.557 | -4.0 % | 5.7 → 0.0 | 15.4 → 0.0 | -100 / -100 % |
| anim/hover transitions | 2.514 → 2.526 | +0.4 % | 2.666 → 2.572 | -3.5 % | 36.6 → 1.5 | 74.4 → 1.5 | -96 / -98 % |
| input/hover across 1,000 rows | 0.270 → 0.268 | -0.9 % | 0.305 → 0.284 | -6.8 % | 6.8 → 0.0 | 14.3 → 0.0 | -100 / -100 % |
| input/click checkbox in 500 rows | 0.404 → 0.400 | -1.0 % | 0.441 → 0.416 | -5.5 % | 4.6 → 0.4 | 9.5 → 0.4 | -91 / -96 % |
| input/type in TextInput | 0.342 → 0.341 | -0.2 % | 0.468 → 0.404 | -13.8 % | 44.9 → 0.8 | 101 → 0.8 | -98 / -99 % |
| input/type in 2,000-line TextArea | 0.578 → 0.432 | -25.2 % | 0.640 → 0.457 | -28.5 % | 471 → 205 | 471 → 205 | -56 / -56 % |
| input/type in a wrapped 900-line TextArea | 0.777 → 0.667 | -14.1 % | 0.809 → 0.701 | -13.3 % | 485 → 254 | 485 → 254 | -48 / -48 % |
| input/undo-redo in TextArea | 0.595 → 0.449 | -24.7 % | 0.644 → 0.471 | -26.9 % | 484 → 204 | 484 → 204 | -58 / -58 % |
| input/tab through 300 fields | 0.322 → 0.310 | -3.6 % | 0.343 → 0.322 | -6.3 % | 28.3 → 0.3 | 43.0 → 0.3 | -99 / -99 % |
| input/drag to reorder 200 rows | 0.287 → 0.278 | -3.2 % | 0.330 → 0.305 | -7.7 % | 15.3 → 8.8 | 32.3 → 8.8 | -42 / -73 % |
| richtext/open 5,000 blocks | 15.083 → 9.135 | -39.4 % | 17.727 → 10.976 | -38.1 % | 18,230 → 4,626 | 18,255 → 4,629 | -75 / -75 % |
| richtext/type in 5,000 blocks | 0.842 → 0.736 | -12.5 % | 0.851 → 0.774 | -9.0 % | 87.8 → 51.1 | 90.7 → 51.2 | -42 / -44 % |
| richtext/enter in 5,000 blocks | 0.789 → 0.708 | -10.3 % | 0.805 → 0.724 | -10.1 % | 90.7 → 81.9 | 93.2 → 81.8 | -10 / -12 % |
| richtext/wheel through 5,000 blocks | 0.715 → 0.613 | -14.3 % | 0.725 → 0.622 | -14.2 % | 9.9 → 8.6 | 12.7 → 8.6 | -13 / -32 % |
| richtext/select all 5,000 blocks | 1.043 → 0.956 | -8.3 % | 1.052 → 0.948 | -10.0 % | 12.1 → 0.3 | 14.8 → 0.3 | -98 / -98 % |
| richtext/resize 1,000 blocks | 3.403 → 2.851 | -16.2 % | 3.787 → 3.250 | -14.2 % | 3,981 → 968 | 3,984 → 968 | -76 / -76 % |
| richtext/type in a paragraph of 600 spans | 2.559 → 2.421 | -5.4 % | 2.745 → 2.662 | -3.0 % | 1,966 → 592 | 1,971 → 592 | -70 / -70 % |
| richtext/type in a 40x5 table | 0.484 → 0.478 | -1.1 % | 0.501 → 0.497 | -0.8 % | 17.7 → 11.2 | 18.0 → 11.2 | -37 / -38 % |
| richtext/type in an auto-height editor | 0.780 → 0.696 | -10.8 % | 0.796 → 0.696 | -12.6 % | 22.4 → 11.5 | 25.6 → 11.5 | -49 / -55 % |
| overlay/popover open | 0.516 → 0.498 | -3.4 % | 0.539 → 0.517 | -4.0 % | 50.1 → 33.1 | 59.9 → 32.9 | -34 / -45 % |
| overlay/dialog open | 3.021 → 3.033 | +0.4 % | 2.979 → 2.950 | -1.0 % | 26.1 → 12.5 | 33.6 → 12.1 | -52 / -64 % |
| overlay/menu bar open | 0.417 → 0.406 | -2.6 % | 0.441 → 0.418 | -5.2 % | 32.9 → 17.0 | 41.3 → 16.5 | -48 / -60 % |
| overlay/command palette filter 2,000 | 1.515 → 1.298 | -14.4 % | 1.576 → 1.371 | -13.0 % | 663 → 364 | 759 → 359 | -45 / -53 % |
| overlay/tooltip show | 0.549 → 0.549 | -0.1 % | 0.606 → 0.572 | -5.6 % | 14.7 → 1.9 | 29.3 → 1.9 | -87 / -94 % |
| overlay/dropdown open 200 items | 0.465 → 0.431 | -7.3 % | 0.512 → 0.446 | -12.9 % | 58.4 → 38.4 | 62.6 → 35.2 | -34 / -44 % |
| controls/gallery mount | 1.123 → 1.081 | -3.7 % | 1.279 → 1.178 | -7.9 % | 830 → 475 | 847 → 459 | -43 / -46 % |
| controls/tabs switch | 0.838 → 0.772 | -7.8 % | 1.209 → 1.008 | -16.7 % | 1,896 → 1,529 | 2,047 → 1,529 | -19 / -25 % |
| controls/expander toggle (animated) | 2.976 → 2.872 | -3.5 % | 3.357 → 2.981 | -11.2 % | 136 → 60.6 | 296 → 60.6 | -56 / -80 % |
| controls/slider drag | 0.277 → 0.274 | -1.0 % | 0.307 → 0.293 | -4.6 % | 7.8 → 1.2 | 15.5 → 1.2 | -85 / -92 % |
| icons/mount 600 | 1.704 → 1.625 | -4.6 % | 1.993 → 1.820 | -8.7 % | 1,669 → 1,453 | 1,811 → 1,453 | -13 / -20 % |
| theme/light-dark switch | 0.826 → 0.777 | -5.9 % | 0.872 → 0.803 | -8.0 % | 27.7 → 0.0 | 49.2 → 0.0 | -100 / -100 % |
| theme/named scheme cycle | 0.819 → 0.786 | -4.1 % | 0.872 → 0.804 | -7.9 % | 29.9 → 2.2 | 51.4 → 2.2 | -93 / -96 % |
| paint/tiles plain | 0.295 → 0.295 | -0.0 % | 0.326 → 0.310 | -4.9 % | 5.8 → 0.0 | 12.7 → 0.0 | -100 / -100 % |
| paint/tiles rounded + border | 0.877 → 0.879 | +0.2 % | 0.909 → 0.896 | -1.4 % | 5.8 → 0.0 | 12.7 → 0.0 | -100 / -100 % |
| paint/tiles shadow | 4.449 → 4.412 | -0.8 % | 4.457 → 4.414 | -1.0 % | 13.1 → 0.0 | 20.1 → 0.0 | -100 / -100 % |
| paint/tiles gradient | 1.422 → 1.393 | -2.0 % | 1.392 → 1.354 | -2.7 % | 17.6 → 0.0 | 24.6 → 0.0 | -100 / -100 % |
| paint/tiles transform + opacity | 2.069 → 2.080 | +0.5 % | 2.067 → 2.046 | -1.0 % | 5.8 → 0.0 | 12.7 → 0.0 | -100 / -100 % |
| paint/dense text screen | 0.689 → 0.687 | -0.3 % | 0.775 → 0.739 | -4.6 % | 18.1 → 0.0 | 29.8 → 0.0 | -100 / -100 % |
| a11y/type in rich text, bridge on | 1.605 → 0.759 | -52.7 % | 1.813 → 0.782 | -56.8 % | 2,136 → 51.9 | 2,278 → 52.0 | -98 / -98 % |
| a11y/create 1,000 rows, bridge on | 2.852 → 2.585 | -9.4 % | 4.130 → 3.429 | -17.0 % | 7,982 → 6,190 | 8,461 → 6,190 | -22 / -27 % |
| a11y/edit every 10th of 1,000, bridge on | 0.468 → 0.424 | -9.5 % | 0.623 → 0.562 | -9.8 % | 627 → 219 | 693 → 219 | -65 / -68 % |
| a11y/tab through 300 fields, bridge on | 0.320 → 0.307 | -3.8 % | 0.343 → 0.322 | -6.1 % | 30.6 → 0.3 | 43.1 → 0.3 | -99 / -99 % |

### Rows without a baseline

The after suite has 151 rows; 68 of them were added during plan 17 and do not exist at 520002e9. All rows of the baseline exist in the after suite under the same name, and no row was renamed. Most new rows are app-shaped workloads (selection walks, typing beside a diff, the real `Application.Run` loop on a virtual clock, the UIA bridge), which is where plan 17 aimed most of its work, so the 83 matched rows understate the total. Part B's app measurements cover that side.

Counts: Text 8, Scroll, virtual lists, tables 7, Components and reactivity 3, Input 7, Window 1, Accessibility 29, Media 4, Loop (waits and wakes) 7, Terminal 2; total 68

| Row | JIT min (ms) | AOT min (ms) | JIT KB/op | AOT KB/op | Count it reports (JIT / AOT) |
|---|---|---|---|---|---|
| **Text** (8) | | | | | |
| text/create 2,000 unique emoji labels | 5.750 | 7.080 | 4,165 | 4,212 |  |
| text/resize a wrapped TextArea | 2.403 | 2.979 | 18.4 | 18.4 |  |
| text/open highlighted JSON in a TextArea | 0.520 | 0.666 | 268 | 268 |  |
| text/wheel highlighted JSON in a TextArea | 0.351 | 0.356 | 8.0 | 8.0 |  |
| text/page down through highlighted JSON in a TextArea | 0.421 | 0.432 | 41.5 | 41.5 |  |
| text/rebuild around highlighted JSON in a TextArea | 0.344 | 0.348 | 7.3 | 7.1 |  |
| text/selectable viewer hover | 0.672 | 0.699 | 0.2 | 0.2 |  |
| text/selectable viewer wheel | 0.726 | 0.744 | 10.8 | 10.8 |  |
| **Scroll, virtual lists, tables** (7) | | | | | |
| virtual/selection walk 1,000 rows | 0.438 | 0.459 | 89.4 | 89.1 | 19.00 / 19.00 rows made |
| virtual/selection walk 1,000 item rows | 0.418 | 0.423 | 23.8 | 23.7 | 3.00 / 3.00 rows made |
| virtual/selection walk 1,000 item rows, the list draws the highlight | 0.406 | 0.412 | 15.6 | 15.5 | 1.00 / 1.00 rows made |
| virtual/partner walk beside 1,000 item rows, every row told | 0.599 | 0.644 | 104 | 104 | 23.00 / 23.00 rows made |
| virtual/partner walk beside 1,000 item rows, two rows told | 0.571 | 0.584 | 37.9 | 37.9 | 6.00 / 6.00 rows made |
| virtual/rebuild beside a 20,000-row RowHeights list | 0.350 | 0.399 | 8.5 | 8.3 | 0.00 / 0.00 rows made/key |
| virtual/rebuild beside a 20,000-row RowHeights list, rows told | 0.361 | 0.430 | 43.4 | 43.2 | 37.00 / 37.00 rows made/key |
| **Components and reactivity** (3) | | | | | |
| component/list walk beside chrome built with it | 0.632 | 0.659 | 172 | 172 | 3.00 / 3.00 rows made |
| component/list walk beside chrome in kept parts | 0.593 | 0.617 | 33.6 | 33.5 | 3.00 / 3.00 rows made |
| anim/signal tween, 1 kHz posts, real loop | 39.469 | 38.893 | 274 | 269 | 1.00 / 1.00 rebuilds/frame |
| **Input** (7) | | | | | |
| input/type beside a 150-line diff, rows made in Build | 0.629 | 0.702 | 377 | 376 | 37.00 / 37.00 rows made/key |
| input/type beside a 150-line diff, rows kept | 0.492 | 0.515 | 15.0 | 14.7 | 0.00 / 0.00 rows made/key |
| input/type beside a large TextArea and checkboxes | 0.654 | 0.690 | 23.0 | 22.3 |  |
| input/key press, down and up 70 ms apart, real loop | 0.679 | 0.704 | 122 | 122 | 1.00 / 1.00 frames/press |
| input/wheel over a list that cannot scroll, real loop | 0.005 | 0.008 | 4.4 | 4.0 | 0.00 / 0.00 frames/notch |
| input/splitter drag, 1 kHz mouse, real loop | 5.884 | 6.861 | 4,592 | 4,591 | 0.90 / 0.90 rebuilds/frame |
| input/slider drag, 1 kHz mouse, real loop | 3.222 | 3.588 | 2,126 | 2,124 | 0.90 / 0.90 rebuilds/frame |
| **Window** (1) | | | | | |
| window/open a second window | 0.281 | 0.289 | 127 | 127 |  |
| **Accessibility** (29) | | | | | |
| a11y/selection walk 1,000 rows, bridge on | 0.475 | 0.516 | 252 | 252 | 19.00 / 19.00 rows made |
| a11y/selection walk 1,000 item rows, bridge on | 0.422 | 0.449 | 24.5 | 24.4 | 3.00 / 3.00 rows made |
| a11y/selection walk 3,000 item rows, bridge on | 0.414 | 0.423 | 24.7 | 24.6 | 3.00 / 3.00 rows made |
| a11y/selection walk beside details, keys 60 ms apart, real loop, bridge on | 0.687 | 0.712 | 122 | 122 | 0.00 / 0.00 a11y trees/key |
| a11y/selection walk beside details, keys 500 ms apart, real loop, bridge on | 0.697 | 0.723 | 123 | 122 | 0.00 / 0.00 a11y trees/key |
| a11y/selection walk beside details and kept chrome parts, keys 60 ms apart, real loop, bridge on | 0.682 | 0.711 | 130 | 130 | 0.00 / 0.00 a11y trees/key |
| a11y/filter a list below 1,000 rows, keys 500 ms apart, bridge on | 0.490 | 0.629 | 284 | 284 | 1.00 / 1.00 a11y trees/key |
| a11y/type beside a 150-line diff, rows made in Build, bridge on | 0.625 | 0.701 | 414 | 430 | 37.00 / 37.00 rows made/key |
| a11y/type beside a 150-line diff, rows kept and labelled, bridge on | 0.505 | 0.515 | 15.6 | 15.4 | 0.00 / 0.00 rows made/key |
| a11y/progress bar ticks over 1,000 rows, bridge on | 0.246 | 0.264 | 0.0 | 0.0 |  |
| a11y/caret walk in rich text beside 300 rows, bridge on | 0.305 | 0.318 | 0.9 | 0.8 | 0.00 / 0.00 a11y trees/key |
| a11y/caret walk in 5,000 blocks, a reader reading, bridge on | 0.694 | 0.686 | 1.0 | 1.0 | 0.00 / 0.00 a11y trees/key |
| a11y/type in a 2,000-line TextArea, a reader reading, bridge on | 0.606 | 0.643 | 309 | 309 |  |
| a11y/click checkbox in 500 rows, UIA | 0.479 | 0.468 | 0.8 | 0.8 |  |
| a11y/type in TextInput, UIA | 0.342 | 0.405 | 1.3 | 1.3 |  |
| a11y/edit 1 label of 1,000 rows, UIA | 0.249 | 0.280 | 0.9 | 0.9 |  |
| a11y/tab through 300 fields, UIA | 0.340 | 0.345 | 0.3 | 0.3 |  |
| a11y/wheel 1,000 rows, UIA | 0.478 | 0.609 | 3.3 | 3.3 |  |
| a11y/selection walk 1,000 rows, UIA | 0.504 | 0.539 | 252 | 252 | 19.00 / 19.00 rows made |
| a11y/selection walk 3,000 item rows, UIA | 0.444 | 0.446 | 24.8 | 24.7 | 3.00 / 3.00 rows made |
| a11y/selection walk beside details, keys 60 ms apart, real loop, UIA | 0.707 | 0.717 | 122 | 122 | 0.00 / 0.00 a11y trees/key |
| a11y/selection walk beside details, keys 500 ms apart, real loop, UIA | 0.733 | 0.754 | 123 | 122 | 0.00 / 0.00 a11y trees/key |
| a11y/type in rich text, UIA | 0.781 | 0.810 | 52.2 | 52.3 |  |
| a11y/progress bar ticks over 1,000 rows, UIA | 0.245 | 0.262 | 0.0 | 0.0 |  |
| a11y/caret walk in rich text beside 300 rows, UIA | 0.320 | 0.332 | 0.9 | 0.8 | 0.00 / 0.00 a11y trees/key |
| a11y/type in a 2,000-line TextArea, UIA | 0.528 | 0.564 | 259 | 259 |  |
| a11y/caret walk in a 2,000-line TextArea, UIA | 0.490 | 0.490 | 1.0 | 0.9 | 0.00 / 0.00 a11y trees/key |
| a11y/context menu walk over 1,000 rows, UIA | 0.377 | 0.396 | 0.5 | 0.5 | 0.00 / 0.00 a11y trees/key |
| a11y/command palette typing over 2,000 commands, UIA | 1.452 | 1.501 | 485 | 480 | 0.00 / 0.00 a11y trees/key |
| **Media** (4) | | | | | |
| media/video player playing, position 10/s, real loop | 39.746 | 39.358 | 12.2 | 12.0 | 0.00 / 0.00 builds/report |
| media/video player bar fade, real loop | 52.214 | 59.931 | 58.3 | 56.9 | 2.00 / 2.00 builds/fade |
| media/scrubber hover, 1 kHz mouse, real loop | 15.261 | 15.125 | 22.8 | 22.7 | 0.00 / 0.00 builds/frame |
| media/scrubber hover with a preview card, 1 kHz mouse, real loop | 17.237 | 16.930 | 23.5 | 23.5 | 0.00 / 0.00 builds/frame |
| **Loop (waits and wakes)** (7) | | | | | |
| loop/idle window, 5 s, real loop | 0.000 | 0.000 | 0.3 | 0.2 | 0.20 / 0.20 passes/s |
| loop/minimized window changed, 5 s, real loop | 0.016 | 0.025 | 0.9 | 0.9 | 0.20 / 0.20 passes/s |
| loop/toast held 2 s, real loop | 5.251 | 5.453 | 10.2 | 10.2 | 19.24 / 19.24 passes/s |
| loop/tooltip delay, real loop | 0.488 | 0.489 | 2.8 | 2.8 | 8.00 / 8.00 passes/s |
| loop/toast over a screen built anew, real loop | 5.231 | 5.447 | 512 | 512 | 2.00 / 2.00 builds/toast |
| loop/spinner on a screen built anew, rebuilt per step, real loop | 8.845 | 9.644 | 7,620 | 7,619 | 1.00 / 1.00 rebuilds/frame |
| loop/spinner on a screen built anew, turned in place, real loop | 7.474 | 7.667 | 0.4 | 0.4 | 0.00 / 0.00 rebuilds/frame |
| **Terminal** (2) | | | | | |
| terminal/coloured screen, forced frame | 0.796 | 0.853 | 0.0 | 0.0 |  |
| terminal/dense colour screen, unique rows, forced frame | 5.740 | 6.275 | 0.0 | 0.0 |  |

For context, the ledger recorded these rows' before and after on the day each change landed (against that day's code, not 520002e9, and not part of this run):

- `loop/idle window`: 62.6 → 0.2 loop passes per second (R3 aa11334, 46e5b33, faf8ab2: the idle wait runs to the next owned deadline); `loop/toast held 2 s` 71.3 → 19.7; `loop/tooltip delay` 66.4 → 8.0. Today: 0.2, 19.2, 8.0.
- `anim/signal tween, 1 kHz posts`: 15.1 → 1.0 rebuilds per frame (R2 883943f, e5d626e, b10d022: animations step once per frame). Today: 1.0.
- `loop/toast over a screen built anew`: 23.0 → 2 builds per toast, 5,941 KB at first (R3 c3ed8ca: the fade written in place). Today: 2.0, 512 KB.
- `media/video player playing`: 408.8 → 12.4 KB per step (R3 fb5d935: the position written through refs, no rebuild). Today: 12.2.
- `text/rebuild around highlighted JSON`: 1.696 → 0.399 ms JIT (R2 dc079b8); `text/open highlighted JSON`: 6.23 → 0.57 ms JIT (R4 a476866c, monospace ASCII lines fit unmeasured). Today: 0.344 and 0.520.
- The selection walks compare variants side by side: 19 rows made per key with a one-argument template, 3 on item rows (R2 bff6de2), 1 when the list draws the highlight (R5 0d3bc10a); the real-loop walks build 0 accessibility trees per key (R5 a11y-layout-refresh-in-place; 1.0 at a reader's pace before).

### Caveats

- **Runtime.** Both JIT sides ran on .NET 10.0.14, the newest .NET 10 runtime installed (both runtimeconfigs ask for 10.0.0 and roll forward to the latest patch; the build settings and package versions are the same at both commits). Both AOT publishes were made with SDK 10.0.300 and report runtime 10.0.8, the ILCompiler pack it carries. Both sides were measured today, so neither the 2026-10-07 runtime nor older captures enter the comparison.
- **The baseline's own code is unchanged in the after suite.** `git diff 520002e9 5b8ac7d8` over the suite's scenario files and `Ui.cs` removes no line: every baseline row's setup and step are word for word the same, element counts match on all 83 rows, and no row in either suite failed its check or rendered nothing. The harness changes are additions (figures, a timed-window hook, loop rows); one line moved the window's `Performance` lookup inside the timed span, a few nanoseconds.
- **The suites differ in what runs around a row.** Rounds run every scenario in turn, and the after suite runs 151 scenarios where the baseline runs 83, so a shared row meets a different JIT and GC state. The README records one such case (media rows placed before the a11y rows moved three of them 20–25 % on JIT). The NativeAOT runs, which have no tiering, agree with JIT on every large move.
- **Some wins rest on approved behaviour changes,** not only on faster code. Among the matched rows: `a11y/type in rich text` (the editor's accessible text made when a reader asks, and no Value change for nodes that offer text, R2; AT-SPI with a reader listening still builds the text per keystroke) and `component/signal fan-out` (the `[ThreadStatic]` reactive scope, R2: reads off the UI thread no longer subscribe). Among rows without a baseline: animations step once per frame and range values are told at most every 250 ms (both R2).
- **Render is CPU raster.** Headless frames draw into a CPU Skia surface, so the paint rows' flat time says nothing about GPU windows; their allocation (now 0) carries over.
- **AOT code placement.** The README records `grid/` rows moving 5–10 % on AOT with unrelated code. The grid wins (−63…−78 %) are far past that; AOT-only moves under ~8 % in untouched code should be read with that in mind.
- **Machine load.** Every run waited for the idle gate (CPU under 10 % for 20 s, no test host or game). Freshly rebooted, the machine still had short bursts during four of the six JIT runs (a 2-second sample peaking at 27–64 %; run means 2.4–9.6 %); AOT runs averaged 1.4–2.3 % except after-3 at 9.1 %. The min over 3 runs absorbs this: a row's per-run mins differ by 1–2 % at the median and 3–4 % at the 90th percentile, on both sides and both builds.
- **Capture commit field.** The suite asks `git describe` in its own folder, and the runs started from copies outside the trees, so the field came out empty; it was filled in (520002e9, 5b8ac7d8) when the captures were saved to the repository.

### How the numbers were taken

- Baseline worktree `.claude/worktrees/plan17-baseline`, detached at 520002e9, left in place and clean for part B (its build outputs remain under its `bin/` and `obj/`).
- Builds: `dotnet build -c Release samples/HultUI.Samples.PerfSuite` in each tree, one at a time (the after tree rebuilt with `--no-incremental`); JIT outputs copied to the scratchpad and run with `dotnet <dll>`; NativeAOT with `<PublishAot>true</PublishAot>` added to the suite's csproj for the publish only (`dotnet publish -r win-x64 -c Release`), then removed again in both trees.
- Runs: full suite, 5 rounds × 5 steps (no `--quick`), `--json`; order base, after, base, after, base, after for JIT, then the same for AOT; `scripts/perf/idle-gate.ps1` (after tree's copy) before every run; the suite pins itself to the 16 fastest logical processors and raises its priority; a thread job sampled machine CPU every 2 s during each run.
- Captures: `samples/HultUI.Samples.PerfSuite/captures/2026-10-26-520002e-total-base-{jit,aot}-{1,2,3}.json` and `2026-10-26-5b8ac7d-total-after-{jit,aot}-{1,2,3}.json`. Scripts, run logs (`gate.log`, `runs/*.txt`), the recheck captures and per-row CSVs (`compare-full-jit.csv`, `compare-full-aot.csv`) are in the scratchpad folder `total`.

## Part B: the apps

**Baseline** 520002e9 (main on 2026-10-07, just before plan 17) against **after** 3c2afc67 (round 5 released; its code is main f31194ab). Measured 2026-10-27 between 01:10 and 05:25 on the i7-14700K after a reboot, every timed launch behind the idle gate and pinned to the P-cores, base and after alternated. Two kinds of measurement:

- **Headless key walks**, 23 walks over the five apps, JIT and NativeAOT, 3 launches per side and build (276 walk results). Bytes per key are UI-thread allocations; time per key is the UI thread's CPU per key (min and median), because headless sessions wait for git and other producers on the UI thread.
- **Real windows**, NativeAOT, 56 launches: 5 per side and app (start-up, 20 s idle, memory, a key walk through the window's input path), plus 3 per side of Meridian typing into an 89,000-character request body.

**In one line:** on every app walk, bytes per key fell 51–99 % (summed over the 23 walks −90 %, on both builds) and UI-thread CPU per key fell on 20 of 23 walks by 5 % or more (geometric mean −26 % AOT, −23 % JIT); an idle window went from about 75 UI-thread wakes a second to none; memory after a walk is 10–22 % lower in the three GL apps; gen-2 GCs while typing in a large Meridian body fell 127 → 1. Start-up did not move.

### Headline per app

NativeAOT unless marked; base → after. "CPU/key" is the median UI-thread CPU per key of the headless walk; "work/key" is the real window's handler plus frames per key (median).

| App | Key walks: KB per key | Key walks: CPU / work per key | Start-up (median of 5) | Idle UI-thread wakes/s | Memory after a 100-key walk (private, WS) | GC |
|---|---|---|---|---|---|---|
| **Junction** | History 670 → 60 (−91 %); with a reader 1,175 → 66 (−94 %); summary typing 1,373 → 74 (−95 %); review comment 2,218 → 155 (−93 %); real window 1,520 → 62 (−96 %) | History 2.38 → 1.90 ms (−20 %), with a reader at 500 ms 2.70 → 2.02 (−25 %); typing −36 %, review −37 %; real window 3.70 → 1.35 ms (−64 %), p95 6.57 → 1.95 | 263 → 254 ms (−3 %, noise) | 75.4 → 0 | 328 → 269 MB (−18 %), 307 → 260 MB (−15 %); live 324 → 253 MB (−22 %), LOH 37.3 → 2.6 MB | walk: gen-1 27 → 22, gen-2 7 → 5, pause 242 → 183 ms (mostly the highlight worker) |
| **Vesper** | Detail 873 → 44 (−95 %); partner 917 → 59; Brief 797 → 34; Thumbnails 839 → 61; Insert 1,151 → 300 (−74 %); folder in/out 1,285 → 326 (−75 %); real window 1,020 → 60 (−94 %) | Detail 2.01 → 1.47 ms (−27 %), with a reader 3.04 → 1.50 (−51 %); all six walks −13…−27 %; real window 1.97 → 1.28 ms (−35 %) | 283 → 270 ms (−5 %, noise) | 81.3 → 0 | 139 → 120 MB (−14 %), 126 → 111 MB (−12 %) | walk: 6 gen-0 GCs → 0 |
| **Meridian** | URL typing, 25 requests 329 → 69 (−79 %), 200 requests 1,223 → 70 (−94 %); History 464 → 52 (−89 %); find in a 2,000-line response 984 → 430 (−56 %); real-window History 647 → 58 (−91 %) | URL typing −18 % / −32 %; find −28 %; History median flat (−2 %, min −11 %); real window 1.87 → 1.43 ms (−24 %) | 267 → 264 ms (−1 %) | 74.3 → 0 | 171 → 153 MB (−10 %), 118 → 109 MB (−8 %) | 89k-character body, 507 keys: gen-2 127 → 1, pause 100 → 29 ms (−71 %), frame p99 2.92 → 2.20 ms |
| **Aperture** | Poster grid Down 405 → 182 (−55 %), Right 401 → 112 (−72 %) | −11 % / −15 % | 349 → 356 ms (+2 %, noise) | 75.8 → 0 | idle (sign-in page) 158 → 157 MB, flat | – |
| **Reel** | Steady playback 45.4 → 0.7 KB per frame (−98 %), with a reader 47.8 → 0.7 | flat (5.85 ms a frame, all CPU raster) | 348 → 346 ms (−1 %) | 76.2 → 0 | idle (no file) 150 → 149 MB, flat | – |

What the headline says:

- **Bytes per key were the big change, and they reach the real window.** The headless walks and the real-window walks agree: Junction History 670 KB per key headless, 1,520 in a real window (where the baseline drew 2.4–2.5 frames per key) → 60–62 in both; Vesper Detail 873 / 1,020 → 44 / 60; Meridian History 464 / 647 → 52 / 58. At the baseline the AOT walks allocated 2–15 % more than JIT (the AOT-only allocation part A found in the framework rows); after, AOT is within −8…+5 % of JIT on every walk.
- **Time per key fell less than bytes, but on almost every walk.** Headless CPU per key (AOT median) fell 13–51 % on 19 walks and 11 % on Aperture's Down walk; it stayed flat on Meridian's History (−2 % median, −11 % min) and on Reel, whose frame is a full-window CPU raster in both trees. In real windows, where the GPU draws, the per-key work fell 24–64 % and the 95th percentile 21–70 %.
- **With a reader listening, the baseline built a whole accessibility tree on half to all of the keys; the after tree builds none** on every walk except the review composer (1.0 per key in both). That is where the largest time cuts are: Vesper Detail with a reader −51 %, Junction History at 60 ms with a reader −46 %.
- **Idle is now asleep.** At the baseline every app's UI thread woke about 75 times a second with nothing to do; after, 0.00 in all five apps, with UIA clients listening.
- **Start-up is unchanged** within its noise (a launch-to-launch range of 10–130 ms; the largest median move is Vesper's −13 ms, with a −4 ms min).

### The biggest wins and what explains them

| Walk | Before → after (AOT) | What explains it (round, commit) |
|---|---|---|
| Junction review comment typing, with a reader | 5,026 → 215 KB/key (−96 %); CPU 3.93 → 2.14 ms (−46 %); 23 gen-0 + 20 gen-1 GCs → 1 | R4 3135de73 (review keeps the woven rows across caret moves, 1,011 → 157 KB/key then), R5 b1a12ef1 (composer edits are view-only builds that remake one row, 1,367 → 209 KB/char then) |
| Junction Changes summary typing, with a reader | 2,174 → 75 KB/key (−97 %); CPU 3.78 → 2.28 ms (−40 %); trees 1.0 → 0 | R3 0720260 (diff rows flattened once, on item rows), 22f1139 (diff rows named for readers once), R4 b35fc587 (chrome in parts), fbec1130 (restyle only what changed), R5 ce70fec7 (TextArea and Checkbox patches keep layout) |
| Vesper Detail, Brief, partner walks | 797–931 → 34–60 KB/key (−93…−96 %); CPU −25…−51 % | R3 0971573 (panes on item rows, 561 → 222 KB/key then), R4 598441d4 (the F3 partner tells two rows), e6a432fa (grids keep tiles and cells), R5 fcf366c6 (per-pane parts, 80 % of the build), 76b6f78e + 574f43a7 (handler lists kept, one handler array) |
| Junction History, Down at 60 ms and at a reader's pace | 670 / 1,175 → 60 / 66 KB/key (−91 / −94 %); CPU −20 / −25 % | R2 73b1c10 (History on item rows, 212 → 41 KB/key then), R3 b985e74 (a landing highlight rebuilds only for the shown key), 3730052 (a selection move is a patch, not a tree), R4 b35fc587, R5 ea6ceb7a (include bar and footer kept), a11y-layout-refresh-in-place (R5 bd88e747 group: 1.0 → 0 trees per key at 500 ms) |
| Meridian URL typing with 200 requests | 1,223 → 70 KB/key (−94 %); CPU −32 %; 25 requests: 329 → 69 | R5 ff6262ff (request tree, response pane and chrome as kept parts; URL edits are view-only builds), so the request count no longer matters (70 KB with 25 or 200); R2 dc079b8 + 5943f4f (a rebuild hands the response text back without re-wrapping) |
| Meridian History Down | 464 → 52 KB/key (−89 %) | R5 fe50f969 (History on item rows with one kept label delegate, 355 → 109 KB/key then) |
| Meridian typing in an 89k-character body | gen-2 GCs 127 → 1; GC pause 100 → 29 ms; process CPU 1,109 → 766 ms (−31 %) | R5 828a7d69 (the apps' large-object threshold 85,000 → 524,288 B: each keystroke's new 178 KB document string stays in gen 0) |
| Aperture poster grid | Right 401 → 112, Down 405 → 182 KB/key | R3 921605c (the grid on item rows, rows made per key 6 → 1.1/2.5) |
| Reel steady playback | 45.4 → 0.7 KB per frame | R3 fb5d935 (position and scrubber written through refs, no rebuild), f80d57a (bar fade in place), R2 883943f (animations step once per frame), R1 7b095f0 (zero-allocation frame floor) |
| Idle (all five apps) | 75 → 0 UI-thread wakes/s | R3 aa11334, 46e5b33, faf8ab2 (the idle wait runs to the next owned deadline, or forever) |
| Junction memory after a walk | private −18 %, live −22 %, LOH 37.3 → 2.6 MB live | the walk's 96 % fewer UI-thread bytes, and R5 828a7d69 (fewer large strings on the large-object heap); the highlight worker's ~10 MB per key is unchanged (below) |

### Anything worse

Nothing got measurably worse on what a user feels. The rises found, all small or explained:

- **Meridian History, headless AOT, 90th percentile 2.69 → 3.47 ms (+29 %),** with the median flat (−2 %) and the min −11 %. In all three after-AOT runs a stretch of about 9 consecutive keys near the start of the walk costs ~3.4 ms each, with the same bytes as the rest. JIT's p90 fell 9 %, and the real-window History walk (AOT, GPU) fell at p50 (−24 %) and p95 (−21 %). Not traced further.
- **Aperture start-up +6 ms median (+2 %), +4 ms min**; Junction's min +1 ms, Reel's +1 ms. All inside the launch-to-launch spread (Aperture's baseline ran 341–472 ms).
- **More gen-0 and gen-1 GCs while typing in the 89k body** (39 / 1 → 50 / 4): the price of the large-object threshold, since the document strings now land in gen 0. The total pause still fell 71 %.
- **Uncollected heap at idle can read higher:** Junction's `GC.GetTotalMemory` after start-up 13.7 → 23.6 MB and Vesper's after the walk 6.5 → 7.7 MB are garbage not yet collected (the after Vesper walk made no GC at all). Live data after a full collection is lower in both (8.0 → 7.1, 2.9 → 1.6 MB). Meridian's GC committed after a full GC at idle is 4.3 → 4.8 MB (+0.5 MB).

Not worse, but unchanged and now the largest cost left: **Junction's highlight prefetch** allocates about 10 MB per History key on a worker on both sides (headless all-thread 10,448 → 9,815 KB/key) and keeps one core busy through a real-window walk (process CPU ~16 s in a 15.5 s walk, both trees; all-thread 1,149 → 1,040 MB). It drives most of the walk's GC pause (242 → 183 ms). It is the open candidate `apps-junction-highlight-prefetch`.

### Headless key walks

Each walk: warm-up keys, then 30–240 measured keys (80–100 for most), in a fresh session at 1440×900 (Vesper 1240×820, Reel 1280×760). KB per key is UI-thread bytes, mean of 3 runs (runs agree within 2 %, except Aperture's baseline JIT and Vesper's Thumbnails after, within 5 %). CPU per key in ms: min over the runs' mins, median of the runs' medians. Trees: accessibility trees built per key (AOT; JIT is the same). Base → after.

| App, walk | JIT KB/key | AOT KB/key | AOT CPU min | AOT CPU median | JIT CPU median | Trees/key |
|---|---|---|---|---|---|---|
| Junction History, Down every 60 ms | 640 → 57 (−91 %) | 670 → 60 (−91 %) | 2.100 → 1.753 (−17 %) | 2.382 → 1.896 (−20 %) | 2.314 → 1.915 (−17 %) | – |
| Junction History, 60 ms, recording bridge | 878 → 59 (−93 %) | 902 → 62 (−93 %) | 2.110 → 1.787 (−15 %) | 3.591 → 1.945 (−46 %) | 3.390 → 1.942 (−43 %) | 0.50 → 0 |
| Junction History, every 500 ms | 686 → 59 (−91 %) | 721 → 62 (−91 %) | 2.265 → 1.772 (−22 %) | 2.480 → 1.953 (−21 %) | 2.366 → 1.869 (−21 %) | – |
| Junction History, 500 ms, recording bridge | 1,139 → 63 (−95 %) | 1,175 → 66 (−94 %) | 2.471 → 1.842 (−25 %) | 2.704 → 2.024 (−25 %) | 2.504 → 1.951 (−22 %) | 1.00 → 0 |
| Junction Changes, commit summary typing | 1,299 → 76 (−94 %) | 1,373 → 74 (−95 %) | 3.383 → 2.087 (−38 %) | 3.566 → 2.279 (−36 %) | 3.300 → 2.308 (−30 %) | – |
| Junction Changes, summary typing, bridge | 1,998 → 76 (−96 %) | 2,174 → 75 (−97 %) | 3.589 → 2.069 (−42 %) | 3.781 → 2.281 (−40 %) | 3.425 → 2.247 (−34 %) | 1.00 → 0 |
| Junction review comment typing | 2,146 → 157 (−93 %) | 2,218 → 155 (−93 %) | 3.063 → 1.905 (−38 %) | 3.283 → 2.077 (−37 %) | 2.977 → 2.055 (−31 %) | – |
| Junction review comment typing, bridge | 4,737 → 217 (−95 %) | 5,026 → 215 (−96 %) | 3.689 → 1.962 (−47 %) | 3.927 → 2.137 (−46 %) | 3.418 → 2.062 (−40 %) | 1.00 → 1.00 |
| Vesper Detail, 60 ms | 836 → 44 (−95 %) | 873 → 44 (−95 %) | 1.823 → 1.329 (−27 %) | 2.013 → 1.474 (−27 %) | 1.979 → 1.485 (−25 %) | – |
| Vesper Detail, 60 ms, recording bridge | 882 → 45 (−95 %) | 931 → 45 (−95 %) | 1.875 → 1.333 (−29 %) | 3.035 → 1.496 (−51 %) | 2.869 → 1.503 (−48 %) | 0.50 → 0 |
| Vesper partner (F3) walk, both panes on one folder | 879 → 60 (−93 %) | 917 → 59 (−94 %) | 1.873 → 1.385 (−26 %) | 2.079 → 1.554 (−25 %) | 1.934 → 1.535 (−21 %) | – |
| Vesper Brief | 770 → 34 (−96 %) | 797 → 34 (−96 %) | 1.686 → 1.233 (−27 %) | 1.899 → 1.382 (−27 %) | 1.794 → 1.376 (−23 %) | – |
| Vesper Thumbnails, 88 pictures | 805 → 61 (−92 %) | 839 → 61 (−93 %) | 3.983 → 3.425 (−14 %) | 4.252 → 3.707 (−13 %) | 4.416 → 3.958 (−10 %) | – |
| Vesper Insert marks | 1,116 → 302 (−73 %) | 1,151 → 300 (−74 %) | 2.036 → 1.518 (−25 %) | 2.225 → 1.734 (−22 %) | 2.087 → 1.752 (−16 %) | – |
| Vesper folder in and out, every 300 ms | 1,257 → 329 (−74 %) | 1,285 → 326 (−75 %) | 2.184 → 1.641 (−25 %) | 2.314 → 1.788 (−23 %) | 2.191 → 1.797 (−18 %) | – |
| Meridian URL typing, 25 requests | 306 → 70 (−77 %) | 329 → 69 (−79 %) | 1.121 → 0.924 (−18 %) | 1.345 → 1.098 (−18 %) | 1.323 → 1.169 (−12 %) | – |
| Meridian URL typing, 200 requests | 1,188 → 70 (−94 %) | 1,223 → 70 (−94 %) | 1.439 → 0.968 (−33 %) | 1.640 → 1.110 (−32 %) | 1.517 → 1.084 (−29 %) | – |
| Meridian History, 300 entries, 60 ms | 406 → 53 (−87 %) | 464 → 52 (−89 %) | 2.272 → 2.013 (−11 %) | 2.494 → 2.444 (−2 %) | 2.338 → 2.137 (−9 %) | – |
| Meridian find typing in a 2,000-line JSON response | 942 → 431 (−54 %) | 984 → 430 (−56 %) | 1.959 → 1.286 (−34 %) | 2.417 → 1.747 (−28 %) | 2.253 → 1.696 (−25 %) | – |
| Aperture poster grid, Down and back (400 films) | 372 → 183 (−51 %) | 405 → 182 (−55 %) | 1.156 → 1.146 (−1 %) | 1.494 → 1.337 (−11 %) | 1.448 → 1.321 (−9 %) | – |
| Aperture poster grid, Right and back | 367 → 113 (−69 %) | 401 → 112 (−72 %) | 1.285 → 1.085 (−16 %) | 1.469 → 1.249 (−15 %) | 1.444 → 1.225 (−15 %) | – |
| Reel playback, 24 fps, KB per frame | 44.2 → 0.8 (−98 %) | 45.4 → 0.7 (−98 %) | 5.816 → 5.709 (−2 %) | 5.896 → 5.853 (−1 %) | 5.947 → 5.925 (−0 %) | – |
| Reel playback, 24 fps, recording bridge | 46.5 → 0.7 (−99 %) | 47.8 → 0.7 (−99 %) | 5.744 → 5.708 (−1 %) | 5.872 → 5.860 (−0 %) | 5.891 → 5.917 (+0 %) | 0.33 → 0 |

- **Summed over the 23 walks:** JIT 23,739 → 2,559 KB per key (−89 %), AOT 24,952 → 2,555 (−90 %). Geometric mean of CPU per key after/base: AOT median 0.741, min 0.758; JIT median 0.774, min 0.802. Walks ≥5 % faster at the median: 20 of 23 on AOT, 21 on JIT. None rose by more than 0.4 %.
- **GCs during the walks** (AOT, per run): the baseline collected 1–23 times in the typing and Vesper walks, the after tree 0–2 (Junction review with a bridge 23 + 20 → 1 + 0; Meridian find 9 / 9 / 8 → 2 / 0 / 0). Junction's History walks collect ~58 gen-0 and ~17 gen-1 times on both sides: the highlight worker.
- **Worker bytes.** All-thread KB per key equals the UI thread's on every walk except Junction History (10.4–12.4 MB per key on both sides, −5…−10 %) and Vesper's folder walk (1,310 → 350).
- **Count checks.** Trees per key came from watching the window's last tree; in the after tree it matched the window's own counter on every walk. The after tree's bridge received 1.0–1.4 patches per key where the baseline built trees (selection, caret and value patches instead of a tree).

### Real windows

NativeAOT hosts, default frame, 1440×900 at 150 % (Vesper 1240×820, Reel 1280×760); GL for Junction, Vesper and Meridian, Vulkan with colour events for Aperture and Reel, as the apps choose. Medians of 5 launches per side (3 for the typing run).

#### Start-up to the first frame shown

From `HULTUI_STARTUP` ("first frame shown", time since the OS started the process).

| App | Base median (min–max) | After median (min–max) | Δ median | Δ min | Each launch, base / after |
|---|---|---|---|---|---|
| Junction | 263 (250–291) | 254 (252–284) | −8 ms (−3 %) | +1 | 291 251 263 250 274 / 265 252 255 254 284 |
| Vesper | 283 (272–305) | 270 (268–288) | −13 ms (−5 %) | −4 | 305 291 281 272 283 / 288 268 272 268 270 |
| Meridian | 267 (263–273) | 264 (257–277) | −3 ms (−1 %) | −6 | 268 273 267 263 267 / 257 264 266 277 260 |
| Aperture | 349 (341–472) | 356 (346–360) | +6 ms (+2 %) | +4 | 472 347 355 349 341 / 346 358 356 360 346 |
| Reel | 348 (342–378) | 346 (343–364) | −2 ms (−1 %) | +1 | 342 363 348 378 346 / 348 364 343 346 347 |

None clears the 10 ms start-up bar with a consistent min. The phases agree: the window is made at the same moment on both sides (SDL_CreateWindow at t ≈ 200 ms for the GL apps, ≈ 105 ms for the Vulkan ones). Plan 17 did not aim at start-up after round 1 (4315c4b measured it unchanged).

#### Idle, 20 s after start-up

| App | UI-thread wakes/s | Process context switches/s | Process CPU ms/s | Frames/s |
|---|---|---|---|---|
| Junction | 75.4 → 0.00 | 261 → 186 | 1.56 → 0.78 | 0 → 0 |
| Vesper | 81.3 → 0.00 | 272 → 186 | 0.78 → 0.00 | 0 → 0 |
| Meridian | 74.3 → 0.00 | 261 → 187 | 0.78 → 0.78 | 0 → 0 |
| Aperture | 75.8 → 0.00 | 461 → 385 | 1.56 → 0.78 | 0 → 0 |
| Reel | 76.2 → 0.00 | 462 → 385 | 2.34 → 1.56 | 0 → 0 |

The remaining process switches (186 a second in the GL apps, 385 in the Vulkan ones) are the runtime's and drivers' own threads; there are 74–86 fewer on every app, which is the UI thread's old polling. Process CPU is read at the OS's 15.6 ms resolution, so its column only says "about 1 ms a second, both sides".

#### Memory

MB, medians of 5. "Heap" is `GC.GetTotalMemory(false)` (garbage included); "live" lines follow a full blocking collection.

| App, moment | Private | Working set | GC heap | GC committed | LOH |
|---|---|---|---|---|---|
| Junction, after start-up idle | 151.0 → 134.9 (−11 %) | 127.1 → 120.2 (−5 %) | 13.7 → 23.6 | 32.0 → 22.2 (−31 %) | 2.2 → 1.3 |
| Junction, idle, live | 149.2 → 135.1 (−9 %) | 125.5 → 120.5 (−4 %) | 8.0 → 7.1 (−11 %) | 30.9 → 26.2 (−15 %) | 2.2 → 0.0 |
| Junction, after the 100-key walk | 327.6 → 269.4 (−18 %) | 306.5 → 259.8 (−15 %) | 120.5 → 63.7 (−47 %) | 163.4 → 114.8 (−30 %) | 37.6 → 19.1 (−49 %) |
| Junction, after the walk, live | 323.9 → 253.0 (−22 %) | 302.8 → 243.0 (−20 %) | 82.0 → 54.6 (−33 %) | 159.6 → 98.0 (−39 %) | 37.3 → 2.6 (−93 %) |
| Vesper, after start-up idle | 115.1 → 101.7 (−12 %) | 97.5 → 89.1 (−9 %) | 8.5 → 2.9 | – (no GC yet) | – |
| Vesper, idle, live | 116.1 → 104.0 (−10 %) | 98.7 → 90.2 (−9 %) | 2.0 → 1.5 | 10.1 → 5.5 (−45 %) | 0 → 0 |
| Vesper, after the walk | 139.4 → 119.5 (−14 %) | 126.2 → 110.5 (−12 %) | 6.5 → 7.7 | 23.7 → 5.5 (−77 %) | 0.1 → 0 |
| Vesper, after the walk, live | 140.0 → 119.6 (−15 %) | 126.6 → 110.5 (−13 %) | 2.9 → 1.6 (−46 %) | 24.2 → 10.5 (−57 %) | 0 → 0 |
| Meridian, after start-up idle | 117.2 → 109.0 (−7 %) | 89.0 → 88.9 (0 %) | 3.9 → 3.4 | – (no GC yet) | – |
| Meridian, idle, live | 117.3 → 110.0 (−6 %) | 88.4 → 89.4 (+1 %) | 0.9 → 0.9 | 4.3 → 4.8 (+11 %) | 0 → 0 |
| Meridian, after the walk | 170.7 → 152.8 (−10 %) | 118.2 → 108.6 (−8 %) | 19.0 → 7.7 (−59 %) | 21.0 → 4.8 (−77 %) | 0.1 → 0 |
| Meridian, after the walk, live | 171.3 → 152.7 (−11 %) | 118.8 → 108.5 (−9 %) | 2.2 → 1.8 | 21.6 → 10.9 (−50 %) | 0 → 0 |
| Aperture (sign-in page), after start-up idle | 157.5 → 156.6 (−1 %) | 111.4 → 111.4 (0 %) | 1.9 → 1.5 | – | – |
| Aperture, idle, live | 158.3 → 157.3 (−1 %) | 111.7 → 111.8 (0 %) | 0.7 → 0.7 | 3.0 → 2.7 | 0 → 0 |
| Reel (no file), after start-up idle | 150.0 → 149.3 (0 %) | 110.3 → 110.5 (0 %) | 1.5 → 1.4 | – | – |
| Reel, idle, live | 150.5 → 149.9 (0 %) | 110.5 → 110.8 (0 %) | 0.6 → 0.7 | 2.5 → 2.3 | 0 → 0 |

- **After a walk**, the three apps with one are 10–22 % lower in private bytes and 8–20 % lower in working set, and their GC committed is 30–77 % lower. Junction's large-object heap after a walk holds 2.6 MB of live data instead of 37.3 (the large-object threshold and far fewer large strings).
- **At idle**, the three GL apps are 8–16 MB lower in private bytes before any walk, while the two Vulkan apps are flat. That fits round 1's dropped GL depth buffer (4315c4b; a 2160×1350 depth buffer is about 11.7 MB of driver memory), but it was not traced here.

#### Key walks through the window's input path

A Down key every 150 ms (released in the same post), 100 keys, through `Window.HandleInputEvent`; Junction History, Vesper Detail (61 files), Meridian History (300 entries). "Work" is the key's handler plus every frame presented before the next key.

| App | UI KB/key | Work/key p50 | Work p95 | Frame p50 | Frame p95 | Frames per key | GC pause in walk | GCs gen 0/1/2 | All-thread MB in walk |
|---|---|---|---|---|---|---|---|---|---|
| Junction | 1,520 → 62 (−96 %) | 3.70 → 1.35 ms (−64 %) | 6.57 → 1.95 (−70 %) | 1.27 → 1.15 (−9 %) | 2.62 → 1.76 (−33 %) | 2.4 → 1.0 | 242 → 183 ms | 38/27/7 → 38/22/5 | 1,149 → 1,040 |
| Vesper | 1,020 → 60 (−94 %) | 1.97 → 1.28 ms (−35 %) | 2.88 → 1.86 (−35 %) | 1.62 → 1.09 (−33 %) | 2.39 → 1.67 (−30 %) | 1.0 → 1.0 | 4.8 → 0 ms | 6/0/0 → 0/0/0 | 100 → 6 |
| Meridian | 647 → 58 (−91 %) | 1.87 → 1.43 ms (−24 %) | 2.72 → 2.14 (−21 %) | 1.74 → 1.35 (−22 %) | 2.59 → 2.07 (−20 %) | 1.0 → 1.0 | 3.3 → 0 ms | 3/0/0 → 0/0/0 | 63 → 6 |

Junction's baseline drew 2.4–2.5 frames per key (a frame for the key-up, and a second build when the highlight landed); after, one (R4 5250e1f: inputs that run no handler draw nothing; R3 b985e74).

#### Meridian: typing into an 89,000-character request body

The body is the first 89,000 characters of TODO.md; the caret in the middle; 507 characters typed 100 ms apart (round 5 step 6's run, three times as long).

| Side | Gen-2 GCs | Gen-0 / gen-1 | GC pause | Frame p95 / p99 | Work per key p50 / p95 / p99 | UI KB/key | Process CPU | Private after |
|---|---|---|---|---|---|---|---|---|
| Base (3 runs) | 127 (127–127) | 39 / 1 | 100 ms (98–102) | 2.34 / 2.92 ms | 1.60 / 3.06 / 3.79 ms | 2,226 | 1,109 ms | 180.8 MB |
| After (3 runs) | 1 (1–1) | 50 / 4 | 29 ms (28–29) | 1.35 / 2.20 ms | 1.26 / 2.01 / 2.88 ms | 1,823 | 766 ms | 180.1 MB |

The baseline ran a background gen-2 collection every 4 keystrokes. Round 5 step 6 measured 103 → 2 gen-2 and −48 % pause for the threshold alone; against 520002e9 the pause fell 71 % and the frame p95 42 %, since the text changes since then also made each keystroke cheaper (2,226 → 1,823 KB, a new 178 KB string each time remains: `textarea-chunked-document` is the user's chosen next step).

### What could not be measured identically

- **Real app executables were not launched.** At the baseline, the apps' custom frames activate the window inside the `Application` constructor (`WindowsActivation`, which honours `SDL_HINT_WINDOW_ACTIVATE_WHEN_SHOWN=0` for them, arrived in round 4, 445b34c8), so a baseline Junction, Vesper, Meridian or Aperture exe would take the foreground. Every real window here comes from a host (from round 5 step 6's) that makes each app as its `Program.cs` does — title, size, minimum size, theme, renderer (Vulkan with colour events for Aperture and Reel), `MpvBackend.IsAvailable` with the same libmpv — but with the default frame, no settings file, no saved placement and no single-instance claim. Each host is a NativeAOT publish built like the apps (Speed, ICU, and the after tree's `apps/AppRuntime.props`). `Program.cs` differs in one line between the trees (Meridian's minimum size reads two constants), so the start-up path is otherwise the apps'.
- **Loop passes per second at the baseline.** The frame log that counts passes (`HULTUI_FRAMELOG`) arrived in round 3 (3c81fb6). Idle is reported as the UI thread's context switches per second (every pass that waits ends in one; read with `NtQuerySystemInformation`), plus process CPU and frames drawn.
- **Accessibility trees per key.** The baseline has no counter (`Window.AccessibilityTreesBuilt` is round 5). Both trees are counted the same way: a new tree object in `Window._previousA11yTree` after a pumped frame. Headless walks use the recording bridge; a headless UIA bridge does not exist at the baseline. Real windows ran with UIA clients listening system-wide (`UiaClientsAreListening` was true in all 56 launches), so the UIA bridge was live in every one.
- **Aperture's grid at the baseline loads only its first 100 films** (the paging bug fixed in round 3, fc051314). Both grid walks stay inside the first 100 (8 rows down and back; 20 cards right and back). On a full library the baseline's arrows would stop at film 100. In a real window Aperture had no server (the lab server and its key were not used), so it shows its sign-in page: start-up, idle and memory only.
- **Reel's playback is the fake backend, headless only:** `FakeMediaBackend` publishes a 640×360 frame and a position every 1/24 s, no decoding. The real-window Reel opens no file. Real Vulkan video frames were measured in rounds 4 and 5 (f9980647: 395 → 35 B per F16 frame) and are not repeated here.
- **Junction's review walk needs a GitLab merge request.** Both trees use round 5's set-up: a local origin repository and the tests' recorded GitLab (`FakeGitLabHandler` and `Recordings/*.json`, compiled from each tree's own test project; the recordings are identical at both commits).
- **Meridian's real-window walk is History.** URL typing, find typing and the 25/200-request comparison are headless; the real window walks History and types into the 89,000-character body.
- **No headless real-loop timing.** The headless walks pump frames on a virtual clock; the real loop's waits and wakes are covered only by the real-window runs.

### Caveats

- **Machine state.** Freshly rebooted (last boot 2026-10-26 23:28). Every timed launch waited for the idle gate (CPU under 10 % for 20 s, no test host or game) and ran pinned to the 16 P-core threads; the gate passed every time. UIA clients were listening system-wide throughout, as on the user's desktop.
- **Runtimes.** JIT: .NET 10.0.14 for both trees. NativeAOT: SDK 10.0.300, runtime 10.0.8 (its ILCompiler pack). Both trees in Release from clean worktrees; the hosts' scratch projects reference each tree's own `src/` and `apps/`.
- **Headless CPU is CPU raster.** Headless frames draw into a CPU Skia surface, so CPU per key includes software painting a GPU window does not do (Reel's 5.8 ms a frame is almost all of it, which is why its CPU is flat while its bytes fell 98 %). Bytes per key carry over to real windows; headless CPU per key is a relative measure between the trees, and the real-window work per key is the absolute one.
- **How headless CPU is measured.** Each key's input, then three frames (the key's, the next, and the rest of the key's interval in virtual time), then a real sleep of the key's pace so raw tasks land. CPU per key is the UI thread's cycle count (`QueryThreadCycleTime`) from one key to the next, calibrated against the stopwatch at start (about 3.37 million cycles per ms). Sleeping costs nothing, and the headless session's waits for git subprocesses are waits, not work.
- **GC settings are part of what changed.** The after tree's apps and both after hosts carry `System.GC.LOHThreshold = 524288` from `apps/AppRuntime.props` (round 5, 828a7d69); the probe line confirmed a 300 KB string lands in gen 0 after and gen 2 before, in every launch. The gen-0 budget follows the L3 size: 17,301,504 B on this i7-14700K (33 MB L3) on both sides. A machine with less L3 collects more often and keeps less committed, so the baseline's GC counts would be higher there and the memory lower; a cap on the budget was declined in round 5 (b1323e9d).
- **Memory before the first GC.** An app that has not collected yet reports nothing for GC committed (`GC.GetGCMemoryInfo` describes the last collection); those cells show "–".
- **Order.** Each app's headless walks run in one process in a fixed order, so later walks meet a warmer JIT; the order is the same on both sides, and the AOT runs, which have no tiering, agree with JIT on every move.
- **Window safety.** 56 timed real-window launches plus 13 trial launches; the foreground guard never fired (`foregroundNeverTaken=True` in all 69), the CBT hook refused no activation (0 in every launch), no post stalled over 100 ms, and every window closed through its own `Close()`, except two trial Reel launches whose host crashed on a missing settings folder right after the window was made (fixed before the timed runs; the process exited and took the window with it). No window drag, no F16 kept-target experiment, no screen capture, no SendKeys, no OS input.
- **GPU errors.** The System log holds two `nvlddmkm` 153 errors, both at 2026-10-25 02:35 (before the reboot); there were none before this work started and none after it (checked 00:25 and 05:25 on 2026-10-27), and no Display, dxgkrnl or TDR events since the boot.

### How the numbers were taken

- **Fixtures** (scratchpad `total/partB/fx`, made by `prep.py`): a `git clone --no-hardlinks` of this worktree (1,910 commits, origin removed, README.md edited on 73 lines for the Changes diff); Vesper walked its `src/HultUI.Core` (Elements: 61 files) read-only; 88 PNGs for Thumbnails; Meridian workspaces with 25 and 200 requests, a "Big 2000" request to a localhost fake (`python -m http.server` on 127.0.0.1:8767 serving a 1,993-line JSON document, stopped afterwards), a "Notes Big" request with the 89k body, and a 300-entry history; two empty .mkv files for Reel. Apps' settings went to fresh scratch folders (`XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `VESPER_CONFIG_DIR`); git's global and system config were empty files for Junction and its git children.
- **Headless host** (`walk/src/Program.cs`, projects `walk/base` and `walk/after`): `HeadlessSession` over each app, internals reached by reflection only (`Window._root`, `Window._previousA11yTree`, `MeridianApp.OpenWorkspace`, `JunctionApp.StartReview`). JIT builds in `walk/bin-*`, NativeAOT publishes in `walk/aot-*`. Run by `walkrun.ps1`: per build and rep, each app's base and after launches back to back (order flipped per rep), the idle gate before every launch, affinity 0xFFFF. 3 reps × 2 builds × 5 apps × 2 sides = 60 launches; results in `runs/walks.jsonl`, `walks.csv`.
- **Real-window host** (`win/src/Program.cs`, NativeAOT publishes `win/aot-*`): per launch, start-up from `HULTUI_STARTUP`, `idle:20`, memory with and without a full collection, a 100-key walk, memory again, then `window.Close()` posted. Run by `winbatch.ps1` / `winrun.ps1`: 5 reps per app, order flipped per rep, gated, pinned; then 3 reps per side of the Meridian body typing. Window creates, closes and any stall over 100 ms are on each launch's timeline (`runs/w-*.out.txt`; `winbatch.log`).
- **Tables** by `tally.py` (`tables-partB.md`). Everything is in the scratchpad folder `total/partB`; nothing was committed (the run files are not in a format the repository keeps), and both worktrees were left with clean `git status`.
