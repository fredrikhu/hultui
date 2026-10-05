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

> **A bare `Box.New()` is a column, so its axes are the reverse of the rows
> around it.** Inside a `Box`, `JustifyContent` is *vertical* (the main axis)
> and `AlignItems` is *horizontal* (the cross axis, default `Stretch`). Most
> cells in an app are `Flex.Row`, where the two are the other way round, so
> `AlignItems.Center` reads from habit as "centre this vertically" — in a `Box`
> it centres the text horizontally instead, and every line of a text column
> comes out centred in its cell. To centre a child vertically in a `Box`, use
> `JustifyContent.Center` and leave `AlignItems` at `Stretch`, so a text child
> fills the cell and clips at the cell's edge rather than at the width of its
> own text. A cheap test that catches it without pixels: assert the text's
> border box starts at the cell's content box `X`.

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

> **`JustifyContent` distributes free space in the container's own main size,
> so a container sized by its content has none.** A column with no height that
> its parent doesn't stretch — say a `Flex.Column` inside a
> `Flex.Row().Height(32).AlignItems(AlignItems.Center)` — is exactly as tall as
> its children, and `JustifyContent.Center` on it changes nothing; the row's
> `AlignItems` is what centres it. To centre within a size, give the container
> that size: a `Height`, a stretch from its parent, or a `MinHeight` (an auto
> container justifies within its min and max). A scroll view justifies within
> its viewport, so content that fits is centred and content that overflows
> starts at the top. Until 2026-10-05 an auto-sized container justified against
> the space its parent offered and then grew to where its last child ended —
> 24.6px tall around 17.3px of text, with the text at the bottom.

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
| `Baseline` | Align children along their text baseline (rows only — see below) |

**Baseline.** In a row, the children set to `Baseline` are placed so the first line of
text in each sits on one baseline — a big number beside its small unit, a label beside the
text in a field, text beside an icon:

```csharp
Flex.Row(gap: 6).AlignItems(AlignItems.Baseline).Children(
    Text.New("42").FontSize(32),
    Text.New("items").FontSize(12));
```

- **Where a child's baseline is.** Text, and controls that draw text (text fields and
  areas, checkbox/radio/toggle labels, dropdowns, number, date and time fields), report
  where they really draw their first line. A container uses its first child's — or, when a
  row inside it aligns by baseline, that row's shared one. Anything else (a box, an image,
  an icon) gets one at the bottom of its border box, as in CSS, so text beside an icon
  sits on the icon's bottom edge.
- **The row grows** when that alignment pushes something below the tallest child: text
  sitting on an icon's bottom hangs its descenders under it, and the row is tall enough to
  hold them. The measure pass knows this, so a parent sizing the row gets the real height.
- **The group sits at the top of the row**; in a row taller than the group (a fixed
  height), the space is left below it.
- **In a column it is `FlexStart`**, as CSS does: a column has no baseline across its line.
  Grid items are not baseline-aligned yet (they start-align), and a grid container
  reports no baseline of its own, so it is treated like a box.

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

**`FlexShrink(0)` does not stop a label from wrapping.** A child in a tight row is
*measured* under the leftover width before shrink is resolved, and a text element that
may wrap does so during that measurement. A `FlexShrink(0)` parent then faithfully keeps
the wrapped size — narrow and tall — because as far as shrink is concerned nothing was
compressed. A chip, badge or tag that must stay on one line needs both:

```csharp
Flex.Row().FlexShrink(0).Children(
    Text.New(label).MaxLines(1).TextOverflow(TextOverflow.Ellipsis));
```

The diagnosis, when a label wraps that should not: dump the ancestor chain with widths
and `FlexShrink` per level — a wrapped label's box is about as wide as the wrapped text,
not the intrinsic single line.

The related trap in the other direction is in `TODO.md`'s durable notes: **`FlexGrow(1)`
alone does not mean "take the leftover space"**, because the default `auto` basis sizes
the item to its own content first. `FlexBasis(0)` is what says "start from nothing".

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

A min or max binds the box **and what is laid out inside it**: a column with `MaxWidth(720)`
in a 1400-wide parent is 720 wide, and its children are laid out across 720; text with a
`MaxWidth` wraps at it and is as tall as its wrapped lines. The same holds for a size the
parent forces — a stretched flex item, a scroll view's content — and on both axes.

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

**Never `FlexGrow` the content inside one.** The scroll view measures its child against
an unbounded height — that is what makes scrolling possible — and a growing child resolves
that unbounded space to a number near `float.MaxValue`. The child then reports an
astronomical height, the scroll view collapses to its content's *minimum*, and everything
laid out beside it shrinks with it. The symptom is the confusing part: the page still
paints, and the accessibility tree still reports sensible boxes for the widgets inside,
but clicks land on whatever is behind the region because the ancestor's box is a few
hundred points tall. Give the content `FlexGrow(1)` on the *scroll view* and nothing on
what it holds:

```csharp
var scroll = ScrollView.Vertical().FlexGrow(1);      // the viewport grows
scroll.AddChild(Flex.Column(gap: 24).Padding(24));   // the content does not
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

### Nesting scroll views

**The wheel is shared out along the path.** A scroll view takes the axes it can move on
and leaves the rest to the scroll views around it, so a horizontal strip inside a vertical
page scrolls sideways and lets the vertical wheel through to the page, and a list at the end
of its range hands the next gesture to the page. A *gesture* — one spin of the wheel, a
swipe and its inertia — stays with the scroller its first event moved, for as long as the
events keep coming (less than 300 ms apart) and the pointer stays over it: a flick that
reaches the end of a list stops there rather than carrying on with the page, and a page
being scrolled is not taken over by a list that arrives under the pointer. Browsers do the
same and call it latching. Shift turns a vertical wheel sideways. A wheel over an overlay
belongs to the overlay.

A **horizontal** view (`ScrollView.Horizontal()`, or `VerticalWheelScrollsSideways` set)
also turns a plain vertical wheel sideways — a list in Windows' list mode moves through its
columns that way, and a mouse without a tilt wheel has no other way there short of Shift. It
defers to the page: while a scroll view around it can still move the way the wheel turned,
the wheel is the page's, so a strip on its way past keeps the page scrolling. Only when
nothing around can — the strip fills its pane, or the page is already at its end — does the
strip take it.

To take part from your own code — something that scrolls or changes a value on the wheel —
follow the same three steps the built-in elements do:

```csharp
el.On<ScrollEvent>(e =>
{
    var self = e.CurrentTarget;               // never the captured element: a rebuild detaches it
    if (!e.IsFor(self)) return;               // a gesture that belongs to another scroller
    float dy = e.RemainingDeltaY;             // 0 once something nearer the pointer used it
    if (dy == 0) return;
    bool moved = MyScroll(dy);
    e.Consume(self, movedX: false, movedY: moved);   // what you used; the rest goes on
});
```

Don't set `StopPropagation` on a wheel event: in this framework it also ends the element's
remaining handlers, which is what used to keep an app's own wheel handler on a scroll view
from ever running.

One constraint still decides how a wide *virtualized* list has to be built:

- **A `VirtualListElement` cannot scroll sideways at all.** Its content width is pinned
  to the viewport, so `ScrollOffsetX` is permanently 0. A plain `ScrollView` measures its
  children and can.

The pattern that works, used by every wide list in the apps (a side-by-side diff, a
file viewer): keep **one vertical scroll view**, hold the horizontal offset as app state
in a `HorizontalPan`, and apply it as a **negative left margin on the content inside a
cell with `OverflowX(Overflow.Hidden)`** — which is what `HorizontalPan.Cell(content)`
builds. The margin both shifts the content left and widens it by the same amount
(cross-axis stretch computes the forced width as `crossSize - margin.Horizontal`), so
text genuinely arrives from the right rather than leaving a hole. Fixed columns beside
the cell (line-number gutters) stay frozen, and several cells sharing one pan stay
aligned.

```csharp
_pan = new HorizontalPan(MarkDirty);                      // once
_pan.ContentWidth = HorizontalPan.MeasureWidest(lines, fontSize, fontFamily);

// per row, inside the vertical list:
_pan.Cell(Text.New(line).MaxLines(1).TextOverflow(TextOverflow.Clip))

// on the list:
list.ScrollSidewaysWith(_pan);                             // the wheel's and autoscroll's sideways motion
list.On<KeyDownEvent>(e => _pan.HandleKey(e));
```

`ScrollSidewaysWith` is what makes the list's sideways axis the pan's: a trackpad's
sideways swipe, Shift+wheel and middle-click autoscroll all go there. Keys are still the
app's to route. `_pan.Scrollbar(track, thumb, border)` builds a draggable bar under the
list. (`_pan.HandleScroll(e)` exists for a pan whose content is not a scroll view's.)

---

**A rounded box clips round.** `Overflow.Hidden` on a box with a corner radius cuts its
children to the rounded shape, the way `overflow: hidden` does with a `border-radius` — so
an image inside a circular avatar is a circle, and artwork filling a rounded card follows
the card's corners instead of squaring them off. Without `Hidden` nothing is clipped at
all, rounded or not.

## 12. Outline, not a conditional border

A common bug: selecting a tab or a swatch makes its neighbours shift, because the
selected state adds a border and the border takes space. Reaching for `box-sizing:
border-box` does not help, and HultUI is already border-box: a declared `Width()` *is*
the border box and padding and border eat inward. That only helps an element with a
declared size. With no declared width, both box models give `content + padding +
border`, so a border added in one state still grows the box and shoves every sibling
along.

Use **`Outline(width, color, offset)`** instead: a ring drawn outside the border box that
layout never reads. `offset: -width` puts it exactly where the border would have been;
use that where elements touch, since a ring at offset 0 is painted over by the next
sibling.

```csharp
Box.New()
    .Outline(selected ? 2f : 0f, accent, offset: -2f)   // width 0 draws nothing
```

When a size *is* declared, border-box fixes the outer size but the content box still
shrinks by the border width, so the text inside still moves. The complete fix for that
half is a transparent border in the unselected state: layout reads `BorderWidth`
regardless of colour, and a fully transparent border is not drawn.

When testing that a state change is layout-neutral, the fixture must be
**content-sized**: with declared widths, border-box hides exactly the problem the test
exists to catch.

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
