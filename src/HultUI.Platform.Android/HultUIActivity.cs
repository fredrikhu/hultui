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

    /// <summary>The <c>AppContext</c> data the Java VM's pointer goes in, as a long. HultUI.Media.Mpv
    /// reads the same name; neither assembly references the other.</summary>
    internal const string JavaVmSlot = "HultUI.Android.JavaVM";

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
        // The process's Java VM, for native code that needs it and cannot ask for it: FFmpeg's
        // MediaCodec decoders inside libmpv (HultUI.Media.Mpv's Mpv.GiveJavaVm). Android 12 and
        // later hand it to anyone through JNI_GetCreatedJavaVMs; Android 10 and 11 keep that in
        // libraries an app cannot load, and without the VM the video decoded through a copy.
        AppContext.SetData(JavaVmSlot, (long)Java.Interop.JniEnvironment.Runtime.InvocationPointer);
        // A device navigated by a D-pad with no touch screen (a TV, a set-top box): its windows' arrows
        // move the focus (Window.ArrowKeysMoveFocus, docs/design/DPAD-FOCUS.md). Before the app runs,
        // which makes its windows on SDL's thread once the surface exists.
        if (Resources?.Configuration is { } config)
            HultUI.Window.DefaultArrowKeysMoveFocus = config.Navigation == global::Android.Content.Res.Navigation.Dpad
                && config.Touchscreen == global::Android.Content.Res.TouchScreenType.Notouch;
        base.OnCreate(savedInstanceState);
        // Android 8+ draws a translucent highlight over a focused view that is not in touch
        // mode. SDLSurface keeps the focus, so after text typed as key events and Back the whole
        // app sat under a 12 % white wash (spikes/AndroidHello, finding 7).
        if (Window?.DecorView is { } decor) DisableFocusHighlight(decor);
        // The safe area HultUI lays out in: the bars and the cutout, as they arrive and change.
        FindViewById(global::Android.Resource.Id.Content)?.SetOnApplyWindowInsetsListener(new ContentInsetsListener());
        // The keyboard's area is HultUI's to keep clear (Window.KeyboardInset). Under the default,
        // adjust-pan, Android moved the whole window down by the status bar when the keyboard came
        // up, to reveal SDL's hidden edit view at the window's corner: drawn and touched 49 DIP
        // lower than HultUI laid out (measured on the emulator). On an edge-to-edge window
        // adjust-resize neither resizes nor pans; it only reports the inset.
        Window?.SetSoftInputMode(SoftInput.AdjustResize);
        FollowPeakRefreshRate();
        // Focus moved to another kind of field with the keyboard up (docs/design/SOFT-KEYBOARD.md
        // §8): SDL sends Java no new type while its keyboard shows. SDL's own show task stores
        // it, and the keyboard reads it when its input restarts, which is posted after the task.
        global::HultUI.Platform.SDL3.SDL3Window.TextInputSwitched = (inputType, area) =>
        {
            ShowTextInput(inputType, area.x, area.y, area.w, area.h);
            RunOnUiThread(() =>
            {
                if (CurrentFocus is { } editor && GetSystemService(InputMethodService) is global::Android.Views.InputMethods.InputMethodManager keyboard)
                    keyboard.RestartInput(editor);
            });
        };
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
        // The activity's display from Android 11; before it, the window manager's.
        var display = OperatingSystem.IsAndroidVersionAtLeast(30) ? Display : WindowManager?.DefaultDisplay;
        if (display is null) return;
        var current = display.GetMode();
        float peak = 0f;
        foreach (var mode in display.GetSupportedModes() ?? [])
            if (current is null || (mode.PhysicalWidth == current.PhysicalWidth && mode.PhysicalHeight == current.PhysicalHeight))
                peak = Math.Max(peak, mode.RefreshRate);
        if (peak <= 0f) return;
        global::HultUI.Platform.SDL3.SDL3Window.PlatformRefreshRate = peak;
        _peakRate = peak;
        if (FindSurfaceView(Window?.DecorView) is { } surfaceView)
            surfaceView.Holder?.AddCallback(_frameRateVote = new FrameRateVote(peak));
        Log.Info(Tag, $"display peak refresh rate {peak} Hz (SDL saw {display.RefreshRate} Hz)");
        // A film over most of a window: the display matched to its frame rate while it plays.
        global::HultUI.Window.ContentFrameRateChanged = fps => RunOnUiThread(() => MatchFrameRate(fps));
    }

    private float _peakRate;
    private FrameRateVote? _frameRateVote;

    /// <summary>
    /// Matches the display to a film's frame rate while it plays, and lets it go after
    /// (docs/design/TV-VIDEO.md §5). Everywhere from Android 11, a vote for the film's rate as a fixed
    /// source in place of the peak: a phone picks a rate it is a multiple of where it can do so
    /// seamlessly (120 Hz for 24 fps). On a TV (no touch screen, a D-pad), the display mode outright:
    /// the panel's resolution at the highest refresh rate that is a whole multiple of the film's, which
    /// Android 11 switches to (an HDMI resynchronisation, a second or two of black) where a vote would
    /// not. A Shield ran its 4K panel at 59.94 Hz throughout, and a 23.976 fps film juddered 3:2.
    /// </summary>
    private void MatchFrameRate(double? fps)
    {
        if (_frameRateVote is { } vote)
        {
            vote.Rate = fps is { } f ? (float)f : _peakRate;
            vote.FixedSource = fps is not null;
            vote.Revote();
        }
        if (!global::HultUI.Window.DefaultArrowKeysMoveFocus || Window is not { } window) return;
        var display = OperatingSystem.IsAndroidVersionAtLeast(30) ? Display : WindowManager?.DefaultDisplay;
        if (display is null || window.Attributes is not { } attributes) return;
        int modeId = 0;   // none: the system's own
        if (fps is { } rate && display.GetMode() is { } current)
        {
            var modes = (display.GetSupportedModes() ?? [])
                .Where(m => m.PhysicalWidth == current.PhysicalWidth && m.PhysicalHeight == current.PhysicalHeight)
                .ToList();
            float? best = global::HultUI.DisplayRefresh.ForVideo(modes.Select(m => m.RefreshRate), rate);
            if (best is { } fits) modeId = modes.First(m => m.RefreshRate == fits).ModeId;
            Log.Info(Tag, $"film at {rate:0.###} fps: display mode {modeId} ({best?.ToString("0.###") ?? "none fits"} Hz at {current.PhysicalWidth}x{current.PhysicalHeight})");
        }
        else Log.Info(Tag, "film gone: the display's own mode");
        if (attributes.PreferredDisplayModeId == modeId) return;
        attributes.PreferredDisplayModeId = modeId;
        window.Attributes = attributes;
    }

    private static SurfaceView? FindSurfaceView(View? view)
    {
        if (view is SurfaceView surface) return surface;
        if (view is ViewGroup group)
            for (int i = 0; i < group.ChildCount; i++)
                if (FindSurfaceView(group.GetChildAt(i)) is { } found) return found;
        return null;
    }

    /// <summary>Votes for the display's peak rate each time SDL's surface is made or changes, or for a
    /// film's rate as a fixed source while one plays (<see cref="MatchFrameRate"/>).</summary>
    private sealed class FrameRateVote(float rate) : Java.Lang.Object, ISurfaceHolderCallback
    {
        public float Rate = rate;
        public bool FixedSource;
        private ISurfaceHolder? _holder;

        public void SurfaceCreated(ISurfaceHolder holder) => Vote(_holder = holder);
        public void SurfaceChanged(ISurfaceHolder holder, global::Android.Graphics.Format format, int width, int height) => Vote(_holder = holder);
        public void SurfaceDestroyed(ISurfaceHolder holder) => _holder = null;

        /// <summary>Votes again, for a rate that changed while the surface lives.</summary>
        public void Revote()
        {
            if (_holder is { } holder) Vote(holder);
        }

        private void Vote(ISurfaceHolder holder)
        {
            // From Android 11; before it, the display keeps the rate it runs at.
            if (OperatingSystem.IsAndroidVersionAtLeast(30) && holder.Surface is { IsValid: true } surface)
                surface.SetFrameRate(Rate, (int)(FixedSource ? SurfaceFrameRateCompatibility.FixedSource : SurfaceFrameRateCompatibility.Default));
        }
    }

    /// <summary>
    /// Hands SDL3Window the insets content should clear (docs/design/ANDROID-PLATFORM.md §3):
    /// the system bars and the display cutout. SDL's own safe area also takes out the gesture
    /// zones, which Android keeps for edge swipes, not content. And the soft keyboard's height,
    /// the <c>ime()</c> inset, for <c>Window.KeyboardInset</c> (docs/design/SOFT-KEYBOARD.md §3):
    /// an edge-to-edge window is no longer resized for the keyboard. With no insets animation
    /// callback set, Android hands this listener the keyboard's end height as the slide starts.
    /// Then applies the insets as the view would have, so SDL's surface below still gets them.
    /// </summary>
    private sealed class ContentInsetsListener : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View view, WindowInsets insets)
        {
            global::HultUI.Platform.SDL3.SDL3Window.SetContentInsets(OperatingSystem.IsAndroidVersionAtLeast(30)
                ? Typed(insets)
                : Legacy(insets));
            return view.OnApplyWindowInsets(insets) ?? insets;
        }

        [System.Runtime.Versioning.SupportedOSPlatform("android30.0")]
        private static global::HultUI.Platform.SDL3.SDL3Window.PixelInsets Typed(WindowInsets insets)
        {
            var bars = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
            int keyboard = insets.IsVisible(WindowInsets.Type.Ime()) ? insets.GetInsets(WindowInsets.Type.Ime()).Bottom : 0;
            return new(bars.Top, bars.Right, bars.Bottom, bars.Left, keyboard);
        }

        /// <summary>
        /// Before Android 11 there are no typed insets: the system window's, which an app that is
        /// not drawn under the bars gets as 0 (Android lays the window out between them), and the
        /// cutout's. The keyboard has no inset of its own: under adjust-resize Android makes the
        /// window shorter for it, and HultUI lays out in what is left, so it is 0 here and a
        /// window drawn under the keyboard would see it in the bottom inset.
        /// </summary>
        private static global::HultUI.Platform.SDL3.SDL3Window.PixelInsets Legacy(WindowInsets insets)
        {
            var cutout = insets.DisplayCutout;
#pragma warning disable CA1422 // obsolete from Android 11, which takes the typed path above
            int top = Math.Max(insets.SystemWindowInsetTop, cutout?.SafeInsetTop ?? 0);
            int right = Math.Max(insets.SystemWindowInsetRight, cutout?.SafeInsetRight ?? 0);
            int bottom = Math.Max(insets.SystemWindowInsetBottom, cutout?.SafeInsetBottom ?? 0);
            int left = Math.Max(insets.SystemWindowInsetLeft, cutout?.SafeInsetLeft ?? 0);
#pragma warning restore CA1422
            return new(top, right, bottom, left, 0);
        }
    }

    /// <summary>The status and navigation bars' icons, dark over a light theme and light over a
    /// dark one: Android's defaults for this theme are white, which vanished over HultUI's
    /// white background.</summary>
    private void FollowTheme(global::HultUI.Core.Styling.Theme theme)
    {
        var bg = theme.Background;
        bool light = 0.2126 * bg.R + 0.7152 * bg.G + 0.0722 * bg.B > 140;
        RunOnUiThread(() =>
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                const int Mask = (int)(WindowInsetsControllerAppearance.LightStatusBars | WindowInsetsControllerAppearance.LightNavigationBars);
                Window?.InsetsController?.SetSystemBarsAppearance(light ? Mask : 0, Mask);
                return;
            }
            // Before Android 11: the decor view's system UI flags (light status bar from 6, light
            // navigation bar from 8), and the bars in the app's background. The window is laid out
            // between them, not under them, and Android's own white navigation bar sat under a dark
            // app (a Galaxy Tab S4, Android 10).
            if (Window is not { DecorView: { } decor } window) return;
            var barColor = new global::Android.Graphics.Color(bg.R, bg.G, bg.B);
            window.SetStatusBarColor(barColor);
            window.SetNavigationBarColor(barColor);
#pragma warning disable CA1422 // obsolete from Android 11, which takes the path above
            const SystemUiFlags Light = SystemUiFlags.LightStatusBar | SystemUiFlags.LightNavigationBar;
            var flags = (SystemUiFlags)(int)decor.SystemUiVisibility;
            decor.SystemUiVisibility = (StatusBarVisibility)(int)(light ? flags | Light : flags & ~Light);
#pragma warning restore CA1422
        });
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
        // TalkBack (docs/design/TALKBACK.md): each window's tree as virtual views on SDL's surface,
        // its actions through the window's router onto the UI thread.
        global::HultUI.AccessibilityBridgeFactory.Platform = window =>
        {
            var router = new global::HultUI.AccessibilityActionRouter(window);
            window.SetActionRouter(router);
            return global::HultUI.Platform.Accessibility.Android.AndroidAccessibility.Attach(this, router);
        };
    }

    private static void DisableFocusHighlight(View view)
    {
        view.DefaultFocusHighlightEnabled = false;
        if (view is ViewGroup group)
            for (int i = 0; i < group.ChildCount; i++)
                if (group.GetChildAt(i) is { } child) DisableFocusHighlight(child);
    }
}
