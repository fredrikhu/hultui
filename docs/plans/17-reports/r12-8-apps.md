SATISFIED: no

**Method.**
- **Shared publishes:** the five apps were published on NativeAOT at f9c44628 into `r12-apps\aot\<App>\<App>Host.exe`, and READY.txt was written first. All five published with exit 0 and passed the smoke test.
- **Extended build:** `r12-apps\aotx\` holds the same hosts plus in-process scripted input (UnsafeAccessor `HandleInputEvent`, `BenchStep`, `NavigateTo`), SetWindowPos resizes, and a per-frame log of PerformanceCounters and UI/all-thread bytes. It replaces the `claude/render-probe` branch.
- **Headless counterpart:** the same scenes ran headless with the same instrument (walk host `R12.cs`).
- **Same-day walks:** 68 AOT walks per side, round 10's release cc55bdcc against f9c44628, plus a 2-point AOT bisect.
- **Driver:** it counts context switches per thread from NtQuerySystemInformation, named by thread description or start module. It also counts processes started in a job object (git runs), I/O operations and TCP connections per pid.

REVIEW: none. Round 11 helped the real apps; nothing regressed.
- **Same day, AOT, 67 walks:** geometric mean after/base 0.964; UI-thread KB summed −1.0 %.
- **6fb8ff07 (Meridian keeps 4 bodies):**
  - Tab switch HTML+JSON, all threads: 29,289 → 213 KB a click (−99.3 %); to HTML 52,281 → 249. GCs 36/7/2 → 0/0/0.
  - Pretty/raw toggle, all threads: HTML 27,373 → 89 KB, JSON 5,933 → 40, XML 5,994 → 78. GCs 32/5/0 → 0.
  - UI thread −35…−68 %; builds a toggle 2.5–3 → 1.
- **1879ba78 (map list draws the selection):** click at 600 endpoints 23.3 → 14.8 KB (−36.5 %); layout visits 70.3 → 20.3; measure misses 70.4 → 37.0.
- **a72ed576 (the only layout commit):** measure misses fell on 14 walks while bytes stayed flat.
  - Meridian History Down 67.6 → 45.6 a key; Runner at 200 requests 14,440 → 10,656 a run.
  - Junction diff find 96.1 → 76.1; Files Down 149.1 → 140.1.
  - Vesper partner 15.9 → 12.0; insert 18.4 → 14.5.
- **Moves that are not regressions:**
  - **7d823b34, WordCache fill timing.** Junction review-comment typing 3.114 → 3.399 KB a key (+9.2 %), review Enter +8.3 %, Meridian find-Enter 9.37 → 9.84 (+5 %).
    - The bisect pins it on that commit: c99e23bf reads 3.114, 7d823b34 reads 3.399.
    - It is the table doubling inside the measured window: about 23 KB once per pipeline. Other walks moved down (summary typing −2.2 %, URL typing −3.4 %), and the commit's own fill cost is 166.6 → 104.0 B a word.
  - **Vesper thumbnails +13.9 %:** decode-landing timing. Re-runs read 5.14–5.35 KB on both sides.
  - **Vesper transfer-frames +30 %:** a timing walk.

CANDIDATES:
- id: apps-vk-idle-f16-driver-wakes (owner 3)
  what: an F16 (scRGB) swapchain at rest keeps an NVIDIA DXGI-interop thread waking about 96 times a second. An 8-bit swapchain while no HDR picture is on screen would remove it.
  evidence:
  - Real AOT windows, 55 s at rest, guard thread excluded:
    - Aperture and Reel: 207 wakes/s, from an nvoglv64.dll thread (~111/s) and an nvwgf2umx.dll thread (~96/s).
    - GL apps: one nvoglv64 thread at ~10.5/s.
    - UI thread: 8–32 wakes per 55 s in every app; 0 frames.
  - Reel variants, 12 s each:
    - default (F16, MAILBOX): 209/s;
    - `HULTUI_VULKAN_F16=0`: 112/s, and the nvwgf2umx thread is gone;
    - FIFO: 244/s;
    - `HULTUI_DISPLAY_INFORMATION=0`: 225/s (+16/s of UI-thread polling, so the events are right);
    - GL: 10.2/s.
  real app: Aperture browsing its library or Reel paused, on an HDR display. That is every second they sit open, which is most of their life.
  expected:
  - Rest wakes 209 → about 112/s (−46 %), the same class as round 3's runtime-idle-wakeups.
  - Swapchain GPU memory 3 × 1440×900 × 8 B = 31.1 MB → 15.6 MB (retained GPU memory bar).
  cost: medium. The swapchain is rebuilt when HDR content appears or goes; pixel and behaviour impact, see NEEDS THE USER.
- id: apps-junction-diff-uia-bytes (owner 7, lead)
  what: in a real window the Windows accessibility bridge costs about 40 % of the UI thread's bytes in Junction's commit file walk; the headless a11y walks do not show it.
  evidence:
  - bigrepo fixture (40 files of 8,000 lines), held Down/Up at a real 33 ms pace:
    - real window: UI thread 977 KB a key; "other" (no a11y counter) 16–17 % of frame time;
    - same scene with `HULTUI_A11Y=0`: 586 KB a key and "other" 1.2 %;
    - headless, same instrument: 586 KB.
  - First frame of a key: 1,020,088 → 622,888 B.
  - History Down and Vesper Down show "other" at only 1.7–2.0 %, so it is the diff or document view.
  - The listening UIA client is unknown; something on this desktop listens.
  real app: Junction, stepping through a commit's files, which reviewers do constantly.
  expected: memory churn −40 % per key if the bridge's per-document work can be cut. Split it with a probe build first.
  cost: unknown; no API impact expected.
- id: apps-junction-diff-second-frame (owner 3 with 6, lead)
  what: in the real window each key in the commit file walk draws a second frame about 3.5 ms after the first, with 0 B and no style or layout. Headless draws 1 frame a key.
  evidence:
  - 2.00 frames a key with a11y on or off; History Down is 1.07, Vesper Down 1.02.
  - The second frame costs render 0.34–0.42 ms + flush 0.26–0.39 ms + other 0.3–0.4 ms with a11y on. That is about 43 % of the key's UI frame time with a11y on, about 45 % with it off.
  real app: as above, at every key in the commit file walk.
  expected: if the frame is not needed, about −40 % frame time a key (time bar); cause not found yet.
  cost: unknown.

REAL WINDOW (1440p, 100 %, 200.01 Hz, HDR on, SDR white 240 nits; RTX 4080, driver 610.88):
- **Holds:** 4 holds of 103 / 148 / 99 / 18 s, waits 0 / 735 / 351 / 283 s; 33 launches.
- **Safety:** 0 foreground takes (and none handed back), 0 activations refused, 0 nvlddmkm events, 0 screen reads. One Jellyfin sign-in ("aperture-r12" device).
- **Start-up shares, 2 launches each, no trace (same as round 11):**
  - GL apps: pre-SDL 14.6–16.8 %, SDL_CreateWindow 56.3–63.5 %, first frame 17.2–26.8 %.
  - Vulkan apps: pre-SDL 10.8–11.1 %, CreateWindow 23.3–24.5 %, instance and device 24.9–26.6 %, first frame 32.2–33.0 %.
- **Memory:**
  - Working set at +1.5 s: Junction 109, Vesper 90, Meridian 85, Aperture 117, Reel 114 MB.
  - From +5 s to +60 s at rest: Junction +2.0 MB, the others ±0.2 MB.
  - Managed heap 13.5 / 2.1 / 0.7 / 0.9 / 0.7 MB; 0 GCs through start-up and the minute; ≤10 KB allocated at rest (the probe's own line).
- **What the apps do unprompted at rest:**
  - 0 file reads; the 1 write and 13 other I/O ops in 55 s are the probe's own.
  - No timers or watchers fire on the UI thread.
  - Aperture signed in keeps 12 TCP connections open (0 new) and a 6.2 MB heap.
- **Frames at rest:** 0, except one repaint (24 B, no layout) when another window opens over the app.
- **Real vs headless, same scenes, same instrument:**
  - Junction commit file walk: render 48–50 %, flush 24–25 %, present 6 %, other 16–17 % (headless: render 96 %).
  - History Down: render 31 %, flush 62 %, 27 KB a key in both.
  - Vesper Down: render 44 %, flush 40 %, present 15 %.
  - Resize (10 SetWindowPos, 1440↔1200): GL flush 82–83 %, Vulkan present 59–67 %; 1.5–2.5 frames a resize against 1 headless.
  - UI KB a resize, real vs headless: Junction 527 vs 301, Vesper 46 vs 26, Meridian 73 vs 72, Aperture 8.7 vs 1.9, Reel 7.8 vs 1.9.
- **Trap:** `HULTUI_TRACE` adds 55–59 MB of working set to the GL apps and 20 MB to Reel (Junction 109 → 168 MB). Never read memory from a traced run.

WAITING, RE-RANKED:
- **junction-held-key-bigfiles: close, not a candidate.**
  - In real time at 33 ms: all threads 948–1,149 KB a key, git 0.10 a key (4 runs in 40 keys). Round 11's 2,367–3,110 KB came from the headless clock.
  - At a 100 ms tapping pace each new file is tokenized once: about 14.5 MB on the worker (~14,400 lines); revisits are cached at 948 KB. This is by design (HighlightRest 120 ms).
- **vesper-folder-nudge-stale: close.**
  - Real window, 30 navigations at 100 ms: 0 of 71 frames fall in the nudge's phase; every frame lands 0–39 ms after a navigation.
  - 2.20 frames a navigation at 33 ms against 2.37 at 100 ms.
  - Only a listing taking 70–370 ms (a slow share) could pay the stale build: about 30 of 345 KB a navigation.
- **junction-review-old-path:** still reproduces (GitDiffLine.cs:31, :44); round 11 did not touch apps/Junction/Git. Not perf.
- **aperture-poster-as-hero:** code unchanged (ApertureApp.Cards.cs:538-539); not exercised.
- **aperture-tmdb-size-fallback:** code unchanged (RequestModels.cs:419); not exercised.
- **junction-narrow-gitlab-glyphs:** untouched by round 11; not exercised.
- **meridian-pane-choice-not-persisted:** still reproduces (MeridianApp.cs:126; flipped at :318, never saved).
- **meridian-min-width-wraps:** not re-shot (no captures allowed). Round 11 did not touch the tab strip or the Send bar, so round 10's shots stand.
- **junction-chromeparttests-load:** CI green on cc55bdcc and b0dd1954; keep watching.

EXAMINED, NOT WORTH IT:
- **WordCache fill (7d823b34):** +0.28–0.47 KB a key on 3 walks; it is fill timing once per pipeline (see REVIEW).
- **The repaint when another window opens over an app:** 1 frame, 24 B, no layout.
- **Proposed suite scenarios (rows the suite lacks):**
  - an idle Vulkan window counting every thread's wakes, not only the UI loop's;
  - frames per key in a document-view walk under a real loop;
  - an app-shaped shell resize (sidebar, list and diff, 1440↔1200).

NEEDS THE USER:
- **apps-vk-idle-f16-driver-wakes:** may Aperture and Reel draw their chrome through an 8-bit swapchain while no HDR picture is on screen, rebuilding to F16 when HDR video appears?
  - Cost: one swapchain rebuild at each switch, and possibly a visible step in SDR brightness on an HDR display.
  - Before/after images: in-process frame reads (`HULTUI_VULKAN_CAPTURE`) with `HULTUI_VULKAN_F16=0` and `=1`, or the user looks at both.
- **Jellyfin:** 1 new device ("aperture-r12"); the earlier devices and the API key are still to be removed and revoked.

FILES: in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\0bab04ae-e7a9-44cb-b4da-96d849a107f6\scratchpad\r12-apps\`
- **Publishes:**
  - `aot\` (shared, READY.txt) and `aotx\` (scripted);
  - sources in `start\` and `start-x\` (`src\Program.cs`, `src\Script.cs`, `Start.props`, `pub.ps1`).
- **Real-window probes, all in `rest\`:**
  - drivers: `Driver.cs`, `batch.ps1` (h1), `batch2.ps1` (h2, h4), `batch3.ps1` (h3);
  - analysis: `rest.py`, `frames.py`, `marks.py`, `variants.py`, `startshares12.py`;
  - data: `runs\h1..h4.jsonl` and `runs\*.frames.csv`.
- **Lock log:** `locks.log`.
- **Walks:**
  - `walks.ps1`, `walks2.ps1`, `run.ps1`, `tree.ps1`, `aotpub.ps1`, `bisect.ps1`;
  - analysis: `mkeffect.py`, `cmp10.py`, `allkb.py`;
  - data: `runs\walks.jsonl`, `effect.txt`, `runs\bisect.jsonl`, `runs\thumbs.jsonl`;
  - headless scenes: `walk\src\R12.cs`, `runs\r12-*.frames.csv`.
- **Trees, hosts and fixtures:**
  - trees `tree-r10rel` (cc55bdcc), `tree-now`, `tree-xc99e23bf`, `tree-x7d823b34`;
  - walk hosts `walk\aot-*`;
  - fixtures `fx\` (jrepo copy, bigrepo from `mkbig.py`, the rest of r11u's fx) and `www\`.
- **State left:** the fixture server is stopped, build servers are shut down, and the worktree is clean at f9c44628.
