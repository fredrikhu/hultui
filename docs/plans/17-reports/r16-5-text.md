SATISFIED: no

I measured at 440ba012 (detached, SDK 11.0.100) with a counter probe that is now reverse-applied; the worktree is clean, the build servers are shut down and no probe process is running. Headless runs used the NativeAOT and JIT builds of round 15's host, extended: per-face HarfBuzz calls by word length, cold runs against cold words, and wrap-range asks, hits and puts. Scale 1.5, 1,440×900 logical. Documents: the real-text fixtures and a fresh clone of jrepo (1,772 commits).

REVIEW:
- **f388b11d against 52852f6e (objection).** The one-line fast path now accepts a line up to 1/256 px wider than the box (`T <= maxWidth + OneLineFitTolerance`, TextPipeline.cs:503). A kept wrap range still runs up to the text's own width T (`fitsBelow = Math.Min(fitsBelow, shaped.TotalWidth)`, :547–552). A Layout whose shape-cache entry is gone asks the range before shaping (:492). So at T − ε, with ε under 1/256, the kept 2-line layout comes back where a fresh layout gives 1 line.
  - **Probe (`tolcheck`):** 8 strings ("SAME DAY", "Open in new window", …) × letter spacing 0 and 0.4 × ε of 1e-5, 1e-4, 1e-3 and 0.003. Each was wrapped once at T − 2/256, then the shape cache was cleared: 64 of 64 asks DIFFER (kept 2 lines, fresh 1).
  - **Why the corpora miss it:** they never evict between two widths. This is f388b11d's own bug ("SAME DA") coming back. Candidate below.
- **52852f6e/83034345, the memo in the real apps:**
  - **Vesper's reader:** zh 317 entries / 2,763 KB (real window 245 / 2,381); archive 403 / 6,080 KB. Hits in a 40-step drag: zh 92 % (88 % at 960), archive 94 % (93 %). Reopening the viewer: 275/275 and 430/436 hits, 0 HarfBuzz calls.
  - **Rich text editor:** TODO.md 17 / 229 KB, zh 333 / 2,844 KB. Drag 92 % (513/560 and 10,063/10,977); a second editor 100 %.
  - **Junction's Files view and Meridian's body:** 0 entries. Their TextAreas break lines themselves, so nothing changed there.
  - **Full-GC heap:** Vesper archive 16.2 MB, zh 13.5 MB, the same as the landed row's 16.5 / 13.7.
  - Apart from the objection above, no caller got a range answer that differs from a fresh layout. Culture changes clear the memo (FollowCulture :279–280).
- **043f5b38 (HarfBuzz 14.4), behaviour and counts unchanged:**
  - **Corpora at the tip:** 48k 7A18AF8DE8E05271; 240k late and reference F09E9038726E4BD6; CJK wrap 1E6F1A93894D0B6A / rows 6C7FA51792E8EB84, 0 of 1,344,616 differ.
  - **The same binaries with libHarfBuzzSharp 8.3.1.5 swapped in:** identical hashes. Read-off-cuts 724,228 → 724,215: 13 widths (0.002 %) are shaped instead of read.
  - **App counters, 14.4 against 8.3.1, four actions (Junction TODO, Vesper zh, Meridian big, editor TODO):** every phase identical except where a load landed one phase later. Vesper zh's open totals 8,210 = 8,210.
  - The itemizer, ICU and BiDi paths make no HarfBuzz calls (only the shaper and `FirstWithScript` do).
- **00ee728d:** one more field copied into a TextSpan that is already made per line span; 0 B a frame. No app gives a TextArea a decorated span: Junction's underlines are on TextElement labels (JunctionApp.Issues.cs:93–94).
- **GlyphOutlines:** made only when `TextAsOutlines` is turned on (SkiaRenderContext.cs:310). Only `HultUI.Headless` PinnedFonts sets it, for the goldens; no app pays anything. NativePath is painting (expert 2).

CANDIDATES:
- id: text-wrap-range-one-line-tolerance
  what: A kept wrap range ends where the one-line fast path begins, so a width a float error short of a paragraph's own width gets one line, as a fresh layout does.
  evidence: the probe above, 64/64 asks differ, at TextPipeline.cs:492 / :503 / :547–552.
  real app: f388b11d's case (the application team's finding 7: a label sized to its text, its width coming back a float error short). It shows only after that label was wrapped once at a width within one word of its own (a resize that narrows it slightly), and once the shape cache has turned over. Vesper's reader on the archive fills the cache (1,524 entries, 8,180 KB in my run). Rare, but it is a reported bug coming back.
  expected: correctness, no perf bar. 64 → 0 differing asks; one float a WrapRange (at most 4,096 entries).
  cost: small. Keep T in the WrapRange and test `!(T <= maxWidth + OneLineFitTolerance)` in KeptWrap, the fast path's own expression, so float rounding cannot split the two. Add a WrapRangeTests case that clears the shape cache. No API. Pixels: restores f388b11d's one-line answer.

REAL WINDOW:
- **Setup:** 1 hold (waited 290.9 s behind expert 4, held 62.3 s), 4 NativeAOT launches: Junction on the archive, Vesper zh, Meridian archive, the editor on TODO.md. 0 foreground takes, 0 refused activations, WM_CLOSE exit 0 on all 4, 0 nvlddmkm events, no screen reads.
- **Idle gate:** failed before the hold (expected on this desktop). The batch's exit code 1 is the gate's.
- **Monitors:** the 4K (DISPLAY2, primary, 3840×2160, 120 Hz, dpi 144 = 150 %); window client 2,160×1,350 at (1189,549). The 1440p (2560×1440, 120 Hz) to its left.
- **Cold shapes equal headless within 0.1 %:**
  - Junction start 667 calls (headless 661), archive open 11,738 (11,735);
  - Vesper zh open 8,210; Meridian archive 12,188; editor TODO 5,206;
  - DirectWrite system fonts in both.
- **Junction archive drag, 1 physical px steps:** 5,124 calls per 40 steps (headless 1 logical px: 4,491), 68 KB a step on both.
- **Vesper zh drag:** 51.9 KB a step in the real window against 31.4 headless, although the text counters are lower (1,236 against 1,578 calls; prefix runs 7,751 against 11,347; 94 % wrap hits). The extra bytes are outside the text pipeline; not chased.

WAITING, RE-RANKED:
- **textarea-chunked-document:** keep parked. Only 00ee728d and ae3ac740 touched TextArea; storage is unchanged and the 256k-character condition is unmet.
- **text-open-measures-every-line (full):** keep Waiting (question 57, "not now"). Same order as round 15:
  - Junction's Files view on the 522 KB archive, a drag step at 1,440: 191k characters itemized, 1,042 prefix runs, 112 calls, 68 KB. At 960: 237k, 2,960, 122, 114 KB.
  - Meridian's body on the archive at 960: 298k characters, 2,582 runs, 211 calls, 145 KB a step. At 1,440: 2.4 KB (the lines fit).
- **syntax-shared-registry-rest:** keep parked. Only TextMateSharp.Grammars moved (2.0.4 → 2.0.5, 70afa1d8).
- **text-cjk-row-start-suffix:** lower. Vesper zh drag 39 calls a step at 1,440 (real window 31), 75 at 960; a new width 825; the wrap ranges answer 88–94 % of rows.
- **apps-junction-big-file-revisit:** still worth it on expert 8's numbers (30.6–30.8 MB a key), but there is no text-pipeline part.
  - The text pipeline is warm on every revisit I measured: 0 HarfBuzz calls; Junction's Files reopen of the 522 KB archive costs 349 KB UI.
  - The cost is the git blob reads and the TextMateSharp tokenize (round 15, Examined). Owner 4 or 6, not 5.
- **Suite (d):** keep, low. Nothing new in text; expert 8's zh drag is equal.

EXAMINED, NOT WORTH IT:
- **HarfBuzz 14.4 cold shapes per real action.** Every second visit (History again, Files reopen, viewer reopen, resend, second editor) makes 0 calls. Distinct (face, script, direction) plans: 3–15 a process.

  | Action | Cold calls | By face |
  |---|---|---|
  | Junction start on History | 661 | Segoe UI 532 (46 one-letter, 327 two-letter), Consolas 129 |
  | 20 History Downs | 58 | 2.9 a key |
  | Junction Files: TODO.md | 2,401 | Consolas 2,347 |
  | Junction Files: archive | 11,735 | Consolas 11,662, Segoe UI 57 |
  | Vesper viewer: zh | 8,210 | Microsoft YaHei UI 5,773, Consolas 2,417, Segoe UI 2 |
  | Vesper viewer: archive | 18,977 | Consolas 18,959 |
  | Meridian body: archive | 12,188 | Consolas 11,927, Segoe UI 241 |
  | Meridian body: README.big | 12,665 | YaHei 5,329, Yu Gothic UI 3,487, Consolas 3,605, Segoe UI 241 |
  | Editor open: TODO.md | 5,206 | Segoe UI 5,200 (3,832 of 3–9 letters) |
  | Editor open: zh | 8,437 | Segoe UI 2,458, YaHei 5,961 |

  - Plan 18 timed Segoe UI only. At +193 ns a call (an upper bound for 3–9-letter words) that is ≤ 1.0 ms once for the editor's open and ≤ 0.10 ms for Junction's start-up: below the 10 ms start-up bar.
  - Consolas and the CJK UI faces carry 85–100 % of the cold calls in the opens; plan 18 did not time them. The build phase could time them if wanted.
- **A kept shape plan per (face, script, direction):** 40–50 ns × the calls above, once a process. That is 0.03 ms at Junction's start-up and ≤ 0.95 ms on the largest open (Vesper's archive). No bar, and no O(n) per frame.
- **Shaping whole runs instead of word by word, sized:**

  | Open | Cold words (calls now) | Cold runs | Characters shaped: now → whole runs |
  |---|---|---|---|
  | Junction Files: archive | 11,330 | 1,892 | 95k → 275k |
  | Vesper viewer: archive | 17,924 | 3,449 | 155k → 415k |
  | Meridian body: archive | 11,317 | 2,397 | 93k → 271k |
  | Editor: TODO.md | 5,034 | 1,149 | 42k → 99k |
  | CJK (Vesper zh, Meridian big, editor zh) | 7,537 / 11,119 / 7,676 | 7,335 / 10,771 / 7,470 | — |

  - On Latin text, calls fall 77–84 % and the characters shaped grow 2.4–2.9×. On CJK text a run already is a word, so calls fall 3 %.
  - Used in place of the word cache, it would scale worse: Junction's archive drag at 960 would shape about 72k characters a step against 1,139 now (64×).
  - Used only to fill the word cache, every cut must equal the word shaped alone, to the bit. The buffer guesses its script per run, so Common-script words (digits, "→") would take their run's script, and the corpora would move.
  - Only first visits pay any of this. Not a scaling argument.
- **The rich text editor's resize, 248 B a block a step:** TODO.md's 1,015 blocks cost 251 KB a step, headless and in the real window. A new BlockLayout and paragraph are made per block per width, and the wrap ranges answer 92 % of them. BlockLayout was slimmed in round 8. In the apps the editor holds MR descriptions and review replies, which are short: no real-app case.

NEEDS THE USER: none.

FILES: all in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\b6ef64d9-4ed1-4793-9955-6a986d74de79\scratchpad\r16-text\
- **Probe:** r16-probe.patch (reverse-applied) and TextProbe.cs.keep.
- **Host:** host\Program.cs and host\TextHost.csproj. Apps junction, vesper, meridian, rte and `tolcheck`. Builds: jit, jit-hb8 (libHarfBuzzSharp 8.3.1.5 swapped in), aot.
- **Corpora:** tools\corpus and tools\cjkwrap, each with out\ and out-hb8\. Logs: dump48k.log, dump48k-hb8.log, dump240k-late.log, dump240k-ref.log, cjkwrap.log, cjkwrap-hb8.log.
- **Scripts:** run.ps1 (headless), batch.ps1 (guarded, desktop lock), aotpub.ps1.
- **Runs:** runs\a-*.log (JIT), runs\aot-*.log, runs\hb8-*.log, runs\r16a-*-real.txt, runs\tolcheck.log.
- **Logs:** holds.log, batch-r16a.log, run-a.log; fixtures in fx\ (jrepo clone, docs).
