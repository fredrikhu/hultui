# Vesper Commander — Implementation Plan: Archives

**Responds to:** `ARCHIVES-DESIGN-BRIEF.md` + Claude Design's batch-3 mocks (G–K).
**Date:** 2026-08-05

## The decision that shapes everything

`System.IO.Compression` (the BCL zip API) **cannot** read/write password-protected zips and **cannot** handle 7z/rar/bz2/xz. This is precisely why the design's **capability matrix** routes those through 7-Zip:

| | zip | tar·gz | 7z | rar | encrypted |
|---|---|---|---|---|---|
| **Built-in** (`System.IO.Compression`) | r + w | r | — | — | — |
| **7-Zip** (external `7z.exe`, when configured) | r + w | r + w | r + w | r only | r + w |

**Backend split (recommended, matches the matrix, no new managed dependency):**
- **Built-in** handles plain zip (read **and** write) and tar/gz read — covers the common case with zero external deps, AOT-safe.
- **7-Zip** (shelled `7z.exe`, path in a new setting) handles everything else: 7z, rar (read), bz2/xz/tgz, **and all encryption** (reading protected archives + creating encrypted ones).
- 7-Zip is **optional**: when it isn't configured, the capability matrix disables the controls that need it (the Pack "Encrypt" checkbox, the 7z/tar.gz format options) with the "set it in Tools → Archiver" hint the design already drew.

**Consequence for the design:** encryption is v1 **via 7-Zip only** — the built-in zip path stays unencrypted, and the Pack "Encrypt" checkbox + the password prompt only apply to 7-Zip-handled archives. This is fully consistent with the matrix; it just means the encrypt controls gate on 7-Zip being present.

**→ Open decision for you:** accept the 7-Zip-gated encryption above, **or** add a managed library (SharpZipLib — AOT-compatible) so built-in zip can do encryption without 7-Zip. Recommend the 7-Zip route (no new dependency, matches the matrix).

---

## Architecture: representing "inside an archive"

A pane's `Dir` becomes an **archive-descended path** while browsing inside — e.g. `C:\dev\assets.zip` (root) or `C:\dev\assets.zip\textures` (nested). These aren't real filesystem paths, so a small parser routes them:

- **`ArchivePath.TryParse(path) → (archiveFile, internalPath)?`** — walk the segments; if one is an archive file *and* a real file on disk, split there. `C:\dev\assets.zip\textures` → (`C:\dev\assets.zip`, `textures`).
- Everything that consumes a `Dir` checks this first: `FileSystem.List`, `ActivateCursor`, the breadcrumb, copy sources, find/sync. Real paths fall straight through to today's code — **no behavior change off the archive path.**

`FileEntry` gains one optional field: `long PackedSize = -1` (for the Ratio % column; -1 = not-in-archive). The archive-member marker is implicit (its `FullPath` parses as an archive path).

---

## Phase 0 — Archive VFS foundation (engine, no UI)

**New `ArchiveVfs.cs`** — the backend behind everything:
- `bool IsArchiveFile(string path)` — by extension (the set already in `Icons.KindFor`).
- `Capabilities Caps(string ext)` — the matrix above (read/write per backend), factoring in whether 7-Zip is configured.
- `DirListing ListEntries(string archiveFile, string internalPath, string? password)` — immediate children at that level, as `FileEntry`s with `PackedSize`. Built-in via `ZipArchive`; else shell `7z l` and parse. Surfaces `PaneState.Denied` for encrypted-without-password, a corrupt sentinel otherwise.
- `Stream? OpenEntry(string archiveFile, string entryPath, string? password)` — for extract + (later) preview/content.
- `string? Create(IReadOnlyList<string> sources, string archivePath, PackOptions opts)` — built-in zip write, else `7z a`. Returns error or null.

**New `ArchivePath.cs`** — the parser + helpers (`InArchive(dir)`, `Split`, `Combine`).

**New `SevenZip.cs`** — locate/validate `7z.exe` (from the setting or PATH), run list/extract/add, parse output. All best-effort; absence → capabilities downgrade.

**Tests** (`tests/VesperCommander.Tests`): `ArchivePath` parsing (nested, no-archive, archive-at-leaf); `ArchiveVfs.ListEntries` over a committed sample `.zip` fixture; capability matrix per extension with/without 7-Zip. These are pure/fixture-based and fully headless.

**Risk:** low — self-contained engine, no UI wiring yet.

---

## Phase 1 — Browse into an archive (§G)

- **`FileSystem.List`** (or a wrapper `Listing()` calls): if `ArchivePath.TryParse` hits, delegate to `ArchiveVfs.ListEntries`; else today's path. The `..` row leads; at the archive root, `..` points to the containing real folder.
- **`ActivateCursor`**: Enter on an archive file → `NavigateTo(side, archiveFilePath)` (descend to root). Enter on a folder inside → `NavigateTo` to the nested archive path. `..` / Backspace at the archive root → the containing folder, `focusChildName` = the archive file (cursor lands back on it).
- **Breadcrumb** (`BuildBreadcrumb`/`Crumb`, Pane.cs): the archive segment renders as the **amber crumb** (`#d9a06b` text + zip glyph + amber @10% fill, radius Sm); clickable → archive root. Truncation pins the drive **and** archive crumbs (only real folders collapse into "…").
- **Listing grid inside** — swap in the Ratio column: `16 / 1fr / 60 / 40 / 116` when `InArchive(Dir)`. Ratio = `PackedSize/Size`; dirs show "—". `ListLayout()` gains an in-archive variant.
- **Footer inside**: amber counts + "packed K · %" replaces free space.
- **Read-only enforcement**: reuse the `LockedBlock` notice idiom — a new `InArchiveBlock(side)` gates F7/F8/rename/paste-in/move ("Can't write inside an archive — F5 copies out", amber, transient). F7/F8 footer hint chips dim to Neutral700 while inside.
- **States:** root, nested+truncated, empty archive, blocked-write.

**Verification:** vshot with a sample `.zip` in the tree — descend (Enter), screenshot root + nested + empty + the blocked-write notice.

**Risk:** medium — the path-routing must not leak into real-fs code (drive picker, `PathEquals`, measured-dir cache). Keep the archive check localized to `List`/`ActivateCursor`/breadcrumb.

---

## Phase 2 — Extract (§C/§I)

Extract = **copy-out**, reusing `RunTransfer` + the progress overlay:
- **`FileOps.CopyFileStreamed`** currently opens the source with `File.OpenRead`. Add an archive-aware source: when the source `FullPath` parses as an archive member, copy from `ArchiveVfs.OpenEntry(...)` instead. (One branch; the streaming/progress/cancel machinery is unchanged.)
- **F5 while inside an archive** → the copy confirm with the verb **"Extract"** (Card 440), destination = other pane. Runs the streamed transfer.
- **Alt+F9 on an archive file** in a normal listing → same confirm + the **one** checkbox ("Into a subfolder `assets\`", default ON when the archive root has >1 top-level entry). New `Cmd.Extract`.
- **Password**: if `OpenEntry` reports encrypted-locked, raise the password prompt (Phase 5) before the transfer; feed the cached password through.
- **F6 (move) inside an archive** → blocked (read-only), the §G notice.
- Conflicts reuse the existing overwrite dialog (source row shows `archive › member`). Partial failure → the MarkRed footer notice.

**Verification:** extract a zip entry to a scratch dir headlessly; assert the file lands with correct bytes.

**Risk:** medium — the copy-source branch + wiring password through the transfer worker.

---

## Phase 3 — Pack (§B/§H)

- **Pack dialog overlay** — new `CommanderApp.Pack.cs` + `BuildPack()` (`Backdrop` + `Card 520`): name (explicit `Height(30)`), Format seg `[Zip|7z|tar.gz]`, Level seg, checkboxes (move / own-archive-each / encrypt), Destination seg + path preview, Cancel + primary "Pack N items". `Cmd.Pack` (Alt+F5). State: default, encrypt-on (masked field), name-collision (Add-to-it / auto-rename, primary disabled), 7-Zip-unavailable (external formats @45% + hint).
- **`ArchiveVfs.Create`** drives it: built-in `ZipArchive` write (levels map to `CompressionLevel`); 7z/tar.gz/encrypt via `7z a`. "Own archive each" loops per item; "Move to archive" deletes sources on success.
- **Runs through the progress overlay** — packing many files reports per-item like a copy (reuse the `TransferOp` progress shape, or a thin pack-progress that drives the same overlay). Design stops at the dialog.
- **Encrypt** control enabled only when 7-Zip is configured (per the decision above).

**Verification:** pack a few files headlessly → assert the archive exists and round-trips (list its entries back via `ArchiveVfs`).

**Risk:** medium-high — the pack dialog is net-new UI; the built-in vs 7-Zip write split; progress integration.

---

## Phase 4 — Archive-aware Find & Sync (§D/§J)

- **Find** (`FindEngine`): when the (now-live) **Archives** option is on, descend into archive files during the walk — enumerate members via `ArchiveVfs` as `Hit`s whose parent path is the archive. The find-result row renders the member parent path (zip glyph + amber archive name + `›` + internal path). `↵` jumps the pane **inside** the archive (`NavigateTo` to the member's archive-dir + `focusChildName`). Footer gains "N inside archives".
- **Sync** (`SyncEngine`): with Archives on, include archive members aligned by relative path (archive root = its containing folder). `→` out of an archive = extract (the copy overlay); **`←` into an archive is disabled** (read-only v1) with the reason on the rail tooltip. Content mode reads the entry stream. Encrypted archives are skipped + counted in the denied banner.
- **Wire the existing "Archives" checkboxes** (Find dialog + Sync toolbar) — they exist visually from batch 2; make them functional (no new control).

**Verification:** find `*.png` recursively with Archives on over a tree containing a sample zip → members appear; SyncEngine classification test with a member row.

**Risk:** medium — extending the two engines to walk archives; the find-jump-into-archive path.

---

## Phase 5 — Errors, password, capability (§E/§K)

- **Password prompt overlay** — new `BuildPasswordPrompt()` (`Card 420`): masked native input (`Height(30)`), "show" hold-to-reveal, amber lock glyph, archive name in the title. **Security rules (hold to these):** value never stored or logged, `UIA IsPassword=true`, focus-trapped, **one prompt per archive per session** (in-memory `Dictionary<archivePath,string>` cache, never persisted), cleared on cancel. Wrong password → MarkRed border + inline error, field clears + keeps focus, no animation.
- **Corrupt / needs-7-Zip** — the centered in-pane message idiom (diff/sync empty-state precedent): MarkRed warn glyph (broken) vs amber zip glyph (missing tool), reason line, Back/Backspace. The pane never descends — cursor stays on the file, the message replaces the listing region.
- **Unsupported create** (.rar) — the amber command-line notice, no modal.
- **Capability matrix** — encoded once in `ArchiveVfs.Caps`; the Pack format seg and Extract read it to disable controls with a reason hint.

**Risk:** medium — the password handling must be exactly right (the security rules are non-negotiable).

---

## Phase 6 — Keybindings, settings, final pass

- **Keymap**: add `Cmd.Pack` (Alt+F5) and `Cmd.Extract` (Alt+F9) to `Keybindings.Defaults`/`Order` — they become rebindable like the rest.
- **Settings**: add a **7-Zip path** field (the `Archiver` enum stays; add `public string SevenZipPath = "";`) with a Tools-page field; `SevenZip.cs` reads it (falls back to PATH). Update the Tools "Archiver" area to show the 7-Zip path + status (found / not found).
- **Final**: full build, engine tests green, vshot pass across browse/pack/extract/password/errors.

---

## Deferred (note, not in v1)
- **Preview inside archives** (text/image preview reading an entry stream) — a stretch; the preview column stays blank inside archives in v1.
- **Nested archives** (a zip inside a zip) — one level only in v1.
- **rar/7z create** — never offered (matrix); read-only where the matrix says so.

## Suggested commit sequence
Phase 0 (VFS engine + tests) · Phase 1 (browse-in) · Phase 2 (extract) · Phase 3 (pack) · Phase 4 (find/sync) · Phase 5 (errors/password) · Phase 6 (keys/settings/final) — one commit per phase, engine-with-tests first where one exists (VFS, capability, classification are all headless-testable), then the UI, then a vshot pass. Confirm between phases; the browse-in + extract pair is the natural first shippable milestone.
