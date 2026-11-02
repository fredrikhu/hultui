using HultUI.Core.Builders;
using HultUI.Core.Components;
using HultUI.Core.Elements;
using HultUI.Core.Reactivity;

namespace VesperCommander;

public partial class CommanderApp
{
    // ── Parts ────────────────────────────────────────────────────────────
    //
    // The title bar, the rail, the command line and each pane's tab strip, path trail, column
    // header and footer are child components of the app, kept from build to build. A cursor key
    // rebuilt all of them with the lists: 209.5 KB a key in Detail (34.5 with the parts kept),
    // four fifths of it for elements a cursor key does not change — both panes' trails, tab
    // strips and headers on every key, the inactive pane's as well. A part keeps its elements
    // while the app rebuilds around it, and builds again when something it shows changes:
    //
    //  - every MarkDirty in the app (_parts): every part builds again. Safe by default —
    //    a rebuild for anything not named below builds all of them;
    //  - its own revision: a pane's parts when it goes to another folder (PaneDirty), its column
    //    header when the pointer crosses the sort columns (ColumnsDirty, with no build of the
    //    window);
    //  - what a view-only build (MarkViewDirty: a cursor move, a mark, type-to-find, Tab, a grid
    //    scrolled, a listing, a thumbnail or a preview landing, a worker's progress tick) can
    //    change that a part shows, which MarkViewDirty compares with what the part was built
    //    from: which pane is active (the trails), the mark count, the filter, the files cut and
    //    the thumbnails still decoding (the footers), and the filter, a notice, the open prompt
    //    and a backgrounded transfer's progress (the command line). A folder-size walk's tick
    //    builds the footers outright (ProgressDirty);
    //  - a signal or a loader its build reads: the footer reads its pane's listing, so a listing
    //    that lands builds it again.
    //
    // The app's build places each part (HultUI.Core.Components.Part.Place), which builds there and
    // then a part whose revision moved: where its elements used to be built, in the build that
    // removes it if it no longer belongs, and before the app's OnAfterBuild, which reads one of
    // them (the command line's field, to focus). PartTests holds every part to what a full build
    // of them makes.

    private readonly PartGroup _parts = new();
    private readonly Signal<int> _cmdRevision = new(0);
    private Part? _chromePart, _railPart, _cmdPart;
    private readonly PaneParts[] _paneParts = [new(), new()];

    /// <summary>Builds of the parts so far, for tests.</summary>
    internal int PartBuilds => _parts.Builds;

    /// <summary>A pane's parts, their own revisions, and what a view-only build compares them
    /// by.</summary>
    private sealed class PaneParts
    {
        public Part? Strip, Trail, ResultsTrail, Columns, Foot;
        public readonly Signal<int> StripRevision = new(0), TrailRevision = new(0),
            ColumnsRevision = new(0), FootRevision = new(0);
        /// <summary>Whether the trail was built for the active pane.</summary>
        public bool TrailActive;
        public FootKey FootKey;
    }

    private PaneParts PaneOf(char side) => _paneParts[Si(side)];

    // Each part's host is the flex item where the part's root used to be, so it takes the root's
    // place in the layout: none of them ever shrank (FlexShrink(0) on each host), and the rail
    // fills its host's height (FlexGrow on its root), as it filled the body row's.

    private Part ChromePart => _chromePart ??= new(_parts, BuildChrome);

    private Part RailPart => _railPart ??= new(_parts, () => BuildRail().FlexGrow(1));

    private Part CmdPart => _cmdPart ??= new(_parts, () =>
    {
        _cmdKey = CmdKeyNow();
        return BuildCommandLine();
    }, own: _cmdRevision);

    private Part StripPart(char side) => PaneOf(side).Strip ??=
        new(_parts, () => BuildTabStrip(side), PaneOf(side).StripRevision);

    // One trail part per shape: the host is the trail's identity in the pane, so leaving a result
    // set replaces its pill with the path trail instead of patching one into the other.
    private Part TrailPart(char side) => ResultsOf(side) is not null
        ? PaneOf(side).ResultsTrail ??= new(_parts, () => BuildTrail(side), PaneOf(side).TrailRevision)
        : PaneOf(side).Trail ??= new(_parts, () => BuildTrail(side), PaneOf(side).TrailRevision);

    private Part ColumnsPart(char side) => PaneOf(side).Columns ??=
        new(_parts, () => BuildListHeader(side, InArchive(side)), PaneOf(side).ColumnsRevision);

    // The footer reads the pane's listing (through Rows, as the view does): a listing that lands
    // builds it again on its own.
    private Part FootPart(char side) => PaneOf(side).Foot ??=
        new(_parts, () =>
        {
            PaneOf(side).FootKey = FootKeyNow(side);
            return BuildFooter(side, Rows(side));
        }, PaneOf(side).FootRevision);

    /// <summary>Rebuilds the window, every part with it. Hides <c>Component.MarkDirty</c>, so
    /// every rebuild this app asks for — and every helper it hands its MarkDirty to: the find
    /// bars, the pans, the toasts — builds the parts as well.</summary>
    private new void MarkDirty()
    {
        _parts.Invalidate();
        base.MarkDirty();
    }

    /// <summary><see cref="MarkDirty"/> for a change the parts do not show: the panes' views, the
    /// preview and the overlays rebuild, the parts stay as they are — but for the few things a
    /// view-only change can move that a part shows, compared here with what each was built from
    /// (see the Parts note).</summary>
    private void MarkViewDirty()
    {
        for (int si = 0; si < 2; si++)
        {
            char side = si == 0 ? 'L' : 'R';
            var p = _paneParts[si];
            if (p.TrailActive != (_active == side)) p.TrailRevision.Value++;
            if (p.FootKey != FootKeyNow(side)) p.FootRevision.Value++;
        }
        if (_cmdKey != CmdKeyNow()) _cmdRevision.Value++;
        base.MarkDirty();
    }

    /// <summary>A pane went to another folder: its tab strip, trail, column header and footer
    /// build again with the view; the other pane's, the rail and the title bar stay.</summary>
    private void PaneDirty(char side)
    {
        var p = PaneOf(side);
        p.StripRevision.Value++;
        p.TrailRevision.Value++;
        p.ColumnsRevision.Value++;
        p.FootRevision.Value++;
        MarkViewDirty();
    }

    /// <summary>A change only a pane's column header shows (the pointer over its sort columns):
    /// the header builds again alone, with no build of the window.</summary>
    private void ColumnsDirty(char side) => PaneOf(side).ColumnsRevision.Value++;

    /// <summary>A footer shows the pane's mark count, its rows' counts and sizes (which the
    /// active pane's filter narrows), the files cut for a move and, in Thumbnails, the decodes
    /// still running.</summary>
    private readonly record struct FootKey(int Marked, string Search, int Cut, int Decoding);
    private FootKey FootKeyNow(char side) => new(Marks(side).Count, side == _active ? _search : "",
        _cutPending.Count,
        ViewOf(side) == ViewMode.Thumbnails && _thumbsCache != null ? _thumbsCache.PendingCount : 0);

    /// <summary>The command line shows the filter and its matches in the active pane, a notice,
    /// the open prompt in the active pane's folder, or a backgrounded transfer's percent and count
    /// (-1 when none shows), which its worker's ticks move.</summary>
    private readonly record struct CmdKey(string Search, char Active, string? OpenIn, string? Notice,
        int TransferPercent, int TransferDone);
    private CmdKey _cmdKey;
    private CmdKey CmdKeyNow()
    {
        int percent = -1, done = -1;
        if (_op is { } op && !_progressOpen) (percent, done) = (TransferPercent(op), op.Done);
        return new(_search, _search.Length > 0 ? _active : '\0', _cmdActive ? Dir(_active) : null, _notice, percent, done);
    }

    private static int TransferPercent(TransferOp op) =>
        op.TotalBytes > 0 ? (int)(100 * op.TotalBytesDone / op.TotalBytes) : 0;

    /// <summary>A pane's path trail, or the pill of a result set that replaced it.</summary>
    private Element BuildTrail(char side)
    {
        bool active = PaneOf(side).TrailActive = _active == side;
        // A fed-in result set replaces the path trail outright — it is not a place, so a trail
        // of folders would be a lie about where the pane is.
        return ResultsOf(side) is { } results
            ? BuildResultsBreadcrumb(side, active, results)
            : BuildBreadcrumb(side, active);
    }
}
