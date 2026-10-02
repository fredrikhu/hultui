using HultUI.Core.Events;

namespace HultUI.WebView;

/// <summary>Which keys an application gets a say in before a page does.</summary>
public static class WebKeys
{
    /// <summary>
    /// Ctrl or Alt held, or a function key, and not a modifier on its own. Typing, arrows,
    /// Escape and Tab stay the page's: an application's arrow handler must not scroll a list
    /// behind the page while the user moves a caret in it. The same rule WebView2's engine
    /// offers keys by.
    /// </summary>
    public static bool IsShortcut(KeyCode key, KeyModifiers modifiers) =>
        !IsModifier(key) && ((modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt)) != 0 || key is >= KeyCode.F1 and <= KeyCode.F12);

    private static bool IsModifier(KeyCode key) =>
        key is KeyCode.ShiftLeft or KeyCode.ShiftRight or KeyCode.CtrlLeft or KeyCode.CtrlRight
            or KeyCode.AltLeft or KeyCode.AltRight or KeyCode.SuperLeft or KeyCode.SuperRight;
}
