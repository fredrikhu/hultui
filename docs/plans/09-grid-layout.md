# Plan 09 — Grid Layout (CSS-Grid-style table layouting)

Add a second container algorithm alongside flex: a fixed-template grid. Cells
are placed by `(row, column)` within a grid declared by **track templates**.
Tracks support fixed pixels, percent, `auto` (intrinsic), and `fr` (fractional
free space). No spanning, no auto-flow, no subgrid in the first cut — those are
explicitly deferred.

This is a strict superset of HTML-table tabular layout for the patterns Fredrik
cares about (data grids, property inspectors, settings forms), without
inheriting the HTML auto-table-algorithm complexity.

## Design summary

- New container *mode*, not a new element: `BoxElement` gains `Grid` style
  alongside `Flex` and `Box`. A non-null/non-empty grid template switches the
  container into grid layout. Flex containers are unchanged.
- New `GridStyle` carries `ColumnTemplate`, `RowTemplate`, `RowGap`,
  `ColumnGap`. Children read placement from a new `GridItemStyle` on
  `PositionStyle` (or a new style object — see Step 1).
- Track size kinds: `Fixed(px)`, `Percent(%)`, `Auto`, `Fr(weight)`.
  `MinContent` / `MaxContent` deferred.
- `LayoutEngine` dispatches on container type at the top of `ComputeLayout`:
  if `element.Grid.HasTemplate`, call `LayoutGridChildren`; else
  `LayoutFlexChildren`.
- Measure cache extended so a cell's measurement under
  `(constraintW=trackWidth, ...)` is reusable across the resolve passes.
- Fluent builder: `Grid.Of("200px auto 1fr", "auto auto").Cell(0,0, child)…`
  with terse string templates parsed at element construction time (cheap, only
  on configuration changes — not per frame).

## Out of scope (explicitly deferred, do not add)

- Row/column **spanning** (`row-span`, `col-span`)
- Auto-flow placement (CSS `grid-auto-flow: row|column|dense`)
- `min-content` / `max-content` track sizing
- `minmax()` track sizing
- Subgrid
- Named lines / named areas (`grid-template-areas`)
- Sticky headers / frozen columns (separate feature; needs scroll integration)
- Column resize via drag

If a use case in the sample app demands one of these, log it and revisit; do
not silently expand scope.

## Implementation steps

### Step 1 — Grid types (~80 lines)

New files in `src/HultUI.Core/Layout/`:

- `GridTrack.cs` — `readonly record struct GridTrack(GridTrackKind Kind, float Value)`
  with factory methods `Fixed(px)`, `Percent(p)`, `Auto`, `Fr(weight)`.
- `GridStyle.cs` — `ColumnTemplate: GridTrack[]`, `RowTemplate: GridTrack[]`,
  `RowGap`, `ColumnGap`. `HasTemplate` convenience property. `CopyFrom(...)`
  to mirror the other style objects.
- `GridPlacement.cs` — per-child placement: `int Row`, `int Column`. Default
  `(0,0)`. Lives on a new `GridItemStyle` property on `Element` (mirrors how
  `Flex.AlignSelf` lives on the child not the container).

Add `Grid` property to `Element`, `GridItem` property to `Element`.
Update `Element.PatchFrom` to copy both. Update style copy contracts.

### Step 2 — Template string parser (~60 lines)

`GridTemplate.Parse("200px auto 1fr 2fr")` returns `GridTrack[]`. Parsing rules:

- Whitespace-separated tokens.
- `<n>px` → `Fixed(n)`.
- `<n>%` → `Percent(n)`.
- `<n>fr` → `Fr(n)`. Bare `fr` → `Fr(1)`.
- `auto` → `Auto`.
- Bare number → `Fixed(n)` (so `Grid.Of("200 auto 1fr")` works).

Reject empty/null with a clear exception. Tokenize once at builder time, never
on layout pass. Tested in `GridTemplateTests.cs`.

### Step 3 — `LayoutGridChildren` core algorithm (~250 lines)

Lives in `LayoutEngine.cs`. Mirrors the structure of `LayoutFlexChildren` so
caches/tracing/dirty-flag plumbing stay symmetric. Phases:

1. **Resolve track sizes for a fixed axis** (`ResolveTracks(...)`):
   - Pass 1: assign `Fixed` and `Percent` track widths (closed-form).
   - Pass 2: for `Auto` tracks, measure all cells in that track at
     `(unconstrained, ...)` and take the max border-box width.
     Use `MeasureSize` (already exists, write-free) and the measure cache.
   - Pass 3: distribute remaining space across `Fr` tracks proportional to
     their weight. If no remaining space, `Fr` tracks collapse to 0.
2. **Resolve column widths**, then **row heights** (rows depend on column
   widths because cell heights depend on cell widths).
3. **Position cells**: each cell's content box = track origin + track size,
   minus cell margins. Reuse `ComputeAlignOffset` for cell-level
   `AlignSelf`/`JustifySelf` (Step 5).
4. Call `ComputeLayout(child, constraint, absX, absY, forcedW, forcedH)` once
   per cell — Phase 6 is authoritative (same contract as flex).
5. Return container content size = sum(track sizes) + sum(gaps).

Symmetric `MeasureGridChildrenSize` (write-free) for ancestor Phase 1
measurement. Returns the same content size as the layout pass without writing
`Layout`.

### Step 4 — Dispatch + cache safety (~30 lines)

In `ComputeLayout` and `MeasureSize`, replace the single
`LayoutFlexChildren`/`MeasureFlexChildrenSize` call with a branch:

```csharp
if (element.Grid.HasTemplate)
    contentSize = LayoutGridChildren(...);
else
    contentSize = LayoutFlexChildren(...);
```

Add a sanity test: with `HasTemplate == false`, generated IL/behavior is
identical to before. The `Grid` property is null-friendly (lazy-initialized or
empty by default — pick whichever avoids per-element allocation when grid
is unused).

**Cache invariant:** grid container's measure cache works as-is because
`(constraintW, constraintH, forcedW, forcedH)` already determines track
resolution outcomes. No new cache key needed.

### Step 5 — Cell alignment (~60 lines)

Per-cell `JustifySelf` and `AlignSelf` (override container's
`JustifyItems` / `AlignItems`). Default `Stretch` for both. Extend
`GridItemStyle` to carry these, add fluent helpers.

Container-level `JustifyItems` / `AlignItems` on `GridStyle`.

Reuse `ComputeAlignOffset` from flex (rename if needed for clarity).

### Step 6 — Builder API (~100 lines)

`src/HultUI.Core/Builders/Grid.cs`:

```csharp
public static class Grid
{
    public static BoxElement Of(string columns, string rows = "auto", float gap = 0);
    public static BoxElement Of(GridTrack[] columns, GridTrack[] rows, float gap = 0);
}
```

Returns a `BoxElement` with `Grid` populated. `Cell(int row, int col, Element child)`
helper as an extension method that sets `child.GridItem.Row/Column` then calls
`AddChild(child)`.

Plus `ElementExtensions` additions: `.GridRow(int)`, `.GridColumn(int)`,
`.JustifySelf(...)`, `.AlignSelf(...)` (already exists for flex but the grid
context reuses it), `.RowGap`, `.ColumnGap`.

### Step 7 — Tests (~400 lines)

New `tests/HultUI.Core.Tests/Layout/GridLayoutTests.cs`. Coverage targets:

- Fixed columns: cells receive correct widths and x-positions.
- Percent columns resolve against container content width.
- `Fr` distribution: 1fr/2fr split divides remaining space 1:2.
- `Auto` columns measure to natural content width and are stable across passes.
- Mixed `200px auto 1fr` template.
- Row resolution after column resolution (cell text wraps, row height grows).
- Gaps applied between tracks (not at edges).
- Container content size returned correctly for ancestor measurement.
- Cell-level `JustifySelf` / `AlignSelf` overrides.
- `MarkLayoutDirty` correctly invalidates measure cache for `Auto` tracks
  when a cell's content changes.
- Negative: parsing errors throw with useful messages.
- Cache: a clean tree on the second layout pass produces zero re-measurement
  (assert via a counting `IMeasureContext`).

Add `GridTemplateTests.cs` for parser unit tests.

### Step 8 — Documentation

New `docs/GRID.md`, written in the same voice/structure as `docs/LAYOUT.md`:

1. Quick example
2. Track templates (string syntax + array form)
3. Track kinds: `Fixed`, `Percent`, `Auto`, `Fr`
4. Placing cells (`Row`, `Column`, `Cell(...)`)
5. Gaps
6. Cell alignment (`JustifySelf`, `AlignSelf`)
7. Auto-track caveats (perf cost, virtualization incompatibility)
8. What's not supported yet (link back to "Out of scope")
9. Putting it together (settings dialog example)

Cross-link from `docs/LAYOUT.md` ("for tabular layouts, see GRID.md").

### Step 9 — Sample project

`samples/HultUI.Samples.Grid/` based on the `VirtualList` sample skeleton.
Showcase three real layouts:

1. **Property inspector** — two-column `auto 1fr` form with labels and inputs.
2. **Settings dialog** — three-column `auto 1fr auto` (label, control, hint).
3. **Tabular data** — fixed-width columns with header row, 20 data rows.

Add the project to `HultUI.slnx`.

## Verification gates

After each step:

- `dotnet build` succeeds.
- All existing tests pass (no regression in flex layout).

After Step 7:

- New grid tests all pass.
- Run the layout perf benchmark (if one exists; otherwise add a small one
  comparing 100-cell flex vs 100-cell grid layout times).

After Step 9:

- Sample app launches, all three layouts render correctly under window resize.
- Manual smoke: drag the window narrow enough that columns must shrink — `Fr`
  tracks should give space first, `Auto` should stay at content size.

## Open questions to resolve as we go

1. **Where does `GridPlacement` live?** New `GridItemStyle` on `Element`, or
   piggyback on `PositionStyle`? Probably new property — `PositionStyle` is
   about absolute positioning, conceptually separate.
2. **Default placement when `Row`/`Column` are unset?** Spec-style would be
   auto-placement (row-major fill). For Step 1 we'll require explicit
   placement and treat unset = `(0,0)` as a likely user error caught by a
   warning in dev builds.
3. **Implicit row creation:** if `Row >= RowTemplate.Length`, do we extend
   with an implicit `Auto` row (CSS `grid-auto-rows`)? Lean yes, default to
   `Auto`. Decide during Step 3.
4. **`Fr` minimum size:** CSS treats `1fr` as `minmax(auto, 1fr)` so content
   can push `fr` tracks larger than the proportional share. This is a known
   sharp edge — for v1, treat `1fr` as pure proportional (can shrink to 0).
   Document the divergence.
