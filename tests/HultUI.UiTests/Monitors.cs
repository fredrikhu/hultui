using System.Runtime.InteropServices;
using HultUI.Core.Primitives;

namespace HultUI.UiTests;

/// <summary>
/// The monitors as the placement tests see them, in the physical pixels SDL places windows in.
/// SDL makes the process per-monitor aware only when its video starts, and a test that reads the
/// work areas before it opens a window comes first: on a monitor above 100 % an unaware thread
/// sees that monitor's work area scaled down, so the first test of a run aimed at the wrong
/// rectangle. The calling thread is made per-monitor aware (V2, as SDL makes the process) before
/// it looks, and stays so: windows it makes later are per-monitor aware anyway.
/// </summary>
internal static class Monitors
{
    private static readonly nint PerMonitorAwareV2 = -4;   // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

    /// <summary>Every monitor's work area, in physical pixels.</summary>
    public static List<Rect> WorkAreas()
    {
        SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        var areas = new List<Rect>();
        EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfoW(monitor, ref info))
                areas.Add(new Rect(info.rcWork.Left, info.rcWork.Top,
                    info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top));
            return true;
        }, 0);
        return areas;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint clip, nint data);

    [DllImport("user32")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32")] private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc proc, nint data);
    [DllImport("user32", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfoW(nint monitor, ref MONITORINFO info);
}
