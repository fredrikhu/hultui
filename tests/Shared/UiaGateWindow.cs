using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Windows;
using Xunit;

namespace HultUI.Testing;

/// <summary>
/// A Windows UI Automation bridge on a hidden window of its own, as a real window has one, for tests of
/// what waits for its gate (plan 17 round 16): a window builds no accessibility tree and raises nothing,
/// and <see cref="AccessibilityClients"/> says nobody listens, until a client asks for the root of one of
/// the app's windows. <see cref="AskForRoot"/> asks as a client does (<c>WM_GETOBJECT</c> with
/// <c>UiaRootObjectId</c>); the next frame builds the tree. Put <see cref="Bridge"/> in a headless window
/// (<c>HeadlessWindow.InstallAccessibilityBridge</c>); dispose this after the window's last frame.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UiaGateWindow : IDisposable
{
    public IntPtr Handle { get; private set; }

    public WindowsAccessibilityBridge Bridge { get; }

    public UiaGateWindow()
    {
        // "STATIC" is a system class: no class to register, no window procedure to keep alive.
        Handle = CreateWindowExW(0, "STATIC", null, unchecked((int)0x80000000) /* WS_POPUP */,
            0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, Handle);
        Bridge = new WindowsAccessibilityBridge(Handle, new NoHost());
    }

    /// <summary>A client asks for the window's root, on this thread.</summary>
    public void AskForRoot() => SendMessageW(Handle, 0x003D /* WM_GETOBJECT */, IntPtr.Zero, -25 /* UiaRootObjectId */);

    /// <summary>Whether UI Automation says a client listens on this desktop: without one the gate
    /// opens on an ask, and still nothing is built for nobody.</summary>
    public static void AssertClientsListen() =>
        Assert.True(UiaClientsAreListening(), "UI Automation says no client listens on this desktop");

    /// <summary>The bridge put in a window, with what it is given to say (<see cref="IAccessibilityBridge.Announce"/>)
    /// kept in <paramref name="said"/>; everything else is the bridge's, its gate too.</summary>
    public IAccessibilityBridge Telling(List<string> said) => new TellingBridge(Bridge, said);

    private sealed class TellingBridge(IAccessibilityBridge inner, List<string> said) : IAccessibilityBridge
    {
        public bool IsActive => inner.IsActive;
        public void ApplyChanges(TreeDiff diff, AccessibilityNode root) => inner.ApplyChanges(diff, root);
        public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root) => inner.ApplyPatchedProperties(patch, root);
        public void NotifyFocusChanged(AccessibilityNode? focused) => inner.NotifyFocusChanged(focused);
        public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous) => inner.NotifyFocusChanged(focused, previous);
        public void NotifyChangesApplied(AccessibilityNode root) => inner.NotifyChangesApplied(root);
        public void NotifyWindowActivated() => inner.NotifyWindowActivated();
        public void Announce(AccessibilityNode? source, string text, AnnouncementPoliteness politeness)
        {
            said.Add(text);
            inner.Announce(source, text, politeness);
        }
    }

    [DllImport("UIAutomationCore.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UiaClientsAreListening();

    public void Dispose()
    {
        Bridge.Dispose();
        if (Handle != IntPtr.Zero) { DestroyWindow(Handle); Handle = IntPtr.Zero; }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int exStyle, string cls, string? name, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")] private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>A host with nothing behind it: what the tree says, not actions a client asks for.</summary>
    private sealed class NoHost : IUiaHostBridge
    {
        public void InvokeElement(ElementId id) { }
        public void ToggleElement(ElementId id) { }
        public void SelectElement(ElementId id) { }
        public void SetValue(ElementId id, string value) { }
        public void RequestFocus(ElementId id) { }
        public float ContentScale => 1f;
        public string? GetValue(ElementId id) => null;
        public bool? GetToggleState(ElementId id) => null;
        public int GetItemCount(ElementId listId) => 0;
        public string? GetItemLabel(ElementId listId, int index) => null;
        public bool GetItemSelected(ElementId listId, int index) => false;
        public int GetSelectedIndex(ElementId listId) => -1;
        public bool GetCanSelectMultiple(ElementId listId) => false;
        public IReadOnlyList<int> GetSelectedIndices(ElementId listId) => [];
        public void SelectItemOnly(ElementId listId, int index) { }
        public void SetItemSelected(ElementId listId, int index, bool selected) { }
        public int FindItemIndex(ElementId listId, int startAfter, AccessibleItemQuery query) => -1;
        public bool GetItemOffscreen(ElementId listId, int index) => false;
        public bool TryGetItemLayout(ElementId listId, int index, out Rect layoutRect) { layoutRect = default; return false; }
        public bool TryGetScrollInfo(ElementId id, out ScrollInfo info) { info = default; return false; }
        public void SetScrollPercent(ElementId id, double horizontalPercent, double verticalPercent) { }
        public void ScrollByAmount(ElementId id, int horizontalAmount, int verticalAmount) { }
        public bool TryGetRangeValue(ElementId id, out RangeValueInfo info) { info = default; return false; }
        public void SetRangeValue(ElementId id, double value) { }
    }
}

/// <summary>A fact that runs on Windows only, where the UI Automation bridge is.</summary>
public sealed class UiaGateFactAttribute : FactAttribute
{
    public UiaGateFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows-only: the UI Automation bridge's gate.";
    }
}
