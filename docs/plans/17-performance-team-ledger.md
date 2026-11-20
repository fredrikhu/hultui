# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 18
- **Phase:** survey — round 18, to start in a fresh context, **after the weekly usage reset (2026-11-22 03:00 UTC)**:
  round 17 ended at 85 % of the week, and a survey and build cost ~25 points. Round 17 RELEASED 2026-11-20: main
  e799c23b (merge, tree identical to the tested branch but the ledger; origin/main 3c6d6a5b was in the branch), CI run
  44653899561 green on the first attempt (test, test-linux, docs, both meridian-keyring jobs) and "Push on main"
  44653898406 green. Round 17's files: the survey brief `brief-r17-survey.md`, build briefs `brief-r17b<N>.md` and
  `brief-r17fix.md`, agent ids `r17-agents.txt`, probes `r17-<area>\` and `r17b<N>\`, `archive_r17.py`, `merge_r17.py`,
  `land_r17.py`, `edits_log.py`, `fullsuite-r17.sh` with `fedora-r17.sh` (the model for round 18's full suite), all in
  session 35903ed7's scratchpad `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\35903ed7-ff10-416d-84e9-b6c09675ab6b\scratchpad\`.
  For round 18's start: move round 16's Landed rows, round 17's build order (first copy its untaken steps 9 and 10 into
  Waiting, with their evidence) and the answered questions 69–72 (72b stays while open) to the archive, as
  `archive_r17.py` did; the survey reviews round 17's build, `git log 0df5e3ea..e799c23b` (the archive commit to the
  round 17 release), with main's 18 commits outside plan 17 that 45cbde7b merged (plan 19 items 1–4: the digit table
  with punctuation 781a857f, the cold-label rows ba15d841, right-to-left runs kept whole 78a69ee5, shaper call counts
  52acb2f2; Junction's Changes load under index moves 3490ba67; WebView middle-click autoscroll e66ead9f), and anything
  main gains after. The Jellyfin account to browse as is in memory `aperture-jellyfin` (never in committed docs). Read
  the desk at the start and before every hold (the 4K came and went during round 17). Open for the user: question 72b
  (step 11 waits). To resume in a fresh context, run in this worktree: `/loop Run the next step of the performance team:
  follow docs/plans/17-performance-team.md, with state in docs/plans/17-performance-team-ledger.md. Read both once per
  session (afterwards only the ledger's Phase and the part the step needs), do the step the ledger's Phase calls for,
  record it in the ledger, and commit.` Before resuming, `git fetch` and merge main if it moved; build steps use
  `scripts/perf/step-ab.ps1`. Older rounds are in [the archive](17-performance-team-ledger-archive.md) (search it, don't
  read it whole). History note: rewritten 2026-10-25 (every commit after 3353ce47 has a new ID; a pre-push hook refuses
  pushes built on the old history).
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
| 1. Layout | 17 | no | b8a3dcf5: the suite row `virtual/columns cursor walk 100k item rows` measures nothing (its list is never focused) and makes every suite run that includes it exit 1, which step-ab refuses → step 1 (a); 38ec9773, b8a3dcf5, 266d6aee: no O(n) work a step (1M rows cost what 100k do); round 16's wrapped fits hold. Real window (1 hold): an ancestor measures a VirtualList through its rows, each visible row laid out at 3 widths it is never drawn at → step 1 (b) |
| 2. SkiaSharp: painting | 17 | no | none (96d00ab3: every app's palette draws 14 bounded layers an open, 0 unbounded; 89732bec: 2 misses in 20 History keys, 0.02 KB a frame; main's 8 change no paint code). Real window (1 hold, 6 launches): the video player's control bar fades through a window-sized layer Skia keeps (Reel +12 / +32 MB DXGI local) → step 6, question 69 |
| 3. GL and Vulkan | 17 | no | none (the dialog fade's layer is the panel's size in every app, GL and Vulkan; 5766a137: one swapchain rebuild a toggle; 23b552d8 has no caller). Real windows (4 holds, 28 launches): the same player-bar layer with real mpv (+12 MB) → step 6; Vulkan's 3 swapchain images (~19.5 B a pixel against GL's 13) → step 10; 5da967d2's swap-interval lever changes nothing (Examined) |
| 4. .NET runtime | 17 | no | 80e94eeb: Changes-mode kept texts are unreachable after any working-tree event (the key carries the data generation) → step 2 (a); main's columns row measures nothing (as expert 1). Element sizes unchanged; no leaks in open/close cycles of palettes, viewers, views and repos. Real window (1 hold): TokenizeDiff walks the stretches both versions share twice (−45 % lines with a skip) → step 3 |
| 5. Text pipeline | 17 | no | none (40cadb57: 0 of 5,038,170 layouts differ; every text counter equals round 16's). Junction's wrapped labels laid out at 4 widths a frame thrash the one-range wrap memo (text-wrap-ranges-per-width, Waiting until step 1 (b), the root-cause fix, is measured). Real window (1 hold): equal to headless AOT |
| 6. Reactivity and reconcile | 17 | no | none (ec57c4ee, 5766a137, 49b0da84, 80e94eeb, 791d1a44 read and counted; d3e40ecb's generator DLL byte-identical). Colours landing remake every row on every first file visit, not only the first Commit open → step 4, question 70; every view switch builds the whole toolbar → step 5. Real window (1 hold): equal to headless within 0.5 % |
| 7. Accessibility bridges | 17 | no | none (the gate closed equals `HULTUI_A11Y=0`, 0 B a frame; a first ask builds the visible tree only, 46–58 KB; 41ecec65, 38ec9773, b8a3dcf5, 266d6aee nothing O(n)). Correctness: a Linux reader that starts after the app hears no focus moves → step 7. Real window (2 holds, 13 launches): with a client 80 provider calls a key on the UI thread; two Waiting items closed |
| 8. App workloads | 17 | no | 80e94eeb bisected: +48 MB working set after a big-file walk, +38 MB after a full GC (committed 34.5 → 72.8 MB) → step 2 (b); main's 8: no real-app regression. Round 16 helped: Junction start −18 %, a ref move −57 %, Commit → History −54 %, a wrapped drag −87 %, the first palette open DXGI +18 → +8 MB, Vesper's reader open −28 %. Real windows (4 holds, 21 launches); Jellyfin needs a user name (question 71) |

## Queued candidates

Rounds 1–16's build orders all landed but round 6's step 8, round 10's step 14, round 11's step 1 (c), round 14's
step 8 and round 15's step 8 (a), measured and declined, round 11's steps 13 and 14, round 13's step 5 (b),
round 15's step 1 and round 16's step 10, measured with nothing to change, and round 13's step 6 (c), not attempted
(back to Waiting) (rounds 5–16 are in [the archive](17-performance-team-ledger-archive.md#round-16-build-order), with each survey's
closed-from-Waiting note). Round 17's build order comes from its survey.
A build step may bundle one expert's related small candidates as separate commits. Items marked **(user)** wait for a
decision in Needs the user; the rest of their step goes ahead.

### Round 17 build order

Ranked by expected effect in the real apps over cost (the brief's "Real apps first"); the harness fix first (every
step-ab needs it), then the review objection to 80e94eeb. Each step names the report lines its brief quotes (`r17-k` =
`17-reports/r17-k-<area>.md`) and measures its real-app case in a guarded real window (the lock
`Global\HultUI-r17-desktop`, the harnesses in session 35903ed7's scratchpad `r17-<area>\`) as well as by step-ab. Resize
steps come from a Win32 timer callback on the UI thread (`dragrelT`).

1. **perfsuite-columns-walk-focus + layout-virtuallist-measure-through-rows** (1; r17-1 REVIEW and Candidate 1, r17-4
   REVIEW, r17-5's lead) — separate commits. (a) First, a harness fix: `ColumnsWalk` (Scenarios.Virtual.cs:127, the
   class at :927) never focuses its list, so `virtual/columns cursor walk 100k item rows` prints "25/25 steps rendered
   nothing", and since b8a3dcf5 every suite run that includes it exits 1 (Program.cs:120), which `step-ab.ps1` (:212)
   refuses: `list.Focus()` after mount (focused: 26.8 cells, 262 KB a step). Check the row renders and a full suite run
   exits 0; (a) is the base for (b)'s step-ab. (b) An ancestor measuring a VirtualList measures it as a leaf, not
   through its realized rows (`LayoutEngine.MeasureSize` :619, an internal virtual on ScrollViewElement): today each
   visible row is laid out at up to three widths it is never drawn at (`RemeasureFlexedCrossSizes` :760 under 8 nested
   `MeasureFlexItems` levels; Junction's code labels at 642 drawn, then 1152, 908, 886). Headless 6 px steps, KB a
   step: Commit unwrapped 66.4–82.9 → 28.2–31.4, Changes unwrapped 70.1–74.8 → 29.5, Commit wrapped 94.0/97.5 →
   62.4/65.7, Changes wrapped 165.5/167.0 → 136.7/136.3, expert 5's a359e426 split 71.5–80.2 → 24.0 (re-breaks 3,905 →
   1,396 per 40 steps); real window (AOT, 150 %) Commit unwrapped 70.5/71.9 → 37.5/34.3, Changes 74.8/76.3 → 36.3/36.1.
   Layout boxes and pixels equal in 30 of 30 cases; four suites pass with it on. Behaviour: a VirtualList in a container
   sized from its content loses the width its realized rows gave it (audit: CommandPalette, TableView, InspectorView and
   16 app sites). Prototype `r17-layout\probe-r17.patch`. Run the full suites, the goldens, Meridian and Aperture.
   Afterwards re-count Waiting text-wrap-ranges-per-width's misses and say what is left for it; layout-virtuallist-
   spacer-stale closes if (b) removes the spacer's last reader outside the list. Small; no API, no pixels.
2. **The 80e94eeb objection: apps-junction-changes-recolour-same-texts + dotnet-junction-kept-texts-working-set** (4
   with 5; r17-4 REVIEW and Candidate 2, r17-8 REVIEW and Candidate 1) — separate commits. (a) Junction keys a
   working-tree diff's colours by the data generation (JunctionApp.Highlight.cs:285), which every working-tree event
   moves (JunctionApp.cs:887): a save of any file, or a new untracked file, re-reads and re-colours the diff on screen
   from line 1 though its texts did not change, and its colours blink off (bigrepo: 323.6 MB and 13,623 lines,
   uncoloured from 484 to 3,224 ms; Window.cs 27 MB, TextPipeline.cs 6.4 MB), and every kept text becomes unreachable.
   Keep a 64-bit hash of each text with the Working/Staged highlights (repo|mode|path); when the new generation's texts
   hash the same, land the old colours under the new key and carry the kept texts (expected −98…−99 % of the event's
   colour bytes, no uncoloured frames). (b) After a big-file walk Junction holds +48 MB working set since 80e94eeb
   (ws/private 154.1/163.4 → 202.6/212.2 MB); after a forced compacting full GC heap 12.7 → 23.6, committed 34.5 → 72.8,
   ws 144.9 → 183.1 MB, gen-2s over the scene 9 → 3: the kept LOH strings move when gen-2 runs and leave ~27 MB
   committed free. Measure r17-8's options (UTF-8 or pooled texts, a smaller bound, one aggressive collection when the
   walk goes idle, `System.GC.ConserveMemory`) and r17-4's idle expiry against f76cb440 alone, in a real window with
   `gcmem`/`gcfull` (`r17-apps\aotx-*`); keep the one that brings ws within ~10 MB of f76cb440's and keeps the
   revisits' gen-2 win. If none does, stop and bring the trade to the user with the numbers (not needed:
   step 2 landed an aggressive collection at rest). Expert 8's suite proposal (h) as the guard if it can be built without Junction (an AsyncLoader per selection
   of ~30 MB cancelled at 400 ms; committed and ws after a full GC as well as all-thread bytes and gen counts).
   Small–medium; app + Syntax; no API.
3. **syntax-diff-skip-shared-stretches** (4; r17-4 Candidate 1) — `TokenizeDiff`'s old-version walk takes a line's end
   state and spans from the new version's walk when the line has the same text and an equal start state (stage 1 keeps
   each new line's end state, 8 B a line, and a reuse flag; stage 2 tries the expected line, then a hash index of line
   texts, at most 16 tries). Prototype `r17-dotnet\skip\skip-prototype.diff` (CRLF; `git apply --ignore-whitespace`):
   bit-for-bit spans on 9 real pairs, also cancelled every 150 ms and resumed; lines walked bigrepo 13,626 → 7,453
   (285.1 → 156.9 MB), Window.cs @41ecec65 3,156 → 1,668 (14.9 → 8.0 MB), VirtualListElement.cs @b8a3dcf5 3,654 →
   2,064, the ledger archive 7,681 → 4,240; single-hunk diffs gain nothing. Junction headless, a stop on an unvisited
   big file 295.0 → 172.7–175.0 MB all threads, GCs 18/0/0 → 10/0/0; real window Commit open 229.0/258.5 → 169.0 MB. Add
   the 9 B a line kept to Junction's `Bytes`. After step 2. Medium-small; Syntax; no API, no pixels.
4. **reactivity-junction-colours-before-rows, route (b)** (6 with 5; r17-6 Candidate 1, r17-5 and r17-2 on it; Waiting
   junction-review-focus-colours) — when a file's colours land after its rows' first frame, the row shape changes with
   `hl` and `InvalidateRows` makes every row on screen again (JunctionApp.Diff.cs:629–639): a first visit 744.1–822.5
   KB, 2 builds, 2 frames, 68 rows, 514 measures, against a revisit's 365.3–378.4 KB (real window 744.4–817.4 against
   362.5–375.6; the first Commit open 1,202.2–1,221.2 KB). Recolour the realized code labels in place
   (`TextElement.Spans` value-compares and marks only paint for colour-only spans, TextElement.cs:88–103: 0 text
   layouts, r17-5) and skip `InvalidateRows` when the row shape differs only in its highlight; a floor probe that
   skipped the invalidation gave a first visit 474.0–545.6 KB (−36…−40 %). Find out why an invalidated row costs ~7.8 KB
   at paint even when patched in place (keyed rows and no spans: render 263.6–265.4 KB; r17-2: those bytes are rows
   realized in `ScrollViewElement.UpdateContentSize` inside the render pass), its own commit if it pays. Keep a focused
   review control across a landing (junction-review-focus-colours). Probe `r17-reactivity\probe17.patch`
   (`PROBE_KEYROWS`, `PROBE_NOSPANS`, `PROBE_NOINVAL`), `ProbeR17Tests.cs`. Route (a), holding the first frame until
   short colours land, was question 70 (the user: the recommendation, (b) now). After step 3 (it moves when colours land). Small–medium; app; no API;
   the same settled pixels.
5. **reactivity-junction-view-tabs-part** (6; r17-6 Candidate 2; with r17-8's reactivity-vesper-same-folder-down lead) —
   every view switch builds the whole toolbar part (it reads `View`: 52.6–53.1 KB, the tabs 33.8–34.2, the git buttons
   15.9): the seven view tabs in a part of their own (prototype `PROBE_TABSPLIT`, `patch_tabs.py`): headless History
   136.2 → 117.8 KB (−13.5 %), Tags 98.0 → 79.6 (−18.8 %), Overview −12.1 %, Branches −11.5 %, Changes −9.1 %, Commit
   −4.0 %; real window History 134.5–135.2 → 116.3–117.0, Tags 96.9 → 78.7. Try the per-tab form too (tabs read `View`
   untracked and `SetView` bumps the two tabs' revisions: ~24 KB more, History about −31 %, Tags −43 %). Extend
   ChromePartTests; check pixels by frame hashes. In the same step, measure first: with both of Vesper's panes on one
   folder a Down costs 32.9 KB (18 KB in its input) against 2.8 KB (r17-8, not compared with base); a commit only if it
   is ours and clears the churn bar. Small; app; no API.
6. **painting-player-bar-fade-layer** (2 with 3; r17-2 Candidate, r17-3 Candidate 1; question 69: (b), the user 2026-11-19) —
   the video player's control bar fades by its `Opacity` (VideoPlayer.cs:275, :833–870) and holds two Sliders
   (`PaintBounds` null, SliderElement.cs:216) and three Canvas glyphs, so every fade frame draws through a window-sized
   layer that Skia keeps for the window's life: real window Reel (NativeAOT, `ProceduralBackend`) first fade DXGI local
   +12.0 MB, private +13.5 (Vulkan 1920×1140), +32.0 / +22.5 (3778×2044), GL +12.0 / +23.2; with real mpv playing
   (r17-3) Skia's cache 4.27 → 16.27 MB at the first auto-hide. (a) Pixel-identical: skip a subtree whose
   `RenderOpacity` is 0 in `ElementRenderer` (the hidden bar is drawn on every video frame at alpha 0; elements rendered
   a hidden frame 24 → 8, 220 of 220 frames byte-identical). (b) Bounds for the Slider (its content box grown by
   ThumbRadius + 4) and the glyphs (a private `CanvasElement` subclass reporting its border box grown by 2): layer area
   2.19 → 0.152 Mpx; first fade DXGI local +12.0 → +0.0 (Vulkan), +32.0 → +2.0 (3778×2044), +12.0 → +2.0 (GL); scene end
   local −15…−20 %, private −14…−16 %; 4K fullscreen ~48 MB by arithmetic (~96 on F16). Pixels: 50 of 111 fade frames
   differ in 1–2 pixels of the full-screen icon by 1/255 (question 69). Optional in the same step: give Checkbox,
   RadioButton, ToggleSwitch, Dropdown, NumericUpDown, ProgressBar, Spinner, the pickers, Calendar and Expander's part
   their known reach (closes 96d00ab3's "unknown paint bounds" note; no app fades one today). Probe
   `r17-painting\probe-r17.patch` with `LayerProbe.cs` (`PAINT_SLIDER_BOUNDS`, `PAINT_SKIP0`), hosts `headless\`,
   `rw\`; r17-3's `rw\` and `fx\bands.y4m` for real mpv. A gated guard like `DialogFadeGpuMemoryTests` for the bar.
   Small; no API.
7. **a11y-atspi-adopt-focus-on-attach** (7; r17-7 Candidate; correctness) — on a tree built from nothing (a Linux reader
   attached, or org.a11y.Status turned on after the app had the focus) `LinuxAccessibilityBridge.ApplyChanges` clears
   `_holderId` (LinuxAccessibilityBridge.cs:340–344) and nothing about the focus is told until it moves; the UIA bridge
   adopts it (WindowsAccessibilityBridge.cs:693–702). AtSpiRig, 1,000 items, the focus landed before the attach: a
   cursor walk sends 0 signals a move (2 when it landed after), a selection Down 2 (4); served lists 0. Adopt the
   focused node as the holder; a Linux bridge test with the focus set before the bridge. Small; no API, no pixels;
   step-ab with `-Filter a11y/` is enough (no other row reaches the code).
8. **text-heights-pass-height-only** (5 with 1; r17-1 Candidate 2, r17-5 EXAMINED; measure first, after step 1) —
   Junction's heights pass asks a full wrapped layout of every row to read one height. The two surveys measured
   different scenes: on 200 rows of real prose (mean 473 characters) the pass is 43–44 KB a Commit step (47 % of its
   bytes, 66 % after step 1) and 105–112 KB a Changes step, with only 4–6 % of the heights moving (r17-1); on the ledger
   commit a359e426 8.1 KB a pass (30 %), split 13.7 KB (16 %), C# 1.1 KB (r17-5). Build only the part with no public
   API, an internal height-only ask that allocates nothing, if it clears the churn bar on a real document after step 1;
   a "same line count" width range through `TextMeasure` is public API (not asked). Small–medium; text; no API.
9. **a11y-client-frame-share** (7; r17-8 lead; measure first) — with a UIA client asking for the root, "other" (time
   outside style, layout, render, flush and present) is 26.2 % of Junction's frame time against 3.7 % with none
   (Commit → History 82.8 % against 7.7 %, Commit open 39.9 against 0.2, History Downs 19.3 against 4.4), bytes only
   +2.8–6 %. Count what runs in those frames with a client asking (r17-7: 80 provider calls a key on the UI thread) and
   say what is ours and removable; a fix only if it clears the time bar in timed runs. Medium to measure.
10. **vk-swapchain-two-images** (3; r17-3 Candidate 2; measure first) — a Vulkan window takes 3 swapchain images
   (`minImageCount + 1`, SdlVulkanHost.cs:835): ~19.5 B of DXGI local a pixel against GL's ~13; Reel and Aperture in 4K
   fullscreen hold 3 × 31.6 MB, and two images would save ~32 MB (~8 % of local while playing). Count frames and
   acquire waits in Reel playback and in a timer-step drag with 2 images first, on a maximized window (never fullscreen
   on the user's monitors), mindful of HDR.md:484's image-count hazard; build only if pacing holds. Small code, medium
   risk.
11. **vesper-file-not-its-own-partner** (6; question 72: (b), the user 2026-11-19; step 5's 6751a51a) — with both panes on
   one folder and the other pane's cursor on ".." or a folder, F3 pairs the cursor's file with the same file in the
   other pane, rings it, and moves the ring with every key, so each key builds the window (29.5 KB a key against 2.2 KB;
   expert 8's real window 32.9 KB). Never pair a file with itself: no ring there, F3 does what it does with no partner.
   First check that F3 with no partner opens the file as usual (if it does not, stop and report); show the ring before
   and after in headless frames. Small; app; no API.
   **Stopped before any change** (r17b11, 2026-11-20): F3 is "Compare files (diff)" (the viewer is Shift+F3); with no
   partner `DiffPartner` (CommanderApp.Diff.cs:254) falls back to `<otherDir>\<name>` and the diff opens in its Missing
   state ("Right file missing", a "Copy left → right" button; `DiffViewTests.WithNothingToPairAgainstTheMissing…`). In
   the self-pair layout that fallback is the cursor's own file, so F3 shows "Files are identical"; the no-partner path
   would call an existing file "not found" and offer to copy it into its own folder. → question 72b.

Steps 9 and 10 (measure-first leads) were not taken in round 17: the weekly usage budget was at 81 % after step 8,
kept for step 11, the full suite and the release. They stay queued for round 18's survey to re-rank.

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
  Round 17 (r17-7): add step 7's late-attach check.
- **gl-threaded-optimization-profile** (3; the user's driver setting, information only) — the walk lands while the
  window rests; the setting would remove only the per-frame spin (round 9: mean 357 JIT / 344 AOT, max 456/578
  `SwitchToThread` a paced frame; round 10: mean 378/376, max 723/644).
- **text-open-measures-every-line (full)** (5; user: not now, 2026-10-13). Round 13 (r13-5), stronger on real CJK text:
  a 1 px drag step re-breaks every wrapped row of the document (Japanese at 1440: 1,040 prefix tables, 119,899
  characters itemized and 84,495 fallback questions a frame; real window at 960 logical: 2,618 tables, 166,806
  characters) with ~40 rows on screen; step 3 removes the shaping and bytes, not this O(document) itemizing.
  Round 14 (r14-5): asked again as question 57 (zh at 1440 1,140 tables and 131k characters itemized a drag frame;
  the 154k-character file 2,178–5,288 tables and 9,900–13,550 HarfBuzz calls a frame; Arabic 1,846 BiDi probes a frame).
- **controls-menu-max-height-scroll** (1; not perf: no maximum height or scrolling). Round 17 (r17-1): unchanged.
  (layout-virtuallist-spacer-stale closed by dd3ad712: no parent's measure reads the spacer any more.)
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
  Round 17 (r17-4): on 2026-11-19 `updates-testing` has the final 11.0.100-1.fc44, `updates` still rc.3; the installed rpm is dotnet-sdk-10.0; both links still in place. The user could take it from updates-testing now.
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
  Round 17 (r17-8): (e), (g) and (i) closed (the closed note); (h) medium, as step 2's guard; (a), (d) and (f) low.
- **findbar-count-not-announced** (7, reader behaviour, not perf) — a find bar's new "n of m" reaches the bridge as a
  Name change on the count label; a reader speaks it only if it moves there (no `Announce`, no live regions).
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
  Round 17 (r17-7): low; UIA renames are bounded at 200 (a 150-item replace makes 150 raises, ~1,950 calls with a client); AT-SPI eager lists up to 2,000 (1,000 name signals a replace) need Orca.
- **junction-colours-before-rows-route-a** (6; question 70's (a), not built) — after step 4 (b3bb93bd) a first file visit
  in Junction's Commit view still costs 2 builds and 2 frames, 492–570 KB against a revisit's 362–393 KB; holding the
  first frame until short colours land would make it a revisit (−25…−32 %, 1 frame) with the diff shown later by the
  tokenize's time. Re-ask only if a survey shows the difference matters to a user.
- **elements-selectable-label-lazy-wiring** (1 or 4, lead; r17b4) — a selectable label costs 1,552 B against a plain
  label's 672 B (six handlers and a menu delegate wired at creation); wiring them on first use would save ~11 % a diff
  row (Junction's code labels, Vesper's viewer lines, Meridian's response). Framework; for the next survey.
- **junction-diff-row-cells-one-label** (6, lead; r17b4) — a diff row is 8–10 elements; drawing its number and sign cells
  as one label each would drop 3 elements, ~1.8 KB a made row. App; for the next survey.
- **window-setroot-component-not-mounted** (6, lead; r17b6) — `Window.SetRoot(component.Mount())` never mounts the
  component, so its `OnMount` never runs (step 6's probe hit it; a box around it works). A framework gap or a documented
  rule; for the next survey.
- **vesper-file-not-its-own-partner** (6; **(user)** question 72b; round 17 step 11 stopped) — with both panes on one
  folder and the other cursor on ".." or a folder, F3 ("Compare files") pairs the cursor's file with itself ("Files are
  identical"), rings it and moves the ring with every key, so each key builds the window (29.5 → 2.2 KB a key once
  fixed). Built when the user picks a way in question 72b.
- **text-wrap-ranges-per-width** (5; r17-5 Candidate; after step 1) — keep up to 4 wrap ranges a paragraph instead of 1,
  so a label laid out at several widths a frame keeps one range per width: before step 1, Junction C# unified 72.0 →
  46.0 KB a step (−36 %), split ledger 99.1 → 52.9 (−47 %), second drag 85.1 → 38.3; 0 of 4,857,450 drag layouts
  differ; retained +2–3 %. Step 1 (b) removes the ancestor-width layouts that thrash the memo (element re-breaks 2,534 →
  28 per 40 steps), so this may be unneeded: step 1 re-counts the misses.
  Round 17 step 1 (dd3ad712) re-counted: C# 574f43a7 far misses 1,062 → 26, 42.2/39.0 KB a step (the memo alone reached
  46.0); a359e426 split far misses 3,658 → 1,094, 49.1/34.9 KB (memo alone 52.9/38.3). Low: for the next survey, the
  memo on top of step 1 on the split view only.
- **vk-drag-steps-dropped** (3, information; r17-8) — in a real-window timer-step drag Aperture and Reel (Vulkan) took
  only 11–19 of 31 queued steps in the drag window (present 54–88 % of those frames); the GL apps took all 31. For the
  next survey: is a Vulkan resize step's present slower than the 16 ms pace, and does a user's own drag show it?
- **junction-files-first-open** (6, lead; r17-8, r17-6) — Junction's Files view first open: 3,806 KB, 3 frames, 3 builds
  (its loading states), once a session.
- **aperture-signin-user-message** (app bug, not perf; r17-8) — with an API key on a server with several users,
  `ApertureFailure.UserMessage` drops the "name the user to browse as" detail the sign-in returns (question 71).

Closed from Waiting by the round 17 survey (→ Examined unless named): junction-colour-landing-remakes-rows (→ step 4),
a11y-served-item-one-provider-per-tell (r17-7: with the gate a Vesper key is 1.4 KB with no reader asking; with one 2.1
KB, and one provider a tell would save at most ~0.15 KB), vesper-start-focus-event (closed by 0d96e805: no reader, no
tree, no event; a reader's first ask adopts the focus with no event), and Suite scenarios (e) (41ecec65's row), (g)
(ec57c4ee's gated UiTests guard) and (i) (96d00ab3's `DialogFadeGpuMemoryTests`); (h) → step 2.

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
| 17 | 8dbae799, dd3ad712, 4c890435 | perfsuite-columns-walk-focus (harness) + layout-virtuallist-measure-through-rows | (a) `ColumnsWalk` focuses its list: `virtual/columns cursor walk 100k item rows` renders 26.8 cells, 261.5 KB a step, 0 unrendered steps; a full suite run exits 0 again (`--quick`, all 248 scenarios, and step-ab's full JIT and AOT runs). (b) An internal `ScrollViewElement.MeasuredAsLeaf` (true for VirtualListElement), read by `MeasureSize` and the baseline probe: an ancestor measures a virtual list as a leaf, not through its realized rows, so no visible row is laid out at a width it is never drawn at. Headless (1440×900, 40 steps of 6 px, old and new in one process, alternated ×2), KB a step: resize Commit unwrapped 86.0/67.3 → 31.4/28.2 (asks 429 → 244), Changes unwrapped 77.7/71.1 → 29.5, Changes unwrapped split 145.6/142.3 → 66.5 (asks 835 → 358), Commit wrapped 103.7/95.5 → 63.3 (breaks 123.5 → 87.4), Changes wrapped split 270.7/265.1 → 195.4; splitter steps Commit unwrapped 33.5/28.5 → 13.9, Changes wrapped split 250.5/241.7 → 142.1; expert 5's a359e426 split 80.2/71.5 → 24.0 (re-breaks 3,919 → 1,396 per 40 steps, the rows' own 2,538 → 28); History, Vesper's panes, reader and compare view equal; 0 builds. Real window (NativeAOT, the 4K at 150 %, 2 holds, 0 foreground takes, 0 nvlddmkm; timer steps on the UI thread): Commit unwrapped resize 70.5–81.9 → 23.2–37.5 KB, 12.8–13.4 → 8.1–9.3 Mcycles, asks 497 → 317; Changes unwrapped split 143–145 → 60–73 KB, asks 880 → 417; Changes wrapped split splitter 233–236 → 126 KB (16 scenes in dd3ad712's body). Audit: 19 sites and the samples (CommandPalette, Vesper's Find and Rename set their height; TableView, InspectorView and the 16 app lists grow in columns whose siblings don't shrink): none needs the old content width; the layout audit over 9 suites, 4.75 M boxes, differs only in the new test and A11yUndrawnFrameTests (a 1 px counter no longer squeezed to 0.77 px by the list's rows: a fix). Pixels: boxes, scroll offsets and frame hashes equal the old rule's in 240 of 240 cases (1.0 and 1.5, wrap on and off, unified and split, 5 views, 3 widths, top and scrolled, caches emptied); goldens pass. step-ab (8dbae799) `-Pairs 2`: JIT 0 slower both pairs (pair 2 one faster, the partner walk −10 %), AOT 0/0 both pairs, A/A 0/0; the partner walk 36.2 → 33.6 KB allocKB on both runtimes. `VirtualListMeasuredAsLeafTests` (2) fail under a reverse edit; Windows `HultUI.slnx` 30 projects, 14,429 passed; Fedora the slnf plus Junction, Meridian, Aperture and Reel each in a private D-Bus session, 26 projects, 13,974 passed. LAYOUT.md, ELEMENTS.md (size a virtual list from outside: FlexGrow or a height, plus stretch or a width). After (b), expert 5's jcommit host: C# 574f43a7 re-breaks 1,129 → 185/142 (far misses 1,062 → 26), 71.8/79.7 → 42.2/39.0 KB (their 4-range memo reached 46.0); a359e426 split 4,394 → 1,579 (far 3,658 → 1,094), 99.0/85.2 → 49.1/34.9 KB (memo 52.9/38.3); 1,368 of the 1,396 re-breaks left are the heights pass's (step 8). |
| 17 | 34606bab, b1423e6e, b185a60c | apps-junction-changes-recolour-same-texts + dotnet-junction-kept-texts-working-set (the review objection to 80e94eeb) | (a) A working-tree event whose new generation reads the same texts keeps the diff's colours (a 64-bit hash of each text with the Working/Staged highlights) and carries the kept texts: headless bigrepo, 2 alternated pairs, an untracked file or another file's save with a big modified file on screen 288.7 → 9.4 MB all threads, 13,639 → 0 lines walked, the diff uncoloured 475–2,921 ms → never; git processes (8/9) and file reads (1) unchanged; without the Changes view's own load the event's colour bytes 284.6 → 5.9 MB (−98 %); a small file 42 → 0 lines; a real text change still recolours, spans equal a fresh walk's. Real window (AOT) 266 → 9.4 MB an event, uncoloured 2.8 s → 0 frames. (b) Once a walk that kept texts comes to rest, one `GC.Collect(2, GCCollectionMode.Aggressive)` 2 s later on the UI thread gives the memory back (pauses 7.6–12.0 ms, no frame or key in that window). Options in the real window (NativeAOT, bigrepo, expert 8's jbig scene, 2 holds alternated), end-of-scene ws │ after a forced full GC ws/committed/private │ revisit pass MB, gen-2s: kept (80e94eeb) 202.6 │ 181.6/71.3/192.4 │ 368.5, 0; none (f76cb440) 155.7 │ 146.2/35.8/154.8 │ 375.1, 4; **kept + one aggressive GC at rest 134.1 │ 134.7/24.4/144.6 │ 368.1, 0 (kept)**; a compacting gen-2 at rest 180.7 │ 181.3/71.2/191.2 (frees the heap, keeps committed); `GCConserveMemory=5` 175.6 │ 157.3/46.8/167.8 │ 367.6, 2; a 6 MB bound 162.0 │ 149.7/39.3/159.8 │ 374.0, 3; dropping the texts at rest 187.8 │ 170.6/60.2/181.8; UTF-8 texts 183.9 │ 155.6/45.1/165.3 │ 397.0, 2 (1 run). The committed code, hold 3: 134.0 MB at scene end (kept 202.5, none 154.6); after a second 12-key pass and its collection 141.7 (186.6, 163.6), that pass 384.0 MB with 0 gen-2s (none 392.6, 4). Pixels: settled coloured frames after two events byte-identical to a fresh load's. step-ab (66f46f11) `-Pairs 2`, all runs idle-gated: JIT 0/0 both pairs (pair 2 the rich-text a11y rows' allocKB 3.3 → 2.4, the A/A the other way), AOT 0/0 both pairs, A/A 0/0. `HighlightGenerationTests` (3) and an idle-collection test in `HighlightResumeTests` fail under every reverse edit; Windows Junction 1,171, Syntax 137, Headless 1,794; Fedora Junction 1,171 (private D-Bus). Real window 3 holds, 24 launches, 0 foreground takes, 0 nvlddmkm, the 4K at 150 %, 120 Hz. Suite proposal (h) not built (the suite runs no Junction; the Junction test is the guard). DECISIONS.md (the options table), NATIVE-AOT.md (only an aggressive collection gives committed memory back). |
| 17 | a787c194, 23dd0de4 | syntax-diff-skip-shared-stretches | `TokenizeDiff`'s old-version walk takes a line's end state and spans from the new version's walk when the line has the same text and starts from an equal state (TextMateSharp's `StateStack.Equals` ignores whether a rule's begin matched to the end of its line, which changes how `\G` matches next: the take also compares that, through an `UnsafeAccessor` to an internal member that a test pins); 9 B a line counted in Junction's kept-text `Bytes`. Equality: 0 lines differ, spans bit for bit against e77f210d and against each version's whole `TokenizeLines`, fresh and cancelled every 20 ms and resumed (1,326 cancels), at -U3 and -U12, on 379 pairs (the 9 survey pairs, 252 files of 1,000+ lines from the last 200 commits, 118 XML/YAML/PowerShell/shell); lines walked over the 200-commit corpus 916,266 → 621,679 (−32 %); bigrepo file00 13,626 → 7,453, 279.8 → 152.9 MB. Headless JIT (bigrepo, 2 idle-gated runs a side alternated): Commit open 297 → 171 MB all threads, GCs 18/2/1 → 10/2/1, colours at 3,042/3,045 → 1,812/1,799 ms; a stop on an unvisited file 300 → 172–174 MB, GCs 18/1/0 → 11/0/0, colours 2,783/2,833 → 1,635/1,587 ms; 12 keys at 400 ms unchanged (~31 MB a key); step 2's idle collection fires once on both sides. Real window (NativeAOT, 1 hold, base/new/base/new, 0 foreground takes, 0 nvlddmkm; **the 1440p only, 100 %, 200 Hz — the 4K was not attached**): Commit open 293.7 → 168.5 MB, GCs 20/4/2 → 11/2/1, colours 2,920/2,873 → 1,607/1,632 ms; a stop on an unvisited file 297.8 → 172.8 MB, colours 3,222 → 1,845/1,838 ms; the whole scene 1,358/1,370 → 1,138/1,134 MB (−17 %); end of scene after the idle collection ws 129.6 → 130.6 MB, heap 25.5 → 26.4 MB (the 9 B a line counted is 0.34 MB of it; ~13 B a line of distinct state objects the kept walks hold is not counted; five bigrepo pairs still fit the 16 MB bound at 13.92 MB counted). Pixels: 67 of 67 settled coloured frames byte-identical (bigrepo's first 3 files and a repo of 4 real pairs, every wheel position). step-ab (e77f210d) `-Pairs 2`, all 8 runs idle-gated: JIT and AOT 0 slower, 0 faster, no allocKB moved, A/A 0/0. 10 new tests fail under reverse edits (never taking 24, text alone 4, only the rules open 3, no end-of-line check 1, no records across a cancel 4; Junction's bytes test without the 9 B); some walked-line counts updated; Windows Syntax 147, Junction 1,172, Headless 1,794; Fedora Syntax 147, Junction 1,172 (private D-Bus). |
| 17 | b3bb93bd, ef113cef | reactivity-junction-colours-before-rows, route (b) (question 70: the recommendation) | When only a diff's highlight moved, `RecolourKeptRows` sets the new spans on the code labels on screen (each label finds its row and lane through its selection group and index) and `InvalidateRows` is skipped; a label whose spans would change its face (weight, slant, size, family) still has its row made again, and any other shape change still remakes every row; while reviewing, a landing keeps the woven rows as a caret move does (Waiting junction-review-focus-colours settled). Headless JIT, Commit view 1,440×900, alternated ×2: a C# first visit 746.6–824.5 → 492.0–570.3 KB (−31…−34 %), rows made 68 → 34, text measures 499 → 250, 2 builds and 2 frames either way; the landing alone 334.5–347.3 → 79.9–93.7 KB, 0 rows, 0 measures, render phase 272.5–282.8 → 12.6–22.6 KB; wrapped 746.4–945.1 → 519.4–718.9; markdown 763–799 → 507–544; big files 800–848 → 547–606; the first Commit open 1,117.8 → 852.4; Changes' first open 1,792.3 → 1,473.1; a review's landing 436–438 → 216 KB, rows 18 → 0; revisits unchanged (362–393 KB, 1 build, 1 frame). Real window (NativeAOT, 1 hold, 4 launches alternated, the 4K at 150 %, DPI 144, 120 Hz — re-attached during the step; 0 foreground takes, 0 nvlddmkm): first visits 733.1–868.2 → 479.8–613.4 KB (−29…−35 %), rows 68 → 34, render phase 593–718 → 335–461 KB; the first Commit open 1,193.8 → 928.8 / 945.1; Changes' open 1,090.4 → 784.8; revisits 361.4–397.5 unchanged. The ~7.8 KB a remade row is the row template, not paint or the list: 8–10 elements a row (Box 600 B, Text 672 B, a styled Box ~800 B), a selectable code label 1,552 B (six handlers and a menu delegate); `RenderDiffRow` alone 7.4–9.4 KB a row; a keyed row is made in full as a twin, then patched (leads in Waiting). Pixels: 48 of 48 frames byte-identical to the base's (plain and settled, 1.0 and 1.5, wrap on and off, unified and split, 3 stops each); every settled frame equals rows made afresh. step-ab (2b42bda0) `-Pairs 2`: JIT pair 1 0/0, pair 2 8 slower (component and a11y rows), A/A 0/0; AOT pair 1 0/0, pair 2 1 faster, A/A 10 slower; no allocKB moved; the suite's code is identical on both sides (`git diff 2b42bda0 b3bb93bd -- src samples` empty), and a JIT re-run moved other rows (3/6 and 1/0, A/A 2/10): noise. The four landing cases and the review-focus test fail under a reverse edit, the tab-width test under a recolour-whenever-the-highlight-moved mutant; Windows Junction 1,177, Headless 1,794; Fedora Junction 1,177 (private D-Bus), DiffRowKeepTests and ReviewTests 3 more runs of 3. REACTIVITY.md (recolour the rows on screen in place; don't make a row again for a colour). |
| 17 | 3ea96588, 6751a51a, b5ab4704 | reactivity-junction-view-tabs-part + reactivity-vesper-same-folder-down (r17-8's lead; partly) | (a) Each of Junction's seven view tabs is its own part, lit by a `Computed<bool>(() => View == view)`, so a view switch builds the two tabs whose light moved and not the toolbar (the survey's one-part prototype saved 18.4 KB; this form 2.5× that, with no `SetView` bump and no stale tab; a full chrome build +0.4 KB, +0.3 %; the tabs take no keyboard focus, so their element identity, hover kept, is pinned instead). Headless JIT, a fixed fixture, wide 1,440, KB a switch by key: Overview 101.4 → 54.2 (−46.5 %), History 175.3 → 128.1 (−26.9 %), Tags 141.5 → 94.4 (−33.3 %), Branches −20.6 %, Commit 424.1 → 376.9 (−11.1 %), Changes −8.9 %; by click the same −45…−47 KB; compact 1,200 −31…−33 KB (History −23.9 %, Tags −27.5 %); part builds a switch toolbar 1 → 0, tab parts 0 → 2, frames unchanged. Real window (NativeAOT, the 4K at 150 %, 120 Hz, GL; 3 holds, 8 launches alternated, 0 foreground takes, 0 nvlddmkm; repeat launches within 0.1 KB): wide History 174.0 → 127.1, Tags 140.2 → 93.3, Overview 100.6 → 53.7, Commit 421.5 → 374.6; compact (1,185 logical) History 137.2 → 104.4, Tags 118.9 → 86.2. (b) Vesper with both panes on one folder: `PartnerStays` (081921c7, round 8) took the cursor's own file in the other pane for a moving partner, though `DiffPartner` never pairs a file with itself; with the other cursor on a file a key 17.5 → 2.2 KB (−87 %), 1 → 0 builds (headless, 60 files); with the other cursor on ".." or a folder F3 pairs the file with itself, so its ring follows each key and every key still builds (29.5 KB: expert 8's 32.9 KB case) → question 72. Pixels: 56 frames (key and click, wide and compact, 1.0 and 1.5) equal the base's in two alternated runs (a first base run on a fresh fixture differed in Commit/Changes at 1.0 against every later run of either side; the toolbar band matched). step-ab (b6c752e2) `-Pairs 2`: JIT pair 1 2 slower (virtual/rebuild beside 20,000 RowHeights rows, the same 2 as the A/A), pair 2 0 (one a11y rich-text row's allocKB +0.9 KB, that pair alone); AOT 0/0 both pairs and A/A. New tests fail under reverse edits; Windows Junction 1,182, VesperCommander 954 (+4), Headless 1,794; Fedora Junction 1,182 (private D-Bus), the slnf (Vesper 936). |
| 17 | 0a7bb324, c23edfb9, fe46c02b | painting-player-bar-fade-layer (question 69: (b)) | (a) A subtree at opacity 0 is skipped when every element in it only paints (internal `Element.RenderOnlyPaints`; scroll views, text fields, selectable labels, video and web views are always drawn): a hidden player bar's frame renders 24 → 7 elements, window-sized alpha-0 layers 1 → 0; 171 of 171 headless Reel frames byte-identical. (b) `SliderElement` reports paint bounds (its box ∪ the track ∪ the thumb square ±(r+4)) and the player bar's glyphs are a private `CanvasElement` subclass reporting their border box +2, so the bar fades through a layer of its own size: 2.189 → 0.152 Mpx a fade frame at 1920×1140, 7.722 → 0.298 at 3778×2044. Real windows (NativeAOT, the 4K at 150 % primary, the 1440p to its left; 4 holds, 0 foreground takes, 0 nvlddmkm), first-fade DXGI local, first-fade private, end local, end private, Skia's cache at the end: Vulkan `ProceduralBackend` +12.0 → +0.0, +13.5 → +0.1, 67.5 → 55.5 (−18 %), 127.0 → 112.6 (−11 %), 18.26 → 7.26 MB; Vulkan with mpv (bands.y4m) +12.0 → +2.0, +14.4 → +3.9, 284.2 → 274.2, 493.0 → 479.4, 16.33 → 5.33; Vulkan 3778×2044 +32.0 → +2.0, +32.2 → +2.8, 194.4 → 164.4 (−15 %), 165.9 → 135.9 (−18 %), 38.26 → 8.26; GL `ProceduralBackend` +12.0 → +2.0, +27 → +3.4, 49.0 → 39.0 (−20 %), 140.8 → 115.6 (−18 %), 24.98 → 13.98; GL with mpv +24.0 → +2.0, +38 → +4, 271.1 → 249.1, private 458 against 482 and 533; nonlocal and working set unchanged. Pixels: 50 of 171 Reel frames differ, all fade frames, only device pixels (1888,1097) and (1888,1098) on the full-screen icon, by 1/255; goldens pass; Junction 74/74, Vesper 73/73, Meridian 27/27 frames (palette fades included) byte-identical. Only the Slider and the player's glyphs got bounds (the other controls' reach not worked out; no app fades one). Guard: gated UiTests `BarFadeGpuMemoryTests` (GL and Vulkan, 1920×1140, a 1920×78 bar; limit the bar's layer twice + 1 MB = 3 MB): +2.0 MB with the change (Skia's cache 13.71 GL / 5.92 Vulkan), +12.0 under the reverse edit (24.71 / 16.92); a second fade +0.0 everywhere. Aperture's player over `FakeMediaLibrary` headless: the layer is the bar's size, nothing drawn once it has gone. step-ab (e600d2b5) `-Pairs 2`: JIT and AOT `anim/signal tween, 1 kHz posts` −72 % (16.65 → 4.71 ms; its faded bar holds a Slider), `media/video player bar fade` −4.5 %; AOT otherwise clean, A/A clean; JIT `virtual/rebuild 20,000-row` +34…+42 % also in its A/A (noise), `grid/edit auto-column` +14 % one pair only; no allocKB moved. New tests fail under reverse edits; Windows Core 5,212, Headless 1,801, Rendering.Skia 462, Media 93, Media.Backends 18 (+12), Reel 53, Aperture 370 (+21), Fluent 38, the UiTests guard; Fedora the slnf with Reel and Aperture each in a private D-Bus session, 24 of 24 assemblies. ELEMENTS.md (one element with unknown paint bounds makes its faded ancestor window-sized). |
| 17 | 13fee514, b5ac3235 | a11y-atspi-adopt-focus-on-attach (correctness) | On a tree built from nothing (a Linux reader attached, or org.a11y.Status turned on late), `LinuxAccessibilityBridge.ApplyChanges` takes the node the tree marks focused as the holder and its target (the current item) as `_focusedPath`, so the item's object says focused and the list's does not, as after an early attach; a move the window tells in the same frame still goes out once (`_focusUntold`, which covers a field focused before the first tree); after a re-attach a served item that had the focus before the detach is marked unfocused. No signal at the attach itself, as UIA: Orca reads the focused state when it starts (`orca.py _activate_services` → `find_focused_object` → `AXUtilities.get_focused_object`), it does not wait for an event. Signals a move sends with the focus landed before / after the attach (AtSpiRig, headless): cursor walk eager (1,000) 0 / 2 → 2 / 2, selection Down eager 2 / 4 → 4 / 4, cursor walk served (10k, 1M) 0 / 2 → 2 / 2, selection Down served 0 / 2 → 2 / 2. `A11yAtSpiLateAttachTests` (19 cases: late or early status, walk and selection, eager and served, Tab out, a text field, a tree of element rows and an open context menu at the attach, detach and re-attach, a field focused before the first tree): 7 fail with the adopt undone, 2 more with the edge cases undone. step-ab (50bf6a18) `-Pairs 2 -Filter a11y/` (only the 21 AT-SPI rows reach the bridge): JIT 0/0 both pairs (one row's allocKB moved in pair 1 and the same way in the A/A), AOT 0/0, no allocKB moved, A/A clean. Windows Linux bridge 104, Core 5,212, Headless 1,820; Fedora the slnf (22 projects) green. ACCESSIBILITY-PLAN.md (a reader that attaches late hears the next move on both bridges). No real window (no desktop Orca). |
| 17 | e790d321, 91721d06 | text-heights-pass-height-only (measured first, then built) | Where a height ask's bytes went (base 13a3e123, JIT headless, prose Commit, per 6 px step): the heights pass 42.0 of 63.6 KB, of it 31.0 KB the break (503 `TextLine`s and a `LineSource` for each of 89 rows broken again), layouts and line arrays 9.5, memo puts 0.1, Junction's float[] 0.8; Changes 112 of 138 KB (84.5 the break). `TextMeasure.MeasureWrapped` now asks an internal `IWrappedSizeContext` (`SkiaMeasureContext`), and `TextPipeline.WrappedSize` breaks with the same decisions and adds the lines' widths and heights to the bit without making a line, keeping a size-only width range; a paragraph seen for the first time, or one whose kept layout was drawn (`TextLine.RunsMade`), is laid out as before (without that rule visible rows broke twice: 574f43a7 142 → 229 re-breaks a drag); first opens equal bytes. No public API. Headless JIT, UI KB a step (heights pass): prose Commit 63.6 → 21.7 (42.0 → 2.5), split 72.4 → 21.3, Changes 138.0 → 25.8 (112.0 → 4.1), split 196.5 → 41.1; a359e426 18.4 → 10.4 (9.3 → 1.5), split 24.5 → 15.3; 574f43a7 7.7 → 7.4, split 12.7 → 10.4; cycles equal or lower everywhere. Headless NativeAOT (idle-gated): prose Commit 78.7 → 35.2 KB, heights pass 49,494 → 896 B, 10.15/9.96 → 9.60/9.89 Mcycles; a359e426 split 33.6 → 23.5 KB (13,696 → 1,144 B; second-sweep re-breaks 1,575 → 1,772, cause not found); 574f43a7 31.9 → 32.1, split 34.4 → 31.0. Real window (NativeAOT, the 4K at 150 %, 120 Hz, GL, HDR on; 2 holds, 3 launches, 0 foreground takes, 0 nvlddmkm; timer steps): prose Commit 70.3/71.1 → 31.8/31.9 KB (heights pass 40 → 2.9), split 77.3/78.1 → 31.2; Changes 131.3/133.4 → 34.0 (101 → 4.4), split 188.6/189.6 → 51.7/51.1; a359e426 21.1/19.6 → 18.0/17.7, split 30.1/31.0 → 22.5; 574f43a7 28.1/24.6 → 25.0/22.2; Mcycles flat. Equality: 2,011,835 size asks against the reference layout's size (reallatin, realbig, zh/ja/ko/th, ar/he/emoji; 5 styles × 47 widths) with 402,360 layouts between and 114,500 range-end probes: 0 differ; corpus hashes unchanged (48k 7A18AF8DE8E05271, 240k F09E9038726E4BD6, also with a size asked before each layout); Junction heights, boxes and pixels equal in all 8 scenes. step-ab (13a3e123) `-Pairs 2`: JIT 0 slower both pairs (one faster in pair 1, also in the A/A), two a11y rich-text-in-list rows' allocKB 2.9 → 2.0 / 3.3 → 2.4 on JIT only (stack-allocation placement); AOT 0/0, no allocKB moved, A/A clean. `WrappedSizeTests` (5) fail under reverse edits; Windows Text 311, Skia 467, Headless 1,820 (goldens), Junction 1,182, Vesper 954; Fedora the slnf (22 projects, 11,746 tests); Fedora Junction 1,181 of 1,182 — `JunctionAppHostTests.TheBranchesViewShowsABranchCreatedOnDisk` fails 3 of 3 at the base 13a3e123 too (r17fix). ELEMENTS.md. |
| 17 | 9f9518e2 | junction-linux-ref-in-new-folder (correctness; found by step 8's Fedora run) | Fedora's `JunctionAppHostTests.TheBranchesViewShowsABranchCreatedOnDisk` failed 3 of 3 at 13a3e123 — and also at b5ab4704 (3 of 3 alone, 2 of 2 in its class, no D-Bus session), so no round 17 commit caused it; step 5's Fedora pass was a race won under load. `PumpUntilVisible` reads the a11y tree for 20 s; the Branches list held only "main": the summary was read in the first frame, and no reload followed because on Linux inotify watches a new folder only after it hears the folder made, by when git had written `refs/heads/feature/from-a-test` into it, and the refs watcher's filter had no `DirectoryName`, so the folder's own event was dropped too. A real Junction bug on Linux: a `feature/x` branch made in a terminal, a fetch's first slashed branch or a tag in a new folder left the views stale; Windows never affected (10 of 10 at 2-core affinity). Fix (`apps/Junction/Git/GitWatcher.cs`): the refs watcher also asks for folder events, so a folder made or removed under `.git/refs` counts as a ref moving; on Windows one more event inside the same debounce window. `GitWatcherTests.ARefMadeInANewFolder_IsToldAsARefMoving` (two branches and a tag in new folders, then a branch deleted) fails 3 of 3 under a reverse edit on Fedora (nothing heard); the host test and the new test 10 of 10 on Fedora (private D-Bus) and Windows (0x3); `JunctionAppHostTests` + `GitWatcherTests` 30 of 30 on both (twice on Fedora); Junction.Tests 1,183 of 1,183 on both. No step-ab (app watcher only; no suite row reaches it). |

## Examined, not worth it

From the round 17 surveys (details in `17-reports/r17-*`):

- **text-heights-pass-same-height-range** (r17-5): a "same height" width range for Junction's heights pass needs a public
  out-range on `TextMeasure.MeasureWrapped`, for at most −4.9 KB of 27 KB a step (−18 %) on one scene; reusing
  `_diffHeights`' array −1.5 KB (5.7 %). Step 8 measures the height-only ask without API.
- **Swap interval 1 during tweens** (5da967d2's untried lever; r17-3): NVIDIA's write-combined pool fills the same either
  way (Vesper GL nonlocal 16.03 → 28.52 MB at visit 5, then flat), 90–94 frames per 5 visits on both sides.
- **The 100 ms display-colour poll on Vulkan without events** (r17-3): 13–15 UI wakes a second only when a GL-default app
  is forced onto Vulkan; Aperture and Reel, which ship on Vulkan, use display-colour events.
- **The palette's kept panel layer** (r17-3): +8 MB on GL, +4–5 MB on Vulkan, once a window: below the bar.
- **Keyed diff rows alone** (r17-6): −46 % measures a colour landing, +2 % bytes; only worth it with step 4's recolour.
- **A11y after the gate** (r17-7): a crossing into ≤ 200 items rebuilds the tree (an in-place refresh saves ~35 of 380
  KB, −9 %, readers only); a reader's attach costs 46–58 KB inside the ask plus 68–89 KB of the client's walk, once a
  window; the gate never closes after a reader quits (closing it would make an idle reader's next key silent). The
  kept-label array sized by UI-thread Name asks is a headless-probe trap: a real client asks off the UI thread (+3 KB
  entering 20,000 files, against 469 KB headless).
- **The TokenizeDiff skip on paced keys** (r17-4): at a 400 ms pace the worker walks until the next key either way (28–31
  MB a key on both sides); step 3's win is per finished pass. Vesper's viewer re-colours on every open (12 MB on workers
  a 166 KB markdown open): a cache would help only reopening the same file.
- **Column lists** (r17-1, r17-4, r17-7): ~6–8 KB a cell scrolled in, as rows; hit-testing a reverse walk over ~186
  cells; no app uses columns or the cursor yet.
- **Reel's pixel-path video frame** (r17-2): 0.242 KB a frame (`SKImage.FromPixels`), on the pixel path only; mpv's GPU
  path keeps one wrapper an image.
- **Meridian typing in an 89 KB body** (r17-2): 9.4 KB a frame, the edited line's shaping and the TextArea's incremental
  bookkeeping.
- **Junction's sideways pan, the splitter's press and release, Meridian's send, the player chrome** (r17-6): 0.08 builds a
  pan step; 2 view builds a drag; Meridian's chrome does not build on a send; no player build reads the position.
- **Vesper thumbs and Tab** (r17-8): +0.7 KB a key; 204 KB a Tab on both sides.

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

Round 17 questions (from the survey, 2026-11-19). Recommendations in brackets.

69. **The video player's bar fade** (r17-2, r17-3; step 6 (b)): the player's control bar fades through a window-sized
    layer that Skia keeps for the window's life: in Reel +12 MB of GPU memory at its default size and +32 MB near
    maximized (~48 MB in 4K fullscreen by arithmetic, ~96 MB on an HDR swapchain), private +13…+23 MB. Giving the slider
    and the bar's glyphs their paint bounds shrinks the layer to the bar (−93 % of its area): GPU −12…−30 MB, private
    −14…−16 % a playing window. During the 0.3 s fade-out and 0.15 s fade-in, 1–2 pixels of the full-screen icon are one
    level off (1 of 255) in 50 of 111 frames; every other frame is byte-identical. (a) Keep today's fade. (b) Bounds,
    with the one-level change. [(b): the same kind of change as question 68's.] → **(b)** (2026-11-19, the user). Before/after images of the fade frames
    on request (headless, `PAINT_DUMPDIR`).
70. **Colours before rows** (r17-6; step 4's route (a)): moving to a file in Junction's Commit view draws its diff plain
    and the colours land a few frames later; today every row on screen is then made again (a first visit costs about
    twice a revisit's bytes and 2 frames). (a) Hold the file's first frame until its colours land when the tokenize is
    short: 1 frame, about −50 % bytes a first visit, the diff shows a little later. (b) Draw plain at once and recolour
    the rows in place when the colours land: step 4, no decision needed, −36…−40 %. [(b) now; (a) only if step 4's
    numbers leave a difference worth a later first frame.] → **the recommendation** (2026-11-19, the user). Step 4
    (b3bb93bd) landed −29…−35 %; (a) would add −25…−32 % and 2 → 1 frames a first visit with the diff shown later by
    the tokenize's time: not built (Waiting junction-colours-before-rows-route-a).
71. **Jellyfin's user** (r17-8): jellyfin.home has 3 users, so an API-key sign-in needs a user name (the host logs
    `signin=NotFound: … has 3 users — name the one to browse as`; this is why round 16's sign-ins returned
    `library=none`). Name the user the surveys should browse as (`START_APERTURE_USER`), or leave Aperture's library
    unmeasured. [A user with a small library, or a test user.] → the user named the account (2026-11-19); it is in
    memory `aperture-jellyfin` and in each brief, not in committed docs (the repository is public).

72. **A file as its own F3 partner in Vesper** (step 5, 6751a51a): with both panes on the same folder and the other
    pane's cursor on ".." or a folder, Vesper pairs the cursor's file with the same file in the other pane, rings it,
    and moves that ring with every cursor key, so each key builds the window (29.5 KB a key against 2.2 KB; expert 8's
    real window 32.9 KB). Comparing a file with itself shows no difference. (a) Keep it so. (b) Never pair a file with
    itself: no ring there, and F3 does what it does with no partner. [(b), if F3 with no partner opens the file as
    usual; the build agent checks that first and shows the before/after ring.] → **(b)** (2026-11-19, the user; step 11).
72b. **The same, now that F3 is known** (step 11 stopped, 2026-11-20): F3 is "Compare files", and with no partner it
    opens the diff as "Right file missing" with a "Copy left → right" button, so (b)'s condition does not hold; in the
    self-pair layout F3 today shows "Files are identical". Which instead? (1) Drop only the ring there (F3 keeps showing
    "Files are identical"; the ring then no longer always marks what F3 opens). (2) F3 does nothing there, as on a
    folder, and nothing is ringed (the rule stays "a ring marks what F3 compares"). (3) F3 opens the viewer there (new
    behaviour). (4) Keep it as it is. Each of (1)–(3) also removes the per-key build (29.5 → 2.2 KB a key).
    [(2): comparing a file with itself is never useful, and it keeps the ring's meaning.]

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

Both items need other hardware (r17-3: this desk has no AMD or Intel GPU, and nothing in rounds 16–17 touched the
destroy-first path, SdlVulkanHost.cs:845, or the depth/stencil request); surveys stop carrying them.

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
- 2026-11-19: round 17 survey done (reports cae692da..fbf22216, `17-reports/r17-*`; all eight spawned at ~11:20 UTC, all
  in by 12:16; 65 → 71 % weekly, the 5-hour window 6 → 53 %). No expert satisfied. Review objections: 80e94eeb (expert 8
  bisected +48 MB working set after a big-file walk, committed 34.5 → 72.8 MB after a full GC; expert 4: Changes-mode
  kept texts are unreachable after any working-tree event) → step 2; b8a3dcf5's suite row `virtual/columns cursor walk
  100k item rows` measures nothing and makes every suite run that includes it exit 1, which step-ab refuses (experts 1
  and 4; checked: `step-ab.ps1:212` throws on a non-zero exit) → step 1 (a), first. Main's 8 commits: no real-app
  regression; round 16 helped the real apps (Junction start −18 %, a ref move −57 %, a wrapped drag −87 %, the first
  palette open +18 → +8 MB DXGI, Vesper's reader open −28 %). Experts 2 and 3 found the same window-sized layer in the
  video player's fading control bar (step 6, question 69); expert 5's ancestor-width label layouts were sent to expert 1
  mid-survey, who found the cause (an ancestor measures a VirtualList through its rows) → step 1 (b). Merged into a
  10-step build order; questions 69 (the player bar's pixels), 70 (colours before rows) and 71 (Jellyfin's user) asked;
  3 Waiting items and 3 suite proposals closed, 4 items added. Real windows: 83 guarded launches in 15 holds of
  `Global\HultUI-r17-desktop`, 0 foreground takes, 0 driver events, 0 screen reads. For PERFORMANCE.md: an element
  faded by `Opacity` gets a window-sized layer whenever a descendant has unknown paint bounds (Slider, Toggle,
  Checkbox, Dropdown…), so check Skia's cache after the first fade; a VirtualList inside auto-basis flex rows is
  measured through its rows at widths never drawn; strings over the LOH threshold kept in a cache move gen-2 timing
  and keep committed memory far above their own size: measure working set and committed after a full GC, not only the
  heap. Phase → build.
- 2026-11-19: round 17 step 1 landed (8dbae799, dd3ad712; captures 4c890435): the columns walk row focuses its list, so
  full suite runs exit 0 again; an ancestor measures a VirtualList as a leaf, not through its rows: Junction resize and
  splitter steps −48…−70 % UI bytes (real window Commit unwrapped 70.5–81.9 → 23.2–37.5 KB a step, 12.8–13.4 → 8.1–9.3
  Mcycles), pixels equal in 240 of 240 cases, the whole Windows suite and the Fedora run pass. It takes most of
  text-wrap-ranges-per-width's win (C# far misses 1,062 → 26); the heights pass makes what is left (step 8).
  layout-virtuallist-spacer-stale closed. For PERFORMANCE.md: size a VirtualList from outside (FlexGrow or a height, plus
  stretch or a width); run the layout audit's floor on the differing tests before blaming a change. Next: step 2.
- 2026-11-19: round 17 step 2 landed (34606bab, b1423e6e; captures b185a60c): the objection to 80e94eeb is answered. A
  working-tree event that leaves a diff's texts unchanged keeps its colours (bigrepo 288.7 → 9.4 MB an event, no
  uncoloured frames; real window 266 → 9.4 MB, a 2.8 s blink → none), and one aggressive collection 2 s after a
  text-keeping walk rests gives the memory back: end-of-scene working set 202.6 → 134.1 MB, below the 155.7 MB of the
  build before 80e94eeb, with 80e94eeb's revisit win kept (0 gen-2s a revisit pass). Question 72 not needed. The
  orchestrator corrected DECISIONS.md's monitor (the 4K at 150 %, not a 2560×1440 at DPI 144). For PERFORMANCE.md: a
  forced compacting gen-2 frees the heap but keeps committed memory; only `GCCollectionMode.Aggressive` gives it back:
  run it once at rest, between frames. After restoring a reverse edit, rebuild the test project (`--no-build` runs the
  app DLL copied into the test bin). Next: step 3.
- 2026-11-19: round 17 step 3 landed (a787c194; captures 23dd0de4): a diff's old version takes the lines it shares with
  the new one: −32 % lines walked over the last 200 commits' big files, 0 of 379 pairs differ (fresh and resumed);
  bigrepo Commit open 293.7 → 168.5 MB with colours ~1.3 s sooner in the real window. The desk changed: only the 1440p
  (100 %, 200 Hz) is attached, the 4K is not (read the desk again before each real-window step). For PERFORMANCE.md:
  TextMateSharp's `StateStack.Equals` is not enough to reuse a line's tokens (it ignores a begin that matched to the end
  of its line, which changes `\G` on the next line). Next: step 4.
- 2026-11-19: the user answered: question 69 → (b) (the player bar's bounds, with the one-level change; step 6), 70 → the
  recommendation ((b) now, step 4), 71 → named the Jellyfin account to browse as (kept in memory `aperture-jellyfin` and
  in briefs only: the repository is public).
- 2026-11-19: round 17 step 4 landed (b3bb93bd; captures ef113cef): a file's colours landing recolours the diff rows on
  screen instead of making them again: Junction first file visits −29…−35 % (real window 733.1–868.2 → 479.8–613.4 KB),
  the landing 334.5–347.3 → 79.9–93.7 KB with 0 rows made, a review's landing keeps the focused control (Waiting
  junction-review-focus-colours closed). Question 70's route (a) would add −25…−32 % and one frame less a first visit
  with the diff shown later: not built. The 4K was attached again during the step (read the desk before every hold).
  For PERFORMANCE.md: a virtual row costs ~0.6–0.8 KB an element plus ~0.9 KB a selectable label: keep elements per
  row few. Next: step 5.
- 2026-11-19: round 17 step 5 landed (3ea96588, 6751a51a; captures b5ab4704): each Junction view tab is its own part
  lit through a `Computed`, so a switch builds two tabs, not the toolbar (real window History 174.0 → 127.1 KB, Overview
  100.6 → 53.7, Tags 140.2 → 93.3, Commit 421.5 → 374.6); Vesper with both panes on one folder no longer builds on a
  cursor key while the other cursor is on a file (17.5 → 2.2 KB a key). Question 72 asked (a file ringed as its own
  F3 partner). For PERFORMANCE.md: for a one-of-N selection (tabs, segments) give each item its own part read through
  a `Computed` of the selection, so a change builds only the two items that moved; a Key on a part's root does not
  make its host replace it. Next: step 6.
- 2026-11-19: round 17 step 6 landed (0a7bb324, c23edfb9; captures fe46c02b): the video player's bar fades through a
  layer of its own size (the Slider and the bar's glyphs report paint bounds; a hidden subtree that only paints is
  skipped): first-fade DXGI local +12 / +24 / +32 MB → +0…+2 MB on Vulkan and GL, with real mpv too; scene-end local
  −4…−20 %, private −11…−18 %; only 2 pixels of the full-screen icon differ by 1/255 during the fade (question 69: (b)).
  The gated UiTests guard needed a 4th hold (the orchestrator allowed it): +2.0 MB with the change, +12.0 under the
  reverse edit, GL and Vulkan. The user answered question 72: (b), never pair a file with itself in Vesper → step 11.
  Lead: `Window.SetRoot(component.Mount())` never mounts the component (its `OnMount` never runs). For PERFORMANCE.md:
  one element with unknown paint bounds makes its faded ancestor window-sized (on GL with mpv it kept 24 MB); number
  every probe launch within a hold (a reused label overwrites its log). Next: step 7.
- 2026-11-20: round 17 step 7 landed (13fee514; captures b5ac3235): a Linux reader that starts after the app now hears
  the next focus move (AtSpiRig: a cursor walk after a late attach 0 → 2 signals a move, eager and served), as the UIA
  bridge did; no signal at the attach (Orca reads the focused state when it starts). For PERFORMANCE.md (testing): a
  served-list probe on synthetic rows sends no selected states and understates selection signals; use real string
  lists. Next: step 8.
- 2026-11-20: round 17 step 8 landed (e790d321; captures 91721d06): a wrapped height ask adds the lines up without making
  a layout (internal, no API): Junction's heights pass 40 → 2.9 KB a Commit step and 101 → 4.4 a Changes step in the
  real window, the prose Commit step 70.3 → 31.8 KB and Changes 131.3 → 34.0 (−55…−74 %); 0 of 2,011,835 sizes differ.
  Together with step 1, a wrapped Junction resize step is now about a quarter of round 16's. Found on the way: Fedora's
  `JunctionAppHostTests.TheBranchesViewShowsABranchCreatedOnDisk` fails 3 of 3 at 13a3e123 (it passed in step 5's Fedora
  run at b5ab4704, so step 6 or 7 or the environment): a fix agent bisects it before step 11 (r17fix). For
  PERFORMANCE.md: a height-only text ask must not take a drawn row's layout away (`TextLine.RunsMade`); a zero-allocation
  test of a re-break warms the same widths first. Steps 9 and 10 (measure-first leads) stay queued for round 18's
  survey: the weekly budget is at 81 % with ~2 days to its reset, kept for step 11, the full suite and the release.
- 2026-11-20: the Fedora failure was older than round 17 and a real Linux bug in Junction (9f9518e2): a ref made in a new
  folder (`feature/x`, a tag in a new folder) was never heard, because inotify starts watching a new folder only after
  it hears it made and the refs watcher dropped folder events; it now asks for them. Junction.Tests 1,183 of 1,183 on
  Fedora and Windows. For PERFORMANCE.md (testing): a test that waits on the file watcher can pass on Windows and race
  on Linux; run a Fedora failure alone three times at the last commit known to pass before bisecting. Next: step 11.
- 2026-11-20: round 17 step 11 stopped before any change, as its brief said: F3 is "Compare files", and with no partner
  it opens the diff as "Right file missing" with a copy button, not the file, so question 72's (b) cannot be built as
  asked. Question 72b put to the user with three ways (drop only the ring; F3 does nothing there; F3 opens the viewer)
  [(2)]; step 11 waits (Waiting vesper-file-not-its-own-partner). Round 17's build order is done (8 steps landed, the
  watcher fix, steps 9–10 queued for round 18). Phase → the full suite.
- 2026-11-20: main had moved by 18 commits (plan 19: the digit table with punctuation, right-to-left runs kept whole,
  the pipeline's shaper call counts; Junction's Changes load under index moves; WebView middle-click autoscroll); merged
  into the branch as 45cbde7b, no conflicts (TextPipeline.cs, GitWatcher.cs, JunctionApp.cs, DECISIONS.md auto-merged),
  the merged tree built. Round 17's full suite at 45cbde7b, 02:10–02:28: Windows `HultUI.slnx` 30 projects, 0 failures
  (Junction 1,188 in 10 m 5 s, Core 5,212, Headless 1,822, Text 324, Skia 471); Fedora the slnf (22 projects) and
  Junction 1,188, Meridian 680, Aperture 370, Reel 53 each in a private D-Bus session, 0 failures; docfx 0 warnings, 0
  errors. Phase → release.
- 2026-11-20: round 17 RELEASED. Merged into main e799c23b and pushed (origin/main 3c6d6a5b in the branch, tree identical
  to the tested branch but the ledger; the primary checkout fast-forwarded behind the guard). CI run 44653899561 green on
  the first attempt (test, test-linux, docs, meridian-keyring on Ubuntu and macOS); "Push on main" green. Round 17 in
  sum: 8 build steps landed, a Junction Linux watcher bug fixed on the way, steps 9–10 queued and step 11 waiting for
  question 72b. Real-app results: a wrapped Junction resize step about a quarter of round 16's (steps 1 and 8), a save
  that leaves a diff's texts as they were keeps its colours (288.7 → 9.4 MB an event), Junction's big-file working set
  202.6 → 134.1 MB, big multi-hunk diffs coloured with 32–45 % fewer lines walked, first file visits −29…−35 %, view
  switches −27…−47 %, the player bar's first fade 12–32 → 0–2 MB of GPU memory. Usage 65 → 85 % of the week. Round → 18,
  Phase survey, in a fresh context after the weekly reset.
