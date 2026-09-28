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
