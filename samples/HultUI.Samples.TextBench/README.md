# Text pipeline benchmark

A micro-benchmark for `HultUI.Text`, built so a change to the text pipeline can
be shown not to cost anything on the paths it does not target. It was written
for the BiDi work ([docs/plans/14-bidi.md](../../docs/plans/14-bidi.md)), where
the thing most worth proving is a negative: text with no right-to-left script in
it must pay what it paid before.

```bash
dotnet run -c Release --project samples/HultUI.Samples.TextBench
```

`--csv` prints the same numbers as CSV.

## Reading the output

| Group | What it is |
|---|---|
| `warm/…` | The same string laid out over and over — the steady state a scrolling list reaches, where everything downstream of the layout cache is already paid for. |
| `cold/…` | Every call is a string the pipeline has never seen — a freshly built list of labels. Shaping dominates. |
| `auto/…` | The same work with `TextDirection.Auto`, where the paragraph direction comes from the content. |
| `resize/…` | Already-shaped labels laid out at widths the layout cache has not seen — a window or pane being dragged: one-line Ellipsis and Clip, a two-line Ellipsis, plain wrapping. |
| `caret/…` | The offset↔x mapping behind a caret, a click and a selection highlight, measured on an already-laid-out line. |
| `recheck/…` | The warm rows again, on fresh pipelines: the harness's control group. |

**Compare the `min` column, not the median.** Medians move with whatever else
the machine is doing — a busy machine reads 3–5× its own quiet numbers — while
the minimum is the cost when nothing interfered. **And check the `recheck` rows
against their `warm` twins**: they run identical work, so if a pair disagrees the
capture is measuring position in the run and neither number can be compared with
another capture.

Two things the harness does that a naive one does not, both because getting them
wrong invents regressions that are not there:

- **Global pre-warm across every scenario before any measurement.** Shared code
  reaches tier-1 and dynamic PGO gets data first. Without it the same LRU cache
  hit reads 250 ns early in a run and 60 ns late in it — the comparer calls
  devirtualize only once PGO has data — and whichever scenario ran first looks
  four times slower than its own twin.
- **Round-robin sampling.** Sample *n* of every scenario before sample *n+1* of
  any, so residual drift lands on all rows equally instead of on the first ones.

## Capture: BiDi stage 2 (2026-08-16)

`i7-14700K, Windows 11, .NET 10, Release`. Before = the commit that shipped BiDi
stage 1; after = stage 2. Taken with the same binary of this project, by
stashing only `src/HultUI.Text` for the "before" run.

| scenario | before (min) | after (min) | |
|---|---:|---:|---|
| `warm/latin-67` | 71 ns | 59 ns | unchanged path |
| `warm/latin-4700` | 4.41 µs | 3.72 µs | **−16 %** — vectorized classifier |
| `warm/mixed-38` | 57 ns | 58 ns | unchanged |
| `warm/hebrew-25` | 51 ns | 51 ns | unchanged |
| `cold/latin-67` | 3.07 µs | 2.95 µs | unchanged path |
| `cold/latin-wrapped` | 42.85 µs | 44.19 µs | unchanged path (noise) |
| `cold/mixed-38` | 22.19 µs | 21.14 µs | **−5 %** — one UAX #9 pass per paragraph |
| `cold/hebrew-25` | 32.01 µs | 31.21 µs | **−2 %** — same |

Allocations are zero on every warm row before and after; the cold rows are
unchanged bar the level array the BiDi path now allocates once instead of once
per line.

The `caret/…` rows are new in stage 2 and have no "before": caret x costs 3 ns,
a hit test 12–16 ns, a selection range 11–38 ns.

## Capture: BiDi stage 3 (2026-08-16)

Every row above re-measured within noise of its stage-2 value — first-strong
auto direction, direction-aware alignment and visual caret movement cost the
existing paths nothing. Auto direction in particular is free, because it rides
the UAX #9 analysis that was already happening:

| scenario | | |
|---|---:|---|
| `auto/latin-cold` | 2.96 µs | vs `cold/latin-67` 2.95 µs — the classifier still answers before ICU |
| `auto/mixed-cold` | 19.64 µs | vs `cold/mixed-38` 20.52 µs — same analysis, now also reporting its verdict |

### What the numbers caught

- **`ContainsAnyExceptInRange<char>` allocates.** The BiDi classifier ("does this
  text contain anything at or above U+0590?") is a vectorized range search, 3–6×
  faster than a character loop. The `char` overload boxes its two bounds — 96 B
  per call, measured, on *every text layout in the framework*. Casting the span
  to `ushort` first is the identical search, identical speed, zero allocation.
- **Hoisting the classifier behind the shape cache is not worth it.** A hit in
  that cache proves the string is RTL-free (the BiDi path never populates it), so
  the classifier could run once per string instead of once per call. Measured, it
  buys 0.16 µs on a 4,700-character warm layout and costs every warm BiDi lookup
  ~45%. Left alone; the note is in `TextPipeline.Layout` so nobody re-derives it.

## Capture: truncation from prefix widths (2026-10-05)

`i7-14700K, Windows 11, .NET 10, Release`, pinned to the performance cores. Before = the
parent commit, built from a second worktree with this project copied in; the two sides run
alternately, twice each. Mins of the second pair:

| scenario | before (min) | after (min) | |
|---|---:|---:|---|
| `resize/ellipsis-1line` | 19.86 µs | 1.66 µs | **−92 %**, 21.5 → 2.2 KB per call |
| `resize/clip-1line` | 6.48 µs | 1.78 µs | **−73 %**, 14.2 → 2.3 KB |
| `resize/ellipsis-2lines` | 26.54 µs | 15.10 µs | **−43 %** |
| `resize/wrap` | 28.39 µs | 19.41 µs | **−32 %** |
| `cold/latin-wrapped` | 53.10 µs | 37.88 µs | **−29 %** |

Every other row is within noise of its own before. `samples/HultUI.Samples.PerfSuite`'s
`text/ellipsis 1,000 labels resize` went from 56.8 ms and 71 MB per frame to 8.4 ms and 7.8 MB.

### What the numbers caught

- **Shaping was not the cost.** A resize drag over truncated labels made almost no
  HarfBuzz calls: every partial word the binary search tried was a word-cache hit by the
  second frame. The cost was everything around it — ~40,000 itemizations a frame (each a
  `HasGlyph` per character), a fresh substring and glyph list per candidate prefix, and
  ~3,400 ICU line-break iterators, each opened from scratch at ~4.7 µs.
- **Half of those ICU calls read nothing.** `ComputeWrapped` itemized and shaped the whole
  text and found its break opportunities, then handed them to `BreakIntoLines`, which
  ignored them and found them again. Gone; that alone is the `wrap` rows' gain.
- **Now a truncated string's prefix widths come from its own segments**, worked out once
  and kept with its shaping; a candidate cut costs one word-cache lookup for the partial
  word it ends in. The searches are the same probe for probe and each probe returns
  `MeasureString`'s number to the bit — tests hold it to the old path's output
  (`TruncationEquivalenceTests`, stub shaper and HarfBuzz both).
- **One-line Ellipsis no longer breaks the text into lines first** to throw the line away.
  Where that is provably the same answer — one font run, two or more characters, no
  zero-width leading segment, an empty line as tall as a full one — the line breaker is
  skipped; anything else takes the old path.
