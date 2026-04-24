using HultUI.Core.Elements;

namespace HultUI.Core.Accessibility;

/// <summary>
/// Single-subscriber hook elements use to announce property changes relevant
/// to accessibility (checkbox toggled, text value changed, etc.) without
/// requiring a full tree rebuild.
///
/// The <see cref="Window"/>-owned accessibility bridge installs itself as the
/// observer on the root element; descendants walk up the parent chain to find it.
/// Property names come from <see cref="AccessibilityProperties"/>.
/// </summary>
public interface IElementPropertyObserver
{
    void OnElementPropertyChanged(Element element, string propertyName);
}
