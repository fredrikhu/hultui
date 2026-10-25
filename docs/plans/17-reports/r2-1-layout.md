# Round 2 survey: 1. Layout

Probe counters across all 94 rows (JIT) plus AOT for the walk, sticky, grid and theme rows,
at 6ba7e2d; prototypes reverted. Probe patch in the session scratchpad `l1r2/`.

SATISFIED: no

REVIEW: none. 6891682/81be6ca: text/ellipsis does 1 leaf measurement per label per step
(was 2), the pinned entry never thrashes, rows flat. Frame-floor series, bd72d97, b23dcfb: no
new misses or bytes. Junction rebuilds only when a breakpoint flips; Meridian's pane resizes
with flex minimums. Neutral: LayoutEngine.cs:1249 now heap-allocates `floors` for every
shrinking line of > 64 items (no row or app hits it).

## Candidates

- **layout-grid-linear-tracks.** Bucket cells by row and column in one pass; prefix sums instead of `TrackOrigin`'s per-cell loop; optionally a grid plan from measure to layout pass. grid/resize, edit, create (1,800 cells) per step: 1,080,000 row-loop iterations (LayoutEngine.Grid.cs:266-270), 7,200 column-loop (:226-230), 547,200 TrackOrigin (:327-333, called :64-65, :122-123) — ~900 per cell vs 1,802 ComputeLayout, 5,101 MeasureSize, 0 leaf measurements. O(rows² × columns) → O(N); very likely ≥ 5 % on all three rows; grid plan would cut MeasureSize 5,101 → ~2,700. Bit-identical. Small; fix GRID.md §7.
- **layout-zorder-sorted-allocs** (widened: z-order cache). Work out "any z-index or sticky child" and the sorted order during layout (LayoutEngine.cs:78-88 already reads every child's position type), keep it until relayout. scroll/sticky sections wheel: 3 sorts per frame = 1,274–1,322 of 1,550–1,600 B per frame (82 %) from ZOrderHelper.cs:37-53. Children scanned per step (ZOrderHelper.cs:21-30): scroll/wheel 5,000 rows 10,084; jump 5,081; dense text 3,000; overlays ~2,160; hover 2,081; click 2,058; slider 1,091. Junction's ZIndex panels at the root (JunctionApp.PanelShell.cs:40, Dialog.cs:117) sort the root's children every frame and pointer move while open. Sticky wheel 1.55 → ~0.3 KB/frame (−80 %); per-frame/per-event scans go. Small–medium; a live `Position.ZIndex =` would need MarkLayoutDirty (all 18 uses are builder-time).
- **layout-leaves-ignore-offered-height** (new). Leaves whose MeasureContent ignores the offered height should say so (`IgnoresAvailableHeight` default true, VideoElement opts out — the only one that reads it, VideoElement.cs:113). Base Element (empty Box spacers, ~100 `Box.New().FlexGrow(1)` in apps), ChevronElement (ExpanderElement.cs:567), SortMark (TableView.Render.cs:376), Calendar, DatePicker, TimePicker, ContextMenu ignore it but are keyed on exact height (Element.cs:1399; LayoutEngine.cs:829-831), making parents exact-height too. controls/expander toggle: 31 leaf measurements and 188 MeasureSize per animation frame. Prototype: 0 and 55; per step MeasureSize 2,408 → 674 (−72 %), leaf measurements 456 → 30 (−93 %); other rows unchanged. Likely the time bar on that row; vertical resizes, splitter drags and expand animations stop re-measuring chrome rows. Small; identical sizes.
- **layout-ordered-children** (narrowed: hit-test and cull). Per-container "in-flow, ordered" flag at layout time; hit-test binary-searches, render culling finds the visible range, base ScrollView stops re-scanning children every frame (ElementRenderer.cs:92 → ScrollViewElement.cs:178-200, already done in layout at LayoutEngine.cs:174). input/hover 1,000 rows 994 hit visits per move; click in 500 rows 1,493; scroll/wheel 5,000 rows 4,950 hit + 4,974 culled visits per step to draw 162; dense text 2,754 culled per frame; Junction Files tree (JunctionApp.Files.cs:232-233). O(n) → O(log n + visible). Medium; HitTest overrides must stay in the border box; render-cull half in painting's file.
- **layout-applytheme-layout-on-change** (new). Seven controls mark layout on every ApplyTheme (NumericUpDownElement.cs:303, ProgressBarElement.cs:100, SplitterHandleElement.cs:90, ExpanderElement.cs:350, CalendarElement.cs:380, TimePickerElement.cs:492, ToastElement.cs:165); ApplyTheme runs on every resolve incl. every patch. theme/light-dark and named scheme cycle: the whole 8.92 KB/op is NumericUpDown re-measuring value strings + 78 layouts. Prototype: 0.20 KB/op, 0 layouts (−98 %). A patched SplitterHandle (Junction's two splits, JunctionApp.cs:424-426) re-dirties its ancestor chain on every whole-app rebuild. Low priority (theme switches rare). Small. Also the opposite gap (correctness): Checkbox takes `CheckboxSize` from the theme (CheckboxElement.cs:40) and never marks layout.
- **layout-maxlines-setter-dirty** (correctness only). MaxLines and Overflow are auto-properties (TextElement.cs:37-38); no mounted-label setter use today, but round 1 made more cached results depend on MaxLines (6891682, 81be6ca, TextElement.cs:589). Small.

## Examined, not worth it

- **layout-virtuallist-keep-rows (prototyped): not worth it.** Walk allocation 106.7 → 113.2 KB/step (+6 %, TreeDiffer snapshots); saves layout (ComputeLayout 157 → 41) but not style (198 resolves stay — PatchFrom marks style); TableView sort keeps 0 rows; column resize +4 %. The step's bytes are the template: 94.1 of 106.7 KB (88 %) = 19 fresh rows × 4.95 KB; the template must run because rows close over state the list cannot see. Keep-rows saves part of realize's ~10 %: under the bar.
- Moving realization out of render: changes no count or byte.
- Stale rows resolved before being dropped (96 of 198 resolves, 0 B); detaching early would hide them from the a11y build.
- VirtualList realize bookkeeping: 0.6 of 106.7 KB/step; builder `.Children()` HashSets are 8 % of template bytes (supports reconcile-blueprint-hashset).
- Text shaping in realize: 5.85 KB/step for new words — inherent.
- TableView column resize re-templates all 30 rows (232 of 262 KB/step); no app uses TableView.
- GridStyle allocated by `.AlignItems()` on flex containers (ElementExtensions.cs:61): ~1 %.
- FocusTraversal O(n) per Tab, 0 B; hover 0.41 KB = event objects.
- Rows at their minimum: layout/resize 2,000 rows 0 leaf measurements; deep nesting 60 misses for 60 levels; wrap, baseline, aspect, bounded shrink ≤ 12 misses; text/wrap 1 measurement per paragraph.

## Needs the user

1. **An opt-in VirtualList API for rows that depend only on their item and selection** (e.g. `RenderWith((item, selected) => …)` + a flag): the list re-templates only rows whose item or selection changed; anything else a row shows must call `InvalidateRows()`. Walk: template calls 19 → 3 per step, ~107 → ~27 KB/step (−75 %), resolves and layouts ~−80 %. Without it realize cannot shrink below the template's 88 %. Add this public API and have apps (Junction History, Commit, Files; Vesper panes) adopt it one list at a time?
2. **layout-scroll-strip-narrowing** (not perf): scroll containers lay content out against an unbounded main size (LayoutEngine.cs:242-248), so a tab strip cannot both narrow and scroll. The CSS behaviour (shrink to minimum first, then scroll) would change pixels in every scroll view whose content row has shrinkable items. Pursue outside plan 17, or drop?
