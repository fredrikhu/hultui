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
| `HULTUI_RENDERER` | `vulkan` | Draws through Vulkan instead of OpenGL — phase 1 of `docs/design/HDR.md`, and the only API that can present HDR on Windows and Linux both. Read once per process at window creation. SDR and sRGB for now: the aim is output indistinguishable from the GL path, which stays the default and is what every shipped build uses. Anything other than `vulkan` leaves it on GL. |
| `HULTUI_VULKAN_CAPTURE` | a file path | Writes the first presented swapchain image there as a PPM, once, then carries on. The only way to see what this renderer actually drew: the headless driver renders through `SkiaRasterRenderSurface` and never touches a GPU backend, and screen-grabbing the window captures whatever is in front of it. Diagnostic — it stalls the queue and allocates a staging buffer. Captures the **first frame that reuses a swapchain image**, not the first frame drawn: while every image is still fresh every belief about it is true, and a renderer can draw those frames perfectly and every frame after black. `HULTUI_VULKAN_CAPTURE_FRAME=<n>` picks another. The count restarts whenever a swapchain is built, so the file always holds the newest swapchain's work — resize the window and what lands on disk is from the size it is now, which is how a resize gets checked without looking at the screen. |

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
