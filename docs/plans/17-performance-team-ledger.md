# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 16
- **Phase:** build — round 16, step 5 next (step 1 landed 40cadb57, step 2 ec57c4ee + 5766a137, step 3 41ecec65, step 4 0d96e805 + b02b46d6 + 32fffce7 — the user's Narrator check of `r16b4ive` is open and the release waits for it; the survey merged 2026-11-18; 10 steps in "Round 16 build order"; questions
  66 and 67 answered 2026-11-18 with the recommendations (66 (a), 67 (b) with images first); build briefs `brief-r16b<N>.md` in this session's
  scratchpad, agent ids in `r16-agents.txt`). Survey: started 2026-11-18 in a fresh context (session b6ef64d9). Main had moved by 59 commits
  outside plan 17 (to 3038f82b: SkiaSharp 4.156 with HarfBuzzSharp 14.4 and the CPU surface in BGRA, SDL 3.5.0, the
  app team's fixes, `Element.OnLayoutChanged`, the software renderer); merged into the branch as cd699003 (clean).
  Round 14's Landed rows, round 15's build order, questions 62, 63 and 65 and round 14's log moved to the archive
  (`archive_r16.py`). Round 16's files: the common brief `brief-r16-survey.md`, agent ids `r16-agents.txt`, probes
  `r16-<area>\`, desktop lock `Global\HultUI-r16-desktop`, all in this session's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\b6ef64d9-4ed1-4793-9955-6a986d74de79\scratchpad\`. If
  the session dies, re-spawn only experts without a `17-reports/r16-*` report. Round 15's files (briefs, probes
  `r15-<area>\` and `r15b<N>\`, `fullsuite-r15.sh` with `fedora-r15.sh` — the model for round 16's full suite: Fedora
  runs Junction, Meridian, Aperture and Reel on their own, each in a private D-Bus session, as
  `HultUI.CrossPlatform.slnf` has none of them — `archive_r15.py`, `merge_r15.py`, `sr-variants\`) are in session
  8e8ba33b's scratchpad `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\8e8ba33b-db33-43fa-885a-cb6b82b7253f\scratchpad\`.
  **Round 16 builds a11y-gate-on-client (question 50: build) and a11y-items-on-demand-sooner (question 64: (a), Windows
  only)**; the prototypes are that scratchpad's `sr-variants\patches\gate.patch` and `items200.patch`. The screen-reader
  fix (a11y-keyboard-moves-silent, 9b7513d1..9550ecec, outside plan 17) is on main and released (junction-v1.28.0,
  vesper-v1.28.0, meridian-v1.11.0, aperture-v1.26.0, reel-v1.12.0 on 9df64bf6); the survey treats it as reviewed. Open
  for the user, not blocking: another F16 held-drag run (Waiting) only with a new approval. Outside plan 17: CI's Linux
  job runs no Junction, Meridian, Aperture or Reel tests; AT-SPI/Orca is unchecked for the screen-reader fix.
  To resume in a fresh context, run in this worktree: `/loop Run the next step of
  the performance team: follow docs/plans/17-performance-team.md, with state in docs/plans/17-performance-team-ledger.md.
  Read both once per session (afterwards only the ledger's Phase and the part the step needs), do the step the ledger's
  Phase calls for, record it in the ledger, and commit.` Before resuming, `git fetch` and merge main if it moved; build
  steps use `scripts/perf/step-ab.ps1`. Older rounds are in [the archive](17-performance-team-ledger-archive.md) (search
  it, don't read it whole). History note: rewritten 2026-10-25 (every commit after 3353ce47 has a new ID; a pre-push
  hook refuses pushes built on the old history).
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`)
- **Review commits since:** round 16's survey reviews round 15's build, 587f1767..75f2288a (the last survey report to the round 15 release; the one merge is round 15's own into main), and main's 41 commits outside plan 17 since that release, `git log --no-merges 75f2288a..3038f82b ^9550ecec` (42 with the ledger's cc093b50) (SkiaSharp 4 043f5b38, SDL 3.5.0 fd0cccb7, the app team's fixes; the screen-reader fix counts as reviewed). Earlier: round 15's survey reviewed round 14's build, 06d4db59..5b2985be (the last survey report to the round 14 release; the one merge is round 14's own into main). Earlier: round 14's survey reviewed round 13's build, 8f01b6fd..e432c224 (the last survey report to the round 13 release; its two merges are round 13's own into main). Round 13's survey reviewed round 12's build, 5f1744ca..afc1ed40 (the last survey report to the round 12 release; the one merge is round 12's own into main). Round 12's survey reviewed round 11's build, 5731ee58..b0dd1954 (the last survey report to the
  round 11 release; it includes the .NET 11 upgrade's step 0 only as already reviewed). Earlier: round 11's survey reviewed round 10's build and the .NET 11 upgrade, f4d6050d..868e2ffc
  (the last survey report to the upgrade's captures; round 10's merge into main cc55bdcc is in the range). Earlier: round 10's survey reviewed round 9's build, 4919ac40..facced69 (the last survey report
  to the round 9 release; 82 commits, 81 not merges; no merge from main this round). Round 9's survey reviewed
  eaa1ec29..10acc8c0 (round 8's build, with main's merge 3a15b5b1); round 8's survey reviewed 17d4220a..df45b4a2.
- **Probe code:** round 15 surveys in session 8e8ba33b's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\8e8ba33b-db33-43fa-885a-cb6b82b7253f\scratchpad\r15-<area>\`
  (the common brief `brief-r15-survey.md` and `archive_r15.py` beside them); round 14 surveys in session 41943a9b's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\41943a9b-f657-4483-971a-a2b346b967f7\scratchpad\r14-<area>\`
  (the common brief `brief-r14-survey.md` and `archive_r14.py` beside them); round 13 surveys in session 17c11f5e's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\17c11f5e-9de0-4477-8f58-0d0a5e006605\scratchpad\r13-<area>\`
  (the common brief `brief-r13-survey.md` and `archive_r13.py` beside them); round 12 surveys in session 0bab04ae's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\0bab04ae-e7a9-44cb-b4da-96d849a107f6\scratchpad\r12-<area>\`
  (the common brief `brief-r12-survey.md` and the archive script `archive_r12.py` beside them); round 11 surveys (and
  the upgrade's harness `r11u\`) in session ec6d577c's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\ec6d577c-f9ba-4479-acbd-908e67c0ea0a\scratchpad\r11-<area>\`
  (the common brief `brief-r11-survey.md` beside them); round 10 surveys in session 3484c586's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\3484c586-c631-4af0-957d-95e7a03cac41\scratchpad\r10-<area>\`
  (the common brief `brief-r10-survey.md` beside them); round 9's build-step probes `r9b<N>\` in session c484f5ac's
  scratchpad `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\c484f5ac-8f22-48dd-b669-13c24b1e7889\scratchpad\`;
  round 9 surveys in session c484f5ac's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\c484f5ac-8f22-48dd-b669-13c24b1e7889\scratchpad\r9-<area>\`
  (the common brief `brief-r9-survey.md` beside them); round 8's build-step probes in
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\64e7e0e2-bc1c-4069-9d42-9311fee04c9e\scratchpad\`; round 8 surveys in session 64e7e0e2's old scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\64e7e0e2-bc1c-4069-9d42-9311fee04c9e\scratchpad\r8-<area>\`;
  round 7 surveys (and builds) in session 855f4ddd's scratchpad `r7-<area>\`; round 6 surveys in session 855f4ddd's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\855f4ddd-feda-4f0a-a595-a83e07af08f8\scratchpad\r6-<area>\`
  (each report names its files). Rounds 4–5 in the scratchpad of session dc3c745c (a fresh context may have another
  scratchpad): `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\dc3c745c-a83d-4dc0-96ba-442a47e69937\scratchpad\`.
  Round 5 surveys `r5*/` (r5rt: R5Host walk/gc host; r5a11y: staleness audit; r5apps: app probe), round 5 builds
  `r5b11/`…`r5b14/`, the total comparison `total/` (part B: default-frame app hosts for both trees,
  walks.jsonl), round 4 `r4*/`.

**Repository move (user, 2026-10-24): HultUI moved to `github.com/fredrikhu/hultui`; the old
`hultui-revamped` will be deleted. Never push to the old URL again.** The new main had the same
history (d2bc157b). The shared `origin` was switched at round 4's release (2026-10-25: `git remote
set-url origin git@github.com:fredrikhu/hultui.git`, `git fetch --prune origin`). Open PRs/issues on
the old repo don't carry over. The repository is being prepared for open-sourcing: plan 17 adds only
HultUI and its apps, nothing outside them. **The new repo will be public and will
NOT get self-hosted runners (user, 2026-10-24: anyone could push code that runs on them); it uses
GitHub-hosted runners** (`ci/github-hosted-runners`, docs/CI.md; merged into main with round 4 at the
user's choice, 2026-10-25). Plan 17 never registers a runner. Hosted runners are slower (2-core while
the repo is private; Windows CI ~26 min) and differ in fonts and tooling: expect new CI precondition
failures like 187dbe1's, fixed in tests, not by adding runners.

**App releases:** cut only when the user asks (they asked after rounds 2 and 3; round 3's on
187dbe1: junction-v1.21.0, vesper-v1.21.0, meridian-v1.4.0, aperture-v1.20.0, reel-v1.5.0). Follow
memory `release-tagging` (≤3 tags per push).

## Verdicts

| Expert | Round | Satisfied | Review objections |
|---|---|---|---|
| 1. Layout | 16 | no | none (d43b3518: MeasureSize −34…−50 % a History key, equal bytes; 7c0a3b79: one static read a box write; c32c4b2e's two frames reach only resizes made inside posted work or a handler, never a user's drag). Real windows (2 holds): a wrapped view's first open builds again after its first frame (Vesper +1,085 KB, Junction +539 KB) → step 7; a `SetWindowPos` from another thread draws 2 frames on every grow step (a harness artifact: layout-wide-step-two-frames closed) |
| 2. SkiaSharp: painting | 16 | no | none (SkiaSharp 4: 0.0 KB render-phase bytes in every app frame but one gradient; pinned outlines never in an app; the software renderer 0 B a frame; round 15's frames hold). Real windows (2 holds, 9 launches): the dialog fade's window-sized layer is kept on GL (Junction DXGI +18 MB, private +55 MB) → step 5, question 67; History's moving gradient misses the shader cache on every key (0.2 KB a frame) → step 6 |
| 3. GL and Vulkan | 16 | no | none (SDL 3.5.0: rest, drag and start-up equal; expert 8's ~40 MB jump is NVIDIA's shader compiler on a per-exe disk-cache miss after the Skia update, closed). Real windows (4 holds, 33 launches): the dialog layer on GL and Vulkan (−15…−22 % GPU memory with a layer-free fade) → step 5; tween bursts grow NVIDIA's write-combined memory 2–6 MB a burst on GL → step 10 |
| 4. .NET runtime | 16 | yes | none (+16–24 B an element from 943610c7, 41e2775e, d43b3518: +3 % tree bytes, +0.3 % heap, below the bars; SDL3-CS and SkiaSharp 4 allocate nothing new; Kiota 2.1.6 −5.6 % a request). Real window (1 hold): round 15's memory wins hold; the big-file revisit is 32 MB a key, 92 % of it the cancelled colour pass → step 9 |
| 5. Text pipeline | 16 | no | f388b11d against 52852f6e: a kept wrap range brings back the "SAME DA" wrap once the shape cache has turned over (64/64 asks differ) → step 1. HarfBuzz 14.4: identical corpus hashes and app counts; cold words cost ≤ 1 ms once (Examined). Real window (1 hold): cold shapes equal headless within 0.1 % |
| 6. Reactivity and reconcile | 16 | no | c32c4b2e: a live frame inside a posted action's or a handler's batch draws before the build (posted steps 1.90 frames, a user's drag 0.95; no real-app frames today) → step 2; 033a900c: 0 host-against-host reconciles in the apps. Real windows (3 holds): Junction's wrapped diff builds the view on every width step (156–164 KB) → step 8 |
| 7. Accessibility bridges | 16 | no | none (the screen-reader fix: 5 raises and 18.3 provider calls a move, +0.08 KB; 9fcc1397: 0 Name raises a crossing). Real windows (3 holds, 17 launches): 0 root asks in 16 bridge-on launches; the decided gate and items-on-demand prototypes re-based and measured → steps 3–4, question 66 |
| 8. App workloads | 16 | no | c32c4b2e bisected (wrapped diff 0.95 → 1.90 frames a step in its posted-step harness; step 2); main's 41: no real-app regression (SkiaSharp 4's GL frames equal). Round 15 helped: History scene −63 % all-thread bytes, a save 22 → 7 processes, a held Shift 30 → 0 frames, Vesper's wrapped reader 279 → 19 KB a step. Real windows (4 holds, 53 launches); Jellyfin sign-in failed (`library=none`): Aperture's library not measured |

## Queued candidates

Rounds 1–15's build orders all landed but round 6's step 8, round 10's step 14, round 11's step 1 (c), round 14's
step 8 and round 15's step 8 (a), measured and declined, round 11's steps 13 and 14, round 13's step 5 (b) and
round 15's step 1, measured with nothing to change, and round 13's step 6 (c), not attempted (back to Waiting)
(rounds 5–15 are in [the archive](17-performance-team-ledger-archive.md#round-15-build-order), with each survey's closed-from-Waiting note).
Round 16's build order follows, from its survey.
A build step may bundle one expert's related small candidates as separate commits. Items marked **(user)** wait for a
decision in Needs the user; the rest of their step goes ahead.

### Round 16 build order

Ranked by expected effect in the real apps over cost (the brief's "Real apps first"); the review objections first, then
the user's decided items. Each step names the report lines its brief quotes (`r16-k` = `17-reports/r16-k-<area>.md`) and
measures its real-app case in a guarded real window (the lock `Global\HultUI-r16-desktop`, the harnesses in this
session's scratchpad `r16-<area>\`) as well as by step-ab. **Resize harnesses:** since c32c4b2e a `SetWindowPos` posted
with `InvokeOnMainThread`, or made from another thread, counts frames a user never gets (r16-1, r16-6); make resize
steps from a Win32 timer callback on the UI thread (`r16-reactivity\start\src\Script.cs`, `dragrelT`) or inside
`SuspendBatching`.

1. **text-wrap-range-one-line-tolerance** (5; r16-5 REVIEW and Candidate) — correctness, the objection to 52852f6e
   against f388b11d: a kept wrap range runs up to the text's own width T (TextPipeline.cs:547–552) while the one-line
   fast path accepts T ≤ width + `OneLineFitTolerance` (:503), and a Layout whose shape-cache entry is gone asks the
   range first (:492), so a label a float error short of its own width wraps again ("SAME DA"): 64/64 asks differ in
   the `tolcheck` probe. Keep T in the `WrapRange` and test the fast path's own expression in `KeptWrap`; a
   WrapRangeTests case that clears the shape cache. Small; no API; restores f388b11d's pixels.
2. **reactivity-live-frame-sets-aside-batch** (6; r16-6 REVIEW and Candidate 1, r16-1, r16-8) — separate commits. (a)
   `RenderLiveResizeFrame` sets aside the open batches, flushes what they held, and restores them, as `RunFrameCore`'s
   nested pass does (internal `SetAsideBatches`/`RestoreBatches`): posted resize steps 1.90 → 0.95 frames a step, 160 px
   steps 2 → 1, the build inside the size message; a user's own drag is unchanged (it runs from `PumpEvents`, outside
   every batch). Prototype `r16-reactivity\probe16.patch` (`HULTUI_PROBE_LIVE_SETASIDE`), `ProbeLiveBatchTests.cs`; with
   expert 8's suite proposal (g) as a gated UiTests guard (a wrapped-row list resized 21 steps from posted work draws 1
   frame a step). (b) App side, a correctness fix: Aperture's `SetFullscreen` (ApertureApp.Player.cs:261–268) and Reel's
   `ToggleFullscreen` (ReelApp.cs:210–215) write the player's fullscreen flag and `Rerender()` before
   `_window.SetFullscreen`, so a toggle's first frame shows the right controls (it draws the old ones today; the toggle
   stays 2 frames because of `ResizePending`). Count from code and headless; never toggle fullscreen on the user's
   monitor. Small; no API.
3. **a11y-items-on-demand-sooner** (7; question 64: (a), Windows only; r16-7 Candidate 2) — a virtual list over 200 items
   is served on demand on Windows; the Linux bridge keeps 2,000 (the value comes from the bridge through an internal
   member or a Window-side value, no API; today one Core const read at AccessibilityTreeBuilder.cs:355, :663 and
   Refresh.cs:242, :318). Real window at head → items200, bridge off in brackets: Junction start 2,220.9 → 1,848.3 KB UI
   (1,837.9), a ref move with History 1,315.5 → 599.0 (−54.5 %; raises 1,002 → 3, calls 13,186 → 199), Commit → History
   600.3 → 270.7, Vesper's navigation between two 1,000-file folders 505.0 → 294.0 (the first 1,013.3 → 319.9), viewer
   open 1,672.8 → 1,041.0. Prototype `...8e8ba33b...\scratchpad\sr-variants\patches\items200-fixed.patch` (applies at
   440ba012); 12 tests fail at 200 and need lists sized past it or a per-test threshold (r16-7 lists them). Test the
   risks r16-7 names: a served item's focus since the screen-reader fix (5 fresh providers a move; a rename of the
   focused served item is not told, e.g. History's row 0 when a commit lands), a list crossing 200 (a whole-tree build,
   ~1,100 nodes in Junction), a focused row element making a list eager, menus and dropdown popups over 200, Linux at
   2,000. With expert 8's suite scenario (e) as its guard (an insert above 1,000 item rows with UiaRig: 1,000 → 3 Name
   raises). Small–medium; no API, no pixels.
4. **a11y-gate-on-client** (7; question 50: build; r16-7 Candidate 1; **(user)** question 66 for `AccessibilityClients`)
   — build the tree and raise events only after a UIA client asks for the window's root. Real window: 0 root asks in
   16 bridge-on launches; with the gate and no client every number equals bridge-off (Junction start 1,837.9 KB UI
   against 2,220.9; a ref move 588.5 against 1,315.5; Vesper's navigation 293.8 against 505.0; its viewer open 39 % of
   UI bytes). Prototype `sr-variants\patches\gate-fixed.patch` (applies at 440ba012; 2 headless tests that never ask for
   the root need it asked: `A11yLiveResizeTests.Uia_ALiveResize_RaisesEachMovedElementsBoundsOnce`,
   `A11yLayoutRefreshInPlaceTests.UIA_ARenamedGroup_…`). Test r16-7's risks: an out-of-process client that only
   registers a focus handler (and Magnifier follow-focus, the touch keyboard on a text field, Voice Access), a root ask
   during a frame or a live resize, each window its own gate (dialogs, DevTools). Small–medium; no API, no pixels.
5. **Dialog fade without a window-sized layer** (2 with 3; r16-2 Candidate 1, r16-3 Candidate 1; **(user)** question 67)
   — `Dialog.Show` tweens the full-window scrim's `Opacity` (Dialog.cs:98–100), so 12–15 fade frames draw through a
   window-sized layer whose texture Skia keeps for the window's life: first palette open Junction GL DXGI +18.0 MB
   (64.6 MB at the end), private +40.7…+55.3 MB; maximized 4K +32 MB; Meridian +18 MB; Vesper +12 MB; Vulkan +19 MB.
   The layer-free fade (the scrim's colour tweened, the panel's own opacity in a layer of its paint bounds): DXGI
   +8.0 MB (−15…−22 % of the scene), private −15…−26 %. Probes `r16-painting\probe-r16.patch`
   (`PAINT_DIALOG_FADE=split|none`), `r16-gl-vulkan\pub\kfade`. With expert 8's suite proposal (i) as a gated guard of
   DXGI local after the first `Dialog.Show`. Small (option b) or medium (option e); no API; pixels during the fade.
6. **painting-gradient-shader-by-size** (2; r16-2 Candidate 2) — the gradient shader cache (SkiaRenderContext.cs:602–631)
   is keyed by the canvas rect, so Junction History's "Details →" gradient, which follows the focused row, misses on
   every key: real window 20 of 20 Downs, 0.2 KB a frame (the only render-phase allocation of a History key frame) and
   a native shader each. Key by size, draw translated: 12 → 3 misses over two walks, 0 of 44 frame hashes differ
   (`PAINT_GRAD_LOCAL`). Small; no API, no pixels; update PaintEffectCacheTests' counts.
7. **layout-wrapped-first-open-in-frame** (1; r16-1 Candidate) — a wrapped view's first open measures every line at the
   window's width (`BodyWidth` falls back to `_window.LayoutWidth`, CommanderApp.cs:3659; `RebuildViewerHeights`,
   CommanderApp.Viewer.cs:366), then `SettleWrappedRowWidths` (CommanderApp.cs:3588) rebuilds after the frame: Vesper's
   first wrapped open of TODO.md 14 builds and 2,272.5 KB against 11 and 1,187.0 unwrapped (real window 2,783 against
   1,017–1,090), one frame with wrong heights in 1 of 3 runs; Junction's first wrapped Commit view 7 builds and 2,213 KB
   against 3 and 1,674 (`SettleDiffScrollbar`, JunctionApp.Diff.cs:741). Move the width check into the body's
   `OnLayoutChanged` (7c0a3b79) and fit in place on the first measure (as `FitWrappedRowsInPlace` does on a resize).
   Expected: that open −48 % (Vesper), −24 % (Junction); O(document). Not prototyped. Small–medium; app code; no API.
8. **reactivity-junction-wrap-resize-in-place** (6 with 1; r16-6 Candidate 2, lead) — Junction's wrapped diff builds the
   view on every width step (real window, timer steps: 156–164 KB and 1 view build a 6 px or 160 px step) where
   Vesper's reader fits heights in place (10–16 KB, no build). Prototype first; mind the code labels' flex basis
   (`RebaseCodeLabels`, 7f3c9fd0), split mode and review cards; keep it only if it clears the churn bar (~−80…−90 %
   expected) with every settled frame equal to a full build's. Medium; app; no API.
9. **apps-junction-big-file-revisit** (4 with 6; r16-8 and r16-4 WAITING, r16-5) — on big files (bigrepo, ~675 KB each)
   the colour read and tokenize is cancelled by the next key at a 400 ms pace and nothing is kept: revisits 31.95 MB a
   key on all threads (first visits 32.02), GCs 26/9/5 per 12 keys; 2.60 MB a key is LOH strings (the two versions'
   text), the other 92 % the cancelled colour pass; one finished pass 255 MB in 3 s. Keep the tokenizer's progress or
   result for the next visit (the part that clears the churn bar, ~−29 MB a revisit), and the two versions' text if it
   pays. The text pipeline is warm on revisits (r16-5: 0 HarfBuzz calls). Probe `r16-apps\bisect\` (real time) with
   `r16-dotnet\loh\`; with expert 8's suite proposal (h) (all threads with `GC.GetTotalAllocatedBytes(precise: true)`,
   gen counts and LOH bytes on a real clock). Medium; app + Syntax; no API.
10. **gl-tween-burst-nonlocal-growth** (3; r16-3 Candidate 2, measure first) — on GL each burst of tween frames grows
   NVIDIA's resident write-combined memory: scrollbar hover visits Vesper +4.5 / +6.1 / +4.0 MB (DXGI nonlocal 16.0 →
   28.5), Junction +2.0 / +4.0 / +4.0; Vulkan +0.9, nonlocal +0; equal on every build. Run 50 visits in a real window:
   a fix only if it keeps growing (find what Skia or we hand the driver per burst); if it is bounded, record it as the
   driver's pool. Small to measure.

### Waiting

Re-ranked by the next survey.

- **live-reader check of menus** (user) — the six-step script in ACCESSIBILITY-PLAN.md (round 7 step 13) for Narrator,
  NVDA and Orca; a silent move would mean moving focus to the selected item (needs the user's approval).
  Round 16 (r16-7): settled for Narrator and NVDA by the screen-reader fix (`fixed4`: menu bar, context menu, a one-level submenu right in both); still unchecked: the dropdown step (A7), submenus deeper than one level (e28d72f6), light-dismiss (2603955a), Orca.
- **junction-changes-leads** (6/7/8, reader behaviour, no perf number) — only the caret is told to a reader, not a
  multi-selection's other members; a reader's "select" on a folder row clicks it, which opens or closes the folder. (The
  perf parts were examined by round 8: below the bars.) Round 13: still so (round 12 touched no selection pattern).
  Round 16 (r16-6, r16-7): unchanged; reader behaviour with no perf number, outside plan 17 (surveys stop re-ranking it).
- **junction-review-old-path** (app bug, not perf; round 7 step 8a) — the review composer sends GitLab `old_path` = the
  new path for a renamed file, because `GitFileDiff.OldPath` is never set (JunctionApp.Changes.cs:122, 124;
  GitRepository.cs:496, 511; `RenamedFrom` holds git's old path). Decide whether review positions on renamed files
  should carry the old path. Reproduces in round 13.
  Round 16 (r16-6): unchanged (nothing in 75f2288a..3038f82b touches `OldPath`); an app bug outside plan 17 (surveys stop re-ranking it).
- **dropdown-follow-control-on-resize** (1, correctness, low) — a Dropdown list or DatePicker calendar stays at its
  opening point when a resize moves its control; anchoring changes how a press on the control behaves. Not perf.
  Round 16 (r16-1): partly settled by 2603955a (a press on the custom frame's edge closes light-dismiss popups), but `FramePressedPending` is read only after the size loop, so the list still floats during an edge drag; keyboard snaps and app-made resizes keep it open.
- **junction-leads (remaining)** (tests) — `SelectConflict` (JunctionApp.Changes.cs:1477) and a conflict row's
  right-click have no test of their own (not perf; still so in round 13).
  Round 16 (r16-6): unchanged; tests, outside plan 17 (surveys stop re-ranking it).
- **vesper-brief-filter-clear-reveal** (1, not perf; round 9 step 10) — clearing a filter with Backspace leaves Brief's
  cursor column out of view (the scroll is clamped to the narrower filtered layout; the sideways reveal has no
  apply-after-layout form like the vertical one). The scrollbar half was fixed by round 10 step 15 (`BriefScrolled`,
  CommanderApp.Views.cs:616–625; r13-6).
  Round 16 (r16-1): `OnLayoutChanged` (7c0a3b79) on Brief's grid now gives the sideways reveal the after-layout form it lacked; small, not perf.
- **findbar-countonscreen-foreach** (4, nit) — `CountOnScreen` loops `foreach` over `bar.Children` (FindBar.cs:212; 32 B
  on AOT, 0.4 % of an 8 KB Enter), and `RewriteCount` (:317–319) calls it once a switch with the bar open; fix when
  FindBar is next touched.
- **textarea-chunked-document** (5/4; the user chose option B on 2026-10-25) — parked: Meridian's 89k body typing is
  ~238–241 KB/key, most of it the TextArea's new string; the user's 256k-character condition is unmet (round 13: still).
- **a11y-served-items-followups** — a live Orca check only (no desktop Orca here; optional).
- **gl-threaded-optimization-profile** (3; the user's driver setting, information only) — the walk lands while the
  window rests; the setting would remove only the per-frame spin (round 9: mean 357 JIT / 344 AOT, max 456/578
  `SwitchToThread` a paced frame; round 10: mean 378/376, max 723/644).
- **text-open-measures-every-line (full)** (5; user: not now, 2026-10-13). Round 13 (r13-5), stronger on real CJK text:
  a 1 px drag step re-breaks every wrapped row of the document (Japanese at 1440: 1,040 prefix tables, 119,899
  characters itemized and 84,495 fallback questions a frame; real window at 960 logical: 2,618 tables, 166,806
  characters) with ~40 rows on screen; step 3 removes the shaping and bytes, not this O(document) itemizing.
  Round 14 (r14-5): asked again as question 57 (zh at 1440 1,140 tables and 131k characters itemized a drag frame;
  the 154k-character file 2,178–5,288 tables and 9,900–13,550 HarfBuzz calls a frame; Arabic 1,846 BiDi probes a frame).
- **controls-menu-max-height-scroll** (1; not perf: no maximum height or scrolling), **layout-virtuallist-spacer-stale**
  (1; invisible; round 11 step 4: confirmed wrong but invisible, 209 stale spacer heights; marking the spacer needs its
  own proof).
- **aperture-poster-as-hero**, **aperture-tmdb-size-fallback**, **junction-narrow-gitlab-glyphs**,
  **meridian-pane-choice-not-persisted**, **meridian-min-width-wraps** (apps; not perf). Round 13 (r13-8):
  meridian-pane-choice-not-persisted reproduces (MeridianApp.cs:148, flipped at :340, never saved); the min-width wrap
  at 760×540 stands on Windows (46fccd88 keeps 448 / 1320); the others' code is unchanged.
- **syntax-shared-registry-rest** (5; parked) — Typst, Julia, PHP, Razor and HTML still use the shared registry (round
  11: `.typ` +42,419 KB kept); the user (question 42): no Typst files; parked.
- **fedora-local-dotnet** (environment) — once Fedora ships the final `dotnet-sdk-11.0`, remove
  `/usr/local/share/dotnet` and both links, `/usr/local/bin/dotnet` and `/usr/local/sbin/dotnet` (`which` picks the sbin
  one). Round 13 (r13-4): Fedora 44's updates have only 11.0.100~rc.3; `wsl-test.sh` follows the dotnet on PATH.
  Round 14 (r14-4): `updates` and `updates-testing` still have only 11.0.100~rc.3; both links still in place.
- **f16-held-drag-post-stall** (3; one survey stall, round 11 step 13's approved re-run clean) — cause unknown. Round 13
  (r13-3): the only F16 drag left is during HDR playback, where mpv posts every frame (the stall's precondition); a run
  needs an HDR clip playing on the 1440p at 200 Hz and a new approval from the user.
- **Suite scenarios** (8) — (a) an idle Vulkan window counting every thread's wakes: keep, low, only as a gated UiTests
  guard of round 12 step 5 (116 against 212 a second). (b) frames per key under a real loop and (c) an app-shaped shell
  resize: closed in round 13 (DiffFrameCountTests covers (b); (c) went with a11y-structural-walk-during-resize).
  Round 14 (r14-8) proposes: (d) a 75,000-character Chinese wrap resize, 1 px a frame, on real gettext prose at 960
  logical px past the word cache (guards 698c2758); (e) a11y insert above 1,000 item rows with a UIA client listening
  (UiaRig); (f) a gated UiTests guard for 4fdb56cb (a Vulkan window's DXGI local memory back within 2 s of 18 size
  changes).
  Round 15 (r15-8): make (d) fractional (1 physical px at 150 %, where step 1's objection shows) and add the
  154k-character realbig file for the past-the-cache case; (e) stands (a commit over History −52 % UI bytes with
  a11y off); (f) low.
  Round 16 (r16-8, r16-3, r16-4): (d) low; (e) medium, as step 3's guard; (f) low; new (g) a gated UiTests guard that a wrapped-row list resized from posted work draws 1 frame a step (with step 2), (h) an AsyncLoader per selection that allocates ~30 MB and is cancelled at 400 ms, all threads and gen counts on a real clock (with step 9), (i) a gated guard of DXGI local after the first `Dialog.Show` on a 2160×1350 GL window (with step 5).
- **findbar-count-not-announced** (7, reader behaviour, not perf) — a find bar's new "n of m" reaches the bridge as a
  Name change on the count label; a reader speaks it only if it moves there (no `Announce`, no live regions).
- **vesper-start-focus-event** (7, lead) — at start Vesper raises a focus-changed event for a window that never had the
  foreground (round 13: 5 of 5 launches), and in 2 of 5 refuses a leaf patch and builds a second tree (~53 KB), start
  only. Cause not isolated; not perf.
  Round 16 (r16-7): now in every app (the screen-reader fix's `NotifyChangesApplied` tells the first holder at start); the gate (step 4) removes it where no reader asks.
- **text-cjk-row-start-suffix** (5, lead; r13-5) — after step 3 the largest HarfBuzz source: a row-start word read off
  the line's word (Japanese 300 of 1,051 calls at 1000 px, 216 of 453 a drag; Chinese 201 of 1,165, 77 of 403). Needs
  the slice's own script guess and safe cuts at both ends. Medium–large; up to ~20–30 % of a new width's calls.
  Round 14 (r14-5): after step 1 (a) row-start segments are 88 of 951 calls (zh) and 118 of 681 (ja) at 1200;
  short words (under 8 characters) cut at need are the largest share (461 of 951); giving 2–7 character words their cuts
  in the tables halves a new width's calls but costs opening +248 KB (zh) / +121 KB (ja): a trade.
  Round 16 (r16-5): lower; the wrap ranges answer 88–94 % of rows (Vesper zh drag 39 calls a step at 1,440, 75 at 960; a new width 825).
- **junction-summary-overtaken-at-start** (6, lead; r14-6 REVIEW) — AsyncLoader drops a superseded result, so a
  working-tree callback during the first full summary restarts it: with a log appended every 0.4–1 s the first summary
  lands at 0.8–1.3 s (1.3–2.1 s on 2 cores) after 2–4 dropped loads, its remote/for-each-ref/rev-parse/describe run 3–4
  times; on a slower machine it can be overtaken as long as callbacks keep coming (round 13's CI failure). Coalescing
  measured worse (Examined).
- **vesper-local-waiting-text** (6, wording, optional; round 14 step 6, 1b019826) — a slow local folder's waiting block
  reads "Connecting to C:" (the share/server text); it could read "Loading <folder>" for a local folder, which also
  changes what the Ctrl+Shift+N review state shows and the test that checks it. The user's call; not perf.
- **vk-instance-extensions-once** (3; fix when Vulkan Init is next touched) — `HasInstanceExtension`
  (SdlVulkanHost.cs:532) enumerates on every call (8 `vkEnumerateInstanceExtensionProperties` calls at Init): one shared
  list saves 7.1–7.4 ms of start-up, below the 10 ms bar.
- **a11y-item-renames-unfetched** (7; question 64, with question 50's live check; r15-7 Candidate 2) — with UIA, an
  item node that has no wrapper yet raises no Name change (today `RaisePropertyChanges` → `TryWrapper` →
  `TryMakeItemWrapper` makes a wrapper and a native object only to raise): a Vesper navigation between two 1,000-file
  folders makes 1,003 Name raises and 13,052 provider calls every navigation (the first also keeps 1,000 wrappers and
  1,003 native objects, 372.4 KB); a Junction ref move with History shown 1,003 raises, 13,184 calls, 453.8 KB of
  1,426.8 UI KB (the old apps-junction-ref-move-a11y). UIA only: AT-SPI publishes every item. The renaming walk stays.
  Round 16 (r16-7): low once step 3 lands (at most 200 renames an event on Windows: Vesper 150 ↔ 250 files 154 raises, 2,028 calls, 42–65 KB a navigation); Linux needs Orca (AT-SPI clients cache every item).
- **a11y-served-item-one-provider-per-tell** (7; round 16 step 3, 41ecec65) — since a UIA list over 200 is served on
  demand, each told event on a served item makes fresh item providers (5 a key move; `GetFocus` a new `UiaVirtualItem`
  each call): a key move in Vesper's 1,000 files 1.7 → 3.0 KB a key with the desktop listener (Junction's History about
  the same), against −211 KB a navigation. One provider per item per tell (r16-7's estimate −0.36 KB a key; key moves
  only). Small; below the churn bar on its own unless a held key shows more.
- **junction-review-focus-colours** (6, correctness, small) — focus is lost when a review's colours land while a review
  row's control is focused (the landing remakes the rows: found by a32c8b9f's diagnosis); only in the first frames after a
  review opens. Keep the focused control's row, or move focus to its remade twin.
  Round 16 (r16-6): unchanged; app correctness outside plan 17 (surveys stop re-ranking it).

Closed from Waiting by the round 16 survey (→ Examined unless named): a11y-items-on-demand-sooner (→ step 3),
a11y-gate-on-client (→ step 4), apps-junction-big-file-revisit (→ step 9), layout-wide-step-two-frames (a harness
artifact: a `SetWindowPos` from another thread draws 2 frames on a grow step, a posted one inside a batch since
c32c4b2e; on the window's own thread every step draws 1), after-layout-callback (question 51: `Element.OnLayoutChanged`,
7c0a3b79, is that hook), a11y-commit-tab-trailing-frame (draws nothing since a86de0d1; gone under the gate when no
client asks), junction-chromeparttests-load (no recurrence in rounds 13–16), junction-gitwatcher-burst-test (closed
by f5bb3133: the debounce on a ManualClock, `Forget()` drops the warm-up callback).

## Landed

Round 15 (the round round 16's survey reviews; it moves to the archive at round 17's survey). Rounds 1–14 are in
[the archive](17-performance-team-ledger-archive.md#landed-rounds-14) ([round 5](17-performance-team-ledger-archive.md#landed-round-5),
[round 6](17-performance-team-ledger-archive.md#landed-round-6), [round 7](17-performance-team-ledger-archive.md#landed-round-7),
[round 8](17-performance-team-ledger-archive.md#landed-round-8), [round 9](17-performance-team-ledger-archive.md#landed-round-9),
[round 10](17-performance-team-ledger-archive.md#landed-round-10), [round 11](17-performance-team-ledger-archive.md#landed-round-11),
[round 12](17-performance-team-ledger-archive.md#landed-round-12), [round 13](17-performance-team-ledger-archive.md#landed-round-13),
[round 14](17-performance-team-ledger-archive.md#landed-round-14)).

| Round | Commit | Candidate | Before → after |
|---|---|---|---|
| 15 | 6c7a6cab (docs only) | text-junction-zh-fractional-drag (step 1, the review objection; measured: a one-time growth, no code change) | Bisected headless on NativeAOT, 1,418 → 1,378 physical px at scale 1.5 (945.3 → 918.7 logical), UI KB a step: all of round 14 52.9, none 42.1; without 5d9a5c71 only 29.2, without 2c809bc1 only 65.2, without c06aa102 only 52.9 (BiDi text only). The cost is 5d9a5c71's larger bound: the zh README first passes 8,192 words in this drag, so one step allocates 983 KB (960 KB the word cache's slot arrays growing 8,192 → 16,384); the other 39 steps 28.9 KB against 42.1. Whole widths show it too (59.6 → 64.5 KB) when the growth falls inside the measured steps; the survey's whole-width walk had it before them. Real window (NativeAOT, GL, the 4K at 150 %, 1 physical px steps, idle-gated, alternated ×2, 2 holds, 12 launches, 0 foreground takes), round 13's text → now, UI KB and UI-thread ms a step: zh first 40 steps 42.4 / 10.24 → 53.1 / 9.75 (29.1 KB without the growth step), next 40 39.8 / 10.30 → 25.9 / 9.92 (−35 %; GCs 1/1 → 0/0); ja 54.2 / 9.74 → 48.5 / 9.88, next 60.2 / 10.22 → 75.3 / 9.96 (51.3 without its growth step); the 154k file 5,374 KB / 39.4 ms → 69.4 / 17.1, next 5,533 / 40.2 → 117.4 / 17.8 (it grows once more, to 32,768 slots, 1,920 KB). Headless within 0.6 KB a step. A slot table that grows without copying would add a step to every lookup to save ~1 MB once a process: not built. The survey's `R15_OLDTRIM` switch ran the natural-word lookup twice on a miss; fixed in `r15b1\r15b1-probe.patch` before the bisect. DECISIONS.md "A full word cache takes every word" (rounds 14 and 15). No code changed, so no step-ab or test run. |
| 15 | b258f4fb, 9d40e558, 3ec73560, 5d2acc9e, ff6922f0 | painting-release-settle-now (the review objection to b62e22cf and 64c127a2) + painting-junction-row-press-frames + painting-modifier-key-frames (question 63: (a), each app) + painting-rte-number-labels | (a) A release with nothing marked settles hover or follows the scroll at once and asks for a frame only for a tint, a handler run or DevTools: headless Meridian row-release frames 10 (9 identical) → 1, walk release 6/6 identical → 0, Vesper right-click release 6/6 → 0; NativeAOT UI time Meridian release 1.15–1.21 → 0.18 ms, a whole click 3.39–3.46 → 2.54–2.61 ms (−25 %; its row move +8…+13 %, the release's leftover work moved there), Vesper release 1.86 → 0.01 ms; real window Meridian release frames 10 → 1, 174–177 → 158–164 Mcycles. Hover enter/leave handlers can now run at the release, before any frame. (b) The 14 right-click-only row press handlers are passive (13 Junction, 1 Meridian): History press frames 10 (10 identical) → 0, release frames 10; AOT a press 1.90–2.03 → 0.005 ms, a click 6.07–6.52 → 4.31–4.44 ms; real window 20 → 10 frames for 10 clicks, 186–189 → 158 Mcycles (3 identical press frames left on the Commit view's selectable diff text, round 14's declined case). (c) Each app's root key handler is passive and draws no frame for `KeyChord.IsModifierKey`, no framework change: headless all five apps Shift 5 → 0 frames, Ctrl 5 → 0, a held Shift 16 → 0; AOT a key-down 0.4–7.4 ms → ≤ 0.01 ms; real window Junction, Meridian, Vesper 5 / 5 / 31 → 0 frames both holds, a held Shift 89–102 → 19–20 Mcycles in Junction. (d) Numbered items' labels from a cache: editor click and caret frames 0.78 → 0.00 KB, typing 2.59 → 1.80 KB (AOT the same), time unchanged. Headless at 1.5, 120 Hz; timed runs NativeAOT, idle-gated, alternated, 2 passes of 2 pairs; real window the 4K at 150 %, 120 Hz, UIA listener on, 2 holds of ~122 s, 0 foreground takes, 0 nvlddmkm. Pixels: 0 misses in 451 forced-frame checks (base 176, 0); goldens pass. step-ab (e499d0e1) `-Pairs 2`: JIT pair 1 video bar fade +15 % and 2 faster, pair 2 0/0, A/A 0/0; AOT pair 1 six shelf walks +29…+33 %, pair 2 0/0, A/A 0/0; no allocKB moved, no pair-1 row reaches the changed code. Every new test fails under a reverse edit; b62e22cf's and 0da9f80e's guards still catch theirs. Windows Core 5,086, Headless 1,617, RichText 62, Fluent 34, Vesper 945, Meridian 680, Aperture 364, Reel 52, Junction 1,143; Fedora Headless 1,437. ELEMENTS.md (a release settles hover; a one-button handler is `OnPassive`; "A root key handler"), REACTIVITY.md's trap names modifier keys. |
| 15 | fdd795bd, cea60a27, 56da4346, 4047ce4d, bc2426aa | reactivity-junction-save-colours-cover (the review objection to 6a4c778d) + reactivity-junction-reveal-in-frame + reactivity-junction-save-git-set | (a) The early colours ask in `ReloadChangesView` reaches 8 lines past each hunk on both sides (sweep 0/3/8/16/32: 3 misses a line added 5 below a hunk and 4–8-line pastes; 8 covers every one-line edit that joins a hunk, 3 lines of context growing it by at most 7; 16 adds 8 tokenized lines to every in-place save): headless NativeAOT, a save that adds a line, frames, builds and tokenizes 2 → 1, UI 490–552 → 298–309 KB (−40 %), all threads 2,424–2,506 → 1,942–1,991 KB (−20 %), UI time 4.5–5.3 → 3.0–3.2 ms; in-place saves the same frames and UI KB, all threads +3…5 % (65 → 73 lines tokenized); a 12-line paste still 2 tokenizes; real window 3 → 1 frames, 309–358 → 187–208 KB UI, 2,261–2,332 → 1,833–1,880 KB all threads. (b) The Commit tab's and Changes' caret reveal also runs in `OnAfterBuild`, the after-frame call kept as the check: headless AOT frames a key at the edge, Changes (61 files) 2.70–2.75 / 2.00 / 2.00 → 2.00 / 1.00 / 1.00 at 400 / 100 / 33 ms (UI time −20…−45 %), Commit tab 2.90 / 3.00 / 2.05 → 1.90 / 2.00 / 1.15–1.30 (−25…−37 %), builds and KB unchanged; real window Changes 39 → 30 frames for 10 keys, 42–45 → 23 for 20 held, Commit 40 → 30 and 43–51 → 25–32. 4047ce4d (Core): `ScrollRangeIntoView` treats a band out of view by less than 0.01 as in view — on Fedora the file list laid out alternately 711 and 711.00006 high, so the check moved it 0.00006 and drew a second identical frame (1 key in 28). (c) A save runs 7 git commands, not 11: each list's two diffs are one (`--raw --numstat`), one `status` a generation is shared by the summary and Changes, HEAD's message is kept while the refs generation stands; a save's rows land ~180 ms sooner (headless 708–802 → 537–588 ms after the write with the debounce, real window 699–831 → 519–590), all threads −4…−6 %, UI KB and time unchanged; outside ref operations 19 → 16 commands. Real window: the 1440p only (100 %, 200 Hz; the 4K was not attached), 3 holds, 0 foreground takes; watcher events and `git status` sometimes came 0.7–1.8 s late on both sides (those saves left out). Watcher probe as the survey (files 29/29, 4 stale, 0 regressions; reftable 29/29, 5 stale, 0 regressions); outside-operations walk 48/48 views equal a load that shares nothing, before and after; every settled frame equals a full build's. step-ab (909e630e) `-Pairs 2` JIT + AOT 0/0 both pairs, A/A 0/0, no allocKB moved. Tests fail under reverse edits; Windows Junction 1,147 (at 4047ce4d one `GitWatcherTests.ABurstOfEvents` failure, 2 callbacks for 1, then 15/15 on 3 reruns), Headless 1,617, Core 5,087; ReviewTests + ChromePartTests on 2 cores 45/45 four times but in 2.7 / 7.8 / 19.0 / 4.6 min (gaps between the fixture's git pushes; base not run on 2 cores); Fedora Junction 1,146 of 1,147: `DiffRowKeepTests.TheHighlightLanding_MakesTheRowsOnScreenAgainInColour` fails, also at 909e630e. REACTIVITY.md: ask early for what will land; reveal in `OnAfterBuild` too; loads at one generation share a command run; keep what an event cannot change. |
| 15 | 17e13e63 | junction-diffrowkeep-fedora-race (not perf; found by step 3's Fedora run) | `DiffRowKeepTests.Hosted` counted rows 3 frames after the list showed; on Fedora git starts in a few ms, so the colours had landed first (31 plain rows, the tokenize 17–30 ms later); it failed on Windows too when run alone (4 of 4) and at 6ea0db3b. An internal `JunctionApp.HighlightHold` (`Task?`, null by default) the tokenize worker waits on after its rest; the test holds the colours, sees plain rows, releases them and sees them made again (a reverse edit fails 6 of 6 on both OSes). Fedora: the slnf 22/22; Junction 1,147, Meridian 680 (+4), Aperture 364 (+21), Reel 52, each alone in a private D-Bus session with an unlocked keyring. |
| 15 | a9321aac, add912d2, f2a9c654, 0322094b, 38629c1e | dotnet-junction-commit-pane-keeps-detail + dotnet-junction-patch-buffer-shared-pool (the review objections to 8b994a41 and c7dd2365) + dotnet-junction-git-second-process + junction-ctrl-d-kept-patch | Real window (NativeAOT, guarded, the 1440p only at 100 %, 200 Hz; 4 holds, 16 launches, 0 foreground takes; each change by its own probe switch, alternated ×2). (a) The Commit tab lets go of the last commit's diffs when another detail lands: heap after a full GC back in History 28.13 / 32.17 → 25.27 / 25.47 MB (the 6.04 / 6.06 MB base freed only on the next Commit-tab build is never held), LOH and GC counts the same. (b) `RunUpTo` rents from a pool of its own, not `ArrayPool<char>.Shared`: after 120 History keys heap 17.00 / 19.74 → 15.12 / 18.58 MB, LOH 2.57 / 3.07 → 1.57 MB; a deterministic probe (120 commits on 4 threads) kept 4.9–5.9 → 2.0 MB, allocated 18.5 → 15.6 MB first pass and 13.6 MB both sides warm. (c) The second process was conhost.exe, a child of git.exe (`CreateNoWindow` from a GUI app still gives a console program a hidden console): `ProcessStartInfo.StartDetached` (new in .NET 11, DETACHED_PROCESS) on Windows only (on Unix it starts a new session): 22.1–22.4 → 14.3–14.4 ms a git run (median, −35 %), 2.00 → 1.00 processes a command (868 for 434 → 411 for 411), start-up 52 → 26 processes; stdout, stderr, stdin, exit codes, kill-on-cancel and the job object as before; a run given no stdin reads an empty input instead of the app's. Together: a big-commit walk then History 32.18 → 18.2 MB after a full GC; a 120-key History walk at a reading pace heap 18.4 / 25.0 → 10.1 / 12.0 MB, all threads 143 / 165 → 64 / 70 MB, GCs 12–13/6/4 → 6–7/3–4/3 (mostly (c) by timing: the same 183 detail reads, colour `git show`s 29 / 26 → 6 as fewer details land after the next key). (d) Ctrl+D during a first visit checks the selected file's own diff, or waits for its patch and runs when it lands (two headless tests). Order effect: within each pair the later launch allocated more on its History walk, whichever side it was: alternate sides between holds. step-ab (5d589919) `-Pairs 2`: no row slower in both pairs on JIT (A/A 3 slower); AOT "a11y/tab among 10 fields after 2,000 rows, AT-SPI" +12–13 % both pairs with the suite's source identical on both sides (noise), A/A clean; no allocKB moved. Five new tests fail under reverse edits; Windows Junction 1,152, Headless 1,617, Core 5,087; Fedora Junction 1,152 twice (alone, private D-Bus, keyring). REACTIVITY.md (a view's kept state goes when its owner is replaced), NATIVE-AOT.md (`ArrayPool<T>.Shared` keeps arrays on every thread). |
| 15 | dae30194, 59609522, c32c4b2e, 49c7b384 | reactivity-posted-drain-bound + question 62 (b) (a framework user's report; the user's request) | (a) A drain pass runs at least one posted action, then stops at half of `DisplayHz()` read on the platform's `TimeProvider`; order kept; a throwing action leaves the rest queued; `_liveFrameQueued` is the count left; the WinForms `HostNextFrameIn` path needed no change. (b) Each pass runs in one reactive batch under `AutoBatch`, flushed before animations, layout and render; what the flush's rebuilds post runs in the same pass while the budget lasts; after a throwing action a throwing build in the flush is dropped so the action's exception propagates; a pass started inside an open batch flushes it and sets it aside for the pass (internal `ReactiveRuntime.SetAsideBatches` / `RestoreBatches`, no public API), which also fixes a click handler that runs a nested loop. The report's repro: 100 posts 100 → 1 build (`PostedWorkTests`). New PerfSuite rows (dae30194, `Op.Detail`), JIT, idle-gated, 2 runs identical, base → (a) → (b): a burst of 500 with 20 ms builds 1 frame of 10,000 ms with 500 rebuilds → 500 frames of 20 ms → 1 frame and 1 rebuild drawn at 20 ms, UI time 11.75 → 89.5 → 0.17 ms, 106,767 → 235 KB; a 50 ms stream for 2 s with 60 ms builds 1 frame of 2,400 ms (40 rebuilds) → 40 frames → 34 frames of 1 rebuild, longest 60 ms, last change at 2,040 ms; a 5 ms stream with 20 ms builds 1 frame of 8,000 ms (400 rebuilds) → 400 frames, last change still at 8,000 ms → 101 frames, longest 20 ms, last change at 2,020 ms, 85,413 → 22,956 KB. Real window (NativeAOT, the 1440p only at 100 %, 200 Hz, 2 launches a side, 2 holds, 0 foreground takes): a 5 ms stream for 1 s 200 builds in one 4,035 ms frame with a key handled ~3.7 s after its push → 47 builds in 47 frames at 45 fps (median 21.5, longest 37 ms), the key handled 4–7 ms after; a 50 ms stream one 2,460 ms frame → 15.5 fps, the key 8–9 ms after; Aperture Home (7 posters) builds 7/7 → 6/7, frames 4 → 4. tests/ audit: no test reads a rebuild after a posted write; Aperture `PartTests.ABuildALoaderAsksFor_BuildsTheParts` counted two loads landing in one pass (2 builds, 5 part builds → 1 and 3; it asserts the single build now). step-ab (85868653) `-Pairs 2`: JIT a 6 µs wheel row (0.0057 → 0.0066 ms) in both pairs and the bar fade in pair 1, AOT the six shelf walks +37 % in pair 2 only, A/A 0/0, no allocKB moved; a filtered re-run of every flagged row 0 slower in all four pairs (noise). Reverse edits fail their tests ((a) 4 tests and the live-frame count; (b) four edits). Windows every project (`dotnet test HultUI.slnx -c Release`, 9 min) pass; UiTests opted in (LiveResizeFrame, SdlLoopPass, FramePacing) 14; Fedora the slnf plus Junction 1,152, Meridian 680, Aperture 364, Reel 52. REACTIVITY.md, THREADING.md, HEADLESS.md, the PerfSuite README. |
| 15 | 29f1ddc0, 7f3c9fd0, b71c57de, 2d5d96ee | layout-vesper-wrap-resize-no-build + layout-junction-wrap-keep-rows + junction-review-cards-resize-stale | NativeAOT probe host, base ad5306de against after, alternated ×2, idle-gated; real window the 4K at 150 %, 120 Hz (2 holds, 0 foreground takes). (a) Vesper's wrapped reader and compare view keep their rows and build nothing on a resize (`FitWrappedRowsInPlace`; with a Brief or Thumbnails grid behind it still builds): headless per 4 px step reader on TODO.md 280.9 → 24.3 UI KB, 6.50 / 6.59 → 6.24 / 6.16 Mcycles, builds 1 → 0, rows made 37 → 0; height-only 61.9 → 2.3 KB; aaa-long.txt 301.6 → 93.1 KB; compare view 698 → 297 and 428 → 137 KB, 12.97 / 13.02 → 11.86 / 11.83 Mcycles; real window reader 280.9 → 24.3 KB, 1 px physical 274 → 17.6, height 79 → 2.7; compare 1 px 673 → 266 KB, 19.24 → 17.15 Mcycles; 1.06 frames a step both sides. (b) Junction's wrapped diff keeps its rows over a width change, and kept rows' code labels get the new flex basis (`RebaseCodeLabels`; the prototype's old basis left a one-token line 688 px past its row, an existing DiffWrapTests case; a zero minimum instead made Changes split worse, 465 → 779 KB): 400 px drag (100 steps, 405 rows) 399.6 / 391.9 → 276.8 / 269.2 KB a step with no layout-cache miss (text measures 9–13 KB a step both sides); headless Commit 279.5 → 152.6 KB (rows 18 → 0), splitter 258.9 → 132.1, split 283 → 190, Changes 437 → 277, Changes split 465 → 371; 1 build, 1 frame a step either way; cycles −2…−7 % except Commit split splitter +6.5 % headless (raster render), −15 % in the real window: kept; real window 282.8 → 154.5 KB, splitter 259 → 132 KB, rows 18 / 11 → 0 (width steps drew 2 frames a step on both sides at 16 ms pacing; not chased). (c) A review's cards are measured again on a resize with lines unwrapped: 0 → 1 view build a resize step while reviewing; the card's row 363.5 px around a 318.3 px card now fits; a splitter step 2 → 1 frames. Pixels: headless 1× and 1.5×, Vesper 132/132 and Junction 146/146 steps a scale byte-identical to base, scroll offsets equal, the first frame equal to the settled one, 0 stale heights. step-ab (ad5306de) `-Pairs 2`: JIT 0/0 both pairs (A/A video bar fade −15 %); AOT pair 1 virtual/selection walk +11 %, pair 2 none, A/A none; no allocKB moved. New tests fail under reverse edits; Windows Vesper 947, Junction 1,153 of 1,154 (`TheKeyboardReachesTheCommentAgain`, 3/3 alone), Headless 1,631, Core 5,087; Fedora the slnf (Vesper 929) and Junction 1,154 (private D-Bus). LAYOUT.md. |
| 15 | 52852f6e, 83034345 | text-wrap-width-range (plain path; the BiDi loop untouched) | A wrapped plain layout is kept for the widths that break it into the same lines, lo and hi taken from `BreakIntoLines`' and `SplitByCharacter`'s own decisions, and a Layout inside that range is answered with it; a memo bounded at 4,096 entries and 16 MB; `KeepsWrapRanges = false` (internal) is the reference. Vesper's wrapped reader, headless NativeAOT, base db8080c3, alternated ×2, idle-gated, 1 frame a step, 1 px steps at 1,440: the 602k-character archive 2,181 → 44 KB (−98 %), 21.9 / 20.8 → 3.6 / 3.6 ms; zh 133 → 31 KB, 5.4 / 5.2 → 3.0 / 3.0 ms; ja 126 → 58 KB, 4.4 / 4.3 → 2.8 / 2.8 ms; TODO.md 19.6 → 13.8 KB, 2.19 / 2.09 → 2.02 / 1.93 ms; 4 px steps archive 2,187 → 111 KB (21.5 → 5.2 ms), zh 138 → 69 KB; the archive's shape-cache misses 1,566 → 3 a step. Real window (the 4K at 150 %, 1 hold, 8 launches, 0 foreground takes): archive 1 physical px 2,179 → 35 KB, 21.5 / 21.1 → 2.9 / 2.6 ms, 4 px 2,182 → 78 KB; zh 1 px 131 → 24 KB, 5.4 / 5.5 → 2.6 / 2.5 ms, 4 px 135 → 50 KB. A whole new width costs the same (archive resize 23.1 / 22.9 → 23.4 / 23.0 ms); opening +22 KB (archive), +87 KB (zh); Junction's Files view and Meridian's response body the same KB in every phase, times within noise. Rows kept a 1 px step: archive 93.8 %, zh 91 %, ja 92 %, ko 92 %, TODO.md 95 %. Retained after a full GC: archive 16.9 → 16.5 MB, zh 13.5 → 13.7 (the memo 5.8 MB, 403 entries, on the archive). Equivalence: 7 documents × 5 styles × 3 modes × 47 drag widths, 5,038,170 layouts, 0 differ; 36,144 kept ranges probed at their ends: 108,432 widths inside answered by the kept layout and equal, 72,288 just past the ends laid out again and equal; in the suite Skia and Text `WrapRangeTests` on a trimmed corpus and `CacheBoundTests` (10 of 12 mutants caught; the 2 survivors redundant). Corpus hashes unchanged (48k 7A18AF8DE8E05271; 240k F09E9038726E4BD6; CJK wrap 0 of 1,344,616, 1E6F1A93894D0B6A / 6C7FA51792E8EB84). step-ab (db8080c3) `-Pairs 2`, rerun from the start after a Windows Update restart: JIT and AOT 0 slower both pairs; faster both runtimes and pairs: text/wrap resize −30…−37 %, richtext/open −45…−53 %, richtext/resize −26…−31 %; A/A JIT none, AOT shelf walks −44 % (noise); layout/cards create 300 +5 % allocKB (2,480 → 2,604, the memo filling in a fresh session; +0.3 % over 200 steps once full). Seven existing tests about breaking at each width opt out with `KeepsWrapRanges = false`. Windows Text 306, Skia 424, Headless 1,631 (goldens), Vesper 947, Meridian 680, Junction 1,154; Fedora the slnf (Text 306, Skia 424, Headless 1,451, Vesper 929), Junction 1,154 and Meridian 680 (+4), each in a private D-Bus session. DECISIONS.md, ELEMENTS.md. |
| 15 | eb68bbca (docs), 1542fcb8, da63c3d0 | media-interop-context-release (measured, declined) + media-player-cycle-growth (measured; ours fixed) | Reel on Vulkan, an Aperture-shaped dispose, the 1440p only (100 %, 200 Hz, HDR on); 11 desktop holds, 0 foreground takes, 0 nvlddmkm, no stall over 1 s. (a) Releasing the interop's hidden GL context once the last source gave its image back, 3 launches kept / 4 released: after the dispose private 155.9–158.2 → 147.6–149.7 MB, working set 147.3–149.6 → 147.2–148.3, DXGI local 44.7 → 38.4 MB; the next video slower: interop ready 0 → 29–30 ms, Open → first video frame 70–78 → 96–107 ms, Open → first window frame 63–66 → 86–96. The survey's ~40 MB private / ~17 MB resident was everything libmpv, CUDA and the driver leave after a first video; the context itself holds ~9 MB of commit and < 1 MB resident. Declined (DECISIONS.md eb68bbca): ~30 ms on every later video, and a safe release needs every GL user to register first (Aperture makes the next episode's mpv context before the old image is freed). (b) 10 open/dispose cycles, growth a cycle private / working set: Vulkan +1.07 / +1.23 and +1.02 / +1.10 MB, GL +1.70 / +1.97 and +1.36 / +1.39, `gpu-hwdec-interop=no` +0.88 / +0.91; managed heap flat at 2.8 MB, threads 27 → 21, process heaps no trend, GPU memory flat; all of it in one 1 GB reservation beside the GC's range that is NVIDIA's (it does not move with `DOTNET_GCRegionRange`; 31.9 MB on Vulkan / 54.8 on GL before any video); it stops with no player, a fake renderer through our interop path, mpv's software renderer, and a fresh GL context per video: recorded, not ours (DECISIONS.md). Ours, fixed (1542fcb8): the GL render context kept a wrapper per GPU source and never let it go, so every disposed player's sink stayed reachable; the GL surface now handles `ReleaseFrameTarget` and drops the wrapper at the next frame: managed growth a cycle +0.22 → 0 MB with a fake pixel-frame backend, +0.02 → 0 with mpv; open timings unchanged (first window frame 52–64 → 48–77 ms in one hold). step-ab (f4ad97a3) `-Pairs 2`: JIT 0 slower both pairs (pair 2 one faster, noise), AOT 0/0 both pairs (a11y/selection walk AT-SPI 29.3 → 26.7 KB allocKB both pairs), A/A clean. Two new tests fail under reverse edits (`GpuSourceWrapperReleaseTests`, `GpuFrameSinkGLTests.ADisposedPlayer_IsNotKeptByTheWindow`); Windows Media 87, Media.Backends 30, Platform.SDL3 64, Rendering.Skia 426, Reel 52, Aperture 364 (+21), real-window UiTests (Vulkan probe, GL sink, mpv GL, GPU source GL) 47/47; Fedora the slnf and Aperture and Reel each in a private D-Bus session, 0 failed. |
| 15 | 9fcc1397 | a11y-junction-compact-chrome-unnamed (correctness) | Junction's compact chrome (under 1,406 logical px) keeps its icon-only buttons' names: '' → "Search & commands Ctrl+P", "Overview 1" … "Files 7", "Fetch", "Unified", "Split", "Wrap", and the same bug fixed in Tree/List (Branches, Tags) and "Only main" (History); buttons named by their count alone ('1') → "Changes 4 1", "Pull 1", "Push 1" (the count moved into `ActionButton`, so a running Pull reads "Pulling…" on both sides); every name equals the labelled form's. Name raises a crossing (1,440 ↔ 1,400): History 9 → 0, Commit 11 → 0, Changes 11 → 0, Branches 10 → 0; on the real bridges UIA name raises 9 / 11 → 0, AT-SPI signals 66 → 57 and 72 → 61; structure and bounds raises unchanged; the reader's tree equal to a fresh build after every crossing. Event streams 117/117 identical; 10 headless Junction frames byte-identical. `CompactChromeNameTests` (4 cases) fail under a reverse edit. Windows Junction 1,158, Headless 1,631; Fedora Junction 1,157 of 1,158 — `ReviewTests.AResizeMeasuresTheCardsAgain_WithLinesUnwrapped` (b71c57de's test) fails intermittently on Fedora only (3/3 at da4f93c6, 1/3 at 52852f6e, 2/3 at 9fcc1397: a splitter drag draws 3 frames, 1 expected), older than this step. ACCESSIBILITY-PLAN.md rule. No step-ab (names only). |
| 15 | 1d852da7, a32c8b9f, 37b9e96b | junction-reviewtests-races (tests only; found by steps 6 and 9) | `ReviewTests.AResizeMeasuresTheCardsAgain_WithLinesUnwrapped` (Fedora, 3/3 · 1/3 · 2/3): opening the review fetches the request's head, and the watcher's reload landed 300 ms later inside the first splitter step on Fedora (git starts in a few ms): one build from the drag, then `GitWatcher.OnDue` → `OnExternalChange` and five loaders landing added 2 builds and frames; b71c57de draws no extra frame. `ACaretMove_MakesTheRowsItMovedBetween_AndKeepsTheRest` (Windows 2 of 6: "20 rows made") and `TheKeyboardReachesTheCommentAgain` (once in step 6): the diff's colours landing (the highlight worker's posted result) remade the rows mid-key (2 rows → 18–22; the control Tab had just focused was in a remade row). Fixes: the shared `Hosted` disposes the watchers before any test body (37b9e96b, replacing 1d852da7's single-test copy); every test opens the review through a shared `OpenReview` that waits for the review, `HighlightsPending == 0` and no builds for a while (a32c8b9f). The class 5/5 on Windows, 5/5 on 2 cores (affinity 0x3), 5/5 on Fedora; before the colours fix 5 of 14 runs failed, under its reverse edit 3 of 6; Junction.Tests 1,158 on Windows and Fedora. `GitWatcherTests.ABurstOfEvents` is a different cause, not reproduced (Waiting). |
| 15 | c6a19b06, 870a6fc7 | junction-history-highlight-prefetch (question 65: (b); the user looked at before/after images and said "Commit it") | History's rest warms the Commit tab's colours only once the Commit tab has been opened in this session; `PickInitialFile` also skips the prefetch when the detail landed after the selection moved (the key rides in the closure the load already makes, 0 B). Real window, NativeAOT, jrepo, 2 pairs, alternated, idle-gated, all-thread KB a key: 250 ms, 80 keys, Commit tab never opened 541 / 555 → 403 / 403 (−25…−27 %), GCs 3/1/0 → 2/1/0, git 4.04 → 4.00 a key, tokenizes 2 → 0, UI 31.1 → 29.5 KB a key; 400 ms, 60 keys 4,104 / 4,702 → 440 / 440 (−89…−91 %), GCs 16/5/1 and 18/5/1 → 2/1/0, git 5.50 / 5.58 → 4.00, tokenizes 57 / 60 → 0; 250 ms after the tab was opened 1,100 / 1,105 → 924 / 925 (the late-landing skip: 3 of 40 loads); headless JIT 527 → 410, 3,436 / 3,534 → 404, with the tab opened the same both sides (772); 150 ms with the tab opened 37–43 of 80 loads late, tokenizes 14–15 → 1, 693 / 699 → 515 / 507 KB. The survey's 3.13 → 0.90 MB at 250 ms did not reproduce at 0bb65f5a (the prefetch seldom passes its two 120 ms rests at 250 ms now). The first switch to the Commit tab in a session: the first coloured frame 11.4 / 5.9 ms in 1 frame → 96.6 / 95.6 ms in 2 frames (the plain diff at 6.8 / 7.3 ms); later switches 2.5–3.5 ms both sides; coloured frames byte-identical. step-ab (0bb65f5a) `-Pairs 2`: no allocKB moved; JIT pair 1 two virtual-list rows −17…−19 %, pair 2 none, A/A none; AOT none both pairs, A/A video bar fade +16/+17 % (noise; the suite does not run Junction). `CommitPrefetchTests` (2) fail under their reverse edits; 6 tests in 3 files that waited for History's colours now open the Commit tab once first; Windows Junction 1,160, Headless 1,631; Fedora Junction 1,160 (private D-Bus). |
| 16 | 40cadb57, 8c6d45de | text-wrap-range-one-line-tolerance (correctness; the review objection to 52852f6e against f388b11d) | `WrapRange` also keeps the text's own width T (NaN with a newline), and `KeptWrap` refuses any width where `T <= maxWidth + OneLineFitTolerance`, the one-line fast path's own expression; one float a range, no API. Probe `r16b1\tolcheck` (8 strings × 2 spacings × 4 ε, wrapped once at T − 2/256, then the shape cache cleared): 64/64 asks differ → 0/64; edge sweep (1,984 asks, 49 floats around T − 1/256 and 13 widths from T to T − 0.01, cache cold and warm, bit for bit) 514 → 0; the widths just past the edge still answered by the kept wrap (896). Round 15's real-document equivalence (5,038,170 layouts): 3 → 0 differ — three archive layouts in sans 13 at 667.99976 px with text 668.00195 px wide, 2 lines kept where a fresh layout gives 1: the bug was on real text at the base. Kept answers 1,430,056 → 1,430,053. Corpus hashes unchanged (48k 7A18AF8DE8E05271; 240k F09E9038726E4BD6; CJK wrap 1E6F1A93894D0B6A / 6C7FA51792E8EB84, 0 of 1,344,616). step-ab (031ae90f) `-Pairs 2`: AOT 0/0 both pairs, A/A 0; JIT pair 1 six untouched rows slower (grid auto-column, five a11y rows), pair 2 none, A/A none; wrap, resize and rich text rows −3.0…+1.1 %; the two "type in a rich text editor inside a VirtualList row" rows +0.9 KB on JIT only, both pairs (JIT object stack allocation: filtered both 2.4 / 2.0, with `DOTNET_JitObjectStackAllocation=0` both 3.8 / 3.5, 0 of 235 rows moved). `WrapRangeTests.AKeptWrap_LeavesTheWidthsAFloatErrorShort_ToTheOneLine` fails under a reverse edit; Windows Text 311, Skia 457, Headless 1,733 (goldens), Vesper 951 (+4 skipped); Fedora Text 311, Skia 457. No real window (no frame or memory claim). The verdicts are in the captures commit (step-ab copies after the change commit). |
| 16 | ec57c4ee, 5766a137, 84106f2b | reactivity-live-frame-sets-aside-batch (the follow-up to c32c4b2e) + Aperture/Reel fullscreen order (correctness) | (a) `RenderLiveResizeFrame` sets aside the reactive batches open around it, flushes what they held and gives them back (internal `SetAsideBatches`/`RestoreBatches`, as `RunFrameCore`'s nested pass does): a resize made inside posted work or a handler builds in the size message's frame. Real window (NativeAOT Junction, jrepo's 8th commit in the Commit view, wrap on, the 4K at 150 %, 120 Hz, GL, 2 launches a side alternated in one 80 s hold, idle gate passed, 0 foreground takes): posted 6 px steps 1.90 → 0.95 frames a step (40 → 20), held writes 20/20 → 0/20, bytes inside `SetWindowPos` 9.3 → 162.7 KB, UI KB a step 163.5 → 162.7; posted 160 px steps 14 → 7 frames (~1,274 KB both); timer steps (a real drag's context) 0.95 / 0.88 frames and 156–162 KB a step on both sides — a user's own drag unchanged. A build that throws inside the size message: that frame is not drawn, the resize stays pending, the batches are given back; SDL's event watcher logs and swallows it (before, a posted resize's throwing build reached `RunFrame` and `Run`; now it is logged as any live-frame build is), and the next size event draws the new size. (b) Aperture's `SetFullscreen` and Reel's `ToggleFullscreen` write the player's flag and rebuild before `_window.SetFullscreen`, then read `Window.IsFullscreen` back (a refused toggle showed the wrong glyph in Aperture before): the frame drawn inside the toggle shows "Exit full screen" (and back), Aperture builds once a key instead of twice, a toggle stays 2 frames; headless only (never toggled in a real window), through `HeadlessOptions.WindowsFrame`'s `SetFullscreen` now drawing its first frame inside the call. step-ab (f8db7335) `-Pairs 2`: JIT and AOT 0 slower, 0 faster both pairs (A/A JIT keyed rows −11 %, two a11y allocKB rows); the posted-burst rows unchanged. Every new test fails under a reverse edit (the no-batch guard passes either way by design); the gated UiTests guard (expert 8's (g)) draws 21 frames for 21 posted steps (reverse edit 21 + 21). Windows Core 5,118, Headless 1,738, Junction 1,161, Aperture 369 (+21), Reel 53, Meridian 680 (+4), UiTests LiveResizeFrame/SdlLoopPass/FramePacing 16/16 (the fullscreen UiTests not run); Fedora the slnf 22/22, Junction 1,161, Aperture 369, Reel 53 (private D-Bus). REACTIVITY.md, THREADING.md, HEADLESS.md (write what the new frame shows before a call that resizes the window, then read `IsFullscreen`). |
| 16 | 41ecec65, 9c21b2b6 | a11y-items-on-demand-sooner (question 64: (a), Windows only) | Each bridge reports its eager-items cutoff through an internal `IEagerItemsLimit` (UIA 200, AT-SPI 2,000, any other 2,000); the window reads it when a bridge is installed and builds, patches and refreshes by it; HeadlessUiaReader, UiaRig/AtSpiRig and 25 test wrappers pass on the wrapped bridge's number. Real window (NativeAOT, the desktop listener on, the 4K at 150 %; 5 holds, 24 launches, 0 foreground takes, 0 nvlddmkm; repeat launches identical), UI-thread KB: Junction start 2,212.3 → 1,835.7; Commit → History 600.0 → 270.2; a ref move with History 1,314.7 → 598.8 (−54.5 %; Name raises 1,002 → 3, provider calls 13,186 → 199; kept wrappers / natives 1,086 / 1,038 → 86 / 38); Vesper start 951.5 → 603.5, navigation between two 1,000-file folders 504.7 → 293.7 (the first 1,029.0 → 334.6; raises 1,003 → 3, calls 13,052 → 52), viewer open 1,664.1 → 1,041.4, close 794.7 → 718.3. Slower: a key move in a served list (fresh providers each tell) Vesper's 1,000 files 1.7 → 3.0 KB a key, Junction's History about the same (Waiting a11y-served-item-one-provider-per-tell). A rename of the focused served item is told as one Name change on that item (a real UIA client heard it once), also at a 200 crossing; Vesper 150 ↔ 250 crossings build the tree whole once, focus kept by id with no focus event, 382.7 / 371.2 / 358.2 / 326.6 KB against 438.2 / 412.6 / 335.2 / 362.5, raises 153–154 → 4, calls 2,028–9,695 → 78–106; an open inline editor keeps a 300-item list eager; a 201-entry menu is served. Also fixed: `GetItemLabel` on item rows sized a kept-label array to the whole list on the UI thread (24 KB at every return to History, 480 KB at 20,000 items): internal `IAccessibleItemSource.PeekItemLabel`. Guard (expert 8's (e)): suite row `a11y/insert above 1,000 item rows, UIA` 0 Name raises a step, 0.13 ms, 72 KB (with UIA at 2,000: 1,000 raises, 8.7 ms, 158 KB); the AT-SPI row at 1,999 items 1,999 name signals. Event streams 114/117 identical (the 3 are UIA lists over 200); every AT-SPI stream and list under 200 equal. step-ab (0e8bb81a) `-Pairs 2`: JIT 0 slower both pairs, `selection walk 1,000 rows, UIA` −12…−21 %, 252 → 90.3 KB, A/A 2 faster; AOT the walk −14/−17 %, 251.8 → 90.2 KB, pair 2 media scrubber hover +10 % (one pair), A/A 0/0. Negative controls fail as expected; Windows Core 5,119, Headless 1,746, Linux bridge 104, Fluent 38, Vesper 951, Meridian 680, Junction 1,161, UiTests ReaderFocus 3, VirtualListSample 6, AccessibilitySample 8; Fedora the slnf 22/22, Junction 1,161, Vesper 933 (private D-Bus). ACCESSIBILITY-PLAN.md, DECISIONS.md, ELEMENTS.md. Rule broken: a 5th desktop hold (6.6 s) to re-run a fixed UiTest whose first version compared against the desktop's real focused element; that log held a browser tab's title and was deleted. |
| 16 | 0d96e805, b02b46d6, 32fffce7, 3211f964 | a11y-gate-on-client (question 50: build) + question 66 (a) | (a) A window builds its UIA tree and raises events only once a UIA client asks for its root (WM_GETOBJECT; built at once inside the ask, or by the next frame when asked inside a frame); each window its own gate, never closed. (b) `AccessibilityClients` ("a client listens": WebView2's own tree, a terminal's reader timers, text versions) also waits for that first ask on Windows; Linux unchanged; no public API. Real window (NativeAOT, the 4K at 150 %, idle-gated, alternated; 3 holds, 25 launches, 0 foreground takes, 0 nvlddmkm), db5dc041 → gate with no client asking (a11y bytes, raises, calls, kept wrappers and root asks all 0), UI KB: Junction start 1,835.9 → 1,823.7 (17 raises / 216 calls → 0), Commit → History 270.2 → 258.0 (417 calls → 0), a ref move 593.6 / 598.8 → 587.6 / 592.9, palette view switch ~1,268 → 1,202.3, 10 History Downs ~256 → ~250; Vesper start in 1,000 files 603.5 → 578.8, navigation 293.7 → 293.5, 10 Downs 29.7 → 17.1 (−42 %), 150 ↔ 250 switches −10…−18 %, viewer open 1,040.9 → ~1,016, close 717.8 → ~656.5; Meridian's preview open ~198.5 → 151.9 (13 raises / 144 calls → 0). After step 3 the gate's own share is small on Junction (−0.7 % start, −1 % a ref move) and large on key moves. An out-of-process client asking at ~5 s: the tree built once (203 nodes, 84.8 KB inside the ask), the next 5 Downs told; with Meridian's preview open the ask built the tree (45.1 KB) and the web page read the same (22 elements, heading found), no reload. A focus-only listener receives the same as before: UI Automation asks our root for it (3 asks when it registers with the window there, 4 when the window appears after), 11/11 and 6/6 focus events both sides; only a client that never makes UIA ask (this desk's own listener) gets nothing; Magnifier, the touch keyboard and Voice Access take the same path (reasoned, not run). 32fffce7: (b) first made the terminal subscribe to the static `ActiveChanged` event, and terminals left in disposed windows (still `IsHosted`) read their screen on every `Force` flip: 8 rich-text and text-area caret/typing rows +32 KB/op, +11…22 % in step-ab; the terminal now takes the announce place from its tree node, rows back. step-ab (db5dc041 → 32fffce7) `-Pairs 2`: JIT 0 slower (pair 2 the rich-text-in-list row 2.4 → 3.3 KB, as in step 3), A/A 0/0; AOT 0 slower, 1 faster (−12 %), A/A 0/0; the suite's UIA rows still measure the bridge (UiaRig answers IsActive). Windows Core 5,119, Headless 1,751, Linux bridge 104, Terminal 176, WebView 77, WebView.Engines 140, Fluent 38, Vesper 951, Meridian 680, Junction 1,161, UiTests 32 (+10 foreground-only skipped); Fedora the slnf 22/22. ReaderFocusTests' handlers drop other processes' elements before reading. ACCESSIBILITY-PLAN.md ("The gate on Windows"), DECISIONS.md. **The user's live Narrator check of Meridian's preview (question 66) is open:** `r16b4\live\` (start-meridian.cmd, CHECKLIST.md); the release waits for it. |

## Examined, not worth it

From the round 16 surveys (details in `17-reports/r16-*`):

- **apps-gl-native-commit-jump** (r16-8 lead; r16-2, r16-3): NVIDIA's GL shader compiler on a disk-cache miss, not a
  commit. The cache (`%LOCALAPPDATA%\NVIDIA\GLCache`) is keyed by exe name; m156's programs were new on this machine, so
  each new exe's first launch compiled them: +53–59 MB working set kept for that process plus one long flush (23.5–28.8
  ms), once per program per machine after a Skia or driver update. Repeats (ABBA) and five bisect builds equal with a warm
  cache; `__GL_SHADER_DISK_CACHE_PATH=<empty folder>` reproduces it (`__GL_SHADER_DISK_CACHE=0` is ignored on Windows).
  For PERFORMANCE.md: look for a `GLCache` file written in that second before blaming a commit; run a scene twice per exe.
- **layout-wide-step-two-frames** (r16-1, r16-6): a harness artifact. A `SetWindowPos` from another thread draws 2 frames
  on every grow step (shrinks 1); one posted with `InvokeOnMainThread` runs inside the drain's batch since c32c4b2e
  (step 2). On the window's own thread, outside a batch, every step draws 1 in Junction and Vesper (±120 px, ±4 px, a
  16 ms drag). For PERFORMANCE.md: make resize steps from a Win32 timer callback on the UI thread.
- **after-layout-callback** (question 51; r16-1): `Element.OnLayoutChanged` (7c0a3b79, the app team) is that hook; off it
  costs one static read a box written, on a thread-static read and a handler scan a box write plus a
  `ConditionalWeakTable.Remove` an unmount, 0 B a frame.
- **a11y-commit-tab-trailing-frame** (r16-7): an a11y-only pass that draws nothing since a86de0d1; gone under the gate
  when no client asks; with a reader it is the designed trailing bounds refresh.
- **junction-chromeparttests-load** (r16-6): no recurrence in rounds 13–16; Junction.Tests 1,161/1,161 twice under load.
- **junction-gitwatcher-burst-test** (r16-6): closed by f5bb3133 (the debounce on a ManualClock; `Forget()` drops the
  warm-up callback).
- **HarfBuzz 14.4's cold words** (r16-5, r16-8; plan 18's +193 ns a cold Segoe UI word): every second visit makes 0
  calls; first visits ≤ 1.0 ms once (the rich text editor's open), ≤ 0.10 ms at Junction's start, below the start-up
  bar; Consolas and the CJK UI faces carry 85–100 % of an open's cold calls (not timed). Corpus hashes and app counts
  equal to 8.3.1 (13 of 724,228 widths shaped instead of read). A kept shape plan a (face, script, direction): 0.03–0.95
  ms once a process. Whole-run shaping: Latin calls −77…−84 % but 2.4–2.9× the characters; in place of the word cache a
  Junction drag step would shape ~72k characters against 1,139 (64×); Common-script words would take their run's script.
- **Element size, +16–24 B an element** (r16-4): 943610c7 (`_accessibleEnabled` over an 8-byte boundary, +8),
  41e2775e (`InheritedStyle.CursorImage`, 72 → 80 B), d43b3518 (`MeasureCache._maxContentWidth`, +8): Junction History
  +3.1 % tree bytes, Vesper +3.2 %, +0.3 % of the heap; the walks' +1.3 % and the suite's +2 %. Packing the bools into a
  flags byte, the float into padding and the cursor image into a side table: −20 B an element, below every bar, medium.
  For PERFORMANCE.md: every Element field is paid on every element of every build; a bool is free only while the byte
  fields have room, and a 4-byte field in an embedded struct can cost 8 (check `GetUninitializedObject` sizes).
- **Kiota's model parse** (r16-4): ~40 KB an item, 25× the JSON, half of it `BaseItemDto.GetFieldDeserializers()` (155
  setter delegates and a dictionary, generated); once a page under one gen-0 GC. Kiota 2.1.6 is −5.6 % a request.
- **A Skia scratch purge after a tween on GL** (r16-3; r16-2's option (d)): NVIDIA keeps the memory in its own pool and
  reservations; sound only on Vulkan (`PurgeScratch`).
- **The posted-work drain's budget in the real Junction** (r16-6): 0 budget stops in 109 passes, at most 2 actions a
  pass; nothing to tune.
- **033a900c in the apps** (r16-6): 0 host-against-host reconciles in all five app suites.
- **The rich text editor's resize, 248 B a block a step** (r16-5): TODO.md's 1,015 blocks 251 KB a step; the apps'
  editors hold short texts.
- **One provider per served item per tell** (r16-7): −0.36 KB a key, key moves only; optional inside step 3.
- **Keys in an empty Junction Commit tab** (r16-2): 16 of 20 identical frames (round 15's declined "keys the apps
  ignore"); Vesper's PageDown at the list end, Meridian's map keys: known.
- **Vesper with the same folder in both panes** (r16-4): 30 KB a key at 100, 1,000 and 5,000 files, flat.

From the round 15 surveys (details in `17-reports/r15-*`):

- **gl-ui-thread-rest-wakes** (r15-3): a sampling artifact. Round 14's driver read each rest's closing mark after the
  next phase's UI work had begun (every nonzero rest was followed at once by UI work; every rest followed by a sleep
  read 0). Exactly bounded rests read 0 UI-thread runs in all 7 passive rests; 900 WinEvents a launch from another
  process woke the thread 0 times. For PERFORMANCE.md: bound a rest window from inside, or sleep after its closing mark.
- **text-bidi-real-window-private** (r15-3, r15-4, r15-5): NVIDIA's write-combined GL surfaces made by a resize's frames
  (`PAGE_READWRITE|PAGE_WRITECOMBINE`, ~11.9 MB each, never resident; walk 1 +19.1 MB on jrepo, +37.4 on the Arabic
  file, walk 2 +1.3 / +11.6), bounded, there without Arabic text too, never on Vulkan: not a leak, not c06aa102's.
- **mpv-cuda-context-at-rest** (r15-3): the CUDA thread (10.4 wakes a second, beside NVIDIA Vulkan's 115) exists only
  while a player exists and is gone after the dispose; libmpv loads every interop with its render context, so a
  software-decoded clip pays it too (~313 MB commit, ~104 MB resident while open); real films decode through NVDEC and
  need it. Skipping it for software streams needs the codec known before the render context is made.
- **gl-reel-paused-driver-threads** (r15-8): a paused Reel's 98 wakes a second are the NVIDIA Vulkan device's own
  polling threads (Reel's empty player rests at 115 a second, r15-3) and the CUDA thread above: nothing of ours to
  change.
- **vk-format-switch-chained** (r15-3, env-gated A/B of the destroy-first test): chaining the F16 → 8-bit switch saves
  ~30–230 ms once a process (create 339.6 / 136.0 → 13.4 / 12.3 ms, then a ~96 ms first present) and costs ~90 ms on
  each later switch off (~110 against ~20 ms) and ~95 ms on each later switch on (first present 93–102 against 0).
- **text-junction-first-visit-tokenize** (r15-5): a warm ~340-line C# file allocates 1.6–2.5 MB (4.7–7.3 KB a line);
  ≥83 % of the allocation-tick samples are TextMateSharp/Onigwrap types (Onigwrap.Region 12 %, OnigResult 9.5 %,
  OnigCaptureIndex 9.4 %, StateStack 7.1 %) plus Int32[] 27 %; HultUI's TextSpan[] 2.9 %. HultUI already uses
  TokenizeLine2 and stops at the last shown line: what is left lives in the dependency.
- **junction-files-head-key** (r15-6): not reachable. The Files view browses `_selectedCommitHash ?? "HEAD"`
  (Files.cs:92) and History selects a hash as soon as the log lands (History.cs:428–436).
- **A 16 MB width cache for BiDi probes** (r15-5; r15-4's Arabic cache lead): Junction's Arabic drag at 960 80.8 →
  59.1 MB (−27 %), at 1440 −7 %, keeping +8.8 MB; Meridian's drag −9 %; the drags still miss 209–458 new prefixes a
  frame (no cliff); the remaining ~1.1 MB a frame is RTL shaping without the word cache, declined in round 1.
- **The scrollbar tween's mid-curve identical frames** (r15-2): 4 of 28 at 200 Hz × 1.0, 1 of 18 at 120 × 1.0, 0 at
  120 × 1.5; telling them apart without drawing would also drop frames that change pixels.
- **Keys the apps ignore** (r15-2): Meridian's Down on the map, Reel's empty player, Vesper's PageDown at the end draw
  same-pixel frames (the root-handler cause of step 2 (c)) but are rare.
- **UIA `Rebase` over the whole tree per apply** (r15-7): ~1,100 dictionary lookups and 0 B an apply. **Meridian's
  palette typing** 3.9 KB a11y a key (its part). **Vesper's breadcrumbs renamed by position**: a handful a pane.
- **The watcher's per-window limits** (r15-6; `_ask` 256, `_waiting` 4,096): 1 reload per cold build of the
  103-project solution, nothing on incremental builds. **Per-file reads coalesced** (`R15_FILECOALESCE`): only 0–2
  reads dropped in a real window, and the naive version showed the kept diff on 12 of 20 first frames.
- **Folding GetCommit, GetCommitFiles and GetCommitDiff into one git run** (r15-4): −2 of ~10 runs and ~2 % of a key's
  bytes. **Palette open/close retention**: +20 KB over 40 toggles.

From the round 14 surveys (details in `17-reports/r14-*`):

- **media-interop-gl-preload, built and measured (round 14 step 8; DECISIONS.md 0ba99050):** with the opt-in, a Vulkan
  interop window loaded the GL driver on a worker after the device. NativeAOT probe host built as each app's Main, the
  4K at 150 %, before (104179d2) and after alternated, idle-gated: Reel opening a film at rest Open → interop ready
  96.5 → 44.0 ms but → first video frame only 196.5 → 177.3 ms; Aperture's first playback 96.6 → 46.1 and 236 → 230 ms;
  Reel started with a film 177 → 162 ms. Only 11–21 ms reaches the frame because mpv's GL context creation takes 76–88
  ms instead of 49–62 when the driver was loaded seconds earlier (a UI-thread load at device creation was as slow).
  Cost in every session that never plays: one more NVIDIA GL thread waking 10 a second for the process's life (113 →
  123), Reel's empty player 108 → 127 MB working set and 106 → 132 MB private, Aperture before any playback 143 → 161 and
  153 → 181 MB. Reverted; the patch (with Aperture's opt-in, no tests) is `r14b8\gl-interop-preload.patch` in session
  41943a9b's scratchpad. For PERFORMANCE.md: judge a driver preload by the frame that uses it; count its at-rest threads
  and memory in sessions that never use it; `SDL_GL_LoadLibrary` on a worker pumps nothing there.

- **Closed from Waiting:** junction-git-optional-locks (6: a content save makes `git status` write the index 0 times in 8;
  with `GIT_OPTIONAL_LOCKS=0` files rewritten with the same bytes stay stale, 192 entries and 2.6 MB re-hashed by every
  summary status; Changes' `git diff --name-status` writes the index anyway; the staging miss is question 58 instead),
  junction-commit-first-visit-frames (1, 8: 1.00 frames a key at 33 ms; at a reader's pace the second frame is the
  colours landing, by design), a11y-label-under-named-button (7: none of the apps' 11 buttons whose label changes does
  so without its card's structure).
- **Layout (1):** keying Junction's diff rows so the colours landing patches them (text measures 137 → 4, but 299
  patches and 312 → 320 KB, +2.5 %); wheel scrolling (1 frame and 8–14 KB a notch; a touchpad's 1/8 notches are
  coalesced to the display rate, 4.6–4.7 frames a notch at 120 Hz); a DPI change (3–4 frames, 0.6–1.2 MB, once a move
  between monitors).
- **Painting (2):** a press on selectable read-only text draws an identical frame (TextElement.cs:354 marks
  unconditionally, and the focus clause would need to know the text draws no focus); Junction's render-phase bytes on
  keys are row realization (97–99 %; paint calls 0.1–0.2 KB a frame); GPU memory at rest (Aperture 11.1 MB after the
  purge, the glyph atlas one 4 MB page: nothing left to purge); hover-style frames with identical pixels (Meridian's rail
  4 in a 1,668-move sweep).
- **GL and Vulkan (3):** extending the Vulkan start-up preload to `vkCreateInstance` (ceiling ~20 ms under the loader
  lock; round 7 measured the driver-load half at −5.2 ms; not without a timed prototype); holding 1 ms through playback
  (~1.4 hint changes a second, ~30–60 µs); the `SurfacePurge` wake (1 a run of size changes).
- **.NET runtime (4):** a git process start costs 27.7 KB (`Run`) / 31.4 KB (`RunAsync`) on all threads, `Process`'s
  stream buffers (only own pipes would avoid them); `_lastCuts` holding one closed document's text (at most one
  string); `SelectRepo`'s 180–240 KB on the UI thread (rare); culture-sensitive string calls elsewhere in `src` (none in
  hot paths).
- **Text (5):** making natural words at a start's second sight (new widths −25…−30 %, drags +14…+33 %); ending natural
  words at the ASCII change scanned from the table's end (several words a start; replaced by step 1 (a)'s rule); capping
  natural-word length (they average 22.7 characters); Korean (the plain path; drags 738–744 KB).
- **Reactivity (6):** AsyncLoader coalescing (let the in-flight result land, then start the newest key): Changes under a
  0.4 s churn 266 → 235 git runs but 30 → 78 builds, 26 → 68 frames, 1.8 → 8.1 MB UI; dropping superseded results is the
  cheaper rule.
- **Accessibility (7):** the wheel (1 ScrollPercent raise, ~0.3 KB a notch); the review editor's formatting bar enabling
  at once (one whole tree an editor focus change, rare; tellable only by changing the tree builder); History search
  renames (~46 Name raises a key; a11y-items-on-demand-sooner would cover them); Reel playback (≤ 4 range raises a second by
  design); d73b1439's handler checks (0 B).
- **App workloads (8):** the long "other" frames after a ref move are the bridge, not a timer tick (18.8–23.6 ms at 1 ms
  resolution, gone with `HULTUI_A11Y=0`); Vulkan rest wakes and timer resolution (NVIDIA's).

From the round 13 surveys (details in `17-reports/r13-*`):

- **Closed from Waiting:** size-change-restore-second-frame (1, 3: `SetWindowPos` from another thread draws 2 frames a
  grow, round 5's trap; from the window's own thread, where drags, maximize and snap come from, 1; 9 KB, 0.3 ms render);
  F16 kept drag targets (3: F16 is on only while an HDR video is on screen, ~2 KB a drag frame then);
  meridian-hover-frames-real (2: the probe's pointer column sat in the map list's scrollbar band, whose 0.12 s expand
  tween draws 16 frames at 120 Hz, 25 at 200 Hz, 3 headless); a11y-structural-walk-during-resize (7: the walks come at a
  drag's first step, whose bounds are final, or at one breakpoint rebuild: 0 saved); aperture-cursor-guard-textarea (6:
  the only TextArea is caught at ApertureApp.cs:500, which tests both types).
- **The Vulkan apps' ~112 rest wakes** (3, 4, 8): four NVIDIA driver threads in nvoglv64 (~97, 10, 4 and 1 a second),
  the busiest a 10 ms poll at the 1 ms resolution the device itself holds; a windowless console process with only a
  Vulkan device shows the same. Not ours; unchanged by present mode, OBS's layer, a11y or SDL's hint. Aperture and Reel
  on GL until a video plays would rest at ~10 a second, but that is a runtime renderer switch against the Vulkan-opt-in
  decision.
- **FIFO for 8-bit Vulkan** (3, 8): adds NVIDIA's DXGI threads (97 + 25 a second) and +37 MB local; MAILBOX stays.
- **Replacing the VkSurfaceKHR at the switch to 8-bit** (3): NVIDIA keeps its DXGI path (identical threads and memory);
  only a device rebuild is left (question 54).
- **SDL's 1 ms timer at rest** (4, 8; expert 3's step 8): no own wake saved in any app; NVIDIA Vulkan holds 1 ms anyway;
  the machine-wide effect is unmeasurable here. Kept as step 8 only under question 53.
- **8-bit against F16 paint cost** (2): identical draws, layers, atlas and image textures; F16 adds a 22.8 MB offscreen
  and larger swapchain images. GL's FBO 0 stencil (11.12 MB at 2160×1350) is accounting only, flat.
- **Capping short UI tweens below the display's rate** (2): frames scale with refresh (a 0.12 s tween is 15 frames at
  120 Hz, 24 at 200 Hz); a cap would change smoothness on high-refresh displays.
- **A bigger or 2-way fallback front table** (5): misses are 0.5 % (ja) and 1.6 % (zh) of drag questions. Rank CJK work
  on the real ja/zh fixtures, not round 12's generated one (its 11k new words a width thrash the 8,192-entry word cache
  on their own, hiding step 3's effect: −2 % there).
- **Skipping property raises for elements no client was ever given** (7): ~140 of a resize's 142 raises, ≤ 1,963 calls
  and QIs once a resize after the debounce, 0 managed B; below every bar (a finer form of question 50).
- **67fc0884's label array growth** (1, 4, 7): 24–40 B an item once an append, only with a reader (`GetItemLabel`
  already resized so). **Wrapper lookup for items served on demand** (7): one uncontended lock and 2 map lookups, 0 B.
  **Native pattern objects** (7): 70.4 KB native a 400-item warm walk, paced by the client, unchanged.
- **Bytes per pointer move that draws nothing** (1): 0.15–0.85 KB on all threads, no frame.
- **Junction History 1.10 frames a key with a pointer** (8): the hover tint really follows the scroll. **Aperture's
  5-frame bursts** (8): posters landing, by design.
- **Empty builds** (6): 0 in 31 headless walks of common actions in all five apps. Watcher misses both versions share
  (index-only changes, a linked worktree's own switch) heal at the next event; pack-refs, gc and config reloads are
  rare.
- **apps-junction-drag-a11y-bytes** (8's lead): the bridge's third of Junction's drag-step bytes is the one structural
  walk at the drag's first step (327 KB, final bounds); closed with a11y-structural-walk-during-resize.
- **Word cache entry size** (4): 56 B × 8,192 = 458,776 B, 65 KB under the LOH line; a 64 B entry would put the array on
  the LOH (keep in mind when the entry grows).

From the round 12 surveys (details in `17-reports/r12-*`):

- **terminal-noop-scrollback-wheel, terminal-row-strings-by-content** (2; closed from Waiting): no app references
  HultUI.Terminal and nothing in it changed since round 10 but the target framework; under "real apps first" no case.
- **vesper-folder-nudge-stale** (6, 8; closed from Waiting): real window, 1 of 40 keys stale at a 33 ms repeat, 0 of 10
  at 300 ms (~0.025 view builds a key, 0.3 % of 282 KB); 0 of 71 frames in the nudge's phase at 100 ms. Headless counts
  59 of 60 stale because the pump runs the posted nudge right after the next key's load starts. Only a 70–370 ms
  listing (a slow share) could pay it.
- **junction-held-key-bigfiles** (8; closed from Waiting): in real time at 33 ms 948–1,149 KB a key on all threads and
  0.10 git runs a key; round 11's 2,367–3,110 KB came from the headless clock. A new file at a 100 ms pace is tokenized
  once (~14.5 MB on the worker), by design (HighlightRest 120 ms).
- **The first F16 replacement's retained memory** (3): +26 MB DXGI non-local, +4.9 MB local, ~+50 MB private 15–30 s
  after one maximize, snap or drag in Aperture and Reel; it plateaus (later resizes ≤ +2 MB); driver-side (8-bit
  returns everything; a rebuild made earlier only moves it into start-up, round 4). Folded into question 49.
- **A resize step made from another thread** (1, 3): a grow draws 2–3 frames (WM_PAINT after the live frame's
  `ValidateRect`), the window's own thread 1; round 5's trap, real only when another process resizes the window.
- **Skipping the second draw of a growing step** (1): built behind a probe switch, not run: no real-app case.
- **GL resource-cache purging, a persistent program cache** (2): budget flat 8–9.4 MB over every interaction and 18
  resizes; 8 lazy program compiles a Junction session, all before the first key; SkiaSharp 3.119 has no API.
- **Resize bytes a step** (1, 3, 4): 6.6–46.5 KB on all threads, re-layout at each new width (caches miss by design),
  0 GCs in 40 steps; Vesper's 15 KB median drag frame (91 KB in WinSxS) is layout at each width; between two widths
  716/685 MeasureSize; rare.
- **Junction History's compact breakpoint** (1): 17 rows remade, 300–421 KB a crossing; the rows really change there.
- **Resize breakpoint rebuilds** (6): 2–4 crossings per 48 width steps, each changing what is shown; 0 on height steps.
- **Start-up proofs** (5): ~440 of Junction's 653 first-frame HarfBuzz calls are digit and mono-ASCII proofs, once per
  process.
- **"Last fallback face if it has the glyph"** (5): not exact (the OS picks another face for 4,054 of 16,931 ideographs
  in ja, 4,249 of 16,740 in ko): pixels would change.
- **Partial-word shaping in wrapped Latin text during a drag** (5): 18 HarfBuzz calls a frame.
- **OS fallback misses in a CJK listing** (5): Vesper 71 + 383 calls, once per page visit.
- **The UIA thunks' managed bytes** (4): ≤ 21 B a raise. **NativeAOT's thread pool** (4): workers retire on their own.
- **Building the a11y tree after the first present** (7): Junction's first tree is 103 nodes, +182 KB; unlikely to reach
  10 ms.
- **A label delegate made anew each build** (7): it skips the off-screen rename check, but no app does it.
- **IRawElementProviderAdviseEvents on the root** (7): UIA never asks for it, so a provider cannot learn "nobody
  subscribed" that way.
- **Spinners at the display rate** (3): none ran in any real launch. **GL drag frame bytes** (3): 1.9–2.4 KB, the
  per-size SKSurface, drag only. **F16 kept drag targets** (3): 2,000 B a drag frame, ~64 KB a 32-step drag, drag only.
- **More start-up frames in a real window** (2, 5, 8): Junction 13–14 frames to settle against 5 headless — loads
  landing in real time, drawn from the caches with 0 extra shaping.
- **Folding the hover settle into a breakpoint's resize frame** (1): 1 frame a crossing with the pointer over the list.

From the round 11 surveys (details in `17-reports/r11-*`):

- **layout-ordered-children** (1; closed from Waiting): Meridian's map, the one app case (618 hit visits a move in round
  10), now 40.5–42.5 at every size; apps ≤ 42.5 (Junction changes wheel 33.5–36.2); only suite rows remain.
- **The map selection drop without the list's layout mark** (1): −46 ComputeLayout a click (41 of them cache-clean), 0
  MeasureSize or KB; risks a list whose width comes from its rows. **Keeping the list's measure cache across a
  render-time `ReplaceChildren`**: 0 saved. **Measure-cache capacity**: fits once forced heights collapse; not raised
  without a case.
- **Paint on .NET 11** (2): `richtext/open 5,000 blocks` +7.2 KB render (+0.3 %, first open, the paragraph layout in
  render); sub-blob misses after a relayout keyed by run object (only the synthetic 600-span row pays, 223.6 a key;
  realistic rows 3; 0 managed bytes); pixel-route frames 248 B (surfaces without GL only); 7ca2b3db's second frame per
  width-changing step with a pan bar (merges into the next move's frame in a paced loop).
- **GL/Vulkan on .NET 11** (3): no tier-1 or PGO allocation in frame, present or resize paths (every steady round 0.0 B);
  an app element unmounted holding its own `IGpuImageSource` would turn 7e964aeb's gate off for the process (64–65
  frames a second on a held edge with a post), but no app or sample has one (GpuFrameSink.cs:58–59 documents it);
  other-thread bytes 0.5–0.6 B a frame.
- **.NET 11 JIT boxing patterns** (4): a nullable non-int struct boxes 24 B a value in a loop through
  `HashCode.Add/Combine`, a generic `v is null` / `v?.GetHashCode()` on `T?`, or a `Dictionary<(int, W?)>` lookup; single
  calls, `int?`, `EqualityComparer<T?>`, record structs with nullable fields, `HashSet<W?>` don't. None left on a hot
  path in src or apps (c99e23bf was the one). AOT 11 boxes only the generic `IsNull<T?>` loop (unused).
- **Text alternatives** (5): a range-key struct with the default comparer, 25 ns a lookup on both runtimes (slower than
  .NET 10's shape); a non-randomized word hash (Marvin 9 % of the walk; a crafted document could chain a bounded
  cache); the width cache's dispatch alone (595 lookups a step, ~7 µs).
- **Reactivity** (6): `WeakElementRef` derives from `WeakReference`, so each kept key is finalizable (one finalizer run
  and one extra survival when pruned; once a key, not per build); duplicate dependency reads (Junction 10 reads, 3
  distinct; lists reused, 0 B); Vesper folder in/out's 4 builds a key are chained (navigation, listing, preview part,
  nudge), one batch would not merge them; `AsyncLoader.Start` in Aperture's build (once a process).
- **Bridges on .NET 11** (7): UIA identical on .NET 10 and 11 in JIT, TC=0 and AOT (a label edit 0/80/80 B off/tree/UIA;
  a client walk 10.6 B a node; a served item 104 B); AT-SPI over a real D-Bus ±10 B a signal (PropertyChange 188 →
  179–181 B); our `CallMethodAsync` +64 B a call (start-up and status changes only).
- **Apps** (8): Vesper +1.5…+7.9 % and Junction −4.7…+0.5 % between rounds are the machine (the same binary reproduces
  them across days); Junction bigfiles +0.4 % same day; Junction Changes Down over 300 files 294 KB a key, 83 % first
  draws of 26.2 new diff rows; Meridian find typing 59.1 KB and History search 50.0 KB a character (by design); Vesper
  folder in/out 288 KB once per Enter (55 % first draws); Meridian palette with a reader "frameOther" 17.3 of 32.2 KB
  (the overlay, flat since round 8); Aperture Discover Down 0.67 builds a key.

From the round 10 surveys (details in `17-reports/r10-*`):

- **paint-text-lines-seek** (2; round 10 step 14, built and measured, 9f76ecb6 DECISIONS.md): binary search for a long
  label's first visible line (≥ 32 lines, tallest line and sorted flag on the layout, +8 B, exact): `text/wheel over a
  2,000-line plain label` min JIT −2.9 / −4.4 %, AOT −3.7 / −3.1 % (A/A 2.4 % JIT, 0.8 % AOT), rich-text row ±0.5 % / −3.9 %;
  no allocation moved. Real but under the 5 % bar (7–10 µs a step, ~4–5 ns a line); revisit for 20k+-line texts drawn
  every frame. Patch kept in the session scratchpad `r10b14\text-lines-seek.patch`.

- **Steady-state render bytes in the suite** (2): all VirtualList realization in the render pass by design (`TableView
  10k sort` 234 KB, typing beside a 150-line diff 249 KB, `list 100k replace` 143 KB). **Effects** (2): shadow and
  gradient tiles 0 cache misses, 0 B; layers only in scenarios built for them. **Image memory** (2): Vesper decodes at
  target size; `SkiaImageSource` shares pixels. **TextArea draws visible rows only** (2, 5).
- **Bytes per video frame** (3): 14.6 B JIT / 12.2 AOT on the UI thread, all from mpv's 9 `time-pos` changes a second
  (`MediaPlayer.Apply`'s Batch closure, MediaPlayer.cs:276); render 0 B; under the 0.1 KB target. **GPU memory left after
  a web view is disposed** (3): GL +16.3 then +7.5 MB, Vulkan +21.3, flat after; NVIDIA's GL driver pool (reused, not
  Skia's cache, predates 58aeb965); no framework lever. **F16 vs 8-bit GPU memory** (3): decided in round 3.
- **`Secrets.Resolve`'s variable name** (4): ~1.4 KB of a 47.1 KB drag frame (3 %); goes with step 13. **Hover settle
  under a wheel** (4): 0 B added. **The lent `TreeDiff`'s kept capacity** (4, 7): bounded, holds no node after `Clear`.
  **`SaveMap` writer's int-to-string** (4): off the UI thread.
- **The label cull loop's O(lines) checks** (5): a few µs of a 0.24 ms frame, 0 B; expert 2 proposes the seek (step 14,
  measure first). **HultUI.Syntax's per-line share** (5): 2.0–8.2 %, the span arrays it returns; TextMateSharp 92–98 %.
  **`TextPaintBounds`** (5): stops at scroll views. **bf74fbca's second registries** (5): +378 / +680 KB once per theme.
- **Vesper name prompt, Meridian dialogs, Junction MR title and composer** (6): already build only when something shown
  moves. **Discovery map at 2,000 endpoints** (6): ~35 B an endpoint a click, accepted in round 9 (step 11 removes the
  rest). **Junction History resize** (6): 33.6 KB a step, rows remade only at layout breakpoints. **Reel** (6): no
  typing or full build per key.
- **Bridges' whole-tree walks per structural apply** (7): 0 B, the window's walk is O(n) too. **History with a reader**
  (7): the 300 eager item nodes (on-demand items declined in round 2). **View switch at 600 endpoints with a reader**
  (7): UIA ~175 B a node, the lazy-wrapper lever declined in rounds 1–2. **Probe trap** (7):
  `HeadlessWindow.FindByAutomationId` builds a whole accessibility tree per call (+850 KB a click at 6,073 nodes inside a
  step); find elements once, outside the step — a guideline note.
- **Junction Files search Down's 96 TextArea measure misses** (1): O(1) each, 0 B. **ff6125a6 per frame** (1): one flag
  read, one ~7-parent walk after a change of children.
- **Junction diff find Enter +1.0 %** (8): bimodal noise over 13 runs in 6 trees. **Vesper Tab 92.2 KB**, **Meridian
  method pick 149.5 KB** (8): once per action, unchanged. **Reel playback 0.8 KB a frame** (8): the fake backend.

From the round 9 surveys (details in `17-reports/r9-*`):

- **junction-leads' old-side state convergence between hunks** (5): at best lines walked 82.2 → 68.2 % of new-side
  lines (−17 %) over 466 files, off the UI thread, an `IStateStack` compare per hunk; closed.
- **A long-line cap per grammar** (5): worker time only, DECISIONS.md declined a table; HTML's 2,000 bounds a minified
  inline script at JavaScript's cost (79 ms). **Counting non-ASCII by advance** (5): −25 % of one file's once-per-open
  cost (TODO.md), out of proportion. **Pretty HTML/XML** (5): worker, once per response, and it lowers the open bytes.
- **Lazy UIA wrappers** (7, again): 4.06 KB of 36.5 a reader-on Aperture grid Down (11 %), ~207 B a node added; declined
  rounds 1–2, no new scaling evidence. **AT-SPI cache add/remove per node** (7): push-based by design. **Meridian's new
  views with a reader** (7): 1–9 % over bridge-off, 0 trees.
- **RGBA16F interop image for an 8-bit page on Vulkan** (3): 13.5 vs 6.7 MB, 4.6 % of the process, Vulkan opt-in.
  **Closing the WGC frame pool on hide** (3): 0 MB in this process. **Web view open/close cycles** (3): GL +7.5 MB once
  then flat, Vulkan flat; browser processes exit with the last controller. GL drag frame 2,080 B and the driver's drag
  commit (3): as rounds 2 and 5.
- **Meridian find typing +8.5 %** (8, 2): the response Part 47.1 → 51.1 KB a character after main's Request tab and
  toggles; under the bar. **Response tabs** 53.1 KB a click, **Discovery** once per action (8). **Vesper transfer
  frames +21 %** (8): timing; no round 8 commit touches transfers.
- **Meridian dialogs** (4, 6): typing 0.22–0.59 KB a character, 0 builds; 200 open/type/close cycles 0 B retained.
  **Request tab `PrettyJson(sent.Body)`** (4): the sent body only.
- **Other row-level `Opacity` in apps** (2): Junction resolved review cards, clone list, dragged repo row; Meridian
  locked option rows, sending/stale response, runner row; disabled buttons — transient or a few layers.
- **Bounded, not leaks** (4): Linux/macOS `KeyringMemo` (one entry per secret), Aperture's per-shelf `KeptElements`
  (max(64, 2 × live)), the artwork memo (1,024), Vesper `_ringMoved` (two cells), `Restyle` (a `VisualState?` struct, no
  delegate). **e7d10c63's `PositionFromPoint` / `TickSelectionEdgeScroll`** (1, 4, 5): O(depth) and 0 B; no idle wake.
  **Vesper `MoveRing`'s linear search** (1): at most what is on screen, 0 B.

From the round 8 surveys (details in `17-reports/r8-*`):

- **runtime-element-layout-state phase 2** (1, 4): elements made × ~70 B are 4.0–7.6 % of a key's bytes in every walk,
  retained −1.5…−2.3 %; round 7's builds made fewer copies. Under both bars; closed.
- **layout-patch-keeps-layout-dropdown-expander** (1): a Fluent rebuild lays out 16 and 9 elements; closed.
- **Palette rows follow their item** (1): Junction palette type+erase −7.9 %, suite −1…−2 %; would change the documented
  `palette-row-N` automation ids.
- **Junction Files' 149 measure misses a key, grid/edit's auto column, TableView column resize, Vesper same-names
  misses** (1): the relayout boundary (round 5), the scenario's design, no app, small.
- **Snapping text blobs' x fraction** (2, 3): wheel steps are whole layout pixels (40 at any scale), so only touchpad pans
  add cache entries; snapping x moves glyphs.
- **VirtualList realization in the render pass, History's affordance shader per key (0.7 %), palette Down's 3.7 KB of
  overlay text, canvas and halo text, disabled buttons' groups** (2): by design, under 1 %, or no app draws them.
- **efaf947b's pixel-frame size reads, `SaveFrame` readbacks** (3): once per video frame; only when a frame is asked for.
- **`UiaDisconnectProvider` per drop, `_nativeLock` per lookup, `LocalPosition` O(depth), the `AsyncLocal` ambient
  application, `KeptElements` retention (max(64, 2 × live)), a kept `TreeDiff`** (4, 7): ≤ 0.5 a key, 0 B, or bounded.
- **Junction big files' gen 2, the CRLF third copy** (4): 0–1 per 100 keys, background GCs; split without the `Replace`
  copy (JunctionApp.Files.cs:630) only if it ever matters.
- **A kept touched-rows array, a long-line P per grammar, 600-span typing, TextArea typing with a reader, emoji labels on
  the BiDi path, multi-font `LineSource`, richtext/open cache misses** (5): trades retained memory for churn (−6.7 % a
  Meridian send), declined in DECISIONS.md or round 1, or needed.
- **Framework batching of the main-thread drain or of `MarkDirty`** (6; the drain half SUPERSEDED 2026-11-15 by round 15
  step 5, c32c4b2e, question 62 (b): an audit of apps/, samples/, src/ and tests/ found no such site): an await continuation that writes and then reads
  would see the old tree, in 5 apps; the two app sites (step 4) get the same win. **Junction Changes' diff pane as a
  `Part`** (6): ≤ 13 of 376 KB (−3.5 %). **Kept Brief columns** (6): gone with step 7.
- **A node-with-children's own change in place; the bridges' whole-tree pass per structural apply** (7): ≤ 4.5 % of a
  stage, +6 % on caret keys after 4+ stages in a row; a constant factor, 0 B.
- **Junction Changes' caret at 300 files** (6, 7, 8): 83 % render of the reached file's diff rows, which are needed; build
  12 %; the reader 4.5 % of a stage.
- **Commit view's all-thread rise after 3d54d779** (8): the grammars' first use moved from History's prefetch to the
  Commit tab (bisected; the second stretch 285.6 → 293 KB, GCs equal). **Junction Files all-thread +7.5 %, Meridian's
  method pick, History search's chrome** (8): off the UI thread, once per action, 1.7 KB.

From the round 7 surveys (details in `17-reports/r7-*`):

- **vk-startup-preload** (3): a worker loading `vulkan-1.dll` before `SDL_Init` cuts SDL's load 73–82 → 2.5–3.4 ms but
  grows `SDL_Init` 15–17 → 44–55 ms (loader lock); AOT min −5.2 ms, under the 10 ms bar.
- **Two same-size redraws after the first frame** (3): Windows' post-show WM_PAINT runs `OnWindowResized` at an
  unchanged size, then a frame after MOUSE_ENTER; after the first present, once per window.
- **GL `BeginGraphicsPreload`, `opengl32.dll` in a Vulkan window** (3): no new evidence; a one-off DLL load.
- **atspi-kept-tab-retire alone** (7, 4): one map operation instead of two saves ~1 % of the step; the `Count` locks
  are the cause (step 1).
- **Rebuilding only the changed container's a11y subtree** (7): −10 % Changes, −2.4 % Aperture; the in-place walk is
  step 7.
- **runtime-element-layout-state phase 2 alone** (1, 4): ~70 B of cold fields per element, −2…−9 % a key (Waiting, low).
- **Sticky rows' second O(n) pass** (1): ~300 calls a key at 300 files, 0 B. StickyRows twin cost +0.3 KB (+3 %).
- **Changes wheel at 300 files** (1, 8): the approved trade; breaks even at ~220 notches per arrow key, a mixed
  session −92 %.
- **The drawn line's second pass after lazy runs** (5): at most +2.6 % time; wheel rows 3.3–4.7 KB/step.
- **BiDi rows' `GlyphPosition[]`, text/create, a richtext/open closure, tokenize vs file size** (5): declined RTL
  cache, element size, 3.5 % under the bar, flat per line from 500 to 4,000 lines.
- **`Canvas.LocalClipBounds` per paragraph, steady render bytes, layers, rounded clips** (2): flat by depth; 0 B;
  opacity only; GPU work.
- **Coalescing `MarkDirty` in a dispatch** (6): doubles happen on one-off actions only. P5a stands (reconcile 9 % of a
  Meridian URL key at most).
- **Retained +470 B a dialog cycle** (4, round 6's question): the dialog's label names the cycle, so the text caches
  keep a new string each open; a constant label is 0 B over 1,400 cycles. Bounded caches, not a leak.
- **`TokenizeDiff`'s cancelled `Task.Delay` throw, `AtSpiServedItems` key copies, kept-tab object pooling** (4): under
  0.1 % of a key; only when a served list shrinks; no app keeps tabs.
- **Vesper Tab (102.8 KB, 93 % build), Meridian find's search (24 KB a character), Junction big files at 33 ms** (8):
  once per action; needed; timing, not a leak.

From the round 6 surveys (details in `17-reports/r6-*`):

- **meridian-history-aot-p90** (4, 6, 8): two regimes, not a runtime effect — the walk's first ~22 keys have the
  selection on screen (64 made, 16 laid out, 63 KB/key), later keys don't (48 made, 0 laid out, 46 KB; the total
  comparison's "same bytes" was off by +16.6 KB/key); the slow stretch moves between reps in both trees with identical
  counts. It exposed meridian-history-selection-offscreen (step 7).
- **palette-row-keys** (6): keyed by `Group|Title|Detail`, bytes +3 % bridge off and +11 % UIA, trees 0 → 0.75 per step
  (reordering kept rows is structural), patches 122 → 253. Replaced by palette-virtual-rows (step 9).
- **live-frame-requests-public** (3, 4): no consumer; 2a332218 draws a posting source at the timer's rate.
- **apps-tokenize-on-ui-thread** (5, 8): no tokenizer share on the UI thread in History, Files or Commit.
- **a11y-box-selected-patch** (7): the only producer is a tab switch, which also swaps a 901-node panel.
- **a11y-in-place-fallbacks** (7): 4 of ~1,150 refreshes; the composite grid's are real structure changes, kept to 20
  trees per 120 notches by the backoff.
- **Button `IsEnabled` flip, tab-switch wrappers, Junction History search tree** (7): once per user action, or under 5 %
  of a key; the AT-SPI part falls to step 15.
- **RowHeights height function per rebuild** (1): 20,000 calls, 0 B, array lookups; skipping needs an API.
- **Flex-shrink heap arrays above 64 items, hit-test enter/leave allocation** (1): 0 calls; 0.1–0.24 KB a hover step.
- **Keeping pool threads alive; the GL per-frame driver spin** (3): the walk never recurs; the spin is `glGetError` under
  NVIDIA's threaded driver (DECISIONS "One submit per frame").
- **`BoundingRect` O(depth)** (4): app trees are depth 2–3 (23–83 reads per hit test).
- **Retained heap over open/close cycles** (4): growth is the text pipeline's byte-bounded caches and the blob map; no leak.
- **Reel headless 0.6–0.7 KB per frame** (4): the fake backend and virtual clock; the real AOT window reads 90–111 B.
- **Rich document `Blocks` copy, BiDi `GlyphPosition[]`, emoji itemize substrings, an exact run array** (5): public API,
  a declined RTL word cache, lazy runs (step 14), or −1…−4 %.
- **Spanned-line composes, render-time cull visits, rounded clips, image decode, video pixel path** (2): needed anyway,
  layout-ordered-children, GPU-side, already bounded by every app, or 248 B a frame.
- **Mounts without unmounts** (6): patched-away twins count as mounted; not a leak. Reconcile pooling (P5a) stands at
  2.4–8 % of a key.
- **runtime-element-layout-state** (4; build step 8, declined b35a81d8, docs/DECISIONS.md): layout state in its own
  object made Box 584 → 384 B and Text 656 → 456 B unlaid-out (+48 B laid out) and cut bytes a key −16 % Junction
  summary, −15 % Meridian URL, −12 % Meridian History, −4…−10 % Vesper/Aperture (retained +0.9…+1.6 %; audit identical
  in 163 scenarios), but the box read moved off the element cost `scroll/wheel 5,000-row list` +8…+14 % (flat with a
  16 B box copy kept inline) and `layout/resize 2,000-row list` +3…+5 % on AOT (A/A ±1 %; inlining the setter still
  +2…+4 %). Patch kept in scratchpad `r6b8/change3-final.patch`. Phase 2 lead: fields no per-element pass reads
  (focus, a11y-observer and overlay-host references, `OverlayOrigin`, Text selection; 50–100 B a copy) can move out
  at no time cost.

From the round 5 surveys (details in `17-reports/r5-*`):

- **paint-subblob-trusted-face-bounds** (2): Consolas −10.5 % glyphs (≈ −2 % frame by round 3's ratio), Segoe
  UI/Arial −30 % but no app draws dense colour in them; 81 → 515 `GetGlyphWidths` per rich text keystroke.
- **paint-drawspannedline-overlap** (2): no new span producer; a latent contract, closed.
- **Bitmap-strike faces** (Courier New Regular, Calibri) drawing whole runs per stretch (2): no app colours text
  in them; would need per-ppem bounds and a new pixel A/B.
- **reconcile-restyle-on-change (b)** (6): style allocates 0 B; resolves = patched + made; time only in a 1–2 %
  phase; the component splits remove the patches instead. Closed.
- **Reconcile pooling (P5a)** (6): reconcile bytes ≤ 5 % of a key (Vesper 8.8, Meridian 12.0 KB). Stands.
- **`MonospaceAsciiAdvance` on `IMeasureContext`** (5): Junction's and Vesper's own (n + 1) × advance rule already
  holds for their faces; no byte would change.
- **A VirtualList measured as a leaf** (1): resize misses −25 % but it moves geometry (Junction Commit text
  measures 15 → 60). FlexBasis(0) on Vesper's dual panes (−20 % resize misses) is an app guideline.
- **Relayout boundary** (stop at an element whose size is unchanged) (1): a large engine change; app lists are
  virtual (~30 rows).
- **RGBA8 interop image on 8-bit swapchains** (3): 8.8 MB of 600 MB GPU memory (1.5 %).
- **A video in a second window during a drag** (3): no app opens a second video window.
- **GL resize re-wrap** (3): FBO 0's size and flip must match; cannot keep.
- **TextArea document as one string** (4): see Needs the user, round 5 question 3.
- **Two loop passes per drawn frame** (4): a pass with no frame allocates 0 B; the loop's design.
- **Junction LOH at session end** (8): live data equal (68.0 vs 68.1 MB); LOH free space by GC timing.

From the round 1 surveys (details in `17-reports/`):

- **Layout:** MinContentHeight outside the cache; layout/resize 2,000 rows at the semantic
  minimum; wrap chips, baseline, aspect tiles; sticky; TableView sort; element size (P5c);
  VirtualList wheel realization.
- **Painting:** per-draw shadow/gradient/dash objects; run blob cache bound; per-label
  layout lookup; SaveLayer/clips; picture caching (damage-rect decision); canvas context.
- **GL/Vulkan:** GL cache limit/purge; FBO 0 without stencil (pixel risk); video GPU
  churn; start-up GPU work (no program cache in SkiaSharp 3.119.2); F16 MAILBOX base cost
  (decided); MSAA atlas resolve (paint-side).
- **.NET:** overlay and row-replacement leaks (none); icon raster caches; Junction
  highlight prefetch (TextMateSharp); Vesper KeyChord enumerator; LINQ in the frame path
  (none).
- **Text:** RTL word cache; plain path for emoji/ZWJ; layout cache size; ICU breaks;
  word cache bound; moving line starts out of Render; incremental rich text paragraph.
- **Reactivity:** batching of signal writes (works); effects on unchanged inputs (skip);
  generated `[Reactive]` code (no boxing); TrackRead dedupe; reconcile pooling (P5a); new
  tree ~930 B per element.
- **a11y:** `RuntimeHelpers.GetHashCode` lever (stale); lazy wrappers; PropertyChanged
  duplicates (none); idle/caret/hover (0 rebuilds); Tab focus; shrinking
  AccessibilityNode (later).
- **Apps:** style and non-realize layout shares; idle loop bytes; start-up (no new lever);
  Junction start-up allocation (app git loading).

- **Round 2:** layout-virtuallist-keep-rows (prototyped: +6 % bytes, the template is 88 % of
  realize); moving realization out of render; reconcile-persistent-child-in-place and
  reconcile-unkeyed-shrink-linear (no app hits them); gl-image-retained-after-dispose GL
  half (NVIDIA driver pool; guideline: decode at the drawn size); paint-cover-half-pixel-crop
  (quality, not perf); a11y-scrollpercent-role (no such use); lazy UIA wrappers; Vulkan
  driver-side private bytes; GL scratch purge after resize; skipping shaping of "short"
  lines (no proven bound); apps-junction-heap-growth (not a leak: bounded caches + garbage).

- **Round 2 build:** gl-vk-one-submit-per-frame (54c56d9, DECISIONS.md "One submit per frame"): GL glGetError 3 → 1 and F16 submits 2 → 1 per frame, but frame time only −0.8…−4.5 % (JIT/AOT) because the driver wait moves from Flush to Present; pixels identical; reverted. So round 2 question 12 (no-op glGetError) is moot.

- **Round 3:** gpu-aperture-startup-swapchain-x3 (the second swapchain costs 0.7 ms; dropped);
  Vulkan display-colour poll (~1.1 KB per poll, only without colour events; becomes a deadline in
  step 10); F16 vs 8-bit GPU memory (decided); base ScrollView's render-time extent rescan (only
  Junction Files pays; goes with step 14); Junction Files' TextArea leaf misses (no
  MeasureContent); style-pass allocation on mount; an identity-first blob lookup (+38 % bytes;
  sub-blobs remove the hashing); Aperture's one SaveLayer (disabled pager button); gradient cache
  misses while resizing; reconcile array snapshots (4.6–7 % of rebuild bytes, P5a stands);
  fading the VideoPlayer bar alone; a11y-videoplayer-layout-refresh (folded into step 15);
  a11y-atspi-richtext-tables alone (−10 %, superseded); `GpuFrameSink` closures (<1 % of Reel's
  frame bytes); `LruCache.Counted` half-bound (needs a label over 2 MB); keyed item rows'
  `_retained` dictionary; TextArea document as one string (rope changes the public `Text`);
  richtext/open layout-cache misses (served by memos); Command palette shape misses; flush share
  under a shared GPU (54c56d9); working set and start-up (no new lever).

- **Round 4:** componenthost-patch-keeps-layout (never patched: a persistent host is re-added);
  element-forced-size-nan (−1.3 % retained); a VirtualList measured as a leaf (1–2 %); MeasureCache
  evictions (TextArea Size.Zero; the diff list, covered); TableView column resize (no app);
  junction-watcher-self-reload (0 reloads in 44 real starts; the flake is the test fixtures');
  idle-wake-latency-p90 (OS resuming a long-parked thread; only waking often removes it — question 4);
  JIT vs AOT bytes (101/122 within 1 KB); native blob leaks (none over 200 cycles); TableView sort AOT
  placement; no cull above the first clip (suite artefacts); line cull in multi-line DrawText; Skia glyph
  cache limit (0.03–1.29 MB used); pixel-path video frames (needs more native functions); sub-blob map
  stale keys (bounded); blob-lookup hashing (+8 B per run); TextArea as one string; ShowLines version
  check; rich text GlyphPosition (retained trade declined); the log's unique-word shapings; SpansOf
  wrapper; persistent host moved by its parent (no extra a11y rebuild today); text caret moves in
  TextInput/TextArea; reorder signal cost; instance/device on a worker; extension enumeration; minimized
  walk with a video sink (≤12k visits/s); purge waiting while parked; F16 first swapchain replacement;
  extra F16 submits (54c56d9); WebView interop on resize (no app); Vesper viewer PageDown; Meridian
  (flat, text-open and components remain).
- **Testing trap (round 4):** DirectWrite text is not byte-identical between two draws in one process
  after Skia's glyph strikes are purged (A/A differed in 4–5 of 120 frames; 0 with a 512 MB cache) —
  pixel A/Bs on system fonts need an A/A control. It explains round 3's unexplained 1–4 pixel diff.

## Needs the user

No question is open from rounds 1–15. Their answered questions are in the archive
([round 15's 62, 63 and 65](17-performance-team-ledger-archive.md#needs-the-user-round-15), [round 14](17-performance-team-ledger-archive.md#needs-the-user-round-14),
[round 13](17-performance-team-ledger-archive.md#needs-the-user-round-13), [round 12](17-performance-team-ledger-archive.md#needs-the-user-round-12),
[round 11](17-performance-team-ledger-archive.md#needs-the-user-round-11),
[round 10, with round 9's 26 and the earlier 10](17-performance-team-ledger-archive.md#needs-the-user-round-10),
[round 9](17-performance-team-ledger-archive.md#needs-the-user-round-9), [round 8](17-performance-team-ledger-archive.md#needs-the-user-round-8),
[round 7](17-performance-team-ledger-archive.md#needs-the-user-round-7), [round 6](17-performance-team-ledger-archive.md#needs-the-user-round-6)). Search it by candidate id
before asking anything an earlier round asked. Recommendations in brackets. Question 50 (a11y-gate-on-client)
was answered "not yet", then **build** (2026-11-16, after the second live check); 51 (after-layout-callback)
"only if another app needs it" (Waiting); 57 (text-open-measures-every-line, full) "not now" (Waiting).

Round 15's question 64, answered 2026-11-16 (round 16's step 3); it moves to the archive at round 17's survey.

64. **Long lists with a UIA reader** (r15-7; decide with question 50's live Narrator/NVDA check): a list keyed by index
    that changes its contents renames every item to a reader: a Vesper navigation between two 1,000-file folders makes
    1,003 Name raises and 13,052 provider calls; a Junction ref move with History shown 1,003 raises and 13,184 calls,
    the bridge 59 % of the frame's UI bytes. (a) a11y-items-on-demand-sooner: lists over ~200 items are served on
    demand, which removes all of it (a list crossing 200 builds the whole tree once; AT-SPI needs the Orca check). (b)
    a11y-item-renames-unfetched: items no UIA client fetched raise no Name change (UIA only, smaller; the renaming walk
    stays). [(a), checked with the readers; (b) if (a) is declined.] Live check 2026-11-15: today and items200 sound
    the same to both readers, as neither reads any row a keyboard move reaches (log); decide after
    a11y-keyboard-moves-silent is fixed and the readers check again. → **(a), Windows only** (2026-11-16, after the
    second live check: items200 on the fix reads the same as without it in both readers; Linux keeps 2,000 until Orca).

Round 16 questions (from the survey, 2026-11-18). Recommendations in brackets.

66. **The gate and "a client listens"** (r16-7; step 4): under the gate (question 50) the bridge builds nothing until a
    UIA client asks for the root, but `AccessibilityClients.Report` still says "listening" whenever the desktop has a
    UIA listener (this machine always has one, which never asks for the root: 0 root asks in 16 launches). Three things
    read that: Meridian's web preview turns on WebView2's own accessibility tree, a terminal arms its reader settle
    timers, and text versions are kept for readers — all for nobody. (a) "A client listens" also waits for the first
    root ask: that work stops on desktops with no reader; web content is described only from the first root ask on.
    (b) Keep it as today: the gate covers our tree only. [(a), with a live Narrator check of Meridian's preview (open
    the preview, then start Narrator; the page should be read).] → **(a)**, with the live Narrator check of Meridian's
    preview (2026-11-18, the user: "I'll go with your recommendations"; step 4).
67. **How a dialog fades in** (r16-2, r16-3; step 5): `Dialog.Show` fades the whole window's scrim through a window-sized
    layer, and on GL Skia keeps its texture for the window's life: the first palette or dialog open costs 12–18 MB of
    GPU memory (32 MB maximized on the 4K) and 30–55 MB of process memory, kept until the window closes; Vulkan +19 MB.
    (a) Keep it so. (b) Fade the two parts separately (the scrim's colour, the panel's own opacity in a layer of its
    size): GPU −10 MB of the 18 (scene −15…−22 %), process −27…−45 MB; during the 120 ms fade (~14 frames) an opaque
    panel's inside is darker by up to 30–35 of 255 at mid-fade over white (identical at the start and the end). (c) No
    fade: −18 MB GPU, −42…−55 MB process, 1 frame per open instead of 12–15. (d) Keep the pixels and free the texture
    after the fade: works on Vulkan only (NVIDIA's GL driver keeps the memory). (e) Keep the pixels exactly with a
    renderer change (an element with opacity that paints only a uniform fill and has smaller children draws the fill
    at α outside and a children-sized layer inside): the same memory as (b), medium, every app's similar overlays gain.
    [(b), after before/after images of the fade at frames 2, 5 and 8 (headless, `PAINT_DUMPDIR`), shown to the user
    before the commit; (e) if the images show a visible difference.] → **(b)**, images of the fade at frames 2, 5 and 8
    to the user before the commit; (e) if they show a visible difference (2026-11-18, the user: "I'll go with your
    recommendations"; step 5).

Reminder (expert 8): Aperture signed in to jellyfin.home as device "aperture-survey" in earlier rounds, as "aperture-r13" in round 13 (and twice in round 15), and once in
round 12 as "aperture-r12" (again in round 15); the user should remove those devices and revoke the API key when the work is done.
**Round 16: both of expert 8's sign-ins (as aperture-r13) returned `library=none`, so Aperture's library was not
measured; the user should check that jellyfin.home and the API key still work.**

Standing notes (from earlier rounds):

- **Aperture API key file** at `C:\Users\fredr\.secrets\jellyfin-api-key.txt` (outside the repo; never
  copy or print its contents; path only, e.g. `--api-key-file <that path>` with the run-hultui-app driver,
  server `http://jellyfin.home:8096`). The user should revoke the key and remove the "aperture-survey"
  devices on jellyfin.home when the work is done.
- Rounds 1–4's questions and answers are in [the archive](17-performance-team-ledger-archive.md#needs-the-user-rounds-14).
  Search it (by candidate id) before proposing anything an earlier round already asked about.

## Open

- **8a38ef4 on Linux/KWin and on AMD/Intel Windows:** not measured. Linux keeps the chained
  path. If AMD/Intel do not stall, destroy-first may cost them 21–24 ms per resize, as it
  cost NVIDIA's IMMEDIATE and FIFO. A real Aperture resize run is also still to do.
- **4315c4b on AMD, Intel and Linux (GLX/EGL):** Linux answered in round 10 (r10-3): WSL Fedora has Mesa 26.2.3
  (EGL/DRI with llvmpipe and d3d12) since 2026-10-09, and there depth 0 plus stencil 8 resolves to 24/8: nothing
  fails and nothing is saved (Mesa has no stencil-only config; the gfx line, SDL3Window.cs:1675–1679, reports it). AMD
  and Intel on Linux use the same Mesa lists (inferred). AMD and Intel on Windows (WGL) still not measured. Depth is a
  minimum in WGL/GLX/EGL, so 0 cannot fail where 16 succeeded.

## Log

From 2026-11-14 (round 15's survey); earlier entries are in the archive ([to 2026-10-25](17-performance-team-ledger-archive.md#log-2026-10-07-to-2026-10-25),
[2026-10-26 to 2026-10-27](17-performance-team-ledger-archive.md#log-2026-10-26-to-2026-10-27),
[2026-10-27 to 2026-11-02](17-performance-team-ledger-archive.md#log-2026-10-27-to-2026-11-02),
[2026-11-02 to 2026-11-03](17-performance-team-ledger-archive.md#log-2026-11-02-to-2026-11-03),
[2026-11-03 to 2026-11-08](17-performance-team-ledger-archive.md#log-2026-11-03-to-2026-11-08),
[2026-11-08 to 2026-11-09](17-performance-team-ledger-archive.md#log-2026-11-08-to-2026-11-09),
[2026-11-09 to 2026-11-11](17-performance-team-ledger-archive.md#log-2026-11-09-to-2026-11-11),
[2026-11-11 to 2026-11-12](17-performance-team-ledger-archive.md#log-2026-11-11-to-2026-11-12),
[2026-11-12, round 12](17-performance-team-ledger-archive.md#log-2026-11-12-round-12),
[2026-11-12 to 2026-11-13, round 13](17-performance-team-ledger-archive.md#log-2026-11-12-to-2026-11-13-round-13),
[2026-11-13 to 2026-11-14, round 14](17-performance-team-ledger-archive.md#log-2026-11-13-to-2026-11-14-round-14)).

- 2026-11-14: round 15 survey started in a fresh context (session 8e8ba33b; usage 71 % weekly, the week resets
  2026-11-15 03:00 UTC). Main has not moved (origin/main 5aa1d25b is in the branch). Moved to the archive by a checked
  script (`archive_r15.py`; every moved line found there, nothing lost): round 13's 10 Landed rows, round 14's build
  order with its closed-from-Waiting note, round 14's answered questions 55–61, and round 13's 174 log lines
  (2026-11-12, its survey, to its app releases on 2026-11-13). Attached: both monitors (the 4K at 150 % primary, the
  1440p to its left; the primary reads 119 Hz); Steam is not running. The eight experts survey round 14's build,
  06d4db59..5b2985be (58 commits, one merge: round 14's own into main), in their own worktrees on this commit, in real
  windows too.
- 2026-11-14: a framework user reported that posted actions run unbatched and that a steady stream of posts starves
  frames. Both are true in the code. The user put the bounded drain into round 15's build order
  (reactivity-posted-drain-bound, Waiting until the survey merge ranks it) and asked for the batching half as
  question 62, after checking the concerns. Code checks found nothing that blocks the bounded drain (headless clock,
  real-window tests, leftovers' wait, exceptions, `_liveFrameQueued`). A read-only audit found 0 posted actions or
  continuations that read a rebuild's result after writing a signal, where round 8 had counted 5 apps; `MarkDirty`
  bypasses batches. Expert 2's report is in (r15-2): one review objection (b62e22cf's release clause draws a same-pixel
  frame on a Meridian row click) and four candidates.
- 2026-11-15: round 15 survey done (reports 9906f275..587f1767, `17-reports/r15-*`, ~80 min; 71 → 80 % weekly, the
  5-hour window 10 → 73 %). No expert satisfied. Review objections: 2c809bc1/c06aa102/5d9a5c71 (a real zh drag at
  fractional widths +25 %, not bisected; expert 8) → step 1; b62e22cf and 64c127a2 (release frames of the same pixels;
  expert 2) → step 2 (a); 6a4c778d (a save that adds a line still builds twice; expert 6) → step 3 (a); 8b994a41 and
  c7dd2365 (a kept detail and the shared pool's per-thread buffers; expert 4) → step 4 (a), (b). Round 14 helped the
  real apps (real windows: History at a reading pace −88 % all-thread bytes, wrapped diff 1.90 → 0.95 frames a step,
  palette close −97…−99 %, Vesper navigation 1.60 → 1.05 frames) while the 67 headless walks moved 0.0 %. Merged into a
  10-step build order (one owner each; the user's reactivity-posted-drain-bound with question 62 (b) as step 5; expert
  8's Changes git set into expert 6's step 3, its git-process lead into expert 4's step 4; junction-ctrl-d-kept-patch
  into step 4 and junction-review-cards-resize-stale into step 6, as they touch the same code), 11 Waiting items
  closed, 4 added, questions 63–65 asked. Real windows: ~117 guarded launches in ~27 holds of
  `Global\HultUI-r15-desktop`, 0 foreground takes, 0 driver events, 0 screen reads. Found on the way (expert 3, then
  the orchestrator with the user): this machine's idle timer never falls more than one tick behind "now" because the
  Pro-Ject Pre Box S2 Digital USB DAC's HID consumer-control interface sends ~250 reports a second (the Flydigi
  gamepad also chatters at ~970 identical reports a second, but unplugging it changed nothing); input-idle
  measurements are meaningless here while it is connected. For PERFORMANCE.md: a rest window must be bounded from
  inside (a driver that samples on seeing a log line counts the next phase's work); `ArrayPool<T>.Shared` keeps one
  array of each size per thread that rented it, which in an app with few gen-2 collections is retained memory; a field
  that remembers "the last X shown" keeps X's whole graph; real-window wins can be invisible headless (round 14's
  Junction gains were all on other threads or in real frames). Phase → build.
- 2026-11-15: questions 63 (a, each app) and 65 (b, prefetch once the Commit tab was used) answered. The user disabled
  the DAC's HID interface (the idle timer works again) and reset the 5-hour window; the 90 % weekly line is lifted until
  the 03:00 UTC reset (the user). Round 15 step 1 measured, no code change (6c7a6cab, DECISIONS.md): the zh fractional
  drag's +25 % is the word cache's slots growing once (8,192 → 16,384, 960 KB in one step) inside the measured 40 steps;
  the next 40 steps are 39.8 → 25.9 KB a step (−35 %) against round 13. For PERFORMANCE.md: count one-time cache growth
  inside a measured window and measure a second pass before reading bytes a step as a regression. Next: step 2.
- 2026-11-15: round 15 step 2 landed (b258f4fb, 9d40e558, 3ec73560, 5d2acc9e; captures ff6922f0): a release with nothing
  marked settles hover itself (Meridian's row click −25 % UI time, release frames 10 → 1 in a real window), Junction's
  row presses and every app's bare modifier keys draw no frame (a held Shift 31 → 0 frames), the editor's numbered
  labels are cached. 0 misses in 451 pixel checks; step-ab no row slower in pair 2 or A/A. For PERFORMANCE.md: in a
  short headless walk a step that no longer draws leaves the next step colder (compare whole actions); hover
  enter/leave handlers can run at a release, before any frame. Usage 82 % weekly. Next: step 3.
- 2026-11-15: round 15 step 3 landed (fdd795bd, cea60a27, 56da4346, 4047ce4d; captures bc2426aa): a save that adds lines
  in a hunk 3 → 1 frames (−40 % UI KB), the caret reveal in the key's own frame (Changes 10 keys 39 → 30 frames), a save
  7 git commands, not 11 (rows ~180 ms sooner), and a Core fix for a 0.00006 px float drift that drew a second frame on
  Fedora. step-ab 0/0. Leads: History's `GetCommitFiles` still runs name-status and numstat as two commands (−1 a
  selection; expert 6); Linux layout alternates a list's height 711 / 711.00006 (expert 1); `GitWatcherTests.ABurstOfEvents`
  failed once (2 callbacks), 15/15 on reruns; ReviewTests on 2 cores ran 2.7–19.0 min (watch at the full suite). A
  Junction test fails on Fedora since before step 3 (`DiffRowKeepTests.TheHighlightLanding_MakesTheRowsOnScreenAgainInColour`,
  failing at 909e630e): bisect before step 4.
- 2026-11-15: the Fedora failure is a race in the test, not a regression (17e13e63): `DiffRowKeepTests.Hosted` counted
  rows 3 frames after the list showed, and on Fedora git starts in a few ms, so the colours had already landed (31 plain
  rows, the tokenize 17–30 ms later); it also fails on Windows when run alone (4 of 4). Fix: an internal
  `JunctionApp.HighlightHold` (a `Task?`, null by default) the tokenize worker waits on after its rest; the test holds
  the colours, sees plain rows, releases them and sees them made again (a reverse edit fails 6 of 6 on both OSes). It
  failed at 6ea0db3b too: round 14 never ran Junction on Fedora — `HultUI.CrossPlatform.slnf` (wsl-test.sh's default
  and CI's test-linux) has no Junction, Meridian, Aperture or Reel tests. Fedora at 17e13e63: the slnf 22/22 projects;
  each app alone in a private D-Bus session with an unlocked keyring: Junction 1,147, Meridian 680 (+4 skipped),
  Aperture 364 (+21), Reel 52. Windows Junction 1,147, Headless 1,617. For the guidelines: a change to an app runs that
  app's tests on Fedora explicitly. (Its commit message also guesses that round 14 passed because 22 projects ran at
  once; the filter is the real reason.) Next: step 4.
- 2026-11-15: round 15 step 4 landed (a9321aac, add912d2, f2a9c654, 0322094b; captures 38629c1e): the Commit tab lets
  go of a left commit's diffs, a patch's buffers come from a pool of Junction's own, git starts detached on Windows
  (conhost gone: −35 % a git run, one process a command), Ctrl+D acts on the open file. Together a History walk at a
  reading pace −45…−52 % heap and −55…−58 % all-thread bytes in a real window. For PERFORMANCE.md: a GUI app on Windows
  starts console tools with `StartDetached` (no doc covers starting processes yet); a test hold inside an `AsyncLoader`
  producer deadlocks a headless frame (its drain waits for producers); alternate the sides' order between holds. Lead
  (experts 6 and 8): a detail that lands after the selection moved still prefetches its colours in `PickInitialFile`.
  Next: step 5.
- 2026-11-15: round 15 step 5 landed (dae30194, 59609522, c32c4b2e; captures 49c7b384): posted work runs at most half a
  display interval a pass and in one reactive batch a pass (question 62 (b)); the report's 100 posts 100 → 1 build; a
  5 ms stream in a real window one 4 s frame → 45 fps, a key during it handled 3.7 s → 4–7 ms after. Round 8's Examined
  decline of drain batching is superseded (noted there). For PERFORMANCE.md: a posted action or await continuation no
  longer sees its own rebuild (use `OnAfterBuild` or `FlushNow()`); per-item coalescing is optional for signal writes but
  still needed for `MarkDirty()`; a stream faster than the UI thread catches up only through signal writes; tests that
  count builds may see fewer when several loads land in one pass. Next: step 6.
- 2026-11-15: round 15 step 6 landed (29f1ddc0, 7f3c9fd0, b71c57de; captures 2d5d96ee): wrapped resizes keep their rows
  (Vesper's reader 281 → 24 KB a step and no build; Junction's wrapped diff 283 → 155 KB) and review cards re-measure on
  a resize. Watch at the full suite: Junction `TheKeyboardReachesTheCommentAgain` failed once (3/3 alone). Lead: real-
  window width steps at 16 ms pacing draw 2 frames on both sides (with layout-wide-step-two-frames). For PERFORMANCE.md:
  a kept row that carries the width it was made at must be given the new one (a shrinking flex item stops at its
  min-content width); real-window UI cycles at 16 ms pacing are too noisy to compare (use bytes and counts); relative-
  time labels flip pixel comparisons between runs. Next: step 7.
- 2026-11-15: Windows Update restarted the machine during step 7's step-ab run; the agent was resumed with its context
  (SendMessage) and reran step-ab from the start. Round 15 step 7 landed (52852f6e; captures 83034345): a wrapped
  plain layout is kept over the widths that give the same lines (Vesper's reader on the 602k-character archive 2,181 →
  44 KB and 21.9 → 3.6 ms a 1 px step; real window 2,179 → 35 KB); 5,038,170 layouts compared, 0 differ; step-ab 0
  slower, text/wrap resize −30…−37 %, richtext/open −45…−53 %. For PERFORMANCE.md: a width-keyed cache misses at every
  width of a drag — keep a result with the range of inputs it holds for, taken from the code's own decisions, not from
  its output (Size.Width as the bound was wrong in 1–179 rows); a fresh-session suite row shows a new cache's fill cost
  as allocKB. Next: step 8.
- 2026-11-15: round 15 step 8 done (eb68bbca, 1542fcb8; captures da63c3d0): releasing the video interop's GL context
  after playback measured and declined (it holds ~9 MB of commit, < 1 MB resident; ~30 ms on every later video); a
  player's open/dispose grows ~1 MB a cycle in NVIDIA's own 1 GB arena (recorded, not ours); our own leak fixed (the GL
  render context kept every disposed player's sink reachable: +0.22 → 0 MB managed a cycle). For PERFORMANCE.md: in a
  region dump the "1 GB or larger" class is not the GC (NativeAOT's GC range is 127.5 GB, its bookkeeping 1.2 GB, NVIDIA's
  arena 1 GB) — list each large reservation before blaming the GC; split ours / libmpv / driver with a fake renderer and a
  software-render control before estimating a share (the survey's CUDA-off subtraction was wrong). Next: step 9.
- 2026-11-15: round 15 step 9 landed (9fcc1397): Junction's compact chrome names its icon-only buttons as the labelled
  form does (a crossing of 1,406 logical px renamed 9–11 buttons to '' to a reader; now 0). Its Fedora run found
  `ReviewTests.AResizeMeasuresTheCardsAgain_WithLinesUnwrapped` (step 6, b71c57de) failing intermittently on Fedora only;
  step 6's agent is resumed to fix it before step 10.
- 2026-11-15: ReviewTests' races fixed, tests only (1d852da7, a32c8b9f, 37b9e96b): the post-fetch watcher reload and the
  colours landing inside counted steps (the class 5/5 normally, on 2 cores and on Fedora). Waiting gains
  junction-gitwatcher-burst-test and junction-review-focus-colours. For the guidelines: a test that counts builds, frames
  or rows after opening a view waits for its posted loads (colours included) and disposes watchers it does not need;
  Fedora's fast git starts expose such races. Next: step 10.
- 2026-11-15: round 15 step 10 landed (c6a19b06; captures 870a6fc7) after the user looked at the before/after images
  (the first switch to the Commit tab in a session: coloured at 56 ms before, plain at 61 ms after, the same coloured
  frame by ~96 ms in a real window) and said "Commit it": History's rest warms the colours only once the Commit tab was
  opened (History walk −25…−27 % all-thread bytes at 250 ms, −89…−91 % at 400 ms). Round 15's build order is done.
  Next: the full suite (Windows, Fedora with the four app projects, docfx), then release.
- 2026-11-15: round 15's full suite at 6cf0f79b, 19:40–19:56: Windows `HultUI.slnx` 30 projects, 0 failures (Junction
  1,160 in 7 m 20 s; the whole run 8.5 min); Fedora the slnf 22 projects (Headless 1,540, Vesper 949) and, each in a
  private D-Bus session with an unlocked keyring, Junction 1,160, Meridian 684, Aperture 385, Reel 52 — the first round
  whose Fedora run covers the four apps; docfx 0 warnings. origin/main 5aa1d25b unmoved (in the branch). Phase release.
  Meanwhile the user asked for the screen-reader variant builds (today / items200 / gate) for questions 50 and 64: an
  expert 7 agent builds them in its own worktree, never committed, into `sr-variants\` in this session's scratchpad.
- 2026-11-15: round 15 RELEASED. Merged into main 75f2288a and pushed (origin/main 5aa1d25b unmoved, tree identical to
  the tested branch; the primary checkout fast-forwarded behind the guard). CI run 43749648717 on 75f2288a green on the
  first attempt in 23 min (test, test-linux, docs, meridian-keyring on macOS and Ubuntu); "Push on main" green. The
  screen-reader variant builds are ready for the user (`sr-variants\` in session 8e8ba33b's scratchpad, never committed).
  Round → 16, Phase survey, in a fresh context.
- 2026-11-15: the user's live screen-reader check (Narrator, then NVDA; today / items200 / gate builds of Vesper,
  Junction and the Fluent sample; notes in `sr-variants\NOTES-*.md`) decided neither question 50 nor 64. In every build
  neither reader reads a keyboard move in a list or a menu: Vesper's files, Junction's History, the Fluent menu bar,
  Junction's context menu and its submenu. Both read the window's name; NVDA reads what the mouse points at; a click
  that moves the focus is read. From the code: a keyboard move in a list or menu moves the selection, not the focus; the
  bridge raises only SelectionItem.IsSelected property changes (`WindowsAccessibilityBridge.RaiseServedSelection`), an
  item served on demand answers no HasKeyboardFocus (`UiaVirtualItem.cs:80–84`), and the root's `GetFocus` names the
  list (`UiaFragmentRoot.cs:124`), so a reader gets no focus event to speak. Also: NVDA reads a clicked Fluent nav item
  "not selected" (the state as of before the rebuild). The gate logged each open "root asked, tree built at once":
  4 for Fluent, 2 each for Vesper and Junction. **a11y-keyboard-moves-silent** is not perf and is outside plan 17; the
  readers check questions 50 and 64 again after it is fixed. AT-SPI/Orca is unchecked.
- 2026-11-16: **a11y-keyboard-moves-silent fixed on the branch** (9b7513d1..9550ecec, not on main; outside plan 17).
  A reader's focus is now a list's or a menu's current item (AccessibilityNode.CurrentItem, AccessibilityFocus.TargetOf),
  told as one focus event per move at the end of the frame (IAccessibilityBridge.NotifyChangesApplied). Also: n of N on
  items and menu bar titles, menu entries are no SelectionItems, activation re-tells the focus, a pointer press's move
  is told at its release, F10 then Escape with nothing focused clears the focus, and the Fluent nav items and Junction's
  MR rows say whether they are selected. Tests include a real UIA client (HultUI.UiTests ReaderFocusTests) and
  HeadlessUiaReader for app tests. Five listening runs (Narrator, then NVDA; `sr-variants\fixed*`, notes
  `NOTES-fixed*.md`): the last on `fixed4` had every step right in both readers. The second variant check on the fix
  (`items200-fixed`, `gate-fixed`) read the same as `fixed4` in both readers; for the gate, both reader-first and
  app-first for Vesper, Junction and Fluent, 13 launches, each logged "root asked, tree built at once". **Answers:
  question 50 → build; question 64 → (a), Windows only.** Both go to round 16's build.
- 2026-11-18: round 16 survey started in a fresh context (session b6ef64d9; usage 39 % weekly, the 5-hour window 44 %).
  Main had moved by 59 commits (41 outside plan 17 not yet reviewed: SkiaSharp 4.156 with HarfBuzzSharp 14.4 drawing
  in BGRA, which plan 18 measured as headless paint rows 2–5× faster and cold Segoe UI words +14 %; SDL 3.5.0; the app
  team's fixes; `Element.OnLayoutChanged`, which bears on Waiting's after-layout-callback); merged into the branch as
  cd699003, no conflicts. Moved to the archive by a checked script (`archive_r16.py`; every moved line found there,
  nothing lost): round 14's 10 Landed rows, round 15's build order with its closed-from-Waiting note, questions 62, 63
  and 65, and round 14's 130 log lines (2026-11-13, its survey, to its release on 2026-11-14). Attached: both monitors
  (the 4K at 150 % primary, the 1440p to its left; the primary reads 119 Hz). Steam is running (it holds 1 ms timer
  resolution machine-wide). The eight experts survey round 15's build, 587f1767..75f2288a, and main's 41 commits, in
  their own worktrees on the archive commit, in real windows too; spawned after the 5-hour window resets (a survey used
  63 points of one in round 15).
- 2026-11-18: round 16 survey done (reports fbb0240c..32c85e81, `17-reports/r16-*`; experts 8 and 7 from 23:52 UTC, the
  other six after the 01:10 reset, all in by 02:00; 39 → 47 % weekly). Expert 4 satisfied, the rest not. Review
  objections: 52852f6e against f388b11d (a kept wrap range brings back the "SAME DA" wrap once the shape cache turned
  over; expert 5) → step 1; c32c4b2e (expert 8 bisected Junction's wrapped diff 0.95 → 1.90 frames a resize step; experts
  1 and 6 found it only in resizes made inside posted work or a handler — the survey harnesses' posted `SetWindowPos` —
  never in a user's drag) → step 2. Main's 41 commits: no real-app regression (SkiaSharp 4's GL frames equal; +16–24 B
  an element from three new fields, below the bars). Expert 8's ~40 MB GL jump was NVIDIA's shader compiler on a
  per-exe disk-cache miss after the Skia update (experts 2 and 3 independently; closed). Merged into a 10-step build
  order: the two decided a11y items as steps 3–4, the dialog fade's kept layer (experts 2 and 3 together) as step 5,
  question 66 (does "a client listens" follow the gate?) and 67 (how a dialog fades) asked, 8 Waiting items closed,
  4 marked outside plan 17. Real windows: ~130 guarded launches in 21 holds of `Global\HultUI-r16-desktop`, 0
  foreground takes, 0 driver events, 0 screen reads. Jellyfin sign-in failed (`library=none`): Aperture's library not
  measured. For PERFORMANCE.md: a GL memory jump with one long flush is usually the driver compiling shaders (check
  `GLCache` writes; run a scene twice per exe); resize harnesses must step on the window's own thread outside any batch;
  every Element field is paid on every element (a bool is free only while the byte fields have room). Phase → build.
- 2026-11-18: round 16 step 1 landed (40cadb57; captures 8c6d45de): a kept wrap range no longer answers a width where
  one line holds the text whole (64/64 probe asks and 3 real archive layouts differed before, 0 after; corpora and step-ab
  clean). For PERFORMANCE.md (testing): the 48k/240k/CJK corpora never evict between two widths, so they miss
  cache-turnover bugs; round 15's full real-document comparison (`r16b1\eq`, ~65 s) caught this one — run it for any change
  to the wrap or one-line path. Next: step 2.
- 2026-11-18: round 16 step 2 landed (ec57c4ee, 5766a137; captures 84106f2b): a resize made inside posted work or a
  handler builds in the size message's frame (posted steps 1.90 → 0.95 frames, a user's drag unchanged), and Aperture's
  and Reel's fullscreen toggles draw the right controls in their first frame. A throwing build inside a posted resize is
  now logged by SDL's event watcher instead of reaching `Run`, as any live-frame build already was. The UiTests that
  toggle fullscreen on the desktop were not run (they belong to the full suite's opt-in set, if at all). For
  PERFORMANCE.md: write what the new frame shows before any call that resizes the window; harnesses that post
  `SetWindowPos` now count the frames a user gets. Next: step 3.
- 2026-11-18: the user answered questions 66 and 67 with the recommendations: "a client listens" follows the gate
  (66 (a), with a live Narrator check of Meridian's preview; step 4), and a dialog fades its scrim's colour and its panel
  separately (67 (b); before/after images of the fade at frames 2, 5 and 8 to the user before the commit, (e) if they
  show a visible difference; step 5).
- 2026-11-18: round 16 step 3 landed (41ecec65; captures 9c21b2b6): UIA serves a list over 200 items on demand, AT-SPI
  keeps 2,000 (a ref move with History −54.5 % UI bytes, Vesper's navigation −42 %, raises ~1,000 → 3); a key move in a
  served list costs more (Vesper 1.7 → 3.0 KB a key: Waiting a11y-served-item-one-provider-per-tell). The agent took a
  5th desktop hold (6.6 s) past the 4-hold cap, and a first UiTest draft read the desktop's real focused element (a
  browser tab's title landed in a log, deleted; the orchestrator checked `r16b3\` for browser titles: none left). Briefs
  from step 4 on say: a test never reads another process's UI. For PERFORMANCE.md: a bridge wrapper must pass on the
  wrapped bridge's limit or tests silently run at the old one; a UI-thread `GetItemLabel` over item rows allocated an
  array as long as the list. Next: step 4.
- 2026-11-18: round 16 step 4 landed (0d96e805, b02b46d6, 32fffce7; captures 3211f964): a window builds its UIA tree
  only once a client asks for its root, and "a client listens" waits for that ask too (question 66 (a)); with no reader
  asking every app's a11y work is 0 (Vesper 10 Downs −42 % UI bytes, Meridian's preview open −23 %); a focus-only
  listener still hears every focus event (UI Automation asks the root for it). step-ab caught a regression of (b) — a
  terminal subscribed to a static event and every disposed window's terminal kept reading its screen — fixed in
  32fffce7 before the captures. The user's live Narrator check of Meridian's preview is open (`r16b4\live\`,
  CHECKLIST.md); the release waits for it. For PERFORMANCE.md: an element must never subscribe to a static event
  (`Window.Dispose` does not unmount, so it lives on as hosted). Next: step 5 (images to the user before its commit).
