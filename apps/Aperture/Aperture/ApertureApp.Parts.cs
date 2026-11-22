using Aperture.Core;
using Aperture.Core.Requests;
using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;

namespace Aperture;

public sealed partial class ApertureApp
{
    // ── Parts ────────────────────────────────────────────────────────────
    //
    // The title bar and the rail are parts (HultUI.Core.Components.Part), kept while the app
    // rebuilds around them: a cursor key built both with the view, 34 KB of it for elements the
    // key does not change. A part builds again only when something it shows moved:
    //
    //  - a signal or a loader it reads (the libraries, the waiting and open counts on the rail):
    //    its own build reads them, so its own effect hears them, whether the app builds or not;
    //  - anything it shows, as a value (TitleBarShows, RailShows): each part keeps the value it
    //    last built from, and every build of the app compares it with the value now and builds
    //    the part when they differ. The title bar shows the server, who is signed in and the
    //    scale its icons are cut for; the rail the door lit for the page, the libraries, the
    //    request server's user (its doors), the three badges and the scale.
    //
    // So no path has to say what it changed. Every page opened, Back and Forward, a sign-out,
    // a loader that lands and a report read build the app, and a part builds when its value
    // moved: a film's page builds the rail (its light goes out) and not the title bar; a page
    // fetched while wheeling builds neither. Until plan 17 round 18 a rebuild built both parts
    // unless the path that asked said view-only: 49 KB on every page opened and every load
    // that landed. MarkViewDirty and RerenderView are now MarkDirty and Rerender by other
    // names, kept for the paths that know they move nothing a part shows. The hidden MarkDirty
    // still builds every part, whatever moved: PartTests holds both parts to what that full
    // build makes.

    private readonly PartGroup _parts = new();
    private Part? _titleBarPart, _railPart;

    /// <summary>Builds of the title bar and the rail so far, for tests.</summary>
    internal int PartBuilds => _parts.Builds;

    /// <summary>What the title bar shows: the server and who is signed in on it (null when no one
    /// is), and the display scale its icons are cut for.</summary>
    private readonly record struct TitleBarShows(ServerInfo? Server, SignedInUser? User, float Scale);

    /// <summary>What the rail shows: the door lit for the page and, for a library's, which one;
    /// the libraries (the loader's list); the request server's user, whose permissions open its
    /// doors and badges (null: no request server, no doors); the waiting, open and new counts;
    /// and the display scale its icons are cut for.</summary>
    private readonly record struct RailShows(RailDoor Lit, string? LitLibrary, IReadOnlyList<LibraryView> Libraries,
        RequestUser? Requests, int Waiting, int Open, int News, float Scale);

    /// <summary>The rail's doors, one lit for each page: the libraries share one, told apart by the
    /// library's id.</summary>
    private enum RailDoor { None, Home, Library, Discover, Requests, Issues, Favourites, Search, Settings }

    private static RailDoor DoorOf(ApertureView view) => view switch
    {
        // Home's rows in full are Home's, as a Discover row in full is Discover's.
        ApertureView.Home or ApertureView.ContinueWatching or ApertureView.NextUp => RailDoor.Home,
        ApertureView.Library => RailDoor.Library,
        ApertureView.Discover or ApertureView.DiscoverTitle or ApertureView.DiscoverGenre or ApertureView.DiscoverPerson
            or ApertureView.DiscoverCompany or ApertureView.DiscoverAll or ApertureView.DiscoverGenres
            or ApertureView.DiscoverCompanies => RailDoor.Discover,
        ApertureView.Requests => RailDoor.Requests,
        ApertureView.Issues or ApertureView.Issue => RailDoor.Issues,
        ApertureView.Favourites => RailDoor.Favourites,
        ApertureView.Search => RailDoor.Search,
        ApertureView.Settings => RailDoor.Settings,
        _ => RailDoor.None,
    };

    /// <summary>The values each part last built from.</summary>
    private TitleBarShows _titleBarShown;
    private RailShows _railShown;

    private TitleBarShows TitleBarNow() => new(_library?.Server, _library?.User, Ui.DisplayScale);

    private RailShows RailNow()
    {
        var route = Current;
        var lit = DoorOf(route.View);
        return new(lit, lit == RailDoor.Library ? route.Id : null, Libraries, _requestServer?.User,
            PendingRequestCount, OpenIssueCount, MyIssueNewsCount, Ui.DisplayScale);
    }

    /// <summary>The rail's value as the app's build reads it: untracked, as the loaders it reads are
    /// the rail's own to hear, or each count that landed would build the app as well.</summary>
    private RailShows RailNowUntracked() => ReactiveScope.Untracked(_railNow ??= RailNow);
    private Func<RailShows>? _railNow;

    // Each part's host is the flex item where the part's root used to be: it never shrinks
    // (FlexShrink(0) where it is placed), and the rail fills its host's height.

    private Part TitleBarPart => _titleBarPart ??= new(_parts, BuildTitleBarPart);

    private Part RailPart => _railPart ??= new(_parts, BuildRailPart);

    private Element BuildTitleBarPart()
    {
        _titleBarShown = TitleBarNow();
        return BuildTitleBar(_titleBarShown);
    }

    private Element BuildRailPart()
    {
        _railShown = RailNow();
        return BuildRail(_railShown).FlexGrow(1);
    }

    /// <summary>The title bar's host for the app's build: built first when what it shows moved.</summary>
    private ComponentHost PlaceTitleBar() => TitleBarPart.Place(stale: TitleBarNow() != _titleBarShown);

    /// <summary>The rail's host for the app's build: built first when what it shows moved.</summary>
    private ComponentHost PlaceRail() => RailPart.Place(stale: RailNowUntracked() != _railShown);

    /// <summary>Rebuilds the window and every part, whatever moved. Hides <c>Component.MarkDirty</c>,
    /// so every helper handed this app's MarkDirty builds the parts as well: the safe default for a
    /// change no value above covers (a toast that comes; PartTests' oracle).</summary>
    private new void MarkDirty()
    {
        _parts.Invalidate();
        base.MarkDirty();
    }

    /// <summary>Rebuilds the window: the view, and a part when what it shows moved.</summary>
    private void MarkViewDirty() => base.MarkDirty();

    /// <summary><see cref="Rerender"/>, for a change no part shows: every build compares what the
    /// parts show, so the two are the same; the name keeps what the caller knows.</summary>
    private void RerenderView() => Rerender();
}
