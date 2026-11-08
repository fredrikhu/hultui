SATISFIED: no

Method: I re-ran round 8's walks as NativeAOT publishes of round 8's walk host, with round 7's probe, on ea3e57ab. I ran 47 walks plus the 2 Discover walks, 2 reps each, and compared them with round 8's survey rows (27020cdb). Bytes agree between reps to 0.1 %.

I added 7 walks and built them against both 27020cdb and ea3e57ab, alternated, 2 reps:
- Aperture Home opened with its posters held, then released 1 or 6 a step.
- Vesper Thumbnails opened, with its decodes landing.
- The review composer with Enter.
- An Aperture toast.

I added 8 Meridian walks for main's new flows (ea3e57ab only). They use an in-memory keyring and the local fixture server. I bisected one rise. Everything is headless: no real windows and no screen captures.

Jellyfin sign-ins as "aperture-survey": **0** (fake libraries only). My worktree is clean at ea3e57ab, and the fixture server is stopped.

REVIEW:
- **d366505b (objection).** A resize that changes the library grid's column count now costs about 3× more:
  - Bytes per column change: 79 / 92 KB → 243 / 256 KB.
  - Measure misses: 28 / 37 → 115 / 127. Layout visits: 125 / 150 → 270 / 300.
  - `library-resize-4px` (one build per ~50 steps): 3.7 → 6.5 KB a step (+77 %).
  - Bisect: 40c2c554 gives 3.6, d366505b gives 6.9. The other steps are 2 KB in both trees. This becomes candidate 3.
- **fa10f889 (no objection; bug fix).** Home's cursor walks rose because page turns now build:
  - Right: 0.4 → 6.1 KB a key (0.10 builds a key, 51–69 KB a page turned).
  - Down: 0.4 → 28.7 KB (0.5 builds a key; in this fixture, Down moves a page).
  - The base built all 7 cards and walked the ring onto hidden ones. Now 5 cards a page are built.
- **Main's 3a15b5b1 framework parts.** `TickSelectionEdgeScroll` reads the clock and returns. `SelectionEdgeScrolling` is false at rest. `SetMax` sets a float. So: 0 B a frame and no idle wakes.
- **Meridian find typing: 51.1 → 55.5 KB (+8.5 %).** The response Part's build per character went 47.1 → 51.1 KB. That Part is where main added the Request tab and the pretty/raw/web toggles. It is under the bar; I did not bisect it.

**Round 8 helped the apps:**
- Totals: all 49 walks 4,901 → 4,035 KB/key (−18 %, geometric mean 0.78). Round 6's 33 walks 2,029 → 1,760 (−13 %, geometric mean 0.66).
- Rises: Vesper transfer (timing), Meridian find typing (+8.5 %), the resize objection and the Home page turns (both above). Nothing else is worse.
- **Aperture:**
  - Grid Right 48.0 → 0.5 (−99 %). Down 81.4 → 26.2 (−68 %); with a reader 84.9 → 29.8.
  - Discover Right 334.2 → 16.2 (−95 %). Down 337.6 → 103.7 (−69 %; Down turns a shelf's page).
  - Home open, posters one a step: 2,568 → 859 KB (−67 %), 36 builds in both.
  - Home open, six a step: 2,564 → 362 KB (−86 %), builds 36 → 6.
- **Vesper:**
  - Detail 20.9 → 4.3 (−80 %); with a reader 21.4 → 4.7.
  - Brief 28.8 → 2.6 (−91 %). Thumbnails 35.9 → 5.2 (−86 %).
  - Thumbnails open with landings (88 images, 40 frames): 1,556 → 908 KB (−42 %), CommanderApp builds 45 → 2.
  - Also Insert −12 %, folder in/out −10 %, type-to-find −9 %, Tab −6 %, partner −6 %.
- **Meridian:** History Down 52.8 → 23.4 (−56 %). History search 47.4 → 48.1: flat, because the walk selects nothing (the ledger's "nothing selected" case).
- **Rich text (review composer):** typing 3.3 → 3.2; Enter 2.3 → 2.2 (−3 %), no builds. The block wins need large documents; a review comment has a few blocks.
- **Junction:** History 60 ms −6 %, with a reader −11 %. Changes wheel with a reader −13 %. Changes Down with a reader −3 %.
- **Shares:** headless render is 81–97 % of UI cycles on most walks.

CANDIDATES:
- id: apps-meridian-discovery-map-click (owner 6; app)
  what: A click on an endpoint in Discovery's map rebuilds the window and every row. It should move the selection by ref and build only the detail.
  evidence:
    - 600-endpoint spec imported (`ImportSpecFile`): 2,903 KB a click. Build is 96 % of the bytes.
    - Per click: 1.03 MeridianApp builds and 2.05 Parts. 2,496 elements. GCs 6/3/0 per 40 clicks.
    - 60 endpoints: about 340 KB a click, 336 elements. That is about 4.4 KB a row: linear in the endpoints.
    - The row's click calls `MarkDirty` (MeridianApp.Discovery.cs:412).
    - `BuildEndpointMap` puts every row into a ScrollView with no virtualization (:344-364).
    - 12d0392e ("a large spec read in full") makes large maps reachable.
  expected: scaling bar (O(endpoints) → O(1) per click); bytes about −95 %.
  cost: medium, app only. Rows on VirtualList or KeptElements, the detail as a Part. No pixel change.
- id: apps-meridian-env-cell-typing (owner 6; app)
  what: A character typed into an Environments cell rebuilds the whole window and re-reads every secret from the keyring.
  evidence:
    - 3 environments × 40 keys + 4 secrets: 818.8 KB a character. Build is 94 % of the bytes.
    - One MeridianApp build a character, 693 elements, 0 layout visits: the build changes nothing on screen.
    - 24 keyring `Get`s a character (`Secrets.Resolve` per secret cell, own + shared; MeridianApp.Environments.cs:360).
    - On Windows each `Get` is a `CredReadW`: `WindowsCredentialKeyring.Get` (Keyring.cs:119) has no `KeyringMemo`. The Linux and macOS keyrings have one.
    - The cell's handler builds the view (`OnTextChanged → MarkViewDirty`, :341).
  expected: bytes −99 % and 24 → 0 keyring reads a character. Build only when the unsaved state flips, as `DocEdited` does (Chrome.cs:127).
  cost: small–medium, app only. A memo for Windows would be optional.
- id: apps-aperture-grid-column-change (owner 6; layout 1 for the measure misses; objection to d366505b)
  what: A column-count change re-hosts the kept cards, and that costs 3× a fresh build.
  evidence: 243 / 256 vs 79 / 92 KB a column change; measure misses 115 / 127 vs 28 / 37; layout visits 270 / 300 vs 125 / 150; bisected above.
  expected: bytes bar. About −165 KB a column change; `library-resize-4px` 6.5 → ~3.7 KB a step (−43 %). A suite row (kept grid, column change) would pin it.
  cost: small–medium, app or KeptElements.
- id: apps-meridian-response-drag-build (owner 6; app; rank low)
  what: Every pointer move on the response pane's edge (8f7f8fa5) rebuilds the response Part.
  evidence:
    - 39.5 KB a move; build is 86 % of the bytes; one Part a move.
    - 62 layout visits and 211 measure misses a move: the body is re-laid at the new width, which is needed.
  expected: bytes about −86 % a move, if the width is set through a ref.
  cost: medium. It is a drag, not a key.
- id: apps-tokenize-per-line-bytes (owner 5; lead)
  what: `SyntaxHighlighter.TokenizeLines` allocates about 5 KB a line whatever the line holds, and 15 KB a line for HTML.
  evidence (direct probe; 3 calls each after 2 warm-up calls):
    - JSON, 1,993 lines: 9,863 KB a call, 4.95 KB a line, 1.6 spans a line.
    - XML, 2,463 lines: 12,439 KB, 5.05 KB a line.
    - HTML, 3,641 lines: 53,182 KB, 14.6 KB a line (607 B a character). The same page on one line: 2,680 KB.
    - Effect in Meridian (all threads): a pretty toggle on that HTML is 56,145 KB; raw is 2,927 KB. Over 20 toggles: 34 gen-0 and 4 gen-1 GCs.
    - Every JSON response up to 2,000 lines pays the JSON cost when it is formatted.
    - `MarkupPretty.Html` is only 3 % of the HTML toggle: 1,771 KB for a 46 KB page (19× its UTF-16 size). XML 873 KB (7.8×), JSON 301 KB.
  expected: memory churn bar on the worker, if the per-line overhead is in HultUI.Syntax and not inside TextMateSharp. The tokenizer runs off the UI thread, but each GC stops the UI thread too.
  cost: unknown; expert 5 to profile.

WAITING, RE-RANKED:
- **Suite scenarios (document-switch):** still worth a row. Junction Files 84.2 KB a key (was 85.0): build 62 %, 1.23 builds a key, 149.1 measure misses. Also add the kept-grid column change above.
- **aperture-leads (remaining): close.**
  - Toasts: 0.5 KB a frame, ~22 frames drawn and ~4 builds a toast, base = after.
  - Grid Down 26.2 KB a key: 0.70 rows realized a key, render 97 %.
- **junction-changes-leads:** still reproduces. Reader behaviour only; no Junction app commit in round 8, and the a11y commits touched neither multi-selection nor folder select.
- **junction-review-old-path:** still reproduces. `GitFileDiff` is made with `OldPath` null (GitRepository.cs:496, 511, 2269), so `PositionFor` gets the new path (JunctionApp.Review.cs:1182).
- **aperture-poster-as-hero:** code unchanged (ApertureApp.Cards.cs:538-539). Not exercised: no sign-in this round.
- **aperture-tmdb-size-fallback:** code unchanged (w1920 with no fallback, RequestModels.cs:414-419). Not exercised.
- **junction-narrow-gitlab-glyphs:** no Junction app commit in round 8. Not exercised: it needs GitLab.
- **meridian-pane-choice-not-persisted:** still reproduces. `_responseBottomChoice` is a field that Ctrl+\ flips (MeridianApp.cs:291); nothing saves it.
- **meridian-min-width-wraps:** still reproduces. At 760×540, the minimum, "Headers 6" wraps onto two lines, and Send's Ctrl+Enter keycap and the right end of the response bar are cut off (`drv\shots\min2.png`).

EXAMINED, NOT WORTH IT:
- **Meridian response tabs, including the Request tab (967ce1ac):** 53.1 KB a click, one Part build, render 84 % of cycles. Once per click.
- **Discovery once per action:** from a URL that is the spec (600 endpoints), 11,664 KB UI and 15 builds; from a file, 9,690 KB.
- **Vesper transfer-frames +21 %:** timing. Reps 22.2 / 18.5 vs 16.8 / 16.6; more progress builds land per frame (0.23 → 0.30 a frame). No round 8 commit touches transfers.
- **Meridian find typing +8.5 %:** see REVIEW; under the bar.
- **Big files at 33 ms, all threads 3,148 → 788:** timing.

NEEDS THE USER:
- No new decisions.
- 0 "aperture-survey" sign-ins this round. The earlier devices and the API key are still to be removed and revoked.

FILES: `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\c484f5ac-8f22-48dd-b669-13c24b1e7889\scratchpad\r9-apps\`
- **Walk host:** `walk\src\Program.cs` (round 8's host + dispatch + the review Enter walk), `walk\src\R9.cs` (new walks and probes), `walk\Walk.props`, `walk\{after,base,pre366,at366}\WalkHost.csproj` (`after` defines R9AFTER).
- **Trees with the probe:** `tree-after` (ea3e57ab), `tree-base` (27020cdb), `tree-pre366` (40c2c554), `tree-at366` (d366505b).
- **Scripts:** `tree.ps1`, `aotpub8.ps1`, `run.ps1`, `batch9.ps1`, `batchnew.ps1`, `gen9.py`, `merge9.py`, `rename9.py`, `cmp.py`, `sum.py`.
- **Data:**
  - `runs\walks.jsonl`, `runs\cmp-in.jsonl`, `cmp-all.txt`
  - `runs\new9.jsonl`, `runs\new9-cmp.jsonl`, `runs\bisect.jsonl`, `runs\scale.jsonl`
  - `runs\after9-aot-r{1,2}-meridian9.out.txt`
  - `runs\after9-aot-probe2-meridian9.out.txt` (the PRETTY and TOKENIZE lines)
  - `drv\shots\min2.png`
- **Fixtures:** `fx` (round 8's, plus `coll-r9`, `spec600.json`, `spec60.json`) and `www` (plus `big.html`, `big.xml`, `spec600.json`).
