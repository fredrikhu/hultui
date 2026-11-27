# Android

HultUI runs on Android 12 (API 31) and later. This is in progress (plan 20): touch scrolling,
gestures, touch text editing and a screen reader bridge are still to come. What is here works
on the emulator; the design notes in `docs/design/` (`APP-LIFECYCLE.md`,
`ANDROID-PLATFORM.md`) have the reasons and the measurements.

## An app

An Android app is a `net11.0-android` project with one activity, derived from
`HultUIActivity` in the `HultUI.Platform.Android` package:

```csharp
using Android.App;
using HultUI.Platform.Android;

[Activity(Label = "Hello", MainLauncher = true, Exported = true,
    Theme = HultUIActivity.DefaultTheme, ConfigurationChanges = HultUIActivity.ConfigurationChanges)]
public sealed class MainActivity : HultUIActivity
{
    protected override void RunApp()
    {
        using var app = new HultUI.Application("Hello");
        app.SetRoot(MyUi.Build());
        app.Run();
    }
}
```

- **`RunApp` runs on SDL's thread**, which is HultUI's UI thread for the life of the process,
  not Android's main thread. Everything HultUI says about the UI thread applies to it.
- **The window is the whole screen.** `Application`'s width, height and window style mean
  nothing on Android. There is one window: overlays (menus, dialogs, popovers) draw inside it,
  as on the desktop.
- **`ConfigurationChanges` and `DefaultTheme` are required.** SDL handles rotation and the
  rest itself; an activity Android re-creates would start a second SDL.
- **Name clashes.** A `net*-android` project's implicit usings bring `Android.App` and
  `Android.Widget`, whose `Application` and `ScrollView` collide with HultUI's. Inside the
  activity, `Application` is also Android's (`Activity.Application`). Write
  `HultUI.Application`, alias the two, or keep the UI in a project of its own and name only its
  app class from the activity. `spikes/AndroidHello` hosts the Fluent sample that way.
- **Renderer.** OpenGL ES 3.0. An app that asks for `RendererKind.Vulkan` draws with GL ES on
  Android until the Vulkan path supports it there; `HULTUI_RENDERER=vulkan` still forces it.

## The lifecycle

Home, the app switcher and the screen lock send the app to the background. See
[THREADING.md](THREADING.md#the-app-lifecycle): `Application.EnteringBackground` is where to
save, nothing is drawn while away, the UI thread keeps running, and a playing video pauses.

## Safe areas

Android draws apps edge to edge, under the status bar and the gesture bar. `Window.SafeArea`
says how far in from each edge the system covers, in layout units; `Window.SafeAreaChanged`
says when it changes (a rotation, the bars hidden).

- **By default the root is laid out inside the safe area**, with the window's background behind
  the bars. Nothing to do.
- **To draw behind the bars** (a full-bleed video, a hero image), set
  `window.LaysOutInSafeArea = false` and pad your own controls by `window.SafeArea`.
- **Overlays stay inside the safe area** either way.
- **The soft keyboard is not part of the safe area.** It has its own inset: see below.

## The soft keyboard

The keyboard shows while a field has focus and hides when focus leaves it. Say what a field
holds, and the phone shows the keyboard for it:

```csharp
TextInput.New().Kind(TextInputKind.Email)     // @, no capitals, no correction
TextInput.New().Kind(TextInputKind.Number)    // digits
```

- **The kinds:** `Text` (the default), `Password`, `Number`, `Decimal`, `Phone`, `Email`,
  `Url`, `Username`, `Name`, `Search`. `.Password()` wins over the kind. The desktop ignores all
  but `Password`.
- **A text area and the rich text editor** get a new-line key; a `TextInput` gets the keyboard's
  action key, which arrives as `KeyCode.Return`. The key's label (Search, Go, Done) cannot be set.
- **A number field** asks for digits, with the point and minus when its step or range needs them.
- **The root is laid out above the keyboard** by default, so a scroll view shrinks and can still
  be scrolled to its end, and the focused field is scrolled above the keyboard when it comes up or
  focus moves. `Window.KeyboardInset` is the keyboard's height over the window's bottom edge, in
  layout units, and `Window.KeyboardInsetChanged` says when it changes. An app that moves its own
  bottom bar sets `window.LaysOutAboveKeyboard = false` and reads the inset.
- **Overlays stay above the keyboard**: a dialog is centred in the space above it.
- **Put away with Back,** the keyboard comes back with a tap on the field. From Android 16 the
  keyboard takes Back itself and the app is not told; HultUI sees the keyboard's inset fall to 0
  and starts input afresh on the next tap (docs/design/SOFT-KEYBOARD.md §10).

## The Back key

Android's Back (the button, or the edge gesture) arrives as `KeyCode.Back`:
1. **The app's key handlers see it first.** A page's history goes back here: handle it, and
   set `Handled`.
2. **Unhandled, it does what Escape would** where the keyboard is: closes a context menu, a
   dropdown, a dialog, a popover.
3. **With nothing for Escape to do,** Android's own back moves the app to the background.

On the desktop a keyboard's browser-back key is `KeyCode.Back` too; unhandled, it does what
Escape would, and nothing more.

## Touch

Fingers are touch pointers, here and on desktop touchscreens:
- **A tap is a click** when the finger lifts within 8 DIP of where it landed.
- **A double tap** is two taps within 300 ms and 32 DIP: `ClickCount` 2, as a double click.
- **Hover ends when the finger lifts.** A `:hover` look shows the press, then goes.
- **No tooltips, no cursor.**
- **A second finger presses nothing.** It goes only to the element holding the first finger's
  capture (`PointerDownEvent.CapturePointer`), with `IsPrimary` false.
- **The system can take a finger** (a system gesture, a call). The element it pressed gets
  `PointerCancel` (`OnPointerCancel`) and no click; undo what the press started there.

Pointer events say which pointer they come from: `PointerId` (the mouse is always
`PointerEvent.MousePointerId`), `PointerType` and `IsPrimary`.

Gestures (`docs/design/GESTURES.md`):
- **A finger that moves 8 DIP scrolls** the innermost list or scroll view that can move that way,
  and the element it landed on gets `PointerCancel`, not a click. A sideways swipe on a shelf
  moves the shelf; an up or down one moves the page. A list already at its end lets the one
  around it take the swipe.
- **A flick throws the content**, and it slows to a stop. A touch on it stops it.
- **A long press** (500 ms) runs an `OnLongPress` handler, or opens the context menu, or shows
  the tooltip while the finger stays down.
- **`OnPan(axis, start, update, end)`** gives an element of your own the finger: swipe to
  dismiss, a carousel. Implement `IPanScrollable` on a surface of your own that scrolls.
- **A slider** follows a sideways swipe and lets a vertical one scroll the page.
- **An element that captures the pointer on its press** keeps the finger from every scroller.
- **A finger on empty space** clears the focus when it lifts as a tap, not when it lands, so a
  scroll of the page keeps the field's focus and its keyboard.

Text by touch (`TextInput`, `TextArea`, the rich text editor), as on Android:
- **A tap** places the caret and shows a handle under it for a few seconds; drag it to move the
  caret, tap it for Paste and Select All.
- **A sideways drag** on the focused field moves the caret with the finger, and selects nothing;
  an up or down one scrolls.
- **A long press or a double tap** selects the word and shows two handles and the edit bar: Cut,
  Copy, Paste, Select All, and "⋯" for the rest of the field's context menu. The finger, still
  down after a long press, grows the selection a word at a time.
- **A long press past the text's end** puts the caret there and shows the bar for Paste.
- **Drag a handle** to move its end of the selection.
- **A tap elsewhere, typing or the mouse** put them away.

## Secrets

`CredentialStore.Default` is the Android Keystore: an AES key that never leaves the device's
Keystore encrypts each secret into a file in the app's private folder. Code written against
`CredentialStore.Default` on the desktop needs no change.

## Building and running

The .NET Android workload (`dotnet workload install android`, from an elevated prompt when .NET
is installed in Program Files), the Android SDK and a JDK. The SDK and JDK can come from the
workload itself:

```bash
dotnet build MyApp.Android.csproj -t:InstallAndroidDependencies -f net11.0-android \
  -p:AndroidSdkDirectory=<sdk folder> -p:JavaSdkDirectory=<jdk folder> -p:AcceptAndroidSdkLicenses=True
```

That accepts Google's Android SDK licence on your behalf. Then build, install and start:

```bash
dotnet build MyApp.Android.csproj -c Release -p:AndroidSdkDirectory=<sdk> -p:JavaSdkDirectory=<jdk>
adb install -r bin/Release/net11.0-android/<application id>-Signed.apk
```

An app built against HultUI's source (project references, as the spike is) also passes
`-p:HultUIAndroid=true`: it adds the TalkBack bridge's Android target
(`HultUI.Platform.Accessibility.Android`), which the desktop builds leave out because they have no
Android workload. A build without it fails in the restore with NETSDK1005.

A Debug build installed with `adb install` needs `-p:EmbedAssembliesIntoApk=true`. Debug builds
otherwise expect fast deployment (`dotnet build -t:Install`), and the app aborts at start with
"Failed to load System.Private.CoreLib.dll".

HultUI's own messages go to logcat under `DOTNET` (what a desktop app writes to stderr) and
`HultUI` (`adb logcat -s DOTNET:* HultUI:*`).

## Waiting for events

The UI thread sleeps in `SDL_WaitEventTimeout` between frames. SDL 3.5.0, as `ppy.SDL3-CS`
2026.1002.1 ships it, never slept there on Android: each pass of its wait queued a poll sentinel,
which woke the wait again, so an idle app took a whole core. HultUI turns the sentinel off on
Android, which SDL's own fix (upstream 8dc8694) makes unnecessary once a package carries it.

## Video

`HultUI.Media` with the mpv backend plays on Android through the same render-API path as on the
desktop, drawn with GL ES. `HultUI.Media.Mpv.Natives` carries `libmpv.so` for `android-arm64` and
`android-x64`, built by `native/mpv/build-libmpv-android.sh` (the `android` job in
`mpv-natives.yml`; `native/mpv/README.md`). An app built against HultUI's source lists them itself,
as `AndroidNativeLibrary` items with their ABI, as the spike and `Aperture.Android` do.

What differs from the desktop:
- **Decoding.** `hwdec=mediacodec,mediacodec-copy`. FFmpeg needs the app's Java VM for MediaCodec,
  and `MpvBackend` hands it over before mpv starts.
  - Zero-copy comes first: `mediacodec`, through AImageReader. The decoder's frames reach mpv's
    GL textures directly.
  - The copy is the fallback where zero-copy will not start. It moves every frame through memory
    and uploads it again; `auto-safe` lands on it.
  - At 4K HEVC (36 Mbps) on a Galaxy S25, zero-copy cut the app from 50 % of a core to 30 %, and
    the codec service from 9 % to 4 %. HDR10 and Dolby Vision are tone-mapped right, subtitles
    draw, and a paused picture stays. The emulator decodes zero-copy too.
  - An older measurement said zero-copy saved nothing. It was taken while SDL's wait kept every
    app busy on a whole core.
- **Chroma scaling.** mpv scales chroma bilinearly on Android (`cscale=bilinear`). MediaCodec
  decodes to NV12, and on the S25's Adreno every separable scaler mpv has turned NV12's
  two-channel chroma plane into speckled noise.
- **Audio.** AudioTrack first, with a 0.5 s buffer, then AAudio. mpv 0.41's AAudio output froze
  the picture after a seek made while paused; AudioTrack underran with mpv's 0.2 s buffer.
- **https.** The Android libmpv's FFmpeg has no TLS. An https:// source is read through
  `HttpClient` instead (the platform's TLS and trusted certificates), by range requests mpv can
  seek with. Each read asks for at most 32 KB (`MpvHttpStream.MaxRead`). Android's response stream
  is a Java one, and every read copied mpv's whole ~128 KB buffer into a new Java array and back
  for one ~16 KB TLS record: 12–16 % of a core and most of the Java GC's ~10 % at 36 Mbps. With
  the cap, the stream's thread takes under 2 %. An HLS playlist (a Jellyfin transcode) cannot go that way, because FFmpeg fetches its
  segments itself. FFmpeg fetches it from a proxy on the loopback address instead, in plain http,
  and the proxy asks the server over https through `HttpClient` (`MpvLoopbackProxy`). Seeking and
  the duration work as on the desktop, and FFmpeg needs no TLS, which would make the libmpv build
  LGPLv3. The proxy forwards only requests that carry the process's random secret, and only to a
  server the app gave it. If a transcode still fails to open, Aperture plays the original file and
  says so.
- **Subtitles.** ASS fonts a file embeds are used as on the desktop. Otherwise libass gets the
  system's Roboto, so subtitles without embedded fonts in scripts Roboto lacks (CJK) draw nothing.
- **Still pictures on GL ES.** A paused picture is copied out of mpv's cache, and some GL ES drivers
  refuse the copy from mpv's usual float format (the emulator does). `MpvBackend` tries the copy
  once and gives mpv a format that works; the emulator gets `rgb10_a2`.
- **Size.** `libmpv.so` is 25 MB for arm64 and 31 MB for x86_64, before the APK's compression.
- **Backgrounding** pauses a playing video (see the lifecycle above).

`VideoPlayer` lays its bar out in two rows below 560 DIP wide, a phone held upright: the time and
the scrubber over the buttons, and no volume slider, since the phone has volume buttons.

## Not yet

These are plan 20's later phases:
- **No pinch** yet, and no magnifier over the text a handle moves.
- **TalkBack is checked on one phone, a Galaxy S25** (phase 6, docs/design/TALKBACK.md §8, §9
  and §11): it explores, swipes, clicks, scrolls, types and hears announcements, goes into an open
  drop-down and a long press's menu, moves through text by character, reads a dialog and a
  3,000-item list, and reads and runs Aperture's main views and the Fluent sample. Not yet on
  another phone or keyboard.
- **SDL's keyboard view** (`org.libsdl.app.SDLDummyEdit`, which SDL focuses for the keyboard's input)
  is hidden from TalkBack by the bridge. A view added to SDL's layout beside the surface is
  otherwise TalkBack's to meet, unnamed.
- **File dialogs** return Android's `content://` addresses, which `File.Open` cannot open.
- **No second window.**
- **An Android TV build** (phase 5).
