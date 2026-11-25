# TalkBack bridge — design note

**Status (2026-11-26): proposal, nothing built.** Needs the user's OK on the decisions in §6;
it adds no public API. Plan 20, phase 6 ([plan](../plans/20-mobile-support.md) §2.9). Required
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
