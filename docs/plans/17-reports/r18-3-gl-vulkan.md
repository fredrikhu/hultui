SATISFIED: no

**Method.** Worktree detached at aa3a3696 (SDK 11.0.100). I made my own NativeAOT probe hosts for Reel, Junction and Aperture: round 17's scripted host plus four things.
- **Steps:** `dragrelT` (r17b2's timer steps), a new `dragrelM`, and `place`, `vkstat`, `gcmem` (now with page faults) and `jidle`.
  - `dragrelM` is the same steps framed by `WM_ENTERSIZEMOVE` / `WM_EXITSIZEMOVE`, sent to the window on its own thread. That is what Win32's modal loop does for a user's edge drag.
  - `place` sizes the window to the 4K's work area, 3840×2088, with the default frame. It never maximizes or goes fullscreen.
- **A worktree-only patch** (`vkprobe.patch`, `VkProbe.cs`): `HULTUI_VULKAN_IMAGES=<n>` overrides SdlVulkanHost.cs:835, and counters time the acquire wait and the present. I reversed it after publishing; the worktree is clean.
- Counts, DXGI `QueryVideoMemoryInfo` and Skia's cache only; no timings are ranked. MB means MiB as DXGI reports it.

REVIEW: none.
- **c23edfb9 and 0a7bb324 (player bar layer, skipped subtree at opacity 0):** checked over a 50 s Reel session with real mpv. The session had 6 bar fades, pause/play, 4 drags and a maximized-sized window.
  - DXGI local stays flat from the first resize to the end in every launch: Vulkan 3 images 382.27 MB, 2 images 352.39, GL 322.20, HDR 845.58 / 665.71. It does not move between fade 1 and fade 6.
  - Skia's cache: Vulkan 6.26–6.33 MB, GL 35.74 MB, HDR 67.49 MB, all flat.
  - Fade frames are equal on GL and Vulkan (about 180 a 4 s fade phase with 30 fps video).
- **b1423e6e (Junction's idle aggressive collection):** no objection; details under EXAMINED.
  - It fires only after a walk was cancelled. A finished walk keeps no texts (JunctionApp.Highlight.cs:583), so hold B's single file visits never triggered it.
  - In hold D (6 Downs at 400 ms, then wheel scrolling) 4 of 4 collections landed in the middle of a scroll. The GC's own pause was 6.16–7.97 ms.
- **Main's commits:** none touches SDL3, the Skia surfaces or Media. Aperture's Tab (0f9c8477) draws 6 frames for 6 Tabs on Vulkan, with 2 images or 3.

CANDIDATES:
- id: vk-swapchain-two-images (promoted from Waiting)
  - what: On Windows, build the Vulkan swapchain with `minImageCount` images (2 on this NVIDIA: min 2, max 8) instead of `minImageCount + 1` (SdlVulkanHost.cs:835).
  - evidence (DXGI local, 3 images → 2):
    - **Reel, SDR, playing:**
      - default window 1920×1140: 272.21 → 263.77 MB (−8.4, −3.1 %);
      - maximized-sized 3818×2032 px: 382.27 → 352.39 MB (−29.9, −7.8 %);
      - identical in holds A and C, run in opposite orders.
    - **Reel, HDR clip on the HDR 4K (F16 swapchain):**
      - default: 407.36 → 373.61 (−33.8, −8.3 %);
      - maximized-sized: 845.58 → 665.71 (−179.9, −21.3 %);
      - one pair only. An F16 image costs about 23 B a pixel of local here, not 8.
    - **Aperture sign-in page, no video:**
      - default 2160×1350: 68.98 → 57.36 (−16.8 %);
      - maximized-sized: 162.05 → 132.17 (−18.4 %).
    - **Pacing in playback holds:**
      - frames equal: SDR fades 188/166/163 → 179/161/163, 8 s of plain playback 265 → 259, the 15 s tail 616 → 615;
      - HDR fades and plain playback 186/167/221 → 184/173/227;
      - acquire waits over 1 ms: 0 in every playback and fade phase, except 4 of 615 frames (max 1.4 ms) in one SDR tail;
      - Aperture's drags: 1 of 24 frames (max 1.5 ms).
    - **The cost, in modal-loop drags while video plays:**
      - with 2 images, 20 of 42 and 4 of 12 frames waited over 1 ms (up to 8.5–9.1 ms, one refresh at 120 Hz);
      - with 3 images, 2 of 57 (max 3.2 ms);
      - steps taken were within noise either way (2 images 14/26/31/16, 3 images 31/10/16/17).
    - **Image reuse:** a capture of presented frame 600 with 2 images (each reused about 300 times) shows content: 9 distinct colours in a 60×60 sample grid, mean 161.
  - real app:
    - Reel and Aperture hold the swapchain the whole session, and Aperture holds it while only browsing.
    - A maximized window saves 30 MB in SDR, and about 180 MB in HDR playback (one pair).
    - At 3840×2160 fullscreen (never toggled here) it should save 31.6 MB in SDR, and in HDR, at 23 B a pixel, about 190 MB.
  - expected: the GPU-memory bar (≥5 %), cleared in every measured case except Reel SDR at the default size (−3.1 %).
  - cost: small code, medium risk; no API or pixel change.
    - The build must run `VulkanProbeTests` with 2 images under a validation layer it proves loaded. HDR.md:488 says captures alone cannot fail on NVIDIA.
    - Keep `+1` off Windows (Mesa/AMD/Intel unmeasured).
    - The F16 drag case needs the user (NEEDS THE USER).

REAL WINDOW:
- **Holds:** 4 holds, 17 launches, 0 foreground takes, 0 nvlddmkm events, no stall, the screen never read.
  - A: 152.9 s held after 173.5 s waiting; B: 114.0 s after 0; C: 178.1 s after 576.1; D: 106.5 s after 110.8.
  - The idle gate failed for A and B (testhost) and passed for C and D (CPU 5 % and 2 %).
- **Desk:**
  - Every window on the 4K: DISPLAY2, primary, 3840×2160 at 150 % (DPI 144), 120 Hz, HDR on with SDR white at 240 nits.
  - The 1440p (100 %, 120 Hz) was to its left. Timer 1/0.5/15.625 ms (Steam). The cursor was outside the window in every launch.
- **A timer-step resize (`dragrelT`) runs outside Win32's modal loop, so Vulkan rebuilds the swapchain at the exact size on every step.**
  - That is 12–30 builds a 31-step drag, with the present over 1 ms in 20–70 % of steps.
  - The same steps framed by `WM_ENTERSIZEMOVE` (`dragrelM`, a user's drag) build 1–2 times. Their presents to the over-allocated, SUBOPTIMAL swapchain are over 1 ms in 50–90 % of frames.
  - Leftover queued steps that ran after the exit rebuilt exact again; those are an artefact of the probe.
- **Dropped timer steps are not Vulkan's:** GL Reel while playing took 15 and 10 of 31; paused, it took 31 and 31. Vulkan Reel took 10–31 either way, and Aperture (no video) took 13–31.
- **The F16 swapchain's per-image cost:**
  - Measured from the 3 → 2 image difference: 33.8 MB an image at 2.19 Mpx and 179.9 MB at 7.76 Mpx, about 15–23 B a pixel against 4.0 B a pixel for 8-bit.
  - HDR Reel at the maximized size holds 845.58 MB local with 3 images.
- **GL Reel:** Skia's cache grows with window size, 12.49 MB at the default size to 35.74 MB maximized. Vulkan stays at 6.3 MB.
- **Junction:** the idle collection's pause and its page-fault cost, from hold D (below). The GL shader cache was written once per new exe (Reel GL, the HDR launch).

WAITING, RE-RANKED:
- **vk-swapchain-two-images:** promoted to a candidate with the numbers above.
- **vk-drag-steps-dropped:** close; nothing to build.
  - A timer-step drag has no modal loop, so it rebuilds every step (12–30 builds). A user's drag runs the loop and builds 1–2 times a drag (`dragrelM`).
  - WM_TIMER steps also drop on GL while a video plays (15 and 10 of 31). The count measures the driver's timer starving, not a slower Vulkan present.
- **vk-instance-extensions-once:** unchanged; SdlVulkanHost.cs:532 is untouched in both ranges. Low; it could ride along as its own commit if the two-images build touches SdlVulkanHost.
- **gl-threaded-optimization-profile:** information only and unchanged. No GL code moved in either range, and I did not measure it again.
- **f16-held-drag-post-stall:** not run. Still low, but it is now the gate for two images on F16 (NEEDS THE USER).

EXAMINED, NOT WORTH IT:
- **b1423e6e's collection against frame pacing** (Junction GL, bigrepo, two 6-Down browses with the wheel every 8 ms; sides off/on/on/off):
  - It costs at most one late frame per browse.
  - Frame gaps over the collection: 14.9, 16.1, 23.7 and 23.5 ms against the scroll's 12–20 ms cadence. Gaps over 20.8 ms: 1 and 1 with it, 0 and 0 without.
  - The cost moves to the next browse: +5,400–5,800 page faults, about 22 MB committed again on worker threads.
  - It pays off in memory: after two browses, working set 150.4 / 149.5 MB against 187.5 / 186.2 (−20 %), committed 28.3 against 70.4 MB, and +1 gen-2 per collection.
  - Waiting for input to go idle as well would save the one late frame. The pause is shorter than a 120 Hz frame, so this is below every bar.
- **Present cost of the over-allocated swapchain in a drag:** over 1 ms in most drag frames. It is the known trade of drag-only over-allocation (memory `swapchain-maintenance1`); nothing to change.
- **GL Reel's window-sized cache growth:** 12.49 → 35.74 MB, about 4.2 B a pixel. The cause is not proven: it is not a stencil (Skia is told the default framebuffer's 8 bits, SkiaGLRenderSurface.cs:110), and mpv's GL target sized to the window is a guess. GL is not Reel's or Aperture's renderer.

NEEDS THE USER:
- **vk-swapchain-two-images on F16 (HDR video).** Two images measured 0 acquire waits over 1 ms in HDR playback and fades, and save about 180 MB of local at a maximized size (one pair). A drag during HDR playback with 2 images is unmeasured: that run is the F16 held-drag-with-post run, which needs your approval.
  - (a) Approve one guarded run in the build: an HDR clip playing on the 4K, a maximized-sized window, modal-loop timer steps, stopping on any stall over 1 s.
  - (b) Use 2 images for the 8-bit swapchain only. This keeps the SDR and Aperture savings and drops the HDR one.
  - Also decide whether to accept that drag frames wait up to one refresh (8.5–9.1 ms) in about 40 % of frames while a video plays. With 3 images, 2 of 57 frames waited.
  - Recommended: (b) now, (a) if you approve.

FILES (in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\00f54f03-b511-4949-9273-30fe719e1bbb\scratchpad\r18-gl-vulkan\`):
- **Host:** `start\src\Script.cs` (adds `dragrelT`, `dragrelM`, `place`, `vkstat`, `gcmem` with page faults, `jidle`, and a gen-2 column in the frame log), `start\src\Program.cs`, `start\Start.props`, `start\pub.ps1`, `start\pub-run.log`. Publishes are in `pub\aot18\{Reel,Junction,Aperture}\`.
- **Probe patch:** `vkprobe.patch` and `VkProbe.cs` (worktree-only, reversed).
- **Driver and batches:** `rw\common.ps1` (lock `Global\HultUI-r18-desktop`), `rw\hold.ps1` (gate before the lock), `rw\batchA.ps1`–`rw\batchD.ps1`, `rw\Driver*.cs`, `rw\Mem.cs`, `rw\Desk.cs`, `rw\TimerRes.cs`, `rw\idle-gate.ps1`.
- **Analysis:** `rw\an18.py` (`python -I an18.py <runs> <label> [--g2]`), `rw\ppm.py`.
- **Data:** `rw\runs\hA-*`, `hB-*`, `hC-*`, `hD-*` (`.out.txt`, `.frames.csv`, `.startup.txt`; `hC-1-reel-vk2-cap.ppm`), plus `*-windows.log`, `*-desk.log`, `locks.log`, `gate.log`, `rw\hB.log`, `rw\hC.log`, `rw\hD.log`.
- **Fixtures:** `fx\bands.y4m` (copy), `fx\big-hB-*` and `fx\big-hD-*` (copies of r17b2's bigrepo).
- **State:** the worktree is clean at aa3a3696. Build servers are shut down, and nothing of mine is running.
- **For PERFORMANCE.md:**
  - A `SetWindowPos` resize without `WM_ENTERSIZEMOVE` rebuilds a Vulkan swapchain on every step. To stand in for a user's drag, send `WM_ENTERSIZEMOVE` / `WM_EXITSIZEMOVE` around the steps.
  - WM_TIMER drag steps starve while a video plays, on GL too.
  - Junction's idle collection fires only after a cancelled walk, so scenes that test it must browse faster than the colours land.
