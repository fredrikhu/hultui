# D-pad focus: arrows that move the focus — design note

**Status (2026-11-28): approved.** The user took both recommendations in §6: a public
`Window.ArrowKeysMoveFocus`, on by default only where a D-pad navigates. Plan 20 phase 5, the Android TV build
(`docs/plans/20-mobile-support.md` §3). Found on the user's Shield TV (2019 Pro, Android 11,
960×540 DIP).

## 1. What the Shield showed

A TV remote has a D-pad, OK and Back, and no Tab. On the Shield SDL delivers the D-pad as the
arrow keys, OK as Enter (it pressed a focused button) and Back as `KeyCode.Back`.
- **Arrows never move the focus.** Only Tab does (`docs/FOCUS.md`), so on the sign-in page no
  field and no button can be reached, nor an item page's Play. Aperture's own card cursor
  walks the posters, so browsing half works.
- **Nothing has the focus at start.** No ring, and nothing for OK to press.
- **A rebuild that replaces the focused element drops the focus.** On the sign-in page an
  error banner inserted above the buttons left nothing focused, and so did the Quick Connect
  code replacing the form. The next key had nowhere to go.
- **The ring hides until a key is pressed** (`_focusFromKeyboard` starts false).

Android's own Views do all of this natively (`FocusFinder`, `View.requestFocus` on the first
focusable, focus kept across layout changes), which is what every TV app relies on.

## 2. The rule

**A bare arrow that nothing used moves the focus.** In `Window`'s key path, after a key down has
been dispatched to the focused element and bubbled (`Window.cs`, the general branch), an Up,
Down, Left or Right with no modifiers that came back with neither `Handled` nor
`StopPropagation` moves the focus to the nearest focusable element in that direction.
- **Candidates:** focusable, `TabIndex >= 0` (a roving list shows one stop, as for Tab), in the
  same `FocusScope()` (a modal dialog keeps the D-pad inside it, a focused popover is its own
  scope), with a non-empty box. Off-screen ones in a scroll view count only from inside that scroll
  view: Down past the last visible row reaches the next, but from outside, the hidden part is not
  there (on the Shield, Down from the rail's last door went to a card below the bottom of Home's
  scrolled page, and the next OK opened it).
- **Geometry:** `Element.WindowBorderBox` (scroll and sticky applied, overlay origin added),
  never layout boxes, which are unscrolled.
- **Which one:** Android's `FocusFinder` rule. A candidate must lie in the direction (its far
  edge past the current box's near edge, and, as the web's spatial navigation has it, its centre
  past the current box's leading edge: by Android's test alone Up from a top-row poster on the
  Shield took the rail's door beside it, a few pixels higher). One overlapping the current box across the other axis
  (in its "beam") beats one that does not; then the smallest `13·major² + minor²`, where major is
  the gap along the direction and minor the offset of the centres across it. Ties keep document
  order.
- **No candidate:** nothing happens. No wrap: a TV's edge is an edge.
- **Nothing focused:** an arrow focuses the scope's first stop, as the first Tab does.
- **After the move,** each scroll view around the new element scrolls it into view by the least
  scroll (the loop `RevealFocusAboveKeyboard` already runs for the soft keyboard). Focus moved by
  the D-pad to an element no one can see is the one thing worse than no move.
- **`FocusEvent.Reason`:** `Next` for Down and Right, `Previous` for Up and Left. No new enum
  member; an element with a tab order of its own (a web page) starts at its first or last stop.

Text fields, sliders, lists and the rest keep their arrows by consuming them, which every one
already does except the slider (§4).

## 3. What else a D-pad window does

The same switch (§6) turns on three more, because without them the rule above has nothing to
start from:
1. **Something always has the focus.** After a frame's build, a window with nothing focused in
   its scope focuses the scope's first stop. A dialog already does this; the root now does too.
2. **A focused element that leaves keeps the focus where it was.** When the reconciler drops the
   focused element, the window remembers its `WindowBorderBox`, and after the frame focuses the
   candidate nearest to that box (the same distance, no direction). The sign-in's banner case
   lands back on "Sign in with a code"; the Quick Connect case on Cancel.
3. **The ring always shows.** `_focusFromKeyboard` starts true. A pointer press still hides it,
   for a TV with a mouse plugged in.
4. **A field waits for OK before its keyboard.** Focus reaching a text field by the D-pad (or by
   the window's own choice) readies it but asks for no text input: on the Shield the Leanback
   keyboard came up at once and took the D-pad. OK (Enter) on a focused field whose keyboard is not
   open opens it and is not the field's Enter (which would submit the form); while it is open,
   Enter (the keyboard's Done) is the field's as always. A pointer press still opens it at once.
5. **A modal overlay that opened without taking the focus** gets it, at its first stop, so the
   D-pad never acts on the page behind a dialog.

Building it showed where Android's rule alone falls short: from the last of a side rail's upper
buttons, the next rail button sits at the window's foot and a nearer button in the content wins.
Android TV apps keep the D-pad in their rail themselves (`nextFocusDown`, Leanback's focus-search
listeners); Aperture does too (§5). A framework "focus group" could do it for every app, but it
would be public API, and one app needs it so far.

## 4. Framework fixes the rule needs (audit of every arrow handler in `src/`)

Every handler but one marks the arrows it acts on. Changes:
- **`SliderElement`** acts on all four arrows and marks none: with the rule, the value would step
  *and* the focus would leave. It sets `Handled` on what it acts on. With the switch on, only Left
  and Right (it is horizontal); Up and Down are left for the focus, as Android's `SeekBar` does.
  `VideoPlayer`'s check for a slider target stays harmless.
- **`DropdownElement` and `DatePickerElement`, closed:** Down opens them, so the D-pad could never
  pass one. With the switch on, a closed one leaves Up and Down alone; OK (Enter) opens it.
- **`VirtualListElement.KeyboardScrollEnabled`** eats Up and Down for any focused row inside the
  list (it does not check the target). With the switch on it leaves keys whose target is not the
  list itself.

Left as they are (each traps an arrow on purpose, and a TV app avoids them or they are rare there):
text areas and the rich text editor (all four), a text input (Left and Right; Up and Down leave),
`CalendarElement` and `TimePickerElement` (all four), a terminal and a web view with keys forwarded
(everything), `NumericUpDownElement` (Up and Down; sideways leaves), tab strips (along the strip;
across leaves), the scrollbar and splitter (along their axis). Context menus and the menu bar take
their keys before dispatch and are unchanged.

**`VideoPlayer`** traps all four arrows (seek and volume) for any focused control inside it, so the
D-pad cannot walk the player's own buttons. A TV player wants Left and Right to seek while its
controls are hidden and to move between them while they show. That is the player step (the remote's
media keys), not this one.

## 5. Aperture

- **The card cursor only when it has the keyboard.** The root takes the arrows for the cursor
  wherever the focus is (`ApertureApp.cs`, `HandleCursorKey`), so with the rail focused Down would
  jump into the posters. With the switch on, the cursor takes them only when the focus is on its
  card or nowhere.
- **The cursor's edges let go.** At an end the cursor returns "still ours" (`MoveCursor`,
  `MoveCursorAcrossShelves`), so the rail and the top bar could never be reached. With the switch
  on, Left at a shelf's first card (or a grid's first column), Up at the top shelf or row, and
  Right and Down at the last return false, and the rule takes the key: Left to the rail, Up to the
  library's bar. Left no longer wraps into the shelf above.
- **An item page opens on a verb, never a toggle.** It hands the keyboard to its first verb
  (`MarkArrivalFocus`); a series or a folder has no Play, so that was the Watched toggle, and OK
  toggled it. The first verb is now Play, Resume, Play all or the like; with none, once the page
  knows (a series' next episode and a season's episodes load after it), its first season or episode
  (`MarkArrivalCard`). This one holds on the desktop too, where Enter toggled Watched the same way.
- **The first page starts on its first card**, as every page opened later does (`Arrive`): the
  window's own choice was the title bar's account button. And a list that changes under a cursor
  the arrival placed and no key moved (Home's rows loading one by one) starts on its first card
  again, where it went back to nowhere.
- **The rail keeps Up and Down.** They walk its doors in order; at either end the window's rule
  takes the key. **Right goes back to where the page was left** (the cursor's card), as a TV app's
  side rail returns to its content; by the rule it went to what sat level with the door, a shelf's
  arrows from the top doors, which the user found odd. OK on the door of the page already open hands
  the keyboard to its content too (it stayed on the door).
- **Every card is a stop on a D-pad.** The walk shows Tab one stop (the cursor's card), and the
  window's rule saw only that card among a shelf's: from anything that was not a card it skipped
  whole shelves to it, which the user saw as Up "registering twice". On a D-pad every card is a stop,
  and a card taking the keyboard puts the cursor on it (`OnWalkCardFocused`).
- **Search registers its results before making their cards.** A card asks the list what it is to the
  keyboard; made against the last list, the first result was no stop, so neither Tab (desktop too) nor
  the D-pad could reach the results from the field.
- **Focus moves count only when the user made them.** A page keeps the focus wherever it went
  since it opened, rather than handing it to its verb or first card (`KeyboardMovedSinceArrival`).
  On a D-pad that is only a move after an input: the window's own choice while the page loads is
  replaced by the page's.
- **Not yet:** the server field asking for a URL keyboard (the Leanback keyboard capitalised
  "Jellyfin.home"), and the keyboard's Done on the server field submitting the form with the user
  name still empty. Both small, both after the Shield check.

## 6. Open questions for the user

1. **The switch — public API.** Proposed: `Window.ArrowKeysMoveFocus { get; set; }`, everything in
   §2 to §4 behind it. Without a public switch it could only be turned on by the platform, and
   Aperture's tests (and a desktop app made for a TV or a gamepad) could not turn it on.
2. **Its default.** Proposed: on where the device is navigated by a D-pad (Android's
   `Configuration.Navigation == Dpad` with no touch screen: a TV, a set-top box), off elsewhere, so
   phones, tablets and the desktop behave exactly as today. The alternative is on for all of
   Android, as Android's own Views move focus with a keyboard's arrows on a phone too.

## 7. Tests

Headless (`HultUI.Headless.Tests`): the beam rule on a grid of buttons; an arrow a text input used
does not move; a dialog keeps the D-pad inside; Down into a scroll view's hidden row scrolls it in;
nothing focused → first stop; a replaced focused element hands the focus to its neighbour; the
slider's Up and Down leave it. Aperture: the rail reached by Left from Home's first card and from
a grid's first column; Up from the top shelf; an item page of a series opens on its first season,
not Watched. Then the Shield by hand.
