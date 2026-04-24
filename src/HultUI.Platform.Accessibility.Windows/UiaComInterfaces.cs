using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace HultUI.Platform.Accessibility.Windows;

// NOTE: GeneratedComInterface produces source-generated CCWs at build time —
// the key reason we use it is AOT compatibility. It does NOT support C#
// properties (SYSLIB1091); every COM slot must be spelled out as a method.
// The GUIDs below come from UIAutomationCore.h.

[SupportedOSPlatform("windows")]
[GeneratedComInterface]
[Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
internal partial interface IRawElementProviderSimple
{
    [PreserveSig] int GetProviderOptions(out int pRetVal);
    [PreserveSig] int GetPatternProvider(int patternId, out IntPtr pPatternProvider);
    [PreserveSig] int GetPropertyValue(int propertyId, out UiaVariant pRetVal);
    [PreserveSig] int GetHostRawElementProvider(out IRawElementProviderSimple? pRetVal);
}

[SupportedOSPlatform("windows")]
[StructLayout(LayoutKind.Sequential)]
internal struct UiaRect
{
    public double Left;
    public double Top;
    public double Width;
    public double Height;
}

[SupportedOSPlatform("windows")]
[GeneratedComInterface]
[Guid("f7063da8-8359-439c-9297-bbc5299a7d87")]
internal partial interface IRawElementProviderFragment
{
    [PreserveSig] int Navigate(int direction, out IRawElementProviderFragment? pRetVal);
    [PreserveSig] int GetRuntimeId(out IntPtr pRetVal);
    [PreserveSig] int GetBoundingRectangle(out UiaRect pRetVal);
    [PreserveSig] int GetEmbeddedFragmentRoots(out IntPtr pRetVal);
    [PreserveSig] int SetFocus();
    [PreserveSig] int GetFragmentRoot(out IRawElementProviderFragmentRoot? pRetVal);
}

[SupportedOSPlatform("windows")]
[GeneratedComInterface]
[Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58")]
internal partial interface IRawElementProviderFragmentRoot
{
    [PreserveSig] int ElementProviderFromPoint(double x, double y, out IRawElementProviderFragment? pRetVal);
    [PreserveSig] int GetFocus(out IRawElementProviderFragment? pRetVal);
}

// ──── Pattern provider interfaces

[SupportedOSPlatform("windows")]
[GeneratedComInterface]
[Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2")]
internal partial interface IInvokeProvider
{
    [PreserveSig] int Invoke();
}

[SupportedOSPlatform("windows")]
[GeneratedComInterface]
[Guid("56d00bd0-c4f4-433c-a836-1a52a57e0892")]
internal partial interface IToggleProvider
{
    [PreserveSig] int Toggle();
    [PreserveSig] int GetToggleState(out int pRetVal);
}

[SupportedOSPlatform("windows")]
[GeneratedComInterface]
[Guid("c7935180-6fb3-4201-b174-7df73adbf64a")]
internal partial interface IValueProvider
{
    [PreserveSig] int SetValue([MarshalAs(UnmanagedType.LPWStr)] string val);
    [PreserveSig] int GetValue([MarshalAs(UnmanagedType.BStr)] out string pRetVal);
    [PreserveSig] int GetIsReadOnly([MarshalAs(UnmanagedType.Bool)] out bool pRetVal);
}

[SupportedOSPlatform("windows")]
[GeneratedComInterface]
[Guid("2acad808-b2d4-452d-a407-91ff1ad167b2")]
internal partial interface ISelectionItemProvider
{
    [PreserveSig] int Select();
    [PreserveSig] int AddToSelection();
    [PreserveSig] int RemoveFromSelection();
    [PreserveSig] int GetIsSelected([MarshalAs(UnmanagedType.Bool)] out bool pRetVal);
    [PreserveSig] int GetSelectionContainer(out IRawElementProviderSimple? pRetVal);
}
