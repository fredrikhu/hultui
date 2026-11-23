SATISFIED: no

**Method.** I published all five apps on NativeAOT at aa3a3696 into `r18-apps\aot\` (all exit 0, 0 IL warnings, READY.txt written before any hold). These are the scripted host, which behaves like the plain host when HOST_SCRIPT is unset. Round 17's `r17-apps\aot` and `aotx` folders, and r16's UIA client, have lost their DLLs (SDL3.dll, libonigwrap.dll, FlaUI), so they no longer start. I made a runnable base, `r18-apps\base` (0df5e3ea plus the same-version DLLs), and rebuilt the UIA client in `r18-apps\uia`. Real-window base is 0df5e3ea and new is aa3a3696. Headless: 48 AOT app walks on each side. Bisect builds: `aotx-s1` (66f46f11), `aotx-pre19` (252c70c2), `aotx-p19` (45cbde7b). Probe build `aotp` (Aperture plus an `apcache` step that reads the poster cache's entries and decoded sizes).

REVIEW:
- **Round 17 helped the real apps** (real window, base → new):
  - Junction first file visits: 698 → 547 KB a key (−22 %). Revisits unchanged at 376/377.
  - First Commit open: 1,464 → 1,356 KB (−7 %).
  - View switches: History 300 → 255 KB (−15 %), Overview 150 → 89 (−40 %), Branches 162 → 117 (−28 %), Commit 494 → 473 (−4 %).
  - bigrepo walk at rest after b1423e6e's collection: ws 202.4 → 134.9 MB, committed 92.3 → 24.5 MB. The Commit open's all-thread bytes fell 258 → 168 MB (a787c194).
  - Reel with mpv, the player bar's first fade: DXGI local +12.0 → +2.0 MB; end-of-scene private 487 → 474 MB.
  - Vesper with both panes on one folder, other cursor on a file: 29.9 → 8.9 KB a key. With the other cursor on "..": 50.8 = 50.8 KB a key, which is step 11's decided case (headless 29.5).
  - Headless walks: geomean 0.961, sum −0.9 %. Vesper partner 33.5 → 4.5, transfer-frames −25 %.
- **Resize and splitter steps were equal in both of my scenes:**
  - jrepo's 8th commit: unwrapped 47.5 / 47.2 KB a step, wrapped 19.6 / 19.6.
  - bigrepo HEAD's big file: 14.8–15.7 / 15.6–15.7, wrapped 17.0–18.1 on both sides; splitter 55.3 / 55.4.
  - So dd3ad712 and e790d321 save nothing on these diffs; their real-window wins came from experts 1 and 5's own diffs.
  - jrepo splitter: new has one 129 KB frame mid-drag where base has 9 KB, in both runs (+6 KB a step over 20 steps). Below every bar.
- **45cbde7b (main's plan 19 merge; bisected across 5 builds):** Junction start's first History frame 347 → 488 KB UI, the next 344 → 383 KB (start +7.7 %, once a session). History Down 15.2 → 16.0 KB a key (+5 %). Lead for expert 5 (probably the digit table's first use), low.
- **0f9c8477 (Aperture history and keyboard):**
  - Back and Forward fetch nothing and cost one page build: Home 198–209 KB, an item page 143 KB, the same as base's Escape.
  - Tab costs 0.5 KB, 1 frame.
  - An item page's first build is 244 → 278–294 KB (+14–20 %), and a whole page open is 1,444 → 1,535 / 1,706 KB (one base sample). The likely cause is Pressable handlers and the arrival focus. Lead for expert 6, low.
  - Headless `library-resize-4px` 3.2 → 4.2 KB a step (+1 KB).
- **ee044d36 / 93f8a26d (New seasons row):**
  - The first season look, 30 s after sign-in: ~3.4 MB all-thread, off the UI thread, once.
  - F5 adds +0.4 MB for its look.
  - Home start 1,159 → 1,174 KB (+1.3 %).
  - No objection.

CANDIDATES:
- id: apps-aperture-artwork-retained (lead, owner 4, with 2 for the GPU side)
  what: Aperture keeps every item page's hero, decoded on the CPU and as a GPU texture, in a poster cache bounded by entry count (400, PosterCache.cs:80), not by bytes. Private memory grows ~30 MB per item page opened and is never returned.
  evidence: probe `apcache`, 2 runs identical:
    - Home: 19 images, 8.1 MB decoded.
    - After 6 item pages: 47 entries, 65.1 MB decoded. Of that, 3 heroes at 2176×1224 (10.2 MB each) and one 2000×3000 poster used as hero (22.9 MB).
    - Private 147 → 331 MB, still 313 MB after a full GC (heap 5.1 MB). Working set 144 → 252 MB.
    - DXGI local 81 → 140 MB, nonlocal 31.6 → 52 MB.
  real app: Aperture, opening film, episode and series pages, dozens a session. 6 pages cost +184 MB private; 20 pages would be ~+600 MB (O(pages visited)).
  expected: retained memory. A byte budget for big images (e.g. keep the last 2–3 heroes) would cut ~40 MB decoded plus their textures after 6 pages, roughly −15…−30 % private; more in longer sessions. Expert 2 should say whether dropping the CPU bitmap after upload is safe.
  cost: small–medium (app cache). No API change; pixels only if an evicted hero must load again.
- id: apps-aperture-artwork-landing-rebuilds-view (lead, owner 6)
  what: every artwork landing calls `RerenderView()` (ApertureApp.Cards.cs:339–344), so each poster or hero that lands rebuilds the whole page.
  evidence: an item page open takes 8–11 frames and 1.4–1.9 MB UI. Of those, 6–8 frames are whole-page builds of 92–107 KB each as data and artwork land. Home start: 13–14 frames, 1.16 MB.
  real app: every Aperture page open and every scroll that brings new posters in.
  expected: churn. If a landing only rebuilt the card or picture it affects (a per-picture part), most of the ~107 KB builds would go. My estimate: −30…−50 % a page open. The split between data landings and artwork landings is not counted yet.
  cost: medium. No API change, no pixel change.
- id: apps-aperture-home-shelf-turn-builds-home (lead, owner 6)
  what: a Down or Up that lands on a shelf showing another page turns that shelf and builds all of Home (ApertureApp.Cursor.cs:243–245, Home.cs:140).
  evidence: real window, after walking Right past the first page: 128–133 KB and 2.25–3 frames a Down, the same on base and new. A Right within a page costs 0.7 KB, 1 frame. Headless `home-down-100ms` is 29.1 KB because its shelves have only 5 cards.
  real app: Aperture browsed by keyboard (0f9c8477 makes the whole app keyboard-driven). Every row change after walking right.
  expected: memory churn bar. With each shelf's page as its own part, about −75 % a Down.
  cost: small–medium.

REAL WINDOW:
- **Desk and safety:**
  - 4 holds: h1 96.6 s, h2 208.9 s, h3 191.7 s, h4 111.5 s. Waits 0 / 0 / 166 / 188 s.
  - One more hold-4 attempt was stopped while still waiting for the lock and never took it (re-run with an added scene).
  - 32 launches, 30 ran (h1's 2 base launches failed on the missing SDL3.dll). 0 foreground takes, 0 nvlddmkm events, 0 screen reads.
  - Every launch was on the 4K (DISPLAY2, primary, DPI 144 = 150 %, 120 Hz), with the 1440p at 100 %, 120 Hz to its left. Timer resolution 1 ms (Steam). The OS cursor was outside the windows.
  - Idle gate: h3 failed (mean 31 %; my headless walks were running). h1, h2 and h4 passed.
  - I signed in to Jellyfin as the named user (key file path only). All of Aperture's config folders were deleted afterwards.
- **What only the real window showed:** Aperture's artwork memory (the headless fake library has no image bytes), the shelf-turn Down (4.5× headless), the per-landing page builds, the Vulkan steps not taken, the UIA client's time share, and the Junction start bisect.
- **Frame shares (new build, frames after start):**
  - Junction scene: render 30.9 %, flush 58.3 %, layout 6.1 %, present 3.7 %, other 0.9 %; 117.6 KB a frame.
  - Aperture signed in: render 32–38 %, flush 40–44 %, present 17 %, layout 3–5 % (13.8 % on item pages).
  - Reel with mpv: render 51 %, present 32 %, flush 14 %.
  - Aperture during a Vulkan drag: present 54 %, other 27 %.
- **Start-up:** first frame 290–462 ms.
- **Memory:**
  - Ws at scene end: Junction 171 MB; Aperture after 3 item pages 239–255 MB (private 280–284, equal on base).
  - DXGI local on the sign-in page: 68.98 MB, equal to rounds 16–17.

WAITING, RE-RANKED:
- Suite (h): close. b1423e6e's idle-collection test in HighlightResumeTests is the guard; the suite runs no Junction.
- Suite (a), (d), (f): keep, low. (f): the sign-in page's DXGI is unchanged at 68.98 MB.
- New suite proposals:
  - (j) an Aperture Home with 3 shelves of 30 cards: walk Right past the first page, then Down (guards the shelf-turn lead).
  - (k) an Aperture item page with N pictures landing one per frame, counting view builds per landing.
- junction-files-first-open: unchanged, once a session, low. 3,841 KB and 5 frames (base 3,906 KB; round 17 measured 3,806 KB and 3 frames).
- vk-drag-steps-dropped: still stands, medium, expert 3.
  - At 16 ms pacing, Reel with mpv took 13 (base) and 19 (new) of 31 steps; Aperture's sign-in page took 17 and 25. At 33 ms all four took 31 of 31.
  - UI-thread cost is 11.7–12.4 Mcyc a step (~3.7 ms), so each step spends 13–30 ms waiting outside our code (present 54 %, other 27 %).
  - In a user's own drag the OS merges mouse moves while a step runs, so I infer ~30–45 updates a second against GL's 60+. Not measured: no real input allowed.
- a11y-client-frame-share: still stands, medium, expert 7.
  - With our client asking: "other" is 18.5 % of frame time against 0.9 % with none (round 17: 26.2 % against 3.7 %). Bytes only +2.4 % a frame.
  - By step: Commit open 34.9 %, view switches 21–54 % (96 % on the first switch after the client's second ask), History Downs 16 %.
  - The time is outside our phases; count the provider calls before building anything.
- aperture-signin-user-message: still reproduces. ApertureResult.cs:46: NotFound becomes "That is no longer on <host>". Sign-in with a user name works.
- aperture-poster-as-hero: still reproduces (ApertureApp.Cards.cs:549–550). It now has a cost: a 2000×3000 poster decoded as a hero is 22.9 MB CPU plus 22.9 MB GPU, on 1 of the 6 pages I opened. Folded into apps-aperture-artwork-retained.
- aperture-tmdb-size-fallback: code unchanged (RequestModels.cs:424, `w1920`, no fallback). Not exercised (no request server).
- junction-narrow-gitlab-glyphs: no GitLab file changed. Stands, not exercised.
- meridian-pane-choice-not-persisted: no Meridian commit since 0df5e3ea. Still reproduces.
- meridian-min-width-wraps: unchanged (MinWindowWidth 760, MeridianApp.cs:146). Still stands.

EXAMINED, NOT WORTH IT:
- Aperture Back/Forward history: no fetch; it costs the destination page's build, as Escape did. 100 Visits of three fields each.
- The season watch's 30-minute tick: one build every 30 min.
- Aperture keyboard Tab: 0.5 KB a press.
- Vesper real-window Tab: 177–195 KB, already Examined.

NEEDS THE USER:
- Round 17's scratch publishes and r16's UIA client have lost their DLLs. A cleanup is deleting *.dll files from older scratchpads; anyone reusing them must re-publish or copy the DLLs back.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\00f54f03-b511-4949-9273-30fe719e1bbb\scratchpad\r18-apps\`
- Publishes:
  - `aot\` (READY.txt), `base\`, `aotp\`, `aotx-s1\`, `aotx-pre19\`, `aotx-p19\`, with trees `t-*`;
  - `start-x\` (Script.cs adds `apcache`), `mkbase18.ps1`, `uia\`.
- Real windows: `rest\common18.ps1`, `batch18a–d.ps1`; `rest\runs\h1..h4*` (`.out.txt`, `.frames.csv`, `.jsonl`, `-windows.log`); `rest\runs-h1..h4.log`; `locks.log`, `gate.log`.
- Headless: `mkwalk18.ps1`, `walk\aot-r17\`, `walk\aot-r18\`, `runs\walks18.jsonl`, `runs\cmp18.jsonl`, `cmp18.txt`.
- Fixtures: `fx\`.
- State: my worktree is clean at aa3a3696, build servers are shut down, and none of my processes are running.
