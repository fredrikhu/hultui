using System.Runtime.CompilerServices;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;

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
        var newTree = _component.Build();

        if (Children.Count == 0)
        {
            AddChild(newTree);
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
        if (_buildEffect is null) return;
        // Run THROUGH the effect (forced past its change check) so Build()'s reactive
        // reads are re-tracked. Calling Rebuild() directly left the effect subscribed
        // only to whatever an earlier build happened to read, so any source first read
        // during a MarkDirty rebuild — e.g. a freshly created per-key AsyncLoader —
        // was never subscribed to, and its later completion went unobserved.
        _buildEffect.Execute(force: true);
    }

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
