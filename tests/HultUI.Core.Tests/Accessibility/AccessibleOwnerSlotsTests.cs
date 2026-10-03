using HultUI.Core.Accessibility;
using HultUI.Core.Builders;
using HultUI.Core.Elements;
using Xunit;

namespace HultUI.Core.Tests.Accessibility;

/// <summary>
/// Synthetic accessibility ids (virtual list items, described content) carry their owner as a
/// slot, not as the owner's own id. The old ids packed the owner's id into 30 or 31 bits, and
/// element ids come from a counter that every element ever built draws from: in an application
/// running for months (Vesper builds about 470 elements a key press) an owner's id outgrew the
/// field and its nodes' ids named some other element.
/// </summary>
/// <remarks>
/// The slot table is the process's. Owners made up here are far above any real element's id,
/// so these tests never touch a slot that another test's elements hold.
/// </remarks>
public class AccessibleOwnerSlotsTests
{
    private static readonly ulong Far = 1UL << 50;

    [Fact]
    public void AnOwnerWhoseIdOutgrewTheField_StillDecodesToItself()
    {
        var list = new ElementId(Far | 5);
        var page = new ElementId(Far | 7);

        Assert.True(VirtualItemId.TryDecode(VirtualItemId.For(list, 9), out var l, out var i));
        Assert.Equal((list, 9), (l, i));
        Assert.True(ContentNodeId.TryDecode(ContentNodeId.For(page, -3), out var p, out var k));
        Assert.Equal((page, -3), (p, k));
    }

    [Fact]
    public void OwnersTheOldFieldWidthApart_DoNotCollide()
    {
        // With 30 bits of owner (content) and 31 (items), these pairs named the same node.
        var a = new ElementId(Far + 77);
        var b = new ElementId(Far + 77 + (1UL << 30));
        var c = new ElementId(Far + 77 + (1UL << 31));

        Assert.NotEqual(ContentNodeId.For(a, 1), ContentNodeId.For(b, 1));
        Assert.NotEqual(VirtualItemId.For(a, 1), VirtualItemId.For(c, 1));
        Assert.True(ContentNodeId.TryDecode(ContentNodeId.For(b, 1), out var owner, out _));
        Assert.Equal(b, owner);
        Assert.True(VirtualItemId.TryDecode(VirtualItemId.For(c, 1), out owner, out _));
        Assert.Equal(c, owner);
    }

    [Fact]
    public void AnOwnersIds_StayTheSame_WhileItHoldsItsSlot()
    {
        var owner = new ElementId(Far + 101);
        Assert.Equal(VirtualItemId.For(owner, 3), VirtualItemId.For(owner, 3));
        Assert.Equal(ContentNodeId.For(owner, 3), ContentNodeId.For(owner, 3));
    }

    [Fact]
    public void AGoneOwnersIds_ResolveToNothing_AndItsSlotGoesToNobodyElse()
    {
        var gone = new ElementId(Far + 201);
        var id = VirtualItemId.For(gone, 4);

        AccessibleOwnerSlots.Release(gone);

        // Still an item's id, but of nothing: the bridges answer "not available".
        Assert.True(VirtualItemId.TryDecode(id, out var owner, out _));
        Assert.Equal(default, owner);
        // Never handed to a newer owner, nor back to the same one should it return.
        Assert.NotEqual(id, VirtualItemId.For(new ElementId(Far + 202), 4));
        Assert.NotEqual(id, VirtualItemId.For(gone, 4));
    }

    [Fact]
    public void UnmountingAnOwner_GivesItsSlotBack()
    {
        var owner = Box.New();
        var root = Box.New().Children(owner);
        var item = VirtualItemId.For(owner.Id, 2);
        var node = ContentNodeId.For(owner.Id, 2);
        Assert.True(VirtualItemId.TryDecode(item, out var o, out _));
        Assert.Equal(owner.Id, o);

        root.RemoveChild(owner);

        Assert.True(VirtualItemId.TryDecode(item, out o, out _));
        Assert.Equal(default, o);
        Assert.True(ContentNodeId.TryDecode(node, out o, out _));
        Assert.Equal(default, o);
    }

    [Fact]
    public void ManyThreadsAskingAtOnce_GetOneId()
    {
        // The Windows bridge asks for items' ids on UI Automation's threads.
        var owner = new ElementId(Far + 301);
        var ids = new ElementId[64];
        Parallel.For(0, ids.Length, i => ids[i] = VirtualItemId.For(owner, 1));
        Assert.Single(ids.Distinct());
    }
}
