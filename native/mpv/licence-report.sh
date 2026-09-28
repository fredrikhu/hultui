#!/usr/bin/env bash
# Prints what went into a libmpv built by build-libmpv.sh and what its licence is,
# from the configured source trees — so a build that quietly turned GPL is visible
# here before it is anywhere else. Usage: licence-report.sh <work dir>
set -euo pipefail
WORK=$1
SRC=$WORK/src
HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=versions.env
source "$HERE/versions.env"

fail=0
echo "libmpv build report — $(date -u +%Y-%m-%dT%H:%MZ)"
echo
echo "mpv        $MPV_TAG"
echo "FFmpeg     $FFMPEG_TAG"
echo "libplacebo $LIBPLACEBO_TAG"
echo "libass     ${LIBASS_TAG} (or the host package on Windows)"
# What was installed, not only what was asked for: the two differed whenever a work
# directory outlived a versions.env bump, and the report said the new tag regardless.
nvinstalled=$(pkg-config --modversion ffnvcodec 2>/dev/null || echo "not found")
echo "nv-codec-headers ${NVCODEC_TAG} (MIT) — installed: ${nvinstalled}"
stale=0
if [ "$nvinstalled" != "${NVCODEC_TAG#n}" ]; then
  echo "  installed nv-codec-headers ($nvinstalled) is not the pinned ${NVCODEC_TAG#n}  <-- stale work directory"; stale=1
fi
echo

echo "mpv build configuration"
if grep -qE '^#define HAVE_GPL 1' "$SRC/mpv/build/config.h"; then
  echo "  HAVE_GPL 1  <-- GPL parts are IN: this build is GPL"; fail=1
else
  echo "  HAVE_GPL 0  (built with -Dgpl=false: LGPL-2.1+)"
fi
for f in HAVE_LUA HAVE_JAVASCRIPT HAVE_LIBARCHIVE HAVE_DVDNAV HAVE_CDDA HAVE_LIBBLURAY HAVE_RUBBERBAND HAVE_VULKAN HAVE_SHADERC HAVE_CUDA_HWACCEL HAVE_CUDA_INTEROP; do
  v=$(grep -E "^#define $f " "$SRC/mpv/build/config.h" | awk '{print $3}')
  echo "  $f ${v:-?}"
done
echo

echo "FFmpeg configuration"
conf=$(grep -E '^#define FFMPEG_CONFIGURATION' "$SRC/ffmpeg/config.h" | sed -E 's/^#define FFMPEG_CONFIGURATION "(.*)"$/\1/')
echo "  $conf" | fold -s -w 100 | sed '2,$s/^/  /'
for bad in --enable-gpl --enable-nonfree --enable-version3; do
  if grep -q -- "$bad" <<<"$conf"; then echo "  $bad PRESENT  <-- not LGPL-2.1"; fail=1; fi
done
lic=$(grep -E '^#define CONFIG_(GPL|NONFREE|VERSION3) ' "$SRC/ffmpeg/config.h" | awk '{print $2"="$3}' | tr '\n' ' ')
echo "  $lic"
echo

echo "libplacebo build options"
PY=$(command -v python3 || command -v python)
"$PY" - "$SRC/libplacebo/build/meson-info/intro-buildoptions.json" <<'EOF'
import json, sys
for o in json.load(open(sys.argv[1])):
    if o["name"] in ("vulkan", "shaderc", "glslang", "lcms", "opengl", "d3d11", "libdovi"):
        print(f"  {o['name']} = {o['value']}")
EOF
echo

if [ "$fail" = 1 ]; then
  echo "RESULT: NOT LGPL — do not ship"; exit 1
fi
if [ "$stale" = 1 ]; then
  # Not a licence problem, and not reported as one: the binary is built from something
  # other than what versions.env and the notices name.
  echo "RESULT: built from a stale work directory — delete it and rebuild before shipping"; exit 1
fi
echo "RESULT: LGPL-2.1+ throughout (mpv -Dgpl=false; FFmpeg without gpl/nonfree/version3; libplacebo LGPL-2.1; libass ISC; dav1d BSD-2)"
