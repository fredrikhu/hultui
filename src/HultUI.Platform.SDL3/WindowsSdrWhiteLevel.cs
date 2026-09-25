using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.DevTools;

namespace HultUI.Platform.SDL3;

/// <summary>
/// How bright Windows is currently drawing SDR white, in nits.
///
/// <para>This exists because scRGB defines 1.0 as <b>80 nits</b> and Windows does not draw
/// SDR white at 80 nits. In HDR mode it composites SDR content at whatever the "SDR content
/// brightness" slider says — commonly 200 to 480 — so an app that writes a correct linear
/// 1.0 for white lands far below every other window on the desktop and looks grey. That is
/// not a rendering bug and no capture can show it: the swapchain image is right, and the
/// compositor is doing what it was told.</para>
///
/// <para>SDL exposes an HDR-enabled flag but this binding has no SDR white point, and
/// Vulkan has no notion of one at all, so it comes from the display configuration API.
/// Returns 80 when it cannot be had, which is scRGB's own reference and therefore the value
/// that changes nothing.</para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsSdrWhiteLevel
{
    /// <summary>scRGB's reference white, and the answer when the real one is unavailable.</summary>
    public const float ScRgbReferenceNits = 80f;

    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    // 11, from the DISPLAYCONFIG_DEVICE_INFO_TYPE enum. Worth stating plainly because a
    // wrong value here is not a compile error and not a crash: DisplayConfigGetDeviceInfo
    // returns 87, ERROR_INVALID_PARAMETER, and a fallback quietly reports 80 nits — which
    // is a plausible-looking number and the one value that makes the caller do nothing.
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL = 11;
    private const int ERROR_SUCCESS = 0;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>
    /// The SDR white level of the display showing that window, in nits. Falls back to
    /// <see cref="ScRgbReferenceNits"/>, which makes the caller's scale exactly 1.
    /// </summary>
    /// <param name="hwnd">The window whose monitor to ask about. Zero asks about whichever
    /// active path answers first, which is only right when every display agrees.</param>
    public static float Query(nint hwnd = 0)
    {
        try
        {
            return QueryCore(MonitorDeviceName(hwnd));
        }
        catch (DllNotFoundException) { return ScRgbReferenceNits; }
        catch (EntryPointNotFoundException) { return ScRgbReferenceNits; }
    }

    /// <summary>The GDI device name of the monitor a window is on, e.g. <c>\.\DISPLAY1</c>,
    /// or null when there is no window to ask about.</summary>
    private static string? MonitorDeviceName(nint hwnd)
    {
        if (hwnd == 0) return null;
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == 0) return null;

        var info = new MONITORINFOEXW { cbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>() };
        return GetMonitorInfoW(monitor, ref info) ? info.szDevice : null;
    }

    private static float QueryCore(string? deviceName)
    {
        uint pathCount = 0, modeCount = 0;
        int sizes = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);
        if (sizes != ERROR_SUCCESS)
        {
            StartupTrace.Mark($"sdr-white: GetDisplayConfigBufferSizes -> {sizes}");
            return ScRgbReferenceNits;
        }

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
        int query = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (query != ERROR_SUCCESS)
        {
            StartupTrace.Mark($"sdr-white: QueryDisplayConfig -> {query} (paths {pathCount}, modes {modeCount}, " +
                $"pathSize {System.Runtime.InteropServices.Marshal.SizeOf<DISPLAYCONFIG_PATH_INFO>()})");
            return ScRgbReferenceNits;
        }

        // The path driving this window's monitor, matched by GDI device name. The level is
        // per display and genuinely differs — one monitor in HDR mode at 240 nits beside one
        // in SDR reporting 80 — so answering from whichever path comes first is answering
        // about the wrong screen half the time.
        for (uint i = 0; i < pathCount; i++)
        {
            if (deviceName is not null && !PathDrives(paths[i], deviceName)) continue;

            var request = new DISPLAYCONFIG_SDR_WHITE_LEVEL
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL,
                    size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SDR_WHITE_LEVEL>(),
                    adapterId = paths[i].targetInfo.adapterId,
                    id = paths[i].targetInfo.id,
                },
            };
            int got = DisplayConfigGetDeviceInfo(ref request);
            StartupTrace.Mark($"sdr-white: path {i} -> rc {got} raw {request.SDRWhiteLevel}");
            if (got != ERROR_SUCCESS) continue;

            // Documented as the level scaled by 1000 against 80 nits.
            float nits = request.SDRWhiteLevel / 1000f * ScRgbReferenceNits;
            if (nits > ScRgbReferenceNits) return nits;
        }
        return ScRgbReferenceNits;
    }

    /// <summary>Whether a path's source is the given GDI device.</summary>
    private static bool PathDrives(DISPLAYCONFIG_PATH_INFO path, string deviceName)
    {
        var request = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                adapterId = path.sourceInfo.adapterId,
                id = path.sourceInfo.id,
            },
        };
        return DisplayConfigGetSourceName(ref request) == ERROR_SUCCESS
            && string.Equals(request.viewGdiDeviceName, deviceName, StringComparison.OrdinalIgnoreCase);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEXW
    {
        public uint cbSize;
        public int left, top, right, bottom;
        public int workLeft, workTop, workRight, workBottom;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public int type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_SDR_WHITE_LEVEL
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint SDRWhiteLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public uint refreshNumerator;
        public uint refreshDenominator;
        public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    /// <summary>Opaque here: only its size matters, because QueryDisplayConfig has to be
    /// given somewhere to put the modes even when nothing reads them.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct DISPLAYCONFIG_MODE_INFO { }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount,
        [Out] DISPLAYCONFIG_PATH_INFO[] paths, ref uint modeCount,
        [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr currentTopology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SDR_WHITE_LEVEL request);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetSourceName(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MONITORINFOEXW info);
}
