# Vesper Commander — Design Handover (Batch 1)

**For:** Claude Design
**From:** implementation
**Date:** 2026-08-02
**Scope:** Visual/UX design for the next batch of file-manager features, *before* implementation starts.

---

## 0. What this is

We agreed on five additions. Three need real UX design before we can build them; two are mostly plumbing and need only a small visual decision. This document briefs **only the parts that need design**. Everything here must fit the existing **Nocturne** design system and reuse the components already built — do not invent a new visual language.

**Deliverable from Design:** static mockups (HTML/CSS in the Nocturne idiom, matching the existing `styles.css` handoff), one per surface below, showing every listed state. Annotate spacing, color roles (by token name), and interaction affordances. No new colors outside the ramps unless justified.

| # | Feature | Design load | Section |
|---|---------|-------------|---------|
| 1 | Navigation history (back/forward) | **Light** | §A |
| 2 | Column-header sorting | **Light** | §B |
| 3 | OS clipboard (cut/copy/paste) | **Minimal** (one state) | §C |
| 4 | Find files | **Heavy** | §D |
| 5 | Diff view (F3) | **Heavy** | §E |
| 6 | Sync / compare directories (F9) | **Heavy** | §F |

(F3 diff and F9 sync are already advertised in the Options shortcut list but were never built — they read as broken today. Both need a view designed.)

---

## 1. Design system quick-reference (Nocturne)

Source of truth: `apps/VesperCommander/Theme/Nocturne.cs`. Dark theme only — **do not design a light variant**.

**Surfaces**
- App ground `Bg #161826`
- Raised (bars, menus, palette, cards) `Surface #232532`
- Card body uses `Bg` with a `Neutral600` 1px border (see Card component §2)
- Overlay backdrop: `#05060c` at **62%** alpha, click-to-dismiss

**Text**
- Primary `Text #e9e9ed`
- Muted steps via opacity: `On(0.60)` secondary, `On(0.45)` metadata, `On(0.42)` placeholder, `On(0.30–0.35)` disabled
- Directory rows render in `Accent200`
- Marked (selected-for-op) files: `MarkRed #e0685f`, dimmed metadata `MarkRedDim #a95349` — **not theme-configurable**

**Accent** — single blurple `#9184d9`, mutable at runtime (4 choices). Use the ramp, not literals:
- `Accent200/300` bright labels, `Accent500` fills/rings at low alpha, `Accent600` borders
- Cursor row today: `Accent500 @13%` fill + `Accent600` 1px border

**Neutral ramp** `Neutral100`(lightest)→`Neutral900`(darkest). Hairline divider = `Text @16%`.

**Radii** `Sm 4 · Md 8 · Lg 14 · Row 6 · Window 12`

**Type** — UI text 12–13.5px. Monospace (`Ui.Mono`) for paths, sizes, drive letters, counts — use it for anything tabular/numeric.

**Density** — rows are 20px (Compact) or 24px (Comfortable). Any new list must honor both.

---

## 2. Components already built — reuse these

Design should compose from these, not redraw them:

- **`Backdrop(card, topAligned)`** — full-shell dim + centered (or top-aligned at 70px) card that stops click propagation. All modal surfaces use it.
- **`Card(width, title, subtitle)`** — bordered/rounded/shadowed panel with a header row: title (13px medium) + optional mono subtitle (11px, `On(0.45)`) + ✕ close. Shadow `0 16 40 rgba(0,0,0,0.65)`. Existing widths: drive picker **420**, palette **520**, bookmark manager, confirm/overwrite.
- **List row grid** — file lists use grid columns `icon 16px / name 1fr / size 60px / modified 116px`, gap 10, row height = density. Header row exists (`BuildListHeader`) but headers are currently inert.
- **Footer** — files/dirs counts + free space + marked count, mono.
- **Breadcrumb bar** — clickable path segments, left-truncates whole folders with a "…" crumb; leading drive crumb opens the drive picker.
- **Progress overlay** — dual bars, per-item queue, rate/ETA, Pause/Skip/Background/Cancel. Reuse its bar + button styling for any long operation (relevant to Sync §F).
- **Confirm / Overwrite dialogs** — for yes/no and source-vs-target compare. The Overwrite dialog's two-column Source/Target compare is a strong precedent for Diff/Sync headers.

---

## A. Navigation history (back/forward) — *light*

**Need:** a back/forward affordance per pane, plus an optional history dropdown. Today the only "back" is Backspace (up a level); there is no way to return to a sibling folder you just left.

**Design the:**
1. **Placement** of Back / Forward controls. Candidate: left end of each pane's breadcrumb bar, before the path. They must read as per-pane (two panes on screen).
2. **Three states each:** enabled, disabled (no history in that direction — dim to `On(0.30)`), hover.
3. **History dropdown** (optional, if you think it earns its place): a small list of recent locations for that pane, opened from a caret next to Back. Reuse context-menu styling (`Surface` bg, `Neutral700` border, `Neutral900` hover).

**Constraints:** icons only (no text labels) to save breadcrumb width; must survive the breadcrumb's truncation behavior; keyboard equivalents will be Alt+Left / Alt+Right (show in dropdown/tooltip if you spec one).

**Open questions for Design:** Do back/forward belong in the breadcrumb or a separate slim toolbar row? Is the dropdown worth it, or do Alt+arrows suffice for v1?

---

## B. Column-header sorting — *light*

**Need:** make the existing header row (Name / Size / Modified) clickable to sort, with a direction indicator. Sort is currently buried in Options.

**Design the:**
1. **Sort indicator** — an up/down glyph on the active column. Size/weight/position within the header cell.
2. **Active vs inactive header** — the sorted column's label should stand out (candidate: `Text` full-strength + accent glyph; inactive at `On(0.55)`).
3. **Hover** affordance on a sortable header (it must read as clickable — the rest of the list is not).
4. **Icon and Name** columns: Name is sortable; the icon column is not. Confirm Size and Modified header alignment with their right/left-aligned data.

**Constraints:** header row height matches the list header today; must work at both densities; indicator must be legible against `Surface`.

---

## C. Clipboard cut/copy/paste — *minimal*

**Mostly engineering** (wire Ctrl+C/X/V to the OS clipboard). The **one design decision**: how a **"cut" (pending-move) file looks** in the list — the convention is a dimmed/ghosted row until paste or Esc.

**Design the:** the cut-pending row treatment (e.g. name + icon at ~50% opacity). It must be visually distinct from a **marked** row (`MarkRed`) and from the **cursor** row (accent ring). Show all three together in one mock so they don't collide.

No dialogs, no toolbar. If you think a brief "N items copied" status hint is warranted, propose where (footer?), but keep it optional.

---

## D. Find files — *heavy*

**Need:** find files by name (and later, content) under the active pane's folder. Today quick-search only prefix-matches the visible listing.

**Design the surface.** Decide and mock **one** of:
- **(a) Docked filter bar** — a slim input above/below the list that filters the current listing live (fast, in-place, no results view). Best for "narrow this folder."
- **(b) Modal find dialog** (`Backdrop` + `Card`, ~560 wide) — pattern field, scope (this folder / recursive), options (match case, regex, hidden), and a **results list** reusing the file-row grid with a path column. Best for "search the tree."

Recommendation: spec **both roles** but they can share one surface (a filter bar that expands into recursive results). Your call — justify it.

**States to show:** empty query (idle), typing/searching (in-progress indicator — reuse the "measuring…" spinner idiom the folder-size feature uses), results found, **no matches**, and error (path denied). Results rows need: icon, name (with matched substring emphasized?), parent path (mono, `On(0.45)`), size/date. Selecting a result should conceptually jump the pane there — show the selected/cursor state.

**Constraints:** must not block the UI (search is async); results reuse the list grid + density; scope controls should feel like the existing Options controls (segmented / checkbox styling from Nocturne `ToHultTheme`).

**Open questions:** Docked vs modal vs hybrid? Do we show results in-pane (replacing the listing) or in an overlay? How is "clear search / back to folder" expressed?

---

## E. Diff view (F3) — *heavy*

**Need:** compare two text files (typically the file under the cursor in each pane, or two marked files) side by side. `DiffTool` setting already exists (Builtin/VSCode/Junction) — this designs the **Builtin** view.

**Design the:**
1. **Layout** — two-pane side-by-side text, each with a gutter (line numbers, mono). A header identifying left vs right file (reuse the Overwrite dialog's two-column Source/Target header idiom, with mono paths).
2. **Change colors** — added / removed / changed line backgrounds and inline (intra-line) highlights. Propose sRGB values in the Nocturne register: a green and a red that harmonize with `MarkRed`/accent (the theme already has a preview "string green" and `MarkRed`). Keep them muted (line-background tints, not saturated blocks).
3. **Sync-scrolled** panes, a change-count summary, and prev/next-change navigation affordance.
4. **States:** files identical (empty-diff message), binary/too-large (can't diff), one side missing.

**Is it a full-screen view or a large overlay?** Recommend: fills the shell (like Preview "Full" mode does today) rather than a modal card, because diffs need width. Confirm and mock the chrome (how you exit back to the panes — Esc / a close affordance).

**Constraints:** read-only in v1 (no editing/merging); mono throughout; must honor density for line height; reuse the preview's TextArea scroll/selection behavior.

---

## F. Sync / compare directories (F9) — *heavy*

**Need:** compare the left and right pane folders and show what differs, with the ability to copy the deltas in either direction. This is the marquee Total-Commander feature and the biggest design piece.

**Design the:**
1. **Compare view** — a two-column listing (left folder vs right folder) aligned row-by-row on matching names, with a **center status column** showing the relationship per row: only-left `←`, only-right `→`, newer-left, newer-right, identical (hidden by default), differ. Use accent for directional arrows, `MarkRed`/green sparingly.
2. **Filters/toggles** — show/hide equal files, by-content vs by-date+size compare, recurse subfolders. Match Nocturne segmented/checkbox styling.
3. **Per-row action + direction** — how the user chooses "copy left→right" for a row or for all. A default proposed direction per row, overridable.
4. **Summary + commit** — counts (N to copy →, M to copy ←, K conflicts) and a confirm that hands off to the **existing progress overlay** for the actual transfer. Design the pre-flight summary; the transfer UI already exists — reuse it.
5. **States:** comparing (async, in-progress), folders identical, conflicts present, one side unreadable.

**Full-shell view**, not a card — it needs the full width of both panes. Mock the header (both folder paths, mono), the aligned list, the center status rail, the toolbar of toggles, and the commit bar.

**Constraints:** builds on the same file-row grid; the actual copy reuses `CopyStreamed` + the progress overlay, so the "run" step is solved — design stops at the pre-flight summary handoff.

---

## 3. Cross-cutting requirements (apply to every surface)

- **Dark only**, Nocturne tokens by name — no ad-hoc hex except the two justified diff/sync change colors, which you should still express as named additions.
- **Two densities** (20 / 24px rows) for anything list-shaped.
- **Every surface needs:** idle/empty, in-progress (async), success, no-result, and error states. We ship the states you draw — omissions become blank screens.
- **Keyboard-first.** Show the primary key hint where natural (headers, buttons). Bindings: Alt+←/→ history, click headers to sort, Ctrl+C/X/V clipboard, a find key (propose one — Ctrl+F or Alt+F7), F3 diff, F9 sync.
- **Reuse `Backdrop`/`Card`** for modal surfaces; **full-shell** for Diff and Sync.
- **Accessibility:** the app has a live UIA/AT-SPI bridge — every control must have a clear text label; don't rely on color alone for diff/sync status (pair arrows/glyphs with the color).

## 4. Explicitly out of scope for Design (engineering-only)

- Clipboard plumbing, history stack data structure, sort comparators, async search/compare engines.
- Making Move off-thread, wiring the phantom Ctrl+R/H/U/`=` shortcuts to real keys.
- These need no mockups — noted so Design doesn't spend time on them.

## 5. Open questions to resolve with the requester before/while designing

1. History: breadcrumb-embedded controls vs a dedicated slim toolbar row?
2. Find: docked filter bar, modal dialog, or hybrid — and in-pane vs overlay results?
3. Diff & Sync: confirm full-shell (recommended) vs large overlay.
4. Diff/Sync change colors: approve a green+red pair in the Nocturne register.
5. Find key binding: Ctrl+F vs Alt+F7 (Total Commander uses Alt+F7).
