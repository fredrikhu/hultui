# Vesper Commander — SFTP Design Brief

**For:** Claude Design
**From:** implementation
**Date:** 2026-08-11
**Scope:** Visual/UX design for SFTP support — a pane can browse a remote host over SSH, and every existing file operation works across the local/remote boundary — *before* implementation starts.

---

## 0. What this is

MobaXterm is Windows-only, Termius is Electron with a subscription, WinSCP is not a commander on other platforms. A dual-pane manager where one side is remote is Vesper's natural next step — and the plumbing precedent already exists: the archive feature proved a pane can run over a virtual filesystem (`ArchiveVfs`), with copy/view/search working across the real/virtual seam. SFTP is another VFS backend behind the same interface. **The engineering seam is solved; what needs design is everything the network adds**: connections, trust, auth, latency, and failure.

Everything here fits the existing **Nocturne** design system (`apps/VesperCommander/Theme/Nocturne.cs`, quick-reference in `DESIGN-HANDOVER.md` §1–2) and reuses the built components — `Backdrop`/`Card`, the list-row grid, the breadcrumb bar, the drive picker, the progress overlay, the confirm/overwrite dialogs. Do not invent a new visual language.

**Deliverable from Design:** static HTML/CSS mockups in the Nocturne idiom, one per surface, every listed state, tokens by name, key hints annotated.

| # | Surface | Design load | Section |
|---|---------|-------------|---------|
| 1 | Connection manager (sites list + quick connect) | **Heavy** | §A |
| 2 | Host-key trust prompt | **Medium** | §B |
| 3 | Auth prompts (password / key / passphrase / keyboard-interactive) | **Medium** | §C |
| 4 | Remote pane chrome (how a connected pane reads) | **Heavy** | §D |
| 5 | Remote listing specifics (permissions, owner, symlinks) | **Medium** | §E |
| 6 | Transfers over the network (progress semantics) | **Medium** | §F |
| 7 | Disconnect / reconnect / error states | **Heavy** | §G |

---

## A. Connection manager — *heavy*

**Entry point (decided):** saved SFTP sites appear **in the drive picker** below the drives, plus a "New connection…" row — a remote host is conceptually a drive, and the leading breadcrumb crumb already opens this surface. Do not design a dedicated manager dialog; the site *editor* (§A.3) is the only new modal.

**Design the:**
1. **Sites list** — saved connections: name, `user@host:port` (mono), optional folder grouping, last-connected hint. Reuse list-row styling; letter-key/type-ahead like the drive picker.
2. **Quick connect** — a one-line `user@host` input for unsaved hops, with "save this" after success.
3. **Site editor** (`Backdrop` + `Card`, ~480) — name, host, port, user, auth method (password / key file / agent), initial remote path, optional local "pair" directory for the other pane. Follow the Options dialog's control styling.
4. **Connected indicator** in the sites list — a site that already has a live session (opening it again reuses the session).

**Constraints:** passwords are never stored in the settings file — the editor offers "save password" only as an OS-keyring option (engineering handles storage; design shows the wording and a keyring badge). Key file path is mono with a browse affordance.

**States:** empty (no saved sites — first-run copy), connecting (row-level spinner, cancellable), auth failed (row-level error, re-prompt path), unreachable.

## B. Host-key trust prompt — *medium, security-critical*

First contact with a host (or a **changed** key) must interrupt with a trust decision. Two distinct designs — do not merge them:

1. **Unknown host (TOFU)** — fingerprint (SHA-256, mono, selectable), key type, host; actions: Trust & connect / Connect once / Cancel. Neutral tone.
2. **KEY CHANGED** — this is the man-in-the-middle warning. Alarm treatment (`MarkRed` register), old vs new fingerprint side by side, and the safe action (Cancel) must be the default/primary; "trust the new key" requires deliberate effort (secondary placement, maybe typed confirmation). Never one-click.

Both are `Backdrop` + `Card`. Show the exact copy — wording is part of the design here.

## C. Auth prompts — *medium*

Sequential, cancellable prompts riding the existing dialog idiom (the archive password prompt is precedent):

1. **Password** — masked input, "save to keyring" checkbox, attempt counter on retry (wrong password re-prompts, shows "2nd attempt").
2. **Key passphrase** — same shape, names the key file (mono).
3. **Keyboard-interactive** — server-sent prompt text rendered verbatim (it may be a 2FA code request).
4. **Agent** — no prompt; only a failure state ("agent has no acceptable key — fall back to password?").

## D. Remote pane chrome — *heavy*

The core question: **at a glance, which pane is remote?** Mixing up local and remote before an F8 delete is the disaster case.

**Design the:**
1. **Breadcrumb treatment (decided: this is the marker)** — leading crumb becomes an **accent-tinted host chip**, `user@host` in mono, where the drive letter normally sits; the "where am I" slot stays consistent between local and remote. Path crumbs continue normally (`/var/www/…`). No whole-bar tint or pane border — the chip carries it. Prove it at both densities and against the active-pane indicator.
2. **Destructive-op reinforcement** — the chip alone is not enough protection for F8: delete/move confirmations on a remote pane must name the host in the dialog body ("Delete 3 files from **web-prod-01**?"). Design that dialog variant; it is the real guard for the mixed-panes disaster case.
3. **Connection state in the chrome** — live (default, quiet), reconnecting (animated, amber), lost (red, with the §G overlay). A latency hint is optional — propose it only if it earns its place.
4. **Footer additions** — free-space is often unavailable over SFTP; design the footer with that slot absent or showing server info instead.

## E. Remote listing specifics — *medium*

The list grid gains Unix metadata:

1. **Permissions column** — `rwxr-xr-x` (mono) as an optional column like the existing ones; decide default-on or default-off for remote panes. A chmod editor dialog (nine checkboxes + octal input, live-synced) for F-key/context-menu "Properties".
2. **Owner/group** — optional column, mono, `On(0.45)`.
3. **Symlinks** — arrow-annotated name (`name → target`), target in muted mono; broken symlink in the error register. Directories-vs-file link behavior is engineering.
4. **Hidden dotfiles** — reuse the existing hidden-files toggle; no new design, just confirm it applies.

**Constraints:** both densities; columns participate in the existing sort-header behavior.

## F. Transfers — *medium*

The existing progress overlay (dual bars, queue, rate/ETA, Pause/Skip/Background/Cancel) is reused as-is. Design only the network deltas:

1. **Stall state** — throughput at zero with the connection still up: the rate line shows a stall indicator instead of a frozen number.
2. **Resume** — an interrupted transfer found a partial file on reconnect: an overwrite-dialog variant with a **Resume** action (show partial size vs total, mono).
3. **Remote↔remote** — copying between two remote panes routes through the local machine; a one-line hint in the pre-flight confirm so the user knows the data path.

## G. Disconnect / reconnect / errors — *heavy*

The network fails mid-anything. Design the ladder:

1. **Blip (auto-reconnect)** — the pane keeps its listing, chrome shows reconnecting (§D.3), a countdown "reconnecting… (attempt 2)"; user can force-retry or disconnect.
2. **Dead (reconnect failed)** — pane content dims (listing is stale — treat like the cut-pending ghost, whole-listing), a centered in-pane message with Retry / Disconnect actions. The other pane must remain fully usable.
3. **Mid-operation failure** — transfer interrupted: progress overlay error state, per-item retry/skip (existing idiom), resume path per §F.2.
4. **Auth expired / password changed** — reconnect triggers a fresh §C prompt, not a silent failure.
5. **Permission denied** on a remote op — the existing error-line idiom in dialogs; nothing new, just confirm the copy fits.

**States matrix to mock:** connected-idle, reconnecting, dead, dead-with-other-pane-active — the last one matters because it proves the app degrades to a single-pane manager instead of a modal wall.

---

## 1. Cross-cutting requirements

- **Dark only**, Nocturne tokens by name; the only candidate new tokens are the remote-pane marker tint and a stall/amber — justify each as a named addition.
- **Two densities** for every list surface.
- **Every surface:** idle/empty, in-progress, success, no-result, error. Omissions ship as blank screens.
- **Keyboard-first.** Sites list is letter-navigable like the drive picker; all dialogs Esc-cancellable; propose the "open connection manager" binding if it isn't just the drive-picker key.
- **Accessibility:** UIA/AT-SPI live; the remote marker and connection states must not be color-only (pair with text/glyph); fingerprints selectable/copyable.

## 2. Explicitly out of scope for Design (engineering-only)

- SSH library choice and AOT compatibility, the `IVfs`/`ArchiveVfs`-style backend seam, keyring/DPAPI storage, agent protocol, connection pooling, known_hosts file handling, transfer resume mechanics.
- Terminal-over-SSH (opening a shell on the host) — a separate future feature; don't design for it, but don't paint the chrome into a corner that forbids a "terminal here" affordance later.

## 3. Decisions (pre-answered — Design does not need to ask)

1. **Connection entry point: the drive picker** (§A). No dedicated manager dialog.
2. **Permissions column: default-on for remote panes**, absent for local ones — it's the WinSCP-class expectation and the pane has the width; users can hide it like any optional column.
3. **Latency indicator: dropped for v1.** Quiet chrome; the reconnecting/stall states (§D.3, §F.1) already surface a bad link when it matters.
4. **Both panes remote: supported in v1.** The host chip must work doubled — two different hosts side by side is exactly when identification matters most; mock that case.
5. **Remote marker: the accent-tinted host chip** in the breadcrumb (§D.1), reinforced by host-naming destructive dialogs (§D.2). No bar tint, no pane border.
