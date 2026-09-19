# Third-party notices — HultUI.Media.Mpv.Natives

The native library in this package is built from the following projects, unmodified,
at the versions recorded in `native/mpv/versions.env` of the HultUI repository and in
the `LICENCE-REPORT.txt` beside each binary. The build scripts in `native/mpv/` are
the complete description of how; there are no patches.

| Project | Licence | Source |
|---|---|---|
| mpv (built with `-Dgpl=false`) | LGPL-2.1-or-later | https://github.com/mpv-player/mpv |
| FFmpeg (built without `--enable-gpl`, `--enable-nonfree`, `--enable-version3`) | LGPL-2.1-or-later | https://github.com/FFmpeg/FFmpeg |
| libplacebo | LGPL-2.1-or-later | https://github.com/haasn/libplacebo |
| libass | ISC | https://github.com/libass/libass |
| dav1d | BSD-2-Clause | https://code.videolan.org/videolan/dav1d |
| FreeType | FTL (BSD-style) | https://freetype.org |
| HarfBuzz | MIT (Old MIT) | https://github.com/harfbuzz/harfbuzz |
| FriBidi | LGPL-2.1-or-later | https://github.com/fribidi/fribidi |
| libpng | PNG Reference Library License | http://www.libpng.org |
| zlib | zlib | https://zlib.net |
| Brotli | MIT | https://github.com/google/brotli |
| bzip2 | bzip2 licence (BSD-style) | https://sourceware.org/bzip2 |
| libiconv (Windows build) | LGPL-2.1-or-later | https://www.gnu.org/software/libiconv |
| mingw-w64 runtime, winpthreads (Windows build) | permissive (ZPL/MIT-style) | https://www.mingw-w64.org |

Under the LGPL, a recipient of this package may relink the application against a
modified libmpv: the library is a separate shared object (`libmpv-2.dll`,
`libmpv.so.2`) loaded at run time, replaceable in place, and the corresponding source
is the projects above at the recorded versions.

The full text of the GNU Lesser General Public License, version 2.1, is at
https://www.gnu.org/licenses/old-licenses/lgpl-2.1.txt.
