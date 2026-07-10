using System.Collections.Concurrent;
using System.Runtime.Versioning;
using HultUI.Core.Accessibility;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Platform.Accessibility.Windows;

namespace HultUI;

/// <summary>
/// Hooks the Windows UIA bridge into our SDL-based event loop.
///
/// UIA clients call provider methods from worker threads, which means we must
/// either snapshot everything (we do, for reads) or marshal writes onto the
/// UI thread. This adapter uses a <see cref="ConcurrentQueue{T}"/> drained at
/// the start of each frame, so element mutations happen on the same thread as
/// the render loop. (<c>WM_GETOBJECT</c> is serviced by the bridge's own
/// window-procedure subclass, so no SDL message hook is involved.)
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class Sdl3UiaHostBridge : IUiaHostBridge, IDisposable
{
    private readonly Window _window;
    private readonly ConcurrentQueue<Action> _pendingActions = new();

    public Sdl3UiaHostBridge(Window window)
    {
        _window = window;
    }

    public float ContentScale => _window.PlatformWindow.DisplayScale;

    /// <summary>
    /// True when a UIA action is queued but not yet run. The render loop folds
    /// this into <c>NeedsRender</c> so an action posted from a UIA worker thread
    /// wakes an otherwise-idle app to drain it (see <see cref="DrainPendingActions"/>).
    /// </summary>
    public bool HasPendingActions => !_pendingActions.IsEmpty;

    /// <summary>Called by <see cref="Window"/> once per frame to run queued actions on the UI thread.</summary>
    public void DrainPendingActions()
    {
        while (_pendingActions.TryDequeue(out var action))
        {
            try { action(); }
            catch { /* never let AT clients crash the UI loop */ }
        }
    }

    // ──── Actions — enqueue for UI-thread drain

    public void InvokeElement(ElementId id) => _pendingActions.Enqueue(() =>
    {
        // A virtualized-list item has no element of its own; route its activation
        // back to the owning list by index (works even for off-screen items).
        if (VirtualItemId.TryDecode(id, out var listId, out var index))
        {
            if (GetElementOrNull(listId) is IAccessibleItemSource source)
                source.ActivateItem(index);
            return;
        }
        EventDispatcher.DispatchDirect(new ClickEvent { Position = default }, GetElementOrNull(id) ?? Throw());
    });

    public void ToggleElement(ElementId id) => _pendingActions.Enqueue(() =>
    {
        if (GetElementOrNull(id) is CheckboxElement cb)
        {
            cb.IsChecked = !cb.IsChecked;
            cb.OnChanged?.Invoke(cb.IsChecked);
        }
    });

    public void SelectElement(ElementId id) => _pendingActions.Enqueue(() =>
    {
        if (GetElementOrNull(id) is RadioButtonElement rb)
        {
            if (!rb.IsChecked)
            {
                rb.IsChecked = true;
                rb.OnChanged?.Invoke(true);
            }
        }
    });

    public void SetValue(ElementId id, string value) => _pendingActions.Enqueue(() =>
    {
        switch (GetElementOrNull(id))
        {
            case TextInputElement input: input.Text = value; input.TextChanged?.Invoke(value); break;
            case TextAreaElement area: area.Text = value; area.TextChanged?.Invoke(value); break;
        }
    });

    public void RequestFocus(ElementId id) => _pendingActions.Enqueue(() =>
    {
        GetElementOrNull(id)?.Focus();
    });

    // ──── Reads — safe to run on the calling thread since we only touch
    //      simple CLR properties that don't mutate collections.

    public string? GetValue(ElementId id) => GetElementOrNull(id) switch
    {
        TextInputElement input => input.Text,
        TextAreaElement area => area.Text,
        _ => null,
    };

    public bool? GetToggleState(ElementId id) => GetElementOrNull(id) switch
    {
        CheckboxElement cb => cb.IsChecked,
        RadioButtonElement rb => rb.IsChecked,
        _ => null,
    };

    private Element? GetElementOrNull(ElementId id) => _window.FindElement(id);

    private static Element Throw() => throw new InvalidOperationException("Element id no longer in tree");

    public void Dispose()
    {
        _pendingActions.Clear();
    }
}
