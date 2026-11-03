# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 7
- **Phase:** build — round 7, step 5 next (steps 1–4 landed). Survey done 2026-11-02 (reports `17-reports/r7-*.md`, 631fe15f..17d4220a):
  seven experts unsatisfied, GL and Vulkan satisfied; one review objection (4325bac6, step 1). 13 build steps; round 7
  questions 1–4 were answered yes on 2026-11-02 (the user's live reader check of step 13 remains). Round 6 released: merge 0b8e684a, CI fixes
  1939afcb, CI run 41432647572 green. Question 10 (`TokenizeDiff` public) open, not blocking. Weekly usage 37 % at the
  survey's start (reset 2026-11-08 03:00 UTC; extra usage off); pause at 90 % (user, 2026-10-27). The total comparison
  of rounds 1–5 is in [`17-reports/total-r1-r5.md`](17-reports/total-r1-r5.md). Older rounds are in
  [the archive](17-performance-team-ledger-archive.md) (search it, don't read it whole). History note: rewritten
  2026-10-25 (every commit after 3353ce47 has a new ID; a pre-push hook refuses pushes built on the old history).
  To resume in a fresh context, run in this worktree: `/loop Run the next step of the performance team: follow
  docs/plans/17-performance-team.md, with state in docs/plans/17-performance-team-ledger.md. Read both once per
  session (afterwards only the ledger's Phase and the part the step needs), do the step the ledger's Phase calls
  for, record it in the ledger, and commit.` Before resuming, `git fetch` and merge main if it moved.
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`)
- **Review commits since:** round 6's survey reviewed 643eb07b..f31194ab (round 5's build); round 7's survey reviews
  round 6's build, cd3b6c8f..1939afcb (the last survey report to the round 6 release).
- **Probe code:** round 7 surveys in the same scratchpad's `r7-<area>\`; round 6 surveys in session 855f4ddd's scratchpad
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
| 1. Layout | 7 | no | none (151 shared suite rows: no counted work rose; StickyRows +3 % bytes against its twin) |
| 2. SkiaSharp: painting | 7 | no | none (43 rows differ from round 6's probe, all down; steady frames 0 B) |
| 3. GL and Vulkan | 7 | **yes** | none (steady frame 0 B and idle 0 wakes on GL, Vulkan 8-bit and F16; the walk trigger fired 13/13; 39 guarded windows, 0 foreground takes, no new `nvlddmkm` events) |
| 4. .NET runtime | 7 | no | **4325bac6:** the bridges' `ConcurrentDictionary` maps grow to 1,792 locks and `Count` takes all of them, twice per AT-SPI apply and once per UIA apply (36–56 / 18–28 µs at ~3,000 objects) → step 1 |
| 5. Text pipeline | 7 | no | none (no text row rose against round 6's landed figures) |
| 6. Reactivity and reconcile | 7 | no | none (public `Part` correct, `Place` 0 B; doubles only on one-off actions) |
| 7. Accessibility bridges | 7 | no | none (audit: 0 STALE in 3,129; 2,050 in-place refreshes, 0 mismatches; fallbacks 135, as step 15) |
| 8. App workloads | 7 | no | none — round 6 helped every walk: 33 round 6 walks 6,726 → 2,373 KB/key (−65 %), 47 walks −80 %; Meridian History Down +32 % pays for 8bcf3b14's scroll fix |

## Queued candidates

Rounds 1–6's build orders all landed but round 6's step 8, declined (rounds 5 and 6 are in [the archive](17-performance-team-ledger-archive.md#round-6-build-order)).
A build step may bundle one expert's related small candidates as separate commits. Items marked **(user)** wait for a
decision in Needs the user; the rest of their step goes ahead.

### Round 7 build order

Ranked by expected win over cost: the round 6 objection and correctness first, framework before apps. Each step names
the report lines its brief quotes (`r7-k` = `17-reports/r7-k-<area>.md`).

1. **Bridge maps, shortcut enumerator, menu slot leak** (4; r7-4 Review and Candidates 1–3, r7-6 Candidate 1, r7-8
   Candidate 2) — (a) the objection to 4325bac6: `LinuxAccessibilityBridge._objects`, `WindowsAccessibilityBridge._wrappers`
   and `_nativeByElement` made with `concurrencyLevel: 1` (one writer, the UI thread; `Count` takes 1 lock, not up to
   2,047): kept-tab switch with AT-SPI −5…−9 % expected, verify on AOT; folds atspi-kept-tab-retire. (b) `Shortcut.Matches`
   (ShortcutMap.cs:207) boxes a 32 B enumerator per shortcut tried, 168 per Vesper key: AOT Detail 26.4 → 21.1 KB/key
   (−20 %), Brief −15 %, Thumbnails −14 %, partner −13 %, Insert −12 %; also `RichTextToolbar.StateOf`/`IsStale`'s
   `foreach`; pin with a Core allocation test (0 B per `Matches`; a counter alone made the JIT stack-allocate it).
   (c) a context menu, submenu or Dropdown popup opened with a bridge keeps its `AccessibleOwnerSlots` slot
   (`Window.RemoveOverlayAt` never unmounts overlay content): 50 → 250 slots over 200 cycles, heap +56…+198 B a cycle;
   popovers and dialogs 0.
2. **Find hits, touched rows, exact line arrays** (5; r7-5 Candidates 1–3) — `TextSearch` lists a line's hits on first
   ask (TextSearch.cs:378-383): find typing in highlighted JSON 54.1 → 24.9 KB/key (−54 %), Meridian 2,000-line find
   typing 132.2 → 60.4 (−54 %); `_touchedStarts` sized up front (TextAreaElement.cs:513-524): highlighted JSON open
   259.4 → 77.3 KB (−70 %); exact `TextLine[]` and no clip layout for an ellipsis (TextPipeline.cs:845, :1552, :2985;
   `WithLastLine` :1789; `EllipsizeMeasuredLine`): styled ellipsis 374.8 → 218.7 (−42 %), multi-font −20 % (~−37 % with
   its ellipsis), text/wrap −9 %, richtext/open −6 %, retained −2.5 %; dump hash 7A18AF8DE8E05271 unchanged.
3. **paint-text-baseline-whole-device-pixel** (2 with 3; r7-2 Candidate 1) — blobs drawn at a whole device-pixel
   baseline when the matrix has no skew or perspective, so Skia's GPU text-blob cache keeps ~1 entry per draw, not one
   per fractional y a shared blob was drawn at. Explains richtext-table-mid-depth-hump (entries scanned a frame
   421 / 1,613 / 2,337 / 2,733 / 1,081 at 0–100 % depth, 21–26 ns each; snapped 253–316); long walks grow 819 → 5,815
   (table) and 170 → 1,217 (JSON TextArea, any scale) without it. Raster A/A = A/B, 0 of 1,330 frames at 1.0× and 1.5×.
   The build confirms the mechanism in Skia's source, A/Bs in-process GL and Vulkan captures (A/A first), times the
   RichTextTable sample at each depth and a TextArea wheel walk in a guarded real window, and reads the matrix once per
   line, not per draw (the prototype made ~206 P/Invokes a frame).
4. **virtuallist-item-rows-follow-their-item** (6 with 1; r7-6 Candidate 5) — `KeepUnchangedRows` hands a let-go row
   back to the index where an equal item now stands, before the template runs: Vesper type-to-find 92.7 → 64.3 KB/key
   (−31 %), rows' render −76 %, unmounts 34.8 → 8.4 a key; UIA 148.4 → 113.9. Vesper keeps zebra parity in the item
   only while zebra is on and memoizes its filtered `Rows()` (CommanderApp.cs:1620, ~7 KB a key). Scaling: a filter,
   insert or stage remakes only rows that enter the window. Junction Changes, Meridian History and the palette adopt
   once their items drop the index (they key automation ids by it).
5. **Palette: row margins + drawn selection** (1 with 7; r7-1 Candidates 1–2) — VirtualList lays a row's margin
   inside its slot, so the palette's rows drop their padding box (CommandPalette.cs:438–457): suite filter 2,000 68.9 →
   56.8 KB (−17.5 %), typing UIA −14.5 %, AT-SPI −13.4 %, Junction palette type+erase −9.5 %; then the list draws the
   selection and hover tint and rows are made unselected: Junction palette Down 32.2 → 14.9 KB (−54 %). Rows still say
   selected (question 7): the list marks the selected composite row's node. New suite row: palette Down with a twin.
   Pixel A/B.
6. **findbar-step-own-part** (6 with 8; r7-8 Candidate 3) — Core's FindBar builds in its own `Part`, so a step and its
   count build the bar, not the view (JunctionApp.FindBar.cs:30 `new(MarkViewDirty)`): Junction diff Enter 87.8 →
   ~36 KB (−59 %; 101 measure misses a step), Meridian response Enter 60.3 → ~14 (−77 %), Vesper reader Enter 69. Suite
   row: a find step beside a 3,000-row list, bar in the view vs its own part.
7. **a11y-structural-refresh-in-place** (7 with 8; r7-7 Candidate 1, r7-8 Candidate 5) — the in-place walk takes
   children added and gone, builds nodes only for new elements and hands the bridge a full diff's order; used for the
   coalesced refresh and for Children-dirty frames (Window.cs:1660; Refresh.cs:252/279; backoff Window.cs:1134).
   Junction Changes 300 Down with UIA: 1.55 trees a key (264 nodes, ~33 KB each), 156 → ~105 KB (−33 %); slow wheel
   1 tree a notch, 51 → ~20 (−60 %); fast wheel 23.4 → ~16; Aperture grid Down with a reader at 100 ms a key 0.99 trees
   a key, a11y 70 % of 111.9 KB. Rebuilding only the changed container was measured too small (−10 %, −2.4 %). Suite
   row `a11y/image grid walk Down, 600 cards, keys 100 ms apart, bridge on`. Medium–large; split if it grows.
8. **Junction** (6/8 with 5; r7-6 Candidates 3, 4, 6; r7-8 Candidates 1, 6; r7-5 junction-leads a) — History's
   detail prefetch waits for the selection to rest (JunctionApp.History.cs:74, JunctionApp.cs:1074): 60 ms held walk
   all threads 1,644 → 29.1 KB a key (−98 %), gen 2 4 → 0 per 100 keys, git 4 → 0 a key; Create MR title builds on
   empty ↔ non-empty only, description on `toolbar.IsStale` (JunctionApp.CreateMergeRequest.cs:323, 329: 256.5 /
   126.3 → ~2 KB a character); issue-list keys write `_composerRevision` (IssueAutocomplete.cs:158–162: 212 → ~34
   KB); the composer writes its revision only when a shown thing changes (JunctionApp.Changes.cs:1206: 13.5 → ~1.2);
   ELEMENTS.md: wire `SelectionChanged` through `IsStale`; a renamed file's old side read at the old path
   (JunctionApp.Highlight.cs:275, colour bug). **(question 4: yes)** Changes' caret drawn by the list (2 rows remade a
   caret key; with a reader ~1 tree a key from the remake; bridge off should fall as History's did).
9. **Vesper Brief** (6; r7-6 Candidate 2, Waiting) — marks and cut in `BriefKeptCell`, out of the Brief state
   (CommanderApp.Views.cs:505, 598): Brief Insert 480.3 → ~35 KB (−93 %; 437 cells made a mark); trace Brief
   type-to-find's 91.8 KB a key (~63 elements made though cells should be handed back).
10. **Meridian** (6/8; r7-6 Candidates 7, 9; r7-8 Candidate 7) — bug: key the assertion stale banner
    (MeridianApp.Chaining.cs:48–58: the first key after a send drops focus, the second goes nowhere); the environment
    prompt's double build; URL typing skips the editor build while Headers, Body or Auth is open, comparing the Params
    and Chain counts and the stale banner (MeridianApp.Requests.cs:338, 448–459: 64.3 → ~7 KB a character); REACTIVITY.md:
    a child that comes and goes ahead of same-type siblings needs a key. **(question 4: yes)** History's drawn
    selection (68.7 KB, 2.5 rows a key → −25…−30 %).
11. **Aperture Home** (6/8; r7-6 Candidate 8, r7-8 Candidate 4) — a Home arrow builds no view, or only the shelf it
    leaves and the one it reaches, cards handed back (ApertureApp.Cursor.cs:117, ApertureApp.Home.cs:84): 64.6–68.1 →
    ~15 KB a key (−78…−95 %; grows with each shelf). Suite row: a cursor walk over 5 shelves of 7 cards. **(question 3:
    yes)** a public kept-elements helper in Core, adopted here first.
12. **syntax-long-line-cap** (5; r7-5 Candidate 4; **(question 1: yes)**) — `LineWalk` (SyntaxHighlighter.cs:441-486)
    tokenizes a long line's first P characters from its start state: tokenizing a line is O(n²) (likely Onigwrap's
    per-search `GetByteCount` over the line); headless a 32k line takes 17 s (JavaScript), 27 s (C#), 0.96 s (JSON); in
    a window it colours 108–4,055 characters for 250 ms of worker time each prefetch.
13. **context-menu-as-menu** (7; r7-7 Needs the user; **(question 2: yes)**) — `ContextMenuElement` in context menus
    and menu-bar menus read as Menu/MenuItem (UIA MenuItem with Invoke; AT-SPI menu, menu item); a Dropdown's popup stays
    a List. No bytes.

### Waiting

Re-ranked by the next survey.

- **junction-tags-test-flake** (tests; round 6 step 9b) — `TagsViewTests.ATagsKeyRebuildsOnce_AndTheListFollowsTheSelection`
  fails in its class's runs and passes alone. Cause not found.
- **dropdown-follow-control-on-resize** (1, correctness, low) — a Dropdown list or DatePicker calendar stays at its
  opening point when a resize moves its control; anchoring changes how a press on the control behaves. Not perf.
- **junction-leads (remaining)** (6/8) — `ChangesView` makes new lists on every call while a stage is in flight
  (~130 rows a flip; memoized nodes plus step 4 cover it); `SelectConflict` and a conflict row's right-click have no
  test of their own; old-side state convergence between hunks (file walk 46 → 33 % of lines); big files' gen 2 0–1 per
  100 keys from 1.5 MB texts (not worth it alone).
- **aperture-leads (remaining)** (6/8) — Discover (needs Seerr) and toasts not measured; the library cursor never
  takes real focus though a comment says it does (rows are made after `OnAfterBuild`).
- **vesper-coalesce-markdirty** (6/8, low) — 3 app and 5 part builds per folder change, build 127 of 316 KB; two app
  builds ~30 KB, ~−10 %: at the bar. Expert 6: drop (one-off doubles; framework coalescing would change 5 apps).
- **runtime-element-layout-state phase 2** (1/4, low) — cold fields ~70 B per element: −2…−9 % a key, retained
  −1.5…−2.3 %; under both bars alone.
- **terminal-noop-scrollback-wheel** + **terminal-row-strings-by-content** (2/Terminal, low) — a no-op notch draws a
  whole terminal frame (5,440–5,682 glyphs); 10,812 B a streamed line. Only the terminal sample uses HultUI.Terminal.
- **layout-ordered-children** (1, low) — suite rows only (wheel over 5,000 rows 4,954 hit visits); apps at most 33 hit
  visits a move and 13 culls a frame.
- **Suite scenarios** (8): document-switch (Junction Files 85.4 KB a key: 62 % view build, 1.23 builds a key — the
  file's text lands and builds again — 149 measure misses a key); a held selection that starts background work needs
  all-thread bytes (a Junction test counting detail loads pins step 8's prefetch).
- **textarea-chunked-document** (5/4; the user chose option B on 2026-10-25) — parked: Meridian's 89k body typing is
  241.5 KB/key, ~178 KB of it the TextArea's new string, 1–2 % of a key's time; the user's 256k-character condition is
  unmet.
- **a11y-served-items-followups** — a live Orca check only (no desktop Orca here; optional).
- **F16 kept drag targets** (3) — deferred since round 5's `nvlddmkm` errors; never repeat blind.
- **gl-threaded-optimization-profile** (3; the user's driver setting, information only) — the walk now lands while the
  window rests; the setting would remove only the per-frame spin (mean 149–394, max ≤ 1,198 `SwitchToThread` a paced
  frame; Auto mode threaded in 13 of 13 app-like launches).
- **layout-audit-padding-border** (1, tooling) — compare `LayoutResult` padding and border too; with the next audit change.
- **layout-patch-keeps-layout-dropdown-expander** (1) — do it whenever Dropdown or Expander is touched (16/9 elements
  per Fluent rebuild; no app uses Expander).
- **text-open-measures-every-line (full)** (5; user: not now, 2026-10-13).
- **richtext-short-viewport-jitter** (5/editor; RichTextEditorElement.Render.cs:362-367, a one-line fix: prefer the
  top when `caret.Height >= viewport`), **controls-menu-max-height-scroll** (1/2; not perf),
  **layout-virtuallist-spacer-stale** (1; invisible; with an audit).
- **aperture-poster-as-hero**, **aperture-tmdb-size-fallback**, **junction-narrow-gitlab-glyphs**,
  **meridian-pane-choice-not-persisted**, **meridian-min-width-wraps** (apps; not perf).

Closed from Waiting by the round 7 survey: richtext-table-mid-depth-hump (→ step 3), atspi-kept-tab-retire (→ step 1),
vesper-key-rest-bytes (→ step 1: the shortcut enumerator), palette-per-row-box (→ step 5), context-menu-read-as-list
(→ step 13, question 2), vesper-brief-marks-and-filter-rows (→ steps 4 and 9), meridian-leads (→ step 10),
a11y-textarea-keystroke-tree (fixed by daebfa76: 0 trees in 40 keys at 89k characters), vk-startup-preload (Examined).
The junction-leads' find step (→ 6), detail load (→ 8), big-files timing (closed: timing, not a leak), composer and
issue list (→ 8); aperture-leads' Home (→ 11), Down with a reader (→ 7), `Place()` in Junction and Meridian (done).

## Landed

Round 6 (the round the next survey reviews; moves to the archive at round 8's survey). Rounds 1–5 are in
[the archive](17-performance-team-ledger-archive.md#landed-rounds-14) ([round 5](17-performance-team-ledger-archive.md#landed-round-5)).

| Round | Commit | Candidate | Before → after |
|---|---|---|---|
| 6 | 65a45f59 | text-line-strings-once | Lines and measures itemize ranges; `MeasureRange` (bit-equal to `MeasureString` of the range, which now calls it) replaces every measured substring (also `SegmentWidth`, `MinContentWidth`, `MeasureBidi`, styled/BiDi pieces); the ellipsized line is one `string.Concat`; `ApplyEllipsis(Func)` split into `TruncatedTail` + `WithLastLine`. KB/op JIT (AOT within 1 %): multi-font ellipsis 2,222.8 → 1,369.5 (−38 %), ellipsis 608.6 → 442.6 (−27 %), wrap 397.3 → 294.1 (−26 %), styled ellipsis −20 %, spans bold −18 %, bidi ellipsis −15 %, styled bidi −12 %, 600-span typing −13 %, 4 more −6…−7 %; 17 rows down, none up. Suite 0/0 JIT + AOT ×2 (one +5.8 % JIT row reads −0.2 % on rerun). 48,000-layout dump hash 7A18AF8DE8E05271 both builds, two 240,000-layout corpora match; a 0.01 % mutation changes 360 layouts. |
| 6 | 0af738d9 | richtext-insert-one-pass | `InsertInline` builds text with one `string.Concat` and spans with `RichSpans.Insert` (counting pass, exact builder, `MoveToImmutable`); same `Add` order; identity kept where the old code returned the block (undo coalescing compares by reference). 600-span typing 514.8 → 327.9 KB/key (−36 %, JIT = AOT), auto-height editor −10 %, other rich typing −4…−5 %. Suite 0/0 ×2 (one +6 % JIT row reads −2 % on rerun). 20,000 random inserts equal to the old slices; 300 extra spans 92,400 → 14,400 B. |
| 6 | 8b6c3950 | paint-blob-key-by-words-text | `ShapedRun.GlyphsFollowText` (internal; set by ShapeByWords unless `composeNow`; never letter-spaced or RTL; `ShapedRun` stays 56 B) — the run-blob map hashes (face, size, text) and equal flagged texts match without reading glyphs; a miss builds its blob from the words (`ComposeGlyphsInto`) through a context buffer made on first use (≤ 4,096 glyphs); `ComposeDigits` takes a span; `GlyphsDrawn`/`PrefixMeasurer` read `GlyphCount`. On top of step 1, KB/op (JIT = AOT within 1 %): scroll/jump 10.5 → 3.0 (−71 %), scroll wheels −41…−57 %, TextArea resize −34 %, richtext/wheel −27 %, text/wrap 294.1 → 221.4 (−25 %), long TextArea open −16 %, popover open −11 %, ellipsis −10 %; 39 rows down, none up; window/open −3.4 %. Time 0/0 JIT + AOT ×2 (text/wrap min −6.4 % JIT, −2.2 % AOT; hit-only frames within noise). Pixels: 151 rows × 7 frames A/A = A/B (512 MB glyph cache); goldens byte-identical. Not the round 3 side map nor the round 4 cached hash. |
| 6 | 55e24c16 | dialog-scrim-button-role | Approved (user, reader-visible). `InferRole` makes no Button of a clickable box with a direct Dialog-role child (`HoldsDialog`); an explicit role still wins. Meridian's in-tree Options backdrop has no node (the card is its parent's child); `Dialog.Show`'s overlay-root scrim is an unnamed UIA Pane / AT-SPI panel with no Invoke; dialog role/name, click-outside, Escape and hit-testing unchanged. Probe per tick, recording/UIA/AT-SPI: dialog 283.5/283.9/1,081 → 1.2/1.4/400 KB, in-tree modal 259.9/260.3/1,058 → 1.0/1.1/1.3, trees 40 → 0, Name events 80 → 40; palette key Name events 2 → 1 (UIA 454.2 → 441.2). Four new suite rows 260 → 0.8–1.2 KB/op, min −18…−22 % JIT, −25…−29 % AOT; suite 0 slower, 4 faster. Audit 0 STALE in 2,749, 0 mismatches. WSL 22 projects pass. |
| 6 | f9bceb6d | sdl-custom-frame-maximized-dpi | Correctness: `MaximizedFrameInset(dpi)` = `GetSystemMetricsForDpi(SM_C?FRAME + SM_CXPADDEDBORDER, GetDpiForWindow)` for NCCALCSIZE, the maximized caption margin and the Alt+Space menu. `MaximizedFrameInsetTests` match `AdjustWindowRectExForDpi` at 96–192 DPI (4/5 fail on the old metrics); `WindowPlacementTests` checks a maximized Borderless client covers the work area on every monitor. Not reproduced on a window: only the 1440p @100 % monitor was attached (system DPI 96); maximize on both monitors, the DPI hop and Snap Layouts on a lower-DPI monitor still to check. Suite 0/0. |
| 6 | dfd1a5b5 | gl-thread-walk-after-first-frame | On Windows + NVIDIA (`GL_VENDOR`) after the first GL frame: start and join a no-op thread, mark paint-dirty, present once more; the walk starts after the next swap (a thread before the first frame, glFlush or glFinish don't set it off). AOT Meridian first send's slowest frame 62.6/61.1 → 5.7/3.6 ms (bridge on 61.5 → 2.4); time to first present unchanged (Meridian 226–236, Junction 222–228, Vesper 244–249 ms); the frame ~45 ms after the first waits out the walk (56–69 ms). **Regression:** Junction's first repository frame +115–117 ms instead of +94–97 (its own loading threads used to set the walk off at ~+0.5 s with no frame waiting) → follow-up 4c. `GLDriverThreadWalkTests` (VulkanProbe `--first-thread`: 0.2/0.1 ms after a pool burst; reverted 0.2/56.6). 0 guard events over ~100 windows; no `nvlddmkm` events. Suite 0/0. |
| 6 | b788b2ef | gl-thread-walk-after-first-frame (4c) | The trigger waits 200 ms after the first frame, then for the window to rest (nothing to draw, no tween, no posted work, not minimized); one new loop deadline `IdleDeadline.DriverThreadWalk`, counted only while ahead. AOT, alternated ×2 vs bc78f875: Junction first repository frame +153/+116 → +94/+96 ms (the regression gone); Meridian first send slowest frame 2.4–2.8 ms at ~1.07 s and ~0.34 s after start; time to first present flat; the walk now runs at +242–252 ms and no frame in any app waits for it; idle 0 loop passes from ~1.1 s to 15.8 s. 500 ms settle let a 0.34 s send meet the walk (60–64 ms). Still exposed: a request within ~0.3 s of showing, or an app that never rests. Suite 0/0. 0 guard events over 50 launches; 0 `nvlddmkm`/TDR events. |
| 6 | 9484d992 | vesper-transfer-progress-rebuilds | `Repaint()` goes view-only with the transfer's percent/count in `CmdKey`; throttle on `Application.Time`. Backgrounded copy 209 → 28 KB a tick JIT (206 → 28 AOT, −86 %), 1.31 → 1.13 ms (−13…−14 %); dialog open 282 → 100 KB (−64 %); part builds per tick 11 → 0.04, footers (and free-space queries) 2 → 0. Suite rows `component/worker tick beside kept parts, every part built` vs `…, view only`: 193.0 vs 54.6 KB/op. |
| 6 | b3fc6be5 | find-step-current-match-rows (Vesper) | The current match is index + line, out of the rows' shape; `InvalidateRow` on the old and new rows; FindBar on `MarkViewDirty` (reader and Vesper's diff view). Reader Enter 330 → 69 KB (−79 %), rows made 35.5 → 2.5, parts 2 → 0; time over 400 Enters −31 % JIT / −14 % AOT. ELEMENTS.md FindBar note. |
| 6 | c55e6b7a | vesper-find-results-item-rows | Find results on item rows with `SelectedIndex` (no `IndexOf` per row): 401 hits 405 → 317 KB/key (−22 %), rows 18 → 2.3; with UIA 611 → 331; time −1…−3 % (the win grows with hits). The selected result is now told as selected. |
| 6 | ed13b156 | vesper-free-label-per-build | Free space cached per folder and listing generation: queries per Insert/type-to-find key 1 → 0 (bytes and time flat locally, ~20 µs a query; the gain is on network shares); not stale after copy, move, delete or refresh. |
| 6 | 1b3ebdf7 | app-modal-cards-dialog-role (Vesper) | Approved (user, question 5). Vesper's overlay cards are `Dialog` named by their title. With UIA, on top of the above: transfer tick 171 → 101 KB, trees 0.78 → 0; find walk 331 → 318, trees 0.10 → 0. |
| 6 | e7f840ad | dialog-focus-when-nothing-focusable | Approved (user, question 5). A dialog with nothing to focus is focusable (`TabIndex -1`) and takes the focus, not the scrim; recording, UIA and AT-SPI (in-process) tests. 10 scenes for the step: pixels and focus identical; a11y differs only as intended (two content-named Buttons → `Dialog "<title>"`, the selected find result told as selected). Suite 0/0 JIT + AOT. |
| 6 | 9f1b0995 | vesper-marks-in-row-shape | Mark, cut and measured size in a 24 B `PaneRowItem`. Insert 191.4 → 41.5 KB/key JIT (−78 %, min −20 %), AOT 189.1 → 40.5 (−79 %, −12 %); rows 30 → 2.5; Space −83 %; with a reader −79 %. Type-to-find flat (rows matched by index; +1 % with a reader). |
| 6 | 3bf54bc9 | vesper-zebra-row-index | The zebra tint takes the index from the row's item: `IndexOfPath` per full rebuild 59 → ≤ 4; zebra + type-to-find 279.7 → 203.2 KB (−27 %). |
| 6 | 59a953ff | vesper-own-selection-revisit | Re-opens 68817800 (DECISIONS.md): Detail lists draw the cursor with `.SelectionHighlight`; rows draw it while zebra is on or a drag hovers a folder in that pane. Detail Down 35.1 → 23.3 KB (−34 % JIT and AOT, min −12 %/−5 %), rows 2.3 → 0.3; F3 partner −26 %. Fix: the context-menu key's box from `CursorRowBox` (the old unscrolled layout box opened the menu below the window in a scrolled list). |
| 6 | cdd2c1ac | composite-row-kept-cards (Thumbnails) | Rows keyed by place; unchanged tiles handed back into their own row's place. Down 88.3 → 42.9 KB (−51 %), tiles 11 → 3; Right −38 %; Insert 315.7 → 56.3 (−82 %). |
| 6 | 53aea1b3 | vesper-find-result-label | Reader bug fix: results read as their row shows them ("f000.txt, 10 B", "deep.txt, in sub, 2 B"), not `Hit { … }`. Step 6 overall (AOT): Detail −34 %, Insert −81 %, Space −84 %, Thumbnails Down −52 %. 35 scenes × dark/light × zebra off/on identical (pixels, a11y, focus) but the intended find-result names; suite 0/0 ×2. |
| 6 | cf7c7d8b | meridian-body-edit-in-place | The body stays one string when its block ends the file; a body keystroke splices it; other edits leave it packed and the reparse reads it without copying; `Chain.Analyze` → `Tokenize` no longer copies the body twice per build; the Assertions tab's stale-banner check compares in place (`SerializesTo`). 89k body typing 1,894 → 237 KB/key (−87 % JIT, −88 % AOT), min −11…−12 %, gen-0 per 240 keys 26 → 3; URL typing with an 89k body 1,129 → 64 (−94 %), assertion typing 1,492 → 76 (−95 %). 16 saved files byte-identical; an old-`SetBody` copy checks 15 documents through 22 edits. |
| 6 | 8bcf3b14 | meridian-history-selection-offscreen | Bug fix: History's arrows scroll the selection into view. Bytes per key past the screen 51 → 76 KB (the on-screen cost); earlier History walk numbers were partly off-screen. |
| 6 | 44e9254a | meridian-popover-builds | Popovers open/close view-only; a pick builds once (Promote + Flash folded). Method open + pick 636 → 217 KB (−66 %), builds 3 → 2, parts 8 → 0, min −18 % JIT / −10 % AOT; environment −41 %; operator −63 %. |
| 6 | 65190e72 | app-modal-cards-dialog-role (Meridian) | Approved (user, question 5). The environment prompt's card is `Dialog "New key"`: a text change with UIA 10.5 → 0.4 KB, trees 1 → 0. No other role-less modal card. Step 7a: 27 scenes identical but the intended History scroll and dialog; suite 0/0 ×2 JIT + AOT. |
| 6 | eac5f19b | span-transparent-keeps-text-colour | Pixel fix (pre-existing bug): a span with a transparent colour no longer recolours the text under it. Merged find tints had painted text in the editor colour where a match started with a token (an unstable sort decided ties) or ran into the next one. Non-overlapping span lists draw exactly as before; goldens byte-identical. |
| 6 | 9947adaf | textarea-highlight-layer (API) | Approved (user, question 3b). **Public `TextHighlight(int Start, int Length, Color Background)` record struct (12 B; `TextSpan` is 40) and `TextAreaElement.Highlights` (`IReadOnlyList<TextHighlight>?`, fluent `.Highlights(list)`)**: same list by reference costs nothing, a new list marks paint only; each row merges spans and highlights per line by index (a highlight is a background-only span after the selection fill, under the text); nothing copied, 0 B a row, not exposed to readers. New suite rows: find typing 0.725 → 0.460 ms JIT (0.791 → 0.489 AOT), 333 → 54 KB; find Enter 0.657 → 0.391, 305 → 26 KB. ELEMENTS.md. |
| 6 | 3eec43f7 | meridian-find-typing | Spans stay one list across keys; matches go to `Highlights`, kept between builds by search key. 2,000-line response: typing 1,529 → 132 KB/key, min 3.01 → 2.25 ms JIT / 3.09 → 1.87 AOT; Enter 1,458 → 65; 19,000 lines: typing 14,009 → 730 KB, 11.6 → 3.0 ms JIT / 14.1 → 3.4 AOT, Enter 13,414 → 172; gen-2 per 240 keys 24–108 → 0; layouts per key 1 (the find bar), body line passes and shapes 0. |
| 6 | c5586b3f | junction-files-find-highlights | The same in Junction Files; line starts computed once per file with the text (fixes a selection seed in a match-less file using the previous file's starts). 6,000-line C#: typing 7,089 → 145 KB/key (7.3 → 2.7 ms JIT, 8.4 → 2.9 AOT), Enter 7,023 → 80, repaint with find open 7,140 → 186. Step 7b pixels: suite 159 scenarios A/A = A/B; Meridian/Junction scenes differ only where text now keeps its token colour (eac5f19b). Suite: no row slower in both runs (TableView 10k sort +5–6 % AOT only, its known placement effect). |
| 6 | 0493921d, 49634ea5 | richtext-table-scroll-depth | User's case (300×10 rich text table, 1200×800): nothing grows with position at 3c709d10 — wheel 0.608/0.599/0.621 ms top/middle/bottom, cells drawn 261/270/260, 136 B per step; drawing, hit-testing and the a11y range already binary-search (since e624b076). One thing scaled: every frame drew all 301 row-edge grid lines; now only the visible stretch's (313 → 38 rectangles a frame at any depth, bottom wheel −3 %; scaling bar). Four top/bottom twin suite rows. 41 frames A/A = A/B; goldens identical; suite 0/0. |
| 6 | ba7b47af | richtext-table-real-window | Sample only (no src change): `samples/HultUI.Samples.RichTextTable` opens the editor with the 300×10 table, focused, HUD on continuous redraw (F12 cycles). Real-window probe (GL, idle-gated, never took the foreground, WM_CLOSE) flat top to bottom at e624b076 AND at the tip, ~3,000 FPS (0.29–0.37 ms; HUD = Stopwatch within 1 %); Debug, the Fluent page's old hosting, and HUD-bounds + focused editor flat too. The user's 600 → 60 curve did not reproduce, so no bisect and no fix; their 1.7 ms at the top was already 5× the probe. Not copied: a focused foreground window, a real pointer (hover hit tests), a11y clients on a focused window, an uncommitted tree. The user ran the sample on both their computers on 2026-10-30: no slowdown on either, closed. |
| 6 | dcb40b7c, db606351 | palette-virtual-rows | Approved (off-screen rows leave the tree). The palette's list is a VirtualList with `RowHeights`: only rows on screen are elements, keyed by command + index, the selection in the item; the "n more" line scrolls with the rows. An open overlay's list is told in the frame it changes (with a reader listening `PrepareOverlays` realizes its rows first) and a selected state in an overlay builds that overlay's part (over 200 rows 1 tree a change → 0 + 1 part). Suite KB/op JIT (AOT within 1.5 %): filter 2,000 361.2 → 115.8 (−68 %; suite row text is cold; warm over 120 keys 335.0 → 68.5, −80 %), typing UIA 469.5 → 144.7 (−69 %); best min −9…−14 %. Apps KB/step: Junction 300 branches typing 752.8 → 54.4 (−93 %), Down/Up 755.7 → 32.6; Meridian 200 requests 437.5 → 71.6 (−84 %), Down/Up 455.7 → 22.3; reader nodes added/removed 246.5 → 24.1 and 226.1 → 37.1, 0 window trees. Suite 0 slower JIT + AOT at ±10 %; one AOT row in Waiting (input-wheel-noscroll-aot-shift). Pixels: 110 of 116 steps identical, the other 6 an open palette after a window resize (step 9b); goldens identical. Reader: the "n more" line leaves the tree with off-screen rows; **rows now say selected or not** (question 7: kept, user 2026-10-31). 8 reverse edits caught; WSL a11y + palette 168 pass, 28 skipped. |
| 6 | ed11148e, 6a1c8ab4 | composite-list-told-on-its-own | Bug fix. The tree build and the in-place refresh ask a scroll view to realize its rows as they reach it (`ScrollViewElement.RealizeForAccessibility`): a mount's rows are in that frame's tree; a change's or scroll's come with the layout refresh, in that frame or within 100 ms in a burst. Reader vs a fresh tree at rest, JIT + AOT, recording + UIA: Junction Commit opened 28 nodes missing → 0, Aperture Library +168 −168 → 0, Files keys +6 −6 → 0, Commit wheel +1 −1 → 0. Bridge off: all walks equal. Price: Aperture wheel 5.18 → 5.42 KB a notch, 8 → 9 trees in 120; composite grid still 20 per 120. `a11y/` + `virtual/` rows nothing slower. Audit 0 STALE in 2,804, 0 walk mismatches, fallbacks 75 → 85 (mostly the new tests). Pixels 161 frames × 3 views A/A 0, A/B 0. 6a1c8ab4 repairs `TheOlderEntryPointsStillReachThePanels`, broken by db606351. |
| 6 | f76eb195 | overlay-layout-on-window-resize | Bug fix. `OnWindowResized` marks each open overlay for layout (the modal resize path goes through it too); an overlay placed at a point is kept on screen from where it was asked to be, so it goes back when the window grows. On the resize frame: Dialog scrim 800×600 → 1000×700, palette card at 320 tall 440 → 230 px; dialog, palette and menu pixels equal a fresh open at that size. `layout/resize` flat (JIT 1.675 → 1.677, AOT 2.569 → 2.586 ms), bytes equal, other resize rows within 3 %. Full suite for the step: 163 rows, bytes equal, largest rise +3.8 % in code not touched. Windows and Fedora tests green; 21 reverse-edit failures across both fixes. |
| 6 | b22767a8 | suite row (step 10) | `a11y/type in a rich text editor inside a VirtualList row, bridge on`. With change 92084157: JIT 0.301 → 0.256 ms, 83.4 → 2.4 KB, trees 1 → 0; AOT 0.294 → 0.245, 83.1 → 2.4. |
| 6 | 454fc82f | junction-changes-composer | The composer is a part with its own signal, placed by the view's build. KB/char none / UIA: 1 file 76.8 → 13.6 / 77.4 → 14.1, 200 files 1,320 → 13.6 / 14.1 (−99 %, min −23 / −21 % JIT, −27 / −28 % AOT); window builds 1 → 0. |
| 6 | 92084157 | junction-review-composer-row-kept | Typing keeps the row and builds nothing: new `RichTextToolbar.IsStale` (a build only when a button would change) and `Window` asks for no tree for an element in a row of an item-publishing list (it has no node; trees stayed at 1 without it). Typing JIT 150.4 → 2.0 / 206.7 → 2.0 KB (min −16 / −20 %), AOT 148.9 → 2.1 / 204.8 → 2.1 (−20 / −20 %); caret 148.9 → 0.4 / 204.8 → 0.4; rows and trees a key 1 → 0. Focus, caret and undo survive; the editor's height is fixed. Reader gap found: question 8. |
| 6 | a35f1797 | junction-branches-selection-highlight | The list draws the pick's tint on a picked caret row and the caret's on any other; `Tinted` in the item covers a pick away from the caret. KB/key 92.8 → 46.5 / 93.5 → 47.0 (−50 %), min −2 / −6 % JIT, −5 / −7 % AOT; rows a key 2.13 → 0.27. |
| 6 | 9d920925 | junction-diff-find-shape | The current match is out of the rows' shape, `InvalidateRow` on the old and new rows, FindBar on `MarkViewDirty`. Enter 467.8 → 107.5 / 468.8 → 108.4 KB (−77 %), min −5 / −7 % JIT, −8 / −9 % AOT; rows 36 → 3. Step 10a: 96 scenes × 2 themes identical (pixels, a11y, focus; A/A identical); full suite 0 slower, 1 faster JIT + AOT; Junction.Tests 1,006 pass; 24 reverse edits caught; WSL not run (→ 10b). |
| 6 | d8e28f93 | junction-history-list-drawn-selection (API) | Pixels approved. **Public additions `VirtualListElement.HoverHighlight` (`.HoverHighlight(fill, radius)`) and `SelectionOverlay` (`.SelectionOverlay(e)`)**, off by default, next to `SelectionHighlight`: the list draws the hover tint under the pointer's row unless selected, and holds one element over the selected row's end, in a box the pointer goes through; a move repositions it and lays nothing out. History's rows are made unselected with no hover; the affordance is a kept part. KB/key JIT (AOT within 1 KB), none / UIA: Down/Up 39.3 → 16.1 / 39.9 → 16.5 (−59 %), cycles −3…−5 %; Ctrl+arrows −59…−60 %, 1.014 → 0.966 ms; rows a key 2.07 → 0.07. 184 scenes: a11y and focus identical; pixels only on the caret's row (a subject runs to the words and fades over 24 px) and one hover change (a row the caret passes while the pointer stays keeps its tint; before it lost it). |
| 6 | 1daa836d | app-modal-cards-dialog-role (Junction) | Approved (question 5). 12 cards in the 9 backdrops are `Dialog`s named by their title (the merge-request panel for its page). A character in a card's field over 1,000 rows with UIA 461.6 → 335.5 KB/key (−27 %), trees 1.33 → 0.05, min −6…−7 %; bridge off equal. Pixels and focus identical; 34 Buttons named after a card's text become 17 Dialogs named by a title. |
| 6 | d960db3b | popover-menu-role | Approved (question 6). **New `AccessibilityRole.Menu`** (appended; UIA Menu, AT-SPI menu 33). A clickable box with a Menu child is no Button (`HoldsDialog` is now `HoldsPopup`); an entry directly in a Menu is selected under the pointer, patched into the applied tree (`A11yPatchKind.Selected`, 0 trees a move); a MenuItem without a menu of its own gets Invoke on UIA, every MenuItem the click action on AT-SPI. Not in `Popover`: neither app uses it, and its keyboard and press rules differ. Junction 16 Buttons → 4 Menus + 8 items, Meridian 48 → 6 + 36. Bytes flat. Pixels and focus identical over 194 scenes. |
| 6 | b7f31f16, 358b5ba2 | junction-issue-list-esc | Bug fix: Esc closes the composer's issue list (-1 is no caret, not the end of the text); one scene differs, the bug's own. 358b5ba2: an AT-SPI test for a menu entry's selection, written by the orchestrator after the step's agent was stopped; reverse edit caught. Step 10b's full suite: 164 rows, JIT and AOT 0 slower, 0 faster, no row moved 2 % in bytes; three AOT `paint/tiles` rows read +5…+7 % once and +0.3…+1.7 % on 3 alternated pairs alone. WSL 22 projects pass. |
| 6 | 2be205e5, 3a8e6c9e | virtuallist-sticky-rows (API) | Approved (question 3a). **Public `.StickyRows(Func<T,int,bool>)`**: the list realizes and holds the last sticky row at or above the first visible one until the next pushes it off; paint, hit test, a11y and highlights follow it. Pixel test against a scroll view of `Sticky()` headers at 11 offsets, push-off and fractional included: identical. Suite rows with twins: sticky vs none AOT wheel 0.198 vs 0.189 ms, 20.3 vs 19.9 KB; walk 0.203 vs 0.195 ms, 12.3 vs 11.8 KB; twins flat. |
| 6 | 90fb4df7, 02f5d183, 49bd7342 | junction-changes-virtual | Changes is a composite VirtualList of tagged rows with `RowHeights` and `StickyRows`; selection and folder moves use `RerenderView`; headers keyed so a new count keeps button focus. KB/key AOT (JIT within 1 %), 300 files: Down 3,078 → 317 (−90 %), median 4.80 → 3.60 ms (−25 %); with UIA 3,398 → 369 (−89 %), 5.39 → 3.84 (−29 %); 60 files 978 → 295 (−70 %); Stage 3,308 → 920 KB, min −30 %; reload landing 10.3 → 2.0 MB; elements 3,160 → 571; chrome builds a key 3 → 0. **Wheel at 300 files costs more, the virtualizing trade:** bytes 0.1 → 35 KB (UIA 27 → 55), time +1…+5 %, with UIA a tree every third notch (→ 11b). Suite 168 rows 0/0 JIT + AOT. 386 scenes × 2 themes: 376 identical, 10 Tab scenes have two fewer Tab stops before the composer. Reader: off-screen rows leave the tree (50,078 off-screen nodes gone, none added); Tab no longer reaches off-screen buttons (question 9: kept, user 2026-11-01). Core 5,022, Headless 1,243, Junction 1,041, Fedora a11y 98 pass; 47 reverse edits caught. |
| 6 | a4f798dd, daebfa76, 17504b64, 47a7b51a, 82312fa1, d546eb6e | junction-review-composer-reader | Approved (question 8). **The row that holds the focus opens:** internal, no API; its elements hang under its item's node while it holds the focus, at most one row, every other row stays its item. `RowsAreComposite` measured and declined (DECISIONS: on-screen rows only, each a button; caret key 134.6 KB and 0.34 trees vs 120.4 and 0; wheel 25.2 KB and 0.33 trees vs 13.2 and 0). Reader: editor with role and name, focus, text and caret readable, text-changed told per key, 0 trees; replies the same. Any focusable in an item row of any list is now told (before, silence), one tree per Tab in the review's selectable diff text. Bridge off: every walk same bytes, time within 2 %, 2 KB a key. UIA JIT / AOT: key 2.0 → 2.7 / 2.1 → 2.7 KB, time +3–4 %; caret 0.4 → 1.0 KB; diff walk and wheel flat; open and close 950 → 1,011 KB a step, 0.5 → 1 trees, slowest step's med +24–39 % (closing builds at once). daebfa76: a keystroke in text made on request builds no tree: `a11y/type in rich text beside 300 rows` 86.1 → 8.3 KB, −16…−26 %. Fix on the way: item boxes in mixed-height lists are where rows are drawn (205 offscreen flags corrected). 22 of 23 mutants caught; 0 STALE in 2,920 audits; 392 scenes pixels and focus identical; suite 0 slower, 2 faster JIT + AOT; Fedora passes. Check: Changes' wheel trees are 20 per 120 notches at a notch a frame, like the grid; the 47a7b51a test pins a held header's focus. |
| 6 | 985adef7 | apps-junction-highlight-prefetch | `TokenizeDiff` (internal, HultUI.Syntax, question 10) stops each side at its last shown line, walks unshown lines for state only, and walks the lines both versions start with once. One worker, one waiting slot; a newer selection cancels; a prefetch never takes the worker from the diff on screen; 120 ms rest for the log's prefetch. KB a key all threads, JIT / AOT: History 60 ms 9,794 → 1,636 / 9,839 → 1,637 (−83 %), 500 ms −61 %; Commit files 100 ms 3,853 → 641 (−83 %), 500 ms −21 %; generated repo 17,580 → 3,564 (−80 %). Workers 673 → 47 Mc; GC pause 0.55 → 0.11 ms; GCs per 100 keys 19/4/0 → 7/4/0.8. UI thread up to +4 KB, cycles within 3 %. Colour lands earlier on Tab (336 → 150 ms) and a file key at rest (max 1,212 → 202), later on a held walk's last key (p90 +30–60 ms). Spans byte-identical over 2,081 files, 15 languages, 934,306 spans. |
| 6 | 323a3073 | junction-commit-diff-flatten | A diff's rows are made as they're asked for; row models hold no colours, so a landing flattens nothing; find, copy, wrap and review make their rows when needed. Commit file key on the UI thread (150dcf7f): 33–100 ms 551–555 → 214–218 KB (−61 %, UIA −56 %), 500 ms 1,248 → 236 (−81 %, UIA −76 %); 8,000-line files 3,240 → 569 (−83 %, UIA −71 %), UI min −31 / −39 %; row records a key 10–35. Step 12: 421 scenes identical (pixels, a11y, focus); suite 171 rows 0/0 JIT + AOT (AOT baseline = step 11b's publish, same source, the baseline tree's publish failed); Junction 1,075, Syntax 78, Meridian 465 on the final code, Vesper 876 and Headless 1,266 before a last Junction-only edit; 28 reverse edits caught. |
| 6 | dca932e1 | suite row (image grid) | `virtual/image grid walk Right` and `Down, 600 cards`, with twins `…, cards handed back` from 31997de7: Right 48 → 22 KB, Down 114 → 52 KB. |
| 6 | 1f4bbda8 | aperture-resize-rebuild | A resize builds only when a build would read something new off the window; size reads sit behind one noting method. 4 px step 71.5 → 2.3 KB (−97 %), builds 1 → 0, UIA 71.9 → 2.6; Home 130.0 → 2.3; item page 77.3 → 14.1; time −1…−3 % (raster dominates headless). `WindowFitTests` walks 16 views against a forced build. |
| 6 | 31997de7 | composite-row-kept-cards (Aperture) | A grid row made again keeps the cards that didn't change. Right 115.0 → 87.3 KB (−24 %; UIA −32 %, trees 0.34 → 0), Down 181.9 → 114.1 (−37 %), across a column count 341.5 → 118.9 (−65 %). Fix on the way: after a sort, Right focused a card out of the tree and the next key was lost. A card Tab reached now keeps the keyboard. |
| 6 | 5765d27b, 3a8e621f, e4c2607d, df0bf30b, a267faab | public-part-in-core (API) | Approved (question 3c). **Public `Part(group, build, own, afterBuild)`, `Place(stale)`, `PartGroup.Invalidate()`** in Core (ELEMENTS.md §27). Aperture's title bar and rail on it: Right 87.3 → 49.2 KB (−44 %), Down 114.1 → 75.9 (−33 %), across a column count 118.9 → 80.8 (−32 %), no part built on any. Whole step: Right −57 %, Down −58 %, resize −97 %, across a column count −76 %. Vesper's, Meridian's and Junction's chrome and composer moved onto it with no behaviour change (scenes 35 / 27 / 421); Junction's `KeptPart` stays (a handed-back element the list patches). Suite 173 rows 0/0 JIT + AOT. Core 5,032, Headless 1,266, Aperture 329, Vesper 876, Meridian 465; Junction 1,072 of 1,075 then 31 rerun after the fix: the whole project goes in the full-suite step. Scene limits: Aperture and Meridian have one theme; the A/A control ran for Aperture only. |
| 6 | afa7116d, 61b538c3 | text-lazy-line-runs | A line on the plain or styled path keeps width, metrics and range and makes its runs on the first `TextLine.Runs` read; `TextLine` stays 56 B (one field: runs, a shared `LineSource`, or the line a copy shares with); widths from `MeasureRuns`, bit-equal to shaping; an ellipsized line measured from stack characters; caches charge a line as if its runs were made; `DrawParagraph` tests the clip first. Declined (DECISIONS): BiDi lines, and a styled paragraph's first layout with over 32 spans (600-span typing 2.33 → 2.59 ms lazy). KB/op JIT (AOT within 1 KB): text/wrap 221 → 67 (−70 %), multi-font ellipsis 1,337 → 414 (−69 %), ellipsis −57 %, styled wrap −48 %, shrink 300 overfull rows −38 %, spans bold −35 %, styled ellipsis −34 %, richtext/resize −22 %, richtext/open 4,331 → 3,480 (−20 %); mins multi-font −5/−7 %, richtext/open −6 % AOT. Retained 5,000-block document open 10.51 → 8.12 MB (−23 %), closed 4.03 → 3.43. **Moved to first draw:** wheel rows allocate more (richtext/wheel 6.3 → 8.0 KB, table 0.1 → 1.1); sessions drawing every line: time −3…+2.6 %, bytes −51…+0.9 %. Suite AOT 0/0; JIT two a11y rows +12–13 % read −1.4/−0.3 % on 3 pairs; a11y/click checkbox UIA min +5 % on a two-moded baseline, median flat. 48,000-layout hash unchanged both paths, 240,000-layout corpora match, mutations change 1,427–8,243 layouts; 175 rows × 7 frames identical; goldens identical. Text 281, Skia 337, Headless 1,266, Core 5,032, Markdown 62, Meridian 465, Vesper 876, Junction 1,075, Fedora pass; 21 reverse edits; app walks same bytes. |
| 6 | 799bcb39, 4325bac6, de3f8dbe, 2353944f | a11y-atspi-snapshot-per-diff + apps-scenario-atspi-rows | 15 AT-SPI suite rows (`AtSpiRig`: the bridge in process, signals counted; `InternalsVisibleTo` for PerfSuite). The bridge keeps its objects by element in a concurrent map; each apply walks the tree once, refreshing what changed, making objects only for new nodes, keeping a parent's child list while its children's ids are unchanged; a gone node's object answers UnknownObject and drops its node; each property written whole, arrays replaced, never written over. No API change. KB a step (JIT = AOT): dialog tick 493.8 → 1.1, palette key 967 → 139, filtered list 1,154 → 289, added row 1,085 → 289, kept-tab switch 595 → 441, 1,000 rows made 7,825 → 7,310; apply alone dialog 796.8 → 0.2. Time: `--compare` 0/0 of 190 rows JIT ×2 and AOT; large-tree mins palette −6…−8 %, filter −9…−12 %, added row −5…−8 %; **kept-tab switch +2.5–2.8 % AOT in three sets** (kept on balance; Waiting atspi-kept-tab-retire). Signals: 588,974 suite lines byte-identical; 1,300 test labels identical, 993 of 1,054 app labels (the 61 vary between runs of one build); live pyatspi 39/39. Object audit: the same 34 pre-existing mismatches. Staleness audit 0 STALE in 3,113, 0 walk mismatches over 2,014. Retained over 800 cycles: palette flat (was +8.1 KB); dialog +470 B a cycle in both builds, not the bridge. Bridge 103 Windows and Fedora, Headless a11y 213 Fedora; 5 reverse edits. |
| 7 | 1986bed9 | a11y-bridge-map-one-lock | The objection to 4325bac6. `_objects` (AT-SPI) and `_wrappers` (UIA) made with `concurrencyLevel: 1, capacity: 31` (one writer, the UI thread; reads lock-free); `_nativeByElement` (two writers) and `AtSpiServedItems._made` (bus thread) unchanged. Kept-tab switch with AT-SPI, best min of 3 alternated pairs: AOT 0.630 → 0.605 ms (−4.0 %; a11y phase 0.280 → 0.246), JIT 0.508 → 0.475 (−6.4 %): round 6's +2.5–2.8 % won back; AT-SPI add a row and filter −3…−5 %; UIA rows within ±3.5 %. Tests read the lock array: 1 (1,792 under the reverse edit). |
| 7 | 2aa55491 | shortcut-matches-no-enumerator | Index loops in `Shortcut.Matches` and `RichTextToolbar.StateOf`; `RichDocument.TableAt` answers non-table blocks before boxing its `ImmutableArray` into `RichTable.At` (264 B per `IsStale` with the default bar). AOT KB/key: Vesper Detail 26.4 → 21.2 (−20 %), Brief 34.0 → 28.7 (−16 %), Thumbnails −13 %, partner −13 %, Insert −11 %; Junction review typing 4.1 → 3.7 (−10 %), History −0.6 KB; CPU per key within noise. Allocation tests fail under reverse edits (240 / 32 / 264 B a call) on the JIT test build with TieredPGO off. |
| 7 | 5000753a | a11y-overlay-owner-slot-leak | Leak closed: `RemoveOverlayAt` calls `AccessibleOwnerSlots.ReleaseTree(content)` under one lock. Not an unmount: `Show` never mounts, so unmounting would stop the component hosts of content shown again. 2,000 menu cycles with AT-SPI: slots 2,000 → 0, heap +46…+198 B a cycle → 0 (flat at 6,719 KB); recording bridge 2,000 → 0; popover, dialog and no bridge 0. Step 1: event streams before/after with ids masked equal (15,978 / 1,334 / 5,146 lines); suite `--compare` 0 slower, 0 faster JIT + AOT ×2 (AOT TableView 10k sort +4.7–6.5 %: placement, JIT flat, no step code in the row); captures 56ca60f1; Core 5,033, Headless 1,269, Linux bridge 104 (Windows and Fedora), Fedora Headless a11y 260, Vesper 876 pass. |
| 7 | 45628641 | text-find-lazy-line-hits | A find lists a line's hits when a row first asks: a binary search into the sorted hits, kept until the next rerun (`HitsOn` keeps its type). KB/key JIT / AOT: text/find typing 54.0 → 24.9 / 53.4 → 24.2 (−54 %); Meridian response find 132.1 → 60.4 / 131.2 → 59.6 (−54 %); Junction diff find typing 853.9 → 416.7 / 849.7 → 412.5 (−51 %, min −4.7 % JIT); Vesper reader find typing 646.8 → 297.7 / 641.2 → 292.1 (−54 %). Find Enter flat at 25.5; wheels with find open and Junction find Enter within 0.1 KB and 2 % time. |
| 7 | d748b316 | textarea-touched-rows-sized | A replaced text's touched rows sized once, to a lower bound that can't overshoot (one row per hard line touched; an edit inside a big text allocates under 1 KB). text/open highlighted JSON 268.4 → 86.3 KB (−68 %, JIT and AOT), min −3 %; Meridian resending a 19,000-line JSON 1,792.7 → 1,139.5 KB a send JIT, 1,823.0 → 1,169.8 AOT (−36 %). |
| 7 | 73819ebd | text-exact-line-arrays | Each layout keeps an exact `TextLine[]` filled from a kept scratch list taken while filling (nested calls get their own); a truncated layout replaces its last line, no second layout (styled, BiDi and `EllipsizeMeasuredLine`). KB/op JIT (AOT within 1 KB): multi-font ellipsis 413.7 → 257.7 (−38 %), styled ellipsis 636.6 → 481.0 (−24 %), bidi and styled bidi ellipsis −9 %, wrap −9 %, styled wrap −7 %, richtext/open 3,483 → 3,263 (−6 %), resize −6 %; no row up; richtext/open JIT min +0.8…+5.4 % over three sets, medians −1.0…+2.2 %, AOT +0.9 % (noise). Retained 5,000-block document 8.12 → 7.93 MB open, 3.43 → 3.38 closed. Step 2: 48,000-layout hash 7A18AF8DE8E05271 and both 240,000-layout corpora F09E9038726E4BD6 on both builds, 0 differ; mutations change 83–41,034 layouts on every path. Suite 0/0 JIT + AOT (captures 7ac787f7). Text 293 (Windows and Fedora), Skia 337, Core 5,037, Headless 1,269, Meridian 465, Vesper 876, Junction 1,075 pass. |
| 7 | 18fe58a0 | paint-text-baseline-whole-device-pixel | A blob whose glyphs all sit on the baseline is drawn with its baseline on the whole device pixel its glyphs land on anyway, when the matrix has no skew or perspective and the text is under 128 device px; the matrix read once per push/pop that moves it; the on-baseline flag in the sign bit of the kept blob's glyph count (a separate bool made map entries +8 B: scroll/jump 3.0 → 3.7 KB). Mechanism confirmed in Skia m119 (`TextBlob::Key::Make` keys a direct-mask blob by the fractional part of its mapped origin; `findBlobIndex` scans linearly; direct mask below 162 device px). Real window 1200×800 at 1.5×, AOT, idle-gated, ABBA, table render ms after a 200-notch walk, 0/25/50/75/100 %: GL 0.156/0.251/0.256/0.257/0.271 → 0.142/0.147/0.154/0.162/0.149, Vulkan 0.151/0.240/0.255/0.270/0.287 → 0.158/0.162/0.166/0.167/0.172; frame at 75 % GL 0.437 → 0.338, Vulkan 0.522 → 0.396; JSON TextArea render no longer grows over 200 notches (GL 0.047 → 0.055 before, flat ~0.048 after). Pixels: in-process GPU readback GL and Vulkan at 1.0×/1.5×, 38 states, A/A 0 of 152, A/B 0 of 152; Vulkan PPMs byte-identical; raster identical at 1/1.25/1.5/2×; goldens pass. Suite 0/0 JIT + AOT, bytes unchanged (captures 3405d093). 50 guarded launches, 0 foreground takes, no `nvlddmkm`/TDR; the F16 PPM encode at 1.5× held a frame over 1 s twice (the diagnostic capture; the watchdog stopped those runs). Skia 357 (Windows and Fedora), Core 5,037, Headless 1,269 / Fedora 1,146 pass; `BaselineOnPixelTests` caught three reverse edits. DECISIONS.md (Rendering). |
| 7 | dc469aa0, ca4a0079 | virtuallist-item-rows-follow-their-item | Suite rows (filter at the top of a 1,000-commit item-row list; insert above while scrolled 200 rows down; twins whose items carry their place). `KeepUnchangedRows` sends a row whose index now holds another item to `_moving`; `RealizeRow` hands it to the index where an equal item stands in the same selected state, before the template runs. Moved rows keep their mount (focus stays) and take their new place's height and sticky state; still made again: a hovered or pressed row (unless it holds the focus), rows moved in a build that called `InvalidateRow`, `InvalidateRows`, a theme change. The patch sets `SelectedIndex` without dropping rows by old index; `RealizeForAccessibility` realizes a moved focused row. No API change. Filter 19 → 3 rows, 76.6 → 14.8 KB, min −6 % JIT / −9 % AOT; insert 20 → 1 rows, 80.5 → 7.1 KB, −7 % / −12 %; twins flat. Junction's Files, Commit and Tags lists get moves on their own. ELEMENTS.md "Rows follow their item", REACTIVITY.md "Keep the index out of a list's item". |
| 7 | ebff97b2, df7f3a3f | vesper-rows-follow-their-item | `PaneRowItem` carries `Odd` (zebra parity, only while zebra is on), not the index; with a filter on, `Rows()` keeps the filtered list per listing, side and filter text. Vesper type-to-find, 400 files, AOT: Detail 195.7 → 88.5 KB a key (−55 %; JIT 197.8 → 89.6, median −19 %), min/median 1.163/1.357 → 1.077/1.225 ms, rows made 26.5 → 9.3, elements added/removed 133.7 → 45.3; with UIA 334.9 → 227.7; Brief 195.0 → 180.6 (memo only). Down and Insert flat. Step 4: suite 0 slower JIT + AOT, 1 faster AOT (captures 82c40437); event streams UIA and AT-SPI identical, recording identical but the intended composite-row case (54 removed + 54 added → 48 bounds changes); audit 0 STALE in 3,123, fallbacks 128; 43 Vesper scenes pixels, a11y and focus identical. Core 5,037, Headless 1,277, Vesper 876, Meridian 465, Aperture 329, Linux bridge 104, Junction 1,075, Fedora Headless a11y + VirtualList 285 pass; 6 reverse edits caught. |

## Examined, not worth it

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

Round 7 questions (asked 2026-11-02). Recommendations in brackets.

1. **syntax-long-line-cap** (step 12): may a line longer than P characters be coloured only on its first P (P about
   2,000; larger for JSON, which tokenizes ~17× faster)? Today a 32k-character line gets 108–4,055 coloured characters
   for 250 ms of worker time on every prefetch that reaches it, and 15–27 s in headless tests. Lines over ~4k would get
   more colour, lines between P and ~3k less. [Yes. VS Code goes further: no colour at all over 20,000 characters.] → **yes, cap it** (2026-11-02).
2. **context-menu-read-as-list** (step 13): context menus and menu-bar menus are read as a List of ListItems, while
   Junction's and Meridian's popover menus are now Menu and MenuItem. Read them as Menu/MenuItem too (UIA MenuItem with
   Invoke; AT-SPI menu, menu item), and keep a Dropdown's popup a List, as a combo box's is? Readers would hear "menu"
   where they hear "list" today. [Yes. A live NVDA or Narrator check is yours: readers follow menus by focus, and these
   items move by selection.] → **yes, read them as menus** (2026-11-02; the live reader check is the user's).
3. **A public kept-elements helper in Core** (step 11), as `Part` was for parts: Vesper Brief, Vesper Thumbnails and
   Aperture's grid each hand-roll "hand back the unchanged cells" (8 rules in REACTIVITY.md), and Brief's marks slipped
   (437 cells per mark). New public API: key, per-cell state, from the build just before only, live ref, row-place
   check. [Yes, with Aperture Home as its first user; the others adopt it later.] → **yes, add it** (2026-11-02).
4. **List-drawn selection pixels in Meridian History and Junction Changes** (steps 8 and 10), as approved for Junction
   History in round 6: the list draws the selection (and hover tint), so a key remakes no rows. Pixels may change only
   where the selection is drawn. Meridian History −25…−30 % a key; Junction Changes' caret 2 rows a key → 0, and with a
   reader ~1 tree a key fewer. [Yes, as Junction History.] → **yes, both** (2026-11-02).

Round 6 questions 1–9 and round 5's question 5, all answered, are in [the archive](17-performance-team-ledger-archive.md#needs-the-user-round-6).
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

From 2026-10-27 (round 6's survey); earlier entries are in the archive ([to 2026-10-25](17-performance-team-ledger-archive.md#log-2026-10-07-to-2026-10-25),
[2026-10-26 to 2026-10-27](17-performance-team-ledger-archive.md#log-2026-10-26-to-2026-10-27)).

- 2026-10-27: round 6 survey started in a fresh context; round 5's answered questions 1–4 moved to the archive. The
  eight experts run in their own worktrees, reviewing 643eb07b..f31194ab.
- 2026-10-27: round 6 survey done (reports 45d29157..cd3b6c8f, `17-reports/r6-*`). All eight unsatisfied, no review
  objections; round 5 helped every app walk (−59 % summed over 33). Fifteen build steps ranked; four questions for the
  user. Found two bugs: maximized custom frames inset at the system DPI (step 4), Meridian History never scrolls to its
  selection (step 7). Survey agents: 300–400k tokens each. Phase → build.
- 2026-10-27: the user answered round 6 questions 1–4: all yes (scrim stops being a Button; off-screen palette rows
  leave the tree; StickyRows, a TextArea highlight layer and a public `Part` approved; History pixels may change).
- 2026-10-27: round 6 step 1 landed (65a45f59, 0af738d9): measures take ranges, not substrings, and ellipsis makes no
  closure (−13…−38 % on 17 text rows, bit-identical over 48,000 layouts); rich text inserts in one pass (600-span typing
  −36 %). Guideline notes: a lambda capturing parameters costs its closure on every call, used or not — split test and
  action; measure a range, never a substring; build `ImmutableArray`s at exact size with a counting pass and
  `MoveToImmutable` (a doubling builder cost 6.4× the spans); editor rewrites must keep object identity where undo
  compares by reference.
- 2026-10-27: round 6 step 2 landed (8b6c3950): a run of words finds its text blob by its text, so scrolls and resizes stop
  composing glyph arrays to look up blobs they already have (scroll/jump −71 %, wheels −41…−57 %, text/wrap −25 % on
  top of step 1; pixels and goldens identical). Guideline notes: a cache keyed by something built lazily must not build
  it to look up — key by what it follows from; readers that copy a lazy run's glyphs use `ComposeGlyphsInto`, counters
  `GlyphCount`. Flake seen: Junction `DiffFindTests.EnterWalksTheMatchesAndWraps` (one extra build count) fails 2 of 5
  runs on the base sources too — check it in the full-suite step.
- 2026-10-27: round 6 step 3 landed (55e24c16): a dialog's backdrop is no Button, so a text change inside a dialog patches
  instead of building the whole tree (260–284 KB → ~1 KB per change, trees 40 → 0; palette Name events 2 → 1). Asked the
  user question 5 (app modal cards without a role). Guideline notes: give a modal card `.AccessibleRole(Dialog)` and a
  name and put it directly in its clickable backdrop; a Button or Tab with no name of its own around other content
  blocks name patches beneath it — name it or keep it small.
- 2026-10-27: the user answered question 5 yes: the apps' modal cards get the Dialog role and a name (steps 5, 7, 10);
  a dialog with nothing focusable focuses the dialog (framework half with step 5).
- 2026-10-27: round 6 step 4 landed (f9bceb6d, dfd1a5b5): maximized custom frames inset at the window's DPI (unit-tested
  against `AdjustWindowRectExForDpi`; the 4K monitor wasn't attached, so the two-monitor checks are still open), and on
  NVIDIA GL the one-time driver thread walk is set off right after the first frame (Meridian first send 62 → 4–6 ms).
  Junction's first repository frame came ~20 ms later, so the orchestrator sent follow-up 4c: defer the trigger until
  start-up settles. Guideline notes: on NVIDIA GL a slow frame ~45 ms after the first is the driver's once-per-process
  thread walk; when timing a "first action", check whether the process already walked; custom chrome reads frame
  metrics at the window's DPI (`GetSystemMetricsForDpi`).
- 2026-10-27: step 4c landed (b788b2ef): the walk's trigger waits 200 ms and then for the window to rest, so Junction's
  first repository frame is back at +94–96 ms and Meridian's first send stays at 2.4–2.8 ms; no frame waits for the
  walk; idle stays at 0 wakes. Guideline note: one-time work set off on purpose goes at the first rest after start-up,
  not right after the first frame — start-up threads of the app's own may still be loading.
- 2026-10-27: round 6 step 5 landed (9484d992..e7f840ad, six commits): Vesper's transfer tick −86 %, find Enter −79 %, find
  results −22 % (−46 % with UIA), free space no longer queried per key, overlay cards are dialogs, and a dialog with
  nothing to focus takes the focus. Found: find-result items are read as the `Hit` record's `ToString()`
  ("Hit { FullPath = … }") — the orchestrator folds a real item label (the path) into step 6 as a reader bug fix.
  Guideline notes: a worker's progress tick is view-only — put what it moves in the part's compare key, throttle on
  `Application.Time`; keep a find's current match out of the rows' shape and `InvalidateRow` the old and new rows; a
  one-argument `RenderWith` with `IndexOf` per row is O(items × rows) — use item rows with `SelectedIndex`; cache OS
  queries per listing generation, not per build. Step agent: 574k tokens (six items is about the most for one agent).
- 2026-10-27: round 6 step 6 landed (9f1b0995..53aea1b3): marks live in the row item (Insert −79 %, Space −84 %), the
  zebra tint reads the row's index, Detail lists draw the cursor (−34 %; fixes the context-menu key's box in a
  scrolled list), Thumbnails keep unchanged tiles (−51 % Down), find results read as their rows show them. Guideline
  notes: put state only some rows show in the item, including the row's index — never look the index up in the
  template (O(rows²)); keep item structs small; let the list draw a background-or-ring selection and switch to
  row-drawn (in the rows' shape) while anything else paints that row; position against a row with `ScreenBorderBox`,
  not `Layout.AbsoluteBorderBox`; for card rows, key rows by place and hand a card back only into its own place.
  User asked to pause at 90 % weekly usage; 80 % now.
- 2026-10-27: round 6 step 7a landed (cf7c7d8b..65190e72): Meridian body typing at 89k characters −87…−88 % (gen-0 GCs
  26 → 3 per 240 keys; URL and assertion typing with that body −94…−95 %), History scrolls to its selection (a bug),
  popovers open view-only and a pick builds once (−41…−66 %), the environment prompt is a dialog. Question 6 for the
  user (popover menus read as buttons). Guideline notes: a model that re-derives everything per edit is O(document)
  per key — keep big content as one string and splice; don't copy text to count things — compare serialized state in
  place; a popover opening is view-only; when the app, not the list, owns the keys, the app scrolls the list; check
  the selection is on screen before trusting walk numbers. Usage 82 %.
- 2026-10-27: the user answered question 6 yes (popover menus become menus, with step 10) and added step 7c (a 300×10
  rich text table that rendered slower scrolled down).
- 2026-10-27: round 6 step 7b landed (eac5f19b..c5586b3f): `TextAreaElement.Highlights`; Meridian and Junction find hand
  the body only their matches (2,000-line find typing −91 %, 19,000-line −95 % and 11.6 → 3.0 ms, Junction 6,000-line
  −98 %; gen-2 collections gone). Pixel fix on the way: transparent spans no longer recolour text, so match text keeps
  its token colour. Guideline notes: don't merge transient tints into a document's span list — use `Highlights` and
  keep the spans the same list; memoize the highlight list by query, options, current match and document; compute
  line starts once per document; span arrays copied per key reach the LOH and cost gen-2 GCs. Usage 85 %.
- 2026-10-27: step 7c landed (0493921d, 49634ea5): the user's slow-at-the-bottom rich text table no longer reproduces
  (flat top to bottom); the grid lines were the one thing drawn whole (313 → 38 rectangles a frame); top/bottom twin
  rows pin it. Guideline note: cull every kind of drawn item by the visible stretch, decorations too (grid lines,
  separators, markers); pin with a top/bottom twin and a render-log count. Usage still 85 % (a lean step: 214k tokens).
- 2026-10-27: the user added step 7d (after step 8): their 2026-10-06 test fell 600 → 60 FPS top to bottom in a real
  window with continuous redraw, which 7c's headless rows can't see; a real-window probe with a positive control at
  e624b076 settles it.
- 2026-10-28: step 8 (runtime-element-layout-state) measured and declined (b35a81d8): −12…−16 % bytes a key in
  Junction/Meridian, but the box read off the element cost the 5,000-row wheel +8…+14 % and AOT resize +3…+5 %; source
  back to f0d3139d. Guideline notes: a field the layout pass, paint cull or hit test reads for every element stays in
  the element; per-row views belong in `VirtualList`; check an AOT time change against an A/A build. Usage 87 %
  (306k tokens, 3.2 h, most of it idle-gated timing).
- 2026-10-28: step 7d landed as a sample (ba7b47af): a real-window probe of the user's 300×10 table is flat top to
  bottom at e624b076 and at the tip (~3,000 FPS), so their 600 → 60 curve did not reproduce; a small mid-depth hump at
  the tip (+0.06 ms vs e624b076) went to Waiting. Usage 88 % (180k tokens): loop paused before step 9 under the user's
  90 % rule.
- 2026-10-30: the user ran the sample on both computers: no slowdown on either, so step 7d is closed (which commit
  removed it stays unknown; the probe was already flat at e624b076). Loop resumed at 88 % with step 9. The user then
  lifted the 90 % rule until the week's reset and said to use up the one-off $10 credit.
- 2026-10-31: round 6 step 9 landed (dcb40b7c, db606351): the command palette's rows on screen are the only elements
  (Junction typing −93 %, Meridian −84 %, suite −68…−69 %, min −9…−14 %; 0 window trees). Found two bugs, now step 9b
  (composite lists aren't told on their own; an open overlay misses a window resize) and question 7 for the user (rows
  told selected). Guideline notes: a virtual list realizes rows in render, after the a11y phase, and the flags end with
  the frame, so what a reader must see needs realizing before that phase; a list's `Padding` moves its scrollbar and
  clip, so put vertical padding in the first and last rows and side padding in a box per row; don't use `SelectedIndex`
  on a composite list (it builds the whole tree), carry the selection in the item; key rows by id + index, since
  position-only keys change a mounted row's name, which builds the whole tree. Usage 90 % (439k tokens).
- 2026-10-31: the user answered question 7: keep the palette rows' selected state.
- 2026-10-31: step 9b landed (6a1c8ab4, ed11148e, f76eb195): a composite list's rows reach a reader without anything else
  building a tree (Junction Commit, Files, Aperture Library now match a fresh tree at rest; bridge off equal), and an
  open overlay is laid out on the frame of a window resize. `input/wheel over a list that cannot scroll` read 8.5–8.6 µs
  AOT after unrelated commits, so step 9's shift was code placement; closed. Guideline notes: app authors have nothing
  to do for composite lists; an overlay never needs `Window.Resized`; `ScrollViewElement.RealizeForAccessibility` is the
  seam for any element that makes its accessible content while drawing; ask for it only where the walk already knows it
  has a scroll view (asking at every element cost `a11y/wheel 1,000 rows, UIA` +5 %). Usage 93 % (546k tokens). Step 10
  split into 10a and 10b.
- 2026-10-31: step 10a landed (b22767a8..f52e2184): a composer character builds the composer alone (−82…−99 %, O(files)
  gone), typing a review comment builds nothing (150 → 2 KB, trees 1 → 0), the Branches list draws its selection (−50 %),
  a diff find step remakes 3 rows, not 36 (−77 %). Question 8 for the user. The `EnterWalks…` flake was the repository
  watcher's reload; settled, 10 of 10 pass. Guideline notes: a form is a part with its own signal, placed by the view's
  build, and its compare is tested on the frame the list counts it; before a list takes the highlight, check what else
  moves with the selection (a highlight can change colour); ask whether a key needs a build at all, since an editor
  holds its own state. Usage 96 % (565k tokens).
- 2026-11-01: step 10b landed (d8e28f93..358b5ba2): History's list draws its caret, hover tint and affordance (−59 %, rows
  a key 2.07 → 0.07), Junction's 12 modal cards are dialogs (UIA −27 %, trees 1.33 → 0.05), popover menus are menus, and
  Esc closes the issue list. The plan's usage ran out on 2026-10-31 at ~08:40 with the step's work committed; the agent
  was stopped before its report, so the orchestrator took the numbers from the commit bodies, finished the one
  uncommitted test and re-ran the test projects. Guideline notes: move what needs a row told its selection out of the
  row (`HoverHighlight`, `SelectionOverlay`); give a modal card the Dialog role and a name; a popover of rows gets
  `AccessibilityRole.Menu` with MenuItem rows. Time a key behind a git process in the UI thread's cycles, not wall
  time (a plain History key waits ~90 ms on the prefetch).
- 2026-11-01: the user answered question 8: fix it (step 11b, after step 11).
- 2026-11-01: the user answered question 9: keep it.
- 2026-11-01: step 11 landed (2be205e5..502f7798): `VirtualList.StickyRows`, and Junction's Changes list on it (Down at 300
  files −90 %, median −25…−29 %; Stage −72 %; 3,160 → 571 elements), selection moves leave the chrome alone. Wheel over
  the list pays for rows made as they scroll in (0.1 → 35 KB, time +1…+5 %), as every virtual list does. Question 9 for
  the user. Guideline notes: a long sectioned list uses tagged rows, `RowHeights` and `StickyRows`; give a sticky row its
  own fill, since the list draws highlights on that fill; a row that holds a focusable control and shows a changing count
  needs a `Key`, or a new count drops the focus; button rows count border as well as padding, so test each row kind's
  height against what it lays out to; compare a virtualized walk by medians, since a min depends on the row it lands on.
  Weekly usage 9 % (607k tokens, 4.1 h, 65 % of a 5-hour window).
- 2026-11-01: step 11b landed (a4f798dd..31994f0a): the list row holding the focus is read through its elements, so a
  reader reaches the comment being typed, with 0 trees a key and bridge-off walks unchanged; a keystroke in text made on
  request builds no tree (typing in rich text beside 300 rows −90 %). Changes' wheel trees are normal (20 per 120 at a
  notch a frame). Guideline notes: a control in an item row needs no app code, and `RowsAreComposite` is for rows that are
  controls; a row read without focus needs a label saying what it holds; a card with a click handler sets a role, or it
  reads as a button; compare wheel tree counts at the same clock pace. Left open: a draft can't be read with focus
  elsewhere (needs API); `VirtualItemIndexAt` on a served list of mixed heights assumes one height; a list over 2,000
  builds a full-length tree as a row opens and again as it closes. Weekly usage 13 % (582k tokens).
- 2026-11-02: step 12 landed (985adef7, 323a3073, d92f14bf): a diff tokenizes only what it can show (History −83 %,
  Commit files −83 %, workers −93 %, GC pause −80 %; spans byte-identical over 934,306 spans) and makes its rows as
  they're asked for (Commit file key −61…−83 %). The agent rewrote its commits along the way, so c01cda36 and 3b8c8ebd
  are gone. Guideline notes: cancelled doesn't mean invalid, keep a result that finished; a prefetch yields to what the
  screen waits for, and rests only when nothing says the reader stayed, so a single key stays immediate; row models hold
  no colours, read them at draw time, so a landing touches only visible rows; a lazy `Items` list must be safe off the UI
  thread (over 2,000 items, labels are read on reader threads); a headless clock runs with frames, so a worker's budget
  needs a standing clock there; walk measures need cold reps, a cache hides the first pass; Windows thread and process
  times alias paced bursts, use `QueryProcessCycleTime`. Question 10 for the user. Weekly usage 24 % (641k tokens, but
  11.3 h: three timing chains of 6–7 minute walks).
- 2026-11-02: step 13 landed (dca932e1..b7a3ded8): Aperture's resize builds only when a build reads something new (−97 %),
  kept cards and parts take grid keys −57…−58 %, and `Part` is public in Core with Vesper, Meridian and Junction on it.
  Guideline notes (in REACTIVITY.md): put size reads behind one noting method and compare with the last build, and have
  that check do what a build did besides (the cursor reveal); a build a loader asks for is a full one, or a rail badge
  goes stale; a kept element keeps the keyboard; a ref read in `OnAfterBuild` lags a list's rows. Trap: `link.exe` fails
  past MAX_PATH publishing the suite from the scratchpad (native-path properties in `r6b13\pub-suite.ps1`; likely step
  12's publish failure too). Weekly usage 28 % (620k tokens, 2.9 h).
- 2026-11-02: step 14 landed (afa7116d, 61b538c3): a text line makes its runs when first read (text/wrap −70 %, multi-font
  ellipsis −69 %, richtext/open −20 %, a 5,000-block document holds −23 %; layouts bit-identical). Kept although sessions
  that draw every line pay up to +2.6 % time (a drawn line itemizes and looks words up twice), as 91d48601's lazy glyphs
  were: the suite reads 0 slower on AOT, and sessions that leave lines undrawn use a quarter to a half fewer bytes.
  Guideline notes: anything that reads `TextLine.Runs` makes them, so test visibility first and weigh caches as if the
  runs were made; a hot string helper turned into span overloads that pass a 60 B style by value cost 6–7 % on AOT with
  JIT flat, so keep the hot string path, add a span method beside it, and check AOT on the rows it serves. Leads: the
  extra measure pass for drawn lines could get cheaper (truncated labels from the prefix-width table, a styled piece's
  font lookup cached); a font registered between a layout and its first draw would make that line's runs in the new
  face (apps register fonts at start-up). Weekly usage ~30 % (589k tokens, 2.7 h).
- 2026-11-02: step 15 landed (799bcb39..2353944f): the AT-SPI bridge keeps its objects by element (dialog tick 494 →
  1.1 KB, palette −86 %, filter −75 %, large-tree mins −5…−12 %; signals byte-identical). The agent first handed back
  without timing or the staleness audit, leaving an audit run splicing Window.cs in the worktree; it was sent back,
  waited for the run to restore the tree, confirmed it equal to 4325bac6, and finished both. One row slower: the
  kept-tab switch +2.5–2.8 % on AOT, kept on balance against the large-tree wins (Waiting). Guideline notes: keep a
  bridge's objects by element and refresh them with a walk that allocates nothing; note what the signals need before
  mutating the objects; replace arrays a reader may hold, never write them in place; prove a bridge change with recorded
  signal streams, masking test noise; a long-lived concurrent map costs a little on mass add and remove. All 15 build
  steps done; the full test suite next. Weekly usage 36 % (553k tokens).
- 2026-11-02: round 6 full suite on d124755e: Windows `dotnet test HultUI.slnx -c Release` 30 projects (every test
  project in the solution), 0 failures (UiTests skipped by their gate), 8 min; WSL Fedora (`scripts/wsl-test.sh`, CI's
  CrossPlatform filter) 22 projects, 0 failures. Neither known flake failed. Warnings only: CS1573 (a `<param>` missing)
  at AccessibilityTreeBuilder.Refresh.cs:130-131. main has not moved since f31194ab. Phase → release.
- 2026-11-02: round 6 merged into main as 0b8e684a (detached origin/main merge in this worktree, tree identical to the
  tested branch, primary fast-forwarded behind the guard). CI run 41429148272 red, two faults from this round, no product
  bug: (1) docs: docfx with `--warningsAsErrors` rejected `<see cref="ScrollRangeIntoView"/>` in step 11's `StickyRowFor`
  (the method is `ScrollViewElement`'s) → 6dbbbe63; (2) Windows test: Vesper `PartTests.EveryValueAPartShows_ReachesIt`
  waited only until a folder was queued for measuring; the worker set the total and Done after the last pumped frame on
  the 2-core runner, so the full build read 6.2 K while the footer part showed ≥ 6.2 K → f0f3df8d waits for Done.
  Merged as 1939afcb; CI run 41432647572 green (test, test-linux, docs). Guideline notes: a test that waits for a worker
  waits for its done state, not its start; run `dotnet docfx docfx.json --warningsAsErrors` before a release when public
  doc comments changed. Round → 7, Phase → survey; the loop stops, and round 7 starts in a fresh context.
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
