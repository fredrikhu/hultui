using HultUI.Core.Elements;

namespace HultUI.Core.Accessibility;

/// <summary>
/// The <see cref="ElementId"/>s of the nodes an <see cref="IAccessibleContentSource"/>
/// describes, which have no element of their own. Like <see cref="VirtualItemId"/>, whose
/// marker is bit 63: bit 62 marks a content node, and the id packs the owning element's slot
/// (<see cref="AccessibleOwnerSlots"/>, 30 bits) and the node's key (32 bits). So it never
/// collides with a real element or a virtual item, stays the same while the source keeps the
/// node and stays mounted, and routes back to its source. The owner's own id is not packed:
/// it can outgrow 30 bits in a long-running application.
/// </summary>
public static class ContentNodeId
{
    private const ulong Marker = 1UL << 62;
    private const ulong VirtualItemMarker = 1UL << 63;

    public static ElementId For(ElementId owner, int key)
        => new(Marker | ((ulong)AccessibleOwnerSlots.SlotOf(owner) << 32) | (uint)key);

    /// <summary>True (with the owner and the key) if the id is a content node's. The owner is
    /// <c>default</c> when it has unmounted since: the node is gone.</summary>
    public static bool TryDecode(ElementId id, out ElementId owner, out int key)
    {
        if ((id.Value & (Marker | VirtualItemMarker)) != Marker)
        {
            owner = default;
            key = 0;
            return false;
        }
        AccessibleOwnerSlots.TryOwnerOf((uint)((id.Value >> 32) & 0x3FFF_FFFFUL), out owner);
        key = (int)(uint)(id.Value & 0xFFFF_FFFFUL);
        return true;
    }
}
