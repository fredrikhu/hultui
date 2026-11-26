using Android.App;
using Android.Views;
using Android.Views.Accessibility;
using HultUI.Core.Accessibility;
using Log = Android.Util.Log;

namespace HultUI.Platform.Accessibility.Android;

/// <summary>
/// Puts the TalkBack bridge on SDL's surface (docs/design/TALKBACK.md §4): the bridge is made now,
/// on HultUI's UI thread as the window is made, and the surface is found and given its provider,
/// its hover listener and the accessibility state on Android's main thread.
/// </summary>
internal static class AndroidAccessibility
{
    private const string Tag = "HultUI";

    /// <param name="activity">The activity SDL's surface is in.</param>
    /// <param name="host">The window's action router.</param>
    public static AndroidAccessibilityBridge Attach(Activity activity, IAccessibilityActionHost host)
    {
        var bridge = new AndroidAccessibilityBridge(host);
        activity.RunOnUiThread(() =>
        {
            if (FindSurfaceView(activity.Window?.DecorView) is not { } surface)
            {
                Log.Warn(Tag, "TalkBack: no SDL surface found; the app stays one opaque view to it");
                return;
            }
            var manager = activity.GetSystemService(global::Android.Content.Context.AccessibilityService) as AccessibilityManager;
            var accessible = new SurfaceAccessibility(surface, bridge, manager);
            surface.SetAccessibilityDelegate(accessible.Delegate);
            surface.SetOnHoverListener(accessible);
            surface.ImportantForAccessibility = ImportantForAccessibility.Yes;
            bridge.SetEventSink(accessible);
            if (manager is not null)
            {
                manager.AddAccessibilityStateChangeListener(accessible);
                bridge.SetEnabled(manager.IsEnabled);
            }
            Log.Info(Tag, $"TalkBack bridge on the surface; accessibility {(manager?.IsEnabled == true ? "on" : "off")}");
        });
        return bridge;
    }

    private static SurfaceView? FindSurfaceView(View? view)
    {
        if (view is SurfaceView surface) return surface;
        if (view is ViewGroup group)
            for (int i = 0; i < group.ChildCount; i++)
                if (FindSurfaceView(group.GetChildAt(i)) is { } found) return found;
        return null;
    }
}
