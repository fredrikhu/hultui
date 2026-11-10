# Round 10 survey, expert 5: text pipeline

SATISFIED: no

Measured at 3462f18e (detached), headless JIT only, bytes and counts only; no window opened; worktree clean. AOT and
Fedora not run.

REVIEW: none.
- **Layout corpora unchanged.** 48,000 layouts hash 7A18AF8DE8E05271 (1,004,690 lines, 22,578,320 glyphs); both
  240,000-layout corpora (300 late, and reference) F09E9038726E4BD6; per-layout hashes 0 differ from round 9's files
  (`hashdiff.py`).
- **Suite rows, round 9 probe `types` mode, 24 steps:** 43 shared text/TextArea/richtext rows within 0.2 KB/step or
  lower (text/resize a wrapped TextArea 15.4 → 14.9, text/wrap 60.7 → 60.0); nothing rose.
- **28ec28fd (BreakByCount):** open bytes as landed: toc.html 329.6 / 181.5 KB cold/warm, pretty 378.1, one line
  276.8, 1 MB bundle 360.0 / 145.9, TODO.md 1,986.7. **The wheel after an open shapes each row once:** first 20 notches
  7.2–22.5 KB/notch, back over the same rows 0.9–2.0, down again 0.8–2.1. **Fallbacks:** a non-ASCII row (é) goes to
  the search at +6 KB (321.2 → 327.4); a joining character is the cliff below (it predates 28ec28fd but is now 98 % of
  such an open).
- **3a08f828/b4f182b5 (label cull):** the clip check precedes `line.Runs`; one-line labels read no clip;
  `LocalClipBounds` once a multi-line label a frame, 0 B. Loop note under EXAMINED.
- **ae32c9d8/bf74fbca:** HultUI.Syntax's own share of `TokenizeLines` is 3.3 % JSON (92 B a line), 8.2 % XML (303 B),
  2.0 % HTML (265 B), nearly all the span arrays it returns (1.9 / 6.6 / 1.6 %); TokenizeLine2 is the rest (2.61 /
  3.29 / 12.87 KB a line). bf74fbca's own registries keep +378 KB (PowerShell), +680 KB (Less), cleared on `SetTheme`:
  fine.
- **0ef0b61f:** app side only; a small body character 3.8 KB as landed. Other range commits touch no text path (Sync's
  faded colours don't key text layout).

CANDIDATES:
- id: text-textarea-cluster-walk-whole-line
  what: When a wrapped line holds one joining character (emoji, combining mark, ZWJ, any surrogate), `WrapLine` walks
  the grapheme clusters of the whole hard line and lists every cluster end, even when only one row is searched.
  evidence:
  - `ClusterEnds(start, hardEnd)` runs before the row loop (TextAreaElement.cs:818). It makes one `List<int>` entry per
    character, grown by doubling, and drops it above 4,096 (:646), so it is rebuilt at every wrap. One
    `GetNextTextElementLength` call per character.
  - **Typing** (probe rows, one character a step into a one-line 89k JSON body), KB a key:

    | Face | ASCII | é (no walk) | one emoji | one combining mark |
    |---|---|---|---|---|
    | Consolas | 191.3 | 191.3 | 1,216.8 | 1,215.7 |
    | Segoe UI | 196.7 | 196.7 | 1,223.3 | 1,221.1 |

    Int32[] is 83–85 % of the emoji rows: the list doubled to 131,072 entries, every key.
  - **Open, cold** (1 MB minified bundle, Cascadia Mono 13):
    - one line 321.2 KB; + é 327.4; + emoji 16,724.9 (52×); + emoji at char 10 16,725.2; + mark 16,711.8
    - the bundle with its own line breaks, one emoji in a long line: 360.0 → 8,560.4
    - toc.html on one line 276.8 → 16,686.7
    - the reference path (`FitsByCount` false) +16.4 MB the same
  - **Real file:** docfx index.json has 3 lines (124,269 / 131,293 / 10,456 characters) with ZWJ and emoji. Their lists
    are about 3.2 MB of its 6,179.8 KB cold open (52 %).
  expected:
  - Churn bar on an app metric: typing into a long one-line body with an emoji −84 % a key (1,217 → ~191); opening such
    a file −98 % (16.7 MB → ~0.33 MB); index.json about −50 %.
  - Scaling bar: per wrap, O(line) cluster calls and 4 B a character become O(rows searched).
  cost: small–medium, internal, no API, rows identical by construction.
  - Find cluster ends only for rows that fall to `FindLineBreak`, from a known boundary before the row and extended as
    the search reads. Any two characters below U+0300 other than CR LF have a boundary between them, so a vectorized
    skip can find one.
  - Prove with the `rows` corpora with an emoji or mark inserted, the current walk as reference.
  - Add a suite row, e.g. "input/type in a one-line JSON body with an emoji".
- id: syntax-markdown-loads-every-grammar
  what: The first Markdown, Ruby, shell or F# file loads every embedded language's grammar (Markdown asks for 69) and
  the highlighter's static registry keeps them all until `SetTheme`.
  evidence: one process per grammar, a 3-line file after a JSON warm-up, full GC (`grammars.txt`).
  - **Kept, first file:** .md +29,444 KB; .rb +29,316; .sh +29,180; .fs +29,225 (each 49 raw grammars loaded); .jl
    +9,183; .cshtml +4,567; .php +4,449; .html +3,078. C#, JS, TS and Python keep +1.3–1.6 MB; most others under 1 MB.
  - **Allocated:** that first .md file allocates 82,868 KB (on a worker); the second call 10 KB.
  - **TextMateSharp alone, Markdown:** with no embedded grammars handed out +1,335 KB; with 18 common fence languages
    +17,015 KB (C/C++ are heavy); all +29,689 KB.
  - **Who reaches it:** any .md file in Junction Files (TokenizeLines), diffs (TokenizeDiff) and its Markdown preview
    (SpansForLanguage), and in Vesper's preview (SpansForText).
  expected: retained-memory bar. About −28 MB (−95 % of what a Markdown file keeps) for a text with no fences, which is
  far more than 5 % of any app's managed heap. The saving shrinks with each fence language a text uses.
  cost: medium–large, no API.
  - The exact route keeps colours: scan the text's fence info strings and HTML blocks, and load a registry with only
    those grammars. It needs the grammar's fence names mirrored, and a corpus check (round 7 step 12's corpus) that
    tokens are equal.
  - The cheaper routes change colours or behaviour: see NEEDS THE USER.
  - No TextMateSharp fork (step 14's rule).

WAITING, RE-RANKED:
- textarea-chunked-document: keep parked; the 256k-character condition is still unmet (no new typed text is that
  large).
  - Typing rows unchanged: 206.5, 254.1, 204.6, reader 309.2 KB/key; 30-word probe rows 753.0 / 825.6. System.String is
    78–97 %.
  - Meridian's 89k body was 238.8 KB/key in round 9's walk, with 51.4 KB of it the build that 0ef0b61f removes. About
    187 KB/key is expected now, ~93 % the string. That is derived, not re-run: the walk host is expert 8's.
  - The big per-key cost on a one-line body with an emoji is the cluster walk (candidate above), not the string.
- text-open-measures-every-line (full): the user's "not now" stands. Current numbers: text/open a long document 35.2
  KB/step, TODO.md cold 1,986.7 KB, both unchanged.

EXAMINED, NOT WORTH IT:
- **The label cull loop is still O(lines) a frame:** 2,000 line checks for ~52 drawn on the new wheel row, each an
  interface indexer, two reads and a compare, 0 B. A binary search would make it ~63. That is a few µs of a 0.24 ms
  frame by instruction count, under the time bar. A 20k-line sent body would only scale it.
- **HultUI.Syntax's per-line share after ae32c9d8:** 2.0–8.2 %, almost all the span arrays it must return;
  TextMateSharp is 92–98 %.
- **`TextPaintBounds` reads every line's runs and blobs:** only for a text in a faded group outside a scroll view.
  `SubtreePaintBounds` stops at scroll views (ElementRenderer.cs:174), so long labels never reach it.
- **bf74fbca's second registries:** +378 / +680 KB once per theme, Less loading CSS again.

NEEDS THE USER:
- syntax-markdown-loads-every-grammar: is a cheaper route acceptable?
  - (a) Drop the registry after an idle minute. Colours are kept; the next file pays the load again (83 MB allocated
    on a worker, and colour arrives later).
  - (b) Limit Markdown's fence languages. Fences in other languages draw uncoloured (a colour change). The 18 common
    ones still keep 17 MB unless C/C++ are left out.
  - (c) The exact route: load per text only the fenced languages it names. No colour change; medium–large.
  - Before/after images for (b): render one Markdown file with a fence per bundled language with both registries,
    using `SaveFrame` on the headless host.
- Questions 10 (`TokenizeDiff` public) and 26 (merged VS Code theme, for later) stay the user's.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\3484c586-c631-4af0-957d-95e7a03cac41\scratchpad\r10-text\`
- **Text probe** (`probe\`, round 9's r9b6 probe pointed at this worktree):
  - added an `fb <file>` mode (fallback opens) and the probe rows "type in a one-line 89k … TextArea"
  - the wheel lines now also print back/again
- **Tokenize probe** (`tok\`, r9b14c's probe): added `split2`, `rooted`, `one <file>`, `trim <scope> <list>`.
- **Layout dumps:** `dumps.ps1`, `dumps.txt`, `h48.txt`, `h240late.txt`, `h240ref.txt`.
- **Suite rows:** `rows.ps1`, `tip-text.txt`, `tip-textarea.txt`, `cmprows.py`.
- **Open and fallback:** `open.ps1`, `open-bytes.txt`, `fallback.txt`, `joiners.py`, `joiners.txt`.
- **Typing:** `type-oneline.txt`, `type-oneline-eacute.txt`.
- **Syntax:** `tok-split2.txt`, `tok-rooted.txt`, `grammars.ps1`, `grammars.txt`, `trim.txt`.
- **Corpus copies:** `corpus\` (docfx files as of today: toc.html, ELEMENTS.html and index.json were regenerated since
  round 9; the bundle and TODO.md are unchanged).
