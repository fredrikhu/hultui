# Junction — GitLab integration implementation plan

**Design source:** `design/gitlab-handoff/design_handoff_junction_gitlab/` (Claude Design, 2026-09-12; `README.md` is the spec, `Junction Git Client v2.dc.html` the interactive prototype, `GitLab State Boards.dc.html` the states the prototype does not reach). The brief it answers is `GITLAB-DESIGN-BRIEF.md`; the API notes are `design/gitlab-api-notes.md`.
**Status:** Phases 0–5 built and green (2026-09-13; 119 GitLab tests). **Cursor:** ▶ Phase 6.

**Phase 5 as built, where it differs from the plan:** the action row lives in `JunctionApp.MergeRequestActions.cs` — checkout and review, a divider, then Approve/Revoke (hidden only when GitLab says the user cannot approve), **Merge ▾** (a popover: *Merge now* needs `mergeable`; a running pipeline offers only *Merge when pipeline succeeds*; anything else is a disabled button with the status word beside it, and auto-merge already set disables it too), Rebase when `need_rebase` (the call returns at once, so it polls the request until `rebase_in_progress` clears and reads `merge_error`), and the draft toggle as a retitle (`Draft:`, `[Draft]`, `WIP:` all count). The merge confirm is the DialogSpec with a second checkbox (`SecondCheckboxLabel`/`OnConfirm2`), pre-set from the request's own squash/remove flags then the project's, and it quotes the target's protection. Each button spins on its own (`_mrBusyAction`); a refusal is one line under the row until the next action or a page change. Todos are loaded per account host and refreshed with every hosted refresh; the bell sits left of the search box and is absent at zero; a row opens the request in whichever local repository hosts the project, else the browser. The Overview strip counts open requests naming the user as reviewer across the workspace and opens the panel in workspace scope on Reviewing, moving to a GitLab-backed repository first when the active one is not. Not verified on the lab: an actual merge and a rebase (the fake covers both forms; merging a lab request is the owner's call). Verified on the lab: approve, revoke, mark as draft, mark as ready, the bell (26 seeded todos) and the strip.

**Phase 4 as built, where it differs from the plan:** the framework spike came first and changed the framework rather than the app: `VirtualListElement` now keeps a row whose template gives it a `Key` across a rebuild (reconciled with its next realization, the reconciler leaving a self-realizing element's children alone), so the composer and the reply fields inside diff rows keep their focus and caret through the rebuild every keystroke causes. Thread and composer cards are rows of the same list, heights measured up front from the note bodies the way the wrapped diff measures lines, with a signature of the cards' content in the height key so a reply landing from the server re-measures. The review is a Commit-view range compare between the request's `diff_refs` **base** and head (the base is the merge base and is what GitLab diffs against; `start_sha` only rides in the position) — the single-request endpoint is fetched first because the list omits `diff_refs`, then the head under `refs/remotes/<remote>/merge-requests/N`. Reviewing forces unified. Line codes walk each hunk's old/new counters as GitLab's parser does; context lines send both numbers and no `type`, and the lab accepted a 2–4 range that way. *Request changes* goes through `bulk_publish`'s `reviewer_state` (REST has no other way); *Approve* publishes then approves. Resolve is immediate in either mode. Drafts on the server put the window in review mode whoever started it, so a review resumes across restarts. Outdated means "the line is not in this diff at this head" (no head-SHA comparison — GitLab shows older threads on their lines when the lines survive). Remote detection now reads the configured URL rather than the insteadOf-rewritten one, which the tests use to point gitlab.com at a local origin. Not built: the live-refresh *new* mark; drag-select of a range is there but a range cannot be started from the keyboard without a caret first.

**Phase 3 as built, where it differs from the plan:** the reviewer and label pickers are popovers listing the project's members and labels (the design's fixed pill set does not survive a real project); the target dropdown lists the hosting remote's branches with the default first. The title comes from the single commit's subject or the leaf (`PC-1042-refund-splitting` → `PC-1042: refund splitting`); the description lists the subjects as bullets and adds `Closes #N` when the leaf names an open issue or starts with GitLab's `<iid>-` form. `DialogSpec` gained an `Info` line and a disabled-checkbox reason, used by the push confirm (both the toolbar's and the Branches row's), and the delete-branch action now asks, offering `-D` only when there is unmerged work. The toast gained an action variant (4.5 s) used after every successful push. The composer's issue list is inline above the field rather than a floating popover, and reads the token at the caret with an end-of-text fallback. A GitLab-backed repository links bare `#N` and `!N` in subjects and messages without an `.issuetracker` file. Not built: the delete-remote-branch confirm (Junction has no remote-branch delete) and the composer-footer create-MR link (the toast's action covers it).

**Phase 2 as built, where it differs from the plan:** the repository-panel shell was **not** extracted — the merge-request panel is a two-page card of its own (`JunctionApp.MergeRequests.cs`) and the four existing panels are untouched; factoring the five is a later cleanup, not a prerequisite. Hosted data lives in `JunctionApp.GitLabData.cs`: per-repository loaders for project + protected branches, open merge requests, the latest hundred pipelines keyed by SHA (one call covers every branch tip and recent History row — no per-commit status calls), and per-MR approvals, discussions and diffs; every value is a `Hosted<T>` with an as-of stamp, and a failed reload keeps the previous value and adds the failure. A refresh happens on Fetch, on panel open and on a repository switch, but only when nothing is in flight and the data is older than two minutes — a refresh that pre-empted a first load superseded its result with the failure. A merge request whose pipeline GitLab did not link (a branch pipeline) falls back to the SHA lookup. Remote-tracking branch rows carry the `!iid` chip by leaf name, since after a clone the MR's branch usually exists only as `origin/<name>`. The action row holds only *Checkout locally* and the browser; approve/merge/rebase/draft come with Phase 5, the review entry with Phase 4. The driver gained `--gitlab` (borrows the real accounts; tokens stay in the OS store) so the panel could be rendered against the lab.

**Phase 1 as built, where it differs from the plan:** the Options rail lists only the two panes that exist (Appearance, GitLab accounts) rather than six entries with four dead ones; Phase 6 adds the rest. The first-run strip is on the History view only, as the prototype draws it, and its dismissal is persisted per host and cleared on sign-out. *Add gitlab.com account* steers to the token card with a toast until the OAuth application is registered (`GitLabOAuth.GitLabComClientId`). The waiting card's spinner is a static glyph for now. Sign-out is split so the revoke runs off the UI thread and the forgetting on it — the settings list is read by every build. The bundled Phosphor set gained the 26 GitLab-related glyphs (`scripts/phosphor-icons.txt`).

**Phase 0 as built, where it differs from the plan below:** the code lives in `apps/Junction/GitLab/` (namespace `Junction.GitLab`), not `Git/Hosting/`. The credential store went into the framework as designed — interface and in-memory/null stores in `HultUI.Core.Platform`, the Windows P/Invoke store as `HultUI.CredentialStore.Default` in the umbrella package, because `HultUI.Core` forbids unsafe code and `LibraryImport` needs it. Meridian's own `Keyring.cs` was **left in place**: `Meridian.Core` has no framework dependency and taking one for sixty lines of P/Invoke is the wrong trade. `HttpEngine` was not lifted either — the client classifies the four network failures in a dozen lines off `HttpRequestError`, and the rest of the engine is Meridian's. The OAuth loopback is a raw `TcpListener` rather than `HttpListener` (http.sys refuses non-elevated URL bindings on Windows).

## 0. What came back, against the brief

Everything asked for is designed, all four open questions are answered, and the design added one thing the brief did not ask for.

**Answered decisions (do not reopen):** MR panel is two pages in one 760-wide card (list → detail, Esc steps back); pipeline status sits inside the Branches LAST COMMIT cell, no new column; the review composer sits between diff rows, indented to the code column; *GitLab accounts* lives in a new **Options dialog**, and the Repository tools menu gains only *Merge requests…* and *Create merge request…*.

**Scope the design added:** a full Options dialog (860×640, rail with General · Appearance · Code & diff · External tools · GitLab accounts · Keyboard shortcuts, opened by `Ctrl+,` and the palette, no gear in the chrome). The Appearance card moves into it. This resurrects `OPTIONS_DIALOG_PLAN.md`, which was never built. Accounts need a home, so the shell and the GitLab pane are on the critical path; the other four panes are not, and are scheduled last.

**Things the prototype fakes that the app must not:** busy states on timers (600–1200 ms), seeded MRs and pipelines, `token < 20 chars → 401`. Toast duration is real: 1.7 s, 4.5 s when the toast carries an action.

**Two design details that need an engineering decision before they are built:**

1. The review diff is **unified only** in the prototype (split view is on a board). Ship unified first; split follows once thread cards work as variable-height rows in the virtualised list.
2. The MR detail renders the description as "plain paragraphs, `- ` bullets, blank lines as gaps, backticks stripped". Junction has no markdown renderer; that minimum is a 40-line function, not a dependency. Do not pull one in.

## 1. Reuse before writing

| Need | Exists today | Plan |
|---|---|---|
| HTTP with timeouts, TLS failure classification, trusted-thumbprint override | `apps/Meridian/Meridian.Core/HttpEngine.cs` (390 lines) | Do **not** reference Meridian.Core from Junction — one app should not depend on another. Lift the handler setup + failure classification into `src/HultUI.Net/` (new, small) or copy the ~80 lines Junction needs. Decide at Phase 1 by measuring how much of HttpEngine is Meridian-specific (spill files, phase timings are). |
| OS credential store | `apps/Meridian/Meridian.Core/Keyring.cs` — `IKeyring`, `WindowsCredentialKeyring` (CredReadW/CredWriteW P/Invoke, AOT-safe) | Promote to `src/HultUI.Platform/Keyring.cs` (or the same new project) with Junction's key scheme `junction|gitlab|<host>`. macOS/Linux stores are a later slice; the interface already returns false when there is none. |
| Overlay panel shape (z 42, 640 wide) | `JunctionApp.Worktrees.cs` and siblings | The MR panel is the same shell at 760 wide with a page stack. Extract `BuildRepositoryPanel(title, subtitle, toolbar, body)` first — four copies exist, a fifth is the moment to factor. |
| Prompt / confirm with checkbox + info line | `DialogSpec` (`JunctionApp.Dialog.cs`) | Add an optional `Info` line (icon + text in a `--color-bg` box) for the protected-branch copy; add a second checkbox slot (Squash / Delete source). |
| Row grid, chips, tags, per-row icon actions | `Ui.cs`, Branches/Tags grids | Reuse verbatim. |
| Palette-shaped list (600 wide) | `JunctionApp.Palette.cs` | Todos list reuses the card, rows and key handling with its own item source. |
| Anchored popover | sidebar `+`, stash | Composer `#` autocomplete anchors above the field with the same popover. |
| Range compare in the Commit view | `BuildRangeCompareView`, `GetRangeDiff(base, head)` | Review mode is a range compare between the MR's `start_sha` and `head_sha` with a review gutter added. Fetch both refs first. |
| Issue links | `Git/IssueTracker.cs`, `JunctionApp.Issues.cs` | Add a synthesized rule set for GitLab-backed repos (`#\d+`, `group/project#\d+`, `!\d+`) merged after the file's rules. |
| Async, cached, per-entity data | `KeyedAsyncLoader<TKey,T>` | Every hosted collection (projects, MRs, pipelines, threads, todos) is a keyed loader with an "as of" stamp; the stale mark reads the stamp. |
| Background loop with a settle and a lock check | auto-fetch in `JunctionApp.RemoteOps.cs` | The GitLab refresh timer is the same loop shape, separate switch (Options › General › *Refresh GitLab data on a timer*). |

## 2. Phases

Each phase leaves the app shippable. Tests live in `tests/Junction.Tests` (xunit, headless sessions); the API client gets its own fixture that replays recorded JSON so the suite never touches the network.

### Phase 0 — foundations (no UI)

- [x] `GitLab/GitLabRemote.cs`: detect the host from `origin`'s fetch URL (HTTPS and SSH forms, custom ports, subgroups) → `(host, scheme, port, projectPath)`, plus the API and web roots. Pure, tested against a table of URLs.
- [x] `GitLab/GitLabClient.cs` + `GitLabModels.cs` + `GitLabResult.cs`: `HttpClient` per host, `PRIVATE-TOKEN` or `Bearer`, JSON via a source-generated snake_case `GitLabJson` context (AOT-safe), pagination (`per_page=100`, follows `x-next-page`, page cap, `updated_after` filters), 429 → `Retry-After`, every status → a typed `GitLabFailure` with the design's copy behind `UserMessage`. Read endpoints for user, token probe, project, protected branches, MRs (list/get/approvals/discussions/draft notes), pipelines, commit statuses, issues, todos, membership projects. Writes come with the phases that need them.
- [x] Credential store in the framework (`ICredentialStore`, `InMemoryCredentialStore`, `NoCredentialStore`, `HultUI.CredentialStore.Default` → Windows Credential Manager); `GitLabAccountData` (host, web root, user, kind, expiry, scopes, client id) persisted in settings **without** the token; `GitLabAccounts` owns sign-in, refresh, sign-out and per-host clients.
- [x] OAuth PKCE (`GitLab/GitLabOAuth.cs`): loopback listener on `localhost:7171/auth/redirect`, `S256` challenge, code exchange, refresh, revoke on sign-out, state check, denied/forged/no-application outcomes. **The gitlab.com client id is still the empty constant** `GitLabOAuth.GitLabComClientId` — registering the application is a one-time manual step (User settings → Applications, redirect `http://localhost:7171/auth/redirect`, *Confidential* unchecked, scopes `api read_user`).
- [x] PAT flow: `GET /user` then `GET /personal_access_tokens/self`; verified / refused / read-only / not-GitLab outcomes are the design's states; an OAuth token pasted as a PAT (404 on the probe) still signs in.
- [x] `tests/Junction.Tests/GitLab/`: `FakeGitLabHandler` replaying 15 embedded recordings, 74 tests across remote parsing, client, OAuth (including the whole dance on a free port), accounts (settings JSON never contains the secret), and the real Windows store under a throw-away name.

**Done:** a test signs a fake account in, lists MRs across two recorded pages, and the serialized settings contain no token.

### Phase 1 — Options shell + GitLab accounts pane (design §A, Options)

- [x] Options dialog (`JunctionApp.Options.cs`): 860×640 card, 200px rail, pane with title + *Done*, setting-row primitives (row, toggle, chip). `Ctrl+,` and palette verbs *Options…* / *GitLab accounts…* / *Appearance: colour scheme…*. Appearance moved in from its own overlay (`JunctionApp.Appearance.cs` is the pane now).
- [x] Accounts pane (`JunctionApp.GitLabAccounts.cs`): account rows (avatar, name, `@user`, OAuth/PAT tag, host, expiry with the < 14 days warning register, scope tags, *Rotate token* / *Sign out* → confirm), the expired/refused row variant (*Sign in again* / *Remove*), OAuth waiting card with *Paste a token instead* / *Cancel*, self-managed card (URL, masked token, **Test** with idle/testing/verified/failed and the design's three failure sentences, **Add account**), detected-projects list.
- [x] Repo ↔ project marks (`JunctionApp.GitLabMarks.cs`): `GitLabRemoteFor` (origin, or the chosen remote, or the only one; host must read as GitLab), sidebar row mark at 55% / 26% with the two tooltips, Overview card, title bar, Repository settings *Hosted on* line with *Use another remote…* and the *add one in Options* link.
- [x] First-run strip on History for a GitLab-backed repo with no account; dismissed once per host, persisted (`GitLabDismissedHosts`), cleared on sign-out.
- [x] Sign-out degradation: `GitLabActive(path)` = project detected **and** account for its host; a repo without a GitLab remote renders no mark and no strip (tested).

**Done:** the token flow (Test → verified → Add account → row, mark lit, strip gone, secret in the store, identity in settings) and sign-out (confirm → forgotten, store empty, strip back) are covered end to end in `OptionsAccountsTests` against recordings. **Still to do by hand on the lab:** one sign-in with Dana's token at `http://gitlab.home`, and one on gitlab.com once the OAuth application exists.

**First moment a real instance matters:** the end of this phase. Recordings prove the parsing; only a live server proves token scopes, expiry, self-signed TLS copy, and subgroup paths.

### Phase 2 — Merge requests panel, read side (design §C list + detail, §E glyphs)

- [x] MR panel: a 760-wide two-page card (list → detail, Esc back then close). The shell extraction was skipped — see the as-built note.
- [x] List page: filter chips with counts (Assigned to me · Reviewing · Mine · All open), This repo / Workspace segmented, `/` search, rows per the spec (iid · title · Draft · source chip → target · author · updated · pipeline glyph · approvals `1/2` · status word from `detailed_merge_status`), legend, context menu (Checkout locally — Open in browser · Copy !iid link), keys `/ ↑↓ ↵ c o Esc`.
- [x] Detail page: header, meta row, Status / Pipeline / Approvals cards with the design's reason lines, description (paragraphs, bullets, gaps, backticks stripped), Changes list with thread-count pills, Discussions list with file · line(s); **Checkout locally** fetches `refs/merge-requests/<iid>/head` into `refs/remotes/<remote>/merge-requests/<iid>` and creates the source branch on it, or checks it out and fast-forwards, refusing to rewrite a diverged one (`MergeRequestCheckoutGitTests` against a real local origin).
- [x] Pipeline glyph vocabulary (`Ui.PipelineMark` / `PipelineGlyph` / `PipelineWord`): toolbar head glyph + `!iid` pill, History rows, Branches LAST COMMIT cell (lock · subject · glyph · chip), Commit header line, Overview card footer (`main passed · N MRs · M need you`). One pipelines call per repository keyed by SHA; nothing drawn for none.
- [x] Repository tools menu: *Merge requests…* (opens the accounts pane when signed out) and the palette verb. *Create merge request…* comes with Phase 3.
- [x] States: skeleton once, empty copy per filter, failure line (offline / 429 / 401) with the rows kept and dimmed, *as of* stamps on the header and detail.
- [x] Refresh: on Fetch, on panel open and on a repository switch when nothing is in flight and the data is older than two minutes. The timer waits for the Options General pane (Phase 6).

**Done:** the lab instance's five merge requests list, filter, open and read correctly with the glyphs from `gitlab-runner`'s real results (rendered through the driver with `--gitlab`); a failed reload keeps the rows and says how old they are (tested against recordings). **Not yet checked by hand:** *Checkout locally* against the lab — the git half is covered by tests on a local origin.

### Phase 3 — Create MR, protected branches, issues (design §D, §G, §F)

- [x] Create MR card (`JunctionApp.CreateMergeRequest.cs`, 560): push notice when unpublished or ahead, source → target dropdown with the protected info line, title/description pre-fill, reviewer and label pickers, Draft / Delete source / Squash defaulting from the project (applied late when the project loads after the card), footer summary, *Create* vs *Push and create*, `Ctrl+Enter`, error line for 409/403. Entries: Branches row button + menu (*Create merge request…* / *Open merge request !N*) + `m`, palette, tools menu, the **push toast action**.
- [x] Protected branches: `PushGuard` feeds the push confirm's `Info` line, greys the lease with *— not allowed on this branch*, and turns the primary into *Try anyway* where nobody can push; delete branch asks with the rule and a `-D` checkbox only when there is unmerged work.
- [x] Issues: synthesized `#N` / `!N` link rules for GitLab-backed repos; composer `#` list of open issues (↑↓ ↵ Esc, `#iid ` inserted at the token); closing-keyword hint under Description with the issue's title.

**Done:** the card sends the form GitLab expects (source, target, title, draft, description, squash, remove-source — tested against a recorded POST), the push confirm quotes *main is protected on gitlab.com — maintainers can push, force push is not allowed.* with the lease greyed, the composer offers and inserts open issues and explains a closing keyword. **Checked on the lab by rendering:** the push guard on `main`, the card on an unpublished branch, the autocomplete. **Not yet done by hand:** creating an MR on the lab from the card.

### Phase 4 — Review threads in the diff (design §K)

- [x] Review entry from MR detail (*Review*, `r`, a Changes row, a Discussion row): fetch the request's `diff_refs` and its head, open the Commit view as a base…head compare with the review header (← Merge request · pill · title · Reviewing tag · threads count with ▲▼ · Expand all · as-of); file tree thread pills and unresolved dots.
- [x] Review gutter (22px): `+` on every row at 28%, lit on the caret row and in a range; thread marks (filled unresolved / outlined resolved).
- [x] Caret + range: `↑↓`, `Shift+↑↓`, drag from `+`, Shift-click; band = accent-900 rows with a 3px inset edge; a range stops at a side boundary and says so; context lines count as *new*.
- [x] Thread cards as variable-height rows in the virtualised diff list (collapsed line / open card with notes, Reply, Resolve/Unresolve, open-in-browser; resolved at 72%; focused ring). Spiked first: heights measured up front (`RowHeights`, already in the framework since the wrapped diff), and keyed-row retention added to `VirtualListElement` so a field inside a row keeps focus — `tests/HultUI.Headless.Tests/VirtualListKeyedRowTests.cs`, `ReviewTests.AThreadCardIsExactlyAsTallAsTheRowTheListGivesIt`.
- [x] Composer card (between rows, indented 130px): header names the line/range and side, textarea, *Add comment* / *Add to review*, *Start review*, Cancel.
- [x] Review mode: pending strip (`N comments pending · Submit review ▾ Comment/Approve/Request changes · Discard`), draft notes API for pending, bulk publish on submit, discard confirm; leaving with drafts asks and offers to discard them.
- [x] Positions: `position_type=text`, `base/head/start_sha` from `diff_refs`, `new_path/old_path`, single line `new_line|old_line`, ranges via `line_range` with `line_code = sha1(path)_old_new` per end.
- [x] Outdated threads section under the diff; post-failed inline with text preserved. Not built: live refresh with a *new* mark (a refresh never loses a draft — the composer's text lives in state — but nothing marks what is new).
- [x] Legend row and keys `c n p e Esc`.

**Done when:** on the lab instance, a two-line range comment authored in Junction appears at the same lines in the web UI, a thread resolved in the web UI shows resolved in Junction on refresh, and a draft review of three notes publishes as one review. **Checked 2026-09-13 on the lab:** a range comment on README.md lines 2–4 of !1 came back from GitLab as a thread on lines 2–4; a reply and a resolve from Junction landed; three drafts published as one review (four threads, none pending). The web-UI half is the owner's to eyeball.

### Phase 5 — Acting on MRs, todos, Overview strip (design §C actions, §H, §J)

- [x] Detail action row: Approve / Revoke, **Merge ▾** (Merge now / when pipeline succeeds → confirm with squash and delete-source checkboxes and the protected line; `sha` guard; 405/409 inline), Rebase when `need_rebase` (poll `rebase_in_progress`), Mark as draft / ready. Spinner in the button, never a panel spinner.
- [x] Todos bell in the title bar (badge, hidden at 0 / signed out), palette-shaped list with verb tiles, *Done* per row, *Mark all done*, row → MR detail.
- [x] Overview: strip (*You have N merge requests to review across M repositories · Review them →* opening the panel in workspace scope on Reviewing) and the card footer line (`main passed · 4 MRs · 2 need you`, from Phase 2).

### Phase 6 — Clone from GitLab, branch from issue, remaining Options panes (design §I, boards, Options)

- [ ] Clone mode URL / GitLab switch: account picker, search, membership rows (`GET /projects?membership=true&min_access_level=30`), 6 skeleton rows once, empty copy.
- [ ] Branch from issue: pick an issue → `<iid>-<slug>` from the default branch, and an MR later pre-filled with `Closes #<iid>`.
- [ ] Options panes General (auto-fetch + interval chips, pull strategy, prune, GitLab refresh timer, confirm destructive, **todo filters** — which kinds ring the bell and whether only repositories in the open workspace count; asked for after Phase 5, where a busy instance's 26 seeded todos made the point), Code & diff (font, size, default view, wrap, whitespace, tab width), External tools (editor, terminal, merge tool, custom command), Keyboard shortcuts (generated from the same table the tooltips read, so it cannot drift). Retire the palette-only toggles these replace, keep the verbs.

## 3. Test instance — when and how

**As built (2026-09-13):** GitLab CE on a Proxmox VM at `http://gitlab.home` (192.168.0.54, plain HTTP, hosts entry on the Windows side), a docker-executor runner in the same VM with `clone_url = "http://gitlab.home"` and `extra_hosts = ["gitlab.home:192.168.0.54"]` so job containers resolve the name, seeded with `scripts/gitlab-lab-seed.sh`. Users `dana` (maintainer), `reviewer1`, `reviewer2`, `dev` (developer); group `acme`, subgroup `acme/platform`; projects `acme/core-api`, `acme/web-client`, `acme/platform/infra`. The tokens the script printed are kept by the owner, never in the repo. Two lessons: the root token needs `admin_mode` as well as `api` on a current GitLab, and a runner's `glrt-` token is not a personal access token.

**When.** Phase 0 runs on recordings. Set the instance up **during Phase 1**, so it is there the day accounts land; from Phase 2 on every "done when" is checked against it. Real gitlab.com is also needed once for the OAuth application registration and for one end-to-end sign-in; the lab instance covers everything else, including OAuth if an application is registered on it.

**Shape.** One Proxmox **VM** (not an LXC: GitLab Omnibus wants its own systemd, sysctl and a lot of RAM; it runs in a container but every upgrade fights it), plus the runner inside the same VM for a lab.

| Item | Value |
|---|---|
| Guest | Debian 12 or Ubuntu 24.04 LTS, cloud-init template if you have one |
| Sizing | 4 vCPU, **8 GB RAM** (6 is the floor, swap 2 GB), 40 GB disk on SSD-backed storage |
| Network | bridged, **static IP or DHCP reservation**, a DNS name the Windows box resolves (`gitlab.lab.example` or a hosts-file entry) |
| Package | GitLab **CE** Omnibus from `packages.gitlab.com` |
| TLS | plain HTTP for the lab is fine and avoids the self-signed-CA problem on the Windows side; if you want HTTPS, use Let's Encrypt via DNS-01 (`letsencrypt['enabled']` with a DNS plugin) rather than a self-signed cert |

**Install (on the guest):**

```bash
sudo apt update && sudo apt install -y curl openssh-server ca-certificates tzdata perl
curl -s https://packages.gitlab.com/install/repositories/gitlab/gitlab-ce/script.deb.sh | sudo bash
sudo EXTERNAL_URL="http://gitlab.lab.example" apt install -y gitlab-ce
sudo cat /etc/gitlab/initial_root_password    # valid 24 h; change it on first login
```

Then in `/etc/gitlab/gitlab.rb` set `gitlab_rails['gitlab_shell_ssh_port']` if 22 is taken, and `sudo gitlab-ctl reconfigure`. Keep `puma['worker_processes'] = 2` and `sidekiq['max_concurrency'] = 10` on 8 GB.

**Runner (pipelines must actually run, or the glyphs have nothing to show):**

```bash
curl -L https://packages.gitlab.com/install/repositories/gitlab/gitlab-runner/script.deb.sh | sudo bash
sudo apt install -y gitlab-runner docker.io
sudo usermod -aG docker gitlab-runner
sudo gitlab-runner register --url http://gitlab.lab.example --executor docker --docker-image alpine:3.20
```

Register it as an instance runner (Admin → CI/CD → Runners → New instance runner; paste the token when asked). A `.gitlab-ci.yml` with three stages (build / test / deploy, one `sleep 20` job each, one `when: manual`, one that fails on a branch named `*-fail`) gives every status the glyph sheet needs.

**Seed data the phases need** (script it against the API once the root PAT exists; keep the script in `scripts/gitlab-lab-seed.sh`):

- Users: `dana` (maintainer), `reviewer1`, `reviewer2`, `dev` (developer). Log in as `dev` from Junction for the protected-branch refusals.
- Group `acme` with subgroup `acme/platform`; projects `acme/core-api`, `acme/web-client`, `acme/platform/infra` (subgroup paths are where URL encoding bites).
- Protected `main` (maintainers push, no force push), protected `release/*` wildcard, one project where nobody can push.
- Labels, a milestone, ~15 issues (some closed), MRs in every `detailed_merge_status` you can force: mergeable, conflict (edit the same line on two branches), need_rebase, ci_still_running, not_approved (turn on required approvals — Free allows "any 1"), draft, discussions_not_resolved.
- Discussions: single-line and **multi-line** diff threads, one resolved, one outdated (push a new head after commenting), a pending draft review left by `reviewer1`.
- Todos for `dana`: a review request, a mention, a failed pipeline.
- Tokens: a PAT with `api`, one with only `read_api` (the scope-error state), one expiring in 3 days (the warning register), and an OAuth application (`http://localhost:7171/auth/redirect`, non-confidential) to test the browser flow off gitlab.com.

**Windows side:** add the DNS name, and if HTTPS with a private CA is used anyway, import the CA into the machine store — `HttpClient` on Windows trusts the OS store, so no code changes. Clone the three projects into a Junction workspace called *Lab*.

**Snapshot the VM** after seeding. Every destructive test (merge, rebase, delete branch) then rolls back in seconds instead of re-seeding.
