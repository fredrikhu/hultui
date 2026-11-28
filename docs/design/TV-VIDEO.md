# Video on a TV: the remote's media keys, 4K, HDR and the frame rate — design note

**Status (2026-11-28): media keys, the player's bar, the frame rate, 4K and HDR10 built and checked
on the Shield; the user's look at HDR10 on the television left (§4).** Plan 20 phase 5, the Android TV
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

## 3. 4K: a surface the size of the panel (built, 2026-11-28; the user's choice: always on a TV)

On a TV (a D-pad and no touch screen), `HultUIActivity.DrawAtPanelSize`; nothing public. A spike
measured it first, the numbers below.
- **The panel gets every pixel.** The Shield's display is 3840×2160 under a 1920×1080 `wm size`
  override, and the compositor projects the UI's 1080p onto the panel. `SurfaceHolder.setFixedSize`
  at the panel's size (`Display.Mode`; `DisplayMetrics` report the UI's 1080p) gives a buffer the
  hardware composer scans out 1:1: source crop and display frame both 3840×2160, DEVICE composition.
- **The layout keeps its size.** SDL reports a 3840×2160 window at density 2, which would lay out at
  1920×1080 DIP; an internal `SDL3Window.PlatformSurfaceScale` of 2 multiplies the display scale (4 in
  all), so the layout stays 960×540 DIP, drawn at 4K.
- **Cost, measured in the spike** (a frame log since removed: frame time, and flush + present, each
  5 s):
  - A focus move on Home: 12–15 ms a frame at 1080p, 14–15 ms at 4K, mostly waiting in present.
  - A page's first frame (images arriving): up to 120 ms at 4K.
  - Bleach (1080p, scaled by mpv into a 4K target): 23.98 fps delivered, every frame 41.7 ms after the
    last (SurfaceFlinger's present times, none dropped), 8.7 ms a frame, app CPU 37 %.
  - *2001* (4K HDR10 HEVC, decoded by `OMX.Nvidia.h265.decode`): about 10 ms a frame. It stalls only
    to buffer: the stream outruns the Shield's 86 Mbps Wi-Fi at any surface size.
  - Memory on Home: EGL 16 MB at 1080p, 37 MB at 4K; app PSS 419 MB and 488 MB.
- **What Android hands over in view pixels.** SDL passes on the view's own coordinates for the mouse,
  normalises a finger by the surface's size, and HultUI.Platform.Android reads the insets and the
  keyboard's height from the view system; SDL hands the keyboard's caret rectangle back to it. All of
  them are half the surface's pixels, so they go through `SDL3Window.ViewScale` (layout units per view
  pixel: `InputScale` × `PlatformSurfaceScale`), not `InputScale`. Not checked with a mouse on the
  Shield.

The cost is within the noise of a 1080p frame, and there is no resize mid-session.

## 4. HDR10 (built, 2026-11-28; the user's choice: only while an HDR video shows, with its metadata)

### What the spike found

A spike made the whole surface PQ for the session (the EGL surface made in
`EGL_GL_COLORSPACE_BT2020_PQ_EXT` through `SDL_EGL_SetAttributeCallbacks`):
- **No 10-bit surface on a Tegra X1.** Its EGL lists 56 configs; the deep ones are all RGBA16F (float),
  none RGB10_A2, so asking for 10/10/10/2 left HultUI on the software renderer. The surface is half
  float (`SDL_GL_FLOATBUFFERS`), holding PQ-encoded values.
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
- **The layer carried no metadata** (`hdr metadata types=0`), so the television assumed its defaults.

### As built

The user chose HDR10 only while an HDR video is on screen, as on the desktop, with the film's
metadata passed on. Nothing public; `HULTUI_VIDEO_HDR=0` turns it off.
- **The window.** On a TV whose display lists HDR10 (`HultUIActivity.ShowHdrVideoAsHdr10`, from
  Android 9), `SDL3Window.PlatformHdrOutput` asks EGL for a half-float framebuffer, in EGL's default
  colour space; without one, 8-bit and SDR, as before. Half float for the whole session, since the
  format is fixed when EGL makes the surface: EGL 70 MB on Home where 8-bit took 37 MB.
  `AndroidHdrOutput` tags the buffers sRGB, or BT.2020 PQ, after each swap that follows a change, so
  the frame drawn in the old space leaves with the old tag. EGL sets a dataspace only as it makes the
  surface, so a tag set after it stays, and a new window (Android gives one after the app comes back)
  is tagged again. The GL driver writes PQ and sRGB values alike.
- **The tag goes past the NDK.** `ANativeWindow_setBuffersDataSpace` took sRGB and refused BT2020_PQ
  with EINVAL on the Shield: the NDK's wrapper first asks SurfaceFlinger whether the display takes
  HDR, and an app is told no, though the display lists HDR10 and SurfaceFlinger switches the
  television for a PQ layer from EGL. So where the NDK refuses, the tag goes through the window's own
  `perform` entry, as Android's EGL sets it (`native_window_set_buffers_data_space`). That is
  `ANativeWindow` as `system/window.h` lays it out, the struct every vendor's EGL driver is built
  against: checked by its magic and size first, and without the match HDR video stays SDR. The
  operation is `NATIVE_WINDOW_SET_BUFFERS_DATASPACE`, 19; 18 is `SET_SIDEBAND_STREAM`, which, called by
  mistake on the Shield, faulted in `Parcel::writeNativeHandle`.
- **The surface follows the pictures** with the Vulkan surface's rule (`HdrPictureFollower`): the
  frame is drawn in sRGB (`RgbaF16`, sRGB-encoded) until a frame draws an HDR picture. After that frame
  the next one is drawn in PQ, the window tags PQ, and that frame is owed (`IPictureRangeSurface`, as on
  Vulkan). A frame whose video is still loading keeps what was shown; a frame without an HDR picture
  goes back to sRGB. An HDR picture anywhere counts, as on the desktop: a TV's only video is the
  player over the window, and the HDMI switch falls together with the frame-rate switch (§5).
- **The film in PQ.** While the frame is PQ, the surface lends each video a 10-bit RGB10_A2 target of
  its own marked `GpuFrameTarget.Pq` (internal); mpv is told `target-prim=bt.2020`, `target-trc=pq`
  through the same confirmed, asynchronous path as linear light (`MpvTargetColour.Pq`), and the frame
  wraps the target tagged PQ. Back in sRGB the targets are deleted and the video draws into its own
  8-bit texture again.
- **The metadata.** mpv's `video-params` (`prim-red-x` … `prim-white-y`, `max-luma`, `min-luma`,
  `max-cll`, `max-fall`; `MpvBackend.MetadataOf`) reach the surface through `IHdrMetadataSource` and
  `IPictureRangeSurface.HdrMetadataDrawn`, with each draw of an HDR picture. The window sets them as
  EGL surface attributes (SMPTE ST 2086, CTA-861.3, in units of 1/50,000), which Android's EGL hands
  the buffers at the next swap. A film that states none sends none. PQ's ceiling, 10,000 nits, is
  no stated peak: libplacebo puts it in `max-luma` when the video gives none, which is what *2001*
  through the Shield's MediaCodec decoder reports (no light levels either; the hardware decoder's
  frames carry no mastering SEI). So on the Shield that film sends no metadata, and the television
  uses its defaults.
- **Tests:** `MpvTargetColourTests` (PQ asked once each way, frames stay encoded, metadata read, the
  named primaries standing in, PQ's ceiling no peak),
  `GpuFrameSinkTests` (metadata reaches the surface only with an HDR picture; a PQ target reaches the
  producer), `AndroidHdrOutputTests` (the EGL attributes and their scale). The switch itself needs the
  Shield.
- **Checked on the Shield** (the log line `[HultUI] HDR10 output: …` says each tag):
  - Home: half-float buffers tagged sRGB, the display in SRGB, the UI as in 8-bit.
  - *2001* (HDR10): the buffers tagged PQ, the layer BT2020_PQ, the display in BT2100_PQ at 4K
    23.976 Hz, HDMI Rec.2020 12-bit 4:2:2; mpv confirmed bt.2020/pq. Frames on time between the
    stalls Wi-Fi causes.
  - Back: tagged sRGB, the display SRGB at 59.94 Hz, the page whole. A second round trip the same.
  - Bleach (SDR): the display stays SRGB at 23.976 Hz; 23.98 fps, none late.
- **Not checked:** what the television shows. A screenshot is SurfaceFlinger's SDR conversion of the
  PQ buffer, so the film's highlights need the user's eyes.

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

Media keys, the player's bar, the frame rate, 4K and HDR10: all built and checked on the Shield.
Left: the user's look at the television in HDR10.
