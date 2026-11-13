SATISFIED: no

I measured at 2ced4e26 (detached, SDK 11.0.100) on NativeAOT, headless and in real windows. I used round 12's probe counters, re-derived for the shipped code, plus new counters for who asks HarfBuzz and why. Both the probe and the prototype were applied by patch and then reverse-applied. The worktree is clean and the build servers are shut down.

The fixtures are real text, not generated characters. I built them from installed gettext translations (msys64 coreutils/gnupg/util-linux/gettext `ja`, and git/coreutils/bash `zh_CN`), grouped into README paragraphs:
- **`README.ja.md`:** 79,011 characters, 999 lines, 820 distinct, 39k kana, 12.8k han.
- **`README.zh-CN.md`:** 75,265 characters, 779 lines, 972 distinct, 42.8k han.
Junction shows each one in the Files view. Round 12's fixture ("synth") is the control.

REVIEW: none against the code of ce6f4962 or 7a753337. One objection to 34dbc7fc's note in DECISIONS.md.
- **Corpora unchanged at 2ced4e26:** 48k 7A18AF8DE8E05271, 240k late F09E9038726E4BD6, CJK wrap widths 1E6F1A93894D0B6A / rows 6C7FA51792E8EB84. 0 of 1,344,616 widths differ from the reference path; 403,333 are read off cuts.
- **ce6f4962 on real text:** the table answers more often than on the fixture.
  - Japanese: 99.6 % of 113,672 questions at 1000 px; 99.5 % of 3.13 M over the 40-frame drag.
  - Chinese: 98.5 % at 1000 px; 98.4 % of 3.16 M over the drag.
  - Synth: 95.8 % at 1000 px; 94.5 % over the drag.
  - Misses are nearly all slot collisions (Chinese drag: 52,094); Han-context misses are 138 (Japanese drag).
  - Real windows: 99.6 % (ja) and 98.7 % (zh).
- **7a753337, what is left at a new width:** a new width shapes 0.3–0.4× the document on real text, not 1.3×. The ~1.3× held only for the fixture, and after 7a753337 the fixture is at 0.94×. Headless at 1000 px:
  - Japanese: 2,086 HarfBuzz calls / 31,570 characters, of which:
    - 1,068 are probe cuts shaped: 660 on words cached without cuts, 312 on words under 8 characters, 94 refused as unsafe;
    - 705 are table words cut by the table's end;
    - 298 are row-start words.
  - Chinese: 3,231 calls / 20,753 characters. 1,170 of them re-shape whole words the word cache evicted: at 1000 px every miss evicts an entry (it holds 8,192).
- **Objection to 34dbc7fc:** it dropped "cuts at first need" for saving 7 of 11,223 calls. That was measured only on the synthetic fixture, where the variant is still worth only −2 %. On real text it is the largest remaining share → candidate below.

CANDIDATES:
- id: text-cjk-cuts-at-need
  what: Two changes to the prefix tables' word shaping.
    - (a) A word cut short that has no cuts yet (shaped by a line width or a drawn row, or under 8 characters) gets its cuts at the first cut. That cut and every later one are read off them.
    - (b) A table's last word cut by the table's end is read off the word to its natural end (next space or line end, at most 1,024 characters on), shaped once with cuts. This applies only when the word starts somewhere that is the same at every width: a line start, after a space, or a font-run boundary.
  evidence: Headless, AOT, Junction Files view, shipped → (a) → (a)+(b). HarfBuzz calls (UI KB):
    - Japanese:
      - 1440 → 1200: 1,233 → 932 → 720 (764 → 649);
      - → 1000: 2,086 → 1,511 → 1,051, −50 % (1,002 → 755);
      - → 1320: 670 → 444 → 261;
      - back to 1440: 443 → 0;
      - 40-frame drag: 1,764 → 1,201 → 453, −74 % (1,440 → 764, −47 %).
    - Chinese:
      - → 1000: 3,231 → 1,426 → 1,165, −64 % (894 → 625);
      - → 1320: 1,338 → 330;
      - back to 1440: 597 → 0;
      - drag: 2,084 → 1,054 → 403, −81 % (954 → 635).
      - The first new width (1200) costs +190 KB: (b)'s natural-end words are made once.
    - The cliff, at 960 logical px (a 1,440 px window on the user's 150 % display):
      - Chinese drag: 273,751 calls / 82,028 KB over 37 frames (7,400 calls and 2.2 MB a frame). Every miss evicts a word-cache entry, and 182,508 calls re-shape whole words. With (a): 6,058 / 2,027 KB. With (a)+(b): 2,389 / 1,308 KB (−99 % / −98 %).
      - Japanese drag: 8,869 → 3,416 calls, 4,955 → 2,650 KB.
      - Before 7a753337 (cuts off): Chinese 418,560 calls / 97 MB, Japanese 390,245 / 136 MB. So it is not a round 12 regression: the 8,192-entry cache's cliff was already there.
    - Opening a file changes little: 5,170 → 4,963 calls (ja), 7,150 → 7,004 (zh).
    - Bit identity with (a)+(b): CJK wrap corpus 0 of 1,344,616 widths differ (read off cuts 403,333 → 722,201), rows hash unchanged; 48k and 240k unchanged. Rendering.Skia 405 and Text 302 tests pass.
    - Latin control (TODO.md): 1000 px 137 → 103 calls, +13 KB; drag 475 → 372.
    - The synthetic fixture moves only −2 %: it hides the effect.
  real app: Junction's Files view and other TextAreas (commit message, conflicts, rebase; Vesper's viewer; Meridian bodies) showing CJK, when the user resizes, maximizes, snaps or drags a pane. Real window on the 4K at 150 %: a 40-step drag of Junction on the real Chinese README took 277,414 calls and 82.8 MB with the shipped code, and 2,096 calls and 1.1 MB with the prototype.
  expected: scaling (no per-frame re-shaping once a document's per-width words pass 8,192 cache entries) and memory churn (−25…−98 % UI KB a resize step or drag frame on CJK text). HarfBuzz calls drop 50–99 % per new width or drag frame. At round 12's measured ~1.3 µs a call, that is likely the time bar too: confirm in the build phase.
  cost: small, ~45 lines in TextPipeline.cs; no API, no pixel change (exact by RunCuts' existing proof). `proto-cuts-at-need.patch` applies to 2ced4e26, and R13_CUTSATNEED=0 turns it off.
- id: text-cjk-row-start-suffix (lead, not prototyped)
  what: read a row-start word (the slice from the row start) off the line's word, as 34dbc7fc describes.
  evidence: after the candidate it is the largest remaining HarfBuzz source.
    - Japanese: 300 of 1,051 calls at 1000 px; 216 of 453 in a drag. 66 % start with the same script class as their run.
    - Chinese: 201 of 1,165; 77 of 403 in a drag. 100 % same script.
  real app: the same views as above, at every new width.
  expected: up to another ~20–30 % of a new width's calls.
  cost: medium–large. Needs the slice's own script guess, safe cuts at both ends, and advances re-summed from the cut (+4 B a glyph).

REAL WINDOW:
- **Setup:** the 4K at 150 %, 120 Hz, HDR on, GL (RTX 4080, driver 610.88). Junction on AOT with my probe host, a window of 2,160×1,350 px. Resizes ran on the UI thread; the drag was 16 ms apart.
- **Launches and holds:** 6 launches in 4 holds.
  - r13a: waited 323 s, held 92.7 s, 0 launches (the idle gate timed out).
  - r13b: waited 477 s, held 0.0 s, 0 launches (an argument-quoting bug; no process started).
  - r13c: waited 223 s, held 68.8 s, 4 launches (two shared a label, so only the prototype logs were kept).
  - r13d: held 34.9 s, 2 launches.
  - Every launch: 0 foreground takes, 0 refused activations, WM_CLOSE exit 0, 0 nvlddmkm events, no screen reads.
- **What the real window showed:** the logical width. On the 150 % display, the steps to 1,200 / 1,000 / 1,320 px are 800 / 760 / 880 logical px. There Junction draws a layout without the file text (21 texts, 0 tables). The text work happens at 1,440 px = 960 logical, which the 1440p-at-100 % runs of round 12 never reached.
- **At 960 logical:**
  - Chinese drag: 7,498 calls and 2.24 MB a frame; with the prototype, 57 calls and 30 KB.
  - Japanese drag: 8,095 calls / 4,228 KB vs 2,545 / 2,007.
  - The headless run at 960 reproduces both, so the cliff depends on width, not on the real window.
- **Same as headless:** the faces (Yu Gothic UI and Microsoft YaHei UI for both documents; every stream memory-mapped), the front table's hit rate, and 0 word-cache evictions at open. Opening a file drew 3 frames in the real window against 1 headless, with the same shaping.

WAITING, RE-RANKED:
- **textarea-chunked-document:** keep parked. Round 12 changed nothing in TextArea storage, and the user's 256k-character condition is still unmet.
- **text-open-measures-every-line (full):** the user's "not now" stands; the case is stronger with real CJK text. A 1 px drag step re-breaks every wrapped row of the document.
  - Japanese at 1440: per frame, 1,040 prefix tables, 119,899 characters itemized, 84,495 fallback questions, 20,462 word lookups.
  - Real window at 960 logical: 2,618 tables, 166,806 characters, 117,766 questions a frame.
  - Chinese at 1440: 1,143 tables and 131,412 characters a frame.
  - About 40 rows are on screen. text-cjk-cuts-at-need removes the shaping and bytes, not this O(document) itemizing.
- **syntax-shared-registry-rest:** keep parked. Round 12 touched no syntax code, and the user said there are no Typst files.

EXAMINED, NOT WORTH IT:
- **A bigger or 2-way front table:** misses are 0.5 % (ja) and 1.6 % (zh) of drag questions, about 1,400 a frame on the keyed path.
- **Ranking CJK work on the round 12 fixture:** the cut-at-need variants move it only −2 %. Its 11k new words per width overflow the 8,192-entry cache, so every miss evicts and that thrash is the fixture's own. Use the real ja/zh fixtures from now on.

NEEDS THE USER: none.

FILES: all in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\17c11f5e-9de0-4477-8f58-0d0a5e006605\scratchpad\r13-text\
- **Patches:**
  - `proto-cuts-at-need.patch`: the clean candidate.
  - `r13-probe-and-proto.patch`: counters plus the R13_PROTO prototype levels 1–3.
  - `probe-only.diff`: round 12's counters re-derived for the shipped code.
- **Host and scripts:**
  - `host\` (Program.cs; publishes in aot, aot2, aot3).
  - `run.ps1` (headless), `batch.ps1` (guarded real windows, the lock, `-NoGate`), `aotpub.ps1`, `sum13.py`, `cmp13.py`.
  - `holds.log`, `batch-r13a.log` … `batch-r13d.log`.
- **Fixtures:** `mkreal.py` builds `fx\realja` and `fx\realzh` (git repositories).
- **Results:** `runs\`
  - `h1`/`h2`: shipped code;
  - `h3p0`–`h3p3`: prototype levels;
  - `h4*-todo`: Latin control;
  - `h5w960*`: 960-wide runs, including `nocuts`;
  - `r3-*-p3-real`, `r4-*-p0-real`: real windows.
- **Corpora:**
  - `corpus\`, `cjkwrap\`;
  - `c48-clean.txt`, `c240-clean.txt`, `cjkwrap-clean.txt`;
  - `cjkwrap-p0..p3.txt`, `cproto.txt`;
  - test logs `test-skia-proto.log`, `test-text-proto.log`.
