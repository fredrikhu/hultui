using HultUI.Core.Elements;

namespace HultUI.Core.Events;

public sealed class FocusManager
{
    public Element? FocusedElement { get; private set; }

    public void SetFocus(Element? element)
    {
        if (ReferenceEquals(FocusedElement, element))
            return;

        var previous = FocusedElement;
        FocusedElement = element;

        if (previous is not null)
            EventDispatcher.DispatchDirect(new BlurEvent(), previous);

        if (element is not null)
            EventDispatcher.DispatchDirect(new FocusEvent(), element);
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
