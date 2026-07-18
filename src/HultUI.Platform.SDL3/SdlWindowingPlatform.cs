using System.Runtime.InteropServices;
using HultUI.Core.Platform;
using HultUI.Core.Styling;

namespace HultUI.Platform.SDL3;

/// <summary>
/// The real-display windowing platform: SDL3 windows with GL-backed Skia
/// surfaces, driven by SDL's event queue.
/// </summary>
public sealed class SdlWindowingPlatform : IWindowingPlatform
{
    public TimeProvider TimeProvider => TimeProvider.System;

    public IPlatformWindow CreateWindow(string title, int width, int height, WindowStyle style)
        => new SDL3Window(title, width, height, style);

    public bool PumpEvents(Action<IPlatformWindow>? onWindowClose)
        => SDL3Window.PollAllEvents(onWindowClose is null ? null : w => onWindowClose(w));

    /// <summary>
    /// Idle-frame wait. A plain <see cref="Thread.Sleep(int)"/> does not process
    /// Win32 <em>sent</em> messages, so a UIA query that reaches the window as a
    /// sent message (e.g. via the accessibility host provider / MSAA bridge) would
    /// stall for the whole interval — making assistive technologies very slow to
    /// walk the tree. On Windows, wait in a message-aware way so an incoming
    /// message wakes us immediately while a truly idle app still parks the CPU.
    /// </summary>
    public void WaitForEvents(int timeoutMs)
    {
        if (OperatingSystem.IsWindows())
            MsgWaitForMultipleObjectsEx(0, IntPtr.Zero, (uint)timeoutMs,
                0x04FF /* QS_ALLINPUT */, 0x0004 /* MWMO_INPUTAVAILABLE */);
        else
            Thread.Sleep(timeoutMs);
    }

    [DllImport("user32.dll")]
    private static extern uint MsgWaitForMultipleObjectsEx(uint nCount, IntPtr pHandles,
        uint dwMilliseconds, uint dwWakeMask, uint dwFlags);

    public void Dispose() => SDL3Window.ShutdownSDL();
}
