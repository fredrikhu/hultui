# Windows: check the start-up swapchain fix, and what Linux round 2 found

Hand this to a Claude Code session on the Windows machine (RTX 4080, 150% HDR display). The Linux
results it refers to are under **Results** in [HDR-LINUX-VERIFY-2.md](HDR-LINUX-VERIFY-2.md).

## 1. The fix to test: `ffce665`, one swapchain at start-up instead of two

### What was wrong

Round 2 asked Linux for "exactly ONE swapchain build in the trace at start-up" as the positive
control for the window-creation reorder (`d1d7cd5`). **Linux saw two, both at the right size:**

```
vulkan swapchain 750x450 x3 R16G16B16A16Sfloat/ExtendedSrgbLinearEXT
skia surface
vulkan swapchain 750x450 x3 R16G16B16A16Sfloat/ExtendedSrgbLinearEXT
platform window ready
```

The reorder worked: there is no 500x300 build. The second build comes from **the GL fix in the
same commit**. The explicit `ActiveSurface?.Resize(PixelWidth, PixelHeight, DisplayScale)` after
construction reached `SkiaVulkanRenderSurface.Resize`, whose `_width/_height` started at 0, so it
never early-returned and called `RecreateSwapchain`. With the fixed extent off, `Rebuild` rebuilds
unconditionally. **None of that is platform-specific**, so Windows should have had the same two
lines. The likely explanation is that the one-line observation was taken before the GL fix went in.

### What changed

`IVulkanSurfaceHost` gained `WidthPx`/`HeightPx`, the window size the swapchain was built for.
`SdlVulkanHost` implements them, and `SkiaVulkanRenderSurface` starts from them. The explicit
`Resize` in `SDL3Window` is untouched, because GL still needs it. On Vulkan it is now a no-op.

### What to check

```powershell
dotnet build -c Release samples/HultUI.Samples.Minimal
$exe = 'samples\HultUI.Samples.Minimal\bin\Release\net10.0\HultUI.Samples.Minimal.exe'
$env:HULTUI_STARTUP = '1'; $env:HULTUI_A11Y = '0'
foreach ($r in '', 'vulkan') {
  $env:HULTUI_RENDERER = $r
  $p = Start-Process -PassThru $exe -RedirectStandardError "start-$($r -replace '^$','gl').log"
  Start-Sleep 4; Stop-Process $p
}
Select-String 'vulkan swapchain|first frame shown|No canvas|Exception' start-*.log
```

Establish, and write down:

- **Vulkan: exactly one `vulkan swapchain` line before `platform window ready`**, at 750x450 on the
  150% display. Two lines means the fix did not take on Windows. A 500x300 line means the reorder
  has regressed.
- **GL: `first frame shown`, and no `No canvas set. Call BeginFrame first.`** This is the check that
  matters most, because the explicit `Resize` exists for GL. The fix should not affect GL, but GL is
  what broke last time.
- **Resize still rebuilds**: drag an edge on the Vulkan window. There should be one new swapchain
  line per size, each matching the window. If the first resize after start-up is skipped, the
  surface's starting size and the host's disagree.
- **A window opened on a 100% monitor**, if one is attached: expect two builds (the DPI transition
  plus the start-up one), down from three.
- If convenient, the start-up and first-frame numbers against `HDR.md`'s table. Linux measured no
  change in total start-up from removing the rebuild, and Windows measured none from the reorder
  either, so **no change is the expected result**, not a failure.

## 2. The second fix to test: GL now waits for Vulkan before writing the shared image

### What was wrong

Nothing ordered mpv's GL write of the next video frame after the previous UI frame's Skia sample
of the same image. There was no release barrier and no semaphore running Vulkan → GL, and the
acquire fence only waits for the frame that last presented *that swapchain image*. On Linux,
exposure was measured with a fence checked when GL starts writing: 0/1200 at normal load, but
**59/960** once frames were GPU-bound (the video drawn 250 extra times per frame). The full account
is item 0 of [HDR-LINUX-VERIFY-2.md](HDR-LINUX-VERIFY-2.md#results).

### What changed

- A **second shared semaphore**, Vulkan → GL, created with the existing one (both or neither).
- The present submit records a **release barrier** (queue family → `QUEUE_FAMILY_EXTERNAL`,
  `SHADER_READ_ONLY` kept) when the frame drew the image, and signals it.
- `LendInteropImage` calls `glWaitSemaphoreEXT` on the GL context before mpv writes.
- `SignalSemaphore` now **flushes**. Nothing flushed the GL signal before, and a Vulkan binary
  wait needs it submitted.
- The fallback (`HULTUI_VULKAN_SEMAPHORE=0`) does a queue wait at the same point instead.
- Destroying the shared image on a video-size change now waits for the device first.

**All of it is shared code, but the Windows half is untested**: `OPAQUE_WIN32` handles for the
second semaphore, `GL_EXT_semaphore_win32`, and `glWaitSemaphoreEXT` on WGL.

### What to check

Build Release, then run VideoPlayer on the Vulkan renderer on 2001 at 20:00 as in
`HDR-WINDOWS-CUDA.md`, with `HULTUI_STARTUP=1`:

- **The trace still says `(semaphore ordering)`.** If the second semaphore failed to import, both
  are dropped and it says `glFinish ordering`. That is the fallback working, but it means the new
  path was not tested.
- **Playback advances.** A GL wait on a semaphore that is never signalled stalls GL. On Linux that
  meant no frames at all, so a frozen picture or a stall is the failure signature to look for. Use
  `HULTUI_VIDEO_HDR_PROBE=48`: six samples with changing level counts means frames kept coming.
- **CPU against the parent commit** (`abeca08`), round-robin as before. Linux measured no
  difference (128–130 against 113–144 ms/s).
- **Validation**, if you can get the layers to report on Windows (a debug messenger, or
  `VK_LAYER_LOG_FILENAME`). Linux was clean with synchronisation validation on, including controls.
- **`HULTUI_VULKAN_SEMAPHORE=0` still plays** (the fallback's new queue wait).

## 3. For your records, from Linux round 2 (nothing here needs a run)

- **Synchronisation validation is clean** on the semaphore path and the `glFinish` path, on Wayland
  and X11. Controls with deliberate faults showed it catches a missing dependency **across submits**
  on the one queue. Windows never ran it, and now does not need to for the Vulkan side.
- **The resize stretch is DXGI's.** KWin shows a mismatched buffer unscaled (800 captures, 0
  stretched, stale frames caught). Keep the Windows reasoning to Windows.
- **"The price of 16-bit colour" in `HDR.md` is a Windows conclusion.** On NVIDIA Linux F16 costs
  5 ms of first frame (Windows: 100), about 50 ms/s through a resize storm (Windows: about 500), and
  no memory. Vulkan's start-up and memory costs there are the API's. Worth labelling in `HDR.md`.
- **`swapchain_maintenance1`**: NVIDIA's Linux driver advertises it but offers no present scaling on
  any surface, so `FIXED_EXTENT=1` declines itself there. The video penalty could not be measured.

## What to send back

Append under a **Results** heading in this file and commit. For the swapchain check, quote the
trace lines. For GL, say that `first frame shown` appeared, not only that nothing crashed.

## Results

Windows 11, RTX 4080, driver 610.62, two 2560x1440 monitors, the window on the 150% HDR one.
Release, `HULTUI_A11Y=0`, `SDL_WINDOW_ACTIVATE_WHEN_SHOWN=0`. Run at `184ae17`.

### 1. One swapchain at start-up — pass, on both renderers

**Vulkan: exactly one `vulkan swapchain` line, before `platform window ready`, at the right size.**

```
[startup] vulkan swapchain 750x450 x3 R16G16B16A16Sfloat/ExtendedSrgbLinearEXT +   11,3 ms   t=  575,5 ms
[startup] platform window ready        +    0,8 ms   t=  602,1 ms
[startup] first frame shown            +  168,9 ms   t=  788,6 ms
```

No 500x300 line, so the reorder has not regressed.

**GL: `first frame shown` appeared** — quoted rather than "nothing crashed", as asked:

```
[startup] platform window ready        +    3,1 ms   t= 1170,2 ms
[startup] first frame shown            +   66,2 ms   t= 1244,1 ms
```

stderr was empty on both: no `No canvas set. Call BeginFrame first.`, no exception.

**Your reading of the earlier one-line observation is right.** That trace was taken before the GL
fix existed — the GL breakage was found afterwards and the explicit `Resize` added in response, and
both went into `d1d7cd5` together. So the committed code did have two lines on Windows too, exactly
as the code says it must. Nothing platform-specific, and nothing was ever seen that contradicted
you.

**Resize still rebuilds, one per size, none skipped.** Four programmatic resizes, four new
swapchains:

| asked (window px) | client rect | swapchain built |
|---|---|---|
| 820x640 | 805x602 | 1208x904 |
| 900x700 | 885x662 | 1328x994 |
| 1000x760 | 985x722 | 1478x1084 |
| 760x560 | 745x522 | 1118x784 |

Each swapchain is the client rect x1.5, which is the same relationship start-up shows (a 500x300
DIP request builds 750x450), so it is the display scale and not a mismatch. Win32's `GetClientRect`
reports logical coordinates here while SDL reports backing pixels; that predates this fix — the
pre-change trace from the earlier session shows the same ratio. **The first resize after start-up
was not skipped**, which was the specific worry.

**The 100% monitor case was not run.** Both attached monitors are 2560x1440 and this machine has
no 100% display configured, so the two-builds-instead-of-three check has no hardware here.

### 2. The Vulkan to GL semaphore — pass, and it costs nothing

**The Windows half of the new path really ran**, which was the thing in doubt —
`OPAQUE_WIN32` for the second semaphore, `GL_EXT_semaphore_win32`, `glWaitSemaphoreEXT` on WGL:

```
[startup] vulkan interop ready on NVIDIA GeForce RTX 4080/PCIe/SSE2 (semaphore ordering)
```

Not `glFinish ordering`, so neither semaphore was dropped and the fallback did not quietly stand in.

**Playback advances — no stall.** `HULTUI_VIDEO_HDR_PROBE=48`, six samples, all with content and
all different:

```
sample 1/6 ... max 2,240, 496803 above 1.0; middle row 1957 levels, 1068 reversals
sample 2/6 ... max 2,240, 485074 above 1.0; middle row 1906 levels, 1152 reversals
sample 3/6 ... max 2,240, 477318 above 1.0; middle row 1901 levels, 1102 reversals
sample 4/6 ... max 2,240, 477015 above 1.0; middle row 1965 levels, 1218 reversals
sample 5/6 ... max 2,240, 492154 above 1.0; middle row 1958 levels, 1132 reversals
sample 6/6 ... max 2,240, 755837 above 1.0; middle row 2019 levels, 1133 reversals
```

The level counts and above-1.0 counts change between samples, so these are six different frames
rather than one frozen one. A GL wait on a semaphore nobody signals would have shown here.

**CPU: no measurable cost**, matching your 128–130 against 113–144. Three reps, round-robin,
against the parent commit built in a worktree, 4K HEVC HDR, 15 s window after a 12 s settle:

| | CPU | |
|---|---|---|
| `abeca08` parent, nothing ordering the write | 52, 67, 68 ms/s | `cuda`, semaphore ordering |
| `184ae17` HEAD, the new semaphore | 58, 73, 62 ms/s | `cuda`, semaphore ordering |
| HEAD, `HULTUI_VULKAN_SEMAPHORE=0` | 186, 221, 198 ms/s | `cuda`, `glFinish` ordering |

The first two ranges overlap; the difference is inside this machine's noise, which moves by a third
between sessions. **The fallback plays** and costs what the `glFinish` path has always cost.

*(Method note: the harness printed a "median" that is actually the maximum — PowerShell's
`[int](3/2)` rounds to 2, not 1. The per-rep values above are the real ones; no conclusion rested
on the label.)*

### 3. Validation — cannot be run on this machine at all, and an earlier claim of mine was wrong

**There is no validation layer installed here.** `HKLM\SOFTWARE\Khronos\Vulkan\ExplicitLayers`
and the `HKCU` equivalent do not exist, and there is no Vulkan SDK. `VK_LAYER_LOG_FILENAME` with
`VK_LOADER_LAYERS_ENABLE=*validation*` and synchronisation validation enabled produced **no log file
at all** — the layer never loads, so there is nothing to redirect.

**This invalidates something I wrote earlier in `HDR.md`**, and it is corrected there rather than
quietly dropped: the `swapchain_maintenance1` section claimed "validation layers were clean" for the
over-allocated swapchain. They were not run. The run produced no layer output and that silence was
read as approval — a measurement that could not fail, which is the first shape in that document's
own Traps section, committed while writing about the others.

So the Vulkan side rests on your runs, as your brief anticipated. Nothing on Windows contradicts
them; Windows simply cannot check.

### Not done

- **The 100% monitor case** — no such display attached (above).
- **Validation on Windows** — impossible without installing the SDK, which seemed worse than
  relying on the Linux runs that already cover the same code.
- **By-eye playback.** The probe samples prove frames changed; a person did not watch the film.
