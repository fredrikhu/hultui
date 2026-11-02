# Round 6 survey: 5. Text pipeline

I measured at ef4288ad, the plan 17 tip, which contains round 5's build 643eb07b..f31194ab. Everything ran headless and I report only bytes, counts and complexity. I ran the perf suite's own scenarios through a probe: 8 warm-up steps, then 16–32 measured steps, with allocations sampled by type through GCAllocationTick. Temporary counters recorded lines made against lines drawn, the run and string bytes of each line, substrings and closures. I measured three prototypes, and a 48,000-layout bit dump compares two builds by hash. I also used a headless Meridian driver with allocations by type, adding a walk with an 89k-character body. All of it is in `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\855f4ddd-feda-4f0a-a595-a83e07af08f8\scratchpad\r6-text\`:
- `probe\` (modes `types` and `dump`) and `types.sh`;
- `mdrv\`, the Meridian walks;
- `counters2.patch` with `R6Counters.cs`;
- `proto-strings.patch` with `proto_strings.py`, `proto-meridian-merge.patch` and `proto-richinsert.patch`;
- results `base-text.txt`, `proto1.txt`, `proto2.txt`, `cnt1-4.txt`, `mer1-3.txt` and `suite-wheel.txt`.

Every edit was removed with `git apply -R` and the untracked counter file was deleted. The worktree is clean and was rebuilt from clean source. Linux was not checked.

SATISFIED: no

## Review

REVIEW: none.

- **91d48601, scroll rows paying first draws: the figure still holds, and it does not matter in an app.**
  - The suite at ef4288ad reads: selectable viewer wheel 11 KB/op, richtext/wheel 9, scroll/jump 10. The ledger has 10.5, 8.3 and 9.8.
  - In the probe a viewer wheel step makes 0 new lines and draws 15 lines that were laid out earlier. GlyphPosition[] is about 15 % of the step; the rest is realizing the list's rows.
  - The cost moved from open to the first draw; it was not added. A steady redraw costs 0.2 KB (viewer hover), and scrolling back over drawn lines composes nothing.
- **91d48601's thread rule holds.** Every `ShapedRun.Glyphs` reader is in Skia, TextLine, the paragraph or the pipeline. `TextMeasure` returns sizes only, and the pipeline is single-threaded by contract.
- **cc6f8467 and a1ff84a1:** no caller holds a kept list across another itemize. No objection.
- **6895ac2d:** the reader row is 309.7 KB/key. The document copy is about 243 KB of that, and the UIA row with no reader reads 260.4.
- **9c0419ce:** a correctness fix for the clock; it changes no allocation.
- **No text row rose against round 5's landed figures.** Probe readings: text/wrap 391.8, richtext/open 4,612, richtext/resize 979 KB/step.

## Candidates

- **id: text-line-strings-once** (5)
  - **what:** a line, an ellipsis and a measure allocate one string or none, and ellipsis makes no delegate.
  - **the changes:**
    - `ShapeLineText` (TextPipeline.cs:2898) is called with `text[from..to]` and then trims it with `TrimEnd()`; cut only the trimmed range instead.
    - The ellipsized line (:1727) is built as `tail[..best] + "…"`; use `string.Concat(span, "…")`.
    - `MeasureLineText` (:2948) should itemize ranges (`ItemizeRange`) and measure a range, not cut three strings.
    - The same range measure replaces `MeasureString(text[a..b])` in `MeasureStyled` (:660), the `FitPrefix` fallback (:526) and `BreakIntoLines` (:2834). It is the word lookups by span that `ShapeSegment` already does.
    - `ApplyEllipsis` (:1646 and :2156) builds a closure and a `Func` for every ellipsized layout. Split it into a test of the tail range and a replacement of the last line.
  - **evidence, counts per step:**
    - text/wrap: 300 of 450 lines cut a second, trimmed string, at 127 characters a line.
    - text/ellipsis: 981 ellipsized lines each make two strings.
    - multi-font ellipsis: 14,262 itemize substrings and 1,000 closures.
    - styled ellipsis: closures are 10 % of the row, plus 2,320 `MeasureStyled` substrings.
    - Typing in a 600-span paragraph: 2,250 substrings per key.
  - **evidence, prototype (KB/step):**

    | Row | Before | After | Change |
    |---|---|---|---|
    | text/wrap | 391.8 | 293.3 | −25 % |
    | text/ellipsis | 610.4 | 447.0 | −27 % |
    | multi-font ellipsis | 2,141 | 1,320 | −38 % |
    | styled ellipsis | 984 | 741 | −25 % |
    | type in a 600-span paragraph | 592 | 515 | −13 % |
    | bidi ellipsis | 1,516 | 1,360 | −10 % |
    | styled bidi ellipsis | 1,724 | 1,568 | −9 % |
    | richtext/open, richtext/resize | – | – | −6 % |
    | layout/cards create 300 | – | – | −2 % |

    No row rose (emoji labels +0.07 %).
  - **bit-identical:** 48,000 layouts, two widths each, dumped as bits: 1,004,690 lines, 1,194,845 runs and 22,578,320 glyphs. The corpus covers plain, styled, BiDi, Rtl, letter-spaced, fallback faces, CJK, a soft hyphen, trailing spaces, ellipsis and maxLines. Hash 7A18AF8DE8E05271 on both builds.
  - **expected:** churn bar on 7 rows.
  - **cost:** small. No API or pixel change.

- **id: text-lazy-line-runs** (5; Waiting, re-ranked with numbers)
  - **what:** a line the pipeline breaks keeps its width, metrics and range, and makes its run strings and run objects the first time `TextLine.Runs` is read. This follows 91d48601's pattern.
  - **evidence:** lines made against lines drawn per step, and the run and string bytes of the lines never drawn, all at base:

    | Row | Lines made | Lines drawn | Bytes of undrawn lines | Share of the row |
    |---|---|---|---|---|
    | text/wrap | 450 | 36 | 251 KB | 64 % |
    | text/ellipsis | 984 | 38 | 270 KB | 44 % |
    | multi-font ellipsis | 1,999 | 38 | 916 KB | 43 % |
    | styled wrap | 450 | 33 | 96 KB | 52 % |
    | styled ellipsis | 1,998 | 34 | 337 KB | 34 % |
    | richtext/open | 7,838 | 29 | 1,163 KB | 25 % |
    | richtext/resize | 1,664 | 30 | 271 KB | 28 % |
    | layout/cards create 300 | 1,922 | 132 | 199 KB | 8 % |

  - **expected:**
    - On top of text-line-strings-once: text/wrap about 293 → 140 KB (−50 %), multi-font about −65 %, richtext/open and resize −24…−26 %.
    - Retained: the open 5,000-block document holds about 1 MB less of its 10.6 MB (an estimate). That clears the retained bar.
  - **design notes:**
    - Width without shaping is `MeasureLineText`'s rule (`MeasureString` of the trimmed range equals `ShapeLineText`'s sum), so it needs text-line-strings-once's range measure.
    - Keep the text, style and pipeline in one object per layout, not in each line: `ResolvedInheritedStyle` is a struct of about 60 B, which per line would eat a third of the saving.
    - Three reads would make every line's runs at once: `LayoutBytes`/`GlyphBytes` (:92-112; keep a run count from the itemize), `DrawParagraph` reading `line.Runs.Count` before its clip test (SkiaRenderContext.cs:890; test the clip first), and `PipelineTextParagraph.PrefixMeasurer` (caret only).
    - The thread rule is the same as for `Glyphs`.
  - **cost:** medium–large. No API change; pixels the same by construction.

- **id: meridian-find-typing** (5/8; Waiting, re-measured)
  - **what:** a find character merges every syntax span of the response.
  - **evidence:**
    - 1,528.7 KB per character, unchanged since round 5. TextSpan[] is 91.5 % of it.
    - `DecorateResponseBody` (MeridianApp.Response.cs:487-510) copies all syntax spans into a list, adds the hits (the list grows into a second array twice the size), then sorts: O(n log n) per character.
    - Prototype, an exact-capacity merge of the two sorted lists with no sort: 615.3 KB/char (−60 %). What remains is one copy of every span (79 %) and Hit[] (10 %).
    - Junction's `DecorateFileBody` (JunctionApp.Files.cs:966-990) is the same code and also rebuilds `LineStarts` every build.
  - **options:**
    - (a) The app-side merge in both apps.
    - (b) A framework layer: a TextArea draws a second, background-only span list over `Spans`, so a key hands over only the hits. That is O(hits) instead of O(document spans), about −90 %. The app's own comment puts a 98,004-line response at 14.5 MB a key.
  - **expected:** churn bar; (b) also the scaling bar.
  - **cost:** (a) small, app code. (b) small–medium, an API addition of one property.

- **id: richtext-insert-one-pass** (5/editor)
  - **what:** typing in a rich paragraph builds its text and spans in one pass.
  - **evidence:**
    - `InsertInline` (RichEditState.cs:672-679) makes two `Slice`s, whose builders start at the default capacity and grow by doubling, then copy in `ToImmutable`. It then makes two `Append`s, each a Concat builder plus a copy, and four strings.
    - Prototype, one `string.Concat` and one span pass at exact capacity, making the same `Add` calls in the same order: typing in a 600-span paragraph 591.9 → 433.1 KB/key (−27 %), strings 30.6 → 21.5 %.
    - About three more span-array copies a key remain elsewhere in the edit; not traced.
    - The prototype was checked only by the scenario's text-length check.
  - **expected:** churn bar on one row.
  - **cost:** small, no API change.

- **id: meridian-body-edit-bytes** (8/5; Waiting, re-measured)
  - **evidence:**
    - Typing in an 89k-character body: 1,898.9 KB/key, about 10.7 copies of the document. Strings are 78.7 %, char[] 12.3 %, StringBuilder 6.2 %.
    - The TextArea's own copy is about 178 KB (9 %).
    - The rest is Meridian's model. `ReqDocument.SetBody` (ReqDocument.cs:229-256) splits the body into lines and calls `Trim()` per line (it allocates for indented JSON), with a `SourceLine` each. Then `Touch` → `Reparse` (:84) walks every line again.
  - **what:** keep the body block as one string.
  - **expected:** −80…−90 %.
  - **cost:** small–medium, app code.

- **id: apps-junction-highlight-prefetch** (5/8; Waiting, keep at the top of the app items)
  - **what:** tokenize only what the diff can show.
  - **evidence:**
    - The code is unchanged since round 5.
    - `JunctionApp.Highlight.cs:93-109` tokenizes both whole files per prefetch. `SpansFor` (:126-134) reads the old side only for deletions, and only for lines the hunks show.
    - The total comparison put it at about 10 MB/key on a worker, a core kept busy, and most of the walk's GC pause.
  - **expected:** −50…−90 % of worker bytes per History key.
  - **cost:** medium.

## Examined, not worth it

- **textarea-chunked-document** (close, or keep parked): measured, it does not pay.
  - In the only large-document edit, Meridian's 89k body, the TextArea's copy is 9 % of the key.
  - The TextArea suite row is exactly one copy per key: 206 KB at about 110k characters.
  - Since the large-object threshold landed (828a7d69), documents up to 256k characters stay in gen 0, and no app edits larger ones.
- **text-open-measures-every-line:** no new evidence; the user said not now.
- **apps-tokenize-on-ui-thread:** code unchanged (9c0419ce is clock correctness). It costs latency only and stays low.
- **richtext-short-viewport-jitter:** not perf. The scroll code is unchanged; only 415ecefa's ApplyTheme touched the editor. It stays.
- **The rich document's block array copied per keystroke:** RichBlock[] is 86 % of richtext/type in 5,000 blocks, which is 51.6 KB. `Blocks` is a public `ImmutableArray`, so this is a 40 KB memcpy by API.
- **GlyphPosition[] on the BiDi rows (25–34 %):** each new cut shapes its RTL pieces with HarfBuzz. A word cache for RTL was declined in round 1.
- **Itemize substrings for emoji labels (8,000 per step):** the public `ShapedRun.Text` is a string. They would go only with lazy runs.
- **text/create:** TextElement is 48.7 % of the row: element size, not text (P5c).
- **An exact run array instead of `List<ShapedRun>`:** 32 B per line, −1…−4 %, under the bar alone.
- **text/spans recolor and bold:** the TextSpan[] there are the scenario's own.
- **Typing in a TextArea while a reader reads:** +49 KB/key over UIA with no reader, after round 5's −56 %. A narrow case.

## Needs the user

None. meridian-find-typing's option (b) adds one public TextArea property; the orchestrator decides whether that needs asking.
