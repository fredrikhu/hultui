# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 2
- **Phase:** build — round 2 surveys merged 2026-10-09 (no expert satisfied); building the
  round 2 build order. Round 2 survey started 2026-10-09 after round 1's release (merge 64ba531,
  CI run 37861109308 green: test, test-linux, docs). To resume after a pause, run in this
  worktree: `/loop Run the next step of the performance team: follow
  docs/plans/17-performance-team.md, with state in docs/plans/17-performance-team-ledger.md.
  Read both, do the step the ledger's Phase calls for, record it in the ledger, and commit.`
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`; the session
  moved here from `claude/ui-framework-performance-a2cca6` after a restart)
- **Review commits since:** fc0f4f7 (round 2 reviews all of round 1: fc0f4f7..64ba531)
- **Probe code kept for the build phase** (session scratchpad `probes/`, plus
  `painting-probes.patch`, `PaintProbe.cs`, `probedriver.cs`): a11y hidden-HWND rig,
  reactivity counters and Junction probe, painting probes.

## Verdicts

| Expert | Round | Satisfied | Review objections |
|---|---|---|---|
| 1. Layout | 2 | no | none |
| 2. SkiaSharp: painting | 2 | no | none |
| 3. GL and Vulkan | 2 | no | 8aed816: idle pass not 0 B on a real SDL window (pump closure) |
| 4. .NET runtime | 2 | no | 8aed816 (same); f3a31d6 + 0178784: shared caches bounded by entries, not bytes |
| 5. Text pipeline | 2 | no | none |
| 6. Reactivity and reconcile | 2 | no | none |
| 7. Accessibility bridges | 2 | no | none |
| 8. App workloads | 2 | no | none (round 1 helped every app: −20…−62 % bytes/frame) |

## Queued candidates

Round 1's build order all landed (see Landed). Round 2's surveys (reports `17-reports/r2-*`)
re-ranked everything. A build step may bundle one expert's related small candidates as
separate commits. Items marked **(user)** wait for a decision in Needs the user.

### Round 2 build order

1. ~~**runtime-sdl-pump-closure + runtime-paced-wait-spin**~~ LANDED 0ceecea, ff7a970, 2612de1 (4; = sdl-pump-closure-per-pass (3))
   — `SdlWindowingPlatform.cs:84` allocates 88/72 B per loop pass (idle 5.7 KB/s, animating
   3–4 MB/s); SDL truncates `WaitForEvents(1)` to 0, so the loop spins the last ~1 ms of
   every interval while dirty (41–46k passes/s for 200 frames). Real-window steady frames → 0 B.
2. ~~**vk-minimized-swapchain-lost**~~ LANDED 82e382b, 3766d78 (3, correctness) — a Vulkan window that draws while
   minimized never draws again (orphaned swapchain at SdlVulkanHost.cs:585, OUT_OF_DATE
   forever, later resizes fail with ErrorNativeWindowInUseKHR) and spins ~50k passes/s.
   Destroy on the zero-extent skip, retry at acquire, no spin while a dirty window cannot
   begin a frame.
2b. ~~**media-minimized-playback**~~ FIXED 7e52ff5 (3/8, verification of 82e382b) — the UI thread no longer
   renders mpv while minimized, so mpv may wait its 200 ms render timeout per frame. Check
   Reel (and the VideoPlayer sample) playing a compressed clip while minimized: audio
   continuity, frame drops, A/V sync and resume after restore, CPU; fix if needed.
3. ~~**text-textarea-patch-keeps-lines + text-textarea-span-scan**~~ LANDED 27b8803, 95e93b5, 3a057ea, e7717a0 (5) — a patch with the same
   text re-wraps the whole TextArea (Meridian: 19,013 lines, 1.4 MB per keystroke in its
   editors; 25k unique lines 64 MB); SpansForLine scans from span 0 (JSON wheel 612k
   iterations per step → 117). Watch Meridian's overlapping search spans. Add a suite row
   for a parent rebuild around a large TextArea.
4. ~~**text-shaper-direct**~~ LANDED d049be7, d5176b7 (5) — HarfBuzz called directly, one buffer/font per shaper:
   −25…−50 % allocKB on 6 rows, bit-identical over 1.1 M shaper calls.
5. ~~**text-shape-cache-byte-bound**~~ LANDED 48823d5, 8a91d93 (5, from 4's review) — bound the shape and word caches by
   bytes/glyphs; closing a TextArea keeps 4–155 MB in the app-lifetime caches today.
6. ~~**paint-effect-object-cache + paint-blob-builder-reuse + junction-lane-palette +
   aperture-postercache-trim-thread**~~ LANDED 8d30d31, 103f30e, c85396e, 93b8fef, d27ea6b (2) — shadows/gradients create native objects per
   draw (the last steady-frame bytes in real apps; suite rows → 0); blob builder per miss
   (scroll/jump −39 %); Junction graph palette (History −1.97 KB/frame); Aperture disposes
   posters on the download thread (correctness).
7. ~~**paint-selectable-label-paragraph**~~ LANDED e7a7b03, bf5adcc, fbb5afc, 7755670 (2) — Junction diff and Vesper reader ~10 KB/frame of
   paragraphs → ~0.
7b. ~~**test-alloc-first-run-flake**~~ FIXED da2ccd8, f578b55 (4, test reliability; moved up 2026-10-09) — "allocates
   nothing" tests fail on the first run after a build (three seen: Headless
   `ASteadyRepaintOfSlidersAndNumberFields_AllocatesNothing`, `ASteadyRepaint_AllocatesNothing`
   5.4 B/frame; Skia `Paragraphs_AndTheirSelection_AllocateNothingPerFrame` 22.9 B/frame). Find
   the cause (tier-0/OSR, JIT stack allocation, a one-time lazy init) and fix it for all of
   them before the round's full suite and CI.
8. **layout-grid-linear-tracks + layout-leaves-ignore-offered-height +
   layout-maxlines-setter-dirty + checkbox-size-layout** (1) — grid O(rows² × cols) (~900
   loop iterations per cell); spacers/chevrons keyed on exact height (expander animation
   MeasureSize −72 %, leaf measurements −93 %); two correctness fixes.
9. **a11y-node-extras + a11y-snapshot-stale-fields + a11y-atspi-snapshot-diet** (7) — node
   136 → 80 B (9 of 11 a11y rows −13…−39 %); stale Role/AutomationId on rebased wrappers;
   AT-SPI paths built once (−27…−34 % per Linux a11y frame); plus
   **a11y-focus-unbuilt-node** and **a11y-virtual-item-selected** (approved 2026-10-09).
10. **reconcile-blueprint-hashset** (6) — no HashSet for empty parents (component rows
    −10.7…−11.8 %); **reactivity-tween-tick-per-frame** (approved; Reel 13 rebuilds per
    presented frame, 172 MB per 11 s) with the **apps-scenario-tween-signal-rebuild** suite
    row; **reactivity-scope-threadstatic** (approved, low priority).
11. **layout-zorder-sorted-allocs** (1, widened to a z-order cache) — sticky wheel 1.55 → ~0.3
    KB/frame; no per-frame/per-event child scans; Junction panels sort the root per move.
12. **runtime-palette-filter-allocs + runtime-syntax-registry-first-use** (4) — palette filter
    −33 % on its row (Filter −84…−91 %); Vesper's first code folder loads TextMate on the UI
    thread (10 ms, 5 MB).
13. **text-textarea-measure-lines** (5) — width-only FitsWhole (JSON open −36 %, −75 % with
    item 4); Meridian response frame 25.7 MB. Once it lands, lower `ShapeCacheBytes` to ~8 MB
    and give any new width cache a weight (48823d5).
14. **gl-vk-one-submit-per-frame + gl-vk-resize-duplicate-frames** (3) — Flush is the 2nd
    largest share in every app (14–36 %); 3 → 1 frames per resize step. Needs timing.

15. **a11y-atspi-active-on-status + a11y-value-for-text + a11y-richtext-text-snapshot +
    a11y-range-value-events** (7; approved 2026-10-09) — gate the Linux bridge on
    org.a11y.Status (always-on override); drop whole-text Value changes for text-pattern
    elements (rich text keystroke ~1.5 MB UIA / ~750 KB AT-SPI); lazy rich text tables;
    announce Slider/ProgressBar values (UIA RangeValue, AT-SPI Value).
16. **meridian-response-format-on-ui-thread + syntax-maxlines-uncoloured** (8/5; approved)
    — prettify and tokenize on the request worker (15.3 ms on the UI thread today), then
    colour responses over 20,000 lines.
17. **layout-virtuallist-item-rows** (1; approved) — opt-in `RenderWith((item, selected) =>
    …)` VirtualList API (walk template calls 19 → 3, −75 % bytes), adopted in Junction
    History first, then other lists one at a time.
18. **wsl-mesa-verify** (3/5; approved) — Mesa 26.2.3 (EGL, DRI, Vulkan: Dozen on the RTX 4080
    via D3D12, and llvmpipe) and 48 Noto font packages (Indic, Thai, Lao, Khmer, Myanmar,
    Tibetan, Ethiopic, CJK, colour emoji; 83 → 611 faces) installed in WSL Fedora 2026-10-09:
    check 0f5816a (GL depth 0) and 7a827e9 on Mesa EGL/llvmpipe and lavapipe, and re-run
    d049be7's shaper equivalence on real Indic/Thai/emoji faces on Linux.

### Waiting

- **test-alloc-first-run-flake** (4, test reliability; in step 12) — two "allocates nothing" tests failed once each on the first run right after a build (Headless `ASteadyRepaintOfSlidersAndNumberFields_AllocatesNothing`; Skia `Paragraphs_AndTheirSelection_AllocateNothingPerFrame`, 22.9 B/frame); reruns pass. Likely tier-0/JIT warm-up; would flake CI. Harden the warm-up.
- **text-memo-cache-byte-bound** (5) — BiDi/styled memo caches are not byte-bounded: 15.2 MB after a 5,000-block rich text document is opened twice and resized; `MemoRange` keys keep evicted `TextMemo`s alive. richtext/open reuses ~11 MB of them, so a bound costs re-open speed.
- **paint-drawspannedline-overlap** (2, correctness, latent) — `DrawSpannedLine` (`FirstSpanEndingAfter`) binary-searches on End and assumes spans do not overlap; a multi-line DrawText/paragraph given overlapping spans would skip spans (TextArea is safe: its per-line spans are clipped). Small.
- **mpv-vo-thread-minimized** (3, lead) — mpv's vo thread costs ~125 Mcyc/s at 60 fps whether drawing or skipping; while minimized a lower-cost mode (e.g. pausing video output, `vid=no`-like) might save it. Investigate.
- **test-frame-pacing-event-precondition** (4, test) — UiTests `FramePacingTests.AnEventEveryMillisecond` fails its own precondition (its event thread's `Sleep(1)` takes ~2 ms, so ~510 events < the 606 it requires). Fix the test.
- **runtime-idle-wakeups** (4/3) — 65 wakes/s idle in every app. Medium; after items 1–2
  (same loop code). WinForms host ticks every 8 ms.
- **vk-swapchain-target-per-frame** (3) — Vulkan steady frame 2,658 → ~610 B; changes HDR.md's
  "never cache a swapchain render target" invariant. Medium.
- **vk-purge-disposed-image-textures** (3) — −178 MB DXGI after disposing 8 large photos on
  Vulkan. Small.
- **gpu-aperture-startup-swapchain-x3** (3) — ~10 ms, borderline.
- **paint-spanned-line-glyph-work** (2) — glyphs processed ~16× per spanned line per frame
  (Junction diff 26,210 per frame). (a) small, (b) medium; time unmeasured.
- **text-textarea-incremental-wrap** (5) — keystroke O(lines) → O(edited), −46…−58 % on 3
  rows. Medium.
- **tooling-textbench-warmup** (5) — TextBench measures tier-0 code; the perf suite also tiers
  up during rounds 1–4 (lead for 4). Small.
- **layout-ordered-children** (1, narrowed) — hit-test and cull O(n) → O(log n + visible).
  Medium.
- **layout-applytheme-layout-on-change** (1) — theme switch rows −98 % alloc; patched
  SplitterHandle re-dirties Junction's ancestor chain. Small, low priority.
- **reconcile-restyle-on-change** (6, rank last) — Junction 585 → ~310 resolves; style is
  0.6–1 % of app frames. Text's SelectionColor is overwritten by ApplyTheme (latent bug).
- **a11y-property-patch** (7, large) — after a11y-node-extras; Linux needs snapshot-per-diff.
- **a11y-atspi-snapshot-per-diff** (7) — 496 B/node rebuilt per apply on Linux. Medium.
- **a11y-atspi-virtual-items** (7, correctness) — lists over 2,000 items are empty to Orca.
  Medium; needs a live libatspi check.
- **a11y-atspi-cache-child-index** (7) — unverified; needs a live libatspi client.
- **apps-tooling-generic-framelog** (8, built by 4) — env-gated HULTUI_BENCH/HULTUI_FRAMELOG.
- **Suite scenarios** (8): apps-scenario-rich-row-walk, apps-scenario-document-switch,
  apps-scenario-image-grid, apps-scenario-loop-wakes (real loop), a first-code-icon test.
- **aperture-tmdb-size-fallback**, **aperture-poster-as-hero**, **aperture-hero-scrim-seam**,
  **junction-narrow-gitlab-glyphs**, **meridian-pane-choice-not-persisted**,
  **meridian-min-width-wraps** (apps; mostly not perf).

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

## Needs the user

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
12. (later) no-op glGetError — only if timing shows the round trip costs.

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
