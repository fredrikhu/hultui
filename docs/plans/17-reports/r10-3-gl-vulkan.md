# Round 10 survey, expert 3: GL and Vulkan

SATISFIED: yes

I measured at 3462f18e (detached; `src` and `apps` are the same as facced69), with `HULTUI_A11Y=0`. I report counts
and bytes only.

**Monitors:** the 4K at 150 % is primary (system DPI 144), with the 1440p at 100 % to its left. Every window opened at
scale 1.5:
- drag and steady runs: 1200×800 px;
- video: 1440×810 px;
- web view: 2160×1350 px (a 2160×780 page) and 3240×1740 px (a 3240×1170 page).

**Real windows: 29 guarded launches.** Each had the default frame, the in-process no-activate hint, the CBT hook plus
`WS_EX_NOACTIVATE`, the 5 ms foreground guard, and a posted WM_CLOSE.
- The guard fired 0 times and the hook refused 0 activations.
- The screen was never read; only the probe's own frames were read, in process.
- No device lost and no stall over 1 s. The longest gap was 439 ms, the first F16 drag step (the known first F16
  swapchain build).
- The last `nvlddmkm` event is 2026-10-25, the same before and after the runs.
- The F16 kept-target drag was not run. The worktree is clean.

REVIEW: none.
- **Steady frames:** 0.0 B on GL, Vulkan 8-bit and F16, JIT and AOT (least of 5 rounds of 120 frames).
- **8 s idle after a drag:** 0 frames, 0 loop passes, 0 wakes in all six. One GL JIT run once showed 3 frames, 130
  passes and 0 wakes; two repeats showed 0, so I put it down to the user's pointer.
- **Drag bytes are unchanged from round 9** (JIT/AOT, 1 frame a step): GL 2,080/1,984 B a frame, Vulkan 8-bit 136/40,
  F16 2,136/2,040. The drag clip holds (worst +1.3 px).
- **7e964aeb (held resize):** with one post waiting, a held edge draws 1 frame in a second and 1 frame a step, on GL and
  Vulkan 8-bit. Round 9's survey measured 64–65 frames a second and 2 a step.
- **f5609009 (cached interop check):**
  - Web page on screen: steady 0.0 B on Vulkan, JIT and AOT (round 9: 24 B).
  - Video through mpv: Vulkan 8-bit and F16 allocate exactly what GL does on the UI thread (14.6 B a frame JIT, 12.2
    AOT; see Examined). The render path adds 0 B.
- **58aeb965 (hidden WebView2 page gives GPU memory back), DXGI local, this process:**

  | Renderer, page | Shown | Hidden | Change |
  |---|---|---|---|
  | GL, 2160×780 | 98.1 MB | 83.3 MB | −14.8 (−15 %) |
  | GL, 3240×1170 | 169.4 MB | 137.6 MB | −19 % |
  | Vulkan 8-bit, 2160×780 | 146.5 MB | 102.0 MB | −30 % |
  | Vulkan 8-bit, 3240×1170 | 277.1 MB | 182.0 MB | −34 % |

  - Hidden memory stays flat over 5 hide/show cycles, JIT and AOT.
  - Vulkan shown again: +15.9 MB at the large page (the accepted one-off; it was 7.4 MB at 100 %), 0 at the small page.
  - The page frame arrives 15–39 ms after a show.
  - Steady 0.0 B with the page shown and after a hide and show (GL, Vulkan, JIT, AOT).
- **74cf844c (Meridian disposes a preview hidden for a minute), with the probe's shortened deadline:**
  - Idle 8 s, shown or hidden: 0 frames, 0 page frames, 0 passes, 0 wakes.
  - The deadline costs 1 pass and 1 wake. After the dispose: 0.
  - GL hidden 83.3 → 45.3 MB; Vulkan 102.0 → 71.5 MB. The browser processes go 6 → 0.
  - The next first page frame takes 438 ms.
- **b44b4db5 (`WS_SYSMENU`):** the new code runs only on WM_STYLECHANGING for GWL_STYLE, never in the frame path.
- **The other commits** touch no present, resize, GL or Vulkan code. The `SkiaRenderContext` changes (b4f182b5,
  f9a7cc74) leave every steady frame above at 0 B.

CANDIDATES: none.

WAITING, RE-RANKED:
- **uitests-workareas-before-dpi-aware: still true; still worth doing as a small test fix (no perf bar).**
  - A DPI-unaware thread sees the 4K's work area as 2560×1392 at (0,0). Per-monitor v2 sees 3840×2088.
  - Three files call `WorkAreas()` before `new Application`. b44b4db5 added the third:
    - WindowPlacementTests.cs:29 and :52;
    - WindowStateActivationTests.cs:65;
    - CustomFrameSystemMenuTests.cs:47.
  - Do it with the next UiTests change.
- **F16 kept drag targets: stays deferred; not run.**
  - An F16 drag frame still costs 2,000 B more than 8-bit (2,136 against 136 JIT, 2,040 against 40 AOT). That is about
    168 KB for an 84-frame drag.
  - The target's key includes the size (SkiaVulkanRenderSurface.cs:677–691), so every F16 step wraps a new target.
  - This happens only during a drag, never in a steady frame. Round 5's `nvlddmkm` reason stands.
- **gl-threaded-optimization-profile: still information only.**
  - `SwitchToThread` per paced frame: mean 378 (JIT) and 376 (AOT), max 723 and 644. Round 9: mean 357 and 344, max
    456 and 578.
  - The walk happens once per launch, at start-up; 0 at idle. It is the user's driver setting, with no framework lever.
- **Open, 8a38ef4: still nothing here can measure it.** This machine has only the RTX 4080 (no integrated GPU), and no
  KWin. The change is Windows-only, and Linux keeps the chained path anyway.
- **Open, 4315c4b: partly answerable now; the ledger's text is stale.**
  - WSL Fedora has had Mesa 26.2.3 since 2026-10-09 (EGL and DRI with llvmpipe and d3d12; Vulkan through Dozen and
    lavapipe; memory `wsl-fedora-testing`).
  - On Mesa, depth 0 plus stencil 8 resolves to 24/8: nothing fails and nothing is saved. Mesa has no stencil-only
    config. The gfx line reports this (SDL3Window.cs:1675–1679).
  - AMD and Intel on Linux use the same Mesa config lists, so I expect the same result there. This is an inference, not
    a measurement.
  - AMD and Intel on Windows (WGL) still cannot be measured here. Here, NVIDIA reports depth=0 stencil=8.

EXAMINED, NOT WORTH IT:
- **Bytes per video frame (new probe):** a 60 fps clip through mpv, Gpu sink.
  - UI thread: 14.6 B a frame (JIT) and 12.2 B (AOT). All threads: 47.6 and 40.3. GL, Vulkan 8-bit and F16 are
    identical.
  - All of it comes from mpv's 9 `time-pos` changes a second:
    - on the UI thread, 96 B (JIT) or 80 B (AOT) each: `MediaPlayer.Apply`'s `ReactiveRuntime.Batch(() => …)` closure
      (MediaPlayer.cs:276);
    - on all threads, 312 or 264 B each, adding the backend's event record and the `Dispatch` closure (MediaPlayer.cs:221
      and :271).
  - The render path allocates 0 B. That is 2.8 KB/s in total, under the 0.1 KB-a-frame target.
  - Paused for 4 s: 0 frames, 0 passes, 0 wakes.
- **GPU memory left after a web view is disposed:**
  - GL: 29.0 MB with no engine; 45.3 MB after the first controller is disposed (+16.3); 52.8 MB after a second (+7.5
    more). It then stays flat over 5 open/close cycles and after the engine is disposed.
  - Vulkan 8-bit: 50.2 → 71.5 MB (+21.3).
  - It is not Skia's cache. That stays at 15.2 MB in 5 resources, and scratch and full purges free 0 MB of DXGI memory.
  - It is not `SaveFrame` either: the numbers are the same with and without it.
  - It is NVIDIA's GL driver pool:
    - Before any web view, 3 page-sized textures made and deleted add 22.3 MB that stays after the delete.
    - After a dispose, 3 such textures cost 0 MB of new memory and 6 cost 22.4 MB, so the freed memory is reused.
    - This is round 2's gl-image-retained-after-dispose GL half, and it predates 58aeb965: step 12's base run went
      26.5 → 41.6 MB too. There is no framework lever.
- **F16 against 8-bit GPU memory:** 103.5 against 31.5 MB at 1200×800 px. That was decided in round 3.

NEEDS THE USER: none.

FILES: all under `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\3484c586-c631-4af0-957d-95e7a03cac41\scratchpad\r10-gl-vulkan\`
- Probe host:
  - `host\R10Host.csproj`: round 9 step 12's host, built against this worktree. The AOT publish is `aot\R10Host.exe`.
  - `src\Program.cs`, `src\R9.cs` (drag), `src\R12.cs` (webhide).
  - `src\R10.cs`: the new `videobytes` mode. It uses the user's own `native\mpv\out\win-x64\libmpv-2.dll` from the
    primary checkout through `HULTUI_MPV_PATH`, read only.
  - `src\SkiaCache.cs`: Skia cache usage and purges by reflection, plus the GL pool test.
  - Switches: `PROBE_SKIA`, `PROBE_PURGE`, `PROBE_NOSAVE`, `PROBE_GLPOOL`.
- Runners: `run.ps1` (one launch; prints `nvlddmkm` events) and `batch.ps1` (sets steady, post, video, video-aot, regl,
  web, web2, pool, walk).
- `monitors.ps1` and `monitors-aware.ps1` (work areas under each DPI awareness).
- Logs: `batch-*.log`, `batch2-*.log`, and `runs\*.out.txt` / `runs\*.err.txt`.
- Diffs read: `diff-7e964aeb.txt`, `diff-f5609009.txt`, `diff-58aeb965.txt`, `diff-74cf844c.txt`, `diff-b44b4db5.txt`,
  `diff-ff6125a6.txt`, `diff-f9a7cc74.txt`, `diff-b4f182b5.txt`. Also `commits.txt` and `src-stat.txt`.
- Build logs: `build*.log`, `publish*.log`.
