using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Platform.Accessibility.Windows;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// What UI Automation is told about a masked field, at the provider calls it makes. Any process
/// in the session can make them, not only a screen reader, so the value pattern handing out the
/// text was a password readable by anything that asked.
///
/// <para>The value comes two ways — the snapshot behind <c>GetPropertyValue</c>, and the live
/// element behind the value pattern — and both are checked, because they were two leaks.</para>
/// </summary>
[SupportedOSPlatform("windows")]
public class PasswordUiaTests
{
    [WindowsFact]
    public void APasswordFieldSaysSo_AndOffersOnlyTheBullets()
    {
        using var hwnd = TestHwnd.Create();
        using var host = HandleWindowHost.Create(hwnd.Handle);
        var bridge = Assert.IsType<WindowsAccessibilityBridge>(AccessibilityBridgeFactory.Create(host.Application.PrimaryWindow));
        try
        {
            var username = TextInput.New("fredrik");
            var password = TextInput.New("hunter2").Password();
            var root = Box.New().Children(username, password);
            host.Application.SetRoot(root);
            var tree = AccessibilityTreeBuilder.Build(root);
            bridge.ApplyChanges(AccessibilityTreeDiffer.Diff(null, tree), tree);

            Assert.Equal((true, "●●●●●●●", "●●●●●●●"), Read(bridge, password));
            // The control: an ordinary field still gives its text, so the bullets above are
            // the mask and not a bridge that gives nothing.
            Assert.Equal((false, "fredrik", "fredrik"), Read(bridge, username));
        }
        finally { bridge.Dispose(); }
    }

    private static (bool IsPassword, string? Property, string Pattern) Read(WindowsAccessibilityBridge bridge, Element element)
    {
        var native = bridge.NativeFragment(element.Id);
        UiaFragment fragment;
        try { fragment = Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native)); }
        finally { Marshal.Release(native); }

        Assert.Equal(0, fragment.GetPropertyValue(UiaInterop.UIA_IsPasswordPropertyId, out var isPassword));
        Assert.Equal(UiaVariant.VT_BOOL, isPassword.Vt);

        Assert.Equal(0, fragment.GetPropertyValue(UiaInterop.UIA_ValueValuePropertyId, out var value));
        string? property = value.AsString();
        value.Free();

        Assert.Equal(0, fragment.GetPatternProvider(UiaInterop.UIA_ValuePatternId, out var pattern));
        Assert.NotEqual(IntPtr.Zero, pattern);
        string fromPattern;
        try
        {
            var provider = Assert.IsType<UiaValueProvider>(NativeProvider.LogicOf(pattern));
            Assert.Equal(0, provider.GetValue(out var bstr));
            fromPattern = Marshal.PtrToStringBSTR(bstr);
            Marshal.FreeBSTR(bstr);
        }
        finally { Marshal.Release(pattern); }

        return (isPassword.ShortValue != 0, property, fromPattern);
    }
}
