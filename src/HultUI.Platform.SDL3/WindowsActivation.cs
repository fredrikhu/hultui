using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static SDL.SDL3;

namespace HultUI.Platform.SDL3;

/// <summary>
/// Keeps the framework's own window-state changes from activating a window when SDL was told not
/// to activate one (<c>SDL_HINT_WINDOW_ACTIVATE_WHEN_SHOWN=0</c>).
/// </summary>
/// <remarks>
/// <para>SDL reads the hint only when it shows a window. Maximize and restore are
/// <c>ShowWindow(SW_MAXIMIZE)</c> and <c>ShowWindow(SW_RESTORE)</c>, which activate whatever the
/// hint says — there is no show command that maximizes without activating — and so are the
/// custom frame's <c>WM_SYSCOMMAND</c> maximize and restore, full screen's restore before it covers
/// the monitor and its maximize on the way out, and a maximized placement. So are a minimize of a
/// window that was raised (it asks for its own activation inside the call, even through
/// <c>SW_SHOWMINNOACTIVE</c>) and a raise, unless SDL's separate raise hint says no. A process
/// that set the hint because it shares a desktop someone is using (a probe, a test) had its window
/// ask for the foreground at each of them: ten times in one maximize, restore, full screen and
/// placement sequence (plan 17, round 4); minimize and raise since round 5.</para>
/// <para>So while the hint says no, those calls run under a CBT hook on the calling thread that
/// refuses <c>HCBT_ACTIVATE</c>. The window still maximizes, restores and covers the monitor; it is
/// only not activated. An app that leaves the hint unset — every real one — installs no hook and
/// behaves as before.</para>
/// </remarks>
internal static unsafe class WindowsActivation
{
    private const int WH_CBT = 5;
    private const int HCBT_ACTIVATE = 5;

    [ThreadStatic] private static int t_depth;
    [ThreadStatic] private static nint t_hook;
    private static int s_refused;

    /// <summary>Activations refused so far, on any thread. For tests.</summary>
    internal static int Refused => Volatile.Read(ref s_refused);

    /// <summary>
    /// Refuses every activation on this thread until the scope is disposed, on Windows and when
    /// SDL was told not to activate a window when it shows one; otherwise does nothing.
    /// </summary>
    internal static Scope RefuseIfUnwanted() =>
        OperatingSystem.IsWindows() && Unwanted ? Begin() : default;

    /// <summary>Whether SDL was told not to activate a window when it shows it.</summary>
    internal static bool Unwanted
    {
        get
        {
            bool activate = SDL_GetHintBoolean(SDL_HINT_WINDOW_ACTIVATE_WHEN_SHOWN, true);
            return !activate;
        }
    }

    [SupportedOSPlatform("windows")]
    private static Scope Begin()
    {
        if (t_depth++ == 0)
        {
            delegate* unmanaged[Stdcall]<int, nint, nint, nint> proc = &Proc;
            t_hook = SetWindowsHookExW(WH_CBT, (nint)proc, 0, GetCurrentThreadId());
        }
        return new Scope(active: true);
    }

    /// <summary>The span the hook stays in. Scopes nest; the outermost one removes the hook.</summary>
    internal readonly ref struct Scope
    {
        private readonly bool _active;

        public Scope(bool active) => _active = active;

        public void Dispose()
        {
            if (!_active || --t_depth != 0) return;
            if (t_hook != 0) UnhookWindowsHookEx(t_hook);
            t_hook = 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint Proc(int code, nint wParam, nint lParam)
    {
        if (code == HCBT_ACTIVATE)
        {
            Interlocked.Increment(ref s_refused);
            return 1;   // refused: the window is not activated
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    [DllImport("user32.dll")] private static extern nint SetWindowsHookExW(int id, nint proc, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
