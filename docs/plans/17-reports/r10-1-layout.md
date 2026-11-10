# Round 10 survey, expert 1: layout

SATISFIED: no

I measured at 3462f18e (JIT Release, headless, `HULTUI_A11Y=0`), counting calls and bytes only. I reapplied round 9's
probe without its three prototypes, which have since landed. I added counters for hover settles and re-hit-tests, the
walk in `IsShownInWindow`, and `ForEachGroupLabel` visits.
- **Suite probe:** 40 warm-up steps, then 20 measured, on 221 rows (214 shared with round 9).
- **App walks:** round 9's driver plus three new probes: a Vesper Brief scrollbar walk, a Meridian Discovery map probe
  (60, 600 and 2,000 endpoints, using round 9 step 11's spec files), and a heap check with `dotnet-dump` and `gcroot`.
- **Clean-up:** every edit was reversed with `git apply -R` and the probe file removed. The worktree is clean at
  3462f18e.

REVIEW: none.
- **Suite:** no layout counter rose on the 214 shared rows. The only changes are drops on a11y rows, from the landed
  5c9d6b8b.
- **App walks against round 9:** equal, except for landed drops:
  - Aperture resize: 13.9 → 5.3 KB a step, 12.5 → 2.9 elements made.
  - Aperture step across a column change: 63.0 KB, 58 made.
  - Vesper viewer drag move: 1.2 → 0.0 KB. Held below the view: 3.7 → 2.6 KB.
  - Brief Down/Up across columns: 4.6 KB, 12 builds in 480 keys, 0 steps with a blank column.
- **ff6125a6 (hover settle):** in the suite, at most 2 settles a step, on 13 rows, and at most 8 parents walked. On
  Junction's Changes wheel walk the hovered row is re-hit-tested once in 10 notches: hit visits rise 32.9 → 36.2 a notch,
  with 0 B. No objection.
- **25a96234 (`KeptElements` prune):** each new element costs amortized O(1), since the prune runs at 64 entries and
  then each time the count doubles.
- **0a9a9c64:** `CanTakeCard` is O(1).
- **Kept grid across a change of columns:** only the new cards miss the measure cache (3.5 cards a step: 7 text and 3.5
  image misses). Kept cards hit.
- **6801d779:** 290 visits a move in Vesper's viewer and 1,202 on the suite's 400-line viewer, at 0 B. Round 9 declined
  the scoped walk.

CANDIDATES:
- id: layout-wheel-latch-holds-removed-view
  what: The wheel latch (`WheelGesture._latched`, PointerEvents.cs:197) holds the last scroller it latched to until the
  next wheel event anywhere in the window. A view that a build removes after a wheel therefore stays alive through that
  scroller's children. The fix lets go of the latch at the end of a frame that changed the tree, when the scroller is no
  longer shown.
  evidence:
  - **`gcroot` path:** `Window._wheelGesture` (Window.cs:63) → `_latched` → the map's `ScrollViewElement` → `ChildList`
    → the rows. It was the only root.
  - **Walk:** Meridian Discovery with an imported spec. The pointer goes over the map, either with 4 wheel notches or
    with none, then off the map, then 30 frames. Key `1` opens the Requests view, then 4 frames. The heap is read after
    three full GCs.

    | Endpoints | No wheel (heap with map → Requests view) | Wheel, now | Wheel, prototype |
    |---|---|---|---|
    | 60 | 1,123 → 842 KB | 1,125 → 1,125 | 1,125 → 844 |
    | 600 | 4,317 → 1,819 KB | 4,317 → 4,317 | 4,317 → 1,819 |
    | 2,000 | 14,286 → 6,065 KB | 14,287 → 14,287 | 14,285 → 6,056 |

  - **Duration:** the tree stays held after 2 s and after pointer moves.
  - **Other ways of leaving:** a click on a row or a programmatic focus lets the map go. An early reading that a click
    held it was my probe's own stack temporary.
  - **Prototype:** in `NoteStructureForHover` (Window.cs:3150), when `_root.DirtyFlags` has `Children` and the latched
    scroller fails `IsShownInWindow`, call `WheelGesture.Release()`. `Release()` is an internal setter that clears
    `_latched`. Saved in `proto-wheel-latch-gesture.patch` and `probe-r10-full.patch`; the switch is `PROBE_WHEELLATCH`.
  expected:
  - **Retained memory bar:** the managed heap after a view switch drops 25 % at 60 endpoints and 58 % at 600 and 2,000
    (2.5 MB and 8.2 MB). This closes a retention.
  - **Scope:** any app view that is wheeled and then removed by a build is held the same way (Junction's view switches,
    Aperture leaving the library). I measured only Meridian.
  cost: small. No API change and no behaviour change: `LatchFor` already lets go of a scroller that is not an ancestor
  of the target, and a removed scroller never is one. The cost is one O(depth) walk, only in frames that changed the
  tree while a latch is held, and 0 B.
  - **Build notes:** the check must come before `NoteStructureForHover`'s early return when nothing is hovered (the
    pointer may have left the window), and it must also cover an overlay that closes while it holds the latched
    scroller.
  - **Test:** wheel a ScrollView, build it away, run a full GC, and check that a weak reference to it is dead. The
    latching tests must still pass.
- id: apps-meridian-map-virtual-list (owner 6 or 8; found by layout)
  what: Discovery's endpoint map is a ScrollView over a plain column of every row and heading
  (MeridianApp.Discovery.cs:524–525). As a `VirtualList`, it would turn the hit-test, the cull, the z-scan and the
  reconcile into work for the rows on screen only.
  evidence: per step, headless, from the probe counters:

  | Endpoints | Children of the map's column | Elements | Hit visits a hover move | Hit visits / culls a wheel notch | Culls a drawn frame | Per click: hit visits, `ZScans`, layout marks | Map's elements on the heap |
  |---|---|---|---|---|---|---|---|
  | 60 | 72 | 321 | 77.5 | 64.6 / 36.5 | 36 | 226, 76, 123 | 281 KB |
  | 600 | 612 | 2,481 | 617.5 | 602 / 579 | 579 | 1,847, 616, 663 | 2,498 KB |
  | 2,000 | 2,040 | 8,109 | 2,046 | 2,030 / 2,007 | 2,007 | 6,131, 2,048, 2,098 | 8,230 KB |

  - **Bytes:** a click costs 54.8 / 77.3 / 124.0 KB. The growth with the map is the column's children, about 35 B an
    endpoint, the cost round 9 noted. A hover move allocates 0.0 KB and a wheel notch 0.1 KB.
  - **Hit test:** `HitTester` (HitTester.cs:78–84) tests every child of the column against the point. The scroll view's
    clip does not prune any of them, because the point is always inside it.
  - **Docs:** GRID.md:226 already says to prefer `VirtualListElement` for thousands of rows.
  expected:
  - **Scaling bar:** O(endpoints) becomes about 30 rows' work per hover move, wheel notch and drawn frame. At 600
    endpoints that is 618 → about 30 hit visits.
  - **Retained memory:** the map's elements drop from 2.5 MB to about 0.1 MB (my estimate: about 40 realized rows), 58 %
    of the heap with the map shown at 600 endpoints. This is independent of the latch fix, which helps only after the
    map is left.
  cost: medium, app code, no API change.
  - **Readers:** a reader sees only the realized rows unless the list publishes item nodes. Check with expert 7.
  - **Kept rows:** the rows on `KeptElements` move into the list's row template, and the group headings need
    `RowHeights`.

WAITING, RE-RANKED:
- vesper-brief-filter-clear-reveal (with 6): **verified, and worse than the note said.** The scrollbar sets the offset
  without a `ScrollEvent` (Scrollbar.cs:161 for a track click, :190 for a thumb drag). Brief makes its columns from the
  offset and rebuilds only on a `ScrollEvent` (CommanderApp.Views.cs:586).
  - **Walk:** 400 files, a 1240×820 window, left pane.
    - A track click jumps to offset 2,197: 4 of 4 columns in view are blank, with 0 builds.
    - A thumb drag in 40 px moves: a column is blank from the 2nd move, and 4 of 4 from the 5th. Over the drag, 7 of 8
      moves show a blank column, with 0 builds.
    - The next Down key scrolls back to the cursor.
  - **Result:** scrolling Brief with the scrollbar shows an empty pane. The decision is under NEEDS THE USER.
- layout-ordered-children: still not worth building as a framework change. Meridian's map is the first app case (618
  hit visits a move and 579 culls a frame at 600 endpoints, against at most 33.5 / 13 in apps in round 9). The app's
  `VirtualList` (candidate above) fixes more: it also removes the elements, the z-scan and the reconcile. Suite rows are
  unchanged; the 400-line selectable viewer rows show 384 hit visits a move.
- dropdown-follow-control-on-resize: Dropdown was untouched in round 9. Correctness, not performance.
- layout-audit-padding-border: tooling with no number. Do it with the next audit change.
- controls-menu-max-height-scroll: `ContextMenuElement` was untouched in round 9. Not performance.
- layout-virtuallist-spacer-stale: `VirtualListElement` was untouched in round 9. Invisible and 0 B; do it with an
  audit.

EXAMINED, NOT WORTH IT:
- **Junction Files search Down's 96 TextArea measure misses a key:** one non-growing TextArea is measured at 4 widths
  times about 12 heights or forced heights in each layout. Its `MeasureContent` returns `Size.Zero`, so each miss is
  O(1) and 0 B. The box misses around it belong to expert 8's Waiting document-switch item (149 misses a key).
- **ff6125a6's per-frame cost:** one flag read a frame, plus one walk up of about 7 parents in a frame that changed the
  tree. 0 B.
- **Kept rows in Meridian's tree and Aperture's grid:** a moved kept card hits its measure cache. Only new elements
  miss.

NEEDS THE USER:
- **Brief's scrollbar shows blank columns (vesper-brief-filter-clear-reveal, the verified half).** Choose a fix:
  - (a) Framework: ScrollView tells listeners when the user scrolls with the bar or by autoscroll, through a new public
    callback or event. Brief listens to it alongside `ScrollEvent`. This adds public API.
  - (b) Brief hides its horizontal scrollbar. Users notice the change.
  - (c) Brief makes every column. 400 files is 15 columns, about 400 cells a build against the 5 columns made now.
  - **Recommendation:** (a). It changes no existing listener, because a scroll set from code still raises nothing.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\3484c586-c631-4af0-957d-95e7a03cac41\scratchpad\r10-layout\`.
- **Probe code:**
  - `probe-r10-full.patch`: every edit, including the latch prototype behind `PROBE_WHEELLATCH`.
  - `LayoutProbe.r10.cs`
  - `proto-wheel-latch-gesture.patch`
  - `filter_patch.py`: makes the probe from round 9's patch.
- **Drivers:**
  - `appdrv\`, built into `appdrv-bin\`: round 9's walks plus the new `vbar` walk.
  - `mer\`, built into `mer-bin\`: the Meridian map probe and its `latch` mode.
- **Suite build:** `suite-bin\`.
- **Scripts:** `cmp.py`, `cmpdrv.py`.
- **Outputs:**
  - Suite: `probe-head.txt`, `cmp-r9-r10.txt`, `tally-kgrid2.txt`.
  - App walks: `drv-head.txt`, `cmpdrv-r9-r10.txt`, `drv-new.txt`, `drv-vbar.txt`, `drv-files-measure2.txt`.
  - Meridian map: `drv-mer4.txt` (counters).
  - Latch: `drv-latch-final.txt`, `drv-latch-final1.txt`.
  - `gcroot.txt`: the root path.
