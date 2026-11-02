using HultUI.Core.Builders;
using HultUI.Core.Components;

namespace Aperture;

public sealed partial class ApertureApp
{
    // ── Parts ────────────────────────────────────────────────────────────
    //
    // The title bar and the rail are parts (HultUI.Core.Components.Part), kept while the app
    // rebuilds around them: a cursor key built both with the view, 34 KB of it for elements the
    // key does not change. A part builds again when something it shows changes:
    //
    //  - a signal or a loader it reads: the libraries, the waiting and open counts on the rail;
    //  - anything else — where the app is, the server and who is signed in, the request server's
    //    doors, the reports with news, the display scale: _parts, which every MarkDirty and
    //    Rerender in the app invalidates, and every build nothing asked to be view-only: a loader
    //    that lands builds the app through a signal, and what landed can move a plain field a
    //    part reads (a report read takes the badge off the rail).
    //
    // Safe by default, then: a rebuild builds the parts too. Only paths that change nothing a part
    // shows say so, with MarkViewDirty and RerenderView: the cursor, a resize that changes the view
    // (ApertureApp.WindowFit.cs) and artwork landing. PartTests holds both parts to what a full
    // build of them makes.

    private readonly PartGroup _parts = new();
    private Part? _titleBarPart, _railPart;

    /// <summary>Whether the build to come was asked for by MarkViewDirty or RerenderView.</summary>
    private bool _viewBuild;

    /// <summary>Builds of the title bar and the rail so far, for tests.</summary>
    internal int PartBuilds => _parts.Builds;

    // Each part's host is the flex item where the part's root used to be: it never shrinks
    // (FlexShrink(0) where it is placed), and the rail fills its host's height.

    private Part TitleBarPart => _titleBarPart ??= new(_parts, BuildTitleBar);

    private Part RailPart => _railPart ??= new(_parts, () => BuildRail().FlexGrow(1));

    /// <summary>Rebuilds the window, the parts with it. Hides <c>Component.MarkDirty</c>, so every
    /// rebuild this app asks for, and every helper it hands its MarkDirty to, builds the parts as
    /// well.</summary>
    private new void MarkDirty()
    {
        _parts.Invalidate();
        base.MarkDirty();
    }

    /// <summary><see cref="MarkDirty"/> for a change no part shows: the view on screen rebuilds,
    /// the title bar and the rail stay as they are.</summary>
    private void MarkViewDirty()
    {
        _viewBuild = true;
        base.MarkDirty();
    }

    /// <summary><see cref="Rerender"/> for a change no part shows.</summary>
    private void RerenderView()
    {
        _viewBuild = true;
        _revision.Value++;
    }
}
