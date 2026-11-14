SATISFIED: no. One candidate, junction-commit-detail-whole-patch, is promoted from Waiting with real-window numbers. A second, small one is the watcher's per-event debounce. Neither is a round 13 regression.

Worktree: detached at e6f7b5aa, SDK 11.0.100, `git status` clean. Build servers are shut down, and no host, watcher or background task is running. Only bytes, counts and complexity are reported, no timings.

REVIEW: none. Every round 13 source commit was read.
- **da72e01b (weak table).**
  - One `StrongBox<int>` per summary loader. `KeyedAsyncLoader` capacity is 32, so the table is bounded and its entries die with their loaders. The `static` factory lambda means 0 B per `SummaryFor` after the first.
  - Real window, Junction with 3 repos, 2 × 30 `SelectRepo` switches: heap after a full GC was 10.5 → 24.9 → 9.8 MB in one launch and 12.8 → 13.7 → 10.5 MB in the other. No growth.
- **698c2758 (CJK cuts).**
  - `_naturalsMade` is 65,560 B, made once per pipeline (one per process). It sits under both the 85,000 B default LOH threshold and the apps' 524,288.
  - Natural-end words (≤ 1,024 chars) and their cuts are weighed into the word cache's 4 MB (`WordCacheBytes`). Each array is ≤ 4 KB, the glyphs ≤ ~24 KB, so nothing reaches the LOH, and the maximum kept per document is unchanged.
  - `_lastCuts` holds a reference to the document string (TextArea's `MeasurePrefixes` passes the whole text) until the next cut. That is at most one string, the same lifetime class as `PrefixView`'s held text, not growth (see EXAMINED).
- **505123d7 (labels kept).**
  - Retained size is the same as round 12 after any walk; only the churn is gone. Junction History: `ItemLabel[1000]` (24 B each, 24 KB) plus about 1,000 strings of ~180–200 B, roughly 0.2 MB.
  - They are dropped on new Items (every History reload) or a new delegate. `ForgetReplaced` clears replaced entries, so no stale items are held.
  - Lists over 2,000 items keep no labels from the UI walk. The array is still sized to the item count (24 B × n), which reaches the LOH only past 21,845 items.
- **4fdb56cb (purge deadline).** 0 B per loop pass: a struct enumerator over a `Dictionary` with ≤ 1 window. Aperture and Reel at rest showed 0 B on all threads and 0 UI-thread wakes in 20 s.
- **cd7e3db2 / d797205f (timer policy).** 0 B per wait: timestamp arithmetic, with the `Action<bool>` made once. `SDL_SetHint` runs only on a change. The hint read "0" at the first frame in all 7 launches.
- **d73b1439 (click-handler notification).**
  - One extra 0 B handler scan per click handler added. A 0 B ancestor walk when a kept box gains or loses its click handlers.
  - Bytes per key fell or held against round 13: Aperture Tab 291 B (481–609), Reel Tab 210 B (526), Meridian Tab 121 B (413), Vesper Down 5.34 KB (5.6–5.7).
- **Watcher (15f97351, 013e1f1b, d413e001).** One more `FileSystemWatcher` per reftable repository. `Forget()` cancels one debounce, which costs one extra caught exception per `RefreshAll`. The per-event cost is older than round 13 (candidate 2).

CANDIDATES:
- id: junction-commit-detail-whole-patch (owners 8/6, with 4; promoted from Waiting)
  what: `LoadDetail` reads and parses the whole commit patch into `GitDiffLine` objects. Only the selected file's diff is ever read (`DiffByPath[_selectedFilePath]`; Review reads `OldPath`, which is always null). Load the opening file's patch, or the whole patch only below a size.
  evidence:
  - Probe: the same calls as `LoadDetail`, against the same calls plus one `diff-tree -- <files[0]>`, on HultUI's own last 300 commits (to e6f7b5aa), JIT, sv-SE.
  - Whole patch: 628.7 MB allocated on all threads (mean 2.1 MB, p50 226 KB, p90 5.7 MB, max 87.4 MB for facced69's 8.0 M chars), 22/17/8 gen 0/1/2 GCs. That is 11.6 B allocated and 4.1 B kept per patch character.
  - Opening file only: 114.4 MB (−82 %), 0 GCs, at most 0.5 MB kept.
  - Kept while a commit is selected: up to 32.0 MB (facced69: 223,012 lines, 185 files) against 38 KB.
  - A cap (whole patch below 262,144 chars, else per file): 133.7 MB (−79 %), with 47 of 300 loads going per file and at most 1.0 MB kept.
  - The worst cases are the plan's own merges and capture commits: each round's merge is 3–8 M chars and 33–87 MB a load.
  - jrepo's 20 commits below HEAD: whole patch 147 MB, opening file 4.9 MB.
  - `ParseUnifiedDiff` also makes 4 culture-sensitive `StartsWith(string)` calls per content line ("diff --git ", "--- ", "+++ ", "@@ "), about 890K on facced69. The machine's culture is Swedish, and the apps don't set `InvariantGlobalization` while the PerfSuite does, so suite rows never pay this. Ordinal comparisons are a free part of this step.
  real app: Junction on the HultUI repo, History Down at a reading pace (each selection rests past the 120 ms `DetailRest`).
  - Real window, jrepo, 20 Downs 400 ms apart, 2 launches: 187.0 / 205.8 MB on all threads (9.35 / 10.3 MB a key), 12/11/3 and 14/12/4 GCs.
  - 182 git runs, 9.1 a key. UI thread 24–26 KB a key.
  - Repo switches on the fixtures (each lands on HEAD and loads its detail): 25.8 MB a switch on all threads, 16–18 gen 2 GCs per 30 switches (one launch; in the other only 23 and 12 of each phase's 30 switches got through, 7.9 / 4.5 MB on all threads per scripted switch).
  expected: memory churn −82 % on all threads per detail load (bar 10 %); about −76 % of the History walk's all-thread bytes; gen 2 GCs 8 → 0 per 300 loads; retained up to 32 MB → ≤ 1 MB.
  cost: medium, app only. Switching files inside the Commit tab would need a git run per file's first visit (or keep the whole patch below the cap). No API or pixel change.
- id: dotnet-junction-watcher-debounce-timer (owners 4/6)
  what: `GitWatcher.ScheduleRefresh` makes a new CancellationTokenSource, a `Task.Run` and a `Task.Delay` on every event, and cancels the previous one, which throws a caught exception. The work-tree filter also builds two strings per event. Re-arm one `ITimer` under the existing lock instead.
  evidence:
  - Probe, NativeAOT: 1,000 files written 1 ms apart into a fake repository's obj folder, ~2.9 events a file.
    - No watcher: 4,734–4,746 B a file.
    - Bare `FileSystemWatcher`: 5,868 B.
    - `GitWatcher`: 9,439–9,565 B and 2,690–2,875 exceptions.
    - Prototype on one timer: 5,868 B and 0 exceptions.
  - So about 1.2 KB, one thread-pool task, one timer and one exception per event, all removed. JIT gives the same picture (9,828–9,886 B, 2,966–2,980 exceptions).
  - GitWatcher.cs:135–139 and :157–185.
  real app: Junction open on the repo while the user builds it (this user's daily loop). Events counted in the worktree:
  - Touch a Core file and build Junction: 267 events, 266 under bin/obj, so about 330 KB and 267 exceptions.
  - An AOT publish: 551 events, about 680 KB and 551 exceptions.
  - It scales with files written: a 1,000-file checkout is ~3,000 events. All of it is off the UI thread and gen 0.
  expected: −76 % of the watcher's bytes per event (4.7–4.8 KB → 1.1 KB a file, the rest is the FSW's own); exceptions per event 1 → 0 (O(events) → 0).
  cost: small, app only. Same debounce rule. `Forget` and `Dispose` stop the timer.

REAL WINDOW (my NativeAOT hosts from e6f7b5aa: round 13's host plus in-process pointer, wheel, repo-switch and full-GC steps; GL for Junction, Vesper, Meridian; Vulkan 8-bit for Aperture, Reel):
- **Runs and guards:**
  - 2 holds: h1 158.3 s after a 307.0 s wait; h2 176.4 s after a 229.3 s wait. 7 launches.
  - 0 foreground takes, all exit 0 on WM_CLOSE, 0 nvlddmkm events. Input was in-process only.
  - Monitors: the 4K 3840×2160 at 120 Hz, primary, 150 % (window DPI 144 every launch), and the 1440p 2560×1440 at 120 Hz attached to its left. System timer 1.000 ms at both hold starts (another process holds it).
- **At rest, 20 s:** 0 B on all threads, 0 frames, 0 GCs in 7 of 7 launches.
  - The UI thread woke 0 times except in one of 3 Junction launches: 1.31 a second in SDL's message wait, still 0 B and 0 frames. It was absent with the loop log on and with one repo, so it is likely the user's real pointer.
  - NVIDIA threads: GL 9.8–11.3 a second; Vulkan 100 / 10.2 / 4.2–4.7 / 1.0 (115.6–115.8 total). Same as round 13.
- **Pointer moves (150 in-process at 8 ms):** 0 B on 141–149 of 150 in every app. The first move costs ~3 KB (entering the hover chain). Junction's row crossings cost 256 B (×6). 0–8 frames. Nothing to cut.
- **What headless cannot show:**
  - Junction's History walk at a reading pace allocates 9.35–10.3 MB a key on all threads against 24–26 KB on the UI thread (×400), with 3–4 gen 2 GCs per 20 keys, and starts 9.1 git processes a key.
  - Vesper's wheel: 40 ticks drew 13 frames, 4.6 KB a tick, 14.2 KB a drawn frame.

WAITING, RE-RANKED:
- **junction-commit-detail-whole-patch:** promoted (above).
- **findbar-countonscreen-foreach:** still a nit, unchanged (FindBar.cs:212; round 13 touched no FindBar). 32 B per Enter or per switch with the bar open, against ≥ 8 KB per Enter. Fix it when FindBar is next touched.
- **fedora-local-dotnet:** keep waiting. FedoraLinux-44 `updates` and `updates-testing` both have only `dotnet-sdk-11.0` 11.0.100~rc.3.26522.148-0.13.fc44. Installed rpm is dotnet-sdk-10.0-10.0.112. Both links (`/usr/local/bin/dotnet` and `/usr/local/sbin/dotnet`) still point at `/usr/local/share/dotnet`, which runs 11.0.100. WSL was terminated after the check.
- **textarea-chunked-document:** keep parked. No round 13 change to TextArea storage, and no real-app text near 256k characters.

EXAMINED, NOT WORTH IT:
- **Per git process start:** 27.7 KB (`Run`) and 31.4 KB (`RunAsync`) on all threads, 0 GCs over 300 runs. Allocation ticks are almost all char[] and byte[]: the StreamReader and FileStream buffers that `Process` makes at start for redirected streams. Avoidable only with our own pipes. A 17-run fetch is ~470 KB, gen 0, off the UI thread.
- **`_lastCuts` holding a closed document's text until the next cut:** one string (Meridian's 89k body would be 178 KB), no growth. Clearing it when `PrefixView` releases is one line but saves at most one document.
- **`SelectRepo`'s own UI bytes:** 180–240 KB a switch (`Persist()` writes settings on the UI thread, plus `RequestPreload`). A rare user action, below the bars.
- **Repo-switch retained memory:** see REVIEW (no growth over 60 switches).
- **Culture-sensitive string calls in `src`:** none in hot paths. The grep hits in EmbeddedLanguages.cs are span (ordinal) overloads, and the rest are `List.IndexOf`.

NEEDS THE USER: none. Whether the Commit tab should load each file's diff when it is first opened (a git run per first file visit) instead of the whole patch is a behaviour choice for owners 8/6. The 256K-character cap keeps today's behaviour for 253 of 300 commits.

FILES (in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\41943a9b-f657-4483-971a-a2b346b967f7\scratchpad\r14-dotnet\):
- host\src\Program.cs, host\src\Script.cs: the probe host (multi-repo Junction, ptr/sweep/wheel/repo/heap steps, SNAP and HEAP lines, DPI).
- host\Driver.cs (round 13's), host\Monitors.cs, host\batch14.ps1, host\pub.ps1, host\pub-aot.log, host\sum.py, host\holds.log.
- host\runs\h1.jsonl, host\runs\h2.jsonl, *.out.txt, *.frames.csv, h2-1-Junction-fl.framelog.txt; host\h1-sum.txt, host\h2-sum.txt.
- aot\<App>\: the five AOT hosts.
- patchprobe\Program.cs, sum.py, cap.py, detail300.csv, detail-jrepo22.csv; repo-patch-sizes.txt.
- gitrun\Program.cs: bytes per git run.
- watchprobe\Program.cs (burst/plain/fix/count modes), aotproj\ (AOT build), burst-aot.txt, count-build.txt, count-publish.txt.
- fx\jrepo, fx\bigrepo, fx\bigrepo2: fixture copies.
- Diffs read: da72e01b.diff, 698c2758.diff, 505123d7.diff, 4fdb56cb.diff, cd7e3db2.diff, d73b1439.diff.
