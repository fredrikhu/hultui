# Round 6 survey: 6. Reactivity and reconcile

I measured at ef4288ad, detached. No commits, and the worktree was left clean: the probe was removed with `git apply -R final.patch` and `R6Probe.cs` was deleted. Every walk ran headless, JIT, Release, on P-cores. A step is one key (or one action) and two frames. Bytes come from `GetAllocatedBytesForCurrentThread`, split into exclusive buckets: build per component type, per-part builds in Vesper, Meridian and Aperture, dispatch (handler code outside builds), reconcile, style, layout, a11y and render. Counts are per step. No timings were taken. Double rebuilds inside one dispatched event were logged across five app suites.

Probes are in the scratchpad at `C:\Users\fredr\AppData\Local\Temp\claude\C--Users-fredr-source-repos-fredrikhu-hului-revamped--claude-worktrees-distracted-mendel-5071ba\855f4ddd-feda-4f0a-a595-a83e07af08f8\scratchpad\r6-reactivity\`:
- `apply6.py`: the core counters.
- `apply6b.py`: per-component, per-part and dispatch buckets.
- `apply6c.py`: the prototype switch `R6_PROTO_FIND`.
- `R6Probe.cs`.
- `drv\`: the walk driver. Modes: junction, typing (`R6_CHANGED=n` adds changed files), files, vesper, vfind, vfull, meridian, mhsearch, mfresh, mmethod, aperture (with resize), reel, palette. `R6_A11Y=1` installs the bridge.
- `R6ReviewTypingProbe.cs`: Junction.Tests probe for review composer typing.
- `doubles.ps1` and `doubles-*.log`, `proto-keys.patch`, and the raw outputs `run-*.txt`.

SATISFIED: no

REVIEW:
- **a52de219: correct.** Brief takes the empty path 3.97 times a key and restores every time (`restoreMoved` 9 a key, 0 settle scans), at 36.5 KB a key. Reel's rebuild is 2.1 KB. Only Junction Files search still settles: 0.73 scans and 2.2 comparisons a key.
- **ff6262ff: correct.** No part builds on a key: URL typing 71.8 KB (54 patched, the same with a 2,000-line JSON response), History Down 62.0 KB, History search 70.4 KB. Meridian.Tests logs 14 doubles, all clicks (see meridian-popover-builds).
- **fcf366c6: correct on keys.** Detail 34.1 KB with 0 part builds. Insert builds only the footer part (4.0 KB). Two paths still take the full `MarkDirty`, which now also rebuilds all 11 parts:
  - `Repaint()` (CommanderApp.cs:2060–2066): transfer, pack, archive add and folder size, about every 40 ms.
  - The find bars (`NewFindBar => new(MarkDirty)`, CommanderApp.FindBar.cs:23).
  - Neither is a regression: a full build cost the same before the parts. But neither uses the view-only path (candidates 2 and 3).
- **Vesper doubles.** VesperCommander.Tests logs 115 app doubles. Each now also rebuilds the parts (495 part doubles logged). All are the one-off paths DECISIONS.md declined, plus Space on a result set (`HandleKeyDown` + `ToggleMark`, 3). No new double source.
- **6388f868: correct.** One app build per find step.
- **b1a12ef1: holds.** Review comment typing is 158.6 KB a character with the bridge off (209 at landing). With a reader it is 337.4 KB, with a whole accessibility tree per character (1.00, a11y bucket 63.1 KB). That is a lead for expert 7, below.
- **574f43a7 / 76b6f78e: no objection.**
- **Junction doubles.** Junction.Tests logs 23 doubles, all one-off clicks, drops and dialogs. The suite failed 2 tests only with the double logger in (a stack trace per rebuild, under load); 994/994 pass without it.

CANDIDATES:
- id: junction-changes-composer (re-ranked up; scaling)
  what: A summary keystroke builds the whole Changes view, lists included. Make the composer a part with its own signal, and keep the lists and the diff.
  evidence: A keystroke with 1 changed file costs 76.2 KB (62 patched). With 200 changed files it costs 1,324.7 KB: 1,270 elements made, 1,269 patched, reconcile 75.5 KB. That is O(changed files) per keystroke (`typing`, `R6_CHANGED=200`; JunctionApp.Changes.cs, summary `OnTextChanged` → `MarkViewDirty`).
  expected: Scaling bar. About 15 KB a keystroke at any file count: −80 % at 1 file, −99 % at 200.
  cost: medium, app. No API or pixel change.
- id: find-step-current-match-rows (app ×2, 6/8)
  what: `ViewerFindShape` (CommanderApp.Viewer.cs:599) and `DiffFindShape` (JunctionApp.Diff.cs:817) put the current match's index in the rows' shape, so every Enter or F3 remakes every row on screen. Take the index out of the shape and remake only the rows of the old and new current match. Also hand Vesper's find bars `MarkViewDirty`.
  evidence: Prototype (`R6_PROTO_FIND`), Vesper reader on a 3,000-line file: Enter 345.1 → 105.3 KB (−69 %), rows' render 255.3 → 24.2 KB, unmounts 216 → 18. The find bar's full rebuild adds another 29.2 KB (title bar 17.0, command line 12.2). Junction's diff has the same shape (round 5 measured 440.5 KB an Enter); I did not measure it this round.
  expected: Memory churn. Vesper −78 % (to about 76 KB an Enter); Junction similar.
  cost: small, app. No pixel change. Guideline note for the FindBar section of ELEMENTS.md.
- id: vesper-transfer-progress-rebuilds (kept, with numbers)
  what: Send `Repaint()` through `MarkViewDirty`, with the backgrounded transfer's percent and count added to `CmdKey`. The command line shows them (CommanderApp.Pane.cs:1304); the progress dialog is built by the view.
  evidence: `vfull`: a full `MarkDirty` costs 189.2 KB (12 builds, 169 patched); `MarkViewDirty` costs 14.7 KB. At about 25 ticks a second that is 4.7 MB/s against 0.37 MB/s.
  expected: −92 % a tick.
  cost: small, app. Also: `Repaint` reads `Environment.TickCount64` instead of `Application.Time`.
- id: vesper-marks-in-row-shape (kept)
  what: A mark remakes every visible row because marks are in the rows' shape. Move them into the row item (ELEMENTS.md, "State that only some rows show").
  evidence: Insert costs 190.7 KB a key; rows' render is 164.9 KB of it, with 150 unmounts and 158 mounts a key.
  expected: Two rows a key, about 30 KB (−84 %).
  cost: small–medium, app.
- id: palette-virtual-rows (framework, new; replaces palette-row-keys)
  what: `CommandPalette` builds every match up to `MaxRows` (100) on each keystroke, but only 8–11 rows fit (`MaxHeight` 440, rows 32 or 46 px). Put the rows on VirtualList item rows with `RowHeights`.
  evidence: Junction palette, typing and erasing "bran": 264.6 KB a step, of which build 254.5 KB; 282 elements made and 160 unmounted a step.
  expected: Scaling bar: O(matches) becomes O(visible). −75 % here, about −90 % at 100 matches.
  cost: medium. Rows off screen would leave the a11y tree, as in any VirtualList of 2,000 items or fewer. A reader would notice, so this may need the user's approval.
- id: aperture-resize-rebuild (split out of apps-aperture-components)
  what: `OnWindowResize` calls `MarkDirty` on every step. Build only when something the build reads from the width changes: `VisibleCards`, the hero width, the artwork step (as e6a432fa did for Vesper).
  evidence: A 4 px step that keeps the column count costs 68.3 KB and 63 patches.
  expected: About 0 KB of build per such step.
  cost: small, app.
- id: apps-aperture-components (kept, with numbers)
  what: Keep the rail and the title bar as parts.
  evidence: The rail costs 17.0 KB and the title bar 16.7 KB of every grid key. Right is 118.2 KB a key, Down 179.9 KB.
  expected: Right −29 %, Down −19 %.
  cost: medium, app.
- id: composite-row-kept-cards (app, 8/6, new)
  what: A cursor move remakes whole composite rows. Hand back the unchanged cards, as Brief does (REACTIVITY.md, "Hand back the elements a key does not change").
  evidence:
  - Aperture grid (`GridRowItem(Row, CursorColumn)`): Right render 51.8 KB (one row, 53 unmounts); Down render 113.4 KB (two rows, 116 unmounts).
  - Vesper Thumbnails Down: 106.9 KB a key, render 86.0 KB, 86 unmounts.
  expected: About two cards made a key: Aperture Down about −55 %, Thumbnails about −70 %.
  cost: medium, app.
- id: junction-branches-selection-highlight (app, 8/6, new)
  what: Branch rows differ by focus only in their background and key (`BranchLeafRow`). Let the list draw the selection, as Tags does (8157c282).
  evidence: Branches Down costs 92.7 KB a key; render is 53.9 KB of it, with 55 unmounts a key. Tags is now 34.4 KB.
  expected: About −50 % a key (Tags got −54 %).
  cost: small. Pixels to be checked the same way as Tags.
- id: meridian-popover-builds (widens meridian-method-popover-double-build)
  what: Opening a popover is a full `MarkDirty`, which rebuilds all four parts. A pick builds the app twice: the `BuildPopover` row runs `action(); MarkDirty();`, and `SetMethod` and `SelectEnvironment` also call `MarkDirty`. Open and close the popover view-only, and build once per pick.
  evidence: Opening the method chip and picking costs 610.7 KB, including two request-tree builds at 113 KB each (25 requests). Meridian.Tests doubles: PromoteToCollection + Flash 4, SelectEnvironment 3, SetMethod 2.
  expected: About 285 KB (−53 %) per open and pick.
  cost: small, app.
- id: vesper-own-selection-revisit (kept, owner 1)
  evidence: A Detail key costs 34.1 KB; the two cursor rows' render is 12.7 KB of it (16.5 mounts, 10.5 unmounts).
  expected: A list-drawn highlight removes at most −37 %. The zebra stripes and the partner ring still have to move out of the rows (68817800).
  cost: medium.
- id: vesper-coalesce-markdirty (kept, low)
  evidence: 115 app doubles in the suite, each a 189 KB full build. All one-off actions.

EXAMINED, NOT WORTH IT:
- **palette-row-keys.** The prototype keyed rows by `Group|Title|Detail`. Bytes went up 3 % with the bridge off (265.3 → 273.5 KB a step) and 11 % with UIA (298.8 → 331.7). Accessibility trees per step went 0 → 0.75, because reordering kept rows is a structural change. Unmounts fell 160 → 29, but patches rose 122 → 253. Closed.
- **meridian-history-aot-p90, my side.** Over 40 fresh History keys, keys 1–31 have the same counts: 1 build, 47 patched, 64 made, 16 unmounted, 62.6–68.0 KB. Key 4 costs 82.8 KB with the same counts; key 0 costs 160.6 KB. No stretch builds, patches or mounts more, so reconcile does not explain it. This stays with expert 8's trace.
- **Reconcile pooling (P5a) stands.** Reconcile's share of a key: Vesper Detail 1.9 of 34.2 KB (5.6 %), Junction History 1.4 of 43.6 (3 %), Meridian URL 5.8 of 71.8 (8 %), Aperture 2.8 of 118 (2.4 %), Junction 200-file typing 75.5 of 1,325 (5.7 %).
- **Mounts without unmounts are not a leak.** Meridian URL typing shows 53 mounts and 0 unmounts a key. `AddChild` mounts every element as it is built, so twins that are patched away count as mounted. Only the Video, Terminal and WebView elements override `OnMounted`.
- **Reel.** An app rebuild costs 2.1 KB, or 56.8 KB with the chapters panel open, and happens a few times a film.
- **Lead for expert 8.** Vesper spends 5.3–6.0 KB a key inside event dispatch outside builds, about 17 % of a Detail key. That is the app's handler code.
- **Lead for expert 7.** Review comment typing with a reader builds a whole tree per character (337.4 against 158.6 KB, a11y bucket 63.1 KB). The composer's card row is remade (`InvalidateRow`), and the diff list's rows are real accessibility nodes, so the remake is structural. Lists named through item labels build 0 trees a key (Vesper Detail, Junction summary typing with the bridge on).

NEEDS THE USER:
- **A framework kept part.** Junction (`KeptPart` plus a revision signal), Meridian (a revision signal plus `afterBuild`) and Vesper (`Place()` plus counters) each carry their own `Part`, with different traps; REACTIVITY.md lists the lessons. A public `Part` in Core that follows Vesper's `Place()` rule would make Aperture's adoption, and the next app's, small. It is an API addition: worth it?
- **palette-virtual-rows.** May rows the palette has not scrolled to leave the accessibility tree, as they do in every other VirtualList?
