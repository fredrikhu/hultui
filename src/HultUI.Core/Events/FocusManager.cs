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
}
