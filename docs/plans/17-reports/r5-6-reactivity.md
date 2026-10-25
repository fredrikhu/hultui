# Round 5 survey: 6. Reactivity and reconcile

At 969f3308 (detached; worktree left clean, no commits). Headless JIT, Release, P-core affinity, 1.5 s warm-up per walk. A step is one key and two frames. Bytes come from `GetAllocatedBytesForCurrentThread`, split into exclusive phase buckets (build, reconcile, style, layout, a11y, render, handler registration, patch handler copy, plus per-app build sections), with counts per step. Allocation numbers and counts only; no timings were taken.

Double rebuilds were found by logging every component that built twice inside one dispatched event (`R5_DOUBLE`), across six full test suites: Reel 50, Meridian 418, Aperture 296, Vesper 852, Media 83 and Junction 989 tests, all passing with the probe in.

Probe code is in the scratchpad under `r5react/`:
- `apply.py` and `remove.py`: insert and remove the counter lines (every line ends in `// PROBE`).
- `R5Probe.cs`: the counters, with prototype switches `R5_PROTO_RESTORE` and `R5_PROTO_HANDLERS`.
- `drv/`: the walk driver. Modes `junction,typing,files,vesper,meridian,aperture,reel,vfind,mhsearch`; `R5_A11Y=1` installs the bridge; `R5_VIEWS` and `R5_VW`/`R5_VH` pick Vesper views and window size.
- `R5ReviewTypingProbe.cs`: a Junction.Tests probe, adapted from round 4's review probe.
- `doubles.sh` and `doubles-*.log`: the double-rebuild runs.
- `run-*.txt`: raw outputs.

SATISFIED: no

## Review

- **87698f16 with e6a432fa (objection).** `TryRestoreMoved` runs only at the end of `ReconcileKeyed` (TreeDiffer.cs:257).
  - When a build hands back every child of a container, the live parent has no children left at reconcile time. `ReconcileChildren` then takes its `oldSnapshot.Length == 0` path (TreeDiffer.cs:142-147). That path calls `AddChild`, and `DetachSilently` → `SettleMove` marks the parent layout + Children dirty for each child, which is exactly the marking 87698f16 set out to avoid.
  - Vesper Brief: 138 cells handed back per key, 111 of them settled this way (only the 1.03 columns per key that hold a remade cell restore). With a reader that is a full a11y tree every key (1.00/key, 70.2 KB). It also costs 6,284 pending-move comparisons per key in the linear `SettleMove` scan.
  - Reel: the VideoPlayer host is its container's only child, so every app build with a reader rebuilds the a11y tree (1 tree, 3.8 KB per build).
  - → reconcile-restore-moved-into-empty.
- **b35fc587 / 3135de73 (objection, app).** Typing in a review comment rebuilds the whole app twice and the three chrome parts once per character, and weaves the review again each time: 1,367 KB per character. → junction-review-composer-typing.
- **fbec1130: correct.** The style phase allocates 0 B in every walk; resolves per key now equal elements patched plus elements made.
  - The Log's guideline holds: "a write through a `Ref` into kept rows (or any live element) needs `MarkStyleDirty()`". Inherited setters mark nothing, and a patch now resolves only what it patched.
  - The framework's live inherited writes (Terminal link cursor, WebView cursor, Button text colour in `ApplyTheme`) mark style. A grep finds no app writing inherited style through a Ref.
  - Part (b) is examined, below.
- **e6a432fa: correct otherwise.** A column holding a remade cell restores unmarked: `restoreMoved` 1.03/key, `pendingMove` 138/key. Only all-kept columns miss (above).
- **a3aeabb2: fine.** The cost of theming the twin was accepted in the commit. Patched themed controls keep layout (Meridian URL typing: layout 0 B, 0 rows remade).
- **3b0bc6a5 / 94fb3179: correct.**
  - The card writes go through Refs to Left, width, aspect ratio (each marks layout only on change) and Source, SourceRect and time `Content` (paint/layout). There are no inherited-style writes, so no `MarkStyleDirty` is needed.
  - `ShowPreview` reads untracked, and a card coming or going goes through `_previewComesOrGoes`.
- **418f25a6: correct.** Rented snapshot; re-entrant notify rents its own buffer. `ReactiveList` still copies a `List` per mutation (examined).
- **79a34897:** Tags walk 1.00 build/key. **5250e1f:** nothing in my area; a signal write from any handler still marks dirty, so it still gets a frame.
- **Minor (no candidate):** a `Build()` that throws restores `Element.Building` in `finally` but leaves `_pendingMoves` unflushed. The old parent then stays unmarked until the next outermost reconcile. Harmless unless an app catches build exceptions.
- **Current per-key bytes, a11y off:**
  - Junction: History 80.0, Commit 78.8, Branches 104.2, Tags 86.1, Files search 65.7, summary typing 95.8 KB. Chrome parts built per key: 0 (they restore 3/key).
  - Vesper Detail 244.0, Brief 230.5, Thumbnails 305.5; Meridian History 353.3, URL typing 264.8; Aperture grid Right 131.2, Down 196.2 KB.

## Candidates

- **id: reconcile-restore-moved-into-empty** (6, framework)
  - what: `ReconcileChildren`'s "no old children" path tries `Element.TryRestoreMoved(existing, newSnapshot)` before adding. A container whose every child the build handed back then gets them back unmarked, like the keyed path.
  - evidence: TreeDiffer.cs:142-147 vs :257. Prototype (one line):
    - Vesper Brief, bridge on: 300.7 → 234.0 KB/key (−22 %); a11y trees per key 1.00 → 0; parents marked by settled moves 111 → 0 per key; settle comparisons 6,284 → 0; restores 1.03 → 5 per key.
    - Reel app build, bridge on: 7.2 → 3.5 KB (−51 %); trees per build 1 → 0.
    - Bridge off: Brief +3.5 KB from the prototype's list copies.
    - The Brief cursor has no a11y state, so the dropped tree diffed to nothing; no reader event is lost.
  - expected: churn −22 % per Brief key with a reader, −51 % per Reel app build with a reader; removes an O(cells × pending moves) scan per key.
  - cost: small. `TryRestoreMoved` should take a span and reuse a scratch list for `before` so bridge-off bytes stay flat; an O(1) "move pending" flag on Element would make `SettleMove` free when nothing is pending. No API or pixel impact.

- **id: findbar-step-double-rebuild** (6, framework `HultUI.Core/Builders/FindBar.cs`)
  - what: `FindBar.Move`, `ToggleMatchCase`, `ToggleRegex` and `Close` let the `TextSearch` call `onChanged` and then call `_onChanged` again. Every find step (Enter, Shift+Enter, F3, n/p, arrow buttons) rebuilds the owner twice in every app.
  - evidence:
    - Cause: FindBar.cs:51 `this(new TextSearch(onChanged), onChanged)`; :141-146 `Search.Move(dir); … _onChanged();`; TextSearch.cs:139-144 also calls `_onChanged()`.
    - Test logs: Junction DiffFindTests 299 doubles plus Files find; Vesper DiffFindTests 16.
    - Vesper reader, find Enter: 518.9 KB/step with 2 builds/step (one build ≈ 130 KB). Junction diff/files and Meridian response find bars use the same path.
  - expected: builds per find step 2 → 1; Vesper reader find step about 519 → 390 KB (−25 %).
  - cost: small. Set `_pendingReveal` before calling the search and skip the second `_onChanged` when the bar's own search already notified. Open with a selection can notify 3 times (SetQuery, SeekTo, Open); fold it into one. No API change.

- **id: junction-review-composer-typing** (6/8, app; follows junction-review-rows-shape)
  - what: one rebuild per typed character, with the chrome kept and the woven rows kept.
  - evidence: R5ReviewTypingProbe, 400-line file, review composer, 30 characters:
    - 1,367.3 KB per character (bridge 1,481.1, 1 tree/char).
    - Builds per character: JunctionApp 2, ChromePart 3. Build 944.9 KB, render 291.4 KB, 230 rows remade, handler registration 92.2 KB.
    - Two app builds: `MarkdownEditor` passes `OnDocumentChanged(changed)` and `OnSelectionChanged(MarkDirty)` (JunctionApp.MarkdownEditor.cs:51-52, Review.cs:1038). A typed character raises both.
    - Chrome builds: `MarkDirty` bumps `_chromeRevision`.
    - Re-weave: the composer's document is among what the woven list is kept against.
    - The Create MR description editor has the same double: 44 + 32 + 8 doubles in its tests.
  - expected: −80…−90 % per character (from builds 2 → 1, chrome 3 → 0, weave and rows kept). Fix by:
    - storing the document through a signal, or marking in one of the two callbacks. The Fluent sample's signal-plus-MarkDirty pairing already coalesces, because the forced build snapshots the signal's version.
    - using `MarkViewDirty` for typing.
    - remaking only the composer row (its height key) when only its text changed.
  - cost: medium, app only. The alternative, the editor not raising `SelectionChanged` for the caret move an edit makes, would change when an app callback runs; I recommend the app fix.

- **id: apps-meridian-components** (6/8, approved one app at a time after Junction)
  - what: title bar, rail, collection tree and response panel as persistent parts, Junction's ChromePart pattern; URL and param typing opt out.
  - evidence: Meridian URL keystroke (1400×900, 25-request collection) 264.8 KB/key, 293.0 with a 2,000-line JSON response. Every element is patched per key (213/234 patches, 214/235 resolves, 0 remade).
    - Build sections: tree 120.4, subtabs 21.2, editor body 19.0, rail 18.1, title bar 17.9, URL bar 13.8, response 8.7 (34.6 with JSON), tabs 7.9, footer 6.1, reconcile 22.0/24.3 KB.
    - Unchanged by the key: tree + rail + title bar + response = 165.1 (62 %) / 191.0 KB (65 %).
    - The tree costs ~4.4 KB per request row, so it scales with the collection on every keystroke.
  - expected: −60…−70 % per URL keystroke; scaling (the tree's O(requests) per keystroke goes).
  - cost: medium, app only.

- **id: meridian-history-rows** (6/8, app)
  - what (a): the history list on item rows `(entry, selected)` with the row index in the item.
  - what (b): drop the extra `MarkDirty` after `_historyFind.SetQuery`.
  - evidence:
    - History Down: 353.3 KB/key, render 247–260 KB, 256 elements remade per key (every visible row: one-argument `RenderWith`). `rows.IndexOf(entry)` runs per realized row, an O(n) record-equality scan over up to 10,000 entries (MeridianApp.History.cs:256).
    - History search: `_historyFind = new TextSearch(MarkDirty)` (MeridianApp.cs:164) and `OnTextChanged(v => { SetQuery(v); MarkDirty(); })` (History.cs:188; same at :270). That is 2 builds per typed character, 416.8 KB per character (test log: 10 doubles).
  - expected:
    - (a) about 353 → 110 KB/key (−69 %), rows per key ~30 → 2, and the O(n·rows) `IndexOf` removed.
    - (b) builds per search character 2 → 1, about −84 KB (−20 %).
  - cost: small (rows show relative time, so the shape needs the minute or day boundary).

- **id: apps-vesper-components** (6/8, waiting item; now with sections)
  - what: per-pane header (tab strip, path trail, list header) and footer, the rail, and the chrome with the command line as persistent parts. A cursor key rebuilds only the pane list's selection.
  - evidence: Detail key 244.0 KB (bridge 271.4), 1 whole-window build, 169 patches.
    - Path trails 78.5 KB (39 KB per pane on an 8-folder path), list headers 28.3, tab strips 28.0, rail 14.3, footers 6.2, rest of the build (chrome, command line, root) 34.1, reconcile 22.0, render 13.8, view areas 6.5.
    - About 155 KB (64 %) is pane chrome a cursor key does not change.
    - Brief 230.5 and Thumbnails 305.5 KB carry the same build.
  - expected: −70…−80 % per Detail key; −50…−65 % per Brief/Thumbnails key.
  - cost: medium, app.

- **id: vesper-mark-double-rebuild** (6/8, app; can ride with apps-vesper-components)
  - what: Insert builds the window once, not twice.
  - evidence: `ToggleMark(advance: true)` calls `MoveCursor` (which marks dirty) and then `MarkDirty` (CommanderApp.cs:1305-1306). Insert: 630.0 KB/key with 2 builds; Space: 225.6 KB with 1.
  - expected: Insert about 630 → 405 KB (−36 %).
  - cost: small. The tests show other two-build actions (viewer open `LoadViewer`+`OpenViewer` 57, prompt commits ~30); they are one-off actions and worth folding in the same pass.

- **id: events-handler-churn** (4/1, with 6; re-opens P4's inline-array half with new evidence)
  - what: (a) a patch reuses the live element's handler lists instead of dropping emptied types and making new lists; (b) small inline handler storage with a typed delegate per entry, in place of a `Dictionary<Type, List>` and a per-registration wrapper closure.
  - evidence:
    - Bytes inside `AddHandler` per key: Vesper Detail 24.8 KB (77.5 registrations, ~320 B each), Meridian URL 27.8 (83), Junction summary typing 18.1 (67), Meridian History 22.4 (61), Aperture grid 10.7–14.4.
    - The wrapper is `evt => handler((TEvent)evt)` (Element.cs:920).
    - Patch handler copy (`CopyAppHandlersFrom`: a `List<Type>` of emptied types and a new list per type per patched handled element, Element.cs:1702-1745): Vesper 13.2, Meridian 10.1, Junction 4.3 KB.
    - Together 14–23 % of these keys. P4 was declined on retained memory and ≤5 ns per event, not on rebuild churn.
    - Prototype (a): patch copy 13.2 → 1.9 KB; Vesper Detail 244.0 → 227.2 (−6.9 %), Meridian URL 264.8 → 256.1 (−3.3 %), Junction typing −1.6 %.
  - expected: (a) alone −2…−7 %. With (b), an estimate (not prototyped) of ~320 → 60–100 B per registration gives −11…−16 % per key on these rows, in every app's rebuilds.
  - cost: medium. No API change; `EventHandlerEntry` grows by one field if the typed delegate is stored with a static invoker.

- **id: junction-changes-composer** (6/8, app; continues apps-junction-components)
  - what: the commit composer as its own child component, or the Changes file lists and diff pane as kept parts.
  - evidence: summary keystroke 95.8 KB. Changes column without the composer 26.9, diff pane 23.1, composer 11.2, handlers 22.4, build 6.3, reconcile 5.6. The lists and diff do not change on a keystroke.
  - expected: −60…−70 % per summary keystroke (more with many changed files).
  - cost: medium, app.

- **id: apps-aperture-components** (6/8, after Meridian and Vesper)
  - what: rail and title bar as parts.
  - evidence: grid Right 131.2 / Down 196.2 KB per key; rail 16.8 + title bar 16.5 + their handlers unchanged.
  - expected: −25…−35 % per grid key.
  - cost: medium, app; lowest of the splits.

## Examined, not worth it

- **reconcile-restyle-on-change (b)** (waiting): close it.
  - The style phase allocates 0 B in every walk; resolves per key equal patches + made (Meridian URL 214 = 213 patched + 1, 0 changed).
  - It is time only, in a phase that is 1–2 % of a frame (fbec1130), and it does not change the complexity class (O(patched) is O(build)).
  - The component splits remove the patches themselves (Meridian URL 213 → ~60).
- **Reconcile pooling (P5a) stands:** reconcile bytes per key, with handler copy measured apart, are Vesper 8.8, Meridian 12.0, Junction 5.6, Aperture 2.8 KB (≤5 % of a key).
- **`TryRestoreMoved`'s `before` list and pending-move list:** ≤6 restores per key outside Brief. Covered by the first candidate where it matters.
- **`ReactiveList` copies its subscribers per mutation** (64 B). No app uses it (samples only); could ride along with any reactivity commit.
- **One-off doubles** (one action, not a hot path) belong in the guidelines rather than candidates:
  - Junction: dialog confirm + action, Flash after Copy, repo drag drop, branch-tree Left/Right folder toggle (2 builds per toggle).
  - Meridian: Promote + Flash, method or environment popover pick, Ctrl+F seeding.
  - Vesper: drive picker, goto and mark-pattern prompts.
- **Vesper `FileSystem.FreeLabel`** (`new DriveInfo(...).AvailableFreeSpace`) runs per pane on every build: a disk query, or a network round trip on a share, per key on the UI thread. Under 1 KB per key, so not a bytes candidate; a lead for 8 (cache it per folder, refresh on file operations or focus).
- **Junction History/Branches/Tags/Files keys after the parts** (65–104 KB/key: view build 34–49 KB plus 2–3 rows): nothing new in my area.

## Needs the user

None. Only if the orchestrator prefers the framework route for junction-review-composer-typing: should `RichTextEditorElement` stop raising `SelectionChanged` for the caret move an edit makes, so `DocumentChanged` alone tells an edit? That changes when an app callback runs. I recommend the app-level fix instead.

Guideline material:
- Two `MarkDirty` calls in one handler mean two builds, while signal writes in one event coalesce, and a `MarkDirty` in the same event absorbs them (the forced build snapshots their versions).
- A container whose children are all handed back needs the restore-into-empty fix before the hand-back guideline holds.
