# Android spike (plan 20, phase 1)

**Result (2026-11-25): HultUI runs on Android. A HultUI app draws, takes taps, shows the soft
keyboard and takes typed text on an API 35 x86_64 emulator, through SDL3-CS's own activity,
with GL ES and with the software renderer.** It took two small patches to `src/` (GL ES) and
no other framework change. What it does not do yet is the list in "What broke", and that list
is what phase 2 of [plan 20](../../docs/plans/20-mobile-support.md) is for.

Everything here is a spike: not in the solution, not built by CI, and the two `src/` patches are
marked `SPIKE (plan 20, phase 1)` and live on branch `claude/android-spike` only.

## The question

Can a HultUI app, unchanged, run on Android with the packages the repository already uses
(ppy.SDL3-CS 2026.1002.1, SkiaSharp 4.156.1, HarfBuzzSharp 14.4.0.101, .NET 11 with CoreCLR),
and what breaks first?

## What it is

- `AndroidHello.csproj`: `net11.0-android`, API 31 minimum, `android-x64` and `android-arm64`,
  CoreCLR (the .NET 11 default), one `ProjectReference` to `src/HultUI`. The HultUI projects
  stay `net11.0`; package natives are chosen by the app's TFM.
- `MainActivity.cs`: since 2026-11-25 a subclass of `HultUIActivity` from
  `src/HultUI.Platform.Android` (docs/design/ANDROID-PLATFORM.md). That class does what this
  file first did by hand:
  - subclasses `Org.Libsdl.App.SDLActivity`, with `GetLibraries()` returning only `SDL3`
  - runs the app from `Main()` on SDL's thread (`SDLThread`), which becomes HultUI's UI thread
  - turns off Android's default focus highlight

  What is left here is reading the experiment switches. `-e sample credentials` tests the
  Keystore store. The app also has a dropdown, to check that Back closes an overlay, and logs
  its safe area.
- `HelloApp.cs`: the app. A counter button, a `TextInput`, a paragraph that must wrap (ICU's
  line breaker), and a 40-row `ScrollView`. It calls `SDL_SetMainReady()` first, since there is
  no `SDL_main`. It logs to logcat under `HultUISpike`, with `HULTUI_STARTUP` and
  `HULTUI_FRAMELOG` on, and probes how ICU loads.
- `SdlProbe.cs`: logs every app-lifecycle and window event SDL sends, and the window flags.
- `scripts/resume-check.ps1`: starts the app, sends it Home, brings it back, and counts drawn
  pixels before and after.
- `scripts/background-check.ps1`: leaves the app in the background (Home or `-Lock`). It
  reports whether the UI thread ran there, whether anything was drawn, any GL errors, and the
  frame after return.
- `scripts/cycle-check.ps1`: sends the app Home and back N times (ticker off), reading
  `dumpsys meminfo` every 10 trips, and checks the process, errors and the last frame.
- `-e sample fluent` runs the Fluent sample's UI (`FluentApp`, from the unchanged sample project)
  instead of this app.
- Intent extras for experiments: `-e renderer software`, `-e rebind 0|1`, `-e redraw <ms>`,
  `-e block 0` (SDL's `BLOCK_ON_PAUSE` off), `-e ticker 0` (no 1 s ticker, whose frame each
  second would hide a lost one; with it off and no `redraw`, the app changes nothing on return).
- **Since the lifecycle work**
  ([`docs/design/APP-LIFECYCLE.md`](../../docs/design/APP-LIFECYCLE.md)), the framework does
  what three of these did:
  - it redraws after a return (`redraw`)
  - it turns SDL's block off on Android, so `-e block 1` no longer brings the block back
  - the GL rebind was never needed

  Check the framework with `-e rebind 0` and no `redraw`, so the spike adds nothing.

## Running it

One-time setup on Windows. This is what this spike did; all paths are per-user except the
workload:

```powershell
dotnet workload install android            # elevated: the SDK lives in Program Files
dotnet build spikes/AndroidHello/AndroidHello.csproj -t:InstallAndroidDependencies -f net11.0-android `
  -p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk -p:JavaSdkDirectory=$env:LOCALAPPDATA\Android\jdk `
  -p:AcceptAndroidSdkLicenses=True
# then, with JAVA_HOME at that JDK: sdkmanager "emulator" "system-images;android-35;google_apis;x86_64"
# avdmanager create avd -n hultui35 -k "system-images;android-35;google_apis;x86_64" -d pixel_6
```

The emulator needs the Windows Hypervisor Platform (`emulator -accel-check`: "WHPX is installed
and usable"). It runs headless:

```powershell
& "$env:LOCALAPPDATA\Android\Sdk\emulator\emulator.exe" -avd hultui35 -no-window -no-snapshot -no-boot-anim -no-audio -gpu swiftshader_indirect
```

Build and install. **A Debug build must embed its assemblies to be installed with
`adb install`**; without it the app aborts with "Failed to load System.Private.CoreLib.dll",
because Debug builds expect fast deployment through `dotnet build -t:Install`. And
`-p:HultUIAndroid=true` builds the TalkBack bridge's Android part (docs/design/TALKBACK.md); without it
the restore leaves that target out and the build fails with NETSDK1005:

```powershell
dotnet build spikes/AndroidHello -c Debug -p:HultUIAndroid=true -p:EmbedAssembliesIntoApk=true -p:AndroidSdkDirectory=$env:LOCALAPPDATA\Android\Sdk -p:JavaSdkDirectory=$env:LOCALAPPDATA\Android\jdk
adb install -r spikes/AndroidHello/bin/Debug/net11.0-android/org.hultui.spike.androidhello-Signed.apk
adb shell am start -W -n org.hultui.spike.androidhello/crc64ea9bba10985cac6f.MainActivity
adb logcat -s HultUISpike:* DOTNET:* SDL:*
```

## Exit criteria (plan 20, phase 1)

| Criterion | Result |
|---|---|
| First frame on an API 35 x86_64 emulator with GL ES, at the right density, crisp text | **Yes.** `gl="OpenGL ES 3.0 (SwiftShader)"`, stencil 8. Layout 411.4×914.3 DIP at scale 2.625 on a 1080×2400, 420 dpi screen, which is exactly 1080/2.625 by 2400/2.625 (`b-gles-first-frame.png`). The GL ES frame matches the software frame (23 of 47,278 sampled pixels differ, at glyph edges) |
| Software renderer as the fallback | **Yes, and it happened on its own** before the GL ES patch: HultUI's existing fallback ("OpenGL window creation failed … falling back to software rendering") drew the first frame (`a-software-first-frame.png`) |
| A tap raises the counter (SDL touch-to-mouse) | **Yes.** Every `adb shell input tap` on the button gave one click, on both renderers |
| The text field shows the soft keyboard and takes typed text | **Yes.** Focus started text input and Android showed the keyboard (`mInputShown=true`). Text arrived character by character (`c-soft-keyboard.png`) |
| APK size, cold start to first frame, ICU | Below |
| Home and back works, or the failure is written down | **Fails, written down:** the app survives, but the screen stays black until the second frame after return (below) |
| A short report of what broke | "What broke", below |

## Numbers (emulator on an i7-14700K, WHPX, SwiftShader GL ES)

| | Debug | Release |
|---|---|---|
| APK, both ABIs | 108.9 MB (assemblies embedded; 30.4 MB without, fast deployment) | **35.9 MB** (same size as `.aab`) |
| Native libraries per ABI, uncompressed | | x86_64 39.5 MB, arm64 38.2 MB. `libSDL3.so` 12 MB, `libSkiaSharp.so` 9.4–10.1 MB, `libHarfBuzzSharp.so` 3 MB, CoreCLR + JIT about 9 MB |
| `am start -W` (activity shown) | about 600–880 ms | **285–328 ms** TotalTime (eight cold starts) |
| Process start → `SDL_Init` | 626 ms | 224–266 ms |
| → application ready | 696 ms | 271–322 ms |
| → **first frame shown**, GL ES | 1,598 ms | **976–1,048 ms** |
| → first frame shown, software renderer | | 829–831 ms |

The first frame comes about 700 ms after the application is ready (GL ES), or 530 ms on the
software renderer. On the desktop, the NativeAOT apps reach their first frame in about 230–245 ms
in total. The software run shows that most of it is not GPU work. The likely cost is HultUI's
first layout, text shaping and drawing being **JIT-compiled** by CoreCLR. Whether this build used
ReadyToRun was not checked. About 180 ms is GL setup and Skia's shader compilation on a
CPU-emulated GPU. A real phone has a real GPU and a
slower CPU, so measure there before drawing conclusions. ReadyToRun or NativeAOT is the lever to
try in phase 2.

## Findings

1. **The trim target in `Directory.Build.targets` does not touch a mobile build.**
   `lib/x86_64/` and `lib/arm64-v8a/` in the APK hold `libSDL3.so`, `libSkiaSharp.so` and
   `libHarfBuzzSharp.so`. The guard plan 20 §1 proposed is not needed.
2. **SDL3-CS's Android binding is enough for the entry point.** `SDLActivity` exposes
   overridable `GetLibraries()` and `Main()`. SDL calls `Main()` on its own thread, about 170 ms
   after `onCreate` in a Debug build. `SDL_SetMainReady()` before `SDL_Init` is all the app
   needs.
3. **GL ES needs two lines** (the `src/` patches): ask SDL for `SDL_GL_CONTEXT_PROFILE_ES` 3.0
   and build Skia's interface with `GRGlInterface.CreateGles`. Without them, SDL looks for
   desktop GL and window creation fails with "Could not initialize OpenGL / GLES library".
4. **DPI works unchanged.** `SDL_GetWindowDisplayScale` gives 2.625 at 420 dpi and pixel
   density 1, and `SDL3Window.UpdateSize` turns that into the right DIP size.
5. **ICU works today, through a private library.** HultUI's `IcuLibrary` tries `icu` (fails),
   then `libicuuc.so`, which loads from the i18n APEX with symbols suffixed `_75`. Text wraps.
   `libicuuc.so` is not a public NDK library, though. The public `libicu.so` (API 31+) also
   loads and exports `ubrk_open` unversioned (and `_75`). Phase 2 should try `libicu.so` first on
   Android.
6. **SDL sends the whole lifecycle; HultUI uses none of it, and still mostly copes.**
   - Home: `FOCUS_LOST`, `MINIMIZED`, `WILL_ENTER_BACKGROUND`, `DID_ENTER_BACKGROUND`,
     `LOW_MEMORY` (Android's `onTrimMemory`).
   - Return: `WILL_ENTER_FOREGROUND`, `DID_ENTER_FOREGROUND`, `FOCUS_GAINED`, `RESTORED`.
   - Rotation: `RESIZED`, `PIXEL_SIZE_CHANGED`, `SAFE_AREA_CHANGED`.
   - In the background the window is `SDL_WINDOW_MINIMIZED`, and HultUI already draws nothing
     while minimized.
   - The GL context survived background and return on the emulator. Re-binding it made no
     difference.
7. **The default focus highlight trap.** Android 8+ draws a translucent highlight over a
   focused view that is not in touch mode. `SDLSurface` keeps focus. After text typed as key
   events and then Back, the whole app sat under a 12 % white wash until restart: every pixel
   blended toward white, button `#4F46E5` became `(100,92,232)` (`d-focus-highlight-wash.png`).
   SurfaceFlinger shows no layer above the app; `dumpsys activity top` shows `SDLSurface`
   focused. `View.DefaultFocusHighlightEnabled = false` on SDL's views fixes it (`MainActivity`).
   A platform package must do this for every app.
8. **Android's implicit usings clash with HultUI names.** `Application` (`Android.App`) and
   `ScrollView` (`Android.Widget`) are ambiguous in a `net*-android` project, and so would be
   `Button` and others. An app needs aliases, as `HelloApp.cs` has. Worth a line in the docs, or
   a platform package that does not need app code in such a project.
9. **SDL's default block stops HultUI's UI thread in the background; turning it off is safe.**
   With `SDL_HINT_ANDROID_BLOCK_ON_PAUSE` on (the default), a 1 s ticker posting to the UI thread
   ran 0 times in 7.4 s away. With it off (`-e block 0`), it ran 7 times, through Home and
   through the screen lock. The frame log showed `frames/s=0.0 parked-ms/s=1000` while away, no
   GL or runtime errors, and a drawn screen after return (`scripts/background-check.ps1`).
   [`docs/design/APP-LIFECYCLE.md`](../../docs/design/APP-LIFECYCLE.md) §2 item 8 builds on this.

## On a real phone (2026-11-25)

**A Samsung Galaxy S25** (SM-S931B):
- Android 17 (API 37), arm64-v8a
- Adreno 830, OpenGL ES 3.2
- 1080×2340 at density 480, so scale 3.0 and 360×780 DIP
- three-button navigation
- Release build, over USB

The first run of the arm64 build. Everything the emulator showed holds:

| Check | Result |
|---|---|
| First frame (`m-s25-first-frame.png`) | Drawn correctly at 3×. Content clears the status bar (34.3 DIP) and the button bar (48 DIP); the status icons are dark over the light theme, light over Fluent's dark one (`n-s25-fluent.png`) |
| Cold start, steady (three runs) | Activity shown 132–143 ms; app ready 145–163 ms; **first frame 665–682 ms** from process start (the emulator: about 1.0 s). The first launch after installing took 5.8 s, once. About 520 ms between app ready and the first frame is HultUI's first layout and drawing, most likely being compiled by the JIT |
| `resume-check.ps1` (ticker off, app changes nothing on return) | 64,518 drawn samples before Home and after return |
| `background-check.ps1`, Home | 7 ticks in 7.7 s away, 0.0 frames/s, no errors, drawn on return. Not run with `-Lock`: the phone's lock screen asks for its PIN |
| `cycle-check.ps1`, 60 trips | Same process, no errors, drawn after the last. **GPU memory (meminfo's Graphics) 59.3 MB before, 40.7 MB after.** PSS 177 → 160 → 160 → 166 → 159 → 157 → 169 MB every 10 trips: no growth |
| Back | With the dropdown open it closed the list and the app stayed. With nothing open, the app's handler heard it and the app went to Samsung's launcher, process kept |
| Keystore store (`-e sample credentials`, two launches) | Round trip, delete, an entry kept across launches, nothing in the clear |
| ICU | `libicu.so` loads; the versioned names end in `_78` here, `_75` on the emulator. The unversioned names (`IcuLibrary`'s Android path) are what holds across devices |
| The Fluent sample | Runs; its desktop layout leaves the page no room at 360 DIP |

**New: the display's refresh rate is read wrong.**
- SDL reported `refresh=30Hz` (the `[HultUI] gfx:` line), while the screen runs at up to 120 Hz.
- `dumpsys display`: `renderFrameRate 120`, with modes of 120, 80 and 60 Hz among others.
- SDL reads the rate the display happens to idle at when the app starts; Samsung's adaptive screen drops to 30 Hz when nothing moves.
- HultUI paces frames at the window's `RefreshRate`, so on this phone animations, scrolling and the future fling would run at 30 fps.
- The fix belongs with phase 3's smooth scrolling:
  - read the display's peak rate in the activity
  - ask for it (`Surface.setFrameRate`, or the window's preferred display mode)
  - pace by it, or better by Android's vsync (`Choreographer`)

## Touch on the emulator (phase 3, 2026-11-26)

`scripts/touch-check.ps1` drives the phase 3 build through `adb shell input` and reads the
app's logcat lines. On the emulator (scale 2.625), all six checks pass:

| Check | Result |
|---|---|
| A tap on the button | A click, its press stamped `Touch`, pointer ID 2, primary |
| A slow drag on the list (214 DIP in 6 s) | Scrolled 206: the finger's travel less the 8 DIP slop, with no fling. At 4 s the finger moved 53 DIP/s, over the 50 a fling needs, and the list rightly went on 14 more |
| A fast swipe (214 DIP in 60 ms) | Flung 970 DIP and stopped at the list's end (`o-touch-fling-end.png`) |
| A long press (900 ms at one point) | The press, then its cancel, then `OnLongPress`, with no click |

Not checked yet: two fingers (the emulator's multi-touch needs its window), a drag on a slider in
the list, frame time while flinging, and all of it on the S25, including the refresh-rate fix.

## The soft keyboard on the emulator (phase 4, 2026-11-26)

`scripts/keyboard-check.ps1` taps the spike's text and number fields, presses Back and long-presses
the field's words, and reads logcat and the keyboard's `EditorInfo` (`dumpsys input_method`). All
five checks pass:

| Check | Result |
|---|---|
| A tap on the text field | The keyboard comes up; `Window.KeyboardInset` 336 DIP, told once |
| The text field's input type | `0x1c001`: text, sentence capitals, correction, one line (no new-line key) |
| A tap on the number field, the keyboard up | `0x2`, the digits; the inset follows the shorter keyboard (292) |
| Back | The keyboard goes; the inset is 0 |
| A long press on the words | "bravo" selected, two handles and the edit bar (`p-keyboard-selection.png`) |

Two findings, both fixed in `HultUIActivity`, each checked by taking the fix out:
- **Android's default soft-input mode panned the window.** When the keyboard came up, adjust-pan
  moved the whole window down by the status bar (49 DIP) to reveal SDL's hidden edit view at the
  window's corner, so everything was drawn, and touched, 49 DIP below its layout, under a black
  status bar. `SoftInput.AdjustResize` on an edge-to-edge window neither resizes nor pans.
- **SDL never tells a keyboard that is up about a new field's type.** It sends Java the type only
  when it shows the keyboard, which it skips while the keyboard is shown, and its Java only stores
  it (`SDLDummyEdit.setInputType`). A move from the text field to the number field kept the
  letters. On such a switch HultUI hands SDL's `showTextInput` the type SDL would have made and
  restarts the keyboard's input.

## Video on the emulator (phase 5, 2026-11-26)

`-e sample video -e file <path>` plays a file in HultUI's player over the Android libmpv
(`native/mpv/out/android-*`, which the build packs when it is there). The file has to be one the
app can read, such as one in its own files folder. Without one the sample asks for mpv's lavfi test
pattern, which the trimmed build lacks (no libavdevice), so nothing opens.

`scripts/video-check.ps1 -Clip <file>` puts a clip there, plays it, taps to pause and to play on,
and reads logcat and screenshots. Six of six with H.264 and AV1 1080p test patterns: the first
frame at about 600 ms, 47–60 frames in 2 s, and the picture on screen while playing and while
paused.

Checked by taking the fix out: with `GlesFboFormat` turned off, the paused picture is black
(0.04 of the middle band lit) and the check fails. mpv drew a still frame into its cache in
rgba16f and copied it into the target with a blit the emulator's GL ES refuses.

## TalkBack on the emulator (phase 6, 2026-11-26)

Two scripts, emulator only (adb root, raw touches; `scripts/talkback-lib.ps1` says why). Run
`keyboard-check.ps1` before them, or cold-boot after: the raw touches leave Gboard in its
hardware-keyboard mode.
- `scripts/talkback-check.ps1`, the main sample: 8 of 8. Explore by touch, a double tap clicks,
  an announcement, swipes in order, swiping past a list's end scrolls it, two fingers scroll.
- `scripts/talkback-a11y-check.ps1`, `-e sample a11y`: 13 of 13. A text area and a one-line field
  read by character with the caret moving, a held double tap opening a context menu with TalkBack's
  focus in it, Back closing it, a dialog whose title is said and that Back closes, a list of 3,000
  items.

## TalkBack on the phone, by hand (not done yet)

The phone has no root, so nothing can drive TalkBack there. To check by hand, after installing the
spike's current build (`adb install -r`, once the user OKs it; it replaces the spike already there):
1. Turn TalkBack on: Settings → Accessibility → TalkBack, or hold both volume keys if the shortcut
   is on.
2. Start the a11y sample: `adb shell am start -n
   org.hultui.spike.androidhello/crc64ea9bba10985cac6f.MainActivity -e sample a11y`.

Then:

| Do | Hear (or see) |
|---|---|
| Rest a finger on "Options" | "Options. Button", then "Double-tap and hold to long press" |
| Double tap anywhere | A confirm dialog: "Cancel. Button. In Delete the file?" |
| Back (the button bar's) | The dialog closes |
| Double tap and hold, on "Options" focused | A menu: "Rename. Menu item. 1 of 2" |
| Swipe right | "Delete" |
| Back | The menu closes |
| Rest on the notes field, double tap it, then swipe up | The keyboard; "e", the field's last character, and its caret moves one back |
| Swipe up again | "r" |
| The same on the "Title" field | "o", from "delta echo" |
| Rest on "Item 2" | "Item 2. 3 of 3000. In list. 3000 items" |
| Double tap | `activated Item 2` in logcat |
| Swipe right past the last item on screen | The list scrolls; "Item 13. 14 of 3000", and on |
| The main sample (no extra), "Tap me", double tap | "Counted 1", the announcement |

Watch for anything said twice, a stop that says nothing, or a highlight drawn where the element is
not (the S25 is scale 3.0, the emulator 2.625).

## What broke (for phase 2)

1. **Black after returning from the background.** The app survives Home and return (no crash,
   input works: a tap after return registered as a click), but **the first frame drawn after
   `DID_ENTER_FOREGROUND` never reaches the screen**, on GL ES and on the software renderer
   alike (`e-black-after-resume.png`). The second frame does. With nothing changing after
   return, HultUI draws nothing, so the screen stays black. If one tap is the first change, its
   frame is lost too.

   Measured with `scripts/resume-check.ps1`, counting drawn pixel samples:

   | Variant | Before Home | Just after return | After a tap |
   |---|---|---|---|
   | GL, no fix | 65,056 | **0** | 65,066 |
   | GL, context re-bound | 65,056 | **0** | 65,066 |
   | software, no fix | 65,056 | **0** | 65,066 |
   | GL, one more redraw 16 ms or 100 ms after the foreground event | 65,056 | **64,991** | 65,066 |
   | software, one more redraw 100 ms after | 65,056 | **64,992** | 65,066 |

   **Fix for phase 2:** on `DID_ENTER_FOREGROUND` and `WINDOW_RESTORED`, mark the whole window
   dirty, and draw again on a later frame, rather than once at once.

   **Fixed 2026-11-25** by the lifecycle work (`Window.SettleRedraw`;
   [`docs/design/APP-LIFECYCLE.md`](../../docs/design/APP-LIFECYCLE.md) §8). With the app changing
   nothing on return (`-e rebind 0 -e ticker 0`), the screen just after return holds 65,047
   drawn samples, as before Home, on GL and software.
2. **Rotation shows a stale frame.** After rotating to landscape, HultUI laid out at 914×411
   DIP correctly, but the screen showed a 1080-pixel white strip and black elsewhere
   (`f-rotation-stale-frame.png`). It was correct from the next frame (`g-landscape-next-frame.png`).
   This is the same cause as item 1: the first frame after a surface change is drawn before the
   new surface is ready. Same fix: redraw once more after `PIXEL_SIZE_CHANGED`. **Fixed
   2026-11-25** the same way: a resize settles on Android (`h-landscape-after-fix.png`, 1 % black
   against 55 % before, ticker off).
3. **The soft keyboard changes the window without telling HultUI.** With the keyboard up,
   Android moved SDL's surface below an opaque status bar and covered the bottom with the
   keyboard (`c-soft-keyboard.png`). HultUI got no resize event, so the bottom of the layout
   sits under the keyboard. Rotating with the keyboard open drew a frame at the wrong size
   until the keyboard closed. Phase 2's IME-inset work (plan 20 §2.4) covers this.
4. **No safe area.** Edge-to-edge is in force (the app targets API 37, the .NET 11 default), so
   the title is drawn under the status bar's clock and the last row under the gesture bar
   (`a-software-first-frame.png`). SDL already sends `SAFE_AREA_CHANGED`; HultUI needs to read
   `SDL_GetWindowSafeArea` (plan 20 §2.6).
5. **No touch scrolling.** A swipe in the `ScrollView` does nothing: SDL's synthesized mouse
   drag is not a pan (plan 20 §2.3, phase 3). Taps, as clicks, are all that touch does today.
6. **First frame about 1 s**, JIT-bound on the emulator (Numbers, above).

## The patches to `src/`

Both are guarded by `OperatingSystem.IsAndroid()` and leave every other platform as it was:

- `src/HultUI.Platform.SDL3/SDL3Window.cs`: GL ES 3.0 attributes on Android.
- `src/HultUI.Rendering.Skia/SkiaGLRenderSurface.cs`: `GRGlInterface.CreateGles` on Android.

Plan 20 phase 2 turns them into a proper GL ES path.
