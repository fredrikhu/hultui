# Continuous integration

Every workflow in `.github/workflows/` runs on **GitHub-hosted runners**. The repository is
public, and a self-hosted runner attached to a public repository runs whatever code reaches a
workflow on it, on a machine that keeps its state between jobs. So **do not register a
self-hosted runner on this repository**, not even for one job.

Until 2026-10-24, when the project lived in the private `fredrikhu/hultui-revamped`, CI ran on
two self-hosted runners: the development machine and an Ubuntu container on Proxmox. That
setup guide is in the history: `git show d2bc157b:docs/SELF-HOSTED-RUNNERS.md`.

## The workflows

| Workflow | Runs on | Jobs | Proves or produces |
|---|---|---|---|
| `ci.yml` | push to `main`, by hand | `test` (Windows), `test-linux`, `docs` | the whole solution's tests on both OSes; the DocFX site with warnings as errors |
| `<app>-installer.yml` (Junction, Vesper, Meridian, Aperture, Reel) | an `<app>-vX.Y.Z` tag, by hand | `test`, `test-linux` (Junction, Meridian), `installer` | the app's tests, a NativeAOT build, an Inno Setup installer; a GitHub release on a tag |
| `mpv-natives.yml` | an `mpv-natives-v*` tag, by hand | `windows`, `linux`, `package` | the trimmed LGPL libmpv for both OSes, proved by the backend tests, and `HultUI.Media.Mpv.Natives` |
| `cef-natives.yml` | a `cef-natives-v*` tag, by hand | `pack` | `HultUI.WebView.Cef.Natives.<rid>` for linux-x64 and linux-arm64, proved by the engine tests |
| `cef-helper.yml` | a change to `native/cef-helper/**`, a `cef-helper-v*` tag, by hand | `build` | `hultui-cef-helper` for both Linux RIDs, and `HultUI.WebView.Cef` |

"By hand" is `workflow_dispatch`:

```bash
gh workflow run ci.yml --ref <branch>
```

A dispatch of an installer workflow builds a `0.1.0-ci.<run>` installer as an artifact and
publishes no release. A dispatch of a natives workflow publishes to that workflow's rolling
`*-ci` prerelease.

## The images

The jobs name `windows-2025` and `ubuntu-24.04`, not `-latest`. A `-latest` label moves to a
new image on GitHub's schedule, and the apt package names in these workflows are Ubuntu
24.04's (the `t64` names, `7zip`). Moving to a newer image is a change to every workflow at
once, made on purpose.

What the jobs add to the image:

| Job | Adds | Why |
|---|---|---|
| every job that runs `dotnet` | `actions/setup-dotnet`, `10.0.x` | there is no `global.json`, so any 10.0 SDK builds the solution, and the newest is taken |
| `test-linux` | `fonts-dejavu-core fonts-noto-cjk fonts-noto-core fonts-noto-color-emoji 7zip` | see the next section |
| `test-linux`, `cef-natives` | `sysctl kernel.apparmor_restrict_unprivileged_userns=0` | Ubuntu 24.04 forbids an unconfined program a user namespace, so Chromium's sandbox cannot start: the CEF engine tests time out and `CefSandboxTests.OnThisMachine_TheSandboxCanStart` fails. The old runner, a Proxmox container, had its host's 0. Not something to ask of a user (`docs/design/WEBVIEW-LINUX-BRIEF.md`), but a hosted VM is thrown away after its job |
| installers | Inno Setup, when the image lacks it | the step falls back to `choco install innosetup` |
| `mpv-natives` Windows | an MSYS2 MINGW64 toolchain and the packages `native/mpv/README.md` lists | through `msys2/setup-msys2` |
| `mpv-natives` Linux | the apt list in `native/mpv/README.md` | |
| `cef-natives` | `bzip2`, both `binutils` (a `strip` per RID), CEF's runtime libraries, Mesa | `native/cef/build.sh`, and the engine tests on the packed CEF |
| `cef-helper` | `gcc-aarch64-linux-gnu` | to cross-compile the linux-arm64 helper |

The Windows image already has what NativeAOT needs (Visual Studio with the C++ workload),
Python, `gh`, Git for Windows and 7-Zip.

## Tests that skip on a missing font or tool

Some tests need something a machine may not have, and skip rather than fail, naming what to
install:

| Attribute | Needs | Linux package |
|---|---|---|
| `[HanFontsFact]` | a font with Han ideographs | `fonts-noto-cjk` |
| `[DevanagariFontsFact]` | a font that shapes Devanagari | `fonts-noto-core` |
| `[ColourEmojiFact]`, `[ColourEmojiTheory]` | a colour emoji face | `fonts-noto-color-emoji` |
| `[SevenZipFact]` (Vesper) | a `7z` on the path | `7zip` |
| `[MpvFact]` | libmpv (`HULTUI_MPV_PATH`) | none: `mpv-natives.yml` points the backend tests at the build |
| `[CefFact]` | a usable CEF (`HULTUI_CEF_DIR`) | none: `cef-natives.yml` points the engine tests at the packed CEF |

So a run that reports more skipped tests than the last one is usually a runner missing a
package. `cef-natives.yml` fails on purpose when its CEF tests skip, because a package proved
by skipped tests is not proved.

Golden images do not depend on installed fonts: `HultUI.Headless` bundles the DejaVu files they
draw with.

The UI-automation tests in `HultUI.UiTests` open real windows and need a GPU. They skip unless
`HULTUI_UITESTS` is set, and no workflow sets it.

## Rules for a public repository

- **Triggers.** Pushes (which need write access), tags and `workflow_dispatch` only. No
  `pull_request_target` and no `workflow_run`: both run with this repository's token on code
  someone else chose. If CI for contributors' pull requests is wanted later, use
  `pull_request`, which gives a fork's run a read-only token and no secrets, and keep the
  repository setting that asks for approval before a first-time contributor's workflows run.
- **Token.** The repository default is read-only (Settings → Actions → General → Workflow
  permissions). Every workflow declares `contents: read` at the top. Only a job that publishes a
  release declares `contents: write`, and only the steps that publish get `GH_TOKEN`. The
  natives builds compile a dozen upstream projects with no token in their environment.
- **Checkout.** Every `actions/checkout` sets `persist-credentials: false`, so the token is not
  left in `.git/config` for later steps. Nothing after checkout needs git credentials.
- **Tag names.** A tag's version is checked against `X.Y.Z[-suffix]` before it reaches a
  command line, and it reaches one through an environment variable, not `${{ }}` inside a
  script.
- **Actions.** GitHub's own (`actions/*`) are used by major version. A third-party action is
  pinned to a full commit SHA with the version in a comment, as `msys2/setup-msys2` is.
- **Secrets.** None are used. Adding one to a workflow that builds fetched upstream code is a
  decision to make carefully, not a convenience.
- **Artifacts.** In a public repository, anyone can download them. Nothing secret goes in.
- **Timeouts.** Every job has `timeout-minutes`, well above its normal time, so a hang ends
  instead of running for GitHub's default of six hours.

## Releases

Pushing `<app>-vX.Y.Z` (for example `junction-v1.22.0`) runs that app's installer workflow, which
creates the GitHub release with `GITHUB_TOKEN` and attaches the installer.

- **Push at most three tags in one `git push`.** For a larger push GitHub fires no push events
  at all, so no release workflow starts. If that happens, start each one from its tag:

  ```bash
  gh workflow run junction-installer.yml --ref junction-v1.22.0
  ```

- **The natives releases did not move with the repository.** Aperture's and Reel's installers
  download libmpv from this repository's `mpv-natives-ci` prerelease, so that release has to
  exist here first: `gh workflow run mpv-natives.yml` creates it. The same holds for
  `cef-natives-ci` and `cef-helper-ci`.

## Cost and speed

Standard GitHub-hosted runners are free and unmetered for a public repository. For a private
one, minutes count against the account's allowance, and Windows minutes count double.

Hosted runners start from a clean image every time, so each job restores NuGet packages and
the natives builds fetch their sources again. They are also slower than the development
machine the old Windows runner was, and a private repository's standard runners have half the
cores of a public one's (2 against 4). Measured on the first runs, 2026-10-24, with the
repository still private:

| Job | Hosted, private (2 cores) | Self-hosted before |
|---|---|---|
| `ci.yml` `docs` | 2 min | |
| `ci.yml` `test` (Windows) | 25.5–26 min: build 4, tests 21–22 | |
| — of which `Junction.Tests` | 15–17 min | about 3.5 min |
| — `HultUI.Rendering.Skia.Tests`, `VesperCommander.Tests`, `HultUI.Text.Tests` | 2.5–4.5, 1–3.5, 2.5 min | |
| `ci.yml` `test-linux` | 8.5 min: tests 8 (one run of the same job in `junction-installer.yml` took 21) | |
| `<app>-installer.yml` `installer` | 3–4 min: NativeAOT publish, Inno Setup (on the image) | |
| Vesper, Aperture, Reel `test` | 2.5–5.5 min | |
| `mpv-natives.yml` | 22 min: Windows 21 (MSYS2 1.5, build 18), Linux 13, package 1 | |
| `cef-natives.yml` | 7 min | |
| `cef-helper.yml` | 2 min | |

The Windows job is the long pole, and `Junction.Tests` is most of it. Splitting that
assembly into several test processes measured 4 min → 1.7 min on the development machine, and
is the next lever if the time matters.

## Traps

- **A `run:` step in `pwsh` passes or fails on its last command only.** Two `dotnet test`
  lines in one step used to pass when the first failed and the second passed. Check
  `$LASTEXITCODE` between commands, as the Aperture and Reel test steps do.
- **A job whose `runs-on` matches no runner waits** — for up to a day — rather than failing.
  That is how the first push to this repository looked: its jobs still asked for
  `[self-hosted, windows, x64]`.
- **The MSYS2 toolchain is always today's.** `msys2/setup-msys2` with `update: true` installs
  the current packages, and MSYS2 builds `mingw-w64-crt` from upstream's git. The old runner's
  MSYS2 had stayed at crt r375; the first hosted run got r532, which moved `__mingw_fe_dfl_env`
  into `libmingw32.a` and broke the static `libmpv-2.dll` link (worked around in
  `native/mpv/build-libmpv.sh`). A native build that fails at a link step with nothing changed
  here is most likely the toolchain moving: compare the `pacman` versions in the log.
- **Ubuntu 24.04 limits user namespaces.** See the `sysctl` row above: without it, every CEF
  engine test times out after ten seconds with "CEF is not running", which looks like a hang,
  not a missing permission.
