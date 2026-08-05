# Vesper Commander — Design Brief: Multi-Rename Tool

**For:** Claude Design
**From:** implementation
**Date:** 2026-08-05
**Scope:** UX/visual design for a batch-rename tool (Total Commander's "Multi-Rename Tool") — a modal overlay that renames the marked files by a template, with a live before/after preview. Design *before* implementation.

---

## 0. What this is

Vesper is a Total Commander-style dual-pane file manager (HultUI/.NET, **Nocturne** dark theme, tokens in `apps/VesperCommander/Theme/Nocturne.cs`). It already has single-file rename (Shift+F6). This adds **batch rename**: select N files, open a dialog, build a new-name template, watch a live old→new preview, apply. Invoked on the **marked set** (or the cursor file when nothing is marked — the existing `OperationTargets` rule).

This brief covers only what needs **design**. The rename engine (`FileOps.Rename`), the token expansion, and conflict detection are engineering (out of scope, §6).

**Deliverable from Design:** high-fidelity static mockups (HTML/CSS in the Nocturne idiom, matching the existing `_ds` handoff bundles), one per surface/state below, token names + spacing + interactions annotated in a right-hand "Spec" column — same format as the batch-2/3/4 handoffs. Dark theme only.

**What already exists to build on:** the overlay shell — `DialogCard(width)` / `Backdrop` / `DialogFooter`, the reusable `TextInput`, `Seg(...)` segmented control, `ToggleRow`, `CheckItem`, `Stepper`, `GhostButton`/`PrimaryButton` (all in `CommanderApp.Options.cs`); `VirtualListElement` for the preview rows; `Ui.Mono`/`Ui.Kbd`; the `Icons.Draw` file icons. Reuse these — don't invent new primitives.

| Surface | Design load | Section |
|---|---|---|
| Dialog shell & layout | **Medium** | §A |
| Name / extension template + tokens | **Heavy** | §B |
| Counter | **Medium** | §C |
| Search & replace | **Medium** | §D |
| Case & trimming | **Light** | §E |
| Live preview table | **Heavy** | §F |
| States & edges | **Medium** | §G |

---

## 1. Nocturne quick-reference

Dark only. Surfaces: app ground `Bg #161826`; card `Bg` + `Neutral500` 1px border + `RadiusMd` + shadow; raised bars/rows `Surface #232532`; hairline `Divider` = Text @16%. Text: primary `Text #e9e9ed`; muted `On(0.60)`/`On(0.45)`; mono (`Ui.Mono`) for names/tokens/counts. Accent blurple `#9184d9` ramp (200 links, 500 fills @6–14%, 600 borders/rings, 900 fill). Literals: `MarkRed #e0685f` (conflicts/errors), a green (valid/changed) — reuse the diff green `#3eb37e` if a "will change" accent is wanted. Radii `Sm 4 · Md 8 · Row 6`.

## 2. Components to reuse
- **`DialogCard(width)` + `DialogFooter`** — the Options window shell (940px there; this tool likely wants ~720–820px). Header = title + subtitle (e.g. "12 files") + ✕.
- **`TextInput`** for the mask/find/replace fields (needs explicit height — see the known gotcha; wrap borderless inputs in a bordered box).
- **`Seg` / `ToggleRow` / `CheckItem` / `Stepper`** for options (case mode, regex toggle, counter digits).
- **`VirtualListElement`** for the preview table (batches can be hundreds of files).
- **`Ui.Kbd`** for token chips and shortcut hints.

---

## A. Dialog shell & layout — *medium*
- A modal card over the dimmed shell (like Options). Propose the overall split: a **top controls region** (template + options) and a **bottom live-preview table** that grows to fill, with the footer (**Rename N** primary + Cancel + a reset).
- The card should feel like one workbench: as you edit any control, the preview updates instantly.
- Opening: propose a keybinding (Total Commander uses **Ctrl+M**; it joins the rebindable "commands" keymap) and a command-palette entry "Multi-rename…". Header subtitle shows the count ("Rename 12 files").
- A11y: focus lands in the name-mask field; Tab order flows controls → preview → footer; Esc cancels.

## B. Name & extension template + tokens — *heavy*
The core. Two fields — **name mask** and **extension mask** — that accept literal text plus bracketed **tokens**. Design the fields, and a **token palette** (clickable chips that insert a token at the caret, each with a tooltip).

Propose the **token set** (recommend a focused core, mark the rest as optional):
- `[N]` — the whole original name (without extension); `[N2-5]` — characters 2–5; `[N2-]` — from char 2 to end.
- `[E]` — the original extension.
- `[C]` — the counter (see §C); `[C:3]` — zero-padded to 3 digits inline.
- `[P]` — the parent folder name.
- `[Y][M][D]` / `[h][m][s]` — the file's modified date/time parts (optional — mark as such).

Show: a field with mixed literals + tokens (tokens visually distinct — a subtle chip/pill vs literal text, or just monospace with accent-colored `[...]`), the token palette below/beside it, and how an empty extension mask means "keep original extension". Annotate the escape for a literal bracket.

## C. Counter — *medium*
The `[C]` token's configuration: **start** (default 1), **step** (default 1), **digits / zero-pad** (e.g. `001`), and optionally a **base** (decimal only for v1 — flag hex/letters as optional). Design this as a compact inline group (Steppers / small fields). Show how the counter appears in the preview (0001, 0002, …).

## D. Search & replace — *medium*
An optional **Find** + **Replace** pair applied to the resulting name, with a **regex** toggle (and, implied, case-sensitivity). Design the two fields + the regex toggle + a subtle inline validity indicator when the regex is malformed (red hairline / icon, no modal). Show a before/after where the replace fires.

## E. Case & trimming — *light*
A segmented **case** control: `As-is · UPPER · lower · Title Case · first-cap`, applied to the name. Optionally a "trim spaces" / "remove double spaces" toggle. Keep compact.

## F. Live preview table — *heavy*
The heart of the trust model — every file's **old name → new name**, updating live. Design:
- **Row**: original name (muted/mono) · an arrow · new name (primary). The changed portion could subtly highlight, but keep it legible at N rows.
- **Conflict / error states per row**, color-coded and never by color alone (pair an icon):
  - **collision** — two files map to the same new name, or the target already exists on disk → `MarkRed` new name + a ⚠ icon + a short reason.
  - **unchanged** — new == old → dim the row (it will be skipped).
  - **invalid** — empty result / illegal characters → `MarkRed` + reason.
- A **summary bar** above or below the table: "10 will rename · 2 conflicts · 0 unchanged", conflicts in red. The **Rename** button is disabled (or warns) while conflicts remain — propose which.
- Virtualized (hundreds of rows); show a scrolled state.

## G. States & edges — *medium*
- **No template change** — new names equal originals; Rename disabled, summary "nothing to rename".
- **All conflicts** — Rename disabled with the reason surfaced.
- **Single file** — the tool still works (degenerate batch); or should single-file route to the existing inline rename? Propose.
- **Very large batch** (hundreds) — virtualized table, responsive.
- **After apply** — propose: close on success; on partial failure (some renames error mid-batch, e.g. locked file) show which failed and keep the dialog open. Design the partial-failure surface.
- **Undo** — out of scope for v1 engineering, but if the design wants an "undo last rename" affordance, note it (flagged as optional).

---

## 3. Cross-cutting
- **Dark only**, Nocturne tokens by name.
- **Live**: any control edit re-previews instantly (debounced under the hood).
- **Keyboard-first**: reachable via keymap + palette; Enter on the dialog = Rename (when enabled), Esc = cancel; the token palette chips are clickable and keyboard-insertable.
- **Non-destructive until Apply**: nothing renames until the user commits; conflicts block the commit.
- **Accessibility**: the preview rows expose "old → new, conflict" as text; validity/errors aren't color-only.
- Reuse the existing rename engine's semantics (same-name = no-op, illegal chars rejected).

## 4. Open questions for Design
1. **Token set scope** — which tokens are v1 core vs optional (date/time, parent, ranges)?
2. **Counter placement** — inline near the mask, or its own labeled group?
3. **Rename button while conflicts exist** — hard-disabled, or enabled-but-warns-and-skips-conflicts?
4. **Single-file case** — open the full tool, or defer to inline Shift+F6?
5. **Changed-portion highlight** in the preview — worth the visual noise, or keep new names plain?
6. **Undo affordance** — include a lightweight "undo last batch", or omit for v1?

## 5. Out of scope for Design (engineering only — no mockups)
- Token expansion + counter/date formatting; regex find/replace; case transforms.
- Conflict detection (intra-batch collisions + on-disk existence) and safe two-phase apply (rename via temp names when a batch permutes names, e.g. a↔b swaps).
- The rename transaction + partial-failure reporting; optional undo journal.
- Wiring Ctrl+M into the rebindable keymap + the palette entry.

These need no screens — listed so Design doesn't spend time on them.
