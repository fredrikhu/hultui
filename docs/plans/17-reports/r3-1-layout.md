# Round 3 survey: 1. Layout

Counts and bytes only, no timings. Probe counters on all 102 suite rows at ac5d415 (JIT), compared
with round 2's baseline at 6ba7e2d on the 94 shared rows. Probe walks in headless Junction (synthetic
400-commit repo, 24 branches, 30 tags, 2,300-file tree) and Vesper (400-file folder); an
inherited-style diff logger over the Vesper (811), Aperture (254), Meridian (417), Reel (43), Fluent
(34) and Headless (731) suites; pixel captures in Junction. Probes reverted; worktree clean. Probe
patch and tests in the session scratchpad `l1r3/`.

SATISFIED: no

REVIEW: none. Layout counters held or fell on all 94 shared rows (no row makes more ComputeLayout,
MeasureSize, leaf-measure or MeasureContent calls, hit-test or render visits). Grid rows: the 1.63 M
loop steps a step are gone (afbd13c). controls/expander toggle: MeasureSize 2,408 → 674,
MeasureContent 456 → 30 per step (7856452). Z-order (91efc25): per-frame scans gone; order redone
only after a child list changes (scroll/jump 20 a step, Vesper key 56, 0 B each). d2ee688/54a48c3/
c135390 add no layout marks; the only style-pass layout marks in the suite are the pre-existing theme
rows. bff6de2/73b1c10: Junction History per key 2.8 rows made, 33 KB template bytes, 17.6 layouts;
hover 25 hit visits. Allocation 53604f3-before → bff6de2-after (JIT and AOT): no row rose ≥5 % except
the known text/wheel JSON +3.4 KB. A 20-step probe showed component/counter 3.8 → 13.6 KB and
richtext/type in a 40x5 table 23 → 61 KB; both vanish at 200–3,000 steps (3.6 and 25.9 KB) — caches
growing inside a short window.

## Candidates

- **layout-virtuallist-row-inherit** (= virtuallist-row-style-inheritance; recommend now). Resolve
  one-argument rows under their list with ResolveUnder, as item rows already do, instead of under the
  theme defaults (VirtualListElement.cs:533 vs :531). Today a realized row resolves at the theme's
  16 px; the next style pass resolves it under the list (12–13 px in the apps) and StyleResolver.cs:71
  marks every container and icon of every realized row for layout (100 % of these marks are
  BoxElement/ImageElement "16 → 13" in Junction, "16 → 12.5" in Vesper). Per key, before → after:

  | Walk | Layouts in layout pass | ComputeLayout | MeasureContent | Leaf measures |
  |---|---|---|---|---|
  | Junction Branches | 255 → 32 | 975 → 432 | 136 → 2 | 830 → 130 |
  | Junction Tags | 167 → 28 | 706 → 311 | 67.5 → 2 | — |
  | Junction Diff | 126 → 42 | — | — | — |
  | Vesper cursor | 137 → 77 | — | 421 → 302 | — |

  Style-pass layout marks → 0 in every walk (Branches 241, Tags 153, Diff 106, Vesper 120 a key).
  Resolve counts and Vesper allocation (542 KB a key) unchanged. Scaling: the second layout of every
  realized row per rebuild goes; time likely ≥5 % on Branches/Tags keys (AOT timing in build). Also a
  correctness fix: rows stop flipping between theme-default and list inheritance. Small (one line +
  test). Pixels: none found — the diff logger saw only BoxElement/ImageElement FontSize differ, neither
  draws from inherited style; no text or control element differed. Lists covered: Vesper panes, diff,
  viewer, thumbnails; Aperture grid; Meridian history; DevTools Inspector; Junction Branches, Tags,
  Diff. Junction pixel captures prototype vs not: 0 px in Branches, Tags, Diff, Files (control 0 px).
  Not exercised: Junction Blame, Vesper Rename and Find, whose row texts set size and colour explicitly
  (JunctionApp.Blame.cs:154-157, CommanderApp.Rename.cs:295-315, CommanderApp.Find.cs:453-460). Only a
  row relying on an ancestor's text style changes (e.g. a TableView under `.FontSize()`); no app has
  one.
- **layout-style-mark-once** (new). Stop re-walking subtrees already marked for style. Every PatchFrom
  calls MarkStyleDirty (Element.cs:1329), which walks the whole subtree (Element.cs:1034); the
  reconciler patches top-down, so each ancestor level walks everything below again: O(n × depth) per
  rebuild. Prototype: a "subtree marked" DirtyFlags bit, cleared with Style at Window.cs:939; the three
  add paths mark a new child when the parent has the bit. Mark visits before → after:

  | Walk | Mark visits |
  |---|---|
  | component/edit 1,000 rows | 44,004 → 12,003 |
  | controls/tabs switch | 7,508 → 1,807 |
  | command palette | 2,136 → 615 |
  | a11y/create | 13,000 → 6,000 |
  | Junction History key | 2,586 → 474 |
  | Junction Branches key | 6,479 → 1,728 |
  | Junction Tags key | 8,006 → 1,392 |
  | Junction Files, 2,300-file search, per key | 383,482 → 37,432 |

  Resolves and every other counter identical on all 102 rows; Core 4,917 and Headless 731 tests pass.
  Scaling O(n·depth) → O(n): −73…−90 % mark visits; time unproven, largest on deep or large trees.
  Small; no API or pixel change.
- **apps-junction-files-virtual** (owner 8, with 1). Junction's Files tree on a VirtualList with item
  rows; today every row is a ScrollView child (JunctionApp.Files.cs:233). A 2,300-file search builds
  14,128 elements. Per Down key: 28,036 KB allocated, 14,128 resolves (14,115 change nothing), 383,482
  style marks, 35,249 MeasureSize. Per pointer move 3,501 hit visits; per frame 3,474 render-cull
  visits + a 3,506-child extent scan (virtual Junction lists: 20–32 hit visits a move). Churn ~28 MB →
  History-like (~200 KB) per key; every per-event and per-frame O(n) → O(visible). Medium, app-only;
  keep keyboard, search and scroll behaviour.
- **adopt-item-rows** (re-ranked; approved). Move more lists onto `.RenderWith((item, selected) => …)`.
  Template bytes per key:
  1. Vesper panes: 60 rows re-made a key (the inactive pane re-templates its 30 too): 339 of 542 KB
     (63 %). FileEntry is a record, so equality works; shape = marks, partner path, active pane,
     columns; SelectedIndex must carry the cursor (CommanderApp.Pane.cs:741-744). Best value.
  2. Junction Branches 19.3 rows a key, 552 of 768 KB (72 %); Tags 17 rows, 383 of 745 KB (51 %). Both
     need stable or value-equal TreeNode items first (nodes rebuilt every build,
     JunctionApp.Branches.cs:274).
  3. Aperture grid (ApertureApp.Library.cs:73-81): not measured; items are int row indices, so the
     shape must include the item list and cursor row.
  4. Junction Commit diff: 22 rows, 200 of 422 KB a key, but its items change each key in the walk —
     no gain.
  5. TableView: no app uses it; sort or column resize remakes every row anyway.

  Churn −45…−65 % per key on items 1 and 2. Small–medium per list, one at a time.
- **reconcile-restyle-on-change** (shared with 6; rank after layout-style-mark-once). Mark style on
  patch only when inherited style or theme-fed values changed. Unchanged / total resolves per step:
  component/edit 6,003 / 6,003; Junction History key 315 / 351; Junction Files search key
  14,115 / 14,128; item-row walk 103 / 118 (kept rows re-resolved). Round 2 capture's style column
  0.175–0.186 ms on component/* rows: time ≥5 % there plausible; apps ~1 % (style share). Medium.
- **layout-applytheme-layout-on-change** (low). Mark layout in ApplyTheme only when a value
  MeasureContent reads changed: NumericUpDownElement.cs:305, ProgressBarElement.cs:102,
  SplitterHandleElement.cs:89, ExpanderElement.cs:350, CalendarElement.cs:380,
  TimePickerElement.cs:492, ToastElement.cs:165. Prototype on theme/light-dark and named scheme:
  8.91 → 0.19 KB/op, 78 → 0 layouts, 100 → 0 MeasureContent, 50 → 0 style-pass marks. Junction walks
  unchanged (195.2 KB, 17.6 layouts per History key; patched controls are layout-marked by PatchFrom
  anyway). Churn −98 % on the two theme rows only. Small.
- **layout-ordered-children** (low; narrowed). Binary-search hit testing and culling over ordered flex
  children (HitTester.cs:45,76-77; ElementRenderer.cs:49,106). Hit visits per move: scroll/wheel
  5,000-row list 4,954 (+4,974 culled per frame); input/hover 1,000 rows 993; input/click in 500 rows
  1,493; input/drag to reorder 200 rows 2,406; a11y/wheel 1,000 rows UIA 954; Junction Files search
  3,501. App lists on a VirtualList cost 20–32. Scaling O(n) → O(log n + visible); suite-only once
  Files is virtual. Medium: HitTest is public virtual, runtime transforms need a parent notification.

## Examined, not worth it

- Base ScrollView's render-time extent rescan (ElementRenderer.cs:92 → ScrollViewElement.cs:178-209,
  already done in layout at LayoutEngine.cs:167): skipping it passes Core, Headless, Vesper, Meridian,
  Aperture, Fluent, Skia tests (one Meridian HTTP-timeout flake, passed on rerun). Suite scroll views
  have 1–20 direct children (1 a step on ~70 rows); only Junction Files pays (3,506 a frame), and that
  goes with apps-junction-files-virtual.
- Junction Files' TextArea: 96 leaf misses a key, but TextAreaElement has no MeasureContent
  (Size.Zero) — trivial.
- Style-pass allocation: component/mount 300 shows 43 KB (2.3 % of the row), gallery mount 7.2 KB —
  first-time style objects in ApplyTheme on mount; steady rebuilds 0–200 B.
- Junction History hover, wheel and render: 25 hit visits, 6 culled. Nothing left.

## Needs the user

1. **layout-virtuallist-row-inherit:** rows of a one-argument VirtualList inherit text style from their
   list, as item rows and every other child already do. No app screen changes pixels; only a row that
   relies on an ancestor's text style would change. Approve? Recommended: yes.
2. layout-scroll-strip-narrowing: still outside plan 17 as decided; no new evidence.
