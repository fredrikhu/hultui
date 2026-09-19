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

case "$(uname -s)" in
  MINGW64_NT*) HOST=windows; RID=win-x64 ;;
  Linux)       HOST=linux;   RID=linux-x64 ;;
  *) echo "unsupported host: $(uname -s) (run from an MSYS2 MINGW64 shell or Linux)" >&2; exit 1 ;;
esac

mkdir -p "$SRC" "$PREFIX" "$OUT/$RID"
export PKG_CONFIG_PATH="$PREFIX/lib/pkgconfig${PKG_CONFIG_PATH:+:$PKG_CONFIG_PATH}"
export PATH="$PREFIX/bin:$PATH"

log() { printf '\n\033[1;34m== %s\033[0m\n' "$*"; }

fetch() {  # fetch <dir> <git url> <tag>
  local dir=$1 url=$2 tag=$3
  if [ ! -d "$SRC/$dir/.git" ]; then
    log "fetch $dir $tag"
    git -c advice.detachedHead=false clone --depth 1 --branch "$tag" "$url" "$SRC/$dir"
  fi
}

# ──── FFmpeg: LGPL only, decode only ────
build_ffmpeg() {
  fetch ffmpeg https://github.com/FFmpeg/FFmpeg.git "$FFMPEG_TAG"
  if [ -f "$PREFIX/lib/pkgconfig/libavcodec.pc" ]; then log "ffmpeg: already built"; return; fi
  log "ffmpeg: configure"
  cd "$SRC/ffmpeg"
  local host_flags=()
  if [ "$HOST" = windows ]; then
    host_flags=(--target-os=mingw32 --arch=x86_64 --enable-d3d11va --enable-dxva2)
  else
    # No VAAPI/VDPAU in this first cut: they need a display interop the framework
    # does not provide yet, and libva would be one more runtime dependency.
    host_flags=(--disable-vaapi --disable-vdpau --disable-libdrm)
  fi
  # (libpostproc left the FFmpeg tree in 8.0; nothing to disable.)
  ./configure --prefix="$PREFIX" \
    --disable-gpl --disable-nonfree --disable-version3 \
    --disable-programs --disable-doc --disable-debug \
    --enable-static --disable-shared --enable-pic \
    --disable-avdevice \
    --disable-encoders --disable-muxers --disable-devices \
    --disable-sdl2 --disable-xlib --disable-libxcb \
    --enable-zlib --enable-libdav1d \
    --pkg-config-flags=--static \
    "${host_flags[@]}"
  log "ffmpeg: build"
  make -j"$JOBS"
  make install
}

# ──── libplacebo: GL only, no Vulkan, no shader compilers ────
build_libplacebo() {
  fetch libplacebo https://github.com/haasn/libplacebo.git "$LIBPLACEBO_TAG"
  if [ -f "$PREFIX/lib/pkgconfig/libplacebo.pc" ]; then log "libplacebo: already built"; return; fi
  cd "$SRC/libplacebo"
  git submodule update --init --depth 1
  log "libplacebo: configure"
  meson setup build --prefix="$PREFIX" --libdir=lib --buildtype=release --default-library=static \
    -Dvulkan=disabled -Dshaderc=disabled -Dglslang=disabled -Dlcms=disabled \
    -Dopengl=enabled -Dd3d11=disabled -Dlibdovi=disabled -Dunwind=disabled -Dxxhash=disabled \
    -Ddemos=false -Dtests=false
  log "libplacebo: build"
  meson compile -C build -j "$JOBS"
  meson install -C build
}

# ──── libass (Linux only; MSYS2's package is used on Windows) ────
build_libass() {
  [ "$HOST" = linux ] || return 0
  fetch libass https://github.com/libass/libass.git "$LIBASS_TAG"
  if [ -f "$PREFIX/lib/pkgconfig/libass.pc" ]; then log "libass: already built"; return; fi
  cd "$SRC/libass"
  log "libass: configure"
  ./autogen.sh
  ./configure --prefix="$PREFIX" --enable-static --disable-shared --with-pic
  log "libass: build"
  make -j"$JOBS"
  make install
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
    host_opts=(-Dc_link_args=-static -Dcpp_link_args=-static
               -Dd3d11=disabled -Ddirect3d=disabled -Dgl-win32=disabled -Dgl-dxinterop=disabled -Degl-angle=disabled)
  else
    # GL only, through the framework's context: no windowing, no display interop.
    host_opts=(-Dx11=disabled -Dwayland=disabled -Degl=disabled -Ddrm=disabled
               -Dvaapi=disabled -Dvdpau=disabled -Dpipewire=disabled -Djack=disabled -Dopenal=disabled)
  fi
  meson setup build --prefix="$PREFIX" --libdir=lib --buildtype=release \
    -Dgpl=false -Dlibmpv=true -Dcplayer=false \
    -Dlua=disabled -Djavascript=disabled -Dlibarchive=disabled -Dlibbluray=disabled \
    -Ddvdnav=disabled -Dcdda=disabled -Duchardet=disabled -Drubberband=disabled \
    -Dzimg=disabled -Dlcms2=disabled -Dvapoursynth=disabled -Dlibavdevice=disabled \
    -Dvulkan=disabled -Dshaderc=disabled -Dspirv-cross=disabled \
    -Dsdl2-video=disabled -Dsdl2-audio=disabled -Dsdl2-gamepad=disabled \
    -Dmanpage-build=disabled -Dhtml-build=disabled -Dtests=false \
    -Dprefer_static=true \
    "${host_opts[@]}"
  log "mpv: build"
  meson compile -C build -j "$JOBS"
}

collect() {
  log "collect $RID"
  local dest=$OUT/$RID
  if [ "$HOST" = windows ]; then
    cp "$SRC/mpv/build/libmpv-2.dll" "$dest/"
    strip "$dest/libmpv-2.dll" || true
    ls -la "$dest/libmpv-2.dll"
    # A self-contained DLL depends on nothing outside the OS.
    log "runtime dependencies"
    objdump -p "$dest/libmpv-2.dll" | grep -i 'DLL Name' | sort
  else
    cp "$SRC/mpv/build/libmpv.so.2"* "$dest/" 2>/dev/null || cp "$SRC/mpv/build/libmpv.so" "$dest/libmpv.so.2"
    strip "$dest/libmpv.so.2" || true
    ls -la "$dest/"
    log "runtime dependencies"
    readelf -d "$dest/libmpv.so.2" | grep NEEDED
  fi
  bash "$HERE/licence-report.sh" "$WORK" > "$dest/LICENCE-REPORT.txt"
  cat "$dest/LICENCE-REPORT.txt"
}

build_ffmpeg
build_libplacebo
build_libass
build_mpv
collect
log "done: $OUT/$RID"
