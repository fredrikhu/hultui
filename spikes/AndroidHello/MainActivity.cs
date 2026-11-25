using Android.App;
using Android.Content.PM;
using Org.Libsdl.App;

namespace AndroidHello;

/// <summary>
/// SDL's own activity, from SDL3-CS's Android binding. It loads libSDL3.so, makes the
/// SDLSurface the app draws into, and calls <see cref="Main"/> on SDL's thread (SDLThread),
/// not Android's UI thread. That thread is HultUI's UI thread for the life of the app.
/// </summary>
/// <remarks>
/// The configuration changes are SDL's recommended set: SDL handles them itself, and an
/// activity Android re-creates on rotation would start a second SDL.
/// </remarks>
[Activity(Label = "HultUI spike", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.NoActionBar",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden
        | ConfigChanges.Navigation | ConfigChanges.UiMode | ConfigChanges.Density | ConfigChanges.FontScale
        | ConfigChanges.Locale | ConfigChanges.LayoutDirection)]
public class MainActivity : SDLActivity
{
    /// <summary>Only SDL itself: there is no native "main" library, the app is managed.</summary>
    protected override string[] GetLibraries() => ["SDL3"];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Spike switches, from `adb shell am start ... -e renderer software -e rebind 0`.
        HelloApp.Renderer = Intent?.GetStringExtra("renderer");
        HelloApp.Rebind = Intent?.GetStringExtra("rebind") != "0";
        HelloApp.RedrawAfterMs = int.TryParse(Intent?.GetStringExtra("redraw"), out int ms) ? ms : 0;
        HelloApp.BlockOnPause = Intent?.GetStringExtra("block") != "0";
        HelloApp.Ticker = Intent?.GetStringExtra("ticker") != "0";
        HelloApp.Sample = Intent?.GetStringExtra("sample");
        base.OnCreate(savedInstanceState);
        // Android 8+ draws a translucent "default focus highlight" over a focused view that is
        // not in touch mode. SDLSurface keeps focus, so after text typed as key events and Back,
        // the whole app sat under a 12 % white wash until restart.
        DisableFocusHighlight(Window!.DecorView);
    }

    private static void DisableFocusHighlight(Android.Views.View view)
    {
        view.DefaultFocusHighlightEnabled = false;
        if (view is Android.Views.ViewGroup group)
            for (int i = 0; i < group.ChildCount; i++)
                DisableFocusHighlight(group.GetChildAt(i)!);
    }

    /// <summary>Runs on SDL's thread once the surface exists.</summary>
    protected override void Main() => HelloApp.Run();
}
