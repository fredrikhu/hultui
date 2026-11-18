using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Windows;
using Xunit;

namespace HultUI.Headless.Tests;

/// <summary>
/// A dropdown the app named gives UI Automation its choice through the Value pattern, read-only,
/// as a Win32 drop-down list does: the reader reads the name, the role, then the choice, and a
/// change of the choice is a value change, not a name change. One the app did not name reads as
/// its choice and offers no value to say it twice.
/// </summary>
[SupportedOSPlatform("windows")]
public class A11yDropdownValueTests
{
    private sealed class Active(IAccessibilityBridge inner) : IAccessibilityBridge, IEagerItemsLimit
    {
        // The real bridge's number of items past which a list's are served on demand (UIA 200, AT-SPI 2,000).
        public int MaxEagerVirtualItems => AccessibilityTreeBuilder.MaxEagerVirtualItemsOf(inner);
        public bool IsActive => true;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
        public void NotifyChangesApplied(AccessibilityNode root) => inner.NotifyChangesApplied(root);
    }

    private sealed class UiaRig : IDisposable
    {
        private readonly TestHwnd _hwnd = TestHwnd.Create();
        public HeadlessSession Session { get; } = HeadlessSession.Create(400, 300);
        public WindowsAccessibilityBridge Bridge { get; }
        public readonly List<UiaRaised> Raised = [];

        public UiaRig()
        {
            var window = Session.Window.Window;
            var router = new AccessibilityActionRouter(window);
            var host = new UiaHostBridge(window, router);
            window.SetUiaHostBridge(host);
            window.SetActionRouter(router);
            Bridge = new WindowsAccessibilityBridge(_hwnd.Handle, host);
            Bridge.Raised = Raised.Add;
            Session.Window.InstallAccessibilityBridge(new Active(Bridge));
        }

        private UiaFragment Wrapper(Element element)
        {
            var native = Bridge.NativeFragment(element.Id);
            Assert.NotEqual(IntPtr.Zero, native);
            try { return Assert.IsType<UiaFragment>(NativeProvider.LogicOf(native)); }
            finally { Marshal.Release(native); }
        }

        public string? Name(Element element)
        {
            Wrapper(element).GetPropertyValue(UiaInterop.UIA_NamePropertyId, out var name);
            try { return name.Vt == UiaVariant.VT_BSTR ? name.AsString() : null; }
            finally { name.Free(); }
        }

        /// <summary>The Value pattern's value and whether it is read-only; null with no pattern.</summary>
        public (string? Value, bool ReadOnly)? ValuePattern(Element element)
        {
            Wrapper(element).GetPatternProvider(UiaInterop.UIA_ValuePatternId, out var pattern);
            if (pattern == IntPtr.Zero) return null;
            try
            {
                var provider = Assert.IsType<UiaValueProvider>(NativeProvider.LogicOf(pattern));
                provider.GetValue(out var bstr);
                provider.GetIsReadOnly(out int readOnly);
                try { return (Marshal.PtrToStringBSTR(bstr), readOnly != 0); }
                finally { Marshal.FreeBSTR(bstr); }
            }
            finally { Marshal.Release(pattern); }
        }

        public void Dispose()
        {
            Bridge.Dispose();
            Session.Dispose();
            _hwnd.Dispose();
        }
    }

    private static DropdownElement Mount(UiaRig rig, DropdownElement dropdown)
    {
        rig.Session.Window.SetRoot(Box.New().Padding(new Thickness(40)).Children(dropdown));
        rig.Session.PumpFrames(2);
        return dropdown;
    }

    [WindowsFact]
    public void UIA_ANamedDropdown_KeepsItsName_AndGivesItsChoiceAsAReadOnlyValue()
    {
        AccessibilityClients.Force = true;
        try
        {
            using var rig = new UiaRig();
            var dropdown = Mount(rig, Dropdown.New(["Alpha", "Beta"], 0).AccessibleName("Group"));
            Assert.Equal("Group", rig.Name(dropdown));
            Assert.Equal(("Alpha", true), rig.ValuePattern(dropdown));

            rig.Raised.Clear();
            dropdown.SelectedIndex = 1;
            rig.Session.PumpFrame();

            Assert.Contains(new UiaRaised(UiaRaisedKind.Property, UiaInterop.UIA_ValueValuePropertyId, dropdown.Id), rig.Raised);
            Assert.DoesNotContain(new UiaRaised(UiaRaisedKind.Property, UiaInterop.UIA_NamePropertyId, dropdown.Id), rig.Raised);
            Assert.Equal("Group", rig.Name(dropdown));
            Assert.Equal(("Beta", true), rig.ValuePattern(dropdown));
        }
        finally { AccessibilityClients.Force = null; }
    }

    [WindowsFact]
    public void UIA_AnUnnamedDropdown_ReadsAsItsChoice_WithNoValuePattern()
    {
        AccessibilityClients.Force = true;
        try
        {
            using var rig = new UiaRig();
            var dropdown = Mount(rig, Dropdown.New(["Alpha", "Beta"], 0));
            Assert.Equal("Alpha", rig.Name(dropdown));
            Assert.Null(rig.ValuePattern(dropdown));

            dropdown.SelectedIndex = 1;
            rig.Session.PumpFrame();

            Assert.Equal("Beta", rig.Name(dropdown));
            Assert.Null(rig.ValuePattern(dropdown));
        }
        finally { AccessibilityClients.Force = null; }
    }

    /// <summary>A date picker is a combo box named by its date, with the date as its value too: it
    /// offers no Value pattern, so a reader reads the date once.</summary>
    [WindowsFact]
    public void UIA_ADatePicker_NamedByItsDate_OffersNoValuePattern()
    {
        AccessibilityClients.Force = true;
        try
        {
            using var rig = new UiaRig();
            var picker = DatePicker.New(new DateOnly(2026, 9, 15));
            picker.Culture = System.Globalization.CultureInfo.InvariantCulture;
            picker.Format = "yyyy-MM-dd";
            rig.Session.Window.SetRoot(Box.New().Padding(new Thickness(40)).Children(picker));
            rig.Session.PumpFrames(2);

            Assert.Equal("2026-09-15", rig.Name(picker));
            Assert.Null(rig.ValuePattern(picker));
        }
        finally { AccessibilityClients.Force = null; }
    }
}
