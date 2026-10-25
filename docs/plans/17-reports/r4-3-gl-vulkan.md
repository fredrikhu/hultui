# Round 4 survey: 3. GL and Vulkan

At 653971d (all of round 3); real windows that never take focus (in-process hint, WS_EX_NOACTIVATE /
CBT hook, foreground guard — 0 guard events over 18 launches; the CBT hook refused 3 activation
requests in the maximize/restore/snap sequence; no screen captures). Probes reverse-applied; worktree
clean. Probe code in the session scratchpad `r4gl/` (host with fault controls, element count and
start-up probe; run/vvl/vvl-video scripts; `r4gl-probe.patch` = counters + three prototypes; `vkload/`;
logs in `runs/`).

SATISFIED: no

## Review

- Validation (core + sync): 0 messages across six cases — F16 drag and title-bar move (4 one-to-one
  builds + the exact release), 8-bit drag, maximize/restore/snap/minimize/unminimize/drag/maximize, Reel
  playing a 1080p120 clip through drag, move and hold. Positive controls fired in the same
  configurations (VUID-VkSemaphoreCreateInfo-sType-sType for a bad call; VUID-vkDestroyDevice-device-05137
  at device destroy).
- **Validation trap:** a probe run that ends with WM_CLOSE never reaches vkDestroyDevice (Run returns
  on SDL's quit before the closed window is disposed), so leak checks at device destroy stay silent.
  Probes must dispose the Application, as the apps do with `using`.
- c25f057/351e309: as designed; while minimized the walk still runs each pass while a video sink exists
  (negligible, Examined).
- 4c3c1af + 6458cc8/7864f5c/aefd2d7/16f81bc: 5 swapchain builds per 80-step drag, 0 per move. A leftover
  cost per kept F16 step predates round 3 (candidate 1).
- aa11334: the colour-poll deadline applies only to Vulkan windows without colour events and not
  minimized; Aperture and Reel use events.
- 48527ac: no issue.

## Candidates

- **vk-drag-kept-step-remakes.** While a drag keeps the over-allocated swapchain, also keep the F16
  offscreen (sized to the same 256-px bucket) and the swapchain render targets (wrapped at the allocated
  size), so a kept step creates nothing. Plain probe, F16, 80-step drag: 76 of 80 steps kept the
  swapchain, yet 82 offscreen creations (~18–26 MB F16 each), 81 synchronous submits (GPU waits), 81
  scratch purges, 87 new target wraps (2,048 B + an image view + a framebuffer each). Offscreen bucketed
  only: 10 creations, 9 sync submits, 9 purges; target wraps still 86. Causes:
  SkiaVulkanRenderSurface.cs:702-716 (OffscreenFor remakes on any size change), :769-783 (Flush +
  synchronous Submit), :524-526 (target key includes the window size, set at SdlVulkanHost.cs:1187-1194);
  round 3 measured kept targets 8-bit −12 % median, F16 flat because "the offscreen is remade per size".
  Time bar on F16 drag steps (Aperture and Reel on HDR displays): one GPU wait, one F16 allocation and
  one purge less per kept step. Small for the offscreen part (≤1 bucket each way, ~5 MB at 1804×1261,
  only during a drag); the targets part needs the allocated size on the public VulkanFrameTarget (an API
  addition). Needs a mid-drag in-process capture and validation.
- **media-modal-loop-video-rate.** Inside Win32's modal size/move loop a GPU source's repaint request
  posts a window message, and the window's size/move subclass answers it with a live frame; skip that
  frame within 20 ms of a size change (the step's own frame took the newest picture). Reel (Vulkan F16),
  1080p120 HEVC, in-process modal loop: HEAD move 57 frames / 50 mpv drops, held still 2 s 127 / 112,
  drag 285 / 11; prototype move 169 / 0, held 369 / 0, hold phase 412 / 3 (was 174 / 116), drag 289 / 12.
  Without the 20 ms skip extra frames competed with drag steps (drops 11 → 16). Cause:
  MediaPlayer.cs:235-241 coalesces repaints on `_repaintPending`, reset only by the UI-thread drain that
  never runs inside the loop, so video is drawn only by SDL's ~64 Hz timer (Application.cs:346-384).
  PostMessage never blocks (THREADING.md's rule holds). Validation clean. Follow-ups: SDL's timer frames
  become duplicates (draw only when dirty); the size/move subclass is installed only on Vulkan windows
  (SDL3Window.cs:537-538) — GL windows with video need it too. Video frames during a move or held drag
  ~64/s → the clip's 120/s, drops ~50 % → 0, for clips above 64 fps. Small–medium; no API or pixel change.
- **media-vulkan-video-frame-bytes.** Keep the Vulkan renderer's SKImage over a video's interop image
  across frames, and replace the per-frame closure in GpuFrameSink.TryRenderFrame with a kept delegate.
  Safe: the host always hands the image over in SHADER_READ_ONLY_OPTIMAL on the graphics queue, where
  Skia leaves it (SdlVulkanHost.Interop.cs:286, :565-579, :835-838). Reel F16, 120 fps clip, UI-thread
  bytes per playback frame: HEAD 488–654; wrapper kept 273–316 (interop wraps 2,645–3,260 per run → 1);
  with the delegate 209–244. Validation clean through drag/move/hold. Code: SkiaRenderContext.cs:1246-1258
  re-wraps every renderer-owned frame; GpuFrameSink.cs:102-132 builds a closure per frame. Round 3's
  "closures <1 % of Reel's frame bytes" is stale (frame fell to ~500 B after 4c3c1af). ~200 B per frame
  remain unattributed (position reports ~25 B). Churn ~500 → ~220 B per playback frame (−55 %), ~33 KB/s
  less at 120 fps. Small–medium; key the kept image by VkImage handle, drop it on slot recreation or
  ForgetGpuSource; needs a reused-frame capture with changing video content.
- **sdl-hint-honoured-placement-and-uitests-guard.** When `SDL_HINT_WINDOW_ACTIVATE_WHEN_SHOWN=0` is
  set, run the framework's own maximize, full-screen and restore calls under a thread CBT hook that
  refuses activation, and add SWP_NOACTIVATE to the two full-screen SetWindowPos calls; put
  ForegroundGuard on the unguarded real-window UiTests. Sources: WindowsPlacement.cs:125-130 (3
  activation requests), WindowsPlainFullscreen.cs:78, 91, 103, 106 (2). Under such a hook the sequence
  refused 3 activations and still maximized, restored and snapped (swapchains 2560×1369, 1200×780,
  1904×1421) without taking the foreground. 13 UiTest classes (62 facts) open real windows without the
  guard; two call EnsureForeground on purpose (Needs the user). No perf bar; keeps the never-take-focus
  rule for HULTUI_UITESTS=1 runs; real apps unchanged (hint unset). Small.
- **vk-startup-preload** (low). Make BeginGraphicsPreload useful for Vulkan: load the Vulkan driver on a
  worker (one vkEnumerateInstanceExtensionProperties) and warm the text pipeline on the main thread as
  the GL preload does; Aperture calls it. SDL3Window.cs:1865 returns at once for Vulkan, skipping the
  text warm too (Reel's call, Program.cs:41, does nothing; Aperture makes none). Round 3 AOT traces:
  SDL_CreateWindow 82–85 ms, then instance + device 114–120 ms; nvoglv64.dll and nvgpucomp64.dll load at
  the first enumeration and stay. A worker doing the device work during SDL_CreateWindow waited for that
  load, so only the load can move earlier. Start-up bar at its edge (GL preload ~10 ms on Junction):
  measure on AOT first, drop it under 10 ms. Small.

## Examined, not worth it

- Instance and device on a worker during SDL_CreateWindow: both serialize on the driver load; net gain
  under the bar for medium complexity.
- HasInstanceExtension / HasDeviceExtension enumerating on every call (4–7 calls at start-up, up to
  ~1,800 names): driver stays loaded; one-off, far under the bar.
- A minimized window with video walking its tree each pass (351e309 keeps the walk while a sink
  exists): Aperture Home with the player sheet 205 elements, ≤12k visits/s at 60 fps.
- Disposed-image purge waiting for the next frame while parked (5002e2f with aa11334): only images
  disposed in the last 1 s interval wait; posters ~0.5 MB; not worth a deadline.
- F16 first swapchain replacement after show: once per window, F16 only; later F16 and every 8-bit
  replacement ~10× cheaper; the cost is in the driver's vkCreateSwapchainKHR; a same-size rebuild after
  the first show only moves the hitch into start-up.
- Extra F16 submits per frame (4c3c1af's acquire barrier; offscreen.Flush): 54c56d9 showed the driver
  wait only moves.
- WebView interop image rebuilt on every resize step: no app uses WebView.
- Display-colour poll deadline: only for Vulkan windows without colour events (neither Aperture nor
  Reel); 10 polls/s, ~1.1 KB each.

## Needs the user

1. **Foreground UiTests:** WinFormsHostSampleTests and WebViewAccessibilityTests (12 facts) take the
   foreground by design for Tab traversal (`HostedInput.EnsureForeground`). Run them only with an
   explicit opt-in (e.g. `HULTUI_UITESTS_FOREGROUND=1`) so a `HULTUI_UITESTS=1` run never takes focus,
   or keep them in the default run?
2. Reminder: one more "aperture-survey" sign-in on jellyfin.home; cleanup of those devices and the API
   key is still pending.
