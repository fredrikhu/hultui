# Vesper Commander — Design Brief: View Modes

**For:** Claude Design
**From:** implementation
**Date:** 2026-08-05
**Scope:** UX/visual design for alternate pane view modes (Brief, Thumbnails) alongside today's Detail view — *before* implementation.

---

## 0. What this is

Vesper is a Total Commander-style dual-pane file manager (HultUI/.NET, **Nocturne** dark theme, source of truth `apps/VesperCommander/Theme/Nocturne.cs`). Today each pane shows a single **Detail** view — a virtualized row list with columns (icon · name · size · modified, plus ratio % inside archives). This brief adds two more per-pane view modes:

- **Brief** — names only, in multiple columns (more files on screen at once).
- **Thumbnails** — a tile grid; images render as thumbnails, everything else as a large icon + name.

This brief covers only what needs **design**. The thumbnail decode/cache, the grid/flow virtualization, and the view-mode plumbing are engineering (listed out-of-scope in §6).

**Deliverable from Design:** high-fidelity static mockups (HTML/CSS in the Nocturne idiom, matching the existing handoff `_ds` bundle), one per surface below, showing **every listed state**, with token names / spacing / interaction annotated in a right-hand spec column — same format as the batch-2/3 mocks. Dark theme only.

**What already exists to build on:** the Detail list (virtualized rows, cursor ring = `Accent500 @13%` fill + `Accent600` border, marked = `MarkRed` text/icon, cut = 45% ghost); real **image decoding** (`SkiaImageSource.FromFile`, off-thread, used by the preview panel today — reuse for thumbnails); the **canvas/SVG icons** (`Icons.Draw`, renderable at any size); the two **densities** (Compact 20px / Comfortable 24px rows); the breadcrumb + footer; and the Options **Panes** page (where a default-view setting would live).

| Surface | Design load | Section |
|---|---|---|
| Switching view mode | **Medium** | §A |
| Detail view (current — reference) | **Light** | §B |
| Brief view (multi-column names) | **Heavy** | §C |
| Thumbnail grid | **Heavy** | §D |
| States (loading / non-image / empty / large) | **Medium** | §E |

---

## 1. Nocturne quick-reference

Dark only — **do not** design a light variant.

- **Surfaces:** app ground `Bg #161826`; raised bars `Surface #232532`; hairline `Divider` = Text @16%.
- **Text:** primary `Text #e9e9ed`; muted via opacity (`On(0.60)` / `On(0.45)` / `On(0.30)`). Directory names `Accent200`. Monospace (`Ui.Mono`) for sizes/counts/paths.
- **Accent:** blurple `#9184d9` ramp; cursor row = `Accent500 @13%` fill + `Accent600` 1px border (**reserve the border on every cell** so the cursor never shifts layout — a pattern used throughout).
- **File-manager literals:** marked `MarkRed #e0685f`; archive amber `#d9a06b`; image-kind green `#8fd0b0`; code `Accent300`; archive `#d9a06b`.
- **Radii** `Sm 4 · Md 8 · Row 6`. **Density** every mode must honor Compact/Comfortable.

## 2. Components to reuse

- **The row/cell selection idiom** — cursor ring, `MarkRed`, 45% cut-ghost, zebra tint. Brief cells and thumbnail tiles must carry the **same** cursor/mark/cut treatments so selection reads identically across modes.
- **`Icons.Draw(kind, tint?)`** — the SVG-rasterized file icons; renders at any box size (use larger in the thumbnail grid for non-images).
- **Image decode** — `SkiaImageSource.FromFile` (off-thread, ≤24 MB, Contain-fit) already powers the preview panel; thumbnails reuse it at a small target size.
- **Breadcrumb + footer** — unchanged across modes (footer keeps the files/dirs/free counts).
- **Options → Panes page** — the natural home for a "Default view" control.

---

## A. Switching view mode — *medium*

**Design the:** how a user changes a pane's view, and where the current mode is shown. Decisions to make and mock:
1. **Per-pane, not global** (Total Commander convention) — each pane remembers its own mode. Confirm and show two panes in different modes side by side.
2. **A control** — a small view-toggle (three segments: Detail / Brief / Thumbs, or three glyph buttons) somewhere in the pane chrome. Candidate: the right end of the **breadcrumb row** or a slim strip in the **list header**. Mock the placement (it must not crowd the breadcrumb's truncation).
3. **A keyboard cycle** — propose keys (TC uses Ctrl+F1/F2/F3 for Full/Brief/Thumbs; or a single "cycle view" key). These join the rebindable keymap, so pick sensible defaults.
4. **Active-mode indication** — the current mode's glyph/segment reads active (accent), the others muted.

## B. Detail view — *light (reference)*

The current view. Grid `icon 16 / name 1fr / size 60 / [ratio 40 inside archives] / modified 116`, row height = density, cursor ring / mark / zebra as today. **Design only** any small adjustments you'd make so Detail sits consistently in a three-mode set (e.g. the view-toggle control from §A living in its header). Otherwise no change — include it in the mock set as the baseline.

## C. Brief view — *heavy*

**Names only, multiple columns.** More files visible; no size/date. Files flow **top-to-bottom then wrap to the next column** (TC "Brief"), like a newspaper column layout.

**Design the:**
1. **Cell** — icon (16px) + name (mono/sans 12px, ellipsized). Column width: fixed, or sized to the longest name in that column? Propose (fixed columns are simpler and steadier; auto-fit reads tighter). Show the cursor ring / `MarkRed` / cut-ghost on a brief cell.
2. **Column count** — derive from pane width and a target column width; show how it reflows when the pane resizes (two panes → fewer columns each; Full-preview → one pane wider).
3. **Cursor navigation** — arrows move within a column (down/up) and across columns (left/right move between columns at the same row). Mock the cursor in a middle column. The `..` parent leads column 1.
4. **Footer** — unchanged (counts + free space).

**States:** a folder with many short names (several columns), long names (ellipsis), few items (one partial column), both densities.

## D. Thumbnail grid — *heavy*

**A tile grid.** Images show a **thumbnail**; folders, code, archives, other files show a **large icon** + name. The marquee visual mode.

**Design the:**
1. **Tile** — a fixed-size tile (propose a size, e.g. ~96–120px square-ish) containing: the thumbnail/large-icon on top, the filename (1–2 lines, ellipsized, centered) below. Directories use the folder icon (accent). The cursor ring / `MarkRed` name / cut-ghost apply to the whole tile.
2. **Thumbnail framing** — images Contain-fit inside the tile's image area with a subtle `Surface`/`Neutral900` backing (letterbox for non-square). Show a small type badge or extension for non-images? Propose.
3. **Grid layout** — tile gap, how many per row (flows from pane width), row height, and how it reflows on resize / in Full-preview.
4. **Cursor navigation** — arrows move by tile (left/right within a row, up/down between rows). The `..` parent as the first tile (or a back affordance). Mock the cursor on a middle tile.
5. **Footer** — unchanged.

**States to mock:** a folder of images (thumbnails loaded), **thumbnails still loading** (placeholder — reuse the "measuring…" spinner idiom, or a shimmer), a mix of images + folders + code files, a non-image-heavy folder (mostly large icons), empty folder, both densities (tile size may or may not track density — propose).

## E. States & edges — *medium*

- **Thumbnail loading** — async decode; design the per-tile placeholder (icon or spinner) that swaps to the image when ready. Never block the UI (decode is off-thread; the grid streams thumbnails in).
- **Un-decodable / huge image** — falls back to the image-kind icon; design that fallback tile.
- **Very large folder** — the grid/list is virtualized (only visible tiles realize); no design impact but the mock should imply scrolling.
- **Empty / offline / denied** — reuse the existing centered state blocks (they sit above whichever mode).
- **Marking a range / drag-select** — the existing right-drag marking should work in Brief/Thumbs too; confirm the marked treatment reads on a tile/brief cell.

---

## 3. Cross-cutting requirements

- **Dark only**, Nocturne tokens by name.
- **Selection parity** — cursor ring, `MarkRed`, cut-ghost, zebra must read the same in all three modes (a mark is a mark whether it's a row, a brief cell, or a tile).
- **Both densities** — at minimum for Detail and Brief; propose whether tile size tracks density.
- **Per-pane** — the two panes can be in different modes; mock that.
- **Keyboard-first** — every mode reachable by key (they join the rebindable keymap); the view-toggle control shows its key.
- **Accessibility** — the UIA/AT-SPI bridge is live; tiles/cells need text labels; don't rely on color alone (mark pairs red text with the icon).
- **Async, never blocking** — thumbnail decode streams in; the grid is responsive while decoding (we recently fixed a UI-thread flood in find — same discipline applies).

## 4. Open questions to resolve

1. **View-toggle placement:** breadcrumb-row end vs a slim strip in the list header? (Mock both, recommend one.)
2. **Brief column width:** fixed vs fit-to-longest-name?
3. **Thumbnail tile size:** one size, or a couple (small/large)? Does it track density?
4. **Non-image tiles:** large icon only, or icon + a type/extension badge?
5. **Keys:** confirm a scheme (e.g. Ctrl+F1 Detail / Ctrl+F2 Brief / Ctrl+F3 Thumbs, or a single cycle key).
6. **Where the default-view setting lives** on the Options Panes page (a segmented "Default view" control) — confirm.

## 5. Out of scope for Design (engineering only — no mockups)

- Thumbnail **decode + cache** (off-thread `SkiaImageSource`, keyed by path+mtime+size, bounded LRU) and streaming them into the grid.
- The **grid/flow virtualization** (only visible tiles/cells realize — the pane list is `VirtualListElement` today; Brief/Thumbs need a column-flow / grid-flow variant).
- Persisting per-pane view mode (session) + the default-view setting.
- Wiring the view-toggle keys into the rebindable keymap.

These need no screens — listed so Design doesn't spend time on them.
