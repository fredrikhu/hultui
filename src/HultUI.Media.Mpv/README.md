# HultUI.Media.Mpv

The libmpv backend for `HultUI.Media`: files, URLs and streams in every format
FFmpeg knows, hardware decoding, audio, subtitles, playback speed with pitch
correction, and GPU rendering straight into the framework's compositor.

```csharp
using var player = new MediaPlayer(new MpvBackend());
player.Open(MediaSource.FromFile("talk.mkv"));
app.SetRoot(new VideoPlayer(player).Mount());
```

## Bring your own libmpv

No native library ships in this package. The backend loads `libmpv-2.dll` on
Windows, `libmpv.so.2` on Linux and `libmpv.2.dylib` on macOS, looking:

1. in the directory (or at the file) named by `HULTUI_MPV_PATH`, if set;
2. beside the application;
3. wherever the operating system's loader looks (`PATH`, `LD_LIBRARY_PATH`, the
   usual library directories).

`MpvBackend.IsAvailable` says whether it was found, without throwing.
`HULTUI_MPV_LOG=warn` (or `info`, `v`, `debug`) forwards mpv's own log to stderr,
the fastest way to learn why a source would not open.

Verified with shinchiro's `mpv-dev-x86_64` build of 2026-09-03 (client API 2.5).

The build to use is the project's own trimmed LGPL build (`-Dgpl=false`, no Lua,
JavaScript, yt-dlp or disc support, against an LGPL FFmpeg), published per RID
alongside the packages. Any other libmpv works the same way — a distribution's
package, a community build — but those are GPL, which is your application's
licence question to answer, not this package's.

## What the backend does

- Opens with `pause=yes`, so a source loads paused and reports its duration,
  size and tracks before anything moves; `Play` starts it.
- `keep-open=yes`: reaching the end stops at the last frame rather than
  unloading; `Play` from there restarts from the beginning.
- Frames go through mpv's render API. On a GL surface mpv renders into a
  framebuffer the sink owns and Skia composites the texture; on a raster or
  headless surface mpv's software renderer writes pixels into the sink's ring
  on a worker thread. The sink decides which at the first draw.
- Hardware decoding is `auto-copy`: decoded on the GPU where a decoder exists,
  copied back for mpv's own GL pipeline, which is what desktop GL on Windows
  needs.
