using HultUI.Core.Elements;
using HultUI.Core.Reactivity;

namespace HultUI.Core.Components;

/// <summary>
/// A piece of an owner's build that it keeps between its builds: a title bar, a rail, a toolbar.
/// The owner makes the part once and places it from every build (<see cref="Place"/>); nothing
/// inside it is built, and nothing around it is marked, unless what it shows moved. A key that
/// changes only the view beside it costs the view alone.
///
/// <para>A part builds again when a signal or a loader its build reads changes, as any component
/// does, and when its owner says it may have to: through the <see cref="PartGroup"/> every part of
/// the owner shares, which the owner's ordinary rebuilds invalidate, so a rebuild builds the parts
/// too unless the path says it changes nothing they show. See REACTIVITY.md, "Keep what a key does
/// not change in a child component".</para>
/// </summary>
public sealed class Part : Component
{
    private readonly PartGroup _group;
    private readonly Func<Element> _build;
    private readonly Signal<int>? _own;
    private readonly Action? _afterBuild;
    private int _revision = -1, _ownRevision = -1;

    /// <param name="group">The parts of this owner: <see cref="PartGroup.Invalidate"/> has every
    /// one of them build when it is next placed.</param>
    /// <param name="build">Makes the part's elements. It runs where the owner's build places the
    /// part, or on its own when something it reads changes, so it can run in a state the owner's
    /// next build is about to leave.</param>
    /// <param name="own">A revision of this part alone: writing it builds the part with no build of
    /// the owner, for a change only the part shows. Inside an event that build waits for the
    /// dispatch to end; a build of the owner in between places the part, and the waiting one then
    /// finds nothing new.</param>
    /// <param name="afterBuild">Runs after each build of the part but the first, which runs as
    /// the host is made, before the part is in the window: for work that waits for the part's
    /// elements, such as a field taking the keyboard.</param>
    public Part(PartGroup group, Func<Element> build, Signal<int>? own = null, Action? afterBuild = null)
    {
        _group = group;
        _build = build;
        _own = own;
        _afterBuild = afterBuild;
    }

    /// <summary>This part's builds so far.</summary>
    public int Builds { get; private set; }

    public override Element Build()
    {
        _revision = _group.Revision;
        if (_own is not null) _ownRevision = _own.Value;
        _group.Builds++;
        Builds++;
        return _build();
    }

    protected internal override void OnAfterBuild()
    {
        if (_afterBuild is not null && Mount().Parent is not null) _afterBuild();
    }

    /// <summary>
    /// The part's host, for the owner's build to add where the part's root used to be, built again
    /// first when <paramref name="stale"/> says so or a revision moved since it last built. It
    /// builds there and then, where its elements used to be built: in the build that removes it if
    /// it no longer belongs, and before the owner's <see cref="Component.OnAfterBuild"/>, which may
    /// read its elements.
    /// </summary>
    /// <param name="stale">Whether something the build reads and no revision covers changed, as the
    /// owner compares it: what a view-only rebuild can move that the part shows.</param>
    public ComponentHost Place(bool stale = false)
    {
        var host = Mount();
        if (stale || _revision != _group.Revision || (_own is not null && _ownRevision != _own.Peek()))
            MarkDirty();
        return host;
    }
}

/// <summary>
/// The <see cref="Part"/>s of one owner. An owner calls <see cref="Invalidate"/> from the rebuilds
/// that may change what a part shows, usually by hiding <c>MarkDirty</c>, so that every rebuild it
/// asks for builds the parts as well:
/// <code>
/// private new void MarkDirty() { _parts.Invalidate(); base.MarkDirty(); }
/// private void MarkViewDirty() => base.MarkDirty();   // for a change no part shows
/// </code>
/// </summary>
public sealed class PartGroup
{
    /// <summary>Moves with every <see cref="Invalidate"/>. A plain counter, not a signal: a part
    /// builds where its owner's build places it, not from an effect, which outside an event would
    /// run before the owner's build, and inside one after it.</summary>
    public int Revision { get; private set; }

    /// <summary>Builds of this owner's parts so far, of all of them.</summary>
    public int Builds { get; internal set; }

    /// <summary>Has every part build when it is next placed.</summary>
    public void Invalidate() => Revision++;
}
