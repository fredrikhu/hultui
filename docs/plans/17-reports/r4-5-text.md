# Round 4 survey: 5. Text pipeline

At 653971d; allocations, call counts and complexity only. Probes reverted; worktree clean. Probe code
in the session scratchpad `r4text/` (`probe_on.py`, `probe2.py`, `proto_mono.py`, `proto_rich.py`,
`soak.py`, `probe/` with an allocation-by-type sampler `AllocListener.cs`). Linux not checked.

SATISFIED: no

REVIEW: none.

- Allocation, full suite (122 rows, HEAD vs the dbcb7d8 capture): run 2 moved no row; run 1 had one
  one-off (richtext/select all 0.4 → 2.3 KB, back to 0.4 in run 2 and two filtered reruns).
- Round 3 on text rows: −4…−60 %; only richtext/wheel up (+0.7 KB, the known sub-blob map growth).
- dc6c047 equivalence soak after dbcb7d8: 12 seeds × 2,500 edits + the 1,500 standard, with plain-path
  emoji symbols (⚡ ✅ ⌚ ☕ ⭐) and VS16/keycap/skin-tone pieces added: 31,500 edits, 0 line-start lists
  differing from a fresh area.
- dbcb7d8: the itemizer looks ahead (VS16, modifiers, ZWJ); prefix tables assume a prefix itemizes
  like the run cut short — still holds, every look-ahead character sends the text to the BiDi path
  (no table). 3,000 random plain-path strings with emoji-presentation symbols, 4 styles, Segoe UI
  Emoji: 242,584 prefixes, table width = laid-out width to the bit.
- bff5581/896e99a: retained after close equal over 2 open/close cycles (TODO.md 2.66 MB, 18,884-line
  JSON 4.38 MB, 20,000-line log 3.66 MB). No leak.
- 7d6cc85: no text measuring off the UI thread in any app (TextMeasure only from Build).
- f0238b2: measurer cache keyed by the whole style incl. colour; a colour tween makes at most 64
  measurers of 64 B, then clears.

## Candidates

- **text-prefix-table-transient.** The TextArea's line-break search gets a prefix table built in the
  pipeline's kept scratch, as a view valid until the next table, over a range of the document — today
  every wrapped row pays a Substring, an exact copy of the segment array, PrefixRun[], PrefixWidths and
  MeasuredPrefixes (TextAreaElement.cs:919; TextPipeline.cs:1740 `segments.ToArray()`, :1858). Per table
  24 B + 20 B per segment + ~178 B of objects, plus 22 B + 2 B per char of substring. Tables are nearly
  all of the line-pass bytes: long-document open 1,175 KB per open (813 tables, 37,991 segments, 120,769
  chars); wrapped resize step Notes 1,291 KB (copies + objects 993, substrings 275), TODO.md 255 KB;
  20,000-line log open 65.8 MB (40,000 tables, 2.17 M segments), resize 69.2 MB/step; wrapped 900-line
  typing 12.6 tables per key (~17 of 269 KB). text/open a long document 1,373 → ~440 KB/op (−68 %), ~190
  (−86 %) without the substring; log open 65.8 → ~5 MB; wrapped resize −77…−98 %. Bit-identical by
  construction. Suggest a suite row "text/resize a wrapped TextArea". Small–medium: a new IRenderContext
  overload (default the old path) or a documented lifetime; the TextArea is the only caller.
- **text-textarea-mono-ascii-fits** (Needs the user). A TextArea skips measuring a printable-ASCII hard
  line in a face proven fixed-pitch (all 95 printable ASCII one advance, "MM" two) when (n + 1) ×
  advance ≤ wrap width — as Junction's diff heights and Vesper's comparison already do with a weaker
  proof. Prototype in FitsWhole (TextAreaElement.cs:695-707), mono 12 px, 860 px: 18,884-line JSON first
  open frame 2,126 → 318 KB, widths measured 18,887 → 0, word shapings 3,613 → 29, width cache 16,384
  entries / 3.6 MB → 96; TODO.md first open 1,961 → 1,422 KB, lines measured 997 → 233; row counts
  identical (14,633 and 861); `--verify` passes on 34 text/input scenarios. text/open highlighted JSON
  1,902 → 268 KB/op (−86 %); cost moves to scrolling (text/wheel highlighted JSON 7.9 → 13.3 KB/op).
  Retained after close JSON 4.38 → 0.23 MB (−95 %), TODO.md 2.66 → 1.50 (−44 %). Small. Risk: a mono
  face that kerned ASCII past one advance on a line would draw that line clipped, not wrapped.
- **text-paragraph-construct-diet.** PipelineTextParagraph builds one exact TextLine[] instead of a List
  plus a second array, makes its List<VisualRange> only when first used; ComputeWrappedStyled and
  ComputeWrappedBidi move their ellipsis lambda into a helper (a ~112 B closure is made on every styled
  or BiDi layout miss, ellipsis or not). PipelineTextParagraph.cs:19, :31, :38; TextPipeline.cs:780,
  :1436. Rich text open sample (8.3 MB per open): TextLine[] 6.9 %, List<TextLine> 3.3 %,
  List<VisualRange> 2.2 %, the display class 2.1 %. Prototype allocKB (JIT): selectable viewer wheel 2.2
  → 1.6 (−27 %); richtext/open −7.0 %; richtext/resize −7.0 %; styled wrap −3.8 %; bidi wrap −2.3 %;
  styled bidi wrap −1.9 %. Churn bar on the viewer wheel row (−27 %, only 0.6 KB/op). Small.
- **text-open-measures-every-line** (Waiting, re-measured; full version Needs the user). First open
  (warm code): TODO.md 1,961 KB, line pass 92 %, 997 widths and 3,741 word shapings for ~48 rows on
  screen (second open 181 KB); JSON 2,126 KB, line pass 93 %, 18,887 widths (4,608 width-cache misses),
  3,613 shapings (second open 261 KB). mono-ascii-fits covers the app cases (JSON fully, TODO.md 75 %);
  left: proportional fonts and non-ASCII lines. Full fix measures only on-screen lines and estimates the
  extent: large, behaviour change.
- **tooling-textbench-warmup** (Waiting). Warm-up passes call cold rows with the same indices
  (TextBench/Program.cs:51): 1,305 methods compiled before measuring, 928 during. Tooling. Small.
- **apps-tokenize-on-ui-thread** (Waiting, unchanged). Junction's markdown fences tokenize in Build
  under SyntaxHighlighter's single Gate while workers hold it for a whole file (3 s budget)
  (SyntaxHighlighter.cs:149/201/242; fences cached by (theme, language, code), 256 entries,
  JunctionApp.Markdown.cs:489-496). Latency only (GitLab views). Low.

## Examined, not worth it

- TextArea document as one string: the new string is 95–99.7 % of a keystroke (2,000 lines 210 of 220
  KB; 19k JSON 713 of 720; log 6,336 of 6,356). Still large (public Text contract); no new evidence.
- Cutting visible lines again after each edit (ShowLines version check): ~5–8 KB of a 220 KB keystroke.
- One prefix table per hard line instead of per row: windows overlap ~1.5× (~−33 % word lookups on
  wrapped lines); equality of row-start itemization and Han context unproven — transient tables first.
- Rich text open: GlyphPosition[] 31 % of the bytes; 2,607 glyph arrays (165k glyphs, ~2.6 MB) composed
  per open for 2,000 unstyled blocks the 4 MB layout cache cannot keep; keeping them needs the memo-line
  count past 4,096 — the retained-memory trade already declined.
- Word cache on a 20,000-line log with a unique token per line: 20,000 shapings per resize step; by
  design, an extreme document.
- SpansOf's List wrapper per rich text block: ~96 KB per 5,000-block open (~1 %).

## Needs the user

1. **text-textarea-mono-ascii-fits:** may a TextArea treat a printable-ASCII line in a proven
   fixed-pitch face as fitting when (n + 1) × advance ≤ width, without shaping it (as Junction's diff and
   Vesper's comparison already do)? Wins: JSON open −86 % bytes, −4.15 MB kept after close, TODO.md
   −27 %. Risk: a mono face that kerns ASCII by more than one advance across a line would draw it
   clipped instead of wrapped.
2. **text-open-measures-every-line (full version):** may a TextArea measure only the lines on screen
   and estimate the scroll extent of the rest, refining it as lines are drawn (the scrollbar can shift)?
