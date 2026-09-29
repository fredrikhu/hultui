# Phase 1 on Linux: what to run

The Vulkan renderer works on Windows — an app on `HULTUI_RENDERER=vulkan` draws correctly
and the captured swapchain image is the real UI. **None of it has run on Linux.** The
phase 0 spike proved the memory sharing there; the renderer itself has not been started.

This is the list, cheapest first. Run it from a checkout of `main`.

**Phases 2 to 4 have their own brief:** `docs/design/HDR-LINUX-BRIEF.md`. Finish steps 2 to 4
here first — none of that is meaningful until the renderer can draw a window at all.

## Results so far (2026-09-25, RTX 4080, Fedora 44, Wayland, Mesa 26.2.2)

The raw transcript and the captures it names (`vk.png`, `vk-wl.png`, `vk-x11.png`,
`vk-validation.png`) are in `spikes/HdrInterop/results/linux-phase1/`. They were committed at the
repository root at the time and moved there by the review fixes (plan 15, item 7.4).

**Step 1 passed, identically to Windows** — the same eight `ok` and the same two expected
failures. So `VK_IMAGE_USAGE_TRANSFER_SRC_BIT` is Skia's requirement rather than anything
specific to a driver or a platform, which is worth knowing: the error message in
`SdlVulkanHost` can be stated as fact.

**Step 2 segfaulted**, and the core dump named it in two frames:

```
#0  Wayland_GLES_MakeCurrent (libSDL3.so)
#1  SDL_GL_MakeCurrent_REAL  (libSDL3.so)
```

`SDL3Window.MakeCurrent()` called `SDL_GL_MakeCurrent` unconditionally, and on a Vulkan
window there is no GL context to make current. SDL takes the null straight into the
backend; Wayland dereferences it, Windows happens to tolerate it. Fixed — the method
returns early when there is no context, and `BeginPreload` no longer warms a GL driver a
Vulkan window will never use.

**Correction to the heading:** the Vulkan driver on that machine is the **NVIDIA proprietary
driver 615.71.09**. Mesa 26.2.2 is installed but provides only llvmpipe; it drove none of
these runs.

### Steps 2 to 4 (2026-09-25, same machine, KDE Plasma 6, both monitors HDR)

**Step 2 passed.** White window, indigo button, black text, via both the frame-3 capture and
the default capture; the startup trace has `vulkan swapchain 750x450 x3`. Channel values match
the 8-bit path within 1 LSB (button `79,71,229` against `79,70,229`).

One thing the brief did not predict: **the default capture never fired on an idle Minimal
window.** It waits for frame `images + 1` (4 on this 3-image swapchain), and a window that is
left alone presents only 3 frames here. Hovering the button was enough to get frame 4. So the
claim in `SdlVulkanHost.Capture.cs` that the default is "still reachable in an app that
renders only when dirty" needs an input event to be true on Wayland.

**Step 3 passed on both display servers.** `SDL_VIDEODRIVER=x11` gives a 4-image
`B8G8R8A8Unorm` swapchain and the same picture. Whether the `currentExtent == 0xFFFFFFFF`
branch ran on Wayland was not traced directly; the swapchain matched the drawable size
through every rebuild below, which is what that branch has to get right.

**Step 4 passed.** Minimal under `VK_LOADER_LAYERS_ENABLE=*validation*`, window dragged by
hand: **412 swapchain recreations, zero validation messages**, and the capture taken after
the last resize (1010x350) shows the right UI with the click count it had reached. The layer
was confirmed loaded into both the instance and device chains (`VK_LOADER_DEBUG=layer`),
and the app installs no debug messenger, so the layer's default of printing errors and
warnings to stdout was in force. The zero is real.

### A bug found along the way: Wayland shrinks the window on a mixed-scale desktop

Not Vulkan's and not HDR's: GL does the same. On this desktop DP-2 (primary) is at scale 1.0
and DP-3 at 1.5. SDL creates the window believing the scale is 1.5, KWin places it on the
1.0 output, and SDL reports that as two separate events:

```
UpdateSize px=750x450 density=1.5 displayScale=1.5   (creation)
UpdateSize px=500x300 density=1   displayScale=1.5   (pixels change, scale not yet)
UpdateSize px=500x300 density=1   displayScale=1     (scale-only change → resize)
UpdateSize px=333x200 density=1   displayScale=1
```

The second event leaves a 333-DIP layout (500 px ÷ a stale 1.5). The third looks like a
pure DPI change to the `scaleOnlyChange` branch of `SDL3Window.UpdateSize`, which "preserves"
that already-wrong 333-DIP size by calling `SDL_SetWindowSize`. A 500x300 window ends up as
333x200. It happened on every scripted launch (6/6); in the one interactive run the window
kept 750x450, presumably because KWin put it on the 1.5 output that time, which fits the
explanation. A frame-3 capture races the shrink,
which is why an early capture came out at 333x200 and looked like an F16 bug. On Wayland,
window coordinates are already logical, so the compositor owns DIP preservation, and that
branch probably should not run there at all.

**A second bug, found on Windows by looking at the window** (2026-09-25): the renderer drew
the first three frames and every frame afterwards black, which no capture of frame 1 could
have seen. `SkiaVulkanRenderSurface` cached one wrapped surface per swapchain image; Skia
tracks a render target's image layout inside the target, and this backend transitions the
image to `PRESENT_SRC_KHR` on the way to the screen without a way to tell Skia so —
SkiaSharp exposes no `setVkImageLayout`. On reuse, Skia transitioned from a layout the image
no longer had, and the contents became undefined. Fixed by building the target fresh each
frame declaring `UNDEFINED`, which is always true and costs one validation. The capture now
defaults to the first *reused* frame for the same reason.

## 1. The bisect — no window, a minute, proves Skia on your driver

```bash
dotnet run --project spikes/HdrInterop -- --bisect
```

**Expect eight `ok` and the last two `FAILED`.** Those two failures are the control, not a
problem: they are the rows that deliberately leave out `VK_IMAGE_USAGE_TRANSFER_SRC_BIT`,
which is what phase 1 turned out to require.

If those two **pass** on Linux, that is worth knowing — it would mean the requirement is
specific to NVIDIA on Windows rather than to Skia, and the error message in
`SdlVulkanHost` should say so.

If earlier rows fail, Skia's Vulkan backend does not work on this driver at all and phase 1
stops there.

## 2. The renderer

```bash
HULTUI_RENDERER=vulkan HULTUI_VULKAN_CAPTURE=/tmp/vk.ppm HULTUI_STARTUP=/tmp/vk-start.txt dotnet run --project samples/HultUI.Samples.Minimal
```

Close the window after a second or two, then look at what it drew:

```bash
magick /tmp/vk.ppm /tmp/vk.png && cat /tmp/vk-start.txt
```

**A white window, an indigo "Click me" button, black text.** The startup trace should
contain a `vulkan swapchain <w>x<h> x<n>` line.

**If `/tmp/vk.ppm` does not exist, the renderer drew nothing.** That is the failure phase 1
spent an afternoon on: everything reports success, no frame is ever produced. The startup
trace says how far it got, and `SKSurface.Create` returning null is the usual reason.

Compare against the GL path, which should look identical:

```bash
HULTUI_STARTUP=/tmp/gl-start.txt dotnet run --project samples/HultUI.Samples.Minimal
```

## 3. Both display servers

The `currentExtent == 0xFFFFFFFF` branch in `BuildSwapchain` — "you choose the size" — only
ever runs on Wayland. On X11 the compositor dictates the extent. **That branch has never
executed anywhere.**

```bash
SDL_VIDEODRIVER=wayland HULTUI_RENDERER=vulkan HULTUI_VULKAN_CAPTURE=/tmp/vk-wl.ppm dotnet run --project samples/HultUI.Samples.Minimal
```

```bash
SDL_VIDEODRIVER=x11 HULTUI_RENDERER=vulkan HULTUI_VULKAN_CAPTURE=/tmp/vk-x11.ppm dotnet run --project samples/HultUI.Samples.Minimal
```

## 4. Resize, under validation

**The window has never been resized on either platform.** `RecreateSwapchain` is the
least-tested code in the backend and the most likely thing to break — destroying a swapchain
while Skia still holds render targets over its images is a use-after-free that a release
build will happily run.

Validation output goes to stdout on Linux, where on Windows it vanishes into
`OutputDebugString` — so this run is worth more on your machine than anything that can be
done on mine.

```bash
VK_LOADER_LAYERS_ENABLE=*validation* HULTUI_RENDERER=vulkan dotnet run --project samples/HultUI.Samples.Minimal
```

Drag the window edge around for a few seconds. Anything printed is a finding.

## What would not be surprising

In rough order of likelihood:

- **Resize.** Untested everywhere. Expect validation errors about images in use, or a hang
  in `DeviceWaitIdle`.
- **Wayland's extent branch.** Never executed.
- **The format.** Mesa and NVIDIA-Linux may offer `R8G8B8A8_UNORM` where Windows gave BGRA,
  which exercises the `Rgba8888` branch of `ColorTypeFor` — also never run. A frame with red
  and blue swapped means that mapping is wrong rather than missing.
- **`supportedUsageFlags`.** The swapchain now insists on `COLOR_ATTACHMENT | TRANSFER_SRC`
  and throws naming the actual mask if the surface will not give both. A Mesa surface that
  refuses `TRANSFER_SRC` would stop phase 1 dead and is worth knowing early.

## What to send back

The contents of `/tmp/vk-start.txt`, the PPM (or a PNG of it), and anything validation
printed. A failure that produces no PPM is as useful as one that does — the startup trace
is the evidence either way.
