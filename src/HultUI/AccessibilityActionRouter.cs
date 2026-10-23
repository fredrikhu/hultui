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

    /// <summary>
    /// Queues an action for UI-thread execution (every action, and bridge-specific writes), and
    /// wakes the frame loop: the action is drained by the next frame (<see cref="HasPendingActions"/>
    /// asks for one), and an idle loop waits for a wake, or its next deadline, before it looks.
    /// </summary>
    public void Enqueue(Action action)
    {
        _pendingActions.Enqueue(action);
        Wake(WakeSource.A11yAction);
    }

    /// <summary>A bridge's <c>IsActive</c> may have changed off the UI thread (assistive technology
    /// switched on or off, a client attached): the loop wakes so the window looks again — the
    /// first frame of an attach builds the whole tree.</summary>
    public void ActiveChanged() => Wake(WakeSource.A11yClient);

    /// <summary>
    /// Wakes the loop through the thread pool, never from the calling thread: these calls come on
    /// assistive technology's threads, and a wake is an <c>SDL_PushEvent</c>, which waits for SDL's
    /// event-watcher lock; a frame drawn inside the watcher during a live resize holds it, and a
    /// frame must never wait for a thread that posts (docs/THREADING.md).
    /// </summary>
    private void Wake(WakeSource source)
    {
        Interlocked.Increment(ref _wakesAsked);
        ThreadPool.UnsafeQueueUserWorkItem(static s => s.App.WakeLoop(s.Source), (App: _window.Application, Source: source),
            preferLocal: false);
    }

    /// <summary>How many wakes this router has asked for, counted as each is asked, before its pool
    /// hop. For tests that a call asks for none: a wake that never comes cannot be waited for, and
    /// the platform's own count also takes wakes from every other thread.</summary>
    internal int WakesAsked => Volatile.Read(ref _wakesAsked);
    private int _wakesAsked;

    public Element? FindElement(ElementId id) => _window.FindElement(id);

    // ──── IAccessibilityActionHost — the actions the platform bridges route to.

    /// <summary>A described node's action goes to its source (seam W11). True if it was one.</summary>
    private bool ToContent(ElementId id, AccessibleContentAction action, string? value = null)
    {
        if (!ContentNodeId.TryDecode(id, out var owner, out var key)) return false;
        if (FindElement(owner) is IAccessibleContentSource source) source.PerformAccessibleAction(key, action, value);
        return true;
    }

    public void InvokeElement(ElementId id) => Enqueue(() =>
    {
        if (ToContent(id, AccessibleContentAction.Invoke)) return;
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

    public void ToggleElement(ElementId id) => Enqueue(() =>
    {
        if (ToContent(id, AccessibleContentAction.Toggle)) return;
        // Toggling a virtualized item is its activation: a menu choice is turned on by
        // being chosen, and the menu owns what that means.
        if (VirtualItemId.TryDecode(id, out var listId, out var index))
        {
            if (FindElement(listId) is IAccessibleItemSource source)
                source.ActivateItem(index);
            return;
        }
        // A checkbox, a switch or a toggling button (Element.AccessibleToggled) is toggled by
        // being pressed: the click runs its own handler and anything the app hung on the click.
        // A switch had no route here and its Toggle did nothing.
        var target = FindElement(id);
        if (target is CheckboxElement or ToggleSwitchElement or { AccessibleToggled: not null })
            EventDispatcher.DispatchDirect(new ClickEvent { Position = default }, target);
    });

    public void SelectElement(ElementId id) => Enqueue(() =>
    {
        if (ToContent(id, AccessibleContentAction.Select)) return;
        // Selecting a virtualized-list item is its activation (single-select).
        if (VirtualItemId.TryDecode(id, out var listId, out var index))
        {
            if (FindElement(listId) is IAccessibleItemSource source)
                source.ActivateItem(index);
            return;
        }
        switch (FindElement(id))
        {
            // A radio is selected by its click, which also clears the rest of its group: set
            // directly, the group was left with two checked.
            case RadioButtonElement { IsChecked: false } rb:
                EventDispatcher.DispatchDirect(new ClickEvent { Position = default }, rb);
                break;
            // One of a set that selects itself on click — a tab.
            case { AccessibleSelected: false } item:
                EventDispatcher.DispatchDirect(new ClickEvent { Position = default }, item);
                break;
        }
    });

    /// <summary>
    /// Opens or closes what the element opens. A dropdown and a date picker open on a press,
    /// not a click, so they are opened as Enter opens them; a submenu through its menu; anything
    /// else that says it opens (<see cref="Element.AccessibleExpanded"/>: an expander, a popover's
    /// anchor) by its click, when it is not already as asked.
    /// </summary>
    public void ExpandCollapseElement(ElementId id, bool expand) => Enqueue(() =>
    {
        if (ToContent(id, AccessibleContentAction.Invoke)) return;
        if (VirtualItemId.TryDecode(id, out var listId, out var index))
        {
            if (FindElement(listId) is IAccessibleItemSource source) source.SetItemExpanded(index, expand);
            return;
        }
        switch (FindElement(id))
        {
            case DropdownElement dropdown:
                dropdown.SetOpenFromAssistiveTool(expand);
                break;
            case DatePickerElement picker:
                if (expand) picker.OpenPopup();
                else picker.ClosePopup();
                break;
            case { AccessibleExpanded: { } open } element when open != expand:
                EventDispatcher.DispatchDirect(new ClickEvent { Position = default }, element);
                break;
        }
    });

    /// <summary>
    /// Scrolls the element into view in every scroll view that holds it, innermost first. A list
    /// item is scrolled into its list first (it may have no element); a described node brings its
    /// owner into view.
    /// </summary>
    public void ScrollElementIntoView(ElementId id) => Enqueue(() =>
    {
        if (ContentNodeId.TryDecode(id, out var owner, out _)) id = owner;
        else if (VirtualItemId.TryDecode(id, out var listId, out var index))
        {
            if (FindElement(listId) is ScrollViewElement list and IAccessibleItemSource source
                && index >= 0 && index < source.ItemCount)
            {
                var (top, height) = source.ItemBounds(index) ?? (index * source.ItemHeight, source.ItemHeight);
                list.ScrollRangeIntoView(top, height);
            }
            id = listId;
        }
        if (FindElement(id) is not { } element) return;
        // Layout boxes are unscrolled, so each view reads the element's box in its own content space.
        var box = element.Layout.AbsoluteBorderBox;
        for (var parent = element.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is not ScrollViewElement view) continue;
            view.ScrollIntoView(element);
            view.ScrollRangeIntoViewHorizontal(box.X - view.Layout.AbsoluteContentBox.X, box.Width);
        }
    });

    // ──── Extra actions the Windows UIA host routes through the same queue.

    /// <summary>A range's value from assistive technology (AT-SPI's Value.CurrentValue): set
    /// through the element, which clamps it and tells its own change; nothing for a read-only one.</summary>
    public void SetRangeValue(ElementId id, double value) => Enqueue(() =>
    {
        if (FindElement(id) is IRangeValueElement range && !range.IsReadOnly)
            range.SetAccessibleValue(value);
    });

    public void SetValue(ElementId id, string value) => Enqueue(() =>
    {
        if (ToContent(id, AccessibleContentAction.SetValue, value)) return;
        switch (FindElement(id))
        {
            // As a paste over the whole value: undoable, refused where a paste is.
            case TextInputElement input: input.SetValueFromAssistiveTool(value); break;
            case TextAreaElement area: area.SetValueFromAssistiveTool(value); break;
            case RichTextEditorElement editor: editor.SetValueFromAssistiveTool(value); break;
        }
    });

    public void FocusElement(ElementId id) => RequestFocus(id);

    public void RequestFocus(ElementId id) => Enqueue(() =>
    {
        if (ToContent(id, AccessibleContentAction.Focus)) return;
        FindElement(id)?.Focus();
    });
}
