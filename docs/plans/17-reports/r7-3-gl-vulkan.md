# Round 7 survey, expert 3: GL and Vulkan

SATISFIED: yes

Measured at 57e6547c (same `src` as 1939afcb), all runs `HULTUI_A11Y=0`; counts, except one prototype
(vk-startup-preload) timed in alternated pairs — not idle-gated (another survey's `testhost` was running), so read
those as ±a few ms.

- **Probe host:** round 6's host rebuilt in JIT and NativeAOT (`aot\R7GlHost.exe`), four modes: **steady** (bytes per
  steady frame, 8 s idle, then a pool burst of 8 threads and 30 repaints); **walkrun** (follows the framework's own
  NVIDIA thread-walk trigger inside `app.Run`, counts the walk with the import counter, logs window events, times each
  frame's pass); **vkload** (no window; times what a Vulkan preload could overlap); **loads** (when each driver module
  first appears).
- **Real windows: 39 launches**, plus 30 vkload launches with no window. Every window: default frame, SDL's
  no-activate hint in process, the CBT hook plus `WS_EX_NOACTIVATE`, the 5 ms foreground guard, a posted WM_CLOSE.
  Guard fired 0 times, hook refused 0 activations, screen never read, no stall over 1 s, no lost device.
- **GPU safety:** last `nvlddmkm` event still 2026-10-25 02:35:54, before and after; no TDR (event 4101). F16
  kept-target drag not run.
- Worktree clean; no tracked file edited.

## REVIEW

None.

- **Steady frames and idle at HEAD**, JIT and AOT, on GL, Vulkan 8-bit (B8G8R8A8, MAILBOX) and F16 (R16G16B16A16
  scRGB, MAILBOX, HDR on): steady frame 0.0 B on all three; 8 s idle 0 frames, 0 loop passes, 0 waits, 0 wakes on all
  three; first 1.5 s of `app.Run`: GL draws 1 frame (the walk's) in 3 passes and 2 waits, Vulkan 0 frames in 1 pass
  and 1 wait, 0 walks, no walk deadline. Steady GL frames measured while the walk was still armed (window always
  dirty, 720 frames): the per-pass `SetOffDriverThreadWalkIfResting` allocates nothing.
- **dfd1a5b5 and b788b2ef (the NVIDIA thread-walk trigger)** in an app-like start: the trigger set the walk off in 13
  of 13 launches (7 JIT, 6 AOT), exactly 1 walk each. The walk's frame is painted ~232 ms after the first frame's
  paint (200 ms after the first frame was shown), 0.7–1.6 ms after its pass began (the no-op thread's start and join
  don't stall the UI thread); the walk starts 2–3 ms after that frame; no frame waits for it. Later pool burst: 0
  walks; the UI thread calls `SwitchToThread` at most 361–1,198 times a frame, mean 149–394 (a frame that waits out a
  walk calls it ~300k times). Then 8 s idle: 0 frames, passes, wakes.
- **Observation, not a framework miss:** after a busy start (720 unpaced frames, then rest) NVIDIA's Auto threaded
  optimization stayed single-threaded in 5 of 12 AOT launches (JIT stayed threaded 7 of 7): no walk at all and 0
  `SwitchToThread` a frame even in the later burst. The framework drew its trigger frame in every launch; the driver
  chose not to thread.
- **f9bceb6d (maximized custom frame inset at the window's DPI):** no `GetSystemMetrics` call left in
  `src/HultUI.Platform.SDL3` except the fallback when `GetDpiForWindow` returns 0. Attached today: only the 2560×1440
  at 96 DPI (primary; system DPI 96), as in round 6's build, where old and new insets are equal — so maximize on both
  monitors, the DPI hop and Snap Layouts on a lower-DPI monitor still cannot be checked (Needs the user).
- **f76eb195 (overlay layout on resize):** one loop over open overlays per resize, nothing per frame.
- **Idle loop and frame pacing:** in round 6's source diff the only loop change is `IdleDeadline.DriverThreadWalk`;
  no other deadline or wake, no new SaveLayer, flush, submit or GRContext call.

## CANDIDATES

None.

## WAITING, RE-RANKED

- **vk-startup-preload: not worth it; move to Examined.** NVIDIA's Vulkan driver (`nvoglv64`, then `nvgpucomp64`
  ~68 ms later) loads inside `SDL_Vulkan_LoadLibrary`, the first step of `SDL_CreateWindow` for a Vulkan window (90 ms
  in all), and stays loaded. A worker that loads `vulkan-1.dll` and enumerates instance extensions before `SDL_Init`
  cuts SDL's own load from 73–82 ms to 2.5–3.4 ms, but `SDL_Init(VIDEO)` grows from 15–17 ms to 44–55 ms (both threads
  load DLLs under the loader lock), so only the text warm overlaps the driver load. AOT, 5 alternated pairs
  (`SDL_Init` + text warm + SDL's load): min 92.8 → 87.6 ms (−5.2), median 95.0 → 89.3; the text warm itself is
  4.3–5.3 ms on AOT. JIT: −15.3 ms with the text warm (18 ms on JIT); without the warm 89.9 → 89.6. Under the 10 ms
  start-up bar on AOT. Aperture does nothing before its `Application` constructor (`Program.cs:45`), so no app work to
  add to the overlap.
- **gl-threaded-optimization-profile: still information only.** Since b788b2ef the walk lands while the window rests,
  so the driver setting would only remove the per-frame spin (mean 149–394, max ≤ 1,198 `SwitchToThread` per paced
  frame). Auto mode threaded in every app-like launch (13 of 13), so the apps pay that spin.

## EXAMINED, NOT WORTH IT

- **Two same-size redraws right after the first frame** (GL and Vulkan, JIT and AOT, every launch): Windows sends a
  WM_PAINT after the show (EXPOSED, data1 0); SDL's event watcher draws it "as a resize": `OnWindowResized` runs,
  `Resized` is raised, root and overlays laid out again, though size, density and scale are unchanged (640×400, 1.0,
  1.0 before the show and at SHOWN). That is the second frame, 30–40 ms after the first. A third follows 4–9 ms later
  (cause not traced; in these runs it follows the pointer's MOUSE_ENTER). Both come after the first present, so the
  start-up bar never sees them; the apps' `Resized` handlers rebuild only on a scale or breakpoint change, so each
  costs one relayout and paint per window, once.
- **GL apps calling `BeginGraphicsPreload`:** the GL preload starts SDL first, so it overlaps only the text warm and
  the app's own pre-window work; measured 2026-09-02 at ~10 ms median on Junction (JIT), prototype reverted. No new
  evidence.
- **`opengl32.dll` loads in a Vulkan-only window** during device setup (between choosing the present mode and building
  the swapchain). Vulkan UI still shows 0 walks and 0 `SwitchToThread`; a one-off DLL load.

## NEEDS THE USER

- **f9bceb6d window checks** (correctness, not perf): maximize on both monitors, the DPI hop, and the Snap Layouts
  flyout on the lower-DPI monitor. They need the 4K monitor at 150 % attached and primary at sign-in (system DPI 144);
  today only the 1440p at 100 % is attached. Then: UiTests `WindowPlacementTests` and the Borderless
  `WindowStateActivationTests` (`HULTUI_UITESTS=1`), plus a Snap Layouts hover the user does by hand.

## FILES

Scratchpad `r7-gl-vulkan\` of session 855f4ddd: probe host `host\Program.cs`, `host\ImportCounter.cs`,
`host\R7GlHost.csproj` (AOT with `-p:R7Aot=1`); runner `run.ps1` (`-Exe` picks the AOT build); monitor and DPI list
`monitors.ps1`; stdout and stderr of all 69 launches `runs\` (steady-*, walkrun-*, aot-*, vkload-*, loads-vk8,
startup-*); AOT build `aot\R7GlHost.exe`.
