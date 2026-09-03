using HultUI.Core.Elements;

namespace HultUI.Core.Events;

public sealed class FocusManager
{
    public Element? FocusedElement { get; private set; }

    /// <summary>
    /// Called after focus changes. The Window subscribes to provide
    /// platform services (e.g. clipboard) to the newly focused element.
    /// </summary>
    public Action<Element?>? FocusChanged { get; set; }

    public void SetFocus(Element? element)
    {
        if (ReferenceEquals(FocusedElement, element))
            return;

        var previous = FocusedElement;
        FocusedElement = element;

        if (previous is not null)
        {
            previous.SetInteractionFocused(false);
            EventDispatcher.DispatchDirect(new BlurEvent(), previous);
        }

        if (element is not null)
        {
            element.SetInteractionFocused(true);
            EventDispatcher.DispatchDirect(new FocusEvent(), element);
        }

        FocusChanged?.Invoke(element);
    }

    public void ClearFocus() => SetFocus(null);

    /// <summary>
    /// Move focus to the next focusable element in document order (Tab). Returns false
    /// when focus did not move: there was nothing focusable, or the end was reached and
    /// <paramref name="wrap"/> is false.
    /// </summary>
    /// <param name="wrap">
    /// Whether the end returns to the first element. True for a window, where Tab has
    /// nowhere else to go. False for a tree embedded in a host that has its own tab
    /// order after this one — a false return is the host's cue to move on.
    /// </param>
    public bool MoveFocusNext(Element root, bool wrap = true)
    {
        var next = FocusTraversal.FindNext(root, FocusedElement, wrap);
        if (next is null) return false;
        SetFocus(next);
        return true;
    }

    /// <summary>
    /// Move focus to the previous focusable element in document order (Shift+Tab).
    /// </summary>
    /// <inheritdoc cref="MoveFocusNext"/>
    public bool MoveFocusPrevious(Element root, bool wrap = true)
    {
        var prev = FocusTraversal.FindPrevious(root, FocusedElement, wrap);
        if (prev is null) return false;
        SetFocus(prev);
        return true;
    }
}
