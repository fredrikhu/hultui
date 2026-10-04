# HultUI Built-in Elements Reference

This document covers every built-in element in HultUI, including builder syntax, key properties, fluent API methods, and short examples.

**Key imports used throughout:**

```csharp
using HultUI.Core.Builders;   // Box, Flex, Text, TextInput, etc.
using HultUI.Core.Elements;    // BoxElement, TextElement, etc.
using HultUI.Core.Primitives;  // Color, Rect, Point, Size, Thickness, CornerRadius
using HultUI.Core.Layout;      // FlexDirection, JustifyContent, AlignItems, TextOverflow, ImageFit
using HultUI.Core.Styling;     // FontWeight, CursorType
using HultUI.Core.Rendering;   // ICanvasDrawingContext, IImageSource
```

---

## Common Properties (all elements)

Every element inherits from `Element` and exposes these properties, settable directly or through fluent extension methods.

### Layout (Box model)

| Property | Fluent | Type |
|---|---|---|
| `Box.Width` | `.Width(value)` | `Dimension` |
| `Box.Height` | `.Height(value)` | `Dimension` |
| `Box.MinWidth` | `.MinWidth(value)` | `Dimension` |
| `Box.MaxWidth` | `.MaxWidth(value)` | `Dimension` |
| `Box.MinHeight` | `.MinHeight(value)` | `Dimension` |
| `Box.MaxHeight` | `.MaxHeight(value)` | `Dimension` |
| `Box.Padding` | `.Padding(thickness)` or `.Padding(uniform)` | `Thickness` / `float` |
| `Box.Margin` | `.Margin(thickness)` or `.Margin(uniform)` | `Thickness` / `float` |

There is also `.Size(w, h)` as a shorthand for setting both width and height.

### Flex Layout

| Property | Fluent | Type |
|---|---|---|
| `Flex.Direction` | `.FlexDirection(dir)` | `FlexDirection` |
| `Flex.Wrap` | `.FlexWrap(wrap)` | `FlexWrap` |
| `Flex.FlexGrow` | `.FlexGrow(value)` | `float` |
| `Flex.FlexShrink` | `.FlexShrink(value)` | `float` |
| `Flex.FlexBasis` | `.FlexBasis(value)` | `Dimension` |
| `Flex.Gap` | `.Gap(value)` | `float` |
| `Flex.JustifyContent` | `.JustifyContent(jc)` | `JustifyContent` |
| `Flex.AlignItems` | `.AlignItems(ai)` | `AlignItems` |
| `Flex.AlignSelf` | `.AlignSelf(a)` | `AlignSelf` |
| `Flex.AlignContent` | `.AlignContent(ac)` | `AlignContent` |

### Inherited Style

These propagate to child elements unless overridden.

| Property | Fluent | Type |
|---|---|---|
| `Inherited.FontFamily` | `.FontFamily("name")` | `string` |
| `Inherited.FontSize` | `.FontSize(16f)` | `float` |
| `Inherited.FontWeight` | `.FontWeight(FontWeight.Bold)` | `FontWeight` |
| `Inherited.TextColor` | `.TextColor(color)` | `Color` |
| `Inherited.Cursor` | `.Cursor(CursorType.Pointer)` | `CursorType` |

### Font families and metrics

- **One family or a list.** `.FontFamily("JetBrains Mono, Consolas, monospace")` uses the first
  family that is installed, as CSS's `font-family` does. A list chooses the face; a character that
  face lacks still falls back to another installed font. A single name that is not installed
  draws in the platform's default face, so end a list with a generic name to say what happens
  instead.
- **Generic names.** `FontFamilies.Monospace` (`"monospace"`) is the platform's monospace face:
  Cascadia Mono, then Consolas, on Windows; Menlo on macOS; fontconfig's `monospace` on Linux.
  `FontFamilies.SansSerif` is the default interface face.
- **Metrics.** `TextMeasure.MeasureFont(style)` at build time, and `ctx.MeasureFont(style)` while
  drawing, return a `FontMetrics`: the ascent, descent and line gap, `LineHeight`, the
  `CharacterWidth` (the advance of `0`, which in a monospace face is the cell width), whether the
  face is fixed-pitch, the face actually used, and where an underline and a strikeout go.
- **Glyph coverage.** `HasGlyph(style, codePoint)` says whether the face itself has a glyph, not a
  fallback font. A character grid draws a missing one on its own, since a fallback glyph's advance
  need not be the cell's.
- **Fallback keeps a sequence in one font.** A character that only extends the one before it — a
  joiner, a variation selector, an emoji tag, and a skin tone or combining mark the font has — stays
  in that one's font, as does a character after a joiner that the font has. A sequence split across
  fonts is never shaped into its one glyph: the joiners in 👨‍👩‍👧 are in most text faces, and used to
  break the family into three people.
- **Measure contexts that cannot read a font** (test stubs, the headless fixed-metrics context)
  answer with estimates from a measured `"0"`, and say every glyph is there.

### Events

| Fluent | Handler type |
|---|---|
| `.OnClick(handler)` | `Action<ClickEvent>` |
| `.OnPointerDown(handler)` | `Action<PointerDownEvent>` |
| `.OnPointerUp(handler)` | `Action<PointerUpEvent>` |
| `.OnPointerEnter(handler)` | `Action<PointerEnterEvent>` |
| `.OnPointerLeave(handler)` | `Action<PointerLeaveEvent>` |
| `.OnScroll(handler)` | `Action<ScrollEvent>` |

You can also call `el.On<TEvent>(handler)` directly for any event type.

**Pointer capture.** A `PointerDownEvent` handler can call `e.CapturePointer()`.
From then until the button comes up, every `PointerMoveEvent` and the
`PointerUpEvent` go to that element wherever the pointer is, in that element's
coordinates; nothing else sees the pointer meanwhile (no enter/leave, no drag
start, no context menu from that press), and hover catches up on the release.
This is how `Slider` keeps following a drag that wanders off its track. The
release still synthesizes a click when it lands on the captured element.

Handlers are replaced by each rebuild's versions during reconciliation, so closures stay in sync with what the element displays. One rule: never capture the element variable itself — for hover/press/focus styling use the declarative `.OnHover`/`.OnPressed`/`.OnFocus` overrides (they survive rebuilds and restore the base look automatically), use `e.CurrentTarget` for other self-references (e.g. reading `.Layout`), and `Ref<T>` for cross-element reach. See "Event handlers follow the latest build" in REACTIVITY.md.

### Identity

| Fluent | Type | Purpose |
|---|---|---|
| `.Name("ContactCard")` | `string` | A developer-facing label. Shows in the inspector as `#ContactCard` and in frame traces. Duplicates are fine — nothing resolves elements by it. |
| `.AutomationId("save-btn")` | `string` | A selector for tests and UI Automation. Keep it unique per window and stable across runs; lookups take the first match. |

`Key` (set as a property, not fluent) is a third identity field with a stricter rule: it must be unique **among siblings**, because the reconciler matches children by it across rebuilds. See "Name, AutomationId, Key" in DEVTOOLS.md.

### Accessibility

| Fluent | Type |
|---|---|
| `.AccessibleRole(role)` | `AccessibilityRole` |
| `.AccessibleName("label")` | `string` |
| `.AccessibleDescription("desc")` | `string` |

`AccessibleName` is what a screen reader announces. `Name` is only its last-resort fallback — reach for `AccessibleName` on anything user-facing, and leave `Name` for debugging.

**A `BoxElement` with a click handler is announced as a button.** When no role is set, the
tree builder infers one from the element type, and the last rule is "a box with an
`OnClick` is a `Button`", named from the text inside it. That is right for a clickable
card and wrong for a row whose click merely selects it, or a backdrop whose click closes a
dialog: give those an explicit `.AccessibleRole(...)`. An explicit role always wins over
inference.

### Tree

| Method | Description |
|---|---|
| `.Children(params Element[])` | Replace all children (fluent) |
| `.AddChild(element)` | Append a child |
| `.RemoveChild(element)` | Remove a child |
| `.ReplaceChildren(elements)` | Replace all children |
| `.Parent` | The parent element (read-only) |
| `.Children` | List of child elements (read-only) |

`.Children(...)` **replaces** the whole child list — a second call throws the first one
away. Assemble the full list and call it once, or use `AddChild` for the odd extra.
`ReplaceChildren` unmounts only the elements that actually leave, so a child present in
both the old and the new list is moved, not remounted.

Removing an element unmounts everything under it too: a component hosted three levels
down stops rebuilding and gets its `OnUnmount`. Putting a removed element back (a cached
panel, say) mounts it and everything under it again, even if it is a descendant of the
removed subtree and still has its old parent. A mounted element added somewhere else is
moved and hears neither.

---

## 1. BoxElement

The universal container. Equivalent to a `<div>`. Use it for layout grouping, backgrounds, and borders.

**Builders:**

```csharp
Box.New()             // Empty container, default column direction
Flex.Row(gap: 8)      // BoxElement with FlexDirection.Row and gap
Flex.Column(gap: 4)   // BoxElement with FlexDirection.Column and gap
```

**Properties (in addition to common):**

| Property | Fluent | Type | Default |
|---|---|---|---|
| `BackgroundColor` | `.Background(color)` | `Color` | `Color.Transparent` |
| `BorderColor` | `.BorderColor(color)` | `Color` | `Color.Transparent` |
| `CornerRadius` | `.CornerRadius(8f)` or `.CornerRadius(new CornerRadius(...))` | `CornerRadius` | `0` |
| `Outline` | `.Outline(width, color, offset = 0)` | `Outline` | `Outline.None` |

**Outline is the ring for a selected or active state.** It is CSS's `outline`: drawn
outside the border box and never read by layout, so it cannot resize an auto-sized
element or push its siblings. A border added only in the selected state does both —
HultUI is border-box, but that only decides how a *declared* size is read, and an
element with no declared width is `content + padding + border` either way. Positive
`offset` pushes the ring outward, negative pulls it in; `offset: -width` lands exactly
where a border of that width would sit, which is what you want where elements touch
(list rows), since a ring at offset 0 is painted over by the next sibling. A width of 0
or a transparent colour draws nothing, so the unselected branch needs no `if`. See
[LAYOUT.md — Outline, not a conditional border](LAYOUT.md#12-outline-not-a-conditional-border).

**Example:**

```csharp
var card = Box.New()
    .Background(new Color(255, 255, 255, 255))
    .BorderColor(new Color(200, 200, 200, 255))
    .CornerRadius(8f)
    .Padding(16f)
    .Children(
        Text.New("Card title").FontSize(18f).FontWeight(FontWeight.Bold),
        Text.New("Some body text here.")
    );
```

**Row layout example:**

```csharp
var toolbar = Flex.Row(gap: 8)
    .Padding(12f)
    .AlignItems(AlignItems.Center)
    .Children(
        Text.New("File"),
        Text.New("Edit"),
        Text.New("View")
    );
```

---

## 2. TextElement

Renders text. Supports multi-line wrapping, text overflow with ellipsis, and line clamping.

**Builder:**

```csharp
Text.New("content")
```

**Properties (in addition to common):**

| Property | Type | Default |
|---|---|---|
| `Content` | `string` | `""` |
| `Overflow` | `TextOverflow` | `TextOverflow.Clip` |
| `MaxLines` | `int?` | `null` (unlimited) |
| `Selectable` | `bool` | `false` |
| `SelectionGroup` / `SelectionIndex` | `TextSelectionGroup?` / `int` | `null` / `-1` |
| `Spans` | `IReadOnlyList<TextSpan>?` | `null` |
| `Links` / `LinkActivated` | `IReadOnlyList<TextLink>?` / `Action<string>?` | `null` |

`TextOverflow` values: `Clip`, `Ellipsis`.

**Selection (opt-in via `.Selectable()`):**

- Click-drag to select, Shift-click to extend, Ctrl+A / Ctrl+C, right-click Copy
- Double-click selects the run of same-class characters under the pointer — letters, digits
  and `_` are one class, whitespace another, punctuation and symbols a third. So a
  double-click in code yields one identifier rather than everything up to the next space,
  and one in the indentation yields the indentation.
- Triple-click selects the line
- Shift with ←/→/↑/↓/Home/End/PageUp/PageDown moves the loose end of the selection;
  Ctrl+Shift+Home/End reach the ends of the document

Give several labels the same `SelectionGroup` and their own `SelectionIndex` and one
selection runs across all of them — the group holds it as line numbers, so it survives
virtualized rows being recycled and copies lines that were never realized. Set the group's
`RevealLine` so keyboard extension can scroll a line that has walked off screen into view.

**Links:**

`Links` marks ranges of `Content` as clickable; a click inside one calls `LinkActivated`
with that range's target. Appearance is separate — give the same ranges a `TextSpan` to
colour and underline them — so links stay part of the flowing, selectable text instead of
becoming their own elements.

```csharp
Text.New(message)
    .Selectable()
    .Spans([new TextSpan(start, length, accent, Decoration: TextDecoration.Underline)])
    .Links([new TextLink(start, length, url)], OpenInBrowser);
```

**Example:**

```csharp
var heading = Text.New("Welcome to HultUI")
    .FontSize(24f)
    .FontWeight(FontWeight.Bold)
    .TextColor(new Color(30, 30, 30, 255));

var truncated = Text.New("This is a very long description that will be cut off...")
    .Width(200f)
    .FontSize(14f);
truncated.Overflow = TextOverflow.Ellipsis;
truncated.MaxLines = 2;
```

---

## 3. TextInputElement

A single-line text input field. Extends `BoxElement`, so it supports backgrounds, borders, and corner radius.

**Builder:**

```csharp
TextInput.New()              // Empty input
TextInput.New("initial text") // Pre-filled
```

**Properties (in addition to BoxElement):**

| Property | Fluent | Type | Default |
|---|---|---|---|
| `Text` | -- | `string` | `""` |
| `Placeholder` | `.PlaceholderText("hint")` | `string` | `""` |
| `PlaceholderColor` | -- | `Color` | `rgb(150,150,150)` |
| `SelectionColor` | -- | `Color` | `rgba(66,135,245,80)` |
| `CursorColor` | -- | `Color` | `rgb(0,0,0)` |
| `FocusBorderColor` | -- | `Color` | `rgb(66,135,245)` |
| `CursorWidth` | -- | `float` | `1.5f` |
| `TextChanged` | `.OnTextChanged(handler)` | `Action<string>?` | `null` |
| `Clipboard` | -- | `IClipboard?` | Set by Application |
| `CanUndo`, `CanRedo` | -- | `bool` (read-only) | |

`Undo()` and `Redo()` do what Ctrl+Z and Ctrl+Y do, for an Edit menu or a toolbar button;
both return false when there was nothing to do.

**Built-in interaction features:**

- Click to place caret
- Click-drag to select text
- Shift-click to extend selection
- Double-click to select a word
- Triple-click to select all
- Ctrl+A to select all
- Ctrl+C / Ctrl+X / Ctrl+V for clipboard
- Ctrl+Z to undo, Ctrl+Y or Ctrl+Shift+Z to redo
- Ctrl+Left/Right for word navigation, Ctrl+Backspace/Delete to erase a word
- Horizontal scroll when text overflows the box
- Right-click menu: Undo, Redo, Cut, Copy, Paste, Select All

The Ctrl+letter chords match the letter printed on the key (`KeyDownEvent.LayoutKey`), not
its position: on a German keyboard Ctrl+Z is the key `KeyCode` calls `Y`, and on a French one
Ctrl+A is the key it calls `Q`.

**Undo** takes back one action as a person counts them, not one keystroke: typing undoes a
word at a time (a step ends where the next word starts), a run of Backspaces or of Deletes
undoes as one, and a paste, a cut, deleting a selection and an input-method commit are each a
step of their own. Moving the caret or clicking ends a run. Undoing an edit made over a
selection brings the selection back with the text. 200 steps are kept.

**A value set from code forgets the history.** Setting `Text` to a different string — the app
clearing a field after its form was sent, or reformatting what was typed — drops every step,
because their offsets describe the old text. The rebuild after each keystroke in a controlled
field hands back the value the field already holds, which changes nothing and keeps the
history. A rebuild that *replaces* the element (see Reconciliation under `TextAreaElement`)
loses it along with the caret.

A "word" means the same thing to every one of these as it does to a double-click:
letters, digits and `_` form one class, whitespace another, and punctuation and symbols a
third — so `feature/my-branch` and `src/core/Foo.cs` are several stops, not one. The rule
lives in `WordBoundary` and is shared by `TextInputElement`, `TextAreaElement` and
`TextElement`.

**The theme owns the box — style the theme or give the element a `Style`.** `ApplyTheme`
runs on *every* style pass, not once, and it re-imposes `Padding`, `BorderWidth`,
`CornerRadius`, `BackgroundColor` and `BorderColor` each time. A `.Padding(...)` or
`.Background(...)` set on the input itself survives exactly until the next pass and then
silently reverts. Three ways to a field that differs from the theme's:

- **`.Style(TextInputStyle)`** — a reusable look, resolved ahead of the theme: every knob
  of `TextInputStyle` is nullable (`Background`, `FocusBackground`, `Border`,
  `FocusBorder`, `Underline`, `FocusUnderline`, `Placeholder`, `Cursor`, `Selection`,
  `Padding`, `BorderWidth`, `CornerRadius`, `FontSize`, `UnderlineThickness`,
  `FocusUnderlineThickness`), and null falls through to `Theme.TextInput`, then the
  palette. Declare it once as a static and apply it per field; it is compared by
  reference, so a fresh object every build is a style pass every build. A height the
  style does not name is left to the element, and **the line is centred in whatever height
  the field has**, the same place the caret goes — so a bordered form field is one
  element:

  ```csharp
  static readonly TextInputStyle Field = new()
  {
      Background = Bg, Border = Divider, BorderWidth = new Thickness(1f),
      CornerRadius = new CornerRadius(8f), Padding = new Thickness(0, 10, 0, 10),
  };

  TextInput.New(value).Style(Ui.Field).Height(34f).FlexGrow(1)
  ```

- **`DrawsChrome = false`** hands the box back to you: the theme then leaves padding,
  border, radius and fill alone, and the element draws only its text, caret and selection.
  Use it for an input embedded in a search box or a toolbar that draws its own frame.
- **Wrap the input in a `Box`** that carries the frame, for a composed control — a
  magnifier beside the text, a button inside the field. A plain bordered field no longer
  needs this.

`FontSize` is the exception: the theme value is only a default (`??=`), so a builder
`.FontSize(...)` on the input wins over both the style and the theme. `TextColor` and
`FontFamily` are not touched by the theme at all. `TextAreaElement` takes the same
`TextInputStyle` through its own `.Style(...)` and has **no** `DrawsChrome`.

**Example:**

```csharp
var searchBox = TextInput.New()
    .PlaceholderText("Search...")
    .Width(300f)
    .Height(36f)
    .CornerRadius(4f)
    .Background(new Color(245, 245, 245, 255))
    .OnTextChanged(text => Console.WriteLine($"Query: {text}"));
```

---

## 4. CheckboxElement

A toggle checkbox. Click to toggle between checked and unchecked. Draws a check mark when checked.

**Builder:**

```csharp
Checkbox.New()          // Unchecked
Checkbox.New(true)      // Pre-checked
```

**Properties (in addition to common):**

| Property | Fluent | Type | Default |
|---|---|---|---|
| `IsChecked` | -- | `bool` | `false` |
| `OnChanged` | `.OnCheckedChanged(handler)` | `Action<bool>?` | `null` |
| `CheckboxSize` | -- | `float` | `18f` |
| `BorderWidth` | -- | `float` | `2f` |
| `BoxColor` | -- | `Color` | `rgb(200,200,200)` |
| `CheckedColor` | -- | `Color` | `rgb(66,135,245)` |
| `CheckmarkColor` | -- | `Color` | `rgb(255,255,255)` |
| `CornerRadius` | -- | `float` | `3f` |

**Example:**

```csharp
var row = Flex.Row(gap: 8)
    .AlignItems(AlignItems.Center)
    .Children(
        Checkbox.New(false)
            .OnCheckedChanged(v => Console.WriteLine($"Dark mode: {v}")),
        Text.New("Enable dark mode")
    );
```

---

## 5. RadioButtonElement

A radio button. Only one radio button in the same group can be checked at a time. When a radio button is selected, all others in the same group within the element tree are automatically deselected.

**Builder:**

```csharp
RadioButton.New("groupName")          // Unchecked, in group
RadioButton.New("groupName", true)    // Pre-checked, in group
```

**Properties (in addition to common):**

| Property | Fluent | Type | Default |
|---|---|---|---|
| `GroupName` | -- | `string` | `""` |
| `IsChecked` | -- | `bool` | `false` |
| `OnChanged` | `.OnRadioChanged(handler)` | `Action<bool>?` | `null` |
| `RadioSize` | -- | `float` | `18f` |
| `BorderWidth` | -- | `float` | `2f` |
| `CircleColor` | -- | `Color` | `rgb(200,200,200)` |
| `CheckedColor` | -- | `Color` | `rgb(66,135,245)` |
| `DotColor` | -- | `Color` | `rgb(255,255,255)` |

Clicking an already-checked radio button does nothing. The group scope is the entire element tree (walks up to the root, then searches all descendants).

**Example:**

```csharp
var options = Flex.Column(gap: 6).Children(
    Flex.Row(gap: 8).AlignItems(AlignItems.Center).Children(
        RadioButton.New("size", true).OnRadioChanged(v => { if (v) SetSize("small"); }),
        Text.New("Small")
    ),
    Flex.Row(gap: 8).AlignItems(AlignItems.Center).Children(
        RadioButton.New("size").OnRadioChanged(v => { if (v) SetSize("medium"); }),
        Text.New("Medium")
    ),
    Flex.Row(gap: 8).AlignItems(AlignItems.Center).Children(
        RadioButton.New("size").OnRadioChanged(v => { if (v) SetSize("large"); }),
        Text.New("Large")
    )
);
```

---

## 6. ScrollViewElement

A scrollable container. Extends `BoxElement`. Clips overflowing children and provides scrollbar rendering.

**Builders:**

```csharp
ScrollView.New()          // Default: vertical scroll
ScrollView.Vertical()     // Same as New() -- vertical scroll
ScrollView.Horizontal()   // Horizontal scroll (row direction, X scrollbar shown, Y hidden)
```

**Properties (in addition to BoxElement):**

| Property | Fluent | Type | Default |
|---|---|---|---|
| `ScrollOffsetX` | -- | `float` | `0` |
| `ScrollOffsetY` | -- | `float` | `0` |
| `ScrollSpeed` | `.ScrollSpeed(40f)` | `float` | `40f` |
| `ShowScrollbarX` | -- | `bool` | `false` |
| `ShowScrollbarY` | -- | `bool` | `true` |
| `ScrollbarColor` | -- | `Color` | `rgba(128,128,128,100)` |
| `ScrollbarThumbColor` | -- | `Color` | `rgba(128,128,128,180)` |
| `ScrollbarWidth` | -- | `float` | `6f` |
| `ContentWidth` | -- | `float` | (read-only) |
| `ContentHeight` | -- | `float` | (read-only) |

Scroll offset is automatically clamped to the valid range based on content size.

**Interaction features:**

- Mouse wheel scrolling
- Scrollbar thumb drag
- Track click
- Middle-click autoscroll (`ScrollBy(dx, dy)` from the frame loop; `CanAutoScroll` says whether there is anywhere to go)

**Nesting.** A scroll view's wheel handler sets `StopPropagation` whether or not the
offset moved, so a horizontal scroll view inside a vertical one eats the vertical wheel:
the inner offset clamps to 0 and the outer never sees the event. Two-axis surfaces are
built with one vertical scroll view and a sideways *pan* held as app state — see
[LAYOUT.md — Nesting scroll views](LAYOUT.md#nesting-scroll-views).

**Reveal before layout.** `ScrollRangeIntoView(top, height)` scrolls the minimum
distance to show a band of content, and needs the viewport height to do it. Called
before the view has been laid out (from `OnAfterBuild`, say — that runs before the
layout pass), it holds the request and applies it when the extent is next computed,
so a reveal asked for on the same frame the content appeared is not lost.
`ScrollIntoView(child)` and `ScrollRangeIntoViewHorizontal(left, width)` are the
element and sideways forms.

**`ScrollSidewaysWith(ISidewaysScroller)`.** Set when the horizontal offset is owned by
something other than the view's own `ScrollOffsetX` (a `HorizontalPan`). `ScrollBy`
then forwards `dx` there instead, and autoscroll asks it whether sideways travel is
possible. A panned list without this is half-broken: the wheel works because the app
routes it, but the autoscroll gesture, which the framework drives, aims sideways and
nothing moves.

**Example:**

```csharp
var list = ScrollView.Vertical()
    .Height(400f)
    .ScrollSpeed(60f)
    .Children(
        Flex.Column(gap: 4).Children(
            Enumerable.Range(0, 100)
                .Select(i => Text.New($"Item {i}").Padding(new Thickness(4, 8, 4, 8)))
                .ToArray()
        )
    );
```

---

## 7. CanvasElement

A custom drawing surface. Extends `BoxElement`, so it inherits backgrounds, borders, and corner radius. The `Draw` callback receives an `ICanvasDrawingContext` with coordinates relative to the element's content box.

**Builders:**

```csharp
Canvas.New(draw)   // With draw callback
Canvas.New()       // Without draw callback (set Draw property later)
```

**Properties (in addition to BoxElement):**

| Property | Type |
|---|---|
| `Draw` | `Action<ICanvasDrawingContext>?` |

**ICanvasDrawingContext methods:**

| Method | Description |
|---|---|
| `Bounds` | The drawing area (`Rect`, read-only) |
| `DrawRect(rect, fill)` | Filled rectangle |
| `DrawLine(from, to, color, strokeWidth)` | Line between two points |
| `DrawCircle(center, radius, fill)` | Filled circle |
| `DrawCircleStroke(center, radius, color, strokeWidth)` | Circle outline |
| `DrawRoundedRect(rect, radius, fill)` | Filled rounded rectangle |
| `DrawRoundedRectStroke(rect, radius, color, strokeWidth)` | Rounded rectangle outline |
| `DrawText(text, position, color, fontSize)` | Text at a given position |

**Example:**

```csharp
var chart = Canvas.New(ctx =>
{
    float w = ctx.Bounds.Width;
    float h = ctx.Bounds.Height;

    // Draw axes
    ctx.DrawLine(new Point(0, h), new Point(w, h), new Color(0, 0, 0, 255), 1f);
    ctx.DrawLine(new Point(0, 0), new Point(0, h), new Color(0, 0, 0, 255), 1f);

    // Draw a data point
    ctx.DrawCircle(new Point(w * 0.5f, h * 0.3f), 6f, new Color(66, 135, 245, 255));

    // Draw a label
    ctx.DrawText("50%", new Point(w * 0.5f + 10, h * 0.3f - 8), new Color(0, 0, 0, 255), 12f);
})
.Size(400f, 200f)
.Background(new Color(250, 250, 250, 255));
```

**Pixel snapping.** Coordinates are in DIPs and the renderer decides what lands on a
device pixel. The rule is CSS's: snap the *edges* of a shape onto the device grid, then
derive its size from the snapped edges — never round position and size independently,
or two shapes sharing a coordinate can round apart and open a one-pixel seam. Knowing
which operations snap tells you what a custom drawing can rely on:

| Operation | Snapped? |
|---|---|
| Borders (`DrawBorder`, the stroke of a rounded rect) | yes, edges and thickness; drawn **inside** the border box, not straddling its edge |
| Solid fills (`DrawRect`, backgrounds, gradients) | yes; a rect asked for at all becomes at least one device pixel, so a 0.3-DIP caret cannot vanish, while a zero-size rect stays empty |
| `DrawLine` | **only when axis-aligned**; diagonals keep raw coordinates, because nearly every diagonal in the framework is a hand-drawn glyph (a checkbox tick, a chevron) that snapping would kink |
| Arcs and rounded corners | no — snapping a curve steps it |
| `FillPolygon`, `DrawPolyline` | no — antialiased along their slopes; a caller that wants a crisp edge places its points on the grid itself |
| Text, shadows, images | no — text keeps subpixel positioning, shadows are blurred, images would change resampling |

Two consequences worth knowing. A snapped fill's size is *position-dependent*: a 1-DIP
rule at 1.5× is one device pixel at one `y` and two at another, which is correct — a
position-independent size is what opens seams. A line is the exception and keeps a
constant weight, since it has a width rather than two edges. And a fill drawn out to
the same edge as a border is pulled back by half the stroke width, because two
antialiased curves on one boundary blend rather than cover, and the fill showed through
at rounded corners. A custom control that draws its own frame and fill should do the
same.

Everything above is invisible at scale 1.0. A pixel test for renderer geometry that only
runs at 100% is a false green; the headless driver takes `--scale` for this reason.

---

## 8. ImageElement

Displays an image from an `IImageSource`. The source is provided by the platform backend. The `Fit` property controls how the image is scaled within the element's content box.

**Builder:**

```csharp
Image.New()            // No source yet
Image.New(source)      // With an IImageSource
```

**Properties (in addition to common):**

| Property | Fluent | Type | Default |
|---|---|---|---|
| `Source` | `.ImageSource(source)` | `IImageSource?` | `null` |
| `Fit` | `.ImageFit(fit)` | `ImageFit` | `ImageFit.Contain` |

**ImageFit values:**

| Value | Behavior |
|---|---|
| `Contain` | Scale uniformly to fit entirely within the box (may letterbox) |
| `Cover` | Scale uniformly to cover the box entirely (may crop) |
| `Fill` | Stretch to fill the box exactly (may distort) |
| `ScaleDown` | Like `Contain`, but never scales up beyond intrinsic size |
| `None` | Render at intrinsic size, no scaling |

If the source is `null` or not yet loaded, the element renders nothing. The intrinsic size of the image is used for layout measurement when no explicit width/height is set.

**Example:**

```csharp
var avatar = Image.New(avatarSource)
    .Size(48f, 48f)
    .ImageFit(ImageFit.Cover);

var banner = Image.New(bannerSource)
    .Width(Dimension.Percent(100))
    .Height(200f)
    .ImageFit(ImageFit.Cover);
```

**Changing pixels.** A source whose pixels change over time — a video frame, a
camera, a software-rendered view — implements `IPixelImageSource`, and the renderer
draws its current bytes without copying. `PixelFrameSink` is the ready-made one: a
ring of three pinned buffers the producer writes through a `Span<byte>`, from any
thread.

```csharp
var sink = new PixelFrameSink();
var view = Image.New(sink).Width(640f).Height(360f);

// Producer thread. FramePublished fires here; marshal before touching the tree.
sink.FramePublished += _ => Application.InvokeOnMainThread(() => view.MarkPaintDirty());

using (var frame = sink.BeginWrite(640, 360, PixelFormat.Bgrx8888))
{
    frame.Pixels.Fill(0x40);   // rows are frame.Stride bytes apart
    frame.Publish();
}
```

Mark the element layout-dirty too when the frame size changes, since it is the
intrinsic size. The renderer holds a frame from the draw until the frame is
flushed and only then hands it back, so a producer never overwrites what is being
uploaded; with three slots it never has to wait for it either. The `x` formats
(`Rgbx8888`, `Bgrx8888`) are opaque with the fourth byte ignored, which is what a
decoder emitting `rgb0`/`bgr0` needs. Under the headless host the render log
records `version=N` for every frame drawn.

---

## 9. ToggleSwitchElement

An on/off switch: a pill-shaped track with a knob that slides to the on side. Same
contract as a checkbox (click or Space toggles, `OnChanged` reports the new state),
but the knob animates across the track and the element reads as a **switch** to
assistive technology, so a reader says "switch, on" rather than "check box, checked".

**Builder:**

```csharp
ToggleSwitch.New()                    // Off
ToggleSwitch.New(true, "Wi-Fi")       // On, with a label to the right
```

**Properties (in addition to common):**

| Property | Fluent | Type | Default |
|---|---|---|---|
| `IsOn` | -- | `bool` | `false` |
| `OnChanged` | `.OnToggled(handler)` | `Action<bool>?` | `null` |
| `Label` | -- | `string?` | `null` |
| `TrackWidth` / `TrackHeight` / `KnobSize` | -- | `float` | `40` / `20` / `14` |
| `TransitionSeconds` | -- | `float` | `0.15` (zero snaps) |
| `KnobPosition` | -- | `float` (read-only) | where the knob is drawn, 0..1 |

Colours (`OnTrackColor`, `OffTrackColor`, `OffBorderColor`, `OnKnobColor`,
`OffKnobColor`) and sizes come from `Theme.ToggleSwitch` (`ToggleSwitchTheme`),
which derives from the palette: the on track is `Primary`, the off state is an
outlined `SurfaceVariant`. Override per app with
`Theme.Dark.With(new ToggleSwitchTheme { TrackWidth = 48f })`.

The knob position is a single number the element owns. Setting `IsOn` on a mounted
element tweens it; on an unmounted one (the incoming twin of a rebuild, or an
element not yet shown) it snaps, so a rebuild that patches `IsOn` onto the live
element slides the knob from wherever it currently is.

**Example:**

```csharp
ToggleSwitch.New(settings.DarkMode, "Dark mode")
    .OnToggled(on => settings.DarkMode = on);
```

---

## 10. ProgressBarElement

A horizontal progress bar: a track with a fill that grows from the left to the
fraction `Value` is of `Minimum`..`Maximum`. It stretches to the width it is
offered, so it sits in a column the way a divider does; give it a `Width` to fix
it. Labels, percentages and captions are the app's to compose around it.

**Builder:**

```csharp
ProgressBar.New(0.68)                 // 68 % of the default 0..1 range
ProgressBar.New(340, max: 500)        // 340 of 500
ProgressBar.New().Indeterminate()     // activity with no known fraction
```

**Properties (in addition to common):**

| Property | Fluent | Type | Default |
|---|---|---|---|
| `Value` | `.Value(v)` | `double` (clamped) | `0` |
| `Minimum` / `Maximum` | `.Range(min, max)` | `double` | `0` / `1` |
| `IsIndeterminate` | `.Indeterminate(bool = true)` | `bool` | `false` |
| `Fraction` | -- | `double` (read-only) | `0..1` |
| `Height` | -- | `float` | `4` |
| `CornerRadius` | -- | `float?` | `null` (a pill) |
| `SweepPosition` | -- | `float` (read-only) | where the indeterminate segment is |

`TrackColor`, `FillColor`, `Height` and `CornerRadius` come from
`Theme.ProgressBar` (`ProgressBarTheme`): the fill is `Primary` on a
`Border`-coloured track. A value set on the element itself wins over the theme, so
two bars in one panel can be coloured apart.

The indeterminate sweep is a looping tween that runs only while the element is
mounted and stops the moment it leaves the tree or `IsIndeterminate` clears, so an
idle window with a finished bar renders nothing.

**Accessibility:** reads as a **progress bar** with a read-only range value; set
`AccessibleName` to say what is progressing.

**Example:**

```csharp
Flex.Column(gap: 6).Children(
    Text.New($"Downloading update — {pct}%").FontSize(12f),
    ProgressBar.New(pct, max: 100));
```

---

## 11. SpinnerElement

A busy indicator: a ring of dots with the leading one at full colour and a fading
tail, turning for as long as the element is on screen. Drop it in a row beside the
label of whatever is waiting.

**Builder:**

```csharp
Spinner.New()          // the theme's size (16)
Spinner.New(24f)       // a fixed size
```

**Properties (in addition to common):**

| Property | Type | Default |
|---|---|---|
| `Size` | `float` | theme `16` |
| `Color` | `Color` | theme `Primary` |
| `DotCount` | `int` | `8` |
| `PeriodSeconds` | `float` | `0.9` |
| `TailAlpha` | `float` | `0.15` |
| `Phase` | `float` (read-only) | where the leading dot is, 0..1 of a turn |

All of these come from `Theme.Spinner` (`SpinnerTheme`); a size passed to the
builder wins over the theme's.

The turn is a looping tween that starts from the element's first paint and stops
itself as soon as the element is no longer hanging off a hosted root, so a rebuild
that stops asking for the spinner stops the tween and nothing has to be serviced
after a build. `Element.IsHosted` is the check it uses.

**Accessibility:** reads as an indeterminate **progress bar**; set `AccessibleName`
to say what is being waited for.

**Example:**

```csharp
Flex.Row(gap: 8).AlignItems(AlignItems.Center).Children(
    Spinner.New(14f),
    Text.New("Fetching…").FontSize(12.5f));
```

---

## 12. NumericUpDownElement

A number field with step buttons: the value on the left, an up/down strip on the
right. The range is enforced by the element, so callers stop clamping at every
site. Up/Down and the wheel step by `Step`, PageUp/PageDown by `LargeStep`,
Home/End go to the bounds, a held button repeats after a short delay, and typing
replaces the value on Enter (or blur) after parsing; Escape reverts an edit.

**Builder:**

```csharp
NumericUpDown.New(12, min: 6, max: 72)                 // integers, step 1
NumericUpDown.New(1.5, 0, 10, step: 0.5)               // shows one decimal
NumericUpDown.New(12).Format(v => $"{v:0.#} pt")       // custom display; typing parses a plain number
```

**Properties (in addition to common):**

| Property | Fluent | Type | Default |
|---|---|---|---|
| `Value` | `.Value(v)` | `double` (clamped, snapped) | `0` |
| `Minimum` / `Maximum` | `.Range(min, max)` | `double` | `0` / `100` |
| `Step` | `.Step(s)` | `double` | `1` |
| `LargeStep` | `.LargeStep(s)` | `double?` | ten steps |
| `Formatter` | `.Format(fn)` | `Func<double, string>?` | as many decimals as the step |
| `OnChanged` | `.OnValueChanged(h)` / `.Changed(h)` | `Action<double>?` | `null` |
| `DisplayText` / `IsEditing` | -- | read-only | what the field shows; whether a typed edit is pending |

The box reuses `Theme.Dropdown` (like the date and time pickers) so a form row
shares one shape; the strip comes from `Theme.NumericUpDown`
(`NumericUpDownTheme`: `ButtonWidth`, chevron colour and stroke, pressed fill,
`RepeatDelaySeconds`, `RepeatIntervalSeconds`).

**One field that should not share the shape** — a compact stepper in a tight row, a
wide picker — takes a `DropdownStyle` through `.Style(...)`: every knob of
`DropdownTheme` made nullable (`Background`, `BorderColor`, `TextColor`,
`PlaceholderTextColor`, `ChevronColor`, `CornerRadius`, `BorderWidth`,
`FocusBorderWidth`, `Padding`, `FontSize`, `MinWidth`, `ChevronGap`, `ChevronShape`,
`ChevronStrokeWidth`, `PopupGap`), laid over the theme by `DropdownTheme.Resolve`. The
same `.Style(...)` exists on `DropdownElement`, `DatePickerElement` and
`TimePickerElement`, so one declared style reshapes any field in the family. Declare it
once as a static; it is compared by reference.

```csharp
static readonly DropdownStyle Compact = new() { Padding = new Thickness(2, 6), FontSize = 11f, MinWidth = 48f };
NumericUpDown.New(count, 1, 9).Style(Compact)
```

`OnChanged` runs for user gestures only — buttons, keys, wheel, typing and
assistive technology — never for assigning `Value`.

**Accessibility:** reads as a **spin button** with a writable range value.

**Example:**

```csharp
NumericUpDown.New(settings.FontSize, 6, 72)
    .Format(v => $"{v:0.#} pt")
    .OnValueChanged(v => settings.FontSize = v);
```

---

## 13. PaneSplitter and SplitterHandleElement

The draggable border between a fixed-size pane and whatever fills the rest of a row
or column. It is a **model plus a handle**, not a container: `PaneSplitter` holds
the pane's size as app state and reports every move (the way `HorizontalPan` holds
its offset), and the owner rebuilds the pane at that size. A size written onto an
element would be overwritten by the next build.

```csharp
// Once, in the component:
_split = new PaneSplitter(MarkDirty, Persist, min: 180, max: 600, initial: 260);
_split.AttachDrag(root);   // on an ancestor: a drag ends with the pointer off the handle

// In Build():
Flex.Row().AlignItems(AlignItems.Stretch).Children(
    fileList.Width(_split.Size).FlexShrink(0),
    _split.Handle(),
    diff.FlexGrow(1));
```

| Member | Meaning |
|---|---|
| `Size` | the pane's width (side by side) or height (stacked), always within `min..max` |
| `Handle()` / `Handle(hover, grabbed)` | the grip, themed or with explicit tints |
| `AttachDrag(root)` | hears the move and release on an ancestor; passive, so an idle window does not repaint per pointer move |
| `Restore(size)` | set from remembered settings; no callback |
| `IsDragging` | true while grabbed |
| `SplitterAxis.Vertical` (constructor) | stacked panes: the handle is a horizontal strip dragged up and down |

`onChanged` runs on every move; `onCommitted` once per drag (and per key step), so a
settings file is written once rather than sixty times a second. The grab is
remembered as pointer-at-press plus size-at-press, so a splitter dragged past its
minimum does not jump when the pointer comes back.

**`SplitterHandleElement`** is the grip on screen: the resize cursor, a tint under
the pointer and a stronger one while grabbed, a focus ring, and the keyboard —
arrow keys along the axis move by `SplitterTheme.KeyStep` (Shift: five steps),
Home/End go to the bounds. It draws no line of its own; the pane beside it already
borders that edge. Theme: `Theme.Splitter` (`SplitterTheme`: `Thickness` 6,
`Hover`/`Grabbed` tints defaulting to the primary colour at 18 % and 40 %, `KeyStep`).

**Accessibility:** the handle reads as a **separator** named "Resize"; set
`AccessibleName` to say what it resizes.

---

## 14. ToastHost and ToastElement

One transient notice at a time, bottom-centre. `ToastHost` is app state, like
`PaneSplitter`: the owner adds `Build()` to its root on every build and the host calls
back to rebuild when the toast appears, changes, fades or goes. `ToastElement` is the
pill it draws.

```csharp
// Once, in the component:
_toasts = new ToastHost(MarkDirty);

// In Build():
if (_toasts.Build() is { } strip) root.AddChild(strip);

// Anywhere:
_toasts.Flash("Copied");
_toasts.Flash("Pushed main", ToastSeverity.Success, action: ("Create merge request", OpenMergeRequest));
_toasts.Flash("Push failed", ToastSeverity.Error, seconds: 4);
```

| Member | Meaning |
|---|---|
| `Flash(message, severity, action, seconds)` | show, replacing what is up; an action keeps it `ActionSeconds` instead of `Seconds` |
| `Dismiss()` | take it down now, no fade |
| `Message` / `Severity` / `Action` / `IsShowing` / `Opacity` | what is up |
| `Seconds` 1.7, `ActionSeconds` 4.5, `FadeInSeconds`, `FadeOutSeconds`, `BottomMargin` 22, `ZIndex` 50 | the timings and placement |

The clock is a tween, not a task, so it runs on the frame loop's virtual time and a
headless test pumps exactly the seconds it means to. A second `Flash` while one is up
replaces the text and restarts the clock without blinking. The strip spans the
window's bottom edge and is looked through by the pointer (`Element.IsHitTestVisible
= false`), so a toast over the bottom edge never steals the clicks meant for whatever
sits there; the pill inside it is still clickable.

**`ToastElement`** draws the pill from `Theme.Toast` (`ToastTheme`: surface fill,
border, text, the action verb in `Primary`, and fixed success/warning accents plus
`Danger` for errors). A severity draws a bar at the leading edge.

**Accessibility:** a plain toast is an **alert** named by its message. Either way the
message is announced as the toast appears (`Element.Announce`: politely, an error
assertively), since a reader would not find it in the second or two it is up. With an
action the whole pill is a **button** named by
the verb, with the message as its description: a bigger target for the second or
two it is up, and one invokable node rather than a link buried in an alert. Clicking it dismisses the toast and runs
the action.

---

## 15. VirtualListElement\<T\>

A scroll view that realizes only the rows in and around the viewport. Extends
`ScrollViewElement`. For a list in the thousands it is the difference between a frame
and a freeze; for a few dozen rows a plain `ScrollView` over a `Flex.Column` is simpler.

**Builder:** there is no `VirtualList` builder class — use the element's own factory.

```csharp
var list = VirtualListElement<FileEntry>.New()
    .Items(entries)                 // IReadOnlyList<T>
    .ItemHeight(24f)                // uniform rows (default 40)
    .RenderWith(e => Row(e))        // Func<T, Element>
    .AccessibleItemLabel(e => e.Name)
    .OnActivateItem(Open)           // a reader's Invoke/Select action on a row
    .OnSelectionChanged(ix => _selected = ix)
    .FlexGrow(1);
```

| Fluent | Property | Meaning |
|---|---|---|
| `.Items(list)` | `Items` | the data; any `IReadOnlyList<T>` |
| `.ItemHeight(h)` | `ItemHeight` | one height for every row |
| `.RowHeights(Func<T,int,float>)` | `ItemHeightFor` | per-row heights (the extension is not called `ItemHeightFor`: that name would bind to invoking the delegate) |
| `.RenderWith(Func<T,Element>)` | `ItemTemplate` | builds a row; called for every realized row on every pass |
| `.SelectedIndex(i)`, `.CanSelectMultiple()`, `.OnSelectionChanged(h)` | `SelectedIndex`, `CanSelectMultiple`, `SelectedIndices` | built-in selection, keyboard included |
| `.OnActivateItem(h)` | `OnItemActivate` | called when assistive technology invokes or selects a row through the accessibility bridge; pointer and keyboard activation are the row template's own `OnClick` / `On<KeyDownEvent>` handlers |
| `.KeyboardScroll(bool)` | `KeyboardScrollEnabled` | arrows / PageUp / PageDown / Home / End |
| — | `RowsAreComposite` | one item is a row of controls (a wrap grid), so the realized rows answer to assistive tech rather than one node per item — see **A row that is not one thing** below |
| — | `InvalidateHeights()`, `InvalidateRows()` | after a change the list cannot see |

**How rows live.** The visible window plus a row or two of overscan is rebuilt on every
patch — the template is re-invoked, so a rebuild of the owning component always shows
current data with no reference tricks. A row with a `Key` is retained across passes and
patched instead of replaced, which is what keeps focus, a caret or a scroll offset alive
inside it; key rows that hold state. Rows are force-sized to their height and
`FlexShrink(0)`, so a template that wants a different height must say so through
`RowHeights`.

**No sideways scroll.** The content width is pinned to the viewport, so `ScrollOffsetX`
is always 0 and a wide row simply clips. A virtual list that has to pan sideways uses a
`HorizontalPan` and a negative-margin cell — see
[LAYOUT.md — Nesting scroll views](LAYOUT.md#nesting-scroll-views) — and wires the pan
with `.ScrollSidewaysWith(pan)` so middle-click autoscroll follows it.

**Revealing a row.** `ScrollRangeIntoView(top, height)` works from `OnAfterBuild`, before
layout, because the virtual list clamps against the rows it holds *now* rather than the
extent the last layout left behind; a request made in the same frame the list grew is
honoured, not clamped back to 0.

**Accessibility.** The list is announced as a `List`; `AccessibleItemLabel` names each
row without realizing it, so a reader can walk 20,000 items that were never built.

**Items that are drawn, not built.** `IAccessibleItemSource` is not only for lists that
virtualize: a container that paints its own rows — the context menu does — implements it so
those rows reach assistive tech at all. `ItemBounds(i)` places them when they are not a
uniform height (a menu with separators in it), and `ItemIsToggled(i)` says which of them is
the choice in force.

**A ref points at the element you just built.** `WithRef` sets `Current` immediately, and
reconciliation repoints it at the live element afterwards — so inside one `Build()`, a ref
registered earlier in that same build refers to an element whose layout has not run, and
reading `.Layout` from it gives zeros. Read a ref's geometry **before** building the new
tree (the top of `Build()`), where it still points at the frame that was laid out. The video
scrubber's preview learned this the hard way: it was positioned in the corner every time,
because the track it measured was the track it had just created.

**Part of an image.** `ImageElement.SourceRect` draws one rectangle of the source — in the
image's own pixels — over the whole element, which is how a frame is taken from a sprite
sheet. It is also the element's intrinsic size, so a sheet of twenty frames measures as one
frame. `IRenderContext.DrawImage(source, dest, sourceRect)` is the draw underneath; a
context with no pixels to cut from falls back to drawing the whole image. The headless
recorder records the rectangle (`source=` in the log) and passes it on; until 2026-10-04 it
took that fallback, so a recorded session drew the whole sheet squashed into the frame's box.

**A slider cut into pieces.** `SliderElement.Segments` takes values inside the range where
the track is split, with `SegmentGap` between the pieces — a video scrubber divided by its
chapter marks, so a chapter is something you can see and aim at. Marks outside the range and
duplicates are ignored, and no marks is the single continuous track it has always been.
`ShowHoverFill` paints the track up to the pointer in `SliderTheme.HoverFillColor` (the fill
colour at just under half opacity by default), which is how a scrubber says "release here and
this is where you land". It is off by default: on an ordinary slider a second fill reads as a
second value.

**Pointing at a slider.** `SliderElement.OnHoverValue` reports the value under the pointer
as it crosses the track, and null when it leaves — including while dragging, since the thumb
is under the pointer either way. Hovering never changes the value; that still takes a press,
a drag or a key. The video scrubber's preview is what this exists for.

**Marking the current choice.** Set `ContextMenuItem.IsChecked` rather than writing a mark
into `Label`. `true` draws the theme's `CheckGlyph` in the icon column, `false` reserves the
column while the item is off (so turning it on shifts nothing), and `null` — the default —
means the item is not a choice. A label that begins `"• "` is read out as punctuation, and
the spaces its unchecked siblings need in order to line up are read out with them; a checked
state is announced as a state and exposed through UIA's Toggle pattern and AT-SPI's `checked`.

**A row that is not one thing.** Drive a wrap grid through the list — one item per *row*
of cards — and that model inverts: the list would publish one node per row, named after
all the cards in it, and every card's own name, role and automation id would vanish from
the tree. `RowsAreComposite = true` says the rows carry real controls, so the builder
walks the realized rows instead of synthesizing item nodes. The trade is that off-screen
rows are not in the tree; they enter it as they realize, the way a scroll view's content
does. Aperture's poster grid is the case this exists for.

---

## 16. TextAreaElement

A multi-line, word-wrapping text editor. Not a `BoxElement`: it draws its own box from
`Theme.TextInput`, exactly as `TextInputElement` does (see "The theme owns the box"
above — there is no `DrawsChrome` here, so a bordered area is a text area inside a
bordered `Box`).

**Builder:**

```csharp
TextArea.New()                     // empty
TextArea.New("initial text")       // pre-filled; caret at the end
    .PlaceholderText("Notes…")
    .OnTextChanged(t => _notes = t)
    .ReadOnly()                    // selectable and scrollable, not editable
    .Spans(highlightSpans);        // IReadOnlyList<TextSpan>, colour and decoration per range
```

Those four are the only fluent members; everything else is a property.

| Property | Type | Notes |
|---|---|---|
| `Text` | `string` | setting it collapses the selection to a caret, and a different value forgets the undo history |
| `CursorPosition` | `int` | UTF-16 offset |
| `SelectionStart`, `SelectionEnd`, `HasSelection`, `SelectedText` | read-only | document-order |
| `CanUndo`, `CanRedo` | read-only | false while `ReadOnly` |
| `ReadOnly` | `bool` | |

Undo and redo work as in `TextInputElement` — Ctrl+Z, Ctrl+Y / Ctrl+Shift+Z, `Undo()`,
`Redo()`, the right-click menu — and a line break typed with Enter counts as typing, so a new
line's first word starts a step of its own. A read-only area ignores both keys and has no Undo
in its menu.
| `Spans` | `IReadOnlyList<TextSpan>?` | syntax colouring; backgrounds only for search hits so grammar colour survives |
| `ScrollOffsetY`, `ScrollSpeed`, `Scrollbar*` | | vertical only |
| `TextChanged` | `Action<string>?` | |

**Wrapping is unconditional.** Every hard line wraps at the content-box width; there is
no wrap switch, and the wrapped rows are cached against the width they wrapped at, so a
resize re-wraps with nothing for you to do. The corollary is that there is never
anything to the right to scroll to: `ScrollBy` ignores `dx`, and autoscroll is vertical.

**Revealing a position.** Two calls:

```csharp
area.ScrollLineIntoView(line);      // a logical line index
area.ScrollOffsetIntoView(offset);  // a character offset — prefer this
```

Both scroll only if the target is off screen, and both resolve through the wrap cache to
the true visual row. Prefer the offset form for anything that already holds an offset
(a search hit, a selection): when the whole body is one logical line — minified JSON, a
long log line — every hit is on line 0 and the line form has nothing to say.

**Reconciliation** keeps the caret and selection when the incoming text is the same
string, so a rebuild mid-edit is safe; a rebuild that *replaces* the element (a changed
`Key`, a changed type, a swapped parent) loses them, which is the usual reason a field
"keeps only its first character".

---

## 17. Date and time pickers

Three elements in `HultUI.Core.Elements`, builders in `HultUI.Core.Builders`:

```csharp
MonthCalendar.New(selected: today)             // CalendarElement — the month grid
    .Selected(d => _date = d);

DatePicker.New(value: _date)                   // DatePickerElement — a field that opens the grid
    .Changed(d => _date = d);                  // Action<DateOnly?>; null when cleared

TimePicker.New(value: _time)                   // TimePickerElement — a segmented spinner
    .Changed(t => _time = t);                  // Action<TimeOnly?>
```

**The builder is `MonthCalendar`, not `Calendar`.** `System.Globalization.Calendar`
owns that name, and every file that formats a date imports that namespace, so a builder
called `Calendar` would be ambiguous in exactly the files that want one.

`Selected` / `Changed` are the only fluent members; the rest are properties on the
element. **Setting `Value` (or `SelectedDate`) from code never raises the callback** —
callbacks are for the user's own gestures.

| Element | Properties |
|---|---|
| `CalendarElement` | `SelectedDate`, `DisplayMonth`, `FocusedDate`, `Today`, `MinDate`, `MaxDate`, `FirstDayOfWeek`, `Culture`, `MonthFormat` (`"MMMM yyyy"`); `ShiftMonths(n)`, `SelectDate(d)`, `DateAt(row, col)` |
| `DatePickerElement` | `Value`, `Format` (`"d"`), `Placeholder`, `Culture`, `MinDate`, `MaxDate`, `Today`, `FirstDayOfWeek`, `AllowClear`; `IsOpen`, `Calendar`, `DisplayText`, `OpenPopup()`, `ClosePopup()` |
| `TimePickerElement` | `Value`, `ShowSeconds`, `Use12Hour`, `MinuteStep`, `Culture`, `AllowClear`; `ActiveSegment`, `Step(±1)`, `MoveSegment(±1)`, `FocusSegment(s)`, `TypeDigit(n)`, `Clear()` |

Both fields draw the closed face from `Theme.Dropdown`, and both take a per-field
`DropdownStyle` through `.Style(...)` — the same one a dropdown or a number field takes,
see [NumericUpDownElement](#12-numericupdownelement) — for a picker that should not share
the form's shape.

Design calls that are settled:

- **The calendar draws itself** rather than composing 42 child boxes: a hover highlight
  would otherwise cost 42 allocations, and flex is the wrong tool for equal cells.
- **Six rows, always.** Sizing to the month makes the popup change height as you page,
  so the day under the pointer becomes a different day.
- **The time picker is a segmented spinner, not a dropdown of times.** A list is either
  1,440 rows or cannot express 14:37.
- **`Today` and `Culture` are properties**, not reads of the ambient clock and locale.
  That is what makes a rendered picker comparable against a baseline that does not
  expire and does not depend on the developer's country.

**Themes.** `Theme.Calendar` (`CalendarTheme`) themes the month grid only. Both fields
borrow **`Theme.Dropdown`** for their box, so a form does not grow three differently
shaped controls; `Theme.TimeField` (`TimeFieldTheme`) adds only what a segmented field
needs (active segment, separators, horizontal padding).

**A field whose width follows its value must `MarkLayoutDirty`.** `DatePickerElement`
measures a *sample* date so a numeric format never resizes, but a long format
("Monday, 15 September") has no widest case, so the live value is measured too and a
repaint alone would clip it. Its `Value` setter marks layout for that reason; the time
picker's digits are fixed-width, so its `Value` only repaints. Copy the distinction in
any custom field whose text changes its width.

---

## 18. ButtonElement

A push button. Extends `BoxElement`; focusable, activates on Enter or Space, sets the
pointer cursor, and is a `Button` to assistive technology before anything subscribes to
its click. Its whole look comes from the theme and is re-imposed on every style pass —
**style the theme or give the element a `Style`, not its box**. Focus shows through the
window's focus visual (`ShowsFocusVisual`), which the theme keeps to keyboard focus by
default, so a click leaves no ring behind.

**Builder:**

```csharp
Button.New("Save")                                   // Primary
Button.New("Cancel", ButtonVariant.Secondary)
Button.New("Delete", ButtonVariant.Danger)
Button.New("More…", ButtonVariant.Ghost)
    .OnClick(_ => ...)
    .Variant(ButtonVariant.Secondary)                // fluent form
Button.New(ButtonVariant.Ghost)                      // empty: add a glyph and a label
    .Children(Icon(...), Text.New("Fetch"))
Button.New("Start").Enabled(false).Tooltip("Nothing older to squash into")
```

**Variant is meaning, style is shape.** The variant picks the colours from the palette;
a `ButtonStyle` names whatever it wants different — colours or geometry — and leaves the
rest to the layers beneath it. Each knob resolves in this order: the element's own
`.Style(...)`, the theme's style for its variant (`ButtonTheme.Secondary`, …), the
shared values on `ButtonTheme`, then the palette. A reusable look is therefore a style
declared once and applied per element:

```csharp
// once, in the app's UI helpers
static readonly ButtonStyle IconButton = new()
{
    Size = 28f, Padding = new Thickness(0), Circular = true,
};

// at every use site: secondary colours, round geometry, nothing restated
Button.New(ButtonVariant.Secondary).Style(Ui.IconButton).Children(Icon(Icons.Plus))
```

Hand the same style instance each build (a static), not a fresh one: `Style` compares by
reference, and a new object every rebuild is a style pass every rebuild.

A `ButtonStyle` carries nullable `Background`, `HoverBackground`, `PressedBackground`,
`Border`, `HoverBorder`, `Text`, `Padding`, `CornerRadius`, `BorderWidth`, `FontSize`,
`FontWeight`, `Size` (a fixed square), `MinWidth`, `MinHeight` and `Circular`. A size the
style does not name is left to the app — `.Width(Dimension.Percent(100))` on a button
survives the style pass — while padding, radius, font and border width are always the
theme's. `Circular` with a `Size` sets the radius up front, so the focus ring follows it;
without one, the radius is taken from the laid-out box at paint time.

**Hover and pressed are interaction styles**, the same mechanism as `.OnHover` and
`.OnPressed`, so `.Transition(TransitionProperty.Background, 90f)` on a button fades them
and an app's own `.OnHover` layers over them rather than fighting them. A theme change
mid-hover keeps the button lit, in the new colours.

**Disabled** (`IsEnabled`, `.Enabled(false)`): drawn at `ButtonTheme.DisabledOpacity`
over the app's own `Opacity`, dropped from the Tab ring, and its click is swallowed
before any handler runs — the app's or a row's behind it. It stays under the pointer, so a
tooltip can say why. Assistive technology sees it as disabled.

| Variant | Fill | Border | Label | For |
|---|---|---|---|---|
| `Primary` | `Primary` | none | `PrimaryText` | the one action a view is for |
| `Secondary` | none | `Border` | `TextColor` | every other button in a row of them |
| `Danger` | `Danger` | none | `DangerText` | deletes and discards |
| `Ghost` | none | none | `Primary` | toolbar and inline actions |

Under the pointer a filled variant darkens (`HoverDarken`, `PressedDarken`); an unfilled
one takes a wash of its own label colour (`HoverTint`, `PressedTint`), which reads as
"the same button, lit" on any ground.

**`ButtonTheme`** carries what every variant shares — `CornerRadius`, `Padding`,
`FontSize`, `FontWeight`, **`BorderWidth`**, `DisabledOpacity` — and one `ButtonStyle`
per variant (`Primary`, `Secondary`, `Danger`, `Ghost`). Null on a variant's style means
"what the theme and palette say", so a one-knob override keeps the rest, and a variant
can be reshaped as well as recoloured:

```csharp
app.Theme = Theme.Dark.With(new ButtonTheme
{
    BorderWidth = new Thickness(1.5f),
    Secondary = new ButtonStyle { Border = brand, Text = brand },
    Ghost = new ButtonStyle { Padding = new Thickness(3, 4, 3, 4) },   // tighter inline actions
});
```

**The border width is shared by every variant, and a variant with no visible border
carries it transparent.** Layout reads the width regardless of colour and a fully
transparent border is not drawn, so a button that changes variant — a tab going
active, a toggle going primary — never moves its neighbours. The same idea as
[Outline](LAYOUT.md#12-outline-not-a-conditional-border), applied inside one control.

`Label` reads and writes the text child. `ResolvedBackground`, `ResolvedBorder`,
`ResolvedText` and the hover/pressed pair expose what the theme resolved to, for tests.

The framework's own `Dialog.Confirm` and `Dialog.Alert` build their actions from this
element — cancel is `Secondary`, the confirming action `Primary`, and a destructive one
`Danger` — so a themed app's dialogs match its buttons without any work.
