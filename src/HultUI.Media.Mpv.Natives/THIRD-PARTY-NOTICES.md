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
| nv-codec-headers (FFmpeg's NVDEC/CUDA loader headers; their loader code is compiled in) | MIT — notice below | https://github.com/FFmpeg/nv-codec-headers |
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

## nv-codec-headers

The MIT licence asks for its notice to travel with the software. The headers carry it per
file: `dynlink_loader.h` and `dynlink_cuda.h` under "Copyright (c) 2016", and
`dynlink_cuviddec.h` and `dynlink_nvcuvid.h` under "Copyright (c) 2010-2023 NVIDIA
Corporation", each with this permission notice:

> Permission is hereby granted, free of charge, to any person obtaining a copy of this
> software and associated documentation files (the "Software"), to deal in the Software
> without restriction, including without limitation the rights to use, copy, modify, merge,
> publish, distribute, sublicense, and/or sell copies of the software, and to permit persons
> to whom the software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or
> substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
> INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
> PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
> FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
> OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
> DEALINGS IN THE SOFTWARE.

The loader opens the NVIDIA driver's own libraries (`nvcuda.dll`, `libcuda.so.1`) at run
time; nothing from the driver is in this package, and the build fails if the library ever
links to them.
