SATISFIED: yes

I measured at f9c44628, detached, on SDK 11.0.100. Everything is NativeAOT, with a11y on as users run it. I report counts, bytes and shares only.

**Monitor:** the 1440p only: 2560×1440 at 100 % (96 dpi), 200 Hz, HDR on, SDR white 240 nits.

**Real windows (all guards held):**
- **Launches:** 16, in 4 holds of the desktop lock:
  - h1: 172 s held, after a 273 s wait;
  - h2: 192 s held, after 307 s;
  - h3: 169 s held, after 797 s;
  - h4: 109 s held, after 24 s.
- **Guards:** every window had the default frame, the in-process no-activate hints, the CBT hook (refused 0) and the driver's WS_EX_NOACTIVATE (set 6–8 times a launch).
- **Foreground:** taken 0 times; the 5 ms guard never fired.
- **GPU and screen:** no `nvlddmkm` events, no stall over 1 s, no device lost. The screen was never read.
- **Worktree:** clean.

**Harness:** one AOT exe (`R12AppHost`) holds all five apps, built as each app's Main builds it. Aperture shows its sign-in page and Reel its empty player, as in r11u.
- **Frames:** the window's own `SkiaRenderContext` frame count, read by UnsafeAccessor. Each drawn frame is one BeginFrame, one flush and one present (Window.cs:2004–2022).
- **Loop counters:** HultUI's internal FrameLog statics (passes, waits by deadline, wakes by source, posts), read per phase through `UnsafeAccessorType`.
- **Timeline:** `HULTUI_STARTUP` (it also marks every Vulkan swapchain build).
- **Resize:** a WM_APP message to the window's own procedure runs the drag inside the loop's own wait, as a real drag is.

REVIEW: none.
- Round 11's 50 commits touch no SDL3, GL, Vulkan, present or media source. The src diff is 14 files, all in Core builders/elements/layout, Syntax and Text.
- On f9c44628, which contains them, all five apps rest at 0 frames, 0 passes, 0 waits and 0 wakes, and every drag step draws 1 frame.

CANDIDATES: none.

REAL WINDOW:
- **Idle (all 16 launches, GL and Vulkan, 8 s plus 2 s): 0 frames, 0 loop passes, 0 waits, 0 posts, 0 wakes.** Only owned deadlines appear:
  - the GL driver-walk, 1–3 passes once after start;
  - a11y-layout, 1–3 passes after a resize.
  - The probe host showed the same 0.
- **Loads:**
  - **Junction start:** 10 frames (8, then 2, then 0 a second), 23–24 passes and 7–8 posts in 6 s.
  - **A file written into Junction's repo by another program:** 5 frames, 14 passes, 7 posts, 4.7 MB on all threads, 1 gen-0 GC.
  - **Vesper, Meridian, Aperture GL:** 1 frame each.
  - **Reel, Aperture Vulkan:** 0 frames.
  - **Vesper opened on C:\Windows\WinSxS:** 11 frames in its first second.
  - No spinner ran in any load.
  - **Vesper does not watch folders:** an external new file gave 0 passes in 5 s. That is app behaviour, not perf.
- **Drag (frames per SetWindowPos step, SWP_NOACTIVATE): 1 frame on every step.**
  - GL: 12 drags, 384 steps.
  - Vulkan F16: 11 drags, 352 steps.
  - Vulkan 8-bit: 4 drags, 128 steps.
  - Emptying the queue after each step (a resting pointer) changes nothing: WM_PAINT never arrives inside the size loop.
  - Vulkan adds 1 frame at loop end (the exact-size rebuild).
  - GL held edge: 0 frames in 1 s (12 holds).
- **One-shot resizes:**
  - **From another thread:** a grow draws 2 frames (WM_WINDOWPOSCHANGED, then WM_PAINT → EXPOSED(0)) and a shrink 1. That held for 124 steps across all five apps, GL and Vulkan.
  - **From the window's own thread** (an app's maximize path): 1 frame (12 of 12).
  - So it is round 5's measurement trap, real only when another process resizes the window.
- **Swapchain and context builds:**
  - **GL:** 1 context a window, and a new GRBackendRenderTarget and SKSurface for each size (32 a drag).
  - **Vulkan F16 drag:** Aperture 4 bucket builds (up to 2048×1280) plus 1 exact; Reel 2 plus 1.
  - **Vulkan 8-bit drag:** the same bucket pattern.
  - **One-shot steps:** 1 rebuild each, exact.
  - **F16 totals:** 64 one-shot rebuilds and 11 drags ran clean.
- **Driver spin, measured as `SwitchToThread` calls on the UI thread per frame (the probe host only ever measured steady frames: 230–311):**

  | Frame | GL | Vulkan F16 | Vulkan 8-bit |
  |---|---|---|---|
  | Drag step | 7.0k–21.8k | 115–413 | 0 |
  | One-shot step | 6.2k–15.1k | 650–1,870 | 0 |
  | Load | 326–1,058 (WinSxS list 12.7k) | — | — |

  GL resize frames spin 25–70× a steady frame.
- **GPU memory per process at rest (DXGI local / non-local, in-process):**

  | App | Renderer | Local | Non-local |
  |---|---|---|---|
  | Junction | GL | 26.2 MB | 14–16 MB |
  | Vesper | GL | 22.1 MB | 11.6 MB |
  | Meridian | GL | 25.9 MB | 11.6 MB |
  | Aperture | GL | 25.9 MB | 11.6 MB |
  | Aperture | Vulkan F16 | 131.4 MB | 14.2 MB |
  | Reel | Vulkan F16 | 93.9 MB | 14.4 MB |
  | Reel | Vulkan 8-bit | 30.1 MB | 17.1 MB |

  - Skia's cache holds 4.2–14.2 MB (limit 256 MB).
  - nvidia-smi gives N/A per process under WDDM; the whole GPU moved ≤ ±30 MiB a launch.
- **The first F16 swapchain replacement in a window keeps memory.** One maximize, snap or drag does it.
  - It keeps +26.1–26.5 MB DXGI non-local, +4.9 MB local and about +50 MB private 15–30 s later. Threads fall 43 → 36.
  - **Aperture:** private 137.8 → 186.4 MB (+35 %), DXGI 145.6 → 177.7 MB (+22 %).
  - **Reel:** private 134.5 → 184.1 MB (+37 %), DXGI 108.3 → 142.1 MB (+31 %).
  - It plateaus: snaps first or drags first give the same, and later resizes add ≤ 2 MB.
  - Vulkan 8-bit returns everything (DXGI flat, private back to rest); GL keeps +9 MB non-local after 3 drags, then flat.
  - Round 11's probe logs show it too (`drag-vk16`: non-local 16.8 → 43.0 MB, private 146 → 198 MB). Nobody reported it.
- **UI bytes per drag frame:**
  - Junction: median 2,152 B (the first step 106–124 KB).
  - Meridian: 2,360 B.
  - Aperture: GL 1,944 B, F16 2,000 B.
  - Reel: F16 2,000 B, 8-bit median 0 B.
  - **Vesper: 15,032 B median (mean 45 KB), and 91 KB in WinSxS.** Lead for experts 1/8: layout at each width.
- **Start-up shares to the first presented frame:**
  - **GL apps:** before the first mark 13–32 %; SDL_CreateWindow (the GL driver load) 43–66 %; GL context, Skia surface and ready 2–9 %; first frame 17–40 %.
  - **Aperture (Vulkan):** before the first mark 10 %, SDL_CreateWindow 20 %, Vulkan instance and device 23 %, swapchain 3 %, first frame 40 %.
  - **Reel (Vulkan):** before the first mark 5–12 %, SDL_Init 5 %, SDL_CreateWindow 21–23 %, Vulkan setup 26–28 %, swapchain 3 %, first frame 30–32 %.
  - That matches the known GL-driver lead. The GL preload for GL apps is already examined (~10 ms, round 7).
- **What the probe host did not show:**
  - resize frames' driver spin;
  - real loads (posts, frames, 4.7 MB a repo change);
  - Vesper's 15–91 KB drag frames and Junction's 106–124 KB first drag frame;
  - the a11y-layout deadline after a resize;
  - the cross-thread WM_PAINT frame (known trap) next to the same-thread single frame.

WAITING, RE-RANKED:
- **F16 kept drag targets: stays deferred; the drag probe was not run.**
  - In the real apps the per-size wrap is the whole F16 drag frame: Aperture and Reel 2,000 B a frame against 0 B median on 8-bit Vulkan. That is about 64 KB a 32-step drag, on drags only, so it clears no bar.
- **gl-threaded-optimization-profile: still information only (the user's driver setting).**
  - Real apps make it 25–70× larger on resize: GL drag frames 7.0k–21.8k `SwitchToThread`, one-shot steps 6.2k–15.1k, a dense Vesper list frame 12.7k.
  - Vulkan F16: 115–1,870; Vulkan 8-bit: 0.
  - Still no framework lever.
- **f16-held-drag-post-stall: not run; I do not ask for a run now.**
  - Plain F16 drags and one-shot rebuilds ran clean on the stall's monitor: 11 drags and 64 rebuilds in Aperture and Reel.
  - The stall needs a post waiting at every step. Real Aperture and Reel at rest post 0 times, so a resize at rest cannot meet it. Playback might, which nothing here exercised.
- **Open items (8a38ef4, 4315c4b on AMD, Intel and Linux):** no new evidence; NVIDIA only here.

EXAMINED, NOT WORTH IT:
- **The first F16 replacement's retained memory (the numbers above):**
  - It is driver-side. The framework makes and frees the same objects on 8-bit, which returns everything.
  - Round 4 found that a rebuild made earlier only moves the cost into start-up.
  - A chained replacement stalls the first present about 100 ms per rebuild (VulkanSwapchainChoice.cs:172–185).
  - F16 against 8-bit memory was decided in round 3.
- **The cross-thread one-shot grow's second frame:** round 5's trap. It costs 1 frame per snap made by another process; the app's own thread draws 1. Too small to clear a bar.
- **Spinners at the display rate (200 frames a second here):** no real-app launch showed one, so there is no number to rank. The behaviour is by design.
- **GL drag frame bytes (1.9–2.4 KB, the per-size SKSurface):** drag only.

NEEDS THE USER: none.

FILES (all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\0bab04ae-e7a9-44cb-b4da-96d849a107f6\scratchpad\r12-gl-vulkan\`):
- **Host:** `host\R12AppHost.csproj` (global.json beside it); `src\Program.cs` (the app factory, driver, phases, drag and snap, MsgLog, the FrameLog accessors, the guards, DXGI memory); `src\ImportCounter.cs` (round 11's).
- **AOT exe:** `aot\R12AppHost.exe`.
- **Scripts:** `publish.ps1`, `batch.ps1` (the lock, the external guard, per-spec environment), `sum.py` (summaries), `monitors.ps1`.
- **Logs:** `holds.log`, `batch-h1..h4.log`, `runs\*.out.txt`, `runs\*.startup.txt`, `runs\launches.jsonl`.
- **Other:** `fx\jrepo` (my own fixture copy), `src-stat.txt` (the review diff).
