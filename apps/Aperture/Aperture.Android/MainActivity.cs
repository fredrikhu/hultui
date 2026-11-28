using Android.App;
using Android.Content;
using HultUI.Platform.Android;

// A Jellyfin server on the LAN is usually plain http:// (plan 20 §2, Network): Android 9 and later
// refuse cleartext unless the app allows it. The address is the user's to give, so it is allowed
// everywhere rather than for named hosts. The banner is what an Android TV's home screen shows for
// the app (320×180, drawn from the icon and the wordmark: Resources/drawable-xhdpi/banner.png).
[assembly: Application(Label = "Aperture", Icon = "@mipmap/aperture", Banner = "@drawable/banner",
    UsesCleartextTraffic = true)]

// One APK for phones, tablets and TVs: a TV has no touch screen, and Google Play and the TV's own
// installer take an app that asks for one as not for TVs; Leanback is there on a TV and nowhere else.
[assembly: UsesFeature("android.hardware.touchscreen", Required = false)]
[assembly: UsesFeature("android.software.leanback", Required = false)]

namespace Aperture.Android;

/// <summary>Aperture's activity: HultUIActivity runs <see cref="ApertureAndroid.Run"/> on SDL's
/// thread once the surface exists. The Leanback launcher filter puts it on an Android TV's home
/// screen, which lists only apps that have one (the Shield's is leanback-only).</summary>
[Activity(Label = "Aperture", Icon = "@mipmap/aperture", MainLauncher = true, Exported = true,
    Theme = HultUIActivity.DefaultTheme, ConfigurationChanges = HultUIActivity.ConfigurationChanges)]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLeanbackLauncher])]
public class MainActivity : HultUIActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
#if DEBUG
        // `adb shell am start -n org.hultui.aperture/... -e demo 1`: a made-up library, no server.
        ApertureAndroid.Demo = Intent?.GetStringExtra("demo") == "1";
        // `-e env "K=V;K=V"`: environment variables for the run, as the spike takes them, for the
        // diagnostics HultUI reads there (HULTUI_MPV_LOG, HULTUI_FRAMELOG). CoreCLR on Android reads
        // no debug.mono.env. Quote the value for the device's shell, or its `;` ends the command.
        string env = Intent?.GetStringExtra("env") ?? "";
        foreach (var pair in env.Split(';', StringSplitOptions.RemoveEmptyEntries))
            if (pair.Split('=', 2) is [var key, var value]) Environment.SetEnvironmentVariable(key, value);
        // What those diagnostics write to stderr, which nothing reads on Android, into logcat.
        if (env.Length > 0) Console.SetError(new LogcatWriter());
#endif
        base.OnCreate(savedInstanceState);
    }

    protected override void RunApp() => ApertureAndroid.Run();
}
