# Android platform package, safe areas and the Back key — design note

**Status (2026-11-25): proposal, nothing built. Each of §2, §3 and §4 adds public API and needs
the user's OK before it lands.** Plan 20, phase 2 ([plan](../plans/20-mobile-support.md) §5).
This is the part of phase 2 that adds API. The rest is built on branch `claude/android-spike`:
- the lifecycle
- GL ES
- ICU through the NDK
- Vulkan requests drawing with GL ES on Android
- the desktop-only audit

Everything here comes from the spike (`spikes/AndroidHello/README.md`), which had to hand-write
each of these pieces.

## 1. What an app has to write today

The spike's `MainActivity` and `HelloApp` are what every HultUI Android app would copy:
- a subclass of SDL3-CS's `Org.Libsdl.App.SDLActivity`, with `GetLibraries()` returning
  `["SDL3"]` and `Main()` running the app
- `SDL_SetMainReady()` before the first window
- SDL's recommended `ConfigurationChanges` list on the `[Activity]` attribute
- turning off Android's default focus highlight on SDL's views, without which the whole app
  washes white after typing and Back (spike finding 7)
- aliases for `Application` and `ScrollView`, which Android's implicit usings make ambiguous

None of that is the app's business. Also:
- nothing keeps content clear of the status bar and the gesture bar
- Back leaves the app even when a menu or dialog is open
- there is no place for Android's secret store

## 2. `HultUI.Platform.Android`: the activity base

A new package, `net11.0-android`. It references `HultUI` and ppy.SDL3-CS, whose `net8.0-android`
assembly carries SDL's Java side. An app's activity becomes:

```csharp
[Activity(Label = "Aperture", MainLauncher = true, Exported = true,
    Theme = HultUIActivity.Theme, ConfigurationChanges = HultUIActivity.ConfigurationChanges)]
public sealed class MainActivity : HultUIActivity
{
    protected override void RunApp()
    {
        using var app = new Application("Aperture");   // width, height and style mean nothing here
        app.SetRoot(new ApertureApp(app, app.PrimaryWindow).Mount());
        app.Run();
    }
}
```

The proposed API, in `HultUI.Platform.Android`:

```csharp
public abstract class HultUIActivity : Org.Libsdl.App.SDLActivity
{
    /// <summary>SDL's recommended set: SDL handles these itself, and an activity Android
    /// re-created on rotation would start a second SDL. Constants, so they fit the attribute.</summary>
    public const ConfigChanges ConfigurationChanges = /* orientation, screen size and layout,
        keyboard, navigation, UI mode, density, font scale, locale, layout direction */;

    /// <summary>A theme with no action bar, drawn edge to edge (the app pads by the safe area, §3).</summary>
    public const string Theme = "@android:style/Theme.Material.NoActionBar";

    /// <summary>Builds and runs the app, on SDL's thread, which is HultUI's UI thread for the
    /// life of the process. Returning ends the activity.</summary>
    protected abstract void RunApp();
}
```

**What the base does, which apps no longer write:**
- loads only `libSDL3.so`, and calls `SDL_SetMainReady()`
- turns off the default focus highlight on SDL's views
- writes an exception that escapes `RunApp` to logcat before it ends the process
- registers the platform's services with `HultUI` (below), and forwards the Back key's default
  (§4)

**Platform services, with no further API.** `HultUI` is `net11.0` and cannot call Android's Java
APIs, so the package hands it what it needs through internal hooks
(`InternalsVisibleTo HultUI.Platform.Android`). The first is the **secret store**.
`CredentialStore.Default` becomes an Android Keystore store when the activity has registered
one: an AES key kept in the Keystore, encrypting entries in a file in the app's private folder.
Aperture's Jellyfin token goes there with no app change, because Aperture already takes
`CredentialStore.Default`. The store's class stays internal; only `CredentialStore.Default`'s
behaviour changes.

**Name clashes.** In a `net*-android` project, `HultUI.Application` and `HultUI.Core.Builders.ScrollView`
collide with Android's implicit usings. The package's README and `docs/GETTING-STARTED.md` say
to alias them, or to keep the UI in a project of its own and name only its app class from the
activity. The spike hosts the Fluent sample that way: its Android head references the
unchanged sample project and calls `new FluentApp(app, window)`. Renaming HultUI types for this
is out of proportion.

## 3. Safe areas

**Today:** edge-to-edge is in force for apps targeting API 35 and later; .NET 11 targets API
37. So the title sits under the status bar's clock and the last row under the gesture bar
(`a-software-first-frame.png`). SDL already reports the safe area (`SDL_GetWindowSafeArea`,
and `SDL_EVENT_WINDOW_SAFE_AREA_CHANGED` on rotation, as the spike logged). Nothing reads it.

**Proposed API**, on `HultUI.Window`:

```csharp
/// <summary>The window's edges the system covers (status bar, gesture bar, a display cutout),
/// in layout units. Zero on the desktop.</summary>
public Thickness SafeArea { get; }

/// <summary>Raised on the UI thread when SafeArea changes: a rotation, the bars shown or hidden.</summary>
public event Action? SafeAreaChanged;

/// <summary>Whether the root is laid out inside SafeArea (true, the default) or across the
/// whole window, for an app that draws behind the bars itself (a full-bleed video, a hero image)
/// and pads its own controls by SafeArea.</summary>
public bool LaysOutInSafeArea { get; set; } = true;
```

**The default keeps content visible.** The root is laid out inside the safe area, and the
window's background shows behind the bars. An app that wants to draw there (Aperture's
full-screen player, which should run under a hidden status bar) turns `LaysOutInSafeArea` off
and pads by `SafeArea` where it needs to. Overlays (menus, dialogs, toasts) are kept inside the
safe area too, whatever the root does. On the desktop `SafeArea` is zero, and nothing changes.

**The keyboard is not in the safe area.** The soft keyboard's inset is a separate value (a
visible area). It comes in phase 4 with the keyboard work, so `SafeArea` means one thing: what
the system's own furniture covers.

## 4. The Back key

**Today:** Back leaves the app (Android 12+ moves a root activity to the background), even with
a context menu or a dialog open. The spike saw this. Aperture's history navigation (back to the
previous page) cannot hear it.

**Proposed:**
- **A new key code, `KeyCode.Back`.** SDL reports Android's Back as `SDL_SCANCODE_AC_BACK` when
  `SDL_HINT_ANDROID_TRAP_BACK_BUTTON` is set; the platform sets it, and `EventTranslator` maps it.
  On the desktop the same key comes from keyboards with a browser-back key, which today map to
  `Unknown`.
- **Default handling, in order:**
  1. An app's `OnKeyDown` handlers see `KeyCode.Back` first, as any key. Aperture's history goes
     back here.
  2. Unhandled, it **closes the topmost overlay** (a menu, a dropdown, a dialog, a popover), as
     Escape does today.
  3. With nothing to close, the **system's default runs**: the activity base calls Android's own
     back handling, which moves the app to the background.

Predictive back (Android 14's swipe preview) needs Android's `OnBackInvokedCallback` instead of
SDL's trap. It is not designed here; the trap gives the classic behaviour, which is what phones
do without the preview.

## 5. Also in phase 2, with no API: a CI job

An Android job (an emulator on GitHub's Linux runners, which accelerate emulators since April 2024)
that builds the spike's APK and runs its three scripts:
- `resume-check.ps1`
- `background-check.ps1`
- `cycle-check.ps1`

It cannot be checked without pushing, which the user has asked to hold. It is written when
pushing is back on.

## 6. Open questions for the user

1. **§2, the package and `HultUIActivity`** with `RunApp()`, `ConfigurationChanges` and
   `Theme`; the Keystore store behind `CredentialStore.Default` with no new public type
   (recommended). Or a public `AndroidKeystoreCredentialStore`, as `WindowsCredentialStore` is
   public?
2. **§3, safe areas**: `Window.SafeArea`, `SafeAreaChanged` and `LaysOutInSafeArea` (default on,
   recommended). Or off by default, so every app pads for itself?
3. **§4, Back**: `KeyCode.Back`, then closing the topmost overlay, then the system's default
   (recommended)?
4. **Names** in all three?

## 7. Size

About a week and a half:
- **§2:** the package, the activity base, the Keystore store and its tests, about four days.
- **§3:** about three days. It needs headless tests through a new
  `HeadlessWindow.SetSafeArea`, which is one more test-driver method.
- **§4:** about two days.

A device check on the emulator goes with each, through the spike.
