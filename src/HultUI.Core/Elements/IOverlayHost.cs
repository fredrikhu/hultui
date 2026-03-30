using HultUI.Core.Styling;

namespace HultUI.Core.Elements;

public interface IOverlayHost
{
    OverlayHandle ShowOverlay(Element content, float x, float y, bool dismissOnOutsideClick = true, bool hitTestable = true);
    void CloseOverlay(OverlayHandle handle);
    void UpdateOverlayPosition(OverlayHandle handle, float x, float y);
    Theme? ActiveTheme { get; }
}
