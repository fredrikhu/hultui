# Vesper Commander — Design Brief: Archives

**For:** Claude Design
**From:** implementation
**Date:** 2026-08-05
**Scope:** UX/visual design for archive support (browse-into, pack, extract) *before* implementation.

---

## 0. What this is

Vesper is a Total Commander-style dual-pane file manager (HultUI/.NET, **Nocturne** dark theme, source of truth `apps/VesperCommander/Theme/Nocturne.cs`). It already handles real copy/move/delete with a streamed progress overlay, tabs, find, diff, and directory-sync. **Archives are the one marquee gap left.**

This brief covers only the parts that need **design**. The archive read/write engine, the virtual-filesystem layer, and wiring the existing Find/Sync "Archives" toggles are engineering and are listed as out-of-scope in §7.

**Deliverable from Design:** high-fidelity static mockups (HTML/CSS in the Nocturne idiom, matching the existing handoff `styles.css` / `_ds` bundle), one per surface below, showing **every listed state**. Annotate spacing, color roles by token name, and interaction affordances in a right-hand spec column, same as the batch-2 mocks. Dark theme only.

**What already exists to build on:** the `.zip/.7z/.rar/.gz/.tar/.bz2/.xz/.tgz` file kinds render with a **zip icon** (amber `#d9a06b` family); an `Archiver` setting (**Built-in zip** / **7-Zip**) sits on the Tools page; the Find and Sync surfaces each already show an **"Archives" checkbox** (currently inert — this batch makes it real).

| Surface | Design load | Section |
|---|---|---|
| Browse *into* an archive (pane as a folder) | **Heavy** | §A |
| Pack — create/add to an archive | **Heavy** | §B |
| Extract flow | **Medium** | §C |
| Archive-aware Find / Sync | **Light** | §D |
| Error / edge states | **Medium** | §E |

---

## 1. Nocturne quick-reference

Dark only — **do not** design a light variant.

- **Surfaces:** app ground `Bg #161826`; raised (bars, cards, menus) `Surface #232532`; cards use `Bg` + a `Neutral500` 1px border; overlay backdrop `#05060c` @62%.
- **Text:** primary `Text #e9e9ed`; muted via opacity — `On(0.60)` secondary, `On(0.45)` metadata, `On(0.30)` disabled. Monospace (`Ui.Mono`) for paths, sizes, counts.
- **Accent:** single blurple `#9184d9`, runtime-swappable ramp. `Accent200/300` bright labels, `Accent500` fills @9–28%, `Accent600` borders/rings.
- **File-manager literals:** marked files `MarkRed #e0685f` (never theme-configurable); directory rows `Accent200`; archive icon/accent `#d9a06b` (amber). Diff/sync greens: `DiffGreen #3eb37e`.
- **Radii** `Sm 4 · Md 8 · Lg 14 · Row 6`. **Type** 10–13.5px. **Density** rows are 20px (Compact) or 24px (Comfortable) — every list must honor both.

## 2. Components to reuse (compose from these, don't redraw)

- **`Backdrop(card, topAligned)` + `Card(width,title,subtitle)`** — the modal shell (dim backdrop + bordered/rounded/shadowed card with title + mono subtitle + ✕). Existing widths: 420 drive picker, 520 palette, 560 find, 440 sync pre-flight.
- **List-row grid** — `icon 16px / name 1fr / size 60px / modified 116px`, gap 10, row height = density. Header row exists.
- **Breadcrumb bar** — clickable path segments (`Crumb`), left-truncates whole folders with a "…" crumb; leading **drive crumb** opens the drive picker.
- **Progress overlay** (`BuildProgress`) — dual bars (File accent-500 / Total accent-700), per-item queue with status glyphs, rate/ETA, Pause/Skip/Background/Cancel. **Packing and extracting reuse this** — design stops at the pre-flight/confirm handoff.
- **Confirm / name-prompt dialogs** — for yes/no and single-field input (new-folder/rename precedent for the pack name field).
- **Command line** (bottom bar) and **footer** (files/dirs counts + free space + marked count).

---

## A. Browse *into* an archive — *heavy*

**Behavior:** Enter on a `.zip` (etc.) descends into it; the pane shows the archive's contents as if it were a folder. Navigate subfolders inside; `..` / Backspace at the archive root exits back to the real folder. This is the defining feature — get the "you are inside an archive" read right.

**Design the:**
1. **How the pane signals you're inside an archive.** The pane background never changes (Nocturne rule), so the signal must be in the chrome. Options to explore: an archive segment in the **breadcrumb** (e.g. `… / project / assets.zip ▸ / textures`), a slim **banner strip** under the breadcrumb ("Inside assets.zip · read-only · Esc to exit"), and/or an accent tint on the archive crumb. Recommend a breadcrumb treatment where the archive name reads as a distinct crumb (amber, archive glyph) and the path continues *inside* it — plus a one-line banner. Mock both and pick.
2. **The listing inside.** Same row grid (icon / name / size / modified). Show **uncompressed size** (and consider a packed-size or ratio column — propose whether it earns the space). The `..` row exits the archive. Folders inside the archive use the normal folder icon.
3. **What's disabled inside.** Browsing archives is read-only in v1: new-folder, rename, delete, paste **into** an archive are blocked. Design the affordance for a blocked action — reuse the existing transient **command-line notice** pattern ("Can't write inside an archive"), and consider dimming/hiding the F7/F8 hint chips while inside.
4. **Exit + tab behavior.** Backspace / `..` at the archive root returns to the containing folder with the cursor landing back on the archive file. Confirm this; mock the breadcrumb at the archive root vs a nested internal path.

**States to show:** archive root, a nested folder inside, empty archive, and the "blocked write" notice.

**Constraints:** honor both densities; breadcrumb truncation must still work with the archive crumb in the path; the archive crumb must be clickable (jump to archive root).

---

## B. Pack — create / add to an archive — *heavy*

**Behavior:** with files/folders selected, a **Pack** command (TC uses **Alt+F5**) opens a dialog to compress them into a new archive (or add to an existing one). Runs through the existing progress overlay.

**Design the pack dialog** (`Backdrop` + `Card`, ~520 wide):
- **Archive name** field (seeded from the selection or folder name; the name-prompt input is the precedent — give it explicit height, a known HultUI gotcha).
- **Format** — segmented `[Zip | 7z | tar.gz]` (Zip is the built-in default; 7z/tar route to the external 7-Zip per the `Archiver` setting — reflect availability: if 7-Zip isn't configured, show those disabled with a hint).
- **Compression level** — segmented or slider `[Store | Fast | Normal | Max]`.
- **Options** — checkboxes: "Move to archive (delete originals after)", "Put each item in its own archive", and (later) "Encrypt with password" (design the password field's presence but note password entry is security-sensitive — see §E).
- **Destination** — where the archive lands (default: the other pane's folder, matching copy/move). Show it as a read-only path with a change affordance, or a simple segmented `[This pane | Other pane]`.
- **Footer** — Cancel + a primary **Pack** button (accent outline, never filled, matching the app's button idiom). The selection summary ("Pack 12 items · 34 MB") reads near the title.

**States:** default, name-collision (archive already exists → overwrite/rename inline error), and 7-Zip-unavailable (external formats disabled).

**After confirm:** hands off to the existing progress overlay — **design stops at the dialog.**

---

## C. Extract flow — *medium*

**Behavior:** extracting is conceptually **copy-out**. Two paths to design the entry point for:
1. **Copy from inside an archive** — while browsing inside (§A), F5/copy on selected members extracts them to the other pane. This should reuse the **existing copy confirm + progress overlay** verbatim — so mostly no new UI, just confirm the wording ("Extract 5 items to …").
2. **Extract without browsing** — a **Extract** command (TC uses **Alt+F9**) on a selected archive file in the normal listing. Decide: does this need its own small dialog (destination + "extract to a subfolder named after the archive" toggle), or is it just "extract all to the other pane" through the copy confirm?

**Design the:** the extract confirm/dialog (if you decide it needs one) — likely a compact `Card` (~440) with destination + the "into a named subfolder" toggle + Extract primary. And confirm the copy-overlay wording for the browse-and-copy path.

**States:** normal extract, destination-exists conflict (reuse the existing overwrite dialog), partial/failed member.

---

## D. Archive-aware Find / Sync — *light*

The Find dialog and Sync view already render an **"Archives" checkbox** (currently inert). This batch makes it functional: when on, search/compare **descends into archives**, and archive members appear as results with the archive shown as their parent path.

**Design the:** the result/row treatment for an **archive member** — how a hit inside `assets.zip` reads in the find results list and in the sync grid (e.g. parent path shows `assets.zip › textures`, with the archive glyph). Mostly a small annotation on the existing find-result row and sync row; no new surface. Confirm the checkbox already-designed styling is reused.

---

## E. Error / edge states — *medium*

Design compact treatments (reuse `Card` or the centered message idiom from the diff/sync empty states) for:
- **Encrypted / password-protected archive** — a password prompt on entry/extract. **Design the prompt's look**, but note: entering passwords is security-sensitive; the field is masked and the value is never logged. Mock the prompt; implementation handles it carefully.
- **Corrupt / unreadable archive** — entering it fails: a centered "Can't open this archive — it may be corrupt" with a back affordance.
- **Unsupported format** — e.g. `.rar` create (read-only support only), or a format needing 7-Zip when it's not configured: a clear message pointing at the Tools → Archiver setting.
- **Format-support matrix** (a spec note, not a screen): built-in = **zip read + write**, tar/gz read; everything else (7z, rar, bz2, xz) via external **7-Zip** when configured (rar = read-only). Design should assume this capability split when disabling controls.

---

## 3. Cross-cutting requirements

- **Dark only**, Nocturne tokens by name; no ad-hoc hex.
- **Both densities** (20/24px rows) for anything list-shaped.
- **Every surface needs** idle, in-progress (async — reuse the progress overlay / the find "measuring…" spinner idiom), success, empty, and error states. We ship the states you draw.
- **Keyboard-first:** show key hints — **Enter** browse-in, **Backspace/..** exit, **Alt+F5** pack, **Alt+F9** extract, **F5** copy/extract-out. (Alt+F5/Alt+F9 are proposals — confirm in §4.)
- **Reuse `Backdrop`/`Card`** for the pack/extract/password dialogs; browse-in is *not* an overlay — it's the pane itself.
- **Accessibility:** the app has a live UIA/AT-SPI bridge — every control needs a text label; don't rely on color alone (pair the archive amber with the zip glyph).

## 4. Open questions to resolve

1. **In-archive signal:** breadcrumb archive-crumb vs a banner strip vs both? (Recommend both — mock and decide.)
2. **Extract:** does the non-browse Extract command need its own dialog, or reuse the copy confirm with a destination?
3. **Key bindings:** confirm Pack = Alt+F5, Extract = Alt+F9 (TC convention). These would join the rebindable keymap.
4. **Packed-size / ratio column** inside archives — worth the width, or drop it?
5. **Password entry** in the pack dialog — include an "Encrypt" option in v1, or defer encryption entirely?

## 5. Out of scope for Design (engineering only — no mockups)

- The archive read/write engine (`System.IO.Compression.ZipArchive` for zip; shelling out to 7-Zip for other formats via the `Archiver` setting).
- The archive **virtual-filesystem** layer that makes `Enter` on a `.zip` present its contents as a listing (a `FileEntry` provider keyed by `archive!internal/path`).
- Wiring the Find/Sync "Archives" toggles to the walker/compare engines.
- Making Pack/Extract reuse `RunTransfer` + the progress overlay.

These need no screens — listed so Design doesn't spend time on them.
