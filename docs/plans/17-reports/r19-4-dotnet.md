# Round 19 survey, expert 4: .NET runtime

SATISFIED: no

Set-up: detached at 56d9a3f6 (SDK 11.0.100). I report bytes, counts and complexity only. I made no repo edits, so the
worktree is clean. Build servers are shut down and nothing is left running. I used 2 desk holds and 5 launches of expert
8's shared NativeAOT publishes.

REVIEW: one objection, to **d7eedc4b** (see candidate 1). No objection to the other commits.

- **d7eedc4b (pictures land by URL):** `_artWaits` (ApertureApp.Cards.cs:445) keeps every `ArtWait` until its picture
  lands. Each wait's `Current` is the waiting `ImageElement`, so it holds that page's whole element tree.
  - Refs are never cleared when an element unmounts (Element.cs:937–960).
  - Normally this is short-lived: 0 waits are left after each settle in the healthy probe run.
  - When a picture body stalls, it never lands, and the retention grows with every page visited.
  - Each build that makes a waiting card adds a link to its chain: 4,885 waits for 443 URLs.
- **8b7471c8 (UIA disconnects on their own thread):**
  - **Lifetime:** one background MTA thread per process, made at the first `Disconnect`, waiting in `Monitor.Wait` with
    no timeout. That is 0 wakes at rest and one `Pulse` per apply that hands it providers while idle.
  - **What it keeps:** `s_bridges` holds a bridge only from `Wake` to its visit; the `Run` loop's local is dead after
    the visit in optimized code (not probed).
  - **Dispose drain:** tells what is left on the UI thread, then spins (`Sleep(0)`, never pumps) at most 2 s on the one
    call in flight. It is bounded, but a hung client would burn a core for up to 2 s at window close.
  - `_handed.RemoveAt(i)` is O(n²) per batch, on the worker only (n = 13–14 for a palette close).
  - **Real window, Junction palette open + 3 Downs + Escape ×30, heap after full GCs:**
    - with round 18's FlaUI reader-like client asking (30 bulk adds and removes heard): 6.6 → 6.4 MB, private 114.5 →
      117.6 MB, flat from cycle 10;
    - with no client: 6.5 → 6.3 MB.
  - No retention.
- **f7f2f6c5:** the re-arm while input continues costs one `Task.Delay` and closure per 2 s, only after a big walk.
  Nothing per frame.
- **678a786c:** `Drop` disposes on the UI thread. `HeroBytes` is a loop over at most 4 heroes per hero ask.
- **941cffdf / 0f5cda09 / f980fc8d (kept parts):** bounded.
  - Rail doors are fixed by name and slot. Home shelves are kept by row key, and a row Home no longer draws lets its
    part go when Home builds.
  - Headless JIT, healthy art run, 100 open/back cycles then 150 forward pages: heap 1.35 → 1.86 → 1.81 MB. Round 18's
    no-art run of the same probe went 1.32 → 1.84 MB (and 1.62 MB after 150 forward). No growth from the parts.
  - New objects: `HomeShelfPart` 136 B, `RailDoorPart` 48 B, `ArtWait` 64 B; `ApertureApp` 1,472 → 1,704 B.
- **3191b065:** 8 B per cache entry, as the build measured.
- **d335159e (main, pixel frame upload):**
  - The per-frame sweep of `_uploaded` is 0 B.
  - A redraw with no new frame now makes no `SKImage` wrapper (before: one raster wrap per draw).
  - A new frame makes two wrappers (the raster wrap plus `ToTextureImage`).
  - It keeps one texture per source drawn in the frame and frees it at the end of the first frame that does not draw
    it. `SkiaRenderContext` 416 → 432 B. No objection.
- **bd3605ff (main, long scrollbar and canvas text lines):** no app uses `ScrollbarElement` or `DrawTextLine` (0 hits in
  apps/), so there is nothing in Meridian or Vesper's viewer to measure.
  - `DrawTextLine` goes through the label pipeline's bounded layout cache (one layout per new line, nothing kept beyond
    the cache).
  - `CanvasElement` 608 → 616 B, `ScrollbarElement` 624 B.
- **Main's others:**
  - 11554b8b: `ButtonElement` 640 → 664 B, `CheckboxElement` 592 → 624 B; parsing allocates nothing when mnemonics are
    off.
  - d9bf8e25: `ContextMenuElement` +16 B, `ContextMenuItem` +8 B.
  - `Window` +8 B, `FocusManager` +8 B.
  - 1951019d is event-driven: `ColorScheme` calls `SDL_GetSystemTheme` only when read, and no framework code reads it
    per frame. No idle wakes added.
  - `TextElement`, `BoxElement` and `ImageElement` are unchanged.

CANDIDATES:

- id: dotnet-aperture-picture-body-deadline
  what: Give each picture download one deadline that also covers the body read, and drop waits whose element left the
  window, so a stalled transfer can neither hold the cache's 8 slots forever nor keep every page visited after it.
  evidence:
  - **Code:** `FetchAsync` (PosterCache.cs:369–376) sends with `ResponseHeadersRead` and no token, then calls
    `ReadAsByteArrayAsync()` with no token. `HttpClient.Timeout` (20 s, PosterCache.cs:90) covers the send only.
  - **Probe with the app's own client, local TCP server, 8 stalled then 4 healthy pictures:**
    - Headers sent, then 100 bytes, then nothing: at 1, 5, 15, 25, 40 and 60 s, 12 in flight, 0 landed, 0 of 4 healthy
      pictures loaded.
    - No headers at all: all 8 fail at 20 s and the 4 healthy ones land.
  - **Headless Aperture (JIT, 160 films, the first 8 bodies stall), heap after full GC:**
    - 1.72 MB at cycle 0 → 34.15 MB after 100 open/back cycles (~0.33 MB a page) → 41.06 MB after 150 forward pages and
      Home.
    - 443 URLs stay in flight; 4,885 waits, 4,874 of them off the tree; 365 detached page tops holding 42,444 elements.
    - Clearing only the off-tree waits' elements: 41.06 → 2.18 MB (363 of 365 tops freed). The healthy run ends at
      1.81 MB.
  - **Same pattern elsewhere:** JunctionApp.Markdown.cs:595 reads a body with `CancellationToken.None` in `Sync()`, so a
    stall blocks a pool thread forever. Lower stakes.
  real app: Aperture browsing when a transfer goes quiet mid-body (Wi-Fi roam, VPN reconnect, sleep and resume on another
  network, a NAS dropping off). With no TCP keepalive, a receive-only connection never errors. One stall loses a slot
  and keeps one page. Eight stalls stop every picture for the rest of the session, and every page visited after that is
  kept. Expert 8 should measure on the real server how often this happens.
  expected: closes a leak (retained bar): about 0.33 MB of heap per page visited after a stall, and pictures come back
  within the deadline. A per-request `CancellationTokenSource` is about 0.1–0.2 KB per download. No change for healthy
  downloads.
  cost: small, Aperture only, no API, no pixels.
  - A failed body lands `Failed`, which already shows the placeholder.
  - Prune trap: a kept Home shelf's cards are not hosted while the user is on another page. Pruning their waits only
    means a card made again later, since `CardArt` equality already handles it.
  - Guard: a `PosterCache` test with a stuck body stream, and an `ArtworkLandingTests` case that leaves pages while
    pictures stall.
- id: dotnet-vesper-viewer-walk-cancel-collect
  what: Cancel Vesper's viewer and diff colour walk when the view closes or moves on, and run Junction's f7f2f6c5
  at-rest collection after a big walk.
  evidence:
  - **Code:** `StartViewerHighlight` (CommanderApp.Viewer.cs:307–326) and the diff (CommanderApp.DiffHighlight.cs:46–63)
    run `TokenizeLines` with no token. `_viewerHlKey` only drops the late result. A cancellable overload exists
    (SyntaxHighlighter.cs:198); the cap is 20,000 lines / 2 M characters.
  - **Headless JIT, fx\vbig (HultUI's own C#):**
    - 8,000 lines: the walk allocates 55.1 MB off the UI thread (6.9 KB a line) over ~955 ms.
    - At rest 3 s after closing: ws 118.2 MB, committed 30.6 MB. A forced compacting full GC: ws 111.7, committed 26.0.
      One aggressive collection: ws 92.2 (−26 MB, −22 %), committed 6.4, private 72.2 → 46.7.
    - Open then close after 150 ms: the walk runs on 44.5 MB in all (≈31 MB after the close, 437 ms more). A 3,300-line
      file 23.6 MB (187 ms more), 2,000 lines 10.7 MB.
  - **Real window (AOT, 2 launches, equal within 0.2 MB):**
    - Opening the 8,000-line file allocates 55.0 MB (all threads).
    - At rest 1, 5 and 10 s after closing: ws 133.4–133.6 MB (86.1 before the open), private 128–129.5, committed 30.7.
      GC counts stay frozen: nothing collects at rest.
    - Three 150 ms flips add 81.7 MB (headless 78.8), then ws rests at 138.2–143.3.
    - A forced compacting full GC: ws 135.4–139.9 MB. Only an aggressive collection gives memory back.
  - **Real window, 165 KB ELEMENTS.md, 30 open/close cycles:** ~11 MB on all threads per open (722 KB on the UI thread),
    heap flat at 9.8 MB, committed 34.9 MB at rest.
  real app: Vesper, Shift+F3 or F3 on a source file (an everyday commander action), and stepping through a folder of big
  files with Shift+F3 / Esc / Down.
  expected:
  - Retained bar: working set at rest after one 8,000-line view ~133.5 → ~107 MB (about −20 %, scaled from the headless
    −26 MB).
  - Churn and CPU: the walk after a close is not done (≈31 MB and ~0.4 s of a core for an 8,000-line file closed at
    150 ms; up to ~138 MB and ~2.4 s at the 20,000-line cap, an estimate).
  cost: small, Vesper only, no API, no pixels.
  - Reuse Junction's rule (≥ 2,000 lines or ≥ 32 MB allocated, then wait 2 s with no input).
  - A shared framework helper would be public API (ask the user).
  - Guard: a closed viewer's walk observes its token, one collection after a big walk, none after a small one.

REAL WINDOW:

- **Holds** (lock `Global\HultUI-r19-desktop`), desk: the 1440p only (DISPLAY1, 2560×1440 at 100 %, DPI 96, 200 Hz,
  primary), Steam running, timer 1 ms, idle gate passed before each hold:
  - Hold 1: 09:08:45–09:10:50 local, waited 70.5 s, held 124.8 s, 3 launches.
  - Hold 2: 09:18:20–09:19:15, waited 151.0 s, held 55.1 s, 2 launches.
  - Windows: Vesper 1256×859, Junction 1456×939. All exited 0 on WM_CLOSE; 0 foreground takes, 0 refused activations,
    0 nvlddmkm events, 0 screen reads.
- **What headless did not show:**
  - Vesper rests with GC counts frozen for 10 s after a walk, so the +47 MB of working set stays (candidate 2).
  - With a real UIA client, 30 palette closes go through the disconnect thread with no retention (REVIEW).
- **Steady-state frames on AOT:** the Junction palette's open tween frames are 0 B each. A Down in the palette is 144 B
  and 1 frame; a close is 1 frame of 0.27 KB (1.08 KB with the reader client). A rest after Vesper's viewer draws 0
  frames.

WAITING, RE-RANKED:

- findbar-countonscreen-foreach: still a nit. FindBar.cs is untouched since round 18, and line 212 still loops
  `foreach` over `bar.Children` (32 B on AOT).
- textarea-chunked-document: stays parked. 3442c3f5 touched only the TextArea's own menu and bd3605ff only moved
  scrollbar painting (same pixels); the user's 256k-character condition is still unmet.
- fedora-local-dotnet: keep waiting. On 2026-11-24 `updates` still has 11.0.100~rc.3 and `updates-testing` the final
  11.0.100-1.fc44; the installed rpm is dotnet-sdk-10.0-10.0.112; both links still point to /usr/local/share/dotnet
  (11.0.100). Read-only check; I did not terminate WSL.
- aperture-postercache-slot-stall: the code side is answered, see candidate 1.
  - A slow header frees its slot at 20 s; a stalled body never does.
  - Since d7eedc4b a stall also keeps every page visited after it.
  - A separate hero lane would not help when all 8 slots are stuck. The deadline is the fix; expert 8 measures how often
    it happens.
- reconcile-keyed-match-alloc (expert 6): agreed, ~0.3 KB.
  - On AOT, `ReconcileKeyed` (TreeDiffer.cs:207) allocates a `Dictionary<string, Element>` (80 B plus buckets and 24 B
    entries: ~216 B for 3 keys, ~330 B for 7) and a `List<Element>(n)`, plus `ToArray` only when the children changed.
  - Zero-allocation shape for n ≤ 8: a linear key scan instead of the dictionary, and compare matches with
    `parent.Children[i]` as they come, allocating the result array only at the first difference.
  - A `[ThreadStatic]` pooled dictionary would be corrupted by the recursion through `Patch`, so it needs a per-depth
    pool.

EXAMINED, NOT WORTH IT:

- **PosterCache's LRU:** `Touch` is a `LinkedList.Remove(string)` scan over up to 400 entries plus a 48 B node per `Get`
  of a cached picture. Since d7eedc4b, `Get` runs per card made, not per kept card: about 60 a library open, 2.9 KB of
  ~600 KB (0.5 %), tens of µs.
- **The UIA worker's O(n²) take:** off the UI thread, n = 13–14.
- **Button +24 B, Checkbox +32 B:** about 1 KB per tree of 50 buttons.
- **Vesper viewer open and close bytes on the UI thread:** 631–716 KB to open, 267 KB to close, the same order as round
  14's ~1 MB. Already covered by expert 5's text items.

NEEDS THE USER:

- Only if candidate 2's at-rest collection becomes a shared framework helper (public API, e.g. on `Application`) rather
  than Vesper-internal code like Junction's.

FILES (in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\e91920b9-3793-496a-9a73-f017250d8772\scratchpad\r19-dotnet\`):

- `stall\` (src\Program.cs, proj\Stall.csproj): `run-stall.txt`, `run-slowhead.txt`.
- `aperture\` (src\Program.cs, proj\ApProbe.csproj, assembly named Aperture.Tests): `run-art100.txt`,
  `run-stall8-100.txt`, and `run-stall8-100b.txt` (has the cleared-waits line).
- `vwalk\` (src\Program.cs, proj\VWalk.csproj): `run1.txt`; fixtures in `fx\vbig\`.
- `sizes\`: `sizes-56d9a3f6.tsv` and `diff-aa3a3696-56d9a3f6.txt`, from round 18's probe re-pointed.
- `rw\`:
  - harness: `common.ps1`, `batch-r1.ps1`, `batch-r2.ps1`, `frames.py`, `Driver*.cs`;
  - logs: `batch-r1.txt`, `batch-r2.txt`, `runs\r1-*`, `runs\r2-*` (out, frames.csv, uia.txt).
- Also: `locks.log`, `gate.log`, `fedora-check.sh`, `fedora-check.txt`; fixtures `fx\jrepo`, `fx\viewer`.
