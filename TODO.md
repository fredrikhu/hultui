# HultUI — Implementation TODO

## Just Completed
- **A scroll view that had not been painted could not be scrolled** — its extent was computed
  by the *renderer*, and the renderer culls: anything outside a clipping ancestor is skipped
  entirely, so such a view reported a content size of zero. That zero is not inert, because the
  offset setters clamp against it — setting `ScrollOffsetY` on one silently did nothing, which
  is the worse of the two ways to be wrong. Wheeling was never the broken path (the wheel only
  arrives once the pane is on screen, and being on screen is what made it measurable); what
  broke is everything that scrolls a view *before* it is painted — restoring a saved offset,
  revealing a search hit, scrolling an item into view on the frame the pane is built. A vertical
  scroll view nested inside another vertical one hit it every time, because whatever sits above
  it in the outer view is usually tall enough to push it outside the outer's clip on the first
  frame; it arrived permanently unable to scroll, which is how this surfaced. The extent is now
  computed by the layout pass, which is the pass that actually knows where the children ended
  up. `UpdateScrollExtent` (pure: measure and clamp) split from `UpdateContentSize` (the render
  hook), because `VirtualListElement` also realizes and lays out its visible rows there and that
  cannot run from inside the layout pass it would re-enter. 5 headless tests, 3 of which fail
  with the layout-pass call removed — and the two that do not are labelled as the coverage they
  are. Still open, and a different complaint: an inner view swallows the wheel once it has
  nothing left to give instead of handing the rest to the page.
- **The WinUI look, as a sample** — `samples/HultUI.Samples.Fluent`: a Windows 11
  Settings-shaped app — borderless window, caption bar drawn as elements, NavigationView with
  the 3x16 accent selection pill, a content frame with one rounded corner, settings cards,
  toggle switches, InfoBars, an expander, a controls gallery, light/dark and six Windows accent
  presets. No framework feature was added for the shape of it; the whole design language turned
  out to be arithmetic (32px controls, 4px radii, 68px rows) plus the two details that actually
  carry the resemblance. Those are worth naming because both are easy to lose: WinUI's control
  border is a *vertical gradient*, darker along the bottom, which is what makes a Fluent button
  look like it rests on the page — a box takes one border colour, so a button is two nested
  boxes, outer drawing the bottom edge and inner the other three. And in dark mode the accent
  is a pale blue with *black* text on it; getting that inversion backwards is the fastest way to
  look like something else. Colours carry Microsoft's own names (`CardBackgroundFillColorDefault`,
  `ControlStrokeColorSecondary`) so the sample can be checked against the published table.
  Icons are Segoe Fluent Icons where the font is installed and Phosphor where it is not —
  probed by file, because a missing family does not fail at the font stack, it silently draws
  blank boxes. Mica is the one thing faked, and the About page says so. 9 tests, each verified
  by putting the fault back; drivable as `--app fluent [--dark false] [--accent 0-5]`.
- **The slider is themable** — `SliderTheme` (`TrackColor` / `FillColor` / `ThumbColor` /
  `ThumbBorderColor`, all nullable and all falling back to what the control already painted).
  It was the last interactive control reading `theme.Primary` and `theme.Border` straight,
  so an app whose card borders are a hairline and whose slider track is a mid grey had to pick
  which one to get right. Sizes stay per-instance on purpose:
  `ApplyTheme` runs on every style pass, so a themed size would overwrite whatever the caller
  set, and being a size it would need a layout invalidation with it. 3 headless tests, one of
  which is the one that matters — an unthemed slider paints exactly what it did before.
- **A dropdown's list is a stroked chevron away from Fluent** — `DropdownTheme.ChevronShape`
  (`Triangle` | `Stroke`) and `ChevronStrokeWidth`. The drop mark was the glyph `▾` typed from
  the control's own font; every current design system draws a thin V instead, and a glyph is
  the wrong mechanism for it — the shape then depends on what the UI font's designer thought a
  small solid triangle looks like, and on the font having one at all. `Stroke` is two
  `DrawLine` calls at 0.62em. The default stays `Triangle`, because changing it would repaint
  every dropdown and date picker in every app that never asked; opt in per theme. The
  painting moved to a shared `ChevronPainter` — the dropdown and the date picker had a copy
  each of the same constant and the same six lines, which is how two controls that are supposed
  to look identical stop looking identical. 3 headless tests.
- **Nothing knew where an element actually was** — `Element.ScreenBorderBox` /
  `ScreenContentBox` / `AncestorScrollOffset`. A scroll view does not move its children: their
  boxes are computed once and stay where the content puts them, and scrolling is a translate
  applied at paint time and mirrored in the hit tester. So everything that positions itself
  *against* an element needed to subtract the ancestors' scroll — and four places had their own
  copy of that walk while three more had simply forgotten it. The three that had forgotten:
  a dropdown and a date picker opened their popup wherever the control *would* be if nothing
  had scrolled (on a long page, most of a screen away from the control it belongs to — this is
  the one that was reported), a tooltip appeared beside the wrong row, and
  `HeadlessWindow.Click(element)` fired into whatever else was at that point. That last one is
  the worst shape a defect can have: a click that hits nothing is not an error, so the test
  around it passed with its handler never having run. `DoubleClick`/`RightClick`/
  `HeadlessElement.Click` all shared `CenterOf`, so all three were wrong the same way. The
  screenshot driver had it too, and gained a `wheel:` step to reach anything below the fold.
  7 headless tests, each verified by putting the offset back. Related, found while writing
  them and *not* fixed: a vertical scroll view nested directly inside another vertical one
  measures its content as zero and cannot scroll at all.
- **Autoscroll reaches text areas** — the gesture matched on `ScrollViewElement`, which is
  what carries a *list*. A wall of text is a `TextAreaElement`: it scrolls its own wrapped
  rows rather than child elements, and is a `BoxElement`. So every list in every app scrolled
  under a held middle button and the pane those lists exist to open sat still — Meridian's
  response body most visibly, Junction's Files body beside it. A reader has no way to tell the
  two kinds of scrolling thing apart, so the window now looks for `IAutoScrollable`
  (`MiddleClickAutoScroll` / `CanAutoScroll` / `ScrollOffsetY` / `ScrollBy`) and both answer to
  it; implement it on anything else that scrolls content it owns. `TextAreaElement.ScrollOffsetY`
  became public and clamping on the way (it was internal, and three test files reflected at it).
  The trap to remember is the shape, not the fix: a capability found by class name silently
  excludes everything that does the same job differently. 8 framework tests, 2 in Meridian,
  2 in Junction — each verified by putting the class-name match back.
- **Middle-click autoscroll** — holding the middle button in any `ScrollViewElement` (and so
  any `VirtualList`) anchors an origin and turns the pointer's distance from it into a
  *speed*, the way Windows does: the view keeps moving while the button is held even though
  no further input arrives, faster the further out, and it stops when the pointer comes back
  inside the dead zone or the button is released. A ring is drawn at the anchor so the zone
  you have to return to is the circle you can see. On by default (`MiddleClickAutoScroll`),
  because in a list the middle button otherwise does nothing at all; a view with nothing to
  scroll never claims the press, so the button keeps whatever meaning the app gave it (Vesper
  closes tabs with it). A gesture that scrolled swallows its own click, a press that never
  left the dead zone still clicks, and the release is dispatched either way so nothing is
  left holding a pressed `:active` style. The scroll runs from the frame loop rather than
  from pointer moves — `NeedsRender` keeps frames coming while the button is down — and the
  captured view is re-resolved if a rebuild reconciles it away. 11 headless tests.
- **Date and time picker polish** — the calendar's labels are centred in their cells.
  `IRenderContext.DrawText` lays lines out from the top of the rect it is given
  (`TextAlign` is horizontal only), so every label in a cell taller than its own line sat
  high in it by the leftover height. The time field also stopped borrowing the dropdown's
  horizontal padding (`TimeFieldTheme.PaddingX`): a dropdown's padding sits beside a sentence
  of arbitrary length, and around a fixed handful of digits it read as a field several
  characters wider than it had content for. Its segments are now centred in the field too —
  measure reserved padding either side that the render never applied, so the digits sat flush
  against the left border with all the slack on the right.
- **Date and time pickers** — `CalendarElement` (a month grid: header, weekday headings, six-by-seven days, drawn rather than composed — forty-two boxes rebuilt to move a hover highlight is forty-two allocations, and flex is the wrong tool for a grid of equal cells), `DatePickerElement` (a field that opens the calendar in an overlay and keeps focus itself, forwarding the arrows in, per the W3C combobox pattern), and `TimePickerElement` (a segmented HH:mm[:ss][ AM/PM] spinner: Left/Right pick a segment, Up/Down step it, digits type into it, the wheel nudges it, A/P set the half of the day). `Today` and `Culture` are properties rather than reads of the ambient clock and locale, so a rendered picker can be compared against a baseline that does not expire and does not depend on the developer's country. Themed by `CalendarTheme` and `TimeFieldTheme`; both fields borrow `DropdownTheme` for their box so a form does not end up with three differently-shaped controls in a row. Builders: `MonthCalendar` (not `Calendar` — `System.Globalization.Calendar` owns that name in exactly the files that format dates), `DatePicker`, `TimePicker`. 48 headless tests; `samples/HultUI.Samples.DateTime`.
- **Repeating tweens** — `TweenBuilder.Repeat(n)` / `.Loop()` / `.Yoyo()`. The cycle and its phase are derived from elapsed time rather than accumulated per cycle, so a repeat holds its period: a restart that dropped the frame's overshoot would quantise every cycle up to a whole number of frames, and a spinner built that way visibly slows down and drifts. A finite repeat ends on its end value; a loop never renders the terminal frame at all. Junction's Fetch/Pull/Push buttons are the first consumer.
- **Headless mode** (`HultUI.Headless`) — apps run with no window, GL context, or display. `IPlatformWindow`/`IWindowChrome`/`IWindowingPlatform` in `HultUI.Core.Platform` decouple `Window`/`Application` from SDL3 (which now implements them); `InputEvent` moved to `HultUI.Core.Events`. `SkiaRasterRenderSurface` renders into a CPU `SKSurface` reusing the same render/measure contexts as the GL path, with `Snapshot`/`ReadPixels`. `HeadlessSession` drives a manual frame pump on a virtual clock, so animations and tooltip delays advance deterministically; `PointerState` takes a `TimeProvider` so double-click timing does too. Input injects through the same funnel as real SDL events (`Click`/`TypeText`/`Drag`/`Wheel`/`PressKey`); queries run against fresh accessibility-tree snapshots (`GetByText`/`FindByRole`/`FindByAutomationId`), so a test can only find what assistive tech can. 42 tests, green on Windows and on a display-less Linux runner. Docs: `docs/HEADLESS.md`.
- **Display-list snapshot testing** — `RecordingRenderContext` decorates the real render context and captures every drawing call as readable text, so a regression diff says what moved instead of handing you two images, and baselines delta-compress in git. `FixedMetricsMeasureContext` derives text geometry from font size alone for baselines that hold across operating systems. Rendering still happens, so pixel capture works alongside it.
- **Cross-platform CI** — `test-linux` job runs `HultUI.CrossPlatform.slnf` (the solution minus the Windows-only `HultUI.UiTests`) on `ubuntu-latest`, proving the framework is display-free. The FlaUI UI tests stay excluded from CI; the headless suite is what gates interaction coverage there now.
- **Flex shrink / scroll-view collapse** — a scroll container's indefinite main axis now propagates into nested flex containers, so content overflows and scrolls instead of collapsing to the viewport (`FlexShrink(0)` is no longer needed as a workaround; existing ones are redundant). Percentages against an indefinite size fall back to auto, per CSS. Aligning the flex Phase 1 and Phase 6 constraints also fixed a cache mismatch that laid subtrees out twice — the benchmark's "remove row" went 10.2ms → 4.3ms.

- **Linux AT-SPI2 per-object D-Bus export** — `LinuxAccessibilityBridge` now connects to the a11y bus (`org.a11y.Bus.GetAddress`), registers via `org.a11y.atspi.Socket.Embed`, and exports the whole accessible tree through a single `IPathMethodHandler` over `/org/a11y/atspi/accessible/*` plus a `Cache.GetItems` bulk-fetch. Implements `Accessible`, `Component`, `Application`, `Action`, `Properties`, `Peer`, and `Introspectable`; emits `children-changed`, `state-changed` (focused / checked / enabled), and `property-change` (name / description / value) signals driven by the render-side `TreeDiff`. The served tree is an immutable snapshot swapped on each rebuild, so the bus-thread handlers read it lock-free. Verified on a live GNOME a11y stack with `pyatspi` (Orca's client): full tree / name / role / extents / interfaces round-trip and change signals delivered. 24 unit tests pin the ABI role numbers — the scaffold's guesses were wrong (push button 25→43, check box 45→7, text 40→61, …), exactly the class of bug on-desktop verification exists to catch. Also emits `Cache.AddAccessible`/`RemoveAccessible` (verified: 5-node burst at startup, well-formed on the wire) to keep client caches warm, and seeds a minimal application root *before* `Embed` so the registry's immediate post-Embed probe resolves instead of answering `UnknownObject`. Harness: `samples/AtSpiVerify` drives it headlessly (no display) and `atspi_inspect.py` / `atspi_events.py` read it back.
- **Accessibility bridge scaffolding** — Shared core in `HultUI.Core/Accessibility/`: `AccessibilityTreeDiffer` + `TreeDiff`, extended `AccessibilityNode` (IsFocusable, IsFocused, IsEnabled, IsToggled, Value, Parent), `IElementPropertyObserver` hook on `Element`. Checkbox/Radio/TextInput/TextArea fire notifications on value change. `Window` rebuilds + diffs the tree only when `DirtyFlags.Children` is set and a bridge is active. 16 new differ/builder tests (37 accessibility tests total, 383 core tests pass).
- **Windows UIA bridge** (`HultUI.Platform.Accessibility.Windows`) — AOT-compatible via `[GeneratedComInterface]` + `StrategyBasedComWrappers`. `IRawElementProviderSimple/Fragment/FragmentRoot` implemented. Patterns: Invoke, Toggle, Value, SelectionItem. `WindowsAccessibilityBridge` owns wrapper cache keyed by `ElementId`, raises UIA structure/property/focus events. `IUiaHostBridge` collaboration surface with SDL3 host adapter that installs a `WM_GETOBJECT` message hook and marshals UIA worker-thread actions through a per-frame queue.
- **Linux AT-SPI2 bridge scaffold** (`HultUI.Platform.Accessibility.Linux`) — Project structure, role mapping, session-bus connection flow via `Tmds.DBus.Protocol 0.92`. D-Bus per-object export is stubbed pending on-desktop verification; `IsActive` stays false until that lands.
- **Platform factory** — `AccessibilityBridgeFactory.Create(window)` dispatches by OS. `HultUI.csproj` references both platform bridges unconditionally; each is `SupportedOSPlatform`-gated so only the right one activates.

- **IME composition** — `SDL_EVENT_TEXT_EDITING` plumbed through `EventTranslator → TextEditingEvent`. `TextInputElement` and `TextAreaElement` render the preedit inline at the caret with a thin underline across the whole preedit and a thick underline on the active IME segment. `SDL_SetTextInputArea` is called each frame on the focused input so OS candidate windows snap to the caret; `SDL_ClearComposition` fires on blur. Custom candidate-list rendering is deliberately skipped — the OS-native window is used instead. New `ITextInputHost` abstraction in `HultUI.Core.Platform`; `SDL3TextInputHost` is the implementation. 9 unit tests covering the state machine.

## Up Next (in order)
- Windows UIA bridge on-device verification — run a sample, open Accessibility Insights / `Inspect.exe`, confirm tree, name, role, bounding-rect, and pattern actions round-trip.
- **AT-SPI `Action.DoAction`** — an AT can now activate controls. The neutral
  invoke/toggle/select logic was lifted out of the Windows-only `Sdl3UiaHostBridge`
  into a shared `AccessibilityActionRouter` (`src/HultUI/`, implementing
  `HultUI.Core.Accessibility.IAccessibilityActionHost`); both the Windows UIA host
  and the Linux AT-SPI bridge route through it, and `Window` drains its UI-thread
  action queue each frame on every platform. Verified live with pyatspi:
  `do_action(0)` on the button dispatches a real click (counter 0→3 over three
  calls) and on the checkbox toggles it. The Windows UIA path was refactored to
  run through the router and **re-verified on Windows (2026-07-19): the FlaUI
  suite `tests/HultUI.UiTests` is 14/14 green** — invoke/toggle/select/set-value
  all still round-trip through the shared router.
- AT-SPI refinements (remaining polish): (1) a `state-changed:defunct` still shows up in a *mid-stream* event monitor on first contact with each node — but that's a client cold-start artifact: the monitor connects after our startup `Cache.AddAccessible` burst and only subscribes to `object:` events, so it never warms its cache. A client (Orca) running from app launch receives the cache signals. Not fixable app-side for a cold listener; our app provably emits only the intended signals. (2) `Component` extents are now device-pixel-correct (bounds × display scale, via `IAccessibilityGeometryHost` — verified: at 2× the button reads `[24,84 752x76]` vs `[12,42 376x38]` at 1×). The remaining gap is *absolute screen* coordinates: under Wayland a client has no reliable window screen origin, so SCREEN and WINDOW coordType both resolve to window-relative pixels; Wayland is the only target, so this is not planned. (3) `TreeDiff.Reordered` (same children, new order) needs **no** signal — every query is served from the freshly rebuilt snapshot, so `GetChildren`/`GetChildAtIndex` already return the new order. Verified: a persistent pyatspi client observes each reorder with the reorder signal entirely disabled. A high-level AT that caches child order and only refreshes on events (Orca) would want an `object:children-changed` nudge; add it when an Orca test shows it's needed and confirms the form (`samples/AtSpiVerify/atspi_reorder.py` reproduces reorders). (4) end-to-end Orca *speech* pass (largely covered by the pyatspi verification, since Orca reads through the same API; the gap is Orca-specific event handling and actually listening).

## Deferred
- **Automatic minimum size for flex items** (CSS `min-height:auto`) — content can still be crushed below its own size inside a bounded container. Needs a real recursive min-content pass in which scroll containers contribute zero; approximating it from the flex Phase 1 measurement was tried and reverted, because for a container that measurement is its full content extent, so an ancestor wrapping a scroll view gets pinned to the whole scrollable height (14,640px tall, 1007 draw calls instead of 47). `FlexShrinkTests` carries a skipped case. Add a shrink-heavy benchmark first — the current suite barely exercises the shrink path, so it would not catch the cost.
- **Golden-image testing** — the raster surface already exposes `Snapshot`/`ReadPixels`. Blocked on font determinism: glyphs come from `SKFontManager.Default`, so an embedded, pinned font is needed before pixel baselines can hold across machines.
- **Render-log element attribution** — recorded ops carry no element identity, so "what did this element draw?" means matching on coordinates. `ElementRenderer` has a single per-element call site to hook.
- UIA `ITextProvider` (word-by-word reader navigation) — currently exposed via `IValueProvider`. Would also let selectable labels (`TextElement.Selectable`) report their selected sub-range to assistive tech via `GetSelection`/`RangeFromPoint`; today mouse/keyboard selection works but a screen reader reads the label as a whole.
- AT-SPI `Text` / `EditableText` interfaces.
- macOS `NSAccessibility` bridge.
- LiveRegion / alert announcements.
- Accessibility for overlays, tooltips, context menus.

## Traps & decisions (durable)

Engineering notes that aren't obvious from the code, carried over from the AT-SPI
and benchmark work.

- **Do not change the `FlexShrink = 1` default.** Fixed-height items shrinking in a
  bounded, overflowing column is correct CSS; the real bug was narrower (a scroll
  container's indefinite main axis not propagating into nested flex containers), and
  it is fixed. (The `min-height:auto` clamp is a separate, still-deferred item above.)
- **A/B every layout-engine change with the benchmark** — `--auto` and
  `--auto --virtual`, in one session, with `SDL_WINDOW_ACTIVATE_WHEN_SHOWN=0` so the
  window doesn't steal focus. Unit tests passed straight through a viewport-collapsing
  regression that only the benchmark caught. Compare within a single session only;
  cross-session numbers are noise.
- **Snapshot baselines in raw string literals carry the source file's line endings**
  (CRLF on Windows, LF on Linux under autocrlf), so a baseline can pass one CI leg and
  fail the other. Normalise with `ReplaceLineEndings("\n")`.
- **`FlexGrow(1)` alone does not mean "take the leftover space".** The default `auto` basis
  sizes the item to its own content first and grows from there, so an item whose content is
  wide — a text line holding one unbreakable token, a base64 blob or a long hash — takes that
  width and overflows its row. `FlexBasis(Px(0))` is what says "start from nothing and take
  what is left"; the app already uses it where a column must not be pushed out. Wrapped text
  makes it look like a wrapping bug, but the text had wrapped correctly at the width the
  label had wrongly taken.
- **Measure text at the width the label actually gets, padding included.** A wrapped row's
  height comes from measuring its line at the code column's width; the label's own horizontal
  padding is part of that column and not room the text can use. Junction subtracted the
  number/sign columns and not the label's 14px, so every line was wrapped 14px too wide and
  any line that band was the difference for came out a visual line short and drew over the row
  below. It reads as an intermittent fault — it only shows when a line's length lands in the
  band. Measuring *narrower* than the truth is the safe direction: a row a line too tall is a
  small gap, a row a line too short is overlapping text. The row builder and the measurement
  must share named constants, since this is exactly what drifts.
- **A resize relayouts but does not rebuild.** An app that skips the rebuild on an ordinary
  resize (both apps do, to keep a drag off the build path) is fine for every fixed-height
  row and wrong for any height *derived from the width* in the build — a wrapped diff row,
  say. Layout then stacks rows at the new width using heights measured at the old one, and
  the overflow draws over the row below. It only showed on a window resize: dragging the
  pane's own splitter is app state and rebuilds anyway. Junction's diff and Vesper's diff
  *and* viewer all had it; Vesper's auto grid tracks did not save it, because the virtualized
  body forces the measured height regardless of what the track would have grown to.
- **`IsDetached` does not mean "no longer in the tree".** `TreeDiffer` marks the *incoming*
  element of a patch, the one thrown away — so a patched element is still the right object and
  correctly unflagged. An element that a rebuild *replaces* is dropped from the tree without
  being marked at all, and looks perfectly alive. Anything holding an element across frames
  has to ask `Window.IsInTree` as well; the middle-click autoscroll went on scrolling a
  replaced list nobody could see. And when the live one cannot be found yet — a fresh element
  has no layout until the pass after the tick that looks for it — keep the old reference and
  ask again next frame rather than giving up, or one rebuild ends the gesture for good.
- **A click belongs to the primary button.** `ClickEvent` used to be synthesized for whatever
  button was pressed, and `OnClick` takes no button — so every handler in every app fired on a
  middle click too. It only mattered once middle-drag became the way to scroll a list: a press
  that never left the autoscroll's dead zone staged the diff line under it in Junction. Clicks
  are now the primary button's alone, which is what the DOM does (`auxclick` for the rest) and
  what half this framework's own controls were already doing by hand with
  `if (evt.Button != 0) return`. A handler that really wants the middle or right button reads
  it off PointerDown, where the button is.
- **A control that owns its own wrapping is immune to all of this — but test that it does.**
  Meridian shows response bodies through `TextAreaElement`, which wraps during layout and
  caches its line starts against the width it wrapped at, so a resize re-wraps with nothing
  for the app to do. The measured-height fault only exists where an app measures wrapped rows
  itself and hands them to a virtualized list; reach for the control before building that
  machinery. What "it wraps itself" does not buy you is the width being right: adding 14px to
  the wrap width inside the control left every suite in the repository green, all four apps
  and the framework, because nobody had asserted that a wrapped line fits the box it is in.
  `TextAreaWrapWidthTests` does now.
- **Correcting a width takes two halves, and each is useless alone.** The column width is
  read from a laid-out box, so a build started before layout sees the width from a frame ago
  — one rebuild on resize measures at the *old* width and fixes nothing. The post-frame
  settle that re-reads the width is what corrects it. But that settle is registered from a
  build, so with no rebuild on resize it is never scheduled and never runs. Disable either
  half and the fault comes back; both were verified that way in Vesper.
- **A row grows to fit its text; the list still stacks it at the height it was given.** So
  "is this row tall enough" has to be asked of the label's own line boxes, not of its
  laid-out box — the box reads back as the row's either way, and a check against it passes
  straight through the bug. `LayoutProbe.DrawnText(label).Height` is the clamped one; summing
  the element's `_lines` is not. Two tests written the first way were green against provably
  broken code.
- **Keys among siblings are all-or-nothing.** A single keyed child in an otherwise unkeyed
  child list makes the reconciler match that whole list by key, and the unkeyed siblings are
  then *replaced* rather than patched — losing focus, caret and scroll. Adding one key to
  Junction's commit composer left the summary field discarding everything typed after the
  first character. Key every sibling, or none.
- **Inside `HultUI.*`, bare `Text` binds to the `HultUI.Text` namespace**, not the
  builder — alias `using Text = HultUI.Core.Builders.Text;` inside the namespace.
- **`grep "warning CS"` does not match `warning CA1416`** — platform-analyzer warnings
  hide from that filter, so a clean-looking `warning CS` grep can still be masking
  CA1416. The Linux a11y test project carries a scoped `<NoWarn>CA1416</NoWarn>`
  because its pure role-mapping tests call `[SupportedOSPlatform("linux")]` helpers on
  every CI leg (Windows included).
- **AT-SPI wire-level gotchas:** `Tmds.DBus.Protocol`'s `MessageWriter`/`Reader` are
  ref structs — they can't be a `using` variable and passed by `ref` at once (dispose
  by hand). Never guess AT-SPI role numbers — introspect a live GTK app with `busctl`.
  The registry probes the app root *during* the `Embed` round-trip on the receive
  thread, so seed the root into the snapshot **before** `Embed` or that probe answers
  `UnknownObject` (visible only on the raw bus, not through pyatspi). Verify with two
  lenses: pyatspi for tree/semantics, `busctl monitor` for wire-format and timing —
  each catches bugs the other misses.
- **Re-verifying the AT-SPI bridge** needs a running `at-spi2-registryd`.
  `samples/AtSpiVerify` drives the bridge headlessly
  (`HeadlessWindow.InstallAccessibilityBridge` / `InstallActionRouter`), and
  `atspi_inspect.py` / `atspi_action.py` / `atspi_reorder.py` read it back with pyatspi.
