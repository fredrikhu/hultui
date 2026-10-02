# hultui-cef-helper

The executable the CEF engine (`HultUI.WebView.Cef`) names as CEF's
`browser_subprocess_path`: Chromium starts its zygote, renderer, GPU and utility processes
from it. About forty lines of C: `dlopen` libcef, the API handshake, `cef_execute_process`.

**Why C.** Chromium's zygote has to be single-threaded to enter its sandbox and cannot fork a
.NET process even unsandboxed; every .NET executable, NativeAOT included, has threads before
`Main`. Spike L0's question 1 tried every .NET shape (`spikes/CefOsr/README.md`); the owner
decided on this helper on 2026-10-02 (`docs/design/WEBVIEW-LINUX-BRIEF.md`). It is the
engine's only native code.

**What it knows.** Nothing of a CEF release or of the engine's version: the browser process
sets `HULTUI_CEF_DIR` (the directory holding `libcef.so`) and `HULTUI_CEF_API_VERSION` in the
environment before `cef_initialize`, and every child inherits them. So one helper serves any
CEF that offers the engine's API version, and needs rebuilding only for a new architecture.

**Building.**

- `HultUI.WebView.Cef`'s own build compiles it for the build machine's RID when the build runs
  on Linux and `cc` is there, into `out/<rid>/`, and every application that references the
  engine gets it in `runtimes/<rid>/native/`. Without a C compiler the build warns and
  `CefEngine.Availability` says the helper is missing.
- `build.sh [rid] [cc]` builds one RID, `linux-arm64` with a cross compiler
  (`build.sh linux-arm64 aarch64-linux-gnu-gcc`). The `cef-helper` workflow builds both and
  packs `HultUI.WebView.Cef` with them.

`out/` is not committed.
