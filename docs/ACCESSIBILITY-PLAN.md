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
2. **`ITextProvider` (UIA text pattern):** deferred at first; text inputs expose
   their value through `IValueProvider`. **Built 2026-10-04** as the terminal's seam
   A1 (below, *Text and announcements*): any element can offer text a reader moves
   through by character, word and line. Text inputs and TextArea do not offer it yet.
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
Every rebuild is a new tree, though, and a node reaches its whole tree through
`Parent` and `Children`: after each diff the bridge points every surviving
wrapper at its node in the new tree (one lookup per node, nothing allocated) and
drops the wrappers of elements the new tree lacks. A wrapper left on an old node
kept that tree alive and answered sibling navigation from it (500 checkbox rows
held 201 trees, 34 MB, after 200 toggles).

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
`FindItemByProperty` on the container, and the container-level `Selection` pattern
(`ISelectionProvider.GetSelection`, needs a SAFEARRAY of providers).

## Patterns & properties supported

Elements expose these to UIA today (property/pattern → how to opt in):

- **AutomationId** — `AutomationId("id")` (stable test selectors).
- **Name / HelpText / ControlType / IsEnabled / bounds** — from role + name.
- **Invoke** — buttons and list items (routes to click/activation).
- **Toggle** — checkboxes.
- **Value** — text inputs.
- **IsPassword** — a `TextInputElement` with `Masked` set (2026-10-05). Its Value, through
  both the property and the value pattern, is the bullets on screen, never the text: any
  process in the session can ask UIA for it, not only a screen reader. On Linux the node
  takes AT-SPI's `password text` role (40) and the same bullets, and masking or revealing a
  field sends a `property-change:accessible-role` signal.
- **SelectionItem** (`IsSelected` + `Select`) — list items (`VirtualListElement.SelectedIndex`).
- **ExpandCollapse** — any element with `AccessibleExpanded` set (e.g. tree items);
  Expand/Collapse reuse the element's click-to-toggle. A change raises the
  ExpandCollapseState property event (2026-10-05), and on Linux the node carries AT-SPI's
  `expandable` state, plus `expanded` while open, with a `state-changed:expanded` signal —
  before that, Orca read every expandable node as a plain button.
- **Table column headers** — role `ColumnHeader` (UIA HeaderItem, AT-SPI table column header),
  Invoke/click sorts (`TableView<T>`, 2026-10-05).
- **Tabs** — per W3C Core-AAM 1.1 (2026-10-05). Role `TabList` is the strip: UIA `Tab`,
  AT-SPI `page tab list`. Role `Tab` is one tab: UIA `TabItem`, AT-SPI `page tab`; it is
  named from its content, as a button is. Role `TabPanel` is the content shown for a tab:
  UIA `Pane`, AT-SPI `scroll pane`. (`Tab` used to be UIA's strip type and `TabPanel`
  AT-SPI's tab list.) `TabsElement` uses all three: each header is a tab with
  `AccessibleSelected` set, and the panel is named after the tab it shows. Its headers are kept
  across a switch, so a reader is told the selection moved rather than the tabs being replaced,
  and they are keyboard-reachable the ARIA way: Tab stops at the open tab, arrows move and open
  (2026-10-05).
- **Selected** — `Element.AccessibleSelected` (`null` = not selectable) for one of a set —
  a tab; a `RadioButtonElement` is selected when checked. The node's `IsSelected` gives UIA
  the SelectionItem pattern (a tab's container is its strip; `Select` clicks the element)
  and its IsSelected property and change event, and AT-SPI the `selectable` and `selected`
  states. AT-SPI's `selected` used to come from `IsToggled`, which no radio or tab carried,
  so neither was ever announced as selected.
- **IsOffscreen** — nodes (and virtual items) clipped out of the visible viewport,
  from the scroll-adjusted bounds vs. the window ∩ ancestor scroll viewports.
- **Scroll** — scroll views and virtualized lists; read scroll percent/view size
  and programmatically scroll (`SetScrollPercent`, `Scroll` by line/page).
- **RangeValue** — `SliderElement` (and anything implementing `IRangeValueElement`);
  read/set the value and its bounds.

Not yet wired (candidates): container-level `Selection` (needs a SAFEARRAY of
providers), a UIA `ItemContainer` on virtualized lists.

## Text and announcements (built 2026-10-04, the terminal's seams A1 and A2)

- **Text (A1).** An element offers `AccessibleText` through the internal
  `Element.AccessibleTextSource`: the text, its lines, a caret, a selection, the
  visible part, and where each character is (`AccessibleTextGeometry`). The node carries
  the snapshot the tree was built with, and the source, which the bridges read on their
  own threads between rebuilds. The differ compares snapshots and tells `Text` (with the
  previous node, so a bridge can describe the change) and `TextSelection`.
  - **Lines and paragraphs (2026-10-07).** A paragraph is a `'\n'`-separated run. A line
    is a paragraph unless the element says where its text wraps (`lineStarts`), and then
    it is a line as drawn, which is what a word processor's reader means by one.
  - **Windows:** the text pattern, `ITextProvider2` and `ITextRangeProvider`, as native
    vtables like the other patterns. Units: character (a surrogate pair whole), word (a
    run with the blanks after it), line, paragraph; format and page take the next larger
    one, as UIA asks. Ranges never move onto the empty end. Selecting from a reader is
    refused (`UIA_E_INVALIDOPERATION`). Text changes raise `Text_TextChanged`, caret and
    selection moves `Text_TextSelectionChanged`.
  - **Linux:** `org.a11y.atspi.Text`, its answers in `AtSpiText` (plain functions,
    tested without a bus), and `TextChanged` insert/delete, `TextCaretMoved` and
    `TextSelectionChanged` signals. A sentence is a line; a paragraph granularity is a
    paragraph. Not run against a real AT-SPI client yet.
  - **Who offers it:** the terminal, and the rich text editor (docs/plans/16, stage 5):
    its blocks as paragraphs, its drawn lines as lines, a picture as its words, a table's
    cells in reading order, made on the UI thread from its layout. Its boxes follow
    scrolling without a new snapshot. Checked from outside with Windows' own UI Automation
    client on the Fluent sample: lines, paragraphs, words, boxes, a point to a word, and
    the caret. Text inputs and TextArea are the next candidates.
- **Announcements (A2).** `Element.Announce(text, politeness)`: a UIA notification
  (`UiaRaiseNotificationEvent`, activity `HultUI.Announcement`; polite is
  `NotificationProcessing_All`, assertive `ImportantAll`), an AT-SPI 2.46
  `Object:Announcement`. Given to the bridge after the frame's tree is applied, from the
  element's node; nothing is kept while no reader listens; an element not in a window
  yet says it once a window takes it. Toasts announce their message (an error
  assertively); the terminal announces new output.
- **Found on the way:** the Windows bridge never told `AccessibilityClients` that a reader
  listened (only the Linux bridge did), so content that describes itself only while one
  listens never did on Windows; it reports from `UiaClientsAreListening` now. The Linux
  bridge advertised `org.a11y.atspi.Value` for every string value, with no handler behind
  it; AT-SPI's Value is a number, and it is no longer advertised.

## Out of scope (follow-ups)

- `EditableText` for AT-SPI; the text pattern on text inputs and TextArea.
- macOS `NSAccessibility`.
- UIA `LiveRegion` (`Element.Announce` covers what live regions were wanted for).
- ~~Accessibility for overlays, tooltips, context menus.~~ **Done 2026-09-22.** The
  window's snapshot now includes every hit-testable overlay — context menus, dropdown
  lists, dialogs — built with the overlay's own origin, since overlay content is positioned
  at render time and its element boxes are otherwise relative to the overlay rather than
  the window. A context menu publishes its entries as virtual items (`IAccessibleItemSource`,
  the contract virtualized lists use), because it draws them rather than building an element
  each; separators are left out, and an entry's shortcut is part of its name. Tooltips stay
  out on purpose: they belong to the element they describe, and a sibling node would be read
  twice. **Extended 2026-09-23:** an entry that marks the current choice sets
  `ContextMenuItem.IsChecked`, which the menu reports through `IAccessibleItemSource.ItemIsToggled`
  — so the mark is a state rather than a bullet inside the name. Any node with a toggle state
  now offers UIA's Toggle pattern whatever its role, and `Window.FindElement` searches open
  overlays, without which every accessibility action aimed at a menu or dialog was a silent
  no-op.
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
