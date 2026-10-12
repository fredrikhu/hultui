# Round 3 survey: 3. GL and Vulkan

Worktree at 5d9be15; RTX 4080, 1200×780 DIP = 1800×1170 px at 150 %, 120 Hz, Release JIT,
P-cores, `HULTUI_A11Y=0` unless stated, windows never focused. GPU memory from DXGI, Skia
`DumpMemoryStatistics`, nvidia-smi. Validation SDK 1.4.357 core + sync, loaded (deliberate
fault → 2× VUID-vkDestroyDevice-device-05137). Probes reverted; scratchpad `r3-gl/`.

SATISFIED: no

## Review of round 2

- **82e382b (objection): a minimized window rebuilds the a11y tree on every loop pass.** The minimized branch returns at Window.cs:1039-1043 before `ClearAllFlags` (:1093), so `DirtyFlags.Children` stays set and, with an AT client listening, `a11yImmediate` (:966-967) is true every pass. Trigger: one AddChild while minimized. 2,016 elements: 7.0 MB/s GL, 10.8 MB/s Vulkan (~177 KB per pass at 40–61 passes/s), UI 29.8/45.3 Mcyc/s vs 3.5; 5,016 elements 26.9 MB/s, 79.8 Mcyc/s; until restore. Realistic: Junction minimized while its file watcher refreshes.
- **7e52ff5 (objection): `ServiceUndrawnSources` walks the whole tree every pass while a minimized window is dirty, video or not** (Window.cs:860-887). Looping tween, 2,015 elements: 123k visits/s, 15.8 Mcyc/s (idle 3.5–5); a change every 33 ms, 5,015 elements: 447k visits/s, 32.1 Mcyc/s.
- No regression: 0ceecea/ff7a970 (idle pass 0 B on GL; 61 passes/s idle; 2.1–2.2 passes per tween frame), 65cdae0/e1d86a1 (120 frames/s at 120 Hz), b471108 (1 frame per resize step, validation 0), 82e382b/595b8dd swapchain side (10 minimize/restore cycles, 1 build each, validation 0; Reel minimized 0 drops). 61edb19 docs only.

## Candidates

1. **gl-vk-minimized-children-flag.** The minimized branch clears the dirt it consumed (Children; Style/Layout already cleared), keeps paint dirt: one a11y rebuild per change, not per pass (Window.cs:966-967, :1039-1043, :1093; Element.cs:993). 177–441 KB per pass → 0; O(n) per pass removed; UI 30–80 → ~4 Mcyc/s while minimized. Small; headless test with `HeadlessWindow.Minimized` + a bridge stub.
2. **gl-vk-undrawn-sources-walk** (same step). Walk for GPU image sources only when some exist (live `IGpuImageSource`/`GpuFrameSink` count or a per-window list kept at mount/unmount); `CollectGpuSources` (Window.cs:881) runs every pass of a dirty minimized window even with no video. 123k–447k visits/s → 0 without video. Small; keep 7e52ff5's "new player loads while minimized" test green.
3. **vk-purge-disposed-image-textures** (re-confirmed). Purge Skia's unlocked non-scratch resources after the next submitted frame once image sources are disposed, rate-limited (next to `PurgeScratch`, SkiaVulkanRenderSurface.cs:394-409). 8 × 3840×2400 photos drawn then disposed: Skia purgeable 176 MB, DXGI 391.5 MB → 213.2 MB after a purge (−45 %), private 550.6 → 413.1 MB. GL half stays declined (driver pool). Small.
4. **runtime-idle-wakeups** (shared with 4, re-confirmed). Idle GL 61.0 and Vulkan 60.8 wakes/s, every one the 16 ms wait (Application.cs:533). Deadlines riding on it: a11y trailing refreshes (Window.cs:824-828), Vulkan display-colour poll every 100 ms (SDL3Window.cs:1271-1294; the comment at :1253 relies on the 16 ms wake), DevTools inspector tick (Application.cs:414), an undrawn animation step (:510-515), minimized dirty windows (:643-647); WinForms host 8 ms timer (HultUIApplicationHost.cs:148). 61 → ~0 wakes/s GL; 10/s on a polled Vulkan window. Medium.
5. **vk-swapchain-target-per-frame** (re-confirmed). One `GRBackendRenderTarget` + `SKSurface` per swapchain image for the swapchain's life; F16 blit with `DrawSurface` instead of `Snapshot`. Per steady frame F16 2,054 B, 8-bit 1,819 B, GL 0; plus 1 vkCreateImageView + 1 vkCreateFramebuffer created and destroyed. SkiaSharp 3.119.2 cannot set/read a target's layout, so the host must barrier each acquired image back to COLOR_ATTACHMENT_OPTIMAL. Medium; changes HDR.md's "never cache a render target over a swapchain image" — needs a reused-frame capture and validation with a positive control.

## Examined, not worth it

- gpu-aperture-startup-swapchain-x3: a Borderless window builds 2 swapchains before the first frame; the second is +0.7 ms vs +9.7 ms for the first. Drop from Waiting.
- Display-colour poll: ~1.1 KB per poll (WindowsDisplayColor.cs:144-169), only for windows without display-colour events (Reel and Aperture use events).
- GL private bytes across minimize cycles: driver commit, working set flat.
- F16 vs 8-bit Vulkan GPU memory (DXGI 213 vs 63 MB; ~100 MB of the gap driver-side): F16 cost already decided. 8-bit offscreen without video: −3.9 % and pixels change.
- F16 resize submits: the third is the synchronous submit in `ReleaseOffscreen`, once per step.
- Vulkan private bytes after image dispose + purge: driver-side.

## Needs the user

1. **mpv-vo-thread-minimized: turn the video track off while minimized?** (`vid=no` at minimize, `vid=auto` at restore; Chromium does this for background video with audio.) Reel, muted audio, 10 s minimized: process CPU Vulkan 1080p24 H.264 145 → 90 Mcyc/s (−38 %); Vulkan 1080p60 HEVC 292 → 85 (−71 %); GL 1080p60 HEVC 290 → 84 (−71 %); vo and decoder threads → 0; audio continuous. Cost at restore: video resumes 84–300 ms after it (today 5–36 ms), A/V sync off by up to 144–170 ms for 1.1–1.4 s, and in 2 of 3 runs a 66–68 ms audio stall (today avsync ≤ 14 ms, no stall). Accept a short glitch at every restore for lower CPU while minimized, or keep today's behaviour?
