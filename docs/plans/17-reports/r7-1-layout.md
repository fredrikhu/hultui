# Round 7 survey, expert 1: Layout

SATISFIED: no

Measured at 57e6547c on JIT Release builds, counting calls and bytes only. Suite probe: warm 40 steps, then 20
measured, all 190 rows, compared row by row with round 6's probe on the 151 rows both have. Headless app walks used
`HULTUI_A11Y=0` (the suite's a11y rows install their own bridge and still report a11y bytes). Prototypes were gated by
`PROBE_PALBOX`, `PROBE_PALSEL` and `PROBE_FIELDS`. Every edit was reversed with `git apply -R`; the worktree was left
clean at 57e6547c.

## REVIEW

None. No commit from round 6 raised counted work in this area.

- **Suite:** layouts, measures, leaf measures, text measures, style resolves, hit visits, culls and z-order scans did
  not rise on any shared row. Byte rises are moves from landed commits:
  - text/styled ellipsis resize: render bytes 8.9 → 20.9 KB, but the row's total fell 1,081 → 361 KB (61b538c3 moved
    line runs to first draw).
  - a11y/type in rich text: render 3.8 → 4.8 KB, total 53.5 → 49.6 KB.
  - overlay/command palette filter 2,000: 335.3 → 68.9 KB a key (db606351). It now makes 14 rows a key, 55.4 KB of
    that in the row template.
- **StickyRows (2be205e5, 3a8e6c9e):** each sticky row costs about the same as its twin without sticky rows.

  | Row | Sticky rows | Without |
  |---|---|---|
  | Wheel | 11.37 KB | 11.05 KB (+3 %) |
  | Section walk | 12.28 KB | 11.98 KB |

  Hit visits are equal (19.6). Render visits rise by 1.9 (the held row and its gap). The sticky pass is O(n) per
  invalidation and allocates 0 B.
- **App walks, KB per key, round 6 → round 7:**

  | Walk | Round 6 → round 7 |
  |---|---|
  | Junction History Down | 44.4 → 20.9 |
  | Junction Branches | 93.2 → 47.0 |
  | Junction Commit | 58.5 → 58.2 |
  | Junction Tags | 34.4 → 34.5 |
  | Junction Files search | 56.4 → 56.5 |
  | Vesper same / other names | 41.8 / 36.6 → 30.5 / 25.4 |
  | Aperture Right / Down | 117.6 / 178.9 → 49.4 / 75.7 |
  | Aperture resize | 80.7 → 6.1 |
  | Junction Changes, 300 files, Down | 3,020 → 223 |
  | Junction Changes, 300 files, summary typing | 2,713 → 13.6 |

  - Resize steps are unchanged: 2.2 KB (History, Branches, Tags), 7.2 KB (Commit), 19.7 KB (Vesper).
  - Hover is 0.1–0.2 KB with 25–27 hit visits a move. History's list-drawn hover tint (d8e28f93) costs the same as
    before.
- **Changes wheel at 300 files (90fb4df7):** the approved virtualizing trade, not a regression. 9.2 KB a notch back
  and forth, 12.2 KB a notch walking down the list; a notch makes one row of 9 elements (row template 8.2 KB); 33 hit
  visits, 4.5 culls a frame.
- **ed11148e, dcb40b7c, 17504b64:** only run while a screen reader listens.
- **f76eb195:** runs once per window resize, O(open overlays).

## CANDIDATES

- id: layout-virtuallist-row-margins (folds Waiting palette-per-row-box)
  what: VirtualList lays a row's margin out inside the row's slot (height = slot minus vertical margins, placed at
  content X plus the left margin), so CommandPalette's rows carry the list padding as margins and drop the wrapping
  column box each row has today (CommandPalette.cs:438–457).
  evidence: count-only prototype `PROBE_PALBOX` (the box dropped; the padding lost in the prototype only).

  | Row | Base → no box |
  |---|---|
  | Suite filter 2,000 | 68.9 → 56.8 KB a key (−17.5 %); style resolves 64 → 50 |
  | Suite palette typing, UIA | 166.4 → 142.3 KB (−14.5 %) |
  | Suite palette typing, AT-SPI | 179.3 → 155.3 KB (−13.4 %) |
  | Junction palette, 300 branches, type+erase | 97.4 → 88.1 KB a step (−9.5 %); elements made 103 → 92.5 |
  | Junction palette, 300 branches, Down | 32.2 → 29.8 KB (−7.5 %) |

  expected: churn bar on three suite rows (−13…−17.5 %).
  cost: small to medium, no API change. Behaviour changes only for a row whose root element has a margin; none of the
  13 `.Margin(` calls in apps is on a VirtualList row root. Pixels should be identical (the box paints nothing); a
  pixel A/B is still needed. The other route (painting scrollbars at the padding edge) would change Junction Changes'
  list, which has `Padding(8,8,4,8)`.
- id: layout-palette-drawn-selection (owner 1, with 7 for the reader side)
  what: the palette's list draws the selection and the hover tint (`SelectionHighlight` / `HoverHighlight` with the
  row's corner radius), and rows are made unselected, so Down no longer remakes two rows a key.
  evidence: prototype `PROBE_PALSEL` (rows made unselected, no hover handler):

  | Row | Base → prototype |
  |---|---|
  | Junction palette Down | 32.2 → 15.3 KB (−52 %); rows made 2.5 → 0.5; elements made 32.2 → 14.3 |
  | With row margins as well | 32.2 → 14.9 KB (−54 %) |
  | Palette typing | −3 % |
  | Suite filter / UIA typing, both prototypes together | −24 % / −20 % |

  The 14 elements still made per Down are the card's rebuild. Setting the list's `SelectedIndex` through its ref
  instead of rebuilding the card would remove most of them (not measured).
  expected: churn bar on palette Down (−52 %). No suite row covers palette Down today; add one with a twin.
  cost: medium, no public API. Pixels identical only on top of row margins (the list draws the highlight at the row's
  border box, with the call `BoxElement` uses). Reader: rows must still say selected (question 7, kept); the palette's
  rows are composite and today only lists that publish item nodes tell selection, so the list must mark the selected
  row's node (expert 7).

## WAITING, RE-RANKED

- palette-per-row-box: now clears the churn bar on suite rows; folded into layout-virtuallist-row-margins.
- dropdown-follow-control-on-resize: not a performance item. Anchoring the list changes how a press on the control
  behaves; f76eb195 already keeps point-placed overlays on screen after a resize. Leave it to a correctness pass.
- layout-ordered-children: still low. Suite, per step: wheel over 5,000 rows 4,954 hit visits and 4,974 culls; hover
  over 1,000 rows 993 hit visits; loop/toast held 2 s 5,480 culls; signal tween 3,560; dense text screen 2,754. Apps:
  with Changes virtual, the most in 17 app walks is 33 hit visits a move and 13 culls a frame. The scaling bar holds
  on suite rows only; no app pays.
- layout-audit-padding-border: tooling, no number. Do it with the next audit change.
- layout-patch-keeps-layout-dropdown-expander: unchanged (16 / 9 elements per Fluent rebuild; one Junction dropdown;
  no app uses Expander). Not worth it.
- layout-virtuallist-spacer-stale: invisible and free; a one-line mark that belongs with an audit.
- runtime-element-layout-state, phase 2 lead: measured; not worth it alone. Each of Element's own fields compared
  with a fresh element's over 5,058 mounted elements (Junction, Vesper, Aperture, Changes). Element's own fields sum
  to 510 B; a new BoxElement is 584 B, a new TextElement 656 B. Fields non-default in at most 3 % of elements and not
  read per element per frame: `_name`, `OverlayOrigin`, `_pendingAnnouncements`, `_accessibleDescription`,
  `AccessibilityObserver` (0.1 %), `_refs` (1.8 %), `_automationId` (1.9 %), `_accessibleName` (2.1 %), `TabIndex`,
  `_dispatchDepth`, three `bool?` a11y states: about 78 B before padding, 64–72 B in practice. Per key (elements made
  × ~70 B): Commit −5.8 %, Branches −6.4 %, Tags −6.4 %, History −4.1 %, Aperture Right −6.1 %, Changes 300 Down
  −6.3 %, Changes wheel −6.7 %, Vesper −3.7 %. Retained 21–34 KB per app (−1.5…−2.3 % of Vesper's 1,449 KB heap).
  Under both bars. `_transform` (24 B, never set) and `_opacity` are read per element when drawing, the trap phase 1
  hit. `_overlayHost` and `FocusManager` are set on 29 % (copied down by the style pass).

## EXAMINED, NOT WORTH IT

- **Sticky rows' second O(n) pass** (`EnsureStickyRows`, per invalidation): Changes at 300 files makes 309 height
  calls and about 300 sticky calls a key, 0 B. The same class as round 6's RowHeights pass.
- **StickyRows twin cost:** +0.3 KB (+3 %) a wheel or walk step.
- **Changes Down at 300 files, 223 KB:** 170 KB is row templates, 18 rows a key, about 16 of them the diff pane's rows
  for the newly selected file. App area (expert 8).
- **1 kHz input rows:** scrubber hover is 12 hit visits a move and splitter drag 24. The splitter's 4.6 MB a step is
  486 style resolves a frame from rebuilding every frame (expert 6), not layout.
- **Commit resize, 1,709 MeasureSize a step:** examined in round 5; unchanged at 7.2 KB.

## NEEDS THE USER

None. Both candidates aim at identical pixels and no public API; the reader change in layout-palette-drawn-selection
keeps the reading the user chose in question 7.

## FILES

Scratchpad `r7-layout\` of session 855f4ddd: probe code `probe-r7.patch` (every edit, the palette prototypes
included), `LayoutProbe.r7.cs`, `filter_patch.py`, `probe-fw.patch`, `tops.py`; driver `appdrv\` (`Program.cs`,
`FieldStats.cs`, `Persist.cs`, `AppDrv.csproj`, built into `appdrv-bin\`); outputs `probe-head.txt`,
`cmp-r6-r7.txt`, `drv-base.txt`, `drv-pal-base.txt`, `drv-pal-box.txt`, `drv-pal-sel.txt`, `drv-pal-both.txt`,
`suite-pal-base.txt`, `suite-pal-box.txt`, `suite-pal-sel.txt`, `suite-pal-both.txt`, `drv-fields.txt`; review diffs
`sticky-vl.diff`, `sticky-misc.diff`.
