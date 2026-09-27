# Zero-copy decode on Windows, through CUDA: what to find out, in this order

Hand this to a Claude Code session on the Windows machine (RTX 4080). Read the **Traps** section
of `docs/design/HDR.md` first, then the **Results** in `docs/design/HDR-LINUX-ZEROCOPY.md` —
this brief is that result, carried across.

## Why this exists

`HDR.md` concluded that Windows cannot have zero-copy decode: `dxva2gldx` is 8-bit, the ten-bit
bridge needs ANGLE, and ANGLE will not import Vulkan memory. **Both of those are still true, and
the conclusion was still wrong, because it missed a third route: CUDA.**

On Linux, `hwdec=nvdec` with mpv's `cuda-interop` was measured working end to end: frames reach
the VO as `cuda[p010]`, ten bits survive (879 of 877 ramp levels), the range survives (4.926 =
1000/203 with the peak forced to 1000), and 4K CPU drops from ~400 to ~155 ms/s. The mechanism
does not involve our shared image at all — mpv allocates its **own** plane textures, registers
them with `cuGraphicsGLRegisterImage`, copies the decoded surface into them GPU to GPU, and
renders from them into whatever FBO we lend it.

Nothing in that is Linux-specific, which was checked in mpv v0.41.0's source:

- `cuda-interop` requires `cuda-hwaccel` and `gl`, with **no platform condition**
  (`meson.build`, `cuda_interop = get_option('cuda-interop').require(...)`).
- `gl` is satisfied on our Windows build by `plain-gl`, even with `gl-win32` disabled — that is
  what the render API already runs on.
- `hwdec_cuda_gl.c` is plain GL plus `cuGLGetDevices`: no EGL, no GLX, no WGL.
- CUDA is loaded at run time through ffnvcodec's dynamic loader (`cuda_load_functions`), not
  linked, so `libmpv-2.dll` gains **no** dependency on `nvcuda.dll` — which is what keeps it
  loadable on AMD and Intel machines.
- It is not a licence question. None of mpv's Windows interops is GPL-gated; only the
  `direct3d` *video output* is, and nothing here uses it. On the FFmpeg side, `nvdec` needs only
  the MIT-licensed nv-codec-headers; the nonfree CUDA options (`cuda-nvcc`, `libnpp`) are for
  CUDA filters and are not needed.

The default is already `hwdec=auto` (commit `e1924ca`). In mpv's autoprobe order the zero-copy
entries ahead of `nvdec` are `d3d11va` and `vulkan`, whose interops the Windows build disables,
and `dxva2`, which `auto` skips as not whitelisted. So once the library has CUDA, **NVIDIA should
land on `nvdec` with no code change**, and AMD/Intel on `d3d11va-copy` exactly as today.

**Do the steps in order. Step 1 may end it, and it needs no build.**

## 1. Does CUDA-GL interop work here at all? (no rebuild)

This is exactly how Linux was answered: with a libmpv that already has the interop, used to find
out and never shipped. A community Windows build does — shinchiro's `mpv-winbuild-cmake`, linked
from mpv.io's installation page; take the `mpv-dev-x86_64-*` archive, which holds
`libmpv-2.dll`. **It is GPL. Use it to measure, do not commit it or put it anywhere the natives
package could pick it up.**

```powershell
$film = '<2001 on the media share: 2001.A.Space.Odyssey.1968.2160p.MAX.WEB-DL.DDP5.1.DV.HDR.H.265-RegEdits.mkv>'
$env:HULTUI_MPV_PATH = 'C:\path\to\mpv-dev\libmpv-2.dll'
$env:HULTUI_RENDERER = 'vulkan'; $env:HULTUI_STARTUP = '1'; $env:HULTUI_A11Y = '0'
$env:HULTUI_MPV_LOG = 'v'
$exe = 'samples\HultUI.Samples.VideoPlayer\bin\Release\net10.0\HultUI.Samples.VideoPlayer.exe'
$p = Start-Process -PassThru $exe -ArgumentList "`"$film`"",'--start=1200' -RedirectStandardError cuda.log
Start-Sleep 15; Stop-Process $p
Select-String -Path cuda.log -Pattern 'cuda-interop|hwdec|VO: \[libmpv\]|interop ready'
```

(Build Release first: `dotnet build -c Release samples/HultUI.Samples.VideoPlayer`.)

Establish, and write down:

- That the DLL's `List of enabled features` includes `cuda-interop`. If it does not, the test
  proves nothing — find a build that does.
- That the CUDA driver loads (`Loading hwdec driver 'cuda'` with no `Loading failed.` after it)
  in **our hidden WGL context beside the Vulkan device**.
- **The VO line, not `hwdec-current`.** `VO: [libmpv] 3840x… cuda[p010]` is zero-copy.
  `p010` is a copy. Linux found that `hwdec-current` reports `nvdec` even when frames are being
  copied (with no GL interop loaded), so the trace can rule a copy *in* and never *out*.
- Then the same on the **GL renderer** (`Remove-Item Env:HULTUI_RENDERER`). That is Skia's own
  WGL context rather than the hidden one, and it is the default, so it matters more.

If either renderer refuses, stop and report the log lines — the build change below would be
wasted.

## 2. The build change, only if 1 says yes

`native/mpv/build-libmpv.sh`, Windows branches only. The Linux branch wants the same thing but
different packages and its own run; leave it.

- **Headers: already in the script.** The Linux change (done after this brief was written)
  added `build_nvcodec_headers`, which installs nv-codec-headers from the tag pinned in
  `versions.env` into the build prefix. It is guarded `[ "$HOST" = linux ] || return 0`;
  **dropping that guard is the whole header change for Windows**. The headers' Makefile already
  handles MSYS2 (`cygpath`), and no pacman package or runner change is needed. Do not install
  `mingw-w64-x86_64-ffnvcodec-headers` as well: two copies would have pkg-config choosing
  between versions.
- **FFmpeg**, Windows `host_flags`: add `--enable-ffnvcodec --enable-nvdec`. Explicitly, rather
  than relying on autodetect — silent autodetection is how this capability went missing without
  anyone deciding it should. Keep `--disable-nonfree`; do **not** add `cuda-nvcc`, `libnpp` or
  `cuda-llvm`.
- **mpv**, Windows `host_opts`: add `-Dcuda-hwaccel=enabled -Dcuda-interop=enabled` (`enabled`,
  not `auto`, for the same reason). Leave `d3d11`, `direct3d`, `gl-win32`, `gl-dxinterop` and
  `egl-angle` disabled — enabling `gl-dxinterop` brings back the 8-bit `dxva2` bridge.
- **Versions:** pinned at n12.1.14.0, the version FFmpeg n8.1.2's configure asks for first
  (mpv needs >= 11.1.5.1). It sets the **minimum Windows driver at 531.61**. An older driver
  should fall back to a copy mode rather than fail; check that if you can.
- **Licence and dependencies**, both of which must be unchanged:
  - `LICENCE-REPORT.txt` still ends `RESULT: LGPL-2.1+`, and shows `HAVE_CUDA_HWACCEL 1` and
    `HAVE_CUDA_INTEROP 1` (already printed). Drop "Linux only for now" from its
    nv-codec-headers line, and from the README table.
  - `objdump -p libmpv-2.dll | grep 'DLL Name'` lists only OS DLLs — in particular **not**
    `nvcuda.dll` or `nvcuvid.dll`. If either appears, the DLL will not load on a machine without
    an NVIDIA driver, which is worse than no zero-copy.
  - `native/mpv/README.md`: nv-codec-headers (MIT) in the component table; the "Hardware decoding
    through D3D11VA/DXVA2 (copy-back)" bullet updated.
- **Tests:** `HULTUI_MPV_PATH=out/win-x64/libmpv-2.dll HULTUI_MPV_EXPECT_LGPL=1 dotnet test
  tests/HultUI.Media.Backends.Tests`.

## 3. Verifying it, with the built DLL

Everything with `HULTUI_MPV_PATH` pointing at the new DLL, no `HULTUI_MPV_HWDEC` unless stated.

**Launch the built exe, never `dotnet run`** — the process `dotnet run` returns is the CLI host,
not the app, so its CPU time measures nothing. The Linux brief had exactly that bug.

- **VO format** as in step 1, on both renderers: `cuda[p010]`.
- **CPU**, 2001 at 20:00, 12 s warm-up, 20 s window, two rounds:

  ```powershell
  foreach ($mode in 'auto','auto-copy','no') {
    $env:HULTUI_MPV_HWDEC = $mode
    $p = Start-Process -PassThru $exe -ArgumentList "`"$film`"",'--start=1200' -RedirectStandardError "cpu-$mode.log"
    Start-Sleep 12; $p.Refresh(); $t0 = $p.TotalProcessorTime
    Start-Sleep 20; $p.Refresh(); $t1 = $p.TotalProcessorTime
    '{0}: {1:N0} ms CPU/s' -f $mode, (($t1 - $t0).TotalMilliseconds / 20)
    Stop-Process $p; Start-Sleep 2
  }
  ```

  The earlier Windows figures were 468 ms/s for `d3d11va-copy` and 921 for software. Linux went
  ~400 → ~155. Do it on the Vulkan renderer and on GL.
- **Ten bits**, with the ramps in `spikes/HdrInterop/ramps/` — the same files the Linux figures
  came from, HEVC Main10 so NVDEC will actually take them (a `.y4m` never reaches the hardware
  decoder, and anything under 144 rows is refused and silently decoded in software):

  ```powershell
  $env:HULTUI_RENDERER = 'vulkan'; $env:HULTUI_VIDEO_HDR_PROBE = '30'
  ```

  `ramp10-hevc.mkv` should read **~879 levels, ~22 reversals**; `ramp8-hevc.mkv` is the control
  and should read **~455, ~758**. If the 10-bit ramp reads like the control, something quantised.
  And read the `hwdec` and VO lines for *this* file — the ramp is where Linux caught a silent
  software fallback.
- **Range**, 2001 at 20:00, `HULTUI_VIDEO_HDR_PROBE=120`, six samples, prove none is black: at
  the display's own peak (455 nits here) the shared image max should be **~2.240**; with
  `HULTUI_VULKAN_PEAK_NITS=1000`, **4.926**. Same as `auto-copy` on both counts.
- **Handles** over 45 s of playback, with `GetProcessHandleCount` — **not**
  `Process.HandleCount`, which sat flat while 200 handles leaked:

  ```powershell
  Add-Type -Namespace W -Name K -MemberDefinition '[DllImport("kernel32.dll")] public static extern bool GetProcessHandleCount(IntPtr h, out uint n);'
  $n = 0; [W.K]::GetProcessHandleCount($p.Handle, [ref]$n) | Out-Null; $n
  ```

  Sample every 10 s. Flat is the answer; compare with `auto-copy`.
- **The fallback**: `HULTUI_VULKAN_INTEROP=0` (mpv's software renderer) must still play, at
  about `auto-copy`'s CPU. It will likely still *report* `nvdec`; the VO line will say `p010`.
- **Not NVIDIA:** if any AMD or Intel Windows machine is at hand, the new DLL must load and
  `auto` must land on `d3d11va-copy`. If none is, say so — the `objdump` check above is then the
  only evidence, and it is a reason, not a run. A hybrid laptop whose GL context is on the iGPU
  is the other untested case: `cuGLGetDevices` should find no CUDA device and mpv should fall
  back.

## 4. What to send back

Append results to this file under a **Results** heading and commit, as the other briefs do. Say
which driver version, which ffnvcodec version, and what the VO line said on each renderer.

**Step 1 is the most valuable part even if nothing else is attempted.** It decides whether
the build change is worth making, and it needs no build.

## Results

Run 2026-09-27 on the Windows machine (RTX 4080, driver 32.0.16.1062, NVIDIA HDR display
peaking at 455 nits). **Step 1 passes on every count, on both renderers.**

### 1a. CUDA-GL interop in our own context, with no mpv at all

The brief's step 1 needed a community libmpv. It turned out there was a cheaper question first,
because of something the Linux run established: **mpv registers its own plane textures, not the
image we lend it.** So the thing to prove is plain CUDA-GL interop in a plain WGL context, which
needs no mpv and no download. `spikes/HdrInterop --cudagl` does exactly mpv's sequence —
`cuGLGetDevices`, then register, map, get the mapped array, unmap — on the plane formats a P010
frame uses:

```
  GL renderer : NVIDIA GeForce RTX 4080/PCIe/SSE2
  CUDA device : 0 (for this GL context)

  GL_R8    (NV12 luma)    registered and mapped
  GL_RG8   (NV12 chroma)  registered and mapped
  GL_R16   (P010 luma)    registered and mapped
  GL_RG16  (P010 chroma)  registered and mapped
```

The 16-bit pair is the one that matters: an 8-bit-only result would have been the `dxva2` story
again. **This is the check to repeat first on any new machine** — it is seconds, it needs no
libmpv, and a failure here means nothing else can work.

### 1b. End to end, with shinchiro's build (GPL, used to measure, not shipped)

`mpv-dev-x86_64-20260927-git-a1bf4b6559`, which lists `cuda-hwaccel cuda-interop`. On **both**
renderers, after the D3D and EGL drivers fail as expected in a plain WGL context:

```
[mpv libmpv_render] Loading hwdec driver 'cuda'
[mpv vd] Using hardware decoding (nvdec).
[mpv cplayer] VO: [libmpv] 3840x1746 cuda[p010]
```

`cuda[p010]` on the Vulkan renderer (our hidden context) **and** on GL (Skia's own context).

**CPU**, 2001 at 20:00, release exe, 12 s settle, 20 s window:

| renderer | `auto` (nvdec) | `auto-copy` |
|---|---|---|
| Vulkan | **198 ms/s** | 420 ms/s |
| GL | **53 ms/s** | 215 ms/s |

Less than half on Vulkan, a quarter on GL, and in line with Linux's ~400 → ~155.

**Ten bits survive.** `ramp10-hevc.mkv` reads **879 levels, 22 reversals**; `ramp8-hevc.mkv`
reads **455, 758**. Those are the Linux numbers exactly, which is a good sign for the ramps as a
shared instrument.

### A side finding, not about CUDA

The Vulkan renderer costs about **145 ms/s more than GL at 4K** on the same decode path (198 vs
53, and 420 vs 215). That is far more than the 0.15–0.25 ms a frame the benchmark showed for
small UI operations, and it is not the acquire fence, which is 6 µs. At 24 fps it is roughly 6 ms
a frame, which points at the F16 swapchain's bandwidth and the offscreen pass rather than at
anything per-submit. Worth its own measurement before Vulkan is ever made the default; recorded
here because this is where it showed up.

### 2 and 3. The build change, made and verified

`build-libmpv.sh`: the `[ "$HOST" = linux ] || return 0` guard dropped from
`build_nvcodec_headers`, `--enable-ffnvcodec --enable-nvdec` on the Windows FFmpeg flags, and
`-Dcuda-hwaccel=enabled -Dcuda-interop=enabled` on the Windows mpv options. The DirectX interops
stay disabled. No pacman package and no runner change were needed — the headers' Makefile
handled MSYS2 on its own, exactly as the brief said.

**Static checks, all as required:**

```
mpv features : cuda-hwaccel cuda-interop ffnvcodec gl
licence      : nv-codec-headers n12.1.14.0 (MIT)
               HAVE_CUDA_HWACCEL 1 / HAVE_CUDA_INTEROP 1
               --enable-dxva2 --enable-ffnvcodec --enable-nvdec
               RESULT: LGPL-2.1+ throughout
objdump -p   : ADVAPI32, AVRT, CRYPT32, DWrite, GDI32, … — OS DLLs only,
               no nvcuda.dll and no nvcuvid.dll
```

**Runtime, against our own DLL:**

| | Vulkan | GL |
|---|---|---|
| VO format | `cuda[p010]` | `cuda[p010]` |
| CPU, `auto` | **156 ms/s** | **58 ms/s** |
| CPU, `auto-copy` | 323 ms/s | 382 ms/s |

The zero-copy figures agree closely with the community build's 198 and 53; the copy figures vary
more between runs, which is the film streaming over SMB, so read the zero-copy column and the
direction rather than the exact copy numbers.

- **Ten bits:** `ramp10-hevc.mkv` **879 levels, 22 reversals**; `ramp8-hevc.mkv` **455, 758**.
  The Linux numbers exactly, on both builds.
- **HDR range unchanged:** shared image max **2.240** at the display's own 455-nit peak, the same
  as the copy path, so zero-copy costs nothing in range.
- **Fallback:** `HULTUI_VULKAN_INTEROP=0` still plays, at 1205 ms/s — mpv's software renderer at
  4K, expensive and working.
- **Tests:** backend tests 8/8 with `HULTUI_MPV_EXPECT_LGPL=1`; full solution suite green apart
  from the known `Junction.Tests` load flake.

**Not verified, and it is a reason rather than a run:** that the DLL still loads on a machine
with no NVIDIA driver. There is none here. The `objdump` import list above is the evidence — CUDA
is reached through ffnvcodec's runtime loader and never appears as an import — but nobody has
watched it start on AMD or Intel hardware. Same for a hybrid laptop where `cuGLGetDevices` should
find no device and mpv should fall back.

**One trap for whoever runs this script next on Windows:** it requires a **MINGW64** shell, not
the MSYS one. `C:\msys64\usr\bin\bash.exe` without `MSYSTEM=MINGW64` fails the `uname -s` check
on line 24 and exits immediately — and launched through `Start-Process` with redirected output
that looks exactly like a long quiet build. Twenty minutes went into a build that had never
started; what gave it away was file timestamps in the work tree, not any error.
