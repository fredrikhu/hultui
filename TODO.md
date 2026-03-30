# HultUI — Implementation TODO

## Just Completed
- **Context Menu** — right-click menu using overlay system. ContextMenuTheme (background, border, text/disabled/hover/separator/shortcut colors, corner radius, padding, font sizes, min width). ContextMenuElement with hover highlight, separators, shortcut hints. `Func<IReadOnlyList<ContextMenuItem>>` on Element for dynamic items. `.ContextMenu()` fluent API. Built-in TextInput context menu (Cut/Copy/Paste/Select All with dynamic disabled state). Keyboard navigation (Up/Down/Enter/Escape). Light/Dark theme presets. Dismiss on outside click.

## Up Next (in order)

### 1. VirtualList
- Virtualized scrolling for large lists — only materialize visible items
- Fixed item height for efficient offset calculation
- Reuse/recycle element instances as user scrolls
- Integrate with existing ScrollViewElement for scroll input
- Builder API: `.ItemHeight(float)`, `.Items(IReadOnlyList<T>)`, `.ItemTemplate(Func<T, Element>)`

### 2. Multi-line Text Input
- Multi-line editing with word wrapping and vertical scrolling
- Cross-line text selection (shift+click, shift+arrow, click-drag across lines)
- Decide: extend existing `TextInputElement` or create separate `TextAreaElement` — pick whichever is best technically (user doesn't have a preference)
- Integrate with existing clipboard, undo/redo, and context menu

## Deferred
- IME composition / candidate list support (basic text input works, full CJK composition later)
- Momentum / fling scrolling on ScrollView
- Platform accessibility bridges (AT-SPI2, UIAutomation, NSAccessibility)
