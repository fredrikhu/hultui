# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 15
- **Phase:** survey — round 15, started 2026-11-14 in session 8e8ba33b (usage 71 % weekly; the week resets 2026-11-15
  03:00 UTC). The eight experts survey round 14's build, 06d4db59..5b2985be (58 commits, one merge: round 14's own into
  main; main's a589b52b and 5aa1d25b touch workflows and docs only), in their own worktrees on the commit that records
  this Phase, in real windows too, under the desktop lock `Global\HultUI-r15-desktop`. Common brief
  `brief-r15-survey.md`, agent ids `r15-agents.txt`, probes `r15-<area>\`, the archive script `archive_r15.py`, all in
  this session's scratchpad `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\8e8ba33b-db33-43fa-885a-cb6b82b7253f\scratchpad\`.
  When all eight reports are in: commit each as `17-reports/r15-<k>-<area>.md`, merge them into the ledger (Verdicts,
  a ranked build order, Waiting re-ranked, Examined, questions from 62), set Phase build. If the session dies,
  re-spawn only the experts without a `17-reports/r15-*` report. Round 14's files (briefs `brief-r14*.md`,
  `fullsuite-r14.sh`, the model for the full suite, probes `r14-<area>\` and `r14b<N>\`) are in session 41943a9b's
  scratchpad (Probe code below). Guideline from round 14 for build briefs: a step that changes when a navigation or a
  load applies runs its app's tests on Fedora too.
  To resume in a fresh context, run in this worktree: `/loop Run the next step of
  the performance team: follow docs/plans/17-performance-team.md, with state in docs/plans/17-performance-team-ledger.md.
  Read both once per session (afterwards only the ledger's Phase and the part the step needs), do the step the ledger's
  Phase calls for, record it in the ledger, and commit.` Before resuming, `git fetch` and merge main if it moved; build
  steps use `scripts/perf/step-ab.ps1`. Open for the user, not blocking: the live screen-reader check of menus (with
  question 50's bridge gate); another F16 held-drag run (Waiting) only with a new approval. Older rounds are in
  [the archive](17-performance-team-ledger-archive.md) (search it, don't read it whole). History note: rewritten
  2026-10-25 (every commit after 3353ce47 has a new ID; a pre-push hook refuses pushes built on the old history).
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`)
- **Review commits since:** round 15's survey reviews round 14's build, 06d4db59..5b2985be (the last survey report to the round 14 release; the one merge is round 14's own into main). Earlier: round 14's survey reviewed round 13's build, 8f01b6fd..e432c224 (the last survey report to the round 13 release; its two merges are round 13's own into main). Round 13's survey reviewed round 12's build, 5f1744ca..afc1ed40 (the last survey report to the round 12 release; the one merge is round 12's own into main). Round 12's survey reviewed round 11's build, 5731ee58..b0dd1954 (the last survey report to the
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
| 1. Layout | 14 | no | none (0da9f80e: only `VirtualListElement` reads `IsHovered` while painting, every other hover look marks its own frame, a wheel notch under a resting pointer draws 1 frame in every app; 154a7ca3 holds at 150 % with fractional widths, 0 stale rows in 60 steps; a save over History 0 builds and 0 frames). Real windows (3 launches): Junction's wrapped diff draws 2 frames a resize or splitter step → step 4 (a); a save over Changes draws a same-pixel frame first → step 6 (a); Meridian's tab switch with the pointer on the map list draws a same-pixel frame → step 4 (b); hover stays on a row scrolled away from a resting pointer → question 59 |
| 2. SkiaSharp: painting | 14 | no | none (4fdb56cb: the purge drops no on-screen texture, a repaint right after uploads nothing, Aperture DXGI local 109.8 → 85.0 MB; 0da9f80e: 9,150 inputs that drew no frame all byte-identical to a forced frame; 8-bit path intact; d73b1439's walk-only frames draw nothing). Real windows (8 launches): Vesper draws a frame on every button release → step 5 (a); the scrollbar's hover tween draws 15 frames at 120 Hz, its tail frames identical → step 5 (b) |
| 3. GL and Vulkan | 14 | no | none (cd7e3db2: nothing paces without asking, 0 hint changes at rest or in 60 resize steps, a tween gives 1 ms back 1.0 s after; 4fdb56cb: one `SurfacePurge` wake per run of size changes, none on GL; 276789d8: reopen and close clean, no thread left). Real windows (15 launches): the video interop loads the GL driver inside the first video frame (~50 ms) → step 8 (question 55); the UI thread wakes 1.3–10.8 a second at rest with no loop pass and no message (lead, Waiting) |
| 4. .NET runtime | 14 | no | none (every round 13 commit read: da72e01b's table bounded, no growth over 60 repo switches; natural-end words under the LOH line, weighed into the 4 MB; labels retained as round 12, only the churn gone; 0 B a purge pass, a timer wait or a handler scan). Real windows (7 launches): Junction's History at a reading pace allocates 9.35–10.3 MB a key on all threads (the whole commit patch) → step 2; the watcher allocates 1.2 KB and throws one caught exception an event → step 3 (b) |
| 5. Text pipeline | 14 | no | 698c2758 (b): on real Chinese text the natural-end words cost +49…+72 % UI KB at every new width (1200 508 → 755, 1000 471 → 808 against (a) alone), winning only in drags → step 1 (a). Real window (8 launches; new real ko/th/ar/he/emoji fixtures): a wrapped Arabic README's drag at 1440 allocates 842.6 MB (every probe through the full BiDi layout) → step 1 (b); a CJK document past the word cache re-shapes everything a frame (156.9 MB a drag) → step 1 (c) (question 56) |
| 6. Reactivity and reconcile | 14 | no | none (watcher probe on both backends identical to round 13 step 1's; 8f9f2683 no stale case; d413e001's `Forget` loses no outside change; Meridian's tab switch −40 %). Real window (3 launches): a build in a gitignored folder reloads the repository (Changes 22–33 git runs a build) → step 3 (a); Vesper draws 1.70 frames a navigation, one with the new title over the old listing → step 6 (b); terminal staging is never heard → question 58 |
| 7. Accessibility bridges | 14 | no | none (all 22 label delegates give current names; d73b1439's fallback 10 → 11 is timing in one ReviewTests case; 0 STALE in 3,682 audits; 117 event streams identical). Real windows (21 launches): opening or closing the palette or a menu builds the whole window's tree (Junction palette close 98.7 % bridge bytes) → step 7; serving list items on demand from ~200 items (−16.6 % Junction start, −37 % a viewer open) waits with question 50's live check; 0 root asks in 21 launches |
| 8. App workloads | 14 | no | none (round 13 helped: 67 AOT walks geomean 0.997 headless; real windows (39 launches) pointer sweeps Junction diff 33 → 0 frames, Vesper 48 → 2; a save over History −27 % UI KB, the app's Fetch −36 %, checkout −34 %; Junction start −30 %; a Chinese drag at 960 logical 2,183 → 39.6 KB a step; Vesper wrapped drag 1 frame a step; Aperture −24.5 MB GPU after size changes; Junction at rest 15.6 ms timer). Leads: a ref move with History on screen pays the bridge 25–55 % of its bytes (Waiting, with question 50); Vesper's wrapped drag 248–269 KB a step (Waiting) |

## Queued candidates

Rounds 1–14's build orders all landed but round 6's step 8, round 10's step 14, round 11's step 1 (c) and round 14's
step 8, measured and declined, round 11's steps 13 and 14 and round 13's step 5 (b), measured with nothing to
change, and round 13's step 6 (c), not attempted (back to Waiting) (rounds 5–14 are in
[the archive](17-performance-team-ledger-archive.md#round-14-build-order), with each survey's closed-from-Waiting note). Round 15's build order
comes from its survey.
A build step may bundle one expert's related small candidates as separate commits. Items marked **(user)** wait for a
decision in Needs the user; the rest of their step goes ahead.

### Waiting

Re-ranked by the next survey.

- **reactivity-posted-drain-bound** (6, with 4; a framework user's report, 2026-11-14; **the user: into round 15's build
  order**, its batching half is question 62) — `DrainMainThreadQueue` (src/HultUI/Application.cs:1132–1138) runs
  `while (TryDequeue) action();`, so a post made while it drains runs in the same pass: background work that posts
  faster than the UI thread runs it never leaves the drain, and since `PumpEvents` comes first (:691) no input is
  handled and no frame drawn until the stream stops. The report: a list component and a loader posting every ~50 ms,
  7–9 rebuilds inside one ~2 s frame (500 rebuilds in one 10.6 s frame before the app batched its own posts). Fix: a
  pass runs at least one action, then stops once a budget (about half the display interval, read on the platform's
  `TimeProvider`) is spent; order kept; a throwing action still leaves the pass with the rest queued (nothing catches
  it today); `_liveFrameQueued` = the count left (its doc, :505), not 0. Checked in the code 2026-11-14: the headless
  clock moves only in `PumpFrame` and the idle wait (HeadlessSession.cs:189), so a headless pass drains everything as
  today (a test advances the clock inside actions to reach the budget); the two real-window tests that post and pump
  once post one cheap action (LiveResizeFrameTests.cs:184, SdlLoopPassTests.cs:137); leftovers start the next pass at
  once with nothing dirty (NextDeadline's Posted, :909) and wait out the interval with a dirty window (:851; a
  UI-thread post never wakes that wait), so they run right after the frame; passes with nothing dirty take a 1 ms
  wait every third (:875). Limit: a stream faster than the UI thread still grows the queue, and only batching
  (question 62) lets it catch up. Measure: the report's repro as PerfSuite rows (a list component bumped per posted
  change: a burst of 500; a stream every 50 ms, each action advancing the virtual clock) and a stream into a real
  window. Small; no API, no pixels.
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
- **junction-chromeparttests-load** (tests) — two `ChromePartTests` failed once under load (round 10). Round 13 (r13-6):
  no recurrence; since b6c6eb6f one case (a remote added) arrives through the real FileSystemWatcher alone, a new
  load-sensitive point. Watch at the full suite and in CI.
  Round 14 (r14-6): no recurrence; 63e005f3's `TempRepo.Settle()` removed round 13's ReviewTests race.
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
- **findbar-count-not-announced** (7, reader behaviour, not perf) — a find bar's new "n of m" reaches the bridge as a
  Name change on the count label; a reader speaks it only if it moves there (no `Announce`, no live regions).
- **vesper-start-focus-event** (7, lead) — at start Vesper raises a focus-changed event for a window that never had the
  foreground (round 13: 5 of 5 launches), and in 2 of 5 refuses a leaf patch and builds a second tree (~53 KB), start
  only. Cause not isolated; not perf.
- **a11y-gate-on-client** (7; question 50: not yet) — build the tree and raise events only after a UIA client asks for
  the window's root. Round 13 (r13-7): 0 root asks in 22 launches; the bridge costs Junction +40 % UI bytes at start and
  +29 % a commit-walk key, Vesper +29 % a drag. New risk evidence: a desktop-wide listener receives our bounds,
  structure, name and focus events (13 provider calls each) without ever asking for the root; a gate would cut it off.
  Decide after the user's live check with Narrator and NVDA (it could go with the menu check). Round 13 step 5 (b): each raise with a listener costs ~9 µs CPU (13 provider calls and 13 QIs, UI Automation's own work); a bulk rename (a keyed-by-index list swapping its contents renames every kept row, one raise each) could be told as one ChildrenInvalidated, or rows no client fetched could raise no Name: both change what a reader hears and need the same live check.
  Round 14 (r14-7): 0 root asks and 0 worker-thread calls in 21 launches of 5 apps; the bridge's share of UI-thread
  frame bytes: Junction start 17.5 %, palette open 35 %, palette close 98.7 %, clearing History's search 48 %, Vesper's
  viewer open 38 %, keys and wheel 1.4–2.5 %. Two items wait with it (below): a11y-items-on-demand-sooner and
  apps-junction-ref-move-a11y.
- **after-layout-callback** (1; question 51: only if another app needs it) — a public hook after layout and before
  render. Round 13 (r13-1): Vesper's wrapped viewer also needs a laid-out width, but a width lead fixes it without a
  hook (step 7); Aperture's and Junction's other after-frame reads are reveals or focus. Still not needed.
- **vk-format-switch-chained** (3; round 12 step 5; round 13 step 6 (c) not attempted) — switching F16 off rebuilds
  destroy-first: 325 ms in Reel (the 8-bit create alone 311 ms); the switch on is chained (2.7 ms plus a 96–154 ms first
  present), so a chained switch off would likely save ~200 ms once an HDR → SDR transition. Small; mind memory
  `resize-stretch-fix`.
  Round 14 (r14-3): keep, low; first an env-gated A/B of `DestroyBeforeReplacing` keyed on the new format (nobody
  has measured whether chaining shortens the 311 ms 8-bit create); no HDR clip run this round.
- **text-cjk-row-start-suffix** (5, lead; r13-5) — after step 3 the largest HarfBuzz source: a row-start word read off
  the line's word (Japanese 300 of 1,051 calls at 1000 px, 216 of 453 a drag; Chinese 201 of 1,165, 77 of 403). Needs
  the slice's own script guess and safe cuts at both ends. Medium–large; up to ~20–30 % of a new width's calls.
  Round 14 (r14-5): after step 1 (a) row-start segments are 88 of 951 calls (zh) and 118 of 681 (ja) at 1200;
  short words (under 8 characters) cut at need are the largest share (461 of 951); giving 2–7 character words their cuts
  in the tables halves a new width's calls but costs opening +248 KB (zh) / +121 KB (ja): a trade.
- **a11y-items-on-demand-sooner** (7; with question 50's live check; r14-7 Candidate 2) — serve a virtual list's items on
  demand above ~200 items, not 2,000 (`MaxEagerVirtualItems`, AccessibilityTreeBuilder.cs:18). AOT prototype, real
  windows with the desktop listener: Junction start 2,239.5 → 1,867.2 KB UI (−16.6 %; a11y 426 → 54 KB), Vesper's
  viewer open 1,742.5 → 1,099.2 (−37 %), a History search's 3 keys 2,840 → 718 provider calls, whole-tree builds for an
  overlay ~100 → 13–21 KB. A reader would notice: a relabelled served item is not told (AT-SPI keeps its copy), a list
  crossing the threshold builds the whole tree, a UIA client walking a long list makes a wrapper a step; AT-SPI needs
  the Orca check (a11y-served-items-followups). Decide with question 50's live Narrator/NVDA check.
- **apps-junction-ref-move-a11y** (8/7, lead; with question 50; r14-8 Candidate 1) — with History on screen a ref move
  pays the bridge 25–55 % of its UI bytes (terminal commit 1,579 vs 708 KB with `HULTUI_A11Y=0`, the app's Fetch 1,840
  vs 1,383) and one frame with 15.6–23.6 ms of "other": a new commit at the top moves every realized row, and a list
  keyed by index renames them all. Rows keyed by hash or a bulk rename told as one ChildrenInvalidated would change what
  a reader hears; count raises an event first (round 13 step 5 (b)'s probe).
- **apps-vesper-wrapped-drag-bytes** (8/1/5, lead; r14-8 Candidate 2) — Vesper's wrapped reader still allocates 248–269
  KB a drag step at 1 frame a step, none of it the bridge (64 PatchFroms a step headless suggests visible rows are
  rebuilt, not only re-measured); opening the reader 1,631 KB, 468 KB of it the bridge. Count builds and text calls a
  step first.
- **gl-ui-thread-rest-wakes** (3, lead; r14-3 REAL WINDOW) — the UI thread wakes at rest with no loop pass and no
  message (both hooks read 0): Junction 1.3–4.9 a second (3 launches), Reel 7.1–10.8 after a clip played, a resize run
  and driver-thread mpv reads (0 right after a fresh start); expert 4 saw 1.31 a second in 1 of 3 Junction launches
  (likely the real pointer). Next: stack-sample the UI thread with r14-3's h3 sequence.
- **mpv-cuda-context-at-rest** (3/8, lead; r14-8 REAL WINDOW) — after the first clip NVIDIA's `nvcuda64` thread wakes ~10
  a second, even for a raw y4m clip (mpv's CUDA context): whether CUDA could start only for a stream that
  hardware-decodes.
- **text-junction-first-visit-tokenize** (5/8, lead; r14-1 REAL WINDOW) — a file's first visit in Junction's commit walk
  allocates 7.0 MB on all threads against 454 KB a revisit: tokenizing a 300-line C# file 6.4–10.5 MB off the UI
  thread; the colours landing remakes the 34–37 rows on screen (rows unkeyed; keying them measured: +2.5 % bytes).
- **junction-files-head-key** (6, lead, unconfirmed; r14-6 REVIEW) — the Files view's file body and blame at HEAD are
  keyed `{repo}|HEAD|{path}` with no generation (JunctionApp.Files.cs:677–680, Blame.cs:87–93): an open file may keep
  showing old content after a commit. Correctness; check with the right loader first.
- **junction-summary-overtaken-at-start** (6, lead; r14-6 REVIEW) — AsyncLoader drops a superseded result, so a
  working-tree callback during the first full summary restarts it: with a log appended every 0.4–1 s the first summary
  lands at 0.8–1.3 s (1.3–2.1 s on 2 cores) after 2–4 dropped loads, its remote/for-each-ref/rev-parse/describe run 3–4
  times; on a slower machine it can be overtaken as long as callbacks keep coming (round 13's CI failure). Coalescing
  measured worse (Examined).
- **vesper-local-waiting-text** (6, wording, optional; round 14 step 6, 1b019826) — a slow local folder's waiting block
  reads "Connecting to C:" (the share/server text); it could read "Loading <folder>" for a local folder, which also
  changes what the Ctrl+Shift+N review state shows and the test that checks it. The user's call; not perf.
- **junction-ctrl-d-kept-patch** (6/4, correctness, small; round 14 step 2) — in a big commit, during the ~40 ms a file's
  first visit waits for its patch, the old file's diff stays on screen and `CurrentDiffTarget` (fullscreen, Ctrl+D's
  binary-file refusal) reads the kept file's patch: a Ctrl+D in that window acts on the old file. Have it read the
  selected path, or wait for the landing.
- **junction-review-cards-resize-stale** (1, correctness; round 14 step 4) — with review cards on and wrap off, a resize
  never re-measures the cards: `DiffWrapNeedsRemeasure` (apps/Junction/JunctionApp.cs:537–538) covers wrap only, so the
  resize at :506 asks for no build, and the after-frame check (JunctionApp.Diff.cs:701) is scheduled only after a build;
  the cards keep heights measured at the old width (`CardTextWidth`, JunctionApp.Review.cs:715).
- **text-bidi-real-window-private** (5/3/4, check; round 14 step 1 (b), c06aa102) — after an Arabic README's walk in a
  real window (GL, the 4K at 150 %), private bytes read 158/161 → 188/189 MB after a full collection, while the GC's
  committed bytes (46.0/48.8 → 47.3/47.5) and the working set (160/162 → 162/161) stayed and headless private bytes did
  not move: commit charge outside the GC heap, never resident, in the real window only, where frames now come 14 times
  as fast (likely the GL driver's). The change makes no native object. Trace it once (VMMap-style region types) to rule
  out a leak.
- **vk-instance-extensions-once** (3; fix when Vulkan Init is next touched) — `HasInstanceExtension`
  (SdlVulkanHost.cs:532) enumerates on every call (8 `vkEnumerateInstanceExtensionProperties` calls at Init): one shared
  list saves 7.1–7.4 ms of start-up, below the 10 ms bar.

## Landed

Round 14 (the round round 15's survey reviews; it moves to the archive at round 16's survey). Rounds 1–13 are in
[the archive](17-performance-team-ledger-archive.md#landed-rounds-14) ([round 5](17-performance-team-ledger-archive.md#landed-round-5),
[round 6](17-performance-team-ledger-archive.md#landed-round-6), [round 7](17-performance-team-ledger-archive.md#landed-round-7),
[round 8](17-performance-team-ledger-archive.md#landed-round-8), [round 9](17-performance-team-ledger-archive.md#landed-round-9),
[round 10](17-performance-team-ledger-archive.md#landed-round-10), [round 11](17-performance-team-ledger-archive.md#landed-round-11),
[round 12](17-performance-team-ledger-archive.md#landed-round-12), [round 13](17-performance-team-ledger-archive.md#landed-round-13)).

| Round | Commit | Candidate | Before → after |
|---|---|---|---|
| 14 | 2c809bc1, c06aa102, 5d9a5c71, 11ee4926, 9fd8cf07 | text-cjk-natural-end-trim (the review objection to 698c2758 (b)) + text-bidi-textarea-probes + text-word-cache-cjk-cliff (question 56: yes, the admission failed) | Junction's Files view on real gettext READMEs (zh 75,265, ja 79,011, both in one file 154,276, ar 76,025, he 27,913, th 41,708 characters; ACCESSIBILITY-PLAN.md with 208 emoji); NativeAOT probe host, each change behind a switch in the same binary, idle-gated, alternated ×2, UI-thread CPU min. (a) A table's last segment the word cache has is its own answer, and a natural word also ends at the first ASCII letter/digit ↔ other change at or past the table's end: zh resize to 1000 11.4 → 10.6 ms (808 → 555 KB, 1,433 → 1,141 calls), 1200 −7 %, 960 −8 %, drag at 960 1,928 → 1,353 KB; ja 1000 12.1 → 11.2; the 154k file ±1 %; real window zh 1000 13.3 → 11.6 ms (815 → 562 KB), ja 13.7 → 12.2. Slower: zh's frame back at 960 8.7 → 16.7 ms, a gen-1 collection that moved in (8.8 → 9.0 when it lands in the drag). (b) A TextArea's probes of a BiDi line are `MeasureTextWidth(span)` widths, and an unbounded BiDi line skips the break pass: Arabic drag at 1440 5,759 → 460 ms (−92 %; 830.9 → 44.4 MB; 4.37 M → 217k calls), drag at 960 7,014 → 701, open 170 → 98 (−42 %); Hebrew drag at 960 831 → 269 ms, Thai 295 → 152, emoji 83.8 → 79.2; real window Arabic drag 5,810 → 426 ms (841.8 → 32.8 MB). Slower: Thai at 1320 18.0 → 18.7 ms (+4 %, both pairs). Held: full-GC heap Arabic +3 MB (the width cache at its 4 MB bound); real-window Arabic private bytes +28–30 MB outside the GC heap and never resident (Waiting text-bidi-real-window-private). (c) A scan-resistant admission declined (4 variants: the big drag 157 → 184 / 177 / 63 / 76 MB, zh at 960 worse; DECISIONS.md); the bounds raised to 32,768 entries / 16 MB: the 154k file's drag at 1440 1,071 → 451 ms (157.5 MB → 953 KB; 396,885 → 908 calls), at 960 1,411 → 597, a new width −43…−52 %; real window drag 1,090 → 466 ms (156.9 MB → 708 KB); zh's frame back at 960 17.7 → 8.1 ms; TODO.md the same bytes and calls; held: cache 3.4 → 10.5 MB (the 154k file), zh 3.7 → 4.7, ja 4.0 → 6.0; private bytes 44.7 → 42.7 MB headless, 173 → 170 real window. Exact: 48k 7A18AF8DE8E05271, 240k F09E9038726E4BD6, CJK wrap 0 of 1,344,616 widths differ (1E6F1A93894D0B6A / 6C7FA51792E8EB84); 46,997 probe widths and layouts on the four READMEs 0 differ; goldens pass. step-ab (ded3e51c) `-Pairs 2` JIT + AOT 0/0, A/A 0/0; text/create 2,000 labels +192 KB (the cache's slots grow once). Tests fail under reverse edits; Windows Text 303, Rendering.Skia 421, Core 5,086, Headless 1,584, Junction 1,112, Vesper 935, Meridian 679; Fedora Rendering.Skia 421, Text 303 (9fd8cf07: the BiDi shaper bound 0.75, Noto faces give 0.51). DECISIONS.md. 4 holds, 0 foreground takes. |
| 14 | 44958e6e | junction-commit-detail-whole-patch, part: the parser's markers ordinal (the per-file load waits for question 60) | `ParseUnifiedDiff`'s nine `StartsWith(string)` calls are ordinal: on this sv-SE machine each was an ICU collation lookup (~890,000 on one plan merge; the suite's InvariantGlobalization never paid it), and a culture match is wrong too (a soft hyphen before "diff --git " made a second file). Parse of a whole patch, JIT, pinned, warmed, min / median of 10, alternated ×2: facced69 (8.0 M characters) 203.4 / 208.4 → 20.2 / 21.3 ms (−90 %), 61308cfd 148.1 → 13.4 ms (−91 %); bytes unchanged. Shared by the Commit tab, Changes, the range compare and conflicts. `DiffParsingTests.MarkersAreMatchedOrdinally_WhateverTheCulture` fails with the old comparisons; Junction.Tests 1,113 pass. |
| 14 | c7dd2365, 8b994a41, 9545744f | junction-commit-detail-whole-patch, the per-file load (question 60: (b) with (d)) | A commit's patch below 262,144 characters is read whole through a pooled buffer; a longer one stops git there, keeps the first file if it is complete, else reads the opening file alone (`-- :(literal)path [old path]`); another file is read on its first visit, the previous file's diff stays on screen until it lands (no "Loading…" frame), and after a file lands the next and previous files are read ahead (AsyncLoader producers, none on the UI thread). Readers of the parsed patch: `BuildCommitBody`, `PrefetchHighlight`, `CurrentDiffTarget` (fullscreen, Ctrl+D's binary refusal), `ComposerPosition` (range detail, still whole). Real window (NativeAOT, the 4K at 150 %, 4 holds, 0 foreground takes), idle-gated, alternated ×2: History walk on jrepo, 20 Downs 400 ms apart, 9.9–10.2 → 1.4–1.9 MB a key on all threads, UI 39.5–40.3 → 37.7–38.3 KB, GCs 14–15/11–12/4 → 2–3/1/0 per 20 keys, git runs 10.7–10.8 → 10.0–10.5 a key, UI time 1.3–1.8 ms either way; c655b411 (7.2 M characters, 278 files) key walk 1 frame a key, median 1.8–2.0 ms (before 2.0–2.1; without read-ahead 11 of 22 keys waited 2–3 frames), read-ahead +1.2 % bytes a key (1,969 → 1,993–1,995 KB), git runs 1.50 → 2.33–2.46 a key (each file read once, sooner), no GC added; a click on a far file 34.5–45.9 ms in 2 frames, the old diff kept (before 1.7–12.2 ms, 1 frame); revisits 1.5–6.8 ms; the first diff row 319–335 → 235–248 ms; a small commit (167b4ef9) unchanged. HultUI's last 300 commits 628.7 → 115.4 MB on all threads (−82 %), GCs 22/17/8 → 0/0/0, kept up to 32.0 → 1.0 MB. All 948 files of the 47 big commits (jrepo 480) read alone equal their slice of the whole patch. step-ab (5ade932c) `-Pairs 2`: AOT 0/0, A/A 0; JIT pair 1 0/0, pair 2 two a11y selection-walk rows +10–19 % (A/A 0; the suite never references Junction: noise). CommitPatchTests 8 (reverse edits of (b) and (d) fail); Junction 1,138, Headless 1,586 pass. |
| 14 | 21cf206c, a4bad904, 3a859d8e, 05f19d27, e0263b8f, c178893d | dotnet-junction-watcher-debounce-timer + reactivity-junction-watch-index-staging (question 58: yes) + reactivity-junction-watch-ignored-paths | (b) `GitWatcher` debounces on one re-armed timer, not a CTS + `Task.Run` + `Task.Delay` a event: NativeAOT probe, 1,000 files written 1 ms apart, 9,428–9,456 → 5,817 B a file (a bare FileSystemWatcher 5,814), exceptions 2,765–2,797 → 0, still 1 callback. (c) An index rename is told when its entries changed (path, mode, object id, flags): outside `git add` and `restore --staged` reach Changes (1 callback, 12 git runs); the 53-operation probe 3 fixed (4 of 60) on both backends, 0 regressions, stale 7 → 4 (files) and 8 → 5 (reftable); extra callbacks 15 of 60 in the survey's variant → 1 of 60 (an outside checkout, pull or stash and in-app actions no extra reload; over Changes an outside switch or in-app checkout draws 1 more build and frame, a load the base dropped as superseded now lands). Two follow-ups: `Forget` no longer counts an index write alone as untold (every in-app stage ran a preload, 19 → 22 git runs), and the watcher reads the index's baseline off the UI thread as it starts, kept only if the index is 250 ms older than the watcher (a fresh clone's first `git diff` writes the index even with `GIT_OPTIONAL_LOCKS=0`: `FixtureQuietTests` caught +1 reload and +2–16 git runs on open; start back to 0 callbacks, 25–28 git runs). (a) Events under a folder `git check-ignore` says is ignored ("folder/", no `--no-index`, so a force-added file is still heard) and a folder's own Changed reload nothing. Headless over Changes per build 2 callbacks / 22 git runs / 4 builds / 4 frames / ~2.9 MB → 0 / 1 / 0 / 0 / 0.22 MB, a rebuild → 0 / 0 / 0 / 0 / 0.14 MB, after a touched source file 4/44 → 1/11; real window (NativeAOT, the 4K at 150 %, 2 holds, 8 launches, 0 foreground takes) over Changes a build 2/22/4/4, 153 KB UI, 3.06 MB → 0/1/0/0, 0 KB, 0.26 MB, a rebuild → 0.18 MB; over History 0.41–0.47 → 0.17–0.27 MB; an outside switch over Changes 2 callbacks / 30 git runs → 1 / 19. ReviewTests on 2 cores (affinity 0x3) 5/5 at 05f19d27 and at e0263b8f. step-ab (5eaed9ad) `-Pairs 2` JIT + AOT: no row slower in both pairs, 0 allocKB moved (JIT pair 1 virtual/insert +10 %, icons/mount +28 %, not repeated; A/A video bar fade +17 % JIT). Every new test fails under its reverse edit; Junction 1,125, Headless 1,584 pass. REACTIVITY.md (the index trap rewritten, a working-tree section with three traps); GitProcess gains an internal `RunWithStdin` with a timeout. |
| 14 | 27ee5bda, a3ebdfca, 25972258, 835adab9, b3355310, 10d27366, 8e720829 | layout-junction-wrap-resize-lead + layout-hover-removed-row-no-frame | (a) With a wrapped diff shown, a resize and a file-list splitter drag hand the build the body's width plus or minus the change, a resize crossing no breakpoint or scale builds only the view, and a height-only resize builds nothing; a wrapped code label's flex basis is its real width, not 0 (835adab9: each new row was first laid out at zero width, one word a line, ~20 KB a line, which the lead's 400 height measures pushed out of the 4 MB layout cache: Changes with 400 rows splitter +33 % KB before it), and the sign column is held at 16 px only while lines wrap (b3355310: 835adab9 moved the code 2–3 px in 54 of 216 unified wrap-off frames). NativeAOT, idle-gated, alternated ×2, per step, before (1cb9c4a0) → after. Commit view, 200-line prose: 4 px width 2 → 1 frames, chrome parts 6 → 0, PatchFroms 291.7 → 51.2, 540 → 278 KB (−48 %), 6.39 → 4.11 ms (−35 %); splitter 446 → 265 KB, 5.4 → 3.3 ms (−39 %); height-only 180 → 3.2 KB, 0 builds; real window (the 4K at 150 %) 543.9 → 279.0 KB, 4.82 → 3.78 ms, 1 px physical 574 → 279 KB, splitter 2.80 → 2.01 ms (−27 %). Changes, 400 rows: unified width 653 → 382 KB and 7.0 → 5.0 ms, splitter 554 → 351 KB and 6.7 → 4.3 ms, split width 1,062 → 446 KB, splitter 1,064 → 450 KB and 8.0 → 4.6 ms; real window splitter 554 → 351 KB, 3.99 → 3.27 ms, 2 → 1 drawn; ~200 rows −40…−49 % KB, −33…−39 % time. Pixels: 216 settled frames (Commit and Changes 400 rows, unified and split, wrap off and on, widths 1,440 → 760, the 1,406 breakpoint, two heights, scale 1.5 and back) byte-identical to 1cb9c4a0's (base A/A 216/216); 0 stale against a full render; steps that drew twice 17 → 1 (the splitter release draws twice before and after). (b) A removed row's leave asks for no frame of its own: Meridian's tab switch with the pointer on the map list 2 → 1.5 frames a switch (to Requests 1 frame 4 of 4; back to the map still 2, the new row's tint), same-pixel frames 4 → 0, render visits 263 → 231.5, 1.10 → 0.93 ms headless; 0 stale of 8. step-ab (1cb9c4a0) `-Pairs 2` JIT + AOT 0/0 (A/A JIT 6 a11y rows slower: noise), after the fix `-Pairs 1` 0/0, no allocKB moved. Tests (DiffFrameCountTests +4, HoverAfterRebuildTests +2, a zero-width-layout spy 10d27366) fail under reverse edits but the tint guard; Junction 1,130, Core 5,086, Headless 1,586, Meridian 679, Vesper 935 pass. LAYOUT.md (lead a splitter like a resize; build only what reads the width). 1 hold, 0 foreground takes. |
| 14 | 64c127a2, 545d5d89, c283aed6 | painting-vesper-pointer-up-frame + painting-scrollbar-tween-tail | (a) Vesper's root release handler is passive (it only cancels the context-hold timer, ends a right-button mark, clears `_findResizing`): NativeAOT, alternated ×2, a click 2 → 1 frames (the 20 release frames were all identical to the frame before), UI time 4.0 → 2.1 ms headless at 1.5, 2.4 → 1.3 at 1.0; real window 20 → 10 frames per 10 clicks, 1.59–1.63 → 0.93–0.97 ms a click (the 4K at 150 %), 1.59–1.67 → 1.00–1.01 (the 1440p at 100 %). A right click's release still draws one frame (the mark's rebuild under the held button replaced the hovered row, and the release settles the hover; not checked for identical pixels). (b) `Scrollbar.SetHover`'s tween ends at its target within half a device pixel: a bar visit (on and off) headless 120 Hz × 1.5 30 → 18 frames, Junction 60.8–61.0 → 36.1–36.4 ms, Meridian 33.8–35.0 → 20.2–20.3, Vesper 57.5–57.9 → 34.2–34.8; 200 Hz × 1.0 48 → 28 frames, −41…−44 %; real window (120 Hz; no 200 Hz monitor that day) 31 → ~19 frames, Junction 21.2–21.6 → 13.0–14.8 ms, Meridian 10.4–10.5 → 6.7–7.0, Vesper 18.0–19.6 → 11.3–12.1 on the 4K, the same on the 1440p. Of the 6 frames dropped each way up to 6 were identical, the rest moved an antialiased edge less than half a device pixel in all; every frame still drawn hashes as the uncut frame at its step (72 visits), the last equals the settled frame; at 200 Hz × 1.0 4 identical mid-curve frames stay (17 → 4). Pixels (a): presses identical, 140 forced-frame checks a run, 0 misses. step-ab (1532dc4c) `-Pairs 2`: JIT pair 1 two RowHeights rebuild rows +22–29 % with the same rows +19–25 % in A/A (noise), pair 2 clean; AOT clean; no allocKB moved. Tests (PaneMoveTests +2, ScrollbarHoverTests theory at scales 1/1.5/2) fail under reverse edits; Core 5,086, Headless 1,589 (goldens), Vesper 937, Meridian 679, Aperture 363, Fluent 34, Junction 1,138 pass. 2 holds, 0 foreground takes. |
| 14 | 6a4c778d, ae8429d3, 3a26cd25 | junction-changes-save-same-frame (option (a)) + reactivity-vesper-nav-on-arrival (the slow-local-folder case: question 61) | (a) A watcher event over Changes starts the view's own loads (and the colours) in `Reload` without a build and lets the landing build. NativeAOT, idle-gated, alternated ×2: a save of a text file 2 → 1 frames and builds, UI KB 184.7 → 137.6 headless, 177.3 → 137.1 real window; of a C# file 3 → 1 (today's first frame drew the diff uncoloured), 393 → 154 headless, 375 → 149 real window; the last frame as early or earlier (626/609 → 595/599 ms with the debounce); every headless last frame equals a full build. (b) Vesper's navigation (folder, cursor, marks, history, `PaneDirty`) applies when a local listing lands, or at the 350 ms connecting delay; `IsConnecting` reads the cheap fields before `IsLoading` (which made the build rebuild when a load started: why the survey's half-prototype got worse); the root key handler is passive and asks for its frame itself, except after a navigation key whose navigation waits (an ignored key such as F13 still draws one frame). Real window (the 4K at 150 %, a11y on): frames a navigation 1.90/1.80 → 1.00, mixed frames (the new name over old rows) 0.90/0.80 → 0, window builds 2 → 1, part builds 5 → 4, patched 71 → 62, made 201.5 → 190.5, UI KB 308.5 → 289.2, time to the final frame 9.6/8.1 → 2.5/2.6 ms; a real local folder lands in ~2.5 ms. Shares and archives unchanged (name at 15–17 ms, connecting block at 407–412). A local folder slower than 350 ms (a listing held 700 ms) shows its name at 405–410 ms with the connecting block instead of at 16 ms over the old rows (question 61). Tab changes and pane swaps apply a waiting navigation first. step-ab (cae1a0bd) `-Pairs 2`: JIT pair 2 video bar fade +14/+17 % (seen in step 5), AOT pair 2 six shelf-walk rows −25 % with the same six +35 % in A/A: noise; no allocKB moved. ChromePartTests +2 and NavigationArrivalTests 2 fail under reverse edits; Junction 1,140, Vesper 939 (+4 skipped), Headless 1,589 pass; ReviewTests 3/3 on 2 cores. REACTIVITY.md two sections. 2 holds, 0 foreground takes. |
| 14 | 1b019826, c0ad6307 | reactivity-vesper-nav-on-arrival, the slow local folder (question 61: (b)) | A local folder whose rows are not there 50 ms after the key moves the pane: its name over the existing waiting block (spinner, "Connecting to C:", Cancel), the rows landing into it; never the new name over the old rows; a folder that lands within 50 ms keeps its single frame; shares, servers and archives unchanged. Why 50 ms: in a real window (warm cache) 61 files reach their frame in 2.5–2.9 ms, 1,000 in 3.1–6.8, 5,000 in 5.5–8.7, 20,000 in 12.0–17.2, each one frame; 50 ms is 3 frames at 60 Hz. Real window (NativeAOT, idle-gated, alternated ×2): a local navigation still 1.00 frame, 0 mixed, 1 window build, 4 part builds, UI 289.2 → 289.6 KB, last frame 2.8/2.6 → 2.6/2.9 ms; a 700 ms local listing: name and waiting block at 377–378 → 63–66 ms (headless 404–410 → 93–96), rows ~710 ms, 0 mixed; the archive (name 2–3 ms) and the share (name 15–16, block 394–412) unchanged. step-ab (70841b39) 1 pair: JIT video bar fade +13–15 % and seven a11y rows −10…−16 % with no a11y code changed, AOT icons/mount 600 −11/−13 % (seen in step 5's A/A): noise; no allocKB moved. `NavigationArrivalTests.ASlowFolder_ShowsItsNameAndTheWaitingBlock_NeverOverTheRowsItLeaves` fails at 350 ms and without the block; Vesper 939 (+4 skipped), Headless 1,606 pass. REACTIVITY.md. 2 holds, 0 foreground takes. |
| 14 | b62e22cf, 4b52b42e | layout-hover-follows-scroll (question 59: yes, after before/after images) | A drawn frame in which a scroll view's offset moved (wheel, key, scrollbar, reveal, code, clamp, sticky) hit-tests the resting pointer again before a11y and drawing (`Window.FollowScrollWithHover`, `ScrollViewElement.ContentMoves`, `HitTester.HitTestRealizing`, all internal): leave/enter, hover style, list tint and cursor move in the scroll's own frame. A showing tooltip whose owner scrolled away closes; nothing follows while a press is held (the release asks for the frame that follows) or with the pointer outside the window. Real window (NativeAOT, the 4K at 150 %, 120 Hz, one 113 s hold, alternated ×2, pointer placed in-process): Junction History wheel 1.03 → 1.00 frames a notch, 0.883/0.865 → 0.878/0.863 ms, 8.0 → 8.5/7.9 KB; held key 1.03 → 1.00 frames, 1.022/0.959 → 0.952/0.909 ms; Vesper Detail wheel 1.00 either way, 1.070/1.065 → 1.076/1.046 ms; held key 1.095/1.107 → 1.104/1.129 ms. Headless Junction 1.498/1.492 → 1.473/1.454 ms, +0.3 KB (the date now under the pointer starts its tooltip delay). Row under the pointer hovered 0/4 → 4/4. The old 1.03 was ff6125a6's settle drawing a second frame after a notch recycled the hovered row. Per scroll frame one more hit test of 12–33 visits, 0 B. step-ab (7e82f818) 2 pairs: AOT clean; JIT pair 2 video bar fade +15 % (noisy in steps 5–8); no allocKB moved. `HoverFollowsScrollTests` (8): six guards each fail under a reverse edit (7, 2, 1, 1, 1, 1); Core 5,086, Headless 1,614, Junction 1,140, Vesper 939, Meridian 679, Aperture 363, Reel 51, Fluent 34. ELEMENTS.md "Hover follows a scroll under a still pointer". |
| 14 | 00f17182, f1029545 | a11y-overlay-open-close-part | Opening or closing a hit-testable overlay puts its part under the root or takes it out (`SwapOverlayParts`) in one diff in build order; a walk frame swaps the parts first, and a frame that needs a whole tree still builds one. Two things only the whole build used to tell are told on their own: a popover anchor's expanded state on an element with children (to the walk), and a menu entry whose flyout opens or closes (its menu's part, `PinContextMenuItem`). Real window (NativeAOT, the desktop UIA listener, the 4K at 150 %, 4 holds), before → after, a11y KB / UI-thread frame KB (bridge off): Junction History context menu open 102.3 → 8.6 / 213.4 → 119.8 (113.8), close 99.9 → 0.8 / 100.8 → 1.8 (1.0); palette open 117.0 → 16.3 / 267.7 → 166.1 (150.7), close 101.4 → 1.4 / 101.7 → 1.7 (0.2); Vesper palette open 34.6 → 17.1 / 348.6 → 331.1 (314.0), close 18.7 → 1.6 / 19.1 → 1.9, the recent-folders popover open 22.4 → 5.6 / 56.8 → 42.8, close 17.2 → 0.4, a tab's context menu open 21.1 → 1.7 / 28.0 → 8.4, close 16.9 → 0.4; Meridian palette open 17.9 → 11.3 / 88.5 → 81.9, close 8.0 → 1.4 / 8.3 → 1.8; whole trees 1 → 0 on every open and close. Event streams 117/117 identical; audit (7 suites) 0 STALE, 0 WALK-VALUE/EVENT, 6,809 walks, 10 fallbacks; a cross-process UIA client read the same overlay tree and focus in all three apps. step-ab (3b60256f) `-Pairs 2`: JIT pair 1 video bar fade +17/+19 % (moved both ways in steps 5–6), pair 2 and A/A clean; AOT clean; no allocKB moved. `A11yOverlayTests`, `A11yLeafPatchTests` compare UIA and AT-SPI against a forced build (5 reverse edits each fail 1–14); Windows Core 5,086, Headless 1,606, Linux bridge 104, Junction 1,140, Vesper 939, Meridian 679, Aperture 363; Fedora Linux bridge 104, Headless 1,426. ACCESSIBILITY-PLAN.md. |

## Examined, not worth it

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

No question is open from rounds 1–14. Their answered questions are in the archive
([round 14](17-performance-team-ledger-archive.md#needs-the-user-round-14), [round 13](17-performance-team-ledger-archive.md#needs-the-user-round-13),
[round 12](17-performance-team-ledger-archive.md#needs-the-user-round-12), [round 11](17-performance-team-ledger-archive.md#needs-the-user-round-11),
[round 10, with round 9's 26 and the earlier 10](17-performance-team-ledger-archive.md#needs-the-user-round-10),
[round 9](17-performance-team-ledger-archive.md#needs-the-user-round-9), [round 8](17-performance-team-ledger-archive.md#needs-the-user-round-8),
[round 7](17-performance-team-ledger-archive.md#needs-the-user-round-7), [round 6](17-performance-team-ledger-archive.md#needs-the-user-round-6)). Search it by candidate id
before asking anything an earlier round asked. Recommendations in brackets. Question 50 (a11y-gate-on-client)
and 51 (after-layout-callback) were answered "not yet" and "only if another app needs it"; both are Waiting.
Round 14's a11y-items-on-demand-sooner and apps-junction-ref-move-a11y wait with question 50 (Waiting); question
57 (text-open-measures-every-line, full) was answered "not now" (Waiting).

Round 15 questions. Recommendations in brackets.

62. **Batch posted work** (2026-11-14; the other half of reactivity-posted-drain-bound, from a framework user's report):
    each posted action and await continuation runs outside a batch, so N posts that each write a signal a component
    reads are N rebuilds in one pass (the report's repro: 100 posts, 100 builds, expected 1), where an event handler's
    writes coalesce (`ReactiveRuntime.AutoBatch`). Round 8 declined batching the drain (Examined: "an await continuation
    that writes and then reads would see the old tree, in 5 apps"). An audit on 2026-11-14 of apps/, samples/ and src/
    (not tests/) found 0 such sites: posted code ends in a rebuild request with nothing read after it; focus and scroll
    go through pending flags read in `OnAfterBuild` (JunctionApp.cs:1107–1200, CommanderApp.cs:974–1030,
    MeridianApp.cs:344–371, VideoPlayer.cs:306); `AsyncLoader` landings (AsyncLoader.cs:147) and the media, web view and
    terminal applies batch themselves; nothing calls `FlushNow` or `SuspendBatching`; the drain runs before layout and
    render, so a deferred rebuild still lands in the same frame. Limits: `MarkDirty()` rebuilds at once even in a
    batch (ComponentHost.cs:67–92), so only signal writes (`Rerender`, `[Reactive]`) coalesce; a posted action that
    runs a nested loop (a file dialog under the WinForms host, whose timer runs whole frames: HultUIApplicationHost.cs:
    174–184) would hold the batch open across them, as a click handler already can (no app does either). (a) Keep it
    so: apps coalesce their own posts (REACTIVITY.md's rule), the user's app keeps its workaround. (b) Batch every
    drain pass by default under the existing `AutoBatch` switch, a nested pass suspending the outer batch (which also
    fixes the click handler's case), REACTIVITY.md updated; code elsewhere that writes a signal after an await and then
    reads an element that rebuild makes would read the old tree. (c) An opt-in batched post: new public API, nothing
    existing changes. [(b): posts then behave as event handlers do, none of our code breaks, and `AutoBatch = false`
    turns both off.] → **(b)** (2026-11-14; built with reactivity-posted-drain-bound in round 15, as its own commit,
    with the nested-pass guard and REACTIVITY.md; audit `tests/` first).

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

From 2026-11-13 (round 14's survey); earlier entries are in the archive ([to 2026-10-25](17-performance-team-ledger-archive.md#log-2026-10-07-to-2026-10-25),
[2026-10-26 to 2026-10-27](17-performance-team-ledger-archive.md#log-2026-10-26-to-2026-10-27),
[2026-10-27 to 2026-11-02](17-performance-team-ledger-archive.md#log-2026-10-27-to-2026-11-02),
[2026-11-02 to 2026-11-03](17-performance-team-ledger-archive.md#log-2026-11-02-to-2026-11-03),
[2026-11-03 to 2026-11-08](17-performance-team-ledger-archive.md#log-2026-11-03-to-2026-11-08),
[2026-11-08 to 2026-11-09](17-performance-team-ledger-archive.md#log-2026-11-08-to-2026-11-09),
[2026-11-09 to 2026-11-11](17-performance-team-ledger-archive.md#log-2026-11-09-to-2026-11-11),
[2026-11-11 to 2026-11-12](17-performance-team-ledger-archive.md#log-2026-11-11-to-2026-11-12),
[2026-11-12, round 12](17-performance-team-ledger-archive.md#log-2026-11-12-round-12),
[2026-11-12 to 2026-11-13, round 13](17-performance-team-ledger-archive.md#log-2026-11-12-to-2026-11-13-round-13)).

- 2026-11-13: round 14 survey started in a fresh context (session 41943a9b; usage 42 % weekly). Main has not moved
  (origin/main e432c224 is in the branch). Moved to the archive by a checked script (`archive_r14.py`; every moved line
  found there, nothing lost): round 12's 6 Landed rows, round 13's build order with its closed-from-Waiting note, round
  13's answered questions 52–54, and round 12's 88 log lines (2026-11-12, its survey to its app releases). Attached: both
  monitors (the 4K at 150 % primary, the 1440p to its left); Steam is running (it holds 1 ms machine-wide). The eight
  experts survey round 13's build, 8f01b6fd..e432c224 (49 commits, two merges: round 13's own into main), in their own
  worktrees on this commit, in real windows too.
- 2026-11-13: round 14 survey done (reports fb946f66..06d4db59, `17-reports/r14-*`, ~65 min; 42 → 50 % weekly). No
  expert satisfied. One review objection: 698c2758 (b), the natural-end words, cost +49…+72 % UI KB at every new width
  on real Chinese text → step 1 (a). Round 13 helped the real apps more than headless shows (geomean 0.997 headless;
  real windows: pointer sweeps Junction diff 33 → 0 frames, Vesper 48 → 2; the app's Fetch −36 % and checkout −34 % UI
  KB; Junction start −30 %; a Chinese drag at 960 logical −98 % a step; Aperture −24.5 MB GPU after size changes).
  Real windows: 104 guarded launches in 19 holds of `Global\HultUI-r14-desktop`, 0 foreground takes, 0 driver events,
  0 screen reads. What only real windows or real data showed: a wrapped Arabic README allocates 842.6 MB a drag (every
  wrap probe through the full BiDi layout); History at a reading pace 9–10 MB a key on all threads (the whole commit
  patch); a build beside Junction reloads it from gitignored folders; Vesper's navigation draws a frame with the new
  title over the old listing (1.70 frames a navigation); a save over Changes, a Vesper click and a scrollbar hover tween
  draw same-pixel frames; opening or closing the palette builds the whole reader tree; the video interop loads GL
  inside the first video frame. Merged into an 8-step build order (expert 4's watcher timer into expert 6's step 3;
  expert 1's Changes frame into expert 6's step 6), 5 Waiting items closed, 9 added, questions 55–59 asked. For
  PERFORMANCE.md: frames drawn inside the size message carry the previous frame's "other" share (read only bytes and
  frames from drag rows); a parser's `StartsWith(string)` is culture-sensitive on a Swedish machine, and the suite's
  InvariantGlobalization hides it (use ordinal); a root pointer-up handler that changes nothing visible should be
  passive; `PatchFrom` marks paint on every patch, so a view build ahead of its load draws a same-pixel frame; a list
  keyed by index renames every kept row when a commit arrives at the top; walks on a shared fixture hear other
  sessions' git activity. Phase → build.
- 2026-11-13: step 1 spawned (expert 5, base ded3e51c, brief `brief-r14b1.md`). The user answered questions 55–59 with
  the recommendations: 55 (b), the GL preload stays opt-in and Aperture opts in (step 8); 56 yes, raise the word cache
  bounds only if step 1 (c)'s admission fails (told to step 1's agent); 57 re-measure after step 1, then ask again; 58
  yes, Junction hears terminal staging (step 3 (c)); 59 yes, hover follows content scrolled under the pointer, with
  before/after images for the user before the commit (new step 9).
- 2026-11-14: round 14 step 1 landed (2c809bc1, c06aa102, 5d9a5c71, 9fd8cf07; captures 11ee4926): the review objection
  answered (a CJK table's end reads a segment the cache has, a natural word stops at a Latin name: zh new widths −7…−8 %
  time, −18…−31 % KB); a TextArea's probes of a wrapped BiDi line are span widths (Arabic drag −92 % time, 830.9 → 44.4
  MB; real window 5,810 → 426 ms); the word cache holds 32,768 words / 16 MB (question 56: the admission failed; the
  154k-character CJK file's drag −58 %, 157.5 MB → 953 KB). Every corpus exact; step-ab 0/0. Open: real-window Arabic
  private bytes +28 MB outside the GC heap and never resident (Waiting, a check). Question 57 asked again with what is
  left a drag frame (itemizing only, ~11–12 ms a frame on the largest documents). For PERFORMANCE.md: log GC counts per
  phase (a drop in allocation moves a collection into one frame); take the idle gate before the desktop hold (it timed
  out inside again); test render contexts without prefix tables route a line search through `MeasureSubstring`; a text
  test's count bound needs margin for Fedora's faces. Next: step 2.
- 2026-11-14: round 14 step 2 paused for the user (question 60). Landed: 44958e6e, the diff parser's markers ordinal
  (a plan merge's parse 203 → 20 ms on this sv-SE machine). The per-file load is built and proved (948 files of the 47
  big commits read alone equal their slice of the whole patch; 7 tests, a pixel check of the pane on first and second
  visits) but not committed: in a big commit a file's first visit waits for a git run (33–47 ms, a "Loading…" frame).
  step-ab and Headless.Tests run after the answer. For PERFORMANCE.md: a parser that tests markers with
  `StartsWith(string)` pays ICU on a non-English culture; a headless key press already builds and renders, so a test
  of a key's own frame asserts right after `PressKey` (one pump lands the load). Next: step 3.
- 2026-11-14: round 14 step 3 landed (21cf206c, a4bad904, 3a859d8e, 05f19d27, e0263b8f; captures c178893d): Junction's
  watcher debounces on one timer (0 exceptions, −38 % bytes a file event), hears outside staging (question 58; 3 of 53
  operations fixed, 0 regressions, 1 extra callback in 60 after two follow-ups for echoes the measuring turned up), and
  drops writes under folders git ignores (a build beside Changes 22 git runs, 4 frames, 3.06 MB → 1, 0, 0.26 MB in a
  real window). ReviewTests 5/5 on 2 cores; step-ab no row slower. Step 2's held patch still applies. For
  PERFORMANCE.md: `git diff` writes the index even with optional locks off; `git check-ignore --stdin` stops at the
  first path inside a submodule and must have its output read while its input is written; a content baseline is safe
  only read together with a tell or before the watcher listens. Next: step 4.
- 2026-11-14: the user answered question 60 ((b) with (d): keep the previous diff on screen and read the neighbours
  ahead) and question 57 again (not now; Waiting).
- 2026-11-14: round 14 step 4 landed (27ee5bda, a3ebdfca, 835adab9, b3355310, 10d27366; captures 25972258, 8e720829):
  Junction's wrapped diff leads its width on a resize and a splitter drag (one frame a step, −48 % KB, real window −22
  … −27 % time) and a height-only resize builds nothing; a removed row's leave draws no frame (Meridian's same-pixel
  frames 4 → 0). The first report found Changes with 400 wrapped rows +33 % KB on a splitter step; sent back, the agent
  found the cause (a wrapped label's zero flex basis laid each new row out one word a line, flooding the 4 MB layout
  cache) and fixed it without touching the bound (400 rows now −37…−58 % KB); asked to compare settled pixels with the
  base, it found that fix moved the code 2–3 px in 54 of 216 wrap-off frames and fixed that too (216/216 identical). New
  Waiting item junction-review-cards-resize-stale. For PERFORMANCE.md: a flex child with basis 0 is laid out at zero
  width first (one word a line for text), which can flood a layout cache; compare settled frames with the base build,
  not only with a full render of the same build (that check cannot see a pixel move both share); lead a splitter like a
  resize, and build only what reads the width. Next: step 2's per-file load (question 60), then step 5.
- 2026-11-14: round 14 step 2 landed (c7dd2365, 8b994a41; captures 9545744f; 44958e6e before): a commit over 262,144
  characters reads its patch a file at a time, keeps the open diff on screen until the next file's lands and reads the
  neighbours ahead (question 60). History walk 9.9–10.2 → 1.4–1.9 MB a key on all threads, GCs 14–15/11–12/4 → 2–3/1/0
  per 20 keys; a big commit's key walk 1 frame a key with no "Loading…" frame; a click on a far file ~40 ms with the old
  diff kept; the 300-commit total −82 %. New Waiting item junction-ctrl-d-kept-patch (Ctrl+D in the ~40 ms wait acts on
  the old file). For PERFORMANCE.md: let a loader land into state no build reads as a tracked read, so background loads
  draw no frames; `PressKey` and `Click` build at once in a headless test. Next: step 5.
- 2026-11-14: round 14 step 5 landed (64c127a2, 545d5d89; captures c283aed6): a Vesper click draws one frame (real window
  −40…−42 % UI time a click), and the scrollbar's hover tween ends within half a device pixel (a bar visit 31 → ~19
  frames, −35…−40 % time in real windows; every frame still drawn hashes as before). Both monitors read 120 Hz that hour.
  For PERFORMANCE.md: a root-level release or tracker handler should be `OnPassive`; a tween can stop at its target once
  the travel left is below half a device pixel. Testing trap: a test that right-presses in Vesper must cancel the hold,
  whose timer runs on real time and opens the real shell menu. Next: step 6.
- 2026-11-14: round 14 step 6 landed (6a4c778d, ae8429d3; captures 3a26cd25): a save over Junction's Changes builds once,
  when its rows land (a C# file 3 → 1 frames, −60 % UI KB; today's first frame drew the diff uncoloured), and Vesper's
  navigation moves the pane when the next folder's rows land (real window 1.90 → 1.00 frames, 0 mixed frames, final
  frame 9.6 → 2.5 ms). The agent asked about a local folder slower than 350 ms (the name now shows at ~405 ms instead of
  16 ms): question 61. For PERFORMANCE.md: a build that reads `IsLoading` rebuilds when a load starts, so read the cheap
  fields first; an ordinary root key handler draws a frame for every key; start a view's own loads in the reload and let
  the landing build. Next: step 7 (step 6's agent resumes for question 61 after it, if the answer changes ae8429d3).
- 2026-11-14: the user answered question 61: (b), a slow local folder moves the pane at ~50 ms.
- 2026-11-14: round 14 step 7 landed (00f17182; captures f1029545): an overlay opening or closing builds only its part of
  the reader tree (Junction's palette close 101.7 → 1.7 KB UI, a context menu close 100.8 → 1.8; whole trees 1 → 0 on
  every open and close in three apps; 117 event streams identical, audit clean, a cross-process client reads the same).
  For PERFORMANCE.md: a state derived from "is my overlay open" must notify its change, since no whole build tells it by
  the way any more; a probe that reads a live log opens it with FileShare.ReadWrite (one launch crashed on it). Next:
  step 6's follow-up for question 61, then step 8.
- 2026-11-14: round 14 step 6's follow-up landed (1b019826; captures c0ad6307; question 61 (b)): a local folder slow to
  list moves the pane at 50 ms to its waiting block (a 700 ms listing shows its name at 63–66 ms in a real window, from
  377–378), never over the old rows; ordinary navigations still 1 frame. Optional wording item vesper-local-waiting-text
  to Waiting. Next: step 8.
- 2026-11-14: round 14 step 8 measured and reverted (DECISIONS.md 0ba99050): the opt-in GL driver preload for video
  interop brought the first video frame only 11–21 ms sooner (mpv's own GL setup got slower after an early load) and
  cost every session that never plays a 10-a-second NVIDIA thread and +18–28 MB (Examined). Next: step 9 (images to the
  user before its commit).
- 2026-11-14: the user said yes to step 9's before/after images (Junction History, Vesper thumbnails and detail, Fluent
  link cards) and its choices: a showing tooltip closes when its owner scrolls away, hover stays put while a button is
  held, and every kind of scroll is followed. Its agent was resumed to test, measure and commit.
- 2026-11-14: round 14 step 9 landed (b62e22cf; captures 4b52b42e): hover follows content that scrolls under a resting
  pointer in the scroll's own frame (Junction History 1.03 → 1.00 frames a notch in a real window, no time added; the
  row under the pointer hovered 0/4 → 4/4; one more hit test of 12–33 visits a scroll frame, 0 B). Added after the
  images: a release after a scroll during a held press asks for the frame that moves hover. Round 14's build is done.
  Next: the full suite, then release.
- 2026-11-14: round 14's full suite at e21a0202: Windows 0 failures (Junction 1,140 in 10 m 43 s), docfx 0 warnings;
  Fedora 1: Vesper's picking the top volume ("/") left the pane on its folder, deterministic, since step 6(b) ae8429d3
  (bisected on Fedora: cae1a0bd passes). Step 6's agent was sent back to fix it; no Fedora run in step 6's brief caught
  it. For the guidelines: a step that changes when a navigation or a load applies runs its app's tests on Fedora too.
- 2026-11-14: the Fedora failure fixed (6ea0db3b): listing the top of the tree ("/", and "\" on Windows) always threw
  (`Directory.GetParent("")` for its ".." row), older than this round; before ae8429d3 the pane moved anyway over the
  old rows, after it waited for a listing that never came. Now the top lists with no ".." row, and a listing that throws
  moves a waiting navigation at once (`ListingFailed`). Tests `FileSystemTests.TheTopOfTheTree_ListsWithNoParentRow`,
  `NavigationArrivalTests.ANavigationToTheTopOfTheTree_MovesThePane`; Fedora whole suite 22/22 projects pass at
  6ea0db3b; Windows Vesper 941, Headless 1,614. Phase release.
- 2026-11-14: round 14 RELEASED. Merged into main 5b2985be and pushed (origin/main e432c224 unmoved, tree identical
  to the tested branch). CI run 43637780093 on 5b2985be: Windows `test` red once on
  `HultUI.Syntax.Tests.LongLineTests.ALongJsonLine_IsColouredFurther` (the full line's tokenize coloured one `:` at
  1866 that the prefix's did not; no Syntax change this round; 5/5 local, 4/4 on 2 cores; a flake candidate in memory
  `ci-red-findings`); every other job green, CodeQL green. The rerun was cancelled at 21:03 by the user's push
  a589b52b and Dependabot's 5aa1d25b (workflows and docs only); CI run 43642521961 on 5aa1d25b, which carries round
  14, all green (Windows test, test-linux, docs, keyrings), CodeQL and the Junction installer green. origin/main
  merged into the branch. Round → 15, Phase survey, in a fresh context.
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
