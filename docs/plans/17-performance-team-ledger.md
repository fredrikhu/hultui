# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 9
- **Phase:** build — round 9, step 15 next; step 13 measured, its pixel changes wait for questions 20–22 (step 13b;
  the user asked for comparison images, sent 2026-11-09: `artifacts/r9-fade-compare/`); steps 1–12, 14 and 16 landed
  2026-11-08/09 (16 steps in "Round 9 build order"); question 23 open (not blocking). Build
  agents run one at a time in this worktree; their briefs go in session c484f5ac's scratchpad as `brief-r9b<N>.md`.
  Questions 14–19 answered. Open for the user: question 10 (`TokenizeDiff` public), the live screen-reader check of
  menus and disabled menu items (ACCESSIBILITY-PLAN.md). Weekly usage 17 % after the survey
  (reset 2026-11-15 03:00 UTC; extra usage off); the 90 % pause applies to build steps (user, 2026-10-27). Older rounds are in [the archive](17-performance-team-ledger-archive.md)
  (search it, don't read it whole). History note: rewritten 2026-10-25 (every commit after 3353ce47 has a new ID; a
  pre-push hook refuses pushes built on the old history). To resume in a fresh context, run in this worktree: `/loop
  Run the next step of the performance team: follow docs/plans/17-performance-team.md, with state in
  docs/plans/17-performance-team-ledger.md. Read both once per session (afterwards only the ledger's Phase and the part
  the step needs), do the step the ledger's Phase calls for, record it in the ledger, and commit.` Before resuming,
  `git fetch` and merge main if it moved.
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`)
- **Review commits since:** round 8's survey reviewed 17d4220a..df45b4a2 (round 7's build); round 9's survey reviews
  round 8's build, eaa1ec29..10acc8c0 (the last survey report to the round 8 release), which also brings main's merge
  3a15b5b1 (32 commits from other sessions: Meridian discovery and runner auth, text selection in scrolled views) —
  review it like the rest.
- **Probe code:** round 9 surveys in session c484f5ac's scratchpad
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
| 1. Layout | 9 | no | **d366505b**: a column change in Aperture's grid remakes every visible card (`TryKeep(..., place: key)`, ApertureApp.Library.cs:396): resize walk 6.1 → 14.1 KB a step (+131 %) → step 1; **e7d10c63** (main): the new edge-scroll tick runs `RepaintGroup`'s whole-tree walk and new `List` every frame → step 4. 213 shared suite rows: no layout counter rose |
| 2. SkiaSharp: painting | 9 | no | **967ce1ac** (main): the Request tab's sent body is one plain label, and `DrawText` draws every line every frame (1,903 runs a frame, 1,706 lines off the clip, at 89k characters) → step 2. f3302209 and 307ee07c hold |
| 3. GL and Vulkan | 9 | no | none (steady frame 0 B and idle 0 wakes on GL, Vulkan 8-bit and F16, JIT and AOT; the drag clip holds within +1.3 px; 30 guarded windows at 1.5× on the 4K monitor, 0 foreground takes, no new `nvlddmkm` events) |
| 4. .NET runtime | 9 | no | **f276bb38 + 417ef713 + 967ce1ac** (main): Windows' keyring has no memo, so every build asks `CredReadW` per secret (22–66 a key) → step 8; **12d0392e** (main): a large spec read as a string leaves up to 8 MB pooled → step 11. Suite 0 of 214 rows rose, JIT and AOT |
| 5. Text pipeline | 9 | no | none (48,000-layout hash 7A18AF8DE8E05271 and both 240,000-layout corpora unchanged; rich text rows as landed) |
| 6. Reactivity and reconcile | 9 | no | **41f48bd3** (main): each request row's actions button and menu make Meridian's tree part +42…+51 % a full build → step 9 |
| 7. Accessibility bridges | 9 | no | none (audit: 0 STALE in 3,138; 5,914 in-place walks, 0 mismatches; fallbacks 126; round 8's no-build cursor keys 0 trees with every reader) |
| 8. App workloads | 9 | no | **d366505b** (bisected): a grid column change 79/92 → 243/256 KB → step 1. Round 8 helped the apps: 49 walks 4,901 → 4,035 KB/key (−18 %, geometric mean 0.78), round 6's 33 walks 2,029 → 1,760 (−13 %); Meridian find typing +8.5 % (under the bar) |

## Queued candidates

Rounds 1–8's build orders all landed but round 6's step 8, declined (rounds 5–8 are in [the archive](17-performance-team-ledger-archive.md#round-8-build-order),
with the round 8 survey's closed-from-Waiting note).
A build step may bundle one expert's related small candidates as separate commits. Items marked **(user)** wait for a
decision in Needs the user; the rest of their step goes ahead.

### Round 9 build order

Ranked by expected win over cost: the review objections first, framework before apps. Each step names the report
lines its brief quotes (`r9-k` = `17-reports/r9-k-<area>.md`).

1. **apps-aperture-grid-columns-keep-cards** (6; r9-1 Review and Candidate 1, r9-8 Review and Candidate 3) — the
   objection to d366505b: `GridRow` hands a card back only into the row with its old key (`TryKeep(..., place: key)`,
   ApertureApp.Library.cs:396); round 7's `GridCards.TakeBack` handed it into any row while its old row was still under
   the list. Prototype `place: null` (r9-1 `probe-r9.patch`, `PROBE_GRIDPLACE`): resize walk 14.1 → 5.4 KB a step
   (round 8: 6.1), a step across a column change 236.1 → 63.0 KB (−73 %), elements 249 → 58, text measures 110 →
   14.5; expert 8's AOT walk `library-resize-4px` 6.5 → ~3.7 KB (bisect 40c2c554 3.6, d366505b 6.9). Check ring, focus
   and pixels across a column change plus d366505b's 7 scenes; add a suite row (kept grid, column change). Small, app.
2. **paint-label-lines-cull** (2; r9-2 Review and Candidate 1) — `SkiaRenderContext.DrawText` (both overloads,
   SkiaRenderContext.cs:815/861) skips lines wholly outside `Canvas.LocalClipBounds` with `DrawParagraph`'s line-height
   slack (:924-935), reading the clip only for labels of more than one line. Meridian Request tab, 89k-character body
   (prototype `PROBE_TEXTCULL`): wheel runs a frame 1,903 → 194 (−90 %); opening the tab render 538.5 → 13.8 KB, step
   3,783 → 3,258 KB (−14 %); pixels identical (A/A control); suite unchanged. Add a suite row (wheel over a 2,000-line
   plain label in a ScrollView). Small, framework, no API or pixel change.
3. **dotnet-hover-holds-removed-view** (4 with 1/6; r9-4 Candidate 2; **question 14: re-hit-test**) —
   `Window._lastHoveredElement` (Window.cs:27/2168) and `PointerState._hoveredElement` (PointerState.cs:16) keep a view
   a build removed alive until the pointer moves: Meridian with an imported spec, pointer at rest, 471 KB of 4.9 MB (50
   endpoints), 4.0 of 12.3 MB (500), 15.5 of 38.3 MB (2,000). After a frame's reconcile, when the hovered element is no
   longer hosted, re-hit-test at the last pointer position (as the drop fix does), firing leave/enter, so the element
   now under a still pointer shows its hover style. Test retention (weak reference) and the hover events. Small, Core.
4. **layout-group-drag-label-list** (1; r9-1 Review e7d10c63 and Candidate 2) — `CollectGroupLabels` (Window.cs:2869)
   reuses one scratch list; optionally walks from the drag's scroll view, not the root. Vesper viewer, 20,000-line log
   (290 elements, 37 labels): a drag move 1.2 → 0.0 KB (`PROBE_GROUPLIST`), a frame held past the edge 3.7 → 2.6 KB
   (−30 %); callers `UpdateGroupDrag:2781`, `TickSelectionEdgeScroll:2852`. Add suite row `text/selectable viewer drag
   select`. Small, framework.
5. **a11y-patch-diff-lent** (7; r9-7 Candidate 1) — the window keeps one `TreeDiff` and lends it for patches
   (`TryPatchA11y`, Window.cs:963; the walk's patch and structural diff, :1204/1213) to bridges that let go of it when
   the call returns (UIA and AT-SPI, an internal marker); `HeadlessAccessibilityBridge` keeps `Diffs`, so it and any
   other bridge still get fresh ones. Prototype (`apply-proto-patchdiff.py`, ~280 B a patch): suite `a11y/type in
   TextInput` UIA/AT-SPI 1.3 → 1.0 (−23 %), `click checkbox in 500 rows` UIA 0.8 → 0.5 (−38 %), AT-SPI 0.7 → 0.4
   (−43 %); Meridian prompt typing −15 %, Backspace −26…−28 %, Vesper Detail cursor −9…−12 %; trees 0, staleness equal.
   Rerun the audit. Small, no API or reader change.
6. **text-textarea-mono-ascii-breaks-by-count** (5; r9-5 Candidate 1; **question 16: covered by round 4's approval**) —
   a wrapped row of printable ASCII in a proven monospace face breaks at n = floor(width / advance), then walks back to
   the last space as the search does; falls back to the search within the proof's rounding of a multiple of the
   advance; per row, the first n + 1 characters must be printable ASCII (`WrapLine` → `FindLineBreak`,
   TextAreaElement.cs:814-1139; prototype `proto-wrap-by-count.patch`). Cold open, KB: Meridian 1 MB HTML 8,271.6 →
   330.2 (−96 %), pretty −94 %, 1 MB JSON −74 % (warm −97 %), Junction 1 MB minified bundle 63,637.5 → 360.5
   (−99.4 %), TODO.md −25 %; rows identical to the measured path at every width (13 corpora, up to 7.0 M rows). Guard
   n ≥ 1 and a finite width (the prototype throws on NaN). Add suite row `text/open a minified file in a monospace
   TextArea`. Small, internal.
7. **GL and Vulkan: resize posts and the interop check** (3; r9-3 Candidates 1 and 2) — (a)
   **gl-vk-live-resize-stuck-post**: `RenderLiveResizeFrame` draws a clean frame on every live-resize event while the
   main-thread queue is non-empty (Application.cs:573, :583-586), so a post that can't run until the drag ends (the
   web view's placement flush, WebViewElement.cs:198-202, or any continuation) redraws the same picture every SDL tick:
   a held edge 64–65 frames a second → 0–1, a drag step 2 frames → 1; draw for posted work only when something was
   posted since the last such frame. (b) **vk-interop-check-allocates**: `SdlVulkanHost.TryEnableInterop`
   (SdlVulkanHost.Interop.cs:143-188) makes its lambda's closure (24 B) on every call, the cached return included, and
   `SupportsExternalGL` calls it on every GPU-source draw (SkiaRenderContext.cs:1484, :1693; Window.cs:1580): Vulkan
   8-bit and F16 24 B → 0 a frame with a web view or a video on screen. Guarded real windows (probe host `r9-gl-vulkan\host`).
   Small, no API or pixel change; each its own commit.
8. **Meridian typing builds nothing + Windows keyring memo** (6 with 4; r9-6 Candidate 1, r9-4 Candidates 1 and 4,
   r9-8 Candidate 2; **question 15: yes**) — (a) an Environments value cell (MeridianApp.Environments.cs:341:
   `doc.SetValue(key, v); MarkViewDirty()`) builds only when `doc.Edited` flips: 3 × 12 312.3 → 2.3 KB a key, 3 × 40
   842.9 → 5.4, 5 × 40 1,167.2 → 5.4, keyring reads 10–36 → 0; a header value (Requests.cs:573-578) and the body
   (:678-683) with the URL box's check (e0ace9c9): 65.4 → 2.2, 59.4 → 3.9. (b) `WindowsCredentialKeyring`
   (Keyring.cs:119-133) gets the `KeyringMemo` Linux and macOS have (SecretServiceKeyring.cs:106): 22–66 `CredReadW` a
   key and ~59 a frame dragging the response pane with the Request tab → 0 after the first build. Prototype
   `r9-reactivity\final9.patch` (`R9_ENVFIX`). Small, app; each its own commit.
9. **apps-meridian-tree-rows-kept** (6; r9-6 Review 41f48bd3 and Candidate 2) — the tree part hands back unchanged
   request rows with `KeptElements`, keyed by path; state: method, display name, active, dirty, first produces entry,
   uses count, depth, display scale. A full build at 25 requests 321.1 → 153.6 KB (−52 %), 200 requests 1,579.2 → 236.1
   (−85 %), elements 1,399 → 199; a toast 654.5 → 315.8 KB; removes the 41f48bd3 cost (+42…+51 %). Prototype
   `R9_TREEKEEP`. Medium, app.
10. **vesper-brief-scroll-before-build** (6/1; r9-1 Waiting re-rank, r9-6 Candidate 3; was brief-columns-lag-a-key) — a
    Brief key that scrolls sets the scroll before its build (`ScrollBriefToCursor` in `MoveCursor` before
    `MarkViewDirty`; `BriefGeometry` reads `ScrollOffsetX`, Views.cs:612; `OnAfterBuild` scrolls, CommanderApp.cs:842).
    Down ×402: builds 29 → 18 (−38 %), 14.0 → 11.2 KB a key (−20 %); PageDown: blank columns in view after 5 of 6 keys
    → 0. Prototypes `PROBE_BRIEFSCROLL` (r9-1), `R9_BRIEFFIX` (r9-6). Small, app; bug fix.
11. **Meridian discovery at scale** (6 with 4; r9-6 Candidate 4, r9-8 Candidate 1, r9-4 Candidate 3 (a)–(c)) — (a) a map
    row's click (`MarkDirty`, MeridianApp.Discovery.cs:412; every row in a ScrollView, :344-364) moves the selection
    and builds the view only, handing back unchanged rows (`KeptElements` by `endpoint.Key`, or a VirtualList): 600
    endpoints 2,903 KB a click, 60 → ~340 KB (~4.4 KB a row); expected ≥ −80…−95 %. (b) the spill file is parsed from
    bytes (`JsonDocument.Parse(ReadOnlyMemory<byte>)`, no `File.ReadAllText`): 2.0 / 8.0 MB left in `ArrayPool.Shared`
    at 500 / 2,000 operations → 0. (c) `ApiMap.Record`/`SaveMap` per send (97–396 KB, 272–1,106 KB) only if (a) lands.
    Medium, app.
12. **webview-hidden-keeps-interop** (3; r9-3 Candidate 3) — a hidden web view releases its D3D11 interop texture, its
    `WGL_NV_DX_interop2` registration (VisualCapture.cs:343-398) and the sink's GL texture in a frame with the context
    current, and restarts the capture when shown: hidden 90.7 → 68.4 MB GPU (−25 %; Vulkan 146.5 → 124.2), ~−29 MB with
    the sink's texture. Medium. Question 17 answered: Meridian also disposes a preview hidden for about 60 s (all 45.4
    MB and the 6 `msedgewebview2` processes), as its own commit; the release covers that minute.
13. **Faded images and rows in Vesper** (2; r9-2 Candidates 2 and 3) — measure first, as round 8 step 9: Vesper's cut
    icons (`icon.Opacity = 0.45f`, CommanderApp.Pane.cs:991, Views.cs:666) are 26.6 (Detail) / 61 (Brief) / 29.5
    (Thumbnails, whole tiles, Views.cs:419) layers a frame, Sync's Equal rows (Sync.cs:313, `row.Opacity = 0.55f`)
    20–26 with "Show equal" on. Time real GL and Vulkan frames with and without the layers (prototype `PROBE_IMGFADE`:
    an `ImageElement` fades itself, ≤2/255 on icons). If ≥ 5 % of the frame, ask the user about the pixels (framework
    image fade; Vesper fading row and tile colours); otherwise record it as examined. **(user, only if it clears 5 %)**
14. **apps-tokenize-per-line-bytes** (5; r9-8 Candidate 5) — profile first: `SyntaxHighlighter.TokenizeLines` allocates
    ~5 KB a line (JSON 1,993 lines 9,863 KB a call, XML 5.05 KB a line) and 14.6 KB a line for HTML (3,641 lines 53,182
    KB; one line 2,680 KB); a pretty toggle on that HTML is 56,145 KB all threads, 34 gen-0 and 4 gen-1 GCs over 20
    toggles. Keep a change only if the bytes are in HultUI.Syntax (not TextMateSharp) and fall ≥ 10 %; else record it.
15. **Reader focus in Vesper and Meridian** (7; r9-7 Needs the user; **question 19: in the app**) — not perf. (a)
    vesper-reader-focus: Vesper's focused pane is a Box with no role or name, so `IsSemantic` drops it
    (AccessibilityTreeBuilder.cs:717; CommanderApp.Pane.cs:177) and every focus move tells a reader "focus on nothing";
    give the pane a role and name, and the cursor's Brief cell or thumbnail tile `AccessibleSelected` set with the
    `Restyle` call (a patch, 0 trees a key). (b) meridian-focus-lost (from Waiting): the new-request prompt's Escape
    puts focus back on "New request"; the Request tab's "reveal" keeps focus where a keyboard user expects it; History
    walks with an element focused. Check 0 trees a key and the audit. Small, app.
16. **frame-restore-keeps-no-sysmenu** (3, correctness; found by the user's Snap Layouts hover, 2026-11-08; **taken
    next, ahead of step 3**) — a custom-frame window **restored from maximized gets `WS_SYSMENU` back** (style 16C70000
    → 16CF0000) and nothing strips it again: our code only clears it in `Install` (WindowsCustomFrame.cs:188-190) and
    lends it in `ShowSystemMenu` (:581-591), so SDL's restore path rewrites the style. With `WS_SYSMENU`, Windows
    validates the Snap Layouts flyout against the standard caption band (memory `snap-layouts-caption-band`: 45 px at
    150 %, ~31 at 100 %), so a pointer entering the maximize button from below misses on both monitors (button 60 / 40
    px) and stays broken for the window's life. Found with the user's hand checks in Meridian and Vesper: a fresh
    window works and keeps working across monitors (read: no `WS_SYSMENU`, `HTMAXBUTTON` over the whole button, probe
    `r9-snap\hitcol.ps1`); maximize alone is fine; restore breaks it. Clearing `WS_SYSMENU` from outside
    (`r9-snap\strip.ps1`: SetWindowLongPtr + SWP_FRAMECHANGED) made the whole button arm the flyout again (the user).
    Fix: the frame's WndProc answers `WM_STYLECHANGING` (GWL_STYLE) by clearing `WS_SYSMENU` from `styleNew` unless
    `ShowSystemMenu` has lent it. Test (UiTests, guarded real window): after maximize, restore, full screen and back,
    and a monitor move, the style has no `WS_SYSMENU` (fails under a reverse edit); Alt+Space still opens the system
    menu. Synthetic cursor moves don't open the flyout on an inactive window (probes `r9-snap\flyout.ps1`,
    `diff3.ps1`), so the user hovers by hand at the end. Small, framework.

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
  no apply-after-layout form like the vertical one); a full build does the same, no column goes blank. Unverified:
  `ScrollEvent` comes only from the wheel (Window.cs:2496), so a scrollbar drag in Brief likely makes no new columns.
- **apps-toast-gone-view-only** (6, low; r9-6 Candidate 5) — a toast's going-away build rebuilds every part
  (`new ToastHost(MarkDirty)`, ApertureApp.cs:106, MeridianApp.cs:177): Aperture Home 128.3 KB flash to gone (73 %
  parts), Meridian at 25 requests 654.5 KB; view-only on the hide −36…−37 %. A one-off event.
- **apps-meridian-response-drag-build** (6/8, low; r9-8 Candidate 4) — each pointer move on the response pane's edge
  (8f7f8fa5) rebuilds the response Part: 39.5 KB a move, 86 % build; −86 % with the width set through a ref. A drag.
- **uitests-workareas-before-dpi-aware** (tests, small; found running f9bceb6d's checks) — the placement UiTests read
  `WorkAreas()` before SDL makes the process per-monitor aware, so the first Borderless pass sees the 4K at 96 dpi
  (2560×1392 at 0,0); and every UiTests window is made on the 96 dpi monitor first (`scale=1` at creation), so the
  high-dpi cases are brief moves. Set the test thread per-monitor aware first (`SetThreadDpiAwarenessContext(-4)`, as
  the scratch check `r9-4k\ZzR9FourKVisibleTests.cs` did).
- **findbar-countonscreen-foreach** (4, nit) — `CountOnScreen` loops `foreach` over `bar.Children` (32 B on AOT, 0.4 %
  of an 8 KB Enter); fix when FindBar is next touched.
- **terminal-noop-scrollback-wheel** + **terminal-row-strings-by-content** (2/Terminal, low) — a no-op notch draws a
  whole terminal frame (5,440–5,682 glyphs); 10,276 B a streamed line. Only the terminal sample uses HultUI.Terminal.
- **layout-ordered-children** (1, low) — suite rows only (wheel over 5,000 rows 4,954 hit visits); apps at most 33.5 hit
  visits a move and 13 culls a frame.
- **Suite scenarios** (8): document-switch (Junction Files 84.2 KB a key: 62 % view build, 1.23 builds a key — the
  file's text lands and builds again — 149 measure misses a key). (The kept-grid column change goes with step 1.)
- **textarea-chunked-document** (5/4; the user chose option B on 2026-10-25) — parked: Meridian's 89k body typing is
  ~238–241 KB/key, most of it the TextArea's new string; the user's 256k-character condition is unmet.
- **a11y-served-items-followups** — a live Orca check only (no desktop Orca here; optional).
- **F16 kept drag targets** (3) — deferred since round 5's `nvlddmkm` errors; never repeat blind. Round 9: an F16 drag
  frame allocates 2,136 B JIT / 2,040 AOT against 8-bit's 136/40 (the per-size wrap kept targets would remove), drag only.
- **gl-threaded-optimization-profile** (3; the user's driver setting, information only) — the walk lands while the
  window rests; the setting would remove only the per-frame spin (round 9: mean 357 JIT / 344 AOT, max 456/578
  `SwitchToThread` a paced frame; round 8: mean 340–387, max ≤ 986).
- **layout-audit-padding-border** (1, tooling) — compare `LayoutResult` padding and border too; with the next audit change.
- **text-open-measures-every-line (full)** (5; user: not now, 2026-10-13).
- **controls-menu-max-height-scroll** (1; not perf: no maximum height or scrolling; the Vulkan-drag half is gone since
  f3302209, round 9 expert 2), **layout-virtuallist-spacer-stale** (1; invisible; with an audit).
- **aperture-poster-as-hero**, **aperture-tmdb-size-fallback**, **junction-narrow-gitlab-glyphs**,
  **meridian-pane-choice-not-persisted**, **meridian-min-width-wraps** (apps; not perf; round 9: the Meridian two and
  junction-review-old-path still reproduce, the others' code is unchanged and was not exercised).

Closed from Waiting by the round 9 survey: brief-columns-lag-a-key (→ step 10), aperture-leads (toasts measured →
apps-toast-gone-view-only; grid Down is the row of cards scrolling in, 26–29 KB a key, which has to be made; Discover's
Down walking a shelf's pages is a UX note), junction-leads' old-side state convergence (Examined).

## Landed

Rounds 8 and 9 (round 8 is the round round 9's survey reviewed; it moves to the archive at round 10's survey). Rounds 1–7 are in
[the archive](17-performance-team-ledger-archive.md#landed-rounds-14) ([round 5](17-performance-team-ledger-archive.md#landed-round-5),
[round 6](17-performance-team-ledger-archive.md#landed-round-6), [round 7](17-performance-team-ledger-archive.md#landed-round-7)).

| Round | Commit | Candidate | Before → after |
|---|---|---|---|
| 8 | f3302209, bda4dad3 | paint-beginframe-keeps-surface-clip | Correctness, the objection to d04464f6: `SkiaRenderContext.SetCanvas(canvas, baseSaveCount = 1)` and `BeginFrame` restores to that count; the Vulkan surface records `SaveCount` after its 8-bit kept-target and F16 offscreen clips (`WrappedImage.BaseSaveCount`, `_offscreenBaseSaveCount`, reset on `Hold` and a new offscreen); GL and raster still restore to 1. 2400×1600 canvas at 1.5× clipped to 1200×800: after `BeginFrame` save count 1 → 2, device clip 2400×1600 → 1200×800, `ClipBounds` 1601×1068 → 801×535. In-process saves over a 16-step Vulkan drag: a clip 64 px too narrow differs in exactly 64 columns with the fix (142–176k channels 8-bit, 139–173k F16) and in 0 without; shown pixels identical. Suite 0/0 ×2 alternated, allocKB flat, steady frame 0 B; goldens identical. `SurfaceClipThroughBeginFrameTests` (3 cases, reverse edits fail 2 each). Skia 360, Core 5,054, Headless 1,443, VulkanProbe 38 (`HULTUI_UITESTS=1`) pass. |
| 8 | 61a82eba, e0530518 | a11y-walk-name-without-string | The in-place walk's `Compare` checks a name from content with `NameIs` (the same rules as `ResolveName`, nothing built); folds a11y-walk-unchanged-bytes. Junction Changes at 300 files, KB a step, alternated ×2 (identical), same trees, diffs and events: caret Down tree/UIA/AT-SPI 92.0/94.7/96.8 → 81.6/84.3/86.4 (−11 %), wheel a notch every 120 ms 22.8/25.4/27.4 → 16.2/18.8/20.7 (−24…−29 %), bridge off flat (78.5, 16.6); a walk of the unchanged tree 6.86 → 0.02 KB. New suite row `a11y/selection walk 1,000 composite item rows named by their texts, UIA`: JIT 40.0 → 27.4 KB (−32 %), AOT 39.9 → 27.3, time flat. Suite JIT 0 slower ×2; AOT 1 then 0 (selection-walk rows +6–10 % in render only, rows without a reader too: AOT code layout). Audit 0 STALE, 0 WALK-VALUE/EVENT, fallbacks 125. |
| 8 | f35c3df9 | a11y-disabled-menu-item (API) | Approved (question 11). **Public default member `IAccessibleItemSource.ItemIsEnabled(int) => true`**; `ContextMenuElement` answers it; `MatchItems` compares it (enabling an entry in an open menu patches, no tree); items served past 2,000 read it. A dimmed context-menu or menu-bar entry was read as enabled; now UIA IsEnabled false, AT-SPI without enabled/sensitive (not yet heard with a live reader). Menu suite rows bytes flat, times within 3 %. ACCESSIBILITY-PLAN.md, ELEMENTS.md; docfx 0 warnings. Core 5,060, Headless 1,456, Linux bridge 104, Junction a11y 131 pass; Fedora 104 + 297; 11 reverse-edit failures. |
| 8 | bc007ae8 | text-richtext-paragraph-shares-lines | A rich text paragraph shares the layout's exact `TextLine[]`, makes its `TextLineBox[]` when `Lines` is first read, `SpansOf` returns an exact array, `RichTable.Normalize` makes no closure per block (`TextLines` documented read-only). KB a step JIT, idle-gated, alternated ×2: richtext/open 5,000 blocks 3,264.6 → 2,596.2 (−20.5 %), resize 1,000 654.7 → 544.1 (−16.9 %); retained with the document open 7.94 → 7.41 MB (−6.7 %). Tests: `AParagraph_SharesItsLayoutsLines_AndMakesTheirBoxesWhenRead` (0 B a line cached, boxes 20 B a line on first read), `Normalize_OfADocumentWithNoTable_AllocatesNothingABlock` (2,160 B without). |
| 8 | a10f21b1 | text-richtext-block-layout-slim | `BlockLayout` 152 → 72 B: cell and picture fields in a side object, a text block's style from its paragraph. On top of bc007ae8: open 2,600.9 → 2,245.7 (−13.7 %), resize 544.1 → 473.8 (−12.9 %), retained 7.41 → 7.06 MB. `RichTextLayoutCostTests` width change 328 → 248 B a block. |
| 8 | 7aaae4c8 | text-richtext-block-list-no-copy | Block layouts in a `List` shifted in place when an edit changes their count: richtext/enter in 5,000 blocks 81.5 → 42.4 (−48.0 %), resize −1.6 %; an Enter test 16 → 8 B a block (the rest is `RichDocument.Blocks`' own copy, declined). Wheel through 5,000 blocks +2.7 / +0.7 % min, inside its base's spread. |
| 8 | 48ed3a35, 734a6cd7 | richtext-short-viewport-jitter | Bug fix: a caret at least as tall as the view scrolls to its top (was 18.6 px off, key after key). Step 3 overall ec5d1275 → 48ed3a35: JIT open −31.1 % (min −5.7 %), resize −28.8 %, enter −48.2 %; AOT open 3,267.2 → 2,250.2, resize 654.7 → 466.0, enter 81.8 → 42.4, times within 3 %. Suite 214 rows 0/0 ×2 and base/base. 902 in-process frames A/A = A/B, goldens pass; 147 reader trees (2.24 M character bounds) identical. Text 293, Core Rich 135, Skia 360, Headless 492, Junction MR/review 40 pass. |
| 8 | ad8e6fbd, 4960997d, 561c5659 | apps-coalesce-landing-builds | Aperture's `PosterCache` and Vesper's `ThumbnailCache` callbacks post at most one build at a time (an `Interlocked` flag cleared before the build reads the cache, so a later landing still builds); framework unchanged; REACTIVITY.md rule. Pictures served after 15–30 ms, idle-gated, alternated ×2 JIT + one AOT pair: Aperture Home 36 posters builds 41 → 15–16, most in a frame 8–10 → 4, 3,614 → 1,825–1,896 KB (−48…−50 %; AOT −51 %); grid 42 posters 46 → 14–15, 10–13 → 3, −39…−42 % (AOT −36 %); Vesper Thumbnails 44 decodes 45 → 2–3, 42–44 → 1–2, −45…−49 % (AOT −42 %). At 60–120 ms: Home 41 → 26 builds, grid 46 → 25. Same pictures at the end (55/58/97), settled frames identical. Tests `PicturesThatLandTogether_BuildTheViewOnce` (42 builds without), `ThumbnailsThatLandTogether_BuildTheViewOnce` (8 without); `PartTests.APictureLanding_BuildsNoPart` waits for every poster. Aperture 333, Vesper 879, Headless 1,459 pass. No suite scenario opens these views (no captures). |
| 8 | d366505b | aperture-grid-cursor-no-build | The library grid's cards on `KeptElements`; `MoveRingWithoutBuild` handles the grid; a move that falls back to a build moves the live ring first (else PageDown twice left two rings: the list keeps an unchanged row); a card made the frame after a build takes focus in a post-frame callback. 600 films, idle-gated JIT ×2 + AOT: Right 49.20 → 0.29 KB a key (AOT 48.70 → 0.29), Down 81.93 → 29.07 (AOT 81.28 → 28.96; rows scrolling in); headless reader Right 49.23 → 0.32, Down 85.54 → 32.69, 0 trees; builds a key 1 → 0; min −3…−8 % JIT, −5…−16 % AOT. Pixels, ring and tree (focus aside) match the base in 7 scenes; focus now lands on the cursor's card (before: nowhere). |
| 8 | 24d63eb5 | apps-aperture-discover-cursor-no-build | Discover's cursor key moves the ring on live cards: along a page 340.2 → 0.35 KB (AOT 337.5 → 0.35), builds 1 → 0, gen-0 GCs 8 → 0, min −9 % JIT, −12 % AOT. Pixels and tree (focus included) match the base. |
| 8 | fa10f889, f20b045f | aperture-home-cursor-turns-page | Bug fix, Home and Discover (same row code): the cursor turns its shelf's page. Home at 1000 px across a page 0.23 → 13.77 KB (0.125 builds a key, one a page turned; new cards fetch pictures); ring and focus on the cursor's card in every scene (cursors 5–7 had none). Discover: Right onto page 2 16.1 KB, 0.10 builds; Down (a shelf's next page) 105 KB, 0.67 builds (was 340). Also fixed: with a watchlist, moving onto a title card left two rings. Home Right/Down stays 0.26 KB. `component/shelf walk` rows within 2 %, bytes equal. `GridCursorTests`, `DiscoverCursorTests`, `ShelfPageTests` (reverse edits fail); Aperture 342 + 21 skipped, Headless 1,459 pass. REACTIVITY.md: "A key that still builds moves the live ring first". |
| 8 | 7a12f5a9 | aperture-home-build-diet (a) | A Home shelf comes back whole (by row key) when every card on its page came back live and its `HomeShelfState` matches: title, count, page, cards per page, See all, scale, and the row's place among all rows and their number (the prototype missed the place: a row moved below a hidden row kept a stale menu); every child of Home's page keyed. Headless 1440×900, 6 shelves, 36 cards, idle-gated JIT ×2 + AOT: Home build 66.02 → 26.29 KB (−60 %), elements 46 → 4, time flat (AOT 65.61 → 26.44); Home open, posters released one per build 3,368–3,433 → 2,281–2,312 KB (−32 %; AOT −34 %), six per build 1,547 → 1,371 (−11 %); page-turn walk at 1000 px 13.77 → 6.80 KB a key; Right/Down 0.26 KB, no build. |
| 8 | 2e10b549, 07e5b3f9 | aperture-home-build-diet (b) | A card's picture address is memoized by item id, kind, pixel size and sizing side, checked against the item's 3 picture tags, cleared on a new data generation or at 1,024 entries. Home build 26.29 → 11.10 KB (−58 %; −83 % for the step; AOT 26.44 → 11.21); Home open one poster a build 2,260–2,265 → 1,721–1,730 (−24 %), six a build −6.5 %; page-turn walk 6.80 → 5.72. 18 scenes (landings, page turns, Move up, Hide then Move down, Combine, rename, watched, resize, watchlist) identical to a forget-all build and to HEAD: pixels, tree, every row menu, ring, focus. `component/shelf walk` rows flat. `HomeShelfKeepTests` (4: a build with nothing changed makes exactly 4 elements), `CardArtworkUrlTests` (3); reverse edits fail; Aperture 349 + 21 skipped, Headless 1,459 pass. REACTIVITY.md two paragraphs. |
| 8 | 081921c7 | vesper-cursor-no-build (Detail) | A plain cursor key sets the mounted list's `SelectedIndex` and scrolls; the F3 partner is checked by name first (`DiffTargetPath` only when the other pane's cursor can pair, still no build); Insert builds through a new `marked` flag. Detail 17.09 → 1.74 KB a key (−90 %; AOT 16.54 → 1.64), headless reader 17.45 → 2.09, 0 trees, builds 1 → 0; with a pairing partner 17.32 → 2.78. |
| 8 | 80f31170 | element-restyle (API) | Approved (question 12, 2026-11-08). **Public `Element.Restyle(Action<Element> change)`** (not in the brief): changes a live element's look past its hover, press and focus styles (internal `InteractionStyles.Restyle`); without it a ring set directly on a once-hovered tile vanished when the pointer left (hover restores the look it first captured). `KeptElements` and REACTIVITY.md use it. Three `InteractionStyleTests`. |
| 8 | 4048e35b, 74739a89 | vesper-cursor-no-build (Brief, Thumbnails) | Both grids on `KeptElements`; a cursor key restyles the two live cells or tiles; every pane build first puts restyled cells back and has the thumbnail list remake its rows (Tab, a click or a filter build after keys that built nothing); Brief still builds when a key scrolls and on the key after a page jump. KB a key JIT: Brief Down/Up 31.88 → 1.79, Right/Left 32.20 → 1.74; Thumbnails Right/Left 27.68 → 1.22, Down/Up on screen 31.25 → 1.41, a 30-key scrolling walk 57.51 → 26.33 (the tiles scrolled in); builds 1 → 0, 0 trees with the reader, time −3…−9 %, AOT within 1 KB. 47 scenes × dark/light identical to the base and a full build (three pre-existing or harness differences: an idle hover tooltip, a 0.0005 px tree rect after End, Brief's columns lagging a key after a scroll — in the base too). Insert then Up 21.55 → 13.88; Tab, type-to-find, partner flat; suite shelf and image-grid rows within 4 %. `CursorNoBuildTests` (5), `GridCursorNoBuildTests` (12); 19 reverse edits fail; Vesper 896 + 4 skipped, Core 5,064, Headless 1,459 pass. |
| 8 | 62a67257 | apps-meridian-history-archive-part | The archived-send panel is its own `Part` (own group and signal); Down/Up/Enter set the live list's `SelectedIndex` and highlight through its ref and write the part's signal; the view rebuilds the panel only when the send, the day or `_partRevision.Peek()` changed; falls back to a view build when no panel is on screen or the list was built from other rows or another day. Headless, 300 sends, idle-gated, base 43f7e106, JIT ×2 / AOT pair: History Down/Up 52.05 → 21.64 KB (−58 %; AOT 51.44 → 21.55), with the reader 53.12 → 22.71 (−57 %); search keeping the selection 56.55 → 40.88 (−28 %; AOT −26 %), dropping it 63.66 → 47.98 (−25 %); nothing selected 47.97 flat. A key: window builds 1 → 0, plus 1 panel build; time flat (render dominates). Send, find, method pick flat. 31 scenes with the reader identical to the base and a full build (pixels, tree, focus, texts, list index/scroll/highlight); restore by `r` and the button act on the selected send. No public API. |
| 8 | 66e31aa7 | meridian-history-up-after-search (bug) | Pre-existing crash on 43f7e106: walk 30 rows down, search down to 20 rows, press Up → `ArgumentOutOfRangeException`. Up clamps the cursor to the rows. HistoryTests `UpAfterASearchThatShortenedTheRows_SelectsTheLastRow`, `AKeyBuildsTheArchiveAlone_AndASearchCharacterBuildsNoArchive`; PartTests check History keys as panel-only builds; 9 reverse edits fail; Meridian 586 + 4 skipped, Headless 1,459 pass. REACTIVITY.md, ELEMENTS.md. |
| 8 | 307ee07c | paint-meridian-history-faded-row-layers | Approved (question 13; pixels). An older send's row has no `Opacity`: its 5 texts, the method chip's tint and label and its hover tint draw at 60 % of their alpha (`Azimuth.Fade`, `MethodChip` `opacity`); app code only. Real window 1440×900 at 150 %, AOT, idle-gated, one ABAB launch a renderer, medians: layers 29.0–29.4 → 0 a frame; GL wheel 0.912 → 0.614 ms (GPU 0.954 → 0.261), Down 0.971 → 0.702 (GPU 0.902 → 0.269); Vulkan 8-bit wheel 0.898 → 0.681 (GPU 0.607 → 0.084), Down 1.063 → 0.829 (GPU 0.573 → 0.091); F16 wheel 1.063 → 0.816 (GPU 2.208 → 0.494), Down 1.105 → 0.893 (GPU 2.19 → 0.498, one segment; another process on the GPU in the second). Pixels: at rest 18–50k at 1/255, chips ≤ 8/255 (8-bit) / 6 (F16); hovered older row ≤ 5/255 outside its chip, **its chip label up to 13/255 in 55 pixels** (label over tint and faded hover fill); selected ≤ 1/255, selected and hovered ≤ 3, chips ≤ 10; hover and selection fills unchanged. Headless bytes flat JIT + AOT, CPU per step −57…−73 %; 25 History scenes' tree, focus, texts identical. `AnOlderSendFadesItsColours_NotItsRow` (3 reverse edits fail); Meridian 587 + 4 skipped, Headless 1,459 (goldens) pass. 4 guarded launches, 0 foreground takes, no `nvlddmkm` events. |
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

## Examined, not worth it

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

Round 9 questions (asked 2026-11-08; 14–16 from the survey reports as they came in, 17–19 with the merged survey;
20–22 from step 13's measurement, 2026-11-09).
Recommendations in brackets. Asked only if step 13 clears 5 %: faded images (≤ 2/255) and Vesper's faded rows and tiles.

14. **dotnet-hover-holds-removed-view** (r9-4): `Window._lastHoveredElement` and `PointerState._hoveredElement` keep a
    view a build removed alive until the pointer moves (Meridian, imported spec, pointer at rest: 471 KB of 4.9 MB at 50
    endpoints, 15.5 MB of 38.3 MB at 2,000). Should a rebuild under a still pointer re-hit-test hover, so the new element
    under it shows its hover style without a move (as browsers do), or only release the removed elements (no pixel
    change)? [Release only.] → **yes, re-hit-test** (2026-11-08).
15. **dotnet-meridian-windows-keyring-memo** (r9-4, r9-6): give Windows' `WindowsCredentialKeyring` the `KeyringMemo`
    Linux and macOS have (22–66 `CredReadW` calls a key in the Environments table, ~59 a frame dragging the response
    pane with the Request tab). A secret changed outside the app (the CLI, Credential Manager) is then seen only after a
    restart, as on Linux and macOS today. Acceptable? [Yes.] → **yes** (2026-11-08).
16. **text-textarea-mono-ascii-breaks-by-count** (r9-5): break a wrapped row of printable ASCII in a proven monospace
    face by its character count, with the proof round 4 approved for fits by count (a476866c: 95 characters, every
    ordered pair within 1/4096 of one advance). Same risk: a face that kerned ASCII past the proof would clip instead of
    wrap. Does that approval cover it? [Yes: same proof, rows identical on every width tested.] → **yes, covered**
    (2026-11-08).
17. **meridian-dispose-hidden-preview** (r9-3; with step 12): Meridian keeps its web preview controller for the app's
    life (`_preview ??=`). Disposing it when it leaves the screen frees all 45.4 MB of the process's GPU memory (90.7 →
    45.3) and the 6 `msedgewebview2` processes (283–315 MB working set, 140–198 MB private); the next "web" toggle then
    starts the browser again (first page frame 395–673 ms after `NavigateToString`, 5 runs). Dispose after a while
    hidden? [Yes, after about 60 s hidden, so a quick toggle back stays instant; step 12 covers that minute.] →
    **yes, after 60 s** (2026-11-08).
18. **f9bceb6d's window checks** (Open; r9-3): the 4K monitor at 150 % is now attached and primary. `WindowPlacementTests`
    and the Borderless `WindowStateActivationTests` (`HULTUI_UITESTS=1`) show maximized and full-screen windows over the
    desktop, so they need the desk to themselves. Run them now, or at a time you name (while you are away)? The Snap
    Layouts hover stays by hand. [While you are away.] → "later", then **"run the window tests now"** (2026-11-08):
    see Open and the log.
19. **vesper-reader-focus** (r9-7; reader behaviour, not perf, not new in round 8): Vesper's focused pane is a Box with
    no role or name, so `IsSemantic` drops it (AccessibilityTreeBuilder.cs:717; CommanderApp.Pane.cs:177) and every
    focus move tells a reader "focus on nothing"; in Brief and Thumbnails a reader hears nothing as the cursor moves.
    Fix in the app (a role and name on the pane, `AccessibleSelected` on the cursor's cell or tile set with the `Restyle`
    call: a patch, 0 trees), or in the framework (a focusable element is always semantic: trees change in every app)?
    [In the app, as a step after the perf steps, with meridian-focus-lost (Waiting).] → **in the app** (2026-11-08):
    step 15.
20. **paint-image-fades-itself** (step 13 measured, 2026-11-09; framework): a lone `ImageElement` with `Opacity` < 1
    draws its still image once at that alpha instead of in an offscreen layer (as a plain Box already does). Vesper's
    cut icons today: 26.6 (Detail) / 61 (Brief) layers a frame. Real window, AOT, 1240×820 at 100 %, ABAB+BABA: Detail
    GL frame 1.136 → 0.835 ms (−27 %), GPU 0.753 → 0.211 (−72 %); Vulkan 8-bit −16…−18 % / −83…−84 %; F16 −19…−20 % /
    −64 %; Brief GL 1.425 → 0.818 (−43 %), GPU 1.323 → 0.216 (−84 %), Vulkan −29 % / −91 %. Pixels: Detail icons 611 / 431
    / 8 of 6,976 px differ, at most 2/255 (GL / Vulkan / F16); Brief icons 0 / 0 / 360 px, at most 1/255. May faded images
    change by ≤ 2/255? [Yes: invisible, and a sixth to two fifths of those frames.]
21. **apps-vesper-faded-rows: Sync's Equal rows** (step 13): Sync with "Show equal" on fades each Equal row with
    `row.Opacity = 0.55f` (21–26 layers a frame). Fading the row's text and fills to 55 % instead (as Meridian History,
    question 13): Down/Up GL 0.824 → 0.511 ms (−38 %), GPU −77 %; Vulkan 8-bit −21 % / −82 %; wheel and hover GL −36…−37 %,
    Vulkan −18…−22 %, F16 −20…−27 % (GPU −56…−81 %). Pixels: 13.7k–15.4k glyph-edge pixels at 1/255, one pixel at 9/255
    (F16 at most 4/255). May they change? [Yes: as question 13.]
22. **apps-vesper-faded-rows: cut thumbnail tiles** (step 13): a cut tile fades as a whole (`Opacity 0.45`, 29.5–33
    layers a frame). Fading each part instead: GL −26…−27 % frame, GPU −71…−73 %; Vulkan −20 % / −83…−84 %; F16 −15…−17 %
    / −68…−69 %. But the pixels move more: 99.7 % of a cut thumbnail's pixels by a mean of 5/255 (at most 6, F16 9) because
    the tile's dark backing shows through the faded picture; a badge over its kind icon by up to 24/255; kind icons and
    the cursor tile's ring and fill up to 7/255 (19 on F16); names up to 3/255. Leaving the backing out under the picture
    should avoid most of the thumbnail shift (not built or measured). [Build that variant first and measure; if every
    part stays within ~2/255 but the badge, ask again with its numbers.]
23. **syntax-encoded-tokens-colours** (step 14, 2026-11-09): colouring from TextMateSharp's encoded tokens
    (`TokenizeLine2`) would save a further 28 % of the tokenizer's bytes on JSON, 18 % on XML, 4 % on HTML. It matches
    theme rules by parent scope, as VS Code does, so it changes colours on 2,388 of 1,752,346 corpus lines (mostly
    TypeScript/JavaScript, then PowerShell, shell, C++ and Markdown; nothing in Meridian's JSON/XML/HTML response bodies).
    May colours change to VS Code's parent-scope matching? [Show before/after examples of the changed lines first.]

Round 8 questions 11–13, round 7 questions 1–4, round 6 questions 1–9 and round 5's question 5, all answered, are in
the archive ([round 8](17-performance-team-ledger-archive.md#needs-the-user-round-8),
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
- **4315c4b on AMD, Intel and Linux (GLX/EGL):** not measured (WSL Fedora has no GL
  stack; installing one needs sudo). Depth is a minimum in WGL/GLX/EGL, so 0 cannot fail
  where 16 succeeded.

## Log

From 2026-11-03 (round 8's survey); earlier entries are in the archive ([to 2026-10-25](17-performance-team-ledger-archive.md#log-2026-10-07-to-2026-10-25),
[2026-10-26 to 2026-10-27](17-performance-team-ledger-archive.md#log-2026-10-26-to-2026-10-27),
[2026-10-27 to 2026-11-02](17-performance-team-ledger-archive.md#log-2026-10-27-to-2026-11-02),
[2026-11-02 to 2026-11-03](17-performance-team-ledger-archive.md#log-2026-11-02-to-2026-11-03)).

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
- 2026-11-08: round 8 step 4 landed (ad8e6fbd, 4960997d, 561c5659): pictures that land together share one build in
  Aperture and Vesper (−36…−51 % of the bytes from opening a view until its pictures are in; at most 1–4 builds a
  frame instead of up to 44). Guideline notes: coalescing breaks tests that count one build per landing — wait for the
  landed state; an out-of-repo AOT probe sets `PublishAot` in its own project (`-p` reaches the source generator:
  NETSDK1207); hash a tightly packed `ReadPixels` buffer with varying text (pid paths, free space) removed — Pillow's
  `getbbox()` on RGBA checks alpha only.
- 2026-11-08: round 8 step 5 landed (d366505b, 24d63eb5, fa10f889; captures f20b045f): Aperture's library grid and
  Discover cursor keys build nothing (−99 % a key; grid Down −65 %, the rest rows scrolling in), the grid's cursor card
  takes focus, and the cursor turns its shelf's page on Home and Discover (a ring and focus where none showed; a
  watchlist double ring fixed too). Guideline notes: a key that still builds moves the live ring first (a kept row
  keeps its old ring); a headless session runs async loads in its pump, so a fake library that waits on a gate hangs
  it — use a failing page. For later (UX): on Discover, Down walks a shelf's own pages, not to the next shelf.
- 2026-11-08: round 8 step 6 landed (7a12f5a9, 2e10b549; captures 07e5b3f9): Aperture Home hands back whole shelves
  whose cards all came back and memoizes picture addresses: a Home build 66.0 → 11.1 KB (−83 %), Home open −32…−49 %
  with posters one a build; 18 scenes identical. Guideline notes: hand a container back whole only after asking for
  each child every build and only when all came back live; the kept state covers what the handlers read, not only
  what is drawn (the row menu's captured list and the row's place). Measuring tip: landings at random times move the
  build count ~10 % run to run — release held pictures in fixed batches (driver mode `openb`).
- 2026-11-08: round 8 step 7 landed (081921c7, 80f31170, 4048e35b; captures 74739a89): a plain cursor key in Vesper's
  Detail, Brief and Thumbnails builds nothing (−90…−96 % a key; a scrolling Thumbnails walk −54 %), 47 scenes
  identical. The agent added a public `Element.Restyle(Action<Element>)` the brief did not ask for (a ring set directly
  on a once-hovered tile was lost when the pointer left): question 12 for the user. Guideline notes: change a live
  element's look with `Restyle`, never by setting it directly; every build resets live restyles first, not only the
  key's own build; rows a list makes in a frame read the live cursor, not their stale item. Lead (Waiting): Brief's
  columns lag one key because a programmatic scroll fires no `ScrollEvent`.
- 2026-11-08: round 8 step 8 landed (62a67257, 66e31aa7): a Meridian History key builds the archived-send panel alone,
  not the window (−58 % a key, −25…−28 % a search character), and a pre-existing crash is fixed (Up after a search
  that shortened the rows threw). Guideline notes: when a list draws its own selection and one panel beside it shows
  it too, make the panel a `Part` rebuilt when the view places it and what it shows changed, and move the list by ref;
  before skipping a build on a key, check the rows on screen are the rows the key walked; clamp a kept cursor at both
  ends after a filter shortens the rows.
- 2026-11-08: round 8 step 9 measured (no commit): Meridian History's faded rows are 29 offscreen layers a frame and
  cost 20–32 % of a real frame on GL, Vulkan 8-bit and F16 (GPU 70–86 %; ~8–10 µs CPU and 18–61 µs GPU a layer).
  Fading the colours changes pixels by 1–8/255, so it is question 13 for the user. 8 guarded launches: guard 0,
  foreground 0, no device-lost, no new `nvlddmkm` event. Guideline notes: `Opacity` on an element with children costs
  one offscreen layer per element — for list rows fade the colours, not the row; a GL timestamp query issued after
  SwapBuffers needs a `glFlush` (else it waits for the next frame's submit: ~49 ms read for ~0.9 ms). The headless
  suite cannot pin GPU or flush costs, so no suite row.
- 2026-11-08: the user answered questions 12 and 13 yes: `Element.Restyle` stays public; Meridian History fades its
  older sends' colours instead of the rows (step 9b).
- 2026-11-08: round 8 step 9b landed (307ee07c): Meridian History fades an older send's colours, not its row — 29
  layers → 0 a frame, a frame −21…−33 % on GL, Vulkan 8-bit and F16 (GPU −74…−86 %). Pixels change by 1–8/255 as
  approved, except the hovered older row's chip label, up to 13/255 in 55 pixels (told to the user). Every build step
  is done; Phase → full suite. Usage ~11 % weekly.
- 2026-11-08: the user saw before/after crops of the hovered older row (the GET chip's label 47,93,138 → 51,101,150 at
  worst; 68 pixels over 8/255) and kept step 9b as it is.
- 2026-11-08: round 8 full suite on 1b75f86e (src as eec55e8d): Windows `dotnet test HultUI.slnx -c Release` 30 projects
  0 failures (Core 5,064, Headless 1,459, Junction 1,082 in 7 m 40 s, Vesper 896, Meridian 587, Aperture 349), Fedora
  `wsl-test.sh` 22 projects 0 failures, docfx `--warningsAsErrors` 0 warnings. origin/main 12d0392e is in the branch.
  Phase → release.
- 2026-11-08: round 8 merged into main as 10acc8c0 (detached origin/main merge in this worktree, tree identical to the
  tested branch; origin/main 12d0392e was in the branch, so the push gate held). Primary checkout fast-forwarded behind
  the guard; branch pushed. CI run 42497326336 queued.
- 2026-11-08: CI run 42497326336 on 10acc8c0 green: test 18 min, test-linux 4 min, docs 2 min, meridian-keyring
  (ubuntu-24.04, macos-15) 1 min each. Round 8 landed 9 steps (17 kept changes, 2 bugs fixed in passing: a Meridian
  History crash after a search, an Aperture Home double ring) and 3 public additions the user approved
  (`IAccessibleItemSource.ItemIsEnabled`, `Element.Restyle`; pixel change in Meridian History). Round → 9, Phase →
  survey; the loop stops, and round 9 starts in a fresh context.
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
