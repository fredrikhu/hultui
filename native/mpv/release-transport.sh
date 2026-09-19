#!/usr/bin/env bash
# Moves build outputs between workflow jobs through a GitHub release, with nothing
# but curl and python — the two GitHub-provided transports both failed on the
# self-hosted runners (artifact storage is a small quota; actions/cache's cross-OS
# archive breaks on a Windows tar path with a space). A release asset has neither
# problem and is useful to a person afterwards.
#
#   release-transport.sh ensure   <tag> [--prerelease]     create the release if missing
#   release-transport.sh upload   <tag> <file> <asset>     replace the asset of that name
#   release-transport.sh download <tag> <asset> <dest>     fetch an asset to a file
#
# Needs GH_TOKEN and GITHUB_REPOSITORY (both set in Actions); GITHUB_SHA for ensure.
set -euo pipefail
: "${GH_TOKEN:?GH_TOKEN}" "${GITHUB_REPOSITORY:?GITHUB_REPOSITORY}"

API=https://api.github.com/repos/$GITHUB_REPOSITORY
# A python that runs: on Windows, "python3" on the PATH can be the Store's stub.
PY=
for candidate in python3 python; do
  if "$candidate" -c pass > /dev/null 2>&1; then PY=$candidate; break; fi
done
: "${PY:?no working python found}"
auth=(-H "Authorization: Bearer $GH_TOKEN" -H "Accept: application/vnd.github+json" -H "X-GitHub-Api-Version: 2022-11-28")

release_json() { curl -sS "${auth[@]}" "$API/releases/tags/$1"; }
json_field()   { "$PY" -c 'import json,sys; d=json.load(sys.stdin); print(d.get(sys.argv[1], ""))' "$1"; }

case ${1:-} in
  ensure)
    tag=$2; pre=${3:-}
    if [ -n "$(release_json "$tag" | json_field id)" ]; then echo "release $tag exists"; exit 0; fi
    body=$("$PY" -c 'import json,sys; print(json.dumps({"tag_name": sys.argv[1], "target_commitish": sys.argv[2], "name": "libmpv natives " + sys.argv[1], "prerelease": sys.argv[3] == "--prerelease", "body": "Trimmed LGPL-2.1 libmpv for HultUI.Media.Mpv, built by the mpv natives workflow. See native/mpv/README.md and each LICENCE-REPORT."}))' "$tag" "${GITHUB_SHA:-main}" "$pre")
    # A race with the other build job creating the same release is fine: one wins.
    curl -sS "${auth[@]}" -X POST "$API/releases" -d "$body" > /dev/null || true
    echo "release $tag created"
    ;;

  upload)
    tag=$2; file=$3; name=$4
    json=$(release_json "$tag")
    id=$(json_field id <<<"$json")
    [ -n "$id" ] || { echo "no release $tag" >&2; exit 1; }
    for old in $("$PY" -c 'import json,sys; d=json.load(sys.stdin); print(" ".join(str(a["id"]) for a in d.get("assets", []) if a["name"] == sys.argv[1]))' "$name" <<<"$json"); do
      curl -sS "${auth[@]}" -X DELETE "$API/releases/assets/$old"
    done
    curl -sS --fail "${auth[@]}" -H "Content-Type: application/octet-stream" --data-binary @"$file" \
      "https://uploads.github.com/repos/$GITHUB_REPOSITORY/releases/$id/assets?name=$name" > /dev/null
    echo "uploaded $name ($(wc -c < "$file") bytes) to $tag"
    ;;

  download)
    tag=$2; name=$3; dest=$4
    aid=$(release_json "$tag" | "$PY" -c 'import json,sys; d=json.load(sys.stdin); m=[str(a["id"]) for a in d.get("assets", []) if a["name"] == sys.argv[1]]; print(m[0] if m else "")' "$name")
    [ -n "$aid" ] || { echo "no asset $name on $tag" >&2; exit 1; }
    mkdir -p "$(dirname "$dest")"
    # Only the octet-stream Accept: a JSON Accept beside it makes the API describe
    # the asset instead of redirecting to it.
    curl -sS --fail -L -H "Authorization: Bearer $GH_TOKEN" -H "Accept: application/octet-stream" \
      -H "X-GitHub-Api-Version: 2022-11-28" -o "$dest" "$API/releases/assets/$aid"
    echo "downloaded $name ($(wc -c < "$dest") bytes) from $tag"
    ;;

  *) echo "usage: $0 ensure|upload|download ..." >&2; exit 2 ;;
esac
