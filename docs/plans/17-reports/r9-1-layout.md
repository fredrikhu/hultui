SATISFIED: no

Measured at ea3e57ab, JIT Release, counting calls and bytes only. I reapplied round 8's probe (`probe-r8.patch` and `LayoutProbe.r8.cs`) cleanly. I added counters for `CollectGroupLabels` (`Extra2` = elements visited, `Extra3` = labels found, `Extra4` = calls) and three prototypes, each switched on by an environment variable: `PROBE_GROUPLIST`, `PROBE_GRIDPLACE`, `PROBE_BRIEFSCROLL`.
- **Suite probe:** 40 warm-up steps, then 20 measured, on all 214 rows, compared row by row with round 8's 213 rows.
- **App walks:** round 8's headless driver with `HULTUI_A11Y=0`, plus new walks: Vesper Brief and Thumbnails, Aperture Home, a Vesper viewer drag-select, and Aperture resizes across a column change.
- **Clean-up:** every edit was reversed with `git apply -R` and the probe file removed. The worktree is clean at ea3e57ab.

REVIEW:
- **d366505b (Aperture grid cards on `KeptElements`): a change of columns now remakes every visible card.**
  - **Cause:** `GridRow` hands a card back only into a row with its old row's key (`ApertureApp.Library.cs:396`, `TryKeep(..., place: key)`). The rule it replaced, round 7's `GridCards.TakeBack`, handed a card back into any row while its old row was still a child of the list.
  - **Evidence (headless, 600 films, 1440×900, KB a step):**

    | Walk | Before d366505b (round 8) | Now | Prototype (`place: null`) |
    |---|---|---|---|
    | Resize, 40 widths 1436–1280, one frame each | 6.1 KB, 2.9 elements made, 35.9 leaf measures, 11.6 text measures | 14.1 KB (+131 %), 12.5 made, 51.3 leaf, 16.4 text | 5.4 KB, back to round 8's counts |
    | Every step a column change (1440 ↔ 1300) | not measured | 236.1 KB, 249 made, 110 text measures | 63.0 KB (−73 %), 58 made, 14.5 text measures |

  - Rows made and builds are the same in both runs (72 and 12). The prototype only measures bytes; I did not check its pixels, ring or focus. This becomes candidate `apps-aperture-grid-columns-keep-cards`.
- **e7d10c63 (text selection in a scrolled view; a drag past the edge scrolls it):**
  - **Fine:** `PositionFromPoint` and `WithinSelectionView` now walk the ancestors (O(depth), 0 B). The idle check stops at its first condition (`_selectionScroll` null); while stalled at the view's end it asks for no frames. `TickSelectionEdgeScroll` reads the clock each frame, 0 B.
  - **Objection:** each new per-frame tick calls `UpdateGroupDrag`, which calls `RepaintGroup`. That walks the whole window tree and allocates a new `List` every call. This is old code, but it now runs every frame while the pointer is held past the edge, as well as on every move. This becomes candidate `layout-group-drag-label-list`.
- **4fc94836 (PaneSplitter):** O(1) per call; `SetMax` once a build. No cost.
- **7a12f5a9 and 2e10b549 (Aperture Home):** a Home rebuild is 11.2 KB with 4 elements made and 0 layouts. The shelves handed back mark no layout. Right and Down are 0.3 KB with 0 layouts.
- **4048e35b (Vesper grids):** keys that do not scroll cost 1.8 KB (Brief Down) and 1.5 / 1.7 KB (Thumbnails Right / Down), with 0 elements made and 0 layouts.
- **d366505b's post-frame focus callback:** runs only after a grid build, once (one delegate and one `ToArray`). Grid Right is 49.4 → 0.3 KB with 0 layouts; grid Down is 75.7 → 21.5 KB (the row scrolling in).
- **Suite:** no layout counter rose on the 213 shared rows. Every change is a drop from a landed commit (richtext open, enter and resize; a11y rows).
  - One rise is a probe artifact: `a11y/type in a rich text editor inside a VirtualList row` went 3.73 → 8.25 KB in steps 41–60, outside layout, a11y and render. The timed captures show it flat: 2.5 → 2.4 KB in 48ed3a35's and 61a82eba's before and after. Not a regression.
- **Other app walks against round 8:** within 1 % except landed drops: Vesper other-names Down 20.5 → 5.0; Commit resize 7.2 → 6.1; Changes resize 5.7 → 4.9.

CANDIDATES:
- id: apps-aperture-grid-columns-keep-cards (owner: 6 or 8, since the code is Aperture's; found by layout)
  what: Hand a grid card back into whatever row now holds its place, while its live row is still under the list, as round 7 did, instead of only into the row with its old key.
  evidence: `ApertureApp.Library.cs:396`. Resize walk 14.1 → 5.4 KB a step (−62 %); a step across a column change 236.1 → 63.0 KB (−73 %), 249 → 58 elements made, 110 → 14.5 text measures. Prototype: `place: null`.
  expected: churn bar on an app frame metric, by 62–73 %. It also removes d366505b's regression (+131 % on the resize walk).
  cost: small, app code only. Pixels should not change, but d366505b chose the rule on purpose ("a change of columns ... makes them"). The step must check ring, focus and pixels across a column change, plus d366505b's 7 scenes.
- id: layout-group-drag-label-list
  what: `CollectGroupLabels` (`Window.cs:2869`) reuses one scratch list instead of allocating one per call. Optionally, it walks from the drag's scroll view instead of the root.
  evidence: Vesper viewer on a 20,000-line log (290 elements, 37 labels on screen):
  - A drag move costs 1.2 KB, against 0.1 KB for a plain hover move. Every move walks all 290 elements to find 37 labels. The probe counted 290 visits a call, and the extra 1.1 KB matches a `List` grown to 64 entries for 37 labels.
  - With a reused list (`PROBE_GROUPLIST`): a drag move goes 1.2 → 0.0 KB; a frame with the pointer held below the view goes 3.7 → 2.6 KB (−30 %).
  - Callers: `UpdateGroupDrag:2781` (every move) and `TickSelectionEdgeScroll:2852` (every frame past the edge).
  - Junction's diff views and Vesper's viewer use selection groups.
  expected: churn bar, −96 % a drag move and −30 % an edge-scroll frame. The optional scoped walk clears the scaling bar: O(view's subtree) instead of O(whole tree) per move.
  cost: small; no API or pixel change. The per-label paint marks only feed the damage probe and tracer, because the frame is repainted whole. Add a suite row `text/selectable viewer drag select` next to the existing hover and wheel rows.

WAITING, RE-RANKED:
- brief-columns-lag-a-key (with 6): **now worth building, as an app fix.**
  - Cause: Vesper's `BriefGeometry` (`CommanderApp.Views.cs:612`) picks the columns to make from the old scroll offset. `OnAfterBuild` then scrolls (`CommanderApp.cs:842`), and the programmatic scroll fires no `ScrollEvent`.
  - Prototype (`PROBE_BRIEFSCROLL`): when a scroll is pending, the build uses the scroll `ScrollBriefToCursor` is about to set.
  - Brief, 400 files, Down 240 keys and back up:

    | Measure | Now | Prototype |
    |---|---|---|
    | KB a key | 5.2 | 4.6 (−11.5 %) |
    | Builds in 480 keys | 22 | 12 (one per scrolling key) |
    | Steps showing a column in sight not made (blank), in a 480-key round trip with 12 scrolls | 5 | 0 |

  - The framework alternative, a `ScrollEvent` on a programmatic scroll, would add a build per scroll and change every listener's behaviour. Not recommended.
  - Vesper Brief is the only `On<ScrollEvent>` user in apps and samples. Guideline: a view that picks its content from the scroll offset applies the scroll it is about to set before it builds.
- dropdown-follow-control-on-resize: not performance; Dropdown was untouched in round 8. Leave it to a correctness pass.
- layout-ordered-children: still low. Suite numbers are unchanged:

  | Row | Per step |
  |---|---|
  | wheel over 5,000 rows | 4,953.6 hit visits, 4,974.2 culls |
  | hover over 1,000 rows | 992.7 hit visits |
  | click checkbox in 500 rows | 1,493.1 hit visits |
  | drag to reorder 200 rows | 2,406 hit visits |
  | toast held 2 s | 5,480 culls |
  | signal tween | 3,560 culls |
  | dense text screen | 2,754 culls |

  Apps at most 41.5 hit visits a move (the new Vesper viewer walks).
- layout-audit-padding-border: tooling, no number. Do it with the next audit change.
- controls-menu-max-height-scroll: `ContextMenuElement` changed only for a11y (f35c3df9). Still not performance.
- layout-virtuallist-spacer-stale: `VirtualListElement` was untouched in round 8. Invisible and 0 B; do it with an audit.

EXAMINED, NOT WORTH IT:
- **`PositionFromPoint` through `ScreenContentBox` (e7d10c63):** O(depth) and 0 B per move or link hover. It was a field read, but it was wrong in scrolled views.
- **Vesper `MoveRing`'s linear search of `_ringMoved`:** O(cells restyled since the last build), at most what is on screen, 0 B.
- **The Aperture grid Down's 21.5 KB a key:** the row of about 8 cards scrolling in (21.7 KB of template bytes). The gesture's own cost.

NEEDS THE USER: none. The Aperture card rule restores round 7's behaviour, and the Brief fix is app code that removes a visible blank column.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\c484f5ac-8f22-48dd-b669-13c24b1e7889\scratchpad\r9-layout\`.
- **Probe code:** `probe-r9.patch` (every edit, the three prototypes included), `LayoutProbe.r9.cs`.
- **Driver:** `appdrv\` (new walks `vgrid`, `aphome`, `vdrag`, and the aperture resize additions), built into `appdrv-bin\`.
- **Suite build:** `suite-bin\`.
- **Scripts:** `cmp.py`, `cmpdrv.py`, `caprow.py`.
- **Outputs:**
  - Suite: `probe-head.txt`, `cmp-r8-r9.txt`.
  - App walks against round 8: `drv-head.txt`, `cmpdrv-r8-r9.txt`.
  - New walks: `drv-new.txt`, `drv-new2.txt`.
  - Aperture resize: `drv-ap-base.txt`, `drv-ap-place.txt`.
  - Viewer drag: `drv-drag-base.txt`, `drv-drag-list.txt`.
  - Brief walk: `drv-brief-base.txt`, `drv-brief-proto.txt`.
