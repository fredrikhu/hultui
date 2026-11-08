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

A field holds the text typed into it, so a key needs a build only for what else on screen reads
that text. On the Headers, Body and Auth sub-tabs the URL is read only by the Params and Chain
counts: a URL character compares the two with what the last build showed, and builds nothing when
neither moved and the request was already unsaved (round 7, step 10: 66.6 → 4.0 KB a character
and −17 % time on NativeAOT, −27 % on JIT; a query typed out −83 %).

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
why the grid's cursor never had the keyboard. Aperture's grid, 600 films, 1440×900, a key and three
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

A column whose cells are all kept is left with no children by the time the reconciler reaches
it, since the build moved every one into its new tree. Until round 5 those came back one by one,
each marking the column's structure, so with a reader listening every key built the whole
accessibility tree; now they come back as they were and mark nothing (Brief with a reader, 400
files: 300.6 → 230.2 KB a key, 1.00 → 0 trees; Reel's player, alone in its container, 5.8 → 2.0
KB a build). The same elements in the same order is the rule: a column handed back in another
order, or with a new cell, marks its structure as any change does.

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

---

## Key Imports

| Namespace | What it provides |
|---|---|
| `HultUI.Core.Reactivity` | `Signal<T>`, `Computed<T>`, `Effect`, `ReactiveRuntime`, `[Reactive]` |
| `HultUI.Core.Components` | `Component`, `ComponentHost` |
| `HultUI.Core.Builders` | `Flex`, `Box`, `Text`, and other element builders |
| `HultUI` | `Application`, `Window`, `AsyncLoader<T>` |
