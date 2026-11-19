# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 17
- **Phase:** survey — round 17 running (session 35903ed7, started 2026-11-19). The eight experts survey round 16's build
  and main's 8 commits from the archive commit, in their own worktrees, in real windows too, under the brief
  `brief-r17-survey.md` (session 35903ed7's scratchpad); their reports go to `17-reports/r17-<k>-<area>.md`. When all
  eight are in: merge them into Verdicts, a round 17 build order and Waiting, as round 16's survey did, and set Phase
  `build`. To resume in a fresh context, run in this worktree: `/loop Run the next step of the performance team: follow
  docs/plans/17-performance-team.md, with state in docs/plans/17-performance-team-ledger.md. Read both once per session
  (afterwards only the ledger's Phase and the part the step needs), do the step the ledger's Phase calls for, record it
  in the ledger, and commit.` (agent ids in `r17-agents.txt` beside the brief; an expert whose report is not in
  `17-reports/` is re-run). Older rounds are in [the archive](17-performance-team-ledger-archive.md) (search it, don't
  read it whole). History note: rewritten 2026-10-25 (every commit after 3353ce47 has a new ID; a pre-push hook
  refuses pushes built on the old history).
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`)
- **Review commits since:** round 17's survey reviews round 16's build, 32c85e81..b131ae85 (the last survey report to the round 16 release; 50 commits not merges), with main's 8 commits outside plan 17 that its merges 666e6486 and 5e180d17 brought in (266d6aee, 4917f40d, 23b552d8, 38ec9773, b8a3dcf5, d3e40ecb, 22c6e002, 791d1a44); main gained nothing after b131ae85. Earlier: round 16's survey reviewed round 15's build, 587f1767..75f2288a (the last survey report to the round 15 release; the one merge is round 15's own into main), and main's 41 commits outside plan 17 since that release, `git log --no-merges 75f2288a..3038f82b ^9550ecec` (42 with the ledger's cc093b50) (SkiaSharp 4 043f5b38, SDL 3.5.0 fd0cccb7, the app team's fixes; the screen-reader fix counts as reviewed). Earlier: round 15's survey reviewed round 14's build, 06d4db59..5b2985be (the last survey report to the round 14 release; the one merge is round 14's own into main). Earlier: round 14's survey reviewed round 13's build, 8f01b6fd..e432c224 (the last survey report to the round 13 release; its two merges are round 13's own into main). Round 13's survey reviewed round 12's build, 5f1744ca..afc1ed40 (the last survey report to the round 12 release; the one merge is round 12's own into main). Round 12's survey reviewed round 11's build, 5731ee58..b0dd1954 (the last survey report to the
  round 11 release; it includes the .NET 11 upgrade's step 0 only as already reviewed). Earlier: round 11's survey reviewed round 10's build and the .NET 11 upgrade, f4d6050d..868e2ffc
  (the last survey report to the upgrade's captures; round 10's merge into main cc55bdcc is in the range). Earlier: round 10's survey reviewed round 9's build, 4919ac40..facced69 (the last survey report
  to the round 9 release; 82 commits, 81 not merges; no merge from main this round). Round 9's survey reviewed
  eaa1ec29..10acc8c0 (round 8's build, with main's merge 3a15b5b1); round 8's survey reviewed 17d4220a..df45b4a2.
- **Probe code:** round 17 surveys in session 35903ed7's scratchpad
  `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\35903ed7-ff10-416d-84e9-b6c09675ab6b\scratchpad\r17-<area>\`
  (the common brief `brief-r17-survey.md` and `archive_r17.py` beside them); round 16 surveys and build probes in session
  b6ef64d9's scratchpad `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\b6ef64d9-4ed1-4793-9955-6a986d74de79\scratchpad\`
  (`r16-<area>\`, `r16b<N>\`); round 15 surveys in session 8e8ba33b's scratchpad
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

Rounds 1–16's build orders all landed but round 6's step 8, round 10's step 14, round 11's step 1 (c), round 14's
step 8 and round 15's step 8 (a), measured and declined, round 11's steps 13 and 14, round 13's step 5 (b),
round 15's step 1 and round 16's step 10, measured with nothing to change, and round 13's step 6 (c), not attempted
(back to Waiting) (rounds 5–16 are in [the archive](17-performance-team-ledger-archive.md#round-16-build-order), with each survey's
closed-from-Waiting note). Round 17's build order comes from its survey.
A build step may bundle one expert's related small candidates as separate commits. Items marked **(user)** wait for a
decision in Needs the user; the rest of their step goes ahead.

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

- **junction-colour-landing-remakes-rows** (6 or 5, lead; round 16 step 7) — when a diff's colours land (markdown, C#),
  every row on screen is made again and each wrapped label measured again (~21 Mcycles on Junction's first Commit open
  at 2160×1350; with colours held that open is −30 %, which step 7's change could not keep: +9 % on the session's first
  Commit open). Recolour the kept rows in place (the spans change, the line breaks don't), or land the colours before the
  rows' first frame when they are close. For the round 17 survey.

## Landed

Round 16 (the round round 17's survey reviews; it moves to the archive at round 18's survey). Rounds 1–15 are in
[the archive](17-performance-team-ledger-archive.md#landed-rounds-14) ([round 5](17-performance-team-ledger-archive.md#landed-round-5),
[round 6](17-performance-team-ledger-archive.md#landed-round-6), [round 7](17-performance-team-ledger-archive.md#landed-round-7),
[round 8](17-performance-team-ledger-archive.md#landed-round-8), [round 9](17-performance-team-ledger-archive.md#landed-round-9),
[round 10](17-performance-team-ledger-archive.md#landed-round-10), [round 11](17-performance-team-ledger-archive.md#landed-round-11),
[round 12](17-performance-team-ledger-archive.md#landed-round-12), [round 13](17-performance-team-ledger-archive.md#landed-round-13),
[round 14](17-performance-team-ledger-archive.md#landed-round-14), [round 15](17-performance-team-ledger-archive.md#landed-round-15)).

| Round | Commit | Candidate | Before → after |
|---|---|---|---|
| 16 | 40cadb57, 8c6d45de | text-wrap-range-one-line-tolerance (correctness; the review objection to 52852f6e against f388b11d) | `WrapRange` also keeps the text's own width T (NaN with a newline), and `KeptWrap` refuses any width where `T <= maxWidth + OneLineFitTolerance`, the one-line fast path's own expression; one float a range, no API. Probe `r16b1\tolcheck` (8 strings × 2 spacings × 4 ε, wrapped once at T − 2/256, then the shape cache cleared): 64/64 asks differ → 0/64; edge sweep (1,984 asks, 49 floats around T − 1/256 and 13 widths from T to T − 0.01, cache cold and warm, bit for bit) 514 → 0; the widths just past the edge still answered by the kept wrap (896). Round 15's real-document equivalence (5,038,170 layouts): 3 → 0 differ — three archive layouts in sans 13 at 667.99976 px with text 668.00195 px wide, 2 lines kept where a fresh layout gives 1: the bug was on real text at the base. Kept answers 1,430,056 → 1,430,053. Corpus hashes unchanged (48k 7A18AF8DE8E05271; 240k F09E9038726E4BD6; CJK wrap 1E6F1A93894D0B6A / 6C7FA51792E8EB84, 0 of 1,344,616). step-ab (031ae90f) `-Pairs 2`: AOT 0/0 both pairs, A/A 0; JIT pair 1 six untouched rows slower (grid auto-column, five a11y rows), pair 2 none, A/A none; wrap, resize and rich text rows −3.0…+1.1 %; the two "type in a rich text editor inside a VirtualList row" rows +0.9 KB on JIT only, both pairs (JIT object stack allocation: filtered both 2.4 / 2.0, with `DOTNET_JitObjectStackAllocation=0` both 3.8 / 3.5, 0 of 235 rows moved). `WrapRangeTests.AKeptWrap_LeavesTheWidthsAFloatErrorShort_ToTheOneLine` fails under a reverse edit; Windows Text 311, Skia 457, Headless 1,733 (goldens), Vesper 951 (+4 skipped); Fedora Text 311, Skia 457. No real window (no frame or memory claim). The verdicts are in the captures commit (step-ab copies after the change commit). |
| 16 | ec57c4ee, 5766a137, 84106f2b | reactivity-live-frame-sets-aside-batch (the follow-up to c32c4b2e) + Aperture/Reel fullscreen order (correctness) | (a) `RenderLiveResizeFrame` sets aside the reactive batches open around it, flushes what they held and gives them back (internal `SetAsideBatches`/`RestoreBatches`, as `RunFrameCore`'s nested pass does): a resize made inside posted work or a handler builds in the size message's frame. Real window (NativeAOT Junction, jrepo's 8th commit in the Commit view, wrap on, the 4K at 150 %, 120 Hz, GL, 2 launches a side alternated in one 80 s hold, idle gate passed, 0 foreground takes): posted 6 px steps 1.90 → 0.95 frames a step (40 → 20), held writes 20/20 → 0/20, bytes inside `SetWindowPos` 9.3 → 162.7 KB, UI KB a step 163.5 → 162.7; posted 160 px steps 14 → 7 frames (~1,274 KB both); timer steps (a real drag's context) 0.95 / 0.88 frames and 156–162 KB a step on both sides — a user's own drag unchanged. A build that throws inside the size message: that frame is not drawn, the resize stays pending, the batches are given back; SDL's event watcher logs and swallows it (before, a posted resize's throwing build reached `RunFrame` and `Run`; now it is logged as any live-frame build is), and the next size event draws the new size. (b) Aperture's `SetFullscreen` and Reel's `ToggleFullscreen` write the player's flag and rebuild before `_window.SetFullscreen`, then read `Window.IsFullscreen` back (a refused toggle showed the wrong glyph in Aperture before): the frame drawn inside the toggle shows "Exit full screen" (and back), Aperture builds once a key instead of twice, a toggle stays 2 frames; headless only (never toggled in a real window), through `HeadlessOptions.WindowsFrame`'s `SetFullscreen` now drawing its first frame inside the call. step-ab (f8db7335) `-Pairs 2`: JIT and AOT 0 slower, 0 faster both pairs (A/A JIT keyed rows −11 %, two a11y allocKB rows); the posted-burst rows unchanged. Every new test fails under a reverse edit (the no-batch guard passes either way by design); the gated UiTests guard (expert 8's (g)) draws 21 frames for 21 posted steps (reverse edit 21 + 21). Windows Core 5,118, Headless 1,738, Junction 1,161, Aperture 369 (+21), Reel 53, Meridian 680 (+4), UiTests LiveResizeFrame/SdlLoopPass/FramePacing 16/16 (the fullscreen UiTests not run); Fedora the slnf 22/22, Junction 1,161, Aperture 369, Reel 53 (private D-Bus). REACTIVITY.md, THREADING.md, HEADLESS.md (write what the new frame shows before a call that resizes the window, then read `IsFullscreen`). |
| 16 | 41ecec65, 9c21b2b6 | a11y-items-on-demand-sooner (question 64: (a), Windows only) | Each bridge reports its eager-items cutoff through an internal `IEagerItemsLimit` (UIA 200, AT-SPI 2,000, any other 2,000); the window reads it when a bridge is installed and builds, patches and refreshes by it; HeadlessUiaReader, UiaRig/AtSpiRig and 25 test wrappers pass on the wrapped bridge's number. Real window (NativeAOT, the desktop listener on, the 4K at 150 %; 5 holds, 24 launches, 0 foreground takes, 0 nvlddmkm; repeat launches identical), UI-thread KB: Junction start 2,212.3 → 1,835.7; Commit → History 600.0 → 270.2; a ref move with History 1,314.7 → 598.8 (−54.5 %; Name raises 1,002 → 3, provider calls 13,186 → 199; kept wrappers / natives 1,086 / 1,038 → 86 / 38); Vesper start 951.5 → 603.5, navigation between two 1,000-file folders 504.7 → 293.7 (the first 1,029.0 → 334.6; raises 1,003 → 3, calls 13,052 → 52), viewer open 1,664.1 → 1,041.4, close 794.7 → 718.3. Slower: a key move in a served list (fresh providers each tell) Vesper's 1,000 files 1.7 → 3.0 KB a key, Junction's History about the same (Waiting a11y-served-item-one-provider-per-tell). A rename of the focused served item is told as one Name change on that item (a real UIA client heard it once), also at a 200 crossing; Vesper 150 ↔ 250 crossings build the tree whole once, focus kept by id with no focus event, 382.7 / 371.2 / 358.2 / 326.6 KB against 438.2 / 412.6 / 335.2 / 362.5, raises 153–154 → 4, calls 2,028–9,695 → 78–106; an open inline editor keeps a 300-item list eager; a 201-entry menu is served. Also fixed: `GetItemLabel` on item rows sized a kept-label array to the whole list on the UI thread (24 KB at every return to History, 480 KB at 20,000 items): internal `IAccessibleItemSource.PeekItemLabel`. Guard (expert 8's (e)): suite row `a11y/insert above 1,000 item rows, UIA` 0 Name raises a step, 0.13 ms, 72 KB (with UIA at 2,000: 1,000 raises, 8.7 ms, 158 KB); the AT-SPI row at 1,999 items 1,999 name signals. Event streams 114/117 identical (the 3 are UIA lists over 200); every AT-SPI stream and list under 200 equal. step-ab (0e8bb81a) `-Pairs 2`: JIT 0 slower both pairs, `selection walk 1,000 rows, UIA` −12…−21 %, 252 → 90.3 KB, A/A 2 faster; AOT the walk −14/−17 %, 251.8 → 90.2 KB, pair 2 media scrubber hover +10 % (one pair), A/A 0/0. Negative controls fail as expected; Windows Core 5,119, Headless 1,746, Linux bridge 104, Fluent 38, Vesper 951, Meridian 680, Junction 1,161, UiTests ReaderFocus 3, VirtualListSample 6, AccessibilitySample 8; Fedora the slnf 22/22, Junction 1,161, Vesper 933 (private D-Bus). ACCESSIBILITY-PLAN.md, DECISIONS.md, ELEMENTS.md. Rule broken: a 5th desktop hold (6.6 s) to re-run a fixed UiTest whose first version compared against the desktop's real focused element; that log held a browser tab's title and was deleted. |
| 16 | 0d96e805, b02b46d6, 32fffce7, 3211f964 | a11y-gate-on-client (question 50: build) + question 66 (a) | (a) A window builds its UIA tree and raises events only once a UIA client asks for its root (WM_GETOBJECT; built at once inside the ask, or by the next frame when asked inside a frame); each window its own gate, never closed. (b) `AccessibilityClients` ("a client listens": WebView2's own tree, a terminal's reader timers, text versions) also waits for that first ask on Windows; Linux unchanged; no public API. Real window (NativeAOT, the 4K at 150 %, idle-gated, alternated; 3 holds, 25 launches, 0 foreground takes, 0 nvlddmkm), db5dc041 → gate with no client asking (a11y bytes, raises, calls, kept wrappers and root asks all 0), UI KB: Junction start 1,835.9 → 1,823.7 (17 raises / 216 calls → 0), Commit → History 270.2 → 258.0 (417 calls → 0), a ref move 593.6 / 598.8 → 587.6 / 592.9, palette view switch ~1,268 → 1,202.3, 10 History Downs ~256 → ~250; Vesper start in 1,000 files 603.5 → 578.8, navigation 293.7 → 293.5, 10 Downs 29.7 → 17.1 (−42 %), 150 ↔ 250 switches −10…−18 %, viewer open 1,040.9 → ~1,016, close 717.8 → ~656.5; Meridian's preview open ~198.5 → 151.9 (13 raises / 144 calls → 0). After step 3 the gate's own share is small on Junction (−0.7 % start, −1 % a ref move) and large on key moves. An out-of-process client asking at ~5 s: the tree built once (203 nodes, 84.8 KB inside the ask), the next 5 Downs told; with Meridian's preview open the ask built the tree (45.1 KB) and the web page read the same (22 elements, heading found), no reload. A focus-only listener receives the same as before: UI Automation asks our root for it (3 asks when it registers with the window there, 4 when the window appears after), 11/11 and 6/6 focus events both sides; only a client that never makes UIA ask (this desk's own listener) gets nothing; Magnifier, the touch keyboard and Voice Access take the same path (reasoned, not run). 32fffce7: (b) first made the terminal subscribe to the static `ActiveChanged` event, and terminals left in disposed windows (still `IsHosted`) read their screen on every `Force` flip: 8 rich-text and text-area caret/typing rows +32 KB/op, +11…22 % in step-ab; the terminal now takes the announce place from its tree node, rows back. step-ab (db5dc041 → 32fffce7) `-Pairs 2`: JIT 0 slower (pair 2 the rich-text-in-list row 2.4 → 3.3 KB, as in step 3), A/A 0/0; AOT 0 slower, 1 faster (−12 %), A/A 0/0; the suite's UIA rows still measure the bridge (UiaRig answers IsActive). Windows Core 5,119, Headless 1,751, Linux bridge 104, Terminal 176, WebView 77, WebView.Engines 140, Fluent 38, Vesper 951, Meridian 680, Junction 1,161, UiTests 32 (+10 foreground-only skipped); Fedora the slnf 22/22. ReaderFocusTests' handlers drop other processes' elements before reading. ACCESSIBILITY-PLAN.md ("The gate on Windows"), DECISIONS.md. **The user's live Narrator check of Meridian's preview (question 66) is open:** `r16b4\live\` (start-meridian.cmd, CHECKLIST.md); the release waits for it. |
| 16 | 96d00ab3, 05673aba | painting-dialog-fade-layer / gl-dialog-fade-window-layer (question 67: (b), approved after the images) | `Dialog.Show` fades the scrim's colour from alpha 0 and the panel's own opacity (to the app's own value, through `Element.Restyle`: set directly, a hover starting mid-fade left the panel stuck at 0.504), not the whole window's scrim by `Opacity`; same 0.12 s timing and easing; the palette, Confirm, Alert and WebView's script dialogs all go through it. Real window (NativeAOT, the change on and off in this tree, first Ctrl+P, 2 launches a side alternated; the 4K at 150 %, 120 Hz; 4 holds, 0 foreground takes, 0 nvlddmkm): Junction GL 2160×1350 DXGI local +18.0 → +8.0 MB, private +41.7 / 41.8 → +14.0 / 14.0 MB, scene end local 64.6 → 54.6 MB (−15 %), private 170.3 → 142.4 (−16 %); Meridian GL +18.0 → +4.0, +39.3 → +6.3, local 63.8 → 49.8 (−22 %), private −23 %; Junction Vulkan +19.0 → +5.0, +20.8 → +7.1, local 88.0 → 74.0 (−16 %); maximized 3800×2100 +32.0 → +8.0, +64.6 → +1.6, local 139.5 → 115.5, private 247.2 → 184.2 (−25 %); a second open +0 either side; working set equal. Pixels (headless, Junction's palette at 2160×1350): only the panel's area, frame 2 (α 0.5) max 35 of 255 (dark 105,056 px > 8, light the whole panel), frame 5 max 22–24, frame 8 max 8–9; the window before and the last frame byte-identical; goldens unchanged. Guard (expert 8's (i)): gated UiTests `DialogFadeGpuMemoryTests` (a `--dialog-fade` mode of `HultUI.UiTests.VulkanProbe`, own process): a 900×690 panel in a 2160×1350 GL window +18.0 MB fails under a reverse edit, +8.0 passes (limit: the panel's layer twice plus 1 MB); Skia's cache 33.4 → 19.4 MB. step-ab (b1f65222) `-Pairs 2`: JIT pair 1 RowHeights 20,000 rebuild +48 / +41 % (A/A +43 / +37 %: noise), pair 2 0; AOT 0 both pairs; A/A 0 both runtimes; `overlay/dialog open` 12.5 → 12.9 KB JIT, 12.3 → 12.7 AOT (the second tween's closures, once an open), time +2.7 % / +4.8 %. `DialogFadeTests` (4) fail under the old fade; Windows Core 5,119, Headless 1,755, Skia 457, Junction 1,161, Vesper 951 (+4), Meridian 680 (+4), Aperture 369 (+21), Fluent 38, the UiTests guard; Fedora the slnf 22/22. ELEMENTS.md (don't fade a window-sized element by its `Opacity`). Still through a window-sized layer: a panel holding a control with unknown paint bounds (checkbox, toggle, dropdown, slider): no current `Dialog.Show` panel has one. |
| 16 | 89732bec, ca94d190 | painting-gradient-shader-by-size (question 68 on its one-level pixels) | A gradient's shader is kept by kind, angle, stops and snapped size, made over the box where that size was first drawn; a box drawn elsewhere draws with the canvas moved by the difference (Save, Translate, Restore), a box that has not moved draws with no move (the survey's always-move prototype rounded 4,254 pixels of Aperture's hero one level off at 1.5). Headless (JIT, 20 keys and five window sizes): Junction History key misses 10 → 0 at 1.0, 10 → 2 at 1.5, render bytes 0.17 → 0.00 / 0.02 KB a frame, shaders over the run 16 → 1 / 3; Meridian 10 → 2 / 4; Aperture 10 → 10 (every size new). Real window (NativeAOT, 1 hold, 4 launches alternated, the 4K at 150 %, 0 foreground takes): History 20 Downs misses 20/20 → 3/20, render-phase bytes 0.36 → 0.05 KB a frame; 17 of 20 key frames 0 B (the 3 left: snapped heights a different float at 1.5, each kept after its first draw). Pixels: Junction History 35/35 and Aperture 26/26 frames at 1.0 and 1.5, Meridian 12/12 at 1.5 byte-identical (fractional window sizes included); Meridian at 1.0 4 of 12 frames differ — at two window sizes a welcome rule kept its size and moved, 25 and 14 pixels of that 1 px rule 1 level off (max 1/255); goldens pass (none draws a gradient). step-ab (07e973d7) `-Pairs 2`: JIT pair 1 RowHeights 20,000 rebuild +26…37 % (A/A +28 / +37 %: noise), pair 2 clean; AOT 0/0 both pairs, A/A 0/0; no allocKB moved. PaintEffectCacheTests +3 tests (5 cases; a moving box one shader at 0 B a frame, the old code 208 B; a moved gradient within 1 level of an in-place shader; an unmoved one byte-identical, the always-move mutant fails); Windows Skia 462, Headless 1,755, Junction 1,161, Aperture 369 (+21), Meridian 680 (+4); Fedora Skia 462, Headless 1,534. |
| 16 | 3d728568, 13a1b363, 6c08a046 | layout-wrapped-first-open-in-frame | A wrapped body measures at its width in its own `OnLayoutChanged` (7c0a3b79) once it has a box, not after the frame; Vesper's after-frame width check is gone (the body's hook sees every width change of these full-window bodies), Junction keeps one only for the pan (`FollowDiffWidth`: an unwrapped diff's scrollbar appearing needs a build). The survey's Vesper diagnosis was half wrong: the reader's body spans the window, so each line was already measured once at the right width (TODO.md 252, zh 389, archive 1,521); the 1206 came from the after-frame check firing on the Loading view and rebuilding the window. Headless AOT, 2–4 alternated idle-gated pairs, first open before → after: Vesper reader 14 → 11 builds, 3–4 → 3 frames, TODO.md 2,272.0 → 2,222.6 KB (−2 %; the expected −48 % does not exist); compare view 13 → 10 builds, 4,031.5 → 3,965.0 KB; an open after a resize line measures 502 → 251 (TODO.md), archive 3,040 → 1,520, compare 3,348 → 1,756 (KB within 15, cache hits); real window 14 → 11 builds, 2,269.5 → 2,220–2,232 KB. Junction Commit view first open 7 → 3 builds, 3 → 2 frames, line measures 401 → 201 (1,318 then 808 px → 808 only), frames at wrong heights 1 → 0, UI 2,206–2,215 → 1,812–1,828 KB (−18 %; real window 2,220.6 → 1,798.3); Changes first open 2,323.5 → 2,136.5 KB, 43.5–44.6 → 32.6–33.0 Mcycles; opens after a resize Commit 651 → 452 KB, 17 → 10 Mcycles, Changes 1,446 → 1,208 KB, 27 → 16 Mcycles (real window the same); later opens, resize steps and History keys equal. **Slower: the session's first Commit open in time**, headless 40.3–58.0 → 43.9–46.9 Mcycles (min +9 %), real window 38.5 / 39.4 → 41.4 / 44.1: the rows are now drawn before the markdown colours land, and the colours' frame makes them again (~21 Mcycles; colours held: 34.1–35.3 → 23.9–24.9, −30 %; opened after Changes 26.9–27.6 → 20.3) — kept as a once-a-session trade for −18 % bytes and no wrong-height frame (Waiting junction-colour-landing-remakes-rows). Pixels: frame hashes equal the base's in every series; every settled frame equals a full build's; where the base drew rows at wrong heights the first frame now equals the settled one; find reveals give the same offset and hit. step-ab (4eec21e9) `-Pairs 2`: JIT pair 1 virtual/rebuild beside a 20,000-row list +33…47 % (A/A the same rows: noise), pair 2 0/0; AOT 0/0 both pairs, A/A 0/0. New tests fail on the base; Windows Vesper 953 (+4), Junction 1,162, Headless 1,755; Fedora the slnf (Vesper 935 +20), Junction 1,162 (private D-Bus). Real window 2 holds, 0 foreground takes, resize steps from a Win32 timer callback. LAYOUT.md (measure at a width in `OnLayoutChanged`; a list replacing one elsewhere has no box yet). |
| 16 | 49b0da84, fda77e01 | reactivity-junction-wrap-resize-in-place | A width step that crosses no breakpoint (a window resize, a drag step of the file list's splitter) measures Junction's wrapped diff rows at the width it leads to and fits them in place before layout, with no build (`FitWrappedDiffInPlace`, the same fit as step 7's hook; wrap and review cards, unified and split, Commit and Changes); step 7's `OnDiffListLaidOut` stays as the check; a breakpoint, a scale change and the splitter's press, release and keys still build. Headless AOT (2 alternated idle-gated pairs, each series on an empty gen 0), UI KB a step (Mcycles): Commit 6 px 104.8 → 47.4 (8.79 → 8.30) and 95.7 → 38.2, split 99.5 → 42.1 and 92.7 → 35.2, 160 px in a band 121.9 → 63.3, splitter 95.8 → 38.3 and 86.9 → 29.4 (5.93 → 5.55); Changes 6 px 163.5 → 125.6 (9.97 → 9.90) and 152.6 → 114.7, split 193.4 → 155.5, 160 px 228.2 → 188.3, split 160 px 387.3 → 347.4 (−10.3 %), splitter 154.7 → 116.8; view builds 1 → 0 a step, 1 frame either way; Commit time −3…−11 %, Changes ±1.3 %. Real window (NativeAOT, the 1440p only at 100 %, 200 Hz — the 4K was not attached; 2 holds, sides swapped, 0 foreground takes; steps from a Win32 timer callback): Commit 105.3 → 47.9 KB, Changes 164.0 → 126.2, builds 1 → 0, 9.82 → 9.23 and 10.91 → 10.46 Mcycles. The survey's −80…−90 % is not reached: the heights pass itself (200 or 400 rows through `TextMeasure.MeasureWrapped`) is what is left (a lead: keep each row's same-height width range). Pixels: every settled step byte-identical to the base's at 1.0 and 1.5, unified and split, across the breakpoint, every step equal to a full build's, 0 stale-height frames; review open (lines wrapped and unwrapped) 48 of 48 steps equal a forced full build. step-ab (d2ddfacd) `-Pairs 2`: JIT pair 1 virtual/rebuild beside 20,000 rows +28…36 % (A/A +29…41 %: noise), pair 2 0/0, the two rich-text allocKB rows 2.4 → 3.3 / 2 → 2.9 (flip both ways in earlier steps); AOT 0/0 both pairs, A/A 0/0. `DiffFrameCountTests` (resize, file-list drag) and `ReviewTests.AResizeMeasuresTheCardsAgain` fail on the base; Windows Junction 1,162, Headless 1,755; Fedora Junction 1,162 (private D-Bus). LAYOUT.md. |
| 16 | f76cb440, 80e94eeb, 40ba50e2 | apps-junction-big-file-revisit | (a) Syntax: a cancelled `TokenizeDiff` resumes when called again with the same string instances, lines shown, clock and grammar (the progress in a ConditionalWeakTable keyed by the text; `SetTheme` clears it; no API). (b) Junction keeps the two texts of a file whose tokenize a key cancelled (LRU, bound 16 MB: five bigrepo pairs, or one pair at Syntax's 2,000,000-character limit); files that finish before the next key keep nothing. Headless JIT, bigrepo, 400 ms a key, all threads precise, 2 alternated idle-gated pairs: first visits 32.05 / 31.57 → 31.99 / 31.89 MB a key, GCs 27/9/5 and 26/7/5 → 24/4/2 and 22/3/1; revisits 32.39 / 31.95 → 31.13 / 31.20 (−3…−4 %), LOH 2.60 → 1.73 (4 of 12 revisits find their texts), GCs 27/16/6 and 27/6/6 → 21/1/0 and 22/2/1; 12 keys over a pair of unvisited big files 33.6 → 31.0 MB a key (−8 %), LOH 2.61 → 0.43, gen-2 4–6 → 0, colours land after the stop 2,361 / 2,479 → 1,095 / 1,093 ms (−55 %); a stop on a file left part-way once 2,687 / 2,710 → 2,411 / 2,425 ms (−10 %). **Retained heap after a full GC 9.7 → 21.0 MB** (10.9 MB kept texts, within the 16 MB bound) — kept as a bounded trade for the colours and the gen-2 collections. The survey's −29 MB a revisit is not reached: at a steady 400 ms the worker tokenizes until the next key either way; a revisit saves the two re-reads (LOH, git processes) and the gen-2s. Real window (NativeAOT, the 1440p only, 100 %, 200 Hz; 2 holds, sides swapped; a third 0.1 s hold opened no window): first visits 33.2 → 32.0 / 32.2 MB a key, GCs 25/6/3 → 22/3/1; revisits 33.1 → 30.6 (−8 %), GCs 27/7/4 → 21/1/0 and 22/0/0. step-ab (ff990dfb) `-Pairs 2`: JIT pair 1 one faster a11y row (A/A the same: noise) and the two rich-text allocKB rows, pair 2 0/0, A/A 0 slower; AOT 0/0 both pairs, A/A text/multi-font ellipsis +13 % (noise). Expert 8's suite row (h) not built (the suite runs no Junction or `TokenizeDiff`); HighlightResumeTests' counts guard it. TokenizeDiffResumeTests (16), HighlightResumeTests (3) fail under reverse edits; Windows Junction 1,165, Syntax 137, Headless 1,755; Fedora Junction 1,165 (private D-Bus), Syntax 137 in 3 of 4 runs (the known wall-clock `ACancelledTokenizeStopsAndTheNextOneRuns` once right after a build; 8/8 alone, base 3/3). |
| 16 | 5da967d2 (docs only) | gl-tween-burst-nonlocal-growth (measured: NVIDIA's pool, levels off; no code change) | 50 scrollbar hover visits in a real window (NativeAOT at HEAD, hover driven in-process, frames a 5-visit block steady at 138–140; the 1440p only, 100 %, 200 Hz, HDR on; 4 holds, 0 foreground takes, 0 nvlddmkm), DXGI nonlocal MB at rest → after 5 / 10 / 20 / 50 visits / after a 30 s rest (working set): Vesper GL 11.6 → 23.6 at every mark (86.1 → 100.2 / 99.4 / 99.7 / 100.4 / 100.4; with `HULTUI_A11Y=0` the same); Junction GL (History) 15.1 → 23.1 at every mark (103.9 → 111.1…112.3); Vesper Vulkan 17.3 → 17.3 / 17.3 / 19.8 / 19.8 / 19.8; Junction Vulkan 18.3 → 18.3 / 20.3 / 20.8 / 20.8 / 20.8 (the rest's +4.5 MB working set is the first gen-0 GC's commit). Other bursts level too: Junction GL palette open/close 15.1 → 23.1 / 25.1 / 25.1…; a toolbar button hover reaches the same 23.1 later; 50 bar visits then 50 palette cycles end at 25.1 (one pool, not one per kind of burst). On GL all of it is resident write-combined private memory (Vesper 3.3 → 15.8 MB at visit 5); Skia's cache flat through all 50 (33 resources / 8.2 MB Vesper, 53 / 9.3 MB Junction); `glFinish` after every frame fills the same pool (+8–11 MB) and a one-frame fence +12, so it is not frames queued ahead of the GPU; the slow +1 MB from visit 10 to 50 is the managed heap's first allocation budget. DECISIONS.md entry with the table and the controls. Not tried: swap interval 1 during tweens. |

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

No question is open from rounds 1–16. Their answered questions are in the archive
([round 16's 66, 67 and 68, with round 15's 64](17-performance-team-ledger-archive.md#needs-the-user-round-16),
[round 15's 62, 63 and 65](17-performance-team-ledger-archive.md#needs-the-user-round-15), [round 14](17-performance-team-ledger-archive.md#needs-the-user-round-14),
[round 13](17-performance-team-ledger-archive.md#needs-the-user-round-13), [round 12](17-performance-team-ledger-archive.md#needs-the-user-round-12),
[round 11](17-performance-team-ledger-archive.md#needs-the-user-round-11),
[round 10, with round 9's 26 and the earlier 10](17-performance-team-ledger-archive.md#needs-the-user-round-10),
[round 9](17-performance-team-ledger-archive.md#needs-the-user-round-9), [round 8](17-performance-team-ledger-archive.md#needs-the-user-round-8),
[round 7](17-performance-team-ledger-archive.md#needs-the-user-round-7), [round 6](17-performance-team-ledger-archive.md#needs-the-user-round-6)). Search it by candidate id
before asking anything an earlier round asked. Recommendations in brackets. Question 50 (a11y-gate-on-client)
was answered "not yet", then **build** (2026-11-16; built in round 16); 51 (after-layout-callback)
"only if another app needs it" (closed by 7c0a3b79); 57 (text-open-measures-every-line, full) "not now" (Waiting).

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

From 2026-11-18 (round 16's survey); earlier entries are in the archive ([to 2026-10-25](17-performance-team-ledger-archive.md#log-2026-10-07-to-2026-10-25),
[2026-10-26 to 2026-10-27](17-performance-team-ledger-archive.md#log-2026-10-26-to-2026-10-27),
[2026-10-27 to 2026-11-02](17-performance-team-ledger-archive.md#log-2026-10-27-to-2026-11-02),
[2026-11-02 to 2026-11-03](17-performance-team-ledger-archive.md#log-2026-11-02-to-2026-11-03),
[2026-11-03 to 2026-11-08](17-performance-team-ledger-archive.md#log-2026-11-03-to-2026-11-08),
[2026-11-08 to 2026-11-09](17-performance-team-ledger-archive.md#log-2026-11-08-to-2026-11-09),
[2026-11-09 to 2026-11-11](17-performance-team-ledger-archive.md#log-2026-11-09-to-2026-11-11),
[2026-11-11 to 2026-11-12](17-performance-team-ledger-archive.md#log-2026-11-11-to-2026-11-12),
[2026-11-12, round 12](17-performance-team-ledger-archive.md#log-2026-11-12-round-12),
[2026-11-12 to 2026-11-13, round 13](17-performance-team-ledger-archive.md#log-2026-11-12-to-2026-11-13-round-13),
[2026-11-13 to 2026-11-14, round 14](17-performance-team-ledger-archive.md#log-2026-11-13-to-2026-11-14-round-14),
[2026-11-14 to 2026-11-16, round 15 and the screen-reader fix](17-performance-team-ledger-archive.md#log-2026-11-14-to-2026-11-16-round-15-and-the-screen-reader-fix)).

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
- 2026-11-18: round 16 step 5 at its stop point: option (b) built (Dialog.cs `FadeIn`: the scrim's colour from alpha 0,
  the panel's own opacity through `Element.Restyle`), uncommitted; headless Junction palette at 2160×1350: only the panel
  area changes, frame 2 max 35 of 255 (dark 105,056 px > 8, light the whole panel), frame 5 max 22–24, frame 8 max 8–9,
  the last frame identical; images sent to the user (light frames 2 and 5, dark frame 2). Waiting for the user.
- 2026-11-18: the user looked at the fade images and approved (b): "Commit it, I think it looks fine and it's only a
  few ms." Step 5's agent resumed to finish its proof and commit.
- 2026-11-18: round 16 step 5 landed (96d00ab3; captures 05673aba): a dialog fades its scrim's colour and its panel, not
  a window-sized layer (first open GPU memory +18 → +8 MB on GL, +32 → +8 maximized on the 4K; private −16…−25 % at the
  scene's end); a gated UiTests guard holds it. The user will do the Narrator check of Meridian's preview (question 66)
  after step 10, so it doesn't disturb a performance run (2026-11-18, the user); the full suite and the release follow
  it. For PERFORMANCE.md: never fade a window-sized element by its `Opacity` (a window-sized layer whose GPU texture Skia
  keeps for the window's life). Next: step 6.
- 2026-11-18: round 16 step 6 landed (89732bec; captures ca94d190): a gradient's shader is kept by its size, so
  History's moving "Details →" gradient makes 3 shaders in 20 keys instead of 20 (17 of 20 key frames 0 B in the render
  phase). One pixel change: Meridian's 1 px welcome rule, moved at two window sizes at scale 1.0, has 14–25 pixels one
  level (1/255) off; asked as question 68 (keep, or revert 89732bec; recommendation keep). Next: step 7.
- 2026-11-18: round 16 step 7 landed (3d728568, 13a1b363; captures 6c08a046): wrapped bodies measure at their width in
  `OnLayoutChanged`, not after the frame. Junction's first wrapped Commit open 7 → 3 builds, −18 % UI bytes, no
  wrong-height frame; opens after a resize −31…−41 % time. The survey's Vesper −48 % did not exist (only −2 %; the real
  waste was an open after a resize, half the line measures). Kept with one trade, the orchestrator's call: the session's
  first Commit open +9 % CPU time (its markdown colours now land after the rows were drawn and remake them, ~21 Mcycles)
  → Waiting junction-colour-landing-remakes-rows. For PERFORMANCE.md: measure at a width in `OnLayoutChanged` once the
  element has a box. Next: step 8.
- 2026-11-18: round 16 step 8 landed (49b0da84; captures fda77e01): Junction's wrapped diff fits its rows to a width
  step in place with no build (Commit −54…−66 % UI bytes a step, Changes −10…−25 %; the survey's −80…−90 % not reached:
  the heights pass is what is left). Only the 1440p was attached for the real-window run. Next: step 9.
- 2026-11-18: round 16 step 9 landed (f76cb440, 80e94eeb; captures 40ba50e2): a cancelled big-file tokenize resumes, and
  Junction keeps the cancelled files' texts (≤ 16 MB). Colours land 55 % sooner on a stop over an unvisited big file,
  gen-2 GCs while browsing big files 4–6 → 0, all-thread bytes only −3…−8 % (the survey's −29 MB a revisit did not hold:
  the worker tokenizes until the next key either way); kept with a bounded retained cost (+11 MB after browsing big files),
  the orchestrator's call. Lead for the next survey: skip the old version's walk through stretches both versions share once
  the two states match (~half of each big-file pass). Next: step 10.
- 2026-11-18: round 16 step 10 measured, no code change (5da967d2, DECISIONS.md): a GL window's tween bursts fill an
  NVIDIA pool of write-combined memory that levels off after the first few bursts (Vesper nonlocal 11.6 → 23.6 MB, then
  flat over 50 visits and a 30 s rest; Junction 15.1 → 23.1, its palette's ceiling 25.1); Skia's cache flat; Vulkan +2.5
  MB then level. Round 16's build order is done. Next: the user's Narrator check of Meridian's preview (question 66;
  `r16b4\live\CHECKLIST.md`), then the full suite (Windows, Fedora with the four app projects each in a private D-Bus
  session, docfx), then release.
- 2026-11-19: the user's live Narrator check of Meridian's preview (question 66, step 4's `r16b4\live\` build) passed:
  "It works as expected in the checklist." Main had moved by 7 commits (VirtualList cursor apart from the selection,
  lists in columns, the app id from the application, KeyChord Super, a million-row PerfSuite row, Roslyn's version, a
  TODO); merged into the branch as 666e6486, one conflict (VirtualListAccessibilityTests.cs: both sides added tests,
  both kept), the merged tree built.
- 2026-11-19: round 16's full suite at 666e6486, 00:20–00:37: Windows `HultUI.slnx` 30 projects, 0 failures (Junction
  1,166 in 9 m 9 s); docfx 0 warnings; Fedora the slnf 22 projects and Junction 1,166, Meridian 680, Aperture 369, Reel 53
  each in a private D-Bus session, but **VesperCommander 1 failure**: step 7's `WrapResizeFrameTests` (3 of 3 reruns of
  the class failed one or both tests). Cause (expert 1): a timing race in the test — on Fedora the 22 KB read lands
  before the first frame drains the UI queue, so that frame draws the text and "Loading…" is never drawn (1 frame, not
  2); the one-frame open is correct (each line measured once at the laid-out width, nothing built after the frame); it
  failed 1 of 2 at 3d728568 too (step 7's Fedora pass ran the whole slnf at once, which slowed the read). Fixed in
  89506c22: an internal `ViewerLoadGate` (null in shipped code, modelled on `DiffCompareGate`) and `HoldViewerLoad()`;
  the tests hold the read until the waiting view is drawn and also assert that the first frame shows it; the class 10/10
  on Windows, 5/5 on 2 cores, 10/10 on Fedora, VesperCommander.Tests whole 953 (+4) / 935 (+20); both tests still fail
  under a reverse edit of step 7; the shipped path's `Task.Run` of an async lambda +80 B an open. Main moved again by
  one Junction change (791d1a44: a staging move is settled only by its own command); merged as 5e180d17 (clean);
  Junction.Tests re-run on Windows and Fedora on the merge: Windows 1,167 and Fedora 1,167 (private D-Bus), 0 failures. Phase → release.
- 2026-11-19: round 16 RELEASED. Merged into main b131ae85 and pushed (origin/main 227e9462 in the branch, tree identical
  to the tested branch; the primary checkout fast-forwarded behind the guard). CI run 44426151547 green on the first
  attempt (test, test-linux, docs, meridian-keyring on Ubuntu and macOS); "Push on main" green. The user asked for app
  releases: junction-v1.29.0, vesper-v1.29.0, meridian-v1.12.0, aperture-v1.27.0, reel-v1.13.0 on b131ae85 (99 commits
  since the 1.28.0 set: SkiaSharp 4, SDL 3.5.0, Jellyfin.Sdk 2026.10.12, round 16), pushed 3 + 2, all five installer runs
  started. Round → 17, Phase survey, in a fresh context.
- 2026-11-19: all five app releases published: Vesper, Meridian, Aperture and Reel green on the first attempt; Junction
  green on attempt 2 (attempt 1 red on Vesper's `KeptRowTests.AFolderMeasuredWithSpace_ShowsItsTotal`, the known flake
  plan 18 saw: a 10 s wait for a background folder size under load; it passed in Vesper's own run).
- 2026-11-19: round 17 survey started in a fresh context (session 35903ed7; usage 65 % weekly, the week resets
  2026-11-22 03:00 UTC; the 5-hour window 6 %). Main unmoved since the release (b131ae85). Moved to the archive by a
  checked script (`archive_r17.py`; every moved line found there, nothing lost): round 15's 12 Landed rows, round 16's
  build order with its closed-from-Waiting note, questions 64, 66, 67 and 68, and round 15's 151 log lines (2026-11-14,
  its survey, to the screen-reader fix on 2026-11-16). Attached: both monitors (the 4K at 150 % primary reading 119 Hz,
  the 1440p to its left). Steam is running. The eight experts survey round 16's build, 32c85e81..b131ae85, and main's
  8 commits outside plan 17, in their own worktrees on the archive commit, in real windows too.
