# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 10
- **Phase:** build — round 10, step 1 landed (f451a9ef); step 2 SPAWNED 2026-11-10 (brief `brief-r10b2.md`). The five app releases cut on facced69
  (the user asked for them before the round's work) are all published. The survey is merged
  (reports `17-reports/r10-*`); build briefs `brief-r10b<N>.md` go in session 3484c586's scratchpad. Questions 27–32
  asked 2026-11-09; 27–30 answered as recommended the same day (27 → step 5, 28 → new step 15, 29 → step 7, 30 →
  step 12); 31 and 32 answered no (the user left them to the recommendation): steps 2 and 13 are app-only. Open for the user, none blocking: question 26 (a merged syntax theme to match VS Code; for
  later), question 10 (`TokenizeDiff` public), the live screen-reader check of menus and disabled menu items
  (ACCESSIBILITY-PLAN.md). Older rounds are in [the archive](17-performance-team-ledger-archive.md) (search it, don't
  read it whole). History note: rewritten 2026-10-25 (every commit after 3353ce47 has a new ID; a pre-push hook refuses
  pushes built on the old history). To resume in a fresh context, run in this worktree: `/loop Run the next step of
  the performance team: follow docs/plans/17-performance-team.md, with state in
  docs/plans/17-performance-team-ledger.md. Read both once per session (afterwards only the ledger's Phase and the part
  the step needs), do the step the ledger's Phase calls for, record it in the ledger, and commit.` Before resuming,
  `git fetch` and merge main if it moved. Build steps use `scripts/perf/step-ab.ps1`.
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`)
- **Review commits since:** round 10's survey reviews round 9's build, 4919ac40..facced69 (the last survey report
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

Rounds 1–9's build orders all landed but round 6's step 8, declined (rounds 5–9 are in [the archive](17-performance-team-ledger-archive.md#round-9-build-order),
with the round 9 survey's closed-from-Waiting note). Round 10's build order comes from its survey.
A build step may bundle one expert's related small candidates as separate commits. Items marked **(user)** wait for a
decision in Needs the user; the rest of their step goes ahead.

### Round 10 build order

Ranked by expected win over cost: the review objections first, then retention, then the biggest app wins. Each step
names the report lines its brief quotes (`r10-k` = `17-reports/r10-k-<area>.md`).

1. **a11y-selected-button-patch** (7; r10-7 Review and Candidate 1) — the objection to cb1df8c0 with ea42f45b: mark a
   selected state in place for a Button that says selected or not before and after, as for a ListItem (the clause at
   the observer, Window.cs:4711; prototype `element.Role is ListItem or Button`, `apply-buttonsel.py`). Meridian map
   click KB (off / tree / UIA / AT-SPI): 60 endpoints 58.1 / 92.3 / 92.7 / 92.8 → 58.1 / 58.3 / 58.4 / 58.6; 600
   81.5 / 343.6 / … → 81.5 / 81.8 / …; 2,000 120.8 / 973.5 / 973.9 / 974.0 → 120.8 / 121.0 / 121.1 / 121.3; trees 1
   → 0; events identical. Update `A11yLeafPatchTests` "selected, not an item" (they pin today's rule). Small, no API.
2. **Views held after they leave** (4; r10-4 Review c940c643 and Candidates 1–2) — (a)
   **dotnet-kept-elements-hold-weakly**: `KeptElements` holds live elements weakly (an internal `IRef` over
   `WeakReference<Element>` that PatchFrom retargets, internal `WithRef(IRef)`): Meridian Requests → History at 200
   requests 1,350 of 5,770 KB freed (−23.4 %), 25 requests −5.2 %. (b) **apps-refs-hold-last-view**: apps empty their
   `Ref` fields and scratch lists in builds that don't show that view — Meridian `_respTabRef`/`_responseBodyRef`/
   `_urlRef` (+118…+141 KB), Aperture Home `_homeShelfCards`/`_homeShelfRows`/`_homeCards` (−11…−17 %), Vesper
   `_cursorRowL`/`_briefScrollL`/`_briefCellsL` (−6 %), Aperture `_gridList` (−6 %, keep its height as a float). Weak
   reference tests; a guideline (a `Ref` into a view keeps the whole view). Small; each its own commit. **Question 31
   (recommended no) leaves `Ref<T>` as it is.**
3. **layout-wheel-latch-holds-removed-view** (1; r10-1 Candidate 1) — `WheelGesture._latched` (PointerEvents.cs:197)
   holds the last latched scroller until the next wheel anywhere; release it at the end of a frame that changed the
   tree when the scroller is no longer shown (`NoteStructureForHover`, Window.cs:3150, before its no-hover early
   return; also an overlay closing with the latched scroller). Meridian map wheeled then left, heap after full GCs: 60
   endpoints 1,125 → 844 KB, 600 4,317 → 1,819 (−58 %), 2,000 14,287 → 6,056 (−58 %); prototype
   `proto-wheel-latch-gesture.patch`. Weak-reference test; latching tests still pass. Small, framework, no API.
4. **apps-meridian-runner-rail-reads-every-request** (6/8; r10-8 Candidate 2) — the Runner rail counts from the
   scanned tree instead of reading and parsing every `.req` file three times a build (`ScopeRequestsCountFor`,
   Runner.cs:156/160/181/234-241): rail 7,926 → 36 KB a build; a run of 50 121,536 → 18,969 KB (−84 %), 200 1,783,390
   → 189,255 KB (−89 %), GCs 107/17/2 → 11–12/1–2/1. Then keep result rows (one new row a result; ~−99 % estimated).
   Measure the Chain subtab's `AnalyzeAllRequests` (Chaining.cs:239, 316-333) and fix it the same way if it clears a
   bar. Edge: an unreadable request file is counted but still not run. Small–medium, app.
5. **App typing and toasts that build nothing** (6; r10-6 Candidate 1, Waiting re-rank apps-toast-gone-view-only) —
   (a) Junction's prompt (`JunctionApp.Dialog.cs:153/205`) and rebase reword (`Rebase.cs:548`), Aperture's issue reply
   (`ApertureApp.Issues.cs:416`, builds only when "empty" flips) and report message (`Report.cs:187`) build nothing a
   character; Junction's repo modal path (`RepoModalView.cs:187`) and MR list filter (`MergeRequests.cs:385`) build
   the view only. Junction prompt over Changes 194.4 → 1.4 KB a character, History 154.8 → 0.6; Aperture reply 135.1 →
   2.3. (b) a toast's going-away build is view-only (`ToastHost(() => IsShowing ? MarkDirty() : MarkViewDirty())`,
   Junction, Aperture, Meridian): Junction 366.3 → 227.4 KB (−38 %), Aperture −39 %, Meridian −21…−23 %. (c) **bug,
   question 27 (a)**: Aperture's `browse.back` skips Backspace when the target is a `TextInput` or `TextArea`
   (ApertureApp.cs:554; Search and the issue reply went back a page on Backspace since da0676d0). Small, app; each its
   own commit.
6. **text-textarea-cluster-walk-whole-line** (5; r10-5 Candidate 1) — `WrapLine` finds cluster ends only for rows that
   fall to `FindLineBreak`, from a known boundary before the row, not the whole hard line (`ClusterEnds`,
   TextAreaElement.cs:818, :646). Typing into a one-line 89k body with one emoji 1,216.8 → ~191 KB a key (−84 %);
   opening a 1 MB one-line bundle with an emoji 16,724.9 → ~327 KB (−98 %); docfx index.json ~−50 %. Rows identical
   (the `rows` corpora with an emoji or mark inserted, current walk as reference). Add a suite row ("input/type in a
   one-line JSON body with an emoji"). Small–medium, internal.
7. **apps-vesper-sync-cursor-rebuilds-rows** (6; r10-8 Candidate 1; **question 29: yes, reveal**) — Sync's rows get their
   index from the loop, not `VisibleSyncRows().IndexOf(r)` (CommanderApp.Sync.cs:349, :92: O(rows²)): 300 files 2,568.6
   → 1,715.9 KB a key (−33 %); then rows kept (`KeptElements` by path; state selected and direction) and Down/Up without
   a build: ~−95 %. Then the cursor reveal (approved): Sync scrolls to keep the cursor in view. Add the suite row "a cursor key over a plain 200-row list
   rebuilt through the root" with a kept-rows twin. Small + medium, app.
8. **Junction builds less** (6; r10-6 Candidates 2 and 4) — (a) **apps-junction-diff-rows-keep-width**: leave
   `codeWidth` out of the diff's height shape when it neither wraps nor shows a review (`JunctionApp.Diff.cs:519`,
   `InvalidateRows` :525): split drag 159.7 → 42.4 KB a move (−73 %), resize 32.8 → 21.0 KB a step (−36 %); pixel A/B
   at several widths. (b) **apps-junction-view-only-clicks**: `MarkViewDirty` where the chrome reads nothing changed
   (Staging.cs:56/69/72, Dialog.cs:68/82, JunctionApp.cs:1262, :1549–1589, :438/441): −118.6 KB a click by the part
   buckets; only sites shown view-only. Small, app; each its own commit.
9. **a11y-focus-find-down** (7; r10-7 Candidate 3) — `MoveA11yFocus` (Window.cs:864) finds the old and new focused
   nodes with `FindNodeDown` (:1046) instead of `FindNodes` over the whole tree (:1318), the whole-tree walk kept as a
   fallback; also `NotifyPendingA11yFocus` and `Announce`'s `FindA11yNode`. Meridian Tab at 2,000 endpoints 6,072 →
   ~58 visits. Scaling only; time it on a reader-on suite row. Small, no behaviour change.
10. **a11y-container-name-in-place** (7; r10-7 Candidate 2) — a node with children takes its own Name/Description
    change in place (the observer, Window.cs:4672; the walk, AccessibilityTreeBuilder.Refresh.cs:237; UIA `UpdateFrom`,
    AT-SPI `Refresh` keyed by ElementId). Vesper navigation with a reader, KB tree/UIA/AT-SPI: Detail 1,000 files 453.2
    / 541.4 / 674.8 → ~322.4 / 410.7 / 544.0 (−19…−29 %), Brief 60 −10…−12 %; also 60 of Junction's 61 audit
    fallbacks. Rerun the audit. Medium, no API or reader change.
11. **apps-meridian-map-virtual-list** (6 with 7; r10-1 Candidate 2) — Discovery's map as a `VirtualList` instead of a
    ScrollView over every row (MeridianApp.Discovery.cs:524–525): at 600 endpoints hit visits a hover move 617.5 → ~30,
    culls a frame 579 → ~30, the map's elements 2,498 → ~100 KB (−58 % of the heap with the map shown). Kept rows move
    into the row template; headings need `RowHeights`; a reader must still hear every row (item nodes; check with the
    audit). Medium, app.
12. **syntax-markdown-loads-every-grammar** (5; r10-5 Candidate 2; **question 30: route (c)**) — the first Markdown,
    Ruby, shell or F# file keeps ~29 MB of every embedded grammar until `SetTheme` (.md +29,444 KB, first call 82,868 KB
    allocated). Route (c), approved: load per text only the fence languages it names (scan fence info strings and
    HTML blocks); tokens equal on round 7 step 12's corpus. No TextMateSharp fork. Medium–large, no API.
13. **apps-pane-drag-sets-size** (6; r10-6 Candidate 3; absorbs Waiting apps-meridian-response-drag-build) — while a
    `PaneSplitter` drag runs, set the live pane's size through a `Ref` and `MarkLayoutDirty()`, build once on release:
    Meridian 2,000-line body 38.9 → 2.5 KB a move (−94 %), Junction Changes split 42.4 → 2.3 (after step 8). Mid-drag
    full-build equality check (Junction's `_diffPan.ViewportWidth`, Meridian's `SetMax`). **Question 32 (recommended no)
    keeps it app-only.** Small–medium, a drag: rank low.
14. **paint-text-lines-seek** (2; r10-2 Candidate 1; r10-5 Examined says under the time bar) — measure first: binary
    search for the first line the clip reaches in `DrawText` and `DrawParagraph`, only for labels of ~32 lines or more
    (prototype `PROBE_LINESEEK`): the 2,000-line row 1,999 → 60 line visits a step. Keep only if `step-ab.ps1` shows
    ≥ 5 % on `text/wheel over a 2,000-line plain label in a ScrollView`; else record it as examined. Small, no API.
15. **vesper-brief-scrollbar-columns** (1 with 6; r10-1 Waiting re-rank; **question 28 (a): public API approved**) —
    bug: Brief shows blank columns when scrolled with its scrollbar (the bar sets the offset with no `ScrollEvent`,
    Scrollbar.cs:161/190; Brief makes columns only on one, CommanderApp.Views.cs:586). ScrollView tells listeners when
    the user scrolls with the bar or by autoscroll (a new public callback or event; a scroll set from code still raises
    nothing; existing `ScrollEvent` listeners unchanged); Brief listens beside `ScrollEvent`. Test: a track click and a
    thumb drag over 400 files leave 0 blank columns (r10-1's `vbar` walk: 4 of 4 and 7 of 8 today). Docs (ELEMENTS.md,
    the scroll section); docfx 0 warnings. Small, framework + app.

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

Closed from Waiting by the round 10 survey: apps-toast-gone-view-only (→ step 5), apps-meridian-response-drag-build
(→ step 13, now 38.9–39.2 KB a move), vesper-brief-filter-clear-reveal's scrollbar half (→ question 28; the
filter-clear half stays).

## Landed

Round 9 (the round round 10's survey reviews; it moves to the archive at round 11's survey) and round 10. Rounds 1–8 are in
[the archive](17-performance-team-ledger-archive.md#landed-rounds-14) ([round 5](17-performance-team-ledger-archive.md#landed-round-5),
[round 6](17-performance-team-ledger-archive.md#landed-round-6), [round 7](17-performance-team-ledger-archive.md#landed-round-7),
[round 8](17-performance-team-ledger-archive.md#landed-round-8)).

| Round | Commit | Candidate | Before → after |
|---|---|---|---|
| 9 | 4142ee85, 0a9a9c64, 71f95ff1, 96dbc917 | apps-aperture-grid-columns-keep-cards | The objection to d366505b. A card goes back while its own row is still a child of the list (`CanTakeCard`, `place: null`, as round 7's `GridCards.TakeBack`); a hovered or pressed card only into its own row. d366505b's `place: key` remade every card on a change of columns, though a place moves to another row only then, and that invalidates every row first. Follow-up: across a change of columns the keyboard goes back to the cursor's card after the frame (that card and the first row's last card on a narrowing are still made again). Headless, 600 films, JIT idle-gated ×2: a column change each step 235.6 → 62.5 KB, elements 249 → 58, leaf/text measures 369 → 60 / 110 → 14.5; resize over 40 widths 13.6 → 4.9 KB (round 8 base 6.1); cursor keys unchanged (Right 0.3, Down 21.5, no build); AOT walk `library-resize-4px` 6.4 → 3.2 KB (below round 8's 3.6), CPU within noise. New suite rows (kept image grid across a change of columns: own-row 194 KB / 32.4 cards, kept 32 KB / 3.0); shared rows 0/0 ×2 (captures 96dbc917). d366505b's 7 scenes + a last-column scene, 96 checkpoints: pixels and tree as HEAD, 32 column changes as a forget-all grid, one ring on the cursor's card, the keyboard on it at all 48 (HEAD 4 of 16). Aperture 356, Headless 1,459 pass; GridRowKeepTests +4, GridCursorTests +3. REACTIVITY.md "A grid whose columns change passes no place". |
| 9 | 3a08f828, b4f182b5, 01f0886d | paint-label-lines-cull | The objection to 967ce1ac. Both `DrawText` overloads skip lines wholly outside `Canvas.LocalClipBounds` with `DrawParagraph`'s line-height slack (one helper, `LineOutsideClip`, now shared), checked before a line's runs are read; one-line labels read no clip; internal `CullsTextLines` (on) is the tests' pixel reference. Meridian Request tab, 89k-character body, JIT idle-gated ×2: open runs 1,911 → 192, blobs 1,771 → 61, render 538.5 → 13.8 KB (−97 %), step 3,797 → 3,277 KB (−14 %); wheel runs a frame 1,903 → 194 (−90 %), render 0 → 0.61 KB a frame (lines scrolling in, once each); AOT the same counts. New suite row `text/wheel over a 2,000-line plain label in a ScrollView`: min 0.361 → 0.238 ms JIT (−34 %), 0.383 → 0.227 AOT (−41 %), 0.10 → 3.10 KB a step; full suite 217 rows JIT + AOT ×2: 0 slower, 1 faster, allocations identical elsewhere (captures 01f0886d). Pixels: 14 in-process dumps, before/after differ only in the elapsed-time label, as A/A. `LabelLinesCullTests` 27 cases (plain/spanned, translated, 1.5×, rotated, in a layer, cut lines, stacked marks); 4 reverse edits fail; Skia 387, Core 5,064, Headless 1,459 (goldens) pass. |
| 9 | b44b4db5 | frame-restore-keeps-no-sysmenu | Correctness, found by the user's Snap Layouts hover. SDL's WM_WINDOWPOSCHANGED handler calls `WIN_SetWindowResizable` whenever a window leaves maximized or minimized, which rebuilds the style with `STYLE_NORMAL` (includes `WS_SYSMENU`): restore from maximized or minimized, full screen from maximized, a placement moving a maximized window turned 16C70000 into 16CF0000, and Windows then validated the flyout against the standard caption band (a bottom entry missed on both monitors for the window's life). The frame's WndProc answers `WM_STYLECHANGING` (GWL_STYLE) by clearing `WS_SYSMENU` from `styleNew` unless `ShowSystemMenu` is lending it (`_lendingSysMenu`); the lend strips from the current style afterwards. `CustomFrameSystemMenuTests` ([UiTheory], both frames, guarded): after every transition and monitor moves the custom frame has no `WS_SYSMENU` (the default frame keeps it), HTMAXBUTTON at the button's middle and 2 dip above its bottom, HTCAPTION on the title bar, Alt+Space and a caption right-click open the menu; 2 reverse edits fail. UiTests 21/21 (handedBack 0), Platform.SDL3 53, Core 5,064 pass. The user hovered by hand on the 4K and the 1440p after maximize, restore, minimize and monitor moves: "works now in every case". |
| 9 | ff6125a6, 7b88102e | dotnet-hover-holds-removed-view | Approved (question 14: re-hit-test). At the end of a frame that changed some tree's children (a build, a list realizing rows while drawing), closed a hit-testable overlay or ran `SetRoot`, the window checks the hovered element is still shown (in the root's tree or an open overlay's; not `IsHosted`, which a closed overlay keeps) and else hit-tests hover again at the last pointer position; the move, the capture release and the pointer leaving go through the same path (internal `PointerState.LeaveRemoved`). Not while a button is held or an OS drag is over the window; not when the pointer is outside. Fixes two old bugs: a move after a removal entered the surviving ancestors again and never left them; a tooltip still in its delay opened for a removed element. Retained heap, Meridian with an imported spec, map row clicked then `1`, pointer at rest, full GC, JIT ×2 / AOT: 50 endpoints 1,517 → 1,268 KB (−16 %; AOT −18 %), 500 7,383 → 5,318 (−28 %), 2,000 28,162 → 20,136 (−28 %); the removed tree no longer survives into gen 2. Per frame: a null check, or one flag read a tree; steady frames 0 B. Suite JIT + AOT ×2: bytes unchanged everywhere; `hover across 1,000 rows` 0.279 → 0.288/0.281 ms JIT, AOT flat; `table/TableView 10k sort` +5–6 % AOT in five pairs, JIT +2/−1 %, all in the build phase that runs none of the changed code — the AOT code-placement row round 7 recorded (±6 %, JIT is the check), accepted (captures 7b88102e). `HoverAfterRebuildTests` 12 + Meridian `AViewSwitchedUnderAStillPointer_LetsTheMapGo`; 3 reverse edits fail 8/4/2; Core 5,064, Headless 1,471 (goldens), Meridian 588, Vesper 896, Junction 1,082 pass. ELEMENTS.md (hover after a rebuild; enter/leave handlers must keep the element). |
| 9 | 6801d779, c4a459ef, d329e714 | layout-group-drag-label-list | The note on e7d10c63. `CollectGroupLabels` (a new `List` a call) replaced by a static `ForEachGroupLabel(el, token, visit)` with static lambdas (`MarkPaintDirty` on repaint, `EndDrag` on release): no list, nothing holds labels between walks. The scoped walk was declined (it would miss group labels outside the drag's scroll view; a few µs a move); 290 visits a walk stay. Vesper viewer drag, 20,000-line log (290 elements, 37 labels), JIT ×2 + AOT: a drag move 1.16 → 0.04 KB, a frame held below the view 3.74 → 2.62 (−30 %), hover 0.10 flat. New suite rows (400-line viewer, 1,202 elements): `text/selectable viewer drag select` 8.3 → 0.1 KB, min 0.762 → 0.750 ms JIT; `... drag held below the view` 8.6 → 0.4 KB (the rest is new lines' first highlight). Full suite 219 rows, `step-ab.ps1 -Pairs 2`, JIT + AOT: 0 slower, 0 faster, only the new rows' bytes moved, A/A clean (captures d329e714). `GroupSelectionDragTests` (spans both ways, copy, held scroll 7.2/14.85/22.5 px, end in 14 frames); `FrameAllocationTests` (a move allocates only the input, a held frame 0 B; 2,232 / 2,192 B under a reverse edit). Core 5,064, Headless 1,476, Vesper viewer/diff 97, Junction diff/selection 88 pass. |
| 9 | 5c9d6b8b, a5efa8d0 | a11y-patch-diff-lent | The window keeps one `TreeDiff` (`A11yPatchDiff()`) and lends it in `TryPatchA11y` and both branches of `TryRefreshA11yInPlace`, emptied in each caller's `finally` (internal `TreeDiff.Clear()`), only to bridges carrying the internal marker `ITreeDiffBorrower` (UIA and AT-SPI: they loop over the diff during the call and keep no field); the headless bridge and others still get a fresh diff. The suite's `UiaRig`/`AtSpiRig` carry the marker too (HultUI.Core grants `InternalsVisibleTo` to the PerfSuite; six suite `OnAfterBuild` overrides became `protected internal`). Suite, JIT = AOT: `a11y/type in TextInput` UIA/AT-SPI 1.3 → 1.0 KB (−23 %), `click checkbox in 500 rows` UIA 0.8 → 0.5 (−38 %), AT-SPI 0.7 → 0.4 (−43 %), 15 more a11y rows −0.3…−0.4 KB, bridge-off twins flat; full suite `-Pairs 2` JIT + AOT 0/0, A/A clean, only a11y rows' bytes moved, all down (captures a5efa8d0). Apps KB a key (tree/UIA/AT-SPI), JIT ×2 identical + AOT: Meridian prompt typing 1.84/1.85/1.91 → 1.57/1.57/1.63 (−15 %), Backspace AOT 0.81/0.81/0.87 → 0.52/0.52/0.58 (−33…−36 %), Vesper Detail cursor AOT −18…−20 %, Meridian History Down −2.5 %. Event streams identical; audit 0 STALE, 0 WALK-VALUE/EVENT, 6,018 walks, fallbacks 125. `A11yLentDiffTests` (real AT-SPI any OS, real UIA on Windows; 8 fail when a fresh diff is forced); four a11y test wrappers carry the marker. Core 5,064, Headless 1,486, Linux bridge 104 (Windows and Fedora), Fedora Headless a11y 217, Meridian 588, Vesper 896 pass. |
| 9 | 2d07f529, 28ec28fd, c152a279 | text-textarea-mono-ascii-breaks-by-count | Approved (question 16). `WrapLine` calls an internal `BreakByCount` (~25 lines): a wrapped row of printable ASCII in a proven monospace face breaks at floor(width / advance) and walks back to the last space as the search does, falling back to the search within the proof's rounding of a multiple of the advance or when the first n + 1 characters aren't printable ASCII; n ≥ 1 and a finite width guarded. KB to open, cold (warm), JIT ×2 + AOT: Meridian toc.html 8,104.5 → 329.7 (6,862.7 → 181.5), pretty 6,524.4 → 378.1, one line 9,307.7 → 276.8; Junction 1 MB bundle 63,637.0 → 360.5 (62,501.2 → 145.9); index.json 11,821.5 → 5,084.9 (regenerated docs: 74 % of it on non-ASCII lines, still searched); TODO.md 2,652.5 → 1,987.2. Wheel after the open shapes words when first drawn (one-line 83 KB page +363 KB over 20 notches against −1,655 at the open). Rows: 11 corpus runs, both faces, 216–396 widths incl. 196 near-multiples, 37.4 M rows, 0 differ; 48,000-layout hash and both 240,000 corpora unchanged. New suite row `text/open a minified file in a monospace TextArea` (256 K characters on one line): 26.385 → 1.021 ms JIT (−96 %), 28.615 → 1.046 AOT, 9,016.4 → 38.4 KB; full suite `-Pairs 2` JIT + AOT 0 slower, 1 faster, A/A clean (captures c152a279). `TextAreaBreakByCountTests` + 4 in `TextAreaFitsByCountTests` (5 reverse edits fail); Text 293, Core TextArea 76, Headless 1,489 (goldens), Meridian 588, Vesper 896, Junction 1,082; Fedora Headless 1,333, Core TextArea 76 pass. DECISIONS.md (the fits-by-count entry, retitled). |
| 9 | 7e964aeb, f5609009, 45b78a04 | gl-vk-live-resize-stuck-post + vk-interop-check-allocates | (a) A held resize draws for a waiting post once, when the queue's count moves (reset when it drains) — except while a GPU source that signals only by posting may be on screen (an app's own `IGpuImageSource` keeps one post in flight; the count rule alone would freeze it, 2a332218): internal `GpuFrameSink.MaySignalOnlyByPosting` from an internal `asksForLiveFrames` flag that MediaPlayer and WebViewController set. Guarded real windows, JIT ×2 + AOT: a no-op post a step, edge held 1 s: GL 65/64 → 0, Vulkan 8-bit 65 → 0 (one more post 62–64 → 1); frames a step 2 → 1; Meridian's preview on screen held 61–65 → 0, 32 steps 64 → 33 frames; an own posting-only source still drawn every tick (66 → 64). (b) `TryEnableInterop` is `_interopReady ?? EnableInterop()`: Vulkan 8-bit and F16 with a web view 24.0 → 0.0 B a frame (JIT ×2, AOT), allocation ticks 4–5 → 0 over 20,000 frames; GL 0. 64 guarded launches, 0 guard firings, 0 new `nvlddmkm`, no stall over 1 s; idle after a drag 0 frames, 0 wakes. Suite `-Pairs 2` JIT + AOT 0/0, no allocation moved (captures 45b78a04). `ALiveFrame_DrawsForAWaitingPost_Once`, `VulkanInteropCheckTests` (no GPU: 24,000 B per 1,000 calls → 0); Core 5,067, Headless 1,490, Platform.SDL3 54, WebView 73, Media 83, UiTests LiveResizeFrame* + VulkanProbeTests 44/44 pass. THREADING.md, MEDIA.md. |
| 9 | 0ef0b61f, fa2477cf, 6543b6fc | apps-meridian-typing-builds-nothing + dotnet-meridian-windows-keyring-memo | (a) An Environments cell builds the view only when `doc.Edited` flips; a header value only when the URL box's check fails; the body also when its body type changes (the first character into a request with no body block shows "Body json"). KB a character JIT (AOT), elements, keyring Gets: Environments 3×12 312.2 → 2.3 (304.9 → 2.3), 264 → 0, 10 → 0; 3×40 842.7 → 5.4 (819.8 → 5.4), 676 → 0, 20 → 0; 5×40 1,166.9 → 5.4, 916 → 0, 36 → 0; header 65.1 → 2.1, body 59.3 → 3.8; min a character −9…−23 %; with a reader 2.6–5.9 KB. 6 scenes × 90 steps as built and with a forced build after every key: pixels, tree, focus, caret, text, files on disk identical (A/A identical; the body-type check removed breaks the last scene). (b) `WindowsCredentialKeyring` on `KeyringMemo` (remembers not-found, asks again after a failure, ignores letter case as Credential Manager does; native calls behind an internal `ICredentialStore`); `KeyringMemo` keys entries by their three parts, so a remembered answer allocates nothing on all three stores (the Linux/macOS store files changed mechanically, compiled, not run here: CI's meridian-keyring job checks them). Fake store, 12 secrets × 3 environments, JIT: response-pane drag with the Request tab 59.4 → 0 reads a frame, 57.1 → 47.1 KB (−18 %); a full Environments build 66 → 0 reads. README: the restart rule on every OS. Suite `-Pairs 2` JIT + AOT 0/0, no allocation moved (captures 6543b6fc). Tests: 2 typing tests, 3 `WindowsKeyringMemoTests`, `PartTests`' view-only walk types a variable instead; reverse edits fail; Meridian 593 + 4 skipped, Headless 1,490 pass. REACTIVITY.md. |
| 9 | c940c643, 164668f3 | apps-meridian-tree-rows-kept | The objection to 41f48bd3. Request and folder rows come back through `KeptElements` keyed by path (request state: method, title, lit, unsaved, first produced variable, uses count, depth, scale; folder: name, depth, scale); every tree-part build calls `Begin`, opening a workspace `Clear()`s, every child keyed. Bug fixed on the way: the folder menu captured the scan's `CollectionFolder`, so a kept row's Delete… would count stale requests — it now looks the folder up by path when picked. Internal `RequestRowsMade`, `ForgetTreeRows()`. Headless UI-thread KB, JIT ×2 identical (AOT within 1 %): a full build 25 requests 327.6 → 127.6, 200 requests 1,587.5 → 132.8 (flat with collection size), elements 1,407 → 117, tree part 1,389.2 → 15.8; a toast 3,180.7 → 272.1; a send (both builds) 3,270.4 → 361.0; opening a request 1,587.6 → 148.2. 29 scenes kept vs forget-all (opens, a send, edit, save, rename/delete through the menus, new request and folder, folder rename, environment switch, toast, hover and focus on a row and its ⋯, History and back, changes on disk, scale 1.5): pixels, tree, focus, elements and every row's menu items identical. Suite `-Pairs 2` JIT + AOT: no regression, no allocation moved (JIT pair 1's three slower rows — two a11y bridge rows, the media bar fade, in its noise line — didn't repeat) (captures 164668f3). 3 PartTests (4 reverse edits fail); `AssertPartsAreAFullBuild` forgets kept rows first; Meridian 596, Headless 1,490, Aperture 356, Vesper 896 pass. REACTIVITY.md. |
| 9 | 25a96234, 3fdf33c3, 073e1100 | vesper-brief-scroll-before-build (+ kept-elements-prune-keeps-this-build) | (Core, bug found in the step) `KeptElements`' prune dropped elements the build in progress had already made (not in the window until the reconcile) once a build made more than 64, so the next key found no live cell and built them all again. (Vesper) Brief applies the scroll it is about to set at build time (`ScrollBriefBeforeBuild`, every `RequestScroll` path: keys, filters, loads), clamped to the columns the build makes; no `ScrollEvent`, no API. Builds and KB a key, base → prune → both, JIT (AOT): Down ×402 40 → 24 → 12 builds (one a scroll), 25.4 → 6.3 → 4.9 KB (24.7 → 4.5); Up 24 → 12 builds, 5.70 → 4.95; Right ×14 333 → 86 KB; type-to-find 251 → 213; page jumps and Home/End now make the columns they show (+1 % net on Home/End). Blank columns in view after a key: Up 11, page keys 18 columns, Home/End 48, Left 11, find 3 → 0 everywhere. Every step equal to a full build (pixels, tree, elements, focus), JIT and AOT; before 3–12 steps a walk differed. Suite `-Pairs 2` JIT + AOT 0/0, no allocation moved (captures 073e1100). `ABuildThatMakesManyCards_KeepsEveryOne` + 4 Brief tests (reverse edits fail); Vesper 898, Headless 1,491, Aperture 356, Meridian 596 pass. ELEMENTS.md: a scroll set from code raises no event. |
| 9 | cb1df8c0, 3c945439, df13e8c4, d1274992, 56acc2eb | Meridian discovery at scale | The objection to 12d0392e. (reader, bug) a map row now tells a reader it is selected (`AccessibleSelected`). (a) A map click builds the view only and hands back every row and group heading through `KeptElements` (key endpoint key; state the endpoint record, selected, scale), line order/keys/counts worked out once per map, kept rows let go on a build without the map. KB a click JIT (AOT the same bytes): 60 endpoints 365.3 → 54.7 (−85 %), 600 2,846.6 → 77.2 (−97 %), 2,000 9,073 → 124.4 (−99 %); elements 333/2,493/8,129 → 55/55/63; part builds 2 → 0; min 6.4 → 2.4 ms at 2,000 (AOT 3.82 → 1.60); ~35 B an endpoint still grows (the list's children); kept rows +0.66 MB at 2,000. (b) The parse reads with `Utf8JsonReader` and a small `JsonDocument.ParseValue` per path item (the brief's premise was wrong for .NET 10: the transcode is 1 B a character; JsonDocument's doubling token table, 1+2+4 MB, is what stays pooled): pool leftover 9,244 → 51 KB; retained with the map open at 2,000 23,028 → 13,755 KB (−9.1 MB, JIT and AOT); import 45.5 → 31.1 MB; parse 8.4 → 7.6 ms; 18 specs × 4 encodings parse equal (broken specs: same message, line, column); a non-object `paths`/`info` now fails cleanly instead of crashing. (c) `PathMatches` splits no strings, `Templatize` makes a string only for an unseen segment, `SaveMap` hands the map to a background writer (latest per file, streamed, same bytes; `Dispose` waits): UI KB a send 504.8 → 6.0 (600), 1,126 → 16.9 (2,000). Pixels of a 12-click walk byte-identical; a click walk equals a full build (pixels, tree, focus). Suite `-Pairs 2` JIT + AOT 0/0, no allocation moved (captures 56acc2eb). Reverse edits fail; Meridian 647 + 4 skipped, Headless 1,491 pass. REACTIVITY.md, NATIVE-AOT.md (JsonDocument's pooled arrays). |
| 9 | 58aeb965, 74cf844c, d2961b06 | webview-hidden-keeps-interop + meridian-dispose-hidden-preview | (a) A hidden WebView2 page gives its capture's GPU memory back in the window's next frame (internal `GpuFrameSink.ReleaseWhileHidden`, run by `Window.RenderFrame` with the context current, only for engines marking themselves `IReleasesGpuWhileHidden`: WebView2 and the fake; CEF unchanged); no capture restart on show (a restart drew black, white, then the old page; left running, the capture delivers the frame on every show); the sink's GL texture kept (0 MB freed on GL); the interop device stays open until dispose and the D3D11 context is flushed after the release (else +7.4 MB a cycle) — rejected parts in DECISIONS.md. Also: a drawn element shows its page again if another element (a rebuild's twin) hid it. Guarded windows, DXGI local, 2160×779 page at 100 % (the 4K was unplugged), GL JIT ×2, Vulkan 8-bit ×1, GL AOT ×1: GL hidden 87.1 → 72.2 MB (−17 %), shown 87.1 flat, flat over 5 cycles; Vulkan hidden 133.7 → 96.6 (−28 %), shown 133.7 → 141.1 (+7.4 MB once from the first hide, flat after: accepted, Vulkan is opt-in); page frame after a show 14–28 ms. (b) Meridian disposes a preview off screen (`IsHosted`) for a minute (a one-shot timer on `Application.Time`): hidden GL 72.2 → 41.6 MB, Vulkan 96.6 → 66.0, `msedgewebview2` 6 → 0; the next "web" toggle's first page frame 247–281 ms. Idle shown or hidden 0 frames, 0 wakes (1 wake at the deadline); steady 0 B; 0 guard firings, 0 new `nvlddmkm`. Suite `-Pairs 2` JIT + AOT: AOT 0/0; JIT pair 1 richtext/open +15 %, text/wrap resize +17 %, two more text/ pairs within noise (captures d2961b06). GpuFrameSinkTests 3, WebViewHiddenGpuTests 3, a WebView2 UiTest, Meridian `AHiddenPreview_IsLetGoAfterAMinute_AndNotBefore` (reverse edits fail); WebView 76, Media 86, Headless 1,491, Meridian 648 + 4 skipped, web view UiTests 25 (+6 foreground-only skipped). WEBVIEW.md, DECISIONS.md, Meridian GUIDE.md. |
| 9 | d00f7f65, 39e378ad | apps-tokenize-per-line-bytes | Profiled: a line's bytes are mostly TextMateSharp's (AOT, KB a line: JSON 4.10 of which TextMateSharp 3.68, XML 5.19 / 4.26, HTML 14.71 / 13.96; HTML two thirds Onigwrap capture results), left alone (no fork). HultUI.Syntax's own share was `ColorFor`'s `Theme.Match` per token (58–75 B a token: 8.2 / 12.2 / 3.4 %): a token's colour is now kept per scope-name list (keyed on a copy, capped at 4,096 lists, cleared on `SetTheme`; a text has 15–126 lists). KB a call JIT (AOT): JSON 7,909 → 7,240 (−8.5 %; 8,201 → 7,532), XML 12,362 → 10,803 (−12.6 %; −12.2 %), HTML 51,695 → 49,895 (−3.5 %); time equal or faster (AOT 15.5 → 13.9, 16.8 → 15.6, 100.2 → 97.8 ms). Meridian pretty toggle on big.html, 20 toggles: 55.1 → 53.3–53.6 MB JIT, gen-0 35–36 → 34. Spans: the round 7 step 12 corpus rebuilt (3,206 files, 1,752,925 lines, 4,130,773 spans) identical. Suite `-Pairs 2` JIT + AOT 0/0, no allocation moved (no row reaches HultUI.Syntax) (captures 39e378ad). `TokenizeBytesTests` (32 B a token vs 104 with the old match; bar 60); Syntax 86, Meridian 648 + 4 skipped, Junction 1,082 pass. DECISIONS.md. |
| 9 | b4dc84b4, 8f756384, 2cf31e2c, 65929882, 76dce98a | reader focus in Vesper and Meridian (question 19, app code) | Not perf. Vesper: the pane is a Group named "Left pane, <folder>" (the focused node was null in every view); Tab moves the keyboard focus to the pane it makes active (0 focus events in 10 Tabs before; now the pane, then in Detail its selected cursor item). Cursor keys 0 trees, KB a key unchanged (Detail 1.50/1.68/1.50/1.72 off/tree/UIA/AT-SPI); the folder in the name costs a navigation with a reader 0 → 1 tree (~+20 KB; question 24). Meridian: a prompt or confirm card's Escape gives focus back to its opener; the Request tab keeps focus when its reveal button goes; History's list (named, a Tab stop) takes focus when the view opens and holds it through the walk; 0 trees, KB within 0.5 a step; focus rings now show only on buttons getting focus back after Escape/Enter. Brief and Thumbnails NOT fixed: telling the cursor item with 0 trees needs the window's selected-state patch gate (Window.cs:4659) widened to cells/tiles plus `RowsAreComposite` on the thumbnail list (prototype `r9b15\proto-gate-cells.patch`: 0 trees, flat KB; +~100 nodes a navigation with a reader) — question 25. Audit 0 STALE, 0 WALK-VALUE/EVENT; Vesper fallbacks 55 → 9. PaneReaderFocusTests 2, ReaderFocusTests 5 (reverse edits fail); Vesper 900, Meridian 653, Aperture 356, Headless 1,491 pass; a11y/ rows' bytes unmoved. |
| 9 | 6c518629, f9a7cc74, 8a6d7bb7, 0e6bc590 | paint-image-fades-itself + vesper-sync-equal-rows-fade-colours | Approved (questions 20 and 21; pixels). (a) A faded image draws its picture at its alpha with no offscreen group (internal `IFadingImageContext` on SkiaRenderContext and RecordingRenderContext; IVT to HultUI.Rendering.Skia; GPU and pixel-stream sources keep their layer). (b) Sync's Equal rows fade their colours, not the row. Real windows, AOT, ABAB+BABA GL and Vulkan 8-bit, F16 ABAB, guarded: Detail layers 26.6 → 0, Down/Up GL 1.147 → 0.827 ms (−28 %), GPU −72 %, Vulkan 8-bit −17 % / −84 %, F16 −18 % / −64 %; Brief 61 → 0, GL 1.458 → 0.824 (−43 %), GPU −84 %, Vulkan −29 % / −91 %, F16 −29 % / −78 %; Sync 26.3 → 0, Down/Up GL 0.833 → 0.516 (−38 %), GPU −77 %, Vulkan −20 % / −82 %, F16 −26 % / −62 %, wheel/hover GL −37/−38 %. Pixels as approved: Detail icons 611/431/8 of 6,976 px ≤ 2/255; Brief 0/0/360 at 1/255; Sync 13.7k/14.1k/8.8k at 1/255 + 3 px at 9/255 on 8-bit; A/A 0; goldens unchanged. New suite row `paint/400 faded icons` 2.971 → 2.283 ms JIT, 2.737 → 2.088 AOT; suite `-Pairs 2` JIT + AOT 0 slower, no allocation moved (captures 0e6bc590). `OpacityLayerTests` 3, `SyncEqualRowTests` (reverse edits fail); Skia 387, Core 5,067, Headless 1,495, Vesper 901, Aperture 356, Meridian 653 pass; 0 guard firings, 0 `nvlddmkm`. DECISIONS.md (Rendering), ELEMENTS.md (Opacity). |
| 9 | ea42f45b, 9f83f755, 8cea790c | a11y-selected-patch-for-cells | Approved (question 25). A selected state is marked in place for a menu entry, a row directly in a composite list, and now any element whose `Role` is `ListItem` that says selected or not both before and after; anything else (a tab, a radio, a button in a set) still builds a tree (narrower than step 15's prototype, which took every element). Vesper: Brief cells and thumbnail tiles are named `ListItem`s (the entry's name, without the mark: a renamed node with children builds a tree), `ShowCursor` sets `AccessibleSelected` with the ring's `Restyle`, the thumbnail list `RowsAreComposite`, dead `ThumbRowLabel`s gone. A reader now hears the item left and the item reached on every Brief/Thumbnails cursor key (before: nothing), and on Tab the pane then its item in all views; 0 trees a key, a Tab or a mark with tree/UIA/AT-SPI; KB a key Brief 2.21–2.33 → 2.49, Thumbnails 1.81–2.06 → 1.96; a navigation with a reader +34/+95 nodes (Brief UIA 237 → 250 KB, Thumbnails 262 → 294), without within 0.5 KB. Audit 0 STALE/WALK-VALUE/WALK-EVENT, fallbacks 10; Aperture grid and Discover, Meridian History/prompt/Request tab event streams identical. Suite `-Pairs 2` JIT + AOT 0/0, all 67 a11y rows' bytes equal (captures 8cea790c). A11yLeafPatchTests + Vesper tests (reverse edits fail 4 / 2 / 1); Core 5,067, Linux bridge 104, Headless 1,501, Vesper 903, Meridian 653, Aperture 356, Junction 1,082 pass. ACCESSIBILITY-PLAN.md, ELEMENTS.md. |
| 9 | ae32c9d8, bf74fbca, d9f55f21 | syntax-encoded-tokens-colours | Approved (question 23; colours). Colours from TextMateSharp's encoded tokens (`TokenizeLine2`), a colour id straight to a colour (step 14's per-scope-list cache gone); bold/italic still not from the theme (they would change widths). bf74fbca: TextMateSharp 2.0.4 never applies a theme rule naming a parent scope (it looks for the rule's own scope among the parents too); a grammar whose root scope is a rule's one parent gets its own registry with that parent dropped and the rule last (PowerShell, Less) — no fork, no API. KB a call JIT (AOT): JSON 7,240 → 5,112 (−29.4 %; −28.3 %), XML 10,803 → 8,765 (−18.9 %; −18.2 %), HTML 49,895 → 47,675 (−4.4 %); min JIT JSON 10.3 → 7.2 ms, XML ~11.9 → 10.4, HTML flat; Meridian pretty toggle 20× 53.4 → 51.1 MB JIT, gen-0 34–35 → 32–33. Corpus (3,094 files, 1.75 M lines, per token against the base): Dark+/Light+ change exactly step 14b's 2,388 lines / 9,128 tokens, then bf74fbca takes back the 61 PowerShell member tokens (2,335 lines, 9,067 tokens; one `$script:` line stays); other themes 0; no JSON/XML/HTML line; states equal. Suite `-Pairs 2` JIT + AOT no regression, no allocation moved (captures d9f55f21). `ThemeColourTests` (Dark+ for TS/JS keys, `new`, a C member, PowerShell members in .ps1 and a Markdown fence), `TokenizeBytesTests` 20–32 B a token; reverse edits fail; Syntax 94, Meridian 653, Junction 1,082, Vesper 903, Headless 1,501 pass; no golden changed. DECISIONS.md. |
| 10 | f451a9ef, 8c02c5a2 | a11y-selected-button-patch | The objection to cb1df8c0 with ea42f45b. A selected state is marked in place also for an element whose app-set `Role` is `Button` and that says selected or not both before and after (`element.Role is ListItem or Button`); an inferred button (`ButtonElement`, a clickable box), a button that starts or stops saying selected, tabs, radios and every other role still build a tree. Meridian map click, KB off / tree / UIA / AT-SPI, JIT: 60 endpoints 58.1 / 92.3 / 92.7 / 92.8 → 58.1 / 58.3 / 58.4 / 58.6; 600 81.5 / 343.6 / 343.9 / 344.1 → 81.5 / 81.8 / 81.8 / 82.0; 2,000 120.8 / 973.5 / 973.9 / 974.0 → 120.8 / 121.0 / 121.1 / 121.3; trees a click 1 → 0. A reader hears the same: ordered UIA raises and AT-SPI signals identical in all 90 reader-mode scenarios (Vesper 48, Meridian 30, Aperture 12); only the 9 map-click rows of 121 moved. Audit 0 STALE in 3,367, 0 WALK-VALUE/EVENT, fallbacks 83 (Junction 62 vs 61 with the rule reverse-edited: run-to-run; no Junction element reaches the clause). Suite `-Pairs 2` JIT + AOT 0/0, no allocation moved, all 67 a11y rows identical (captures 8c02c5a2). `A11yLeafPatchTests` +3 per bridge (a selected button patches; a Tab still builds; a button that starts saying selected builds; reverse edits fail 2/2); Core 5,067, Headless 1,505, Linux bridge 104, Meridian 653, Vesper 903, Aperture 356, Junction 1,082 pass. ACCESSIBILITY-PLAN.md, ELEMENTS.md. |

## Examined, not worth it

From the round 10 surveys (details in `17-reports/r10-*`):

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

Round 10 questions (from the survey, 2026-11-09). Recommendations in brackets.

27. **aperture-backspace-goes-back** (r10-6; bug, not perf, since da0676d0 on 2026-10-05): Backspace in a text field
    also goes back: typing "ab" in Search and pressing Backspace leaves "a" and goes to Home; the issue reply does the
    same (`browse.back` = Escape or Backspace, ApertureApp.cs:554; only the cursor keys check for a text field). (a)
    Aperture skips `browse.back`'s Backspace when the target is a `TextInput` or `TextArea` (small, app), or (b) text
    fields mark the keys they consume as handled (changes key routing in every app)? [(a).] → **(a)** (2026-11-09;
    step 5).
28. **vesper-brief-scrollbar-blank-columns** (r10-1; bug): scrolling Brief with its scrollbar shows blank columns (a
    track click 4 of 4 blank, a thumb drag 7 of 8 moves with a blank column, 0 builds). (a) Framework: ScrollView tells
    listeners when the user scrolls with the bar or by autoscroll (a new public callback or event; Brief listens beside
    `ScrollEvent`; a scroll set from code still raises nothing), (b) Brief hides its horizontal scrollbar, or (c) Brief
    makes every column (400 files: 15 columns, ~400 cells a build against 5 columns)? [(a): public API.] → **(a),
    the framework callback** (2026-11-09; step 15).
29. **vesper-sync-cursor-reveal** (r10-8; bug): Sync's cursor leaves the view and nothing scrolls it back (after 40
    Downs in a 26-row view no highlighted row is on screen, `r10-apps\drv\shots\sync40.png`). Reveal the cursor
    (scrolling the user will see)? [Yes, with step 7's kept rows.] → **yes** (2026-11-09; step 7).
30. **syntax-markdown-grammars-route** (r10-5; step 12): the first Markdown/Ruby/shell/F# file keeps ~29 MB of grammars.
    (a) drop the registry after an idle minute (colours kept, the next file pays ~83 MB allocated and colour arrives
    later), (b) limit Markdown's fence languages (other fences uncoloured: a colour change; the 18 common ones still keep
    17 MB), or (c) load per text only the fence languages it names (no colour change, medium–large)? [(c).] →
    **(c)** (2026-11-09; step 12).
31. **ref-lets-go-when-removed** (r10-4; optional, framework): should the public `Ref<T>` let go of its element when the
    element leaves the window? It would fix every app holder at once but changes what `Current` returns to code that
    reads a removed element (Aperture's Library.cs:45 does). [No: fix the apps (step 2) and write the guideline.] → **no**
    (2026-11-09; the user left it to the recommendation).
32. **panesplitter-sizes-pane** (r10-6; optional, public API): should `PaneSplitter` take the pane's element and size it
    during a drag, calling `onChanged` only on press and release? [No: step 13's app refs need no API.] → **no**
    (2026-11-09; the user left it to the recommendation).

Round 9 question 26 (from step 14b, 2026-11-09), still open. Round 9's answered questions 14–25 are in
[the archive](17-performance-team-ledger-archive.md#needs-the-user-round-9). Recommendations in brackets.

26. **syntax-merged-theme** (from step 14b; not perf, not blocking): today and the encoded path both differ from VS Code
    on ~974k corpus lines because TextMateSharp keeps an included theme in a second lookup and the main theme's broader
    rule wins: JSON keys are teal where VS Code shows #9CDCFE (every key in Meridian's JSON bodies), `this` is #9CDCFE
    where VS Code shows #569CD6. Giving it one merged theme would match VS Code. Wanted? [Later, its own step with
    images: it recolours every JSON key.]

Round 9 questions 14–25, round 8 questions 11–13, round 7 questions 1–4, round 6 questions 1–9 and round 5's question 5,
all answered, are in the archive ([round 9](17-performance-team-ledger-archive.md#needs-the-user-round-9),
[round 8](17-performance-team-ledger-archive.md#needs-the-user-round-8),
[round 7](17-performance-team-ledger-archive.md#needs-the-user-round-7), [round 6](17-performance-team-ledger-archive.md#needs-the-user-round-6)).
Recommendations in brackets.

10. **tokenize-diff-public** (from step 12; not blocking): `TokenizeDiff` in HultUI.Syntax tokenizes only the lines a diff
    shows and is `internal`, with Junction as a friend assembly. Another diff view would want it. Make it public? [Yes,
    but later: no second user exists, and a friend assembly costs nothing. Decide when one turns up.]

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

From 2026-11-08 (round 9's survey); earlier entries are in the archive ([to 2026-10-25](17-performance-team-ledger-archive.md#log-2026-10-07-to-2026-10-25),
[2026-10-26 to 2026-10-27](17-performance-team-ledger-archive.md#log-2026-10-26-to-2026-10-27),
[2026-10-27 to 2026-11-02](17-performance-team-ledger-archive.md#log-2026-10-27-to-2026-11-02),
[2026-11-02 to 2026-11-03](17-performance-team-ledger-archive.md#log-2026-11-02-to-2026-11-03),
[2026-11-03 to 2026-11-08](17-performance-team-ledger-archive.md#log-2026-11-03-to-2026-11-08)).

- 2026-11-08: round 9 survey started in a fresh context (session c484f5ac). Main held only round 8's own merge
  10acc8c0, merged into the branch as 85400e17 (tree unchanged). Moved to the archive by a checked script (every moved
  line found there, no line lost): round 7's 30 Landed rows, round 8's build order with the survey's closed-from-Waiting
  note, round 8's answered questions 11–13, and round 7's 139 log lines (2026-11-02 to its release on 2026-11-03).
  Weekly usage 12 %. The eight experts run in their own worktrees, reviewing eaa1ec29..10acc8c0 (78 commits, main's
  merge 3a15b5b1 with 32 commits from other sessions included).
- 2026-11-08: survey reports in so far: r9-1 layout, r9-4 .NET, r9-2 painting, r9-6 reactivity (all not satisfied).
  The user answered expert 4's two questions early: a rebuild under a still pointer re-hit-tests hover (question 14),
  and Windows memoizes keyring reads as Linux and macOS do (question 15).
- 2026-11-08: r9-5 text in (not satisfied). The user answered question 16: round 4's monospace fits-by-count approval
  covers breaking wrapped ASCII rows by count.
- 2026-11-08: round 9 survey done (reports 031d48b4..4919ac40, `17-reports/r9-*`). No expert satisfied. Review
  objections: d366505b remakes every Aperture grid card on a column change (+131 % resize walk; experts 1 and 8, bisected;
  step 1); from main's merge, the Request tab's plain label draws all lines every frame (step 2), Windows' keyring has no
  memo (step 8), a large spec read as a string (step 11), the tree rows' actions (+42…+51 % a full build; step 9), and the
  edge-scroll tick's per-frame group walk (step 4). Round 8 helped the apps: 49 walks −18 % KB/key (geometric mean
  0.78). Found independently by two or three experts: Vesper Brief's column lag (1, 6; step 10), Meridian Environments
  typing (4, 6, 8; step 8), Discovery clicks (4, 6, 8; step 11). Biggest new leads: a removed view held by the hover
  fields (−10…−40 % of Meridian's heap; step 3), monospace rows broken by count (−74…−99.8 % on a big open; step 6).
  14 build steps; questions 14–16 answered during the survey, 17–19 asked (web preview dispose, window checks on the 4K
  monitor, Vesper reader focus). The 4K monitor at 150 % is now attached and primary. Survey agents 267–373k tokens
  each; weekly usage 12 → 17 %. Phase → build.
- 2026-11-08: the user answered questions 17–19: Meridian disposes a preview hidden for about 60 s (step 12); Vesper's
  reader focus is fixed in the app (new step 15, with meridian-focus-lost from Waiting); the window checks "later",
  then "run the window tests now": 11 of 11 UiTests pass on the 4K at 150 % (primary) and the 1440p at 100 %, run
  in a scratch worktree so step 1's build was untouched. The user saw the windows only on the 1440p (every UiTests
  window is made there first; the 4K cases are brief moves), so a held check (each window 3 s on top, no activation)
  ran while the user watched the 4K: seen there, dpi 144, the Borderless maximized client the work area to the pixel.
  The user's Snap hover found a bug: Meridian on the 4K misses a bottom entry into the maximize button (step 16);
  Vesper works. Probing: Meridian's hit test covers the whole button; synthetic cursor glides (`SetCursorPos`,
  `SendInput`) opened no flyout on an inactive window. Test trap → Waiting uitests-workareas-before-dpi-aware.
- 2026-11-08: round 9 step 1 landed (4142ee85, 0a9a9c64, 71f95ff1; captures 96dbc917): Aperture's grid hands its
  cards to their new rows across a change of columns (−73 % a step, elements 249 → 58; resize −64 %, AOT −50 %) and
  keeps the keyboard on the cursor's card (HEAD lost it at 12 of 16 column changes); the objection to d366505b is
  closed. Guideline notes: the row-place check is for rows that keep their place — a grid that reflows checks the
  card's own row is still in the list; leave a hovered or pressed card in its own row (as VirtualList's moved rows);
  a card whose ring moved with no build is made again, so if it held the keyboard ask for it back after the frame.
- 2026-11-08: step 16's cause found with the user's hand checks (Meridian from main in the primary checkout, then
  Vesper): restoring a window from maximized puts `WS_SYSMENU` back, so Windows validates the Snap Layouts flyout
  against the standard caption band and a bottom entry misses on both monitors, for the window's life. Not DPI: a
  fresh window dragged across monitors kept the full button. Clearing the style from outside fixed it (the user saw
  it). Step 16 runs next, ahead of step 3, while the user can hover by hand. The flyout's first frame at the
  monitor's top-left is the Windows shell's (memory snap-layouts-caption-band).
- 2026-11-08: round 9 step 2 landed (3a08f828, b4f182b5; captures 01f0886d): a plain label draws only the lines its
  clip reaches — Meridian's Request tab with an 89k body: wheel runs a frame −90 %, open render −97 % (step −14 %);
  new suite row −34 % JIT, −41 % AOT; pixels identical; the objection to 967ce1ac closed. Guideline notes: a plain
  label culls by its clip like a selectable one — no need to switch a long read-only text to a TextArea for paint;
  a colour stretch clips its glyphs to their line's box, so stacked marks above a line are cut in spanned text, not
  plain (an existing difference); publish the AOT suite through a wrapper project that sets `PublishAot` (NETSDK1207).
- 2026-11-08: round 9 step 16 landed (b44b4db5): a custom-frame window keeps `WS_SYSMENU` off whatever rewrites its
  style (SDL's `WIN_SetWindowResizable` on leaving maximized or minimized), so the Snap Layouts flyout covers the whole
  maximize button after a restore; the user checked by hand on both monitors: "works now in every case". With that,
  f9bceb6d's window checks (Open since round 6) are all done and the item is closed. Guideline notes: SDL rebuilds a
  window's style on every change out of maximized or minimized, so a frame that needs a style removed must refuse it
  in WM_STYLECHANGING, not only at install; synthetic cursor moves don't open the flyout on an inactive window — a
  hover check is by hand.
- 2026-11-08: round 9 step 3 landed (ff6125a6; captures 7b88102e): a build that removes the hovered element hit-tests
  hover again at the resting pointer (the user's choice, question 14), so the removed view is released — Meridian's
  retained heap −16…−28 % with an imported spec — and the new element under the pointer shows its hover look; two
  old bugs fixed on the way (ancestors entered twice after a removal; a delayed tooltip opening for a removed
  element). Accepted: AOT `TableView 10k sort` +5–6 %, the known code-placement row (JIT flat). Guideline notes: an
  enter or leave handler must not rebuild the element under the pointer into a new one (it would rebuild every frame
  while the pointer rests) — keep the element and change what it shows; hover now follows rows a virtual list
  recycles under a still pointer, a plain scroll view keeps hover on what scrolled away.
- 2026-11-08: the user asked where a step's time goes (agent transcripts, `timingreakdown3.py` in session c484f5ac's
  scratchpad): a perf step 55–70 min — the model's own turns 36 %, tools 29 %, waiting on its own background jobs 26 %;
  per run: full suite `--compare` ~6–7.5 min, filtered capture 0.3–0.9, test project 0.3 (max 3), incremental build
  0.3, AOT publish ~1–1.7, probe/app walk median 0.6 (max 11); survey agents 25–50 min, a cold build 1.2 min in a
  fresh worktree. The user approved a shared step script this round (`scripts/perf/step-ab.ps1`); fewer full-suite
  runs per step (the user's call) and a parallel round with a measurement lock (for round 10) were offered, not taken.
- 2026-11-08: `scripts/perf/step-ab.ps1` added (the user's choice): one call builds the base commit (git archive) and
  the working tree, publishes NativeAOT through a generated wrapper (PublishAot on itself only, NETSDK1207; bin/obj
  under a short %TEMP% path for link.exe), runs before/after alternated behind the idle gate, and prints verdicts,
  moved allocations, an A/A line and the step's own rows; `-Slug` copies the last pair into captures/. Tested on step
  3's rows (27113cb0 → 7b88102e, JIT + AOT): built in ~40 s a side, AOT TableView 10k sort +7 % again (JIT −1 %).
  Traps fixed on the way: under `pwsh -File` arrays arrive as one string (-Runtime splits on ',', -Show on '|');
  Git Bash's tar reads 'C:' as a host (use System32\tar.exe); format numbers invariant (this machine's culture
  prints decimal commas). The brief's Phase `build` steps 1 and 4 now say to use it; briefs from step 4 on do.
- 2026-11-08: round 9 step 4 landed (6801d779, c4a459ef; captures d329e714): a selection drag walks its group's labels
  with no list — a drag move 1.16 → 0.04 KB, a held edge frame −30 %, new suite rows 8.3 → 0.1 KB; the note on
  e7d10c63 closed. The first step on `step-ab.ps1`: no trouble, ~47 s a full build pair, the gate once waited ~4 min.
  Guideline notes: headless `Application.RunFrame` doesn't advance the clock — clock-driven work (edge scroll, tweens)
  needs `session.PumpFrame(delta)`; a held-drag allocation test holds ~1 px past the edge so most rounds select no new
  line; an interpolated assert message inside an `Allocations` round allocates.
- 2026-11-09: round 9 step 5 landed (5c9d6b8b; captures a5efa8d0): the UIA and AT-SPI bridges borrow one diff from the
  window for patches and in-place refreshes — reader-on typing −15 %, Backspace −33…−36 % AOT, suite a11y rows
  −23…−43 %; event streams and the audit unchanged. The agent found step 1's
  `GridRowKeepTests.ACardUnderThePointer_IsNotMovedByAChangeOfColumns` failing since step 3: it asserted no card is
  hovered after a change of columns, which question 14 changed on purpose; the orchestrator updated its last assertion
  (the old card let go, only a card under the still pointer hovered), Aperture 356 pass. Guideline notes: a wrapper
  around a real bridge (a test wrapper, a perf rig, a probe) hides the bridge's internal markers, so mark the wrapper
  too or the measurement takes the generic path; `scripts/wsl-test.sh` passes `-c Release` itself (giving it again
  fails `dotnet test`). Process note: a step that changes behaviour other steps' tests pin should run those apps'
  test projects — step 3 ran Meridian, Vesper and Junction but not Aperture.
- 2026-11-09: round 9 step 6 landed (2d07f529, 28ec28fd; captures c152a279): monospace ASCII rows break by count —
  opening a 1 MB minified file in Junction 63.6 MB → 0.36 MB, Meridian's 1 MB HTML −96 %, the new suite row −96 %
  time; rows identical over 37.4 M rows. Guideline notes: `GetLineStarts` reuses its rows for any width within 0.5 px,
  so a width sweep must invalidate rows before each width (the survey's probe and an old test compared cached rows);
  an allocation test of "no shaping" runs the counted side first, while caches are cold.
- 2026-11-09: round 9 step 7 landed (7e964aeb, f5609009; captures 45b78a04): a held resize no longer redraws the same
  picture for a post that can't run (held edge 65 → 0 frames a second, 2 → 1 a step, Meridian's preview 64 → 33 frames
  over 32 steps), and the Vulkan interop check allocates nothing (24 → 0 B a frame with a web view or video). The agent
  went beyond the brief for a case the survey missed: a source with one post in flight still draws every tick. Guideline
  note: skipping a redraw for a waiting post is safe only when no source that signals by posting could be waiting on it.
  Rule clarified: "never the F16 kept-target drag" is round 5's kept-target experiment (Waiting); the shipped F16
  offscreen drag UiTest `ADrag_KeepsTheHalfFloatOffscreen_WithItsSwapchain` may run (it did, 0 `nvlddmkm`).
- 2026-11-09: round 9 step 8 landed (0ef0b61f, fa2477cf; captures 6543b6fc): Meridian's Environments typing 312–1,167
  → 2–5 KB a character with 0 keyring reads, header −97 %, body −94 %, identical in 90 checked steps; Windows keyring
  reads remembered (question 15), a Request-tab drag 59 → 0 reads a frame. Release note: `KeyringMemo`'s key change
  touched the Linux and macOS stores, not run here — the release's CI meridian-keyring jobs (Ubuntu, macOS) must pass.
  Guideline notes: when a typing handler skips its build, check every label that reads the edited document, not only
  the counts; a memo over a store that ignores letter case must ignore it too.
- 2026-11-09: round 9 step 9 landed (c940c643; captures 164668f3): Meridian's request tree hands back unchanged rows — a
  full build at 200 requests 1,587.5 → 132.8 KB and now flat with the collection's size, a send 3.27 → 0.36 MB, a
  toast −91 %; 29 scenes identical to a forget-all build; the objection to 41f48bd3 closed; a stale folder-menu record
  fixed. Guideline notes: a kept row's handler captures the key, not the record, and looks the data up when it runs; a
  full-build oracle must forget kept elements first, or it compares kept elements with themselves. (Meridian's tree has
  no folder expand/collapse or drag-move: the brief's list was wrong there.)
- 2026-11-09: round 9 step 10 landed (25a96234, 3fdf33c3; captures 073e1100): Vesper Brief scrolls before its build — no
  blank columns after any key (was up to 48 on a Home/End walk), a Down walk 40 → 12 builds and 25.4 → 4.9 KB a key, every
  step equal to a full build. On the way the agent found and fixed a Core bug: `KeptElements`' prune dropped elements the
  build in progress had made once it made more than 64. Found, not fixed → Waiting vesper-brief-filter-clear-reveal.
  Guideline note: before trusting a build count, count why each no-build key built (18 of the prototype's builds were
  the prune bug).
- 2026-11-09: round 9 step 11 landed (cb1df8c0..d1274992; captures 56acc2eb): Discovery's map click −85…−99 % (2,000
  endpoints 9.1 MB → 124 KB a click, elements 8,129 → 63), a large spec −9.1 MB retained and −14 MB to import, a send to a
  tracked URL 1.1 MB → 17 KB on the UI thread; the selected map row is now told to a reader; the objection to 12d0392e
  closed. Guideline notes: `KeptElements` holds live elements — forget them on any build that doesn't show them, or a
  hidden view stays alive; large JSON: read it with `Utf8JsonReader` and `ParseValue` per piece, not one `JsonDocument`
  (its token table doubles and stays pooled); probe bins under %TEMP% can lose their NuGet DLLs to a temp cleaner —
  `touch` the bin after each build.
- 2026-11-09: round 9 step 12 landed (58aeb965, 74cf844c; captures d2961b06): a hidden web page gives back its capture's
  GPU memory (GL −17 %, Vulkan −28 % while hidden) and Meridian lets a preview hidden for a minute go (GPU 72 → 42 MB
  on GL, the 6 browser processes exit; the next "web" toggle ~0.25 s). Accepted: Vulkan +7.4 MB shown after the first
  hide (one-off, flat; Vulkan is opt-in). The 4K monitor was unplugged during the step (measured at 100 %). Guideline
  notes: measure a GPU release by showing again too (a deferred D3D11 destroy and a reopened WGL device added memory
  on show); sample page frames on show every frame (a sparse sampler missed black frames); mount counts are not "on
  screen" — rebuild twins mount and never unmount; use `IsHosted`.
- 2026-11-09: round 9 step 13 measured (no commit; scratch `r9b13\summary.txt`): Vesper's faded layers cost far more
  than 5 % in every view on GL, Vulkan 8-bit and F16 — Sync Equal rows 18–38 % of the CPU frame and 56–82 % of the GPU,
  cut icons in Detail/Brief 16–43 % / 64–91 %, cut tiles 15–27 % / 68–84 % — so questions 20–22 go to the user (image
  fade ≤ 2/255; Sync rows' glyph edges 1/255; cut tiles ~5/255 with a badge up to 24/255). Real windows at 100 % (only
  the 1440p attached), 5 guarded launches, 0 guard firings, 0 new `nvlddmkm`. Guideline note: a faded image in a list
  is a layer per image; for list rows and tiles fade the colours, and let an image draw at its alpha.
- 2026-11-09: the user asked for images to compare questions 20–22's options: the orchestrator built comparison PNGs
  from step 13's in-process frames (whole frame today vs option, a 3x zoom of the most-changed area, the difference x40;
  controls 0 px) with `r9b13\compare.ps1` (System.Drawing; nothing installed), in `artifacts/r9-fade-compare/` (ignored).
- 2026-11-09: round 9 step 14 landed (d00f7f65; captures 39e378ad): the tokenizer's bytes are mostly TextMateSharp's; its
  own share, a theme match per token, is now kept per scope list — XML −12.6 %, JSON −8.5 %, HTML −3.5 % a call, spans
  identical over 1.75 M lines. Question 23 for the user: VS Code's parent-scope matching would save 18–28 % more but
  changes colours on 2,388 corpus lines. Guideline notes: under NativeAOT an `EventListener` gets no `GCAllocationTick`
  — split AOT bytes by difference against the library alone; warm up by time even for allocation counts (tier-1 stack
  allocation moved JIT XML by 344 KB); a library call that builds a fresh list each call is worth caching on its key.
- 2026-11-09: round 9 step 15 landed in part (b4dc84b4..76dce98a): Vesper's pane is told to a reader and Tab moves focus
  to the active pane; Meridian's prompt Escape, the Request tab's reveal and History keep or hand back focus; 0 trees a
  key, bytes flat, audit clean (Vesper fallbacks 55 → 9). Brief and Thumbnails need a framework change (question 25);
  the folder in the pane's name costs a tree a navigation with a reader (question 24). Guideline notes: a focusable
  element needs a role or a name; an app key handler that takes Tab must move the focus too; a card the app draws in its
  own tree (not `Dialog.Show`) hands focus back as it closes; a list walked by app-level arrows should hold the focus.
- 2026-11-09: an early full Windows suite on 73fad609 while the user decides questions 20–25: `dotnet test HultUI.slnx -c
  Release` 30 projects, 0 failures (Core 5,067, Headless 1,491, Junction 1,082 in 8 m 19 s, Vesper 900, Meridian 653,
  Aperture 356, Skia 387, Text 293, WebView 76, Media 86, Platform.SDL3 54). Still to run before the release: Fedora
  (`scripts/wsl-test.sh`), docfx `--warningsAsErrors`, and the suite again if steps 13b/15b land.
- 2026-11-09: Fedora `wsl-test.sh` on the branch: 22 projects, 0 failures (Core 5,066 + 1 skipped, Headless 1,335 + 82,
  Vesper 882 + 20, Skia 387, Text 293, Linux bridge 104); docfx `--warningsAsErrors` 0 warnings, 0 errors (run from the
  repository root, where `docfx.json` is). The round can release as it is; it waits only for questions 20–25.
- 2026-11-09: the user agreed with the recommendations on questions 20–25: 20 and 21 yes (step 13b), 22 build the
  backing-free variant and show new images first, 23 show before/after images first (step 14b), 24 keep the folder in
  Vesper's pane name, 25 yes (step 15b).
- 2026-11-09: round 9 step 13b landed (6c518629, f9a7cc74, 8a6d7bb7; captures 0e6bc590): faded images draw at their
  alpha (Vesper Detail −28 %, Brief −43 % a GL frame, GPU −72…−91 %) and Sync's Equal rows fade their colours (−38 % GL,
  GPU −77 %), pixels as approved. The cut-tile backing-free variant stays uncommitted: pictures within ~1/255 but the
  letterbox rows, the badge up to 24/255, kind icons and the cursor tile up to 7 (19 on F16) — question 22 asked again
  with its images. Guideline note: a faded group costs ~0.01 ms CPU and 0.013–0.02 ms GPU a frame each — fade a row's
  colours, keep `Opacity` for lone boxes and images (which now draw at their alpha).
- 2026-11-09: round 9 step 15b landed (ea42f45b, 9f83f755; captures 8cea790c): a `ListItem`'s selected state is patched
  in place, so Vesper's Brief and Thumbnails tell a reader the cursor item on every key with 0 trees (before: silent).
  Guideline notes: a view that walks its own items with no build gives each item the `ListItem` role and a name and sets
  `AccessibleSelected` with the cursor's restyle; keep what changes often (a mark, a count) out of the name of an
  element with children — a new name there builds the tree.
- 2026-11-09: round 9 step 14b (no commit): images of question 23's colour changes, sent to the user — only Dark+/Light+
  change, 2,334 of 2,388 lines toward VS Code's colour (54 PowerShell lines away), no JSON/XML/HTML line. Found: both
  paths differ from VS Code on ~974k lines (an included theme looked up second) — question 26, for later.
- 2026-11-09: the user answered question 22 no ("changes too much": cut tiles keep their layer) and question 23 yes
  ("an improvement in my eyes": colours from encoded tokens with VS Code's parent-scope matching, step 14c).
- 2026-11-09: round 9 step 14c landed (ae32c9d8, bf74fbca; captures d9f55f21): syntax colours come from TextMateSharp's
  encoded tokens with VS Code's parent-scope matching (question 23) — JSON −29 %, XML −19 %, HTML −4 % of the
  tokenizer's bytes, JSON −30 % time — and a theme rule naming a grammar's root scope now applies, so PowerShell members
  keep VS Code's colour too; exactly the expected lines changed. Guideline notes: TextMateSharp's encoded tokens are the
  cheap path; its parent-scope theme rules never apply in 2.0.4 (worked around per grammar); bold/italic from a theme
  would change text measurement, so the highlighter leaves them out. Every build step is done; Phase → full suite.
- 2026-11-09: round 9 full suite on 2370eea5: Windows `dotnet test HultUI.slnx -c Release` 30 projects 0 failures (Core
  5,067, Headless 1,501, Junction 1,082 in 8 m 24 s, Vesper 903, Meridian 653, Aperture 356, Syntax 94), Fedora
  `wsl-test.sh` 22 projects 0 failures, docfx `--warningsAsErrors` 0 warnings. origin/main 10acc8c0 is in the branch.
  Phase → release.
- 2026-11-09: round 9 merged into main as facced69 (detached origin/main merge in this worktree; origin/main 10acc8c0 was
  in the branch and the merged tree is identical to the tested one, so the push gate held). Primary checkout
  fast-forwarded behind the guard; branch fast-forwarded to the merge and pushed. CI queued.
- 2026-11-09: CI run 42675929595 on facced69 green: test ~17 min, test-linux ~5 min, docs, meridian-keyring (ubuntu-24.04,
  macos-15; step 8's keyring memo change checked there). Round 9 landed 16 steps (1–12, 13b, 14, 14c, 15, 15b, 16; 14b
  images, 13 measured first) in ~40 commits, fixed bugs on the way (Snap Layouts after a restore, a KeptElements prune,
  hover after a rebuild, a Meridian folder-menu record, a spec that crashed the parser, reader focus in Vesper and
  Meridian), added `scripts/perf/step-ab.ps1`, and changed pixels/colours only as the user approved (questions 20, 21,
  23). Round → 10, Phase → survey; the loop stops, and round 10 starts in a fresh context.
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