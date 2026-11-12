SATISFIED: no

I measured at f9c44628 (detached, SDK 11.0.100) with temporary probe counters in HultUI.Text and Rendering.Skia, headless and in real windows (NativeAOT). Both are reverse-applied now, the worktree is clean, the build servers are shut down, and VSStandardCollectorService150 was already stopped. Timings come only from micro-probes and are indicative: min of runs, other surveys running.

REVIEW: none.
- **7d823b34 `WordCache`:** sound. Chains, free list, LRU unlink and grow all check out, removed entries let go of their word, font and run, and there are no locks (the pipeline is single-threaded by contract, as `LruCache` was).
- **6274c26c `Extends`:** the `< 0x300` arm is exact.
- **91e5559c (merged theme):** builds once per registry and adds nothing per token.
- **7f6ade87 (`TokenizeDiff` public):** no perf effect.
- **0ed79e8a (`TextSearch.Version`):** the count label is rewritten only when the version moved, so one re-layout per change.
- **Corpora on the tip, probe build:** 48k hash 7A18AF8DE8E05271.

CANDIDATES:
- id: text-fallback-front-cache
  what: `SkiaFontCollection.MatchCharacter` (SkiaFontCollection.cs:177) works out the script's language through ICU, then looks up a record key holding a string. It does this for every character the primary face lacks, on every itemize. A 4,096-slot struct table answers a repeat in a few compares, before the language is worked out. It is keyed by code point, weight, slant, the paragraph's Han context and the UI culture, which is everything the answer depends on.
  evidence:
  - **Micro-probe** (JIT, min of 7×100k, counters compiled out): a hit 93–96 → 12.2–12.9 ns. An OS miss is unchanged at 4.1–4.5 µs and 275 B.
  - **Prefix tables** (what FindLineBreak asks for) over 300 lines of a Japanese document, 20,311 chars, 3 alternated pairs: min 4.542 → 2.977 ms (−34 %), the same 249.7 KB.
  - **Control:** TODO.md, 300 lines, 1.42 ms on both sides.
  - **Corpora with the prototype:** 48k 7A18AF8DE8E05271 and 240k late F09E9038726E4BD6, both unchanged.
  - **Junction Japanese scenario, AOT headless:** every shaping, itemize and OS-call counter is identical; the table answers 94.5 % of the drag's lookups.
  - **Allocation:** a first version with a record per slot allocated +2.3 MB over 40 drag frames through collisions; the struct table allocates nothing.
  real app: Junction Files view on a 79,029-char Japanese README (1,200 lines), real window, AOT. At ~83 ns saved a hit (indicative):
  - opening it: 91,626 lookups, 88,734 of them hits (~7.4 ms);
  - one resize 1440 → 1000 px: 114,717 in one frame (~9.5 ms);
  - a held drag at 1 px each 33 ms: 28,659 a frame (~2.4 ms a frame).
  The same applies to any CJK text in a TextArea: Junction files and diffs, Meridian bodies, the editor. Latin text barely pays (TODO.md: 1,764 lookups over a 600-frame drag), and Vesper's 240 CJK names only 576 over 6 PageDowns.
  expected: the time bar on every CJK re-wrap or open (−34 % on the prefix pass), and scaling (per-character work in every itemize).
  cost: small, ~25 lines; no API, no pixels.
  - Retains 4,096 × ~40 B = 160 KB per collection, one per app. It can be made at the first fallback, or use fewer slots.
  - Same one-thread rule as the dictionary it fronts. Clear it in `Register`.
  - Patch `proto-front-cache.patch` applies to f9c44628.
- id: text-cjk-rewrap-partial-shaping (lead, measured, not prototyped)
  what: in text without spaces, a prefix table's segment is the whole font run from the row start. The table's window end and every FindLineBreak probe (TextAreaElement.cs:1168) cut it mid-run, and each cut is shaped as a new string (TextPipeline.cs ~2095). So at each new width a wrapped CJK document is shaped again, about 1.3× its own length.
  evidence: Japanese doc, headless:
  - HarfBuzz calls by resize step: 1440→1200 8,428 (59,149 chars); →1000 14,105 (101,569 chars; 3,952 of them probe cuts, the rest table windows); →1320 4,768.
  - Latin TODO.md, the same steps: 390 / 154 / 44 calls.
  - Real window at 1000 px: 14,513 calls / 104,916 chars.
  real app: Junction Files or diffs of CJK text when the window is maximized, snapped, resized or the pane moves. A 1 px drag is mild (1,162 calls over 37 frames).
  expected: most of the 14k calls per new width. HarfBuzz marks the glyphs where a cut is unsafe (UNSAFE_TO_BREAK; HarfBuzzSharp exposes `GlyphInfo.GlyphFlags`). At any other cluster boundary, the prefix has the same glyphs as the whole run's slice, so its width could be read from the run's pen position instead of shaping again.
  cost: medium, no API.
  - Needs the flags kept on `ShapedRun`.
  - Exclude prefixes whose guessed script differs (a prefix of Common characters only).
  - Bit identity must be proven on the 48k/240k corpora plus a CJK wrap corpus. If it fails, it becomes a pixel question for the user.

REAL WINDOW:
- **Setup:** only the 1440p monitor, scale 1.0, 200.01 Hz, HDR on. GL for Junction, Vesper and Meridian; Vulkan F16 Mailbox for Aperture and Reel.
- **Holds:**
  - r1: waited 317 s, held 79.5 s, 5 launches.
  - r2: waited 222 s, held 34.4 s, 2 launches.
  - In all: 0 foreground takes, 0 refused activations, WM_CLOSE exit 0 every time, 0 nvlddmkm events.
- **Faces are the same as headless.**
  - Segoe UI 400 and 600 (Medium and SemiBold both get the Semibold face), Consolas 400, and 700 for Meridian's SemiBold.
  - Fallback faces: Segoe UI Symbol (TODO.md, Vesper, Meridian), Segoe UI Emoji, and Yu Gothic UI / Microsoft YaHei UI / Microsoft JhengHei UI for CJK.
  - 2–4 resolve misses and 2–5 HarfBuzz fonts to the first frame.
  - Every face's stream is memory-mapped (0.4–20.9 MB files), so the HarfBuzz blobs copy nothing.
- **Shaping to settled is the same:** Junction 681 calls real vs 675 headless.
- **The real window draws more start-up frames,** all from the caches with 0 extra shaping:
  - Junction 14 frames to settled vs 5 headless (2,572 vs 1,590 layouts);
  - Meridian, Aperture and Reel draw 2–3 frames after the first with no text change (headless 0).
  - Some SetWindowPos steps draw 2 frames (headless 1).
  - Not text work; for experts 3 and 8.
- **At rest:** 0 frames and 0 text calls in 3 s, 5.8–9 KB, in both.
- **Drag frames:** the same text work as headless (Junction TODO.md: 1,591 word lookups, 5,954 chars itemized, 15 HarfBuzz calls a frame). Aperture and Reel allocate 5.8 KB a drag frame on the UI thread vs 2.0 headless (the Vulkan resize path, not text).
- **DPI:** the real scale is only 1.0. Headless 1.0 vs 1.5: every text counter is identical in all 6 scenarios (only the UI thread's KB moves, by ±1.4 KB). A DPI change lays out every label again from the shape cache with 0 shaping (Junction 183 layouts, Vesper 397, Meridian 151). The pipeline is scale-free.
- **Posted keys:** PageDown/PageUp posted to Vesper's own HWND without focus are ignored, so the listing counts were taken headlessly.

WAITING, RE-RANKED:
- **textarea-chunked-document:** keep parked. No real typed text comes near 256k characters; round 11's numbers stand (89k body 192–254 KB a key).
- **text-open-measures-every-line (full):** the user's "not now" stands; the case is stronger for CJK.
  - TODO.md (100,290 chars, 250 non-ASCII lines): the open measures 233 widths, makes 2,421 HarfBuzz calls (26,088 chars) and itemizes 31,626 chars; 5,537 KB on the UI thread (AOT headless), 5,661 KB over 4 frames in the real window.
  - The Japanese document: 1,170 widths (every line), 11,294 calls, 91,626 fallback lookups; every new width wraps every wrapped line again.
  - Measuring on-screen lines only would cover about 50 lines.
- **syntax-shared-registry-rest:** keep parked (no Typst); 91e5559c only lowered a further registry 387 → 140 KB.
- **meridian-narrow-editor-fedora-fonts:** pinning the font does not fix it.
  - DejaVu pinned (`HeadlessOptions.Golden()`) fails on Windows too: the chain sub-tab shows 46.6 of 69.8 px.
  - Fedora's UI face is Noto Sans (`fc-match`): 61.6 of 66.2 px. Segoe UI: 445 of 448.
  - `EditorMinWidth` 448 (MeridianApp.cs:97–103) is tuned to Segoe UI.
  - The fix that fits: the floor = the larger of 448 and the sub-tab strip's measured width. The breakpoint moves to about 1,325 px on Fedora; Windows is unchanged.

EXAMINED, NOT WORTH IT:
- **The browser-style "last fallback face if it has the glyph" shortcut:** not exact. Over U+4E00–U+9FFF the OS picks another face for 4,054 of 16,931 ideographs in ja and 4,249 of 16,740 in ko, so pixels would change. The front cache keeps the OS's answers instead.
- **Partial-word shaping in wrapped Latin text during a drag:** 630 HarfBuzz calls over 37 frames; 18 a frame over a 600-frame drag; the word cache is not flushed (16 misses on return).
- **Start-up proofs:** about 440 of Junction's 653 first-frame HarfBuzz calls are 1–2-character digit and mono-ASCII proofs. Once per process, far below 10 ms.
- **OS fallback misses in a CJK listing:** Vesper 71 + 383 calls (first frame + 6 pages), about 0.3 ms a page on its first visit.
- **Meridian's headless DPI change:** 1,479 KB a frame while all 151 layouts hit the cache. Not text; for expert 2.

NEEDS THE USER:
- **meridian-narrow-editor-fedora-fonts:** should the editor's floor come from the measured sub-tab strip? On Linux the response pane would move under the editor about 5 px sooner. The alternative is that the test checks only the Windows face, which hides a real 4.6 px cut on Fedora.

Note on the Japanese fixture: it draws ideographs evenly from U+4E00–U+58C3, so its face switches (and so its shaping counts) are higher than real Japanese text. The per-character counts (fallback lookups, chars itemized) do not depend on this.

FILES: all in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\0bab04ae-e7a9-44cb-b4da-96d849a107f6\scratchpad\r12-text\
- **Probe and prototype patches:**
  - `probe.patch`, `probe2.patch` — the counters.
  - `probe-and-proto-final.patch` — everything that was in the worktree.
  - `proto-front-cache.patch` — clean, applies to f9c44628.
- **Probe host and scripts:**
  - `host\` (Program.cs, TextHost.csproj; publishes in `aot\` and `aot2\`).
  - `run.ps1` (headless), `batch.ps1` (guarded real windows plus the lock), `aotpub.ps1`, `sum.py`.
  - `holds.log`, `batch-r1.log`, `batch-r2.log`.
- **Results:** `runs\` (h1-* and h3/h5/j6-* headless; r1-* and r2-* real).
- **Micro-probe:** `fb\` with `fb.txt`, `fb2.txt`, `fb-base.txt`, `fb-ab.txt`, `fb-ab2.txt`.
- **Corpora:** `corpus\` (round 11's probe pointed at this worktree), `h48-base.txt`, `h48-proto.txt`, `h240late-proto.txt`.
- **Fixtures:** `fx\cjk`, `fx\cjk2`, `fx\cjkrepo`, made by `mkcjk.py` and `mkcjkdoc.py`.
- **Meridian test check:** `meridian-pinned.log`.
