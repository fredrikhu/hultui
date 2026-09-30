# HDR output

**Status, 2026-09-29.** HDR video plays on the Vulkan renderer, with the transport bar and the rest
of the UI composited over it, on Windows and on Linux (KDE Wayland, NVIDIA). Apps choose Vulkan in
code: Reel and Aperture do, and everything else stays on OpenGL. **Untested:** Mesa, AMD and
Intel, and a hybrid GPU. Every Vulkan number here comes from one vendor's driver.

This page is the current design: how a frame gets to the screen, the rules it depends on, the
defaults and what they cost, and what is not done. How it got here, with every measurement and
every wrong turn, is [HDR-LOG.md](HDR-LOG.md). Platform run sheets are the `HDR-LINUX-*.md` and
`HDR-WINDOWS-*.md` files beside it, and the review fixes are `docs/plans/15-vulkan-hdr-review-fixes.md`.

**Before changing anything, read [Traps](#traps).** Most of them cost hours, several were shipped
and found by the other platform, and many were nearly recorded as successes by a measurement that
could not have failed.

## Why Vulkan

HDR is a property of the **presented surface**: the compositor has to be handed an HDR-capable
swapchain before anything drawn into it can be HDR. **OpenGL on Windows cannot ask for one.** No
WGL colour-space extension is honoured for HDR.

Two faster routes were rejected because they are Windows-only, and because video would stop being
an element in the tree: **mpv owning the output** (`--wid`, a child window) and **DirectComposition**
with separate video and UI swapchains. **Vulkan** has `VK_EXT_swapchain_colorspace` on Windows and
Linux, and SkiaSharp 3.119.2 ships its Vulkan backend.

**mpv's render API only speaks OpenGL or CPU memory.** There is no Vulkan render type, and building
libmpv with Vulkan does not add one: mpv's own Vulkan output draws into a surface mpv owns, which is
the `--wid` route again. So mpv keeps rendering with GL, into an image the Vulkan device allocated
and exported. That bridge is external memory, and it is the part the whole design rests on.

## How a frame gets to the screen

### Choosing the renderer

`new Application(..., renderer: RendererKind.Vulkan)` asks for Vulkan. **`HULTUI_RENDERER`
overrides the app in both directions**, because it is how the design docs select a renderer and how
a machine where Vulkan misbehaves gets working again without a rebuild. The renderer is chosen once
per process.

**An app's request falls back to GL** when Vulkan cannot start, with a line on stderr. There are two
paths: SDL refuses the Vulkan window when there is no driver at all, and a driver with no usable GPU
(a remote session, a VM, a filtered loader) fails later, when the host is created. **A request
from `HULTUI_RENDERER` never falls back**: the failure is the answer, since a silent GL would make
every such run a false negative. `HultUI.WinForms` hosts GL only.

### The swapchain (`SdlVulkanHost`)

- **Format:** scRGB half-float (`R16G16B16A16_SFLOAT` with `EXTENDED_SRGB_LINEAR`) when the
  display is in HDR mode and the instance enabled `VK_EXT_swapchain_colorspace`, otherwise 8-bit
  `B8G8R8A8_UNORM` or `R8G8B8A8_UNORM` in sRGB-nonlinear. Never the `_SRGB` formats.
- **Present mode:** MAILBOX, then IMMEDIATE, then FIFO. IMMEDIATE let the compositor stretch the
  previous image into a resized window.
- **Resize:** rebuilt for every size by default. `HULTUI_VULKAN_FIXED_EXTENT=1` over-allocates and
  presents one-to-one through `swapchain_maintenance1` instead.
- **Synchronisation:** a semaphore per image orders the present after the frame. The acquire waits
  on a fence, because SkiaSharp exposes nothing to hand a semaphore to; it costs 0.0064 ms a frame.
- **Per window:** each host has its own instance, device and command table. Device-level function
  pointers are never shared between windows.

The choices themselves are `VulkanSwapchainChoice`, unit-tested without a GPU.

### Drawing the UI (`SkiaVulkanRenderSurface`)

The render target over a swapchain image is **built fresh every frame**: Skia tracks the image's
layout inside it, and the host moves the layout behind Skia's back.

On scRGB, 1.0 is 80 nits, and the UI's white has to land at the display's SDR white instead: the
**white scale**, SDR white over 80 (3.0 for 240 nits). **Skia cannot put a glyph past 1.0**, with
any paint, colour matrix or shader. So the frame is drawn into a 0..1 offscreen, where glyphs and
fills agree, and multiplied once on the way to the swapchain. `ScRgbFramePaths.Choose` picks one of
four routes:

| Route | When |
|---|---|
| Direct | not scRGB, or a white scale of 1 |
| Offscreen | scRGB with a white scale above 1 (Windows) |
| Encoded offscreen | the compositor decodes sRGB with gamma 2.2 (KWin), at any white scale |
| Scaled space | `HULTUI_VULKAN_SCALED_SPACE=1`: the scale rides in the destination colour space, no pass |

The encoded offscreen exists because **KWin shows an sRGB window through a pure 2.2 gamma** while
Skia uses the piecewise curve, so shadows came out lighter than a GL window beside them. The UI is
drawn encoded and decoded with 2.2 in the blit. Both shaders keep the sign of negative values, for
wide gamut.

### Display colour: HDR state, SDR white, peak

| | HDR state | SDR white | peak |
|---|---|---|---|
| **Windows** | display configuration (`GET_ADVANCED_COLOR_INFO_2`) | display configuration, per monitor | white × SDL's headroom |
| **Windows, events on** | WinRT `DisplayInformation` | `DisplayInformation` | `DisplayInformation`, absolute |
| **Wayland** | SDL | the protocol constant **2.5375** (203 nits) | 203 × SDL's headroom |
| **X11** | none: X11 offers only sRGB-nonlinear | — | — |

- **SDL's HDR flag is wrong on Windows.** It calls a display HDR when headroom exceeds 1, so a
  slider above the panel's peak reads SDR, and it goes stale across Win+Alt+B.
- **Nothing announces the SDR brightness slider.** The window gets no message at all. So Windows is
  polled every 100 ms (about 12 µs a poll), unless the app turns on display-colour events
  (`displayColorEvents: true`, which Reel and Aperture pass, or `HULTUI_DISPLAY_INFORMATION=1`).
  The events are hand-rolled COM with no new dependency, and track a dragged slider at once.
- **On Wayland SDR white is a protocol constant**, not a property of the monitor. SDL's
  `SDR_white_level` reads 1.0 there, and using it gives a dim picture.
- **A change** arrives as a display or HDR-state event, or from the poll. The swapchain is rebuilt
  only if its format changes, and the blit follows the new white scale either way.
- **HDR metadata is not sent.** For scRGB, Windows composites from the display's own profile, and
  invented numbers are worse than none.

### Video: mpv into a shared image

1. **A hidden GL context** beside the Vulkan device, created on the first frame that draws a video.
   It must be on the same GPU: the device UUIDs are compared, through the indexed
   `glGetUnsignedBytei_vEXT` query.
2. **A shared `RGBA16F` image per GPU source** (a video, a web page), allocated dedicated and exportable, exported as a Win32 handle or
   an fd, and imported into GL (`GL_EXT_memory_object`, with `GL_DEDICATED_MEMORY_OBJECT_EXT`) as a
   texture with a framebuffer over it.
3. **mpv renders into that framebuffer** through its GL render API, told the format and
   `MPV_RENDER_PARAM_DEPTH=16`. Skia then draws the image. There is no copy through main memory.
4. **Ordering, both ways.** GL signals a semaphore after mpv draws, which the Vulkan hand-back
   barrier waits on. A second semaphore runs back: the present releases the image to the external
   queue and signals it, and GL waits on it before the next write (184ae17). The barrier command
   buffers are a ring of three with a fence each, so nothing blocks the CPU. Each image has its
   own pair, and one barrier submit hands back every image drawn in the frame.
   `HULTUI_VULKAN_SEMAPHORE=0` goes back to `glFinish` and a queue wait.

**A failure is harmless.** A missing extension, a context on another GPU or a refused import leaves
`SupportsExternalGL` false, and the video takes its pixel path, which is slower and works.
`HULTUI_VULKAN_INTEROP=0` refuses the interop outright. **Each source is lent an image of its
own**, keyed by the source (seam W7 in [WEBVIEW.md](WEBVIEW.md)), so a video and a web page, or
two pages, draw in one window. A source that goes away gives its image back, and the host
destroys it at the next frame after a device wait. A source that asks twice in one frame at two
sizes gets nothing the second time.

### HDR video

On an scRGB target, mpv is told to draw linear light for the display:

- `target-prim=bt.709`, `target-trc=linear`, `target-peak` = the display's peak (10 to 10000 nits).
- `gamut-mapping-mode=clip`, so colours outside BT.709 stay as negative values, which scRGB shows
  as the colours they are. `HULTUI_VIDEO_WIDE_GAMUT=0` maps them into BT.709 instead.
- **libplacebo's linear 1.0 is exactly 203 nits**, the BT.2408 reference white. The frame is drawn
  at 203 over the canvas's white (203/240 on Windows) under the default `reference` policy, so a
  1000-nit highlight is 1000 nits. `HULTUI_VIDEO_WHITE=ui` stretches it to the UI's white instead.
  On Wayland the two already agree, since 2.5375 × 80 = 203.

The options are set **asynchronously**, and a frame is labelled with the settings mpv has
*confirmed*, not the ones it was asked for (`MpvTargetColour`). A paused frame is drawn again once
a change is confirmed. `HULTUI_VIDEO_HDR=0` turns all of this off.

### Decode

`hwdec=auto-safe`. **On NVIDIA it is zero-copy on both platforms**: the trimmed LGPL libmpv has
NVDEC with CUDA interop (38da446 on Windows), and mpv registers its own plane textures with CUDA and
renders from them into the shared framebuffer. The VO line reads `cuda[p010]`. The DirectX interops
stay off, because they bridge at 8 bits. So AMD and Intel on Windows decode on the GPU and copy
(`d3d11va-copy`). The natives build refuses to link `nvcuda` at load time, so the DLL still loads on
a machine without NVIDIA's driver.

### mpv's threads

**The UI thread is mpv's render thread, and it never waits on mpv's core.** After every load and
seek, the core waits up to 200 ms for the first frame to be drawn. So every command from the UI
thread is queued (`mpv_command_async`, `mpv_set_property_async`), and a frame that arrives before
the video's size is known is taken without drawing (`MPV_RENDER_PARAM_SKIP_RENDERING`) and drawn
once the size arrives.

## Invariants

The rules the design depends on. Each one was broken once, and most failed silently.

- **Never cache** a render target over a swapchain image, or a wrapped renderer-owned image. Skia
  tracks the layout inside them.
- **Swapchain images need `TRANSFER_SRC`.** Without it Skia returns a null surface and says
  nothing.
- **scRGB only with `VK_EXT_swapchain_colorspace` enabled.** Drivers list the colour space either
  way.
- **The white scale is applied once**: by the blit, or by the scaled colour space, never both.
- **Read the shared image through GL**, never Vulkan. Skia owns its Vulkan layout.
- **A Win32 handle is closed by the caller; an fd is consumed by the import.**
- **An app's request may fall back to GL; `HULTUI_RENDERER`'s may not.**
- **A frame's colour label follows what mpv confirmed.**
- **The UI thread makes no synchronous libmpv call** once a render context exists.
- **The natives never import `nvcuda` at load time.**

## Defaults

Every switch is described in [ENVIRONMENT.md](../ENVIRONMENT.md).

| Decision | Default | Why | Switch |
|---|---|---|---|
| Renderer | GL; Vulkan per app | Vulkan costs start-up, memory and resize CPU that apps without HDR video would pay for nothing | `renderer:`, `HULTUI_RENDERER` |
| Swapchain format | follows the display | enumeration lists the same formats in HDR and out | `HULTUI_VULKAN_F16` |
| Present mode | MAILBOX | no resize stretch, and video level with GL | `HULTUI_VULKAN_PRESENT` |
| Fixed extent | off | halves resize CPU, costs 4K video about 70% | `HULTUI_VULKAN_FIXED_EXTENT` |
| Scaled colour space | off | 0.10 ms a frame and no memory saved, on a path off Skia's usual one | `HULTUI_VULKAN_SCALED_SPACE` |
| Display-colour events | off, apps opt in | COM in the message loop, which apps without HDR do not need | `displayColorEvents:`, `HULTUI_DISPLAY_INFORMATION` |
| HDR video | on | an HDR swapchain with a tone-mapped picture wastes the swapchain | `HULTUI_VIDEO_HDR` |
| Video white | `reference` | decided by eye: a correct picture beats matching chrome | `HULTUI_VIDEO_WHITE` |
| Wide gamut | on | confirmed by eye on Windows | `HULTUI_VIDEO_WIDE_GAMUT` |
| Interop, semaphores | on | the copy cost 675 ms/s of CPU at 1080p on Linux; the semaphore saves 135 ms/s at 4K on Windows | `HULTUI_VULKAN_INTEROP`, `HULTUI_VULKAN_SEMAPHORE` |
| hwdec | `auto-safe` | zero-copy where the interop exists, a copy otherwise | `HULTUI_MPV_HWDEC` |

## What it costs

Windows 11, RTX 4080, a 120 Hz HDR display at 150%, Release builds, 2026-09-27. One value per
quantity; where HDR-LOG.md has several, these are the most careful.

| | GL | Vulkan | |
|---|---|---|---|
| start-up to first frame | 552 ms | 679 ms | +127, all in the first frame of an F16 target; 8-bit Vulkan is 581 |
| second window | 170–178 ms | **154–161 ms** | |
| present pass | **0.10 ms** | 0.40 ms | 0.20 of it Vulkan and the F16 target, 0.10 the offscreen |
| private memory, two windows | **81 MB** | 133 MB | 86 on an 8-bit swapchain; the offscreen is 9 MB a window |
| resize CPU, 120 resizes | **90–123 ms/s** | 789–804 ms/s | MAILBOX; IMMEDIATE 275–366, FIFO 297–390 |
| 4K HEVC HDR video, CPU | 51 ms/s | **43 ms/s** | both zero-copy; 178 without the semaphore |

- **The Windows costs are 16-bit colour, not Vulkan.** An 8-bit Vulkan window starts within 30 ms
  of GL, resizes at GL's cost, and sits 5 MB above it.
- **On NVIDIA Linux F16 is nearly free.** There the fixed costs are the API's: about +95 ms of
  start-up and +23 MB, with or without F16.
- **The fixed extent**, in its own A/B: 10 swapchain builds instead of 122, and resize CPU from
  801 to 466 ms/s (GL 264), which is 3.0x GL's down to 1.8x. It costs 4K video 91–95 ms/s against
  48–63.
- **Resize CPU under MAILBOX is driver-side.** Halving the frames drawn during a resize left it
  unchanged.

## Not done

- **Mesa, AMD, Intel, a hybrid GPU.** No machine here has one.
- **Linux checks still open:** negative scRGB (wide gamut) on KWin, the indexed device-UUID query
  on Mesa, the scaled colour space, and the GL fallback, whose automated tests are Windows-only.
- **Zero-copy on AMD and Intel under Windows.** Their only bridges are 8-bit, or need ANGLE, which
  cannot import Vulkan memory.
- **Start-up's +127 ms.** It is pipeline compilation for the F16 target, and SkiaSharp exposes no
  persistent pipeline cache.
- **A fixed extent only while dragging** would get both its resize saving and full video speed. It
  needs a settled-size signal the renderer does not have.
- **HDR10 swapchains and HDR metadata.** scRGB was chosen: HDR10 would halve bandwidth, but PQ is
  absolute luminance and Skia's colour management is relative.
- **Video luminance under the scaled colour space** matches the offscreen path to 0.4% on average,
  but no same-frame comparison was possible.
- **Per-source images on Linux.** Seam W7 ran on Windows only; the fd export of each source's
  semaphores has not.
- **One "render() not being called or stuck" line at Vulkan start-up** remains, logged only at
  `HULTUI_MPV_LOG=v`. The UI thread is busy with its first frame and the window reveal, so the
  picture comes up no later because of it.

## Phases

Code comments name these phases.

| Phase | What it built |
|---|---|
| 0 | `spikes/HdrInterop`: the external-memory bridge, proven on Windows and Linux |
| 1 | `SkiaVulkanRenderSurface`: the Vulkan renderer, SDR |
| 2 | the half-float scRGB swapchain, and the offscreen for the white scale |
| 3 | the swapchain following the display's HDR state and SDR white |
| 4 | video through the shared image, without a copy |
| 5 | HDR range through mpv's target options, and the luminance policy |

## Traps

**Synchronisation validation does not see the GL/Vulkan seam.** Removing the semaphore wait that
orders mpv's GL write before Skia's read gave **0 validation messages**, while a fault on the
Vulkan side gave 21 at once. GL's writes to external memory are invisible to the layer. The seam's
failure is **blinking black frames**, which a person sees, and its control is behavioural: withhold
the signal and GL stalls. "Validation was clean" covers everything either side of the seam, never
the seam.

The traps below are grouped by **shape**, because the shapes recur and the phases do not.

### 1. Measurements that cannot fail

A check that reports the same thing whether the code works or not reads as evidence and is not.
**Build the positive control first.**

- **A frame with nothing in it looks exactly like a pipeline that lost the picture.** Both report a
  maximum of 0.0 or 1.0. Three wrong conclusions came from capturing 2001 during its black
  overture, twice after the lesson was written down. Take several spaced samples and prove the
  content was there.
- **A maximum cannot see dithering**, and **an 8-bit test clip cannot show 8-bit quantisation**. It
  took a 10-bit ramp to find an 8-bit target dithered inside a 16-bit one.
- **`Process.HandleCount` does not move per handle**; `GetProcessHandleCount` does.
  **Re-exporting one allocation is not a leak test**: NVIDIA on Windows returns the same `HANDLE`
  every time. Reallocate each cycle, as the renderer does.
- **The headless driver proves nothing about this renderer.** It draws through
  `SkiaRasterRenderSurface`. A swapchain capture (`HULTUI_VULKAN_CAPTURE`) is the evidence, and the
  real-window tests in `HultUI.UiTests` use it.
- **A capture proves the image, never the screen.** A wrong SDR white scale is invisible to every
  capture: the image is right and the compositor obeys it.
- **An idle window may never reach the default capture frame.** A missing capture can mean an idle
  window, not a renderer that drew nothing.
- **A benchmark can measure the wrong workload.** The GL-to-Vulkan ordering was timed at 0.27 ms
  a frame, flat from 1080p to 4K, against a `glClear` on an idle GPU. Under a real 4K decode it was
  0.74 to 5.33 ms. A cost that does not scale with what it should scale with is a suspect benchmark.
- **A validation run with no layer installed reports nothing.** On Windows,
  `VK_LOADER_LAYERS_ENABLE` does nothing without the SDK, and the silence was once recorded as
  clean. Inject a fault and see the layer catch it.
- **A fallback that was never exercised.** The GL fallback was "verified across the matrix", but
  only the environment override had run. The fallback itself first ran under fault injection
  (`VK_LOADER_VENDOR_ID_FILTER=0x1234` for a driver with no GPU, `VK_DRIVER_FILES` at a missing
  file for no driver), and one of its two paths did not exist. Both are now UiTests.

### 2. State another API owns, and moves

Anything holding a handle to something Skia or a driver also tracks goes stale, silently.

- **Never cache a render target over a swapchain image.** Three images meant three good frames and
  then black for ever. **Never cache a renderer-owned `SKImage`** either.
- **Do not read the shared image through Vulkan.** Guessing its layout returned 0.000 from an image
  with a picture in it. Read it through GL, as `HULTUI_VIDEO_HDR_PROBE` does.
- **Layouts Skia owns are assumed, not known.** The present barrier assumes
  `COLOR_ATTACHMENT_OPTIMAL`, and the hand-back barrier assumes `SHADER_READ_ONLY_OPTIMAL`, because
  SkiaSharp cannot report or be told a layout. That holds for today's drawing (linear sampling, no
  mipmaps). A Skia update that draws differently would break it with no error.
- **Handle ownership is asymmetric.** GL does not take ownership of a Win32 handle;
  `glImportMemoryFdEXT` consumes the fd. Backwards leaks on one platform and double-frees on the
  other.
- **`GL_DEDICATED_MEMORY_OBJECT_EXT` is required** for a dedicated allocation. Without it NVIDIA on
  Linux read every 4K texture as black, with no GL error. Windows happened to work.

### 3. Defaults that are not neutral

- **An omitted `MPV_RENDER_PARAM_DEPTH` means 8 bits and dithering**; mpv does not infer it from
  the format. **`internal_format = 0`** means 8 bits too.
- **`target-colorspace-hint` does nothing through the render API.** No render parameter names a
  colour space. The way in is the `target-prim`, `target-trc` and `target-peak` options, and
  without `target-peak` nothing exceeds 1.0.
- **mpv's default gamut mapping desaturates** wide-gamut colours into BT.709. `clip` keeps them.
- **Skia returns a null surface with no diagnostic** without `TRANSFER_SRC`, or when
  `GRVkExtensions` or `VkPhysicalDeviceFeatures2` are left null.
- **SDL's `SDR_white_level` is wrong on Wayland** (it reads 1.0), and **SDL's HDR flag is wrong on
  Windows** (headroom-based and stale).
- **The Windows loader (1.4.357) returns a pointer for commands of extensions that were not
  enabled.** A null check proves nothing about whether an extension may be used, and a test
  that leaves one out cannot reproduce the crash a stricter loader would give.

### 4. One platform hiding the other's bug

Each of these was invisible on the machine it was written on.

- **SDR white is a protocol constant on Wayland and a display query on Windows.**
- **KWin decodes sRGB with 2.2**, so white matches and shadows do not, and no colour-space tag can
  fix it.
- **`glXGetProcAddress` returns a stub for any name** under libglvnd; Skia parsed garbage from one
  and crashed every GL launch on X11.
- **Windows tolerates `SDL_GL_MakeCurrent` with a null context; Wayland dereferences it.**
- **A mixed-scale Wayland desktop reports an output change as two events.**
- **F16 is expensive on Windows and nearly free on NVIDIA Linux.** A cost measured on one says
  nothing about the other.

### 5. Waits that only a timeout ends

mpv breaks a deadlock with a 200 ms timeout and a line at the `v` log level, so the only symptoms
are a stall and a log nobody reads. All three were real.

- **A synchronous `mpv_set_property` on the render thread** waited for the core, which waited for
  the render: 200–360 ms per change of target colour.
- **A frame with no size to draw at**, because the size is read from the core, which was waiting
  for that frame. Reopening a file took three timeouts, about 600 ms.
- **A synchronous seek while the core waited for a frame.** Scrubbing froze the window for 200 ms
  on nearly every seek. At 30 ms between seeks the frame was drawn first, so a slow scrub test saw
  nothing.

The detector is mpv's `frame-drop-count`: a first frame that timed out counts as dropped. The
headless tests use mpv's software renderer, which has its own thread and cannot see any of these.
`MpvGLTests` in `HultUI.UiTests` run the GL path, where the UI thread renders.
