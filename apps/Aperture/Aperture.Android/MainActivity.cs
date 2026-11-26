using Android.App;
using HultUI.Platform.Android;

// A Jellyfin server on the LAN is usually plain http:// (plan 20 §2, Network): Android 9 and later
// refuse cleartext unless the app allows it. The address is the user's to give, so it is allowed
// everywhere rather than for named hosts.
[assembly: Application(Label = "Aperture", Icon = "@mipmap/aperture", UsesCleartextTraffic = true)]

namespace Aperture.Android;

/// <summary>Aperture's activity: HultUIActivity runs <see cref="ApertureAndroid.Run"/> on SDL's
/// thread once the surface exists.</summary>
[Activity(Label = "Aperture", Icon = "@mipmap/aperture", MainLauncher = true, Exported = true,
    Theme = HultUIActivity.DefaultTheme, ConfigurationChanges = HultUIActivity.ConfigurationChanges)]
public class MainActivity : HultUIActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
#if DEBUG
        // `adb shell am start -n org.hultui.aperture/... -e demo 1`: a made-up library, no server.
        ApertureAndroid.Demo = Intent?.GetStringExtra("demo") == "1";
#endif
        base.OnCreate(savedInstanceState);
    }

    protected override void RunApp() => ApertureAndroid.Run();
}
