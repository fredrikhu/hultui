using Android.App;
using HultUI.Platform.Android;

namespace AndroidHello;

/// <summary>
/// The spike's activity, on HultUI.Platform.Android's <see cref="HultUIActivity"/>, which does
/// what this file used to: SDL's entry, the focus highlight, the platform's services
/// (docs/design/ANDROID-PLATFORM.md §2). What is left is reading the spike's experiment switches.
/// </summary>
[Activity(Label = "HultUI spike", MainLauncher = true, Exported = true,
    Theme = HultUIActivity.DefaultTheme, ConfigurationChanges = HultUIActivity.ConfigurationChanges)]
public class MainActivity : HultUIActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Spike switches, from `adb shell am start ... -e renderer software -e rebind 0`.
        HelloApp.Renderer = Intent?.GetStringExtra("renderer");
        HelloApp.Rebind = Intent?.GetStringExtra("rebind") != "0";
        HelloApp.RedrawAfterMs = int.TryParse(Intent?.GetStringExtra("redraw"), out int ms) ? ms : 0;
        HelloApp.BlockOnPause = Intent?.GetStringExtra("block") != "0";
        HelloApp.Ticker = Intent?.GetStringExtra("ticker") != "0";
        HelloApp.Sample = Intent?.GetStringExtra("sample");
        // The video sample (plan 20 phase 5): a file to play, and mpv's switches as environment
        // variables, "HULTUI_MPV_HWDEC=mediacodec-copy;HULTUI_MPV_LOG=info".
        HelloApp.VideoFile = Intent?.GetStringExtra("file");
        foreach (var pair in (Intent?.GetStringExtra("env") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            if (pair.Split('=', 2) is [var key, var value]) Environment.SetEnvironmentVariable(key, value);
        base.OnCreate(savedInstanceState);
    }

    /// <summary>Runs on SDL's thread once the surface exists.</summary>
    protected override void RunApp() => HelloApp.Run();
}
