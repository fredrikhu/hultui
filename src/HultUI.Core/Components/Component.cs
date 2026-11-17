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

    /// <summary>Called before each Build() invocation, inside the build: a rebuild asked for
    /// here is dropped (see <see cref="MarkDirty"/>).</summary>
    protected internal virtual void OnBeforeBuild() { }

    /// <summary>Called after each Build() invocation and tree reconciliation, still inside the
    /// build: a rebuild asked for here is dropped (see <see cref="MarkDirty"/>).</summary>
    protected internal virtual void OnAfterBuild() { }

    /// <summary>
    /// Marks this component as needing a rebuild. Use when non-reactive state changes.
    /// The rebuild runs through the component's reactive Effect, so it re-tracks
    /// dependencies — it is safe for a rebuild triggered this way to start reading a
    /// reactive source it has never read before.
    /// </summary>
    /// <remarks>
    /// Called from this component's own <see cref="OnBeforeBuild"/>, <see cref="Build"/> or
    /// <see cref="OnAfterBuild"/>, it is dropped: the build is still running. So is a write
    /// there to a signal the build read. Ask from outside the build instead, with
    /// <c>Application.InvokeOnMainThread(MarkDirty)</c> or the window's
    /// <c>RequestPostFrameCallback(MarkDirty)</c> (docs/REACTIVITY.md, Lifecycle hooks).
    /// </remarks>
    protected void MarkDirty() => Host?.ForceRebuild();

    /// <summary>
    /// The <see cref="ComponentHost"/> element that shows this component in a tree. The first call
    /// makes the host and builds it; every later call returns that same host, unbuilt, until the
    /// host is removed from its tree, after which the next call makes a new host that builds at once.
    /// </summary>
    /// <remarks>
    /// <para>So a component a parent keeps between its builds, made once and placed from every
    /// build, does not build again when the parent sets a plain property on it: the child's
    /// <see cref="Build"/> reads the property, but nothing tells the host it changed, and the child
    /// keeps its old tree. To have it follow, pick one:</para>
    /// <list type="bullet">
    /// <item>make the property a signal or a <c>[Reactive]</c> property, so a write rebuilds the child;</item>
    /// <item>give the child a method of its own that sets the state and calls <see cref="MarkDirty"/>;</item>
    /// <item>build that piece as a <see cref="Part"/> and place it with
    /// <see cref="Part.Place(bool)">Place(stale: true)</see>, or <see cref="PartGroup.Invalidate"/>
    /// for all of an owner's parts;</item>
    /// <item>make a new component instead, which builds its own host.</item>
    /// </list>
    /// <para><see cref="MarkDirty"/> builds at once, and so does a signal write outside a batch, so set
    /// any plain state the build reads before either. Element event handlers and work posted with
    /// <c>Application.InvokeOnMainThread</c> run in a batch, where a write's build waits for the
    /// batch to end; elsewhere, make several writes inside <c>ReactiveRuntime.Batch</c>. See
    /// docs/REACTIVITY.md, "What triggers a rebuild".</para>
    /// </remarks>
    public ComponentHost Mount() => Host ?? new ComponentHost(this);
}
