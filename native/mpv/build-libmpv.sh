#!/usr/bin/env bash
# Builds a trimmed, LGPL-2.1 libmpv for HultUI.Media.Mpv. See README.md beside it.
#
# One script for both hosts it runs on:
#   - Windows, inside an MSYS2 MINGW64 shell  -> out/win-x64/libmpv-2.dll (self-contained)
#   - Ubuntu 24.04 (the Linux runner)          -> out/linux-x64/libmpv.so.2 (+ its own
#                                                 FFmpeg, libplacebo and libass linked in;
#                                                 base libraries from the distribution)
#
# What "trimmed, LGPL" means here, and why each knob is set, is in README.md; this
# file only does it. Every version is pinned in versions.env.
set -euo pipefail

HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=versions.env
source "$HERE/versions.env"

WORK=${MPV_WORK:-$HERE/work}
PREFIX=$WORK/prefix
SRC=$WORK/src
OUT=${MPV_OUT:-$HERE/out}
JOBS=${JOBS:-$(nproc)}

log() { printf '\n\033[1;34m== %s\033[0m\n' "$*"; }
die() { printf 'build-libmpv: %s\n' "$*" >&2; exit 1; }

case "$(uname -s)" in
  MINGW64_NT*) HOST=windows; RID=win-x64 ;;
  Linux)       HOST=linux;   RID=linux-x64 ;;
  *) die "unsupported host: $(uname -s) (run from an MSYS2 MINGW64 shell or Linux)" ;;
esac

# The host check above is not enough on Windows: Git for Windows' bash also reports
# MINGW64_NT (and sets MSYSTEM=MINGW64), passes it, and then fails twenty minutes later at
# "make: command not found". Ask for the toolchain itself, up front.
check_toolchain() {
  local tool missing=()
  for tool in git make meson ninja pkg-config strip; do
    command -v "$tool" >/dev/null 2>&1 || missing+=("$tool")
  done
  if [ "$HOST" = windows ]; then
    command -v objdump >/dev/null 2>&1 || missing+=(objdump)
    [ "${MSYSTEM:-}" = MINGW64 ] || die "MSYSTEM is '${MSYSTEM:-unset}', not MINGW64: run from an MSYS2 MINGW64 shell"
    local machine
    machine=$(gcc -dumpmachine 2>/dev/null || true)
    [ "$machine" = x86_64-w64-mingw32 ] || die "gcc targets '${machine:-nothing}', not x86_64-w64-mingw32: this is not an MSYS2 MINGW64 toolchain (Git for Windows' bash passes the host check and has none)"
  else
    command -v readelf >/dev/null 2>&1 || missing+=(readelf)
  fi
  [ ${#missing[@]} -eq 0 ] || die "missing tools: ${missing[*]} (see README.md for the packages)"
}
check_toolchain

mkdir -p "$SRC" "$PREFIX" "$OUT/$RID"
export PKG_CONFIG_PATH="$PREFIX/lib/pkgconfig${PKG_CONFIG_PATH:+:$PKG_CONFIG_PATH}"
export PATH="$PREFIX/bin:$PATH"

fetch() {  # fetch <dir> <git url> <tag>
  local dir=$1 url=$2 tag=$3
  if [ ! -d "$SRC/$dir/.git" ]; then
    log "fetch $dir $tag"
    git -c advice.detachedHead=false clone --depth 1 --branch "$tag" "$url" "$SRC/$dir"
    return
  fi
  # A work directory kept from before a versions.env bump still holds the old tree, and
  # every "already built" check below would then happily reuse it. CI never sees this —
  # its checkout is clean — but a local rebuild does.
  local have
  have=$(git -C "$SRC/$dir" describe --tags --exact-match 2>/dev/null || echo "(not at a tag)")
  [ "$have" = "$tag" ] || die "$SRC/$dir is at $have, versions.env wants $tag: delete it (or the whole work directory) and run again"
}

# "Already built" means built from this tag with these options, not merely that the output
# exists: each stage writes what it was built from, and is redone when that differs.
built_from() {  # built_from <name> <what it must have been built from>
  [ "$(cat "$PREFIX/stamps/$1" 2>/dev/null)" = "$2" ]
}
record_built() {  # record_built <name> <what it was built from>
  mkdir -p "$PREFIX/stamps"
  printf '%s' "$2" > "$PREFIX/stamps/$1"
}

# ──── nv-codec-headers: NVDEC and CUDA interop, on both hosts ────
# Headers and a .pc file, nothing compiled. FFmpeg and mpv load libcuda and libnvcuvid
# at run time through them rather than linking, so the library still loads on a
# machine with no NVIDIA driver, and simply finds no CUDA there. Built from the pinned
# tag rather than a distribution package so the version, and with it the minimum
# driver, is the same everywhere. Both hosts: CUDA is how Windows gets zero-copy too,
# the DirectX bridges being 8-bit or needing ANGLE (docs/design/HDR-WINDOWS-CUDA.md).
# The headers' Makefile handles MSYS2 paths itself, so no pacman package is wanted —
# two copies would leave pkg-config choosing between versions.
build_nvcodec_headers() {
  fetch nv-codec-headers https://github.com/FFmpeg/nv-codec-headers.git "$NVCODEC_TAG"
  if [ -f "$PREFIX/lib/pkgconfig/ffnvcodec.pc" ] && built_from nv-codec-headers "$NVCODEC_TAG"; then
    log "nv-codec-headers: already installed"; return
  fi
  log "nv-codec-headers: install"
  make -C "$SRC/nv-codec-headers" PREFIX="$PREFIX" install
  record_built nv-codec-headers "$NVCODEC_TAG"
}

# ──── FFmpeg: LGPL only, decode only ────
build_ffmpeg() {
  fetch ffmpeg https://github.com/FFmpeg/FFmpeg.git "$FFMPEG_TAG"
  local host_flags=()
  if [ "$HOST" = windows ]; then
    # NVDEC alongside the DirectX decoders, asked for explicitly for the same reason as
    # on Linux: autodetection is how this went missing without anyone deciding it should.
    # The DirectX ones stay because they are what AMD and Intel fall back to.
    host_flags=(--target-os=mingw32 --arch=x86_64 --enable-d3d11va --enable-dxva2
                --enable-ffnvcodec --enable-nvdec)
  else
    # NVDEC, asked for explicitly so missing headers fail here instead of quietly
    # building without it. Not the nonfree CUDA options (cuda-nvcc, libnpp): those are
    # for CUDA filters, and decoding needs none of them.
    # No VAAPI/VDPAU yet. VAAPI zero-copy needs EGL in mpv, and FFmpeg links libva
    # directly rather than loading it, which would make libva a hard runtime dependency.
    host_flags=(--enable-ffnvcodec --enable-nvdec --disable-vaapi --disable-vdpau --disable-libdrm)
  fi
  # (libpostproc left the FFmpeg tree in 8.0; nothing to disable.)
  # cuda-llvm off explicitly: FFmpeg autodetects it wherever clang has an nvptx target. It is
  # free software, so this is not about the licence — it is the same drift the explicit
  # --enable-nvdec exists to stop, a feature arriving because a runner happened to have it.
  local flags=(--prefix="$PREFIX"
    --disable-gpl --disable-nonfree --disable-version3
    --disable-cuda-llvm
    --disable-programs --disable-doc --disable-debug
    --enable-static --disable-shared --enable-pic
    --disable-avdevice
    --disable-encoders --disable-muxers --disable-devices
    --disable-sdl2 --disable-xlib --disable-libxcb
    --enable-zlib --enable-libdav1d
    --pkg-config-flags=--static
    "${host_flags[@]}")
  # Reuse a built FFmpeg only if it was configured the same way, from the same tag, against
  # the same headers. Otherwise a work directory from before a flag change keeps the old
  # FFmpeg, and mpv links against it without complaint: -Dcuda-hwaccel=enabled sees the
  # headers and never learns that FFmpeg itself was built without NVDEC.
  local want="$FFMPEG_TAG nv-codec-headers=$NVCODEC_TAG ${flags[*]}"
  if [ -f "$PREFIX/lib/pkgconfig/libavcodec.pc" ] && built_from ffmpeg "$want"; then
    log "ffmpeg: already built"; return
  fi
  log "ffmpeg: configure"
  cd "$SRC/ffmpeg"
  rm -f "$PREFIX/stamps/ffmpeg"
  ./configure "${flags[@]}"
  log "ffmpeg: build"
  make -j"$JOBS"
  make install
  record_built ffmpeg "$want"
}

# ──── libplacebo: GL only, no Vulkan, no shader compilers ────
build_libplacebo() {
  fetch libplacebo https://github.com/haasn/libplacebo.git "$LIBPLACEBO_TAG"
  if [ -f "$PREFIX/lib/pkgconfig/libplacebo.pc" ] && built_from libplacebo "$LIBPLACEBO_TAG"; then
    log "libplacebo: already built"; return
  fi
  cd "$SRC/libplacebo"
  git submodule update --init --depth 1
  log "libplacebo: configure"
  rm -rf build
  meson setup build --prefix="$PREFIX" --libdir=lib --buildtype=release --default-library=static \
    -Dvulkan=disabled -Dshaderc=disabled -Dglslang=disabled -Dlcms=disabled \
    -Dopengl=enabled -Dd3d11=disabled -Dlibdovi=disabled -Dunwind=disabled -Dxxhash=disabled \
    -Ddemos=false -Dtests=false
  log "libplacebo: build"
  meson compile -C build -j "$JOBS"
  meson install -C build
  record_built libplacebo "$LIBPLACEBO_TAG"
}

# ──── libass (Linux only; MSYS2's package is used on Windows) ────
build_libass() {
  [ "$HOST" = linux ] || return 0
  fetch libass https://github.com/libass/libass.git "$LIBASS_TAG"
  if [ -f "$PREFIX/lib/pkgconfig/libass.pc" ] && built_from libass "$LIBASS_TAG"; then
    log "libass: already built"; return
  fi
  cd "$SRC/libass"
  log "libass: configure"
  ./autogen.sh
  ./configure --prefix="$PREFIX" --enable-static --disable-shared --with-pic
  log "libass: build"
  make -j"$JOBS"
  make install
  record_built libass "$LIBASS_TAG"
}

# ──── mpv: the library only, GPL parts off ────
build_mpv() {
  fetch mpv https://github.com/mpv-player/mpv.git "$MPV_TAG"
  cd "$SRC/mpv"
  rm -rf build
  log "mpv: configure"
  local host_opts=()
  if [ "$HOST" = windows ]; then
    # One DLL with everything inside it: the static runtime, the static deps.
    # No window outputs: the framework's GL context is the only display.
    # cuda-interop is the zero-copy path here: hwdec_cuda_gl.c is plain GL with no EGL,
    # GLX or WGL in it, and mpv's gate is "cuda-hwaccel and (gl or vulkan)" with no
    # platform condition — gl being satisfied by plain-gl, which the render API already
    # runs on. CUDA itself is loaded at run time through ffnvcodec, so the DLL gains no
    # dependency on nvcuda.dll and still loads on AMD and Intel machines.
    #
    # The DirectX interops stay off. gl-dxinterop in particular would bring back the
    # 8-bit dxva2 bridge, which is a zero-copy path that throws away the ten bits.
    host_opts=(-Dc_link_args=-static -Dcpp_link_args=-static -Dprefer_static=true
               -Dcuda-hwaccel=enabled -Dcuda-interop=enabled
               -Dd3d11=disabled -Ddirect3d=disabled -Dgl-win32=disabled -Dgl-dxinterop=disabled -Degl-angle=disabled)
  else
    # GL only, through the framework's context: no windowing, no display output.
    # Decode interop is a different thing and is on: cuda-interop is what makes
    # hwdec=nvdec zero-copy (docs/design/HDR-LINUX-ZEROCOPY.md). It needs only GL,
    # which plain-gl provides, and neither EGL nor a window.
    # Our own prefix holds only static archives, so FFmpeg, libplacebo and libass
    # link in by construction; the distribution's libraries must stay shared (its
    # archives are not PIC — prefer_static here failed the link on libfontconfig.a).
    # What static archives do need is their private dependencies, which pkg-config
    # only lists with --static; the wrapper asks for that on every query.
    # x11-clipboard is detected on its own rather than following -Dx11, so on a host
    # with the X11 and Xfixes headers it links libX11 in; the framework's clipboard is
    # SDL's.
    # PipeWire is the audio output, explicitly: without it mpv falls back to PulseAudio
    # through pipewire-pulse, which measured ~25 ms of CPU a second more at 4K than the
    # native client. It makes libpipewire-0.3 a runtime dependency; PulseAudio and ALSA
    # stay as the fallbacks when no PipeWire daemon is running.
    host_opts=(-Dcuda-hwaccel=enabled -Dcuda-interop=enabled
               -Dx11=disabled -Dx11-clipboard=disabled -Dwayland=disabled -Degl=disabled -Ddrm=disabled
               -Dvaapi=disabled -Dvdpau=disabled -Dpipewire=enabled -Djack=disabled -Dopenal=disabled)
    printf '#!/bin/sh\nexec pkg-config --static "$@"\n' > "$WORK/pkg-config-static"
    chmod +x "$WORK/pkg-config-static"
    export PKG_CONFIG="$WORK/pkg-config-static"
  fi
  meson setup build --prefix="$PREFIX" --libdir=lib --buildtype=release \
    -Dgpl=false -Dlibmpv=true -Dcplayer=false \
    -Dlua=disabled -Djavascript=disabled -Dlibarchive=disabled -Dlibbluray=disabled \
    -Ddvdnav=disabled -Dcdda=disabled -Duchardet=disabled -Drubberband=disabled \
    -Dzimg=disabled -Dlcms2=disabled -Dvapoursynth=disabled -Dlibavdevice=disabled \
    -Dvulkan=disabled -Dshaderc=disabled -Dspirv-cross=disabled \
    -Dsdl2-video=disabled -Dsdl2-audio=disabled -Dsdl2-gamepad=disabled \
    -Dmanpage-build=disabled -Dhtml-build=disabled -Dtests=false \
    "${host_opts[@]}"
  log "mpv: build"
  meson compile -C build -j "$JOBS"
}

collect() {
  log "collect $RID"
  local dest=$OUT/$RID lib deps
  if [ "$HOST" = windows ]; then
    lib=$dest/libmpv-2.dll
    cp "$SRC/mpv/build/libmpv-2.dll" "$dest/"
    strip "$lib" || true
    ls -la "$lib"
    # A self-contained DLL depends on nothing outside the OS.
    deps=$(objdump -p "$lib" | grep -i 'DLL Name' | sed -E 's/^[[:space:]]*DLL Name:[[:space:]]*//' | sort)
  else
    lib=$dest/libmpv.so.2
    cp "$SRC/mpv/build/libmpv.so.2"* "$dest/" 2>/dev/null || cp "$SRC/mpv/build/libmpv.so" "$lib"
    strip "$lib" || true
    ls -la "$dest/"
    deps=$(readelf -d "$lib" | grep NEEDED | sed -E 's/.*\[(.*)\].*/\1/' | sort)
  fi
  log "runtime dependencies"
  printf '%s\n' "$deps"
  check_no_cuda_link "$deps"

  bash "$HERE/licence-report.sh" "$WORK" > "$dest/LICENCE-REPORT.txt"
  { echo; echo "Runtime dependencies of $(basename "$lib")"; printf '  %s\n' $deps; } >> "$dest/LICENCE-REPORT.txt"
  cat "$dest/LICENCE-REPORT.txt"
}

# CUDA is reached through ffnvcodec's loader, which opens the driver's libraries at run time,
# so the library still loads on a machine with no NVIDIA driver and simply finds no CUDA
# there. Linking them instead would build and pass every test on an NVIDIA runner, and ship
# a library that fails to load on every AMD and Intel machine. This rule used to live only in
# the README; now the build enforces it.
check_no_cuda_link() {  # check_no_cuda_link <dependency list>
  local bad
  bad=$(printf '%s\n' "$1" | grep -iE '^(nvcuda|nvcuvid|nvidia-encode|libcuda|libnvcuvid|libnvidia-encode)[.]' || true)
  [ -z "$bad" ] || die "the library links NVIDIA's driver libraries directly, and would not load without an NVIDIA driver: $(echo $bad)"
}

build_nvcodec_headers
build_ffmpeg
build_libplacebo
build_libass
build_mpv
collect
log "done: $OUT/$RID"
