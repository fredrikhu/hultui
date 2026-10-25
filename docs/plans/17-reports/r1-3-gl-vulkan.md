# Round 1 survey: 3. GL and Vulkan

Real windows that never took focus (resizes via SetWindowPos NOACTIVATE), HDR display,
2160×1395 px; GPU dedicated memory per process, Skia cache usage, GL/Vulkan call counts
from temporary probes (worktree agent-ae3e1938c91127c3c at b9f80d0).

SATISFIED: no
REVIEW: none (only docs changed since b9f80d0)

## Candidates

- **gl-vk-swapchain-rebuild-leak.** On Vulkan with the F16 scRGB swapchain (Aperture, Reel), destroy the old swapchain before building its replacement instead of chaining it as `oldSwapchain`; with MAILBOX or IMMEDIATE every rebuild leaks the old swapchain's images.
  - Evidence: Aperture fixture library, R16G16B16A16 / ExtendedSrgbLinear: GPU dedicated 263.8 MB → 1,521.4 after 30 resizes → 2,860.7 after 60 → 2,987.8 after 61, kept after 5 s idle; Skia cache only 30.3 → 67.3 MB. ~52 MB per rebuild = 3 × 1800×1200 × 8 B. 10 resizes: IMMEDIATE 263.8 → 798.3, MAILBOX → 780.9, FIFO 178.1 → 225.8 (no leak). 8-bit (`HULTUI_VULKAN_F16=0`) no leak. Probe destroying `old` first, no oldSwapchain: 263.8 → 278.3 after 20. Deferring the destroy by a round of presents didn't help. SdlVulkanHost.cs:637, :667, :673.
  - Expected: leak closed, ~52 MB VRAM per resize; several GB per edge drag.
  - Cost: small. Re-run the resize-stretch screen check (memory resize-stretch-fix) and a Linux/KWin resize — chaining is the spec's smooth-transition path.
- **gl-vk-offscreen-scratch-pool.** Create the F16 offscreen unbudgeted, or purge scratch after replacing it.
  - Evidence: `SKSurface.Create(_grContext, budgeted: true, info)` (SkiaVulkanRenderSurface.cs:531); ReleaseOffscreen (:575-585) disposes into Skia's scratch cache; nothing sets a cache limit or purges (256 MB). Aperture: cache 30.3 → 67.3 (2 sizes) → 94.7 → 120.1 MB after 27 resizes over 4 sizes; `PurgeUnlockedResources(scratchOnly)` → 39.7 MB. Each size w×h×8 B (17–24 MB).
  - Expected: ~−80 MB GPU after that run; up to ~−220 MB per window worst case.
  - Cost: small.
- **gl-no-depth-buffer.** `SDL_GL_DEPTH_SIZE` = 0 (SDL default 16 gives a 24-bit depth buffer Skia never uses).
  - Evidence: SDL3Window.cs:349-357 sets stencil 8, never depth; context reports depth=24 stencil=8. WinForms host already asks `DepthBits = 0` (Wgl.cs:93). Junction 2160×1395: GPU dedicated 57.8 → 49.1 MB; GL call counts identical. ~2.9 B/px → ~24 MB maximized 4K. Every GL window.
  - Expected: −15 % GPU dedicated per GL window.
  - Cost: one line; stencil kept.
- **gl-vk-one-submit-per-frame.** Flush without submitting in EndFrame and Present; submit once per frame (Vulkan in Present; GL at the start of the next frame so no in-frame glGetError waits on the driver thread).
  - Evidence: GL does 3 flushAndSubmit per frame: SkiaRenderContext.cs:128, SkiaGLRenderSurface.cs:110-111. One glGetError per submit (204 in 62 Junction History frames); glFlush 0, no finish callbacks. Under NVIDIA's threaded driver the first glGetError per frame waits for the driver thread. Vulkan F16: 2 Skia vkQueueSubmit per frame + host barrier submit (SdlVulkanHost.cs:1098) = 3; 8-bit 1+1.
  - Expected: Flush is 19 % of a Junction frame; in-frame glGetError 3 → 0, Vulkan F16 Skia submits 2 → 1. Confirm with HULTUI_TRACE that the wait doesn't move into SDL_GL_SwapWindow.
  - Cost: small. GL out-of-memory detected one frame later.
- **gl-vk-resize-duplicate-frames.** Skip a live-resize frame when pixel size and scale equal the last drawn and nothing is dirty; today RESIZED, PIXEL_SIZE_CHANGED and EXPOSED each draw and present.
  - Evidence: Application.cs:311-321, from SDL3Window.cs:641-666. Junction GL, 10 resizes: 29 frames, 25 live, 15 at an unchanged size. Aperture Vulkan F16, 30 resizes: 72 frames, 71 live, 41 unchanged.
  - Expected: frames per resize step 2.4–2.9 → ~1; gain mostly on GL (memory says halving frames left Vulkan MAILBOX resize CPU unchanged).
  - Cost: small; needs the stretch screen check.

## Examined, not worth it

- GL GRContext cache limit / purge-on-idle: Junction Skia holds ~5 MB real GPU memory (4 MB A8 glyph atlas + ~1 MB); rest of the 16–17 MB is FBO 0 + stencil. Purge freed 1.7 MB cache, 0.0 MB process GPU.
- GL default framebuffer without stencil: 3.4 MB more; Vesper uses stencil on FBO 0 twice a frame → pixel risk.
- Video per-frame GPU churn: none (GL Reel 0 TexStorage2D, 1.36 MB upload = the YUV frame; Vulkan 0 vkAllocateMemory; per-frame view/framebuffer wraps must not be cached per memory vulkan-renderer).
- Start-up GPU work: 10 programs / 41 textures (Junction GL), 11 pipelines (Aperture Vulkan); SkiaSharp 3.119.2 exposes no persistent program cache.
- F16 MAILBOX base cost (263.8 vs 178.1 FIFO vs 78.9 8-bit): decided.
- Junction History 1 MSAA atlas resolve per frame from Skia's path atlas (graph): paint-side.
- UI loop spins while Junction loads Branches: up to 494 pumps between frames (9,217 in 2.5 s) — posted-work wakes; handed to .NET and app workloads.

## Needs the user (resolved by the orchestrator)

- **Idle wake-ups.** When nothing is dirty, Application.cs:451-457 waits only 16 ms → every idle app wakes ~65×/s, renders 0 frames, ~150 B per wake (~10 KB/s). Blocking until the next animation, a11y-coalesce or display-colour-poll deadline → ~0 wakes/s (10/s on Vulkan with the 100 ms poll). Asked whether idle CPU counts as a bar. **Orchestrator: yes** — added an idle bar to the brief (2026-10-07); this is now candidate **runtime-idle-wakeups**, overlapping the .NET expert.
