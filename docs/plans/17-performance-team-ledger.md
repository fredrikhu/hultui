# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 11
- **Phase:** survey — round 11. Its first step, the **.NET 11 upgrade**, landed on the branch 2026-11-11
  (6403f8fe..868e2ffc; Landed, round 11; not yet on main: it merges with the round). The survey profiles .NET 11 /
  SDK 11.0.100. Round 10 released: merged into main as cc55bdcc; CI run 42924747474 green; app releases
  junction-v1.25.0, vesper-v1.25.0, meridian-v1.8.0, aperture-v1.23.0, reel-v1.9.0. Round 11's survey first archives
  round 9's Landed rows, round 10's build order and answered questions 10 and 26–36, and round 9's log entries
  (2026-11-08 to its release on 2026-11-09), then spawns the eight experts. To resume in a fresh context, run in this worktree: `/loop Run the next step
  of the performance team: follow docs/plans/17-performance-team.md, with state in
  docs/plans/17-performance-team-ledger.md. Read both once per session (afterwards only the ledger's Phase and the part
  the step needs), do the step the ledger's Phase calls for, record it in the ledger, and commit.` Before resuming,
  `git fetch` and merge main if it moved. The common survey brief from round 10 is reusable: session 3484c586's
  scratchpad `brief-r10-survey.md` (see Probe code); build steps use `scripts/perf/step-ab.ps1`. Answered 2026-11-11:
  26 yes and 10 yes (now Waiting **syntax-merged-theme** and **tokenize-diff-public**, for round 11's build order), 33
  keep. Open for the user, not blocking: the live screen-reader check of menus. Older rounds are in [the
  archive](17-performance-team-ledger-archive.md) (search it, don't read it whole). History note: rewritten 2026-10-25
  (every commit after 3353ce47 has a new ID; a pre-push hook refuses pushes built on the old history).
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`)
- **Review commits since:** round 11's survey reviews round 10's build and the .NET 11 upgrade, f4d6050d..868e2ffc
  (the last survey report to the upgrade's captures; round 10's merge into main cc55bdcc is in the range). Earlier: round 10's survey reviewed round 9's build, 4919ac40..facced69 (the last survey report
  to the round 9 release; 82 commits, 81 not merges; no merge from main this round). Round 9's survey reviewed
  eaa1ec29..10acc8c0 (round 8's build, with main's merge 3a15b5b1); round 8's survey reviewed 17d4220a..df45b4a2.
- **Probe code:** round 10 surveys in session 3484c586's scratchpad
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
| 1. Layout | 10 | no | none (214 shared suite rows: no layout counter rose; app walks equal to round 9 apart from the landed drops; ff6125a6 at most 2 settles a step, 0 B). New: the wheel latch keeps a removed view alive → step 3 |
| 2. SkiaSharp: painting | 10 | no | none (every paint counter on 214 shared rows within 5 % of round 9; the line cull skips 1,836 lines a step unread; faded images and Sync rows 0 layers) |
| 3. GL and Vulkan | 10 | yes | none (steady 0 B and idle 0 wakes on GL, Vulkan 8-bit and F16, JIT and AOT; held resize with a post 1 frame a second; hidden page −15…−34 % GPU; 29 guarded windows at 1.5× on the 4K, 0 foreground takes, no new `nvlddmkm`) |
| 4. .NET runtime | 10 | no | **c940c643**: Meridian's kept tree rows keep the whole Requests view alive behind every other view (1,350 KB of 5,770 at 200 requests) → step 2. Suite 0 rows rose JIT or AOT; steady 0 B; idle unchanged |
| 5. Text pipeline | 10 | no | none (48,000-layout hash 7A18AF8DE8E05271 and both 240,000-layout corpora unchanged; 43 text rows flat or lower) |
| 6. Reactivity and reconcile | 10 | no | none (c940c643 flat with collection size; 25a96234 amortized O(1); reader-focus commits add no build) |
| 7. Accessibility bridges | 10 | no | **cb1df8c0 with ea42f45b**: Meridian's map rows are Buttons, so a selected-state change builds the whole tree with a reader (974 KB a click at 2,000 endpoints, ×8.1 the bridge-off cost) → step 1. Audit 0 STALE in 3,374, 0 WALK-VALUE/EVENT, fallbacks 126 → 82 |
| 8. App workloads | 10 | no | none (Junction diff find Enter +1.0 % bisected: bimodal noise). Round 9 helped the apps: 65 walks geometric mean 0.852, sum 20,186 → 11,239 KB a key (−44 %); Discovery click −97 %, Environments typing −99.4 %, 89k body typing −24 %, Aperture resize −49 %; Junction flat (no Junction commit) |

## Queued candidates

Rounds 1–10's build orders all landed but round 6's step 8 and round 10's step 14, measured and declined (rounds
5–10 are in [the archive](17-performance-team-ledger-archive.md#round-10-build-order), with each survey's closed-from-Waiting note). Round 11's
build order comes from its survey (the .NET 11 upgrade went first, as step 0: Landed).
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
- **uitests-workareas-before-dpi-aware** (tests, small; found running f9bceb6d's checks) — the placement UiTests read
  `WorkAreas()` before SDL makes the process per-monitor aware, so the first Borderless pass sees the 4K at 96 dpi
  (2560×1392 at 0,0); and every UiTests window is made on the 96 dpi monitor first (`scale=1` at creation), so the
  high-dpi cases are brief moves. Set the test thread per-monitor aware first (`SetThreadDpiAwarenessContext(-4)`, as
  the scratch check `r9-4k\ZzR9FourKVisibleTests.cs` did). Round 10: still true; a third file reads it early
  (CustomFrameSystemMenuTests.cs:47, b44b4db5); do it with the next UiTests change.
- **findbar-countonscreen-foreach** (4, nit) — `CountOnScreen` loops `foreach` over `bar.Children` (32 B on AOT, 0.4 %
  of an 8 KB Enter); fix when FindBar is next touched.
- **terminal-noop-scrollback-wheel** + **terminal-row-strings-by-content** (2/Terminal, low) — a no-op notch draws a
  whole terminal frame (5,440–5,682 glyphs); 10,276 B a streamed line. Only the terminal sample uses HultUI.Terminal.
- **layout-ordered-children** (1, low) — suite rows only (wheel over 5,000 rows 4,954 hit visits); apps at most 33.5 hit
  visits a move and 13 culls a frame. Round 10: Meridian's map is the first app case (618 hit visits a move, 579 culls
  a frame at 600 endpoints); step 11's VirtualList fixes more, so still not a framework change.
- **Suite scenarios** (8): document-switch (Junction Files 86.2 KB a key, 1.23 builds a key — the file's text lands
  and builds again — 149.1 measure misses a key, render 78 % of cycles). Round 10 adds two patterns, each with a
  kept-rows twin: a cursor key over a plain 200-row list rebuilt through the root (with step 7) and one row a frame
  appended to a 200-row list rebuilt through the root (with step 4).
- **textarea-chunked-document** (5/4; the user chose option B on 2026-10-25) — parked: Meridian's 89k body typing is
  ~238–241 KB/key, most of it the TextArea's new string; the user's 256k-character condition is unmet.
- **a11y-served-items-followups** — a live Orca check only (no desktop Orca here; optional).
- **F16 kept drag targets** (3) — deferred since round 5's `nvlddmkm` errors; never repeat blind. Round 9: an F16 drag
  frame allocates 2,136 B JIT / 2,040 AOT against 8-bit's 136/40 (the per-size wrap kept targets would remove), drag only.
  Round 10: unchanged (~168 KB an 84-frame drag; the target's key includes the size, SkiaVulkanRenderSurface.cs:677–691).
- **gl-threaded-optimization-profile** (3; the user's driver setting, information only) — the walk lands while the
  window rests; the setting would remove only the per-frame spin (round 9: mean 357 JIT / 344 AOT, max 456/578
  `SwitchToThread` a paced frame; round 8: mean 340–387, max ≤ 986; round 10: mean 378/376, max 723/644).
- **layout-audit-padding-border** (1, tooling) — compare `LayoutResult` padding and border too; with the next audit change.
- **text-open-measures-every-line (full)** (5; user: not now, 2026-10-13).
- **controls-menu-max-height-scroll** (1; not perf: no maximum height or scrolling; the Vulkan-drag half is gone since
  f3302209, round 9 expert 2), **layout-virtuallist-spacer-stale** (1; invisible; with an audit).
- **aperture-poster-as-hero**, **aperture-tmdb-size-fallback**, **junction-narrow-gitlab-glyphs**,
  **meridian-pane-choice-not-persisted**, **meridian-min-width-wraps** (apps; not perf; round 9: the Meridian two and
  junction-review-old-path still reproduce, the others' code is unchanged and was not exercised). Round 10: the same;
  min-width wraps only after a send (760×540: "Headers 6" wraps, Send's keycap and the response bar's end cut off;
  shots `r10-apps\drv\shots\min760*.png`).

- **apps-refs-hold-last-view (unmeasured holders)** (4; found in round 10 step 2) — Aperture `_cursorCard`, Vesper
  `_thumbVListL/R` (the next build reads its height), Meridian `_historyListRef`: measure at the next survey.

- **aperture-cursor-guard-textarea** (6, low; found in round 10 step 5) — Aperture's cursor-key guard tests only
  `TextInputElement`, not `TextArea`; no harm seen in the issue reply. Check with the next Aperture keyboard change.

- **a11y-label-under-named-button** (7, lead; found in round 10 step 10) — a label under a name-from-content button,
  changed with no structure change, still builds a tree through the leaf patch (`NamedFromContentAbove`); it could fall
  back to the walk now that a renamed node with children walks in place. Measure at the next survey.

- **virtuallist-selection-relayout** (1/6, framework; found in round 10 step 11) — a selection move in an item-row
  `VirtualList` lays out every realized row again (869 measure calls against 113 for the old column at 60 endpoints): a
  JIT click in Meridian's map +14 % at 60 endpoints (AOT within 1.5 %). Measure at the next survey.

- **syntax-shared-registry-rest** (5; found in round 10 step 12) — Typst (`.typ`/`.typc` reach 57 grammars), Julia
  (9.2 MB), PHP, Razor (`.cshtml`) and HTML files still use the shared registry with a fixed set of grammars. Measure at
  the next survey if an app opens them.
- **junction-chromeparttests-load** (tests; round 10 step 12) — two `ChromePartTests` failed once in a full run while a
  four-way corpus scan loaded the machine; passed alone and on a quiet machine. Their build counts are load-sensitive:
  watch at the full suite and in CI.

- **ondafterbuild-markdirty-dropped** (6, framework; found in round 10 step 15) — `MarkDirty` called from
  `OnAfterBuild` is silently dropped, so Vesper's `RestorePaneScroll` retry never runs (Detail and Thumbnails restore on
  the first build, so harmless today). A trap for app authors: document or make it work; measure at the next survey.

- **dotnet11-aot-textarea-long-document** (5/4; found by the upgrade step) — the one place .NET 11 got slower: AOT
  `long TextArea document` +13–15 % (3.466 → 3.984 ms min) and `wrapped TextArea resize` +11 % (2.916 → 3.246), both
  pairs. **Correction (2026-11-11, from the captures' runtime fields):** the "SDK share" is not ILCompiler 11. The main
  comparison's AOT before side ran .NET **10.0.8** (the runtime pack and ILCompiler SDK 10.0.300 brings); the split's
  before side (net10.0 built by SDK 11.0.100) ran **10.0.15**. So the split is 10.0.15 → 11.0.0 (+6 % and +4 %, under
  the bar: long document 3.686 → 3.912 ms, resize 3.079 → 3.212), and the rest, roughly +6 %, came with the .NET 10
  servicing update 10.0.8 → 10.0.15 that any .NET 10 app gets (inferred across two sessions' runs, not alternated in
  one; confirm with one alternated AOT pair, 10.0.8 against 10.0.15). JIT 0 slower (its before side ran 10.0.14). The
  survey's experts 5 and 4 profile the AOT path (codegen of the line walk, inlining).
  Captures `samples/HultUI.Samples.PerfSuite/captures/2026-11-11-c99e23bf-dotnet11-aot-{before,after}.json`; split
  runs in this session's scratchpad `r11u\ab-split\`.
- **meridian-stalled-timeout-early** (tests; found by the upgrade step) — `HttpEngineTests.AStalledServerIsATimeoutThat
  SaysItConnected` failed in both Windows full runs on .NET 11: the timeout fired at 388 and 399 ms against a ≥ 400 ms
  assertion (passes 8/8 alone, 5/5 in its project). Early, not late: the lower bound measures from a point after the
  timer starts, or .NET 11's timer fires up to ~12 ms early. Known flake since 2026-10-05 (memory ci-red-findings), now
  more likely; fix the assertion's start point (or a tolerance the timer's resolution explains) before the round's
  release so 2-core CI stays green.
- **uitests-liveresize-order** (3, tests; found by the upgrade step) — two `LiveResizeFrameTests` fail in the full
  real-window UiTests run on .NET 10 too (pass alone); GPU-sink tests that run before them likely leave a process-wide
  flag set. Pre-existing; fix with the next UiTests change (with uitests-workareas-before-dpi-aware).
- **fedora-local-dotnet** (environment) — remove `/usr/local/share/dotnet` and the `/usr/local/bin/dotnet` link in WSL
  Fedora once Fedora ships the final `dotnet-sdk-11.0`; `wsl-test.sh` takes `DOTNET_ROOT` from the dotnet on PATH
  since 67105a0b, so it follows.

- **syntax-merged-theme** (5, Syntax + every app that colours code; the user said yes to question 26 on 2026-11-11) —
  give TextMateSharp one merged theme (the included theme's rules folded into the main theme's lookup) so colours
  match VS Code: JSON keys #9CDCFE (today teal; every key in Meridian's JSON bodies), `this` #569CD6. Its own step,
  not folded into a perf step: before/after images of JSON, C#, TypeScript, Markdown and a Junction diff under each
  theme that includes another; update the golden images it recolours (`HULTUI_UPDATE_GOLDENS`) and say which ones
  in the step; check the colour-cache and theme-load cost with `step-ab.ps1` (a merge done once per theme load
  should cost nothing a key). Round 9 step 14b's ~974k-line corpus comparison is the test: differences from VS Code
  should fall to the ones explained elsewhere.
- **tokenize-diff-public** (5, Syntax; the user said yes to question 10 on 2026-11-11) — make
  `SyntaxHighlighter.TokenizeDiff` (SyntaxHighlighter.cs:205) public, with the two types it uses, both internal today:
  `LineRange` (the shown lines it takes) and `TokenizedDiff` (the spans it returns). Review the surface first: the
  required `TimeProvider clock` (`TokenizeLines` takes it optional), whether `LineRange` is inclusive at `Last`, the
  nullable arrays in the result. Then XML docs, a docs page entry (docfx `--warningsAsErrors` stays at 0), drop
  `<InternalsVisibleTo Include="Junction" />` from HultUI.Syntax.csproj if Junction then needs no other internal, and
  a public-API test in HultUI.Syntax.Tests. No perf number expected; small.

- **meridian-history-test-midnight** (tests; found 2026-11-11 by meridian-v1.8.0's installer run at 00:00:41 UTC) —
  `HistoryTests.AnOlderSendFadesItsColours_NotItsRow` (tests/Meridian.Tests/HistoryTests.cs ~:411) captures
  `DateTimeOffset.Now` and asserts `HistoryLog.IsToday`; across midnight the send and the test's "now" fall on different
  days. Use a fixed clock (e.g. noon) or an injected time; check other `IsToday`/`DateTime.Now` tests the same way.

## Landed

Round 10 (the round round 11's survey reviews; it moves to the archive at round 12's survey) and round 11. Rounds 1–9
are in [the archive](17-performance-team-ledger-archive.md#landed-rounds-14) ([round 5](17-performance-team-ledger-archive.md#landed-round-5),
[round 6](17-performance-team-ledger-archive.md#landed-round-6), [round 7](17-performance-team-ledger-archive.md#landed-round-7),
[round 8](17-performance-team-ledger-archive.md#landed-round-8), [round 9](17-performance-team-ledger-archive.md#landed-round-9)).

| Round | Commit | Candidate | Before → after |
|---|---|---|---|
| 10 | f451a9ef, 8c02c5a2 | a11y-selected-button-patch | The objection to cb1df8c0 with ea42f45b. A selected state is marked in place also for an element whose app-set `Role` is `Button` and that says selected or not both before and after (`element.Role is ListItem or Button`); an inferred button (`ButtonElement`, a clickable box), a button that starts or stops saying selected, tabs, radios and every other role still build a tree. Meridian map click, KB off / tree / UIA / AT-SPI, JIT: 60 endpoints 58.1 / 92.3 / 92.7 / 92.8 → 58.1 / 58.3 / 58.4 / 58.6; 600 81.5 / 343.6 / 343.9 / 344.1 → 81.5 / 81.8 / 81.8 / 82.0; 2,000 120.8 / 973.5 / 973.9 / 974.0 → 120.8 / 121.0 / 121.1 / 121.3; trees a click 1 → 0. A reader hears the same: ordered UIA raises and AT-SPI signals identical in all 90 reader-mode scenarios (Vesper 48, Meridian 30, Aperture 12); only the 9 map-click rows of 121 moved. Audit 0 STALE in 3,367, 0 WALK-VALUE/EVENT, fallbacks 83 (Junction 62 vs 61 with the rule reverse-edited: run-to-run; no Junction element reaches the clause). Suite `-Pairs 2` JIT + AOT 0/0, no allocation moved, all 67 a11y rows identical (captures 8c02c5a2). `A11yLeafPatchTests` +3 per bridge (a selected button patches; a Tab still builds; a button that starts saying selected builds; reverse edits fail 2/2); Core 5,067, Headless 1,505, Linux bridge 104, Meridian 653, Vesper 903, Aperture 356, Junction 1,082 pass. ACCESSIBILITY-PLAN.md, ELEMENTS.md. |
| 10 | b2fdcf52, c3cb789a, b837de41, 3194c830, 35102eb4, b6204f39 | dotnet-kept-elements-hold-weakly + apps-refs-hold-last-view | The objection to c940c643. (a) `KeptElements` entries are an internal `WeakElementRef` (a non-generic `WeakReference` implementing IRef, registered by an internal `Element.WithRef(IRef)`, repointed by PatchFrom): 0 managed bytes added a key (same 24 B object as `Ref<Element>`, plus one weak GC handle); `Made` 243.2 B a new key, 88.0 a known one, before and after. (b) Meridian empties `_respTabRef`/`_responseBodyRef` in a build with no response panel and `_urlRef` at each build's start; Aperture empties `_homeShelfCards` after Home's shelves and lets `_gridList` go in another view's build (height kept as `float? _gridViewportHeight`); Vesper's `OnAfterBuild` lets go of the cursor row and Brief's scroll view once unhosted (`_cursorRowL/R` is shared by all three modes; the offset is kept). Retained heap after the switch, full GC, KB JIT (AOT): Meridian 200 requests → History 2,364 → 1,027 (2,161 → 874); 25 requests 933 → 733 (773 → 585); a 239 KB response open → History 12,461 → 10,623 (12,263 → 10,476), held by the old view 1,852 → 5; Aperture Home 5×20 → Settings 1,528 → 1,013 (1,386 → 867), 8×40 1,994 → 1,180 (1,850 → 1,025); grid 600 → Settings 1,666 → 1,292 (1,539 → 1,163); Vesper Brief 400 files → Detail 1,825 → 1,406 (1,664 → 1,253), 3,000 2,646 → 2,214 (2,472 → 2,062); the old view dead after in every row. Bytes a key/build unchanged JIT + AOT. Suite `-Pairs 2` JIT + AOT 0/0, no allocation moved, A/A clean (captures b6204f39). Retention tests per change (reverse edits fail; Vesper/Aperture also without the kept offset/height); Core 5,067, Headless 1,507, Meridian 654, Aperture 358, Vesper 904, Junction 1,082 pass. REACTIVITY.md "A ref into a view keeps the whole view", ELEMENTS.md §28. |
| 10 | 1c3cf5c5, ef42ca03 | layout-wheel-latch-holds-removed-view | `WheelGesture` gets internal `Latched` and `Release()`; `NoteStructureForHover` → `NoteStructureChange`: at the end of a frame that changed the root's or an open overlay's children it releases a latched scroller `IsShownInWindow` rejects (before the hover early return); `RemoveOverlayAt` releases a latch inside the closed overlay and `SetRoot` one on the old root (a reused root has no Children flag). Retained KB after a switch, map wheeled, JIT (AOT): Meridian map 60 endpoints 1,122 → 841 (948 → 670), 600 4,314 → 1,815 (4,127 → 1,657), 2,000 14,283 → 6,052 (14,037 → 5,901), equal to the no-wheel walk; Aperture → Settings, grid 600 films 1,566 → 1,300 (1,430 → 1,164), Home 5×20 1,295 → 1,030 (1,158 → 894). Per frame: two null checks when nothing is hovered or latched; one O(depth) walk only in a frame that changed a tree while latched; 0 B. Suite `-Pairs 2` JIT + AOT: no allocation moved; AOT 0/0 both pairs, JIT pair 2 0/0, pair 1's 4 slower rows also in its A/A line (noise in the before run); 16 wheel rows within ~1 % (captures ef42ca03). ScrollChainingTests +5 (each release reversed alone fails its test; the check after the early return fails the pointer-gone case); Core 5,067, Headless 1,512, Meridian 654, Vesper 904, Aperture 358, Junction 1,082 pass. REACTIVITY.md. |
| 10 | 150d08bc, 47ea53fb, 96a54d45, 2ccde148, cf68adeb, 70025cb3, 5c73087d | apps-meridian-runner-rail-reads-every-request | (a) The Runner's rail counts its scopes from the scanned collection (no file read): KB a run JIT (AOT) 200 requests 1,799,085 → 189,878 (1,783,386 → 189,251), 50 123,095 → 19,258 (121,536 → 18,970); GCs at 200 107/19–22/2 → 11/1/1; rail 8,001 → 36.8 KB a build. An unreadable request file is now counted in "N req" / "Run · N req" but still left out of the run ("49 / 49" beside "Run · 50 req"; question 33). (b) Result rows on `KeptElements` keyed by request path, let go on a new run: 200 189,871 → 39,283 (−79 %; AOT 39,729), 50 −50 %, GCs 2/0/0; ~19 MB a run is the sends themselves. (b2) A result builds the Runner's results part alone (own revision): 200 39,286 → 24,302 (AOT 24,975), MeridianApp builds 202 → 2, part builds 404 → 4 plus 202 results-only builds; a run behind another view builds nothing there. All three: **1,799,085 → 24,302 KB a run at 200 (−98.6 %)**, GCs 1/0/0. (c) Chain sub-tab keeps chain nodes per scan, compared by reference: 80 URL keys at 200 requests 3,278 → 117 KB a key chained (−96 %), 2,919 → 74 unchained; CPU 13.5 → 1.3 ms JIT, 8.4 → 1.4 AOT; GCs 15/1 → 0; pixels and trees identical. Checkpoint walks (idle, start, results 1, n/4, n/2, n−1, end, a row expanded/collapsed; 50, 200, 50 with an unreadable file): element and a11y trees as d4551a06 and as a keep-nothing build; unreadable case differs only in the three counts. New suite rows `component/append a row to 200 rows built through the root, every row made` (0.921 ms, 1,494 KB/op) and `…, rows kept` (0.652 ms, 36 KB/op). Suite `-Pairs 2` JIT + AOT 0/0, no allocation moved (captures 5c73087d). Tests (reverse edits fail): counts vs files incl. unreadable, n results n rows (135 without), window builds only at start and end (17 without), Chain reads once a scan (14 without); Meridian 658 + 4 skipped, Headless 1,512, Core 5,067 pass. REACTIVITY.md. |
| 10 | 3e2b9c3c, b03e7b16, 45441b1a, f8d952c3, bce29c46, a7d3c7bf | apps-typing-builds-nothing-junction-aperture + apps-toast-gone-view-only + aperture-backspace-goes-back | KB a character JIT (AOT), elements, builds: Junction prompt over Changes 194.3 → 1.4 (192.9 → 1.4), 172 → 0, 5 → 0; over History 154.7 → 0.6; reword 332.8 → 1.0 (330.7 → 1.0), 320 → 0; add-repo folder 472.4 → 195.0 (468.9 → 192.6), builds 10 → 2, part builds 8 → 0 (the folder check's landing also `MarkViewDirty`: it had built the chrome a second time a character); MR filter 221.8 → 92.3, builds 4 → 1; Aperture issue reply 135.1 → 2.3 (134.5 → 2.3), 137 → 0; report message 99.9 → 0.9 (98.9 → 0.5); with a reader 1.1–2.9 KB, no tree. Toast flash to gone: Junction Changes 365.3 → 226.7 (369.8 → 231.7), part builds 8 → 4; Meridian 25 requests 270.4 → 212.2, 200 279.0 → 215.9; Aperture Home 148.5 → 90.1 (150.4 → 92.4). Bug (question 27 (a)): Backspace in an Aperture text field erases and stays; Escape and Backspace on the page still go back. 10 scenes (161 steps) identical as built, rebuilt and with a forced full build after each step, with and without a reader; equal to ae9b68ec but for the Backspace fix. Suite `-Pairs 2` JIT + AOT 0/0, no allocation moved (captures a7d3c7bf). Tests (reverse edits fail); Junction 1,085, Aperture 362, Meridian 659, Headless 1,512, Core 5,067, Vesper 904 pass. REACTIVITY.md (toast callback; three typing cases). |
| 10 | 38317590, c542faac, 91158ec4 | text-textarea-cluster-walk-whole-line | A searched row finds cluster ends only after its own start and only as far as its search reads, starting from the last end found or the nearest earlier spot between two chars below U+0300 (not CR LF); the search caps each probe lazily (no cluster count up front); one-char clusters skip the call; internal `ClustersByRow = false` keeps the whole-line walk as the reference. KB a key typing into a one-line 89k body, JIT ×2 identical (AOT within 0.4): Consolas emoji 1,216.8 → 192.4, mark 1,215.7 → 191.3; Segoe UI emoji 1,223.3 → 199.0, mark 1,221.1 → 196.7; ASCII/é unchanged. Open cold: 1 MB bundle one line + emoji 16,724.9 → 341.6, + mark 16,711.8 → 328.6, the bundle as is with one emoji 8,560.4 → 369.1; toc.html one line + emoji 16,686.7 → 304.5; index.json 6,179.8 → 4,132.5 (−33 %; warm 4,172.6 → 2,124.2). Rows: r9b6 corpora with 124,437 joiners (emoji, mark, ZWJ family, surrogate pair, space + mark, flags, Hangul, Devanagari, skin tone, keycap, lone surrogate, 40-emoji run), 3 faces/modes, 3,240 widths incl. 196 near-multiples, 42,599,160 rows: base hash = change in 20 of 20 runs, 0 widths differ from the in-build reference; layout hashes 7A18AF8DE8E05271 and F09E9038726E4BD6 unchanged. New suite row `input/type in a one-line JSON body with an emoji`: JIT 2.173 → 0.917 ms (−58 %), AOT 2.256 → 0.910 (−60 %), 1,216.1 → 191.7 KB/op; nothing else moved ×2, A/A clean (captures 91158ec4). TextAreaClustersTests Core 4 + Headless 3 (4 reverse edits fail); Text 293, Core TextArea 80, Headless 1,515, Meridian 659, Vesper 904, Junction 1,085; Fedora Headless 1,354, Core TextArea 80 pass. |
| 10 | 1c8d7a77, a3e89cc3, 00539fd2, 525f45e5, 819c6c58, 7143a2d1 | apps-vesper-sync-cursor-rebuilds-rows + vesper-sync-cursor-reveal | (a) Sync's rows take their place from the build's loop; (b) Sync keeps its rows (`KeptElements`) and a cursor key moves the ring on live rows, no build; (c) approved (question 29): a cursor key scrolls its row into view. KB a key AOT (JIT), 80 Down/Up keys: 60 files (40 rows) 453.7 → 0.4 (422.6 → 0.4); 300 files (200 rows) 2,568.5 → 0.4 (2,054.2 → 0.4); 3,000 files (2,000 rows, 18,086 elements) 81,102 → 0.4 (47,962 → 0.4); (a) alone 300 → 1,716, 3,000 → 16,444. Builds a key 1 app + 2 parts → 0; GCs per 80 keys at 3,000 files 451/262/66 → 0/0/0 (AOT); min a key at 3,000 32.2 → 1.7 ms JIT ((b) alone 1.22; the reveal draws the rows a scroll brings in). With a reader 0.9–1.0 KB a key, 0 trees; the cursor row's selected state is now told (the row's Button role set by the app). Reveal: after 40 Downs the ringed row is inside the view (719–743 of 111.9–743, scroll 352.9; before 1047.9–1071.9 at scroll 0, off screen). New suite rows `component/cursor key over a plain 200-row list built through the root, every row made` / `…, rows kept`: 1,607 → 50 KB/op, 1.08 → 0.80 ms JIT, AOT within 1 %. Suite `-Pairs 2` JIT + AOT 0/0, no allocation moved, A/A clean (captures 7143a2d1). SyncCursorTests 8 (pixels, tree, colours, rings, reader tree vs a keep-nothing build; Show equal on/off, Space, rail and row clicks, rescan, a fallback build; 5 reverse edits fail); Vesper 911 + 4 skipped, Headless 1,515, Core 5,071 pass. REACTIVITY.md, ACCESSIBILITY-PLAN.md. |
| 10 | 5af78ecf, 2337e73f, b9a716aa | apps-junction-diff-rows-keep-width + apps-junction-view-only-clicks | (a) The width stays in the diff's height shape only while rows wrap or the review is on: split drag 159.3 → 42.0 KB a move (AOT 157.2 → 41.2), elements unmounted 127 → 0, rows made 14 → 0, render 119.5 → 2.2; 4 px resize 32.1 → 20.3 (AOT 32.0 → 20.4); History unchanged. (b) `MarkViewDirty` at every listed site plus the MR search key; part builds a click 4 → 0 in Changes, 3 → 0 elsewhere; KB a click JIT (AOT within 3): staging line 306 → 169, hunk 306 → 170, Clear 296 → 159; dialog open 208 → 71, Escape/Cancel 177 → 40 (a confirm keeps the full build); popover Escape 177 → 40; search keys History 138 → 14, Branches 163 → 40, Tags 157 → 34, Files 168 → 44, MRs 255 → 126; folders Files 187/305 → 63/181, Commit tab 192/200 → 68/76; MR filter Escape 256 → 126. 150 scene steps (widths, pan scrolled and clamped, split, wrap on/off, drag, review, every site, a confirmed new-branch prompt, the MR filter): pixels, reader tree, focus, caret, pan, dialog/popover state identical base = a = b with and without a reader (each side differs from its own forced full build in the same 67 steps on base too: the pan's stale viewport and a tooltip). Suite `-Pairs 2`: no allocation moved; AOT clean; JIT one different slower row per pair with a clean A/A: noise (captures b9a716aa). `DiffRowKeepTests` +2 (186 / 5 rows made under a reverse edit), `ChromePartTests.ViewOnlyClicks_BuildTheViewAndNoChrome` (each of nine site groups reverted fails); Junction 1,088, Headless 1,515, Core 5,071 pass. REACTIVITY.md. |
| 10 | acaac297, bb2958de, ea1971ec | a11y-focus-find-down | `MoveA11yFocus`, `NotifyPendingA11yFocus` and `Announce`'s lookup find the node with `FindNodeDown` (the element's ancestors), the whole-tree walk only when the path finds nothing; the window keeps the focused element by reference (dropped on `SetRoot` and client detach); internal counter `AccessibilityFocusNodesVisited`. Nodes looked at a focus move: Meridian Tab at 2,000 endpoints (6,073 nodes) 6,072 → 55, 60 endpoints 252 → 55; Vesper Tab 118 / 233 / 225 → 23; Aperture cursor keys 56–115 → 49–76; Junction Tab 93 → 47; a move into an overlay opened in the same handler still falls back (~607 → 248–306). Meridian at 2,000, real UIA, key handling, JIT ×2: min 128.0 / 128.8 → 109.7 / 111.5 µs. New suite rows `a11y/tab among 10 fields after 2,000 rows, UIA` / `AT-SPI`: JIT UIA 0.497 → 0.490 ms min (build 0.168 → 0.148), AOT 0.557 → 0.534 (0.188 → 0.157); AT-SPI build 0.121 → 0.106 JIT, 0.144 → 0.134 AOT. Suite `-Pairs 2` JIT + AOT: none slower in both pairs, no allocation moved (captures ea1971ec, timings in its body). Event streams identical in all 129 scenario-modes (Meridian, Vesper, Aperture, Junction; dialog, context menu, popover mounted and focused in one handler, removed row, an ancestor turning semantic (the fallback), list rows, an announcement); KB a step unchanged (one-time ~0.2 KB chain list growth). Audit 0 STALE/WALK-VALUE/WALK-EVENT, fallbacks 83 → 80. `A11yFocusMoveTests` +2 (≤ 22 nodes a move, 4,004 reverted; the fallback tells the bridge); Core 5,071, Headless 1,517, Linux bridge 104, Meridian 659, Vesper 911, Aperture 362, Junction 1,088 pass. ACCESSIBILITY-PLAN.md. |
| 10 | b5f5828c, b7e6bda2, d4c46da8, e17ae14b, 15e32c20 | a11y-container-name-in-place | A renamed node with children is walked in place, its children carried over (`CarryChildrenOf`, `AdoptChildren`; the observer routes the change to the walk with `_a11yWalkDue`); a node made again is walked like any node (a leaf that gains children gets them, one that now takes its text loses them; `mustBeLeaf`/`MayChange` gone). Vesper navigation KB tree / UIA / AT-SPI: Detail 1,000 453.0 / 542.0 / 675.7 → 322.6 / 411.6 / 556.9; Brief 1,000 453.1 / 492.5 / 537.0 → 336.2 / 375.6 / 420.3; Thumbnails 1,000 356.7 / 374.4 / 393.1 → 249.9 / 267.6 / 286.2; at 60 files −8…−11 %; trees a navigation 1 → 0; bridge-off within the probe's spread. Junction's Commit count (stage/unstage a file) 670.4 / 675.4 / 678.9 → 622.5 / 627.6 / 631.1, trees 2 → 0 a step. Event streams identical in all 135 scenario-modes; stale checks 0. Audit, all seven suites: fallbacks 80 → 10 (Junction 58 → 1, Vesper 11 → 0, Headless 11 → 9; b7e6bda2 alone left 73, Vesper archive path buttons gaining an icon); 0 STALE/WALK-VALUE/WALK-EVENT. New suite rows `a11y/rename a pane over 1,000 rows, UIA` / `AT-SPI` (e17ae14b: each step also sets the title the pane shows, since a step that draws nothing makes the suite exit 1): 259 → 0.2 KB, JIT UIA min 0.575 → 0.520 ms, AOT AT-SPI 0.770 → 0.604; suite `-Pairs 2` JIT + AOT 0 slower, only these rows' bytes moved (base 375146d0, b5f5828c's tree with the fixed rows; captures 15e32c20). In-place scenes (group renamed, renamed with rows changed, nested groups, named scroll view, overlay panel, a tab named by its label, a button gaining/losing an icon) tell what a build tells with 0 trees through the recording bridge, AT-SPI and UIA; AT-SPI keeps its objects, UIA leaves no wrapper on the old node; reverse edits fail. Core 5,072, Headless 1,539, Linux bridge 104, Meridian 659, Vesper 911, Aperture 362, Junction 1,088 pass. ACCESSIBILITY-PLAN.md. |
| 10 | 5e93a4fe, bf536526, 03783526 | apps-meridian-map-virtual-list | The Discovery map is a `VirtualList` of item rows (the column's padding and gaps became row margins; `RowHeights` takes heading heights from `TextMeasure`); a click sets the list's `SelectedIndex` through its ref and the endpoint detail is a `Part` of its own (no window build). 60 / 600 / 2,000 endpoints, before → after: elements 321 / 2,481 / 8,109 → 203 / 210 / 210; hit visits a hover move 77.5 / 617.5 / 2,046 → 42.5 / 40.5 / 40.5; a wheel notch 64.6 / 602 / 2,030 → 33.7 / 29 / 29 hit visits, culls 36.5 / 579 / 2,007 → 4.9 / 5.1 / 5.1; culls a drawn frame → 3–4 (0 B); a click JIT 54.8 / 77.3 / 124.0 → 22.2 / 22.2 / 30.6 KB (AOT the same within 0.3), hit visits 226 / 1,847 / 6,131 → 121 / 116 / 116, `ZScans` 76 / 616 / 2,048 → 0 / 0 / 3.7, layout marks 123 / 663 / 2,098 → 24 / 24 / 31, builds 1 → 0; the map's elements 271 / 2,488 / 8,230 → 163 / 169 / 185 KB; AOT heap with the map shown 981 / 4,167 / 14,315 → 890 / 1,220 / 3,982 KB, after leaving 713 / 1,707 / 6,109 → 711 / 1,046 / 3,766; AOT click min 0.710 / 0.849 / 1.577 → 0.721 / 0.720 / 0.732 ms. Worse: JIT click at 60 endpoints ~+14 % (1.17 → 1.33 ms: a selection move in an item-row list lays out every realized row again, 869 measure calls vs 113; AOT within 1.5 %; → Waiting); a wheel notch 0.1 → 7.4 KB (the 1.5 rows it scrolls in; time flat). Reader: every line a ListItem named as the row was (**was a Button: question 34**); items served on demand at 2,000; an off-screen row reached by the reader's scroll, invoke/select selects it; a click with a reader 56.6 / 80.1 / 119.3 → 19.4 / 19.4 / 19.5 KB, no tree; event streams the same but for the role (and served ids at 2,000). Pixels identical but one hover tint (after a long wheel the row under a still pointer is tinted at once: the hover re-hit); two endpoints with the same method and path ("get"/"GET") no longer both show selected. Suite `-Pairs 2` JIT + AOT: no allocation moved, JIT pair 1's one slower row also in A/A, AOT clean (captures 03783526). New tests (reverse edits fail): rows on screen however long the map, a reader reaches a row off screen, a click moves the selection with no build; Meridian 662 (HttpEngineTests' stalled-server timeout failed once under load, passed 3× alone), Core 5,072, Headless 1,539, Linux bridge 104 pass. ELEMENTS.md "A scroll view of rows becomes a list", REACTIVITY.md. |
| 10 | 7b7a486c, a86b208f, a0ef7841, ea957e38 | syntax-markdown-loads-every-grammar | Approved route (question 30 (c)). A Markdown text gets a registry holding only the grammars it can reach (fence info strings: 52 fence rules, 193 terms mirrored from the grammar; '<' or '&' for HTML; `---` for YAML; up to 4 registries kept, cleared on `SetTheme`); shell heredocs, Ruby heredocs and `__END__`, F# `///` and `(**`, HTML `<style>`/`<script>`/on-event attributes scanned the same way; the grammar walk no longer boxes enumerators (3,590 → 37 KB over 78 grammars). Kept KB after the first file, full GC, JIT (AOT within 10): 3-line .md 29,446 → 1,310 (−95.6 %; allocated 82,873 → 3,345); no fences 29,499 → 1,340; 1 fence 29,485 → 1,658; 3 fences 29,605 → 3,930; 18 fences 30,513 → 21,372; repo README.md 29,742 → 4,014; a fence in every language 31,560 → 31,604 (allocated +0.4 %); .rb 29,315 → 1,030, .sh 29,179 → 600, .fs 29,227 → 559. First call README 155 → 58 ms JIT, 121 → 38 AOT; 3-line .md 119 → 9 JIT, 91 → 2.7 AOT; worst case (every language) +0.5 % AOT, +3.7 % JIT (first-use compile). Apps, retained after a full GC: Junction Files showing README.md 31,889 → 6,138 KB JIT (31,745 → 5,992 AOT); Vesper preview of README.md +30,612 → +4,868 KB. Colours: round 9's corpus (3,110 files tokenized, 1,758,209 lines + edge texts) on all 21 themes, 0 lines differ; TokenizeDiff equal on 81,086 lines, SpansForLanguage on 534 fences. Suite `-Pairs 2` JIT + AOT 0/0, no allocation moved (captures ea957e38). EmbeddedLanguageTests 10 (tables pinned to the grammar files, scan = each fence rule's expression on 255,024 lines; reverse edits fail); Syntax 104, Junction 1,088, Vesper 911, Meridian 662, Headless 1,539 (goldens) pass. Still shared: Typst (57 grammars), Julia (9.2 MB), PHP, Razor, HTML files. |
| 10 | 7ca2b3db, 956194e7, bfcba907, 05d03300, c7f7e6ba, f5549b05, e4e99925 | junction-diff-pan-viewport + apps-pane-drag-sets-size | (a) Bug: after a build or a resize Junction reads the laid-out diff list after the frame and gives the pan its viewport; HorizontalPan's thumb follows a viewport assigned while the bar is shown (no API, no extra build a resize): one sideways wheel turn at start reaches 361 → 963 of 963 px; after resizes 967/1103, 1103/1203, 1203/1303, 963/1403 → all reached; thumb at start 655.7 → 351.6 px; a 4 px resize 20.3 → 20.4 KB, one extra layout-and-paint frame while the bar shows. (b) A splitter drag sizes the live pane through a `Ref` + `MarkLayoutDirty()` and builds on press and release only (and on a move while the diff wraps, a review is open, or an offset clamps). KB a move JIT (AOT): Meridian 2,000-line body 38.8 → 2.5 (38.6 → 2.5), no response 10.9 → 0.1, pane at the bottom 36.4 → 0.1; Junction Changes split 42.0 → 2.4 (41.2 → 2.4), Commit split 65.2 → 5.5 (64.1 → 5.5); part/window builds a move 1 → 0; the pane follows the pointer exactly. Vesper and Aperture have no splitter. 157 steps compared in-process with a forced full build (every diff row remade): pixels, reader tree, elements, focus, sizes, pan, 0 differ with and without a reader (before (a), 67 of 125 Junction steps differed). Follow-ups: the new refs let go in builds that don't show them (c7f7e6ba, f5549b05). Suite `-Pairs 2` JIT + AOT 0/0, no allocation moved, A/A clean (captures e4e99925). New tests (DiffScrollTests, HorizontalPanFollowTests ×2, Junction PaneSplitterTests, Meridian ResponseViewerTests; reverse edits fail); Core 5,072, Headless 1,541, Meridian 664, Vesper 911, Aperture 362 pass; Junction 1,089–1,090 of 1,091 (flakes on base too: → step 13b). REACTIVITY.md, ELEMENTS.md, LAYOUT.md. |
| 10 | 0b276fec | junction-view-only-tests-settle (tests) | Cause: on Windows a folder made by TempRepo's copy reports a change the first time anything lists it, so the app's first `git for-each-ref` set off "refs\heads changed" in 25 of 25 fresh copies and the watcher reloaded ~300 ms later inside the counted window (chrome 14 → 21, data generation 0 → 1). `CopyTree` lists every folder of a copy before any watcher exists (0 of 19 copies report after; `FixtureQuietTests` fails 3 of 3 without it); `HostedOnGitLab` settles after the GitLab mark; `ViewOnlyClicks` waits for the watcher's report of its own write. 20 fresh runs each with 28 busy threads: `AFolderOrFilterCharacter` 3/20 (+2/20 instrumented) → 0/20; `ViewOnlyClicks` 0/20 → 0/20 (step 13's ~1 in 4 not reproduced here). Reverse edits of steps 5/8 still fail. `ReviewTests.TheKeyboardReachesTheCommentAgain` left (a different flake: nothing focused after a Tab). Junction 1,092 ×2 (one earlier run 1,091 + that flake), Headless 1,541, Core 5,082 pass. Tests only. |
| 10 | 2f7ab168, 81b6d84e, b63f8ceb, ab8c714d, 495f1e85, 4cb55b86 | vesper-brief-scrollbar-columns | Approved (question 28 (a); public API). **Public `ScrollViewElement.UserScrolled` (`Action?`)**: "Called after the user moves this view's offset: the wheel, the scrollbar (a track click, each move of a thumb drag), the autoscroll (the middle button held, a selection held past the view's edge) and a VirtualListElement's own scroll keys"; once per move that changed the offset, last in each path; code scrolls (setters, `ScrollBy`, reveals) and moves handed to a `SidewaysScroller` raise nothing; `PatchFrom` carries it; the window's autoscroll goes through an internal `ScrollByUser`; `ScrollEvent` unchanged (scope wider than the brief: question 35). Vesper: Brief listens to `UserScrolled` and makes the columns it scrolls to; returns from another view at the start with columns made for the start (question 36); a folder that fits its pane has nothing to scroll (`FlexGrow` on the content made it ~1.7e38 px wide at 60 files). Blank columns in view, headless 1240×820: track click 400 files 4 of 4 → 0 (0 → 1 build); bar walk 400 / 3,000 / 60 files 15 / 22 / 22 → 0; autoscroll 182 frames 139 / 139 / 111 → 0; return from Detail, Thumbnails, viewer 17 / 17 / 0 → 0; wheel builds 25 → 4; steps not matching a full build 30 → 0 at 400 and 3,000. Brief KB a key Down 5.0 → 4.9, Up 5.2, 12 builds; 3,000 files 6.07, 105 builds. step-ab `-Pairs 1` JIT 0/0, no allocation moved (captures 4cb55b86). UserScrolledTests 9, BriefScrollColumnsTests 4, BriefReturnTests 4 (reverse edits fail); Core 5,072, Headless 1,550, Vesper 919, Junction 1,092, Meridian 664, Aperture 362 pass; docfx `--warningsAsErrors` 0. ELEMENTS.md. |
| 10 | 00b8fc79, d85c6ff6, f0106c63 | vesper-brief-return-offset | Approved (question 36 (b)). Framework, no new API: while a view has no viewport or no content on an axis, setting `ScrollOffsetX`/`Y` keeps the value (≥ 0) and reads it back; the layout pass applies it once both have a size, clamped, before any held reveal, raising no `UserScrolled`; a later set replaces it, `ScrollBy` adds to it; user input never holds an offset (private `MoveOffsetX/Y`: wheel, bar, autoscrolls, VirtualList keys); `ScrollRangeIntoView` reveals from the held offset, `ScrollRangeIntoViewHorizontal` does nothing before a width; VirtualList's content is its row height. Callers checked (every reveal in Junction, Meridian, Aperture, CommandPalette, Inspector, Vesper; autoscroll carry-over; UIA scroll; a11y horizontal reveal; Locator; PerfSuite); one needed a guard (`ScrollBriefToCursor` before layout). Vesper: leaving Brief stores offset, cursor and result set (`PaneTab.BriefLeft`); the build that makes the new scroll view sets it and builds those columns (`BriefOpenAt`); a cursor moved while away is revealed with the least move; `NavigateTo` clears it. Return walk at 60/400/3,000 files × mid/end × cursor/bar × Detail, Thumbnails, viewer, diff, sync: back at the offset and cursor left 20 → 60 of 60, cursor shown as left 40 → 60 of 60, blank-column steps 0 → 0 of 138, steps differing from a full build 0 → 0; the return build at 400/end 596 → 459 KB. step-ab `-Pairs 1` JIT 0 slower, no allocation moved (captures f0106c63). ScrollViewElementTests +6 (replaces `ScrollOffset_ClampsToZeroWhenNoContent`), ScrollOffsetBeforeLayoutTests 3, BriefReturnTests 14; reverse edits fail; Core 5,088, Headless 1,553, Vesper 929, Junction 1,092, Meridian 664, Aperture 362 pass; docfx 0 warnings. ELEMENTS.md §6, REACTIVITY.md. |
| 11 | 6403f8fe, 207fbe25, 67105a0b, c177a16c, c99e23bf, 868e2ffc | dotnet-11-upgrade (the user, 2026-11-11) | Everything targets .NET 11: one `HultUITargetFramework` (net11.0) in Directory.Build.props used by all 102 projects; a new `global.json` (SDK 11.0.100, latestFeature) that every workflow's setup-dotnet reads; installer, workflow and script publish paths; `wsl-test.sh` exports `DOTNET_ROOT` from the dotnet on PATH; guides and the two skills name .NET 11; `step-ab.ps1 -BaseSdk` and per-side target framework for the AOT wrapper. Fix found by the measurement (c99e23bf): .NET 11's optimized JIT boxes `HashCode.Add<T?>` in `TextPipeline.MetricDigest`'s loop (2–3 × 24 B a metric span on every styled layout; 12 JIT rows' allocKB up, styled wrap 74.8 → 225.7 KB) — plain values now, test fails on net11 with the old code (72 B a call). Measured ca31555d on SDK 10.0.300 / net10.0 against the tree on SDK 11.0.100 / net11.0, whole suite (230 rows), 2 pairs: JIT 0 slower / 0 faster, A/A 0/0, allocKB down on 2 rows (dropdown 38.4 → 36.8, wheel 4.4 → 4.2); AOT A/A 0/0, 2 slower (long TextArea document +13–15 %, wrapped TextArea resize +11 %; → Waiting dotnet11-aot-textarea-long-document), highlighted JSON −15 %; idle 0.2 passes/s, the 17 zero-KB rows still 0. AOT app walks (69 × 2): geometric mean 1.000, KB a key +0.3 %, none > 5 % (Vesper thumbs +4.8 %), builds equal, GCs gen0/1/2 123/17/8 → 121/15/8. Start-up (42 guarded launches, 0 foreground takes): Junction median 258.8 → 255.3 ms, Vesper 280.9 → 275.6, the others ±9 ms; working set flat (Junction 105.5 → 105.6 MB, Aperture 116.3 → 116.2). Exe sizes +0.2…+1.6 % (Junction 23.87 → 24.03 MB). Warnings: build 367 → 359 (SDK 10's ILCompiler NU1903 gone), AOT/trim 0 → 0 (Aperture's Kiota IL2104 1 → 1), docfx 0. Tests: Windows 30 projects, round 10's counts + 1 (Meridian's stalled-timeout flake failed both full runs → Waiting); Fedora 22 projects, 0 failures (Vesper zebra tint once, then 10/10); UiTests 130 pass (2 LiveResizeFrameTests fail in the full run on .NET 10 too → Waiting); mpv 29/29. Harness in this session's scratchpad `r11u\`. |

## Examined, not worth it

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

No question is open from rounds 1–10. Their answered questions are in the archive
([round 10, with round 9's 26 and the earlier 10](17-performance-team-ledger-archive.md#needs-the-user-round-10),
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

From 2026-11-09 (round 10's survey); earlier entries are in the archive ([to 2026-10-25](17-performance-team-ledger-archive.md#log-2026-10-07-to-2026-10-25),
[2026-10-26 to 2026-10-27](17-performance-team-ledger-archive.md#log-2026-10-26-to-2026-10-27),
[2026-10-27 to 2026-11-02](17-performance-team-ledger-archive.md#log-2026-10-27-to-2026-11-02),
[2026-11-02 to 2026-11-03](17-performance-team-ledger-archive.md#log-2026-11-02-to-2026-11-03),
[2026-11-03 to 2026-11-08](17-performance-team-ledger-archive.md#log-2026-11-03-to-2026-11-08),
[2026-11-08 to 2026-11-09](17-performance-team-ledger-archive.md#log-2026-11-08-to-2026-11-09)).

- 2026-11-09: round 10 survey started in a fresh context (session 3484c586). Main had not moved (origin/main
  facced69 is in the branch). Moved to the archive by a checked script (every moved line found there, nothing lost):
  round 8's 19 Landed rows, round 9's build order with the survey's closed-from-Waiting note, round 9's answered
  questions 14–25 (26 stays), and round 8's 100 log lines (2026-11-03 to its release). Weekly usage 42 %. The eight
  experts run in their own worktrees on the archive commit, reviewing 4919ac40..facced69 (81 commits, no main merge).
- 2026-11-09: round 10 survey done (reports b6c20c17..f4d6050d, `17-reports/r10-*`, ~2.5 h). GL/Vulkan satisfied;
  the other seven not. Two review objections: c940c643's kept tree rows keep the whole Requests view alive (expert 4,
  step 2) and cb1df8c0 + ea42f45b: Meridian's map rows are Buttons, so a click with a reader builds the whole tree
  (expert 7, step 1). Round 9 helped the apps (65 walks, geometric mean 0.852, −44 % summed). 14 build steps; questions
  27–32 to the user (none blocks a step's start; 29 and 30 shape steps 7 and 12). 4315c4b's Linux half answered (Open).
- 2026-11-09: the user asked for new app releases before the round's build work. Cut on main facced69 (round 9's
  release, CI run 42675929595 green): junction-v1.24.0, vesper-v1.24.0, meridian-v1.7.0, aperture-v1.22.0,
  reel-v1.8.0, annotated (`git tag -a -F`), pushed 3 + 2; installer runs 42692675116 (Vesper), 42692675510 (Junction),
  42692676124 (Meridian), 42692706680 (Reel), 42692710316 (Aperture) started. Step 1 waits for them.
- 2026-11-09: the user answered questions 27–30 as recommended: (27) Aperture skips Backspace as "back" in a text field
  (step 5c); (28) the framework scroll callback, public API (new step 15); (29) Sync reveals its cursor (step 7); (30)
  Markdown loads only the fence languages a text names (step 12). Questions 31 and 32 stay open (recommended no; steps
  2 and 13 go app-only).
- 2026-11-09: installer runs: junction-v1.24.0, vesper-v1.24.0, aperture-v1.22.0 and reel-v1.8.0 green and released;
  meridian-v1.7.0 failed its Windows test job on `Junction.Tests.ComposerPartTests.WhatTheComposerShows_FollowsEveryWayItChanges`
  (the test's own `git commit --amend` hit an `index.lock` held by the app's background git; the same test passed in
  Junction's own installer run on the same commit): a flake; `gh run rerun --failed` once.
- 2026-11-09: the user left questions 31 and 32 to the recommendation: both no (`Ref<T>` unchanged; `PaneSplitter`
  gains no API). Steps 2 and 13 stay app-only.
- 2026-11-09: meridian-v1.7.0 green on the re-run (test, test-linux, installer) and released (Meridian-Setup-1.7.0.exe);
  all five releases published. Round 10 step 1 spawned.
- 2026-11-10: round 10 step 1 landed (f451a9ef; captures 8c02c5a2): a Button with an app-set role that says selected
  before and after is patched in place; Meridian map click with a reader 974 → 121 KB at 2,000 endpoints, 0 trees;
  reader streams identical. Guideline notes: commit before splicing an audit (a reverse `git diff` patch takes
  uncommitted work too); `FindByAutomationId` builds a whole tree per call; to tell a set's selection with no tree, set
  the role (`ListItem` or `Button`) and `AccessibleSelected` on every row from the start. Step 2 spawned.
- 2026-11-10: round 10 step 2 landed (b2fdcf52, c3cb789a, b837de41, 3194c830, 35102eb4; captures b6204f39):
  `KeptElements` holds weakly and the apps let go of refs into a left view: Meridian 200 requests → History 2,364 → 1,027
  KB retained (−57 %), Aperture Home 8×40 → Settings −41 %, Vesper Brief → Detail −23 %, every old view dead after the
  switch; 0 bytes added a kept key. Guideline note: take a test's weak reference in a NoInlining helper (the JIT kept
  the grid alive in the test's own frame). Found: going back to Brief from Detail is blank until a key (pre-existing;
  folded into step 15); three holders left unmeasured (Waiting). Step 3 spawned.
- 2026-11-10: round 10 step 3 landed (1c3cf5c5; captures ef42ca03): the wheel latch lets go of a scroll view a build
  removed (end of a changed frame, overlay close, `SetRoot`): Meridian map wheeled then left 4,314 → 1,815 KB retained at
  600 endpoints (−58 %), 14,283 → 6,052 at 2,000; Aperture grid and Home → Settings −17…−20 %. Guideline notes: before
  this fix a retention check that wheeled first measured the latch, not the app; a latch-style field must be released at
  frame end, on overlay close and on `SetRoot` (Children flags miss a reused root). Step 4 spawned.
- 2026-11-10: round 10 step 4 landed (150d08bc, 47ea53fb, 96a54d45, 2ccde148, cf68adeb, 70025cb3; captures 5c73087d):
  Meridian's Runner counts from the scanned tree, keeps result rows and builds its results alone: a 200-request run
  1,799,085 → 24,302 KB (−98.6 %), GCs 107/19–22/2 → 1/0/0; the Chain sub-tab −96…−97 % a URL key. Question 33 (an
  unreadable file now counted in the rail) asked. Guideline notes: counts from a snapshot the app holds; a growing list
  keeps its rows and builds alone; number kept rows' builds by the part's own builds, not the window's. Step 5 spawned.
- 2026-11-10: round 10 step 5 landed (3e2b9c3c, b03e7b16, 45441b1a, f8d952c3, bce29c46; captures a7d3c7bf): typing in
  Junction's prompt/reword and Aperture's reply/report builds nothing (−98…−99.7 % a character), Junction's folder and
  MR filter build the view only (−59 %), a toast's hide builds the view only (−21…−39 %), and Backspace in an Aperture
  text field no longer goes back (question 27 (a)). Leftovers: the MR filter's Escape (→ step 8b), Aperture's cursor
  guard misses `TextArea` (Waiting). Guideline: a field read only by its action builds nothing, one read for "has text"
  builds when that flips, one the view reads takes `MarkViewDirty`. Step 6 spawned.
- 2026-11-10: round 10 step 6 landed (38317590, c542faac; captures 91158ec4): TextArea finds cluster ends only for the
  rows it searches: typing into a one-line 89k body with an emoji 1,216.8 → 192.4 KB a key (−84 %), opening a one-line
  1 MB bundle with an emoji 16,724.9 → 341.6 KB (−98 %), index.json −33 %; 42.6 M rows identical. Guideline notes:
  per-line work before a row loop is a cliff when most rows take a fast path; a search that needs a total up front can
  often take it lazily. Step 7 spawned.
- 2026-11-10: round 10 step 7 landed (1c8d7a77, a3e89cc3, 00539fd2, 525f45e5, 819c6c58; captures 7143a2d1): Vesper
  Sync's cursor key builds nothing and the cursor stays in view (question 29): 2,568.5 → 0.4 KB a key at 300 files,
  81,102 → 0.4 at 3,000, GCs 451/262/66 → 0/0/0, 32.2 → 1.7 ms. Guideline notes: a cursor's look can reach into the row
  (an inherited TextColor write needs `MarkStyleDirty`, a ring's border width `MarkLayoutDirty`); a row with an inferred
  role must set its role for its selected state to be told with no tree; a kept row's handler looks up its place when it
  runs. Tooling: long batches via Bash `run_in_background` with `pwsh -File` (a PowerShell background task reported
  exited while running and a second copy overlapped: timings disturbed, bytes not); leftover `tail -f` watchers from
  agents stopped. Step 8 spawned.
- 2026-11-10: round 10 step 8 landed (5af78ecf, 2337e73f; captures b9a716aa): Junction diff rows keep their width
  (split drag 159.3 → 42.0 KB a move, resize −37 %) and view-only clicks build no chrome (part builds 3–4 → 0; e.g.
  dialog Escape 177 → 40 KB, History search key 138 → 14). Found: the diff pan's viewport updates only on a build, so
  the sideways wheel stops short (pre-existing; folded into step 13). Guideline: keep out of what makes a list's rows
  again anything no row reads; a dialog's open and cancel build the view, its confirm builds everything. Step 9
  spawned.
- 2026-11-10: round 10 step 9 landed (acaac297, bb2958de; captures ea1971ec): a focus move finds its nodes down the
  element's ancestors: Meridian Tab at 2,000 endpoints 6,072 → 55 nodes looked at, 128 → 110 µs min; reader streams
  identical in 129 scenario-modes; audit fallbacks 83 → 80. Notes: the audit's `apply6.py` anchored on `FindById`, now
  gone (re-anchored on `SetRoot` in the r10b9 copy); take timings before committing a change, since commits are never
  amended. Step 10 spawned.
- 2026-11-10: round 10 step 10 landed (b5f5828c, b7e6bda2, d4c46da8, e17ae14b; captures 15e32c20): a renamed node with
  children is walked in place: Vesper navigation with a reader −18…−29 % at 1,000 files, trees 1 → 0; Junction's Commit
  count trees 2 → 0; audit fallbacks 80 → 10; streams identical in 135 scenario-modes. Guideline notes: naming a
  container after what it shows is now cheap; a perf-suite step must draw or the suite exits 1. New lead in Waiting
  (a label under a name-from-content button). Step 11 spawned.
- 2026-11-10: round 10 step 11 landed (5e93a4fe, bf536526; captures 03783526): Meridian's Discovery map is a virtual
  list and a click builds the detail alone: at 2,000 endpoints elements 8,109 → 210, hit visits a move 2,046 → 40.5,
  culls a frame 2,007 → 4, a click 124.0 → 30.6 KB, AOT 1.577 → 0.732 ms, heap with the map shown 14,315 → 3,982 KB. JIT
  click at 60 endpoints +14 % (selection relayout of realized rows; → Waiting). Rows now read as list items, not
  buttons: question 34 asked. Step 12 spawned.
- 2026-11-10: round 10 step 12 landed (7b7a486c, a86b208f, a0ef7841; captures ea957e38): a Markdown, shell, Ruby, F# or
  HTML-embedding text loads only the grammars it can reach (question 30 (c)): kept after a first 3-line .md 29,446 →
  1,310 KB (−95.6 %), Junction Files showing README.md 31,889 → 6,138 KB retained, first call 155 → 58 ms; 0 colour
  changes on 1.76 M lines × 21 themes. Guideline notes: TextMateSharp loads every grammar a grammar names, transitively,
  on the first line, so a cache is keyed by the set a text reaches; a reused larger registry can make a colour-equality
  test pass without testing anything (reset between cases). Step 13 spawned.
- 2026-11-10: the user answered question 34: keep list items (Meridian's map rows).
- 2026-11-10: round 10 step 13 landed (7ca2b3db, 956194e7, bfcba907, 05d03300, c7f7e6ba, f5549b05; captures e4e99925):
  Junction's diff pan reads its width after layout, so the sideways wheel reaches the end of the longest line (361 →
  963 of 963 px at start); a splitter drag sizes the live pane and builds nothing a move (Meridian 38.8 → 2.5 KB, Junction
  Changes 42.0 → 2.4, Commit 65.2 → 5.5). Found: this round's ChromePartTests (steps 5 and 8) are flaky under load — a
  watcher rebuild lands in their view-only window (base too) → step 13b before the full suite. Guideline notes: what a
  build reads from layout is a frame old (read after the frame, update in place); a `Ref` added for speed is emptied in
  builds that don't show it; compare against a full build in the same process when text changes between runs. Step 13b
  spawned.
- 2026-11-10: round 10 step 13b landed (0b276fec, tests only): a fixture copy gives Junction's watcher nothing to report
  (a fresh copied folder reported a change on its first listing, so the watcher reloaded inside the tests' counted
  window); ChromePartTests 3/20 → 0/20 under load. ReviewTests.TheKeyboardReachesTheCommentAgain stays a known flake
  (memory ci-red-findings updated). Step 14 spawned.
- 2026-11-10: round 10 step 14 measured and declined (9f76ecb6, DECISIONS.md only): the long-label line seek is a real
  3–4 % in every pair, under the 5 % bar; patch kept in the scratchpad. Step 15 spawned.
- 2026-11-11: round 10 step 15 landed (2f7ab168, 81b6d84e, b63f8ceb, ab8c714d, 495f1e85; captures 4cb55b86): public
  `ScrollViewElement.UserScrolled` (question 28 (a)); Brief shows 0 blank columns after a track click, a thumb drag,
  autoscroll and a return from another view, at 60/400/3,000 files (before 4 of 4, 15–22 of 23, 111–139 of 182, 17 of
  21), every step equal to a full build. Questions 35 (the callback's scope is wider than asked: the wheel and list keys
  too) and 36 (Brief returns at the start, the old offset is not kept) asked. Guideline notes: a view that makes only
  what its offset shows builds on `UserScrolled`, only when the set in view changes; a new scroll view opens at 0 and
  can't take an offset before its first layout; `FlexGrow` on a horizontal ScrollView's content makes it ~1.7e38 px wide;
  `MarkDirty` from `OnAfterBuild` is dropped (Waiting). Every build step done; full suite started.
- 2026-11-11: the user answered questions 35 (keep `UserScrolled` wide) and 36 ((b): restore Brief's old position, step
  15b). An early full Windows suite runs first; 15b follows, then the full suite again.
- 2026-11-11: early full Windows suite on ce839ef3 (src as after step 15): `dotnet test HultUI.slnx -c Release` 30 projects,
  0 failures (Core 5,072, Headless 1,550, Junction 1,092 in 8 m 35 s, Vesper 919, Meridian 664, Aperture 362, Skia 387, Text
  293, Syntax 104, Linux bridge 104, Terminal.XTermNet 2,254). Step 15b spawned; the full suite runs again after it.
- 2026-11-11: round 10 step 15b landed (00b8fc79, d85c6ff6; captures f0106c63): a ScrollView keeps an offset set before
  its first layout and applies it then (no new API); Brief comes back where it was left (60 of 60 return cases at the
  offset and cursor left, 0 blank columns, all equal to a full build). Guideline notes: to reopen a view at a position,
  set the offset on the element the build makes and build its content for it in the same build; never compute a
  least-distance reveal from a width before layout; user-input paths never hold an offset.
- 2026-11-11: the user asked for new app releases after this round too (after the merge into main and green CI). The
  final full suite started.
- 2026-11-11: the user: .NET 11 has just been released; consider it for the next round. Recorded as Waiting
  dotnet-11-upgrade, to go first in round 11 (its own measured step before the survey).
- 2026-11-11: round 10 full suite on 776838cb (src as after step 15b): Windows `dotnet test HultUI.slnx -c Release` 30
  projects, 0 failures (Core 5,078, Headless 1,553, Junction 1,092 in 8 m, Vesper 929, Meridian 664, Aperture 362);
  Fedora `wsl-test.sh` 22 projects, 0 failures (Core 5,077 + 1, Headless 1,384 + 83, Vesper 911 + 20); docfx
  `--warningsAsErrors` 0 warnings, 0 errors. origin/main facced69 is in the branch. The 27 idle MSBuild worker nodes left
  by the runs blocked the user's .NET 11 SDK install ("Files In Use: .NET Host"); `dotnet build-server shutdown` cleared
  them. Phase → release.
- 2026-11-11: round 10 merged into main as cc55bdcc (detached origin/main merge in this worktree, tree identical to the
  tested branch; origin/main facced69 was in the branch, so the push gate held). Primary checkout fast-forwarded behind
  the guard; branch fast-forwarded to the merge and pushed. CI run 42924747474 queued. Release tags drafted for after
  green CI: junction-v1.25.0, vesper-v1.25.0, meridian-v1.8.0, aperture-v1.23.0, reel-v1.9.0 (mpv-natives-ci present).
- 2026-11-11: .NET 11 SDKs installed for round 11: the user on Windows (11.0.100); WSL Fedora 11.0.100 + .NET 10.0.15
  runtimes via Microsoft's script in /usr/local/share/dotnet (the user chose it over Fedora's RC3 package). Upgrade
  traps noted in Waiting dotnet-11-upgrade.
- 2026-11-11: CI run 42924747474 on cc55bdcc green: test, test-linux, docs, meridian-keyring (ubuntu-24.04, macos-15).
  Round 10 landed 16 kept steps (1–13, 13b, 15, 15b; 14 measured and declined) and its public additions the user
  approved (`ScrollViewElement.UserScrolled`; Meridian map rows read as list items). App releases cut on cc55bdcc (the
  user's request): junction-v1.25.0, vesper-v1.25.0, meridian-v1.8.0 (pushed first), aperture-v1.23.0, reel-v1.9.0;
  installer runs 42927197621, 42927198218, 42927198241, 42927201764, 42927202800 started.
- 2026-11-11: installers: junction-v1.25.0, vesper-v1.25.0, aperture-v1.23.0, reel-v1.9.0 green and released;
  meridian-v1.8.0's Windows test job failed on `HistoryTests.AnOlderSendFadesItsColours_NotItsRow` at 00:00:41 UTC (a
  midnight race in the test: `DateTimeOffset.Now` + `IsToday`; passed in CI on the same commit an hour earlier) →
  `gh run rerun --failed` once; the test fix is in Waiting.
- 2026-11-11: meridian-v1.8.0 green on the re-run (attempt 2: test, test-linux, installer) and released
  (Meridian-Setup-1.8.0.exe): all five app releases published. Round 10 landed 16 kept steps (1–13, 13b, 15, 15b; 14
  declined) in ~60 commits: two review objections fixed (a selected Button patched in place; kept rows held weakly),
  retention closed (views let go, the wheel latch), the Runner −98.6 % a run, typing that builds nothing in Junction and
  Aperture, TextArea cluster ends per row (−84 % a key with an emoji), Sync and the Discovery map with no build a key,
  Junction's view-only clicks and pane drags, a reader's focus lookups and renamed containers in place, Markdown loading
  only the grammars it reaches (−95 % kept), and bug fixes the user approved (Aperture Backspace, Brief's blank columns
  and return position, Sync's cursor reveal, Junction's sideways pan); public `ScrollViewElement.UserScrolled`. Round →
  11, Phase → upgrade then survey; the loop stops, and round 11 starts in a fresh context.
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
