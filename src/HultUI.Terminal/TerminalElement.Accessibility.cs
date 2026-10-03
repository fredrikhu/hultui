using HultUI.Core.Accessibility;
using HultUI.Core.Animation;

namespace HultUI.Terminal;

/// <summary>
/// The terminal for a screen reader: a document named after the program's title, whose value is
/// the visible screen (<see cref="TerminalController.ReadScreen"/>). Windows offers the value
/// through the read-only Value pattern; a reader reads it when it asks.
/// </summary>
/// <remarks>
/// A change is told to the reader once the output has paused for <see cref="ScreenSettle"/>, or
/// after <see cref="ScreenSettleLimit"/> of unbroken output: one update per burst, not one a
/// frame. Nothing is timed while no assistive tool listens. Reading line by line and hearing new
/// output as it arrives need the text range model and announcements (seams A1 and A2).
/// </remarks>
public sealed partial class TerminalElement
{
    internal static readonly TimeSpan ScreenSettle = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan ScreenSettleLimit = TimeSpan.FromSeconds(2);

    // Two clocks: one restarted by every change, which runs out once the output pauses, and one
    // started by a burst's first change, which runs out after the limit however it continues.
    private TweenHandle _settleTimer, _limitTimer;
    private bool _settlePending, _limitPending;

    internal override string? AccessibleValue => _controller?.ReadScreen();

    /// <summary>The screen changed: tell the reader once it settles.</summary>
    private void NoteScreenChanged()
    {
        if (!AccessibilityClients.Active) return;
        if (_settlePending) _settleTimer.Cancel();
        _settlePending = true;
        _settleTimer = Animate.Value(0f, 1f, static _ => { })
            .Duration((float)ScreenSettle.TotalSeconds)
            .OnComplete(ScreenSettled)
            .Start();

        if (_limitPending) return;
        _limitPending = true;
        _limitTimer = Animate.Value(0f, 1f, static _ => { })
            .Duration((float)ScreenSettleLimit.TotalSeconds)
            .OnComplete(ScreenSettled)
            .Start();
    }

    private void ScreenSettled()
    {
        StopScreenWait();
        if (IsHosted) NotifyAccessibilityPropertyChanged(AccessibilityProperties.Value);
    }

    private void StopScreenWait()
    {
        if (_settlePending) _settleTimer.Cancel();
        if (_limitPending) _limitTimer.Cancel();
        _settlePending = _limitPending = false;
    }
}
