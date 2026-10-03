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

**The sandbox, and Ubuntu.** Chromium sandboxes its renderers in user namespaces, which the
browser process (your application's own executable) makes. Ubuntu 23.10 and later let a
program do that only under an AppArmor profile that allows it. Without one the engine reports
itself unavailable, with the reason, rather than let CEF end the process ("No usable
sandbox!"). So **an application that bundles CEF ships as its own executable** (a NativeAOT or
apphost publish, not `dotnet app.dll`: a profile names one executable, and naming `dotnet` would
allow every .NET program), and **its `.deb` installs a profile for that executable**. CEF's own
processes inherit it; the helper needs no profile of its own (tested: a profile on the helper
alone does not help). The profile, as a conffile in `/etc/apparmor.d/<app>`:

```
abi <abi/4.0>,
include <tunables/global>

profile <app> /opt/<app>/<executable> flags=(unconfined) {
  userns,

  # Site-specific additions and overrides. See local/README for details.
  include if exists <local/<app>>
}
```

and the maintainer scripts that load it (`postinst configure`: make the empty
`/etc/apparmor.d/local/<app>`, then `apparmor_parser --replace --write-cache
--skip-read-cache` when `aa-enabled` says AppArmor is on) and unload it (`prerm remove`:
`apparmor_parser --remove`; `postrm purge`: remove the local file). debhelper's `dh_apparmor`
writes the same. A tested example builds the whole package: `native/cef/deb/build-sample-deb.sh`
in the HultUI repository. On a system without AppArmor (Debian, Fedora) the scripts do nothing.

The setuid `chrome-sandbox` CEF also ships is left out of this package: no package can install
a file setuid root. `HULTUI_CEF_SANDBOX_CHECK=0` skips the engine's check, for a system it
misjudges.

**What the application's `.deb` depends on.** Measured on Debian 13 and Ubuntu 24.04 (the
libraries the application and CEF link or load), in names both have:

```
Depends: libc6, libgcc-s1, libstdc++6,
 libx11-6, libx11-xcb1, libxcb1, libxcomposite1, libxdamage1, libxext6, libxfixes3,
 libxrandr2, libxcursor1, libxi6, libxkbcommon0,
 libwayland-client0, libwayland-cursor0, libwayland-egl1,
 libegl1, libgl1, libgbm1, libvulkan1,
 libasound2t64 | libasound2, libatk1.0-0t64 | libatk1.0-0,
 libatk-bridge2.0-0t64 | libatk-bridge2.0-0, libatspi2.0-0t64 | libatspi2.0-0,
 libcups2t64 | libcups2, libglib2.0-0t64 | libglib2.0-0,
 libcairo2, libpango-1.0-0, libdbus-1-3, libexpat1, libfontconfig1, libfreetype6,
 libnspr4, libnss3, libudev1
Recommends: libdecor-0-0, libegl-mesa0, libgl1-mesa-dri, mesa-vulkan-drivers
```

**Licences.** CEF is BSD-3-Clause; Chromium and its components are under their own licences,
listed in `CEF-CREDITS.html` beside `libcef.so`, with `CEF-LICENSE.txt`.
