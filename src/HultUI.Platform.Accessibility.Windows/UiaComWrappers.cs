using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace HultUI.Platform.Accessibility.Windows;

/// <summary>
/// AOT-compatible bridge between managed provider objects and raw COM
/// pointers. <see cref="StrategyBasedComWrappers"/> uses the GUID + vtables
/// generated for each <c>[GeneratedComInterface]</c> at build time, so we
/// avoid runtime-only <c>Marshal.GetIUnknownForObject</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class UiaComWrappers
{
    private static readonly StrategyBasedComWrappers Wrappers = new();

    public static IntPtr GetIUnknown(object managed) =>
        Wrappers.GetOrCreateComInterfaceForObject(managed, CreateComInterfaceFlags.None);

    public static object GetObject(IntPtr comPtr) =>
        Wrappers.GetOrCreateObjectForComInstance(comPtr, CreateObjectFlags.None);
}
