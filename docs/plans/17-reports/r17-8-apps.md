SATISFIED: no

**Method.** The five apps are published on NativeAOT at 0df5e3ea in `r17-apps\aot\` (all exit 0, 0 IL warnings). READY.txt was written before any hold, and hold 1's smoke test is appended to it. My scripted hosts are in `r17-apps\aotx\`. Round 17 adds three things to them: `dragrelT` (resize steps from a Win32 timer callback, with UI-thread cycles), `gcmem`/`gcfull` (heap, committed, ws and private, before and after a forced compacting full GC), and the Aperture sign-in failure's reason. The bases are r16-apps\aotx (440ba012) and my harness built at 440ba012, ff990dfb, f76cb440 and 80e94eeb (`aotx-<sha>`). Headless: the 48 AOT walks at 0df5e3ea, compared with round 16's 440ba012 rows. All figures are bytes, counts and shares.

REVIEW:
- **80e94eeb (round 16 step 9 (b)) objection, bisected: after a big-file walk, Junction holds +48 MB working set, and +38 MB even after a full GC.**
  - Scene: bigrepo, the Commit view's file walk (12 first visits, 12 revisits at 400 ms, 40 held keys).
  - End of scene, ws / private MB: ff990dfb 154.1 / 163.4; f76cb440 154.3 / 163.7; **80e94eeb 202.6 / 212.2** (+31 %).
  - After a forced compacting full GC, f76cb440 → 80e94eeb:
    - heap 12.7 → 23.6 MB (the kept texts, as the Landed row says);
    - committed 34.5 → 72.8 MB;
    - ws 144.9 → 183.1 MB;
    - private 154.3 → 192.6 MB.
  - Gen-2 collections over the scene: 9 → 3.
  - Repeats: the new side 3 launches (ws 202.4–203.4), the old side 4 launches (440ba012 ×2 158.6–159.9, ff990dfb, f76cb440). f76cb440 alone is equal to its parent.
  - The row's "retained heap 9.7 → 21.0 MB" understates the real-window cost 3–4×.
- **Main's 8 commits: no real-app regression.**
  - Headless walks: geomean 1.002; sum of UI KB a key +0.0 %. The only walk past 1 % is vesper thumbs-60ms: 5.2 → 5.9 KB a key, layout visits 4.9 → 6.2 a key. That is below every bar.
  - Real window: History Downs 28.6 → 27.4 KB a key. Against round 16's 440ba012 numbers: Vesper navigation 318.5 → 302.7 KB, Meridian left-list click 183.8 → 182.3 KB, URL typing 10.1 → 9.3 KB a character.
- **Round 16 helped the real apps** (440ba012 → 0df5e3ea, one launch a side, the 4K):
  - **Junction start:** UI 2,159.6 → 1,775.5 KB (−18 %). With our UIA client asking about 1 s in: 1,824.9 KB.
  - **A ref move with History shown:** 1,428.4 → 619.0 KB (−57 %), 7 = 7 frames, all threads 5.39 → 4.56 MB. With the client asking: 623.4 KB.
  - **Commit → History:** 652.0 → 298.2 KB (−54 %); with the client 314.7 KB.
  - **First Commit open (unwrapped):** 1,575.9 / 1,569.7 → 1,482.2 / 1,473.3 KB (−6 %), 2 = 2 frames. Wrap toggle 902.7 → 890.5 KB.
  - **Wrapped Commit diff, a user's drag** (timer steps, 21 × −120 px):
    - 162.2 → 20.4 KB a step (−87 %);
    - UI-thread cycles 206 → 163 M per 21 steps;
    - 0.95 frames a step on both sides.
  - **Wrapped Commit diff, posted steps:** 1.90 → 0.95 frames a step, 171.6 → 40.6 KB a step, all threads 3.35 → 0.41 MB per 21 steps, GCs 1/1 → 0/0.
  - **First palette open:** DXGI local +18.0 → +8.0 MB, private +55.5 → +13.5 MB; private at scene end 220.7 → 186.9 MB.
  - **History gradient (89732bec):** not isolated, because my log has no render-phase bytes. A History key is −1.2 KB a key overall.
  - **bigrepo:**
    - UI 910.6 → 632.7 KB a key (−30 %; the a11y gate);
    - revisits 368.4 / 374.5 → 336.8 / 366.2 MB a 12-key pass (−2 … −9 %), gen-2 4 → 0–1;
    - first visits equal.
  - **Vesper reader open (TODO.md):** 1,723.8 → 1,246.7 KB (−28 %). Wheel, PageDown, wrap and the wrapped drag are equal within 1 %.

CANDIDATES:
- id: dotnet-junction-kept-texts-working-set (owner 4, with 5; the 80e94eeb objection)
  - what: the kept-texts LRU (strings over the apps' 512 KB LOH threshold) changes when gen-2 runs. It leaves about 27 MB of committed free memory plus extra garbage, besides the 10.9 MB of texts it keeps.
  - evidence: the bisect above. The gcmem lines are in `runs\h2-jbig*`, `h4-jbig*` (`*.out.txt`).
  - real app: Junction on a repository with large files (bigrepo's 675 KB files), in any browse session where a key cancels colours. The memory stays until the next gen-2, and nothing at rest triggers one.
  - expected: retained memory: ws at the end of the walk −20 … −24 % (202 → about 155–165 MB) if only the texts stay. Options to measure:
    - keep the texts as UTF-8 or pooled;
    - a smaller LRU bound;
    - one `GCCollectionMode.Aggressive` collection when the walk goes idle;
    - `System.GC.ConserveMemory` in AppRuntime.props.
  - cost: small–medium; no API, no pixels.
- id: a11y-client-frame-share (lead, owner 7)
  - what: with a UIA client asking for the root, "other" (time outside style, layout, render, flush and present) is 26.2 % of Junction's frame time over the scene. With no client it is 3.7 %; base 440ba012 with the desk listener is 12.4 %.
  - evidence, share of "other", client asking vs none:
    - Commit → History 82.8 % vs 7.7 %;
    - Commit open 39.9 % vs 0.2 %;
    - History Downs 19.3 % vs 4.4 %.
    Bytes rise only 2.8–6 %. One launch a side.
  - real app: any Narrator, Magnifier or Voice Access user switching views and moving keys.
  - expected: the time bar, if expert 7's raise counts per frame show a removable part. I give shares only.
  - cost: unknown.
- id: reactivity-vesper-same-folder-down (lead, owner 6, low)
  - what: with both panes on the same folder (docs), a Down in the active pane costs 32.9 KB (18 KB in its input). Different folders: 2.8 KB a key on base and new alike.
  - real app: Vesper with both panes on one folder. Not compared with base.
  - cost: unknown.

REAL WINDOW:
- **Safety:** 4 holds of 192.4 / 238.6 / 139.5 / 124.4 s; waits 0 / 777 / 303 / 69 s. 21 launches, 0 foreground takes, 0 nvlddmkm events, 0 screen reads.
- **Desk:** every launch on the 4K (DISPLAY2, primary, DPI 144 = 150 %, 120 Hz). Timer resolution 1 ms (Steam). The user's OS cursor was inside the window's area in 20 of 21 launches. Idle gate: h1 failed (mean 18 %); h2–h4 passed.
- **Only the real window showed:** 80e94eeb's working set, the client's frame share, the bytes inside the size message, and DXGI memory. Headless, the walks moved +0.0 %.
- **Walks (~2 min each, new build), shares of frame time after start:**
  - Junction (297 frames): render 36.6 %, flush 41.6, present 13.0, other 4.8, layout 3.8; 52.5 KB a frame.
    - History read at 800 ms: 23.7 KB and 4 git processes a key.
    - Files view first open: 3,806 KB, 3 frames (lead, owner 6, once a session).
    - Changes saves: 408 / 106 KB, 7 processes each.
    - Branches Down 79.5 KB; palette typing 41 KB a character.
    - Held Down 39.5 KB a frame; held Shift 0 frames.
    - Wrapped timer-step drag: 17.5–19.1 KB a step, 0.97 frames a step.
  - Vesper (526 frames): render 42.3 %, flush 35.7, present 10.2, other 8.4, layout 3.4.
    - Down 4.0 KB; navigation 302.7 KB; Tab 177–204 KB.
    - Reader: wheel 24.4 KB a notch, PageDown 334 KB (about 8.5 KB a newly shown line; 34 % of those frames is "other"); close 634–664 KB.
    - zh README (168 KB) wrapped open: 6,815 KB, 85 % "other" (the Waiting text-open-measures-every-line).
    - Wrapped drag 37–39 KB a step (zh 55).
  - Meridian (428 frames): render 35.9 %, flush 39.3, present 21.2, layout 2.1, other 1.5.
    - URL typing 9.3–10.7 KB a character; left-list click 182 KB.
    - Palette open: 15 frames, 147–200 KB.
    - Drag 6.4–8.5 KB a step.
  - Aperture, sign-in page (Vulkan, 112 frames): present 76.5 %. DXGI local 68.98 MB, equal to round 16.
  - Reel, mpv playing (599 frames): render 44.3 %, present 42.7 %. Steady playback has a median of 0 B a frame; play/pause toggles cost up to 51 KB.
  - Vulkan timer drags: Aperture and Reel got through only 11–19 of 31 queued steps in the drag window (present 54–88 % of those frames); the GL apps took all 31. Information for expert 3.
- **Start-up:** first frame 307–380 ms; SDL_CreateWindow is 26–50 % of it.
- **Working set at the end of each walk (ws / private MB):** Junction 179.4 / 195.2, Vesper 161.8 / 165.8, Meridian 106.7 / 133.0, Aperture 118.6 / 109.8, Reel 254.3 / 483.2.
- **Once only:** in h4 both of Vesper's sides took 1.9–2.6 s to the first frame, against 0.3–0.5 s in h1–h2. It came right after three bigrepo copies; not repeated.

WAITING, RE-RANKED:
- **Suite (a):** keep, low.
- **Suite (d):** keep, low.
- **Suite (e):** closed by 41ecec65 (the row `a11y/insert above 1,000 item rows, UIA`).
- **Suite (f):** keep, low (Aperture DXGI 68.98 MB, equal).
- **Suite (g):** closed by ec57c4ee (the gated UiTests guard).
- **Suite (h):** raise to medium, as 80e94eeb's guard. An AsyncLoader per selection that allocates ~30 MB and is cancelled at 400 ms, measuring committed MB and ws after a forced full GC as well as all-thread MB and gen counts. It was not built in round 16.
- **Suite (i):** closed by 96d00ab3 (DialogFadeGpuMemoryTests).
- **aperture-poster-as-hero / aperture-tmdb-size-fallback:** code unchanged; not exercised (the sign-in failed, see below).
- **junction-narrow-gitlab-glyphs:** no GitLab code touched; stands.
- **meridian-pane-choice-not-persisted:** unchanged (MeridianApp.cs:148, flipped at :351, never saved).
- **meridian-min-width-wraps:** unchanged (MinWindowWidth 760, :146); not re-shot.
- **Rich text editor:** not reached in a real window (it needs GitLab). Headless walks review-comment-typing (± a11y) and summary-typing moved < 1 %.

EXAMINED, NOT WORTH IT:
- **Vesper thumbs:** +0.7 KB a key, below every bar.
- **Vesper Tab:** 204.4 = 204.9 KB on base and new. Once per action, already Examined; the real window is 2× the headless 92–103 KB.
- **History Downs' 58–76 MB all-thread per 20 keys:** 3–4 keys of about 12 MB each, the known big-commit detail landing. The client run's +18 MB is one more landing, not the client.
- **Vesper right-pane Down 25 KB:** my vtab step counted a Tab that landed late; per key it is 2.8 KB on both sides.
- **A ref move's 7 frames and 17 git processes:** the count is unchanged; the bytes fell 57 %.
- **Meridian body typing:** not measured. `clickel TextAreaElement` found none on "Customers 0".

NEEDS THE USER:
- **Jellyfin:** the server has 3 users, so an API-key sign-in needs a user name. The host now logs `signin=NotFound: jellyfin.home:8096 has 3 users — name the one to browse as`, which is also why round 16's sign-ins returned `library=none`. Name the user to browse as (the scripted host takes `START_APERTURE_USER`), or leave Aperture's library unmeasured. Separately, the app's `ApertureFailure.UserMessage` drops this detail (an app bug).
- **The 80e94eeb trade:** if expert 4 cannot get the extra committed memory back, decide whether +38–48 MB working set is worth revisits −2 … −9 % all-thread bytes and gen-2 collections 4 → 0–1 per 12-key revisit pass.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\35903ed7-ff10-416d-84e9-b6c09675ab6b\scratchpad\r17-apps\`
- **Publishes:**
  - `aot\` (READY.txt), `aotx\`;
  - `aotx-16\`, `aotx-ff990dfb\`, `aotx-f76cb440\`, `aotx-80e94eeb\` (Junction), with their trees `t-*`;
  - sources `start\`, `start-x\src\` (Script.cs, Program.cs); `mkbase.ps1`.
- **Real windows, in `rest\`:**
  - scripts: `common17.ps1`, `Gate.cs`, `batch17a.ps1`–`batch17d.ps1`;
  - summaries: `sum17.py`, `marks17.py`, `shares16.py`;
  - data: `runs\h1..h4.jsonl`, `*.frames.csv`, `*.sum.txt`, `*-windows.log`, `h1-jr16-aotx-uia.uia.txt`;
  - logs: `runs-h1..h4.log`; `locks.log` and `gate.log` in `r17-apps\`.
- **Headless:** `walk\aot-r17\`, `mkwalk.ps1`, `run.ps1`, `mk17.py`, `cmp10.py`, `runs\walks17.jsonl`, `runs\cmp17.jsonl`, `cmp17.txt`. The probe's VirtualList row counter did not apply at 0df5e3ea (38ec9773 changed that line), so that one count is missing.
- **Fixtures:** `fx\`.
- **State:** my worktree is clean at 0df5e3ea and the build servers are shut down. None of my processes are running.
