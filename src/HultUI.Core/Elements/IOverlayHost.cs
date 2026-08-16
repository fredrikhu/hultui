using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public interface IOverlayHost
{
    /// <param name="onDismissed">Runs when the host dismisses the overlay on an outside
    /// click. An explicit <see cref="CloseOverlay"/> does not raise it — the callback
    /// exists so an owner tracking open state is told about the one close it did not
    /// initiate.</param>
    OverlayHandle ShowOverlay(Element content, float x, float y, bool dismissOnOutsideClick = true, bool hitTestable = true, Action? onDismissed = null);
    void CloseOverlay(OverlayHandle handle);
    void UpdateOverlayPosition(OverlayHandle handle, float x, float y);
    Theme? ActiveTheme { get; }
}
