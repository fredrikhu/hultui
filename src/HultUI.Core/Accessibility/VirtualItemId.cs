using HultUI.Core.Elements;

namespace HultUI.Core.Accessibility;

/// <summary>
/// Encodes and decodes the synthetic <see cref="ElementId"/>s used for the
/// accessibility nodes of virtualized-list items, which have no backing element.
/// </summary>
/// <remarks>
/// Real ids come from a small incrementing counter, always well under 2^63.
/// Setting the top bit marks an id as a virtual item and packs the owning list's
/// id (low 31 bits) and the item index (32 bits), so the id never collides with a
/// real element, stays stable across rebuilds/scrolls, and can be routed back to
/// its list for activation.
/// </remarks>
public static class VirtualItemId
{
    private const ulong Marker = 1UL << 63;

    public static ElementId For(ElementId listId, int index)
        => new(Marker | ((listId.Value & 0x7FFF_FFFFUL) << 32) | (uint)index);

    /// <summary>True (with the decoded owner + index) if the id is a virtual item.</summary>
    public static bool TryDecode(ElementId id, out ElementId listId, out int index)
    {
        if ((id.Value & Marker) == 0)
        {
            listId = default;
            index = 0;
            return false;
        }
        listId = new ElementId((id.Value >> 32) & 0x7FFF_FFFFUL);
        index = (int)(uint)(id.Value & 0xFFFF_FFFFUL);
        return true;
    }
}
