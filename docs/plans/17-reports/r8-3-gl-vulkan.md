# Round 8 survey, expert 3: GL and Vulkan

SATISFIED: yes

Measured at 27020cdb (detached; `src` the same as df45b4a2) with `HULTUI_A11Y=0`. I report counts only. I rebuilt round 7's probe host as R8GlHost in JIT and NativeAOT. The AOT publish needed `C:\Program Files (x86)\Microsoft Visual Studio\Installer` on PATH for vswhere.

**Real windows: 13 launches.** Each one had the default frame, SDL's no-activate hint set in process, the CBT hook plus `WS_EX_NOACTIVATE`, the 5 ms foreground guard and a posted WM_CLOSE.
- Guard fired 0 times and the hook refused 0 activations.
- The screen was never read.
- No stall over 1 s and no lost device. The only frame gap over 100 ms in any run was the first frame after the window was made.

**GPU safety:** the last `nvlddmkm` event is still 2026-10-25 02:35:54, both before and after the runs. No TDR (event 4101). The F16 kept-target drag probe was not run. Worktree clean; no tracked file edited.

REVIEW: none.

- **Steady frames and idle at HEAD**, JIT and AOT, on GL, Vulkan 8-bit (B8G8R8A8Unorm, Mailbox) and F16 (R16G16B16A16Sfloat, ExtendedSrgbLinear, Mailbox, HDR on):
  - Steady frame: 0.0 B on all six runs.
  - 8 s idle: 0 frames, 0 loop passes, 0 waits, 0 wakes on all six.
  - Vulkan: 0 walks and 0 `SwitchToThread` calls, including during the later pool burst.
- **Thread-walk trigger (dfd1a5b5, b788b2ef), app-like start (walkrun):**
  - The walk fired in 5 of 6 GL launches (3 of 3 JIT, 2 of 3 AOT), exactly 1 walk each.
  - The walk's frame is the 4th frame, painted 236–247 ms after the first (round 7: about 232).
  - Pool burst afterwards: 0 walks. 8 s idle: 0 frames, passes or wakes.
  - In the third AOT launch the driver stayed single-threaded (0 walks, 0 `SwitchToThread`), although the framework drew its trigger frame. This is round 7's AOT observation (5 of 12), not a framework miss.
  - Vulkan walkrun: 3 frames, 0 walks.
  - In steady mode the walk now starts 2–3 ms into `app.Run` instead of 64–105 ms. The cause is timing, not code: that mode draws 720 unpaced frames before Run, and this time they outlasted the 200 ms `DriverWalkSettleMs`, so the rest test passes on Run's first pass. It is not an app-like start.
- **18fe58a0 (text baseline on a whole device pixel), GPU paths:**
  - Per frame it adds only flag writes and one `TotalMatrix` read per matrix move.
  - The steady scene draws its text every frame through that path on GL, Vulkan 8-bit and F16, at 0 B.
- **f64e4674 frame robustness (d04464f6, 5715d0df):**
  - Per frame: a struct scope (two bool writes, one `SDL3Window` type test), `_saveStack.Clear()` and one `RestoreToCount(1)`.
  - A size change left pending inside a frame is safe. `ResizePending` is the Resize deadline (Window.cs:1485), and `ProcessPendingResizes` runs every pass (Application.cs:1074), so the next pass draws it. No lost frame, no busy loop.
- **e7965a2d, 775d1046 (`SaveFrame`, `HULTUI_FRAME_REQUESTS`):**
  - Per frame: one `_frameSaves.Count` check.
  - The FileSystemWatcher exists only while the variable is set.
  - Nothing in apps, samples or skills calls `SaveFrame`.
- **4da0d543 (monitors):**
  - Per pump: one static bool test.
  - The monitor list is built on first read and cached until SDL reports a display event.
  - No app or framework code reads `Monitors` or `Window.Monitor` per frame.
- **AsyncLocal `s_ambient` set in the `Application` constructor:** the UI thread's ExecutionContext is no longer the default one. Steady frames stay at 0 B and the burst's posts cost no UI-thread bytes. Runtime-side effects belong to expert 4.

CANDIDATES:
None.

WAITING, RE-RANKED:
- **gl-threaded-optimization-profile: still information only** (the user's NVIDIA driver setting; no framework lever).
  - Threaded launches pay a spin per paced frame in the later burst:
    - JIT 3 of 3: `SwitchToThread` mean 340–387 a frame, max 610–986.
    - AOT 2 of 3: mean 359–369, max 684–792.
  - The single-threaded AOT launch: 0. Since b788b2ef the walk itself lands while the window rests.
- **F16 kept drag targets: stays deferred.** Not run.
- **f9bceb6d window checks (Open): still not possible.** Only the 2560×1440 at 96 DPI is attached (primary, system DPI 96); the 4K at 150 % is not. Display settings untouched.

EXAMINED, NOT WORTH IT:
- **The horizontal version of 18fe58a0 (x fractions in Skia's blob key):**
  - Skia keeps a cache entry per x fraction too. But a wheel notch pans 40 layout px (`ScrollSpeed` 40, ScrollViewElement.cs:51, :118), which is 40/50/60/80 device px at 1/1.25/1.5/2×.
  - So a mouse pan keeps every blob's x fraction. Only fractional wheel deltas (a touchpad) add one entry per pan step.
  - Snapping x would move glyphs, a pixel change. Not worth it without a touchpad workload.
- **efaf947b pixel-frame draw:** reads `image.Width`/`Height` (2 native calls per pixel-frame draw) where it read managed fields before. Once per video frame; noise.
- **Diagnostic readback cost (`SaveFrame` on GL `glReadPixels`, Vulkan host copy, F16 → sRGB on the UI thread):** paid only when a frame is asked for; no hot path calls it.

NEEDS THE USER:
- **f9bceb6d window checks** (correctness, not perf), as in the ledger's Open. Needed: the 4K monitor at 150 % attached and primary at sign-in. Then:
  - maximize on both monitors, the DPI hop and the Snap Layouts flyout on the lower-DPI monitor;
  - UiTests `WindowPlacementTests` and the Borderless `WindowStateActivationTests` (`HULTUI_UITESTS=1`);
  - a Snap Layouts hover by hand.

FILES: scratchpad `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\64e7e0e2-bc1c-4069-9d42-9311fee04c9e\scratchpad\r8-gl-vulkan\`:
- Probe host, round 7's source unchanged and pointed at this worktree: `host\Program.cs`, `host\ImportCounter.cs`, `host\R8GlHost.csproj` (AOT with `-p:R7Aot=1`).
- AOT build: `aot\R8GlHost.exe`.
- Runner and monitor/DPI list: `run.ps1` (`-Exe` picks the AOT build), `monitors.ps1`.
- Stdout and stderr of all 13 launches: `runs\` (steady-{gl,vk8,vk16}, aot-steady-*, walkrun-gl-1..3, aot-walkrun-gl-1..3, walkrun-vk8).
- Round 7 build diffs for my area: `diff-app-window.txt`, `diff-sdl3.txt`, `diff-skia.txt`.
- Build logs: `build.log`, `publish.log`.
