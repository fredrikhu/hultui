# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 12
- **Phase:** survey — round 12 running (started 2026-11-12 in a fresh context, session 0bab04ae): the eight experts
  survey in their own worktrees, in real windows too, ranked by real-app effect (common brief `brief-r12-survey.md` in
  session 0bab04ae's scratchpad). When all eight reports are in, merge them here and set Phase `build`. Round 11
  released: merged into main as b0dd1954; CI run 43121085428 green (the first CI on .NET 11 and `global.json`). No app
  releases this round (the user did not ask); the installers' new .NET 11 publish paths are first exercised by the next
  release tags. To resume in a fresh context, run in this worktree: `/loop Run the next step of the performance team: follow docs/plans/17-performance-team.md, with
  state in docs/plans/17-performance-team-ledger.md. Read both once per session (afterwards only the ledger's Phase and
  the part the step needs), do the step the ledger's Phase calls for, record it in the ledger, and commit.` Before
  resuming, `git fetch` and merge main if it moved; build steps use `scripts/perf/step-ab.ps1` (with `global.json` a base
  tree picks its own SDK). Open for the user, not blocking: the live screen-reader check of menus; another F16
  held-drag run (Waiting) only with a new approval. Older rounds are in [the archive](17-performance-team-ledger-archive.md)
  (search it, don't read it whole). History note: rewritten 2026-10-25 (every commit after 3353ce47 has a new ID; a
  pre-push hook refuses pushes built on the old history).
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`)
- **Review commits since:** round 12's survey reviews round 11's build, 5731ee58..b0dd1954 (the last survey report to the
  round 11 release; it includes the .NET 11 upgrade's step 0 only as already reviewed). Earlier: round 11's survey reviewed round 10's build and the .NET 11 upgrade, f4d6050d..868e2ffc
  (the last survey report to the upgrade's captures; round 10's merge into main cc55bdcc is in the range). Earlier: round 10's survey reviewed round 9's build, 4919ac40..facced69 (the last survey report
  to the round 9 release; 82 commits, 81 not merges; no merge from main this round). Round 9's survey reviewed
  eaa1ec29..10acc8c0 (round 8's build, with main's merge 3a15b5b1); round 8's survey reviewed 17d4220a..df45b4a2.
- **Probe code:** round 12 surveys in session 0bab04ae's scratchpad
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
| 1. Layout | 11 | no | none (221 shared suite rows: no layout counter moved ≥ 3 % on .NET 11; app walks equal to round 10, KB −0.1…−1.1; `DOTNET_TieredCompilation=0` adds nothing). New: scroll columns measured once per forced height → step 4 |
| 2. SkiaSharp: painting | 11 | yes | none (paint counters within 5 % on 221 rows; 102 rows render 0 B on JIT, TC=0 and AOT; every paint-side key shape 0 B a call after 1.2 M warm-up calls; the two slower AOT TextArea rows draw exactly what they drew on .NET 10) |
| 3. GL and Vulkan | 11 | yes | none (steady 0.0 B and idle 0 wakes on GL, Vulkan 8-bit and F16, JIT ± PGO and AOT; JIT drag frames −96 B on .NET 11; held resize with a post 1 frame a second; hidden page −17 % GL / −32 % Vulkan GPU; 30 guarded launches + 14 UiTests windows on the 1440p at 100 %, 0 foreground takes). One F16 held-drag stall → question 37 |
| 4. .NET runtime | 11 | no | **207fbe25** (the upgrade, AOT): ILCompiler 11 no longer devirtualizes `IAlternateEqualityComparer` in `Dictionary.AlternateLookup` (2 interface dispatches a lookup; reproduced outside HultUI) → step 1. JIT app walks net10 = net11 (56 walks, 0 rose, geomean 0.983); no other nullable-struct `HashCode` or boxing pattern on a hot path |
| 5. Text pipeline | 11 | no | none (48,000-layout hash 7A18AF8DE8E05271 and both 240,000-layout corpora F09E9038726E4BD6 unchanged on .NET 11; 92 text rows at or below round 10, TC=0 too). Found the AOT regression's cause → step 1 |
| 6. Reactivity and reconcile | 11 | no | none (only b2fdcf52 in the area; generators 0 warnings on SDK 11 and 0 B a read/write for 18 types on .NET 11 and 10; suite component/a11y/input rows 0 up) |
| 7. Accessibility bridges | 11 | no | **5e93a4fe** with `KeepUnchangedRows`: an item whose label changes off screen is never told (Meridian "seen 1×" stale 90 of 90 frames) → step 2. Audit 1 STALE in 3,549 (that item), 0 WALK-VALUE/EVENT, fallbacks 10; UIA identical on .NET 10/11 (JIT, TC=0, AOT); AT-SPI ±10 B a signal |
| 8. App workloads | 11 | no | none (Vesper +1.5…+7.9 % between rounds is the machine: the same binary reproduces it across days; same day all equal). Round 10 helped the apps: 69 walks geometric mean 0.674, KB a key sum 1,919,189 → 38,495 (−98 %; Runner −98.6 %, Sync 2,568.6 → 0.5, response drag −90.9 %, map click −71 %). Render 90 % of headless UI cycles (median) |

## Queued candidates

Rounds 1–11's build orders all landed but round 6's step 8, round 10's step 14 and round 11's step 1 (c), measured
and declined, and round 11's steps 13 and 14, measured with nothing to change (rounds 5–11 are in
[the archive](17-performance-team-ledger-archive.md#round-11-build-order), with each survey's closed-from-Waiting note). Round 12's build order
comes from its survey.
A build step may bundle one expert's related small candidates as separate commits. Items marked **(user)** wait for a
decision in Needs the user; the rest of their step goes ahead.

### Waiting

Re-ranked by the next survey.

- **live-reader check of menus** (user) — the six-step script in ACCESSIBILITY-PLAN.md (round 7 step 13) for Narrator,
  NVDA and Orca; a silent move would mean moving focus to the selected item (needs the user's approval).
- **junction-changes-leads** (6/7/8, reader behaviour, no perf number) — only the caret is told to a reader, not a
  multi-selection's other members; a reader's "select" on a folder row clicks it, which opens or closes the folder. (The
  perf parts were examined by round 8: below the bars.)
- **junction-review-old-path** (app bug, not perf; round 7 step 8a) — the review composer sends GitLab `old_path` = the
  new path for a renamed file, because `GitFileDiff.OldPath` is never set (`RenamedFrom` now holds git's old path).
  Decide whether review positions on renamed files should carry the old path.
- **dropdown-follow-control-on-resize** (1, correctness, low) — a Dropdown list or DatePicker calendar stays at its
  opening point when a resize moves its control; anchoring changes how a press on the control behaves. Not perf.
- **junction-leads (remaining)** (tests) — `SelectConflict` and a conflict row's right-click have no test of their own
  (not perf).
- **vesper-brief-filter-clear-reveal** (6/1, not perf; round 9 step 10) — clearing a filter with Backspace leaves
  Brief's cursor column out of view (the scroll is clamped to the narrower filtered layout; the sideways reveal has
  no apply-after-layout form like the vertical one); a full build does the same, no column goes blank. **Round 10
  verified the scrollbar half, worse than noted → question 28:** the scrollbar sets the offset with no `ScrollEvent`
  (Scrollbar.cs:161 track click, :190 thumb drag) and Brief makes columns only on one (CommanderApp.Views.cs:586): 400
  files, a track click to offset 2,197 shows 4 of 4 columns blank, a thumb drag 7 of 8 moves with a blank column, 0
  builds (r10-1).
- **findbar-countonscreen-foreach** (4, nit) — `CountOnScreen` loops `foreach` over `bar.Children` (32 B on AOT, 0.4 %
  of an 8 KB Enter); fix when FindBar is next touched.
- **terminal-noop-scrollback-wheel** + **terminal-row-strings-by-content** (2/Terminal, low) — a no-op notch draws a
  whole terminal frame (5,440–5,682 glyphs); 10,276 B a streamed line. Only the terminal sample uses HultUI.Terminal.
- **textarea-chunked-document** (5/4; the user chose option B on 2026-10-25) — parked: Meridian's 89k body typing is
  ~238–241 KB/key, most of it the TextArea's new string; the user's 256k-character condition is unmet.
- **a11y-served-items-followups** — a live Orca check only (no desktop Orca here; optional).
- **F16 kept drag targets** (3) — deferred since round 5's `nvlddmkm` errors; never repeat blind. Round 9: an F16 drag
  frame allocates 2,136 B JIT / 2,040 AOT against 8-bit's 136/40 (the per-size wrap kept targets would remove), drag only.
  Round 10: unchanged (~168 KB an 84-frame drag; the target's key includes the size, SkiaVulkanRenderSurface.cs:677–691).
- **gl-threaded-optimization-profile** (3; the user's driver setting, information only) — the walk lands while the
  window rests; the setting would remove only the per-frame spin (round 9: mean 357 JIT / 344 AOT, max 456/578
  `SwitchToThread` a paced frame; round 8: mean 340–387, max ≤ 986; round 10: mean 378/376, max 723/644).
- **text-open-measures-every-line (full)** (5; user: not now, 2026-10-13).
- **controls-menu-max-height-scroll** (1; not perf: no maximum height or scrolling; the Vulkan-drag half is gone since
  f3302209, round 9 expert 2), **layout-virtuallist-spacer-stale** (1; invisible; with an audit).
  Round 11 step 4: layout-virtuallist-spacer-stale confirmed wrong but invisible (the audit's 209 stale spacer
  heights, 208 at 0, and 138 held-row gaps); not fixed — marking the spacer puts a real-height box under the main
  pass and needs its own proof, and lists stay audit-unclean anyway (rows laid out as roots, 8.7k relative-Y diffs).
- **aperture-poster-as-hero**, **aperture-tmdb-size-fallback**, **junction-narrow-gitlab-glyphs**,
  **meridian-pane-choice-not-persisted**, **meridian-min-width-wraps** (apps; not perf; round 9: the Meridian two and
  junction-review-old-path still reproduce, the others' code is unchanged and was not exercised). Round 10: the same;
  min-width wraps only after a send (760×540: "Headers 6" wraps, Send's keycap and the response bar's end cut off;
  shots `r10-apps\drv\shots\min760*.png`).


- **aperture-cursor-guard-textarea** (6, low; found in round 10 step 5) — Aperture's cursor-key guard tests only
  `TextInputElement`, not `TextArea`; no harm seen in the issue reply. Check with the next Aperture keyboard change.

- **a11y-label-under-named-button** (7, lead; found in round 10 step 10) — a label under a name-from-content button,
  changed with no structure change, still builds a tree through the leaf patch (`NamedFromContentAbove`); it could fall
  back to the walk now that a renamed node with children walks in place. Measure at the next survey.
  Round 11 (r11-7): measured, 60 rows 16.74 → 0.08 KB a change, 1,000 rows 264.9 → 0.08 (trees 1 → 0, reader events
  identical); no app pays it (Vesper's 432 such changes all land in frames that walk anyway, 0 refused). Prototype
  `r11-a11y\apply-proto.py` ready (it fails the 4 `A11yLeafPatchTests` that pin the rebuild); build it when an app
  has a button whose label changes alone.


- **syntax-shared-registry-rest** (5; found in round 10 step 12) — Typst (`.typ`/`.typc` reach 57 grammars), Julia
  (9.2 MB), PHP, Razor (`.cshtml`) and HTML files still use the shared registry with a fixed set of grammars. Measure at
  the next survey if an app opens them.
  Round 11 (r11-5), first file in its own process, KB kept: `.typ`/`.typc` +42,419 (57 grammars, 117,238
  allocated; the largest single retention now, ×1.4 Markdown's before step 12), `.jl` +9,183, `.php` +4,449,
  `.cshtml` +4,567, `.html` +3,078. Typst is worth step 12's fence-scan route if the apps meet it → question 42;
  the rest stay parked.
  The user, 2026-11-11 (question 42): no Typst files — skip; the whole item stays parked.
- **junction-chromeparttests-load** (tests; round 10 step 12) — two `ChromePartTests` failed once in a full run while a
  four-way corpus scan loaded the machine; passed alone and on a quiet machine. Their build counts are load-sensitive:
  watch at the full suite and in CI.


- **fedora-local-dotnet** (environment) — remove `/usr/local/share/dotnet` and the `/usr/local/bin/dotnet` link in WSL
  Fedora once Fedora ships the final `dotnet-sdk-11.0`; `wsl-test.sh` takes `DOTNET_ROOT` from the dotnet on PATH
  since 67105a0b, so it follows.



- **vesper-folder-nudge-stale** (6 → 8, lead; r11-6 Examined) — `NudgeWhenConnectingIsDue` (CommanderApp.cs:631) tests
  only `Loader(side).IsLoading`, not that it is still the load it was set for: fast repeated navigation may pay one view
  build (~30–34 KB of 280–313 a key). Check in a real window.
- **junction-held-key-bigfiles** (8, lead; r11-8 Examined) — a held key over 40 files of 8,000 lines at a 33 ms pace:
  2,367–3,110 KB a key on all threads against 585 on the UI thread, 0.65–0.85 git shows a key; the headless clock can't
  judge a real key repeat (the worker and `Task.Delay(HighlightRest)` run on real time). Measure with `JUNCTION_BENCH`
  before calling it a candidate.
- **a11y-item-added-past-end-offscreen** (7; found in round 11 step 2) — an item added past the end of an item-rows
  list, off screen, is still not told to a reader (applied tree 200 items, a fresh build 201, 0 walks); a fix needs a
  guard for lists over 2,000 items (labels read live). Also: a label delegate made anew each build drops the kept
  labels, so step 2's check doesn't cover those lists. Next survey.
- **f16-held-drag-post-stall** (3; survey stall once, round 11 step 13's one approved re-run clean) — cause unknown.
  The re-run opened on the 4K at 120 Hz; the stall was on the 1440p at 200 Hz. Another run needs a new approval
  from the user and should place the window on the 200 Hz display. No fix: no evidence points to one.
- **meridian-narrow-editor-fedora-fonts** (tests; found in round 11 step 3) — on WSL Fedora, Meridian.Tests alone fails
  `NarrowEditorLayoutTests.Tab_titles_…` ("chain sub-tab shows 61.6 of its 66.2px"), on the base commit too: Fedora's
  fonts. CI's Linux job doesn't run Meridian.Tests. Pin the font (DejaVu, as the goldens do) or measure the title
  with the face the test gets.

## Landed

Round 11 (the round round 12's survey reviews; it moves to the archive at round 13's survey). Rounds 1–10 are in
[the archive](17-performance-team-ledger-archive.md#landed-rounds-14) ([round 5](17-performance-team-ledger-archive.md#landed-round-5),
[round 6](17-performance-team-ledger-archive.md#landed-round-6), [round 7](17-performance-team-ledger-archive.md#landed-round-7),
[round 8](17-performance-team-ledger-archive.md#landed-round-8), [round 9](17-performance-team-ledger-archive.md#landed-round-9),
[round 10](17-performance-team-ledger-archive.md#landed-round-10)).

| Round | Commit | Candidate | Before → after |
|---|---|---|---|
| 11 | 6403f8fe, 207fbe25, 67105a0b, c177a16c, c99e23bf, 868e2ffc | dotnet-11-upgrade (the user, 2026-11-11) | Everything targets .NET 11: one `HultUITargetFramework` (net11.0) in Directory.Build.props used by all 102 projects; a new `global.json` (SDK 11.0.100, latestFeature) that every workflow's setup-dotnet reads; installer, workflow and script publish paths; `wsl-test.sh` exports `DOTNET_ROOT` from the dotnet on PATH; guides and the two skills name .NET 11; `step-ab.ps1 -BaseSdk` and per-side target framework for the AOT wrapper. Fix found by the measurement (c99e23bf): .NET 11's optimized JIT boxes `HashCode.Add<T?>` in `TextPipeline.MetricDigest`'s loop (2–3 × 24 B a metric span on every styled layout; 12 JIT rows' allocKB up, styled wrap 74.8 → 225.7 KB) — plain values now, test fails on net11 with the old code (72 B a call). Measured ca31555d on SDK 10.0.300 / net10.0 against the tree on SDK 11.0.100 / net11.0, whole suite (230 rows), 2 pairs: JIT 0 slower / 0 faster, A/A 0/0, allocKB down on 2 rows (dropdown 38.4 → 36.8, wheel 4.4 → 4.2); AOT A/A 0/0, 2 slower (long TextArea document +13–15 %, wrapped TextArea resize +11 %; → Waiting dotnet11-aot-textarea-long-document), highlighted JSON −15 %; idle 0.2 passes/s, the 17 zero-KB rows still 0. AOT app walks (69 × 2): geometric mean 1.000, KB a key +0.3 %, none > 5 % (Vesper thumbs +4.8 %), builds equal, GCs gen0/1/2 123/17/8 → 121/15/8. Start-up (42 guarded launches, 0 foreground takes): Junction median 258.8 → 255.3 ms, Vesper 280.9 → 275.6, the others ±9 ms; working set flat (Junction 105.5 → 105.6 MB, Aperture 116.3 → 116.2). Exe sizes +0.2…+1.6 % (Junction 23.87 → 24.03 MB). Warnings: build 367 → 359 (SDK 10's ILCompiler NU1903 gone), AOT/trim 0 → 0 (Aperture's Kiota IL2104 1 → 1), docfx 0. Tests: Windows 30 projects, round 10's counts + 1 (Meridian's stalled-timeout flake failed both full runs → Waiting); Fedora 22 projects, 0 failures (Vesper zebra tint once, then 10/10); UiTests 130 pass (2 LiveResizeFrameTests fail in the full run on .NET 10 too → Waiting); mpv 29/29. Harness in this session's scratchpad `r11u\`. |
| 11 | 7d823b34, 6274c26c, 74397eb9, 8e34bf92 | text-word-cache-own-table + text-itemize-extends-below-marks (+ text-itemize-primary-glyph-per-char declined) | The objection to 207fbe25. (a) The word shape cache is a `WordCache` of its own (chained buckets, index-linked LRU, LruCache's rules kept) instead of `Dictionary` + `AlternateLookup` + `LinkedList`, which ILCompiler 11 no longer devirtualizes: AOT micro-probe 37.5 → 14.4 ns a lookup (ILC 10: 30.1 → 14.5); bytes kept a word 125.3 → 52.0 (−0.58 MB a pipeline, 8,192 fewer objects), allocated filling 166.6 → 104.0. The width cache stays (595 lookups a step, ~7 µs; DECISIONS.md). (b) `FontItemizer.Extends` skips `GetUnicodeCategory` below U+0300 (identical results, every code point swept). (c) A mask of the primary face's ASCII measured slower by 2–10 % (87 % of 116,260 asks a step repeat; DECISIONS.md). Whole suite, base 36ed22ae → 6274c26c, 2 pairs, min: AOT open a long document 3.924 → 2.657 ms (−32 %, 23 % below .NET 10's 3.466), wrapped resize 3.317 → 2.351 (−29 %), richtext/open −23 %, wrap 150 −21 %, 0 slower, 9 and 8 faster, A/A 0/0; JIT open 2.795 → 2.204 (−21 %), resize −20 % (pair 2's 8 a11y rows +12–18 % re-run alone: 0 slower, noise). allocKB 11 rows down, 3 up by the table doubling while it fills (section walk 11.8 → 13.0; 11.7 at full size); steady 0 B. AOT samples in the line walk 36,453 → 22,936; lookup cluster 29.8 → 20.2 %; write barriers 2,487 → 111. Corpora unchanged (48k 7A18AF8DE8E05271, 240k F09E9038726E4BD6). Text 302, Core 5,078, Headless 1,553, Vesper 929, Meridian 664, Junction 1,092 pass; WordCacheTests 8 (one a 20,000-op comparison with LruCache), the Extends sweep. |
| 11 | f77d0551, bb01b32d, 5177628d, 44a18f2f | tests that fail by clock or order (meridian-stalled-timeout-early, uitests-liveresize-order, uitests-workareas-before-dpi-aware, meridian-history-test-midnight) | Not perf. (a) Meridian's stalled-timeout bound is 400 − 17 ms (TimerQueue's interrupt clock fires up to one 15.625 ms step early against a Stopwatch, on .NET 10 too): 30/30 under 24 busy threads; the other lower-bound timer tests already have a step of slack. (b1) `GpuImageSourceGLTests` lets its image source go; both LiveResizeFrameTests assert `!GpuFrameSink.MaySignalOnlyByPosting` first (new `InternalsVisibleTo HultUI.UiTests`): both classes in one run 5/7 → 7/7, a leak now fails on a named message. (b2) one shared `Monitors.WorkAreas()` sets the thread per-monitor aware first (unprovable with only the 100 % monitor; 13 tests pass). Guard: handed back 0, never fired. (c) Meridian's History reads `_app.Time.GetLocalNow()` in all 11 places; its tests run at a fixed local noon (Fixtures/HistoryClock); new `JustAfterMidnight_TodayIsTheAppClocks` fails under a reverse edit (3 of 47). Full Windows suite 30 projects 0 failed (Junction 1,092, Vesper 929, Meridian 665, Core 5,078, Headless 1,553, Skia 387, Text 302, Aperture 362); Fedora cross-platform slnf 0 failed; Meridian.Tests alone on Fedora 1 of 669 (NarrowEditorLayoutTests, fonts, fails on the base too → Waiting). |
| 11 | 9fcf3696, a72ed576, 66803c43 | layout-scroll-forced-height-measure (+ layout-audit-padding-border) | (audit) `HULTUI_LAYOUT_AUDIT=<dir>` (internal `LayoutAudit`, ENVIRONMENT.md): after each outermost pass the tree is laid out again from scratch and every differing box logged, padding, border and margin included. (change) `ScrollsDown` in `LayoutEngine.MeasureSize`: a column with `OverflowY == Scroll` measured at a forced height reuses its unforced measurement at that width (not grids, percentage or min/max heights, aspect ratios); the sideways mirror had 0 calls anywhere (DECISIONS.md). MeasureSize a Meridian map click 869 → 245 (60 endpoints), 941 → 257 (600), 943.9 → 259.9 (2,000), rows flex-measured 912 → 152; suite 49 rows down (6 halved: worker tick 162 → 56, selection walk 407 → 181, key press 191.8 → 91.1), 12 up by 1–2; Junction commit Down 85.5 → 17.3, branches 97.5 → 46.3, files search 518.8 → 261.8; Vesper same names 141.3 → 81.4. Map click time (JIT, idle-gated, alternated ×2, best min) 1.233 → 1.033 ms (−16 %) at 60, −16 % at 600, −19 % at 2,000 (undoes round 10 step 11's +14 %); step-ab whole suite JIT and AOT 0/0, A/A 0/0 (layout phase −5…−20 % on the halved rows, a small share of their frames). Audit across all test projects: 14,833 differing of 4.52 M boxes with the change, 14,843 with it off, the same kinds (VirtualList rows laid out as roots, stale spacers, held-row gaps, test mutations); a deliberate fault gave 6,010 on Core alone. Pixels and map hashes identical. Core 5,081, Headless 1,553, Junction 1,092, Vesper 929, Meridian 665, Aperture 362, Reel 51, Fluent 34 pass; `ScrollForcedHeightMeasureTests` (13 measurements instead of 1 under a reverse edit). |
| 11 | 635d6c53, a5f0f906, b3906200, e280bd82, b013a4d6 | apps-refs-hold-last-view-rest | Retained KB after a full GC with the other view shown, JIT (AOT): Aperture `_cursorCard` emptied in OnAfterBuild once its card left the window (IsHosted): Home after 3 moves → Settings 1,212 → 948 (1,213 → 951), grid 600 after 5 moves 1,485 → 1,221 (1,517 → 1,239) — the menu key in Settings now passes on instead of opening a menu for a card of the view left; Meridian History empties `_historyListRef`, `_historyFieldRef`, `_historyArchiveRef` in builds that show another view (the archive part rebuilds on a new host when History returns): 200 sends → Requests 1,101 → 784 (1,109 → 793), 2,000 → 1,495 → 1,178, → Discovery 1,115 → 799; Vesper `_thumbVListL/R` let go in LetGoOfLeftViews, height kept as `_thumbListHeight`: 400 files → Detail 1,510 → 1,232 (1,518 → 1,250), 3,000 2,213 → 1,935. Every old view dead after the switch; path search on 11 switches: no holder left. Bytes a key, a build and an idle frame identical. Suite JIT pair 1 6 rows slower, pair 2 0 (noise), AOT 0/0, A/A 0/0, no allocKB moved. Tests +1 an app (each fails reverted): Aperture 363, Meridian 666, Vesper 930, Junction 1,092, Headless 1,553. REACTIVITY.md: a ref holds its view even unread; a ref a part's build sets must not be emptied in every build. |
| 11 | 4cced183, 89fec6c2 | a11y-item-rows-offscreen-label (question 38: yes) | The objection to 5e93a4fe. On an item-rows patch the list drops each kept label whose item changed or is past the end; when such an item has no row the patch makes again, it sends a Name change, so the frame walks in place (rows made again keep their old path — the first version's walk on on-screen changes made Junction's Branches checkout fall back to a full build). Meridian, an endpoint seen again off screen: stale frames 90 → 0, walks 0 → 1 (Name ×1), 0 trees, 2,242.1 → 2,218.6 KB; on screen unchanged. Audit, 7 suites: STALE 1 → 0, WALK-VALUE/EVENT 0, fallbacks 10 → 10, walks 7,010 → 7,050. Suite AOT 0/0 both pairs; JIT pair 2 one row (+17 %, the A/A shows −15 %: noise); no allocKB moved. `AChangedItem_IsToldToAReader_OnScreenOrNot_WithNoTreeBuilt` (fails reverted); Core 5,081, Headless 1,556, Accessibility.Linux 104, Vesper 930, Meridian 666, Aperture 363, Junction 1,092 pass. ELEMENTS.md. |
| 11 | 6fb8ff07, a111ddc6 | apps-meridian-body-formatted-again (question 39: yes, the last 4) | Meridian keeps the last 4 finished bodies (text and colour) by tab, response and pretty/raw, least recently shown out first; dropped when a send or the Runner's "open response" replaces the tab's response, the tab closes, another workspace opens, or `Application.Theme` changes; closing a tab also lets go of its body on screen, the find bar's binding and the loaders' last values. Tab switch HTML + JSON, all threads: to HTML 51,144 → 249 KB (AOT 52,277 → 249), to JSON 6,252 → 183; UI 316 → 214; builds 6 Part + 1 → 4 Part + 1; GCs 34/5/1 and 35/9/2 → 0/0/0; XML 10,839 → 230. Pretty/raw toggle AOT: HTML 27,375 → 89, XML 6,022 → 78, JSON 5,933 → 40; Part builds 2.5–3 → 1; GCs → 0. Typing unchanged. 4 large pretty bodies kept: 4,394–4,641 KB; all freed after closing the tabs. First frame after a switch back is the coloured body (images `r11b6\png\`, sent to the user); settled frames identical. Suite 0/0 JIT and AOT, A/A 0/0. `KeptResponseBodyTests` 8 (each fails reverted); Meridian 674, Headless 1,556 pass. |
| 11 | 1c7dc920, 1879ba78, 1fcbd1b7 | reactivity-meridian-map-list-draws-selection (question 40: images first, then (c)) | **Public `RowHighlight.BorderThickness`** (`Thickness?`, init-only, null by default): a per-side ring width, drawn as a box draws a per-side border; the one-width form unchanged (same constructor and Deconstruct, no new overload). The map list draws its selection (Raised fill, Radius1, the 2 px accent — on the **top** edge: `Thickness(2,0,0,0)` is Top, Right, Bottom, Left; the survey misread it as left) and hover. Images base vs after (rest, sel3-hover6, sel3-hover3, sel8-hover3 at 600 endpoints) byte-identical; pixel hashes after 8 clicks identical at 60/600/2,000 over 4 runs. Walk a key at 600/2,000, JIT: 30.6–31.2 → 22.0 KB (−28 %), render 8.4 → 0, rows made 2 → 0, layout visits 70.3 → 20.3, measure misses 49.9 → 37.0; AOT 36.6 → 28.0 (−23 %). Click, idle-gated JIT: 600 endpoints 22.1 → 13.6 KB, min 1.08 → 0.95 ms (−12 %); 2,000 min 1.15–1.24 → 1.01–1.02 (−11 %). Reader tree and events identical (4,071 lines). Suite no allocKB moved, AOT 0/0, JIT one row each way in single pairs (noise). Core 5,084, Headless 1,558, Meridian 674, Vesper 930, Junction 1,092 pass; a public-API test, a per-side pixel test at 1× and 1.5×, `AClickMakesNoRow_HoweverLongTheMap`. ELEMENTS.md; docfx 0 warnings. |
| 11 | 91e5559c, db4b8f97 | syntax-merged-theme (question 26: yes; colours change on purpose) | `MergedOptions`/`MergedTheme` fold a theme and the file it includes into one theme (included rules first, the theme's own after, its GUI colours on top); `RootedOptions` wraps the merged theme (the PowerShell/Less rule still last); a theme with no include is handed over unchanged. Three of 21 themes include another (Dark+, Light+, and High Contrast Light, which the brief missed: 0 lines change). Round 9's frozen corpus (3,094 files, 1,759,787 lines) token by token against the VS Code emulation: Dark+ 974,248 → 0 lines differ (JSON/JSONC/CSS keys #4EC9B0 → #9CDCFE; 13,108 `this`/`self`/`base`/`super` tokens #9CDCFE → #569CD6), Light+ 974,478 → 0, the other 19 unchanged; tokenizer states identical. Still differing from VS Code, out of scope: Monokai 166,353, Quiet Light 63,784, Red 17,264, Dimmed Monokai 6,870 lines (parent-scope rules TextMateSharp never applies). No golden changed. Dark+ load 1.67 → 1.59 ms, 4,519,568 → 4,277,080 B; a further registry 387 → 140 KB (the theme file read once a load); tokenize bytes identical. Suite no allocKB moved; three single-pair slower rows outside Syntax (noise; a stray `find.exe` held a core busy during the timings). ThemeColourTests +7 (6 fail reverted; the no-include case covers all 18); Syntax 111, Headless 1,558, Meridian 674, Junction 1,092, Vesper 930 pass. 30 before/after images (`r11b8\png\`), six sent to the user. DECISIONS.md, THEMES.md. |
| 11 | 7f6ade87 | tokenize-diff-public (question 10: yes) | **Public** `SyntaxHighlighter.TokenizeDiff(string path, string? oldText, IReadOnlyList<LineRange> oldShown, string? newText, IReadOnlyList<LineRange> newShown, int maxLines, CancellationToken cancellationToken, TimeProvider? clock = null)`, `readonly record struct LineRange(int First, int Last)` (from 1, both ends included; checked by TokenizeDiff, not a constructor, since `default`/`with` would get past it), `readonly record struct TokenizedDiff(IReadOnlyList<TextSpan>[]? Old, IReadOnlyList<TextSpan>[]? New, int LinesWalked)` (`default` = nothing coloured). Both range lists checked first (before line 1, ending before it starts, not after the previous → `ArgumentException` naming the parameter and range; touching allowed; null → `ArgumentNullException`). `InternalsVisibleTo Junction` dropped. XML docs; THEMES.md "Colouring code and diffs"; docfx 0 warnings. TokenizeDiffTests +11 (the reflection test and 7 checks fail reverted); Syntax 121, Junction 1,092 pass. No perf number. |
| 11 | bf9aa781, 1d9eaf92 | ondafterbuild-markdirty-dropped (question 41: (a)) | Not perf. REACTIVITY.md (lifecycle table, a paragraph with an example, a link from MarkDirty) and Component.cs's XML docs: a MarkDirty or a write to a signal the build read is dropped in OnBeforeBuild, Build and OnAfterBuild; use `Application.InvokeOnMainThread(MarkDirty)` or `RequestPostFrameCallback(MarkDirty)`; deliberate (Junction's find bar relies on it); a build inside another component's reconcile has its MarkDirty deferred, not dropped. Vesper's `RestorePaneScroll` makes one try (the 4-build counter is a bool); `ClosingTheViewerPutsAThumbnailPaneBackWhereItWas` (with the restore stubbed, the viewer, diff and Thumbnails tests fail); Vesper 931 pass. A Debug-only once-per-type note for a drop inside OnAfterBuild was proposed, not built. |
| 11 | 0ed79e8a, f0444f6e | findbar-count-after-bind (question 45: yes) | Bug, reproduced headlessly (Ctrl+F, a query): Junction a.txt (3 matches) → b.txt (1) showed "1 of 3" for "1 of 1", first frame and settled; Meridian pretty/raw: raw "1 of 2" with 0 hits, pretty "no matches" with 2; Meridian tabs (since step 6's kept bodies) the other tab's "1 of 1". Fix: `FindBar.Service` compares a new internal `TextSearch.Version` with the version the bar was built from and, if it moved, writes the count and its good/bad colour into the bar on screen with no build (nothing while closed); ELEMENTS.md. Builds a key 1.00 in both apps, unchanged; Junction Changes caret 311.6 → 311.8 KB, with the bar open 333.4 → 333.5 KB (count wrong on 10 of 90 keys → 0); Meridian find typing 65.9 → 65.9. Suite 0/0 JIT and AOT, no allocKB moved. 4 tests (FindBarTests, DiffFindTests, ResponseBodyWorkerTests, KeptResponseBodyTests), all fail reverse-edited; Core 5,084, Headless 1,559, Vesper 931, Meridian 676; Junction 1,092 of 1,093 (`ReviewTests.TheKeyboardReachesTheCommentAgain`, the known focus flake, 3/3 alone). |
| 11 | 4016e20b, 18772928 | suite-document-switch | Two suite rows (`Scenarios.DocumentSwitch.cs`; README; 232 scenarios, 205 off Windows): `component/cursor key over a 200-file list beside its document, text landing a frame later` (a VirtualList of 200 files and a read-only coloured TextArea of 180–420 lines through a KeyedAsyncLoader; the key posted so it lands inside the step's frame) and its twin `…, text in hand`. JIT, two idle-gated runs: landing 1.85/1.84 ms min, 198.4 KB, 2.0 builds; in hand 1.38/1.37 ms, 167.9 KB, 1.0 build — the second build and its frame ≈ 0.47 ms and 30.5 KB a key; AOT 1.86 / 1.42 ms, 198.1 / 167.8 KB; render 94–97 % (the new TextArea's first draw). Per-step checks (build count, a laid-out "Loading…" frame, text, spans, key, one row filled, a quiet next frame), each seen failing when broken. About twice Junction's Files walk a key (86.2 KB, 1.23 builds), since every key here opens an unseen file. Other rows unmoved (JIT 0/0; AOT one outlier row cleared by two more pairs). |
| 11 | (none) | f16-held-drag-post-stall, one diagnostic run (question 37: yes, one run) | **Not reproduced.** The one approved launch (JIT, Vulkan F16, a post waiting at every step) ran clean: exit 0 in 14 s, graceful WM_CLOSE, guards never fired, no dump. It opened on the 4K (scale 1.5, 120 Hz, 1200×800 px), where every survey run including the stall opened on the 1440p (scale 1, 200 Hz, 800×533) — the one visible difference. Step 0 274 ms (268 ms the first over-allocated F16 swapchain build, 1224×816 into 1280×1024); all 80 steps one frame each (median 3.6 ms), acquire, fence wait and present returning at once; 3 bucket builds (30–33 ms) and the exact-size build at the end (30 ms). Held 1 s without a post: 60 timer events, 0 frames; with a post: 63 events, 1 frame 15 ms after it (present SuboptimalKHR, the over-allocated swapchain kept, as designed). 0 `nvlddmkm`/System/Application events. Logging-only probe edits reverse-edited; nothing committed. Files `r11b13\runs\`. |
| 11 | (none) | atspi-aot (question 44: yes) | clang and lld 22.1.8-4.fc44 installed in FedoraLinux-44 (dnf added 21 packages, among them gcc/gcc-c++ 16.2.1, binutils 2.46.1, glibc-devel, make, and upgraded 13 to match: glibc 2.43-2 → 2.43-9, libgcc/libstdc++/libgomp 16.0.1 → 16.2.1, elfutils 0.194 → 0.197). The D-Bus probe publishes as NativeAOT on .NET 11 (linux-x64, 3.0 MB); no trim/AOT warnings from Tmds.DBus.Protocol 0.92.0 or HultUI.Platform.Accessibility.Linux rooted whole. Bytes a signal AOT (two runs) vs JIT: PropertyChange 169/168 vs 179–181, ChildrenChanged 175/180 vs 190, AddAccessible 226 vs 237–240, StateChanged 98/99 vs 101–104 (AOT 3–15 B less); handler side Properties.Get 1,152 vs 1,104 (+4 %, per client query), GetRole 888 vs 896; `CallMethodAsync` 504 both. 16/16 signals arrive and parse, 20/20 calls answer, an unknown path returns UnknownObject. No candidate. WSL probe folders deleted. |

## Examined, not worth it

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

No question is open from rounds 1–11. Their answered questions are in the archive
([round 11](17-performance-team-ledger-archive.md#needs-the-user-round-11),
[round 10, with round 9's 26 and the earlier 10](17-performance-team-ledger-archive.md#needs-the-user-round-10),
[round 9](17-performance-team-ledger-archive.md#needs-the-user-round-9), [round 8](17-performance-team-ledger-archive.md#needs-the-user-round-8),
[round 7](17-performance-team-ledger-archive.md#needs-the-user-round-7), [round 6](17-performance-team-ledger-archive.md#needs-the-user-round-6)). Search it by candidate id
before asking anything an earlier round asked. Recommendations in brackets.

Reminder (expert 8): Aperture signed in to jellyfin.home 3 more times this round as device
"aperture-survey"; the user should remove those devices and revoke the API key when the work is done.

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

From 2026-11-11 (after round 10's release); earlier entries are in the archive ([to 2026-10-25](17-performance-team-ledger-archive.md#log-2026-10-07-to-2026-10-25),
[2026-10-26 to 2026-10-27](17-performance-team-ledger-archive.md#log-2026-10-26-to-2026-10-27),
[2026-10-27 to 2026-11-02](17-performance-team-ledger-archive.md#log-2026-10-27-to-2026-11-02),
[2026-11-02 to 2026-11-03](17-performance-team-ledger-archive.md#log-2026-11-02-to-2026-11-03),
[2026-11-03 to 2026-11-08](17-performance-team-ledger-archive.md#log-2026-11-03-to-2026-11-08),
[2026-11-08 to 2026-11-09](17-performance-team-ledger-archive.md#log-2026-11-08-to-2026-11-09),
[2026-11-09 to 2026-11-11](17-performance-team-ledger-archive.md#log-2026-11-09-to-2026-11-11)).

- 2026-11-11: the user answered the open questions: 26 **yes** (a merged syntax theme to match VS Code) and 10 **yes**
  (`TokenizeDiff` public), both now Waiting items for round 11's build order (syntax-merged-theme,
  tokenize-diff-public); 33 **keep** (an unreadable request file stays counted in the Runner's rail). Only the live
  screen-reader check of menus stays open for the user.
- 2026-11-11: round 11 step 0, **dotnet-11-upgrade** (expert 4, one build agent, brief in this session's scratchpad
  `brief-r11u.md`): kept, 6403f8fe..868e2ffc (Landed). One `HultUITargetFramework` for 102 projects, a `global.json`
  (11.0.100) the workflows read, Fedora's `DOTNET_ROOT` from the dotnet on PATH; one .NET 11 JIT boxing regression
  found and fixed (c99e23bf). Suite JIT 0/0, AOT 2 TextArea rows slower (+11…+15 %, mostly the SDK's ILCompiler; →
  Waiting), app walks geomean 1.000, start-up −3…−5 ms, working set flat. Tests: Windows 30 and Fedora 22 projects green
  apart from Meridian's stalled-timeout flake (both Windows full runs; → Waiting, fix before the release). CI must still
  confirm the macOS keyring job, setup-dotnet reading global.json, docfx on an ubuntu runner with only .NET 11, and the
  installers' publish paths (at the round's release). Phase → survey.
- 2026-11-11: round 11 survey started (session ec6d577c, the same fresh context as step 0). Main has not moved
  (origin/main cc55bdcc is in the branch). Moved to the archive by a checked script (every moved line found there,
  nothing lost): round 9's 18 Landed rows, round 10's build order with its closed-from-Waiting note, round 10's
  answered questions 27–36 with round 9's 26 and the earlier 10, and round 9's log (2026-11-08 to 2026-11-09). The
  eight experts run in their own worktrees at dee48fbb on .NET 11; common brief `brief-r11-survey.md` in this
  session's scratchpad, probes in its `r11-<area>\` folders. Each re-ranks its Waiting items; experts 4 and 5 take
  dotnet11-aot-textarea-long-document, expert 4 also meridian-stalled-timeout-early and a sweep for other .NET 11
  tier-1 JIT allocations.
- 2026-11-11: round 11 survey done (reports 305025b3..5731ee58, `17-reports/r11-*`, ~1 h): painting and GL/Vulkan
  satisfied. Review objections: 207fbe25 (ILCompiler 11 lost the devirtualization of `Dictionary.AlternateLookup`'s
  comparer — found by experts 4 and 5 apart; the whole AOT TextArea regression) and 5e93a4fe (an off-screen item's new
  label never reaches a reader). Round 10 helped the apps (69 walks geomean 0.674, −98 % KB a key). Merged into a
  12-step build order (one owner each; virtuallist-selection-relayout split into a framework measure fix, step 4, and
  an app selection change, step 7), 12 Waiting items closed, questions 37–45 asked. The ledger's AOT note was corrected
  twice on the way (the "SDK share" was a .NET 10 servicing artefact, then the cause found). Phase → build.
- 2026-11-11: round 11 step 1 landed (7d823b34, 6274c26c; captures 74397eb9; declined 8e34bf92): the word shape cache
  has its own table (NativeAOT 11's lost devirtualization of `AlternateLookup` worked around) and the itemizer skips
  the category lookup below U+0300. AOT long document 3.924 → 2.657 ms (−32 %, below .NET 10), wrapped resize −29 %,
  rich text open −23 %; JIT −20…−21 %; 0 rows slower. For PERFORMANCE.md: `Dictionary.AlternateLookup` with a custom
  comparer is not devirtualized on NativeAOT 11 (2 interface dispatches a lookup); a per-call cache in front of a
  cheap bitset behind one interface call cost more than the call. Next: step 3 (step 2 waits for question 38).
- 2026-11-11: round 11 step 3 landed (f77d0551, bb01b32d, 5177628d, 44a18f2f; tests and Meridian's History clock): the
  stalled-timeout bound allows one timer-clock step, the UiTests' GPU image leak is closed and named, the placement
  tests read work areas per-monitor aware, History runs on the app clock with tests at noon. Full Windows suite 0
  failed; Fedora 0 failed apart from a pre-existing Meridian font test (Waiting). Memory ci-red-findings updated.
  Next: step 4 (step 2 still waits for question 38).
- 2026-11-11: round 11 step 4 landed (9fcf3696 audit, a72ed576 change; captures 66803c43): a scroll column measured at
  a forced height reuses its unforced measurement; Meridian map click MeasureSize −72 % and −16…−19 % time; Junction
  commit Down 85.5 → 17.3 measure calls. The layout audit is now committed (`HULTUI_LAYOUT_AUDIT`), with padding,
  border and margin. For the guidelines: prove a layout change with the audit against a run with the change off.
  Next: step 5 (steps 2, 6, 7 and 10 wait for questions 38–41).
- 2026-11-11: the user answered questions 37–45: 37–41 as recommended (one guarded F16 diagnostic run; tell a reader
  an off-screen label change; keep Meridian's last 4 views; map selection images first, then a public per-side
  `RowHighlight` border; document the OnAfterBuild drop), 42 skip Typst, 43 file nothing upstream, 44 install clang
  and lld in WSL Fedora for the AT-SPI AOT check, 45 fix the find bar count if it reproduces. Steps 13 (F16
  diagnostic) and 14 (AT-SPI AOT) added. Order after step 5: 2, 6, 7, 8, 9, 10, 11, 12, 13, 14.
- 2026-11-11: round 11 step 5 landed (635d6c53, a5f0f906, b3906200, e280bd82; captures b013a4d6): Aperture's cursor
  card, Meridian's three History refs and Vesper's thumbnail lists let go of the view left (−264…−317 KB, −18…−29 % of
  those retained sets; no holder left on 11 switches). Next: step 2.
- 2026-11-11: round 11 step 2 landed (4cced183; captures 89fec6c2): a reader hears an item-rows list item's new label
  when its row is off screen (stale 90 → 0 frames; audit STALE 1 → 0). Lead for the next survey: an item added past
  the end off screen (Waiting). Next: step 6.
- 2026-11-11: round 11 step 6 landed (6fb8ff07; captures a111ddc6): Meridian keeps its last 4 finished response
  bodies; switching back to a big HTML tab 51,144 → 249 KB on all threads and 0 GCs, and the first frame is the
  coloured body. Images sent to the user. Next: step 7 (images first, then the per-side `RowHighlight` border).
- 2026-11-11: round 11 step 7 landed (1c7dc920, 1879ba78; captures 1fcbd1b7): public `RowHighlight.BorderThickness`;
  Meridian's map list draws its selection with today's look (images byte-identical, so none sent): −28 % KB a click,
  −11…−12 % click time, 0 rows made. Next: step 8 (merged syntax theme, with images).
- 2026-11-11: the user: the next survey also measures with real windows (headless hides work a real window does) and
  favours the real apps over synthetic rows. Written into the brief: "Real apps first" under What counts, "Real
  windows too" under Phase `survey`, and the merge ranks by expected win in the real apps.
- 2026-11-11: round 11 step 8 landed (91e5559c; captures db4b8f97): one merged theme, so Dark+ and Light+ colour
  exactly as VS Code over round 9's 1.76 M-line corpus (974k lines each → 0 differ); no golden changed; images sent.
  The agent left a `find.exe` (PID 63688) holding a core; its Stop-Process was denied, so it is the user's to end
  (asked). Next: step 9.
- 2026-11-11: the user ended the stray `find.exe` (PID 63688) left by step 8's agent; the core is free again.
- 2026-11-11: round 11 step 9 landed (7f6ade87): `TokenizeDiff`, `LineRange` and `TokenizedDiff` public, the clock
  optional, ranges checked; Junction's friend-assembly line gone. Steps 10 and 12 (both expert 6, both the dropped
  rebuild) go to one agent as separate commits.
- 2026-11-11: round 11 steps 10 and 12 landed (bf9aa781, 1d9eaf92; 0ed79e8a, captures f0444f6e): the dropped rebuild
  documented and Vesper's dead retry gone; the find bar's stale count reproduced in Junction and Meridian and fixed in
  FindBar (no build added). Next: step 11.
- 2026-11-11: round 11 step 11 landed (4016e20b; captures 18772928): two suite rows for a list cursor that opens a
  document whose text lands a frame later, with an in-hand twin (the second build ≈ 0.47 ms and 30.5 KB a key). Next:
  step 13 (one guarded F16 held-drag diagnostic; dotnet-dump 9.0 is already a global tool).
- 2026-11-12: round 11 step 13 (no commit): the one approved F16 held-drag re-run did not stall (clean on the 4K at
  120 Hz; the survey's stall was on the 1440p at 200 Hz). Back to Waiting; another run needs the user's approval.
  Next: step 14.
- 2026-11-12: round 11 step 14 (no commit): AT-SPI on NativeAOT in WSL Fedora (clang and lld installed, as approved;
  dnf also upgraded glibc and the gcc runtime libraries to match): AOT allocates 3–15 B less a signal than JIT, the
  bridge works, no warnings, no candidate. All build steps done; the full suite runs next.
- 2026-11-12: round 11 full suite on 06c701d4 (src as after step 14): Windows `dotnet test HultUI.slnx -c Release` 30
  projects, 0 failures (Core 5,084, Headless 1,559, Junction 1,093 in 9 m, Vesper 931, Meridian 676, Aperture 363,
  Syntax 121, Reel 51, Fluent 34; UiTests 25 + 100 skipped, gate off); Fedora `wsl-test.sh` 22 projects, 0 failures
  (Core 5,083 + 1, Headless 1,390 + 83, Vesper 913 + 20); docfx `--warningsAsErrors` 0 warnings, 0 errors. origin/main
  cc55bdcc is in the branch. Phase → release.
- 2026-11-12: round 11 merged into main as b0dd1954 (detached origin/main merge in this worktree, tree identical to the
  tested branch; origin/main cc55bdcc was in the branch, so the push gate held). Primary checkout fast-forwarded behind
  the guard; branch reset to the merge and pushed. CI run 43121085428 started (the first on .NET 11).
- 2026-11-12: CI run 43121085428 on b0dd1954 green: test (~21 min), test-linux, docs, meridian-keyring (ubuntu-24.04,
  macos-15) — the first CI on .NET 11. Round 11 landed the .NET 11 upgrade (step 0) and 13 build steps (1–12, 14
  measured; 13 a clean diagnostic): AOT text lookups −32 % (below .NET 10), a reader told off-screen item labels,
  clock and order test fixes, a scroll column measured once per width (map click −16…−19 %), views let go (Aperture,
  Meridian, Vesper), Meridian's kept response bodies (51 MB → 0.25 MB a switch back), the map list drawing its
  selection (public `RowHighlight.BorderThickness`), the merged syntax theme (Dark+/Light+ = VS Code), public
  `TokenizeDiff`, the dropped-rebuild rule documented, the find bar's stale count fixed, two document-switch suite
  rows; the layout audit committed (`HULTUI_LAYOUT_AUDIT`). Round → 12, Phase → survey; the loop stops, and round 12
  starts in a fresh context.

- 2026-11-12: round 12 survey started in a fresh context (session 0bab04ae). Main has not moved (origin/main b0dd1954
  is in the branch). Moved to the archive by a checked script (`archive_r12.py`; every moved line found there, nothing
  lost): round 10's 16 Landed rows, round 11's build order with its closed-from-Waiting note, round 11's answered
  questions 37–45, and round 10's 161 log lines (2026-11-09 to its release on 2026-11-11). Housekeeping item closed:
  the VS collector service is stopped and step 14 deleted the WSL probe folders. Attached now: one monitor, the 1440p
  (the 4K is unplugged). The eight experts survey round 11's build, 5731ee58..b0dd1954 (50 commits, one merge: round
  11's own into main), in their own worktrees on this commit, in real windows too (the brief's "Real windows too").
