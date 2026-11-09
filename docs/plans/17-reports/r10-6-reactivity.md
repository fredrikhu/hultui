# Round 10 survey, expert 6: reactivity and reconcile

SATISFIED: no

Measured on 3462f18e, detached. Headless 1440×900, JIT Release, pinned to P-cores. A step is one key or one pointer
move plus two frames. Bytes come from `GetAllocatedBytesForCurrentThread`, split into round 9's buckets (`apply10.py`
is `apply9.py` pointed at this worktree; every anchor still matched). No timings. The prototypes sit behind `R10_*`
environment switches in the same binary. Afterwards I reversed everything with `git apply -R final10.patch` and
deleted `R6Probe.cs`; the worktree is clean.

Fixtures:
- **Junction:** a repo of 300 files over 30 commits, with 60 files modified and 5 untracked (`mkrepo.ps1`). Round 9's
  `fx\jrepo` had lost its `.git`.
- **Meridian:** a 25-request collection built by the driver, plus 175 more for the large case. A loopback server
  answers with a 2,000-line JSON body.
- **Aperture:** `FakeMediaLibrary` with 35 films, and `FakeRequestServer` holding one issue with 6 comments.

REVIEW: none.
- **Reactivity code:** the only change in round 9's range is 25a96234. Its prune skips entries made this build, and the
  doubling bound is unchanged.
- **0ef0b61f:** `UrlCountsOf` scans the body once per character with `IndexOf` (`Chain.Analyze` → `VariableNames`).
  That costs 0 B, and the old per-character build did the same scan.
- **c940c643:** a full build is flat with the number of requests. A toast at 25 requests is 271.3 KB and at 200
  requests 279.9 KB: the tree part grows by +7.7 KB for 175 more requests, about 22 B a request per build.
- **cb1df8c0..d1274992:** no objection. The map at 2,000 endpoints still costs about 35 B an endpoint a click, which
  step 11 accepted.
- **0a9a9c64 and 71f95ff1 (the Aperture grid; 4142ee85 is only the suite row):** no objection. There is one post-frame
  callback per change of columns.
- **ea42f45b and 9f83f755:** no objection; the check is one role compare.
- **b4dc84b4..76dce98a:** no build added. These are `Focus()` calls only, and `PaneName` is memoized by folder.
- **74cf844c:** the list of preview pages drops unhosted pages on each build, and no build was added.

CANDIDATES:
- id: apps-typing-builds-nothing-junction-aperture
  what: Characters typed into Junction's prompt, Junction's rebase reword and Aperture's issue reply and report message
  build nothing, as Meridian's fields have since 0ef0b61f. The Aperture fields would build only when "empty" flips,
  which enables or disables the Send button.
  evidence:
  - **Junction prompt** (`JunctionApp.Dialog.cs:153` and `:205`, `_dialogValue = v; MarkDirty()`). The build reads the
    value only in `TextInput.New(_dialogValue)`; Validate and Confirm read the field when Enter is pressed. Each
    character makes a full build: the app plus the title bar, toolbar, sidebar and composer parts.

    | View behind the prompt | KB a character | Elements made | Builds | With the reader bridge |
    |---|---|---|---|---|
    | Changes (65 changed files) | 194.4 → 1.4 | 172 → 0 | 5 → 0 | 194.9 → 1.9 |
    | History | 154.8 → 0.6 | 138 → 0 | 4 → 0 | 155.2 → 1.2 |

    The chrome parts are 118.6 KB of each of these builds: toolbar 51.4, sidebar 43.3, title bar 23.9.
  - **Aperture issue reply** (`ApertureApp.Issues.cs:416`): 135.1 → 2.3 KB a character (−98 %), 137 → 0 elements, 1
    app build + 2 part builds → 0.
  - **Same pattern, read in the code but not driven:**
    - Aperture's report message (`Report.cs:187`; it is in the player).
    - Junction's rebase reword (`Rebase.cs:548`, a TextArea).
    - Junction's repo modal path (`RepoModalView.cs:187`): it needs a view build for the path hint and the button, but
      not the chrome, so `MarkViewDirty` saves 118.6 KB a character.
    - Junction's MR list filter (`MergeRequests.cs:385`): the same `MarkViewDirty` saving.
  expected: churn −98…−99.6 % a character (per key). The cost now grows with the view behind the field (O(elements));
  after the change it does not.
  cost: small, app only. No API change, same pixels. A guideline already exists (REACTIVITY.md, from step 8); the HULTUI
  analyzer could also flag `MarkDirty` inside `OnTextChanged`.

- id: apps-junction-diff-rows-keep-width
  what: When the diff neither wraps nor shows a review, leave the code width out of the diff's height shape
  (`JunctionApp.Diff.cs:519`), so a width change stops making every diff row again.
  evidence:
  - `shape` includes `codeWidth`. Every width change runs `mounted.InvalidateRows()` (`Diff.cs:525`), even though
    without wrapping the width feeds only `RebuildDiffHeights` (skipped) and the pan's viewport (`Diff.cs:716–721`).
  - Changes split drag: 159.7 → 42.4 KB a move (−73 %); elements unmounted 127 → 0; render 119.5 → 2.2 KB, which was
    the remade rows' first layout.
  - Changes window resize, 4 px steps: 32.8 → 21.0 KB a step (−36 %); render 14.0 → 2.2 KB.
  expected: churn −36 % on a Changes resize step and −73 % on a split-drag move.
  cost: small, app (one line plus a test). Pixels should be the same, but the build agent should confirm with a pixel
  A/B at several widths.

- id: apps-pane-drag-sets-size (absorbs Waiting apps-meridian-response-drag-build)
  what: While a `PaneSplitter` drag is under way, write the size onto the live pane through a `Ref` and call
  `MarkLayoutDirty()`; build once when the drag is let go.
  evidence: prototype measured over 40 moves. The pane followed the pointer: −40 px for −40 px.
  - Meridian, 2,000-line body: 38.9 → 2.5 KB a move (−94 %), 1 → 0 part builds, 34 → 0 elements.
  - Meridian, no response: 11.0 → 0.1 KB a move.
  - Junction Changes split: 159.7 → 2.3 KB a move with the base code (−98.5 %), or 42.4 → 2.3 once
    apps-junction-diff-rows-keep-width lands.
  expected: churn −94…−98.5 % a drag move. It is a drag, so rank it low.
  cost: small–medium, app only. Risk: anything else a build reads from the size. Junction sets `_diffPan.ViewportWidth`
  at build time, so the pan would be stale until the release; Meridian has `SetMax`. A full-build equality check
  mid-drag is needed. The framework route is under NEEDS THE USER.

- id: apps-junction-view-only-clicks (lead)
  what: Junction's full `MarkDirty` rebuilds the three chrome parts, 118.6 KB, from paths that change nothing the chrome
  reads. Those paths should use `MarkViewDirty`.
  evidence: the chrome is 61 % of a prompt character and 65 % of a toast. I checked by grep that `JunctionApp.Chrome.cs`
  and `Sidebar.cs` never read `_stageSel`, `_dialog` or the toasts. Sites:
  - stage-line and hunk selection clicks (`Staging.cs:56`, `:69`, `:72`);
  - opening and closing a dialog (`Dialog.cs:68`, `:82`);
  - closing a popover (`JunctionApp.cs:1262`);
  - the search-focus keys (`JunctionApp.cs:1549–1589`);
  - file-tree expand (`JunctionApp.cs:438`, `441`).
  expected: −118.6 KB a click at each site, by the part buckets; not run per site.
  cost: small per site, app only. Keep "safe by default": change only the sites that can be shown to be view-only.

WAITING, RE-RANKED:
- **apps-toast-gone-view-only:** still worth doing, rank low. Prototype
  `ToastHost(() => IsShowing ? MarkDirty() : MarkViewDirty())`, measured flash to gone:
  - Junction Changes 366.3 → 227.4 KB (−38 %), part builds 8 → 4. Junction has the same `ToastHost(MarkDirty)` at
    `JunctionApp.cs:425`.
  - Aperture Home 148.5 → 90.1 KB (−39 %). My fixture adds a request server, which puts the rail at 79.5 KB against
    round 9's 60.2; that is the fixture, not a regression.
  - Meridian 271.3 → 213.0 KB at 25 requests (−21.5 %) and 279.9 → 216.3 KB at 200 (−23 %). It is lower than round 9's
    −37 % because the tree rows are now kept.
  - It is a one-off event, but it clears the churn bar per toast. Cost: 3 lambdas.
- **apps-meridian-response-drag-build:** still worth doing. It is now 38.9 KB a move and −94 % with the size set
  through a ref; I folded it into apps-pane-drag-sets-size.
- **vesper-brief-filter-clear-reveal:** not perf. Brief's scroll code is unchanged since 3fdf33c3 (9f83f755 only set
  roles), and I did not measure it again. It stays with expert 1, since the sideways reveal needs an apply-after-layout
  form.
- **junction-changes-leads:** reader behaviour, with no perf number. It stays with expert 7.

EXAMINED, NOT WORTH IT:
- **Vesper name prompt, Meridian dialogs, Junction MR title and composer:** they already build only when something
  shown moves (`Overlays.cs:228`, `CreateMergeRequest.cs:394`, `Changes.cs:1276`).
- **Discovery map at 2,000 endpoints:** still O(n) a click at about 35 B an endpoint (124 KB). Only a virtual list would
  remove it, and step 11 accepted the current cost.
- **Junction History resize:** 33.6 KB a step. The rows are made again only on about 0.1 builds a step, at layout
  breakpoints, so it is not the diff's width rule.
- **Reel:** no typing build and no full build per key.

NEEDS THE USER:
- **Aperture: Backspace in a text field also goes back.** Found while measuring; it is not perf. Headless: typing "ab"
  in Search and pressing Backspace leaves "a" and goes from Search to Home. The issue reply does the same. This dates
  from da0676d0 (2026-10-05), where `browse.back` = Escape or Backspace, checked at `ApertureApp.cs:554`; only the
  cursor keys check whether the target is a text field. `TextInput` and `TextArea` never stop the editing keys they
  handle.
  - Decide between: (a) Aperture skips `browse.back`'s Backspace when the target is a `TextInput` or `TextArea` (small,
    app only); or (b) text fields mark the keys they consumed as handled. Option (b) changes key routing in every app.
- **PaneSplitter (only if the framework route is preferred over app refs):** should `PaneSplitter` accept the pane's
  element and size it during a drag, calling `onChanged` only on press and release? That is public API. The app-level
  refs need no API.

FILES: all in `C:\Users\fredr\AppData\Local\Temp\claude\eb4e2037ca05cb48\3484c586-c631-4af0-957d-95e7a03cac41\scratchpad\r10-reactivity\`
- Probe: `apply10.py`, `R6Probe.cs` (round 9's copy), `R6Probe10.cs` (with the switches), `final10.patch` (the probe
  plus the prototypes R10_JDIALOG, R10_AISSUE, R10_TOAST, R10_MDRAG, R10_JDRAG, R10_JROWS).
- Driver: `drv\Program.cs`, `drv\R10Drv.csproj`. Modes: jdialog, jtoast, jdrag, jresize, mtoast, mdrag, atoast, aissue,
  asearch. `R10_WHO=1` lists the elements one drag move replaces.
- Fixture: `mkrepo.ps1`, `fx\jrepo`.
- Outputs: `run-jdialog.txt`, `run-jdialog-fix.txt`, `run-jdialog-a11y.txt`, `run-jdialog-a11y-fix.txt`,
  `run-jtoast-jdrag.txt`, `run-jtoast-jdrag-fix.txt`, `run-jdrag-fix.txt`, `run-jdrag-who.txt`,
  `run-jdrag-who-fix.txt`, `run-jresize.txt`, `run-jresize-rows.txt`, `run-m-a.txt`, `run-m-a-fix.txt`,
  `run-mdrag-aissue.txt`, `run-mdrag-aissue-fix.txt`, `run-aissue.txt`, `run-aissue-fix.txt`, `run-aissue-dbg.txt`,
  `run-asearch.txt`.
- Review: `show-c940c643.diff`, `show-3c945439.diff`, `show-d1274992.diff`.
