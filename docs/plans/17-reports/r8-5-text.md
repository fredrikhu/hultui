# Round 8 survey, expert 5: Text pipeline

SATISFIED: no

Measured at 27020cdb (detached), headless only. I report bytes, counts and complexity, no timings. Probe: round 7's `types` probe, rebuilt against this worktree. Each run is 8 warm-up steps, then 24 measured steps, with allocations by type from GCAllocationTick. Ticks arrive late, so a small row's type split can include the tail of the row before it; the totals are exact. Three prototypes were applied, measured and reverted with `git apply -R` run from the worktree; the worktree is clean. Linux and AOT were not checked.

REVIEW: none.
- **No text row rose against round 7.** Figures are KB/step, round 7 survey → now:
  - text/wrap 66.6 → 60.7
  - styled ellipsis 374.8 → 218.7
  - multi-font ellipsis 422.6 → 266.4
  - bidi ellipsis 1,300.3 → 1,159.3
  - richtext/open 3,476.6 → 3,256.7
  - richtext/resize 700.4 → 656.2
  - find typing 54.1 → 24.9
  - text/open highlighted JSON in a TextArea 259.4 → 77.3
  - 600-span typing 327.7 → 326.2
  - Every TextArea typing and reader row is the same to 0.1 KB (2,000-line 206.5, wrapped 900-line 254.1, undo-redo 204.6, with a reader 309.2).
- **Layout dump unchanged.** The 48,000-layout hash is still 7A18AF8DE8E05271 at the tip (1,004,690 lines, 22,578,320 glyphs).
- **45628641, d748b316, 73819ebd, 49509afd, 4fcc9663, 18fe58a0:** no objection.
  - 49509afd slices `ReadOnlyMemory` and adds no per-line work for lines under P.
  - 73819ebd clears its kept line list and caps it at 1,024 lines.
- **f64e4674 (app-support), text parts:**
  - caf2e17f `GrowsWithContent` is off by default; a non-growing area pays one field read in `InvalidateLines` and one virtual call to the base measure. No app or sample uses it.
  - 0a47dded's `Select` and `SetValueFromAssistiveTool` are not on a hot path.
  - 94d27324's halo text makes an SKPath on each halo draw, but only `ICanvasDrawingContext` uses it, and no app does.
  - The merge has no RichText changes.

CANDIDATES:
- id: text-richtext-paragraph-shares-lines
  what: Four edits that stop each rich text block from making objects it does not need.
    - A rich text paragraph keeps the layout's exact `TextLine[]` when it adds no empty line, instead of copying it.
    - It makes its `TextLineBox[]` the first time `Lines` is read.
    - The editor's `SpansOf` returns an exact `TextSpan[]` instead of a `List`.
    - `RichTable.Normalize` stops making a closure for every block.
  evidence:
    - Shares of tip richtext/open (3,256.7 KB/step): TextLineBox[] 9.4 %, TextLine[] 8.7 % (the layout's array plus the paragraph's copy, PipelineTextParagraph.cs:35-47), List<TextSpan> 3.5 % (RichTextEditorElement.Layout.cs:105), `<>c__DisplayClass4_0` 3.8 %.
    - Reflection over the closure classes shows the `<>c__DisplayClass4_0` is `RichTable.Normalize`'s capture of `columns` (RichTable.cs:97-98, `rows.All(r => r.Count == columns)`). The compiler allocates it on entry to the while body, so once per block, tables or not. This settles round 7's "source not found".
    - Without a reader, `Lines` is read only for the caret's block, a selection, find hits and drawn list markers. A reader reads every paragraph's `Lines` (RichTextEditorElement.Accessibility.cs:357), so those boxes are still made, as today.
    - Prototype `proto-para2.patch`, KB/step:

      | Row | Before | After | Change |
      |---|---|---|---|
      | richtext/open 5,000 blocks | 3,256.7 | 2,589.2 | −20.5 % |
      | richtext/resize 1,000 blocks | 656.2 | 543.8 | −17.1 % |

      The first three edits alone give −17.0 % on open; `Normalize` adds the rest. Every other richtext/ and text/spans row stays within 0.7 KB.
    - Retained heap with the 5,000-block document open: 7.94 → 7.41 MB (−6.7 %); closed 3.40 MB both.
    - Tests: Skia 357/357, Headless rich text, a11y and golden subsets 392 + 75 pass, Core "Rich" 134 pass.
    - `ParagraphAllocationTests.AParagraph_AllocatesItsTwoArraysAndAFewObjects` fails as intended: it pins 28 B a line, and the prototype makes 0 B a line once the layout is cached. The build rewrites it.
  expected: churn bar on two suite rows (−20.5 % and −17.1 %) and the retained bar (−6.7 %).
  cost: small; internal, no API or pixel change. `TextLines` may now return the cache's own array, which is read-only by contract.

- id: text-richtext-block-layout-slim
  what: The editor's private `BlockLayout` moves its table and picture fields into a side object made only for cells and pictures, and takes a text block's style from its paragraph instead of keeping a second copy.
  evidence:
    - After the first candidate, `BlockLayout` (~150 B) is 26.3 % of richtext/open and 35.3 % of resize.
    - Its fields: a 40-B `ResolvedInheritedStyle` that duplicates `Paragraph.Style`, plus 53 B of table and picture fields that most blocks never use (RichTextEditorElement.Layout.cs:140-176).
    - Prototype `proto-para3.patch` (both candidates; this part is the `BlockLayout` hunk) uses properties over a lazy `BlockExtra`. Over the first candidate:
      - open 2,589.2 → 2,234.1 KB/step (−13.7 %)
      - resize 543.8 → 473.5 (−12.9 %)
      - retained open 7.41 → 7.06 MB (−4.7 %)
    - Both candidates together: open −31.4 %, resize −27.8 %, retained −11.1 %.
    - The same test subsets pass; goldens are among them.
  expected: churn bar on open and resize (−13…−14 % each on top of the first candidate).
  cost: small to medium; one private class (~25 lines). Cells and their kept paragraphs keep the side object, because a cell's paragraph is set after its style.

- id: text-richtext-block-list-no-copy
  what: The editor keeps its block layouts in a `List` and shifts them in place when an edit adds or removes blocks, instead of making a new `BlockLayout[]` for every change in block count.
  evidence:
    - Relayout (RichTextEditorElement.Layout.cs:283-287) makes a new array whenever the block count changes, so Enter or a merge allocates 8 B × blocks (40 KB at 5,000 blocks).
    - Prototype `proto-blocklist.patch`, tested alone on the tip: `CollectionsMarshal.SetCount`/`AsSpan`, `LayOut` and `BuildTable` filling a `Span`, `.Length` → `.Count` at about 20 sites in four editor files.
      - richtext/enter in 5,000 blocks: 82.0 → 42.9 KB/step (−47.7 %)
      - resize −1.2 %; every other row is flat.
    - Headless subset 404 and Core "Rich" 134 pass.
    - What remains of the 42.9 KB is mostly the document's own `RichBlock[]` copy. That array sits behind the public, immutable `RichDocument.Blocks`, and copying it was already declined.
  expected: churn bar on richtext/enter (−48 %). The edit still memmoves O(n) block references, but allocates 0 B for them.
  cost: small to medium; internal. An edit touches four partial files.

- App reach of these three: Junction's Markdown editors (MR description, review comments) hold small documents, so the app gain is small. The wins are on the suite's long-document rows and in retained memory.

WAITING, RE-RANKED:
- textarea-chunked-document: keep parked; the user's condition is still unmet.
  - The suite's typing rows are unchanged since round 7 (206.5, 254.1, 204.6, reader 309.2 KB/key), and the document string is 79–95 % of each.
  - The probe rows with 30-word lines (~400k characters) are 753–826 KB/key, but they are synthetic. No app has an editable document over 256k characters.
- text-open-measures-every-line (full): no new evidence, so the user's "not now" stands. text/open a long document in a TextArea is unchanged at 35.2 KB/step.
- richtext-short-viewport-jitter: not perf.
  - The code is unchanged (RichTextEditorElement.Render.cs:362-367).
  - It is a one-line fix, preferring the top when `caret.Height >= viewport`.
  - It could go with whichever rich text step touches the editor; it changes behaviour only for a caret taller than the viewport.
- Question 10 (`TokenizeDiff` public) stays the user's.

EXAMINED, NOT WORTH IT:
- **text/open highlighted JSON (77.3 KB/step, 95 % `Int32[]`):** this is d748b316's touched-rows list, one 76 KB array for each replacement of a 19,000-line text.
  - Keeping it, by swapping it with the line-start list, would hold +76 KB per large TextArea in exchange for −76 KB per replacement.
  - That trades retained memory for churn and saves Meridian only −6.7 % per send (76 of 1,140 KB): under the bar.
- **A long-line P for each grammar (round 7's Left open):** DECISIONS.md already declined it.
  - In a window a long SQL, CSS, PowerShell, C++ or Python line still stops at its 250 ms budget, so the time is already bounded.
  - Only headless tests would gain (SQL takes 849 ms on 2,000 characters). No frame bar.
- **richtext/type in a paragraph of 600 spans (326 KB/key):** GlyphPosition[] 25.5 %, String 23.2 % (the public `ShapedRun.Text`), RichSpan[] 16.5 % (the immutable public model), ShapedRun 15.5 %. The rest needs incremental relayout of one paragraph, which round 1 declined.
- **TextArea typing with a reader (+103 KB/key over no reader):** document strings and row x positions. Round 5 already landed text-textarea-a11y-row-xs; the rest is expert 7's.
- **text/create:** TextElement is 39–48 % (element size).
  - Emoji labels cost +35 % over plain ones because surrogates go the BiDi path (`MayNeedBidi`, TextPipeline.cs:968).
  - Widening the plain path needs cluster-aware breaks; round 1 declined that, and there is no new evidence.
- **multi-font ellipsis:** `LineSource` is 25.7 % of 266 KB. Lazy runs need one per layout.
- **richtext/open TextLine and TextLayout (~22 %):** each step mounts a new editor and misses the layout cache. A real first open shapes anyway.

NEEDS THE USER:
- none new. Question 10 stays open.

FILES: all in scratchpad folder `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\64e7e0e2-bc1c-4069-9d42-9311fee04c9e\scratchpad\r8-text\`
- Probe: `probe\`, with modes `types`, `dump`, `retain` and a new `closures` mode (finds the declaring type of a compiler closure). `mdrv\` and `longline\` are copied from round 7 and were not run.
- Tip results: `tip-text.txt`, `tip-textarea.txt`, `dump-tip.txt`, `retain-base.txt`.
- Prototype results: `p1-rich.txt`, `p1-spans.txt` (first three edits), `p2-rich.txt`, `retain-p2.txt` (first candidate), `p3-rich.txt`, `retain-p3.txt` (both candidates), `p4-rich.txt` (block list).
- Patches: `proto-para.patch` (first three edits), `proto-para2.patch` (first candidate), `proto-para3.patch` (first and second candidates), `proto-blocklist.patch` (third candidate, alone on the tip). `proto-final-check.patch` is the same as `proto-para3.patch`.
- Test logs: `test-*.txt`.
