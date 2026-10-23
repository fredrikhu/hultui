using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Primitives;
using HultUI.Platform.Accessibility.Windows;

namespace HultUI;

/// <summary>
/// Hooks the Windows UIA bridge into the frame loop.
///
/// UIA clients call provider methods from worker threads, which means we must
/// either snapshot everything (the bridge does, for reads: the tree) or marshal writes onto the
/// UI thread. This adapter uses a
/// <see cref="System.Collections.Concurrent.ConcurrentQueue{T}"/> drained at
/// the start of each frame, so element mutations happen on the same thread as
/// the render loop. (<c>WM_GETOBJECT</c> is serviced by the bridge's own
/// window-procedure subclass, so no message hook is involved.)
///
/// <para>Nothing here is backend-specific: it reads the window's scale and tree
/// and talks to the router, none of which knows what created the HWND. It was
/// called <c>Sdl3UiaHostBridge</c> until a HultUI surface hosted on someone
/// else's window ran it unchanged in a process where SDL was never
/// initialised.</para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class UiaHostBridge : IUiaHostBridge, IDisposable
{
    private readonly Window _window;
    private readonly AccessibilityActionRouter _router;

    public UiaHostBridge(Window window, AccessibilityActionRouter router)
    {
        _window = window;
        _router = router;
    }

    public float ContentScale => _window.PlatformWindow.DisplayScale;

    // ──── Actions — delegate to the shared router (which owns the UI-thread
    //      queue drained by the Window each frame).

    public void InvokeElement(ElementId id) => _router.InvokeElement(id);
    public void ToggleElement(ElementId id) => _router.ToggleElement(id);
    public void SelectElement(ElementId id) => _router.SelectElement(id);
    public void SetValue(ElementId id, string value) => _router.SetValue(id, value);
    public void RequestFocus(ElementId id) => _router.RequestFocus(id);

    /// <summary>A client asked for the root. If the window has not built a tree for a listening
    /// client yet (one has just attached), the loop wakes and its next frame builds it.</summary>
    public void RootRequested()
    {
        if (!_window.AccessibilityTreeBuilt) _router.ActiveChanged();
    }

    // ──── Reads — the bridge no longer calls these (plan 17, round 4): each found the
    //      element with a walk of the live tree on the client's thread, and the bridge
    //      answers from the accessibility tree it applied instead. Kept for the interface.

    public string? GetValue(ElementId id) => GetElementOrNull(id) switch
    {
        // The bullets, for a password: this is what any UI Automation client in the session
        // reads, not only a screen reader.
        TextInputElement input => input.ShownText,
        TextAreaElement area => area.Text,
        { } element => element.AccessibleValue,
        null => null,
    };

    public bool? GetToggleState(ElementId id)
    {
        // A virtualized item is not an element: its id carries the list it belongs to and
        // its index, and the list answers for it — which is how a menu reports which of
        // its choices is the one in force.
        if (VirtualItemId.TryDecode(id, out var listId, out int index))
        {
            try { return GetElementOrNull(listId) is IAccessibleItemSource s ? s.ItemIsToggled(index) : null; }
            catch { return null; }
        }

        return GetElementOrNull(id) switch
        {
            CheckboxElement cb => cb.IsChecked,
            RadioButtonElement rb => rb.IsChecked,
            _ => null,
        };
    }

    public int GetItemCount(ElementId listId)
    {
        try { return GetElementOrNull(listId) is IAccessibleItemSource s ? s.ItemCount : 0; }
        catch { return 0; }
    }

    public string? GetItemLabel(ElementId listId, int index)
    {
        try { return GetElementOrNull(listId) is IAccessibleItemSource s ? s.GetItemLabel(index) : null; }
        catch { return null; }
    }

    public bool GetItemSelected(ElementId listId, int index)
    {
        try { return GetElementOrNull(listId) is IAccessibleItemSource s && s.IsItemSelected(index); }
        catch { return false; }
    }

    public int GetSelectedIndex(ElementId listId)
    {
        try { return GetElementOrNull(listId) is IAccessibleItemSource s ? s.SelectedIndex : -1; }
        catch { return -1; }
    }

    public bool GetCanSelectMultiple(ElementId listId)
    {
        try { return GetElementOrNull(listId) is IAccessibleItemSource s && s.CanSelectMultiple; }
        catch { return false; }
    }

    public IReadOnlyList<int> GetSelectedIndices(ElementId listId)
    {
        try { return GetElementOrNull(listId) is IAccessibleItemSource s ? s.SelectedIndices : []; }
        catch { return []; }
    }

    public void SelectItemOnly(ElementId listId, int index) => _router.Enqueue(() =>
    {
        if (GetElementOrNull(listId) is IAccessibleItemSource s) s.SelectOnly(index);
    });

    public void SetItemSelected(ElementId listId, int index, bool selected) => _router.Enqueue(() =>
    {
        if (GetElementOrNull(listId) is IAccessibleItemSource s) s.SetItemSelected(index, selected);
    });

    public int FindItemIndex(ElementId listId, int startAfter, AccessibleItemQuery query)
    {
        try { return GetElementOrNull(listId) is IAccessibleItemSource s ? s.FindItem(startAfter, query) : -1; }
        catch { return -1; }
    }

    public bool GetItemOffscreen(ElementId listId, int index)
    {
        try
        {
            if (GetElementOrNull(listId) is not ScrollViewElement sv || sv is not IAccessibleItemSource src)
                return true;
            if (index < 0 || index >= src.ItemCount) return true;
            // The item is offscreen when it sits above or below the list's own
            // viewport (its dominant clip), given the current scroll.
            float itemTop = index * src.ItemHeight - sv.ScrollOffsetY;
            float viewportHeight = sv.Layout.AbsoluteContentBox.Height;
            return itemTop + src.ItemHeight <= 0f || itemTop >= viewportHeight;
        }
        catch { return true; }
    }

    public bool TryGetScrollInfo(ElementId id, out ScrollInfo info)
    {
        info = default;
        try
        {
            if (GetElementOrNull(id) is not ScrollViewElement sv) return false;
            var vp = sv.Layout.AbsoluteContentBox;
            double maxY = Math.Max(0, sv.ContentHeight - vp.Height);
            double maxX = Math.Max(0, sv.ContentWidth - vp.Width);
            double vPercent = maxY > 0 ? sv.ScrollOffsetY / maxY * 100 : UiaNoScroll;
            double hPercent = maxX > 0 ? sv.ScrollOffsetX / maxX * 100 : UiaNoScroll;
            double vView = sv.ContentHeight > 0 ? Math.Min(100, vp.Height / sv.ContentHeight * 100) : 100;
            double hView = sv.ContentWidth > 0 ? Math.Min(100, vp.Width / sv.ContentWidth * 100) : 100;
            info = new ScrollInfo(hPercent, vPercent, hView, vView, maxX > 0, maxY > 0);
            return true;
        }
        catch { return false; }
    }

    private const double UiaNoScroll = -1.0;

    public void SetScrollPercent(ElementId id, double horizontalPercent, double verticalPercent) =>
        _router.Enqueue(() =>
        {
            if (GetElementOrNull(id) is not ScrollViewElement sv) return;
            var vp = sv.Layout.AbsoluteContentBox;
            if (verticalPercent >= 0)
                sv.ScrollOffsetY = (float)(verticalPercent / 100 * Math.Max(0, sv.ContentHeight - vp.Height));
            if (horizontalPercent >= 0)
                sv.ScrollOffsetX = (float)(horizontalPercent / 100 * Math.Max(0, sv.ContentWidth - vp.Width));
            sv.MarkPaintDirty();
        });

    public void ScrollByAmount(ElementId id, int horizontalAmount, int verticalAmount) =>
        _router.Enqueue(() =>
        {
            if (GetElementOrNull(id) is not ScrollViewElement sv) return;
            var vp = sv.Layout.AbsoluteContentBox;
            sv.ScrollOffsetY += AmountToDelta(verticalAmount, vp.Height);
            sv.ScrollOffsetX += AmountToDelta(horizontalAmount, vp.Width);
            sv.MarkPaintDirty();
        });

    // A line is a fixed step; a page is the viewport extent.
    private static float AmountToDelta(int scrollAmount, float viewport) => scrollAmount switch
    {
        0 => -viewport, // LargeDecrement
        1 => -40f,      // SmallDecrement
        3 => viewport,  // LargeIncrement
        4 => 40f,       // SmallIncrement
        _ => 0f,        // NoAmount
    };

    public bool TryGetRangeValue(ElementId id, out RangeValueInfo info)
    {
        info = default;
        try
        {
            if (GetElementOrNull(id) is not IRangeValueElement r) return false;
            info = new RangeValueInfo(r.Value, r.Minimum, r.Maximum, r.SmallChange, r.LargeChange, r.IsReadOnly);
            return true;
        }
        catch { return false; }
    }

    public void SetRangeValue(ElementId id, double value) => _router.Enqueue(() =>
    {
        if (GetElementOrNull(id) is IRangeValueElement r && !r.IsReadOnly)
            r.SetAccessibleValue(value);
    });

    public bool TryGetItemLayout(ElementId listId, int index, out Rect layoutRect)
    {
        layoutRect = default;
        try
        {
            if (GetElementOrNull(listId) is not ScrollViewElement sv || sv is not IAccessibleItemSource src)
                return false;
            if (index < 0 || index >= src.ItemCount) return false;

            // The item sits in the list's content box at index * itemHeight, moved
            // by the list's own scroll and by every ancestor scroll view — the same
            // adjustment AccessibilityTreeBuilder bakes into materialized nodes.
            var content = sv.ScreenContentBox;
            float top = content.Y - sv.ScrollOffsetY + index * src.ItemHeight;
            float left = content.X - sv.ScrollOffsetX;
            layoutRect = new Rect(left, top, content.Width, src.ItemHeight);
            return true;
        }
        catch { return false; }
    }

    private Element? GetElementOrNull(ElementId id) => _window.FindElement(id);

    public void Dispose() { }
}
