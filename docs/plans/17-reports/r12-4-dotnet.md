SATISFIED: yes. Nothing in .NET-runtime code clears a bar: 0 B and 0 GCs at rest, and the word cache allocates 0 B once full. The two candidates below are leads I measured. They belong to expert 3 (GL/Vulkan) and expert 7 (accessibility).

The worktree is detached at f9c44628, SDK 11.0.100, and clean. The probe patch was removed by `git apply -R`; a copy is in the scratchpad. Build servers are shut down. No timings are reported; everything below is a count or a byte figure.

REVIEW: none.
- **7d823b34 / 6274c26c (`WordCache`).** Measured with an out-of-repo probe (`wordcache\`), JIT and NativeAOT, 3 rounds each, identical results.
  - Filling to 8,192 entries: 851,960 B total, 104.0 B a put. All of it is the doublings: stages 53,296 / 106,544 / 213,144 / 426,168 B at 1k/2k/4k/8k entries, 0 GCs.
  - At full size, 0 B for each of these: 100,000 evicting puts, 999,424 span hits, 1,000 replacing puts, 10,000 puts evicting by weight.
  - The last entry array is 393 KB, below the apps' 512 KB large-object threshold, so it stays in gen 0.
- **6fb8ff07 (Meridian's kept bodies).** Code read only.
  - Capped at 4. Dropped on a send or a Runner replacement (`ForgetReplacedBodies`), on a tab close, on a workspace switch or app close (`StopResponseWork`), by least-recently-shown eviction, and after a theme change.
  - The theme drop is lazy: the old bodies (≤ ~4.6 MB, the step's figure) stay until a body is next shown. Bounded, and theme changes are rare: no objection.
- **635d6c53..e280bd82 (views let go).** Code read only; no new holder. The step's path search on 11 switches stands.
- **0ed79e8a (`TextSearch.Version`).**
  - The version is an int bump. `Service` adds one comparison per owner build.
  - `RewriteCount` runs only when the version moved while the bar is open. That is the 32 B enumerator plus the count string, once per switch.
- **Others.**
  - 1c7dc920: `Thickness?` on a record goes through `EqualityComparer` and does not box on .NET 11 (round 11 probe).
  - 4cced183: at most 2,000 comparisons per patch, 0 B.
  - 9fcf3696: when off, one static bool per pass.
  - a72ed576: no new allocation.

CANDIDATES:
- id: gl-vulkan-resize-keeps-native-memory (lead; owner 3)
  what: In the Vulkan apps, one resize by `SetWindowPos` (no modal drag, so no drag end) leaves 45–80 MB more native private memory and two more driver threads 20 s after its last step.
  evidence: Each resize is 40 steps of 12×8 px out and back, then a 20 s rest. Private MB at rest → at resize end → 20 s later:
  - Aperture: 155.1 → 348.8 → 235.4. Write-combined/no-cache private memory 52.8 → 211.3 → 97.9.
  - Reel: 131.2 → 200.0 → 176.2. Write-combined 41.8 → 76.0 → 52.2.
  - Junction (GL), for comparison: 104.7 → 110.2 → 109.3.
  - After the resize, two driver threads appear and stay: nvwgf2umx.dll+0xEBA250 at 25/s and nvoglv64.dll+0xA3D280 at 10/s.
  - Managed heap moves only 0.1–0.5 MB.
  - Only 20 s after one resize was measured: not yet shown whether it is freed later or grows with each resize.
  real app: Aperture or Reel moved or resized by a tool or a snap; also DPI or placement changes. Each event adds about +34…+52 % private bytes.
  expected: Retained-memory bar (≥ 5 % of working set) if the kept memory is the old or over-allocated swapchain. The idle bar too: +35 wakes/s.
  cost: unknown; GL/Vulkan expert to confirm (check whether more resizes grow it).
- id: a11y-bounds-events-com-traffic (lead; owner 7)
  what: Every raised property change makes the in-process UIA core call our providers about 47 times, synchronously on the UI thread. A layout change raises one BoundingRectangle change for every element that moved.
  evidence:
  - Per event (all five apps agree, 46.0–47.5): QI 13, Release 17, AddRef 4, GetProviderOptions 5, GetHostRawElementProvider 3, GetFragmentRoot 2, Navigate, GetRuntimeId, GetPropertyValue(30020, NativeWindowHandle) 1 each. Plus one `SafeArrayCreateVector`.
  - Other threads: 0–1 calls. Managed bytes inside the raises: ≤ 21 B a raise, so the thunks allocate nothing.
  - Events per resize step: Vesper 74.7 (3,517 COM calls a step), Junction 28.5 (1,380), Meridian 15.2, Aperture 14.4, Reel 1.7.
  - All-thread bytes, bridge on minus off: Junction +15 KB a step, Vesper +2.5 KB, Aperture +0.6 KB.
  real app: Window resizes and pane-splitter drags in Vesper and Meridian: O(elements moved) per accessibility layout refresh, plus one cross-process event per listening client per element.
  expected: Scaling bar, if raising bounds changes only for the focused or selected element is acceptable to a reader.
  cost: small to medium. Reader-visible, so it may need the user.

REAL WINDOW (1440p at 100 % (96 dpi), 200 Hz, HDR on; NativeAOT probe hosts built from f9c44628 plus counters):
- **Runs and guards.**
  - 13 launches in 4 holds: 199, 211, 205 and 185 s, after waits of 0, 293, 600 and 280 s.
  - 0 foreground takes, 0 refused activations, the guard never fired. Every app closed on WM_CLOSE with exit 0; 0 nvlddmkm events.
  - Apps: GL Junction (fixture repo), Vesper, Meridian; Vulkan Aperture (signed in to the lab server, Home loading) and Reel (empty).
- **At rest, all five apps, bridge on and off:**
  - 0 B in 20–60 s on all threads for Junction and Vesper. Meridian 808 B a minute, Reel 672 B a minute. Aperture 6.4–7.3 KB once (the same in 20 s and 60 s windows).
  - 0 frames, 0 passes, GCs 0/0/0.
  - No GC at all in any launch (start, rest and resize): no app reached its first gen-0 collection.
- **Start, 8–10 s after the first frame, all threads:**

  | App | All threads | UI thread | Heap after start | Working set |
  |---|---|---|---|---|
  | Junction | 11.3–12.8 MB | 2.2–3.7 MB | 13.8 MB | 105–108 MB |
  | Vesper | 0.64–0.71 MB | — | 2.3–2.4 MB | 88 MB |
  | Meridian | ~4.5 KB | — | 0.95 MB | 83–84 MB |
  | Aperture | 3.1–3.3 MB | 1.0–1.1 MB | 6.0 MB | 136–153 MB |
  | Reel | ~7 KB | — | 0.95 MB | 112–113 MB |

  Aperture runs 55–60 threads at start (19 Windows thread-pool workers) and 48–53 after.
- **Wakes at rest, by thread.**
  - App threads: the UI thread wakes 0 times (6 in 40 s in one run). Windows thread-pool workers 0–0.3/s, and 3–4 of them exit within the minute. The combase thread wakes about once a minute with the bridge on.
  - GL apps: one NVIDIA thread (nvoglv64+0xA3D280) at 10.0–10.4/s.
  - Vulkan apps: a pair at about 97/s each (nvoglv64+0xA3D280 and nvwgf2umx+0xEBA250, equal counts). Plus 10, 4 and 1 per second: about 210 wakes/s at rest, 0–31 CPU ms a minute.
  - The Vulkan apps load nvwgf2umx (88 MB image), D3D12Core, d3d11 and dcomp: NVIDIA presents Vulkan through DXGI here.
  - OBS's `graphics-hook64.dll` (an implicit Vulkan layer) is loaded into every Vulkan app on this machine. Worth knowing for anyone measuring Vulkan here.
- **Resize, 40 steps, all threads, bridge off (on):**
  - Junction 25.1 (40.3) KB a step, Vesper 46.5 (49.0), Meridian 6.6 (6.9), Aperture 10.6 (11.2), Reel 6.8. 0 GCs.
  - The headless walk shows 3.3 KB a step for Aperture's library resize.
  - `FrameLog` counts 0 frames and about 0 pass bytes during these resizes: the frames are drawn by `RenderLiveResizeFrame` inside the wait. For the guidelines: measure resizes with all-thread bytes, not FrameLog's frames.
- **UIA client traffic, bridge on:**
  - At rest: 0–1 incoming calls a minute.
  - At start, incoming/outgoing: Junction 690/46, Vesper 326/23, Aperture 1,104/72, Meridian 0/8, Reel 0/2.
  - During the resize: Junction 52.6–55.2k / 2.3–2.5k, Vesper 140.7k / 6.1k.
  - With `HULTUI_A11Y=0`, UIAutomationCore.dll is not loaded and there are 0 calls. On, it is the only extra module (4.2 MB image).
- **One unexplained hold.** In hold 1, both bridge-on launches had +55–58 MB private bytes: Junction 157.5 against 102.5 MB, Vesper 138.5 against 84.7. Junction's UI thread also had 1,081 context switches at start, against 117.
  - The COM traffic was identical (690 calls), and holds 2 and 4 did not reproduce it (Junction on 103.3–104.6 MB).
  - Likely another UIA client's behaviour at that time, not HultUI. Not reproduced, so no candidate.

WAITING, RE-RANKED:
- findbar-countonscreen-foreach: still a nit, still there. `FindBar.cs:208–215` still loops `foreach` over `bar.Children` (the interface enumerator, 32 B on AOT). 0ed79e8a added a second caller, `RewriteCount`, which runs only on a switch with the bar open. 32 B per Enter or per such switch, against ≥ 8 KB per Enter. Fix it when FindBar is next touched.

EXAMINED, NOT WORTH IT:
- Bytes per resize step (6.6–46.5 KB): re-layout at each new width, where caches miss by design. Not a steady-state frame, and 0 GCs in 40 steps.
- The UIA thunks' managed bytes: ≤ 21 B a raise (480 B over 23 raises, including the counter's own first inserts).
- Start-up allocation: below the first gen-0 budget in every app. No GC to remove.
- Aperture's 6–7 KB at rest happens once per run, not per second.
- NativeAOT's Windows thread pool: workers retire on their own; no leak of threads.

NEEDS THE USER: none.

FILES (all in C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\0bab04ae-e7a9-44cb-b4da-96d849a107f6\scratchpad\r12-dotnet\):
- probe.patch: the framework counters (FrameLog snapshot, R12UiaCount, UIA thunk/P/Invoke counts, bytes inside raises), now reverse-applied.
- host\: src\Program.cs (the probe host), Host.props, pub.ps1, batch.ps1 (desktop lock and guards), sum.py, holds.log, runs\*.out.txt, runs\*.framelog.txt, runs\launches.jsonl, publish-*.log.
- wordcache\: Program.cs, WordCacheProbe.csproj, aot\.
- fx\jrepo: a copy of round 11's fixture repo.
