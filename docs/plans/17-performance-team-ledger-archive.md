# Plan 17 ledger archive

Older parts of [the ledger](17-performance-team-ledger.md), moved out on 2026-10-27 so the ledger stays
short. **Search this file (by candidate id, commit or date); do not read it whole.** The orchestrator moves
finished rounds here at the start of each survey (see the brief, "Keeping the ledger short").

## Landed, rounds 1–4

| Round | Commit | Candidate | Before → after |
|---|---|---|---|
| 1 | 8a38ef4 | gl-vk-swapchain-rebuild-leak | Not a real VRAM leak (Task Manager bookkeeping; nvidia-smi and DXGI flat). Real cost: ~100 ms first-present stall per chained scRGB MAILBOX rebuild. Destroy-first for scRGB MAILBOX on Windows: resize 112 → 29 ms median, resize CPU 453–480 → 198–206 ms/s, stretched frames during resize 72 % → 0, Task Manager 1,416 → 173 MB per 40 resizes. IMMEDIATE, FIFO, 8-bit keep chaining. |
| 1 | 4315c4b | gl-no-depth-buffer | `SDL_GL_DEPTH_SIZE` 0, stencil 8 kept: GPU per GL window 55.3 → 46.8 MB at 2100×1350 (−15 %), 131.9 → 109.5 MB at 3818×2032 (−17 %); Task Manager, DXGI and nvidia-smi agree (~3 B/px). Start-up unchanged (probe first frame 427.8 → 427.2 ms, Junction 480.7 → 480.5). Junction, Vesper and a stencil-dependent FBO 0 scene byte-identical; a stencil-0 control broke the scene. |
| 1 | a2f6051 | paint-text-zero-alloc | Index loops on the text draw path, `PrefixMeasurer` closure moved out, pooled corner `SKPath`. allocKB −10…−96 % on 43 JIT / 44 AOT rows; dense text 18.1 → 0.8 KB/op (AOT 29.8 → 12.5); TextInput keystroke 44.9 → 2.5; render pass ~0.1 KB/frame on text screens; time flat; goldens byte-identical. |
| 1 | 9669d6b | runtime-aot-trace-type-name | Element trace names looked up only while recording (internal `FrameTracer.BeginElement`). AOT allocKB −10…−99.6 % on 40 rows, none up: resize 2,000 rows 685.6 → 2.9 KB/op (time −16 %), TextInput typing 59.0 → 2.4 (−10 %), deep nesting 75.5 → 4.8 (−12 %), tiles 7.7 → 0.7; AOT per-frame bytes now equal JIT. JIT flat. HULTUI_TRACE output identical event for event. Junction AOT frame not re-measured. |
| 1 | 615d75d | paint-image-zero-copy | `SetImmutable` on hand-over: SkiaImageSource's image shares the bitmap's pixels. Private bytes: Vesper 216 thumbnails 83.6 → 66.3 MB (−21 %), Aperture real library (27 posters) 85.0 → 67.3 MB (−21 %), GL window with 8 × 3840×2400 photos 1001 → 723 MB (−278 MB). Extrapolated up to ~260 MB at Aperture's 400-poster cap. Time and allocKB flat on all 83 suite rows; goldens byte-identical; GL readback exact. Vulkan not checked. |
| 1 | ebefa29 | aperture-poster-decode-scale | Aperture artwork requested at layout size × display scale (25 % steps), by the side that decides a cover fit; refetched on scale change with the old picture as stand-in. Real library, 27 pictures: decoded 17.6 → 4.4 MB at 100 % (−75 %), 9.9 MB at 150 % (−44 %), unchanged at 200 %; private 63.8 → 42.9 MB (−33 %) at 100 %, 63.2 → 54.1 (−14 %) at 150 %; Skia mip cache 5.9 → 0 MB. At the 400 cap 258 → 65 / 145 MB. As sharp or sharper (≥1 source px per device px). Same fix for cast, chapters and request-server pictures. |
| 1 | 42098f1, 7096431 | a11y-uia-wrapper-rebase | Every surviving UIA wrapper points at the current tree after each diff; stale wrappers dropped; no per-diff full-tree index. Trees held after 200 toggles 221 → 1 (30–34 MB → 0 retained); ApplyChanges 47–211 → 0.3–1.4 KB per diff; new Windows-only perf rows with the real UIA bridge on a hidden HWND: allocKB −22 / −29 / −30 % (JIT and AOT), label edit min −5.4 %; 0 slower. UIA event stream identical (1,118 events); sibling navigation now current. |
| 1 | 7b095f0, 6f42157, b23dcfb, bd72d97, 4030012, a23cd60 | runtime-frame-floor (+ layout walk half, hover common ancestor, slider/number-field paint) | Steady and idle frames 0 B (tiles, dense text, tweens 0.6–0.7 → 0.0 KB/op; control screen repaint 1,296 → 0 B); Tab 28.4 → 0.3 KB (AOT 34.1 → 0.3); theme switch −62/−56 %; hover −83 %; component rebuilds −5.6 % alloc, −5 % time; 32 rows ≥10 % down, none up, JIT and AOT; 0 slower. Junction/Vesper AOT frames not re-measured. |
| 1 | 0178784 | text-word-alloc-diet | Word cache looked up in place by a span (kept alternate lookup, per-run font hash), exact glyph arrays, widths without glyphs, LRU node reuse. allocKB −10…−60 % on 17 rows JIT and AOT, none up (text/ellipsis 7,261 → 3,418 KB/op, text/wrap 3,093 → 1,572, richtext/open 18,172 → 11,211, 600-span paragraph 1,924 → 776); 0 slower; text/open −10 %, richtext/open −26 % min; output bit-identical over 1.78 M layouts; goldens byte-identical. |
| 1 | aa80f11, d5d0c50, 927347b | runtime-text-services-per-window | One SkiaTextServices per application (SDL, WinForms, headless; pinned set separate), disposed with the platform; surfaces dispose only services they built. 600 open/close cycles: +4 font streams, +44 KB private, +1 KB managed per cycle → flat. WinForms handle recreation 808 → 3 streams after 200 cycles. New row window/open a second window 0.500 → 0.265 ms min, 262 → 132 KB/op; 0 slower on 86 rows. Windows share bounded caches now (one UI thread only). |
| 1 | 7e6fd57 | gl-vk-offscreen-scratch-pool | Scratch purge after the frame that follows a let-go F16 offscreen. GPU memory: 4-size cycle DXGI 215.4 → 154.1 MB (Skia pool 57.1 → 0.1), 40-step drag 458.5 → 242.5 MB (pool 203.0 → 0.1); nvidia-smi agrees. Resize time unchanged on drags and two-size toggles; +0.5 ms on new sizes, +1 ms (3 %) cycling 4 sizes. Unbudgeted alone changes nothing. GL has no such offscreen. Validation 0 messages, positive control 40. Trap: `GetResourceCacheUsage` counts only budgeted resources. |
| 1 | f47751a | a11y-build-diff-garbage | Exact-length child arrays (leaves share an empty one), index walks, positional pairing until the first difference, one owner-slot lookup per list. Build 230 → 144 B/node; unchanged diff 121.7 KB → 176 B (3,001 nodes). a11y/edit every 10th 568 → 368 KB/op (−35 %), time −9…−10 %; UIA rows −41…−47 % alloc, label edit −8…−12 % time; JIT and AOT; 0 slower. Diff identical vs the old differ over 3,300 random pairs; UIA event stream identical (1,118 events); Linux a11y tests pass. |
| 1 | 1cdfda9 | a11y-uia-reparent-wrapper | An element that moves to another a11y parent keeps its UIA wrapper; AT-SPI sends no cache-remove for a moved object and one children-changed:remove per moved child. Before, navigation to moved elements returned null and their events were dropped (6 of 16 in the reparent script). Other event streams identical (UIA 1,120, AT-SPI 6,886); allocation unchanged. |
| 1 | 9f500be | (measurement) | Windows-only perf rows `a11y/tab through 300 fields, UIA` and `a11y/wheel 1,000 rows, UIA`. |
| 1 | f88efff, b780615 | a11y-focus-state-on-tab | A focus move patches IsFocused on 2 nodes of the applied tree. UIA raises HasKeyboardFocus old/new before the focus event (was: at the next rebuild); AT-SPI answers current focus and drops its late duplicate signals. Allocation unchanged; Tab with UIA 0.351 → 0.350 ms. |
| 1 | 57f09d6 | a11y-scroll-bounds-event | Scroll view nodes carry ScrollOffset; UIA raises Vertical/HorizontalScrollPercent on the view (Chromium's mapping, `browser_accessibility_manager_win.cc`) instead of a BoundingRectangle per on-screen node: 846 → 6 events, provider callbacks 11,844 → 84 per 30 notches; bounds and offscreen read live. a11y/wheel 1,000 rows, UIA −54 % min, −62 % median. Node still 136 B (packed states). AT-SPI unchanged (sent nothing before). |
| 1 | 6891682 | layout-truncation-clamp-basis | A MaxLines(1) label in a row starts at min(untruncated width, room), from a pinned MeasureCache entry (no new element bytes). Ellipsis rows −19…−42 % time JIT, −20…−43 % AOT, allocation −21…−52 % (text/ellipsis 4.75 → 3.32 ms, 3,418 → 1,887 KB/op); measurements per step halved; 0 slower. Pixels: ellipsis slack ≤ 0.89 px; zero-room cells draw nothing instead of overlapping. Goldens byte-identical. |
| 1 | 81be6ca | layout-truncating-min-zero | A MaxLines(1) label's automatic minimum is 0 (nested boxes too); a label too narrow for its ellipsis draws nothing. Perf flat JIT and AOT. Fixes: Junction Branches name/tag overlap at 1200 px, Junction sidebar repo name/path running past the edge, Junction Tags subjects over the date column, Meridian's Send button off-window with a long URL. Exposed: Meridian's request tab titles and sub-tabs wrap at ≤1100 px and overlap the URL bar (follow-up 11e-fix); Junction's FlexShrink(0) "current" tag still overlaps at 1000 px. Goldens byte-identical. Images in scratchpad `truncation-minzero/`. |
| 1 | f72ce59 | apps-truncation-followups (Meridian) | Request tab titles one line (ellipsis, ≤220 px, narrow to 72 px, strip clips); sub-tabs one line in a sideways-scrolling strip; URL box clips; Chain, Assertions, History, Environments labels fixed. At 1100 px the editor gets its designed 228 px and the response pane fits (before: titles 4–11 lines deep over the URL bar, pane 179 px off-window). Breakpoint left at 1100 (asked the user). |
| 1 | 7feca72 | apps-truncation-followups (Junction) | Branch name cell clips; the name truncates first, then current/base/head/remote tags show whole or not at all; LAST COMMIT heading one line. No overlap at 1000/1200/1400 px. |
| 1 | a9b6970, e18b689 | aperture-hero-scale | Heroes requested at hero box × display scale (25 % steps), by the cover-fit side, rounded up to a 128-device-px step; stand-in on scale change or step; TMDB w1920 above w1280; only the last 4 heroes kept. 150 %: item hero 0.699 → 1.022 source px per device px, request hero 0.639 → 0.958; item page private 62.1 → 67.6 MB (hero 4.2 → 9.0 MB). Resizing 7 steps 96.9 → 66.0 MB (8 heroes → 1); 7 pages 137.6 → 96.8 MB (106.1 before 11f). Request page at 100 % +7.4 MB (w1920, strict rounding kept by the orchestrator). Measured against the real Jellyfin and Seerr. |
| 1 | 865f4f2 | meridian-pane-breakpoint | Response pane goes to the bottom below 1320 dip (rail 48 + tree 264 + editor 448 + pane 560); 1100–1319 px windows put the pane at the bottom (at 760 tall: pane 380, editor full width). Ctrl+\ unchanged; plan and README updated. Exposed: the bottom pane is a fixed 380 px, so a 1280×672 window (1080p at 150 %, maximized) leaves ~90 px of sub-editor (follow-up 11g-fix). |
| 1 | 9834cbc | junction-branches-compact | Branches/Tags compact below 934 px of content (date only, ↑/↓ and eye headings, icon Checkout named "Checkout"); below 724 px LAST COMMIT/COMMIT move to the tooltip. BRANCH at 1000 px with the sidebar 48 → 165 px, 1200 px 160 → 277, 900 px 0 → 196. Narrow mode also hides the GitLab lock, pipeline glyph and MR chip that live in LAST COMMIT. |
| 1 | 290a217 | meridian-bottom-pane-height | Bottom response pane keeps 380 dip in windows ≥ 736 tall and gives way below, with an editor floor of 316 (chrome + five form rows) and a pane floor of 162 (flex minimums, no rebuild on resize). 1280×672 editor 252 → 316 (header rows 3 → 5), 1100×600 180 → 316, 760×540 120 → 316; 1100×760, 1200×800, 1400×900 unchanged. No splitter (design token; Meridian has no settings file). |
| 1 | 9620a37, b3ae3cc | apps-scenario-virtual-selection-walk-a11y + apps-scenario-textarea-highlighted-json | 5 perf-suite rows (94 scenarios now): virtual/selection walk 1,000 rows (+ a11y bridge on, + UIA), text/open and text/wheel highlighted JSON (19,013 lines, 22,968 spans identical to HultUI.Syntax). JIT min 0.466 / 0.534 / 0.565 / 10.74 / 0.939 ms, 108 / 412 / 412 / 10,811 / 14 KB; realize ~10 % and a11y 12–18 % of the walk step (below the app survey's shares: 5 elements per row vs ~14 in Junction). JSON open median ≈ 2× min from GCs. |
| 2 | 4f795a3 | runtime-sdl-pump-closure | A real SDL loop pass allocates 0 B instead of 88 B (AOT 72). GL idle 5.5 KB/s → 0; GL spinner 3–4 MB/s → 0, no GCs; Vulkan idle 11.7 → 6.2 KB/s (display-colour poll left), spinner 4.45 → 0.41 MB/s (render target per frame left). Test on SDL's dummy driver runs in the normal suite. |
| 2 | 53604f3, 0207ff8 | runtime-paced-wait-spin | SDL on Windows rounded waits down (1 ms → 0); the wait now asks 1 ms more. Spinner 45–49k → 420–447 loop passes/s (224–245 → 2.1–2.2 per frame); UI-thread cycles −39…−57 %; frames/s 200 → 200 on GL and Vulkan, JIT and AOT; live resize still draws inside every step; headless suite flat. Linux/macOS not changed (SDL rounds correctly there per its source). |
| 2 | ba79958, 5805208 | vk-minimized-swapchain-lost | A Vulkan window that drew while minimized draws again: 0 → 26–31 frames in the second after restore; resize ErrorNativeWindowInUseKHR → 29–31 frames (F16 MAILBOX, 8-bit, FIFO). Minimized and dirty 252–264 → 47–49 loop passes/s; idle after restore 401–403 → 61. **Minimized windows are no longer drawn** (GL and Vulkan): GL with a 30 Hz change 129 frames per 5 s, 47–56 ms/s → 0 frames, 9–13 ms/s. Validation 0 messages (before: a leaked VkSwapchainKHR); resize stretch 0; suite flat. Video while minimized unverified (step 2b). |
| 2 | cfe2691 | media-minimized-playback | ba79958 stopped serving mpv while minimized: 236–240 drops per 10 s, avsync up to 151 ms, Reel's next file silent until restore (23.9 s), seeks 235–426 ms. A minimized window now serves its video undrawn (`IGpuImageSource.ServiceUndrawn`: setup + SKIP_RENDERING): 0 drops, avsync ≤ 2 ms, next file 589–612 ms, seek 45–48 ms. Audio was never broken. Process CPU at 1080p24 minimized 63–70 → 105–112 Mcyc/s (still below drawing: GL 162–178); mpv's vo thread does its normal per-frame work. |
| 2 | dc079b8, deadb3f | text-textarea-patch-keeps-lines | Same text handed back by a rebuild keeps the line starts (style/width/context/culture changes still re-wrap). New row text/rebuild around highlighted JSON 1.696 → 0.399 ms JIT, 2.051 → 0.424 AOT, 1,414 → 22 KB. Meridian URL keystroke with an 18,884-line response 3.00 → 1.70 ms, 1,612 → 182 KB. Fixed a real bug: row height kept the old font after a font-size patch. |
| 2 | b4377c7, a15a203 | text-textarea-span-scan | Binary search on a running max of End (Meridian's spans overlap; End goes down 1,180 times), one reused list. JSON wheel 0.834 → 0.399 ms JIT, 1.584 → 0.412 AOT (AOT gap closed), 14–16 → 5 KB. Meridian wheel with find open 7.4–8.1 → 1.05–1.13 ms (−86 %). Contract: hand a new span list to change spans. Trap: AOT grid rows move 7–16 % with unrelated code layout (README note). |
| 2 | e21e376, e7206c9 | text-shaper-direct | HarfBuzz driven directly, one buffer and font per shaper (in-use guard for shared scratch). text/create 4.24 → 3.26 ms (−23 % JIT, −19 % AOT), 5,792 → 4,317 KB; JSON open −22/−20 %, 10,802 → 6,595 KB; styled bidi ellipsis −16/−17 %, −50 % KB; 6 rows −25…−50 % alloc; TextBench cold/latin −20 %, hebrew −14 %; 0 slower. Bytes per call 936 → 176. Bit-identical vs SKShaper over 72.6 k calls on Windows and 51.3 k on WSL Fedora (Linux lacks Indic/Thai/emoji fonts: compared as notdef there). Handles flat over 10 M calls. |
| 2 | 898a65e, 330f207 | text-shape-cache-byte-bound | Shape/word/layout caches bounded by estimated bytes (24 / 4 / 4 MB; estimates within 6 % of measured), LRU by weight, one entry ≤ half the bound. Retained after close: CSV 23.2 → 4.2 MB, 5,000-block rich text 8.8 → 4.5 MB; 19k-line JSON kept on purpose (~22 MB: the TextArea's keystroke store until measure-lines lands). Hit rates identical on all 95 rows; 0 slower JIT; AOT grid moves are code placement (control build). Correction: the survey's 52/155 MB were probe-held documents; real totals 4–26 MB. |
| 2 | 1d61775, 5f6acea | paint-effect-object-cache | Blur filters (≤ 32) and gradient shaders (≤ 256, keyed by content + rect) kept between frames. paint/tiles shadow 7.3 → 0, gradient 11.8 → 0 KB/op, JIT and AOT; 0 slower; goldens byte-identical. |
| 2 | e9b76eb, 5f6acea | paint-blob-builder-reuse | One kept SKTextBlobBuilder: 216 → 136 B per new blob; scroll/jump 10.3 → 6.3 KB/op JIT (13.1 → 9.1 AOT); scroll wheels −17…−33 %; 0 slower. |
| 2 | 1eef320 | junction-lane-palette | History lane palette built once per theme, index loops: graph paint 16,224 → 0 B per frame (headless fixture). |
| 2 | a8bb07d | aperture-postercache-trim-thread | Correctness: the poster cache's evictions are disposed on the UI thread at the next Get (the download thread disposed images the UI could be drawing). ELEMENTS.md: dispose image sources on the UI thread. |
| 2 | 5da5b4c, db8f9f8, 7ae089c, 7755670 | paint-selectable-label-paragraph | A selectable/linked label keeps its paragraph and draws from it. Render-pass bytes per hover frame: Junction diff 10.6 → 0.0 KB (AOT 12.2 → 0), Vesper reader 10.9 → 0.0 (AOT 12.6 → 0); wheel −10…−11 KB/frame (rest is VirtualList realization). New rows text/selectable viewer hover 13.8 → 0.4 KB/op, wheel 14.7 → 3.3. Time flat, 0 slower, pixels byte-identical (pixel test over alignments, RTL, ellipsis, spans, selection). |
| 2 | b1f7efc, a91e0a1 | test-alloc-first-run-flake | Two causes. Headless: real code — the DevTools peak trackers' ring grew (536 B) while frame times fell; now a fixed 64-sample ring of time slices. Skia: no object at all — `GetAllocatedBytesForCurrentThread` counted the unused rest of the thread's allocation piece retired by a background-GC suspension (heap walk: one free object). All allocation tests now use `tests/Shared/Allocations.cs` (least of up to 8 rounds) with TieredPGO off in 6 test projects. Failed runs over 30 fresh runs per project: Headless 1 → 0, Skia 2 → 0; mutation checks still fail. |
| 2 | afbd13c, cc657e4 | layout-grid-linear-tracks | Grid cells bucketed in one pass, origins by prefix sums: grid/create −42 %, resize −54 %, edit −64 % JIT (AOT −61/−72/−78 %, placement control 3–6 %); 1.63 M loop steps per step → a few linear passes; bit-identical (random tests vs the old loops). Grid plan examined, not worth it. |
| 2 | 7856452 | layout-leaves-ignore-offered-height | `IgnoresAvailableHeight` defaults true; VideoElement opts out. controls/expander toggle MeasureSize −72 %, leaf measurements −93 %, row −7 % JIT / −6 % AOT, layout −39/−47 %. Every box identical across all 97 rows and ~556k audited cache hits. |
| 2 | d2ee688 | layout-maxlines-setter-dirty | Correctness: MaxLines and Overflow setters mark layout. Perf flat. |
| 2 | c135390 | checkbox-size-layout | Correctness: Checkbox, RadioButton and ToggleSwitch sizes from the theme re-lay out. Perf flat. |
| 2 | 67c4c22, 4195353 | a11y-node-extras | AccessibilityNode 136 → 80 B (rare fields in a side object). allocKB −13…−39 % on 9 a11y rows, JIT and AOT (checkbox UIA 142 → 87, edit every 10th 368 → 259, wheel UIA 425 → 260); 0 slower; events and trees identical. |
| 2 | 2235e4a | a11y-snapshot-stale-fields | Correctness: a UIA wrapper answers its node's current Role/AutomationId (Group → Dialog now 50032). No event change. |
| 2 | f9dc77d | a11y-atspi-snapshot-diet | AT-SPI path built once (TryFormat + span concat; WSL caught boxing on Fedora's .NET), string[] child paths, presized dictionary: ~500 → ~270 B/node; AT-SPI steps −38…−47 % (−44…−51 % with 67c4c22). Snapshots and signals identical; WSL green. No live AT-SPI client run. |
| 2 | 61aca67 | a11y-focus-unbuilt-node | Correctness (approved): a focus move onto an element mounted in the same handler is told after the next rebuild — +1 UIA focus and AT-SPI state-changed:focused (FindBar Ctrl+F, mount+focus); GetFocus answers. Perf flat. |
| 2 | 7dc7e0e | a11y-virtual-item-selected | Correctness (approved): list item nodes carry IsSelected; SelectedIndex notifies; a no-scroll move raises IsSelected on the old and new item on both bridges. UIA walk +0.2 KB/op (+2–3 %): one rebuild per move while a reader listens. |
| 2 | e877766 | apps-scenario-tween-signal-rebuild | Real-loop suite row via `HeadlessSession.RunLoop` (virtual clock, 1 kHz posts; loop is `Application.RunUntil`): 15.1 rebuilds per frame at baseline. |
| 2 | cc842ac | reconcile-blueprint-hashset | No set for a parent with no children: 288 → 80 B per `.Children` call; component edit/reverse −10.7…−11.0 % allocKB and −9…−17 % time (JIT/AOT), tabs −8.4 %, a11y/create −10.5 %; 0 slower. |
| 2 | 883943f, e5d626e, b10d022 | reactivity-tween-tick-per-frame | Animations step once per frame that may be drawn (approved). Row 15.1 → 1.0 rebuilds/frame, −93.7 % KB, −7/−8 % time. Reel (AOT, Vulkan, 11 s): VideoPlayer rebuilds 616–653 → 312–313, 2.1–2.2 → 1.06 per frame, UI-thread 25 → 13 MB, gen-0 1 → 0, frames 511–518. e5d626e fixed 883943f holding drawn tweens to the 16 ms idle wait (120 Hz tween 89 → 121 fps). Minimized auto-hide and toasts run to time. |
| 2 | c2bf858, 336d2f9 | reactivity-scope-threadstatic | `[ThreadStatic]` reactive scope (approved): 160 → 0 B per effect run; fan-out 94.4 → 16.2 KB/op (−83 %), −6 % JIT; reads off-thread or after an await no longer subscribe; DECISIONS.md updated. |
| 2 | 91efc25, cf59b97 | layout-zorder-sorted-allocs | Z-order kept on the child list, filled on first ask, dropped by child-list changes and the ZIndex/Type setters (layout never runs for a handler's ZIndex write). Sticky wheel 3.1 → 1.9 KB/op JIT (−38.7 %), 3.5 → 2.3 AOT; 5,000-row wheel −6.7 % JIT, −12.0 % AOT (placement control ≤ 2.8 %); jump −11.2 % AOT. Junction with a panel open: 1 sort + 52 scans per frame → 0. 0 slower. Child list 32 → 40 B, PositionStyle +8 B. |
| 2 | 54a48c3 | maxlines-setter-thread | Fix-up of d2ee688: setters mark only what is not already marked, so `.MaxLines(1)` on a new label off the UI thread no longer throws in Debug; a laid-out label still re-lays out and is thread-checked. Perf flat. |
| 2 | 2317632, d1f118d | runtime-palette-filter-allocs | Joined text kept while the palette is open, title-only commands matched once, one array sort: palette row 592.5 → 430.3 KB/op JIT (−27.4 %), 651.9 → 424.3 AOT (−34.9 %); min −11.1 % JIT, −7.3 % AOT; Filter 167/357 → 4.7/9.0 KB per query; results identical to the old code. |
| 2 | c2dbebd | runtime-syntax-registry-first-use | `CanHighlight` answers from a 297-extension table checked against the registry. Vesper's first code folder AOT 7.0 → 2.2 ms and 5.3 MB → 761 KB (JIT 32–38 → 4.4 ms); the TextMate registry loads at the first tokenize instead. |
| 2 | fc00b75, a9cc75f, 46a1219, 9b801d1 | text-textarea-measure-lines | Width per hard line measured without layout (`IRenderContext.MeasureTextWidth`, bit-equal; 4 MB width cache with a weight); last frame's line strings reused; shape bound 24 → 8 MB. JSON open −21 % JIT / −20 % AOT (median −35 %), 6,599 → 1,903 KB; typing in 2,000 lines −22 %, 465 → 222 KB; undo-redo −28 %; steady TextArea repaint 2.2 KB → 0; 10,000-line JSON keystroke 1.66 ms even at 8 MB (was 25 ms at 8 MB). Meridian first response frame 12.4 → 5.8 MB. Retained after closing the JSON 9.6 → 4.3 MB. JSON wheel +3.4 KB (lines shaped when drawn). Line starts identical over ~2 M lines. AOT TableView sort +5…7 % unproven placement. |
| 2 | f122cc9 | gl-vk-resize-duplicate-frames | One live-resize frame per change, not per event: frames per SetWindowPos step 2–3 → 1 on GL, F16 and 8-bit, never 0, never at an old size. GL drag CPU −33 % (AOT −31 %), 8-bit −13…−33 %, F16 flat (driver-side swapchain rebuild); F16 drag submits Skia 278 → 120, host 119 → 40. Held live-resize timer 90 frames/s → 0 with video still drawn (mpv 0 dropped). Resize stretch 0 of ~1,000 samples. |
| 2 | 2170222 | a11y-atspi-active-on-status | Linux bridge active only while org.a11y.Status says AT is on (HULTUI_ACCESSIBILITY=1 overrides). 603-node keystroke with no reader 212.8 KB → 1.54 KB (= no bridge). Live-verified in WSL with pyatspi (off: 1-object tree, 0 events; on: full tree; off/on republishes). |
| 2 | ff94faa | a11y-value-for-text | No Value change for nodes that offer text (Chromium-cited): UIA rich text keystroke −16.7 % JIT / −14.0 % AOT; the whole-document ValueValue (1.5 MB BSTR) and AT-SPI accessible-value (~750 KB) gone; Value still readable on request. TextArea unchanged (no text pattern yet). |
| 2 | 240d468, de851f6 | a11y-richtext-text-snapshot | Rich text editor's accessible text made on request (versioned). a11y/type in rich text 2,100 → 54 KB/op, −43 % JIT / −48 % AOT (UIA −39/−46 %). Streams identical. AT-SPI with a reader still builds it per keystroke (its text-changed needs it). |
| 2 | 444ba70 | a11y-range-value-events | Range on nodes; UIA RangeValue property changes; AT-SPI Value interface + accessible-value; told at most every 250 ms (Chromium's media timeupdate rate). Progress tick rebuilds 60 → 5 per second, −45 % JIT / −52 % AOT; VideoPlayer seek now announced (0 → 19 per 10 s). Live-verified. |
| 2 | 2ec88fa, 1095eb4 | linux-bridge-sync-context | Correctness (found by the live check): the Linux bridge's connecting constructor hung under HultUI's UI SynchronizationContext. Now connects at once; samples/AtSpiVerify bulk check PASS (also live-verifies f9dc77d). |
| 2 | 12d9742 | meridian-response-format-on-ui-thread | Prettify, split, line starts and tokenize on the thread pool, cancellable (new response, mode, tab switch, close); plain first above 2,000 lines, colour when spans land. 18,884-line JSON: UI time to text 108.4 → 2.53 ms (−98 %), to colour 4.04 ms; 312 KB response 3.8 → 1.4 ms. Headless JIT. |
| 2 | 5943f4f | syntax-maxlines-uncoloured | Meridian colours responses up to 200,000 lines (JSON ~5.5 µs a line on a worker; 98k lines 0.54 s, 4.7 MB of spans; the 2 M-character cap stops JSON near 100k lines). Merged find list kept between builds: URL keystroke with find open 8.4 → 1.8 ms, 2,945 → 178 KB. Junction/Vesper keep the 20,000 default (already on workers). |
| 2 | bff6de2, 2a12cf4 | layout-virtuallist-item-rows | Opt-in `.RenderWith((item, selected) => …)` (`ItemRowTemplate`): kept rows whose item (EqualityComparer) and selected state are unchanged; `InvalidateRows()` for anything else; theme object switch re-makes rows. Twin row virtual/selection walk 1,000 item rows: rows per step 19 → 3, 96.8 → 25.7 KB/op (−73.5 %), −6 % JIT / −10 % AOT; layouts −72 %, resolves −40 %; a11y tree and pixels identical to the old template; 0 slower. |
| 2 | 73b1c10 | junction-history-item-rows | Junction History on item rows (shape key: stats, compact, theme): rows per key 17 → 2.83; realize 0.155 → 0.072 ms JIT, 0.20 → 0.079 AOT; 212 → 41 KB per key (−80 %); frame bytes −53 %, frame time −7 % JIT / −10 % AOT (headless, 80 keys). 16/17 frames byte-identical; the 17th fixes hover lost on a key press. |
| 2 | 6aa8259 | wsl-shaper-real-fonts | e21e376 bit-identical on real Linux faces: 116,438 shaper calls, 0 differences, 80 faces, 0 missing glyphs in every script; Text 192/192, goldens 39/39 in WSL. New test that the corpus reaches real glyphs (mutation caught). |
| 2 | 7ac8117 | wayland-minimized-restore | Correctness (found in WSL): after the app minimizes its own window on Wayland, SDL keeps the minimized flag after a restore, so since ba79958 the window stayed undrawn (0 frames in 15 s). A focus gain after a minimize now ends the minimized state on Wayland. Also verified: 4315c4b on Mesa gets depth 24/stencil 8 anyway (no stencil-only config; no saving, no risk, 48 contexts, 0 failures); Vulkan on lavapipe and Dozen pixel-exact through resizes, validation 0 messages; destroy-first not taken on Linux. WSL Linux suite: 9,917 passed, 0 failed. |
| 3 | d3d81ee | vesper-grid-resize | User report (2026-10-10): after a window resize the thumbnail area kept the old width until a key press. Brief and Thumbnail grids are sized in the build from the window, but a resize rebuilt only for a scale change or a wrapped diff/viewer. Now rebuilds when either pane shows Brief or Thumbnails. Correctness; two headless tests (fail without it). |
| 3 | c25f057 | gl-vk-minimized-children-flag | Regression fix (ba79958): a change made while minimized rebuilds the a11y tree once, not every loop pass; the root also keeps paint dirt so the restore draws (a style- or layout-only change while minimized now draws at restore too). One element added while minimized, UIA listening, real window never focused: a11y rebuilds 61/s → 0; GL 2,016 elements 10.8 MB/s → 29 B/s, UI 33.8 → 12 Mcyc/s; Vulkan 10.8 MB/s → 10.7 KB/s (display-colour poll); 5,016 elements 26.8 MB/s → 29 B/s, 69–74 → 21–22 Mcyc/s. `HULTUI_A11Y=0` unchanged. Perf suite 0 slower/faster. MinimizedWindowTests (mutation: 31 builds for 1). |
| 3 | 351e309 | gl-vk-undrawn-sources-walk | Regression fix (cfe2691): a dirty minimized window walks for GPU sources only while one can exist (process-wide count of live `GpuFrameSink`s and of other `IGpuImageSource`s set on images). Tree visits 122k–447k/s → 0 without video; UI −10…−20 % Mcyc/s; what is served unchanged (cfe2691's real-window tests 7/7). Both commits: GL 31–35 → 7.7–8.5 Mcyc/s, Vulkan 33 → 9.6–10.1 while minimized with a change. Perf suite 0 slower/faster (richtext/open min noise, median −1 %). Remaining: the per-pass frame start of a dirty minimized window (~60/s) is step 10's. |
| 3 | d46008c | apps-scenario-selection-walk-real-loop-a11y | Suite rows `a11y/selection walk beside details, keys 60 ms apart, real loop, {bridge on, UIA}`: one Down key then `RunLoop` on the virtual clock to the next key; 1,000 commits on item rows beside an unchanged details pane; reports a11y trees built per key. Shows 7dc7e0e's regression: 1.0 trees/key → 0.4 after 3730052. |
| 3 | 3730052 | a11y-selection-move-patch | Regression fix (7dc7e0e): a selection move marks IsSelected on the changed item nodes of the applied tree and hands the bridge a patch (`IAccessibilityBridge.ApplyPatchedProperties`, default = apply as a diff; UIA and AT-SPI override) instead of forcing a rebuild; falls back to a rebuild for no tree, a list mounted in the same handler, count change, composite rows, crossing 2,000. Event streams identical on 7dc7e0e's 12 scripts and two walks, except UIA VerticalScrollPercent 28 → 16 in a 200-item scrolling walk (one per refresh, not per key). Junction History AOT, UIA listening, 80 keys at 60 ms: a11y trees 82 → 50, a11y bytes per key 293.6 → 179 KB (−40 %), UI bytes 553 → 438 KB (−21 %). Walk row 380.6 → 227.6 KB/op, min −8…−15 % JIT/AOT; 0 slower. 9 + 3 tests, 7 mutations caught. |
| 3 | a86de0d | a11y-undrawn-frame | Found in step 2: 3730052 alone made a refresh coming due between keys its own full repaint (+20 % frame time per key). A frame only accessibility asks for now stops after the a11y phase: not drawn, timed or counted (`PumpFrame`/`RunFrame` return false for it; live resize still draws a clean window with posted work via internal `RenderFrame(drawClean:)`). Junction History net d46008c → a86de0d: frame time per key −9 % (1.386 → 1.266 ms mean), bytes −21 %, a11y bytes −40 %; drawn frames per key 1.02. Walk median −14 %/−6 % JIT, −11 %/−1 % AOT; 0 slower. With a client listening, coalesced refreshes (scroll trailing edge, range values, attach) no longer repaint. |
| 3 | f878ec4 | a11y-atspi-status-serialize | Correctness (2170222): status reads one at a time (`AtSpiStatusReader`), re-read when a change arrived during a read. Live WSL (pyatspi, private bus, bursts of four changes): wrong final state 5/40 and 7/100 → 0/40 and 0/100 (every wrong case: status on, bridge off). 2 mutations caught. |
| 3 | 7ab0ba7 | a11y-uia-text-value-from-node | Thread safety (ff94faa/240d468): ValueValue and the Value pattern's GetValue for a node with text answer from `Node.Value` (editor: the told text version; terminal: the screen as the tree read it), no live-tree walk on a UIA thread. 2 mutations caught. |
| 3 | 8ac5362 | a11y-uia-disconnect-on-close | Leak closed (found by 4): Dispose calls `UiaReturnRawElementProvider(hwnd, 0, 0, NULL)` after restoring the window procedure. 150 open/close cycles of a 60-row window, UIA listening: managed 36,693 → 1,289 KB, private 174–177 → 140 MB (`HULTUI_A11Y=0`: 1,185 KB / 139 MB). Perf suite (with 7ab0ba7, captures bbed443): 0 slower; heavy-allocation row medians −15…−30 % and a full run 32 → 28 s, likely the suite's own UIA rows no longer leaking sessions (not measured further). |
| 3 | b985e74 | apps-junction-prefetch-rebuild | App: a highlight that lands rebuilds only when the build on screen asked for that key (`_highlightShown`, cleared each Build); otherwise cached for Commit/Changes' first frame. History, Down every 500 ms, AOT: builds per key 2.00 → 1.00; a11y off 469.9 → 268.8 KB per key (−43 %), frame 1.96 → 1.13 ms (−42 %); a11y on 1,042 → 573 KB (−45 %), a11y trees 2.00 → 1.06. At 60 ms/key bytes −2…−4 %. HighlightRebuildTests (4 mutations caught). |
| 3 | 0720260 | apps-junction-diff-flatten-per-build | App: diff rows flattened once per (diff, split, highlight, theme) object and the list on item rows with a `DiffRowShape` (staging context, wrap, pan, code font, theme, find state) + `InvalidateRows`. Changes typing beside a 184-row hunk, a11y off, AOT: 1,217.9 → 233.4 KB per keystroke (−81 %), rows made 39 → 0, frame 1.82 → 1.36 ms (−25 %); a11y on 1,962.8 → 978.6 KB. Commit no-op keys 650.7 → 272.2 KB (−58 %), frame −18 %. Review mode unchanged (rows still made per build). DiffRowKeepTests (9; 11 mutations caught). |
| 3 | 22f1139 | junction-diff-item-labels | App, a11y correctness: diff rows named for readers ("added, line 12: …", hunk headers, split pairs, threads) instead of a `DiffRow { … }` dump, made on first ask and kept with the rows. Changes typing, a11y on: a11y 744.4 → 31.5 KB per keystroke (−96 %), total 978.7 → 265.4 KB, a11y share 27 → 4–5 %. Step net with a reader: 1,962.8 → 265.4 KB (−86 %), frame −31 %. |
| 3 | 8a3fa9b | vesper-item-labels | App, a11y correctness: Vesper's comparison (removed/added/changed/"12 unchanged lines"), viewer ("line N: text") and thumbnail rows (the row's file names) named for readers, cached. One a11y tree of a 400-line comparison 1,093.5 → 40.5 KB (−96 %, headless JIT). ItemLabelTests (5 mutations caught). |
| 3 | 6902403 | apps-scenario-type-beside-diff | Suite rows `input/type beside a 150-line diff, rows {made in Build, kept}` and `a11y/…, bridge on`: made in Build 381 KB/op, 37 rows per key (JIT 0.635 ms) vs kept 19 KB/op (0.519 ms); bridge on 579–653 vs 33–34 KB/op labelled. 108 scenarios; `--verify` passes; full compare 0 slower/faster JIT and AOT. |
| 3 | e019d7d | apps-scenario-video-bar-fade | Suite rows `media/video player playing, position 10/s, real loop` and `media/video player bar fade, real loop` (FakeMediaBackend, `RunLoop`, internal `VideoPlayer.Builds`): baseline 1.0 build per report at 409 KB/op, 31 builds per fade at 1,235 KB/op (AOT 395 / 1,191). They run last: placed before `a11y/`, three a11y rows read 20–25 % slower on JIT only (JIT state, README note). |
| 3 | fb5d935 | reactivity-videoplayer-position-in-place | An OnMount Effect writes the elapsed label and scrubber value through Refs; Build reads Position untracked. Per report 41,095 → 420 B, 1 → 0 builds; suite row 408.8 → 12.4 KB/op (−97 %), min −14 %. RangeValue (≤ 250 ms) and seek announcements, drag and hover preview unchanged. REACTIVITY.md "A value that changes ten times a second", MEDIA.md: read Position untracked in Build. |
| 3 | f80d57a | media-videoplayer-fade-rebuild | The fade level is a plain field; the tween writes it and the live bar's Opacity through a Ref (hidden 0, paused 1, else the level). `_controlsVisible` flips at the same moments. Per fade frame 40,872 → 927 B; suite fade row 31 → 2 builds, 1,238 → 88 KB/op (−93 %). Reel AOT, Vulkan, 10 s 1080p24 with pointer sweep and seeks, step net: VideoPlayer rebuilds 287–291 → 5, UI-thread 12.1 → 1.42 MB a11y off (−88 %), 12.4 → 1.55 MB on (−87 %), quiet frame 13.7 → 2.9 KB, mpv drops 0 → 0. |
| 3 | 697e73b | media-patch-keeps-layout | Canvas, Slider (marks layout when ThumbRadius changes), VideoElement and the player root opt into PatchMarksOwnLayout: a player rebuild that moves nothing costs layout 6 → 0 µs per scrubber-hover move and no empty a11y rebuild 100 ms later. DECISIONS.md updated. Perf compare flat. |
| 3 | f5fafaf | apps-chapters-computed | App: Reel's and Aperture's chapter panels read the playhead through a `Computed<int>` chapter index; the app rebuilds only when a mark is crossed. Panel open, per report: Reel 66,591 → 591 B, Aperture 179,530 → 1,752 B, 1 → 0 builds. REACTIVITY.md shows the pattern. |
| 3 | 2316a4d, f553732, 3ffa5ac, b867e2a | apps-playback-pump-out-of-build | App correctness, **broken since each app's first commit** (not plan 17; releases aperture-v1.19.0/reel-v1.4.0 and the previous ones affected): the time-based playback duties ran in the app Build, which barely runs during quiet playback (Aperture 1 build, Reel 0). Aperture's 10 s progress heartbeat and Reel's 15 s resume save never went out: 0 in 25/33 s at f7b3f0a/5cc2a30, a70abde, fecfbbc, 0951288 and 626b0b9 → 2 after the fix. State-driven duties (resume seek, tracks, chapters, volume, Ended) worked. Fix: two Effects per player made when the stream opens — state (State, Duration, tracks; Position untracked) and clock (Position + `Application.Time`); nothing left in Build. App builds per 60 s quiet playback 0 → 0. `FakeMediaBackend.HoldOpening/FinishOpening` for load-order tests; 17 mutations caught. REACTIVITY.md "Build is not a clock", MEDIA.md trap. |
| 3 | 7d6cc85 | text-prefix-table-scratch | `BuildPrefixWidths` builds in two kept lists (runs cleared after each build; segments capped at 4,096 kept) and copies exact arrays out. Bit-identical over 500,000 random strings in 15 scripts × 5 styles. KB/op JIT (AOT within 0.2 %): text/open long document 3,418 → 1,442 (−58 %), richtext/open 11,248 → 10,027 (−11 %), richtext/resize −10 %, text/wrap resize −11 %, wrapped 900-line typing −10 %; time flat on min, medians −6…−22 %; 0 slower. 20,000-line wrapped log open 202.7 → 73.3 MB allocated (−64 %), later opens 125 → 102 ms. TextBench ellipsis-2lines −36 % B/call. |
| 3 | 99e1157 | text-prefix-measure-span | New public `TextPrefixMeasurer(string text, int length)` and `TextLine` overloads (CaretXAt, OffsetAtX, VisualRanges, TryMoveCaretVisually) so merged-cluster lines measure prefixes without cutting substrings; old `Func<string,float>` overloads kept (a literal `null` measurer is now ambiguous; no caller). `MeasureWidth` now caches BiDi-path widths keyed by paragraph direction (TextArea BiDi lines hit the cache too; DECISIONS.md updated). Hover frame with two merged-cluster lines −192 B (JIT 1,464 → 1,272, AOT 1,336 → 1,144); the rest (closures) is expert 2's step 7. Bit-equal prefix widths; 10 mutations caught. Not run on WSL: run the Linux tests in the release phase. |
| 3 | 218f831 | layout-style-mark-once | `DirtyFlags.StyleSubtree` claim: the style mark walk stops at an element whose subtree is already marked; cleared with Style; a mounted unmarked child joining a claimed parent drops the claim up the chain (`NoteChildJoined` in the three add paths), so marked sets are unchanged. Mark visits: component/edit 44,004 → 18,004, tabs −76 %, a11y/create −54 %, palette −71 %, Junction History −75 %, Branches −64 %, Tags −76 %, Files search 383,482 → 60,731 per key. Every other counter and allocation identical on 110 rows. Time: component/edit keyed 1.458 → 1.246 ms JIT (−15 %), −6 % AOT; unkeyed −9/−10 %; reverse −10/−8 %; 0 slower. Shadow audit: 0 differences over ~4.3 M elements in 10 suites (positive control 2,548). 18 tests, 7 mutations caught. |
| 3 | 2af43b9 | layout-applytheme-layout-on-change | Five leaf controls mark layout in ApplyTheme only when a measured value changed; Expander marks the parts whose written sizes changed (fixes a header keeping 32 px on a 48 px theme); Toast's ApplyTheme no longer calls ReplaceChildren (no Children/a11y rebuild per restyle). Theme rows: layouts 78 → 0, MeasureContent 100 → 0; light/dark 8.7 → 0.0 KB/op, −6 % JIT / −7 % AOT; named scheme 10.9 → 2.2 KB/op, −6 %. THEMES.md control-author paragraph. 14 tests, 17 of 18 mutations caught (the 18th's code removed). |
| 3 | 480f294 | paint-raw-run-buffer | `AllocateRawPositionedRun` (struct): per new blob 136 → 80 B (AOT and tier-0 JIT; warm JIT kept the class on the stack). AOT KB/op: scroll/jump 9.1 → 6.3 (−31 %), nested 3.0 → 2.2, wheel 2.3 → 1.8, sticky 2.3 → 1.9, selectable viewer wheel 4.0 → 3.3, richtext wheel 1.0 → 0.6; JIT unchanged; 0 slower. (e9b76eb's "136 B wrapper" was 80 B wrapper + 56 B run buffer.) |
| 3 | dbcdaef | paint-polygon-pooled-path | `FillPolygon`/`DrawPolyline` reuse the kept SKPath (Rewind); Terminal `BoxDrawing.HalfEllipse` uses a stackalloc span. Per forced frame (JIT = AOT): six Powerline lines 12,880 → 1,744 B; 40 ticked tasks 3,224 → 24 B. Pixels byte-identical (6 shots, scale 1 and 1.5). The kept SKPath is shared by polygons, polylines, corner clips and the canvas context: nothing may hold it across those calls. |
| 3 | f0238b2 | paint-cluster-line-measurer | One `TextPrefixMeasurer` per style, cached by style value (≤ 64) on SkiaRenderContext and on TextPipeline (`PrefixMeasurer(style)`, used by PipelineTextParagraph). Hover frame with two merged-cluster lines: JIT 1,272 → 312 B, AOT 1,144 → 312 B (= the ASCII baseline: the lines now cost 0 B); with selection 1,400/1,240 → 312. 0 slower. |
| 3 | 3891499 | paint-native-blob-handles | Approved (question 11, "as long as headless still works"). Text blobs are native handles through SkiaSharp's C API: `sk_textblob_builder_make`, `sk_canvas_draw_text_blob`, `sk_textblob_unref` **and `sk_textblob_get_bounds`** (a fourth, for TextPaintBounds), resolved from the library SkiaSharp loaded; a missing export throws by name. Released once (cache empty, Dispose, finalizer). KB/op JIT = AOT: scroll/jump 6.3 → 2.4 (−62 %), nested −45 %, sticky −37 %, wheel −33 %, selectable viewer wheel −33 %, richtext wheel 0.6 → 0.2; new blob 80 → 0 B; 0 slower. Condition met: Headless 767, Rendering.Skia 250, 17 golden scenes byte-identical, 27/27 non-outline headless frames identical, GL and Vulkan readback identical, WSL Fedora 10,012 passed / 0 failed. Leak test: 100k blobs < 32 MB (81.9 MB with unref removed). DECISIONS.md records the pin. |
| 3 | 8ddc3eb, ea6c48e | paint-spanned-line-sub-blobs | Each colour stretch draws a kept native sub-blob of only the glyphs whose ink (face box + 1/8 em, 3/8 slanted, + 2 device px) reaches its clip; colour/bitmap/untrusted-box faces draw whole runs. Glyphs per frame −73…−91 % (Junction diff hover 10,650 → 2,763; spans recolor 99,167 → 8,734). Time: Junction diff hover −15/−17 %, wheel −19/−21 %, Vesper reader hover −19/−21 %, wheel −25/−27 %, spans recolor −70/−71 %, spans bold −53/−55 % (JIT/AOT); suite spans recolor −65/−67 %, bold −24/−41 %, rich text rows −9…−14 %; richtext/wheel 0.2 → 1.3 KB/op from a one-time map growth (not presized: ~57 KB per window). Pixels byte-identical (in-process A/B over 8 scripts × 3 scales × 5 transforms, 17 goldens, 27 frames, GPU readback). WSL 10,030 passed / 0 failed. No win yet on variable fonts (Cascadia Mono). |
| 3 | 9084be9 | vk-uitests-no-foreground | Test harness: the Vulkan probe UiTests' windows get the in-process SDL hint, `WS_EX_NOACTIVATE` and a foreground watcher that fails the test (exit 5) if one is ever taken. |
| 3 | 5002e2f | vk-purge-disposed-image-textures | Sources disposed after a draw are counted (any thread); the Vulkan surface runs its scratch purge after a frame once the count moved, at most once a second, no GPU wait (0.02–0.2 ms). 8 × 3840×2400 photos drawn then disposed: DXGI 391.5 → 213.2 MB (−45 %), Skia purgeable 176 → 0 MB, nvidia-smi +355 → +175 MiB, private 538–586 → 401 MB. Poster-grid churn (27 disposals/s): uploads 25.8/s unchanged (no re-uploads), frame time no slower. GL untouched (driver keeps the memory). |
| 3 | 4c3c1af | vk-swapchain-target-per-frame | One GRBackendRenderTarget + SKSurface per swapchain image for the swapchain's life (`SwapchainTargets`); F16 blit with DrawSurface; host contract `SetSwapchainReleasing` (targets dropped before images go) and `TransitionAcquiredImage` (barrier to COLOR_ATTACHMENT_OPTIMAL: just before the blit on the offscreen path, at once on the direct path; a frame after a GPU source draw wraps per frame as before — a barrier at every acquire slowed mpv's GL draw, 1.25–1.47 vs 0.97–1.14 ms). Idle-animating window: 2,046–2,048 B/frame → 0 (F16 and 8-bit, JIT and AOT), frame min −10…−16 %, median −7…−15 %; vkCreateImageView/Framebuffer per frame 1 → 0. Reel HDR playback 2,580 → 530–552 B/frame (8-bit video unchanged by design), mpv drops 0. Validation (SDK 1.4.357, core + sync) clean on reused-frame captures (40th frame, 3 resizes + minimize, F16/offscreen/scaled/8-bit/FIFO/IMMEDIATE/fixed extent); positive control (no barrier) `VUID-vkCmdDraw-None-09600` ×10 — NVIDIA drew correctly anyway, so the capture UiTest runs under the layer. HDR.md invariant rewritten. |
| 3 | 6458cc8, 7864f5c, aefd2d7, 16f81bc | vk-drag-swapchain-overallocate | Approved (question 14). maintenance1 on by default on Windows; a drag (first size change inside WM_ENTERSIZEMOVE…EXITSIZEMOVE, seen by a new comctl32 subclass `WindowsSizeMoveWatch` on every Vulkan window incl. Reel's default frame) over-allocates a one-to-one swapchain the window still fits (kept on SUBOPTIMAL) and rebuilds exact at the end; a title-bar move never over-allocates (`SizeMoveTracker`). **MAILBOX only:** under FIFO a one-to-one swapchain hung for good (acquire fence never signals, 3 repros); under IMMEDIATE its first build cost 172–196 ms and showed a stretch — both keep exact rebuilds. `HULTUI_VULKAN_FIXED_EXTENT=1` fixed (60–65 → 6.9 ms/step; `=0` turns maintenance1 off). Surface-format query cached on kept steps; Resize skips the target drop when the host keeps the swapchain. Median ms per drag step JIT/AOT: probe 32.9 → 5.6 / 34.5 → 5.9–7.8; Aperture Home 35.5 → 8.1 / 36.5–38.1 → 8.0–8.1; Reel idle 33.9 → 5.6–7.3 / 33.2–35.2 → 5.8–7.3; p95 36–41 → 7–12.6; builds per drag 80 → 5; release frame 29.6–36.3 ms; Aperture AOT drag CPU 6.6–6.9 → 1.3–1.4 Gcycles. Move: 0 builds before and after. Video drops per drag (Reel AOT): 1080p60 35–36 → 0–2, 1080p120 228–241 → 11–12; release 0 → 0 (60) / 0 → 2–3 (120); A/V desync max 17.5–20 → 10–13 ms (60). GPU memory during a drag 299–318 → 321–322 MB, back to 214 after. Stretch 0/519 and 0/494 sampled (positive control 64/581); mid-drag capture 0 px differ; validation 0 messages incl. maximize/snap/minimize (positive control fired). The 157–163 ms FIXED_EXTENT outlier was the first swapchain replacement after show (190–290 ms on every path). |
| 3 | 3c81fb6 | apps-tooling-generic-framelog | `HULTUI_FRAMELOG` (`1` = stderr, else a file) + `HULTUI_FRAMELOG_SECS`: per-interval passes/frames/ticks, idle and paced waits with early returns, time parked, deadline kinds, posts and wakes by source, inputs, bytes per pass and input, GCs, heap, private bytes, windows. One static readonly bool when off. ENVIRONMENT.md. |
| 3 | aa11334, 46e5b33, faf8ab2 | runtime-idle-wakeups | The idle wait runs to the next owned deadline (animations; a11y trailing refreshes 100/250 ms only while a client listens; resize throttle; inspector; Vulkan colour poll only without colour events) or forever; wakes from AccessibilityActionRouter enqueues, AT-SPI status flips (`IAccessibilityActionHost.ActiveChanged`), first WM_GETOBJECT, and any UI-thread work done inside the wait (WebView2, OLE, DispatcherQueue); a non-empty post queue is a deadline due at once (46e5b33 fixed a lost post). Real windows, JIT and AOT: idle 60.6–60.9 → 0.0 passes/s (all five apps, GL and Vulkan with colour events); Vulkan polled 60.7 → 10.6; minimized 36 → 0.1; minimized dirty 38.2 → 0.7. On time: spinner 120 fps unchanged; a11y layout refresh 102–112 → 100–101 ms (due 100), range 250–265 → 250–252; live resize 1.5 frames/step unchanged; Reel 1080p60/120 0 drops; WSL AT-SPI DoAction 4.9–8.0 → 1.7–3.4 ms. Input post → frame median unchanged, p90 in a background window 0.74–1.11 → 2.49–5.03 ms (watch item). Suite 0 slower, 2 faster; the real-loop selection walk now meets its 100 ms refresh (0.4 → 0.6 trees/key, by design). |
| 3 | 173fc89 | reactivity-animation-deadline | Approved (question 5). `AnimationManager.NextChangeIn`; toast hold, tooltip show delay, VideoPlayer auto-hide and six Terminal timers are delays (`Animate.After`); in the loop a tween's first step is 0 (no jump after idle). A 1 s toast every 3 s: 44.5 → 31.2 passes/s, animation steps 30 → 13.7/s; tooltip every 3 s 12.1 → 2.6 passes/s, delay 486–500 → 500.6–502.6 ms (no longer early). Cost: an extra identical first frame per tween (toast row +4 % time) — step 12 bounds it. |
| 3 | 9740353 | winforms-idle-ticks | The WinForms host's 8 ms timer ticks only when something is due; wakes post a tick. Idle 62.9 → 0 passes/s; spinner 63 fps unchanged; toast every 3 s 64 → 8.1; worker post → frame median 16.2 → 0.6 ms, p90 16.9 → 4.8; toast end ~2 ticks later. |
| 3 | 8d9e831 | apps-scenario-loop-wakes | Four `loop/` suite rows: idle 62.6 → 0.2 passes/s, minimized and changed 62.6 → 0.2, toast held 2 s 71.3 → 19.7, tooltip delay 66.4 → 8.0. |
| 3 | 243643e | gl-uitests-focus-hardening | The in-process GL loop UiTests get `ForegroundGuard` (SDL hint, CBT hook with WS_EX_NOACTIVATE and activation refused, 5 ms watcher). Other GL UiTests (incl. WinFormsHostSampleTests' `EnsureForeground`) still unhardened. |
| 3 | 8055fc2, cfa1ec8, 1608999 | runtime-pointer-events-on-demand | PointerMoveEvent made only if something on the pinned path listens; Enter/Leave made at the first listener and shared up the chain (handlers added mid-walk still heard); PaneSplitter/HorizontalPan's idle drag trackers register as passive-and-not-listening (`OnPassive(handler, listening)`, condition on the delegate target so handler entries stay 16 B). B per move incl. the 40 B MouseMoveInput: within an element 104 → 40, hover change 216 → 40; real windows (60 Hz posted moves, HULTUI_FRAMELOG, GL and Vulkan, JIT and AOT) synthetic/Junction/Reel 216 → 40, UI thread 13.0 → 2.4 KB/s; Vesper 216 → 104 (its root move handler); Meridian 418 → 242 JIT. Suite input/hover 0.2 → 0.0 KB/op; 0 slower. |
| 3 | fefb81c, 3bbe2f9, 9569e6e | reactivity-pointer-rebuild-per-frame (b) | User's answer (b): only PaneSplitter, HorizontalPan, the slider (hover, OnChanged during drag, OnHoverValue → VideoPlayer scrubber) apply the latest position once per frame via internal `PointerFollow`/`IPreFrameHost` (run at the start of RenderFrame, no allocation); release, press, keys, a11y value sets and PanTo flush first, so a drag ends exactly on the pointer (Junction pane 431.6 → 438.0 for a 200 px drag); every other handler keeps every move. 1 kHz moves, real loop: 120-row splitter app 16.1 → 1.0 rebuilds/frame, 10,499 → 655 KB/frame (−94 %); Junction Commit splitter 8.1 → 1.03, 1,782 → 267 KB (−85 %); scrubber hover 15.7 → 0.97, 628 → 43 KB (−93 %); slider drag 10.3 → 1.0, OnChanged 309 → 30. New suite rows (117 rows): splitter drag −57 %/−62 % time JIT/AOT, 63.0 → 4.6 MB/op; slider drag −53/−59 %, 30.8 → 2.1 MB; scrubber hover −6/−9 %, −93 % bytes. REACTIVITY.md "A drag rebuilds once per frame"; ELEMENTS.md. |
| 3 | 4aa6358 | apps-scenario-toast | Three `loop/` rows over a ~300-element screen built anew: toast (23.0 builds/toast, 5,941 KB/op at first), spinner rebuilt per step (1.0 rebuilds/frame, 7,630 KB/op) vs turned in place (0.0, ~0 KB). 120 rows; `--verify` passes. |
| 3 | c3ed8ca | reactivity-toasthost-fade-in-place | Approved (question 3). The fade writes `_opacity` and the mounted toast's Opacity through a Ref; `onChanged` on appear, change and go only; the hold stays a delay. Per toast: builds 23 → 2 in Junction, Meridian and Aperture; Junction 4,505 → 389 KB (−91 %, 6,215 → 959 with the bridge), Meridian 1,544 → 132, Aperture 3,139 → 272. Timing and screen-reader stream unchanged. Suite toast row 5,940.8 → 513.4 KB/op, −16.6 %/−21.9 % time JIT/AOT. |
| 3 | 518f90b | apps-whole-app-tween-rebuilds | App: Junction's remote-op icon motion and spinners write live elements through Refs; Meridian's 150 ms send readout writes its label and stops with its own send (ticks used to stack on repeated sends). Junction fetch 61 → 0 builds per 61 frames, 11.40 → 0 MB (14.18 → 0 with the bridge), still 60 fps; busy spinner 12.84 → 0 MB; AOT 0 B. Meridian repeated sends 11.9–17.9 builds/s → 0, 1,548–2,308 → 1.4 KB/s. Aperture has no such clock. |
| 3 | 6a18847, d721937 | tween-first-step-bounded | The loop's first tween step is min(time since the loop's last step, 1/DisplayHz) (Run and the WinForms host; RunFrame/Tick unchanged); a tween that starts with a delay (tooltip, toast hold, auto-hide; internal `ITween.Delay`) still starts at 0 so it never fires early. A 300 ms tween after idle: 20 → 19 frames, done 316 → 299 ms; toast flash → gone unchanged; the extra identical first frame gone. Allocation identical on every row. AOT TableView 10k sort +4.4…7.1 % in 5/5 pairs, JIT ±0.3 %, attributed to AOT code placement (evidence in the commit). |
| 3 | dc6c047, 6cb96e1 | text-textarea-incremental-wrap | Line starts kept and patched in place: untouched rows stay, later rows shift by one vectorized add, only touched lines re-wrap; the text-keyed wrap memo is gone; replaced runs matched in order (hash lookup after 8 misses) so new text sharing its ends stays cheap. Fixed an old bug: an edited Arabic line at narrow widths could break unlike a fresh layout (break search started from a carried width). Wrapped 20,000-line log: typing 8,777 → 6,353 KB/key (−28 %; the rest is the new 6.3 MB string), 4.9 → 2.1 ms JIT, 5.0 → 2.4 AOT; held while open 8.63 → 0.34 MB (−8.3 MB). 19k JSON typing 981 → 724 KB (−26 %), 0.92 → 0.77 ms. Suite: 2,000-line typing −7 % KB, −5 %/−2 % min; wrapped 900-line −8 %, −7/−3 %; undo-redo −8 %, −7/−4 %; 0 slower. Equivalence: 1,500 random edits in a test (mixed scripts, widths 37–2,400, font changes), 12 × 2,500 soak, fc00b75's audit 10.7 M lines 0 different; WSL passes. |
| 3 | bff5581, ec42c45, 896e99a | text-memo-cache-byte-bound | BiDi/styled memos and remembered lines bounded by weight too (10 MB and 12 MB; `LruCache.Reweigh`), `MemoRange` keys hold an entry number so evicted entries are not kept alive; weights within 3–5 % of full-GC measurements. Retained after the texts go: 2,000-char Hebrew/Latin 64.7 → 26.5 MB (−59 %), styled 45.2 → 22.1 (−51 %), 400-char 32.9 → 26.0. The 5,000-block rich document re-open unchanged (31.18 MB). Suite 0 slower; styled ellipsis −2.4 % KB. 896e99a fixed a flaky memo test (a string gets its memo on its second layout if no other string took its admission slot). |
| 3 | cfb9b01, 26afda3 | layout-virtuallist-row-inherit | Approved (question 6). One-argument rows resolve under their list (`ResolveUnder`). Per key: Junction Branches style-pass layout marks 240.7 → 0, layouts 255 → 33.7, ComputeLayout 975 → 478, leaf measures 830 → 205; Tags marks 153 → 0, layouts 166.7 → 30.7; Vesper walk marks 120 → 0, layouts 136.6 → 76.6. Branches/Tags min −4.6/−4.8 %, median −6/−7 % JIT; Vesper flat. Pixels: inherited-style diff over 7 suites + probe walks showed only Box/Image FontSize (unused); 15 headless screens 0 px different (positive control 3,994 px). ELEMENTS.md. |
| 3 | bd0fd07, 6af2d0a, ee585cf | a11y-item-label-keep | Approved (question 9). Item-rows lists keep `(item, label)` per index while the item is equal and the delegate the same; `InvalidateItems` (InvalidateRows, new Items, theme) and a new delegate clear it; UI thread only (lists over 2,000 are read off-thread). New row `a11y/selection walk 1,000 item rows, bridge on` 274.1 → 112.3 KB/op (−59 %), min −5.0 % JIT / −7.6 % AOT; real-loop walk 278 → 181 KB/op. New public `HeadlessWindow.AccessibilityTreesBuilt`. Contract documented on AccessibleLabel, ItemRowTemplate, InvalidateRows, ELEMENTS.md. |
| 3 | 0971573 | vesper-item-rows | App: Vesper's panes (SelectedIndex = cursor, −1 inactive; `PaneRowState` shape: partner path normalised, size/date/zebra, results, scale, drop target, marks/cut/folder-size snapshots), comparison and viewer on item rows. Pane walk 360 keys: other-name folders 561 → 222 KB/key (−60 %), min −11 % JIT / −16 % AOT (a11y on −60 %, −10.5/−14.6 %); same-name folders −30 %, −9/−14 % (the F3 partner moves each key). Rows made per key 60 → 2.65; leaf measures 3,099 → 174. 23 headless frames byte-identical. KeptRowTests 18, 21 mutations caught. |
| 3 | f12d789 | junction-branches-tags-item-rows | App: Branches/Tags TreeNode rows memoized (`RefRowsMemo`: refs identity, query, flat/tree, open-folders generation); item rows with the caret as SelectedIndex; `BranchRowItem(Node, Index, Picked, Role)` from a lazy view so a pick or base/head change remakes only those rows; label delegates in fields. Synthetic repo, headless, JIT and AOT: Branches 756–775 → 212–219 KB/key (−72 %), min −10 %/−13 %; Tags 726–745 → 340–349 (−53 %), min −10 %; rows made 19.3 → 1.7 and 17 → 2.7. 30 frames byte-identical; 24 mutations caught. |
| 3 | b7f2847 | apps-junction-files-virtual | App: the Files tree is a `VirtualListElement<FileTreeRowItem>` (Node, Open, Cursor, Expanded) with visible rows memoized and a path index for the cursor; `RowsAreComposite`. 2,300-file search, Down key: 16.8–17.0 MB → 173–176 KB (−99 %), min 9.9–14.0 → 1.00–1.04 ms (−90 % AOT, −93 % JIT), median −95 %; bridge on 18 MB → 190 KB; elements 14,128 → 257; resolves per key 14,128 → 266; hit visits per move 3,502 → 34; render culls per frame 3,474 → 7. a11y: on-screen nodes identical over 23 states; off-screen rows no longer published (a VirtualList cannot publish them as tree items), no new events. 23 frames byte-identical; 13 mutations caught. |
| 3 | b10a34c | itemrows-scale-shape | Bug fix (hits 73b1c10 from round 2, released in junction-v1.20.0, and 0720260/f12d789): kept rows were not remade on a display-scale change, and the retired glyph rasters were freed by the next one, so after 150 % and back every icon in Branches rows was gone (1,753 px; History 36 px). `Ui.DisplayScale` added to the four Junction shapes; round trip 0 px. ELEMENTS.md: put the scale in the shape. Framework safety net queued in step 17. |
| 3 | 921605c | aperture-grid-item-rows | App: the poster grid on item rows, `GridRowItem(Row, CursorColumn)` from a lazy view; shape: loaded count, in-place patches, cards per row, artwork landings, display scale. 600 films: Right 359–362 → 130–131 KB/key (−64 %), Down 360 → 196 (−46 %); rows made 6 → 1.1/2.5; time flat (headless raster dominates). 16 frames byte-identical; 10 mutations caught. |
| 3 | bb7a652, c417d44 | a11y-patched-props-untold | Approved (question 7). Role, AccessibleName, AccessibleDescription, AccessibleExpanded, AutomationId, Name and the Label of Checkbox/RadioButton/ToggleSwitch notify on change; the differ compares AutomationId (no event; UIA answered the old id before). 13 standard scripts identical; a new script tells each change at its own step (UIA Name, HelpText, ExpandCollapseState; AT-SPI accessible-name, -description, expanded). 26 tests, 9 mutations caught. Allocation identical on 121 rows; 0 slower. |
| 3 | e91961d, 996d24e | a11y-role-change | Approved. The differ reports Role first; UIA raises ControlType and LocalizedControlType; AT-SPI sends property-change:accessible-role (not for the application root). Live WSL: a caching client's role now matches after a change in both directions. 6 tests. |
| 3 | ba2f8a1, a0d5822 | a11y-atspi-cache-child-index | Approved. The survey's live check was invalid (its driver cleared the cache). libatspi overwrites a slot on cache-add and reinserts on children-changed:add: before, 6 of 10 insert cases left a wrong cached list. Now removals first, then one children-changed:add per added child in index order (the run ending the parent's children keeps one signal), cache-adds last. Live: 10 of 10 cases match. An `AtSpiClientCache` model reproduces the live results in tests. UIA unchanged. |
| 3 | f5dcbad, 6d517d0 → d38564a | a11y-layout-refresh-moved-only (reverted) | Correct (1,155 shadow audits empty) but +16 B per element, +0.5…+1.9 % allocation on element-building rows, a11y/create +5.3 % min JIT, and its own probe paid more bytes than it saved; the common case it helped (whole-app rebuilds per animation frame) was removed by steps 4 and 12. Reverted by the orchestrator per the brief; numbers in DECISIONS.md. |
| 3 | e5aed2e, dbcb7d8, 22189da | linux-emoji-colour-fallback | Correctness, pixels changed on purpose. UTS #51 presentation: Emoji_Presentation characters, a pictograph or keycap base + VS16, the first character of a modifier or ZWJ sequence go to the colour emoji face ahead of the primary face; plain ™ © # 1 and ❤/↔ without VS16 stay text; VS15 turns the preference off. New `EmojiPresentation` (Unicode 16.0 tables checked against ICU), new public `IFontCollection.MatchEmoji` (default null), `SkiaFontCollection.MatchEmoji` (fontconfig `und-Zsye`; DirectWrite by family name; colour faces only). Windows: #️⃣ 1️⃣ *️⃣ ™️ ©️ ®️ ↔️ ▶️ ☝🏽 now Segoe UI Emoji; Fedora: 😀 ☕ ⚡ ❤️ now Noto Color Emoji, keycaps/☝🏽/❤️‍🔥 one glyph. Broad text pages byte-identical on both OSes; goldens unchanged. Perf 0 slower/faster (a first version cost +6/+11 % on long-document open and was replaced by a vectorized gate); new row `text/create 2,000 unique emoji labels`. WSL 10,216 passed. |
| 3 | d2630a5 | toasthost-second-flash-opacity | Bug since ToastHost's first commit (546bd05, every Aperture release): a second Flash during a fade kept the fade's level, so the replacement stayed half drawn for its hold. It now rises from its current level to full at the fade-in's pace (same in-place step), holds at 1, fades out normally; 2 owner rebuilds. |
| 3 | 48527ac | customframe-install-activates | `WindowsCustomFrame.Install`'s SetWindowPos (since 01acd91) gets SWP_NOACTIVATE: a custom-frame window no longer asks for activation while it is made. CustomFrameActivationTests under ForegroundGuard (Borderless fails without the flag; Default is the control). |
| 3 | ee4004a | junction-remoteop-test-flake | Test timing, not 518f90b's code: Junction's GitWatcher sometimes reloads after the app's own `.git` writes (2 of 40 starts), landing builds inside the measured window. `Settle` now waits for 700 ms of real time with no build (cap 3 s). 50/50 under two concurrent suite copies. |
| 3 | 6c726ea | itemrows-remake-on-scale | Framework safety net: Window remembers the display scale; on a change it calls internal `Element.OnDisplayScaleChanged()` over the root and overlays before `Resized`, and item-row VirtualLists drop their rows (as on a Theme switch). One float compare per resize. The apps keep the scale in their shapes (one compare per build); Junction's five scale tests pass through the framework alone. |
| 3 | fc05131 | aperture-library-paging | Bug since f7b3f0a: pages requested by window height only, so End, arrows and the wheel stopped at the 100th film; a landed page waited for an unrelated rebuild. Now the next page loads when the furthest realized row or the cursor comes within a page of the loaded end; End goes to the last item (up to 1,000 per request); the build reads `_gridPage.Value` so a landed page shows; a second after-frame reveal. 250 and 1,000 films reach the end; End ≤ 3 requests. |
| 4 | 671cf74 | runtime-sdl-wake-flag-after-poll | Correctness (aa11334 with eba3197): `PumpEvents` clears `_wakePending` after `PollAllEvents` (in a finally, Interlocked.Exchange), so a wake landing between the clear and the first poll's OS pump (window messages, live-resize watchers, main-thread callbacks) is no longer swallowed. Dummy-driver test through the real PumpEvents: a wake 50 ms into a 3,000 ms wait ran 3,001 ms before, ~50 ms after. Real window: idle 0.0 passes/s, spinner 199 fps unchanged, 0 lost in 3,000 wake pairs. |
| 4 | e645fd2 | test-frame-pacing-event-precondition | The storm thread spins to a schedule (Stopwatch + SpinWait, 1 ms or four per display interval above 250 Hz) and the precondition scales with the display: at 200.01 Hz 512 → 1,008 events, all 4 FramePacingTests pass under ForegroundGuard. |
| 4 | 421ba2f | tooling-textbench-warmup | The JIT warm-up runs on a second scenario set following the measurement's index sequence, cold rows get a new string per call, warm-up runs ≥3 s then until the compiled-method count is flat for 1 s (cap 30 s), and a health line prints. Methods compiled during measurement 914–960 → 126; run-to-run spread 2× → ≤12 %; cold rows −1…−6 %, resize rows −7…−60 % min (the old warm-up measured them partly at tier 0). Old TextBench captures are not comparable across this change. |
| 4 | 5250e1f, a500141 | events-noop-input-frames | Every input follows the move rule: a frame when an ordinary handler ran for an event the input dispatched on that event's path (key, press with focus/blur, release with click, ScrollEvent, drag enter/over/leave), hover changed, something marked itself dirty, a drag was active before/after, focus moved, or the keyboard-focus flag flipped; F12 marks the root; the safety net for app handlers stays (HULTUI002). Audit of every owned/passive handler; one miss fixed (Terminal's IME composition clear did not mark). Shadow audit: 69/70 skipped no-op inputs pixel-identical (the 70th a test's off-thread echo). Real windows AOT, UIA listening: frames per key press 2 → 1; frame time per press Junction 2.0 → 1.18 ms (−42…−44 %), Vesper 1.64 → 0.92 (−40…−47 %); no-op wheel notches 16–20 → 0 frames. New rows: key press 1.326 → 0.696 ms JIT (−47.5 %), −45.9 % AOT; 20 no-op wheel notches 5.4 → 0.005 ms. 0 slower. ELEMENTS.md "Which inputs draw a frame". |
| 4 | 4000186, 2629cd1 | apps-vesper-passive-move-handlers | Vesper's root and pane-row PointerMove handlers and Junction's review rows are passive and listen only while they act; **`Element.OnPassive<TEvent>(Action<TEvent>, Func<bool> listening)` made public** (API addition). Vesper: 60 idle moves along a row 57–60 → 1 frame; 104 → 40 B per move. Tests incl. right-drag mark, Find grip, review drag. |
| 4 | 9fd01e26 | button-isenabled-rebuild | Approved. `ButtonElement.IsEnabled` notifies (the only control with an enabled state). UIA IsEnabled and AT-SPI enabled + sensitive now told for a direct off/on and in a rebuild's own frame; live WSL: a directly disabled button stayed enabled in the client's cache 2/2 → 0/2. Allocation identical on 124 rows. 8 tests. |
| 4 | d942d16d | atspi-reorder-signal | Approved. One children-changed:add per child out of place in the client's cached list (previous snapshot minus departed children), added children through the same pass, a trailing run keeps one signal. Live WSL: all 7 reorder shapes read the old order before; 17/17 insert and reorder cases match after. Apply after reversing 1,000 children 265.4 → 287.3 KB (999 signals). UIA unchanged. 15 tests on the `AtSpiClientCache` model. |
| 4 | 6a10a76f | atspi-status-subscribe-first | `AtSpiStatusReader.Watch` subscribes to org.a11y.Status, then makes the first read through the same reader (a change during subscription or a slow first read is read again); live WSL 29/29. |
| 4 | c88af01f | a11y-uia-host-reads-off-thread | Every UIA pattern read (value, toggle, range, item selected, list selection, find, scroll, on-demand items) and RaiseScrolled answer from the applied node; new rare node data `ScrollExtent` and `ItemSource` (API additions; `IUiaHostBridge` reads stay, unused). Visits per UIA read on a 1,533-element page 1,494–1,532 → 0; scroll refresh UI-thread reads 2 → 0. Events and trees identical in 18 scripts; pattern answers now match their properties (a switch's Toggle pattern said indeterminate). a11y/create +0.2 KB/op; 0 slower. |
| 4 | 08ae00cd, 6348e810 | apps-scenario-text-caret | Suite rows: rich text caret walk beside 300 rows (recording, UIA), the walk in 5,000 blocks with a reader reading, typing and a caret walk in a 2,000-line TextArea (UIA); 6348e810 kept the walk's editor at 300 px (it had shrunk to 10 px). |
| 4 | 5a4465c4 | a11y-text-caret-patch | A rich text caret/selection move swaps a new text version onto the applied tree and hands the bridges a TextSelection patch (no tree); AT-SPI reads offsets from the versions. Per key: beside 300 rows recording 78.8 → 1.0 KB, UIA 79.2 → 1.1, AT-SPI 319.3 → 1.2; 5,000 blocks AT-SPI 474.9 → 1.2; trees per move 1 → 0. Caret walk −17.5/−20.4 % JIT, −24.3/−27.6 % AOT. Streams identical in 22 scripts; live WSL 18/18. |
| 4 | 4486b5c9 | richtext-readonly-a11y | Correctness (lead from step 3, confirmed): the editor's ReadOnly setter notifies and the patch swaps in the new version, so a reader's thread no longer answers the old read-only state until another rebuild. |
| 4 | 2ca6306f | a11y-text-caret-shares-text | When only the caret/selection moved (same document, width, theme stamp, scroll, read-only, blocks on screen), the next text shares the last one's text, tables and geometry (internal AccessibleText constructor; at most one share source, dropped once made). 5,000 blocks with a reader reading: 474 → 1.3 KB/key both bridges; walk 1.136 → 0.711 ms (−37.4 %) JIT, −47.6 % AOT. Every shared answer equals a fresh build. |
| 4 | 5e6ef9a2 | a11y-textarea-text-pattern | Approved rule. TextArea offers a versioned text source (lines = drawn rows; character boxes for visible rows ±1); a keystroke is told as UIA TextChanged / AT-SPI text-changed with the characters, never a whole-text Value (still readable on request); caret moves, read-only and re-wraps patched; caret moves with a reader share tables; `AccessibleTextVersion.Characters`. 2,000-line area, 40 keys: BSTR characters 4,869,780 → 0, AT-SPI signal characters 4,869,780 → 40; managed flat. Caret moves now told (0.4 → 1.0 KB/op). Live WSL: the area has a Text interface now, 11/11. BiDi limitation (boxes between caret positions) documented. |
| 4 | 79a34897 | apps-junction-tags-double-rebuild | A Tags key rebuilds once: builds per key 2 → 1; 406.0 → 231.3 KB/key (−43 %), bridge −41 %; the key's rebuild 0.82 → 0.41 ms. Test: 30 keys, one build each, selection stays in view. |
| 4 | 87698f16 | reconcile-moved-host-unmarked | Framework (found while measuring the chrome split): a move made during a component's build no longer marks the old parent at once; the keyed reconcile puts a host back unmarked when its children come out the same (`Element.TryRestoreMoved`), carrying the host's own marks up; anything else is marked as before (`SettleMove`/`FlushPendingMoves`); moves outside a build unchanged. With chrome parts, a11y trees per key at 2/4/8 frames per key 1.00 → 0.33/0.68/1.00 (= pre-split). 7 tests, 5 mutations caught. |
| 4 | b35fc587 | apps-junction-components | Approved. Title bar, sidebar (rail + list) and toolbar are `ChromePart`s reading `_chromeRevision`; `MarkDirty`/`Rerender` bump it, view-only paths (`MarkViewDirty`/`RerenderView`: selections, cursors, search and composer fields, find bars, splitters, diff pan) don't; hosts `FlexShrink(0)`, sidebar root `FlexGrow(1)`. Headless KB/key −56…−73 % on six walks (bridge −40…−68 %); chrome builds per key 0; AOT app History 257.5 → 119.0 KB/key, typing 204.5 → 66.1. 15 tests compare each chrome input with a forced full rebuild; 8 mutations caught. 0 px on 21 screens. |
| 4 | 3135de73 | junction-review-rows-shape | Review mode keeps the woven list across caret moves (checked against everything the weave reads); caret and range in `DiffRow.Mark`; one stable label delegate. 1,011 → 156.7 KB/key (−84 %), bridge −82 %; rows made per key 31 → 2; time per key min 4.52 → 3.07 ms. |
| 4 | 51ee152b | junction-fixture-quiet-git | `TempRepo` sets `GIT_OPTIONAL_LOCKS=0` and fixtures turn automatic maintenance off: the app rewrote the fixture index in 40/40 starts → 0/40 (0/100 twice); `FixtureQuietTests` 0 of 8 runs beside other classes. The writer behind the remaining 3/11 failures with locks alone is unproven (detached `git maintenance run --auto` is the best guess). `DiffItemLabelTests` reports whether the data generation moved. |
| 4 | 953257ae, 7cba3678 | apps-scenario-kept-chrome | Suite rows: list walk beside chrome built with it 173.5 vs in kept parts 35.2 KB/op; selection walk beside kept chrome parts with the bridge 1.0 → 0.6 trees/key (pins 87698f16). Full compare 0 slower (bar fade +15 % min = its 52/60 ms noise on rerun). REACTIVITY.md "Keep what a key does not change in a child component". |
| 4 | f9980647 | media-video-frame-bytes | `GpuFrameSink` makes its GL delegates once, per-frame inputs in fields; Vulkan keeps the SKImage over each source's shared image (keyed by owner, checked against VkImage, size, origin, colour space, alpha; remade on resize, failed lend or `ReleaseFrameTarget`); `GRBackendTexture` disposed at once (GL too); the render context no longer holds renderer-owned wrappers (a dead sink stayed in its table). Reel 1080p24 F16 a11y off, UI-thread B/frame: JIT 454 → 96, AOT 447 → 103; 1080p120 395 → 143; finalizable objects per frame 1 → 0. Content proof (F16 brightness 18 → 142), validation layer loaded and clean. HDR.md "never cache" rule rewritten. 0 slower. |
| 4 | f81f3108 | apps-scenario-vulkan-steady-frame | No headless GPU source, so a gated real-window row: `VulkanProbe --video-frame-bytes` + `VulkanProbeTests.ASteadyVideoFrame_AllocatesAlmostNothing_AndThePictureKeepsChanging`. B/frame F16 395.2 → 35.2 (limit 100), 8-bit 2,820 → 2,331 (limit 2,500), GL 157.3 → 13.3 (limit 100); brightness must climb > 20. |
| 4 | ee6d0bb0 | media-modal-loop-video-rate | Internal `LiveFrameRequests`: in Win32's size/move loop, MediaPlayer's and WebViewController's off-thread repaint requests post a registered message; the subclass (now on GL windows too) draws a live frame, skipping within 20 ms of a size change; SDL's timer stops drawing duplicate frames for posted work meanwhile. Reel 1080p120 Vulkan, frames/drops: move 56/49 → 107/0, held drag 128/111 → 240/0, hold 170/115 → 274/3; drag phase 287/11 → 223/11 (duplicates gone, drops unchanged), step time flat. GL move 56/48 → 104/2. THREADING.md, MEDIA.md. |
| 4 | 1b11aeda | vk-drag-kept-step-remakes | The F16 offscreen is made at the swapchain's allocated size (canvas clipped to the window) and kept with it; **`VulkanFrameTarget.AllocatedWidthPx/AllocatedHeightPx` public** (API addition). 80-step drag: offscreens made (each a sync submit + purge) 81 → 5; step median 5.4 → 4.4 ms (−20 %), drag CPU −17 %; Skia cache peak 52.7 → 55.7 MB. Reel drag step within noise, bytes per frame 2,437 → 1,408. 34 Vulkan UiTests pass under the layer. |
| 4 | c34d7668 | apps-scenario-scrubber-card | Suite row `media/scrubber hover with a preview card, 1 kHz mouse, real loop` (120 stills; the old hover row had no `PreviewFrame`, so never showed a card); README count 133 (121 off Windows). |
| 4 | 3b0bc6a5 | reactivity-scrubber-preview-in-place | The hovered position is a plain field; a hover writes the card through Refs (Left, picture Source/SourceRect/AspectRatio, time label); only a card appearing or leaving builds (`Signal<int>`); Build clears the refs when it makes no card. Hover row 420.7 → 22.8 KB/op (−94.6 %), card row 447.9 → 23.5 (−94.8 %), AOT −94.4/−94.6 %; builds per frame 1 → 0; bytes per frame 42,507 → 1,912 (the 16 pointer moves). 21 frames byte-identical, a11y trees identical. 0 slower. |
| 4 | 2bfa69c2 | reel-next-file-stills | Correctness (found in passing): `PreviewFrame`/`PreviewWidth` reached only the first file's player, so from the second file in a folder on (N/P, roll-on) the scrubber had no preview — in every released Reel. Internal `ScrubFrameSource` seam for tests. |
| 4 | 94fb3179 | reel-still-previews-rebuild | **Public `VideoPlayer.RefreshPreview()`** (API addition; MEDIA.md): asks `PreviewFrame` again for the hovered point and updates the card in place. Reel posts it when a still lands (not `Rerender`); Aperture likewise for trickplay sheets (same gap). Reel, 120 stills: app rebuilds per file 120 → 0; pointer elsewhere 2,042 → 0 B per still (bridge 5,912 → 0); pointer resting on the scrubber: the card never appeared → appears with the first still, 1,116 B per still. |
| 4 | 418f25a6 | reactivity-computed-pooled-notify | Computed notifies through a rented snapshot like Signal: per upstream write 128 → 0 B (70.4 → 0 for one change in ten, 160 → 0 with three readers), JIT and AOT. Reel with the chapter panel open −12 % per report. 6 tests (re-entry, dispose, subscribe and unsubscribe mid-notify). 0 slower. |
| 4 | a4a6f4a7 | apps-scenario-wrapped-resize | Suite row `text/resize a wrapped TextArea` (900-line notes, 40 widths); README 134 scenarios. |
| 4 | d7c0c046 | text-prefix-table-transient | `IRenderContext.MeasureTextPrefixes(text, start, length, style)` (API addition; default measures a substring): the pipeline answers with a view over kept lists, good until the next text call; `FontItemizer` reports runs as ranges. Per op JIT = AOT: wrapped resize 1,028 → 19 KB (−98 %), long document open 1,373 → 372 KB (−73 %), wrapped typing −5 %; 20,000-line log open 65.9 → 6.7 MB, its resize step 69.2 → 6.3 MB. Min −1…−5 %. `text/wrap`/`ellipsis` +6…+12 % in filtered JIT reruns = tiering (PGO off / `DOTNET_TC_CallCountingDelayMs=0` / AOT flat). |
| 4 | eb5a6ecd | text-paragraph-construct-diet | `PipelineTextParagraph` keeps one exact line array and makes its selection list on first selection draw; styled and BiDi wraps make no ellipsis closure without ellipsis. Selectable viewer wheel 2.2 → 1.6 KB (−27 %), rich text open 5,000 blocks −7.3 %, resize 1,000 −7.0 % (min −4 %); wraps −2…−4 %. 0 slower. |
| 4 | a476866c | text-textarea-mono-ascii-fits | Approved. `IRenderContext.MonospaceAsciiAdvance(style)` (API addition, default 0) proven once per face and size with the shaper (own glyph for all 95 printable ASCII, single and every ordered pair one glyph at the same advance within 1/4096; none with letter spacing); a printable-ASCII line fits unmeasured when (n + 1) × advance ≤ width. Highlighted JSON open 6.23 → 0.57 ms JIT (−91 %), 8.02 → 0.73 AOT, 1,902 → 270 KB; retained 4.38 → 0.17 MB; JSON wheel 7.9 → 9.6 KB/op, +2 % (words shaped at first draw). Proof ~0.5 ms, 219 KB per face and size. ELEMENTS.md: give logs, JSON and code a monospace face. |
| 4 | a709880f | apps-scenario-terminal-json-page | Suite rows `terminal/coloured screen, forced frame` (Cascadia Mono on Windows, runs last) and `text/page down through highlighted JSON in a TextArea` (every step a first draw: the cost side of sub-blobs). |
| 4 | b52ca178 | paint-subblob-variable-fonts | An outline face whose box Skia marks as a guess takes each run's ink extent from its own glyphs (`GetGlyphWidths` at 2048 per 256 glyphs into kept buffers, face slack; map at the sub-blob bound, ~57 KB per context, starts over when full); a spanned line looks up extents once per line. Colour and bitmap faces still whole. Min JIT/AOT: terminal coloured screen −60.4/−60.9 %, selectable viewer hover −48.2/−49.3 %, wheel −47/−49 %, JSON rebuild −15 %, wheel −14 %, open −10/−8 %, page down (first draw) −5/−9 %. Glyphs per frame Cascadia viewer 34,374 → 4,268, terminal 42,445 → 3,549. First draw: 7.0 native blobs per new line vs 1.0, sub-blob map restarts 68× in 389 screens, still faster. Pixels: 160 in-process scenes identical with a 512 MB glyph cache (default cache: A/A itself differs in 12, sub-blobs match one draw), WSL identical; pixel tests accept either of two whole-run draws, up to 3 tries. 0 slower. |
| 4 | 1207c8b3 | terminal-row-strings | Each terminal row keeps its last drawn string while its text is the same (`StringBuilder.Equals(span)`); lone characters and the one under the block cursor come from a per-code-point table cleared on font change; `TextFrame` remade only when it moved. Per forced frame: coloured screen 6,048 → 0 B, Powerline 856 → 0, CJK/Hebrew/emoji 2,392 → 0 (2,496 with cursor → 0); suite row 5.9 → 0.0 KB/op JIT and AOT, time flat. 20/20 frames identical. |
| 4 | b0a62174 | richtext-viewframe-record | `NoteViewFrame` makes a record only when X or Y changed: 40 ticked tasks 24 → 0 B per frame (the editor's last render allocation). |
| 4 | 0b992365 | aperture-hero-scrim-seam | Approved visual fix: `Halide.BackdropScrim` horizontal ends at `Alpha(Bg, 0)` (item page and Discover heroes). Headless read across the scrim edge: a 9-level step → at most 1 (gradient rounding). |
| 4 | 0ea667cf | paint-menu-rows-outside-window | `ContextMenuElement.Render` skips rows and separators wholly outside `ctx.ClipBounds` (layout, hit-test, keys, submenu anchors and a11y unchanged; the edge row still drawn); label heights measured once per text style. 200-item dropdown: text items per frame 202 → 30, label measurements 200 → 0, hover step −13 %; suite `overlay/dropdown open 200 items` −8.4 % JIT, −10.0 % AOT, +0.8 KB per open (the heights array). 20/20 frames identical; WSL Fedora 10,365 pass. |
| 4 | a1097a38 | apps-scenario-reader-pace-walk | Suite rows `a11y/selection walk beside details, keys 500 ms apart, real loop, {bridge on, UIA}`: 1.0 tree/key, 219 KB/op vs 0.6 and 181 at 60 ms (pins a11y-layout-refresh-bounds-only); README 138 scenarios (125 off Windows). |
| 4 | dddaac36 | a11y-leaf-property-patch | A leaf's own value (TextInput), toggle, range (at its coalesced refresh), name, description or expanded change remakes that node (found by walking semantic ancestors), compares with `CompareNodes`, swaps it in with `ReplaceChild` and hands the bridges a patch in tree order with selection/caret patches; role, enabled, radio/tab selection, masking, nodes with children, content-named ancestors, > 32 leaves a frame or a pending focus move still rebuild. AT-SPI object fields settable in place. UIA KB/op: TextInput 53.5 → 1.4 (−97 %), checkbox in 500 rows 87.2 → 0.8 (−99 %), label of 1,000 rows 173.7 → 35.8 (−79 %); min −14…−42 % JIT and AOT. AT-SPI TextInput 214.5 → 3.9, checkbox 352.4 → 1.2. Reel playback trees/s 3.05 → 1.05, a11y bytes −57 %. Equivalence: 29/30 stream scripts identical (find bar: 2 BoundingRectangle events lost inside the 100 ms layout wait, accepted coalescing), 53 patched-vs-forced-rebuild tests, FlaUI 14 steps and live AT-SPI 29 commands identical before/after. 0 slower. |
| 4 | b099714b | a11y-overlay-open-rebuild | Correctness: a hit-testable overlay opening or closing marks the tree for the next frame. Fixes dddaac36's regression (Dropdown/DatePicker are leaves whose `AccessibleExpanded` notification used to force the rebuild that found their list) and an older gap (a plain `ShowOverlay` never reached a reader: 0 trees over open, change, close). |
| 4 | a3aeabb2 | layout-themed-patch-keeps-layout | A patch applies the window's theme to the incoming twin for kinds opting in through `Element.PatchThemesTwin` (Button, Kbd, TextInput, MenuBar, table header/cells/grips) before comparing; those plus SplitterHandle, MenuTitle, table row, sort mark and item-row VirtualList opt into `PatchMarksOwnLayout`; their `ApplyTheme` writes geometry only where it differs and marks layout (also fixes a theme switch without a rebuild and `Style`/`Variant` set on a live button keeping the old box); item-row lists compare row-height offsets. Laid out / MeasureSize per key: Junction Commit 17.7 → 1.1 / 1,273.5 → 84.6, Vesper other names 19.1 → 8.1 / 501 → 285, Meridian URL typing 25 → 0 / 359 → 0; History/Branches/Tags/Files −30…−38 %. AOT Junction Commit frame −6.3 %. ~120 B per themed twin button (tween row +4 % bytes). 0 slower. |
| 4 | fbec1130 | reconcile-restyle-on-change (a) | A patch marks only the patched element (and the way up); the style pass enters a child only if marked or the parent's resolved style changed; a moved or joining child marks where it lands; `Mount()` marks only itself (a fresh box's mount re-marked whole persistent child components: 70 elements per key from Junction's chrome). Resolves per key: Junction History 331 → 59, Commit 319 → 57, Branches 683 → 90, Tags 521 → 76 (−81…−87 %), Vesper −48/−62 %, Meridian History −43 %, Aperture grid −61/−71 %; theme switch still 518. Style phase −72…−87 % on kept-part rows; clears the scaling bar, not the 5 % frame bar (style is 1–2 % of a frame). Proof for both: env-gated layout + style audits over all 28 test projects (style: 0 differences; layout: only pre-existing VirtualList spacers), 51 frames identical across themes/scales/run-time switches; full `HultUI.slnx` and WSL (10,422) pass. THEMES.md, REACTIVITY.md. |
| 4 | 572002ab | layout-vesper-partner-row (API) | **Public `VirtualListElement.InvalidateRow(int)`** (API addition; ELEMENTS.md): drops one realized row and that item's kept label, keeps the rest. Suite rows `virtual/partner walk beside 1,000 item rows, every row told` vs `… two rows told`: 0.611 → 0.571 ms (−6.6 %), 105.2 → 39.4 KB/op, rows per step 23 → 6; AOT −9.6 %. 0 slower. |
| 4 | 598441d4 | layout-vesper-partner-row | Vesper's F3 partner leaves the rows' shape; a move tells the old and new partner rows. Same-name walk −39 % KB JIT, −40 % AOT (target −42 %), rows made 31.6 → 3.5; real AOT app 479.9 → 261.1 KB/key (−46 %), frame 1.374 → 1.280 ms. |
| 4 | e6a432fa | vesper-grids-rebuild | Thumbnails on item rows (entries + cursor per row item; shape covers marks, cut, results, drop target, density, icon scale, `ThumbnailCache.Landed`); Brief hands back unchanged cells keyed by path (2 cells made per key, not ~140); a resize rebuilds only when a build would decide differently (trail cut, Brief rows/columns, tiles per row), else the live grid takes the new size (d3d81ee holds). Per step: Brief key 759.9 → 237.8 KB (−69 %), Thumbnails Right −50 %, Down −42 % (whole-window build remains), Brief resize 755.5 → 12.5 KB (−98 %), Thumbnails resize 520.6 → 25.7 (−95 %), builds per resize step 1.00 → 0.03/0.05; real AOT app Brief −78 %, Thumbnails −68 % (frame 1.606 → 1.192 ms). 39 frames, layout and a11y dumps identical (A/A identical); WSL 10,435 pass. REACTIVITY.md: handing back elements a key does not change. |
| 4 | 445b34c8 | sdl-hint-honoured-placement | New `WindowsActivation`: with `SDL_HINT_WINDOW_ACTIVATE_WHEN_SHOWN=0`, a thread CBT hook refuses activation during Maximize/Restore (SDL path), `SetFullscreen`, the `Placement` setter and the custom frame's maximize/restore syscommands; `SWP_NOACTIVATE` on all four full-screen `SetWindowPos` calls (both frames). Hint unset: no hook (real apps unchanged; requests 10 → 8 from SWP_NOACTIVATE). Activations reaching the guard per sequence 10 → 0 (8 refused by the framework). `WindowStateActivationTests` on both frames. |
| 4 | 33c9ba34 | uitests-guard | ForegroundGuard on 11 real-window UiTest classes (2 others gated, below); the guard sets the raise hint, passes hints to samples via env and gives their windows `WS_EX_NOACTIVATE`, watches child processes, logs with `HULTUI_UITESTS_GUARD_LOG`. Full `HULTUI_UITESTS=1` run: 122 pass, 0 fail, 14 skip, 0 hand-backs. One foreground take during the step's own run: WebView2's DevTools window (another process) took it ~100 ms before the guard handed it back; that test is now gated. |
| 4 | 3200a2a2 | runtime-close-ends-run | Correctness (found by the step): since aa11334 a pass that closed the last window then waited forever — an external `WM_CLOSE` (FlaUI, Task Manager) left an idle app running windowless until something woke it; released in junction-v1.21.0, vesper-v1.21.0, meridian-v1.4.0, aperture-v1.20.0, reel-v1.5.0. `if (!IsRunning) break;` before the wait: posted WM_CLOSE never → 0 ms; a11y sample close 1,450 → 79 ms; VirtualList UiTests 5 s (killed) → 0.6 s. `RunLoopTests` assertions. |
| 4 | c2ea7860 | foreground-uitests-opt-in | Approved. `[UiForegroundFact]` needs `HULTUI_UITESTS_FOREGROUND=1` as well: WinFormsHostSampleTests (8), WebViewAccessibilityTests (4) and WebView2's two DevTools tests (one took the foreground, the other can't focus the page under the guard) — 14 facts; only ever run to prove they skip. Testing skill, ENVIRONMENT.md, PACKAGING.md, WINFORMS-INTEROP.md. |

## Landed, round 5

Moved from the ledger at round 7's survey (2026-11-02).

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

## Landed, round 6

Moved from the ledger at round 8's survey (2026-11-03).

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

## Landed, round 7

Moved from the ledger at round 9's survey (2026-11-08).

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

## Landed, round 8

Moved from the ledger at round 10's survey (2026-11-09).

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

## Landed, round 9

Moved from the ledger at round 11's survey (2026-11-11).

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

## Landed, round 10

Moved from the ledger at round 12's survey (2026-11-12).

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

## Landed, round 11

Moved from the ledger at round 13's survey (2026-11-12).

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

## Round 5 build order

Correctness and round 4 objections first, then value per cost.

1. ~~**Round 4 objections, small framework fixes**~~ LANDED a52de219, 6388f868, 2a332218, 3dd5c97b, 6895ac2d, a18e70dd (6/4/5/3):
   **reconcile-restore-moved-into-empty** (`ReconcileChildren`'s no-old-children path, TreeDiffer.cs:142-147,
   tries `TryRestoreMoved` before adding: a container whose every child was handed back no longer marks its
   parent per child — Vesper Brief with a reader 300.7 → 234.0 KB/key, a11y trees 1.00 → 0; Reel app build
   with a reader −51 %; removes the O(cells × pending moves) `SettleMove` scan; keep bridge-off bytes flat),
   **findbar-step-double-rebuild** (FindBar.Move/ToggleMatchCase/ToggleRegex/Close notify twice: builds per
   find step 2 → 1 in every app; Vesper reader find −25 %; fold Open's up-to-3 notifies),
   **live-frame-posted-work** (ee6d0bb0: a public GPU source outside Media/WebView signals by post and now
   freezes during any size/move loop; suppress the posted-work draw only while live requests arrived
   recently, e.g. 100 ms), **text-textarea-a11y-row-xs** (5e6ef9a2: with a reader each keystroke rebuilds the
   accessible text through per-character `TextCaretX`, O(rows × length²): 709 → 310 KB/key hoisting the row
   string, then a default-member `IRenderContext.TextCaretXs` (API addition) for O(rows × length); suite row
   "a11y/type in a 2,000-line TextArea, a reader reading"), **sdl-hint-minimize-raise** (Minimize/Raise under
   `WindowsActivation.RefuseIfUnwanted`, or the raise hint; test hygiene).
2. ~~**a11y-overlay-live**~~ LANDED 0b477882, 02e17103, cb34eecc, 22021d66, 827f6ad5, cc028a7f, 08c49d10, b23c9251 (approved 2026-10-25) (7, correctness) — overlay content gets the window's observer; overlay style
   and layout run before the a11y phase; patches find nodes under overlays; ContextMenuElement tells its
   selection and has item geometry before the tree reads it (all menu items have zero-height rects today);
   AT-SPI `state-changed:expandable`. Today: label/check/text/children changes inside an open overlay build
   0 trees; command palette typing (932 → 924 nodes) never reaches a reader; menu/dropdown moves never told.
3. ~~**Text**~~ LANDED 91d48601, cc6f8467, a1ff84a1 (5): **text-glyphs-on-first-draw** (word-cache runs compose their glyph array on first read: text/wrap
   −59 %, ellipsis −59 %, richtext/resize −40 %, open −38 %, 12 rows −8…−59 %; retained rich text 5,000 blocks
   −57 % open, −61 % closed; 20,000 layouts bit-identical; scroll rows pay first draws), **text-line-temporaries-
   scratch** (Itemize's per-call `List<FontRun>` and BiDi temporaries into kept scratch: 7 rows −14…−27 %),
   **text-last-line-view** (a wrap's last line fills scratch instead of a durable PrefixWidths; after the first).
4. ~~**Paint**~~ LANDED e5ff3fea, 09abf712, dff306df (2/4): **paint-run-ink-extents-dead-keys** (b52ca178's run-keyed extent map keeps dropped runs alive:
   2.29 MB after a 20,000-line Cascadia document closes; per-face glyph-extent table or a field on the run),
   **paint-blob-maps-frame-working-set** (a full blob map clears all 2,048 entries, so a screen needing more
   remakes every blob every frame — dense-colour terminal at 1200×900, Chinese terminal at 1080p, 4K: keep the
   last two frames' blobs, hysteresis, hard cap; suite row `terminal/dense colour screen, unique rows, forced
   frame` at ~1600×1000 first).
5. ~~**Layout**~~ LANDED ce70fec7, 415ecefa, a31a38c7, 93226dda (1/5): **layout-patch-keeps-layout-more-kinds** (TextArea, Checkbox, RadioButton, ToggleSwitch,
   RichTextEditor opt into `PatchMarksOwnLayout`: Meridian URL keystroke lays out the 18,884-line response plus
   9.5 ancestors, Junction Changes typing 10 elements and 1,982 MeasureSize; typing frames −8…−10 %, with a
   reader −20…−27 KB/key; suite row: typing beside a large TextArea and checkboxes), **layout-virtuallist-
   offsets-kept** (a RowHeights list allocates a new `float[n+1]` every patch, twice after `InvalidateRows`:
   O(n) bytes per rebuild → 0; Vesper viewer find typing −9.4 %; suite row beside a 20,000-row list).
6. ~~**runtime-gc-gen0-budget-and-loh-threshold**~~ LANDED 828a7d69 (LOH threshold kept), b1323e9d (gen-0 cap declined) (4) — in the five AOT apps: (a) gen-0 budget capped at 6 MB
   (default 17.3 MB follows L3): working set Junction −10 %, Vesper −9 %, bytes per key unchanged; (b) LOH
   threshold ~512 KB: typing in a 42.5k–256k-character TextArea no longer triggers a gen-2 GC every 14–17
   keys. Each its own commit; time frame p95/p99 and `GC.GetTotalPauseDuration` on app walks and TODO.md
   typing; verify the option is in the shipped exe (`--runtimeopt`).
7. ~~**GPU**~~ LANDED 7f9df4ea, 6c627f27, ba8fb89d (trace) (3): **vk-sdr-video-lazy-barrier** (8-bit direct path keeps its target after a video frame and submits
   the acquire barrier lazily before Skia's first submit: 2,054 → 112 B per frame, validation clean with a
   positive control; time mpv's render phase), **vk-drag-kept-target-wraps** (8-bit only: 80 → 12 wraps per
   80-step drag, −77 % bytes per step; **F16 deferred** — its prototype coincided with two `nvlddmkm` 153 GPU
   errors and a > 1 s stall: validation run first, never repeat blind), **gl-first-use-flush** (trace only:
   Meridian's first response frame is 98 % flush and 28–30 % of its session frame time, the first palette open
   99.7 %, gone on repeat — likely Skia program compiles/atlas; KHR_debug timestamps or Nsight, then decide).
8. ~~**App double rebuilds and lists**~~ LANDED b1a12ef1, fe50f969, 54eded98, 57e954ca, ea6ceb7a, 1ed9d938 (6/8, app): **junction-review-composer-typing** (1,367 KB per typed
   character: 2 app builds + 3 chrome builds + re-weave → one view-only build keeping rows, −80…−90 %; app fix,
   not the editor's SelectionChanged), **meridian-history-rows** (item rows + no O(n) `IndexOf` per row:
   353 → ~110 KB/key; history search 2 → 1 builds per character), **vesper-mark-double-rebuild** (Insert
   2 → 1 builds, −36 %; fold the one-off doubles), **apps-junction-commit-tree-virtualize** (the Commit file
   tree is a plain ScrollView built whole per key: 2,616 KB/key on a 382-row commit → O(visible rows),
   −60…−70 %; Changes list same shape; highlight landing uses `MarkViewDirty`), **apps-junction-view-parts**
   (History include bar + footer as parts, no `Commits.ToList()` per build: −20…−25 %), and
   **vesper-detail-resize-stale** (approved) (not perf: run e6a432fa's fit comparison in Detail too).
9. ~~**layout-virtuallist-selection-highlight**~~ LANDED 0d3bc10a (API), 8157c282 (Tags); History and Vesper declined, 68817800 (approved 2026-10-25: opt in) (1/8) — an opt-in highlight the VirtualList draws under the
   selected rows, so a move makes no rows: Junction History −30 %, Tags −54 %, suite item-row walk −36 %;
   visual API (fill, ring, radius), adopted one list at a time.
10. ~~**events-handler-churn**~~ LANDED 76b6f78e, 574f43a7 (4/1/6; re-opens P4's inline half with new evidence: rebuild churn, not events) —
    (a) a patch reuses the live element's handler lists (patch copy 13.2 → 1.9 KB; Vesper −6.9 %),
    (b) inline handler storage with a typed delegate per entry instead of `Dictionary<Type, List>` + a
    wrapper closure (~320 → 60–100 B per registration estimated, −11…−16 % per key). Prototype (b) first.
11. ~~**apps-meridian-components**~~ LANDED ff6262ff (6/8, app) — title bar, rail, request tree, response as parts; URL typing opts
    out: 309–312 KB/keystroke, the tree 120 KB (O(requests)); −50…−70 %.
12. ~~**apps-vesper-components**~~ LANDED fcf366c6 (6/8, app) — per-pane header/footer, rail, chrome as parts: panes are 71 % of the
    build; −45…−80 % per key.
13. ~~**a11y-layout-refresh-in-place**~~ LANDED bd88e747, 4175f435, c9adbb6c, 4f471ae9, 577949d9, eb856985, a123f46f (7, large) — a layout-due refresh walks elements against the applied tree
    and updates offscreen bits, scroll offsets and moved rects in place (scroll-space rects), falling back to
    the rebuild on any id/count mismatch: item-row walk with a reader −77 %, wheel UIA −99 %, Junction History
    at a reader's pace −30…−47 %, Reel 1 tree/s → 0. Must reproduce the builder's rules exactly.
14. ~~**a11y-virtual-items-and-selection**~~ LANDED 6483f85f, 167b4ef9, 46a5aa12, 69a9fac2, 27673829, b6de3fcf (7, correctness; added events approved) — lists over 2,000 items:
    AT-SPI serves items on demand (ChildCount 0 today), selection moves raise UIA IsSelected / AT-SPI
    selected (40 moves told 0 times today).

## Round 6 build order

Every step landed but step 8 (runtime-element-layout-state, declined b35a81d8); the commits are in the ledger's
Landed rows for round 6 (they move here at round 8's survey). Moved here at round 7's survey (2026-11-02).


Ranked by expected win over cost; framework first where an app item and a framework item compete. Each step names the
report lines its brief quotes (`r6-k` = `17-reports/r6-k-<area>.md`).

1. **text-line-strings-once + richtext-insert-one-pass** (5; r6-5 Candidates 1 and 4) — a line, ellipsis and measure cut
   one string or none, `ApplyEllipsis` makes no closure (TextPipeline.cs :2898, :1727, :2948, :660, :526, :2834, :1646,
   :2156); `InsertInline` builds text and spans in one pass (RichEditState.cs:672-679). Prototype KB/step: text/wrap
   391.8 → 293.3 (−25 %), ellipsis −27 %, multi-font ellipsis −38 %, styled ellipsis −25 %, 600-span typing 592 → 515
   (−13 %; insert one-pass alone 591.9 → 433.1, −27 %), bidi ellipsis −9…−10 %; 48,000-layout bit dump identical
   (hash 7A18AF8DE8E05271). Small, no API or pixel change.
2. **paint-blob-key-by-words-text** (2 with 5; r6-2 Candidate 1) — runs from `ShapeByWords` get a flag; the run-blob map
   hashes (face, size, text) and equal flagged texts match without reading glyphs; a miss builds its blob from the
   word segments through a lazily made scratch span. `PROTO_TEXTKEY=2`: text/wrap 397 → 325 KB/op (−18 %, render 72.7 → 0),
   long TextArea open −16.5 %, scroll/jump render −70 %, popover open −12 %; blob/sub-blob makes, runs and glyphs drawn
   identical on 151 rows; `PROTO_VERIFY` 0 mismatches; 39 frames A/A = A/B. Re-measure on top of step 1. Not the
   round 3 side map nor the round 4 cached hash (no map, no growth; `ShapedRun` stays 56 B).
3. **dialog-scrim-button-role** (7; r6-7 Candidate 1;; user: yes) — a clickable box whose direct child has
   the Dialog role is a backdrop, not a Button (`InferRole`, AccessibilityTreeBuilder.cs:654; Meridian's Options
   backdrop MeridianApp.Palette.cs:121). Prototype: dialog label tick UIA 284.2 → 1.8 KB (−99.4 %), trees 40 → 0 per 40
   ticks; in-tree modal 260.4 → 1.2 (AT-SPI 1,058 → 1.4); palette key Name events 2 → 1; 1,134 Headless tests pass.
4. **sdl-custom-frame-maximized-dpi + gl-thread-walk-after-first-frame** (3; r6-3 Candidates 1–2) — (a) a correctness
   bug: inset a maximized custom frame with `GetSystemMetricsForDpi(…, GetDpiForWindow)` (WindowsCustomFrame.cs:424-425,
   :281, :592); fixes the 3 px band on a lower-DPI monitor and the two Borderless WindowStateActivationTests (2,554 vs
   2,560 px; replaces uitests-borderless-maximize-width). (b) on GL after the first present, start and join one no-op
   thread and draw one more frame so NVIDIA's one-time thread walk (13–14k `Thread32Next`, 306k–461k UI `SwitchToThread`)
   lands at start-up, not in Meridian's first send (−43…−75 ms) or the first palette open (up to −160 ms); confirm with
   the import counters, time the extra frame on AOT Meridian/Junction/Vesper, decide on an NVIDIA-only gate.
5. **Vesper: progress and find** (6/8; r6-6 Candidates 2–3, r6-8, r6-1 Leads) — `Repaint()` through `MarkViewDirty`
   with the transfer's percent/count in `CmdKey`, `Application.Time` (CommanderApp.cs:2060-2066: 189.2 → 14.7 KB a tick,
   −92 %); the viewer's find shape without the current match (`ViewerFindShape`, CommanderApp.Viewer.cs:599) and find
   bars on `MarkViewDirty` (prototype Enter 345.1 → 105.3 KB, −69 %; ~−78 % with the bars); find results with item
   rows, no `IndexOf` per row (CommanderApp.Find.cs:403); vesper-free-label-per-build (1 `DriveInfo` per Insert/type-to-find
   key, 2 per folder change and repaint). Suite row: a worker posting `MarkDirty` every 40 ms over kept parts.
6. **Vesper: rows** (6/8 with 1; r6-6, r6-1 vesper-own-selection-revisit, r6-4 Vesper Insert strings) —
   vesper-marks-in-row-shape (Insert 287 KB/key, render 89 %, ~30 rows remade → about −80 %); vesper-own-selection-revisit
   (`PROBE_VSEL`: Detail −28…−32 %, pixels identical with zebra off; zebra on keeps the row-drawn cursor; r6-1 answers
   68817800's three objections); composite-row-kept-cards for Thumbnails (Down 106.9 KB, render 86.0 → about −70 %); the
   zebra `IndexOfPath` per row (CommanderApp.Pane.cs:940).
7. **Meridian** (8/4/5; r6-4 Candidates 2–3, r6-5, r6-6, r6-8) — meridian-body-edit-in-place (89k body 1,795–1,899 KB/key;
   `ReqDocument.SetBody` 1,205 KB alone, ReqDocument.cs:229-256, :84, :318 → −67…−90 %); meridian-history-selection-offscreen
   (a bug: Down never scrolls the list; scroll into view by `index × RowRun`); meridian-popover-builds (open + pick
   610.7 → ~285 KB; `BuildPopover` row `action(); MarkDirty();` at MeridianApp.Requests.cs:750 with `SetMethod` :388 and
   `SelectEnvironment`; open/close view-only); meridian-find-typing with option b, the user-approved TextArea highlight layer (a: exact merge, 1,528.7 → 615.3
   KB/char; b: a TextArea highlight layer, ~−90 %, also for Junction's `DecorateFileBody`). Suite row: find typing and
   Enter in a 2,000-line highlighted TextArea.
7c. **richtext-table-scroll-depth** (5 with 1; user, 2026-10-27) — three weeks ago the user made a rich text example
    with a 300-row × 10-column table that rendered much slower scrolled down than at the top. The sample is gone:
    rebuild it, count work (layout/measure/shape calls, rows and cells visited, hit tests, a11y) and frame time at the
    top, middle and bottom, find the O(position) cost, fix it if it clears a bar, and pin it with a suite row.
7d. **richtext-table-real-window** (3 with 5; user, 2026-10-27, after step 8) — the user's test (~2026-10-06/07: a
    window with only the editor and the 300×10 table, DevTools overlay on continuous redraw) read 600 FPS at the top,
    ~450 a quarter down, ~300 in the middle, ~150 three quarters, 60 at the bottom (1.7 → 16.7 ms, worse than linear);
    7c's headless rows (CPU raster, editor-only repaint) never saw it. Real-window probe (GL, continuous redraw, FPS +
    frame work at 0/25/50/75/100 %), positive control at e624b076 (tables arrived; b688db76 47 min later drew only
    clipped blocks — the likely fix), then the tip; find the cause and fix if the tip reproduces; keep the probe as a
    sample the user can scroll. Brief in scratchpad `brief-r6b7d.md`.
8. **runtime-element-layout-state** (declined, see Examined) (4, 1 reviews; r6-4 Candidate 1) — `_layout`, `MeasureCache`, cache constraints and
   `FlexPlan` (228 B) move to an object made on first layout; throwaway rebuild copies (48–87 per key) never get one.
   Expected per key: Meridian History −16…−20 %, Junction summary −17 %, Meridian URL −15 %, Vesper Brief −12 %, Aperture
   Right −11 %; laid-out elements +28 B (Vesper window +13 KB). Time the extra pointer read on `Layout` (render, hit
   testing, AOT); run the layout audit. Phase 2 (−100…−150 B more) only if phase 1 holds.
9. **palette-virtual-rows** (1; r6-1 layout-palette-virtual-rows, r6-6, r6-7 palette evidence; user: off-screen rows leave the tree) —
   CommandPalette's up-to-100 rows (CommandPalette.cs:349-357) become VirtualList item rows keyed by command with
   `RowHeights`. `PROBE_PALROWS`: palette filter over 2,000 335.3 → 52.2 KB/key (−84 %), UIA palette typing 888.8 →
   134.6 (−85 %); Junction palette 264.6 KB/step, −75 %. Replaces palette-row-keys (keying alone measured worse).
9b. **composite-list-told-on-its-own + overlay-layout-on-window-resize** (7 with 1; found by step 9) — two bugs. (a) A
    `RowsAreComposite` list in the window's own tree is never told by itself: its rows were missing from the tree on
    mount and 600 ms after a change, until something else built a tree (Junction Files and Commit, Aperture Library).
    (b) An open overlay is not laid out again on a window resize until something in it changes. Brief in scratchpad
    `brief-r6b9b.md`.
10. **Junction** (6/8; r6-6, r6-8, r6-1) — junction-changes-composer (summary keystroke with 200 changed files 1,324.7 KB,
    1,270 elements → ~15 KB at any count); junction-review-composer-row-kept (155 → ~40 KB/key, with a reader trees 1 → 0;
    `DraftEdited` remakes the row, JunctionApp.Review.cs:498); junction-branches-selection-highlight (Branches Down 92.7 KB,
    render 53.9 → about −50 %, as Tags 8157c282); the diff's find shape (`DiffFindShape`, JunctionApp.Diff.cs:817; round 5
    measured 440.5 KB an Enter); History's list-drawn selection, pixels approved: affordance overlay at the row end, list-owned hover tint (`PROBE_JSEL` 44.4 → 21.4 KB/key).
    Suite row: typing in a rich text editor inside a VirtualList row with the bridge on. Split for one agent's size:
    10a composer, review row, Branches, diff find, suite row (brief `brief-r6b10a.md`); 10b History selection, Junction's
    9 modal cards as Dialogs (question 5), popover menus as menus in Junction and Meridian (question 6).
11. **virtuallist-sticky-headers + Junction Changes as a VirtualList** (1/8; r6-1 Candidate 1; user: API approved) —
    Changes with 300 files: 3,065 elements, Down 3,020 KB/key, typing 2,713 KB/char; DECISIONS.md "Junction's Changes
    list stays a scroll view, for now" named this trigger. O(changed files) → O(visible rows), −90 % or more at 300 files.
    Also: a Changes cursor key is a full rebuild with chrome (305 KB with 21 files; step 10a) — `SelectChange` could use
    `RerenderView`. Brief in scratchpad `brief-r6b11.md`.
11b. **junction-review-composer-reader** (7 with 8; user, question 8, 2026-11-01) — a reader can reach the comment being
    typed in Junction's review. `RowsAreComposite` while reviewing is the route step 10a named; 92084157's `InItemRow`
    (an element in a row of an item-publishing list asks for no tree) has to stay right for a list that switches. Keep
    typing's 2 KB/key with no reader; with one, measure what telling the text costs and keep trees a key at 0 if it can.
12. **apps-junction-highlight-prefetch + junction-commit-diff-flatten** (5/8, 6/8; r6-5, r6-8) — tokenize only to the last
    shown hunk line / after the selection rests (JunctionApp.Highlight.cs:93-109; History 9,826 KB/key on all threads vs
    60 on the UI thread, 59/16/1.5 GCs per 100 keys → −50…−90 %); flatten a new file's diff lazily (415-file commit:
    file key 556 KB, build 439, 0.88 whole-file flattens per key → −60…−70 %).
13. **Aperture** (6/8; r6-6, r6-8; user approved a public `Part` in Core) — aperture-resize-rebuild (`OnWindowResize`
    → build only when cards, hero width or artwork step change: 68–72 KB, 63 patches a 4 px step → ~0); composite-row-kept-cards
    (Down render 113.4 KB, 116 unmounts → about −55 %); apps-aperture-components (rail 17.0 + title bar 16.7 KB of every
    grid key: Right −29 %). Suite row: image-grid.
14. **text-lazy-line-runs** (5; r6-5 Candidate 2) — lines keep width, metrics and range and make runs on the first
    `TextLine.Runs` read (lines made vs drawn: text/wrap 450/36, richtext/open 7,838/29; undrawn lines 25–64 % of those
    rows). On top of step 1: text/wrap ~293 → 140 KB, multi-font ~−65 %, richtext open/resize −24…−26 %, ~1 MB retained
    off a 5,000-block document. Medium–large; mind the three eager readers r6-5 names.
15. **a11y-atspi-snapshot-per-diff + apps-scenario-atspi-rows** (7, Linux; r6-7 Candidate 2) — AT-SPI objects kept by id,
    repointed per apply, made only for added nodes (LinuxAccessibilityBridge.cs:246, :814; ~265 B per node per apply).
    Dialog tick 799.5 → ~2 KB, palette 1,338 → ~535, Junction palette AT-SPI apply 116 KB/key (UIA 23). AT-SPI suite rows
    need an `InternalsVisibleTo` for PerfSuite.

Closed from Waiting by the round 6 survey (see Examined): live-frame-requests-public, meridian-history-aot-p90,
apps-tokenize-on-ui-thread, palette-row-keys, a11y-box-selected-patch, a11y-in-place-fallbacks,
uitests-borderless-maximize-width (→ step 4). Folded into the build order: meridian-find-typing, meridian-response-find-step,
meridian-body-edit-bytes, meridian-method-popover-double-build (→ 7), vesper-marks-in-row-shape, vesper-own-selection-revisit
(→ 6), vesper-transfer-progress-rebuilds, vesper-free-label-per-build (→ 5), junction-changes-composer (→ 10),
virtuallist-sticky-headers (→ 11), apps-junction-highlight-prefetch (→ 12), apps-aperture-components (→ 13),
text-lazy-line-runs (→ 14), a11y-atspi-snapshot-per-diff (→ 15), dialog-scrim-button-role (→ 3), gl-driver-thread-walk (→ 4).

## Round 7 build order

Every step landed (step 8 as 8a and 8b); the commits are in the ledger's Landed rows for round 7 (they move
here at round 9's survey). Moved here at round 8's survey (2026-11-03).

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

Closed from Waiting by the round 7 survey: richtext-table-mid-depth-hump (→ step 3), atspi-kept-tab-retire (→ step 1),
vesper-key-rest-bytes (→ step 1: the shortcut enumerator), palette-per-row-box (→ step 5), context-menu-read-as-list
(→ step 13, question 2), vesper-brief-marks-and-filter-rows (→ steps 4 and 9), meridian-leads (→ step 10),
a11y-textarea-keystroke-tree (fixed by daebfa76: 0 trees in 40 keys at 89k characters), vk-startup-preload (Examined).
The junction-leads' find step (→ 6), detail load (→ 8), big-files timing (closed: timing, not a leak), composer and
issue list (→ 8); aperture-leads' Home (→ 11), Down with a reader (→ 7), `Place()` in Junction and Meridian (done).

## Round 8 build order

Every step landed (step 9 measured first, then built as 9b after question 13); the commits are in the
Landed rows for round 8 (moved to this archive at round 10's survey). Moved here at round 9's survey
(2026-11-08).

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

Closed from Waiting by the round 8 survey: a11y-walk-unchanged-bytes (→ step 2: the joined names), richtext-short-viewport-jitter
(→ step 3), junction-tags-test-flake (29 tests × 6 under load, 0 failures; round 7's two full suites and CI green since
63467f8e), vesper-coalesce-markdirty (dropped by experts 6 and 8: no double), runtime-element-layout-state phase 2 and
layout-patch-keeps-layout-dropdown-expander (Examined), the held-selection suite scenario (pinned by
`CommitPrefetchTests.AHeldArrow_LoadsOnlyTheCommitItStopsOn`), junction-leads' `ChangesView` lists (95c86c97) and big
files' gen 2 (Examined), aperture-leads' Discover and the grid cursor's focus (→ step 5), junction-changes-leads' perf
parts (Examined). After the survey: a11y-disabled-menu-item (→ step 2, question 11 yes).

## Round 9 build order

Every step landed (step 13 measured first, then built as 13b after questions 20–22; 14 then 14c after
question 23; 15 in part, then 15b after question 25; 16 taken ahead of step 3); the commits are in the
Landed rows for round 9 (moved to this archive at round 11's survey). Moved here at round 10's survey
(2026-11-09).

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

Closed from Waiting by the round 9 survey: brief-columns-lag-a-key (→ step 10), aperture-leads (toasts measured →
apps-toast-gone-view-only; grid Down is the row of cards scrolling in, 26–29 KB a key, which has to be made; Discover's
Down walking a shelf's pages is a UX note), junction-leads' old-side state convergence (Examined).

## Round 10 build order

Every step landed but step 14 (measured and declined, 9f76ecb6); 13b and 15b were added during the round
(15b after question 36). The commits are in the Landed rows for round 10 (moved to this archive at round
12's survey). Moved here at round 11's survey (2026-11-11).

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
   (Staging.cs:56/69/72, Dialog.cs:68/82, JunctionApp.cs:1262, :1549–1589, :438/441; also the Escape that clears the MR
   filter, MergeRequests.cs:872, left by step 5): −118.6 KB a click by the part buckets; only sites shown view-only. Small, app; each its own commit.
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
    full-build equality check (Junction's `_diffPan.ViewportWidth`, Meridian's `SetMax`). **Question 32 (no) keeps it
    app-only.** Also (bug, pre-existing, found in step 8): the diff pan's viewport and scrollbar thumb update only on a
    build, and a resize often builds nothing; at start the viewport is the 1298 px window guess against ~700 px on screen,
    so the wheel stops at 361 of ~960 px sideways. Have the pan's viewport follow layout. Small–medium, a drag: rank low
    (the pan bug is worth it alone).
13b. **junction-view-only-tests-settle** (6, tests; found in step 13) — this round's new Junction tests
    `ChromePartTests.AFolderOrFilterCharacter…` (step 5) and `ChromePartTests.ViewOnlyClicks_BuildTheViewAndNoChrome`
    (step 8) fail about 1 run in 4 (also on 4eeb23ac): a file-watcher rebuild (`OnExternalChange` → `Rerender`) lands inside
    the test's "view only" window because `HostedOnGitLab` (ChromePartTests.cs:1022) waits only for the GitLab mark. Add a
    settle step (wait until the watcher's initial change is drained, or count only builds the test caused) so the counts
    are exact under load; 20 runs under a loaded machine with 0 failures. Also look at the known flake
    `ReviewTests.TheKeyboardReachesTheCommentAgain` (memory ci-red-findings) if the same cause. Small, tests only.
15b. **vesper-brief-return-offset** (1 with 6; question 36 (b), 2026-11-11) — Brief comes back from Detail, Thumbnails
    or the viewer/diff/sync at the position it was left at, with its columns made for that offset: a ScrollView applies
    an offset set before its first layout once it has laid out (a fix to the existing setter, no new API), and Brief
    builds its columns for the restored offset (no blank column, no extra build where none is needed). Small, framework +
    app.
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
    the scroll section); docfx 0 warnings. Also (found in step 2, pre-existing at 755512aa): going back to Brief from
    Detail is blank until a key — the new scroll view opens at offset 0 while its columns are made at the old offset.
    Small, framework + app.

Closed from Waiting by the round 10 survey: apps-toast-gone-view-only (→ step 5), apps-meridian-response-drag-build
(→ step 13, now 38.9–39.2 KB a move), vesper-brief-filter-clear-reveal's scrollbar half (→ question 28; the
filter-clear half stays).

## Round 11 build order

Step 0 (the .NET 11 upgrade) went first; steps 1–12 landed (step 1 (c) measured and declined, 8e34bf92); steps
13 and 14 were measured with nothing to change. The commits are in the Landed rows for round 11 (moved to this
archive at round 13's survey). Moved here at round 12's survey (2026-11-12).

Ranked by expected win over cost: the review objections first, then the tests CI needs before the release, then
scaling and retention, then app wins, then the approved non-perf steps. Each step names the report lines its brief
quotes (`r11-k` = `17-reports/r11-k-<area>.md`).

1. **AOT text lookups** (5, with 4; r11-5 Candidates 1–3, r11-4 Review and Candidate 1; closes Waiting
   dotnet11-aot-textarea-long-document) — the objection to 207fbe25: ILCompiler 11.0.0 no longer devirtualizes
   `IAlternateEqualityComparer` in `Dictionary.AlternateLookup.FindValue`, so the word shape cache (MeasurementCache.cs
   :50–72, :191) makes 2 `RhpInterfaceDispatch` calls and out-of-line `GetHashCode`/`Equals` a lookup (AOT
   micro-probe 15.3 → 26.6 ns; 38,036 lookups a step × 12.2 ns = 0.46 ms of `text/open a long document`'s +0.52 ms;
   sample share of the lookup cluster 12.2 → 20.0 %). (a) **text-word-cache-own-table**: its own chained table with an
   index-linked LRU, LruCache's rules kept (8,192 entries, 4 MB bound, half the bound per entry, least recently used out,
   Put replaces): prototype `r11-text\proto-words-only.patch` open −27 %, resize −23 %, rich text/wrap/ellipsis/labels
   −6…−17 %, 42 rows none slower; ~124 → 52 B bookkeeping a word. The width cache the same way only if it costs nothing
   extra (595 lookups a step, ~7 µs). (b) **text-itemize-extends-below-marks**: `FontItemizer.Extends` (FontItemizer.cs
   :165, or before the call at :85) returns false below U+0300 without `GetUnicodeCategory` (13.9 % of the line walk;
   identical results, test over U+0000–U+02FF): open −7 %. (c) **text-itemize-primary-glyph-per-char**, measure first:
   `primaryFont.HasGlyph` per character in Itemize is 8.5 % of the walk after (a)+(b); keep only if it clears a bar.
   Corpora 48k/240k unchanged; tests for eviction order, weight bound, re-put, span equals string, growth, slot reuse;
   `step-ab.ps1 -Runtime Jit,Aot`. Medium-small, no API.
2. **a11y-item-rows-offscreen-label** (7; r11-7 Review and Candidate) **(question 38: yes)** — the objection to
   5e93a4fe: in `VirtualListElement.KeepUnchangedRows`, an item whose label a reader was given and that no longer equals
   the item at its index asks the frame's in-place walk. Meridian, an observed endpoint seen again off screen: stale
   frames 90 → 0 (1 in-place walk, 0 trees); on screen unchanged; audit STALE 1 → 0, walks +6/+46/0, 0 mismatches; 0 B
   without a reader. Prototype `r11-a11y\apply-proto2.py` (6 lines); run Junction's audit too (19 lists use
   `RenderWith`). Small, no API.
3. **Tests that fail by clock or order** (4 with 3 and 8; each its own commit; r11-4 Waiting
   meridian-stalled-timeout-early, r11-3 Waiting uitests-liveresize-order and uitests-workareas-before-dpi-aware, r11-8
   Waiting meridian-history-test-midnight) — needed before the release (2-core CI). (a) HttpEngineTests.cs:207 allows one
   clock-interrupt step: `>= 400 ms − 17 ms`, with the reason (TimerQueue counts on the interrupt clock, 15.625 ms steps;
   the 388/399 ms failures; .NET 10 failed the same way). (b) `GpuImageSourceGLTests` (GpuImageSourceGLTests.cs:28–30)
   lets its image go (`image.Source = null` in a `finally`), and the two LiveResizeFrameTests assert
   `!GpuFrameSink.MaySignalOnlyByPosting` first (5/7 → 7/7 in one run); the four early `WorkAreas()` reads set the thread
   per-monitor aware first (WindowPlacementTests.cs:29, :52, WindowStateActivationTests.cs:65,
   CustomFrameSystemMenuTests.cs:47). (c) Meridian's History reads `_app.Time.GetLocalNow()` (11 `DateTimeOffset.Now`
   sites in MeridianApp.History.cs) and its tests set noon (HistoryTests :356–384, :411, :468, :547, :601; PartTests:42;
   ReaderFocusTests:122). Small.
4. **layout-scroll-forced-height-measure** (1; r11-1 Candidate; the measure half of Waiting
   virtuallist-selection-relayout) — a column with `OverflowY == Scroll` measured at a forced height takes its unforced
   measurement at the same width with the height applied (`LayoutEngine.MeasureSize`, ~10 lines; not grids, percentage
   or min/max heights, aspect ratios), so the 4-slot `MeasureCache` stops thrashing (the map list sees 4 widths × 4 forced
   heights a click). MeasureSize a map click 869 → 245 (60 endpoints), 941 → 257 (600), 943.9 → 259.9 (2,000); rows
   flex-measured 912 → 152; Junction commit Down 85.5 → 17.3, branches 97.5 → 46.3, files search 518.8 → 261.8; Vesper
   same-names 141.3 → 81.4; ~60 suite rows fewer, ~10 rise by 1–2; 0 B; pixel and geometry hashes identical; Core 5,078
   and Headless 1,553 pass. The layout-change audit across all suites (with Waiting layout-audit-padding-border's padding
   and border compare and layout-virtuallist-spacer-stale's spacer check), a count test, the sideways mirror case
   checked. Prototype `r11-layout\proto-scroll-forced-height.cs.txt`. Time by step-ab (the map click was JIT +14 % after
   round 10 step 11). Small, no API.
5. **apps-refs-hold-last-view-rest** (4; r11-4 Candidate 2; closes Waiting apps-refs-hold-last-view (unmeasured
   holders)) — Aperture `_cursorCard` (ApertureApp.Cursor.cs:40/212): Library 600 → Settings −265 KB (−6.0 %), Home
   5×20 → Settings −264 KB (−6.6 %); Meridian `_historyListRef` with `_historyFieldRef` and `_historyArchiveRef` (all
   three, or 0 is freed): History → Requests −289 KB (−6.1 %) at 200 sends, −238 KB (−4.7 %) at 2,000; Vesper
   `_thumbVListL/R` (keep the height as a float): −274 KB (−4.3 %) at 400 files. Weak-reference tests; each app its own
   commit. Small, app.
6. **apps-meridian-body-formatted-again** (8 with 6, 4 for retention; r11-8 Candidate) **(question 39: yes, the last 4)** — keep
   the last few finished response views by (tab, response, mode) instead of one job slot (`_respJob`,
   MeridianApp.Response.cs:461–505). Switch back to an HTML tab 51,016 KB on all threads (316 on the UI thread) →
   ~200–316; XML 10,864, JSON 6,252 → the same; pretty HTML toggle 52,125 KB; GCs over 20 switches 35/8/2 → ~0; Part
   builds 3 → 1. Kept views 0.4–1.3 MB each (pretty HTML 1,265 KB), bounded LRU (4 suggested). Small–medium, app.
7. **reactivity-meridian-map-list-draws-selection** (6; r11-6 Candidate; the app half of virtuallist-selection-relayout)
   **(question 40: images first, then (c))** — the map list draws its selection (`.SelectionHighlight(Azimuth.Raised, Azimuth.Radius1)`,
   MeridianApp.Discovery.cs:598; hover to `.HoverHighlight`): KB a click 30.8 → 22.2 at 600 endpoints (−28 %), 26.4 →
   19.6 at 60, probe-free −37 %; rows made 2 → 0, layout visits 70.2 → 20.3. After step 4; measure the two together.
   Small, app (+ a public per-side `RowHighlight` border under 40 (c)).
8. **syntax-merged-theme** (5; approved, question 26) — r11-5's scope: SyntaxHighlighter.cs only (a merged raw theme,
   the include chain flattened, included rules before main rules, GUI colours main over included; an options wrapper in
   `EnsureRegistry` :356; `RootedOptions`/`RootedTheme` :541–617 wrap it), ~50–80 lines; only Dark+ and Light+ change;
   ThemeColourTests +3 (Dark+ JSON key #9CDCFE, `this` #569CD6, a Light+ case); no golden changes; round 9's VS Code
   corpus (`r9b14b\probe`) differences should fall to the explained lines; before/after images with step 14b's harness;
   step-ab on the theme load and a key. Small–medium.
9. **tokenize-diff-public** (5; approved, question 10) — r11-5's scope: `TokenizeDiff` (:205), `LineRange` (:14),
   `TokenizedDiff` (:18) public; `TimeProvider? clock = null`; `LineRange` documented 1-based with both ends included and
   validated (rising, non-overlapping, First ≥ 1); arrays null for a side with no text or nothing shown; keep
   `LinesWalked`; drop `<InternalsVisibleTo Include="Junction" />`; reflection and validation tests; a docs section
   (THEMES.md); docfx 0 warnings. Small.
10. **ondafterbuild-markdirty-dropped** (6; r11-6 Waiting) **(question 41: (a))** — under (a): REACTIVITY.md's
    lifecycle table says a MarkDirty or a write to a signal the build read, from `OnBeforeBuild`, `Build` or
    `OnAfterBuild`, is dropped (use `RequestPostFrameCallback` or `InvokeOnMainThread`); delete Vesper's dead
    `RestorePaneScroll` retry (CommanderApp.cs:305, dead since 00b8fc79). Never honour a MarkDirty during `Build`
    (Junction's find bar relies on the drop: 0.95 drops a key on Changes). Small.
11. **suite-document-switch** (8; r11-8 Waiting Suite scenarios) — a suite row for a list cursor that swaps a document
    whose text arrives later (Junction Files Down: 86.2 KB a key, 1.23 builds, 149.1 measure misses, 60.8 layout
    visits), with a twin that builds once. Small, tooling.
12. **findbar-count-after-bind** (6; r11-6 Needs the user; **question 45: yes**) — check first: Meridian builds
    `_responseFind.Build()` before the body binds the search (Response.cs:384) and Junction before `BindDiffSearch`
    (Diff.cs:76), and the re-run's "changed" is dropped, so an open bar may show the old "n of m" after a pretty/raw
    switch or a new file. If it reproduces, fix it in FindBar (read the count after the bind). Small, not perf.
13. **f16-held-drag-post-stall** (3; r11-3 Needs the user; **question 37: yes, one run**) — one diagnostic re-run of
    `drag-vk16-post` (JIT, Vulkan F16, a held drag with a post waiting) with every real-window guard: each step logged
    before `SetWindowPos`, `dotnet-dump collect` at the first stall instead of exiting, then stop. Read the dump's UI
    thread: a driver wait in `AcquireNextImage`/`WaitForFences` (no timeout, SdlVulkanHost.cs:1153, :1169), a deadlock,
    or steps that draw nothing. If it doesn't stall, say so and stop (no repeats). A fix, if one is clear, is its own
    step. Small, measured.
14. **atspi-aot** (7; r11-7 Needs the user; **question 44: yes**) — install clang and lld in WSL Fedora (`dnf`), publish
    expert 7's D-Bus probe (`r11-a11y\dbusprobe\`) as NativeAOT on .NET 11, and compare bytes a signal and a client call
    with JIT (PropertyChange 179–181 B, ChildrenChanged 190, AddAccessible 237–240, StateChanged 101–104, Properties.Get
    1,104, GetRole 896). Information unless AOT differs. Small.

Closed from Waiting by the round 11 survey: dotnet11-aot-textarea-long-document (→ step 1), uitests-liveresize-order,
uitests-workareas-before-dpi-aware, meridian-stalled-timeout-early, meridian-history-test-midnight (→ step 3),
virtuallist-selection-relayout (→ steps 4 and 7), apps-refs-hold-last-view (unmeasured holders) (→ step 5),
syntax-merged-theme (→ step 8), tokenize-diff-public (→ step 9), ondafterbuild-markdirty-dropped (→ step 10, question
41), Suite scenarios (→ step 11; round 10's two other patterns landed as rows in its steps 4 and 7),
layout-ordered-children (→ Examined: the map now 40.5–42.5 hit visits a move, apps ≤ 42.5).

## Round 12 build order

All six steps landed (step 4 (b)'s two other variants measured and dropped, 34dbc7fc; step 3's walk kept as it
is, fd514631). The commits are in the ledger's Landed rows for round 12 (they move here at round 14's survey).
Moved here at round 13's survey (2026-11-12).

Ranked by expected effect in the real apps over cost (the brief's "Real apps first"); no review objection this round.
Each step names the report lines its brief quotes (`r12-k` = `17-reports/r12-k-<area>.md`) and measures its real-app
case in a guarded real window (the desktop lock and harnesses in session 0bab04ae's scratchpad `r12-<area>\`) as well
as by step-ab.

1. **junction-diff-second-frame** (1, with 2 and 8; r12-8 Candidate 3, r12-2 Candidate) — in a real window each key of
   Junction's commit file walk draws a second frame ~3.5 ms after the first (2.00 frames a key with a11y on or off;
   History Down 1.07, Vesper Down 1.02; that frame's render + flush + other ≈ 43–45 % of the key's UI frame time), and
   every discrete size change with a diff shown draws a second full frame (20 frames for 9 size changes against
   Vesper's and Meridian's 10; with the width follow off 14, −28 % text runs). Likely cause, to confirm first: 7ca2b3db's
   after-frame width follow (`FollowDiffWidthAfterFrame`, JunctionApp.Diff.cs:724) giving the pan its viewport after
   the frame. Fix: the pan gets its width before the frame renders (a framework seam in HorizontalPan if needed; no
   public API without asking). Bars: time on a real-app key (−40 % frame time a key if the frame goes), frames per size
   change. Pixels and the pan's reach as 7ca2b3db's tests pin them. Small–medium.
2. **Chrome rebuilt for nothing** (6; r12-6 Candidates 1–2 and its lead for expert 8) — separate commits:
   (a) **reactivity-junction-watch-chrome**: `OnExternalChange` (JunctionApp.cs:713) starts the new generation's
   summary and worktree loads and builds the view only (`RerenderView`); an equal worktree list hands back the old one;
   `RepoSummary` compares by content. Per watcher event, UI KB 581.4 → 412.2 (−29 %), unchanged repo 578.2 → 275.2
   (−52 %), Changes view 496.9 → 315.7 (−36 %); builds 10 → 6 / 2; final frame byte-identical (prototype
   `r12-reactivity\final12.patch`, `R12_WATCHVIEW`/`R12_WTEQ`/`R12_SUMEQ`). Check the GitLab loaders that key on
   `DataGen`; ChromePartTests. (b) **reactivity-meridian-tab-switch-chrome**: a tab revision read only by the Tree and
   Response parts (Requests.cs:387, MeridianApp.cs:535; `R12_TABREV`): 227.8 → 192.7 KB a switch (−15.4 %), XML↔JSON
   −16.0 %, top builds 5 → 3; measure a send's two MarkDirty too (MeridianApp.cs:670). (c) **junction-watch-git-reruns**,
   measure first: 14 git runs (251 K chars) an event in History and 19 (391 K) in Changes, 4.4 MB on all threads; a
   working-tree-only event re-reads the log ×2, the selected commit's diff-tree ×3, `remote` ×2 and `for-each-ref` ×2 —
   keep only the reads the event can change. Small–medium, app.
3. **UIA work in real windows** (7, with 4's and 8's leads; r12-7 Candidates, r12-8 Candidate 2, r12-4 Candidate 2) —
   separate commits: (a) **a11y-junction-diff-uia-bytes**, split first: Junction's commit file walk (bigrepo, 33 ms
   keys) 977 KB a key on the UI thread with the bridge against 586 with `HULTUI_A11Y=0` (= headless), "other" 16–17 % of
   frame time against 1.2 %; find what the bridge does per document change (each raise costs 13 provider calls + 13
   QIs; wrapper upkeep) and cut it with reader events identical. (b) **a11y-resize-walk-debounce** **(question 47)**:
   hold the coalesced layout walk until the size has rested 100 ms (Window.cs): BoundingRect raises a resize Vesper
   1,596 → ~125, Junction 625, Meridian 319; ~11 fewer frames a resize; Vesper 20,930 provider calls + 20,930 QIs a
   resize today. (c) **a11y-item-added-past-end-offscreen** **(question 48)**: the 5-line prototype
   (`r12-a11y\rw\all-probe-edits.patch`): told in frame 0 by 1 in-place walk, 0 trees. Audit across the 7 suites; reader
   event streams identical but for what (b) and (c) change on purpose. Small–medium.
4. **text-fallback-front-cache** (5; r12-5 Candidate 1, then Candidate 2 measured first) — (a) a 4,096-slot struct
   table in front of `SkiaFontCollection.MatchCharacter` (SkiaFontCollection.cs:177), keyed by code point, weight,
   slant, Han context and UI culture, cleared in `Register` (patch `r12-text\proto-front-cache.patch`): a hit 93–96 →
   12.2–12.9 ns, a CJK prefix pass −34 %, 0 B; Junction on a Japanese README: open 91,626 lookups (88,734 hits), one
   resize 114,717; corpora unchanged. (b) **text-cjk-rewrap-partial-shaping**, only if bit-identical on the 48k/240k
   corpora and a CJK wrap corpus: read a prefix's width from the run's pen position at safe-to-break boundaries
   (`GlyphInfo.GlyphFlags`) instead of shaping it again (14,105 HarfBuzz calls at a new width); otherwise record the
   numbers. Small (a), medium (b); no API.
5. **vk-chrome-8bit-until-hdr** (3; r12-8 Candidate 1, r12-3 F16 retained memory, r12-4 Candidate 1) **(question 49:
   yes — F16 only while an HDR video plays)** — Aperture and Reel draw through an 8-bit swapchain unless an HDR video is
   playing, and rebuild to F16 when one starts (back to 8-bit when it stops): rest wakes 209 → ~112 a second, swapchain 31.1 → 15.6 MB, and the first resize's kept +50 MB
   private / +26 MB DXGI gone (8-bit returns everything). Medium; pixels (SDR brightness on an HDR display), one
   rebuild a switch.
6. **meridian-editor-floor-measured** (5 with 8; r12-5 Waiting) **(question 46)** — the editor's floor is the larger of
   448 and the sub-tab strip's measured width (MeridianApp.cs:97–103), so `NarrowEditorLayoutTests` passes on Fedora's
   Noto Sans; Windows unchanged. Not perf. Small.

Closed from Waiting by the round 12 survey: terminal-noop-scrollback-wheel and terminal-row-strings-by-content,
vesper-folder-nudge-stale, junction-held-key-bigfiles (→ Examined), a11y-item-added-past-end-offscreen (→ step 3 (c),
question 48), meridian-narrow-editor-fedora-fonts (→ step 6, question 46).

## Needs the user, rounds 1–4

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
    renderer to SkiaSharp's C API at the pinned 3.119.2, as e21e376 did for HarfBuzz? [Yes, with a
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
12. No-op glGetError — moot: 54c56d9 showed the driver wait only moves (declined).

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

## Needs the user, round 5

Round 5 questions 1–4 (asked and answered 2026-10-25; question 5 stays open in the ledger). Recommendations in brackets.

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
4. **vesper-detail-resize-stale** (not perf): in Detail view a resize never rebuilds, so the path trail's
   truncation stays stale until the next key; e6a432fa's fit comparison runs only in grid views. Fix it in
   step 8? [Yes, small.] → **yes** (2026-10-25).

Question 5 (answered 2026-10-27 as round 6 question 4; moved here at round 7's survey):

5. **junction-history-row-selection-pixels** (from r5 step 9): Junction History can adopt the list-drawn selection
   (survey −30 % per key) only if two things move out of its rows, which changes pixels: the caret row's "Details →"/
   "Compare →" affordance (it sits in the row's flow, so that row's subject ellipsis comes earlier) and the hover tint
   (unselected rows tint under the pointer, the selected one doesn't). May the affordance become an overlay at the
   row's end and the list own the hover tint? [Ask when convenient; not blocking.] → **yes** (2026-10-27, round 6
   question 4; step 10).

## Needs the user, round 6

Round 6 questions 1–9 (asked 2026-10-27 to 2026-11-01, all answered; question 10 stays open in the ledger).
Recommendations in brackets.

1. **dialog-scrim-button-role** (step 3): dialogs and the command palette stop being read inside a "button" named
   after all their text; a palette keystroke raises one Name event instead of two; Meridian's Options backdrop the
   same. Removes events, adds none. [Yes.] → **yes, fix it** (2026-10-27).
2. **palette-virtual-rows** (step 9): palette rows not scrolled to either leave the reader's tree, as in every other
   VirtualList, or are served as list items (role Button → list item). [Leave the tree, as every VirtualList does; the
   selected row is always on screen.] → **leave the tree** (2026-10-27).
3. **Public API additions:** (a) `VirtualList.StickyRows(...)`, held and pushed off like `Sticky()` (step 11); (b) a
   TextArea highlight layer drawn over `Spans`, so find hands over only its hits (step 7; Junction Files too; the
   alternative is an app-side merge, −60 % instead of ~−90 %); (c) a public `Part` in Core following Vesper's `Place()`
   rule, first used by Aperture (step 13). [Yes to all three, each opt in.] → **yes to all three** (2026-10-27).
4. Round 5 question 5 below, with round 6's numbers (History −50…−52 % per key). → **yes, change the pixels**
   (2026-10-27): the affordance becomes an overlay at the row's end and the list owns the hover tint (step 10).
5. **app-modal-cards-dialog-role** (from step 3): Junction's 9 modal backdrops, Vesper's `Backdrop` and Meridian's
   environment prompt hold cards with no role, so each backdrop is still a Button named after the whole card, with
   the same cost (~260 KB per text change over 1,000 rows with UIA). Give each card `.AccessibleRole(Dialog)` and a
   name: readers then hear a dialog where today they hear a button. Also: a dialog with nothing focusable focuses the
   scrim, now an unnamed pane (was a button reading the dialog's text) — focus the dialog instead. [Yes to both, in
   the app steps 5, 7 and 10.] → **yes, make them dialogs** (2026-10-27).
6. **popover-menu-role** (from step 7a): Meridian's (and Junction's) popover menus are read as Buttons named after every
   row. A menu role with items (like the context menu's) would change what readers hear. [Yes, as the context menu.]
   → **yes, make them menus** (2026-10-27; with step 10, Meridian's half included).
7. **palette-rows-told-selected** (from step 9; not blocking, kept on the branch): the brief said the palette's
   selected row must stay told, but it never was, so step 9 added `AccessibleSelected` on every row. New reader
   behaviour: rows gain SelectionItem and may read "not selected", and selecting a row from assistive technology
   clicks it, which runs the command. Keep it, or take it out? [Keep: a reader then knows which row Enter runs. It's a
   small commit to take out.] → **yes, keep it** (2026-10-31).
8. **junction-review-composer-reader** (from step 10a; not blocking; an existing gap): a reader can't reach the comment
   being typed in Junction's review, because the editor in that list row has no node and the row is read only as "new
   comment". Making the list `RowsAreComposite` while reviewing would fix it but changes what a reader hears, and costs
   a11y work per key. [Yes, fix it: a comment nobody can read back is a real gap.] → **yes, fix it** (2026-11-01;
   step 11b).
9. **junction-changes-offscreen-rows** (from step 11; not blocking, kept on the branch): the off-screen answer you gave
   named the palette and Commit and Files had it earlier, but not Changes by name. Rows off screen now leave the
   reader's tree there too, and Tab no longer reaches a Hidden file's "Watch again" or an off-screen "Stage all" (before,
   Tab landed on something not visible). The held header stays, with its button. [Keep: it's what every other virtual
   list does, and the list scrolls to its selection.] → **yes, keep it** (2026-11-01).

## Needs the user, round 7

Round 7 questions 1–4 (asked 2026-11-02, all answered; question 10 stays open in the ledger).
Recommendations in brackets.

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

## Needs the user, round 8

Round 8 questions 11–13 (asked 2026-11-03 and 2026-11-08, all answered; question 10 stays open in the ledger).
Recommendations in brackets.

11. **a11y-disabled-menu-item** (with step 2): disabled context-menu and menu-bar items are read as enabled. The fix
    adds a public default interface member, `IAccessibleItemSource.ItemIsEnabled(int)` (default: enabled), which
    `ContextMenuElement` answers from `IsDisabled`. Additive; no existing implementer has to change. [Yes: a reader bug,
    small.] → **yes, with step 2** (2026-11-03).
12. **element-restyle** (step 7, 80f31170; not blocking): step 7's agent added a public `Element.Restyle(Action<Element>
    change)`: it changes a live element's look (a ring, a background) so that its hover, press and focus styles layer over
    the new look. Set directly, the look is lost when a hover or press ends (the style restores what it first captured).
    Vesper needs it from another assembly; `KeptElements.Live` users will too. Keep it public? [Yes: it is the fix for a
    real trap, and REACTIVITY.md now tells authors to use it. The other way is an internal method plus friend access
    for the app.] → **yes, keep it public** (2026-11-08).
13. **paint-meridian-history-faded-row-layers** (step 9 measured, no commit): Meridian History fades older sends with
    `row.Opacity(0.6)`, so every visible row is its own offscreen layer (29 a frame). Real window, 1440×900 at 150 %,
    AOT, idle-gated, ABAB + BABA, medians with → without layers: GL wheel 0.923 → 0.627 ms a frame (−32 %; GPU 0.953 →
    0.261, −73 %), Down −27 %; Vulkan 8-bit wheel 0.906 → 0.685 (−24 %; GPU −86 %), Down −25 %; F16 wheel 1.059 → 0.774
    (−27 %; GPU 2.240 → 0.483, −78 %), Down −20 %. Fading the colours instead (60 % alpha on each text and the chip)
    changes pixels: 8-bit 18–20k pixels by exactly 1/255 (glyph edges) and 2,478 inside the 32 method chips, max 8/255
    (chip text over its tint); F16 49.7k at 1/255 and 19.7k in the chips, max 6/255. May History's faded rows change
    those pixels? [Yes: invisible at 1–8/255, and a fifth to a third of every History frame.] → **yes, fade the colours** (step 9b, 2026-11-08).

## Needs the user, round 9

Round 9 questions 14–25 (asked 2026-11-08 and 2026-11-09, all answered; question 26 stays open in the ledger).

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
    change by ≤ 2/255? [Yes: invisible, and a sixth to two fifths of those frames.] → **yes** (2026-11-09; step 13b).
21. **apps-vesper-faded-rows: Sync's Equal rows** (step 13): Sync with "Show equal" on fades each Equal row with
    `row.Opacity = 0.55f` (21–26 layers a frame). Fading the row's text and fills to 55 % instead (as Meridian History,
    question 13): Down/Up GL 0.824 → 0.511 ms (−38 %), GPU −77 %; Vulkan 8-bit −21 % / −82 %; wheel and hover GL −36…−37 %,
    Vulkan −18…−22 %, F16 −20…−27 % (GPU −56…−81 %). Pixels: 13.7k–15.4k glyph-edge pixels at 1/255, one pixel at 9/255
    (F16 at most 4/255). May they change? [Yes: as question 13.] → **yes** (2026-11-09; step 13b).
22. **apps-vesper-faded-rows: cut thumbnail tiles** (step 13): a cut tile fades as a whole (`Opacity 0.45`, 29.5–33
    layers a frame). Fading each part instead: GL −26…−27 % frame, GPU −71…−73 %; Vulkan −20 % / −83…−84 %; F16 −15…−17 %
    / −68…−69 %. But the pixels move more: 99.7 % of a cut thumbnail's pixels by a mean of 5/255 (at most 6, F16 9) because
    the tile's dark backing shows through the faded picture; a badge over its kind icon by up to 24/255; kind icons and
    the cursor tile's ring and fill up to 7/255 (19 on F16); names up to 3/255. Leaving the backing out under the picture
    should avoid most of the thumbnail shift (not built or measured). [Build that variant first and measure; if every
    part stays within ~2/255 but the badge, ask again with its numbers.] → **as recommended** (2026-11-09): step 13b
    builds the backing-free variant, uncommitted, with new comparison images; then ask again.
    Step 13b's backing-free variant (`r9b13b\tiles-variant.patch`, uncommitted; images
    `artifacts/r9-fade-compare/tiles-variant-{gl,vk16}.png`, sent 2026-11-09): layers 29.8/32.3 → 0, GL frame −27…−28 %,
    GPU −73…−74 %, Vulkan 8-bit −20 % / −84…−85 %, F16 −19…−20 % / −71 %. Pixels against today (GL / Vulkan 8-bit / F16):
    pictures 37k/37k/4k of 159k px (33k at 1/255), max 7/7/10 only in the half-pixel letterbox rows; badge max 24/24/19;
    kind icon max 7/7/19 (the backing still under it); the cursor tile's fill through the backing 6/6/19, ring max 4;
    names max 3; Vulkan only 17–30 px at 13/255 at the letterbox bars' rounded ends. It does not stay within ~2/255
    apart from the badge. Asked again with these images: commit this variant? [The user's eye: the zooms; my lean is
    yes for the pictures, the kind icons and cursor tile changes are small and only on cut tiles.] → **no: it changes too much**
    (2026-11-09). Cut tiles keep their layer; examined.
23. **syntax-encoded-tokens-colours** (step 14, 2026-11-09): colouring from TextMateSharp's encoded tokens
    (`TokenizeLine2`) would save a further 28 % of the tokenizer's bytes on JSON, 18 % on XML, 4 % on HTML. It matches
    theme rules by parent scope, as VS Code does, so it changes colours on 2,388 of 1,752,346 corpus lines (mostly
    TypeScript/JavaScript, then PowerShell, shell, C++ and Markdown; nothing in Meridian's JSON/XML/HTML response bodies).
    May colours change to VS Code's parent-scope matching? [Show before/after examples of the changed lines first.] →
    **as recommended** (2026-11-09): step 14b makes before/after images; then ask again.
    Step 14b (no commit; switch kept in `r9b14b\switch.patch`; images `artifacts/r9-syntax-compare/01–10`, sent
    2026-11-09): only Dark+ and Light+ change (the themes that include a second theme file); Solarized, Kimbie, One Dark,
    Dracula, Tomorrow Night Blue change 0 lines. Dark+ groups: TS/JS quoted object key 950 lines #9CDCFE → #CE9178
    (the most noticeable: whole key tables), `new` 863 #C586C0 → #569CD6, TSX JSX braces 310 (light blue → each token's
    colour), TS/JS numeric key 103, PowerShell `$x.Member` 54 #DCDCAA → #9CDCFE (away from VS Code: TextMateSharp never
    counts the root scope as a parent), C/C++ `.`/`->` member 52, shell `${…}` 33, smaller JSDoc/Markdown groups. 2,334 of
    2,388 move to VS Code's colour. No JSON/XML/HTML line changes (an HTML body's inline `<script>` changes like TS/JS).
    The agent recommends adopting at least for JSON, XML and HTML (where the bytes are: −28 / −18 / −4 % of the
    tokenizer's bytes) or for every language (cost: the 54 PowerShell lines). Asked again with the images. → **yes, "an
    improvement in my eyes"** (2026-11-09): step 14c adopts it for every language.
24. **vesper-pane-name-folder** (step 15): Vesper's pane is now read as "Left pane, <folder>". Because the walk can't
    patch a name on a node with children, a folder change with a reader on costs one tree (~+20 KB, UIA 209 → 229 KB a
    navigation). A fixed "Left pane" name costs nothing. Keep the folder in the name? [Yes: a reader learns where it
    is; the cost is once per navigation, only with a reader.] → **yes, keep it** (2026-11-09).
25. **a11y-selected-patch-for-cells** (step 15; framework, internal): to tell a reader Vesper's cursor in Brief and
    Thumbnails with 0 trees, the window's selected-state patch (today only for a menu entry or a row directly inside a
    composite list, Window.cs:4659) must also take a cell or tile, and the thumbnail list must publish its rows as
    composite (`RowsAreComposite`). Prototype: the cursor item told by name and selected state, 0 trees, flat KB; +~100
    nodes a navigation with a reader (261 → 280 KB). Question 19 said "in the app"; this needs the framework. Allow it?
    [Yes: internal, no API, and without it Brief and Thumbnails stay silent to a reader.] → **yes** (2026-11-09; step
    15b).

## Needs the user, round 10

Round 10 questions 27–36 (asked 2026-11-09 to 2026-11-11), round 9's question 26 and the earlier question 10, all
answered by 2026-11-11. Moved here at round 11's survey.

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

33. **meridian-runner-unreadable-count** (step 4, 2026-11-10; behaviour, minor): the Runner's rail now counts from the
    scanned collection, so a request file that can't be read is counted in "N req" and "Run · N req" but still left out
    of the run ("49 / 49" beside "Run · 50 req"); before, the count read every file and left it out. Keep, or mark
    unreadable files apart in the rail (a small follow-up)? [Keep: rare, and the run still reports what it ran.] →
    **keep** (2026-11-11).

34. **meridian-map-rows-list-items** (step 11, 2026-11-10; reader behaviour): the Discovery map is now a virtual list,
    so a screen reader hears each endpoint as a "list item" (selected / not selected) in a list, where before it was a
    "button" among buttons; names are the same, every endpoint is still reachable (items served on demand at 2,000), and
    invoke or select on an item selects it like a click. Round 10's survey had flagged an app-only switch to list items as
    the user's call. Keep the list items, or keep the rows as buttons (a list of Buttons is possible but loses the served
    items and the selected-state patch)? [Keep: a selectable list read as a list is the standard pattern, as Vesper's
    Brief cells were approved as list items in question 25.] → **keep list items** (2026-11-10).

35. **scrollview-userscrolled-scope** (step 15, 2026-11-11; public API): question 28 approved a callback for "the
    scrollbar or autoscroll"; step 15 made `ScrollViewElement.UserScrolled` fire for every user move — the wheel, the
    scrollbar, autoscroll and a VirtualList's scroll keys — so one callback means "the user moved it" (`ScrollEvent` is
    unchanged; Brief listens to `UserScrolled` alone). Keep the wider scope, or narrow it to bar + autoscroll (a small
    change; Brief would listen beside `ScrollEvent`)? [Keep: one meaning, and a listener needn't combine two signals.]
    → **keep it wide** (2026-11-11).
36. **vesper-brief-return-offset** (step 15, 2026-11-11; behaviour): Brief now comes back from Detail, Thumbnails or the
    viewer/diff/sync at the start (offset 0) with its columns made for the start; before, it tried to restore its old
    offset, but the restore landed only after a build and left the pane blank until a key (17 of 21 return steps
    blank). The old position is no longer kept. (a) Keep returning at the start, or (b) restore the old position
    properly as a step 15b (a ScrollView applies an offset set before its first layout once it has laid out — a fix to
    how the existing setter behaves before layout, no new API — and Brief makes the columns for it)? [(b): returning
    where you were is what a file manager should do; small.] → **(b)** (2026-11-11; step 15b).

Round 9 question 26 (from step 14b, 2026-11-09), answered 2026-11-11. Round 9's answered questions 14–25 are in
[the archive](17-performance-team-ledger-archive.md#needs-the-user-round-9). Recommendations in brackets.

26. **syntax-merged-theme** (from step 14b; not perf, not blocking): today and the encoded path both differ from VS Code
    on ~974k corpus lines because TextMateSharp keeps an included theme in a second lookup and the main theme's broader
    rule wins: JSON keys are teal where VS Code shows #9CDCFE (every key in Meridian's JSON bodies), `this` is #9CDCFE
    where VS Code shows #569CD6. Giving it one merged theme would match VS Code. Wanted? [Later, its own step with
    images: it recolours every JSON key.] → **yes** (2026-11-11; Waiting syntax-merged-theme, round 11).


10. **tokenize-diff-public** (from step 12; not blocking): `TokenizeDiff` in HultUI.Syntax tokenizes only the lines a diff
    shows and is `internal`, with Junction as a friend assembly. Another diff view would want it. Make it public? [Yes,
    but later: no second user exists, and a friend assembly costs nothing. Decide when one turns up.] → **yes**
    (2026-11-11; Waiting tokenize-diff-public, round 11).

## Needs the user, round 11

Round 11 questions 37–45 (asked 2026-11-11), all answered on 2026-11-11. Moved here at round 12's survey.

Round 11 questions (from the survey, 2026-11-11). Recommendations in brackets.

37. **f16-held-drag-post-stall** (r11-3): a Vulkan F16 held drag with a post waiting stopped drawing for 1 s once (no
    driver event, focus never taken; never run before; not repeated). Allow **one** diagnostic re-run (JIT, F16, a post;
    each step logged before `SetWindowPos`, `dotnet-dump collect` at the stall instead of exiting, stop at the first
    stall)? If it is a deadlock, an HDR app could freeze during a held resize. [Yes: one guarded run.] → **yes** (2026-11-11; one guarded run, step 13).
38. **a11y-item-rows-offscreen-label** (r11-7; step 2): a reader then hears an item's new label when it changed while
    its row was off screen (today it keeps the old one, "seen 1×", until something rebuilds). Fix it as a bug? [Yes.] → **yes** (2026-11-11; step 2).
39. **apps-meridian-body-formatted-again** (r11-8; step 6): switching back to a request tab or toggling pretty/raw then
    shows the finished, coloured body at once (today an empty body, or the other mode greyed, then uncoloured text,
    then colour). Approve, and how many views to keep (0.4–1.3 MB each)? [Yes; the last 4 (tab, response, mode).] → **yes, the last 4** (2026-11-11; step 6).
40. **meridian-map-selection-look** (r11-6; step 7): the list's own highlight can't draw today's 2 px left-only accent.
    (a) a 2 px accent all round the selected row, (b) no accent, the Raised fill only, or (c) a new public
    `RowHighlight` border width per side (today's look kept)? Images can be made: the headless map at 600 endpoints with
    row 3 selected and hovered, base against the prototype. [Images first; (c) keeps today's look.] → **images first, then (c)** (2026-11-11; the user took the recommendation; step 7 adds a public per-side `RowHighlight` border).
41. **ondafterbuild-markdirty** (r11-6; step 10): (a) document that a MarkDirty from `OnAfterBuild` is dropped and delete
    Vesper's dead retry, or (b) honour it, deferred to the frame's drain with a per-host cap per frame (never during
    `Build`)? Either costs 0 builds in today's apps. [(a).] → **(a)** (2026-11-11; step 10).
42. **syntax-typst** (r11-5): do the apps meet Typst (`.typ`) files? The first one keeps +42 MB of grammars until
    `SetTheme`; the fence-scan route (step 12 of round 10) fixes it with no colour change. [Only if you open Typst files
    in Junction or Vesper; else park.] → **no Typst: skip** (2026-11-11; syntax-shared-registry-rest stays parked).
43. **report-upstream** (r11-4, r11-5, the upgrade): file the two .NET 11 regressions on dotnet/runtime — ILCompiler 11
    no longer devirtualizes `IAlternateEqualityComparer` in `Dictionary.AlternateLookup.FindValue` (a 40-line repro,
    `r11-dotnet\altprobe\`), and JIT 11 boxes `HashCode.Add<T?>` of a nullable struct in a loop (the upgrade's
    probe)? Public posts from your account. [Your call; steps 1 and c99e23bf work around both.] → **no** (2026-11-11; nothing filed upstream).
44. **atspi-aot** (r11-7): install clang and lld in WSL Fedora to measure AT-SPI on NativeAOT? [No: nothing points to an
    AOT difference.] → **yes** (2026-11-11; install clang and lld in WSL Fedora, step 14).
45. **findbar-count-after-bind** (r11-6; step 12): if an open find bar's "n of m" goes stale after a pretty/raw switch or
    a new file (read in the code, not reproduced), fix it in FindBar? [Yes, after a check; not perf.] → **yes** (2026-11-11; step 12).

## Needs the user, round 12

Round 12 questions 46–51 (asked 2026-11-12), all answered on 2026-11-12 (50 and 51 left Waiting items). Moved
here at round 13's survey.

Round 12 questions (from the survey, 2026-11-12). Recommendations in brackets.

46. **meridian-editor-floor-measured** (r12-5; step 6): Meridian's editor keeps a floor of 448 px tuned to Segoe UI, so
    on Fedora (Noto Sans) the chain sub-tab is cut by 4.6 px and `NarrowEditorLayoutTests` fails there. Take the floor
    from the measured sub-tab strip (on Linux the response pane moves under the editor ~5 px sooner; Windows unchanged),
    or keep 448 and let the test check only the Windows face? [The measured floor.] → **the measured floor** (2026-11-12; step 6).
47. **a11y-resize-walk-debounce** (r12-7; step 3 (b)): during a live resize a reader would be told element bounds once
    the size has rested 100 ms, not every 100 ms of the drag (today they already arrive up to 100 ms late). Vesper
    1,596 → ~125 raises a resize, ~11 fewer frames. Accept this as coalescing? [Yes.] → **yes** (2026-11-12; step 3 (b)).
48. **a11y-item-added-past-end-offscreen** (r12-7; step 3 (c)): a reader then hears items appended below the visible rows
    (Junction History paging, Aperture library paging, a Vesper file that sorts last); today its list stays short until
    something else walks. Fix it as a bug, like question 38? [Yes.] → **yes** (2026-11-12; step 3 (c)).
49. **vk-chrome-8bit-until-hdr** (r12-8, r12-3, r12-4; step 5): Aperture and Reel at rest on an HDR display wake ~207
    times a second (an NVIDIA interop thread at ~96/s exists only with the F16 swapchain) and keep +50 MB private and
    +26 MB GPU memory after their first resize. Draw them through an 8-bit swapchain while no HDR picture is on screen
    and rebuild to F16 when HDR video appears (one rebuild a switch; possibly a visible step in SDR brightness)?
    Before/after images can be read in-process (`HULTUI_VULKAN_CAPTURE` with `HULTUI_VULKAN_F16=0` and `=1`). [Images
    first, then decide.] → **yes** (2026-11-12): the HDR (F16) swapchain only while an HDR video is playing; 8-bit
    otherwise (step 5; the before/after images still go to the user as a check).
50. **a11y-gate-on-client** (r12-7): in 15 real launches no UIA client ever asked for an app's root, so building the tree
    and raising events only after a client asks would remove all bridge work here (Junction's commit file walk −40 % UI
    bytes). The risk: a client subscribed desktop-wide that acts on events alone could miss events. [Not yet: decide
    after a live check with Narrator and NVDA running, which you would run (it could go with the menu check).] → **not
    yet** (2026-11-12): decide after the live reader check (Waiting).
51. **after-layout-callback** (round 12 step 1): Junction's resize now guesses the diff's width before layout (the list's
    last width plus what the window gained or lost), because no public hook runs after layout and before render. Add
    a public `Window.RequestAfterLayoutCallback` (or similar) so an app can set a value from the laid-out tree in the
    same frame, and move Junction to it? [Yes if another app needs it; today the guess is right in every measured
    case, and the after-frame check stays.] → **only if another app needs it** (2026-11-12; Waiting).

## Log, 2026-10-07 to 2026-10-25

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
  fecfbbc on a detached origin/main (main had aa5cba1/c1c93d4 from another session), suite
  passed again on the merge, pushed c1c93d4..fecfbbc, primary fast-forwarded behind the guard.
- 2026-10-09: CI green on fecfbbc (run 37861109308). Round 2 survey started, eight experts.
- 2026-10-09: round 2 surveys in (all eight not satisfied); reports in `17-reports/r2-*`;
  queue re-ranked into 14 build steps; 12 questions for the user; survey worktrees removed.
- 2026-10-09: the user answered all 12 round 2 questions (recommendations taken; WSL install yes). Queued items 15–18.
- 2026-10-09: installed Mesa + Noto fonts in WSL Fedora (user approved); round 2 step 6 next.
- 2026-10-10: round 2 full suite passed (30 projects, 0 failures); merged into main as 0951288, pushed fecfbbc..0951288, primary fast-forwarded.
- 2026-10-10: CI green on 0951288 (run 38072271269); tagged and pushed the five app releases (3 + 2).
- 2026-10-10: all five app releases published with installers (Vesper re-run once: runner not acquired). Round 3 survey started.
- 2026-10-10: round 3 surveys in (all eight not satisfied); reports in `17-reports/r3-*`. Three
  round 2 regressions (ba79958, cfe2691, 7dc7e0e) and a UIA closed-window leak lead the queue;
  16 build steps, 13 questions for the user; survey worktrees removed (probe code kept in the
  scratchpad). Paused for a break at the user's request.
- 2026-10-10: two user reports during the pause. Vesper's thumbnail area kept its old width after a
  resize until a key press: fixed d3d81ee (Brief and Thumbnails rebuild on resize). Aperture's Home
  resizes much slower than other apps: an app-workloads diagnosis agent is measuring it (Vulkan F16
  swapchain vs whole-app rebuild per step vs card pictures); result goes into the round 3 build.
- 2026-10-10: Aperture resize diagnosed (`17-reports/r3-aperture-resize.md`): not the start screen —
  every step rebuilds the Vulkan F16 swapchain (92 %, 32 of ~37 ms; Reel the same; GL apps 4.6 ms).
  Drag over-allocation prototyped 37 → 9 ms; joins step 9 if approved (question 14); the
  FIXED_EXTENT opt-in is broken. Questions now 15.
- 2026-10-10: the user took the recommendations on 14 (yes, fix the opt-in) and 15 (not now);
  questions 1–13 still open.
- 2026-10-11: round 3 build resumed. Step 1 landed c25f057, 351e309 (minimized-window
  regressions). Guideline notes: dispose MediaPlayer and web view controllers when they close (a
  live GpuFrameSink makes a dirty minimized window walk its tree); an app's own IGpuImageSource is
  released by setting Source = null.
- 2026-10-11: step 2 landed (d46008c..bbed443): selection moves patched, not rebuilt (+ a11y-only
  frames draw nothing, a86de0d), AT-SPI status serialized, UIA text value from the node, UIA
  providers released on close. Windows and WSL test projects pass. New waiting item
  a11y-uia-host-reads-off-thread. Guideline notes: `SelectedIndex` costs no a11y rebuild up to 2,000
  items; in tests `PumpFrame()` is false for a frame only accessibility asked for.
- 2026-10-11: step 3 landed (b985e74..6902403): Junction History one rebuild per key; diff rows
  memoized on item rows; diff, comparison, viewer and thumbnail rows named for readers; two suite
  rows. Guideline notes: rebuild for a background result only if the build on screen read it;
  never derive list rows in Build (memoize by input identity, item rows + shape key, mutation-test
  every shape input); label every record/index list (ToString() dumps cost 744 KB per keystroke);
  removing allocations can move a gen-0 GC into a frame — compare frames without a GC.
- 2026-10-11: step 4 landed (e019d7d..f5fafaf): Reel playback session −87…−88 % UI-thread bytes,
  VideoPlayer rebuilds 290 → 5 per 10 s. Found: Aperture's and Reel's periodic playback duties
  (heartbeat, resume save) run in the app Build, which rarely runs during quiet playback; queued
  as step 4b (correctness) ahead of step 5. Guideline notes: read fast signals untracked in Build
  and write live elements from an OnMount Effect; read coarse facts through a Computed; keep an
  animation level in a plain field; new suite rows can shift other rows' JIT timings.
- 2026-10-11: step 4b landed (2316a4d..b867e2a): Aperture's progress heartbeat and Reel's resume
  save now run during quiet playback; broken since each app's first commit, so the current and
  previous releases have it. Guideline: Build is not a clock — periodic work rides a ticking signal
  and checks `Application.Time`; state-driven work goes in an Effect disposed with what it watches.
- 2026-10-11: step 5 landed (7d6cc85, 99e1157): prefix tables in kept lists (long document open
  −58 % bytes, 20k-line log −64 %); span prefix measure for merged-cluster lines. **Release phase:
  run the WSL Linux tests** (text change; not run in the step). Guideline notes: build one-off tables
  in capped kept scratch lists and copy exact arrays out; measuring callbacks take text + length;
  a BiDi width cache must key on paragraph direction.
- 2026-10-11: the user answered questions 1–13: recommendations on 1, 3–10, 12, 13; (a) on 2 (every
  pointer-move handler once per frame); yes on 11 as long as headless still works.
- 2026-10-11: the user changed question 2 to (b): only splitter, pan, slider and scrubber coalesce.
- 2026-10-11: step 6 landed (218f831, 2af43b9): style marking once per subtree (component edit
  −15 % JIT), theme switches lay out only what changed size. Guideline note (THEMES.md): ApplyTheme
  runs every style pass — mark layout only when a measured size changed, and mark a part whose box
  you wrote. For reconcile-restyle-on-change: `NoteChildJoined` must stay in the three add paths.
- 2026-10-11: step 7 landed (480f294, dbcdaef, f0238b2): AOT blob building −56 B per blob,
  Powerline/task-tick frames ~0 B, merged-cluster lines 0 B per steady frame. Guideline notes: AOT
  and tier-0 JIT heap-allocate helpers warm JIT keeps on the stack — prefer struct APIs and check
  AOT bytes; pass polygon points as a span; a closure capturing a struct on a draw path costs ~120 B
  per call — cache by value on a shared owner.
- 2026-10-11: step 8 landed (3891499, 8ddc3eb, ea6c48e): native text blob handles (the user's
  headless condition met; a fourth C function, `sk_textblob_get_bounds`, was needed — told to the
  user) and per-stretch sub-blobs (spanned text −15…−70 % time). **Incident:** step 8's GPU probe
  window (Borderless, env var only) took the foreground four times, ~1 s each, while the user was in
  their browser; the brief's focus rule now requires the default frame, the in-process SDL hint and
  a foreground guard.
- 2026-10-11: step 9a landed (9084be9, 5002e2f, 4c3c1af): Vulkan frees disposed-image textures
  (−178 MB DXGI on 8 photos) and keeps swapchain render targets (2 KB/frame → 0, frame −10…−16 %).
  **Focus, again:** an early 9a probe window (default frame, in-process hint) took the foreground
  twice; its guard handed it straight back and stopped the run. `WS_EX_NOACTIVATE` fixed it over
  several hundred runs; the brief's focus rule now requires it too.
- 2026-10-11: the user reported that when 9a's probe stopped mid-frame, Brave's videos paused for a
  while and recovered; information only (runs continue as they are). Brief: close windows
  gracefully, batch launches, log a timeline, stop on device-lost or stalls over 1 s.
- 2026-10-11: step 9b landed (6458cc8..16f81bc): drag steps ~35 → 6–8 ms on Vulkan (MAILBOX).
  Incidents, told to the user: the focus guard fired once (21:18, Aperture's custom frame activates in
  its constructor — queued as customframe-install-activates); FIFO one-to-one probes hung (one window
  21:49–22:00 until terminated) → MAILBOX-only rule; **two mid-drag screen captures at ~23:40 grabbed
  the user's browser instead of the probe window** (the driver's HWND_TOPMOST did not raise it); the
  agent viewed one and deleted both. Captures are now in-process only and saved only when the probe's
  marker is on screen; the remaining four are 1618×904 window frames.
- 2026-10-12: the user is away until 18:00 (no load expected). Step 10 landed (3c81fb6..873a7e5):
  idle apps wake 0 times a second (was 61), minimized 0 (was 36–38); deadlines on time; focus guard
  never fired. Decided without asking: bound the tween first step by one interval (step 12), inside
  the approved answer to question 5.
- 2026-10-12: step 11 landed (fefb81c..9569e6e): a hover move costs only the platform's 40 B input;
  splitters, pans, sliders and the scrubber rebuild once per frame (−85…−94 % bytes per drag frame).
  Focus guard never fired. Guideline notes: hover styles cost nothing per move; a root move handler
  costs 64 B per move window-wide; an app's own move handler that calls MarkDirty still rebuilds per
  move (no public coalescing API).
- 2026-10-12: step 12 landed (4aa6358..d721937): toasts 23 → 2 app builds; Junction spinners and
  remote-op motion 0 rebuilds; Meridian's send readout no longer stacks; no extra first frame. Added
  step 17 (round 3 fix-ups): the GitLab test-fake race failed twice and would make the full suite
  flaky, a pre-existing toast opacity bug, and the custom-frame activation. Guideline notes: animate
  the element through a Ref; `onChanged` wired to MarkDirty fires on discrete changes only; a readout
  timer writes its label and stops with its owner.
- 2026-10-12: step 13 landed (dc6c047..896e99a): TextArea edits re-wrap only touched lines (−8.3 MB
  held on a 20k-line log, typing −26…−28 %), text memos bounded by bytes (worst case −51…−59 %
  retained). Step 14 split: 14a framework parts + Vesper, 14b Junction lists + Aperture grid.
- 2026-10-12: step 14a landed (cfb9b01..0971573): rows inherit from their list (Branches layouts per
  key 255 → 34), kept item labels (−59 % a11y walk), Vesper on kept rows (−60 % per key). Guideline
  notes: a shape field changes only when what is drawn changes (normalise "nothing"); a label lambda
  capturing build state keeps nothing — hold the delegate in a field. Lead: `InvalidateRow(i)` would
  let Vesper's same-name partner remake ~4 rows a key instead of 32.
- 2026-10-12: step 14b landed (f12d789..921605c): Junction Branches −72 %, Tags −53 % per key; Files
  on a virtual list (−99 % per key, −90 % time); Aperture grid −46…−64 %. Found and fixed a released
  bug (junction-v1.20.0): kept History rows lost their icons after a display-scale round trip. Step 17
  gains a framework safety net for it and Aperture's library paging bug (pre-existing). Guideline
  notes: per-row state goes in a value item from a lazy view, not the shape; keep the model objects
  between builds; display-scale glyphs belong in the shape.
- 2026-10-12: step 15 landed (bb7a652..a0d5822): rebuild-made name/description/expanded/role changes
  told; AT-SPI child order right in a caching client (10/10 live). Change 4 (skip the bounds refresh
  when nothing moved) reverted by the orchestrator (d38564a): it cost every element 16 B and slowed
  a11y/create 5 % for a case this round already removed. Two possible new events queued as round 4
  questions (AT-SPI reorder, IsEnabled through a rebuild).
- 2026-10-12: the user started the suggested task for the FakeGitLabHandler race in a separate
  session (step 17 leaves it alone) and asked to pause after round 3 is merged and released.
- 2026-10-12: step 16 landed (e5aed2e..22189da): colour emoji on Linux and VS16 symbols on Windows.
  Guideline note: a per-character check in the itemizer must be gated by a vectorized scan.
- 2026-10-13: step 17 landed (d2630a5..fc05131): toast replacement opacity, custom-frame activation,
  Junction remote-op test settle, item rows remade on a display-scale change, Aperture library paging.
  Round 3 build complete; full suite next.
- 2026-10-13: full suite on the branch passed (30 projects, 0 failures; Junction 969). origin/main had
  moved (8524328: the user's separate session fixed the GitLab fake race), so the merge was made on a
  detached origin/main (96ba3ec), built and the full suite run again on it (30 projects, 0 failures),
  pushed 8524328..96ba3ec, primary fast-forwarded behind the guard. CI run 38322327278 started.
- 2026-10-13: CI 38322327278 red on test-linux only: `SpannedLineSubBlobTests.TheCorpus_HasRunsInXOrder_
  AndRunsWhoseXGoesBack` ("122 runs in X order, 0 going back") — the runner has no Devanagari font, so
  no run goes back; a test precondition, not a flake or a code bug (no re-run). Fixed 187dbe1
  (`[DevanagariFontsFact]`, as `[HanFontsFact]`), pushed 96ba3ec..187dbe1 (fast-forward), primary
  fast-forwarded. CI run 38323118109 started.
- 2026-10-13: CI 38323118109: test-linux green (the font fix worked); the Windows job failed once on
  `Junction.Tests.DiffItemLabelTests.TheLabelsAreMadeOnce` (label remade between two reads — likely
  Junction's watcher reloading after its own git writes, see junction-watcher-self-reload); re-run once
  per the brief: attempt 2 green on all three jobs. Tagged 187dbe1 and pushed 3 + 2 tags:
  junction-v1.21.0, vesper-v1.21.0, meridian-v1.4.0, aperture-v1.20.0, reel-v1.5.0; installer runs
  38324218701, 38324219011, 38324219212, 38324271540, 38324271987.
- 2026-10-13: Junction, Vesper, Aperture and Reel released with installers (Junction-Setup-1.21.0.exe,
  VesperCommander-Setup-1.21.0.exe, Aperture-Setup-1.20.0.exe, Reel-Setup-1.5.0.exe). Meridian's run
  failed test-linux on `SpannedLineSubBlobTests.ANewSubBlob_AllocatesNothing` (40.4 B a row against a
  bound of 40: a single-shot read caught a retired ~8 KB allocation context) — re-run once (attempt 2),
  and the bound fixed to 120 on the branch (beed97f; the no-presize mutation still fails at 279 B),
  pushed 187dbe1..beed97f to main (fast-forward), CI on it started.
- 2026-10-13: Meridian's installer re-run green; Meridian-Setup-1.4.0.exe published. CI 38326831433
  on beed97f green on all three jobs. Round 3 closed: Round 4, paused before its survey at the user's
  request.
- 2026-10-13: resumed; the user approved both queued a11y events (AT-SPI reorder signal; IsEnabled
  changed by a rebuild). Round 4 survey started, eight experts.
- 2026-10-13: round 4 surveys in (all eight not satisfied); reports in `17-reports/r4-*`. One review
  objection each from 4 (lost wake since the infinite idle wait) and 6 (Vesper grid resize rebuilds);
  round 3 confirmed in the real apps. 14 build steps, 4 questions; survey worktrees removed.
- 2026-10-13: the user took every recommendation on the 4 round 4 questions (mono-ASCII fits yes;
  estimated extents not now; foreground UiTests opt-in; wake-latency watch item closed). Step 1 landed
  (671cf74, e645fd2, 421ba2f): lost wake fixed, frame-pacing test at 200 Hz, TextBench warm-up.
- 2026-10-13: step 2 landed (5250e1f..2629cd1): a key press draws one frame, not two (−40…−47 % frame
  time per press), no-op wheels draw nothing, Vesper idle moves draw nothing; `OnPassive(handler,
  listening)` public. Guideline notes: a mostly-idle move handler goes in OnPassive with a cached
  listening delegate; any ordinary handler costs a full frame each run (incl. a root KeyDown/PointerUp
  ignoring most input, any KeyUp); a control callback that changes something visible must mark dirty.
- 2026-10-24: step 3 landed after the weekly reset (9fd01e26, d942d16d, 6a10a76f, c88af01f): button
  enabled state and AT-SPI reorders told; status subscribed before read; UIA reads from the node (0 visits).
  Windows suites and WSL pass.
- 2026-10-24: step 4 landed (08ae00cd..5e6ef9a2): rich text caret moves patch the a11y tree (−99 % per
  key), share text with a reader, read-only reaches readers, and the TextArea text pattern (no
  whole-text Value per keystroke). All suites and WSL pass.
- 2026-10-24: step 5 landed (79a34897..7cba3678): Junction keeps its chrome in parts (−56…−73 % per key
  headless, AOT History −54 %), Tags rebuilds once, review caret moves keep the rows (−84 %), a reconcile
  puts a kept host back unmarked (framework), fixtures quiet git. 0 px on 21 screens; all suites pass.
  Guideline notes: a child component's host is a flex item of its own (give it the root's FlexShrink/
  FlexGrow); kept hosts need keyed parents; persistent parts need a signal for the plain state they show,
  with "rebuild the chrome" the default and view-only paths opting out.
- 2026-10-24: step 6 landed (f9980647..1b11aeda): a Vulkan video frame allocates 96 B, not 454 (no
  finalizable object per frame); video keeps its rate inside the modal move/size loop (120 fps clip: drops
  49–115 → 0–3); a drag keeps the F16 offscreen (81 → 5 made, step −20 %). Focus guard 0 events over 36
  launches; validation clean. Guideline note: a GPU producer raises repaints off the UI thread through
  `IVideoSink.RequestRepaint`, not its own post, so it keeps its rate inside the modal loop.
- 2026-10-24: step 7 landed (c34d7668..418f25a6): scrubber hover −94.6 % (card moved in place through
  Refs), `VideoPlayer.RefreshPreview()` (Reel 120 → 0 rebuilds per file; the card now appears under a
  resting pointer), Computed notifies without allocating; Reel's next-file stills bug fixed (every
  released Reel). Guideline notes: a value that follows the pointer is written through Refs, build only
  when an element appears or leaves; clear Refs when a build stops making their element; call
  `RefreshPreview()` when a late still lands. Build trap: restoring a file with PowerShell `Copy-Item`
  keeps its old mtime and MSBuild skips the rebuild — restore with `git show >`/`cp`, then `touch`.
- 2026-10-24: step 8 landed (a4a6f4a7..a476866c): wrapped resize −98 %, long document open −73 %, 20,000-line
  log open 65.9 → 6.7 MB; paragraphs −7…−27 %; monospace JSON opens −91 % (approved fits-by-count). 21
  scenes pixel-identical (system fonts and DejaVu, A/A control); WSL Fedora passes. Headless only (the
  desktop was in use). Guideline notes: use `FontFamilies.Monospace` for logs, JSON and code in a
  TextArea; a range-overload `IRenderContext` answer is only good until the next text call. Traps:
  filtered default-JIT runs show +6–12 % on barely touched rows (recheck with
  `DOTNET_TC_CallCountingDelayMs=0`, PGO off or AOT); the shaper's float pen drifts over ~9,000
  characters, so proofs shape long strings in pieces.
- 2026-10-24: step 9 landed (a709880f, b52ca178): variable fonts (Cascadia, Segoe UI Variable, Noto Sans on
  Linux) draw sub-blobs: terminal coloured screen −60 %, selectable viewer −48 %, JSON −10…−15 %, first
  draw −5…−9 %. All Windows suites and WSL (10,358) pass. Guideline notes: colour spans in any outline
  face draw only what each stretch reaches; colour-emoji and bitmap-strike faces (Calibri, Courier New
  Regular) still draw the whole run per stretch — avoid them for coloured code, logs and terminals. A
  Windows pixel A/B needs an A/A control or a raised glyph cache (DirectWrite remakes masks by history).
- 2026-10-24: step 10 landed (1207c8b3..0ea667cf): terminal frames allocate nothing (6 KB → 0), the rich
  text editor's last render allocation gone, Aperture's hero scrim seam fixed, long menus draw only visible
  rows (−8…−13 %). 20 frames identical (A/A identical); Windows suites and WSL (10,365) pass. Guideline
  notes: no new strings in Render — keep the last drawn string and compare against the builder; compare
  values before making a "where am I drawn" record for a bridge thread; for hundreds of choices use a
  filtering list (palette, VirtualList), not a menu; a scrim ending inside a picture fades to 0 %. Test
  traps: an overlay that doesn't fit moves to y = 0, so aim at the popup's `WindowBorderBox`; a stub
  subclassing `NullRenderContext` must re-declare `IRenderContext` for a default-member override to apply.
- 2026-10-24: step 11 landed (a1097a38, dddaac36, b099714b): with a reader, a leaf's value, toggle, range or
  name patches one node (TextInput −97 %, checkbox −99 %, label −79 %; Reel playback a11y bytes −57 %);
  overlays opening/closing reach readers (regression caught in-step, older gap fixed). Streams identical
  in 29/30 scripts plus FlaUI and live AT-SPI; all suites, guarded UiTests and WSL (10,395) pass.
  Guideline notes: a leaf's own value/toggle/range/name change costs ~1 KB and no tree with a reader; a
  name change under a button or tab with no `AccessibleName` rebuilds the whole tree (give such buttons
  an AccessibleName); a text that changes often marks layout and rebuilds once per refresh.
- 2026-10-24: step 12 landed (a3aeabb2, fbec1130): themed controls keep layout across a no-op patch (Junction
  Commit 17.7 → 1.1 laid out per key), a rebuild restyles only what it patched (−81…−87 % resolves on
  Junction walks). Audits clean, 51 frames identical, full solution and WSL pass. Guideline notes: a
  control whose `ApplyTheme` writes its own padding/border/size compares and marks layout on change (and
  with `PatchThemesTwin` touches only itself and its children); writing a part's inherited style from
  `ApplyTheme` needs `part.MarkStyleDirty()` unless it depends only on the theme; a write through a `Ref`
  into kept rows, persistent child components or a control's parts needs `MarkStyleDirty()`. Process: the
  agent wrote to two temporary sibling worktrees with shell commands before a session hook stopped
  Edit/Write there; both removed (checked with `git worktree list`), all later work in this worktree.
- 2026-10-24: step 13 landed (572002ab, 598441d4, e6a432fa): `VirtualListElement.InvalidateRow(int)`;
  Vesper's partner walk −40 %, Brief key −69 %, grid resize steps −95…−98 % (d3d81ee's cost gone, its fix
  kept). 39 frames identical; all suites and WSL pass. Guideline notes: a second highlight that moves
  uses `InvalidateRow(old/new)`, not the shape; handing back live plain elements needs every sibling
  keyed, reuse only from the build just before, a `Ref` per element (it follows a patch to the live one),
  and everything shown in the comparison; window-sized builds record what they decided from the size and
  compare that on resize, writing plain sizes onto the live element.
- 2026-10-25: step 14 landed (445b34c8, 33c9ba34, 3200a2a2, c2ea7860): framework placement honours the
  no-activate hint, every real-window UiTest runs guarded (122 pass, 0 hand-backs), foreground tests
  opt-in; plus a run-loop correctness fix (an external WM_CLOSE now ends an idle app; the bug is in
  every app released since aa11334 — candidate for a hotfix release if the user wants one). One ~100 ms
  foreground take by WebView2's DevTools window during the step (now gated). All 14 build steps landed.
  Guideline notes: `ForegroundGuard.Begin()` first in any test that opens a window (`ForSamples()` +
  `LaunchSample()` for sample exes); `SWP_NOACTIVATE` on every Win32 call in a test; windows owned by
  another process can take the foreground, so such tests go behind `HULTUI_UITESTS_FOREGROUND`; pixel
  read-back works from windows kept out of the foreground.
- 2026-10-25: full suite passed on e65e2d23 (30 projects, 12,401 passed, 0 failed). Phase `release`: shared
  origin switched to fredrikhu/hultui; the user chose to merge `ci/github-hosted-runners` first.
- 2026-10-25: round 4 released: merge 643eb07b pushed to fredrikhu/hultui main after the full suite passed
  on the combined tree (CI branch + round 4: 12,401 passed, 0 failed); primary checkout fast-forwarded behind
  the guard; CI run 40147356739 green on hosted runners. Round 5 survey starts. No app releases (not asked);
  candidates for one: the external-WM_CLOSE fix (3200a2a2) and Reel's next-file stills (2bfa69c2).
- 2026-10-25: round 5 survey complete: all eight not satisfied. Objections: 87698f16+e6a432fa restore misses
  all-handed-back containers (6), review composer typing 1,367 KB/char (6), 5e6ef9a2 reader keystroke
  O(rows × length²) (5), ee6d0bb0 public GPU source freezes in size/move (4). A probe (expert 3, F16 kept
  drag targets) coincided with two `nvlddmkm` 153 GPU errors and a > 1 s stall at 02:35; stopped, not
  repeated (the same event appears once on 2026-10-23 with no probe). 14-step build order, 4 questions.
  **Paused at the user's request** for repository work; survey worktrees removed.
- 2026-10-25: resumed after the repository work (history rewrite; this branch rebased, main merged as
  f28e57be; four stale mentions of the round 4 merge remapped to 643eb07b). Round 5 answers: list selection
  highlight yes (opt in), popup a11y events yes, Vesper Detail resize fix yes; TextArea chunked text open
  (the user asked how the public `Text` would look). Step 1 starts.
- 2026-10-25: round 5 step 1 landed (a52de219..a18e70dd): the round 4 objections fixed — kept children come back
  unmarked into an empty container (Vesper Brief with a reader −23 %, 0 trees), one build per find step (−15…−20 %),
  posting GPU sources draw during moves again, TextArea reader typing −56 % with `TextCaretXs`, minimize/raise
  refuse activation. All Windows suites and WSL pass; guarded UiTests 120 pass, 2 Borderless maximize-width failures
  pre-existing at a33bd6b0 (Waiting). Guideline notes: don't let a FindBar's own search call `MarkDirty` too; for every
  caret position on a line call `TextCaretXs` once.
- 2026-10-25: round 5 step 2 landed (0b477882..b23c9251): readers now hear changes inside open overlays (menus,
  dropdowns, dialogs, palette) as patches or overlay-only rebuilds; menu items have real bounds; AT-SPI expandable.
  Palette typing with UIA −32 %. Proof 49/49, FlaUI 24/24, live AT-SPI 39/39, audit 0 stale, pixels identical; all
  suites, guarded UiTests (2 known Borderless failures) and WSL pass. New bug found: the dialog scrim reads as a
  Button named from the dialog's text (Waiting). Guideline note: with a reader, a keystroke in a dialog or palette
  costs in proportion to the overlay — keep overlay content small. **Paused at the user's request** after this step.
- 2026-10-25: round 5 step 3 stopped early at the user's request: glyphs on first read (91d48601: text/wrap −60 %,
  rich text held −57 %) and line scratch (cc6f8467: BiDi wraps −55 %) landed; text-last-line-view saved as a patch.
  WSL and Junction.Tests at cc6f8467 still to run. Guideline notes: glyph arrays now come with the first draw, so a
  scroll into lines laid out ahead pays a few KB per step (a whole session never more); `ShapedRun.Glyphs` must be read
  on the thread that lays the text out. **Paused** (user).
- 2026-10-25: round 5 step 3 finished after the pause: text-last-line-view kept (a1ff84a1: text/wrap −17 %, rich text
  −12 %); WSL 22/22 and the full Windows solution pass for all three step 3 commits; WIP branch deleted. Testing note:
  a bit-compare needs a large mutation to prove coverage (+0.001 on cut-only widths changed 0 of 20,000 layouts, ×1.5
  changed 911); a row whose bytes don't move never ran the change, so a time difference there is noise.

## Log, 2026-10-26 to 2026-10-27

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

## Log, 2026-10-27 to 2026-11-02

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

## Log, 2026-11-02 to 2026-11-03

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

## Log, 2026-11-03 to 2026-11-08

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

## Log, 2026-11-08 to 2026-11-09

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

## Log, 2026-11-09 to 2026-11-11

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

## Log, 2026-11-11 to 2026-11-12

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
