using HultUI.Core.Builders;
using HultUI.Core.Elements;
using HultUI.Testing;
using Xunit;

namespace HultUI.Core.Tests.Elements;

/// <summary>
/// <see cref="Element.ReplaceChildren"/> unmounts the old children that are not in the new list
/// and keeps the ones that are. It asks that of each old child by searching the new list, and
/// marks the new children only when there are many of both (plan 17, reconcile-blueprint-hashset):
/// a builder's <c>.Children(...)</c> on a fresh element has no old children and used to make a
/// set of them anyway, 208 of the 272–288 bytes such a call allocated. With many of both it made
/// the set until plan 17 round 18: about 1 KB each time a list's window moved by a row.
/// </summary>
public class ReplaceChildrenTests
{
    private sealed class Probe : BoxElement
    {
        public int Mounts, Unmounts;
        protected override void OnMounted() => Mounts++;
        protected override void OnUnmounted() => Unmounts++;
    }

    [Fact]
    public void OnAParentWithNoChildren_AllocatesOnlyItsChildList()
    {
        // Fresh parents and children for every call of every round: the call under test is the
        // first ReplaceChildren a parent sees, as a builder's .Children(...) is.
        const int calls = 64;
        var parents = new BoxElement[Allocations.MaxRounds * calls];
        var kids = new Element[parents.Length][];
        for (int i = 0; i < parents.Length; i++)
        {
            parents[i] = Box.New();
            kids[i] = [Box.New(), Box.New(), Box.New()];
        }
        int next = 0;

        double perCall = Allocations.PerCall(calls, () => { parents[next].ReplaceChildren(kids[next]); next++; }, goal: 88);

        // The child list (40 B: a List<Element> and the paint order it keeps for its children)
        // and its array of three (48 B), and nothing else.
        Assert.True(perCall <= 88, $"ReplaceChildren of three on an empty parent allocated {perCall:0} B a call");
    }

    /// <summary>A list's window moved by a row: 42 children, 41 of them staying. Asking which stay
    /// allocates nothing; the set it made was ~1.4 KB.</summary>
    [Fact]
    public void ManyChildren_MovedByOne_AllocateNothing()
    {
        var all = new Probe[43];
        for (int i = 0; i < all.Length; i++) all[i] = new Probe();
        Element[] first = all[..42], second = all[1..];
        var parent = Box.New().Children(first);
        var root = Box.New().Children(parent);
        void Round()
        {
            parent.ReplaceChildren(second);
            parent.ReplaceChildren(first);
        }
        Round();

        long bytes = Allocations.Least(Round);

        Assert.Equal(0, bytes);
        Assert.Equal<Element>(first, parent.Children);
        // The 41 that stayed were never let go; the two at the ends came and went each time.
        for (int i = 1; i < 42; i++) Assert.Equal(0, all[i].Unmounts);
        Assert.True(all[0].Unmounts > 0 && all[42].Unmounts > 0);
        GC.KeepAlive(root);
    }

    [Theory]
    [InlineData(3, 4)]     // 12 pairs: searched
    [InlineData(8, 8)]     // 64: searched, the most that is
    [InlineData(9, 8)]     // 72: marked
    [InlineData(40, 30)]
    public void ChildrenThatStay_AreNotUnmounted_AndTheOthersAre(int oldCount, int newCount)
    {
        var old = new Probe[oldCount];
        for (int i = 0; i < old.Length; i++) old[i] = new Probe();
        var parent = Box.New().Children(old);
        var root = Box.New().Children(parent);

        // Every other old child stays, in reverse order, among fresh ones. All of them probes,
        // passed as a Probe[]: an array of a derived type, as an app's BoxElement[] is, which a
        // search must read as Element[] without a type check failing.
        var next = new List<Probe>();
        for (int i = old.Length - 1; i >= 0 && next.Count < newCount; i -= 2) next.Add(old[i]);
        var fresh = new List<Probe>();
        while (next.Count < newCount)
        {
            var p = new Probe();
            fresh.Add(p);
            next.Insert(next.Count / 2, p);
        }

        Probe[] replacement = [.. next];
        parent.ReplaceChildren(replacement);

        Assert.Equal<Element>(next, parent.Children);
        foreach (var child in old)
        {
            bool stays = next.Contains(child);
            Assert.Equal(stays ? 0 : 1, child.Unmounts);
            Assert.Equal(1, child.Mounts);
            Assert.Same(stays ? parent : null, child.Parent);
        }
        foreach (var p in fresh)
        {
            Assert.Equal(1, p.Mounts);
            Assert.Same(parent, p.Parent);
        }
        GC.KeepAlive(root);
    }
}
