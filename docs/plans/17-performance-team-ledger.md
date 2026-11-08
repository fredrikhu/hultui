# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 8
- **Phase:** build — round 8, step 4 next (9 steps; steps 1–3 landed). Step 2 resumed 2026-11-08 after the weekly
  reset (next reset 2026-11-15 03:00 UTC) on top of main's merge 3a15b5b1 (32 commits, mostly Meridian). Briefs and
  probes stay in session 64e7e0e2's old scratchpad (see Probe code). The survey's eight reports are
  `17-reports/r8-*.md` (common brief `brief-r8-survey.md`). The 90 % weekly pause applies to build steps (user,
  2026-10-27). Round 7
  released: main's app-support merge (f64e4674, another session) merged into the branch as c655b411 with fixes
  7dfd2ce3, then the branch merged into main as df45b4a2; CI run 41691075588 green (test, test-linux, docs). Open for
  the user, none blocking: question 10 (`TokenizeDiff` public), the live screen-reader check of menus (script in
  ACCESSIBILITY-PLAN.md, round 7 step 13), f9bceb6d's window checks (Open). Weekly usage resets 2026-11-08 03:00 UTC
  (extra usage off); pause at 90 % (user, 2026-10-27). Older rounds are in
  [the archive](17-performance-team-ledger-archive.md) (search it, don't read it whole). History note: rewritten
  2026-10-25 (every commit after 3353ce47 has a new ID; a pre-push hook refuses pushes built on the old history).
  To resume in a fresh context, run in this worktree: `/loop Run the next step of the performance team: follow
  docs/plans/17-performance-team.md, with state in docs/plans/17-performance-team-ledger.md. Read both once per
  session (afterwards only the ledger's Phase and the part the step needs), do the step the ledger's Phase calls
  for, record it in the ledger, and commit.` Before resuming, `git fetch` and merge main if it moved. The common
  survey brief from round 7 is reusable: session 855f4ddd's scratchpad `brief-r7-survey.md`.
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`)
- **Review commits since:** round 7's survey reviewed cd3b6c8f..1939afcb (round 6's build); round 8's survey reviews
  round 7's build, 17d4220a..df45b4a2 (the last survey report to the round 7 release), which also brings main's
  app-support merge f64e4674 (25 commits from another session: headless locators, frame robustness, lock privacy,
  UIA, overlays, rich text) — review it like the rest.
- **Probe code:** round 8 surveys in session 64e7e0e2's scratchpad
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
| 1. Layout | 8 | **yes** | none (190 shared suite rows: no layout counter rose; 39e60064's stretched-margin branch fires only in Junction's palette, −27 % MeasureSize there) |
| 2. SkiaSharp: painting | 8 | no | **d04464f6** (main's app-support, merged in c655b411): `BeginFrame`'s `RestoreToCount(1)` drops the Vulkan surface's drag clip (6c627f27, 1b11aeda); no shown pixel changes, but the clear and overhanging draws cover up to 255 px more each way → step 1 |
| 3. GL and Vulkan | 8 | **yes** | none (steady frame 0 B and idle 0 wakes on GL, Vulkan 8-bit and F16, JIT and AOT; the walk fired once in 5 of 6 GL launches; 13 guarded windows, 0 foreground takes, no new `nvlddmkm` events) |
| 4. .NET runtime | 8 | no | none (0 of 213 suite rows moved more than 0.2 KB and 2 % against step 11's captures, JIT and AOT; round 7 step 1 holds; `KeptElements` bounded) |
| 5. Text pipeline | 8 | no | none (no text row rose; dump hash 7A18AF8DE8E05271 unchanged) |
| 6. Reactivity and reconcile | 8 | no | none (`KeptElements` correct; moved rows O(moving rows)) |
| 7. Accessibility bridges | 8 | no | none (audit: 0 STALE in 3,171; 5,593 in-place walks, 0 mismatches; fallbacks 128) |
| 8. App workloads | 8 | no | none — round 7 helped the apps: 33 round 6 walks 2,373 → 2,029 KB/key (−14 %, geometric mean 0.72), 47 walks 4,894 → 4,229 (−14 %, 0.60); none worse by more than 2 % |

## Queued candidates

Rounds 1–7's build orders all landed but round 6's step 8, declined (rounds 5–7 are in [the archive](17-performance-team-ledger-archive.md#round-7-build-order),
with the round 7 survey's closed-from-Waiting note).
A build step may bundle one expert's related small candidates as separate commits. Items marked **(user)** wait for a
decision in Needs the user; the rest of their step goes ahead.

### Round 8 build order

Ranked by expected win over cost: the round 7 objection first, framework before apps. Each step names the report lines
its brief quotes (`r8-k` = `17-reports/r8-k-<area>.md`).

1. **paint-beginframe-keeps-surface-clip** (2 with 3; r8-2 Review and Candidate 1) — the objection to d04464f6:
   `SkiaRenderContext.BeginFrame` (SkiaRenderContext.cs:298-317, `RestoreToCount(1)` at :312) runs after the Vulkan
   surface's `BeginFrame` (Window.cs:1856, then :1862) and drops its over-allocation clip (SkiaVulkanRenderSurface.cs:61-72,
   680, 870-883); the cached `_clip` never puts it back. Probe: surface clip 1200×800 at save count 2 → count 1, device
   clip 2400×1600, `ClipBounds` 1601×1068 layout px instead of 800×533. Restore to the save count the surface handed the
   canvas over at (internal `SetCanvas(canvas, baseSaveCount)`; Vulkan passes 2 while it clips); a throwing frame's pushes
   are still undone. Tests: a clipped canvas keeps `DeviceClipBounds` through `BeginFrame`; FrameRobustnessTests pass; the
   VulkanProbe too-narrow-clip check (6c627f27) shows a difference again. Correctness; no time bar expected. Small.
2. **a11y-walk-name-without-string** (7 with 4; r8-7 Candidate 1, r8-4 Candidate 1; folds a11y-walk-unchanged-bytes) —
   the in-place walk's `Compare` (AccessibilityTreeBuilder.Refresh.cs:658, `node.Name != ResolveName(element)`) builds
   every name-from-content string to compare it (`GetContentText`, AccessibilityTreeBuilder.cs:803, 821-844: a
   `StringBuilder` and a string per control named by two or more texts); compare segment by segment instead (prototypes
   `r8-a11y\proto-nameb.patch`, ~40 lines; `r8-dotnet\proto-namecmp.patch`, 49). Junction Changes at 300 files: a walk of
   the unchanged 264-node tree 7.67 → 0.02 KB; caret Down 120 ms apart with tree/UIA/AT-SPI 91.4/94.1/96.3 →
   81.6/84.3/86.5 KB (−10.2…−10.7 %); wheel a notch every 120 ms 22.8/25.4/27.3 → 16.2/18.8/20.7 (−24…−29 %); bridge
   off flat; trees 0, the same diffs and events. Add a suite twin row (composite rows named by 2–3 texts, with a
   reader), check bytes on AOT, rerun the staleness audit. Small, no API or reader change. **(question 11: yes)** adds,
   as its own commit, **a11y-disabled-menu-item** (reader bug, not perf; round 7 step 13): a disabled context-menu or
   menu-bar item is published as enabled because `MakeItemNode` always sets `isEnabled: true`
   (AccessibilityTreeBuilder.cs:558). Fix (r8-7 Waiting): an additive default member `IAccessibleItemSource.ItemIsEnabled(int)`
   (default enabled), `ContextMenuElement` answering `!IsDisabled`, compared in `MatchItems` and the served-item
   providers; small–medium.
3. **Rich text editor blocks** (5; r8-5 Candidates 1–3, Waiting richtext-short-viewport-jitter) — (a) a paragraph keeps
   the layout's exact `TextLine[]` (PipelineTextParagraph.cs:35-47), makes its `TextLineBox[]` on the first `Lines` read,
   `SpansOf` returns an exact array (RichTextEditorElement.Layout.cs:105), and `RichTable.Normalize` makes no closure per
   block (RichTable.cs:97-98): richtext/open 5,000 blocks 3,256.7 → 2,589.2 KB (−20.5 %), resize 656.2 → 543.8
   (−17.1 %), retained with the document open 7.94 → 7.41 MB (−6.7 %); `ParagraphAllocationTests` is rewritten (28 → 0 B
   a line). (b) `BlockLayout` (RichTextEditorElement.Layout.cs:140-176) keeps its table and picture fields in a side object
   made only for cells and pictures and takes a text block's style from its paragraph: on top of (a) open −13.7 %,
   resize −12.9 %, retained −4.7 %. (c) block layouts in a `List` shifted in place (Layout.cs:283-287): richtext/enter in
   5,000 blocks 82.0 → 42.9 KB (−47.7 %). (d) the one-line short-viewport jitter fix (RichTextEditorElement.Render.cs:362-367:
   prefer the top when `caret.Height >= viewport`). Prototypes `r8-text\proto-para2.patch`, `proto-para3.patch`,
   `proto-blocklist.patch`. Internal, no pixel change; each its own commit.
4. **apps-coalesce-landing-builds** (6; r8-6 Candidate 1) — a picture landing posts a build only when none is pending (an
   `Interlocked` flag, as Vesper's `FindRepaint`) at Aperture's `PosterCache` caller (ApertureApp.cs:107) and Vesper's
   `ThumbnailCache` caller (CommanderApp.Views.cs:52); both caches' comments say the caller coalesces (PosterCache.cs:246,
   ThumbnailCache.cs:109). Aperture Home, 36 posters: builds 41 → 16–22, 3,590 → 1,896–2,290 KB (−36…−47 %), at most 8–9
   builds a frame → 2–5; grid, 42 posters: 2,424 → 1,515 (−37 %); Vesper Thumbnails, 160 PNGs: builds 45 → 2–5,
   −40…−46 %, at most 27–28 builds a frame → 1. The same pictures drawn. REACTIVITY.md: a background callback that fires
   once per item posts one build at a time. Small.
5. **Aperture cursor** (6/8; r8-6 Candidates 2 and 5, r8-8 Candidate 1) — (a) the library grid's cursor key moves the
   ring on the live cards through `KeptElements`/`Live` (`MoveRingWithoutBuild`, ApertureApp.Cursor.cs:168): 600 films
   Right 49.4 → 0.3 KB (−99 %), Down 75.7 → 23.2 (−69 %; the rest is rows scrolling in), with UIA 0 trees; focus now
   follows the cursor, as Cursor.cs:17 says it should (today the focused element is empty, the aperture-leads bug). (b)
   Discover the same (`MoveRingWithoutBuild` returns false off Home, Cursor.cs:170; `BuildDiscoverView`, Discover.cs:25,
   the `string.Join` key :77): 334.2/337.6 → ~0.4 KB a key. (c) bug: the Home cursor walks onto another page of its shelf
   and no card shows the ring (1000 px, 4 cards a page: cursors 5–7); turn `_rowPages` to the cursor's page. An Aperture
   cursor test per view.
6. **aperture-home-build-diet** (6; r8-6 Candidate 4) — a Home build with every card kept is 69.6 KB: (a) hand back a
   whole shelf (`KeptElements` keyed by the row) when its heading inputs and its page's cards are unchanged: 69.6 → 30.6
   KB, elements 46 → 4, Home open with landings coalesced 1,897 → 1,583 KB (−17 %); the heading closures capture `row`
   and `all`, so the state must cover what the menu shows. (b) memoize each card's picture `Uri` by item, kind, pixels and
   scale (`JellyfinLibrary.ImageUrl`, JellyfinLibrary.cs:272: 1,136 B a card → 2 B): −20 KB a build more. Medium.
7. **vesper-cursor-no-build** (6; r8-6 Candidate 3) — a plain cursor key builds nothing (`MoveCursor`, CommanderApp.cs:1170):
   Detail sets the mounted list's `SelectedIndex` through its ref and scrolls (17.3 → 2.2 KB, −87 %; UIA 17.7 → 2.6);
   Brief and Thumbnails restyle the two live cells or tiles with `KeptElements.Live` and set `CursorRowRef` (Brief 31.8 →
   0.5, Thumbnails 27.7–31.3 → 0.5, a scrolling 30-key Down walk 57.9 → 1.9; −97…−98 %); build as today on a Shift sweep,
   zebra, a drag hover, an overlay or full shell, a cleared filter or a partner row. Compare names in the partner check
   (`DiffTargetPath` + `IndexOfPath` cost 0.9 KB); trap: `ThumbRow` reads `item.Cursor` (CommanderApp.Views.cs:279) and
   must read the live cursor. Medium.
8. **apps-meridian-history-archive-part** (6/8; r8-8 Candidate 2; round 7 step 10's Left open) — a History key moves the
   list's selection through its ref and builds only the archived-send panel as its own `Part` (MeridianApp.History.cs:188-191;
   `BuildHistoryRealView` is 40.7 KB a call: archive 14.9, list 3.2, filter bar and footer ~22.6): Down 52.8 → ~24 KB
   (−55 %), History search typing ~−30 %. Medium.
9. **paint-meridian-history-faded-row-layers** (2; r8-2 Candidate 2) — measure first: `row.Opacity(0.6)` on sends older
   than today (MeridianApp.History.cs:382) makes every visible row a bounded SaveLayer (ElementRenderer.cs:71-85), 29 a
   frame on Down, wheel and hover (~59 render passes). Time a real GL and Vulkan History wheel frame (guarded window)
   with and without the layers. If they are ≥ 5 % of the frame, ask the user about the pixels (glyph edges ±1–2/255, the
   chip text over its tint); otherwise record it as examined. **(user, only if it clears 5 %)**

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
- **junction-leads (remaining)** (5/tests) — old-side state convergence between hunks (file walk 46 → 33 % of lines;
  not measured); `SelectConflict` and a conflict row's right-click have no test of their own (not perf).
- **aperture-leads (remaining)** (6/8) — toasts not measured; grid Down after step 5 is the rows scrolling in.
- **findbar-countonscreen-foreach** (4, nit) — `CountOnScreen` loops `foreach` over `bar.Children` (32 B on AOT, 0.4 %
  of an 8 KB Enter); fix when FindBar is next touched.
- **terminal-noop-scrollback-wheel** + **terminal-row-strings-by-content** (2/Terminal, low) — a no-op notch draws a
  whole terminal frame (5,440–5,682 glyphs); 10,276 B a streamed line. Only the terminal sample uses HultUI.Terminal.
- **layout-ordered-children** (1, low) — suite rows only (wheel over 5,000 rows 4,954 hit visits); apps at most 33.5 hit
  visits a move and 13 culls a frame.
- **Suite scenarios** (8): document-switch (Junction Files 85.0 KB a key: 62 % view build, 1.23 builds a key — the
  file's text lands and builds again — 149 measure misses a key).
- **textarea-chunked-document** (5/4; the user chose option B on 2026-10-25) — parked: Meridian's 89k body typing is
  ~238–241 KB/key, most of it the TextArea's new string; the user's 256k-character condition is unmet.
- **a11y-served-items-followups** — a live Orca check only (no desktop Orca here; optional).
- **F16 kept drag targets** (3) — deferred since round 5's `nvlddmkm` errors; never repeat blind.
- **gl-threaded-optimization-profile** (3; the user's driver setting, information only) — the walk lands while the
  window rests; the setting would remove only the per-frame spin (round 8: mean 340–387, max ≤ 986 `SwitchToThread` a
  paced frame in threaded launches; one AOT launch stayed single-threaded).
- **layout-audit-padding-border** (1, tooling) — compare `LayoutResult` padding and border too; with the next audit change.
- **text-open-measures-every-line (full)** (5; user: not now, 2026-10-13).
- **controls-menu-max-height-scroll** (1/2; not perf; a long menu also draws to the allocated edge during a Vulkan drag
  until step 1 lands), **layout-virtuallist-spacer-stale** (1; invisible; with an audit).
- **aperture-poster-as-hero**, **aperture-tmdb-size-fallback**, **junction-narrow-gitlab-glyphs**,
  **meridian-pane-choice-not-persisted**, **meridian-min-width-wraps** (apps; not perf).

Closed from Waiting by the round 8 survey: a11y-walk-unchanged-bytes (→ step 2: the joined names), richtext-short-viewport-jitter
(→ step 3), junction-tags-test-flake (29 tests × 6 under load, 0 failures; round 7's two full suites and CI green since
63467f8e), vesper-coalesce-markdirty (dropped by experts 6 and 8: no double), runtime-element-layout-state phase 2 and
layout-patch-keeps-layout-dropdown-expander (Examined), the held-selection suite scenario (pinned by
`CommitPrefetchTests.AHeldArrow_LoadsOnlyTheCommitItStopsOn`), junction-leads' `ChangesView` lists (95c86c97) and big
files' gen 2 (Examined), aperture-leads' Discover and the grid cursor's focus (→ step 5), junction-changes-leads' perf
parts (Examined). After the survey: a11y-disabled-menu-item (→ step 2, question 11 yes).

## Landed

Rounds 7 and 8 (round 8 is the round the next survey reviews; round 7 moves to the archive at round 9's survey). Rounds 1–6 are in
[the archive](17-performance-team-ledger-archive.md#landed-rounds-14) ([round 5](17-performance-team-ledger-archive.md#landed-round-5),
[round 6](17-performance-team-ledger-archive.md#landed-round-6)).

| Round | Commit | Candidate | Before → after |
|---|---|---|---|
| 7 | 1986bed9 | a11y-bridge-map-one-lock | The objection to 4325bac6. `_objects` (AT-SPI) and `_wrappers` (UIA) made with `concurrencyLevel: 1, capacity: 31` (one writer, the UI thread; reads lock-free); `_nativeByElement` (two writers) and `AtSpiServedItems._made` (bus thread) unchanged. Kept-tab switch with AT-SPI, best min of 3 alternated pairs: AOT 0.630 → 0.605 ms (−4.0 %; a11y phase 0.280 → 0.246), JIT 0.508 → 0.475 (−6.4 %): round 6's +2.5–2.8 % won back; AT-SPI add a row and filter −3…−5 %; UIA rows within ±3.5 %. Tests read the lock array: 1 (1,792 under the reverse edit). |
| 7 | 2aa55491 | shortcut-matches-no-enumerator | Index loops in `Shortcut.Matches` and `RichTextToolbar.StateOf`; `RichDocument.TableAt` answers non-table blocks before boxing its `ImmutableArray` into `RichTable.At` (264 B per `IsStale` with the default bar). AOT KB/key: Vesper Detail 26.4 → 21.2 (−20 %), Brief 34.0 → 28.7 (−16 %), Thumbnails −13 %, partner −13 %, Insert −11 %; Junction review typing 4.1 → 3.7 (−10 %), History −0.6 KB; CPU per key within noise. Allocation tests fail under reverse edits (240 / 32 / 264 B a call) on the JIT test build with TieredPGO off. |
| 7 | 5000753a | a11y-overlay-owner-slot-leak | Leak closed: `RemoveOverlayAt` calls `AccessibleOwnerSlots.ReleaseTree(content)` under one lock. Not an unmount: `Show` never mounts, so unmounting would stop the component hosts of content shown again. 2,000 menu cycles with AT-SPI: slots 2,000 → 0, heap +46…+198 B a cycle → 0 (flat at 6,719 KB); recording bridge 2,000 → 0; popover, dialog and no bridge 0. Step 1: event streams before/after with ids masked equal (15,978 / 1,334 / 5,146 lines); suite `--compare` 0 slower, 0 faster JIT + AOT ×2 (AOT TableView 10k sort +4.7–6.5 %: placement, JIT flat, no step code in the row); captures 56ca60f1; Core 5,033, Headless 1,269, Linux bridge 104 (Windows and Fedora), Fedora Headless a11y 260, Vesper 876 pass. |
| 7 | 45628641 | text-find-lazy-line-hits | A find lists a line's hits when a row first asks: a binary search into the sorted hits, kept until the next rerun (`HitsOn` keeps its type). KB/key JIT / AOT: text/find typing 54.0 → 24.9 / 53.4 → 24.2 (−54 %); Meridian response find 132.1 → 60.4 / 131.2 → 59.6 (−54 %); Junction diff find typing 853.9 → 416.7 / 849.7 → 412.5 (−51 %, min −4.7 % JIT); Vesper reader find typing 646.8 → 297.7 / 641.2 → 292.1 (−54 %). Find Enter flat at 25.5; wheels with find open and Junction find Enter within 0.1 KB and 2 % time. |
| 7 | d748b316 | textarea-touched-rows-sized | A replaced text's touched rows sized once, to a lower bound that can't overshoot (one row per hard line touched; an edit inside a big text allocates under 1 KB). text/open highlighted JSON 268.4 → 86.3 KB (−68 %, JIT and AOT), min −3 %; Meridian resending a 19,000-line JSON 1,792.7 → 1,139.5 KB a send JIT, 1,823.0 → 1,169.8 AOT (−36 %). |
| 7 | 73819ebd | text-exact-line-arrays | Each layout keeps an exact `TextLine[]` filled from a kept scratch list taken while filling (nested calls get their own); a truncated layout replaces its last line, no second layout (styled, BiDi and `EllipsizeMeasuredLine`). KB/op JIT (AOT within 1 KB): multi-font ellipsis 413.7 → 257.7 (−38 %), styled ellipsis 636.6 → 481.0 (−24 %), bidi and styled bidi ellipsis −9 %, wrap −9 %, styled wrap −7 %, richtext/open 3,483 → 3,263 (−6 %), resize −6 %; no row up; richtext/open JIT min +0.8…+5.4 % over three sets, medians −1.0…+2.2 %, AOT +0.9 % (noise). Retained 5,000-block document 8.12 → 7.93 MB open, 3.43 → 3.38 closed. Step 2: 48,000-layout hash 7A18AF8DE8E05271 and both 240,000-layout corpora F09E9038726E4BD6 on both builds, 0 differ; mutations change 83–41,034 layouts on every path. Suite 0/0 JIT + AOT (captures 7ac787f7). Text 293 (Windows and Fedora), Skia 337, Core 5,037, Headless 1,269, Meridian 465, Vesper 876, Junction 1,075 pass. |
| 7 | 18fe58a0 | paint-text-baseline-whole-device-pixel | A blob whose glyphs all sit on the baseline is drawn with its baseline on the whole device pixel its glyphs land on anyway, when the matrix has no skew or perspective and the text is under 128 device px; the matrix read once per push/pop that moves it; the on-baseline flag in the sign bit of the kept blob's glyph count (a separate bool made map entries +8 B: scroll/jump 3.0 → 3.7 KB). Mechanism confirmed in Skia m119 (`TextBlob::Key::Make` keys a direct-mask blob by the fractional part of its mapped origin; `findBlobIndex` scans linearly; direct mask below 162 device px). Real window 1200×800 at 1.5×, AOT, idle-gated, ABBA, table render ms after a 200-notch walk, 0/25/50/75/100 %: GL 0.156/0.251/0.256/0.257/0.271 → 0.142/0.147/0.154/0.162/0.149, Vulkan 0.151/0.240/0.255/0.270/0.287 → 0.158/0.162/0.166/0.167/0.172; frame at 75 % GL 0.437 → 0.338, Vulkan 0.522 → 0.396; JSON TextArea render no longer grows over 200 notches (GL 0.047 → 0.055 before, flat ~0.048 after). Pixels: in-process GPU readback GL and Vulkan at 1.0×/1.5×, 38 states, A/A 0 of 152, A/B 0 of 152; Vulkan PPMs byte-identical; raster identical at 1/1.25/1.5/2×; goldens pass. Suite 0/0 JIT + AOT, bytes unchanged (captures 3405d093). 50 guarded launches, 0 foreground takes, no `nvlddmkm`/TDR; the F16 PPM encode at 1.5× held a frame over 1 s twice (the diagnostic capture; the watchdog stopped those runs). Skia 357 (Windows and Fedora), Core 5,037, Headless 1,269 / Fedora 1,146 pass; `BaselineOnPixelTests` caught three reverse edits. DECISIONS.md (Rendering). |
| 7 | dc469aa0, ca4a0079 | virtuallist-item-rows-follow-their-item | Suite rows (filter at the top of a 1,000-commit item-row list; insert above while scrolled 200 rows down; twins whose items carry their place). `KeepUnchangedRows` sends a row whose index now holds another item to `_moving`; `RealizeRow` hands it to the index where an equal item stands in the same selected state, before the template runs. Moved rows keep their mount (focus stays) and take their new place's height and sticky state; still made again: a hovered or pressed row (unless it holds the focus), rows moved in a build that called `InvalidateRow`, `InvalidateRows`, a theme change. The patch sets `SelectedIndex` without dropping rows by old index; `RealizeForAccessibility` realizes a moved focused row. No API change. Filter 19 → 3 rows, 76.6 → 14.8 KB, min −6 % JIT / −9 % AOT; insert 20 → 1 rows, 80.5 → 7.1 KB, −7 % / −12 %; twins flat. Junction's Files, Commit and Tags lists get moves on their own. ELEMENTS.md "Rows follow their item", REACTIVITY.md "Keep the index out of a list's item". |
| 7 | ebff97b2, df7f3a3f | vesper-rows-follow-their-item | `PaneRowItem` carries `Odd` (zebra parity, only while zebra is on), not the index; with a filter on, `Rows()` keeps the filtered list per listing, side and filter text. Vesper type-to-find, 400 files, AOT: Detail 195.7 → 88.5 KB a key (−55 %; JIT 197.8 → 89.6, median −19 %), min/median 1.163/1.357 → 1.077/1.225 ms, rows made 26.5 → 9.3, elements added/removed 133.7 → 45.3; with UIA 334.9 → 227.7; Brief 195.0 → 180.6 (memo only). Down and Insert flat. Step 4: suite 0 slower JIT + AOT, 1 faster AOT (captures 82c40437); event streams UIA and AT-SPI identical, recording identical but the intended composite-row case (54 removed + 54 added → 48 bounds changes); audit 0 STALE in 3,123, fallbacks 128; 43 Vesper scenes pixels, a11y and focus identical. Core 5,037, Headless 1,277, Vesper 876, Meridian 465, Aperture 329, Linux bridge 104, Junction 1,075, Fedora Headless a11y + VirtualList 285 pass; 6 reverse edits caught. |
| 7 | 8bd2a2a2, b1c2894e | layout-virtuallist-row-margins | Suite rows `overlay/command palette Down 2,000` (+ UIA, AT-SPI) and twins `a11y/selection walk 1,000 composite item rows, UIA` (rows draw / the list draws). VirtualList lays a row's margins out inside its place; the palette's rows carry the list padding as margins, no wrapping box per row (folds palette-per-row-box). KB per step JIT = AOT: suite palette Down 21.7 → 19.0, filter 2,000 115.9 → 103.8 (−10 %), Junction palette Down 32.0 → 29.8, type+erase 97.2 → 88.0, Meridian Down 21.5 → 19.6, type+erase 94.9 → 81.6 (−14 %). ELEMENTS.md (VirtualList, `RowHeights`). |
| 7 | ae3c5e70, c66d1bbb | layout-palette-drawn-selection | The palette's list draws the choice and the hover tint, rows made unselected; composite rows that set `AccessibleSelected` are told by the list whether they are selected (same IsSelected changes as before); Down sets the mounted list's `SelectedIndex` through its ref and scrolls the row into view instead of building the card. No API change. With (a), KB per step JIT / AOT: suite palette Down 21.7 → 3.9 / 3.9 (−82 %), filter 2,000 −14 %, typing UIA/AT-SPI −12 %, Down UIA/AT-SPI −58 / −57 %; Junction palette Down 32.0 → 3.7 / 3.8 (−88 %; elements 32.2 → 3.9), type+erase −13 %; Meridian Down 21.5 → 0.8 (−96 %), type+erase −19 %. Min times −3…+1 %. Step 5: suite 0 slower JIT + AOT, 1 faster AOT (captures 88f72224); 105 palette scenes (framework light/dark, Junction dark/light, Meridian, Vesper dark/light) A/A identical and A/B 0 px inside the card; reader trees and recording diffs key by key identical; audit 0 STALE. Core 5,037, Headless 1,285, Junction 1,075, Meridian 465, Vesper 876, Linux bridge 104 pass; Fedora Headless a11y/palette/VirtualList 316, bridge 104. |
| 7 | f5e8baeb, b6119ae9 | findbar-step-builds-nothing (API) | Suite twin rows: a find step beside a 3,000-row list, the bar in the view / with `Moved`. **Public additive `FindBar.Moved` (`Action?`)**: when set, a step that moves only the current match (Enter, Shift+Enter, F3, n, p, arrows) runs `Moved` instead of `onChanged`; the bar writes the count into the label on screen (found by automation id, as `FindBarElement` finds its parts) and runs `Service`, so the reveal still happens; without a bar on screen or without `Moved`, the old path. Junction diff and Vesper reader/comparison `InvalidateRow` the two rows the match moved between; Junction Files and Meridian response hand the TextArea a new `Highlights` list. KB per Enter, AOT (JIT within 2 KB): Junction diff 88.0 → 31.5 (−64 %), Meridian response 60.2 → 12.6 (−79 %), Vesper reader 69.1 → 21.1 (−69 %); builds per Enter 1 → 0; median CPU −2…−5 %; typing unchanged. Suite row 170.4 → 8.1 KB (−95 %), min 0.623 → 0.604 ms JIT, 0.658 → 0.609 AOT; suite 0/0 JIT + AOT (captures 62ac3205). Declined (DECISIONS.md): the bar as its own `Part` (48.8 / 28.6 / 38.4 KB an Enter; the bar's build alone 16 KB), the count as its own `Part` (typing +0.3–0.4 KB), a `Ref` on the count (typing +0.1 KB). 189 find scenes (Junction diff and Files dark/light, Vesper reader and comparison dark/light, Meridian response) A/A and A/B identical: pixels, bridge tree, told changes, focus. Core 5,037, Headless 1,295, Meridian 465, Vesper 876, Junction 1,075, Fedora Headless 1,172 pass; 4 reverse edits. ELEMENTS.md §19. |
| 7 | 31595ee9, 86f9c609, 77ce4633, 2937e090 | a11y-structural-refresh-in-place | Suite rows: image grid Down at 100 ms a key (recording, UIA, AT-SPI) and a composite walk (recording, AT-SPI), with trees per key. The in-place walk takes children added, gone or moved: pairs them as the differ does (by position, then id), builds only the new subtrees and hands the bridge a full diff in the same order; a removed node's parent is a stand-in holding the old children (AT-SPI keeps the old index); `ReplaceChildren` swaps the array in one write; items added at or removed from a list's end and the open row handled. Children-dirty frames use it too. Internal test switch `Window.AccessibilityBuildsEveryRefresh`; no API change. Suite (JIT = AOT within 0.3 KB): grid Down 144.6 → 127.5 KB (−12 %), UIA −11 %, AT-SPI −10 %, trees 1.0 → 0 a key; filter below 1,000 rows 284 → 12.7 (−96 %; AT-SPI −94 %), min −6…−8 %; add a row, AT-SPI −81 %; `--compare` 0/0 (captures eac6fb72). Probes JIT, KB a key: Junction Changes Down UIA 155.6 → 116.6 (−25 %), trees 62 → 0 over 40 keys; wheel at 120 ms 51.3 → 25.9 (−50 %), 60 → 0; a notch a frame 21.5 → 17.7, 18 → 0; Aperture Down 286.1 → 260.4 (−9 %), 38 → 0; bridge off, nodes added/removed and AT-SPI signal counts identical. Audit over 7 suites: 0 STALE in 3,127; 5,450 in-place walks (3,356 structural), 0 WALK-VALUE, 0 WALK-EVENT; fallbacks 135 → 120. Event streams: Linux signals (5,146) and UIA raises (460) identical, Headless diffs by test identical. Core, Headless, both bridges, Aperture, Vesper, Meridian, Junction 1,075 pass; Fedora Headless 1,190, bridge 104; live pyatspi not run (harness gone). ACCESSIBILITY-PLAN.md. |
| 7 | 3d54d779 | junction-history-prefetch-on-rest | History loads a commit's detail once the selection has rested 120 ms, acting on the last move's time, not the timer's firing (a first version raced). 60 ms held walk, KB a key: all threads 1,642.8 → 30.1 AOT, 1,643.0 → 30.4 JIT (−98 %); UI 33.2 → 30.1 (−9 %); GCs per 100 keys 11/8/3 → 0/0/0; git processes a key 4 → 0; CPU min −12 %. A key every 500 ms with the UI thread live keeps its loads (5.70 git a key both ways). Tests fixed, not the app: `TagsViewTests.ATagsKeyRebuildsOnce` waits 600 ms of quiet for the watcher's first change (63467f8e), `BranchRootMemoryTests` waits for its git command (9b96bcd9) — both had relied on the prefetch's git processes slowing first frames. |
| 7 | 532d1679 | junction-createmr-fields | Create MR title builds only when it empties or fills (`MarkViewDirty`; only the primary button reads it), the description only while `IsStale` says the bar would change. KB a key AOT (JIT agrees): title 247.3 → 2.8 (−98.9 %) over History, 282.4 → 2.4 over Changes with 200 files; description 118.2 → 3.1 (−97.4 %), 136.9 → 3.3. RichTextToolbar remarks and ELEMENTS.md: wire `SelectionChanged` through `IsStale`. |
| 7 | 4b22d3cb | junction-issue-list-keys | The composer's issue list moves by writing the composer's revision, not `MarkDirty`: 206.1 → 38.4 KB a key (−81 %) at 1 and 200 files; with a reader 209.7 → 42.0. |
| 7 | 4c603918 | junction-composer-skip-build | The composer part records what it shows of the text (issue token, highlighted row, closing reference; both need a '#'); a character builds only when that changes. Off GitLab 14.6 → 2.0 KB (−86 %), on GitLab 16.3 → 2.0 (−88 %), with a reader 2.6 both; a11y 0.5 KB a character, no tree; typing still told. |
| 7 | 4fcc9663 | junction-renamed-file-colours | Colour bug fix: the diff parser keeps git's `rename from` path in a new `GitFileDiff.RenamedFrom`; `HighlightAsk` reads the old side there (`OldPath` left unset: the review composer sends it to GitLab as `old_path`). Only the four deleted lines' 20 px bands changed in the scene. Step 8a: six scenes dark and light (History, Create MR, issue list, composer on/off GitLab, renamed file) A/A 0 and A/B 0 but the intended bands; suite 0/0 JIT + AOT, no row's bytes moved over 2 % (captures 5912b804); Junction 1,081 ×2, Core 5,042 pass. |
| 7 | eff3ba90 | vesper-brief-marks-per-cell | `BriefKeptCell` holds `Marked` and `Cut`; the Brief grid's state is a new shape-only `ShapeState<T>` (Thumbnails keeps `GridState`). AOT KB a key (400 files, 1240×820): Insert 471.0 → 35.7 (−92 %), min 1.323 → 1.211 ms (−8 %); Space on a file 470.1 → 31.8 (−93 %), on a folder (marked and measured) 469.8 → 31.8; Shift+Down/Up 470.6 → 35.3; times −8…−9 % (JIT −23…−25 %); with UIA Insert 471.5 → 36.2, −11 %, 0 trees both sides. Cells made a key 140 → 1–2. Type-to-find unchanged (180.6 KB): its ~45 cells a key are entries the filter brings on screen, none remade (REACTIVITY.md). 71 scenes (24 new Brief scenes; cut set by reflection — `Clipboard.SetFiles` writes the user's real clipboard) A/A clean, pixels/a11y/focus identical; suite 0/0, no row's bytes moved (captures 79f0515c); Vesper 878 pass; 2 reverse edits. |
| 7 | 9a9c4271 | meridian-assertion-banner-key | Bug fix: the stale banner is keyed, so `ReconcilePrefixSuffix` no longer patches assertion row 0 into it; the focused field keeps the keyboard as the banner comes and goes (before: the first key after a send left no focus, the second was lost). Typing with the banner up 114.4 → 114.9 KB a key, time within noise. REACTIVITY.md (Tree Diffing): a child that comes and goes ahead of same-type siblings needs a key. |
| 7 | 3e2d5864 | meridian-prompt-double-build | A submitted environment prompt builds the window once, not twice: 140.0 → 87.1 KB (−38 %, JIT the same), min −4 % AOT. |
| 7 | e0ace9c9 | meridian-url-typing-skip-build | A URL character builds nothing when it moves nothing shown (Headers, Body, Auth: compare the Params and Chain counts and the stale banner). KB a character AOT: Headers 66.6 → 4.0 (−94 %), min −17 % (JIT −27 %); Body −93 %, Auth −94 %; a full query string −83 %; Params unchanged. REACTIVITY.md: a text field holds its own text, so a key builds only for what else on screen reads it. |
| 7 | 5a9f8a54 | meridian-history-list-drawn-selection | Approved (question 4). History's list draws the selection (at 60 % for sends older than today, as the rows' own opacity); rows keep their own hover tint; `hist-row-N` ids kept; the selected send still heard as selected. Down 63.5 → 47.4 KB a key (−25 %), rows made 2 → 0; time over 400 keys AOT min 0 %, median −1 %, with a reader −5 %; JIT −4 / −8 %. Pixels: none for today's sends (3 scenes); older sends (7 scenes) only the selected row, fill 1/255 and glyph edges up to 6/255; one scene keeps a passed row's hover tint (up to 15/255, as Junction History). Step 10: 38 Meridian scenes, a11y and focus identical but the bug's own 3; suite 0/0 JIT + AOT, no allocation moved (captures 7ce5e216); Meridian 468 pass; 4 reverse edits. |
| 7 | 49509afd | syntax-long-line-cap | Approved (question 1). A line over P characters is tokenized on its first P only (never splitting a surrogate pair), the rest plain; the cut line leaves the next line the state it started in (as a line stopped at its budget), so a long line walked only for state isn't tokenized. P internal: 2,000, and 8,000 for `source.json`, `source.json.comments`, `text.xml` (dense 2,000-character lines: JavaScript 79 ms, C# 131; 8,000: JSON 65, XML 40). One line, no budget: JavaScript 32k 16,291 ms / 13.8 MB → 79 ms / 0.9 MB, C# 26,108 → 130 ms, JSON 907 → 65 ms; window budget: JavaScript 4k–32k coloured 2,442–173 → 2,000 characters, C# 1,160–108 → 2,000, 80–130 ms a line instead of 250; diff walk below a 4k/8k/16k line window 821/789/332 → 70/39/21 ms (JavaScript/C#/JSON); Files call over 322 real `node_modules` files with long lines 89.5 → 48–49 s (all-thread bytes +3 %). Slow grammars still meet the window budget at 2,000 (SQL 849 ms, CSS 284, PowerShell 281, C++ 268, Python 246; table of 17 in DECISIONS.md). Spans: 3,206-file corpus, every line ≤ 2,000 with no longer line above identical (1,687,032 lines, 3,922,381 spans); 71 of 80 longer lines and 396 lines below them in one file changed. LongLineTests 6 (5 fail without the cap, 2 under `<=`→`<`); Syntax 84, Meridian 468, Junction 1,080/1,081: a pre-existing one-frame race in `NewWorktreeBranchTests`, fixed by the orchestrator in 8b97f424 (waits for git, as 9b96bcd9). |
| 7 | 02a68f63, 110fcd27 | context-menu-as-menu | Approved (question 2). Suite row `a11y/menu bar walk over 1,000 rows, UIA`. `ContextMenuElement` defaults to `Role = Menu`; `DropdownElement` sets `List`; item roles from the list's role (`AccessibilityTreeBuilder.ItemRoleOf`). Context menus and menu-bar menus read UIA Menu/MenuItem (50009/50011, were 50008/50007), AT-SPI menu 33 / menu item 35, served menus over 2,000 items too; a Dropdown's popup stays List/ListItem. Items keep Invoke and SelectionItem (Toggle on a checked choice), AT-SPI click and selectable; the menu container keeps Selection, Scroll and ItemContainer only when it publishes items; submenu parents have no ExpandCollapse (Invoke opens them). Selection told by selection, not focus (IsSelected / `state-changed:selected`), patched, 0 trees. Suite 207 rows 0/0 JIT + AOT, no allocation moved (captures 7eb3279a); context-menu walk UIA 0.393 → 0.388 ms JIT, 0.5 KB, 0 trees. Event streams: context menu 58 of 104 lines differ, menu bar 86 of 167, dropdown 0 of 103 — every difference a role or control type. Audit 0 STALE in 1,717. Core 5,044, Headless 1,332, Linux bridge 104 (Windows and Fedora), Fedora Headless a11y 265, Fluent 34, Vesper 878, Junction menu tests 62 pass; goldens pass. ACCESSIBILITY-PLAN.md holds a six-step live-reader script (Narrator, NVDA, Orca) for the user. |
| 7 | 2357d8c8 | a11y-composite-selection-in-place | Takes in step 7's set-aside patch: a composite list's own `SelectedIndex` asks for no tree; a composite row's `AccessibleSelected` is patched in place (as a menu entry's); the in-place walk writes a node's selected state in place, children or not (`Sameness.SelectedMoved`). Suite KB a key JIT = AOT: composite item-row walk bridge on 32.4 → 25.3 (−22 %), UIA 34.3 → 27.1 (−21 %), list-drawn highlight UIA 25.2 → 18.1 (−28 %), AT-SPI 35.2 → 28.0 (−20 %); trees per key with a bridge 1.0 → 0.0. |
| 7 | 3ecbedfa | junction-changes-list-drawn-caret (API) | Approved (question 4). Changes' caret and hover tint drawn by its list (`SelectionHighlight`, `HoverHighlight`); **public additive `VirtualListElement.HoverRowFor` (`.HoverHighlight(fill, radius, rows:)`)** so held headers, "No changes" and picks get no tint; rows the caret reaches set `AccessibleSelected(false)` and the list marks the caret's row selected (a reader now hears the caret's row selected; before, no row was). 300 files, KB a key JIT / AOT: no bridge 91.5 → 74.2 / 89.9 → 72.8 (−19 %), UIA 108.3 → 86.2 / 107.2 → 85.2 (−20 %), AT-SPI 112.2 → 87.6 / 111.0 → 86.7 (−22 %); rows made 2.20 → 0.20 a key; trees 0; min cycles −5…−28 %; wheel −2…−3 %. The rest of a caret key is the diff pane redrawing the reached file. |
| 7 | 95c86c97 | junction-changes-rows-follow-their-item | `ChangesView` kept while a stage is in flight; items compare tree nodes by value (path, depth, segment, file count, file, folder totals), no index. Stage/unstage one file, KB an operation JIT / AOT: no bridge 2,271 → 1,280 / 2,349 → 1,334 (−44 %), UIA and AT-SPI −42…−43 %; rows made 62 → 5; min cycles −5…−12 %. Step 8b: 108 scene checkpoints dark and light identical but one intended hover case (a row the caret passes under a resting pointer keeps its tint and tooltip, 6 checkpoints per theme); a11y tree identical but rows' selected states (the caret's row selected in 30 of 41); focus, reader focus, Tab order identical; held header keeps focus; audit 0 STALE in 3,129; suite no row past 8 %, no allocation past 1 % (captures 04e4ce4c). Junction 1,082, Core 5,044, Headless 1,339, Fedora Headless a11y 348 pass. REACTIVITY.md, ELEMENTS.md. |
| 7 | 637811cc, bdd8cd1c | kept-elements-helper (API) | Approved (question 3). Suite rows `component/shelf walk Right/Down, 5 shelves of 7 cards` (every card made / cards kept / ring moved without a build). **Public `KeptElements<TState>` (HultUI.Core.Components)**: `Begin(build)` (only what the owner's previous build made or handed back can come back — a build can patch a dropped element by position into something else that stays in the window), `TryKeep(key, state, out element, place)` (equal state, still hosted, optional row-place check for virtual-list rows), `Made(key, state, element)` (sets the key, one ref per key that follows the patch), `Live(key)` (the live element, for a change made with no build), `Clear()`, `Count`; entries pruned each time the count doubles. 8 tests; ELEMENTS.md §28, REACTIVITY.md. |
| 7 | 31e1aaa7 | aperture-home-cursor-no-build | Aperture Home on `KeptElements`, and a cursor key builds nothing: it moves the ring on the live cards through `Live` (one ring value for the build and the move; checks the owner's latest build drew that view). AOT KB a key (before / cards kept / both): one shelf Right 63.7 / 32.9 / 0.2, Down 65.3 / 31.9 / 0.2, min 0.526 → 0.482 / 0.505 → 0.476 ms; five shelves Right 255.7 / 70.7 / 0.3, Down 249.3 / 69.3 / 0.3, min 0.801 → 0.729 / 0.784 → 0.730; builds a key 1 → 0; UIA same bytes, min −3…−12 %; JIT min −8…−29 %. Suite rows 218.4 → 38.2 → 0.3 KB, cards made 35 → 2 → 0; `--compare` 0/0 JIT + AOT (captures 80f14806). 34 Home scenes: pixels, a11y tree (bounds, offscreen) and focus identical for both routes, A/A identical. Core 5,044, Headless 1,347, Aperture 332 pass; 7 reverse edits. REACTIVITY.md "A key that moves only a ring needs no build". |
| 8 | f3302209, bda4dad3 | paint-beginframe-keeps-surface-clip | Correctness, the objection to d04464f6: `SkiaRenderContext.SetCanvas(canvas, baseSaveCount = 1)` and `BeginFrame` restores to that count; the Vulkan surface records `SaveCount` after its 8-bit kept-target and F16 offscreen clips (`WrappedImage.BaseSaveCount`, `_offscreenBaseSaveCount`, reset on `Hold` and a new offscreen); GL and raster still restore to 1. 2400×1600 canvas at 1.5× clipped to 1200×800: after `BeginFrame` save count 1 → 2, device clip 2400×1600 → 1200×800, `ClipBounds` 1601×1068 → 801×535. In-process saves over a 16-step Vulkan drag: a clip 64 px too narrow differs in exactly 64 columns with the fix (142–176k channels 8-bit, 139–173k F16) and in 0 without; shown pixels identical. Suite 0/0 ×2 alternated, allocKB flat, steady frame 0 B; goldens identical. `SurfaceClipThroughBeginFrameTests` (3 cases, reverse edits fail 2 each). Skia 360, Core 5,054, Headless 1,443, VulkanProbe 38 (`HULTUI_UITESTS=1`) pass. |
| 8 | 61a82eba, e0530518 | a11y-walk-name-without-string | The in-place walk's `Compare` checks a name from content with `NameIs` (the same rules as `ResolveName`, nothing built); folds a11y-walk-unchanged-bytes. Junction Changes at 300 files, KB a step, alternated ×2 (identical), same trees, diffs and events: caret Down tree/UIA/AT-SPI 92.0/94.7/96.8 → 81.6/84.3/86.4 (−11 %), wheel a notch every 120 ms 22.8/25.4/27.4 → 16.2/18.8/20.7 (−24…−29 %), bridge off flat (78.5, 16.6); a walk of the unchanged tree 6.86 → 0.02 KB. New suite row `a11y/selection walk 1,000 composite item rows named by their texts, UIA`: JIT 40.0 → 27.4 KB (−32 %), AOT 39.9 → 27.3, time flat. Suite JIT 0 slower ×2; AOT 1 then 0 (selection-walk rows +6–10 % in render only, rows without a reader too: AOT code layout). Audit 0 STALE, 0 WALK-VALUE/EVENT, fallbacks 125. |
| 8 | f35c3df9 | a11y-disabled-menu-item (API) | Approved (question 11). **Public default member `IAccessibleItemSource.ItemIsEnabled(int) => true`**; `ContextMenuElement` answers it; `MatchItems` compares it (enabling an entry in an open menu patches, no tree); items served past 2,000 read it. A dimmed context-menu or menu-bar entry was read as enabled; now UIA IsEnabled false, AT-SPI without enabled/sensitive (not yet heard with a live reader). Menu suite rows bytes flat, times within 3 %. ACCESSIBILITY-PLAN.md, ELEMENTS.md; docfx 0 warnings. Core 5,060, Headless 1,456, Linux bridge 104, Junction a11y 131 pass; Fedora 104 + 297; 11 reverse-edit failures. |
| 8 | bc007ae8 | text-richtext-paragraph-shares-lines | A rich text paragraph shares the layout's exact `TextLine[]`, makes its `TextLineBox[]` when `Lines` is first read, `SpansOf` returns an exact array, `RichTable.Normalize` makes no closure per block (`TextLines` documented read-only). KB a step JIT, idle-gated, alternated ×2: richtext/open 5,000 blocks 3,264.6 → 2,596.2 (−20.5 %), resize 1,000 654.7 → 544.1 (−16.9 %); retained with the document open 7.94 → 7.41 MB (−6.7 %). Tests: `AParagraph_SharesItsLayoutsLines_AndMakesTheirBoxesWhenRead` (0 B a line cached, boxes 20 B a line on first read), `Normalize_OfADocumentWithNoTable_AllocatesNothingABlock` (2,160 B without). |
| 8 | a10f21b1 | text-richtext-block-layout-slim | `BlockLayout` 152 → 72 B: cell and picture fields in a side object, a text block's style from its paragraph. On top of bc007ae8: open 2,600.9 → 2,245.7 (−13.7 %), resize 544.1 → 473.8 (−12.9 %), retained 7.41 → 7.06 MB. `RichTextLayoutCostTests` width change 328 → 248 B a block. |
| 8 | 7aaae4c8 | text-richtext-block-list-no-copy | Block layouts in a `List` shifted in place when an edit changes their count: richtext/enter in 5,000 blocks 81.5 → 42.4 (−48.0 %), resize −1.6 %; an Enter test 16 → 8 B a block (the rest is `RichDocument.Blocks`' own copy, declined). Wheel through 5,000 blocks +2.7 / +0.7 % min, inside its base's spread. |
| 8 | 48ed3a35, 734a6cd7 | richtext-short-viewport-jitter | Bug fix: a caret at least as tall as the view scrolls to its top (was 18.6 px off, key after key). Step 3 overall ec5d1275 → 48ed3a35: JIT open −31.1 % (min −5.7 %), resize −28.8 %, enter −48.2 %; AOT open 3,267.2 → 2,250.2, resize 654.7 → 466.0, enter 81.8 → 42.4, times within 3 %. Suite 214 rows 0/0 ×2 and base/base. 902 in-process frames A/A = A/B, goldens pass; 147 reader trees (2.24 M character bounds) identical. Text 293, Core Rich 135, Skia 360, Headless 492, Junction MR/review 40 pass. |

## Examined, not worth it

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

Round 8 question (asked 2026-11-03). Recommendations in brackets.

11. **a11y-disabled-menu-item** (with step 2): disabled context-menu and menu-bar items are read as enabled. The fix
    adds a public default interface member, `IAccessibleItemSource.ItemIsEnabled(int)` (default: enabled), which
    `ContextMenuElement` answers from `IsDisabled`. Additive; no existing implementer has to change. [Yes: a reader bug,
    small.] → **yes, with step 2** (2026-11-03).

Round 7 questions 1–4, round 6 questions 1–9 and round 5's question 5, all answered, are in the archive
([round 7](17-performance-team-ledger-archive.md#needs-the-user-round-7), [round 6](17-performance-team-ledger-archive.md#needs-the-user-round-6)).
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

- **f9bceb6d's window checks** (round 6 step 4; expert 3 again in round 7): maximize on both monitors, the DPI
  hop and the Snap Layouts flyout on the lower-DPI monitor need the 4K monitor at 150 % attached and primary at
  sign-in; only the 1440p at 100 % has been attached. Then the UiTests `WindowPlacementTests` and the Borderless
  `WindowStateActivationTests` (`HULTUI_UITESTS=1`), plus a Snap Layouts hover by hand.
- **8a38ef4 on Linux/KWin and on AMD/Intel Windows:** not measured. Linux keeps the chained
  path. If AMD/Intel do not stall, destroy-first may cost them 21–24 ms per resize, as it
  cost NVIDIA's IMMEDIATE and FIFO. A real Aperture resize run is also still to do.
- **4315c4b on AMD, Intel and Linux (GLX/EGL):** not measured (WSL Fedora has no GL
  stack; installing one needs sudo). Depth is a minimum in WGL/GLX/EGL, so 0 cannot fail
  where 16 succeeded.

## Log

From 2026-11-02 (round 7's survey); earlier entries are in the archive ([to 2026-10-25](17-performance-team-ledger-archive.md#log-2026-10-07-to-2026-10-25),
[2026-10-26 to 2026-10-27](17-performance-team-ledger-archive.md#log-2026-10-26-to-2026-10-27),
[2026-10-27 to 2026-11-02](17-performance-team-ledger-archive.md#log-2026-10-27-to-2026-11-02)).

- 2026-11-02: round 7 survey started in a fresh context. Moved to the archive (counts checked before and after, no
  line lost): round 5's 34 Landed rows, round 6's build order with the survey's closed-from-Waiting note, round 6's
  answered questions 1–9 and round 5's question 5, and the 15 log entries of 2026-10-26 to 2026-10-27. The eight
  experts run in their own worktrees, reviewing cd3b6c8f..1939afcb (109 commits).
- 2026-11-02: round 7 survey done (reports 631fe15f..17d4220a, `17-reports/r7-*`). GL and Vulkan satisfied (steady
  frames 0 B, idle 0 wakes, 39 guarded windows); the other seven not. One review objection: 4325bac6's maps take up
  to 2,047 locks per `Count` (step 1). Round 6 helped every app walk (33 walks −65 %, 47 walks −80 %). The two
  unexplained leads are traced: Vesper's 5–6 KB key rest is the shortcut enumerator (found by experts 4, 6 and 8),
  the table's mid-depth hump is Skia's GPU blob cache keyed by fractional y (expert 2). 13 build steps; four
  questions for the user (long-line colour cap, context menus as menus, a Core kept-elements helper, list-drawn
  selection pixels in Meridian History and Junction Changes). Phase → build.
- 2026-11-02: round 7 step 1 landed (1986bed9, 2aa55491, 5000753a; captures 56ca60f1): the bridges' maps keep one
  lock (kept-tab switch −4.0 % AOT, −6.4 % JIT: round 6's regression won back), shortcut matching and the formatting
  bar allocate nothing (Vesper keys −11…−20 %), and a closed menu gives back its owner slot (leak closed). Guideline
  notes: a `ConcurrentDictionary` one thread writes, whose `Count`/`Keys`/`Values` runs per frame or apply, gets
  `concurrencyLevel: 1`; on per-key paths never `foreach` over an `IReadOnlyList` nor pass a struct collection to an
  interface parameter; overlay content never mounts or unmounts, so what an unmount releases needs an explicit release
  when the overlay closes; AOT TableView 10k sort moves ±6 % with code placement, JIT is the check.
- 2026-11-02: the user answered round 7 questions 1–4: all yes (long lines coloured on their first ~2,000
  characters; context menus and menu-bar menus read as menus, the live reader check theirs; a public kept-elements
  helper in Core, Aperture Home first; list-drawn selection in Meridian History and Junction Changes).
- 2026-11-02: round 7 step 2 landed (45628641, d748b316, 73819ebd; captures 7ac787f7): find typing −51…−54 % in every
  app's find, a replaced TextArea text −68 % (Meridian resend −36 %), truncated labels −24…−38 %, retained −2 %; bit
  equality on all three corpora. Process: the step's agent ran `git commit --amend` after the orchestrator had
  committed 3fc577ab, amending the ledger commit; it rebuilt the history (same tree, now d61b9ad1; unpushed commits
  only). From now on briefs say never amend, and the orchestrator doesn't commit while a build agent runs. Guideline
  notes: a per-call result comes from a kept scratch list taken while it is filled (nested calls get their own) and
  keeps an exact array, never the kept list; an index rebuilt every rerun but read only for rows on screen is built on
  first ask; a list dropped above a size and regrown on every big update gets `EnsureCapacity` with a lower bound. Left
  open: `PipelineTextParagraph` copies the layout's lines into its own array (could share the exact array when it adds
  no empty line).
- 2026-11-03: round 7 step 3 landed (18fe58a0; captures 3405d093): text blobs draw with their baseline on a whole device
  pixel, so Skia's GPU blob cache no longer grows with the distance scrolled. The 300×10 table after a long walk renders
  flat at 0.14–0.17 ms at every depth on GL and Vulkan (was 0.15 → 0.29 ms), frame −23…−24 % at 75 %; pixels
  identical on GPU and raster. Closes round 6's richtext-table-mid-depth-hump. Guideline notes: text drawn from a shared
  blob should land on a whole device pixel (only glyphs on the baseline, under the atlas limit, on a scale-and-translate
  matrix); pack flags into spare bits of a dictionary value struct, one extra field shows as allocation; use in-process
  readback for many GPU states — `HULTUI_VULKAN_CAPTURE` on an F16 swapchain stalls a frame over 1 s. Left open: no
  1.25× GPU capture (no 125 % monitor; raster covers it); Skia's real cache entry counts aren't reachable from SkiaSharp.
- 2026-11-03: round 7 step 4 landed (dc469aa0..df7f3a3f; captures 82c40437): item rows a patch moves follow their item
  instead of being made again (suite filter 19 → 3 rows, insert 20 → 1; Vesper type-to-find −55 % a key, median
  −19 %), focus survives a move. Adopted in Vesper; Junction's Files, Commit and Tags get it for free. Left for later:
  Meridian History and the palette build automation ids from the index (`hist-row-N`, documented `palette-row-N`), and
  Junction Changes needs memoized TreeNodes first. Guideline notes: keep the index out of an item and carry only what
  the row shows of its place — an automation id made from the index counts as shown; after a reverse-edit run rebuild
  every test project whose bin holds the mutated assembly (a stale bin showed a false failure); test-based probes run
  in the worktree (scratch trees pass MAX_PATH); `C:\Users\fredr\AppData\Local\Temp` is itself a git repository, so no
  `git apply` inside the scratchpad.
- 2026-11-03: round 7 step 5 landed (8bd2a2a2..c66d1bbb; captures 88f72224): VirtualList rows lay their margins out
  inside their place, so the palette drops a box per row; the palette's list draws the choice and tells the chosen
  composite row to a reader; Down moves the choice through the list's ref. Palette Down −82 % (suite), −88 %
  (Junction), −96 % (Meridian); filter and typing −12…−19 %; pixels and reader identical. Guideline notes: give a
  VirtualList row an inset with margins, not a box around it; a list can draw the selection on composite rows — rows
  `AccessibleSelected(false)`, the list tells the selected one; when only the list shows the selection, move it
  through the list's ref instead of rebuilding the owner. Left open: `SelectionOverlay` is placed over the whole place,
  not inside a row's margins (no app uses both).
- 2026-11-03: round 7 step 6 landed (f5e8baeb, b6119ae9; captures 62ac3205), in another form than planned: a find
  step that moves only the current match builds nothing — new additive public `FindBar.Moved`, the count written into
  the live label, the two rows invalidated (Enter −64…−79 % in Junction, Meridian and Vesper; suite row −95 %). The
  bar as its own `Part` was measured and declined (48.8 / 28.6 / 38.4 KB an Enter). Guideline note: when a key only
  moves a highlight, don't build — write the changed label on the live element and tell the list the two rows that
  changed (REACTIVITY.md's "value that changes ten times a second"); a control in a `Part` still rebuilds every
  element in it (~16 KB for the find bar). Left open: a typed character still builds the whole view (Junction diff
  406 KB a key, mostly rows making new highlights); the terminal and Fluent samples use `Build()` without `Moved`.
- 2026-11-03: round 7 step 7 landed (31595ee9..2937e090; captures eac6fb72): a layout refresh or a Children-dirty
  frame updates the accessibility tree in place when children come, go or move — 0 whole trees on Junction Changes
  keys and wheels and Aperture's grid (were 1–1.55 a key), filter −96 %, Changes wheel with UIA −50 %, Down −25 %; a
  reader gets the same diff (audit 0 STALE, 0 walk mismatches over 3,356 structural walks; event streams identical).
  Guideline note: a reader no longer costs a whole tree when rows are remade or scroll in; what still builds one is a
  property change no patch takes (a kept composite row's `AccessibleSelected`, a composite list's own `SelectedIndex`);
  tests comparing against a full build use `Window.AccessibilityBuildsEveryRefresh`. Left open (Waiting
  a11y-composite-selection-in-place): composite-list selection still builds a tree (suite composite walk 1.0 a key);
  the walk allocates 6.9–8.2 KB even on Junction's unchanged tree (likely row names from text content); 11 Headless
  tests fail under the audit splice (7 before), all pass without it.
- 2026-11-03: round 7 step 8a landed (3d54d779..4fcc9663, test fixes 63467f8e and 9b96bcd9; captures 5912b804): Junction
  History's held walk −98 % on all threads with 0 git processes and 0 GCs per 100 keys; Create MR typing −97…−99 %;
  issue-list keys −81 %; composer typing −86…−88 %; a renamed file's deleted lines get their colours. Guideline notes:
  a prefetch for a view not on screen waits for the selection to rest, acting on the last move's time; wire an
  editor's `SelectionChanged` through `IsStale`; when typing builds a part, record what the part shows of the text and
  compare against that; a walk host that sleeps the UI thread between keys can't measure timer-based work — pump frames
  through the gap. Usage 72 % weekly after 8a: the remaining steps go smallest-first so the round can release before
  the 90 % pause — 9, 10, 12, 13, then 8b and 11 if usage allows (else they queue for round 8).
- 2026-11-03: round 7 step 9 landed (eff3ba90; captures 79f0515c): a Vesper Brief mark, Space or range mark makes
  only the cells it changes (−92…−93 % a key, time −8 % AOT). Guideline notes: state only some cells show (a mark, a
  cut) goes in each cell's record, not the shared shape — a set in the shape makes every cell again when one member
  changes; a driver must never send Ctrl+X/C to Vesper (it writes the system clipboard). A small brief cost ~1 % of
  weekly usage (73 % after it).
- 2026-11-03: round 7 step 10 landed (9a9c4271..5a9f8a54; captures 7ce5e216): Meridian's assertion field keeps the
  keyboard after a send (bug fixed), a submitted prompt builds once (−38 %), URL typing on Headers/Body/Auth builds
  nothing (−93…−94 %), History's list draws the selection (−25 %, rows 2 → 0; approved pixels only on older sends'
  selected row). Guideline notes: a child that comes and goes ahead of same-type siblings needs a key; a text field
  holds its own text, so a key builds only for what else reads it; when a row has its own opacity, draw the list's
  highlight at that opacity. Left open: a History key still builds the window because the archive panel shows the
  selected send (making it a part would save ~40 KB a key). Usage 74 % weekly; small briefs cost ~1 % a step.
- 2026-11-03: round 7 step 12 landed (49509afd; test fix 8b97f424): long lines are coloured on their first 2,000
  characters (8,000 for JSON and XML); a 32k-character JavaScript line 16.3 s → 79 ms headless, a diff walk below long
  lines 821 → 70 ms in a window, a real `node_modules` Files call 89.5 → 48 s. No perf-suite row covers HultUI.Syntax,
  so no captures. Guideline note: app authors need do nothing; lines between P and what the old budget reached get
  less colour; tests that colour a long line get a bounded, deterministic result. Left open: slow grammars (SQL,
  CSS, PowerShell, C++, Python) still meet the window budget at 2,000 — a per-grammar P table is the follow-up.
- 2026-11-03: round 7 step 13 landed (02a68f63, 110fcd27; captures 7eb3279a): context menus and menu-bar menus read as
  Menu/MenuItem on UIA and AT-SPI; a Dropdown's popup stays a List; bytes and time flat, 0 trees a move. The user's
  live-reader check has a script in ACCESSIBILITY-PLAN.md (if a reader stays silent on a move, moving focus to the
  selected item would need the user's approval). Found, not fixed (Waiting a11y-disabled-menu-item): a disabled menu
  item is published as enabled (the item node always sets `isEnabled: true`).
- 2026-11-03: round 7 step 8b landed (2357d8c8, 3ecbedfa, 95c86c97; captures 04e4ce4c): a composite list's selection
  builds no a11y tree (−20…−28 %, trees 1 → 0 a key); Junction Changes' caret and hover are drawn by its list (−19…−22 %
  a key, rows 2.2 → 0.2; new additive `HoverRowFor`); a stage moves the rows it shifts (−42…−44 %, rows 62 → 5).
  Reader change: Changes now tells the caret's row as selected (as the palette's rows since round 6 question 7).
  Guideline notes: an item holding a remade model object compares by what the row shows; keep a derived view while
  its inputs are unchanged; `HoverHighlight(rows:)` for rows that never tinted themselves. Left open (Waiting
  junction-changes-leads): the diff pane is now a caret key's main cost (~74 KB); a stage with a reader still builds
  one tree; only the caret is told, not a multi-selection's other members; a reader's "select" on a folder row clicks
  it, which opens or closes the folder.
- 2026-11-03: round 7 step 11 landed (637811cc, bdd8cd1c, 31e1aaa7; captures 80f14806), the round's last build step:
  public `KeptElements<TState>` in Core hands back the elements a build did not change, and Aperture Home's cursor key
  builds nothing (one shelf 63.7 → 0.2 KB a key, five shelves 255.7 → 0.3, min −7…−9 % AOT); pixels, tree and focus
  identical. Guideline notes: "only from the build just before" needs the owner's build count; a key that changes only
  a ring and the focus can skip the build (change the live element, one ring value for build and move); put shared
  settings in each element's record-struct state, not a separate shape. Left open: Vesper Brief, Thumbnails and
  Aperture's grid onto the helper (later, per the user); a Home build with cards kept still costs ~33 KB a shelf;
  pre-existing: the cursor can walk past a shelf's visible page and then no card shows the ring. Every build step is
  done; Phase → full suite. Usage 80 % weekly.
- 2026-11-03: round 7 full suite on cedb6747: Windows `dotnet test HultUI.slnx -c Release` 30 projects 0 failures
  (Junction 1,082, Core 5,044, Headless 1,347), Fedora `wsl-test.sh` 22 projects 0 failures, docfx 0 warnings. Then
  main had moved (f64e4674, another session's app-support merge, 25 commits, 91 files, 14 overlapping) and its own CI
  was red (docs: InvalidCref `MakeCurrent` in HeadlessSession; Windows: 3 ConPTY tests timing out on the runner, passing
  here). Merged main into the branch (c655b411): three conflicts, each two additions kept (the item node's role from
  step 13 and its expanded state from app-support in `MakeItemNode` and its callers; main's `InScrollable` beside the
  internal `MapControlType`). The full suite on the merge: Fedora 22 and Windows 29 projects clean, Headless 2 failures
  — step 13's MenuRoleTests expected Invoke on a submenu's parent, which now expands (round 6's rule) since item nodes
  carry an expanded state — and docfx's main-side cref. Fixed in 7dfd2ce3 (test, ACCESSIBILITY-PLAN.md, cref → `Enter`):
  Headless 1,443 pass, docfx 0 warnings. Phase → release.
- 2026-11-03: round 7 merged into main as df45b4a2 (detached origin/main merge in this worktree, tree identical to the
  tested branch; the push gate held: origin/main f64e4674 was in the branch). Primary checkout fast-forwarded behind
  the guard; branch pushed. CI run 41691075588 green: test 22 min, test-linux 6.5 min, docs 1.5 min (main's own run on
  f64e4674 had been red: the docs cref, fixed in 7dfd2ce3, and 3 ConPTY tests timing out on the Windows runner, which
  passed this time — a runner flake candidate). Round → 8, Phase → survey; the loop stops, and round 8 starts in a
  fresh context.
- 2026-11-03: round 8 survey started in a fresh context (main had not moved: origin/main df45b4a2 is in the branch).
  Moved to the archive (counts checked before and after, no line lost): round 6's 51 Landed rows, round 7's build
  order with the survey's closed-from-Waiting note, round 7's answered questions 1–4, and round 6's 34 log entries
  (2026-10-27 to 2026-11-02). Weekly usage 81 %; asked the user, who chose to run the survey now though it may pass
  the 90 % line. The eight experts run in their own worktrees on 27020cdb, reviewing 17d4220a..df45b4a2 (101 commits,
  main's app-support merge f64e4674 included).
- 2026-11-03: round 8 survey done (reports 8748c603..eaa1ec29, `17-reports/r8-*`). Layout and GL/Vulkan satisfied;
  the other six not. One review objection: d04464f6 (main's app-support) drops the Vulkan surface's drag clip at every
  `BeginFrame` (step 1). Experts 4 and 7 found the same lead independently: the in-place a11y walk builds
  name-from-content strings to compare them (step 2; it was a11y-walk-unchanged-bytes). Round 7 helped the apps: 47
  walks −14 % KB/key (geometric mean 0.60), none worse by more than 2 %; Aperture's walk processes now exit cleanly.
  9 build steps (rich text blocks, picture landings coalesced, cursor keys that build nothing in Aperture and Vesper,
  Aperture Home's build, Meridian History's archive part, a measure-first faded-rows step); one question for the user
  (11: an additive `IAccessibleItemSource.ItemIsEnabled`). Bug found: Aperture Home's cursor walks onto a hidden shelf
  page (step 5). Survey agents 235–375k tokens each; weekly usage 81 → 86 %. Phase → build.
- 2026-11-03: the user answered question 11 yes: disabled menu items get `IAccessibleItemSource.ItemIsEnabled` with
  step 2.
- 2026-11-03: round 8 step 1 landed (f3302209; captures bda4dad3): `BeginFrame` restores to the save count the surface
  handed over, so the Vulkan drag clip (6c627f27, 1b11aeda) holds again (`ClipBounds` 1601×1068 → 801×535; a 64 px
  too-narrow clip shows in in-process saves again); suite flat, pixels identical. Guideline notes: a surface that puts
  state on the canvas before the context's `BeginFrame` hands over its base save count, and resets restore to it; to
  prove a clip, compare two in-process saves with a deliberately wrong clip (`--frame-counter` cannot see it);
  VulkanProbeTests skip without `HULTUI_UITESTS=1` and an all-skipped run exits 0. Usage 87 % weekly (~1 % a small step).
- 2026-11-03: the user paused step 2 while its agent was adding tests; the agent was stopped (no commit, 5 files of
  uncommitted work kept in the worktree) and the loop stopped. Usage 87 % weekly.
- 2026-11-08: the weekly limit reset (0 %); the loop resumed at 04:07 local as the user asked. Main had moved 32
  commits (12d0392e: Meridian discovery and runner auth, text selection in scrolled views; none in step 2's files):
  merged as 3a15b5b1. Step 2 resumes from the first agent's uncommitted edits.
- 2026-11-08: round 8 step 2 landed (61a82eba, f35c3df9; captures e0530518): the in-place a11y walk compares a name
  from content without building it (Junction Changes with a reader: caret −11 %, wheel −24…−29 %; new suite row −32 %
  JIT and AOT; audit clean), and disabled menu items are read as disabled through the new default member
  `IAccessibleItemSource.ItemIsEnabled` (question 11). The first agent's diff was complete; the resumed agent measured
  it. Guideline notes: an AOT before/after can move render time on rows the change never runs — split the timing by
  phase before calling a row slower; for a probe, put the old code behind a temporary switch in the same build so old
  and new share one binary. Usage 2 % weekly (~2 % for the step).
- 2026-11-08: round 8 step 3 landed (bc007ae8, a10f21b1, 7aaae4c8, 48ed3a35; captures 734a6cd7): rich text paragraphs
  share their layout's lines and make line boxes when read, a block layout keeps no cell/picture fields or second
  style, block layouts shift in place, and a caret taller than the view shows its top. Rich open −31 %, resize −29 %,
  Enter −48 % (JIT and AOT), retained −11 %; pixels and reader trees identical. Guideline notes: a collection
  expression `[x]` passed as `IReadOnlyList<T>` is a compiler single-element list, not an array, so `is T[]` fast paths
  miss it; the AOT publish needs the VS Installer folder on PATH (vswhere). Flake seen: Junction
  `ReviewTests.TheKeyboardReachesTheCommentAgain` once under load (passed 5 reruns). Usage 2 % weekly.
