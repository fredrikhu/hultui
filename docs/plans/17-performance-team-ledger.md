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

1. **gl-vk-swapchain-rebuild-leak** (3) — F16 Vulkan swapchain leaks ~52 MB VRAM per
   resize (MAILBOX/IMMEDIATE); destroy old before create. Re-run the resize-stretch check.
2. **gl-no-depth-buffer** (3) — `SDL_GL_DEPTH_SIZE` 0; −15 % GPU memory per GL window.
3. **paint-text-zero-alloc** (2; = runtime-text-draw-enumerators (4),
   runtime-drawtext-boxed-enumerators (8), paint-text-draw-enumerators (1)) — index loops in
   text draw, `PrefixMeasurer` closure, pooled corner `SKPath`; ~45 rows −10…−94 % allocKB.
4. **runtime-aot-trace-type-name** (4) — trace args only while recording; Junction AOT
   frame −40 % bytes.
5. **paint-image-zero-copy** (2) — `SetImmutable` before `SKImage.FromBitmap`; −50 % decoded
   image memory (Aperture up to ~375 MB, Vesper ~50 MB).
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
12. **apps-scenario-virtual-selection-walk-a11y** + **apps-scenario-textarea-highlighted-json**
    (8) — two perf-suite scenarios the big round 2 items need.

### Waiting

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
2. **Bounds events on scroll** (a11y): keep one BoundingRectangle change per on-screen node
   per scroll rebuild (133 per rebuild over 1,000 rows), or raise one scroll change on the
   scroll view as browsers do? Changes the UIA event stream.
3. **Stale focus state** (a11y, correctness): a Tab leaves HasKeyboardFocus/IsFocused stale
   until the next rebuild. Fix it at the cost of a small patch per focus move?
4. **Aperture poster decode size** (painting): request posters at card width × display
   scale instead of × 2? −75 % poster memory at 100 % scale (up to ~280 MB), softer
   posters briefly after moving to a higher-DPI monitor.
5. **Should `MarkDirty()` join an open batch?** (reactivity): 3 MarkDirty calls in one
   handler = 3 rebuilds today; joining caps it at 1, but a handler reading the tree right
   after MarkDirty sees the old tree until `FlushNow()`. Changes documented semantics.
6. **Aperture real-library measurement** (apps): provide an API key file, or allow using
   the stored Jellyfin session?

Decided by the orchestrator: idle wake-ups count as a bar (brief updated); a change aimed
at per-frame allocation checks its bytes on AOT too (brief updated).

## Open

None yet.

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
