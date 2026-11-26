# Soft keyboard and touch text editing — design note

**Status (2026-11-26): approved by the user, all six recommendations in §6 taken. Built and
headless-tested: §2 and §3 (§8), §4 (§9). Checked on the emulator
(`spikes/AndroidHello/scripts/keyboard-check.ps1`); not yet on the phone.** Plan 20, phase 4
([plan](../plans/20-mobile-support.md) §2.4). It builds on the [gestures note](GESTURES.md):
long press, pan, and `PointerCancel`.

The phone user expects three things of a text field:
- the right keyboard: digits for a number, `@` for an email, no autocorrect in a URL
- the field stays in view above the keyboard, and the content under the keyboard can still be
  scrolled to
- text is selected and edited by touch: a long press selects a word, handles drag its ends, and
  a bar offers Cut, Copy, Paste and Select all

## 1. Today

- **The keyboard shows and hides already.** Focus on a `TextInput`, `TextArea`, rich text
  editor or `ITextServicesClient` calls `ITextInputHost.StartTextInput`
  (`Window.OnFocusChanged`); focus elsewhere calls `StopTextInput`. On Android
  `SDL_StartTextInput` is what shows the soft keyboard, so the spike's field typed
  (`spikes/AndroidHello/README.md`).
- **`TextInputKind` has `Text` and `Password`** (`src/HultUI.Core/Platform/TextInputKind.cs`). A
  `TextInput` asks for `Password` when `Masked`, else `Text` (`TextInputElement.InputKind`,
  internal). The text area and the editor ask for plain text input, with no kind.
  `SDL3Window.StartTextInput` passes `Password` to SDL as
  `SDL_TEXTINPUT_TYPE_TEXT_PASSWORD_HIDDEN` through `SDL_StartTextInputWithProperties`.
- **SDL 3's text-input properties** (as the SDL3-CS binding lists them):
  - `SDL_PROP_TEXTINPUT_TYPE_NUMBER`: text, name, email, username, hidden and visible passwords,
    number, hidden and visible number passwords
  - `SDL_PROP_TEXTINPUT_CAPITALIZATION_NUMBER`: none, sentences, words, letters
  - `SDL_PROP_TEXTINPUT_AUTOCORRECT_BOOLEAN` and `SDL_PROP_TEXTINPUT_MULTILINE_BOOLEAN`
  - `SDL_PROP_TEXTINPUT_ANDROID_INPUTTYPE_NUMBER`, Android's own `InputType` bits, which
    override the rest there
  - none for the IME's action (Android's `imeOptions`: Search, Go, Next, Done on the return key)
- **The keyboard covers the content.** The activity is edge-to-edge (target SDK 35 enforces it),
  so Android no longer resizes the window for the keyboard (`adjustResize`). The keyboard's
  height is the `ime()` inset. `HultUIActivity`'s insets listener reads only
  `systemBars() | displayCutout()` into `Window.SafeArea` (`docs/design/ANDROID-PLATFORM.md` §3).
  The soft keyboard is not part of the safe area (`docs/ANDROID.md`).
- **Touch text editing does not exist.** Since phase 3 a finger places the caret and scrolls;
  it never drag-selects, because that would stop a finger on a field from scrolling the page
  (`GESTURES.md` §2). A long press opens the field's own context menu (Cut, Copy, Paste), the
  gestures note's rule for any element with one. A double tap selects the word, as a double
  click does.

## 2. Input kinds

New `TextInputKind` members, each mapped to SDL's properties, with the capitalization and
autocorrect a phone gives such a field:

| Kind | SDL type | Android `InputType` override | Capitalize | Autocorrect |
|---|---|---|---|---|
| `Text` | text | — | sentences | on |
| `Password` | hidden password | — | none | off |
| `Number` | number | `NUMBER` | — | — |
| `Decimal` | number | `NUMBER \| NUMBER_FLAG_DECIMAL \| NUMBER_FLAG_SIGNED` | — | — |
| `Phone` | text | `PHONE` | — | — |
| `Email` | email | — | none | off |
| `Url` | text | `TEXT \| TEXT_VARIATION_URI` | none | off |
| `Username` | username | — | none | off |
| `Name` | name | — | words | off |
| `Search` | text | — | none | on |

- **Multiline is not a kind.** The text area and the rich text editor always ask for it
  (`SDL_PROP_TEXTINPUT_MULTILINE_BOOLEAN`), so Enter makes a new line rather than closing the
  keyboard. A `TextInput` never does.
- **`NumericUpDown`** asks for `Number` (or `Decimal` when it has decimal places).
- **A `TextInput` says its kind** through a new property; `Masked` still wins and makes it
  `Password`.
- **The desktop** ignores every kind but `Password`, as today.
- **The return key's label** (Search, Go, Next, Done) has no SDL property. It would need a Java
  shim that replaces SDL's input connection. Enter still arrives as `KeyCode.Return` with any
  label, so §6 recommends leaving it out of this phase.

## 3. The keyboard's area

The keyboard is the `ime()` inset, read in the same listener as the safe area and passed to the
window as its own value. Two shapes were considered:

- **A. Fold it into `Window.SafeArea`.** No new API: the root shrinks above the keyboard, and
  `SafeAreaChanged` fires. But an app that pads itself by the safe area (a full-bleed background
  with `LaysOutInSafeArea = false`) would then pad by the keyboard too, and every platform keeps
  the two apart (Android's `systemBars()` and `ime()`, iOS's safe area and keyboard frame,
  Flutter's `padding` and `viewInsets`).
- **B. A value of its own (recommended).**
  - `Window.KeyboardInset` (layout units, the keyboard's height over the window's bottom edge,
    0 when hidden) and `KeyboardInsetChanged`.
  - `Window.LaysOutAboveKeyboard`, default true: the root is laid out in the safe area less the
    keyboard, as `adjustResize` did. An app that animates its own bottom bar sets it false and
    reads the inset.
  - Overlays (menus, dialogs, the edit bar of §4) stay above the keyboard either way.

**The focused field is kept in view.** When the inset grows, or focus moves while it is up,
the window scrolls the focused element into view in its nearest scroll view, in the area above
the keyboard (`ScrollViewElement.ScrollIntoView`, which exists). No API.

**The keyboard animates in** over about 250 ms. Android reports the inset per frame through
`WindowInsetsAnimation`; the window takes only the end value, so a show is one layout, not
fifteen. What is drawn under the rising keyboard for those frames is the old layout, as most apps
do.

**To check on the emulator first:** that edge-to-edge with the activity's default soft-input mode
delivers `ime()` insets to the listener. If it does not, the manifest's `windowSoftInputMode`
gets `adjustResize`, which on API 30+ still dispatches the inset without resizing.

## 4. Touch text editing

For `TextInput`, `TextArea` and the rich text editor, through one internal interface the three
implement (caret and selection rectangles, select the word at a point, move one end of the
selection, the edit items), so the window draws the handles and the bar once.

- **A tap** places the caret, as now. It also shows the **insertion handle** under the caret for
  a few seconds, which drags the caret. A tap on the handle shows the bar with Paste and Select all.
- **A long press on text selects the word under the finger** and shows the **two selection
  handles** and the **edit bar**. This replaces the context menu on text elements (§6 asks). The
  bar's items are the context menu's items, the same list (`BuildContextMenuItems`), laid out as
  a horizontal bar above the selection, or below it when there is no room.
- **Dragging a handle** moves its end of the selection, by character; Android snaps to words while
  a handle moves outwards, and so does this. A handle dragged to the field's edge scrolls the field
  (the existing `TickSelectionEdgeScroll`). The handles are overlays above the text, sized for a
  finger (about 22 DIP), in the theme's primary colour.
- **A pan still scrolls.** A finger that moves on the text without a handle scrolls the page, or
  the text area itself (`IPanScrollable`), and leaves the selection as it is.
- **The bar and handles go** on a tap elsewhere, typing, focus moving, or scrolling (they return
  when the scroll ends, as on Android).
- **No magnifier** in this phase. Android's `Magnifier` widget magnifies a `View`; SDL draws into a
  `SurfaceView`, which needs checking, and HultUI could draw its own from the frame later.
- **The mouse is unchanged.** All of this is for touch pointers only.

## 5. Public API (approved 2026-11-26; as built, one name changed: §8)

```csharp
// HultUI.Core.Platform
public enum TextInputKind
{
    Text, Password,                                       // as today
    Number, Decimal, Phone, Email, Url, Username, Name, Search,
}

// TextInputElement, beside Masked; the builder beside .Masked()
public TextInputKind Kind { get; set; }                    // default Text; Masked makes it Password
public static TextInputElement Kind(this TextInputElement input, TextInputKind kind);

// HultUI.Window, beside SafeArea (shape B of §3)
public float KeyboardInset { get; }                        // layout units; 0 with no soft keyboard
public event Action? KeyboardInsetChanged;
public bool LaysOutAboveKeyboard { get; set; } = true;
```

`ITextInputHost.StartTextInput(TextInputKind)` already exists; it gains a `multiline` overload
with a default, so existing hosts compile. The headless driver records the kind
(`TextInputHost.Kind`) and gets `HeadlessWindow.SetKeyboardInset(float)`, as it has
`SetSafeArea`. Everything in §4 is internal.

## 6. Decisions for the user

1. **The keyboard's area:** shape B, a `KeyboardInset` of its own with the root laid out above
   it by default (recommended), or shape A, folded into the safe area.
2. **The kinds:** the ten in §5 (recommended), or fewer to start (`Number`, `Email`, `Url`).
3. **Return-key labels:** leave them out for now (recommended), or a Java shim over SDL's input
   connection.
4. **Long press on text** selects the word with handles and the edit bar, in place of the
   context menu (recommended, as Android and iOS do), or keeps opening the menu.
5. **No magnifier** in this phase (recommended).
6. **The API in §5, with these names.**

## 7. Tests, and size

**Headless:**
- each kind reaches the host; `Masked` overrides `Kind`; the text area asks for multiline
- a keyboard inset shrinks the root by default, not with `LaysOutAboveKeyboard = false`, and fires
  `KeyboardInsetChanged` once
- the focused field below the keyboard's top is scrolled above it
- a long press on a word selects it and shows the handles and the bar; Copy from the bar copies it
- a handle drag extends the selection, by word outwards; to the edge, the field scrolls
- a pan on a field scrolls the page and keeps the selection
- the mouse's selection and context menu unchanged: the existing suites

**Device:**
- the numeric keyboard for a number field, `@` for email, on the emulator and the phone
- the field above the keyboard in Aperture's login and search
- select, copy and paste a word by touch (the plan's exit criteria)

**Size:** M–L.
- kinds and the keyboard inset: about a week
- touch editing (the interface on three controls, handles, the bar): two to three weeks
- device checks and tuning: the rest

## 8. As built: kinds and the keyboard's area (2026-11-26)

**One name changed from §5.** The property is `TextInputElement.InputKind`, not `Kind`, and the
builder is `.Kind(...)` as proposed. A property and an extension method of one name cannot both
be reached: `input.Kind(...)` binds to the property and fails (CS1955), the reason
`Masked`'s builder is `.Password()` and `Placeholder`'s is `.PlaceholderText()`.

**Kinds:**
- `SDL3Window.StartTextInput(kind, multiline)` always passes SDL's properties: the type,
  capitalization, correction and lines of §2's table, plus Android's `InputType` for `Number`,
  `Decimal`, `Phone` and `Url` (`SDL3Window.TextInputProperties`). SDL's Java hands the type to
  the keyboard as `EditorInfo.inputType`; its `imeOptions` are fixed at no-extract and
  no-fullscreen, which confirms §2: there is no return-key label without a shim.
- **Finding: the lines must always be said.** SDL takes a field to be multiline unless told
  (`SDL_PROP_TEXTINPUT_MULTILINE_BOOLEAN` defaults to true while `SDL_HINT_RETURN_KEY_HIDES_IME`
  is unset), so before this a one-line `TextInput` on Android showed a new-line key.
- A switch of kind on Android starts input again without stopping it first: SDL hands the
  keyboard the new type on any start, and a stop hides the keyboard for a frame. The desktop
  still stops first, for Windows' input method.
- A number field (`NumericUpDown`) asks for `Number`, or `Decimal` when its step has a fraction
  or its range goes below zero (an internal `Element.WantedInputKind`). A terminal or a web view
  asks for `Text`.

**The keyboard's area:**
- `HultUIActivity`'s insets listener reads the `ime()` inset beside the bars, and SDL3Window
  reads it as the safe area is read (`ISafeAreaSource.KeyboardInset`, on the same event).
- `Window.RootArea` takes the larger of the safe area's bottom and the keyboard from the bottom:
  both are measured from the window's edge, so a keyboard over the gesture bar includes it.
- Overlays are laid out in the height above the keyboard and kept there, so a dialog is centred
  above it and a menu never opens under it.
- The focused field is scrolled above the keyboard after the layout of the frame the keyboard
  grew in, or the frame after focus moved while it was up: each scroll view it is in, innermost
  first, with the part of the view under the keyboard taken out of its viewport. With the root
  above the keyboard (the default) no view is under it, and that is a plain scroll into view.
  With no keyboard nothing scrolls: the desktop is unchanged.

**Tests:**
- `SoftKeyboardTests` (26): every kind to the host as one line; `Masked` over the kind; a kind
  switch while focused; a rebuild patches the kind; the text area and the editor ask for lines;
  the number field's kinds; the inset told once, the root above it, `LaysOutAboveKeyboard` off;
  the keyboard and the gesture bar; the field revealed when the keyboard comes, on focus moving,
  and with the root under the keyboard; no scroll with no keyboard; a dialog and a menu above it.
- `TextInputPropertiesTests` (11, SDL3): §2's table, and every kind has its own entry.
- Each rule checked with a negative control: the covered part of the view, the overlay height
  with the menu clamp, the kind's patch, the area's lines, and the reveal itself.

**On the emulator** (`spikes/AndroidHello/scripts/keyboard-check.ps1`, README "The soft keyboard
on the emulator"): the listener gets the `ime()` inset (336 DIP, and 292 for the digits), a
one-line field is `0x1c001`, a number field `0x2`, Back clears the inset. Two findings:
- **§3's fallback was needed, for another reason.** The default soft-input mode (adjust-pan) moved
  the window down by the status bar when the keyboard came up, to reveal SDL's hidden edit view at
  the window's corner: drawn and touched 49 DIP below HultUI's layout. `HultUIActivity` sets
  adjust-resize, which on an edge-to-edge window neither resizes nor pans.
- **A switch of kind with the keyboard up did not reach it.** SDL sends Java a type only when it
  shows the keyboard, which it skips while shown, and SDL's Java only stores the type. So
  `SDL3Window.TextInputSwitched` hands HultUI.Platform.Android the type SDL would have made
  (`SDL3Window.AndroidInputType`, pinned to the emulator's values in `TextInputPropertiesTests`),
  which it gives SDL's `showTextInput` before restarting the keyboard's input. Android does not
  stop input first on a switch (the desktop still does, for Windows).

Still to check on the phone: each keyboard, and the field kept in view in Aperture's login and
search.

## 9. As built: touch text editing (2026-11-26)

**Shape.** An internal `ITouchTextEditing` on `TextInputElement`, `TextAreaElement` and
`RichTextEditorElement` (the selection's caret boxes, the word at a point, a handle's drag, the
menu), and `Window.TouchText.cs`, which draws the handles above the overlays and keeps the bar.
No public API.

**As §4 says:**
- A tap shows the insertion handle for 4 s; a tap on it shows the bar with Paste and Select All;
  a drag of it moves the caret.
- A long press on text selects the word, with two handles and the bar, in place of the context
  menu. An `OnLongPress` handler on the path still comes first. A double tap does the same (its
  second press selects the word, as a double click does).
- A handle's drag moves its end by character, never onto the other end. The finger holds the
  handle below the line; the offset from the finger to the caret's middle is kept for the drag.
- The handles and the bar go on a tap elsewhere, a key or typed text, the mouse, the wheel, the
  focus leaving, or the selection they showed going (cut, typed over).
- The handles are 22 DIP across, in the theme's primary colour, and are not drawn where their
  caret is scrolled out of the field or a scroll view around it.

**Different from §4:**
- **The bar shows Cut, Copy, Paste and Select All**, the enabled ones, not the whole context
  menu: the rich text editor's menu has a link's and a table's items. When the menu holds more,
  the bar ends with "⋯", which opens the whole menu. Undo and Redo are left to the keyboard.
- **A scroll keeps the handles**, following the text, rather than hiding them until it ends; the
  bar goes while it scrolls and comes back.
- **No word snapping** while a handle moves outwards: by character both ways, for now.
- **A handle at the field's edge** scrolls the text input and the text area by their own caret
  reveal, a step per move, not `TickSelectionEdgeScroll` (which serves labels in a scroll view).
- **Finding: a finger on empty space no longer clears the focus at the press.** A press there
  may be a scroll starting, and clearing the focus dropped the field's keyboard and its
  selection under any scroll of the page. The focus is cleared when the finger lifts as a tap,
  so a tap there still does what a click does.

**Tests:** `TouchTextEditingTests` (25): the long press, the handles' place and colour, the double
tap, Copy, Select All and Cut from the bar, the bar above the selection and below it with no
room, each handle's drag, a handle never collapsing the selection, the insertion handle's tap,
drag and timeout, a tap elsewhere, typing (with a selection and without), the mouse, a pan on
the page, the text area and the editor, a scrolled page, an `OnLongPress` handler around a field,
and the focus under a tap and a scroll on empty space. Each rule checked with a negative control.

**Found in passing, not fixed here:** a mouse click on a text area inside a scrolled page puts
the caret on the wrong line: the area reads the window's point as its layout's. The touch path
maps the finger through the area's drawn place and is right.
