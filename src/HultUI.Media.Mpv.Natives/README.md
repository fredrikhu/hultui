# HultUI.Media.Mpv.Natives

The native libmpv that `HultUI.Media.Mpv` plays through. Add it beside that package
and the backend finds the library on its own:

```xml
<PackageReference Include="HultUI.Media.Mpv" Version="..." />
<PackageReference Include="HultUI.Media.Mpv.Natives" Version="..." />
```

It is a trimmed LGPL-2.1 build: mpv with its GPL parts left out, FFmpeg without
`gpl`, `nonfree` or `version3`, libplacebo GL-only, libass, dav1d — decoding only, no
scripting, no discs, and no Vulkan output of mpv's own (the framework's Vulkan renderer
still gets HDR video, through a GL image it shares). It has NVDEC with CUDA interop, so an
NVIDIA machine decodes zero-copy; NVIDIA's driver libraries are loaded when first used,
never linked, so the library loads on any machine and the build checks that it stays so.
Windows gets one self-contained `libmpv-2.dll`; Linux
gets `libmpv.so.2` that needs the distribution's freetype, fribidi, harfbuzz,
fontconfig, zlib, GL, and all three of libpipewire-0.3, libpulse and libasound — each is
linked, not loaded on demand, so a machine without one of them cannot load the library at
all. (PipeWire is the audio output; PulseAudio and ALSA are its fallbacks when no PipeWire
daemon is running.) When that happens the backend says which library was missing rather
than that libmpv was not found.

Shipping it makes your application a distributor of LGPL software. The obligations
are the usual ones — keep the notices, let users replace the library, point them at
the source — and `THIRD-PARTY-NOTICES.md` in this package plus the
`LICENCE-REPORT.txt` beside each binary give you what to point at. The build is
`native/mpv/` in the HultUI repository, pinned to upstream tags, with no patches.
