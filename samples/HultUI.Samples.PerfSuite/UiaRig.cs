using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using HultUI.Headless;
using HultUI.Platform.Accessibility.Windows;

namespace PerfSuite;

/// <summary>
/// The real UI Automation bridge, on a hidden window that never shows or takes focus, for
/// the rows that time what a Windows screen reader adds on top of the tree build and diff:
/// the bridge's wrapper upkeep and the UIA events it raises. The window disposes it with the
/// session. It reports itself active whether or not a UIA client listens, so the rows time
/// the same work on any desk.
/// </summary>
[SupportedOSPlatform("windows")]
sealed class UiaRig : IAccessibilityBridge, IDisposable
{
    private IntPtr _hwnd;
    private readonly WindowsAccessibilityBridge _bridge;

    private UiaRig(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _bridge = new WindowsAccessibilityBridge(hwnd, new NoHost());
    }

    /// <summary>Changes the bridge has applied, diffs and in-place patches (a selection move), for a
    /// scenario's check.</summary>
    public int Applies { get; private set; }

    /// <summary>Focus moves the bridge was told of, for a scenario's check.</summary>
    public int FocusMoves { get; private set; }

    public static UiaRig Install(HeadlessWindow window)
    {
        // "STATIC" is a system class: no class to register, no window procedure to keep alive.
        var hwnd = CreateWindowExW(0, "STATIC", null, unchecked((int)0x80000000) /* WS_POPUP */,
            0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("Could not create the hidden window.");
        return window.InstallAccessibilityBridge(new UiaRig(hwnd));
    }

    public bool IsActive => true;

    public void ApplyChanges(TreeDiff diff, AccessibilityNode root)
    {
        _bridge.ApplyChanges(diff, root);
        Applies++;
    }

    public void ApplyPatchedProperties(TreeDiff patch, AccessibilityNode root)
    {
        _bridge.ApplyPatchedProperties(patch, root);
        Applies++;
    }

    public void NotifyFocusChanged(AccessibilityNode? focused)
    {
        _bridge.NotifyFocusChanged(focused);
        FocusMoves++;
    }

    public void NotifyFocusChanged(AccessibilityNode? focused, AccessibilityNode? previous)
    {
        _bridge.NotifyFocusChanged(focused, previous);
        FocusMoves++;
    }

    public void Announce(AccessibilityNode? source, string text, AnnouncementPoliteness politeness)
        => _bridge.Announce(source, text, politeness);

    public void Dispose()
    {
        _bridge.Dispose();
        if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int exStyle, string cls, string? name, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    /// <summary>A host with nothing behind it: the rows time the bridge, not actions a client asks for.</summary>
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
