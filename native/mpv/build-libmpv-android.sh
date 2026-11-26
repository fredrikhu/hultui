#!/usr/bin/env bash
# Builds the trimmed, LGPL-2.1 libmpv for Android (plan 20 phase 5): one self-contained
# libmpv.so per ABI, cross-built with the NDK on Linux. See README.md beside it.
#
#   ANDROID_NDK_HOME=~/android/android-ndk-r27c ./build-libmpv-android.sh arm64-v8a
#   ANDROID_NDK_HOME=... ./build-libmpv-android.sh x86_64          (the emulator)
#
# The same trim as the desktop build (build-libmpv.sh): mpv -Dgpl=false, FFmpeg without gpl,
# nonfree or version3 and decode only, libplacebo GL only. What differs is Android's:
# - Nothing comes from the host. FFmpeg, libplacebo, libass, dav1d and libass's text libraries
#   (freetype, fribidi, harfbuzz) are built from pinned sources and linked in, with libc++.
#   libmpv.so needs only what every Android has: libc, libm, libdl, liblog, libz, libandroid,
#   libEGL, libGLESv2 (or 3), libmediandk, libaaudio.
# - Hardware decoding through MediaCodec: FFmpeg's decoders (--enable-mediacodec, which needs
#   the Java VM: HultUI.Media.Mpv hands it to av_jni_set_java_vm, which the library exports),
#   and mpv's AImageReader interop (android-media-ndk), which puts decoded frames into GL ES
#   textures with no copy.
# - Audio through AAudio, AudioTrack the fallback.
# - Subtitles: libass with no font provider (no fontconfig on Android). Embedded fonts work;
#   a fallback font is the player's to name (sub-font / sub-fonts-dir).
set -euo pipefail

HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=versions.env
source "$HERE/versions.env"

ABI=${1:?usage: build-libmpv-android.sh arm64-v8a|x86_64}
case "$ABI" in
  arm64-v8a) TRIPLE=aarch64-linux-android; CPU_FAMILY=aarch64; CPU=armv8; FF_ARCH=aarch64; RID=android-arm64 ;;
  x86_64)    TRIPLE=x86_64-linux-android;  CPU_FAMILY=x86_64;  CPU=x86_64; FF_ARCH=x86_64; RID=android-x64 ;;
  *) echo "build-libmpv-android: unknown ABI '$ABI' (arm64-v8a or x86_64)" >&2; exit 1 ;;
esac

log() { printf '\n\033[1;34m== %s [%s]\033[0m\n' "$*" "$ABI"; }
die() { printf 'build-libmpv-android: %s\n' "$*" >&2; exit 1; }

[ "$(uname -s)" = Linux ] || die "cross-builds from Linux only (WSL, or the Linux runner)"
NDK=${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}
[ -n "$NDK" ] && [ -f "$NDK/source.properties" ] || die "ANDROID_NDK_HOME must point at an NDK (r27: $ANDROID_NDK_REVISION)"
ndk_rev=$(sed -n 's/^Pkg.Revision *= *//p' "$NDK/source.properties")
[ "$ndk_rev" = "$ANDROID_NDK_REVISION" ] || die "the NDK at $NDK is $ndk_rev, versions.env wants $ANDROID_NDK_REVISION"
API=$ANDROID_API
TC=$NDK/toolchains/llvm/prebuilt/linux-x86_64
[ -d "$TC" ] || die "no LLVM toolchain at $TC"
CC=$TC/bin/${TRIPLE}${API}-clang
CXX=$TC/bin/${TRIPLE}${API}-clang++

for tool in git make meson ninja pkg-config nasm autoreconf libtoolize python3 awk; do
  command -v "$tool" >/dev/null 2>&1 || die "missing tool: $tool (see README.md for the packages)"
done
python3 -c 'import jinja2' 2>/dev/null || die "missing python3 jinja2 (libplacebo generates its GL loader with it)"

# Sources are shared by the ABIs; each ABI builds out of tree in its own work directory. The
# build directories sit where licence-report.sh looks for them: <work>/src/ffmpeg/config.h,
# <work>/src/mpv/build/config.h, <work>/src/libplacebo/build/meson-info.
SRCROOT=${MPV_ANDROID_SRC:-$HERE/work-android/src}
WORK=${MPV_ANDROID_WORK:-$HERE/work-android/$ABI}
PREFIX=$WORK/prefix
BUILD=$WORK/src
OUT=${MPV_OUT:-$HERE/out}
JOBS=${JOBS:-$(nproc)}
mkdir -p "$SRCROOT" "$PREFIX" "$BUILD" "$OUT/$RID"

# Only our own prefix: a host library found by pkg-config would be linked for the wrong machine.
export PKG_CONFIG_LIBDIR="$PREFIX/lib/pkgconfig"
unset PKG_CONFIG_PATH
export PKG_CONFIG_SYSROOT_DIR=
# Static archives list their private dependencies only with --static.
printf '#!/bin/sh\nexec pkg-config --static "$@"\n' > "$WORK/pkg-config-static"
chmod +x "$WORK/pkg-config-static"

fetch() {  # fetch <dir> <git url> <tag>
  local dir=$1 url=$2 tag=$3
  if [ ! -d "$SRCROOT/$dir/.git" ]; then
    log "fetch $dir $tag"
    git -c advice.detachedHead=false clone --depth 1 --branch "$tag" "$url" "$SRCROOT/$dir"
    return
  fi
  local have
  have=$(git -C "$SRCROOT/$dir" describe --tags --exact-match 2>/dev/null || echo "(not at a tag)")
  [ "$have" = "$tag" ] || die "$SRCROOT/$dir is at $have, versions.env wants $tag: delete it and run again"
}

built_from() { [ "$(cat "$PREFIX/stamps/$1" 2>/dev/null)" = "$2" ]; }
record_built() { mkdir -p "$PREFIX/stamps"; printf '%s' "$2" > "$PREFIX/stamps/$1"; }

# The meson cross file: the NDK's clang for this ABI and API, Android as the host.
CROSS=$WORK/android-cross.ini
cat > "$CROSS" <<EOF
[binaries]
c = '$CC'
cpp = '$CXX'
ar = '$TC/bin/llvm-ar'
strip = '$TC/bin/llvm-strip'
ranlib = '$TC/bin/llvm-ranlib'
nm = '$TC/bin/llvm-nm'
pkg-config = '$WORK/pkg-config-static'

[host_machine]
system = 'android'
cpu_family = '$CPU_FAMILY'
cpu = '$CPU'
endian = 'little'

[properties]
pkg_config_libdir = '$PREFIX/lib/pkgconfig'

[built-in options]
c_args = ['-fPIC']
cpp_args = ['-fPIC']
EOF

meson_build() {  # meson_build <name> <source dir> <options...>
  local name=$1 src=$2; shift 2
  rm -rf "$BUILD/$name"
  meson setup "$BUILD/$name" "$src" --cross-file "$CROSS" --prefix="$PREFIX" --libdir=lib \
    --buildtype=release --default-library=static "$@"
  meson compile -C "$BUILD/$name" -j "$JOBS"
  meson install -C "$BUILD/$name"
}

# ──── dav1d: AV1 in software (BSD-2) ────
build_dav1d() {
  fetch dav1d https://code.videolan.org/videolan/dav1d.git "$DAV1D_TAG"
  if built_from dav1d "$DAV1D_TAG"; then log "dav1d: already built"; return; fi
  log "dav1d"
  meson_build dav1d "$SRCROOT/dav1d" -Denable_tools=false -Denable_tests=false
  record_built dav1d "$DAV1D_TAG"
}

# ──── freetype, fribidi, harfbuzz: libass's text (FreeType licence, LGPL-2.1, MIT) ────
# FreeType first without HarfBuzz: HarfBuzz is built on it, and libass needs no FreeType that
# calls back into HarfBuzz (that is for autohinting complex scripts, which subtitles do not use).
build_text() {
  fetch freetype https://gitlab.freedesktop.org/freetype/freetype.git "$FREETYPE_TAG"
  fetch fribidi https://github.com/fribidi/fribidi.git "$FRIBIDI_TAG"
  fetch harfbuzz https://github.com/harfbuzz/harfbuzz.git "$HARFBUZZ_TAG"
  local want="$FREETYPE_TAG $FRIBIDI_TAG $HARFBUZZ_TAG"
  if built_from text "$want"; then log "freetype, fribidi, harfbuzz: already built"; return; fi
  log "freetype"
  meson_build freetype "$SRCROOT/freetype" -Dharfbuzz=disabled -Dpng=disabled -Dbzip2=disabled \
    -Dbrotli=disabled -Dzlib=disabled -Dtests=disabled
  log "fribidi"
  meson_build fribidi "$SRCROOT/fribidi" -Ddocs=false -Dbin=false -Dtests=false
  log "harfbuzz"
  meson_build harfbuzz "$SRCROOT/harfbuzz" -Dfreetype=enabled -Dglib=disabled -Dgobject=disabled \
    -Dcairo=disabled -Dicu=disabled -Dchafa=disabled -Dtests=disabled -Ddocs=disabled \
    -Dintrospection=disabled -Dutilities=disabled
  record_built text "$want"
}

# ──── libass: subtitles (ISC), no font provider ────
build_libass() {
  fetch libass https://github.com/libass/libass.git "$LIBASS_TAG"
  local want="$LIBASS_TAG"
  if built_from libass "$want"; then log "libass: already built"; return; fi
  log "libass"
  [ -x "$SRCROOT/libass/configure" ] || (cd "$SRCROOT/libass" && ./autogen.sh)
  rm -rf "$BUILD/libass"; mkdir -p "$BUILD/libass"
  (cd "$BUILD/libass" && PKG_CONFIG="$WORK/pkg-config-static" CC="$CC" AR="$TC/bin/llvm-ar" RANLIB="$TC/bin/llvm-ranlib" \
    "$SRCROOT/libass/configure" --host="$TRIPLE" --prefix="$PREFIX" --enable-static --disable-shared --with-pic \
      --disable-fontconfig --disable-require-system-font-provider --disable-libunibreak \
    && make -j"$JOBS" && make install)
  record_built libass "$want"
}

# ──── FFmpeg: LGPL only, decode only, MediaCodec ────
build_ffmpeg() {
  fetch ffmpeg https://github.com/FFmpeg/FFmpeg.git "$FFMPEG_TAG"
  local arch_flags=()
  # x86_64 has FFmpeg's assembly (nasm); arm64 its NEON, on by default.
  [ "$ABI" = x86_64 ] && arch_flags=(--x86asmexe=nasm)
  local flags=(--prefix="$PREFIX"
    --target-os=android --arch="$FF_ARCH" --enable-cross-compile
    --cc="$CC" --cxx="$CXX" --ar="$TC/bin/llvm-ar" --ranlib="$TC/bin/llvm-ranlib"
    --nm="$TC/bin/llvm-nm" --strip="$TC/bin/llvm-strip" --pkg-config="$WORK/pkg-config-static"
    --disable-gpl --disable-nonfree --disable-version3
    --disable-cuda-llvm --disable-ffnvcodec --disable-vulkan --disable-vaapi --disable-vdpau
    --disable-programs --disable-doc --disable-debug
    --enable-static --disable-shared --enable-pic
    --disable-avdevice
    --disable-encoders --disable-muxers --disable-devices
    --disable-sdl2 --disable-xlib --disable-libxcb
    --enable-zlib --enable-libdav1d
    --enable-jni --enable-mediacodec
    "${arch_flags[@]}")
  local want="$FFMPEG_TAG dav1d=$DAV1D_TAG ${flags[*]}"
  if built_from ffmpeg "$want"; then log "ffmpeg: already built"; return; fi
  log "ffmpeg: configure"
  rm -rf "$BUILD/ffmpeg"; mkdir -p "$BUILD/ffmpeg"
  (cd "$BUILD/ffmpeg" && "$SRCROOT/ffmpeg/configure" "${flags[@]}")
  log "ffmpeg: build"
  make -C "$BUILD/ffmpeg" -j"$JOBS"
  make -C "$BUILD/ffmpeg" install
  record_built ffmpeg "$want"
}

# ──── libplacebo: GL (ES) only ────
build_libplacebo() {
  fetch libplacebo https://github.com/haasn/libplacebo.git "$LIBPLACEBO_TAG"
  (cd "$SRCROOT/libplacebo" && git submodule update --init --depth 1)
  if built_from libplacebo "$LIBPLACEBO_TAG libc++"; then log "libplacebo: already built"; return; fi
  log "libplacebo"
  # Built in "<work>/src/libplacebo/build", where the licence report reads its options.
  local dir=$BUILD/libplacebo/build
  rm -rf "$BUILD/libplacebo"; mkdir -p "$BUILD/libplacebo"
  meson setup "$dir" "$SRCROOT/libplacebo" --cross-file "$CROSS" --prefix="$PREFIX" --libdir=lib \
    --buildtype=release --default-library=static \
    -Dvulkan=disabled -Dshaderc=disabled -Dglslang=disabled -Dlcms=disabled \
    -Dopengl=enabled -Dd3d11=disabled -Dlibdovi=disabled -Dunwind=disabled -Dxxhash=disabled \
    -Ddemos=false -Dtests=false
  meson compile -C "$dir" -j "$JOBS"
  meson install -C "$dir"
  # libplacebo has C++ in it, and its pkg-config file names no C++ runtime: mpv, linking as C,
  # then left __gxx_personality_v0 undefined, and the library would not load. The NDK's static
  # libc++ goes in its private libraries, so the link takes it right after libplacebo.a, inside
  # the group. (-static-libstdc++ on the C link brings nothing.)
  local pc=$PREFIX/lib/pkgconfig/libplacebo.pc
  grep -q '^Libs.private:' "$pc" \
    && sed -i 's/^Libs.private:\(.*\)$/Libs.private:\1 -lc++_static -lc++abi/' "$pc" \
    || printf 'Libs.private: -lc++_static -lc++abi\n' >> "$pc"
  record_built libplacebo "$LIBPLACEBO_TAG libc++"
}

# ──── mpv: the library, GPL parts off, Android's outputs and decoders on ────
build_mpv() {
  fetch mpv https://github.com/mpv-player/mpv.git "$MPV_TAG"
  log "mpv: configure"
  local dir=$BUILD/mpv/build
  rm -rf "$BUILD/mpv"; mkdir -p "$BUILD/mpv"
  # libc++ comes in with libplacebo (build_libplacebo), so the app ships no libc++_shared.so for it.
  # egl-android: EGL for the AImageReader interop, which renders into the app's GL ES context
  # through the render API; mpv opens no window of its own.
  meson setup "$dir" "$SRCROOT/mpv" --cross-file "$CROSS" --prefix="$PREFIX" --libdir=lib \
    --buildtype=release --default-library=shared \
    -Dgpl=false -Dlibmpv=true -Dcplayer=false \
    -Dlua=disabled -Djavascript=disabled -Dlibarchive=disabled -Dlibbluray=disabled \
    -Ddvdnav=disabled -Dcdda=disabled -Duchardet=disabled -Drubberband=disabled \
    -Dzimg=disabled -Dlcms2=disabled -Dvapoursynth=disabled -Dlibavdevice=disabled \
    -Dvulkan=disabled -Dshaderc=disabled -Dspirv-cross=disabled \
    -Dsdl2-video=disabled -Dsdl2-audio=disabled -Dsdl2-gamepad=disabled \
    -Dmanpage-build=disabled -Dhtml-build=disabled -Dtests=false \
    -Degl-android=enabled -Dandroid-media-ndk=enabled -Daaudio=enabled -Daudiotrack=enabled -Dopensles=disabled \
    -Dx11=disabled -Dwayland=disabled -Degl=disabled -Ddrm=disabled -Dvaapi=disabled -Dvdpau=disabled \
    -Dcuda-hwaccel=disabled -Dcuda-interop=disabled -Dpipewire=disabled -Dpulse=disabled -Dalsa=disabled
  log "mpv: build"
  meson compile -C "$dir" -j "$JOBS"
}

collect() {
  log "collect $RID"
  local dest=$OUT/$RID lib=$OUT/$RID/libmpv.so
  cp "$BUILD/mpv/build/libmpv.so" "$lib"
  "$TC/bin/llvm-strip" --strip-unneeded "$lib"
  ls -la "$dest/"
  local deps
  deps=$("$TC/bin/llvm-readelf" -d "$lib" | grep NEEDED | sed -E 's/.*\[(.*)\].*/\1/' | sort)
  log "runtime dependencies"
  printf '%s\n' "$deps"
  # Only the platform's own libraries: anything else would have to ship beside it.
  local foreign
  foreign=$(printf '%s\n' "$deps" | grep -vE '^lib(c|m|dl|log|z|android|EGL|GLESv2|GLESv3|mediandk|aaudio|OpenSLES)\.so$' || true)
  [ -z "$foreign" ] || die "libmpv.so needs libraries Android does not have: $(echo $foreign)"
  # The C++ runtime is linked in: none of it may be left for the loader, which has none to give
  # ("cannot locate symbol __gxx_personality_v0" at dlopen, seen on the emulator).
  local undefined cxx
  undefined=$("$TC/bin/llvm-nm" -D --undefined-only "$lib")
  # (__cxa_atexit and __cxa_finalize are bionic's, @LIBC, and there on every device.)
  cxx=$(grep -E ' (_Z|__cxa_|__gxx_)' <<<"$undefined" | grep -v '@LIBC$' | head -5 || true)
  [ -z "$cxx" ] || die "libmpv.so leaves C++ runtime symbols undefined: $(echo $cxx)"
  # The Java VM goes in through this, from HultUI.Media.Mpv: MediaCodec decoding needs it.
  # (Read whole, not piped into grep -q: under pipefail, grep -q's early exit fails llvm-nm.)
  local exported
  exported=$("$TC/bin/llvm-nm" -D --defined-only "$lib")
  grep ' T av_jni_set_java_vm$' <<<"$exported" >/dev/null \
    || die "libmpv.so does not export av_jni_set_java_vm: MediaCodec would have no Java VM"
  REPORT_TARGET=android bash "$HERE/licence-report.sh" "$WORK" > "$dest/LICENCE-REPORT.txt"
  { echo; echo "Runtime dependencies of libmpv.so ($ABI, API $API, NDK $ndk_rev)"; printf '  %s\n' $deps; } >> "$dest/LICENCE-REPORT.txt"
  cat "$dest/LICENCE-REPORT.txt"
}

build_dav1d
build_text
build_libass
build_ffmpeg
build_libplacebo
build_mpv
collect
log "done: $OUT/$RID/libmpv.so"
