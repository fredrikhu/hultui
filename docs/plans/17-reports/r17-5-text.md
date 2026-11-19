SATISFIED: no

I measured at 0df5e3ea (detached, SDK 11.0.100). The probe was round 16's counters, re-based onto 40cadb57, plus new counters and a multi-range prototype behind `TEXT_MULTIRANGE`. It is now reverse-applied, the worktree is clean, the build servers are shut down and no probe process is running. Runs were headless (JIT and NativeAOT) and in one real-window hold. Scale 1.5, 1,440×900 logical. Junction scenes are on jrepo, wrap on, Commit view, with 40 window steps of −6 logical px, run twice:
- **a359e426:** a ledger rewrite, 382 rows, 33k characters.
- **574f43a7:** Element.cs, 72 rows, with long lines on screen.
- **Split view:** a359e426, 263 rows, 516 texts.

REVIEW: none. No objection to round 16's build or main's 8 commits.
- **40cadb57 at the tip:** round 15's full real-document comparison found 0 of 5,038,170 layouts differ, with 1,430,053 of 2,787,729 wrapped asks kept (51.3 %). That equals the landed row. Real-app kept-answer rates in a 40-step drag are unchanged from round 16: Vesper zh 9,584/10,456 (91.7 %), archive 14,985/15,905 (94.2 %), editor TODO 513/560 (91.6 %).
- **Round 16's build and main's 8, text counters:** every counter of Junction Files archive, Vesper zh, Meridian archive and README.big (open, drag at 1,440 and at 960) is identical to round 16's at 440ba012. Bytes are within 0.7 % (Vesper zh open 6,978.5 → 6,931.0 KB).
- **3d728568 / 13a1b363 / 49b0da84, text side:** one heights pass a step, at the laid-out width, as intended. What is left is in CANDIDATES (the labels on screen) and EXAMINED (the heights pass itself).
- **f76cb440 / 80e94eeb:** only colour-only spans reach the pipeline. They take the plain path and its keys (TextPipeline.cs:414), so there is no text cost.
- **Main's 8 commits:** none touches text code.

CANDIDATES:
- id: text-wrap-ranges-per-width
  what: Keep up to 4 wrap ranges per paragraph instead of 1. A put replaces the range it touches or overlaps, else takes a new slot, else the least recently used. A label laid out at several widths each frame then keeps one range per width, instead of each width evicting the last.
  evidence: Junction's wrapped code labels on screen are laid out at 4 widths every frame. One is the drawn width W (TextElement.Render). The other three are W+510, W+266 and W+244 (1312 / 1068 / 1046 at W = 802). Those come from `RemeasureFlexedCrossSizes → MeasureLeafSize` several flex levels up (stack captured), at widths never drawn. The one-range memo thrashes:
  - **C# commit:** 1,062 of 1,346 range misses per 40 steps were asked more than 60 px from the kept range; 1,129 re-breaks against 94 in the heights pass alone.
  - **Split ledger:** 3,658 of 5,590 misses far; 4,394 re-breaks against 1,781.

  Prototype results, NativeAOT headless, first drag:

  | Scene | UI KB a step | Re-breaks | Word-cache gets | Characters itemized |
  |---|---|---|---|---|
  | C# unified | 72.0 → 46.0 (−36 %) | 1,129 → 432 | 193,657 → 74,444 | 403k → 149k |
  | Split ledger | 99.1 → 52.9 (−47 %) | 4,394 → 2,130 | 201,927 → 90,517 | — |

  - **Split ledger, second drag:** 85.1 → 38.3 KB a step (−55 %).
  - **Unified ledger (no long lines on screen):** unchanged.
  - **Other apps:** Vesper's reader and the editor keep the same hits (zh 9,584 → 9,585; archive 14,985 → 14,985; editor re-breaks 47 → 35), drag bytes within ±1 %.
  - **Retained:** after a session with 5 jump resizes the full-GC heap grows 13.5 → 13.9 MB (zh) and 16.3 → 16.6 MB (archive), +2–3 %. Older width classes are kept, inside the memo's 16 MB cap.
  - **Equivalence:** 0 of 4,857,450 drag layouts differ from the reference. The probe's bounds phase reads `KeptWrapRange`, which the prototype does not fill; a build must update it.
  real app: Junction with "wrap" on: a window resize or a drag of the file list's splitter while the diff shows long lines (C#, markdown) or the split view. It comes in bursts of 40–100 steps, a few times a session. Real window (below): 71.1 → 45.5 KB and 64.3 → 34.3 KB a step (C#); 98.6 → 52.1 and 83.9 → 37.0 (split). The colours' landing frame on the C# commit also re-breaks 20 labels for the same reason.
  expected: the churn bar, −36…−56 % UI bytes a width step in these scenes. No time claim: cycle shares stayed within noise.
  cost: small–medium (TextPipeline WrapRange / KeptWrap / Put, `KeptWrapRange` for the tests). No API. Pixels unchanged (bit-identical layouts).
  - **Lead for expert 1 (layout), the root cause:** if layout stopped laying the labels out at the three ancestor widths, 3 of the 4 layouts a frame per wrapped label on screen would go away entirely. This candidate would then be unneeded. I'd have expert 1 look at it first.

REAL WINDOW:
- **Setup:** 1 hold of `Global\HultUI-r17-desktop` (waited 516.3 s, held 61.3 s; idle gate passed before the hold). 4 NativeAOT launches of the probe host on Junction's Commit view with wrap on, sides alternated (C# base, C# multi, split multi, split base).
  - 0 foreground takes, 0 refused activations, the driver set `WS_EX_NOACTIVATE` 8 times per launch, WM_CLOSE exit 0 on all 4, 0 nvlddmkm events, no screen reads.
- **Monitors:** measured on the 4K (DISPLAY2, primary, 3840×2160 at 120 Hz, dpi 144 = 150 %), client 2160×1350 at (1189,549), GL, HDR on. The 1440p (DISPLAY1, 2560×1440 at 120 Hz) to its left.
- **Steps:** 40 × −9 physical px from a Win32 timer callback on the UI thread, twice; bytes counted inside `SetWindowPos`.
- **What it showed:** nothing the headless run missed. Text counters equal headless AOT to the call (word gets 193,657, re-breaks 1,129, …), and UI bytes a step are within 1 % (71.1 vs 72.0; 98.6 vs 99.1). The heights pass alone matches too (0.07 / 0.75 Mcycles, 1,089 / 13,696 B a pass). Timer resolution was not read, and I make no wake claim.

WAITING, RE-RANKED:
- **text-open-measures-every-line (full):** keep Waiting (user: "not now"). Numbers at the tip equal round 16's, per drag step:

  | View | Characters itemized | Prefix tables | HarfBuzz calls | KB |
  |---|---|---|---|---|
  | Junction Files archive at 1,440 | 191k | 1,042 | 112 | 68 |
  | Junction Files archive at 960 | 237k | 2,960 | 122 | 114 |
  | Meridian archive at 960 | 298k | 2,582 | 211 | 145 |
  | Meridian README.big at 960 | 285k | 45,065 | 52 | 30.8 |

- **text-cjk-row-start-suffix:** low, unchanged. Vesper zh drag: 39 calls a step at 1,440, 75 at 960; the wrap ranges answer 92 % of rows.
- **syntax-shared-registry-rest:** parked. No Syntax registry change.
- **textarea-chunked-document:** parked. Neither round 16 nor main's 8 changed TextArea storage; the 256k-character condition is still unmet.
- **junction-colour-landing-remakes-rows:** not a text-side change; owner 6. `TextElement.Spans` already value-compares and marks only paint for colour-only spans (TextElement.cs:88–103). Setting the landed spans on kept labels would cost 0 text layouts.
  - **The colours' frame today:** on the ledger commit 385 Layout calls (383 one-line fast path, 2 width hits), 0 HarfBuzz, 0 re-breaks. On the C# commit 360 calls, 20 re-breaks and 24 HarfBuzz calls, from the ancestor-width layouts above.
  - **The remake is app-side:** `rowShape` changes with `hl`, so `InvalidateRows` runs (JunctionApp.Diff.cs:629–639). That frame is 490–534 KB UI, almost none of it text.

EXAMINED, NOT WORTH IT:
- **A same-height width range for Junction's heights pass** (the review question; it would need a public out-range on `TextMeasure.MeasureWrapped`). Per step of the heights pass alone, NativeAOT:

  | Scene | Rows measured | Rows whose height moves | Bytes a pass | Share of step bytes | Share of step cycles |
  |---|---|---|---|---|---|
  | Ledger unified | 220.6 | 13.1 | 8,064 | 30 % (of 27.0 KB) | 4.0 % |
  | Split | 333.6 | 10.5 | 13,696 | 16 % | 5.3 % |
  | C# | 29 | 1.7 | 1,089 | 2 % | 0.5 % |

  - **The ceiling:** avoiding the ~14 re-breaks a step whose line count holds, plus reusing the array, is at most −4.9 KB of 27 KB (−18 %) on one scene, with a public API. Not worth asking now. The multi-range candidate takes the larger share without API.
- **Reusing `_diffHeights`' float[] (app-side):** −1.5 KB a step (5.7 % of 27 KB), below the bar on its own.
- **First-drag cold words in Junction's diff:** 1,728 Consolas calls per 40 steps (43 a step), 0 on a second drag. These are rows the mono shortcut stops covering as the width narrows, measured for the first time; inherent.
- **~215 one-line fast-path layouts a step outside the heights pass** (the chrome's labels): 0 B.

NEEDS THE USER: none.

FILES: all in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\35903ed7-ff10-416d-84e9-b6c09675ab6b\scratchpad\r17-text\
- **Probe:** r17-probe-full.patch (counters, wrap log, stack capture, the `TEXT_MULTIRANGE` prototype; reverse-applied) and TextProbe.cs.r17.
- **Host:** host\Program.cs and host\TextHost.csproj, with the new `jcommit` mode (headless and real). Builds in host\jit and host\aot.
- **Scripts:** jc.ps1, run.ps1, batch17.ps1, aotpub.ps1.
- **Equivalence:** eq\ (round 16 step 1's probe); logs eq-full.log (tip) and eq-multi.log.
- **Runs:**
  - runs\jc-*.log and runs\aot-*.log (Junction scenes);
  - runs\a-*.log, b-*.log, m-*.log (round 16's app scenes, at the tip and against the multi-range prototype);
  - runs\*-jcommit-x-real.txt (real window).
- **Logs:** holds.log, batch-r17a.log, build-*.log.
- **Fixtures:** fx\jrepo (r16-apps clone), fx\jrepo2 (r16-text clone), fx\docs.
