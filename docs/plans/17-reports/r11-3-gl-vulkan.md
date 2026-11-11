SATISFIED: yes

I measured at dee48fbb (detached; .NET 11, SDK 11.0.100) with `HULTUI_A11Y=0`. I report counts and bytes only.

**Monitors changed since round 10.** Only the 1440p is attached: 2560×1440 at 100 % (96 dpi), 200 Hz, HDR on, SDR white 240 nits. The 4K is unplugged. Every window opened at scale 1:
- drag and steady runs: 800×533 px;
- web view: 2160×1160 px (a 2160×779 page);
- video: 960×540 px.

**Real windows: 30 guarded probe launches and 14 UiTests windows.** Each had the default frame, the in-process no-activate hint, the CBT hook plus `WS_EX_NOACTIVATE`, the 5 ms foreground guard, and a posted WM_CLOSE.
- The guard fired 0 times and the hook refused 0 activations (the UiTests guard log too: refused 0, handed back 0).
- The screen was never read.
- No device lost, no new `nvlddmkm` event (the last is still 2026-10-25).
- **One stall over 1 s, stopped and not repeated** (see NEEDS THE USER).
- The longest gap in the other runs was 449 ms, the first F16 drag step (the known first F16 swapchain build).
- The F16 kept-target drag probe was not run. The worktree is clean.

REVIEW: none.
- **Round 10's build** touched no SDL3, GL, Vulkan, present or media code; `src` there changed only its target framework. Window.cs's new per-frame `NoteStructureChange` (1c3cf5c5) leaves steady frames at 0 B.
- **The upgrade (207fbe25) only made JIT cheaper here.** JIT drag frames are now 96 B smaller and equal to AOT, with or without TieredPGO:
  - GL 2,080 → 1,984 B;
  - Vulkan 8-bit 136 → 40 B;
  - F16 2,136 → 2,040 B.
- **Video:** a `time-pos` change costs ~88 B on the UI thread under JIT (96 in round 10). AOT is unchanged.

**What round 10 found, confirmed on .NET 11:**
- **Steady frames: 0.0 B** on GL, Vulkan 8-bit and F16, JIT and AOT.
  - The probe now prints every round, not just the least, so a late tier-1 box would show.
  - With 6 s of frames first (11,700–38,464 frames) and 10 rounds of 120: every round is 0.0 B. This holds with TieredPGO on (the runtime default) and off (round 10's setting), and on AOT.
- **Idle 8 s after a drag: 0 frames, 0 loop passes, 0 waits, 0 wakes** in all 13 drag runs that reached it.
- **Held resize with one post waiting (7e964aeb): 1 frame in the held second and 1 frame a step**, on GL and Vulkan 8-bit, JIT and AOT. Without a post: 0 frames.
- **The drag clip holds** (worst +2.0 layout px, 0 frames over the root).
- **Hidden page gives GPU memory back (58aeb965), DXGI local, this process, 2160×779 page:**

  | Renderer | Shown | Hidden | Change |
  |---|---|---|---|
  | GL | 87.1 MB | 72.2 MB | −14.9 (−17 %) |
  | Vulkan 8-bit | 141.1 MB | 96.6 MB | −44.5 (−32 %) |

  - JIT and AOT are identical, and flat over 5 hide/show cycles. Shown again: +0 MB.
  - Steady 0.0 B with the page shown and after a hide and show.
  - Idle shown or hidden: 0 frames, 0 passes, 0 wakes.
  - The dispose deadline (74cf844c) costs 1 pass and 1 wake. After the dispose: GL 41.6 MB, Vulkan 66.0 MB, 0 browser processes.
  - One GL JIT run had 1 extra frame and pass at the deadline. Its repeat had 0, and so did AOT and Vulkan; I put it down to the user's pointer, as in round 10.
  - The first page after a dispose took 318 ms.
- **Video through mpv (60 fps clip, Gpu sink), bytes a drawn frame (least of four 1 s windows), UI / all threads:**
  - JIT: GL 10.6 / 39.1, Vulkan 8-bit 13.4 / 43.9, F16 13.4 / 43.9.
  - AOT: GL 12.2 / 40.3, F16 10.8 / 35.8.
  - The render path still adds 0 B. Paused 4 s: 0 frames, 0 passes, 0 wakes.
- **Start-up:** not measured again. Nothing in the range touches start-up code, and the upgrade's 42 guarded launches cover it.

CANDIDATES: none.

WAITING, RE-RANKED:
- **uitests-liveresize-order: cause found and the fix measured. Do it as a small test fix (no perf bar).**
  - **Cause:** `GpuImageSourceGLTests` (GpuImageSourceGLTests.cs:28–30) leaves an `ImageElement` holding its own `IGpuImageSource`. The process-wide count `GpuFrameSink.s_otherKind` changes only in the `Source` setter (ImageElement.cs:20, GpuFrameSink.cs:77–81). So `MaySignalOnlyByPosting` stays true for the rest of the process, and a waiting post draws on every timer tick.
  - That gives the full run's exact numbers: "with a post waiting 10" and "a post waiting alone 10", where the tests expect 1.
  - Reproduced with both classes in one run (`--filter "FullyQualifiedName~GpuImageSourceGLTests|FullyQualifiedName~LiveResizeFrameTests"`; the GpuImage class runs first): **the tree as it is fails 2 of 7**, the same two tests.
  - With the image let go after the test (`image.Source = null` in a `finally`): **7 of 7 pass.**
  - Optionally, the two LiveResizeFrameTests could also assert `!GpuFrameSink.MaySignalOnlyByPosting` first, so a future leak names itself.
  - The prototype was reverse-edited; the tree is clean.
- **uitests-workareas-before-dpi-aware: still true in code; cannot be shown today. Do it in the same UiTests change.**
  - The four early calls are unchanged: WindowPlacementTests.cs:29 and :52, WindowStateActivationTests.cs:65, CustomFrameSystemMenuTests.cs:47.
  - With only the 100 % monitor attached, all three DPI contexts (−1, −2, −4) see the same work area, 2560×1392 at (0,0).
- **F16 kept drag targets: stays deferred; not run.**
  - An F16 drag frame costs 2,040 B on both JIT and AOT, against 40 B on 8-bit. The 2,000 B difference is the wrap at each new size:
    - `SwapchainTargetUses.TargetSize` (SwapchainTargets.cs:82–89) keeps the allocated size only for `IsEightBit` formats;
    - so every F16 step makes a new `GRBackendRenderTarget` and `SKSurface`, plus a VkImageView and a framebuffer (SkiaVulkanRenderSurface.cs:677–691, 721–746).
  - Dropping the `IsEightBit` test would remove about 168 KB from an 84-frame drag. That is drag only, never a steady frame, so it clears no bar.
  - Round 5's `nvlddmkm` reason stands. This round's F16 stall argues even more against touching the F16 drag blind.
- **gl-threaded-optimization-profile: still information only.**
  - `SwitchToThread` per paced frame: mean 230 (JIT) and 311 (AOT), max 613 and 585, at 200 Hz. Round 10, at 120 Hz: mean 378/376, max 723/644.
  - The walk happens once per launch, in the first 1.5 s; 0 after a pool burst of 8, 0 at idle. It is the user's driver setting, with no framework lever.

EXAMINED, NOT WORTH IT:
- **.NET 11 tier-1 or PGO allocations in the frame, present and resize paths:** none found. Every steady round is 0.0 B after a long warm-up (JIT with and without PGO, and AOT). Drag and video bytes are equal to or lower than on .NET 10 (numbers above).
- **The leak behind the UiTests failure, in the product:**
  - An app element unmounted while holding its own `IGpuImageSource` would switch the 7e964aeb gate off for the whole process. A held edge with a waiting post then draws 64–65 frames a second instead of 1 (round 9's numbers).
  - No app or sample has its own `IGpuImageSource`; only `src/` implements it. The code documents the trade-off (GpuFrameSink.cs:58–59), so no workload clears a bar.
- **Other-thread bytes during steady frames:** 0.5–0.6 B a frame (about 360 B a 600-frame series). It is off the UI thread and the same on GL and Vulkan; at idle the UI loop has 0 wakes.

NEEDS THE USER:
- **A Vulkan F16 held drag with a post waiting stalled once (`drag-vk16-post`, JIT).** Decide whether one diagnostic re-run is allowed.
  - **What happened:** the warm grow's step 0 took 165 ms. Then no frame came for 1,006 ms, and the watchdog stopped the run (exit 6).
  - **No side effects:** no `nvlddmkm` event, no System or Application event between 10:37 and 10:39, the foreground was never taken.
  - **Not repeated, per the rules,** and the AOT F16 post run was skipped.
  - **First run of its kind:** this was the first time F16 was ever run with the held post. Round 9 step 7a and round 10 ran only GL and Vulkan 8-bit with it.
  - **The plain F16 drag is fine:** 4 runs this round (JIT and AOT, with and without a 6 s warm-up) and 2 in round 10 passed.
  - **The code shows no difference:** `RenderLiveResizeFrame` takes the same branches for 8-bit and F16 (Application.cs:589–605), so the post's role is not shown.
  - **The cause is unknown:** the probe logs a step only after `SetWindowPos` returns. It cannot tell a UI thread blocked inside a step (a driver wait: `AcquireNextImage` and `WaitForFences` use no timeout, SdlVulkanHost.cs:1153 and :1169; or a deadlock) from steps that draw nothing.
  - **The proposed re-run:** at most one, JIT, F16, with a post. It would log each step before `SetWindowPos`, take a dump (`dotnet-dump collect`) at the stall instead of exiting, and stop at the first stall.
  - **Why it matters:** if it is a deadlock, an app with HDR and F16 could freeze during a held resize. If the user declines, the item stays open with no change to F16 resize code.

FILES: all under `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\ec6d577c-f9ba-4479-acbd-908e67c0ea0a\scratchpad\r11-gl-vulkan\`
- Probe host:
  - `host\R11Host.csproj`: round 10's host on net11.0 against this worktree, TieredPGO left at the default.
  - The AOT publish is `aot\R11Host.exe` (`publish.ps1`).
  - `src\*.cs`: round 10's sources. `src\R9.cs` now prints every steady round and the all-thread bytes, and adds `PROBE_WARM_MS` and `PROBE_ROUNDS`.
- Runners: `run.ps1` (one launch; prints `nvlddmkm` events) and `batch.ps1` (sets steady, long, post, post2, web, more, plus round 10's).
- Monitor checks: `monitors.ps1`, `monitors-aware.ps1`.
- Logs: `batch-steady.log`, `batch-long.log`, `batch-post.log`, `batch-post2.log`, `batch-web.log`, `batch-more.log`, and `runs\*.out.txt` / `runs\*.err.txt`. The stall is `runs\drag-vk16-post.*`.
- UiTests check: `uitests-fix.log` and `uitests-base.log` (7/7 against 5/7), with their guard logs `uitests-guard-fix.log` and `uitests-guard-base.log`.
- Review: `commits.txt`, `src-stat.txt`, `diff-window.txt`, `diff-props.txt`.
- Build logs: `build.log`, `publish.log`.
