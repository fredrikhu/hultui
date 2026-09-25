# Phase 1 on Linux: what to run

The Vulkan renderer works on Windows — an app on `HULTUI_RENDERER=vulkan` draws correctly
and the captured swapchain image is the real UI. **None of it has run on Linux.** The
phase 0 spike proved the memory sharing there; the renderer itself has not been started.

This is the list, cheapest first. Run it from a checkout of `main`.

## Results so far (2026-09-25, RTX 4080, Fedora 44, Wayland, Mesa 26.2.2)

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

**Steps 2 to 4 are still to run.**

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
