# HDR on Linux: a brief for a Claude Code session there

Everything in `docs/design/HDR.md` phases 1 to 4 is built and verified **on Windows only**.
This is the list of what to run on Linux, in order, with what each step is meant to prove and
what a failure would mean. Hand this file to a Claude Code session on the Linux machine and it
should be able to work from it without further context.
**Traps found by this work live in one place** — the Traps section of
`docs/design/HDR.md`, grouped by shape. Anything learned here belongs there too,
not only in the results below.


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

## Results (2026-09-25)

**Machine:** RTX 4080 on the **NVIDIA proprietary driver 615.71.09**, Vulkan 1.4.351, Fedora 44,
KDE Plasma (KWin) Wayland. There are two monitors, both in HDR mode: DP-2 is primary at scale
1.0 with SDR white at 408 nits, and DP-3 is at scale 1.5 with SDR white at 455 nits
(`kscreen-doctor -o`). No Mesa hardware driver was involved (Mesa here is llvmpipe only), and
there is no second GPU, so the hybrid-GPU and Mesa-versus-NVIDIA questions were **not tested**.
The phase 1 steps 2–4 results are in `HDR-PHASE1-LINUX.md`.

### Phases 2 and 3 — works end to end on Wayland, and **the picture is dim**

- **Wayland offers wide colour.** The surface lists 28 formats, including
  `R16G16B16A16Sfloat/ExtendedSrgbLinearEXT`, `A2B10G10R10UnormPack32/Hdr10St2084EXT`,
  `DisplayP3LinearEXT` and `Bt2020LinearEXT`. That list is the same with HDR on or off.
- **SDL reports HDR on Wayland.** With the monitors in HDR mode, `SDL_PROP_DISPLAY_HDR_ENABLED_BOOLEAN`
  is true, so `hdrDisplay=on`, and **phase 3 picks scRGB by itself** with no `HULTUI_VULKAN_F16`
  (`vulkan swapchain 750x450 x3 R16G16B16A16Sfloat/ExtendedSrgbLinearEXT`). With HDR off it
  reports `hdrDisplay=off` and picks `B8G8R8A8Unorm`, which is correct.
- **`sdrWhite=80nits`, `whiteScale=1`, as the brief predicted**, so the offscreen pass is skipped.
- **The F16 window is visibly dimmer than the GL window** (compared side by side on the same
  monitor, by eye). **This is the finding the brief asked for.** The capture is *not* dim: F16
  and 8-bit captures agree within 1 LSB. So the image is right and the scale is wrong. KWin
  composites SDR white at 408/455 nits, and we send scRGB 1.0 = 80 nits, which is ~5× below it
  if KWin reads scRGB at its spec value. Linux needs its own equivalent of
  `WindowsSdrWhiteLevel`. The number to feed it is the compositor's SDR white, which KWin knows
  per output. SDL 3 has `SDL_PROP_WINDOW_SDR_WHITE_LEVEL_FLOAT` / `HDR_HEADROOM` for this, which
  the binding here does not expose.
- **The white scale that matches GL is ~2.54, not 408/80.** Measured by eye with GL and
  Vulkan side by side and `HULTUI_VULKAN_WHITE_SCALE` forced:

  | whiteScale | Result | Monitor |
  |---|---|---|
  | 1 (the default here) | visibly dimmer | not recorded |
  | 2.0 | slightly dimmer | not recorded |
  | **2.54** (203/80) | **looks the same** | **DP-3** (455 nits SDR, scale 1.5) |
  | **2.54** (203/80) | maybe ever so slightly dimmer | **DP-2** (408 nits SDR, scale 1.0) |
  | 5.1 (408/80) | brighter: button brighter, text thinner | not recorded |

  (Monitor names come from `kscreen-doctor -o`. The person comparing had KDE's primary and
  secondary swapped after an update, so only the 2.54 rows, which were re-mapped afterwards,
  carry a monitor. The others were asked for on DP-2 but may have been on either.)

  So KWin does not read scRGB 1.0 as 80 nits of *its* SDR white. The two monitors pointed to a
  fixed scale: one proportional to the brightness setting would match on DP-3 at 2.54 and need
  only 2.28 on DP-2, so the same window would look slightly *brighter* there, and it looked, if
  anything, slightly dimmer.

- **The Wayland colour-management protocol states the number: 2.5375.** From
  `wp_color_manager_v1.create_windows_scrgb` in `color-management-v1.xml`:

  > Nominal color value R=G=B=0.0 corresponds to BT.2100/PQ system 0 cd/m², and R=G=B=1.0
  > corresponds to BT.2100/PQ system 80 cd/m². […] The reference white level of Windows-scRGB is
  > unknown. If a reference white level must be assumed for compositor processing, it should be
  > R=G=B=2.5375 corresponding to 203 cd/m² of Report ITU-R BT.2408-7. […] it seems probable that
  > Windows implements both EGL_EXT_gl_colorspace_scrgb_linear and Vulkan
  > VK_COLOR_SPACE_EXTENDED_SRGB_LINEAR_EXT as Windows-scRGB.

  The compositor maps an image's reference white onto each output's own SDR brightness. So on
  Wayland, for an `ExtendedSrgbLinearEXT` swapchain, **SDR white belongs at 2.5375 (203/80) on
  every monitor, whatever its brightness setting.** That is the value matched by eye, and it
  explains the per-monitor result. It is a protocol constant, not a per-display query: the
  fix for Linux is `whiteScale = 2.5375` when the video driver is Wayland and the swapchain is
  scRGB, rather than a port of `WindowsSdrWhiteLevel`. (It assumes the Vulkan WSI tags the
  surface as Windows-scRGB, which is what the NVIDIA driver's result here implies. Mesa's WSI
  may differ and was not tested.)

- **SDL's own white level is the wrong number for this path.** Read from the same bundled
  `libSDL3.so` (SDL 3.5.0) on Wayland, on the MPG321UX with HDR on:
  `SDL.window.SDR_white_level=1.0` and `SDL.window.HDR_headroom=1.0022`. The headroom is real
  compositor data (456 nits peak over 455 nits SDR, as `kscreen-doctor` reports). But the white
  level of 1.0 follows the *EGL* scRGB convention the spec contrasts with, where 1.0 is reference
  white, and feeding it in reproduces the dim picture. Read on one monitor only. The probe is a
  small ctypes script and was not committed.
- **Phase 2's offscreen pass runs cleanly on Linux.** The `whiteScale=5.1` run went through it
  under validation: zero messages, and the frame-4 capture is correct. (A PPM clips to 8 bits,
  so the capture cannot show the scale itself. Only the screen can.)
- **X11 cannot, and says so.** `SDL_VIDEODRIVER=x11` lists only `B8G8R8A8Unorm/SrgbNonLinear`
  and `B8G8R8A8Srgb/SrgbNonLinear`, and reports `hdrDisplay=off`, correctly.
- **Small bug: X11 plus `HULTUI_VULKAN_F16=1` rebuilds the swapchain for nothing.** Each
  display event logs `rebuilding for scRGB` and builds the same `B8G8R8A8Unorm` swapchain again
  (3 times at startup). `SdlVulkanHost.DisplayChanged` compares what is *wanted*
  (`WantsWideColor`) with what was *obtained* (`_format`), and when the surface cannot give F16
  the two never match. It also resets the capture counter each time. Harmless to the picture,
  but it should compare against what the format choice would produce.

### Phase 4 — works on this driver, both display servers

- **Spike:** `VERDICT: interop works.` Same UUID on both APIs, fd exported and imported, and
  0.25/0.5/0.75 read back exactly. See `spikes/HdrInterop/results/linux.txt`.
- **Syncbench: 0.16 ms a frame, at both 1080p and 4K**, lower than Windows' 0.27 on the same
  card. The blunt synchronisation is good enough here too.
- **The sample runs on the interop** on Wayland and on X11: `vulkan interop ready on NVIDIA
  GeForce RTX 4080/PCIe/SSE2` and `vulkan interop image 320x180 RGBA16F (655360 bytes)`, with no
  fallback to the software path. The frame-60 capture of the split clip is **the right way up
  with the right colours**: bright pink-red top (`255,169,166`), dark blue bottom (`0,0,145`).
  So `BottomLeftOrigin` and the colour type are right for this driver.
- **CPU, finally measurable:** a compressed 1080p24 x264 clip, release build, a 10 s window
  after 5 s of warm-up, and two rounds that agree to 1%. **135 ms/s with the interop, 810 ms/s
  with `HULTUI_VULKAN_INTEROP=0`, and 122–127 ms/s on the GL renderer.** The interop removes
  ~675 ms of CPU a second, puts Vulkan level with GL, and justifies the phase. Why the software
  path costs 810 here against the ~175 estimated on Windows is not explained. The two Linux runs
  use identical mpv settings, so the comparison between them holds.
- **fd ownership:** the player under validation, window dragged by hand, gave **159 swapchain
  recreations with fds flat at 255** (one sample at 256). **Caveat:** the interop image is sized
  to the *video* (1920x1080) and was created once. Window resizes do not re-export it, so
  `glImportMemoryFdEXT` ran once and a per-import leak would **not** have shown. That needs a
  clip whose resolution changes mid-stream.
- **Validation:** **nothing from HultUI**. In particular there is nothing about the
  `FinishExternalFrame` queue-family transfer or its `GENERAL` layout, on the y4m clip or through
  the 159 resizes. The H.264 run did print 60 errors, 20 each (the layer's repeat cap) of
  `VUID-VkImageCreateInfo-pNext-06811`, `VUID-VkVideoBeginCodingInfoKHR-pPictureResource-07240`
  and `VUID-vkCmdDecodeVideoKHR-pDecodeInfo-07142`. All three are Vulkan *Video* decode, which
  is mpv/ffmpeg's `hwdec=auto-copy` choosing the Vulkan H.264 decoder in the same process, not
  our code. Worth knowing if validation is ever made part of a test, and worth asking whether
  `auto-copy` should prefer NVDEC or VA-API here.
- **Wayland vs X11:** both work. The hidden SDL GL window next to a Vulkan window gave KWin no
  trouble in either mode.

### Not run, or not proven

- **Mesa**, a **hybrid GPU**: no such hardware here.
- **The fd-per-import leak check**: see the caveat above.
- **Mesa's Vulkan WSI**: whether it also presents `ExtendedSrgbLinearEXT` as Windows-scRGB,
  and so wants the same 2.5375.
- The full solution build fails on the two WinForms projects (`NETSDK1100`), which is expected
  on Linux. Everything this brief needs builds cleanly on its own.

## Acted on (2026-09-25, from Windows — none of it re-run on Linux yet)

Four findings above are now fixed in code. **All four need a Linux run to confirm**, because
none of them can be verified on Windows: three are Wayland- or X11-only paths and the fourth is
a comment.

**`docs/design/HDR-LINUX-VERIFY.md` is the protocol for checking them** — what passes, what a
false pass looks like, and the regression each fix could have introduced. The summaries below
say what changed; that file says what to run.

1. **The dim scRGB window.** `SDL3Window.WhiteScale()` returns **2.5375** when the video driver
   is `wayland`, rather than going through `SdrWhiteLevelNits`, which is a Windows query that
   answers 80 everywhere else. `HULTUI_VULKAN_WHITE_SCALE` still overrides.
   **To confirm:** the F16 window should now match the GL window side by side with nothing
   forced, on both monitors. If it does not, the remaining suspect is whether the NVIDIA WSI
   really presents `ExtendedSrgbLinearEXT` as Windows-scRGB.
2. **The X11 rebuild loop.** `SdlVulkanHost.DisplayChanged` now compares the format
   `ChooseFormat` would actually produce against `_format`, instead of comparing what was wanted
   against what was obtained — which could never agree on a surface with no wide format. The
   probe does not log, so the "wide colour asked for, surface has none" line no longer repeats.
   **To confirm:** `SDL_VIDEODRIVER=x11 HULTUI_VULKAN_F16=1` should log `format unchanged` and
   build the swapchain once. Verified on Windows only in the negative sense: the trace there
   still says `format unchanged` three times with scRGB correctly in use.
3. **The mixed-scale Wayland shrink.** The `scaleOnlyChange` branch of `SDL3Window.UpdateSize`
   no longer runs on Wayland at all, where window coordinates are logical and the compositor
   already preserves the logical size. **This affects the GL path too**, so it is the one change
   here that a normal Linux app would notice.
   **To confirm:** a scripted launch on the mixed-scale desktop should keep 500x300 rather than
   shrinking to 333x200, on both renderers, and moving a window between the 1.0 and 1.5 outputs
   by hand should still look right.
4. **The capture-default claim.** `SdlVulkanHost.Capture.cs` no longer says the default frame is
   reachable in an app that renders only when dirty; it now says an idle window may stop one
   frame short and that touching the window is what produces it.

Not acted on: the Vulkan Video validation errors, which are ffmpeg's `hwdec=auto-copy` choosing
the Vulkan H.264 decoder rather than anything here. Whether `auto-copy` should prefer NVDEC or
VA-API on this hardware is a real question and a separate one.
