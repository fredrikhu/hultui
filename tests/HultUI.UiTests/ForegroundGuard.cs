using System.Runtime.InteropServices;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// No window an in-process test makes may become the foreground: the real-window tests run on a
/// desktop someone is using. <c>SDL_WINDOW_ACTIVATE_WHEN_SHOWN=0</c> in the environment was not
/// enough on its own (plan 17, 2026-10-11: a probe window took the foreground four times, and one
/// with the hint set in process still did twice). The Vulkan probe's windows have been kept out
/// of the foreground since b0ed204; this does the same for the tests that open a GL window in the
/// test process:
/// <list type="bullet">
/// <item>SDL is told in process, before the window exists;</item>
/// <item>a CBT hook on the test's thread makes every top-level window it creates
/// <c>WS_EX_NOACTIVATE</c> (neither a click nor the window Windows picks to activate when the
/// foreground one closes or minimizes can make it the foreground) and refuses every activation
/// on the thread;</item>
/// <item>a watcher looks every 5 ms and, if this process ever holds the foreground, hands it
/// straight back to the window that had it; the test then fails when the guard is disposed.</item>
/// </list>
/// Windows only; elsewhere it does nothing. Dispose it on the thread that began it.
/// </summary>
internal sealed unsafe class ForegroundGuard : IDisposable
{
    private static nint _hook;
    private static int _refused;

    private readonly Thread? _watcher;
    private readonly bool _withoutTheHint;
    private volatile bool _stopped;
    private int _taken;
    private nint _previous;

    /// <summary>Starts guarding the calling thread's windows, before the test makes any.</summary>
    public static ForegroundGuard Begin() => new(withoutTheHint: false);

    /// <summary>
    /// The guard without the SDL hint: SDL and the framework act as they do in a real app, which
    /// leaves the hint unset, and only the hook and the watcher keep the windows back. For a test
    /// that shows what an app does without the hint; every other test uses <see cref="Begin"/>.
    /// The hint is back to "0" when the guard is disposed.
    /// </summary>
    public static ForegroundGuard BeginWithoutTheHint() => new(withoutTheHint: true);

    private ForegroundGuard(bool withoutTheHint)
    {
        _withoutTheHint = withoutTheHint;
        // Override: an SDL_WINDOW_ACTIVATE_WHEN_SHOWN in the environment wins over a hint set at
        // normal priority, and "1" is what SDL assumes when nothing is set.
        SDL.SDL3.SDL_SetHintWithPriority(SDL.SDL3.SDL_HINT_WINDOW_ACTIVATE_WHEN_SHOWN,
            withoutTheHint ? "1" : "0", SDL.SDL_HintPriority.SDL_HINT_OVERRIDE);
        if (!OperatingSystem.IsWindows()) return;
        _previous = GetForegroundWindow();
        delegate* unmanaged[Stdcall]<int, nint, nint, nint> proc = &Proc;
        _hook = SetWindowsHookExW(5 /* WH_CBT */, (nint)proc, 0, GetCurrentThreadId());
        Assert.True(_hook != 0, "the CBT hook that keeps the test's windows out of the foreground failed");
        _watcher = new Thread(Watch) { IsBackground = true, Name = "foreground guard" };
        _watcher.Start();
    }

    /// <summary>Activations the hook refused (a window asking to come forward is not a failure).</summary>
    public static int Refused => Volatile.Read(ref _refused);

    private void Watch()
    {
        uint self = (uint)Environment.ProcessId;
        while (!_stopped)
        {
            nint foreground = GetForegroundWindow();
            if (foreground != 0)
            {
                GetWindowThreadProcessId(foreground, out uint owner);
                if (owner != self) _previous = foreground;
                else
                {
                    Interlocked.Increment(ref _taken);
                    if (_previous != 0) SetForegroundWindow(_previous);
                }
            }
            Thread.Sleep(5);
        }
    }

    public void Dispose()
    {
        if (_withoutTheHint)
            SDL.SDL3.SDL_SetHintWithPriority(SDL.SDL3.SDL_HINT_WINDOW_ACTIVATE_WHEN_SHOWN, "0",
                SDL.SDL_HintPriority.SDL_HINT_OVERRIDE);
        if (!OperatingSystem.IsWindows()) return;
        _stopped = true;
        _watcher?.Join();
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
        Assert.True(_taken == 0, $"a test window took the foreground ({_taken} times seen); it was handed back");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint Proc(int code, nint wParam, nint lParam)
    {
        if (code == 3 /* HCBT_CREATEWND */)
        {
            // CBT_CREATEWND { CREATESTRUCT* lpcs; HWND hwndInsertAfter; }; dwExStyle is its last field.
            var create = *(CreateStruct**)lParam;
            if (create->hwndParent == 0) create->dwExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */;
        }
        else if (code == 5 /* HCBT_ACTIVATE */)
        {
            Interlocked.Increment(ref _refused);
            return 1;
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CreateStruct
    {
        public nint lpCreateParams, hInstance, hMenu, hwndParent;
        public int cy, cx, y, x;
        public int style;
        public nint lpszName, lpszClass;
        public uint dwExStyle;
    }

    [DllImport("user32.dll")] private static extern nint SetWindowsHookExW(int id, nint proc, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
}
