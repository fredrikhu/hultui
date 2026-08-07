using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Primitives;
using Xunit;

namespace HultUI.Core.Tests.Components;

/// <summary>
/// Pins the reconciliation contract for interaction config (.OnHover/.OnPressed/.OnFocus):
/// like event handlers, the incoming build is the source of truth. An incoming element
/// WITH interaction replaces the retained config; one WITHOUT clears it (a hover style
/// attached only while a condition holds must disappear when it stops holding). Active
/// states re-apply through RefreshInteractionAfterPatch, so a hover tint survives a
/// rebuild that lands mid-hover.
/// </summary>
public class InteractionReconcileTests
{
    private static readonly Color Base = new(10, 10, 10, 255);
    private static readonly Color TintA = new(200, 0, 0, 255);
    private static readonly Color TintB = new(0, 200, 0, 255);

    private static (BoxElement parent, BoxElement el) Hovered(Color tint)
    {
        var parent = Box.New();
        var el = Box.New().Background(Base).OnHover(b => b.BackgroundColor = tint);
        parent.AddChild(el);
        el.SetInteractionHovered(true);
        Assert.Equal(tint, el.BackgroundColor);   // sanity: tint active pre-patch
        return (parent, el);
    }

    [Fact]
    public void HoverTint_SurvivesPatch_MidHover()
    {
        var (parent, el) = Hovered(TintA);

        // A rebuild patches Background back to the build value; the re-applied
        // override must restore the tint without the pointer re-entering.
        TreeDiffer.Reconcile(parent, el, Box.New().Background(Base).OnHover(b => b.BackgroundColor = TintA));

        Assert.Equal(TintA, el.BackgroundColor);
    }

    [Fact]
    public void Reconcile_ReplacesInteractionConfig()
    {
        var (parent, el) = Hovered(TintA);

        TreeDiffer.Reconcile(parent, el, Box.New().Background(Base).OnHover(b => b.BackgroundColor = TintB));

        // Still hovered, but the LATEST build's override paints.
        Assert.Equal(TintB, el.BackgroundColor);
    }

    [Fact]
    public void Reconcile_IncomingWithoutInteraction_ClearsConfig()
    {
        var (parent, el) = Hovered(TintA);

        // The condition that attached the hover style stopped holding (e.g. the tab
        // became active): no interaction on the incoming element, so the stale
        // override must not tint — neither right after the patch nor on re-hover.
        TreeDiffer.Reconcile(parent, el, Box.New().Background(Base));

        Assert.Equal(Base, el.BackgroundColor);

        el.SetInteractionHovered(false);
        el.SetInteractionHovered(true);
        Assert.Equal(Base, el.BackgroundColor);
    }

    [Fact]
    public void Unhover_AfterPatch_RestoresPatchedBase()
    {
        var (parent, el) = Hovered(TintA);

        // The rebuild changes the resting color; un-hover must restore the NEW base,
        // not the one snapshotted before the patch.
        var newBase = new Color(30, 30, 30, 255);
        TreeDiffer.Reconcile(parent, el, Box.New().Background(newBase).OnHover(b => b.BackgroundColor = TintA));
        Assert.Equal(TintA, el.BackgroundColor);

        el.SetInteractionHovered(false);
        Assert.Equal(newBase, el.BackgroundColor);
    }
}
