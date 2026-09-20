# Threading model

**One thread owns the element tree and the reactive graph.** HultUI takes no
locks anywhere in its hot paths, and that is a deliberate consequence of this
rule rather than an oversight — see
[DECISIONS.md](DECISIONS.md). Everything below follows from it.

## The rule

Build elements, read and write `[Reactive]` properties and `Signal<T>`, mutate
element properties, and touch a `Window` — all on the UI thread. That is the
thread that constructed the `Application`, and the one its frame loop runs on.

Everything else — file I/O, HTTP, git, parsing, whatever your app does that
takes milliseconds — belongs on a background thread, and its *results* come back
to the UI thread before they touch any state.

## Getting back onto the UI thread

Two mechanisms, and most code needs neither explicitly:

**`await` already does it.** The application installs a
`SynchronizationContext` on the UI thread, so any `await` reached from UI-thread
code resumes on the UI thread, on the next frame. This is why the ordinary shape
just works:

```csharp
async Task LoadAsync()
{
    var text = await File.ReadAllTextAsync(path);   // runs off-thread
    Content = text;                                 // back on the UI thread
}
```

The exception is `ConfigureAwait(false)`, which explicitly opts out of that
resumption — correct inside library I/O, wrong immediately before touching UI
state.

**`Application.InvokeOnMainThread(action)`** queues work to run on the next
frame, for callbacks that never had a UI-thread context to return to: a
filesystem watcher, a native callback, a `Task.Run` continuation, a timer from
another library.

Both wake the loop. Between frames the application parks in the platform's idle
wait (16 ms at a time); posting from another thread cuts that wait short through
`IWindowingPlatform.Wake`, so the work runs within a frame of being posted rather
than after the wait times out. Posting from the UI thread itself does not wake
anything — the loop is running that code, not parked.

Rendering is paced to the display (`Window.RefreshRate`, from the platform; 60 Hz
when unknown). The swap interval is 0, because a vsync'd swap blocks the modal
resize loop, so the loop itself holds a dirty window back until one interval has
passed since the last render. Events are pumped and handled on every iteration
regardless, so a 1000 Hz mouse updates hover a thousand times a second and is
drawn at the display's rate; a posted video frame is drawn on the next interval.
The DevTools HUD shows the measured rate next to the display's, and
`FramePacingTests` (real window) holds a tween to it, with and without an event
every millisecond.

```csharp
watcher.Changed += (_, e) => Application.InvokeOnMainThread(() => Files = Reload());
```

One thread must not post: a thread the UI thread waits for during a frame. On
Windows a frame can render inside SDL's event watcher (that is how the window
repaints during a live resize, and `Maximize`/`Restore` resize synchronously,
so the watcher fires under whoever called them). SDL holds its watcher lock for
the duration, and `Wake` is an `SDL_PushEvent`, which waits for that lock. If the
frame then waits for a thread that is inside `InvokeOnMainThread`, both wait
forever. A native decoder whose render call waits for its own callback thread is
the known case (`HultUI.Media` hops such requests through the thread pool); a
plain producer that only publishes and posts is fine, because nothing in a frame
waits for it.

## What happens when you get it wrong

Nothing visible, at first — which is the problem. A background write to a
signal does not throw; it corrupts the reactive graph or the dirty-flag walk,
and the symptom appears frames later somewhere unrelated.

`HultUI.Core.Threading.UIThread` turns that class of bug into an immediate
exception that names the operation and both threads:

```
Signal<T>.Value was called from a thread-pool thread (managed thread 12);
HultUI owns the element tree and the reactive graph on the UI thread (managed
thread 1) and takes no locks, so this would corrupt state silently rather than
fail here. Marshal the mutation back with Application.InvokeOnMainThread(...),
or drop the ConfigureAwait(false) so the continuation resumes on the UI thread.
```

The guard is **on in Debug builds and off in Release**. It costs one static
bool read when off, and ~0.65 ns when on — below the framework benchmark's noise
floor, so `UIThread.Enforce = true` in a Release diagnostic build is a supported
way to chase a production-only report.

It is also *inert until an application binds a thread*: `UIThread.Bind()` is
called by `Application`'s constructor and `Unbind()` on disposal. Pure layout or
reactivity unit tests that never start an application are never checked, because
there is no UI thread for them to violate.

## Hosting the tree yourself

A custom pump that owns the element tree without an `Application` should call
`UIThread.Bind()` on its own thread before building any UI, and `Unbind()` when
it tears down — otherwise the guard stays inert and cross-thread mutations go
back to being silent.

## Testing

`HeadlessSession` runs the whole framework on the calling thread with a virtual
clock, so tests are single-threaded by construction and deterministic. Work that
would normally hop to the thread pool is queued and drained by `PumpFrame`
instead, which is what makes async loading testable at all — see
[HEADLESS.md](HEADLESS.md).
