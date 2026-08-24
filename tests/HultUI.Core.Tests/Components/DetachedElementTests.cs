using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;
using Xunit;

namespace HultUI.Core.Tests.Components;

/// <summary>
/// Pins the lifetime contract that reconciliation implies but never used to state: the incoming
/// build's elements are discarded once patched, so a reference to one is worthless from the
/// second frame on. Reading such an element's <see cref="Element.Layout"/> gave geometry that
/// was never computed and writing its scroll offset changed nothing — both silently, so the
/// symptom was always "this control has stopped responding" with nothing to point at.
///
/// <para>These tests are the counterpart to HULTUI003: the analyzer catches the identity
/// comparison at compile time, and this catches every other way of using a corpse at run time.
/// </para>
/// </summary>
public class DetachedElementTests
{
    [Fact]
    public void APatchedIncomingElement_IsMarkedDetached()
    {
        var parent = Box.New();
        var existing = Box.New();
        parent.AddChild(existing);
        var incoming = Box.New();

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.True(incoming.IsDetached);
        // The retained element is what the tree kept, and it is emphatically alive.
        Assert.False(existing.IsDetached);
    }

    [Fact]
    public void ChildrenOfAPatchedElement_AreMarkedToo()
    {
        var parent = Box.New();
        var existing = Box.New();
        existing.AddChild(Box.New());
        parent.AddChild(existing);

        var incoming = Box.New();
        var incomingChild = Box.New();
        incoming.AddChild(incomingChild);

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.True(incomingChild.IsDetached);
    }

    [Fact]
    public void AnIncomingElementWithNoCounterpart_IsAdoptedAndStaysLive()
    {
        // A newly added child is not patched into anything — it becomes the live element, so
        // marking it would be a false positive of the worst kind.
        var parent = Box.New();
        var existing = Box.New();
        parent.AddChild(existing);

        var incoming = Box.New();
        var adopted = Box.New();
        incoming.AddChild(adopted);

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.False(adopted.IsDetached);
        Assert.Same(adopted, existing.Children[0]);
    }

    [Fact]
    public void AReplacedElement_IsNotMarked()
    {
        // Types differ, so the incoming element is inserted rather than patched: it is live.
        var parent = Box.New();
        var existing = Box.New();
        parent.AddChild(existing);
        var incoming = Text.New("different type");

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.False(incoming.IsDetached);
        Assert.Same(incoming, parent.Children[0]);
    }

    [Fact]
    public void AnElementPatchedAgainstItself_IsNotMarked()
    {
        // A Build() that returns a cached element hands the same instance in as both sides.
        // It is still in the tree afterwards, so declaring it dead would break a legal — if
        // unusual — pattern.
        var parent = Box.New();
        var cached = Box.New();
        parent.AddChild(cached);

        TreeDiffer.Reconcile(parent, cached, cached);

        Assert.False(cached.IsDetached);
    }

    [Fact]
    public void ARefIsRepointedAtTheLiveElement()
    {
        // The documented way out, and what the diagnostic tells people to use.
        var parent = Box.New();
        var existing = Box.New();
        parent.AddChild(existing);

        var reference = new Ref<BoxElement>();
        var incoming = Box.New();
        incoming.WithRef(reference);

        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.Same(existing, reference.Current);
        Assert.False(reference.Current!.IsDetached);
    }

#if HULTUI_STRICT_LIFETIME

    [Fact]
    public void ReadingLayoutOfADetachedElement_Throws()
    {
        var parent = Box.New();
        var existing = Box.New();
        parent.AddChild(existing);
        var incoming = Box.New();
        TreeDiffer.Reconcile(parent, existing, incoming);

        var ex = Assert.Throws<InvalidOperationException>(() => _ = incoming.Layout);
        Assert.Contains("replaced by reconciliation", ex.Message);
        // The message has to name the way out, or it is just a nastier silent failure.
        Assert.Contains("Ref<T>", ex.Message);
    }

    [Fact]
    public void TheMessageNamesTheElement()
    {
        var parent = Box.New();
        var existing = Box.New();
        parent.AddChild(existing);
        var incoming = Box.New();
        incoming.Name = "FilterStrip";
        TreeDiffer.Reconcile(parent, existing, incoming);

        var ex = Assert.Throws<InvalidOperationException>(() => _ = incoming.Layout);
        Assert.Contains("FilterStrip", ex.Message);
    }

    [Fact]
    public void ScrollingADetachedScrollView_Throws()
    {
        // The bug the guard was written for: an overlay that captured the list during a build
        // kept setting an offset nothing read.
        var parent = Box.New();
        var existing = ScrollView.Vertical();
        parent.AddChild(existing);
        var incoming = ScrollView.Vertical();
        TreeDiffer.Reconcile(parent, existing, incoming);

        Assert.Throws<InvalidOperationException>(() => incoming.ScrollOffsetY = 100);
        Assert.Throws<InvalidOperationException>(() => incoming.ScrollOffsetX = 100);
    }

    [Fact]
    public void ReadingLayoutOfALiveElement_DoesNotThrow()
    {
        var parent = Box.New();
        var existing = Box.New();
        parent.AddChild(existing);
        TreeDiffer.Reconcile(parent, existing, Box.New());

        // The whole point is that the retained element keeps working.
        _ = existing.Layout;
        existing.Name = "still fine";
    }

    [Fact]
    public void TheCheckCanBeSwitchedOff()
    {
        // For a test — or a diagnostic tool — that means to look at a discarded element.
        var parent = Box.New();
        var existing = Box.New();
        parent.AddChild(existing);
        var incoming = Box.New();
        TreeDiffer.Reconcile(parent, existing, incoming);

        Element.StrictLifetimeChecks = false;
        try
        {
            _ = incoming.Layout;
        }
        finally
        {
            Element.StrictLifetimeChecks = true;
        }
    }

#endif
}
