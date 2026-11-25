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
- **The soft keyboard is not part of the safe area.** Its inset comes with the keyboard work.

## The Back key

Android's Back (the button, or the edge gesture) arrives as `KeyCode.Back`:
1. **The app's key handlers see it first.** A page's history goes back here: handle it, and
   set `Handled`.
2. **Unhandled, it does what Escape would** where the keyboard is: closes a context menu, a
   dropdown, a dialog, a popover.
3. **With nothing for Escape to do,** Android's own back moves the app to the background.

On the desktop a keyboard's browser-back key is `KeyCode.Back` too; unhandled, it does what
Escape would, and nothing more.

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
- **Touch.** Taps click, but a swipe does not scroll. Touch scrolling, inertia and long press
  are phase 3 (`docs/design/POINTER-IDENTITY.md`, `docs/design/GESTURES.md`).
- **The soft keyboard covers the field.** The visible area and touch text editing are phase 4.
- **No TalkBack** until its bridge (phase 6).
- **File dialogs** return Android's `content://` addresses, which `File.Open` cannot open.
- **No second window.**
- **Video** needs a libmpv build for Android (phase 5).
