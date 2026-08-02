# Vesper Commander — Batch 2 Implementation Plan

**Responds to:** `DESIGN-HANDOVER.md` (brief) + Claude Design's batch-2 mocks (six surfaces).
**Date:** 2026-08-02

## Locked decisions

- **Diff/Sync colors — Vesper-tuned.** Add a `DiffGreen ≈ #3eb37e` scaled to Vesper's ground; deletions reuse existing `MarkRed #e0685f`. Junction (`GitClientColors.cs`) is a *structural* reference (which roles to define), not literal values — do **not** import its `#4ade80/#f87171`.
- **Archives — deferred.** Find and Sync operate on the real filesystem only. The mocks' "Archives" checkbox is hidden/disabled this batch. Leaves the dead `Archiver` setting untouched.
- **Order — all six, sequenced light→heavy** (below). Sort + history land first (reuse existing components), then clipboard, then the three new async engines.

## Key bindings added this batch

`Alt+←` / `Alt+→` history back/forward · click list headers to sort · `Ctrl+C`/`Ctrl+X`/`Ctrl+V` clipboard · `Alt+F7` find · `F3` diff · `F9` sync. All must skip when a TextInput/TextArea is focused (`typing` guard already exists in `HandleKeyDown`).

---

## Phase 0 — Shared foundation (do first)

**Theme tokens — `Theme/Nocturne.cs`** (new `// ── Diff / sync ──` block):
```
DiffGreen     = Hex("#3eb37e")           // add markers / new-file rail
DiffGreenText = Hex("#7fd0a5")           // added-line text
DiffAddBg     => Alpha(DiffGreen, 0.10)
DiffDelBg     => Alpha(MarkRed,   0.22)
DiffDelText   => MarkRed
DiffChangeBg  => Alpha(Accent500, 0.09)
DiffChangeMark=> Alpha(Accent500, 0.28)  // intra-line highlight
```
Rail/status glyph colors reuse `Accent300` (directional), `DiffGreen` (new), `MarkRed` (conflict), `Neutral600` (skip), `Neutral500` (equal). Alignment-gap hatch = `Neutral900` (drawn in code).

**Rationale:** every heavy feature reads these; landing them once avoids churn. No behavior change until consumed.

---

## Phase 1 — Column-header sorting (light)

**Design:** Mock B. Click header cycles asc→desc on that column; a new column starts Name-asc, Size/Modified-desc. Active label at `Text` + 8×8 accent triangle 4px after; inactive `Neutral600`; hover `Neutral300` + 45% ghost triangle. Icon column not sortable. Dirs always grouped first.

**Sub-decision (recommended): keep sort global.** `Settings.Sort` is a single value today. Add `public bool SortDescending;` to `Settings.cs`. Header clicks set both. *Per-pane sort is deferred* — matches current architecture, avoids threading sort state through `PaneTab`.

**Changes:**
- `Settings.cs` — add `SortDescending` field (JSON auto-picks it up via `IncludeFields`).
- `FileSystem.List` — honor descending (reverse the comparator tail; dirs-first grouping unchanged).
- `CommanderApp.Pane.cs` `BuildListHeader` (line 317) — make Name/Size/Modified cells clickable: `OnClick` → `SetSort(column)`; render active triangle / inactive / hover per spec. Add `SortGlyph` helper.
- `CommanderApp.cs` — `SetSort(SortMode col)`: same column → flip `SortDescending`; new column → set default direction (Name asc, else desc), `BumpGen` both panes, persist, `MarkDirty`.

**States:** active-asc, active-desc, inactive, hover (ghost triangle). Both densities (header padding unchanged).

**Tests:** `vshot` KEYS to click a header (needs a header-hit env or reuse `CLICK` by header text) + assert row order via focused-listing dump; unit test `FileSystem.List` ascending vs descending for each `SortMode`.

**Risk:** low. Pure reuse of the existing header row.

---

## Phase 2 — Navigation history (light)

**Design:** Mock A. Per-pane back/caret/forward cluster, right-aligned in the **tabs row** (`margin-left:auto`). Enabled `Neutral400`; hover `Neutral900` fill + `Accent300` glyph; disabled `Neutral700`, no hover. Dropdown (caret / `Alt+↓` / 400ms long-press) = `Surface` card, width 300, rows 26px, current path `Accent200` + dot, older `On(0.60)→On(0.45)`, mono paths left-truncated.

**State model — per tab.** Add to `PaneTab` (line 48):
```
public readonly List<string> Back = new();
public readonly List<string> Fwd  = new();
```
History is per-tab so switching tabs restores that tab's history (matches how `Cursor`/`Marks` already live on `PaneTab`).

**Choke point — `NavigateTo` (line 400).** Push the *current* `Dir` onto `Back` and clear `Fwd` before reassigning `Dir` — **but only for user navigation**, not history replay. Add a `bool recordHistory = true` param; `GoBack`/`GoForward` call with `false`. Cap `Back` at ~50.

**Changes:**
- `CommanderApp.cs` — `NavigateTo(..., bool recordHistory = true)`; `GoBack(side)`/`GoForward(side)` (pop/swap between stacks, navigate with `recordHistory:false`); `CanGoBack/CanGoForward`.
- `HandleKeyDown` — `Alt+Left`→GoBack, `Alt+Right`→GoForward, `Alt+Down`→open history dropdown (when cluster focused/hovered pane).
- `CommanderApp.Pane.cs` `BuildTabStrip` (line 138) — after the tab chips + `+`, add `Box.New().FlexGrow(1)` spacer then the history cluster (Back 26×24 · caret 16×24 · Forward 26×24). Each: enabled/hover/disabled per spec, `Tooltip("Back · Alt+←")`.
- New `HistoryDropdown` overlay — reuse context-menu styling (or `Card` idiom); a keyed overlay like the others, opened per side. Add `_historyOpen`/`_historySide` state + a `HandleHistoryKey` (Esc/Up/Down/Enter) mirroring `HandleBookmarksKey`.

**A11y:** "Back to C:\dev\vesper", "Forward (unavailable)"; dropdown `role=listbox`.

**Tests:** `vshot` — navigate down two folders (`dd e`), `Alt+←` twice, assert `Dir` restored; assert Back/Fwd disabled at ends.

**Risk:** low-medium. The only subtlety is *not* recording history on replay and on `GoUp` vs descend (both should record — they're user nav). Verify `GoUp`, `DescendCursor`, breadcrumb clicks, bookmark jumps, drive select all route through `NavigateTo` so history is uniform. (If any set `Dir` directly, reroute them.)

---

## Phase 3 — Clipboard cut / copy / paste (medium)

**Design:** Mock C. Cut rows ghost to **45%** (icon+name), size/date → `Neutral700`; distinct from `MarkRed` mark and the cursor ring (ring paints full-strength over ghosted content). Footer: `✂ N cut — Ctrl+V moves · Esc cancels` in `Accent300`. `Esc` or a new cut/copy clears pending; paste into the same folder is a no-op.

**Clipboard semantics.** Real OS clipboard so it interops with Explorer:
- **Copy/Cut** — put the selected files' paths on the clipboard. Windows uses `CF_HDROP` + the `Preferred DropEffect` format (Copy=5 / Move=2) so Explorer honors cut/paste. Non-Windows: fall back to newline path list (interop best-effort).
- **Paste** — read `CF_HDROP` from the clipboard into the active folder; Move if DropEffect=Move (then clear source clipboard), else Copy. Reuses `RunCopy` (F5 path) and `ProcessMove`.

**Changes:**
- New `Clipboard.cs` (VesperCommander) — `SetFiles(paths, cut:bool)`, `TryGetFiles(out paths, out cut)`. Wrap Win32 clipboard (the framework may expose text-only `Application.Clipboard` — check; likely need P/Invoke `OleSetClipboard`/`CF_HDROP`, guarded to Windows).
- `CommanderApp.cs` — `_cutPending` HashSet<string> (for the ghost render, cleared on paste/Esc/new-op); `CopyToClipboard(cut:bool)`, `PasteFromClipboard()`; `HandleKeyDown` `Ctrl+C/X/V`.
- `CommanderApp.Pane.cs` `FileRow` (line 366) — if `_cutPending.Contains(e.Name)`, render icon+name at 45% and metadata at `Neutral700`; ensure the cursor ring still paints over it.
- `BuildFooter` — cut counter (`Accent300`), left of the mark counter; optional "N items copied" hint that fades (4s) — a low-priority polish, can ship after.

**Sub-decision (recommended):** ship the **ghost + footer + move/copy** first; the fading "copied" toast is optional follow-up (it needs a timer/animation and adds little).

**Tests:** `Clipboard.cs` is Windows-P/Invoke — hard to unit test headlessly. Test the *reducer* (`_cutPending` set/clear on X/Esc/paste) via `vshot` (Ctrl+X marks ghost, Esc clears). Manual verify Explorer interop (like F4 launch, `Process`/OLE can't run headless).

**Risk:** medium — the P/Invoke `CF_HDROP` + DropEffect is the fiddly part. Isolate it behind `Clipboard.cs` so the rest is testable.

---

## Phase 4 — Find files (heavy)

**Design:** Mock D. Modal `Card 560`, top-aligned 70px, `Alt+F7`, scoped to active pane's folder. Pattern input + optional "Containing text" + scope segmented [This folder | Recursive] + checkboxes Match case / Regex / Hidden (**Archives hidden this batch**). Results reuse pane grid `16/1fr/60/116` + inline parent path (mono `On(0.45)`), match substring in `Accent200`. Async, streaming, footer progress. States: idle, searching, results, no-match ("Search recursively ↵"), error (denied banner `DiffRed@12%` + `MarkRed` + lock, results stay usable). Keys: `↵` jump active pane + close · `Ctrl+↵` other pane · `Esc` close/stop.

**Engine — `FindEngine.cs` (new).** Async directory walk on a background thread (like `CopyStreamed`), streaming matches back via a callback marshalled to the UI (mirror how `_dirSizes` measurement posts back). Name match: glob (translate `*?` to regex) or literal substring or user regex; optional content grep (open file, honor binary-detect from the preview's NUL heuristic, cap size). Cancellable via a token. Reports `(scanned, ~total, matches[])` for the footer.

**Changes:**
- `FindEngine.cs` — pure-ish walker + matcher; takes options, emits results incrementally + progress; respects Hidden filter and cancellation.
- `CommanderApp.cs` — find state (`_findOpen`, query, options, results, progress, cancel token, selection index) + `OpenFind`/`RunFind`/`JumpToResult`; `HandleFindKey` (typing filters shallow live; Enter escalates to recursive; ↵/Ctrl+↵/Esc). `Alt+F7` in `HandleKeyDown`.
- `CommanderApp.Overlays.cs` — `BuildFind()` overlay (Backdrop + Card 560): inputs, segmented, checkboxes (reuse Nocturne `ToHultTheme` checkbox/segmented idiom from Options), results list reusing the file-row grid + parent-path column + match emphasis, footer progress (accent-arc spinner like folder-size "measuring…"), denied banner.
- `JumpToResult` → `NavigateTo(side, parentDir, focusChildName: name)` (records history — nice interplay with Phase 2), close find.

**States:** all five in the mock. `role=listbox`, live-region match count, assertive denied banner.

**Tests:** unit-test `FindEngine` matching (glob/regex/substring/case/hidden) over a temp tree; `vshot` — `Alt+F7`, type a pattern, assert results count / selection; no-match + denied branches.

**Risk:** medium-high — async streaming into a rebuilding overlay (watch the VirtualList fresh-snapshot rule from memory: results list must get a new reference each update or rows go blank). Cancellation on Esc/close must stop the walker.

---

## Phase 5 — Diff view F3 (heavy)

**Design:** Mock E. **Full-shell** (like Preview "Full"); `←`/`Esc` return. Header: filename, count pills (+adds `DiffGreen` / −dels `MarkRed` / ~changes accent), change-nav `n`/`p` + arrows, "Open in VS Code". Sub-header: two-column Left/Right (Overwrite-dialog idiom, mono path + size/date). Body: side-by-side, 34px right-aligned line gutter (`Neutral600` 10.5) + 12px marker column (+/−/~ **always paired with color**), add/del/changed line bgs (Phase-0 tokens), intra-line marks, unchanged runs collapse ("⋯ N unchanged lines · click to expand ⋯"), synced scroll, line height = density, read-only v1. States: identical, binary/too-large, one-side missing (+ "Copy left → right F5").

**Diff algorithm — `DiffEngine.cs` (new).** Line-level LCS/Myers diff (standard), producing a list of `(Op Add|Del|Change|Equal, leftLine?, rightLine?)`. Intra-line: token/char diff on Change pairs for the highlight spans. Collapse equal runs > N (e.g. 6) into a foldable marker. Binary/too-large guard up front.

**Diff source.** F3 on the cursor file: compare pane's cursor file vs the *other pane's same-named file* (TC behavior); if two files are marked, diff those. Resolve target, handle missing side.

**Changes:**
- `DiffEngine.cs` — Myers line diff + intra-line + run-collapse; `DiffResult` model.
- `CommanderApp.cs` — `_diffOpen` + operands + `OpenDiff(side)` (resolve L/R, guard binary/missing) + change-nav index + `HandleDiffKey` (n/p/Esc). `F3` in `HandleKeyDown`. Respect `DiffTool` setting: Builtin → this view; VSCode/Junction → launch external (wire the currently-dead setting).
- New `CommanderApp.Diff.cs` (partial) — `BuildDiff()` full-shell view: header pills + nav, two-column sub-header, two synced scroll bodies reusing the read-only selectable TextArea idiom (gutter + marker + tinted lines), collapse markers, the three edge states. Reuse `SyntaxHighlight` for token color under the diff tint if cheap; otherwise plain mono v1.

**Tests:** unit-test `DiffEngine` (add/del/change/equal, intra-line spans, run-collapse, identical, binary) over string pairs — high value, fully headless. `vshot` — `F3` on a known pair, screenshot; identical + missing-side branches.

**Risk:** medium — the diff engine is well-trodden and unit-testable; the view is a big build but reuses TextArea scroll/selection and the Overwrite two-column idiom.

---

## Phase 6 — Sync directories F9 (heavy)

**Design:** Mock F. **Full-shell**; `←`/`Esc` return. Header "Synchronize" + both paths. Toolbar: segmented [Date+size | Content], checkboxes Recurse / Show-equal (**no Archives this batch**). Grid `1fr/56/104/34/104/56/1fr` mirrored, center rail 34px. Rail glyphs (glyph+color, never color alone): `→`/`←` `Accent300` proposed copy (newer wins) · `+→`/`←+` `DiffGreen` new file (other side "— missing —" italic) · `≠` `MarkRed` conflict (row bg `Alpha(MarkRed,0.08)`, no default) · `∅` `Neutral600` skip · `=` `Neutral500` @55% (hidden unless Show-equal). Interactions: click rail cycles →←∅ · Space marks · Ctrl+click all-visible · `F3` diffs row. Commit bar: colored counts + primary "Synchronize N files… ↵" (accent **outline**, never fill); conflicts excluded from N. Commit → pre-flight confirm (`Card 440`: per-direction counts + total bytes) → **existing progress overlay** (`CopyStreamed`). States: comparing (striped bar, "N of M files", stream in, Esc cancels), identical, one-side unreadable.

**Compare engine — `SyncEngine.cs` (new).** Async walk of both trees (recurse optional), align by relative path, classify each: only-left / only-right / newer-left / newer-right / equal / conflict (differ, same mtime, or by-content mismatch). By-content mode hashes/compares bytes. Streams rows + progress, cancellable. **The transfer is already solved** — the plan's new code stops at building the per-row action set; commit maps rows→copy ops and calls the existing `RunCopy`/`CopyStreamed` + progress overlay.

**Changes:**
- `SyncEngine.cs` — dual-tree align + classify + default-direction rule (newer wins; equal skipped; conflict none); streaming + progress + cancel.
- `CommanderApp.cs` — `_syncOpen` + rows + toolbar options + selection/`F9` `OpenSync()` + rail-cycle / Space / Ctrl+click reducers + `HandleSyncKey`. Commit → `BuildSyncPreflight` (Card 440) → hand rows to the copy pipeline.
- New `CommanderApp.Sync.cs` (partial) — `BuildSync()` full-shell (mirrored grid + center rail + toolbar + commit bar) and `BuildSyncPreflight()`. Row `F3` reuses Phase-5 `OpenDiff` on the row pair.

**Tests:** unit-test `SyncEngine` classification (each relationship, by-date vs by-content, recurse) over temp trees — high value. `vshot` — `F9`, screenshot compare list; rail-cycle changes a row's direction; pre-flight counts. Commit → progress overlay is already covered by existing copy tests.

**Risk:** high (biggest surface) but **de-risked**: classification is unit-testable, and the transfer half already exists. Build the engine + view + pre-flight; reuse everything downstream.

---

## Cross-cutting

- **Density** (20/24) for every new list (find results, diff lines, sync rows) — read `RowHeight`.
- **A11y** — the UIA/AT-SPI bridge is live; label every control, never rely on color alone (diff markers + sync rail already pair glyph+color per design).
- **VirtualList gotcha** (memory `hultui-virtuallist-gotcha`) — find/diff/sync result lists must hand a fresh snapshot reference each update or rows blank out.
- **`typing` guard** — every new global key (Alt+F7, F3, F9, Alt+arrows, Ctrl+C/X/V) must respect the existing focused-input check in `HandleKeyDown`.
- **Not in scope, noted:** wiring the phantom palette-only shortcuts (Ctrl+R/H/U/`=`) to real keys; archive VFS; per-pane sort; the fading "copied" toast (optional Phase-3 tail).

## Suggested commit sequence

1. `Phase 0` theme tokens (tiny, standalone).
2. `Phase 1` sort · 3. `Phase 2` history · 4. `Phase 3` clipboard — three light/medium commits, each shippable.
5. `Phase 4` find (engine commit, then overlay commit).
6. `Phase 5` diff (engine + unit tests, then view).
7. `Phase 6` sync (engine + unit tests, then view + pre-flight).

Each phase: build the library/engine with unit tests first where one exists (sort, find, diff, sync engines are all headless-testable), then the view, then a `vshot` screenshot pass. Confirm with you between the light batch (1–3) and each heavy feature (4, 5, 6).
