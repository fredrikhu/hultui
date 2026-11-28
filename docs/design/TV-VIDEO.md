# Video on a TV: the remote's media keys, 4K, HDR and the frame rate — design note

**Status (2026-11-28): media keys, the player's bar and the frame rate built; 4K and HDR10 shown
to work in an env-gated spike (§3, §4), two decisions open.** Plan 20 phase 5, the Android TV
build, after D-pad focus (`DPAD-FOCUS.md`). From the user's Shield TV (2019 Pro, Android 11, Tegra
X1+, a 4K HDR10 television).

## 1. What the Shield showed

- **Playback works.** A 4K HDR10 HEVC film (3840×1746, 10-bit) decodes on `OMX.Nvidia.h265.decode`
  into a surface (mpv's AImageReader zero-copy path), the picture whole.
- **At 1080p.** The app's surface is 1920×1080 RGBA_8888 with the default dataspace
  (`dumpsys SurfaceFlinger`): Android TV draws apps at the UI's 1080p and the display scales the
  layer to the panel's 3840×2160. The film is scaled down to 1080p in mpv and back up by the display.
- **In SDR.** mpv tone-maps the HDR10 picture into the 8-bit surface; the television stays in SDR.
- **At 59.94 Hz.** The display runs 3840×2160 at 59.94 Hz throughout; a 23.976 fps film judders (3:2).
  The display offers 23.976, 24, 25, 29.97, 30, 50, 59.94 and 60 Hz at 4K and at 1080p.
- **The remote's media keys do nothing.** Play/pause, fast-forward and rewind reach SDL as media
  scancodes, which `EventTranslator` maps to `KeyCode.Unknown`.
- **What the GPU offers:** `EGL_EXT_gl_colorspace_bt2020_pq`, `_bt2020_linear`, `_scrgb(_linear)`,
  `_display_p3(_linear)`, `EGL_EXT_pixel_format_float` and `EGL_EXT_surface_SMPTE2086_metadata`. The
  display's HDR capabilities: HDR10 (type 2), 500 nits.

## 2. The remote's media keys (built; the user approved the eight members and names)

`KeyCode.MediaPlayPause`, `MediaPlay`, `MediaPause`, `MediaStop`, `MediaNext`, `MediaPrevious`,
`MediaFastForward`, `MediaRewind`, appended to the enum, mapped from SDL's media scancodes in
`EventTranslator` and from WinForms' `Keys.Media*`; `KeyChord` writes them "Play/Pause", "Media
Play", "Media Pause" (not "Pause", the Pause key's), "Media Stop", "Next Track", "Previous Track",
"Fast Forward", "Rewind", and reads those back. `VideoPlayer.Shortcuts` binds play/pause, play,
pause, and fast-forward and rewind as the jump keys (a held key repeats). Stop, next and previous are
the app's: in Aperture stop leaves the film as Escape does, and next and previous step the queue, or
the chapters without one.

## 2b. The player's controls on a D-pad (built; the user's choice)

OK on the picture plays and pauses (as a click), Left and Right seek. On a D-pad Up or Down from the
picture shows the bar and puts the focus on its play button (where they changed the volume, and no
button could be reached); in the bar the arrows walk its controls by the window's rule (a slider keeps
Left and Right), and Back gives the focus back to the picture before it would leave the film. The bar
fading with the focus in it gives the focus back to the picture too. The bar's buttons show the focus
ring (with Tab on the desktop as well). On a D-pad the bar is two rows at any width, the scrubber over
the buttons: in one row the scrubber sat between them and took Left and Right, so the remote could not
get past it to the volume, the speed or the subtitles.

## 3. 4K: a surface the size of the panel (spike, 2026-11-28: works)

A spike behind environment variables answers the open questions; nothing in it is public.
`HULTUI_ANDROID_SURFACE=panel` (`HultUIActivity.DrawAtPanelSize`):
- **The panel gets every pixel.** The Shield's display is 3840×2160 under a 1920×1080 `wm size`
  override, and the compositor projects the UI's 1080p onto the panel. `SurfaceHolder.setFixedSize`
  at the panel's size (`Display.Mode`; `DisplayMetrics` report the UI's 1080p) gives a buffer the
  hardware composer scans out 1:1: source crop and display frame both 3840×2160, DEVICE composition.
- **The layout keeps its size.** SDL reports a 3840×2160 window at density 2, which would lay out at
  1920×1080 DIP; an internal `SDL3Window.PlatformSurfaceScale` of 2 multiplies the display scale (4 in
  all), so the layout stays 960×540 DIP, drawn at 4K.
- **Cost, measured with an env-gated frame log** (`HULTUI_SPIKE_FRAMES=1`, frame time and flush +
  present each 5 s):
  - A focus move on Home: 12–15 ms a frame at 1080p, 14–15 ms at 4K, mostly waiting in present.
  - A page's first frame (images arriving): up to 120 ms at 4K.
  - Bleach (1080p, scaled by mpv into a 4K target): 23.98 fps delivered, every frame 41.7 ms after the
    last (SurfaceFlinger's present times, none dropped), 8.7 ms a frame, app CPU 37 %.
  - *2001* (4K HDR10 HEVC, decoded by `OMX.Nvidia.h265.decode`): about 10 ms a frame. It stalls only
    to buffer: the stream outruns the Shield's 86 Mbps Wi-Fi at any surface size.
  - Memory on Home: EGL 16 MB at 1080p, 37 MB at 4K; app PSS 419 MB and 488 MB.
- **Not checked:** a mouse. SDL's Android mouse events arrive in view pixels, half the surface's; touch is
  normalised and lands right.

**Proposal:** the whole surface at the panel's size on a TV, always. The cost is within the noise of
a 1080p frame, and there is no resize mid-session.

## 4. HDR10 (spike, 2026-11-28: works; two decisions open)

`HULTUI_ANDROID_HDR=pq`:
- **No 10-bit surface on a Tegra X1.** Its EGL lists 56 configs; the deep ones are all RGBA16F (float),
  none RGB10_A2, so asking for 10/10/10/2 left HultUI on the software renderer. The surface is FP16
  (`SDL_GL_FLOATBUFFERS`) holding PQ-encoded values, made in BT.2020 PQ through
  `SDL_EGL_SetAttributeCallbacks` (`EGL_GL_COLORSPACE_KHR` = `EGL_GL_COLORSPACE_BT2020_PQ_EXT`).
- **Android switches the television.** SurfaceFlinger sees the layer's dataspace as BT2020_PQ and moves
  the display's colour mode from SRGB to BT2100_PQ; during a 23.976 Hz film HDMI runs Rec.2020 12-bit
  4:2:2. Back to SRGB when the app goes.
- **The UI in PQ.** Skia is told the surface is PQ / BT.2020 (`SKColorSpaceTransferFn.Pq`) and converts
  every colour into it. Skia's PQ puts linear 1.0 at 203 nits, BT.2408's reference white: sRGB white
  wrote 0.580 (201 nits), 50 % grey 43.5 nits (a raster probe).
- **The film.** The GL surface lends each video a 10-bit RGB10_A2 target of its own
  (`TryAcquireFrameTarget`), mpv renders into it with `target-prim=bt.2020` and `target-trc=pq` and no
  `target-peak`, so HDR10 passes through untouched and the television tone-maps it. Skia wraps the
  target tagged PQ and copies it unconverted. *2001* played four minutes at about 10 ms a frame. An SDR
  film (Bleach) goes to PQ with its white at 203 nits, as the UI's: 24 fps, 7.5 ms a frame.
- **Cost:** a focus move 15–16 ms; EGL 70 MB on Home (37 MB in 8-bit), 202 MB during 4K playback.
- **A trap, found and fixed.** The lent target's image was wrapped anew every frame and never disposed
  (the caller does not dispose a renderer-owned frame's image). The finalizer then freed Skia's GPU
  objects off the GL thread, and Skia faulted in its next flush. The runtime's signal handler returned
  into the fault, so the UI thread spun at 100 % in the kernel with the picture frozen; twice, about
  55 s into *2001*. One wrapper per lent target: four minutes clean. Found by `simpleperf` (no root
  needed for a debuggable app): half the samples in `__kernel_rt_sigreturn` under coreclr's handler.
- **Not done:** the film's mastering metadata. The layer carries none (`hdr metadata types=0`), so the
  television assumes its defaults. `EGL_EXT_surface_SMPTE2086_metadata` and CTA-861.3 surface
  attributes take it, from mpv's video parameters.
- **Not checked:** what the television shows. A screenshot is SurfaceFlinger's SDR conversion of the
  PQ buffer, which looks dim; the UI at 203 nits and the film's highlights need the user's eyes.

**Open decisions (the user's):**
1. **HDR for the whole app or only during HDR films.** The whole app is one surface, no switch: the
   television stays in HDR inside the app, the UI at 203 nits, and EGL memory doubles. Only during
   films (as on the desktop) needs a switch mid-session: the FP16 surface kept and its buffers'
   dataspace changed (`ANativeWindow_setBuffersDataSpace`, untested), or the EGL surface made again. The
   HDMI resynchronisation would fall together with the frame-rate switch at the film's start.
2. **Whether to pass the mastering metadata** (above), which only matters with HDR.

## 5. The frame rate (built, 2026-11-28; no public API)

- **The rate.** mpv's `container-fps`, read with the video's geometry and raised as an internal
  `BackendEvent.FrameRateChanged` (HultUI.Media shares its internals with HultUI.Media.Mpv), kept as
  the internal `MediaPlayer.VideoFrameRate`; null for a variable rate or none stated.
- **The film, not a preview.** A `VideoElement` that covers at least half of its window hands the
  rate to the window (`Window.SetContentFrameRate`, internal) as frames arrive and as it draws, and
  null when it unmounts or stops covering it; a smaller one hands none.
- **The display.** The window passes it to an internal hook, `Window.ContentFrameRateChanged`, which
  `HultUIActivity` sets:
  - Everywhere from Android 11: the surface's frame-rate vote becomes the film's rate as a fixed
    source (in place of the display's peak), so a phone moves to a multiple seamlessly.
  - On a D-pad device (a TV): `WindowManager.LayoutParams.preferredDisplayModeId` set to the mode at
    the panel's current resolution whose rate is the highest whole multiple of the film's
    (`DisplayRefresh.ForVideo`, 0.05 % tolerance: 23.976 is not 24), and back to 0 (the system's own)
    when the film goes. Android 11 switches modes for this where a vote would not (an HDMI
    resynchronisation: a second or two of black as the film starts and as it stops).

## 6. Order

Media keys (built), the player's bar (built), the frame rate (built), then 4K and HDR together,
measured on the Shield (spike done; §3 and §4). Next: the user's two decisions in §4, then the spike
made real (the environment variables replaced by the TV default and the HDR choice), the metadata,
and tests.
