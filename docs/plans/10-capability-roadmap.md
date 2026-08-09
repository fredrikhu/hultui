# Capability Roadmap: In-Spirit Feature Gaps

Prioritized backlog of capabilities the framework lacks, filtered to those that
fit HultUI's spirit and ordered by impact-over-effort. Companion to the
[master plan](00-master-plan.md), which covered the Git-client API feedback.

## What "in spirit" means here

A candidate is **in spirit** when it is one of:

- a **Skia draw primitive** exposed through the existing `IRenderContext`
  `Push*/Pop` + `Draw*` model,
- a **text or box-model primitive** that slots into the layout/text pipeline,
- a **reactive ergonomic** built on the existing signals/animation core, or
- a **reusable interactive widget** (the framework already ships Checkbox,
  Slider, ScrollView, ContextMenu, VirtualList).

Explicitly **out of spirit** (see [Not included](#not-included-out-of-spirit)):
a CSS selector/stylesheet cascade, media-query/breakpoint engine, WebView/video,
and print/PDF export.

## Priority order

Tiers are ordered by priority. Within a tier, do the rows top-to-bottom.
Effort is rough dev-days-equivalent; Impact is how often users hit the gap.

### Tier A — Complete the text model (small, high daily impact)

The text pipeline already shapes, wraps, caches, and (now) ellipsizes. These
finish it. All are fluent additions plus a pipeline/renderer touch.

| # | Feature | Why in spirit | Effort | Impact |
|---|---------|---------------|--------|--------|
| 1 | **`text-align`** (left/center/right/justify) | Pure text-pipeline primitive; per-line X offset in `TextLayout`. Table-stakes and the most-felt gap. | S | High |
| 2 | **Italic / oblique** — wire `FontSlant` into `InheritedStyle`/`ResolvedInheritedStyle` + `.Italic()` | Completes an existing stub; itemizer already takes a slant arg (hardcoded `Normal`). | XS | Med-High |
| 3 | **Text decoration** — underline / strikethrough | Renderer draws lines under/over run boxes; a style flag + `DrawLine`. | S | Med |
| 4 | **`line-height` + `letter-spacing`** as inherited style | Text primitives; `TextAreaElement` already has an internal line-height to generalize. | S-M | Med |

### Tier B — Visual primitives (Skia, medium effort, high "modern look" impact)

These are the difference between "functional" and "polished". All are native
Skia capabilities behind new `IRenderContext` members.

| # | Feature | Why in spirit | Effort | Impact |
|---|---------|---------------|--------|--------|
| 5 | **Box-shadow / drop-shadow** | Skia `MaskFilter`/shadow; a `BoxStyle` prop + one draw call. Needed for elevation, menus, dialogs. | M | High |
| 6 | **Linear / radial gradients** as fills | Generalize `Color` fills to a `Brush` (solid \| gradient) in the fill plumbing. Fits the existing fill model. | M | High |
| 7 | **Border upgrades** — per-side color, dashed/dotted, focus-ring primitive | Skia paint options; extends `DrawRoundedRect`. Unblocks proper focus indication. | M | Med-High |
| 8 | **2D transforms** — rotate / scale (+ transform-origin) via `PushTransform` | Skia-native; extends the `Push*` stack that already has translate. Must invert the matrix in `HitTester` and paint culling. | M-H | Med |

### Tier C — Reactive interaction & motion (medium effort, high ergonomics)

Built on the reactive + animation core, **not** a CSS cascade. This removes the
biggest ergonomic pain the Git client hit (hand-wiring hover handlers everywhere).

| # | Feature | Why in spirit | Effort | Impact |
|---|---------|---------------|--------|--------|
| 9 | **Interaction-state styling** — `:hover` / `:focus` / `:pressed` the reactive way (e.g. hover/focus signals or `.OnHover(style => …)`) | Reactive, not selector-based. Junction wires `PointerEnter`/`Leave` by hand in every widget (see `SplitPane`); this is the fix that stays true to the model. | M | High |
| 10 | **Declarative transitions + spring easing** | `AnimationManager`/`Tween` already exist; animate a property when its bound value changes, add a spring `Easing`. | M | Med-High |

### Tier D — First-class widgets (medium effort, high reuse)

The framework already ships interactive widgets, so these belong in the box.
Ordered by reuse-per-effort. Heavier composites (TreeView, SplitPane, DataGrid)
stay app-level for now — they compose from primitives and vary per app.

| # | Feature | Why in spirit | Effort | Impact |
|---|---------|---------------|--------|--------|
| 11 | **Dialog / Modal** primitive on the overlay host | Every Junction dialog is bespoke; one primitive replaces them all. | M | High |
| 12 | **Dropdown / Select / ComboBox** | Builds on the existing overlay + ContextMenu machinery. Ubiquitous, currently absent. | M | High |
| 13 | **ProgressBar + Spinner** | Small, self-contained; determinate + indeterminate. | S | Med |
| 14 | **Tabs / TabView** | Tabbed / multi-pane UIs are hand-built today; a primitive generalizes them. | M | Med |

### Tier E — Layout primitives (small-medium)

| # | Feature | Why in spirit | Effort | Impact |
|---|---------|---------------|--------|--------|
| 15 | **`aspect-ratio`** | Layout-engine primitive; fits flex/grid sizing. | S-M | Med |
| 16 | **`position: sticky`** | Extends the existing position model; useful for scroll headers. | M | Low-Med |

## In spirit but large — deferred, flagged deliberately

These belong long-term but are projects, not increments. Call them out so they
aren't mistaken for quick wins.

- **Rich text / inline spans** — mixed weight/color/size/links within one text
  block. In spirit (text primitive) but turns `TextElement` into a multi-run
  model; sizeable pipeline + selection + a11y work.
- **BiDi / RTL reordering** — `TextDirection.Rtl` is a stub; correct Arabic/Hebrew
  needs the Unicode BiDi algorithm before shaping. In spirit for correctness, but
  a substantial algorithmic addition. Do it before claiming i18n support.

## Known follow-ups (deferred)

Small, understood gaps left behind while shipping the tiers, kept here so they
aren't forgotten.

- **Dialog: drag-select past the input edge** — text drag-selection inside an
  overlay-hosted input stops if the pointer leaves the input, because the
  drag-capture path is gated to non-overlay hits. Click-to-caret, typing,
  double-click word-select and right-click Cut/Copy/Paste all work. Fix needs the
  pointer-move handler to apply the overlay's offset when capturing.
- **Dialog: Esc-to-dismiss + focus trap** — a modal dialog should close on Esc and
  keep Tab focus within itself. Needs the dialog to move keyboard focus into its
  content (a bit of `FocusManager` access through the overlay host). Dismissal is
  currently scrim-click / buttons only.
- **Dialog: `Window` presentation** — the separate-OS-window backend (agreed:
  overlay first, window later). Currently `DialogPresentation.Window` throws.

## Not included (out of spirit)

Mirrors the master plan's exclusion list — kept out on purpose, with the reason.

- **CSS selector / stylesheet cascade + style classes** — conflicts with the
  imperative-builder + `Theme` model. Tier C #9 delivers the practical `:hover`
  need reactively without importing the cascade.
- **Media queries / responsive breakpoints / container queries** — better
  expressed by binding to a window-size signal than as a new engine.
- **Backdrop-filter (frosted glass), background images, CSS filters, blend modes**
  — low value vs. cost; the `CanvasElement` immediate-mode escape hatch covers
  bespoke cases.
- **WebView / video / embedded browser, print / PDF export** — out of scope for a
  Skia UI toolkit.
- **Retained SVG/path element** — `CanvasElement` already covers immediate-mode
  vector drawing.

## Process

- Same as the master plan: execute in tier order; after each feature, verify the
  build and (where there's runtime surface) drive it in a sample. Add a detailed
  per-feature plan doc (like `01`–`09`) when a feature needs design decisions.
- Suggested first slice: **Tier A in one pass** (`text-align`, italic, decoration,
  line-height) — all small, all high-frequency, no new subsystems.
