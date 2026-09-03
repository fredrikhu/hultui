using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using Xunit;

namespace HultUI.Core.Tests.Components;

/// <summary>
/// Pins the reconciliation contract for event handlers: the incoming build is the
/// source of truth for APP handlers (registered via On), while element-OWNED handlers
/// (constructor wiring registered via OnOwn) always stay with the retained element.
/// Before this contract, handlers were never copied — a retained element kept its
/// first build's handlers forever, which stranded conditionally-attached handlers
/// (disabled→enabled buttons, swapped-in dialog pages) as dead clicks.
/// </summary>
public class HandlerReconcileTests
{
    [Fact]
    public void Reconcile_ReplacesAppHandlers_WithIncomingBuilds()
    {
        var parent = Box.New();
        var existing = Box.New();
        parent.AddChild(existing);

        bool oldFired = false, newFired = false;
        existing.On<ClickEvent>(_ => oldFired = true);

        var incoming = Box.New();
        incoming.On<ClickEvent>(_ => newFired = true);

        TreeDiffer.Reconcile(parent, existing, incoming);
        EventDispatcher.Dispatch(new ClickEvent(), existing);

        Assert.True(newFired);
        Assert.False(oldFired);
    }

    [Fact]
    public void Reconcile_IncomingWithoutHandlers_ClearsAppHandlers()
    {
        // The disabled-button case in reverse: an enabled→disabled rebuild must
        // drop the click handler, not leave the stale enabled one behind.
        var parent = Box.New();
        var existing = Box.New();
        parent.AddChild(existing);

        bool fired = false;
        existing.On<ClickEvent>(_ => fired = true);

        TreeDiffer.Reconcile(parent, existing, Box.New());
        EventDispatcher.Dispatch(new ClickEvent(), existing);

        Assert.False(fired);
        Assert.False(existing.HasAnyEventHandlers);
    }

    [Fact]
    public void Reconcile_KeepsOwnedHandlers_DiscardsIncomingsOwned()
    {
        // Owned handlers are element machinery whose closures capture their own
        // instance — the retained element must keep ITS wiring, and must never
        // adopt the incoming (detached) element's.
        var parent = Box.New();
        var existing = Box.New();
        parent.AddChild(existing);

        bool retainedOwnFired = false, incomingOwnFired = false;
        existing.OnOwn<ClickEvent>(_ => retainedOwnFired = true);

        var incoming = Box.New();
        incoming.OnOwn<ClickEvent>(_ => incomingOwnFired = true);

        TreeDiffer.Reconcile(parent, existing, incoming);
        EventDispatcher.Dispatch(new ClickEvent(), existing);

        Assert.True(retainedOwnFired);
        Assert.False(incomingOwnFired);
    }

    [Fact]
    public void CopiedHandler_SeesRetainedElement_AsCurrentTarget()
    {
        // The sanctioned self-reference: a copied handler's captured element local
        // is the discarded incoming build, so handlers must reach the live element
        // through e.CurrentTarget (which HoverBg and popover anchors rely on).
        var parent = Box.New();
        var existing = Box.New();
        parent.AddChild(existing);

        Element? seen = null;
        var incoming = Box.New();
        incoming.On<ClickEvent>(e => seen = e.CurrentTarget);

        TreeDiffer.Reconcile(parent, existing, incoming);
        EventDispatcher.Dispatch(new ClickEvent(), existing);

        Assert.Same(existing, seen);
    }

    [Fact]
    public void HandlerSwap_MidDispatch_DoesNotBreakEnumeration()
    {
        // A handler may MarkDirty → synchronous rebuild → PatchFrom, swapping this
        // very element's handlers while they are being invoked. Dispatch runs the
        // snapshot taken when it started — all of it, without throwing.
        var parent = Box.New();
        var existing = Box.New();
        parent.AddChild(existing);

        bool secondFired = false;
        existing.On<ClickEvent>(_ =>
        {
            var incoming = Box.New();
            incoming.On<ClickEvent>(_ => { });
            TreeDiffer.Reconcile(parent, existing, incoming);
        });
        existing.On<ClickEvent>(_ => secondFired = true);

        EventDispatcher.Dispatch(new ClickEvent(), existing);

        Assert.True(secondFired);
    }

    /// <summary>
    /// The same rule one level up: a handler that rebuilds the tree changes what the
    /// ancestors above it <i>are</i>, and the event still bubbling through them must not
    /// pick up the new build's handlers.
    ///
    /// <para>This is the shape that made one click both add a row and delete it: the button
    /// ran "add" and marked dirty, the rebuild patched the button's parent into the new
    /// row's remove button, and the same click bubbled straight into "remove".</para>
    /// </summary>
    [Fact]
    public void HandlerSwap_MidDispatch_DoesNotReachAncestorsStillToBubble()
    {
        var root = Box.New();
        var ancestor = Box.New();
        var button = Box.New();
        root.AddChild(ancestor);
        ancestor.AddChild(button);

        bool installedRan = false;
        button.On<ClickEvent>(_ =>
        {
            // What the rebuild the click asked for does to the row it was clicked in.
            var incoming = Box.New();
            incoming.On<ClickEvent>(_ => installedRan = true);
            incoming.AddChild(Box.New());
            TreeDiffer.Reconcile(root, ancestor, incoming);
        });

        EventDispatcher.Dispatch(new ClickEvent(), button);

        Assert.False(installedRan);
        // And it is installed, not lost: the next click is the one that runs it.
        EventDispatcher.Dispatch(new ClickEvent(), ancestor);
        Assert.True(installedRan);
    }

    /// <summary>A handler attached to an ancestor by an earlier handler in the same walk is
    /// held for the next event too — one rule, whether the new handler arrived through a
    /// rebuild or through a bare <c>On</c>.</summary>
    [Fact]
    public void HandlerAddedMidDispatch_DoesNotRunForTheEventThatAddedIt()
    {
        var ancestor = Box.New();
        var button = Box.New();
        ancestor.AddChild(button);

        int ran = 0;
        button.On<ClickEvent>(_ => ancestor.On<ClickEvent>(_ => ran++));

        EventDispatcher.Dispatch(new ClickEvent(), button);
        Assert.Equal(0, ran);

        EventDispatcher.Dispatch(new ClickEvent(), button);
        Assert.Equal(1, ran);
    }
}
