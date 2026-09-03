using HultUI.Core.Platform;
using HultUI.Core.Primitives;
using HultUI.WinForms.Interop;

namespace HultUI.WinForms.Services;

/// <summary>
/// Drives the Windows IME for a hosted control, through IMM32.
/// </summary>
/// <remarks>
/// The framework calls <see cref="StartTextInput"/> when a text element takes focus and
/// <see cref="StopTextInput"/> when it loses it. Both matter: an IME left armed over
/// non-text UI consumes dead keys and the IME-toggle hotkey, so arming is not something
/// to do once at start-up.
/// </remarks>
internal sealed class WinFormsTextInputHost : ITextInputHost, IDisposable
{
    private readonly Func<nint> _hwnd;
    private readonly Func<float> _scale;
    private Imm.Association? _association;

    private Rect _lastRect;
    private int _lastCursorOffset;
    private bool _hasPushedRect;

    internal WinFormsTextInputHost(Func<nint> hwnd, Func<float> scale)
    {
        _hwnd = hwnd;
        _scale = scale;
    }

    public void StartTextInput()
    {
        var hwnd = _hwnd();
        if (hwnd == IntPtr.Zero) return;
        _association ??= new Imm.Association(hwnd);
        _association.Arm();
    }

    public void StopTextInput()
    {
        _association?.Disarm();
        _hasPushedRect = false;
    }

    /// <summary>
    /// Positions the IME's candidate window at the caret.
    /// </summary>
    /// <remarks>
    /// The framework calls this on every frame a text element is focused, and each call
    /// reaches into IMM32 — which makes the candidate window flicker if it is forwarded
    /// unchanged. Same guard as the SDL host: only push when something moved.
    /// </remarks>
    public void SetInputArea(Rect caretRect, int cursorOffset)
    {
        var hwnd = _hwnd();
        if (hwnd == IntPtr.Zero) return;
        if (_hasPushedRect && cursorOffset == _lastCursorOffset && caretRect.Equals(_lastRect))
            return;

        _lastRect = caretRect;
        _lastCursorOffset = cursorOffset;
        _hasPushedRect = true;

        // The framework works in layout units; IMM32 wants client pixels.
        float scale = _scale();
        Imm.SetCompositionPosition(hwnd,
            (int)MathF.Round(caretRect.X * scale),
            (int)MathF.Round(caretRect.Y * scale),
            (int)MathF.Round(caretRect.Width * scale),
            (int)MathF.Round(caretRect.Height * scale));
    }

    public void ClearComposition()
    {
        var hwnd = _hwnd();
        if (hwnd != IntPtr.Zero) Imm.CancelComposition(hwnd);
    }

    public void Dispose()
    {
        _association?.Dispose();
        _association = null;
    }
}
