# Round 19 survey, expert 8: App workloads

SATISFIED: no

**Method.** I published the five apps on NativeAOT at 56d9a3f6 into `r19-apps\aot\` and wrote READY.txt before any hold
(all exit 0, 0 IL warnings). I also published `mid\` at 1ccda002, which is round 18 without main's 8 commits, and
`aotp\Aperture`, a probe build: 56d9a3f6 plus r18b9's framework counters plus a PosterCache probe that writes one row
per download (ask, slot, headers, last byte and end times, bytes, queue depth; never the URL).

Bases:

- `r18`: round 18's survey publish (aa3a3696).
- `r18p`: the same plus `apcache`.

Every launch was a guarded real window on the 1440p only (2560×1440, 100 %, 200 Hz), window 1440×900. Aperture was
signed in to the real Jellyfin as the named user, with the key-file path only. All numbers are UI-thread KB and frames
from the frame log, plus counts.

REVIEW:

- **Round 18 helped Aperture (r18p → aot, real server, two holds).**
  - Item page from Home's second shelf, 4 pages: 1,824–2,033 KB and 8–15 frames → 73–301 KB and 3–12 frames
    (−85…−96 %).
  - Film page from a library: 1,059 → 678 KB (−36 %).
  - Library first open: 541–636 → 332–352 KB (−35…−48 %). A second library: 605 → 290 KB (−52 %).
  - Home start: 794–990 → 529–542 KB (−32…−47 %).
  - Back: 216–238 → 180–206 KB (−13…−18 %). Forward: 172–215 → 144–183 KB (−15…−17 %). Home by key 1: 361 → 299 KB.
  - Home shelf, 11 cards a page:
    - the Right that turns a page: 248.6 KB in 2 frames → 80.8 KB in 1 (−67 %);
    - the Left back: 77.8 → 63.0 KB;
    - Down: 177 → 2.8 KB, Up: 60 → 0.4 KB (133bcfe6: no page turn).
  - After 4 item pages: private 188.3 → 181.2 MB (−3.8 %), heap 17.3 → 7.8 MB, DXGI local 73.2 → 66.6 MB.
  - Heroes: 4 heroes of 1408×793 (17.0 MB) on both sides. At 100 % every hero fits 678a786c's 24 MB budget, so that
    commit changes nothing on this monitor; it acts at 150 %.
  - Sign-in page DXGI local: 38.86 → 33.23 MB, exactly step 12's number.
- **Round 18 helped Junction (bigrepo, r18 → aot).**
  - Commit open: 1,065 → 971 KB (−8.8 %).
  - First file visits: 633 → 520 KB a key (−17.8 %). Revisits: 564 → 460 KB (−18.5 %).
  - Wheel over the diff: 54.7 → 39.9 KB a notch (−27 %).
  - History Down: 15.3 KB on both.
  - At rest after the big walk: working set 124.5 / 126.6 MB, heap 25.6 / 26.0 MB, equal. Both builds collect at rest
    here (b1423e6e fires first), so f7f2f6c5's case did not arise; it is not worse.
- **Vesper and Reel.**
  - Vesper Multi-rename (second run of each exe): DXGI local 30.16 → 22.29 MB (−26 %), private 100.2 → 89.4 MB (−11 %).
    Bytes equal (789.5 KB at open).
  - Reel with mpv: DXGI local 248.33 → 244.58 MB. The player bar's second fade: 130.7 → 105.7 KB.
  - Vesper's Arabic reader drag was **not re-measured**. My scripted Shift+F3 opened no reader on either build (1.2 KB,
    1 frame). The pane drag was 10.8 KB a step on both.
- **Main's 8 commits cost no app anything measurable.**
  - Aperture, mid → aot: page turn 76.6 / 80.2 KB, item pages 231–315 / 233–301 KB, Back 187–225 / 187–223 KB.
  - Home start: 486 KB on mid (one sample) against 527–542 KB on aot. Home start moves with landing timing (r18p gave
    794 and 990 KB in two runs).
  - d335159e: no app draws a pixel source on the GPU path (mpv uses `GpuFrameSink`).
  - 1951019d: no polling, and no app reads the setting.
  - bd3605ff: no app uses the new scrollbar or canvas text lines.
  - 11554b8b adds an `int _mnemonicIndex` to every `TextElement`: expert 6 should size it.
- No objection.

CANDIDATES:

- id: apps-junction-changes-diff-retained (lead, owner 4 with 6; folds in expert 3's lead)
  what: Junction's full preload (JunctionApp.cs:470 → `EnsureChangesLoaded` → `ReadChanges` → `GetWorkingDiff`,
  JunctionApp.Changes.cs) parses the whole working diff at start and keeps every file's parsed diff, while the Changes
  view shows one file.
  evidence: real window, aot, jrepo (1,910 commits). A copy reset clean, then scripted deletions, fresh copy each launch.
  Columns: deleted lines | all-thread MB by 1.2 s | GCs by then | heap / working set / private MB after a full GC.
  - clean: 0 | 15.4 | 0/0/0 | 5.6 / 105.9 / 102.9
  - docs/ deleted (116 files): 32k | 37.5 | 2/1/1 | 12.7 / 115.4 / 114.4
  - src/ + tests/ deleted (1,322 files): 267k | 148.3 | 9/8/2 | 53.0 / 173.0 / 171.3
  - round 18's dirty fixture (694 files): 783k | 314.9 | 20/19/5 | 123.1 / 332.7 / 342.7
  - That is ~380–500 B allocated and 150–220 B kept per changed line. UI bytes at start are equal (1.93–1.97 MB): the
    cost is off the UI thread, and it stays.
  - Expert 3's 3,049-file tree: 640 MB heap.
  real app: any big uncommitted change before a commit: a folder deleted, a rename not yet staged, a line-ending or
  formatter run, a vendored update. It is paid at every start and every repository switch, even when the user never
  opens Changes.
  expected: retained memory. +9 % working set at 32k changed lines, +63 % at 267k, +214 % at 783k. It is also O(changed
  lines) scaling. Parsing per selected file (or keeping the raw patch and parsing on view) leaves about the clean figure
  plus one file.
  cost: medium (app code). No API or pixel change. The first file in Changes would wait for one `git diff -- <file>`.
  Same code path on Linux.

REAL WINDOW:

- **Desk.**
  - Only the 1440p was attached at all four holds (DISPLAY1 2560×1440, DPI 96, 200 Hz, primary).
  - Timer resolution 1 ms (Steam). The user's OS cursor was inside the window rectangle at every launch.
  - The idle gate passed for h1, h3 and h4. h2 failed (mean 25.6 %, the other surveys' load).
- **Holds and safety.**
  - Holds: h1 187.9 s, h2 152.7 s, h3 175.8 s, h4 150.1 s; waits 0 / 41.7 / 305 / 160.8 s.
  - 30 launches, all `ok`: 0 foreground takes, 0 refused activations reported, 0 nvlddmkm events, no screen reads.
  - Aperture's config folders are deleted, and no file contains the user name.
- **What only the real window and real data showed.**
  - The Changes preload's retained heap (a real repository tree).
  - The real server's download times (below).
  - Real-server page sizes: 11 cards a shelf page, a hero of 1408×793.
  - The first launch of a new exe costs +41–58 MB private on GL: Vesper rename on aot in h2, Meridian on r18 in h4, with
    the same bytes and a longer first frame. That is NVIDIA's shader cache; a second run is equal, so I excluded it.
- **Frame shares (aot, frames after start).**
  - Aperture on Vulkan, signed in: render 30–33 %, flush 47–50 %, present 11–13 %, other 3–8 %, layout 2–4 %, style
    ≤0.5 %.
  - Junction on GL: render 50–64 %, flush 25–30 %, present 5–13 %, other 1–12 %, layout 3–4 %.
  - Vesper pane drag: flush 67 %, render 19 %, layout 7 %, present 7 %.
  - Reel with mpv: render 57 %, present 25 %, flush 15 %.
- **Aperture's UI bytes by phase (probe), Home to a library.**
  - Layout 13 %, render 9 %, style ~0 %; builds and reconcile the rest.
  - A library first open puts 187–224 KB in the render phase (66 % of that step's bytes), against 7.7 KB for all of
    Home's start. Most likely the grid's labels are laid out at paint. That only moves bytes between phases; it is not
    waste.
- **Start-up shares (aot).**
  - GL apps: the window phase (which includes the GL driver) 49–60 %, the first frame 22–32 %, before SDL 12–15 %, the
    rest ≤3 %.
  - Aperture on Vulkan: GPU (instance, device, swapchain) 39–44 %, window 29–32 %, before SDL 14 %, first frame 7–15 %.
- **Working set by area (aot).**
  - Aperture after 4 pages: working set 166.9, private 181.2 MB. Of that: GC committed 21.8 (heap 7.8), decoded pictures
    23.4 (60 images, heroes 17.0), the rest about 136 MB native (Skia, the Vulkan driver, which counts GPU memory in
    private bytes, and code). DXGI local 66.6, nonlocal 22.4 MB.
  - Junction, clean jrepo at rest: working set 103.8 MB, heap 5.7.
  - bigrepo after its walk: working set 126.6 MB, heap 26.0.
  - Vesper: working set 89.8 MB, heap 5.5.
  - Reel with mpv: working set 245.7, private 462.7, DXGI local 244.6 MB.
  - Meridian idle: working set 81.6 MB, heap 3.9 (one sample).

WAITING, RE-RANKED:

- **aperture-postercache-slot-stall: not worth it as perf; keep expert 4's body timeout as a small correctness fix.**
  - 186 downloads over three probe walks (cold and warm server).
  - Queue wait: 0 over 500 ms, max 362 ms. The queue was non-empty 0.62 s of 48.7 s cold, 0.01–0.02 s of 36 s warm. 0
    waiters behind 8 downloads that were each slow (>1 s).
  - Body, headers to last byte: posters p50 0.0–0.5 ms, max 2 ms; heroes (158–370 KB) 3–5 ms. The slowest download of
    all: a cold hero, 563 ms (362 ms to the first byte, the server resizing).
  - Downloads in flight at each of 22 dumps: 0. None in flight near 20 s, and no body ever went quiet.
  - A replay with a separate hero lane or a 1.5–3 s timeout changes nothing here: heroes never waited.
  - Expert 4's stuck-body case is a robustness gap (the body read has no token) that I did not see on this LAN server.
- **aperture-poster-card-cost (6): low.** A real page turn is 80.2 KB for 11 new cards (~7 KB a card including the
  shelf), 63.0 KB with the pictures cached. It happens once every 11 keys.
- **junction-files-first-open: unchanged, once a session, low.** Clean jrepo: 4,022 KB, 5 frames. On the same tree r18
  4,057 KB and aot 4,065 KB.
- **aperture-signin-user-message:** still reproduces (ApertureResult.cs:46).
- **aperture-poster-as-hero:** the code is unchanged (ApertureApp.Cards.cs:796–797). None of the heroes on the 6 item
  pages I opened was a poster: every one was a 1408×793 backdrop.
- **aperture-tmdb-size-fallback:** unchanged (RequestModels.cs:424), not exercised.
- **junction-narrow-gitlab-glyphs:** no GitLab file changed since aa3a3696.
- **meridian-pane-choice-not-persisted:** still reproduces (MeridianApp.cs:148, flipped at :351, never saved).
- **meridian-min-width-wraps:** still stands (MinWindowWidth 760, MeridianApp.cs:146). No Meridian commit since round 18.
- **suite-bidi-rows-fresh-session (5):** a suite matter. Keep, low: warm the rows with an untimed pass. I could not
  re-take the real Arabic drag (see REVIEW).
- **Suite (a) and (d):** keep, low.
- **Suite (f):** keep, low. Its number is now 33.23 MB (two images, on the 1440p).

EXAMINED, NOT WORTH IT:

- d335159e, 1951019d and bd3605ff in the apps: no app takes these paths (see REVIEW).
- Aperture Back, Forward and Down in a library with cached pictures: 0.4 KB and 1 frame a key.
- The Junction and Meridian memory jumps on a first launch: the GL shader cache, excluded.

NEEDS THE USER:

- A cleanup is changing shared scratch fixtures, not only deleting DLLs. Round 18's `r18-apps\fx\jrepo` has 694 tracked
  files deleted (a 24.4 MB diff), and round 14's jrepo has 3,049. Anyone measuring Junction must reset a copy first.
  Should the cleanup skip `*\fx\*` folders?

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\e91920b9-3793-496a-9a73-f017250d8772\scratchpad\r19-apps\`

- Publishes: `aot\` (READY.txt), `mid\`, `aotp\Aperture`; hosts `start-x\` (plain) and `start-p\` (probe host);
  `probe-r19-final.patch` (framework counters and PosterCache rows).
- Real windows: scripts `rest\common19.ps1`, `scenes19.ps1`, `batch19a.ps1`–`batch19d.ps1`; analysis `sum19.py`,
  `slots19.py`, `startup19.py`; output `rest\runs\h1..h4*` (`.out.txt`, `.frames.csv`, `.slots.csv`, `.startup.txt`,
  `h*.jsonl`), `rest\runs-h1..h4.log`, `locks.log`, `gate.log`.
- Fixtures: `fx\`. `jclean` is reset; `jdel-docs` and `jdel-srctests` are the scripted deletions.
- State: my worktree is clean at 56d9a3f6, build servers are shut down, and none of my processes or monitors are
  running.
