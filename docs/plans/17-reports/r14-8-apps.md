SATISFIED: no

**Method.**
- **Shared publishes:** all five apps were published on NativeAOT at e6f7b5aa into `r14-apps\aot\<App>\`, and READY.txt was written first. All exited 0, and the smoke test passed (hold h1).
- **Scripted hosts:** built from round 13's start-x code at e6f7b5aa (`aotx\`) and at 2ced4e26 (`aotx-base\`, a clean archive of the tree). Round 14 added new script steps:
  - `git:<args>` (a terminal's git, run from the script thread) and `touch:` (a save);
  - `jfile`, `jfetch`, `jcheckout` (the app's own actions);
  - `gpu` (the process's DXGI `QueryVideoMemoryInfo`);
  - `reelpause` (with the real mpv backend), and key modifiers.
- **Headless:** the same 67 walks as round 13 (`walks.ps1` + `walks2.ps1`), sides alternated. Base: round 13's own `walk\aot-r13` (2ced4e26, identical walk source). New: `walk\aot-r14` (e6f7b5aa).
- **Real windows:** 4 holds of 146, 150, 173 and 49 s. Waits were 0, 0, 139 and 317 s. 39 launches. Every scene ran base and new, alternated.

REVIEW: none.
- **Headless:** geomean of UI KB new/base is 0.997, sum −0.1 %.
  - Junction History down-60ms: 29.3 → 26.8 KB a key (−8.5 %); history-500ms-a11y −8.0 %. This is da72e01b; it answers my round 13 objection.
  - Only one other walk moved over 2 %: vesper thumbs-open-landings +8.9 % (builds 0.13 → 0.23, one rep). That is a timing walk, as in round 13.
- **Real windows, did round 13 help? Yes, more than the headless walks show.**
  - **Pointer sweeps** (60 moves, frames base → new): Junction History 49 → 13 (only the HoverHighlight tint is left), Junction diff 33 → 0, Vesper list 48 → 2, Reel 7 → 0, Aperture library 9 → 0, Meridian 0 → 0. Placing the pointer drew 1 → 0 frames in every view except History.
  - **Junction watcher, History on screen, pointer resting** (UI KB base → new):
    - a save: 2 → 1 frames, 237.8 → 172.4 (−27 %);
    - terminal commit: 1,844 → 1,579 (−14 %), 7 frames both;
    - terminal branch: 724 → 678;
    - terminal `fetch origin f1`: 980 → 955;
    - the app's own Fetch: 2,860 → 1,840 (−36 %), all-thread 10.6 → 6.0 MB, frames 98 → 77 (the toolbar spinner);
    - the app's own checkout: 1,639 → 1,077 (−34 %), all-thread 9.2 → 5.1 MB;
    - terminal checkout: 726 → 680.
  - **Junction Changes view:** unchanged, as designed (two saves 895 / 444 KB, commit 594 KB). A terminal `git add` drew 0 frames on both sides: the staged state goes stale (junction-git-optional-locks, expert 6).
  - **Junction start on jrepo** (UI bytes in the 1.5 s after the first frame, 5 scenes a side): 3,115–3,187 → 2,191–2,240 KB (−30 %). With a11y off: 1,873. On bigrepo (2 commits): 367 both. So the cut is about 950 KB of bridge bytes that scale with History's 1,910 items, most likely 505123d7 (labels kept).
  - **Junction History drag** (UI KB a step): 27.6 / 24.9 → 19.2 / 17.0 (−30 %). With a11y off (hold 4): 15.4 / 16.6. The bridge's drag share fell from ~11 / 7.6 KB (round 13) to 3.8 / 0.4. This closes most of round 13's apps-junction-drag-a11y-bytes.
  - **CJK Files view, 41-step 1 px drags:**
    - zh at a 1,440 px window (960 logical): 89,494 → 1,622 KB (2,183 → 39.6 KB a step, −98 %), 1.51 → 0.98 frames a step;
    - ja at 1,440 px: 105.5 → 52.2 KB a step (−51 %); ja at the default size: 26.0 → 16.4 (−37 %);
    - zh at the default size: 17.1 both;
    - one resize to 1,440 px: 1.39 → 1.31 MB.
    - This is 698c2758, the same as round 13's prototype measure.
  - **Vesper wrapped reader** (TODO.md, wrap on): drag 1.93 → 0.97 frames a step, 318.5 / 312.3 → 252.6 / 248.0 KB a step (−21 %). The 1 px drag went 1.95 → 0.98 frames, 335.7 → 269.3 KB. This is 154a7ca3.
  - **Aperture after size changes** (signed in, 18 changes through 9 sizes; DXGI local / non-local MB):
    - settled 80.98 / 31.56, after the drag 100.48 / 31.56 on both sides;
    - base stays at 100.48 / 31.56 for 6 s;
    - new goes to 82.98 / 24.56 between 0.55 and 1.55 s after the last change: 24.5 MB freed (4fdb56cb);
    - rest wakes equal afterwards (116.5 vs 117.6 a second, all NVIDIA).
  - **Reel paused on a clip** (real mpv, bands.y4m): the `mpv-events` thread went 2.0 → 0 wakes a second (276789d8). See REAL WINDOW for what is left.
  - **Timer resolution at rest** (the host's 5 ms guard thread: ~180 a second at 1 ms, ~67 at 15.6 ms):
    - Junction 180.2 → 67.3; new Vesper 68.4, Meridian 67.7 (cd7e3db2);
    - Vulkan apps 171–183 on both sides: the NVIDIA driver holds 1 ms;
    - the machine-wide timer is 1 ms (another process holds it).

CANDIDATES:
- id: apps-junction-ref-move-a11y (owner 7, lead; evidence for Waiting a11y-gate-on-client)
  what: with History on screen in a real window, a ref move costs the bridge 25–55 % of the event's UI bytes, plus one frame with 15.6–17.8 ms of "other".
  evidence: hold 2 (a11y on) against hold 3 (HULTUI_A11Y=0), both on the new host, UI KB:
    - terminal commit 1,579 vs 708 (−55 %);
    - terminal branch 678 vs 480;
    - terminal fetch 955 vs 682;
    - the app's Fetch 1,840 vs 1,383;
    - the app's checkout 1,077 vs 781;
    - terminal checkout 680 vs 484.
    - Frames with over 5 ms of "other": 3 with a11y on (commit, fetch, the app's Fetch; the base had 18.8–23.6 ms) against 0 of 145 with it off.
    - A new commit at the top of the list moves every realized row: a list keyed by index renames all of them.
  real app: Junction, every commit, fetch or checkout (terminal or in-app) while History is shown: many times a day.
  expected: churn bar (−25…−55 % an event) and likely the time bar on that frame, if the bridge's per-event work goes (rows keyed by hash, or a bulk rename told as one ChildrenInvalidated). Count raises per event first, with round 13 step 5 (b)'s probe.
  cost: medium; may change what a reader hears (same live check as a11y-gate-on-client).
- id: apps-vesper-wrapped-drag-bytes (owner 1, with 5; lead)
  what: the wrapped reader still allocates 248–269 KB a drag step at 1 frame a step, and none of it is the bridge.
  evidence: hold 4, a11y on vs off: 252.6 / 248.0 / 269.3 KB a step both ways. Headless round 13 step 7 (299 KB, 64 PatchFroms a step) suggests the visible rows are rebuilt, not only re-measured. Opening the reader costs 1,631 KB, 468 KB of it the bridge.
  real app: Vesper's reader (Shift+F3) with wrap on, resizing or snapping the window: occasional.
  expected: churn bar if wrapped rows keep their elements and only re-measure at a new width; count builds and text calls a step first.
  cost: medium, no API.

REAL WINDOW (the 4K primary, DPI 144 = 150 %, 120 Hz, HDR on (sdrWhite 240), GL 610.88, RTX 4080; the driver process is DPI-unaware and logs it as 2560×1440):
- **Safety:** 0 foreground takes, 0 activations handed back, 0 nvlddmkm events, 0 screen reads. The OS cursor was outside every window. Two Jellyfin sign-ins with the existing device "aperture-r13" (no new device).
- **Shares (non-resize frames, new):**
  - History Down: render 50 %, flush 37 %, present 9 %, other 3 %; 25.6 KB a key, 1.10 frames.
  - Commit view walk (bigrepo): render 46–50 %, other 21–25 %; 828–896 KB a key, 1.00 frames (unchanged from round 13).
  - Vesper navigation: other 39 %, 343.4 KB, 1.37 frames.
  - A commit event: other 66 % (the lead above).
- **Trap:** frames drawn inside the size message carry the previous normal frame's "other" value: Reel's drag showed a constant 0.496 ms, frame-time sum 680 vs 18 ms. Read only bytes and frames from drag rows, never their shares.
- **Reel paused:** 125–127 wakes a second on both sides. What is left:
  - NVIDIA nvoglv64 threads at 99.5, 10 and 4 a second;
  - an `nvcuda64` thread at 10 a second, even on a raw y4m clip: mpv's CUDA context;
  - working set 310 vs 252 MB (one launch each, not conclusive).
  - For expert 3: whether CUDA could start only for a stream that hardware-decodes.
- **At rest:** the UI thread woke 0 times in every app. Working set at +10 s: Junction 113, Vesper 97, Meridian 94, Reel 109, Aperture 114 MB; 0 GCs.

WAITING, RE-RANKED:
- **aperture-poster-as-hero:** not perf, unchanged (ApertureApp.Cards.cs:535–539).
- **aperture-tmdb-size-fallback:** not perf, unchanged (RequestModels.cs:411–419).
- **junction-narrow-gitlab-glyphs:** not perf; no GitLab file changed in round 13.
- **meridian-pane-choice-not-persisted:** reproduces (MeridianApp.cs:148, flipped at :340, never saved).
- **meridian-min-width-wraps:** stands (MinWindowWidth 760, MeridianApp.cs:146).
- **junction-leads (remaining):** still no test calls `SelectConflict` (JunctionApp.Changes.cs:1477).
- **Suite scenarios (a):** keep low. Vulkan rest is 114.4–114.8 wakes a second on both sides, all NVIDIA.
- **vesper-enter-two-frames:** below every bar; propose closing.
  - 6 of 30 (base) and 11 of 30 (new) navigations drew 2 frames.
  - The second frame carries the listing as it lands (227–263 KB); bytes are equal either way.
  - It depends on timing: whether the async listing lands before the next frame.
- **junction-commit-first-visit-frames:** not reproduced. The 33 ms first-visit pass drew 1.00 frames a key, and revisits at 100 ms 1.00. Keep low.
- **junction-commit-detail-whole-patch:** stands, off the UI thread.
  - bigrepo commit walk at 100 ms: 4.1–6.8 MB all-thread a key against 0.83 MB UI.
  - jrepo History: 12.8 MB all-thread once a 40-key walk rests.
  - Keep low; it is a scaling case only for huge patches.

EXAMINED, NOT WORTH IT:
- **Long "other" frames after a ref move:** they look like a timer-tick wait (15.6 ms) but are the bridge. The base, at 1 ms resolution, shows 18.8–23.6 ms, and they vanish with a11y off.
- **Vulkan timer resolution and rest wakes:** driver-owned, as in round 13.

PERF-SUITE PROPOSALS:
- **"text/wrap a 75,000-character Chinese document resize, 1 px a frame":** use real gettext prose (`r13-text\mkreal.py`) at 960 logical px, past the 8,192-entry word cache. It guards 698c2758 (real window 2,183 → 39.6 KB a step). The suite's wrap rows are Latin and synthetic.
- **"a11y/insert above 1,000 item rows, a UIA client listening" (UiaRig):** the commit-arrives case of apps-junction-ref-move-a11y. The suite's insert-above rows run without a bridge.
- **A gated UiTests guard for 4fdb56cb:** a Vulkan window's DXGI local memory drops back within 2 s of 18 size changes (−17.5 MB here).

NEEDS THE USER:
- **apps-junction-ref-move-a11y:** cutting the bridge's per-event raises changes what a reader hears. Decide it with the a11y-gate-on-client live check (Narrator, NVDA).
- **Jellyfin:** remove the devices "aperture-r12", "aperture-r13" and "aperture-survey", and revoke the API key, when the work is done.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\41943a9b-f657-4483-971a-a2b346b967f7\scratchpad\r14-apps\`
- **Publishes:**
  - `aot\` (shared, READY.txt); `aotx\` (new, scripted); `aotx-base\` (2ced4e26, scripted);
  - sources in `start\` and `start-x\src\` (`Script.cs` with the round 14 steps, `Program.cs` with Reel's `START_REEL_CLIP`);
  - `tree-base\` (the 2ced4e26 archive tree), made by `mkbase.ps1`.
- **Real windows, in `rest\`:**
  - scripts: `common14.ps1`, `batch14a.ps1`–`batch14d.ps1`;
  - analysis: `frames14.py`, `wakes14.py`;
  - data: `runs\h1..h4.jsonl`, `runs\*.frames.csv`, `runs\*.out.txt`, `runs\h*-windows.log`; logs `runs-h1.log`–`runs-h4.log`.
- **Lock log:** `locks.log`.
- **Fixtures:** `mkfx14.ps1` makes `fx\jremote.git`, `fx\jw-*`, `fx\jpush`, `fx\cjk-ja`, `fx\cjk-zh`; also `fx\bands.y4m`.
- **Headless:** `walk\aot-r14` (tree-r14, `tree.ps1`, `aotpub.ps1`); `allwalks.ps1`, `walks*.ps1`, `run.ps1`; `runs\walks.jsonl`, `mk14.py`, `cmp10.py`, `effect14.txt`.
- **State left:** the worktree is clean at e6f7b5aa, build servers are shut down, and the fixture server is stopped. The one JunctionHost still running is expert r14-dotnet's.
