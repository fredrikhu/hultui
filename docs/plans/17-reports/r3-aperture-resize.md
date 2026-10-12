# Round 3: Aperture resize diagnosis (user report)

The user (2026-10-10): "resizing in the start screen of Aperture is a lot slower than other
applications". Diagnosed by the app-workloads expert at ae10215 (src/apps identical to 5d9be15).
Probes reverted; worktree clean. Probe code, driver, runs and logs in the session scratchpad
`aperture-resize/` (`probe/full-probe.diff` applies on ae10215).

**Bottom line.** Not the start screen: every live-resize step rebuilds Aperture's **Vulkan F16
(scRGB) swapchain**, 92 % of the step. On GL, Aperture resizes as fast as Junction and Vesper; Reel
(also Vulkan F16) is as slow as Aperture. Over-allocating the swapchain only during a drag cut the
step from ~37–39 ms to ~9 ms, JIT and AOT.

## Setup

RTX 4080; 4K HDR monitor at 150 %. Window 1800 → 2600 px wide, 1300 tall; 40 steps of 20 px each
way, 10 ms apart. `SetWindowPos` with NOACTIVATE from a separate DPI-aware driver process (windows
never focused); the call is synchronous, so its time is the step the user feels. a11y on. Release
JIT, idle-gated, P-cores, two alternated runs per config; one AOT pair.

## Measurements (median `SetWindowPos` ms per step, run 1 / run 2)

| Config | Median | p95 | Swapchain rebuild | Present | App rebuild | KB/step |
|---|---|---|---|---|---|---|
| Aperture Home, Vulkan F16 (default) | 36.98 / 36.56 | 39.3 / 39.8 | 32.3 | 0.96 | 0.22 | 333 |
| Aperture Home, GL | 4.56 / 4.71 | 6.6 / 6.2 | 0.04 | 0.10 (flush 1.9) | 0.27 | 327 |
| Aperture Home, Vulkan 8-bit (`HULTUI_VULKAN_F16=0`) | 8.77 / 8.70 | 10.7 / 10.7 | 3.7 | 0.33 | 0.23 | 328 |
| Aperture Library, Vulkan / GL | 36.13 / 38.01, 4.51 / 3.59 | | 32.9 / 0.04 | | 0.11 | 374 |
| Aperture item page, Vulkan / GL | 36.88 / 33.59, 4.40 / 5.24 | | 30.8 / 0.04 | | 0.21 | 278 |
| Junction, GL | 4.50 / 5.34 | 7.0 / 8.0 | 0.04 | 0.10 | 0 | 0 |
| Vesper, GL | 4.31 / 5.33 | 6.8 / 7.8 | 0.04 | 0.11 | 0 | 0 |
| Reel, Vulkan F16, idle | 32.65 / 34.44 | 33.9 / 36.7 | 30.3 | 1.06 | 0 | 7 |
| `HULTUI_VULKAN_FIXED_EXTENT=1` (existing opt-in) | 57.96 / 57.06 | 63.3 / 61.5 | 1.9 + 41 inside present | 50.9 | | |

- Process CPU per step: Vulkan 80–81 Mcyc, GL 13 Mcyc; 68–69 Mcyc of Vulkan's on the UI thread inside
  `UpdateSize` (driver CPU, not app code).
- Rebuild split (Home, Vulkan): `vkCreateSwapchainKHR` 22.0 ms, `vkDestroySwapchainKHR` 8.3 ms,
  surface queries ~2 ms, dropping wrapped surfaces (flush + sync submit) 0.3 ms, `vkDeviceWaitIdle`
  0.01 ms.
- Rest of the frame: style 0.02, layout 0.03–0.08, render 0.35, flush 0.54, a11y 0.35 mean.
- AOT confirms: default 37.27 / 37.93 ms, GL 5.01 / 4.98 ms.

## Causes, ranked

1. **F16 swapchain rebuilt every step: 92 %** (32–33 of 35.6 ms UI time). GL avoids it (4.6 ms per
   step); 8-bit Vulkan rebuilds in 3.7 ms (8.7 per step); Reel pays the same 30 ms with almost no UI
   work; Library and item page cost the same as Home.
2. Vulkan present ~1 ms per frame (2.7 %); GL pays ~2 ms in flush instead — no real difference.
3. Aperture's whole-app rebuild per resize event (`ApertureApp.cs:149-163`): 0.22 ms (0.6 %) but
   92 % of the bytes (333 KB per step; Junction and Vesper 0).
4. Artwork: no effect on step time (Home: 2 fetches, 2 decodes, 2 uploads per 80 steps; item page 14
   fetches from hero 128-px re-asks), all off the UI thread.
5. Harness artifact: growing steps draw a second frame (~0.8 ms) from a WM_PAINT when `SetWindowPos`
   comes from another process (1.5 frames per step), equal for every app; b471108's same-thread
   harness saw 1 frame per step.

## Fixes

**A. Over-allocate the swapchain only while a drag is in progress** (prototyped; recommended).
Between `WM_ENTERSIZEMOVE` and `WM_EXITSIZEMOVE`, build the swapchain to a 256-px bucket with
`VK_KHR_swapchain_maintenance1` ONE_TO_ONE scaling; rebuild once at the exact size when the drag
ends.

- JIT, same session: 37.65 / 38.89 → **9.16 / 9.71 ms** median; p95 39.4 / 41.5 → 11.3 / 12.4;
  process 83 → 16.5–20 Mcyc per step (GL same session 5.44 / 5.14).
- AOT: 37.27 / 37.93 → **9.16 / 9.32 ms**; p95 39.3 / 39.6 → 14.4 / 12.1; process 80 → 15.6 Mcyc
  (GL 5.01 / 4.98).
- Swapchain builds per 80 steps 80 → 4, plus one exact rebuild of 32–34 ms at release.
- Correctness: a screen capture during the held drag (window 1778 px, swapchain 2816×1536) was
  pixel-identical to the exact-size baseline (0 of 2.29 M pixels). Core validation 0 messages;
  positive control (leaked swapchain) fired `VUID-vkDestroyDevice-device-05137` ×2.
- **Finding: the existing `HULTUI_VULKAN_FIXED_EXTENT=1` opt-in is broken today.** Every present
  returns SUBOPTIMAL, which forces a rebuild each step (57–58 ms, slower than default). Keeping a
  SUBOPTIMAL ONE_TO_ONE swapchain that the window still fits: 8.57 / 8.78 ms, one 157–163 ms
  outlier step per run.
- Changes (lines at ae10215): `SdlVulkanHost.cs:493-495` enable maintenance1 by default;
  `:613`, `:684`, `:1175` over-allocate when available AND (override OR live resize), remember
  whether the current swapchain was built with scaling; `:1113-1114` no rebuild on SUBOPTIMAL in
  that state; ENTER/EXIT from `WindowsCustomFrame.WndProc` (`:296`) to the host — on EXIT drop the
  wrapped surfaces, rebuild exact, set `ResizePending`; Reel (`WindowStyle.Default`) has no
  window-procedure subclass (`SDL3Window.cs:523-526`) and needs one.
- Add-on, prototyped: cache the surface-format query in `Rebuild` (`SdlVulkanHost.cs:1177`), 1.37 ms
  on every step that keeps the swapchain: 9.45 / 9.37 → 7.98 / 8.12 ms.
- Left vs GL: presenting the over-allocated swapchain costs 3.9–4.1 ms vs 0.96 at exact size (the
  composited path).
- Cost: medium, ~60 lines + a unit test for the keep rule; run the real-window stretch harness
  (memory `resize-stretch-fix`) before shipping.
- Risk: video playing during a drag pays the composited path (~+70 % CPU measured 2026-09-27, only
  while dragging); ~13 MB more GPU memory during a drag; NVIDIA 610.x only (AMD/Intel unverified);
  Linux untouched; HDR.md's FIXED_EXTENT text needs updating.

**B. 8-bit swapchain unless HDR content is on screen** (not prototyped beyond the env override).
8.77 / 8.70 ms per step (rebuild 3.7 ms); steady present 0.96 → 0.33 ms; GPU memory 213 → 63 MB
(round 3 GL/Vulkan report). Where: `SdlVulkanHost.cs:140` `WantsWideColor` also requires a video or
GPU image source shown. Cost: a format switch when playback starts or stops (a rebuild, plus ~100 ms
shader compile on the first F16 frame, once per process). Pixels change on HDR displays (the
compositor composes 8-bit sRGB instead of the app's white scale and 2.2 decode). A + B together not
measured.

**C. Aperture rebuilds only when a size-derived value changes** (prototyped; low priority). Key:
`VisibleCards` for widths 168, 300, 150, 140, 240, 224; hero pixel steps; `HeroHeight`; library row
count. Bytes per step 333 → 26 KB (−92 %; GL 327 → 18). Time within noise. Risk: a missed size read
leaves that layout stale until the next rebuild.

## Needs the user

1. Approve A (over-allocated swapchain presented unscaled during a drag; video costs more while
   dragging; one ~33 ms frame at release; maintenance1 on by default)? Also fix or remove the broken
   `FIXED_EXTENT=1` opt-in.
2. B: accept the UI pixel change on HDR displays and a format-switch hitch when playback starts and
   stops?
3. Jellyfin cleanup: 51 more runs signed in as device "aperture-survey". No scratch file holds the
   API key (checked).
