#!/usr/bin/env bash
# Makes the CEF a HultUI.WebView.Cef.Natives.<rid> package carries, in out/<rid>/cef/:
# CEF's official minimal distribution for the pinned version, checked against its SHA-1,
# flattened (Release/ and Resources/ in one directory: CEF finds icudtl.dat only beside
# libcef.so) and stripped (the official libcef.so is 1.45 GB with its debug sections).
# Spike L0, question 2, found both needed.
#
#   native/cef/build.sh <rid> [tarball]      rid: linux-x64 or linux-arm64
#
# The tarball is downloaded (to cache/) unless given. Needs curl, tar, bzip2, and strip that
# reads the target's ELF (binutils' strip for the host, llvm-strip or aarch64-linux-gnu-strip
# for arm64).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
source "$here/versions.env"
rid="${1:?usage: build.sh <linux-x64|linux-arm64> [tarball]}"
case "$rid" in
  linux-x64)   platform=linux64;    sha1="$CEF_SHA1_LINUX_X64";   strips=(strip llvm-strip) ;;
  linux-arm64) platform=linuxarm64; sha1="$CEF_SHA1_LINUX_ARM64"; strips=(aarch64-linux-gnu-strip llvm-strip) ;;
  *) echo "unknown RID $rid" >&2; exit 2 ;;
esac
name="cef_binary_${CEF_VERSION}_${platform}_minimal"
tarball="${2:-}"
if [[ -z "$tarball" ]]; then
  mkdir -p "$here/cache"
  tarball="$here/cache/$name.tar.bz2"
  if [[ ! -f "$tarball" ]]; then
    url="https://cef-builds.spotifycdn.com/$(python3 -c 'import sys,urllib.parse;print(urllib.parse.quote(sys.argv[1]))' "$name").tar.bz2"
    echo "downloading $url"
    curl -fL --retry 3 -o "$tarball.part" "$url"
    mv "$tarball.part" "$tarball"
  fi
fi
actual="$(sha1sum "$tarball" | cut -c1-40)"
if [[ "$actual" != "$sha1" ]]; then
  echo "SHA-1 mismatch for $tarball: $actual, expected $sha1" >&2
  exit 1
fi

strip_tool=""
for s in "${strips[@]}"; do command -v "$s" >/dev/null && { strip_tool="$s"; break; }; done
[[ -n "$strip_tool" ]] || { echo "no strip for $rid (tried ${strips[*]})" >&2; exit 1; }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
tar -xjf "$tarball" -C "$work" "$name/Release" "$name/Resources" "$name/LICENSE.txt" "$name/CREDITS.html" "$name/README.txt"
out="$here/out/$rid/cef"
rm -rf "$out"
mkdir -p "$out"
cp -a "$work/$name/Release/." "$out/"
cp -a "$work/$name/Resources/." "$out/"
# chrome-sandbox only works setuid root, which no package can carry (see README.md: the
# namespace sandbox is what runs). Left out rather than shipped broken.
rm -f "$out/chrome-sandbox"
for so in "$out"/*.so "$out"/*.so.*; do
  [[ -f "$so" ]] && "$strip_tool" --strip-unneeded "$so"
done
cp "$work/$name/LICENSE.txt" "$out/CEF-LICENSE.txt"
cp "$work/$name/CREDITS.html" "$out/CEF-CREDITS.html"
echo "$CEF_VERSION" > "$out/CEF-VERSION"
echo "out/$rid/cef: $(du -sh "$out" | cut -f1), libcef.so $(du -h "$out/libcef.so" | cut -f1)"
