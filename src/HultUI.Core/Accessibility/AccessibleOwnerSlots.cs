using HultUI.Core.Elements;

namespace HultUI.Core.Accessibility;

/// <summary>
/// Small, stable numbers for the elements that own synthetic accessibility nodes (a virtual
/// list's items, an <see cref="IAccessibleContentSource"/>'s nodes), packed into those nodes'
/// ids in place of the owner's own id, which does not fit.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> A synthetic id has room for 30 or 31 bits of owner beside its marker and
/// its 32-bit key. Element ids come from one counter that every element ever built draws from,
/// the twins each rebuild constructs included, so in an application that runs for long enough
/// an owner's id no longer fits, and the truncated id names some other element. Measured
/// 2026-10-03: Vesper Commander builds about 470 elements a key press, which passes 2^30 in
/// about eighty days of ordinary use, and Junction, Aperture or a playing video build next to
/// none while idle.</para>
/// <para><b>How.</b> The owners themselves are few: the live elements, not the twins, and only
/// those that publish nodes. Each gets a slot from a counter of its own the first time it is
/// asked for one, and gives it back when it unmounts. Slots are never reused, so an id kept
/// from an owner that has gone resolves to nothing, as a removed node should, rather than to a
/// newer element. 2^30 owners in one process would exhaust them; nothing comes near.</para>
/// <para>Thread-safe: the Windows bridge asks for virtual items' ids on UI Automation's
/// threads.</para>
/// </remarks>
internal static class AccessibleOwnerSlots
{
    /// <summary>The largest slot: the narrower of the two ids' owner fields (30 bits).</summary>
    internal const uint MaxSlot = (1u << 30) - 1;

    private static readonly object Gate = new();
    private static readonly Dictionary<ulong, uint> SlotOfOwner = new();
    private static readonly Dictionary<uint, ulong> OwnerOfSlot = new();
    private static uint _last;

    /// <summary>Whether any owner has ever taken a slot: lets every unmount in an application
    /// that publishes no such nodes skip the lock.</summary>
    private static volatile bool _any;

    /// <summary><paramref name="owner"/>'s slot, taking one if it has none.</summary>
    /// <exception cref="InvalidOperationException">2^30 owners have taken slots in this process.</exception>
    public static uint SlotOf(ElementId owner)
    {
        lock (Gate)
        {
            if (SlotOfOwner.TryGetValue(owner.Value, out var slot)) return slot;
            if (_last == MaxSlot)
                throw new InvalidOperationException("Every accessibility owner slot has been used in this process.");
            slot = ++_last;
            SlotOfOwner[owner.Value] = slot;
            OwnerOfSlot[slot] = owner.Value;
            _any = true;
            return slot;
        }
    }

    /// <summary>The owner holding <paramref name="slot"/>; false once it has given the slot back
    /// (or never held it).</summary>
    public static bool TryOwnerOf(uint slot, out ElementId owner)
    {
        lock (Gate)
        {
            if (OwnerOfSlot.TryGetValue(slot, out var value))
            {
                owner = new ElementId(value);
                return true;
            }
        }
        owner = default;
        return false;
    }

    /// <summary><paramref name="owner"/> has unmounted: its slot goes, and its nodes' ids with it.</summary>
    public static void Release(ElementId owner)
    {
        if (!_any) return;
        lock (Gate)
        {
            if (SlotOfOwner.Remove(owner.Value, out var slot)) OwnerOfSlot.Remove(slot);
        }
    }
}
