using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;
using Log = Android.Util.Log;

namespace AndroidHello;

/// <summary>
/// Spike instrumentation: logs the app-lifecycle and window events SDL sends (HultUI ignores the
/// lifecycle ones today) and, on demand, each SDL window's flags. Logcat tag: HultUISpike.
/// </summary>
internal static unsafe class SdlProbe
{
    public static void Install() => SDL3.SDL_AddEventWatch(&Watch, IntPtr.Zero);

    /// <summary>Set by the app: runs on HultUI's thread after the app returns to the foreground.</summary>
    public static Action? OnForeground;

    /// <summary>
    /// The experiment: unbind the GL context and bind it again, so SDL binds it to the EGL
    /// surface Android made on resume. Binding the same window and context alone is a no-op
    /// in SDL (already current), which leaves the context on the destroyed surface.
    /// </summary>
    public static void RebindGL()
    {
        var window = SDL3.SDL_GL_GetCurrentWindow();
        var context = SDL3.SDL_GL_GetCurrentContext();
        if (window == null || context == null) { Log.Info(HelloApp.Tag, "rebind: no current GL window/context"); return; }
        bool unbound = SDL3.SDL_GL_MakeCurrent(window, null);
        bool bound = SDL3.SDL_GL_MakeCurrent(window, context);
        Log.Info(HelloApp.Tag, $"rebind: unbind {unbound}, bind {bound} {(bound ? "" : SDL3.SDL_GetError())}");
    }

    public static void LogWindowFlags(string when)
    {
        for (uint id = 1; id < 8; id++)
        {
            var window = SDL3.SDL_GetWindowFromID((SDL_WindowID)id);
            if (window == null) continue;
            Log.Info(HelloApp.Tag, $"{when}: window {id} flags {SDL3.SDL_GetWindowFlags(window)}");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static SDLBool Watch(IntPtr userdata, SDL_Event* evt)
    {
        uint type = evt->type;
        // App events (TERMINATING .. DID_ENTER_FOREGROUND, LOCALE, SYSTEM_THEME) and window events.
        bool app = type is >= (uint)SDL_EventType.SDL_EVENT_TERMINATING and <= (uint)SDL_EventType.SDL_EVENT_SYSTEM_THEME_CHANGED;
        bool window = type is >= (uint)SDL_EventType.SDL_EVENT_WINDOW_FIRST and <= (uint)SDL_EventType.SDL_EVENT_WINDOW_LAST;
        if (app || window)
            Log.Info(HelloApp.Tag, $"sdl event {(SDL_EventType)type}");
        if (type == (uint)SDL_EventType.SDL_EVENT_DID_ENTER_FOREGROUND && OnForeground is { } act)
            HultUI.Application.InvokeOnMainThread(act);
        return true;
    }
}
