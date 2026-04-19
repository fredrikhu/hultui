# Layout Guide

HultUI uses a flexbox layout engine. Every element has three style objects that
control layout: `Box` (sizing, spacing, overflow), `Flex` (direction, alignment,
grow/shrink), and `Position` (absolute positioning). Builder extension methods
provide a fluent API on top of these.

> For tabular layouts where cells in different rows must align to the same
> column boundaries (data grids, property inspectors, settings forms), see
> [`GRID.md`](GRID.md).

```csharp
using HultUI.Core.Builders;    // Flex, Box, Text, ElementExtensions
using HultUI.Core.Layout;      // FlexDirection, JustifyContent, AlignItems, AlignSelf,
                                // AlignContent, FlexWrap, Overflow, PositionType
using HultUI.Core.Primitives;  // Thickness, Dimension, Color
```

---

## 1. Box Model

Every element has **Padding**, **BorderWidth**, and **Margin**, all represented
by the `Thickness` struct. Thickness has three constructors:

```csharp
new Thickness(8)              // uniform: all sides 8
new Thickness(4, 12)          // vertical 4, horizontal 12
new Thickness(4, 12, 8, 12)  // top, right, bottom, left
```

Set them through the `Box` style object or through builder extension methods:

```csharp
// Direct property access
var card = Box.New();
card.Box.Padding = new Thickness(16);
card.Box.BorderWidth = new Thickness(1);
card.Box.Margin = new Thickness(0, 0, 12, 0); // bottom margin only

// Builder pattern (uniform shorthand)
var card = Box.New()
    .Padding(16)
    .Margin(8);

// Builder pattern (Thickness overload)
var card = Box.New()
    .Padding(new Thickness(12, 16))
    .Margin(new Thickness(0, 0, 12, 0));
```

The layout engine computes the final size as:
**Margin > Border > Padding > Content**.

---

## 2. Flex Containers

Use `Flex.Row()` and `Flex.Column()` to create flex containers. The default
direction for a plain `BoxElement` is `Column`.

```csharp
// Horizontal row with 8px gap between children
var toolbar = Flex.Row(gap: 8).Children(
    Text.New("File"),
    Text.New("Edit"),
    Text.New("View")
);

// Vertical column with 12px gap
var list = Flex.Column(gap: 12).Children(
    Text.New("Item 1"),
    Text.New("Item 2"),
    Text.New("Item 3")
);
```

You can also set direction explicitly on any element:

```csharp
var container = Box.New()
    .FlexDirection(FlexDirection.RowReverse);
```

Available directions: `Row`, `RowReverse`, `Column`, `ColumnReverse`.

---

## 3. Main Axis Distribution (JustifyContent)

`JustifyContent` controls how children are distributed along the main axis
(horizontal for rows, vertical for columns). The default is `FlexStart`.

```csharp
// Center children horizontally in a row
var centered = Flex.Row()
    .JustifyContent(JustifyContent.Center)
    .Children(
        Text.New("A"),
        Text.New("B")
    );

// Push children to opposite ends
var spaced = Flex.Row()
    .JustifyContent(JustifyContent.SpaceBetween)
    .Children(
        Text.New("Left"),
        Text.New("Right")
    );
```

All values:

| Value | Behavior |
|---|---|
| `FlexStart` | Pack children toward the start (default) |
| `FlexEnd` | Pack children toward the end |
| `Center` | Center children along the main axis |
| `SpaceBetween` | Equal space between children, none at edges |
| `SpaceAround` | Equal space around each child (half-size at edges) |
| `SpaceEvenly` | Equal space between all children and edges |

---

## 4. Cross Axis Alignment (AlignItems)

`AlignItems` controls how children are aligned on the cross axis (vertical for
rows, horizontal for columns). The default is `Stretch`.

```csharp
// Vertically center children in a row
var row = Flex.Row(gap: 8)
    .AlignItems(AlignItems.Center)
    .Children(
        Text.New("Small").FontSize(12),
        Text.New("Large").FontSize(24)
    );
```

All values:

| Value | Behavior |
|---|---|
| `Stretch` | Stretch children to fill the cross axis (default) |
| `FlexStart` | Align children to the start of the cross axis |
| `FlexEnd` | Align children to the end of the cross axis |
| `Center` | Center children on the cross axis |
| `Baseline` | Align children along their text baseline |

---

## 5. Individual Alignment (AlignSelf)

`AlignSelf` overrides the parent's `AlignItems` for a single child. The default
is `Auto`, which inherits the parent's `AlignItems` value.

```csharp
var row = Flex.Row()
    .AlignItems(AlignItems.FlexStart)
    .Height(100)
    .Children(
        Text.New("Top-aligned"),
        Text.New("I stretch").AlignSelf(AlignSelf.Stretch),
        Text.New("Bottom").AlignSelf(AlignSelf.FlexEnd)
    );
```

All values: `Auto`, `FlexStart`, `FlexEnd`, `Center`, `Stretch`, `Baseline`.

---

## 6. Flex Grow and Shrink

`FlexGrow` controls how remaining space is distributed among children. The
default is `0` (do not grow). `FlexShrink` controls how children shrink when
space is insufficient. The default is `1` (allow shrinking). `FlexBasis` sets
the initial size before growing or shrinking.

```csharp
// Sidebar + main content layout
var layout = Flex.Row().Children(
    Box.New().Width(200).FlexShrink(0),          // fixed 200px sidebar
    Box.New().FlexGrow(1)                         // main fills remaining space
);

// Three equal-width columns
var columns = Flex.Row(gap: 16).Children(
    Box.New().FlexGrow(1).FlexBasis(0),
    Box.New().FlexGrow(1).FlexBasis(0),
    Box.New().FlexGrow(1).FlexBasis(0)
);

// 1:2 ratio
var ratio = Flex.Row(gap: 8).Children(
    Box.New().FlexGrow(1),
    Box.New().FlexGrow(2)
);
```

Use `.FlexShrink(0)` on elements that must keep their declared size:

```csharp
var icon = Box.New()
    .Size(24, 24)
    .FlexShrink(0);  // never compress the icon
```

---

## 7. Wrapping

By default, flex children stay on one line (`NoWrap`). Enable wrapping so
children flow onto new lines when they run out of space.

```csharp
var tags = Flex.Row(gap: 8)
    .FlexWrap(FlexWrap.Wrap)
    .Children(
        Text.New("C#"),
        Text.New("Flexbox"),
        Text.New("Cross-platform"),
        Text.New("Performance")
    );
```

Available values: `NoWrap`, `Wrap`, `WrapReverse`.

When wrapping is enabled, `AlignContent` controls how the wrapped lines are
distributed on the cross axis:

```csharp
var grid = Flex.Row(gap: 8)
    .FlexWrap(FlexWrap.Wrap)
    .AlignContent(AlignContent.Center)
    .Children(/* ... */);
```

`AlignContent` values: `FlexStart`, `FlexEnd`, `Center`, `Stretch`,
`SpaceBetween`, `SpaceAround`.

---

## 8. Fixed and Percentage Sizes

Set explicit sizes with pixel values or percentages. Pixel values can be passed
directly as `float` thanks to an implicit conversion on `Dimension`. Use
`Dimension.Percent()` for percentage-based sizing. The default for both `Width`
and `Height` is `Dimension.Auto`.

```csharp
// Fixed pixel size
var avatar = Box.New().Size(48, 48);

// Percentage width
var half = Box.New().Width(Dimension.Percent(50));

// Mixed
var sidebar = Box.New()
    .Width(300)
    .Height(Dimension.Percent(100));
```

Constrain sizes with `MinWidth`, `MaxWidth`, `MinHeight`, `MaxHeight`:

```csharp
var responsive = Box.New()
    .Width(Dimension.Percent(100))
    .MaxWidth(960)
    .MinHeight(400);
```

The constraint defaults are `Dimension.None` (unconstrained).

---

## 9. Gap

Gap adds spacing between children without affecting the outer edges. Set it
through the `Flex.Row()`/`Flex.Column()` constructor or the `.Gap()` extension.

```csharp
// Via constructor
var row = Flex.Row(gap: 12).Children(/* ... */);

// Via extension method
var column = Flex.Column()
    .Gap(16)
    .Children(/* ... */);
```

For different horizontal and vertical gaps (useful with wrapping), set `RowGap`
and `ColumnGap` directly on the flex style:

```csharp
var grid = Flex.Row()
    .FlexWrap(FlexWrap.Wrap);
grid.Flex.RowGap = 16;     // vertical gap between wrapped lines
grid.Flex.ColumnGap = 8;   // horizontal gap between items in a line
```

When `RowGap` or `ColumnGap` is `0`, the uniform `Gap` value is used as the
fallback.

---

## 10. Absolute Positioning

Set `Position.Type` to `Absolute` to remove an element from the normal flow. It
is positioned relative to its nearest ancestor that has clipping (overflow
hidden/scroll) or the root. Use `Position.Top`, `Right`, `Bottom`, `Left` to
set offsets. Control stacking order with `Position.ZIndex`.

```csharp
var container = Box.New()
    .Size(Dimension.Percent(100), Dimension.Percent(100));

// Badge in top-right corner
var badge = Box.New().Size(20, 20);
badge.Position.Type = PositionType.Absolute;
badge.Position.Top = 4;
badge.Position.Right = 4;
badge.Position.ZIndex = 10;

container.Children(badge);
```

Position offsets accept `Dimension` values, so percentages work too:

```csharp
var centered = Box.New().Size(200, 100);
centered.Position.Type = PositionType.Absolute;
centered.Position.Top = Dimension.Percent(50);
centered.Position.Left = Dimension.Percent(50);
```

The default position type is `Relative`, which keeps the element in normal flow.

---

## 11. Overflow

Overflow controls whether content that exceeds an element's bounds is visible,
clipped, or scrollable. Set independently for each axis.

```csharp
// Clip overflow on both axes
var clipped = Box.New().Size(200, 100);
clipped.Box.OverflowX = Overflow.Hidden;
clipped.Box.OverflowY = Overflow.Hidden;
```

Available values: `Visible` (default), `Hidden`, `Scroll`.

`Overflow.Scroll` tells the layout engine to give children unlimited space on
that axis, enabling scrollable content.

### ScrollViewElement

For scrollable regions, use `ScrollViewElement` directly. It sets
`OverflowX = Hidden` and `OverflowY = Scroll` by default, handles scroll
events, and renders scrollbars.

```csharp
var list = ScrollViewElement.New()
    .Width(Dimension.Percent(100))
    .FlexGrow(1)
    .Children(
        Flex.Column(gap: 4).Children(
            Text.New("Row 1"),
            Text.New("Row 2"),
            Text.New("Row 3")
            // ... many more rows
        )
    );
```

Configure scrollbar appearance:

```csharp
var scroll = ScrollViewElement.New();
scroll.ScrollSpeed = 60f;
scroll.ScrollbarWidth = 8f;
scroll.ScrollbarColor = new Color(128, 128, 128, 80);
scroll.ScrollbarThumbColor = new Color(128, 128, 128, 200);
scroll.ShowScrollbarX = true;  // enable horizontal scrollbar
```

---

## Putting It Together

A typical layout combining several concepts:

```csharp
var app = Flex.Column().Size(Dimension.Percent(100), Dimension.Percent(100)).Children(

    // Header
    Flex.Row(gap: 12)
        .Padding(new Thickness(8, 16))
        .AlignItems(AlignItems.Center)
        .Background(new Color(30, 30, 30))
        .Children(
            Text.New("HultUI App").FontSize(18).FontWeight(FontWeight.Bold),
            Box.New().FlexGrow(1),  // spacer
            Text.New("Settings")
        ),

    // Body: sidebar + content
    Flex.Row().FlexGrow(1).Children(

        // Sidebar
        Flex.Column(gap: 8)
            .Width(220)
            .FlexShrink(0)
            .Padding(12)
            .Children(
                Text.New("Home"),
                Text.New("Projects"),
                Text.New("Settings")
            ),

        // Scrollable content area
        ScrollViewElement.New()
            .FlexGrow(1)
            .Padding(16)
            .Children(
                Flex.Column(gap: 12).Children(
                    Text.New("Welcome").FontSize(24),
                    Text.New("Content goes here.")
                )
            )
    )
);
```
