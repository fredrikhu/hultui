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
    //    scale its icons are cut for; the rail the libraries, the request server's user (its
    //    doors), the three badges and the scale.
    //
    // So no path has to say what it changed. Every page opened, Back and Forward, a sign-out,
    // a loader that lands and a report read build the app, and a part builds when its value
    // moved: a page fetched while wheeling builds neither. Until plan 17 round 18 a rebuild built
    // both parts unless the path that asked said view-only: 49 KB on every page opened and every
    // load that landed. MarkViewDirty is now the base MarkDirty and RerenderView is Rerender,
    // kept for the paths that know they move nothing a part shows. The hidden MarkDirty still
    // builds every part, whatever moved: PartTests holds both parts to what that full build makes.
    //
    // Each door on the rail is a part of its own inside the rail's (RailDoorPart), as Junction's
    // view tabs are (3ea96588): a page opened moves the light, and the light (RailLight) is
    // compared on the app's build as the values are, so a page opened builds the door going dark
    // and the one lighting up, about 3.5 KB each, not the rail's nine (31.3 KB). Whatever builds
    // the rail builds every door with it, from the value it recorded.

    private readonly PartGroup _parts = new();
    private Part? _titleBarPart, _railPart;

    /// <summary>Builds of the title bar, the rail and the rail's doors so far, for tests.</summary>
    internal int PartBuilds => _parts.Builds + _railDoors.Builds;

    /// <summary>What the title bar shows: the server and who is signed in on it (null when no one
    /// is), and the display scale its icons are cut for.</summary>
    private readonly record struct TitleBarShows(ServerInfo? Server, SignedInUser? User, float Scale);

    /// <summary>What the rail shows but its light: the libraries (the loader's list); the request
    /// server's user, whose permissions open its doors and badges (null: no request server, no
    /// doors); the waiting, open and new counts; and the display scale its icons are cut for.</summary>
    private readonly record struct RailShows(IReadOnlyList<LibraryView> Libraries, RequestUser? Requests,
        int Waiting, int Open, int News, float Scale);

    /// <summary>A door on the rail, as the light finds it: a library's by its id.</summary>
    private readonly record struct RailLight(RailDoor Door, string? Library);

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

    /// <summary>The values each part last built from, and the rail's light as the app's last build
    /// found it.</summary>
    private TitleBarShows _titleBarShown;
    private RailShows _railShown;
    private RailLight _light;

    private TitleBarShows TitleBarNow() => new(_library?.Server, _library?.User, Ui.DisplayScale);

    private RailShows RailNow() => new(Libraries, _requestServer?.User,
        PendingRequestCount, OpenIssueCount, MyIssueNewsCount, Ui.DisplayScale);

    private RailLight LightNow()
    {
        var route = Current;
        var door = DoorOf(route.View);
        return new(door, door == RailDoor.Library ? route.Id : null);
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

    /// <summary>The rail's host for the app's build: built first when what it shows moved, and if
    /// not, the doors whose light moved built alone.</summary>
    private ComponentHost PlaceRail()
    {
        bool stale = RailNowUntracked() != _railShown;
        var light = LightNow();
        if (light != _light)
        {
            _light = light;
            if (!stale) Relight();
        }
        return RailPart.Place(stale);
    }

    // ── The rail's doors ─────────────────────────────────────────────────

    /// <summary>One door on the rail, a part inside the rail's, made once and kept: the fixed doors
    /// by name, the libraries' by their place on the rail, so a library added, gone or another
    /// server's makes no part and drops none. <see cref="Light"/> is the part's own revision: the
    /// light moving onto the door or off it builds the door alone.</summary>
    private sealed class RailDoorPart
    {
        public readonly Signal<int> Light = new(0);
        public readonly Part Part;

        /// <summary>The door this part is on the rail's last build: a library slot's library.</summary>
        public RailLight Key;

        public RailDoorPart(PartGroup group, Func<RailDoorPart, Element> build) =>
            Part = new Part(group, () => build(this), own: Light);
    }

    /// <summary>The rail's doors: whatever builds the rail builds every door with it.</summary>
    private readonly PartGroup _railDoors = new();
    private RailDoorPart? _homeDoor, _discoverDoor, _requestsDoor, _issuesDoor, _favouritesDoor, _searchDoor, _settingsDoor;
    private readonly List<RailDoorPart> _libraryDoors = [];

    /// <summary>The doors the rail's last build placed, and the one of them lit.</summary>
    private readonly List<RailDoorPart> _doorsOnRail = [];
    private RailDoorPart? _litDoor;

    /// <summary>Builds the door that went dark and the one lit now, and no more of the rail.</summary>
    private void Relight()
    {
        RailDoorPart? lit = null;
        foreach (var door in _doorsOnRail)
            if (door.Key == _light) { lit = door; break; }
        if (lit == _litDoor) return;
        if (_litDoor is not null) _litDoor.Light.Value++;
        if (lit is not null) lit.Light.Value++;
        _litDoor = lit;
    }

    /// <summary>From the rail's build: every door builds with it; the light is found again.</summary>
    private void StartDoors()
    {
        _railDoors.Invalidate();
        _doorsOnRail.Clear();
        _litDoor = null;
    }

    private ComponentHost PlaceDoor(ref RailDoorPart? door, RailDoor key, Func<RailDoorPart, Element> build) =>
        PlaceDoor(door ??= new RailDoorPart(_railDoors, build), new RailLight(key, null));

    private ComponentHost PlaceDoor(RailDoorPart door, RailLight key)
    {
        door.Key = key;
        _doorsOnRail.Add(door);
        if (key == _light) _litDoor = door;
        return door.Part.Place();
    }

    /// <summary>The door in the library slot <paramref name="slot"/>, made the first time a rail has
    /// that many.</summary>
    private RailDoorPart LibraryDoor(int slot)
    {
        while (_libraryDoors.Count <= slot)
        {
            int at = _libraryDoors.Count;
            _libraryDoors.Add(new RailDoorPart(_railDoors, door => BuildLibraryDoor(door, at)));
        }
        return _libraryDoors[slot];
    }

    /// <summary>Whether the light is on this door.</summary>
    private bool IsLit(RailDoorPart door) => door.Key == _light;

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
