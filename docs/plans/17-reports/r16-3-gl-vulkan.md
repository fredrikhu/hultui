SATISFIED: no

**Method.** Worktree detached at 440ba012 (SDK 11.0.100). Real windows used expert 8's scripted AOT hosts (`aotx` = 440ba012, `aotx-base` = 75f2288a) and my own publishes of the same host source:
- the bisect builds: k99db = 99db58cf (SkiaSharp 3.119.4), k043f = 043f5b38 (SkiaSharp 4), k26df = 26df5a87 (fd0cccb7's parent), kfd0c = fd0cccb7 (SDL 3.5.0);
- kfade, a probe: 440ba012 plus a Dialog.cs fade change, reverted by reverse edit.

My copy of expert 8's driver also samples, at every mark and from outside the process: committed and resident bytes by class (private RW, private write-combined, mapped, image), new private allocations of 1 MB or more, and newly loaded modules. The host's `gpu` step reads DXGI QueryVideoMemoryInfo. Each launch also records the NVIDIA GL shader-cache files written while it ran. All numbers are bytes and counts.

REVIEW: none. Nothing in round 15's build or main's 41 commits regresses GL or Vulkan.
- **fd0cccb7 (SDL 3.5.0), against its parent, 2 launches a side, ABBA:**
  - rest (8 s, bounded by the host's own marks): 9.8 / 10.5 against 11.0 / 10.5 wakes a second. All of them are on NVIDIA's GL worker thread; the UI thread woke 0 times. Machine timer 1 ms (Steam).
  - 21-step width drag: 0.95–1.00 frames a step and 52.2 KB UI a step on both.
  - SDL_CreateWindow 187–203 ms on both; memory equal.
- **c5bdaa3d (Moved/DisplayChanged):** two flags set by SDL events and checked once a pump. No poll, no per-frame query, and no app subscribes.
- **8bf15a75, 45ec1a6c (placement):** run only on demand; the apps read Placement at open and close only.
- **5af9df10, c39c9478 (software renderer):** the GL path is unchanged apart from a three-way `ActiveSurface` null check.
- **043f5b38 (SkiaSharp 4), GPU side:** flush median 0.287–0.310 ms on all six builds; Vesper's DXGI local is 39.14 MB at every step on every build.
- **1542fcb8:** one empty `ConcurrentQueue.TryDequeue` a frame, 0 B.
- **The screen-reader fix:** 440ba012 with `HULTUI_A11Y=0` ends 110.4 / 122.6 MB, against 111.7–112.7 / 122.1–124.0 with it on. Not the cause of the jump.

**apps-gl-native-commit-jump: closed.** The cause is NVIDIA's GL shader compiler on a disk-cache miss, not a commit.
- **Repeat:** expert 8's Vesper scene with `gpu` at every phase, 2 launches a side (ABBA). No jump on either side. End of scene 111.7–111.9 / 122.1–123.8 MB working set / private on both, which is expert 8's base number. DXGI local 39.14 MB throughout; nonlocal 16.0 → 28.5 MB on both.
- **Bisect:** one launch each on 99db58cf, 043f5b38, 26df5a87, fd0cccb7 and 440ba012 with `HULTUI_A11Y=0`. No jump on any. End of scene 110.4–112.7 / 122.4–124.0 MB. No shader-cache file written. Nothing is left to bisect.
- **Timing evidence:** expert 8's jumps fall at the same second as NVIDIA cache writes in `%LOCALAPPDATA%\NVIDIA\GLCache`:
  - VesperHost's file `5f15bf507c70781b.bin` was rewritten at 01:21:44, which is h3-vesper-aotx's first wheel notch (MARK 1 at 01:21:31, plus 13.4 s);
  - Meridian's `2b58be56cc82402f` was written at 01:23:23, 1.2 s into h3-meridian-aotx.
  - Expert 8's samples show no new threads, handles or modules at any jump.
- **Cold cache test:** `__GL_SHADER_DISK_CACHE_PATH=<empty folder>` works on Windows (`__GL_SHADER_DISK_CACHE=0` does not).

  | Vesper, 1.2 s after the first frame | Working set | Private | Private RW, resident | First frame after `application ready` |
  |---|---|---|---|---|
  | 440ba012, empty cache | 149.4 MB | 157.4 MB | 93.6 MB (7 more allocations ≥ 1 MB) | +371.6 ms |
  | 75f2288a, empty cache | 149.4 MB | 158.0 MB | 93.6 MB | +124.6 ms |
  | 440ba012, the same folder again | 90.6 MB | 103.9 MB | 40.9 MB | +56 ms |

  - The cold run keeps it: 151.1 / 159.0 MB at 5.5 s.
  - The first-frame times are one launch each, for the build phase to confirm.
- **How the cache is keyed:** by exe name. Both builds' VesperHost.exe wrote `5f15bf507c70781b` into their private folders, and my four new exes at new paths compiled nothing. So the base already had m119's programs from earlier rounds, and m156's wheel, History and collection programs were new on this machine. This agrees with expert 2's GL hook (same programs at the same frames, first launches only).
- **Real app:** once per program per machine, after a driver or Skia update: +53–59 MB working set kept for that process, plus the compile. Not HultUI's; SkiaSharp 4 has no program cache (plan 18).
- **The 37–55 MB first palette open** is the dialog fade's full-window layer (candidate 1).

CANDIDATES:
- id: gl-dialog-fade-window-layer (owner 3 with 2)
  - what: `Dialog.Show` fades in by tweening the full-window scrim's `Opacity` (Dialog.cs:98–100). ElementRenderer then pushes a window-sized SaveLayer, and Skia keeps that texture in its cache for the rest of the session (the GL surface never purges).
  - evidence: first Ctrl+P, 440ba012 against kfade. kfade fades the panel as a group (a layer the panel's size) and tweens the scrim's colour from alpha 0 (`Animate.Background`).

    | Case | DXGI local, first open | Private, first open | End of scene |
    |---|---|---|---|
    | Junction GL 2160×1350, 2 launches a side | +18.0 → +8.0 MB | +40.7 / +41.8 → +14.1 / +13.7 MB | local 64.6 → 54.6 MB (−15 %); private 167.4 / 170.6 → 142.0 / 142.8 MB (−15 %) |
    | Junction GL, window first sized to 3800×2100 | +32.0 → +8.0 MB (139.5 → 115.5, −17 %) | +64.7 → +1.6 MB (248.3 → 184.1, −26 %) | |
    | Meridian GL | +18.0 → +4.0 MB (63.8 → 49.8, −22 %) | +39.5 → +5.1 MB (147.4 → 113.0, −23 %) | |
    | Junction Vulkan | +19.0 → +5.0 MB (88.1 → 74.1, −16 %) | +19.2 → +6.8 MB | |

    - On GL the private rise is NVIDIA's backing for the texture (+19.4 MB RW and +22.5 MB write-combined commit, only +0.7 and +3.0 MB resident). It grows inside existing reservations: no new allocation of 1 MB or more.
    - Working set changes by only ±0.2 MB.
    - A second open reuses the texture (+0 local). On every build it adds +4 MB nonlocal and write-combined memory.
  - real app: every session that opens a palette or dialog (Junction and Meridian Ctrl+P, Vesper's and Junction's dialogs); the first open, then for the session. A maximized 4K window is the 3800×2100 row.
  - expected: GPU memory (retained-memory bar) 15–22 % lower; process commit 15–26 % lower.
  - cost: small (about 6 lines in Dialog.cs, no API), but pixels change during the 120 ms fade (NEEDS THE USER). An exact-pixel version is medium: the renderer would split the layer when an element with opacity paints only a uniform fill and its children's paint bounds are smaller (the fill at α outside, a children-sized layer inside).
  - expert 2's option (d), purging Skia's scratch textures when the tween ends:
    - **GL: not sound as the fix.** NVIDIA keeps freed textures in its pool (docs/design/HDR.md: Skia freed 211 MB, DXGI 303.7 → 312.0 MB; round 2: −11 MB DXGI of 47 MB purgeable). The commit measured here sits in the driver's own reservations, which a Skia purge never reaches.
    - **Vulkan: sound and cheap.** `PurgeScratch` already exists, and round 2 measured Vulkan purges returning DXGI memory. It would free about 19 MB (expected, not measured).
    - The layer-free fade avoids the texture on both, so I prefer it.
- id: gl-tween-burst-nonlocal-growth (lead, owner 3)
  - what: on GL, each burst of tween frames grows NVIDIA's resident, write-combined, GPU-visible system memory.
  - evidence: scrollbar hover visits 1, 2, then 3–5 (9 frames each):
    - Vesper: working set +4.5, +6.1, +4.0 MB (+14.7 in all; +10.6 on the docs pane); DXGI nonlocal 16.0 → 19.5 → 24.5 → 28.5 MB.
    - Junction History: +2.0, +4.0, +4.0 MB; nonlocal 20.4 → 29.4 MB.
    - All of it is resident write-combined private memory.
    - Vulkan: +0.9 MB working set, nonlocal +0.
    - Equal on every build (base and new both +7.1 MB at the scrollbar visits in hold 1).
    - In the full scene, navigation, clicks and scrollbar visits together add +19.4 MB against +14.7 MB for the visits alone, which hints at a pool with a ceiling. Not shown.
  - real app: Vesper and Junction, +10–15 % working set after a few scrollbar hovers.
  - expected: the leak bar if it keeps growing over 50 visits; if it is bounded, it is the driver's pool (information, next to the threaded-optimization item).
  - cost: unknown. Next step: 50 visits in a real window; all 4 of my holds are used.

REAL WINDOW:
- **Setup:** the 4K (DISPLAY2, primary), DPI 144 (150 %), 120 Hz, HDR on (SDR white 240 nits). Drawables: Vesper 1860×1230 px, Junction and Meridian 2160×1350 px. OS cursor outside the window. Machine timer 1/0.5/15.625 ms in every hold (Steam).
- **Safety:** 4 holds of 112.0 / 147.2 / 109.0 / 50.1 s, after waits of 0 / 0 / 233.1 / 281.0 s. 33 launches, 0 foreground takes, 0 nvlddmkm events, no stall over 1 s, the screen never read. The idle gate failed in holds 1–3 (my own publish at 68 % CPU, then 10 % CPU, then a testhost) and passed in hold 4.
- **Only a real window showed:** the compile cost, the dialog layer's driver commit, the tween-burst growth, and NVIDIA's cache keying by exe name. Headless rasterizes on the CPU and shows none of them.

WAITING, RE-RANKED:
- **gl-threaded-optimization-profile:** information only, unchanged. NVIDIA's GL worker is the only thread that wakes at rest (9.8–11.0 a second). The tween-burst growth may be that worker's buffers (unmeasured).
- **vk-instance-extensions-once:** keep low. SdlVulkanHost is untouched in 75f2288a..440ba012; still 7.1–7.4 ms, under the 10 ms bar.
- **f16-held-drag-post-stall:** not run; it needs the user's approval. No Vulkan host or surface change in the range; SDL 3.5.0's event pump is untested on that path.
- **Suite (a):** keep low (Vulkan rests 111.5–112.4 wakes a second on both sides, expert 8).
- **Suite (f):** keep low (Aperture's DXGI memory 69.0 → 66.5 MB on both sides, expert 8).
- **Expert 8's proposal (i):** worth it only together with gl-dialog-fade-window-layer. Guard DXGI local after the first `Dialog.Show` on a 2160×1350 GL window (+18.0 MB today, +8.0 with the probe), not process commit, which is the driver's and noisier. Medium-low.

EXAMINED, NOT WORTH IT:
- **The native commit jump:** NVIDIA's compiler, once per program per driver or Skia update (above).
- **The remaining bisect builds:** not needed; every build is equal with a warm cache.
- **A Skia purge after a tween on GL:** the driver keeps the memory (above).
- **`__GL_SHADER_DISK_CACHE=0`:** ignored on Windows; `__GL_SHADER_DISK_CACHE_PATH` works.

NEEDS THE USER:
- **gl-dialog-fade-window-layer pixels:** with the simple fix, an opaque panel's inside is darker during the fade by (1−α)·α·(scrim alpha)·background. That is at most 11.8 % at α = 0.5 with the default 120/255 scrim (up to 30 of 255 over white), and identical at α = 0 and 1, for about 14 frames of the 120 ms fade. Choose: accept that, take the exact-pixel renderer split (medium), or keep today's layer. Before/after images can be made with a headless capture 60 ms into the fade on both builds.

FILES (all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\b6ef64d9-4ed1-4793-9955-6a986d74de79\scratchpad\r16-gl-vulkan\`):
- **Harness:** `rw\Mem.cs` (the memory sampler), `rw\Driver.cs` (expert 8's driver, which calls it), `rw\Driver13.cs`, `rw\TimerRes.cs`, `rw\common.ps1`, `rw\hold.ps1`, `rw\batch1.ps1`–`rw\batch4.ps1`.
- **Analysis:** `rw\sum.py`, `rw\rest.py`, `rw\threads8.py`, `rw\newthreads.py`.
- **Data:** `rw\runs\g1..g4.jsonl`, `*.frames.csv`, `*.startup.txt`, `g*-windows.log`, `locks.log`, `gate.log`, `glcache\` (the private shader caches).
- **Publishes:** `start\pub.ps1`, `start\src\` (expert 8's host source), `pub\k99db`, `pub\k043f`, `pub\k26df`, `pub\kfd0c` (Vesper), `pub\kfade` (Junction and Meridian).
- **Fixtures:** `fx\jrepo`, `fx\coll25`.
- **State:** the probe patch was reverted by reverse edit; the worktree is clean at 440ba012; build servers are shut down; nothing of mine is running.
- **For PERFORMANCE.md:** a GL jump of about 40–55 MB with one long flush is NVIDIA's compiler on a disk-cache miss. Look for a `GLCache` file written in that second, and reproduce it with `__GL_SHADER_DISK_CACHE_PATH=<empty folder>`. The cache is keyed by exe name.
