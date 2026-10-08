using HultUI.Core.Elements;

namespace HultUI.Core.Accessibility;

/// <summary>
/// Encodes and decodes the synthetic <see cref="ElementId"/>s used for the
/// accessibility nodes of virtualized-list items, which have no backing element.
/// </summary>
/// <remarks>
/// Real ids come from a small incrementing counter, always well under 2^62. Setting the top
/// bit marks an id as a virtual item and packs the owning list's slot
/// (<see cref="AccessibleOwnerSlots"/>, in the 31 bits below the marker) and the item index
/// (32 bits), so the id never collides with a real element, stays stable across rebuilds and
/// scrolls while the list stays mounted, and can be routed back to its list for activation.
/// The list's own id is not packed: it can outgrow 31 bits in a long-running application.
/// </remarks>
public static class VirtualItemId
{
    private const ulong Marker = 1UL << 63;

    public static ElementId For(ElementId listId, int index)
        => ForSlot(AccessibleOwnerSlots.SlotOf(listId), index);

    /// <summary>The id of item <paramref name="index"/> of the list holding <paramref name="slot"/>:
    /// for a caller making every item's id, which looks the slot up once.</summary>
    internal static ElementId ForSlot(uint slot, int index)
        => new(Marker | ((ulong)slot << 32) | (uint)index);

    /// <summary>True (with the decoded owner + index) if the id is a virtual item. The owner is
    /// <c>default</c> when its list has unmounted since: the item is gone.</summary>
    public static bool TryDecode(ElementId id, out ElementId listId, out int index)
    {
        if ((id.Value & Marker) == 0)
        {
            listId = default;
            index = 0;
            return false;
        }
        AccessibleOwnerSlots.TryOwnerOf((uint)((id.Value >> 32) & 0x7FFF_FFFFUL), out listId);
        index = (int)(uint)(id.Value & 0xFFFF_FFFFUL);
        return true;
    }
}
