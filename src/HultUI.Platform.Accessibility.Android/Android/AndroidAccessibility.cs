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
            if (surface.Parent is ViewGroup layout) layout.SetAccessibilityDelegate(new LayoutDelegate());
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

    // SDL's view for the keyboard's input, which it adds beside the surface as text input starts.
    private const string KeyboardViewClass = "org.libsdl.app.SDLDummyEdit";

    /// <summary>
    /// Keeps SDL's keyboard view from TalkBack. SDL focuses that view as text input starts, so the
    /// keyboard has a view to type into; it draws nothing and has no name. Its focus event drew
    /// TalkBack's focus onto it, a few milliseconds before the field's own, and TalkBack said
    /// "Unlabelled" as Aperture's Search page opened: some times, as the two events raced (Google
    /// TalkBack on a Galaxy S25). Its first event, on its way up through SDL's layout, is dropped
    /// here, and the view is hidden then. It keeps its input focus, which the keyboard needs.
    /// </summary>
    private sealed class LayoutDelegate : View.AccessibilityDelegate
    {
        public override bool OnRequestSendAccessibilityEvent(ViewGroup host, View child, AccessibilityEvent e)
        {
            if (child.Class.Name != KeyboardViewClass) return base.OnRequestSendAccessibilityEvent(host, child, e);
            if (child.ImportantForAccessibility != ImportantForAccessibility.NoHideDescendants)
            {
                child.SetAccessibilityDelegate(new HiddenDelegate());
                child.ImportantForAccessibility = ImportantForAccessibility.NoHideDescendants;
                Log.Info(Tag, "TalkBack: SDL's keyboard view hidden from it");
            }
            return false;
        }
    }

    /// <summary>A view TalkBack is not to know: it sends nothing, is not shown to TalkBack, and refuses its focus.</summary>
    private sealed class HiddenDelegate : View.AccessibilityDelegate
    {
        public override void SendAccessibilityEvent(View host, EventTypes eventType) { }

        public override void SendAccessibilityEventUnchecked(View host, AccessibilityEvent e) { }

        public override void OnInitializeAccessibilityNodeInfo(View host, AccessibilityNodeInfo info)
        {
            base.OnInitializeAccessibilityNodeInfo(host, info);
            info.VisibleToUser = false;
            info.ImportantForAccessibility = false;
        }

        public override bool PerformAccessibilityAction(View host, global::Android.Views.Accessibility.Action action, global::Android.OS.Bundle? args) =>
            action != global::Android.Views.Accessibility.Action.AccessibilityFocus && base.PerformAccessibilityAction(host, action, args);
    }
}
