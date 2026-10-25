# Round 2 survey: 5. Text pipeline

Counters and env-gated prototypes on 6ba7e2d, reverted. Probe and prototype patch in the
session scratchpad `r2-text/` (direct shaper with compare mode, width-only measure, span scan,
counters; PerfProbe.cs, ProbeJsonViewer.cs).

SATISFIED: no

REVIEW: none. A full 5×5 suite run matches the latest captures row for row (a 2×3 run reads
high on cold rows: harness artifact). Word cache text/wrap 21,126 hits / 2 misses; ellipsis
27,185 / 3 after 6891682 halved the measuring; bidi ellipsis shaper calls 3,004 → 2,353 per
step. 0178784's kept lists cannot be re-entered; every TextMeasure caller is on the UI thread.
d5d0c50's shared caches matter only for DevTools and multi-control WinForms; what empties the
shared shape cache is a TextArea open. 81be6ca measures only boxes < 2× font size, cached.

## Candidates

- **text-shaper-direct** (replaces text-shaper-reuse). SkiaTextShaper calls HarfBuzz itself, reads glyphs through `GetGlyphInfoSpan`/`GetGlyphPositionSpan`, one Buffer and one SKFont per shaper, one HarfBuzz font per face. Today each call builds an unused SKPaint, an SKFont and a Buffer, converts to UTF-8 and copies 5 arrays (SkiaTextShaper.cs:44-45, :53, :61). Prototype (SKShaper's arithmetic: scale 512, Size/512 × ScaleX): bytes per call 929 → 174 (text/create), 1,069 → 210 (bidi ellipsis), 1,508 → 300 (JSON open). allocKB: text/create 5,792 → 4,317 (−25.5 %); bidi wrap 1,082 → 649 (−40 %); bidi ellipsis 4,603 → 2,495 (−46 %); styled bidi wrap −44 %; styled bidi ellipsis 7,041 → 3,499 (−50 %); open highlighted JSON 10,810 → 6,604 (−39 %); grid/create −8 %; richtext auto-height −9 %; none up; AOT the same. **Bit-identity: 1,108,128 shaper calls old vs new (glyph id, X, Y, advance, width as bits), 0 mismatches** — 579,015 non-ASCII, 627,774 RTL; Arabic, Devanagari, Thai, Tibetan, Khmer, Myanmar, CJK + kana, Hangul + jamo, emoji (ZWJ, flags, VS16, skin tone), combining marks, lone surrogates, ligatures. Only unused cluster numbering changes. Small–medium. Build proof: corpus test on Windows and WSL Fedora, goldens.
- **text-textarea-patch-keeps-lines** (new). A patch with the same text re-wraps the whole document: the Text setter clears line starts even when unchanged (TextAreaElement.cs:62), TreeDiffer.cs:105 patches the TextArea on every parent rebuild, `GetLineStarts` checks only the width (:332). 19,013-line JSON: 1,412 KB and 19,013 lines measured per rebuild; 25,000 unique log lines: 25,001 shape-cache misses (a sequential pass longer than the LRU always misses), 64 MB per rebuild. Meridian MarkDirties the whole app per keystroke in the URL and body editors (MeridianApp.Requests.cs:336, :591), so each keystroke re-wraps the open response. Side bug (code reading): `_lineHeight` resets only on a render-context change (:1328), so a font-size patch keeps the old line height — test first. O(lines) → 0 per parent rebuild, ~−99 %. Small; needs a suite row (parent rebuild around a large TextArea).
- **text-textarea-span-scan** (confirmed). Binary search for the first span, index loop, one reused list per element (:1478-1492). JSON wheel: 611,651–622,767 span iterations per step for 53–54 lines → 117; step 14.4 → 4.1 KB; row 15 → 5 KB/op (−67 %); should close the 66 % AOT-vs-JIT gap on that row. **Trap:** Meridian merges search hits into syntax spans sorted by Start only (MeridianApp.Response.cs:470) — spans overlap and End is not monotone; search the running maximum of End, or check monotonicity once per list with a fallback. Small. Test: random start-sorted lists with overlaps vs the linear scan.
- **text-textarea-incremental-wrap** (confirmed). After Replace or Undo/Redo (:675-683, :111-118) re-wrap only touched hard lines and shift later starts. Per keystroke: 2,000 FitsWhole, 2,000 substrings, 2,041 lookups; ComputeLineStarts 268 of 466 KB (input/type 2,000-line), 213 of 464 (wrapped 900-line), 278 of 477 (undo-redo). −46…−58 % on 3 rows. Medium. Test: incremental starts equal a full recompute after random edits (wrapped lines, surrogates, ZWJ).
- **text-textarea-measure-lines** (confirmed, re-scoped). FitsWhole asks a width only through a default-implemented `IRenderContext.MeasureTextWidth` (MeasureString, bit-equal to ComputeShaping's TotalWidth), kept per hard line; optionally keep visible line strings between frames (:1402). JSON open: 18,971 lines, 14,557 hits / 4,467 misses costing 9.43 MB (5.29 in the shaper), substrings ~1.2 MB. Prototype: JSON open 10,810 → 6,888 KB (−36 %), long document open 4,614 → 3,421 (−26 %), typing flat, wheel 14 → 20 KB (lines shaped when first drawn). With text-shaper-direct: JSON open 10,810 → 2,687 KB (−75 %). 8,716 width results vs Layout: 0 mismatches. A JSON open no longer adds 4,467 entries to the shape cache. Medium; one default-implemented interface method + Recording forwarder.
- **tooling-textbench-warmup** (confirmed). Warm until the JIT settles, add `--filter`, re-run with `DOTNET_TC_CallCountingDelayMs=0`, print methods compiled during measurement. Full run: 1,300 methods compiled before measuring, 1,290 more over the 25 rounds (only 42 with tiering off → tier-ups); filtered to 3 rows 943 vs 1,889 with delay 0. The perf suite shows the same (+1,461 / 343 / 211 / 571 during rounds 1–4) — lead for 4. Small.

## Examined, not worth it

- Skipping shaping for "provably short" lines: no proven advance bound without shaping (kerning, fallback); the width-only path gets the bytes safely.
- ComputeShaping's List wrappers: ~128 B per cold label (~5.6 % of text/create).
- d5d0c50 cross-window competition: no app has a second window; caches bounded.
- syntax-maxlines-uncoloured as perf: the TextArea side stops depending on span count once span-scan lands; the remaining cost is tokenizing on the UI thread (Needs the user).

## Needs the user

1. **Colour Meridian responses longer than 20,000 lines?** (SyntaxHighlighter.cs:99, :302). Meridian tokenizes synchronously in Build (MeridianApp.Response.cs:378) at 0.3–0.5 s per 19k-line response; raising the limit lengthens that UI freeze unless tokenizing moves off the UI thread first.
