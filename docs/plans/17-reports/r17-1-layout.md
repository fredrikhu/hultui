SATISFIED: no

All numbers are counts and bytes at 0df5e3ea (.NET 11.0.100), from headless Release tests and one NativeAOT real-window launch. I reversed the probe with `git apply -R`, deleted the new files, left the worktree clean and shut down the build servers.

REVIEW:
- **b8a3dcf5: the PerfSuite row `virtual/columns cursor walk 100k item rows` measures nothing.**
  - `ColumnsWalk` never focuses its list, so `PressKey(Right)` reaches no one. The row prints "25/25 steps rendered nothing", 0 rows made, 0 KB. The other walk focuses its list (Scenarios.Virtual.cs:285).
  - Since this commit, every suite run that includes the row exits with code 1 (Program.cs:120). Check whether `step-ab.ps1` reads that exit code.
  - With the list focused (a 2-line probe), a step makes 26.8 cells and allocates 262 KB. Most of that is the column scrolled in once the cursor passes the right edge.
  - Fix: `list.Focus()` after mount.
- **38ec9773, b8a3dcf5, 266d6aee: no O(n) work per step.**
  - 1M rows cost the same bytes per op as 100k: wheel 28 = 28 KB, jump 180 vs 179, replace 147 vs 146. TableView 1M wheel 41 = 10k wheel 41 KB.
  - Cursor walk over 100k state rows: 2.0 rows made, 13 KB a key.
  - These are O(realized rows) or O(1): `DropRowsWhoseStateChanged`, the column visible range, `IndexAfter`/`ItemRect`/`ClampX`/`RowsFor`, and a column `ScrollIndexIntoView`. `EnsureOffsets` is O(n) once, and only with `ItemHeightFor`.
  - Columns wheel: 753 KB per 3 notches, which is 3 columns × ~31 cells, about 8 KB a cell. Rows cost about 7 KB a row, so this is the same per-row cost.
  - No app uses `CurrentIndex`, `RenderWithState`, `ListFlow`, `RowWidth` or `IndexAfter` yet (grep of apps/: 0), so there is no real-app case today.
- **3d728568, 13a1b363, 49b0da84 hold.**
  - Every Junction width step in my probes: 0 builds, 1 heights pass.
  - Vesper's wrapped reader lays out each visible line at one width only: 229 asks over 195 texts a step, none at a width that is never drawn.

CANDIDATES:
- id: layout-virtuallist-measure-through-rows
  what: When an ancestor measures a VirtualList, it measures the list's realized rows as a flex column. Each visible row is laid out at up to three widths it is never drawn at, though the list lays out its own rows at its own width. Measure the list as a leaf instead.
  evidence:
    - **Cause:** in Junction's Commit view, each visible code label is laid out at 642 (drawn), then 1152, 908, 886, then 642 again, every width step. The extra widths come from `RemeasureFlexedCrossSizes` (LayoutEngine.cs:760) under 8 nested `MeasureFlexItems` levels (:573) from the root. The chain is all FlexGrow(1) with basis auto (root col → Row sidebar|main → col → col → Row filelist|splitter|diff → col → VirtualList).
    - **Prototype:** `MeasureSize` takes the leaf path for a VirtualList (LayoutEngine.cs:619, plus an internal virtual on ScrollViewElement). I switched it at run time and alternated it in one process.
    - **Headless, 1440×900, 6 px steps, 200 lines of real prose (mean 473 characters):**
      - Commit unwrapped: 66.4–82.9 → 28.2–31.4 KB a step; text-layout asks 429 → 244.
      - Changes unwrapped: 70.1–74.8 → 29.5 KB.
      - Commit wrapped: 94.0/97.5 → 62.4/65.7 KB (−33 %); line-break passes 122 → 86.
      - Changes wrapped: 165.5/167.0 → 136.7/136.3 KB (−17 %); breaks 241 → 207.
      - History: 2.3 = 2.3 KB.
    - **Expert 5's commit a359e426, split view, 263 rows:**
      - Re-breaks per 40 steps 3,905–3,919 → 1,396.
      - Re-breaks from the rows' own layout 2,534–2,538 → 28; the rest are the heights pass's.
      - 71.5–80.2 → 24.0 KB a step (−66…−70 %).
      - e432c224 split: element re-breaks 972/835 → 40/36, 27.1/24.6 → 12.8/12.7 KB.
    - **Same output:**
      - Layout boxes and pixels match in 30 of 30 cases: 5 views × 3 widths, wrap on and off, every measure cache emptied before each side, base stable.
      - Four suites pass with the switch on: HultUI.Core.Tests 5,209, HultUI.Headless.Tests 1,792, VesperCommander.Tests 954, Junction.Tests 1,168.
  real app:
    - **Where:** Junction, every window-resize step and every file-list splitter step with a diff shown in Commit or Changes. Unwrapped is the default. A drag is dozens of steps a second.
    - **Real window (AOT, 150 %):** Commit unwrapped 70.5/71.9 → 37.5/34.3 KB a step; Changes unwrapped 74.8/76.3 → 36.3/36.1 KB.
    - **Expert 5's lead:** yes, the measures at those ancestor widths can be avoided. This removes 3 of the 4 layouts a frame per wrapped label (element re-breaks 2,534 → 28 per 40 steps). Their 4-range memo is then not needed for this case. The visible rows' wrap ranges also stop being evicted: range hits per step 115 → 128.
  expected: Churn bar: −48…−62 % UI bytes per resize step unwrapped, −17…−33 % wrapped, −66…−70 % on a359e426 split. Scaling: the work removed is proportional to realized rows × nesting depth.
  cost: small. A few lines in `LayoutEngine.MeasureSize` and a VirtualList override; no public API, no pixel change found.
    - **Behaviour:** a VirtualList inside a container sized from its content (no width, not stretched) loses the width its realized rows gave it. That width changes as rows scroll in, so it was never a stable width.
    - **Audit:** 3 framework sites (CommandPalette has a fixed width; TableView; InspectorView) and 16 app sites.
    - **Build phase:** run the full suites, the goldens, and Meridian and Aperture.

- id: text-heights-pass-height-only (owner: expert 5; the round 16 step 8 lead)
  what: Junction's heights pass asks a full wrapped layout of every row to read one height. A text-side height-only ask that allocates nothing, with a "same line count" width range, would remove most of the pass.
  evidence:
    - **Commit, prose, per 6 px step:** 199 rows asked (1 fast-path hit), 8.2 heights changed (4.1 %), ~120 rows re-broken (the kept "same breaks" range answers only ~40 %). That is 43–44 KB, 47 % of the step's bytes (66 % once the candidate above lands), and 36–41 % of the step's UI cycles.
    - **Changes, per step:** 400 rows asked, 23.2 changed (5.8 %), ~240 re-broken, 112 KB (68 %, then 81 %).
    - **a359e426 split:** the heights pass makes 1,368 of the 1,396 re-breaks left.
  real app: The same Junction resize and splitter drags with wrap on.
  expected: Churn: 41–44 KB (Commit) and 105–112 KB (Changes) a step go if the ask allocates nothing; 94–96 % of asks could be skipped with a line-count range.
  cost: medium, text side. The call side cannot cut anything on its own: Junction already asks once per row. Skipping rows from the app would need the range exposed through `TextMeasure` (public API: needs the user).

REAL WINDOW:
- **Setup:**
  - 1 launch, 1 hold: waited 759.4 s for the lock (another expert held it), held 90.5 s.
  - 0 foreground takes, 0 activations refused, 8 windows given `WS_EX_NOACTIVATE`, 0 nvlddmkm events. Idle gate passed (CPU 1 %). Timer resolution 1 ms (Steam).
  - Junction host on NativeAOT, on the 4K primary at 150 % (window DPI 144), 120 Hz; client 1440×900 logical.
  - Resize steps of ~9.5 px (≈6.3 logical) from a Win32 timer on the UI thread, leaf switch alternated twice.
- **KB a step (base → leaf):**
  - Commit unwrapped: 70.5/71.9 → 37.5/34.3; asks 497 → 317.
  - Commit wrapped: 101.8/102.3 → 70.9/70.9; breaks 123–125 → 87.1.
  - Changes wrapped: 163.6/163.1 → 132.3/132.6; breaks 232 → 196.5.
  - Changes unwrapped: 74.8/76.3 → 36.3/36.1; asks 490 → 298.
- **New against headless:** nothing; the same mechanism and shares at 150 %.

WAITING, RE-RANKED:
- **dropdown-follow-control-on-resize:** no commit since round 16 touches it. Correctness, low, not perf.
- **vesper-brief-filter-clear-reveal:** CommanderApp.Views.cs is untouched since d85c6ff6. Not perf. b8a3dcf5's `ListFlow.Columns` with `ScrollIndexIntoView` would give a ported Brief a reveal held until layout.
- **controls-menu-max-height-scroll:** ContextMenuElement is untouched. Not perf.
- **layout-virtuallist-spacer-stale:** unchanged (b8a3dcf5 only gives columns a zero-size spacer). Still invisible, since rows are placed at exact Y. The candidate above removes the spacer's last reader outside the list; close this item if it lands.

EXAMINED, NOT WORTH IT:
- **Column hit-testing:** a reverse walk over the realized cells (~186), no z-order sort (absolute cells), the same order of cost as rows; no app uses columns.
- **GetItemLabel's label array sized to the item count** (VirtualListElement.cs:489): UI thread, eager lists only (≤200 UIA, ≤2,000 AT-SPI), so bounded. That is expert 7's area, as is FindItem's linear default.
- **`SelectedIndices` `.Order().ToArray()`:** multi-select lists only; no hot app path.
- **Vesper's wrapped reader at hidden widths:** 0 asks.

NEEDS THE USER: none. layout-virtuallist-measure-through-rows changes behaviour only for a VirtualList in a container sized from its content, and no such list was found; the four suites pass with it on.

FILES (in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\35903ed7-ff10-416d-84e9-b6c09675ab6b\scratchpad\r17-layout\`):
- **Probe:** `probe-r17.patch` (tracked edits) and `probe-new\` (ProbeR17.cs, ProbeR17Layout.cs, ProbeR17HeightsTests.cs, ProbeR17WrapAsksTests.cs).
- **Headless results:** heights-archive*.txt, eq-junction*.txt, other-junction.txt, repo2-*.txt, vesper-asks.txt, leaf-suites.txt, perf-virtual.txt, perf-columns.txt.
- **Real window:** `rw\` (pub.ps1, batch17.ps1, start\src\Script.cs with leaf/psum/sleep steps, rest\common17.ps1). Results in rw\rest\runs\r17L1-*; hold log rw\locks.log; idle-gate.log.
- **Fixtures:** fx\jrepo (prose), fx\j-574f43a7, fx\j-a359e426.
