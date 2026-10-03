#!/usr/bin/env bash
# Builds hultui-cef-helper for one RID into native/cef-helper/out/<rid>/.
#
#   native/cef-helper/build.sh [rid] [cc]
#
# rid defaults to the machine's (linux-x64 or linux-arm64); cc defaults to $CC, then cc.
# For linux-arm64 on an x64 machine, name a cross compiler: build.sh linux-arm64 aarch64-linux-gnu-gcc.
# HultUI.WebView.Cef's own build runs the same command for the machine's RID when a C
# compiler is there, so a developer never needs this script; the natives workflow does.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
case "$(uname -m)" in
  x86_64) default_rid=linux-x64 ;;
  aarch64) default_rid=linux-arm64 ;;
  *) default_rid="linux-$(uname -m)" ;;
esac
rid="${1:-$default_rid}"
cc="${2:-${CC:-cc}}"
out="$here/out/$rid"
mkdir -p "$out"
"$cc" -O2 -Wall -Wextra -o "$out/hultui-cef-helper" "$here/hultui-cef-helper.c" -ldl
strip "$out/hultui-cef-helper" 2>/dev/null || true
echo "built $out/hultui-cef-helper"
