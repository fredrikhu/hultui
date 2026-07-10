# Accessibility Bridge Implementation Plan

Status: **ready to implement** — picked up where session ended due to token budget.
Decisions below are confirmed by Fredrik; proceed without re-asking.

## Goal

Make Windows Inspector, Accessibility Insights, Narrator, Orca (Linux), and
Accerciser see HultUI apps as proper accessible UIs instead of opaque Skia
surfaces. Implement the Windows UIAutomation bridge and the Linux AT-SPI2 bridge.
macOS (NSAccessibility) is deferred.

## Current state (as of 2026-04-22)

- `src/HultUI.Core/Accessibility/` has `IAccessibilityBridge`, `AccessibilityNode`,
  `AccessibilityRole`, `AccessibilityTreeBuilder`, `NullAccessibilityBridge`.
- `AccessibilityTreeBuilder.Build` is **not invoked from `src/`** yet — only in
  tests. Integration point is open.
- `DirtyFlags.Children` (see `src/HultUI.Core/Elements/DirtyFlags.cs:9`) is
  already propagated when children mutate — reuse as the structural-change
  trigger for the bridge.
- `FocusManager.FocusChanged` already fires on focus moves
  (`src/HultUI.Core/Events/FocusManager.cs:29`), and `Window` already subscribes
  (`src/HultUI/Window.cs:96`). Add bridge forwarding alongside the existing
  `OnFocusChanged` handler.
- SDL3 window exposes HWND via
  `SDL_GetPointerProperty(props, SDL_PROP_WINDOW_WIN32_HWND_POINTER, ...)`.
- Projects target `net10.0`, `HultUI` is `IsAotCompatible=true` — expose UIA
  providers as hand-built native COM objects (see `NativeProvider.cs`); avoid
  `ComWrappers`/runtime COM, which UIAutomationCore rejects on .NET 10.

## Confirmed decisions

1. **Linux D-Bus library:** add `Tmds.DBus.Protocol` as a dependency of
   `HultUI.Platform.Accessibility.Linux`. MIT, AOT-compatible, ~100KB.
2. **`ITextProvider` (UIA text pattern):** deferred to a follow-up. Text inputs
   will announce and expose value via `IValueProvider`; word-by-word screen
   reader navigation comes later.
3. **Project layout:** separate per-platform projects
   (`HultUI.Platform.Accessibility.Windows`, `.Linux`), matching the existing
   `HultUI.Platform.SDL3` pattern. No `#if` shared project.

## Work breakdown

### Step 1 — Shared core (start here)

Files to add in `src/HultUI.Core/Accessibility/`:

- **Extend `AccessibilityNode`** with fields the bridges need:
  - `Parent` back-reference (mutable internal setter, set when added as child)
  - `IsFocusable`, `IsFocused`, `IsEnabled`
  - `IsToggled` (nullable `bool?` — null for non-toggleable)
  - `Value` (string? — for TextInput, Slider later)
  - Already has: `ElementId`, `Role`, `Name`, `Description`, `BoundingRect`, `Children`.
- **`AccessibilityTreeDiffer.cs`** — compares prev/next tree, produces a
  `TreeDiff` with `Added`, `Removed`, `Reordered`, `PropertyChanged(node, prop)`
  lists. Key nodes by `ElementId` so wrappers can be reused across rebuilds.
- **Evolve `IAccessibilityBridge`** from `Update(root)` to:
  - `bool IsActive { get; }` — cheap "is anyone listening" check, polled per frame
  - `void ApplyChanges(TreeDiff diff)`
  - `void NotifyFocusChanged(AccessibilityNode? focused)` (keep)
  - `void NotifyPropertyChanged(AccessibilityNode node, string propertyName)` (keep)
  - Keep `NullAccessibilityBridge` as the default no-op.
- **`IElementPropertyObserver` hook on `Element`** — single-subscriber callback
  that bridges use to hear about per-element property changes (checkbox toggled,
  text value changed, etc.). Elements like `CheckboxElement.OnChanged`,
  `TextInputElement` value setters, `RadioButtonElement.IsChecked` setter fire
  this after their existing work.

### Step 2 — Window integration

In `src/HultUI/Window.cs`:

- Add `IAccessibilityBridge _accessibilityBridge = NullAccessibilityBridge.Instance;`
- Factory resolves real bridge lazily: on first frame where
  `AccessibilityBridgeFactory.ShouldActivate()` returns true, construct the
  platform bridge. On Windows this polls `UiaClientsAreListening()`; on Linux
  it checks the AT-SPI bus for registered assistive techs.
- After `_layoutEngine.PerformLayout(...)` in `RenderFrame` (around line 169),
  add:
  ```
  if (_accessibilityBridge.IsActive && _root.DirtyFlags.HasFlag(DirtyFlags.Children))
  {
      var newTree = AccessibilityTreeBuilder.Build(_root);
      var diff = AccessibilityTreeDiffer.Diff(_previousA11yTree, newTree);
      _accessibilityBridge.ApplyChanges(diff);
      _previousA11yTree = newTree;
  }
  ```
- In `OnFocusChanged` (line 99), also call
  `_accessibilityBridge.NotifyFocusChanged(FindNode(element))`.

### Step 3 — Windows UIA bridge

New project: `src/HultUI.Platform.Accessibility.Windows/`.

- TFM `net10.0`, `AllowUnsafeBlocks`, Windows-only (`<IsPackable>false</IsPackable>`,
  `<SupportedOSPlatform>windows</SupportedOSPlatform>`).
- References: `HultUI.Core`, `HultUI.Platform.SDL3` (for HWND access).

Files:

- **`UiaInterop.cs`** — P/Invokes: `UiaReturnRawElementProvider`,
  `UiaHostProviderFromHwnd`, `UiaRaiseAutomationEvent`,
  `UiaRaiseAutomationPropertyChangedEvent`, `UiaRaiseStructureChangedEvent`,
  `UiaClientsAreListening`, `UiaDisconnectProvider`. Constants for
  `UIA_*ControlTypeId`, `UIA_*PropertyId`, `UIA_*PatternId`,
  `StructureChangeType`, `NavigateDirection`.
- **`NativeProvider.cs`** — exposes the managed provider logic to UIA as
  hand-built **native** COM objects (unmanaged memory + a tear-off per interface,
  each with a vtable of `[UnmanagedCallersOnly]` thunks). This replaced a
  `StrategyBasedComWrappers` + `[GeneratedComInterface]` approach: on released
  .NET 10, handing such a CCW to `UiaReturnRawElementProvider` access-violates
  inside UIAutomationCore before it even calls the provider. A native object is
  marshalled/called correctly, keeps the assembly AOT-friendly, and needs no
  Windows-Desktop/WPF dependency. The managed side implements the plain
  `INative*` interfaces defined in this file.
- **`UiaComInterfaces.cs`** — shared blittable structs (`UiaRect`). The provider
  interface GUIDs live in `NativeProvider.cs`.
- **`UiaFragmentRoot.cs`** — one per window. Implements all three provider
  interfaces. `ElementProviderFromPoint` walks the node tree doing point-in-rect,
  returning the deepest hit. `GetFocus` returns the currently focused wrapper.
- **`UiaFragment.cs`** — per `AccessibilityNode`, cached by `ElementId`. Maps:
  - `Role` → `UIA_ControlTypePropertyId`
  - `Name` → `UIA_NamePropertyId`
  - `Description` → `UIA_HelpTextPropertyId`
  - `BoundingRect` → `UIA_BoundingRectanglePropertyId` (convert to screen coords
    via `ClientToScreen` on the HWND)
  - `IsFocusable` → `UIA_IsKeyboardFocusablePropertyId`
  - `IsFocused` → `UIA_HasKeyboardFocusPropertyId`
  - `IsEnabled` → `UIA_IsEnabledPropertyId`
  - `GetPatternProvider(patternId)` dispatches to pattern impls based on role.
- **`WindowsAccessibilityBridge.cs`** — the `IAccessibilityBridge` impl.
  - Polls `UiaClientsAreListening()` for `IsActive`.
  - Subclasses the window procedure (`SetWindowLongPtrW(GWLP_WNDPROC)`) and, on
    `WM_GETOBJECT` with `lParam == UiaRootObjectId`, returns
    `UiaReturnRawElementProvider(hwnd, wParam, lParam, nativeRoot)`'s LRESULT.
    `WM_GETOBJECT` is a *sent* message, so it must be serviced from the window
    procedure — SDL's `SDL_SetWindowsMessageHook` only runs in the message pump
    and never sees it, so the earlier hook-based approach never connected the
    provider (and, when it occasionally did, crashed — see `NativeProvider.cs`).
  - `ApplyChanges(diff)` updates the per-`ElementId` wrapper cache and, when
    `HULTUI_UIA_EVENTS=1`, raises the corresponding bulk structure-changed /
    property-changed events. Events are opt-in for now; the passive tree served
    on `WM_GETOBJECT` already lets an AT navigate and read on demand.
  - `NotifyFocusChanged`: `UiaRaiseAutomationEvent(wrapper, UIA_AutomationFocusChangedEventId)`.
  - Cache `UiaFragment` wrappers keyed by `ElementId` so COM identity is stable.
- **Pattern impls** (same file or separate):
  - `InvokeProviderImpl` — calls a `ClickEvent` dispatch on the element for
    Button role.
  - `ToggleProviderImpl` — reads/writes `CheckboxElement.IsChecked`.
  - `SelectionItemProviderImpl` — for `RadioButtonElement.Select()`.
  - `ValueProviderImpl` — for `TextInputElement`/`TextAreaElement` value
    get/set (set routes through the element's text-mutation path so IME and
    events behave correctly).

Wrapper caching is load-bearing: UIA clients compare provider identity across
calls. Key wrappers off `AccessibilityNode.ElementId`, reuse across rebuilds.

Threading: UIA may call provider methods from worker threads. Either marshal
to the UI thread, or snapshot values onto the wrapper at construction/diff
time (preferred — matches the design above and avoids cross-thread element
access).

### Step 4 — Linux AT-SPI2 bridge

New project: `src/HultUI.Platform.Accessibility.Linux/`.

- TFM `net10.0`. Reference `HultUI.Core`.
- `PackageReference Include="Tmds.DBus.Protocol" Version="0.21.2"` (or current).

Files:

- **`AtSpiConstants.cs`** — interface names (`org.a11y.atspi.Accessible`,
  `Component`, `Application`, `Action`, `Value`, `EditableText`), role enum
  mapping (AT-SPI uses its own `Role` enum — map from `AccessibilityRole`).
- **`AtSpiBus.cs`** — connects to the AT-SPI bus. AT-SPI has two buses: call
  `org.a11y.Bus.GetAddress` on the session bus to discover the a11y bus
  address, then connect to that bus. Implement as a `Connection` from
  `Tmds.DBus.Protocol`.
- **`AtSpiObject.cs`** — base for exported D-Bus objects. Each
  `AccessibilityNode` exports at `/org/a11y/atspi/accessible/<id>`.
- **`AtSpiAccessible.cs`** — implements the `Accessible`, `Component`,
  `Action` interfaces for a node. Navigation via parent/child queries returns
  object paths; clients fetch them lazily.
- **`AtSpiApplication.cs`** — root object at
  `/org/a11y/atspi/accessible/root` implementing the `Application` interface.
  Registers with the AT-SPI registry via
  `org.a11y.atspi.Socket.Embed`.
- **`LinuxAccessibilityBridge.cs`** — the `IAccessibilityBridge` impl.
  - `IsActive` returns true once registration with the AT-SPI registry
    succeeds. No per-poll check like Windows — if nothing was listening,
    registration fails/returns an empty socket and we stay inactive.
  - `ApplyChanges(diff)`:
    - Added → export new `AtSpiAccessible` on the bus, emit
      `children-changed::add` signal on parent.
    - Removed → unexport, emit `children-changed::remove`.
    - PropertyChanged → emit `PropertyChange` signal (AT-SPI marshals specific
      property-name strings like `"accessible-name"`, `"accessible-value"`).
  - `NotifyFocusChanged` → emit `StateChanged::focused` true/false signals on
    the old/new nodes, and `window:activate` if needed on the root.

### Step 5 — Platform dispatch

- Factory in new file `src/HultUI/AccessibilityBridgeFactory.cs`:
  - On `OperatingSystem.IsWindows()` → return `WindowsAccessibilityBridge`.
  - On `OperatingSystem.IsLinux()` → return `LinuxAccessibilityBridge`.
  - Else → `NullAccessibilityBridge.Instance`.
- `HultUI.csproj` gains conditional `ProjectReference` for each platform
  assembly guarded by `$(RuntimeIdentifier)` / `$([MSBuild]::IsOSPlatform(...))`
  so AOT publishing doesn't drag in the wrong one. (If this gets messy, just
  reference both unconditionally — each has `SupportedOSPlatform` and the JIT
  will skip loading the other.)

## Test strategy

- **Unit tests for diffing** — `AccessibilityTreeDifferTests` in
  `tests/HultUI.Core.Tests/Accessibility/`. Add/remove/reorder/property-change
  cases. Table-driven.
- **Tree builder tests** already exist — extend for new node fields
  (`IsFocusable`, `IsToggled`, `Value`).
- **Manual verification**:
  - Windows: run `HultUI.Samples.TodoList` or `HelloWorld`, open
    `Inspect.exe` (Windows SDK) or Accessibility Insights for Windows, verify
    tree shows elements, names, roles. Toggle a checkbox via the tool.
  - Linux: run a sample, open `accerciser`, verify tree. Run `orca` and
    alt-tab in — it should announce the window title and focused control.

## Virtualized lists

A `VirtualListElement` only materializes on-screen rows as elements, so the a11y
tree would otherwise expose just those. `IAccessibleItemSource` (implemented by
the list) lets the accessibility layer expose every item as a `ListItem` under a
`List`, with labels from item data (`AccessibleItemLabel`), scroll-adjusted
positions, and activation (`OnActivateItem`) — so assistive tech can read,
navigate, and act on the whole list at any size. Built in increments, all done:

- **Increment 1 (done):** expose all items — read & navigate. Eagerly materializes
  each item as a `ListItem` node.
- **Increment 2 (done):** activation — a `ListItem` exposes the UIA Invoke
  pattern; its synthetic id routes back through `IAccessibleItemSource.ActivateItem(index)`.
- **Increment 3 (done):** on-demand provisioning for large lists. Above
  `AccessibilityTreeBuilder.MaxEagerVirtualItems` a list is marked
  `IsVirtualItemContainer` and emits no child nodes; the bridge synthesizes a
  fresh `UiaVirtualItem` per navigation result (identity via the stable
  `VirtualItemId` runtime id, so no caching), resolving name/bounds/count live
  through `IUiaHostBridge`. Nothing is built eagerly and scrolling recomputes
  bounds on demand rather than re-emitting per-item events — verified with a
  100k-item list (navigate, hit-test, invoke all instant). The cap is now just a
  fast-path switch, not a ceiling.

Remaining refinements (nice-to-have): a UIA `ScrollPattern` / `ItemContainer`
`FindItemByProperty` on the container, and selection state (`SelectionItem`
`IsSelected`) so a screen reader announces which item is selected.

## Out of scope (follow-ups)

- `ITextProvider` for rich text-pattern navigation (UIA).
- `org.a11y.atspi.Text` / `EditableText` for AT-SPI.
- macOS `NSAccessibility`.
- UIA `LiveRegion` / AT-SPI live regions.
- Accessibility for overlays, tooltips, context menus.
- `IRawElementProviderAdviseEvents` event filtering (minor perf opt).
- List/ListItem AT-SPI role codes (Linux bridge currently maps them to Panel).

## Pick-up checklist

When resuming:

1. Skim this file and `src/HultUI.Core/Accessibility/*.cs` to reload context.
2. Start with **Step 1 (shared core)** — changes are small, fully testable
   without any platform work.
3. Do **Step 2 (Window integration)** with a stubbed `IsActive => false` so
   nothing fires yet; run samples to confirm no regressions.
4. Pick **Windows or Linux** for Step 3/4 — they're independent. Windows has
   a faster verification loop (`Inspect.exe` is tight); Linux needs a running
   DE with AT-SPI enabled.
5. Move `Platform accessibility bridges (AT-SPI2, UIAutomation, NSAccessibility)`
   out of the `## Deferred` section of `TODO.md` as work starts.
