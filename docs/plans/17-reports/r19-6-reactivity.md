# Round 19 survey, expert 6: Reactivity and reconcile

SATISFIED: no

REVIEW:

- **d7eedc4b (picture landing in place): I agree with expert 4.** I had found the same code path: `_artWaits`
  (ApertureApp.Cards.cs:445–466) keeps each `ArtWait` until its download lands, and each link's `Current` holds the
  waiting element and the detached tree above it. Every download does land (success or failure, PosterCache.cs:343–348),
  so healthy runs leave 0 waits. A stalled body never lands, so its links grow with every build.
  - **The body deadline is the root fix.**
  - **The prune is safe for kept parts only at a point after a frame is drawn**, for example a post-frame callback or
    the next `Get` outside a build. After a frame, an unhosted element can never come back: `KeptElements.TryKeep` needs
    `IsHosted`, and a removed host is made again by `Mount()`.
  - **The prune is not safe inside a build.** During the app's build, a kept Home shelf part and every card it hands
    back are off the tree (`IsHosted` false). A card just made is unhosted until its build is patched in. A prune there
    would strip live shelf cards of their waits, and they would stay on the glyph until a later build.
  - **Also drop duplicate links.** A twin's Ref is pointed at the same live element after the patch.
- **941cffdf / 0f5cda09: one note (part-place-stale-double-build, confirmed, below every bar).** `PlaceTitleBar` and
  `PlaceRail` work out `stale` against default values before `Place` mounts the host. `Mount()` builds the part, then
  `MarkDirty()` builds it again.
  - At start: title bar 2 builds, rail 3, doors 17.
  - Prototype (`Place` returns at once when it has just made the host): 1, 2 and 13 builds; 422 → 390 elements made.
  - Bytes: headless JIT start 879.8 → 847.0–847.5 KB; real window AOT −33 KB of 5.4 MB UI bytes at boot.
  - With the fix, 88 Aperture PartTests, HomeShelfTurnTests and RequestServerHostTests pass. Junction and Meridian
    already call `Mount()` first.
- **133bcfe6: no objection.** Up and Down move shelf to shelf with no build and 1 frame a key.
  - Headless: 0.4–1.3 KB a key; Home scrolls 0 → 151 → 520 → 889 → 1258 with no build; PageUp and PageDown the same.
  - Real window: 0–4 KB a key beyond the probe line.
- **f980fc8d:** a shelf turn is 80.6–81.5 KB headless (one shelf build of 73 KB, 52 elements made, 0 app builds). Real
  window: ~62.5 KB net, 40 made, 2–3 frames. Matches the Landed row.
- **ab4069ca, 5b7a4494, 421cc677: no objection.** Back lands on the cursor card with focus, both headless and in the
  real window (c=42, y=1418, card-m042 focused).
- **ab8a2050: no real-app case.** No app uses `TableView`; Junction's `MdTableView` is its own code. `SetSortKeys`
  returns early on equal keys, so an app that sets its sort on every build costs nothing.
- **ca72af3e:** a Vesper Down is 1.4 KB a key with 0 builds, as landed.
- **fd943d6f:** not measured again; nothing in my probes contradicts it.
- **The quirk after a sign-in to another server** (the rail shows the old libraries) is unchanged since before 941cffdf.
  It comes from the libraries loader's old list, not from a stale part. Not perf.
- **Expert 3's Changes lead: the code path is confirmed.** The start's full preload (JunctionApp.cs:470 →
  `PreloadViews` → `EnsureChangesLoaded`) runs `ReadChanges`, which diffs every staged and unstaged file
  (JunctionApp.Changes.cs:170–173) and keeps the result in `ChangesData`, with Changes not shown.
  - A fix on my side: the preload fetches the lists only, and the diff is read per file when it is selected, with the
    same cap as c7dd2365 for Commit.
  - Owners are experts 3, 4 and 8. I did not measure it. My Junction probe used a fresh TempRepo, so the dirty jrepo did
    not touch my numbers.

CANDIDATES:

- id: reactivity-vesper-tab-builds-both-trails
  what: A change of the active pane recolours the live drive label of both path trails (a `Ref` to the label, then
  `TextColor` and `MarkStyleDirty`) instead of building both trails. The drive label's colour is the only thing in a
  trail that reads which pane is active (CommanderApp.Pane.cs:654–659; `MarkViewDirty` in CommanderApp.Parts.cs:116
  moves `TrailRevision` for both panes).
  evidence:
  - Prototype `PROBE_TRAIL`, headless JIT, alternated ×2, repeats identical: Tab 96.7–107.4 → 15.9–27.2 KB (−75…−84 %);
    trail builds 2 → 0; made 73–83 → 9–19.
  - Frames byte-identical after every Tab (SHA-256 of the captured frame equal on both sides).
  - Real window (AOT): Tab 82.8–83.6 → 16.4–17.3 KB (−80 %), made 61 → 9, 1 frame both sides.
  - One PartTests assertion pins the old behaviour ("Tab did not build the left trail"); the build must change it to
    check the live label against what a full build makes.
  real app: Vesper, Tab and a click into the other pane (both set `_active`). This is the commander's core loop (Tab,
  navigate, F5/F6), tens to hundreds of times a session.
  expected: churn bar, −80 % a Tab.
  cost: small, app only (Parts.cs and Pane.cs, about 15 lines). No API; no pixels in the prototype. The result-set pill
  (`BuildResultsBreadcrumb`) needs the same treatment or keeps building.
- id: apps-junction-tree-cursor-rows-only (from expert 1; I take it as owner)
  what: In Junction's Files tree and Commit file tree, a cursor step onto a folder, back onto the open file, or past the
  end moves the ring on the live list (`RenderWithState`, `CurrentIndex`, `ScrollRangeIntoView`) instead of a whole-view
  `MarkViewDirty` (JunctionApp.Files.cs:592, JunctionApp.Commit.cs:959).
  evidence: expert 1's prototype: a folder step 54.4 → 9.1 KB (−83 %); Down at the end 45.4 → 0.2 KB; 0 of 280 frames
  differ. Real window today: 52.6 KB a key (Files), 79.9 KB (Commit).
  what the prototype must also cover:
  - Keep the `Open` flag in the items. The Commit tree's open file stays lit while the cursor is on a folder
    (CommitTreeRowItem, Commit.cs:816–818).
  - The case where the cursor lands back on the open file (`node.Path == _selectedFilePath`) must move the ring too, not
    build.
  - A reader must hear the new row: check which accessible state the Cursor flag drives, and raise the selection or
    focus change on the live row.
  - Keep the keyboard on the list or row as a build left it.
  - Keep the least-scroll rule of `_pendingFilesTreeScroll` / `_pendingScrollToSelected`.
  - Re-check `JunctionApp.Blame.cs:55` and `Review.cs:1600`, which also set the cursor path.
  real app: Junction, arrowing through a tree in Files and Commit.
  expected: churn bar, about −83 % a folder step.
  cost: small, app only. Its framework half (`DropGathered` marking layout) stays expert 1's.
- id: reactivity-aperture-card-caption-in-card (estimate; measure first)
  what: A poster card's two caption labels sit in the card itself (the artwork with `MarginBottom(S2)`, the subline with
  `MarginTop(1)`, the card with gap 0) instead of in a `Flex.Column` of their own. Each card has one element fewer
  (6 → 5).
  evidence:
  - Headless, made from the app in a second window, batches of 7 new items: a card is 7.1–7.8 KB to make; its first
    frame 0.4–2.6 KB.
  - Construction per element: a bare box 600 B; a column 656 B; a label 752 B with its styles; `OnClick` with a closure
    +152 B. With titles off a card is 4.7 KB and 3 elements.
  - Element size itself was closed as P5c.
  real app (estimated at −0.66 KB a card): Back to Home −29 KB of 296 (−10 %); Back to a grid −29 of 288 (−10 %); a
  shelf turn −6 %; a grid row −5…−7 %; a library first open −5 %.
  expected: churn bar, only on Back (~10 %), so borderline. Build only after measuring, and with pixels byte-identical.
  cost: small, app only. Pixels must match: margins on items that never shrink (LAYOUT.md's shrink weights include
  margins).

REAL WINDOW:

- **Setup:** 1 hold, 09:26:03–09:28:54 (waited 222.8 s, held 171.5 s). 8 launches: Aperture ×4 alternating
  `PROBE_PLACE` 0/1/1/0; Vesper ×4 alternating `PROBE_TRAIL` 0/1/1/0. NativeAOT from this worktree at 56d9a3f6 plus
  probes.
- **Desk:** the 1440p (2560×1440 at 100 %, 200 Hz, primary) was the only monitor. Aperture ran on Vulkan Mailbox with
  2 images and HDR on, 1440×900; Vesper on GL, 1240×820.
- **Guards and system:** timer 1 ms (Steam). The user's cursor was inside both windows' rectangles at every launch. 0
  foreground takes, 0 refused activations, 0 nvlddmkm events. The idle gate passed before the hold (last 9.2 %).
- **Shown:** each Aperture mark includes a ~44 KB probe line; the numbers below have it removed.
  - Home Up/Down: 0 builds, 1 frame each.
  - Turn: ~62.5 KB, 40 made.
  - Back to Home: ~286 KB, 285 made. Back to the grid: ~286 KB, 292 made. Home by key 1: ~285 KB, 288 made.
  - Enter: 13–89 KB. Library first open: 547–564 KB, 388 made.
  - Start: title/rail/parts builds 2/3/22 → 1/2/16; made 422 → 390.
  - Vesper as above: Tab 83 → 17 KB.
  - Counts matched headless and repeated within 0.1 KB, except the library's first open (590–608 KB with the probe
    line).
- **Nothing new only a real window showed in my area.** The pointer resting in the window did not add builds.

WAITING, RE-RANKED:

- **reconcile-keyed-match-alloc: low, keep waiting.** Junction History Down (30-commit TempRepo) is 14.9 KB a key with 1
  app build of 13.3 KB.
  - The keyed matches are 4 containers with 9 children, 1.13 KB (7.6 %); the two `ToArray` snapshots add 0.29 KB (2 %).
  - Elsewhere: Aperture 0.2–2.5 % a step; Vesper Tab 1.2 KB (4–7 % once the trail fix lands).
  - Expert 1's 1.7 KB `Dictionary` in a History Down is this.
  - It stays below the 10 % bar, and pools were declined (P5a). A no-pool path would remove ~1.1 KB of a History Down:
    for 8 children or fewer, match by a linear scan; write the matches into the incoming snapshot the pass already owns;
    no List and no final `ToArray`. Ride with the next TreeDiffer change.
- **part-place-stale-double-build: confirmed, below every bar** (−32.5 KB once a session; numbers in REVIEW). Two
  framework lines; fix it when `Part` is next touched.
- **aperture-poster-card-cost: measured** (7.1–7.8 KB a card; numbers in CANDIDATES). What is left is element bodies
  (P5c, closed) and per-item strings. The levers are the caption-in-card candidate, or keeping the page (question 75).
- **aperture-item-page-first-build: close; settled by 941cffdf, 0f5cda09 and d7eedc4b.** A film page with 14 cast is now
  141.5 KB warm (125 made, 2 app builds) and a plain film page 44–46 KB. The +34–50 KB from 0f9c8477 is below the
  150–165 KB that steps 7–8 took off. Expert 8 can confirm it on the real server.
- **junction-files-first-open: unchanged.** Once a session (TempRepo view switch 322 KB, 3 builds, 111 made). Leave it
  with expert 8.
- **junction-summary-overtaken-at-start: unchanged.** `AsyncLoader` and `SummaryFor` are untouched in both ranges.
- **junction-colours-before-rows-route-a: no new number;** keep waiting.
- **vesper-local-waiting-text: unchanged** (CommanderApp.Pane.cs is untouched by ca72af3e). Wording, the user's call.

EXAMINED, NOT WORTH IT:

- **Aperture grid and Home cursor keys:** 0.4–1.3 KB, 0 builds. Nothing to take.
- **TreeDiffer's two child snapshots:** 0.2–5 KB a step, 2 % of a History Down. Pools were declined.
- **ArtWait retention with healthy downloads:** 0 waits left after a settle. The stalled case is expert 4's, above.

NEEDS THE USER:

- **Re-ask question 75: keep the page Back returns to** (answered "(a) for now; ask again with steps 7–9's numbers").
  - Steps 7–9 are built. Back still makes the whole page: Home 285–297 elements, ~286–296 KB; a grid 292, ~286–288 KB,
    the same headless and in the real window.
  - Enter is 13–135 KB, so Back is 70–87 % of an open-and-back.
  - Keeping one page (Home or a grid) hidden while away retains ~300 elements (~0.25–0.3 MB), cuts Back about 85 %, and
    works against REACTIVITY.md's let-go rule.
  - (a) No. (b) Keep one page, Home or a grid, dropped on sign-out and on a scale or theme change. [(b)]

FILES (all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\e91920b9-3793-496a-9a73-f017250d8772\scratchpad\r19-reactivity\`):

- **Probes:** `probe19.patch` (r18b9's framework probes; TreeDiffer counters; element types; `PROBE_PLACE` in Part.cs;
  Vesper `PROBE_TRAIL`); `ProbeR19Tests.cs` and `WideShelves.cs` (Aperture: Walk, WalkNoArt, CardCost);
  `ProbeR19JunctionTests.cs`; `ProbeR19VesperTests.cs`.
- **Headless results:** `ap-walk-1.txt`, `ap-walk-2.txt`, `ap-place{0,1}-{1,2}.txt`, `ap-cardcost-2.txt`,
  `jn-actions-1.txt`, `vs-trail{0,1}-{1..4}.txt` (runs 3–4 hold the working prototype), `test-place-parts.log`,
  `test-trail-parts.log`.
- **Real window:** `start\` (host src, with a Vesper probe line added), `aotp\{Aperture,Vesper}\`, `rest\`
  (`common19.ps1`, `batch19a.ps1`, `sum19.py`, `runs\h1-*`), `sum-h1.txt`, `batch-h1.log`, `locks.log`, `desk-h1.txt`,
  `gate-h1.txt`, `fx\`.
- **Worktree:** clean at 56d9a3f6 (probes reversed with `git apply -R`, probe tests removed). Its test bins were last
  built with the probes in, so rebuild before using `--no-build`. Build server shut down.
