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

A Debug build installed with `adb install` needs `-p:EmbedAssembliesIntoApk=true`. Debug builds
otherwise expect fast deployment (`dotnet build -t:Install`), and the app aborts at start with
"Failed to load System.Private.CoreLib.dll".

HultUI's own messages go to logcat under `DOTNET` (what a desktop app writes to stderr) and
`HultUI` (`adb logcat -s DOTNET:* HultUI:*`).

## Not yet

These are plan 20's later phases:
- **Touch text editing.** A finger places the caret but does not select; selection handles and
  the edit menu are phase 4. No pinch yet.
- **No TalkBack** until its bridge (phase 6).
- **File dialogs** return Android's `content://` addresses, which `File.Open` cannot open.
- **No second window.**
- **Video** needs a libmpv build for Android (phase 5).
