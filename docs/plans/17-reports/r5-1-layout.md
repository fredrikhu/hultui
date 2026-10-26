# Round 5 survey: 1. Layout

At 969f3308 (the worktree was detached there; it is clean). Everything was measured as counts and bytes only, JIT Release, with no timings. Two probes:
- **Suite probe:** all 140 rows at warm 40 / steps 20, compared row by row with round 4's probe at 653971d on the 122 rows both have.
- **Headless app walks:** Junction (400 commits, 24 branches, 30 tags; a 2,300-file search), Vesper (400 files a pane, same and other names; viewer on a 20,000-line log), Aperture (600 films). New this round: resize walks across 40 widths in Junction History, Commit, Branches and Tags, in Vesper and in Aperture.

Prototypes were gated by environment variables. All probe edits were removed by a reverse `git apply -R`. Probe code and outputs are in the session scratchpad `r5layout/`:
- `apply_probe.py`, `apply_probe2.py` (selection prototype, twin counter), `apply_probe3.py` (miss tallies)
- `LayoutProbe.final.cs`, `probe-final.patch`
- `appdrv/` (driver), `cmp.py`, `tab.py`
- outputs `probe-head.txt`, `drv-*.txt`, `suite-sel-*.txt`

SATISFIED: no

## Review

None. Round 4's build caused no regression in my area.

**Suite rows.** The 122 rows shared with round 4 make no more of any counted work:
- layouts, ComputeLayout, MeasureSize and leaf measures
- text MeasureContent and style resolves
- hit visits and render visits, z-order scans, rows made

No row's allocKB rose more than 2 %, with two known exceptions:
- `overlay/dropdown open 200 items` +0.9 KB: 0ea667cf's label-heights array, as its commit says.
- `anim/signal tween` 368.8 → 372.7 KB (+1 %): a3aeabb2's themed twins.

Only calls to MarkLayoutDirty rose, never new marks:
- TableView sort and resize column 541 → 656
- gallery mount 529 → 604
- component/mount 2,702 → 3,002
- dialog open 17 → 20

These are themed controls' ApplyTheme marking a fresh element that is already dirty, which stops at the first step. New marks (LayoutMarkNew) stayed level or fell.

**One shift that is not a regression.** In the two "type beside a 150-line diff, rows kept" rows, the render pass went 0 → 1,472 B and the layout pass 2,208 → 736 B. The field's new text is now shaped when it is drawn, not in layout, because a3aeabb2 no longer lays the field out. Net bytes are the same.

**What fell:**
- Style marks down the tree went to about 0 everywhere (fbec1130), for example component/edit 18,004 → 0.
- anim/signal tween: 160 → 0 laid out per step.
- type beside the diff, rows kept: MeasureSize 159 → 5.
- menu bar open: 18 → 0 laid out.
- scrubber hover: 420.9 → 23.0 KB.

**App walks (KB per key):**

| Walk | Round 4 head → now |
|---|---|
| Junction History | 191.6 → 80.7 |
| Junction Commit | 189.8 → 79.1 (laid out 20.7 → 1.1) |
| Junction Branches | 215.6 → 104.7 |
| Junction Tags | 345.2 → 86.2 |
| Junction Files search | 176.8 → 66.0 |
| Vesper, same names | 401.9 → 231.1 |
| Vesper, other names | 225.1 → 225.6 (whole-window build; apps-vesper-components) |
| Aperture | flat |

- No walk allocates in the style or layout pass.
- Hover and wheel cost 0.1 KB and about 25 hit visits.
- Resize steps allocate 2.2 KB in Junction History, Branches and Tags and in Vesper, and 7.2 KB in Junction Commit.

**The CI branch** (6a089ebd^2) touches workflows, docs/CI.md, an mpv link flag and test timeouts. Nothing in layout.

## Candidates

### layout-virtuallist-selection-highlight
Owner 1 for the API; 8 for adopting it in Junction. Needs the user, question 1.

**What:** an opt-in highlight that the VirtualList draws itself under the selected row or rows, so a selection move makes no rows again. Rows on item rows are then made with `selected = false`.

**Why:** today an item-row list remakes the old and the new selected row on every move, because the row's background is drawn by the template:
- `DropRowsWhoseSelectionChanged` (VirtualListElement.cs:481), called from the `SelectedIndex` setter (:97–109).
- `DropGathered` (:492–505) also marks layout up to the root.
- The apps only change the row background: Junction History (JunctionApp.History.cs:1098–1104), Tags (JunctionApp.Tags.cs:265–266), Vesper's cursor fill and ring (CommanderApp.Pane.cs:896, :904–905).

**Evidence:** a counting prototype (`PROBE_SELPAINT`: rows treated as unselected; the cost of drawing one rectangle is not counted). Per key:

| Walk | KB/key | Other counters |
|---|---|---|
| Junction History | 80.7 → 56.6 (−30 %) | rows made 2.5 → 0.5 (template 30.2 → 5.9 KB); ComputeLayout 67.7 → 6.3; laid out in layout pass 9 → 0; MeasureSize 296 → 3; text MeasureContent 25 → 4.7; resolves 58.7 → 32.3 |
| Junction Tags | 86.2 → 39.4 (−54 %) | ComputeLayout 84.5 → 1.5 |
| Junction Branches | 104.7 → 98.4 (−6 %) | the compare pick is the selection and lives in the items (`BranchRowItem.Picked`, JunctionApp.Branches.cs:323/:341), so those rows are made again unless picks become the list's multi-selection |
| Vesper same / other names | 231.1 → 218.4 / 225.6 → 212.9 (−5…−6 %) | whole-window build dominates |
| Aperture | no change | its cursor is a card inside a row |

Suite rows, KB per op:

| Row | Before → after | Other counters |
|---|---|---|
| `virtual/selection walk 1,000 item rows` | 25.0 → 16.1 (−36 %) | rows 3 → 1; ComputeLayout 45 → 5 |
| `component/list walk beside chrome in kept parts` | 30.0 → 21.3 (−29 %) | |
| `a11y/selection walk 1,000 item rows, bridge on` | 112 → 103 (−8 %) | |
| `input/key press … real loop` | 125 → 116 (−7 %) | |
| kept-chrome a11y walk | | MeasureSize 407 → 2 |

**Expected:** clears the churn bar on Junction History and Tags and on two suite rows (−29…−54 %). The time bar is likely on the item-row walk, since two of three rows are no longer made, styled, laid out or put in the a11y tree; to be measured in the build phase, AOT Junction too.

**Cost:** medium.
- A public API addition, for example `.SelectionHighlight(background, border?, cornerRadius?)`, drawn at the selected rows' rectangles before the rows. With multi-selection it highlights each selected row on screen.
- App adoption: History picks Accent900 for the selection or Neutral900 for a caret outside it on each build, and keeps today's template path while a multi-selection is open. Tags is a plain change.
- Pixels stay identical if the list draws the same fill and radius the row drew and the rows' own background becomes transparent.
- Branches and Tags rows are keyed by focus to reset their hover tint; that needs care.

### layout-virtuallist-offsets-kept
Owner 1. No API change, no pixel change.

**What:** a list with RowHeights works out its row offsets into the array it already has, and only once per patch.

**Why:**
- Every patch of the owner allocates a new `float[n+1]`: `KeepUnchangedRows` (VirtualListElement.cs:904–916) through `EnsureOffsets` (:509–525). This happens even when no height moved, and the new array is kept while the equal old one is thrown away.
- When the app called `InvalidateRows` first, `InvalidateItems` (:458) nulls `_offsets`. The patch then sees them as moved, nulls them again, and the layout pass builds a second array.

**Evidence:** Vesper's viewer on a 20,000-line log, typing in the find bar:
- 2 arrays of 20,001 floats per keystroke = 160 KB of 1,702 KB (9.4 %).
- The layout pass's 80 KB per key is one of them. The same holds with wrap on.
- Lists using RowHeights: Vesper viewer and diff (CommanderApp.Viewer.cs:554, CommanderApp.Diff.cs:751), and Junction's wrapped and review-card diffs (JunctionApp.Diff.cs:569). Each pays 4(n+1) B per rebuild at least.
- Above 21,250 rows each array lands on the large object heap.

**Expected:** the scaling bar for allocation: O(n) bytes per rebuild → 0, and one O(n) pass over the height function instead of two. −9.4 % bytes on the viewer walk, just under the 10 % bar on its own. A suite row ("rebuild beside a 20,000-row RowHeights list") would pin it.

**Cost:** small. Fill a kept spare array, compare it with the old one, swap only when they differ, and keep the computed array instead of nulling it. It could share a build step with the candidate above.

### layout-ordered-children
Low; unchanged from round 4. Binary-search hit testing and culling over ordered flex children (HitTester.cs, ElementRenderer.cs). Hit visits per step, all from suite rows:
- scroll/wheel 5,000 rows: 4,954, plus 4,974 culls a frame
- hover across 1,000 rows: 993
- click in 500 rows: 1,493
- drag 200 rows: 2,406
- a11y/wheel 1,000 rows: 954

Every app list is virtual (about 25 visits a move in Junction). Medium cost.

## Examined, not worth it

- **A VirtualList measured as a leaf** (new evidence from resizes):
  - Key walks are cache hits, as round 4 found.
  - A resize step misses: each realized row is measured twice, once at the flex-basis width and once at its own. Vesper: 60 grid rows at the window-wide basis width (1,234) and 60 at the pane width (594). MeasureSize 1,551 with 1,204 misses; leaf measures 1,969 with 824 misses. Grids hold their measurement at one exact width only (LayoutEngine.cs:609).
  - The as-leaf prototype cut MeasureSize misses by 25 % and leaf measures by 21 %, but it moved geometry (Junction Commit resize text measures 15 → 60, as lists came out a different size). That is the semantics change round 4 named.
- **FlexBasis(0) on Vesper's dual panes** (an app guideline, not a framework change):
  - The basis probe becomes a constant zero-width measure, which stays cached.
  - Resize misses 1,204 → 964 (−20 %); leaf measures 1,969 → 1,635.
  - Vesper's Quick View already does it (CommanderApp.cs:724).
- **Bytes for themed twins** (a3aeabb2): 2–5 twins a key at 72–216 B each, 144 B (History) to 1,080 B (Vesper) per key, at most 0.9 % of a key.
- **Relayout of every sibling on a leaf edit:**
  - `a11y/edit 1 label of 1,000 rows` lays out 4 elements but visits 1,004 ComputeLayout (1,001 clean) and makes 1,001 MeasureSize hits per edit; component/edit is the same (1,007 / 1,001).
  - Stopping at an element whose size comes out unchanged (a relayout boundary) is a large engine change.
  - App lists are virtual, so n is about 30 realized rows.
- **A selection move marks layout up to the root:** 9 elements laid out and about 290 MeasureSize hits per History key. Covered by the first candidate; alone it is 1–2 %, as round 4 said.
- **Junction Commit resize:** 1,709 MeasureSize a step (1,389 hits). Its two lists are measured 48 times a step at different (width, forced height) pairs, with 88 row re-measures. It is the same double row measure as above plus nested flex probes, on resize only; no suite row.
- **Junction Files:** 96 TextArea leaf misses per key. Its MeasureContent returns zero, so they cost almost nothing (as rounds 3 and 4 found).
- **layout-virtuallist-spacer-stale:** still invisible and still free. The fix is a one-line mark on the spacer; it belongs with an audit, not with performance.
- **Leads for expert 8, and for 5 if TextSearch is theirs:**
  - Vesper viewer find typing on a 20,000-line log costs 1,702 KB per keystroke. The key handler and build take 1,352 KB (the search over 20,000 lines plus the whole-window build). The frames take 360 KB: 35 rows made again per keystroke (280 KB, because the find look invalidates every row) plus the offsets above.
  - Aperture rebuilds on every resize step (`OnWindowResize` → `MarkDirty`, ApertureApp.cs:149–162): 92 KB, 63 patches and 77 resolves per step in the grid. Building only when the column count changes would fix it, as e6a432fa did for Vesper.

## Needs the user

1. **layout-virtuallist-selection-highlight:** may a VirtualList draw the selected row's highlight itself, opt in per list, so an app's rows no longer show the selection and a selection move makes no rows again? It is a visual API: fill, optional ring, corner radius, drawn under the row. Junction History −30 % bytes per key, Tags −54 %, suite item-row walk −36 %. [Yes, opt in, adopted one list at a time like item rows.]
