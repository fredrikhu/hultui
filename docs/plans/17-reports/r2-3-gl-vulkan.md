# Round 2 survey: 3. GL and Vulkan

Real windows that never took focus, `HULTUI_A11Y=0`, Borderless 1200×781 px on the 100 % HDR
display (Vulkan picks F16 MAILBOX); call counts from wrappers on Skia's GL and Vulkan proc
resolvers; GPU memory from DXGI `QueryVideoMemoryInfo`, nvidia-smi and Skia
`DumpMemoryStatistics`. Worktree clean at fc807ec. Probe, run script and patch in the session
scratchpad `r2-gl/`.

SATISFIED: no

## Review of round 1

- **8aed816 (objection): an idle pass is not 0 B on a real window.** `SdlWindowingPlatform.cs:84` wraps the callback in a new lambda per pump: 88 B per pass (idle 5.7 KB/s GL, 11.9 KB/s Vulkan), every frame too. FrameAllocationTests runs headless. → sdl-pump-closure-per-pass (= runtime-sdl-pump-closure).
- Note: real-window steady frames are 608 B on GL (88 pump + ~520 from 5 box shadows, `SKMaskFilter.CreateBlur` per draw) and 2,658 B on Vulkan (vk-swapchain-target-per-frame). The suite draws on a raster surface and sees none of it.
- 7a827e9: nothing found (82 builds for 80 resizes + 2 at start-up; minimize/restore without a frame: no rebuild, DXGI flat). The zero-extent skip path orphans the swapchain — older than 7a827e9, see the first candidate.
- 1785d16 working (Skia purgeable 0.0 MB after an 80-step drag). 0f5816a working (GL 1200×781: 28.7 MB DXGI). 12d1480 on Vulkan now checked: shared-pixel PNG reads back exact from a reused-frame capture on F16 and 8-bit. f3a31d6: nothing in this area.

## Candidates

- **vk-minimized-swapchain-lost** (correctness + idle). A Vulkan window that draws a frame while minimized never draws again, and its loop spins a core. Minimized → 0×0 extent → acquire OUT_OF_DATE → `Rebuild` (SdlVulkanHost.cs:974) hits the skip at :582-588, and :585 nulls `_swapchain` without `vkDestroySwapchainKHR`. On restore nothing rebuilds (SkiaVulkanRenderSurface.cs:323 returns early); `AcquireNextImage` (:961) returns null forever: 0 of 20 frames drawn (F16 MAILBOX, 8-bit, FIFO; GL 21 of 20). A later resize fails: `vkCreateSwapchainKHR: ErrorNativeWindowInUseKHR`. The spin: a dirty window that cannot begin a frame never moves the deadline, `Application.cs:452` waits `Math.Max(1, remainingMs)`, which returns at once: restored after a dirty frame 52,595 passes/s, 4.65 MB/s garbage, 0 frames; minimized with a 30 fps ticker 47,905 passes/s (GL 74). Triggers: anything that draws while minimized (Reel/Aperture video, image arrival, animation). Prototype (destroy on skip + retry at acquire): restore 21/20, resize 21/20, idle after restore 52,595 → 64.6 passes/s; without the loop part it still spins while minimized. Small–medium. Test: opt-in UiTest minimizing with SW_SHOWMINNOACTIVE.
- **gl-vk-one-submit-per-frame** (confirmed). GL: 3.00 glGetError per frame (SkiaRenderContext.cs:128 carries the work; SkiaGLRenderSurface.cs:113-114 are empty submits that still check errors). Vulkan F16: 2.00 Skia vkQueueSubmit + the host barrier (SdlVulkanHost.cs:1114); `offscreen.Flush()` (:369) is a no-op today. Move the final flush to Present, release held video frames after it (`ReleaseHeldFrames`, SkiaRenderContext.cs:129). GL 3 → 1 driver round trip, Vulkan F16 2 → 1 submits, pixels identical; time vs Flush (19–28 % of app frames) needs build timing. Small; DevTools "Flush" moves into "Present".
- **gl-vk-resize-duplicate-frames** (confirmed). Live-resize watch draws only when `UpdateSize()` reports a change or the window is dirty (SDL3Window.cs:674 discards the result, :688 draws a full frame per RESIZED, PIXEL_SIZE_CHANGED and EXPOSED, each marking layout and raising Resized). Per SetWindowPos step: 2 frames + ~1 from WM_PAINT when growing; 30-step drag 89 frames; Vulkan F16 80-step drag 514 Skia + 217 host submits. 3 → 1 frames per step. Small; needs the during-resize stretch sampling check.
- **vk-swapchain-target-per-frame.** Keep one SKSurface per swapchain image for the swapchain's life; after each acquire submit a pre-recorded barrier back to COLOR_ATTACHMENT_OPTIMAL. Today per frame: GRBackendRenderTarget (SkiaVulkanRenderSurface.cs:434), SKSurface.Create (:452), canvas wrapper (:347 / :371) with its ConcurrentDictionary; 776 + 1,272 B (F16) / 1,944 B (8-bit); natively 1 vkCreateImageView + 1 vkCreateFramebuffer + destroys per frame. Vulkan steady frame 2,658 → ~610 B (−77 %). Medium: changes HDR.md's invariant "never cache a render target over a swapchain image" — needs a reused-frame capture and validation with a positive control.
- **sdl-pump-closure-per-pass** (= runtime-sdl-pump-closure). `SDL3Window.PollAllEvents(onWindowClose)` directly (contravariance). Idle GL 5.7 KB/s → 0, Vulkan 11.9 → 6.2. One line + an SDL-platform idle-pass test.
- **vk-purge-disposed-image-textures** (Vulkan half of gl-image-retained-after-dispose). When an image source is disposed, purge Skia's unlocked scratch after the next submitted frame, rate-limited. 8 × 3840×2400 photos drawn then disposed: Skia keeps 175.9 MB purgeable, DXGI 287.8 vs 107.5 at start; with the purge DXGI 109.5 (−178 MB, −62 %). Bounded at the peak (same-size uploads reuse it). Up to 256 MB per window. Small.
- **gpu-aperture-startup-swapchain-x3** (confirmed, borderline). A Borderless Vulkan window builds 2 swapchains before its first frame (1440×900 → 1440×931 when the custom frame installs, SDL3Window.cs:445-447 vs :524, :537); Aperture 3 with a saved placement; Reel 1. Install the frame before settling/creating, or build lazily at the first acquire. Start-up bar needs timing. Small.

## Examined, not worth it

- gl-image-retained-after-dispose, GL half: NVIDIA's GL driver keeps it (Skia 221.7 → 10.7 MB on purge, DXGI 313.7 → 313.9); commit, not resident. No lever.
- Vulkan private bytes after uploads: +197 MB resident 10 s after dispose and purge, not in Skia or SKGraphics. Driver-side.
- GL scratch purge after a resize: 47.1 MB purgeable opacity-layer bins after an 80-step full-window opacity drag; purge frees only −11 MB DXGI; apps use opacity on small elements.
- Per-size DXGI growth during drags (~0.5 MB GL, ~0.9 MB Vulkan per new size); GL private commit during drags (working set flat): driver.
- Minimize/restore without a frame: no leak, no rebuild.
- Display-colour poll ~620 B per 100 ms on Vulkan, only without display-colour events (Aperture and Reel opt in).
- runtime-idle-wakeups: unchanged at 65.2 passes/s; left to that candidate.

## Needs the user

1. **Install Mesa in WSL Fedora** (mesa-dri-drivers, mesa-libEGL, mesa-vulkan-drivers; passwordless sudo works) so 0f5816a's depth-0 config runs on Mesa EGL (llvmpipe, d3d12) and lavapipe? Software or D3D12-layered, not AMD/Intel hardware. Only an RTX 4080 is visible here. 7a827e9 is Windows-only by construction.
2. **GL error checks** (later): a no-op glGetError for Skia removes the last driver round trip per frame, but Skia would no longer detect GL out-of-memory. Only if the build phase shows that round trip still costs time.
