# Touch gestures — design note

**Status (2026-11-25): design approved, nothing built.** The user approved §5's API as
proposed and took every recommendation in §7:
- latch at the start
- stop at the edge
- Android-like physics everywhere
- long press opens the context menu, else the tooltip
- touch scrollbars later Plan 20, phase 3, second part ([plan](../plans/20-mobile-support.md)
§2.3). It builds on the approved [pointer identity note](POINTER-IDENTITY.md): one primary
pointer, touch pointers with IDs, `PointerCancel`, and other fingers routed to the capture
holder.

The gestures are the ones a phone user expects of any list or page:
- a tap clicks, which the pointer note already gives
- a finger drags the content with it
- a flick throws it, and it slows to a stop
- a horizontal flick on a row of posters moves the row, and a vertical one moves the page
- a long press does what a right-click does
- two fingers pinch

The spike's swipe did nothing (`spikes/AndroidHello/README.md`, "What broke" 5).

## 1. Today

- **Scrolling is wheel-driven, and that model is what pan reuses.**
  - A `ScrollEvent` bubbles. Each scroller takes the axes it can move and reports them with
    `Consume`, through `ConsumedX` and `ConsumedY` (`PointerEvents.cs`).
  - `WheelGesture` latches a run of wheel events to the scroller that used the first, with a
    300 ms gap, and lets go when the pointer leaves it or a rebuild replaces it.
  - Pan needs the same answers: who scrolls, in which axis, and what happens at the edge.
- **The window already drives scrolling per frame.**
  - The middle-button autoscroll (`TickAutoScroll`) and the drag-select edge scroll
    (`TickSelectionEdgeScroll`) run each frame from `Window`. They keep frames coming while they
    run, and carry the scroller's position across a rebuild that replaced it.
  - Autoscroll reaches its surfaces through `IAutoScrollable` (`ScrollBy(dx, dy)`, which returns
    whether anything moved). `ScrollViewElement` (and `VirtualListElement`, which derives from
    it), `TextAreaElement` and the web view implement it.
  - `ISidewaysScroller` covers horizontal-only surfaces, such as `HorizontalPan`.
  - Inertia is one more per-frame tick of the same kind.
- **Mouse drags mean other things, and must keep meaning them:**
  - drag-select in text elements
  - drag-and-drop after 5 units (`Window.DragThreshold`)
  - scrollbar and slider thumbs, which capture the pointer
  - the splitter
- **Long press has an obvious meaning already.** `.ContextMenu(...)` attaches a menu that a
  right-click opens (`Window.TryShowContextMenu`), and `.Tooltip(...)` a hover tip. Touch has
  neither right-click nor hover.

## 2. The model: competing recognizers, decided by movement, time and lifting

A touch on a phone is ambiguous when it lands. It may become a tap, a scroll in either axis, a
drag of a slider or a long press. The platforms all settle this the same way: Android's
intercept, iOS's recognizers, Flutter's gesture arena. Every candidate watches the finger, and
the first whose condition is met **claims** it. The others, and the element under the finger,
are cancelled.

**On a touch press** (primary pointer only), the window collects candidates along the hit path,
from the target out to the root:
- **Scrollers.** Every scroller (§3) that can move in some axis, with those axes.
- **Pan handlers.** Elements with an `OnPan` handler (§5), with their axis.
- **Long press.** Elements with a long-press meaning: an `OnLongPress` handler, a context menu,
  a tooltip, or later a text selection or a drag source.
- **Tap.** Always, through the pointer note's click rule.

The press itself is dispatched as today, so a button shows its pressed look at once.

**It is settled by the first of these:**

1. **Movement past the touch slop** (8 DIP). The movement's dominant axis picks the claimant:
   the innermost candidate that pans in that axis **and can still move that way**. A scroller at
   its top cannot move further up, so a downward pull passes to the scroller around it. This is
   the wheel's `Consume` rule at the start of the gesture.
   - The claimant takes the pointer.
   - The pressed element gets `PointerCancel`: no click, and the pressed look ends.
   - Every later move of that finger goes to the claimant.
2. **Lifting before anything claimed.** It is a tap, so the click follows (pointer note §3).
3. **Holding 500 ms without passing the slop**, with a long-press candidate on the path. The
   long press claims: §4. Without one, nothing happens until the finger moves or lifts.
4. **The OS cancelling the touch.** `PointerCancel` everywhere, and nothing claims.

**An element that captures the pointer on a touch press opts out.** It owns the finger from the
press, and no scroller can claim it. That is the escape hatch for a drawing surface or a custom
control. Text elements and the editors capture on a mouse press, for drag-select. On a touch
press they must not, or a finger landing on a text field could never scroll the page. On touch,
text selection starts from a long press instead (phase 4). This is part of the pointer note's
audit of eighteen controls.

**The slider is a pan candidate, not a capture.** A horizontal slider in a vertical list is a
horizontal pan candidate. A vertical swipe that starts on it scrolls the list, and a horizontal
one moves the thumb. That is how sliders behave inside scrolling pages on Android and iOS.

## 3. Pan, nested scrollers, and inertia

**What can be panned.** The scrollers that exist are `ScrollViewElement` (and
`VirtualListElement`), `TextAreaElement`, the rich text editor and `HorizontalPan`. They meet
the window through one interface, `IPanScrollable` (§5). It is public, like `IAutoScrollable`,
so an app's own scroller can take part. It asks two things: can you move this way, and move by
this much, telling me how much you moved.

**The content follows the finger 1:1.** Each move of a claimed finger scrolls by the finger's
movement in the claimed axis, or both axes for a two-axis scroller. Moves are in layout units,
so density does not enter. The scroll marks paint as wheel scrolling does, so the frame cost is
today's scroll cost, which plan 17 spent many rounds on.

**Nested scrollers: latch at the start, no handoff mid-gesture.** The claimant is chosen once,
by the rule in §2 item 1. If the content then reaches its edge mid-drag, it stops there; the
scroller around it does not take over the rest of the same gesture. This is the wheel latch's
behaviour (`WheelGesture`) and iOS's. Android's nested scrolling does hand off. It is less
predictable, and it would make the wheel and the finger differ. §7 asks.

**Cross-axis is the case Aperture needs.** A shelf of posters scrolls horizontally inside a page
that scrolls vertically. A mostly-horizontal swipe on a poster claims the shelf; a
mostly-vertical one, the page. The dominant-axis rule decides at 8 DIP, before either moves
visibly. The tapped poster's click is cancelled either way.

**Inertia (fling).**
- **Velocity.** On release, the velocity is fitted over the last 100 ms of moves. A least-squares
  line through the samples, as Android's `VelocityTracker` does, is steadier than the last
  delta.
- **Thresholds.** Below 50 DIP/s there is no fling, Android's minimum. Above 8,000 DIP/s it is
  clamped.
- **The decay.** The fling then runs each frame in `Window`, beside `TickAutoScroll`. Velocity
  decays exponentially, with a time constant of about 325 ms: a flick at 2,000 DIP/s travels
  about 650 DIP, or two-thirds of a phone screen. Each tick advances by elapsed time, not by
  frame count, so a dropped frame does not shorten the throw.
- **It stops when:**
  - it reaches the content's edge (clamped, no bounce: §7)
  - the velocity falls under about 10 DIP/s
  - a new touch lands on that scroller: the fling stops, and that touch's tap is not a click,
    the convention everywhere
  - a wheel event arrives
  - the scroller leaves the tree
- **Rebuilds.** Across a rebuild that replaces the scroller, the fling carries its offset as the
  autoscroll does (`IAutoScrollable.ScrollOffsetY` today; generalized for both axes).
- **Virtual time.** The fling's clock is the platform's `TimeProvider`, so headless tests run a
  fling on virtual time and assert where it ends to the unit.

**Pan for other elements.** An element with `OnPan(axis, …)` (§5) gets `PanStart`, `PanUpdate`
(the delta and the total) and `PanEnd` (the release velocity) when it claims. Swipe-to-dismiss,
a custom carousel and a pull-to-refresh are built from these. Inertia is a scroller's
behaviour, not a pan handler's: `PanEnd` hands over the velocity, and the app animates with
HultUI's tweens if it wants a throw.

**Scrollbars on touch.** A desktop scrollbar is a target for the mouse. On touch-only use, the
platforms show a thin indicator while scrolling and hide it after. That is a styling change
with no gesture logic in it; §7 asks whether it belongs here.

## 4. Long press

After 500 ms without passing the slop, the long press claims the finger. The pressed element
gets `PointerCancel`, so no click follows the release, and then the first of these that applies
happens:

1. **An `OnLongPress` handler** on the path runs (innermost first, bubbling like a click).
2. **A context menu.** An element with `.ContextMenu(...)` opens it at the finger. This is the
   same path as right-click (`TryShowContextMenu`), so every menu written for the mouse works
   by touch with no app change.
3. **A tooltip.** An element with `.Tooltip(...)` shows it while the finger stays down. This is
   how Android shows the labels of icon buttons.
4. **Later, in their own work:** text elements start a selection at the word (phase 4), and a
   drag source starts a drag-and-drop on the next move. On touch, drag-and-drop starts only
   from a long press; a plain move is a pan.

500 ms is Android's long-standing long-press time (recent versions use 400 ms). It is a
constant to tune on a device.

**Haptic feedback** on a long press is Android's convention, and needs Java glue in the
platform package. It is not designed here.

## 5. Public API proposal (needs the user's OK)

In `HultUI.Core.Events` and the builders:

```csharp
public enum PanAxis { Horizontal, Vertical, Both }

public sealed class LongPressEvent : PointerEvent;

public sealed class PanStartEvent : PointerEvent;
public sealed class PanUpdateEvent : PointerEvent
{
    public Point Delta { get; init; }   // since the last update
    public Point Total { get; init; }   // since the press
}
public sealed class PanEndEvent : PointerEvent
{
    public Point Velocity { get; init; }   // DIP per second, at release; zero after a cancel
    public bool Cancelled { get; init; }
}

// Builders, beside OnClick:
public static T OnLongPress<T>(this T el, Action<LongPressEvent> handler) where T : Element;
public static T OnPan<T>(this T el, PanAxis axis,
    Action<PanStartEvent>? start, Action<PanUpdateEvent> update, Action<PanEndEvent>? end) where T : Element;
```

In `HultUI.Core.Elements`, for scrollers, beside `IAutoScrollable`:

```csharp
/// <summary>A surface a finger can scroll. ScrollViewElement, TextAreaElement, the rich text
/// editor and HorizontalPan implement it; implement it on any other surface that scrolls
/// content it owns.</summary>
public interface IPanScrollable
{
    /// <summary>Whether the content can move by a positive (+1) or negative (-1) delta in this axis.</summary>
    bool CanPan(PanAxis axis, int direction);
    /// <summary>Scrolls by a delta (positive is further down and right through the content) and
    /// returns how much it actually moved.</summary>
    Point PanBy(float dx, float dy);
    /// <summary>The offset, so a gesture can carry its position across a rebuild.</summary>
    Point PanOffset { get; set; }
}
```

The headless driver, on virtual time:

```csharp
window.Swipe(Point from, Point to, TimeSpan duration, int steps = 10);  // one finger, then lifts
window.LongPress(Element element);                                      // holds past the long-press time
```

A fling is a fast `Swipe` followed by the session's existing `AdvanceTime` and `PumpFrames`
(or `PumpUntil`) to let it run out. Pinch is §6, and
not in this list.

The physics constants are not API. The slop, the long-press time, the velocity window,
minimum and maximum, and the decay constant are internal constants. They are tuned on a device
and can become settings later if an app has a reason.

## 6. Pinch: designed, not built

Two fingers on one element need the pointer note's routing: the first finger's press captures,
and the second arrives at the capture holder with `IsPrimary = false`. A `PinchEvent` (focus
point, scale since the start, scale since the last update) and an `OnPinch` builder would sit
on that. Nothing in HultUI or its apps zooms today. Aperture's image views and a future map are
the candidates, so pinch is built when the first of them needs it, with its own small API item
then. Nothing in §2–§5 needs to change for it.

## 7. Decisions (2026-11-25)

1. **Nested scrollers latch at the start**, with no handoff mid-gesture, as the wheel does
   (§3).
2. **At the edge the content stops**, with no visual effect for now. A stretch or bounce can
   come later as a paint-time effect, without changing the gesture code.
3. **One set of physics, Android-like, on every platform.** Revisit if iOS returns.
4. **Long press:** an `OnLongPress` handler, else the context menu, else the tooltip (§4).
5. **Touch scrollbars are a separate styling item**, later.
6. **The API in §5, with these names and shapes**: `OnLongPress`, `OnPan(PanAxis, …)` with
   `PanStartEvent`, `PanUpdateEvent` and `PanEndEvent`, `IPanScrollable`, and the headless
   `Swipe` and `LongPress`.

## 8. Tests, and size

**Headless, on virtual time:**
- a tap still clicks
- a vertical swipe on a button in a list scrolls the list, cancels the button and does not click
- a horizontal swipe on a poster in a shelf scrolls the shelf, not the page; the reverse for
  vertical
- an inner scroller at its edge lets the outer one claim
- a slider in a list takes horizontal swipes and gives vertical ones to the list
- a capture on press keeps the finger from every scroller
- a fling's end offset is stable to the unit across frame rates (30, 60, 120 Hz on the virtual
  clock)
- a touch during a fling stops it without clicking
- a fling across a rebuild that replaced the scroller ends where it would have
- a long press opens the context menu and no click follows; with no menu, the tooltip
- the wheel and mouse drags unchanged: the existing suites

**Device:**
- the spike's `adb shell input swipe` (with a duration, for a fling) and `input draganddrop` /
  long `swipe` at one point for a long press
- frame time while panning and flinging Aperture's grid, measured against the desktop numbers
  in plan 17's ledger, on the emulator and on the test phone

**Size:** L, about four to six weeks after the pointer note's work:
- the candidate collection and claiming: about a week
- pan for the five scrollers through `IPanScrollable`: about a week
- the fling and its carry across rebuilds: about a week
- long press with the context-menu and tooltip paths: a few days
- the text-element capture change and the slider as a candidate: a few days
- tests, and tuning on a device, take the rest

Pinch is S–M when it is wanted.
