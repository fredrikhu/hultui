using HultUI.Core.Builders;
using HultUI.Core.Elements;

namespace Aperture;

/// <summary>
/// The page Back returns to, kept while you are away from it: Home or a library's grid, one page at a
/// time (plan 17 round 19, question 79).
///
/// <para>Back made the whole page again: Home 285 to 297 elements and about 290 KB, a grid of 300 films
/// 292 elements, where opening the film's page cost 13 to 135 KB, so Back was 70 to 87 % of an open and a
/// Back. Now the page left is taken out of the window as it is, every shelf part, card and picture in it,
/// and put back on Back (Escape, the mouse's back button, Forward, a number key or the rail opening the same
/// page). The page's own build then runs as for any build of it on screen: each card is handed back when
/// what it shows is what it was made with, each shelf builds only when its heading or a card on its page
/// moved, the grid keeps each row whose item is equal and where it was scrolled. So what the data changed
/// while away is what is made again (a film renamed, a played tick, a new row), and nothing else but the
/// cards a cursor key moved the ring between with no build, which the page's next build makes again
/// wherever it runs. Back to Home went from about 294 KB and 297 elements to 16 KB and 7, to a grid of 300
/// films from 287 KB and 292 to 35 KB and 32 (plan 17 round 19, headless).</para>
///
/// <para>It goes against REACTIVITY.md's rule that a view another replaced is let go ("A ref into a view
/// keeps the whole view"), within limits: one page, about 0.27 MB at 300 films; another page left takes its
/// place. It is let go of (unmounted, its pictures' waits pruned after the next frame) on a
/// sign-out or a sign-in, on a change of display scale or theme, and when what it shows is no longer the
/// data's: a refresh or a played film moves <see cref="_dataGeneration"/>, and a grid whose list another
/// library, sort or filter replaced (<see cref="_gridKey"/>).</para>
///
/// <para>Out of the window it is moved, not removed: <c>AddChild</c> under a holder no window ever shows,
/// which fires no lifecycle event, so it stays mounted. Removed, it would be unmounted, and every shelf
/// part in it would let go of its host and build again on the way back. Nothing in it is hosted
/// (<c>IsHosted</c> walks to the holder), so nothing hands its elements back, takes the pointer or the
/// keyboard, or is drawn or read out while it is away; the picture waits are the one exception
/// (<see cref="CanTakePicture"/>).</para>
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>A page out of the window, and what its next build needs that a build of another page
    /// would have let go of.</summary>
    private sealed class KeptPage(Route route, Element root, BoxElement holder)
    {
        public Route Route { get; } = route;

        /// <summary>The page's root as the window last showed it (the view keyed <c>view-</c>).</summary>
        public Element Root { get; } = root;

        /// <summary>What holds it while away: never in a window, so nothing under it is hosted.</summary>
        public BoxElement Holder { get; } = holder;

        /// <summary>The cursor's card, when it was on this page: the ref that hands it the keyboard is
        /// set only when a card is made ringed, and on the way back the card is handed back.</summary>
        public BoxElement? CursorCard;
        public int CursorCardIndex = -1;

        /// <summary>A library's grid, which <see cref="LetGoOfGrid"/> let go of as the page left, and the
        /// list it shows.</summary>
        public VirtualListElement<GridRowItem>? Grid;
        public string? GridKey;

        /// <summary>What every element on it was made for.</summary>
        public int DataGeneration;
        public float Scale, ArtworkScale;
        public object? Theme;
    }

    private KeptPage? _keptPage;

    /// <summary>The page kept for Back, for tests.</summary>
    internal Route? KeptRoute => _keptPage?.Route;

    /// <summary>The page kept for Back's root, for tests.</summary>
    internal Element? KeptRoot => _keptPage?.Root;

    /// <summary>Pages shown again as they were left, for tests.</summary>
    internal int PagesShownAgain { get; private set; }

    /// <summary>Set when a kept grid comes back, for its first build: the list is where it was left.</summary>
    private bool _gridShownAgain;

    private const string BodyKey = "body";

    private static string ViewKey(ApertureView view) => "view-" + view;

    /// <summary>The window's row of rail and view, while browsing; null otherwise.</summary>
    private Element? LiveBody()
    {
        if (_stage != ApertureStage.Browsing) return null;
        var host = Mount();
        if (!host.IsHosted || host.Children.Count == 0) return null;
        foreach (var child in host.Children[0].Children)
            if (child.Key == BodyKey) return child;
        return null;
    }

    /// <summary>The view in the window's body, if any.</summary>
    private static Element? ViewIn(Element body)
    {
        foreach (var child in body.Children)
            if (child.Key?.StartsWith("view-", StringComparison.Ordinal) == true) return child;
        return null;
    }

    /// <summary>
    /// From <see cref="Leave"/>: takes the page being left out of the window as it is, Home or a library,
    /// in place of whichever page was kept before. Its build next time hands back what did not change.
    /// </summary>
    private void KeepPage(Visit visit)
    {
        var view = visit.Route.View;
        if (view is not (ApertureView.Home or ApertureView.Library)) return;
        if (LiveBody() is not { } body || ViewIn(body) is not { } root || root.Key != ViewKey(view)) return;
        // A library still loading shows no grid: nothing worth keeping.
        if (view == ApertureView.Library && _gridList.Current is not { IsHosted: true }) return;

        DropKeptPage();
        // The keyboard leaves with the page, as it would if the build that replaces it removed it.
        if (_window.FocusedElement is { } focused && Contains(root, focused)) _window.ClearFocus();

        var kept = new KeptPage(visit.Route, root, Box.New())
        {
            DataGeneration = _dataGeneration,
            Scale = Ui.DisplayScale,
            ArtworkScale = _artworkScale,
            Theme = _app.Theme,
        };
        if (_cursorCard.Current is { } card && Contains(root, card))
        {
            kept.CursorCard = card;
            kept.CursorCardIndex = _cursorCardIndex;
        }
        if (view == ApertureView.Library)
        {
            kept.Grid = _gridList.Current;
            kept.GridKey = _gridKey;
            // The grid is the kept page's now: a library opened meanwhile must not take its rows.
            LetGoOfGrid();
        }
        // Moved, not removed: it stays mounted, and its parts keep their hosts.
        kept.Holder.AddChild(root);
        _keptPage = kept;
    }

    /// <summary>
    /// From <see cref="Arrive"/>: the kept page back in the window, in place of the page being left, when
    /// it is the page arrived at and still what its data says. The build that follows runs as a build of
    /// that page on screen.
    /// </summary>
    private void ShowKeptPage(Visit visit)
    {
        if (_keptPage is not { } kept || kept.Route != visit.Route) return;
        if (IsStale(kept) || LiveBody() is not { } body)
        {
            DropKeptPage();
            return;
        }
        _keptPage = null;
        int at = body.Children.Count;
        for (int i = 0; i < body.Children.Count; i++)
        {
            if (body.Children[i].Key?.StartsWith("view-", StringComparison.Ordinal) != true) continue;
            at = i;
            body.RemoveChild(body.Children[i]);
            break;
        }
        // Moved back: nothing in it mounts, so nothing in it builds.
        body.InsertChild(at, kept.Root);
        if (kept.Grid is { } grid)
        {
            _gridList.Current = grid;
            _gridShownAgain = true;
        }
        if (kept.CursorCard is { } card && kept.CursorCardIndex == _cursorIndex)
        {
            _cursorCard.Current = card;
            _cursorCardIndex = kept.CursorCardIndex;
        }
        PagesShownAgain++;
    }

    /// <summary>Whether the kept page shows what a build of it would no longer make wholesale: another
    /// session's or generation's data, another scale or theme, or a grid whose list was replaced.</summary>
    private bool IsStale(KeptPage kept) =>
        kept.DataGeneration != _dataGeneration
        || kept.Scale != Ui.DisplayScale
        || kept.ArtworkScale != _artworkScale
        || !ReferenceEquals(kept.Theme, _app.Theme)
        || (kept.GridKey is not null && kept.GridKey != _gridKey);

    /// <summary>After each build: a kept page that went stale is let go of at once, not at Back.</summary>
    private void DropKeptPageIfStale()
    {
        if (_keptPage is { } kept && (_stage != ApertureStage.Browsing || IsStale(kept))) DropKeptPage();
    }

    /// <summary>Lets go of the kept page: removed from its holder it is unmounted, as a page a build
    /// replaces is, its parts let go of their hosts, and its pictures' waits go after the next frame.</summary>
    private void DropKeptPage()
    {
        if (_keptPage is not { } kept) return;
        _keptPage = null;
        kept.Holder.RemoveChild(kept.Root);
        AskToPruneArtWaits();
    }

    /// <summary>Whether a picture element can still take its picture: in the window, or on the page kept
    /// for Back, whose cards show it when they come back.</summary>
    private bool CanTakePicture(Element element)
    {
        if (element.IsHosted) return true;
        if (_keptPage is not { } kept || element.IsDetached) return false;
        var top = element;
        while (top.Parent is { } parent) top = parent;
        return ReferenceEquals(top, kept.Holder);
    }

    private static bool Contains(Element root, Element element)
    {
        for (Element? e = element; e is not null; e = e.Parent)
            if (ReferenceEquals(e, root)) return true;
        return false;
    }
}
