using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;
using Xunit;

namespace HultUI.Core.Tests.Components;

/// <summary>
/// A build that puts another instance of the same component type where one was gets that
/// instance's host, not the old host patched. The reconciler matched hosts by type alone, so
/// an app key set on the host <see cref="Component.Mount"/> returns (which replaces the host's
/// own key), or a single slot holding one component's host or another's, patched the old host:
/// the old component stayed live, and the new one was never mounted.
/// </summary>
[Collection("Reactive")]
public class ForeignHostPatchTests
{
    private sealed class Probe(string name) : Component
    {
        public Signal<int> S { get; } = new(0);
        public int Mounts { get; private set; }
        public int Unmounts { get; private set; }

        public override Element Build() => Text.New($"{name} {S.Value}");

        protected internal override void OnMount() => Mounts++;
        protected internal override void OnUnmount() => Unmounts++;
    }

    /// <summary>A box holding one probe's host, under an app key, beside a label.</summary>
    private sealed class KeyedParent(Probe a, Probe b) : Component
    {
        public Signal<bool> UseB { get; } = new(false);

        public override Element Build()
        {
            var host = (UseB.Value ? b : a).Mount();
            host.Key = "x";
            return Box.New().Children(host, Text.New("label"));
        }
    }

    /// <summary>A build that returns one probe's host or the other's: a single slot.</summary>
    private sealed class SlotParent(Probe a, Probe b) : Component
    {
        public Signal<bool> UseB { get; } = new(false);

        public override Element Build() => (UseB.Value ? b : a).Mount();
    }

    private static string Shown(ComponentHost host) => ((TextElement)host.Children[0]).Content;

    [Fact]
    public void AnAppKeyOnAHost_DoesNotPatchAnotherComponentsHostUnderIt()
    {
        var a = new Probe("a");
        var b = new Probe("b");
        var parent = new KeyedParent(a, b);
        var box = parent.Mount().Children[0];

        parent.UseB.Value = true;

        var shown = Assert.IsType<ComponentHost>(box.Children[0]);
        Assert.Same(b, shown.Component);
        Assert.False(shown.IsDetached);
        Assert.Equal("b 0", Shown(shown));
        Assert.Equal(1, b.Mounts);
        Assert.Equal(1, a.Unmounts);

        // b is live where it shows: its own state reaches the screen.
        b.S.Value = 5;
        Assert.Equal("b 5", Shown(shown));
    }

    [Fact]
    public void ASingleSlot_SwapsOneComponentsHostForAnothers()
    {
        var a = new Probe("a");
        var b = new Probe("b");
        var parent = new SlotParent(a, b);
        var outer = parent.Mount();

        parent.UseB.Value = true;

        var shown = Assert.IsType<ComponentHost>(outer.Children[0]);
        Assert.Same(b, shown.Component);
        Assert.Equal(1, a.Unmounts);

        // a no longer writes into the tree it left.
        a.S.Value = 7;
        Assert.Same(b, ((ComponentHost)outer.Children[0]).Component);
        Assert.Equal("b 0", Shown(shown));

        // And back: a fresh host for a, built from a's state.
        parent.UseB.Value = false;
        var back = Assert.IsType<ComponentHost>(outer.Children[0]);
        Assert.Same(a, back.Component);
        Assert.Equal("a 7", Shown(back));
        Assert.Equal(1, b.Unmounts);
    }

    [Fact]
    public void TryPatch_RefusesAnotherComponentsHost_AndTakesItsOwn()
    {
        var a = new Probe("a").Mount();
        var b = new Probe("b").Mount();

        Assert.False(TreeDiffer.TryPatch(a, b));
        Assert.Equal("a 0", Shown(a));
        Assert.True(TreeDiffer.TryPatch(a, a));
    }

    [Fact]
    public void HostsOfTwoComponents_NeverShareAKey()
    {
        var keys = Enumerable.Range(0, 2000).Select(_ => new Probe("p").Mount().Key).ToHashSet();
        Assert.Equal(2000, keys.Count);
    }
}
