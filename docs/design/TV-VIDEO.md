# Video on a TV: the remote's media keys, 4K, HDR and the frame rate — design note

**Status (2026-11-28): findings and proposals, nothing built.** Plan 20 phase 5, the Android TV
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
ring (with Tab on the desktop as well).

## 3. 4K: a surface the size of the panel (to design)

Two ways, both on `HultUIActivity`'s SDL surface:
1. **The whole surface at the panel's size** (`SurfaceHolder.setFixedSize(3840, 2160)`), the UI drawn
   at a pixel scale of 4. Simplest; HultUI already lays out in DIP. Cost: the UI's Skia drawing at
   four times the pixels on a Tegra X1, every frame, video or not. Measure first.
2. **Only while a film plays**: the surface goes to 4K when the player opens and back to 1080p when it
   closes. The UI is the player's controls then, little to draw. A resize mid-session goes through the
   same path as a window resize.
Either needs the panel's real size (`Display.Mode`, not `DisplayMetrics`, which report the UI's 1080p).

## 4. HDR10 (to design)

The surface as `RGBA_1010102` (or FP16) with `EGL_GL_COLORSPACE_BT2020_PQ_EXT`, the film's mastering
metadata through `EGL_EXT_surface_SMPTE2086_metadata`, mpv told the target (`target-trc=pq`,
`target-prim=bt.2020`, `target-peak`), and the UI drawn in PQ (HultUI's HDR path on Windows and Linux
does the same in scRGB, `docs/design/HDR.md`). Android switches the television to HDR when an HDR layer
is on screen. Only while an HDR film plays, as on the desktop.

## 5. The frame rate (to design)

Android 11: `Surface.setFrameRate(23.976f, FRAME_RATE_COMPATIBILITY_FIXED_SOURCE)` on the SDL surface
while a film plays, which the Shield honours when its "match content frame rate" setting is on, or
`WindowManager.LayoutParams.preferredDisplayModeId` for a mode outright (the Shield's own setting is
the user's choice). HultUI already votes a frame rate from API 30 (`HultUIActivity`).

## 6. Order

Media keys first (small, needs the API answer), then the frame rate (small, no API), then 4K and HDR
together, measured on the Shield.
