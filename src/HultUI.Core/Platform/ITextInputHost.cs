using HultUI.Core.Primitives;

namespace HultUI.Core.Platform;

/// <summary>
/// Platform hook used by focused text inputs to drive the OS IME.
/// The currently focused element calls <see cref="SetInputArea"/> whenever
/// the caret moves so the OS can position its candidate window, and
/// <see cref="ClearComposition"/> on blur so in-flight preedits are cancelled.
/// </summary>
public interface ITextInputHost
{
    /// <summary>
    /// Enables OS text input routing for the window. Must be paired with
    /// <see cref="StopTextInput"/> when focus leaves text-editable elements —
    /// otherwise the IME stays armed and dead keys / IME-toggle hotkeys are
    /// consumed even over non-text UI (notably on Windows IMM/TSF).
    /// </summary>
    void StartTextInput();

    /// <summary>
    /// Disables OS text input routing for the window.
    /// </summary>
    void StopTextInput();

    /// <summary>
    /// Reports the caret rectangle (in window logical coordinates) to the OS IME.
    /// <paramref name="cursorOffset"/> is the cursor's horizontal offset inside the rect.
    /// </summary>
    void SetInputArea(Rect caretRect, int cursorOffset);

    /// <summary>
    /// Cancels any in-progress IME composition. Called when focus leaves a
    /// text input while a preedit is active.
    /// </summary>
    void ClearComposition();
}
