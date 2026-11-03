# Round 7 survey, expert 6: Reactivity and reconcile

SATISFIED: no

Measured at 57e6547c (detached; probe and prototype reversed with `git apply -R final.patch`, probe files deleted,
worktree clean). Headless, JIT, Release, P-cores; a key and two frames; bytes from
`GetAllocatedBytesForCurrentThread` split into exclusive buckets (build per component and per Core Part, dispatch,
reconcile, layout, a11y, render). No timings.

## REVIEW

None.

- **Public Part (5765d27b..a267faab): correct.** Place costs 0 B (11 places a Vesper key). A kept host comes back
  through the moved-host path, so its subtree is not patched against itself (7 patches a Detail key). Junction and
  Meridian mount through a revision signal: the same builds as Place, no bytes lost.
- **Held:** Aperture resize 2.3 KB with 0 builds (1f4bbda8); grid Right 49.5 / Down 76.0 KB (31997de7); Thumbnails Down
  62.8 (cdd2c1ac); composer char 13.5 KB at 0 and 200 files (454fc82f); Meridian popover open + pick 210.4 (44e9254a);
  Junction History Down 20.4.
- **Doubles (rebuilds twice in one event):** Reel 0, Aperture 0, Meridian 5 (r6: 14). Vesper 118 app + 401 part (r6:
  115 + 495): OpenViewer 58, CommitNamePrompt 33, NavigateTo 8, the rest 1–3 each. Junction 23 app + 6 part (r6: 23);
  the 6 are the composer part riding app doubles on CloseDialog + Flash. All one-off actions.

## CANDIDATES

- id: reactivity-shortcut-matches-no-enumerator
  what: `Shortcut.Matches` loops `foreach` over `IReadOnlyList<KeyChord> Keys`, boxing an enumerator for each shortcut
  tried; use an index loop.
  evidence: `ShortcutMap.Find` on Vesper's 59 shortcuts costs 1,696–1,888 B a call; after the fix 0 B. That was all of
  Vesper's "dispatch" bucket (lead vesper-key-rest-bytes: not the preview load). Prototype: Detail Down 22.0 → 17.1 KB
  (dispatch 5.3 → 0.4), Insert 33.2 → 27.3, Space 22.1 → 16.3, type-to-find dispatch 13.3 → 7.4 KB a key. Every app
  uses ShortcutMap (89 call sites); Junction pays ~0.6 KB a key.
  expected: churn, Vesper keys −18…−26 % (JIT); AOT has no PGO, so the same or more.
  cost: small (ShortcutMap.cs:207), no API or pixel change.
- id: vesper-brief-marks-per-cell
  what: Brief's `GridState.Changed` compares the whole marks set (and the cut set), so one mark remakes every cell. Put
  Marked/Cut in `BriefKeptCell` and take the sets out of the Brief state (CommanderApp.Views.cs:505, 598).
  evidence: Brief Insert 480.3 KB a key (build 443.9), 437 elements made and 436 patched; Detail Insert 33.2. Same with
  UIA (477.0, no tree).
  expected: churn ~480 → 35 KB (−93 %).
  cost: small, app.
- id: junction-createmr-fields
  what: in the Create MR card a title character calls `MarkDirty` (all chrome parts plus the view); a description
  character calls `RerenderView` for both DocumentChanged and SelectionChanged. Build the title only when it goes
  empty ↔ non-empty, the description only when `toolbar.IsStale`.
  evidence: title char 256.5 KB over History (app 115.8, toolbar 55.4, sidebar 44.0, title bar 26.0), 292.6 over
  Changes with 200 files; description char 126.3 / 143.8 KB, 86–109 elements made (JunctionApp.CreateMergeRequest.cs:323,
  329).
  expected: churn ~2 KB a character (−98…−99 %).
  cost: small, app. Test `TypingTheDescription_BuildsOnceACharacter` would expect 0 builds. Docs: ELEMENTS.md's
  RichTextToolbar text tells apps to wire SelectionChanged to a rebuild; it should say to check IsStale first.
- id: junction-issue-list-keys
  what: in the composer's issue list, Down/Up (and Esc) call `MarkDirty` (IssueAutocomplete.cs:158–162); the list is
  inside the composer part — write `_composerRevision`.
  evidence: 212.0 KB a key at 1 and 200 files (app 33.6, toolbar 57.0, sidebar 44.7, composer 34.1, title bar 26.0);
  173 elements made.
  expected: churn ~34 KB (−84 %).
  cost: tiny, app.
- id: virtuallist-item-rows-follow-their-item (framework; owner 6, with 1)
  what: `KeepUnchangedRows` matches rows by index ("an insertion above them makes the rows it moves again"). Hand a
  let-go row back to the index where an equal item (same selected state) now stands, before the template runs. Apps
  keep the index out of an item unless the row shows it (Vesper: zebra parity, `index & 1`, only while zebra is on).
  evidence: prototype (VirtualListElement +30 lines, Pane.cs:870), on top of the shortcut fix. Vesper Detail
  type-to-find ("file-01" typed and erased): 92.7 → 64.3 KB a key (−31 %); rows' render 38.1 → 9.3 KB (−76 %); elements
  made 64.5 → 38.1 a key, unmounts 34.8 → 8.4; 666 rows moved. With UIA 148.4 → 113.9 KB (both fixes). Trees stayed at
  5 per 14 keys. Tests with it on: Core VirtualList 65, Headless VirtualList/Palette/Sticky 137, Vesper 876, all pass.
  Also helps Junction Changes' stage flip (needs its TreeNodes memoized: the in-flight lead), Meridian History filter
  and the palette, once their items drop the index (both key automation ids by index).
  expected: scaling — a filter, insert or stage remakes only the rows that enter the window, not every row it shifts;
  churn −31 % on type-to-find.
  cost: small–medium, no API. Pixels the same (a row shows its item). A focused field in a moved row keeps the focus
  (today it is remade). Folded in (Vesper app): with a filter on, `Rows()` makes a new filtered List per call
  (CommanderApp.cs:1620), ~7 KB a key after the shortcut fix; memoize per listing generation + search.
- id: junction-composer-skip-build
  what: every composer character writes `_composerRevision`. Off GitLab the composer shows nothing of the text; on
  GitLab only the issue token and the closing reference. Compare (GitLabActive, IssueTokenAt, ClosingRef match) and
  write only on a change (JunctionApp.Changes.cs:1206).
  evidence: 13.5 KB a char off GitLab (part 12.3), 15.4 on GitLab with no token (part 14.3), same at 1 and 200 files.
  expected: churn ~1.2 KB (−91…−92 %).
  cost: small, app.
- id: meridian-url-typing-skip-build
  what: a URL character calls `MarkViewDirty`, which builds the whole request editor. With the Headers, Body or Auth
  sub-tab open, only the Params and Chain counts and the stale banner can move: compare those and skip the build.
  evidence: 64.3 KB a char (build 57.2, 54 elements made), with or without a 2,000-line response
  (MeridianApp.Requests.cs:338, 448–459).
  expected: churn ~7 KB (−89 %) on those sub-tabs.
  cost: medium, app. `Chain.Analyze` runs per character for the compare key (it already runs per build).
- id: aperture-home-kept-cards
  what: a Home cursor key makes every shelf's cards again (CardRow per row, ApertureApp.Home.cs:84); hand back the
  unchanged cards, as the library grid does.
  evidence: Home Right 67.5 KB, Down 68.1, 63 elements made and 63 patched (Home has 101 elements).
  expected: churn ~15 KB (−78 %).
  cost: medium, app (or small with the helper in Needs the user).
- id: meridian-assertion-banner-key (bug)
  what: the stale banner is added unkeyed above the assertion rows (all Flex.Row); `ReconcilePrefixSuffix` matches them
  by type from the front, so row 0 is patched into the banner and its focused field is replaced
  (MeridianApp.Chaining.cs:48–58). Key the banner.
  evidence: confirmed headless — `assert-expected-0` focused, the first key brings the banner and leaves no focus, the
  second key goes nowhere.
  expected: correctness.
  cost: tiny. Guideline for REACTIVITY.md's Tree Diffing section: a child that comes and goes ahead of siblings of its
  own type needs a key.

## WAITING, RE-RANKED

- vesper-coalesce-markdirty: drop. A folder Enter is 1 app build in the dispatch plus 1 when the async listing lands;
  Backspace the same (Enter to listing 281 KB, 2 app builds, 5 part builds). Doubles occur only on one-off actions.
  Coalescing `MarkDirty` in the framework would change behaviour in 5 apps to save one build per dialog or viewer open.
- vesper-brief-marks-and-filter-rows: marks → vesper-brief-marks-per-cell (480 KB a key); Detail filter rows →
  virtuallist-item-rows-follow-their-item. Brief type-to-find 91.8 KB a key (build 54.8, ~63 elements made a key)
  though its cells should be handed back: not explained, trace in the build. Space with a reader not re-measured (a
  Space on a folder measures it).
- vesper-key-rest-bytes: solved → reactivity-shortcut-matches-no-enumerator.
- junction-leads (reactivity parts): Create MR → junction-createmr-fields; issue-list keys → junction-issue-list-keys;
  composer → junction-composer-skip-build. ChangesView's new lists during a stage: not measured; memoized nodes plus
  rows that follow their item cover it.
- aperture-leads: Home → aperture-home-kept-cards. Detail keys build nothing (0.2 KB; a film's page registers no cursor
  list). Discover not measured (needs a request server). Toasts are one-off. Place() for Junction/Meridian would make
  the same builds, no bytes.
- meridian-leads: assertion → meridian-assertion-banner-key (cause found). The environment prompt double
  (`SubmitDialog` → the callback's `MarkDirty` plus its own) once per submit: a one-line cleanup, no bar.

## EXAMINED, NOT WORTH IT

- Coalescing `MarkDirty` in a dispatch (framework): see vesper-coalesce-markdirty.
- Reconcile pooling (P5a) stands: reconcile is Vesper 1.9 of 17.1 KB, Junction History 1.4 of 20.4, Meridian URL 5.8
  of 64.3 (9 %), Aperture Home 3.3 of 67.5.
- Vesper path-trail part, 39 KB per folder change: once per change.
- Meridian History Down 62 KB: 2 rows plus the archive panel, which shows new content.
- Junction palette type+erase 52.3 KB a step, 40.3 unattributed (outside builds and frame phases): after
  palette-virtual-rows; leave to 1/8 if wanted.

## NEEDS THE USER

- **A Core helper for kept elements**, as `Part` was for parts. Vesper Brief, Vesper Thumbnails and Aperture's grid
  each hand-roll "hand back the unchanged cells", with 8 rules and lessons in REACTIVITY.md; this round's slip is
  Brief's marks in the shared shape (437 cells per mark), and the next consumer is Aperture Home (63 elements a key).
  Is a public helper worth it (key, per-cell state, from the build just before only, live ref, row-place check)?
  [Yes, if Aperture Home is built: then that adoption and later ones are small.]

## FILES

Scratchpad `r7-reactivity\` of session 855f4ddd: probe `apply7.py`, `R6Probe.cs`, `probe.patch`; prototype
`proto.patch` (row follow gated by `R7_PROTO_MOVE=1`, plus the shortcut fix); both `final.patch`; driver `drv\` (modes
vnav, vbrief, ahome, massert, keys, plus round 6's); Junction.Tests probe `R7JunctionProbe.cs`; double logging
`doubles.ps1`, `doubles-*.log`; outputs `run-base.txt`, `run-new*.txt`, `run-proto*.txt`, `run-keys.txt`,
`run-junction-probe*.txt`, `proto-*.txt`.
