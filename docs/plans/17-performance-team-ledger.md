# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 4
- **Phase:** build — round 4; surveys in (`17-reports/r4-*`, all eight not satisfied); steps 1–4 landed, step 5 next.
  All 4 round 4 questions answered 2026-10-13 (the user took every recommendation). To
  resume after a pause, run in this worktree: `/loop Run the next step of the performance team:
  follow docs/plans/17-performance-team.md, with state in docs/plans/17-performance-team-ledger.md.
  Read both, do the step the ledger's Phase calls for, record it in the ledger, and commit.`
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`; the session
  moved here from `claude/ui-framework-performance-a2cca6` after a restart)
- **Review commits since:** b3e00b0 (round 5 reviews all of round 4's build)
- **Probe code kept for the build phase** (session scratchpad): round 4 surveys `r4layout/`,
  `r4paint/` (`probe-r4.patch` with PROTO_RUNINK/ROWSTR/VIEWFRAME), `r4gl/`, `r4rt/` (wakerace,
  latency host, blobprobe), `r4text/`, `r4react/` (R4_PARTS chrome split, R4_REVIEW_SHAPE,
  R4_TAGS_ONCE), `r4a11y/` (reorder prototype, live WSL driver), `r4apps/` (app probe, keyup.py);
  round 3 builds `r3b2/`…`r3b17/`, `r3b9b/`, `r3b14b/`; older `probes/`, `l1r3/`, `r3*`.

**Repository move (user, 2026-10-24): HultUI moved to `github.com/fredrikhu/hultui`; the old
`hultui-revamped` will be deleted. Never push to the old URL again.** The new main has the same
history (a9081035 on both, checked). At round 4's release, before merging: `git remote set-url
origin git@github.com:fredrikhu/hultui.git`, `git fetch --prune origin`, then merge and push to the new
origin as usual (the remote is shared with the primary checkout and other worktrees — that is the
user's intent). No local `pacs-support` branch exists (checked 2026-10-24); if one appears, delete it
(`git branch -D pacs-support`), never push it (replaced by app-support). Open PRs/issues on the old repo
don't carry over. Plan 17 adds nothing PACS/Sectra/healthcare-related (round 4 diff and plan docs
checked); keep it that way for the open-sourcing. **The new repo will be public and will NOT get
self-hosted runners (user, 2026-10-24: anyone could push code that runs on them); it uses GitHub-hosted
runners.** Every workflow is hard-wired to `[self-hosted, …]` today (0 runners on the new repo; CI run
40030590762 on a9081035 queued since 2026-10-23), so round 4's release waits until the workflows run on
GitHub-hosted runners — offered to the user as a separate task (chip task_f35e3acf, "Move CI to
GitHub-hosted runners"). Plan 17 never registers a runner. Hosted runners are slower and differ in fonts
and tooling: expect new CI precondition failures like 58c186f's, fixed in tests, not by adding runners.

**App releases:** cut only when the user asks (they asked after rounds 2 and 3; round 3's on
58c186f: junction-v1.21.0, vesper-v1.21.0, meridian-v1.4.0, aperture-v1.20.0, reel-v1.5.0). Follow
memory `release-tagging` (≤3 tags per push).

## Verdicts

| Expert | Round | Satisfied | Review objections |
|---|---|---|---|
| 1. Layout | 4 | no | none |
| 2. SkiaSharp: painting | 4 | no | none |
| 3. GL and Vulkan | 4 | no | none (validation trap: a probe ending with WM_CLOSE never reaches vkDestroyDevice — dispose the Application) |
| 4. .NET runtime | 4 | no | 1521f47 with 3a539f8: a wake can be swallowed between `_wakePending`'s clear and the poll, and since the infinite idle wait the post waits for the next input (3/3 dummy-driver runs hit the 2,000 ms cap) |
| 5. Text pipeline | 4 | no | none |
| 6. Reactivity and reconcile | 4 | no | e0b816f: a Brief/Thumbnails resize rebuilds the whole app every step (520–750 KB vs 2.3 KB in Full) |
| 7. Accessibility bridges | 4 | no | none |
| 8. App workloads | 4 | no | none — round 3 helped four apps (Junction −41 %, Vesper −48 %, Aperture −30 %, Reel −93 % UI-thread bytes per session; idle 61 → ~0 wakes/s); Meridian flat |

## Queued candidates

Rounds 1–3's build orders all landed or were done (see Landed; round 3's step 15 change 4 reverted,
97ff373). Round 4's surveys (reports `17-reports/r4-*`) re-ranked everything. A build step may bundle
one expert's related small candidates as separate commits. Items marked **(user)** wait for a decision
in Needs the user; the rest of their step goes ahead.

### Round 4 build order

Correctness first, then value per cost.

1. ~~**Runtime fix-ups**~~ LANDED ec4ced5, 397eca4, 94fc1ef (4): **runtime-sdl-wake-flag-after-poll** (a wake landing between
   `_wakePending`'s clear and the poll's last `SDL_PollEvent` is swallowed and the flag stays set, so
   later wakes return early; since 1521f47 an idle loop then waits for the next input — dummy driver: 3/3
   waits ran to the 2,000 ms cap vs 51 ms; clear the flag where the poll consumes the wake;
   SdlWindowingPlatform.cs:83, SDL3Window.cs:760/:834, :132), **test-frame-pacing-event-precondition**
   (fails at the desk's 200 Hz: 516 events, 606 needed; storm cadence independent of `Sleep(1)`),
   **tooling-textbench-warmup** (warm until the compiled-method count stops rising; 922–928 compiled
   during measurement). Small.
2. ~~**events-noop-input-frames + apps-vesper-passive-move-handlers**~~ LANDED c0560e1, 82ee093, 4000186, ea4e01a (4/1, 8) — any key-up, any wheel
   notch that scrolls nothing and any other non-move input forces a full repaint (Window.cs:2133-2138):
   the up frame is 42–46 % of a key press's frame time in Junction and Vesper; repaint only when an
   ordinary handler ran, hover changed, a drag is active or something marked itself dirty (audit owned
   handlers acting on key-up or wheel). Vesper's root and pane-row PointerMove handlers
   (CommanderApp.cs:745, CommanderApp.Views.cs:452) and Junction's review rows (JunctionApp.Review.cs:687)
   go passive: 60 idle moves → 0 frames. Suite row: key-down and key-up apart, frames per press.
3. ~~**a11y correctness**~~ LANDED 968bd8d1, 1698b4a6, 9218026a, 78f3a22b (7): **button-isenabled-rebuild** (approved; IsEnabled set on the live element is
   never told), **atspi-reorder-signal** (approved; one children-changed:add per moved child in index
   order — live 17/17 cases match with the prototype in the scratchpad `r4a11y/reorder-proto.diff`),
   **a11y-uia-host-reads-off-thread** (UIA reads answer from the node, not a live-tree walk on a UIA
   thread — 1,500 visits per read on a 1,505-element page; RangeValue pattern and property agree;
   RaiseScrolled's walk; and the Linux status subscription before the first read).
4. ~~**a11y-text-caret-patch + a11y-text-caret-shares-text**~~ LANDED 1e1948f9, 92beae04, 07ed97e3, 103ad672, 30d19928, a0221337 (incl. a11y-textarea-text-pattern) (7) — a rich-text caret move patches the
   applied tree instead of building it (beside 300 rows UIA 79.0 → ~0.5 KB/key, AT-SPI 319.1 → ~1); with a
   reading client the next text version shares the previous tables (5,000 blocks 474.5 → ~2 KB per
   move). Suite row (caret walk in rich text, bridge on and UIA). Then, if it fits,
   **a11y-textarea-text-pattern** (approved rule; must follow the caret patch).
5. **Junction** (6/8, app): **apps-junction-components** (approved: titlebar, sidebar, toolbar as
   persistent `ChromePart`s read through `_chromeRevision`; view-only paths opt out — the r4-6 design;
   −40…−75 % per key on six walks, AOT app: chrome 128.6 KB per build), **apps-junction-tags-double-rebuild**
   (Tags key rebuilds twice, −43 %), **junction-review-rows-shape** (review mode remakes ~30 rows per caret
   key, ~1 MB/key → −65…−70 %), and settle Junction tests that read app state across frames against the
   git lock-file events (DiffItemLabelTests failed once in CI; `GIT_OPTIONAL_LOCKS=0` for read-only git in
   fixtures or 3be0dea's Settle). Suite row: a walk beside unchanged chrome.
6. **Media frames** (3/4): **media-video-frame-bytes** (Vulkan video frame 527 → ~170–220 B: kept SKImage
   over the interop image keyed by VkImage, a cached Action in GpuFrameSink, dispose the per-frame
   `GRBackendTexture` — one finalizable object per frame today), **media-modal-loop-video-rate** (inside
   the modal size/move loop, answer a GPU source's repaint with a frame, skipping within 20 ms of a size
   change: 120 fps clip during a move 57 → 169 frames, drops 50 → 0; the subclass on GL windows too),
   **vk-drag-kept-step-remakes** (keep the F16 offscreen in the drag bucket: 82 → 10 creations, 81 → 9
   sync submits per 80-step drag). Suite row: Vulkan steady-frame bytes (Windows real window).
7. **VideoPlayer and Reel** (6): **reactivity-scrubber-preview-in-place** (preview card through Refs:
   suite scrubber hover 420.7 KB/op, ≥ −80 %), **reel-still-previews-rebuild** (a public
   `VideoPlayer.RefreshPreview()`; 120 → 0 app rebuilds per file, and the card updates under a resting
   pointer), **reactivity-computed-pooled-notify** (Computed notifies through a rented snapshot: 64 → 0 B
   per upstream write).
8. **Text** (5): **text-prefix-table-transient** (the TextArea's break search gets a view over kept
   scratch: long-document open 1,373 → ~440 KB/op, log open 65.8 → ~5 MB, wrapped resize −77…−98 %),
   **text-paragraph-construct-diet** (selectable viewer wheel −27 %, rich text −7 %),
   **text-textarea-mono-ascii-fits** (approved 2026-10-13) (JSON open −86 %, −4.15 MB retained). Suite row "text/resize a
   wrapped TextArea".
9. **paint-subblob-variable-fonts** (2) — per-run glyph bounds for faces whose box Skia distrusts
   (Cascadia Mono/Code, Segoe UI Variable, Bahnschrift): glyphs per frame −84…−92 % on viewer, JSON and
   terminal screens. Pixel A/B with an A/A control (DirectWrite regenerates glyph masks after strike
   purges) and on WSL Fedora. Watch expert 4's lead: 7.5 native blobs per newly drawn spanned line vs 1.1.
10. **Small paint and controls** (2/1): **terminal-row-strings** (1.6–6.0 KB per terminal frame → 0) +
    **richtext-viewframe-record** (24 B → 0), **aperture-hero-scrim-seam** (approved: end at 0 %),
    **paint-menu-rows-outside-window** (cull a context menu's off-window rows: 200-item dropdown 278 text
    items per frame, 173 outside). Suite row: terminal coloured screen, forced frame.
11. **a11y-leaf-property-patch** (7) — on a frame that would rebuild only for leaf property changes, remake
    those nodes and patch (TextInput UIA 53 → ~3–4 KB/op, checkbox 87 → ~1; incl. range values: Reel's
    scrubber refresh is 3 full a11y builds a second, half of playback bytes with a reader). Suite row: the
    real-loop a11y walk at a reader's pace (500 ms).
12. **layout-themed-patch-keeps-layout + reconcile-restyle-on-change (a)** (1/6) — themed controls and
    item-row VirtualLists keep layout across a patch that changes nothing (Junction Commit layouts per key
    20.7 → 1.1, MeasureSize 1,281 → 85); a patch marks only the patched element for style, resolving down
    only where a style changed (resolves −50…−80 % per key). Part (b) waits.
13. **Vesper** (1/8): **layout-vesper-partner-row** (`VirtualListElement.InvalidateRow(int)`; same-name
    walk −42 %) and **vesper-grids-rebuild** (Thumbnails on item rows, Brief entries kept, resize rebuilds
    only when the grid's geometry changes: −55…−65 % per key, ≥ −90 % per resize step — e0b816f's cost).
14. **Real-window test hygiene** (3): **sdl-hint-honoured-placement-and-uitests-guard** (the framework's
    maximize/full-screen/restore under a CBT hook when the hint is set; SWP_NOACTIVATE on full-screen;
    ForegroundGuard on the 13 unguarded UiTest classes), **foreground UiTests opt-in** (approved: e.g. `HULTUI_UITESTS_FOREGROUND=1`).

### Waiting

Re-ranked by the round 5 survey.

- **richtext-short-viewport-jitter** (5/editor, from step 4) — when the rich text editor's viewport is shorter than
  one line, each caret move scrolls it between two offsets.
- **terminal-noop-scrollback-wheel** (2/Terminal, from step 2) — Terminal raises ContentChanged (a frame) on a
  scrollback wheel notch at the top that scrolls nothing.
- **a11y-virtual-items-and-selection** (7; added events approved) — lists over 2,000: AT-SPI ChildCount 0,
  moves told to nobody; with a11y-uia-host-reads-off-thread's container node.
- **a11y-atspi-snapshot-per-diff + apps-scenario-atspi-rows** (7) — 271 B per node per apply; AT-SPI steps
  3.5–4.2× UIA's. Medium–large.
- **a11y-layout-refresh-bounds-only** (7, from 8) — with a reader, a layout-due refresh rebuilds the whole
  tree (History at a reader's pace 113 KB per key, 30 %); re-snapshot bounds or rebuild moved subtrees.
- **apps-meridian-components, apps-vesper-components** (6/8; one app at a time after Junction).
- **meridian-response-frame-flush** (2/3, lead) — the 18,884-line response frame flushes 86–152 ms;
  needs a trace.
- **junction-commit-file-switch** (8, lead) — walking a large commit's files 2.72 MB per key; profile.
- **reconcile-restyle-on-change (b)** (6/1) — after step 12's part (a); needs the 32-override audit and a
  rule that a never-resolved element always resolves (a prototype drew an unresolved Text).
- **text-open-measures-every-line (full)** (5; user: not now, 2026-10-13).
- **vk-startup-preload** (3, low) — at the 10 ms edge; measure on AOT first.
- **layout-ordered-children** (1, low) — suite rows only now.
- **apps-tokenize-on-ui-thread** (5/8, low).
- **paint-drawspannedline-overlap** (2, lowest).
- **Suite scenarios** (8): document-switch, image-grid.
- **Leads:** a VirtualList measured as a leaf (1–2 %); Vesper zebra tint O(n) per row made (off by
  default); one prefix table per hard line (after step 8).
- **aperture-poster-as-hero**, **aperture-tmdb-size-fallback**, **junction-narrow-gitlab-glyphs**,
  **meridian-pane-choice-not-persisted**, **meridian-min-width-wraps** (apps; not perf).

## Landed

| Round | Commit | Candidate | Before → after |
|---|---|---|---|
| 1 | 7a827e9 | gl-vk-swapchain-rebuild-leak | Not a real VRAM leak (Task Manager bookkeeping; nvidia-smi and DXGI flat). Real cost: ~100 ms first-present stall per chained scRGB MAILBOX rebuild. Destroy-first for scRGB MAILBOX on Windows: resize 112 → 29 ms median, resize CPU 453–480 → 198–206 ms/s, stretched frames during resize 72 % → 0, Task Manager 1,416 → 173 MB per 40 resizes. IMMEDIATE, FIFO, 8-bit keep chaining. |
| 1 | 0f5816a | gl-no-depth-buffer | `SDL_GL_DEPTH_SIZE` 0, stencil 8 kept: GPU per GL window 55.3 → 46.8 MB at 2100×1350 (−15 %), 131.9 → 109.5 MB at 3818×2032 (−17 %); Task Manager, DXGI and nvidia-smi agree (~3 B/px). Start-up unchanged (probe first frame 427.8 → 427.2 ms, Junction 480.7 → 480.5). Junction, Vesper and a stencil-dependent FBO 0 scene byte-identical; a stencil-0 control broke the scene. |
| 1 | 17e3de4 | paint-text-zero-alloc | Index loops on the text draw path, `PrefixMeasurer` closure moved out, pooled corner `SKPath`. allocKB −10…−96 % on 43 JIT / 44 AOT rows; dense text 18.1 → 0.8 KB/op (AOT 29.8 → 12.5); TextInput keystroke 44.9 → 2.5; render pass ~0.1 KB/frame on text screens; time flat; goldens byte-identical. |
| 1 | 8ab877c | runtime-aot-trace-type-name | Element trace names looked up only while recording (internal `FrameTracer.BeginElement`). AOT allocKB −10…−99.6 % on 40 rows, none up: resize 2,000 rows 685.6 → 2.9 KB/op (time −16 %), TextInput typing 59.0 → 2.4 (−10 %), deep nesting 75.5 → 4.8 (−12 %), tiles 7.7 → 0.7; AOT per-frame bytes now equal JIT. JIT flat. HULTUI_TRACE output identical event for event. Junction AOT frame not re-measured. |
| 1 | 12d1480 | paint-image-zero-copy | `SetImmutable` on hand-over: SkiaImageSource's image shares the bitmap's pixels. Private bytes: Vesper 216 thumbnails 83.6 → 66.3 MB (−21 %), Aperture real library (27 posters) 85.0 → 67.3 MB (−21 %), GL window with 8 × 3840×2400 photos 1001 → 723 MB (−278 MB). Extrapolated up to ~260 MB at Aperture's 400-poster cap. Time and allocKB flat on all 83 suite rows; goldens byte-identical; GL readback exact. Vulkan not checked. |
| 1 | f84893d | aperture-poster-decode-scale | Aperture artwork requested at layout size × display scale (25 % steps), by the side that decides a cover fit; refetched on scale change with the old picture as stand-in. Real library, 27 pictures: decoded 17.6 → 4.4 MB at 100 % (−75 %), 9.9 MB at 150 % (−44 %), unchanged at 200 %; private 63.8 → 42.9 MB (−33 %) at 100 %, 63.2 → 54.1 (−14 %) at 150 %; Skia mip cache 5.9 → 0 MB. At the 400 cap 258 → 65 / 145 MB. As sharp or sharper (≥1 source px per device px). Same fix for cast, chapters and request-server pictures. |
| 1 | 4195da4, 7096431 | a11y-uia-wrapper-rebase | Every surviving UIA wrapper points at the current tree after each diff; stale wrappers dropped; no per-diff full-tree index. Trees held after 200 toggles 221 → 1 (30–34 MB → 0 retained); ApplyChanges 47–211 → 0.3–1.4 KB per diff; new Windows-only perf rows with the real UIA bridge on a hidden HWND: allocKB −22 / −29 / −30 % (JIT and AOT), label edit min −5.4 %; 0 slower. UIA event stream identical (1,118 events); sibling navigation now current. |
| 1 | 54631d4, 8aed816, 646139a, 9af7d75, 480b141, 65a01a3 | runtime-frame-floor (+ layout walk half, hover common ancestor, slider/number-field paint) | Steady and idle frames 0 B (tiles, dense text, tweens 0.6–0.7 → 0.0 KB/op; control screen repaint 1,296 → 0 B); Tab 28.4 → 0.3 KB (AOT 34.1 → 0.3); theme switch −62/−56 %; hover −83 %; component rebuilds −5.6 % alloc, −5 % time; 32 rows ≥10 % down, none up, JIT and AOT; 0 slower. Junction/Vesper AOT frames not re-measured. |
| 1 | 0178784 | text-word-alloc-diet | Word cache looked up in place by a span (kept alternate lookup, per-run font hash), exact glyph arrays, widths without glyphs, LRU node reuse. allocKB −10…−60 % on 17 rows JIT and AOT, none up (text/ellipsis 7,261 → 3,418 KB/op, text/wrap 3,093 → 1,572, richtext/open 18,172 → 11,211, 600-span paragraph 1,924 → 776); 0 slower; text/open −10 %, richtext/open −26 % min; output bit-identical over 1.78 M layouts; goldens byte-identical. |
| 1 | e962738, f3a31d6, 5eebbf6 | runtime-text-services-per-window | One SkiaTextServices per application (SDL, WinForms, headless; pinned set separate), disposed with the platform; surfaces dispose only services they built. 600 open/close cycles: +4 font streams, +44 KB private, +1 KB managed per cycle → flat. WinForms handle recreation 808 → 3 streams after 200 cycles. New row window/open a second window 0.500 → 0.265 ms min, 262 → 132 KB/op; 0 slower on 86 rows. Windows share bounded caches now (one UI thread only). |
| 1 | 1785d16 | gl-vk-offscreen-scratch-pool | Scratch purge after the frame that follows a let-go F16 offscreen. GPU memory: 4-size cycle DXGI 215.4 → 154.1 MB (Skia pool 57.1 → 0.1), 40-step drag 458.5 → 242.5 MB (pool 203.0 → 0.1); nvidia-smi agrees. Resize time unchanged on drags and two-size toggles; +0.5 ms on new sizes, +1 ms (3 %) cycling 4 sizes. Unbudgeted alone changes nothing. GL has no such offscreen. Validation 0 messages, positive control 40. Trap: `GetResourceCacheUsage` counts only budgeted resources. |
| 1 | e3cff87 | a11y-build-diff-garbage | Exact-length child arrays (leaves share an empty one), index walks, positional pairing until the first difference, one owner-slot lookup per list. Build 230 → 144 B/node; unchanged diff 121.7 KB → 176 B (3,001 nodes). a11y/edit every 10th 568 → 368 KB/op (−35 %), time −9…−10 %; UIA rows −41…−47 % alloc, label edit −8…−12 % time; JIT and AOT; 0 slower. Diff identical vs the old differ over 3,300 random pairs; UIA event stream identical (1,118 events); Linux a11y tests pass. |
| 1 | 4c3e3b1 | a11y-uia-reparent-wrapper | An element that moves to another a11y parent keeps its UIA wrapper; AT-SPI sends no cache-remove for a moved object and one children-changed:remove per moved child. Before, navigation to moved elements returned null and their events were dropped (6 of 16 in the reparent script). Other event streams identical (UIA 1,120, AT-SPI 6,886); allocation unchanged. |
| 1 | 93847ae | (measurement) | Windows-only perf rows `a11y/tab through 300 fields, UIA` and `a11y/wheel 1,000 rows, UIA`. |
| 1 | 70d68c1, 0b2b003 | a11y-focus-state-on-tab | A focus move patches IsFocused on 2 nodes of the applied tree. UIA raises HasKeyboardFocus old/new before the focus event (was: at the next rebuild); AT-SPI answers current focus and drops its late duplicate signals. Allocation unchanged; Tab with UIA 0.351 → 0.350 ms. |
| 1 | ab8917b | a11y-scroll-bounds-event | Scroll view nodes carry ScrollOffset; UIA raises Vertical/HorizontalScrollPercent on the view (Chromium's mapping, `browser_accessibility_manager_win.cc`) instead of a BoundingRectangle per on-screen node: 846 → 6 events, provider callbacks 11,844 → 84 per 30 notches; bounds and offscreen read live. a11y/wheel 1,000 rows, UIA −54 % min, −62 % median. Node still 136 B (packed states). AT-SPI unchanged (sent nothing before). |
| 1 | 6891682 | layout-truncation-clamp-basis | A MaxLines(1) label in a row starts at min(untruncated width, room), from a pinned MeasureCache entry (no new element bytes). Ellipsis rows −19…−42 % time JIT, −20…−43 % AOT, allocation −21…−52 % (text/ellipsis 4.75 → 3.32 ms, 3,418 → 1,887 KB/op); measurements per step halved; 0 slower. Pixels: ellipsis slack ≤ 0.89 px; zero-room cells draw nothing instead of overlapping. Goldens byte-identical. |
| 1 | 322df92 | layout-truncating-min-zero | A MaxLines(1) label's automatic minimum is 0 (nested boxes too); a label too narrow for its ellipsis draws nothing. Perf flat JIT and AOT. Fixes: Junction Branches name/tag overlap at 1200 px, Junction sidebar repo name/path running past the edge, Junction Tags subjects over the date column, Meridian's Send button off-window with a long URL. Exposed: Meridian's request tab titles and sub-tabs wrap at ≤1100 px and overlap the URL bar (follow-up 11e-fix); Junction's FlexShrink(0) "current" tag still overlaps at 1000 px. Goldens byte-identical. Images in scratchpad `truncation-minzero/`. |
| 1 | be1fd9d | apps-truncation-followups (Meridian) | Request tab titles one line (ellipsis, ≤220 px, narrow to 72 px, strip clips); sub-tabs one line in a sideways-scrolling strip; URL box clips; Chain, Assertions, History, Environments labels fixed. At 1100 px the editor gets its designed 228 px and the response pane fits (before: titles 4–11 lines deep over the URL bar, pane 179 px off-window). Breakpoint left at 1100 (asked the user). |
| 1 | 351e6cd | apps-truncation-followups (Junction) | Branch name cell clips; the name truncates first, then current/base/head/remote tags show whole or not at all; LAST COMMIT heading one line. No overlap at 1000/1200/1400 px. |
| 1 | 6cf4fe6, 17e94b0 | aperture-hero-scale | Heroes requested at hero box × display scale (25 % steps), by the cover-fit side, rounded up to a 128-device-px step; stand-in on scale change or step; TMDB w1920 above w1280; only the last 4 heroes kept. 150 %: item hero 0.699 → 1.022 source px per device px, request hero 0.639 → 0.958; item page private 62.1 → 67.6 MB (hero 4.2 → 9.0 MB). Resizing 7 steps 96.9 → 66.0 MB (8 heroes → 1); 7 pages 137.6 → 96.8 MB (106.1 before 11f). Request page at 100 % +7.4 MB (w1920, strict rounding kept by the orchestrator). Measured against the real Jellyfin and Seerr. |
| 1 | 5b2b676 | meridian-pane-breakpoint | Response pane goes to the bottom below 1320 dip (rail 48 + tree 264 + editor 448 + pane 560); 1100–1319 px windows put the pane at the bottom (at 760 tall: pane 380, editor full width). Ctrl+\ unchanged; plan and README updated. Exposed: the bottom pane is a fixed 380 px, so a 1280×672 window (1080p at 150 %, maximized) leaves ~90 px of sub-editor (follow-up 11g-fix). |
| 1 | 2bca842 | junction-branches-compact | Branches/Tags compact below 934 px of content (date only, ↑/↓ and eye headings, icon Checkout named "Checkout"); below 724 px LAST COMMIT/COMMIT move to the tooltip. BRANCH at 1000 px with the sidebar 48 → 165 px, 1200 px 160 → 277, 900 px 0 → 196. Narrow mode also hides the GitLab lock, pipeline glyph and MR chip that live in LAST COMMIT. |
| 1 | 53442e2 | meridian-bottom-pane-height | Bottom response pane keeps 380 dip in windows ≥ 736 tall and gives way below, with an editor floor of 316 (chrome + five form rows) and a pane floor of 162 (flex minimums, no rebuild on resize). 1280×672 editor 252 → 316 (header rows 3 → 5), 1100×600 180 → 316, 760×540 120 → 316; 1100×760, 1200×800, 1400×900 unchanged. No splitter (design token; Meridian has no settings file). |
| 1 | 1c8ee98, 758fa03 | apps-scenario-virtual-selection-walk-a11y + apps-scenario-textarea-highlighted-json | 5 perf-suite rows (94 scenarios now): virtual/selection walk 1,000 rows (+ a11y bridge on, + UIA), text/open and text/wheel highlighted JSON (19,013 lines, 22,968 spans identical to HultUI.Syntax). JIT min 0.466 / 0.534 / 0.565 / 10.74 / 0.939 ms, 108 / 412 / 412 / 10,811 / 14 KB; realize ~10 % and a11y 12–18 % of the walk step (below the app survey's shares: 5 elements per row vs ~14 in Junction). JSON open median ≈ 2× min from GCs. |
| 2 | 0ceecea | runtime-sdl-pump-closure | A real SDL loop pass allocates 0 B instead of 88 B (AOT 72). GL idle 5.5 KB/s → 0; GL spinner 3–4 MB/s → 0, no GCs; Vulkan idle 11.7 → 6.2 KB/s (display-colour poll left), spinner 4.45 → 0.41 MB/s (render target per frame left). Test on SDL's dummy driver runs in the normal suite. |
| 2 | ff7a970, 2612de1 | runtime-paced-wait-spin | SDL on Windows rounded waits down (1 ms → 0); the wait now asks 1 ms more. Spinner 45–49k → 420–447 loop passes/s (224–245 → 2.1–2.2 per frame); UI-thread cycles −39…−57 %; frames/s 200 → 200 on GL and Vulkan, JIT and AOT; live resize still draws inside every step; headless suite flat. Linux/macOS not changed (SDL rounds correctly there per its source). |
| 2 | 82e382b, 3766d78 | vk-minimized-swapchain-lost | A Vulkan window that drew while minimized draws again: 0 → 26–31 frames in the second after restore; resize ErrorNativeWindowInUseKHR → 29–31 frames (F16 MAILBOX, 8-bit, FIFO). Minimized and dirty 252–264 → 47–49 loop passes/s; idle after restore 401–403 → 61. **Minimized windows are no longer drawn** (GL and Vulkan): GL with a 30 Hz change 129 frames per 5 s, 47–56 ms/s → 0 frames, 9–13 ms/s. Validation 0 messages (before: a leaked VkSwapchainKHR); resize stretch 0; suite flat. Video while minimized unverified (step 2b). |
| 2 | 7e52ff5 | media-minimized-playback | 82e382b stopped serving mpv while minimized: 236–240 drops per 10 s, avsync up to 151 ms, Reel's next file silent until restore (23.9 s), seeks 235–426 ms. A minimized window now serves its video undrawn (`IGpuImageSource.ServiceUndrawn`: setup + SKIP_RENDERING): 0 drops, avsync ≤ 2 ms, next file 589–612 ms, seek 45–48 ms. Audio was never broken. Process CPU at 1080p24 minimized 63–70 → 105–112 Mcyc/s (still below drawing: GL 162–178); mpv's vo thread does its normal per-frame work. |
| 2 | 27b8803, 95e93b5 | text-textarea-patch-keeps-lines | Same text handed back by a rebuild keeps the line starts (style/width/context/culture changes still re-wrap). New row text/rebuild around highlighted JSON 1.696 → 0.399 ms JIT, 2.051 → 0.424 AOT, 1,414 → 22 KB. Meridian URL keystroke with an 18,884-line response 3.00 → 1.70 ms, 1,612 → 182 KB. Fixed a real bug: row height kept the old font after a font-size patch. |
| 2 | 3a057ea, e7717a0 | text-textarea-span-scan | Binary search on a running max of End (Meridian's spans overlap; End goes down 1,180 times), one reused list. JSON wheel 0.834 → 0.399 ms JIT, 1.584 → 0.412 AOT (AOT gap closed), 14–16 → 5 KB. Meridian wheel with find open 7.4–8.1 → 1.05–1.13 ms (−86 %). Contract: hand a new span list to change spans. Trap: AOT grid rows move 7–16 % with unrelated code layout (README note). |
| 2 | d049be7, d5176b7 | text-shaper-direct | HarfBuzz driven directly, one buffer and font per shaper (in-use guard for shared scratch). text/create 4.24 → 3.26 ms (−23 % JIT, −19 % AOT), 5,792 → 4,317 KB; JSON open −22/−20 %, 10,802 → 6,595 KB; styled bidi ellipsis −16/−17 %, −50 % KB; 6 rows −25…−50 % alloc; TextBench cold/latin −20 %, hebrew −14 %; 0 slower. Bytes per call 936 → 176. Bit-identical vs SKShaper over 72.6 k calls on Windows and 51.3 k on WSL Fedora (Linux lacks Indic/Thai/emoji fonts: compared as notdef there). Handles flat over 10 M calls. |
| 2 | 48823d5, 8a91d93 | text-shape-cache-byte-bound | Shape/word/layout caches bounded by estimated bytes (24 / 4 / 4 MB; estimates within 6 % of measured), LRU by weight, one entry ≤ half the bound. Retained after close: CSV 23.2 → 4.2 MB, 5,000-block rich text 8.8 → 4.5 MB; 19k-line JSON kept on purpose (~22 MB: the TextArea's keystroke store until measure-lines lands). Hit rates identical on all 95 rows; 0 slower JIT; AOT grid moves are code placement (control build). Correction: the survey's 52/155 MB were probe-held documents; real totals 4–26 MB. |
| 2 | 8d30d31, d27ea6b | paint-effect-object-cache | Blur filters (≤ 32) and gradient shaders (≤ 256, keyed by content + rect) kept between frames. paint/tiles shadow 7.3 → 0, gradient 11.8 → 0 KB/op, JIT and AOT; 0 slower; goldens byte-identical. |
| 2 | 103f30e, d27ea6b | paint-blob-builder-reuse | One kept SKTextBlobBuilder: 216 → 136 B per new blob; scroll/jump 10.3 → 6.3 KB/op JIT (13.1 → 9.1 AOT); scroll wheels −17…−33 %; 0 slower. |
| 2 | c85396e | junction-lane-palette | History lane palette built once per theme, index loops: graph paint 16,224 → 0 B per frame (headless fixture). |
| 2 | 93b8fef | aperture-postercache-trim-thread | Correctness: the poster cache's evictions are disposed on the UI thread at the next Get (the download thread disposed images the UI could be drawing). ELEMENTS.md: dispose image sources on the UI thread. |
| 2 | e7a7b03, bf5adcc, fbb5afc, 7755670 | paint-selectable-label-paragraph | A selectable/linked label keeps its paragraph and draws from it. Render-pass bytes per hover frame: Junction diff 10.6 → 0.0 KB (AOT 12.2 → 0), Vesper reader 10.9 → 0.0 (AOT 12.6 → 0); wheel −10…−11 KB/frame (rest is VirtualList realization). New rows text/selectable viewer hover 13.8 → 0.4 KB/op, wheel 14.7 → 3.3. Time flat, 0 slower, pixels byte-identical (pixel test over alignments, RTL, ellipsis, spans, selection). |
| 2 | da2ccd8, f578b55 | test-alloc-first-run-flake | Two causes. Headless: real code — the DevTools peak trackers' ring grew (536 B) while frame times fell; now a fixed 64-sample ring of time slices. Skia: no object at all — `GetAllocatedBytesForCurrentThread` counted the unused rest of the thread's allocation piece retired by a background-GC suspension (heap walk: one free object). All allocation tests now use `tests/Shared/Allocations.cs` (least of up to 8 rounds) with TieredPGO off in 6 test projects. Failed runs over 30 fresh runs per project: Headless 1 → 0, Skia 2 → 0; mutation checks still fail. |
| 2 | 0cfdbfc, 289faf9 | layout-grid-linear-tracks | Grid cells bucketed in one pass, origins by prefix sums: grid/create −42 %, resize −54 %, edit −64 % JIT (AOT −61/−72/−78 %, placement control 3–6 %); 1.63 M loop steps per step → a few linear passes; bit-identical (random tests vs the old loops). Grid plan examined, not worth it. |
| 2 | d13fe82 | layout-leaves-ignore-offered-height | `IgnoresAvailableHeight` defaults true; VideoElement opts out. controls/expander toggle MeasureSize −72 %, leaf measurements −93 %, row −7 % JIT / −6 % AOT, layout −39/−47 %. Every box identical across all 97 rows and ~556k audited cache hits. |
| 2 | 19b94e9 | layout-maxlines-setter-dirty | Correctness: MaxLines and Overflow setters mark layout. Perf flat. |
| 2 | 30cb978 | checkbox-size-layout | Correctness: Checkbox, RadioButton and ToggleSwitch sizes from the theme re-lay out. Perf flat. |
| 2 | ae90f1f, 4195353 | a11y-node-extras | AccessibilityNode 136 → 80 B (rare fields in a side object). allocKB −13…−39 % on 9 a11y rows, JIT and AOT (checkbox UIA 142 → 87, edit every 10th 368 → 259, wheel UIA 425 → 260); 0 slower; events and trees identical. |
| 2 | 7def7b1 | a11y-snapshot-stale-fields | Correctness: a UIA wrapper answers its node's current Role/AutomationId (Group → Dialog now 50032). No event change. |
| 2 | d40d301 | a11y-atspi-snapshot-diet | AT-SPI path built once (TryFormat + span concat; WSL caught boxing on Fedora's .NET), string[] child paths, presized dictionary: ~500 → ~270 B/node; AT-SPI steps −38…−47 % (−44…−51 % with ae90f1f). Snapshots and signals identical; WSL green. No live AT-SPI client run. |
| 2 | 94ba38d | a11y-focus-unbuilt-node | Correctness (approved): a focus move onto an element mounted in the same handler is told after the next rebuild — +1 UIA focus and AT-SPI state-changed:focused (FindBar Ctrl+F, mount+focus); GetFocus answers. Perf flat. |
| 2 | 0912a5e | a11y-virtual-item-selected | Correctness (approved): list item nodes carry IsSelected; SelectedIndex notifies; a no-scroll move raises IsSelected on the old and new item on both bridges. UIA walk +0.2 KB/op (+2–3 %): one rebuild per move while a reader listens. |
| 2 | c7dffaa | apps-scenario-tween-signal-rebuild | Real-loop suite row via `HeadlessSession.RunLoop` (virtual clock, 1 kHz posts; loop is `Application.RunUntil`): 15.1 rebuilds per frame at baseline. |
| 2 | bd808d4 | reconcile-blueprint-hashset | No set for a parent with no children: 288 → 80 B per `.Children` call; component edit/reverse −10.7…−11.0 % allocKB and −9…−17 % time (JIT/AOT), tabs −8.4 %, a11y/create −10.5 %; 0 slower. |
| 2 | 65cdae0, e1d86a1, c583ef3 | reactivity-tween-tick-per-frame | Animations step once per frame that may be drawn (approved). Row 15.1 → 1.0 rebuilds/frame, −93.7 % KB, −7/−8 % time. Reel (AOT, Vulkan, 11 s): VideoPlayer rebuilds 616–653 → 312–313, 2.1–2.2 → 1.06 per frame, UI-thread 25 → 13 MB, gen-0 1 → 0, frames 511–518. e1d86a1 fixed 65cdae0 holding drawn tweens to the 16 ms idle wait (120 Hz tween 89 → 121 fps). Minimized auto-hide and toasts run to time. |
| 2 | 495b0b4, 6527b0d | reactivity-scope-threadstatic | `[ThreadStatic]` reactive scope (approved): 160 → 0 B per effect run; fan-out 94.4 → 16.2 KB/op (−83 %), −6 % JIT; reads off-thread or after an await no longer subscribe; DECISIONS.md updated. |
| 2 | ca59c6d, 4d82571 | layout-zorder-sorted-allocs | Z-order kept on the child list, filled on first ask, dropped by child-list changes and the ZIndex/Type setters (layout never runs for a handler's ZIndex write). Sticky wheel 3.1 → 1.9 KB/op JIT (−38.7 %), 3.5 → 2.3 AOT; 5,000-row wheel −6.7 % JIT, −12.0 % AOT (placement control ≤ 2.8 %); jump −11.2 % AOT. Junction with a panel open: 1 sort + 52 scans per frame → 0. 0 slower. Child list 32 → 40 B, PositionStyle +8 B. |
| 2 | 913204a | maxlines-setter-thread | Fix-up of 19b94e9: setters mark only what is not already marked, so `.MaxLines(1)` on a new label off the UI thread no longer throws in Debug; a laid-out label still re-lays out and is thread-checked. Perf flat. |
| 2 | 8b40fde, c1b8918 | runtime-palette-filter-allocs | Joined text kept while the palette is open, title-only commands matched once, one array sort: palette row 592.5 → 430.3 KB/op JIT (−27.4 %), 651.9 → 424.3 AOT (−34.9 %); min −11.1 % JIT, −7.3 % AOT; Filter 167/357 → 4.7/9.0 KB per query; results identical to the old code. |
| 2 | 6457f94 | runtime-syntax-registry-first-use | `CanHighlight` answers from a 297-extension table checked against the registry. Vesper's first code folder AOT 7.0 → 2.2 ms and 5.3 MB → 761 KB (JIT 32–38 → 4.4 ms); the TextMate registry loads at the first tokenize instead. |
| 2 | b23b203, 40d054d, 28fbeb8, 05414ab | text-textarea-measure-lines | Width per hard line measured without layout (`IRenderContext.MeasureTextWidth`, bit-equal; 4 MB width cache with a weight); last frame's line strings reused; shape bound 24 → 8 MB. JSON open −21 % JIT / −20 % AOT (median −35 %), 6,599 → 1,903 KB; typing in 2,000 lines −22 %, 465 → 222 KB; undo-redo −28 %; steady TextArea repaint 2.2 KB → 0; 10,000-line JSON keystroke 1.66 ms even at 8 MB (was 25 ms at 8 MB). Meridian first response frame 12.4 → 5.8 MB. Retained after closing the JSON 9.6 → 4.3 MB. JSON wheel +3.4 KB (lines shaped when drawn). Line starts identical over ~2 M lines. AOT TableView sort +5…7 % unproven placement. |
| 2 | b471108 | gl-vk-resize-duplicate-frames | One live-resize frame per change, not per event: frames per SetWindowPos step 2–3 → 1 on GL, F16 and 8-bit, never 0, never at an old size. GL drag CPU −33 % (AOT −31 %), 8-bit −13…−33 %, F16 flat (driver-side swapchain rebuild); F16 drag submits Skia 278 → 120, host 119 → 40. Held live-resize timer 90 frames/s → 0 with video still drawn (mpv 0 dropped). Resize stretch 0 of ~1,000 samples. |
| 2 | 2170222 | a11y-atspi-active-on-status | Linux bridge active only while org.a11y.Status says AT is on (HULTUI_ACCESSIBILITY=1 overrides). 603-node keystroke with no reader 212.8 KB → 1.54 KB (= no bridge). Live-verified in WSL with pyatspi (off: 1-object tree, 0 events; on: full tree; off/on republishes). |
| 2 | 72a4f96 | a11y-value-for-text | No Value change for nodes that offer text (Chromium-cited): UIA rich text keystroke −16.7 % JIT / −14.0 % AOT; the whole-document ValueValue (1.5 MB BSTR) and AT-SPI accessible-value (~750 KB) gone; Value still readable on request. TextArea unchanged (no text pattern yet). |
| 2 | 9c8bf91, cfb5a1a | a11y-richtext-text-snapshot | Rich text editor's accessible text made on request (versioned). a11y/type in rich text 2,100 → 54 KB/op, −43 % JIT / −48 % AOT (UIA −39/−46 %). Streams identical. AT-SPI with a reader still builds it per keystroke (its text-changed needs it). |
| 2 | 6b8bbe4 | a11y-range-value-events | Range on nodes; UIA RangeValue property changes; AT-SPI Value interface + accessible-value; told at most every 250 ms (Chromium's media timeupdate rate). Progress tick rebuilds 60 → 5 per second, −45 % JIT / −52 % AOT; VideoPlayer seek now announced (0 → 19 per 10 s). Live-verified. |
| 2 | 074d898, 333d7de | linux-bridge-sync-context | Correctness (found by the live check): the Linux bridge's connecting constructor hung under HultUI's UI SynchronizationContext. Now connects at once; samples/AtSpiVerify bulk check PASS (also live-verifies d40d301). |
| 2 | 466dce0 | meridian-response-format-on-ui-thread | Prettify, split, line starts and tokenize on the thread pool, cancellable (new response, mode, tab switch, close); plain first above 2,000 lines, colour when spans land. 18,884-line JSON: UI time to text 108.4 → 2.53 ms (−98 %), to colour 4.04 ms; 312 KB response 3.8 → 1.4 ms. Headless JIT. |
| 2 | 9b512c6 | syntax-maxlines-uncoloured | Meridian colours responses up to 200,000 lines (JSON ~5.5 µs a line on a worker; 98k lines 0.54 s, 4.7 MB of spans; the 2 M-character cap stops JSON near 100k lines). Merged find list kept between builds: URL keystroke with find open 8.4 → 1.8 ms, 2,945 → 178 KB. Junction/Vesper keep the 20,000 default (already on workers). |
| 2 | 545d1b6, b9e5a78 | layout-virtuallist-item-rows | Opt-in `.RenderWith((item, selected) => …)` (`ItemRowTemplate`): kept rows whose item (EqualityComparer) and selected state are unchanged; `InvalidateRows()` for anything else; theme object switch re-makes rows. Twin row virtual/selection walk 1,000 item rows: rows per step 19 → 3, 96.8 → 25.7 KB/op (−73.5 %), −6 % JIT / −10 % AOT; layouts −72 %, resolves −40 %; a11y tree and pixels identical to the old template; 0 slower. |
| 2 | a96c185 | junction-history-item-rows | Junction History on item rows (shape key: stats, compact, theme): rows per key 17 → 2.83; realize 0.155 → 0.072 ms JIT, 0.20 → 0.079 AOT; 212 → 41 KB per key (−80 %); frame bytes −53 %, frame time −7 % JIT / −10 % AOT (headless, 80 keys). 16/17 frames byte-identical; the 17th fixes hover lost on a key press. |
| 2 | 337140a | wsl-shaper-real-fonts | d049be7 bit-identical on real Linux faces: 116,438 shaper calls, 0 differences, 80 faces, 0 missing glyphs in every script; Text 192/192, goldens 39/39 in WSL. New test that the corpus reaches real glyphs (mutation caught). |
| 2 | 595b8dd | wayland-minimized-restore | Correctness (found in WSL): after the app minimizes its own window on Wayland, SDL keeps the minimized flag after a restore, so since 82e382b the window stayed undrawn (0 frames in 15 s). A focus gain after a minimize now ends the minimized state on Wayland. Also verified: 0f5816a on Mesa gets depth 24/stencil 8 anyway (no stencil-only config; no saving, no risk, 48 contexts, 0 failures); Vulkan on lavapipe and Dozen pixel-exact through resizes, validation 0 messages; destroy-first not taken on Linux. WSL Linux suite: 9,917 passed, 0 failed. |
| 3 | e0b816f | vesper-grid-resize | User report (2026-10-10): after a window resize the thumbnail area kept the old width until a key press. Brief and Thumbnail grids are sized in the build from the window, but a resize rebuilt only for a scale change or a wrapped diff/viewer. Now rebuilds when either pane shows Brief or Thumbnails. Correctness; two headless tests (fail without it). |
| 3 | dd3dec3 | gl-vk-minimized-children-flag | Regression fix (82e382b): a change made while minimized rebuilds the a11y tree once, not every loop pass; the root also keeps paint dirt so the restore draws (a style- or layout-only change while minimized now draws at restore too). One element added while minimized, UIA listening, real window never focused: a11y rebuilds 61/s → 0; GL 2,016 elements 10.8 MB/s → 29 B/s, UI 33.8 → 12 Mcyc/s; Vulkan 10.8 MB/s → 10.7 KB/s (display-colour poll); 5,016 elements 26.8 MB/s → 29 B/s, 69–74 → 21–22 Mcyc/s. `HULTUI_A11Y=0` unchanged. Perf suite 0 slower/faster. MinimizedWindowTests (mutation: 31 builds for 1). |
| 3 | 8cd854f | gl-vk-undrawn-sources-walk | Regression fix (7e52ff5): a dirty minimized window walks for GPU sources only while one can exist (process-wide count of live `GpuFrameSink`s and of other `IGpuImageSource`s set on images). Tree visits 122k–447k/s → 0 without video; UI −10…−20 % Mcyc/s; what is served unchanged (7e52ff5's real-window tests 7/7). Both commits: GL 31–35 → 7.7–8.5 Mcyc/s, Vulkan 33 → 9.6–10.1 while minimized with a change. Perf suite 0 slower/faster (richtext/open min noise, median −1 %). Remaining: the per-pass frame start of a dirty minimized window (~60/s) is step 10's. |
| 3 | c52e8fa | apps-scenario-selection-walk-real-loop-a11y | Suite rows `a11y/selection walk beside details, keys 60 ms apart, real loop, {bridge on, UIA}`: one Down key then `RunLoop` on the virtual clock to the next key; 1,000 commits on item rows beside an unchanged details pane; reports a11y trees built per key. Shows 0912a5e's regression: 1.0 trees/key → 0.4 after 40b1e97. |
| 3 | 40b1e97 | a11y-selection-move-patch | Regression fix (0912a5e): a selection move marks IsSelected on the changed item nodes of the applied tree and hands the bridge a patch (`IAccessibilityBridge.ApplyPatchedProperties`, default = apply as a diff; UIA and AT-SPI override) instead of forcing a rebuild; falls back to a rebuild for no tree, a list mounted in the same handler, count change, composite rows, crossing 2,000. Event streams identical on 0912a5e's 12 scripts and two walks, except UIA VerticalScrollPercent 28 → 16 in a 200-item scrolling walk (one per refresh, not per key). Junction History AOT, UIA listening, 80 keys at 60 ms: a11y trees 82 → 50, a11y bytes per key 293.6 → 179 KB (−40 %), UI bytes 553 → 438 KB (−21 %). Walk row 380.6 → 227.6 KB/op, min −8…−15 % JIT/AOT; 0 slower. 9 + 3 tests, 7 mutations caught. |
| 3 | c662dbf | a11y-undrawn-frame | Found in step 2: 40b1e97 alone made a refresh coming due between keys its own full repaint (+20 % frame time per key). A frame only accessibility asks for now stops after the a11y phase: not drawn, timed or counted (`PumpFrame`/`RunFrame` return false for it; live resize still draws a clean window with posted work via internal `RenderFrame(drawClean:)`). Junction History net c52e8fa → c662dbf: frame time per key −9 % (1.386 → 1.266 ms mean), bytes −21 %, a11y bytes −40 %; drawn frames per key 1.02. Walk median −14 %/−6 % JIT, −11 %/−1 % AOT; 0 slower. With a client listening, coalesced refreshes (scroll trailing edge, range values, attach) no longer repaint. |
| 3 | 4eeec65 | a11y-atspi-status-serialize | Correctness (2170222): status reads one at a time (`AtSpiStatusReader`), re-read when a change arrived during a read. Live WSL (pyatspi, private bus, bursts of four changes): wrong final state 5/40 and 7/100 → 0/40 and 0/100 (every wrong case: status on, bridge off). 2 mutations caught. |
| 3 | 3788a33 | a11y-uia-text-value-from-node | Thread safety (72a4f96/9c8bf91): ValueValue and the Value pattern's GetValue for a node with text answer from `Node.Value` (editor: the told text version; terminal: the screen as the tree read it), no live-tree walk on a UIA thread. 2 mutations caught. |
| 3 | 1f294c4 | a11y-uia-disconnect-on-close | Leak closed (found by 4): Dispose calls `UiaReturnRawElementProvider(hwnd, 0, 0, NULL)` after restoring the window procedure. 150 open/close cycles of a 60-row window, UIA listening: managed 36,693 → 1,289 KB, private 174–177 → 140 MB (`HULTUI_A11Y=0`: 1,185 KB / 139 MB). Perf suite (with 3788a33, captures 184b9d5): 0 slower; heavy-allocation row medians −15…−30 % and a full run 32 → 28 s, likely the suite's own UIA rows no longer leaking sessions (not measured further). |
| 3 | 88fa622 | apps-junction-prefetch-rebuild | App: a highlight that lands rebuilds only when the build on screen asked for that key (`_highlightShown`, cleared each Build); otherwise cached for Commit/Changes' first frame. History, Down every 500 ms, AOT: builds per key 2.00 → 1.00; a11y off 469.9 → 268.8 KB per key (−43 %), frame 1.96 → 1.13 ms (−42 %); a11y on 1,042 → 573 KB (−45 %), a11y trees 2.00 → 1.06. At 60 ms/key bytes −2…−4 %. HighlightRebuildTests (4 mutations caught). |
| 3 | 16fc8ec | apps-junction-diff-flatten-per-build | App: diff rows flattened once per (diff, split, highlight, theme) object and the list on item rows with a `DiffRowShape` (staging context, wrap, pan, code font, theme, find state) + `InvalidateRows`. Changes typing beside a 184-row hunk, a11y off, AOT: 1,217.9 → 233.4 KB per keystroke (−81 %), rows made 39 → 0, frame 1.82 → 1.36 ms (−25 %); a11y on 1,962.8 → 978.6 KB. Commit no-op keys 650.7 → 272.2 KB (−58 %), frame −18 %. Review mode unchanged (rows still made per build). DiffRowKeepTests (9; 11 mutations caught). |
| 3 | 83050a6 | junction-diff-item-labels | App, a11y correctness: diff rows named for readers ("added, line 12: …", hunk headers, split pairs, threads) instead of a `DiffRow { … }` dump, made on first ask and kept with the rows. Changes typing, a11y on: a11y 744.4 → 31.5 KB per keystroke (−96 %), total 978.7 → 265.4 KB, a11y share 27 → 4–5 %. Step net with a reader: 1,962.8 → 265.4 KB (−86 %), frame −31 %. |
| 3 | df6441c | vesper-item-labels | App, a11y correctness: Vesper's comparison (removed/added/changed/"12 unchanged lines"), viewer ("line N: text") and thumbnail rows (the row's file names) named for readers, cached. One a11y tree of a 400-line comparison 1,093.5 → 40.5 KB (−96 %, headless JIT). ItemLabelTests (5 mutations caught). |
| 3 | 6902403 | apps-scenario-type-beside-diff | Suite rows `input/type beside a 150-line diff, rows {made in Build, kept}` and `a11y/…, bridge on`: made in Build 381 KB/op, 37 rows per key (JIT 0.635 ms) vs kept 19 KB/op (0.519 ms); bridge on 579–653 vs 33–34 KB/op labelled. 108 scenarios; `--verify` passes; full compare 0 slower/faster JIT and AOT. |
| 3 | 0efc402 | apps-scenario-video-bar-fade | Suite rows `media/video player playing, position 10/s, real loop` and `media/video player bar fade, real loop` (FakeMediaBackend, `RunLoop`, internal `VideoPlayer.Builds`): baseline 1.0 build per report at 409 KB/op, 31 builds per fade at 1,235 KB/op (AOT 395 / 1,191). They run last: placed before `a11y/`, three a11y rows read 20–25 % slower on JIT only (JIT state, README note). |
| 3 | aad602a | reactivity-videoplayer-position-in-place | An OnMount Effect writes the elapsed label and scrubber value through Refs; Build reads Position untracked. Per report 41,095 → 420 B, 1 → 0 builds; suite row 408.8 → 12.4 KB/op (−97 %), min −14 %. RangeValue (≤ 250 ms) and seek announcements, drag and hover preview unchanged. REACTIVITY.md "A value that changes ten times a second", MEDIA.md: read Position untracked in Build. |
| 3 | 91893ba | media-videoplayer-fade-rebuild | The fade level is a plain field; the tween writes it and the live bar's Opacity through a Ref (hidden 0, paused 1, else the level). `_controlsVisible` flips at the same moments. Per fade frame 40,872 → 927 B; suite fade row 31 → 2 builds, 1,238 → 88 KB/op (−93 %). Reel AOT, Vulkan, 10 s 1080p24 with pointer sweep and seeks, step net: VideoPlayer rebuilds 287–291 → 5, UI-thread 12.1 → 1.42 MB a11y off (−88 %), 12.4 → 1.55 MB on (−87 %), quiet frame 13.7 → 2.9 KB, mpv drops 0 → 0. |
| 3 | 7016de4 | media-patch-keeps-layout | Canvas, Slider (marks layout when ThumbRadius changes), VideoElement and the player root opt into PatchMarksOwnLayout: a player rebuild that moves nothing costs layout 6 → 0 µs per scrubber-hover move and no empty a11y rebuild 100 ms later. DECISIONS.md updated. Perf compare flat. |
| 3 | 1e3edff | apps-chapters-computed | App: Reel's and Aperture's chapter panels read the playhead through a `Computed<int>` chapter index; the app rebuilds only when a mark is crossed. Panel open, per report: Reel 66,591 → 591 B, Aperture 179,530 → 1,752 B, 1 → 0 builds. REACTIVITY.md shows the pattern. |
| 3 | 133bd8a, 7bfc1e7, 5e28b9a, 58fe439 | apps-playback-pump-out-of-build | App correctness, **broken since each app's first commit** (not plan 17; releases aperture-v1.19.0/reel-v1.4.0 and the previous ones affected): the time-based playback duties ran in the app Build, which barely runs during quiet playback (Aperture 1 build, Reel 0). Aperture's 10 s progress heartbeat and Reel's 15 s resume save never went out: 0 in 25/33 s at 01e5bd7/1093bfa, 4aa5653, 64ba531, 0951288 and 1569c9d → 2 after the fix. State-driven duties (resume seek, tracks, chapters, volume, Ended) worked. Fix: two Effects per player made when the stream opens — state (State, Duration, tracks; Position untracked) and clock (Position + `Application.Time`); nothing left in Build. App builds per 60 s quiet playback 0 → 0. `FakeMediaBackend.HoldOpening/FinishOpening` for load-order tests; 17 mutations caught. REACTIVITY.md "Build is not a clock", MEDIA.md trap. |
| 3 | 7a8f516 | text-prefix-table-scratch | `BuildPrefixWidths` builds in two kept lists (runs cleared after each build; segments capped at 4,096 kept) and copies exact arrays out. Bit-identical over 500,000 random strings in 15 scripts × 5 styles. KB/op JIT (AOT within 0.2 %): text/open long document 3,418 → 1,442 (−58 %), richtext/open 11,248 → 10,027 (−11 %), richtext/resize −10 %, text/wrap resize −11 %, wrapped 900-line typing −10 %; time flat on min, medians −6…−22 %; 0 slower. 20,000-line wrapped log open 202.7 → 73.3 MB allocated (−64 %), later opens 125 → 102 ms. TextBench ellipsis-2lines −36 % B/call. |
| 3 | 7754fe7 | text-prefix-measure-span | New public `TextPrefixMeasurer(string text, int length)` and `TextLine` overloads (CaretXAt, OffsetAtX, VisualRanges, TryMoveCaretVisually) so merged-cluster lines measure prefixes without cutting substrings; old `Func<string,float>` overloads kept (a literal `null` measurer is now ambiguous; no caller). `MeasureWidth` now caches BiDi-path widths keyed by paragraph direction (TextArea BiDi lines hit the cache too; DECISIONS.md updated). Hover frame with two merged-cluster lines −192 B (JIT 1,464 → 1,272, AOT 1,336 → 1,144); the rest (closures) is expert 2's step 7. Bit-equal prefix widths; 10 mutations caught. Not run on WSL: run the Linux tests in the release phase. |
| 3 | dc4800d | layout-style-mark-once | `DirtyFlags.StyleSubtree` claim: the style mark walk stops at an element whose subtree is already marked; cleared with Style; a mounted unmarked child joining a claimed parent drops the claim up the chain (`NoteChildJoined` in the three add paths), so marked sets are unchanged. Mark visits: component/edit 44,004 → 18,004, tabs −76 %, a11y/create −54 %, palette −71 %, Junction History −75 %, Branches −64 %, Tags −76 %, Files search 383,482 → 60,731 per key. Every other counter and allocation identical on 110 rows. Time: component/edit keyed 1.458 → 1.246 ms JIT (−15 %), −6 % AOT; unkeyed −9/−10 %; reverse −10/−8 %; 0 slower. Shadow audit: 0 differences over ~4.3 M elements in 10 suites (positive control 2,548). 18 tests, 7 mutations caught. |
| 3 | 51f3126 | layout-applytheme-layout-on-change | Five leaf controls mark layout in ApplyTheme only when a measured value changed; Expander marks the parts whose written sizes changed (fixes a header keeping 32 px on a 48 px theme); Toast's ApplyTheme no longer calls ReplaceChildren (no Children/a11y rebuild per restyle). Theme rows: layouts 78 → 0, MeasureContent 100 → 0; light/dark 8.7 → 0.0 KB/op, −6 % JIT / −7 % AOT; named scheme 10.9 → 2.2 KB/op, −6 %. THEMES.md control-author paragraph. 14 tests, 17 of 18 mutations caught (the 18th's code removed). |
| 3 | 5cd0bdf | paint-raw-run-buffer | `AllocateRawPositionedRun` (struct): per new blob 136 → 80 B (AOT and tier-0 JIT; warm JIT kept the class on the stack). AOT KB/op: scroll/jump 9.1 → 6.3 (−31 %), nested 3.0 → 2.2, wheel 2.3 → 1.8, sticky 2.3 → 1.9, selectable viewer wheel 4.0 → 3.3, richtext wheel 1.0 → 0.6; JIT unchanged; 0 slower. (103f30e's "136 B wrapper" was 80 B wrapper + 56 B run buffer.) |
| 3 | 6c70dbe | paint-polygon-pooled-path | `FillPolygon`/`DrawPolyline` reuse the kept SKPath (Rewind); Terminal `BoxDrawing.HalfEllipse` uses a stackalloc span. Per forced frame (JIT = AOT): six Powerline lines 12,880 → 1,744 B; 40 ticked tasks 3,224 → 24 B. Pixels byte-identical (6 shots, scale 1 and 1.5). The kept SKPath is shared by polygons, polylines, corner clips and the canvas context: nothing may hold it across those calls. |
| 3 | 4e85cdd | paint-cluster-line-measurer | One `TextPrefixMeasurer` per style, cached by style value (≤ 64) on SkiaRenderContext and on TextPipeline (`PrefixMeasurer(style)`, used by PipelineTextParagraph). Hover frame with two merged-cluster lines: JIT 1,272 → 312 B, AOT 1,144 → 312 B (= the ASCII baseline: the lines now cost 0 B); with selection 1,400/1,240 → 312. 0 slower. |
| 3 | 27024f6 | paint-native-blob-handles | Approved (question 11, "as long as headless still works"). Text blobs are native handles through SkiaSharp's C API: `sk_textblob_builder_make`, `sk_canvas_draw_text_blob`, `sk_textblob_unref` **and `sk_textblob_get_bounds`** (a fourth, for TextPaintBounds), resolved from the library SkiaSharp loaded; a missing export throws by name. Released once (cache empty, Dispose, finalizer). KB/op JIT = AOT: scroll/jump 6.3 → 2.4 (−62 %), nested −45 %, sticky −37 %, wheel −33 %, selectable viewer wheel −33 %, richtext wheel 0.6 → 0.2; new blob 80 → 0 B; 0 slower. Condition met: Headless 767, Rendering.Skia 250, 17 golden scenes byte-identical, 27/27 non-outline headless frames identical, GL and Vulkan readback identical, WSL Fedora 10,012 passed / 0 failed. Leak test: 100k blobs < 32 MB (81.9 MB with unref removed). DECISIONS.md records the pin. |
| 3 | 5cc9892, b183aa0 | paint-spanned-line-sub-blobs | Each colour stretch draws a kept native sub-blob of only the glyphs whose ink (face box + 1/8 em, 3/8 slanted, + 2 device px) reaches its clip; colour/bitmap/untrusted-box faces draw whole runs. Glyphs per frame −73…−91 % (Junction diff hover 10,650 → 2,763; spans recolor 99,167 → 8,734). Time: Junction diff hover −15/−17 %, wheel −19/−21 %, Vesper reader hover −19/−21 %, wheel −25/−27 %, spans recolor −70/−71 %, spans bold −53/−55 % (JIT/AOT); suite spans recolor −65/−67 %, bold −24/−41 %, rich text rows −9…−14 %; richtext/wheel 0.2 → 1.3 KB/op from a one-time map growth (not presized: ~57 KB per window). Pixels byte-identical (in-process A/B over 8 scripts × 3 scales × 5 transforms, 17 goldens, 27 frames, GPU readback). WSL 10,030 passed / 0 failed. No win yet on variable fonts (Cascadia Mono). |
| 3 | b0ed204 | vk-uitests-no-foreground | Test harness: the Vulkan probe UiTests' windows get the in-process SDL hint, `WS_EX_NOACTIVATE` and a foreground watcher that fails the test (exit 5) if one is ever taken. |
| 3 | 58f78dd | vk-purge-disposed-image-textures | Sources disposed after a draw are counted (any thread); the Vulkan surface runs its scratch purge after a frame once the count moved, at most once a second, no GPU wait (0.02–0.2 ms). 8 × 3840×2400 photos drawn then disposed: DXGI 391.5 → 213.2 MB (−45 %), Skia purgeable 176 → 0 MB, nvidia-smi +355 → +175 MiB, private 538–586 → 401 MB. Poster-grid churn (27 disposals/s): uploads 25.8/s unchanged (no re-uploads), frame time no slower. GL untouched (driver keeps the memory). |
| 3 | 68424c2 | vk-swapchain-target-per-frame | One GRBackendRenderTarget + SKSurface per swapchain image for the swapchain's life (`SwapchainTargets`); F16 blit with DrawSurface; host contract `SetSwapchainReleasing` (targets dropped before images go) and `TransitionAcquiredImage` (barrier to COLOR_ATTACHMENT_OPTIMAL: just before the blit on the offscreen path, at once on the direct path; a frame after a GPU source draw wraps per frame as before — a barrier at every acquire slowed mpv's GL draw, 1.25–1.47 vs 0.97–1.14 ms). Idle-animating window: 2,046–2,048 B/frame → 0 (F16 and 8-bit, JIT and AOT), frame min −10…−16 %, median −7…−15 %; vkCreateImageView/Framebuffer per frame 1 → 0. Reel HDR playback 2,580 → 530–552 B/frame (8-bit video unchanged by design), mpv drops 0. Validation (SDK 1.4.357, core + sync) clean on reused-frame captures (40th frame, 3 resizes + minimize, F16/offscreen/scaled/8-bit/FIFO/IMMEDIATE/fixed extent); positive control (no barrier) `VUID-vkCmdDraw-None-09600` ×10 — NVIDIA drew correctly anyway, so the capture UiTest runs under the layer. HDR.md invariant rewritten. |
| 3 | c6bc336, 3be8ce1, 31500ab, 939bf33 | vk-drag-swapchain-overallocate | Approved (question 14). maintenance1 on by default on Windows; a drag (first size change inside WM_ENTERSIZEMOVE…EXITSIZEMOVE, seen by a new comctl32 subclass `WindowsSizeMoveWatch` on every Vulkan window incl. Reel's default frame) over-allocates a one-to-one swapchain the window still fits (kept on SUBOPTIMAL) and rebuilds exact at the end; a title-bar move never over-allocates (`SizeMoveTracker`). **MAILBOX only:** under FIFO a one-to-one swapchain hung for good (acquire fence never signals, 3 repros); under IMMEDIATE its first build cost 172–196 ms and showed a stretch — both keep exact rebuilds. `HULTUI_VULKAN_FIXED_EXTENT=1` fixed (60–65 → 6.9 ms/step; `=0` turns maintenance1 off). Surface-format query cached on kept steps; Resize skips the target drop when the host keeps the swapchain. Median ms per drag step JIT/AOT: probe 32.9 → 5.6 / 34.5 → 5.9–7.8; Aperture Home 35.5 → 8.1 / 36.5–38.1 → 8.0–8.1; Reel idle 33.9 → 5.6–7.3 / 33.2–35.2 → 5.8–7.3; p95 36–41 → 7–12.6; builds per drag 80 → 5; release frame 29.6–36.3 ms; Aperture AOT drag CPU 6.6–6.9 → 1.3–1.4 Gcycles. Move: 0 builds before and after. Video drops per drag (Reel AOT): 1080p60 35–36 → 0–2, 1080p120 228–241 → 11–12; release 0 → 0 (60) / 0 → 2–3 (120); A/V desync max 17.5–20 → 10–13 ms (60). GPU memory during a drag 299–318 → 321–322 MB, back to 214 after. Stretch 0/519 and 0/494 sampled (positive control 64/581); mid-drag capture 0 px differ; validation 0 messages incl. maximize/snap/minimize (positive control fired). The 157–163 ms FIXED_EXTENT outlier was the first swapchain replacement after show (190–290 ms on every path). |
| 3 | 4f43237 | apps-tooling-generic-framelog | `HULTUI_FRAMELOG` (`1` = stderr, else a file) + `HULTUI_FRAMELOG_SECS`: per-interval passes/frames/ticks, idle and paced waits with early returns, time parked, deadline kinds, posts and wakes by source, inputs, bytes per pass and input, GCs, heap, private bytes, windows. One static readonly bool when off. ENVIRONMENT.md. |
| 3 | 1521f47, 0e2bf80, 5c3c30a | runtime-idle-wakeups | The idle wait runs to the next owned deadline (animations; a11y trailing refreshes 100/250 ms only while a client listens; resize throttle; inspector; Vulkan colour poll only without colour events) or forever; wakes from AccessibilityActionRouter enqueues, AT-SPI status flips (`IAccessibilityActionHost.ActiveChanged`), first WM_GETOBJECT, and any UI-thread work done inside the wait (WebView2, OLE, DispatcherQueue); a non-empty post queue is a deadline due at once (0e2bf80 fixed a lost post). Real windows, JIT and AOT: idle 60.6–60.9 → 0.0 passes/s (all five apps, GL and Vulkan with colour events); Vulkan polled 60.7 → 10.6; minimized 36 → 0.1; minimized dirty 38.2 → 0.7. On time: spinner 120 fps unchanged; a11y layout refresh 102–112 → 100–101 ms (due 100), range 250–265 → 250–252; live resize 1.5 frames/step unchanged; Reel 1080p60/120 0 drops; WSL AT-SPI DoAction 4.9–8.0 → 1.7–3.4 ms. Input post → frame median unchanged, p90 in a background window 0.74–1.11 → 2.49–5.03 ms (watch item). Suite 0 slower, 2 faster; the real-loop selection walk now meets its 100 ms refresh (0.4 → 0.6 trees/key, by design). |
| 3 | 9c43337 | reactivity-animation-deadline | Approved (question 5). `AnimationManager.NextChangeIn`; toast hold, tooltip show delay, VideoPlayer auto-hide and six Terminal timers are delays (`Animate.After`); in the loop a tween's first step is 0 (no jump after idle). A 1 s toast every 3 s: 44.5 → 31.2 passes/s, animation steps 30 → 13.7/s; tooltip every 3 s 12.1 → 2.6 passes/s, delay 486–500 → 500.6–502.6 ms (no longer early). Cost: an extra identical first frame per tween (toast row +4 % time) — step 12 bounds it. |
| 3 | c667032 | winforms-idle-ticks | The WinForms host's 8 ms timer ticks only when something is due; wakes post a tick. Idle 62.9 → 0 passes/s; spinner 63 fps unchanged; toast every 3 s 64 → 8.1; worker post → frame median 16.2 → 0.6 ms, p90 16.9 → 4.8; toast end ~2 ticks later. |
| 3 | c229971 | apps-scenario-loop-wakes | Four `loop/` suite rows: idle 62.6 → 0.2 passes/s, minimized and changed 62.6 → 0.2, toast held 2 s 71.3 → 19.7, tooltip delay 66.4 → 8.0. |
| 3 | 341ac09 | gl-uitests-focus-hardening | The in-process GL loop UiTests get `ForegroundGuard` (SDL hint, CBT hook with WS_EX_NOACTIVATE and activation refused, 5 ms watcher). Other GL UiTests (incl. WinFormsHostSampleTests' `EnsureForeground`) still unhardened. |
| 3 | 3f7eccd, 6a7114c, 1608999 | runtime-pointer-events-on-demand | PointerMoveEvent made only if something on the pinned path listens; Enter/Leave made at the first listener and shared up the chain (handlers added mid-walk still heard); PaneSplitter/HorizontalPan's idle drag trackers register as passive-and-not-listening (`OnPassive(handler, listening)`, condition on the delegate target so handler entries stay 16 B). B per move incl. the 40 B MouseMoveInput: within an element 104 → 40, hover change 216 → 40; real windows (60 Hz posted moves, HULTUI_FRAMELOG, GL and Vulkan, JIT and AOT) synthetic/Junction/Reel 216 → 40, UI thread 13.0 → 2.4 KB/s; Vesper 216 → 104 (its root move handler); Meridian 418 → 242 JIT. Suite input/hover 0.2 → 0.0 KB/op; 0 slower. |
| 3 | 4bd3576, 1bd5e4c, b9e949e | reactivity-pointer-rebuild-per-frame (b) | User's answer (b): only PaneSplitter, HorizontalPan, the slider (hover, OnChanged during drag, OnHoverValue → VideoPlayer scrubber) apply the latest position once per frame via internal `PointerFollow`/`IPreFrameHost` (run at the start of RenderFrame, no allocation); release, press, keys, a11y value sets and PanTo flush first, so a drag ends exactly on the pointer (Junction pane 431.6 → 438.0 for a 200 px drag); every other handler keeps every move. 1 kHz moves, real loop: 120-row splitter app 16.1 → 1.0 rebuilds/frame, 10,499 → 655 KB/frame (−94 %); Junction Commit splitter 8.1 → 1.03, 1,782 → 267 KB (−85 %); scrubber hover 15.7 → 0.97, 628 → 43 KB (−93 %); slider drag 10.3 → 1.0, OnChanged 309 → 30. New suite rows (117 rows): splitter drag −57 %/−62 % time JIT/AOT, 63.0 → 4.6 MB/op; slider drag −53/−59 %, 30.8 → 2.1 MB; scrubber hover −6/−9 %, −93 % bytes. REACTIVITY.md "A drag rebuilds once per frame"; ELEMENTS.md. |
| 3 | bdd2aa3 | apps-scenario-toast | Three `loop/` rows over a ~300-element screen built anew: toast (23.0 builds/toast, 5,941 KB/op at first), spinner rebuilt per step (1.0 rebuilds/frame, 7,630 KB/op) vs turned in place (0.0, ~0 KB). 120 rows; `--verify` passes. |
| 3 | 1d39128 | reactivity-toasthost-fade-in-place | Approved (question 3). The fade writes `_opacity` and the mounted toast's Opacity through a Ref; `onChanged` on appear, change and go only; the hold stays a delay. Per toast: builds 23 → 2 in Junction, Meridian and Aperture; Junction 4,505 → 389 KB (−91 %, 6,215 → 959 with the bridge), Meridian 1,544 → 132, Aperture 3,139 → 272. Timing and screen-reader stream unchanged. Suite toast row 5,940.8 → 513.4 KB/op, −16.6 %/−21.9 % time JIT/AOT. |
| 3 | 2894d98 | apps-whole-app-tween-rebuilds | App: Junction's remote-op icon motion and spinners write live elements through Refs; Meridian's 150 ms send readout writes its label and stops with its own send (ticks used to stack on repeated sends). Junction fetch 61 → 0 builds per 61 frames, 11.40 → 0 MB (14.18 → 0 with the bridge), still 60 fps; busy spinner 12.84 → 0 MB; AOT 0 B. Meridian repeated sends 11.9–17.9 builds/s → 0, 1,548–2,308 → 1.4 KB/s. Aperture has no such clock. |
| 3 | 7cfd0c9, 752dcf1 | tween-first-step-bounded | The loop's first tween step is min(time since the loop's last step, 1/DisplayHz) (Run and the WinForms host; RunFrame/Tick unchanged); a tween that starts with a delay (tooltip, toast hold, auto-hide; internal `ITween.Delay`) still starts at 0 so it never fires early. A 300 ms tween after idle: 20 → 19 frames, done 316 → 299 ms; toast flash → gone unchanged; the extra identical first frame gone. Allocation identical on every row. AOT TableView 10k sort +4.4…7.1 % in 5/5 pairs, JIT ±0.3 %, attributed to AOT code placement (evidence in the commit). |
| 3 | 5b71b4f, d98cca3 | text-textarea-incremental-wrap | Line starts kept and patched in place: untouched rows stay, later rows shift by one vectorized add, only touched lines re-wrap; the text-keyed wrap memo is gone; replaced runs matched in order (hash lookup after 8 misses) so new text sharing its ends stays cheap. Fixed an old bug: an edited Arabic line at narrow widths could break unlike a fresh layout (break search started from a carried width). Wrapped 20,000-line log: typing 8,777 → 6,353 KB/key (−28 %; the rest is the new 6.3 MB string), 4.9 → 2.1 ms JIT, 5.0 → 2.4 AOT; held while open 8.63 → 0.34 MB (−8.3 MB). 19k JSON typing 981 → 724 KB (−26 %), 0.92 → 0.77 ms. Suite: 2,000-line typing −7 % KB, −5 %/−2 % min; wrapped 900-line −8 %, −7/−3 %; undo-redo −8 %, −7/−4 %; 0 slower. Equivalence: 1,500 random edits in a test (mixed scripts, widths 37–2,400, font changes), 12 × 2,500 soak, b23b203's audit 10.7 M lines 0 different; WSL passes. |
| 3 | dc028f9, d18ef87, 2808caf | text-memo-cache-byte-bound | BiDi/styled memos and remembered lines bounded by weight too (10 MB and 12 MB; `LruCache.Reweigh`), `MemoRange` keys hold an entry number so evicted entries are not kept alive; weights within 3–5 % of full-GC measurements. Retained after the texts go: 2,000-char Hebrew/Latin 64.7 → 26.5 MB (−59 %), styled 45.2 → 22.1 (−51 %), 400-char 32.9 → 26.0. The 5,000-block rich document re-open unchanged (31.18 MB). Suite 0 slower; styled ellipsis −2.4 % KB. 2808caf fixed a flaky memo test (a string gets its memo on its second layout if no other string took its admission slot). |
| 3 | 1e68166, 3409c2d | layout-virtuallist-row-inherit | Approved (question 6). One-argument rows resolve under their list (`ResolveUnder`). Per key: Junction Branches style-pass layout marks 240.7 → 0, layouts 255 → 33.7, ComputeLayout 975 → 478, leaf measures 830 → 205; Tags marks 153 → 0, layouts 166.7 → 30.7; Vesper walk marks 120 → 0, layouts 136.6 → 76.6. Branches/Tags min −4.6/−4.8 %, median −6/−7 % JIT; Vesper flat. Pixels: inherited-style diff over 7 suites + probe walks showed only Box/Image FontSize (unused); 15 headless screens 0 px different (positive control 3,994 px). ELEMENTS.md. |
| 3 | 518258a, 757a79b, 0ca47d5 | a11y-item-label-keep | Approved (question 9). Item-rows lists keep `(item, label)` per index while the item is equal and the delegate the same; `InvalidateItems` (InvalidateRows, new Items, theme) and a new delegate clear it; UI thread only (lists over 2,000 are read off-thread). New row `a11y/selection walk 1,000 item rows, bridge on` 274.1 → 112.3 KB/op (−59 %), min −5.0 % JIT / −7.6 % AOT; real-loop walk 278 → 181 KB/op. New public `HeadlessWindow.AccessibilityTreesBuilt`. Contract documented on AccessibleLabel, ItemRowTemplate, InvalidateRows, ELEMENTS.md. |
| 3 | 0971573 | vesper-item-rows | App: Vesper's panes (SelectedIndex = cursor, −1 inactive; `PaneRowState` shape: partner path normalised, size/date/zebra, results, scale, drop target, marks/cut/folder-size snapshots), comparison and viewer on item rows. Pane walk 360 keys: other-name folders 561 → 222 KB/key (−60 %), min −11 % JIT / −16 % AOT (a11y on −60 %, −10.5/−14.6 %); same-name folders −30 %, −9/−14 % (the F3 partner moves each key). Rows made per key 60 → 2.65; leaf measures 3,099 → 174. 23 headless frames byte-identical. KeptRowTests 18, 21 mutations caught. |
| 3 | 92224e3 | junction-branches-tags-item-rows | App: Branches/Tags TreeNode rows memoized (`RefRowsMemo`: refs identity, query, flat/tree, open-folders generation); item rows with the caret as SelectedIndex; `BranchRowItem(Node, Index, Picked, Role)` from a lazy view so a pick or base/head change remakes only those rows; label delegates in fields. Synthetic repo, headless, JIT and AOT: Branches 756–775 → 212–219 KB/key (−72 %), min −10 %/−13 %; Tags 726–745 → 340–349 (−53 %), min −10 %; rows made 19.3 → 1.7 and 17 → 2.7. 30 frames byte-identical; 24 mutations caught. |
| 3 | 5a98a32 | apps-junction-files-virtual | App: the Files tree is a `VirtualListElement<FileTreeRowItem>` (Node, Open, Cursor, Expanded) with visible rows memoized and a path index for the cursor; `RowsAreComposite`. 2,300-file search, Down key: 16.8–17.0 MB → 173–176 KB (−99 %), min 9.9–14.0 → 1.00–1.04 ms (−90 % AOT, −93 % JIT), median −95 %; bridge on 18 MB → 190 KB; elements 14,128 → 257; resolves per key 14,128 → 266; hit visits per move 3,502 → 34; render culls per frame 3,474 → 7. a11y: on-screen nodes identical over 23 states; off-screen rows no longer published (a VirtualList cannot publish them as tree items), no new events. 23 frames byte-identical; 13 mutations caught. |
| 3 | 11ced00 | itemrows-scale-shape | Bug fix (hits a96c185 from round 2, released in junction-v1.20.0, and 16fc8ec/92224e3): kept rows were not remade on a display-scale change, and the retired glyph rasters were freed by the next one, so after 150 % and back every icon in Branches rows was gone (1,753 px; History 36 px). `Ui.DisplayScale` added to the four Junction shapes; round trip 0 px. ELEMENTS.md: put the scale in the shape. Framework safety net queued in step 17. |
| 3 | 73ec513 | aperture-grid-item-rows | App: the poster grid on item rows, `GridRowItem(Row, CursorColumn)` from a lazy view; shape: loaded count, in-place patches, cards per row, artwork landings, display scale. 600 films: Right 359–362 → 130–131 KB/key (−64 %), Down 360 → 196 (−46 %); rows made 6 → 1.1/2.5; time flat (headless raster dominates). 16 frames byte-identical; 10 mutations caught. |
| 3 | d35cf89, 12fe5c2 | a11y-patched-props-untold | Approved (question 7). Role, AccessibleName, AccessibleDescription, AccessibleExpanded, AutomationId, Name and the Label of Checkbox/RadioButton/ToggleSwitch notify on change; the differ compares AutomationId (no event; UIA answered the old id before). 13 standard scripts identical; a new script tells each change at its own step (UIA Name, HelpText, ExpandCollapseState; AT-SPI accessible-name, -description, expanded). 26 tests, 9 mutations caught. Allocation identical on 121 rows; 0 slower. |
| 3 | 68d3863, 0f28ca6 | a11y-role-change | Approved. The differ reports Role first; UIA raises ControlType and LocalizedControlType; AT-SPI sends property-change:accessible-role (not for the application root). Live WSL: a caching client's role now matches after a change in both directions. 6 tests. |
| 3 | dd75acd, 2332fbe | a11y-atspi-cache-child-index | Approved. The survey's live check was invalid (its driver cleared the cache). libatspi overwrites a slot on cache-add and reinserts on children-changed:add: before, 6 of 10 insert cases left a wrong cached list. Now removals first, then one children-changed:add per added child in index order (the run ending the parent's children keeps one signal), cache-adds last. Live: 10 of 10 cases match. An `AtSpiClientCache` model reproduces the live results in tests. UIA unchanged. |
| 3 | 9aa3516, 691d2a7 → 97ff373 | a11y-layout-refresh-moved-only (reverted) | Correct (1,155 shadow audits empty) but +16 B per element, +0.5…+1.9 % allocation on element-building rows, a11y/create +5.3 % min JIT, and its own probe paid more bytes than it saved; the common case it helped (whole-app rebuilds per animation frame) was removed by steps 4 and 12. Reverted by the orchestrator per the brief; numbers in DECISIONS.md. |
| 3 | a52d481, 12c64b0, c1095da | linux-emoji-colour-fallback | Correctness, pixels changed on purpose. UTS #51 presentation: Emoji_Presentation characters, a pictograph or keycap base + VS16, the first character of a modifier or ZWJ sequence go to the colour emoji face ahead of the primary face; plain ™ © # 1 and ❤/↔ without VS16 stay text; VS15 turns the preference off. New `EmojiPresentation` (Unicode 16.0 tables checked against ICU), new public `IFontCollection.MatchEmoji` (default null), `SkiaFontCollection.MatchEmoji` (fontconfig `und-Zsye`; DirectWrite by family name; colour faces only). Windows: #️⃣ 1️⃣ *️⃣ ™️ ©️ ®️ ↔️ ▶️ ☝🏽 now Segoe UI Emoji; Fedora: 😀 ☕ ⚡ ❤️ now Noto Color Emoji, keycaps/☝🏽/❤️‍🔥 one glyph. Broad text pages byte-identical on both OSes; goldens unchanged. Perf 0 slower/faster (a first version cost +6/+11 % on long-document open and was replaced by a vectorized gate); new row `text/create 2,000 unique emoji labels`. WSL 10,216 passed. |
| 3 | 8042e8e | toasthost-second-flash-opacity | Bug since ToastHost's first commit (32be2a6, every Aperture release): a second Flash during a fade kept the fade's level, so the replacement stayed half drawn for its hold. It now rises from its current level to full at the fade-in's pace (same in-place step), holds at 1, fades out normally; 2 owner rebuilds. |
| 3 | 2e3083b | customframe-install-activates | `WindowsCustomFrame.Install`'s SetWindowPos (since 572ab77) gets SWP_NOACTIVATE: a custom-frame window no longer asks for activation while it is made. CustomFrameActivationTests under ForegroundGuard (Borderless fails without the flag; Default is the control). |
| 3 | 3be0dea | junction-remoteop-test-flake | Test timing, not 2894d98's code: Junction's GitWatcher sometimes reloads after the app's own `.git` writes (2 of 40 starts), landing builds inside the measured window. `Settle` now waits for 700 ms of real time with no build (cap 3 s). 50/50 under two concurrent suite copies. |
| 3 | 56046e4 | itemrows-remake-on-scale | Framework safety net: Window remembers the display scale; on a change it calls internal `Element.OnDisplayScaleChanged()` over the root and overlays before `Resized`, and item-row VirtualLists drop their rows (as on a Theme switch). One float compare per resize. The apps keep the scale in their shapes (one compare per build); Junction's five scale tests pass through the framework alone. |
| 3 | a26ca68 | aperture-library-paging | Bug since 01e5bd7: pages requested by window height only, so End, arrows and the wheel stopped at the 100th film; a landed page waited for an unrelated rebuild. Now the next page loads when the furthest realized row or the cursor comes within a page of the loaded end; End goes to the last item (up to 1,000 per request); the build reads `_gridPage.Value` so a landed page shows; a second after-frame reveal. 250 and 1,000 films reach the end; End ≤ 3 requests. |
| 4 | ec4ced5 | runtime-sdl-wake-flag-after-poll | Correctness (1521f47 with 3a539f8): `PumpEvents` clears `_wakePending` after `PollAllEvents` (in a finally, Interlocked.Exchange), so a wake landing between the clear and the first poll's OS pump (window messages, live-resize watchers, main-thread callbacks) is no longer swallowed. Dummy-driver test through the real PumpEvents: a wake 50 ms into a 3,000 ms wait ran 3,001 ms before, ~50 ms after. Real window: idle 0.0 passes/s, spinner 199 fps unchanged, 0 lost in 3,000 wake pairs. |
| 4 | 397eca4 | test-frame-pacing-event-precondition | The storm thread spins to a schedule (Stopwatch + SpinWait, 1 ms or four per display interval above 250 Hz) and the precondition scales with the display: at 200.01 Hz 512 → 1,008 events, all 4 FramePacingTests pass under ForegroundGuard. |
| 4 | 94fc1ef | tooling-textbench-warmup | The JIT warm-up runs on a second scenario set following the measurement's index sequence, cold rows get a new string per call, warm-up runs ≥3 s then until the compiled-method count is flat for 1 s (cap 30 s), and a health line prints. Methods compiled during measurement 914–960 → 126; run-to-run spread 2× → ≤12 %; cold rows −1…−6 %, resize rows −7…−60 % min (the old warm-up measured them partly at tier 0). Old TextBench captures are not comparable across this change. |
| 4 | c0560e1, 82ee093 | events-noop-input-frames | Every input follows the move rule: a frame when an ordinary handler ran for an event the input dispatched on that event's path (key, press with focus/blur, release with click, ScrollEvent, drag enter/over/leave), hover changed, something marked itself dirty, a drag was active before/after, focus moved, or the keyboard-focus flag flipped; F12 marks the root; the safety net for app handlers stays (HULTUI002). Audit of every owned/passive handler; one miss fixed (Terminal's IME composition clear did not mark). Shadow audit: 69/70 skipped no-op inputs pixel-identical (the 70th a test's off-thread echo). Real windows AOT, UIA listening: frames per key press 2 → 1; frame time per press Junction 2.0 → 1.18 ms (−42…−44 %), Vesper 1.64 → 0.92 (−40…−47 %); no-op wheel notches 16–20 → 0 frames. New rows: key press 1.326 → 0.696 ms JIT (−47.5 %), −45.9 % AOT; 20 no-op wheel notches 5.4 → 0.005 ms. 0 slower. ELEMENTS.md "Which inputs draw a frame". |
| 4 | 4000186, ea4e01a | apps-vesper-passive-move-handlers | Vesper's root and pane-row PointerMove handlers and Junction's review rows are passive and listen only while they act; **`Element.OnPassive<TEvent>(Action<TEvent>, Func<bool> listening)` made public** (API addition). Vesper: 60 idle moves along a row 57–60 → 1 frame; 104 → 40 B per move. Tests incl. right-drag mark, Find grip, review drag. |
| 4 | 968bd8d1 | button-isenabled-rebuild | Approved. `ButtonElement.IsEnabled` notifies (the only control with an enabled state). UIA IsEnabled and AT-SPI enabled + sensitive now told for a direct off/on and in a rebuild's own frame; live WSL: a directly disabled button stayed enabled in the client's cache 2/2 → 0/2. Allocation identical on 124 rows. 8 tests. |
| 4 | 1698b4a6 | atspi-reorder-signal | Approved. One children-changed:add per child out of place in the client's cached list (previous snapshot minus departed children), added children through the same pass, a trailing run keeps one signal. Live WSL: all 7 reorder shapes read the old order before; 17/17 insert and reorder cases match after. Apply after reversing 1,000 children 265.4 → 287.3 KB (999 signals). UIA unchanged. 15 tests on the `AtSpiClientCache` model. |
| 4 | 9218026a | atspi-status-subscribe-first | `AtSpiStatusReader.Watch` subscribes to org.a11y.Status, then makes the first read through the same reader (a change during subscription or a slow first read is read again); live WSL 29/29. |
| 4 | 78f3a22b | a11y-uia-host-reads-off-thread | Every UIA pattern read (value, toggle, range, item selected, list selection, find, scroll, on-demand items) and RaiseScrolled answer from the applied node; new rare node data `ScrollExtent` and `ItemSource` (API additions; `IUiaHostBridge` reads stay, unused). Visits per UIA read on a 1,533-element page 1,494–1,532 → 0; scroll refresh UI-thread reads 2 → 0. Events and trees identical in 18 scripts; pattern answers now match their properties (a switch's Toggle pattern said indeterminate). a11y/create +0.2 KB/op; 0 slower. |
| 4 | 1e1948f9, 103ad672 | apps-scenario-text-caret | Suite rows: rich text caret walk beside 300 rows (recording, UIA), the walk in 5,000 blocks with a reader reading, typing and a caret walk in a 2,000-line TextArea (UIA); 103ad672 kept the walk's editor at 300 px (it had shrunk to 10 px). |
| 4 | 92beae04 | a11y-text-caret-patch | A rich text caret/selection move swaps a new text version onto the applied tree and hands the bridges a TextSelection patch (no tree); AT-SPI reads offsets from the versions. Per key: beside 300 rows recording 78.8 → 1.0 KB, UIA 79.2 → 1.1, AT-SPI 319.3 → 1.2; 5,000 blocks AT-SPI 474.9 → 1.2; trees per move 1 → 0. Caret walk −17.5/−20.4 % JIT, −24.3/−27.6 % AOT. Streams identical in 22 scripts; live WSL 18/18. |
| 4 | 07ed97e3 | richtext-readonly-a11y | Correctness (lead from step 3, confirmed): the editor's ReadOnly setter notifies and the patch swaps in the new version, so a reader's thread no longer answers the old read-only state until another rebuild. |
| 4 | 30d19928 | a11y-text-caret-shares-text | When only the caret/selection moved (same document, width, theme stamp, scroll, read-only, blocks on screen), the next text shares the last one's text, tables and geometry (internal AccessibleText constructor; at most one share source, dropped once made). 5,000 blocks with a reader reading: 474 → 1.3 KB/key both bridges; walk 1.136 → 0.711 ms (−37.4 %) JIT, −47.6 % AOT. Every shared answer equals a fresh build. |
| 4 | a0221337 | a11y-textarea-text-pattern | Approved rule. TextArea offers a versioned text source (lines = drawn rows; character boxes for visible rows ±1); a keystroke is told as UIA TextChanged / AT-SPI text-changed with the characters, never a whole-text Value (still readable on request); caret moves, read-only and re-wraps patched; caret moves with a reader share tables; `AccessibleTextVersion.Characters`. 2,000-line area, 40 keys: BSTR characters 4,869,780 → 0, AT-SPI signal characters 4,869,780 → 40; managed flat. Caret moves now told (0.4 → 1.0 KB/op). Live WSL: the area has a Text interface now, 11/11. BiDi limitation (boxes between caret positions) documented. |

## Examined, not worth it

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

- **Round 2 build:** gl-vk-one-submit-per-frame (61edb19, DECISIONS.md "One submit per frame"): GL glGetError 3 → 1 and F16 submits 2 → 1 per frame, but frame time only −0.8…−4.5 % (JIT/AOT) because the driver wait moves from Flush to Present; pixels identical; reverted. So round 2 question 12 (no-op glGetError) is moot.

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
  under a shared GPU (61edb19); working set and start-up (no new lever).

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
  extra F16 submits (61edb19); WebView interop on resize (no app); Vesper viewer PageDown; Meridian
  (flat, text-open and components remain).
- **Testing trap (round 4):** DirectWrite text is not byte-identical between two draws in one process
  after Skia's glyph strikes are purged (A/A differed in 4–5 of 120 frames; 0 with a 512 MB cache) —
  pixel A/Bs on system fonts need an A/A control. It explains round 3's unexplained 1–4 pixel diff.

## Needs the user

Round 4 questions, all answered 2026-10-13 (recommendations taken). Recommendations in brackets.

1. **text-textarea-mono-ascii-fits:** may a TextArea treat a printable-ASCII line in a proven
   fixed-pitch face (all 95 printable ASCII one advance, "MM" two) as fitting when (n + 1) × advance ≤
   width, without shaping it — as Junction's diff and Vesper's comparison already do? JSON open −86 %
   bytes, −4.15 MB kept after close, TODO.md −27 %. Risk: a mono face that kerned ASCII past one
   advance across a line would draw it clipped instead of wrapped. [Yes.] → **yes** (2026-10-13).
2. **text-open-measures-every-line (full):** may a TextArea measure only the lines on screen and
   estimate the rest of the scroll extent, refining it as lines are drawn (the scrollbar can shift)?
   [Not now: question 1 covers the apps' cases.] → **not now** (2026-10-13).
3. **Foreground UiTests:** WinFormsHostSampleTests and WebViewAccessibilityTests (12 facts) take the
   foreground on purpose for Tab traversal. Run them only with an explicit opt-in (e.g.
   `HULTUI_UITESTS_FOREGROUND=1`) so a `HULTUI_UITESTS=1` run never takes focus? [Yes, opt-in.] → **opt-in** (2026-10-13).
4. **idle-wake-latency-p90:** close the watch item as OS behaviour (a long-parked thread resumes
   slowly now and then; only ~60 wakes/s would hide it), or run the latency probe once with its window
   focused by hand? [Close it.] → **closed** (2026-10-13).

Reminder: remove the "aperture-survey" devices on jellyfin.home (rounds 2–4) and revoke the API key
when the work is done.


Round 3 questions, all answered (14–15 on 2026-10-10, the rest on 2026-10-11). Recommendations in brackets.

1. **mpv-vo-thread-minimized:** turn the video track off while minimized (`vid=no`; Chromium does
   this for background video)? Reel minimized: process CPU −38…−71 %; at every restore video
   resumes 84–300 ms late (today 5–36 ms), A/V sync off by up to 170 ms for ~1.3 s, a 66–68 ms audio
   stall in 2 of 3 runs. [Keep today's behaviour.] → **keep** (2026-10-11).
2. **Pointer moves once per frame:** (a) every pointer-move handler runs at most once per frame
   that may be drawn, with the last position (as browsers do; a drawing app would lose points
   without a coalesced-points API), or (b) only the splitter, pan, slider and scrubber? [(b).] → **(b)** (2026-10-11;
   first answered (a), changed to (b) the same day).
3. **ToastHost:** `onChanged` runs on appear, change and go only, not per fade step? [Yes.] → **yes** (2026-10-11).
4. **SelectionColor:** with reconcile-restyle-on-change, an app-set `TextElement.SelectionColor`
   would start to show (today the theme overwrites it; no app sets it)? [Yes.] → **yes** (2026-10-11).
5. **Animation clock** (with runtime-idle-wakeups): a tween started between frames starts its clock
   at its first step; frames shift by at most one interval? [Yes.] → **yes** (2026-10-11).
6. **layout-virtuallist-row-inherit:** rows of a one-argument VirtualList inherit text style from
   their list, as item rows and every other child do (0 px changed in every app screen checked)?
   [Yes.] → **yes** (2026-10-11).
7. **Added a11y events:** Name, Description and ExpandCollapse changes made by a rebuild
   (a11y-patched-props-untold); role changes (a11y-role-change); AT-SPI items and selection moves
   of lists over 2,000, on demand only (a11y-virtual-items-and-selection); extra children-changed
   for middle inserts (a11y-atspi-cache-child-index)? [Yes to all four.] → **yes to all four** (2026-10-11).
8. **TextArea:** apply round 2's rule (no whole-text Value change once it offers text: −121,744
   characters per keystroke at 2,000 lines), or keep Chromium's `<textarea>` mapping? [Apply the
   rule.] → **apply the rule** (2026-10-11).
9. **a11y-item-label-keep:** extend the item-rows contract to the a11y label (an equal item keeps
   its label until `InvalidateRows`)? [Yes.] → **yes** (2026-10-11).
10. **a11y-richtext-reader-keystroke:** a large change to the public AccessibleText for long
    documents edited with a reader running (1,974 KB per keystroke at 5,000 blocks)? [Not now.] → **not now** (2026-10-11).
11. **paint-native-blob-handles:** HultUI.Rendering.Skia calls three libSkiaSharp C functions
    directly (scroll/jump −62 % JIT, −74 % AOT; sub-blobs then cost no managed bytes); ties the
    renderer to SkiaSharp's C API at the pinned 3.119.2, as d049be7 did for HarfBuzz? [Yes, with a
    test that fails if the exports change.] → **yes, as long as headless still works** (2026-10-11)
    (headless rasterizes on the CPU through the same canvas: the headless tests, goldens and WSL
    Linux tests must pass with the native handles).
12. **aperture-hero-scrim-seam:** end the hero's horizontal scrim at 0 % instead of 15 % alpha
    (a visible seam at x 998/999; a deliberate pixel change, not perf)? [Yes.] → **yes** (2026-10-11).
13. **apps-junction-components:** re-open "split Junction into components" (declined 2026-10-06)?
    Junction History key: Build and reconcile 84 % of the bytes, unchanged titlebar, sidebar and
    toolbar 70 % of the Build; Meridian URL key: Build 92 %; Vesper: both panes rebuilt every key.
    [Yes, Junction's chrome first, one app at a time.] → **yes** (2026-10-11).

14. **vk-drag-swapchain-overallocate:** during a window drag, Vulkan windows (Aperture, Reel) keep
    an over-allocated swapchain presented unscaled and rebuild once at the exact size on release
    (resize step 37 → ~9 ms; video playing during a drag costs more CPU while dragging; one ~33 ms
    frame at release; maintenance1 on by default; NVIDIA verified only)? And fix the broken
    `HULTUI_VULKAN_FIXED_EXTENT=1` opt-in rather than remove it? [Yes; fix it.] → **yes, and fix
    the opt-in** (2026-10-10, after asking about 60+ fps video: playback unchanged outside a
    drag; window moves must not trigger it; video proof added to step 9).
15. **vk-8bit-unless-hdr:** 8-bit swapchain unless HDR content (video) is on screen — UI pixels
    change on HDR displays, a format switch when playback starts and stops? [Not now: A gets most of
    the resize gain without a pixel change; revisit for memory.] → **not now** (2026-10-10).

To do for the user when the work is done: remove the "aperture-survey" device registrations on
jellyfin.home (rounds 2 and 3, plus 51 runs of the resize diagnosis) and revoke the API key.

Round 2 questions, answered 2026-10-09 (the user took every recommendation; 11 yes):

1. Gate the Linux bridge on `org.a11y.Status` → **yes**, with an always-on override.
2. List items on demand at any size → **not now** (declined for plan 17 round 2).
3. Drop the Value change for elements with a text pattern → **yes**.
4. Range value events for Slider/ProgressBar → **yes**.
5. Added events for a11y-focus-unbuilt-node and a11y-virtual-item-selected → **yes**.
6. Tweens step once per rendered frame → **yes**.
7. `[ThreadStatic]` reactive scope → **yes**, low priority.
8. Colour Meridian responses over 20,000 lines → **yes, after** tokenizing moves off the
   UI thread.
9. Opt-in VirtualList item-rows API, adopted one list at a time → **yes**.
10. Tab strips that narrow then scroll → **outside plan 17** (not perf).
11. Install Mesa and Noto fonts in WSL Fedora → **yes**.
12. No-op glGetError — moot: 61edb19 showed the driver wait only moves (declined).

13. **wsl-atspi-stack** → **yes** (2026-10-10): installed at-spi2-core 2.60.7, dbus-daemon
    1.16.2, python3-gobject 3.56.3, python3-pyatspi 2.58.2 and gobject-introspection (needed for
    pyatspi's typelibs) in WSL Fedora; `import pyatspi` works. Live AT-SPI checks are now possible.

Note: the round 2 app survey registered a Jellyfin device named "aperture-survey".

Provided 2026-10-07: **Aperture API key file** at `C:\Users\fredr\.secrets\jellyfin-api-key.txt`
(outside the repo; never copy or print its contents). Use with the run-hultui-app driver:
`--app aperture --server http://jellyfin.home:8096 --api-key-file <that path>`. Real-library
measurement (poster grid, artwork decode, a11y on a large library) belongs to the app
workloads expert in the round 2 survey, and to aperture-poster-decode-scale's build. The
user should revoke the key when the work is done.

Answered:

- 1. Flex basis of truncating labels → **clamp** (2026-10-08, after the side-by-side renders):
  candidate layout-truncation-clamp-basis.
- Truncating labels shrinking below their longest word → **yes** (2026-10-08): candidate
  layout-truncating-min-zero.
- 2. Bounds events on scroll → **yes**, one scroll change instead: candidate
  a11y-scroll-bounds-event.
- 3. Stale focus state → **yes**, fix it: candidate a11y-focus-state-on-tab.
- 4. Aperture poster decode size → **yes**: candidate aperture-poster-decode-scale.
- Aperture hero images at 150 % → **yes, make them sharp** (2026-10-08): candidate
  aperture-hero-scale.
- Meridian response-pane breakpoint → **yes, move it to ~1320 px** (2026-10-08): candidate
  meridian-pane-breakpoint.
- Junction Branches compact table at ≤1000 px → **yes** (2026-10-08): candidate
  junction-branches-compact.
- 5. `MarkDirty()` joining an open batch → **no**: it stays synchronous, as REACTIVITY.md
  documents. The guidelines should tell app authors to call it once per handler.

Decided by the orchestrator: idle wake-ups count as a bar (brief updated); a change aimed
at per-frame allocation checks its bytes on AOT too (brief updated).

## Open

- **7a827e9 on Linux/KWin and on AMD/Intel Windows:** not measured. Linux keeps the chained
  path. If AMD/Intel do not stall, destroy-first may cost them 21–24 ms per resize, as it
  cost NVIDIA's IMMEDIATE and FIFO. A real Aperture resize run is also still to do.
- **0f5816a on AMD, Intel and Linux (GLX/EGL):** not measured (WSL Fedora has no GL
  stack; installing one needs sudo). Depth is a minimum in WGL/GLX/EGL, so 0 cannot fail
  where 16 succeeded.

## Log

- 2026-10-07: brief, ledger and `scripts/perf/idle-gate.ps1` written; round 1 survey started.
- 2026-10-07: user removed the round cap and allowed more roles. Split into eight: text
  pipeline out of SkiaSharp, reactivity and reconcile out of layout, accessibility
  bridges out of .NET, and a new app-workloads profiler. Experts 5–8 joined the round 1
  survey late; 1, 2 and 4 were told their narrower scope mid-survey.
- 2026-10-07: session restarted and moved to worktree `distracted-mendel-5071ba`; the eight
  surveys were resumed from their transcripts. All eight reported; no expert satisfied.
  Reports saved in `17-reports/`; survey worktrees removed (probe code kept in the
  scratchpad). Round 1 build started: 12 items, best first.
- 2026-10-08: round 1 build items 1–11e and the truncation app follow-ups landed (see
  Landed). Paused before 11f at the user's request; nothing uncommitted. Two questions open
  for the user (Needs the user). Nothing pushed or merged.
- 2026-10-09: round 1 full suite passed (30 projects, 0 failures); merged into main as
  64ba531 on a detached origin/main (main had 3dca7b4/de5f0e6 from another session), suite
  passed again on the merge, pushed de5f0e6..64ba531, primary fast-forwarded behind the guard.
- 2026-10-09: CI green on 64ba531 (run 37861109308). Round 2 survey started, eight experts.
- 2026-10-09: round 2 surveys in (all eight not satisfied); reports in `17-reports/r2-*`;
  queue re-ranked into 14 build steps; 12 questions for the user; survey worktrees removed.
- 2026-10-09: the user answered all 12 round 2 questions (recommendations taken; WSL install yes). Queued items 15–18.
- 2026-10-09: installed Mesa + Noto fonts in WSL Fedora (user approved); round 2 step 6 next.
- 2026-10-10: round 2 full suite passed (30 projects, 0 failures); merged into main as 0951288, pushed 64ba531..0951288, primary fast-forwarded.
- 2026-10-10: CI green on 0951288 (run 38072271269); tagged and pushed the five app releases (3 + 2).
- 2026-10-10: all five app releases published with installers (Vesper re-run once: runner not acquired). Round 3 survey started.
- 2026-10-10: round 3 surveys in (all eight not satisfied); reports in `17-reports/r3-*`. Three
  round 2 regressions (82e382b, 7e52ff5, 0912a5e) and a UIA closed-window leak lead the queue;
  16 build steps, 13 questions for the user; survey worktrees removed (probe code kept in the
  scratchpad). Paused for a break at the user's request.
- 2026-10-10: two user reports during the pause. Vesper's thumbnail area kept its old width after a
  resize until a key press: fixed e0b816f (Brief and Thumbnails rebuild on resize). Aperture's Home
  resizes much slower than other apps: an app-workloads diagnosis agent is measuring it (Vulkan F16
  swapchain vs whole-app rebuild per step vs card pictures); result goes into the round 3 build.
- 2026-10-10: Aperture resize diagnosed (`17-reports/r3-aperture-resize.md`): not the start screen —
  every step rebuilds the Vulkan F16 swapchain (92 %, 32 of ~37 ms; Reel the same; GL apps 4.6 ms).
  Drag over-allocation prototyped 37 → 9 ms; joins step 9 if approved (question 14); the
  FIXED_EXTENT opt-in is broken. Questions now 15.
- 2026-10-10: the user took the recommendations on 14 (yes, fix the opt-in) and 15 (not now);
  questions 1–13 still open.
- 2026-10-11: round 3 build resumed. Step 1 landed dd3dec3, 8cd854f (minimized-window
  regressions). Guideline notes: dispose MediaPlayer and web view controllers when they close (a
  live GpuFrameSink makes a dirty minimized window walk its tree); an app's own IGpuImageSource is
  released by setting Source = null.
- 2026-10-11: step 2 landed (c52e8fa..184b9d5): selection moves patched, not rebuilt (+ a11y-only
  frames draw nothing, c662dbf), AT-SPI status serialized, UIA text value from the node, UIA
  providers released on close. Windows and WSL test projects pass. New waiting item
  a11y-uia-host-reads-off-thread. Guideline notes: `SelectedIndex` costs no a11y rebuild up to 2,000
  items; in tests `PumpFrame()` is false for a frame only accessibility asked for.
- 2026-10-11: step 3 landed (88fa622..6902403): Junction History one rebuild per key; diff rows
  memoized on item rows; diff, comparison, viewer and thumbnail rows named for readers; two suite
  rows. Guideline notes: rebuild for a background result only if the build on screen read it;
  never derive list rows in Build (memoize by input identity, item rows + shape key, mutation-test
  every shape input); label every record/index list (ToString() dumps cost 744 KB per keystroke);
  removing allocations can move a gen-0 GC into a frame — compare frames without a GC.
- 2026-10-11: step 4 landed (0efc402..1e3edff): Reel playback session −87…−88 % UI-thread bytes,
  VideoPlayer rebuilds 290 → 5 per 10 s. Found: Aperture's and Reel's periodic playback duties
  (heartbeat, resume save) run in the app Build, which rarely runs during quiet playback; queued
  as step 4b (correctness) ahead of step 5. Guideline notes: read fast signals untracked in Build
  and write live elements from an OnMount Effect; read coarse facts through a Computed; keep an
  animation level in a plain field; new suite rows can shift other rows' JIT timings.
- 2026-10-11: step 4b landed (133bd8a..58fe439): Aperture's progress heartbeat and Reel's resume
  save now run during quiet playback; broken since each app's first commit, so the current and
  previous releases have it. Guideline: Build is not a clock — periodic work rides a ticking signal
  and checks `Application.Time`; state-driven work goes in an Effect disposed with what it watches.
- 2026-10-11: step 5 landed (7a8f516, 7754fe7): prefix tables in kept lists (long document open
  −58 % bytes, 20k-line log −64 %); span prefix measure for merged-cluster lines. **Release phase:
  run the WSL Linux tests** (text change; not run in the step). Guideline notes: build one-off tables
  in capped kept scratch lists and copy exact arrays out; measuring callbacks take text + length;
  a BiDi width cache must key on paragraph direction.
- 2026-10-11: the user answered questions 1–13: recommendations on 1, 3–10, 12, 13; (a) on 2 (every
  pointer-move handler once per frame); yes on 11 as long as headless still works.
- 2026-10-11: the user changed question 2 to (b): only splitter, pan, slider and scrubber coalesce.
- 2026-10-11: step 6 landed (dc4800d, 51f3126): style marking once per subtree (component edit
  −15 % JIT), theme switches lay out only what changed size. Guideline note (THEMES.md): ApplyTheme
  runs every style pass — mark layout only when a measured size changed, and mark a part whose box
  you wrote. For reconcile-restyle-on-change: `NoteChildJoined` must stay in the three add paths.
- 2026-10-11: step 7 landed (5cd0bdf, 6c70dbe, 4e85cdd): AOT blob building −56 B per blob,
  Powerline/task-tick frames ~0 B, merged-cluster lines 0 B per steady frame. Guideline notes: AOT
  and tier-0 JIT heap-allocate helpers warm JIT keeps on the stack — prefer struct APIs and check
  AOT bytes; pass polygon points as a span; a closure capturing a struct on a draw path costs ~120 B
  per call — cache by value on a shared owner.
- 2026-10-11: step 8 landed (27024f6, 5cc9892, b183aa0): native text blob handles (the user's
  headless condition met; a fourth C function, `sk_textblob_get_bounds`, was needed — told to the
  user) and per-stretch sub-blobs (spanned text −15…−70 % time). **Incident:** step 8's GPU probe
  window (Borderless, env var only) took the foreground four times, ~1 s each, while the user was in
  their browser; the brief's focus rule now requires the default frame, the in-process SDL hint and
  a foreground guard.
- 2026-10-11: step 9a landed (b0ed204, 58f78dd, 68424c2): Vulkan frees disposed-image textures
  (−178 MB DXGI on 8 photos) and keeps swapchain render targets (2 KB/frame → 0, frame −10…−16 %).
  **Focus, again:** an early 9a probe window (default frame, in-process hint) took the foreground
  twice; its guard handed it straight back and stopped the run. `WS_EX_NOACTIVATE` fixed it over
  several hundred runs; the brief's focus rule now requires it too.
- 2026-10-11: the user reported that when 9a's probe stopped mid-frame, Brave's videos paused for a
  while and recovered; information only (runs continue as they are). Brief: close windows
  gracefully, batch launches, log a timeline, stop on device-lost or stalls over 1 s.
- 2026-10-11: step 9b landed (c6bc336..939bf33): drag steps ~35 → 6–8 ms on Vulkan (MAILBOX).
  Incidents, told to the user: the focus guard fired once (21:18, Aperture's custom frame activates in
  its constructor — queued as customframe-install-activates); FIFO one-to-one probes hung (one window
  21:49–22:00 until terminated) → MAILBOX-only rule; **two mid-drag screen captures at ~23:40 grabbed
  the user's browser instead of the probe window** (the driver's HWND_TOPMOST did not raise it); the
  agent viewed one and deleted both. Captures are now in-process only and saved only when the probe's
  marker is on screen; the remaining four are 1618×904 window frames.
- 2026-10-12: the user is away until 18:00 (no load expected). Step 10 landed (4f43237..a4b5bbb):
  idle apps wake 0 times a second (was 61), minimized 0 (was 36–38); deadlines on time; focus guard
  never fired. Decided without asking: bound the tween first step by one interval (step 12), inside
  the approved answer to question 5.
- 2026-10-12: step 11 landed (4bd3576..b9e949e): a hover move costs only the platform's 40 B input;
  splitters, pans, sliders and the scrubber rebuild once per frame (−85…−94 % bytes per drag frame).
  Focus guard never fired. Guideline notes: hover styles cost nothing per move; a root move handler
  costs 64 B per move window-wide; an app's own move handler that calls MarkDirty still rebuilds per
  move (no public coalescing API).
- 2026-10-12: step 12 landed (bdd2aa3..752dcf1): toasts 23 → 2 app builds; Junction spinners and
  remote-op motion 0 rebuilds; Meridian's send readout no longer stacks; no extra first frame. Added
  step 17 (round 3 fix-ups): the GitLab test-fake race failed twice and would make the full suite
  flaky, a pre-existing toast opacity bug, and the custom-frame activation. Guideline notes: animate
  the element through a Ref; `onChanged` wired to MarkDirty fires on discrete changes only; a readout
  timer writes its label and stops with its owner.
- 2026-10-12: step 13 landed (5b71b4f..2808caf): TextArea edits re-wrap only touched lines (−8.3 MB
  held on a 20k-line log, typing −26…−28 %), text memos bounded by bytes (worst case −51…−59 %
  retained). Step 14 split: 14a framework parts + Vesper, 14b Junction lists + Aperture grid.
- 2026-10-12: step 14a landed (1e68166..0971573): rows inherit from their list (Branches layouts per
  key 255 → 34), kept item labels (−59 % a11y walk), Vesper on kept rows (−60 % per key). Guideline
  notes: a shape field changes only when what is drawn changes (normalise "nothing"); a label lambda
  capturing build state keeps nothing — hold the delegate in a field. Lead: `InvalidateRow(i)` would
  let Vesper's same-name partner remake ~4 rows a key instead of 32.
- 2026-10-12: step 14b landed (92224e3..73ec513): Junction Branches −72 %, Tags −53 % per key; Files
  on a virtual list (−99 % per key, −90 % time); Aperture grid −46…−64 %. Found and fixed a released
  bug (junction-v1.20.0): kept History rows lost their icons after a display-scale round trip. Step 17
  gains a framework safety net for it and Aperture's library paging bug (pre-existing). Guideline
  notes: per-row state goes in a value item from a lazy view, not the shape; keep the model objects
  between builds; display-scale glyphs belong in the shape.
- 2026-10-12: step 15 landed (d35cf89..2332fbe): rebuild-made name/description/expanded/role changes
  told; AT-SPI child order right in a caching client (10/10 live). Change 4 (skip the bounds refresh
  when nothing moved) reverted by the orchestrator (97ff373): it cost every element 16 B and slowed
  a11y/create 5 % for a case this round already removed. Two possible new events queued as round 4
  questions (AT-SPI reorder, IsEnabled through a rebuild).
- 2026-10-12: the user started the suggested task for the FakeGitLabHandler race in a separate
  session (step 17 leaves it alone) and asked to pause after round 3 is merged and released.
- 2026-10-12: step 16 landed (a52d481..c1095da): colour emoji on Linux and VS16 symbols on Windows.
  Guideline note: a per-character check in the itemizer must be gated by a vectorized scan.
- 2026-10-13: step 17 landed (8042e8e..a26ca68): toast replacement opacity, custom-frame activation,
  Junction remote-op test settle, item rows remade on a display-scale change, Aperture library paging.
  Round 3 build complete; full suite next.
- 2026-10-13: full suite on the branch passed (30 projects, 0 failures; Junction 969). origin/main had
  moved (8524328: the user's separate session fixed the GitLab fake race), so the merge was made on a
  detached origin/main (e260030), built and the full suite run again on it (30 projects, 0 failures),
  pushed 8524328..e260030, primary fast-forwarded behind the guard. CI run 38322327278 started.
- 2026-10-13: CI 38322327278 red on test-linux only: `SpannedLineSubBlobTests.TheCorpus_HasRunsInXOrder_
  AndRunsWhoseXGoesBack` ("122 runs in X order, 0 going back") — the runner has no Devanagari font, so
  no run goes back; a test precondition, not a flake or a code bug (no re-run). Fixed 58c186f
  (`[DevanagariFontsFact]`, as `[HanFontsFact]`), pushed e260030..58c186f (fast-forward), primary
  fast-forwarded. CI run 38323118109 started.
- 2026-10-13: CI 38323118109: test-linux green (the font fix worked); the Windows job failed once on
  `Junction.Tests.DiffItemLabelTests.TheLabelsAreMadeOnce` (label remade between two reads — likely
  Junction's watcher reloading after its own git writes, see junction-watcher-self-reload); re-run once
  per the brief: attempt 2 green on all three jobs. Tagged 58c186f and pushed 3 + 2 tags:
  junction-v1.21.0, vesper-v1.21.0, meridian-v1.4.0, aperture-v1.20.0, reel-v1.5.0; installer runs
  38324218701, 38324219011, 38324219212, 38324271540, 38324271987.
- 2026-10-13: Junction, Vesper, Aperture and Reel released with installers (Junction-Setup-1.21.0.exe,
  VesperCommander-Setup-1.21.0.exe, Aperture-Setup-1.20.0.exe, Reel-Setup-1.5.0.exe). Meridian's run
  failed test-linux on `SpannedLineSubBlobTests.ANewSubBlob_AllocatesNothing` (40.4 B a row against a
  bound of 40: a single-shot read caught a retired ~8 KB allocation context) — re-run once (attempt 2),
  and the bound fixed to 120 on the branch (b3e00b0; the no-presize mutation still fails at 279 B),
  pushed 58c186f..b3e00b0 to main (fast-forward), CI on it started.
- 2026-10-13: Meridian's installer re-run green; Meridian-Setup-1.4.0.exe published. CI 38326831433
  on b3e00b0 green on all three jobs. Round 3 closed: Round 4, paused before its survey at the user's
  request.
- 2026-10-13: resumed; the user approved both queued a11y events (AT-SPI reorder signal; IsEnabled
  changed by a rebuild). Round 4 survey started, eight experts.
- 2026-10-13: round 4 surveys in (all eight not satisfied); reports in `17-reports/r4-*`. One review
  objection each from 4 (lost wake since the infinite idle wait) and 6 (Vesper grid resize rebuilds);
  round 3 confirmed in the real apps. 14 build steps, 4 questions; survey worktrees removed.
- 2026-10-13: the user took every recommendation on the 4 round 4 questions (mono-ASCII fits yes;
  estimated extents not now; foreground UiTests opt-in; wake-latency watch item closed). Step 1 landed
  (ec4ced5, 397eca4, 94fc1ef): lost wake fixed, frame-pacing test at 200 Hz, TextBench warm-up.
- 2026-10-13: step 2 landed (c0560e1..ea4e01a): a key press draws one frame, not two (−40…−47 % frame
  time per press), no-op wheels draw nothing, Vesper idle moves draw nothing; `OnPassive(handler,
  listening)` public. Guideline notes: a mostly-idle move handler goes in OnPassive with a cached
  listening delegate; any ordinary handler costs a full frame each run (incl. a root KeyDown/PointerUp
  ignoring most input, any KeyUp); a control callback that changes something visible must mark dirty.
- 2026-10-24: step 3 landed after the weekly reset (968bd8d1, 1698b4a6, 9218026a, 78f3a22b): button
  enabled state and AT-SPI reorders told; status subscribed before read; UIA reads from the node (0 visits).
  Windows suites and WSL pass.
- 2026-10-24: step 4 landed (1e1948f9..a0221337): rich text caret moves patch the a11y tree (−99 % per
  key), share text with a reader, read-only reaches readers, and the TextArea text pattern (no
  whole-text Value per keystroke). All suites and WSL pass.
