# Handoff: Junction — GitLab integration + Options dialog

**Date:** 2026-09-12 · **Design system:** Nocturne (`styles.css` in this folder is the token sheet) · **Answers:** `brief/GITLAB-DESIGN-BRIEF.md` §2–§5

## Overview

This round adds the first network client to Junction: GitLab accounts, merge requests (list, detail, checkout, create, approve/merge/rebase/draft), pipeline status glyphs, issue links and closing references, protected-branch awareness, review threads inside Junction's own diff pane, a todos bell, and an Overview strip for cross-repo review load. It also adds the long-planned **Options dialog** (rail + pane) and relocates the Appearance card into it. Everything is designed against the September state of the app described in `brief/DESIGN-HANDOVER.md`, not the July prototype.

## About the design files

The files here are **design references built in HTML** (Design Components on a small runtime, `support.js`). They are not production code. Recreate them in the HultUI / Junction desktop codebase using its existing views, panels, `DialogSpec`, row grid, chip, toast and palette implementations. Where the prototype fakes something (timers for busy states, seeded data), replace with the real API calls listed in `brief/gitlab-api-notes.md`.

- `Junction Git Client v2.dc.html` — interactive prototype. Open it in a browser; everything below marked **live** can be clicked through.
- `GitLab State Boards.dc.html` — static boards: glyph sheet on Nocturne and Solarized Light, token/MR/review states the prototype does not simulate, Tier 2 surfaces (clone browser, branch-from-issue), protected confirms, and written answers to the brief's four open questions.
- `DiffBody.dc.html` — the plain diff renderer the prototype uses outside review mode (unchanged from July).
- `styles.css` — Nocturne tokens and component classes. Every colour, radius, spacing and shadow below is a token from this file unless a literal is given.

## Fidelity

**High-fidelity.** Layout, spacing, copy, states and keyboard behaviour are final. Colours are named by role so the eleven schemes render them; the few literals (`#93c98d` success, `#cf9494` failure, `#e0a76a` warning, diff add/del tints) map to the app's existing diff-add / diff-del / warning roles.

## Demo state (prototype)

Signed in to gitlab.com as Dana Osei (`@dosei`, OAuth). `core-api`, `web-client`, `analytics-service` are GitLab-backed on gitlab.com; `infra` is on `gitlab.acme.internal` (no account → greyed mark, no hosted data); `mobile-app` and `design-system` have no GitLab remote and must look exactly like today's app. Sign out in Options › GitLab accounts to see the signed-out degradation and first-run strip.

## Decisions (do not reopen)

| Question | Answer |
|---|---|
| MR panel layout | **Two pages** in one 760-wide panel: list → detail with ← back; Esc steps back a page, then closes. |
| Pipeline status in Branches | **Inside the LAST COMMIT cell**, after the subject: lock · subject · pipeline glyph · `!iid` chip. No new column. |
| Where *GitLab accounts…* lives | **Options › GitLab accounts** (+ palette verb + the first-run strip's Connect). Repository tools menu stays repo-scoped and gains only *Merge requests…* and *Create merge request…*. |
| Review composer | **Between diff rows**, under the anchored line/range, indented to the code column (130px in the prototype). |
| Options entry | **Palette only** (*Options…*, *GitLab accounts…*, *Appearance: colour scheme…*) plus `Ctrl+,`. No gear in the chrome. |
| Deliverable split | Tier 1 interactive; Tier 2 + every-state boards static. |

---

## Screens / surfaces

Measurements are at 100% scale in a 1440×900 window. Sidebar 266 / rail 58; title bar 40; toolbar 52.

### 0. Chrome changes

- **Title bar**: breadcrumb `Junction · repo [gitlab mark] · branch · workspace` left; right: **todos bell** (30×26, 1px `--color-divider` border, accent-700 badge with accent-100 count, hidden when 0 or no account) · *Search & commands* `Ctrl P` · Windows caption glyphs.
- **Toolbar**: 7 tabs (padding 5px 7px, 12px heading font, kbd digit) · spacer · **head pipeline glyph** (30px tall, 1px border, icon in the pipeline colour + `!iid` pill in accent-800/accent-100; tooltip carries `Pipeline #4021 · started 4 min ago · !412 <title>`; hidden when the repo is not GitLab-backed or signed out) · Fetch · Pull · Push · Stash · **Repository tools ▾**.
- **Repository tools menu**: Worktrees… · Submodules… · Remotes… — Reflog (recover work)… — *Merge requests…* · *Create merge request…* (last group only when the repo has a GitLab remote; *Merge requests…* opens the accounts pane instead when signed out).
- **Sidebar repo row**: 12px `ph-gitlab-logo` after the name at 55% text alpha; 26% when the host has no account; tooltip `acme/core-api on gitlab.com` / `Connect an account for <host> to use GitLab features`.

### A. Accounts — Options › GitLab accounts (live)

Pane header row: **Add gitlab.com account** (primary, starts OAuth) · **Add self-managed host…** (secondary) · right-aligned note *Tokens live in the Windows Credential Manager, never in the settings file.*

- **Account row** (padding 11px 12px, 1px divider border, `--color-bg`): 34px avatar circle (accent-800/100 initials) · name · `@username` · `OAuth`/`PAT` outline tag · second line mono host · expiry text · scope tags (`tag-neutral`, mono 9px). Actions: *Rotate token* (only when expiry < 14 days, expiry text turns `#e0a76a` with `ph-warning`) · *Sign out* (ghost → DialogSpec confirm).
- **OAuth waiting card**: accent-900 ground, accent-700 border, spinning `ph-circle-notch`; copy *Waiting for the browser… Finish signing in to gitlab.com in the window that just opened. Junction is listening on localhost:7171 for the redirect.* Buttons: *Paste a token instead* (ghost) · *Cancel*. Prototype auto-completes after 3.2 s.
- **Add self-managed host card**: Host URL (mono) · Personal access token (password field, paste-first) + **Test** button · scope note. States: idle · testing (spinner in the button, label *Testing…*) · verified (green row: *Verified — Dana Osei @dosei · expires in 9 days · scopes api, write_repository*, *Add account* enabled) · failed (red row with one of: not-a-GitLab-host / 401 with the token URL / read_api-only scope message). Prototype rule: token < 20 chars → 401; contains "read" → scope error; else verified.
- **Detected projects list** at the bottom: repo tile · name · gitlab mark · mono path · host · state (*connected* green / *no account for this host* muted).
- **Expired / revoked** row and the **inline "Sign in again" card** are on the state boards (A.4).

### B. Repo ↔ project marks (live)

Sidebar (above), Overview card path line (same mark), title bar, and **Repository settings** modal: read-only *Hosted on* line (`acme/core-api on gitlab.com · detected from origin`, *Use another remote…* ghost) with a *No account for <host> — add one in Options* line when applicable.

### C. Merge requests panel (live) — z 42, 760 wide, max-height 740

**List page.** Header: tile `ph-git-pull-request` · *Merge requests* · mono scope subtitle (`acme/core-api on gitlab.com` or `<workspace> · 3 GitLab repositories`) · *as of 2 min ago* · ✕. Filter row: chips **Assigned to me · Reviewing · Mine · All open** (each with a count; selected = accent border + 14% accent fill) · segmented **This repo / Workspace** · search field (`/`). Rows (padding 9px 12px, radius 8, selected accent-900): `!iid` mono accent-300 (40px) · title (+ `Draft` outline tag) · second line: [repo name in workspace scope ·] source prefix chip + mono leaf → mono target · author · updated · right cluster: pipeline glyph · `ph-seal-check 1/2` (green when met) · status word (112px, right-aligned, coloured: Mergeable green, Conflicts red, Pipeline running accent-300, others 62% text). Footer legend: `/ search · ↑↓ ↵ open · c checkout locally · o open in browser · N open`. Right-click row: Checkout locally · Review in the diff — Approve/Revoke · Open in browser · Copy link.

**Detail page.** Header: *← Merge requests* · `!iid` pill · `Open`/`Draft`/`Auto-merge set` outline tag · title · *Open in browser* · ✕; meta row: source chip → target · avatar author · *updated 12 min ago* · as-of. Body (gap 16): three cards in a `1.2fr 1fr 1fr` grid — **Status** (word + reason line), **Pipeline** (`#4021 · running`, clickable, plus build/test/deploy stage glyphs), **Approvals** (`1 of 2`, avatars or *Nobody yet*) — then **Description** (plain paragraphs; `- ` lines render as bullets, blank lines as 8px gaps, backticks stripped), **Changes · N files** (status letter · mono path · thread count pill; *Review in the diff →*), **Discussions · N** (filled/outlined chat icon · first note · mono `file · line` · author · time; click opens review focused on that thread). Action row: **Checkout locally** (primary; busy label *Fetching refs/merge-requests/412/head…*) · Review · | · Approve / Revoke approval · **Merge ▾** (disabled with the reason inline right when not mergeable or auto-merge set) · Rebase (only when `need_rebase`) · Mark as draft / ready. Every busy state is a spinner in the button. Legend: `c checkout · r review · a approve · o open in browser · Esc back`.

**Merge ▾** menu: *Merge now* · *Merge when pipeline succeeds* → DialogSpec confirm: title *Merge !412 into main*, body *source → target. The N commits are squashed into one and the source branch is deleted on origin.*, protected-branch info line, checkboxes *Squash commits* / *Delete source branch when merged* pre-set from the project.

**States** (boards C.5): skeleton rows on first load only; empty; no-account; offline stale mark (*Showing what was loaded 14 min ago*, rows at 75%); 429 (*asked us to slow down — retrying in 42 s*); action failures inline under the action row (409 → *Refresh and try again*, 405, 403 scope, 404 moved).

### D. Create merge request (live) — 560 wide

Entry: Branches row icon button (`ph-git-pull-request`, only when no open MR and not the default branch), row context menu, `m` key, palette, **push toast action**, tools menu. Fields: optional push notice (*PC-1042… has 4 unpushed commits — they are pushed to origin first.*), **Branches** (source chip + leaf → target `<select>` of remote branches, default = project default; protected info line under it), Title (pre-filled from the single commit subject, else `PC-1042: refund splitting` from the leaf), Description (commit list and/or `Closes #1042` from the issue number in the branch), Reviewers (avatar pills), Labels (pills), checks: Draft · Delete source branch when merged (project default) · Squash commits (project default). Footer: summary line (*draft · 2 reviewers · squash · delete source*) · Cancel · **Create merge request** / **Push and create merge request** (busy spinner). Result toast: *Created !413 · Open →*; the branch row now shows the chip. `Ctrl+Enter` submits.

### E. Pipeline glyphs (live)

One vocabulary, shape-coded: success `ph-check-circle` `#93c98d` · failed `ph-x-circle` `#cf9494` · running `ph-circle-notch` accent-300 spinning (1s linear) · pending `ph-clock` 55% · canceled `ph-prohibit` 45% · skipped `ph-skip-forward-circle` 45% · manual `ph-play-circle` 62% · none → nothing drawn. Places: toolbar (current head), History rows (after the stats, then the `!iid` chip when the SHA is an MR head), Branches LAST COMMIT cell, Commit header (`Pipeline #4021 · started 4 min ago`, clickable; *Merged by !398* when the commit came through an MR), MR rows/detail, Overview card footer (`main passed`). Light-scheme values on the boards.

### F. Composer issue autocomplete (live)

In Changes, typing `#` + digits/text in Summary or Description opens a popover **above** the field (surface, neutral-700 border, shadow-lg, padding 4): header *Open issues · acme/core-api*, rows `#1042  Split refunds across original tenders` (palette row shape at 12px). Picking replaces the token with `#1042 `. A closing keyword (`Closes/Fixes/Resolves/Implements #n`) shows the hint line *Closes #1042 ("…") will close the issue when this reaches main.* Only when the repo is GitLab-backed and signed in. History subjects ending in `(#1131)` render the ref as an accent underlined link.

### G. Protected-branch confirmations (live + boards)

DialogSpec confirm gains an info line (`--color-bg` box, `ph-lock-simple` accent-300): *main is protected on gitlab.com — maintainers can push, force push is not allowed.* Push on a protected branch: force checkbox disabled with *— not allowed on this branch*. Push where nobody can push: body *origin will reject a direct push…*, primary *Try anyway*. Delete branch: unmerged-work checkbox `-D`; delete remote branch variant on the boards. Branches cell shows the lock; toolbar Push uses the same path.

### H. Todos (live) — palette shape, 600 wide

Bell → list: 28px tile icon per verb (`review requested` eyes · `mentioned` at · `pipeline failed` x-circle · `assigned` user-plus · `approval required` seal-check) · uppercase accent verb + mono `!iid` · title · `project · time` · **Done** per row · *Mark all done* in the header. Click a row → MR detail. Hidden when zero or signed out.

### I. Clone from GitLab (boards)

Clone mode gains a **URL / GitLab** segmented switch; GitLab side: search + account picker, membership rows (tile · mono path · activity/group · visibility tag), then the existing target folder. Loading = 6 skeleton rows once; empty copy on the board.

### J. Overview hosted status (live)

Strip above the grid (surface card): gitlab mark · *You have 3 merge requests to review across 2 repositories.* · *as of 2 min ago* · **Review them →** (opens the panel in workspace scope, Reviewing filter). Each GitLab-backed card gains a footer line: default-branch pipeline glyph + `main passed` · `4 MRs` (link) · *2 need you* in accent-300.

### K. Review threads in the diff (live)

Entered from MR detail (*Review*, `r`, a Changes row or a Discussion row). Commit view header swaps to: *← Merge request* · `!412` pill · title · `Reviewing · N pending` tag when in review mode · *Open in browser*; second line: source chip → target · author · `4 threads · 2 unresolved` with ▲▼ (`p`/`n`) · *Expand all* (`e`) · as-of. File tree rows gain a thread-count pill and an accent dot for unresolved. Diff is unified and read-only.

- **Gutter**: 22px column left of the line numbers. Every row shows a `+` at 28% alpha (16×16, radius 4); on the caret row or inside a range it lights (accent border, accent-800 fill, accent-100 icon). Rows that anchor a thread show `ph-fill ph-chat-circle` accent-300 (unresolved) or `ph-chat-circle` at 45% (resolved) instead; click toggles the cards.
- **Caret**: 3px inset accent-500 edge on the focused row. `↑↓` move, `Shift+↑↓` extend a range, `c` opens the composer, `Esc` closes composer → clears range → exits review.
- **Range**: mousedown on `+` and drag, or ⇧-click a second row. Band = accent-900 row background with 3px accent inset edge; gutter cell accent-800. A range cannot mix sides (old/new) — extension stops at the boundary and the composer header says *· range stops at the side boundary*. Context lines count as *new*.
- **Thread card** (margin `4px 14px 8px 130px`, surface, neutral-700 border, radius-md, body font): collapsed line = mark · author · first note (ellipsis) · *resolved by X* · `2 notes · line 21 (new)` · *Show*. Open = header (*Thread on line 21 (new)*, `pending` accent tag, `resolved by` neutral tag, *Hide*) · notes (22px avatar, name, time, *not published yet* when pending, body pre-wrap 12.5px) · footer: Reply input (30px; Reply button appears when non-empty) · **Resolve / Unresolve** · open-in-browser icon. Resolved cards at 72% opacity. Focused thread gets a 1px accent ring.
- **Composer card** (same geometry, accent border): header *New comment on lines 23–25 (new)* · textarea (auto-focused, 3 rows) · **Add comment** (or **Add to review** in review mode) · *Start review* (ghost, comment mode only) · Cancel `Esc`.
- **Pending-review strip** (bottom of the diff pane, accent-900 with accent-700 top border): `ph-note-pencil` · *3 comments pending · only you can see them until you submit* · **Submit review ▾** (Comment · Approve · Request changes) · *Discard* (confirm). Leaving review with pending comments asks to discard.
- **Outdated threads**: listed after the diff under *Outdated threads · lines that no longer exist at this head* as dashed cards with an `outdated` tag and *was line 14 (new)*.
- Legend row under the pane: `↑↓ caret · ⇧↑↓ extend range · c comment · n p next/previous unresolved · e expand all · drag + or ⇧-click for a range`.
- Split view, post-failed and live-refresh (*new* mark) states are on the boards.

### Options dialog (live) — 860×640, z 66

Left rail 200px (`--color-bg`): *Options* title · sections **General · Appearance · Code & diff · External tools · GitLab accounts · Keyboard shortcuts** (row padding 8px 10px, selected accent-900 + 2px accent inset edge, accent-100 text) · footer *Settings save as you change them.* Right pane: section title + *Done* (ghost, accent-200); scrolling body padding 6px 22px 22px. Setting row = 12px vertical padding, divider below, label 13px/500 + 11.5px description at 52% alpha, control right-aligned (34×20 toggle: accent when on / neutral-700 off, 16px knob; chips = the include-bar chip; segmented = the diff Unified/Split style).

- **General**: Auto-fetch (toggle; interval chips 1/5/15/30 min appear indented when on) · Pull strategy (Merge / Rebase / Fast-forward only) · Prune on fetch · Refresh GitLab data on a timer (description explains as-of + 10-minute stale mark) · Confirm destructive actions.
- **Appearance**: intro line · 3-column scheme cards painted in their own colours (name + check, 5 swatch dots on the scheme's bg, description; selected = accent border + 1px ring) · Interface scale chips 80–200%.
- **Code & diff**: code font input + −/+ stepper + live preview line · Default diff view · Wrap long lines · Ignore whitespace · Tab width 2/4/8.
- **External tools**: Editor · Terminal · Merge tool chips · Custom command (`$file $line $repo`).
- **GitLab accounts**: §A.
- **Keyboard shortcuts**: two-column groups (Global · Diff · Branches · Merge requests · Review) of `kbd` + description rows.

### Tags (6) and Files (7) — parity views

Rebuilt to the handover's description so the toolbar shows its real width: Tags header (search · New tag) + TAG · COMMIT · CREATED · KIND · ACTIONS grid; Files header (`Files · sha · subject · N files · ▲▼`) + 300px tree + viewer with size/lines, Blame toggle (adds a 210px `sha author · time` column), open/reveal.

---

## Interactions & keys

| Where | Keys |
|---|---|
| Global | `1–7` views · `Ctrl+P` / `Ctrl+K` palette · `Ctrl+,` Options · `Esc` topmost overlay |
| Branches | `/ ↑↓ ↵ p l i` as before · **`m`** create/open MR |
| MR panel list | `/` search · `↑↓ ↵` · `c` checkout · `r` review · `o` browser · `Esc` close |
| MR detail | `c r a o` · `Esc` back to list |
| Review | `↑↓` caret · `⇧↑↓` range · `c` composer · `n p` unresolved · `e` expand all · `Esc` composer → range → exit |
| Create MR | `Ctrl+↵` submit · `Esc` cancel |
| Confirm | `↵` confirm · `Esc` cancel |

Busy states: 600–1200 ms spinners on the button (approve 600, checkout 900, create 900, merge 1100, rebase 1200). Toasts: 1.7 s, **4.5 s when actionable** (`Pushed X to origin · Create merge request →`, `Created !413 · Open →`, `Pushed · Open !412 →`).

## State model (prototype → app)

- `gl.accounts[]` `{host, name, username, kind: OAuth|PAT, expires, expiresDays, scopes[]}`; `glActive(repo)` = project detected **and** an account exists for its host — every hosted surface keys off this.
- `projects[repo]` `{path, host, defaultBranch, mergeMethod, squash, removeSource, protectedRules[{name (wildcard), push, merge, force}]}` from `GET /projects/:path` + `/protected_branches`.
- `mrs[repo][]` `{iid, title, source, target, author, mine, assigned, reviewing, draft, updated, pipeline, pipelineId, approvals{got,need,by[]}, status (detailed_merge_status), desc, changes, headSha, autoMerge}`.
- `pipelines[sha]` from commit statuses; `threads[iid][]` `{id, file, side, from, to, resolved, resolvedBy, outdated, notes[{who, when, body, pending}]}` — pending = draft notes.
- UI state: `mrPanel {page, filter, scope, q, sel, iid, busy}`, `createMr`, `review {iid, mode: comment|review, pending[], expanded{}, expandAll, caret, range{from,to,anchor,stopped}, composer{file, side, from, to, text}, replies{}, focusThread}`, `confirm`, `options {section}`, `opts`, `todos[]`.

## Design tokens (from `styles.css`)

Ground `--color-bg` #161826 · surface `--color-surface` · text `--color-text` #e9e9ed · accent #9184d9 with ramps `--color-accent-100…900`, `--color-neutral-100…900` · divider `--color-divider` · radii `--radius-md/lg` · shadows `--shadow-sm/md/lg` · fonts `--font-heading`/`--font-body` (Inter 500/400), code `ui-monospace, JetBrains Mono, Menlo, Consolas` 12px/1.65. Literals: success `#93c98d`, failure `#cf9494`, warning `#e0a76a`, danger text `#e39a9a`, danger border `#a06868`, danger tile `#4a2a2a`, diff add `rgba(120,178,116,.13)`, diff del `rgba(200,120,120,.13)`. Muted text = `color-mix(in srgb, var(--color-text) N%, transparent)` at 62/55/52/46/42/40 %. Icons: Phosphor regular, plus `ph-fill ph-chat-circle` for unresolved threads.

## Assets

Phosphor icons by name only (`ph-gitlab-logo`, `ph-git-pull-request`, `ph-seal-check`, `ph-chat-circle`, `ph-lock-simple`, `ph-bell`, `ph-eyes`, `ph-note-pencil`, `ph-circle-notch`, `ph-check-circle`, `ph-x-circle`, `ph-clock`, `ph-prohibit`, `ph-skip-forward-circle`, `ph-play-circle`). No images.

## Files

- `Junction Git Client v2.dc.html` — prototype (template + `class Component` logic in one file; the GitLab section of the logic starts at the comment `GitLab integration — accounts, projects, MRs, pipelines, review`).
- `GitLab State Boards.dc.html` — static boards.
- `DiffBody.dc.html`, `styles.css`, `support.js` — runtime dependencies for opening the files locally.
- `brief/` — the implementation brief, app handover and API notes this design answers.
