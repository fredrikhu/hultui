# Round 8 survey, expert 1: Layout

SATISFIED: yes

Measured at 27020cdb (JIT Release, counting calls and bytes only). The round 7 probe (`probe-fw.patch` and `LayoutProbe.r7.cs`) was reapplied with `--3way`. I fixed its VirtualList conflict and added counters for moved rows (`Extra8`) and for the new margin-stretch branch (`Extra1`).
- **Suite probe:** warm 40 steps, then 20 measured, all 213 rows, compared row by row with round 7's probe (57e6547c) on the 190 rows both have.
- **App walks:** round 7's headless driver with `HULTUI_A11Y=0`, 33 walks, including Fluent.
- **Prototypes:** gated by `PROBE_NOMARGIN` and `PROBE_PALMOVE`.
- **Clean-up:** every edit was reversed with `git apply -R`. The worktree is clean at 27020cdb.

REVIEW: none. No commit of round 7's build raised counted work in this area.
- **Suite:** no layout counter rose on any of the 190 shared rows. That covers layouts, measures, leaf measures, text measures, style resolves, hit visits, culls and z-order scans. Every change of 3 % or more is a drop from a landed commit:
  - overlay/command palette filter 2,000: 68.9 → 52.6 KB a step (style resolves 64 → 50, layouts 92 → 78)
  - palette typing: UIA 166.3 → 133.7 KB, AT-SPI 178.3 → 145.7 KB
  - text and a11y rows: drops from 73819ebd, d748b316 and step 7
  - One rise, outside layout: `a11y/selection walk beside details, keys 60 ms apart, real loop, UIA`, a11y bytes 672 → 1,446 B. The row's total only went 120.9 → 121.6 KB (+0.6 %), and the 500 ms twin is flat (800 = 800 B). It looks like real-loop timing; for expert 7 if anyone.
- **New rows:**
  - virtual/filter, rows follow their item: 3 rows made and 16 moved a step (twin: 19 made).
  - virtual/insert above: 1 made and 19 moved (twin: 20 made).
  - overlay/command palette Down 2,000: 14.4 KB a step in the probe, of which 10.9 KB is the text measure of the one row scrolling in. These are first visits to unique labels (the probe measures steps 41–60). The timed suite row shows 3.9 KB. That is the text area, not layout.
- **39e60064** (a stretched column child with side margins is measured at line minus margins). Gated A/B over 33 app walks:
  - The branch fires in one walk only: Junction palette type+erase, 21.5 times a step.
  - There it lowers MeasureSize 169.8 → 124 (−27 %) and leaf measures 152.5 → 140. Bytes are equal.
  - Every resize walk has identical counts (Commit 1,709 MeasureSize a step, Vesper 1,577.7). It is a small win, not a regression.
- **VirtualList commits (ca4a0079, b1c2894e, ae3c5e70, 3ecbedfa):** costs per action, all 0 B in steady frames.

  | Code | Cost |
  |---|---|
  | `TakeMovingRow` | O(moved rows) per realized row, only after a patch (at most ~30² compares) |
  | `TellRowsSelected` | O(window) per selection move |
  | `HoverRowFor` | one call per drawn frame |
  | `RowHeight` margins | a field read |

- **App walks, KB per key, round 7 → round 8:**

  | Walk | Round 7 → round 8 |
  |---|---|
  | Junction History Down | 20.9 → 20.5 |
  | Junction Commit / Branches / Tags / Files search | 58.2 / 47.0 / 34.5 / 56.5, unchanged |
  | Junction palette type+erase | 97.4 → 84.9 |
  | Junction palette Down | 32.2 → 3.7 (3.9 elements and 0.5 rows a key) |
  | Vesper same / other names | 30.5 / 25.4 → 25.6 / 20.5 (2aa55491) |
  | Aperture Right / Down | 49.4 / 75.7, unchanged |
  | Junction Changes Down, 60 / 300 files | 212.4 / 223 → 195.3 / 206.5 |
  | Junction Changes wheel back+forth / walk | 9.2 / 12.4 → 8.9 / 11.9 |

  - Resize steps are unchanged: 2.2 KB (History, Branches, Tags), 7.2 (Commit), 19.7 (Vesper), 6.1 (Aperture), 5.7 (Changes).
  - Hover is 0.1–0.2 KB with 25.5–26.9 hit visits a move.
- **f64e4674:**
  - 534dfcf8: drag moves hit-test the open overlays before the tree, O(open overlays), drags only.
  - 86da0e3a and e60fd2ee: run once per action.
  - d04464f6 and 5715d0df: a struct guard and one `RestoreToCount(1)` a frame, 0 B.
  - 4da0d543: `LocalPosition` now walks the ancestors, O(depth) and 0 B without transforms. Read 8 times in Core controls, never in apps.
  - caf2e17f: the opt-in `GrowsWithContent` re-wraps the whole text on each keystroke (O(text)). No app uses it.

CANDIDATES: none.

WAITING, RE-RANKED:
- dropdown-follow-control-on-resize: not a performance item, and unchanged. Leave it to a correctness pass.
- runtime-element-layout-state phase 2 (shared with 4): still under both bars. Element, Box and Text fields are unchanged in round 7. Elements made × ~70 B, as a share of today's bytes per key:

  | Walk | Share |
  |---|---|
  | History | 4.2 % |
  | Commit | 5.8 % |
  | Branches | 6.4 % |
  | Tags | 6.4 % |
  | Files | 5.8 % |
  | palette type+erase | 7.6 % |
  | palette Down | 7.4 % |
  | Vesper | 4.0–4.5 % |
  | Aperture | 6.1 % |
  | Changes 300 Down | 6.3 % |

  All are under the 10 % churn bar, and retained memory is −1.5…−2.3 % (round 7), under 5 %. Not worth it alone.
- layout-ordered-children: still low; the scaling bar holds on suite rows only, no app pays. Numbers are unchanged. Suite, per step:
  - wheel over 5,000 rows: 4,953.6 hit visits, 4,974 culls
  - hover over 1,000 rows: 992.7 hit visits
  - click checkbox in 500 rows: 1,493 hit visits
  - drag to reorder 200 rows: 2,406 hit visits
  - toast held 2 s: 5,480 culls
  - signal tween: 3,560 culls
  - dense text screen: 2,754 culls

  Apps: at most 33.5 hit visits a move (Changes wheel) and 13 culls a frame (Branches resize).
- layout-audit-padding-border: tooling, no number. Do it with the next audit change.
- layout-patch-keeps-layout-dropdown-expander: not worth it. A Fluent rebuild still lays out 16 elements (Gallery: 35 layouts, 19 clean) and 9 (Personalization: 22, 13 clean). Round 7 touched Dropdown only for a11y (0a47dded, 110fcd27).
- controls-menu-max-height-scroll (shared with 2): `ContextMenuElement` still has no maximum height or scrolling. It is a reachability issue, not performance.
- layout-virtuallist-spacer-stale: unchanged. Invisible, 0 B; a one-line mark that belongs with an audit.

EXAMINED, NOT WORTH IT:
- **Palette rows follow their item** (compare `ListRow` without its index, only "first or not"; key and click by command; prototype `PROBE_PALMOVE`):

  | Row | Base → prototype |
  |---|---|
  | Junction palette type+erase | 85.0 → 78.3 KB (−7.9 %); rows made 10.8 → 9.8, 1 moved a step |
  | Suite palette typing, UIA | 134.1 → 132.6 KB (−1.1 %) |
  | Suite palette typing, AT-SPI | 147.0 → 144.2 KB (−2.0 %) |
  | Suite filter 2,000 | no change (its six queries share no visible rows) |

  It is under the 10 % bar, and it would change the documented `palette-row-N` automation ids (ELEMENTS.md, REACTIVITY.md).
- **Junction Files search Down, 149 MeasureSize misses a key:** 96 are TextArea leaf misses at 0 B (examined in round 3). The rest are ancestors of a changed label, measured at 4 widths (1,400 / 1,134 / 1,100 / 834). That is the relayout-boundary item from round 5.
- **grid/edit auto-column cell:** 5,101 measures and 542 layouts per edit. The auto column's width changes, so the fr columns and all 300 rows move. The scenario is built to show exactly this.
- **TableView column resize:** remakes all 30 visible rows (233 of 254 KB a step). No app uses TableView (round 4).
- **Vesper same-names Down:** 37 MeasureSize misses for 16 elements made. The tally counts about 13 misses a step: the pane boxes and the two lists at the two pane widths. Small.

NEEDS THE USER: none.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\64e7e0e2-bc1c-4069-9d42-9311fee04c9e\scratchpad\r8-layout\`.
- **Probe code:** `probe-r8.patch` (every edit, both prototypes included), `LayoutProbe.r8.cs`.
- **Scripts:** `cmp.py` (suite rows), `cmpdrv.py` (app walks), `tops.py`.
- **Driver:** `appdrv\`, retargeted to this worktree and built into `appdrv-bin\`.
- **Outputs:** `probe-head.txt`, `cmp-r7-r8.txt`, `tops-head.txt`, `drv-head.txt`, `drv-margin.txt`, `drv-nomargin.txt`, `cmp-margin.txt`, `drv-pal-base.txt`, `drv-pal-move.txt`, `suite-pal-base.txt`, `suite-pal-move.txt`, `drv-files-measure.txt`, `drv-vesper-measure.txt`.
