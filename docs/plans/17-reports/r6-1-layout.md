# Round 6 survey: 1. Layout

I measured at ef4288ad, with the worktree detached there and clean afterwards. I recorded counts and bytes only, from JIT Release builds, with no timings. I used two probes:
- **Suite probe:** round 5's counters, with one anchor updated, plus three new counters: exclusive bytes per layout frame, MeasureContent bytes, and RowHeights height calls. It ran all 151 rows (warm 40, steps 20) and was compared row by row with round 5's probe on the 140 rows both have.
- **Headless app walks:** Junction, Vesper and Aperture, Vesper's viewer, and Junction resize walks. New this round: Fluent sample rebuilds, and Junction's Changes view with 60 and 300 modified files.

Prototypes were gated by environment variables: `PROBE_VSEL`, `PROBE_JSEL` and `PROBE_PALROWS`. All probe edits were removed by a reverse `git apply -R`, and the tree is clean.

Probe code and outputs are in the scratchpad `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\855f4ddd-feda-4f0a-a595-a83e07af08f8\scratchpad\r6-layout\`:
- `apply_probe.py`, `apply_probe3.py`, `LayoutProbe.final.cs`, `probe-final.patch` (every probe edit, the app prototypes too)
- `appdrv\` (the driver), `cmp.py`
- outputs `probe-head.txt`, `cmp-r5-r6.txt`, `probe-extra.txt`, `drv-base.txt`, `drv-changes.txt`, `drv-fluent-*.txt`

**Probe trap.** Round 5's miss tally (`apply_probe3.py`) builds a key string on every measure miss. With it on, resize walks read 48–101 KB a step instead of 2.2–7.2 KB. It is now gated to the measure tally, and every number below was taken with tallies off.

SATISFIED: no

## Review

None. Round 5's build (643eb07b..f31194ab) caused no regression in my area.

**Suite rows.** On the 140 shared rows, no counted work rose:
- layouts, ComputeLayout, MeasureSize and leaf measures
- text MeasureContent and style resolves
- hit visits and render visits, z-order scans, rows made

Bytes rose in three places, none of them a regression:
- **Render-pass bytes on text rows (91d48601).** Glyph arrays are now made on first draw. The layout pass fell more on every such row, for example text/wrap 1,372 → 394 KB.
- **scroll/jump 5.7 → 11.6 KB.** 91d48601's own row records this.
- **overlay/popover open: 152 B moved into the a11y bucket (02e17103).** PrepareOverlays now runs before the a11y phase. The row keeps the same 54 resolves and 27 layouts, and its total went 31.96 → 31.52 KB.

**02e17103 does not style or lay out an overlay twice.** The counts on every overlay row are unchanged. An anchored overlay's Place delegate runs twice a frame and allocates nothing.

**App walks, KB per key, round 5 → now:**

| Walk | Round 5 → now |
|---|---|
| Junction History | 80.7 → 44.4 |
| Junction Commit | 79.1 → 58.5 |
| Junction Branches | 104.7 → 93.2 |
| Junction Tags | 86.2 → 34.4 |
| Junction Files search | 66.0 → 56.4 |
| Vesper same names / other names | 231.1 / 225.6 → 41.8 / 36.6 |
| Aperture Right / Down | 130.7 / 195.3 → 117.6 / 178.9 |
| Vesper viewer find typing | 1,702 → 1,490 |

- **Resize steps:** unchanged at 2.2 KB (History, Branches, Tags) and 7.2 KB (Commit).
- **Vesper resize:** 2.2 → 19.7 KB. This is 1ed9d938, which was approved: its trail comparison, plus a part rebuild of 12 elements per step. Waiting vesper-coalesce-markdirty covers it.
- **Hover and wheel:** 0.1 KB and about 25 hit visits.

## Candidates

### virtuallist-sticky-headers (re-ranked, new evidence)
Owner 1 for the framework, 8 for adopting it in Junction.

**What:** sticky rows in a VirtualList. The list keeps the last sticky row at or above the first visible row realized, and holds it at the top until the next sticky row pushes it off. Junction's Changes list could then become a VirtualList of item rows.

**Evidence:** the Changes view, headless, with N modified files in 6 folders:

| | 60 files | 300 files |
|---|---|---|
| Elements | 905 | 3,065 |
| Down, KB/key | 910 | 3,020 |
| Resolves / patches per key | 894 / 723 | 3,065 / 2,883 |
| Summary typing, KB/char | 644 | 2,713 (2,813 patches) |

- That is about 8.8 KB per changed file on every key and every character.
- Every row is built and patched on each one: the view is a ScrollView of section columns (JunctionApp.Changes.cs:526–605), and `Rerender()` rebuilds the whole view.
- DECISIONS.md "Junction's Changes list stays a scroll view, for now" named this trigger: "when a working tree with hundreds of changed files shows up in a profile".
- For comparison, the Commit list after virtualizing costs 142 KB per key at 382 rows.

**Expected:** the scaling bar. Per key and per character, the cost goes from O(changed files) to O(visible rows). At 300 files that is −90 % or more. What is left is the diff pane's rows for a newly selected file (about 160 KB of render).

**Cost:** medium for the framework, medium for the app.
- **Framework:** realize the held row outside the window; its containing range runs until the next sticky row; hit-testing and a11y follow the paint, as `Sticky()` already does.
- **App:** tagged rows (header, folder, file, hidden), RowHeights for the gaps and the two-line hidden rows, item rows or SelectionHighlight.
- Pixels stay identical if the push-off is kept.
- A public API addition (Needs the user).

junction-changes-composer (6/8) alone takes the lists out of typing, but not out of Down.

### layout-palette-virtual-rows (new; folds in palette-row-keys from 6)
Owner 1.

**What:** CommandPalette's result list becomes a VirtualList of item rows keyed by command. Today it builds up to MaxRows = 100 rows on every keystroke (CommandPalette.cs:349–357). The new list would take RowHeights from the palette's `RowHeight(command)` and an explicit height of min(sum of the rows' heights, the card).

**Evidence:** `PROBE_PALROWS` cut the list to 12 rows, about what is on screen.

| Row | 100 rows → 12 | Other counters |
|---|---|---|
| `overlay/command palette filter 2,000` | 335.3 → 52.2 KB/key (−84 %) | resolves 310 → 46; laid out 309 → 45; text measures 103 → 15 |
| `a11y/command palette typing over 2,000 commands, UIA` | 888.8 → 134.6 KB/key (−85 %) | a11y bytes 214 → 31 KB |

Junction's palette lists every repository, every local branch and the actions, so it reaches the cap at about 90 local branches. Meridian's lists its requests.

**Expected:** the churn bar on two suite rows (−84…−85 %). Laid out −85 %; time likely.

**Cost:** medium.
- No pixel change if the rows keep their heights and the list keeps its padding and the "n more" line. Rows keep `selected`, so the selected row's no-hover rule stays.
- **a11y:** off-screen rows leave the tree, unless AccessibleItemLabel serves them as list items. That would change their role from Button to list item. To be checked with 7.

### vesper-own-selection-revisit (re-ranked: now clears the bar)
Owner 8 for the app, with 1.

**Evidence:** a counting prototype, `PROBE_VSEL`. Both panes' lists get `.SelectionHighlight(fill Accent500 at 0.13, ring Accent600 1 px)`, and rows are made unselected. Detail view, 400 files a pane:

| Walk | KB/key | Other counters |
|---|---|---|
| Same names | 42.4 → 30.6 (−28 %) | rows made 3.5 → 1.5; ComputeLayout 92.8 → 31.9; MeasureSize 407 → 141 |
| Other names | 36.6 → 24.9 (−32 %) | rows made 2.6 → 0.6; ComputeLayout 67.9 → 2.8; MeasureSize 285 → 5 |

68817800's three objections no longer hold:
- **The partner ring never meets the cursor.** The partner path is null on the active pane, and the inactive pane's SelectedIndex is −1.
- **Zebra is off by default** (`Settings.Zebra`). With zebra on, the row-drawn cursor stays.
- **The context-menu key's cursor Ref** (CommanderApp.Views.cs:612) can be worked out from the list's row geometry.

**Expected:** the churn bar (−28…−32 %). Pixels are identical with zebra off.

**Cost:** small, app only, no API change.

### layout-ordered-children (low, unchanged)
- scroll/wheel over 5,000 rows: 4,954 hit visits and 4,974 culls a frame.
- hover across 1,000 rows: 993 hit visits.

The one app list that is not virtual is Junction's Changes (290 culls a frame at 300 files), which the first candidate removes.

## Examined, not worth it

- **layout-patch-keeps-layout-dropdown-expander** (closed):
  - On a Fluent rebuild, Gallery lays out 16 elements, Personalization 9, and Home, RichText and About 0.
  - The marks come from Dropdown and Expander; Dropdown's MeasureContent allocates 96 B of a 224 KB rebuild.
  - The only app use is Junction's Create MR target dropdown. No app uses Expander.
  - Do it whenever either control is touched. Making the layout audit compare LayoutResult padding and border stays a tooling item.
- **RowHeights height function per rebuild** (93226dda's note):
  - 20,000 calls per rebuild on both RowHeights suite rows, allocating 0 B.
  - Every app's height function is an array lookup.
  - Skipping the calls needs an app-supplied version, which is an API change, because Vesper's delegate is new each build.
  - Estimated tens of µs beside about 1.5 MB of work a key: under the bar.
- **Flex-shrink heap arrays above 64 items** (LayoutEngine.cs ~1287): 0 calls across all 151 suite rows.
- **Hit-test enter/leave allocation** (a known lead in the brief): stale. Hover across 1,000 rows costs 0.24 KB a step including the frame; Junction hover costs 0.1 KB.
- **A label measure's 10.9 KB** (component/counter, a11y/edit 1 label): the layout pass's bytes there are all MeasureContent. This is the step-count artefact r5-7 named, against the suite's 0.9–2.9 KB. It is text's area, not layout's.

## Leads for other experts

- **8/6, Junction:** the Changes scaling numbers above. junction-changes-composer at 300 files is 2,713 KB per character, not 95.8.
- **8, Vesper, two O(n)-per-row patterns:**
  - Find results use `RenderWith(h => FindResultRow(h, _findResults.IndexOf(h) == _findSel))` (CommanderApp.Find.cs:403): a one-argument RenderWith with an IndexOf per row.
  - With zebra on, `IndexOfPath(Rows(side), …)` runs once per row made (CommanderApp.Pane.cs:940).
- **8, Aperture:** a resize still costs 80.7 KB and 63 patches a step (apps-aperture-components).

## Needs the user

1. **Round 5 question 5** (Junction History selection pixels): the evidence is now stronger. A counting prototype (`PROBE_JSEL`: the list draws the caret in Neutral900) took History Down 44.4 → 21.4 KB per key (−52 %; round 5 measured −30 %). Rows made 2.5 → 0.5, ComputeLayout 67.7 → 6.3, MeasureSize 296 → 3. It still needs the affordance moved out of the row's flow and the hover tint drawn by the list.
2. **virtuallist-sticky-headers:** may VirtualList get a public sticky-rows option, for example `.StickyRows(Func<T, int, bool>)`, held and pushed off the way `Sticky()` is today, so Junction's Changes list can be virtual? [Yes, opt in, like SelectionHighlight.]
3. **layout-palette-virtual-rows:** off-screen palette rows either leave the reader's tree, or are served as list items rather than buttons. Which should it be? [Served as list items, to be confirmed with 7.]
