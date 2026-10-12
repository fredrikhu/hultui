# HultUI Reactivity System and Component Model

## Overview

HultUI uses **fine-grained reactivity** inspired by SolidJS. The system is built on three primitives:

- **Signal\<T\>** -- a mutable reactive value
- **Computed\<T\>** -- a derived reactive value (read-only, lazily recomputed)
- **Effect** -- a side effect that re-runs when its dependencies change

When a signal changes, only the effects that actually read that signal re-run. There is no virtual DOM diff of the entire tree and no broad invalidation pass. Dependencies are tracked automatically at read time.

---

## Signal\<T\>

A `Signal<T>` holds a single mutable value. It is the foundational building block of the reactivity system.

```csharp
using HultUI.Core.Reactivity;

var count = new Signal<int>(0);

count.Value = 5;           // set -- notifies subscribers
int x = count.Value;       // get -- tracks the read in the current reactive scope
int y = count.Peek();      // get -- reads without tracking (no dependency created)
```

**Key behavior:**

- **`.Value` (get)** -- returns the current value and registers a dependency in whichever reactive scope is active (an Effect or Computed). Outside a reactive scope, it simply returns the value.
- **`.Value` (set)** -- updates the stored value. If the new value is equal to the old value (using the default equality comparer), no notification is sent.
- **`.Peek()`** -- returns the current value without creating a dependency. Useful when you need to read a signal inside an Effect without re-triggering that effect when the signal changes.

---

## Computed\<T\>

A `Computed<T>` represents a derived value that is lazily recomputed when any of its dependencies change.

```csharp
using HultUI.Core.Reactivity;

var count = new Signal<int>(3);
var doubled = new Computed<int>(() => count.Value * 2);

Console.WriteLine(doubled.Value); // 6

count.Value = 10;
Console.WriteLine(doubled.Value); // 20
```

**Key behavior:**

- **`.Value`** -- reads the cached result. If any upstream signal has changed since the last computation, the factory function re-runs first. The read is tracked, so a Computed can be a dependency of an Effect or another Computed.
- **`.Peek()`** -- reads the cached value without tracking and without forcing recomputation.
- The factory function passed to the constructor runs inside a tracking scope. Any signals or other computeds read during that function become dependencies automatically.
- Recomputation is **lazy**: it only happens when `.Value` is read after a dependency has changed, not immediately when the dependency changes.

---

## Effect

An `Effect` runs a side-effect function immediately and then re-runs it whenever any of its tracked dependencies change.

```csharp
using HultUI.Core.Reactivity;

var count = new Signal<int>(0);

var effect = new Effect(() =>
{
    Console.WriteLine($"Count is {count.Value}");
});
// Prints "Count is 0" immediately

count.Value = 1;
// Prints "Count is 1"

count.Value = 2;
// Prints "Count is 2"

effect.Dispose(); // stop watching -- the effect will not run again
```

**Key behavior:**

- The callback runs **immediately** on construction. All signal/computed reads inside the callback are tracked.
- When any tracked dependency changes, the callback is scheduled to re-run.
- Call `.Dispose()` to tear down the effect and stop all future executions.
- Dependencies are re-captured on every run. If a conditional branch causes a signal to not be read in a particular run, that signal is no longer a dependency until it is read again.
- Only reads made **on the thread running the callback, while it runs**, are tracked. A read on another thread (inside a `Task.Run` the callback starts) or after an `await` in an `async` lambda subscribes nothing — and should not happen: reactive state belongs to the UI thread (see [The thread guard](#the-thread-guard)). The same holds for `Computed` factories and a component's `Build()`.

---

## ReactiveRuntime

`ReactiveRuntime` provides utilities for controlling when effects execute.

### Batching

When you update multiple signals at once, each update would normally trigger its own round of effect re-runs. `Batch` defers all effect execution until the batch completes, so effects only run once with the final state.

```csharp
using HultUI.Core.Reactivity;

var firstName = new Signal<string>("Alice");
var lastName = new Signal<string>("Smith");

var effect = new Effect(() =>
{
    Console.WriteLine($"Name: {firstName.Value} {lastName.Value}");
});
// Prints "Name: Alice Smith"

ReactiveRuntime.Batch(() =>
{
    firstName.Value = "Bob";
    lastName.Value = "Jones";
});
// Prints "Name: Bob Jones" -- only once, not twice
```

Without the batch, the effect would fire after `firstName` is set (printing "Bob Smith") and again after `lastName` is set (printing "Bob Jones"). With the batch, only the final result triggers the effect.

### Automatic batching of event handlers

You rarely need to call `Batch` by hand: **the framework wraps every event dispatch in a batch automatically.** A handler that updates several signals — even across different components — coalesces into a single rebuild that flushes when the event finishes.

```csharp
box.On<ClickEvent>(_ =>
{
    _selectedId.Value = id;      // no manual Batch needed —
    _showDetail.Value = true;    // these three writes coalesce into
    _detail.Item = item;         // one rebuild at the end of dispatch
});
```

This is safe because event handlers already read cached layout from the previous frame (layout runs once per frame, after the whole event burst), and the batch always flushes before the next frame. The one thing it changes: a handler no longer sees its *own* rebuild partway through — if you write a signal and then need the resulting element to exist (to focus or measure it), the rebuild hasn't happened yet.

For that, prefer the existing pattern of deferring to `OnAfterBuild` (see the Component Model section). When you genuinely need the rebuild mid-handler, escape hatches are available:

| API | Effect |
|---|---|
| `ReactiveRuntime.FlushNow()` | Runs pending effects immediately, even inside the batch. Write your signals, call it, then read the rebuilt element. |
| `using (ReactiveRuntime.SuspendBatching())` | Within the scope, writes flush immediately (pre-auto-batch behaviour). |
| `ReactiveRuntime.AutoBatch = false` | Global off switch — restores immediate per-write flushing everywhere. A last resort. |

```csharp
input.On<KeyDownEvent>(_ =>
{
    _showRename.Value = true;      // triggers a rebuild that creates the field
    ReactiveRuntime.FlushNow();    // apply it now
    _renameRef.Current?.Focus();   // the field exists — focus it
});
```

### `MarkDirty()` is synchronous; signal writes are batched

The two ways of asking for a rebuild behave differently inside a handler, and it pays
to know which one you are holding:

| Call | Inside an event handler | Elsewhere on the UI thread |
|---|---|---|
| a `Signal<T>` write (or a `[Reactive]` setter) | queued; the rebuild runs once when the dispatch ends | runs the dependent effects immediately |
| `Component.MarkDirty()` | rebuilds **and reconciles on the calling stack**, right now | the same |
| `Application.InvokeOnMainThread(...)` | always queued for the next frame, even from the UI thread | the same |

The synchronous one has consequences. A `MarkDirty()` inside a handler replaces
elements *while the event is still bubbling*; the dispatcher pins the handler list of
every element on the path before it starts, so the event finishes with the handlers
that were live when it was raised, and a handler attached mid-dispatch does not run for
the event that attached it. When a click "fires twice" or undoes itself, suspect a
rebuild inside the dispatch, not the input pipeline.

A `MarkDirty()` requested *while reconciliation is already running* — a `BlurEvent`
handler, say, since blur fires synchronously when the focused element is removed by a
patch — is deferred to the end of the outer pass rather than nesting. Nothing to do on
your side; just know that the rebuild lands a moment later than the call.

Drag handlers are the case to defer by hand: a `DragEnter` or `DragOver` that marks
dirty replaces the element the hover landed on. The framework re-resolves the drop
target by hit-testing again, so the drop still arrives, but
`Application.InvokeOnMainThread(MarkDirty)` keeps the gesture off the rebuild path
altogether.

### A tween rebuilds once per frame

A tween's setter runs when the loop steps animations, which `Application.Run` does once
per frame it may draw, by all the time since the last step (see
[When a tween steps](ANIMATIONS.md#when-a-tween-steps)). A tween that writes a signal a
`Build()` reads, or calls `MarkDirty()`, rebuilds that component once per frame drawn —
not once per event that wakes the loop, as it did until plan 17 (15 rebuilds a frame with
an event every millisecond; a video player's bar fading over a playing film, 2.2). Its
setter and `OnComplete` run at most once per display interval, and a delay ends up to one
interval late.

A rebuild per frame is still a rebuild per frame: a spinner that marks the whole app dirty
on every step rebuilds the whole app sixty times a second while it runs. Junction's busy
buttons and its toolbar's fetch, pull and push did, until plan 17: 191–215 KB a frame, about
12 MB a second (History, headless, JIT). Animate the element instead (`Animate.Opacity`, a
`Transform`, `SpinnerElement`), or keep the animated part in a small component of its own.
Junction's spinners now keep one `Ref<Element>` per spinner a build makes, and the step turns
each one on screen; a frame of a fetch costs nothing.

When a rebuild in the middle of the animation must not undo it, keep the animated value in a
plain field as well: the tween's setter writes the field and the live element (through a
`Ref<T>`), and `Build()` reads the field, which is not a signal and so rebuilds nothing. The
`VideoPlayer`'s bar fades this way. Its level was a signal its `Build()` read, so every frame
of a fade rebuilt the player (Reel: 166 rebuilds in 12 s, three quarters of what its UI
thread allocated); a fade frame now costs about 0.9 KB instead of 41 KB (headless, JIT), and
the player rebuilds only where the bar appears and where it goes.

`ToastHost` fades the same way. The `onChanged` you hand it (the apps hand it their window's
`MarkDirty`) runs when the toast appears, changes or goes, and not on the frames of its fades,
which write the toast on screen. It ran on every step until plan 17: one toast rebuilt Junction
23 times, 4.5 MB of garbage (Meridian 1.5 MB, Aperture 3.1 MB); now twice, 0.39 MB (0.13,
0.27; headless, JIT, the real loop).

### A drag rebuilds once per frame

A pointer moves far more often than a window draws: a 1,000 Hz mouse sends about sixteen
moves for each frame of a 60 Hz display. The controls whose drag rebuilds their owner follow
the pointer once per frame that may be drawn, at its latest position: `PaneSplitter`'s
`onChanged`, `HorizontalPan`'s, and a `Slider`'s `OnValueChanged` while it is dragged and its
`OnHoverValue` while it is pointed at (the video scrubber's preview). The moves between two
frames only record where the pointer is; the window applies the last of them at the start of
its next frame, before style and layout, so that frame shows it. The release applies the last
position at once, so a drag ends exactly where the pointer did, frame or no frame. A splitter
between two lists of 60 rows, dragged with such a mouse, rebuilt its owner 16 times a frame
(10.5 MB a frame, headless) and now does once (0.66 MB).

Every other pointer-move handler still hears every move, an app's own on the same element
included. A handler of yours that calls `MarkDirty()` from a `PointerMoveEvent` rebuilds on
every move: build a drag on these controls where you can, or keep it out of `Build()` (write
the live element through a `Ref<T>`).

### A value that changes ten times a second

Some signals move on their own, many times a second: `MediaPlayer.Position` while a film
plays (mpv reports it ten times a second), a download's progress, a clock. A `Build()` that
reads one rebuilds that often, however little of what it makes depends on it. Until plan 17
the `VideoPlayer` read the position for its elapsed time and its scrubber, so every report
rebuilt the whole player: about 22 elements and 41 KB, ten times a second, with the bar
shown or hidden.

Write the elements that show the value instead, and read it untracked in `Build()`:

```csharp
private readonly Ref<TextElement> _elapsed = new();
private Effect? _positionWatch;

protected override void OnMount()
{
    base.OnMount();
    // Runs now and whenever Position changes: rewrites the one label, rebuilds nothing.
    _positionWatch = new Effect(() =>
    {
        if (_elapsed.Current is { } label) label.Content = Format(_player.Position);
    });
}

protected override void OnUnmount()
{
    _positionWatch?.Dispose();
    base.OnUnmount();
}

public override Element Build()
{
    // Untracked: the build shows the value it was built with; the effect keeps it current.
    var position = ReactiveScope.Untracked(() => _player.Position);
    return Text.New(Format(position)).WithRef(_elapsed);
}
```

The `Ref` follows the element on screen across rebuilds, so the effect always writes the live
one, and the build's own value is never stale because it reads the current one. The player's
position report now costs 420 B instead of 41 KB (headless, JIT).

A timer that only refreshes a readout is the same case. Meridian's elapsed time while a request
is in flight ticked every 150 ms by rebuilding the whole window; its tick now writes the label
through a `Ref` (1.5 MB → 1.4 KB a second of waiting, headless, JIT). Stop such a tick with the
thing it was started for (here the send's own cancellation source, not "any send in flight"):
Meridian's kept going when the next send started within one tick of the last one ending, so
sends in a row stacked ticks.

When the build needs only something coarse derived from the value, read that through a
`Computed<T>` instead. An effect that read a computed value runs again only when the value it
computes changes, so a build reading it reruns a few times a film, not ten times a second.
Reel's and Aperture's chapter panels mark the chapter playing now; they read the position in
the app's `Build()`, so with the panel open every report rebuilt the whole app (Reel 66.6 KB,
Aperture 179.5 KB a report, headless). They now read the chapter's index:

```csharp
// Made once per player; a build that reads it reruns when the playhead crosses a mark.
_chapterHere = new Computed<int>(() => CurrentChapterIndex(player.Position));

// in Build():
int current = _chapterHere.Value;
```

A report then costs 0.6 KB (Reel) and 1.8 KB (Aperture).

### `Build()` is not a clock

`Build()` runs when something it read changes, and at no other time. Work that has to happen
every so often, or when a signal changes, does not belong in it. Aperture's progress report
to the server (every ten seconds) and Reel's resume save (every fifteen) ran from the app's
`Build()`, whose comment said it ran on every frame of a playing film. It did not: the
transport bar is a component of its own and rebuilds alone, so a film playing with nobody
touching anything built the app once at most (Aperture, when the bar hid), and from each app's
first version until 2026-10-11 neither the report nor the save ran during quiet playback.

Put such work in an `Effect` made with the thing it watches and disposed with it:

```csharp
// Made with each player; disposed when the player goes.
_saveClock = new Effect(() =>
{
    _ = player.Position;                      // every position report runs this…
    if (!player.IsPlaying) return;
    if (_app.Time.GetElapsedTime(_lastSave) < SaveInterval) return;   // …and checks the clock
    _lastSave = _app.Time.GetTimestamp();
    SaveResumePoint();
});
```

- **Something that follows the clock** rides a signal that ticks (a playing film's `Position`,
  ten times a second) and checks `Application.Time`, or is a delayed tween, the framework's
  timer (see [ANIMATIONS.md](ANIMATIONS.md)). Read the time from `Application.Time`, which a
  headless test moves by hand, not `Environment.TickCount64`.
- **Something that follows state** (a seek once the length is known, the next file when one
  ends) is an effect that reads that state. Read the signals it waits for before any early
  return: an effect depends on what its last run read, and one that returned first hears
  nothing again.
- **Read the rest untracked** (`ReactiveScope.Untracked`), or the effect runs again for every
  value it happens to touch; Aperture's state watch reads the position for its reports that
  way, so it runs a few times a film, not ten times a second.

Neither effect rebuilds anything. A minute of quiet playback once the bar has gone (headless,
the real loop, a position report every 100 ms) still builds each app 0 times; the UI thread
allocates 22,130 → 22,158 B/s in Aperture (six reports) and 22,115 → 22,472 B/s in Reel (four
settings writes), so a check that is not due costs nothing measurable.

---

## Component Model

HultUI components are class-based. You extend `Component` and override the `Build()` method to return an element tree.

### Basics

```csharp
using HultUI.Core.Components;
using HultUI.Core.Elements;

class Greeting : Component
{
    private readonly Signal<string> _name = new("World");

    public override Element Build()
    {
        return Text.New($"Hello, {_name.Value}!");
    }
}

// Mount it into a layout:
var root = Flex.Column().Children(
    new Greeting().Mount()
);
```

### How it works

1. **`Build()` runs inside an Effect.** Every signal or computed read during `Build()` is tracked as a dependency.
2. **When a dependency changes, `Build()` re-runs automatically.** The component produces a new element tree.
3. **The new tree is diffed against the old tree** using `TreeDiffer`. Existing elements are patched in-place rather than replaced wholesale. This preserves element identity, focus state, scroll position, and other transient state.
4. **`.Mount()`** returns a `ComponentHost` element that can be placed in any element tree. The `ComponentHost` manages the component's lifecycle and holds its rendered output.

### Lifecycle hooks

Components expose lifecycle hooks that you can override:

| Method | When it runs |
|---|---|
| `OnMount()` | After the component is first mounted into the tree |
| `OnUnmount()` | When the component, or any element it sits under, is removed from the tree |
| `OnBeforeBuild()` | Immediately before each `Build()` call |
| `OnAfterBuild()` | Immediately after each `Build()` call |

**`OnAfterBuild` runs before the style and layout passes.** The new tree has been
reconciled but not laid out, so any geometry read there — a viewport height, a column
width — is the *previous* frame's, and a fresh element has none at all. Anything that
needs the laid-out result goes through `Window.RequestPostFrameCallback(...)`, which
runs once after the next frame is presented. A scroll reveal wired straight into
`OnAfterBuild` used to do nothing for exactly this reason; `ScrollViewElement` now holds
such a request until it has a viewport, but a hand-rolled version will not.

**What triggers a rebuild.** `Build()` re-runs when a `Signal<T>`, `Computed<T>` or
`[Reactive]` property *that it read* changes, or when the component calls
`MarkDirty()`. Nothing else. A plain `{ get; set; }` property on a component is invisible
to the runtime: set it, and the screen does not change until something else rebuilds.
If `Build()` reads it, make it a signal.

---

## [Reactive] Source Generator

The `[Reactive]` attribute eliminates boilerplate when declaring reactive properties on components.

### Without the generator

```csharp
class Counter : Component
{
    private readonly Signal<int> _count = new(0);

    public int Count
    {
        get => _count.Value;
        set => _count.Value = value;
    }

    public override Element Build() { /* ... */ }
}
```

### With the generator

```csharp
using HultUI.Core.Reactivity;

partial class Counter : Component
{
    [Reactive] public partial int Count { get; set; }

    public override Element Build()
    {
        var count = Count; // reads Signal<int> -- tracked by the Build effect

        return Flex.Row(gap: 12).AlignItems(AlignItems.Center).Children(
            Box.New().Padding(12).Background(Color.FromHex("#4F46E5")).CornerRadius(8f)
                .TextColor(Color.White).Cursor(CursorType.Pointer)
                .OnClick(_ => Count++)
                .Children(Text.New("+")),
            Text.New($"Count: {count}")
        );
    }
}
```

The source generator creates a `Signal<T>` backing field and wires the property getter/setter through it. Reading the property inside `Build()` creates a reactive dependency just as if you read `.Value` on a signal directly.

### Requirements

- The class must be `partial`.
- The property must be `partial` (C# 13+).
- The project must reference the SourceGenerators project as an analyzer.

---

## Tree Diffing

When a component rebuilds, `TreeDiffer` reconciles the new element tree against the previous one.

### Reconciliation rules

1. **Same type at the same position** -- the existing element is patched in-place. Its properties are updated to match the new tree, but the element instance is preserved. This keeps focus, animations, and other transient state intact.
2. **Different type at the same position** -- the old element is removed and the new element is inserted.
3. **Keyed children** -- use `.Key` on elements to provide stable identity. Keyed elements are matched by key across reorders, insertions, and deletions.
4. **Unkeyed children** -- matched by index. This is simpler but can cause unnecessary patches or state loss if the list order changes.

### When to use keys

Use `Key` when rendering a dynamic list where items can be added, removed, or reordered:

```csharp
public override Element Build()
{
    return Flex.Column().Children(
        _items.Value.Select(item =>
            Text.New(item.Name) { Key = item.Id }
        )
    );
}
```

Without keys, inserting an item at the beginning of the list would cause every element to be patched with the next item's data instead of shifting elements down.

### Event handlers follow the latest build

When patching, app event handlers (registered via `On<TEvent>`) **are copied from the new tree** — the retained element always runs the latest `Build()`'s handlers, exactly like properties and callback props. A handler that closes over per-item data therefore stays in sync with what the element displays: if the differ patches element #0 from "Apple" → "Banana", the handler is the new build's, and it captured "Banana". Conditionally attached handlers work the same way — a button built disabled (no handler) then rebuilt enabled gets the enabled build's handler; no key tricks required. (Element-internal wiring, like a `TextInputElement`'s own key handling, is *owned* by the element and survives patching untouched.)

**The one rule this imposes:** a handler must never capture a *build-scope element variable*. The elements a build creates are throwaway blueprints — after reconciliation, the live tree is the retained instances, and the captured variable points at the discarded incoming build. Reach the live element through the event instead:

```csharp
// BUG: 'box' is this build's instance; after a patch the handler (copied to
// the retained element) still writes to the discarded blueprint.
var box = Box.New();
box.On<PointerEnterEvent>(_ => box.BackgroundColor = hoverColor);

// Correct for hover/press/focus styling: declare it. The override survives
// patching, is re-applied after every rebuild (a rebuild landing mid-hover
// can't wipe the tint), and un-hover restores the element's base look.
var box = Box.New().OnHover(b => b.BackgroundColor = hoverColor);

// Correct for other self-references (reading layout to anchor a popover,
// imperative writes): e.CurrentTarget is the element the handler runs on —
// always the live one.
btn.On<ClickEvent>(e => OpenPopoverUnder(e.CurrentTarget.Layout.AbsoluteBorderBox));
```

Interaction config (`.OnHover`/`.OnPressed`/`.OnFocus` and transitions) follows the latest build just like handlers: the incoming element's config replaces the retained one, and an incoming element *without* any interaction clears it — so conditionally attaching a hover style (`if (!active) el.OnHover(...)`) behaves correctly across state flips. For *cross*-element references (a button that focuses an input), use `Ref<T>` via `.WithRef(...)` — refs are repointed to the live element during patching.

Keys still matter for *state*: an unkeyed patch preserves the retained element's transient state (focus, caret, scroll position), so when a slot switches between logically different content — a settings page behind a nav list, a wizard step — key the slot by what it shows so unrelated state doesn't bleed across.

### Keys and animations

Animations started during `Build()` run on the element instance returned by that call. If the differ patches an existing element instead of inserting a new one, the animation targets a discarded element and has no visible effect. To force the differ to create a fresh element (and thus run the animation), include a changing value in the key:

```csharp
[Reactive] public partial int TriggerCount { get; set; }

public override Element Build()
{
    var gen = TriggerCount;
    var box = Box.New().Width(0).Height(60);
    box.Key = $"expandBox-{gen}"; // new key on each trigger → new element → animation runs
    Animate.Width(box, Dimension.Px(0), Dimension.Px(300)).Duration(0.5f).Start();
    return box;
}
```

Without the key suffix, pressing a button that increments `TriggerCount` would rebuild the tree but the differ would patch the existing box — the new animation targets a discarded element and the box stays at its current width.

---

## Direct Mutation

For performance-critical paths, you can skip the component rebuild cycle entirely and mutate elements directly.

```csharp
var label = Text.New("Hello");

// Later, update it directly:
label.Content = "Updated";  // marks paint dirty, no rebuild needed
```

Direct mutation and reactive components can coexist in the same application. Use components for sections of UI with complex state dependencies, and direct mutation for hot paths where you want precise control over what gets updated.

### Layout styles do not self-invalidate

Visual properties like `BackgroundColor` and `CornerRadius` mark themselves paint-dirty in
their setters. The style objects — `Flex`, `Box`, `Position`, `Grid`, `GridItem` and
`Inherited` — do not. They are plain classes with auto-property setters, so this is
silently dropped:

```csharp
pane.Flex.FlexBasis = Dimension.Px(newWidth);   // nothing happens
```

During construction it works anyway, because a new element starts fully dirty and the
first layout pass has yet to run. After that first pass, nothing marks the element dirty,
the next frame sees no reason to re-run layout, and the change never reaches the screen.
Nothing throws and nothing logs — the UI just doesn't move. Call `MarkLayoutDirty()` after
the mutation (`MarkPaintDirty()` for `Position.ZIndex`, which only changes draw order):

```csharp
pane.Flex.FlexBasis = Dimension.Px(newWidth);
pane.MarkLayoutDirty();
```

`Inherited` (the text properties — font, colour, alignment, spacing) needs
`MarkStyleDirty()`, which re-resolves the subtree. The style pass then lays out again every
element it finds measuring differently — font family, size, weight, slant, line height,
letter spacing or direction changed — so that one call is enough:

```csharp
label.Inherited.FontSize = 20f;
label.MarkStyleDirty();     // re-resolves, and re-measures because the size changed
```

`TextColor`, `Decoration`, `TextAlign` and `Cursor` move nothing, and the style pass leaves
layout alone for them. (Until 2026-10-06 the style pass never re-measured, and a metric
change needed `MarkLayoutDirty()` as well; that call still does no harm. A theme whose
font sizes differ from the last one's was laid out at the old sizes.)

Auto-invalidating setters were considered and rejected: every style object would need a
back-pointer to its element and every write would walk to the root, so static UI
construction would pay for a problem it doesn't have.

**`HULTUI002` catches the miss.** The analyzer reports a style assignment inside a
lambda on an element that predates it — a captured local, a field, an event argument's
element — when nothing in the enclosing method invalidates. It stays quiet on
construction code, on elements the callback itself introduced, and when any of
`MarkLayoutDirty` / `MarkPaintDirty` / `MarkDirty` or a structural mutator appears in
scope, including as a method group. Post-layout mutation from a named method is outside
what it can tell apart from construction, so that case is still on you.

---

## Keeping `Build()` Cheap: Off-Thread Data with `AsyncLoader`

`Build()` runs inside an Effect and re-runs on **every** change to a signal it reads — a selection, a hover, a resize, a parent rebuild. It must therefore be **pure and fast**: assemble an element tree from already-available state and return.

**Never do blocking work in `Build()`** — file or network I/O, spawning a subprocess, or heavy computation. `Build()` runs on the UI thread, so anything slow there freezes the whole app: no frames render until it returns.

### The symptom

If the UI hitches when you open or switch to a view, the cause is almost always a synchronous call in some component's `Build()`. It does **not** show up as a slow *frame* in a trace — frames stay fast — it shows up as a **gap between frames** where the UI thread is blocked and nothing renders. (A frame profiler that only times the render phase will look perfectly healthy while the app freezes.)

### The fix: `AsyncLoader<T>`

`AsyncLoader<T>` (namespace `HultUI`) runs the slow work on a thread-pool thread and exposes the result reactively, so `Build()` renders from a cached snapshot and updates when the data lands.

```csharp
partial class UserPanel : Component
{
    private readonly AsyncLoader<UserProfile?> _profile = new(null);

    [Reactive] public partial int UserId { get; set; }

    public override Element Build()
    {
        // Off-thread load, keyed by UserId. Same id = cache hit; a new id
        // supersedes an in-flight older load. Build stays cheap.
        _profile.Load(UserId, () => _api.FetchProfile(UserId));

        if (_profile.IsLoading && _profile.Value is null)
            return Spinner();

        return ProfileCard(_profile.Value);
    }
}
```

- `Load(key, produce)` runs `produce` on a background thread when `key` changes, then applies the result on the UI thread (via `Application.InvokeOnMainThread`), triggering one rebuild.
- `Value` / `IsLoading` are reactive reads — reading them in `Build()` makes it re-run when the load completes.
- `produce` runs off the UI thread and **must not touch elements or reactive state**. Do that in the optional `onLoaded` callback, which runs on the UI thread once the result is applied (inside a batch, before the rebuild) — use it to reconcile dependent state, e.g. dropping a selection that no longer exists in the freshly loaded data.

### The thread guard

That last rule used to be enforced by nothing. A background thread writing a signal or
mutating an element does not fail at the write: it corrupts the reactive graph or a
dirty-flag walk, and the symptom surfaces frames later somewhere unrelated. HultUI takes
no locks in its hot paths — that is the price of the single-UI-thread model — so there is
no safe version of the mistake.

`UIThread` turns it into an immediate exception instead:

```
Signal<Int32>.Value was called from a thread-pool thread (managed thread 7); HultUI owns
the element tree and the reactive graph on the UI thread (managed thread 1) and takes no
locks, so this would corrupt state silently rather than fail here. Marshal the mutation
back with Application.InvokeOnMainThread(...), or drop the ConfigureAwait(false) so the
continuation resumes on the UI thread. Set UIThread.Enforce = false to disable this check.
```

- The UI thread is claimed by `Application`'s constructor and released when it is
  disposed. Code that never starts an application — pure layout and reactivity unit
  tests — has no UI thread and is never checked.
- Guarded entry points: the `Signal<T>` setter, `ReactiveRuntime.ScheduleEffect`,
  `Component.MarkDirty()`, and the four `Element.Mark*Dirty` methods every element
  mutation funnels through.
- `UIThread.Enforce` defaults to true in Debug builds of `HultUI.Core` and false in
  Release. Setting it true in a Release diagnostic build is supported.
- **Cost, measured:** enforcing adds ~0.4ns per guarded call (a thread-local read and a
  comparison) — 29% on a bare `Signal<int>` write, 6% on one with a subscriber, and 166%
  on an already-dirty `MarkPaintDirty`, which is the ratio you get when the operation
  itself is two field reads. In app terms it disappears: the whole
  js-framework-benchmark suite makes 2.5M guarded calls, so the guard accounts for about
  1ms of ~770ms, or 0.13% — well under the benchmark's own run-to-run variance. With
  `Enforce` off the check is one static bool read, which does not measure at all.
- The fix is almost always `Application.InvokeOnMainThread(...)`, or removing a
  `ConfigureAwait(false)` that sent the continuation off the UI thread. `AsyncLoader`
  already does the marshalling for you; prefer it over hand-rolled `Task.Run`.

### `Refresh()` (reload) vs `MarkDirty()` (re-render)

The loader **key** decides whether the expensive work re-runs. Choose it so that **UI-only rebuilds are cache hits**:

- For a view showing a specific entity, key on the entity's id — re-selecting it is a cache hit.
- For a view with no natural key, use a monotonic **refresh generation** counter. Bump it only when the underlying data actually changed; leave plain re-renders alone.

```csharp
private readonly AsyncLoader<IReadOnlyList<Row>> _rows = new([]);
private int _gen;

public override Element Build()
{
    _rows.Load(_gen, LoadRows);          // cache hit unless _gen changed
    return Table(_rows.Value, _selectedRow);
}

// Underlying data changed (saved, deleted, external edit) → reload.
public void Refresh() { _gen++; MarkDirty(); }

// UI-only change (selection, expand/collapse) → re-render from cache, no reload.
private void OnRowClicked(Row r) { _selectedRow = r; MarkDirty(); }
```

The discipline in one line: **`Refresh()` bumps the generation (data changed → reload); `MarkDirty()` re-renders from cached data (UI-only).** Wiring a selection or expand-toggle to a data-reloading `Refresh()` re-runs the load on every click — a common and easy-to-miss source of latency.

### Stable content across reloads

Because `Build()` renders from the cached `Value`, you can keep the previous result on screen while the next one loads instead of blanking to a spinner — gate the placeholder on "have we ever loaded anything" rather than "is a load in flight." Since the result is applied in a single batch, the view swaps from old to new in one frame with no empty state in between.

---

## Key Imports

| Namespace | What it provides |
|---|---|
| `HultUI.Core.Reactivity` | `Signal<T>`, `Computed<T>`, `Effect`, `ReactiveRuntime`, `[Reactive]` |
| `HultUI.Core.Components` | `Component`, `ComponentHost` |
| `HultUI.Core.Builders` | `Flex`, `Box`, `Text`, and other element builders |
| `HultUI` | `Application`, `Window`, `AsyncLoader<T>` |
