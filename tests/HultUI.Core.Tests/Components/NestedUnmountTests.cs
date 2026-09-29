using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;
using Xunit;

namespace HultUI.Core.Tests.Components;

/// <summary>
/// A component hosted somewhere under an element that leaves the tree has left it too.
/// Removal used to unmount only the element removed, so a <see cref="ComponentHost"/> one
/// level down kept its build Effect: it stayed subscribed to every signal its Build read,
/// rebuilt a tree nobody could see on each write, and never heard <c>OnUnmount</c>.
/// </summary>
[Collection("Reactive")]
public class NestedUnmountTests
{
    private sealed class Probe : Component
    {
        public Signal<int> S { get; } = new(0);
        public int Builds { get; private set; }
        public int Mounts { get; private set; }
        public int Unmounts { get; private set; }

        public override Element Build()
        {
            Builds++;
            return Text.New($"s = {S.Value}");
        }

        protected internal override void OnMount() => Mounts++;
        protected internal override void OnUnmount() => Unmounts++;
    }

    /// <summary>Asserts the probe is detached: no rebuild on a write, OnUnmount ran once.</summary>
    private static void AssertUnmounted(Probe probe)
    {
        int before = probe.Builds;
        probe.S.Value++;
        Assert.Equal(before, probe.Builds);
        Assert.Equal(1, probe.Unmounts);
    }

    /// <summary>Asserts the probe is live: a write rebuilds it, and it was never unmounted.</summary>
    private static void AssertLive(Probe probe)
    {
        int before = probe.Builds;
        probe.S.Value++;
        Assert.Equal(before + 1, probe.Builds);
        Assert.Equal(0, probe.Unmounts);
    }

    private static T Keyed<T>(T element, string key) where T : Element
    {
        element.Key = key;
        return element;
    }

    // ──── Direct removal ────

    [Fact]
    public void RemoveChild_UnmountsAComponentNestedInTheRemovedBox()
    {
        var probe = new Probe();
        var box = Box.New().Children(probe.Mount());
        var root = Box.New().Children(box);
        AssertLive(probe);

        root.RemoveChild(box);

        AssertUnmounted(probe);
    }

    [Fact]
    public void RemoveChild_UnmountsAComponentSeveralLevelsDown()
    {
        var probe = new Probe();
        var box = Box.New().Children(Flex.Column().Children(Box.New().Children(probe.Mount())));
        var root = Box.New().Children(box);

        root.RemoveChild(box);

        AssertUnmounted(probe);
    }

    [Fact]
    public void ReplaceChildren_UnmountsAComponentNestedInADroppedChild()
    {
        var probe = new Probe();
        var box = Box.New().Children(probe.Mount());
        var root = Box.New().Children(box);

        root.ReplaceChildren(Text.New("instead"));

        AssertUnmounted(probe);
    }

    // ──── The reconciler's removals ────

    /// <summary>A parent component that shows the probe's host inside a wrapper, or not.</summary>
    private sealed class Parent(Probe probe, Func<Probe, bool, Element> build) : Component
    {
        public Signal<bool> Show { get; } = new(true);
        public override Element Build() => build(probe, Show.Value);
    }

    [Fact]
    public void TypeChange_UnmountsAComponentInsideTheReplacedElement()
    {
        var probe = new Probe();
        var parent = new Parent(probe, (p, show) => Flex.Column().Children(
            show ? Box.New().Children(p.Mount()) : Text.New("gone")));
        var root = Box.New().Children(parent.Mount());
        AssertLive(probe);

        parent.Show.Value = false;

        AssertUnmounted(probe);
    }

    [Fact]
    public void TypeChangeOfTheBuiltRoot_UnmountsAComponentInsideIt()
    {
        var probe = new Probe();
        var parent = new Parent(probe, (p, show) =>
            show ? Box.New().Children(p.Mount()) : Text.New("gone"));
        var root = Box.New().Children(parent.Mount());

        parent.Show.Value = false;

        AssertUnmounted(probe);
    }

    [Fact]
    public void PrefixSuffixMiddleRemoval_UnmountsAComponentInsideTheRemovedMiddle()
    {
        var probe = new Probe();
        var parent = new Parent(probe, (p, show) => show
            ? Flex.Column().Children(Text.New("a"), Box.New().Children(p.Mount()), Text.New("b"))
            : Flex.Column().Children(Text.New("a"), Text.New("b")));
        var root = Box.New().Children(parent.Mount());

        parent.Show.Value = false;

        AssertUnmounted(probe);
    }

    [Fact]
    public void KeyedDrop_UnmountsAComponentInsideTheDroppedRow()
    {
        var probe = new Probe();
        var parent = new Parent(probe, (p, show) => show
            ? Flex.Column().Children(Keyed(Box.New().Children(p.Mount()), "row"), Keyed(Text.New("t"), "t"))
            : Flex.Column().Children(Keyed(Text.New("t"), "t")));
        var root = Box.New().Children(parent.Mount());

        parent.Show.Value = false;

        AssertUnmounted(probe);
    }

    // ──── What must NOT unmount ────

    /// <summary>
    /// The wrapper changes type, but the new build asks for the same component: the host is
    /// moved into the new wrapper before the old one is removed, and must keep running.
    /// </summary>
    [Fact]
    public void HostMovedToANewWrapper_IsNotUnmountedWithTheOldOne()
    {
        var probe = new Probe();
        var parent = new Parent(probe, (p, show) => show
            ? Box.New().Children(p.Mount())
            : Flex.Row().Children(p.Mount()));
        var root = Box.New().Children(parent.Mount());
        var host = probe.Mount();

        parent.Show.Value = false;

        Assert.Same(host, probe.Mount());
        AssertLive(probe);
    }

    [Fact]
    public void ParentRebuild_PassingTheSameHostThrough_DoesNotUnmount()
    {
        var probe = new Probe();
        var parent = new Parent(probe, (p, show) =>
            Flex.Column().Children(Text.New(show ? "on" : "off"), Box.New().Children(p.Mount())));
        var root = Box.New().Children(parent.Mount());

        parent.Show.Value = false;
        parent.Show.Value = true;

        Assert.Equal(1, probe.Mounts);
        AssertLive(probe);
    }

    /// <summary>
    /// The host is inside the child that leaves, and is one of the children that replace
    /// it: a move out of the leaving subtree, not a removal with it.
    /// </summary>
    [Fact]
    public void ReplaceChildren_MovingAHostOutOfTheLeavingChild_DoesNotUnmountIt()
    {
        var probe = new Probe();
        var host = probe.Mount();
        var wrapper = Box.New().Children(host);
        var root = Box.New().Children(wrapper);

        root.ReplaceChildren(host);

        Assert.Same(root, host.Parent);
        Assert.Equal(1, probe.Mounts);
        AssertLive(probe);
    }

    // ──── Coming back ────

    /// <summary>
    /// An app that keeps a subtree and puts it back — a cached panel — gets a working
    /// component again, not one whose Effect was disposed on the way out.
    /// </summary>
    [Fact]
    public void RemovedSubtreePutBack_RemountsTheNestedComponent()
    {
        var probe = new Probe();
        var box = Box.New().Children(probe.Mount());
        var root = Box.New().Children(box);
        root.RemoveChild(box);
        Assert.Equal(1, probe.Unmounts);

        root.AddChild(box);

        Assert.Equal(2, probe.Mounts);
        int before = probe.Builds;
        probe.S.Value++;
        Assert.Equal(before + 1, probe.Builds);
    }

    /// <summary>
    /// Same, but the kept element is a descendant of the removed subtree and is put back on
    /// its own. It still has its old parent, which is what a move looks like; it is not one.
    /// Each way of adding a child is tried directly under the parent: going through a fresh
    /// wrapper would let the wrapper's own mount repair a child the add had skipped.
    /// </summary>
    [Theory]
    [InlineData("AddChild")]
    [InlineData("InsertChild")]
    [InlineData("ReplaceChildren")]
    public void DescendantOfARemovedSubtreePutBack_RemountsTheNestedComponent(string how)
    {
        var probe = new Probe();
        var panel = Box.New().Children(probe.Mount());
        var wrapper = Box.New().Children(panel);
        var root = Box.New().Children(wrapper);
        root.RemoveChild(wrapper);
        Assert.Equal(1, probe.Unmounts);

        switch (how)
        {
            case "AddChild": root.AddChild(panel); break;
            case "InsertChild": root.InsertChild(0, panel); break;
            default: root.ReplaceChildren(panel); break;
        }

        Assert.Same(root, panel.Parent);
        Assert.Equal(2, probe.Mounts);
        int before = probe.Builds;
        probe.S.Value++;
        Assert.Equal(before + 1, probe.Builds);
    }

    /// <summary>
    /// Tabs with <c>keepAlive</c> cache the panel and rebuild the content host around it on
    /// every switch, so the panel leaves inside the old host and comes back into a new one.
    /// </summary>
    [Fact]
    public void KeepAliveTab_ComponentStopsWhileHiddenAndWorksWhenShownAgain()
    {
        var probe = new Probe();
        var tabs = TabsElement.New()
            .Tab("A", () => Box.New().Children(probe.Mount()), keepAlive: true)
            .Tab("B", () => Text.New("b"));
        var root = Box.New().Children(tabs);
        tabs.ApplyTheme(HultUI.Core.Styling.Theme.Light);
        AssertLive(probe);

        tabs.SetSelected(1);
        AssertUnmounted(probe);

        tabs.SetSelected(0);
        Assert.Equal(2, probe.Mounts);
        int before = probe.Builds;
        probe.S.Value++;
        Assert.Equal(before + 1, probe.Builds);
    }
}
