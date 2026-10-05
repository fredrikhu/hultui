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
| `resize/…` | Already-shaped labels laid out at widths the layout cache has not seen — a window or pane being dragged: one-line Ellipsis and Clip, a two-line Ellipsis, plain wrapping; and `resize/bidi-…`, Hebrew with Latin on the BiDi path: labels that fit, one ellipsized line, wrapping. The last two drag through widths never met before in the run, since that path remembers lines by range. |
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
  four times slower than its own twin. **For at least 3 s, in passes**, not one
  pass: a method goes tier 0, instrumented tier 0, tier 1, and the second step needs
  calls after the first. After one pass the caret rows sat at instrumented tier 0
  for whole runs once the background compiler had a long queue — ten times their
  cost (2026-10-06; `DOTNET_TC_CallCountingDelayMs=0` put them back).
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

## Capture: re-wrapping from kept segments (2026-10-05)

Same machine and method as above; before = the commit underneath. Mins of the second pair:

| scenario | before (min) | after (min) | |
|---|---:|---:|---|
| `resize/wrap` | 20.19 µs | 5.95 µs | **−71 %**, 32 → 12 KB per call |
| `resize/ellipsis-2lines` | 15.23 µs | 7.09 µs | **−53 %** |
| `cold/latin-wrapped` | 37.75 µs | 32.59 µs | **−14 %** |
| `cold/mixed-38` | 20.33 µs | 16.66 µs | **−18 %** — the BiDi path breaks lines too |
| `cold/hebrew-25` | 30.63 µs | 26.96 µs | **−12 %** |
| `auto/mixed-cold` | 19.39 µs | 15.62 µs | **−19 %** |

Every other row is within noise. In `samples/HultUI.Samples.PerfSuite`, `text/wrap 150
paragraphs resize` went from 7.1 to 3.1 ms (−69 % allocated) and `text/create 2,000 unique
labels` from 17.6 to 9.8 ms: each new label's min-content width found its breaks with an
iterator of its own.

### What the numbers caught

- **Opening an ICU line-break iterator costs ~6 µs** — more than finding every break in a
  90-character label with it — and `LineBreaker` opened one per call. It now keeps one per
  thread and points it at each text with `ubrk_setText`. `LineBreakerTests` hold a reused
  iterator to a fresh one's breaks, across texts and with eight threads breaking at once.
- **A string's break offsets and segment widths do not depend on the width**, so they are
  kept with its shaping: a new width only does the greedy arithmetic and shapes the lines it
  keeps, plus one measure of its last line for the search that cuts it (which used to gallop
  over that line with ~8 fresh measures). `WidthChangeEquivalenceTests` compare against the
  reference path, wrapping included, bit for bit.
- **Removing duplicate break offsets was quadratic**: 33 µs for the 700 breaks of a
  3,700-character paragraph, 7 µs in one pass, next to ICU's own 29 µs to find them.

## Capture: the BiDi path remembers between widths (2026-10-05)

Same machine and method; before = the commit underneath, which adds the `resize/bidi-…`
rows. Mins of the second pair:

| scenario | before (min) | after (min) | |
|---|---:|---:|---|
| `resize/bidi-fit` | 23.43 µs | 0.45 µs | **−98 %**, 10.6 → 1.0 KB per call |
| `resize/bidi-ellipsis-1line` | 81.38 µs | 9.49 µs | **−88 %**, 31.9 → 5.2 KB |
| `resize/bidi-wrap` | 130.98 µs | 11.56 µs | **−91 %**, 58.3 → 9.0 KB |

The cold rows — strings laid out once — are within noise. In
`samples/HultUI.Samples.PerfSuite`, `text/bidi wrap 150 paragraphs resize` went from 37.6
to 3.6 ms and `text/bidi ellipsis 1,000 labels resize` from 214 to 17 ms (90 → 7.6 MB).

One cost to read the other rows with: in this run the plain `resize/…` rows read 3–13 %
higher on the after side. Run without the BiDi rows, the same comparison is within
noise — what they pay is the memory the BiDi rows' memos hold in the same process
(every Gen0 collection walks more), not any change to the plain path.

### What the numbers caught

- **Right-to-left shaping is the cost, and it was paid at every width.** RTL runs bypass
  the word cache, and HarfBuzz takes ~4 µs for a six-letter Hebrew word, ~25 µs for a
  75-character label (an LTR word: under 1 µs). Every segment measure, every ellipsis
  probe and every line was shaped again at each width. ICU was not the cost: levels
  0.85 µs, breaks 0.67 µs a call.
- **All of it is a function of the text, its style and a range**, so it is memoized:
  levels, breaks, segment widths and the whole-text line on the string's entry; probe
  widths and partial lines in caches shared by all strings (16,384 and 4,096 entries).
  `WidthChangeEquivalenceTests` hold it to the reference path over Hebrew, Arabic and
  mixed text in RTL and auto styles, now with every style through the same pipelines so
  a key missing a style field would fail.
- **A memo costs the strings it never helps.** Most text on this path is a label with an
  em dash, laid out once: making its entry cost ~0.3 µs, +10 % on 2,000 new labels. A
  string now gets its entry at its second layout; the first only notes its hash. The
  first version of that note took one slot per hash, and two strings on one slot took it
  from each other forever: 137 of 1,000 labels never got an entry and were shaped from
  scratch every frame (27 ms where 17 was due). Two slots, and an entry is always used
  once made.
- **Full, the memo holds memory**: 60 widths of 1,000 ellipsized labels keep 11.6 MB
  against 4.9 MB on the reference path; wrapped paragraphs keep less than the reference,
  since a layout's lines share their runs with the remembered ones.

## Capture: the styled path remembers between widths (2026-10-06)

Same machine and method, with the time-based warm-up on both sides; before = the
commit underneath. Mins of the second pair:

| scenario | before (min) | after (min) | |
|---|---:|---:|---|
| `resize/styled-wrap` | 14.33 µs | 6.49 µs | **−55 %**, 29.3 → 5.2 KB per call |
| `resize/styled-ellipsis-1line` | 8.88 µs | 4.76 µs | **−46 %**, 18.8 → 3.5 KB |
| `resize/styled-bidi-ellipsis-1line` | 101.66 µs | 12.54 µs | **−88 %**, 40.8 → 6.1 KB |

In `samples/HultUI.Samples.PerfSuite`: `text/styled wrap 150 paragraphs resize` 6.4 →
2.1 ms, `text/styled ellipsis 1,000 labels resize` 56.7 → 17.9 ms, `text/styled bidi wrap
150 paragraphs resize` 44.4 → 4.7 ms and `text/styled bidi ellipsis 1,000 labels resize`
388 → 41 ms; every other scenario within ±5 %.

Read the other rows of this pair with the same caution as the BiDi capture's: the
styled rows' memos, kept for the whole run in this one process, made the plain and BiDi
`resize/…` rows read 13–71 % higher on the after side (`resize/bidi-fit`, at 0.4 µs, the
most), and the cold BiDi rows 4–19 %.
Without the styled rows the same comparison is within noise, but for `resize/bidi-wrap`
at +10 %, which the perf suite's `text/bidi wrap` (flat) does not show.

### What the numbers caught

- **The styled path did everything again at every width**, as the BiDi path had: every
  segment measured anew — cut at the spans' edges, each piece itemized — every ellipsis
  probe and every line. Right-to-left styled text, laid out by the BiDi path with its
  spans, kept nothing at all, since that path's memo was keyed without spans.
- **One memo now serves both paths.** A string's entry is keyed by its text, style and a
  digest of its metric spans, and keeps the spans themselves: two span lists with one
  digest never share an entry. Probes and lines go to the same bounded caches as the BiDi
  path's, so the memory bound did not move. `WidthChangeEquivalenceTests` compare both
  paths against the reference, bit for bit, with two span lists per text through the same
  pipelines.
- **A delegate over these parameters allocates on every call.** The first cut handed the
  memo a lambda to build a missing line or width; C# allocates such a closure when the
  method is entered, so every hit paid for it too. The lookups are plain code now.

## Capture: multi-font labels take the one-line ellipsis shortcut (2026-10-06)

Same machine and method; before = the commit underneath, which adds the row. Mins of the
second pair:

| scenario | before (min) | after (min) | |
|---|---:|---:|---|
| `resize/multifont-ellipsis-1line` | 10.98 µs | 2.58 µs | **−77 %**, 13.4 → 2.7 KB per call |

In `samples/HultUI.Samples.PerfSuite`, `text/multi-font ellipsis 1,000 labels resize` went
from 41.4 to 11.8 ms (−81 % allocated); every other scenario within ±6 %. In this run
`cold/latin-wrapped` read 22–28 % higher on the after side — the cross-row effect of the
captures above: run with only the cold and warm rows, it is within noise.

### What changed

- **Prefix widths cover any number of font runs.** A prefix of any string itemizes to its
  runs, the last one cut short, so each run keeps its segments and the running width
  MeasureString adds the runs up with. Clip, multi-line Ellipsis and the wrap pass's last
  line use them for multi-font text too.
- **One-line Ellipsis no longer shapes the line it replaces**, whatever the string. The
  proven shortcut stays one-run: a replaced line's height depends on the fonts it
  reached. Everything else runs the wrap pass as it is with measured lines (extent,
  width and metrics, no glyphs) and then ApplyEllipsis as it is. `LineMetrics` is the one
  source of a line's metrics for both kinds of line. A layout ApplyEllipsis would have kept
  unchanged takes the reference path instead.
