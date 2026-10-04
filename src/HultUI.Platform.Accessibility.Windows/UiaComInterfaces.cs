using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HultUI.Platform.Accessibility.Windows;

// The UIA provider interfaces are exposed to COM as hand-built native vtables
// (see NativeProvider); the managed side implements the INative* interfaces in
// NativeProvider.cs. Only the shared blittable structs live here.

/// <summary>Native UIA rectangle (four doubles), as UIA's providers return it.</summary>
[SupportedOSPlatform("windows")]
[StructLayout(LayoutKind.Sequential)]
internal struct UiaRect
{
    public double Left;
    public double Top;
    public double Width;
    public double Height;
}

/// <summary>Native UIA point (two doubles), passed by value to <c>RangeFromPoint</c>.</summary>
[SupportedOSPlatform("windows")]
[StructLayout(LayoutKind.Sequential)]
internal struct UiaPoint
{
    public double X;
    public double Y;
}
