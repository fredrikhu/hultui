SATISFIED: no

All numbers are counts and bytes at aa3a3696 (.NET 11.0.100), from headless Release (JIT) probes and one NativeAOT real-window hold. I saved the probe edits as a patch, reversed them with `git apply -R` and deleted the probe files. The worktree is clean (`git status` empty) and the build servers are shut down.

REVIEW: none.
- **dd3ad712 (a VirtualList measured as a leaf):** every list still has a size from outside.
  - **Sites:** the 16 app sites (Junction 7, Vesper 6, Meridian 2, Aperture 1), CommandPalette, TableView, InspectorView, the VirtualList, Accessibility and Benchmark samples, and the PerfSuite partner walk all set FlexGrow(1) or a height.
  - **Nothing new:** since dd3ad712, `git log -S"VirtualListElement<"` finds only b3bb93bd, which edits an existing site. Main's Aperture commits add no list.
  - **Realized as expected in the probes:**
    - Junction History has 19 children and Commit 35 at 1440×900.
    - Vesper's reader and compare view realize their rows.
    - Aperture's grid has 7 children of 314 px rows in a 780 px viewport.
- **e790d321:** touches no layout file. `LayoutEngine` still measures labels through `MeasureText`, which makes a layout. Only the apps' height passes take the size-only ask. No layout-side objection.
- **0f9c8477 (Aperture's roving Tab stop and the focus-ring copy):**
  - Moving the ring sets `Outline` (paint dirty only) and `TabIndex`. `ShowsFocusVisual` is a plain auto-property, so copying it in `PatchFrom` costs nothing. `Pressable` shares one static handler.
  - Headless, per key: grid Right 0.30 KB with 0 builds. Home Right 10.9–11.1 KB with 0.21 builds a key, which are shelf turns on 20-card shelves. Home Down 35.4–35.7 KB.

CANDIDATES:
- id: layout-list-children-without-set
  what: When a VirtualList's window moves, it makes a new children array (VirtualListElement.cs:1816) and calls `ReplaceChildren`. That builds a `HashSet` of the new children whenever old × new children exceed 64 (`SetOf`, Element.cs:1523). Tell the children that stay by a mark or a reused set, and reuse the array while the row count holds.
  evidence:
    - **Allocation sampling, Vesper reader, 1,500 notches:** the set's entries, buckets and object plus the array are about 1.7 KB of a 19.4 KB notch. There is 0.72–1.0 set-sized replace a notch.
    - **Prototype** (marks and a kept array), headless JIT, KB a wheel notch, old → new:
      - Junction History (1,772 commits) 10.49 → 9.97 (−5.0 %).
      - Junction Commit unified (Window.cs, every 12th line changed) 21.07 → 19.93 (−5.4 %); split 29.43 → 28.31.
      - Vesper reader (Window.cs) 18.98 → 17.96 (−5.4 %); compare view 30.75 → 29.90.
    - **Structural builds:** TreeDiffer.cs:266 `ReplaceChildren(resultChildren.ToArray())` takes the same set on any structural change with old × new > 64.
  real app: Wheel scrolling of Junction's History and diffs and Vesper's reader and compare view, dozens of notches a minute while reading. The real-window numbers are under the next candidate, since I measured the two together.
  expected: With the next candidate, the churn bar (below). Alone it is −2.8…−5.4 % a notch.
  cost: small. Internal only: a mark bit or a pooled set in `ReplaceChildren`, and an internal span overload or a kept array in `SyncVisibleItems`. No API or pixel change. A bool field for the mark is free only while the byte fields have room; a pooled set avoids the field.
- id: elements-selectable-label-static-wiring (the Waiting item elements-selectable-label-lazy-wiring; owner expert 1, since the code is TextElement and Element; expert 4 may review the AOT bytes)
  what: `WireSelection` (TextElement.cs:280) gives each selectable label 6 bound delegates, a menu delegate and a handler array grown 2 → 4 → 8. Use static handlers that read `e.CurrentTarget`, in one exact 6-entry array. Lazy wiring is not needed.
  evidence:
    - **Per label:** wiring 880 → 304 B (a plain label is 672 B, a selectable one 1,552 → 976 B). Without the per-label menu delegate it is 248 B (see the next candidate).
    - **Headless JIT, KB a notch, wiring alone:** Vesper reader 18.98 → 17.82 (2 labels a notch); compare 30.75 → 28.89 (3.5 labels); Junction Commit 21.07 → 20.15 (1.6 labels); split 29.43 → 27.67 (3.1 labels).
    - **Both candidates together, headless:** reader −11.5 % (16.79), compare −9.2 %, Commit −9.7 %, split −9.8 %, History −5.0 %.
    - **Real window (NativeAOT, both together), UI KB per 40 notches, settled series, old → new:**
      - Junction Commit unified 654.4–666.1 → 578.2–588.9 (−11.6 %; 16.7 → 14.7 KB a notch).
      - Vesper reader 605.8–611.2 → 511.5–516.2 (−15.5 %; 15.3 → 12.9 KB a notch).
      - Vesper compare 1,068.9–1,079.7 → 952.1–964.6 (−10.8 %).
      - Junction History 376.1–392.9 → 355.5–372.4 (−5.3 %).
      - 1 frame a notch on both sides.
    - A Junction first file visit makes ~34 rows, so about 19.6 KB of 492–570 KB (−3…−4 %).
  real app: Wheel or arrow-key scrolling of Junction diffs, Vesper's reader and compare view, and Meridian's response viewer: every row made carries one or two selectable labels.
  expected: Churn bar, together with the first candidate (one build step, two commits): −10.8…−15.5 % UI bytes a notch in the real window (Junction Commit, Vesper reader, Vesper compare); headless −9.2…−11.5 %.
  cost: small. Internal only, no API, no behaviour or pixel change.
- id: elements-context-menu-after-patch (correctness, found on the way; not perf)
  what: `Element.PatchFrom` copies `ContextMenuItems` (Element.cs:1797). For TextInput, TextArea, RichTextEditor and selectable labels that provider is the element's own `BuildContextMenuItems`, bound to the fresh copy the build made. So after one rebuild the menu acts on a detached element.
  evidence:
    - **Headless app-level probe** (a component with a TextInput, a selectable label and a counter; click, Ctrl+A, read the live element's menu):
      - Before any rebuild: TextInput "Cut, Copy" enabled; label "Copy" enabled.
      - After one rebuild: TextInput "Cut (off), Copy (off), Paste (off)"; label "Copy (off)". The provider's target is not the live element.
    - **Unit probe:** after `live.PatchFrom(fresh)`, Select All selects the detached copy. Copy writes nothing, because the copy's `Clipboard` is null (Window.cs:2953 sets it only on the focused live element).
  real app: The right-click menu of every text field and selectable text after the field's component rebuilds once: Meridian's URL and body fields, Junction's commit message and diff text (the keyboard Ctrl+C path is unaffected), Vesper's rename, Aperture's search.
  expected: Correctness, like round 17 step 7.
  cost: small. Keep an element-owned provider across a patch: a flag set by the wiring, and copy the source's provider only when the app set it. Alternatively serve the own menu through an internal virtual the window reads when no app menu is set; that saves 56–64 B a label but changes what the public `ContextMenuItems` getter returns, which three Core tests read. No pixel change.

REAL WINDOW:
- **Setup:**
  - 1 hold of `Global\HultUI-r18-desktop`: waited 351.0 s, held 161.3 s. 2 launches: Junction and Vesper, NativeAOT probe builds.
  - 0 foreground takes, 0 nvlddmkm events. Idle gate passed beforehand (CPU 1 %).
  - The 4K primary (3840×2160, 150 %, DPI 144, 120 Hz); the 1440p was attached at 100 %, 120 Hz. Steam running; timer resolution 1 ms. The OS cursor was outside the windows.
  - Window sizes: Junction 2182×1406 px (1455×937 logical); Vesper 1882×1286 px (1255×857 logical).
  - Wheel events injected in-process at the centre of the largest list, 40 ms apart, 40 notches a series. The probe switch was alternated four times a view, with the order flipped between views.
- **What the real window showed that headless did not:**
  - AOT notches allocate less in total (Junction Commit 16.7 vs 21.1 KB headless; Vesper reader 15.3 vs 19.0 KB). The saving in bytes is the same or larger (reader 2.4 vs 2.2 KB a notch), so the shares are larger.
  - The first series after a view switch runs high (Junction Commit 747 KB, reader 839 KB); I excluded it as warm-up.
  - GC over the scene: Junction 4/1/0, Vesper 8/3/1. No extra frames.

WAITING, RE-RANKED:
- **dropdown-follow-control-on-resize:** no commit since round 16 touches DropdownElement, DatePickerElement or `FramePressedPending`. Unchanged: correctness, low, not perf.
- **vesper-brief-filter-clear-reveal:** CommanderApp.Views.cs is untouched since round 17. Unchanged: not perf; the `OnLayoutChanged` route still applies.
- **controls-menu-max-height-scroll:** ContextMenuElement is untouched. Not perf.
- **elements-selectable-label-lazy-wiring:** measured and promoted as elements-selectable-label-static-wiring. Owner expert 1. Static handlers get 880 → 304 B, so lazy wiring is not needed.

EXAMINED, NOT WORTH IT:
- **Aperture keyboard browsing (0f9c8477):** no layout pass on ring or Tab-stop moves (numbers under REVIEW). Grid rows have 7 children (49 < 64), so the first candidate does nothing there.
- **Lead for expert 8 or 6, cause not confirmed:** in the headless grid with `NoArtwork` (404), a Down that realizes a new row builds the whole view, 85–124 KB a key against 0.3 KB otherwise: 1 build a new row, even with 60 films and no paging. The new row's posters are requested, and apparently their failure asks for a build. With a real server each landing does. Worth a real-poster count.
- **BoxElement share:** BoxElement is 24.6 % of a reader notch (4.9 KB; about 4 boxes a row). Element size is expert 4's area and was closed before.
- **A relayout boundary at a now-leaf VirtualList:** not re-measured. Round 5 put a selection move's ancestor relayout at 1–2 %, and it is still a large engine change.

NEEDS THE USER: none. All three candidates are internal; the menu fix keeps the public getter unless the virtual route is chosen.

FILES (in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\00f54f03-b511-4949-9273-30fe719e1bbb\scratchpad\r18-layout\`):
- **Probe:** `probe-r18.patch` (Element.cs, TextElement.cs and VirtualListElement.cs switches) and `probe-new\` (ProbeR18ScrollTests.cs for Junction, ProbeR18ViewerScrollTests.cs for Vesper, ProbeR18KeyboardTests.cs for Aperture, ProbeR18MenuAfterRebuildTests.cs headless, ProbeR18MenuPatchTests.cs for Core).
- **Headless results:** vesper-wheel.txt, vesper-wheel2.txt (2×2 wiring/replace), vesper-types.txt (allocation sampling), junction-wheel.txt, aperture-keys.txt, aperture-why2.txt, menu-patch.log, menu-rebuild.log, idle-gate.log.
- **Real window:** `rw\` (pub.ps1, batch18.ps1, rest\common18.ps1, start\src\Script.cs with the pw, ui and wheelbig steps). Results are in rw\rest\runs\r18L1-*; hold log rw\locks.log; desk rw\rest\runs\r18L1-desk.txt.
- **Fixtures:** fx\jrepo (1,772 commits plus two probe commits of Window.cs), fx\vleft, fx\vright.
