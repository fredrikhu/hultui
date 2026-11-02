# Focus and Keyboard Traversal

Focus decides where keys go. HultUI keeps one focused element per window, moves it with
Tab and Shift+Tab, sets it on click, and dispatches every key event to it first. This
page is the contract, and the ways it can silently go wrong.

```csharp
using HultUI.Core.Events;   // FocusEvent, BlurEvent, KeyDownEvent
```

---

## Making something focusable

Nothing is focusable by default except the framework's own inputs.

```csharp
var pane = Box.New();
pane.IsFocusable = true;    // Tab can land here, click focuses it
pane.TabIndex = 2;          // order among focusables; see below
pane.ShowsFocusVisual = true;  // opt-in focus ring
pane.On<FocusEvent>(_ => _active = Pane.Left);
pane.On<BlurEvent>(_ => ...);
```

| Member | Meaning |
|---|---|
| `IsFocusable` | may hold focus at all (default `false`) |
| `TabIndex` | Tab order. Positive values first, ascending; **`0` sorts after every positive value** (HTML's rule); a negative value is skipped by Tab but can still be focused by click or by `Focus()` |
| `Focus()` | focus this element; a silent no-op if it is not focusable |
| `IsFocused` | read-only; true while the element holds focus, focus style or not |
| `Window.FocusedElement` | the current holder, or `null` |
| `Window.MoveFocus(forward, wrap = true)`, `Window.FocusEdge(first)` | programmatic traversal |
| `Window.ClearFocus()` | the only blur — there is no `Element.Blur()` |

Equal `TabIndex` values keep document order (pre-order over `Children`). Traversal
does not look at visibility, opacity or `IsHitTestVisible`: an element that is
focusable and has a non-negative index is in the ring, drawn or not.

`FocusEvent` and `BlurEvent` are dispatched **directly** to the element — they do not
bubble. Read state from them rather than deriving it from clicks: click already focuses
the nearest focusable ancestor of what was hit, so Tab and click share one path and
stay in sync. `FocusEvent.Reason` says how focus arrived — `Next` (Tab), `Previous`
(Shift+Tab), `Pointer` (a press) or `Programmatic` — for an element with a tab order
of its own inside it, such as a web page, which starts at its first stop on `Next`
and its last on `Previous`.

---

## The focus ring

`ShowsFocusVisual` asks the window to draw the theme's two-ring visual
(`Theme.FocusVisual`) around the element while it holds focus. With `KeyboardOnly`, the
default, the ring shows only while the keyboard is in use, the rule browsers use for
`:focus-visible`:

- **A pointer press hides it**, so a clicked button does not wear a ring.
- **Any key press shows it**, on whatever has focus and wherever a handler moves focus
  next: Tab, an arrow through a tab strip, Escape closing a dialog. Until 2026-10-05 only
  Tab and Shift+Tab did, so a control whose arrow keys move focus moved it unseen after
  a click.
- **A modifier alone does not, nor a chord with Ctrl, Alt or Super**: Ctrl+C after a click
  stays ringless. Shift counts (Shift+Tab, Shift+arrow).

`KeyboardOnly = false` draws it however focus arrived.

---

## Tab reaches your handler first

On Tab the window dispatches an ordinary bubbling `KeyDownEvent` to the focused
element (or the root when nothing is focused) **before** it moves focus. A handler
that sets `StopPropagation` cancels the default traversal, the way a browser's
`preventDefault` on a Tab keydown does. So an app-level "Tab switches pane" binding
works — as long as it stops propagation; otherwise the pane switches *and* focus moves.

While an element is focused, every key event dispatches to it as target and bubbles up
to the root. Handle keys on the element with `On<KeyDownEvent>` and set
`StopPropagation` when the root must not act on the same key.

---

## What a key event says

`KeyDownEvent.Key` is a **position**: the key where that letter sits on a US keyboard,
whatever the layout prints on it. The rest says what the key means:

| Member | What it is |
|---|---|
| `Key` | The position (`KeyCode.Y` is QWERTZ's Z key). Keypad keys are `Numpad0`…`NumpadEquals`, with NumLock on or off. |
| `LayoutKey` | The character the key types on the layout, Shift and AltGr applied, Ctrl and Alt ignored: `'z'` for that QWERTZ key. **Match shortcuts against this.** 0 for keys that type nothing, or when the platform did not say. |
| `Modifiers` | Shift, Ctrl, Alt, Super, and the CapsLock and NumLock states. |
| `ModifierKeys` | The same with left and right apart, and `AltGr`. AltGr is not Alt: with it held, `Modifiers` has no Alt, so an Alt+2 shortcut does not fire when a Swede types `@`. |
| `Text` | The text the keystroke typed, which still arrives next as a `TextInputEvent`. Empty for a key that types nothing; null when the platform cannot say. |
| `Digit` | 0–9 for the number row and, with NumLock on, the keypad; null otherwise. |
| `ScanCode` | The physical key as SDL reports it (the USB HID usage), for keys `KeyCode` has no name for. |

A Ctrl+Z binding written as `e.Key == KeyCode.Z` fires on QWERTZ's Y key; write
`e.LayoutKey is 'z' or 'Z'` and fall back to `Key` when `LayoutKey` is 0 (the WinForms
host and synthesized keys give none) — or let `KeyChord` do it, below.

---

## Shortcuts: `KeyChord` and `ShortcutMap`

A `KeyChord` is one shortcut — a key and its modifiers — and `chord.Matches(e)` applies the
rules above so a handler does not have to:

- **The modifiers must be exactly the chord's.** `new KeyChord(KeyCode.C)` is c, not Ctrl+C.
  Switching on `e.Key` alone is the trap this replaces: a view whose `c` checked out a branch
  checked it out on Ctrl+C too. Super counts as Ctrl (⌘ on a Mac); CapsLock and NumLock never
  count; AltGr is not Alt.
- **A letter is the letter printed on the key** (`LayoutKey`, falling back to the US position).
  **Any other key is a position.** **A digit** is the number row or, with NumLock, the keypad.
- **A character chord**, `KeyChord.Of('+', KeyModifiers.Ctrl)`, is the symbol the layout
  types, Shift ignored — for keys that mean their symbol, as zoom means +. On a Swedish keyboard
  `+` has a key of its own, at the US `-` position: matched by position, Ctrl and + zoomed out.

`ToString()` writes the one notation (`Ctrl+Shift+P`, `Alt+↑`, `Ctrl+,`, a bare letter as
typed: `m`) and `KeyChord.Parse` reads it back, along with the older spellings settings files
hold (`Digit1`, `PgUp`, `cmd+p`).

A `ShortcutMap` is an app's table of them — id, group, title, chords — and the one thing both
the handlers and the hints read, so a key cannot be shown one way and bound another:

```csharp
var keys = new ShortcutMap();
keys.Add("palette", "Global", "Command palette", new(KeyCode.P, KeyModifiers.Ctrl));
keys.Add("mr.approve", "Merge requests", "Approve", new(KeyCode.A)).Legend("approve");
keys.Add("view", "Global", "Views", digits).Shown("1 – 7");

if (keys.Matches("palette", e)) { TogglePalette(); e.StopPropagation = true; }
button.Tooltip($"Search & commands ({keys.Keys("palette")})");
```

The map answers questions; the app still walks its overlays and views in its own order and
asks where it used to compare `e.Key`. `Find(e, group)` returns the first match in table order.
`Legend(group)` gives a panel's key-and-word row.

**Rebinding.** Mark a shortcut `.CanRebind()`. `Rebind(id, chord)` takes the chord from whatever
rebindable shortcut in the group had it (that one goes back to its default, or to none when its
default is the chord) and **refuses** a chord held by a shortcut that is not rebindable — put the
app's fixed keys in the map too, so a command can never land on Ctrl+C. `SaveOverrides()` /
`LoadOverrides()` persist only the changes, as id → chord text; `Clone()` gives an options page a
draft. `KeyChord.From(e)` turns a captured press into a chord, null for a modifier alone.

`VideoPlayer.Shortcuts` is the player overlay's map, for an app's help to name.

---

## Traps

### "The first Tab does nothing"

With `FocusedElement` null, the first Tab lands on the *first* focusable in order —
often the region that already looks active, so nothing visibly changes. If an app
derives its active region from focus, give the active region focus programmatically
when nothing holds it (from `OnAfterBuild`, guarded as below), so the next Tab actually
moves.

### Focusing a detached element kills the keyboard

`Ref<T>.Current` is **never cleared on unmount** — it is only ever assigned — so after
a rebuild drops an element the ref still hands you the dead instance. `Focus()` does
not notice: it needs `IsFocusable` and *a* `FocusManager`, and the manager is stamped
onto every element by the style pass and never removed. Focusing a detached element
therefore **succeeds**. From then on the window routes every key to it, the bubble
path is one element long, and nothing reaches the root's handlers: the whole keyboard
goes dead, with nothing in the log.

It bites in views that *replace* the previous tree rather than layering over it (a
full-window diff, a wizard), where an "auto-focus the active pane" fallback re-focuses
the pane that was just unmounted. Two rules:

1. Before focusing anything held across frames, ask **`Window.IsInTree(element)`**. A
   non-null ref is not proof the element is on screen.
2. A close that swaps back to the old tree **cannot focus in place**: the replacement
   elements are built *after* the handler runs. Record the intent and focus in the next
   `OnAfterBuild`.

### `Window.SetRoot` does not unmount, and does not clear focus

Replacing the root drops the old tree without running `OnUnmounted`, disposing
component effects or clearing focus. `FocusedElement` can still point into the
discarded tree afterwards — the detached-focus trap above by another route. Call
`ClearFocus()` (or focus something in the new tree) when you swap roots, and prefer
rebuilding one component over swapping roots for view changes.

### Blur fires during reconciliation

When a patch removes the focused element, `BlurEvent` fires synchronously from inside
the reconcile. A `MarkDirty()` called from that handler is deferred to the end of the
pass rather than nesting, so it is safe — but it lands a moment later than the call.
For anything that must not run mid-reconcile, defer with
`Application.InvokeOnMainThread(...)`.

### A rebuild that clears `IsFocusable` leaves the element focused

Reconciliation copies `IsFocusable` and `TabIndex` from the new build onto the retained
element without re-validating focus. Flip `IsFocusable` off on the focused element and
it stays the focus holder until something else takes it. Move focus explicitly.

### Overlays

Clicking non-focusable space in the main tree clears focus; inside an overlay (a
context menu, a dialog) it does not, so a dialog's field keeps focus while you click
around its own chrome.

An overlay's content has the window's focus manager, as the root does, so `Focus()` works
inside one: call it on a dialog's field after `Dialog.Show` returns, and what is typed goes
there. When an overlay closes holding the focus, the focus goes back to what had it when the
overlay opened, if that is still in the window, and otherwise nowhere: never left on the
overlay's elements, which have gone (the detached-focus trap above). Until 2026-10-04 overlay
content had no focus manager, `Focus()` inside a dialog did nothing, and a dialog's field
took the keyboard only when clicked.

**A modal overlay holds the keyboard** (`ShowOverlay(..., modal: true)`, which is how
`Dialog.Show` presents every dialog). Tab and Shift+Tab cycle through its controls and
never reach the page behind it, and keys go to it even when the focus is somewhere it does
not reach — nothing focused, or a page element focused from code while the dialog is up —
so the page's own shortcuts cannot fire underneath. Any other overlay is a Tab scope once
it holds the focus: Tab from a field in a popover cycles in the popover.

A dialog takes the keyboard when it opens: its first control, or the dialog itself when it
has none — the panel, which a screen reader is told has the focus as a dialog named by its
title (until 2026-10-27 the scrim took it, a pane with no name to a reader); it is not a Tab
stop. Content that wants a particular field focuses it after `Dialog.Show` returns and
wins. **Escape closes it** with the default result (`DialogOptions.DismissOnEscape`, on by
default), after everything inside has had its turn: a dropdown closing its list, or a panel
that answers on Escape and stops it, keeps the key. The panel is announced as a `Dialog` to
screen readers, named by its title for `Confirm` and `Alert`, unless the content set a role
of its own.

---

## Text input follows focus

When focus lands on a `TextInputElement` or `TextAreaElement` the window arms the
platform's text input (the IME, and on SDL the text-input events at all) and hands the
element the clipboard. Any other element gets key downs only: **a custom element that
types will see keys but no characters**. The opt-in, `Element.WantsTextInput`, is
internal to the framework today — `NumericUpDownElement` is the one non-text-box that
sets it — so a typing element written outside `HultUI.Core` should host a
`TextInputElement` rather than draw its own caret.

---

## Testing focus

Headlessly, `Tab` is a key like any other: inject it, then read
`Window.FocusedElement` or the a11y snapshot's focused node. Read **live** elements
through the window's tree, not the ones your `Build()` returned — after a rebuild those
are discarded twins, and asserting on them is the detached trap in test form.
