# Vulkan and HDR — review fixes

From the 2026-09-28 review of the Vulkan renderer and HDR video work (commits 8858b89..33708b8).
The design was judged sound; the fixes below are implementation bugs, gaps the design never
addressed, drifted docs, and missing tests. Everything here is meant to be done eventually. The
phases are the order to do it in: highest user impact first, with Reel and Aperture now shipping on
Vulkan by default.

**Each item lands with a test where a seam exists, and a negative control where it guards a bug**
(see the testing skill: reintroduce the bug, watch the test fail, revert). Items marked **eye**
need a person at an HDR display; items marked **Linux** need the Linux machine.

## Status

Committed one phase at a time on the branch `vulkan-hdr-review-fixes`: phase 1 `a2533b1`,
phase 2 `b9b13d6` (which also carries 7.1), phase 3 `59ff937`, phase 4 `6e261ff`.

| # | Item | Phase | Status |
|---|------|-------|--------|
| 1.1 | Extension instance commands resolve optionally | 1 | Done (hardening; no crash reproduced) |
| 1.2 | Vulkan failure after window creation falls back to GL | 1 | Done |
| 1.3 | Renderer selection is a tested pure function | 1 | Done |
| 2.1 | React to `SDL_EVENT_WINDOW_HDR_STATE_CHANGED` | 2 | Done — **eye** check pending |
| 2.2 | Refresh display colour on display/HDR events only, not every resize | 2 | Done |
| 2.3 | Offscreen blit follows a changed SDR white scale | 2 | Done — **eye** check pending |
| 2.4 | Display peak set at window creation | 2 | Done |
| 2.5 | HDR state from Windows, not SDL; SDR white polled | 2 | Done |
| 2.6 | `DisplayInformation.AdvancedColorInfoChanged` through hand-rolled COM | 2 | Done — **eye** check pending |
| 3.1 | Reel's GL preload skipped when the app asks for Vulkan | 3 | Done |
| 3.2 | scRGB only when `VK_EXT_swapchain_colorspace` is enabled | 3 | Done |
| 4.1 | A video frame is labelled with the settings it was rendered with | 4 | Done |
| 4.2 | Display peak scaled under `HULTUI_VIDEO_WHITE=ui` | 4 | Done |
| 4.3 | mpv option hygiene: depth cap, peak clamp, `auto-safe` | 4 | Done |
| 5.1 | Build fails if the natives import CUDA at load time | 5 | Done |
| 5.2 | Notices and README: nv-codec-headers, libpipewire, loader error | 5 | Done |
| 5.3 | Build script: pinned-tag checks, toolchain check, report coverage | 5 | Done |
| 6.1 | Per-host Vulkan command table | 6 | Done |
| 6.2 | GL device UUID via the indexed query | 6 | Done — **Linux** (Mesa) check pending |
| 6.3 | Interop state machine hardening | 6 | Done (one limit documented, below) |
| 6.4 | Present semaphores outlive a pending present | 6 | Done |
| 7.1 | `FrameColorFilter` stops recompiling SkSL every frame | 7 | Done (with 2.3) |
| 7.2 | `HULTUI_RENDERER` parsing: case, empty value, recipes | 7 | Done |
| 7.3 | SDR white level and HDR state from SDL's window properties | 7 | Declined — SDL's values are the problem (2.5) |
| 7.4 | Stray scratch files out of the repo root | 7 | Done |
| 8.1 | mpv target options set without blocking the render thread | 8 | Done — the stall was real |
| 8.2 | Wide colour gamut carried through to scRGB | 8 | Done — eye and Linux pending |
| 8.3 | mpv's first-frame wait after every load | 8 | Done — reopen 600 ms → 3–8 ms |
| 9.1 | Pure decisions extracted and unit-tested | 9 | Todo |
| 9.2 | Real-window Vulkan smoke and fallback tests | 9 | Todo |
| 10.1 | Stale code comments | 10 | Todo |
| 10.2 | ENVIRONMENT.md | 10 | Todo |
| 10.3 | HDR.md split into a current design and a dated log | 10 | Todo |
| 10.4 | MEDIA.md, Mpv README, Natives README | 10 | Todo |

## Phase 1 — Apps that ask for Vulkan must still start

Reel and Aperture request `RendererKind.Vulkan`. A machine whose Vulkan driver is present but
unusable crashed at start-up instead of getting an SDR window (1.2); 1.1 was suspected of the same
and turned out to be hardening.

### 1.1 Extension instance commands resolve optionally

`VulkanCommands.LoadInstance` resolves `vkGetPhysicalDeviceSurfaceCapabilities2KHR` through `I()`,
which throws on null, while the comment says the pointer "stays null" without the extension. The
spec lets the loader return null for a command whose instance extension was not enabled, and that
one is enabled only when `surface_maintenance1` is present.

**Fix:** an `OptionalInstance()` resolver that returns zero, used for extension commands; `I()`
stays for core commands.

**Result (2026-09-28): hardening, not a crash fix.** The review called this a start-up crash. The
negative control did not reproduce one: with the maintenance pair deliberately left out, the
Khronos loader 1.4.357 on Windows still returned a pointer, and the old `I()` code started fine.
The change stays because the spec allows null and a stricter loader could return it.

### 1.2 Vulkan failure after window creation falls back to GL

The fallback in the `SDL3Window` constructor covers only `SDL_CreateWindow` failing, which needs the
Vulkan loader to be missing entirely. Anything `SdlVulkanHost.Create` or `new
SkiaVulkanRenderSurface` throws — no ICD, no presenting device, surface usage without TRANSFER_SRC,
Skia refusing the device — propagates out of the constructor, and the SDL window leaks. VMs, RDP
sessions and broken drivers commonly have a loader and no usable device.

**Fix:** when the app (not `HULTUI_RENDERER`) asked for Vulkan and host or surface creation throws:
dispose what was built, destroy the Vulkan window, recreate it as a GL window, and print one stderr
line. When `HULTUI_RENDERER=vulkan` asked, keep failing loudly — that is how the design docs test.
Also destroy the SDL window when the constructor throws for any reason.
**Result (2026-09-28).** The review overstated the gap: with *no* driver (`VK_DRIVER_FILES`
pointed at nothing) SDL already refuses the Vulkan window, because the loader offers no surface
extension, and the old fallback caught it. The real gap is a driver that is present with no usable
GPU. `VK_LOADER_VENDOR_ID_FILTER=0x1234` reproduces that — the old code threw
`no Vulkan physical device` out of `new Application`. Now an app request logs one line and starts
on GL at the right pixel size; `HULTUI_RENDERER=vulkan` still throws the Vulkan error. The host
disposes whatever `Init` built before it threw, and a constructor that throws destroys its native
window.

### 1.3 Renderer selection is a tested pure function

The precedence (environment over app request over default) and "may this failure fall back" are
logic, and they have no test. Extract them into a pure function and unit-test the matrix.

**Result:** `HultUI.Core.Rendering.RendererSelection`, 13 cases in `RendererSelectionTests`.
Negative control: swapping the precedence fails the two cases where both are set.

## Phase 2 — The picture must follow display changes

### 2.1 React to `SDL_EVENT_WINDOW_HDR_STATE_CHANGED`

Not handled today. Turning HDR off while the app runs leaves an scRGB swapchain at white scale 3 on
an SDR display (the UI blows out); turning it on leaves an 8-bit swapchain; the SDR brightness
slider is ignored.
**Fix:** route the event to `RefreshDisplayColor`. **Verify:** eye (Win+Alt+B, and the slider).

### 2.2 Refresh on display/HDR events only

`RefreshDisplayColor` runs on every `RESIZED`, `PIXEL_SIZE_CHANGED` and `DISPLAY_SCALE_CHANGED` —
four `QueryDisplayConfig` calls and a surface-format query per resize, on the path already measured
as Vulkan's main cost. **Fix:** `DISPLAY_CHANGED` and `HDR_STATE_CHANGED` only.

### 2.3 Offscreen blit follows a changed SDR white scale

`OffscreenFor` bakes `k` into the blit when the offscreen is created and rebuilds only on a size or
encoding change. A move between two HDR monitors with different SDR white keeps the old UI
brightness while video (which reads the scale per frame) takes the new one.
**Fix:** rebuild the blit filter when the scale changes, independently of the surface.

### 2.4 Display peak set at window creation

`DisplayPeakNits` is set only in `RefreshDisplayColor`, so until a display event arrives mpv may
tone-map to the assumed 1000 nits. **Fix:** set it next to `SdrWhiteScale` in the constructor.

**Phase 2 result (2026-09-28).** 2.2 was visible in the start-up trace before the change: three
`display changed` refreshes, each with two SDR-white queries, from the size settling alone. 2.3 also
fixed 7.1: `ReleaseOffscreen` no longer touches the video filter, which is keyed on its own scale
and encoding. White still lands at scRGB 3.000 on the 240-nit display. **Still to check by eye:**
with a Vulkan window open, Win+Alt+B both ways (the swapchain should switch format, and the UI
should not blow out on SDR), and the SDR content brightness slider (UI white should follow). Whether
Windows raises `HDR_STATE_CHANGED` for the slider is SDL's to decide and has not been observed.

### 2.5 HDR state from Windows, not SDL; SDR white polled

Found by the eye check on 2026-09-28, on the 455-nit HDR monitor:

- **Win+Alt+B:** SDL raised its event and the white scale followed (3 → 1 → 3), but SDL still said
  `hdr=True` with HDR off, so the swapchain stayed half-float on an SDR screen. It looked right
  only because the scale fell to 1.
- **Slider at 100 (480 nits):** SDL said `hdrDisplay=off` in HDR mode, and the window got an 8-bit
  swapchain — no HDR video. SDL calls a display HDR when its headroom (peak over SDR white)
  exceeds 1, and 455/480 is 0.95. This bug predates the review.
- **Slider moved live:** no event at all; SDR windows beside the probe followed it, the probe did
  not.

**Fix:** on Windows, HDR state comes from the display configuration API's advanced-colour info
(`GET_ADVANCED_COLOR_INFO_2`'s active mode, falling back to "enabled and not wide colour
enforced"), next to the SDR white it already read — `WindowsSdrWhiteLevel` became
`WindowsDisplayColor`. Both are polled once a second; a change updates the host and redraws, and an
unchanged answer does nothing. The peak multiplies SDL's headroom by SDL's own SDR white, because the
two are one snapshot and SDL's goes stale when the slider moves. Wayland is unchanged.

**Verified (2026-09-28/29).** Start-up says `hdrDisplay=on` through the new query, and the three
redundant start-up refreshes are gone. By eye: Win+Alt+B now rebuilds to `B8G8R8A8Unorm/SrgbNonLinear`
when HDR goes off and back to scRGB when it comes on; the slider moves the UI white, and HDR stays
on at 480 nits with the peak at 455.

**Why a poll and not an event.** A subclassed window logging every sent message (positive
control: `WM_WINDOWPOSCHANGED`, seen) got `WM_DISPLAYCHANGE` twice for each Win+Alt+B and **nothing
at all** for the slider. SDL's own message hook could not have answered this — it sees only posted
messages, and these are sent. DXGI cannot either: its output description has no SDR white. The
only event is WinRT's `DisplayInformation.AdvancedColorInfoChanged` (Windows 11 24H2+, hand-rolled
COM from .NET). At one poll a second the lag behind a dragged slider was plainly visible; a poll
costs about 12 µs (median of 300, 69 µs worst), so it now runs every 100 ms, about 0.1 ms of CPU a
second and no extra wake-ups. HDR.md's claim that SDL "asks the compositor, which is the only thing
that knows" goes into 10.3 as wrong on Windows.

### 2.6 `DisplayInformation.AdvancedColorInfoChanged` through hand-rolled COM

Even at 100 ms the poll trailed a dragged slider visibly. A scratch test with the SDK projection
showed the WinRT event fires once per 4-nit step of a drag, 4–16 ms apart, and gives HDR kind, SDR
white and peak in one answer. The projection itself was not an option: it needs a Windows target
framework, which `HultUI.Platform.SDL3` (built for Linux too) cannot have, and would put a 26 MB
`Microsoft.Windows.SDK.NET.dll` into every app — all of which target plain `net10.0`.

**Built as `WindowsDisplayInformation`**, about 300 lines of COM with no new dependency:
`CreateDispatcherQueueController` once on the UI thread (a `GetForWindow` requirement; SDL's pump
services it), `RoGetActivationFactory` → `IDisplayInformationStaticsInterop::GetForWindow` →
`IDisplayInformation5`, and a native handler object answering `IAgileObject`. Nothing was taken
from memory: IIDs from the projection's metadata; the handler's parameterized IID from the WinRT
algorithm after it reproduced `IIterable<String>` and `IVector<String>` exactly; vtable slots from
the OS's `Windows.Graphics.winmd` and, for the interop factory, from the projection's IL. The event
only sets a flag, so a drag's burst costs one refresh per loop pass. Where it is available it is the
source for HDR state, SDR white and peak (the peak no longer depends on SDL's headroom there) and the
poll stops for that window; Windows 10 and 11 before 22H2 keep the poll.

**Opt-in**, decided after the eye check: off by default, on through
`new Application(..., displayColorEvents: true)` — which Reel and Aperture pass — and
`HULTUI_DISPLAY_INFORMATION` overrides the app either way (`1` on, `0` off), the same shape as
`HULTUI_RENDERER`. The rule is `RendererSelection.ResolveDisplayColorEvents`, with its own tests.

**Measured:** subscribes and reads `hdr=True sdrWhite=80 peak=455` on this machine; clean shutdown;
start-up to first frame 587 ms median against 578 without it (six runs each, alternated), so about
9 ms. By eye: a slider drag now tracks immediately — the refreshes come 2–20 ms apart through a
drag, against the poll's 100 ms steps. All four combinations of app request and environment
checked with the probe.

## Phase 3 — Start-up and spec correctness that is cheap to fix

### 3.1 Reel's GL preload skipped when the app asks for Vulkan

Reel calls `BeginGraphicsPreload()` before `new Application(..., RendererKind.Vulkan)`, so the
`UseVulkan` guard passes, the GL driver loads on a worker, and `JoinPreload()` holds up the Vulkan
window. **Fix:** let the renderer request reach the platform before the preload decision (an
overload taking the `RendererKind`, or recording the request earlier). **Verify:** start-up trace
shows no GL preload on Reel's default path.

### 3.2 scRGB only when `VK_EXT_swapchain_colorspace` is enabled

`ChooseFormat` can choose `EXTENDED_SRGB_LINEAR` when the extension was not enabled — invalid
usage, and the comment above the enabling code warns about exactly this. **Fix:** record whether it
was enabled and gate `WantsWideColor` on it.

**Phase 3 result (2026-09-28).** 3.1: `Application.BeginGraphicsPreload(RendererKind)`; Reel names
Vulkan. Measured with a probe app: the old call loaded the GL driver on a worker (116 ms) for a
Vulkan window, and first frame came at 715 ms against 620 ms without it (one run each). With
`HULTUI_RENDERER=gl` the preload still runs. 3.2: with the extension forced off, the swapchain is
`B8G8R8A8Unorm/SrgbNonLinear` and the trace says why; with it, scRGB as before.

## Phase 4 — Video correctness

### 4.1 A frame is labelled with the settings it was rendered with

`ApplyTargetColour` flips `ReferenceWhiteNits` immediately, but when mpv has no new frame the
early return skips rendering, so the image keeps a frame made under the old settings and is drawn
with the new label — wrong brightness after an SDR↔HDR move, lasting while paused.
**Fix:** latch the reference white of the frame actually in the target, and render (redraw) when
the target settings changed rather than taking the early return. Unit-test the latch.

### 4.2 Display peak scaled under `HULTUI_VIDEO_WHITE=ui`

Under `ui` the frame is stretched by `SDR white / 203`, but mpv is still told the display's peak,
so highlights between `peak × 203 / SDR white` and the peak clip on the display instead of being
tone-mapped. **Fix:** divide the peak by the same stretch. Unit-test the arithmetic.

### 4.3 mpv option hygiene

- `DepthOf` returns 32 for RGBA32F, which is undefined behaviour in mpv's dither (`1 << 32`); cap 16.
- `target-peak` outside 10–10000 fails silently while `_appliedPeak` records it; clamp.
- `hwdec=auto` is `auto-safe` on mpv ≥ 0.40 but `auto-unsafe` on older distribution libmpv
  (loadable through `HULTUI_MPV_PATH`); ask for `auto-safe`.
- The `internal_format` comment credits it with the dithering fix; in mpv 0.41 it is ignored and
  `MPV_RENDER_PARAM_DEPTH` is what fixed it.

**Phase 4 result (2026-09-28).** `MpvTargetColour` holds the state (9 tests in
`MpvTargetColourTests`; latching on `Linear` instead of the drawn frame fails one).
`HdrVideoLuminance` holds the frame-scale and producer-peak arithmetic (5 tests; ignoring `ui` for
the peak fails one). Real playback on Vulkan: interop with semaphore ordering, linear target at the
queried 455 nits, `auto-safe` resolving to `nvdec`, the 10-bit ramp at 879 levels as before. The
17 mpv backend tests pass against the bundled libmpv. ENVIRONMENT.md, MEDIA.md and the Mpv README
now say `auto-safe`.

## Phase 5 — Native build hygiene

### 5.1 Build fails if the natives import CUDA at load time

The "no `nvcuda` import" rule lives only in docs; a regression would ship a DLL that does not load
on AMD or Intel. **Fix:** in `collect()`, fail on `nvcuda`/`nvcuvid` in `objdump -p` and on
`libcuda`/`libnvcuvid` in `readelf -d` NEEDED.

### 5.2 Notices, README, loader error

- `THIRD-PARTY-NOTICES.md`: nv-codec-headers (MIT; its loader is compiled in).
- Natives README: libpipewire is a hard NEEDED on Linux, alongside ALSA and PulseAudio.
- `Mpv.TryLoad`: carry the loader's error text, so a missing libpipewire is not reported as
  "libmpv was not found".

### 5.3 Build script robustness

- `fetch()` checks an existing clone is at the pinned tag; the nv-codec-headers skip checks the
  installed version; the FFmpeg stamp includes the tags.
- Toolchain check beyond `MSYSTEM` (Git for Windows' bash passes it): `gcc -dumpmachine`, `make`.
- Explicit `--disable-cuda-llvm`; the licence report records the installed nv-codec-headers
  version and the NEEDED/DLL lists.
- `versions.env`: older drivers fall back to software decoding on Linux, not to copy.

**Phase 5 result (2026-09-29).** The full native build was not re-run: it takes the better part
of an hour per host and CI runs it. Each new piece was exercised on its own instead:
- **CUDA-link check:** run against the shipped `libmpv-2.dll`'s real import list with MSYS2's
  `objdump`, where it passes. It fails both controls, the same list with `nvcuda.dll` added and a
  Linux NEEDED list with `libcuda.so.1`.
- **Toolchain check:** rejects Git for Windows' bash (`gcc targets 'nothing'`) and passes in a real
  MSYS2 MINGW64 shell.
- **Tag check in `fetch()`:** recognises all four existing clones.
- **Licence report:** passes on the local work tree, and on a pretend pin bump says "built from a
  stale work directory" rather than "NOT LGPL".
- **Loader error:** `Mpv.TryLoadFrom` is new, with 2 tests; not recording the error fails one.

The notices gained nv-codec-headers' MIT notice verbatim from the headers. Other MIT components
already listed (HarfBuzz, Brotli) still have only a row and no text, which is the same gap and was
there before this plan. A kept work directory rebuilds FFmpeg once, because the new stamps replace
the old `ffmpeg-configure.txt`.

## Phase 6 — Robustness in less common configurations

### 6.1 Per-host Vulkan command table

Every window overwrites one static function table, so window 1 calls device-level pointers from
window 2's device. Works with one driver; undefined by the spec, and broken if two windows pick
different GPUs. **Fix:** make the table an instance owned by each `SdlVulkanHost`.

### 6.2 GL device UUID via the indexed query

`GL_DEVICE_UUID_EXT` is an indexed query (`glGetUnsignedBytei_vEXT`, index <
`NUM_DEVICE_UUIDS_EXT`). The non-indexed call works on NVIDIA; elsewhere it may read zeros, look
like a GPU mismatch, and quietly disable interop. **Fix:** indexed query, any-match, non-indexed as
fallback. **Linux** (Mesa) to verify.

### 6.3 Interop state machine hardening

- One shared image and one `_lent` per window: a second GPU video source in the same frame would
  overwrite or destroy the first. Unreachable today (Reel's scrubber stills are files); refuse a
  second lend per frame so the second source takes its pixel path.
- A frame that draws video and is never presented leaves `_releaseOwed` set; the next GL write then
  skips its wait and the image is acquired twice. Release at the start of the next frame instead.
- The `glFinish` fallback never hands ownership back to the external queue.
- The failed-lend path gives a black video rather than the pixel path.

### 6.4 Present semaphores outlive a pending present

`Rebuild` after a SUBOPTIMAL present destroys the per-image semaphores while that present may still
wait on one. Defer destruction until the image's next acquire, or use present fences where
`swapchain_maintenance1` is enabled.

**Phase 6 result (2026-09-29).**
- **6.1:** `VulkanCommands` is an instance per host. Two Vulkan windows each build their own
  swapchain, draw white at scRGB 3.000, and close cleanly. There is no negative control, because the
  shared table only breaks when two windows pick different GPUs.
- **6.2:** the indexed `glGetUnsignedBytei_vEXT` query alone enables interop on NVIDIA (checked with
  the fallback switched off). With neither query answering, interop is refused as `GL (unknown)` and
  video takes the software path. GL errors are drained on both sides of the query. Mesa, the reason
  for the change, still needs the Linux machine.
- **6.3:**
  - The glFinish path now releases the image to the external queue (SHADER_READ_ONLY → GENERAL,
    matching its acquire), where it used to take ownership every frame and never return it.
  - A frame that took the image and was never presented hands it back through `FlushOwedRelease`,
    both at the next lend and when the present finds nothing to present.
  - A second lend in one frame is refused, so a second GPU video source draws nothing instead of
    overwriting the first or getting its image destroyed under a recorded draw.
  - **Limit, documented rather than fixed:** a source whose lend is refused, or fails, draws
    nothing. Its mode is decided once at its first draw, so it cannot fall back to pixels
    afterwards. Two GPU sources in one Vulkan window would need an image per source.
- **6.4:** retired semaphores are kept per batch and destroyed after the replacing swapchain has
  presented a full round of images, plus one. A maximize/restore cycle showed each batch destroyed
  one round later, and the last one on dispose.

**Validation.** Every path above was run under core and synchronisation validation
(`VK_LOADER_LAYERS_ENABLE='*validation*' VK_LAYER_VALIDATE_SYNC=1`), with **0 messages**: video on
the semaphore path, video on the glFinish path (both still 879 levels on the 10-bit ramp), and two
windows through eight resizes. The layer loaded in each run: two "forced enabled" lines per instance
and device pair. Dropping the present's wait semaphore produced 4 `SYNC-HAZARD` lines in the same
setup, so the quiet runs are quiet for the right reason. As before, the GL side of the seam is
invisible to the layer.

## Phase 7 — Small cleanups

### 7.1 `FrameColorFilter` recompiles every frame without an offscreen

`OffscreenFor` calls `ReleaseOffscreen()` every frame on the no-offscreen path, disposing
`_frameScale`, so the SkSL filter is recompiled per frame (scaled-space path, white scale 1).

### 7.2 `HULTUI_RENDERER` parsing

Case-sensitive (`Vulkan` forces GL); an empty value means "unset", so the HDR-LINUX-VERIFY-2
recipes that use an empty value to mean GL now run Vulkan. Compare case-insensitively, document
the empty value, fix the recipes to say `gl`.

### 7.3 SDR white level and HDR state from SDL

The pinned binding has `SDL_PROP_WINDOW_SDR_WHITE_LEVEL_FLOAT` and
`SDL_PROP_WINDOW_HDR_ENABLED_BOOLEAN`, which SDL keeps current. On Windows these can replace the
226-line `WindowsSdrWhiteLevel` P/Invoke — **after** confirming SDL reads 3.0 where the P/Invoke
reads 240 nits on this machine. Wayland keeps the 2.5375 constant.

### 7.4 Stray files

`vk.png`, `vk-wl.png`, `vk-x11.png`, `vk-validation.png`, `linux-vulkan.txt` at the repo root:
move under `spikes/HdrInterop/results/` or delete.

**Phase 7 result (2026-09-29).**
- **7.2:** `RendererSelection.ParseEnvironment` matches `vulkan` in any case, and treats blank as
  unset. There are 3 new cases, and a case-sensitive match fails 2 of them. `HULTUI_SDR_TRANSFER`
  is case-insensitive too.
  - The two HDR-LINUX-VERIFY-2 recipes now say `gl`. They ran samples that default to GL, so they
    still worked, but their comment ("anything other than vulkan leaves it on GL") no longer held,
    and the same recipe would put Reel or Aperture on Vulkan.
  - The ENVIRONMENT.md row is rewritten, which does that part of 10.2: MAILBOX default, HDR format
    choice, the fallback rule, and apps choosing in code.
- **7.3 declined.** It proposed replacing `WindowsDisplayColor` with SDL's window properties once
  they were confirmed to read the same. Item 2.5 found the opposite on this machine:
  - SDL's HDR flag is wrong at a high slider setting and stale across Win+Alt+B.
  - Its SDR white is re-read only on a display-configuration change, which the slider does not
    cause.
  - SDL's white is used in exactly one place, multiplying its own headroom, because the two are one
    snapshot.
- **7.4:** the Phase 1 Linux transcript and its four captures moved together to
  `spikes/HdrInterop/results/linux-phase1/`, with history kept, and HDR-PHASE1-LINUX.md points to
  them.

## Phase 8 — Larger design work

### 8.1 mpv target options without blocking the render thread

*Plausible, unmeasured:* `mpv_set_property` waits for the core, which calls the VO synchronously,
which may be waiting (up to 200 ms) for the very render this thread is doing. Measure first
(`HULTUI_STARTUP=1`, time the "mpv linear target" mark); if real, use `mpv_set_property_async`.

**Result (2026-09-29): real, and worse than feared.** Timed with `HULTUI_MPV_LOG=v`:
- **First HDR frame:** the three synchronous sets held the render thread for **360 ms**, and mpv
  logged `mpv_render_context_render() not being called or stuck` once per property. This is a
  deadlock that only mpv's timeout breaks: the set waits for the core, the core for its video
  output, and the output for the render this thread is holding up.
- **Mid-playback:** a temporary hook flipped the target every 60 frames, because moving the window
  to the other monitor changed nothing (both are HDR at the same white). Synchronously, every change
  took **198–361 ms** (median 200) with 8 "stuck" warnings. Asynchronously it takes **0.01–0.45 ms**
  (median 0.01), all 6 changes are confirmed, and there are no warnings beyond one at start-up.

**Fix:** `mpv_set_property_async`, each option tagged with the change's generation. The event
thread counts the replies, and once all three are in, `MpvTargetColour.Applied` records the change
and asks for a repaint. The frame label, and the forced redraw from 4.1, now follow what mpv has
*confirmed* rather than what it was asked, because a redraw before mpv has the options would draw
the old picture again under the new label. `MpvTargetColour` has 12 tests, including a frame drawn
before confirmation and a late reply to an older change. Playback still confirms the linear target
before the first frame and keeps 879 levels on the 10-bit ramp.

**Left alone at the time:** one `render() not being called or stuck` at start-up. It turned out to be
a second wait cycle, fixed in 8.3.

### 8.2 Wide colour gamut

`target-prim=bt.709` with mpv's default gamut mapping desaturates BT.2020/P3 into sRGB primaries,
though scRGB carries wider colours as negative values. Routes: `gamut-mapping-mode=clip`, or a
BT.2020 target with a linear Rec.2020 wrap so Skia converts. Either way the encoded offscreen's
`max(c, 0)` must stop clamping negatives. Needs a saturated BT.2020 test pattern; **eye** and
**Linux**.

**Result (2026-09-29): done by the first route.** The swapchain is scRGB, whose native form for a
colour outside BT.709 is a negative channel, so mpv is told `gamut-mapping-mode=clip` for a linear
target and does no gamut mapping. `clip` skips mpv's desaturation, and in a linear float target
nothing then clamps. A BT.2020 target would need Skia to convert every frame instead.
- **Test pattern:** `spikes/HdrInterop/make-bars.cs` writes eight bars as a BT.2020 PQ Y4M: the
  six primaries and secondaries at ~92 nits, grey, and a BT.709 green expressed in BT.2020. The
  last two are inside BT.709, so they are the controls. The spike's `--range … bars=8` prints each
  bar.
- **Spike, into the shared image:** with `auto` the floor is 0 and the green bar reads
  (0, 0.408, 0.115). With `clip` the floor is -0.383 over 65340 negative samples, and green reads
  (-0.260, 0.501, -0.045). Both controls are identical in the two modes.
- **Real renderer, swapchain capture:** min -0.545 with wide gamut and 0.000 with
  `HULTUI_VIDEO_WIDE_GAMUT=0`. The scaled-colour-space and gamma-2.2 paths also keep -0.545.
- **Encoded offscreen:** the 2.2 encode and decode shaders are now sign-preserving
  (`sign(v) * pow(abs(v), …)`). The old shaders, as a control, give 0.000 on the same path.
- **Switches:** `HULTUI_VIDEO_WIDE_GAMUT=0` goes back to mapping inside BT.709. It means nothing
  for an encoded target, which cannot hold a negative. `HULTUI_MPV_OPTIONS` passes
  `name=value;…` to mpv before it starts; the bars need
  `vf=format=colormatrix=bt.2020-ncl:primaries=bt.2020:gamma=pq`, because a Y4M carries no colour
  tags.

**Still open:** the **eye** check (the capture shows the values reach the swapchain, not what the
compositor and panel make of them) and **Linux**, where KWin's handling of negative scRGB is
untested.

### 8.3 mpv's first-frame wait after every load

*Found while chasing the start-up "stuck" line 8.1 left alone.* After every load, and after every
seek, mpv's core queues the first frame and then waits for it to be shown (`vo_wait_frame` in
`player/video.c`). `vo_libmpv` gives up after 200 ms, logs `mpv_render_context_render() not being
called or stuck`, and counts the frame as dropped.

**The cycle:** the render thread cannot draw without the video's size. The size comes from
`video-params`, which the event thread reads with a synchronous `mpv_get_property`. That read needs
the core, and the core is waiting for the frame. So each side waits for the other until mpv's
timeout. mpv's render API documents this as a rule: no wait dependency from the render thread on a
thread that calls other libmpv functions. Observing the property instead does not help, because mpv
sends property changes at the top of its loop, after the wait.

**Measured** in the VideoPlayer sample on Vulkan, with temporary marks:
- **Start-up:** the event thread's `FILE_LOADED` reads blocked for 200 ms. The UI thread was busy
  anyway, rendering its first frame (131 ms) and showing the window (190 ms), so the picture came up
  no later because of it.
- **Reopening a file:** the UI thread was free and the element was painted on every update, but with
  no size it could not take the frame. Three timeouts, about 600 ms, before the picture came up.
- **Seeks:** 20 seeks 30 ms apart, each call 0.1–0.4 ms, no warning. The size is already known, so
  the frame is taken at once.

**Fix:** `MPV_RENDER_PARAM_SKIP_RENDERING` takes a frame without a target and without drawing it.
`IGpuFrameRenderer` gains `FrameMayBeWaiting` and `SkipFrame`, both with defaults. The sink calls
`SkipFrame` when there is no size but mpv's update callback has fired since the last render. The
flag matters because `RunExternalGL` flushes Skia on a GL surface. The next `Render` with a target
draws the frame taken. The software renderer's thread does the same.
- **Reopen, frame queued to frame shown** (mpv's own log): Vulkan 3 ms, GL 8 ms, no warnings. The
  first reopen on GL takes 85 ms, all of it a Skia flush of the player's "opening" state drawn for
  the first time.
- **Start-up:** GL now shows no warning. Vulkan still shows one, for the busy UI thread above.
- **Test:** `Opening_DoesNotLeaveMpvWaitingForItsFirstFrame` reads mpv's `frame-drop-count` after
  two opens on the software path. It fails on the first open without the fix (1, expected 0). A sink
  test checks that GL is entered only when a frame may be waiting.

## Phase 9 — Tests for what remains

### 9.1 Pure decisions, unit-tested

Extract and test: `FrameColorFilter` scale and peak arithmetic, `ChooseFormat` and
`ChoosePresentMode` over a list, the rebuild-skip predicate (the `FIXED_EXTENT=0` regression),
`DepthOf`. (Renderer selection is 1.3; the mpv latch is 4.1.)

### 9.2 Real-window Vulkan tests

In `HultUI.UiTests` (opt-in through `HULTUI_UITESTS=1`): boot a sample on Vulkan and check a
`HULTUI_VULKAN_CAPTURE` frame has content; boot with `VK_DRIVER_FILES` pointed at nothing and check
it falls back to GL. Skip where Vulkan is unavailable.

## Phase 10 — Docs

- **10.1 Code comments.** About 15 describe behaviour the code no longer has. Fix each when its
  code is touched above; sweep the rest here (`FinishExternalFrame` "stalls on purpose",
  `DisplayPeakNits` "no query", the class summary's per-image cache, `ColorSpaceFor` "not yet here",
  "982 nits", orphaned and stacked summaries, `STARTUP.md` dangling link).
- **10.2 ENVIRONMENT.md.** `HULTUI_RENDERER` row (MAILBOX default, not SDR-only, apps choose in
  code); resize numbers to match HDR.md; `HULTUI_VULKAN_ACQUIRE_TRACE`; on/off parsing stated per
  variable; `HULTUI_MPV_HWDEC` under Media.
- **10.3 HDR.md.** Split into a short current-state design (architecture, invariants, variables,
  known gaps, Traps) and a dated log. Corrections to carry: Vulkan chosen per app in code;
  validation ran on both platforms; Windows zero-copy shipped (38da446); the reverse semaphore
  (184ae17); the start-up double swapchain fixed (ffce665); lcms not needed; one value per
  quantity for the numbers that disagree; Traps gain "layouts Skia owns are assumed" and "a
  fallback that was never exercised".
- **10.4 MEDIA.md, Mpv README, Natives README.** `RendererKind`, the Vulkan video path, NVIDIA
  zero-copy on Windows.

## Process

- Phases in order; items within a phase in order unless one is blocked on hardware, which then
  moves to the end of its phase.
- After each item: build, run the affected test projects, update the Status table.
- Real-window checks go through the run-hultui-app skill; never SendKeys.
