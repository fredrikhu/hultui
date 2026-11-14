SATISFIED: no

All measurements were taken at 5a212f3e (detached, SDK 11.0.100). I report counts and bytes only, no timings. The headless runs used a JIT probe host that drives three apps on real documents:
- **Junction:** the Files view.
- **Vesper:** the wrapped viewer (Shift+F3 with wrap on).
- **Meridian:** the response body of a loopback GET that serves the document.

Real windows used a NativeAOT publish of the same host. The documents were the ja/zh/ko/th/ar/he/emoji/big READMEs, TODO.md, and the ledger archive. The archive is the largest real Latin document here (602,589 characters, 20,637 distinct words), and Junction's Files view opens it from a git fixture. Each round 14 change was reversed by an environment switch in the same binary. The probe edits are reverse-applied: the worktree is clean, the build servers are shut down, and no process is left running.

REVIEW: none. Each of the three changes holds outside Junction. Figures read reversed → shipped.
- **2c809bc1 (Meridian zh):**
  - Open 4,736 → 4,553 KB.
  - Width 1000: 395 → 283 KB.
  - Width 960: 456 → 369 KB.
  - Drag at 960: 1,739 → 572 KB per 40 frames (−67 %); HarfBuzz calls 1,292 → 986.
- **2c809bc1 (Meridian ja):** width 1000 231 → 188 KB; drag 884 → 838 KB.
- **2c809bc1 (Vesper's viewer):** the same bytes. Its wraps rarely read a table end. No phase is worse.
- **c06aa102 (Meridian Arabic):**
  - Open 27.8 → 15.9 MB.
  - A new width 23.0–25.2 → 12.5 MB.
  - Drag at 960: 1,049 → 47.2 MB per 40 frames; calls 5.43 M → 229k.
- **c06aa102 (Meridian Thai):** drag at 960 21.6 → 6.0 MB. Width 1320 3,525 → 212 KB, so Junction's +4 % at 1320 does not show here.
- **c06aa102 (held memory):** full-GC heap 13.5 → 14.2 MB.
- **5d9a5c71, on Latin past the old bounds (the archive):**
  - Vesper's drag 285.0 → 96.1 MB per 40 steps; calls 741k → 325.
  - Meridian's drag at 960 117.4 → 5.8 MB.
- **5d9a5c71, on README.big:**
  - Vesper's drag 109.0 → 19.1 MB at 1440 and 138.3 → 20.3 MB at 960.
  - Meridian's drag at 960 177.3 → 1.2 MB.
- **5d9a5c71, cost:** opening is +8 % to +24 % once, while the cache grows.
- **Latin past the new bounds:** no real document here reaches them. The archive fills 20,576 entries / 9.1 MB in Junction (63 % of the entries, 56 % of the bytes). Concatenating files would make generated text.

CANDIDATES:
- id: text-wrap-width-range
  what: The pipeline keeps a wrapped plain layout for the range of widths over which greedy breaking gives the same lines, and answers any Layout at a width inside that range with the kept layout. The range runs from lo, the widest width the fit test accepted on any line (trailing segment included), to hi, the narrowest width at which some line's next segment or character would fit.
  evidence:
    - **Where:** Vesper's RebuildViewerHeights measures every row through `TextMeasure.MeasureWrapped` at each new width (apps/VesperCommander/CommanderApp.Viewer.cs:366–392; WrappedHeight, CommanderApp.Diff.cs:162–166). Each row gets a full Layout that goes into the 4 MB layout cache, which was full in every run (427–691 entries, 4,086–4,095 KB).
    - **Per drag step, headless, 1 frame a step:**

      | Document | KB a step | Of that, the heights alone at a new width |
      |---|---|---|
      | The archive | 2,402–2,455 | 2,061–2,176 KB |
      | README.big | 478–506 | |
      | zh | 376–385 | 62–100 KB |
      | ja | 379–382 | |
      | ko | 382 | |
      | ar | 373–416 | |
      | th / he / emoji | 314–326 | |
      | TODO.md | 300 | 5–16 KB |

    - **The archive, per step:** 1,683 layouts, 1.0 M characters itemized, 332k word-cache lookups, 1,568 shape-cache misses and 363 ICU break calls. The 8 MB shape cache is full (1,524 entries, 8,189 KB) and thrashes. GCs per 40 steps: 5–6 gen0, 1–5 gen1. A width already seen again costs the same.
    - **For scale:** Junction's TextArea on the archive allocates 120 KB a frame at 1440 and 70 KB at 960.
    - **Prototype bound (Size.Width as lo):** a 1 px shrink would keep zh 348–374 of 388 rows, ja 487–495 of 498, and every archive and TODO.md row.
    - **Why the bound must come from the break pass:** Size.Width is not the fit test's width (a trailing space, the last line's character split). Re-measured at w−1, the rows that rule keeps differ for zh in 1–4 rows, ja 11–21, and Latin 127–179 at 8 px. So lo and hi have to come from BreakIntoLines' and SplitByCharacter's own decisions. With exact bounds: zh 89–96 % and ja 94–97 % of rows are kept on a 1 px step.
  real app: Vesper's wrapped viewer on a large text file. Every resize step, maximize, restore and snap re-measures the whole file. On the user's own ledger archive that is 2.4 MB a step, the same in a real window. The same path serves Vesper's wrapped diff (DiffRowHeight) and any `MeasureWrapped` caller. It also serves any wrapped label the layout engine measures again at a new width: the leaf cache has no range for wrapped text ("wrapped → NaN").
  expected: Scaling: removes O(rows) layouts a drag step for about 90–100 % of rows, and a kept layout needs no ShapedText, so the shape-cache thrash ends too. Memory churn: the archive's step 2.4 MB → about 0.4 MB (−80 % or more); zh heights −90 % (the step 376 → about 290 KB).
  cost: medium. The bounds go in BreakIntoLines and SplitByCharacter, plus a width-free memo beside the layout cache. Proof: an equivalence test over the real documents at many widths, where every kept layout must equal the fresh one to the bit. No API and no pixel change. Plain path first; the BiDi loop later.

REAL WINDOW:
- **Hold and guards:** one hold, r15a. It waited 154.1 s for another expert and held 169.6 s. The idle gate ran before the hold. 8 launches of the NativeAOT probe host on GL. 0 foreground takes, 0 refused activations, WM_CLOSE exit 0 on all 8, 0 nvlddmkm events, no screen reads.
- **Monitors:** the 4K (3840×2160 at 150 %, 120 Hz, primary), with the 1440p (2560×1440, 120 Hz) to its left. The window was 2,160×1,350 px (1,440×900 logical, dpi 144) at (1189,549) on the 4K.
- **Same as headless:** Vesper's viewer allocates per step zh 372/380 KB and the archive 2.40/2.45 MB (95.8/98.0 MB per 40 steps), 40 of 40 frames. A physical-pixel step (0.67 logical px) re-measures the whole file just the same.
- **What the real window adds:**
  - Junction Arabic's return to 960 after the drag there costs 6.2 MB, 28k calls and 1,977 width misses (the width cache at 4,095 KB); headless it was 9.7 MB.
  - Meridian Arabic's return costs 11 KB against 3.75 MB headless: the real drag's smaller steps keep the 960 probes.
  - Junction's archive drag at 960 is 21 KB a step (headless 70 KB).
- **text-bidi-real-window-private, settled:** private bytes after a full GC, c06aa102 → reversed, Junction Arabic:
  - Private: 188.6 vs 162.7 MB (+25.9).
  - Private RW: +25.8.
  - PAGE_WRITECOMBINE: 66.6 vs 42.1 (+24.5).
  - Allocations of 16 MB or more: 124.5 MB in 4 vs 91.7 MB in 3.
  - GC committed: +2.2.
  - Working set: 162.4 vs 161.6.

  With the change, other documents hold the same write-combined amounts: zh 63.4, the archive 67.4, Meridian 63.0. This is the GL driver's GPU-visible staging, sized by frame rate (the reversed build drew a frame every ~145 ms in drags). It is not our memory and not a leak.

WAITING, RE-RANKED:
- **text-open-measures-every-line (full):** keep Waiting under the "not now" answer.
  - The new largest real document, the archive, in Junction:
    - At 1440: 1,164 tables, 216k characters itemized, 121 calls, 120 KB a frame.
    - At 960: 3,407 tables, 273k characters, 70 KB.
    - Real window: 113 / 21 KB a step, 40 of 40 frames.
  - Meridian's response body on the archive at 960: 2,578–2,820 tables, 298–333k characters, 144–146 KB a frame.
  - This is the same order as the 154k file (251–335k characters). No larger real document exists here.
- **text-cjk-row-start-suffix:** still a lead, lower. Meridian zh: a new width 399–605 calls; a drag frame at 960 25 calls and 14 KB. ja: 259–368 calls and 28. Drags have nothing left to win.
- **text-junction-first-visit-tokenize:** close (see Examined).
- **text-bidi-real-window-private:** close (see REAL WINDOW).
- **apps-vesper-wrapped-drag-bytes (8/1/5):** cause now counted.
  - On zh, ja, ko and TODO.md the heights are 5–100 KB of a 300–385 KB step. The rest, about 280–300 KB and about 150 more layouts, is the visible rows made again at every width change: `remake = true` whenever the code width moves (CommanderApp.Viewer.cs:538–545). Yet a row's content does not depend on the width; heights come through RowHeights.
  - On the archive the heights are 86–90 % of the step (my candidate).
  - The remake is app code: hand it to expert 8 or 1.
- **textarea-chunked-document:** keep parked. TextArea storage is unchanged and the 256k-character condition is unmet.
- **syntax-shared-registry-rest:** keep parked. No syntax code changed.

EXAMINED, NOT WORTH IT:
- **Width cache at 16 MB for BiDi probes:**
  - Junction Arabic: drag at 960 80.8 → 59.1 MB (−27 %), at 1440 44.4 → 41.4 (−7 %), return 9.7 MB → 16 KB; it keeps +8.8 MB (39,356 entries, 12.9 MB).
  - Meridian Arabic: drag −9 %, return 3.75 MB → 10 KB, +2.5 MB.
  - Hebrew: unchanged.
  - The drags still miss 209–458 new probes a frame: these are new prefixes, not a cliff. The remaining ~1.1 MB a frame is RTL shaping without the word cache, which was declined in round 1.
- **Tokenizing (text-junction-first-visit-tokenize):**
  - A warm ~340-line C# file allocates 1.6–2.5 MB (4.7–7.3 KB a line, 1.5–2.2 KB a span); the cold registry and grammar 8.1 MB once.
  - Allocation-tick samples over 82 MB put at least 83 % in TextMateSharp/Onigwrap types (Onigwrap.Region 12 %, OnigResult 9.5 %, OnigCaptureIndex 9.4 %, StateStack 7.1 %), plus Int32[] at 27 %. HultUI's own TextSpan[] is 2.9 %.
  - HultUI already uses TokenizeLine2, and TokenizeDiff stops at the last shown line. What is left lives in the dependency.
- **Rich text editor:** in the apps it is used only for GitLab MR descriptions and review replies (JunctionApp.CreateMergeRequest.cs:325, Review.cs:1087), which are short. None of round 14's changes is specific to it; not measured.

NEEDS THE USER: none.

FILES: all in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\8e8ba33b-db33-43fa-885a-cb6b82b7253f\scratchpad\r15-text\
- **r15-probe-final.patch** and **TextProbe.cs.keep:** the counters plus switches R15_OLDTRIM, R15_OLDWORD, R15_OLDPROBE, R15_WIDTHMB and R15_WIDTHCAP.
- **host\Program.cs** and **host\TextHost.csproj:** apps junction, vesper and meridian, headless or real. TEXT_RANGE=1 adds Vesper's range probe and the heights cost. The VMAP memory split is in real windows. Builds: jit, jit2, jit3, aot, aot2.
- **tok\:** the tokenize allocation probe; its output is tok.log.
- **Scripts:** run.ps1 (headless), batch.ps1 (guarded, desktop lock), summ.py, cmp.py, words.py, aotpub.ps1.
- **Fixtures:** fx\docs and fx\reallatin (a git repository holding the archive).
- **Logs:** runs\, holds.log, batch-r15a.log, sweep-a.log, sweep-b.log, sweep-c.log.
