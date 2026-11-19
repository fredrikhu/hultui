SATISFIED: no

**Method.** Shared publishes: the five apps on NativeAOT at 440ba012 in `r16-apps\aot\` (all exit 0, 0 IL warnings). READY.txt was written before any hold, and the smoke test passed in hold h1. Scripted hosts (round 15's start-x plus a `clickid` step, a Meridian collection open and a lock around output appends) were built at three commits: `aotx` = 440ba012, `aotx-base` = 75f2288a (round 15's release, my own worktree), and `r15pre` = round 15's own `aotx` (5a212f3e, run read-only) for checking round 15's build. Headless: 48 AOT walks on base and new, plus a new real-time bigrepo probe (`bisect\`). All bytes and counts. The idle gate failed in h2–h4 (testhost, 14 % CPU).

REVIEW:
- **c32c4b2e (round 15 step 5 (b)) objection, bisected: Junction's wrapped diff draws 2 frames per width step again.** Scene: jrepo's 8th commit in the Commit view with wrap on, 21 steps of −120 px drag. Frames a step: 5a212f3e and each of b258f4fb…59609522 (10 builds, 1 launch each) **0.95**; c32c4b2e **1.90**; 75f2288a and 440ba012 **1.90**.
  - At 440ba012 each step is a 7.8 KB frame inside the size message with no build (rows still at the old width's heights), then a 157 KB build frame 2–7 ms later.
  - This brings back round 14's two-frame resize, which `LeadWrappedDiffWidth` had fixed.
  - Bytes still fell overall: 661 → 345 KB a step at c32c4b2e, then 171 KB after 7f3c9fd0.
- **Main's 41 commits: no confirmed real-app regression.** Base → new, 4K at 150 %, 120 Hz, one launch a side:
  - History, 20 Downs at 400 ms: UI 22.8 → 22.2 KB a key; all threads 9.25 → 9.23 MB; 80 = 80 processes; whole scene 31.82 → 31.86 MB.
  - bigrepo Commit walk: 32.7 / 32.5 → 32.1 / 31.3 MB a key.
  - A save: 7 = 7 processes, 128 → 130 KB. A commit over History: 1,465 → 1,464 KB, 7 frames.
  - zh drag 50.4 = 50.4 KB a step.
  - Vesper navigation 314 → 319 KB (+1.4 %); Vesper reader wrapped drag 19.2 = 19.2 KB a step.
  - Meridian URL typing 10.0 → 10.1 KB a character (1 frame each); request open 304 → 322 KB (+6 %).
  - Held Shift 0 frames on both sides.
  - Median History frame: render 0.391 → 0.390 ms (55 % of the frame), flush 0.254 → 0.256 (35 %), present 7 %, other 0.023 → 0.042 ms. SkiaSharp 4 on GL costs the same in real frames.
  - Rests are the same on both sides (below). Start-up phases are within one-launch noise; SDL_CreateWindow is the largest phase (83–299 ms).
- **Headless walks +1.3 % UI KB a key (geomean 1.010 over 48 walks).** Builds, measures and layout visits are equal. Not SkiaSharp 4: 043f5b38 against its parent 99db58cf moved 0.0 %, 2 runs each. About half the rise comes before that commit and half after it. Below every bar.
- **Round 15 helped the real apps** (5a212f3e → 75f2288a):
  - **History scene:** 85.1 → 31.8 MB all threads (−63 %); processes 264 → 101; start-up 57 → 27 processes.
  - **20 Downs:** 53.1 → 9.25 MB; GCs 5/3/1 → 1/0/0; 10.9 → 4.0 processes a key; UI 39.8 → 22.8 KB a key.
  - **Held Shift:** 30 → 0 frames. **Row click:** 268 → 149 KB, 8 → 4 processes.
  - **Saves:** 22 → 7 processes a save; a save that joins a hunk 2 → 1 frames, 220 → 128 KB UI, 3.41 → 2.32 MB. **git add:** 24 → 9 processes.
  - **Wrapped diff:** 661 → 170 KB a step (but see c32c4b2e); splitter 300 → 174 KB.
  - **Vesper reader wrapped drag:** 279 → 19.2 KB a step.
  - **bigrepo (c6a19b06's trade):** History start and rest 296 → 2.6 MB; the first Commit-tab open 1.6 → 210 MB (real window 160 MB); scene total −10 %.

CANDIDATES:
- id: reactivity-resize-batch-two-frames (owner 6)
  - **what:** a resize that rebuilds a wrapped diff now has its build after the size message's frame, so each step draws a stale frame and then a second one.
  - **evidence:** the bisect above. The size-message frame is 7.8 KB with no build; the build frame is 157 KB. Vesper's reader builds nothing on a resize, so it is not affected (0.95 on base).
  - **real app:** resizing or snapping Junction with "Wrap long lines" on. Occasional. 21 of 21 steps draw 2 frames, and each first frame shows rows at the old width's heights.
  - **expected:** frames 1.90 → 0.95 a step (−50 % of the resize's frames: render, flush and present), and the stale frame goes. Check Junction's breakpoint crossing (`Rerender` from `OnResize`) too.
  - **cost:** small–medium; no API. Pixels: removes a wrong frame.
- id: apps-gl-native-commit-jump (lead; owner 3 once bisected, unconfirmed)
  - **what:** on 440ba012, three GL scenes gained about 40 MB native memory that base never gained.
  - **evidence:** end of scene, base → new:
    - Vesper: 111.7 / 123.1 → 151.5 / 157.5 MB working set / private. The rise comes during 40 wheel events that draw 2 frames.
    - Junction History: 136.6 / 186.2 → 176.9 / 223.6, at the 13th Down.
    - Meridian with a collection open: private 108 → 147 MB already 1.2 s in.
    - Each comes with one long flush (23.5–28.8 ms, against 1.2 ms) and about 47 ms of CPU on both the UI thread and NVIDIA's GL worker thread. Managed allocation +0.01 MB.
    - Equal in the other 6 paired GL scenes, in Aperture (Vulkan) and in all rests. One launch a side.
  - **real app:** Vesper's first wheel, Junction's History walk, Meridian at start with a collection: +27–36 % working set.
  - **expected:** retained-memory bar, if it repeats.
  - **next:** not bisected, because my 4 holds were used. Repeat the Vesper scene twice a side with `gpu` steps. Then bisect 99db58cf against 043f5b38 (SkiaSharp 4), the SDL 3.5.0 commit (fd0cccb7) against its parent, and 440ba012 with `HULTUI_A11Y=0` to rule out the screen-reader fix.
  - **cost:** unknown.
- id: apps-junction-big-file-revisit (Waiting, owner 5 with 4) — re-ranked below, stronger.

REAL WINDOW (the 4K, DISPLAY2, primary, DPI 144 = 150 %, 120 Hz; OS cursor outside; machine timer 1 ms, held by Steam):
- **Safety:** 4 holds of 212.9 / 187.7 / 201.8 / 169.7 s; waits 0 / 0 / 30.7 / 5.2 s. 53 launches, 0 foreground takes, 0 nvlddmkm events, 0 screen reads.
- **Jellyfin:** 2 sign-in attempts as the existing device aperture-r13 both returned `library=none`. Aperture showed only its sign-in page, so library browsing was not measured. On that page DXGI local memory is 69.0 → 66.5 MB after a 21-step drag on both sides, 0.95 frames a step.
- **Idle wakes at rest (5 s window bounded by the host's own lines):** base and new are equal.
  - GL apps: 9.6–10.8 a second. Vulkan apps: 111.5–112.4.
  - All of them on NVIDIA driver threads; the UI thread woke 0 times in every app.
  - GL apps rest at 15.6 ms timer resolution in-process (guard thread ~65 a second), Vulkan at 1 ms (~171).
  - Reel paused after a clip: 122.3 → 123.5 a second (nvoglv64 96–97, nvcuda64 10).
- **Only a real window showed:**
  - the wrapped diff's size-message frame (the 2-frames regression);
  - the native memory jumps;
  - the first palette open commits 37–55 MB private (with +1–3 MB working set) on every side, r15pre included, in Junction and Meridian. Probably GPU scratch memory; information for expert 3.
- **Headless:** the 48 walks show only the +1.3 %. Every win above is in real-window frames or on other threads.

WAITING, RE-RANKED:
- **apps-junction-big-file-revisit:** stronger. The real-time headless probe is stable within ±1 %:
  - first visits 30.6–30.8 MB a key, revisits the same (nothing is kept); GCs 25–27/8–11/5–6 per 12 keys;
  - real window 32.1–32.7 / 31.3–32.5 MB a key on both sides;
  - round 15's 11.9 MB first-visit figure was a low one-launch outlier.
  - Keeping the blob reads or the tokenizer's state clears the churn bar on revisits.
- **Suite (d):** keep, low. The zh drag is 50.4 KB a step on both sides (the round 15 rise was the one-time cache growth).
- **Suite (e):** raise to medium, as the guard for question 64 (a), which is round 16's build. A commit over History is still 1,464 KB UI and 7 frames on all three sides.
- **Suite (f):** keep, low. Aperture's DXGI memory settles 69.0 → 66.5 MB on both sides.
- **aperture-poster-as-hero:** unchanged (ApertureApp.Cards.cs:536–538). Not exercised (sign-in failed).
- **aperture-tmdb-size-fallback:** unchanged (RequestModels.cs:406–419).
- **junction-narrow-gitlab-glyphs:** GitLab code touched only by the screen-reader fix's 2-line a11y change (21ec8bb6). Stands; not exercised.
- **meridian-pane-choice-not-persisted:** still reproduces (MeridianApp.cs:148, flipped at :351, never saved).
- **meridian-min-width-wraps:** MinWindowWidth is still 760 (:146). d43b3518 fixed Meridian's URL-bar label wrapping, so re-check with a headless shot at 760×540 after a send (needs a local HTTP server). Not re-shot.

EXAMINED, NOT WORTH IT:
- **HarfBuzz 14.4 cold words in the apps:** zh open 4,079 → 4,097 KB, the drag equal, Meridian typing equal. Cold Segoe UI words are a time cost on first visits only, and no app action showed a count that reaches a bar.
- **bigrepo's first Commit-tab open, 210 MB:** c6a19b06's intended trade; the scene total is −10 %.
- **History click on a big commit, 18 MB all threads:** the same on base and new. It is the detail landing, which r15pre had prefetched.
- **Flush mean ×1.40 in History:** outliers. The median is 0.254 vs 0.256 ms.
- **Vesper reader drag 20 → 24 frames, Meridian palette 11 → 15 frames:** one launch each; tween frames, about 0 KB.
- **9895efd6 (dashed borders):** no app draws a dashed or dotted border.

NEEDS THE USER:
- **Jellyfin:** both sign-ins today failed (`library=none`). Check that jellyfin.home and the API key are up. Remove the devices aperture-r12, aperture-r13 and aperture-survey, and revoke the key, when the work is done.
- **Suite proposals** (the orchestrator's call):
  - (g) a gated UiTests guard: a wrapped-row list driven by a 21-step drag on the window's thread draws 1 frame a step (guards c32c4b2e's regression);
  - (h) an AsyncLoader per selection that allocates about 30 MB and is cancelled at 400 ms: all-thread MB and GCs a key (the big-file revisit);
  - (i) a gated GL/Vulkan guard for process commit after the first overlay open with a shadow.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\b6ef64d9-4ed1-4793-9955-6a986d74de79\scratchpad\r16-apps\`
- **Publishes:** `aot\` (READY.txt), `aotx\`, `aotx-base\`, `bs-<sha>\` (the 10 bisect builds); sources `start\`, `start-x\src\` (Script.cs, Program.cs, SafeOut.cs); `mkbs.ps1`.
- **Real windows, in `rest\`:** `common16.ps1`, `batch16a.ps1`–`batch16d.ps1`; summaries `sum15.py`, `shares16.py`, `st16.py`, `wakes14.py`; data `runs\h1..h4.jsonl`, `*.frames.csv`, `*.sum.txt`; logs `runs-h1.log`–`runs-h4.log`; `locks.log` and `gate.log` (in `r16-apps\`).
- **Headless:**
  - walks: `walk\aot-b15`, `walk\aot-r16`, `walk\aot-sk3`, `walk\aot-sk4`; `walks16.ps1`, `sk34.ps1`, `mk16.py`, `cmp10.py`; `effect16.txt`, `runs\walks16.jsonl`, `runs\walks-k34m.jsonl`;
  - bigrepo probe: `bisect\` (src, mkbisect.ps1, runbisect.ps1, run1.log, run2.log).
- **Fixtures:** `fx\` (copies of round 15's).
- **State:** my worktree is clean at 440ba012 and build servers are shut down. The base and bisect worktrees are unregistered; their folders in the scratchpad hit "Filename too long" and are partly left behind. None of my processes are running.
