using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Xunit;

namespace HultUI.Platform.SDL3.Tests;

/// <summary>
/// How far a maximized custom-frame window insets its client area: Windows hangs a maximized
/// window past each edge of the monitor's work area by the window's frame at the window's own
/// DPI, so the inset is that frame — not the frame at the system DPI. On a desk with a 4K
/// monitor at 150 % (the system DPI, 144) and a 1440p monitor at 100 %, the system's answer
/// (11 px) inset a window maximized on the 1440p monitor 3 px short of each screen edge, which
/// its 8 px frame overhang left as a strip of non-client frame (plan 17, round 6).
/// </summary>
[SupportedOSPlatform("windows")]
public class MaximizedFrameInsetTests
{
    // The custom frame's window: SDL's resizable window, less WS_SYSMENU (WindowsCustomFrame.Install).
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint WS_MINIMIZEBOX = 0x00020000;
    private const uint WS_MAXIMIZEBOX = 0x00010000;
    private const uint CustomFrameStyle = WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;

    [WindowsTheory]
    [InlineData(96u)]
    [InlineData(120u)]
    [InlineData(144u)]
    [InlineData(168u)]
    [InlineData(192u)]
    public void IsTheFrameWindowsGivesTheWindow_AtTheWindowsOwnDpi(uint dpi)
    {
        // Windows' own answer for the frame round a window of this style at this DPI: the
        // bottom and the sides are all frame (the top adds the caption the frame reclaims).
        var frame = new RECT();
        Assert.True(AdjustWindowRectExForDpi(ref frame, CustomFrameStyle, false, 0, dpi));

        Assert.Equal((frame.Right, frame.Bottom), WindowsCustomFrame.MaximizedFrameInset(dpi));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32")]
    private static extern bool AdjustWindowRectExForDpi(ref RECT rect, uint style, bool menu, uint exStyle, uint dpi);
}

/// <summary>A theory that runs only on Windows, and says so where it is skipped.</summary>
internal sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows only: the custom frame is a Win32 window";
    }
}
