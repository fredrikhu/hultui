# Implementation Plan — Batch 5: Multi-Rename Tool

Responds to `design_handoff_vesper_commander_batch5` (mocks Q–U). Design resolved all six open questions — decisions below are **approved**.

## Approved decisions (README §Recommendations)
1. **Token set (v1 core):** `[N]` `[N a-b]` `[N a-]` `[E]` `[C]` `[C:d]` `[P]` + `[[`/`]]` escapes. Date/time `[Y][M][D][h][m][s]` = v1.1 (dashed "optional" chips, only rendered if a build flag enables them).
2. **Counter** = an inline row in the controls region; dormant at 45% opacity + hint until `[C]` appears in a mask.
3. **Conflicts** hard-disable Rename (45% opacity + footer reason). Unchanged rows skip silently.
4. **Single file** opens the full tool (no special-casing); footer hints Shift+F6 as the fast path.
5. **Changed-portion highlight** = yes, for partial changes only — common prefix/suffix diff tints the changed run `Accent500@13` fill + `Accent200` text; when >~70% of the name differs, the row renders plain.
6. **Undo** = optional v1.1 status-bar toast; needs an engine undo journal → deferred.

**Pipeline order (fixed):** expand tokens → find/replace → case → trim.

## Architecture grounding (current code)
- **Invoked on `OperationTargets(side)`** (marked set, else cursor file). Add `Cmd.MultiRename` = **Ctrl+M** to the rebindable keymap + a palette entry "Multi-rename…" (shows the count).
- **Dialog shell**: reuse `DialogCard(760)` / `Backdrop` (top-aligned) / `DialogFooter`, and the controls — `TextInput`, `Seg`, `Stepper`, `CheckItem`, `GhostButton`/`PrimaryButton`.
- **Preview**: `VirtualListElement` (batches of hundreds).
- **Rename engine**: `FileOps.Rename(entry, newName)` exists for single rename; the batch pipeline + conflict detection + two-phase apply are new (pure, testable).
- **Mask token highlight** is paint-only. `TextInputElement` likely does **not** support `.Spans` (that extension is `where T : TextElement`); the preview highlights via `Text`/`TextArea.Spans`. **Decision (engineering):** render the mask coloring by giving `TextInputElement` a `Spans` pass-through (small framework add, mirrors `TextAreaElement.Spans`) — cleaner than an overlaid ghost Text that must track caret/scroll. Fall back to an overlay only if that proves fiddly.

---

## P0 — Rename engine (pure, unit-tested)
`RenameEngine.cs` — no UI. The heart; everything else renders its output.
- **Token expansion** `Expand(mask, ctx)` where `ctx` = original name (sans ext), extension, parent-folder name, counter value. Grammar: `[N]`, `[N a-b]` / `[N a-]` (1-based char ranges, clamped), `[E]`, `[C]` (zero-padded to the counter's digits), `[C:d]` (inline pad override), `[P]`, `[[`/`]]` → literal brackets. Case-insensitive token names, canonicalized. Malformed `[` with no close → treated as literal (no error).
- **Pipeline** `NewName(file, index, opts)` = expand name-mask + expand ext-mask (empty ext-mask ⇒ keep original ext; a literal/token replaces it), then **find/replace** (all occurrences, case-insensitive unless MatchCase; regex when enabled), then **case** (As-is/UPPER/lower/Title/First-cap — name only, never the extension), then **trim** (leading/trailing, collapse doubles).
- **Counter** = start (≥0) + step (≠0) + digits (1–8), advancing per file in list order.
- **Conflict classification** per result: `Ok`, `Unchanged` (== original), `Collision` (two results equal, case-insensitive on Windows), `Exists` (target already on disk and not itself), `Illegal` (invalid path chars), `Empty`, `TooLong`. A malformed **regex** invalidates the whole batch (engine returns the error + position; the last valid mapping is what the UI holds).
- **Two-phase apply** `Apply(mappings)` — when a batch permutes names (a↔b), rename via unique temp names first, then to targets, so no intermediate collision. Returns per-item success/failure (verbatim OS reason). Completed renames are **never** rolled back.
- **Tests** (`RenameEngineTests`): token expansion table (ranges, escapes, `[C:d]`), pipeline order, ext-mask keep-vs-replace, counter padding/step, case + trim, each conflict class, swap via two-phase, regex + malformed-regex.

## P1 — Dialog shell + masks + token palette + live preview
The workbench (Mock Q/R/T), wired to the engine live.
- **`CommanderApp.Rename.cs`** — `OpenRename()` (captures `OperationTargets`), state (masks, find/replace, opts, counter, selection), `BuildRename()`.
- **Shell**: Card 760, header (pencil `Accent400` · "Multi-rename N files · path" · Ctrl+M chip · ✕); regions = fixed controls / preview fills (min 200) / summary bar / footer. Footer: Reset (ghost) · Cancel · "Rename N files" (accent-outline, never filled).
- **Masks**: two `TextInput` (h30, mono 12), flex 1.7:0.7; ext-mask placeholder "[E] · keep" (Neutral600, never blank). Live token highlight — literal `Text`, token `Accent200` (via the `Spans` add).
- **Token palette**: core chips (`[N]` name · `[N2-5]` range · `[E]` ext · `[C]` counter · `[P]` parent), 10.5 mono Accent200, border Neutral700, fill Accent500@7 / hover @14; click / ↵ inserts at the mask caret and keeps focus; tooltip = token + meaning + a live example from the first target.
- **Preview**: `VirtualListElement`, row grid `[old 1fr | → 16 | new 1.3fr | reason auto]`, mono 11.5, old On(0.45), new Text, arrow Neutral600. Re-previews on any edit (debounced ~80ms).
- **Live counts** in the summary + the primary label.
- **Focus**: name-mask autofocus; Esc cancels; ↵ = Rename when enabled.

## P2 — Counter · find/replace · case & trim
The remaining controls (Mock S).
- **Counter** inline row: Steppers (start / step / digits) + a live sample ("0010 · 0020 · 0030 · …", first in Accent200). Dormant (45% + "insert [C] into a mask to enable") when no `[C]` in either mask.
- **Find / Replace**: two `TextInput` (h26) + `Aa` (match-case) and `.*` (regex) toggles (26×26, on = Accent500@14 + Accent600 border). Malformed regex → input border `MarkRed@55` + reason (`⚠` + text, mono MarkRed 10.5); preview dims to 55% and holds the last valid mapping; **Rename disables**.
- **Case** = 5-way `Seg` (As-is/UPPER/lower/Title/First cap), name only. **Trim** = two `CheckItem` (leading/trailing · collapse doubles), runs last.

## P3 — Preview highlight, conflicts, summary, states
Mock T/U's trust model.
- **Diff-run highlight** — for partial changes, tint the changed run (common prefix/suffix diff, reuse `DiffEngine.IntraSpan`) `Accent500@13` + `Accent200`; skip when >~70% of the name differs (plain).
- **Conflict rows** — `MarkRed@6` wash, new name MarkRed + `⚠` + short reason ("duplicate result" · "already exists on disk" · "illegal character x" · "empty name" · "too long"). **Unchanged** rows at 50% + "unchanged · skipped".
- **Summary bar** (sticky, above footer): "N will rename · M conflicts · K unchanged" (green `#3eb37e`, conflicts MarkRed), + "rows a–b of N" when scrolled.
- **Rename gating**: hard-disabled (45%) while any conflict/invalid-regex remains; footer reason ("resolve N conflicts to rename" / "every result collides — …" / "nothing to rename — …").
- **States**: empty (all dim, no red), all-conflicts (fix hint), single-file (footer Shift+F6 hint).

## P4 — Apply, partial failure, tests
- **Apply**: full success → close + a green shell status-bar line. **Partial failure** → dialog stays open, banner (`MarkRed@7`) with counts, list **filters to failed rows** (link restores all), per-row OS reason; footer "Retry N failed" (re-runs only the failed subset). Completed renames never roll back.
- **Refresh** the pane(s) after apply; land the cursor sensibly.
- **Tests** round out the engine coverage; headless screenshots (clean / conflicts / counter / regex-error / states).
- **Deferred (v1.1):** undo journal + status-bar "Undo Ctrl+Z" toast; date/time tokens.

---

## Sequencing
Five phases, each buildable + verifiable; one commit per phase. **P0 first** (pure engine, fully testable) de-risks everything; P1 makes it visible; P2–P3 complete the controls + trust model; P4 applies safely. Undo and date/time tokens are explicitly v1.1.

## Open choices for you
1. **Build all five phases** straight through, or land **P0 + P1** (engine + workbench with live preview) first for a look before the rest?
2. **Mask highlight** — add `TextInputElement.Spans` (small framework pass-through, recommended) vs an overlaid ghost-Text. (I'll default to the framework add unless you object.)
