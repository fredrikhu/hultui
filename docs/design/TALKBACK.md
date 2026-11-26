# TalkBack bridge — design note

**Status (2026-11-26): approved by the user, all four recommendations in §6 taken. Built the same
day, and checked with TalkBack on the emulator; §8 and §9 say what is built, what the emulator
showed, and what is left; §10 is the TextInput decision, taken and built.** It adds no public API. Plan 20, phase 6 ([plan](../plans/20-mobile-support.md) §2.9). Required
before any Android release beyond the user's own devices: the desktop apps hold that bar
(`docs/ACCESSIBILITY-PLAN.md`).

## 1. Today

- **The tree is platform-neutral.** Each frame that changed something builds or patches an
  `AccessibilityNode` snapshot, diffs it (`TreeDiff`), and hands the bridge the changes
  (`IAccessibilityBridge`: `ApplyChanges`, `NotifyFocusChanged`, `NotifyChangesApplied`,
  property changes made in place, `Announce`). Snapshots are immutable, so another thread may read
  them.
- **Two bridges**, UIA (`HultUI.Platform.Accessibility.Windows`) and AT-SPI
  (`HultUI.Platform.Accessibility.Linux`), about 8,200 lines together.
  `AccessibilityBridgeFactory` (`src/HultUI/AccessibilityBridgeFactory.cs`) gives the Null bridge
  on any other OS, so on Android TalkBack sees one opaque surface today.
- **Actions from assistive technology** arrive on its own thread and are queued on the
  `AccessibilityActionRouter`, which the frame drains on the UI thread. On Android the provider
  runs on Android's main thread, which is not HultUI's UI thread (SDL's), so the same router
  serves it unchanged.
- **Thirty-four roles** (`AccessibilityRole`), from `Button` to `ScrollBar`, plus states, values,
  ranges, text and selection, collection counts for lists whose items are served on demand, and
  live announcements.
- **Owner slots.** An `ElementId` is a `ulong` that packs an owner slot
  (`AccessibleOwnerSlots.cs`). Android's virtual view IDs are `int`s.

## 2. The shape: one virtual view per node, on SDL's surface

Android's model for a view that draws its own content is an `AccessibilityNodeProvider`: the view
exposes a tree of virtual views, each an `AccessibilityNodeInfo` made on request.

- **The provider is reached without subclassing SDL's `SDLSurface`.** `View.AccessibilityDelegate`
  has `getAccessibilityNodeProvider(host)`, so `HultUIActivity` sets a delegate on the surface.
- **Explore by touch.** With TalkBack on, a finger's touches arrive as hover events. The bridge
  listens with an `OnHoverListener` on the surface, hit-tests the snapshot (the UIA bridge's
  `ElementProviderFromPoint` does the same), moves accessibility focus to the node under the
  finger and sends `TYPE_VIEW_HOVER_ENTER`. TalkBack's double tap then performs `ACTION_CLICK`
  on the focused node. **To check first on the emulator:** that SDL's surface does not consume
  those hover events before the listener sees them.
- **Swipe navigation** walks the virtual tree in child order. The snapshot's order is reading
  order already, as UIA and AT-SPI read it.
- **IDs.** A per-window map from `ElementId` to a small `int`, made as nodes are first exposed and
  dropped when the diff removes them. Never reused while TalkBack may still hold one.
- **Bounds.** Layout units × the display density, plus the surface's place on screen
  (`getLocationOnScreen`), into `setBoundsInScreen`. Scrolled content uses the window boxes the
  hit test already uses.

## 3. The mapping

| HultUI | Android |
|---|---|
| role | `className` (`android.widget.Button`, `EditText`, `CheckBox`, `Switch`, `SeekBar`, `ProgressBar`, `ImageView`, `ListView`, `TabWidget` …), and `roleDescription` where no class fits (link, tab, tree item, menu item) |
| name, description, value | `contentDescription` or `text`; `hintText`; `stateDescription` |
| toggled, selected, expanded, enabled, focusable, focused | `checkable`/`checked`, `selected`, the expand and collapse actions, `enabled`, `focusable`/`focused` |
| password | `password` |
| range | `RangeInfo`, `ACTION_SET_PROGRESS` |
| list counts, served items | `CollectionInfo` and `CollectionItemInfo` |
| heading | `heading` |
| text and its selection | `text`, `textSelection`, `ACTION_SET_TEXT`, `ACTION_SET_SELECTION`, the movement-granularity actions |
| invoke, toggle | `ACTION_CLICK` |
| a scroll view, a list | `ACTION_SCROLL_FORWARD`/`BACKWARD` and the directional scroll actions, through the scroller's existing scroll |
| dialog | a pane title on its root node, `TYPE_WINDOW_STATE_CHANGED` when it opens |

**Events**, posted to Android's main thread from the bridge's calls on HultUI's:
- `ApplyChanges` → `TYPE_WINDOW_CONTENT_CHANGED` with `CONTENT_CHANGE_TYPE_SUBTREE` on the
  smallest changed parent, coalesced to one per frame per subtree, as the UIA bridge coalesces
- property changes → `TYPE_WINDOW_CONTENT_CHANGED` with the matching change type (text, state,
  content description), or `TYPE_VIEW_SELECTED` / `TYPE_VIEW_TEXT_SELECTION_CHANGED`
- `NotifyFocusChanged` → `TYPE_VIEW_FOCUSED` on the node, which TalkBack follows with its own focus
- `Announce` → a live region on the source node (polite or assertive), not
  `announceForAccessibility`, which recent Android versions discourage (§6 asks)

## 4. Where it lives, and testing

- **A mapper, platform-neutral and tested on the desktop.** It turns a snapshot and a diff into
  plain records: the virtual tree, each node's class, text, states, actions and bounds, and the
  events to send. It runs in the ordinary test projects against fake snapshots, as the AT-SPI
  bridge's objects, states and text are tested in `HultUI.Platform.Accessibility.Linux.Tests`.
- **A thin Android layer** turns the records into `AccessibilityNodeInfo`s and events, and the
  provider's `performAction` into router actions. It is checked on the emulator and the phone with
  TalkBack on, and with `uiautomator dump`, which reads the same tree TalkBack does.
- **The factory** returns the Android bridge on Android; `HultUIActivity` gives it the surface.

## 5. Public API

None. The bridge plugs in through the existing factory and the activity. Apps describe their
elements as they do for UIA and AT-SPI.

## 6. Decisions for the user

1. **Where the bridge lives:** its own `HultUI.Platform.Accessibility.Android` package, as the
   plan named it and as the other two are (recommended), or inside `HultUI.Platform.Android`.
2. **Announcements as live regions** (recommended), or `announceForAccessibility` while it works.
3. **The mapper-plus-thin-layer split**, with the mapper tested on the desktop and the layer only
   on devices (recommended).
4. **Order of work:** the Fluent sample first, then Aperture's main views (recommended, the plan's
   exit criteria), or Aperture first.

## 7. Size

L, about four to six weeks:
- the mapper and its tests: about two weeks
- the Android layer, explore by touch, actions and events: about two weeks
- the Fluent sample and Aperture on a device with TalkBack, and fixes: the rest

## 8. Built (2026-11-26)

**The package.** `HultUI.Platform.Accessibility.Android` always targets net11.0: the bridge
(`AndroidAccessibilityBridge`) and the mapper (`AndroidNodeMapper`), in HultUI.slnx and tested by
`tests/HultUI.Platform.Accessibility.Android.Tests` (46 tests, each rule also checked by a negative
control). With `HultUIAndroid=true` it targets net11.0-android too and adds the Android layer
(`Android/`): the node provider, explore by touch and the events. `HultUI.Platform.Android`'s reference
asks for that target, but a restore does not see a reference's properties, so an Android build passes
`-p:HultUIAndroid=true` itself (docs/ANDROID.md).

**How it plugs in, all internal.** `AccessibilityBridgeFactory.Platform`, which `HultUIActivity` sets:
each window gets a router and a bridge on SDL's surface. `IAccessibilityEditActions` in Core carries
what `IAccessibilityActionHost` does not (a field's value, a reader's scroll); the router implements it.
The differ now hands a value change its previous node, which TalkBack's text-changed event needs.

**Built as §2 and §3 say, with these differences:**
- **The window's root is the host's one child**, not the host itself: a root scroll view keeps its
  scroll actions.
- **What is left out**, as Android leaves out what is not important for accessibility: a scrollbar
  standing on its own, an image with no name, and what is inside a control that has a name, unless it
  is a control itself (ARIA's presentational children). A hidden node's shown descendants take its
  place.
- **Focusable only for controls**, and never the host: TalkBack makes a focusable node one stop and
  reads every label inside it as part of it.
- **Expanded state** is said by TalkBack from the expand or collapse action before Android 16
  (`setExpandedState` from 16).
- **The announcer** is a node of its own (id 0) that is in no node's children, with the last
  announcement as its text, a live region, over the announcing node.
- **The provider's map from element to node** is made on Android's main thread the first time it asks
  after a tree came, never on the UI thread, whose share is the diff's events.

**On the emulator** (`spikes/AndroidHello/scripts/talkback-check.ps1`), with TalkBack 15:
- §2's check holds: the surface's hover listener gets explore-by-touch before SDL.
- A finger on a control says it ("Tap me. Button", "alpha bravo charlie. Edit box", "collapsed. First
  choice. Drop down list"). Swipes go in reading order. A double tap clicks through the router.
  Swiping past a list's last visible row makes TalkBack call ShowOnScreen and then ScrollForward. A
  two-finger drag reaches HultUI as a pan, with its fling. A double tap on a field gives it the focus
  and the keyboard. An announcement is spoken ("Counted 1").
- The Fluent sample reads cleanly once the three fixes below were in: "collapsed. File. Menu item. In
  Menu bar", "Minimize. Button", "Edit box. Find a setting", "selected. Home".
- **Found and fixed:** SDL's surface is focusable, so TalkBack read the whole window as one item from
  a finger on a heading; named buttons were read with their icon and label after the name ("Home.
  Unlabelled. Image. Home"); the expanded state was said twice ("collapsed. Collapsed.").
- **Testing traps.** `adb shell input` injects past TalkBack's touch explorer (a tap is a plain click),
  and the emulator console's `event send` drops the positions. Touches written to the touchscreen
  device work (adb root, a google_apis image), with a pressure set (the device has no BTN_TOUCH, and
  pressure 0 is a hover), each press as one write: a `sendevent` per event is too slow for a double
  tap. Such a write leaves Gboard in its hardware-keyboard mode until a reboot, so the keyboard check
  runs first. TalkBack's verbose log, set in its preferences while it is stopped, puts what it says
  in logcat. On the emulator the touch, keyboard and TalkBack checks all pass with the bridge in.

**Left** (all but the last two built the same day: §9):
- ~~**Text by character, word and line.**~~ Built (§9), but for a TextInput.
- ~~**Lists over 2,000 items**~~ Built (§9).
- ~~**Long press**, `ACTION_DISMISS` for dialogs and menus.~~ Built (§9).
- ~~**A list that takes an element's focus** (`AccessibilityNode.FocusHolder`).~~ Built (§9).
- **The phone**, by hand: no root there, so the script cannot drive it. And **Aperture's main views**,
  the plan's exit criterion.
- ~~A text field that has only a placeholder has no accessible name, so TalkBack says "Edit box"
  alone.~~ Done the same day, on the user's word, for every platform: a text field with no other
  name is named by its placeholder, as browsers do (docs/ELEMENTS.md), so TalkBack says it as the
  hint.
- English role words ("Link", "Tab", "Menu item") until HultUI's spoken words are localized; TalkBack's
  own words (button, check box, collapsed) follow the phone's language already.

## 9. The rest of phase 6 (2026-11-26)

All internal; no public API. Each part has its desktop tests, each checked by breaking the part, and
`spikes/AndroidHello/scripts/talkback-a11y-check.ps1` checks them with TalkBack on the emulator
(the spike's `-e sample a11y`; 13 of 13 pass, and `talkback-check.ps1` still 8 of 8).

**Lists over 2,000 items.** The items on screen are the list's virtual children, as a RecyclerView's
are: each a node made when TalkBack asks, from the list's node (where the item is, whether it is
selected) and the list (its label, and whether it is a choice that is on, opens something or can be
used), as UI Automation and AT-SPI read an item served on demand. A binary search finds the first
item on screen. A finger finds an item and a double tap runs it through the router. Their ids go
with the list or with the list's end. TalkBack says "Item 2. 3 of 3000. In list. 3000 items", and
swiping past the last item on screen scrolls the list and reads on.

**A list that holds its pane's focus** (`AccessibilityNode.FocusHolder`, Vesper's pane and its file
list). TalkBack is told the focus is on the list's cursor, and told again when the cursor moves. The
bridge finds the list in the holder's subtree, when the focus moves and then once a frame while
that holder has the focus.

**Text by character, word, line and paragraph.**
- **Which units.** A label or a button offers characters, words and paragraphs (its drawn lines are
  not in the tree). A field that offers its text (TextArea, the rich text editor) offers lines too,
  plus `ACTION_SET_SELECTION`.
- **The cursor.** The bridge keeps where TalkBack is in the text, as a TextView keeps its
  accessibility cursor. Each move is told as a traversal event, which TalkBack speaks.
- **In a field, the caret goes too,** through the router's new internal `SetSelection` (Core
  `IAccessibilityEditActions`). The three editors got an internal `SetSelectionFromAssistiveTool`.
  The rich text editor turns offsets in its reader's text (a `'\n'` between blocks, a picture as its
  words) back into document positions.
- **A caret moved elsewhere** (typed, tapped) starts the moves from there. So does a change of the
  text.
- **Found on the emulator:** TalkBack 15 moves through a field it is only reading by itself ("handled
  by Talkback") and leaves the caret. It calls the field's actions once the field has the input
  focus, as Android's EditText has it.
- **A TextInput too,** since the user's OK the same day (§10): it offers its text on every platform.
- **Each move said once.** A move was spoken twice ("e. e"): the traversal, then the caret-moved event
  the move caused a frame later. The bridge now leaves a caret where TalkBack's own move put it
  untold, and tells one the app or the user moved.

**Long press.** A finger's long press means something where a long-press handler or a context menu
is on the element or above it.
- **The node bit, Android only.** The tree marks such nodes (`AccessibilityNode.OffersLongPress`),
  and a list's items take their list's. Only while a bridge asks for it: the TalkBack bridge turns
  `AccessibilityTreeBuilder.MarksLongPress` on, process-wide, so no desktop build walks up for it.
- **TalkBack's hint.** The node offers `ACTION_LONG_CLICK`, so TalkBack adds "Double-tap and hold to
  long press".
- **Found on the emulator:** TalkBack does not use that action for its double tap held. Android
  passes the held press on to the app, at TalkBack's focus, and HultUI's own touch long press handles
  it (phase 3).
- **So a touch long press with a reader on** opens the menu with its first item current, as from the
  keyboard. TalkBack's focus goes into the menu: "Rename. Menu item. 1 of 2. In Menu. 2 items". The
  menu is an overlay at the tree's end, and was reached before only by swiping past the rest of the
  page.
- **The action itself** (from TalkBack's actions menu) goes through the router: the handler on the
  way up, else the context menu from the keyboard's place.

**Dismiss.** A dialog or a menu offers `ACTION_DISMISS` (and is dismissable). The router's `Dismiss`
closes an open context menu, or sends Escape where the keyboard is, as Back's second step does.
Checked on the desktop only: TalkBack calls it from its actions menu, which the script cannot open.

**A dialog's title.** Found on the emulator: as a dialog opened, the window put the focus on its
Cancel button, and TalkBack said only "Cancel. Button". The pane title told as the pane appeared
was never spoken. The dialog's node now has its name as its container title too (Android 14 and
later). TalkBack says it as its focus goes in, "Cancel. Button. In Delete the file?", and "Out of
Delete the file?" as the dialog closes. The dialog's message is a label to swipe to.

**Aperture's main views,** read through the bridge on the desktop
(`tests/Aperture.Tests/ApertureAppHostTests.TalkBack.cs`). The bridge is installed as the window's
own, over the tests' fake library. Home, a library's grid, a detail page and search each read with
every stop saying something, for example "Resume Solaris, 1972, part-watched, 1 h 49 m left" and
"Play Stalker". What the reading shows for Aperture's phone build (phase 5's app changes):
- **A separator dot** between the server's name and the app's name ("·") is a stop of its own.
- **The window's caption buttons** (Minimize, Maximize, Close) are read; a phone has none.
- **Keyboard shortcuts are part of the names:** "Home (1)", "Search (/)", "Back (Esc)", "Refresh (F5)".
- **A sort button says its state in its name** ("A–Z (on)"), not as a toggled state.

**Seen and left:**
- **No diff tells a change of the long-press bit.** A context menu added to an element that is
  already shown takes a new tree to reach TalkBack.
- **The phone, by hand,** and **Aperture's main views on a device**, which wait for Aperture's
  Android build (phase 5).

## 10. A TextInput's text for a reader (decided and built 2026-11-26)

**The question.** A TextInput's node carried its text as a value only, on every platform. So
TalkBack could not read it by character, nor know where its caret was, and a desktop reader got no
text pattern for it either. A text source, as TextArea has, would fix all three. But NVDA, Narrator
and Orca would then read a single-line field through the text pattern too, as they read a native
edit control. **The user said yes.**

**Built** (`TextInputElement.Accessibility.cs`):
- **A versioned text source**, as the text area's: the tree takes a version (the string, the caret,
  the anchor), and the differ and the window's patch for a keystroke or a caret move compare
  versions. No tree is built for either.
- **Caret moves are told where they are made:** keys, the pointer and its drag, the touch handles,
  a word chosen by touch, a reader's selection, `CursorPosition`, `SelectAll`.
- **The value is told as well as the text** (`AccessibleTextVersion.TellsValue`), as a browser tells
  an `<input>`'s value: a one-line value is short. Each keystroke now has:
  - **UI Automation:** a ValueValue change, TextChanged and TextSelectionChanged.
  - **AT-SPI:** accessible-value, text-changed and text-caret-moved. The object keeps its value.
- **A masked field offers no text,** as before: its value is bullets. The mask going on or off tells
  no text change, which would have told the secret as deleted or inserted.

**Checked:**
- **Desktop:** in the headless tests for UI Automation, AT-SPI and the window's patch, each part
  failing its test without it.
- **Emulator:** TalkBack reads a one-line field by character, and its caret moves
  (`talkback-a11y-check.ps1`, 13 of 13).
- **Not checked:** a real desktop reader (NVDA, Narrator, Orca). The UI Automation and AT-SPI events
  are what a native edit raises, but no one has listened yet.
