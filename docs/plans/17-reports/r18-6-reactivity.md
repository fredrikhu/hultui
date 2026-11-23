SATISFIED: no

REVIEW:
- **0f9c8477 (Aperture history and keyboard): one objection.** Back, Forward, Escape or a rail reopen to a scrolled library grid lands at the top.
  - Measured (headless, 300 films, 1440×900): after 10 Downs and Enter, Back gives scrollY 0 (it was 2674 before Enter). The cursor card m070 is not made and focus goes nowhere.
  - The first key after that builds and makes 289 elements (279.1 KB). The second key builds again (75.6 KB, 72 made).
  - Mouse users (cursor −1) always land at 0: wheeled to 2400, clicked a card, pressed the mouse back button, and the grid was at 0.
  - Cause: `LetGoOfGrid` (b837de41) empties `_gridList` when another view shows. `RevealGridCursor` then has no list to scroll, and the new list opens at 0.
  - The commit message and History.cs both promise Back puts you "back where you were scrolled". → first candidate below.
- **0f9c8477, the rest: nothing measurable.** Grid and Home cursor keys still build nothing (0.4 KB a key). The roving Tab stop remakes card 0 once. `Pressable` adds one static delegate per box. `PatchFrom` copies one more flag.
- **ee044d36, f67b25ec, 93f8a26d (New seasons): no objection.** The season look makes one whole-app build every 30 minutes (the first after 30 s), plus one when the row changes. `EnsureSeasonWatch` returns before allocating anything on each build.
- **b3bb93bd, 3ea96588, 6751a51a, 34606bab: no objection (read, not re-measured; the Landed rows stand).**
  - b3bb93bd: the recolour walks only the labels on screen.
  - 3ea96588: the tab parts and their `Computed`s are made once and kept, so nothing leaks.
  - 34606bab: the hash is O(text) per event, small beside the git status the event already runs.
- **3490ba67 (note for experts 4 and 8):** each Changes load now takes two index prints, O(index entries). That is small beside git status.

CANDIDATES:
- id: reactivity-aperture-back-lands-where-it-was
  what: A page reached by Back, Forward or the rail opens its library grid where the visit left it: the scroll offset kept in the `Visit`, or failing that the cursor's row. The cursor's card is then made, ringed and focused in the Back's own frame.
  evidence:
    - Prototype PROBE_REVEAL (the new list gets `ScrollRangeIntoView` of the cursor row): Back lands at 2674 with card-m070 focused. The Back itself costs the same (338.4 KB vs 340.8, 336 made).
    - The next two keys drop from 279.1 + 75.6 KB (2 builds, 361 made) to 0.4 + 0.4 KB (0 builds).
    - Prototype PROBE_SCROLLKEEP (`Leave` saves the grid's `ScrollOffsetY`; the new list's held offset restores it): the mouse case lands at 2400, not 0.
    - The real window shows the same (see REAL WINDOW).
  real app: Aperture, Back, Escape or the mouse back button after opening a film below a library's first screen; Forward; a library reopened from the rail. This is the core browse loop, tens of times a session.
  expected: churn: −354 KB, −2 builds and −361 elements on the keys after each such Back (the first key −99.8 %). Correctness: Back shows the card that was opened.
  cost: small, app only (ApertureApp.History.cs `Visit`, `BuildLibraryView`), no API. Where Back lands changes visibly, as already documented (see NEEDS THE USER). Add a 300-film case to ApertureAppHostTests.History.
- id: reactivity-aperture-parts-build-on-change
  what: The title bar and the rail build only when what they show changes, not on every `Rerender` and every load that lands. The title bar shows the server and user; the rail shows the lit button, libraries, request doors and badges. Later, give each rail button its own part lit by a `Computed`, as Junction's tabs (3ea96588).
  evidence:
    - Parts.cs is "safe by default": `Rerender`, `MarkDirty` and every build not marked view-only rebuild both parts, 17.6 + 31.3 = 49 KB each time.
    - Title-bar and rail builds per action today:

      | Action | Title + rail builds |
      |---|---|
      | Library first open | 4 + 4 |
      | Enter (a film page) | 2 + 2 |
      | Back, Forward, rail | 1 + 1 |
      | Paging build while wheeling | 2 + 2 (a 237.6 KB step) |

    - A Home rebuild that keeps its cards is 12.1 KB view-only against 60.8 KB full, so the parts are 80 % of it.
    - Prototype PROBE_PARTSHAPE (`Build` compares a tuple of what each part reads; the title bar gets its own `PartGroup`), headless:

      | Action | Before → after (KB) | Change |
      |---|---|---|
      | Enter | 167.4 → 100.7 | −40 % |
      | Library first open | 784.6 → 620.6 | −21 % |
      | Forward to a film | 78.6 → 61.1 | −22 % |
      | Back, Escape, rail | −17.5 KB | −5 % |

      Part builds: Enter 4 → 1, first open 8 → 1.
    - Real window: Enter part builds 4 → 1, first open 8 → 1.
    - Not prototyped: per-button rail parts. The rail is about 3.3 KB a button (31.3 KB for 9 buttons), so Enter would go to about 73 KB (−56 %) and Forward to about 33 KB (−58 %).
    - The prototype fails 13 of 120 PartTests and Aperture host tests. Most fail through the oracle's forced full build, which goes through `MarkDirty`. At least 2 show a stale rail after sign-out. A real build must keep that oracle green.
  real app: Aperture, every page open, Back and Forward, and every load landing on a page (a film page lands 2 loads; a library 3, plus 1 per page of 100).
  expected: churn bar: −40 % a film page open, −21 % a library first open, −22 % Forward; about −56…−58 % with per-button parts.
  cost: small–medium, app only (ApertureApp.Parts.cs, `BuildRail`). No API; pixels pinned by the PartTests oracle.
- id: reactivity-aperture-poster-lands-in-place
  what: A poster that lands is put into the live cards that show it, by setting the image's Source, as `RecolourKeptRows` does for Junction's colours. Today each landing triggers a view build that makes those cards again.
  evidence:
    - Headless, in-process PNG posters (15 ms) against 404s:
      - First library open: 1,263.0 KB, 12 builds, 1,039 made, 9 landings, against 787.6 KB, 4 builds, 576 made (+475 KB, +60 %).
      - Each Down that brings a new row: 149.8–169.1 KB and 126–136 made against 102.4–135.1 KB and 84–96 made, each with a 32 KB view build for its landing.
      - A failed fetch (404) still costs a 32 KB view build that changes nothing.
    - Real window: first open 1,237.6–1,638.0 KB with 14–21 builds; ten Downs 1,178–1,398 KB with 11–18 builds.
  real app: Aperture, the first view of every poster in a session (library opens, scrolling a library, Home, Discover). The poster cache is in memory only.
  expected: churn: a library's first open about −45 % (−475 KB, builds 12 → 4); each new row −45…−50 %.
  cost: medium, app side. `Artwork` keeps an Image with a null source in place of the glyph swap, the picture leaves the kept state, and a landing finds the live cards by URL. `ImageElement.Source` marks layout dirty, so check the re-measure. Pixels equal once landed; measure first.
- id: reactivity-aperture-keep-the-page-back-returns-to (lead)
  what: Keep the one page Back returns to (Home or a library grid) hosted but hidden, as a browser keeps a page, instead of making it again.
  evidence:
    - Back to Home makes all 323 elements again (322 KB). A Home rebuild that keeps its cards is 12.1 KB.
    - Back to a grid: 336 made (338 KB) against 31.9 KB kept.
    - `KeptElements` are weak and require `IsHosted`, so nothing survives the other page.
  real app: Back to Home or a library from a film page, the most common Back.
  expected: churn: Back about −80…−90 %. It retains about 330 elements (about 0.3 MB) while away.
  cost: large (views as kept parts), no API. Needs the user (below).

REAL WINDOW:
- **Setup:** 1 hold, 4 launches alternating base and new (PROBE_PARTSHAPE + PROBE_REVEAL).
  - The host: NativeAOT from this worktree at aa3a3696 plus the probes, Aperture over a FakeMediaLibrary (300 films), posters answered in-process (a PNG after 15 ms).
  - The scene: Home, Films by key 2, 10 Downs, Enter, Alt+Left, 2 Rights, Alt+Right, Escape, Home, Films again, Right, Enter, Alt+Left.
- **Desk:** the 4K at 150 % (window DPI 144, 2160×1350 px drawable), 120 Hz, Vulkan Mailbox, HDR on; the 1440p at 100 %, 120 Hz, to its left. Timer resolution 1 ms (Steam).
- **Hold:** 10:53:48–10:56:03; waited 321.5 s for the lock, held 134.4 s. 0 foreground takes, 0 refused activations, 0 nvlddmkm events. The idle gate passed before the hold (CPU 3 %).
- **What it showed that headless did not:**
  - Real posters landing dominate a library's first open: 14–21 builds and 997–1,371 elements, against 4 builds and 576 headless with 404s.
  - A late load landed after the second Back. In base it rebuilt the parts again and, now that the list was hosted, revealed the cursor: the grid was made twice (672 made, 720.5 KB, against 411.1 KB new).
- **Otherwise equal to headless** (each step includes about 42 KB for the probe's own line): Back about 332 KB (headless 338), the first key after Back about 275 KB (headless 279), a key without a build 0.4 KB. Repeat launches matched within 0.1 KB.
- **With the probes on:** Back landed on card-m063 at scrollY 2360 with focus, and the next keys cost 0.4 KB; Enter dropped from about 209–240 KB to about 142 KB (probe line removed).

WAITING, RE-RANKED:
- junction-diff-row-cells-one-label: still worth it on arithmetic. 34 rows a file stop × 1.8 KB = 61 KB, so a first visit (492–570 KB) drops about 11–12 %, a revisit (362–393 KB) about 16 %, and a row scrolled in (7.4–9.4 KB) about 19–24 %. Clears the churn bar; measure first (pixels: the number columns).
- window-setroot-component-not-mounted: not a perf item. No root component in the apps or samples overrides `OnMount`; MULTI-WINDOW.md shows the pattern. Close it here and move it to the framework backlog (`Window.SetRoot` could mount a root `ComponentHost`).
- junction-files-first-open: unchanged, once a session, and its tree is a VirtualList. Leave it with expert 8.
- junction-colours-before-rows-route-a: no new number; keep waiting and don't re-ask.
- junction-summary-overtaken-at-start: unchanged. 3490ba67 adds an index print to the shared status only; `AsyncLoader` and `SummaryFor` are untouched.
- vesper-local-waiting-text: unchanged (CommanderApp.Pane.cs untouched in the range). Wording; the user's call.

EXAMINED, NOT WORTH IT:
- A Down that brings a new grid row builds once. The captured stack shows that build is the posters landing (`RerenderView`, 0 part builds), not the key; it is covered by poster-lands-in-place.
- A Home page turn is 2 builds (the key's page turn plus its posters landing), by design.
- A film page builds 3 times as it opens (navigation plus two loads, about 30 KB each view-only). That follows network timing; parts-build-on-change makes them view-sized.
- History storage: a `Visit` is three fields and 100 are kept. Nothing to save.

NEEDS THE USER:
- keep-the-page-back-returns-to: may Aperture keep one page behind the current one (Home or a grid, about 330 elements, hidden) to make Back about 85 % cheaper? This goes against REACTIVITY.md's rule that a view another view replaced is let go (the Meridian leak). Before/after frames can be checked headlessly by frame hashes of Back.
- back-lands-where-it-was: the visible change is where Back lands, at your old place instead of the top, as 0f9c8477 and History.cs already promise. Confirm the browser model: keep the scroll offset (mouse users too), or keep only the cursor's row.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\00f54f03-b511-4949-9273-30fe719e1bbb\scratchpad\r18-reactivity\`. The worktree is clean at aa3a3696 (probes reversed, bins rebuilt, build server shut down).
- Probes: `probe18.patch` (ComponentHost/Part build counters and stacks, phase bytes, `Element.ProbeMade`, Aperture PROBE_PARTSHAPE / PROBE_REVEAL / PROBE_SCROLLKEEP) and `ProbeR18Tests.cs` (the Walk, BackToScrolledGrid, HomeAndMouse, Stacks, KeptRebuilds and Posters scenes).
- Headless results: `ap-walk-ps0.txt`, `ap-walk-ps1.txt`, `ap-back-reveal0.txt`, `ap-back-reveal1.txt`, `ap-mouse-sk0.txt`, `ap-mouse-sk1.txt`, `ap-kept.txt`, `ap-posters-art0.txt`, `ap-posters-art1.txt`, `ap-stacks.txt`, `parttests-ps1.log`.
- Real window: `start\` (host: `src\Program.cs`, `src\FakeFixture.cs`, `src\Script.cs` with Aperture PROBE lines), `aotp\Aperture\`, `rest\` (`common18.ps1`, `batch18a.ps1`, `sum18.py`, `runs\h1-*`), `sum-h1.txt`.
- Logs: `locks.log`, `desk-h1.txt`, `gate-h1.txt`, `batch-h1.log`.
