# Junction — GitLab Integration Design Brief

**For:** Claude Design
**From:** implementation
**Date:** 2026-09-12
**Status:** Verified with the product owner 2026-09-12. Decisions: all of §2 is in this round; sign-in is OAuth on gitlab.com and PAT on self-managed; the merge-request surface is an overlay panel, not a tab; inline code-review comments (view and write, multi-line ranges) are in scope.
**Scope:** Visual/UX design for a GitLab integration in Junction — accounts, merge requests, pipelines, issues and protected branches — *before* implementation starts. Read `DESIGN-HANDOVER.md` first; it describes the app as it is today and every screenshot referenced here lives in `design/shots/`.

---

## 0. What this is

Junction is a multi-repository desktop git client. Everything it does today is local git plus fetch/pull/push. Fork, GitKraken and Tower all reach past the remote into the hosting service, and that is the one axis on which Junction is behind. GitLab is the target because it is what the team runs (gitlab.com and self-managed both have to work).

**The engineering seam:** Junction has no HTTP client, no accounts, no token storage. All of that is plumbing and is not the design problem. **What needs design is where hosted state shows up in a client whose every screen is about local state**, and how the app behaves when the network is slow, the token is stale, or the repo is not on GitLab at all — which is most repos in most workspaces.

**Non-negotiables (from the handover):**

- **No eighth toolbar tab.** The toolbar is full. New surfaces are overlay panels (the 640-wide z-42 card in `11-worktrees-panel.png`), rows in existing grids, chips on existing rows, or entries in existing menus.
- **Design in Nocturne, name colours by role.** Eleven schemes render every screen. GitLab's orange is not a role in the ramp; do not introduce it. A GitLab-backed thing is marked by an icon (`ph-gitlab-logo`, Phosphor has it), not a colour.
- **Every verb registers in the command palette** (`09-command-palette.png`).
- **Local-first degradation.** A repo with no GitLab remote, no account, or no network must look exactly like today's app with nothing added — no empty columns, no greyed panels, no nag.
- **Reuse the built parts:** repository panels, DialogSpec prompts/confirms, the row grid with chips and per-row icon actions, the include-bar chip idiom, toasts, the Branches/Tags header shape (search · segmented toggle · primary button).

---

## 1. What GitLab offers (research summary, REST v4, verified against docs.gitlab.com 2026-09-12)

| Area | What the API gives a desktop client | Tier |
|---|---|---|
| **Auth** | Personal access token (`PRIVATE-TOKEN` header, default 1-year expiry, `GET /personal_access_tokens/self` to probe scopes/expiry) or OAuth 2.0 authorization-code + PKCE (loopback redirect, 2 h access token, refresh token). Self-managed needs an OAuth app per instance, so PAT is the zero-setup path there. Scopes: `read_api` for everything read-only, `api` for every write, `write_repository` if the token doubles as the git HTTPS credential (`oauth2:<token>@host`). | Free |
| **Merge requests** | List with filters (state, `scope=assigned_to_me / reviews_for_me / created_by_me`, source/target branch, search); detail with `detailed_merge_status` (mergeable · conflict · need_rebase · ci_still_running · not_approved · discussions_not_resolved · draft_status …), `has_conflicts`, `head_pipeline`, approvals (`approved`, `approvals_left`, `approved_by`), `web_url`. Create (source, target, title, description, draft, reviewers, labels, milestone, remove-source-branch, squash). Merge with a `sha` guard, squash, merge-when-pipeline-succeeds. Rebase. Approve / unapprove. Discussions and notes, resolve threads, diff-anchored comments. Local checkout via `refs/merge-requests/<iid>/head`. | Free (multiple reviewers, required approval rules, merge trains: Premium) |
| **Pipelines** | Pipelines per ref/sha with status (`created · pending · running · success · failed · canceled · skipped · manual · scheduled · waiting_for_resource`), `GET /pipelines/latest?ref=`; jobs per pipeline with a plain-text trace; retry / cancel / play; create a pipeline for a ref with variables. **Commit statuses** per SHA (`/repository/commits/:sha/statuses`) — cheapest per-commit signal. | Free |
| **Issues** | List/search (state, assignee, labels, milestone, text), detail, create, close. Closing keywords in commit messages (`Closes #12`, `Fixes group/proj#34`) close on merge to the default branch; a bare `#12` cross-links. Branch-from-issue is a UI convention (`<iid>-<slug>`), reproducible with the branches API. | Free |
| **Project** | Lookup by URL-encoded path (`group%2Fproject`) → id, default branch, avatar, merge method, squash option, "remove source branch after merge". Membership list (`membership=true&min_access_level=30`) for a clone browser. **Protected branches** with push/merge access levels and `allow_force_push`. Releases from tags. Forks. Compare API. | Free |
| **User / todos** | `GET /user` for identity + avatar. **Todos** are the notification feed: assigned, mentioned, review_requested, approval_required, build_failed, unmergeable; mark done. | Free |
| **Limits** | 2,000 req/min per user on gitlab.com, `per_page` ≤ 100, no keyset paging for MRs or pipelines (page by `updated_after`). No webhooks for a desktop app — everything is polled. GraphQL can fetch MR + pipeline + approvals in one call. | — |

Full notes with endpoint paths: `design/gitlab-api-notes.md`.

---

## 2. Functions in this design round

Ranked by daily value to a developer working across several repositories. The product owner confirmed the whole list on 2026-09-12; the tiers now mean **build order**, not design order — design all of them, and make the Tier 1 surfaces leave the slots Tier 2 fills.

### Tier 1 — build first

| # | Function | Why first |
|---|---|---|
| 1 | **Accounts** — add a gitlab.com or self-managed host with a PAT (OAuth as the gitlab.com default), see identity/avatar, token expiry and scopes, sign out. | Everything else depends on it. |
| 2 | **Repo ↔ project detection** — a repo whose `origin` points at a known host is GitLab-backed, silently; the project's default branch, merge method and protected branches are known. | Zero-config; makes every other feature appear on the right repos and only those. |
| 3 | **Merge request list + detail + local checkout** — assigned to me / reviews for me / mine / all open, per repo and across the workspace; detail shows status, pipeline, approvals, description, discussions; *Checkout locally* fetches the MR head ref. | The thing people open a browser tab for ten times a day. |
| 4 | **Create merge request** from a branch (Branches row, push toast, palette, commit composer after publish) — title/description pre-filled from commits, target branch defaults to the project default, draft, reviewers, delete-source-branch, squash. | The second thing. Closes the loop that ends today at "Published X to origin". |
| 5 | **Pipeline status** on the head commit of each branch (Branches grid), on History rows that have one, on the current branch in the toolbar, and on the MR; click-through to the web job. | Cheap (commit statuses), high glance value. |
| 6 | **Issue links and closing references** — GitLab issue refs become links without an `.issuetracker` file; the composer suggests `Closes #<iid>` and autocompletes issue numbers/titles. | Extends a feature that already exists. |
| 7 | **Protected-branch awareness** — lock mark on protected branches; push, force-push and delete confirm with the real rule ("`main` is protected: maintainers only, force push not allowed"). | Prevents the confusing server-side rejection. |
| 8 | **Code review in the diff** — read an MR's line-anchored discussion threads inside Junction's own diff pane, and write new ones: on one line or on a **selected range of lines**, on either side of the diff; reply, resolve, unresolve. | Added by the product owner. Reviewing is the half of the MR loop the web tab still owns; Junction's diff is already better than GitLab's. |

### Tier 2 — build second, design now

| # | Function |
|---|---|
| 9 | **Act on an MR from Junction** — approve / unapprove, merge (with the squash / remove-source / when-pipeline-succeeds options the project allows), rebase, mark ready / draft. |
| 10 | **Todos bell** — pending todos count in the title bar; a panel listing them with mark-done; the source of "someone requested your review". |
| 11 | **Clone from GitLab** — a project browser (my groups, my projects, search) inside the existing Clone modal, instead of pasting a URL. |
| 12 | **Branch from issue** — pick an issue, get `<iid>-<slug>` on the default branch, and an MR later pre-filled with `Closes #<iid>`. |
| 13 | **Workspace-wide MR / pipeline dashboard** on the Overview — every repo card shows open MRs and default-branch pipeline; a workspace list of "my MRs" and "waiting on my review" across repos. This is the multi-repo differentiator. |

### Out of this round

Job log viewer, run a pipeline with variables, create/edit issues, releases from the Tags view, snippets, merge trains and approval rules (Premium), forks. Each is a whole screen for an occasional action; the web UI is the right place until the surfaces above prove themselves.

---

## 3. Integration points — where each function lands in today's UI

Design load per surface. Screenshot references are to `design/shots/`.

| # | Surface | Load | Functions |
|---|---|---|---|
| A | Accounts panel | **Heavy** | 1 |
| B | Repo detection marks (sidebar, Overview card, Repository settings) | Light | 2 |
| C | Merge requests panel (list + detail + actions) | **Heavy** | 3, 9, 13 |
| D | Create merge request dialog | **Medium** | 4, 12 |
| E | Pipeline status glyphs (Branches, History, toolbar, Commit header) | **Medium** | 5 |
| F | Composer issue autocomplete + closing hint | Light | 6 |
| G | Protected-branch confirmations | Light | 7 |
| H | Todos in the title bar | Light | 10 |
| I | Clone-from-GitLab browser | **Medium** | 11 |
| J | Overview per-repo hosted status | **Medium** | 13 |
| K | Review threads in the diff pane | **Heavy** | 8 |

### A. Accounts — *heavy*

**Entry:** Repository tools menu gains *GitLab accounts…* (`08-repository-tools-menu.png`; below the separator, beside Reflog, or a second separator — decide), plus the palette. Also the first-run path: the first time a GitLab-backed repo is opened with no account, a **one-line, dismissable strip** at the top of the Branches / History view offers *Connect to gitlab.com to see merge requests and pipelines* — never a modal, never repeated after dismissal.

**Design the:**

1. **Accounts panel** — the z-42 repository-panel shape, but app-scoped (no repo subtitle). Rows: avatar, display name, `@username`, host (mono), token kind (OAuth / PAT), expiry with a warning register when under 14 days, scopes as small neutral tags. Toolbar: *Add gitlab.com account* (primary, OAuth in the browser) · *Add self-managed host…*.
2. **Add self-managed host** — a DialogSpec-style card: host URL, then PAT field (masked, paste-first), a *Test* result line showing the identity it resolves to, a note on required scopes. Show the four states: idle · testing · verified (identity shown) · failed (401 / wrong host / missing scope, with the scope named).
3. **Waiting for the browser** state for OAuth — the card that sits there while the system browser completes sign-in, with *Cancel* and *Paste a token instead*.
4. **Token expired / revoked** state — how the app tells you, and where: a row-level error in the panel, and a quiet inline notice wherever hosted data would have been (an MR panel that says *Sign in again* rather than showing nothing).

**Constraints:** tokens go to the OS credential store, never the settings file — the panel can say so once. Multiple accounts on one host are out of scope. A host with a private CA is engineering; design shows only the failure copy.

### B. Repo ↔ project marks — *light*

- Sidebar repo row: a 12px `ph-gitlab-logo` after the name, muted, tooltip `group/project on gitlab.example.com`. Nothing else — the row is full (`10-sidebar-add-popover.png`).
- Overview card (`01-overview.png`): the same mark beside the path, and this is where J adds status.
- Repository settings mode of the repo modal: a read-only *Hosted on* line with the detected project, and an override for the rare repo whose remote is not `origin`.
- A repo on a host with **no account** shows the mark greyed with tooltip *Connect an account to use GitLab features*.

### C. Merge requests panel — *heavy*

The centre of the design. **Entry:** Repository tools menu *Merge requests…*, palette, the `!123` chip on a branch row, the toolbar's pipeline/MR glyph (E), and Overview counts (J).

**Design the:**

1. **List** — the z-42 panel, but wider is allowed (760, the rebase modal's width) because the detail needs room. Filter chips in the include-bar idiom (`02b-history-only-current-branch.png`): *Assigned to me · Reviewing · Mine · All open*, and a *this repo / workspace* toggle. Rows: `!iid` mono, title, `Draft` outline tag, source → target as two branch chips (the app's existing prefix-chip + leaf treatment), author avatar, updated time, a **pipeline glyph** and an **approvals fragment** (`2/2` or `1 of 2`), and a **status word** derived from `detailed_merge_status` (*Mergeable · Conflicts · Needs rebase · Pipeline running · Needs approval · Unresolved threads · Draft*). Sort by updated. Search box with `/`.
2. **Detail** — selecting a row opens the detail inside the panel (master-detail, or a second page with *← Merge requests*; decide and justify against the 760 width). Header: title, `!iid`, state tag, source → target, author, *Open in browser* ghost. Then: status block (the mergeability word with its reason, pipeline with stage glyphs, approvals with avatars), description (markdown, rendered plainly — the app renders no markdown today; propose the minimum: paragraphs, code, lists), **Changes** (file list with counts — the diff itself opens in the Commit view's range compare between target and MR head once checked out, so the panel shows the list, not the diff), **Discussions** (MR-level threads with resolved/unresolved marks and a reply field; line-anchored threads live in the diff, §K, and this list links to them by file and line).
3. **Primary action: *Checkout locally*** — fetches `refs/merge-requests/<iid>/head`, creates or updates a local branch, selects it. Show the busy state. **Review** opens the MR's diff in the Commit view (§K) without checking anything out.
4. **Action row** (function 9): *Approve* (toggles to *Revoke approval*; shows who else approved), **Merge ▾** (a small menu: *Merge now · Merge when pipeline succeeds*, with the squash / delete-source-branch checkboxes the project allows shown inline, and the reason it is disabled when it is — the mergeability word from the status block), *Rebase* (only when `need_rebase`), *Mark as ready* / *Mark as draft*, *Open in browser*. Merge is a confirm (DialogSpec) that repeats source → target and the resulting commit shape. Every action shows its busy state on the button, not a panel spinner.
5. **States:** loading (skeleton rows), empty (*No open merge requests for hultui-revamped*), no account (§A.4), offline (*Showing what was loaded 4 min ago* — cached list with a stale mark), rate-limited, and action failures inline under the action row (405 not mergeable, 409 the MR moved under you — *Refresh and try again*).

### D. Create merge request — *medium*

**Entry:** Branches row context menu and row action *Create merge request* when the branch is pushed and has no open MR; the **push/publish toast** gains an action (*Published X to origin · Create merge request →* — the toast is one line today; design the actionable variant); palette; Commit composer footer after a publish.

**Design the:** a DialogSpec-family card, ~560: source (fixed, chip) → target (dropdown of remote branches, default = project default branch), title (pre-filled from the single commit subject or the branch leaf), description (pre-filled from commit bodies, or the issue's `Closes #`), **Draft** checkbox, reviewers (avatar picker, searchable), labels (tag picker), *Delete source branch when merged* and *Squash* checkboxes defaulting to the project's settings, primary *Create merge request*. Result: toast with `!iid` and *Open* action, and the branch row now shows the chip.

**States:** branch not pushed (the dialog offers to push first — one extra line, not a second dialog), MR already exists (open it instead), target is protected and the user cannot merge (informational line, not a block).

### E. Pipeline status glyphs — *medium*

One glyph vocabulary reused everywhere: **success · failed · running (animated, the toolbar already animates Fetch) · pending · canceled · skipped · manual · none**. Shape-coded, not only colour-coded (eleven schemes, some light). Places:

1. **Branches grid** (`05-branches.png`): a new narrow column between UPDATED and AHEAD / BEHIND, or a glyph inside the LAST COMMIT cell — the grid is wide; a column is cleaner. The same cell shows the **MR chip** `!123` when one is open from that branch, and a **lock** for protected branches (G). Clicking the glyph opens the pipeline in the browser (a jobs panel is out of this round).
2. **History rows** (`02-history.png`): a glyph after the stats on rows whose SHA has a status; most rows will have none.
3. **Toolbar**: a single glyph for the current branch's head, between the view tabs and Fetch, tooltip with the pipeline's stage summary. Icon-only in compact mode; hidden when the repo is not GitLab-backed.
4. **Commit header** (`03-commit.png`): glyph + *Pipeline #4021 · passed 12 min ago* after the branch, and *Merged by !123* when the commit came in through an MR.

### F. Composer issue autocomplete — *light*

In the Changes composer, typing `#` in Summary or Description opens a small anchored list (the palette row shape at 12px) of open issues by number and title, filtered as you type. A hint line under Description — *Closes #1042 will close the issue when this reaches main* — appears only when a closing keyword is present. Issue refs in History/Commit become links with no `.issuetracker` file when the repo is GitLab-backed.

### G. Protected-branch confirmations — *light*

The existing DialogSpec confirm gains an informational line with the lock icon: *`main` is protected on gitlab.com — maintainers can push, force push is not allowed*. Design it for push, force-push (the checkbox becomes disabled with that reason), delete branch, and delete remote branch. A lock also appears in the Branches cell (E.1).

### H. Todos — *light*

A bell in the title bar left of the search button, with a count badge in the accent; hidden when zero and when no account. Opens the palette-shaped list (600 wide) of pending todos: action verb (*review requested · mentioned · assigned · pipeline failed*), MR/issue title, project, time, *Done* per row and *Mark all done*. Selecting one opens the MR panel on that MR.

### I. Clone from GitLab — *medium*

The Clone mode of the repo modal (`RepoModalView`) gains a segmented *URL / GitLab* switch. GitLab side: account picker if more than one, search box, a list of projects (avatar, `group/project`, last activity, visibility tag) from memberships, then the existing target folder and colour swatch. Show loading and the empty search.

### J. Overview hosted status — *medium*

Each repo card (`01-overview.png`) gains one footer line: pipeline glyph for the default branch, `3 MRs` (with *2 need you* in the accent when any are assigned or awaiting your review), lock count is not needed. Above the grid, a workspace strip: *You have 4 merge requests to review across 3 repositories* with a link that opens the MR panel in workspace scope (C). This is the cross-repo view nobody else has; make it earn its line.

### K. Review threads in the diff pane — *heavy*

The MR's changes open in the **Commit view** as a range compare between the MR's `start_sha` (merge base) and `head_sha` (`03-commit.png` is the host layout: folder tree, splitter, diff pane with Unified / Split / Wrap). Both refs are fetched first, so positions Junction sends match GitLab's `diff_refs` exactly. This view is read-only — the Changes view's line-staging checkboxes (`04-changes.png`) never appear here, so the gutter is free for review.

**Design the:**

1. **Header context** — the Commit header shows the MR instead of a commit: `!123` chip, title, source → target, *Open in browser*, and a **thread summary** (*4 threads · 1 unresolved*) with prev/next-unresolved arrows (`n` / `p` mirror the find bar). The file tree gains a small count badge on files that carry threads and a dot on files with unresolved ones.
2. **Thread markers in the gutter** — a comment glyph in a new 20px gutter column left of the line numbers, on every line that anchors a thread; filled when unresolved, outlined when resolved. Clicking toggles the thread open under that row. Threads are **collapsed by default**; a palette / header toggle expands all.
3. **Thread card** — rendered between diff rows, full pane width, on the `Surface` ground with a 1px `Neutral700` border, indented to the code column so the line numbers stay aligned. Each note: avatar, name, relative time, body (the same plain markdown minimum as the MR description), `system` notes hidden. Footer: *Reply* field (collapsed to one line until focused), **Resolve** / **Unresolve**, *Open in browser*. Resolved threads render dimmed with the resolved-by line and a *Show* link instead of the notes.
4. **Starting a comment on one line** — hovering a row shows a `+` affordance at the far left of the gutter; clicking opens an empty composer card under that row (textarea, *Comment* primary, *Cancel*, and a note *on line 142 (new)* / *(old)* so the side is explicit). In split view the affordance exists on each half and the note names the side; in unified view the row's own side is used.
5. **Starting a comment on a range** — press the `+` and **drag** down (or up) across rows, or Shift+click a second row: the swept rows get a `Accent900` band in the gutter and code columns, and the composer's note reads *on lines 138–146 (new)*. Ranges cannot cross a hunk boundary or mix sides; the drag stops at the limit and the band shows where it stopped. Keyboard: the diff already has a keyboard caret — `Shift+↑↓` extends a range from it and `c` opens the composer. Design the band on Nocturne and on Solarized Light.
6. **Draft review vs immediate comment** — GitLab distinguishes a single comment (published at once) from notes collected into a review and published together. The composer's primary reads *Add comment*; a second, quieter button *Start review* switches the session into review mode, where every further composer reads *Add to review* and a **pending-review strip** appears at the bottom of the diff pane: *3 comments pending · Submit review ▾ (Comment · Approve · Request changes) · Discard*. Design both modes and the strip. (Engineering: the draft-notes API is Free-tier and verified — see `design/gitlab-api-notes.md` §2; multi-line ranges are `position[line_range]` with a `line_code` per end, and a range cannot mix old and new sides, which is why the band stops at a side boundary.)
7. **Outdated threads** — a thread whose line no longer exists in the current head renders in the file's thread list, not in the gutter, with an *outdated* tag and the original line reference.
8. **States:** loading threads (gutter empty, header says *loading discussions…*), no account (the gutter is absent and the view is the ordinary range compare), post failed (inline under the composer, text preserved), someone else resolved or replied while open (the card refreshes in place with a *new* mark, never loses a draft).

**Constraints:** the diff pane is virtualised — thread cards are rows of variable height in the same list, so the design must accept that a card's height is measured, not fixed. Nothing in the card may need a hover to be discovered on a 760-dip window. The comment body font is the UI font; code in a note uses the code font at the diff's size.

---

## 4. Behaviours to specify with the visuals

- **Polling cadence and freshness.** Hosted data refreshes on repo switch, on Fetch, and on a timer (default off for the network, like auto-fetch). Every hosted surface shows *as of <time>* somewhere quiet and a stale mark past 10 minutes. Design that mark once.
- **Latency.** A panel opens instantly with cached rows and updates in place; never a spinner over old data. Skeleton rows only on the very first load.
- **Failure copy.** 401 → *Sign in again* (§A.4). 403 → *Your token needs the `api` scope for this*. 404 → *Project not found on gitlab.com — was it moved?* Offline → the stale mark, nothing else.
- **Local ↔ hosted naming.** An MR checked out locally is a normal branch; the only trace is the `!123` chip. Nothing in the app should say "MR branch".
- **Keyboard.** Panels take `/` for search, ↑↓ Enter, Esc; the MR panel needs `c` checkout and `o` open in browser at minimum, matching the Branches legend idiom.

---

## 5. Deliverable from Design

Static HTML/CSS mockups in the Nocturne idiom (same `styles.css` token file as the July handoff, Phosphor icons by name), one per surface A–K, every listed state, spacing and colour roles annotated by token name, key hints annotated. Plus a one-page **glyph sheet** for pipeline and MR status (E) and the review gutter (K) shown on Nocturne and on Solarized Light, since that vocabulary has to survive both.

Decided already (do not reopen): no new tab; the MR surface is an overlay panel; OAuth on gitlab.com, PAT on self-managed; every function in §2 is designed in this round.

Open questions for Design to answer in the mocks:

1. MR panel: master-detail in one 760 card, or list → detail as two pages?
2. Pipeline status in Branches: a column, or a glyph inside the LAST COMMIT cell?
3. Where does *GitLab accounts…* sit in the Repository tools menu, given the menu is repo-scoped and accounts are not?
4. Review threads (K): does the composer live between diff rows, or docked at the bottom of the pane with the anchored range highlighted above it? Between rows keeps context; docked keeps the virtualised list simpler. Show both if undecided.
