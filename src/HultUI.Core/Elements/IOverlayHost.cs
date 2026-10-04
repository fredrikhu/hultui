using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public interface IOverlayHost
{
    /// <summary>Put <paramref name="content"/> on top of the window at a point in window
    /// coordinates, above everything the normal tree draws.</summary>
    /// <param name="content">What to show. Laid out at its own measured size.</param>
    /// <param name="x">Left edge, in window coordinates.</param>
    /// <param name="y">Top edge, in window coordinates.</param>
    /// <param name="dismissOnOutsideClick">Whether a click outside the overlay closes it.
    /// False for an overlay its owner closes on its own terms.</param>
    /// <param name="hitTestable">Whether the overlay takes pointer input at all. False makes
    /// it decorative — a drag preview or a tooltip that must not swallow the click under it.</param>
    /// <param name="onDismissed">Runs when the host dismisses the overlay on an outside
    /// click. An explicit <see cref="CloseOverlay"/> does not raise it — the callback
    /// exists so an owner tracking open state is told about the one close it did not
    /// initiate.</param>
    /// <param name="modal">Whether the overlay holds the keyboard while it is open: Tab and
    /// Shift+Tab stay inside it, and keys go to it even when the focus is somewhere it does not
    /// reach — so nothing behind a dialog acts on a key pressed while the dialog is up.</param>
    OverlayHandle ShowOverlay(Element content, float x, float y, bool dismissOnOutsideClick = true, bool hitTestable = true, Action? onDismissed = null, bool modal = false);
    void CloseOverlay(OverlayHandle handle);
    void UpdateOverlayPosition(OverlayHandle handle, float x, float y);
    Theme? ActiveTheme { get; }
}
