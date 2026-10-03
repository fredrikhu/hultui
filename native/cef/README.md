# CEF for Debian and Ubuntu

`build.sh <rid>` makes the CEF that `HultUI.WebView.Cef.Natives.<rid>` carries: CEF's official
"minimal" distribution for the version and SHA-1s in `versions.env`, from
cef-builds.spotifycdn.com, flattened into one directory (CEF finds `icudtl.dat` only beside
`libcef.so`) and stripped (the official `libcef.so` is 1.45 GB with its debug sections, 260 MB
without), into `out/<rid>/cef/`. `chrome-sandbox` is left out: it works only setuid root,
which a package cannot carry.

```sh
native/cef/build.sh linux-x64                 # downloads to cache/ the first time
native/cef/build.sh linux-x64 path/to/cef_binary_..._linux64_minimal.tar.bz2
dotnet pack src/HultUI.WebView.Cef.Natives -p:Rid=linux-x64 -o artifacts/packages
HULTUI_CEF_DIR=$PWD/native/cef/out/linux-x64/cef dotnet test tests/HultUI.WebView.Engines.Tests
```

Sizes for 154.0.32 on linux-x64: a 326 MB download, 356 MB unpacked and stripped, a 154 MB
package (114 MB if it were xz). arm64's download is 424 MB.

To move to a newer CEF, change `versions.env` (the version and both SHA-1s are in
`https://cef-builds.spotifycdn.com/index.json`). The engine is written against API version
14600, which every CEF from 146 on offers, so no engine change is needed until CEF drops it.
The `cef natives` workflow (`.github/workflows/cef-natives.yml`) builds, proves and publishes
both packages on a `cef-natives-v*` tag.
