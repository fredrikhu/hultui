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
- **A character that asks to be an emoji is drawn from the colour emoji face** (Segoe UI Emoji,
  Apple Color Emoji, fontconfig's emoji face — Noto Color Emoji on most Linux desktops), before
  the face's own glyph is used, as Unicode's emoji presentation rules say: a character that is an
  emoji by default (😀 ⭐ ☕), one followed by U+FE0F (❤️ ™️ ©️ #️⃣ 1️⃣), and the first of a skin-tone
  or joined sequence (☝🏽 👁‍🗨). Everything else stays text: a plain ™ © # 1, ❤ or ↔ without the
  selector, and any character followed by U+FE0E, which asks for text. Text faces have many emoji
  in black and white — DejaVu Sans has 😀 and ❤, Segoe UI has # © ™ — and used to win. A machine
  without a colour emoji face, and a pinned collection (golden images), fall back as before.
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

**A pointer event is made only for a listener.** A move, enter or leave event is created
only when an element it would reach has a handler for it. Hover styles (`.OnHover`),
cursors and tooltips need no event, so the pointer crossing a screen that only styles its
hover allocates nothing beyond the platform's own input record. A `PointerMoveEvent`
handler makes an event on every move over its element or anything inside it — on a root,
every move in the window — so attach a move handler to the element that needs it.

**Which inputs draw a frame.** An input draws a frame when it changes something: an element
marked itself dirty (a control's own state, a scroll, a hover or pressed style, an overlay),
the hover moved to another element, a drag is under way or has just ended, or the focus ring
moved, showed or hid. It also draws one when an app handler ran for an event the input
dispatched — the key, the press and the focus and blur it caused, the release and its
click, the wheel's `ScrollEvent` — on that event's way through the tree (`On<TEvent>`,
`.OnClick` and the other fluent ones). A handler may change what is drawn without marking
anything (see "Layout styles do not self-invalidate" in REACTIVITY.md; `HULTUI002` catches
the common case), so the window repaints after one to be safe. Nothing else draws a frame: a
key-up no handler hears, a wheel notch over a list that has nothing to scroll, a press on
static text. Until plan 17's round 4 every input but a pointer move drew one, and a key-up's
frame was 42–46 % of a key press's frame time in Junction and Vesper Commander.

So a handler that runs often and usually does nothing — a move tracker on a root that acts
only during a resize drag or a right-drag selection — belongs in `el.OnPassive<TEvent>(handler)`.
A passive handler promises to mark what it changes (`MarkDirty()` on its component,
`MarkPaintDirty()` or `MarkLayoutDirty()` on an element), and an input that ran only passive
handlers draws no frame of its own. A move handler that acts only while something is under
way can say so: `el.OnPassive<PointerMoveEvent>(handler, listening)` asks `listening` before a
move is made, and a move that nothing on its way listens for makes no event at all (64 bytes
a move otherwise, on every move over the element — over a root, over the window). Hand the
same delegate to every build, from a field (`_listening ??= () => _resizing`), or each build
makes one. Vesper Commander's two move handlers, the root's (the Find dialog's resize grip, a
right-button hold) and each file row's (a right-drag marking rows), drew a full frame for
every move over the panes as ordinary handlers: 60 idle moves along a row drew 60 frames.
Passive and listening only during their gesture, the same moves draw 1 (where the hover left
the file's name), and a move allocates 40 bytes instead of 104.
A callback a control calls
(`OnChanged`, `LinkActivated`, a toast's action) is not a handler: it shares the frame the
control's own change asks for, so one that changes something on screen while the control
itself changes nothing must mark it.

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
| `.AccessibleExpanded(open)` | `bool?` — `null` = cannot open |
| `.AccessibleSelected(selected)` | `bool?` — `null` = not one of a set |

`AccessibleSelected` is for one of a set the user picks from, such as a tab: a reader is told
it is selectable and whether it is the one selected, and selecting it from assistive tech
clicks it, so the element has to select itself on click. `TabsElement` sets it on its headers
(role `Tab`, in a strip of role `TabList`, beside a panel of role `TabPanel`).

`AccessibleName` is what a screen reader announces. `Name` is only its last-resort fallback — reach for `AccessibleName` on anything user-facing, and leave `Name` for debugging.

**A `BoxElement` with a click handler is announced as a button.** When no role is set, the
tree builder infers one from the element type, and the last rule is "a box with an
`OnClick` is a `Button`", named from the text inside it. That is right for a clickable
card and wrong for a row whose click merely selects it: give that an explicit
`.AccessibleRole(...)`. An explicit role always wins over inference.

**A backdrop is not a button when its card is a dialog.** A box with a click whose child
has the role `Dialog` is the dialog's backdrop: it gets no role and no name, so in your own
tree it is no node at all and the dialog is its parent's child; `Dialog.Show`'s scrim, the
root of its overlay, is an unnamed pane (UIA) or panel (AT-SPI) holding the dialog. The
click still closes the dialog, and Escape still does. For a modal you keep in your own
tree, give the card `.AccessibleRole(AccessibilityRole.Dialog).AccessibleName("…")` and put
it directly in the backdrop. Without that the backdrop is a button named after all the
card's text: a reader hears the whole modal as one button, told again whenever any text in
it changes, and every such change builds the window's whole accessibility tree, 260 KB per
change over 1,000 rows with UIA (1,058 KB with AT-SPI), where the card's label is otherwise
patched for about 1 KB (plan 17, round 6). The rule looks at the backdrop's children only,
not further down.

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

### Opacity and what an element paints

`.Opacity(value)` below 1 draws the element and its subtree into an offscreen group and
blends the group once, so overlapping children do not show through each other. The group
is sized to what the subtree paints — each element's `PaintBounds`, unioned down the tree
until a clip (`Overflow.Hidden`, a scroll view) cuts the children off. A plain `Box` with
no children and one fill skips the group and fills in a fainter colour.

`PaintBounds` is the border box unless the element says otherwise. `BoxElement` adds its
shadows and outline, `TextElement` the ink of text longer than its box. **A custom element
whose `Render` paints outside its border box must override `PaintBounds`** — whatever falls
outside it is cut off under opacity. Return `null` when the extent is not known cheaply:
the group is then as large as the clip, which is always right and, on a CPU surface, slow
(a window-sized image per faded element). The framework's controls with focus rings or
thumbs past their box (checkbox, slider, toggle and others) return `null`.

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
| `Masked` | `.Password()` | `bool` | `false` |
| `Spans` | `.Spans(spans)` | `IReadOnlyList<TextSpan>?` | `null` |
| `TextChanged` | `.OnTextChanged(handler)` | `Action<string>?` | `null` |
| `Clipboard` | -- | `IClipboard?` | Set by Application |
| `CanUndo`, `CanRedo` | -- | `bool` (read-only) | |

**`Masked`** makes a password field. The value is drawn as bullets — while an input method
composes, its preedit shows among them, the rest stays bullets — and everything about the
field's geometry is measured on the bullets too: the selection highlight, where a click puts
the caret, how the arrow keys step, and the width a field without one of its own grows to.
Measured on the text, each of those would trace what the bullets hide. For the same reason the
value is one word, as a browser's password field is: Ctrl+Left/Right go to the ends,
Ctrl+Backspace/Delete erase to them, a double-click selects it all, and Ctrl+Z takes a run of
typing back whole rather than a word at a time. Focused, it asks the platform for password
input (`TextInputKind.Password`): on Wayland the input method is told the field is a password;
on Windows, where SDL ignores that, the window is left with no input context while the field
has the keyboard, as a native password box does — no composition, nothing learned. A screen reader is told
the field is a password (UIA's `IsPassword`, AT-SPI's `password text` role) and is given the
bullets as its value, never the text; and Cut and Copy do nothing — the keys and the menu
items alike, greyed out in the menu — because any application can read the clipboard. Paste
still works, and `Text`, `TextChanged`, the caret and the selection all work on the real
value. Its fluent setter is `.Password()` (`.Password(false)` clears it), named unlike the
property for the reason `.Chromeless()` is. It follows a rebuild like every other property,
which matters most here: unkeyed siblings are matched by position, so when a field before it
goes away the live element that becomes the password field may be one that was built unmasked.

**`Spans`** colours ranges of the value without changing it (mask-token highlighting, say),
the same `TextSpan` that `TextElement` and `TextAreaElement` take. They are not drawn while
the field is masked or an input method is composing.

`Undo()` and `Redo()` do what Ctrl+Z and Ctrl+Y do, for an Edit menu or a toolbar button;
both return false when there was nothing to do.

**Built-in interaction features:**

- Click to place caret
- Click-drag to select text
- Shift-click to extend selection
- Double-click to select a word
- Triple-click to select all
- Ctrl+A to select all
- Ctrl+C / Ctrl+X / Ctrl+V for clipboard (only Ctrl+V on a `Masked` field)
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
| `VerticalWheelScrollsSideways` | -- | `bool` | `false`; `true` from `ScrollView.Horizontal()` |
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

**Nesting.** A scroll view takes the wheel axes it can move on and hands the rest to the
scroll views around it, so nested views work for both axes and a list at the end of its
range hands the next gesture to the page. A gesture stays with the view it started on
while the events keep coming, Shift turns a vertical wheel sideways, and a wheel over an
overlay is the overlay's. A horizontal view (`VerticalWheelScrollsSideways`) turns a
vertical wheel it cannot use into sideways motion, as Windows' list view does — but only
when no scroll view around it can still move that way, so a strip inside a scrolling page
never traps the wheel. An app's own `ScrollEvent` handler on a scroll view runs after
the view's, and sees what was used through `ConsumedX`/`ConsumedY`. The rules, and how to
take part from your own code, are in
[LAYOUT.md — Nesting scroll views](LAYOUT.md#nesting-scroll-views). A *virtualized* list
still cannot scroll sideways itself; wide lists use a pan (below and there).

**Reveal before layout.** `ScrollRangeIntoView(top, height)` scrolls the minimum
distance to show a band of content, and needs the viewport height to do it. Called
before the view has been laid out (from `OnAfterBuild`, say — that runs before the
layout pass), it holds the request and applies it when the extent is next computed,
so a reveal asked for on the same frame the content appeared is not lost.
`ScrollIntoView(child)` and `ScrollRangeIntoViewHorizontal(left, width)` are the
element and sideways forms.

**`ScrollSidewaysWith(ISidewaysScroller)`.** Set when the horizontal offset is owned by
something other than the view's own `ScrollOffsetX` (a `HorizontalPan`). The wheel's
sideways motion (a trackpad swipe, Shift+wheel) and `ScrollBy`'s `dx` go there instead,
and autoscroll asks it whether sideways travel is possible. A panned list without this
cannot be scrolled sideways by anything but its scrollbar.

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

**Still images.** `SkiaImageSource` is the Skia renderer's source for a picture that does not
change: `FromFile`, `FromBytes`, `FromPixels`, or `new SkiaImageSource(bitmap)` for an
`SKBitmap` you decoded or drew yourself. The source takes the bitmap over. It marks it
immutable so the image it draws shares the bitmap's pixels instead of copying them (a copy
would hold a 3840×2400 photo twice, 35 MB more), and it disposes the bitmap when it is
disposed. Finish drawing into the bitmap before you hand it over, and do not change or
dispose it afterwards: hand over a `Copy()` if you want to keep drawing.

```csharp
var bitmap = new SKBitmap(64, 64);
using (var canvas = new SKCanvas(bitmap))
    canvas.Clear(SKColors.Teal);                  // all drawing first
var source = new SkiaImageSource(bitmap);          // then hand it over
var picture = Image.New(source).Size(32f, 32f);   // dispose `source`, not `bitmap`, when done
```

**Decode at the size you draw.** A still image costs its decoded pixels, four bytes each,
whatever size it is drawn at. Decode it, or ask a server for it, at the device pixels its box
covers: the layout size times `Window.DisplayScale`. A fixed "twice the layout size" holds
four times the pixels it draws at 100 % (a 168 × 252 poster: 677 KB instead of 169 KB), and
drawing it smaller also makes Skia build a mipmap chain for it. For `ImageFit.Cover`, size it
by the side that decides the fit (the height, for a picture wider than its box), so it still
has a pixel for every pixel drawn. `DisplayScale` changes when
the window moves to a monitor with another scale, which arrives as `Window.Resized`: ask
again at the new size then, key any cache by the size asked for, and drop what was decoded
for the old scale once the new pictures are in. Aperture's `PosterCache` does all three. A
picture whose box follows the window (a backdrop across a page) passes through every width
while the window is dragged: round its size up to a step (Aperture uses 128 device pixels),
so it is asked for again only when the window passes one, keep drawing the old picture until
the new one is in, and then drop the old one.

**Dispose a picture on the UI thread.** Decode wherever you like, but an element that holds
a source can draw it on any frame, and only the UI thread draws. Disposed on another thread,
its native image can be freed in the middle of a draw. A cache that lets pictures go where a
download lands, on the thread pool, should hand them to the UI thread instead: Aperture's
`PosterCache` keeps what its count cap pushes out until the UI thread next asks it for a
picture, and disposes them then.

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
the pane's size as app state and reports it as it moves (the way `HorizontalPan` holds
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
| `AttachDrag(root)` | hears the move and release on an ancestor; passive, so an idle window does not repaint per pointer move, and asks for moves only while dragging, so an idle one makes no move event either |
| `Restore(size)` | set from remembered settings; no callback |
| `IsDragging` | true while grabbed |
| `SplitterAxis.Vertical` (constructor) | stacked panes: the handle is a horizontal strip dragged up and down |

`onChanged` follows a drag once per frame that may be drawn, at the pointer's latest
position, and the release applies the last one (see "A drag rebuilds once per frame" in
REACTIVITY.md); `onCommitted` runs once per drag (and per key step), so a
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
| `.RenderWith(Func<T,bool,Element>)` | `ItemRowTemplate` | builds a row from its item and whether it is selected, and nothing else; the list keeps a realized row until one of those changes — see **Rows that show only their item** below |
| `.SelectedIndex(i)`, `.CanSelectMultiple()`, `.OnSelectionChanged(h)` | `SelectedIndex`, `CanSelectMultiple`, `SelectedIndices` | built-in selection, keyboard included |
| `.OnActivateItem(h)` | `OnItemActivate` | called when assistive technology invokes or selects a row through the accessibility bridge; pointer and keyboard activation are the row template's own `OnClick` / `On<KeyDownEvent>` handlers |
| `.KeyboardScroll(bool)` | `KeyboardScrollEnabled` | arrows / PageUp / PageDown / Home / End |
| — | `RowsAreComposite` | one item is a row of controls (a wrap grid), so the realized rows answer to assistive tech rather than one node per item — see **A row that is not one thing** below |
| — | `InvalidateHeights()`, `InvalidateRows()` | after a change the list cannot see; with `ItemRowTemplate`, whenever anything a row shows besides its item and selected state changes |
| — | `InvalidateRow(index)` | `InvalidateRows()` for one item: its row (if realized) and its kept label are made again, every other row is kept — for something only a row or two show that moves (see **State that only some rows show** below) |
| `.SelectionHighlight(fill, radius, ring, width)`, `.SelectionHighlight(RowHighlight?)` | `SelectionHighlight` | the list draws the selection itself, under each selected row on screen, and makes its rows unselected, so a selection move makes no row — see **A selection the list draws** below |

**How rows live.** With the one-argument template, the visible window plus a row or two of
overscan is rebuilt on every patch — the template is re-invoked, so a rebuild of the owning
component always shows current data with no reference tricks. A row with a `Key` is retained across passes and
patched instead of replaced, which is what keeps focus, a caret or a scroll offset alive
inside it; key rows that hold state. Rows are force-sized to their height and
`FlexShrink(0)`, so a template that wants a different height must say so through
`RowHeights`. A row inherits text style from the list like any other child: a `.FontSize()`,
`.TextColor()` or `.Cursor()` set above the list reaches every row that does not set its own.
(Until 2026-10-12 a row from the one-argument template was resolved under the theme's
defaults while it was made, and the next style pass resolved it again under the list,
which changed its font size and laid out every box and icon of every row a second time:
Junction's Branches walk laid out 255 elements a key, now 34.)

**Rows that show only their item.** Making every visible row again on every rebuild is
what lets a row show anything, and it is most of what a key walk costs: in a list whose
owner rebuilds on each selection move (Junction's History), twenty rows are made to change
the look of two. `.RenderWith((item, selected) => Row(item, selected))` is the promise that
a row shows its item and whether it is selected, and nothing else. The list then keeps a
realized row across rebuilds while the item at its index is still equal to the one it was
made from (`EqualityComparer<T>.Default`: the same object for a class, equal values for a
record or struct — a reload that reads the same data keeps its rows) and its selected
state (`SelectedIndex`, or the multi-selection with `CanSelectMultiple`) is unchanged. A
selection move makes two rows; a rebuild that changes neither makes none. A kept row is
the same element, so it keeps its hover, its focus and its layout.

```csharp
var list = VirtualListElement<Commit>.New()
    .Items(commits)
    .SelectedIndex(selected)                       // what `selected` below follows
    .RenderWith((c, selected) => CommitRow(c, selected));
```

The contract has a second half. Anything else a row shows — data that arrives later for an
unchanged item (counts, a CI status), a setting, the window width, a hover or focus tint
the owner draws itself, a value captured from the build — is invisible to the list, and the
kept rows go on showing what they showed when they were made, until they scroll out and back
in. When such a thing changes, call `InvalidateRows()` on the mounted list (through its ref,
at the top of the owner's build or after it): every row is made again from the latest
template. A convenient shape is a tuple of what the rows read, compared with the last build's:

```csharp
var shape = (stats, compact, Nocturne.Theme);   // what the rows show besides the commit
if (shape != _rowShape) { _listRef.Current?.InvalidateRows(); _rowShape = shape; }
```

Switching `Application.Theme` to another `Theme` object makes the rows again by itself, and so
does a change of the window's display scale (a move to a screen at another scale); a theme
changed in place does not. The scale matters because a glyph cut for it
(`Phosphor.Draw(…, scale)`, an app's own icon cache) is retired when the cache is invalidated
for the new scale and freed at the next invalidation: a kept row holding one drew a freed
raster, and Junction's branch rows lost every icon after a move to 150 % and back, until the
list made its rows again on a scale change. The window tells the list before its `Resized`
event, so an owner that moves its cache to the new scale there has the rows made with the new
glyphs. Handlers in a kept row are the ones it was made with, so
they should read live state (`SelectionContains(c.Hash)`) rather than a value the build
captured. Rows are matched by index: an insertion above the viewport makes the rows it moves
again, as before. The one-argument template keeps its behaviour exactly; nothing changes for
a list that does not opt in. On the perf suite's selection walk the opt-in makes 3 rows per
step instead of 19 and allocates 26 KB instead of 97.

**State that only some rows show.** A second highlight beside the selection (the open file
under a tree's cursor, the branches picked for a comparison), a folder's open state: these
change one or two rows at a time, and in the shape every change would make every row on
screen again. Put them in the item instead. The item can be a small record struct — the
row's model and those flags — made per index by an `IReadOnlyList<T>` view over the rows,
so the list compares the rows on screen and makes again exactly those whose flags moved,
with nothing allocated per row and no list built per rebuild:

```csharp
internal readonly record struct FileRowItem(TreeNode<string> Node, bool Open, bool Cursor, bool Expanded);

sealed class FileRowItems(App app, List<TreeNode<string>> rows) : IReadOnlyList<FileRowItem>
{
    public int Count => rows.Count;
    public FileRowItem this[int i] => new(rows[i], rows[i].Path == app.OpenPath,
        rows[i].Path == app.CursorPath, app.Tree.IsExpanded(rows[i]));
    // GetEnumerator: yield this[i] for each i
}
```

The model objects themselves have to be kept between rebuilds for the comparison to hold:
nodes made again by every build (a tree flattened from a fresh filter) are new objects at
every index, and every row is made again. Keep them while what they come from is the same.
Junction's Files tree, moved from a scroll view of every row onto a virtual list this way,
makes 2.3 rows a key in a search of 2,300 files (28 MB a key before, 0.28 MB now).

When the items are records the list is handed as they come (a folder's entries straight from
the loader) and the highlight is one value that moves, the app can instead tell the list the
rows it moved between: `InvalidateRow(index)` makes that one row again (and lets go of its kept
label), and keeps every other. Call it for the row the highlight left and the row it reached,
through the list's ref, before the rebuild patches the list — the same place `InvalidateRows()`
goes:

```csharp
if (partner != _partnerWas && _listRef.Current is { } list)
{
    list.InvalidateRow(IndexOf(rows, _partnerWas));   // -1, or past the items, does nothing
    list.InvalidateRow(IndexOf(rows, partner));
}
_partnerWas = partner;
```

Vesper rings the file the other pane's cursor would be compared with, and in two folders of the
same names that ring moves with every key; in the shape it made every row of the pane again.
With `InvalidateRow` a key makes about 3.5 rows across both panes instead of 31.6 (same-name
walk, headless: 427 → 259 KB a key). The perf suite pins it: `virtual/partner walk beside 1,000 item
rows, every row told` makes 23 rows a step and allocates 105 KB, `… two rows told` makes 6 and
allocates 39 KB.

**A selection the list draws.** On item rows a selection move still makes two rows, the one
the selection left and the one it reached, when often all that changes in them is their
background. `.SelectionHighlight(fill, cornerRadius, ringColor, ringWidth)` has the list draw
the selection itself: a box at each selected row's border box (every selected row on screen
with `CanSelectMultiple`), under the row and before it is drawn, through the same call a box
makes for its own background and uniform border (`RowHighlight`), so it snaps and rounds the
same way. The rows are then made unselected — the template is asked with `selected = false` —
and a selection move makes no row at all: the list only draws the highlight somewhere else.

```csharp
var list = VirtualListElement<Tag>.New()
    .Items(tags)
    .SelectedIndex(caret)
    .SelectionHighlight(Nocturne.Neutral900, Nocturne.RadiusRow)   // what the rows' background was
    .RenderWith((t, _) => TagRow(t));                               // no background of its own
```

The pixels are the same as rows that drew it when the highlight is the fill, radius and ring the
row drew and the row's own background becomes transparent (a row with a ring keeps a transparent
border of the ring's width, so its contents stay where they were). It fits only a selection that
is a background. A row that shows its selection any other way cannot show it any more: a bolder
label, an icon's colour, something only the selected row has (Junction's History names what
Enter will do on the caret's row), or a hover tint the row leaves off its selected row (a kept
row has no way to learn it became selected, so its hover tint would cover the highlight). Such a
list keeps the selection in its rows. Assistive technology reads the list's selection, not the
rows', so it is told the same either way. Another highlight from a rebuild (a theme's colour) is
drawn on the next frame and makes no row; turning it on or off makes the selected rows again.
`.SelectionHighlight(null)` leaves the selection to the rows. On the perf suite's walk a step
makes 1 row instead of 3 and allocates 16 KB instead of 25 (`virtual/selection walk 1,000 item
rows, the list draws the highlight`); Junction's Tags table, whose caret's tint is the list's,
makes 1.5 elements a key instead of 45.5 and allocates 41 KB a key instead of 87.

The items' accessible labels follow the same contract. The accessibility tree asks for every
item's label each time it is built — with a reader listening, after every structural change
and every scroll — so an item-rows list keeps each item's label while the item is equal to
the one it was made from and `AccessibleItemLabel` is the same delegate; `InvalidateRows()`,
new `Items` and another delegate let them all go. Pass a label that reads only its item as a
lambda that captures nothing, or keep the delegate in a field: a lambda that captures the
build's state is a new delegate every build and keeps nothing (which is right for a label
that reads that state, unless the state is in the shape you invalidate on). On the walk with
a recording bridge listening, keeping the labels takes a step from 274 to 112 KB.

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
a drag or a key. The video scrubber's preview is what this exists for. `OnHoverValue`, and
`OnValueChanged` during a drag, follow the pointer once per frame that may be drawn, at its
latest position; the release applies the last value at once (see "A drag rebuilds once per
frame" in REACTIVITY.md).

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
| `Spans` | `IReadOnlyList<TextSpan>?` | syntax colouring; backgrounds only for search hits so grammar colour survives. Sorted by start, may overlap (a tint over a token). Indexed when first drawn and kept by reference: hand a new list to change them, and the same list each build when they have not changed |
| `ScrollOffsetY`, `ScrollSpeed`, `Scrollbar*` | | vertical only |
| `TextChanged` | `Action<string>?` | |

**Wrapping is unconditional.** Every hard line wraps at the content-box width; there is
no wrap switch, and the wrapped rows are cached against the text, the width and the font
they wrapped with, so a resize or another font size re-wraps with nothing for you to do,
and a rebuild that hands the area the text it already shows wraps nothing. The corollary
is that there is never anything to the right to scroll to: `ScrollBy` ignores `dx`, and
autoscroll is vertical.

**Long documents open faster in a monospace face.** To wrap, the area needs to know which
lines fit, and it measures each line it has no width for — every line, when a document
opens. In a face the renderer proves monospace for printable ASCII (`FontFamilies.Monospace`
on every platform, and any face whose 95 printable ASCII characters all advance alike, kern
no pair and join none), a line of those characters that fits with a character to spare is
taken as fitting without being measured. A 19,000-line JSON response opens in a tenth of the
time, with 86 % fewer bytes, and keeps 4 MB less afterwards. Give logs, JSON, source code and
other machine text a monospace face; letter spacing, a tab or any character outside printable
ASCII on a line measures that line as before, and the rows are the same either way.

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

---

## 19. FindBar and FindBarElement

Find in the text on screen: a field over a `TextSearch`, its match count, the match-case and
regular-expression chips, and previous / next / close — plus the keys and the after-build work
that make them behave. App state, like `ToastHost`: one per searchable view.

```csharp
// Once, in the component:
_find = new FindBar(MarkDirty)
{
    Placeholder = "Find in file…",
    Body = () => _bodyRef.Current,          // gets the keyboard back on close; a reveal waits for it
    Reveal = RevealCurrentMatch,            // scroll _find.Search.Current into view
    Selection = SelectionInBody,            // Ctrl+F searches for it, starting at it
    LetterKeys = true,                      // n / p walk the matches when no field has the keyboard
};
_find.Search.Bind(lines.Length, lanes: 1, (line, _) => lines[line]);   // when the text changes

// The view's key handler, before its own shortcuts:
if (_find.HandleKey(e)) { e.StopPropagation = true; return; }

// In Build():
if (_find.IsOpen) column.AddChild(_find.Build());

// In OnAfterBuild():
_find.Service();
```

| Member | Meaning |
|---|---|
| `Search` | the `TextSearch` — tint lines with `Search.Decorate`, rebind with `Search.Bind` |
| `IsOpen`, `Open()`, `Close()`, `Next()`, `Previous()` | what the keys and buttons do |
| `HandleKey(e)` | Ctrl+F, Escape, Enter / Shift+Enter, Alt+C, Alt+R, and `n` / `p` with `LetterKeys` |
| `Service()` | focus the field, give the body the keyboard back, reveal the current match |
| `RevealCurrent()` | ask for a reveal at the next `Service` without rebuilding |
| `Body`, `Reveal`, `Selection`, `Opening` | the owner's hooks |
| `RevealAfterLayout` (true) | false when `Reveal` defers itself (`ScrollRangeIntoView`) and can land on the same frame |
| `Placeholder`, `FieldWidth` 280, `FieldHeight` 30, `Icon` | the field and the glyphs |

**One build a step.** `onChanged` runs once for each step — a key, a button, opening, closing —
however much of the search the step changed: the search the bar makes tells the owner through
the bar, which holds it for the step's own notice. Until round 5 of plan 17 a step let the search
call `onChanged` and then called it again, two builds for every Enter or F3 and three for Ctrl+F
over a selection, the first before the step had asked for its reveal (Vesper's reader, find Enter:
two builds a step → one). Don't make the bar's `TextSearch` call `MarkDirty` itself too.

**Keep the current match out of the rows' shape.** A body on item rows tints its matches with
`Search.Decorate`, the current one in a colour of its own. What every row shows — the query, its
options, the number of matches — belongs in the shape that makes every row again
(`InvalidateRows()`, see **Rows that show only their item**); the current match does not: only
two rows show that it moved. Tell the list the line it left and the line it reached, at the top
of the build, as `InvalidateRow` is told any highlight that moves:

```csharp
var s = _find.Search;
var hit = s.Index >= 0 && s.Index < s.Hits.Count ? (s.Index, s.Hits[s.Index].Line) : (-1, -1);
if (hit != _hitWas && _listRef.Current is { } list)
{
    list.InvalidateRow(_hitWas.Line);   // -1 does nothing
    list.InvalidateRow(hit.Line);
}
_hitWas = hit;
```

The index and the line, not the line alone: two matches on one line move the tint within it.
Vesper's reader had the index in its shape, so every Enter, F3 or `n` made every row on screen
again: on a 3,000-line file 35 rows and 330 KB an Enter, 2.5 rows and 69 KB now, with the bar's
own rebuild made view-only (an owner with kept parts gives the bar the rebuild that leaves them:
nothing a part shows moves with a find step, and a full one built Vesper's title bar and command
line again on every Enter). Headless, JIT; NativeAOT 326 → 68 KB (plan 17, round 6).

`new FindBar(search, onChanged)` puts a bar over a search something else owns — a terminal
searches its own scrollback. That search tells its owner through its own callback, which the bar
cannot hold back, so give it one that does not rebuild what `onChanged` rebuilds (the terminal's
repaints and raises an event). The bar handles its own keys while its field has the keyboard, so
an owner with nothing else to route can skip `HandleKey`; routing it as well is harmless.
Ctrl+F on an open bar puts the keyboard back in the field with the query selected whole. Keys
match the letter on the key (`LayoutKey`), as the text fields' shortcuts do.

**Reveal timing.** `OnAfterBuild` runs before layout, so by default the reveal goes through the
window's post-frame callback, once the body is in the tree — a ref keeps pointing at an
unmounted body, and a reveal spent on that is lost.

**Looks** come from `Theme.FindBar` (`FindBarTheme`): fill, divider, padding, gap, the field's
`TextInputStyle` and face, the count's face, size and colours (an error, or "no matches", in
`StatusErrorColor`), the chips' `ButtonStyle` off and on, and the buttons' `ButtonStyle`. Null
colours come from the palette. `Icon` draws the magnifier and the three button glyphs from the
app's icon set; unset, or null for a glyph, the buttons show ↑ ↓ × and there is no magnifier.
`FindBarElement` is the row: it applies the theme to its parts each style pass, finding them by
their automation ids — `find-input`, `find-status`, `find-case`, `find-regex`, `find-prev`,
`find-next`, `find-close` — which tests can use too.

**The count** (`FindBar.Status`): "3 of 41"; "20,000+" when the scan stopped at
`TextSearch.MaxHits`; the pattern's error when it does not compile or ran too long; "no matches".

---

## 20. CommandPalette

A query field over everything the app can do, narrowed as the person types and run with Enter
or a click — a modal dialog near the top of the window. It replaced three apps' own palettes.

```csharp
_palette = new CommandPalette(Commands)
{
    ToggleKeys = shortcuts["palette"].Keys,      // the key that opened it closes it
    GroupStyle = PaletteGroupStyle.Trailing,     // a kind tag after the title (Leading: a column)
    SearchIcon = () => Icon(Icons.MagnifyingGlass),
    Footer = "Repositories · Branches · Actions",
};
if (shortcuts.Matches("palette", e)) _palette.Toggle(window);

IEnumerable<PaletteCommand> Commands() =>
[
    new("Push", DoPush) { Keys = shortcuts.Keys("push") },
    new("Checkout tidy", () => Checkout("tidy")) { Group = "Branch", Detail = "feature/cleanup" },
];
```

| `PaletteCommand` | |
|---|---|
| `Title`, `Run` | the row and what choosing it does — the palette closes first |
| `Group` | kind or category: a tag, a column, or nothing (`GroupStyle`); searched |
| `Detail` | a second line — a path, a count, where it leads; searched |
| `Keys` | the key chip; read it from the app's `ShortcutMap` so it cannot go stale |
| `Icon`, `Keywords` | a leading element (built per row), and words it is found by but does not show |

- **The list is taken when it opens**, so a provider can count things or ask a repository; it
  is not called per keystroke.
- **Matching** is `FuzzyMatch.Score` (subsequence, "gtr" finds "Go to Runner") on the title,
  then on the group, detail and keywords, ranked after every title match. Ties go to the shorter
  title, then list order. `CommandPalette.Filter` is the same ranking, for tests.
- **Keys:** ↑ ↓ and Page Up/Down move, Enter runs, Esc or a click outside closes, and so does
  any of `ToggleKeys` — the dialog is modal, so the app's handler never sees them. The chosen row
  is kept in view. `MaxRows` (100) rows show; the rest are counted.
- **State:** `IsOpen`, `Query` (settable), `Matches`, `SelectedIndex`, `Move`, `Run`, `Close`,
  and `Closed`, raised however it closes — at once, not a frame later.
- **Accessibility:** one dialog named `AccessibleName` ("Command palette"); rows are buttons
  named "Title (Group)". Automation ids: `palette`, `palette-card`, `palette-input`,
  `palette-row-N`, `palette-no-match`.

**Looks** come from `Theme.CommandPalette` (`CommandPaletteTheme`): width, maximum height, offset
from the top, scrim, card, rules, row heights (one line and two), colours of the chosen and
hovered rows, the title, detail and group. The card fits a short list and scrolls a long one.
It shows through `Dialog.Show` with `DialogOptions.TopOffset`, which any dialog can use to sit
under the top edge rather than in the middle.

## 21. Kbd and KbdElement

`Kbd.New("Ctrl+P")` is a key chip — the keys in a small outlined box — for anywhere a key is
named. Its look is `Theme.Kbd` (`KbdTheme`): fill (transparent for an outline only), outline,
text colour and face, size, padding and radius; null colours come from the palette.

## 22. MenuBar

A row of menus across the top of a window — File, Edit, View — whose dropdowns are context
menus (`ContextMenuItem`: separators, disabled and danger items, check marks, icons, shortcut
hints, one level of submenus).

```csharp
_menus = new MenuBar(
[
    new("&File", [new() { Label = "Open…", ShortcutHint = keys.Keys("open"), Action = Open }]),
    new("&View", () => [new() { Label = "Word wrap", IsChecked = _wrap, Action = ToggleWrap }]),
]);
titleBar.AddChild(_menus.Mount());
// On the element given to SetRoot, where keys land when nothing has the focus:
host.On<KeyDownEvent>(e => { if (_menus.HandleKey(e)) e.StopPropagation = true; });
host.On<KeyUpEvent>(e => _menus.HandleKeyUp(e));
```

- **Titles:** `&` marks the Alt letter (`&&` is an ampersand). Items given as a function are
  asked for on every open, so checks and disabled states follow the app.
- **Pointer:** a press opens a menu, another closes it; while one is open, moving to another
  title opens that one. Labels are not hit-testable, so a bar inside a `WindowDrag()` title bar
  opens rather than dragging the window.
- **Keyboard (the Windows model):** F10, or Alt pressed and let go alone, gives the bar the keys:
  ← → between titles, ↓ ↑ Enter Space open, a title's letter opens it, Esc / F10 / Alt leave.
  Alt+letter opens a menu from anywhere. In an open menu ← → move to the neighbouring menu when
  there is no submenu to close or open; Esc goes back to the titles. Mnemonics are underlined
  while Alt is held or the bar has the keys. Focus returns to what had it. Right Alt reported
  as AltGr is ignored.
- **State:** `OpenIndex`, `HighlightIndex`, `IsActive`, `Open(i)`, `Close()`, `Menus` (settable).
- **Accessibility:** roles `MenuBar` and `MenuItem` (new; UIA MenuBar/MenuItem, AT-SPI
  menu bar/menu item); a title reports `Expanded` while its menu is open. Automation ids
  `menubar-N`.

**Looks** come from `Theme.MenuBar` (`MenuBarTheme`): fill, divider, height, padding, title
padding and radius, font size, text colour, hover and open fills. The menus take
`ContextMenuTheme`.

**For other menu owners:** `IOverlayHost.OpenMenu(items, position, MenuRequest)` opens a context
menu that reports why it closed (`MenuCloseReason`: Chosen, Escape, Dismissed, Replaced) and
hands back ← → (`Navigate`) when it has no use for them; `HighlightFirst` starts on the first
item for a menu opened from the keyboard. `CloseMenu()` closes it.

## 23. Expander

A header that shows and hides a body below it: a chevron, a title, optionally actions at the
end of the header, and the content. Click the header, or Enter or Space on it, and the body
slides open while the chevron turns.

```csharp
// Keeps its own state, across rebuilds too:
Expander.New("Advanced options", Flex.Column(gap: 8).Children(followLinks, hidden, pattern))

// The app's state, a body built only while open, and an action that does not toggle:
Expander.New("Recent searches", () => BuildRecent())
    .Expanded(_recentOpen).OnExpandedChanged(v => _recentOpen = v)
    .Actions(Button.New("Clear").OnClick(_ => ClearRecent()))
```

- **State:** left alone, the element keeps it — a rebuild does not close an expander the user
  opened (`DefaultExpanded()` starts it open). `Expanded(bool)` hands it to the app, as
  `Tabs.Selected` does: every build says open or closed, so pair it with `OnExpandedChanged`,
  which reports the user's toggles either way. `IsExpanded` sets it from code without a report.
- **Body:** a closed body is out of the tree — not laid out, not reached by Tab, not read. Given
  as an element it is kept while closed and still patched by rebuilds, so whatever lives in the
  element (a text box's undo history, a scroll offset) is there when it opens again. Given as a
  function it is built only while open and dropped on closing. A body that closes with the
  keyboard in it hands the keyboard to the header.
- **Header:** `Header(element)` shows any element in place of the title text (an icon and a
  second line); the title still names it. `Actions(...)` go at the end of the header row and do
  not toggle it.
- **Motion:** the body's height and the chevron's turn move together over
  `TransitionSeconds`; a reversal mid-way takes the share of the time left. Off screen, or at
  zero seconds, it snaps. `Progress` (0–1) says how far open it is drawn.
- **Accessibility:** the header is a button with the expanded state (UIA ExpandCollapse;
  AT-SPI expandable/expanded). Showing only the title it is named by that text, which is then
  not read again inside it; with a header of the app's or actions, by the title. An
  `AutomationId` set on the expander is the header's.

**Looks** come from `Theme.Expander` (`ExpanderTheme`). The default is a disclosure: the chevron
before the title (right when closed, down when open), a hover fill, the body lined up under the
title. `Chevron = Trailing` puts it at the far end (down, then up); `Border`, `HeaderBackground`
and `BodyBackground` make it a card whose header and body share the border and are joined by a
rule, the header giving up its bottom corners while open — the Fluent sample's is one.

## 24. Popover

A card of any content that opens next to the element it belongs to — a filter panel under a
toolbar button, recent folders under a caret — and closes on a press outside it or Esc. Not
modal: the rest of the window stays live.

```csharp
_filters = new Popover(() => Flex.Column(gap: 8).Children(query, onlyMine, applyButton))
{
    Placement = PopoverPlacement.Below, Alignment = PopoverAlignment.End,
};
// In the build: toggle from the anchor's click, and say whether it is open.
Button.New("Filters").OnClick(e => _filters.Toggle(e.CurrentTarget)).AccessibleExpanded(_filters.IsOpen)
```

- **Content:** the function is asked when it opens, on `Refresh()`, and whenever reactive state
  it reads changes; the result is patched into the open card, so focus and what is typed stay.
  A row that has done its work calls `Close()`.
- **Placement:** `Placement` (Below, Above, Right, Left) and `Alignment` (Start, Center, End),
  a theme gap from the anchor. It flips to the other side when it does not fit there and fits
  better on the other; slides along to stay in the window; is placed again every frame, so it
  follows an anchor that scrolls or a window that resizes; and closes if a rebuild drops its
  anchor (key the anchor if its siblings can come and go — by position, a removed sibling
  before it hands its element to the next one). `ActualPlacement` says which side it took.
- **Closing:** a press outside closes it and goes on to whatever was pressed; a press on its
  own anchor only closes it, so the toggle does not reopen it. Opened from a dialog, a press
  elsewhere in the dialog counts as outside. Esc closes it. `Closed` runs however it closed.
- **Keyboard:** opening moves the keyboard to its first control (the card itself when it has
  none), Tab stays inside, and closing with the keyboard inside hands it back to the anchor.
  `ArrowKeys = true` lets ↑ ↓ walk the controls, for a list of rows. With nothing focusable in
  it, the card holds the keyboard, so a key handler on the content never sees a key.
- **Accessibility:** the card is a dialog named by `AccessibleName`, else by its anchor; the
  anchor reports expanded while it is open.

**Looks** come from `Theme.Popover` (`PopoverTheme`): fill, border, corner radius, padding,
shadows, the gap to the anchor and the margin kept from the window's edges.

**Under it**, for other anchored surfaces: `IOverlayHost.ShowAnchoredOverlay(content, anchor,
place, …)` places an overlay with `place(contentSize, windowSize)` every frame, and swallows a
press on the anchor that closes it. `Element.WindowBorderBox` / `WindowContentBox` say where an
element is in the window, overlay or not — what anything placed in the window against it wants
(another overlay, the focus ring, a synthesized click). `ScreenBorderBox` stays in the space the
element's own pointer events arrive in, which inside an overlay is the overlay's; dropdowns, date
pickers and tooltips used it to place their popups, so inside a popover they opened as far off as
the popover is from the window's corner.

## 25. TableView (columns over a virtual list)

The stateful half of a table, like `TreeView<T>`: columns, which are shown and how wide, and
how the rows sort. It builds the header and lays each row's cells on the header's grid; the app
keeps the list, the selection, and anything else its rows do.

```csharp
_files = new TableView<FileRow>(
[
    new() { Id = "name", Header = "Name", Fill = 1, CanHide = false, Text = f => f.Name,
            Compare = (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) },
    new() { Id = "size", Header = "Size", Width = 90, Align = ColumnAlign.End, Text = f => Size(f),
            Compare = (a, b) => a.Size.CompareTo(b.Size), FirstSort = SortDirection.Descending },
], MarkDirty) { Group = f => f.IsDir ? 0 : 1, StateChanged = () => Save(_files.State) };

// In the build:
Flex.Column(gap: 0).Children(_files.Header(), _files.Body(rows).FlexGrow(1))
```

- **Columns** (`TableColumn<T>`): `Id` (names it in saved state), `Header`, a `Text` or a `Cell`
  element, `Width` or `Fill` (a share of what the fixed columns leave), `MinWidth`/`MaxWidth`,
  `Align` (end for numbers), `Compare` (sortable when set), `FirstSort`, `Resizable`, `CanHide`,
  `HiddenByDefault`. Header and rows share one set of grid tracks — never `auto`, which would
  size each row on its own — so a cell cannot drift from its header.
- **Sorting:** a press on a sortable header (or Enter/Space on it) sorts by it, again the other
  way. `Sort(items)` is stable and memoized on the list's identity. `Group` pins rows whatever the
  direction (a file list's ".." and folders). An app that sorts its own rows (Vesper keeps a sort
  per tab) calls `SetSort` to tell the header, and passes `sort: false` to `Body`.
- **Widths:** drag the grip at a column's edge; a double click puts its own width back. Columns
  before the fill resize from their end edge, columns after it from their start edge — the fixed
  columns after a fill are pinned to the table's end, so their end edge cannot follow a drag.
- **Columns shown:** a right press on the header lists the columns that can be hidden, checked
  when shown.
- **State:** `State` (sort, dragged widths, hidden and shown columns) for a settings file;
  `Restore` puts it back and skips columns that no longer exist; `StateChanged` runs when a click
  or a drag settles, not on every move.
- **Keyboard:** sortable headers are Tab stops unless `HeadersTakeFocus` is off — off for a list
  that owns Tab itself.
- **Accessibility:** headers have the new `ColumnHeader` role (UIA header item, AT-SPI table column
  header), invokable, named with their sort ("Size, sorted descending"). `Body` names each row by
  `RowLabel` ("a.txt, Size 100 B"), which a virtual list needs: its rows reach a screen reader by
  that label alone.

**Looks** come from `Theme.Table` (`TableTheme`): header height, fill, hover, text colour, size,
weight and letter spacing; the divider; cell padding (header and rows alike, so they line up);
the sort mark; the grip's width.

## 26. Tabs

A strip of tab headers and the open tab's panel. Three looks (`TabStyle.Underline`, `Pill`,
`Enclosed`) and four placements of the strip (`TabPlacement.Top`, `Bottom`, `Left`, `Right`).

```csharp
Tabs.New().Style(TabStyle.Underline)
    .Tab("Changes", () => ChangesView(), badge: count.ToString())
    .Tab("History", () => HistoryView(), keepAlive: true)
    .Tab("README.md", () => Preview(), onClose: () => CloseReadme())

// The app's state: every build says which tab is open.
Tabs.New().Selected(_tab).OnTabChanged(i => _tab = i)
    .Tab("General", () => General())
    .Tab("Advanced", () => Advanced())
```

- **State:** left alone, the element keeps the open tab across rebuilds. `Selected(index)` hands
  it to the app, as `Expander.Expanded` does: every build says which tab is open, so pair it with
  `OnTabChanged`, which reports the user's choices either way.
- **Panels:** a tab's function is called when the tab opens, and again on each rebuild while it
  is open; what it returns patches the panel showing, as a child in any build is patched, so a
  text box in it keeps its focus and undo history. A panel that is not showing is dropped, unless
  the tab is `keepAlive`: that panel is kept while hidden and shown again as it was.
- **Which tab is which:** across a rebuild a tab is known by its label, or, when no tab came or
  went, by its place, so a tab renamed with a "•" for unsaved changes is still itself. Its
  header, its kept panel and the selection go with it: the open tab stays open when tabs close
  around it, and when it closes, the tab now in its place opens. A panel is only ever patched
  from its own tab's function.
- **Keyboard** (the ARIA tab pattern): Tab stops at the open tab only. The arrow keys along the
  strip (Left/Right, or Up/Down for a strip at the side) move to the next tab, round the ends,
  and open it; Home and End go to the first and last. Enter or Space opens a tab that has focus
  without being open. Delete closes a closable tab. With Ctrl, Alt or Super held, the keys are
  left for the app's shortcuts. When the tab with the keyboard closes, the keyboard on its header
  or in its panel, the open tab's header takes it.
- **Elements:** the strip, the headers and the panel's host are kept, not rebuilt: a switch
  restyles two headers and swaps the panel.
- **Accessibility:** the strip is a `TabList`, each header a `Tab` with `AccessibleSelected`,
  named from its label and badge (a closable tab by those outright, so its "×" is not read; the
  close button is "Close <label>"), and the panel a `TabPanel` named after the open tab. UIA's
  SelectionItem `Select` opens a tab.
