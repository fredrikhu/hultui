# Self-hosted build hosts

Optional. The installer workflows run on GitHub-hosted runners and only fire on release
tags, which fits comfortably inside the free allowance. Self-hosted runners buy independence
from that allowance and faster builds (warm NuGet caches, no image provisioning), at the cost
of two machines to keep alive.

Minutes on a self-hosted runner are not metered at all, on any plan.

## Why the workflows are tags-only

Both installer workflows used to run on every push to `main`. They use `windows-latest`, which
bills at **twice** the minute rate, and two apps building per push exhausted a month's free
2,000 minutes in nine days. They now trigger only on `vesper-v*` / `junction-v*`, with
`workflow_dispatch` for a deliberate off-tag build.

If you add self-hosted runners you *could* put per-push CI back, since those minutes are free.
Weigh that against the machines needing to be awake for a queued job to start.

## Windows runner (the development machine)

This covers the `test` and `installer` jobs of both workflows.

1. Repo → Settings → Actions → Runners → **New self-hosted runner** → Windows x64.
2. Run the commands GitHub shows. They embed a short-lived registration token and the current
   runner version, so use them as given rather than from a copy kept here.
3. When `config.cmd` asks whether to run as a service, say yes — otherwise the runner only
   exists while a console window is open.
4. Point the jobs at it:

   ```yaml
   runs-on: [self-hosted, windows, x64]
   ```

   in place of `runs-on: windows-latest`, in the `test` and `installer` jobs of both
   `.github/workflows/vesper-installer.yml` and `.github/workflows/junction-installer.yml`.

### What the host needs

The hosted `windows-latest` image bundles a lot that a developer machine may not.

| Needed for | Check | If missing |
|---|---|---|
| Build and test | `dotnet --version` (10.0.x) | Install the .NET 10 SDK |
| Every `run:` step | `pwsh` on the **machine** `Path` | See below — this one is a trap |
| Native AOT publish | VS Build Tools with the **Desktop development with C++** workload | Visual Studio Installer |
| `Compile installer` | Inno Setup 6 | The workflow falls back to `choco install innosetup`, so Chocolatey is enough |
| `Create GitHub release` | `gh --version` | `choco install gh -y` |
| `mpv natives` workflow | `C:\msys64\usr\bin\bash.exe -lc 'gcc --version'` (or the path in the `MSYS2_ROOT` repository variable) | An MSYS2 with the MINGW64 packages `native/mpv/README.md` lists. The base tar.xz from msys2-installer's releases extracted to `C:\msys64` is enough; grant the runner account modify rights on it (`icacls C:\msys64 /grant "ghrunner:(OI)(CI)M" /T`), since pacman and the first login write there. |

Every check has to be made **for the service account**, not in your own shell. A tool you can
run is not necessarily a tool the runner can run, and the two ways that goes wrong are below.

### PowerShell 7 has to be the MSI, not the Store build

Install it machine-wide:

```powershell
choco install powershell-core -y
```

Then **restart the runner service**, because a service caches its environment at start and will
not see the new `Path` otherwise.

Two separate failures hide behind this, and fixing either alone still leaves a broken build.

**The Store build is invisible to the service.** A `pwsh` installed from the Microsoft Store
lives under `C:\Program Files\WindowsApps\...` and is reached through a *per-user* App
Execution Alias in `%LOCALAPPDATA%\Microsoft\WindowsApps`. It works perfectly in your shell and
does not exist as far as another account is concerned. The runner logs `pwsh: command not
found` and quietly falls back to Windows PowerShell 5.1. Worse, the workflows name `shell:
pwsh` explicitly in six steps — version stamping, installer compilation, release creation —
and an explicit shell does not fall back. It fails outright.

**Windows PowerShell 5.1 then blocks the script.** A `run:` step is written to a temporary
`.ps1` and dot-sourced, and 5.1's `LocalMachine` execution policy is unset, which means
`Restricted`. The step dies in half a second with *"running scripts is disabled on this
system"*, before any build work starts, so the log looks like the build itself exploded.

Note that PowerShell 5.1 and PowerShell 7 keep their execution policies in **different registry
keys**, so `Get-ExecutionPolicy -List` in `pwsh` reports 7's policy — `RemoteSigned` by default
on Windows — and tells you nothing about the shell the runner actually fell back to. Query
5.1's directly:

```powershell
powershell.exe -Command "Get-ExecutionPolicy -List"
```

Installing pwsh machine-wide fixes both, because the runner then prefers it and 7's default
policy already allows local scripts. If you want the fallback path to work too:

```powershell
powershell.exe -Command "Set-ExecutionPolicy RemoteSigned -Scope LocalMachine"
```

`RemoteSigned` is what Windows Server ships with: locally-authored scripts run, downloaded ones
still need a signature. It is not a meaningful loosening, and it turns a baffling failure into
a working fallback.

The GitHub CLI is the one that bites: hosted images ship `gh`, most desktops don't, and the
release step is the *last* thing in a tag build — so a missing `gh` wastes the whole run.
Note that GitHub **Desktop** is a different product and does not provide `gh`.

### Give the service its own account

`config.cmd` installs the service as `NT AUTHORITY\NETWORK SERVICE`, which is a poor fit for a
build agent — no real user profile, and not a member of the groups that grant read access to
Program Files. Running it as *your* account fixes that but hands every workflow read access to
your home folder, which is a bad trade on a machine with anything personal on it.

A dedicated standard account gets both. It needs no deny rules to stay out of your documents:
`C:\Users\<you>` already grants only you, SYSTEM and Administrators, so simply not being an
administrator is the isolation.

```powershell
$pw = Read-Host -AsSecureString "Password for ghrunner"
New-LocalUser -Name ghrunner -Password $pw -FullName "GitHub Actions runner" -PasswordNeverExpires -UserMayNotChangePassword
Add-LocalGroupMember -Group Users -Member ghrunner
```

`New-LocalUser` joins no group at all, so the second line matters: membership of `Users` is
what grants read and execute on Program Files, where the SDK, the MSVC toolchain, Inno Setup
and `gh` all live. Then give it the runner directory and nothing else:

```powershell
icacls C:\actions-runner /grant "ghrunner:(OI)(CI)F" /T
```

Point the service at it through **Services.msc** → the runner service → Properties → **Log
On** → This account → `.\ghrunner`. Use the GUI rather than `sc.exe config`: it grants the "Log
on as a service" right as a side effect, which `sc.exe` does not, and without that right the
service simply fails to start.

For a little more, add the account to **Deny log on locally** and **Deny log on through Remote
Desktop Services** in `secpol.msc`. It then exists only to run the service — nobody can sign in
as it, whatever happens to the password.

**Changing the account later means discarding the workspace.** `_work` is created by whichever
account first ran a job, and git refuses to operate on a repository owned by somebody else —
*"detected dubious ownership"*, which surfaces at whatever step first shells out to git, often
the release at the very end of a long build. Granting the new account Full Control does not
help: git checks **ownership**, not ACLs.

```powershell
Remove-Item -Recurse -Force C:\actions-runner\_work\<repo-name>
```

The next run re-clones it under the new owner. Delete only the repository directory and the
`_tool` and `_actions` caches beside it survive. Git's suggested `safe.directory` exception
would also silence it, but it suppresses a real mismatch rather than correcting it, and it has
to be set for the service account — awkward when that account is denied interactive logon.

Keeping the build's caches inside the runner directory avoids depending on whatever profile the
service account gets. The runner applies a `.env` file in its root to every job:

```powershell
Set-Content C:\actions-runner\.env "DOTNET_CLI_HOME=C:\actions-runner\home`nNUGET_PACKAGES=C:\actions-runner\home\.nuget\packages`nDOTNET_CLI_TELEMETRY_OPTOUT=1"
```

### `actions/setup-dotnet` has to go

Not an optimisation — leaving it in **fails the build**. `config.cmd` installs the service as
`NT AUTHORITY\NETWORK SERVICE` by default, which is not an administrator, and the action
installs the SDK into `C:\Program Files\dotnet`. It cannot write there, exits 1 about three
seconds in, and every step after it is skipped by the `success()` condition — so the log shows
a job that "did nothing" rather than one that failed at a build.

Provision the SDK on the host instead and let the workflow use it: `dotnet --list-sdks` should
report a 10.0 SDK, and `C:\Program Files\dotnet\` must be on the **machine** `Path`, not just
your user one, or the service will not see it.

The same applies in the container, where the apt-installed SDK lands on the path for every
user including `runner`. If `test-linux` reports `dotnet: command not found`, that package is
what is missing.

If you would rather keep the action, the alternatives are to point it somewhere writable with
`DOTNET_INSTALL_DIR`, or to run the service under an administrator account. Both are more
moving parts than provisioning the SDK once.

## Ubuntu runner in a Proxmox container

This covers Junction's `test-linux` job, which exists to prove the framework is display-free:
it runs full app scenarios with no X11/Wayland and no GPU. An unprivileged LXC container is
therefore exactly the right host — if the suite passes there, the claim holds.

### Create the container

On the Proxmox host:

```bash
pveam update && pveam available --section system | grep ubuntu-24
```

Download whichever 24.04 template that lists, then create the container. Adjust the storage
names (`local-lvm`, `vmbr0`) to your node:

```bash
pveam download local ubuntu-24.04-standard_24.04-2_amd64.tar.zst
```

```bash
pct create 210 local:vztmpl/ubuntu-24.04-standard_24.04-2_amd64.tar.zst --hostname hultui-build --cores 4 --memory 4096 --swap 1024 --rootfs local-lvm:32 --net0 name=eth0,bridge=vmbr0,ip=dhcp --unprivileged 1 --features nesting=1 --onboot 1 --password
```

`--onboot 1` brings it back after a host reboot. 32 GB of disk is comfortable: the NuGet cache
and Release build outputs for the whole solution are several gigabytes, and 16 GB gets tight.
Then:

```bash
pct start 210 && pct enter 210
```

### About `nesting=1`

It is **not required** for this. Proxmox's Ubuntu template runs systemd as PID 1 in an
unprivileged container on its own, so the runner's service works without it. It is worth
having anyway as cheap insurance: it permits nested namespaces, which is what you need the day
you want to run Docker or `buildx` inside this container, and it removes some AppArmor
friction. Enable it or don't — nothing here depends on it.

To set it on a container that already exists:

```bash
pct set 210 --features nesting=1
```

Or in the web UI: select the container → Options → Features → tick **Nesting**. Either way the
container has to be restarted before it takes effect.

One trap: `--features` replaces the whole feature string rather than adding to it. If the
container already has others, name them all in the same command or you will silently drop
them — `pct config 210 | grep features` first, then for example:

```bash
pct set 210 --features nesting=1,keyctl=1
```

### Provision it

Inside the container, as root:

```bash
apt update && apt install -y curl git ca-certificates libicu74 libssl3 zlib1g libfontconfig1 fonts-noto-cjk
```

`libfontconfig1` is the non-obvious one. The renderer references
`SkiaSharp.NativeAssets.Linux`, whose native `libSkiaSharp.so` links against fontconfig — the
Skia render tests fail to load it otherwise. The hosted `ubuntu-latest` image has it already,
which is why this only shows up on a lean container.

`fonts-noto-cjk` is the other one a desktop has and a container does not. `libfontconfig1`
brings DejaVu with it, which covers Latin, Hebrew and Arabic — enough for the bidi tests —
and not one ideograph. The tests for language-aware Han fallback (`[HanFontsFact]` in
`HultUI.Rendering.Skia.Tests`: a Japanese reader gets the Japanese face, a Taiwanese one the
Traditional) have nothing to choose between without it. They skip rather than fail there,
with this package named in the reason — so a run that reports three skipped Skia tests is a
runner missing this package, and the feature is going untested on Linux until it is
installed. It is about 90 MB.

The `cef natives` workflow (`native/cef/build.sh`) needs three more, besides `curl` above:

```bash
apt install -y bzip2 binutils binutils-aarch64-linux-gnu
```

`bzip2` unpacks CEF's tarball, and the two `binutils` give it a `strip` for each RID it packs:
the host's for linux-x64, `aarch64-linux-gnu-strip` for linux-arm64 (an x64 `strip` cannot
read an arm64 `libcef.so`). Its first run, on 2026-10-03, stopped at `curl: command not found`
on a runner provisioned before that line had `curl` in it.

Install the SDK from the Ubuntu archive:

```bash
apt install -y dotnet-sdk-10.0
```

That is all this needs. Nothing here pins an exact SDK — the workflows ask for
`dotnet-version: 10.0.x` and there is **no `global.json`** — so any 10.0 SDK builds the
solution, and the packaged one lands on the path with no profile scripts to maintain.

Do **not** add the packages.microsoft.com feed on top. Noble carries `dotnet-sdk-10.0`
natively, and having both sources configured has a long history of producing conflicting
`dotnet-*` packages. Pick one, and here the distro's is the easy one.

#### If you would rather use the install script

The packaged SDK tracks Ubuntu's own updates, currently the 10.0.1xx feature band, while the
Windows development machine is on 10.0.300 and a hosted runner's `10.0.x` resolves to the
newest. Same major version, same language version, same target framework, so this does not
matter for building this repository — but it does mean the container sits a feature band
behind, and if the build ever starts depending on something newer, it fails *here* while
passing everywhere else. That is a confusing way to find out.

If you want the container in lock-step with CI instead, skip the apt package and use the
script:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh && chmod +x /tmp/dotnet-install.sh && /tmp/dotnet-install.sh --channel 10.0 --install-dir /opt/dotnet
```

```bash
printf 'export DOTNET_ROOT=/opt/dotnet\nexport PATH=$PATH:/opt/dotnet\n' > /etc/profile.d/dotnet.sh
```

| Ask for | Gets you | Use when |
|---|---|---|
| `apt install dotnet-sdk-10.0` | whatever Ubuntu ships, security-patched for you | the default — nothing here needs more |
| `--channel 10.0` | newest 10.0 patch | you want to match the hosted runners exactly |
| `--channel 10.0.3xx` | newest patch of one feature band | you want to track the development machine's band |
| `--version 10.0.300` | exactly that build | you add a `global.json` pin and must match it |

The script's versions stop floating the moment you pin one, so they become yours to remember
to bump; the apt package updates itself. That trade is why the packaged SDK is the default
here and the script is the exception.

### Register the runner

Two things differ from the Windows host, and both surprise people:

- **`config.sh` will refuse to run as root** ("Must not run with sudo"), and `pct enter` drops
  you in as root. So the runner needs its own unprivileged account.
- **`config.sh` never offers to install the service.** The Windows `config.cmd` asks; the
  Linux one does not, by design — `svc.sh` is a separate step, always. If you were waiting for
  a prompt, it is not coming.

Also note that `sudo` may not be installed in the Proxmox template, and you do not need it:
inside `pct enter` you are already root. Commands below are written accordingly.

#### Create the account

```bash
adduser --disabled-password --gecos "" runner
```

#### Configure as that user

Get the download URL, version and registration token from Settings → Actions → Runners → New
self-hosted runner → Linux x64 — the token is short-lived, so take it fresh. Then:

```bash
su - runner
```

```bash
mkdir -p ~/actions-runner && cd ~/actions-runner && curl -o runner.tar.gz -L <the URL GitHub shows> && tar xzf runner.tar.gz
```

```bash
./config.sh --url https://github.com/fredrikhu/hului-revamped --token <TOKEN> --name hultui-build --labels self-hosted,linux,x64 --unattended
```

`--unattended` accepts the defaults instead of prompting. The labels are what
`runs-on: [self-hosted, linux, x64]` matches; the defaults would give you the same three, so
this is just being explicit. Then leave the account:

```bash
exit
```

#### Install the service as root

`svc.sh` writes a systemd unit, so it runs as root and takes the account the service should
run *under* as its argument:

```bash
cd /home/runner/actions-runner && ./svc.sh install runner && ./svc.sh start
```

Check it, and confirm it is enabled so a container restart brings it back:

```bash
./svc.sh status
```

The unit is named `actions.runner.<owner>-<repo>.<runner-name>.service`, so ordinary systemd
tooling works on it too — useful when `svc.sh status` is not saying enough:

```bash
journalctl -u 'actions.runner.*' -f
```

The runner should appear as **Idle** on the repository's Runners page within a few seconds. If
it does not, the token had probably expired before `config.sh` ran; generate a new one and
re-run the configure step.

### Point the job at it

In `.github/workflows/junction-installer.yml`:

```yaml
  test-linux:
    runs-on: [self-hosted, linux, x64]
```

Leaving it on `ubuntu-latest` is also defensible: Linux is the 1× tier and, with tags-only
triggers, that job runs a couple of times a month.

### First-run expectations

The package list above is a starting point, not a proof. The hosted Ubuntu image is fat and
carries plenty this container will not, so expect the first run to name something missing —
the AT-SPI accessibility tests in particular may want `dbus` present. Read the failure and add
the package; the second run is usually clean.

## Caveats for both

- **The machine must be awake and online.** A queued job waits rather than failing, which is
  easy to mistake for a broken workflow.
- **The workspace is not a fresh VM.** `actions/checkout` cleans the repo, but caches and
  installed tools persist. Mostly a speed win, occasionally the reason something passes here
  and fails on a hosted runner.
- **Security.** The usual warning about self-hosted runners concerns *public* repositories,
  where a pull request from a fork would execute arbitrary code on your machine. This
  repository is private, so that exposure does not apply. Reconsider before making it public.
- **One job at a time** per runner by default. The workflows are sequenced (`installer` needs
  `test`), so this only matters if two tags are pushed at once.
