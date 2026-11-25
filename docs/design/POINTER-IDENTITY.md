# Pointer identity — design note

**Status (2026-11-26): built, as §8 records.** The user approved §4's API as proposed on
2026-11-25 and took every recommendation in §6:
- other fingers go to the capture holder only
- desktop touchscreens become touch pointers too
- pens stay as mice for now

Plan 20, phase 3, first part ([plan](../plans/20-mobile-support.md) §2.3). It makes touch a pointer of its own, so the next note can build gestures (tap, long
press, pan with inertia, pinch) on it. Gestures are not designed here, but §5 checks this
design leaves them room.

## 1. Today

HultUI's input model has one pointer, and it is a mouse.

- **The platform layer** sends `InputEvent.MouseButtonInput`, `MouseMoveInput`,
  `MouseWheelInput` and `PointerLeftWindowInput` (`src/HultUI.Core/Events/InputEvent.cs`), with
  no pointer ID or type. They are made by SDL's `EventTranslator`, the headless driver and the
  WinForms host.
- **Touch arrives as SDL's synthesized mouse.** `EventTranslator` handles no
  `SDL_EVENT_FINGER_*` or `SDL_EVENT_PEN_*` event and never reads `which`, so it cannot tell a
  synthesized event (`SDL_TOUCH_MOUSEID`) from a mouse. That is why the spike's taps clicked: a
  finger was a left button. It is also why Windows and Linux touchscreens work as mice today.
- **The window keeps one of everything.** `Window` keeps a single capture target
  (`_pointerCaptureTarget`), hovered element, pointer position and set of held buttons
  (`PointerHeld`). `PointerState` keeps one pressed element and one click run (400 ms, 5 units).
  Around them sit autoscroll, text-selection drags, scrollbar drags, internal and OS
  drag-and-drop, tooltips, cursors and the accessibility press hold.
- **Public pointer events** (`PointerEvents.cs`): `PointerDown`, `PointerUp`, `PointerMove`,
  `PointerEnter`, `PointerLeave`, `Click` and `DoubleClick`. They carry `Position`, `Button` and
  `Modifiers`, and the press's `ClickCount` and `CapturePointer()`.
- **Eighteen controls and builders handle presses**, in twenty files: the slider, the scrollbar,
  text elements, the editors, the pickers, the dropdown, menus, `PaneSplitter`, `TableView`,
  `HorizontalPan`, the terminal and the web view. Seven calls capture the pointer.

The single-mouse model works for a mouse. For touch it has three gaps that matter now:
- Hover sticks after a tap: the spike's tapped button stays "hovered".
- Nothing can tell a finger from a mouse, so nothing can pan by finger and select by mouse.
- A second finger is a second button press on whatever is under it.

## 2. The shape: Pointer Events, with one primary pointer

The model is the web's Pointer Events. Every pointer (the mouse, each finger, a pen) has an ID
and a type, and HultUI's existing `Pointer*` events carry them. HultUI's events already have the
web's names, so the change is fields, not new events, plus one new event: cancel.

**The primary pointer** is the mouse, or the first finger to touch the window while no other
finger does. It goes through **the paths that exist today**: hover, capture, press and click,
focus on press, text selection, drags, the drag threshold, and the accessibility press hold.
That keeps the work to a known surface. Today's mouse code is the primary pointer's code, and
every existing test keeps passing unchanged.

**Other pointers** (a second or third finger) do not hit-test and do not click. They go only to
the element holding the primary pointer's capture, as `PointerDown`, `PointerMove` and
`PointerUp` with `IsPrimary = false`. They go nowhere when nothing holds the capture.
- A second finger never presses a button.
- An element that wants several fingers (a future pinch-zoom image, a map) captures the
  primary pointer on its press and then receives the rest.
- That costs no opt-in API: capture already exists.

This is narrower than the web, which hit-tests every finger. That is on purpose: none of
HultUI's eighteen press-handling controls was written for two simultaneous presses. §6 asks the
user.

## 3. Touch rules, inside the framework

These change behaviour only for touch pointers. The mouse keeps today's rules exactly.

1. **No sticky hover.** A touch press makes its target hovered (`PointerEnter` fires, so a
   `:hover`-styled control shows the press). Its release or cancel ends the hover at once
   (`PointerLeave`). Tooltips never open for touch; a long press will, in the gestures note.
   Touch never changes the cursor.
2. **A tap is a click within a slop.** The release makes a `Click` only if the finger stayed
   within 8 DIP of its press (Android's touch slop) and on the pressed element. Today's rule is
   the pressed element alone, which a finger's natural wobble breaks less often than its slide
   does.
3. **Double tap.** `ClickCount` 2 for a second tap within 300 ms and 32 DIP, Android's double-tap
   timing, against the mouse's 400 ms and 5 units. Word selection by double tap works as
   double-click does.
4. **Cancel.** The OS cancels touches: a system gesture, a call, the window losing them. SDL
   says `SDL_EVENT_FINGER_CANCELED`. The window then sends **`PointerCancel`** to the element
   that got the press, and to the capture holder. It sends no `PointerUp` and no `Click`, ends
   the hover, ends any capture, and clears the pressed look. The gestures note uses the same
   event when a pan takes a finger away from the element it landed on (§5).
5. **Button and modifiers.** A finger reports `Button = LeftButton`, as the web's touch contact
   does, so `e.Button == PointerEvent.LeftButton` checks keep working. `Modifiers` come from the
   keyboard state, as for the mouse.

## 4. Public API proposal (needs the user's OK)

In `HultUI.Core.Events`:

```csharp
public enum PointerType { Mouse, Touch, Pen }

public abstract class PointerEvent : ElementEvent
{
    // … Position, LocalPosition, Button, Modifiers as today …

    /// <summary>The mouse is always <see cref="MousePointerId"/>. Each finger gets an ID for as
    /// long as it touches, and the ID is not reused while any finger is down.</summary>
    public int PointerId { get; init; }
    public PointerType PointerType { get; init; }

    /// <summary>The mouse, or the first finger down. Only the primary pointer hit-tests, hovers,
    /// clicks and takes focus; the others reach the primary pointer's capture holder only.</summary>
    public bool IsPrimary { get; init; }

    public const int MousePointerId = 1;
}

/// <summary>The press ended without a release: the OS took the touch, or a gesture claimed it.
/// No PointerUp and no Click follow. Undo what the press started.</summary>
public sealed class PointerCancelEvent : PointerEvent;
```

A builder method, beside today's `OnPointerDown` and `OnPointerUp`
(`src/HultUI.Core/Builders/ElementExtensions.cs`):

```csharp
public static T OnPointerCancel<T>(this T el, Action<PointerCancelEvent> handler) where T : Element;
```

The platform layer. `InputEvent`'s records are public, because the SDL platform, the headless
driver and the WinForms host all make them:

```csharp
public enum TouchPhase { Down, Move, Up, Cancel }

/// <param name="FingerId">The platform's finger, stable from Down to Up or Cancel.</param>
/// <param name="X">In layout units from the window's top left, as the mouse records.</param>
public record TouchInput(long FingerId, TouchPhase Phase, float X, float Y, KeyModifiers Modifiers) : InputEvent;
```

The headless driver (`HultUI.Headless`), so the touch rules are tested on virtual time:

```csharp
window.TouchDown(int finger, float x, float y);
window.TouchMove(int finger, float x, float y);
window.TouchUp(int finger);
window.TouchCancel(int finger);
window.Tap(Element element);          // down and up at the element's centre, as Click aims
window.Tap(float x, float y);
```

Pressure, tilt and a pen's hover are not here. Pens go on arriving as SDL's synthesized mouse
until a pen note adds them. `PointerType.Pen` is in the enum now so that this is no break.

## 5. Inside the framework

- **`EventTranslator`:**
  - sets `SDL_HINT_TOUCH_MOUSE_EVENTS=0`, so SDL stops making mouse events from fingers
  - drops any mouse event whose `which` is `SDL_TOUCH_MOUSEID`, in case one comes anyway
  - translates `SDL_EVENT_FINGER_DOWN`, `MOTION`, `UP` and `CANCELED` into `TouchInput`. SDL's
    finger positions are 0–1 across the window, so they are multiplied by its size in layout
    units, and `windowID` picks the window.
- **`Window`** maps a primary touch onto today's mouse paths, with the pointer's ID and type
  stamped on the events it makes. The touch rules of §3 sit at three places:
  - the hover update
  - `PointerState`'s click synthesis, which gains per-type slop and timing
  - a new cancel path, beside the capture release

  Secondary pointers take a separate path of a few lines, to the capture holder or nowhere.
- **Controls get a cancel path.** Today they end a press on its release or on the end of
  capture. Each of the eighteen must undo its press on `PointerCancel` too: the slider's drag,
  the scrollbar's thumb, a text selection, a picker's spin, the splitter. The apps' own press
  handlers get the same audit when each app comes to touch. Those with capture
  mostly get it free, since cancel ends capture the way release does. The rest are an audit,
  each with a headless test.
- **Others that take mouse input:**
  - the web view (`WebInput`) and the terminal's mouse reporting get a primary touch as the
    mouse they get today
  - the WinForms host makes no `TouchInput` (WinForms' own touch stays mouse)

**Desktop touchscreens change too, on purpose.** Windows and Linux touchscreens become touch
pointers rather than synthesized mice. Taps still click, and hover stops sticking. A finger drag
does what a mouse drag does today until the gestures note makes it pan. One model on every
platform is simpler than an Android-only path, and the desktop gets the touch rules for free.
§6 asks.

**Room for gestures (the next note).** The pan needs to take a finger away from the element it
landed on. A finger lands on a list row, then moves 8 DIP vertically in a scroll view: from then
on the scroll view owns it, and the row must not click. With this design:
- the scroll view learns of the finger by ID and type
- the row gets `PointerCancel`, which ends its press look and its click
- every later move of that finger goes to the scroll view's pan

Pinch is two pointers on one capture holder, which §2 already routes. So gestures need a
"claim this pointer" call, and nothing from this note changes. The wheel and its latching are
untouched; a pan reuses the latching idea.

## 6. Decisions (2026-11-25)

1. **Other fingers go only to the primary pointer's capture holder**, never hit-tested, so a
   second finger never presses a button (§2).
2. **Desktop touchscreens become touch pointers too**, on Windows and Linux. One model
   everywhere (§5).
3. **Pens stay SDL's synthesized mouse** until a pen note. `PointerType.Pen` is in the enum now
   so that adding them later breaks nothing.
4. **The API in §4, with these names**: `PointerId`, `PointerType`, `IsPrimary`,
   `PointerCancelEvent` and `OnPointerCancel`, `TouchInput` and `TouchPhase`, the headless
   `Touch*` and `Tap`.

## 7. Tests, and size

**Headless:**
- a tap clicks
- a slide past 8 DIP does not
- hover ends at release
- a double tap gives `ClickCount` 2
- a cancel gives no click, a cleared pressed style and an ended capture
- a second finger does not click a button
- a second finger reaches a capture holder with `IsPrimary = false`
- IDs are not reused while a finger is down
- every existing mouse test passes untouched, which is the regression check

**Device:** the spike's taps and swipes through `adb shell input` cover one finger. Two fingers
need the emulator's own multi-touch (Ctrl+drag in its window) or a device.

**Size:** M, about two to three weeks:
- translation, the primary-pointer stamping and the touch rules: about a week
- the cancel path and the audit of eighteen controls: about a week
- the headless driver and tests: the rest

The gestures note builds on this, and its work (pan, inertia, nested handoff, long press,
pinch) is the larger part of phase 3.

## 8. As built (2026-11-26)

The API is §4's, unchanged. Where the build differs from the note, or settles what it left open:

- **Stamping is ambient.** `PointerContext` (internal, thread-static) holds the pointer whose
  input the window is handling, and every `PointerEvent` takes its `PointerId`, `PointerType`
  and `IsPrimary` from it as it is made. The seventeen places the mouse paths make events did
  not change. Outside a touch it is the mouse.
- **A primary finger is the mouse's paths** (`Window.Touch.cs`): its down is a hover at the
  finger and a left press, its moves are moves, its up is a release followed by "the pointer
  left the window", which ends the hover. A touch down sends no `PointerMove` first, as the web's
  touch sends none.
- **A finger inside the slop releases on the element it pressed.** The note's rule (within 8 DIP
  *and* on the pressed element) lost taps: a finger landing on a button's label and lifting 6 DIP
  away on the button's padding released on a different element, and the click synthesis matches
  elements exactly. So within the slop the release, and its click, go to the pressed element
  wherever the wobble went, as Android's views do. The mouse keeps the exact-element rule.
- **A cancel goes to the pressed element too**, wherever the finger is, when that element is
  still shown; to the capture holder when one holds the pointer.
- **Touch makes no drag source**, opens no tooltip, changes no cursor and widens no scrollbar.
  Drags from a finger come from a long press (`GESTURES.md` §4).
- **SDL:** `SDL_HINT_TOUCH_MOUSE_EVENTS`, `SDL_HINT_MOUSE_TOUCH_EVENTS` and
  `SDL_HINT_PEN_TOUCH_EVENTS` are all `0`, on every platform. The last keeps a pen from pressing
  twice (once as SDL's mouse, once as a finger). Mouse events from `SDL_TOUCH_MOUSEID` and
  fingers from `SDL_MOUSE_TOUCHID` or `SDL_PEN_TOUCHID` are dropped.
- **The control audit.** Six controls capture the pointer and so receive other fingers: the
  slider, the scrollbar, the table's column grip, the rich text editor, the terminal and the web
  view. Each ignores pointers that are not primary. Those, the number field and the three text
  controls get a `PointerCancel` path: the drag or selection stops where it got to, a value
  reached stays, a link does not open, the terminal's program and the web page hear the button
  come up (neither can be told "cancelled"). The other controls act on the click, which a cancel
  never makes, and their pressed look is the window's, which a cancel clears. The apps' own
  handlers are audited when each app comes to touch.
- **Tests:** `TouchPointerTests` (19), each touch rule with a mouse control beside it. Negative
  controls, each rule broken in turn: the slop, the double tap's reach, the release on the
  pressed element, the cancel event, the tooltip guard, the slider's primary guard and its
  cancel each fail their test.
- **Not verified on a device yet.** The emulator takes `adb shell input tap` and `swipe`; two
  fingers need its own multi-touch or the phone.
