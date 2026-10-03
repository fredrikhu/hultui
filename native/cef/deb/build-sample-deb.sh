#!/usr/bin/env bash
# A worked example of packaging an application with a bundled CEF as a .deb: the HultUI web view
# sample, as tested on Ubuntu 24.04 and Debian 13 (docs/design/WEBVIEW-LINUX-BRIEF.md, step 7).
# Run where dpkg-deb is, with the NativeAOT publish (taking HultUI.WebView.Cef.Natives) in ~/sample:
#   dotnet publish samples/HultUI.Samples.WebView -c Release -r linux-x64 -p:CefNativesVersion=<v> -p:CefNativesFeed=<feed>
# An application replaces the names (hultui-sample, /opt/hultui-sample, the executable) with its own.
# Builds hultui-sample_0.1_amd64.deb from ~/sample: the application in /opt/hultui-sample, its
# AppArmor profile as a conffile, and the maintainer scripts that load and unload it.
set -e
root=$(mktemp -d)
mkdir -p $root/DEBIAN $root/opt $root/etc/apparmor.d $root/usr/bin
cp -a ~/sample $root/opt/hultui-sample
ln -s /opt/hultui-sample/HultUI.Samples.WebView $root/usr/bin/hultui-sample
cat > $root/etc/apparmor.d/hultui-sample <<'EOF'
# Chromium (CEF) sandboxes its renderers in user namespaces, which Ubuntu 23.10 and later
# allow only a program whose profile says so. The browser process makes them, so the profile
# names the application's own executable; CEF's processes inherit it. Unconfined otherwise.
abi <abi/4.0>,
include <tunables/global>

profile hultui-sample /opt/hultui-sample/HultUI.Samples.WebView flags=(unconfined) {
  userns,

  # Site-specific additions and overrides. See local/README for details.
  include if exists <local/hultui-sample>
}
EOF
cat > $root/DEBIAN/control <<'EOF'
Package: hultui-sample
Version: 0.1
Architecture: amd64
Maintainer: HultUI <noreply@example.invalid>
Description: HultUI web view sample with a bundled CEF
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
EOF
echo /etc/apparmor.d/hultui-sample > $root/DEBIAN/conffiles
cat > $root/DEBIAN/postinst <<'EOF'
#!/bin/sh
# Loads the AppArmor profile, as dh_apparmor's snippet does: on a system without AppArmor (a
# Debian container, a kernel without it) there is nothing to load, and nothing fails.
set -e
if [ "$1" = "configure" ]; then
    profile=/etc/apparmor.d/hultui-sample
    local=/etc/apparmor.d/local/hultui-sample
    [ -e "$local" ] || { mkdir -p "$(dirname "$local")"; install --mode 644 /dev/null "$local"; }
    if command -v aa-enabled >/dev/null 2>&1 && aa-enabled --quiet 2>/dev/null; then
        apparmor_parser --replace --write-cache --skip-read-cache "$profile" || true
    fi
fi
exit 0
EOF
cat > $root/DEBIAN/prerm <<'EOF'
#!/bin/sh
# Unloads the profile while its file is still there; the file itself is a conffile, kept
# until purge.
set -e
if [ "$1" = "remove" ] && command -v aa-enabled >/dev/null 2>&1 && aa-enabled --quiet 2>/dev/null; then
    apparmor_parser --remove /etc/apparmor.d/hultui-sample 2>/dev/null || true
fi
exit 0
EOF
cat > $root/DEBIAN/postrm <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "purge" ]; then
    rm -f /etc/apparmor.d/local/hultui-sample
    rmdir --ignore-fail-on-non-empty /etc/apparmor.d/local 2>/dev/null || true
fi
exit 0
EOF
chmod 755 $root/DEBIAN/postinst $root/DEBIAN/prerm $root/DEBIAN/postrm
dpkg-deb --root-owner-group --build $root ~/hultui-sample_0.1_amd64.deb >/dev/null
ls -la ~/hultui-sample_0.1_amd64.deb
rm -rf $root
