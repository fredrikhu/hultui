# Round 5 survey: 3. GL and Vulkan

**Base and method.** Measured at 969f3308, round 4's build (`ba817e80..969f3308`), with `HULTUI_A11Y=0`. Every run used a real window that never took focus:
- default frame;
- SDL's no-activate hint set in process and in the environment;
- a CBT hook on the UI thread that makes each window `WS_EX_NOACTIVATE` when it is created and refuses every activation;
- a 5 ms foreground guard;
- a posted `WM_CLOSE` to end each run, then the Application disposed.

**Focus and safety record.**
- **Guard:** 0 events over 36 launches.
- **Probe hook:** refused 9 activation requests, all in the 3 minimize/raise runs, all for the probe's own window.
- **Screen:** never read. Pixels came only from `HULTUI_VULKAN_CAPTURE`, which reads the swapchain in process.
- **Watchdog:** stopped two runs after more than 1 s without a frame.
  - **Real GPU stall.** The F16 drag with prototype P1 (targets kept at the allocated size). `nvlddmkm` event 153 was logged at 02:35:52 and 02:35:54, starting before the watchdog fired. The same event also appears once on 2026-10-23 with no probe running. I did not repeat the run.
  - **Probe artefact.** A 4K lavfi source never delivered a frame. No GPU event; the window was idle.
- **Device lost:** none.

**Probe code** is in the scratchpad `r5gl/`:
- `host/` (R5GlHost): modes steady, video, drag (cross-thread or same-thread steps), json, minimize, idle.
- `run.ps1` and `vvl.ps1`.
- `r5gl-probe.patch` plus `R5GlProbe.cs`: the counters and two prototypes, P1 `R5_KEEPALLOC` and P2 `R5_LAZY`/`R5_LAZYBROKEN`.
- Output in `runs/` and `vvl/`.

The patch was reverse-applied and the probe file deleted; the worktree is clean at 969f3308.

SATISFIED: no

## Review

- **8960cf36 (kept SKImage, no per-frame closure): no objection.**
  - The wrapper is always dropped before its image: released owners are drained at `BeginFrame`, and the host destroys forgotten slots after a device wait, at the end of Present or at the next lend.
  - Bytes per frame at HEAD, 1080p120 HEVC clip, 960×540 window, real loop: F16 110 B, GL 86 B. One interop wrapper made per run.
  - The 8-bit swapchain still costs 2,054 B per video frame (candidate 1).
- **91b4e5a7:** the 8-bit limit of 2,500 B per frame should drop to about 150 B if candidate 1 lands.
- **0607f2f7 (`LiveFrameRequests`):** as designed. While a drag is running, timer frames no longer redraw for posted work that no frame can run; those frames showed the same picture. A video in a second window still stalls (see Examined).
- **d9d465f2 (kept F16 offscreen):** an 80-step drag now makes 4 offscreens plus 1 at release. The swapchain target is still wrapped again at every step: 80 wraps per 80-step drag on both F16 and 8-bit (candidate 2).
- **a193327e:** maximize, restore and full screen now honour the hint. Minimize and Raise still ask for activation (candidate 3).
- **b3df6aae and ec4ced5:** no effect in my area.
  - Idle: 0 loop passes, 0 wakes and 0 frames in 8 s on F16, 8-bit and GL, with and without a paused 1080p video.
  - Steady frame (one box repainted): 0.0 B on F16, 8-bit and GL.
- **Measurement trap.** A `SetWindowPos` from another thread or process makes Windows send a `WM_PAINT` after each growing step. SDL turns it into `EXPOSED` with `data1 = 0`, and the event watch draws a second full frame at the same size.
  - Effect: 40 of 120 frames in an 80-step grow-and-shrink drag, on GL and Vulkan.
  - Steps made on the window's own thread, as `DefWindowProc`'s sizing loop makes them, draw 80 frames in 80 steps with no paint frames. I tested this by posting each step to the loop with `PostThreadMessage`.
  - Round 4's r3b9b-driver drags (110 frames for 80 steps) and their CPU and byte totals included these extra frames. Future probes should make the steps on the window's own thread.

## Candidates

- **vk-sdr-video-lazy-barrier** (from vk-sdr-video-frame-target-wrap)
  - **What:** on the direct 8-bit path, keep the swapchain target after a GPU-source frame too, and submit the host's acquire barrier lazily. The barrier goes in just before Skia's first `vkQueueSubmit` of the frame, through a trampoline returned by the host's `GetProcedureAddress`, or at Present if Skia submitted nothing.
  - **Evidence (allocation):** 1080p120 HEVC, 8-bit. HEAD allocates 2,054 B per frame and makes one target wrap per frame. The prototype allocates 112 B per frame (F16 at HEAD: 110 B; GL: 86 B) with 0 wraps.
  - **Evidence (ordering):** Skia makes one submit per frame, always after the interop submit. In 637 + 345 video frames it never submitted before the GL draw, so the barrier still lands after mpv's draw — the reason 68424c2 wraps a fresh target on these frames today.
  - **Evidence (correctness):**
    - Validation layer (core + sync) loaded, 0 messages.
    - Positive control (barrier paid only at Present): `VUID-vkCmdDraw-None-09600` ×10 and `SYNC-HAZARD-WRITE-AFTER-WRITE` ×10.
    - Ramp-clip capture brightness climbs 0 → 99.
  - **Code:** SwapchainTargets.cs:56-60, SkiaVulkanRenderSurface.cs:467-474, SdlVulkanHost.cs:328 and :1118.
  - **Expected:** removes a per-frame allocation from steady video frames on SDR displays: −95 %, about 2 KB → 0.1 KB per frame, about 240 KB/s less at 120 fps. Also one render target, image view and framebuffer fewer per video frame. The build step must time mpv's render phase to confirm the barrier placement costs nothing.
  - **Cost:** medium. Internal only, no API or pixel change. The trampoline needs a lookup from queue to host, because each window has its own device. The same rule could replace `KeptTransitionNow` on every direct-path frame.

- **vk-drag-kept-target-wraps** (8-bit only for now; from the same Waiting item)
  - **What:** wrap a kept target at the swapchain's allocated size, clip its canvas to the window, and leave the window size out of the target's key, as d9d465f2 did for the offscreen.
  - **Evidence:** same-thread 80-step drag. HEAD makes 80 wraps (2,048 B, an image view and a framebuffer each) and allocates 2,205 B per step. The prototype makes 12 wraps (3 per swapchain build) and allocates 512 B per step (−77 %). Validation clean.
  - **F16:** HEAD also makes 80 wraps, at 2,434 B per step. The F16 prototype produced the GPU stall described above. F16 stays on window-sized targets until a build step explains the stall, validation run first.
  - **Code:** SkiaVulkanRenderSurface.cs:580 (key includes the window size), :619 (wrap at the window size); the offscreen clip pattern is at :763-795.
  - **Expected:** churn bar on drag steps (−77 % bytes per 8-bit step) and 68 fewer image-view-plus-framebuffer pairs per drag. Time impact unknown; round 3 measured −12 % median for kept targets at a fixed size.
  - **Cost:** small. Uses the public `AllocatedWidthPx`/`AllocatedHeightPx`. No API change; needs a mid-drag capture to confirm no pixel change.

- **sdl-hint-minimize-raise** (test hygiene, no perf bar)
  - **What:** with the no-activate hint set, run `SDL_MinimizeWindow` and `SDL_RaiseWindow` inside `WindowsActivation.RefuseIfUnwanted()`, as a193327e did for maximize and restore. Alternatively, set `SDL_HINT_WINDOW_ACTIVATE_WHEN_RAISED=0` in process.
  - **Evidence:** GL and Vulkan, default frame.
    - Raise of a minimized window asks for activation once, inside the call. With the raise hint at 0 it asks 0 times.
    - A Minimize after a Raise asks once, inside the call, for the window itself — even through `SW_SHOWMINNOACTIVE`.
    - The first Minimize of a never-activated window asks 0 times.
    - The window still minimizes after a refused request.
  - **Code:** SDL3Window.cs:925-929 and :965-972.
  - **Expected:** none; it keeps the never-take-focus rule for test runs.
  - **Cost:** small.

## Examined, not worth it

- **RGBA8 interop image on 8-bit swapchains.**
  - Numbers: the F16 image is 17,694,720 B at 1080p. The process's local GPU memory (DXGI) during playback is 600 MB on 8-bit, 682 MB on F16 and 532 MB on GL. RGBA8 would save 8.8 MB, or 1.5 %, below the 5 % retained-memory bar.
  - Side note, not measured: on 8-bit, mpv renders at depth 16 with no dithering and Skia converts to 8 bits without dithering, while the GL path has mpv dither to 8 bits.
- **A video in a window other than the dragged one.** `LiveFrameRequests` is process-wide, so the video's requests go to the window in the size/move loop. That window draws a clean frame per request while the video's own window stays undrawn (SDL3Window.cs:762-790, Application.cs:367-371). No app opens a second window with video.
- **Video frame residue:** 110 B on F16 and 86 B on GL, already at the 0.1 KB target.
- **meridian-response-frame-flush, GL side.**
  - Setup: GL window; the perf suite's 19,000-line highlighted JSON in a read-only 12 px TextArea (Consolas and the default monospace face); opened in one frame.
  - From the paint pass to the end of the frame (Flush + Present): 0 B managed and no GC.
  - The flush rasterizes about 20–22 KB of glyph images and adds 2 GPU resources (+0.07 MB).
  - The frame's one GC (gen 2, blocking, compacting) runs in layout, before paint.
  - Conclusion: the viewer does not explain the 86–152 ms flush. A likely suspect is a GC suspension started by Meridian's tokenize worker. Handing it back to experts 8 and 4: look at `HULTUI_FRAMELOG`'s GC columns, or pause the worker.
- **Submit counts:** one acquire-barrier submit per Vulkan frame; F16 makes 2 Skia submits per frame and 8-bit 1. 61edb19 showed submit counts do not move frame time.
- **GL resize re-wrap:** GL re-wraps its surface at every drag step (part of its 2,081 B per step). It cannot keep the surface, because FBO 0's size and the bottom-left flip must match.
- **vk-startup-preload:** stays in Waiting with no new evidence, because a survey cannot take timings. The preload only runs with `HULTUI_GL_PRELOAD=1` (SDL3Window.cs:1893), and only Reel calls it.

## Needs the user

- No new decisions.
- For information: the F16 kept-target prototype run coincided with two GPU errors (`nvlddmkm` 153) and a stall over 1 s. It was stopped and not repeated.
