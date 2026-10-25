# Round 1 survey: 5. Text pipeline

allocKB per op and call counts from perf-suite runs with temporary counters; "prototype"
figures from an env-gated prototype. All reverted; worktree agent-a7d393bd2c7bbc234 clean
at d4b7abf. No timings.

SATISFIED: no
REVIEW: none (round 1)

## Candidates

- **text-textarea-span-scan.** `SpansForLine` binary-searches the first span ending after the line and loops by index, instead of `foreach` from span 0 per visible line per frame.
  - Evidence: TextAreaElement.cs:1478-1492 (`foreach` :1482). 5,000-line viewer, ~45k spans, wheeled near the end: 2,281,372 span iterations per frame for 51 lines, 64.5 KB in SpansForLine (no spans: 0 and 10.3 KB). Real: Junction Files highlighted file (JunctionApp.Files.cs:667-670), Meridian bodies. Lead for painting: ~170 KB more per frame in the span paint path (`DrawSpannedLine`, `DrawLineSegment`, `VisualRanges` enumerators).
  - Expected: O(total spans) → O(log spans + line spans) per line per frame; −64.5 KB/frame.
  - Cost: small. Test: per-line span lists equal the linear scan over random sorted-apart lists.
- **text-word-alloc-diet.** `ShapeByWords` allocates its glyph array at exact size (two passes) instead of `List<GlyphPosition>(text.Length)` + `ToArray`; `MeasureString` sums segment widths and glyph counts without a glyph array; word-cache lookups by `ReadOnlySpan` (Dictionary alternate lookup), no substring per word in ShapeByWords, MeasureString, BuildPrefixWidths, PrefixWidth.
  - Evidence: TextPipeline.cs:1828/:1841, :1836, :1553, :1616, :1772-1782. ShapeByWords alone: 5,629 of 7,297 KB text/ellipsis; 2,337 of 3,096 text/wrap; 4,864 of 7,640 text/create; 9,373 of 18,123 richtext/open. Prototype KB/op: text/wrap 3,096 → 1,574; ellipsis 7,296 → 3,639; create 2,000 7,640 → 6,331; styled ellipsis 6,319 → 4,092; multi-font ellipsis 11,586 → 8,412; spans bold 1,042 → 614; text/open TextArea 7,104 → 4,617; richtext/open 5,000 18,198 → 11,681; richtext/resize 3,981 → 2,670; richtext/type 600 spans 1,966 → 1,081; richtext/type 5,000 blocks 88 → 68; styled wrap 613 → 461; bidi wrap 1,204 → 1,085; bidi ellipsis 7,137 → 6,457.
  - Expected: −10…−50 % on 14 rows. Bit-identical by construction (U+0020 splits only; RTL bypasses).
  - Proof: corpus test exact-array vs List-built runs (letter spacing, digits, multi-run fallback); `string.GetHashCode(span)` equals string hash; existing WordShapeCache, Truncation, WidthChange, PlainPath equivalence tests.
  - Cost: small–medium. Optional add-ons: scratch `List<FontRun>` in Itemize (~120 B/call), reuse evicted LRU node (~100 B/insert).
- **text-textarea-measure-lines.** `FitsWhole` asks for width only (default-implemented width seam like `MeasureTextPrefixes`; BiDi still uses Layout); keep visible line strings between frames until text changes; build prefix-table segments without List growth + ToArray.
  - Evidence: TextAreaElement.cs:422-430, :631-638 (substring + full Layout per line), :1402 (substring per visible line per frame), TextPipeline.cs:1562. text/open: 709 whole-line shapings, 35 lines drawn; LayoutBytes 3,043 KB (1,458 after diet); PrefixBytes 3,494 KB for 440 tables (2,622 after diet). Cold TODO.md: 998 lines shaped and kept in the shape cache, 50 drawn, 11,694 KB per open. 5,000-line wheel frame 10.3 KB (51 substrings).
  - Expected: text/open further ~−1.4 MB (~−30 %); shape-cache entries per opened file ~1,000 → ~50; TextArea scroll frame 10.3 → ~0.1 KB.
  - Cost: medium; small default-implemented IRenderContext addition. Test: FitsWhole vs Layout over a corpus.
- **text-textarea-incremental-wrap.** After `Replace`, re-wrap only touched hard lines and shift later line starts by the length change, instead of `InvalidateLines()`.
  - Evidence: TextAreaElement.cs:675-682, :340-398. input/type 2,000-line TextArea: 2,000 FitsWhole, 2,000 substrings, 2,042 Layout lookups per keystroke; ComputeLineStarts 263 of 471 KB; wrapped 900-line 224 of 485 KB; undo-redo 276 of 484 KB.
  - Expected: O(lines) → O(edited lines) per keystroke; those rows ~−55 % alloc.
  - Cost: medium. Test: incremental starts equal full recompute after random inserts/deletes/newlines/pastes, corpus with wrapped lines, surrogates, ZWJ.
- **text-shaper-reuse.** Drop the unused `SKPaint`, one `SKFont` per shaper (set Size per call), reuse one HarfBuzz Buffer (RTL path too).
  - Evidence: SkiaTextShaper.cs:20-21, :29, :37. 4,925 distinct words: 888 → 696 → 629 managed B/call; glyph ids, positions, widths identical (only unused cluster numbering differs for non-ASCII). Cold TODO.md: 5,136 calls, 5,458 of 11,694 KB. text/create 2,000: 1,815 of 7,640 KB.
  - Expected: −1.3 MB (−11 %) cold open, −7 % text/create; two native create/destroy pairs per call gone. Time win to be shown in build.
  - Cost: small. Test: corpus glyph ids/positions/widths old vs new (Latin, Hebrew/Arabic, emoji ZWJ, CJK fallback).

## Examined, not worth it

- Word cache for RTL runs: bidi ellipsis 3,004 shaper calls/step, styled 8,169 — by design (plan 14); apps rarely lay out RTL.
- Plain path for LTR emoji/ZWJ/marks: needs cluster-aware cutting + BiDi-vs-plain corpus; small real-world exposure.
- Width-keyed layout cache (2,048): reopen 0 of 4,065 hits; resizes miss by design.
- ICU breaks ≤33 calls/step; MinContentWidth ≤33 calls/step.
- Word cache bound (8,192): wrap 21,118 hits / 1.6 misses; ellipsis 49,494 / 0.2; cold TODO.md 85 % hits on first open.
- Moving TextArea line starts out of Render: no work saved.
- Incremental relayout of one rich text paragraph: richtext/type 600 spans 1,966 KB (1,081 with diet); too complex for what remains.

## Needs the user

None.
