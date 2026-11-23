SATISFIED: no

**Setup.** Measured at aa3a3696 (detached, SDK 11.0.100). The probe was round 17's counters, re-based, plus size-path, RTL-memo, proof and BiDi counters and three switches: `TEXT_SZRESTORE`, `TEXT_MULTIRANGE` and `TEXT_PROOFAFTER`. It is now reverse-applied with `git apply -R`; the worktree is clean, the build servers are shut down and no host process is running. Runs were headless (JIT and NativeAOT) plus one real-window hold. Scale 1, 1,440×900, alternated where bytes are compared.

REVIEW:
- **e790d321: the unexplained second sweep (1,575 → 1,772 re-breaks) is now explained.** It is a small regression.
  - **Cause:** a size ask (`WrappedSize`) answered from the width-keyed `_cache` does not put that layout's wrap range back. The view's opening width holds a full layout of every row (first sight lays out in full). So a return to that width (the jcommit host's jump back to 1,440, Vesper's "resize to 1,440") is answered from `_cache`, and each row keeps the range from the far end of the last drag. The next drag's first step then breaks every row again.
  - **Before e790d321 this did not happen:** the old path's ~1,500 puts a sweep evicted the opening layouts from the 4 MB `_cache`, so the return re-broke and renewed the ranges.
  - **Proof:** re-putting the range on that hit (prototype `TEXT_SZRESTORE=2`) brings a359e426 split steps2 back to steps1's size breaks (1,663 → 1,436, = 1,438).
  - **Vesper zh reader, drag after the scene's return to 1,440, against round 17:** characters itemized 364,749 → 434,070 a 40-step drag (+19 %) and prefix tables 11,347 → 14,246 (+26 %). Bytes are still lower: 1,251 → 1,035 KB. With the restore, both counts are exactly round 17's again.
  - This becomes candidate text-wrapped-size-cache-hit-keeps-range.
- **781a857f (number punctuation): it costs proofs on first frames.** Each (face, size, mark) is proven at its first number: ~125 HarfBuzz calls a mark. On/off via a probe switch, JIT:

  | Action | HarfBuzz calls | Proof calls | UI KB |
  |---|---|---|---|
  | Junction first frame | 646 → 1,585 | 1,398 | 1,251 → 1,415 (+13 %) |
  | Junction Files open (ledger) | | | +127 KB (+2.1 %) |
  | Vesper start | | +368 | +58.5 KB |
  | Vesper zh reader open | +869 net (896 proofs, 27 numbers saved) | | +169 KB (+2.4 %) |
  | Meridian archive open | −1,342 | | −288 KB (−4.6 %) |
  | Junction History, 20 Downs | | | −8.8 KB |

  Mixed. The fix tried (EXAMINED) does not clear a bar on AOT, so I raise no objection.
- **78a69ee5 (RTL runs kept): no objection, a large real-app win.** Vesper reader on the real README.ar.md, memo off → on:
  - open: 39,963 → 3,871 HarfBuzz calls, 8,631 → 4,630 KB;
  - resize to 1,200: 38,067 → 8 calls, 7,262 → 2,369 KB;
  - drag at 960: 16,395 → 13 calls.
  - **Retained:** the memo holds 2,317 runs / 666 KB (he: 1,414 / 396 KB). The full-GC heap is lower: 21.3 → 14.2 MB (ar), 10.4 → 7.6 MB (he).
- **52acb2f2:** a counter increment only; no cost.
- **Round 17's build, text side:** no other objection.
  - Meridian archive drag counters equal round 17's. Its open is 6,355 → 5,995 KB (the digit table).
  - Editor TODO.md drag equals round 17: 40,600 layouts and 513 range hits a drag, 10,072 → 10,053 KB.
  - No TextArea or Syntax-registry change since round 17.

CANDIDATES:
- id: text-wrapped-size-cache-hit-keeps-range
  what: When `WrappedSize` answers from the width-keyed `_cache`, put that layout's wrap range back (keep its two floats with the cache entry), so a return to a width laid out before does not leave every row's range at the last drag's far end.
  evidence: prototype `TEXT_SZRESTORE=2`, the second 40-step sweep after the jump back to the opening width:

  | Scene | UI KB a step | Size breaks | Full layouts | Characters itemized |
  |---|---|---|---|---|
  | a359e426 split, NativeAOT | 23.6 → 18.5 (−22 %) | 1,663 → 1,436 | 109 → 7 | 239k → 194k |
  | a359e426 unified, JIT | 19.4 → 16.6 (−14 %) | 1,034 → 807 | 57 → 0 | |
  | 574f43a7 split, JIT | 45.6 → 22.8 (−50 %) | | 116 → 3 | 65k → 22k |
  | Vesper zh reader drag, JIT | 25.9 → 24.6 (−5 %) | 1,065 → 846 | | 434k → 365k (= round 17) |

  - The first sweep is unchanged (33.0 / 52.1 / 37.8 KB).
  - **574f43a7 on AOT:** the base sweep read 92.8 KB a step, which I take to include a one-time cache growth I did not isolate; with the restore it is 22.6.
  - **Restore only on the size path:** doing it on Layout's `_cache` hits too (`TEXT_SZRESTORE=1`) makes the split view's first sweep 33.0 → 81.8 KB. Drawn rows are asked at two widths a frame (1,172 width hits a sweep), and the restored range ping-pongs.
  real app: Junction with wrap on (and Vesper's diff and reader, CommandPalette: every `TextMeasure.MeasureWrapped` caller): a resize drag after the window comes back to the width the view opened at (maximize then restore, a snap, a drag that returns). A few times a session, 40–100 steps each. Real window, Junction split a359e426: 23.6 → 18.6 KB a step.
  expected: the churn bar on the drag after a return: −14…−22 % UI KB a step on the ledger commit, −50 % on 574f43a7 split; re-breaks back to round 17's counts.
  cost: small (TextPipeline only: the range bounds kept beside the width-keyed layout, re-put on a size-path hit). No API. Pixels unchanged (the range is the same layout's own).

- id: text-bidi-wrap-ranges
  what: Give right-to-left paragraphs the wrap ranges left-to-right text has had since rounds 15–16, so a drag step does not lay out every paragraph of the document again. `ComputeWrappedBidi` would track the widths its break decisions hold for.
  evidence: Vesper reader, real README.ar.md (477 BiDi paragraphs), JIT:
  - **Every paragraph, every step:** 477 BiDi layouts a step, 19,080 a 40-step drag. 18,995 of them (99.6 %) at 1,440 and 18,776 (98.4 %) at 960 have the same line starts as the paragraph's previous layout. These are the asks a range would have answered.
  - **Bytes:** the BiDi layouts are 3.06 of 3.41 MB of the drag's UI bytes (90 %) at 1,440 and 4.56 of 4.94 MB (92 %) at 960.
  - **README.he.md:** 82 % / 86 % of bytes, 99.3 % / 98.2 % same breaks.
  - **For comparison:** the left-to-right zh reader drags at 25.9 KB a step.
  real app: Vesper's wrapped reader (also Junction's Files and diff views, Meridian bodies) on any text with right-to-left characters, during a window resize or splitter drag. Rare for this user, but O(document paragraphs) a step. Real window: 19,063 of 19,593 BiDi layouts in the drag had the same breaks (97.3 %).
  expected: churn bar, upper bound: ar 83.4 → ~9 KB a step at 1,440 and 120.7 → ~11 at 960 (−89 %); he 36.4 → ~7. Also the scaling bar: O(paragraphs) a step → O(paragraphs whose breaks move).
  cost: medium (the BiDi break loop's range plus an equivalence run on realar/realhe/emoji, as round 15's). No API, pixels unchanged.

REAL WINDOW:
- **Setup:** 1 hold of `Global\HultUI-r18-desktop` (idle gate passed before; waited 517.2 s, held 67.4 s). 4 NativeAOT launches of the probe host:
  - h1/h2: Junction split a359e426, base then restore;
  - h3: Vesper reader on ar;
  - h4: Junction start, History, Files on the ledger.
  - 0 foreground takes, 0 refused activations, `WS_EX_NOACTIVATE` set 8 times a launch, WM_CLOSE exit 0 on all 4, 0 nvlddmkm events, no screen reads.
- **Desk:** the 4K (DISPLAY2, primary, 3840×2160 at 150 %, 120 Hz, HDR on, GL), client 2160×1350 px (1,440×900 logical); the 1440p (DISPLAY1, 100 %, 120 Hz) to its left; Steam running. Timer resolution not read; no wake claim.
- **What it showed:**
  - **Junction timer-step sweeps (9 physical px):** identical to headless AOT to the call (steps2 1,663 size breaks / 109 full layouts → 1,436 / 7 with the restore). 32.6/23.6 → 32.7/18.6 KB a step.
  - **Junction start:** the same 1,656 proof calls as headless, but spread differently (330 in the first frame, 1,326 in settle, against 1,398 / 258 headless).
  - **Vesper ar:** the open matches headless (3,871 HarfBuzz calls, 2,307 memo misses). The drag's posted 1-physical-px steps cost 154 KB a step against 83 headless with 1-logical-px steps, with the same 23,760 layouts. Not split further; the step widths differ.

WAITING, RE-RANKED:
- **text-wrap-ranges-per-width: close.** Not worth it on top of steps 1 (dd3ad712) and 8 (e790d321). Split a359e426 JIT, the 4-range memo:
  - UI KB a step: 33.0/23.7 → 33.1/23.0;
  - far misses: 1,088/1,408 → 0/281;
  - re-breaks: 1,581/1,772 → 1,581/1,757;
  - the 1,172 width hits it removes were already answered by `_cache` at 0 B.
- **text-cjk-row-start-suffix:** low, unchanged. Vesper zh drag 1,577 / 3,006 HarfBuzz calls a 40-step drag (39 / 75 a step at 1,440 / 960), equal to round 17.
- **text-open-measures-every-line (full):** keep Waiting (user: "not now"). Vesper zh open is unchanged: 211,248 characters itemized, 3,059 tables. The drag's 434k characters go back to round 17's 365k with the restore candidate. The only new evidence is the right-to-left part, which text-bidi-wrap-ranges covers.
- **textarea-chunked-document:** parked. No TextArea storage change since round 17.
- **syntax-shared-registry-rest:** parked. No registry change (a787c194 touched only `TokenizeDiff`).

EXAMINED, NOT WORTH IT:
- **Deferring each digit or mark proof to a face+size's 8th number segment** (prototype `TEXT_PROOFAFTER=8`):
  - Junction first frame: HarfBuzz calls 1,585 → 787. Bytes 1,462 → 1,345 KB on AOT (−8.0 %, under the churn bar); JIT −10.3 %.
  - Whole Junction session: −1,011 calls (−16 %). Meridian keeps its win (open 10,846 → 10,556 calls).
  - The start-up bar (≥10 ms) is unlikely: ~800 calls of 1–3 characters. The build phase could time it if the first frame matters.
- **The 1,172 width hits a split-view sweep** (drawn rows asked at a second width each step): answered by `_cache` at 0 B.
- **The rich text editor's resize, 248 B a block a step:** already examined (r16-5); unchanged.

NEEDS THE USER: none.

FILES: all in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\00f54f03-b511-4949-9273-30fe719e1bbb\scratchpad\r18-text\
- **Probe:** r18-probe.patch (reverse-applied) and TextProbe.cs.r18.
- **Host:** host\Program.cs and TextHost.csproj (round 17's); builds in host\jit and host\aot.
- **Scripts:** jc.ps1, run.ps1, batch18.ps1, aotpub.ps1.
- **Runs:** runs\js-*, ju-*, j574s-*, a-* (Junction, JIT and AOT); v-*, j-led*, m-arch*, r-todo (apps); runs\h1–h4-*-real.txt (real window).
- **Logs:** holds.log, batch-r18a.log, gate.log, build-jit.log, publish-aot.log.
- **Fixtures:** fx\docs, fx\jrepo.
- **Diffs read:** e790.diff, 781a.diff, 78a6.diff, 52ac.diff.
