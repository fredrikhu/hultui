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
