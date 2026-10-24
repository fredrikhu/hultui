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
- **Resize:** while a drag sizes the window (Win32's modal size/move loop, from its first size
  change), the swapchain is allocated to a 256-pixel bucket and presented one-to-one through
  `swapchain_maintenance1`, so the drag rebuilds only when it crosses a bucket; when the loop ends
  it is rebuilt at the window's exact size, which video needs (a larger extent loses the
  compositor's direct path). A title-bar move runs the same loop and builds nothing; snap,
  maximize and a program's resize rebuild for every size. Only under MAILBOX: under FIFO a
  one-to-one swapchain stalled for good, and under IMMEDIATE a drag's first one cost about 180 ms.
  `HULTUI_VULKAN_FIXED_EXTENT=1` over-allocates always, `=0` never. The half-float offscreen is
  made at the over-allocated size too (`VulkanFrameTarget.AllocatedWidthPx`), clipped to the
  window, and kept with the swapchain: remade at every size, an 80-step drag made 81 offscreens,
  each with a synchronous submit and a scratch purge, against 5 swapchains.
- **Rebuild:** the old swapchain is handed to the new one as `oldSwapchain`, except the scRGB one
  under MAILBOX on Windows, which is destroyed first. Handed over, it made the next present wait
  about 100 ms on NVIDIA, and the compositor showed the old image scaled meanwhile. IMMEDIATE and
  FIFO never waited, and destroying first made their resizes slower (5 and 12 ms against 29 and
  33); the 8-bit swapchain never waited either and gained nothing. They keep the chained path.
- **Minimized (Windows):** the surface's extent is 0×0, so no swapchain can exist. The one the
  window had is destroyed, by every present mode, and the first frame after the restore builds a
  new one. The loop does not draw a minimized window, so only a forced frame, a display change or
  a present caught by the minimize gets here. Built during the restore, the scRGB MAILBOX
  swapchain takes 150–220 ms to create (9 ms at start-up, 23 on a resize; 8-bit 1, scRGB FIFO 24).
- **Synchronisation:** a semaphore per image orders the present after the frame. The acquire waits
  on a fence, because SkiaSharp exposes nothing to hand a semaphore to; it costs 0.0064 ms a frame.
- **Layouts:** the present transition moves the image to `PRESENT_SRC_KHR` behind Skia's back.
  When the renderer keeps a render target over the image, it asks the host to move the image back
  to `COLOR_ATTACHMENT_OPTIMAL`, the layout Skia left it in, in a small submit of its own, from
  `UNDEFINED`, discarding what every frame overwrites anyway (`TransitionAcquiredImage`).
- **Before the images go** — any rebuild, a minimized window's swapchain, the host's disposal —
  the host calls the renderer, which drops its render targets over them first
  (`IVulkanSurfaceHost.SetSwapchainReleasing`).
- **Per window:** each host has its own instance, device and command table. Device-level function
  pointers are never shared between windows.

The choices themselves are `VulkanSwapchainChoice`, unit-tested without a GPU.

### Drawing the UI (`SkiaVulkanRenderSurface`)

**One render target per swapchain image, kept for the swapchain's life** (2026-10-11). Skia
tracks the image's layout inside it and the host moves the layout behind Skia's back, so the host
moves it back (above) before any of Skia's work on the image is submitted. Built every frame, the
target cost 2,048 bytes of managed allocation, an image view and a framebuffer a frame, all of an
idle-animating window's allocation; kept, a steady frame allocates nothing. A target is made again
when the image, its size, format or colour space changes, which is how the scaled colour space
follows the white scale. **When the barrier is submitted matters**, and `SwapchainTargetUses`
decides it:

| Frame | Target | Host barrier |
|---|---|---|
| through the offscreen | kept | just before the blit, after any GL draw in the frame |
| straight onto the swapchain | kept | at once, before anything is drawn |
| straight onto the swapchain, after a frame that drew a GPU source | this frame's only, declared `UNDEFINED` | none |

A submit of the host's on the Vulkan queue just before mpv's GL draw of a video frame made that
draw slower on NVIDIA (610): Reel at 1080p24, Render averaged 1.25–1.47 ms a frame against
0.97–1.14 without the submit, on both formats. After the GL draw it costs nothing, but that is
safe only where nothing of Skia's on the swapchain image can be submitted earlier: the offscreen
path. Drawing straight onto the swapchain, Skia may submit its work on the image at any point, so
the barrier goes first, and a frame with a video wraps its own target instead, as every frame did
before. So a video on an 8-bit swapchain still allocates the 2 KB a frame; on an scRGB one with a
white scale above 1 (Windows HDR, where Reel and Aperture play HDR video) it does not.

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

**An offscreen that is let go is freed**, on a resize or when the route no longer needs one. Skia
keeps a disposed surface's texture in its scratch pool for a later surface of exactly that size,
and a resized window never asks for that size again: every size a drag passed through stayed in
GPU memory, width × height × 8 bytes each, until the context's 256 MB budget pushed it out: DXGI
read 458 MB after a 40-step drag, 243 now. So the pool's unused scratch is purged after the next
frame is submitted (2026-10-08), which keeps what that frame drew with.

**So are the textures of disposed images** (2026-10-11). A disposed image's upload loses its key
and stays in the same pool as a spare: eight 3840×2400 photos drawn and disposed held 176 MB, and
DXGI read 391.5 MB against 213.2 after the purge. The surface purges after a frame once
`SkiaImageSource`s that were drawn have been disposed, at most once a second. Only spares go, so
a live image scrolled out of view keeps its texture and is not uploaded again. GL does not purge:
there Skia freed 211 MB and DXGI read 303.7 MB before and 312.0 after, the driver keeping it.

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

**First run on Mesa and Intel (2026-10-03,** Fedora 44, Intel UHD 770, Mesa 26.2.3, Fedora's
`mpv-libs` 0.41 with `ffmpeg-free` 8.1, the Vulkan renderer on sway/Wayland): the shared image
works. mpv's hidden GL context imported its RGBA16F image with semaphore ordering ("vulkan interop
ready … (semaphore ordering)"), and a magenta clip (lavfi, and a VP9 file) read back from the
swapchain as (254, 0, 253), beside a CEF page in a second shared image (seam W7, the Vulkan probe's
`--page-and-video`). Decode was software here: `hwdec` asked `auto-safe` and used `no`, because
this container has no VA-API driver (`vaInitialize failed`). Intel's own VA-API path is not
measured. The GL renderer played the same clip.

### mpv's threads

**The UI thread is mpv's render thread, and it never waits on mpv's core.** After every load and
seek, the core waits up to 200 ms for the first frame to be drawn. So every command from the UI
thread is queued (`mpv_command_async`, `mpv_set_property_async`), and a frame that arrives before
the video's size is known is taken without drawing (`MPV_RENDER_PARAM_SKIP_RENDERING`) and drawn
once the size arrives.

## Invariants

The rules the design depends on. Each one was broken once, and most failed silently.

- **A render target kept over a swapchain image needs the image back in the layout Skia left it
  in** before any of Skia's work on it is submitted, and has to go before its image does: the
  host's `TransitionAcquiredImage` and its call before the images go, both promised through
  `SetSwapchainReleasing`. Without them, wrap the image every frame.
- **The wrapper over a shared video image is kept only while every frame lends the image first.**
  The lend's acquire barrier (`FinishExternalFrame`) puts it in `SHADER_READ_ONLY_OPTIMAL` on the
  graphics queue, the state the wrapper declares, before Skia's work on it is submitted, and Skia
  only samples it. The surface makes it again when the host may have replaced the image (a new
  size, a failed lend, the source let go) or what it declares changes.
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
| Fixed extent | during a drag (Windows, MAILBOX) | a drag step without a rebuild; outside a drag it would cost 4K video about 70% | `HULTUI_VULKAN_FIXED_EXTENT` |
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
- **Over-allocating during a drag** (2026-10-11): a step of an 80-step drag went from 32.9–38.1
  to 5.6–8.2 ms median (probe window, Aperture Home and Reel; JIT and AOT; GL Aperture 3.0–5.5),
  swapchain builds from 80 to 5, plus 30–36 ms for the exact rebuild and its frame at the
  release, and nothing for a title-bar move. Reel playing 1080p60 dropped 0–2 frames in a drag
  instead of 35–36, and 1080p120 11–12 instead of 228–241.
- **The fixed extent all the time**, in its own A/B (2026-09-27): 10 swapchain builds instead of
  122, and resize CPU from 801 to 466 ms/s (GL 264). It costs 4K video 91–95 ms/s against 48–63,
  which is why it is only used during a drag.
- **Resize CPU under MAILBOX is driver-side.** Halving the frames drawn during a resize left it
  unchanged. Most of it, on the scRGB swapchain, was a 100 ms wait in the first present after
  each rebuild; destroying the old swapchain first (2026-10-08) took a 40-resize probe from
  453–480 to 198–206 ms/s, and a resize from 112 to 29 ms. The row above predates that.

## Not done

- **Mesa, AMD, Intel, a hybrid GPU.** No machine here has one.
- **Linux checks still open:** negative scRGB (wide gamut) on KWin, the indexed device-UUID query
  on Mesa, the scaled colour space, and the GL fallback, whose automated tests are Windows-only.
- **Zero-copy on AMD and Intel under Windows.** Their only bridges are 8-bit, or need ANGLE, which
  cannot import Vulkan memory.
- **Start-up's +127 ms.** It is pipeline compilation for the F16 target, and SkiaSharp exposes no
  persistent pipeline cache.
- **Over-allocating during a drag on AMD, Intel and Linux.** Measured on NVIDIA 610.62 only. Linux
  has no modal size loop to frame a drag and keeps the extension off unless
  `HULTUI_VULKAN_FIXED_EXTENT=1` asks.
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
- **Task Manager's dedicated GPU memory is bookkeeping, not memory.** Each scRGB swapchain handed
  over as `oldSwapchain` stayed in the process's figure for good: 20.5 GB on a 16 GB card after 650
  resizes, while nvidia-smi's memory in use for the whole GPU and DXGI's `QueryVideoMemoryInfo` for
  the process stayed flat and nothing failed. Ask nvidia-smi or DXGI before calling a GPU leak.
- **`GRContext.GetResourceCacheUsage` counts only budgeted resources.** Created unbudgeted, the
  scRGB offscreen dropped out of it (19.7 to 10.4 MB) while DXGI read the same 130.6 MB, and once
  disposed Skia budgeted it and pooled it all the same. `DumpMemoryStatistics` lists every
  resource, live or purgeable; read it, or DXGI, before calling a Skia change a saving.
- **Where a one-off lands decides whether a benchmark sees it.** The first swapchain a window
  replaces after it is first shown takes 190–290 ms to create on NVIDIA 610, on every path (a
  start-up build takes 9 ms, a later rebuild about 21). A drag benchmark that places the window
  first pays it before the drag. With `HULTUI_VULKAN_FIXED_EXTENT=1` the placement fitted the
  start-up bucket, so the payment moved into the drag and read as a 200 ms outlier of the change.
- **A capture taken after `SetWindowPos` returns cannot see the resize.** The window procedure draws
  the new size before it returns, so a stall inside that frame is over by the time anything looks.
  On the chained scRGB MAILBOX swapchain the old harness read 0 of 80 resizes stretched, while a
  thread sampling the screen during them found 72% of the composed frames scaled. Sample the
  screen while the resize is happening.
- **A frame per event is not a frame per change.** A `SetWindowPos` step raises RESIZED and
  PIXEL_SIZE_CHANGED for one size, a growing step a WM_PAINT besides, and SDL's live-resize timer
  sends an EXPOSED every ~10 ms while an edge is held or the window is moved. The event watch drew
  a whole frame for each: three a step at one size (2026-10-10: one now; the timer draws only a
  window with something new). The converse holds too: more frames than resizes says nothing about
  whether the frame at the new size was drawn. Count the steps that drew none.
- **A fallback that was never exercised.** The GL fallback was "verified across the matrix", but
  only the environment override had run. The fallback itself first ran under fault injection
  (`VK_LOADER_VENDOR_ID_FILTER=0x1234` for a driver with no GPU, `VK_DRIVER_FILES` at a missing
  file for no driver), and one of its two paths did not exist. Both are now UiTests.

### 2. State another API owns, and moves

Anything holding a handle to something Skia or a driver also tracks goes stale, silently.

- **A render target kept over a swapchain image goes stale unless its layout is put back.** Kept
  with nothing more, three images meant three good frames and then black for ever. It is kept now
  only because the host's barrier hands the image back in `COLOR_ATTACHMENT_OPTIMAL` first
  (2026-10-11), proven by captures of images reused a dozen times each, across resizes, a
  minimize, both formats and all three present modes, each showing the frame just drawn, with
  the validation layer loaded and silent (`VulkanProbeTests`). **The captures alone could not have
  failed**: with the barrier left out, NVIDIA 610 drew every captured frame right, and only the
  layer said what was wrong (`VUID-vkCmdDraw-None-09600`, an image in `UNDEFINED` or
  `PRESENT_SRC_KHR` where the target expects `COLOR_ATTACHMENT_OPTIMAL`). The `SKImage` over a
  shared video image is kept too (2026-10-24), because the host's acquire barrier hands the image
  back in `SHADER_READ_ONLY_OPTIMAL` on every frame that draws it: Reel playing a 120 fps clip
  through a drag, a move and a held drag, the layer loaded (its deliberate faults reported) and
  silent; a clip that brightens frame by frame shows a brighter picture in each later capture
  (`VulkanProbeTests`).
- **Do not read the shared image through Vulkan.** Guessing its layout returned 0.000 from an image
  with a picture in it. Read it through GL, as `HULTUI_VIDEO_HDR_PROBE` does.
- **Layouts Skia owns are assumed, not known.** The present barrier assumes
  `COLOR_ATTACHMENT_OPTIMAL`, the host's other barrier restores it for a render target that
  believes it, and the hand-back barrier assumes `SHADER_READ_ONLY_OPTIMAL`, because SkiaSharp cannot
  report or be told a layout. That holds for today's drawing (linear sampling, no mipmaps, no
  read of the swapchain image). A Skia update that draws differently would break it with no
  error; the validation layer names a wrong layout at once.
- **Handle ownership is asymmetric.** GL does not take ownership of a Win32 handle;
  `glImportMemoryFdEXT` consumes the fd. Backwards leaks on one platform and double-frees on the
  other.
- **`GL_DEDICATED_MEMORY_OBJECT_EXT` is required** for a dedicated allocation. Without it NVIDIA on
  Linux read every 4K texture as black, with no GL error. Windows happened to work.
- **A swapchain forgotten is not a swapchain destroyed.** Minimized, the rebuild found no area and
  set the handle to null. The orphan still owned the surface: the restored window drew nothing,
  for ever, and a resize failed with `ErrorNativeWindowInUseKHR`. The validation layer named it
  only at device destruction, as a leaked `VkSwapchainKHR` (`VUID-vkDestroyDevice-device-05137`).

### 3. Defaults that are not neutral

- **A swapchain larger than its window is SUBOPTIMAL on every present** (NVIDIA 610). Answered
  with a rebuild, as an exact swapchain's SUBOPTIMAL is, the over-allocated swapchain was built
  again after every frame, and `HULTUI_VULKAN_FIXED_EXTENT=1` made a drag step slower than no fixed
  extent at all. A one-to-one swapchain the window still fits keeps going now.
- **Only MAILBOX shows a one-to-one swapchain well.** Under FIFO it stalled for good: a few drags
  in, an acquire's fence never signalled and the window hung, on both formats, with the validation
  layer and without. Under IMMEDIATE the half-float window paid 172-196 ms for the first one-to-one
  swapchain of every drag (its first present when handed over, its creation when destroyed first)
  and showed the old image stretched meanwhile. MAILBOX ran 30 drags, 45 one-to-one swapchains,
  with neither. The other modes rebuild for every size.

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
