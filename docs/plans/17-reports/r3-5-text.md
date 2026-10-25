# Round 3 survey: 5. Text pipeline

Probes and prototypes on ac5d415, reverted; worktree clean.

SATISFIED: no

REVIEW: none. Full suite allocKB unchanged vs the last round 2 capture on every row (text/create
4,203; ellipsis 1,857; JSON open 1,903; JSON wheel 8; rebuild around JSON 9; type in 2,000 lines
222; wrapped 900-line 326; undo-redo 233; richtext/open 11,227). TextBench warm rows 0 B. Hit
rates healthy (text/wrap word 527,955/40; ellipsis shape 25,055/0; bidi memo 25,000/0; JSON open
width cache 76 %; richtext memo 99.8 %). deadb3f holds (0 line-start passes on rebuild); typing
measures 1 line and carries 1,999 widths. Retained after close matches 46a1219. 12d9742/5943f4f
hand spans with the same text instance. MaxLines/Overflow setters return early on equal values.
Shaper 176 B per call.

## Candidates

- **text-prefix-table-scratch** (new). BuildPrefixWidths reuses one kept segment list and font-run list (as MeasureString does) and copies the exact array out, instead of new `List<PrefixSegment>` and `List<FontRun>` per table (TextPipeline.cs:1620-1622, :1647). Prototype: text/open a long document 3,418 → 1,377 KB/op (−60 %); text/wrap resize 1,531 → 1,367 (−11 %); wrapped 900-line typing 326 → 292 (−10 %); undo-redo 233 → 225; TextBench resize/ellipsis-2lines −36 %; a 20,000-line wrapped log open 207.7 → 75.2 MB (−64 %). Identical output by construction. Small.
- **text-textarea-incremental-wrap** (re-ranked up, widened). Carry each hard line's breaks by line index (like CarryLineWidths, TextAreaElement.cs:504) instead of a new Dictionary per pass keyed by a copy of every wrapped line's text (:44, :415, :434); patch line starts around the edit (:400). Retained: a fully wrapped 20,000-line log holds 12.13 MB beyond its 6.2 MB text, 8.19 MB of it the wrap memo (a second copy of the document). Line-start pass share of a keystroke: 2,000 lines 8 %, wrapped 900 12 %, undo-redo 9 %, 19k JSON 27 % (263 of 983 KB), log 28 % (2.4 of 8.6 MB). Retained −8.2 MB; churn −27/−28 % on JSON and log, −8…−12 % on the rows; O(lines) → O(edited). Medium. Proof: random edits vs a fresh area incl. surrogates, ZWJ, CJK; re-run fc00b75's 2.0 M-line audit.
- **text-memo-cache-byte-bound** (measured worst case). Weight BiDi/styled memos and remembered lines, bound by bytes (~16 MB); today count-bounded 4,096 / 16,384 / 4,096 (TextPipeline.cs:933-936). Worst case: 4,096+ mixed Hebrew/Latin 2,000-char paragraphs leave 64.7 MB (~58 MB memos; keys keep 8.2 M chars of dropped text alive); styled LTR 45.2 MB; RTL 400-char 32.8 MB; emoji route text here too. Realistic: 15.2 MB after the 5,000-block rich document opened twice and resized. Worst case 58 → ≤ 16 MB; realistic and suite rows unchanged if the bound sits above 15.2 MB. Small–medium.
- **apps-tokenize-on-ui-thread** (re-ranked up: lock contention). Junction's markdown code fences tokenize in Build (JunctionApp.Markdown.cs:119, :496) under SyntaxHighlighter's single lock (:149, :201); Junction's diff and Files workers hold the same lock for whole files (JunctionApp.Highlight.cs:95-96, JunctionApp.Files.cs:527) with a 3 s budget each (SyntaxHighlighter.cs:97). A fence built while a worker tokenizes makes the UI thread wait up to that budget; the first fence also loads the registry and grammar on the UI thread. Small–medium.
- **linux-emoji-colour-fallback** (correctness, also Windows). Emoji-presentation characters or a character + VS16 should pick the platform colour emoji face. Windows: most emoji already use Segoe UI Emoji, but #️⃣, ™️, ©️ stay monochrome in Segoe UI — the primary face has the base character and is checked first (FontItemizer.cs:53), VS16 only extends the run (:92), Common script gets no language (ScriptLanguage.cs:67) so MatchCharacter never asks for und-Zsye (SkiaFontCollection.cs:183, :192). Linux: 😀 and ❤️ fall back to DejaVu Sans. Medium; changes pixels on Linux and for VS16 keycap/™/© on Windows. Proof: WSL with real Noto faces + Windows list; goldens unaffected (pinned faces).
- **tooling-textbench-warmup** (cause found). Warm-up passes call cold rows with the same indices (samples/HultUI.Samples.TextBench/Program.cs:51), so from pass 2 cold rows hit warm caches and never tier up: default 1,293 methods compiled before measuring, +1,068 during. Fresh indices per pass from a reserved range + a "methods compiled during measurement" health line. Small.
- **paint-drawspannedline-overlap** (latent, shared with 2): only reachable by a multi-line DrawText/paragraph with overlapping spans; no such caller found.

## Examined, not worth it

- TextArea document as one string (Replace rebuilds it: 203 KB per key at 2,000 lines, 6.3 MB for the log, LOH above 42,500 chars): a rope/piece table changes the public `Text` contract — large.
- Fitting tails via MeasureTextWidth instead of prefix tables: on top of the kept list, text/open only −3 %.
- Width cache under unique documents: bounded at 4 MB by design.
- richtext/open layout-cache misses (4,065 per re-open): served by memos and the word cache; a larger bound trades retained memory.
- input/type in TextInput: no text work left (604 shape hits, 0 misses per key).
- Command palette filter: 66 shape misses per key from highlighted pieces; small.

## Needs the user

None.
