# Round 4 survey: 1. Layout

At ba817e8; counts and bytes only (JIT). Full suite probe of 122 rows; headless walks of Junction
(400 commits, 24 branches, 30 tags; 2,300-file search), Vesper (400 files a pane, same and other
names) and Aperture (600 films); prototypes gated by environment variables. Probes reverted;
worktree clean. Probe patch, driver and outputs in the session scratchpad `r4layout/`
(`probe-final.patch`, `appdrv/`, `drv-*.txt`, `suite-*.txt`, `probe-head*.txt`).

SATISFIED: no

REVIEW: none. The 102 suite rows shared with round 3's baseline (5d9be15 = 0951288's code, warm 40 /
steps 20) make no more ComputeLayout, layouts, MeasureSize, leaf measures, MeasureContent, resolves,
hit or render visits, z-order scans or rows made. Fell: theme rows layouts 78 → 0 (51f3126); anim/signal
tween 180 → 160 layouts per step; scroll/jump 11.5 → 5.7 KB. App walks confirm: style-pass layout marks
0 in every walk (1e68166, dc4800d); Junction History hover and wheel 25 hit visits and 0.1 KB (5a98a32);
Files rows made 2.3 a key; persistent hosts, scale-change walks and pre-frame callbacks allocate nothing
per move or frame, pre-frame callbacks 1 per frame (56046e4, 7016de4, 1bd5e4c); no app walk allocates in
the layout pass. text/styled bidi wrap read 59.9 → 842.8 KB in the 20-step probe but the suite captures
hold it flat (745.1 → 741.4 KB) — not real.

## Candidates

- **layout-vesper-partner-row** (owner 1 for the API, 8 for Vesper). Add
  `VirtualListElement.InvalidateRow(int index)` (drops one realized row and its kept label); Vesper then
  invalidates only the old and new F3-partner rows instead of putting the partner path in the pane's
  shape and calling InvalidateRows on the whole inactive pane (CommanderApp.Pane.cs:735-744, :816). 400
  files a pane, same-name walk, per Down key: 403.9 → 232.4 KB (−42 %); rows made 31.6 → 3.5; template
  bytes 197 → 21 KB; leaf measures 1,633 → 215; text MeasureContent 159 → 18; layouts 47.6 → 20; grid
  layouts 62.6 → 6.9. Other-name walk unchanged (225 → 221 KB). Churn −42 %; time likely ≥5 % (0971573
  got −9…−16 % min for the same kind of work). Small; one added public method. Alternative app-only
  route: a lazy-view value item (PaneRowItem) like Junction's BranchRowItem (medium app change, no API).
- **layout-themed-patch-keeps-layout** (owner 1; the layout half of reconcile-restyle-on-change, with 6).
  Themed controls and item-row VirtualLists keep their layout across a patch that changes nothing:
  PatchFrom compares against the twin with the theme's values applied, and these types opt into
  PatchMarksOwnLayout. ButtonElement.ApplyTheme writes Padding, BorderWidth, Width, Height, MinWidth,
  MinHeight (ButtonElement.cs:176-192) that the incoming twin lacks, so Element.cs:1451 marks layout on
  every rebuild for Button, TextInput, Kbd, MenuTitle/MenuBar and TableView's HeaderCell/Grip/SortMark;
  SplitterHandle and VirtualList are not opted in either. The marks re-lay out the ancestor chain; the
  Commit view's diff list is then measured at ≥5 distinct constraints a pass (more than MeasureCache's 4
  slots), each measuring all 23 realized rows: 1,056 MeasureSize a key. Prototype per key:

  | Walk | Layouts | Other counters |
  |---|---|---|
  | Junction Commit | 20.7 → 1.1 | ComputeLayout 61.7 → 1.5; MeasureSize 1,281.5 → 84.6; leaf measures 96.8 → 7.1 |
  | Junction History | 17 → 9 | |
  | Junction Branches | 16.2 → 8.2 | |
  | Junction Tags | 15 → 8 | |
  | Junction Files search | 26.6 → 14.5 | |
  | Vesper, other names | 19.1 → 8.1 | MeasureSize 501 → 285 |
  | Aperture grid | unchanged | |

  Suite: anim/signal tween 160 → 0 layouts a step; typing beside a diff with rows kept MeasureSize 159 →
  5; TableView sort 45 → 36 layouts. A VirtualList opt-in alone moves only Vesper other-names (MeasureSize
  501 → 297). Time ≥5 % plausible on the Junction Commit walk only (est. 45–85 µs of layout per key;
  round 3 had layout at 20–26 % of the Commit walk's frame); elsewhere ≤1 %. Measure on AOT. Medium: each
  type needs an audit that ApplyTheme on a detached twin has no side effects and that its own PatchFrom
  marks layout for what it copies (VirtualList's height-function path relies on the base mark). No API
  change; no pixel change (the flip-flop writes identical values).
- **reconcile-restyle-on-change** (owner 6, with 1; re-measured). (a) A patch marks only the patched
  element for style; the resolver goes down only where a resolved style changed, and a moved child is
  checked under its new parent. (b) A patched element without a theme override whose resolved style
  would not change is not resolved again. Every patch marks style down its subtree (Element.cs:1454), so
  each rebuild re-resolves kept VirtualList rows, persistent and cached subtrees and every patched
  element; Junction History 314 of 314 style-pass resolves unchanged a key (111 patches vs 347 resolves).
  Prototype (a), resolves per key:

  | Walk | Before → after |
  |---|---|
  | Junction History | 347 → 145 |
  | Junction Commit | 335 → 143 |
  | Junction Branches | 699 → 176 |
  | Junction Tags | 537 → 162 |
  | Junction Files | 266 → 133 |
  | Vesper same / other names | 612 / 467 → 310 / 165 |
  | Aperture grid Right / Down | 398 / 461 → 117 / 180 |
  | Suite: item-row walk | 118 → 22 |
  | Suite: rows-kept typing | 304 → 13 |

  Mark walks −75…−81 %. Prototype (a)+(b): Junction History → 47, Commit → 21, Branches → 69, Tags → 59;
  suite component/edit 6,003 → 0, splitter drag 4,860 → 40 a step, slider drag 2,470 → 50, scrubber hover
  230 → 60. Risk seen: prototype (b) drew a Text never resolved in Junction Files (NullReferenceException
  in SkiaFontCollection.Match). Time ≥5 % on the three component/* rows (style 0.171 of a 1.274 ms min,
  13 %); apps ≤1 % (style ≤1.8 % in every walk). Medium–large: (b) needs the 32-override audit and a rule
  that a never-resolved element always resolves; SelectionColor approved; (a) is the safer first half.
- **layout-ordered-children** (low, unchanged). Binary-search hit testing and culling over ordered flex
  children (HitTester.cs:76, ElementRenderer.cs:140). Hit visits per step: scroll/wheel 5,000 rows 4,954
  (+4,974 culls a frame); input/hover 1,000 rows 993; click in 500 rows 1,493; drag 200 rows 2,406;
  a11y/wheel 1,000 rows 954. Every app list is virtual now (25–34 visits a move). Suite rows only. Medium.

## Examined, not worth it

- componenthost-patch-keeps-layout: does not happen as described — the builder moves a persistent
  child's host into the new tree and the reconciler inserts it back, never patched. A parent rebuild
  costs 4 resolves, 2–3 layouts, 0 host layout marks; what remains is the move's structural mark (with a
  reader on, one full a11y build per parent rebuild, 18 KB for a 306-element window) =
  reconcile-persistent-child-in-place, examined in round 2 (Aperture and Reel rarely rebuild those parents).
- element-forced-size-nan: −8 B per element of ~600 (−1.3 % retained); a11y/create allocKB −0.8 %.
- A VirtualList measured as a leaf: would remove 204–297 MeasureSize calls a key (Commit 1,056), nearly
  all cache hits (~1–2 % of a key); themed-patch-keeps-layout already removes Commit's; it would change
  the size of lists sized by their content.
- MeasureCache evictions: Files 205 a key (the TextArea's Size.Zero MeasureContent, trivial), Commit 88
  (the diff list, covered above).
- Layout-pass allocation in the suite is all inside text MeasureContent: one new label measured costs
  ~11 KB (component/counter 11.1 of 13.5 KB a step; component/edit and a11y/edit-1-label 11.4 KB). Lead
  for expert 5.
- TableView column resize remakes every row (264 KB a step); no app uses TableView.
- Vesper zebra tint: with Zebra on, every row made scans the folder for its index
  (CommanderApp.Pane.cs:865), O(n) per row made; off by default; not measured. Lead for expert 8.

## Needs the user

None (InvalidateRow is an added public method; SelectionColor approved 2026-10-11).
