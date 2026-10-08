# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 1
- **Phase:** build — resumed 2026-10-08 22:49 after a pause. Next: item **11f**
  (aperture-hero-scale), then item **12** (two perf-suite scenarios), then the end-of-round
  full test suite (`dotnet test HultUI.slnx -c Release`), then round 2's survey. To resume
  after another pause, run in this worktree: `/loop Run the next step of the performance
  team: follow docs/plans/17-performance-team.md, with state in
  docs/plans/17-performance-team-ledger.md. Read both, do the step the ledger's Phase calls
  for, record it in the ledger, and commit.`
- **Branch:** `claude/happy-wiles-1e3e0e` (worktree `distracted-mendel-5071ba`; the session
  moved here from `claude/ui-framework-performance-a2cca6` after a restart)
- **Review commits since:** fc0f4f7
- **Probe code kept for the build phase** (session scratchpad `probes/`, plus
  `painting-probes.patch`, `PaintProbe.cs`, `probedriver.cs`): a11y hidden-HWND rig,
  reactivity counters and Junction probe, painting probes.

## Verdicts

| Expert | Round | Satisfied | Review objections |
|---|---|---|---|
| 1. Layout | 1 | no | none |
| 2. SkiaSharp: painting | 1 | no | none |
| 3. GL and Vulkan | 1 | no | none |
| 4. .NET runtime | 1 | no | none |
| 5. Text pipeline | 1 | no | none |
| 6. Reactivity and reconcile | 1 | no | none |
| 7. Accessibility bridges | 1 | no | none |
| 8. App workloads | 1 | no | none |

## Queued candidates

Duplicates are folded into one id with one owner; the report each came from is in
brackets. **Round 1 build order** is the numbered list; the rest wait for round 2's
survey to re-rank them.

### Round 1 build order

1. ~~**gl-vk-swapchain-rebuild-leak**~~ LANDED 7a827e9 (3) — F16 Vulkan swapchain leaks ~52 MB VRAM per
   resize (MAILBOX/IMMEDIATE); destroy old before create. Re-run the resize-stretch check.
2. ~~**gl-no-depth-buffer**~~ LANDED 0f5816a (3) — `SDL_GL_DEPTH_SIZE` 0; −15 % GPU memory per GL window.
3. ~~**paint-text-zero-alloc**~~ LANDED 17e3de4 (2; = runtime-text-draw-enumerators (4),
   runtime-drawtext-boxed-enumerators (8), paint-text-draw-enumerators (1)) — index loops in
   text draw, `PrefixMeasurer` closure, pooled corner `SKPath`; ~45 rows −10…−94 % allocKB.
4. ~~**runtime-aot-trace-type-name**~~ LANDED 8ab877c (4) — trace args only while recording; Junction AOT
   frame −40 % bytes.
5. ~~**paint-image-zero-copy**~~ LANDED 12d1480 (2) — `SetImmutable` before `SKImage.FromBitmap`; −50 % decoded
   image memory (Aperture up to ~375 MB, Vesper ~50 MB).
5a. ~~**aperture-poster-decode-scale**~~ LANDED f84893d (2; user said yes 2026-10-07) — request posters at
   card width × display scale instead of × 2; −75 % poster memory at 100 % scale (up to
   ~280 MB), −44 % at 150 %. Refetch at the new scale after a DPI change.
6. ~~**a11y-uia-wrapper-rebase**~~ LANDED 4195da4 + 7096431 (7; = part of a11y-uia-apply-and-retention (4)) — rebase
   surviving wrappers, drop per-diff index; 34 MB leak after 200 toggles; fixes stale
   sibling navigation.
7. ~~**runtime-frame-floor**~~ LANDED 54631d4…65a01a3 (4; + layout-tree-walk-allocs (1) walk/FocusTraversal half,
   layout-hover-common-ancestor (1)) — SlidingWindowMax nodes, PumpEvents delegate, boxed
   Children enumerators in tree walks, FocusTraversal `OrderBy` skip, hover common-ancestor
   HashSet; steady frames to ~0 B, input/tab −80 %.
8. ~~**text-word-alloc-diet**~~ LANDED 0178784 (5) — exact-size glyph arrays, width-only MeasureString, span
   word-cache lookups; 14 rows −10…−50 %.
9. ~~**runtime-text-services-per-window**~~ LANDED f3a31d6 (+e962738, 5eebbf6) (4) — dispose or share SkiaTextServices; closes a
   ~29 KB-per-window leak, stops per-window cache duplication.
10. ~~**gl-vk-offscreen-scratch-pool**~~ LANDED 1785d16 (3) — unbudgeted F16 offscreen or purge scratch;
    ~−80…−220 MB GPU per Vulkan window after resizes.
11. ~~**a11y-build-diff-garbage**~~ LANDED e3cff87 (7) — index loops, lazy child lists, positional diff,
    presized arrays, one slot lookup per list; a11y/edit 627 → ~425 KB/op.
11a. ~~**a11y-scroll-bounds-event**~~ LANDED ab8917b (7; user said yes 2026-10-07) — on a scroll rebuild,
    raise one scroll change on the scroll view (as browsers report a scroll; check
    Chromium's UIA and AT-SPI mapping) instead of a BoundingRectangle change per on-screen
    node (133 per rebuild over 1,000 rows, 3 provider callbacks each).
11b. ~~**a11y-focus-state-on-tab**~~ LANDED 70d68c1 (7; user said yes 2026-10-07, correctness) — a focus move
    updates HasKeyboardFocus/IsFocused at once (small patch per focus move) instead of
    leaving them stale until the next rebuild.
11c. ~~**a11y-uia-reparent-wrapper**~~ LANDED 4c3e3b1 (7, correctness, from 4195da4) — when an ElementId is in
    both diff.Added and diff.Removed (an element moves to another a11y parent, e.g. a Box
    gains an AccessibleName), the bridge processes Added before Removed and drops the
    wrapper: UIA navigation to those children returns null. Process Removed first. Small.
11d. ~~**layout-truncation-clamp-basis**~~ LANDED 6891682 (1; user chose clamp 2026-10-08) — a one-line
    truncating label's flex base = min(natural single-line width, available width) from the
    cached natural width, instead of truncating it to get that number. Prototype: ellipsis
    rows halve their measurements, time −20…−43 %, alloc −21…−51 %; ordinary rows move by
    the ellipsis slack (< 1 px, at most one character). See `17-reports/r1-truncation-basis.md`.
11e. ~~**layout-truncating-min-zero**~~ LANDED 322df92 (1; user said yes 2026-10-08, correctness) — a one-line
    truncating label's automatic minimum is 0, as CSS gives `overflow: hidden` items, instead
    of its longest word. Fixes Junction Branches overlap at ≤1200 px and Meridian's Send
    button pushed off-screen by a long URL. Build after 11d, as its own commit, with
    before/after images of those screens for the user.
11e-fix. ~~**apps-truncation-followups**~~ LANDED be1fd9d, 351e6cd (1, from 322df92) — Meridian: request tab titles and
    sub-tab labels need MaxLines(1) + ellipsis (they wrap and overlap the URL bar at ≤1100 px
    now that the editor gets its designed width). Junction: the FlexShrink(0) "current" tag
    overlaps LAST COMMIT by 23 px at 1000 px. Before/after images for the user.
11f. **aperture-hero-scale** (2; user said yes 2026-10-08) — the item-page and request
    title-page heroes ask for the layout width at any scale (~0.7 source px per screen px at
    150 %, soft). Request layout width × display scale like f84893d; ~5.5 MB more per cached
    hero at a 1400-wide window. Show before/after crops at 150 %.
12. **apps-scenario-virtual-selection-walk-a11y** + **apps-scenario-textarea-highlighted-json**
    (8) — two perf-suite scenarios the big round 2 items need.

**Between items 1 and 2:** render before/after images for Needs-the-user question 1
(flex basis of truncating labels) in a separate worktree, for the user to decide.

### Waiting

- **layout-scroll-strip-narrowing** (1, framework gap) — a tab strip cannot both narrow its
  tabs and scroll, because scroll content is laid out against an unbounded width; Meridian's
  open tab can be cut off at the strip's end.
- **layout-maxlines-setter-dirty** (1, correctness) — the MaxLines property setter does not
  mark layout dirty (a patch does). Small.
- **a11y-atspi-cache-child-index** (7) — from source reading: libatspi's AddAccessible writes
  into the parent's cached child list by index (overwrites, not shifts); with one
  children-changed per parent, inserting several siblings at once may leave stale entries.
  Verify with a live libatspi client first.
- **a11y-focus-unbuilt-node** (7, correctness) — focus moved onto an element mounted in the
  same handler (no node yet) raises no UIA focus event.
- **a11y-scrollpercent-role** (7) — a ScrollViewElement given a role other than ScrollView or
  List raises ScrollPercent without a Scroll pattern. Small.
- **tooling-textbench-warmup** (5) — TextBench filtered to a few rows measures tier-0 code
  (3.6× slower, inflated differences) unless `DOTNET_TC_CallCountingDelayMs=0`; even full runs
  showed a false +11 %. Warm up by time as the perf suite does. Small.
- **layout-zorder-sorted-allocs** (1) — `ZOrderHelper.Sorted` allocates int[], a closure, a
  Comparison and an Element[] per call for containers with sticky or z-index children:
  ~1.4 KB/frame on scroll/sticky sections wheel. Small.
- **reconcile-array-enumerator-boxing** (6) — boxed `SZGenericArrayEnumerator<Element>` on the
  create and build path. Small; fold into reconcile-blueprint-hashset.
- **a11y-snapshot-stale-fields** (7) — IsOffscreen, off-screen bounds, Role, AutomationId
  update only when the diff names the node, so IsOffscreen can go stale after a viewport
  resize. Seen in code only; test first.
- **paint-cover-half-pixel-crop** (2) — at 1:1, Cover crops at half-pixel offsets when a
  source is 1 px larger on the cropped axis (`ComputeImageFit` does not snap the source
  rectangle): slightly soft pictures. Small.
- **aperture-postercache-trim-thread** (2, app correctness) — `PosterCache.Trim` disposes
  images on the download thread while the UI may draw them. Pre-existing.
- **gl-image-retained-after-dispose** (2, lead for 3) — in a GL window, drawing 281 MB of
  photos adds ~380 MB of private bytes beyond the pixels, and ~375 MB stays after every
  source is disposed (437 vs 62 MB at start); before and after 12d1480 alike. Suspects:
  Skia's GPU resource cache, its CPU mip build, NVIDIA system-memory copies. Measure first.
- **layout-virtuallist-realize-in-render** (8; + layout-virtuallist-keep-rows (1)) —
  realize is 12–19 % of key-walk frames, 84 % of a Vesper folder open (5.2 MB); patch rows
  whose item is unchanged. Medium–large. Needs scenario 12 first.
- **a11y-eager-virtual-items** (8) — a11y 30 % of Junction History walk, 55 % of Vesper
  PageDown; on-demand item provisioning below 2,000 or diff item nodes by index. Medium.
- **text-textarea-measure-lines** (5; + text-textarea-short-lines-open (8),
  textarea-render-substrings (2)) — width-only FitsWhole, keep visible line strings, skip
  shaping provably short lines; Meridian JSON open 30 MB / 24k calls. Medium.
- **text-textarea-incremental-wrap** (5) — re-wrap only edited lines; keystroke O(lines) →
  O(edited). Medium.
- **text-textarea-span-scan** (5) — binary search spans per line; 2.28 M iterations per
  frame on a 45k-span file. Small.
- **text-shaper-reuse** (5; = runtime-shaper-allocs (4), text-shaper-per-call-objects (2))
  — drop SKPaint, one SKFont, reuse HarfBuzz buffer; −11 % cold open, −7 % text/create.
  Small.
- **gl-vk-one-submit-per-frame** (3) — one submit per frame, no in-frame glGetError; Flush
  is 19–28 % of frames. Small; needs timing.
- **gl-vk-resize-duplicate-frames** (3) — skip same-size live-resize frames; 2.4–2.9 → ~1
  frame per step. Small.
- **gpu-aperture-startup-swapchain-x3** (8) — settle size before the first swapchain;
  ~10 ms. Borderline.
- **runtime-idle-wakeups** (3, 4) — deadline-based wait instead of 16 ms polling; ~65 →
  ~0 wakes/s idle. Medium (explicit wakes for AT attach, action router, trailing a11y).
- **reactivity-tween-rebuild-per-loop-iteration** (8) — tick animations once per frame;
  Reel 22–62 rebuilds between frames during fades. Small–medium.
- **layout-grid-linear-tracks** (1) — O(rows × cells) → linear; ≥30 % on grid rows. Small.
- **layout-ordered-children** (1) — binary-search hit-test, z-order skip, visible-range
  cull for ordered containers; O(n) → O(log n + visible) per event/frame. Medium.
- **reconcile-restyle-on-change** (6; = layout-style-pass-after-patch (1)) — patch/mount
  dirty style only when inherited style changed; Junction 587 → ~0 resolves per press.
  Medium.
- **reactivity-scope-threadstatic** (6) — `[ThreadStatic]` subscriber; −30 % on signal
  fan-out. Small; update DECISIONS.md:97.
- **reconcile-persistent-child-in-place** (6) — keep field-held child hosts in place; one
  O(N) a11y rebuild less per parent rebuild. Medium.
- **reconcile-blueprint-hashset** (6; = layout-tree-walk-allocs HashSet half (1)) — no
  HashSet when the element has no children; ≈ −9 % on component rows. Small.
- **reconcile-unkeyed-shrink-linear** (6, 1) — O(k·n) → O(n). Small, low priority.
- **paint-selectable-label-paragraph** (2) — keep selectable label paragraphs between
  frames; Junction diff 10.7 → 0.8 KB/frame. Small–medium.
- **paint-spanned-line-glyph-work** (2) — identity-first blob lookup; optional per-stretch
  sub-blobs. Small / medium.
- **a11y-property-patch** (7) — patch only notified nodes; O(n) → O(changed) per
  keystroke/toggle/value tick. Large.
- **a11y-richtext-text-snapshot** (7) — lazy AccessibleText; a11y/type rich text 2,135 →
  ~100–600 KB. Medium.
- **a11y-atspi-snapshot-per-diff** (7) — reuse untouched AT-SPI objects; Linux only.
  Medium.
- **apps-tooling-generic-framelog** (8) — env-gated framework frame log for every app.
  Small.
- **apps-junction-heap-growth** (8) — GC heap 41 → 75 MB over 30 s; needs a heap census
  first.
- **junction-lane-palette** (2; = runtime-junction-graph-allocs (4)) — app: cache the lane
  palette, index loops; History frame −13.9 KB. Small; guideline example.

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

## Needs the user

- **Meridian response-pane breakpoint** (from be1fd9d): at 1100 px the editor gets
  1100 − 48 − 264 − 560 = 228 px, so the URL is almost invisible beside Send. Move the
  side-pane breakpoint from 1100 to ~1320 px (editor ≥ 448 px; below it the pane goes to the
  bottom)? The plan's approved decision says "auto-bottom < 1100dip".
- **Junction Branches compact table at ≤1000 px** (from 351e6cd): with the sidebar open the
  fixed columns (118 + 120 + 96 + 250) leave 86 px for name + LAST COMMIT, so names show
  as "…". Add a compact layout (e.g. icon-only Checkout, narrower columns)?

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
