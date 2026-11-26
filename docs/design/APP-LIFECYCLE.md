# App lifecycle — design note

**Status (2026-11-25): design approved and built, on branch `claude/android-spike`.** The user
approved §3's API as proposed: the names `EnteringBackground` and `EnteredForeground`, media
pausing by default, and `LifecycleState` on every platform (§6). What was built, how it was
checked, and what is left are in §8. Plan 20, phase 2 (`docs/plans/20-mobile-support.md`).
Written from what the Android spike measured (`spikes/AndroidHello/README.md`, branch
`claude/android-spike`). Where something was not measured, this note says so.

A phone app is put in the background, brought back, rotated and squeezed for memory, and may be
killed in the background without another word. Today HultUI hears none of this. On the desktop
there was nothing to hear: a minimized window is still a running app.

## 1. What the spike showed

These are SDL's events, logged by an event watch, on an API 35 emulator:

| What happened | SDL events, in order | Thread that pushed them |
|---|---|---|
| Home | `WINDOW_FOCUS_LOST`, `WINDOW_MINIMIZED`, `WILL_ENTER_BACKGROUND`, `DID_ENTER_BACKGROUND`, `LOW_MEMORY` | the focus loss on Android's UI thread, the rest on SDL's thread (HultUI's UI thread) |
| Back to the app | `WILL_ENTER_FOREGROUND`, `DID_ENTER_FOREGROUND`, `WINDOW_FOCUS_GAINED`, `WINDOW_RESTORED` | SDL's thread |
| Rotation | `WINDOW_RESIZED`, `WINDOW_PIXEL_SIZE_CHANGED`, `WINDOW_SAFE_AREA_CHANGED` | SDL's thread |

- **The background is already quiet, by accident.** SDL marks the window
  `SDL_WINDOW_MINIMIZED` in the background, and HultUI draws nothing for a minimized window
  (`Application`, `IsMinimized` read live from SDL's flags). Android also blocks SDL's event
  pump while paused (`SDL_HINT_ANDROID_BLOCK_ON_PAUSE`, on by default).
- **The first frame after a surface change is lost.** Android destroys the surface on Home and
  makes a new one on return. The first frame HultUI draws after `DID_ENTER_FOREGROUND` never
  shows, on GL ES and on the software renderer alike. An app with nothing to redraw stays black.
  One more frame, drawn 16 ms or 100 ms later, shows correctly (drawn pixel samples 0 → 64,991).
  Rotation does the same: the first frame at the new size shows stale, and the next is right.
  **Why** the first frame is lost was not established. The likely reason is that it is drawn
  before Android attaches the new surface's buffers. The fix below does not depend on knowing.
- **The GL context survived** background and return on the emulator. Binding it again changed
  nothing. Context loss was not seen, but EGL allows it, and devices differ.
- **`LOW_MEMORY` arrives on every Home.** It is Android's `onTrimMemory`, which SDL forwards at
  any level, so it means "the system would like memory back", not "you are about to die".
- **HultUI's UI thread is stopped in the background.** With `SDL_HINT_ANDROID_BLOCK_ON_PAUSE`
  on (SDL's default), SDL parks its thread, HultUI's UI thread, until the app returns. The
  spike's 3-second timer posts to the UI thread. The tick due 1.4 s into the background (at
  3.16 s) ran only at the moment of return (5.12 s). Posted work, `await` continuations and
  timers on the UI thread all wait out the background.
- **When SDL pauses.** In the log, SDL paused (`nativePause`) when the surface was destroyed,
  before `onStop`, not at `onPause`. SDL's activity pauses on `onPause` only on Android older
  than 7 (multi-window). So a paused activity that keeps its surface is not "background" to
  SDL. That is what picture-in-picture is (§7). Picture-in-picture itself was not tried.

## 2. What HultUI does, with no new API

All of this is internal to `HultUI.Platform.SDL3` and `HultUI`. It changes nothing on Windows or
Linux, where SDL sends none of these events.

1. **A platform capability, like the existing ones.** The SDL platform hears the app events and
   passes them on through a capability interface, as `IMonitorSource` and
   `IDesktopSettingsSource` carry monitor and theme changes today (`Application.cs:484-485`).
   The headless platform implements the same interface, so tests drive the same code. (The
   interface is public, as those two are, so it is listed in §3.)
2. **A background gate.** From `WILL_ENTER_BACKGROUND` until `DID_ENTER_FOREGROUND`, the
   application draws, presents and uploads nothing, for every window, whatever SDL's minimized
   flag says. On Android the flag covers this today. On iOS, GPU work in the background gets the
   app killed, and the gate should not depend on a flag SDL sets for another reason. Input
   cannot arrive in the background. Tweens keep their wall clock and land where they would have.
3. **A settle redraw after every surface change.** On `DID_ENTER_FOREGROUND`, on
   `WINDOW_RESTORED`, and on `PIXEL_SIZE_CHANGED` (only on a platform that reports a lifecycle),
   the window marks everything dirty and draws two frames: one at once and one on the next frame
   interval. This is the spike's measured fix, made structural. It costs one extra frame per
   return or rotation. On the desktop it never runs.
4. **Losing the GPU, by design but untested.** If `SDL_GL_MakeCurrent` fails after a return, or
   Skia reports its context abandoned, the window drops its render surface and builds it again
   through the existing `TryCreateGLRenderer` path. That path already falls back to software if
   GL will not start. Pictures made from pixels upload again on their next draw. Frames made on
   the GPU (video, web views) come again from their producers. This needs a device that loses
   its context, or a fault injection like the Vulkan one (`docs/ENVIRONMENT.md`), before it can
   be called done.
5. **Low memory: HultUI empties its own caches first.**
   - `GRContext.PurgeUnlockedResources(scratchResourcesOnly: false)` on GL and Vulkan. Today only
     Vulkan purges, after 30 s at rest (`RestPurge`).
   - The word cache (up to 16 MB), the glyph and image caches.
   - Then one compacting collection.

   It then raises the app's event (§3), so the app can drop what only it knows about, such as
   Aperture's posters. The cost is the next frame's uploads, measured on the desktop at about
   1 ms for a poster grid (`RestPurge`'s remarks).
6. **Events on the right thread, in time.** SDL's README asks iOS apps to handle these events in
   an event watch (`SDL_AddEventWatch`), not from the queue: the system may suspend the app
   before the queue is read. `SDL3Window` already has a watch (`EventWatchCallback`, for live
   resize). The spike showed the watch also sees events pushed from Android's UI thread
   (`FOCUS_LOST`). So the watch acts only on app events pushed on HultUI's UI thread, which on
   Android is SDL's thread and on iOS the main thread. It runs the handlers synchronously, then
   returns. Events pushed from other threads go through the queue as now.
7. **The Android activity's part** (in the future `HultUI.Platform.Android`, plan 20 §6 item 4):
   - turn off the default focus highlight on SDL's views (spike finding 7)
   - leave the back key to its own note
8. **SDL must not stop the UI thread: `SDL_HINT_ANDROID_BLOCK_ON_PAUSE=0`.** The background
   gate (item 2) is what keeps HultUI off the GPU, so SDL's own block is not needed for that. The
   block would stop the UI thread, and with it:
   - network completions and timers, so Aperture's requests would finish only on return
   - the media layer's events
   - later, audio with the screen locked (§7)

   An idle HultUI loop parks in `WaitForEvents` and costs nothing, so a running UI thread in the
   background is not a battery cost by itself.

   **Verified 2026-11-25** with the spike's `scripts/background-check.ps1`. A 1 s ticker posts
   to the UI thread and changes the tree each time; about 7.4 s in the background each run:

   | Run | Ticks run while away | Frames drawn while away | GL/runtime error lines | Drawn after return |
   |---|---|---|---|---|
   | Home, SDL's block on (its default) | 0 | none (the frame log stops too) | 0 | yes |
   | Home, `BLOCK_ON_PAUSE=0` | 7 | 0.0/s | 0 | yes |
   | Screen locked, `BLOCK_ON_PAUSE=0` | 7 | 0.0/s | 0 | yes |

   The frame log while away, block off: `passes/s=1.0–1.4 frames/s=0.0 parked-ms/s=1000
   posts/s=0.9–1.0 minimized=1`. The loop sleeps between posts, runs them, and draws nothing:
   today's minimized check already keeps it off the GPU, and the gate (item 2) will make that
   independent of SDL's flag.

   SDL's companion hint that pauses SDL's own audio does not matter here: mpv has its own
   audio output.

## 3. Public API proposal (needs the user's OK)

On `HultUI.Application`:

```csharp
/// <summary>Whether the app is on screen. Always Foreground on Windows and Linux, where
/// minimizing a window is not leaving the app.</summary>
public AppLifecycleState LifecycleState { get; }

/// <summary>Raised on the UI thread, synchronously, as the app leaves the screen. The last
/// moment the app can count on: Android may end the process later without telling it, and iOS
/// gives about five seconds. Save state here, quickly. Nothing is drawn until EnteredForeground.</summary>
public event Action? EnteringBackground;

/// <summary>Raised on the UI thread when the app is back on screen, before its first frame.</summary>
public event Action? EnteredForeground;

/// <summary>Raised on the UI thread when the system asks for memory back, after HultUI has
/// emptied its own caches. Drop what can be loaded again.</summary>
public event Action? LowMemory;

/// <summary>Raised when the system ends the app, where it says so (iOS does, Android rarely).
/// Best effort: do not rely on it to save; save in EnteringBackground.</summary>
public event Action? Terminating;
```

```csharp
namespace HultUI.Core.Platform;

/// <summary>Where the app is. More states may come (§7), so a switch over it needs a default.</summary>
public enum AppLifecycleState { Foreground, Background }

/// <summary>A platform that tells the application when it leaves and returns to the screen,
/// and when the system wants memory. SDL on Android and iOS, and the headless platform.</summary>
public interface IAppLifecycleSource
{
    AppLifecycleState LifecycleState { get; }
    event Action? EnteringBackground;
    event Action? EnteredForeground;
    event Action? LowMemory;
    event Action? Terminating;
}
```

On `HultUI.Headless.HeadlessSession`, so tests drive the real code paths on virtual time:

```csharp
public void EnterBackground();   // raises EnteringBackground; frames stop
public void EnterForeground();   // raises EnteredForeground; the settle redraw runs
public void SignalLowMemory();   // purges, then raises LowMemory
```

**Why plain events, not a signal.** `LifecycleState` changes twice per trip to the background,
and nothing should rebuild a tree in the background. Code that wants it in a build can read it
there. A signal would wake builds for frames that are not drawn. `Window.Activated` and
`Deactivated` already cover "on screen but not focused" (the notification shade), so two states
are enough.

**What these events are not.** They are not raised for a minimized desktop window, which keeps
running, playing and downloading. That is `Window.Deactivated` and the window's minimized state.
An app that wants to save on the desktop too saves in `Window.CloseRequested`, as today.

## 4. Tests

- **Headless:**
  - no frame drawn or presented while in the background, even with tweens running and posted
    work arriving, while the posted work itself still runs there (§2, item 8)
  - two frames after `EnterForeground`, the whole window both times
  - event order, and handlers running on the UI thread
  - after `SignalLowMemory`, the caches empty (counters the caches already keep), then the event
  - the desktop platform never raising any of them
- **Device**, with the spike's `scripts/resume-check.ps1`, run in phase 2's emulator job:
  - drawn samples after return within a few of before
  - rotation drawing the right size within one frame interval
  - 20 trips to the background with no growth in `dumpsys meminfo` graphics
- **Not covered until a device does it:** context loss (§2, item 4).

## 5. Size and order

About a week for §2 items 1–3 and 5 with headless tests (S/M). Item 4 (context loss) takes days
more and needs a way to provoke it. §3 is small once its shape is agreed.

## 6. Decisions (2026-11-25, the user took every recommendation)

1. **The API in §3, as written.** `EnteringBackground` and `EnteredForeground`, not WinUI's
   `Suspending` and `Resumed`: they say what happens on every platform.
2. **`HultUI.Media` pauses playback when the app enters the background**, unless the app opts in
   to background audio. That opt-in is its own API item, later (§7).
3. **`Application.LifecycleState` is there on every platform**, always `Foreground` on the
   desktop, so app code needs no platform checks.

## 7. Later: picture-in-picture and playing with the screen locked

The user wants both, much later: a floating video window like YouTube's, and audio that goes on
with the screen locked. Neither is built now. This section checks they fit what is built now.

**Picture-in-picture fits the design.** Android shows a picture-in-picture activity paused but
not stopped, and its surface lives on. By the log order in §1, SDL does not treat that as
background. So the app stays `Foreground` and goes on drawing, now at the floating window's
size, which is an ordinary resize with the settle redraw (§2, item 3). It needs, then:
- `supportsPictureInPicture` in the manifest; the spike's `ConfigurationChanges` already list
  the size changes it causes
- a way to enter picture-in-picture
- its playback controls (Android's remote actions)
- probably a `PictureInPicture` member of `AppLifecycleState`, or a window property, so the
  app shows only the video

That member is why the enum says more states may come. Not tried in the spike.

**Playing with the screen locked changes one thing now: SDL must not stop the UI thread**
(§2, item 8). A locked screen stops the activity and destroys its surface, exactly like Home,
so the app is in `Background` and draws nothing. Audio can go on only if:
- **The UI thread runs.** That is item 8, and the reason it is in this design rather than later.
- **mpv is told to stop video** and keep audio. libmpv's render API waits for the app to draw
  frames, and in the background nothing draws them. On `EnteringBackground`, a player that plays
  in the background switches to audio only (mpv's `vid=no`). On `EnteredForeground` it switches
  video back on. Both happen inside `HultUI.Media`, in handlers of this design's events, before
  the gate closes. That is the order §2 already gives.
- **Android allows it.** That takes a foreground service of type `mediaPlayback` with its
  notification, and a media session for the lock screen and headset buttons. This is platform
  glue for `HultUI.Platform.Android` and API for the opt-in. Both are later items.

Nothing else in §2 or §3 needs to change for either feature.

## 8. Built (2026-11-25)

**What is in:**
- **The API of §3:**
  - `AppLifecycleState` and `IAppLifecycleSource` (`src/HultUI.Core/Platform/AppLifecycle.cs`)
  - `Application.LifecycleState`, `EnteringBackground`, `EnteredForeground`, `LowMemory` and
    `Terminating`
  - the headless `EnterBackground`, `EnterForeground` and `SignalLowMemory`
- **SDL (§2 items 1, 6, 8).** `SdlWindowingPlatform` implements the interface:
  - an event watch handles the four app events synchronously when it runs on the UI thread
  - the queue brings the rest, de-duplicated by timestamp
  - an app handler's exception is thrown from `PumpEvents`, never across SDL's C frames
  - `SDL3Window` turns `SDL_HINT_ANDROID_BLOCK_ON_PAUSE` off on Android
- **The background gate (§2 item 2).** One check, `Application.InBackground`, beside the
  minimized check:
  - in `Window.RenderFrame`, forced or not
  - in `RenderDirtyWindows` and `AnyWindowNeedsRender`
  - in the GL driver-walk wait
  - in `NextDeadline`, so a running tween does not wake the loop in the background
- **The settle redraw (§2 item 3).** `Window.SettleRedraw` runs after
  `EnteredForeground`, and after every resize where `Window.SurfaceLosesFirstFrame` (Android,
  iOS). It draws the whole window, then once more in the next frame interval.
- **Low memory (§2 item 5):**
  - `TextPipeline.TrimCaches` (with a new `WordCache.Clear`)
  - Skia's `PurgeUnlockedResources` on the GL and Vulkan surfaces, while the app is on screen
  - then the app's event, then an aggressive compacting collection
- **Media (§6 item 2).** `MediaPlayer` pauses on `EnteringBackground` and drops a pending
  `autoPlay`.

**Tests:**
- **`AppLifecycleTests`** (headless, 9 tests):
  - nothing drawn while posted work runs in the background
  - a forced frame refused there
  - the two settle frames after a return and after a resize
  - the text caches emptied before `LowMemory` is told
- **`MediaPlayerLifecycleTests`** (3 tests).
- **Two negative controls** fail the tests they should: the gate off fails the two drawing
  tests, and a one-frame settle fails the two settle tests.
- **The suites:**
  - Headless 1,969
  - Core 5,322
  - Text 324
  - Media 96
  - Rendering.Skia 488
  - Platform.SDL3 92

**On the emulator** (API 35, the spike with its own workarounds switched off:
`-e rebind 0 -e ticker 0`, so the app changes nothing on return):

| Check | Before | Now |
|---|---|---|
| Drawn samples just after returning from Home, GL | 0 | 65,047 (65,047 before Home) |
| The same, software renderer | 0 | 65,047 |
| Landscape frame after a rotation | 55 % black (stale) | 1 % black, the right frame |
| Ticks run in 7.4 s in the background, Home / screen lock | 0 / not measured | 7 / 7, frames 0.0/s, no errors |

**Left for later:**
- **GPU caches in the background.** On a low-memory signal while the app is away, only the CPU
  caches are emptied. Making the GL context current without a surface needs a device to prove
  it safe (EGL's surfaceless contexts).
- **Losing the GPU (§2 item 4).** Not built: no device has lost its context yet. It needs a
  fault injection first.
- **The render context's blob and glyph maps** are not trimmed yet. Only the text pipeline's
  caches and Skia's GPU resources are.
- **A minimized frame still serves video sources** (`ServiceUndrawnSources`). A player that
  plays in the background must switch mpv to audio only first, as §7 says.
- **`Terminating`** comes only from SDL. The headless session cannot raise it.
- **The settle on iOS** is assumed, not measured.

**On a real phone (2026-11-25).** A Galaxy S25, Android 17, Adreno 830, release build:
- **Return from Home:** drawn just after return, with the app changing nothing (64,518 samples
  before and after). So the settle redraw holds on a real driver.
- **In the background:** 7 ticks in 7.7 s away with no frame drawn and no error.
- **60 trips:** GPU memory 59.3 MB before and 40.7 MB after, PSS without growth.
