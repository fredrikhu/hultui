# HDR on Linux: a brief for a Claude Code session there

Everything in `docs/design/HDR.md` phases 1 to 4 is built and verified **on Windows only**.
This is the list of what to run on Linux, in order, with what each step is meant to prove and
what a failure would mean. Hand this file to a Claude Code session on the Linux machine and it
should be able to work from it without further context.

Phase 1 has its own older list in `docs/design/HDR-PHASE1-LINUX.md` — step 1 there passed on
Fedora 44 / Mesa 26.2.2, steps 2 to 4 have never run. **Do those first**; nothing below is
meaningful if the renderer cannot draw a window at all. This file covers phases 2, 3 and 4.

## Before anything

```bash
git pull && dotnet build HultUI.slnx
```

`libmpv` is needed for phase 4 and is found automatically as `libmpv.so.2` or `libmpv.so`; if
the distribution puts it somewhere unusual, set `HULTUI_MPV_PATH=/path/to/libmpv.so.2`. Without
it the sample shows its **Open…** screen instead of playing, which looks exactly like the
interop failing to start — so check that a video plays on the GL path first.

## Two rules about evidence, learned the hard way

**The headless driver proves nothing here.** It renders through `SkiaRasterRenderSurface` and
never touches a GPU backend, so a screenshot comparison between the GL and Vulkan paths is
identical whatever either one does. `HULTUI_VULKAN_CAPTURE=<path>` reads the presented swapchain
image back to a PPM and is the only honest evidence.

**A capture proves the image, not the screen.** A bug that made every frame after the third
black was invisible to a capture of frame 1 — while every swapchain image is still fresh, every
belief about it is true. The capture defaults to the first *reused* frame for that reason. If
something looks wrong, look at the window; if the window looks right, say so as a separate fact
from what the PPM holds.

## 1. Phase 2 and 3 — does this display offer a wide-colour swapchain?

The format is chosen from what the surface reports, and `DescribeGraphics` prints the whole
list. This is a read-only question and costs a few seconds.

```bash
HULTUI_RENDERER=vulkan dotnet run --project samples/HultUI.Samples.Minimal
```

Look at the `[HultUI] gfx:` line, specifically `surfaceFormats=[...]`, `hdrDisplay=` and
`sdrWhite=`. On Windows it reads:

```
format=B8G8R8A8Unorm colorSpace=SrgbNonLinear ... hdrMetadataExt=yes
surfaceFormats=[B8G8R8A8Unorm/SrgbNonLinear, ..., R16G16B16A16Sfloat/ExtendedSrgbLinearEXT,
                A2B10G10R10UnormPack32/Hdr10St2084EXT, ...]
```

**What matters is whether `ExtendedSrgbLinearEXT` appears at all.** It requires
`VK_EXT_swapchain_colorspace`, which the host enables whenever the loader has it. If the list
holds only `SrgbNonLinear` entries, phases 2 and 3 cannot run on this driver and that is the
finding — record it and stop at this step.

Then force the half-float path on regardless of what the display claims:

```bash
HULTUI_RENDERER=vulkan HULTUI_VULKAN_F16=1 HULTUI_VULKAN_CAPTURE=/tmp/vk-f16.ppm \
  dotnet run --project samples/HultUI.Samples.Minimal
```

**Expect the window to look the same as without `F16`.** On Windows, 99.0% of channels were
identical to the 8-bit path and the differences sat on antialiased edges. A window that is
uniformly *dim* is the specific Windows bug that phase 2 fixed with an offscreen pass, and it
should not appear here — see the next paragraph for why.

**Expect `hdrDisplay=off` and `sdrWhite=80nits` on Linux even on an HDR monitor.** Both come
from a Windows-only query (`WindowsSdrWhiteLevel`), and elsewhere the code returns scRGB's own
reference of 80, which makes the white scale exactly 1 and skips the offscreen pass entirely.
That is not a bug to fix blind: on Wayland the compositor owns this through the colour-management
protocol, and SDL's binding here exposes neither `SDR_WHITE_POINT` nor `HDR_HEADROOM`. **If the
F16 window is visibly dimmer than the GL window, that is the finding** — it means Linux needs an
equivalent of the Windows white-level query and phase 2's offscreen pass, and the number to hunt
for is what the compositor is compositing SDR white at.

X11 cannot do HDR at all. If `SDL_VIDEODRIVER=x11` reports HDR formats, distrust the report.

## 2. Phase 4 — the shared image, which is the interesting one

This is the newest work (commit `212e106`) and the half most likely to behave differently here,
because **the handle is a file descriptor rather than a Win32 handle**. Everything else is the
same code.

First the spike, which needs no window and isolates the driver question:

```bash
dotnet run --project spikes/HdrInterop
```

**Expect it to reach `VERDICT: interop works.`** It checks `GL_EXT_memory_object` and
`GL_EXT_memory_object_fd`, that the GL context and the Vulkan device report the same UUID, then
exports an image and has GL clear it and Vulkan read the colour back. This passed on Linux during
phase 0, so a failure now is a regression or a driver change, not new ground.

Then the per-frame cost, which decides whether the blunt synchronisation is good enough here as
it was on Windows:

```bash
dotnet run --project spikes/HdrInterop -- --syncbench
```

**On Windows/RTX 4080 this is 0.27 ms a frame at 1080p and 0.29 at 4K — flat across resolutions,
because it is the cost of a command-buffer submit and a queue wait rather than of the image.**
That is 7 ms of every second at 24 fps, against roughly 175 ms/s for the copy it replaces. If
Linux comes back in the same range, nothing more is needed. **If it comes back an order of
magnitude worse, that is a real finding**: it would mean `VK_KHR_external_semaphore` with
`GL_EXT_semaphore` is a precondition on this driver rather than the optimisation the Windows
measurement showed it to be. Append the numbers to `spikes/HdrInterop/results/linux.txt`.

Now the real thing. Make a test clip whose halves differ, so the capture proves orientation as
well as content — an upside-down frame is the most likely way for this to be subtly wrong:

```bash
python3 - /tmp/split.y4m <<'PY'
import sys
w, h, fps, frames = 320, 180, 30, 300
with open(sys.argv[1], "wb") as f:
    f.write(f"YUV4MPEG2 W{w} H{h} F{fps}:1 Ip C420jpeg\n".encode())
    half = w * h // 2
    luma = bytes([235]) * half + bytes([16]) * half
    cw, ch = w // 2, h // 2
    u = bytes([84]) * (cw * ch // 2) + bytes([200]) * (cw * ch // 2)
    v = bytes([255]) * (cw * ch // 2) + bytes([100]) * (cw * ch // 2)
    frame = b"FRAME\n" + luma + u + v
    for _ in range(frames):
        f.write(frame)
PY
```

```bash
HULTUI_RENDERER=vulkan HULTUI_STARTUP=1 HULTUI_VULKAN_CAPTURE=/tmp/vk-video.ppm \
  HULTUI_VULKAN_CAPTURE_FRAME=60 \
  dotnet run --project samples/HultUI.Samples.VideoPlayer -- /tmp/split.y4m
```

**Two lines in the startup trace are the proof the interop is running:**

```
[startup] vulkan interop ready on <your GPU>
[startup] vulkan interop image 320x180 RGBA16F (655360 bytes)
```

Anything else means it declined, and it says why on stderr —
`[vulkan] video stays on the software path: …`. That path is not a crash: the video still plays
through the pixel ring, which is what it did before phase 4. **A declined interop with a working
video is a perfectly good result to report**, and the reason is the whole finding.

Then look at the capture. The top half should be bright and reddish, the bottom half dark and
blue. Upside down means the `BottomLeftOrigin` mapping in
`SkiaVulkanRenderSurface.WrapFrame` is wrong for this driver; swapped red and blue means the
colour type is.

Finally compare the two paths on one clip, which is what the off-switch exists for:

```bash
HULTUI_RENDERER=vulkan HULTUI_VULKAN_INTEROP=0 dotnet run --project samples/HultUI.Samples.VideoPlayer -- /tmp/split.y4m
```

**Measuring the CPU difference is an open question, not a solved one.** On Windows the readings
swung between 39 and 276 ms of CPU per second and no ratio could honestly be read off them,
because an uncompressed 1080p y4m is about 75 MB/s of disk reads — larger than the copy being
measured. If `ffmpeg` is available on the Linux box, **encode a compressed 1080p clip first**
and measure with that; that single change is probably enough to settle the number that the whole
phase was justified by. Something like:

```bash
ffmpeg -f lavfi -i testsrc2=size=1920x1080:rate=24 -t 30 -c:v libx264 -preset veryfast /tmp/test1080.mp4
```

and then `/usr/bin/time -v` or reading `/proc/<pid>/stat` over a fixed window, with and without
`HULTUI_VULKAN_INTEROP=0`.

## The Linux-specific things to watch

In rough order of how likely they are to matter:

- **File-descriptor ownership.** `glImportMemoryFdEXT` **consumes** the fd, where the Windows
  side has to `CloseHandle` after importing because GL does not take ownership of an
  `OPAQUE_WIN32` handle. The code already follows both rules
  (`SdlVulkanHost.Interop.ImportIntoGL`), but a leak here shows up as running out of descriptors
  after many resizes, not as an immediate failure. Resize the video window repeatedly and watch
  `ls /proc/<pid>/fd | wc -l`.
- **A second GPU.** On a laptop the hidden GL context can land on the integrated GPU while
  Vulkan picked the discrete one. The code checks the device UUIDs and declines, so the symptom
  is a clean `[vulkan] video stays on the software path: GL is on a different GPU than Vulkan`
  rather than a crash — but on a hybrid machine that is the expected outcome and worth stating.
- **Mesa versus the NVIDIA proprietary driver.** They are different implementations of every
  extension involved here. If one works and the other does not, that is the single most useful
  thing this exercise can find. Say which driver each result came from.
- **Wayland versus X11.** Run the phase 4 steps under both; `SDL_VIDEODRIVER=wayland` and
  `SDL_VIDEODRIVER=x11`. The hidden GL window is created with SDL while the real window is a
  Vulkan one, and whether a compositor is happy with that pair is not something Windows can say
  anything about.
- **Validation.** Worth one run, because on Linux it goes to stdout where on Windows it
  disappears into `OutputDebugString`:

  ```bash
  VK_LOADER_LAYERS_ENABLE=*validation* HULTUI_RENDERER=vulkan \
    dotnet run --project samples/HultUI.Samples.VideoPlayer -- /tmp/split.y4m
  ```

  The queue-family transfer in `FinishExternalFrame` claims the image comes back from
  `VK_QUEUE_FAMILY_EXTERNAL` in `GENERAL` layout, which is the convention when no shared
  semaphore carries a layout. **If validation complains about that barrier, quote it verbatim** —
  it is the part of phase 4 that is convention rather than certainty.

## What to send back

Commit the results into the repo rather than pasting them, so they survive:

- Append the `--syncbench` numbers and the `dotnet run --project spikes/HdrInterop` output to
  `spikes/HdrInterop/results/linux.txt`, saying which driver and display server.
- Add findings to this file under a **Results** heading, the way
  `docs/design/HDR-PHASE1-LINUX.md` records its step 1.
- Keep the PPMs; convert with `magick /tmp/vk-video.ppm /tmp/vk-video.png` if something needs
  looking at.

A step that fails is as useful as one that passes, provided the trace says how far it got. What
is not useful is a green result inferred from something that could not have shown a failure —
if a check was skipped, say it was skipped.
