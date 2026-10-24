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

Both wake the loop. Between frames the application parks in the platform's wait
until the next deadline the loop owns — an animation's next step, the trailing
edge of a coalesced accessibility refresh, a throttled resize, the DevTools
inspector's refresh, a display-colour poll of a Vulkan window without
display-colour events — or, with none, until something arrives. Posting from
another thread ends that wait through `IWindowingPlatform.Wake`, so the work runs
within a frame of being posted. Posting from the UI thread itself does not wake
anything while the loop runs that code: it is drained in the same pass.

The loop used to park 16 ms at a time: about 60 passes a second in every idle
window (37–38 minimized), each looking again at whatever had been set aside for
later. An idle window now passes once and waits (measured in plan 17, round 3).
What that changes for an application:

- **A flag set off the UI thread is not seen until the next wake.** Hand results
  to the UI thread with `InvokeOnMainThread` or an `await`, not by writing a field
  that a `Build` or a frame reads; the loop no longer comes round to read it.
- **Something set aside for later must name when it is due.** A timer is a tween
  with a delay (`Animate.Value(0f, 1f, _ => { }).Delay(s).Duration(0f).OnComplete(…)`):
  the loop wakes once for it. A tween with a `Duration` and a setter that changes
  nothing steps every frame interval until it ends (see
  [ANIMATIONS.md](ANIMATIONS.md#when-a-tween-steps)).
- **A callback the wait itself runs on the UI thread** — a WebView2 event, an OLE
  drop, a WinRT `DispatcherQueue` item, a `WM_GETOBJECT` from a screen reader — comes
  with no event of SDL's to end the wait. What it changes wakes the loop anyway:
  every element and signal mutation, a tween started, a post-frame callback and a
  post made while the loop is parked wake it (`UIThread` sees the loop parked). A
  native callback that only sets a flag of its own must call
  `Application.InvokeOnMainThread`, which then wakes it too.

`HULTUI_FRAMELOG` (see [ENVIRONMENT.md](ENVIRONMENT.md)) prints, among the rest,
what each idle wait waited for and what woke the loop.

Rendering is paced to the display (`Window.RefreshRate`, from the platform; 60 Hz
when unknown). The swap interval is 0, because a vsync'd swap blocks the modal
resize loop, so the loop itself holds a dirty window back until one interval has
passed since the last render. Meanwhile it parks in the platform's wait for what is
left of the interval: an animating window costs two or three passes of the loop a
frame. (On Windows SDL used to cut every such wait a millisecond short and a 1 ms
wait to nothing, so the loop spun through the end of every interval: ~240 passes a
frame at 200 Hz.) Events are pumped and handled on every iteration
regardless, so a 1000 Hz mouse updates hover a thousand times a second and is
drawn at the display's rate; a posted video frame is drawn on the next interval.
The DevTools HUD shows the measured rate next to the display's, and
`FramePacingTests` (real window) holds a tween to it, with and without an event
every millisecond. An application can opt out with
`app.FramePacing = FramePacing.Unlimited`, which renders whenever anything is
dirty, as fast as the GPU returns; the DevTools continuous-redraw mode (F12
twice) does the same on its own, so the throughput figure it exists to show is
not capped. Neither touches the swap interval: this is the loop's pacing, not
the driver's vsync.

**A minimized window is not drawn.** Its frame runs up to the drawing — actions
from assistive technology, style, layout and the accessibility tree stay current
— and stops there with the paint dirt kept, so the first frame after the restore
draws what changed; the restore's own event wakes the loop. Meanwhile the loop
parks as an idle one does, and does not come round for the window at all until the
restore or until something else arrives. Before, GL drew every frame
of an animation or a video nobody could see, and a Vulkan window, whose minimized
surface on Windows has no area, retried a frame every display interval (220 to
400 passes a second at 200 Hz). So a `Window.RequestPostFrameCallback` made
while a window is minimized runs after the restore, not on the next pass, and
nothing that must keep going while minimized should be driven by frames. A
forced frame, `RunFrame(forceRender: true)`, still draws. The minimized state is
SDL's, which the real-window test checks on Windows only.

A video in a minimized window keeps playing, because the window still serves its GPU
image sources without drawing them (`IGpuImageSource.ServiceUndrawn`): on each pass
it is dirty, it takes the frame mpv has waiting (`MPV_RENDER_PARAM_SKIP_RENDERING`)
and lets a new player set up as a draw would. Without that, mpv, whose render thread
is the UI thread, waited 200 ms for every frame and dropped it, and a player opened
while minimized never loaded: in Reel the next file in a folder stayed silent until
the window was restored.

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

Inside Win32's modal size/move loop (a window dragged by its edge or its title
bar, or held still mid-drag) a post waits for the loop to end. So a GPU source's
repaint request (`MediaPlayer`'s, `WebViewController`'s) is also answered from
inside the loop: the request posts the window a message, a `PostMessage`, which
never waits and so is safe from the decoder's own thread; the loop dispatches it
and the window draws. A 120 fps clip shows its 120 frames a second while its
window is moved or a drag is held, on GL and Vulkan (before: about 64, SDL's
live-resize timer, and mpv dropped half). Not within 20 ms of a new size, whose
own frame took the newest picture.

The converse holds too: a frame must not wait for a thread that is waiting for
that frame. mpv's core waits, for up to 200 ms after every load and seek, for the
UI thread to draw the first frame. So the mpv backend never makes a synchronous
libmpv call on the UI thread, and it takes a frame it cannot size yet rather than
leaving it. Each of those used to wait out mpv's timeout. mpv's render API states
the rule under "Threading" in `render.h`.

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
