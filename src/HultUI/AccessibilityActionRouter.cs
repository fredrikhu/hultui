using System.Collections.Concurrent;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;

namespace HultUI;

/// <summary>
/// Platform-neutral router for accessibility actions (invoke a button, toggle a
/// checkbox, select a radio, set a value, move focus). Assistive-technology
/// callbacks arrive on non-UI threads on every platform — UIA worker threads on
/// Windows, the AT-SPI D-Bus receive thread on Linux — so each action is queued
/// and executed on the UI thread when <see cref="Window"/> drains it at the start
/// of the next frame.
/// </summary>
/// <remarks>
/// This is the logic that used to live inline in <see cref="UiaHostBridge"/>;
/// it was lifted here so the Linux AT-SPI bridge can trigger the same actions
/// through <see cref="IAccessibilityActionHost"/> without duplicating the
/// element-lookup, dispatch, and marshalling code.
/// </remarks>
internal sealed class AccessibilityActionRouter : IAccessibilityActionHost, IAccessibilityGeometryHost
{
    private readonly Window _window;
    private readonly ConcurrentQueue<Action> _pendingActions = new();

    public AccessibilityActionRouter(Window window) => _window = window;

    /// <summary>Device pixels per layout unit — bridges scale element bounds by this.</summary>
    public float Scale => _window.PlatformWindow.DisplayScale;

    /// <summary>True when an action is queued but not yet run — folded into NeedsRender so an idle app wakes to drain it.</summary>
    public bool HasPendingActions => !_pendingActions.IsEmpty;

    /// <summary>Runs queued actions on the UI thread. Called once per frame by the Window.</summary>
    public void DrainPendingActions()
    {
        while (_pendingActions.TryDequeue(out var action))
        {
            try { action(); }
            catch { /* never let an AT client crash the UI loop */ }
        }
    }

    /// <summary>Queues an arbitrary action for UI-thread execution (for bridge-specific writes).</summary>
    public void Enqueue(Action action) => _pendingActions.Enqueue(action);

    public Element? FindElement(ElementId id) => _window.FindElement(id);

    // ──── IAccessibilityActionHost — the actions the platform bridges route to.

    public void InvokeElement(ElementId id) => _pendingActions.Enqueue(() =>
    {
        // A virtualized-list item has no element of its own; route its activation
        // back to the owning list by index (works even for off-screen items).
        if (VirtualItemId.TryDecode(id, out var listId, out var index))
        {
            if (FindElement(listId) is IAccessibleItemSource source)
                source.ActivateItem(index);
            return;
        }
        if (FindElement(id) is { } element)
            EventDispatcher.DispatchDirect(new ClickEvent { Position = default }, element);
    });

    public void ToggleElement(ElementId id) => _pendingActions.Enqueue(() =>
    {
        // Toggling a virtualized item is its activation: a menu choice is turned on by
        // being chosen, and the menu owns what that means.
        if (VirtualItemId.TryDecode(id, out var listId, out var index))
        {
            if (FindElement(listId) is IAccessibleItemSource source)
                source.ActivateItem(index);
            return;
        }
        if (FindElement(id) is CheckboxElement cb)
        {
            cb.IsChecked = !cb.IsChecked;
            cb.OnChanged?.Invoke(cb.IsChecked);
        }
    });

    public void SelectElement(ElementId id) => _pendingActions.Enqueue(() =>
    {
        // Selecting a virtualized-list item is its activation (single-select).
        if (VirtualItemId.TryDecode(id, out var listId, out var index))
        {
            if (FindElement(listId) is IAccessibleItemSource source)
                source.ActivateItem(index);
            return;
        }
        if (FindElement(id) is RadioButtonElement rb && !rb.IsChecked)
        {
            rb.IsChecked = true;
            rb.OnChanged?.Invoke(true);
        }
    });

    // ──── Extra actions the Windows UIA host routes through the same queue.

    public void SetValue(ElementId id, string value) => _pendingActions.Enqueue(() =>
    {
        switch (FindElement(id))
        {
            case TextInputElement input: input.Text = value; input.TextChanged?.Invoke(value); break;
            case TextAreaElement area: area.Text = value; area.TextChanged?.Invoke(value); break;
        }
    });

    public void RequestFocus(ElementId id) => _pendingActions.Enqueue(() => FindElement(id)?.Focus());
}
