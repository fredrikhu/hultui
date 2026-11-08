SATISFIED: no

I measured at ea3e57ab (detached), headless only, and report bytes, counts and complexity. The probe is round 8's `types`/`dump` probe plus a new `HtmlProbe.cs`. One prototype was applied, measured and then removed by reverse edits; the worktree is clean. Linux and AOT were not checked.

REVIEW: none.
- **Layout dump and corpora still match.**
  - 48,000 layouts hash 7A18AF8DE8E05271 (1,004,690 lines, 22,578,320 glyphs).
  - Both 240,000-layout corpora (read 300 layouts late, and on the reference path) hash F09E9038726E4BD6.
  - Per layout, 0 differ from round 7 step 2's final files (`h48-a3`, `h240late-a3`, `h240ref-a3`).
- **bc007ae8, a10f21b1, 7aaae4c8, 48ed3a35:** the rows are as landed. KB/step, round 8 survey → now:
  - richtext/open 3,256.7 → 2,229.1 (−31.6 %)
  - resize 656.2 → 465.5 (−29.1 %)
  - enter 82.0 → 42.6 (−48.0 %)
  - Every other richtext/, text/ and TextArea row (61 rows) is within 0.2 KB.
  - text/create moves ±8 KB between runs (emoji 3,517–3,531 against 3,507), which is sampling noise. No Element or TextElement field was added in the range.
- **e7d10c63:** `PositionFromPoint` reads `ScreenContentBox`, which is O(depth) and 0 B, and only during a drag. `TickSelectionEdgeScroll` reads the clock once a frame even with no drag: one call, 0 B, noise. It adds no idle wake, because the frame request needs a drag held past an edge.
- **103eebf2 (pretty HTML/XML):** runs on the worker in `ResponseView.Format`, once per response. It makes the TextArea cheaper, not dearer. Cold open KB, as received → pretty:
  - 1 MB docfx toc.html: 8,271.6 → 6,344.7
  - 83 KB ELEMENTS.html: 964.9 → 867.6
  - the same page on one line ("minified"): 2,713.1 → 1,275.3
- **12d0392e:** not text. `OpenApi.FullText` reads the spilled spec and `Parse` runs after the await, on the UI thread, once per discovery. That is expert 8's to judge.

CANDIDATES:
- id: text-textarea-mono-ascii-breaks-by-count
  what: A wrapped row of printable ASCII in a proven monospace face breaks where its character count says, instead of a measured prefix search.
    - Today `WrapLine` → `FindLineBreak` (TextAreaElement.cs:814-1139) builds a prefix table over about 1.5 rows of text for every row, and shapes each word its probes cut.
    - The prototype computes n = floor(width / advance) instead. It then walks back to the last space exactly as the search does.
    - It falls back to the search when the width is within the proof's rounding of a multiple of the advance: (n + 1) × advance / 4096 + 0.001 px either side.
    - The check is per row: the row's first n + 1 characters must be printable ASCII.
  evidence:
    - Probe: headless 1440×900, response pane wrap 703 px, Meridian's Consolas 12; Junction Files in Cascadia Mono 13. Bytes for `area.Text = body` plus one frame, cold (new session) / warm (the same text again).

      | Body | Rows | Tip cold → proto | Tip warm → proto |
      |---|---|---|---|
      | Meridian: 1 MB HTML as received (toc.html) | 14,854 | 8,271.6 → 330.2 KB (−96 %) | 7,031.0 → 185.0 (−97 %) |
      | Meridian: the same, pretty (103eebf2) | 17,020 | 6,344.7 → 406.4 (−94 %) | 5,037.2 → 236.0 (−95 %) |
      | Meridian: 1 MB JSON as received (docfx index.json; 194 lines non-ASCII) | 13,550 | 12,122.2 → 3,144.5 (−74 %) | 10,400.6 → 311.8 (−97 %) |
      | Junction Files: 1 MB minified bundle (es-*.min.js) | 12,525 | 63,637.5 → 360.5 (−99.4 %) | 62,505.7 → 145.9 (−99.8 %) |
      | 83 KB docfx page as received / pretty | 2,177 / 4,073 | 964.9 → 506.2 / 867.6 → 513.0 | 40.8 / 72.5, unchanged |
      | Junction TODO.md (100 KB, 250 lines with — or →) | 1,694 | 2,652.5 → 1,987.2 (−25 %) | 24.2, unchanged |

    - In the bundle, 25,032 of 25,046 rows break by count; the tip searches all 25,046.
    - The warm tip cost is 76 % `GlyphPosition[]` and 13 % `String`: word shapings, including partial words the probes cut. They also fill the byte-bounded word cache with words nobody draws.
    - A variant that requires the whole hard line to be ASCII helps less: bundle −21 %, JSON −18 %, TODO.md −5 %, HTML the same as above.
    - **Rows identical:** 0 of every corpus's widths differ from the measured reference (`FitsByCount = false`) or from the tip, for both variants.
      - 13 corpus runs, 216–396 widths each: random widths of 40–1,400 px, plus 196 widths within ±0.0001–0.01 px of multiples of the advance.
      - Up to 7.0 M rows per 1 MB corpus.
      - The corpora include non-ASCII: TODO.md in two faces, DECISIONS.md, the ledger archive, index.json and the bundle.
    - Core TextArea 73 and Headless TextArea 83 tests pass with it. Every suite text and TextArea row has the same bytes, because no suite row has a long monospace line.
    - **Wheel after the open** (first 20 notches, every one new rows): words now shape when first drawn, as a476866c's JSON wheel did. KB/notch 6.9 → 7.5 (HTML), 19.0 → 22.5 (bundle), 7.9 → 20.3 (one-line page: +248 KB over 20 notches against −2,048 KB at the open). index.json goes down, 21.5 → 15.8.
  expected:
    - Churn bar on an app metric: opening a large response in Meridian, or a long-lined file in Junction Files, on the UI thread, −74…−99.8 %.
    - Scaling bar: a row costs O(1) plus one vectorized ASCII scan, not a prefix table and a search.
    - The build should add a suite row, e.g. "text/open a minified file in a monospace TextArea".
  cost: small. About 40 lines in TextAreaElement (`WrapLine`, `BreakByCount`, `SegmentAscii`); internal; no API change; no pixel change (the rows are the same).
    - The build must guard n ≥ 1 and a finite width: the prototype's `SegmentAscii` would throw on a NaN width.
    - It uses round 4 question 1's proof and carries the same risk. See Needs the user.

WAITING, RE-RANKED:
- junction-leads (old-side state convergence): not worth it.
  - Upper bound from `oldside.py`: 466 files changed in this repo's last 200 commits, with convergence assumed at every hunk start.
  - Lines walked fall 292,431 → 242,647, i.e. 82.2 → 68.2 % of new-side lines (−17 %). The old side alone falls 56,752 → 6,968.
  - It runs off the UI thread and would need an `IStateStack` comparison per hunk. Even at its best it clears a bar only if tokenizing is over ~60 % of a History key's all-thread bytes. I recommend closing it.
- textarea-chunked-document: keep parked; the user's 256k-character condition is still unmet.
  - The typing rows are unchanged: 206.5, 254.1, 204.6, reader 309.2 KB/key; the 30-word probe rows 753.0 and 825.6.
  - The only new large text is Meridian's read-only response pane (up to the 1 MB viewer cap). It is never typed into, so it does not meet the condition.
- text-open-measures-every-line (full): the user's "not now" stands. text/open a long document is 35.2 KB/step, unchanged.
  - The candidate above covers the monospace ASCII rows that still searched.
  - What is left measured at open: proportional faces, and lines with characters outside ASCII (TODO.md's 250 lines with — and →, about 1.99 MB cold).
- Question 10 (`TokenizeDiff` public) stays the user's.

EXAMINED, NOT WORTH IT:
- **A long-line cap per grammar (round 7's left-open item):** no new evidence, and DECISIONS.md ("A long line is coloured on its first 2,000 characters") declined a table per grammar.
  - SQL 849 ms, CSS 284, PowerShell 281, C++ 268, Python 246 per 2,000 dense characters. Every one still stops at its 250 ms budget, on a worker.
  - A smaller cap would colour fewer characters (a pixel change) to save worker time only. No frame bar.
  - 103eebf2 keeps `<script>`/`<style>` content verbatim, so a minified inline script stays one long line. HTML's cap of 2,000 bounds it at JavaScript's cost (79 ms).
- **Counting non-ASCII characters by advance (TODO.md's remaining cold open):** this would need the monospace proof extended per character, with fallback faces and marks in play. That is out of proportion to −25 % of a once-per-open cost on one file.
- **Pretty HTML's own work (103eebf2):** worker, once per response. It lowers the UI-thread open bytes (above).

NEEDS THE USER:
- text-textarea-mono-ascii-breaks-by-count: does question 1's "yes" (2026-10-13) also cover breaking long rows by count?
  - It uses the same proof (95 printable ASCII characters, every ordered pair within 1/4096 of one advance).
  - The rows match the measured path on every width tested.
  - The risk is question 1's: a monospace face that kerned ASCII past the proof would draw clipped instead of wrapped.
  - The orchestrator may treat it as covered.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\c484f5ac-8f22-48dd-b669-13c24b1e7889\scratchpad\r9-text\`
- **Probe:** `probe\` is round 8's probe plus `HtmlProbe.cs`.
  - New modes: `html <file> <width> [nocount|perseg] [plain] [font=..] [size=..] [types]` and `rows <file> <font> <size> <widths> [pretty|oneline|perseg]`.
  - Both read the prototype's `R9*` statics by reflection, so apply `proto-wrap-by-count.patch` from the worktree before rebuilding. The built `bin\` still has it.
  - `longline\` was copied from round 8 and not run.
- **Prototype:** `proto-wrap-by-count.patch`.
- **Tip rows:** `tip-text.txt`, `tip-textarea.txt`.
- **Dumps:** `h48.txt`, `h240late.txt`, `h240ref.txt`, compared with `hashdiff.py`.
- **Candidate numbers:** `open-bytes.txt` (three modes × 5 corpora), `html-toc.txt`, `html-elements.txt`, `html-toc-types.txt`.
- **Row equality:** `rows-perseg.txt`, `rows-line-toc.txt`.
- **Suite with the prototype:** `proto-textarea.txt`, `proto-perseg-textarea.txt`, `proto-perseg-text.txt`.
- **Tests:** `test-core-textarea.txt`, `test-headless-textarea.txt`.
- **Old-side estimate:** `oldside.py`, `oldside.txt`.
