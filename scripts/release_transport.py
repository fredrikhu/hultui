#!/usr/bin/env python3
"""Moves build outputs between workflow jobs through a GitHub release.

    release_transport.py ensure   <tag> [--prerelease]     create the release if missing
    release_transport.py upload   <tag> <file> <asset>     replace the asset of that name
    release_transport.py download <tag> <asset> <dest>     fetch an asset to a file
    release_transport.py prune    <tag> <prefix> <keep>    delete the assets named <prefix>*
                                                           but <keep>: a rolling release's
                                                           older builds, whose names differ

Nothing but the standard library: both GitHub-provided transports failed on the
self-hosted runners (artifact storage is a small quota; actions/cache's cross-OS
archive breaks on a Windows tar path with a space), and the Linux runner has no
curl. A release asset has none of those problems and is useful to a person
afterwards.

This is a plain file rather than a heredoc inside release-transport.sh because the
Windows runners have no bash on PATH — only MSYS2's, at a path you have to know.
A pwsh step can run this directly; release-transport.sh is now a wrapper around it
for the Linux jobs that were already calling the shell name.

Needs GH_TOKEN and GITHUB_REPOSITORY (both set in Actions); GITHUB_SHA for ensure.
"""

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

def describe(tag):
    """The title and text a release gets, by the workflow its tag belongs to."""
    if tag.startswith("cef-natives"):
        return (f"CEF natives {tag}",
                "CEF's official minimal build, flattened and stripped, as HultUI.WebView.Cef.Natives.<rid> "
                "packages, built and proved by the cef natives workflow. See native/cef/README.md.")
    if tag.startswith("cef-helper"):
        return (f"CEF helper {tag}",
                "hultui-cef-helper and the HultUI.WebView.Cef package, built by the cef helper workflow. "
                "See native/cef-helper/README.md.")
    return (f"libmpv natives {tag}",
            "Trimmed LGPL-2.1 libmpv for HultUI.Media.Mpv, built by the mpv natives workflow. "
            "See native/mpv/README.md and each LICENCE-REPORT.")

verb, args = sys.argv[1], sys.argv[2:]

if verb == "ensure":
    tag = args[0]
    pre = len(args) > 1 and args[1] == "--prerelease"
    title, text = describe(tag)
    if rel := release(tag):
        print(f"release {tag} exists")
        # Every release used to be titled as mpv's: put right what an earlier run wrote, and
        # only that, so a release someone has written by hand stays as they wrote it.
        if rel.get("name") == f"libmpv natives {tag}" and title != rel.get("name"):
            call("PATCH", f"{api}/releases/{rel['id']}", json.dumps({"name": title, "body": text}).encode())
    else:
        body = json.dumps({
            "tag_name": tag, "target_commitish": os.environ.get("GITHUB_SHA", "main"),
            "name": title, "prerelease": pre, "body": text,
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

elif verb == "prune":
    tag, prefix, keep = args
    rel = release(tag) or sys.exit(f"no release {tag}")
    for asset in rel.get("assets", []):
        if asset["name"].startswith(prefix) and asset["name"] != keep:
            call("DELETE", f"{api}/releases/assets/{asset['id']}")
            print(f"deleted {asset['name']} from {tag}")

else:
    sys.exit("usage: release_transport.py ensure|upload|download|prune ...")
