# HultUI.Media — Design

A video player for HultUI, shipped as packages an application opts into, so an
application that never plays video never carries a decoder.

Written 2026-09-19 from a read of the framework as it stands. Nothing below has
been spiked yet; where a claim about a third-party library needs confirming on
real hardware it says **verify**.

## The shape of the problem

Three facts about the framework decide the design:

1. **Images are static.** `IImageSource` in Core is a marker (intrinsic size, loaded,
   alt text). `SkiaRenderContext.DrawImage` accepts only `SkiaImageSource`, which
   wraps one `SKBitmap` and lazily makes one `SKImage` of it. Nothing can change
   pixels between frames.
2. **The frame loop parks blind.** `Application.Run` calls `RunFrame`, and when
   nothing rendered it parks in `IWindowingPlatform.WaitForEvents(16)`. On Windows
   that is `MsgWaitForMultipleObjectsEx`, elsewhere `Thread.Sleep`. Neither is woken
   by `Application.InvokeOnMainThread`, which only enqueues. A decoded frame posted
   from another thread waits up to 16 ms before anything looks at it. Swap interval
   is 0, so there is no vsync to pace against either.
3. **Full redraw per frame, no damage rects** (`docs/DECISIONS.md`). A playing video
   therefore repaints the whole window at the video's frame rate. Measured full
   repaints are ~1–2 ms, so this is fine, and it is what animations already do.

Everything else the player needs — buttons, sliders, menus, theming, a11y roles for
`Button`/`Slider`/`Image`, headless hosting — already exists.

## Seams (framework changes, no video knowledge)

Four small changes, each useful beyond video. No package below is allowed to need
anything not listed here; if implementation finds a fifth, it goes in this section
first.

### S1 — A pixel frame source (`HultUI.Core`)

```csharp
namespace HultUI.Core.Rendering;

/// An image whose pixels change over time. The renderer re-reads it on every draw;
/// Version is for the headless render log, so a test can name the frame painted.
public interface IPixelImageSource : IImageSource
{
    /// Incremented by the producer on every new frame. Monotonic; never reset.
    long Version { get; }

    /// Borrows the current frame. Returns false when there is no frame yet. The
    /// buffer must stay untouched until the matching <see cref="Release"/>, which
    /// comes at the end of the frame, after the flush — not at the end of the draw.
    bool TryAcquire(out PixelFrame frame);
    void Release(in PixelFrame frame);
}

public readonly record struct PixelFrame(
    nint Pixels, int Width, int Height, int Stride, PixelFormat Format, int Slot);

// The "x" formats are opaque with an ignored fourth byte: a decoder's rgb0/bgr0,
// whose alpha byte is undefined and must not be read as premultiplied alpha.
public enum PixelFormat { Rgba8888, Bgra8888, Rgbx8888, Bgrx8888 }
```

**Shipped 2026-09-19** as written here, plus the sink and the borrow rule below.

- Backend-neutral, so it lives in Core beside `IImageSource` and the raster and
  headless surfaces render it unchanged.
- `SkiaRenderContext.DrawImage` grows a second branch: when the source is an
  `IPixelImageSource`, wrap the acquired slot with `SKImage.FromPixels` (no copy)
  and draw; Skia uploads to the GPU on the GL backend and blits on the raster one.
- `RecordingRenderContext` records `Version` so a headless test can assert that
  frame *n* was painted.
- `ImageElement` already re-measures when `Source` changes and `Fit` handles
  letterboxing, so the video element is `ImageElement` plus aspect-ratio measure
  and a role.
- **`PixelFrameSink`, also in Core.** The general case is an application with pixels
  in managed memory — a `byte[]`, a `Span<byte>` — which needs a pinned or native
  buffer behind the `nint` and should never touch one. So the ready-made sink lives
  beside the interface rather than in the Media package: it owns a ring of three
  pinned slots, `BeginWrite(w, h, format)` returns a `ref struct` writer over a
  `Span<byte>` with `Publish()`, and `Write(...)` copies a whole buffer in. A
  borrowed slot is never written over; a second borrow in the same render pass
  returns the same frame; `FramePublished` fires on the producer's thread and the
  subscriber marshals to mark the element dirty. The Media package's sink is this
  class, not a second one.
- **Borrow lifetime.** The Skia context holds every acquired frame until its
  `EndFrame` has flushed — on the GPU backend the upload happens at flush, not at
  the draw call — and only then releases it, so a producer can neither tear nor free
  bytes Skia has yet to read. There is no cross-frame caching of the wrapper: every
  draw re-reads and re-uploads, which is what a changing frame needs anyway; a
  paused frame repainting behind a tooltip pays about a millisecond at 1080p.

### S2 — External GL rendering on the Skia surface (`HultUI.Rendering.Skia`)

A backend that renders on the GPU (libmpv's render API, libvlc 4's output
callbacks) draws into a texture the framework owns, inside the frame, on the GL
thread, and Skia then composites that texture. Skia must be flushed before the
foreign code touches GL and told its state cache is stale afterwards.

```csharp
public sealed class SkiaGLRenderSurface
{
    /// GL entry-point resolver the surface was built with; a GPU decoder resolves
    /// its own GL through the same one so it lands in the same context.
    public Func<string, nint> GetProcAddress { get; }

    /// Runs foreign GL code inside the current frame: flushes Skia's pending work,
    /// invokes <paramref name="draw"/>, then resets Skia's GL state cache
    /// (GRGlBackendState.All). Only valid on the GL thread between Resize and
    /// Present, i.e. from a Render pass.
    public void RunExternalGL(Action draw);

    /// Wraps a GL texture the caller owns as an SKImage, without copying. Rebuild
    /// after the texture is resized; content changes need no rebuild.
    public SKImage WrapTexture(uint textureId, int width, int height,
        bool bottomLeftOrigin, uint internalFormat = GL_RGBA8);
}
```

and, in Core, the texture counterpart of S1 so Core's draw path can dispatch it
without knowing GL:

```csharp
/// An image the producer renders on the GPU during the frame. Discovered by type
/// test like IWin32WindowHandle; a raster or headless surface ignores it and the
/// producer must then fall back to IPixelImageSource.
public interface IGpuImageSource : IImageSource
{
    /// Called by the GL renderer inside the draw. The source renders its current
    /// frame into its own texture (via RunExternalGL) and returns a handle the
    /// renderer already wrapped, or null when nothing is ready.
    object? RenderFrame(IRenderSurface surface);
}
```

The Skia branch: `source is IGpuImageSource g && surface is SkiaGLRenderSurface`
→ `g.RenderFrame(surface)` returns the wrapped `SKImage` → draw it. The only new
public surface on the framework is on the class that was already public for the
WinForms host.

Skia caveat to respect: `RunExternalGL` must be called from inside
`RenderDirtyWindows` (context current, surface bound). Rendering from a decoder
thread is never allowed; the decoder signals, the frame renders.

### S3 — Wake the parked loop (`HultUI.Core`, `HultUI`, `HultUI.Platform.SDL3`)

```csharp
public interface IWindowingPlatform
{
    /// Thread-safe. Unblocks a WaitForEvents in progress so the next RunFrame
    /// starts now. No-op when nothing is waiting.
    void Wake();
}
```

**Shipped 2026-09-19.**

- `Application.InvokeOnMainThread` and the UI `SynchronizationContext`'s `Post`
  call `Wake()` after enqueueing, unless the caller is the UI thread. This was a
  latent bug fix on its own: every posted continuation used to wait up to 16 ms.
- SDL: `Wake` is `SDL_PushEvent` of a type from `SDL_RegisterEvents`, coalesced
  while one is already queued; `WaitForEvents` is `SDL_WaitEventTimeout(null, …)`,
  which peeks without popping and which SDL3 interrupts on a push from any thread.
  The hand-written `MsgWaitForMultipleObjectsEx` it replaced existed so that UI
  Automation queries arriving as Win32 sent messages would wake the wait (fad106c);
  SDL's own Windows wait is the same call with the same mask, and the real-window
  accessibility suite passes unchanged over the new path.
- Headless and the WinForms host: no-op; neither has a wait to cut short.
- Tests: `WakeTests` (headless, the wiring) and `SdlWakeTests` (real window, the
  wait itself parks and a cross-thread wake returns it at once).

With S3, frame pacing needs no continuous-render flag: the decoder thread stores a
frame, posts `MarkPaintDirty` for the element, wakes the loop, and one frame
renders. Paused video costs nothing.

### S4 — Nothing else

Audio is the backend's business (both candidates own their audio output).
Fullscreen has no platform API today; the first version toggles `Maximize` plus
hiding chrome, and a real `IPlatformWindow.SetFullscreen` is a later seam if an
app asks. Accessibility uses the existing `Image` role for the surface and
`Button`/`Slider` for the transport bar.

## Package layout

```
src/
  HultUI.Media/                  package HultUI.Media       — no natives
  HultUI.Media.LibVlc/           package HultUI.Media.LibVlc — refs LibVLCSharp
  HultUI.Media.Mpv/              package HultUI.Media.Mpv    — hand P/Invoke, libmpv
tests/
  HultUI.Media.Tests/            headless, fake backend, no natives
  HultUI.Media.Backends.Tests/   real backends, skipped unless natives resolve
samples/
  HultUI.Samples.Media/          a player app, driven by /run-hultui-app
```

All three packages are `net10.0`, `IsAotCompatible`, in `HultUI.CrossPlatform.slnf`,
and pack through `src/Directory.Build.props` like everything else. `PACKAGING.md`'s
table gains three rows.

### `HultUI.Media`

References `HultUI` (it needs `Application.InvokeOnMainThread` and the Skia
surface types). Contents:

| Type | Role |
|---|---|
| `IMediaBackend` | The contract a decoder package implements. Deliberately narrow — see below. |
| `MediaSource` | `FromFile`, `FromUri`, `FromStream` (backends that cannot do streams say so). |
| `MediaPlayer` | Owns one backend instance. `[Reactive]` state: `State`, `Position`, `Duration`, `Rate`, `Volume`, `Muted`, `VideoSize`, `AudioTracks`, `SubtitleTracks`, `Buffering`, `Error`. All commands and all state on the UI thread; it marshals backend events with `InvokeOnMainThread`. |
| `VideoElement` / `Video.New(player)` | `ImageElement` subclass: intrinsic size from `VideoSize`, `Fit` for letterbox/cover, role `Image` with the media title as label. Paints whatever frame source the backend attached. |
| `VideoPlayer` | Component: `VideoElement` + transport bar (play/pause, scrubber `Slider`, elapsed/remaining, volume, rate, track menus via `ContextMenu`, fullscreen). Auto-hides controls on idle pointer with the existing tween machinery. Keyboard: space, arrows, `M`, `F`. Themed from `Theme` tokens. |
| `PixelFrameSink` | Triple-buffered `IPixelImageSource`. Backend writes into the free slot on its thread, publishes, then the sink bumps `Version` and posts dirty+wake. Never blocks the decoder on the renderer or vice versa. |
| `GpuFrameSink` | `IGpuImageSource` over an FBO+texture the sink owns; `RenderFrame` calls the backend's GL draw inside `RunExternalGL`. Falls back to a `PixelFrameSink` when the surface is not GL. |
| `FakeMediaBackend` (namespace `HultUI.Media.Testing`) | Scripted backend: synthetic frames of a chosen size and colour, fake duration, instant seeks, scriptable errors and buffering. Lives in the package, not a separate one, so an app's headless tests need nothing extra. |

### `IMediaBackend`

```csharp
public interface IMediaBackend : IDisposable
{
    BackendCapabilities Capabilities { get; }        // GpuFrames, PixelFrames, Streams, Rate, Subtitles…

    /// Called once, on the UI thread, before Open. The backend picks the richest
    /// sink mode it supports; the sinks are created by MediaPlayer.
    void Attach(IVideoSink sink);

    void Open(MediaSource source);
    void Play(); void Pause(); void Stop();
    void Seek(TimeSpan position, bool precise);
    void SetRate(double rate);
    void SetVolume(float volume); void SetMuted(bool muted);
    void SelectAudioTrack(int id); void SelectSubtitleTrack(int id);

    /// Raised on any thread. MediaPlayer marshals. The backend never touches
    /// elements or reactive state.
    event Action<BackendEvent> Event;
}
```

`BackendEvent` is a small discriminated set: `StateChanged`, `Position` (backend
throttles to ≤ 10/s), `Duration`, `VideoGeometry` (w, h, sample aspect), `Tracks`,
`Buffering`, `EndReached`, `Error`. Frames go through the sink, not the event.

The contract is narrow on purpose: it is what `VideoPlayer` can show. Chapter
lists, equalisers, DVD menus and playlists stay off it until a control needs them.

### `HultUI.Media.LibVlc`

- `PackageReference LibVLCSharp` only. The consumer adds
  `VideoLAN.LibVLC.Windows` / `VideoLAN.LibVLC.Mac`, or installs `libvlc` from the
  distribution on Linux. The README says so, with the plugin-trimming recipe.
- Frame path: `MediaPlayer.SetVideoFormatCallbacks` picks `RV32`/`BGRA` at the
  reported size; `SetVideoCallbacks` lock/unlock/display map onto the
  `PixelFrameSink` slots. `Capabilities = PixelFrames | Streams | Rate | Subtitles`.
- `LibVLC` construction is slow (plugin cache); do it once per process through
  `AsyncLoader` off the UI thread.
- When LibVLCSharp 4 ships, `SetOutputCallbacks` gives a `GpuFrames` path behind the
  same sink interface, with no change to `HultUI.Media`.

### `HultUI.Media.Mpv`

- No package dependency. Hand-written `LibraryImport` bindings for the ~40 entry
  points the backend uses (`mpv_create`, `mpv_initialize`, `mpv_command`,
  `mpv_set/get_property`, `mpv_observe_property`, `mpv_wait_event`,
  `mpv_render_context_*`). The client API is versioned and ABI-stable, so this is
  a few hundred lines and AOT-clean by construction.
- Frame path: `mpv_render_context_create` with the OpenGL API type and
  `GetProcAddress` from S2; `mpv_render_context_set_update_callback` posts
  dirty+wake; `GpuFrameSink.RenderFrame` calls `mpv_render_context_render` into
  the sink's FBO inside `RunExternalGL`. On a raster/headless surface it uses the
  `sw` render API into a `PixelFrameSink` instead, which makes mpv testable
  headlessly with real media.
- Natives: none on NuGet. The package documents where a build comes from; a
  trimmed LGPL build is the project's own artefact (see comparison below).

### Tests

`HultUI.Media.Tests` hosts `VideoPlayer` with `FakeMediaBackend` under the headless
session: state machine transitions, scrubber ↔ position binding, keyboard,
auto-hide, letterbox geometry via `LayoutProbe`, and "frame *n* painted" via the
recording context and S1's `Version`. `HultUI.Media.Backends.Tests` opens a
checked-in 2-second test clip per backend and asserts first-frame, duration, seek
and end-reached; each fixture skips when its native library does not resolve, so
CI without natives stays green and a machine with them proves the real thing.

## Implementation order

1. S3 (wake) — standalone, fixes a latent latency bug, lands first.
2. S1 (pixel source) + `PixelFrameSink` + `VideoElement` + `FakeMediaBackend` +
   `HultUI.Media.Tests`. Whole player provable headlessly before any decoder exists.
3. `VideoPlayer` transport UI, sample app.
4. S2 (external GL) + `GpuFrameSink`.
5. The `Mpv` backend: bindings, the `sw` path first (testable headlessly with real
   media), then the GL path. Backend test fixture.
6. The trimmed LGPL native build for win-x64, linux-x64, osx-arm64 on the
   self-hosted runners, packaged per RID.
7. `LibVlc` only if an application asks for something mpv lacks (Chromecast, DVD
   menus, VLC's adaptive streaming).

## LibVLCSharp vs a trimmed libmpv

"Trimmed libmpv" means: mpv built with `-Dgpl=false` (LGPL-2.1+), without Lua,
JavaScript, yt-dlp hooks, libarchive, DVD/Blu-ray/CD, Vulkan and the extra
shader toolchain, against an LGPL FFmpeg. Nobody publishes that; it is a build
the project would own.

### Capabilities

| | LibVLCSharp (libvlc 3) | Trimmed libmpv |
|---|---|---|
| Decode + demux | VLC's own modules plus FFmpeg; the broadest set of any player | FFmpeg (statically bundled) plus dav1d |
| Hardware decode | DXVA2/D3D11VA, VAAPI/VDPAU, VideoToolbox. With the memory output the frame is **copied back to system memory and colour-converted on the CPU** (swscale). **Verify** which hwdec modes survive the vmem output on each OS | `d3d11va-copy` / `nvdec` / VAAPI / VideoToolbox. Frames stay NV12 until mpv's GPU shaders convert them; on Windows with desktop GL the copy-back variants are the norm, `nvdec` is zero-copy on NVIDIA |
| Frame delivery to HultUI | CPU RGBA buffers via `SetVideoCallbacks`: one memcpy per frame into a sink slot, then one GPU upload by Skia | Rendered straight into our FBO on the GL thread; Skia composites the texture. No CPU pixels at all on the GL backend |
| 4K60 feasibility | Doubtful: 33 MB/frame of RGBA through swscale and two copies. 1080p60 is comfortable | Expected fine; scaling and conversion are GPU work |
| Scaling quality | Skia's linear/mipmap sampling of an RGBA bitmap | mpv's own scalers (spline36, ewa_lanczos), debanding, dithering, interpolation |
| HDR content | No tone mapping in the vmem path; HDR sources look washed out | Tone-mapped to SDR by libplacebo, correct colours |
| Subtitles | libass; also DVB, teletext, CEA-608/708 | libass; CEA-608/708 through FFmpeg |
| Audio output | Own: WASAPI/DirectSound, PulseAudio/ALSA, CoreAudio | Own: WASAPI, PulseAudio/PipeWire/ALSA, CoreAudio, SDL |
| Playback rate | Yes, pitch-corrected (scaletempo) | Yes, pitch-corrected (scaletempo2, better) |
| Seeking | Precise and fast modes | Precise and keyframe modes, plus frame-step |
| Streaming | HTTP/HLS/DASH (own adaptive logic), RTSP, RTP/UDP, SRT, SMB/FTP/SFTP | Whatever FFmpeg's protocols give: HTTP/HLS/DASH (FFmpeg's, less robust), RTSP, RTP, SRT if built in |
| Chromecast / DVD menus / discovery | Yes | No |
| Raster/headless path | Same vmem callbacks, works anywhere | `sw` render API renders to a CPU buffer, works anywhere |

### Formats

Both play essentially every container and codec you will meet: MP4/MOV, MKV,
WebM, AVI, MPEG-TS/PS, FLV, Ogg; H.264, HEVC, VP8/VP9, AV1, MPEG-2/4, ProRes,
DNxHD; AAC, MP3, Opus, Vorbis, FLAC, AC-3/E-AC-3, DTS, TrueHD, PCM. The
differences are at the edges: VLC has more of its own demuxers for broadcast
oddities (TS with odd PIDs, DVB subtitles, teletext) and hardware-format quirks;
mpv gets FFmpeg's set and nothing else. For a desktop application playing files
and HTTP streams neither will be the one that cannot open something. Neither does
DRM.

### Distribution and size (approximate, measure before deciding)

| | LibVLCSharp | Trimmed libmpv |
|---|---|---|
| Windows natives | Official `VideoLAN.LibVLC.Windows` NuGet, x86 + x64, ~100 MB per arch on disk; trimming the plugins folder gets to ~25–40 MB. No win-arm64 in 3.x (**verify**) | Nothing official. Community GPL builds exist (x64); the LGPL trimmed build is ours, ~30–40 MB as one DLL |
| macOS natives | Official `VideoLAN.LibVLC.Mac` NuGet (**verify** arm64 coverage in 3.x) | Homebrew ships GPL builds; LGPL trimmed build is ours |
| Linux natives | Distribution `libvlc` (versions vary; Ubuntu 24.04 has 3.0.20) | Distribution `libmpv` is GPL; LGPL trimmed build is ours |
| Who maintains the native build | VideoLAN | This project, on the self-hosted runners, for three OSes |
| Startup | `LibVLC` creation scans the plugin cache: tens to hundreds of ms, once per process; hide it behind `AsyncLoader` | `mpv_create`+`initialize` is a few ms; first frame compiles shaders, tens of ms |
| Managed API | Complete C# object model, events, tracks, media parsing; maintained by VideoLAN | String-keyed properties and commands; we bind what `IMediaBackend` needs. Existing .NET bindings are dated, so hand P/Invoke |
| NativeAOT | LibVLCSharp uses delegate marshalling; check its trim/AOT warnings against the framework's AOT bar | Clean by construction with `LibraryImport` and function pointers |
| Release cadence | libvlc 3.0.x maintenance only; 4.0 pre-release for years | Two to three releases a year, stable C API since 0.30 |

### Licence

- **libvlc** is LGPL-2.1+. Dynamic linking from a proprietary app is fine. A few
  bundled plugins in the Windows build are GPL (DVD decryption, some codecs);
  drop them from the plugins folder if the consumer must stay LGPL-only.
- **mpv** is GPL-2+ by default. Built with `-Dgpl=false` it is LGPL-2.1+; the
  remaining GPL pieces (a few legacy outputs and filters) are excluded. Its
  dependencies in that configuration are all permissive or LGPL: FFmpeg (LGPL
  build, no x264/x265/fdk-aac — encoders only, decode unaffected), libass (ISC),
  libplacebo (LGPL-2.1), dav1d (BSD), FreeType, HarfBuzz, fribidi. Any distro or
  community build a consumer would find on their own is GPL, so an LGPL story
  requires shipping our build, not documenting one.
- The repository still has no LICENSE file (`PACKAGING.md`), so neither choice is
  constrained by HultUI's own licence yet.

### Engineering cost

| | LibVLCSharp | Trimmed libmpv |
|---|---|---|
| Framework seams needed | S1, S3 | S1, S2, S3 |
| Backend code | ~400 lines over LibVLCSharp | ~300 lines of bindings + ~400 lines of backend |
| Native build pipeline | None | Windows (mpv-winbuild-cmake, cross from Linux), Linux (meson), macOS (meson); one CI job per OS on the self-hosted runners; a NuGet natives package per RID |
| Ongoing | Bump a NuGet version | Rebuild on each mpv/FFmpeg release, track security fixes in FFmpeg ourselves |

### Recommendation

The video quality, HDR handling, 4K headroom and the GPU frame path all favour
mpv, and its rendering model is the better match for a GL-composited framework.
Its cost is entirely in owning a native build for three operating systems, and
that cost is permanent.

LibVLCSharp needs one fewer seam, no native builds, and covers the file-and-stream
playback a desktop application actually does, at 1080p, today. Its weaknesses are
4K, HDR, and a CPU frame copy that is acceptable on the hardware the framework
targets.

The document's original recommendation was LibVLCSharp first, for the smaller
seam count and the absence of a native build.

**Decision (2026-09-19): trimmed libmpv.** The rendering model, HDR handling and 4K
headroom were judged worth owning the native build. `IMediaBackend` stays
backend-neutral, so a LibVLCSharp backend remains a later option at the cost of
one package.
