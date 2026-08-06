using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Core.Tests.Components;

/// <summary>
/// A rebuild requested while a reconcile is in progress — the canonical case being
/// MarkDirty() from a BlurEvent handler fired synchronously as the focused element is
/// removed — must not run a nested reconcile that mutates the child collection the
/// outer pass is enumerating ("Collection was modified"). It is deferred until the
/// outermost reconcile unwinds, then runs.
/// </summary>
[Collection("Reactive")]
public class ReconcileReentrancyTests
{
    private sealed class BlurRebuildComponent : Component
    {
        public bool ShowChild = true;
        public int BuildCount;
        public readonly FocusManager Fm = new();
        public BoxElement? Child;

        public void TriggerRebuild() => MarkDirty();

        public override Element Build()
        {
            BuildCount++;
            var root = BoxElement.New();
            if (ShowChild)
            {
                var child = BoxElement.New();
                child.IsFocusable = true;
                // Set so the removal path (ClearFocusIfRemoved) finds a focus manager
                // and dispatches BlurEvent when this child leaves the tree.
                child.FocusManager = Fm;
                child.On<BlurEvent>(_ => MarkDirty()); // the dangerous mid-reconcile rebuild
                Child = child;
                root.AddChild(child);
            }
            return root;
        }
    }

    [Fact]
    public void MarkDirtyFromBlurDuringReconcile_DoesNotThrow_AndRebuildsAfter()
    {
        var c = new BlurRebuildComponent();
        c.Mount();                        // build 1: child present
        Assert.Equal(1, c.BuildCount);

        c.Fm.SetFocus(c.Child);           // focus the child so removal fires BlurEvent

        c.ShowChild = false;
        c.TriggerRebuild();               // build 2 reconcile removes child → Blur → MarkDirty (deferred)

        // No exception, and the deferred rebuild ran after the reconcile settled.
        Assert.Equal(3, c.BuildCount);    // initial + toggle rebuild + deferred rebuild
    }
}
