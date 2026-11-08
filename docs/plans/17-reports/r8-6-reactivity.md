# Round 8 survey, expert 6: Reactivity and reconcile

SATISFIED: no

Measured at 27020cdb, detached. Headless, JIT, Release, pinned to P-cores, a key and two frames per step. Bytes come from `GetAllocatedBytesForCurrentThread`, split into round 7's buckets (`apply8.py`), plus app buckets for Home, the picture lookup and the Changes panes. No timings. Prototypes were gated by environment variables. Everything was reversed with `git apply -R final8.patch` and the probe file deleted; the worktree is clean.

REVIEW: none.
- **bdd8cd1c `KeptElements` is correct.** It checks "only from the build before" by build number. `Made` keeps one `Ref` per key, `Prune` runs when the count doubles, and `Live` checks the latest build. 31e1aaa7 holds: Home Right and Down cost 0.3 KB a key with 0 builds.
- **ca4a0079 moved rows.** `TakeMovingRow` is O(moving rows) per realized row, at most the rows on screen. `_moving` is made once and kept.
- **b6119ae9 and 95c86c97 hold.**
- **f64e4674 (app-support).** No Components, Reactivity or TreeDiffer change. The frame guard is a struct. `Application.Switch` sets an `AsyncLocal` once per application, and `InvokeOnMainThread` reads it, which allocates nothing (code read).

CANDIDATES:
- id: apps-coalesce-landing-builds
  what: A picture landing posts one build at a time, not one per picture. Fix the callers of Aperture's `PosterCache` and Vesper's `ThumbnailCache` with an `Interlocked` flag, as Vesper's find already does (`FindRepaint`).
  evidence:
  - Cause: `DrainMainThreadQueue` (Application.cs:1107) has no batch, and `MarkDirty` rebuilds at once (ComponentHost.cs:67–88). So N landings in one drain make N builds. Both caches' comments say the caller coalesces (PosterCache.cs:246, ThumbnailCache.cs:109); neither caller does (ApertureApp.cs:107, CommanderApp.Views.cs:52).
  - Probe: a PNG served after 15–60 ms, counted from opening the view until every picture landed.
  - Aperture Home, 36 posters: app builds 41 → 16–22, 3,590 → 1,896–2,290 KB (−36…−47 %), at most 8–9 builds in one frame → 2–5.
  - Aperture grid, 42 posters: builds 46 → 16, 2,424 → 1,515 KB (−37 %), at most 10 builds in a frame → 3.
  - Vesper Thumbnails, 160 PNGs (97 shown): builds 45 → 2–5, 1,451–1,521 → 790–918 KB (−40…−46 %), at most 27–28 builds in one frame → 1.
  - Pictures drawn at the end are the same (55 on Home, 97 in Thumbnails).
  expected: churn −36…−47 % on every view that loads pictures; at most one build per frame instead of up to 28.
  cost: small, app (2 sites). No API change, same pixels. Guideline for REACTIVITY.md: a background callback that fires once per item posts one build at a time.
- id: aperture-grid-cursor-no-build
  what: The library grid's cursor key moves the ring on the live cards, as Home does. Put the grid's cards on `KeptElements` and use `Live`. It builds only when the target card is not realized or the move is within a page of the loaded end.
  evidence:
  - Prototype in `MoveRingWithoutBuild` (Cursor.cs:168), reading the grid's live cards.
  - 600 films: Right 49.4 → 0.3 KB (−99 %); Down 75.7 → 23.2 KB (−69 %; what is left is the rows scrolling in).
  - With UIA: Right 49.4 → 0.3, Down 77.5 → 24.9, 0 trees.
  - Builds a key 1 → 0. The ring was on the cursor's card after 7 keys and after a build.
  - It also fixes the Waiting lead: today the cursor card never takes focus (focused element empty over 7 keys); with the prototype, focus follows the cursor, as Cursor.cs:17 says it should.
  expected: churn −99 % (Right), −69 % (Down).
  cost: small–medium, app. Same pixels; a reader now follows the cursor (the documented intent).
- id: vesper-cursor-no-build
  what: A plain cursor key builds nothing.
  - Detail: set the mounted list's `SelectedIndex` through its ref and scroll, as the palette does.
  - Brief and Thumbnails: restyle the two live cells or tiles (border, background) and set `CursorRowRef`, with `KeptElements.Live`.
  - Build as today on a Shift sweep, zebra, a drag hover, an overlay or full shell, a cleared filter, or a partner row in the other pane.
  evidence: prototype in `MoveCursor` (CommanderApp.cs:1170), 400 files, 1240×820.
  - Detail Down 17.3 → 2.2 KB (−87 %); with UIA 17.7 → 2.6.
  - Brief Down 31.8 → 0.5, Right 32.3 → 0.5 (−98 %).
  - Thumbnails Right 27.7 → 0.5, Down on screen 31.3 → 0.5, a 30-key Down walk that scrolls 57.9 → 1.9 (−97 %).
  - Builds 1 → 0, 0 trees. Rings matched the cursor headlessly, after 12 Down and 12 Up and after a build.
  - Detail's dispatch bucket rose 0.5 → 1.4 KB from my partner check (`DiffTargetPath` + `IndexOfPath`); comparing names would avoid it.
  - Trap: `ThumbRow` reads `item.Cursor` (Views.cs:279). A row remade from the last build's items would ring the old tile, so it should read the live cursor. Not seen in the walk.
  expected: churn −87…−98 % on Vesper's most used keys.
  cost: medium, app; same pixels and reader tree.
- id: aperture-home-build-diet
  what: Two changes to the Home build, which now runs mostly when pictures land.
  - (a) Hand back a whole shelf, as a `KeptElements` keyed by the row, when its heading inputs and every card on its page are unchanged.
  - (b) Memoize each card's picture `Uri` by item, kind, pixels and scale.
  evidence: a Home build with every card kept, 6 shelves and 36 cards: 69.6 KB.
  - homeView 43.2 KB: 46 elements made, 39 of them the shelves' headings and rows. This is the "~33 KB a shelf" left open: about 7 KB a shelf in elements plus the pictures.
  - Pictures 20.3 KB: 577 B a card on the fake library. The real `JellyfinLibrary.ImageUrl` (JellyfinLibrary.cs:272), plus `ToString`, costs 1,136 B a card in a microbench; memoized, 2 B.
  - Reconcile 2.7 KB.
  - Prototype (a): a build 69.6 → 30.6 KB, elements 46 → 4; Home open with landings coalesced 1,897 → 1,583 KB.
  - (b) adds −20 KB a build, more on a real server.
  expected: churn −56 % a Home build with (a), about −86 % with (a)+(b); Home open −17 % with (a) alone.
  cost: (a) medium, app. Heading closures capture `row` and `all`, so the state must cover what the menu shows. (b) small.
- id: aperture-home-cursor-turns-page (bug)
  what: The cursor can walk onto a card on another page of its shelf. No card then shows the ring, and focus stays behind. Turn the shelf's page (`_rowPages`) to the cursor's page and build.
  evidence: at 1000 px, 4 cards a page: cursors 5–7 show 0 ringed cards and focus stays on card 4; at 1440 px all 7 fit.
  expected: correctness; one Home build per page crossing.
  cost: small, app.

WAITING, RE-RANKED:
- vesper-coalesce-markdirty: drop, as in round 7. A folder change is 1 app build in the dispatch plus 1 when the listing lands, and 5 part builds: no double. Posted landings are where many builds pile up in one frame; that is fixed in the apps (apps-coalesce-landing-builds), with no framework change.
- junction-leads (remaining): my part is closed. `ChangesView`'s lists during a stage landed in 95c86c97 (rows made 62 → 5). Tests, old-side state and gen 2 are not reactivity.
- aperture-leads (remaining): "the grid cursor never takes focus" is confirmed and fixed by aperture-grid-cursor-no-build. Discover (needs Seerr) and toasts (one-off) are still not measured.
- junction-changes-leads (shared): caret Down at 300 files with 30-line files costs 376.2 KB a key.
  - render 328.8: the diff lines made, 359 elements a key, about 11 per line, 332 unmounted.
  - The diff pane's build 25.1; the rest of the view build 13.4 (app 6.6, list column 3.4, reconcile 3.4).
  - Making the diff pane a `Part` saves at most 13 KB: −3.5 % here, at most −18 % on expert 8's 74 KB case. Below the bar.
  - The lever is the diff line row's element count (expert 1/8). The reader tree per stage is expert 7's.
- Step 11 left-open, "Brief, Thumbnails and Aperture's grid onto `KeptElements`", the number each would save:
  - The helper alone: about 0 KB a key. Each already hands back unchanged cells, tiles and cards; a key makes 2, and reusing a `Ref` per key saves about 24 B per element made. Code: about 25–30 hand-rolled lines each.
  - With `Live` for a key that builds nothing: Brief −98 %, Thumbnails −97…−98 %, grid −69…−99 % (the two candidates above).

EXAMINED, NOT WORTH IT:
- Batching the main-thread drain, or coalescing `MarkDirty` while it runs (framework): an await continuation that writes and then reads the tree would see the old tree, in 5 apps. The two app sites get the same win.
- Junction Changes diff pane as its own `Part`: at most 13 of 376 KB (−3.5 %).
- Keeping Brief's columns: 4.5 columns and about 2.6 KB of reconcile a key, gone once cursor keys build nothing; marks already cost 35.7 KB.
- KeptElements / VirtualList moves / app-support: see REVIEW.

NEEDS THE USER:
- none.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\64e7e0e2-bc1c-4069-9d42-9311fee04c9e\scratchpad\r8-reactivity\`
- Probe: `apply8.py`, `R6Probe.cs`.
- Driver: `drv\Program.cs` and `drv\R8Drv.csproj`. New modes: ahome8, agrid8, vthumbland, jchanges, urlbench, plus Brief and Thumbnails walks and ring checks.
- `final8.patch`: probe and prototypes, gated by R8_COALESCE, R8_NOBUILD, R8_GRID, R8_SHELVES.
- Outputs: `run-ahome8*.txt`, `run-agrid8*.txt`, `run-vthumbland-c*.txt`, `run-vdetail*.txt`, `run-vbrief-nb*.txt`, `run-vthumbs-nb*.txt`, `run-vesper-*.txt`, `run-vnav.txt`, `run-jchanges.txt`.
- App-support diffs: `appsupport-*.diff`.
