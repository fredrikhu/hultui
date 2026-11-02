# Round 6 survey: 8. App workloads

I measured **ef4288ad** (round 5 released; its code is f31194ab's) against **643eb07b** (round 5's start). Both are NativeAOT publishes of the total comparison's headless walk host, extended. The base tree is a `git archive` extract in the scratchpad. Each tree carries the same probe: bytes and UI-thread cycles per frame phase, top-level app build and reconcile bytes, builds by component type, VirtualList rows made, layout visits, measure misses, a11y trees and bridge applies, and Vesper free-space queries.

The run covers 33 walks over the five apps. The rich text editor is walked as Junction's review composer. Base and after alternate, 2 reps each, pinned to the P-cores, with no idle gate (bytes and counts only). Bytes per key agree between reps within 0.1 %, except base `transfer-frames` at 9.5 % (copy timing).

Start-up and working-set shares come from the total comparison's after real-window logs, which ran the same code. Probes and data are in the scratchpad `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\855f4ddd-feda-4f0a-a595-a83e07af08f8\scratchpad\r6-apps\`:
- probe edits: `probe6.py`, `probe6b.py` (`probe-final.patch`)
- walk host: `walk\src\Program.cs`
- scripts: `run.ps1`, `batch.ps1`, `cmp.py`, `debugp90.py`, `startup_shares.py`
- data: `runs\walks.jsonl`, `cmp-all.txt`, `startup-shares.txt`

**Real windows opened: 0, so no guard events. Jellyfin sign-ins: 0** (Aperture ran on `FakeMediaLibrary`). The worktree is clean at ef4288ad (probe removed with `git apply -R`). The localhost server on port 8767 for Meridian's response is stopped. The 1.5 GB transfer files are deleted.

SATISFIED: no

## REVIEW: none. Round 5 helped every app walk, and none got worse.

UI-thread KB per key, AOT, 643eb07b → ef4288ad. Over all 33 walks the sum is 16,266 → 6,712 (−59 %), geometric-mean ratio 0.41.

| App | Walks |
|---|---|
| Junction | History 120 → 60 (−50 %; build 57 → 17, handlers 18.9 → 3.2); History with a reader at 500 ms 222 → 66 (−70 %; a11y trees per key 1.0 → 0); Commit file walk on a 415-file commit 2,654 → 556 (−79 %; layout visits 550 → 147); Tags 108 → 55 (−49 %; rows made 2.7 → 0.7); Files 96 → 86 (−10 %); summary typing 92 → 74 (with a reader 124 → 75; laid out 18 → 0, trees 1 → 0); review composer 1,356 → 155 (−89 %; builds per key 5 → 1) |
| Vesper | Detail 269 → 43 (−84 %; with a reader trees 0.5 → 0); partner 291 → 58; Brief 241 → 34; Thumbnails 278 → 61; Insert 765 → 287 (builds 2 → 1); type-to-find 319 → 111; Tab 264 → 120; folder in/out 919 → 315; reader PageDown 309 → 268 (−13 %); transfer 96 → 68 KB per frame pumped; free-space queries per Detail key 2 → 0 |
| Meridian | URL 274 → 69 (−75 %); History 380 → 53 (−86 %; rows made 32 → 0.5); History search 174 → 48 (builds 2 → 1); find typing 699 → 430 (GCs 9/9/8 → 2/0/0); find Enter 1,023 → 440 (builds 2 → 1); 89k-character body typing 2,001 → 1,795 (gen-2 24.5 → 0 per 100 keys); method pick 822 → 554; palette over 200 requests with a reader 215 → 207, now telling 1 patch per key (the base told the reader nothing) |
| Aperture | grid Down 199 → 182, Right 124 → 112, resize step 82 → 72 |
| Reel | 0.8 → 0.8 KB per frame |

Each round 5 Landed row shows up in an app walk where it should: 6388f868, ce70fec7, 8157c282, 574f43a7, ff6262ff, fcf366c6, b1a12ef1, fe50f969, 54eded98, 57e954ca, ea6ceb7a, 828a7d69, the in-place a11y refresh and the live overlay tree. The 91d48601 trade (scrolling pays first draws) still leaves the reader's PageDown 13 % lower. The GL and Vulkan rows need real windows; expert 3 measured them.

## Shares (after; UI bytes per key)

- **Junction:**
  - History: render 63 % (2.8 rows made per key, first draws), build 31 %.
  - Commit file walk: build 80 %.
  - Review with a reader: build 45 %, render 26 %, a11y 28 %.
- **Vesper:**
  - Detail: render 48 %, build 38 %, handlers and posted work 14 %.
  - Insert: render 89 %.
  - Transfer: build 98 %.
- **Meridian:**
  - URL, find, method pick: build 94–100 %.
  - Body typing: handler 77 %, build 23 %.
- **Aperture:**
  - Down: render 62 %.
  - Resize: build 91 %.
- **Headless UI CPU:** render plus overlay 69–98 % on every walk. This is CPU raster, so bytes and counts carry over to GPU windows but CPU shares do not.
- **Start-up** (median of 5):
  - GL apps: before `SDL_Init` 10–11 %, `SDL_CreateWindow` 64–68 %, first frame 18–24 %.
  - Vulkan apps: `SDL_CreateWindow` 22 %, Vulkan instance and device 31–32 %, first frame 32 %.
- **Memory:**
  - At idle: GC committed is 2–9 % of private bytes; native code and drivers are 81–98 %.
  - After a walk: Junction's GC committed is 39–43 % of private (the highlight worker); Vesper and Meridian 3–9 %.

## CANDIDATES

- id: apps-junction-highlight-prefetch (owner 5/8)
  what: Tokenize the highlight only up to the last line the hunks show, or after the selection rests.
  evidence: History allocates 9,826 KB per key on all threads against 60 KB on the UI thread (99.4 % on the worker), with 59/16/1.5 GCs (gen 0/1/2) per 100 keys. The Commit file walk allocates 6,996 KB per key on all threads against 556 on the UI thread. Round 5 left it flat (base 9,942).
  expected: −50…−90 % of all-thread bytes per key, and most of the walk's GC pause (memory bar).
  cost: medium; colours may land a moment later.
- id: meridian-body-edit-in-place (owner 6/8; replaces meridian-body-edit-bytes)
  what: A body edit replaces the body block in `ReqDocument` in place, instead of re-splitting the body into lines and reparsing the whole document on every keystroke (`ReqDocument.SetBody` at `ReqDocument.cs:229–255`, then `Touch` → `Reparse`).
  evidence: An 89k-character body keystroke costs 1,795 KB. `SetBody` measured alone costs 1,205 KB per call (67 %). The TextArea's own new string is ~175 KB (~10 %). The view build adds 402 KB (22 %, not traced; `Serialize()` alone is 350 KB). The cost is O(document) per keystroke.
  expected: about −67 % per keystroke at 89k characters (bytes and scaling bars).
  cost: small–medium, app only.
- id: meridian-find-typing (owner 5/8; meridian-response-find-step folded in)
  what: Search tints become a second, small range list the TextArea draws over its spans, or the app merges them per visible line.
  evidence: 430 KB per find character and 440 KB per Enter. 94–97 % of that is the response pane part's build: `DecorateResponseBody` copies and sorts every syntax span on each query or current-match change (`MeridianApp.Response.cs:500–508`).
  expected: −85…−90 % per character and per Enter (bytes bar).
  cost: medium; a TextArea range list would be an additive API.
- id: vesper-marks-in-row-shape (owner 6/8; keep)
  what: Marks leave the rows' shape, so a mark stops remaking every visible row.
  evidence: Insert costs 287 KB per key, render 255 KB (89 %), with 29.9 rows remade per key. A Detail key costs 43 KB.
  expected: Insert about −80 % (bytes bar).
  cost: small–medium, app only.
- id: vesper-transfer-progress-rebuilds (owner 6/8; keep)
  what: Transfer progress goes through the footer or progress part, not `Application.InvokeOnMainThread(MarkDirty)` every 40 ms (`CommanderApp.cs:2060–2066`).
  evidence: Each repaint costs about 185 KB: one CommanderApp build, 9 part builds and 2 free-space queries. At 25 repaints a second that is ~4.6 MB/s on the UI thread for the whole copy.
  expected: about −90 % per repaint (per-frame bar).
  cost: small, app only.
- id: junction-review-composer-row-kept (owner 6/7; new)
  what: Typing keeps the composer's row unless its height changes. Today `DraftEdited` remakes it on every keystroke (`JunctionApp.Review.cs:498`).
  evidence: 1 row made per key. Build 92 KB, render 56 KB (first draws of the new row). With a reader, 1.0 a11y trees per key and 60 KB of a11y work, 28 % of 215.
  expected: 155 → ~40 KB per key, and with a reader 215 → ~45 with trees 1 → 0 (bytes bar).
  cost: medium, app only. An a11y patch for one remade VirtualList row would be expert 7's alternative.
- id: junction-commit-diff-flatten (owner 6/8; new evidence for junction-commit-file-switch)
  what: Flatten a newly selected file's diff lazily (per hunk, or row heights from line counts) instead of the whole file.
  evidence: A file key on the 415-file commit 150dcf7f costs 556 KB, of which build is 439 (79 %), with 0.88 whole-file flattens per key while ~13 rows show.
  expected: −60…−70 % per file key; O(file lines) → O(visible rows) (scaling bar).
  cost: medium.
- id: meridian-method-popover-double-build (owner 6/8; confirmed)
  what: The popover row handler stops calling `MarkDirty` after an action that already builds.
  evidence: `MeridianApp.Requests.cs:750` with `SetMethod` at `:388`. Open plus pick costs 3 app builds and 8 part builds, 554 KB. The same row handler serves the environment and assertion-operator popovers.
  expected: about −185 KB (one app build) per pick, about −33 % of open plus pick.
  cost: small.
- id: vesper-coalesce-markdirty (owner 6/8; keep)
  what: Fold Vesper's one-off double builds through one signal write.
  evidence: Each folder Enter or Backspace costs 3 app builds and 5 part builds (build 126 of 315 KB).
  expected: about −25 % per folder change.
  cost: small–medium.
- id: apps-aperture-components (owner 6/8; keep)
  what: Build only when the column count changes; make the rail and title bar parts.
  evidence: A 4 px resize step costs 72 KB, 1 app build, 91 % of it build. Down costs 182 KB (build 38 %).
  expected: resize −85 %; Down −20…−30 %.
  cost: medium.
- id: junction-changes-composer (owner 6/8; keep)
  what: The summary keystroke builds only the composer.
  evidence: 74 KB per character, 97 % of it one JunctionApp view build.
  expected: −60…−70 %.
  cost: medium.
- id: palette-row-keys (owner 6; re-measured)
  what: Key the palette's rows by command.
  evidence: The Meridian palette over 200 requests costs 207 KB per key, 86 % of it the palette's build (179 KB).
  expected: −50 % or more.
  cost: small.
- id: vesper-free-label-per-build (owner 6; lower priority now)
  what: Cache the free-space label per folder.
  evidence: The parts cut the calls to 0 per Detail key. Still 1 `DriveInfo` query per Insert or type-to-find key, 2 per folder change and 2 per transfer repaint, all on the UI thread.
  expected: removes a disk or network round trip per key on network drives.
  cost: small.
- id: vesper-key-rest-bytes (owner 6; trace first)
  what: Find what Vesper allocates per key outside the build and the frame.
  evidence: 6.2 KB per key in every Vesper walk, 14 % of a Detail key. Junction allocates 3.2 KB there, Meridian 0.6. Flat since 643eb07b. Probably the cursor's preview load.
  expected: up to −14 % per Detail key.
  cost: unknown.
- id: suite-app-patterns (owner 8)
  what: Add perf-suite scenarios for app patterns the suite misses.
  evidence: Still open: document-switch (Junction Files 86 KB/key) and image-grid (Aperture 182 KB/key, render 62 %). New:
    - a worker posting `MarkDirty` every 40 ms over kept parts (Vesper transfer);
    - find typing and Enter in a 2,000-line highlighted TextArea (Meridian; the suite only opens, wheels and pages highlighted JSON);
    - typing in a rich text editor inside a VirtualList row with the bridge on (review composer: 1 tree per key).
  expected: pins the candidates above.
  cost: small each.

## EXAMINED, NOT WORTH IT

- **meridian-history-aot-p90 (closed):** the slow stretch of keys shows in both 643eb07b and ef4288ad, at keys 51–66 in rep 1 and 29–42 in rep 2. Bytes, rows made, measure misses, layout visits and builds are identical inside and outside it. It is timing from the machine, not work in the code.
- **apps-tokenize-on-ui-thread (closed):** no tokenizer share on the UI thread in History, Files or Commit. UI bytes are build plus render; the tokenizer's bytes are on the worker.
- **Vesper reader PageDown:** 268 KB per page, all render (32.8 lines made per page, first draws). This is round 5's accepted trade, and it is still 13 % lower than at 643eb07b.
- **Palette CPU outside the frame's phases (57 %, both trees):** the overlay's CPU raster (`RenderOverlays` runs outside the Render scope), not layout.
- **Start-up and working set:** no new lever. Native code and drivers are 81–98 % of private bytes at idle.

## NEEDS THE USER

- **Round 5 question 5 (Junction History row selection pixels), new numbers:** a History key is 60 KB, of which render is 38 KB (63 %), from 2.8 rows remade per key. A list-drawn highlight would remove most of that: about −50 % per key, more than the survey's −30 %.
- **textarea-chunked-document (option B):** on the 89k-character body the TextArea's own string is only ~10 % of a keystroke, and Meridian's `SetBody` is 67 %. Build `meridian-body-edit-in-place` first, then decide whether the chunked document is still wanted.
- **Reminder:** no Jellyfin sign-ins this round. The earlier "aperture-survey" devices and the API key should still be removed and revoked when the work is done.
