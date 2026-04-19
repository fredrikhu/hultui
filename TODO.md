# HultUI — Implementation TODO

## Just Completed
- **TextArea** — Separate `TextAreaElement` (not extending TextInputElement). Word wrapping with word-boundary breaks. 2D cursor navigation (Up/Down/Home/End per line, Ctrl+Home/End for document). Cross-line selection via shift+arrows, shift+click, click-drag. Vertical scroll with cursor auto-follow and scrollbar. Enter inserts newlines. Triple-click selects line. Context menu with cut/copy/paste/select-all. Reuses TextInputTheme for styling. Wired into Window for drag-capture and clipboard. 41 unit tests.

## Up Next (in order)

## Deferred
- IME composition / candidate list support (basic text input works, full CJK composition later)
- Platform accessibility bridges (AT-SPI2, UIAutomation, NSAccessibility)
