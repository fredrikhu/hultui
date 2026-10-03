using HultUI.Core.Elements;

namespace HultUI.Core.Accessibility;

/// <summary>
/// The <see cref="ElementId"/>s of the nodes an <see cref="IAccessibleContentSource"/>
/// describes, which have no element of their own. Like <see cref="VirtualItemId"/>, whose
/// marker is bit 63: bit 62 marks a content node, and the id packs the owning element's id
/// (low 30 bits) and the node's key (32 bits). So it never collides with a real element or a
/// virtual item, stays the same while the source keeps the node, and routes back to its source.
/// </summary>
public static class ContentNodeId
{
    private const ulong Marker = 1UL << 62;
    private const ulong VirtualItemMarker = 1UL << 63;

    public static ElementId For(ElementId owner, int key)
        => new(Marker | ((owner.Value & 0x3FFF_FFFFUL) << 32) | (uint)key);

    /// <summary>True (with the owner and the key) if the id is a content node's.</summary>
    public static bool TryDecode(ElementId id, out ElementId owner, out int key)
    {
        if ((id.Value & (Marker | VirtualItemMarker)) != Marker)
        {
            owner = default;
            key = 0;
            return false;
        }
        owner = new ElementId((id.Value >> 32) & 0x3FFF_FFFFUL);
        key = (int)(uint)(id.Value & 0xFFFF_FFFFUL);
        return true;
    }
}
