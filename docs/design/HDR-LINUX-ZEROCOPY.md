# Zero-copy decode on Linux: what to find out, in this order

Hand this to a Claude Code session on the Linux machine. Read the **Traps** section of
`docs/design/HDR.md` first — most of what follows is shaped by the mistakes recorded there.

Today every video frame goes **GPU → CPU → GPU**: `hwdec` is `auto-copy`, the decoder puts a
surface in GPU memory, mpv reads it back to system memory, and the GL renderer uploads it again.
About 24 MB a frame at 4K. Removing that round trip is the last item in `HDR.md`.

**Windows cannot have it, and that is settled** — its one zero-copy bridge that works with a
desktop GL context (`hwdec_dxva2gldx`) shares a `D3DFMT_X8R8G8B8` surface and returns
`IMGFMT_RGB0`, eight-bit RGB, which throws away everything phases 2 to 5 carry. The ten-bit
bridge needs an ANGLE EGL context, and ANGLE will not import Vulkan memory, which is what the
interop rests on. Details in the zero-copy section of `HDR.md`.

**Linux might.** `hwdec_cuda` and `hwdec_vaapi` map the hardware format itself (`IMGFMT_CUDA`,
`IMGFMT_VAAPI`) and keep its subformat, so they can carry ten bits. That is the whole reason this
document exists, and it is why the work is worth scoping there rather than here.

**Do the steps in order. Step 1 may end the project, and it needs no build.**

## 1. What does the copy actually cost? (no rebuild — do this first)

Nothing below is worth doing if the answer is small, and the answer is not known. 468 ms of CPU
per second was measured on Windows for 4K HEVC HDR, but that is decode *and* copy together;
nobody has separated them.

```bash
# Same film, same point, release build. Software decode is the control.
for mode in auto-copy no; do
  HULTUI_MPV_HWDEC=$mode HULTUI_RENDERER=vulkan HULTUI_A11Y=0 \
    dotnet run -c Release --project samples/HultUI.Samples.VideoPlayer -- /path/to/film.mkv --start=1200 &
  pid=$!; sleep 12
  t0=$(awk '{print $14+$15}' /proc/$pid/stat); sleep 20
  t1=$(awk '{print $14+$15}' /proc/$pid/stat)
  echo "$mode: $(echo "($t1-$t0)*10/20" | bc) ms of CPU per second"
  kill $pid; sleep 2
done
```

**Then do the same at 1080p and compare the scaling.** That is what separates the copy from the
decode: a readback is pure bandwidth and scales with pixels, and if `auto-copy` costs roughly four
times as much at 4K as at 1080p then the copy is the larger part; if it barely moves, the decode
is, and zero-copy is not worth a native rebuild.

**Report the two numbers and the ratio before going further.** If the copy is a small share, stop
and say so — that is a complete and useful answer.

## 2. Can the interop work at all? (still no rebuild)

The question that decides the whole approach, and it can be asked before building anything:
**mpv's zero-copy bridge has to import the decoder's surface into *our hidden GL context*** — the
one `SdlVulkanHost.CreateHiddenGLContext` makes beside the Vulkan device, not a context mpv owns.

```bash
HULTUI_RENDERER=vulkan HULTUI_STARTUP=1 \
  dotnet run --project samples/HultUI.Samples.VideoPlayer -- /path/to/film.mkv --start=1200 2>&1 |
  grep -E "interop ready|hwdec"
```

Establish, and write down:

- **Which GL that context actually is.** SDL gives EGL on Wayland and GLX on X11, and it matters:
  **VAAPI interop needs EGL**, so it is Wayland-only. CUDA interop works with either.
- **Which route this hardware even has.** NVIDIA proprietary means CUDA/NVDEC, not VAAPI —
  `vainfo` on NVIDIA reports through `nvidia-vaapi-driver` if installed at all, and that is itself
  NVDEC underneath. On an AMD or Intel machine it would be the other way round.
- **Whether the pieces are present**: `nvidia-smi`, `libcuda.so.1`, `libnvcuvid.so.1`, and
  `pkg-config --modversion ffnvcodec` (mpv requires **>= 11.1.5.1**).

**The risk worth naming now:** CUDA-GL interop registers a GL texture with a CUDA context
(`cuGraphicsGLRegisterImage`), and our GL context is one made only to share memory with a Vulkan
device on the same GPU. Whether CUDA is happy registering a texture whose storage came from
`glTextureStorageMem2DEXT` — Vulkan-owned memory imported into GL — is **not known and is the
crux.** If it refuses, zero-copy needs mpv to render into a texture GL owns outright, which means
a copy from that into the shared image, which is the copy we were removing.

**That is worth a small experiment before the build:** extend `spikes/HdrInterop` to register the
imported texture with CUDA (`cuGraphicsGLRegisterImage` on the FBO's texture) and report whether
it succeeds. If it fails there, it fails everywhere, and it costs an afternoon rather than a
rebuild of FFmpeg and mpv.

## 3. The build change, only if 1 and 2 both say yes

`native/mpv/build-libmpv.sh`. Two places, and the comments in both say why they are off — read
them, because they are not arbitrary.

**FFmpeg** currently says *"No VAAPI/VDPAU in this first cut: they need a display interop the
framework does not provide yet"*:

```sh
host_flags=(--disable-vaapi --disable-vdpau --disable-libdrm)     # the Linux branch
```

For CUDA it needs `nv-codec-headers` (ffnvcodec) installed and `--enable-cuda --enable-nvdec`;
for VAAPI, drop `--disable-vaapi` and add libva. **Note the licence question**: keep
`--disable-nonfree`, and check `native/mpv/licence-report.sh` still produces a clean report — it
already treats several components specially, and the whole natives workflow exists to keep the
distribution LGPL.

**mpv** is where the interop is gated. The option names, read out of `meson.options`:

| option | what it gates |
|---|---|
| `cuda-hwaccel` | CUDA decoding — requires `ffnvcodec >= 11.1.5.1` |
| `cuda-interop` | **CUDA with graphics interop** — the zero-copy part |
| `vaapi` | VAAPI decoding |
| `egl` | currently `-Degl=disabled`; VAAPI interop needs it |

The Linux branch currently disables `egl`, `vaapi` and `vdpau` along with the windowing backends,
under the comment *"no windowing, no display interop"*. **That is the same conflation that hid
this on Windows:** stripping *display* interop also stripped *decode* interop, which is a
different thing.

## 4. Verifying it, which is harder than building it

A zero-copy path that works but drops to eight bits is the Windows failure mode, and it would look
like a success in every measurement except one.

- **`hwdec-current`, never the CPU delta.** `HULTUI_STARTUP=1` prints
  `mpv hwdec: asked …, using …`. It must say `cuda` or `vaapi`, **not** a `-copy` variant. On
  Windows `auto` silently resolved to `d3d11va-copy` and an 11% CPU difference between two runs of
  the *same* decoder nearly went in as a zero-copy result.
- **Then prove ten bits survive**, with the ramp rather than a film:
  generate a 10-bit y4m ramp (the generator is in `HDR.md`'s traps notes and in
  `spikes/HdrInterop/results/windows.txt`), play it, and read
  `HULTUI_VIDEO_HDR_PROBE=90`'s `middle row N levels`. **~880 levels means ten bits arrived; ~256
  means something quantised to eight**, which is exactly what the Windows bridge does.
- **Then prove the range survives**: on a real HDR film the shared image max should still exceed
  1.0 and the swapchain exceed its SDR white, as recorded in `HDR-LINUX-VERIFY.md` section 5a.
- **Then the descriptors**: `spikes/HdrInterop --churn` and the fd count during playback. A new
  import path is a new chance to leak one.
- **Several samples, and prove the content was there.** A black frame reports a maximum and a
  level count that both read exactly like a broken pipeline.

## 5. What to send back

Append results to this file under a **Results** heading and commit, as the other briefs do. Say
which display server, which driver, and which decoder `hwdec-current` reported.

**Step 1's two numbers are the most valuable thing here even if nothing else is attempted** — they
decide whether this phase is worth anyone's time, on either platform, and they need no build.

## Results (2026-09-27)

**Machine:** RTX 4080, NVIDIA proprietary 615.71.09, Fedora 44, KDE Plasma **Wayland**. Hidden GL
context is EGL. Release build of `HultUI.Samples.VideoPlayer`, `HULTUI_RENDERER=vulkan`.
`hwdec-current` under zero-copy: **`nvdec`**, with frames arriving at the VO as `cuda[p010]`.

**Zero-copy already works on this machine, with no rebuild.** The brief assumed Linux runs the
trimmed natives build. It does not: nothing bundles a `libmpv.so.2` for linux-x64, so the resolver
falls through to the system library, which here is RPM Fusion's `mpv-libs` 0.41.0, built with
`-Dcuda-hwaccel -Dcuda-interop -Dvaapi -Degl` and `ffnvcodec`. Every Linux figure recorded in the
other briefs was taken on that library too. So steps 2 and 3 were not needed to answer the
question. Step 3 is still needed for anything *distributed*, because the LGPL natives build has
the interop stripped (and `cuda-interop` is not a GPL feature, so it should survive that).

### 1. What the copy costs

utime+stime of the app process over 20 s, after 12 s of warm-up, `--start=1200`. Two rounds at 4K:

| `hwdec` | resolves to | 4K (2001, 3840x1746 HEVC 10-bit PQ) | 1080p (Queen's Gambit, HEVC 10-bit PQ) |
|---|---|---|---|
| `auto-copy` (today's default) | **`vulkan-copy`** | 393, 416 ms/s | 227 ms/s |
| `nvdec` | `nvdec` | **151, 161 ms/s** | **130 ms/s** |
| `auto` | `nvdec` | 156, 168 ms/s | — |
| `no` | software | 957, 962 ms/s | 639 ms/s |

- **The copy is about 250 ms of CPU per second at 4K24**, 60% of what `auto-copy` spends, and
  about 97 ms/s at 1080p. That is 2.6x for 3.2x the pixels, so it scales as a readback should.
  Not small. Measured directly as `auto-copy` minus `nvdec` rather than inferred from scaling,
  because with the interop present the direct A/B was available.
- `auto-copy` resolves to **`vulkan-copy`** here, not `nvdec-copy`. It is still a copy.
- **`auto` already picks zero-copy** here, which matters for the default (see the end).
- The step 1 script as written measures the wrong process: `$!` after `dotnet run` is the CLI
  host, not the app it launches. These runs launched the built binary directly.

### 2. The crux, which turned out not to be one

CUDA never has to register our Vulkan-imported texture. mpv's `hwdec_cuda` GL interop allocates
its **own** plane textures, registers *those* with `cuGraphicsGLRegisterImage`, and copies the
decoded surface into them GPU to GPU. Its shaders then render from them into whatever FBO the
host lends, which is our imported image, exactly as for software frames. The log shows the CUDA
interop loading in our hidden EGL context, beside the Vulkan device, with no complaint.

VAAPI also loaded (`Using EGL dmabuf interop via GL_EXT_EGL_image_storage`) but found no VAAPI
driver, as expected on NVIDIA without `nvidia-vaapi-driver`. **VAAPI zero-copy is untested**,
and so is AMD/Intel in general.

### 4. Verification

- **`hwdec-current` caught a false pass on the first try.** The first ramp was 1024x64, and
  NVDEC's minimum HEVC height is 144: `asked nvdec, using no`. It read 883 levels, which would
  have gone in as "ten bits survive zero-copy" while testing software decode. Also note a
  **`.y4m` ramp can never exercise this path**: raw video is not hardware-decodable. The ramp
  has to be HEVC Main10.
- **Ten bits survive.** A lossless x265 Main10 ramp, 1024x256, limited range 64..940, verified
  at 877 source levels, middle row read back through GL (`HULTUI_VIDEO_HDR_PROBE=30`):

  | `hwdec` (as reported) | levels | reversals |
  |---|---|---|
  | `nvdec` | **879** | 22 |
  | `vulkan-copy` | 879 | 22 |
  | `no` | 879 | 22 |
  | control: same ramp at **8 bits**, `nvdec` | 455 | 758 |

  ```bash
  ffmpeg -f lavfi -i "color=c=black:s=1024x256:r=24:d=60,format=yuv420p10le" \
    -vf "geq=lum='64+X*876/1023':cb=512:cr=512" -c:v libx265 -x265-params lossless=1 \
    -pix_fmt yuv420p10le -color_range tv -colorspace bt709 -color_primaries bt709 -color_trc bt709 ramp10.mkv
  ```
- **Range survives, with a trap.** At this display's own peak the shared image max is exactly
  1.000 on both paths. That is not a failure: SDL reports a headroom of ~1.0 here, so the peak
  is 203 nits and nothing is meant to exceed 1. With `HULTUI_VULKAN_PEAK_NITS=1000`, 2001 at
  20:00, six samples each, none black (hundreds to thousands of levels per row):

  | | shared image max | swapchain |
  |---|---|---|
  | `nvdec` | **4.926** (= 1000/203), up to 6.0M samples above 1 | max 12.445, HDR reached the swapchain |
  | `vulkan-copy` | 4.926 | max 12.445 |

- **Descriptors:** fd count sampled over 45 s of 4K playback: `nvdec` 199 flat, `auto-copy`
  250 +/-1. The Vulkan interop image is created by the renderer independently of `hwdec`, so
  `--churn` was not re-run; the new path adds no fds of its own.
- **Not done:** resize during zero-copy playback, a hybrid GPU, Mesa, X11/GLX, and the default
  GL renderer with `hwdec=nvdec` (only the Vulkan renderer was run).

### What this leaves

1. **The default.** Changing `auto-copy` to `auto` gets this saving everywhere the interop is
   present, and on Windows's trimmed build `auto` already resolves to the same `d3d11va-copy`
   as today. It needs a run on the GL renderer first, where mpv's CUDA interop would live in
   Skia's GL context rather than a hidden one.
2. **The distributed Linux build** (`native/mpv/build-libmpv.sh`) needs step 3 to get any of this.
   The system-library result says the interop works, so step 3 is no longer a gamble.

### Default changed to `auto` (2026-09-27)

Run with no override on each path, 2001 at 20:00: Vulkan renderer `nvdec` (shared image 925
levels a row, real content); GL renderer `nvdec`, 265 → 117 ms/s against `auto-copy`, picture
checked by screenshot; Vulkan with `HULTUI_VULKAN_INTEROP=0` (mpv's software renderer) plays,
at the same CPU as `auto-copy` (1658 against 1651 ms/s).

**That last one is a trap for step 4's advice.** With no GL interop, `hwdec-current` *still
reports `nvdec`* while the frames reach the filter chain as plain `p010`, so they were copied.
`hwdec-current` rules a copy *in* when it names a `-copy` mode, but cannot rule one *out*.
The proof is the VO format in `HULTUI_MPV_LOG=v`: `VO: [libmpv] … cuda[p010]` is zero-copy,
`p010` is not.
