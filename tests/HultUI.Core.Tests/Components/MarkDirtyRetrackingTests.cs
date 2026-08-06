using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;
using Xunit;

namespace HultUI.Core.Tests.Components;

/// <summary>
/// A <c>MarkDirty()</c> rebuild must run through the component's Effect so its
/// reactive reads are re-tracked. Before this was fixed, MarkDirty called Build()
/// directly, so a signal first read during such a rebuild was never subscribed to
/// and later changes to it went unobserved — the app-level symptom was a pane that
/// sat on its loading placeholder until some unrelated tracked rebuild happened.
/// </summary>
[Collection("Reactive")]
public class MarkDirtyRetrackingTests
{
    /// <summary>Reads whichever signal <see cref="Source"/> points at. Swapping the
    /// source is plain (non-reactive) state, so callers pair it with MarkDirty —
    /// exactly the shape of swapping to a different per-key AsyncLoader.</summary>
    private sealed class SwitchingComponent : Component
    {
        public Signal<int> A { get; } = new(0);
        public Signal<int> B { get; } = new(0);
        public Signal<int> Source;
        public int BuildCount;

        public SwitchingComponent() => Source = A;

        public void SwitchToB()
        {
            Source = B;
            MarkDirty();
        }

        public override Element Build()
        {
            BuildCount++;
            return Text.New($"v={Source.Value}");
        }
    }

    [Fact]
    public void MarkDirtyRebuild_SubscribesToNewlyReadSignal()
    {
        var c = new SwitchingComponent();
        c.Mount();
        Assert.Equal(1, c.BuildCount);

        c.SwitchToB();                       // non-reactive swap + MarkDirty
        Assert.Equal(2, c.BuildCount);

        // The regression: B was first read during a MarkDirty rebuild, so changing it
        // must now trigger a rebuild.
        c.B.Value = 42;
        Assert.Equal(3, c.BuildCount);
    }

    [Fact]
    public void MarkDirtyRebuild_UnsubscribesFromNoLongerReadSignal()
    {
        var c = new SwitchingComponent();
        c.Mount();
        c.SwitchToB();
        var afterSwitch = c.BuildCount;

        // A is no longer read, so it must no longer drive rebuilds.
        c.A.Value = 99;
        Assert.Equal(afterSwitch, c.BuildCount);
    }

    [Fact]
    public void MarkDirty_AlwaysRebuilds_EvenWithNoDependencyChange()
    {
        // MarkDirty exists for non-reactive state, so it must bypass the Effect's
        // "did any dependency actually change?" early-out.
        var c = new SwitchingComponent();
        c.Mount();

        c.MarkDirtyForTest();
        c.MarkDirtyForTest();

        Assert.Equal(3, c.BuildCount);   // initial + two forced rebuilds
    }

    [Fact]
    public void ReactiveChange_StillSkipsRebuild_WhenValueIsUnchanged()
    {
        // The forced path must not defeat the ordinary change check.
        var c = new SwitchingComponent();
        c.Mount();
        var initial = c.BuildCount;

        c.A.Value = 0;   // same value as it already held
        Assert.Equal(initial, c.BuildCount);
    }
}

file static class TestExtensions
{
    // MarkDirty is protected; expose it for the forced-rebuild test.
    public static void MarkDirtyForTest(this Component c) =>
        typeof(Component)
            .GetMethod("MarkDirty", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(c, null);
}
