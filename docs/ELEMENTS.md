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

### Tree

| Method | Description |
|---|---|
| `.Children(params Element[])` | Replace all children (fluent) |
| `.AddChild(element)` | Append a child |
| `.RemoveChild(element)` | Remove a child |
| `.ReplaceChildren(elements)` | Replace all children |
| `.Parent` | The parent element (read-only) |
| `.Children` | List of child elements (read-only) |

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

**Built-in interaction features:**

- Click to place caret
- Click-drag to select text
- Shift-click to extend selection
- Double-click to select a word
- Triple-click to select all
- Ctrl+A to select all
- Ctrl+C / Ctrl+X / Ctrl+V for clipboard
- Ctrl+Left/Right for word navigation, Ctrl+Backspace/Delete to erase a word
- Horizontal scroll when text overflows the box

A "word" means the same thing to every one of these as it does to a double-click:
letters, digits and `_` form one class, whitespace another, and punctuation and symbols a
third — so `feature/my-branch` and `src/core/Foo.cs` are several stops, not one. The rule
lives in `WordBoundary` and is shared by `TextInputElement`, `TextAreaElement` and
`TextElement`.

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
