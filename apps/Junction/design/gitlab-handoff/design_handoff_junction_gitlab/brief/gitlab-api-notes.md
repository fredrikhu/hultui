# GitLab REST API v4 — notes for a desktop git client

Verified against docs.gitlab.com on 2026-09-12 unless flagged. Companion to `../GITLAB-DESIGN-BRIEF.md`.

## 1. Authentication

Docs: [oauth2](https://docs.gitlab.com/api/oauth2/), [REST auth](https://docs.gitlab.com/api/rest/authentication/), [scopes](https://docs.gitlab.com/security/tokens/access_token_scopes/), [glab auth](https://docs.gitlab.com/cli/authentication/)

- **PAT**: header `PRIVATE-TOKEN: <t>` (recommended) or `Authorization: Bearer <t>`. Default expiry 365 days, midnight UTC. `GET /personal_access_tokens/self` returns scopes/expires_at/active (probe on setup); `POST /personal_access_tokens/self/rotate` needs `api` or `self_rotate`.
- **Scopes**: `api` = full read/write API; `read_api` = read-only API (covers `/user`, todos, MR/pipeline/issue reads); `read_user` = `/user` only; `read_repository` / `write_repository` = git-over-HTTPS pull / pull+push. Every write (create/merge/approve MR, retry pipeline, create issue, mark todo done) needs `api`.
- **OAuth 2.0 PKCE**: `GET /oauth/authorize?client_id&redirect_uri&response_type=code&state&scope&code_challenge&code_challenge_method=S256`, then `POST /oauth/token` with `client_id, code, grant_type=authorization_code, redirect_uri, code_verifier` — no `client_secret`. Access token lives 7200 s; `grant_type=refresh_token` returns a new pair and invalidates the old one. Refresh-token expiry is not stated in docs. Revoke: `POST /oauth/revoke`. Device flow exists (`/oauth/authorize_device`).
- **Desktop precedent**: glab registers redirect `http://localhost:7171/auth/redirect`, **Confidential unchecked** (checked → `invalid_client`), scopes `openid profile read_user write_repository api`. Docs say HTTP redirects are "allowed for development"; loopback-with-variable-port is not documented — pin one port.
- **Self-managed**: OAuth app must be created per instance (no global client_id), so PAT is the zero-setup path; base URL `https://host/api/v4`. Custom CA is a client-side concern — nothing API-side.
- **Git credentials**: PAT or OAuth token as password, username any string; docs recommend `oauth2` for OAuth tokens (`https://oauth2:<token>@host/group/proj.git`). Needs `write_repository` to push. Junction can act as a credential helper feeding this.

## 2. Merge requests

Docs: [MR API](https://docs.gitlab.com/api/merge_requests/), [approvals](https://docs.gitlab.com/api/merge_request_approvals/), [discussions](https://docs.gitlab.com/api/discussions/), [notes](https://docs.gitlab.com/api/notes/), [local checkout](https://docs.gitlab.com/user/project/merge_requests/merge_request_troubleshooting/)

- **List**: `GET /projects/:id/merge_requests` (also `/merge_requests`, `/groups/:id/merge_requests`). Filters: `state` (opened/closed/merged/locked/all), `scope` (created_by_me/assigned_to_me/reviews_for_me/all), `author_id|author_username`, `reviewer_id|reviewer_username`, `assignee_id`, `source_branch`, `target_branch`, `search`, `draft`, `order_by`, `with_merge_status_recheck`.
- **Get**: `GET .../merge_requests/:iid` → `iid, title, description, state, draft, source_branch, target_branch, sha, diff_refs{base,head,start}, merge_status` (deprecated) / `detailed_merge_status`, `has_conflicts, head_pipeline, changes_count, user.can_merge, merge_commit_sha, squash_commit_sha, references, web_url`. `detailed_merge_status` values: `mergeable, checking, unchecked, preparing, not_open, draft_status, conflict, need_rebase, ci_must_pass, ci_still_running, not_approved, discussions_not_resolved, requested_changes, commits_status, merge_request_blocked, merge_time, status_checks_must_pass, approvals_syncing, jira_association_missing, security_policy_evaluation, security_policy_violations, locked_paths, locked_lfs_files`.
- **Create**: `POST /projects/:id/merge_requests` — `source_branch, target_branch, title` required; `description, draft, assignee_id|assignee_ids, reviewer_ids, labels, milestone_id, remove_source_branch, squash, allow_collaboration, target_project_id` (fork → upstream). Draft also via `Draft:` / `[Draft]` title prefix.
- **Update**: `PUT .../:iid` same fields + `state_event` (close/reopen/lock/unlock).
- **Merge**: `PUT .../:iid/merge` — `sha` (guard; mismatch → 409), `squash, squash_commit_message, merge_commit_message, should_remove_source_branch, merge_when_pipeline_succeeds, merge_after`. 405 not mergeable, 406 already merged, 422 validation. Newer `auto_merge` param and the cancel endpoint (`POST .../cancel_merge_when_pipeline_succeeds`) — **not verified**. Rebase: `POST .../:iid/rebase` (async; poll `rebase_in_progress`); `skip_ci` param unverified.
- **Approvals**: `POST .../:iid/approve` (optional `sha`, 409 on mismatch), `POST .../unapprove`, `GET .../approvals` → `approved, approvals_required, approvals_left, approved_by`. These are **Free**; `approval_state` and rules are Premium+.
- **Discussions**: `GET/POST .../:iid/discussions`; diff comments via `position[position_type=text]`, `position[base_sha|head_sha|start_sha]` (take them from the MR's `diff_refs` / `.../versions`), `position[new_path|old_path]`, and `position[new_line]` or `position[old_line]` for a single line. Reply: `POST .../discussions/:did/notes`; resolve: `PUT .../discussions/:did` `resolved=true`. Plain `notes` API: `GET/POST .../:iid/notes` (`system` flag marks automated entries).
- **Multi-line comments** (verified): add `position[line_range][start]` and `position[line_range][end]`, each with `line_code` (required, `<sha1 of file path>_<old_line>_<new_line>`, e.g. `adc83b19…_5_5`), `type` (`new` for added lines, else `old`), and optional `old_line` / `new_line`. A range cannot mix sides.
- **Draft notes / reviews** (verified, Free): `GET/POST /projects/:id/merge_requests/:iid/draft_notes`, `GET/PUT/DELETE .../draft_notes/:draft_note_id`, `PUT .../draft_notes/:draft_note_id/publish`, `POST .../draft_notes/bulk_publish`. Create takes `note` (required) plus `position` (same shape as above), `commit_id`, `in_reply_to_discussion_id`, `resolve_discussion`. This is the "Start review … Submit review" flow.
- **Diffs/commits**: `GET .../:iid/diffs` (`changes` deprecated since 15.7), `.../versions`, `.../commits`, `.../pipelines`; `POST .../:iid/pipelines` triggers an MR pipeline.
- **Local checkout**: add `fetch = +refs/merge-requests/*/head:refs/remotes/origin/merge-requests/*`, then `git checkout origin/merge-requests/<iid>`. Works for private forks. Head ref is deleted 14 days after close/merge.

## 3. Pipelines and jobs

Docs: [pipelines](https://docs.gitlab.com/api/pipelines/), [jobs](https://docs.gitlab.com/api/jobs/), [commits](https://docs.gitlab.com/api/commits/), [status checks](https://docs.gitlab.com/api/status_checks/), [merge trains](https://docs.gitlab.com/api/merge_trains/)

- `GET /projects/:id/pipelines` filters `ref, sha, status, scope (running/pending/finished/branches/tags), source, username, updated_after, order_by, sort`; `GET .../pipelines/latest?ref=`. Statuses: `created, waiting_for_resource, preparing, waiting_for_callback, pending, running, success, failed, canceling, canceled, skipped, manual, scheduled`.
- `POST /projects/:id/pipeline` `{ref, variables:[{key,value,variable_type}]}`; `POST .../pipelines/:pid/retry|cancel`.
- Jobs: `GET .../pipelines/:pid/jobs` (`scope`, `include_retried`); `GET /projects/:id/jobs/:jid/trace` (plain text); `POST .../jobs/:jid/retry|cancel|play`. `project/:id/jobs` has its own 600/min limit.
- Commit statuses: `GET /projects/:id/repository/commits/:sha/statuses` (`ref, name, pipeline_id, all`); `POST /projects/:id/statuses/:sha` states `pending/running/success/failed/canceled/skipped`. Also `GET .../commits/:sha/merge_requests` and `.../refs`.
- External status checks: `GET .../merge_requests/:iid/status_checks` — **Ultimate only**. Merge trains: `GET /projects/:id/merge_trains[/:target_branch]`, `POST .../merge_trains/merge_requests/:iid` (`sha, squash, auto_merge`) — **Premium+**.

## 4. Issues

Docs: [issues API](https://docs.gitlab.com/api/issues/), [closing pattern](https://docs.gitlab.com/user/project/issues/managing_issues/), [branch template](https://docs.gitlab.com/user/project/repository/branches/)

- `GET /projects/:id/issues` (`state, assignee_id|assignee_username, author_id, labels, milestone, search, in, scope, iids[], issue_type, order_by, sort, updated_after`); `GET .../issues/:iid` (`iid, title, state, labels, assignees, milestone, references, web_url, closed_by, merge_requests_count`); `POST /projects/:id/issues` (`title` + `description, assignee_ids, labels, milestone_id, due_date, confidential`); close via `PUT` `state_event=close`. `GET .../issues/:iid/related_merge_requests`, `.../closed_by`.
- Closing keywords (case-insensitive): Close/Closes/Closed/Closing, Fix*, Resolve*, Implement* + `#123`, `group/project#123`, or full URL; fires on merge/push to the **default branch**. Bare `#123` only cross-links.
- Branch-from-issue: no API; the web UI uses the project's branch name template, default `%{id}-%{title}` (slugified) — reproducible with `POST /projects/:id/repository/branches {branch, ref}` and an MR description pre-filled with `Closes #<iid>`.

## 5. Repository and project

Docs: [projects](https://docs.gitlab.com/api/projects/), [groups](https://docs.gitlab.com/api/groups/), [protected branches](https://docs.gitlab.com/api/protected_branches/), [repositories](https://docs.gitlab.com/api/repositories/), [branches](https://docs.gitlab.com/api/branches/), [tags](https://docs.gitlab.com/api/tags/), [releases](https://docs.gitlab.com/api/releases/), [avatar](https://docs.gitlab.com/api/avatar/)

- `GET /projects/group%2Fproject` (URL-encode `/`). Fields: `id, path_with_namespace, default_branch, web_url, http_url_to_repo, ssh_url_to_repo, avatar_url, forked_from_project, merge_method, squash_option, remove_source_branch_after_merge, only_allow_merge_if_pipeline_succeeds, permissions.project_access.access_level`. `GET /projects/:id/avatar` (15.4+).
- Lists: `GET /projects?membership=true&min_access_level=30&order_by=last_activity_at` (levels 10/20/30/40/50); `GET /groups` (`min_access_level, top_level_only`), `GET /groups/:id/projects?include_subgroups=true`.
- `GET /projects/:id/protected_branches` → `name` (wildcards), `push_access_levels`, `merge_access_levels` (0 none / 30 dev / 40 maint / 60 admin), `allow_force_push`, `code_owner_approval_required`. Branch objects also carry `protected`, `can_push`, `merged`, `default`.
- Compare: `GET /projects/:id/repository/compare?from&to&from_project_id&straight`; `GET .../repository/merge_base?refs[]`.
- Tags `GET/POST/DELETE /projects/:id/repository/tags` (`tag_name, ref, message`). Releases `POST /projects/:id/releases` (`tag_name` required; `ref` creates the tag if missing; `description, assets:links, milestones`). Forks: `POST /projects/:id/fork`, `GET /projects/:id/forks`.

## 6. Users and notifications

Docs: [users](https://docs.gitlab.com/api/users/), [todos](https://docs.gitlab.com/api/todos/), [events](https://docs.gitlab.com/api/events/), [snippets](https://docs.gitlab.com/api/snippets/)

- `GET /user` → `id, username, name, email, avatar_url, web_url` (`read_user` suffices). `GET /users?username=`.
- Todos are the mention/notification feed: `GET /todos?state=pending&type=MergeRequest&action=` (`assigned, mentioned, directly_addressed, review_requested, approval_required, build_failed, unmergeable, merge_train_removed, marked`); `POST /todos/:id/mark_as_done`, `POST /todos/mark_as_done`. No separate mentions API.
- `GET /events` / `GET /projects/:id/events` (`action, target_type, after, before`). Snippets: `GET/POST /snippets` (`title, files[{file_path,content}], visibility`); 300 creates/hour.

## 7. Limits and gotchas

Docs: [REST overview](https://docs.gitlab.com/api/rest/), [gitlab.com limits](https://docs.gitlab.com/user/gitlab_com/), [rate-limit headers](https://docs.gitlab.com/administration/settings/user_and_ip_rate_limits/), [GraphQL](https://docs.gitlab.com/api/graphql/)

- **gitlab.com**: 2,000 authenticated API req/min/user, 500 unauth/IP, 300/min raw. Headers `RateLimit-Limit/Observed/Remaining/Reset`, on 429 `Retry-After`; Projects/Groups/Users APIs return **no** headers. Self-managed limits are off by default (7,200/hour when on).
- **Pagination**: `per_page` max 100; `x-total`/`x-total-pages` dropped past 10,000 rows; offset capped at 50,000 by default. Keyset (`pagination=keyset`) covers projects, users, project issues, project jobs, tree — **not MRs or pipelines**; page those by `updated_after` + `order_by=updated_at`.
- Prefer integer project `id` after one path lookup; MRs/issues are addressed by `iid`.
- **GraphQL** `/api/graphql` (PAT as `Authorization: Bearer`, complexity 250, 100 nodes/page, 30 s timeout) fetches MR + `headPipeline` + `approvedBy` + `mergeStatusEnum` in one call; REST needs 3–4.
- **Webhooks** need a public receiver — a desktop app polls (todos + per-repo MR/pipeline list on a timer, ETag/`updated_after` to stay cheap).
- **Tiers**: Free — MRs, one assignee, optional approvals, commit statuses, pipelines, todos, releases, protected branches. Premium — multiple assignees/reviewers, required approval rules, Code Owners, merge trains, merged-results pipelines, "request changes" blocking. Ultimate — external status checks.

**Not verified**: `auto_merge` param name, cancel-MWPS endpoint path, rebase `skip_ci`, `GET .../merge_ref`, refresh-token lifetime, loopback redirect port flexibility. (Draft notes and multi-line positions were verified separately on 2026-09-12.)
