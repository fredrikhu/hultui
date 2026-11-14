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
- A write upstream allocates nothing: the computed tells its readers through a pooled snapshot, as a signal does, so one that follows a fast value (a playhead read ten times a second, below) costs no garbage per write. It copied its readers into a new list, 64 bytes a time and twice a write that changed its value, until plan 17's round 4.

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

A `MarkDirty()` from the component's own `OnBeforeBuild`, `Build` or `OnAfterBuild` is not
deferred but dropped, and so is a write there to a signal the build read. See
[Lifecycle hooks](#lifecycle-hooks) for what to call instead.

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

An app that keeps parts (below) hands it a callback that builds them only when a toast comes:
`new ToastHost(() => { if (_toasts!.IsShowing) MarkDirty(); else MarkViewDirty(); })`. A
toast that comes may report a change a part shows, and the code that flashed it may count on that
build; one that goes takes away only the window's own strip, which the view's build places.
Junction, Meridian and Aperture do this (plan 17 round 10, step 5): a toast from flash to gone
over Junction's Changes 365 → 227 KB, part builds 8 → 4; Meridian 271 → 212 KB; Aperture's Home
149 → 90 KB (headless, JIT).

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

### A splitter's drag can move the pane and build nothing

Once a frame is still a build a frame, and the build is the view or the part the pane is in:
Meridian's response pane with a 2,000-line body was built again on every move of its handle,
and Junction's view beside its file list. What a move changes is the pane's size, which is
layout's business. While `IsDragging`, give the pane on screen the size through a `Ref` and call
`MarkLayoutDirty()`; let the press and the release build as before (ELEMENTS.md, "PaneSplitter",
has the code). Plan 17 round 10 step 13, headless, a move and two frames, JIT (NativeAOT within
2 %): Meridian 38.8 → 2.5 KB a move with the body (10.9 → 0.1 with none, 36.4 → 0.1 at the
bottom), Junction's Changes 42.0 → 2.4 and Commit view 65.2 → 5.5; part or view builds a move
1 → 0, elements made 32–51 → 0.

What to look at before doing it:

- **Everything a build reads from the size.** Junction's diff read its pan's viewport in the
  build, and a build reads the layout of the frame before it (next section). Meridian's room for
  the pane (`PaneSplitter.SetMax`) is worked out the build's way on each move.
- **The press.** It builds: its handler holds the handle element it was built with, which a patch
  left behind, so the lit handle comes from a build. It moves nothing, which tells it from a move:
  the pane on screen already has the size.
- **What is measured at the size.** Junction's wrapped diff lines, and a review's cards, are
  measured at the code column's width; while they are on, a move builds as it did.
- **An offset held at an edge.** Junction's diff panned to the end of its longest line keeps the
  end at the column's edge, so a move that widens the column clamps the offset and the rows are
  built at it: those moves still build (14 of the 31 that moved the size in the scenes, which
  start panned to the end).
- **Test it against a full build at every move,** in the same process (Meridian shows the
  response's time, which differs between runs): pixels, the reader's tree, elements, focus and the
  sizes, then a forced full build, then the same again. 157 steps of drags (Changes, Commit,
  Meridian beside and under the editor; panned, split, wrapped, arrow keys) and resizes: none
  differ, with a reader or without. With the full build only at the releases and keys, so that
  moves add up, two releases in the Commit view differ in the handle's hover tint, and they do
  the same when every move builds, as before.

### What a build reads from layout is a frame old

A build runs before the layout of its frame. A `Layout` box read in `Build()` is the frame
before's (a new element's is empty), and a window resize, or a size written on the live tree,
lays out again with no build at all. A number a build keeps from such a box goes stale until
something builds: Junction's diff pan took its viewport from the window at the first build
(1,298 px for a 696 px column) and from the old width after each resize, so the sideways wheel
stopped short of the end of the longest line (361 of 963 px at the start) and the scrollbar's
thumb kept its size. Read the width after the frame (`Window.RequestPostFrameCallback`, asked
for after a build and on `Window.Resized`) and give it to whatever shows it in place:
`HorizontalPan.ViewportWidth` assigned then moves the thumb on screen with no build. Build only
when the answer changes what the build makes (the scrollbar coming or going, an offset the
cells were built at). Junction's scenes, 125 steps: 67 differed from a full build, none now.
What is read after the frame shows a frame later, in a frame of its own; where the move is known
before the layout (a column taking the window's slack moves by the window's change), give it in
the `Resized` handler too and keep the read as the check (LAYOUT.md, "Nesting scroll views").

### A background callback that fires once per item posts one build at a time

A cache or a worker that calls back once per item it finishes, on another thread, usually
ends in `Application.InvokeOnMainThread(MarkDirty)`. The UI thread runs everything posted
since the last frame in one go, and `MarkDirty()` rebuilds on the calling stack, so N items
that land between two frames are N builds in that frame, each making the whole view again for
one more picture. Aperture's `PosterCache` and Vesper's `ThumbnailCache` call back once per
picture and leave the coalescing to the caller. Until plan 17 neither caller did: opening
Aperture's Home (36 posters) built it 41 times, up to 10 times in one frame, and Vesper's
Thumbnails (44 pictures) 45 times, up to 44 in one frame.

Post one build at a time: a flag set when the build is posted and cleared when it runs.

```csharp
private int _landingPosted;

// The cache's callback, on the thread pool.
private void PostLanded()
{
    if (Interlocked.Exchange(ref _landingPosted, 1) == 0)
        Application.InvokeOnMainThread(OnLanded);
}

private void OnLanded()
{
    Interlocked.Exchange(ref _landingPosted, 0);   // first: a landing from now on posts the next build
    MarkDirty();
}
```

Every item that landed before the build ran is in it, and nothing waits longer than it did:
the first landing posts exactly as before. Clear the flag before the build reads what landed,
not after it: cleared after, an item that lands while the build runs finds the flag still set,
posts nothing, and its picture waits for something else to build. A throttle by time (at most
one post every 80 ms) is not the same thing either: it drops the post for the last items of a
burst, so the burst needs a final refresh of its own (Vesper's find has one).

With the flag, from opening the view until every picture landed (headless, a picture served
after 15–30 ms; JIT, then NativeAOT):

| View | Builds | Most builds in one frame | Allocated |
|---|---|---|---|
| Aperture Home, 36 posters | 41 → 14–16 | 8–10 → 4 | 3,614 → 1,825–1,896 KB (−48…−50 %); AOT 3,446 → 1,696 KB |
| Aperture library grid, 42 posters | 46 → 14–15 | 9–13 → 2–3 | 2,419–2,473 → 1,442–1,564 KB (−36…−42 %) |
| Vesper Thumbnails, 44 pictures | 45 → 2–4 | 24–44 → 1–2 | 1,409–1,491 → 756–819 KB (−42…−49 %) |

The same pictures are drawn at the end, and the settled frame has the same pixels. With
pictures served more slowly (60–120 ms) fewer land together: Home 41 → 26 builds (−29 %), the
grid 46 → 25 (−26 %).

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

Something that follows the pointer is the same case. The `VideoPlayer`'s scrubber preview read
the hovered position in `Build()`, so the player was built again on every frame the pointer
moved along the track: about 42 KB a frame, with or without a card to show. The hover now
writes the card on screen through `Ref`s (its `Position.Left` and the layout mark that goes
with it, its picture's `Source`, its time label's `Content`), and only a card that has to come
or go goes through a build, through a `Signal<int>` bumped for that alone: 1.9 KB a frame
(headless, JIT and NativeAOT), most of it the pointer events themselves. When a build makes no
such element, clear the refs that pointed at the last one: a `Ref` keeps the element it was
given after the element leaves the tree, and a write would then land on an element nobody
sees.

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

A report then costs 0.6 KB (Reel) and 1.8 KB (Aperture), and 64 bytes less since the computed's
own notify stopped allocating (plan 17's round 4: Reel's report 527 → 463 B, headless, JIT).

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

### A resize builds only for what it changes

A window dragged by hand passes through a size a frame, and layout follows it by itself: the same
elements, new boxes. A `MarkDirty()` on `Window.Resized` makes all of them again, on every step, for
the few things a build reads off the size. Have each build note what it read, and have the resize
work the same out at the new size and build only when an answer changed. Vesper's grids do this
(round 4, step 13), and Aperture (round 6, step 13):

- **Put the reads behind one method that notes them.** Aperture has one question, how many cards
  of this width fit across, asked from about a dozen places in its views, with six card widths. The
  answer's method notes each width it was asked about, so a view added later is covered. A list of
  the widths you know of goes stale.
- **Compare with what the last build read, not with the last step.** Nothing was built in between.
- **Round before you compare.** A hero's picture is asked for in steps of 128 pixels; its width
  matters only when a step is crossed. Its box is set from the height, so the height matters at
  every step inside the range it follows.
- **Look for what a build did besides.** Aperture's library keeps the cursor on screen in its
  build, so a window shrinking under the cursor scrolled because each step built. The check now
  makes the same call itself. A state that arrives as a resize belongs in the check too: full
  screen taken from outside the app shows only as one.
- **Test it against a full build.** Walk each view through the sizes and compare what is on screen
  with what a forced rebuild makes, with builds far enough apart for a missed one to show.

Aperture, headless, 1440×900, a resize and two frames, NativeAOT within 1 %: a 4 px step that keeps
the columns in the library 71.5 → 2.3 KB (−97 %), one build a step → none; the same in height; Home
130.0 → 2.3 KB (−98 %); an item's page, whose hero asks again at a step, 77.3 → 14.1 KB (−82 %). A
step across a column count builds as it did. Headless, the frames are most of a step, so its time
moved by −1…−3 %.

### Keep what a key does not change in a child component

A rebuild makes everything its `Build()` makes. Junction's window was one component, so a key
in History made the title bar, the sidebar and the toolbar again with the list: 128 KB of the
182 KB a build made, for about 120 elements that show no selection. Put such parts in child
components the parent makes once and hands to every build: `Part` (`HultUI.Core.Components`) is
that component. A rebuild of the parent moves the kept host into its new tree and the reconciler
puts it back; nothing inside it is built, and nothing around it is marked.

```csharp
private readonly PartGroup _parts = new();
private Part? _toolbar;
private Part ToolbarPart => _toolbar ??= new Part(_parts, BuildToolbar);   // made once

public override Element Build()
{
    var view = BuildView();
    view.Key = "view";
    // The host is the flex item where the toolbar's root used to be.
    return Flex.Column(gap: 0).FlexGrow(1).Children(ToolbarPart.Place().FlexShrink(0), view);
}

// A rebuild builds the parts too, unless the path says it changes nothing they show.
private new void MarkDirty() { _parts.Invalidate(); base.MarkDirty(); }
private void MarkViewDirty() => base.MarkDirty();
```

Three things decide whether it stays right and stays cheap:

- **What the part shows has to reach it.** A part rebuilds when a signal or loader its own
  build read changes, not when its parent does. A plain field it reads (the active repository,
  a setting, a width breakpoint) needs the parent to say so: its `MarkDirty()` and `Rerender()`
  call `PartGroup.Invalidate()`, so every rebuild builds the parts too, and only the paths that
  change nothing the parts show — selection moves, cursors, typing in a field of the view, search
  boxes — call `MarkViewDirty()` or `RerenderView()`, which do not. Forgetting to opt out costs a
  build; forgetting to opt in shows a stale toolbar, so make the safe one the default.
  A dialog splits in two (Junction, round 10 step 8): opening it and closing it without acting
  (Escape, Cancel, a click beside it) change only the view, but a confirm runs whatever the card
  was for, which may be the chrome's (a branch checked out, an account signed out), so the confirm
  keeps the full build whatever its action marks. The view-only clicks there — opening and
  cancelling a dialog, closing a popover with Escape, the search keys, lines picked for staging,
  folders opened and closed — each saved the three chrome parts, 123–137 KB a click.
- **The host takes the root's place in the layout.** `Place()` returns the `ComponentHost`, an
  element between the parent and the part's root. Give it what the root had as a flex item
  (`FlexShrink(0)` for a fixed bar, so a crowded row cannot squeeze it), and give a root that
  must fill its host `FlexGrow(1)` (the host is a column).
- **Keep the parent's children keyed.** A keyed list puts a kept host back without marking the
  parent (round 4, 87698f1): no layout, and no structural change, which would make a window
  with a listening screen reader build a whole new accessibility tree for the key. So does a
  parent whose children are all handed back, a host alone in its parent among them (round 5):
  the build moved every child out, and they come back as they were. A host in an unkeyed list
  beside children the build made goes back the ordinary way and marks both.

Junction with its chrome in three parts (b35fc58; headless, JIT, a 1,562-commit clone, a key
and 8 frames): History 242.5 → 107.3 KB a key (−56 %), Branches 186.0 → 50.8 (−73 %), Tags
230.3 → 95.2 (−59 %), Files 224.4 → 79.0 (−65 %), the Commit file list 230.3 → 95.1 (−59 %), a
keystroke in the commit summary 201.9 → 66.7 (−67 %); the NativeAOT app −39…−68 % a key; the
same accessibility trees a key with a reader. The perf suite pins the pattern:
`component/list walk beside chrome built with it` (173.5 KB a step) against `… in kept parts`
(35.2 KB), and `a11y/selection walk beside details and kept chrome parts, …` (0.6 trees a key,
as without the chrome; 1.0 before the reconciler put kept hosts back unmarked).

**A form can be a part with a signal of its own.** A field holds its own text, so a character in
Junction's commit summary changes nothing the window shows but what reads that text. The view
still built on every key, its lists with it, and the key cost more the more files had changed. The
composer is now a part (round 6, step 10): a character writes the part's signal, and only the part
builds, for its issue list and closing hint. The view's build places it, as Vesper's does (`Place`
below), comparing the few things the part shows of the view's data, so a load that lands with no
full rebuild behind it still reaches the commit button. Headless, 1400×900, a key and two frames:
76.8 → 13.6 KB a character with 1 changed file, 1,320 → 13.6 KB with 200 (−99 %, and −23 % time;
NativeAOT 1,300 → 13.2 KB, −27 %), and no window build. Check what the view reads from a field
first: here nothing outside the composer did.

Meridian keeps its title bar, rail, request tree and response pane this way (round 5, step 11),
and adds four lessons:

- **A view-only path can still flip something a part shows.** The tree marks a request with
  unsaved edits, and the first keystroke in its URL makes it unsaved. The editor's edits read
  the flag before and after (`DocEdited`): the edit that flips it takes the full path, the rest
  are view-only. A part shows a value only when the value is an input to it; a part that showed
  the typed text itself would have to rebuild on every key.
- **A part's own controls can rebuild the part alone.** The response pane's tabs, its
  pretty/raw switch and its find bar change nothing else, so they write a second signal only
  the pane reads: no parent build at all. A helper that has work waiting for a build (a
  `FindBar`'s `Service()`, which gives its field the keyboard) then has to run after the part's
  builds too — but not after the first, which runs while the parent's build makes the host,
  before the part is in the window (`Mount().Parent` is still null).
- **A part can hear the revision before the parent removes it.** Outside an event handler a
  signal write runs its effects at once, so `MarkDirty()`'s bump rebuilds the parts before the
  parent's own rebuild takes out one that no longer belongs (the pane, when the last tab
  closed). Its build must cope with that state: return an empty box.
- **A pane that gives way can keep its sizing on its root.** The bottom pane (380 high,
  shrinking to a floor) needs nothing on its host: an unsized host takes its content's height
  and floor. Only a host whose root never shrank needs `FlexShrink(0)`; left out, the tree and
  the right pane gave their width to the editor.

Meridian with the four parts (headless, 1400×900, a key and two frames; NativeAOT the same): a
keystroke in the URL 237 → 72 KB (−70 %), elements patched 218 → 54; in a 200-request collection,
where the tree had cost about 4.4 KB a request on every key, 938 → 72 KB and the key's time −22 %;
the header, param and body editors −64…−70 %; a History key −35 %; a find character in the
response rebuilds only the pane (no window build, −12 %). The time of a key and its frames moves
less (−1…−7 % at 25 requests): at this size the frames, not the build, are most of it.

**A change some parts show takes a revision only they read.** A switch between request tabs (a
tab clicked, a request already open opened from the tree) and a send's start and end change what
the tree (its lit row) and the response pane show, and nothing the title bar (the workspace and the
environment) or the rail (the view) shows. They called `MarkDirty()`, so those two rebuilt on every
switch. They now call `TabDirty()`, which writes a third signal only the tree and the pane read and
rebuilds the view (round 12, step 2). Two sent tabs (an HTML and a JSON body), headless, JIT: a
switch 227.5 → 192.5 KB (−15 %) and 5 → 3 builds, a send 369.4 → 299.4 KB (−19 %) and 11 → 7
builds; in a real window (NativeAOT, clicks pushed into SDL's queue) 287.5 → 251.4 KB a switch and
525.6 → 453.5 a send. Before giving a common path a part of its own, look at which of the kept
parts its full rebuild builds that show nothing of it.

A field holds the text typed into it, so a key needs a build only for what else on screen reads
that text. On the Headers, Body and Auth sub-tabs the URL is read only by the Params and Chain
counts: a URL character compares the two with what the last build showed, and builds nothing when
neither moved and the request was already unsaved (round 7, step 10: 66.6 → 4.0 KB a character
and −17 % time on NativeAOT, −27 % on JIT; a query typed out −83 %). A header value and the body
make the same comparison, the body also with its type (the first character into a request with no
body block makes one, and the sub-tab says "Body json"); a value cell of the Environments table
builds only when it makes its environment unsaved, the one thing the view shows of it besides the
cell. That table had cost one `TextInput` per cell and a keyring read per secret on every
character: 312 KB at 3 environments × 12 keys, 1.2 MB at 5 × 40, then 2.3–5.4 KB (round 9,
step 8).

The other apps follow the same rule (round 10, step 5). What reads a field decides what a
character builds:
- **Only the action that takes the value.** Junction's prompts (`OpenDialog`) and its rebase
  reword are read on Enter, by the validator and the confirm, and on Save: a character writes the
  field's value and builds nothing. It had built the window, chrome too: 194 KB a character over
  Changes, 155 over History, 333 under the rebase card; now 0.6–1.4 KB.
- **Whether the field holds anything.** Aperture's issue reply and problem report turn Send on
  when they do (and Resolve says "Reply and resolve"): compare before and after, and build the
  view when it flips. 135 → 2.3 KB a character in the reply, 100 → 0.9 KB in the report.
- **The view, on every character.** Junction's add-repository folder (its hint, preview and
  button, and the folder check that lands after) and the merge requests' filter (the rows kept)
  still build the view, with `MarkViewDirty`: the chrome shows nothing of a form. The folder went
  from 472 to 195 KB a character (the check's landing had built the chrome a second time), the
  filter from 222 to 92.

**A selection a list and one panel show needs neither the window nor the view.** History's list
draws its own selection, and the only other thing that shows it is the archived send beside the
list; still a key built the whole window for that panel. The panel is now a part with a signal of
its own (round 8, step 8): a key sets the mounted list's `SelectedIndex` and highlight through its
ref (ELEMENTS.md, "the list is then the only thing that shows the selection") and writes the part's
signal, so the panel builds alone. The view's build places it as Junction's composer is placed,
comparing the send it shows, the day its time reads against and the full-rebuild revision, so a
search character or a filter that leaves the selection where it was builds no panel. The key falls
back to a build of the view when there is no panel yet, or when the list on screen was made from
other rows than the key walked (a send logged with no build since). Headless, 300 sends, 1440×900,
a key and three frames, JIT: Down 52.1 → 21.6 KB (−58 %; NativeAOT 51.4 → 21.6), no window build; a
search character with a send selected 56.6 → 40.9 KB (−28 %; NativeAOT 61.1 → 45.5, −26 %). The
key's time does not move: its frames are most of it.

Vesper keeps its title bar, rail and command line, and each pane's tab strip, path trail, column
header and footer, this way (round 5, step 12), and adds four lessons:

- **Let the parent's build place the parts.** Junction's and Meridian's parts read a revision
  signal, so a full rebuild built them from the signal's effect: inside an event when the dispatch
  ended, after the parent's build and its `OnAfterBuild` (which still saw the old parts), and
  outside one before the parent's build, in a state the parent might be about to remove. Vesper
  places each part through a check in its build (`Part.Place()`): a part whose revision moved
  builds there and then, where its elements used to be built, so the command line's field exists
  when `OnAfterBuild` focuses it. The revision for a full rebuild is then a plain counter. A part's
  own signal stays for a part that rebuilds alone (the column header under the pointer, with no
  window build), and a loader a part reads still rebuilds it (a footer hears its listing land).
- **Compare, in the view-only path, what a view-only change can move.** Tab, a click in the other
  pane, a mark, type-to-find and a thumbnail landing all call `MarkViewDirty()`, which compares the
  few inputs of each part that such a change can move with what the part was built from: which
  pane is active (a trail lights its root crumb in the active pane), the mark count, the filter,
  the files cut and the decodes running (a footer), the filter, a notice and an open prompt (the
  command line). The parts that show what moved build; the paths need not know which part shows
  what.
- **Split a part by what changes together.** One header part per pane made Tab build both tab
  strips and column headers as well (159 KB a key), because the trail shows which pane is active:
  as three parts, Tab builds the two trails (106 KB), and the pointer over a sort column builds one
  column header and no window (378 → 26 KB a move).
- **Set what a build shows before asking for it.** Duplicating a tab set the copy's view and sort
  after the build that showed the tab; the next rebuild of anything used to cover that, and a kept
  part does not. With parts, a full rebuild no longer happens by accident.

Vesper with the parts (headless, 1240×820, 400 files a pane eight folders deep, a key and two
frames; NativeAOT within 2 %): a cursor key in Detail 210 → 34 KB (−84 %), elements patched 169 →
7, and the other pane is not touched; with the F3 partner ring 218 → 44 (−80 %); Brief 199 → 37
(−81 %); Thumbnails 219 → 58 and 257 → 94 (−74 %, −63 %); a mark 362 → 190 (−47 %: a mark still
made every visible row again, until round 6 put it in the row's item: 191 → 42); Tab 207 → 105 (−49 %); a folder in and out 554 → 273 (−51 %); a
filter character 365 → 209 (−43 %); a walk with the preview panel 329 → 73 (−78 %). With a reader
the same bytes come off and the same accessibility trees are built. A key's time on NativeAOT
−7…−21 %: the two frames are most of it.

`Part` in `HultUI.Core.Components` is Vesper's rule, public since round 6 step 13: the group's
revision is a plain counter, read where the parent's build places the part; a signal of the part's
own (`own:`) rebuilds it alone; `afterBuild:` runs after its builds but the first, as Meridian's
`Service()` needs; `Place(stale: …)` takes what the parent compared for a view-only build, as
Junction's composer compares what it shows of the working copy. Vesper's parts are on it as they
were. Meridian's and Junction's chrome read a revision signal and are mounted, not placed: on `Part`
with that signal as `own:` they build when they always did, and placing them would be a change of
its own.

Aperture's title bar and rail are its first use, and add one lesson: **a build a loader asks for is
a full one.** A loader that lands builds the app through a signal, with no `MarkDirty()` or
`Rerender()` to invalidate the group, and what it brought can move a plain field a part reads:
opening a report marks it read as its loader lands, and that takes a badge off the rail. So the
app's `Build()` invalidates the group unless the path that asked said view-only, with a flag
`RerenderView()` and `MarkViewDirty()` set, and `Rerender()` and `MarkDirty()` still invalidate it
themselves, since a batch can hold a view-only rebuild and a full one. Headless, 600 films, 1440×900,
a key and two frames, NativeAOT within 1 %: Right 87.3 → 49.2 KB (−44 %), Down 114.1 → 75.9 KB
(−33 %), a resize across a column count 118.9 → 80.8 KB (−32 %), no part built on any of them.

A worker's progress tick is a view-only change too. Vesper's copy, pack and folder-size workers
asked for a full rebuild about every 40 ms, so all eleven parts were built some 25 times a second
for the whole copy, though a tick moves only the progress the view shows and, of the parts, the
command line (a copy running in the background shows its percent and count) and the footers (a
folder's measured size). The tick now takes `MarkViewDirty()`, with the percent and the count in
the command line's compare, and a folder-size walk builds the footers outright (round 6, step 5;
headless, JIT): 211 → 29 KB a tick in the background, 284 → 100 KB with the progress dialog open,
which the view builds. Throttle such ticks on `Application.Time`, as `Build()` is not a clock says,
so a headless test counts them exactly. The perf suite pins it: `component/worker tick beside kept
parts, every part built` against `…, view only`.

### Hand back the elements a key does not change

What the reconciler does for a kept host it does for any live element a build hands back: it
moves the element into the new tree and puts it back where it was, untouched — no patch, no
layout mark. A grid of many small cells that a virtual list cannot hold (Vesper's Brief view:
columns of names, scrolled sideways) can keep its cells that way: remember each cell with what
it was made from, and when a build would make it from the same things, add the live element
instead of a new one. Only the cells that changed are made, and patched into the ones they
replace. Four things keep it right:

- **Key the cells, and key every child of their parent.** A kept element comes back through
  the keyed reconcile, which matches the rest by key and puts the kept ones back in place. In
  an unkeyed parent the old children are matched by position, and the ones the build moved out
  are missing from it: the reconciler would patch one cell from another.
- **Only from the build just before.** A cell another build dropped is out of the tree (its
  column scrolled away, the view changed); handing it back is mounting a removed element again.
  Count builds and keep the cells for the next one only.
- **Keep the live element, not the one you built.** A changed cell is patched into the old one,
  and the element the build made is thrown away; keep a `Ref` on it, which follows the patch.
- **Everything the cell shows is in what you compare.** The entry, the cursor, what only some
  cells show (a mark, a file cut for a move) kept with each cell, and a shape of what every cell
  shows (the theme, the row height, the scale). A set in the shape makes every cell again when one
  member changes: Brief kept its marks there until round 7 (step 9), and a mark made all 140 cells
  on screen, 471.0 → 35.7 KB a key once each cell kept its own (NativeAOT, Insert; Space, Shift+arrows
  the same).

Vesper's Brief view this way (round 4, step 13; headless, a key and two frames, 300 files): a
key 759.9 → 237.8 KB (−69 %), what the rest of the window's build costs; the NativeAOT app in a
real window 1,093 → 242 KB a key. A resize that changes the columns keeps them too: each cell
moves to its new column (755.8 → 229.4 KB a step). A type-to-find key still makes cells (about 45
a key, 400 files): every one is an entry the filter brought into the columns on screen, which had
no cell to hand back; none on screen before is made again.

The same works inside a virtual list whose item is a row of cards (Vesper's Thumbnails, round 6
step 6). The list makes a row when its item changes — the cursor moved onto it or off it — and
makes it in the frame's layout, not in a build, so two of the rules read differently. **Key the
row** (by its place: "t" and its first entry's index): a keyed row the list lets go of is held
back and patched from the row made in its place (`TreeDiffer.TryPatch`), which is where a card
handed back is put back untouched. **Hand a card back only into its own row's place**: the card's
live element must still be a child of a row with the new row's key — the row being replaced. A
card in a row still on screen belongs to that row, and one in a row that scrolled away is out of
the tree. Keep each card with what it was made from (its entry, the cursor on it, its mark, its
picture as the cache had it) and let them all go when anything every card shows changes (the
sizes, the theme, a result set). A cursor move then makes the two cards it moved between: Down
88.3 → 42.9 KB a key, cards made 11 → 3; a mark, which makes every row again, 315.7 → 56.3 KB.

Aperture's library is the same shape (round 6, step 13), and adds four lessons:

- **Compare what the card shows, not how it got there.** A poster's state holds the picture it
  draws, or null for the placeholder: loading and failed look the same, so a failure makes no card.
- **Ask for what a make keeps fresh before deciding.** The picture cache is asked for every card of
  a row made again, handed back or not: that keeps a picture on screen recent in the cache, and is
  how a row made for a landing finds the one card that landed. A kept card keeps its image element,
  and a card patched keeps it too, so nothing flashes.
- **What a card needs is a row still in the list.** Its row's parent is the list: a row the list
  let go of is out of the tree. A row on screen is not asked for its cards, since a row is made for
  the places it holds and only a change of columns, which makes them all again, moves a place to
  another row. Then the card moves with its place: a resize across a column count 341.5 → 118.9 KB.
- **A kept element keeps the keyboard, and what the code thought would go.** The cursor focused its
  ref's card after the build, but a list makes its rows in the frame, so the ref still held the
  card the cursor left. That card used to go with its row and the focus with it; kept, it would
  have held the keyboard one card behind the cursor. Note which place a ref's card was made for.
  The other side is a gain: a card Tab reached no longer loses the keyboard when its row is made.

Headless, 600 films with pictures, 1440×900, a key and two frames, NativeAOT within 1 %: Right
115.0 → 87.3 KB (−24 %; with UI Automation 128.1 → 87.3, and whole trees 0.34 → 0 a key), Down
181.9 → 114.1 KB (−37 %). The suite pins it: `virtual/image grid walk Right, 600 cards` against
`…, cards handed back` (48 → 22 KB, two cards made).

**`KeptElements<TState>` holds these rules** (`HultUI.Core.Components`, round 7 step 11; ELEMENTS.md
§28). Brief, Thumbnails and the library grid each wrote them out by hand, and Brief's marks slipped
into the shape; new code uses the helper, and all three are on it now (the grid since round 8 step 5,
Brief and Thumbnails since step 7):

- `Made(key, state, element)` keys the element and keeps a ref that follows it to the live one, so
  the first and third rules are its job; key the element's siblings too.
- `TryKeep(key, state, out element)` compares the state with what the element was made from, so put
  everything it shows in the state, a setting every element shows among it: a record struct makes
  that a field more, not a set in a shape that remakes them all. Its handlers are the old build's;
  what they capture belongs in the state.
- `Begin(build)`, with the owner's count of its builds, is "only from the build just before". It is
  stricter than "still in the window": a build that left an element out may have patched it, by
  position, into something else that is still there, and a build of the owner that showed another
  view may have done that to all of them. `TryKeep` checks the window as well (`Element.IsHosted`).
- `TryKeep(…, place: key)` is the row-place check for cards a virtual list makes in a frame: those
  skip `Begin` and are handed back only into a parent keyed so.
- **A grid whose columns change passes no place.** A change of columns moves every place but the first
  row's to another row, so the row-place check makes every card on screen again. Aperture's library
  did that from round 8 (step 5) to round 9 (step 1): 249 elements and 110 text measures a change of
  columns. Its rows now check what a card needs themselves (`CanTakeCard`): the card's row is still a
  child of the list (after a change of columns, one the list is about to let go of), and a card under
  the pointer or pressed goes only back into its own row, where a full build leaves it too; moved, it
  would take its hover to where the pointer is not. Every card then moves to its new row untouched, the
  pixels, rings and reader tree what a grid made from nothing shows. Headless, 600 films, 1440×900,
  JIT: a change of columns each step 235.6 → 62.5 KB, 249 → 58 elements made (the view's build and
  the rows), text measures 110 → 14.5; a drag over 40 widths 13.6 → 4.9 KB a step. The suite pins it:
  `virtual/image grid across a change of columns, 600 cards, cards kept in their own row` against `…,
  cards kept`.

Aperture's Home is its first user (one helper per shelf, as one film can stand on two shelves). The
perf suite pins it: `component/shelf walk Right, 5 shelves of 7 cards, every card made` against `…,
cards kept`.

**A shelf whose cards all come back can come back whole** (round 8, step 6). Home builds mostly when
pictures land, and with every card handed back it still made each shelf's heading and row around them:
39 of a build's 46 elements. Each shelf is kept too now, by its row's key, with three rules on top of
the cards':

- **Ask for the children first, every build.** Each card on the page is asked for before the shelf is
  decided: that asks for its picture (how a landing finds the card it changed) and keeps its entry
  current for the next build and for a ring moved with no build. The shelf comes back only when every
  card came back as the live element (`IsHosted`).
- **The state is what the heading's handlers read, not only what it draws.** The row's menu worked out
  "Move up" and "Move down" from the list of rows its build captured, so a row moved past a hidden one
  drew the same and offered a stale menu. The row's place among all the rows is in the state, with the
  title, the count, the page and how many a page holds, "See all" and the scale. What a handler reads
  when it runs (the settings) needs nothing.
- **Key every child of the parent**: the watchlist's shelf, made every build, and the hidden-rows
  footer too.

Headless, 1440×900, six shelves and 36 cards with pictures, JIT (NativeAOT the same within 1 %): a
Home build 66.0 → 26.3 KB, elements made 46 → 4; Home opening while its 36 posters land, one poster a
build 3,368–3,433 → 2,281–2,312 KB (−32 %), six a build 1,547 → 1,371 KB (−11 %); at 1000 px a walk
that turns a shelf's page 13.8 → 6.8 KB a key.

**Remember what the asking makes.** What is left of that build is mostly the asking: each card's
picture address, made again for every card each build with its string, which the cache keys on (577 B a
card against the fake library, 1,136 B against Jellyfin's). Aperture now keeps each address by the item,
the kind and the size in pixels, checks it against the item's picture tags, and lets them all go when
the data is reloaded or 1,024 are held: a Home build 26.3 → 11.1 KB, one poster landing a build
2,260–2,265 → 1,721–1,730 KB to open Home (−24 %, NativeAOT the same).

**A key that moves only a ring needs no build.** Nothing on Home shows the cursor but the ring on one
card and the keyboard on it. So a cursor key there takes the ring off the live card it leaves and
puts it on the one it reaches (`KeptElements.Live`), hands that card the cursor's ref and the
keyboard, and builds nothing. The next build compares each card with what it was made with, so the
two are made again then. Two checks keep it right: Home's latest build is the app's latest (a build
of another view may have patched its cards into something else), and both places are cards the
helper knows (a watchlist title card, or End in a list still loading, takes the build). Make the ring
one value that the build and the move both use, so the two cannot drift. Headless, 1440×900, a key
and two frames, NativeAOT (JIT and UI Automation the same): one shelf (the fake library) 63.7 → 0.2 KB
a key, and five shelves of seven 255.7 → 0.3 KB, no build, −8…−11 % time a key; the cards kept
without the ring move (a build a key that hands back all but two cards) took 32.9 and 70.7 KB. The
suite pins it: `component/shelf walk Right, 5 shelves of 7 cards, ring moved without a build` (219 →
0 KB a key against every card made).

**A key that still builds moves the live ring first.** A card the ring reached with no build was made
without the ring, so a build at another cursor finds it equal to what it was made with and hands it
back, ring and all. In a virtual list the row is not even made again: the list keeps a row while its
item is equal, and its items are its last build's, which know nothing of the moves since. So when a
key has to build (Aperture's library grid: the card it reaches is not in a realized row, or the
cursor is within a page of the loaded end, where the build asks for the next page), it takes the ring
off the live card it leaves and puts it on the one it reaches, if that one is live, and then builds.
A card the list makes in the frame after the build takes the keyboard once that frame is drawn
(`Window.RequestPostFrameCallback`); asked straight after the build, it does not exist yet, which is
why the grid's cursor never had the keyboard. A change of columns asks the same when the keyboard is on
the cursor's card: handed back, the card keeps it, but one made again in its new row (its ring moved
with no build since it was made, or it was the first row's last card, which the patched first row lets
go of) would leave nothing focused. Aperture's grid, 600 films, 1440×900, a key and three
frames, JIT: Right 49.2 → 0.3 KB a key, Down 81.9 → 29.1 (what is left is the rows scrolling in), no
build a key; with a reader the same, and no tree built. Home's shelves do the same when a move builds:
onto a watchlist title card, which every build makes (the card left kept its ring, two rings on
screen), and onto a card on another page of its shelf, which turns the shelf to that page (Discover
too) so the card the cursor is on is the one on screen.

**Every build puts the live ring back first, not only the key's** (Vesper, round 8 step 7). A key in
Vesper that moves only the cursor builds nothing: Detail's list draws the cursor, so the key tells the
mounted list its new `SelectedIndex` and scrolls it there, as the command palette does; Brief and
Thumbnails take the ring off the live cell or tile left and put it on the one reached
(`KeptElements.Live`). Moving the ring first on a key that builds was not enough there: a click, Tab
(the pane it leaves shows no cursor) or a filter also builds after keys that did not, and handed back
a cell the ring reached as it was — Tab left a ring in the pane it left. So each build of the pane
first puts every cell and tile a key restyled back as it was made, and has the thumbnail list make its
rows again; then every element shows what its state says, and the build compares states as always. A
tile the list makes in a frame (its row scrolled in) reads the cursor as it is, not from its row's
item, which is the last build's. Three more lessons:

- **Decide by the cheap facts first.** A move also moves the F3 partner ring in the other pane. A folder
  pairs with nothing, and a same-named row over there is the partner and moves with every key, so that
  builds; only when the other pane's cursor can pair does the key ask F3's own rule (a path made, a file
  looked up), and then the ring stays. Asked on every key, that rule cost 0.9 KB, a twentieth of the
  build it saves.
- **Restyle, so a hover keeps the ring.** A tile has a hover tint, and a ring set directly was gone once
  the pointer left a tile hovered before (`Element.Restyle`, below). The tint reads the tile's border: no
  tint while it is the ring, so a tile need not know at its build whether it will have it.
- **A key that scrolls Brief builds**, as before: its columns are made for the scroll a build reads, so
  the key after a page jump builds too, and the reader tree stays what a build makes.

Headless, 400 files a pane, 1240×820, a key and three frames, idle-gated, JIT, UI-thread bytes a key
(the three frames included): Detail Down/Up 17.1 → 1.7 KB (with a reader 17.5 → 2.1), Brief Down/Up
31.9 → 1.8, Right/Left 32.2 → 1.7, Thumbnails Right/Left 27.7 → 1.2, Down/Up on screen 31.3 → 1.4, a
30-key walk that scrolls 57.5 → 26.3 (what is left is the row of tiles each key scrolls in, as in
Aperture's grid); no build a key. In 47 scenes, each in a dark and a light scheme (zebra, a sweep,
Insert, a filter, both panes and the partner, the preview, the pointer on a tile), the pixels, the
reader tree, the ring, focus and every text matched the app that builds every key, and a full build
after them. The differences: a tooltip, which the headless pump draws only while frames are being made;
after End in Thumbnails a list's height in the reader tree 0.0005 px from a later build's; and, as
before the change, Brief's columns after a key that scrolls them, made for the scroll before it until
the next key.

**A part that every full build makes keeps its rows too** (Meridian's request tree, round 9 step 9).
The tree builds with each `MarkDirty` — a request opened, a send's start and its end, a save, each toast
— and made every row again, with the ⋯ button and the menu each row gained in 41f48bd3. Each request
and folder row now comes back by its path. Its state is what it shows: the method, the title, lit,
unsaved, the first variable it feeds forward, how many it uses, its depth and the scale; a workspace
opened lets them all go. **Capture the key, and look the rest up when the handler runs.** A folder's
menu captured the folder's record from the scan its row was made from, so a kept row's Delete… would
have counted the requests of that scan, not the one on screen; it now finds the folder by its path when
it is picked. Headless, 1440×900, JIT, UI-thread bytes: a full build at 25 requests 327.6 → 127.6 KB
(elements made 294 → 117), at 200 requests 1,587.5 → 132.8 KB (1,407 → 117; the tree part 1,389.2 → 15.8),
now flat in the collection's size; a toast at 200 requests 3,180.7 → 272.1 KB, a send 3,270.4 → 361.0;
NativeAOT within 1 %. In 29 scenes (opens, a send in flight and ended, an edit, a save, a rename, a
delete, new requests and folders, a folder renamed with what is in it, an environment, a toast, hover
and keyboard on a row and its ⋯ across a build, History and back, changes on disk, the scale), the
pixels, the reader tree, focus and every row's menu, each item run, matched a build that keeps nothing.

**A selection only the view shows builds the view, and hands back the rows it does not move**
(Meridian's Discovery map, round 9 step 11). A click on an endpoint built the whole window, the title
bar and the rail too, and made every row of the map again: with a large spec imported, about 4.5 KB an
endpoint a click. The click now builds the view alone (`MarkViewDirty`: no part shows the selection),
and each row and group heading comes back by its key unless what it shows moved. The state is the
endpoint itself, a record that is equal when every fact the row shows and the detail its click selects
is equal, and whether it is selected; the click captures that endpoint, so a kept row selects what a
row made now would. The order, the headings, the keys and the counts are worked out once a map, since a
map is never changed (a send makes a new one). Two lessons:

- **Let the kept elements go when the view is not built.** `KeptElements` held the live elements, so a
  map of 2,000 rows left for another view would have stayed alive behind it, which a test already guarded
  against (a still pointer had done the same). The app's build forgets the rows when it made none. Since
  round 10 the helper holds its elements weakly anyway (see "A ref into a view keeps the whole view",
  below); forgetting still lets go of the states.
- **What is left is the list itself.** The build still walks every line, asks for each row and hands
  the list its children: about 35 B an endpoint a click, against 4.5 KB.

Headless, 1440×900, a click and three frames, idle-gated, JIT: 60 endpoints 365.3 → 54.7 KB a click
(−85 %), 600 2,846.6 → 77.2 KB (−97 %), 2,000 9,073.0 → 124.4 KB (−99 %); elements made 333 / 2,493 /
8,129 → 55 / 55 / 63, no part built; a click's time 1.5 → 1.1 ms, 4.3 → 1.5 ms, 6.4 → 2.4 ms (min;
NativeAOT the same bytes, 0.78 → 0.71 ms, 1.47 → 0.87, 3.82 → 1.60). The
pixels over a click walk match the old build's, and in a walk of clicks (the same row twice, the pointer
over another row, a scrolled map, sends folded in, an observed row, the observed toggle, another view
and back) the pixels, the reader tree and focus match a build that keeps nothing. Each row now tells a
reader whether it is the selected one (`AccessibleSelected`).

What was left went in round 10, step 11: the map is now a virtual list of its lines (item rows, ELEMENTS.md
"A scroll view of rows becomes a list"), so a build patches the list rather than a column of every row,
and a hover move, a wheel notch and a drawn frame hit-test and cull the rows on screen. At 600 / 2,000
endpoints a click allocates 77.3 / 124.0 → 51.5 / 59.9 KB and visits 1,847 / 6,131 → 116 elements in
hit tests, a hover move 618 / 2,046 → 41, and the map holds 2,488 / 8,230 → 168 / 183 KB of the heap.
Then the click stopped building the view: only the list and the detail beside it show the selection,
so the detail became a part of its own (as History's archived send is, "Keep what a key does not
change in a child component"), and a click sets the list's `SelectedIndex` through its ref and writes
the detail's revision. The first selection still builds the view, which places the detail. A click:
51.5 / 59.9 → 22.2 / 30.6 KB, no build.

**A list that grows a row at a time keeps the rows it has, and builds alone** (Meridian's Runner,
round 10 step 4). A run adds a result row as each request answers, and built the whole window for it
with every row again: a run of n results made about n²/2 rows, 7.3 KB each, the list 1,467 KB of a
build at 200 requests. Each row now comes back by its request's path (a run runs a request once), its
state the result (a record nothing changes), its place and the scale; a run that starts over lets them
all go. That left about 90 KB a result for the rest of the window — the scope rail, the title bar and
the rail of views — none of which shows a result. The progress line, the list and the summary are now
a part with a revision of their own (see "Keep what a key does not change in a child component"),
which a result writes: about 20 KB a result, and a run going on behind another view builds nothing
there. **Number the builds of a part's kept rows by the part's own builds**: numbered by the window's,
every build the part made alone would have looked like a gap, and nothing would be handed back. A run
of 200 (headless, the server waiting 20 ms a send, JIT; the rail's counts, taken from the scan since,
took most of it: "What a build shows of files comes from what was read already", below): 1,799,085 → 24,302 KB a run, GCs 107/19/2 → 1/0/0, MeridianApp builds 202 → 2; what is left is
the run's own sends. At every checkpoint of a run of 50 and of 200 the pixels and the reader tree match
a build that keeps no row, and the trees the old build's.

**A plain list the window builds keeps its rows, and a cursor key moves the ring on them** (Vesper's
Synchronize, round 10 step 7). Synchronize puts every row in a plain scroll view, and each Up or Down
built the window, both parts and every row; each row also found its place for its click in a fresh
filtered copy of the rows shown, so a key was O(rows²) in bytes. The build now passes the place from its
loop and keeps each row by its path (its state the pair, its direction and whether the cursor is on it;
the shape the row height and the theme), and a cursor key moves the ring on the two live rows with no
build, as Brief does, and scrolls the cursor's row into view by its place (`ScrollRangeIntoView(index ×
row height)`: every row is one height, so nothing is read back). Three lessons:

- **The cursor's look can reach into the row.** An Equal row draws its texts at 55 % unless the cursor
  is on it, so the move sets its seven texts' colours too. `TextColor` is an inherited style, which a
  write on a live element does not mark (`MarkStyleDirty`), and the ring's 1 px border moves the cells
  (`MarkLayoutDirty`). One function draws the look for the build and for the move.
- **Set the role to tell the selected state with no tree.** A clickable row is read as a button by
  inference, and the window marks a selected state on its node in place only for a role the app set. The
  row sets `Button` and says selected or not on every row, so a key tells a reader the row left and the
  row reached, with no tree; the reader's tree is what it was, with the selected state added.
- **A handler looks up the place when it runs.** A kept row may stand elsewhere at its next click
  ("Show equal" moves the rows after an Equal one), so the click captures the pair and finds its place
  then.

Headless, 1240×820, 80 Down/Up keys at a 60 ms pace with their frames, UI-thread KB a key, NativeAOT
(JIT): 60 files (40 rows) 453.7 → 0.4 (422.6 → 0.4); 300 files (200 rows, 1,886 elements) 2,568.5 → 0.4
(2,054.2 → 0.4); 3,000 files (2,000 rows) 81,102 → 0.4 (47,962 → 0.4), GCs per 80 keys 451/262/66 →
0/0/0; a window and two parts built a key → none; with a reader 0.9–1.0 KB, 0 trees. The place from the
loop alone took 300 files to 1,715.9 KB and 3,000 to 16,444. The reveal costs at 3,000 files 1.2 → 1.7 ms
a key (min; the screen of rows a scroll brings in), against 87 ms before. At every step the window, the
reader's tree and the pixels match a build that keeps no row. The suite pins it: `component/cursor key
over a plain 200-row list built through the root, every row made` against `…, rows kept` (1,605 → 50 KB
a key, 200 → 2 rows made).

A column whose cells are all kept is left with no children by the time the reconciler reaches
it, since the build moved every one into its new tree. Until round 5 those came back one by one,
each marking the column's structure, so with a reader listening every key built the whole
accessibility tree; now they come back as they were and mark nothing (Brief with a reader, 400
files: 300.6 → 230.2 KB a key, 1.00 → 0 trees; Reel's player, alone in its container, 5.8 → 2.0
KB a build). The same elements in the same order is the rule: a column handed back in another
order, or with a new cell, marks its structure as any change does.

### A ref into a view keeps the whole view

An element knows its parent, and a subtree the reconciler removes keeps its links inside, so whatever
holds one element of a view that left the window holds all of it: every row, every text, the pictures.
Nothing on screen shows it; only the heap does. A `Ref<T>` field is the usual holder, since it keeps the
element it last pointed at until something sets it again; a list a build fills and keeps is the other.
Plan 17 round 10 found five behind the view that replaced theirs, each from a change that kept elements
for speed:

- **Empty a ref in the builds that do not show its element.** Meridian's build empties the response
  panel's refs (the open tab, the body) when it placed no panel, as it already did the send's elapsed
  readout, and the URL field's at its start, since only that build makes the field and sets it again.
  Aperture's build lets go of the library grid's ref when it shows another view. Before, each of them kept
  the Requests view or the library behind History, Environments or Settings.
- **Where views share a ref, let go once the build is done.** Vesper's cursor ref is the row, cell or tile
  of whichever view draws the cursor, so no one view can empty it. `OnAfterBuild` lets go of it, and of
  Brief's scroll view's, when its element is no longer in the window (`Element.IsHosted`), after the
  build's own reads; the build or key that next draws the cursor sets it again. Round 11 did the same for
  Aperture's cursor card, the one card of whichever view drew the cursor, which kept Home or the grid
  behind Settings once a key had moved it, and for Vesper's thumbnail list.
- **Every ref into the view, even one nothing reads** (round 11). Meridian's History had three: the
  list's, the find field's and the archived send panel's. Emptying the list's alone freed nothing; the path
  search found the other two, and nothing read the field's at all: `WithRef` makes the ref a holder
  whether or not anything reads it. The build empties all three when it shows another view.
- **A ref a part's build sets comes back with the part** (round 11). History's panel is a `Part`, and only
  its own build sets the panel's ref; a part kept between the owner's builds does not build, so emptying
  the ref at the start of every owner build would leave it empty while History is on screen, and every key
  would build the window. Emptied only in the builds that show another view, it comes back: the part's
  host left the window with the view, an unmounted host lets go of its component, and the next `Mount()`
  makes a new host that builds at once.
- **Keep the value a later build reads, not the element, and give it to the new element.** Aperture's
  library build sized what it asks for from the old grid's viewport height; that is kept as a number now,
  so what the next build makes is what it was. Vesper's Brief kept its old scroll view's offset the same
  way and made its columns for it, but the build that brings Brief back makes a new scroll view, which
  opened at the start, and every column in view was blank (round 10). The build now sets the kept offset
  on the new scroll view, which keeps an offset set before its first layout (ELEMENTS.md, section 6),
  and makes the columns for it: Brief comes back where it was left. Before emptying a ref, look for
  what reads it between builds (a handler, a post-frame callback, a timer, the next build).
- **Empty a scratch list when you are done with it.** Aperture's Home reused one list of a shelf's cards
  from shelf to shelf. Full after the build, it held the last shelf's cards, and through them the whole
  Home that build made, while Home was on screen (a twin tree of 250 KB at five shelves) and behind every
  other view after it.
- **`KeptElements` holds its elements weakly** (round 10). An element is handed back only while the window
  holds it, so `TryKeep` and `Live` return what they did. Held strongly, Meridian's request tree, which only
  the Requests view builds, kept that view alive behind every other view from the day its rows were kept
  (round 9): 1,350 KB of a 5,770 KB heap at 200 requests. The weak ref is the same 24 B object a `Ref` is,
  made once per key, and one weak GC handle; no build allocates more.
- **The window lets go of the scroll view a wheel gesture latched to** (round 10). A gesture stays with its
  scroll view until the next wheel event (LAYOUT.md, "Nesting scroll views"), and that reference kept a view
  that was wheeled and then left alive until the wheel next turned, anywhere in the window: Meridian's
  Discovery map behind the Requests view, 8 MB at 2,000 endpoints. Now the frame that removes the scroll
  view lets go of it, as do an overlay that closes with it and a new root. An app has nothing to do; a
  retention check that wheels before it switches views measured the latch before this, not the app.

To find a holder: take a weak reference to an element of the view, switch views, collect
(`GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();`), and if it is alive, walk the app's fields
breadth-first for a path into the old view's tree. To pin a fix, assert the weak reference is dead, and
take it, and any read of the view, in a `[MethodImpl(MethodImplOptions.NoInlining)]` helper: a value read in
the test's own frame can keep the element alive (Aperture's grid test did, until its reads moved out).

Headless, full GC and `GC.GetTotalMemory(true)`, the heap after the switch, JIT / NativeAOT:

| View left | Before | After |
|---|---|---|
| Meridian Requests, 200 requests, none open → History | 2,364 / 2,161 KB | 1,027 / 874 KB |
| Meridian Requests, 25 requests, none open → History | 933 / 773 KB | 733 / 585 KB |
| Meridian Requests, 200 requests, a 239 KB JSON response → History | 12,461 / 12,263 KB | 10,623 / 10,476 KB |
| Aperture Home, 5 libraries × 20 films → Settings | 1,528 / 1,386 KB | 1,013 / 867 KB |
| Aperture Home, 8 × 40 → Settings | 1,994 / 1,850 KB | 1,180 / 1,025 KB |
| Aperture library grid, 600 films → Settings | 1,666 / 1,539 KB | 1,292 / 1,163 KB |
| Vesper Brief, 400 files → Detail | 1,825 / 1,664 KB | 1,406 / 1,253 KB |
| Vesper Brief, 3,000 files → Detail | 2,646 / 2,472 KB | 2,214 / 2,062 KB |
| Meridian Discovery map, 2,000 endpoints, wheeled → Requests (the latch) | 14,283 / 14,037 KB | 6,052 / 5,901 KB |
| Meridian Discovery map, 600 endpoints, wheeled → Requests (the latch) | 4,314 / 4,127 KB | 1,815 / 1,657 KB |
| Meridian Discovery map, 60 endpoints, wheeled → Requests (the latch) | 1,122 / 948 KB | 841 / 670 KB |
| Aperture library grid, 600 films, wheeled → Settings (the latch) | 1,566 / 1,430 KB | 1,300 / 1,164 KB |
| Aperture Home, 5 × 20, wheeled → Settings (the latch) | 1,295 / 1,158 KB | 1,030 / 894 KB |
| Aperture Home, 5 × 20, 3 cursor moves → Settings (round 11) | 1,212 / 1,213 KB | 948 / 951 KB |
| Aperture library grid, 600 films, 5 cursor moves → Settings (round 11) | 1,485 / 1,517 KB | 1,221 / 1,239 KB |
| Meridian History, 200 sends → Requests (round 11) | 1,101 / 1,109 KB | 784 / 793 KB |
| Meridian History, 2,000 sends → Requests (round 11) | 1,495 / 1,489 KB | 1,178 / 1,173 KB |
| Meridian History, 200 sends → Discovery (round 11) | 1,115 / 1,123 KB | 799 / 808 KB |
| Vesper Thumbnails, 400 files → Detail (round 11) | 1,510 / 1,518 KB | 1,232 / 1,250 KB |
| Vesper Thumbnails, 3,000 files → Detail (round 11) | 2,213 / 2,207 KB | 1,935 / 1,932 KB |

The old view was alive before and is collected after in every row. The bytes of a build or a key did not
move (Meridian's full build at 200 requests 59.4 KB, Home and the grid's keys, Brief's Down and Insert;
in round 11 Home's and the grid's keys, a Home and a Settings build, a History key and build, the
Thumbnails and Detail keys and builds). Round 11's rows come from a host of its own (one process per
row), so their heaps are not comparable with round 10's.

### Keep the index out of a list's item

A virtual list on item rows (`.RenderWith((item, selected) => …)`) hands a row back to the index
where its item now stands (ELEMENTS.md, "Rows follow their item"): a filter, an insertion or a
removal moves the rows it shifts, and makes only the rows that come into the window. That holds
only while an equal item can stand at another index. An item that carries its index never does,
so every row a change moves is made again, as all of them were before round 7. Put in the item
what the row shows, and of its place only what the row shows of it: Vesper's Detail rows show an
odd row's zebra tint, so their item carries `Odd` while the tint is on, not the index. Its
type-to-find (headless, 400 files, a key and two frames, NativeAOT) went 195.7 → 88.5 KB a key
(−55 %) with the filtered rows made once a filter, rows made 26.5 → 9.3, median −10 %; with UI
Automation 334.9 → 227.7 KB. An automation id made from the index (Meridian's `hist-row-N`, the
command palette's `palette-row-N`) is something the row shows of its place: such a list keeps its
index in the item, and its rows are made again when they move.

The same goes for an object made again with its data: an item that holds a tree node compares as
that node, and a model that makes every node again for a change makes every row again with it. Give
the item an `Equals` that compares what the row shows and acts on (Junction's Changes compares a
node by its path, depth, segment, count and file, and carries a folder's totals): a stage there
moved the rows it shifted, 4–8 made of the ~20 on screen, where it made them all. And keep a derived
view while what it is derived from is the same: Changes made its view (git's answer with the
stagings in flight applied) again on every call, new lists each time, so each build while git had
not answered flattened the rows again.

The other half of the rule: keep out of what makes the rows again (`InvalidateRows`) what no row
reads. A width the rows are laid out at is layout's, and layout follows it without a new row.
Junction's diff kept the code column's width in the shape it compares, so a split drag or a window
resize made every diff row on screen again, though only wrapped rows (and the review's cards) are
measured at it; an unwrapped row shows the width only through the sideways offset, which is in the
rows' shape by itself and moves when a wider column clamps it. Leaving the width out unless rows wrap
or the review is on (round 10 step 8; headless, 1440×900, a move or step and two frames, JIT) took a
split-drag move 159.3 → 42.0 KB (−74 %; NativeAOT 157.2 → 41.2): the 127 rows the move replaced and
their first layout, render 119.5 → 2.2 KB. A 4 px resize step went 32.1 → 20.3 KB (−37 %). Pixels,
reader tree and focus are the same at every step of a walk through widths, the pan scrolled and
clamped, split, wrap, a split drag and a review.

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
| `OnBeforeBuild()` | Immediately before each `Build()` call. A rebuild asked for here is dropped (below) |
| `OnAfterBuild()` | Immediately after each `Build()` call and its reconcile. A rebuild asked for here is dropped (below) |

**A rebuild asked for during a build is dropped.** `OnBeforeBuild`, `Build` and `OnAfterBuild`
all run inside the component's build effect (`ComponentHost.Rebuild`). While that effect runs,
`MarkDirty()` returns without doing anything (the effect ignores a forced run from inside
itself), and a write to a signal the build read does not mark it either; when the build ends,
the effect notes the signal's new value as already seen. So the build you asked for never
comes, and the screen shows what this build made until something else rebuilds. (The one
exception: a build that runs inside another component's reconcile, such as a component mounting
again in its parent's patch, has its `MarkDirty()` deferred to the end of that pass, like any
other request made mid-reconcile.) To build once more, ask outside the build:

```csharp
protected override void OnAfterBuild()
{
    if (_needsAnotherPass)
    {
        _needsAnotherPass = false;
        // MarkDirty() here would be dropped: this build has not finished.
        Application.InvokeOnMainThread(MarkDirty);         // on the next frame
        // or, when the next build needs this frame's layout:
        // _window.RequestPostFrameCallback(MarkDirty);    // after this frame is drawn
    }
}
```

The rule is deliberate, and an app relies on it. Junction's diff binds its find bar's search to
the file it shows inside `Build` (`TextSearch.Bind` → the search's change → `FindBar` →
`MarkViewDirty`), on about 95 of every 100 keys that move the caret in the Changes list.
Honoured, each of those keys would build the view twice. Meridian's response body does the same
with a signal write on a switch between pretty and raw. The bar does not need that build: its
`Service`, from `OnAfterBuild`, writes the new count into the bar on screen. Work that only has to follow a build, such as a focus or a reveal, belongs in
`OnAfterBuild` as it is; only a second build has to be asked for this way.

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

**A child that comes and goes ahead of siblings of its own type needs a key.** When the number of
unkeyed children changes, the old and new children are matched from both ends while their types
agree, so a child that appears in front of children of its own type takes the first one's place:
that one is patched into the newcomer, and the next one into it, and so on. Meridian's stale-preview
banner (a row) came in above the assertion rows (rows) with the first key after a send; the first
row's fields were made again, the one being typed into lost the keyboard, and the next key went
nowhere. Key the newcomer (`banner.Key = "stale-banner"`); its unkeyed siblings are still matched by
position among themselves.

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

**Change a live element's look with `Restyle`.** The styles lay themselves over a base look the element takes when it is patched or first hovered, and put it back when the state ends. A background or border set on the live element directly, with no build (a ring moved from one cell to another, `KeptElements.Live`), lasts only until the next hover or press comes or goes: the element goes back to the base, without the ring. `el.Restyle(static e => ((BoxElement)e).BorderColor = ring)` takes the base look back first (so a hover tint showing now is not kept), makes the change, takes the result as the new base and lays the states that hold over it again. With no styles it just makes the change. A style that should differ with that look reads it from the element: it runs after the base is put back.

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

A rebuild does not do it for you on an element it did not patch. Each patch marks the
element it patched; the style pass resolves a child when the child is marked or when what
it inherits changed, and an element moved under another parent is resolved there. So a row
a `VirtualList` kept, the elements of a child component, or a control's own parts, written
through a `Ref` without `MarkStyleDirty()`, keep their old style however often the owner
rebuilds. (Until plan 17 round 4 a patch marked everything under it, and an owner's rebuild
re-resolved such an element by accident — and every one of the hundreds it did not change,
on every key.)

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

### A reload that finds the same hands back what is shown

A loader's readers rebuild when its value changes, and `Signal<T>` compares values with the
type's own equality: a new list is a change even when every item is the same, and so is a record
that holds a list, since a record compares a list member by reference. Junction's repository
watcher reloads a repository's summary and worktrees on every event, an editor's save included,
and each reload landed a new value: the title bar, the sidebar and the toolbar built again for a
repository that had not changed (round 12, step 2). The producer now compares what it read with
the value on screen, taken on the UI thread when the load starts, and hands that back when they
are the same:

```csharp
var shown = loader.Value;   // on the UI thread, as Load is
loader.Load(key, () => Read() is var loaded && loaded.SameAs(shown) ? shown : loaded);
```

The watcher's handler also stopped building the chrome to start those loads: the loaders are the
same objects from one generation to the next, so the parts already hear them, and starting the
loads from the handler is all the chrome's build did. A file created and deleted in the
repository, headless (JIT): 581 → 414 KB on the UI thread an event over History and 10 → 6
builds; a file whose write time moved and nothing else 578 → 274 KB and 10 → 2 builds, no part;
the Changes view 497 → 315 KB; in a real window (NativeAOT) 816 → 647 KB and 10 → 6 builds. The
same frames and accessibility trees. (An `AsyncLoader` constructor taking an
`IEqualityComparer<T>` would do this in the framework; it would be new public API.)

### Key each load on what can change it

One generation for everything an entity holds reloads everything on every change. Junction kept
one per repository, and its watcher bumped it for a file saved in the working tree, so an editor's
save read the log, the selected commit's files, the branches, the tags, the version and the
remotes again, none of which a change to the working tree can move: 14 git commands and 4.3 MB on
all threads an event over History. The watcher now says whether HEAD, a ref, the config or the
packed refs changed in its window. The loads of what those say key on a second generation
(`RefsGen`) that an event in the working tree or the index leaves alone, and the summary reads its
status again and keeps the rest (round 12, step 2). Over History an event now runs `git status`
and `git worktree list`, and makes 4,268 → 226 KB on all threads (−95 %) and 414 → 162 KB on the
UI thread, headless; over Changes, which shows the working tree, 19 → 11 commands and 5,236 →
1,611 KB; in a real window (NativeAOT) 4,502 → 236 KB and 647 → 172 KB an event. The same frames
and accessibility trees.

Four traps on the way, all in what a `FileSystemWatcher` on `.git` reports:

- Git writes HEAD, the config and the packed refs to a lock file and renames it over the old
  one. A watcher that listens for Changed and Created never hears them: a checkout that changed
  no file in the working tree, and a remote added, went unseen until something else changed.
- Git writes the index the same way, and not only to stage: `git status` (optional locks on,
  as outside the tests) replaces it to refresh the file times it keeps, after a checkout, a
  fresh copy or a file rewritten with the same bytes (not after an ordinary save: 0 of 8 in
  round 14). A watcher of the index's renames hears the app's own reads, reloads, and reads
  again; one that ignores them never hears staging from a terminal or an editor (`git add`,
  `restore --staged`, a staged submodule bump), which writes nothing else, and Changes stays
  stale (round 14: 3 of 53 outside operations). Junction's watcher hears the renames and reads
  the index when one comes: it tells the rename only when the entries changed (their paths,
  modes, object ids and flags), which a refresh of the file times never does. It also reads the
  index as it starts: the first rewrite is usually the app's own first `git diff` (which writes
  the index even with optional locks off) over a repository whose files are newer than its index,
  and with nothing to compare it with it was heard back as one more reload of every repository
  opened. The print read then is kept only when the file was written well before the watcher
  listened, so no change the app's first loads missed is taken as seen.
- A reftable repository (`git init --ref-format=reftable`, `git refs migrate`; git 3.0 plans it as
  the default for new repositories) keeps every ref, the branch HEAD names included, in
  `.git/reftable`. HEAD and `refs/heads` are stubs that never change, so a watcher of them heard a
  checkout, a reset or a pull made outside only through the working-tree files it changed, as a
  working-tree event, and the branch label, the branch list and the log kept what they had until
  a commit (round 13: 8 of 9 outside operations left the app stale). Every committed ref
  transaction writes a new table and renames `tables.list.lock` over `tables.list`; that rename
  is the ref moving, and the watcher now hears it in that folder.
- A ref transaction that changes nothing still takes the ref's lock: git creates `X.lock` and
  deletes it, and the folder that held it reports a Changed of its own. Every `git fetch` (git
  2.53 at least) does that for `refs/remotes/<remote>/HEAD`, so a fetch that brought nothing, the
  app's own background fetch included, read the log, the branches and the tags again 300 ms after
  the fetch's own check had decided not to: in a real window on a clone of this repository, 14 git
  commands and 4.5 MB on all threads a fetch, 2 and 0.17 MB once a lock or a folder alone was
  told as a working-tree event.
  Not dropped: a move that writes only a reflog entry (`merge --abort`'s reset, `reflog expire`)
  shows the watcher nothing else, so the reflog keys on the data generation (round 13; see
  docs/DECISIONS.md).

The working tree reports what git never shows, too. A build beside the app writes hundreds of
files under `bin\` and `obj\`, which git ignores, and every quiet gap of a debounce in it reloaded
the repository: a small project's build 2 reloads, 22 git commands, 4 frames and 3 MB over
Changes (round 14). Junction's watcher asks `git check-ignore --stdin` about a folder the first
time a window meets it, as `folder/` and without `--no-index`: git then says a folder holding a
tracked file is not ignored, so a file force-added under `obj\` is still heard. It keeps the
answers until a `.gitignore`, `.git/info/exclude`, the config or the index's entries change, and
drops an event under an ignored folder, and a folder's own change (a folder made in it changes
it; the folder's events say the rest). A first build now asks git once and reloads nothing, a
rebuild asks nothing: over Changes 2 reloads, 22 git commands, 4 frames and 3.0 MB a build → 0,
0–1, 0 and 0.13–0.26 MB, headless and in a real window (NativeAOT). Three traps:

- `check-ignore` stops at the first path inside a submodule ("Pathspec ... is in submodule") and
  answers nothing after it: ask again without it, and tell what is under it.
- It answers as it reads, so its output has to be read while the input is still being written,
  or a long question fills the pipe and both sides wait.
- A `.gitignore` under an ignored folder (`node_modules` holds many) changes nothing git shows;
  only one outside them is a rule changing. The global excludes file (`core.excludesFile`) is not
  watched: a change to it is seen at the next of the changes above.

### An event builds only the view whose loads it re-keys

A new generation re-keys loads; it needs a build only when something on screen keys a load on
it. Junction's watcher handler built the view after every event so the view's loads would see
the new keys, and most events re-key none: another repository's save (the overview reads only
summaries, which land by themselves and rebuild what reads them), and a save in the repository on
screen while History, Branches or Tags is shown, whose loads key on the refs. The generation
accessors now note which repository's generation a load of the view read since its last build
began, and the handler builds the view only when the event moved one of those, or a ref moved in
the repository on screen. Two readers note nothing: the loads the handler starts itself (the
summary and the worktrees, whose parts hear them land) and the preload, which warms views that
are not on screen.

What a view reads from disk without a load needs the handler's eye too. History shows the
issue-tracker file's rules, looked at once a build; a save of that file used to arrive with the
build every event made, and the handler now builds when the file's write time moved (git may
ignore the file, and then no summary moves either).

Per event (round 13): another repository's save 86.0 → 70.0 KB on the UI thread and 2 → 1
builds and frames headless, 91.1 → 71.9 KB in a real window (NativeAOT); a file touched there
18.4 → 2.4 KB and 1 → 0 builds and frames; a save over History 166.4 → 150.8 KB and 2 → 1
frames headless, 205.6 → 187.8 KB in a real window, a touch 21.5 → 1.9 KB and 1 → 0; Changes
unchanged. The same frames and accessibility trees.

A view whose re-keyed loads the handler can start itself needs no build at the event either: it
builds when they land, as the parts do for the summary. Junction's Changes view keys its
working-tree load and its diff's colours on the data generation, so an editor's save over it still
built the view at once. That build patched the 30 elements the view keeps with what they already
showed, and the window drew those pixels again a quarter of a second before the frame with the new
rows; with a file the app colours, that first frame drew the diff uncoloured, the colours landing
drew a second and the rows a third. The handler now starts both loads and builds nothing: the
view's last build read the working tree's loader, so the load landing builds the view. Their
generation is read through an accessor of its own (`ChangesGen`), so the handler can tell them
from a load it cannot start, which still builds the view at once (an image's diff, a conflict, the
reflog). The colours are asked for the diff on screen, as that build asked, so they are on their
way while git runs: left to the build that shows the new rows, those rows drew uncoloured and the
colours a frame later.

A save over Changes (round 14, NativeAOT): a text file 2 → 1 builds and frames, 184.7 → 137.6 KB
on the UI thread headless and 177.3 → 137.1 KB in a real window; a C# file 3 → 1, 393.2 → 154.0 KB
headless (the loads on the thread pool) and 375.1 → 149.3 KB in a real window. The last frame comes
when it did, and is what a full build draws.

### The app's own change, heard again

An app that watches what it also writes hears its own writes. Junction's actions (a checkout, a
commit, a pull, a branch or a tag made) end in `RefreshAll`, which reloads the repository, and
300 ms later the watcher told the same change and the repository was read a second time. Every
change the watcher has heard when `RefreshAll` runs was made before the loads it starts read the
repository, so those loads read it: `RefreshAll` tells the watcher to forget what it heard so
far (`GitWatcher.Forget`), its pending callback and one already posted to the UI thread
included, and everything heard after that is told as before. Nothing is paused: an outside
change made while the action ran is read by the action's own reload, one made after it is heard.
A fingerprint of the refs read before the reload would do the same for one or two git commands
an action, and has to be read before the reload starts, which delays it.

An in-app checkout, headless: 28 → 15 git commands (14 with no watcher at all), 987 → 747 KB on
the UI thread (−24 %), 1,975 → 1,288 KB on all threads (−35 %), 17 → 15 builds; the same on a
reftable repository. In a real window (NativeAOT, a clone of this repository): 28 → 17 git
commands, 1,740 → 1,215 KB on the UI thread and 10.6 → 6.5 MB on all threads a checkout.

### What a build shows of files comes from what was read already

A small read in `Build()` does not freeze anything a person would notice; it allocates on every
build, and a busy view builds often. Meridian's Runner counted the requests of the collection, of
each folder and of the chosen scope on every build of the view by reading and parsing every request
file, as a run does: 7.9 MB a build at 200 requests, and a run built the view once a result, 1.8 GB
a run of 200. The collection's scan had read every file already, and its tree holds each request;
the counts now come from the tree (36 KB a build). When the snapshot cannot give the old count
exactly, say what the new one counts: a request file that cannot be read is counted now, and the run
still leaves it out.

What must be read can be read once a snapshot. The Chain sub-tab read and parsed every request on
each build of the editor, a key in the URL too: 2.9 MB a key at 200 requests, where the Headers
sub-tab's key is 8.6 KB. It now keeps each request's node with the scan it was read for, compared by
reference, and lets them all go when a new scan takes its place. A snapshot that is a new object on
every change makes the check one comparison, and nothing kept is older than the tree beside it:
every change to a request file scans the collection again (the watcher, a save, a rename).

```csharp
private CollectionTree? _nodesOf;   // the scan the nodes were read for
private readonly Dictionary<string, Chain.Node> _nodes = new(StringComparer.OrdinalIgnoreCase);

private List<Chain.Node> AnalyzeAllRequests()
{
    if (!ReferenceEquals(_nodesOf, _tree)) { _nodes.Clear(); _nodesOf = _tree; }
    // each request: its open tab's document, else _nodes[path], else read, parse and keep
}
```

A key in the URL with the Chain sub-tab shown, 200 requests in one chain (headless, NativeAOT):
3,293 → 117 KB, 8.4 → 1.4 ms, the same pixels.

---

## Key Imports

| Namespace | What it provides |
|---|---|
| `HultUI.Core.Reactivity` | `Signal<T>`, `Computed<T>`, `Effect`, `ReactiveRuntime`, `[Reactive]` |
| `HultUI.Core.Components` | `Component`, `ComponentHost` |
| `HultUI.Core.Builders` | `Flex`, `Box`, `Text`, and other element builders |
| `HultUI` | `Application`, `Window`, `AsyncLoader<T>` |
