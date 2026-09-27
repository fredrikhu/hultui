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

- **Headers:** `pacman -S mingw-w64-x86_64-ffnvcodec-headers` in the MINGW64 shell. Add it to
  the pacman line in `native/mpv/README.md`, and install it on the self-hosted Windows runner too
  (`docs/SELF-HOSTED-RUNNERS.md`), or `mpv-natives.yml` will fail there.
- **FFmpeg**, Windows `host_flags`: add `--enable-ffnvcodec --enable-nvdec`. Explicitly, rather
  than relying on autodetect — silent autodetection is how this capability went missing without
  anyone deciding it should. Keep `--disable-nonfree`; do **not** add `cuda-nvcc`, `libnpp` or
  `cuda-llvm`.
- **mpv**, Windows `host_opts`: add `-Dcuda-hwaccel=enabled -Dcuda-interop=enabled` (`enabled`,
  not `auto`, for the same reason). Leave `d3d11`, `direct3d`, `gl-win32`, `gl-dxinterop` and
  `egl-angle` disabled — enabling `gl-dxinterop` brings back the 8-bit `dxva2` bridge.
- **Versions:** mpv needs ffnvcodec **>= 11.1.5.1**; read FFmpeg n8.1.2's configure output for
  its own minimum. The header version sets the **minimum NVIDIA driver** the DLL can use CUDA
  with (listed in nv-codec-headers' README) — record it. An older driver should fall back to a
  copy mode rather than fail; check that if you can.
- **Licence and dependencies**, both of which must be unchanged:
  - `LICENCE-REPORT.txt` still ends `RESULT: LGPL-2.1+`. Add `HAVE_CUDA_HWACCEL` and
    `HAVE_CUDA_INTEROP` to the flags `licence-report.sh` prints, so the report shows it.
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
