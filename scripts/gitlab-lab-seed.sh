#!/usr/bin/env bash
# Seeds a fresh GitLab CE lab instance with everything the Junction GitLab phases need:
# users at four access levels, a group with a subgroup, three projects with real history and
# CI, protected branches, labels, a milestone, issues, merge requests in several merge
# statuses, single-line and multi-line review threads (one resolved, one outdated), a pending
# draft review, todos, three personal access tokens for the test user, and an OAuth
# application for the loopback sign-in.
#
# Run it ON THE GITLAB VM (it needs git, curl, jq and a name that resolves), as a user with
# sudo not required:
#
#   sudo apt install -y jq git
#   export GITLAB_URL=http://gitlab.home
#   export GITLAB_TOKEN=glpat-...      # a root token with the api scope (never commit it)
#   bash gitlab-lab-seed.sh
#
# Runs once on a fresh instance. Snapshot the VM before and after. Re-running skips the
# users, groups and projects that already exist but will add duplicate issues and MRs.
#
# Everything it prints at the end (tokens, application id) is shown ONCE — save it.

set -euo pipefail

: "${GITLAB_URL:?set GITLAB_URL, e.g. http://gitlab.home}"
: "${GITLAB_TOKEN:?set GITLAB_TOKEN to a root personal access token with the api scope}"
API="$GITLAB_URL/api/v4"
HOST="${GITLAB_URL#*://}"
PASSWORD="${LAB_PASSWORD:-Junction-lab-1!}"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# ── helpers ───────────────────────────────────────────────────────────────────────────
api() {                       # api METHOD PATH [curl args...]  → body on stdout
  local method="$1" path="$2"; shift 2
  curl -sS -X "$method" -H "PRIVATE-TOKEN: $GITLAB_TOKEN" "$API$path" "$@"
}
api_as() {                    # api_as TOKEN METHOD PATH [curl args...]
  local token="$1" method="$2" path="$3"; shift 3
  curl -sS -X "$method" -H "PRIVATE-TOKEN: $token" "$API$path" "$@"
}
jqr() { jq -r "$1"; }
say() { printf '\n\033[1m== %s\033[0m\n' "$*"; }
urlenc() { jq -rn --arg s "$1" '$s|@uri'; }

# ── preflight: the token must be an administrator's PAT, or every step below prints null ──
ME=$(api GET /user) || { echo "Could not reach $API — is GITLAB_URL right and the instance up?" >&2; exit 1; }
if ! jq -e '.is_admin == true' <<<"$ME" > /dev/null 2>&1; then
  echo "GITLAB_TOKEN is not an administrator's personal access token." >&2
  echo "  GitLab said: $(jq -r '.message // .username // .' <<<"$ME")" >&2
  echo "  Use a root token (glpat-…) with the api and admin_mode scopes. A runner token (glrt-…) will not do." >&2
  exit 1
fi
echo "Signed in to $GITLAB_URL as $(jq -r .username <<<"$ME") (administrator)"
if ! api GET "/users?username=dana" | jq -e 'length == 0' > /dev/null; then
  echo "  note: user dana already exists — this is a re-run; users, groups and projects are reused, issues and MRs will be added again" >&2
fi

# ── users ─────────────────────────────────────────────────────────────────────────────
say "Users"
ensure_user() {               # ensure_user USERNAME "Full Name" [admin]
  local username="$1" name="$2" admin="${3:-false}" id
  id=$(api GET "/users?username=$username" | jqr '.[0].id // empty')
  if [ -z "$id" ]; then
    id=$(api POST /users \
      --data-urlencode "username=$username" --data-urlencode "name=$name" \
      --data-urlencode "email=$username@acme.example" --data-urlencode "password=$PASSWORD" \
      --data-urlencode "skip_confirmation=true" --data-urlencode "admin=$admin" | jqr .id)
    echo "  created $username (#$id)"
  else
    echo "  exists  $username (#$id)"
  fi
  echo "$id"
}
DANA=$(ensure_user dana "Dana Osei" | tail -1)
REV1=$(ensure_user reviewer1 "Maya Kline" | tail -1)
REV2=$(ensure_user reviewer2 "Tomas Brandt" | tail -1)
DEV=$(ensure_user dev "Sam Developer" | tail -1)

# Tokens for the people who write reviews: admins may mint a PAT for any user.
user_token() {                # user_token USER_ID NAME SCOPES [EXPIRES]
  local uid="$1" name="$2" scopes="$3" expires="${4:-}"
  local args=(--data-urlencode "name=$name")
  for s in $scopes; do args+=(--data-urlencode "scopes[]=$s"); done
  [ -n "$expires" ] && args+=(--data-urlencode "expires_at=$expires")
  api POST "/users/$uid/personal_access_tokens" "${args[@]}" | jqr .token
}
say "Tokens"
DANA_TOKEN=$(user_token "$DANA" "junction" "api write_repository" "$(date -u -d '+365 days' +%F)")
DANA_READONLY=$(user_token "$DANA" "ci-reader" "read_api")
DANA_EXPIRING=$(user_token "$DANA" "expiring-soon" "api" "$(date -u -d '+3 days' +%F)")
REV1_TOKEN=$(user_token "$REV1" "seed" "api")
REV2_TOKEN=$(user_token "$REV2" "seed" "api")
DEV_TOKEN=$(user_token "$DEV" "junction-dev" "api write_repository")
echo "  minted six"

# ── group + subgroup ──────────────────────────────────────────────────────────────────
say "Groups"
ensure_group() {              # ensure_group PATH NAME [PARENT_ID]
  local path="$1" name="$2" parent="${3:-}" full id
  full="$path"; [ -n "$parent" ] && full="$(api GET "/groups/$parent" | jqr .full_path)/$path"
  id=$(api GET "/groups/$(urlenc "$full")" | jqr '.id // empty')
  if [ -z "$id" ]; then
    local args=(--data-urlencode "path=$path" --data-urlencode "name=$name" --data-urlencode "visibility=private")
    [ -n "$parent" ] && args+=(--data-urlencode "parent_id=$parent")
    id=$(api POST /groups "${args[@]}" | jqr .id)
    echo "  created $full (#$id)"
  else
    echo "  exists  $full (#$id)"
  fi
  echo "$id"
}
ACME=$(ensure_group acme "Acme" | tail -1)
PLATFORM=$(ensure_group platform "Platform" "$ACME" | tail -1)
for uid_level in "$DANA:40" "$REV1:30" "$REV2:30" "$DEV:30"; do
  api POST "/groups/$ACME/members" --data-urlencode "user_id=${uid_level%%:*}" \
    --data-urlencode "access_level=${uid_level##*:}" > /dev/null 2>&1 || true
done

# ── projects with history ─────────────────────────────────────────────────────────────
say "Projects"
CI_YML='stages: [build, test, deploy]

build:
  stage: build
  image: alpine:3.20
  script:
    - echo building && sleep 20

test:
  stage: test
  image: alpine:3.20
  script:
    - echo testing && sleep 20
    - case "$CI_COMMIT_REF_NAME" in *-fail) echo "this branch is meant to fail" && exit 1;; esac

deploy:
  stage: deploy
  image: alpine:3.20
  when: manual
  script:
    - echo deploying
'

ensure_project() {            # ensure_project NAMESPACE_ID PATH "Description"  → project id
  local ns="$1" path="$2" desc="$3" id
  local full; full="$(api GET "/namespaces/$ns" | jqr .full_path)/$path"
  id=$(api GET "/projects/$(urlenc "$full")" | jqr '.id // empty')
  if [ -z "$id" ]; then
    id=$(api POST /projects --data-urlencode "path=$path" --data-urlencode "name=$path" \
      --data-urlencode "namespace_id=$ns" --data-urlencode "description=$desc" \
      --data-urlencode "visibility=private" --data-urlencode "initialize_with_readme=false" \
      --data-urlencode "squash_option=default_on" --data-urlencode "remove_source_branch_after_merge=true" \
      --data-urlencode "only_allow_merge_if_pipeline_succeeds=false" | jqr .id)
    echo "  created $full (#$id)" >&2
  else
    echo "  exists  $full (#$id)" >&2
  fi
  echo "$id"
}
CORE=$(ensure_project "$ACME" core-api "Orders, refunds, tenders.")
WEB=$(ensure_project "$ACME" web-client "The storefront.")
INFRA=$(ensure_project "$PLATFORM" infra "Runners, images, deploy scripts.")

git_seed() {                  # git_seed PROJECT_ID  — main + the branches the MRs need
  local pid="$1" path url dir
  path=$(api GET "/projects/$pid" | jqr .path_with_namespace)
  url="http://oauth2:$GITLAB_TOKEN@$HOST/$path.git"
  dir="$WORK/$(basename "$path")"
  if [ "$(api GET "/projects/$pid/repository/branches" | jq 'length')" != "0" ]; then
    echo "  $path already has history, skipping git" >&2; return
  fi
  git init -q -b main "$dir"
  git -C "$dir" config user.name "Dana Osei"
  git -C "$dir" config user.email "dana@acme.example"
  mkdir -p "$dir/src/api" "$dir/docs"
  printf '# %s\n\nSeeded for Junction.\n' "$(basename "$path")" > "$dir/README.md"
  printf '%s' "$CI_YML" > "$dir/.gitlab-ci.yml"
  printf 'namespace Api;\n\npublic class Orders\n{\n    public int Count() => 1;\n\n    public int Refunds() => 0;\n\n    public int Voided() => 0;\n\n    public int Tenders() => 1;\n}\n' > "$dir/src/api/Orders.cs"
  printf 'runner image: alpine:3.19\n' > "$dir/docs/runner.md"
  for n in 1 2 3 4 5 6 7 8; do printf 'line %s of a file that pads the diff context\n' "$n" >> "$dir/docs/notes.md"; done
  git -C "$dir" add -A && git -C "$dir" commit -qm "Initial import"
  git -C "$dir" commit -q --allow-empty -m "Wire CI (#1)"

  # release branch (protected by wildcard later)
  git -C "$dir" branch release/2.3.1

  # feature: two commits, a multi-line change in Orders.cs — the review-thread target
  git -C "$dir" checkout -qb feature/2.4.0/payments/PC-1042-refund-splitting
  printf 'namespace Api;\n\npublic class Orders\n{\n    public int Count() => 2;\n\n    public int Refunds() => Tenders() - Voided();\n\n    public int Voided() => 0;\n\n    public int Tenders() => 1;\n\n    public int Split(int refund)\n    {\n        var perTender = refund / Tenders();\n        return perTender;\n    }\n}\n' > "$dir/src/api/Orders.cs"
  git -C "$dir" commit -qam "PC-1042: split a refund across tenders"
  printf '# %s\n\nSeeded for Junction.\n\nSee PC-1042.\n' "$(basename "$path")" > "$dir/README.md"
  git -C "$dir" commit -qam "PC-1042: mention the refund split in the README"

  # wip: a draft
  git -C "$dir" checkout -q main
  git -C "$dir" checkout -qb wip/2.5.0/platform/PC-1120-event-sourcing-poc
  printf 'namespace Api;\n\npublic record OrderEvent(string Kind);\n' > "$dir/src/api/OrderEvent.cs"
  git -C "$dir" add -A && git -C "$dir" commit -qm "PC-1120: event sourcing POC"

  # chore: will conflict with main once main edits the same line
  git -C "$dir" checkout -q main
  git -C "$dir" checkout -qb chore/runner-image
  printf 'runner image: alpine:3.20\n' > "$dir/docs/runner.md"
  git -C "$dir" commit -qam "Bump runner image"

  # ci: the branch whose pipeline fails
  git -C "$dir" checkout -q main
  git -C "$dir" checkout -qb ci/nightly-fail
  printf 'nightly\n' > "$dir/docs/nightly.md"
  git -C "$dir" add -A && git -C "$dir" commit -qm "Nightly job"

  # stale: behind main, for need_rebase on a fast-forward project
  git -C "$dir" checkout -q main
  git -C "$dir" checkout -qb chore/stale-branch
  printf 'stale\n' > "$dir/docs/stale.md"
  git -C "$dir" add -A && git -C "$dir" commit -qm "A change made before main moved on"

  # main moves: conflicts with chore/runner-image, leaves chore/stale-branch behind
  git -C "$dir" checkout -q main
  printf 'runner image: alpine:3.21\n' > "$dir/docs/runner.md"
  git -C "$dir" commit -qam "Runner image to 3.21"

  git -C "$dir" push -q "$url" --all
  echo "  pushed $path" >&2
}
git_seed "$CORE"; git_seed "$WEB"; git_seed "$INFRA"

# infra merges fast-forward only, so a branch behind main reads need_rebase
api PUT "/projects/$INFRA" --data-urlencode "merge_method=ff" > /dev/null

# ── protected branches ────────────────────────────────────────────────────────────────
say "Protected branches"
for pid in "$CORE" "$WEB" "$INFRA"; do
  api DELETE "/projects/$pid/protected_branches/main" > /dev/null 2>&1 || true
  api POST "/projects/$pid/protected_branches" --data-urlencode "name=main" \
    --data-urlencode "push_access_level=40" --data-urlencode "merge_access_level=40" \
    --data-urlencode "allow_force_push=false" > /dev/null
  api POST "/projects/$pid/protected_branches" --data-urlencode "name=release/*" \
    --data-urlencode "push_access_level=0" --data-urlencode "merge_access_level=40" > /dev/null
done
# web-client: nobody pushes main directly, not even maintainers — the "try anyway" state
api DELETE "/projects/$WEB/protected_branches/main" > /dev/null
api POST "/projects/$WEB/protected_branches" --data-urlencode "name=main" \
  --data-urlencode "push_access_level=0" --data-urlencode "merge_access_level=40" > /dev/null
echo "  main (maintainers, no force push), release/* (no one) on all three; web-client main: no one"

# Optional approvals: Free tier accepts the count; enforcement rules are Premium.
for pid in "$CORE" "$INFRA"; do
  api POST "/projects/$pid/approvals" --data-urlencode "approvals_before_merge=1" > /dev/null 2>&1 || true
done

# ── labels, milestone, issues ─────────────────────────────────────────────────────────
say "Labels, milestone, issues"
for pid in "$CORE" "$WEB" "$INFRA"; do
  for lc in "payments:#9184d9" "backend:#5c6bc0" "infra:#8d6e63" "bug:#cf8f8f" "good first issue:#7fbf79"; do
    api POST "/projects/$pid/labels" --data-urlencode "name=${lc%%:*}" --data-urlencode "color=${lc##*:}" > /dev/null 2>&1 || true
  done
done
MILESTONE=$(api POST "/groups/$ACME/milestones" --data-urlencode "title=2.4.0" \
  --data-urlencode "due_date=$(date -u -d '+30 days' +%F)" | jqr .id)

ISSUE_IIDS=()
new_issue() {                 # new_issue PROJECT "title" "labels" [closed]
  local pid="$1" title="$2" labels="$3" closed="${4:-}" iid
  iid=$(api POST "/projects/$pid/issues" --data-urlencode "title=$title" \
    --data-urlencode "labels=$labels" --data-urlencode "milestone_id=$MILESTONE" \
    --data-urlencode "assignee_ids[]=$DANA" \
    --data-urlencode "description=Seeded issue. Mention its number in a commit message to link it." | jqr .iid)
  [ -n "$closed" ] && api PUT "/projects/$pid/issues/$iid" --data-urlencode "state_event=close" > /dev/null
  echo "$iid"
}
for pid in "$CORE" "$WEB" "$INFRA"; do
  new_issue "$pid" "Split refunds across original tenders" "payments,backend" > /dev/null
  new_issue "$pid" "Refund report is off by one day" "bug" > /dev/null
  new_issue "$pid" "Voided tenders are counted as refunds" "bug,payments" > /dev/null
  new_issue "$pid" "Document the runner image policy" "infra,good first issue" > /dev/null
  new_issue "$pid" "Old: migrate CI to stages" "infra" closed > /dev/null
done
echo "  five per project, one closed each; milestone 2.4.0"

# ── merge requests ────────────────────────────────────────────────────────────────────
say "Merge requests"
new_mr() {                    # new_mr TOKEN PROJECT SOURCE "title" "desc" [extra curl args...]
  local token="$1" pid="$2" source="$3" title="$4" desc="$5"; shift 5
  api_as "$token" POST "/projects/$pid/merge_requests" --data-urlencode "source_branch=$source" \
    --data-urlencode "target_branch=main" --data-urlencode "title=$title" \
    --data-urlencode "description=$desc" --data-urlencode "remove_source_branch=true" "$@" | jqr .iid
}
declare -A MR_REFUND MR_DRAFT MR_CONFLICT MR_FAIL MR_STALE
for pid in "$CORE" "$WEB" "$INFRA"; do
  MR_REFUND[$pid]=$(new_mr "$DANA_TOKEN" "$pid" feature/2.4.0/payments/PC-1042-refund-splitting \
    "PC-1042: refund splitting" "Splits a refund across the original tenders.

- per-tender rows
- \`Refunds()\` no longer counts voided tenders

Closes #1" --data-urlencode "reviewer_ids[]=$REV1" --data-urlencode "reviewer_ids[]=$REV2" \
    --data-urlencode "assignee_id=$DANA" --data-urlencode "labels=payments,backend" \
    --data-urlencode "milestone_id=$MILESTONE" --data-urlencode "squash=true")
  MR_DRAFT[$pid]=$(new_mr "$REV2_TOKEN" "$pid" wip/2.5.0/platform/PC-1120-event-sourcing-poc \
    "Draft: PC-1120 event sourcing POC" "Exploring. Not for review yet." \
    --data-urlencode "draft=true" --data-urlencode "reviewer_ids[]=$DANA")
  MR_CONFLICT[$pid]=$(new_mr "$REV1_TOKEN" "$pid" chore/runner-image \
    "Bump runner image" "" --data-urlencode "labels=infra" --data-urlencode "reviewer_ids[]=$DANA")
  MR_FAIL[$pid]=$(new_mr "$DEV_TOKEN" "$pid" ci/nightly-fail \
    "Nightly job" "The test stage fails on this branch on purpose." --data-urlencode "assignee_id=$DANA")
  MR_STALE[$pid]=$(new_mr "$DEV_TOKEN" "$pid" chore/stale-branch \
    "A change from before main moved" "On infra this reads need_rebase (fast-forward only).")
  echo "  $(api GET "/projects/$pid" | jqr .path_with_namespace): !${MR_REFUND[$pid]} refund · !${MR_DRAFT[$pid]} draft · !${MR_CONFLICT[$pid]} conflict · !${MR_FAIL[$pid]} failing CI · !${MR_STALE[$pid]} stale"
done

# ── review threads on the refund MR of core-api ───────────────────────────────────────
say "Review threads"
PID="$CORE"; IID="${MR_REFUND[$PID]}"
REFS=$(api GET "/projects/$PID/merge_requests/$IID" | jq -c .diff_refs)
BASE=$(jq -r .base_sha <<<"$REFS"); HEAD=$(jq -r .head_sha <<<"$REFS"); START=$(jq -r .start_sha <<<"$REFS")
PATH_SHA=$(printf 'src/api/Orders.cs' | sha1sum | cut -d' ' -f1)
pos() {                       # pos NEW_PATH OLD_PATH  → common position args
  printf -- '--data-urlencode position[position_type]=text --data-urlencode position[base_sha]=%s --data-urlencode position[head_sha]=%s --data-urlencode position[start_sha]=%s --data-urlencode position[new_path]=%s --data-urlencode position[old_path]=%s' "$BASE" "$HEAD" "$START" "$1" "$2"
}

# 1. multi-line, unresolved: the new Split() body, lines 13–16 of the new file
# shellcheck disable=SC2046
THREAD_RANGE=$(api_as "$REV1_TOKEN" POST "/projects/$PID/merge_requests/$IID/discussions" \
  --data-urlencode "body=Integer division here drops the remainder — the last tender should absorb it." \
  $(pos src/api/Orders.cs src/api/Orders.cs) \
  --data-urlencode "position[new_line]=16" \
  --data-urlencode "position[line_range][start][line_code]=${PATH_SHA}_0_13" \
  --data-urlencode "position[line_range][start][type]=new" \
  --data-urlencode "position[line_range][start][new_line]=13" \
  --data-urlencode "position[line_range][end][line_code]=${PATH_SHA}_0_16" \
  --data-urlencode "position[line_range][end][type]=new" \
  --data-urlencode "position[line_range][end][new_line]=16" | jqr .id)
api_as "$DANA_TOKEN" POST "/projects/$PID/merge_requests/$IID/discussions/$THREAD_RANGE/notes" \
  --data-urlencode "body=Good catch — I'll carry the remainder on the last tender." > /dev/null
echo "  multi-line thread on Orders.cs 13–16, with a reply"

# 2. single-line on the old side (a deleted line), then resolved
# shellcheck disable=SC2046
THREAD_OLD=$(api_as "$REV2_TOKEN" POST "/projects/$PID/merge_requests/$IID/discussions" \
  --data-urlencode "body=Was Count() == 1 load-bearing anywhere?" \
  $(pos src/api/Orders.cs src/api/Orders.cs) \
  --data-urlencode "position[old_line]=5" | jqr .id)
api_as "$REV2_TOKEN" PUT "/projects/$PID/merge_requests/$IID/discussions/$THREAD_OLD" --data-urlencode "resolved=true" > /dev/null
echo "  single-line thread on the old side, resolved"

# 3. a thread that goes outdated: comment on README line 5, then push a new head
README_SHA=$(printf 'README.md' | sha1sum | cut -d' ' -f1)
# shellcheck disable=SC2046
api_as "$REV1_TOKEN" POST "/projects/$PID/merge_requests/$IID/discussions" \
  --data-urlencode "body=Link the ticket instead of naming it." \
  $(pos README.md README.md) --data-urlencode "position[new_line]=5" > /dev/null
DIR="$WORK/outdate"; git clone -q "http://oauth2:$GITLAB_TOKEN@$HOST/acme/core-api.git" "$DIR" -b feature/2.4.0/payments/PC-1042-refund-splitting
git -C "$DIR" config user.name "Dana Osei"; git -C "$DIR" config user.email "dana@acme.example"
printf '# core-api\n\nSeeded for Junction.\n\nSee [PC-1042](https://example.atlassian.net/browse/PC-1042).\n' > "$DIR/README.md"
git -C "$DIR" commit -qam "PC-1042: link the ticket" && git -C "$DIR" push -q origin HEAD
echo "  README thread left outdated by a new push"

# 4. a plain MR-level note that mentions dana (→ a 'mentioned' todo)
api_as "$REV1_TOKEN" POST "/projects/$PID/merge_requests/$IID/notes" \
  --data-urlencode "body=@dana can you also run the refund report after this?" > /dev/null

# 5. reviewer2's pending draft review — two notes nobody else can see yet
REFS=$(api GET "/projects/$PID/merge_requests/$IID" | jq -c .diff_refs)
BASE=$(jq -r .base_sha <<<"$REFS"); HEAD=$(jq -r .head_sha <<<"$REFS"); START=$(jq -r .start_sha <<<"$REFS")
# shellcheck disable=SC2046
api_as "$REV2_TOKEN" POST "/projects/$PID/merge_requests/$IID/draft_notes" \
  --data-urlencode "note=Draft: consider naming this SplitAcrossTenders." \
  $(pos src/api/Orders.cs src/api/Orders.cs) --data-urlencode "position[new_line]=13" > /dev/null
api_as "$REV2_TOKEN" POST "/projects/$PID/merge_requests/$IID/draft_notes" \
  --data-urlencode "note=Draft: overall this looks right to me." > /dev/null
echo "  reviewer2 has two pending draft notes"

# 6. one approval, so approvals read 1 of 2
api_as "$REV1_TOKEN" POST "/projects/$PID/merge_requests/$IID/approve" > /dev/null 2>&1 || true

# ── OAuth application for the loopback sign-in ────────────────────────────────────────
say "OAuth application"
APP=$(api POST /applications --data-urlencode "name=Junction (lab)" \
  --data-urlencode "redirect_uri=http://localhost:7171/auth/redirect" \
  --data-urlencode "scopes=api read_user" --data-urlencode "confidential=false")
APP_ID=$(jq -r .application_id <<<"$APP")

# ── done ──────────────────────────────────────────────────────────────────────────────
say "Save these — they are shown once"
cat <<EOF
  Sign in to $GITLAB_URL as any of: dana / reviewer1 / reviewer2 / dev   password: $PASSWORD
  Workspace repos to clone: acme/core-api  acme/web-client  acme/platform/infra

  dana, api + write_repository (1 year):  $DANA_TOKEN
  dana, read_api only (the scope-error state): $DANA_READONLY
  dana, api, expires in 3 days (the warning register): $DANA_EXPIRING
  dev,  api + write_repository (developer role — protected-branch refusals): $DEV_TOKEN

  OAuth application id (paste into a self-managed account's ClientId): $APP_ID
EOF
