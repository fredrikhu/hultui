using HultUI.Core.Elements;

namespace HultUI.Core.Accessibility;

/// <summary>
/// Where <see cref="Element.Announce"/> goes: the window holding the element, which hands it to
/// its accessibility bridge once the element's node is in the tree it has applied.
/// </summary>
internal interface IAccessibilityAnnouncer
{
    void Announce(Element source, string text, AnnouncementPoliteness politeness);
}
