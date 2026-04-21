using HultUI.Core.Platform;
using HultUI.Core.Primitives;

namespace HultUI.Platform.SDL3;

public sealed class SDL3TextInputHost : ITextInputHost
{
    private readonly SDL3Window _window;

    public SDL3TextInputHost(SDL3Window window)
    {
        _window = window;
    }

    public void SetInputArea(Rect caretRect, int cursorOffset)
        => _window.SetTextInputArea(caretRect, cursorOffset);

    public void ClearComposition()
        => _window.ClearComposition();
}
