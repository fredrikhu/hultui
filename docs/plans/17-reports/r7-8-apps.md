# Round 7 survey, expert 8: App workloads

SATISFIED: no

Compared cd3b6c8f (round 6's start, a `git archive` extract) with 57e6547c (round 6 released). Both trees are
NativeAOT publishes of round 6's headless walk host plus 14 new walks, with the same probe: bytes and cycles per phase,
builds by type, rows made, layout visits, measure misses, a11y trees, git runs and output characters per key; the rest
of each key split by where it lands (inside the key press, or in the pump outside the frame). Base and after
alternated, 2 reps, pinned to the P-cores, bytes and counts only. UI bytes per key agree between reps within 0.1 %;
bytes on all threads vary only where a worker races the pace (big files). Meridian's find walks in rep 1 had no
response (fixture server not up): dropped, so those rest on rep 2 alone (n=1). Real windows 0; Jellyfin sign-ins 0
(Aperture on `FakeMediaLibrary`). Worktree clean at 57e6547c (probe removed with `git apply -R`, three prototype edits
undone by reverse edit); the 127.0.0.1:8767 fixture server stopped.

## REVIEW

None. Round 6 helped every app walk; the only walk that got worse pays for a bug fix.

- **Round 6's 33 walks (UI KB per key):** sum 6,726 → 2,373 (−65 %), geometric-mean ratio 0.39. All 47 walks: 24,087 →
  4,894 (−80 %).
- **The one walk that got worse:** Meridian History Down 52.0 → 68.7 (+32 %), from 8bcf3b14's fix (the selection now
  scrolls into view, so rows are on screen): rows made 0.5 → 2.5 a key, render 3.9 → 21.2 KB. Already recorded.
- **Junction Changes wheel at 300 files (the virtualizing trade):** 0.3 → 12.8 KB a notch (0.5 → 35.9 per 3-notch
  step); with a reader 24.3 → 25.2 (+4 %); cycles per notch equal. **A real session does not pay:** a Down key saves
  2,790 KB (3,103 → 313), so the trade breaks even at ~220 notches per arrow key; a mixed walk (5 three-notch wheels,
  5 Downs, then back) 1,538 → 125 KB a step (−92 %), with a reader 1,712 → 151 (−91 %). With a reader a notch builds a
  whole tree 0.29 times (base 0 trees, 0.28 in-place applies) — the tree every third notch the ledger already lists.
- **Notes, not objections:** History at 60 ms: gen-2 GCs rose 1 → 4 per 100 keys while all GCs fell 75 → 25 — the
  commit-patch strings on the large object heap now trigger gen 2 (candidate 1 removes them). Lazy line runs
  (afa7116d): no walk pays more per row it draws first (Changes 9.35 → 9.33 KB a row; Vesper reader PageDown 267.6 →
  256.0).

**Base → after, UI KB per key** (all-thread KB in brackets where it differs):

- **Junction:** History 60 ms 59.9 → 33.8 [9,893 → 1,644]; History 500 ms with a reader 65.6 → 39.2 [11,281 →
  5,195]; Commit file walk on the 415-file commit 560 → 223 [7,545 → 295]; Tags 54.9 → 54.4; Files 86.5 → 85.4;
  summary typing 74.2 → 15.1; review typing 155 → 3.7 (reader 215 → 4.3); Changes 300 Down 3,103 → 313 (reader 3,419
  → 358); diff find Enter 460 → 87.8 [8,968 → 88]; big files at 33 ms 3,306 → 584 [10,474 → 1,783], at 100 ms 3,380
  → 585 [17,673 → 1,357].
- **Vesper:** Detail 43.4 → 26.1; partner 57.8 → 40.4; Thumbnails 58.2 → 41.1; Insert 287 → 49.8; Tab 120 → 103;
  transfer 79.6 → 17.5 per frame. Flat (within 1 %): Brief 33.7 → 34.0, type-to-find 111.5 → 111.8, folder in/out
  314.9 → 316.1.
- **Meridian:** body typing at 89k characters 1,795 → 238; method pick 554 → 147; palette with a reader 207 → 36.8;
  find typing 430 → 75.1; find Enter 440 → 60.3; URL typing 69.4 → 68.5; History search flat.
- **Aperture:** grid Down 182 → 81.3 (reader 218 → 112); Right 112 → 48.0; 4 px resize step 71.7 → 3.7; Home Right
  98.6 → 64.6; a film page's Tab 0.6 → 0.6.
- **Reel:** 0.8 → 0.8 KB per frame.

**Shares, after (UI bytes):** Junction History build 44 %, render 40 %, rest 12 % (98 % of all-thread bytes off the UI
thread); diff find Enter build plus reconcile 64 %, render 35 %; Changes Down render 84 % (28 rows a key: the newly
selected file's diff rows, not list rows made again). Vesper Detail build 57 %, rest 24 %, render 11 %; Tab build
93 %. Meridian find Enter build 83 %; find typing build 67 %, the search 32 %. Aperture Home build 95 %; grid Down
render 59 %, with a reader a11y 70 %. Headless UI cycles: render (CPU raster) 84–92 % on every walk.

## CANDIDATES

- id: junction-history-prefetch-on-rest (owner 8/6)
  what: History's commit-detail prefetch waits for the selection to rest (~120 ms, the rule the highlight prefetch has
  used since 985adef7) instead of starting on every move.
  evidence: the 60 ms held walk starts 4.0 git processes a key (diff-tree ×3, log) and reads 130.5 K characters a key,
  99 % of it the whole patch; 26 of the first 90 commits' patches exceed 85 KB as UTF-16 and land on the large object
  heap. That is 1,644 KB a key on all threads against 33 KB on the UI thread, 12/9/4 GCs per 100 keys. History never
  shows the detail; it only warms the Commit tab. Prototype (an environment gate on `JunctionApp.History.cs:74` and
  `JunctionApp.cs:1074`): all threads 1,644 → 29.1 KB a key (−98 %), UI 33.2 → 29.1, GCs → 0/0/0, git 4 → 0. The 500 ms
  walk rests on every key, so it keeps its loads.
  expected: memory bar, −98 % on all threads while an arrow is held, gen 2 4 → 0 per 100 keys; ~64 fewer git process
  starts a second while the arrow is held.
  cost: small, app only. The Commit tab's own build still asks at once.
- id: shortcut-matches-no-enumerator (owner 4)
  what: `Shortcut.Matches` walks `Keys` (an `IReadOnlyList<KeyChord>`) with the indexer instead of `foreach`.
  evidence: `ShortcutMap.cs:209` boxes an enumerator for every shortcut it checks, and `Find` checks the whole table;
  Vesper's `HandleKeyDown` runs `Find` 2–3 times a key over ~55 shortcuts — the 6.1 KB per key inside the key press
  (traces vesper-key-rest-bytes). Prototype (3 lines), AOT: Vesper Detail 26.1 → 20.8 (−20 %), Brief 34.0 → 28.7
  (−16 %), Thumbnails −14 %, partner −13 %, Insert −12 %, type-to-find and Tab −5 %; rest 6.2 → 0.9 KB. Junction
  −0.5 KB a key; Meridian and Aperture unchanged.
  expected: bytes bar on five Vesper walks (−12…−20 %).
  cost: small, no API change.
- id: findbar-step-own-part (owner 6)
  what: Core's FindBar builds itself in its own `Part`, so a step and its count label build the bar, not the view; the
  body keeps its `InvalidateRow` on the two rows the match moves between.
  evidence: junction-leads' "108 KB unexplained": the find bar's change callback rebuilds the whole view
  (`JunctionApp.FindBar.cs:30`, `new(MarkViewDirty)`). Junction diff Enter 87.8 KB: one JunctionApp view build 51.2
  plus reconcile 5.0 (64 %), render 30.7 (2.6 rows), 101 measure misses a step. Meridian Enter 60.3 KB: the response
  part's build 46.9 plus 2.9 (83 %), 219 measure misses a step. Vesper's reader Enter 69 KB (b3fc6be5).
  expected: Junction Enter ~88 → 36 KB (−59 %); Meridian ~60 → 14 (−77 %). Bytes bar.
  cost: medium (a Core control). Typing still has to hand the body new highlights.
- id: aperture-home-cursor-no-build (owner 6)
  what: an arrow on Home moves the focus and ring without building the view, or builds only the shelf it leaves and the
  shelf it reaches.
  evidence: Home Right and Down 64.6 and 65.3 KB a key: build 61.1 (95 %), 0.99 ApertureApp builds a key, render 0 B.
  `MoveCursor` calls `RerenderView` (`ApertureApp.Cursor.cs:117`), and `BuildHomeView` builds every shelf. The fake
  library has one shelf of 7 cards; the build grows with each shelf a real Home shows.
  expected: −80…−95 % per Home key (bytes bar).
  cost: small–medium, app only.
- id: a11y-composite-row-remade-tree (owner 7)
  what: when a composite list row is made again at a human pace, its subtree is patched instead of building the whole
  a11y tree.
  evidence: Aperture grid Down, recording bridge, keys 100 ms apart: 111.9 KB a key, a11y 78.7 (70 %), 0.99 trees a
  key. Step 13 counted 0.33 under UIA with frames back to back; the 100 ms burst rule lets a tree through on every
  human-paced key. Junction Changes 300 Down with a reader: 0.98 trees a key, a11y 63.5 of 358 KB.
  expected: Aperture Down with a reader −60 %; Junction −15 % (bytes bar).
  cost: medium.
- id: junction-composer-typing-isstale (owner 6; from junction-leads)
  what: the Changes composer builds on a keystroke only when something it shows would change (the rule
  `RichTextToolbar.IsStale` applies).
  evidence: summary typing on a repository with no GitLab 15.1 KB a character (reader 15.7); 12.3 KB of it (81 %) is
  one composer Part build a key.
  expected: ~15 → 3 KB a character (−80 %).
  cost: small, app only.
- id: meridian-history-list-drawn-selection (owner 6)
  what: Meridian History draws its selection with the list (`SelectionHighlight`/`SelectionOverlay` from d8e28f93), so
  a key makes no row.
  evidence: since 8bcf3b14 keeps the selection on screen, History Down 68.7 KB a key: render 21.2 (31 %) from 2.5 rows
  made a key, build 42.4 (62 %) from one MeridianApp build.
  expected: rows 2.5 → ~0.1, −25…−30 % a key; more if the selection stops building the app.
  cost: small–medium; the selection's pixels may change (Needs the user).

## WAITING, RE-RANKED

- **junction-leads:** the find step's 108 KB → findbar-step-own-part; the detail load ("History's floor") is the
  per-key prefetch → junction-history-prefetch-on-rest; "big files at 33 ms 2.8 MB vs 873 KB" is timing, not a leak
  (each 8,000-line file the worker tokenizes reads ~3 MB of `git show` text, and how many keys rest long enough varies:
  33 ms gave 585 and 2,981 KB a key in the two reps, 100 ms 1,774 and 941; both far below base 10.5 and 17.7 MB) —
  closed; gen 2: History → candidate 1, big files 0–1 per 100 keys from 1.5 MB texts (not worth a change alone); the
  non-GitLab composer → junction-composer-typing-isstale. Not measured: in-flight stage lists, old-side convergence,
  the renamed-file bug, the long-line budget.
- **aperture-leads:** Home → aperture-home-cursor-no-build; Down with a reader → a11y-composite-row-remade-tree; a film
  page's Tab costs 0.6 KB with no build — drop that part; `Place()` in Junction and Meridian is done (a267faab,
  df0bf30b); Discover (needs Seerr) and toasts not measured.
- **meridian-leads:** a focus bug and a one-off double build when the prompt is submitted; not perf, not measured; keep
  as bugs.
- **vesper-key-rest-bytes:** traced → shortcut-matches-no-enumerator.
- **vesper-coalesce-markdirty:** still 3 CommanderApp and 5 Part builds per folder change; build is 127 of 316 KB
  (40 %); two app builds are ~30 KB, ~−10 %: at the bar, low priority.
- **vesper-brief-marks-and-filter-rows:** Brief 34.0 KB, flat. Type-to-find 111.8 KB with 7.1 rows made a key and
  render 61 KB (55 %): still worth it, ~−40 % on type-to-find if rows were matched by path.
- **Suite scenarios (document-switch still open):**
  1. Document switch: Junction Files at 85.4 KB a key — 62 % view build, 1.23 builds a key (the file's text lands and
     builds again), 149 measure misses a key.
  2. A find step beside a 3,000-row list: the bar in the view vs in its own part (pins findbar-step-own-part).
  3. A cursor walk over 5 shelves of 7 cards: the view built per move vs kept (pins aperture-home-cursor-no-build).
  4. `a11y/image grid walk Down, 600 cards, keys 100 ms apart, bridge on`: the suite's grid rows run back to back and
     miss the tree on every human-paced key.
  5. No new row for shortcut-matches-no-enumerator: a Core allocation test (0 B per `Matches`) pins it better.
  6. A held selection that starts background work: needs bytes on all threads, which the suite does not count; a
     Junction test counting detail loads per held walk would pin candidate 1.

## EXAMINED, NOT WORTH IT

- **Changes wheel at 300 files:** the virtualizing trade; break-even ~220 notches per arrow key; a mixed session −92 %.
- **Changes Down's 28 rows a key:** the new file's diff rows (24-line files), needed.
- **Vesper Tab at 102.8 KB (93 % build):** one view and two part builds per pane switch, once per action.
- **Meridian find typing's search (24 KB a character, 32 %):** the match list over 2,000 lines, needed.
- **Meridian body typing's rest of 174 KB:** the TextArea's new 89k-character string, parked as
  textarea-chunked-document.
- **Start-up and working set:** not re-measured (no real windows this round).

## NEEDS THE USER

- **meridian-history-list-drawn-selection** would change how Meridian History's selection looks, as Junction History's
  did (approved in round 6). Approve the same for Meridian?
- **Reminder:** no Jellyfin sign-ins this round; the "aperture-survey" devices and the API key are still to be removed
  and revoked when the work is done.

## FILES

Scratchpad `r7-apps\` of session 855f4ddd: probe edits `probe7.py`, `probe6b.py`, `probe7b.py`, `probe-r7.patch`;
walk host `walk\src\Program.cs`, `walk\Walk.props`, `walk\base\WalkHost.csproj`, `walk\after\WalkHost.csproj`;
scripts `run.ps1`, `batch.ps1`, `pubboth.ps1`, `aotpub.ps1`, `aotproto.ps1`, `cmp.py`, `clean.py`, `sum.py`; data
`runs\walks.jsonl`, `runs\walks-clean.jsonl`, `runs\proto.jsonl` (shortcut and no-prefetch prototypes), `cmp-all.txt`;
trees and fixtures `base-tree` (the cd3b6c8f extract, probe applied), `fx`, `www`.
