using HultUI.Core.Platform;
using HultUI.Core.Primitives;

namespace HultUI.WinForms.Services;

/// <summary>
/// Placeholder text-input host. <b>Typing does not reach a hosted HultUI control yet</b>
/// — keyboard, focus traversal and IME are the next slice of this backend, and this
/// exists so the platform contract is satisfied in the meantime rather than to pretend
/// the work is done.
/// </summary>
/// <remarks>
/// What it will become: <see cref="StartTextInput"/> and <see cref="StopTextInput"/>
/// associating and dissociating an IMM32 context on the control's handle, and
/// <see cref="SetInputArea"/> positioning the candidate window at the caret, so a
/// Japanese or Chinese composition appears where the text is rather than at the
/// control's corner. Leaving the IME armed over non-text UI makes it eat dead keys and
/// IME-toggle hotkeys, which is why the framework starts and stops it on focus rather
/// than leaving it on.
/// </remarks>
internal sealed class WinFormsTextInputHost : ITextInputHost
{
    public void StartTextInput() { }
    public void StopTextInput() { }
    public void SetInputArea(Rect caretRect, int cursorOffset) { }
    public void ClearComposition() { }
}
