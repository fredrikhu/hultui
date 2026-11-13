SATISFIED: no

I worked at 2ced4e26, detached, on SDK 11.0.100, with NativeAOT throughout. The probe host is round 12 step 5's B5Host, rebuilt from my worktree as R13Host. Its driver samples each thread's context switches and also its stack: the thread is suspended for microseconds and the copy is walked with dbghelp. Numbers are counts and MB only. The worktree is clean (one probe edit was made and reverse-edited), and build servers are shut down.

REVIEW:
- **2d18a361: no objection to the code.**
  - `_pictureRange` is a volatile written on mpv's event thread. The `RequestRepaint` there goes through `MediaPlayer.OnRepaintRequested`, which hops via the thread pool plus `LiveFrameRequests` (MediaPlayer.cs:239–246). So the event thread never posts to, or waits on, the UI thread.
  - A format change rebuilds once, at the next acquire. GL is untouched.
  - In all 14 launches the loop did 0 frames, 0 passes, 0 waits and 0 wakes at rest.
- **One limit, measured: the rest win lasts only until the first HDR video in a process.** Reel played the PQ clip for 4 s, then the same clip as SDR, paused, and rested on 8-bit. Against an SDR-only session in the same state:
  - NVIDIA's DXGI threads stay: nvwgf2umx at 95.7–99.8/s plus 25.1–25.6/s.
  - Wakes (my harness's threads excluded) go from 126 to 256–264 a second.
  - DXGI local goes from 284.2 to 330.8–331.5 MB, non-local from 107.6 to 139.1 MB, private from 516 to 569–611 MB, threads from 37 to 50.
  - Against round 12 base's always-F16 run at the same point (r12b5 c1-6): local −101 MB, non-local +35, private +17…+59, wakes about the same. So it is not a regression against round 11.
  - Replacing the VkSurfaceKHR at the switch changed nothing (see EXAMINED). The driver keeps this per device.
  - **For HDR.md:** after an HDR video the process keeps NVIDIA's DXGI path for good.
- **Other round 12 commits:** nothing in my area moved. Every launch rested at 0 frames, passes and loop wakes.

CANDIDATES:
- id: gl-sdl-timer-resolution-at-rest
  what: SDL3's default `SDL_TIMER_RESOLUTION` ("1") calls `timeBeginPeriod(1)` at SDL_Init and holds it for the process's whole life. Set the hint to "0" before SDL_Init, and hold 1 ms only while the loop paces frames.
  evidence:
  - **Source:** SDL3.dll (ppy.SDL3-CS 2026.320) imports timeBeginPeriod and timeEndPeriod and reads `SDL_TIMER_RESOLUTION`. HultUI sets no such hint (no match in src).
  - **Probe:** my host's own guard thread, which sleeps 5 ms in a loop, shows the process's timer resolution:
    - GL Reel at rest: 174.8 wakes/s with SDL's default, 68.6/s with the hint at 0.
    - GL Aperture with the hint at 0: 66.5/s.
    - So 1 ms comes only from SDL.
  - **Wakes of the app's own threads don't change.** The UI thread is at 0 either way. Aperture GL has one NVIDIA thread at 10.1/s. Reel GL's two such threads go 10.3 + 10.1 → 11.2 + 10.3.
  - **NVIDIA Vulkan: no gain.** With the hint at 0 the guard still ran at 169.5/s. nvoglv64.dll imports timeBeginPeriod, and the Vulkan driver asks for 1 ms itself; the GL driver does not.
  - **Why the hint can't simply be 0:** the loop waits out the rest of a display interval in whole milliseconds (Application.cs:851–855 through SdlWindowingPlatform.cs:148–165). At 15.6 ms granularity, frames would cap near 64 a second.
  - **The machine-wide figure can't be shown here.** NtQueryTimerResolution read 1 ms before, during and after every launch, because another process holds it (Steam and steamwebhelper load SDL3 too).
  real app: Junction, Vesper and Meridian (GL) sitting open at rest, which is most of their life; also Aperture and Reel on GL or non-NVIDIA Vulkan. On a machine where nothing else asks (a laptop with no video playing), the HultUI app alone keeps Windows' clock interrupt at 1,000 a second instead of 64.
  expected: Idle bar. Machine-wide clock interrupts at rest fall from 1,000 to 64 a second (−94 %) where no other process asks for 1 ms. In-process wakes are unchanged; NVIDIA Vulkan is unchanged.
  cost: small, Windows only.
  - Set the hint next to the IME hint (SDL3Window.cs:2075).
  - `SDL_SetHint` to "1" or "0" makes SDL call timeBeginPeriod or timeEndPeriod. Set "1" when the loop enters a paced wait or a tween runs; set "0" after about 1 s with no paced frame.
  - No API or pixel change. Owned deadlines at rest (caret, tooltip delay, a11y debounce) may fire up to 15.6 ms late.
- id: media-mpv-event-wait-forever
  what: MpvBackend's event thread waits with `mpv_wait_event(_mpv, 0.5)` (MpvBackend.cs:253), so an open player wakes twice a second at rest. Every flag that loop reads is already followed by `mpv_wakeup` (Dispose :856–859, the hwdec trace :553–557), so the wait can be −1.
  evidence: Reel with an SDR clip paused, at rest, 5 launches: mpv-events at 2.0–2.2/s. It is the only thread of ours that wakes at rest (UI thread 0, loop 0); the rest are NVIDIA's.
  real app: Reel paused, Aperture paused: any time a player is open and still.
  expected: Idle bar: −2 wakes a second per open player, toward the target of about 1 a second for what is ours.
  cost: small, no API. A test should check that Dispose still returns at once.

REAL WINDOW:
- **Launches and safety:**
  - 14 launches in 3 holds: h1 141 s after a 31 s wait, h2 158 s after 297 s, h3 101 s after 791 s.
  - Foreground taken 0 times; the driver set WS_EX_NOACTIVATE 4–10 times a launch.
  - 0 nvlddmkm events, no stall over 1 s, no device lost. The screen was never read.
- **Monitor:** the 4K at 150 %, 120 Hz, HDR on, SDR white 240 nits (every launch's INFO line).
- **The lead: the ~112 wakes a second at rest on 8-bit Vulkan are all NVIDIA's.**
  - Totals: Aperture 111.9/s, Reel 112–114/s.
  - They are four Vulkan driver threads, each in a timed WaitForSingleObjectEx inside nvoglv64 (through +A3BA4A):
    - thread function +1075D16: 96.8–99.6/s;
    - +F62DA4: 10.0–10.6/s;
    - +F430ED: 3.9–4.3/s;
    - +F456C2: 1.0–1.5/s.
  - Our threads: 0 (UI thread and loop).
  - The 97/s thread does not depend on:
    - the present mode: MAILBOX 96.8–98.2, IMMEDIATE 97.3, FIFO 98.0;
    - OBS's layer: 98.2 with `DISABLE_VULKAN_OBS_CAPTURE=1`;
    - SDL's hint: 95.9 with 0, because the driver holds 1 ms itself.
  - A GL app at rest: Aperture GL has one NVIDIA GL thread (+7BF21A) at 10.1/s; Reel GL has two.
  - Nothing here is ours to remove beyond the two candidates.
- **FIFO 8-bit also goes through NVIDIA's DXGI path.** Against MAILBOX: nvwgf2umx threads at 97.2 + 24.9/s, DXGI local 54.0 → 91.3 MB, non-local 21.7 → 39.5, private about 106 → 175, threads about 24 → 34. IMMEDIATE equals MAILBOX. MAILBOX stays the right default.
- **A paused player adds** NVIDIA's CUDA thread (10.2–11.1/s) and mpv-events (2/s).
- None of this shows headlessly: no driver threads and no timer resolution there.

WAITING, RE-RANKED:
- **vk-format-switch-chained: keep, low rank.** The switch off happens once per HDR → SDR transition. In round 12 the destroy-first 8-bit create took 311 ms. The chained switch on cost 2.7 ms plus a first present of 96–154 ms, so a chained switch off would likely save about 200 ms, once. The DXGI threads stay either way. A small step if the round has room.
- **size-change-restore-second-frame: attributed; close.** Step 1's restore was a SetWindowPos grow from the probe's script thread (r12b1 host Script.cs:191–197): WM_PAINT, then EXPOSED, then a second frame. That is round 5's trap; round 12 counted 124 of 124 cross-thread grows drawing 2 frames against 12 of 12 own-thread grows drawing 1. A user's drag, maximize, snap or Win+arrow runs on the window's own thread and draws 1 frame. Only another program's SetWindowPos pays the extra frame (9 KB, 0.3 ms render) per grow, which clears no bar.
- **F16 kept drag targets: close.** F16 is now used only while an HDR video is on screen. An F16 drag frame wraps its target for about 2.0–2.1 KB, about 64 KB a 32-step drag, and only during HDR playback. It stays off (SwapchainTargets.cs:70–73).
- **f16-held-drag-post-stall: not run, no approval asked; low.** The only F16 drag left is during HDR playback, where mpv posts every frame, which is the stall's own precondition. A run would need an HDR clip playing, on the 1440p at 200 Hz, with the user's approval.
- **gl-threaded-optimization-profile:** information only, unchanged.
- **Open (8a38ef4, 4315c4b on AMD, Intel, Linux):** no new evidence; NVIDIA only here.

EXAMINED, NOT WORTH IT:
- **Replacing the VkSurfaceKHR at the switch to 8-bit,** to make NVIDIA drop its DXGI path. Env-gated prototype, 2 launches against 1 control: identical threads (nvwgf2umx 97.7–97.9 + 25.2–25.6/s) and memory (local 331.5 MB, non-local 139.1 MB). Only a device rebuild is left (the Skia context, every GPU resource, the mpv interop): large, and it may not work.
- **NVIDIA's 97/s Vulkan thread:** not ours (REAL WINDOW).
- **FIFO for 8-bit:** worse on wakes and memory (REAL WINDOW).

NEEDS THE USER:
- **The timer-resolution candidate's machine-wide count.** Here Steam holds 1 ms, so the effect can't be counted. The build step needs a few minutes with Steam (and any browser video) closed, or the user running `powercfg /energy` as administrator. May the build phase ask for that?
- **What an HDR session leaves behind:** try a Vulkan device rebuild after an HDR session (large, unproven), or accept it and document it in HDR.md? I recommend documenting it.

FILES (all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\17c11f5e-9de0-4477-8f58-0d0a5e006605\scratchpad\r13-gl-vulkan\`):
- **Host:** `host\R13Host.csproj` (global.json beside it); `src\Program.cs` (modes rest, sdrrest, hdrsdrrest, the STACKS pause, TimerRes); exe in `aot\` (the last build includes the probe, now reverted in the worktree).
- **Driver and sampler:** `Driver.cs` (R13Driver: REST0/REST1 per-thread switches); `Stacks.cs` (stack and wait-state sampler, R13TimerResNow); `TimerRes.cs`.
- **Scripts:** `batch.ps1` (lock, guards, specs), `publish.ps1`, `stacktest.ps1`, `perthread.py`, `mem.py`.
- **Logs:** `holds.log`; `runs\h1.jsonl`, `h2.jsonl`, `h3.jsonl`; `runs\*.out.txt`, `*.stacks.txt`, `*.startup.txt`.
- **Other:** `2d18a361-src.diff`, `bands.y4m` (the clip). Note: h1's labels carry stray quotes; h1-3 and h1-4 are Reel on GL, not Aperture, and h1-1 used the old 20 s rest mode with no stacks.
