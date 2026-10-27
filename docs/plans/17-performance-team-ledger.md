# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 6
- **Phase:** survey — round 6: spawn the eight experts (Phase `survey` in the brief), reviewing round 5's build.
  Round 5 released (61308cfd + CI fixes f31194ab, CI green); the total comparison of rounds 1–5 is in
  [`17-reports/total-r1-r5.md`](17-reports/total-r1-r5.md). The ledger was split on 2026-10-27: older rounds are in
  [the archive](17-performance-team-ledger-archive.md) (search it, don't read it whole), and the brief has
  "Keeping token use down". Round 5 question 5 open (Junction History row pixels). History note: rewritten
  2026-10-25 (every commit after 3353ce47 has a new ID; a pre-push hook refuses pushes built on the old history).
  To resume in a fresh context, run in this worktree: `/loop Run the next step of the performance team: follow
  docs/plans/17-performance-team.md, with state in docs/plans/17-performance-team-ledger.md. Read both once per
  session (afterwards only the ledger's Phase and the part the step needs), do the step the ledger's Phase calls
  for, record it in the ledger, and commit.` Before resuming, `git fetch` and merge main if it moved.
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`)
- **Review commits since:** 643eb07b (round 6 reviews round 5's build, 643eb07b..f31194ab: steps 1–14 plus the
  two CI test fixes; the Landed rows below are round 5's)
- **Probe code** in the scratchpad of the session that ran rounds 4–5 (a fresh context may have another
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
| 1. Layout | 5 | no | none |
| 2. SkiaSharp: painting | 5 | no | none |
| 3. GL and Vulkan | 5 | no | none (measurement trap: a drag driven by cross-thread `SetWindowPos` draws an extra `EXPOSED` frame per growing step — 40 of 120 frames; drive steps on the window's own thread) |
| 4. .NET runtime | 5 | no | ee6d0bb0: a public GPU source outside Media/WebView signals by post and now freezes during any size/move loop (minor) |
| 5. Text pipeline | 5 | no | 5e6ef9a2: with a reader, every keystroke rebuilds the TextArea's accessible text through per-character `TextCaretX`, O(rows × length²): +449 KB/key at 2,000 lines |
| 6. Reactivity and reconcile | 5 | no | 87698f16 + e6a432fa: the restore misses a container whose every child was handed back (Vesper Brief 1 a11y tree/key with a reader, Reel per build); b35fc587/3135de73: review composer typing 1,367 KB per character (2 app + 3 chrome builds) |
| 7. Accessibility bridges | 5 | no | none (b099714b made an older gap systematic: menu items get zero-height rects, overlay changes never told) |
| 8. App workloads | 5 | no | none — round 4 helped all five apps (Junction session −40 %, Meridian −29 %, Reel −26 %, Aperture −15 %; Vesper partner/grid walks −29…−67 %) |

## Queued candidates

Rounds 1–5's build orders all landed (round 5's order as it stood is in [the archive](17-performance-team-ledger-archive.md#round-5-build-order); round 4 released in 643eb07b, round 5 in
61308cfd). The next survey re-ranks Waiting and its own new candidates into round 6's build order. A build
step may bundle one expert's related small candidates as separate commits. Items marked **(user)** wait for a
decision in Needs the user; the rest of their step goes ahead.

### Waiting

Re-ranked by the round 6 survey.

- **vesper-marks-in-row-shape** (6/8, lead from r5 step 12) — a mark remakes every visible row because marks are in
  the rows' shape: ~150 KB of what is left on Insert, Space and type-to-find (190–209 KB/key).
- **vesper-transfer-progress-rebuilds** (6/8, lead from r5 step 12) — transfer progress still rebuilds the whole
  window about every 40 ms; route it through the footer/progress part.
- **vesper-own-selection-revisit** (1/6, lead from r5 step 12) — Vesper's panes kept their own selection highlight
  (68817800) because the window build was most of a key; with parts that is no longer true. Re-measure
  `RowHighlight` on Detail/Brief.
- **meridian-history-aot-p90** (6/8, lead from the total comparison) — Meridian History Down, headless AOT: p90
  2.69 → 3.47 ms against the baseline (a stretch of ~9 keys near the walk's start at ~3.4 ms, same bytes); median
  flat, min −11 %, JIT and the real window improved. Trace before calling it real.
- **meridian-find-typing** (5/8, lead from r5 step 11) — find typing in a 2,000-line response is still 1,529 KB per
  character after the pane stopped rebuilding the window, all in the search and the span merge.
- **meridian-method-popover-double-build** (6/8, lead from r5 step 11) — picking a method builds twice: the row
  handler and `SetMethod` both call `MarkDirty`.

- **a11y-atspi-snapshot-per-diff + apps-scenario-atspi-rows** (7, Linux) — 265 B per node per apply; AT-SPI steps
  3.9–4.1× UIA's; keep objects by path; needs a suite seam. Since step 13 (c9adbb6c) only structural applies rebuild
  the AT-SPI snapshot; re-measure what is left.
- **a11y-served-items-followups** (7, leads from r5 step 14) — a live Orca check of `manages-descendants` with
  `active-descendant-changed` on lists served on demand; telling a relabel of a served item a client holds (today
  names are read when asked, so a relabel is not told).
- **a11y-in-place-fallbacks** (7, lead from r5 step 13) — the in-place refresh falls back when a node with children
  changes (4 cases in the audit) and on every scroll of Aperture's composite grid (rows realized at render).
- **vesper-coalesce-markdirty** (6/8, lead from r5 step 8) — routing Vesper's `MarkDirty` through a signal would fold all its
  one-off double builds (viewer open, name prompts, drive pick, confirm cancel; declined one by one in DECISIONS.md).
  Also: caching each folder's trail segments would remove most of the Detail resize comparison's ~7 KB per step.
- **virtuallist-sticky-headers** (1, lead from r5 step 8) — Junction's Changes list stays a scroll view (sticky section headers,
  mixed heights, composer in the column); virtualizing it needs sticky headers inside a VirtualList.
- **junction-changes-composer** (6/8, app) — summary keystroke 95.8 KB; lists and diff unchanged: −60…−70 %.
- **apps-aperture-components** (6/8, app) — rail + title bar: −25…−35 % per grid key. Also Aperture rebuilds on
  every resize step (92 KB, 63 patches): build only when the column count changes, as e6a432fa.
- **apps-junction-highlight-prefetch** (5/8, app; priority up after r5 step 6: the worker drives 95 ms of GC pauses per 100 History keys and defeated the gen-0 cap) — worker tokenizing 5.9 MB per History key at 500 ms (25× the
  UI thread), 92.8 MB at start-up; tokenize only to the last hunk line / after the selection rests.
- **text-lazy-line-runs** (5, lead after step 3) — undrawn lines' strings and run objects (50 % of text/wrap).
- **dialog-scrim-button-role** (7, correctness bug, from r5 step 2) — `Dialog.Show`'s scrim has a click handler, so it is
  inferred as a Button named from all the dialog's text: every dialog is announced inside a "button", and each palette
  keystroke fires a Name change on it.
- **palette-row-keys** (6, lead from r5 step 2) — `CommandPalette` keys rows `{group}|{title}|{index}`, so every
  keystroke replaces every row and its a11y nodes (6 UIA structure events per key); key by command.
- **layout-patch-keeps-layout-dropdown-expander** (1, lead from r5 step 5) — after step 5 a Fluent controls page rebuild
  still lays out 16 elements, from Dropdown, Expander and a one-argument list; give Dropdown and Expander the same
  `PatchMarksOwnLayout`/`PatchThemesTwin` treatment. Also: the layout audit compares border boxes only — it should
  compare `LayoutResult` padding and border too (step 5's stale-padding bug was found only through pixels).
- **uitests-borderless-maximize-width** (3, from r5 step 1; pre-existing at a33bd6b0) — the two Borderless runs of
  WindowStateActivationTests fail on this desk: the maximized client is 2,554 px wide against a 2,560 px work area.
- **a11y-box-selected-patch** (7, lead from r5 step 1) — changing `AccessibleSelected` on a plain Box builds a whole a11y
  tree instead of a patch.
- **meridian-response-find-step** (8/5, lead from r5 step 1) — Meridian's response find step is still 1.7 MB per Enter
  after the FindBar fix.
- **live-frame-requests-public** (3/4, API decision for the user if ever needed) — a third-party GPU source that only
  posts is now drawn at SDL's timer rate (~64 fps) in a move or held drag; its own rate would need `LiveFrameRequests`
  public. No consumer today.
- **richtext-short-viewport-jitter** (5/editor) — viewport shorter than a line: caret moves scroll between
  two offsets.
- **terminal-noop-scrollback-wheel** (2/Terminal) — a no-op scrollback wheel notch raises ContentChanged.
- **layout-virtuallist-spacer-stale** (1; invisible) — one-line mark on the spacer; with an audit.
- **controls-menu-max-height-scroll** (1/2; not perf) — long menus can't scroll; rows past the bottom
  unreachable by mouse.
- **vesper-free-label-per-build** (8, lead) — `DriveInfo.AvailableFreeSpace` per pane per build: a disk query
  (or network round trip) per key on the UI thread; cache per folder.
- **text-open-measures-every-line (full)** (5; user: not now, 2026-10-13).
- **meridian-body-edit-bytes** (8/5, lead from r5 step 6) — editing an 89k-character request body allocates ~2.0 MB per keystroke on the UI thread (supports textarea-chunked-document or a Meridian fix).
- **textarea-chunked-document** (5/4; user chose this option B, 2026-10-25, after step 6's LOH threshold) — store a
  TextArea's text in chunks; keep `string Text { get; set; }` (built on read, cached per version) and
  `OnTextChanged(Action<string>)`; add a read-only `TextAreaElement.Document` (`Length`, indexer,
  `GetText(start, length)`, `CopyTo(Span<char>)`, `Version`) and `OnDocumentChanged(Action<TextChange>)`
  (`Start`, `RemovedLength`, `InsertedText`, `Version`) so large-text editors stop copying the whole document per
  keystroke. Medium–large: undo, find, the a11y text pattern, tests reading `Text`. Only worth it if documents
  above ~256k characters matter once the threshold has landed.
- **vk-startup-preload** (3, low) — the GL preload runs only with `HULTUI_GL_PRELOAD=1` and only Reel calls it.
- **gl-driver-thread-walk** (3, GL on NVIDIA; r5 step 7's gl-first-use-flush trace, no code) — Meridian's first-response
  flush (43–75 ms in process, JIT) is one glyph-atlas upload (`glTexSubImage2D`, 508×13 R8) inside which NVIDIA's GL
  driver walks every thread on the system: a driver thread in `Thread32Next` and `NtMapViewOfSection`, the render
  thread polling `NtQueryInformationThread` and sleeping (236 of 237 samples). It lands in the first GL-heavy frame
  after the process gains threads (the send adds 8): with 14 pool threads added at 2 s it moved to the first palette
  open (162 ms) and the send had none. Ruled out: Skia program compiles (none in that frame), GC (a no-GC region left
  it at 54 ms; the 2 GCs inside take 3 ms), the GPU (a `glFinish` after every frame: still 56 ms), the upload's shape
  (the same upload into a texture of our own: 0.19 ms the first time), the pool grown before the window (still
  49–56 ms). The Vulkan renderer has no such stall (Flush max 11 ms, the first frame; Present max 2.8). The first
  frame's flush (26–29 ms) is Skia's six program compiles (`glCreateProgram` 21 ms). Fix options, none tried: an
  NVIDIA application profile with Threaded optimization off for the GL apps (a driver setting the installer would
  write: the user's call), or no thread creation on the GL apps' interaction paths; warming programs at idle does not
  touch it. Probe: scratchpad `r5b7/` (host `MeridianMode.cs`, `Sampler.cs`, `AllThreads.cs`, `probe-part3.patch`).
- **layout-ordered-children** (1, low) — suite rows only; every app list is virtual.
- **apps-tokenize-on-ui-thread** (5/8, low).
- **Suite scenarios** (8): document-switch (Junction Files 222 KB/key), image-grid (Aperture posters 157 KB/key).
- **aperture-poster-as-hero**, **aperture-tmdb-size-fallback**, **junction-narrow-gitlab-glyphs**,
  **meridian-pane-choice-not-persisted**, **meridian-min-width-wraps** (apps; not perf; untouched by round 4).

Closed from Waiting by round 5 (see Examined): paint-subblob-trusted-face-bounds, paint-drawspannedline-
overlap, reconcile-restyle-on-change (b), `MonospaceAsciiAdvance` on `IMeasureContext`, a VirtualList measured
as a leaf. Folded into the build order: a11y-layout-refresh-bounds-only (→ 13), a11y-overlay-property-changes
(→ 2), sdl-hint-minimize-raise (→ 1), vk-sdr-video-frame-target-wrap (→ 7), meridian-response-frame-flush
(→ 7, gl-first-use-flush), junction-commit-file-switch (→ 8), the last-line prefix-table lead (→ 3),
apps-meridian/vesper-components (→ 11, 12).

## Landed

Round 5 (the round the next survey reviews). Rounds 1–4 are in [the archive](17-performance-team-ledger-archive.md#landed-rounds-14).

| Round | Commit | Candidate | Before → after |
|---|---|---|---|
| 5 | a52de219 | reconcile-restore-moved-into-empty | The no-old-children path tries `TryRestoreMoved` (span + per-thread scratch list; children taken from the build's tree from the end; the parent's pending moves dropped in one pass). Vesper Brief with a reader 300.6 → 230.2 KB/key (−23 %), a11y trees per key 1 → 0, min −5.4 %; Reel app rebuild with a reader 5.8 → 2.0 KB (−66 %); bridge off flat; pending-move searches 222/key (12,569 comparisons) → 0. Streams identical to a forced rebuild every step (UIA + AT-SPI). Declined: an O(1) per-element move-pending counter (+8 B per element, DECISIONS.md). |
| 5 | 6388f868 | findbar-step-double-rebuild | A `FindBar` holds its own search's notices during a step and calls `onChanged` once, reveal set first; opening with a selection builds once, not three times. Per Enter: Vesper reader 518.9 → 416.7 KB (−19.7 %), Junction diff 538.0 → 440.5 (−18.1 %), Meridian response 2,017 → 1,715 (−15 %); builds per step 2 → 1. ELEMENTS.md. |
| 5 | 2a332218 | live-frame-posted-work | Posted work stops being a reason to draw a live frame only for 100 ms after a frame was drawn for a source's request (platform clock): a source that only posts draws 56/124 frames in a move/held drag (0 since ee6d0bb0); Reel 1080p120 keeps 0 drops. THREADING.md, MEDIA.md; headless + guarded real-window tests. |
| 5 | 3dd5c97b, 6895ac2d | text-textarea-a11y-row-xs | Suite row "a11y/type in a 2,000-line TextArea, a reader reading, bridge on"; each row's string cut once, and **default member `IRenderContext.TextCaretXs(lineText, style, Span<float>)`** (API addition; Skia finds the line once, then `CaretXAt` per boundary). 704 → 308.7 KB/op (−56 %), min −25 %; boxes bit-identical (all corpora; Skia 6 styles × 7 line kinds); one ask per row (was 570 per-character asks). |
| 5 | 0b477882 | apps-scenario-overlay-reader | Suite rows: context menu walk and command palette typing over 1,000 rows, UIA. |
| 5 | 02e17103, cb34eecc, 08c49d10, b23c9251 | a11y-overlay-live | Approved. `PrepareOverlays` styles and lays out overlays after root layout, before the a11y phase; overlay Children dirt builds, layout dirt/moves coalesce; hit-testable overlay content gets the observer while open; `FindNodeDown` starts at the overlay's node; a change inside an open overlay rebuilds, diffs and swaps in only that overlay's subtree (`TryRebuildOverlays`); the overlay refresh keeps its own clock (b23c9251: overlay churn had postponed the window's refresh indefinitely). Overlay toggle/keystroke patch 0.9–1.6 KB, no tree. Palette typing over 2,000 commands, UIA 811.7 → 553.4 KB/key (−32 %), min −13.3 %, trees/key 1 → 0 (at the base the reader heard nothing). Proof: 49/49 probe steps equal a fresh tree on recording/UIA/AT-SPI (base 13–15), FlaUI 24/24 (base 10), live WSL pyatspi 39/39 (base 15), Junction/Meridian palettes 0 stale (base 7/7); staleness audit 0 STALE over 2,660 audits; 50 frames byte-identical. 0 slower. |
| 5 | 22021d66, 827f6ad5 | contextmenu-a11y-selection-geometry | Keyboard/hover selection told as IsSelected (a Down key 0.5–1.1 KB patch, no tree); items placed in `MeasureContent` (same sums as `Render`), so no zero-height boxes. |
| 5 | cc028a7f | atspi-expandable-state | `state-changed:expandable` when expanded goes to/from null; `expanded` only when that bit changes. |
| 5 | 91d48601 | text-glyphs-on-first-draw | A multi-word run keeps its width and glyph count and makes its glyph array on the first `ShapedRun.Glyphs` read (one field holds the array or the pipeline; count in existing padding, `ShapedRun` stays 56 B; letter-spaced and > 65,535-glyph runs compose at once; UI-thread contract on the property). Per step JIT = AOT: text/wrap 1,367 → 544 KB (−60 %), ellipsis 1,844 → 740 (−60 %), richtext/open 9,259 → 5,737 (−38 %), resize 2,016 → 1,207 (−40 %), 13 rows −8…−28 %; time 0 slower, 5 faster (richtext/open −11.4 %). Held: 5,000-block rich text 24.79 → 10.58 MB open, 10.45 → 4.04 closed. Scroll rows pay first draws (viewer wheel 2.7 → 10.5 KB, richtext/wheel 1.3 → 8.3, scroll/jump +6 % min, never past the bar on both); whole sessions never cost more; Junction walk 12.99 → 12.32 MB. 20,000 layouts bit-identical (10.5M glyphs), 180 frames identical. WSL not run yet. |
| 5 | cc6f8467 | text-line-temporaries-scratch | `Itemize` and BiDi lines use kept lists (segments, levels, runs, visual order), let go past 4,096; `ShapeLineText` sizes its run list. On top of 91d48601: bidi wrap 534 → 239 KB (−55 %), styled bidi wrap −54 %, emoji labels 6,344 → 4,164 (−34 %), styled wrap −37 %, text/wrap −12 %, others −7…−31 %; flagged rows within ±1 % on rerun. Bit compare and pixels identical. WSL and Junction.Tests not run yet. |
| 5 | a1ff84a1 | text-last-line-view | A wrap's last line fills `BuildPrefixWidths`' kept lists (paragraph's own Han context) instead of a durable PrefixWidths. Per step JIT = AOT: text/wrap 480.8 → 397.3 KB (−17 %), richtext/open 5,280 → 4,631 (−12 %), resize 1,099 → 968 (−12 %); time flat; flagged rows noise on filtered rerun. 20,000-layout dump identical (a ×1.5 width mutation changes 911 layouts, proving coverage); 180 frames identical. Step 3 checks: WSL CrossPlatform 22/22, full `HultUI.slnx` 30/30 (Junction 989/989). |
| 5 | e5ff3fea | paint-run-ink-extents-dead-keys | The run-keyed extent map is gone: each variable face keeps its glyphs' bounds at size 2048 in 256-glyph 2 KB pages made on need; a run's extent is the union per drawn line, fused with the X-order pass. Bit-identical to a direct Skia read over 1,284 runs; 185 frames identical (512 MB glyph cache, A/A). Retained after a 20,000-line Cascadia document closes: heap 6.45 → 4.10 MB (−36 %), extents 2.294 → 0.010 MB; steady frames 0 B; 0 slower (plain comparisons, not `MathF.Min/Max`, on AOT). |
| 5 | 09abf712, dff306df | paint-blob-maps-frame-working-set | Suite row `terminal/dense colour screen, unique rows, forced frame` (1600×1000, 6,662 sub-blobs a frame). Kept blobs carry a frame stamp and glyph count; a full map releases only blobs drawn in neither this frame nor the last; next sweep when the map holds twice what the last kept (≥ 2,048); counters skip the pass when nothing is old; hard cap 262,144 glyphs → full clear. Blobs made per steady frame: dense terminal 2,338/5,877/12,520 → 0 (1200×900/1600×1000/4K), Chinese terminal 4,096/8,294 → 0; new row −23.2 % JIT, −22.5 % AOT; 0 slower; +0.3–0.6 KB/op one-time map growth. 190 frames identical; WSL 10,498 pass. |
| 5 | ce70fec7, 415ecefa | layout-patch-keeps-layout-more-kinds | Suite row `input/type beside a large TextArea and checkboxes`. TextArea, Checkbox, RadioButton, ToggleSwitch and RichTextEditor opt into `PatchMarksOwnLayout` + `PatchThemesTwin`; TextArea/RichTextEditor `ApplyTheme` write padding/border only where they differ and mark layout (fixes: a theme switch without a rebuild now lays out a text area's new padding — Meridian's light-theme response drew at the dark padding; a rebuilt switch slides at the theme's `TransitionSeconds`). AOT per key: Meridian URL laid out 10.5 → 1.2, MeasureSize 365 → 47.5, frame −9.6 % reader on / −7.9 % off; Junction summary 10 → 0, 2,142 → 0, frame −5.4 % / −10.8 %, reader bytes −20.6 %. Suite 0 slower. Layout audit over 29 projects: no new differences; 70 scenes identical. |
| 5 | a31a38c7, 93226dda | layout-virtuallist-offsets-kept | Suite rows `virtual/rebuild beside a 20,000-row RowHeights list` (+ `rows told`). `EnsureOffsets` fills a spare array, swaps only when an offset moved; `KeepUnchangedRows` keeps the computed offsets (no second array after `InvalidateRows`); +1 kept array per RowHeights list. Vesper viewer find typing 1,701.1 → 1,544.8 KB/key (−9.2 %); suite rows 90.3 → 12.2 KB/op (JIT −10.4 %) and 203.4 → 47.1 (AOT −13.6 %). WSL 10,523 pass. The height function still runs O(n) per patch. |
| 5 | 828a7d69 | runtime-gc-loh-threshold | The five AOT apps import `apps/AppRuntime.props`: `RuntimeHostConfigurationOption System.GC.LOHThreshold = 524288` (baked into ILC's knob blob; `--runtimeopt:GCgen0MaxBudget` is silently ignored, so ILC runtimeopts can't be trusted key by key; a `DOTNET_GC*` env var still overrides). Real AOT windows, 3 runs a side: Meridian 89k-character body typing gen-2 GCs 103 → 2, GC pause −48 %, frame p95/p99 1.32/1.80 → 1.02/1.35 ms, CPU −25 %, private +5.8 %, slowest key +1.3 ms; Junction History private −11 % (LOH 34.4 → 12.9 MB); start-up unchanged. `AppRuntimeSettingsTests` linked into the five app test projects. NATIVE-AOT.md "GC settings for an app". Focus guard 0 over 111 launches. |
| 5 | b1323e9d | runtime-gc-gen0-budget (declined) | A `System.GC.Gen0MaxBudget` cap of 6/8/10/12 MB: memory −1…−8.5 % in Vesper/Aperture but Junction History private +11 % at 6 MB (its highlight worker allocates ~6 MB per key; live data promoted), frame p99 and GC pause up at every value. DECISIONS.md. |
| 5 | 7f9df4ea | vk-sdr-video-lazy-barrier | The host owes the acquire barrier until Skia's first submit (`GetProcedureAddress` hands Skia a `vkQueueSubmit` trampoline found by queue; the barrier rides at the head of Skia's first batch, or its own submit at Present); `AcquireBarrier` type; every direct-path frame keeps its target. 8-bit video bytes per frame 1080p120 2,030 → 86 B (−96 %), 1080p24 2,056 → 112, UiTest ramp 2,331 → 35 (limit 2,500 → 100); wraps per frame 1 → 0; mpv's GL draw time flat (an eager-barrier control reads +20…+35 %); one `vkQueueSubmit` fewer per frame. Validation core+sync 0 messages (positive control 2 VUIDs ×10); ramp captures brighten. HDR.md. |
| 5 | 6c627f27 | vk-drag-kept-target-wraps (8-bit) | On an 8-bit over-allocated swapchain a kept target is wrapped at the allocated size, canvas clipped to the window in device pixels, window size out of its key; F16 keeps window-sized targets (deferred after the survey's GPU errors). 80-step drag: wraps 80 → 12, bytes per step 2,200 → 515 (−76 %), step time flat; 5 mid-drag in-process captures identical (a 64 px clip error shows 184,512 channels). UiTest `--drag` asserts it. No `nvlddmkm` events before/after; 128 guarded launches, 0 foreground takes. |
| 5 | b1a12ef1 | junction-review-composer-typing | Both editor callbacks go through `DraftEdited` (one `RerenderView` signal write → one view-only build), which keeps the woven rows and remakes only the editor's row (`InvalidateRow`); Create MR description the same. Composer typing 1,367 → 209 KB/char (−85 %; AOT −84 %), time −36 % JIT / −28 % AOT; builds per char 2 → 1, chrome builds 3 → 0, rows made 230 → 1; Create MR −61 %. |
| 5 | fe50f969 | meridian-history-rows | History on item rows `(Entry, Index)` with `SelectedIndex` (no per-row `IndexOf`), one kept label delegate, `FilteredHistory` memoized; the search box no longer calls `MarkDirty` itself. Down 355 → 109 KB/key (−69 %; 2,000 entries −73…−76 %), search 417 → 77 KB/char (−81 %), time −7…−21 %. The selected send is now announced as selected (was unselected). |
| 5 | 54eded98 | vesper-mark-double-rebuild | Insert builds once (through the cursor move): 646 → 414 KB/key (−36 %), time −9 %. The other one-off doubles declined (DECISIONS.md). |
| 5 | 57e954ca | apps-junction-commit-tree-virtualize | The Commit file list is a VirtualList of item rows (header as row 0, `CommitRowHeights` from font line heights, scroll-into-view by summed heights, flattened rows kept per tree/expand state); the highlight landing uses `MarkViewDirty`. 382-row commit Down 2,002 → 142 KB/key (−93 %), time −27 % JIT / −31 % AOT, window elements 1,834 → 345. Off-screen rows leave the a11y tree (as the Files tab). Changes list declined (sticky headers). |
| 5 | ea6ceb7a | apps-junction-view-parts | History's include bar and footer handed back while their inputs are unchanged (`KeptPart`; every History child keyed); `FilteredCommits` returns the log itself with no query. History Down 82.2 → 47.8 KB/key (−42 %), reader −35 %; parts built per key 2 → 0. |
| 5 | 1ed9d938 | vesper-detail-resize-stale | Approved (not perf): the fit comparison runs in Detail view too, so a resize updates the path trail's truncation; a 4 px step that keeps the trail 2.2 → 9.4 KB. 39 scenes (all step 8 items) identical against a deterministic A/A; WSL passes. |
| 5 | 0d3bc10a | layout-virtuallist-selection-highlight (API) | Approved. **Public `RowHighlight` record struct and `VirtualListElement<T>.SelectionHighlight` (+ fluent `.SelectionHighlight(...)`)** (API addition; null = old behaviour): drawn by an internal `ScrollViewElement.RenderUnderContent` hook after clip/scroll and before rows, with the same `DrawRoundedRect` a Box uses; rows made with `selected = false`; `SelectedIndex` marks paint. Suite row `virtual/selection walk 1,000 item rows, the list draws the highlight`: rows per step 3 → 1, 25.4 → 16.5 KB/op (−35 %, AOT the same), time −2 %. 11 tests, pixels identical to self-drawn rows. ELEMENTS.md §15 (which rows can't adopt). |
| 5 | 8157c282 | junction-tags-selection-highlight | Tags list draws the caret (`Neutral900`, `RadiusRow`); rows ignore `selected`. Tags Down 87.4 → 40.6 KB/key (−54 %), reader −54 %, time −6…−15 %; elements per key 45.5 → 1.5. 74 scenes identical (pixels, a11y, focus). History (affordance in the row's flow, selection-dependent hover tint — round 5 question 5) and Vesper's cursor (zebra, partner ring) declined, 68817800. |
| 5 | 76b6f78e | events-handler-lists-kept | A patch sheds app handlers in place and keeps the lists, dropping only types nothing listens for (mid-dispatch copy-on-write unchanged): patch 208–328 → 0 B; per key −2…−5 % (Vesper Detail −4.7 %). Under the bar alone; the base for 574f43a7. |
| 5 | 574f43a7 | events-handler-array | Re-opens P4's inline half with rebuild-churn evidence: one `EventHandlerEntry[]` per element (24 B entries: typed delegate, phase/flags, an `EventKind` with a static invoker per event type), the first two handlers in one 72 B array reused by patches, no wrapper closure. Per key JIT/AOT: Vesper Detail −14 %, Meridian URL −13 %, Meridian History −12 %, Junction summary −20 %; first handler 424 → 72 B, second 208/88 → 0. Retained: handled Box 1,005 → 654 B, TextInput 3,912 → 1,856 B, 10,000 rows of two handled boxes −30 %. Dispatch time flat or faster (depth 8 all listen 117 → 88 ns JIT). Suite: 10 rows −19…−35 % allocation, none more, 0 slower (TableView 10k sort +4 % AOT only, unexplained, DECISIONS.md). Order, exact-type match, copy-on-write, live pinning, passive handlers kept. P4 bitmask stays declined. |
| 5 | ff6262ff | apps-meridian-components | Title bar, rail, request tree and response pane are kept `Part`s reading `_partRevision` (`MarkDirty`/`Rerender` bump it); editor edits (URL, headers, params, body, assertions, extractions) go through `DocEdited` view-only unless the edit flips the unsaved dot; sub-tabs, URL box, History walk, filters, search and environment values view-only; the response pane's tabs, pretty/raw and find bar rebuild the pane alone (`ResponseDirty`). Headless KB/key (JIT = AOT within 1 KB): URL 237 → 72 (−70 %), with 2,000-line JSON 259 → 72 (−72 %), 200 requests 938 → 72 (−92 %, scaling gone); header/param/body −64…−70 %; History Down −35 %, search −51 %; environment value −33 %; find typing −12 % (window builds per char 1 → 0); patches per key 218 → 54; with a reader −58…−92 %. Time −1…−22 % (frames dominate a key). Suite 0 slower/0 faster ×2; 48 scenes identical (pixels, a11y tree, focus) JIT + AOT. 12 PartTests vs forced full rebuilds, 11/11 mutations caught. |
| 5 | fcf366c6 | apps-vesper-components | Title bar, rail, command line and per pane the tab strip, path trail (path + result-set pill), column header and footer are kept parts placed through `Part.Place()` from the app's build (a stale part rebuilds right there, so `OnAfterBuild` sees the current field and no part builds before its parent removes it). `MarkViewDirty` compares only what a view-only change can move (active pane, footer marks/filter/cut/decodes, command-line filter/notice/prompt); navigation uses `PaneDirty` (that pane's parts only). Row presses, marks, type-to-find, Tab, Brief scrolling, landings and dialog list walks view-only; sort-column hover rebuilds one header. Fix: a duplicated tab's view/sort set before its build. KB/key (AOT within 2 %): Detail 210 → 34 (−84 %), partner 218 → 44 (−80 %), Brief 199 → 37 (−81 %), Thumbnails 219 → 58 / 257 → 94, Insert/Space 362 → 190 (−47 %), Tab 207 → 105, folder in/out 554 → 273, type-to-find 365 → 209, sort hover 378 → 25 (−93 %), preview 329 → 73; with a reader −31…−80 %. AOT time −7…−21 % (folder I/O flat). Patches per Detail key 169 → 7, resolves 181 → 19. Suite 0/0 ×2; 93 scenes identical (pixels, layout, a11y, focus) JIT + AOT. 11 PartTests, 32/33 mutations caught (survivor a redundant `FlexShrink(0)`). |
| 5 | bd88e747, 4175f435, c9adbb6c, 4f471ae9, 577949d9, eb856985, a123f46f | a11y-layout-refresh-in-place | (a) nodes keep scroll-space boxes, `BoundingRect` computed up the parents (groundwork); (b) a coalesced layout refresh walks elements against the applied tree by the builder's rules and updates offscreen bits, scroll offsets and moved boxes in place, comparing item labels as a rebuild's diff would, falling back to a build on any id/count mismatch; (c) AT-SPI objects read box and offscreen state from their node; refreshes that keep failing in place build at once for a while (filtered-list row +36 % → flat); new suite row `a11y/filter a list below 1,000 rows`. KB/op JIT: item-row walk 110.7 → 24.5 (−78 %), one-argument walk 338.1 → 251.9 (−25 %; labels still compared, skipping declined in DECISIONS), beside details 500 ms 215.9 → 122.6 (−43 %), wheel UIA 261.4 → 3.3 (−99 %), edit 1 label UIA 35.3 → 0.9; trees per key 1.0/0.6 → 0; time −5.7…−8.5 %; AOT same bytes, −5…−9 %. Probe recording/UIA/AT-SPI: reader walk 98/99/400 → 8.2/8.5/8.9, wheel 43/43/172 → 0.2, Reel-style tick 79/80/310 → ~1.5 (1 tree/s → 0), label edit 174/174/691 → ~2. Junction History 500 ms 63.5 → 51.6 KB/key (−19 %). Audit over 7 suites: 960 in-place refreshes, 0 mismatches; 0 STALE in 2,768; seven event streams byte-identical. Suite 0/0 JIT + AOT. |
| 5 | 6483f85f, 167b4ef9, 46a5aa12, 69a9fac2, 27673829, b6de3fcf | a11y-virtual-items-and-selection | Approved (added events). A list served on demand (over 2,000 items) carries its item count and selected indices on its node and tells changes of them as its own (new public property-name constants `AccessibilityProperties.ItemCount`/`ItemSelection`, additive; `InternalsVisibleTo` for the Windows bridge); UIA raises IsSelected on the `UiaVirtualItem`s, AT-SPI serves the items on request (ChildCount 0 → 3,000, objects made only when asked) and sends `state-changed:selected`. 40 moves over 3,000 items: UIA 0 → 80 IsSelected, AT-SPI 0 → 80, the same items and order as the 1,500-item path; 0 trees per move; 4.0 → 4.7/5.0/5.4 KB per move (recording/UIA/AT-SPI; 1,500-item path 4.4/4.8/5.2). Suite rows `a11y/selection walk over 3,000 item rows {recording, UIA}` 24.4 → 24.7/24.8 KB/op; AOT 0/0; JIT one row flagged (+13 %, no a11y code) reads +0.9 % on 4 filtered pairs. Audit 1,085 in-place refreshes, 0 mismatches. Differences from the small-list path in DECISIONS (relabels not told, no `manages-descendants`, AT-SPI count change = cache-remove + children-changed + cache-add). 14/14 mutations caught; WSL Fedora 95 bridge + 175 Headless a11y pass. |
| 5 | a18e70dd | sdl-hint-minimize-raise | `Minimize`/`Raise` run under `RefuseIfUnwanted()`: activations reaching the guard 2–3 → 0 with the hint; real apps unchanged. |

## Examined, not worth it

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

Round 5 questions (asked 2026-10-25 on resume). Recommendations in brackets.

1. **layout-virtuallist-selection-highlight:** may a VirtualList draw the selected row's highlight itself, opt
   in per list, so an app's rows no longer show the selection and a selection move makes no rows again? A
   visual API: fill, optional ring, corner radius, drawn under the row. Junction History −30 % bytes per key,
   Tags −54 %, suite item-row walk −36 %. [Yes, opt in, adopted one list at a time like item rows.] → **yes, opt in** (2026-10-25).
2. **a11y-overlay-live:** may the bridges tell changes inside open context menus, dropdown lists, dialogs and
   the command palette (selection moves, name/value and children changes; menu items get real bounds)? New
   UIA and AT-SPI events for changes that are silent today. [Yes.] → **yes** (2026-10-25).
3. **TextArea document as one string** (declined in rounds 3 and 4: a rope changes the public `Text`): new
   evidence — typing above 42,500 characters triggers a gen-2 GC every 14–17 keys at app-sized heaps. [Not
   now: take the LOH-threshold half of runtime-gc-gen0-budget-and-loh-threshold first; revisit chunked
   storage only if documents above ~256k characters matter.] → **threshold first; add option B to Waiting**
   (2026-10-25: keep `Text`/`OnTextChanged`, add a read-only `Document` view and `OnDocumentChanged(TextChange)`;
   options A same API / B additive / C breaking were written up for the user).
5. **junction-history-row-selection-pixels** (from r5 step 9): Junction History can adopt the list-drawn selection
   (survey −30 % per key) only if two things move out of its rows, which changes pixels: the caret row's "Details →"/
   "Compare →" affordance (it sits in the row's flow, so that row's subject ellipsis comes earlier) and the hover tint
   (unselected rows tint under the pointer, the selected one doesn't). May the affordance become an overlay at the
   row's end and the list own the hover tint? [Ask when convenient; not blocking.] → **open**.
4. **vesper-detail-resize-stale** (not perf): in Detail view a resize never rebuilds, so the path trail's
   truncation stays stale until the next key; e6a432fa's fit comparison runs only in grid views. Fix it in
   step 8? [Yes, small.] → **yes** (2026-10-25).

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

From 2026-10-26; earlier entries are in [the archive](17-performance-team-ledger-archive.md#log-2026-10-07-to-2026-10-25).

- 2026-10-26: round 5 step 4 landed (e5ff3fea, 09abf712, dff306df): the extent map no longer keeps dropped runs
  alive (−2.3 MB, −36 % heap after a large Cascadia document), and full blob maps keep the last two frames' blobs
  (dense terminal screens rebuild 0 blobs per frame instead of thousands; −23 % on the new row). WSL passes. Guideline
  notes: a long-lived cache keyed by object identity keeps those objects alive — key per-face data by glyph, not run;
  a bounded cache needs a working-set sweep (frame stamps, skip when nothing is old, doubling hysteresis, hard cap),
  not a full clear; tests that draw thousands of new items in one frame look like one giant screen — draw in frames.
- 2026-10-26: round 5 step 5 landed (ce70fec7..93226dda): text areas, checkboxes, radios, switches and the rich text
  editor keep layout across a no-op rebuild (Meridian URL typing −8…−10 % frame, Junction summary typing −5…−11 %;
  fixes a stale padding after a theme switch), and RowHeights lists stop allocating offsets per rebuild (−9 % Vesper
  find typing). Audit, pixels, all suites and WSL pass. Guideline note: a control that writes its own box in
  `ApplyTheme` must compare and mark layout, or a theme switch keeps the old box; then both patch flags take the
  layout pass out of every rebuild.
- 2026-10-26: round 5 step 6 landed: the apps raise the GC's large-object threshold to 512 KB (828a7d69; typing in an
  89k-character document: gen-2 GCs 103 → 2, pause −48 %, frame p99 −25 %); the gen-0 cap declined at 6–12 MB
  (b1323e9d: Junction's highlight worker made it cost memory and pauses). Guideline notes: set GC knobs through
  `RuntimeHostConfigurationOption` `System.GC.*`, not ILC runtimeopts, and verify in the exe; measure memory settings
  on heavy real data; frame p99 over ~100 inputs is one frame — use 300+ inputs, per-key work percentiles, total pause.
- 2026-10-26: round 5 step 7 landed: 8-bit video frames keep their target with a lazy acquire barrier (7f9df4ea:
  −96 % bytes per frame, mpv draw flat, validation clean), 8-bit drags keep their targets (6c627f27: −76 % per step),
  and Meridian's first-use stall traced to NVIDIA's GL driver walking every thread inside a glyph-atlas upload after
  the process gains threads (ba8fb89d, Waiting: gl-driver-thread-walk; Vulkan has no such stall; a fix would be an
  NVIDIA profile with Threaded optimization off — a user decision — or no thread creation on GL interaction paths).
  No new `nvlddmkm` events; no F16 kept-target drag run. Guideline notes: GL on NVIDIA can stall 50–160 ms on the
  first GL-heavy frame after a process gains threads; time a barrier's placement with an eager control and 3+ batches;
  set clips on kept canvases in device pixels.
- 2026-10-26: round 5 step 8 landed (b1a12ef1..1ed9d938): Junction review typing −85 % per character, Meridian History
  −69…−81 %, Vesper Insert −36 %, Junction's commit file list virtualized (−93 % per key, −27…−31 % time), History
  bars kept (−42 %), Vesper Detail resize updates the path trail. Guideline notes: a rich text editor raises
  DocumentChanged and SelectionChanged per keystroke — route both through one signal write; a TextSearch/FindBar made
  with MarkDirty already rebuilds, so don't MarkDirty again in the box's handler; a MarkDirty after a callee that builds
  is a second build; a per-row IndexOf in a one-argument RenderWith makes each key O(n × rows) — use item rows +
  SelectedIndex; a list that can reach hundreds of rows is a VirtualList of item rows (RowHeights from font line
  heights; scroll into view from summed heights). Trap: Python text-mode writes on Windows turn LF into CRLF — use
  `newline=''`.
- 2026-10-26: round 5 step 9 landed: `VirtualListElement.SelectionHighlight` (0d3bc10a; selection walk −35 %) adopted in
  Junction Tags (8157c282: −54 % per key); History and Vesper can't adopt without pixel changes (68817800; round 5
  question 5 asks about History). Guideline notes: a selection shown only as a row background uses
  `.SelectionHighlight(...)` on item rows; keep selection-only affordances and selection-dependent hover tints out of
  rows. Test trap: `HeadlessWindow.CapturePixels()` renders a fresh frame, so a pixel test can't catch a missing paint
  mark — assert `session.PumpFrame()` returns true after the change (HEADLESS.md should say so).
- 2026-10-26: round 5 step 10 landed (76b6f78e, 574f43a7): handlers live in one small array per element with no
  closure each (−12…−20 % per key in Vesper, Meridian, Junction; a handled element retains 35–53 % less; dispatch flat
  or faster). P4's inline-array half built with new evidence; the bitmask stays declined. Guideline notes: handler
  storage costs 0–72 B per element per build now — what remains is the app's own delegate (a capturing lambda costs a
  closure plus a 64 B delegate every build; a non-capturing one nothing); compare AOT micro/row changes against a
  build that leaves the measured code alone (AOT placement moved −1…+6 %).
- 2026-10-26: round 5 step 11 landed (ff6262ff): Meridian's title bar, rail, tree and response pane are kept parts;
  URL typing 237 → 72 KB/key (−70 %), and 938 → 72 at 200 requests (the tree's O(requests) per key is gone);
  48 scenes identical. Guideline notes (now in REACTIVITY.md): a view-only path can flip something a part shows —
  compare before/after and go full only on a flip; a part's own controls can rebuild that part alone through its
  own signal (post-build helpers then run after its builds too); guard a part's build against stale state, since
  outside a handler it can rebuild before the app's rebuild removes it; only parts that never shrank need
  `FlexShrink(0)`. WSL: `NarrowEditorLayoutTests.Tab_titles_and_sub_tabs_stay_on_one_line…` fails on Fedora on
  the baseline cd1b374f too (not this change) — check it in the full-suite step and on CI's Ubuntu run.
- 2026-10-26: round 5 step 12 landed (fcf366c6): Vesper's chrome and per-pane header/trail/footer are kept parts,
  placed from the app's build with `Part.Place()`; Detail key 210 → 34 KB (−84 %), Brief −81 %, partner walk −80 %,
  AOT time −7…−21 %; 93 scenes identical. Guideline notes (in REACTIVITY.md): place parts from the parent's build;
  in the view-only path compare only the few inputs such a change can move; split a part by what changes
  together; set what a build shows before asking for the build. Leads to Waiting: marks in row shape, transfer
  progress rebuilds, Vesper own-selection re-examine.
- 2026-10-26: round 5 step 13 landed (bd88e747..a123f46f, seven commits): a layout-only a11y refresh updates the
  applied tree in place; with a reader, scrolling and moving cost ~0 KB (wheel UIA −99 %, item-row walk −78 %,
  Reel 1 tree/s → 0, AT-SPI reader walk 400 → 9 KB/step); 0 mismatches over 960 audited refreshes in seven suites.
  An in-place walk costs 88 % of a build plus diff in time: the win is allocation and bridge work, and a walk that
  keeps failing must stop being tried (577949d9). Guideline note: a refresh that only moves or scrolls things costs
  about 0 KB with a reader; changing a list's item count or structure during layout still rebuilds the whole tree.
- 2026-10-26: round 5 step 14 landed (6483f85f..b6de3fcf): lists over 2,000 items now tell selection moves on both
  bridges (0 → 80 events per 40 moves, matching the small-list path) and AT-SPI serves their items (ChildCount 0 →
  3,000); 0 trees per move. The survey's "7 two-node trees per 40 moves" were coalesced layout refreshes, already
  in place since step 13. The .NET runtime updated mid-step (10.0.13 → 10.0.14): AOT publish then needed
  `--disable-build-servers`. Guideline note: a served item's id packs its list's owner slot — a test must read an
  item's path before unmounting its list. All 14 round 5 build steps landed. User (2026-10-26): after this round's
  release, stop the loop for a reboot, then measure the total improvement of all rounds before round 6.
- 2026-10-26: main (d5eaefc9: async-load retry + held-sheet test fixes from other sessions) merged into the branch
  as 7ef01769. Round 5 full suite on it: Windows `dotnet test HultUI.slnx -c Release` 31 projects, 0 failures
  (UiTests skipped by their gate); WSL Fedora slnf 22 projects, 0 failures. Phase → release.
- 2026-10-26: round 5 merged into main as 61308cfd (detached origin/main merge, tree identical to the tested
  branch, gate held), pushed d5eaefc9..61308cfd, primary fast-forwarded behind the guard. CI run 40406538950 red on
  Windows only, two test faults from this round, no product bug: (1) step 13's scroll-view torn-read test never
  wrote a starting set, so a reader that ran before the first write (2-core runner) counted the built tree's values
  as torn (293,354; a 100 ms sleep reproduced 1,314,523 locally; with the start written it passes even with the
  sleep) → 6e51acad; (2) step 6's `AppRuntimeSettingsTests` in Meridian.Tests read the CLI's
  `meridian.runtimeconfig.json`, which `ReferenceOutputAssembly="false"` still copies and which is the GUI's file
  on Windows → `Private="false"` on the CLI reference, f31194ab. Pushed 61308cfd..f31194ab; CI run 40409675144 green
  (test, test-linux, docs). Guideline notes: a thread test starts from a state its reader accepts; a referenced
  exe's runtimeconfig/deps/apphost are copied unless `Private="false"`. Loop stopped for the user's reboot.
- 2026-10-27: total-improvement comparison after the user's reboot, baseline 520002e9 vs after (code of f31194ab),
  idle-gated, pinned, alternated, JIT + AOT (captures 3c2afc67; report `17-reports/total-r1-r5.md`). Perf suite, 83
  shared rows: 0 slower, summed time −25.5 % JIT / −28.7 % AOT, allocation −63…−64 %. Apps: bytes per key −51…−99 % on
  23 walks (−90 % summed), CPU per key −26 % geomean AOT, real-window work per key −24…−64 %, idle 75 → 0 wakes/s,
  memory after a walk −10…−22 %, Meridian 89k-body typing gen-2 127 → 1; start-up unchanged. 56 real-window launches:
  no foreground events, no stalls, no new nvlddmkm events. Baseline worktree removed. Loop still paused for the user.
- 2026-10-27: token economy (user approved items 1–5): the ledger split into this file (56 KB, was 192 KB) and
  `17-performance-team-ledger-archive.md` (rounds 1–4 Landed rows, round 5's build order, rounds 1–4 questions,
  log to 2026-10-25; 218 Landed rows and 83 log entries checked before and after); the brief gained "Keeping token
  use down" (excerpt briefs, quiet commands, 3600 s fallback wake-ups, read once per session) and release step 5
  (each round starts in a fresh context). Round → 6, Phase → survey; round 6 starts in a cleared context.
