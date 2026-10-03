# HultUI.WebView.Cef.Natives.&lt;rid&gt;

CEF (the Chromium Embedded Framework) for `HultUI.WebView.Cef`, for Linux distributions that
do not package it: Debian and Ubuntu. On Fedora (`dnf install cef`) and Arch the engine uses
the distribution's CEF and this package is not needed. Add the one for your RID:

```xml
<PackageReference Include="HultUI.WebView.Cef" Version="..." />
<PackageReference Include="HultUI.WebView.Cef.Natives.linux-x64" Version="..." />
```

Inside is CEF's official "minimal" distribution for the version in `CEF-VERSION`, as
published on cef-builds.spotifycdn.com and checked against its SHA-1, laid out flat (CEF finds
`icudtl.dat` only beside `libcef.so`) and stripped. About 150 MB packed, 360 MB on disk. The
build is `native/cef/build.sh` in the HultUI repository.

**Where it lands.** In `cef/` beside the application, on build and on publish (the package's
`buildTransitive` targets copy it there, for a Linux RID, or with no RID when the build runs
on Linux; a build on Windows or macOS gets none of it). The engine
looks in `HULTUI_CEF_DIR`, then `cef/`, then `runtimes/<rid>/native/cef/`, then the
distribution's, so a bundled CEF wins over Fedora's.

**The sandbox.** Chromium's renderers run in a sandbox made of user namespaces. The setuid
`chrome-sandbox` that CEF also ships is left out: no package can install a file setuid root.
On Ubuntu 23.10 and later, AppArmor stops unprivileged programs from making user namespaces
unless a profile allows it, so an application that bundles CEF needs one, installed by its
package. HultUI's design notes (`docs/design/WEBVIEW-LINUX-BRIEF.md`, step 5.7) give the
options.

**What a page can play.** CEF's official builds leave out the patent-encumbered codecs:
H.264 and AAC. A page's MP4 video in those formats does not play with this package, while
WebM, VP9, AV1 and Opus do. Fedora's CEF decodes through the system's ffmpeg instead, which
plays whatever that ffmpeg has (RPM Fusion's has the full set).

**Licences.** CEF is BSD-3-Clause; Chromium and its components are under their own licences,
listed in `CEF-CREDITS.html` beside `libcef.so`, with `CEF-LICENSE.txt`.
