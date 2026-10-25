# Round 5 survey: 5. Text pipeline

At 969f3308. I measured allocations, call counts and complexity only, never timings. Sources:
- a full perf-suite capture (`r5text/full-head.json`), compared with round 4's survey capture at ba817e8 (`r4text/full-head.json`);
- a headless probe that runs the suite's own scenarios (8 warm-up steps, then 16–30 measured), with allocation-by-type sampling;
- temporary counters in HultUI.Text and Skia (glyph arrays composed versus drawn, layouts, last-line tables, TextCaretX calls);
- three prototypes switched by environment variables: `R5_LAZY`, `R5_SCRATCH` and `R5_HOIST`.

Probe code is in the session scratchpad `r5text/`:
- `probe/`: links the suite's .cs files and has the modes `types`, `lazycheck` and `retain`, plus `ProbeScenarios.cs`.
- `probe-and-protos.patch`: the counters and all three prototypes.
- Scripts `types.sh`, `ab.sh`, `envab.sh`, `retain.sh` and `cmpalloc.py`.
- Results `counters-all.txt` and `ab-lazy.txt`.

All edits were reverted with `git apply -R`, the worktree is clean, and the binaries were rebuilt from clean source. Linux was not checked.

SATISFIED: no

## Review

**Full suite, allocation (ba817e8 → 969f3308).**
- Text rows went down: a long document opened in a TextArea −72.9 %, highlighted JSON open −85.9 %, wrapped 900-line typing −5.4 %, richtext/open −7.2 %, richtext/resize −7.0 %, richtext/select all −87 %, styled wrap −3.8 %.
- Two rows went up, both already explained:
  - The JSON wheel went 7.9 → 8.5 KB/op. It was 9.6 after 9a5f6a2f (words are now shaped when a line is first drawn) and fell to 8.5 at 13c66d49.
  - The selectable viewer wheel went 2.2 → 2.7 KB/op. It was 1.6 after 0567c1ce; 13c66d49 added 1.1 KB, which its commit explains as a one-off growth of the run-blob map landing inside the timed steps. That is painting, not text.

**1333083b:** no objection. The view's lifetime holds.
- `FindLineBreak` makes no text call between asking for the view and reading it.
- A `MeasureWidth` hit leaves the view held, which is harmless; a miss releases it through `BeginParagraph`.
- The range's Han context stays in force until the next `BeginParagraph`. Only `PrefixWidth`'s surrogate fallback could itemize under it, and that path is unreachable.

**9a5f6a2f:** no objection.
- The proof is sound.
- The (n + 1) × advance margin covers the worst drift (n × advance / 4096) up to 4,096 characters per line, which is a wrap width of about 30,000 px.

**0567c1ce, 13c66d49, 0fcc8f5e, 4344dd3a:** nothing for the text pipeline.

**Objection, a0221337 (TextArea text pattern).** With a reader reading, every keystroke rebuilds the accessible text from the layout.
- `BuildAccessibleText` (TextAreaElement.Accessibility.cs:174-186) asks `TextCaretX` for every character of the rows on screen ±1.
- Each call goes through `CaretXOnLine` → `LineText` (TextAreaElement.cs:1743, :2009). During a keystroke the drawn strings are stale until Render, so each call cuts a new substring and does a layout-cache lookup that hashes the whole row.
- 2,000-line area, 10-word lines: 709 KB per key with a reader against 260 KB without (+449 KB, +173 %); 2,724 TextCaretX calls and 174k characters hashed per key.
- 30-word lines: 1,945 against 753 KB per key (+1,192 KB); 3,976 calls and 528k characters.
- The cost is O(rows × row length²) per key.
- The commit's own probe ran without a reader asking, so it never saw this. A caret walk with a reader is fine: 1.1–1.2 KB per key, because the tables are shared.

## Candidates

- **id: text-glyphs-on-first-draw**
  - what: a run built from the word cache (`ShapeByWords`' multi-word path) keeps its width and glyph count and composes its glyph array the first time something reads `ShapedRun.Glyphs`. Layouts that are measured but never drawn then compose nothing.
  - why it matters: the shape cache's single line for every wrapped paragraph, every wrapped line off screen, and every intermediate measure width are composed today and never drawn.
  - evidence, composed versus drawn (counters):

    | Row | Composed per step | Share of the row's bytes | Drawn |
    |---|---|---|---|
    | text/wrap | 885 KB | 63 % | 8.1 % |
    | text/ellipsis | 1,124 KB | 59 % | 3.7 % |
    | richtext/open (per open) | 3,430 KB | 37 % | 0.4 % |
    | richtext/resize | 841 KB | 40 % | 2.0 % |
    | text/create 2,000 labels | 840 KB | 22 % | 2.2 % |
    | layout/cards create 300 | 770 KB | 21 % | 4.4 % |
    | grid/create | 205 KB | – | 14.5 % |
    | layout/shrink 300 rows | 175 KB | – | 11.9 % |

    GlyphPosition[] is the largest allocated type on every wrap, ellipsis and rich text row.
  - evidence, prototype (about 40 lines):
    - The lazy getter is at TextTypes.cs:365. In `ShapeByWords` (TextPipeline.cs:2470-2492) the width is the first pass's sum of word widths, added in the same order.
    - The cache weights `GlyphBytes`/`LineBytes` (TextPipeline.cs:101-112, :1027) read a kept glyph count instead of forcing composition.

    | Row (probe, KB/op) | Before | After | Change |
    |---|---|---|---|
    | text/wrap | 1,399 | 569 | −59 % |
    | text/ellipsis | 1,874 | 766 | −59 % |
    | richtext/resize | 2,076 | 1,239 | −40 % |
    | richtext/open | 9,277 | 5,793 | −38 % |
    | layout/shrink | – | – | −25 % |
    | text/styled wrap | – | – | −23 % |
    | text/multi-font ellipsis | – | – | −22 % |
    | text/create 2,000 labels | 3,778 | 2,939 | −22 % |
    | text/styled ellipsis | – | – | −16 % |
    | text/create emoji labels | – | – | −13 % |
    | text/bidi wrap | – | – | −9 % |
    | grid/create | – | – | −8.5 % |

  - evidence, retained heap:
    - Rich text, 5,000 blocks: 25.04 → 10.83 MB while open (−57 %), 10.57 → 4.16 MB after close (−61 %).
    - 150 paragraphs resized through 40 widths: 1.69 → 0.81 MB (−52 %).
  - evidence, bit-identical: 20,000 layouts compared bit for bit (plain, styled spans, BiDi with Hebrew, Arabic and astral emoji, letter spacing, fallback faces, ellipsis, maxLines): 397k lines, 489k runs, 10.4M glyphs, 0 differ.
  - what goes up: rows that scroll fresh lines into view now pay for their glyphs there.
    - Selectable viewer wheel 1.2 → 7.3 KB/op, richtext/wheel 0.2 → 5.9.
    - JSON page down 38.7 → 40.5 and JSON wheel 4.7 → 4.9. These two come from the prototype's ShapedRun being 16 B bigger; keeping the lazy state behind one reference would halve that.
    - Over a whole session, less is composed, not more.
  - expected: churn bar on 12 rows (−8…−59 %); retained bar (−52…−61 %).
  - cost: medium.
    - No API change: `Glyphs` stays a public getter and the new constructor is internal.
    - Glyphs are composed on the pipeline's thread. Every reader of `Glyphs` (the Skia context, `TextLine` methods, the a11y geometry snapshots) runs on the UI thread.
    - A drawn run's words are looked up twice: once at layout, once when composed.
    - Pixels are the same by construction; the build should still run an A/A pixel check.
    - Real apps gain where text is laid out but not drawn: Junction's markdown views and review/notes editors, long dialogs, resizes. Virtual lists are flat (0 change on all 7 virtual rows). The build should confirm on Junction.
  - follow-up lead, not proposed this round: lazy line runs. After this candidate and the next, the line strings (`text[from..to]` plus `TrimEnd`) and run objects of undrawn lines are most of what remains: strings are 50 % of text/wrap and 47 % of text/ellipsis, run objects about 28 % of ellipsis, and 92–96 % of those lines are never drawn. Medium–large.

- **id: text-line-temporaries-scratch**
  - what: the pipeline's one-shot lists go into kept scratch, as `MeasureString`'s `_measuredRuns` already does.
    - `Itemize(text, style)` makes a new `List<FontRun>` on every call (TextPipeline.cs:225-226), from `ComputeShaping` :554, `ShapeLineText` :2807, `MeasureLineText` :2856, styled :662/:680 and BiDi :1280/:1319. No caller holds the list across another itemize.
    - Also in reach: the BiDi line's `SegmentsIn` list (:896-909), its per-segment lists and level array (:1303-1312), `VisualOrder`'s int[] (:917-920), and `ShapeLineText`'s `List<ShapedRun>` that grows to 4 slots for one run (:2778).
  - evidence, the itemize list alone (prototype, KB/op):

    | Row | Before | After | Change |
    |---|---|---|---|
    | text/styled wrap | 405 | 296 | −27 % |
    | text/styled ellipsis | 1,712 | 1,349 | −21 % |
    | text/bidi ellipsis | 3,114 | 2,460 | −21 % |
    | text/bidi wrap | 655 | 532 | −19 % |
    | text/multi-font ellipsis | 3,852 | 3,269 | −15 % |
    | text/create emoji labels | 7,204 | 6,123 | −15 % |
    | richtext/type in a 600-span paragraph (a keystroke) | 809 | 698 | −14 % |
    | text/ellipsis, text/create | – | – | −6 % |
    | richtext/open, richtext/resize | – | – | −5 % |
    | text/wrap | – | – | −4 % |

    - On top of text-glyphs-on-first-draw: text/ellipsis −15 %, multi-font −20 %, styled wrap −35 %.
    - The BiDi temporaries are another 17 % of emoji create (ValueTuple<int,int,byte>[] 10.2 %, its List 3.3 %, Int32[] 4.0 %).
    - Included in the 20,000-layout bit check: 0 differ.
  - expected: churn bar on 7 rows on its own.
  - cost: small, no API change.

- **id: text-last-line-view** (the Waiting prefix-table lead, measured)
  - what: `BreakIntoLines` builds a durable `PrefixWidths` for a wrap's last line on every layout miss (TextPipeline.cs:2731-2733): a substring, exact run and segment arrays, and an object. It is used only for that call's first cut. Fill the kept scratch lists instead, with the paragraph's own Han context (`FillPrefixTable` over a range, which landed in 1333083b), and measure with the span overload of `PrefixWidth`.
  - evidence:
    - text/wrap: 150 tables, 3,230 segments and 10,642 characters per step, about 100 KB = 7 % today. After the two candidates above it is 15–19 % (PrefixSegment[] 13.1 %, PrefixWidths 2.0 %, plus the substrings).
    - richtext/open: 1,065 tables per open, 18,721 segments, 61,840 characters, about 0.68 MB = 7 % today, about 12 % after text-glyphs-on-first-draw.
  - expected: churn bar on text/wrap only after text-glyphs-on-first-draw.
  - cost: small, bit-identical by construction.

- **id: text-textarea-a11y-row-xs** (owned by 5, a11y expert 7 to comment; fixes the a0221337 objection)
  - what: cut each row's string once, and ask for the caret x of every boundary in one call.
  - evidence:
    - Hoisting only the string (prototype): 709 → 310 KB per key (−56 %) on 10-word lines, 1,945 → 826 (−58 %) on 30-word lines.
    - The TextCaretX calls remain (2,724 and 3,976 per key), each hashing the row. An `IRenderContext.TextCaretXs(lineText, style, Span<float>)` would make it O(rows × length) instead of O(rows × length²). Its default loops `TextCaretX`; Skia does one `SingleLine` lookup, then `CaretXAt` per boundary.
  - expected: churn bar (−56…−58 %) and scaling bar.
  - cost: small. The second half is an API addition (a default member).
  - suggested suite row: "a11y/type in a 2,000-line TextArea, a reader reading, bridge on".

## Examined, not worth it

- **`MonospaceAsciiAdvance` through `TextMeasure`/`IMeasureContext`** for Junction's diff heights (JunctionApp.Diff.cs:404-433) and Vesper's comparison (CommanderApp.Diff.cs:159-214):
  - Both apps already skip measuring with the same (n + 1) × advance rule.
  - The faces they use (Cascadia Mono, Consolas, and Cascadia Code, whose ligatures keep one glyph per character) pass both checks. No count or byte would change.
  - It is a public API for consistency only, with no measured win: a guideline note at most.
- **apps-tokenize-on-ui-thread:** SyntaxHighlighter.cs and JunctionApp.Markdown.cs have not changed since round 4 apart from a licence header. Latency only; stays low.
- **text-open-measures-every-line (full):** no new evidence. The user said not now.
- **JSON wheel 8.5 KB/op:** the accepted monospace trade; words are shaped when first drawn.
- **Selectable viewer wheel 2.7 KB:** the timing of the sub-blob map's growth (13c66d49, painting).
- **TextArea caret walk with a reader:** 1.1–1.2 KB per key; tables are shared.

## Needs the user

None.
