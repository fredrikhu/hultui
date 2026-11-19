# Shaping text a run at a time

**Status (2026-11-19): run shaping no-go. Item 1, the wider digit table, is built (781a857f, on
this plan's branch, not merged); items 2–4 wait.** The run-shaping prototype is on branch
`claude/run-shaping-prototype` (14b29b66), every part of it off unless an environment variable
turns it on.

Plan 18 item 1 (SkiaSharp 4, HarfBuzzSharp 8.3.1 → 14.4 on Windows) left one regression on
purpose: each `hb_shape` call costs more in HarfBuzz 14.4, so words the word cache has not
shaped yet cost more. `text/create 2,000 unique labels` went 2.857 → 3.249 ms min on Windows
JIT (+14 %). Timed alone, a one-letter word with Segoe UI went 237 → 414 ns; the cost is in
running the shape plan, not finding it. Shaping whole runs instead of word by word was named as
the way round it. This plan asks whether that works, with a prototype and numbers.

**The answer in short.**

- **Run shaping works, exactly, and does not pay where it was meant to.** One HarfBuzz call per
  run, cut into its words where HarfBuzz says a cut is safe, gives every word to the bit (0
  differences over 15,470 words in three faces, and the whole pipeline held to it by a test). It
  costs 26–40 % less HarfBuzz time than word by word **when every word is cold**, and the perf
  suite's all-cold paragraph row is 15–21 % faster on Windows (1–4 % on Fedora, where HarfBuzz
  calls are cheaper). But the row that regressed shapes **one** cold word per label, so there is
  nothing to put in one call; and a real first layout (the repo's docs, cold caches) is flat to
  2 % slower on Windows and 5–8 % slower on Fedora, because cold words there come one at a time
  among cached ones and looking ahead for a second one costs what it saves.
- **The regressed row's one cold word is a number with a minus sign**, there by an `int`
  overflow in the scenario. Widening the digit fast path to `- + . , : / % −` (Segoe UI passes
  the same proof) takes the row from 3.18 to 1.32 ms on Windows (−58 %), well below the 2.857 ms
  it had before HarfBuzz 14.4, and 2.49 to 1.61 ms on Fedora (−35 %); the emoji labels −35 % and
  −19 %.
- **The BiDi rows' HarfBuzz calls are right-to-left runs shaped again at each width**, which no
  cache holds. A memo of RTL runs is the lever there; it needs no proof at all.

## How text is shaped today

- **Segments.** `ShapeByWords` cuts each font run into maximal space and non-space segments
  (U+0020 only) and looks each one up in the word cache (`WordCache`: 32,768 entries, 16 MB,
  keyed on the characters, the face and the size). A miss shapes the segment as a string of its
  own: one `hb_shape` call per cold word. Spaces are cold once per face and size.
- **The digit fast path.** A segment of ASCII digits only is composed from a per-face-and-size
  table, proven once (110 calls) to be context-free. A sign or separator (`-38913`, `1,234`,
  `12:30`) takes it off the table: the segment is a word like any other.
- **What reads the word cache.** The whole-text width (`ComputeShaping`), the break segments'
  widths that re-wrapping at a new width reuses (`BreakSegments`), `MeasureRange` for line
  pieces and ellipsis probes, the glyph arrays composed only when drawn (`ComposeGlyphs`), and
  the prefix tables (`PrefixWidths`, with `RunCuts` for words of 8+ characters). The layout-level
  caches (`MeasureCache`, the layout and wrap-range caches) sit above all of it.
- **Right-to-left runs bypass the word cache.** `MeasureRun(rtl: true)` and
  `ShapeWithSpacing(rtl: true)` shape the run whole, every time (plan 14, stage 1: the
  space-split assumptions were never proven for RTL). The BiDi path memoizes widths by range, so
  a new width's new ranges shape their RTL runs again.

## How many calls a first layout makes

Counted with a call counter in `SkiaTextShaper` (prototype only, `HULTUI_SHAPE_COUNT=1`), per
measured step of the perf suite, and for a fresh pipeline laying out the repository's own docs.

| What | HarfBuzz calls | Characters a call |
|---|---|---|
| `text/create 2,000 unique labels` | 2,000 a step: **one per label** | 5.9 |
| `text/create 2,000 unique emoji labels` | 2,000 a step, the same one per label | 5.9 |
| `text/bidi ellipsis 1,000 labels resize` | 2,510 a step | 8.2 |
| `text/styled bidi ellipsis 1,000 labels resize` | 5,678 a step | 4.3 |
| `text/bidi wrap 150 paragraphs resize` | 504 a step | 8.5 |
| `text/styled bidi wrap 150 paragraphs resize` | 989 a step | 4.4 |
| `input/type …`, `richtext/type …` | 1 a keystroke: the word typed into | 5–54 |
| Every other `text/` row; `richtext/open`, `enter`, `wheel`, `resize` | 0–29 a step | |
| The docs (4,000 paragraphs, 339,000 characters, 600 px), fresh pipeline | 12,250: **3.1 a paragraph** | 8.9 |

- **The cold-label row shapes one word per label, and by accident.** Its label is
  `$"Item {next++} — {next * 7919 % 100_003} units"`; `next * 7919` overflows `int` (next starts
  at 1,000,000), so the second number is negative: `Item 1000001 — -38913 units`. `Item`, `—`
  and `units` are cached after the first label, `1000001` goes through the digit table, and
  `-38913` is the one cold word: the minus sign takes it off the digit table. So the row times one
  6-character `hb_shape` call per label, and the +14 % is exactly 2,000 × the per-call cost
  (2,000 × ~195 ns ≈ 0.39 ms of +0.39 ms). With the numbers positive, the row would shape
  nothing.
- **Real text is mostly cached words.** Over the docs, 12,250 calls shape 109,000 of 339,000
  characters; a cold word is usually alone among cached ones.
- **The BiDi rows' calls are RTL runs** shaped again at each width, not cold words: of 4,000
  calls logged on `text/bidi ellipsis 1,000 labels resize`, 3,997 have no Latin letter (Hebrew
  runs), and 1,933 span several words.

## Can a run be shaped once and cut into its words?

Yes, exactly, where HarfBuzz says so. Shape `text[a..b]` in one call; for each segment, the
glyphs whose clusters fall inside it, composed with the pen starting at 0 (the shaper's own
arithmetic), are the glyphs and width that shaping the segment alone gives, **to the bit**,
when all of these hold:

1. **The cut at each end is safe:** the glyph that starts the cluster at the segment's first
   character, and the one at its end, both lack `HB_GLYPH_FLAG_UNSAFE_TO_BREAK`. That flag is how
   HarfBuzz reports kerning, ligatures and contextual forms across the cut: across a space here.
2. **A cluster starts exactly at each end.** A combining mark after a space joins the space's
   cluster, so no glyph starts at the next word; that word is refused.
3. **The segment guesses the run's script.** The shaper guesses the script per buffer, from the
   first character with a script of its own. A word with none (digits with a sign, punctuation,
   symbols: `-38913`, `—`, `1,234`) is shaped alone in no script (DFLT) but inside a Latin run in
   `latn`, and the font may differ between them. A word of another script in the same font run
   (Greek or Cyrillic in Segoe UI) is refused the same way.
4. **Left to right.** A run HarfBuzz guesses right to left is not cut.

Everything that reads the word cache keeps working unchanged, because the prototype only
**fills the word cache** from a run: line breaking, re-layout at a new width, `MeasureCache`,
prefix tables and glyph composition read the same per-word entries they read today.

Measured over 2,000 lines of the docs (16,529 words), HarfBuzz 14.4 on Windows:

| Face | Words cut out exactly | Unsafe cut (kerning/ligature across the space) | No script of their own | Not equal to the word alone |
|---|---|---|---|---|
| Segoe UI | 15,470 | 0 | 1,059 | **0** |
| Arial | 15,078 | 392 | 1,059 | **0** |
| DejaVu Sans | 15,470 | 0 | 1,059 | **0** |

`RunShapingPrototypeTests` holds the pipeline to it: batch on and off lay out 3,000 texts (the
docs, plus Greek, Cyrillic, Vietnamese with combining marks, a mark after a space, ZWJ, soft
hyphens, emoji, Hebrew mixed in, kerning and ligature strings) in sans-serif, serif and
monospace at 11, 13 and 17.5 px, unwrapped and at 240 px, to the same lines, runs, widths and
glyphs, bit for bit.

## What a run cache would key on

A run cache, as opposed to filling the word cache from a run, would key on what the shaper's
output depends on: **the run's characters, the face (the resolved font object, which carries
family, weight and slant), the size (its bits) and the direction**. Not letter spacing (applied
after shaping), not paint-only spans, not the width. Language and OpenType features would join
the key the day the pipeline passes them (today it passes neither). The script needs no key: the
shaper guesses it from the characters.

For left-to-right text a run cache would duplicate what exists. The whole text's shaping is
already cached (`ShapeKey` → `ShapedText`), and every consumer at a new width reads per-word
widths, so the words would still be needed. **For right-to-left runs it is the missing piece:**
they are shaped whole already, so a memo keyed as above needs no proof at all, and it is where
the BiDi rows' 500–5,700 calls a step come from.

## The prototype

Branch `claude/run-shaping-prototype`, commit 14b29b66, not for merging. All of it is off by
default.

- **`IShapesSegments`** (HultUI.Text), implemented by `SkiaTextShaper`: shape `text[a..b]` in one
  call on the kept buffer and font, and hand back a `ShapedRun` for each asked-for segment that
  passes the four conditions above, composed with the shaper's own arithmetic; null for the rest.
- **`TextPipeline.ShapeMissesFrom`** (`HULTUI_RUN_SHAPING=1`): when the word cache misses a word
  with a letter in it, inside a run of several segments (`ShapeByWords`, `MeasureRun`), it looks
  at the words after it in the run, through spaces and through cached words of up to
  `HULTUI_RUN_BRIDGE` characters (12; shaped again and dropped), and ends the batch at its last
  cold word. With two or more cold words it shapes them in one call and puts each word that came
  out exactly into the word cache; the rest are shaped alone, as before. `HULTUI_RUN_TRIGGER=streak`
  looks ahead only from a cold word that follows a cold word, so a lone cold word costs no
  look-ahead.
- **The numeric table** (`HULTUI_NUMERIC_TABLE=1`): the digit table's alphabet becomes
  `0123456789-+.,:/%−`, proven as the digits are (each character alone, every pair: 342 calls once
  per face and size instead of 110).
- **Instruments:** `HULTUI_SHAPE_COUNT=1` counts HarfBuzz calls per perf-suite step;
  `TextBench --runs <docs>` times word by word against one call per line on HarfBuzz alone, and a
  fresh pipeline's first layout of the docs in both modes; the perf-suite row
  `text/create 200 paragraphs of unseen words` (200 wrapped paragraphs of 40 words, two in three
  never seen, every third a common one); `RunShapingPrototypeTests` and `NumericTokenProbeTests`.

## Measurements

Windows: i7-14700K pinned to its P-cores, every timed run behind `scripts/perf/idle-gate.ps1`,
JIT, `main` 3212f91c plus the prototype. A/B pairs run the same build with the variable off and
on, alternated. Fedora: WSL, unpinned (WSL shows no CPU frequencies), the same commit.

**HarfBuzz alone** (`TextBench --runs`, 13 px, 2,000 docs lines of up to 80 characters, every
word cold; min of 40 passes; "labels" are the cold-label row's text):

| Face | Words alone | One call a line, cut | Change | Labels: change |
|---|---|---|---|---|
| Segoe UI, Windows | 641 ns a word | 384 ns | **−40 %** | +17 % |
| Arial, Windows | 629 ns | 429 ns | −32 % | +21 % |
| DejaVu Sans, Windows | 580 ns | 408 ns | −30 % | +19 % |
| DejaVu Sans, Fedora | 348 ns | 258 ns | −26 % | +22 % |

The labels are slower cut from a line because three of their five words have no letter (`1000001`,
`—`, `-38913`) and are shaped alone anyway, on top of the line.

**A fresh pipeline's first layout of the docs** (4,000 paragraphs, 339,000 characters, wrapped at
600 px, 13 px sans-serif; a full collection before each timed pass, the modes alternated, min of
20 passes):

| Mode | Windows, Segoe UI | Calls | Fedora, Noto Sans | Fedora, DejaVu Sans |
|---|---|---|---|---|
| Word by word | 18.17–18.51 ms | 12,250 | 20.03 ms, 12,573 calls | 21.93 ms, 13,515 calls |
| Look-ahead, no bridging | +0.5 % | 9,986 | | |
| Look-ahead, 12-character bridges | +1.7 to +2.1 % | 7,523 | **+7.9 %**, 7,876 calls | **+5.0 %**, 8,798 calls |
| Streak only, 12-character bridges | +1.2 % | 10,612 | | |
| Streak only, no bridging | +0.7 % | 11,564 | | |

(Fedora's `sans-serif` is Noto Sans, a variable font, through fontconfig. "DejaVu Sans" on
Windows is not installed and falls back to Segoe UI, so the Windows column is Segoe UI only.)

**The perf suite, Windows** (min ms, two pairs; the A/A pair of word by word moved no row more
than 5 %):

| Row | Word by word | Look-ahead | Streak only | Numeric table |
|---|---|---|---|---|
| `text/create 200 paragraphs of unseen words` | 6.98 / 7.05 | **−17 % / −21 %** | −15 % / −17 % | |
| `text/create 2,000 unique labels` | 3.28 / 3.26 (3.18 / 3.19) | −0.5 % / +0.6 % | −1.5 % / +1.1 % | **−58 % / −60 %** |
| `text/create 2,000 unique emoji labels` | 5.63 / 5.65 (5.41 / 5.48) | 0 % / −1 % | | **−35 % / −36 %** |
| `text/bidi ellipsis 1,000 labels resize` | 10.11 / 10.05 | 0 % / 0 % | | |
| `text/styled bidi ellipsis 1,000 labels resize` | 15.85 / 15.51 | −2 % / 0 % | +3 % (pair 2) | |
| `text/bidi wrap`, `text/styled bidi wrap` | 2.7, 3.5 | within 2 % | within 2 % | |
| `input/type …`, `richtext/type in 5,000 blocks` | 0.28–0.48 | within ±5 % | within ±2 % | within ±3 % |

The numbers in brackets are the numeric table's own pairs, run later, with its own base.

**The perf suite, Fedora** (min ms, two pairs, unpinned; A/A within ±4 %):

| Row | Word by word | Look-ahead | Streak only | Numeric table |
|---|---|---|---|---|
| `text/create 200 paragraphs of unseen words` | 6.40 / 6.57 | −1 % / −4 % | −1 % / −4 % | +3 % / 0 % |
| `text/create 2,000 unique labels` | 2.49 / 2.54 | +2 % / 0 % | +1 % / −2 % | **−35 % / −38 %** |
| `text/create 2,000 unique emoji labels` | 5.01 / 4.96 | 0 % / +2 % | 0 % / +4 % | **−19 % / −20 %** |
| `text/bidi …` (four rows) | 1.3–5.7 | within ±4 % | within ±4 % | within +9 % / +2 % |
| `input/type …`, `richtext/type in 5,000 blocks` | 0.31–0.51 | within ±3 % | within ±5 % | within +9 % / ±1 % |

Allocations moved on no row under run shaping, on either OS; the numeric table allocates 22 %
less on the labels and 18 % less on the emoji labels (no word string, run or cache entry per
number). The +9 % numeric-table cells on Fedora (`text/bidi wrap`, `richtext/type`) are pair 1 only,
the medians there are +2 % and −6 %, and pair 2 has them at 0 % and −1 %: noise.

**HarfBuzz itself costs more on Windows than on Linux.** The same DejaVu Sans file, the same CPU:
580 ns a cold word through Windows' `libHarfBuzzSharp.dll`, 348 ns through Fedora's (run only:
408 against 194 ns a word). Both Windows builds (8.3.1 and 14.4) are MSVC with Control Flow Guard
and `/GS` on (dumpbin: 230 and 240 objects, `guardN` on all), and HarfBuzz dispatches its lookups
and font functions through function pointers, each a CFG check on Windows. That does not explain
the 8.3.1 → 14.4 step (both have it), and it is not measured: a lead, not a finding.

## Decision

**Run shaping: no-go.** It does not touch the regression it was meant for (one cold word per
label: nothing to batch), the BiDi rows (their calls are RTL runs, already shaped whole) or typing
(one word a key). On real text it is flat to 2 % slower on Windows and 5–8 % slower on Fedora.
It wins only where nearly every word is new (−15 to −21 % on a synthetic row on Windows, −1 to
−4 % on Fedora), at the price of a second shaper entry point, about 150 lines on the hot path of
every cold word, and an exactness argument that applies HarfBuzz's
UNSAFE_TO_BREAK guarantee to several cuts at once (it held on everything measured, but it is a
stronger claim than the one `RunCuts` makes). The prototype stays on its branch. Look at it again
only if a real workload shows long streaks of cold words: a document in a language the session has
not met, opened large.

**Go, if agreed, in this order:**

1. **Widen the digit table to number punctuation. Built 2026-11-19 (781a857f); see "Item 1, built"
   below.** It removes the one HarfBuzz call the
   regressed row makes per label, and it is the largest win measured here. Real interfaces are
   full of such cold tokens: sizes, times, dates, versions, signed values. Steps:
   - Alphabet `0123456789-+.,:/%−` (U+2212). Prove each punctuation mark lazily, the first time a
     segment has it, so a face that only ever shows digits keeps its 110-call proof. (The
     prototype proves the whole alphabet at once, 342 calls, pairs only.)
   - The proof: each character alone one glyph at no offset; every pair at the summed advances;
     **and every digit–mark–digit triple** (another 100 calls a mark), because a contextual
     alternate that needs a digit on each side (a colon raised between figures, as some interface
     faces have) passes every pair. Segoe UI and Cascadia Mono pass all three at 12–14 px
     (`NumericTokenProbeTests`); on Fedora the labels went to the table too, so Noto Sans passed the
     pairs at 13 px (not probed further). Arial already fails on digits (`11` kerns −0.97 px at
     13 px), so it keeps shaping numbers.
   - The same tolerance as the digits (0.01 px): a composed number is not the shaped one to the
     bit, as today's digits are not.
   - `CacheBoundTests.AnEntrysWeight_IsAtLeastTheGlyphsItKeeps` builds its cold word from
     comma-separated digits; it needs a word with a letter in it.
   - Perf A/B on every `text/` row, both OSes.
2. **Give `text/create 2,000 unique labels` a cold word on purpose.** With the numbers fixed (no
   overflow) or composed, the row shapes nothing and stops watching shaping. Make the second
   number `long` or modular without overflow, and add one unique word with letters per label, so
   the row keeps one cold `hb_shape` call per label by design. Note the change in the perf suite
   README, as its history breaks there.
3. **Memo right-to-left runs.** Key: the run's characters, the face, the size bits, RTL. A second
   `WordCache` (it already takes a span, face, size and font hash), weighted like the word cache,
   used by `MeasureRun(rtl: true)` and `ShapeWithSpacing(rtl: true)`. Prototype and measure on the
   four BiDi rows first: they make 500–5,700 HarfBuzz calls a step today.
4. **Keep the instruments.** `HULTUI_SHAPE_COUNT` (or a `PerformanceCounters` field) and the
   unseen-words row are cheap and say at once where HarfBuzz time goes; both could land with item 1.

## Item 1, built (2026-11-19)

Commit 781a857f, on this plan's branch; not merged.

- **What composes.** A segment of digits and marks from `TextPipeline.NumberMarks`
  (`- + . , : / %` and U+2212), with at least one digit: `-38913`, `1,234.56`, `12:30`, `50%`,
  `2026-11-19`, `+46-70-123`. A segment of marks alone (`-`, `...`) is a word as before and proves
  nothing, so a face that never shows a number with punctuation pays nothing new.
- **The proof, per mark, the first time a segment has it:** the mark alone one glyph at no
  offset; beside itself, each digit and each mark proven before it, both ways round, at the summed
  advances; and between every two digits, the three glyphs at the summed advances. 122 shaper
  calls for the first mark, two more for each mark proven before it; the digits keep their own
  110-call proof, made first. A mark that fails is refused for that face and size alone: numbers
  with it are shaped as words, the digits and other marks still compose. Tolerance 0.01 px, as for
  the digits.
- **The reference:** `ComposesNumberPunctuation = false` composes digits only, as before.
- **Tests.** `DigitFastPathTests` (stub shapers): the marks' slots; the proof's call counts
  (122, then 124); marks alone stay words; a mark that kerns beside a digit is refused and the
  others still compose; a colon that takes another glyph between two digits (every pair passes)
  is refused; the reference composes digits only. `NumberPunctuationTests` (system faces:
  sans-serif and monospace at 11, 13 and 17.5 px, and Arial on Windows, which fails the digits'
  proof): 23 numbers and sentences lay out with the reference's glyph ids, each glyph within
  0.02 px of where the reference put it; in Segoe UI, 50 new labels full of signed, separated
  and timed numbers make no shaper call. `CacheBoundTests` takes a cold word with letters in it.
- **Perf A/B, Windows** (`scripts/perf/step-ab.ps1 -Base 4be5be67`, `text/` and `input/type`
  rows, two idle-gated pairs per runtime, P-cores; the A/A pairs flag no row): **0 slower**, 2
  faster on both runtimes.

  | Row | JIT min ms | NativeAOT min ms | KB a step |
  |---|---|---|---|
  | `text/create 2,000 unique labels` | 3.306 → 1.398 (−56 to −58 %) | 3.953 → 2.003 (−49 to −56 %) | 3,276 → 2,572 |
  | `text/create 2,000 unique emoji labels` | 5.653 → 3.696 (−33 to −37 %) | 6.977 → 4.632 (−33 to −35 %) | 3,989 → 3,285 |
  | `text/page down through highlighted JSON …` | flat | flat | 38.8 → 36.2 |
  | `text/wheel highlighted JSON …` | flat | flat | 7.6 → 7.2 |
  | `text/wrap 150 paragraphs resize`, `input/type in TextInput` | flat | flat | flat |

  The labels row is now well below its 2.857 ms from before HarfBuzz 14.4 (plan 18), so the
  regression this plan was about is more than won back on that row; but the row now shapes no word
  at all, which is what item 2 is for.
- **Perf A/B, Fedora** (WSL, unpinned, the same commits in two worktrees of the WSL clone, two
  alternated pairs, `text/` and `input/type` rows; the A/A pair moved no row more than 6 %):
  **0 slower**, 2 faster. `text/create 2,000 unique labels` 2.556 → 1.569 and 2.461 → 1.528 ms
  (−38 to −39 %), `text/create 2,000 unique emoji labels` −22 %, the same allocation drops.
- **Suites:** Windows, all 30 assemblies pass (Junction 1,161). Fedora, `HultUI.CrossPlatform.slnf`
  passes in full (22 assemblies, the goldens among them); Junction, Meridian, Aperture and Reel,
  which that filter leaves out, were not run on Fedora.

## Traps found on the way

- **The overflow in `text/create 2,000 unique labels`** (above): read the strings a row shapes
  (`HULTUI_SHAPE_COUNT` plus a log of the texts) before reasoning about what it times.
- **Timing two pipelines back to back in one process:** the second one paid for collecting the
  first one's caches, and read 9 % slower for nothing. Collect fully before each timed pass and
  alternate which goes first.
- **Shaping a no-script word inside a run is not shaping it alone.** HarfBuzz guesses the script
  per buffer, so `-38913` shaped alone is DFLT and inside `Item … units` is `latn`; a cut that is
  safe by the glyph flags can still be wrong. `RunCuts` met the same thing for prefixes.
