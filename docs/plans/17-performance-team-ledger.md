# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 13
- **Phase:** build — round 13, step 5 (b) next (apps-vesper-nav-uia-share, expert 7; 5 (a) waits for question 52);
  steps 1–4, 6 and 7 landed (15f97351, 013e1f1b, 8e05c94e; 0da9f80e, 185bedad; 698c2758, 54bd7d3a, b8fd2c67;
  8f9f2683, da72e01b, d413e001, 0a8361f9; 4fdb56cb, 276789d8, cdb47cb9; 154a7ca3, 8048b644). Then 5 (a) once
  question 52 is answered, and 8 (question 53); step 6 (d), the HDR.md note, waits for question 54. The survey is
  done (reports `17-reports/r13-*`, committed da6e72c8..8f01b6fd); 8 steps in the round 13 build order; questions 52–54
  are open (52 shapes step 5 (a), 53 step 8, 54 step 6 (d)); the rest of each step goes ahead. Build briefs
  `brief-r13b<N>.md` in session 17c11f5e's scratchpad quote their report lines; one build agent at a time in this
  worktree, with `scripts/perf/step-ab.ps1` and the desktop lock `Global\HultUI-r13-desktop` for real windows. Round 12
  released: merged into main as afc1ed40; CI run 43269911874 green on attempt 2; app releases junction-v1.26.0,
  vesper-v1.26.0, meridian-v1.9.0, aperture-v1.24.0, reel-v1.10.0 on afc1ed40.
  To resume in a fresh context, run in this worktree: `/loop Run the next step of
  the performance team: follow docs/plans/17-performance-team.md, with state in docs/plans/17-performance-team-ledger.md.
  Read both once per session (afterwards only the ledger's Phase and the part the step needs), do the step the ledger's
  Phase calls for, record it in the ledger, and commit.` Before resuming, `git fetch` and merge main if it moved; build
  steps use `scripts/perf/step-ab.ps1`. Open for the user, not blocking: the live screen-reader check of menus (with
  question 50's bridge gate); another F16 held-drag run (Waiting) only with a new approval. Older rounds are in
  [the archive](17-performance-team-ledger-archive.md) (search it, don't read it whole). History note: rewritten
  2026-10-25 (every commit after 3353ce47 has a new ID; a pre-push hook refuses pushes built on the old history).
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`)
- **Review commits since:** round 14's survey reviews round 13's build, 8f01b6fd..(round 13's release). Earlier: round 13's survey reviewed round 12's build, 5f1744ca..afc1ed40 (the last survey report to the round 12 release; the one merge is round 12's own into main). Round 12's survey reviewed round 11's build, 5731ee58..b0dd1954 (the last survey report to the
  round 11 release; it includes the .NET 11 upgrade's step 0 only as already reviewed). Earlier: round 11's survey reviewed round 10's build and the .NET 11 upgrade, f4d6050d..868e2ffc
  (the last survey report to the upgrade's captures; round 10's merge into main cc55bdcc is in the range). Earlier: round 10's survey reviewed round 9's build, 4919ac40..facced69 (the last survey report
  to the round 9 release; 82 commits, 81 not merges; no merge from main this round). Round 9's survey reviewed
  eaa1ec29..10acc8c0 (round 8's build, with main's merge 3a15b5b1); round 8's survey reviewed 17d4220a..df45b4a2.
- **Probe code:** round 13 surveys in session 17c11f5e's scratchpad
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
| 1. Layout | 13 | no | none (c0ce9387: no hover look misses its repaint, 0 stale frames after 72 keys under a resting pointer; 3f93d54c O(1) a resize; 67fc0884 24–40 B an item an append). Real windows (4 launches): hover moves that draw nothing still make frames (drawn frames with the prototype Junction 244 → 129, Vesper 108 → 38, Meridian 180 → 140) → step 2; Vesper's wrapped viewer builds and draws twice a size change → step 7; posted moves count only while the user's cursor is inside the window |
| 2. SkiaSharp: painting | 13 | no | none (c0ce9387: every paint-time hover read covered, 0 of 7 declined settles differ; 2d18a361: draws, layers, atlas and image textures identical on 8-bit, F16 and GL; no suite draw counter moved ≥ 5 %). Real windows (9 launches): a Vulkan window keeps a stencil for every size it drew at (Aperture, 9 sizes: 21.8 MB, 19 MB of it dead) → step 6; 62 of 62 (Junction) and 77 of 77 (Vesper) hover-only frames pixel-identical → step 2; meridian-hover-frames-real explained (a scrollbar tween at the display's rate) |
| 3. GL and Vulkan | 13 | no | none (2d18a361 sound: mpv's event thread never waits on or posts to the UI thread; 0 loop frames, passes and wakes at rest in 14 launches). The rest win lasts until the first HDR video in a process (NVIDIA keeps its DXGI path: wakes 126 → 256–264 a second) → question 54. The ~112 rest wakes on 8-bit Vulkan are four NVIDIA driver threads, none ours. SDL holds 1 ms timer resolution for the app's life → step 8 (question 53); mpv's event thread wakes twice a second while paused → step 6 |
| 4. .NET runtime | 13 | yes | none (every round 12 commit read: the fallback table 131 KB once a collection; the word cache array 458,776 B, 65 KB under the LOH line, which a 64 B entry would cross; bounded per-thread sets; 0 B a frame in the HDR follower). Real windows (12 launches): 0 B and 0 GCs at rest in 11 of 12; a windowless console holding only a Vulkan device shows the same four NVIDIA threads; SDL's 1 ms examined, no own wake saved |
| 5. Text pipeline | 13 | no | 34dbc7fc's DECISIONS.md note: "cuts at first need" was dropped on the synthetic fixture only; on real text it is the largest share left → step 3. Real ja/zh READMEs: the fallback table answers 98.4–99.6 %; a 40-step drag at 960 logical px (a 1,440 px window at 150 %) on Chinese text shapes 277,414 times and allocates 82.8 MB in a real window (the word cache overflows), 2,096 calls and 1.1 MB with the prototype → step 3 |
| 6. Reactivity and reconcile | 13 | no | b6c6eb6f: on a reftable repository an outside checkout, pull, reset, stash or rebase reaches only the working-tree watcher, so the branch, log and branch list stay stale (8 of 9 outside operations; 10 regressions over 53 real git operations) → step 1. Real window: a no-op fetch reloads everything from its lock files (14 git runs, 4.46 MB) → step 1; watcher events rebuild views no load re-keys, and an in-app action reloads twice → step 4 |
| 7. Accessibility bridges | 13 | no | none (5d973076: a real cross-process client makes the same calls and QIs before and after, wrappers ~220 B made on first touch; 117 event streams identical; audit 0 STALE in 3,649, fallbacks 10; client calls arrive on UIA's worker threads, not the UI thread). Real windows (29 launches): `InvalidateRows` drops Junction History's 1,000 kept labels 4 times at start (−650 KB, −21 % of the start frames) → step 5 (question 52); a desktop listener receives our events without ever asking for a root (question 50's risk) |
| 8. App workloads | 13 | no | 053a35ef and b6c6eb6f add ~0.8 KB to every Junction build (`SummaryFor`'s closures; below the bar) → step 4 (c). Round 12 helped: 67 AOT walks geomean 1.003 headless (Meridian tab switch −19.7 %); real windows (28 launches): commit file walk 1.00 frame a key (from 2.00), Vesper navigation 1.30 (from 2.37), Vulkan rest 110–118 wakes a second (from 207). Lead: the UIA bridge is 38 % of a Vesper navigation's frame time in a real window → step 5 (b) |

## Queued candidates

Rounds 1–12's build orders all landed but round 6's step 8, round 10's step 14 and round 11's step 1 (c), measured
and declined, and round 11's steps 13 and 14, measured with nothing to change (rounds 5–12 are in
[the archive](17-performance-team-ledger-archive.md#round-12-build-order), with each survey's closed-from-Waiting note). Round 13's build order
follows, from its survey.
A build step may bundle one expert's related small candidates as separate commits. Items marked **(user)** wait for a
decision in Needs the user; the rest of their step goes ahead.

### Round 13 build order

Ranked by expected effect in the real apps over cost (the brief's "Real apps first"); the review objection first. Each
step names the report lines its brief quotes (`r13-k` = `17-reports/r13-k-<area>.md`) and measures its real-app case in
a guarded real window (the desktop lock `Global\HultUI-r13-desktop` and the harnesses in session 17c11f5e's scratchpad
`r13-<area>\`) as well as by step-ab. CJK text work uses r13-5's real Japanese and Chinese fixtures (`mkreal.py`), not
round 12's generated characters.

1. **reactivity-junction-watch-reftable + reactivity-junction-watch-lock-echo** (6; r13-6 REVIEW and Candidates 1–2) —
   separate commits. (a) The review objection to b6c6eb6f: when `.git/reftable` exists, GitWatcher watches it too and
   reads the `tables.list` rename as a ref move: over 53 real git operations on both backends 0 regressions and 0 ref
   moves unheard (today on reftable 10 regressions and 16 unheard); Junction headless 8 of 9 outside operations stale →
   0. (b) A lock file alone, or a folder's own Changed, is a working-tree event (for reftable only the `tables.list`
   rename is a ref move), and the reflog keys on DataGen: a no-op background fetch 17 → 5 git runs, UI 253.2 → 24.0 KB,
   all threads 824.3 → 181.5 KB; the toolbar Fetch with nothing new 28 → 17 runs (−22 % UI, −30 % all threads); real
   window 14 git runs and 4.46 MB → 0 runs and 81.7 KB. Dropping lock events outright lost 4 operations: not that.
   Prototype `r13-reactivity\final13.patch` (`R13_REFTABLE`, `R13_NOLOCK`), watcher probe `fsw\`. Small; no API.
2. **layout-hover-move-frames** (1, with 2; r13-1 Candidate 1, r13-2 Waiting) — an input's hover clause (Window.cs:2828)
   asks for a frame only when `DrawnHoverChanges` moved or DevTools is open, the rule c0ce9387 gave the settle
   (Window.cs:3198). Headless sweeps: Junction 297 → 109 frames (−63 %), Vesper 161 → 32 (−80 %), Meridian 303 → 127
   (−58 %); 0 stale over 2,009 moves and 160 move-then-leave checks; expert 2: 62 of 62 (Junction) and 77 of 77 (Vesper)
   such frames pixel-identical. Real windows (moves sent in-process): drawn frames Junction 244 → 129, Vesper 108 → 38,
   Meridian 180 → 140; each removed frame is a full repaint (320–544 render visits in Junction). Prototype
   `r13-layout\probe-r13-full.patch` (`PROBE_NOHOVERCLAUSE`). Small; no API, no pixels.
3. **text-cjk-cuts-at-need** (5; r13-5 Candidate 1 and REVIEW) — (a) a word cut short that has no cuts yet gets them at
   its first cut; (b) a table's last word cut by the table's end is read off the word to its natural end (at most 1,024
   characters, only from a start that is the same at every width). Real Japanese / Chinese READMEs in Junction's Files
   view: HarfBuzz calls at 1000 px −50 % / −64 %, a 40-frame drag −74 % / −81 %; at 960 logical px (a 1,440 px window at
   150 %; real window, Chinese) a drag 277,414 calls and 82.8 MB → 2,096 and 1.1 MB; Latin control 137 → 103 calls; the
   first new width +190 KB (natural-end words made once). Bit-identical: the CJK wrap corpus 0 of 1,344,616 widths, 48k
   and 240k unchanged. Update 34dbc7fc's DECISIONS.md note. Patch `r13-text\proto-cuts-at-need.patch` (~45 lines in
   TextPipeline.cs). Small; no API, no pixels; the time bar to confirm here.
4. **Junction events build and load less** (6, with 8; r13-6 Candidates 3–4, r13-8 REVIEW) — separate commits: (a)
   **reactivity-junction-watch-view-skip**: a watcher event skips `RerenderView` when the last view build read no
   `DataGen` load (another repo's event; a working-tree event over History, Branches, Tags, Files, Commit or Overview):
   another repo −19 % (touched −87 %), History −9.5 % and 2 → 1 frames (touched −88 %); real window a save 186.1 → 168.0
   KB, 2 → 1 frames, empty builds 13 → 4 a launch (`R13_VIEWDG`). (b) **reactivity-junction-own-action-echo**, measure
   first: an in-app checkout reloads again 300 ms later from the watcher (28 → 14 git runs, −23 % UI, −37 % all threads
   against no watcher); read a refs/HEAD/config fingerprint before the own reload and let a refs event reload only when
   it moved (not prototyped; medium). (c) **apps-junction-summaryfor-closures**: `SummaryFor` builds its closures and
   key only when the key changed, in the app (an AsyncLoader state overload would be public API: not that): −0.8 KB a
   build (History Down 29.3 KB a key, +3.4 % since 3f93d54c). Small–medium; no API.
5. **Bridge work the apps don't need** (7, with 8's lead; r13-7 Candidate, r13-8 Candidate 1, r13-1 REAL WINDOW) —
   separate commits: (a) **a11y-labels-kept-across-invalidaterows** **(question 52)**: `InvalidateRows` keeps item
   labels and Meridian's history list passes a new label delegate at a day change (option 2): Junction start with a
   reader −650 KB UI (−21 % of the start frames), each later History row-shape change −217 KB of 547 (−40 %); Branches
   and Tags too. (b) **apps-vesper-nav-uia-share**, split first: in a real window the bridge is 38–40 % of a Vesper
   folder navigation's frame time (1.1 % with `HULTUI_A11Y=0`; bytes only −4 %) and opening the viewer costs +420 KB of
   bridge bytes (0 headless); count raises, calls and walks a navigation with round 12 step 3's probe and cut what a
   reader is not told, reader event streams identical. Small (a), unknown (b).
6. **Vulkan and media at rest** (3, with 2's prototype; r13-2 Candidate, r13-3 Candidate 2 and Waiting) — separate
   commits: (a) **painting-vk-dead-size-stencils**: once a size change has rested, one purge of what was not used since
   (images drawn since are kept): Aperture after 9 sizes 9 stencils (21.8 MB) → 1, GR budget 36.6 → 13.8 MB, private
   188.3 → 163.7 MB; GL is flat (`r13-painting\host`, `PAINT_PURGE`). (b) **media-mpv-event-wait-forever**:
   `mpv_wait_event(_mpv, -1)` (MpvBackend.cs:253; every flag the loop reads already has its `mpv_wakeup`): a paused
   player 2 → 0 wakes a second; a test that Dispose still returns at once. (c) **vk-format-switch-chained**, only if the
   step has room: the HDR → SDR switch rebuilds chained, not destroy-first (311 ms once a transition today). (d) HDR.md:
   after an HDR video the process keeps NVIDIA's DXGI threads and memory **(question 54)**. Small; no API.
7. **layout-vesper-wrap-resize-lead** (1; r13-1 Candidate 2) — Vesper's wrapped viewer and diff lead the body's width by
   the window's delta before layout, as Junction's `LeadDiffWidth` does (`SettleWrappedRowWidths`, CommanderApp.cs:3323,
   from `OnWindowResized` at :3274): a resize step 2 → 1 frames, builds 128 → 64, 373 → 310 KB (−17 %) (`PROBE_VLEAD`);
   the first frame shows correct heights; compare settled pixels. Small, app.
8. **gl-sdl-timer-resolution-at-rest** (3; r13-3 Candidate 1; experts 4 and 8 examined it as not worth it here)
   **(question 53)** — set SDL's `SDL_TIMER_RESOLUTION` hint to "0" before SDL_Init, "1" only while the loop paces
   frames or a tween runs, "0" again after ~1 s with none: the app's own wakes unchanged, NVIDIA Vulkan unchanged (the
   driver holds 1 ms itself); machine-wide clock interrupts 1,000 → 64 a second where nothing else asks for 1 ms (a GL
   app at rest on a quiet laptop). Unmeasurable while Steam holds 1 ms here. Small; Windows only; owned deadlines at
   rest may fire up to 15.6 ms late.

### Waiting

Re-ranked by the next survey.

- **live-reader check of menus** (user) — the six-step script in ACCESSIBILITY-PLAN.md (round 7 step 13) for Narrator,
  NVDA and Orca; a silent move would mean moving focus to the selected item (needs the user's approval).
- **junction-changes-leads** (6/7/8, reader behaviour, no perf number) — only the caret is told to a reader, not a
  multi-selection's other members; a reader's "select" on a folder row clicks it, which opens or closes the folder. (The
  perf parts were examined by round 8: below the bars.) Round 13: still so (round 12 touched no selection pattern).
- **junction-review-old-path** (app bug, not perf; round 7 step 8a) — the review composer sends GitLab `old_path` = the
  new path for a renamed file, because `GitFileDiff.OldPath` is never set (JunctionApp.Changes.cs:122, 124;
  GitRepository.cs:496, 511; `RenamedFrom` holds git's old path). Decide whether review positions on renamed files
  should carry the old path. Reproduces in round 13.
- **dropdown-follow-control-on-resize** (1, correctness, low) — a Dropdown list or DatePicker calendar stays at its
  opening point when a resize moves its control; anchoring changes how a press on the control behaves. Not perf.
- **junction-leads (remaining)** (tests) — `SelectConflict` (JunctionApp.Changes.cs:1477) and a conflict row's
  right-click have no test of their own (not perf; still so in round 13).
- **vesper-brief-filter-clear-reveal** (1, not perf; round 9 step 10) — clearing a filter with Backspace leaves Brief's
  cursor column out of view (the scroll is clamped to the narrower filtered layout; the sideways reveal has no
  apply-after-layout form like the vertical one). The scrollbar half was fixed by round 10 step 15 (`BriefScrolled`,
  CommanderApp.Views.cs:616–625; r13-6).
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
- **controls-menu-max-height-scroll** (1; not perf: no maximum height or scrolling), **layout-virtuallist-spacer-stale**
  (1; invisible; round 11 step 4: confirmed wrong but invisible, 209 stale spacer heights; marking the spacer needs its
  own proof).
- **aperture-poster-as-hero**, **aperture-tmdb-size-fallback**, **junction-narrow-gitlab-glyphs**,
  **meridian-pane-choice-not-persisted**, **meridian-min-width-wraps** (apps; not perf). Round 13 (r13-8):
  meridian-pane-choice-not-persisted reproduces (MeridianApp.cs:148, flipped at :340, never saved); the min-width wrap
  at 760×540 stands on Windows (46fccd88 keeps 448 / 1320); the others' code is unchanged.
- **a11y-label-under-named-button** (7, lead; round 10 step 10) — a label under a name-from-content button, changed with
  no structure change, still builds a tree through the leaf patch; round 11 measured 60 rows 16.74 → 0.08 KB a change,
  and no app pays it (prototype `r11-a11y\apply-proto.py`); one rare case, Vesper's transfer overlay "Pause  P" /
  "Resume  P". Round 13: no new case.
- **syntax-shared-registry-rest** (5; parked) — Typst, Julia, PHP, Razor and HTML still use the shared registry (round
  11: `.typ` +42,419 KB kept); the user (question 42): no Typst files; parked.
- **junction-chromeparttests-load** (tests) — two `ChromePartTests` failed once under load (round 10). Round 13 (r13-6):
  no recurrence; since b6c6eb6f one case (a remote added) arrives through the real FileSystemWatcher alone, a new
  load-sensitive point. Watch at the full suite and in CI.
- **fedora-local-dotnet** (environment) — once Fedora ships the final `dotnet-sdk-11.0`, remove
  `/usr/local/share/dotnet` and both links, `/usr/local/bin/dotnet` and `/usr/local/sbin/dotnet` (`which` picks the sbin
  one). Round 13 (r13-4): Fedora 44's updates have only 11.0.100~rc.3; `wsl-test.sh` follows the dotnet on PATH.
- **f16-held-drag-post-stall** (3; one survey stall, round 11 step 13's approved re-run clean) — cause unknown. Round 13
  (r13-3): the only F16 drag left is during HDR playback, where mpv posts every frame (the stall's precondition); a run
  needs an HDR clip playing on the 1440p at 200 Hz and a new approval from the user.
- **Suite scenarios** (8) — (a) an idle Vulkan window counting every thread's wakes: keep, low, only as a gated UiTests
  guard of round 12 step 5 (116 against 212 a second). (b) frames per key under a real loop and (c) an app-shaped shell
  resize: closed in round 13 (DiffFrameCountTests covers (b); (c) went with a11y-structural-walk-during-resize).
- **findbar-count-not-announced** (7, reader behaviour, not perf) — a find bar's new "n of m" reaches the bridge as a
  Name change on the count label; a reader speaks it only if it moves there (no `Announce`, no live regions).
- **vesper-start-focus-event** (7, lead) — at start Vesper raises a focus-changed event for a window that never had the
  foreground (round 13: 5 of 5 launches), and in 2 of 5 refuses a leaf patch and builds a second tree (~53 KB), start
  only. Cause not isolated; not perf.
- **a11y-gate-on-client** (7; question 50: not yet) — build the tree and raise events only after a UIA client asks for
  the window's root. Round 13 (r13-7): 0 root asks in 22 launches; the bridge costs Junction +40 % UI bytes at start and
  +29 % a commit-walk key, Vesper +29 % a drag. New risk evidence: a desktop-wide listener receives our bounds,
  structure, name and focus events (13 provider calls each) without ever asking for the root; a gate would cut it off.
  Decide after the user's live check with Narrator and NVDA (it could go with the menu check).
- **after-layout-callback** (1; question 51: only if another app needs it) — a public hook after layout and before
  render. Round 13 (r13-1): Vesper's wrapped viewer also needs a laid-out width, but a width lead fixes it without a
  hook (step 7); Aperture's and Junction's other after-frame reads are reveals or focus. Still not needed.
- **vk-format-switch-chained** (3; round 12 step 5; round 13 step 6 (c) not attempted) — switching F16 off rebuilds
  destroy-first: 325 ms in Reel (the 8-bit create alone 311 ms); the switch on is chained (2.7 ms plus a 96–154 ms first
  present), so a chained switch off would likely save ~200 ms once an HDR → SDR transition. Small; mind memory
  `resize-stretch-fix`.
- **text-cjk-row-start-suffix** (5, lead; r13-5) — after step 3 the largest HarfBuzz source: a row-start word read off
  the line's word (Japanese 300 of 1,051 calls at 1000 px, 216 of 453 a drag; Chinese 201 of 1,165, 77 of 403). Needs
  the slice's own script guess and safe cuts at both ends. Medium–large; up to ~20–30 % of a new width's calls.
- **junction-git-optional-locks** (6, lead, not measured; r13-6) — Junction's read-only git with `GIT_OPTIONAL_LOCKS=0`:
  `git status` then no longer takes `index.lock` and rewrites the index on the first status after each save (332 KB on
  jrepo), so no lock contention with the user's own git (a 2026-11-09 test hit it), and the watcher could stop ignoring
  index renames (a terminal `git add` or `restore --staged` is never heard today). Behaviour change: background reads no
  longer refresh git's stat cache. Measure first, then ask the user.
- **junction-commit-first-visit-frames** (8, lead; r13-1 REAL WINDOW) — in a real window a file's first visit in the
  commit walk draws 2 frames (the key's build, then the async diff load's) plus an accessibility-only frame; headless
  the load lands before the frame; revisits draw 1.
- **junction-commit-detail-whole-patch** (8, lead; r13-4) — each commit-detail load reads the whole patch (two fixture
  commits 6.1 M characters): History Down 357 KB on all threads a key against 29 KB on the UI thread, 3 loads after the
  selection rests, off the UI thread.

Closed from Waiting by the round 13 survey (→ Examined unless named): hover-move-frames-nothing-shows (→ step 2),
vk-format-switch-chained (→ step 6 (c)), size-change-restore-second-frame, F16 kept drag targets,
meridian-hover-frames-real, a11y-structural-walk-during-resize, aperture-cursor-guard-textarea.

## Landed

Round 12 (the round round 13's survey reviews; it moves to the archive at round 14's survey). Rounds 1–11 are in
[the archive](17-performance-team-ledger-archive.md#landed-rounds-14) ([round 5](17-performance-team-ledger-archive.md#landed-round-5),
[round 6](17-performance-team-ledger-archive.md#landed-round-6), [round 7](17-performance-team-ledger-archive.md#landed-round-7),
[round 8](17-performance-team-ledger-archive.md#landed-round-8), [round 9](17-performance-team-ledger-archive.md#landed-round-9),
[round 10](17-performance-team-ledger-archive.md#landed-round-10), [round 11](17-performance-team-ledger-archive.md#landed-round-11)).

| Round | Commit | Candidate | Before → after |
|---|---|---|---|
| 12 | c0ce9387, 3f93d54c, 183312fc | junction-diff-second-frame | Two causes. (a) The commit file walk's second frame was ff6125a6's hover settle, not the width follow: the OS cursor rests inside the window, each key replaced the diff rows, and `SettleHover` marked the root for paint though nothing in the diff draws hover (headless had no pointer, so 1 frame). The settle now asks for a frame only when an enter/leave handler ran, a `HoverHighlight` list's row hover changed, or DevTools is open (internal `Element.DrawsChildHover`, `PointerState.DrawnHoverChanges`; hover styles already mark their own element). (b) The resize's second frame was 7ca2b3db's after-frame width follow: Junction's resize handler now gives the pan its viewport before layout (`LeadDiffWidth`: the list's last laid-out width plus what the window gained or lost, after a breakpoint rebuild too); the after-frame follow stays as a check and changed nothing in 18 real and 9 headless size changes. Real window, NativeAOT, a11y on, guarded, idle-gated, alternated ×2 (before = the old behaviour behind a probe switch in the same binary): commit walk frames a key 2.00 → 1.00 (33 and 100 ms pace), UI frame ms a key 2.454 / 2.419 → 1.749 / 1.628 (−29 / −33 %); size changes 2.11 → 1.11 frames a change (38 → 20 per 18), render+flush+present 7.36 / 7.09 → 4.61 / 4.73 ms. Headless 2 → 1 frame a key and a size change; settled pixels identical; the first frame after a size change used to draw the old thumb (106–790 px in the scrollbar band) and now equals the settled frame. step-ab `-Pairs 2` JIT + AOT: AOT 0/0, JIT pair 1 one row (video bar fade +16 %, the same in its A/A: noise), pair 2 clean; no allocation moved (captures 183312fc). `HoverAfterRebuildTests` 3, `DiffFrameCountTests` 2 (reverse edits fail but the enter-handler test, which pins existing behaviour); Core 5,084, Headless 1,562, Junction 1,095 pass. ELEMENTS.md, LAYOUT.md, REACTIVITY.md. Probes `r12b1\` (b1-summary.txt). |
| 12 | 053a35ef, 3912f391, b6c6eb6f, 7d086564 | reactivity-junction-watch-chrome + reactivity-meridian-tab-switch-chrome + junction-watch-git-reruns | (a) Junction's `OnExternalChange` starts the summary and worktree loads itself and builds the view only (`RerenderView`); an equal worktree list hands back the old one; `RepoSummary.SameAs` compares by content (the GitLab loaders key on `HostedGen`: unchanged). Per watcher event, UI KB headless History 581.1 → 413.8 (−29 %), builds 10 → 6, parts 7 → 3; README touched with the same content 577.9 → 273.6 (−53 %), 10 → 2 builds, no part; Changes 496.6 → 314.7 (−37 %); real window AOT 815.7 → 646.5. (b) Meridian: a `TabDirty()` revision read only by the tree and response parts for a tab click, opening an open request, and a send's start and end: a switch HTML↔JSON 227.5 → 192.5 KB (−15.4 %), XML↔JSON −16.0 %, builds 5 → 3; a send 369.4 → 299.4 (−19 %), 11 → 7 builds; real window switch 287.5 → 251.4, send 525.6 → 453.5. (c) Junction's watcher tells whether HEAD, a ref, the config or packed refs moved; a working-tree-only event bumps only `DataGen`, a new `RefsGen` keys the log, stats, commit and range detail, tags, reflog, remotes and Files tree, and the summary re-reads only its status: History git runs 14 → 2, all-thread KB 4,268 → 226 (−95 %), UI 413.8 → 162.0; Changes 19 → 11 runs, 5,236 → 1,611 KB; real window all-thread 4,502 → 236 KB, UI 646.5 → 172.1, 14 → 2 git runs. The watcher now also sees git's lock-file renames over HEAD, config and packed-refs (a checkout with no file change, a remote added: missed before) and ignores index renames (`git status` rewrites the index). Final frames and a11y trees byte-identical for (a) and (c) over History, Changes and Commit; (b) equal but for a response-time digit and the temp path (as base vs base). Real window: 2 holds, idle-gated, alternated, 0 foreground takes. step-ab `-Pairs 2` JIT + AOT 0/0, no allocation moved (A/A's video bar fade +15–17 %: noise; captures 7d086564). Tests (reverse edits fail): ChromePartTests +2, `PartTests.ATabSwitchOrASend_BuildsNoTitleBarOrRail`, GitWatcherTests, `ExternalChangeTests.AWorkingTreeEvent_ReadsNoLogOrRefs_AndARefMovingDoes`; Headless 1,562, Junction 1,099, Meridian 677 + 4 skipped pass. REACTIVITY.md: three sections (a reload that finds the same hands back what is shown; a change some parts show takes a revision only they read; key each load on what can change it, with the two watcher traps). |
| 12 | 5d973076, a6bfb1fc, 67fc0884, fd514631, aef9955f | a11y-junction-diff-uia-bytes + a11y-resize-walk-debounce (question 47: yes) + a11y-item-added-past-end-offscreen (question 48: yes) | (a) Split, real window (NativeAOT, bigrepo, Junction History then Commit, 33 ms keys): the bridge's 391 KB a key on the UI thread = in-place walk 286.6 KB (633 diff rows as nodes, their labels and Junction's row records), UIA wrappers 85.5 (633 made and dropped a key), the diff's id sets 30.6, raises 0 B (5 a key, 65 provider calls + 65 QIs, 18–20 µs a raise), native 6 B. Cut: a list's item nodes get their UIA wrapper only when asked for, TreeDiff keeps its id sets per thread: 980.0 → 864.4 KB a key (−11.8 %), 100 ms keys 945.0 → 830.9 (−12.1 %); raises and calls the same; frame time 1.8 ms both. The walk is what a reader is given: kept (DECISIONS.md, fd514631). (b) A live resize holds the coalesced layout walk until the size has rested 100 ms. Real drag, 60 steps 16 ms apart, Vesper / Junction / Meridian: walks 12/11/11 → 3/3/1, BoundingRect raises 1,645/599/319 → 428/181/29, provider calls (as many QIs) 21,619/8,567/4,147 → 5,798/3,133/377, time in raises 19.8/9.9/5.6 → 5.4/2.8/0.4 ms (10–15 µs a raise); bytes and frames unchanged. Steps 100 ms apart are each a rest (Vesper 1,596 → 1,524 raises). The walks left are structural (rows coming into view) and still tell bounds (a lead, DECISIONS.md). (c) An item appended below the window is told in the patch's frame by 1 walk, no tree; a list over 2,000 items asks once per append. Event streams base vs HEAD: 117 scenarios identical (UIA mode too; Meridian's 25 MapTab probes time out on both sides). Audit 7 suites: 0 STALE in 3,646, 0 WALK-VALUE/EVENT, fallbacks 10. step-ab `-Pairs 2` (base 1ffa3d96): AOT 0/0 both pairs, A/A clean; JIT pair 1 one row +10 % (`a11y/add a row to 1,000 rows, AT-SPI`), absent in pair 2 and AOT: noise; allocKB moved only on a11y rows, all down (kept tabs AT-SPI 440.5 → 404.4; captures aef9955f). New tests fail reverse-edited; Core 5,085, Headless 1,572, Linux bridge 104, Vesper 931, Meridian 677, Aperture 363, Reel 51, Fluent 34, Junction 1,099; Fedora Linux bridge 104, Headless a11y/list 363. 4 desktop holds, 0 foreground takes. ACCESSIBILITY-PLAN.md. |
| 12 | ce6f4962, 7a753337, 9e9ecf24, 34dbc7fc | text-fallback-front-cache + text-cjk-rewrap-partial-shaping | (a) A 4,096-slot struct table (128 KB, made at the first fallback question, dropped by `Register`) in front of `SkiaFontCollection.MatchCharacter`, keyed by code point, weight, slant, Han context and UI culture: a hit 86–88 → 5 ns (JIT micro-probe, 3 alternated pairs; an OS miss unchanged ~4 µs); prefix tables over the Japanese document 4.181 → 2.786 ms (−33 %), same 249.7 KB; TODO.md 1.405 → 1.400. (b) A long word a prefix table shapes keeps HarfBuzz's unsafe-to-break flags and pens in the word cache (entry 48 → 56 B); a cut word the cache lacks is read off them at safe cluster starts, never before the character the script was guessed from (HarfBuzz was 18 of the 28 ms of the resize-to-1000 frame): headless resize to 1000 28.1 → 24.7 ms (−12 %; HarfBuzz calls 14,105 → 11,223), to 1320 −19 %, to 1200 −11 %; the rest are the tables' own words (two variants measured and dropped, DECISIONS.md 34dbc7fc). Real window, Junction Files on a 79k-char Japanese README, AOT, UI-thread CPU (min; (a) 3 before / 2 after, (b) 2 pairs): (a) open 47.1 → 46.1 ms, resize to 1200 −4.7 %, to 1000 −5.4 %, 40-frame drag 159.4 → 147.8 (−7.2 %; the table answers 96 % of 114,717 lookups); (b) resize to 1000 31.6 → 26.9 (−15 %; calls 14,513 → 11,360), to 1200 −6.4 %, to 1320 −7.8 %, drag −2.2 %. Fixture caveat: ideographs drawn evenly from U+4E00–U+58C3 (more face switches than real Japanese). Hashes 48k 7A18AF8DE8E05271, 240k F09E9038726E4BD6 unchanged; a new CJK wrap corpus (6 documents, 8 styles incl. Yu Gothic UI, Malgun Gothic, YaHei, emoji, kerned Latin) widths 1E6F1A93894D0B6A / rows 6C7FA51792E8EB84 before = after, 0 of 1,344,616 widths differ from the reference path. step-ab (base e60af01f) `-Pairs 2`: JIT 0 slower, pair 2 multi-font ellipsis −12 %; AOT 0/0, the same row −9.5 %; A/A 0/0; `text/resize a wrapped TextArea` +3.5 % JIT / +0.8 % AOT (12.2 → 12.5 KB, the cuts; below the bar), text/create rows +12.8 KB (the table once per collection) (captures 9e9ecf24). Rendering.Skia 398, Text 302, Headless 1,572 (goldens), Junction 1,099 pass; Fedora Rendering.Skia 398; each of 8 reverse edits per change fails. |
| 12 | 2d18a361, 2516496c | vk-chrome-8bit-until-hdr (question 49: yes) | A Vulkan window on an HDR display draws through 8-bit unless an HDR video is on screen. HDR = the video's own transfer (PQ or HLG) from mpv's `video-out-params` on its event thread, never the target it draws into (no chicken-and-egg); paused keeps F16; loading keeps what the window had (Reel's next HDR file stays F16); an SDR video and `HULTUI_VIDEO_HDR=0` stay 8-bit; `HULTUI_VULKAN_F16` still wins; GL untouched; internal seams only (`PictureRange.cs`, `HdrPictureFollower`). Rest, real AOT windows (the 4K at 150 %, 120 Hz), idle-gated, alternated ×2, base af510f92: wakes Aperture 211.7 → 115.9 a second, Reel 214.7 → 116.2 (the nvwgf2umx thread gone; after a grow and restore 249/250 → 115/116); DXGI local/non-local Aperture 228/14 → 69/25 MB, Reel 171/14 → 54/22; private 148 → 106 and 150 → 106 MB; threads 43 → 25; after a grow and restore base kept +25 MB non-local, +22–28 local, +46 private, the new build nothing; loop at rest 0 frames, passes, wakes. The switch: on, a 2.7 ms chained rebuild then 98 ms in the first present, the video's first two frames tone-mapped and the third HDR (~210 ms after the first picture); off, 325 ms in Reel (destroy-first; the 8-bit create 311 ms), 144 ms in the probe. Images (`r12b5\png\`, sent to the user): probe white 255 / grey 128 identical on both swapchains (0 px differ); real chrome white, background and accent identical, Aperture's card surface 22,22,22 → 19,19,22 and glyph edges 1–3 levels (8-bit blends as GL does); the HDR frame after the switch = base's always-F16 frame (0 of 2,188,800 px; scRGB max 5.684 = the 455-nit peak). 0 black captures, 0 stalls > 1 s, 0 device-lost, 0 nvlddmkm, 0 foreground takes, 10 desktop holds. step-ab `-Pairs 2` JIT 0/0 both (a11y rich-text allocation moves also in A/A); AOT pair 1 0/0, pair 2 4 faster that its A/A shows 11–27 % slower: noise (captures 2516496c). New tests fail reverse-edited (swapchain choice, follower rules, the sink telling the surface, mpv's range, a real-window probe row); Vulkan probe 39, MpvGL + GpuFrameSinkGL 4, Platform.SDL3 55, Rendering.Skia 405, Media 87, Media.Backends 28, Aperture 363 + 21 skipped, Reel 51 pass. HDR.md, MEDIA.md. |
| 12 | 46fccd88 | meridian-editor-floor-measured (question 46: the measured floor) | Not perf. Meridian's editor floor is the larger of 448 and the measured width of an ordinary request's six sub-tabs (strip padding, gaps, each tab's padding and label, the open tab in medium where wider), measured once through `TextMeasure` and cached by font family, display scale and measure context (`EditorMinWidth`, `ForceBottomWindowWidth` now internal instance properties). Windows (Segoe UI): strip 445.2 (447.0) px, floor 448 → 448, breakpoint 1320 → 1320; Fedora (Noto Sans): strip 463.9 (465.2), floor 448 → 466, breakpoint 1320 → 1338 (the strip's 12 px end padding and the medium tab, beyond the 4.6 px cut); pinned DejaVu floor 484, breakpoint 1356. Images at 1320×760 and 1319×760 pixel-identical before and after on Windows; idle 0 B, a full build 127,562/127,596 B unchanged, 100,000 cached reads 0 B. `In_a_wider_face_the_editors_floor_is_the_sub_tab_strips_width` fails under a reverse edit; PartTests and `ResponseViewerTests.TheRightPaneWidensByItsInnerEdge…` read the floor from the app (the latter failed on Fedora, 662 vs 680). Meridian.Tests 678 + 4 skipped on Windows and on Fedora (`wsl-test.sh`). |
| 13 | 15f97351, 013e1f1b, 8e05c94e | reactivity-junction-watch-reftable + reactivity-junction-watch-lock-echo | (a) The objection to b6c6eb6f: GitWatcher also watches `.git/reftable` when it exists; the `tables.list` rename is a ref move, a reftable lock alone is not heard (no new echo). (b) In the refs folder a `*.lock` event or a folder's own Changed is a working-tree event; the reflog panel keys on DataGen (dropping lock events outright left 4 of 53 operations unheard: DECISIONS.md). Watcher probe, 53 real git operations (git 2.53), old, cda4bcd8 and new watchers side by side: 0 regressions against either, both backends; reftable ref moves heard as one 5/29 → 29/29, stale operations 29 (old) / 35 (cda4bcd8) → 8 (the 8 are index-only and linked-worktree changes all three miss); files backend the same 7 stale, refs reloads for nothing 7 → 5. Headless Junction, 9 outside operations: reftable 7/9 stale → 0, files 0 → 0. Headless no-op background sweep 17 → 5 git runs, UI 253.8 → 23.7 KB, all threads 824.9 → 180.7 KB, 2 → 1 builds and frames; toolbar Fetch with nothing new 28 → 17 runs, UI 1,262 → 1,031 KB (−18 %), all threads 2,223 → 1,607 (−28 %). Real window (NativeAOT, GL, the 4K at 150 %, jrepo clone, idle-gated, alternated ×2, 2 holds, 0 foreground takes): a no-op fetch 14 → 2 git runs, all threads 4,455 → 171 KB, UI 514 → 22 KB, 2 → 1 builds and frames; an outside checkout on reftable 4 runs / 1.65 MB stale → 16 runs / 6.1 MB, the correct full reload; on files unchanged. step-ab (cda4bcd8) `-Pairs 2`: JIT 0/0 both pairs (A/A: video bar fade +18 %, a11y edit +10 %), AOT 0/0, no allocKB moved. 5 new tests, each fails under a reverse edit (`[ReftableFact]` skips below git 2.45); Headless 1,572, Junction 1,104 pass. REACTIVITY.md watcher traps (reftable, lock files). |
| 13 | 0da9f80e, 185bedad | layout-hover-move-frames | An input's hover clause takes the settle's rule (c0ce9387): `HandleInputEvent` notes `DrawnHoverChanges` at the start of an input, and a changed hover asks for a frame only when that count moved or DevTools is open; hover styles, `HoverHighlight` tints, scrollbars, tooltips and enter/leave handlers still mark their own frames. Headless JIT, both rules in one process, 8 px / 12 px sweeps: Junction 761 moves 290 → 117 frames (−60 %), Vesper 292 moves 161 → 40 (−75 %), Meridian 971 moves 303 → 135 (−55 %); frames that only redrew what was on screen 191 → 2, 129 → 0, 179 → 3. Real windows (NativeAOT, the 4K at 150 %, 120 Hz, moves in-process, idle-gated): Junction (2 launches, order swapped) 244/258 → 129/114 frames, UI-thread cycles 664/699 → 380/355 M (−43/−49 %), diff sweep 42 → 3 frames, History 48 → 17; Meridian 194/180 → 125/140, 270/252 → 206/209 M (−24/−17 %); Vesper (1 launch; the in-hold idle gate skipped the swapped one twice) 108 → 38, 269 → 101 M (−63 %); bytes a sweep unchanged; 0 foreground takes. Stale check 0: the shown frame equals a full render after each of 2,024 sweep moves and 280 move-then-leave moves, both rules, and after 12 keys under a resting pointer in Junction and Vesper. step-ab (f4b15d54) `-Pairs 2` JIT + AOT: 0 slower, A/A clean; "loop/tooltip delay" −49 % both runtimes (JIT 0.489 → 0.250 ms; the move onto a tooltip owner draws no frame of its own), allocKB 2.7 → 2.5. `HoverMoveFrameTests` 5 + Junction `APointerSweepDownTheDiff_DrawsNoFrame`, each fails under a reverse edit; Core 5,085, Headless 1,577 (goldens), Vesper, Meridian, Aperture, Reel, Fluent, Media, Terminal pass; Junction 1,104 of 1,105 (`GitLab.ReviewTests.AReplyAndAResolveGoToTheThread` timed out loading the review under suite load, before any pointer input; 17/17 alone). ELEMENTS.md and the `IsHovered` doc: an element drawing from its own `IsHovered` gets no frame; use `.OnHover` or an enter/leave handler. |
| 13 | 698c2758, 54bd7d3a, b8fd2c67 | text-cjk-cuts-at-need | A word a prefix table cuts short is read off cuts made once: (a) a cut word with no cuts yet gets them at its first cut; (b) a table's last word cut by the table's end is read off the word to its natural end (≤ 1,024 characters; only from a line start, after a space or a font-run boundary); a row's later probes read the last cuts given with no word-cache lookup; natural-end words are made only while words read again earn more credit than words let go spend (without it a document past the word cache was 10–12 % slower). Real ja/zh READMEs in Junction's Files view (r13-5's `mkreal.py`). Headless NativeAOT, idle-gated, 2 alternated pairs, UI-thread CPU (min), HarfBuzz calls, UI KB: Japanese 1000 px 13.5 → 12.3 ms (−9 %), 2,086 → 1,119 calls, 989 → 777 KB; drag at 1440 272 → 269 ms, 1,854 → 517 calls, 1,468 → 802 KB; drag at 960 359 → 344 ms (−4 %), 11,628 → 3,911 calls, 6.1 → 2.8 MB. Chinese 1000 px 11.9 → 11.7 ms, 3,231 → 1,433 calls; drag at 1440 278 → 281 ms, 2,227 → 591 calls; drag at 960 651 → 337 ms (−48 %), 295,322 → 3,662 calls, 88.6 → 2.0 MB. Real window (the 4K at 150 %, 8 guarded launches in 3 holds, 0 foreground takes): Chinese drag at 960 694 → 381 ms (−45 %), 291,890 → 2,453 calls, 87.7 → 1.4 MB; Japanese drag at 960 390 → 366 ms (−6 %); the 1440 drags and 1000 px within ±2 %. Costs: Chinese, the first width that wraps a line +13 % headless (9.3 → 10.5 ms, +290 KB) and +9 % in the real window, opening the file +4 % (+250 KB); a 154k-character document past the word cache, drags +0.1 %, resizes −2…+7 %; Latin (TODO.md) fewer calls, times within noise. Corpora unchanged (48k 7A18AF8DE8E05271, 240k late and reference F09E9038726E4BD6; CJK wrap 0 of 1,344,616 widths differ, widths 1E6F1A93894D0B6A, rows 6C7FA51792E8EB84; reads off cuts 403,333 → 727,446); goldens byte-identical. step-ab (26bf78b9) `-Pairs 2`: JIT 0/0 both pairs, A/A clean; AOT pair 1 clean, pair 2 one row +11 % (`a11y/tab among 10 fields`, no prefix table, absent elsewhere: noise); `text/resize a wrapped TextArea` 12.5 → 12.2 KB. PrefixCutsTests +4 (five reverse edits fail them; two perf-only mutants stay exact and pass); TextAreaFitsByCountTests' bar relaxed from a twentieth to a third (the search it compares with now allocates 75 KB, not 1,074). Windows Text 302, Rendering.Skia 410, Headless 1,577, Junction 1,105, Vesper 931, Meridian 678; WSL Fedora Rendering.Skia 410, Text 302, Headless 1,403. DECISIONS.md: 34dbc7fc's note rewritten with the real-text numbers (b8fd2c67). |
| 13 | 8f9f2683, da72e01b, d413e001, 0a8361f9 | reactivity-junction-watch-view-skip + apps-junction-summaryfor-closures + reactivity-junction-own-action-echo | (a) A watcher event builds the view only when the view's last build read the generation the event moved, or a ref moved in the repo on screen (History also builds when the issue-tracker file changes). Headless per event: another repo's save 86.0 → 70.0 KB UI, 2 → 1 builds and frames, a touch there 18.4 → 2.4 KB, 1 → 0; History save 166.4 → 150.8 KB, 5 → 4 builds, 2 → 1 frames, a touch 20.5 → 2.7 KB, 1 → 0; Changes unchanged. Real window (NativeAOT, the 4K at 150 %): a save in the shown repo 205.6 → 187.8 KB UI, 5 → 4 builds, 2 → 1 frames; in another repo 91.1 → 71.9 KB, 2 → 1; touches 21.5 → 1.9 and 19.5 → 1.6 KB, 1 → 0; empty builds 16 → 4 a launch. (c) `SummaryFor` builds its key and closures only when the data generation moved (tracked per loader in a weak table; no API): History Down 29.8 → 27.2 KB a key (−8.7 %), History save 150.8 → 144.8 KB. (b) Built as neither proposed option: `RefreshAll` calls a new `GitWatcher.Forget()` on the shown repo's watcher, which drops what was heard so far (a callback already posted included), since the reload starting then reads it all; no git run added, nothing paused (the fingerprint cost 1–2 git runs and delayed every reload; the pause loses outside changes: DECISIONS.md). A checkout headless 28 → 15 git runs (14 with no watcher), UI 987 → 747 KB (−24 %), all threads 1,975 → 1,288 KB (−35 %), 17 → 15 builds, reftable the same; real window 28 → 17 runs, UI 1,740 → 1,215 KB (−30 %), all threads 10.58 → 6.52 MB (−38 %). Final PNGs and a11y trees as before (History, Changes, after checkouts on both backends); 2 real-window holds, alternated ×2, 0 foreground takes. step-ab (0d76fdb6) `-Pairs 2` JIT + AOT 0/0, no allocKB moved (no Junction rows in the suite). New tests fail with their part reversed; Headless 1,577, Junction 1,111 pass. REACTIVITY.md. (da72e01b's body says "16 asks for two key moves": it was 16 extra asks over the test's key presses.) |
| 13 | 4fdb56cb, 276789d8, cdb47cb9 | painting-vk-dead-size-stencils + media-mpv-event-wait-forever | (a) A Vulkan window frees the stencils of sizes it no longer shows once its size has rested: 1 s after the last change, and only after a frame drawn since the old targets went, the surface runs `PurgeUnusedResources` with a cutoff just after the targets were let go and never after the last frame began (a cutoff at the last frame, in whole ms, dropped on-screen images when `Window.SaveFrame` had read the frame back in that millisecond); it runs from a new loop deadline, `SurfacePurge`, at the end of SDL3Window's pump, which the modal size loop never reaches; no public API (one InternalsVisibleTo, Rendering.Skia → Platform.SDL3). Aperture signed in, NativeAOT, the 4K at 150 %, 8-bit, idle-gated, alternated (4 launches, then 3 with a wheel pass first), after 18 size changes: stencils 9 (21.77 MB) → 1 (2.78 MB); Skia budget 32.0–34.7 → 11.2 MB; DXGI local / non-local 104.8–109.8 / 30.6–32.1 → 84.0–85.0 / 25.1–26.6 MB; private at rest 200–207 → 181–184 MB, at the end 211–217 → 180 MB. The purge 1.05–1.33 ms plus one loop pass after a run of size changes; re-upload cost: it dropped 11 of 35 image textures (2.58 MB), the next wheel pass uploaded 7 again (2.54 MB), UI thread over those 28 frames 20.6 ms against 20.1–20.4. Frames saved before and after the rest byte-identical, the same in both builds. (b) mpv's event thread waits with no timeout (the only two flags read outside events, `_stopping` and `_traceHwdec`, are followed by `mpv_wakeup`): Reel paused at rest mpv-events 2.34/2.40 → 0.00/0.00 wakes a second, the app's own threads 2.4 → 0, all threads but the harness 128–134 → 126 (all NVIDIA's). (c) vk-format-switch-chained not attempted (→ Waiting). step-ab (bba011a9) `-Pairs 2`: JIT 0/0 both pairs, no allocKB moved; AOT pair 1 2 faster (icons/mount 600 −22 %, the same in its A/A: noise), pair 2 clean. Tests fail under reverse edits: `AfterSizeChangesRest_TheWindowKeepsOneStencil` (6 stencils without the purge; 2.5 s late without the deadline), two mpv tests (4 wakes in 2 s with the old wait; Dispose 5,011 ms without its wakeup). Validation layer proven loaded by a deliberate fault (20 × `VUID-vkCmdDraw-None-09600`), then 0 messages. Vulkan probe 41, Rendering.Skia 416, Platform.SDL3 55, Media 87, Media.Backends 30, MpvGL 3, Aperture 363 (+21 skipped), Reel 51 pass. 0 foreground takes, 0 nvlddmkm, no stall > 1 s. HDR.md. |
| 13 | 154a7ca3, 8048b644 | layout-vesper-wrap-resize-lead | `LeadWrappedBodyWidth` in Vesper's `OnWindowResized` gives the build the wrapped viewer's and diff's body width as the after-frame check last read it plus what the window gained or lost (it leads only from a width the check saw; the build clears the lead in `OnAfterBuild`); `SettleWrappedRowWidths` stays as the check. Headless JIT, 40 steps of 4 px, the lead off in the same binary as HEAD's path, alternated ×2, per step: viewer 2 → 1 frames and builds, PatchFroms 128 → 64, render visits 394 → 197, 363 → 299 KB (−18 %); diff 2 → 1, 172 → 86, 552 → 276, 502 → 417 KB (−17 %); 1.5× the same counts; AOT 361 → 297 and 499 → 415 KB; wrap off unchanged. Real window (NativeAOT, 1× monitor, steps on the window's own thread 16 ms apart, idle-gated, 1 hold, 0 foreground takes): viewer 2.03 → 1.03 frames, 2 → 1 builds, UI-thread time 3.35 → 2.87 ms (−14 %), 360 → 297 KB; diff 2.03 → 1.03, 4.31 → 3.66 ms (−15 %), 500 → 415 KB. Pixels (viewer and diff, wrap on/off, 1×/1.5×, grows, shrinks, a scale and a height change): settled frames byte-identical to HEAD's 104/104, scroll offsets equal 104/104; the first frame after a size change now equals the settled one 104/104 (HEAD drew stale heights in 2 and 4 of 9 two-frame steps). step-ab (61836127) `-Pairs 2` JIT + AOT 0/0, no allocKB moved (no suite row runs Vesper). `WrapResizeFrameTests` 3 (all fail with the lead removed; the wrong-guess test fails with the check's rebuild removed); Vesper 934 (+4 skipped), Headless 1,577 pass. LAYOUT.md. |

## Examined, not worth it

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
- **Framework batching of the main-thread drain or of `MarkDirty`** (6): an await continuation that writes and then reads
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

No question is open from rounds 1–12. Their answered questions are in the archive
([round 12](17-performance-team-ledger-archive.md#needs-the-user-round-12), [round 11](17-performance-team-ledger-archive.md#needs-the-user-round-11),
[round 10, with round 9's 26 and the earlier 10](17-performance-team-ledger-archive.md#needs-the-user-round-10),
[round 9](17-performance-team-ledger-archive.md#needs-the-user-round-9), [round 8](17-performance-team-ledger-archive.md#needs-the-user-round-8),
[round 7](17-performance-team-ledger-archive.md#needs-the-user-round-7), [round 6](17-performance-team-ledger-archive.md#needs-the-user-round-6)). Search it by candidate id
before asking anything an earlier round asked. Recommendations in brackets. Question 50 (a11y-gate-on-client)
and 51 (after-layout-callback) were answered "not yet" and "only if another app needs it"; both are Waiting.

Round 13 questions (from the survey, 2026-11-13). Recommendations in brackets.

52. **a11y-labels-kept-across-invalidaterows** (r13-7; step 5 (a)): a list's `InvalidateRows()` drops every kept reader
    label, so Junction History makes its 1,000 labels again 4 times at start with a UIA client listening (keeping them:
    −650 KB, −21 % of the start frames) and at every later row-shape change (−217 KB of 547). Option 1: an opt-in list
    option or overload that keeps labels (public API). Option 2: `InvalidateRows` keeps labels by contract (the
    documented behaviour in `VirtualListElement.cs:60–66` and ELEMENTS.md changes), and an app whose labels read other
    state passes a new label delegate, as Meridian's history list would at a day change. A reader hears the same names.
    [Option 2: no new API, one app line.]
53. **gl-sdl-timer-resolution-at-rest** (r13-3; step 8): SDL holds Windows' timer at 1 ms for the app's whole life.
    Asking only while frames are paced would let the machine's clock interrupt fall from 1,000 to 64 a second while a GL
    HultUI app rests and nothing else asks (a laptop), with no change in the app's own wakes; experts 4 and 8 count it
    not worth it here, because Steam already holds 1 ms on this machine. Building it needs a measurement with Steam (and
    any browser video) closed for a few minutes, or `powercfg /energy` run as administrator. Build it, with the step
    asking you for those minutes, or decline? [Build it, with a few minutes of Steam closed when the step asks; drop it
    if the count shows nothing.]
54. **What an HDR video leaves behind** (r13-3; step 6 (d)): after the first HDR video in a process, NVIDIA keeps its
    DXGI threads (~125 wakes a second more) and ~47 MB more GPU memory until the app closes, even back on 8-bit; only
    rebuilding the Vulkan device (the Skia context, every GPU resource, the mpv interop: large, may not work) could
    return it. Document it in HDR.md, or try the device rebuild? [Document it.]

Reminder (expert 8): Aperture signed in to jellyfin.home as device "aperture-survey" in earlier rounds, as "aperture-r13" in round 13, and once in
round 12 as "aperture-r12"; the user should remove those devices and revoke the API key when the work is done.

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

From 2026-11-12 (round 12's survey); earlier entries are in the archive ([to 2026-10-25](17-performance-team-ledger-archive.md#log-2026-10-07-to-2026-10-25),
[2026-10-26 to 2026-10-27](17-performance-team-ledger-archive.md#log-2026-10-26-to-2026-10-27),
[2026-10-27 to 2026-11-02](17-performance-team-ledger-archive.md#log-2026-10-27-to-2026-11-02),
[2026-11-02 to 2026-11-03](17-performance-team-ledger-archive.md#log-2026-11-02-to-2026-11-03),
[2026-11-03 to 2026-11-08](17-performance-team-ledger-archive.md#log-2026-11-03-to-2026-11-08),
[2026-11-08 to 2026-11-09](17-performance-team-ledger-archive.md#log-2026-11-08-to-2026-11-09),
[2026-11-09 to 2026-11-11](17-performance-team-ledger-archive.md#log-2026-11-09-to-2026-11-11),
[2026-11-11 to 2026-11-12](17-performance-team-ledger-archive.md#log-2026-11-11-to-2026-11-12)).

- 2026-11-12: round 12 survey started in a fresh context (session 0bab04ae). Main has not moved (origin/main b0dd1954
  is in the branch). Moved to the archive by a checked script (`archive_r12.py`; every moved line found there, nothing
  lost): round 10's 16 Landed rows, round 11's build order with its closed-from-Waiting note, round 11's answered
  questions 37–45, and round 10's 161 log lines (2026-11-09 to its release on 2026-11-11). Housekeeping item closed:
  the VS collector service is stopped and step 14 deleted the WSL probe folders. Attached now: one monitor, the 1440p
  (the 4K is unplugged). The eight experts survey round 11's build, 5731ee58..b0dd1954 (50 commits, one merge: round
  11's own into main), in their own worktrees on this commit, in real windows too (the brief's "Real windows too").
- 2026-11-12: round 12 survey done (reports 9f64e6bc..5f1744ca, `17-reports/r12-*`, ~70 min). Layout, painting, GL/Vulkan
  and .NET runtime satisfied; text, reactivity, accessibility and apps not. No review objection: round 11 helped the
  real apps (67 AOT walks geomean 0.964). Real windows: ~95 guarded launches under the desktop lock, 0 foreground
  takes, 0 driver events, 0 screen reads; expert 8's shared AOT publishes in `r12-apps\aot\`. What only the real windows
  showed: Junction's watcher builds its chrome 10 times an event; its commit file walk draws 2 frames a key and pays
  40 % of its UI bytes to the UIA bridge; each raised UIA event costs 13 provider calls + 13 QIs on the UI thread, and
  a resize raises bounds for every moved element; no UIA client ever asked for a root; the Vulkan apps wake ~207/s at
  rest (~96 from the F16 swapchain's driver thread) and keep +50 MB after the first F16 resize. Merged into a 6-step
  build order (one owner each; the second-frame lead and the width-follow candidate folded into step 1; experts 4's
  and 8's bridge leads into step 3), 6 Waiting items closed, questions 46–50 asked (47, 48 shape step 3; 49 step 5; 46
  step 6). For PERFORMANCE.md: make resize steps on the window's own thread (another thread's `SetWindowPos` doubles
  grow frames); FrameLog counts 0 frames inside a live resize (measure resizes with all-thread bytes);
  `HULTUI_TRACE` adds 55–59 MB of working set; headless walks overcount timer-posted "if still loading" rebuilds and a
  headless clock overstates held-key costs; SDL ignores keys posted to an unfocused window (mouse moves are taken);
  OBS's `graphics-hook64.dll` is an implicit Vulkan layer on this machine. Round 13's survey reviews round 12's build,
  5f1744ca..(round 12's release). Phase → build.
- 2026-11-12: round 12 step 1 landed (c0ce9387, 3f93d54c; captures 183312fc): two causes of Junction's second frame.
  The hover settle after a build asked for a frame though nothing drew hover (the OS cursor rests in the window; a
  headless host has no pointer, so it never showed); now only when a handler ran or a `HoverHighlight` row changed.
  The resize gives the diff's pan its width before layout. Real window: commit walk 2.00 → 1.00 frames a key and
  −29…−33 % UI time a key; size changes 2.11 → 1.11 frames. Question 51 (a public after-layout hook) asked; lead
  hover-move-frames-nothing-shows to Waiting. For PERFORMANCE.md: a real pointer resting over the window makes every
  rebuild re-hit hover; test frame counts with a pointer. Next: step 2.
- 2026-11-12: the user answered questions 46–51 with the recommendations: 46 the measured floor (step 6), 47 yes (step
  3 (b)), 48 yes (step 3 (c)), 50 not yet (after a live reader check; Waiting), 51 only if another app needs it
  (Waiting); and 49 **yes**: Aperture and Reel use the HDR (F16) swapchain only while an HDR video is playing (step 5).
- 2026-11-12: round 12 step 2 landed (053a35ef, 3912f391, b6c6eb6f; captures 7d086564): Junction's watcher event builds
  the view only and an unchanged repo no chrome (−29…−53 % UI KB an event); a working-tree event re-reads no log, refs or
  remotes (git runs 14 → 2, all-thread KB −95 % in History; the real window 4,502 → 236 KB); Meridian's tab switch and
  send build no title bar or rail (−15…−19 %). The watcher also sees HEAD/config/packed-refs lock renames it missed.
  meridian-send-markdirty closed (in (b)). For PERFORMANCE.md: key each load on what can change it; a reload that finds
  the same hands back what is shown. Next: step 3.
- 2026-11-12: round 12 step 3 landed (5d973076, a6bfb1fc, 67fc0884; DECISIONS.md fd514631; captures aef9955f): UIA
  wrappers for list items made only when asked for (Junction commit walk −12 % UI KB a key; the walk itself is what a
  reader gets and stays); a live resize tells bounds once it rests (a real 16 ms-paced drag: BoundingRect raises −74…−91 %,
  provider calls −64…−91 %); an item appended below the window is told. 117 event-stream scenarios identical, audit 0
  STALE. For PERFORMANCE.md: drive live-resize work with drag-paced steps (16 ms; steps 100 ms apart are each a rest);
  per-launch frame-time sums vary widely in real windows, trust bytes and counts. Next: step 4.
- 2026-11-12: round 12 step 4 landed (ce6f4962, 7a753337; captures 9e9ecf24; DECISIONS.md 34dbc7fc): a struct table in
  front of font fallback (a hit 86 → 5 ns; CJK prefix pass −33 %) and cut CJK words read off their run where HarfBuzz
  says the cut is safe (resize to 1000 px −12 % headless, −15 % real window; HarfBuzz calls −20 %); every corpus
  bit-identical (0 of 1.34 M CJK widths differ). For PERFORMANCE.md: rank on AOT (a JIT micro-probe overstated the
  per-hit saving ~5×); real-window resize frames run inside the size message, outside `Window.Performance`'s timer (use
  UI-thread cycles); a cache in front of a lookup must not slow its hits. Next: step 5.
- 2026-11-12: round 12 step 5 landed (2d18a361; captures 2516496c): Aperture and Reel draw through 8-bit unless an HDR
  video is on screen (the video's own PQ/HLG transfer decides; paused keeps F16): rest wakes −45 % (212/215 → 116 a
  second), DXGI local −70 %, private −42 MB, nothing kept after a resize; the HDR frame after the switch is
  pixel-identical to always-F16. Images sent to the user (chrome white/background/accent identical; a card surface 3
  levels darker on 8-bit, as GL blends). The 4K at 150 % is attached again. For PERFORMANCE.md: read a picture's range
  from the source, never from the target. Next: step 6.
- 2026-11-12: round 12 step 6 landed (46fccd88): Meridian's editor floor is the measured sub-tab strip where wider
  (Windows unchanged at 448 / 1320 px; Fedora 466 / 1338); NarrowEditorLayoutTests passes on Fedora. For PERFORMANCE.md
  (testing): app tests that write a font-dependent number should read it from the app. All six build steps done; the
  full suite runs next (`fullsuite-r12.sh`).
- 2026-11-12: round 12 full suite on 3131ed8e (src as after step 6): Windows `dotnet test HultUI.slnx -c Release` 30
  projects, 0 failures (Core 5,085, Headless 1,572, Junction 1,099, Vesper 931, Meridian 678, Aperture 363, Rendering.Skia
  405, Text 302, Syntax 121, Media 87, Reel 51, Fluent 34; UiTests 25 + 101 skipped, gate off); Fedora `wsl-test.sh` 22
  projects, 0 failures (Core 5,084 + 1, Headless 1,398 + 88, Vesper 913 + 20); docfx `--warningsAsErrors` 0 warnings, 0
  errors (~13 min, `fullsuite-r12.sh`). Phase → release.
- 2026-11-12: round 12 merged into main as afc1ed40 (detached origin/main merge in this worktree, tree identical to the
  tested branch; origin/main b0dd1954 was in the branch and had not moved, so the push gate held). Primary checkout
  fast-forwarded behind the guard; branch reset to the merge and pushed. CI on afc1ed40 next.
- 2026-11-12: CI run 43269911874 on afc1ed40: attempt 1 red — the Windows test job failed
  `HighlighterConcurrencyTests.ACancelledTokenizeStopsAndTheNextOneRuns` (the tokenize ran on 387 ms after its cancel,
  bound 1,078 / 4 = 269 ms; the macOS keyring job cancelled with it); round 12 changed no Syntax file and the full local
  suite passed it, so `gh run rerun --failed` once: attempt 2 green (test, test-linux, docs, meridian-keyring on
  ubuntu-24.04 and macos-15). A 2-core-runner timing flake; memory `ci-red-findings` updated. Round 12 landed six steps in
  ~25 commits, measured in real windows as well as headless: Junction draws one frame a key and per size change (the
  hover settle after a build, the diff pan's width before layout; −29…−33 % UI time a key), its watcher builds no chrome
  for an unchanged repo and re-reads no log or refs for a working-tree change (−95 % all-thread bytes an event),
  Meridian's tab switch and send build no title bar or rail (−15…−19 %), UIA wrappers made on demand and a live resize
  telling bounds once it rests (−74…−91 % raises a drag), off-screen appends told to a reader, a font fallback table and
  CJK cuts read off their run (CJK resize −15 % in a real window), Aperture and Reel on 8-bit until an HDR video is on
  screen (rest wakes −45 %, GPU memory −70 %), and Meridian's measured editor floor. Round → 13, Phase → survey; the loop
  stops, and round 13 starts in a fresh context.
- 2026-11-12: the user asked for app releases after round 12: junction-v1.26.0, vesper-v1.26.0, meridian-v1.9.0,
  aperture-v1.24.0, reel-v1.10.0 cut on afc1ed40 (CI green), pushed 3 + 2 (annotated, messages from files). The first
  installer runs on .NET 11's publish paths: Vesper, Meridian, Aperture and Reel green on the first attempt and
  published; Junction's Windows test job failed `HultUI.Headless.Tests.SingleInstanceTests.ABurstOfLaunches_AllArrive`
  (PumpUntil 15 s; no SingleInstance change since cc55bdcc; passed in CI on the same commit) → `gh run rerun --failed`
  once: green, Junction-Setup-1.26.0.exe published. Memory `ci-red-findings` and `release-tagging` updated.
- 2026-11-12: round 13 survey started in a fresh context (session 17c11f5e; usage 22 % weekly). Main has not moved
  (origin/main afc1ed40 is in the branch). Moved to the archive by a checked script (`archive_r13.py`; every moved line
  found there, nothing lost): round 11's 15 Landed rows, round 12's build order with its closed-from-Waiting note, round
  12's answered questions 46–51, and round 11's 100 log lines (2026-11-11 to its release on 2026-11-12). Both monitors
  are attached (the 4K at 150 % primary, the 1440p to its left). The eight experts survey round 12's build,
  5f1744ca..afc1ed40 (29 commits, one merge: round 12's own into main), in their own worktrees on this commit, in real
  windows too.
- 2026-11-13: round 13 survey done (reports da6e72c8..8f01b6fd, `17-reports/r13-*`, ~75 min). .NET runtime satisfied;
  the other seven not. Review objections: b6c6eb6f (a reftable repository's ref moves read as working-tree events, so
  Junction stays stale after an outside checkout; correctness) → step 1; 34dbc7fc's DECISIONS.md note (measured on a
  generated fixture only) → step 3; 053a35ef and b6c6eb6f +0.8 KB a Junction build (below the bar) → step 4 (c). Round
  12 helped the real apps (67 walks geomean 1.003 headless; commit walk 1.00 frame a key from 2.00, Vesper navigation
  1.30 from 2.37, Vulkan rest 110–118 wakes a second from 207). Real windows: ~104 guarded launches in ~23 holds of
  `Global\HultUI-r13-desktop`, 0 foreground takes by the apps, 0 driver events, 0 screen reads. One disturbance: expert
  2's first batch dot-sourced `driver.ps1.inc` (not a .ps1 name), so the shell opened it in Visual Studio on the user's
  desktop; the user closed it, the batch failed before any window, and the helper is now `driver.inc.ps1`, checked
  before the lock is taken (my Stop-Process of the waiting batch was refused by the permission check). Merged into an
  8-step build order (one owner each; expert 2's stencil purge and expert 3's mpv wait in one Vulkan step; expert 8's
  `SummaryFor` objection into expert 6's step 4; expert 8's Vesper bridge lead into expert 7's step 5), 7 Waiting items
  closed, 4 added, questions 52–54 asked. For PERFORMANCE.md: an NVIDIA Vulkan device runs four polling threads and
  holds 1 ms timer resolution for its life (a Vulkan app rests at ~112 wakes a second here, a GL app at ~10); SDL holds
  1 ms for the app's life, so exclude probe threads from wake counts (a Sleep(5) loop wakes 170 a second at 1 ms); a
  Vulkan window keeps a stencil for each size it drew at; posted mouse moves count only while the real cursor is inside
  the window; mind the logical width (a 1,440 px window at 150 % is 960 logical); rank text work on real text, not
  generated characters; dot-source only `.ps1` files. Round 14's survey reviews round 13's build, 8f01b6fd..(round 13's
  release). Phase → build.
- 2026-11-13: round 13 step 1 landed (15f97351, 013e1f1b; captures 8e05c94e): Junction's watcher hears a reftable
  repository's refs move (the objection to b6c6eb6f: reftable ref moves heard 5/29 → 29/29, 0 regressions over 53 git
  operations on both backends, headless 7 of 9 outside operations stale → 0), and a ref's lock file alone is a
  working-tree event (a no-op fetch in a real window 14 → 2 git runs, 4,455 → 171 KB on all threads; the toolbar Fetch
  −18 % UI, −28 % all threads). step-ab 0/0 JIT and AOT. For PERFORMANCE.md: many git commands take a ref's lock even
  when they change nothing, so a lock event alone is not a ref move; a no-op reflog move on a detached HEAD only touches
  `.git/HEAD.lock`, which no watcher hears (as before). Next: step 2.
- 2026-11-13: round 13 step 2 landed (0da9f80e; captures 185bedad): a pointer move whose hover change nothing draws asks
  for no frame. Headless sweeps −55…−75 % frames (frames that only redrew what was on screen 191 → 2 in Junction); real
  windows Junction −43…−49 % UI-thread cycles a sweep (diff 42 → 3 frames), Vesper −63 %, Meridian −17…−24 %; 0 stale
  over 2,304 checked moves; step-ab 0 slower, the tooltip-delay row −49 %. Junction's
  `ReviewTests.AReplyAndAResolveGoToTheThread` timed out once under suite load (passes alone): watch at the full suite.
  For PERFORMANCE.md: an element that draws from its own `IsHovered` gets no frame on a move, so use `.OnHover` or an
  enter/leave handler; the in-hold idle gate (10 s quiet, 1 min timeout) often fails on this desktop, which idles near
  10 % CPU. Next: step 3.
- 2026-11-13: round 13 step 3 landed (698c2758; captures 54bd7d3a; DECISIONS.md b8fd2c67): a CJK word a prefix table
  cuts short is read off cuts made once. On the real Chinese README at 960 logical px a drag 88.6 → 2.0 MB and −48 %
  UI-thread time headless (real window −45 %, 291,890 → 2,453 HarfBuzz calls); Japanese 1000 px −9 %; elsewhere times
  within ±2 % and calls −46…−73 %. Kept with one measured cost: on Chinese the first width that wraps a line +13 %
  headless / +9 % real window (+290 KB) and opening +4 %, once a document. Corpora and goldens unchanged; step-ab 0
  slower but one AOT noise row. For PERFORMANCE.md: check a cache-dependent text change on a document twice the cache's
  size (where it can turn into a loss); time a resize's own frame, not the settle after it (single-frame headless phases
  swing ±7 ms on GC placement). Next: step 4.
- 2026-11-13: round 13 step 4 landed (8f9f2683, da72e01b, d413e001; captures 0a8361f9): a Junction watcher event builds
  the view only for a load it re-keys (a save in another repo 2 → 1 builds and frames, a touch 1 → 0; empty builds 16 →
  4 a launch), `SummaryFor` makes its closures only when the key moved (History Down −8.7 % a key), and the app's own
  action is not heard back (`GitWatcher.Forget()` when `RefreshAll` starts: a checkout −30 % UI, −38 % all threads, 28 →
  17 git runs in a real window). step-ab 0/0. For PERFORMANCE.md: what a build reads from disk without a load must be
  checked by the event handler; an app that watches its own writes should forget what was heard when its own full reload
  starts, not pause the watcher or read a fingerprint. Next: step 6 before 5, so question 52 can be answered first.
- 2026-11-13: round 13 step 6 landed (4fdb56cb, 276789d8; captures cdb47cb9): a Vulkan window frees the stencils of
  sizes it no longer shows once its size has rested (Aperture after 18 size changes: stencils 21.8 → 2.8 MB, Skia budget
  −66 %, DXGI local −21 MB, private −20…−37 MB; the purge ~1.2 ms, its re-uploads +0.3 ms over 28 frames), and mpv's
  event thread waits with no timeout (a paused player's own wakes 2.4 → 0 a second). (c), the chained HDR → SDR switch,
  not attempted: back to Waiting. step-ab 0 slower. For PERFORMANCE.md: a probe that wraps the platform loses SDL-only
  loop deadlines (a purge or display-colour poll never wakes it); `TotalProcessorTime` moves in 15.6 ms steps, time the
  UI thread with `QueryThreadCycleTime`. Next: step 7.
- 2026-11-13: round 13 step 7 landed (154a7ca3; captures 8048b644): Vesper's wrapped viewer and diff get their body
  width before the build on a resize: 2 → 1 frames and builds a step, −17…−18 % KB, real window −14…−15 % UI-thread
  time; settled frames byte-identical, and the first frame after a size change no longer shows stale heights. step-ab
  0/0. For PERFORMANCE.md: anything a build measures at a laid-out width (wrapped row heights handed to a virtual list)
  should get the led width too, or each size change builds and draws twice. Next: step 5 (b) (5 (a) waits for question
  52).
