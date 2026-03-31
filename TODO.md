# HultUI — Implementation TODO

## Just Completed
- **VirtualList** — `VirtualListElement<T>` extending ScrollViewElement. Fixed item height for O(1) offset calculation. Spacer-based positioning: invisible spacer pushes visible items to correct virtual positions. Element recycling pool. 1-item overscan for smooth scrolling. Fluent API: `.ItemHeight(float)`, `.Items(IReadOnlyList<T>)`, `.RenderWith(Func<T, Element>)`. Inherits scroll input, scrollbar, hit testing from ScrollViewElement. 14 unit tests.

## Up Next (in order)

### Multi-line Text Input
- Multi-line editing with word wrapping and vertical scrolling
- Cross-line text selection (shift+click, shift+arrow, click-drag across lines)
- Decide: extend existing `TextInputElement` or create separate `TextAreaElement` — pick whichever is best technically (user doesn't have a preference)
- Integrate with existing clipboard, undo/redo, and context menu

## Deferred
- IME composition / candidate list support (basic text input works, full CJK composition later)
- Momentum / fling scrolling on ScrollView
- Platform accessibility bridges (AT-SPI2, UIAutomation, NSAccessibility)
