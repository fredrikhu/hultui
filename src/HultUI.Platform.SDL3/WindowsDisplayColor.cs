using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.DevTools;

namespace HultUI.Platform.SDL3;

/// <summary>
/// What Windows is doing with colour on the monitor a window is on: whether it is in HDR mode,
/// and how bright it draws SDR white. Both from the display configuration API, which is the
/// one source that is right the moment either changes.
///
/// <para><b>The SDR white level</b> exists because scRGB defines 1.0 as 80 nits and Windows
/// does not draw SDR white at 80 nits. In HDR mode it composites SDR content at whatever the
/// "SDR content brightness" slider says — 80 to 480 — so an app that writes a correct linear
/// 1.0 for white lands far below every other window on the desktop and looks grey. No capture
/// shows it: the swapchain image is right, and the compositor is doing what it was told.</para>
///
/// <para><b>The HDR state</b> exists because SDL's is wrong in two ways, both seen on the
/// 455-nit test monitor (2026-09-28). SDL calls a display HDR when its headroom — peak over SDR
/// white — exceeds 1, so with the slider at 480 nits it reported an HDR display as SDR and the
/// window got an 8-bit swapchain with no HDR video. And across Win+Alt+B it kept reporting HDR
/// after HDR was switched off, so the swapchain stayed half-float on an SDR screen. The
/// advanced-colour state below is what the Settings page itself shows.</para>
///
/// <para>SDL does expose an SDR white level; on Windows it is the same number, but SDL only
/// re-reads it when the display configuration changes, which the slider does not do.</para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsDisplayColor
{
    /// <summary>scRGB's reference white, and the answer when the real one is unavailable.</summary>
    public const float ScRgbReferenceNits = 80f;

    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    private const int ERROR_SUCCESS = 0;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    // From the DISPLAYCONFIG_DEVICE_INFO_TYPE enum. Worth stating plainly because a wrong value
    // here is not a compile error and not a crash: DisplayConfigGetDeviceInfo returns 87,
    // ERROR_INVALID_PARAMETER, and a fallback quietly reports 80 nits or "no HDR" — plausible
    // answers that make the caller do nothing.
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL = 11;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2 = 15;

    private const uint DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR = 2;

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
            if (FindTarget(MonitorDeviceName(hwnd)) is not { } target) return ScRgbReferenceNits;

            var request = new DISPLAYCONFIG_SDR_WHITE_LEVEL
            {
                header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL,
                    Marshal.SizeOf<DISPLAYCONFIG_SDR_WHITE_LEVEL>(), target),
            };
            // Traced only on failure: this is polled, and the caller traces what changed.
            int got = DisplayConfigGetDeviceInfo(ref request);
            if (got != ERROR_SUCCESS)
            {
                StartupTrace.Mark($"sdr-white: DisplayConfigGetDeviceInfo -> {got}");
                return ScRgbReferenceNits;
            }

            // Documented as the level scaled by 1000 against 80 nits.
            float nits = request.SDRWhiteLevel / 1000f * ScRgbReferenceNits;
            return nits > 0f ? nits : ScRgbReferenceNits;
        }
        catch (DllNotFoundException) { return ScRgbReferenceNits; }
        catch (EntryPointNotFoundException) { return ScRgbReferenceNits; }
    }

    /// <summary>
    /// Whether the display showing that window is in HDR mode right now, or null when Windows
    /// will not say — the caller then falls back to SDL's answer.
    ///
    /// <para>The newer query first, because it separates HDR from the wide-colour mode that
    /// Windows 11 can run an SDR monitor in; both set the older query's "advanced colour
    /// enabled". Where only the older one exists, "enabled and not wide colour enforced" is
    /// Microsoft's own reading of HDR.</para>
    /// </summary>
    public static bool? QueryHdr(nint hwnd)
    {
        try
        {
            if (FindTarget(MonitorDeviceName(hwnd)) is not { } target) return null;

            var info2 = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
            {
                header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2,
                    Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2>(), target),
            };
            int got = DisplayConfigGetAdvancedColorInfo2(ref info2);
            if (got == ERROR_SUCCESS)
                return info2.activeColorMode == DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR;

            var info = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
            {
                header = Header(DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                    Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(), target),
            };
            int legacy = DisplayConfigGetAdvancedColorInfo(ref info);
            if (legacy != ERROR_SUCCESS)
            {
                StartupTrace.Mark($"hdr-state: both advanced-colour queries failed ({got}, {legacy})");
                return null;
            }

            bool enabled = (info.value & 0x2) != 0;          // advancedColorEnabled
            bool wideColorEnforced = (info.value & 0x4) != 0;
            return enabled && !wideColorEnforced;
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
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

    /// <summary>
    /// The target driving that monitor, matched by GDI device name. The answers are per display
    /// and genuinely differ — one monitor in HDR mode at 240 nits beside one in SDR — so
    /// answering from whichever path comes first is answering about the wrong screen half the
    /// time. A null device name takes the first active path.
    /// </summary>
    private static (LUID AdapterId, uint Id)? FindTarget(string? deviceName)
    {
        int sizes = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);
        if (sizes != ERROR_SUCCESS)
        {
            StartupTrace.Mark($"display-config: GetDisplayConfigBufferSizes -> {sizes}");
            return null;
        }

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
        int query = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (query != ERROR_SUCCESS)
        {
            StartupTrace.Mark($"display-config: QueryDisplayConfig -> {query} (paths {pathCount}, modes {modeCount}, " +
                $"pathSize {Marshal.SizeOf<DISPLAYCONFIG_PATH_INFO>()})");
            return null;
        }

        for (uint i = 0; i < pathCount; i++)
        {
            if (deviceName is not null && !PathDrives(paths[i], deviceName)) continue;
            return (paths[i].targetInfo.adapterId, paths[i].targetInfo.id);
        }
        return null;
    }

    private static DISPLAYCONFIG_DEVICE_INFO_HEADER Header(int type, int size, (LUID AdapterId, uint Id) target) =>
        new() { type = type, size = (uint)size, adapterId = target.AdapterId, id = target.Id };

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

    /// <summary>The bitfield union is read as <see cref="value"/>: bit 1 advancedColorEnabled,
    /// bit 2 wideColorEnforced.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
        public uint colorEncoding;
        public uint bitsPerColorChannel;
    }

    /// <summary>Windows 11 24H2 and later. <see cref="activeColorMode"/> is SDR (0), WCG (1)
    /// or HDR (2).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
        public uint colorEncoding;
        public uint bitsPerColorChannel;
        public uint activeColorMode;
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

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetAdvancedColorInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO request);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetAdvancedColorInfo2(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 request);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(nint monitor, ref MONITORINFOEXW info);
}
