# Mobile support: Android and iOS

**Status (2026-11-25): scoping only. Nothing is built. Decided the same day: Android only for
now; iOS is deferred, with no Apple devices to build or test on. The other decisions in §6 are
settled too: the phase-1 spike runs first and decides the rest, with phone and TV as targets,
API 31 as the minimum, CoreCLR as the runtime, and signed APKs on GitHub releases.**

HultUI runs on Windows and Linux. This plan asks what it would take to run HultUI apps on
Android and iOS: what already works on those platforms, what the framework lacks, which apps
are worth it, how to test it, and the order to build it in. Every claim about this repository
names the file it comes from. Claims about dependencies come from the packages in the NuGet
cache or the sources listed at the end.

**The answer in short.**

- **The dependencies are mostly ready. The framework is not.** SDL3-CS ships Android and iOS
  natives plus SDL's Java activity. SkiaSharp 4 and HarfBuzzSharp 14 ship Android and iOS
  builds. .NET 11 runs on both. What is missing is HultUI's own: a GL ES path, an Android/iOS
  entry point, app lifecycle handling, touch input, the soft keyboard, safe areas, ICU loading,
  a Metal renderer for iOS, and two accessibility bridges.
- **Android only, for now (decided 2026-11-25).** Android can be built and tested from the
  Windows desk and on GitHub's Linux runners. iOS needs a Mac for every build, a Metal renderer
  HultUI does not have, and a video path mpv does not offer. With no Apple devices, iOS is
  deferred. Its findings stay in this plan for when it returns.
- **Aperture is the app to aim at.** It is a media client, which is what people use on phones,
  tablets and TVs. Reel follows almost free once Aperture's video works. Junction, Vesper
  Commander and Meridian are desktop tools and stay there.
- **The biggest single job is touch.** HultUI's input model has one mouse pointer, built
  around hover, clicks and wheel latching. Touch needs pointer IDs, gestures, pan with
  inertia, and touch text editing. All of that adds public API.
- **The biggest risk on iOS is video.** libmpv's render API offers OpenGL and software
  rendering, not Metal. Composited video on iOS needs either GL ES to Metal sharing through
  IOSurface, or a second media backend on AVFoundation.

## 1. What already runs there

| Piece | Android | iOS | Evidence |
|---|---|---|---|
| **SDL3 (ppy.SDL3-CS 2026.1002.1)** | Yes: `libSDL3.so` for arm, arm64, x64, x86, plus `lib/net8.0-android34.0/SDL3-CS.dll` and `SDL3-CS.aar` | Yes: `runtimes/ios/native/SDL3.xcframework` (device arm64, simulator arm64 + x86_64). No `net*-ios` managed build; the `net8.0` one is used | NuGet cache, `~/.nuget/packages/ppy.sdl3-cs/2026.1002.1/`. The Android DLL binds `org.libsdl.app.SDLActivity`, `SDLSurface`, `SDLDummyEdit` (the soft-keyboard input view) and `HIDDeviceManager`. The package README lists `ios` and `android` as supported. osu!framework, the package's maker, ships on both |
| **SkiaSharp 4.156.1** | Yes: `lib/net10.0-android36.0`, native assets in `SkiaSharp.NativeAssets.Android` | Yes: `lib/net10.0-ios26.0`, natives in `SkiaSharp.NativeAssets.iOS` | `skiasharp.nuspec` dependency groups. GL (GL ES on Android), Vulkan and Metal backends: `GRContext.CreateGl`, `CreateVulkan`, `CreateMetal(GRMtlBackendContext)` |
| **HarfBuzzSharp 14.4.0.101** | Yes (`net10.0-android36.0`, `HarfBuzzSharp.NativeAssets.Android`) | Yes (`net10.0-ios26.0`, `.iOS`) | `harfbuzzsharp.nuspec`. `SkiaSharp.HarfBuzz` is managed only (`net10.0`, `netstandard2.x`) |
| **.NET 11** | `net11.0-android`. CoreCLR is the default runtime from .NET 11 Preview 4 (Mono before). NativeAOT on Android is experimental. CoreCLR drops Android x86 and API 23 and older | `net11.0-ios`. CoreCLR is the default here too. NativeAOT is supported and still being refined | .NET blog, 2026-05-13 and 2026-07-14; MAUI runtime docs (sources). Every HultUI app already publishes NativeAOT (`<PublishAot>true</PublishAot>`), so the code is trim- and AOT-clean either way |
| **ICU** | Not as HultUI loads it today (§2.7) | Not as HultUI loads it today | `src/HultUI.Text/IcuLibrary.cs` |
| **libmpv** | Builds for Android exist (mpv-android's build scripts, NDK). HultUI's own build (`native/mpv/`) covers only Windows and Linux | MPVKit-style builds exist, but their maintainers call them learning material, and `vo=gpu-next` there is "not production ready". The render API has no Metal type | `src/HultUI.Media.Mpv.Natives/README.md`; MPVKit READMEs (sources). HultUI renders mpv through `MPV_RENDER_API_TYPE` OpenGL (`src/HultUI.Media.Mpv/Mpv.cs:205`, `MpvBackend.CreateGLContext`) |
| **Jellyfin.Sdk + Kiota (Aperture)** | Managed only. Should just work | Managed only. Should just work | `apps/Aperture/Aperture.Core/*.csproj` |
| **TextMateSharp (Syntax)** | No natives: its `onigwrap` ships for Mac Catalyst but not Android or iOS | No | `project.assets.json`. Affects Junction and Meridian only |

### Why the build strips the mobile natives, and what turning them back on takes

`Directory.Build.targets` (target `HultUITrimForeignNativeAssets`) removes the native assets of
Android, iOS, tvOS, Mac Catalyst, browser, musl, bionic, FreeBSD and the 32-bit and exotic
RIDs, plus native `.pdb` files. **It does this only for builds with no runtime identifier.**
The reason is **size, not a decision against mobile**. Measured 2026-10-03, every test and app
output folder held 574 MB, and a worktree's `bin` folders held 31 GB. After the change, each
folder held 89 MB (`docs/ENVIRONMENT.md`, "Build output trimming").

What the target actually removes in a desktop `net11.0` build is small. Aperture's
`project.assets.json` lists only SDL3-CS's four `libSDL3.so` files and its `SDL3.xcframework`,
plus TextMateSharp's Mac Catalyst `libonigwrap.dylib`. **SkiaSharp's and HarfBuzzSharp's
mobile natives never reach a desktop build at all.** They sit in TFM-specific dependency groups,
which only a project targeting `net*-android` or `net*-ios` resolves. That is why none of them
are in the local NuGet cache: nothing here has ever restored for a mobile TFM.

Turning them on therefore takes **almost nothing**:

1. **A mobile app project is the switch.** An app with `TargetFramework` set to
   `net11.0-android` pulls `SkiaSharp.NativeAssets.Android` and the other mobile natives through
   the TFM groups. The HultUI libraries can stay `net11.0`, because a `net11.0-android` app may
   reference `net11.0` libraries, and package assets are chosen by the app's TFM.
2. **Check one trap in phase 1.** .NET for Android resolves per-ABI assets in inner builds that
   set `RuntimeIdentifier`, so the trim target should not fire. If the outer build also runs
   `ResolvePackageAssets` with an empty `RuntimeIdentifier`, though, the target strips
   `libSDL3.so`. The guard is one condition on the target:
   `and '$(TargetPlatformIdentifier)' == ''`. The proof is `lib/x86_64/libSDL3.so` and
   `libSkiaSharp.so` listed inside the APK. **Checked in phase 1: the trap does not fire.**
   Both libraries, and `libHarfBuzzSharp.so`, are in the APK for both ABIs, so no guard is
   needed.
3. **Rewrite the `ENVIRONMENT.md` sentence** "platforms HultUI does not run on" when mobile
   ships.

`HultUIKeepAllRuntimes=true` is not the way. It brings back every RID's natives in desktop
builds, which helps nobody.

## 2. What the framework lacks

### 2.1 Entry point and threading

- **Android.** SDL's Android port runs inside `SDLActivity` (Java). The activity loads
  `libSDL3.so` and calls the app's main function on its own thread (`SDLThread`), not the
  Android UI thread. A HultUI Android app needs a C# `[Activity]` subclass of the bound
  `SDLActivity` that runs `Application.Run()` on SDL's thread. That thread becomes HultUI's UI
  thread, which fits the current single-UI-thread model (`docs/THREADING.md`). osu!framework's
  Android host shows how. Java callbacks (accessibility, insets, the activity's own events)
  arrive on the Android main thread and must be marshalled, as the UIA bridge already does
  through `AccessibilityActionRouter`.
- **iOS.** UIKit owns the main thread through `UIApplicationMain`. SDL's way in is `SDL_RunApp`,
  which starts UIKit and calls the app's main function from its delegate. osu!framework's iOS
  host does this from managed `Main`. SDL keeps a classic `while` loop workable on iOS by
  pumping the run loop inside `SDL_PumpEvents`. SDL's main-callbacks model is the alternative
  if that proves janky.
- **One window.** Both platforms give an app one full-screen window. The `Window` API, popups
  and multi-window (`docs/MULTI-WINDOW.md`) must degrade there. Overlays, menus, tooltips and
  dialogs already draw inside the window, because no code calls `SDL_CreatePopupWindow`, so
  they carry over.

### 2.2 Rendering: GL ES, Vulkan and Metal

| Platform | Today | Needed |
|---|---|---|
| Android, GL | `SDL3Window` asks for **desktop GL 3.3 core** (`SDL3Window.cs:406-408`) and builds Skia's interface with `GRGlInterface.CreateOpenGl` (`SkiaGLRenderSurface.cs:32`). Neither works on EGL/GL ES | `SDL_GL_CONTEXT_PROFILE_ES` 3.0 and `GRGlInterface.CreateGles`, chosen at runtime with `OperatingSystem.IsAndroid()`. Small. The surface's `GL_RGBA8` framebuffer format is valid on ES 3 |
| Android, software | `HULTUI_RENDERER=software` draws with Skia on the CPU into SDL's window surface, with no GPU or driver (`docs/ENVIRONMENT.md`) | Nothing in it is desktop-specific. **It is the quickest way to the first pixels in phase 1**, before GL ES works. Too slow to ship at phone resolutions |
| Android, Vulkan | `SdlVulkanHost` and `SkiaVulkanRenderSurface` (about 4,000 lines) assume desktop features: F16/HDR swapchains, `swapchain_maintenance1`, and mpv frames shared from a hidden **desktop GL 4.5** context (`SdlVulkanHost.Interop.cs:211-214`) through Win32 handles or opaque file descriptors | Android shares memory through `AHardwareBuffer` (`VK_ANDROID_external_memory_android_hardware_buffer`) and EGL images, not opaque file descriptors. Skia's Vulkan backend works on Android. **Later, not first.** GL ES is the Android renderer until Vulkan earns its place. Note that **Aperture and Reel ask for `RendererKind.Vulkan`** (`docs/MEDIA.md`). On Android they must ask for GL instead, or rely on the existing fallback from Vulkan to GL, which prints a line on stderr |
| iOS | Nothing | **A new Metal surface** (`SkiaMetalRenderSurface`): `SDL_Metal_CreateView` and `SDL_Metal_GetLayer` give a `CAMetalLayer`, each frame takes a drawable, and `GRContext.CreateMetal` with `GRBackendRenderTarget(GRMtlTextureInfo)` draws into it. **Skia covers Metal; HultUI does not.** One forum report says `CreateVulkan` returns null on iOS, so MoltenVK is not a shortcut. Apple deprecated OpenGL ES, so GL is no long-term option either. The same surface would serve macOS desktop |

**GPU context and surface loss** has no handling anywhere today. A search for device-lost,
context-lost and `Abandon` in `src/` finds nothing relevant. Android destroys the EGL surface
when the app goes to the background, and a context can be lost on some devices. The renderer
needs a path that drops and rebuilds the `GRContext`, the surface, cached images, glyph
atlases, and mpv's render context.

### 2.3 Touch input and gestures

The input model is **mouse-shaped**:

- `InputEvent` carries `MouseButtonInput`, `MouseMoveInput` and `MouseWheelInput`, with no
  pointer ID and no pointer type (`src/HultUI.Core/Events/InputEvent.cs:12-16`). Pointer events
  carry `Position`, `Button`, `Modifiers` and `ClickCount` only (`PointerEvents.cs`).
- **Touch arrives as SDL's synthesized mouse events** (`Window.cs:3875`, "Touch arrives as
  SDL's mouse events and is followed as the mouse is"). `EventTranslator` handles no
  `SDL_EVENT_FINGER_*` or `SDL_EVENT_PEN_*` events, and never reads `which`, so it cannot tell a
  synthesized event (`SDL_TOUCH_MOUSEID`) from a real mouse.
- **Scrolling is wheel-driven.** `WheelGesture` latches a run of wheel events to one scroller,
  with a 300 ms gap (`PointerEvents.cs:192`), and `ScrollEvent` carries `ConsumedX` and
  `ConsumedY` for nesting. A drag in a `ScrollView` today selects
  text or starts a drag. It never pans. There is no inertia, fling or overscroll code anywhere.
- **SDL3 has no gesture recognition.** SDL2's gesture module was removed, so recognizing
  gestures is HultUI's job.

What touch needs:

1. **Pointer identity.** A pointer ID and type (mouse, touch, pen) on pointer events, and
   finger events translated in `EventTranslator`. Synthesized mouse events are then dropped
   with `SDL_HINT_TOUCH_MOUSE_EVENTS=0`, or recognized and ignored.
2. **Tap.** A tap counts within a touch slop of about 8 dp. Hover states must not stick: today
   a tap leaves the tapped element "hovered", because the synthesized pointer stays there.
   Tooltips need another trigger, such as a long press.
3. **Long press** for context menus and selection.
4. **Pan with inertia.** A touch-pan latches to the first scroller in the direction of travel
   that can consume it, which is the same question `WheelGesture` and `ConsumedX`/`ConsumedY`
   answer. A child that reaches its edge hands the rest to its parent. The pan tracks velocity,
   and the fling decays on the frame clock. The frame clock is already virtual in headless
   sessions (`docs/HEADLESS.md`, "Virtual Time"), so inertia is testable to the millisecond.
   Pan must win over text selection and drag-and-drop by direction and slop, as on every touch
   platform.
5. **Pinch** for zoomable content (images, a future map or PDF view). Few elements need it, so
   it can wait until something does.
6. **Edge cases.** The OS cancels touches (a gesture navigation swipe, an incoming call).
   `SDL_EVENT_FINGER_CANCELED` must end every gesture cleanly. Palm and stylus input ride on
   `SDL_EVENT_PEN_*`.

All of this is **public API**: event fields, new events, and element hooks such as
`OnTap`/`OnLongPress`, or a gesture-recognizer object. It needs the user's OK (§6).

### 2.4 Soft keyboard and IME

What exists: `ITextInputHost` with `StartTextInput(TextInputKind)`, `SetInputArea` and
`ClearComposition` (`src/HultUI.Platform.SDL3/SDL3TextInputHost.cs`). `TextInputKind` has
`Text` and `Password` (`src/HultUI.Core/Platform/TextInputKind.cs`). `SDL3Window.StartTextInput`
uses `SDL_StartTextInputWithProperties` for passwords. Composition arrives as
`TextEditingInput`. On mobile, `SDL_StartTextInput` is what shows the on-screen keyboard, so the
basic show and hide on focus comes for free.

What is missing:

- **Input kinds.** Number, email, URL, search and multiline, each with a return-key hint. SDL
  maps these to `SDL_PROP_TEXTINPUT_TYPE_NUMBER`, capitalization, autocorrect and multiline
  properties. New `TextInputKind` members are public API.
- **The keyboard covers content.** On iOS, SDL shifts its view to keep the rectangle from
  `SDL_SetTextInputArea` visible. On Android with target SDK 35 or later, edge-to-edge is
  enforced and `adjustResize` no longer resizes the window, so the IME inset must be read from
  `WindowInsets` in Java and fed to layout. SDL does not expose that inset. HultUI then needs a visible-area value and a scroll
  of the focused field into view.
- **Touch text editing.** Tap to place the caret, long-press to select a word, selection
  handles, the cut/copy/paste menu, and the magnifier. None of this exists, and every text
  element needs it: `TextInput`, `TextArea` and the rich text editor (plan 16). This is a
  sizeable piece of its own.
- **Hardware keyboards** on tablets and Chromebooks keep working through the existing key path.
  `ShortcutMap` applies as is.

### 2.5 App lifecycle

`EventTranslator` and `SDL3Window` handle no lifecycle events. The SDL events the code uses
(`src/HultUI.Platform.SDL3/*.cs`) include `SDL_EVENT_QUIT` and the window and display events,
but none of `SDL_EVENT_WILL_ENTER_BACKGROUND`, `DID_ENTER_BACKGROUND`,
`WILL_ENTER_FOREGROUND`, `DID_ENTER_FOREGROUND`, `LOW_MEMORY` or `TERMINATING`.

Needed:

- **Stop drawing in the background.** iOS terminates an app that submits GPU work while in the
  background, and on Android the surface is gone. SDL blocks its event pump while the app is
  paused on Android (`SDL_HINT_ANDROID_BLOCK_ON_PAUSE`, on by default). HultUI must not render
  between `WILL_ENTER_BACKGROUND` and `DID_ENTER_FOREGROUND`, and must rebuild the GPU state on
  return if it was lost (§2.2).
- **Save state on the way out.** Both systems may kill a backgrounded app without another word.
  Settings saves (`src/HultUI.Core/Storage/JsonFile.cs`) must flush on
  `WILL_ENTER_BACKGROUND`.
- **Low memory.** Purge Skia's resource cache and image caches. Rest-purge code already exists
  (`src/HultUI.Rendering.Skia/RestPurge.cs`, `SizeRestPurge.cs`), so this needs a trigger, not
  new machinery.
- **Media.** Pause on background unless background audio is wanted, which is a product choice.
  Picture-in-picture and lock-screen media controls come later.
- **App-facing events** (`Application.Suspending`, `Resumed` and `LowMemory`, or similar) are
  public API.

### 2.6 Safe areas, orientation and DPI

- **Safe areas.** No code calls `SDL_GetWindowSafeArea` or handles
  `SDL_EVENT_WINDOW_SAFE_AREA_CHANGED`. Notches, rounded corners, the iOS home indicator and
  Android's system bars need a `Window.SafeArea` inset, or an environment value that layout and
  apps can read. That is public API.
- **Orientation.** `SDL_EVENT_DISPLAY_ORIENTATION` is in the code's event list already. Rotation
  arrives as a resize, which layout handles. Apps choose allowed orientations in the manifest
  or `Info.plist`.
- **DPI.** `SDL3Window.UpdateSize` (`SDL3Window.cs:2333`) lays out in DIPs scaled by
  `SDL_GetWindowDisplayScale` times the pixel density. On Android that should give the density
  (2.625 or 3 on phones); on iOS, the point scale (2 or 3). **This probably works unchanged.**
  Phase 1 checks it. The code that keeps a window's DIP size across monitors is desktop-only
  and must stay off.

### 2.7 Text: ICU and fonts

`IcuLibrary.Load` (`src/HultUI.Text/IcuLibrary.cs:44-63`) tries `"icu"` (Windows' combined
`icu.dll`), then `libicuuc.so`, then a scan of four Linux library folders. None of these exists
on either phone OS for an app to load:

- **Android.** Platform `libicuuc.so` is a private library that apps may not load (Android 7 and
  later). From API 31, the NDK offers ICU4C as `libicu.so` with **unversioned** symbols. The
  existing `DetectSuffixByProbing` path handles unversioned symbols by returning `""`. Phase 2
  must confirm that `ubrk_*`, `ubidi_*` and the script functions are in the NDK subset.
  Alternatives: a minimum of API 31, or bundling ICU, which costs megabytes of data even when
  trimmed.
- **iOS.** `libicucore.dylib` is on every device, but its headers are not public. Loading it
  risks App Review. Bundling a static ICU through NativeAOT's direct P/Invoke is the safe route.
- **The failure mode matters.** `BidiAnalyzer` and `ScriptLanguage` catch
  `DllNotFoundException`, but `LineBreaker.Iterator.Open` calls `IcuLibrary.GetDelegate` with no
  catch (`LineBreaker.cs:58`), so a missing ICU breaks text wrapping rather than degrading it.

**Fonts.** `SKFontManager` uses Android's and Apple's system font managers, so system fallback
should work. `SkiaFontCollection.EmojiFamilies` (`SkiaFontCollection.cs:308-311`) names
Windows' and macOS' emoji faces, and Noto for everything else. Android has `Noto Color Emoji`,
so it falls through correctly. iOS reports `IsMacOS()` false, so it needs `Apple Color Emoji`
added. The default UI face (Roboto on Android, SF via `.AppleSystemUIFont` on iOS) needs a look.

### 2.8 Files, clipboard, secrets, storage and network

| Need | Today | On mobile |
|---|---|---|
| **File dialogs** | `SDL3FileDialogs` (SDL's) | SDL3 has an Android backend that returns `content://` URIs, which must be opened through `SDL_IOFromFile`, not `File.Open`. HultUI's `IFileDialogs` hands out paths, so it needs a stream-based route. iOS support in SDL's dialog code was not confirmed; check `src/dialog/` upstream. Only Reel needs this |
| **Drag and drop, file lists** | OLE and Windows code (`WindowsFileDrag`, `WindowsDropTarget`) | Not applicable on phones. Android's drag-and-drop between apps on tablets comes much later |
| **Clipboard** | `SDL3Clipboard` (text) plus `WindowsClipboard` | SDL's text clipboard works on both. Images and file lists do not |
| **Secrets** | `CredentialStore.Default` is Windows Credential Manager, else `NoCredentialStore` (`src/HultUI/CredentialStore.cs:14`) | **Aperture stores its Jellyfin token there** (`ApertureSettings(CredentialStore.Default)`). It needs an Android Keystore-backed store and the iOS Keychain. The `ICredentialStore` seam exists, so this is new implementations behind it |
| **App folders** | `AppFolders` uses `SpecialFolder.ApplicationData` and `LocalApplicationData` | .NET maps these into the app sandbox on both. Should work; verify |
| **Network** | `HttpClient` | Plain `http://` is blocked by default: Android 9+ needs a network-security config, iOS needs App Transport Security exceptions. **A home Jellyfin server is usually plain `http://` on the LAN**, so Aperture hits this on day one. iOS 14+ also asks for local-network permission (`NSLocalNetworkUsageDescription`) |
| **Back button** | Nothing maps it (no `AC_BACK` in `src/`) | Android's Back must navigate, close an overlay, or leave. With `SDL_HINT_ANDROID_TRAP_BACK_BUTTON`, SDL sends it as `SDL_SCANCODE_AC_BACK`. Predictive back on Android 14+ needs the Java callback API |

### 2.9 Accessibility

The core is platform-neutral: `AccessibilityNode` trees, `TreeDiff`, and
`IAccessibilityBridge` (`ApplyChanges`, `NotifyFocusChanged`, `Announce` and the rest). The two
bridges, UIA (`HultUI.Platform.Accessibility.Windows`) and AT-SPI
(`HultUI.Platform.Accessibility.Linux`), are about 8,000 lines together.
`AccessibilityBridgeFactory` returns the Null bridge on any OS other than Windows and Linux.

- **TalkBack (Android).** An `AccessibilityNodeProvider` on SDL's `SDLSurface`, exposing HultUI
  nodes as virtual views. Explore-by-touch needs hit testing through the provider. Actions
  include click, the scroll actions and set-text. Events include `TYPE_VIEW_FOCUSED` and
  `TYPE_WINDOW_CONTENT_CHANGED`. Virtual view IDs are 32-bit `int`s, while `ElementId` is a
  `ulong` that packs owner slots (`AccessibleOwnerSlots.cs`), so the bridge needs an ID map. The
  provider is called on the Android main thread, so it reads snapshots, as the UIA bridge does.
- **VoiceOver (iOS).** `UIAccessibilityElement`s set as `accessibilityElements` on SDL's view
  (reachable through `SDL_PROP_WINDOW_UIKIT_WINDOW_POINTER`). Layout and screen-changed
  notifications go out through `UIAccessibilityPostNotification`. The .NET iOS bindings cover it
  without Objective-C.
- **Size.** Expect each bridge to come near one existing bridge, about 4,000 lines with tests.
  Shipping without them would drop below the bar the desktop apps hold
  (`docs/ACCESSIBILITY-PLAN.md`).

### 2.10 WebView

The desktop engines are WebView2 and CEF, and the page "is drawn by HultUI, not laid over it by
the operating system" (`docs/WEBVIEW.md`, "What behaves like any element"). The mobile engines,
`android.webkit.WebView` and `WKWebView`, offer no cheap off-screen frames. A mobile
`IWebEngine` would place a native view over the window at the element's bounds, which breaks
clipping, scrolling and overlays drawn on top. **No mobile candidate app needs a WebView**
(Aperture and Reel reference no `HultUI.WebView` project), so this is out of scope until one
does.

### 2.11 Desktop-only parts to fence off

These must report "not available" or stay unreachable on mobile, not crash:

- custom chrome and Snap Layouts (`WindowsCustomFrame`)
- `Window.Placement`
- multi-window
- `SingleInstance`
- the native drag source
- `MenuBar`
- cursor shapes
- the DevTools hotkeys
- `DesktopSettings`
- `HultUI.Terminal` and its PTY

Most are already behind `OperatingSystem.IsWindows()` checks. The work is an audit, not a
rewrite.

## 3. Which apps make sense on a phone or tablet

| App | Fit | Depends on | Verdict |
|---|---|---|---|
| **Aperture** (Jellyfin client) | Strong: browsing and playing a media library is a phone, tablet and TV job. Its shelves already have keyboard and D-pad-style navigation: arrow keys move a cursor across cards and shelves (`apps/Aperture/Aperture/ApertureApp.Cursor.cs:396`) | `HultUI.Media.Mpv` (libmpv), `CredentialStore` (token), Jellyfin.Sdk + Kiota (managed), HTTP to a LAN server. No WebView | **The target.** Needs mpv natives per platform, a secret store, a network config, and phone-width layouts |
| **Reel** (video player) | Medium: local files on phones live behind the system picker and `content://` URIs | `HultUI.Media.Mpv`, file dialogs, file associations (`FileAssociations.cs`, desktop) | **Second.** It shares the player UI (`HultUI.Media.VideoPlayer`) with Aperture, so it costs little once Aperture plays. Needs stream-based file opening |
| **Junction** (git client) | Weak: desktop tool. Needs the git CLI and TextMateSharp's onigwrap, neither on mobile | git, Syntax, keyring (GitLab) | No |
| **Vesper Commander** | Weak: a dual-pane file manager has no file system to manage on iOS, and little on Android under scoped storage | file system, OLE drag, terminal | No |
| **Meridian** (HTTP client) | Weak: request editing is keyboard-heavy. Its web preview needs a WebView | WebView, Syntax, keyring | No |

**Android TV and Google TV** deserve a look as an early target for Aperture. Input there is a
D-pad that SDL delivers as keys, which HultUI's focus model already handles (`docs/FOCUS.md`).
The TV needs no soft keyboard for browsing, no safe-area notch, and no touch. It would ship
Aperture on Android before the touch work of §2.3 is done.

## 4. Testing and CI

### What the headless layer covers

`HultUI.Headless` runs the real `Application` and `Window` with a CPU Skia surface and virtual
time, and never loads SDL (`docs/HEADLESS.md`). Most of the new framework logic is
platform-neutral and belongs there:

- layout at phone sizes and densities (`HeadlessSession.Create(scale: 3f)` already exists), and
  safe-area insets once a headless setter exists
- the touch model: pointer IDs, slop, tap and long press, pan latching and handoff in nested
  scrollers, velocity and inertia on virtual time, and cancellation. This needs touch methods on
  the input driver (`window.Touch`, `window.Pan`, `window.Fling` or similar), which is public
  API on `HultUI.Headless`
- the soft keyboard: `TextInputHost` is already an in-memory recorder (`IsTextInputActive`),
  extended with the input kind and a simulated visible area
- lifecycle handling: a simulated suspend and resume that checks nothing renders while
  suspended, that settings flush, and that caches purge on low memory
- accessibility tree content, which is the same `AccessibilityNode` tree. The bridge mapping
  (node to `AccessibilityNodeInfo` or `UIAccessibilityElement`) gets unit tests against fakes

What it **cannot** cover: SDL's Java and UIKit glue, EGL and Metal surfaces, real IMEs,
TalkBack and VoiceOver, mpv's mobile natives, packaging and signing. That needs a device layer.

### The device layer

- **Android on GitHub-hosted CI.** Standard Linux runners have supported hardware-accelerated
  Android emulation since April 2024, after an "Enable KVM" udev step.
  `reactivecircus/android-emulator-runner` is the usual action. `ubuntu-24.04` carries the
  Android SDK, and the job adds `dotnet workload install android`. Use x86_64 images, because
  CoreCLR in .NET 11 dropped x86, and software GL ES (`-gpu swiftshader_indirect`), which is
  enough for smoke tests. The tests: install the APK, start it, `adb exec-out screencap`,
  `adb shell input tap` and `swipe`, then assert on pixels or an in-app test hook. XHarness, the
  .NET team's device test runner, is a candidate for running xunit on the emulator. Budget 10–15
  minutes a run. The repository is public, so standard runners cost nothing (`docs/CI.md`).
- **iOS on GitHub-hosted CI.** `macos-15` is already in use (`meridian-keyring`). It is Apple
  silicon, and the iOS simulator there runs Metal. The job needs the Xcode version the .NET 11 iOS
  workload asks for (`xcode-select` if the image default differs) and
  `dotnet workload install ios`. Simulator builds need no signing. Device and TestFlight builds
  need certificates and a provisioning profile in secrets, kept away from pull requests per the
  public-repository rules in `docs/CI.md`.
- **The desk.** Android builds, the emulator (Hyper-V) and a USB phone all work on Windows.
  **iOS cannot be built on Windows.** Without a Mac, every iOS build goes through CI, which turns
  a seconds-long loop into a minutes-long one. That is why iOS is deferred.

## 5. Phases

Sizes are rough, in focused working time: **S** up to a week, **M** 2–4 weeks, **L** 1–2
months, **XL** more. Each phase ends in something that runs and is tested, as the performance
rounds did.

### Phase 1: Android spike (S)

**Done 2026-11-25, on branch `claude/android-spike`; report in `spikes/AndroidHello/README.md`.**
On an API 35 x86_64 emulator, HultUI draws through GL ES and through the software renderer, at
the right density (411×914 DIP at 2.625). Taps click, and the soft keyboard opens and types.
Release APK: 35.9 MB for both ABIs. First frame: about 1.0 s after process start, which looks
JIT-bound. Exit criteria met except Home-and-back, whose failure is written down:
- **Lost frames after a surface change.** The first frame drawn after returning from the
  background, or after a rotation, never shows. One more redraw a frame later fixes it.
- **Focus highlight.** Android's default focus highlight washes the whole app white after
  typing and Back. The fix is to turn the highlight off on SDL's views.
- **Corrections to this plan.** The trim-target trap (§1, item 2) does not fire. ICU already
  loads through the private `libicuuc.so`, and the public `libicu.so` also works (§2.7).

A HultUI sample that draws and takes touch on the Android emulator. Spike code only, on its own
branch under `spikes/AndroidHello/`, as `spikes/CefOsr` and `spikes/WebView2Capture` were.

- `dotnet workload install android`, and a `net11.0-android` app (x86_64 and arm64) that
  references `src/HultUI` and friends as `net11.0` projects. It sets
  `SupportedOSPlatformVersion` to 31 and keeps the default CoreCLR runtime (§6)
- an activity subclassing the bound `SDLActivity`, running a small app (a counter button, a
  long `ScrollView`, a `TextInput`) on SDL's thread
- first pixels through the software renderer (`HULTUI_RENDERER=software`), which needs no GL
- then the GL ES context and `GRGlInterface.CreateGles` behind `OperatingSystem.IsAndroid()`,
  as a spike patch
- a check that `libSDL3.so`, `libSkiaSharp.so` and `libHarfBuzzSharp.so` are in the APK, and
  the trim-target guard from §1 if they are not

**Exit criteria**

- The first frame draws on an API 35 x86_64 emulator with GL ES (software is the fallback
  result), at the right density, with crisp text.
- A tap on the button raises the counter, through SDL's touch-to-mouse synthesis.
- The text field shows the soft keyboard and takes typed text.
- These are recorded: APK size, cold start to first frame, and whether ICU loaded.
- Home and back to the app either works or its failure is written down.
- A short report lists everything that broke. Optional: the same on a real phone.

### Phase 2: Android platform layer (L)

**Status 2026-11-25, branch `claude/android-spike`.**

Built, with no new API beyond the approved lifecycle:
- the lifecycle (`docs/design/APP-LIFECYCLE.md` §8)
- GL ES as a proper path
- a Vulkan request draws with GL ES on Android (`RendererSelection`)
- ICU through the NDK's `libicu.so`, and a line-break fallback when ICU is missing
- the §2.11 audit: every not-Windows branch is benign on Android; file dialogs (`content://`
  URIs) and a second window stay open items

Checked on the emulator:
- the Fluent sample runs unchanged and navigates by tap. Its desktop layout is squeezed at phone
  width, which is app layout work.
- 60 trips to the background, through `scripts/cycle-check.ps1`: same process, no errors, the
  screen drawn after every return. PSS went 115 → 125 → 129 → 132 → 125 → 125 → 136 MB, every
  10 trips, so it falls back rather than grows in a line. The emulator reports no GPU memory,
  so a GPU leak check needs a device.

Built after the user's OK (`docs/design/ANDROID-PLATFORM.md` §8), and checked on the emulator:
- `HultUI.Platform.Android` with `HultUIActivity` and the Keystore store behind
  `CredentialStore.Default`
- `Window.SafeArea`: the bars and the cutout, read by the activity rather than SDL, whose area
  also takes out the gesture zones
- `KeyCode.Back`
- the status bar's icons following the theme
- an app's guide, `docs/ANDROID.md`

Waiting for pushing to be allowed: the CI job.

**Phase 2's exit criteria:**
- Met on the emulator:
  - the sample and the Fluent sample run
  - backgrounding 20 (60) times does not crash and shows no steady memory growth
  - rotation lays out again
  - content stays clear of the system bars
  - headless tests cover the lifecycle
- Met on a real phone too, a Galaxy S25 (Android 17, arm64, Adreno 830), on 2026-11-25:
  - first frame 665–682 ms from a cold start
  - 60 trips with no growth, GPU memory 59 → 41 MB
  - Back, safe areas and the Keystore store as on the emulator
  - `spikes/AndroidHello/README.md`, "On a real phone"
- Found there: SDL reports the display at 30 Hz, its idle rate, so HultUI paces at 30 fps on a
  120 Hz screen. To fix with phase 3's scrolling.
- Open:
  - content clear of the keyboard (the IME inset), which is phase 4's
  - the CI job

Not yet: a real device.

- **A platform package.** `HultUI.Platform.Android` (`net11.0-android`): the activity base,
  Java callbacks (insets, back, lifecycle), and the Android credential store.
- **GL ES** as a proper path, not a spike patch.
- **Lifecycle (§2.5).** Stop rendering, rebuild GPU state, flush saves, purge caches.
- **Safe areas and the IME inset (§2.6, §2.4).**
- **ICU (§2.7).** NDK `libicu.so`, since the minimum is API 31 (§6). `LineBreaker` made to
  degrade the way `BidiAnalyzer` does.
- **Smaller items.** Emoji and default fonts, the back key, and the §2.11 audit.
- **A CI job** that builds the APK and runs the phase-1 smoke test on the emulator.

**Exit criteria**

- The sample and the Fluent sample run on the emulator and on one real device.
- Backgrounding and foregrounding 20 times leaks no GPU memory and never crashes.
- Rotation relays the layout correctly.
- Content stays clear of the system bars and the keyboard.
- Headless tests cover the lifecycle handling.
- The CI job is green.

### Phase 3: Touch input (L, public API)

§2.3, items 1–4 and 6: pointer identity, tap, long press, and pan with inertia and nested
handoff. Plus hover rules for touch (no sticky hover; tooltips on long press) and the headless
touch driver. Pinch waits until an element needs it.

**Exit criteria**

- Every scroller in the Fluent sample and in Aperture pans and flings by touch.
- Nested scrollers hand off at their edges.
- Text selection and drag-and-drop still work with a mouse.
- No desktop test changes behaviour.
- The headless tests cover slop, latching, handoff, velocity and cancellation on virtual time.
- The perf suite is flat.

**Status (2026-11-26): built, tested headless, and checked on the emulator; not on the phone yet.** Pointer identity
(`docs/design/POINTER-IDENTITY.md` §8) and the gestures (`docs/design/GESTURES.md` §9), with
`TouchPointerTests` (19) and `TouchGestureTests` (22). Left for this phase:
- the phone (the emulator passed a tap, a drag, a fling and a long press,
  `spikes/AndroidHello/scripts/touch-check.ps1`), two fingers, and frame time while flinging
  Aperture's grid
- the Fluent sample and Aperture by touch (the first exit criterion)
- the perf suite

### Phase 4: Soft keyboard and touch text editing (M, public API)

New `TextInputKind` members and return-key hints. The focused field scrolls into view above the
keyboard. Caret placement, word selection, handles and the edit menu for `TextInput`,
`TextArea` and the rich text editor.

**Status (2026-11-26): design note written, awaiting the user's OK**
(`docs/design/SOFT-KEYBOARD.md`, six decisions in §6). It recommends leaving return-key labels
out: SDL 3 has no property for them.

**Exit criteria**

- Aperture's server, login and search fields work with the soft keyboard on a phone.
- A word can be selected, copied and pasted by touch.
- The number field gets the numeric keyboard.

### Phase 5: Aperture on Android (L)

- **libmpv for Android.** arm64 and x86_64, built with the NDK from `native/mpv/` with the same
  LGPL trim, as a new job in `mpv-natives.yml` and an Android RID in `HultUI.Media.Mpv.Natives`.
  `Mpv.cs` gets `libmpv.so` candidates for Android.
- **Video on GL ES** through the existing render-API path. Hardware decoding is
  `hwdec=mediacodec` with mpv's AImageReader GL interop where it works, else
  `mediacodec-copy`. Audio through AAudio.
- **App changes.** The network-security config for `http://` LAN servers, the Keystore token
  store, and phone and tablet layouts (Aperture's layouts are desktop-sized).
- **An Android TV build** (§3, chosen in §6). It needs phase 2 and the mpv work, but no touch,
  so it can ship ahead of phases 3–4.
- **Distribution.** A signed APK as a GitHub release artifact (`aperture-android-vX.Y.Z`). The
  signing key lives in CI secrets, used only on tag and dispatch runs, never on pull requests.

**Exit criteria**

- On a real phone, Aperture signs in to the lab server, browses, and plays a 1080p HEVC and a
  4K title with hardware decoding at full frame rate.
- Subtitles (ASS) render.
- Playback survives backgrounding, which pauses it, and rotation.
- The token is in the Keystore.

### Phase 6: TalkBack bridge (L, can overlap phase 5)

`HultUI.Platform.Accessibility.Android` (§2.9).

**Status (2026-11-26): design note written, awaiting the user's OK** (`docs/design/TALKBACK.md`,
four decisions in §6). No public API.

**Exit criteria**

- TalkBack reads, navigates by swipe and explore-by-touch, and activates every control in the
  Fluent sample and Aperture's main views.
- Focus changes and live announcements are spoken.
- Tests against a fake provider host are green.
- This is required before any Android release beyond the user's own devices.

### Deferred: iOS bring-up (XL, needs a Mac)

Deferred on 2026-11-25, with no Apple devices. Kept as the plan for when iOS returns.

- **A platform package.** `HultUI.Platform.iOS` with the `SDL_RunApp` entry point and
  lifecycle (§2.1, §2.5).
- **The Metal render surface** (§2.2), which serves macOS desktop too.
- **ICU, bundled (§2.7)**, plus `Apple Color Emoji`.
- **The Keychain** credential store.
- **The runtime choice.** NativeAOT or the CoreCLR default.
- **A `macos-15` CI job** building and smoke-testing on the simulator.
- **Video decision (§6, question 6), made before Aperture on iOS starts.**

**Exit criteria**

- The sample and the Fluent sample draw and take touch on the simulator and on one device.
- The lifecycle rules hold: no GPU work in the background, verified by a background-render
  trap in Debug.
- The simulator job is green.

### Deferred: Aperture on iOS (L, more if video needs a new backend)

- **Video.** Either mpv through a GL ES context rendering into an IOSurface-backed
  `CVPixelBuffer` shared with Metal, or an AVFoundation `IMediaBackend` (the seam exists:
  `src/HultUI.Media/IMediaBackend.cs`) with Jellyfin's server-side remux or transcode to HLS.
- **The VoiceOver bridge (§2.9).**
- **App changes.** ATS exceptions, the local-network permission string, and the Keychain token.
- **Distribution.** TestFlight.

**Exit criteria**

- As for phase 5, on an iPhone.
- VoiceOver works as TalkBack does in phase 6.
- A TestFlight build installs.

### Later, each on its own merit

- Reel on Android, then iOS: stream-based file opening (§2.8) and the system picker.
- Android Vulkan with `AHardwareBuffer` interop for mpv and HDR.
- Picture-in-picture, background audio and lock-screen controls.
- Foldables and tablet multi-window resizing, and stylus input (`SDL_EVENT_PEN_*`).
- Pinch.
- A mobile WebView as a native overlay (§2.10).
- Play Store and App Store listings.

## 6. Decisions

Settled on 2026-11-25: the user took the recommendation on every question. Questions 6 and 10
are deferred with iOS.

1. **Go or no-go, and when.** Against what is left of plan 17 (q82/q83 open, round 20 next) and
   the app-team findings not yet built. **Decided: the phase-1 spike runs first, and its report
   decides whether phases 2 and later go ahead.**
2. **Android only, or iOS too? Decided 2026-11-25: Android only.** There are no Apple devices,
   so iOS waits until there is a Mac to build on.
3. **Which form factors?** Phone, tablet, Android TV or Google TV (§3: a TV build could ship
   before touch is done). **Decided: phone and TV. Tablets follow from the phone layouts.**
4. **Public API. Decided: each item gets a short design note, and lands only after the user OKs
   its shape. Lifecycle events and pointer identity go first, because phases 2 and 3 need
   them.** The items:
   - pointer ID and type on pointer events, plus touch and pen input events
   - gesture events or recognizers (tap, long press, pan, pinch) and their element hooks
   - `Window.SafeArea` and a visible-area value for the soft keyboard
   - lifecycle events on `Application` (suspend, resume, low memory)
   - new `TextInputKind` members and return-key hints
   - headless touch-driver methods
   - new packages: `HultUI.Platform.Android` and `HultUI.Platform.Accessibility.Android`
     (the iOS ones wait with iOS)
   - the Android Keystore credential store
5. **Distribution.** Sideloaded APKs on GitHub releases only, or Play Store, F-Droid,
   TestFlight and App Store? Store accounts cost money (Google: one-time fee; Apple: yearly), and
   store review shapes permissions and licensing. **Decided: signed APKs on GitHub releases
   first, with the signing key in CI secrets. Stores later, if at all.**
6. **Video on iOS (deferred with iOS).** mpv through GL ES to Metal sharing, which keeps one backend and is the
   riskiest technically, or AVFoundation with server-side transcoding, which means a second
   backend and needs a Jellyfin server that can transcode. Related: **LGPL libmpv in an App
   Store app**. The LGPL lets users replace the library, and how that squares with App Store
   terms is a legal question to settle before phase 8, not a technical one.
7. **Runtime.** CoreCLR (the .NET 11 default) or NativeAOT on Android. NativeAOT is what the
   desktop apps ship, but it is still experimental on Android. **Decided: CoreCLR. The spike
   measures startup on it, and NativeAOT is looked at again once it leaves experimental.**
8. **Minimum OS versions.** Android API 31 makes ICU free (§2.7), and .NET 11's CoreCLR needs
   API 24 or later. The iOS minimum follows the .NET 11 workload. **Decided: API 31 (Android
   12). Phones on Android 11 or older are not supported.**
9. **Devices.** Which phone (and tablet) to test on. **Decided: the emulator for phase 1. From
   phase 2, one mid-range phone with an Adreno or Mali GPU, plus a Google TV device.**
10. **macOS desktop (deferred with iOS: it needs a Mac too).** The Metal renderer and the iOS work give most of a macOS port. In scope
    or not?

## 7. Biggest risks

Risks 2 and 4 belong to iOS and are deferred with it. Questions 6 and 10 in §6 are deferred too.

1. **Touch in a mouse-shaped core.** Single pointer, hover-driven styling, click synthesis and
   wheel latching are woven through `Window.cs` (about 6,000 lines). Adding touch without
   changing desktop behaviour is the largest design job and adds the most public API.
2. **Video on iOS.** libmpv's render API offers OpenGL and software rendering, not Metal, and
   Apple has deprecated GL ES. Every composited route is new interop or a second backend.
3. **GPU loss and background rules.** HultUI has no path to rebuild its GPU state, and iOS kills
   apps that render in the background. Getting either wrong crashes in the field, not in tests.
4. **iOS without a Mac.** A CI-only build loop makes the XL phase slower still.
5. **Accessibility parity.** Two new bridges, each about the size of an existing one. Leaving
   them out would ship below the repository's own bar.
6. **ICU.** Private platform libraries on both systems. Bundling costs size. The line breaker
   fails hard without it today.
7. **Platform drift.** Android's edge-to-edge and predictive back, and iOS lifecycle changes,
   move under SDL each year. SDL3-CS is maintained for osu! and tracks SDL closely, which helps,
   but some Java and UIKit glue will be HultUI's own to keep up.

## Sources

Repository files are named where they are used. External sources:

- .NET blog, "MAUI moves to CoreCLR in .NET 11" (2026-05-13):
  <https://devblogs.microsoft.com/dotnet/dotnet-maui-moves-to-coreclr-in-dotnet-11/>
- .NET blog, "CoreCLR progress and Mono timeline" (2026-07-14):
  <https://devblogs.microsoft.com/dotnet/coreclr-progress-and-mono-timeline-dotnet-maui/>
- .NET MAUI runtimes and compilation: <https://learn.microsoft.com/dotnet/maui/deployment/runtimes-compilation>
- .NET MAUI Native AOT: <https://learn.microsoft.com/dotnet/maui/deployment/nativeaot>
- SkiaSharp `GRContext` (`CreateGl`, `CreateVulkan`, `CreateMetal`):
  <https://learn.microsoft.com/dotnet/api/skiasharp.grcontext>
- SkiaSharp discussion on Vulkan (Android works; iOS returns null):
  <https://github.com/mono/SkiaSharp/discussions/2461>
- SDL3 `SDL_ShowOpenFileDialog` (Android `content://` URIs): <https://wiki.libsdl.org/SDL3/SDL_ShowOpenFileDialog>
- GitHub changelog, hardware-accelerated Android virtualization on standard runners
  (2024-04-02): <https://github.blog/changelog/2024-04-02-github-actions-hardware-accelerated-android-virtualization-now-available/>
- android-emulator-runner: <https://github.com/ReactiveCircus/android-emulator-runner>
- MPVKit (iOS libmpv; maturity notes): <https://github.com/mpvkit/MPVKit>, <https://github.com/karelrooted/MPVKit>
- ppy.SDL3-CS package README (platform table), in the NuGet cache at
  `ppy.sdl3-cs/2026.1002.1/README_nuget.md`
