using System.Runtime.CompilerServices;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;
using HultUI.Core.Threading;

namespace HultUI.Core.Components;

/// <summary>
/// An element that hosts a Component, wrapping its Build() in a reactive Effect.
/// On first build, the built tree becomes the host's single child.
/// On subsequent rebuilds, the new tree is diffed against the existing child.
/// </summary>
public sealed class ComponentHost : Element
{
    private readonly Component _component;
    private Effect? _buildEffect;

    /// <summary>
    /// The component this host builds. Exposed so tooling (the DevTools inspector)
    /// can label a node with the component the app author wrote rather than the
    /// generic host element.
    /// </summary>
    public Component Component => _component;

    public ComponentHost(Component component)
    {
        _component = component;
        _component.Host = this;

        // Key by component identity so the reconciler never patches one
        // component's host onto a different component's host.
        Key = $"\0CH:{RuntimeHelpers.GetHashCode(component)}";

        // Eagerly run first build so the element tree is ready
        // before the host is necessarily mounted in a parent.
        _buildEffect = new Effect(Rebuild);
    }

    private void Rebuild()
    {
        _component.OnBeforeBuild();
        // A persistent child component the build hands on is moved into the new tree without
        // marking where it was; the reconcile below puts it back unmarked when it lands in the
        // same place, and marks the old parent when it does not (Element.DetachSilently).
        Element newTree;
        Element.Building++;
        try { newTree = _component.Build(); }
        finally { Element.Building--; }

        if (Children.Count == 0)
        {
            AddChild(newTree);
            if (!TreeDiffer.IsReconciling && Element.Building == 0) Element.FlushPendingMoves();
        }
        else
        {
            TreeDiffer.Reconcile(this, Children[0], newTree);
        }

        _component.OnAfterBuild();
    }

    /// <summary>
    /// Forces a full rebuild of this component, bypassing dependency checks.
    /// Used by hot reload to pick up code changes.
    /// </summary>
    internal void ForceRebuild()
    {
        // Component.MarkDirty() and hot reload reach the tree here without passing
        // through a Signal write, so the reactive guard would not see them.
        UIThread.VerifyAccess("Component.MarkDirty");

        if (_buildEffect is null) return;
        // A rebuild requested while a reconcile is in progress (e.g. MarkDirty from a
        // BlurEvent handler fired as the focused element is removed) must not run a
        // nested reconcile — it would mutate the child collection the outer pass is
        // enumerating. Defer it until the outermost reconcile unwinds.
        if (TreeDiffer.IsReconciling)
        {
            TreeDiffer.DeferRebuild(this);
            return;
        }
        // Run THROUGH the effect (forced past its change check) so Build()'s reactive
        // reads are re-tracked. Calling Rebuild() directly left the effect subscribed
        // only to whatever an earlier build happened to read, so any source first read
        // during a MarkDirty rebuild — e.g. a freshly created per-key AsyncLoader —
        // was never subscribed to, and its later completion went unobserved.
        _buildEffect.Execute(force: true);
        // The effect has finished executing, so any rebuild deferred during a reconcile
        // it ran can now execute without hitting the effect's re-entrancy guard.
        TreeDiffer.DrainDeferred();
    }

    /// <summary>
    /// Runs a rebuild that was deferred because it was requested mid-reconcile.
    /// Called by <see cref="TreeDiffer"/> once the outermost reconcile finishes.
    /// </summary>
    internal void RunDeferredRebuild() => _buildEffect?.Execute(force: true);

    protected override void OnMounted()
    {
        if (_buildEffect == null)
        {
            _component.Host = this;
            _buildEffect = new Effect(Rebuild);
        }
        _component.OnMount();
    }

    protected override void OnUnmounted()
    {
        _buildEffect?.Dispose();
        _buildEffect = null;
        _component.OnUnmount();
        _component.Host = null;
    }

    internal override void PatchFrom(Element source)
    {
        // ComponentHost identity is tied to its component instance.
        // It should not be patched — only replaced if the component type differs.
        base.PatchFrom(source);
    }
}
