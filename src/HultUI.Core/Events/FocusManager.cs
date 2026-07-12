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
    /// Move focus to the next focusable element in document order (Tab).
    /// </summary>
    public void MoveFocusNext(Element root)
    {
        var next = FocusTraversal.FindNext(root, FocusedElement);
        if (next is not null)
            SetFocus(next);
    }

    /// <summary>
    /// Move focus to the previous focusable element in document order (Shift+Tab).
    /// </summary>
    public void MoveFocusPrevious(Element root)
    {
        var prev = FocusTraversal.FindPrevious(root, FocusedElement);
        if (prev is not null)
            SetFocus(prev);
    }
}
