# Environment Variables and Build Switches

Every knob the framework, the apps and the tests read from the environment, in one
place. Values are exact: most of these test for a literal `"1"` or `"0"` and ignore
anything else.

## Framework

| Variable | Value | Effect |
|---|---|---|
| `HULTUI_A11Y` | `0` | No accessibility bridge is created for any window, on any platform. Read once per window at construction. The purpose is equalising benchmarks against a machine with no assistive technology attached (on Linux the AT-SPI bridge otherwise connects whenever a bus is reachable), and an escape hatch if a bridge misbehaves. Any other value leaves it on. |
| `HULTUI_UIA_EVENTS` | `0` | Windows only, finer than the above: the UIA tree stays exported but no structure, property or focus-changed events are raised. |
| `HULTUI_TRACE` | a file path | Starts frame-trace recording at window creation and writes the trace there when the window is disposed. F11 still toggles a trace by hand and takes precedence. |
| `HULTUI_STARTUP` | `1`, or a file path | Phase timings from process launch to the first presented frame (`SDL_Init`, `SDL_CreateWindow`, GL context, Skia surface, first frame). `1` prints to stderr; any other value is taken as a file to write. Needs no app change: the marks are in the framework. |
| `HULTUI_GL_PRELOAD` | `1` | Lets `Application.BeginGraphicsPreload()` do something. With it set, an app that calls that method as the first statement of `Main` loads the GL driver on a worker thread and warms the text pipeline while its own pre-window work runs. Opt-in because loading the driver off the main thread is outside SDL's documented threading contract; validated on Windows/WGL, not on X11/Wayland. Without the variable the call is a no-op. The load stays on the critical path, so what is saved is only the main-thread work the app does anyway, plus the text pipeline's warm-up: ~10 ms measured on Junction, ~25-30 ms on Reel (Release JIT, five runs each way, first frame 502 ms without and 473 ms with, no run overlapping). Reel sets this itself on Windows when nothing else has, so its installed build gets it by default; setting it to `0` still turns it off. |
| `HULTUI_RENDERER` | `vulkan` | Draws through Vulkan instead of OpenGL — phase 1 of `docs/design/HDR.md`, and the only API that can present HDR on Windows and Linux both. Read once per process at window creation. SDR and sRGB for now: the aim is output indistinguishable from the GL path, which stays the default and is what every shipped build uses. Anything other than `vulkan` leaves it on GL. Presents with IMMEDIATE where the surface offers it (MAILBOX, then FIFO, as fallbacks) — the equivalent of the GL path's `SDL_GL_SetSwapInterval(0)`, because `Application` does its own pacing and FIFO would override that from underneath. Measured unpaced with a looping tween: GL ~10100 fps, Vulkan ~9300; on FIFO the Vulkan figure was the display's refresh rate exactly. What remains between the two is one extra command-buffer submit per frame and a render target Skia makes it rebuild each frame — see the note in `SkiaVulkanRenderSurface`. |
| `HULTUI_VULKAN_CAPTURE` | a file path | Writes the first presented swapchain image there as a PPM, once, then carries on. The only way to see what this renderer actually drew: the headless driver renders through `SkiaRasterRenderSurface` and never touches a GPU backend, and screen-grabbing the window captures whatever is in front of it. Diagnostic — it stalls the queue and allocates a staging buffer. Captures the **first frame that reuses a swapchain image**, not the first frame drawn: while every image is still fresh every belief about it is true, and a renderer can draw those frames perfectly and every frame after black. `HULTUI_VULKAN_CAPTURE_FRAME=<n>` picks another. The count restarts whenever a swapchain is built, so the file always holds the newest swapchain's work — resize the window and what lands on disk is from the size it is now, which is how a resize gets checked without looking at the screen. |
| `HULTUI_VULKAN_INTEROP` | `0` | Refuses the shared Vulkan/OpenGL image and leaves video on the software path — phase 4 of `docs/design/HDR.md`. Only meaningful with `HULTUI_RENDERER=vulkan`, where a video is otherwise decoded by mpv into a GL framebuffer over an image the Vulkan device owns, with no copy through main memory and sixteen bits a channel instead of eight. There to be turned off: the arrangement leans on two drivers agreeing about one allocation, and a machine where that goes wrong should still be able to play something. It is also how the two paths are compared on one clip. The interop is refused automatically, with a line on stderr, when the driver lacks the external-memory extensions or the GL context lands on a different GPU than the Vulkan device — so this is an override for the case where it is available and still wrong. Nothing is created until the first frame that draws a video; a window that never shows one pays nothing. |
| `HULTUI_VIDEO_HDR` | `0` | **Off switch.** By default a video producer is asked for linear light with its HDR range intact rather than a tone-mapped SDR picture — phase 5 of `docs/design/HDR.md`. On by default because the alternative is incoherent: a renderer that picked an HDR swapchain and then asked the decoder to throw the range away has wasted the swapchain. Only ever asked on an scRGB swapchain, so an SDR display is untouched, and a producer that does not understand the request keeps writing encoded pixels and says so. Measured on a film mastered at 1000 nits: the shared image goes from a maximum of 1.0 to 4.93 and the swapchain from 3.0 to 12.5 against an SDR white of 3.0, which is 982 nits on screen for a 1000-nit highlight. Set to `0` for a driver that mishandles it. |
| `HULTUI_VIDEO_HDR_PROBE` | a frame count | Reads the shared image back through GL every n frames, six times, and prints what the producer actually wrote into it. Diagnostic: the read is the whole image and stalls. It exists because the swapchain report is taken after compositing, so a missing range there does not say whether the producer or the renderer lost it — and because the equivalent read through Vulkan needs a layout Skia owns and moves. Take more than one sample: a black frame reads exactly like a producer writing nothing. |
| `HULTUI_VIDEO_WHITE` | `ui` | Where a video's reference white is put when `HULTUI_VIDEO_HDR` is on. The default puts it at the 203 nits it actually names, so the picture is absolutely correct and the interface around it — drawn at the display's SDR white, 240 nits here — is 18% brighter than white in the film. `ui` stretches the video so the two match instead, and every absolute luminance in the film is then wrong by that factor. Which is better is a judgement about the app. |
| `HULTUI_VULKAN_PEAK_NITS` | a number | What the display can show at its brightest, for tone mapping. There is no query behind this on either platform — Windows reports an SDR white level and no peak, and this SDL binding exposes no HDR headroom — so unset means the producer assumes a common mastering peak of 1000 rather than knowing. |
| `HULTUI_SDR_TRANSFER` | `srgb` or `2.2` | How an scRGB (HDR) Vulkan window turns its sRGB colours into light. Default: `2.2` under Wayland, `srgb` (the piecewise curve Skia uses) elsewhere. KWin shows an ordinary sRGB window through a pure 2.2 gamma, and with the piecewise curve the Vulkan window's shadows came out lighter and greyer than a GL window beside it — the navy UI and a film's blacks alike, while white matched. In `2.2` the UI is drawn encoded, as on an 8-bit swapchain, and decoded once on the way to the swapchain; linear-light video is encoded with the inverse and comes out unchanged. For comparing the two by eye. |
| `HULTUI_VULKAN_F16` | `1` | Asks for the half-float linear (scRGB) swapchain instead of 8-bit sRGB — phase 2 of `docs/design/HDR.md`, and what phase 3 presents HDR through. Requires `VK_EXT_swapchain_colorspace`, which the host then enables. Off by default, and `HULTUI_VULKAN_WHITE_SCALE=<k>` overrides the display's reported SDR white level — for a machine where the query fails, and for telling the cost of the extra pass from the cost of the half-float target, since 1 turns the pass off. Appearance is meant to be unchanged: measured against the 8-bit path, 99.0% of channels identical and 296 of the 297 differing pixels sit on an antialiased edge, where linear-light blending genuinely differs and is the more correct of the two. Costs roughly half the frame rate on an RTX 4080 (9187 → 4150 fps unpaced), which is the bandwidth of a 64-bit target. |

## Media (`HultUI.Media.Mpv`)

| Variable | Value | Effect |
|---|---|---|
| `HULTUI_MPV_PATH` | a file or a directory | First place the libmpv resolver looks: a path to the library itself, or a directory to search for `libmpv-2.dll` / `libmpv.so.2` / `libmpv.2.dylib`. After that: the application directory, `runtimes/<rid>/native` beneath it, then the loader's own search. |
| `HULTUI_MPV_LOG` | an mpv log level (`warn`, `info`, `v`, `debug`) | Forwards mpv's own log to stderr at that level. |

## App conventions (not read by the framework)

| Variable | Where | Effect |
|---|---|---|
| `HULTUI_DEVTOOLS` | Junction, Vesper Commander, Meridian | `1` sets `app.DevToolsHotkeysEnabled` in a Release build, so a shipped installer can be inspected without rebuilding. The framework property does not read it; each app does. See [DEVTOOLS.md](DEVTOOLS.md). |
| `HULTUI_TRACE_MS` | Junction | An integer: drive continuous frames for that many milliseconds, then close the window so shutdown flushes `HULTUI_TRACE`. |
| `JUNCTION_BENCH` | Junction | A script of interaction steps at absolute milliseconds since launch (`Branches@4000,History@12000,…`), annotated into the trace. |
| `HULTUI_MEDIA_NOHIDE` | the VideoPlayer and ProceduralVideo samples | `1` keeps the transport bar visible, for screenshots under the headless driver, which compresses time so the bar would otherwise auto-hide. |

## Tests

| Variable | Value | Effect |
|---|---|---|
| `HULTUI_UITESTS` | non-empty, not `0`/`false` | Enables the real-window UIA suite (`tests/HultUI.UiTests`); otherwise `[UiFact]` reports skipped. |
| `HULTUI_MPV_EXPECT_LGPL` | `1` | The mpv backend tests assert the loaded libmpv is the trimmed LGPL build rather than only reporting what they found. |

## Build-time defines

These are **MSBuild properties, not environment variables**; they become
`DefineConstants` and change what is compiled in.

| Property / symbol | Default | Effect |
|---|---|---|
| `HultUIStrictLifetime` → `HULTUI_STRICT_LIFETIME` | on in Debug, off in Release | Reading `Layout` or writing a scroll offset on an element that reconciliation discarded throws, naming the element and its build site. Within a strict build `Element.StrictLifetimeChecks` is a runtime switch for a test that means to inspect a discarded element. See [DECISIONS.md — the detached-element trap](DECISIONS.md#the-detached-element-trap). |
| `HultUIElementOrigin` → `HULTUI_ELEMENT_ORIGIN` | on in Debug, off in Release | Records the source file and line each element was built at (`Element.Origin`), which DevTools and the strict-lifetime message show. |

Override either from the command line:

```bash
dotnet build -c Release -p:HultUIStrictLifetime=true
```

## Related application settings

Not environment variables, but the switches people look for alongside them:

- `Application.FramePacing` — `Display` (default: at most one render per display
  interval, events handled as they arrive) or `Unlimited` (render whenever anything is
  dirty, for measuring). Neither touches the swap interval, which stays 0 so a vsync'd
  swap cannot block the Win32 modal resize loop. See [THREADING.md](THREADING.md).
- `Window.RefreshRate` — the display's rate in Hz, 0 when unknown; the loop falls back
  to 60.
- `Application.DevToolsHotkeysEnabled` — the one switch for Ctrl+Shift+I, F12 and F11.
- `UIThread.Enforce` — the wrong-thread guard, on in Debug builds of `HultUI.Core`. See
  [REACTIVITY.md — the thread guard](REACTIVITY.md#the-thread-guard).
