using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit;

namespace HultUI.UiTests;

/// <summary>
/// No window a test makes may become the foreground: the real-window tests run on a desktop
/// someone is using. <c>SDL_WINDOW_ACTIVATE_WHEN_SHOWN=0</c> in the environment was not enough on
/// its own (plan 17, 2026-10-11: a probe window took the foreground four times, and one with the
/// hint set in process still did twice). The Vulkan probe's windows have been kept out of the
/// foreground since b0ed204; this does the same for every other test that opens a window, in the
/// test process or in a sample it launches:
/// <list type="bullet">
/// <item>SDL is told in process, before the window exists, not to activate a window when it shows
/// or raises it (a launched sample is told through its environment); since then the framework's
/// own maximize, restore, full screen and placement refuse activation too;</item>
/// <item>a CBT hook on the test's thread makes every top-level window it creates
/// <c>WS_EX_NOACTIVATE</c> (neither a click nor the window Windows picks to activate when the
/// foreground one closes or minimizes can make it the foreground) and refuses every activation
/// on the thread. A sample's windows are another process's, out of the hook's reach: the watcher
/// gives each of them <c>WS_EX_NOACTIVATE</c> as soon as it exists;</item>
/// <item>a watcher looks every 5 ms and, if this process or a sample ever holds the foreground,
/// hands it straight back to the window that had it; the test then fails when the guard is
/// disposed.</item>
/// </list>
/// With <c>HULTUI_UITESTS_GUARD_LOG</c> set to a file path, each guard appends one line there when
/// it is disposed: the test, the activations its hook refused, the times it handed the foreground
/// back, and the sample windows it held. Windows only; elsewhere it does nothing. Dispose it on
/// the thread that began it.
/// </summary>
internal sealed unsafe class ForegroundGuard : IDisposable
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_NOACTIVATE = 0x08000000;

    private static nint _hook;
    private static int _refused;
    private static readonly string? LogPath = Environment.GetEnvironmentVariable("HULTUI_UITESTS_GUARD_LOG");

    private readonly Thread? _watcher;
    private readonly bool _withoutTheHint;
    private readonly string _test;
    private readonly int _refusedAtStart;
    private readonly List<uint> _samples = [];
    private readonly EnumWindowsProc _hold;
    private volatile bool _stopped;
    private int _taken, _held;
    private nint _previous;

    /// <summary>Starts guarding the calling thread's windows, before the test makes any.</summary>
    public static ForegroundGuard Begin([CallerFilePath] string file = "", [CallerMemberName] string member = "") =>
        new(hook: true, withoutTheHint: false, Name(file, member));

    /// <summary>
    /// The guard without the SDL hint: SDL and the framework act as they do in a real app, which
    /// leaves the hint unset, and only the hook and the watcher keep the windows back. For a test
    /// that shows what an app does without the hint; every other test uses <see cref="Begin"/>.
    /// The hint is back to "0" when the guard is disposed.
    /// </summary>
    public static ForegroundGuard BeginWithoutTheHint([CallerFilePath] string file = "", [CallerMemberName] string member = "") =>
        new(hook: true, withoutTheHint: true, Name(file, member));

    /// <summary>
    /// The guard for a test whose windows are a sample's, launched with <see cref="LaunchSample"/>:
    /// no hook on this thread, which makes none.
    /// </summary>
    public static ForegroundGuard ForSamples([CallerFilePath] string file = "", [CallerMemberName] string member = "") =>
        new(hook: false, withoutTheHint: false, Name(file, member));

    private static string Name(string file, string member) => $"{Path.GetFileNameWithoutExtension(file)}.{member}";

    private ForegroundGuard(bool hook, bool withoutTheHint, string test)
    {
        _withoutTheHint = withoutTheHint;
        _test = test;
        _refusedAtStart = Refused;
        _hold = HoldSampleWindow;
        // Override: an SDL_WINDOW_ACTIVATE_WHEN_SHOWN in the environment wins over a hint set at
        // normal priority, and "1" is what SDL assumes when nothing is set.
        SDL.SDL3.SDL_SetHintWithPriority(SDL.SDL3.SDL_HINT_WINDOW_ACTIVATE_WHEN_SHOWN,
            withoutTheHint ? "1" : "0", SDL.SDL_HintPriority.SDL_HINT_OVERRIDE);
        // A raise asks Windows for the foreground (SetForegroundWindow) unless told not to.
        SDL.SDL3.SDL_SetHintWithPriority(SDL.SDL3.SDL_HINT_WINDOW_ACTIVATE_WHEN_RAISED, "0",
            SDL.SDL_HintPriority.SDL_HINT_OVERRIDE);
        if (!OperatingSystem.IsWindows()) return;
        _previous = GetForegroundWindow();
        if (hook)
        {
            delegate* unmanaged[Stdcall]<int, nint, nint, nint> proc = &Proc;
            _hook = SetWindowsHookExW(5 /* WH_CBT */, (nint)proc, 0, GetCurrentThreadId());
            Assert.True(_hook != 0, "the CBT hook that keeps the test's windows out of the foreground failed");
        }
        _watcher = new Thread(Watch) { IsBackground = true, Name = "foreground guard" };
        _watcher.Start();
    }

    /// <summary>Activations the hook refused (a window asking to come forward is not a failure).</summary>
    public static int Refused => Volatile.Read(ref _refused);

    /// <summary>
    /// Launches a sample told not to activate its windows, and watches it: its windows are held
    /// back like the test's own, and it may not take the foreground either.
    /// </summary>
    public FlaUI.Core.Application LaunchSample(string exe)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) };
        start.Environment["SDL_WINDOW_ACTIVATE_WHEN_SHOWN"] = "0";
        start.Environment["SDL_WINDOW_ACTIVATE_WHEN_RAISED"] = "0";
        var app = FlaUI.Core.Application.Launch(start);
        lock (_samples) _samples.Add((uint)app.ProcessId);
        return app;
    }

    private bool IsSample(uint process)
    {
        lock (_samples) return _samples.Contains(process);
    }

    private void Watch()
    {
        uint self = (uint)Environment.ProcessId;
        nint seen = 0;
        bool seenIsOurs = false;
        while (!_stopped)
        {
            bool samples;
            lock (_samples) samples = _samples.Count > 0;
            if (samples) EnumWindows(_hold, 0);

            nint foreground = GetForegroundWindow();
            if (foreground != 0)
            {
                uint thread = GetWindowThreadProcessId(foreground, out uint owner);
                // Ours: this process, a sample, or a process this one started — WebView2's browser
                // process owns the DevTools window, for one. Asked once per foreground window.
                if (foreground != seen)
                {
                    seen = foreground;
                    seenIsOurs = owner == self || IsSample(owner) || IsDescendant(owner, self);
                }
                if (!seenIsOurs) _previous = foreground;
                else
                {
                    Interlocked.Increment(ref _taken);
                    if (_previous != 0) HandBack(owner == self ? 0 : thread);
                }
            }
            Thread.Sleep(5);
        }
    }

    /// <summary>Gives the foreground back to the window that had it. When another process holds
    /// it (<paramref name="holder"/> is its thread), this one may not hand it on until it shares
    /// that thread's input state.</summary>
    private void HandBack(uint holder)
    {
        if (holder == 0)
        {
            SetForegroundWindow(_previous);
            return;
        }
        uint me = GetCurrentThreadId();
        if (!AttachThreadInput(me, holder, true)) return;
        SetForegroundWindow(_previous);
        AttachThreadInput(me, holder, false);
    }

    /// <summary>Whether <paramref name="ancestor"/> started the process, or started one that did.</summary>
    private static bool IsDescendant(uint process, uint ancestor)
    {
        for (int depth = 0; depth < 8 && process > 4; depth++)
        {
            uint parent = ParentOf(process);
            if (parent == ancestor) return true;
            if (parent == 0 || parent == process) return false;
            process = parent;
        }
        return false;
    }

    private static uint ParentOf(uint process)
    {
        nint handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, process);
        if (handle == 0) return 0;
        try
        {
            var info = default(ProcessBasicInformation);
            return NtQueryInformationProcess(handle, 0, ref info, sizeof(ProcessBasicInformation), out _) == 0
                ? (uint)info.InheritedFromUniqueProcessId : 0;
        }
        finally { CloseHandle(handle); }
    }

    /// <summary>Gives a top-level window of a sample <c>WS_EX_NOACTIVATE</c>, once.</summary>
    private bool HoldSampleWindow(nint hwnd, nint data)
    {
        GetWindowThreadProcessId(hwnd, out uint owner);
        if (!IsSample(owner)) return true;
        long style = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        if ((style & WS_EX_NOACTIVATE) == 0)
        {
            SetWindowLongPtrW(hwnd, GWL_EXSTYLE, (nint)(style | WS_EX_NOACTIVATE));
            Interlocked.Increment(ref _held);
        }
        return true;
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
        if (LogPath is { Length: > 0 } log)
            File.AppendAllText(log, $"{_test}\trefused={Refused - _refusedAtStart}\thandedBack={_taken}\tsampleWindowsHeld={_held}{Environment.NewLine}");
        Assert.True(_taken == 0, $"a test window took the foreground ({_taken} times seen); it was handed back");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint Proc(int code, nint wParam, nint lParam)
    {
        if (code == 3 /* HCBT_CREATEWND */)
        {
            // CBT_CREATEWND { CREATESTRUCT* lpcs; HWND hwndInsertAfter; }; dwExStyle is its last field.
            var create = *(CreateStruct**)lParam;
            if (create->hwndParent == 0) create->dwExStyle |= (uint)WS_EX_NOACTIVATE;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public nint ExitStatus, PebBaseAddress, AffinityMask, BasePriority, UniqueProcessId, InheritedFromUniqueProcessId;
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint data);

    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(nint process, int infoClass, ref ProcessBasicInformation info, int length, out int returned);

    [DllImport("user32.dll")] private static extern nint SetWindowsHookExW(int id, nint proc, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool on);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, nint data);
    [DllImport("user32.dll")] private static extern long GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
}
