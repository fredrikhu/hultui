#!/usr/bin/env bash
# Moves build outputs between workflow jobs through a GitHub release, with nothing
# but Python's standard library — the two GitHub-provided transports both failed on
# the self-hosted runners (artifact storage is a small quota; actions/cache's
# cross-OS archive breaks on a Windows tar path with a space), and the Linux runner
# has no curl. A release asset has none of those problems and is useful to a person
# afterwards.
#
#   release-transport.sh ensure   <tag> [--prerelease]     create the release if missing
#   release-transport.sh upload   <tag> <file> <asset>     replace the asset of that name
#   release-transport.sh download <tag> <asset> <dest>     fetch an asset to a file
#
# Needs GH_TOKEN and GITHUB_REPOSITORY (both set in Actions); GITHUB_SHA for ensure.
set -euo pipefail
: "${GH_TOKEN:?GH_TOKEN}" "${GITHUB_REPOSITORY:?GITHUB_REPOSITORY}"

# A python that runs: on Windows, "python3" on the PATH can be the Store's stub.
PY=
for candidate in python3 python; do
  if "$candidate" -c pass > /dev/null 2>&1; then PY=$candidate; break; fi
done
: "${PY:?no working python found}"

exec "$PY" - "$@" <<'EOF'
import json, os, sys, urllib.request, urllib.error

token = os.environ["GH_TOKEN"]
repo = os.environ["GITHUB_REPOSITORY"]
api = f"https://api.github.com/repos/{repo}"

def call(method, url, data=None, content_type="application/json", accept="application/vnd.github+json", out=None):
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Authorization", f"Bearer {token}")
    req.add_header("Accept", accept)
    req.add_header("X-GitHub-Api-Version", "2022-11-28")
    req.add_header("User-Agent", "hultui-release-transport")
    if data is not None:
        req.add_header("Content-Type", content_type)
    try:
        with urllib.request.urlopen(req) as resp:
            if out is not None:
                with open(out, "wb") as f:
                    while chunk := resp.read(1 << 20):
                        f.write(chunk)
                return None
            body = resp.read()
            return json.loads(body) if body else None
    except urllib.error.HTTPError as e:
        if e.code == 404:
            return None
        raise SystemExit(f"{method} {url}: HTTP {e.code} {e.read()[:300]!r}")

def release(tag):
    return call("GET", f"{api}/releases/tags/{tag}")

verb, args = sys.argv[1], sys.argv[2:]

if verb == "ensure":
    tag = args[0]
    pre = len(args) > 1 and args[1] == "--prerelease"
    if release(tag):
        print(f"release {tag} exists")
    else:
        body = json.dumps({
            "tag_name": tag, "target_commitish": os.environ.get("GITHUB_SHA", "main"),
            "name": f"libmpv natives {tag}", "prerelease": pre,
            "body": "Trimmed LGPL-2.1 libmpv for HultUI.Media.Mpv, built by the mpv natives workflow. See native/mpv/README.md and each LICENCE-REPORT.",
        }).encode()
        try:
            call("POST", f"{api}/releases", body)
            print(f"release {tag} created")
        except SystemExit as e:
            # A race with the other build job creating the same release: one wins.
            if release(tag): print(f"release {tag} created by the other job")
            else: raise

elif verb == "upload":
    tag, path, name = args
    rel = release(tag) or sys.exit(f"no release {tag}")
    for asset in rel.get("assets", []):
        if asset["name"] == name:
            call("DELETE", f"{api}/releases/assets/{asset['id']}")
    with open(path, "rb") as f:
        data = f.read()
    call("POST", f"https://uploads.github.com/repos/{repo}/releases/{rel['id']}/assets?name={name}",
         data, content_type="application/octet-stream")
    print(f"uploaded {name} ({len(data)} bytes) to {tag}")

elif verb == "download":
    tag, name, dest = args
    rel = release(tag) or sys.exit(f"no release {tag}")
    ids = [a["id"] for a in rel.get("assets", []) if a["name"] == name]
    if not ids:
        sys.exit(f"no asset {name} on {tag}")
    os.makedirs(os.path.dirname(dest) or ".", exist_ok=True)
    # The octet-stream Accept alone makes the API redirect to the bytes.
    call("GET", f"{api}/releases/assets/{ids[0]}", accept="application/octet-stream", out=dest)
    print(f"downloaded {name} ({os.path.getsize(dest)} bytes) from {tag}")

else:
    sys.exit("usage: release-transport.sh ensure|upload|download ...")
EOF
