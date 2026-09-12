# Junction — GitLab integration implementation plan

**Design source:** `design/gitlab-handoff/design_handoff_junction_gitlab/` (Claude Design, 2026-09-12; `README.md` is the spec, `Junction Git Client v2.dc.html` the interactive prototype, `GitLab State Boards.dc.html` the states the prototype does not reach). The brief it answers is `GITLAB-DESIGN-BRIEF.md`; the API notes are `design/gitlab-api-notes.md`.
**Status:** Phase 0 built and green (2026-09-12, 74 new tests, 714 total). **Cursor:** ▶ Phase 1.

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

- [ ] Options dialog: 860×640 card, 200px rail, pane with title + *Done*, setting-row primitive (label, description, right-aligned control: toggle / chips / segmented). `Ctrl+,` and palette verbs *Options…* / *GitLab accounts…*. Only **GitLab accounts** and **Appearance** panes are real in this phase (Appearance is a move of the existing card); the other four rails entries are hidden until Phase 6.
- [ ] Accounts pane: account rows (avatar, name, `@user`, OAuth/PAT tag, host, expiry with the < 14 days warning register, scope tags, *Rotate token* / *Sign out* → confirm), OAuth waiting card with *Paste a token instead* / *Cancel*, self-managed card (URL, token, **Test** with idle/testing/verified/failed), detected-projects list.
- [ ] Repo ↔ project marks: sidebar row mark at 55% / 26%, Overview card, title bar, Repository settings *Hosted on* line with *Use another remote…*.
- [ ] First-run strip on History/Branches for a GitLab-backed repo with no account; dismissed once per host, persisted.
- [ ] Sign-out degradation: every hosted surface gated on `glActive(repo)` = project detected **and** account for its host.

**Done when:** signing in on gitlab.com (real) and on the lab instance (PAT) shows the right rows, a repo with no GitLab remote is pixel-identical to today (headless screenshot diff in the test), and sign-out clears the keyring entry.

**First moment a real instance matters:** the end of this phase. Recordings prove the parsing; only a live server proves token scopes, expiry, self-signed TLS copy, and subgroup paths.

### Phase 2 — Merge requests panel, read side (design §C list + detail, §E glyphs)

- [ ] Extract the repository-panel shell; MR panel = shell at 760 with a two-page stack (list → detail, Esc back then close).
- [ ] List page: filter chips with counts (Assigned to me · Reviewing · Mine · All open), This repo / Workspace segmented, `/` search, rows per the spec (iid · title · Draft · source chip → target · author · updated · pipeline glyph · approvals `1/2` · status word), legend, context menu, keys `c r o`.
- [ ] Detail page: header, meta row, Status / Pipeline / Approvals cards, description (the 40-line renderer), Changes list, Discussions list; **Checkout locally** = fetch `refs/merge-requests/<iid>/head` into `origin/merge-requests/<iid>` and create/update a local branch, with the busy label.
- [ ] Pipeline glyph vocabulary (one `PipelineGlyph(status)` in `Ui.cs`): toolbar head glyph + `!iid` pill, History rows, Branches LAST COMMIT cell (lock · subject · glyph · chip), Commit header line, Overview card footer. Commit statuses per SHA via one batched loader per repo; nothing drawn for `none`.
- [ ] Repository tools menu: *Merge requests…* (opens the accounts pane when signed out), *Create merge request…*.
- [ ] States: skeleton once, empty, no-account, offline stale mark with rows at 75%, 429 countdown.
- [ ] Refresh: on repo switch, on Fetch, on panel open, and the optional timer; *as of* stamps everywhere the design shows them.

**Done when:** the lab instance's MRs list, filter, open, and check out; the pipeline glyphs match `gitlab-runner`'s real results on the lab instance; unplugging the network shows the stale mark, not a spinner.

### Phase 3 — Create MR, protected branches, issues (design §D, §G, §F)

- [ ] Create MR dialog (560): push notice when unpushed, source → target select with the protected info line, title/description pre-fill rules, reviewers and labels pickers, Draft / Delete source / Squash defaulting from the project, footer summary, *Create* vs *Push and create*, `Ctrl+Enter`. Entries: Branches row button + menu + `m`, palette, tools menu, **push toast action** (4.5 s actionable toast is a new toast variant).
- [ ] Protected branches: cached per project; lock in the Branches cell; DialogSpec `Info` line on push / force-push (checkbox disabled with the reason) / delete branch / delete remote branch; *Try anyway* variant when nobody can push.
- [ ] Issues: synthesized link rules; composer `#` autocomplete popover above the field (open issues, `#iid  title`); closing-keyword hint line under Description.

**Done when:** an MR created from Junction on the lab instance carries the reviewers, labels and options chosen; a force-push to a protected branch is refused in the dialog with the server's rule, before git is asked.

### Phase 4 — Review threads in the diff (design §K)

- [ ] Review entry from MR detail (*Review*, `r`, a Changes row, a Discussion row): fetch `start_sha` and `head_sha`, open the Commit view as a range compare with the review header (← Merge request · pill · title · Reviewing tag · threads count with ▲▼ · Expand all · as-of); file tree thread pills and unresolved dots.
- [ ] Review gutter (22px): `+` on every row at 28%, lit on the caret row and in a range; thread marks (filled unresolved / outlined resolved).
- [ ] Caret + range: `↑↓`, `Shift+↑↓`, drag from `+`, Shift-click; band = accent-900 rows with a 3px inset edge; a range stops at a side boundary and says so; context lines count as *new*.
- [ ] Thread cards as variable-height rows in the virtualised diff list (collapsed line / open card with notes, Reply, Resolve/Unresolve, open-in-browser; resolved at 72%; focused ring). This is the one framework-level risk: `VirtualListElement` assumes one `ItemHeight`. Options: measure-on-realize with a height cache, or render cards as a separate overlay list interleaved by row index. Spike this **first** in the phase; do not build the rest on an unproven list.
- [ ] Composer card (between rows, indented 130px): header names the line/range and side, textarea, *Add comment* / *Add to review*, *Start review*, Cancel.
- [ ] Review mode: pending strip (`N comments pending · Submit review ▾ Comment/Approve/Request changes · Discard`), draft notes API for pending, bulk publish on submit, discard confirm when leaving.
- [ ] Positions: `position_type=text`, `base/head/start_sha` from `diff_refs`, `new_path/old_path`, single line `new_line|old_line`, ranges via `line_range` with `line_code = sha1(path)_old_new` per end.
- [ ] Outdated threads section under the diff; post-failed inline with text preserved; live refresh with a *new* mark that never loses a draft.
- [ ] Legend row and keys `c n p e Esc`.

**Done when:** on the lab instance, a two-line range comment authored in Junction appears at the same lines in the web UI, a thread resolved in the web UI shows resolved in Junction on refresh, and a draft review of three notes publishes as one review.

### Phase 5 — Acting on MRs, todos, Overview strip (design §C actions, §H, §J)

- [ ] Detail action row: Approve / Revoke, **Merge ▾** (Merge now / when pipeline succeeds → confirm with squash and delete-source checkboxes and the protected line; `sha` guard; 405/409 inline), Rebase when `need_rebase` (poll `rebase_in_progress`), Mark as draft / ready. Spinner in the button, never a panel spinner.
- [ ] Todos bell in the title bar (badge, hidden at 0 / signed out), palette-shaped list with verb tiles, *Done* per row, *Mark all done*, row → MR detail.
- [ ] Overview: strip (*You have N merge requests to review across M repositories · Review them →* opening the panel in workspace scope on Reviewing) and the card footer line (`main passed · 4 MRs · 2 need you`).

### Phase 6 — Clone from GitLab, branch from issue, remaining Options panes (design §I, boards, Options)

- [ ] Clone mode URL / GitLab switch: account picker, search, membership rows (`GET /projects?membership=true&min_access_level=30`), 6 skeleton rows once, empty copy.
- [ ] Branch from issue: pick an issue → `<iid>-<slug>` from the default branch, and an MR later pre-filled with `Closes #<iid>`.
- [ ] Options panes General (auto-fetch + interval chips, pull strategy, prune, GitLab refresh timer, confirm destructive), Code & diff (font, size, default view, wrap, whitespace, tab width), External tools (editor, terminal, merge tool, custom command), Keyboard shortcuts (generated from the same table the tooltips read, so it cannot drift). Retire the palette-only toggles these replace, keep the verbs.

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
