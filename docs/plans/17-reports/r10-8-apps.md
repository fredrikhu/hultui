# Round 10 survey, expert 8: app workloads

SATISFIED: no

**Method.**
- **Trees and builds:** I extracted two detached trees of my own: `tree-base` at 10acc8c0 (round 8's release) and
  `tree-after` at 3462f18e (same src as facced69). Both carry round 7's probe and the walk host as NativeAOT publishes.
  The host is round 9's `Program.cs` + `R9.cs` plus a new `R10.cs`; round 9's `Walk.props` was gone, so I rebuilt it
  from r7b1's.
- **Runs:** 69 walks, base and after alternated per app, 2 reps. Bytes are identical between reps, except the timing
  walks.
- **Fixtures were rebuilt:** the old scratchpads' `fx`/`www` had been cleaned out.
  - `jrepo` is a clone at 3c2afc67. That commit matches round 9's leftover index exactly (2,726 entries, 0 differ).
  - coll25, coll200 and coll-r9 were regenerated with the same names and shapes; 88 images come from jrepo's PNGs.
  - So some absolute rows differ from round 9's (commit-files 125 vs 158.6). Base and after share the fixtures.
- **Safety:** all headless (no monitor or scale involved). 0 real windows, 0 screen captures. Jellyfin sign-ins as
  "aperture-survey": **0**. The fixture server is stopped and my worktree is clean at 3462f18e.

REVIEW: none. No walk got worse.
- **Junction diff find Enter +1.0 % (31.5 → 31.8 KB):** I bisected it over 5 points and it is noise. The walk is
  bimodal, 31.4 or 31.8 KB a key, in every tree: after gave 31.8 / 31.4 / 31.4, and 86bdeb67 gave 31.8 / 31.4 / 31.8 /
  31.4.
- **Other rises are under noise:** resptab +0.3 %, response drag +0.2 %, find typing +0.1 %.

**Round 9 helped real apps.**
- **Totals:** round 9's 65 walks, geometric mean after/base **0.852**. Sum of KB a key 20,186 → 11,239 (−44.3 %),
  mostly Discovery.
- **Meridian:**
  - Discovery map click (600 endpoints): 2,903 → 81.3 KB a click (−97.2 %); builds 3.08 → 1.08.
  - Discovery from a URL: 11,664 → 6,418 KB (−45 %).
  - Spec import: 9,690 → 4,958 KB (−49 %).
  - Environments cell typing: 818.8 → 5.1 KB (−99.4 %); builds 1 → 0.
  - 89k body typing: 239.0 → 182.2 KB (−23.8 %); builds 1 → 0.
- **Aperture:** `library-resize-4px` 6.5 → 3.3 KB (−49.4 %). The d366505b objection is closed.
- **Vesper:** Thumbnails −1.7 / −2.8 %; transfer −12.5 % (timing, spread 17 %). Aperture toast frames −6.2 %.
- **Unchanged (±0.3 %):**
  - All 22 Junction walks. There was no Junction commit, and the headless bridge is not a lent-diff borrower, so the
    a11y walks cannot show 5c9d6b8b.
  - Vesper Detail, Brief, partner, insert, type-to-find, Tab, folder in/out and viewer.
  - Meridian History, find, method pick, palette, URL, response tabs, drag and pretty toggles.
  - Aperture grids, Home and Discover; Reel.
- **Shares:** headless render is a median 90 % of UI cycles (p25 85.5 %, min 39 % for the palette with a reader).

CANDIDATES:
- id: apps-vesper-sync-cursor-rebuilds-rows (owner 6; app)
  what: Each Up/Down in Synchronize (F9) rebuilds CommanderApp and every sync row, and each row builds a fresh filtered
  list to find its own index.
  evidence: (new walk `vesper-sync`, 80 keys, 2 reps; base = after)
  - 300 files (200 shown rows, 1,886 elements): 2,568.6 KB a key.
    - 1 CommanderApp + 2 Part builds a key. Build is 98 % of bytes, 46 % of UI cycles (render 37 %).
    - GCs 12/2/0 per 80 keys.
  - 60 files (40 rows): 453.7 KB a key.
  - The causes in CommanderApp.Sync.cs:
    - Down/Up call `MarkDirty()` (:120-121).
    - `BuildSyncBody` puts every row in a plain ScrollView (:292-294).
    - `SyncRowView` runs `VisibleSyncRows().IndexOf(r)` (:349). With "Show equal" off (the default) that is
      `Where(...).ToList()` (:92), which makes the bytes O(rows²).
  - Prototype that passes the loop index: 300 files 2,568.6 → 1,715.9 KB (−33 %); 60 files 453.7 → 404.9 KB. What is
    left is about 8.2 KB a row a key.
  expected: scaling bar.
  - The index fix alone: O(rows²) → O(rows), −33 % at 300 files.
  - Rows kept (KeptElements keyed by path; state = selected and direction) or a VirtualList: O(1) a key, about −95 % at
    300 files.
  - A recursive compare over a source tree easily has thousands of rows.
  cost: small (index) + medium (kept rows); app only; no pixel change. A VirtualList would also bring the missing reveal
  (see NEEDS THE USER).
- id: apps-meridian-runner-rail-reads-every-request (owner 6; app)
  what: Every MeridianApp build in the Runner view reads and parses every .req file in scope three times, only to show
  counts. A run builds once per result and rebuilds every result row, so a run is O(n²) in file reads and bytes.
  evidence: (new walk `meridian-runner`, one run as one step, the server waits 20 ms a send, 2 reps; base = after)
  - 50 requests: 121,536 KB a run; 52 MeridianApp + 104 Part builds; GCs 7/0/0.
  - 200 requests: **1,783,390 KB a run**; 202 + 404 builds; GCs 107/17/2; build is 96 % of UI cycles. 4× the requests
    gives 14.7× the bytes.
  - Probe at 200 requests:
    - `ScopeRequests()`: 2,629 KB a call, about 13 KB a request (`TryReadFile` + `ReqDocument.Parse` + `EffectiveAuth`,
      Runner.cs:37-58).
    - The rail: 7,926 KB a build. `ScopeRequestsCountFor` runs for the whole collection, each folder and the scope
      (:156, :160, :181, :234-241).
    - The result list: 1,467 KB a build, 7.3 KB a row, every row rebuilt (:276-277).
  - Prototype that counts from the scanned tree (no file read): rail 7,926 → 36 KB a build.
    - Run 50: 121,536 → 18,969 KB (−84 %), GCs 7 → 1.
    - Run 200: 1,783,390 → 189,255 KB (−89 %), GCs 107/17/2 → 11–12/1–2/1.
  - The Chain subtab has the same pattern: `AnalyzeAllRequests` reads and parses every request on each build
    (Chaining.cs:239, 316-333). Not measured.
  expected: memory churn and scaling bars. −89 % a run from the count alone. With result rows kept (one new row a
  result), a run becomes O(n): about −99 % (estimated).
  cost: small (count) + small–medium (kept rows); app only. One edge case: a request file that cannot be read would now
  be counted in "N req", though it still would not run.

WAITING, RE-RANKED:
- **Suite scenarios:** still worth rows.
  - document-switch: Junction Files 86.4 → 86.2 KB a key; 1.23 JunctionApp builds a key (the file's text lands and
    builds again); 149.1 measure misses; 60.8 layout visits; build 10 %, render 78 % of cycles.
  - Two new app patterns the suite lacks, each with a kept-rows twin:
    - "a cursor key over a plain 200-row list rebuilt through the root" (Sync);
    - "one row a frame appended to a 200-row list rebuilt through the root" (Runner).
- **apps-meridian-response-drag-build:** still 39.2 KB a move (base 39.1, round 9 39.5). Build is 33.8 KB (86 %), 1
  Part a move, 210.7 measure misses, 62 layout visits. Round 9 did not touch this path in this walk (Body tab, in-memory
  keyring). Still low rank: it is a drag.
- **aperture-poster-as-hero:** code unchanged (ApertureApp.Cards.cs:538-539). Not exercised (no sign-in).
- **aperture-tmdb-size-fallback:** code unchanged (RequestModels.cs:419, `w1920`, no fallback). Not exercised.
- **junction-narrow-gitlab-glyphs:** no Junction commit in round 9. Not exercised (needs GitLab).
- **meridian-pane-choice-not-persisted:** still reproduces. `_responseBottomChoice` (MeridianApp.cs:126) is flipped at
  :292 and never saved.
- **meridian-min-width-wraps:** still reproduces, but only after a send. At 760×540 the response tab "Headers 6" wraps,
  and Send's Ctrl+Enter keycap and the response bar's right end are cut off. Before a send the editor fits (the URL
  ellipsizes). Shots: `drv\shots\min760.png`, `drv\shots\min760sent.png`.
- **junction-review-old-path:** still reproduces. `GitFileDiff.OldPath` stays null (GitDiffLine.cs:31, :44); no
  Junction commit.
- **junction-leads (remaining):** still reproduces. No test in tests/Junction.Tests covers `SelectConflict` or a
  conflict row's right-click.

EXAMINED, NOT WORTH IT:
- **Junction diff find Enter +1.0 %:** bimodal noise, as in REVIEW (13 runs over 6 trees).
- **Vesper Tab, 92.1 → 92.2 KB a Tab:** CommanderApp + 2 Parts, once per Tab. 8f756384 did not move it.
- **Meridian method pick, 149.5 KB a pick:** 2 view builds; the tree part is not rebuilt. Once per click; unchanged.
- **Reel playback, 0.8 KB a frame with no build:** 0.5 KB is the fake backend's `Advance`, render 0.2 KB. Unchanged,
  headless only.
- **Vesper transfer −12.5 %:** timing (spread 17 %).

NEEDS THE USER:
- **vesper-sync-cursor-off-screen (bug, not perf):** Sync's cursor leaves the view and nothing scrolls it back in. After
  40 Downs in a view that shows 26 rows, no highlighted row is on screen (`drv\shots\sync40.png`). Fixing it adds
  scrolling the user will see. Approve revealing the cursor in Sync? [Yes, with the candidate's VirtualList or kept
  rows.]
- **Jellyfin:** 0 "aperture-survey" sign-ins this round. The earlier devices and the API key are still to be removed
  and revoked.

FILES: `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\3484c586-c631-4af0-957d-95e7a03cac41\scratchpad\r10-apps\`
- **Walk host:** `walk\src\Program.cs` (round 9's host + 2 dispatch lines), `walk\src\R9.cs`, `walk\src\R10.cs` (Sync
  and Runner walks, the RUNCOST probe), `walk\Walk.props` (rebuilt), `walk\{base,after,proto,x1..x5}\WalkHost.csproj`.
- **Trees:**
  - `tree-base` (10acc8c0) and `tree-after` (3462f18e), probe applied.
  - `tree-proto` (after + the Sync index and Runner count prototypes).
  - Bisect trees `tree-x1` (86bdeb67), `tree-x2` (b4f182b5), `tree-x3` (28ec28fd), `tree-x4` (25a96234), `tree-x5`
    (58aeb965).
- **Scripts:** `tree.ps1`, `aotpub.ps1`, `run.ps1`, `batch.ps1`, `cmp10.py`, `genfx.py`, `genrun.py`, `srv.py` (the
  fixture server, with `?delay=`).
- **Data:**
  - `runs\walks.jsonl` (all walks, both sides, 2 reps), `cmp-all.txt`
  - `runs\proto.jsonl` (prototype runs), `runs\bisect.jsonl`
  - `runs\*-meridian-runner.out.txt` (the RUNCOST lines)
  - `drv\shots\min760.png`, `drv\shots\min760sent.png`, `drv\shots\sync40.png`
- **Fixtures:** `fx` (jrepo at 3c2afc67, coll25, coll200, coll-r9, coll-run50, coll-run200, images, reel, spec600.json,
  spec60.json) and `www`.
