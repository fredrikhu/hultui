# Round 19 survey, expert 1: Layout

SATISFIED: no

All numbers are counts and bytes at 56d9a3f6 (.NET 11.0.100). They come from headless Release (JIT) probes and one
NativeAOT real-window hold. The probe edits are saved as `probe-r19.patch` and were reversed with `git apply -R`; the
probe files are deleted. The worktree is clean (`git status` empty) and the build servers are shut down.

**Orchestrator's jrepo note:** my Junction fixture is a copy of `r18-layout\fx\jrepo`, not the r14 one. It is clean:
`git status` shows 0 changes and there are 3,356 tracked files at 130328e4. My Junction numbers stand.

REVIEW: none.

- **bd3605ff adds no per-frame or O(n) work in Meridian, Vesper's viewer or Junction's diffs.**
  - **No app uses the new parts:** a grep of apps/ finds no `ScrollbarElement`, `Scrollbar.Vertical/Horizontal`,
    `DrawTextLine` or `CanvasElement.TextSource`.
  - **Existing scrollbars:** ScrollView, TextArea and the rich text editor draw through `Scrollbar.PaintAt` with the
    same arithmetic. The only addition is one more `Viewport()` float computation; nothing is allocated.
  - **Accessibility role switch:** it gains one type arm. `ScrollbarElement` is not sealed, so this is a hierarchy
    check per unmatched element, and only while a11y is on.
  - **CanvasElement** grows 8 B. Junction's History rows hold one canvas each, about 1.3 rows a notch, so about 10 B a
    notch.
- **f97a6736:** `_childArray` only ever holds the current children, and it is cleared when the list empties. No leak.
- **30a53f02, 3442c3f5 and c3f0eab0:** clean. The label clipboard gap is in Waiting.
- **d9bf8e25:** `ContextMenuAnchor` is a virtual property, not a field. `ContextMenuItem` is now 80 B, with its parsed
  label cached and parsed only for items that use mnemonics.
- **ab8a2050:** only Junction's Markdown uses TableView, and it does not use the app sort. `SetSortKeys` allocates a List
  and a closure per call.
- **6d95da0c:** one closure per dismissal.
- **11554b8b:**
  - **ButtonElement** 640 → 664 B (+24 B for three fields). A Junction view build makes about 4 buttons, so about
    0.1 KB a build.
  - **TextElement** stays 672 B; the new int fits in padding.
  - **Alt press** repaints the root (paint only, one frame).

CANDIDATES:

- id: layout-list-row-drop-keeps-ancestors
  what: When a VirtualList lets go of rows whose state changed, `DropGathered` (VirtualListElement.cs:1179–1192) calls
  `MarkLayoutDirty`. That resets every ancestor's MeasureCache and re-lays out the view around the list. Since dd3ad712
  a list's own size never depends on its rows, and the next frame's `UpdateContentSize` → `LayoutVisibleChildren`
  realizes and places the dropped rows itself. So the drop should mark paint only.
  evidence:
  - **Prototype** (a switch on that one line). Headless, per key, base → change:
    - Junction Files tree, cursor onto a folder: layouts 64.8 → 8.0; container measures 227 → 0; leaf measures 176 → 14;
      content measures 112 → 6.
    - Commit file tree (48 files in 12 folders), onto a folder: layouts 64.5 → 11.9, measures 162 → 1.3, leaves 166 →
      30, contents 68 → 11.
    - A step onto a file: layouts 105 → 68.
    - Commit view Down/Up: measures 8.1 → 0.
    - Bytes change by 0 to −1 KB.
  - **Where the misses come from:** in one pass the Files preview TextArea is measured at 96 distinct (width, height,
    forced height) keys and the tree list at 10. Junction's Commit diff list is measured at 48.
  - **Pixels:** 280 headless frames are byte-identical (70 per scene, two scenes, two runs). The real window gives the
    same counts (REAL WINDOW).
  - **Unaffected keys:** Junction History Down (8 layouts) and Vesper's pane walk are unchanged.
  real app: Junction's Files tab and Commit file tree, walked with the arrow keys, every key. Also any build that changes
  a kept row's state. With the next candidate, this relayout is most of what such a key still does.
  expected:
  - **Time bar:** to be timed by step-ab. It removes about 400 measure calls and 57 box layouts a key, work that grows
    with the size of the view around the list, per row change.
  - **Round 5 comparison:** round 5 put a History key's ancestor relayout at 1–2 %. Junction's Files and Commit views
    measure 15–25× more per key.
  cost: small. One line plus tests: a row-state change leaves the ancestors' MeasureCache intact, and frames stay equal.
  A row height function (`ItemHeightFor`) was not re-run by `DropGathered` at the base either. No API or pixel change.
- id: apps-junction-tree-cursor-rows-only (owner 6 or 8: app code; the framework half is the candidate above)
  what: Junction's Files tree and Commit file tree build the whole view when the cursor moves onto a folder or a key runs
  past the list's end (`MarkViewDirty`, JunctionApp.Files.cs:584–594, JunctionApp.Commit.cs:953–960). With the list's
  `CurrentIndex` and `RenderWithState`, which no app uses yet, a move remakes two rows and a key that moves nothing does
  nothing.
  evidence:
  - **Prototype in the Files tab** (about 15 lines): items without the cursor flag, `RenderWithState`, and the live
    list's `CurrentIndex` plus `ScrollRangeIntoView` set instead of a build.
  - **Folder step:** 54.4 KB, 1 build, 44 elements made → 9.1 KB, 0 builds, 8 made (−83 %). With the candidate above it
    is 8.1 KB (−85 %), with 8 layouts and 0 measures.
  - **Down at the list's end:** 45.4 → 0.2 KB (−99.6 %).
  - **Pixels:** 0 of 280 frames differ.
  - **Commit tree, not prototyped:** a folder step is 72.4 KB, 1 build, 63.7 made; Down at the end is 60.1 KB, 51 made.
    The difference between those two (12.3 KB) is the two rows plus the relayout, so a folder step should come out near
    12 KB.
  real app: browsing Junction's Files tab by keyboard: 12 top-level folders come before the first file, and every folder
  level is a row. Also commits with nested folders, and a held Down at a list's end (each repeat 45–60 KB, about
  1.4–1.8 MB/s at 30 repeats a second).
  expected: churn bar. −83…−85 % per folder key and −99 % per key that moves nothing. Real window: 52.6 KB (Files) and
  79.9 KB (Commit) a key today.
  cost: small to medium, app only, and the same change in both trees. No API, frames identical. These lists are
  `RowsAreComposite`, so a reader is told no current item, before or after.

REAL WINDOW:

- **Setup:**
  - 1 hold of `Global\HultUI-r19-desktop`: waited 218.9 s, held 101.4 s.
  - 2 launches: Junction and Vesper, NativeAOT builds of my own probe.
  - 0 foreground takes, 0 nvlddmkm events. Idle gate passed beforehand (CPU 8 %).
  - Monitor: the 1440p only, 2560×1440 at 100 %, 200 Hz, primary. Steam running; timer resolution 1 ms.
  - Windows: Junction 1456×939, Vesper 1256×859. The OS cursor was inside both windows.
- **Junction Files tree,** 22 folder keys a series, alternated ×2:
  - UI bytes 1,161.2 / 1,153.8 → 1,148.5 KB (52.6 → 52.2 KB a key).
  - Layouts 60.5 / 58.4 → 8.0 a key; container measures 226 → 0; leaf measures 172 → 14; content measures 112 → 6.
  - 22 frames on both sides.
  - These are the same counts as headless: this layout path is deterministic.
- **Junction Commit tree,** Up/Down between a folder and the open file, 12 keys, alternated ×2:
  - 959 KB on both sides (79.9 KB a key).
  - Layouts 32 → 9; measures 242 → 1; leaves 206 → 22; contents 106 → 9.
  - 1 frame a key.
- **Junction History:**
  - Wheel: 11.2 KB a notch down and 9.3 up, 8.2–8.7 elements made a notch, layouts equal to elements made, 1 frame a
    notch.
  - With the cursor inside, there is 1 hit test a notch visiting about 22 elements; no extra hover frames.
  - Down: 21.3 KB a key, 12.2 made, 6.2 laid out.
- **Vesper:**
  - Reader wheel: 14.9 / 12.6 KB a notch, 12 elements made, 1 hit test visiting 30, 1 frame.
  - Pane Down/Up: 1.5 KB a key, 0 elements made, 0 layouts, 1 frame. Headless (JIT) was 2.5 KB with 1.2 made, because
    that walk also scrolled.
- **GC over the scenes:** Junction 2/1/0, Vesper 1/0/0.

WAITING, RE-RANKED:

- **dropdown-follow-control-on-resize:** 6d95da0c touches DropdownElement and DatePickerElement, but only to clear the
  swallow flag after the next frame. Neither anchoring nor the read of `FramePressedPending` changed. Unchanged:
  correctness, low, not perf.
- **vesper-brief-filter-clear-reveal:** CommanderApp.Views.cs is untouched since b3906200, before round 18. Unchanged:
  small, not perf; the `OnLayoutChanged` route still applies.
- **controls-menu-max-height-scroll:** d9bf8e25 adds keyboard open, mnemonics and type-ahead to ContextMenuElement, but
  no maximum height or scrolling. A long menu is now walkable by keyboard yet still runs off screen. Not perf.
- **label-focus-no-clipboard:** confirmed; correctness, small.
  - **The gap:** `Window.OnFocusChanged` (Window.cs:1008–1035) hands the clipboard to TextInput, TextArea and
    text-services clients only. A label gets it only from Tab (`MoveFocus`, :956) and from a press (:2968).
  - **Real paths that miss:** a reader's focus request (`AccessibilityActionRouter.RequestFocus`, :248–252 →
    `Element.Focus()`), and any app's `Focus()` on a label (no app does that today). A reader user who moves to a
    selectable label (Junction's diff or message text, Meridian's response) gets nothing from Ctrl+C or the menu's Copy
    (Shift+F10 since d9bf8e25).
  - **Fix:** at the top of `OnFocusChanged`, `if (element is TextElement { Selectable: true } label) label.Clipboard =
    Clipboard;`. The chain below still stops text input. The two existing copies become redundant.
  - **Test:** a selectable label focused by `label.Focus()`, then Ctrl+A and Ctrl+C, and `session.Window.Clipboard.Text`
    equals its text. A second case focuses it through `session.Window.InstallActionRouter().FocusElement(label.Id)`.
    Both fail at the base and pass with the line.
  - **Size:** about 3 lines and a 30-line test; no API or pixel change. It could ride with the first candidate's step.

EXAMINED, NOT WORTH IT:

- **Pointer moves over lists:** 1 hit test a move, 21–46 elements visited, 0.04–0.10 KB, no frames. Measured headless on
  Junction History, Commit and split, and Vesper's pane, reader and compare view.
- **Wheel notches after round 18:** the work follows the rows made, with layouts equal to elements made and no ancestor
  relayout.
  - **Headless JIT, KB a notch:** Junction History 11.5, Commit 18.5, split 29.6; Vesper pane 11.4, reader 17.1, compare
    30.3.
  - **Types:** TextElement and BoxElement are 50–65 % of the bytes; element size was closed before.
- **PageDown makes a whole new window of rows:** Vesper reader 216 KB (197 elements), compare 380 KB (367), pane 124 KB
  (112); Junction History 145 KB (133). Only row recycling would change this, and pools were declined (P4/P5).
- **Leaf measure keys alone:** the 96 TextArea and 48 list misses are each a size-zero content measure. A width range
  for content-free leaves would save about 90 cheap calls a relayout, under the noise; it was known since round 5 (48 a
  resize step).
- **Lead for expert 8 or 6:** a Junction History Down is 23–27 KB a key, of it strings 8.7 KB (43 %) and a
  `Dictionary<string, Element>` 1.7 KB. That is build and app work.
- **Not measured:** Junction Changes (needs a dirty working tree) and Meridian's lists (the same VirtualList paths).

NEEDS THE USER: none. Both candidates are internal or app-only; the frames came out identical.

FILES (in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\e91920b9-3793-496a-9a73-f017250d8772\scratchpad\r19-layout\`):

- **Probe:** `probe-r19.patch` (ProbeR19 counters, the `NoDropLayout` and `AppCursor` switches, the Junction Files
  prototype) and `probe\` (ProbeR19Alloc.cs, ProbeR19JunctionTests.cs, ProbeR19VesperTests.cs, ProbeR19TreeWalkTests.cs,
  ProbeR19SizesTests.cs).
- **Headless results:** junction-1.txt, vesper-1.txt (types), junction-nodrop0/1.txt, vesper-nodrop0/1.txt,
  treewalk-1…5.txt (treewalk-3 has the constraint breakdown, treewalk-5 the prototype), sizes.txt, idle-gate.log.
- **Real window:** `rw\` (pub.ps1, batch19.ps1, rest\common19.ps1, start\src\Script.cs). Results are in
  rw\rest\runs\r19L1-*; hold log rw\locks.log; batch output rw\batch-r19L1.txt.
- **Fixture:** `fx\jrepo` (clean).
