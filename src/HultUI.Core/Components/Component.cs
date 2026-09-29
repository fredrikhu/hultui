using HultUI.Core.Elements;

namespace HultUI.Core.Components;

/// <summary>
/// Base class for stateful UI components. Subclasses override Build() to return
/// an element tree. When reactive dependencies read during Build() change,
/// the tree is automatically rebuilt and diffed against the previous version.
/// </summary>
public abstract class Component
{
    internal ComponentHost? Host { get; set; }

    /// <summary>
    /// Produce the element tree for this component.
    /// Always called inside this component's reactive Effect — every Signal/Computed
    /// read is tracked, including on a <see cref="MarkDirty"/> rebuild, so a source
    /// first read during any build is subscribed to from then on.
    /// </summary>
    public abstract Element Build();

    /// <summary>Called after the component host is added to the element tree.</summary>
    protected internal virtual void OnMount() { }

    /// <summary>
    /// Called after the component host, or any element it sits under, is removed from the
    /// element tree. The build Effect is disposed first, so the component no longer rebuilds.
    /// </summary>
    protected internal virtual void OnUnmount() { }

    /// <summary>Called before each Build() invocation.</summary>
    protected internal virtual void OnBeforeBuild() { }

    /// <summary>Called after each Build() invocation and tree reconciliation.</summary>
    protected internal virtual void OnAfterBuild() { }

    /// <summary>
    /// Marks this component as needing a rebuild. Use when non-reactive state changes.
    /// The rebuild runs through the component's reactive Effect, so it re-tracks
    /// dependencies — it is safe for a rebuild triggered this way to start reading a
    /// reactive source it has never read before.
    /// </summary>
    protected void MarkDirty() => Host?.ForceRebuild();

    /// <summary>
    /// Create a ComponentHost element that can be placed in the element tree.
    /// </summary>
    public ComponentHost Mount() => Host ?? new ComponentHost(this);
}
