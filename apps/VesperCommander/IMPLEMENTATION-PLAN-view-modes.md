# Implementation Plan — Batch 4: View modes (Detail / Brief / Thumbnails)

Responds to `design_handoff_vesper_commander_batch4` (mocks L–P). Design resolved all six open questions from the brief — every decision below is **approved**, so this plan is about *how*, not *whether*.

## Approved design decisions (from README)
1. **Toggle** = 3 glyph buttons pinned flush-right of the **breadcrumb row** (the one chrome row in all modes). Glyphs: rows / two columns / 2×2 grid.
2. **Brief** = fixed columns, target **186px**, `count = max(1, floor(innerWidth ÷ 186))`, remainder stretched. Top-to-bottom then wrap.
3. **Thumbnail tile** tracks density: **100px tile / 92×70 image area (comfortable)**, **84 / 76×56 (compact)**.
4. **Non-image tiles** = kind icon (30 / 24 compact) + **ext badge** (mono 8.5, uppercase); dirs = accent folder, no badge; images = no badge.
5. **Keys** = Ctrl+F1 Detail / Ctrl+F2 Brief / Ctrl+F3 Thumbs, rebindable, new "Pane view" keymap group.
6. **Default view** = Options → Panes, seg row under Row density; applies to new tabs/panes only, each pane then keeps its own mode for the session.

## Architecture grounding (current code)
- **Per-pane state** lives on `PaneTab` (`CommanderApp.cs:48`) — add `ViewMode View`. Persisted via `SessionStore`/`SessionData` (needs the mode added).
- **Render dispatch** is `BuildPane` (`CommanderApp.Pane.cs:134`): today `state==Ok ? BuildList(...) : …`. Becomes a switch on the tab's `View`.
- **Detail** = the existing `BuildList` → `VirtualListElement<FileEntry>` (`Pane.cs:435`) + `FileRow` + `ListLayout`. Unchanged; only the §A toggle joins the breadcrumb.
- **Keymap** = `Cmd` enum + `Keybindings.Defaults`/`Order` (`Keybindings.cs`); dispatch via `MatchHotkey(wantGlobal:true)` (Ctrl-tier) → `Dispatch(cmd)` in `CommanderApp.Keys.cs`.
- **Options** = `PanesPage` + the searchable `OptionCatalog()` (`CommanderApp.Options.cs`); `Settings` is source-gen JSON (add `DefaultView`).
- **Icons** = `Icons.cs` rasterizes SVG **path** data → cached tinted bitmaps. The 3 view glyphs are `<rect>` compositions, not paths → extend the rasterizer to draw rounded rects (or convert rects to path `d`).
- **Image decode** = `SkiaImageSource.FromFile` (off-thread, ≤24 MB), today a single `_imageLoader` for the preview. Thumbnails need a **multi-entry cache**, not the single loader.
- **Virtualization** = `VirtualListElement<T>` is vertical-row only. **Thumbnails** reuse it by chunking items into rows-of-tiles (`VirtualListElement<TileRow>`). **Brief** needs a column-flow layout (no vertical scroll) — a new builder, not the virtual list.

---

## P1 — View-mode infrastructure + Brief view
The scaffolding all three modes share, plus the first non-Detail mode (Brief needs no decode pipeline, so it lands cheaply).

- **`ViewMode` enum** `{ Detail, Brief, Thumbnails }`; `PaneTab.View` (defaults from `Settings.DefaultView` at tab creation). `NewTab`/session-restore seed it; `SwapPanes` moves it with the tab.
- **Persistence** — `SessionData` per-tab records gain the mode; `Settings.DefaultView` (enum, `SettingsJsonContext`). Default = Detail.
- **Glyphs** — `IconKind.ViewDetail/ViewBrief/ViewThumb`; teach `Icons.Rasterize` to draw the rounded-rect glyph sets (g-det 3 rows, g-bri 2×3 columns, g-thu 2×2 grid) at 16-grid.
- **Toggle** — in `BuildBreadcrumb`, a right-pinned (`flex:none`) 3-button group: 22×15, glyph 11, Bg fill + Neutral800 border, radius Sm4. States: idle Neutral500 · hover Neutral300 + Accent500@8% · active Accent + Accent500@14%. Click sets the pane's mode; tooltip shows the live binding ("Brief · Ctrl+F2"). Breadcrumb budget shrinks by the toggle width so crumbs truncate first.
- **Keys** — `Cmd.ViewDetail/ViewBrief/ViewThumb` = Ctrl+F1/F2/F3 in `Defaults`; new "Pane view" trio at the end of `Order`. `Dispatch` sets the active tab's `View` + `MarkDirty`. Ctrl-tier, so they flow through `MatchHotkey(wantGlobal:true)`.
- **Options** — `PanesPage`: "Default view" `Seg(Detail/Brief/Thumbnails)` under Row density; mirror into `OptionCatalog()` (keywords "view brief thumbnail grid detail") so settings-search finds it.
- **Render switch** — `BuildPane` `state==Ok` branch switches on `View`: Detail→`BuildList`, Brief→`BuildBrief`, Thumbnails→`BuildThumbs` (P2 stub → Detail until built).
- **Brief view** (`BuildBrief`) — compute `rowsPerCol = max(1, floor(availH ÷ rowHeight))`, `cols = ceil(count ÷ rowsPerCol)`, `colWidth = max(186, innerWidth ÷ min(cols, floor(innerWidth÷186)))` per the fixed-186 rule. Column-major fill (`..` leads col 1). Cell = 16 icon + `Ui.Mono` 11.5 name, padding 0 9, gap 7, height = density, ellipsis + tooltip. 1px Divider@60% rule between non-empty columns. Cursor ring / MarkRed / cut-ghost reuse the row treatments. Horizontal `ScrollView`; vertical never scrolls.
- **Brief nav** — column-major means the flat list order *is* the visual order: ↑↓ = cursor∓1 (crosses columns at ends naturally); ←→ = cursor∓`rowsPerCol` (same row, adjacent column), clamped; Home/End; PgUp/PgDn = ∓(rowsPerCol). Cache `rowsPerCol` per side at build (`_briefRows[side]`) for the key handler. `MoveCursor` gains a brief-aware branch (or a `ViewNav` helper switching on mode).

- **Brief column-window virtualization** (decided up front) — only realize columns intersecting the horizontal viewport. Compute the visible column range from `ScrollOffsetX` and `colWidth`, render a spacer for the off-screen left columns + the visible slice + a right spacer, so a 10k-file dir realizes only the on-screen cells. The column-major index math is unchanged; only which columns get built is windowed.

## P2 — Thumbnail grid (static, no decode yet)
Layout, tiles, navigation — images show the loading placeholder; decode arrives in P3.

- **Grid** (`BuildThumbs`) — tile/image sizes from density; `perRow = max(1, floor((innerW + gap) ÷ (tile + gap)))`, gap 8/7, padding 10. Chunk items into rows of `perRow` → `VirtualListElement<TileRow>` (itemHeight = tile + gap) so large folders virtualize for free. Cache `perRow` per side (`_thumbCols[side]`) for nav.
- **Tile** — `..` tile first (up-arrow + dir-colored ".."); dirs = accent folder icon; non-images = kind icon (30/24) + ext badge (mono 8.5 uppercase, Neutral800@88% chip, Neutral300 text, bottom-right); images = image area on Neutral900 backing (placeholder in P2). Name `Ui.Mono` 10.5 centered, 2-line clamp (1-line compact). Cursor ring on whole tile (inset radius 6, no shift); marked = MarkRed name+icon + 7px MarkRed chip at image top-left; cut = 45% ghost; hover Accent500@6%.
- **Thumb nav** — ←→ = cursor∓1; ↑↓ = cursor∓`perRow`; Home/End; PgUp/PgDn = ∓(perRow × visibleRows); Enter opens, Backspace up. Same `ViewNav` switch as Brief.

## P3 — Thumbnail decode pipeline
- **`ThumbnailCache`** — off-thread decode + bounded LRU (~512), keyed `path|mtime|size|targetPx`. `Get(entry, targetPx)` returns a cached `IImageSource` or `null` and enqueues a decode. Decode = `SkiaImageSource.FromFile` then downscale to the image-area box (keeps memory bounded); skip >24 MB and undecodable → mark **fallback**.
- **Streaming** — worker posts a throttled `InvokeOnMainThread(MarkDirty)` (~12/s, the find-repaint idiom) as thumbs land. Only **visible** tiles call `Get` (VirtualListElement realizes visible rows only), so decode naturally windows to the viewport.
- **Tile lifecycle** — placeholder (image-kind icon @40% on backing + still shimmer band Neutral100@4.5%) → swap in place (no fade, zero shift) → fallback (icon full strength + ext badge).
- **Footer** — while the decode queue is non-empty, the right slot shows "decoding n…" (the "measuring…" idiom), reverting to free-space when drained.
- **Dispose** — evict + dispose bitmaps on cache trim and app dispose (mirrors the preview's `_lastImage` discipline).

## P4 — States, selection parity, tests
- **Right-drag marquee** — extend the existing right-drag marking (rows) to brief cells and tiles: Accent600 1px rect + Accent500@8% fill, radius 2; mark entries ≥50% covered on release. Selection parity across all three modes.
- **State blocks** — empty/offline/denied already sit above the list; confirm they render above Brief/Thumbs too, with breadcrumb + toggle live.
- **A11y** — Brief cells expose name + "column n of m"; tiles expose name + kind + mark state; decode progress polite ("3 thumbnails loading"); glyphs aria-hidden; toggle = radiogroup.
- **Tests** (`ViewModesTests`) — brief column math (`rowsPerCol`/`cols`/index↔position), thumb `perRow`, `ThumbnailCache` key + LRU eviction + fallback classification. Headless screenshots for Brief (3-col / 1-col reflow / both densities) and Thumbnails (mixed folder, compact, placeholder). Extend the vshot harness with a resize hook for reflow shots.

---

## Sequencing & scope
Four phases, each buildable + screenshot-verifiable on its own; one commit per phase (the established rhythm). P1 delivers the toggle + Brief (visible value immediately); P2–P3 build Thumbnails; P4 hardens. Detail is untouched throughout.

**Decided:** build all four phases straight through; Brief ships with column-window virtualization (above).

**Deferred (not in this batch):** thumbnail decode of archive-interior images, animated fade on thumb swap, per-mode column customization.
