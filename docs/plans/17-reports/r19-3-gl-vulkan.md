# Round 19 survey, expert 3: GL and Vulkan

SATISFIED: no

**Method.** Worktree detached at 56d9a3f6 (SDK 11.0.100). My own NativeAOT host is r18b8's scripted host with Aperture
over a fake library: 300 films, and posters served as JPEGs at the size the URL asks for (expert 2's fixture). It adds
three steps:

- `skia`: Skia's cache, DXGI local/non-local, private and working set.
- `redraw`: a forced frame.
- `purge`: a forced frame, then `PurgeUnusedResources(ms since just before that frame)`, which keeps what the last frame
  drew with. A dry-run form does the same frame without the purge.

Reel and Junction ran on expert 8's shared publishes, with stderr captured for `HULTUI_VULKAN_ACQUIRE_TRACE`. The
minimize/restore case ran on the Vulkan probe exe, with its window's cover sampled every 20 ms. Results are counts and
bytes only.

REVIEW: none.

- **49fd9b1e (two images):**
  - Reel playing on Vulkan at 200 Hz: `images=2`, acquire wait 0.0030–0.0040 ms a frame in 6 reports of 120 frames over
    2 launches. That is no waits.
  - DXGI local 244.58 MB, the same as round 18's figure after the change.
  - Aperture builds `x2`. Under MAILBOX the restore and a `SetWindowPos` resize rebuild with x2. FIFO keeps x3.
- **d335159e (pixel frame uploaded once per version):** no effect on Reel or Aperture here.
  - Their video takes the GPU-source path: Vulkan interop ("interop image 320x180 RGBA16F"), and GL GpuFrameSink.
    Posters are `SkiaImageSource`. Neither path changed.
  - The new path (SkiaRenderContext.cs:1853–1908) applies only without interop, and to WebView/CEF frames.
  - It keeps one texture per pixel source, and lets it go the first frame the source is not drawn. That is the same
    memory as the old per-frame upload, whose texture went to scratch.
  - `EndFrame` iterates the dictionary and removes entries during the loop (allowed in .NET Core 3+) and allocates
    nothing.
- **1951019d (light/dark):** nothing GPU-side. It sets a static flag in the event pump and raises an event; no app
  subscribes, and HultUI never changes the theme itself.
- **1ddec166 / 0ec5182c:** round 18 measured DXGI local −9 to −30 %. The ghost's small layer stays in Skia's scratch
  pool (~0.16 MB) until the next scratch purge. No objection.

CANDIDATES:

- id: gl-vulkan-rest-purge (promoted from Waiting skia-rest-purge-vulkan)
  - what: once a Vulkan window has drawn no frame for a rest (a loop deadline like `SizeRestPurgeAt`, one wake per
    rest), purge what the last frame did not draw with. The cutoff is `SizeRestPurge.TakeDue`'s: never after the last
    frame began. GL is excluded (below).
  - evidence (real window, NativeAOT, Aperture fake library; walk the Films grid with 40 Downs, Back to Home, 4 s rest
    with 0 frames):
    - Before the purge, both sides: Skia cache 657 resources / 59.85 MB; DXGI local 91.30 MB, non-local 20.36 (vs 43.30
      / 19.36 at start); private 224.4 MB.
    - **The purge:** 657 / 59.85 → 68 / 8.93 MB. DXGI local 91.30 → 47.30 MB (−44.0, −48 %); non-local 20.36 → 13.86.
      Private 225.4 → 180.5 MB at the end (−44.9, −20 %), 2–4 s behind the purge. The dry run stayed at 91.30 / 225.4.
    - **A forced redraw after it** re-uploads nothing: the cache is unchanged, 1 frame.
    - **Forward to the grid** (cursor 280, where it was left): one frame re-uploads the posters on screen, +64 resources
      / +6.69 MB, with non-local +6 MB of staging. Frames (1) and UI bytes (294.8 KB) are equal to the dry run.
    - Posters were 322 at 168×252 px, so about 169 KB of texture each.
  - real app: Aperture (Vulkan) left at rest after browsing, which is normal for a media app.
    - r18b6 on the real server, back at Home after 20 item pages: 65 MB of textures of live pictures, 60 MB of them off
      screen, plus 12 MB of staging, in 327 MB private. That puts ~70 MB within reach (~−21 % private).
    - It scales O(pictures shown) up to Skia's 256 MB budget, and 2.25× larger per poster at 150 %.
  - expected: the retained-memory bar (≥5 % GPU memory or private): −48 % DXGI local and −20 % private measured here.
  - cost: small; no API.
    - Pixels should be unchanged, because only textures go and the decoded pictures stay. The build should check frames
      after the purge byte for byte, and time the re-upload frame.
    - Choose the rest long enough that reading a page for a few seconds does not pay a re-upload on Back. Suggested
      ≥10 s; the build decides.
  - GL is not worth it: the same purge takes the cache 57.78 → 12.54 MB, but DXGI local stays at 82.5 (80.0 at the end)
    and private moves 272.8 → 270.6 (NVIDIA's GL driver keeps the memory).

REAL WINDOW:

- **Desk:** only the 1440p, DISPLAY1 2560×1440 at 100 %, 200 Hz, HDR on (SDR white 240, peak 409), primary. The 4K was
  not attached at 09:06 or 09:10. Timer 1/0.5/15.625 ms (Steam). The user's cursor rested inside every window.
- **Holds:** 2 holds, 15 launches, 0 foreground takes, 0 nvlddmkm events, no stall, the screen never read.
  - h1: 125.1 s, 0 s wait; idle gate failed (26 % mean CPU, others' builds).
  - h2: 78.4 s after waiting 160.9 s; gate passed.
  - My occluder window was never the foreground.
- **Aperture idle at Home (3.9 s):**
  - App threads ~0.6 wakes a second. NVIDIA's threads ~111 a second on Vulkan, ~10 on GL. Probe threads excluded.
  - Aperture's first frame: Vulkan device set-up is one ~120 ms phase. The Vulkan preload was examined earlier (−5.2 ms,
    below the bar), so I am not re-raising it.
- **Reel playing bands.y4m:**
  - 210–212 frames in 5 s on Vulkan and GL; 166–168 B a frame on the UI thread.
  - Paused 4 s: 0 frames. App threads ~1.5 wakes a second (demux), NVIDIA ~112 a second on Vulkan, plus an `nvcuda64`
    thread ~10 a second on Vulkan and GL alike.
  - While playing, GL's driver thread woke 2,982 times in 5 s against ~1,160 for Vulkan's. Reel ships Vulkan, so this
    is information only.
  - DXGI local: Vulkan 244.58 MB, GL 231.5.
- **Found, outside my area (for expert 8, Junction):** starting Junction (GL, shared publish) on the shared fixture
  `...41943a9b...\r14-gl-vulkan\jrepo` (copied):
  - The fixture's working tree now has 3,049 of its 3,355 tracked files deleted: 2,459,107 deleted lines, an 82.2 MB
    patch.
  - Junction allocated 681 MB by 1.5 s after the first frame and 1,025 MB by 3.5 s (GC 56/55/8).
  - At rest it kept a 640 MB heap: committed 810 MB, working set 907 MB, private 918 MB.
  - Likely cause: `ReadChanges` parses the whole unstaged working diff (`GetWorkingDiff`, JunctionApp.Changes.cs), and
    the `full` preload calls it at start. So the cost is O(changed lines), parsed up front and kept.
  - Real case: a large deletion, rename or line-ending change before a commit.
  - Anyone measuring Junction on that jrepo now measures this.

WAITING, RE-RANKED:

- **skia-rest-purge-vulkan:** promoted to gl-vulkan-rest-purge, with the numbers above. Vulkan only.
- **vk-fifo-probe-tests-200hz:** close; I could not reproduce it, and it is not a code fault.
  - Six FIFO minimize/restore runs on the same monitor at 200 Hz with HDR on all passed: 32 frames in the second after
    the restore, 61 passes a second, 15 frames after the resize.
  - They covered three conditions: the probe's window uncovered (2 runs), raised to the top without activation (1), and
    fully covered by an opaque window of ours, 1.00 cover (2). MAILBOX covered and uncovered: 30–32 frames.
  - So the occlusion and activation guesses are refuted.
  - Round 18's failing log shows the desk was slow from the start: the gap from the first swapchain to the first SKIPPED
    was 2.2 s (12 forced frames before the minimize), against 47–112 ms today.
  - Suggestion (tests only): have the test print that pre-minimize time, so a slow desk is named and not read as a FIFO
    failure.
- **vk-instance-extensions-once:** unchanged (SdlVulkanHost.cs:532); 7.1–7.4 ms, below the bar. It can ride with the
  rest-purge build only if that build touches Init, which it does not need to.
- **gl-threaded-optimization-profile:** information only. GL code moved by one line (d335159e's `SetGpuContext`); not
  measured again.
- **f16-held-drag-post-stall:** not run (not allowed). The 4K was not attached.
- **f16-two-images-hdr-run:** still owed to a build step. The 4K was not attached at either hold; not run.
- **Suite (a)** (idle Vulkan wakes guard): low. Today's count is ~111 NVIDIA wakes a second against the app's ~0.6.
- **Suite (f):** close. `VulkanProbeTests.AfterSizeChangesRest_TheWindowKeepsOneStencil` already guards 4fdb56cb's
  mechanism: one stencil kept, and the frame after the purge equal to the one before. A DXGI-local count would add
  nothing.

EXAMINED, NOT WORTH IT:

- **Rest purge on GL:** returns no GPU memory (DXGI local 82.5 → 80.0, private −2.2 MB), though Skia's cache drops 45 MB.
- **d335159e's texture per source:** same memory as before. Reel and Aperture on NVIDIA never take that path.
- **Reel's per-frame UI bytes in playback:** 0.17 KB a frame, near the 0.1 KB target. No single source found in the
  counts.

NEEDS THE USER:

- none.

FILES (in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\e91920b9-3793-496a-9a73-f017250d8772\scratchpad\r19-gl-vulkan\`):

- **Host:** `start\src\Script.cs` (adds `skia`, `redraw`, `purge:do|dry` and a frame counter), `start\src\FakeFixture.cs`
  (sized posters), `start\src\Program.cs`, `start\Start.props`, `start\pub.ps1`. The publish is
  `aot\Aperture\ApertureHost.exe`.
- **Driver:**
  - `rw\common19.ps1`: lock `Global\HultUI-r19-desktop`, gate before the lock, desk logged, `Scene`, `SceneR` (stderr
    captured), `Probe`.
  - `rw\Occl.cs`: window cover sampler, raise, and an occluder of our own.
  - `rw\Driver*.cs`, `rw\TimerRes.cs`, `rw\Gate.cs`.
  - `rw\hold1.ps1`, `rw\hold2.ps1`; `rw\an19.py` (`python -I an19.py <runs> <tag> [markA markB]`).
- **Data:** `rw\runs\h1-*`, `rw\runs\h2-*` (`.out.txt`, `.frames.csv`, `.startup.txt`, `.err.txt`, `.cover.txt`,
  `h1.jsonl` / `h2.jsonl`), `rw\h1.log`, `rw\h2.log`, `locks.log`, `gate.log`.
- **State:** the worktree is clean at 56d9a3f6. The Vulkan probe was built in Release (bin only). Build servers are shut
  down. Nothing of mine is running. The jrepo copy and temp config folders are deleted.
- **For PERFORMANCE.md:**
  - On NVIDIA Vulkan a purge's private bytes fall 2–4 s later.
  - On NVIDIA GL, Skia purges do not return DXGI memory.
  - Check a shared Junction fixture's `git status` before using it.
