SATISFIED: no

I worked at e6f7b5aa (detached, SDK 11.0.100). The probe host is R14Host, built from round 13 step 8's B8Host and published NativeAOT from my worktree. It runs Junction on GL, Reel on Vulkan, and Reel on GL. New probes:
- a callback on SDL's `SDL_TIMER_RESOLUTION` hint (each change is one timeBeginPeriod or timeEndPeriod call);
- the loop's idle waits counted by deadline kind;
- WH_GETMESSAGE and WH_CALLWNDPROC hooks on the UI thread;
- a stack sampler;
- a windowless console probe of the Vulkan loader.

The worktree is clean, the build servers are shut down, and no R14Host is running.

REVIEW: none.
- **cd7e3db2 + d797205f (timer resolution): no objection.** Counts are hint changes from the callback.
  - **Junction GL:**
    - Start: "1" 14 ms after the first frame, "0" at 1.64 s.
    - Rests (20 s over 3 launches): 0 changes. The guard thread's loops read 63.6–64.2 a second, which is 15.6 ms resolution.
    - A 1.5 s tween: one pair; it gave 1 ms back 1.0 s after its last frame.
    - 10 single repaints and 4 repaint pairs: 0 changes, 0 paced waits.
    - 60 resize steps on the window's own thread: 0 changes, 0 paced waits (each frame is drawn inside the size message).
  - **Nothing paces without asking.** Paced waits happened only in the tween and playback phases. In the modal size loop, SDL's live-resize timer is a user32 timer, about 64 a second either way (Application.cs:536).
  - **Reel GL with a clip played and then paused:** 63.7 loops a second. mpv's per-wait NtSetTimerResolution stops while paused, so nothing keeps 1 ms.
  - **During playback the request comes and goes:** 5 changes in 3.5 s on Vulkan, 3 on GL, 2 in 2.5 s on a reopen. Frames are woken by mpv; a paced wait happens only when a position update and a frame land within one interval. That is about 1.4 calls a second (~30–60 µs CPU), below every bar.
  - **Owned deadlines at rest:** the only one seen at rest was the purge's, which fired "unused for 1001–1002 ms".
- **4fdb56cb (SurfacePurge deadline): no objection.**
  - Reel Vulkan, 60 own-thread resize steps (61 swapchains, each step a chained 8-bit rebuild outside a modal loop), in 2 runs: exactly 1 SurfacePurge idle wait and one purge (4.3 → 4.1 MB in Reel's empty library), then 0 passes in the next 6 s.
  - GL windows never get the deadline (0 in 6 GL runs).
  - Modal size loop (code read): `PurgeRestedSurfaces` skips while `InSizeMoveLoop`, and the frame loop is not running then. The purge is owed again after the frame drawn when the drag ends (SDL3Window.cs:958–973, SizeRestPurge.DueAt), so one wake per drag.
- **276789d8 (mpv waits with no timeout): no objection.**
  - Reopening a clip disposes the paused player on the UI thread (ReelApp.cs:151), and the next clip played 74–77 frames in 2.5 s.
  - WM_CLOSE while playing ended Run in 0.27 s, exit 0.
  - Threads: 25 before any clip → 37 with the first paused → 35 with the second playing, so none are left behind.
  - mpv-events stayed below 0.2 wakes a second in all 6 paused rests.
  - MPV_EVENT_SHUTDOWN is not handled; the loop then blocks until Dispose's mpv_wakeup. That is safe and no regression.

CANDIDATES:
- id: media-interop-gl-preload
  what: Load the GL driver that the video interop's hidden context needs on a worker, before the first video frame needs it. Today it loads inside that frame, on the UI thread.
  evidence:
  - **Where it happens:**
    - The first draw of a GPU source calls TryEnableInterop → EnableInterop → CreateHiddenGLContext (SdlVulkanHost.Interop.cs:147–236).
    - That runs `SDL_CreateWindow(OPENGL|HIDDEN)`, which loads opengl32 and runs SDL's WGL probe, plus the context.
    - `BeginPreload` returns early for Vulkan (SDL3Window.cs:2047).
  - **Prototype** (R14_GLPRELOAD=1: `SDL_GL_LoadLibrary` on a worker 1.5 s before Open, 66–72 ms there). Reel on Vulkan, NativeAOT, 4K at 150 %, 120 Hz, 4 launches alternated without/with/with/without:

    | Measure | Without | With |
    |---|---|---|
    | "Open returned" → "vulkan interop ready" (StartupTrace marks) | 98.4 / 93.1 ms | 35.5 / 44.9 ms |
    | Open → first window frame | +201 / +185 ms | +134 / +146 ms |
    | Open → mpv time-pos > 0 | 246 / 229 ms | 180 / 189 ms |

  - Later frames are unchanged: the longest gap is 34–38 ms (a 30 fps clip). Open itself takes 4 ms.
  real app:
  - **Reel opened with a film**, every double-click: Open runs before the first frame (ReelProgram.cs:105). The window shows nothing new for about 0.19 s after Open.
  - **Aperture's first playback** of a session.
  - Once per process, about 50–55 ms each time.
  expected: Start-up bar (first presented video frame, ≥10 ms): about 50 ms, 5× the bar. Timed here on a shared desktop, so the build phase should re-time it.
  cost: small–medium. No API or pixel change.
  - Start it where it cannot hold up the first UI frame. Plan 15 §3.1: a GL preload joined before the Vulkan window cost that frame about 95 ms.
  - Reel with a file: start it after the Vulkan device is made. Aperture: start it at the first rest, as the GL driver thread walk is. Only EnableInterop waits for it.
  - Loading off the main thread is HULTUI_GL_PRELOAD's opt-in (see NEEDS THE USER). The build should measure both Reel's first UI frame and its first video frame.

REAL WINDOW:
- **Launches and safety:**
  - 15 launches in 4 holds: 79, 135, 135 and 66 s held, after waits of 17, 111, 170 and 717 s (holds.log).
  - Foreground taken 0 times. The driver set WS_EX_NOACTIVATE 6–10 times a launch.
  - 0 nvlddmkm events, no stall over 1 s, no device lost. The screen was never read.
  - h1 had a quoting bug: all three of its launches ran Reel (h1-1 and h1-2 on Vulkan, h1-3 on GL), with no video.
- **Monitors:** every window on the 4K at 150 %, 120 Hz (primary). The 1440p was attached at 100 %, to its left; its refresh was not read.
- **Timer-request counts and the purge wake** (REVIEW above) exist only in a real window.
- **Resize, own-thread SetWindowPos 16 ms apart:**
  - Vulkan rebuilds the swapchain at every step outside the modal loop (61 swapchains for 60 steps).
  - GL: 60 frames drawn in the size message, 0 loop frames, and one A11yLayout idle wait per step (60). Vulkan had 18–60.
  - Each step is 7 sent messages and 1 posted.
- **A pointer crossing Reel GL at rest** (h2-3 rest A): 678 passes in 10 s, 0 frames, 30.8 KB UI (45 B a pass). 0da9f80e holds in a real window.
- **UI-thread messages at rest:** 0 posted and 0 sent in all 10 rests. Playback costs one `_SDL_WAKEUP` a frame (116 in 3.5 s). Each open gets 2 WM_GETOBJECT (a UIA client asked).
- **Lead, cause not isolated: the UI thread wakes at rest with no loop pass and no message** (both hooks read 0):
  - Junction: 1.3–4.9 a second (3 launches).
  - Reel after a clip played and paused: 7.1–10.8 a second in the 4 rests that followed a resize run and driver-thread mpv property reads (h2, h3).
  - The same pause right after a fresh start: 0 (2 of 2, h4).
  - Reel before any clip: under 0.2 a second.
  - Next step: stack-sample the UI thread with h3's sequence.
- **Other threads at rest:** NVIDIA's CUDA thread (10.2–11.0 a second) appears with the first clip. Vulkan rest is 98–99 a second on NVIDIA's thread, and the guard reads 167–181 a second (the driver holds 1 ms).
- None of this shows headlessly.

WAITING, RE-RANKED:
- **gl-threaded-optimization-profile:** information only, unchanged. GL rest shows one or two NVIDIA GL threads at 10–15 a second, the driver's own.
- **vk-format-switch-chained: keep, low.**
  - It happens once at the end of each HDR film. Destroy-first costs 325 ms, of which 311 ms is the 8-bit create.
  - Nobody has measured whether chaining shortens the create. Do an env-gated A/B of `DestroyBeforeReplacing` keyed on the new format before building.
  - resize-stretch-fix concerns chained F16 MAILBOX first presents; the switch-off presents 8-bit, which never stalled chained.
  - No HDR clip was run this round.
- **f16-held-drag-post-stall: low, on paper only.** F16 is used only during HDR playback. Not run, no approval asked.

EXAMINED, NOT WORTH IT:
- **vk-instance-extensions-once:**
  - `HasInstanceExtension` (SdlVulkanHost.cs:532) enumerates on every call. Init asks 4 times, so 8 `vkEnumerateInstanceExtensionProperties` calls, each a full rescan of the loader's layer and driver manifests. SDL makes 2 more.
  - Probe, 5 runs: the 4 pairs take 9.5–9.8 ms, so one shared list saves 7.1–7.4 ms (skipping enumeration entirely saves about 9.5). Below the 10 ms bar.
  - Fix it when Init is next touched.
  - The device-extension queries (7 × 2 calls) total 0.2–0.4 ms.
- **vk-startup-preload extended to vkCreateInstance:**
  - The first enumeration (driver load) takes 84–85 ms; vkCreateInstance takes 49–53 ms (OBS's graphics-hook64 and NVIDIA's nvspcap64 load there).
  - The main-thread work it could overlap is SDL_Init (15–38 ms) plus the window's own creation (~5 ms).
  - Round 7 measured the driver-load half at −5.2 ms because both threads load DLLs under the loader lock. With the instance added, the ceiling is about 20 ms. Not re-proposed without a timed prototype.
- **Holding 1 ms through playback:** it would save only the ~1.4 calls a second above. mpv asks for 1 ms per short wait while playing anyway.
- **The SurfacePurge wake:** 1 per run of size changes, 0 afterwards. Nothing to cut.

NEEDS THE USER:
- **media-interop-gl-preload loads the GL driver off the main thread**, which is outside SDL's documented threading contract (validated on Windows/WGL). Today that is HULTUI_GL_PRELOAD's opt-in, which Reel takes on Windows. Choose one:
  - (a) the framework does it by default on Windows for Vulkan windows with interop;
  - (b) it stays opt-in, and Aperture opts in as Reel does (one line of app code).
  - I recommend (b).

FILES (all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\41943a9b-f657-4483-971a-a2b346b967f7\scratchpad\r14-gl-vulkan\`):
- **Host:** `host\R14Host.csproj`, `src\Program.cs`. The modes are Junction, Reel with R14_VIDEO, R14_FIRSTVIDEO with R14_GLPRELOAD, and R14_PAUSESTACKS.
- **Build and launch:** `aot\` (the exe), `publish.ps1`, `batch.ps1` (lock and guards), `Driver.cs`, `Stacks.cs`.
- **Analysis:** `perthread.py`, `threads.py`.
- **Logs:** `holds.log`; `runs\h1–h4.jsonl`; `runs\*.out.txt`, `*.startup.txt`, `*.framelog.txt`, `*.stacks.txt`.
- **Loader probe:** `vkenum\` (`Program.cs`, `run1.txt`, `run2.txt`, `run-once.txt`, `runs5.txt`).
- **Fixture:** `jrepo\` (a 300-commit clone of the repo, used by Junction).
