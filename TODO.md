# HultUI — Implementation TODO

## Just Completed
- **Accessibility bridge scaffolding** — Shared core in `HultUI.Core/Accessibility/`: `AccessibilityTreeDiffer` + `TreeDiff`, extended `AccessibilityNode` (IsFocusable, IsFocused, IsEnabled, IsToggled, Value, Parent), `IElementPropertyObserver` hook on `Element`. Checkbox/Radio/TextInput/TextArea fire notifications on value change. `Window` rebuilds + diffs the tree only when `DirtyFlags.Children` is set and a bridge is active. 16 new differ/builder tests (37 accessibility tests total, 383 core tests pass).
- **Windows UIA bridge** (`HultUI.Platform.Accessibility.Windows`) — AOT-compatible via `[GeneratedComInterface]` + `StrategyBasedComWrappers`. `IRawElementProviderSimple/Fragment/FragmentRoot` implemented. Patterns: Invoke, Toggle, Value, SelectionItem. `WindowsAccessibilityBridge` owns wrapper cache keyed by `ElementId`, raises UIA structure/property/focus events. `IUiaHostBridge` collaboration surface with SDL3 host adapter that installs a `WM_GETOBJECT` message hook and marshals UIA worker-thread actions through a per-frame queue.
- **Linux AT-SPI2 bridge scaffold** (`HultUI.Platform.Accessibility.Linux`) — Project structure, role mapping, session-bus connection flow via `Tmds.DBus.Protocol 0.92`. D-Bus per-object export is stubbed pending on-desktop verification; `IsActive` stays false until that lands.
- **Platform factory** — `AccessibilityBridgeFactory.Create(window)` dispatches by OS. `HultUI.csproj` references both platform bridges unconditionally; each is `SupportedOSPlatform`-gated so only the right one activates.

- **IME composition** — `SDL_EVENT_TEXT_EDITING` plumbed through `EventTranslator → TextEditingEvent`. `TextInputElement` and `TextAreaElement` render the preedit inline at the caret with a thin underline across the whole preedit and a thick underline on the active IME segment. `SDL_SetTextInputArea` is called each frame on the focused input so OS candidate windows snap to the caret; `SDL_ClearComposition` fires on blur. Custom candidate-list rendering is deliberately skipped — the OS-native window is used instead. New `ITextInputHost` abstraction in `HultUI.Core.Platform`; `SDL3TextInputHost` is the implementation. 9 unit tests covering the state machine.

## Up Next (in order)
- Windows UIA bridge on-device verification — run a sample, open Accessibility Insights / `Inspect.exe`, confirm tree, name, role, bounding-rect, and pattern actions round-trip.
- Linux AT-SPI2: finish per-object D-Bus export (`Accessible`, `Component`, `Application`, `Action`), wire `Socket.Embed`, emit `children-changed`, `PropertyChange`, `StateChanged::focused` signals; verify with `accerciser` + `orca`.

## Deferred
- UIA `ITextProvider` (word-by-word reader navigation) — currently exposed via `IValueProvider`. Would also let selectable labels (`TextElement.Selectable`) report their selected sub-range to assistive tech via `GetSelection`/`RangeFromPoint`; today mouse/keyboard selection works but a screen reader reads the label as a whole.
- AT-SPI `Text` / `EditableText` interfaces.
- macOS `NSAccessibility` bridge.
- LiveRegion / alert announcements.
- Accessibility for overlays, tooltips, context menus.
