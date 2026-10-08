# Plan 17 ledger

State of [the performance team](17-performance-team.md). The orchestrator updates this
file after every step and commits it with the step's work. Full survey reports are in
[`17-reports/`](17-reports/).

- **Round:** 1
- **Phase:** build
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
6. **a11y-uia-wrapper-rebase** (7; = part of a11y-uia-apply-and-retention (4)) — rebase
   surviving wrappers, drop per-diff index; 34 MB leak after 200 toggles; fixes stale
   sibling navigation.
7. **runtime-frame-floor** (4; + layout-tree-walk-allocs (1) walk/FocusTraversal half,
   layout-hover-common-ancestor (1)) — SlidingWindowMax nodes, PumpEvents delegate, boxed
   Children enumerators in tree walks, FocusTraversal `OrderBy` skip, hover common-ancestor
   HashSet; steady frames to ~0 B, input/tab −80 %.
8. **text-word-alloc-diet** (5) — exact-size glyph arrays, width-only MeasureString, span
   word-cache lookups; 14 rows −10…−50 %.
9. **runtime-text-services-per-window** (4) — dispose or share SkiaTextServices; closes a
   ~29 KB-per-window leak, stops per-window cache duplication.
10. **gl-vk-offscreen-scratch-pool** (3) — unbudgeted F16 offscreen or purge scratch;
    ~−80…−220 MB GPU per Vulkan window after resizes.
11. **a11y-build-diff-garbage** (7) — index loops, lazy child lists, positional diff,
    presized arrays, one slot lookup per list; a11y/edit 627 → ~425 KB/op.
11a. **a11y-scroll-bounds-event** (7; user said yes 2026-10-07) — on a scroll rebuild,
    raise one scroll change on the scroll view (as browsers report a scroll; check
    Chromium's UIA and AT-SPI mapping) instead of a BoundingRectangle change per on-screen
    node (133 per rebuild over 1,000 rows, 3 provider callbacks each).
11b. **a11y-focus-state-on-tab** (7; user said yes 2026-10-07, correctness) — a focus move
    updates HasKeyboardFocus/IsFocused at once (small patch per focus move) instead of
    leaving them stale until the next rebuild.
12. **apps-scenario-virtual-selection-walk-a11y** + **apps-scenario-textarea-highlighted-json**
    (8) — two perf-suite scenarios the big round 2 items need.

**Between items 1 and 2:** render before/after images for Needs-the-user question 1
(flex basis of truncating labels) in a separate worktree, for the user to decide.

### Waiting

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

1. **Flex basis of one-line truncating labels** (layout): use the CSS max-content basis?
   Halves ellipsis measurements (text/ellipsis 1,988 measurements, 7.7 MB per step) but
   changes how shrink is shared between several shrinkable items in a row (pixels move).
   **Open:** the user asked for before/after images (2026-10-07) and leans to a third
   option, **clamp**: keep today's shrink split but take each truncating label's base as
   min(natural width, available width) instead of truncating it. Rendered and measured
   2026-10-08 ([`17-reports/r1-truncation-basis.md`](17-reports/r1-truncation-basis.md)):
   clamp halves ellipsis measurements (time −20…−43 %, alloc −21…−51 %) and moves ordinary
   rows < 1 px; images shown to the user; awaiting the decision. A second question came out
   of it: give one-line truncating labels an automatic minimum of 0 (as CSS does), which
   fixes overlapping text in Junction Branches at ≤1200 px and Meridian's Send button
   pushed off-screen by a long URL.

- **Aperture hero images at 150 %** (painting, from f84893d): the item-page and request
  title-page heroes ask for the layout width at any scale, so at 150 % they draw ~0.7 source
  pixels per screen pixel (soft). Fixing it costs ~5.5 MB per cached hero at a 1400-wide
  window. Fix it?

Provided 2026-10-07: **Aperture API key file** at `C:\Users\fredr\.secrets\jellyfin-api-key.txt`
(outside the repo; never copy or print its contents). Use with the run-hultui-app driver:
`--app aperture --server http://jellyfin.home:8096 --api-key-file <that path>`. Real-library
measurement (poster grid, artwork decode, a11y on a large library) belongs to the app
workloads expert in the round 2 survey, and to aperture-poster-decode-scale's build. The
user should revoke the key when the work is done.

Answered 2026-10-07:

- 2. Bounds events on scroll → **yes**, one scroll change instead: candidate
  a11y-scroll-bounds-event.
- 3. Stale focus state → **yes**, fix it: candidate a11y-focus-state-on-tab.
- 4. Aperture poster decode size → **yes**: candidate aperture-poster-decode-scale.
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
