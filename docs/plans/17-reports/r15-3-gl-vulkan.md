SATISFIED: no

I worked at 5a212f3e (detached, SDK 11.0.100). The probe host is R15Host, round 14's R14Host with new phases, published NativeAOT from my worktree. The batch driver now has an out-of-process catcher (Catch.cs), and the host has VirtualQuery region dumps.

The catcher watches the UI thread from outside:
- polls its cycle time every 0.5 ms;
- reads the session's last-input tick;
- raises WinEvents from a hidden window in another process;
- samples the thread's stack by suspending it, back to back.

The env-gated swapchain probe in SdlVulkanHost.cs is reverted by reverse edit. The worktree is clean, the build servers are shut down, and nothing is left running.

REVIEW: none.
- **b62e22cf, 545d5d89, 64c127a2, a3ebdfca:** none of them changes GPU work, present or wakes.
  - Round 14 touched nothing in Platform.SDL3, Rendering.Skia, Media*, apps/Reel or apps/Aperture (`git diff 06d4db59 5b2985be` is empty there).
  - Across 15 real-window rest phases (11 watched by the catcher, plus the HDR and dispose rests): 0 loop passes, 0 frames, 0 paced waits, 0 timer-hint changes, 0 posted or sent UI-thread messages.
  - The tween's `_tween.Cancel()` inside its own callback is safe: TweenHandle is a struct, read when the callback runs.
- **5d9a5c71 (word cache 16 MB):** managed only. Entries hold a word, a shared font, a run and its cuts (MeasurementCache.cs:146); no native or GPU object.
  - Real GL window, the real Arabic README (125 KB), walk 1: live heap after a full GC 14.7 → 27.6 MB, GC committed 29.9 → 57.2.
  - Walk 2 adds nothing to the live heap and +0.9 MB to the GC range, so it is bounded.
  - The GC keeping about 30 MB committed over about 28 MB live is for expert 4.
- **0ba99050 (declined preload): nothing left behind.** Reel's empty player rests with 4 NVIDIA threads at 115.1/s (round 14: 4 at 113 without the preload, 5 at 123 with it). Private bytes are 110–113 MB.

CANDIDATES:
- id: media-interop-context-release
  - what: Once the last video source has given its image back, let go of the video interop's hidden GL context and window. Today they live until the window is disposed (`DisposeInterop`, SdlVulkanHost.Interop.cs:961).
  - evidence (Reel on Vulkan, an Aperture-shaped dispose: player and scrubber stills disposed, 8 s rest, 2 cycles × 2 launches):
    - Before any clip: private 110–111 MB, working set 112.
    - After the dispose: 172.5–176.9 private, 160.3–161.9 working set.
    - Region dumps put the retained part in "other" private allocations: 42 → 83 MB, resident 24.5 → 41.1.
    - With the CUDA interop off (`gpu-hwdec-interop=no`), the same +42.5 MB of other and +32 MB working set remain. So it is the GL interop context and the GL driver, not CUDA.
    - My probe of dropping the context failed: an UnsafeAccessor returning a ref to an internal type throws NotSupported on AOT. The share it would free is not measured.
  - real app: Aperture (Vulkan) after leaving playback, browsing its library for the rest of the session. Every session that plays once.
    - Before any playback it is at 143 MB working set and 153 MB private (DECISIONS 0ba99050).
  - expected: Retained memory, at most about 17 MB resident and about 40 MB private, which is about 10 % of Aperture's working set. Prototype first.
    - The GL driver DLL and its threads stay loaded either way (round 14: +19 MB working set, +27 MB private for the driver alone).
  - cost: small–medium.
    - The next video re-creates the context; round 14 timed interop ready at 44–98 ms, driver load included.
    - No API or pixel change.
- id: media-player-cycle-growth (lead)
  - what: Each open/dispose cycle of a player may leave native heap memory behind.
  - evidence:
    - h4, 2 cycles × 4 launches, 8 s after each dispose: private +3.1/+5.2 MB (Vulkan), +1.5 (CUDA interop off), +2.3 (GL).
    - Process heaps +1.5 to +3.6 MB; the managed heap stays at 2.8 MB; threads stay at 27 after each dispose.
    - h3, 3 cycles, 2 s waits, stills kept: +4.0, then +11.0. h2 pairs: 0 to +8.2.
  - real app: Aperture, each episode watched. Roughly 2–5 MB a film, if it is real.
  - expected: Retained memory (leak closed), if a 10-cycle run confirms it grows.
  - cost: unknown. Measure first.

REAL WINDOW:
- **Launches and safety:**
  - 19 launches in 4 holds: 159.5, 132.5, 131.2 and 124.9 s held, after waits of 564.8, 323.4, 130.0 and 0.0 s.
  - Foreground taken 0 times; WS_EX_NOACTIVATE set by the driver 6–10 times a launch; 0 nvlddmkm events; no device lost; the screen was never read.
  - h2 was half wasted: a background tool call passed the specs' variables unexpanded. Its Reel runs played an untagged clip (useful for the dispose memory only). Its Junction runs walked jrepo's default view, not the Arabic file.
  - The h4 idle gate failed: another session's testhost was running. Counts only.
  - h1's Junction ran on round 14's jrepo. The later runs use my own clones.
- **Monitors:** every window on the 4K, 3840×2160 at 150 %, 120 Hz, HDR on (SDR white 240 nits). The 1440p at 100 % is to its left; Windows reports 119 Hz for it.
- **UI thread at rest: 0 runs** in all 7 passive rests (Junction 12 s; Reel 9 s before any clip, 10 s paused after round 14's h3 sequence, 10 s paused after a fresh start, 10 s after dispose, ×2).
  - The cursor was over the app 0 % of the time.
  - 900 WinEvents a launch (NAMECHANGE, LOCATIONCHANGE, STATECHANGE, 100 a second) from another process woke it 0 times: no out-of-context WinEvent hook on the UI thread.
  - 46k–72k stack samples found it only in win32u.dll+AF04, its resting wait.
- **Session input arrives 61–64 times a second with the cursor still** (GetLastInputInfo). Something on this machine injects input constantly.
  - It does not wake an app's thread: Brave's UI thread under the cursor ran 17.8/s, and only 28 of 178 runs came within 3 ms after an input (34 by chance).
  - Input-idle measurements here are meaningless.
- **Per-thread wakes:**
  - Junction GL rest: 1 NVIDIA GL thread at 11.9/s.
  - Reel Vulkan rest: 4 NVIDIA threads at 115/s.
  - nvcuda64's thread runs at 10.4/s only while a player exists, and is gone after the dispose.
- **Player memory:** a paused 320×180 raw clip holds private 485 MB and working set 256 (before the clip: 110 / 112).
  - With `gpu-hwdec-interop=no`: 173 / 152. The CUDA interop costs ~313 MB of commit and ~104 MB resident, for a clip it never decodes.
  - On GL, after the dispose: +108 MB private and +101 MB working set, +50 MB of it in large heap blocks.
- **The GL resize walk adds private commit outside the GC heap**, in write-combined allocations (PAGE_READWRITE|WRITECOMBINE, 0x404) of 11,904 KB each, about one window surface. They are never resident.
  - Walk 1: +19.1 MB on jrepo, +37.4 MB on the Arabic file.
  - Walk 2: +1.3 and +11.6 MB.
  - On Vulkan: 0.0 MB.
- **HDR switch timings** (Reel, the clip tagged PQ; StartupTrace marks; noisy):

  | Switch | Destroy first (today) | Chained (probe) |
  |---|---|---|
  | F16 → 8-bit, first time | create 339.6 / 136.0 ms, then a 0.1 ms present | create 13.4 / 12.3 ms, then a 97.9 / 95.1 ms present |
  | F16 → 8-bit, second time | 20.5 / 19.0 ms in all | about 110 ms |
  | The next switch on | first present 0.0 ms | first present 93–102 ms |

  - One chained run's first switch on took 810 ms in its first present, under the 1 s stop line.
- None of this shows headlessly.

WAITING, RE-RANKED:
- **gl-ui-thread-rest-wakes: close it; a measurement artifact.** Round 14's driver sampled each rest's closing mark late, after the next phase's UI work had begun.
  - Every nonzero round 14 rest was followed at once by UI work: Junction's rest A by the tween (1.3, 2.5/s), rest B by the close (5.7, 4.9/s), Reel's rest C by the second open (10.8, 7.1, 8.7, 8.5/s).
  - Every rest followed by a sleep read 0.
  - In h2 and h3, Junction's RESTB1 sample showed 1 thread: the process was already exiting.
  - My exactly bounded windows read 0 in the same sequence.
  - The same skew shows in mark-based counts this round (the dispose: 9.7/s; the close: 1.4/s).
  - Expert 4's 1.31/s may be the same.
- **mpv-cuda-context-at-rest: not worth it.**
  - The CUDA thread (10.4/s, next to NVIDIA Vulkan's 115/s) exists only while a player exists.
  - libmpv's render API loads every interop when its render context is made, so a software-decoded clip pays CUDA too (~313 MB commit, ~104 MB resident while open; −10–15 MB private, −16–18 MB working set after the dispose).
  - Real films hardware-decode through NVDEC and need CUDA anyway.
- **vk-format-switch-chained: not worth it** (numbers above). Chaining saves about 30–230 ms once a process (the first switch off). It then costs about 90 ms on each later switch off (about 110 against about 20 ms) and about 95 ms on each later switch on.
- **vk-instance-extensions-once:** unchanged code; 7.1–7.4 ms, below the 10 ms bar. Fix it when Vulkan Init is next touched.
- **text-bidi-real-window-private: close it; not a leak, not c06aa102's.** It is the GL driver's write-combined surfaces made by the frames of a resize (REAL WINDOW above). They are bounded, appear without Arabic text, and never on Vulkan.
- **gl-threaded-optimization-profile:** information only, unchanged (no GL code changed; 1 NVIDIA GL thread at 11.9/s at rest).
- **f16-held-drag-post-stall:** not run; it still needs a new approval.

EXAMINED, NOT WORTH IT:
- **HDR switch-off chaining:** the numbers are under WAITING.
- **Skipping the CUDA interop for software-decoded streams:** only a rare stream gains; skipping needs the codec known before the render context is made.

NEEDS THE USER:
- none.

FILES (all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\8e8ba33b-db33-43fa-885a-cb6b82b7253f\scratchpad\r15-gl-vulkan\`):
- **Host:** `host\R15Host.csproj`, `src\Program.cs`. Modes: Junction catch, Reel R14_VIDEO / R15_MODE=fresh|hdr|cycles, Junction R15_MODE=ar; the Regions dump is in Program.cs.
- **Driver and batch:** `Driver.cs`, `Stacks.cs`, `Catch.cs`, `batch.ps1`, `hold.ps1`, `publish.ps1`, `aot\`.
- **Analysis and checks:** `perthread.py`, `between.py`, `threadsets.py`, `selftest.ps1`, `inputrate.ps1`, `undercursor.ps1`.
- **Logs:** `holds.log`, `gate.log`, `runs\h1–h4.jsonl`, and `runs\*.out.txt`, `*.catch.txt`, `*.startup.txt` (the HDR switch marks), `*.framelog.txt`.
- **Fixtures:** `fx\jrepo`, `fx\realar` (clones).
- **Probe patch, reverted:** in SdlVulkanHost.cs, `R15_CHAIN_SWITCH=1` added `&& !(R15ChainSwitch && _format != VkFormat.R16G16B16A16Sfloat)` to the destroy-first test at line 845, plus an "r15 rebuild" StartupTrace mark.
- **For PERFORMANCE.md:** bound a rest window from inside, or keep a sleep after its closing mark; a driver that samples on seeing a log line counts the next phase's work.
