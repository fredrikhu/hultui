# Round 1 survey: 1. Layout

Measured in worktree agent-a297addc38f0f7204 at d47f737 (counting probes, since reverted).

SATISFIED: no
REVIEW: none (round 1, nothing landed)

## Candidates

- **layout-grid-linear-tracks.** Grid track sizing is O(rows × cells). Bucket cells by row and column in one pass; replace `TrackOrigin`'s per-cell loop with prefix sums.
  - Evidence: auto-track passes rescan every child per track (rows LayoutEngine.Grid.cs:265-270, columns :225-230); `TrackOrigin` (:327) loops to the track index per cell, called at :65 and :123; both in measure and layout passes. grid/resize, grid/edit, grid/create 300x6 (1,800 cells): 1,087,200 child-loop + 547,200 TrackOrigin iterations per step vs 5,101 MeasureSize and 1,802 ComputeLayout. GRID.md §7 claims cells × 1; with implicit auto rows it is rows² × columns. No app hits it yet (one grid per row), but a single form/table grid is the documented use.
  - Expected: ≥30 % lower min on grid resize/edit; scaling bar. Bit-identical results. Follow-up: a grid plan from measure to layout pass would halve the remaining MeasureSize hits.
  - Cost: small; fix GRID.md §7.
- **layout-tree-walk-allocs.** Index loops instead of `foreach` over `IReadOnlyList<Element> Children` (boxes a 40 B enumerator per container); allocate `ReplaceChildren`'s HashSet only when the element already has children.
  - Evidence: boxing walks at StyleResolver.cs:67, Window.cs:2269 (ClearFlags), :2335 (ClearAllFlags), LayoutEngine.cs:1771, LayoutEngine.Baseline.cs:65, FocusTraversal.cs:74, ScrollViewElement.cs:183 (every frame), VirtualListElement.cs:461. component/edit one of 1,000 keyed rows: style 171–235 KB/step, ClearAllFlags 117 KB/step. Element.cs:1139 `new HashSet<Element>(children)` on every `.Children(...)`: ≈550 KB of 6,248 KB/op. theme/light-dark: 15 of ~26 KB/step. input/tab 300 fields: 27.7 KB per Tab; FocusTraversal also `OrderBy` + `ToList` (:65) when every TabIndex is 0.
  - Expected: component/* −10…−13 % allocKB; theme/* ≈ −40 %; input/tab, a11y/tab ≥ −80 % with the OrderBy skip.
  - Cost: small. HashSet half is on the build path (reactivity).
- **layout-ordered-children.** Per-container flag at layout time: in-flow children ordered along the main axis, no sticky/z-index/absolute/transformed child. Then hit-test binary-searches, ZOrderHelper skips its scan, render culling finds the visible range, base `ScrollViewElement.UpdateContentSize` stops re-scanning every frame.
  - Evidence: HitTester.cs:76-77, ZOrderHelper, ElementRenderer.cs:108/:142 visit every child. Pointer move in one row of 1,000: 1,004 hit visits. input/hover 1,000 rows: 993 hit visits + 2,136 z-order scans per step. Checkbox click in 500 rows: 1,494 visits. scroll/wheel 5,000 rows: 4,951 hit + 10,139 z-order + 5,136 render visits (4,974 culled) to draw ~160 elements. Junction Files tree puts every visible row in one ScrollView (JunctionApp.Files.cs:231): up to ~2,000 children per move/frame.
  - Expected: O(n) → O(log n + visible) per event and frame; probably ≥5 % on scroll/wheel and jump.
  - Cost: medium. `Transform` setter must invalidate the parent's flag; hit test assumes `HitTest` overrides stay in the border box. Render-cull half is in the painting expert's file.
- **layout-style-pass-after-patch.** Re-resolve a patched element only when its inherited style or a theme-read property changed; StyleResolver descends where a parent's resolved style changed, instead of `PatchFrom` dirtying the whole subtree.
  - Evidence: `PatchFrom` calls `MarkStyleDirty()` unconditionally (Element.cs:1276); `MarkStyleDirtyDown` (:1006) has no early exit. component/edit 1 of 1,000 rows: 6,003 resolves + 44,004 MarkStyleDirtyDown visits per step to change one label. Same on every Junction whole-app rebuild. 27 element types override `ApplyTheme` and need auditing.
  - Expected: removes nearly all style resolves on rebuild frames; ≥5 % on component/*.
  - Cost: medium; ApplyTheme risk. Overlaps reactivity.
- **layout-hover-common-ancestor.** `PointerState.FindCommonAncestor` allocates a HashSet per hover change (PointerState.cs:71); align depths and walk up together.
  - Evidence: 480 B per hover change at depth 4, ~350 B the set. input/hover 1,000 rows: 0.40 of 0.50 KB input bytes/step.
  - Expected: 480 → ~130 B per hover change.
  - Cost: small.
- **layout-virtuallist-keep-rows.** On owner rebuild, patch unkeyed realized rows by index when the item at that index is the same, instead of re-realizing all visible rows inside render.
  - Evidence: `PatchFrom` → InvalidateItems (VirtualListElement.cs:619-620); only keyed rows reused; Junction History rows unkeyed. 100k VirtualList selection move: 28 template calls, 341 resolves, 200 layout misses, 227 MeasureSize, 86 text measurements, 154 KB per step. TableView 10k resize/sort: 30 rows, 560 resolves, 315 misses, 232 KB. Also `new List<int>()` :516, ReplaceChildren HashSet, boxed enumerators :461.
  - Expected: unsure, ≤5 % of a Junction History move; layout misses ~200 → ~0. Needs a scenario first.
  - Cost: medium; row state survives rebuild by position.
- **paint-text-draw-enumerators** (owner: painting). Text drawing boxes `IReadOnlyList` enumerators per line and run: SkiaRenderContext.cs:526, 537, 565, 624, 702, 810, 1161. Deep-nesting leaf edit: 46.5 KB/frame in render; 5–6 KB on every steady scroll/hover frame. Small.
- **runtime-perf-counter-nodes** (owner: .NET). 8 SlidingWindowMax trackers allocate a LinkedListNode per frame (PerformanceCounters.cs:21-28), DevTools open or not: ~0.4 KB/frame. Small.
- **reconcile-unkeyed-shrink-quadratic** (owner: reactivity; analysis only). `ReconcilePrefixSuffix` removes the unmatched middle one `RemoveChild` at a time, each `List.Remove` scanning from the front (TreeDiffer.cs:303-304, Element.cs:1114). 2,000 → 1,000 rows ≈ 1.5 M Equals. O(m·n) → O(n). Small.

## Examined, not worth it

- MinContentHeight outside the measure cache (LayoutEngine.cs:1293): 720 calls/step, all text cache hits, 7 misses; <5 %.
- layout/resize 2,000-row list: 6,002 misses, 8,001 MeasureSize, 0 text measurements, 0.1 KB. At the semantic minimum.
- Wrap chips, baseline rows, aspect-ratio tiles: 2 / 1,002 / 1,202 misses, ≤0.1 KB. Nothing left.
- Sticky: 3 sorts per step, StickyShift O(depth). Small.
- TableView sort 40 KB + 80 KB per 10k sort: inherent, user-triggered.
- Element size (Box 584 B, Text 656 B): declined under P5c.
- VirtualList wheel realization proportional to new rows.
- layout/shrink 300 overfull rows: 1,483 text measurements/step at ~890 B: count inherent; bytes are the text pipeline's.

## Needs the user

- **Flex base size of a one-line truncating label.** Use the CSS max-content basis instead of "truncated at the available width"? Today every ellipsis label is truncated twice per relayout. text/ellipsis 1,000 labels: 1,988 measurements, 7.7 MB per step. Max-content basis halves it but changes how shrink is shared when a row holds more than one shrinkable item (pixels move).
