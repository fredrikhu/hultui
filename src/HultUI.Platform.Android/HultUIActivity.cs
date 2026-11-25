using Android.Content.PM;
using Android.OS;
using Android.Views;
using Org.Libsdl.App;
using Log = Android.Util.Log;

namespace HultUI.Platform.Android;

/// <summary>
/// The activity a HultUI app's own activity derives from (docs/design/ANDROID-PLATFORM.md §2).
/// SDL's activity underneath loads SDL, makes the surface the app draws into, and calls
/// <see cref="RunApp"/> on its own thread, which is HultUI's UI thread for the life of the
/// process. What every app would otherwise write is done here: SDL's entry, the platform's
/// services, and Android's default focus highlight, which washes the app white after typing
/// and Back.
/// </summary>
/// <example>
/// <code>
/// [Activity(Label = "Aperture", MainLauncher = true, Exported = true,
///     Theme = HultUIActivity.DefaultTheme, ConfigurationChanges = HultUIActivity.ConfigurationChanges)]
/// public sealed class MainActivity : HultUIActivity
/// {
///     protected override void RunApp()
///     {
///         using var app = new HultUI.Application("Aperture");
///         app.SetRoot(new ApertureApp(app, app.PrimaryWindow).Mount());
///         app.Run();
///     }
/// }
/// </code>
/// </example>
public abstract class HultUIActivity : SDLActivity
{
    /// <summary>
    /// SDL's recommended set, for the <c>[Activity]</c> attribute: SDL handles these changes
    /// itself, and an activity Android re-created on rotation or a keyboard plugged in would start
    /// a second SDL. A constant, so it fits the attribute.
    /// </summary>
    public const ConfigChanges ConfigurationChanges =
        ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden
        | ConfigChanges.Navigation | ConfigChanges.UiMode | ConfigChanges.Density
        | ConfigChanges.FontScale | ConfigChanges.Locale | ConfigChanges.LayoutDirection;

    /// <summary>A theme with no action bar, for the <c>[Activity]</c> attribute. Android draws the
    /// app edge to edge; HultUI keeps content inside <c>Window.SafeArea</c>. Not called
    /// <c>Theme</c>: that would hide the activity's own <c>Theme</c> (Android's
    /// <c>Resources.Theme</c>) from an app's code.</summary>
    public const string DefaultTheme = "@android:style/Theme.Material.NoActionBar";

    private const string Tag = "HultUI";

    /// <summary>
    /// Builds and runs the app: make the <c>HultUI.Application</c>, set its root, and
    /// <c>Run()</c> it. Called on SDL's thread once the surface exists; returning ends the
    /// activity.
    /// </summary>
    protected abstract void RunApp();

    /// <summary>Only SDL itself: the app is managed, so there is no native "main" library.</summary>
    protected sealed override string[] GetLibraries() => ["SDL3"];

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Android 8+ draws a translucent highlight over a focused view that is not in touch
        // mode. SDLSurface keeps the focus, so after text typed as key events and Back the whole
        // app sat under a 12 % white wash (spikes/AndroidHello, finding 7).
        if (Window?.DecorView is { } decor) DisableFocusHighlight(decor);
        // The safe area HultUI lays out in: the bars and the cutout, as they arrive and change.
        FindViewById(global::Android.Resource.Id.Content)?.SetOnApplyWindowInsetsListener(new ContentInsetsListener());
        FollowPeakRefreshRate();
    }

    /// <summary>
    /// The display's peak refresh rate, for HultUI's frame pacing, and asked of Android for SDL's
    /// surface while it draws. SDL reads the rate an adaptive screen idles at when the app starts:
    /// 30 Hz on a Galaxy S25 that runs at 120, so HultUI paced at 30 fps.
    /// <c>Surface.setFrameRate</c> is a vote that counts only while the surface posts frames, so
    /// the screen still idles low when nothing moves.
    /// </summary>
    private void FollowPeakRefreshRate()
    {
        if (Display is not { } display) return;
        var current = display.GetMode();
        float peak = 0f;
        foreach (var mode in display.GetSupportedModes() ?? [])
            if (current is null || (mode.PhysicalWidth == current.PhysicalWidth && mode.PhysicalHeight == current.PhysicalHeight))
                peak = Math.Max(peak, mode.RefreshRate);
        if (peak <= 0f) return;
        global::HultUI.Platform.SDL3.SDL3Window.PlatformRefreshRate = peak;
        if (FindSurfaceView(Window?.DecorView) is { } surfaceView)
            surfaceView.Holder?.AddCallback(new FrameRateVote(peak));
        Log.Info(Tag, $"display peak refresh rate {peak} Hz (SDL saw {display.RefreshRate} Hz)");
    }

    private static SurfaceView? FindSurfaceView(View? view)
    {
        if (view is SurfaceView surface) return surface;
        if (view is ViewGroup group)
            for (int i = 0; i < group.ChildCount; i++)
                if (FindSurfaceView(group.GetChildAt(i)) is { } found) return found;
        return null;
    }

    /// <summary>Votes for the display's peak rate each time SDL's surface is made or changes.</summary>
    private sealed class FrameRateVote(float rate) : Java.Lang.Object, ISurfaceHolderCallback
    {
        public void SurfaceCreated(ISurfaceHolder holder) => Vote(holder);
        public void SurfaceChanged(ISurfaceHolder holder, global::Android.Graphics.Format format, int width, int height) => Vote(holder);
        public void SurfaceDestroyed(ISurfaceHolder holder) { }

        private void Vote(ISurfaceHolder holder)
        {
            if (holder.Surface is { IsValid: true } surface)
                surface.SetFrameRate(rate, (int)SurfaceFrameRateCompatibility.Default);
        }
    }

    /// <summary>
    /// Hands SDL3Window the insets content should clear (docs/design/ANDROID-PLATFORM.md §3):
    /// the system bars and the display cutout. SDL's own safe area also takes out the gesture
    /// zones, which Android keeps for edge swipes, not content. Then applies the insets as the
    /// view would have, so SDL's surface below still gets them.
    /// </summary>
    private sealed class ContentInsetsListener : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
            global::HultUI.Platform.SDL3.SDL3Window.SetContentInsets(
                new global::HultUI.Platform.SDL3.SDL3Window.PixelInsets(bars.Top, bars.Right, bars.Bottom, bars.Left));
            return view.OnApplyWindowInsets(insets) ?? insets;
        }
    }

    /// <summary>The status and navigation bars' icons, dark over a light theme and light over a
    /// dark one: Android's defaults for this theme are white, which vanished over HultUI's
    /// white background.</summary>
    private void FollowTheme(global::HultUI.Core.Styling.Theme theme)
    {
        var bg = theme.Background;
        bool light = 0.2126 * bg.R + 0.7152 * bg.G + 0.0722 * bg.B > 140;
        const int Mask = (int)(WindowInsetsControllerAppearance.LightStatusBars | WindowInsetsControllerAppearance.LightNavigationBars);
        RunOnUiThread(() => Window?.InsetsController?.SetSystemBarsAppearance(light ? Mask : 0, Mask));
    }

    /// <summary>SDL's entry: the platform's services, then the app.</summary>
    protected sealed override void Main()
    {
        // SDL's Android port expects SDL_main; there is none, so say main is ready before the
        // first window initializes SDL.
        SDL.SDL3.SDL_SetMainReady();
        RegisterServices();
        try
        {
            RunApp();
        }
        catch (Exception e)
        {
            // Into logcat before the process ends, where a developer looks for it.
            Log.Error(Tag, "The app ended with an exception: " + e);
            throw;
        }
    }

    private void RegisterServices()
    {
        CredentialStore.UsePlatformStore(new AndroidKeystoreCredentialStore(FilesDir!.AbsolutePath));
        // An unhandled Back (KeyCode.Back): Android's own back, on Android's UI thread. For a root
        // activity that moves the app to the background, as Android 12 and later do. Qualified:
        // inside an activity, Application is Android's (Activity.Application).
        global::HultUI.Application.SystemBack = () => RunOnUiThread(() => MoveTaskToBack(true));
        global::HultUI.Application.SystemBarsFollow = FollowTheme;
    }

    private static void DisableFocusHighlight(View view)
    {
        view.DefaultFocusHighlightEnabled = false;
        if (view is ViewGroup group)
            for (int i = 0; i < group.ChildCount; i++)
                if (group.GetChildAt(i) is { } child) DisableFocusHighlight(child);
    }
}
