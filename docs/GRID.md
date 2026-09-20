# Grid Layout Guide

HultUI ships a fixed-template grid layout alongside flexbox. Use it for
**tabular** UI: data grids, property inspectors, settings forms — anything
where cells in different rows must align to the same column boundaries.

For flow layouts (toolbars, sidebars, wrapping cards) prefer flexbox — see
[`LAYOUT.md`](LAYOUT.md).

```csharp
using HultUI.Core.Builders;    // Grid, Box, Text, ElementExtensions
using HultUI.Core.Layout;      // GridTrack, JustifyItems, JustifySelf,
                                // AlignItems, AlignSelf
using HultUI.Core.Primitives;  // Dimension, Color, Thickness
```

A grid container is a `BoxElement` with declared **column** and **row**
templates. Every child gets placed by `(row, column)`. Tracks come in four
kinds: fixed pixels, percent, intrinsic (`auto`), and proportional (`fr`).

---

## 1. Quick Example

```csharp
var inspector = Grid.Of("auto 1fr", gap: 8).Children(
    Text.New("Name:").GridCell(0, 0),
    TextInput.New().GridCell(0, 1),

    Text.New("Email:").GridCell(1, 0),
    TextInput.New().GridCell(1, 1)
);
```

The `auto` column shrinks to fit the longest label; the `1fr` column expands
to take all remaining space. Both rows align to the same column boundary —
the kind of alignment flexbox can only fake with hard-coded label widths.

---

## 2. Track Templates

Tracks are declared at construction time using a CSS-grid-style string.

```csharp
Grid.Of("200px auto 1fr")             // 3 columns, default 1 row
Grid.Of("auto 1fr", "auto auto auto") // 2 cols × 3 rows
Grid.Of("100 100 100", "50", gap: 4)  // bare numbers = pixels
```

Or pass `GridTrack[]` directly when building templates programmatically:

```csharp
Grid.Of(
    columns: [GridTrack.Fixed(200), GridTrack.Auto, GridTrack.Fr(1)],
    rows:    [GridTrack.Auto, GridTrack.Auto],
    gap:     8);
```

---

## 3. Track Kinds

| Kind | String | Meaning |
|---|---|---|
| **Fixed** | `100px` or bare `100` | Exactly that many pixels. |
| **Percent** | `25%` | Fraction of the container's content area. |
| **Auto** | `auto` | Sized to the largest cell's natural content. |
| **Fr** | `1fr`, `2fr`, bare `fr` | Proportional share of *remaining* space after fixed/percent/auto resolve. |

Resolution order: **Fixed and Percent first**, then **Auto** (cells get
measured), then **Fr** distributes whatever's left.

```csharp
// 200px label column + 1fr value column + 80px action column
Grid.Of("200px 1fr 80px")
```

**Fr behaviour quirk:** `1fr` here is pure proportional — if the container
has no remaining space, the `fr` track collapses to 0. CSS would treat `1fr`
as `minmax(auto, 1fr)`; we don't yet. Workaround: use `auto` for tracks that
must keep their content visible.

---

## 4. Placing Cells

Each child carries a `GridItem` style with `Row` and `Column` indices
(zero-based, starting from the top-left). Use the fluent helpers:

```csharp
Box.New().GridCell(0, 0)              // row 0, column 0
Box.New().GridRow(2).GridColumn(1)    // row 2, column 1
```

Or set them directly:

```csharp
var cell = Box.New();
cell.GridItem.Row = 1;
cell.GridItem.Column = 0;
```

Cells placed beyond the declared template extend the grid with **implicit
tracks**. By default these are `auto` (intrinsic-sized). Override via
`Grid.ImplicitRow` / `Grid.ImplicitColumn`:

```csharp
var grid = Grid.Of("100px 100px");
grid.Grid.ImplicitRow = GridTrack.Fixed(40);  // every implicit row = 40px
```

Spanning, named lines, and auto-flow placement are not yet supported (see
**§ 8 — Not yet supported**).

---

## 5. Gaps

Gaps add space *between* tracks without affecting outer edges. Constructor
shorthand sets both axes:

```csharp
Grid.Of("100px 100px", "auto auto", gap: 12)
```

Different horizontal vs vertical:

```csharp
var grid = Grid.Of("auto 1fr", "auto auto");
grid.Grid.RowGap = 4;
grid.Grid.ColumnGap = 16;
```

Or via fluent methods (these mirror to flex too, since `BoxElement` carries
both styles):

```csharp
Grid.Of("auto auto").RowGap(8).ColumnGap(12);
```

---

## 6. Cell Alignment

By default, every cell **stretches** to fill its track on both axes. This
matches what most data grids want — the cell takes the full column width.

To override on a specific cell, use `JustifySelf` (horizontal) or
`AlignSelf` (vertical). Both default to `Auto`, meaning "use the container's
default":

```csharp
// Right-align a number in its cell
Text.New("$42.00").GridCell(0, 1).JustifySelf(JustifySelf.End)

// Center a small icon vertically in a tall row
icon.GridCell(0, 0).AlignSelf(AlignSelf.Center)
```

Override the container default for *all* cells:

```csharp
Grid.Of("auto auto").JustifyItems(JustifyItems.Center).AlignItems(AlignItems.Center)
```

| `JustifySelf` / `JustifyItems` value | Behavior |
|---|---|
| `Stretch` | Fill the cell horizontally (default). |
| `Start` | Pack to the left edge of the cell. |
| `End` | Pack to the right edge. |
| `Center` | Center horizontally. |

`AlignSelf` reuses the same enum as flex (`Auto`, `FlexStart`, `FlexEnd`,
`Center`, `Stretch`, `Baseline`). In a grid context, `FlexStart` = top of
the cell; `FlexEnd` = bottom.

> **Note**: when you set `.AlignItems()` or `.AlignSelf()` on an element,
> the value is applied to *both* the flex and grid styles. Whichever
> container algorithm runs reads its own copy. This keeps call sites clean.

**A fixed-height row needs `rows: "1fr"` to centre.** `Grid.Of(cols).Height(28)
.AlignItems(AlignItems.Center)` top-aligns its cells, because the default row
template is one `auto` track: it hugs the tallest cell, and centring happens
inside *that* track, not inside the 28px row. Pass `rows: "1fr"` so the track is
the whole row; it then matches the `Flex.Row` it replaced pixel for pixel. Rows
with no fixed height are fine with `auto`.

```csharp
Grid.Of("16px 1fr 60px 116px", rows: "1fr").Height(28).AlignItems(AlignItems.Center)
```

---

## 7. Performance Notes

The cost depends on the track kinds, and the two cases go in opposite
directions:

- **Fixed + Percent + Fr only**: *cheaper* than the equivalent `Flex.Row` — a
  lot cheaper on a cold first layout. Flex measures each child once
  unconstrained and again with its forced size, so fixed-width cells miss the
  measure cache and shape their text twice; the grid resolves such columns in
  closed form and measures each cell once at its final width. Measured
  headlessly (1,000 rows × 5 text cells, template `16px 1fr 60px 40px 116px`,
  Release): first frame 110–130 ms for flex rows against 29–34 ms for grid
  rows; resize relayout and same-tree rebuild within noise of each other.
  Virtualized, everything is under 6 ms either way — the bigger lever is still
  `VirtualListElement`.
- **Auto tracks**: roughly **1.3–1.5× the cost** of flex, dominated by the
  cell measurement passes below.
- **Auto tracks**: each cell in an auto track gets one extra `MeasureSize`
  call to determine its intrinsic width. The measurement cache absorbs
  repeated calls within a frame, so the cost scales with `(cells × 1)`,
  not `(cells × layout passes)`.
- **Auto rows after auto columns**: row resolution measures cells *with the
  resolved column width forced*, so text cells wrap correctly. This is one
  more cache miss per cell than the column pass.

For grids in the hundreds of cells, expect performance to remain frame-rate
friendly. For thousands of rows, prefer **`VirtualListElement`** with a
fixed row schema — virtualization and `auto` rows are fundamentally
incompatible (you cannot know max-content height without measuring every
row).

### Auto tracks don't shrink

`auto` tracks are sized to their cell content and **do not shrink** when the
grid container is forced smaller than its natural size (e.g. by a flex
parent's shrink distribution). This matches CSS Grid (`auto` ≈ `minmax(auto, auto)`,
not `minmax(0, auto)`).

In practice: if a grid sits inside a flex container with content that doesn't
fit, the grid will overflow rather than compress. Two common fixes:

```csharp
// 1. Tell flex not to shrink the grid container
panelContainingGrid.FlexShrink(0);

// 2. Or wrap the area in a ScrollView so overflow scrolls
ScrollView.New().FlexGrow(1).Children(myGrid)
```

---

## 8. Not Yet Supported

These are explicitly deferred. If you need one, raise it as a use case;
they're not blocked by architecture, just by demand.

- **Row / column spanning** — every cell occupies exactly one track.
- **Auto-flow placement** — every cell needs explicit `Row`/`Column`.
- **`min-content` / `max-content` track sizing** — only `auto` for intrinsic.
- **`minmax(min, max)` track sizing**.
- **Named lines / `grid-template-areas`**.
- **Subgrid** — nested grids align to their own tracks, not the parent's.
- **Sticky headers / frozen columns** — needs scroll-container integration.
- **Column resize via drag**.

---

## 9. Putting It Together

A settings dialog with three columns (label, control, hint), wrapped in a
scrollable region:

```csharp
var settings = Flex.Column().Padding(24).Children(

    Text.New("Editor settings").FontSize(20).FontWeight(FontWeight.Bold),

    Grid.Of("auto 1fr auto", gap: 12).Children(

        // Row 0: theme
        Text.New("Theme").GridCell(0, 0).AlignSelf(AlignSelf.Center),
        Dropdown.New(["Light", "Dark", "Auto"]).GridCell(0, 1),
        Text.New("Visual style").GridCell(0, 2)
            .FontSize(11).TextColor(Color.FromHex("#94A3B8"))
            .AlignSelf(AlignSelf.Center),

        // Row 1: tab size
        Text.New("Tab size").GridCell(1, 0).AlignSelf(AlignSelf.Center),
        TextInput.New().GridCell(1, 1),
        Text.New("Spaces per indent").GridCell(1, 2)
            .FontSize(11).TextColor(Color.FromHex("#94A3B8"))
            .AlignSelf(AlignSelf.Center),

        // Row 2: word wrap
        Text.New("Word wrap").GridCell(2, 0).AlignSelf(AlignSelf.Center),
        Checkbox.New().GridCell(2, 1).JustifySelf(JustifySelf.Start),
        Box.New().GridCell(2, 2)  // empty hint
    )
);
```

Notes:
- The label column (`auto`) shrinks to fit the longest label across all
  rows — every label aligns automatically.
- The control column (`1fr`) takes all remaining space.
- The hint column (`auto`) fits the longest hint.
- `AlignSelf(AlignSelf.Center)` vertically centers labels against
  potentially taller controls.
- `JustifySelf(JustifySelf.Start)` keeps the checkbox at its natural width
  rather than stretching it across the column.
