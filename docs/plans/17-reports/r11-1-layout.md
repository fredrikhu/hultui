SATISFIED: no

I measured at dee48fbb (.NET 11, SDK 11.0.100), JIT Release, headless, with `HULTUI_A11Y=0`, and report counts only. I re-applied round 10's probe without the landed latch prototype and added two tallies: MeasureSize calls per element and parent, and ComputeLayout calls with the reason each one missed its cache.
- **Suite probe:** 230 rows, 40 warm-up steps then 20 measured.
- **App walks:** round 10's walks (junction, files, palette, vesper, aperture, changes).
- **Meridian map probe:** round 10 step 11's spec files at 60, 600 and 2,000 endpoints. It now also hashes pixels and geometry after 8 clicks.
- **Clean-up:** every edit was reversed with `git apply -R`. The worktree is clean at dee48fbb and the build server is shut down.

REVIEW: none.
- **Suite, round 10 (.NET 10) against now (.NET 11):** on the 221 shared rows, no layout counter moved by 3 % or more. The only change is allocKB on `overlay/dropdown open 200 items`, 38.77 → 37.17, which the upgrade already recorded.
- **App walks against round 10:** every layout, measure, hit and render counter is equal. KB a step is down 0.1–1.1 everywhere (for example Junction changes300 Down 206.2 → 205.1).
- **.NET 11's optimized JIT:** a suite run with `DOTNET_TieredCompilation=0` (every method fully optimized at first call, which is where c99e23bf's boxing showed) allocates no more than the default run on any row. Only three rows differ by 3 % or more:
  - the two rich-text-in-a-VirtualList a11y rows drop, 8.25 → 3.68 KB. That is tier-0 code inside the probe's short warm-up, which the timed suite (warmed for at least 5 s) does not see.
  - `media/video player`, a real-loop row, moves +4.8 %.
- **Round 10's layout commits:** 00b8fc79's held offsets cost one NaN check per read. `CanClampY` on a VirtualList calls `EnsureOffsets` earlier, which moves the same work rather than adding any. 2f7ab168 and 1c3cf5c5 cost 0 B and a few comparisons.

CANDIDATES:
- id: layout-scroll-forced-height-measure
  what: A column that scrolls down (`OverflowY == Scroll`) measures its children against no height, so a forced height changes only its own height. The proposal gives such a column the unforced measurement at the same width with the forced height applied, as `MeasureLeafSize` already does for width-only leaves. Today each forced height takes its own slot in the 4-slot `MeasureCache`, and every miss walks all the column's children.
  evidence:
  - **Cause of virtuallist-selection-relayout:** Meridian's map list is measured at 16 distinct inputs a click: 4 widths (1440, 1392, 616, 568) times 4 forced heights (none, 0, 864, 824). That thrashes the 4 slots, giving 24 list misses a click. Each miss flex-measures every realized row (`StretchesInsideMargins` counter 912 a click = 24 × 38 rows). 768 of the click's 869 MeasureSize calls are its rows.
  - **Meridian map click, before → after:**

    | Endpoints | MeasureSize | Rows flex-measured | MeasureLeaf |
    |---|---|---|---|
    | 60 | 869 → 245 | 912 → 152 | 504.5 → 124.5 |
    | 600 | 941 → 257 | 864 → 144 | 144 → 64 |
    | 2,000 | 943.9 → 259.9 | 864 → 144 | 149.9 → 69.9 |

    ComputeLayout and KB a click are unchanged (22.2 / 22.2 / 30.4). Pixel and geometry hashes are identical after all 24 verify clicks.
  - **Suite, 230 rows:** MeasureSize falls on about 60 rows, for example:
    - `a11y/selection walk beside details and kept chrome parts`: 407 → 181
    - `component/worker tick beside kept parts` (both rows): 162 → 56
    - `component/list walk beside chrome` (both rows): 189 → 93
    - `input/key press, down and up 70 ms apart, real loop`: 191.8 → 91.1
    - `text/find Enter beside a 3,000-row list` (both rows): 162 → 94
    - `table/TableView 10k sort` and `resize column`: 64 → 35
    - `virtual/selection walk 1,000 item rows` and its a11y twins: 54 → 36

    About 10 rows rise by 1–2 calls (the extra unforced lookup, for example 3 → 4). No ComputeLayout count, allocKB or other counter moved on any row.
  - **App walks:**
    - Junction commit Down: 85.5 → 17.3
    - Junction branches Down: 97.5 → 46.3
    - Junction files search Down: 518.8 → 261.8
    - Junction palette typing: 124 → 117.3
    - Vesper same-names Down: 141.3 → 81.4

    The other walks and all KB are unchanged.
  - **Tests with the shortcut on:** Core 5,078 and Headless 1,553 (golden images included) pass.
  expected:
  - **Time bar:** to be confirmed by step-ab, on the rows where measure calls halve or more, and on the Meridian map click. That click is where step 11 left JIT +14 % at 60 endpoints, and this removes 72 % of its measure calls.
  - **Scaling:** a forced-height measure of a scroll view no longer walks its children once per forced height. That walk is O(direct children), which for a ScrollView holding rows directly is O(n).
  - **Memory:** 0 B either way.
  cost: small. About 10 lines in `LayoutEngine.MeasureSize`. No API change and no pixel change in what I compared.
  - **Exclusions:** grids, percentage heights or min/max, and aspect ratios.
  - **Flex plans:** `FlexHeightFrom` already treats such a column as any-height, so the layout pass's `TakePlan` still matches.
  - **Build step:** run the layout-change audit (memory `layout-change-audit`) across all suites, and add a count test (the list measured at N forced heights walks its rows once) that fails under a reverse edit.
  - **Possible extension:** a row that scrolls sideways with a forced width is the mirror case. I did not measure it.
  - **Prototype:** `proto-scroll-forced-height.cs.txt`.

WAITING, RE-RANKED:
- **virtuallist-selection-relayout:** fold it into layout-scroll-forced-height-measure. The "lays out every row" cost is the list's measure cache thrashing (above), not the rows' layout. Of the 869 calls, 800 were cache hits, and the real work was 18 uncached ComputeLayouts and 2.5 leaf (text) misses a click.
  - **The selection drop's own `MarkLayoutDirty`:** dropping it is a further, separate change. It takes ComputeLayout from 72.2 → 26.2 a click at 60 endpoints (70 → 26 at 600, 74 → 30 at 2,000), leaves MeasureSize unchanged, costs 0 B and keeps pixels identical. It is below the bars on its own (see Examined).
- **dropdown-follow-control-on-resize:** Dropdown and the date pickers were untouched in round 10. Correctness, not performance; no bar.
- **vesper-brief-filter-clear-reveal, filter-clear half:** still open by code reading. `ScrollBriefToCursor` (CommanderApp.Views.cs:878) sets `ScrollOffsetX` on a kept scroll view that is still laid out with the narrower filtered content. 00b8fc79 holds an offset only while the view cannot clamp, but here it can, so the offset still clamps to the old content. The missing piece is a horizontal reveal applied after layout, like the vertical one. Not performance; no bar.
- **layout-ordered-children:** not worth it. Meridian's map, the one app case (618 hit visits a move), now measures 40.5–42.5 hit visits a hover move at every size. Apps are at 42.5 or less (Junction changes wheel 33.5–36.2). Only suite rows remain.
- **layout-audit-padding-border:** tooling, no number. Build it with the audit run the candidate above needs.
- **controls-menu-max-height-scroll:** `ContextMenuElement` was untouched in round 10. Not performance.
- **layout-virtuallist-spacer-stale:** round 10 changed VirtualList only for held offsets and `UserScrolled`; the spacer code is unchanged. Invisible and 0 B; do it with the candidate's audit.

EXAMINED, NOT WORTH IT:
- **A selection drop without the list's layout mark** (`DropGathered` with only `MarkPaintDirty`): 46 fewer ComputeLayout calls a click, of which 41 are cache-clean hits (uncached ones 17.9 → 12.9). MeasureSize and KB are unchanged and pixels and geometry are identical. It is too small alone and risks a list whose width comes from its rows.
- **Keeping the list's measure cache across a render-time `ReplaceChildren`:** 0 calls saved. The 16-input thrash refills it anyway.
- **.NET 11 optimized-JIT allocations in layout:** none found (the `DOTNET_TieredCompilation=0` run above).
- **Measure-cache capacity:** the map's 4 widths fit the 4 slots exactly once the forced heights collapse. Deeper nesting could thrash again, but I did not measure it; I am not raising it without a case.

NEEDS THE USER: none.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\ec6d577c-f9ba-4479-acbd-908e67c0ea0a\scratchpad\r11-layout\`.
- **Probe code:**
  - `probe-r11-full.patch`: every edit. The prototypes are behind `PROBE_SCROLLFH`, `PROBE_SELNOLAYOUT` and `PROBE_SELKEEPCACHE`; the tallies behind `PROBE_TALLY`, `PROBE_TALLYALL` and `PROBE_TALLYMEASURE`; debug output behind `PROBE_DBG`.
  - `LayoutProbe.r11.cs` (it goes in `src/HultUI.Core/DevTools/`)
  - `filter_patch.py`: makes the base probe from round 10's patch.
  - `proto-scroll-forced-height.cs.txt`: the candidate's prototype.
- **Drivers:**
  - `mer\`, built into `mer-bin\`: the map probe, now with the verify hashes.
  - `appdrv\`, built into `appdrv-bin\`.
  - `suite-bin\`
- **Scripts:** `run-suite.ps1`, `run-suite-tc0.ps1`, `run-drv.ps1`, `cmp.py`, `cmpdrv.py`.
- **Outputs:**
  - Suite: `probe-head.txt`, `probe-sfh.txt`, `probe-tc0.txt`, `cmp-r10-r11.txt`, `cmp-head-sfh.txt`, `cmp-head-tc0.txt`.
  - App walks: `drv-head-base.txt`, `drv-head-sfh.txt`.
  - Meridian map: `mer-base2.txt`, `mer-sfh.txt`, `mer-proto.txt`, `mer-both.txt`, `mer-tally.txt`, `mer-dbg.err`.
  - Tests: `test-core-sfh.log`, `test-headless-sfh.log`.
