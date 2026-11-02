# Round 7 survey, expert 5: Text pipeline

SATISFIED: no

Measured at 57e6547c, headless only; bytes, counts and complexity. Probe: the perf suite's own scenarios, 8 warm-up
steps then 24 measured, allocations by type from GCAllocationTick. Meridian: round 6's headless walk driver, rebuilt
on the tip. Long lines: a new probe that tokenizes one line at growing lengths. Bit-equality: the 48,000-layout dump.
Three candidates prototyped and reverted with `git apply -R`; the worktree was left clean. Linux not checked.

## REVIEW

None.

- **No text row rose against round 6's landed figures.** text/wrap 66.6 KB/step (ledger 67), richtext/open 3,477
  (3,480), 600-span typing 327.7 (327.9), find typing 54.1 (54), find Enter 25.5 (26). Multi-font ellipsis 422.6
  against 414: +2 %, probe against suite.
- **afa7116d (lazy line runs):** a drawn line still measures, then itemizes and looks its words up again when its runs
  are made. At most +2.6 % time in sessions that draw every line (step 14's figure), under the time bar; wheel rows
  3.3–4.7 KB/step. The latent case is already logged: a font registered between a layout and its first draw
  (`LineStyle`, TextTypes.cs:409, chooses faces again when the runs are made).
- **8b6c3950:** the blob map hashes a run's text, O(length) per lookup of a run new to the map — the same order as the
  glyph hash it replaced. No objection.
- **65a45f59, 0af738d9, 985adef7, 9947adaf, eac5f19b:** no objection.

## CANDIDATES

- id: text-find-lazy-line-hits
  what: `TextSearch` lists a line's hits only when a row asks for them, instead of making a `List<Hit>` for every line
  with a hit on every rerun.
  evidence: TextSearch.cs:378-383 fills `_byLine` with a new list and its array (~120 B for one hit) for each line with
  a hit, on every typed character. Only `HitsOn` and `Decorate` read them, for rows on screen; a TextArea using
  `Highlights` never reads them. Hits are added in line-then-lane order, so a line's hits are one run of `_hits`; the
  prototype finds that run by binary search on first ask and keeps it until the next rerun. text/find typing in
  highlighted JSON 54.1 → 24.9 KB/key (−54 %); find Enter flat at 25.5 (no rerun). Meridian response find typing,
  2,000-line JSON: 132.2 → 60.4 KB/key (−54 %; `Hit[]` and `List<Hit>` were 46 % of the key). Junction's and Vesper's
  diff and viewer rows (`Decorate`/`HitsOn`) then get lists only for rows on screen — may explain part of the diff
  find cost in junction-leads; not measured.
  expected: churn bar on one suite row and one app walk, −54 % each.
  cost: small, no API change (`HitsOn` returns the same type). A line with no hits costs a binary search over the
  hits instead of a hash lookup, 0 B.
- id: textarea-touched-rows-sized
  what: when a TextArea's text is replaced, the list of rewrapped line starts is sized to the hard lines up front.
  evidence: `_touchedStarts` (TextAreaElement.cs:513-517) is dropped above 4,096 entries (:524), so each replaced
  document grows it again from empty by doubling: for the 19,000-line JSON ~15 int arrays, ~256 KB, for a 76 KB
  result (the row was 100 % `Int32[]`). Prototype, one line, `touched.EnsureCapacity(kept.Count - back - front)`:
  text/open highlighted JSON in a TextArea 259.4 → 77.3 KB/step (−70 %); every other TextArea row unchanged. In the
  apps: Meridian's response viewer on every send, and any same-style text replacement over 64 rows.
  expected: churn bar, −70 % on one row.
  cost: small, no API or pixel change.
- id: text-exact-line-arrays
  what: the three line breakers fill one list the pipeline keeps and give each layout an exact `TextLine[]`; an
  ellipsized layout replaces its last line in that list instead of building a clip layout and copying it in
  `WithLastLine`.
  evidence: TextPipeline.cs:845 (styled), :1552 (BiDi) and :2985 (`BreakIntoLines`) start a `new List<TextLine>()` at
  default capacity, which the layout keeps; `WithLastLine` (:1789) makes a second list and layout for every truncated
  label. In styled ellipsis, `List<TextLine>`, `TextLine[]` and `TextLayout` were 63 % of the row. Prototype, exact
  arrays alone (KB/step):

  | Row | Before | After | Change |
  |---|---|---|---|
  | styled ellipsis | 374.8 | 288.9 | −23 % |
  | multi-font ellipsis | 422.6 | 336.7 | −20 % |
  | text/wrap | 66.6 | 60.7 | −9 % |
  | styled wrap | | | −8 % |
  | richtext/open | 3,477 | 3,262 | −6 % |
  | richtext/resize | | | −6 % |
  | bidi ellipsis | | | −6 % |

  No row rose. Adding "no clip layout" on the styled path took styled ellipsis to 218.7 (−42 % in total); the same for
  multi-font (`EllipsizeMeasuredLine`) and BiDi should save ~70 KB/step more (estimate, not built): multi-font ~−37 %,
  bidi ellipsis ~−11 %. Dump hash stayed 7A18AF8DE8E05271 with both parts. Retained for the open 5,000-block document
  8.12 → 7.92 MB (−2.5 %, under the retained bar).
  expected: churn bar on styled ellipsis (−42 %) and multi-font ellipsis (−20 % measured, ~−37 % estimated).
  cost: small, internal, no API or pixel change.
- id: syntax-long-line-cap
  what: a very long line is tokenized only on its first P characters, from the line's start state, as a line stopped
  at its budget is today.
  evidence: tokenizing one line is O(n²): each doubling costs ×3.2–3.8 the time, bytes linear (~430–560 B per
  character). With no budget (a headless clock) a 32k-character line takes 17.0 s in JavaScript, 27.1 s in C#, 0.96 s
  in JSON — junction-leads' "30 KB line over 15 s". With the window's 250 ms budget a longer line gets **less**
  colour:

  | JavaScript line | Coloured characters |
  |---|---|
  | 4k | 2,226 |
  | 8k | 804 |
  | 16k | 373 |
  | 32k | 173 |

  A 32k line gets 108 characters in C# and 4,055 in JSON. Each such line still spends its whole 250 ms of worker time
  on every History prefetch that reaches it, and 12 such lines use up the 3 s file budget. Ordinary 70-character lines
  cost the same per line from 500 to 4,000 lines: the problem is only within a line. Likely cause: Onigwrap 1.0.11's
  `ORegex.SafeSearch(ReadOnlySpan, int)` calls `Encoding.Unicode.GetByteCount` twice per regex search, probably over
  the whole line (O(line) per search); TextMateSharp's per-regex cache (`OnigRegExp._lastSearch*`) skips only repeated
  searches. Without forking the native wrapper, only a shorter `LineText` bounds the cost. Times taken under eight
  parallel surveys; only the ratios matter.
  expected: scaling bar — the O(n²) cost per long line on the worker becomes a bounded t(P). By ratio, P = 2,000 costs
  about a third of the budget in JavaScript and less in JSON. Headless tests with long lines become bounded and
  deterministic.
  cost: small (`LineWalk` in SyntaxHighlighter.cs:441-486). Colour changes: lines over ~4k characters get more colour,
  lines between P and ~3k get less. P may need to depend on the grammar (JSON ~17× cheaper; long minified JSON lines
  are common). → Needs the user.

## WAITING, RE-RANKED

- textarea-chunked-document: keep parked. Since cf7c7d8b it is the largest byte cost of TextArea typing, but it still
  clears no time bar. Meridian's 89k-character body typing went 1,898.9 → 241.5 KB/key; strings are 76.9 % of that
  (~178 KB, the TextArea's new text; same with a reader). The suite's TextArea typing rows are 85–99 % one document
  string (2,000-line 206.5 KB, wrapped 900-line 254.1, undo-redo 204.6, with a reader 309.2). Below 256k characters
  the copy stays in gen 0, a memcpy of ~180 KB, roughly 1–2 % of a 1.3 ms key. Option B also needs Meridian to keep
  its body without making a string per key. The user's 256k-character condition is still unmet.
- richtext-short-viewport-jitter: not perf; code unchanged. RichTextEditorElement.Render.cs:362-367: a caret taller
  than the viewport scrolls to its top on one pass and to its bottom on the next. A one-line fix (prefer the top when
  `caret.Height >= viewport`) whenever someone touches the editor.
- junction-leads, text parts: (a) a renamed file's old side is read at the new path (JunctionApp.Highlight.cs:275,
  `GetFileText(base, path)`), so its deletions stay plain — a small app colour fix (`HighlightAsk` needs the old
  path), not perf. (b) The 30 KB line becomes syntax-long-line-cap.

## EXAMINED, NOT WORTH IT

- **Making the drawn line's second pass cheaper (step 14's lead):** at most +2.6 % time; wheel rows 3.3–4.7 KB/step.
  No bar.
- **BiDi ellipsis and wrap rows:** `GlyphPosition[]` is 30–40 % of them. Already declined: BiDi lines make their runs
  at once; an RTL word cache was declined in round 1.
- **text/create:** `TextElement` is 36–48 % of the row — element size, not text.
- **richtext/open:** a closure (`<>c__DisplayClass4_0`) is 3.5 %, about one per block; source not found in the
  editor's layout or paragraph code; under the bar alone.
- **Tokenize cost against file size:** flat per line from 500 to 4,000 lines; no whole-file quadratic.

## NEEDS THE USER

- **syntax-long-line-cap:** may lines over P characters be coloured only on their first P characters (P about 2,000;
  larger for JSON)? Today a 32k line gets 108–4,055 coloured characters and costs 250 ms of worker time each time, or
  15–27 s headless. [Yes. VS Code goes further: lines over 20,000 characters get no colour at all.]

## FILES

Scratchpad `r7-text\` of session 855f4ddd: probes `probe\` (modes `types`, `dump`, `retain`), `mdrv\` (Meridian walks
`mbigbody`, `mfind`), `longline\` (modes `js,json,cs` and `file`); patches `proto-lines2.patch` (exact line arrays plus
the touched-rows line), `proto-search.patch` (lazy line hits); results `tip-text.txt`, `tip-rich.txt`,
`tip-textarea.txt`, `p1-text.txt`, `p2-ell.txt`, `dump-p2.txt`, `retain-base.txt`, `retain-p2.txt`, `mer-big.txt`,
`mer-big-a11y.txt`, `mer-find-base.txt`, `mer-find-proto.txt`, `p-search-find.txt`, `longline.txt`.
