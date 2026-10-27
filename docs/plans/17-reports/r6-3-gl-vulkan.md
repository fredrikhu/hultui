# Round 6 survey: 3. GL and Vulkan

**Base and method.** I measured at ef4288ad. For my area that is the code of f31194ab: nothing in `src/HultUI.Platform.SDL3`, the Skia surfaces or `src/HultUI.Media*` changed after 6c627f27. All runs used `HULTUI_A11Y=0` and report counts only.
- **Probe host:** `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\855f4ddd-feda-4f0a-a595-a83e07af08f8\scratchpad\r6-gl-vulkan\host\Program.cs`. It is round 5's r5gl host cut down, with modes steady+idle, walk and video.
- **Call counter:** `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\855f4ddd-feda-4f0a-a595-a83e07af08f8\scratchpad\r6-gl-vulkan\host\ImportCounter.cs`.
  - It points the import table entries of NVIDIA's driver modules (nvoglv64, nvgpucomp64, nvspcap64 and others) at 60-byte machine-code stubs. Each stub does `lock inc` on a counter, picked by the thread id in the TEB (UI thread or any other), then jumps on to the real function.
  - No managed code runs on driver threads.
  - Functions counted: CreateToolhelp32Snapshot, Thread32First/Next, OpenThread, SwitchToThread, SuspendThread, CreateThread and others.
- **Runner and output:** runner `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\855f4ddd-feda-4f0a-a595-a83e07af08f8\scratchpad\r6-gl-vulkan\run.ps1`; output in `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\855f4ddd-feda-4f0a-a595-a83e07af08f8\scratchpad\r6-gl-vulkan\runs\`.
- **UiTests:** I also ran the existing `VulkanProbeTests` (steady frame, video frame, 8-bit drag) with `HULTUI_UITESTS=1`.

**Real windows: 15** (9 host launches and 6 UiTest probe launches).
- Every host window had:
  - the default frame;
  - SDL's no-activate hint set in process and in the environment;
  - a CBT hook that makes each window `WS_EX_NOACTIVATE` and refuses activation, plus `WS_EX_NOACTIVATE` set again on the window;
  - a 5 ms foreground guard;
  - a posted `WM_CLOSE` to end the run.
- **Guard:** never fired (0 taken). The hook refused 0 activations. The screen was never read.
- **GPU safety:** the last `nvlddmkm` event is still 2026-10-25 02:35:54, both before and after all runs. No device was lost and no stall lasted over 1 s. I did not run the F16 kept-target drag.
- **Worktree:** clean at ef4288ad. No tracked file was edited.

SATISFIED: no

## Review

None. The checks behind that:
- **2a332218, 7f9df4ea, 6c627f27, a18e70dd:** no objection.
  - The submit trampoline allocates nothing; it does one dictionary lookup per `vkQueueSubmit`.
  - `ClipTo` runs only when the clip changes.
  - The 8-bit-only rule (`TargetSize`) holds.
- **HEAD numbers in real windows** (GL, Vulkan 8-bit, Vulkan F16):
  - steady frame: 0.0 B on all three;
  - 8 s idle: 0 frames, 0 loop passes, 0 waits, 0 wakes on all three.
- **UiTests:** all pass.
  - `ASteadyFrame_AllocatesNothing` (F16 and 8-bit): under 100 B a frame.
  - `ASteadyVideoFrame_…` (GL, F16 MAILBOX, 8-bit): under 100 B a frame, the validation layer's log empty, brightness climbs. This needs `HULTUI_MPV_PATH`; without it the test returns at once and still reports a pass.
  - `ADrag_OverAllocates… (8-bit)`: the drag back builds 0 swapchains and stays under 1,000 B a step.
- **Other areas' round 5 commits** (blob maps, glyphs on first read, overlays, a11y): none touches the surfaces or submit or present.

## Candidates

- **id: gl-thread-walk-after-first-frame** (replaces Waiting gl-driver-thread-walk)
  - **What:** on Windows with GL, after the first frame is presented, start and join one no-op thread, then draw one more frame (or make one GL call) at once. NVIDIA's one-time thread walk then lands right after the window appears, not in the first user action that creates a thread.
  - **Evidence:** 4 GL launches with the counters.
    - **Trigger:** the first thread created after the driver loads makes the next frame walk. That frame can be a plain box repaint, not only an atlas upload. Any thread counts: 8 pool workers, one dedicated thread started and joined before any frame, or one left running.
    - **The walk:**
      - on a driver thread: 1 `CreateToolhelp32Snapshot`, 2 `Thread32First`, 13,254–14,496 `Thread32Next` (two passes over the system's ~7,500 threads, so O(threads on the system)) and 517–870 `OpenThread`;
      - on the UI thread: 306k–461k `SwitchToThread` calls while it waits. A normal paced frame makes 235–600.
    - **Never again in that process:** 0 walks in 11 later phases. These covered:
      - pool workers retired at idle (27 → 17 threads after 24 s) and grown back (+9, +10, +18);
      - growth past the earlier peak (27 → 35, 36);
      - a dedicated thread;
      - `Task.Run`.
    - **Cost per walk:** round 5 step 7's timed trace: 43–75 ms in Meridian's first response frame (its send adds 8 threads), and 162 ms at the first palette open when the pool grew at 2 s.
  - **Expected:** Meridian's first-send frame −43…−75 ms, down to the 1.2–3.6 ms of a repeat send. A first palette open up to −160 ms. This is the time bar on an app's worst frame, and it applies once per process.
    - Time to the first presented frame stays the same, because the walk comes after the present.
    - Apps that already create a thread in their first frames see no change.
  - **Cost:** small, about 20 lines in the GL path of `SDL3Window`. No API or pixel change. The build must:
    - confirm with these counters that the walk lands in the extra frame;
    - measure what the extra frame costs right after start-up on AOT Meridian, Junction and Vesper;
    - decide whether to limit it to the NVIDIA vendor string (elsewhere it costs one thread and one frame).

- **id: sdl-custom-frame-maximized-dpi** (replaces Waiting uitests-borderless-maximize-width; a correctness bug, not perf)
  - **What:** inset a maximized custom-frame window by the frame size at the window's own DPI: `GetSystemMetricsForDpi(…, GetDpiForWindow(hwnd))`. Today it uses `GetSystemMetrics`, which answers at the system DPI.
  - **Evidence:**
    - On this desk the system DPI is 144 (the 4K monitor at 150 %).
    - At that DPI, `SM_CXFRAME + SM_CXPADDEDBORDER` = 11. At 96 DPI (the 1440p monitor the test window is on) it is 8.
    - Windows puts a maximized window 8 px past each edge, so the client is 2,560 + 16 − 22 = 2,554 px. That is exactly the failing width in the two Borderless WindowStateActivationTests runs.
    - The same mistake is in `WindowsCustomFrame.cs:424-425` (NCCALCSIZE), `:281` (`ExtendFrame`'s caption margin when maximized) and `:592` (Alt+Space menu position).
    - Aperture, Junction, Meridian and Vesper all use `WindowStyle.Borderless`.
    - `WindowsPlacement.cs:105` already uses `GetDpiForWindow`.
  - **Expected:** no perf bar.
    - It fixes a 3 px non-client band at every edge of a maximized app window on a monitor whose DPI is lower than the system DPI. That band includes the top-row and corner pixels, so the screen edge misses the close button.
    - On a monitor with a higher DPI than the system's, it fixes 3 px of content clipped off-screen.
    - The two Borderless UiTest runs pass again.
  - **Cost:** small. No API change. Pixels change only where the inset is wrong today.

- **id: vk-startup-preload** (kept, low)
  - **What:** load the Vulkan driver on a worker during the app's pre-window work, and warm the text pipeline meanwhile.
  - **Evidence:**
    - Reel's `BeginGraphicsPreload(RendererKind.Vulkan)` does nothing: `BeginPreload` returns at `if (UseVulkan) return;` (SDL3Window.cs:1904), before the text warm.
    - Aperture makes no call.
    - Round 4: nvoglv64 and nvgpucomp64 load at the first instance enumeration. Instance plus device take 114–120 ms and cannot overlap `SDL_CreateWindow`.
  - **Expected:** the start-up bar, at its edge (the GL preload gained ~10 ms on Junction).
  - **Cost:** small. A survey cannot time this; the build times AOT start-up first and drops it under 10 ms.

Waiting item closed: **live-frame-requests-public**.
- No consumer exists.
- Since 2a332218, a GPU source that only posts is drawn at the timer's ~64 fps during a move (56/124 frames), as it was before ee6d0bb0.
- Making `LiveFrameRequests` public would add API that nothing uses. Reopen it if a third-party GPU source asks.

## Examined, not worth it

- **Keeping pool threads alive:** `System.Threading.ThreadPool.ThreadsToKeepAlive` and `ThreadTimeoutMs` both exist in 10.0.14 CoreLib. Not needed: the walk never recurs (0 walks after retire-and-regrow, and 0 past the old peak).
- **Vulkan windows and the walk:**
  - Vulkan UI only: 0 walks, 0 `SwitchToThread`, 0 driver `OpenThread` across +10 and +18 threads.
  - With mpv playing through the GL interop: Gpu mode, 560 frames, threads 11 → 37 after opengl32 loaded. Result: 0 walks and 6 `SwitchToThread` a frame.
  - So Aperture and Reel never pay the walk.
- **The GL per-frame driver spin:**
  - The UI thread calls `SwitchToThread` 235–600 times in a paced GL frame (815 unpaced); Vulkan makes 0.
  - This is the `glGetError` wait under NVIDIA's threaded driver that DECISIONS "One submit per frame" measured: UI cycles −1…−3 % for 3 → 1 calls.
  - Removing it needs either no `glGetError` at all (Skia then loses GL out-of-memory detection; declined) or Threaded optimization off in the driver. Neither is new.
- **F16 kept drag targets:** not run, because of round 5's GPU errors. Still deferred.
- **GL drag re-wrap, RGBA8 interop image, submit counts, GPU cache purge:** no new evidence since rounds 1–5.

## Needs the user

- No new decisions.
- For information: turning Threaded optimization Off in an NVIDIA profile for the GL apps would remove both the one-time walk and the per-frame spin. It is a driver setting on the user's machine; the framework-side candidate above does not depend on it.
