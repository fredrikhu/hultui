using HultUI.Core.Platform;
using HultUI.Core.Primitives;

namespace HultUI.Platform.SDL3;

public sealed class SDL3TextInputHost : ITextInputHost
{
    private readonly SDL3Window _window;

    // Last rect/offset pushed to SDL. SetInputArea is called every paint frame
    // while a text input is focused; forwarding each call reaches into IMM/TSF
    // on Windows (and DBus on Linux) and can make the candidate window flicker.
    private Rect _lastRect;
    private int _lastCursorOffset;
    private bool _hasPushedRect;

    public SDL3TextInputHost(SDL3Window window)
    {
        _window = window;
    }

    public void StartTextInput() => _window.StartTextInput();

    public void StopTextInput()
    {
        _window.StopTextInput();
        _hasPushedRect = false;
    }

    public void SetInputArea(Rect caretRect, int cursorOffset)
    {
        if (_hasPushedRect && cursorOffset == _lastCursorOffset && caretRect.Equals(_lastRect))
            return;
        _lastRect = caretRect;
        _lastCursorOffset = cursorOffset;
        _hasPushedRect = true;
        _window.SetTextInputArea(caretRect, cursorOffset);
    }

    public void ClearComposition() => _window.ClearComposition();
}
