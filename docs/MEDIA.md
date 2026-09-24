# Video Playback

How to put a video in a HultUI app. The design and the reasons behind it are in
[design/MEDIA.md](design/MEDIA.md); this page is the consumer's view.

```csharp
using HultUI.Media;          // MediaPlayer, MediaSource, Video, VideoPlayer, VideoPlayerTheme
using HultUI.Media.Mpv;      // MpvBackend
using HultUI.Media.Testing;  // FakeMediaBackend, TestClip (ships inside HultUI.Media)
```

## Packages

| Package | What it is |
|---|---|
| `HultUI.Media` | The backend contract, the reactive `MediaPlayer`, `VideoElement`, the `VideoPlayer` overlay component, and `FakeMediaBackend` for tests. **No decoder.** |
| `HultUI.Media.Mpv` | The libmpv backend: bindings and `MpvBackend`. **No native library**; it loads whatever libmpv it finds (below). |
| `HultUI.Media.Mpv.Natives` | A trimmed LGPL libmpv for `win-x64` and `linux-x64` under `runtimes/<rid>/native/`. Built by the `mpv natives` workflow, not part of the solution. No macOS build yet. |

An app that never plays video references none of these and pays nothing.

## The shape

```csharp
var player = new MediaPlayer(new MpvBackend());       // one per video, on the UI thread
player.Open(MediaSource.FromFile(path), autoPlay: true);

// in Build():
var view = Video.New(player).FlexGrow(1);              // a VideoElement: an ImageElement fed by the player
// or the whole overlay — transport bar, keys, auto-hide:
var ui = new VideoPlayer(player) { JumpStep = TimeSpan.FromSeconds(10) };
ui.FullscreenRequested += () => {                      // full screen is the app's to grant
    window.ToggleFullscreen();                        // borderless, no mode change
    ui.IsFullscreen = window.IsFullscreen;            // the overlay draws off its own flag
};
```

`MediaPlayer` is signal-backed: `State`, `Position`, `Duration`, `VideoSize`,
`Buffering`, `Error`, the track lists and `IsPlaying` / `HasMedia` / `HasVideo` are all
tracked when read inside a `Build()`. **State is the backend's word**: `Play()` does not
set `State` to `Playing`; the backend does when it is. The exceptions are the settings a
backend does not echo — `Rate`, `Volume`, `Muted` — and `Position` on a `Seek`, which
moves at once so a scrubber lands where it was dropped. Every setter and command must be
called on the UI thread.

**Scrubber previews.** `VideoPlayer.PreviewFrame` is a `Func<TimeSpan, VideoPreviewFrame?>`
the app supplies; the player asks it for the position under the pointer while the scrubber
is hovered or dragged and floats the still above the bar, with the frame's own time under
it. A `VideoPreviewFrame` carries an `IImageSource`, an optional `Source` rectangle and an
optional `Time`. The rectangle is what makes a server's thumbnails usable: they arrive as
sprite sheets — a grid of small frames in one JPEG — and `ImageElement.SourceRect` draws one
cell of it without cropping a bitmap per hovered second. The `Time` is the still's own
position, rounded to whatever interval the sheet was made at, because a still every ten
seconds cannot honestly be labelled 4:57. Answer from a cache and start the fetch on a
miss: the provider is called during a build, so it must not block, and returning null
simply means no preview this frame.

**Naming a track.** A backend builds `MediaTrack.Title` through
`MediaTrack.Describe(title, language, id)`: the language name leads and the file's own
title follows — "English · Stereo", "Japanese · Stereo". Either half alone is enough,
and a file that says nothing leaves "Track 3". Do not name a track by its embedded title
alone: a dubbed release is routinely muxed with the same title on every audio track, so
the menu ends up offering the same word twice and the one field that tells them apart is
the one that got dropped. `MediaTrack.LanguageName` maps the ISO 639 code, including the
639-2/B forms Matroska tends to carry (`ger`, `fre`) which the runtime does not know.

`MediaSource.FromFile(path)`, `FromUri(Uri)`, `FromUri(string)`, `FromStream(stream,
displayName)`. The string form keeps its `Url` verbatim even when .NET cannot parse it
(mpv's `av://lavfi:testsrc` is one), so a backend gets what you typed. The mpv backend
plays files and URLs, not streams.

## Traps

**`Play()` straight after `Open()` is a no-op with mpv.** A backend loads
asynchronously, and the mpv backend ignores `Play`, `Pause`, `Stop` and `Seek` until
the file is loaded. Pass `Open(source, autoPlay: true)` instead: the player waits for
the load and then plays. `FakeMediaBackend` loads synchronously, so a test that calls
`Play()` after `Open()` passes and the app built the same way does not.

**The video does not load until it has been drawn once.** With `vo=libmpv` a file
cannot start without a render context, and the kind of context is only known at the
first draw. `VideoElement` renders even before the first frame for exactly this reason;
a player whose element is never mounted stays in `Opening`.

**A plain property on a component does not rebuild.** `VideoPlayer.JumpStep` is
signal-backed because the transport buttons carry it in their accessible names
("Back 10 s"); anything your own `Build()` reads must be a signal too. See
[REACTIVITY.md](REACTIVITY.md#lifecycle-hooks).

**A thread the frame waits for must never post.** The mpv GL path renders inside the
frame and waits on mpv's own thread; if that thread called `InvokeOnMainThread` (which
wakes the loop through `SDL_PushEvent`) during a live resize, the two would wait on each
other forever. The backend hops its repaint request through the thread pool for this
reason. A backend of your own must do the same; a plain producer that only publishes
frames and posts is fine, because nothing in a frame waits for it. See
[THREADING.md](THREADING.md).

**`Window.SetRoot` does not unmount the old tree.** A `VideoElement` in a swapped-out
root keeps listening for frames until it is collected. Rebuild a component instead of
swapping roots, or dispose the player.

## `VideoPlayer`

The overlay: a transport bar (play, back/forward by `JumpStep`, scrubber, volume, rate
menu, full-screen button) that appears on pointer movement and fades out.

| Member | Default | Meaning |
|---|---|---|
| `AutoHideControls`, `AutoHideDelay` | `true`, 2.5 s | the bar hides while playing and the pointer is still |
| `FadeDuration` | 300 ms | fade-out; fade-in is half of it |
| `SeekStep` | 5 s | Left / Right |
| `JumpStep` | 10 s | J / L and the two buttons; signal-backed |
| `FocusPicture()` | | hands the keyboard to the picture so the transport keys work without clicking it first. Takes effect on a later build when called before the player is mounted — a component's first build runs in its constructor, where there is no focus manager to ask |
| `FullscreenRequested`, `IsFullscreen` | | the component knows no window: it raises the event and you set the flag once you have done whatever full screen means for the app. `Window.SetFullscreen` / `ToggleFullscreen` is the borderless kind — the window covers its display with no chrome and the display mode is left alone. An app that draws its own title bar must stop drawing it while `Window.IsFullscreen`; maximizing instead leaves the task bar over the picture |
| `ControlsVisible` | | true whenever paused |

Keys: Space or K play/pause, Left/Right and J/L seek, Up/Down volume, M mute, F full
screen, Home/End, 0–9 jump to that tenth. Theme through `VideoPlayerTheme`
(`Scrim`, `Foreground`, `Accent`, `BarHeight`, …): `Theme.Dark.With(new VideoPlayerTheme
{ Accent = brand })`.

## Finding libmpv

`MpvBackend.IsAvailable` says whether a libmpv could be loaded; the constructor throws
`DllNotFoundException` when it cannot. The resolver looks, in order, at
`HULTUI_MPV_PATH` (a file, or a directory to search), the application directory,
`runtimes/<rid>/native` beneath it, then the loader's own search. Names tried:
`libmpv-2.dll`, `mpv-2.dll`, `mpv-1.dll` on Windows; `libmpv.2.dylib`, `libmpv.dylib` on
macOS; `libmpv.so.2`, `libmpv.so` elsewhere. `HULTUI_MPV_LOG=warn` forwards mpv's log to
stderr. See [ENVIRONMENT.md](ENVIRONMENT.md).

The backend sets `hwdec=auto-copy`, `keep-open=yes` and disables mpv's own OSC,
terminal and youtube-dl hooks; a trimmed build without those options is fine, they are
set best-effort.

## Testing

`FakeMediaBackend` has no decoder, no threads and no clock. `Open` completes
synchronously to `Paused`; `Advance(wallClock)` moves playback at the current rate,
publishes every frame that falls due and ends at `Duration` — **nothing happens unless
playing**. `Raise(BackendEvent)` injects any event; `Calls` lists every command in order
(`Open(file:x.mp4)`, `Play`, `Seek(00:00:05, precise)`…); `FailNextOpenWith` scripts a
failure; `Duration = null` plays as a live stream. Frames are solid, byte `n` for frame
`n`, so a test can tell which frame the sink holds.

For the real backend, `TestClip.WriteTempY4m(width, height, fps, frames)` writes a
YUV4MPEG2 clip that needs no encoder to produce; the caller deletes it. The backend
tests skip themselves when no libmpv is present.
