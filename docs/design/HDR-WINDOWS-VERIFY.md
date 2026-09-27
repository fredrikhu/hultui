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

## 2. For your records, from Linux round 2 (nothing here needs a run)

- **Synchronisation validation is clean** on the semaphore path and the `glFinish` path, on Wayland
  and X11. Controls with deliberate faults showed it catches a missing dependency **across submits**
  on the one queue. Windows never ran it, and now does not need to for the Vulkan side.
- **Not tested anywhere: a Vulkan → GL ordering gap, found by reading.** There is no release barrier
  to `QUEUE_FAMILY_EXTERNAL` and no semaphore GL waits on. So mpv's GL write of the next video frame
  is not ordered on the GPU after the previous UI frame's Skia sample of the shared image. The old
  `glFinish` path had the same gap. If it ever shows, it would be a torn video frame. No validation
  layer can see it.
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
