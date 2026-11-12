SATISFIED: yes. Nothing in .NET-runtime code clears a bar. At rest every app allocates 0 B and runs 0 GCs. All of the Vulkan apps' ~112–125 wakes a second at rest come from the NVIDIA Vulkan device's own threads (lead answered below).

Worktree: detached at 2ced4e26, SDK 11.0.100, clean. Build servers are shut down and no watcher or host is running. No timings are reported; everything below is a count or a byte figure.

REVIEW: none. Code read on every round 12 source commit, with real-window AOT numbers below.
- **ce6f4962 (fallback table).** 4,096 × 32 B = 131,096 B. It is made once per `SkiaTextServices` (one per platform) at the first fallback question.
  - It stays in gen 0 under AppRuntime.props' 524,288 B LOH threshold.
  - Slots hold no objects. `CultureInfo.CurrentUICulture` is a static read. 0 B per hit.
- **7a753337 (word cache).** The entry has 4 refs + 6 × 4 B = 56 B. The final 8,192-entry array is 458,776 B, 65 KB under the LOH threshold.
  - Guard for later: one more 8-byte field (64 B entries) makes it 524,312 B, which puts it on the LOH.
  - `RunCuts` is made only for words a prefix table shapes (8+ characters) and is counted in the entry's weight.
- **5d973076 (UIA wrappers, TreeDiff sets).**
  - The per-thread sets are kept only up to 4,096 ids, and only the UI thread runs diffs in the apps. Bounded.
  - The `t_seen ?? []` and empty-list returns are as before.
- **2d18a361 (HDR switch).**
  - `HdrPictureFollower` allocates 0 B a frame.
  - mpv's event thread reads 1–2 string properties per reconfigure. The `StartupTrace.Mark` strings are built only on a switch.
  - Nothing is added to the frame path.
- **b6c6eb6f (watcher).** Adds one closure per debounced callback.
  - Real window: Junction at rest started 0 git runs in 20 s (job count 57 → 57). The `git status` index rewrite does not echo back.
- **3912f391 (`TabDirty`).** One `Signal<int>` increment, 0 B.
- **c0ce9387, a6bfb1fc.** Counters and an owned deadline (`Application.Earliest`). No new wake at rest.
- **67fc0884 (appended items told).** `Array.Resize(ref _labels, items.Count)` on each append past the told length is exact-size: 24 B × items.
  - `GetItemLabel` already resized the same way on every count change, and the patch's label loop is already O(n).
  - So the complexity class is unchanged: about 4.8 KB per append at 200 items.

CANDIDATES: none.

REAL WINDOW (NativeAOT probe hosts built from 2ced4e26 = round 12's start-x host plus snapshots at script marks; default frame, default placement on the primary display — the 4K at 150 %, 120 Hz; the monitor was not read back per launch):
- **Runs and guards.**
  - 12 launches in 2 holds: h1 207.3 s after a 165.6 s wait; h2 207.6 s after a 266.7 s wait.
  - A third hold was cancelled while still waiting: it never took the lock or opened a window, and a windowless probe answered its question.
  - 0 foreground takes, every app exit 0 on WM_CLOSE, 0 nvlddmkm events. Input was in-process only (`HandleInputEvent`).
- **At rest, 20 s, all threads:** 0 B, 0 frames, 0 GCs in 11 of 12 launches. The exception: Aperture once 1,984 B with 1 frame, while its UI thread woke 10.1/s in SDL's wait, which looks like real pointer traffic (absent in its other launch).
  - The UI thread woke 0 times in every other launch. Runtime threads (finalizer, thread pool) 0–0.15/s.
  - Total allocated through start, rest and keys: Reel 3.58 MB, Meridian 3.65, Aperture 3.86, Vesper 5.37, Junction 29.8. That is 0 GCs in every app but Junction (1 gen 0, during keys).
- **Per key, 40 keys at 100 ms, all threads (the host's own closure included):**

  | App | Action | All threads a key | UI thread a key | GCs | Notes |
  |---|---|---|---|---|---|
  | Junction (fixture repo) | History Down | 357.1 KB (14,285,344 B) | 29.2 KB | 1 gen 0 | 12 git runs = 3 commit-detail loads after the selection rested |
  | Vesper | Down | 5.6–5.7 KB | — | 0 | |
  | Meridian | Tab | 413 B | — | 0 | |
  | Aperture (sign-in page) | Tab | 481–609 B | — | 0 | |
  | Reel (empty player) | Tab | 526 B | — | 0 | |

  - Junction: each detail load reads the whole commit patch. Two fixture commits are 6.1 M characters. The 29.2 KB UI figure matches round 12's headless History Down (27 KB).
  - Every app drew 1 frame a key.
  - What headless cannot show: Junction's all-thread bytes are 12× its UI bytes (the detail loads off the UI thread).
- **Lead: Aperture and Reel at rest, who wakes (wakes a second, guard and probe threads excluded).**

  | App | Rest wakes/s | Launches |
  |---|---|---|
  | Reel | 109.8–113.6 | 3 |
  | Aperture | 117.1 (124.6 with the pointer traffic) | 2 |

  - **Driver:** four NVIDIA threads, all starting at nvoglv64.dll+0xA3D280, all in timed `WaitForSingleObjectEx` (wait reason UserRequest) through nvoglv64+0xA3BA4A. Stacks walked out of process with dbghelp, the thread suspended only for the walk.

    | Thread loop | Wakes/s | Note |
    |---|---|---|
    | +0x106C528 → +0x1075D16 | 94.0–97.3 | Vulkan ICD code |
    | +0xF62DA4 | 10.0–10.3 | GL apps have this one too, as their only waking thread |
    | +0xF430ED | 4.05–6.0 | |
    | +0xF456C2 | 1.0–1.05 | |
    | +0xA33730 (sometimes) | 1.3–4.2 | in `MsgWaitForMultipleObjectsEx` |

  - **Runtime:** ≤ 0.15/s. **Ours:** 0 (the UI thread 0/s; nothing of HultUI's in any stack).
  - **Windowless proof (`TimerVk.cs`).** A plain console process that only creates a Vulkan instance and device (no window, surface or swapchain) shows the same four threads at 96.9 / 10.0 / 4.1 / 1.0 per second.
    - The device alone raises the process to 1 ms timer resolution: Sleep(5) 64.3 → 171.9 a second while it exists, 64.4 after `vkDestroyDevice`.
    - `TimerProbe.cs` shows a 10 ms wait returns 63.9–64.4/s at the default resolution and 95.6–97.2/s at 1 ms. So the ~97/s thread is the driver's own 10 ms poll, timed by the driver's own 1 ms request.
  - **Our timer request.** SDL also calls `timeBeginPeriod(1)` for the app's whole life (`SDL_HINT_TIMER_RESOLUTION`, which HultUI never sets).
    - Vulkan apps: turning that off left them at 1 ms in 3 of 4 launches, because the device holds it. Once Reel dropped to 88.2/s; not explained.
    - GL apps: off reliably drops the process to 15.6 ms (probe guard 175–179 → 67–69/s, 3 of 3), but their own wakes do not fall (the driver's poll 10.2–10.35 → 11.2–11.7/s).
    - System-wide: the global resolution was already 1.000 ms at both hold starts, with no HultUI window open. Another process holds it on this desktop, so our request adds no timer interrupts here.
  - **Answer:** none of the ~116 is ours. It is the price of holding a Vulkan device on this NVIDIA driver (610.88). Only GL avoids it (~10/s), and the apps' Vulkan choice is already decided (Vulkan stays opt-in, for video).

WAITING, RE-RANKED:
- **findbar-countonscreen-foreach:** still a nit, still there (FindBar.cs:212 `foreach` over `bar.Children`, 32 B on AOT). `RewriteCount` (FindBar.cs:317–319) calls it once per switch with the bar open. 32 B per Enter or per such switch, against ≥ 8 KB per Enter. Fix it when FindBar is next touched.
- **textarea-chunked-document:** keep parked. Round 12 changed no TextArea storage. No typed text in the real apps comes near 256k characters (Meridian's 89k body, ~238–241 KB a key, stands).
- **fedora-local-dotnet:** keep waiting. Checked read-only:
  - FedoraLinux-44's `updates` has only `dotnet-sdk-11.0` 11.0.100~rc.3.26522.148-0.13.fc44, not the final SDK. Installed rpms are SDK 10.0.112 only.
  - New detail: the local install has two links, `/usr/local/bin/dotnet` and `/usr/local/sbin/dotnet`, both → `/usr/local/share/dotnet/dotnet`. `which dotnet` picks the sbin one. Remove both when the final SDK ships.

EXAMINED, NOT WORTH IT:
- **Dropping SDL's 1 ms timer request at rest (ask only while a paced wait < ~32 ms is pending):**
  - GL apps: 0 own wakes saved (10.3 → 11.4/s).
  - Vulkan apps: the NVIDIA device holds 1 ms anyway (3 of 4 launches unchanged).
  - System timer: unmeasurable here, because another process already holds 1 ms. It would matter only on a machine where a resting GL HultUI app is the only one asking: the system clock would run at 64 Hz instead of 1 kHz.
  - The Vulkan frame pacing (`WaitForEvents` in whole ms) needs 1 ms while animating.
  - Reopen only with a measurement from such a machine.
- **Aperture/Reel on GL until a video plays:** rest wakes ~112 → ~10/s, but it means a runtime renderer switch. That is large and goes against the decision that Vulkan is opt-in for video.
- **Junction's commit-detail load reading the whole patch (6.1 M characters on two fixture commits):** an app workload (expert 8). It runs after the selection rests, off the UI thread. Noted only.
- **67fc0884's exact-size labels resize:** see REVIEW; same complexity as before.

NEEDS THE USER: none.

For PERFORMANCE.md:
- An NVIDIA Vulkan device runs four polling threads (~97, 10, 4, 1 wakes/s) and holds the process at 1 ms timer resolution for its life, window or not. So a Vulkan app rests at ~112 wakes/s here, a GL app at ~10.
- SDL holds 1 ms timer resolution for the app's life (`SDL_HINT_TIMER_RESOLUTION`). A probe thread's Sleep(5) wakes 170/s at 1 ms and 64/s without, so exclude probe threads from wake counts.

FILES (all in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\17c11f5e-9de0-4477-8f58-0d0a5e006605\scratchpad\r13-dotnet\):
- host\src\Program.cs, host\src\Script.cs: start-x plus `HOST_TIMER_RES`, the UI thread id, and SNAP lines at "mark:" steps.
- host\Driver.cs: the guarded driver plus thread state and wait reason, the system timer resolution, and dbghelp out-of-process stacks.
- host\batch.ps1: desktop lock and guards.
- host\sum.py, host\holds.log, host\pub.ps1, host\pub-aot.log.
- host\runs\h1.jsonl, host\runs\h2.jsonl, *.out.txt, *.frames.csv, h1/h2-batch.log.
- aot\<App>\: the five AOT hosts.
- TimerProbe.cs: Sleep and wait rates with and without 1 ms.
- TimerVk.cs: a windowless Vulkan instance and device; timer resolution and driver threads.
- threads12.py: per-thread wakes from round 12 step 5's jsonl.
- fx\jrepo: a copy of the fixture repo.
