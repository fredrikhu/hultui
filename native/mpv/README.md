# A trimmed, LGPL libmpv for HultUI.Media.Mpv

`HultUI.Media.Mpv` ships no native library. This directory builds the one it is
meant to run with: libmpv with the GPL parts left out, the pieces a desktop video
player never uses left out, and its own LGPL FFmpeg, libplacebo and libass linked
in — so an application that ships it takes on LGPL-2.1 obligations and nothing more.

| Component | Version (`versions.env`) | Licence | Role |
|---|---|---|---|
| mpv | v0.41.0, `-Dgpl=false` | LGPL-2.1+ | playback engine, render API |
| FFmpeg | n8.1.2, no `gpl`/`nonfree`/`version3` | LGPL-2.1+ | demuxers, decoders, protocols |
| libplacebo | v7.360.1, GL only | LGPL-2.1+ | mpv's GPU video pipeline |
| libass | 0.17.5 (MSYS2 package on Windows) | ISC | subtitles |
| dav1d | host package | BSD-2 | AV1 |
| freetype, fribidi, harfbuzz, zlib, brotli, libpng | host packages | permissive | text and compression |

Any other libmpv — a distribution's, a community build — works with the backend just
the same, but those are GPL. The licence is the whole reason this directory exists.

## What is trimmed, and why

- **GPL code**: mpv's `-Dgpl=false` drops the files never relicensed to LGPL (a few
  legacy outputs and filters). FFmpeg is configured without `--enable-gpl`,
  `--enable-nonfree` and `--enable-version3`, so no x264/x265, no LGPL-3-only bits.
- **Encoding**: FFmpeg's encoders, muxers and devices are off. The player only decodes.
- **Scripting and discs**: no Lua, no JavaScript, no yt-dlp hook, no libarchive, no
  DVD, Blu-ray or CD. A player component does not need them and each is a dependency.
- **Vulkan and shader compilers**: libplacebo and mpv are GL-only. The framework
  renders with GL, and Vulkan plus shaderc is most of a full libmpv's size.
- **Rubberband, zimg, lcms2, uchardet, VapourSynth**: off. Pitch-corrected speed uses
  mpv's own scaletempo2, which stays.
- **Windows**: one self-contained `libmpv-2.dll`; the static runtime and every
  dependency are linked in. Hardware decoding through D3D11VA/DXVA2 (copy-back).
- **Linux**: `libmpv.so.2` with FFmpeg, libplacebo and libass inside it; the base
  libraries (freetype, fribidi, harfbuzz, fontconfig, zlib, ALSA, PulseAudio, GL) come
  from the distribution, as any desktop has them. No VAAPI in this first cut.

## Building

Windows, in an MSYS2 **MINGW64** shell (a plain MSYS2 extracted anywhere is enough —
it needs no system install):

```bash
pacman -S --needed base-devel git mingw-w64-x86_64-{toolchain,meson,ninja,pkgconf,nasm,python,python-jinja,libass,dav1d,zlib,freetype,fribidi,harfbuzz,libpng,brotli,bzip2,libiconv}
./build-libmpv.sh
```

Ubuntu 24.04 (the Linux runner):

```bash
sudo apt install -y build-essential git meson ninja-build nasm pkg-config python3 python3-jinja2 autoconf automake libtool \
  libfreetype-dev libfribidi-dev libharfbuzz-dev libfontconfig-dev libdav1d-dev zlib1g-dev \
  libasound2-dev libpulse-dev libgl-dev
./build-libmpv.sh
```

Sources are cloned into `work/src` at the pinned tags, built into `work/prefix`, and
the result lands in `out/<rid>/` with a `LICENCE-REPORT.txt` that `licence-report.sh`
writes from the configured trees. The report fails the build if any GPL switch is
found on. `JOBS=n` sets parallelism; `MPV_WORK` and `MPV_OUT` move the directories.

To use the result: `HULTUI_MPV_PATH=out/win-x64/libmpv-2.dll dotnet test
tests/HultUI.Media.Backends.Tests`, or pack it — `src/HultUI.Media.Mpv.Natives` picks up
whatever `out/` holds and puts it under `runtimes/<rid>/native/`.

In CI, `.github/workflows/mpv-natives.yml` runs this on both self-hosted runners,
proves each library with the backend tests (`HULTUI_MPV_EXPECT_LGPL=1`), and packs.
Binaries move between jobs as assets of a GitHub release through
`release-transport.sh` — a dispatch run fills the rolling `mpv-natives-ci`
prerelease, a `mpv-natives-v*` tag fills its own release, `.nupkg` included.

## Complete corresponding source

The LGPL asks that a recipient of the binary can get its source. It is: the upstream
repositories at the tags in `versions.env`, plus the two scripts here, which are the
whole of the modification (there are no patches). `THIRD-PARTY-NOTICES.md` in the
natives package says the same to whoever receives it.
