using Aperture.Core;
using Aperture.Core.Requests;
using Aperture.Theme;
using HultUI.Core.Elements;
using HultUI.Core.Events;
using HultUI.Core.Reactivity;

namespace Aperture;

/// <summary>
/// Moving through the shelves without a pointer.
///
/// <para>Every view that shows cards registers what it is showing during its build — the
/// items, and how many fit across — and the arrow keys walk that. Enter opens what the
/// cursor is on, Space plays it. The cursor is the app's, not the framework's focus: a
/// grid of thousands realizes a screenful at a time, and a focus ring that only exists
/// while its row happens to be realized is not a cursor. The card under the cursor does
/// take real focus when it is on screen, so a screen reader follows along and Tab still
/// walks the cards the ordinary way.</para>
/// </summary>
public sealed partial class ApertureApp
{
    /// <summary>What the current view is showing, as its build last described it: library
    /// items, and on the discovery pages titles the library does not have yet
    /// (<see cref="Aperture.Core.Requests.DiscoverItem"/>).</summary>
    private IReadOnlyList<object> _cursorItems = [];

    /// <summary>How many cards fit across, for the up/down arrows. One for a list.</summary>
    private int _cursorPerRow = 1;

    /// <summary>Identifies the list under the cursor — view plus whatever it is about — so
    /// arriving somewhere new starts at the beginning instead of wherever the last list
    /// happened to be.</summary>
    private string _cursorKey = "";

    /// <summary>Where the cursor is, or -1 when nothing has moved it yet.</summary>
    private int _cursorIndex = -1;

    /// <summary>The card the cursor is on, once it is built. Only that one card attaches
    /// itself, so focusing it is a matter of asking the ref.</summary>
    private readonly Ref<BoxElement> _cursorCard = new();
    private bool _pendingCursorFocus;

    /// <summary>The place the card in <see cref="_cursorCard"/> was made for.</summary>
    private int _cursorCardIndex = -1;

    internal int CursorIndex => _cursorIndex;
    internal object? CursorEntry =>
        _cursorIndex >= 0 && _cursorIndex < _cursorItems.Count ? _cursorItems[_cursorIndex] : null;

    /// <summary>The library item under the cursor, or null when it is on something else.</summary>
    internal MediaItem? CursorItem => CursorEntry as MediaItem;

    /// <summary>How many the list under the cursor holds in all, when only the first of them
    /// have loaded (the library grid pages them): at least the items it has.</summary>
    private int _cursorTotal;

    /// <summary>End was pressed in a list that is still loading the rest: each page that lands
    /// moves the cursor on to the new last item, until the last of the list.</summary>
    private bool _cursorToEnd;

    /// <summary>Where the cursor is in the list <paramref name="key"/> names, or -1 when it is on
    /// another list (a view asks before it registers its own).</summary>
    private int CursorIndexIn(string key) => _cursorKey == key ? _cursorIndex : -1;

    /// <summary>Whether End is on its way to the end of the list <paramref name="key"/> names,
    /// whose rest is still loading: the view asks for all of it then.</summary>
    private bool CursorGoingToEndIn(string key) => _cursorKey == key && _cursorToEnd;

    /// <summary>
    /// Called from a view's build: this is what the arrows are walking now. Changing the
    /// key resets the cursor, because "the third poster" means nothing in a different list.
    /// <paramref name="total"/> is how many the list holds in all when only part has loaded.
    /// <paramref name="firstCard"/> is the list's first card on screen: its first, unless the view
    /// opens scrolled (a library returned to where it was scrolled).
    /// </summary>
    private void SetCursorList(string key, IReadOnlyList<object> items, int perRow, int total = 0, int firstCard = 0)
    {
        _cursorListSet = true;
        _cursorShelves.Clear();   // a view of shelves adds them after (AddCursorShelf)
        // What the cursor was on: in this list as last registered, or, on a page just returned to (whose
        // list is not the one registered last), as the visit kept it.
        object? under = _cursorKey != key ? null : _cursorItemsKey == key ? CursorEntry : _arrivedUnder;
        _arrivedUnder = null;
        _cursorItems = items;
        _cursorItemsKey = key;
        _cursorPerRow = Math.Max(1, perRow);
        _cursorTotal = Math.Max(total, items.Count);
        if (_cursorKey == key)
        {
            if (_cursorToEnd && items.Count > 0)
            {
                // A page landed on the way to the end: on to its last item.
                if (_cursorIndex != items.Count - 1) _pendingCursorFocus = true;
                _cursorIndex = items.Count - 1;
                if (items.Count >= _cursorTotal) _cursorToEnd = false;
            }
            else
            {
                // The list can shrink under the cursor as pages load or a filter changes. An empty
                // one is still loading (a library opened again, its place kept from the last visit)
                // or filtered to nothing: the place is kept for the cards to come.
                if (_cursorIndex >= items.Count && items.Count > 0) _cursorIndex = items.Count - 1;
                // The list reordered under the cursor: it follows what it was on. A film just watched
                // goes to the front of Continue watching, and the cursor, kept at its place, came back
                // from the player on the title that moved into it, which OK would have opened (a Shield
                // TV). Not on the way to the end above, which moves it on with each page on purpose.
                if (under is not null && _cursorIndex >= 0 && _cursorIndex < items.Count
                    && !SameEntry(items[_cursorIndex], under) && NearestIndexOf(items, under, _cursorIndex) is int moved)
                {
                    _cursorIndex = moved;
                    _pendingCursorFocus = true;
                }
            }
        }
        else
        {
            // A list that changed under a cursor the arrival put on its first card, which no key has
            // moved since: the new list starts there too. Home's list is named for its rows and
            // grows as they load, and at start the cursor went back to nowhere with the second row.
            if (_cursorFromArrival && _cursorIndex >= 0) _pendingFirstCard = true;
            _cursorKey = key;
            _cursorIndex = -1;
            _cursorToEnd = false;
        }
        // A page arrived at whose cursor is nowhere: it starts on the first card on screen, which
        // takes the keyboard (Arrive) unless something else took it meanwhile. A list still loading
        // waits for its cards.
        if (_pendingFirstCard && items.Count > 0)
        {
            _pendingFirstCard = false;
            if (_cursorIndex < 0)
            {
                _cursorIndex = Math.Clamp(firstCard, 0, items.Count - 1);
                _pendingCursorFocus = true;
                _firstCardFocus = true;
                _cursorFromArrival = true;
            }
        }
    }

    /// <summary>The cursor is where the arrival put it, on the page's first card, and no key has
    /// moved it since (<see cref="SetCursorList"/>).</summary>
    private bool _cursorFromArrival;

    /// <summary>The key <see cref="_cursorItems"/> was registered under; on a page just returned to,
    /// <see cref="_cursorKey"/> is the page's while the items are still the last page's.</summary>
    private string _cursorItemsKey = "";

    /// <summary>What the cursor was on when the page returned to was left (<see cref="Arrive"/>).</summary>
    private object? _arrivedUnder;

    /// <summary>Whether two entries of a walk are the same title: a library item by its id, a title
    /// the request server knows by its kind and TMDB id, anything else as equal. An item's other fields
    /// (its resume point, its tick) change as it is watched.</summary>
    private static bool SameEntry(object a, object b) => (a, b) switch
    {
        (MediaItem x, MediaItem y) => x.Id == y.Id,
        (DiscoverItem x, DiscoverItem y) => x.Key == y.Key,
        _ => Equals(a, b),
    };

    /// <summary>The place of <paramref name="entry"/> in <paramref name="items"/> nearest
    /// <paramref name="near"/> (Home shows a title on two shelves at times), or null.</summary>
    private static int? NearestIndexOf(IReadOnlyList<object> items, object entry, int near)
    {
        for (int d = 1; d < items.Count; d++)
        {
            if (near - d >= 0 && SameEntry(items[near - d], entry)) return near - d;
            if (near + d < items.Count && SameEntry(items[near + d], entry)) return near + d;
        }
        return null;
    }

    /// <summary>Set on arriving at a page with no cursor (<see cref="Arrive"/>): the next list the
    /// page registers starts the cursor on its first card on screen.</summary>
    private bool _pendingFirstCard;

    /// <summary>The cursor's card is the first card an arrival put it on, and has not had the
    /// keyboard yet: it takes it only from what had it when the page was opened.</summary>
    private bool _firstCardFocus;

    /// <summary>Whether the keyboard stays where it went since the page was opened, rather than
    /// going to the first card the arrival put the cursor on: a click or a Tab while the page's
    /// cards were loading. Asked when the keyboard is handed over, after the build, when the tree
    /// it went to is the window's.</summary>
    private bool KeyboardStaysWhereItWent()
    {
        if (!_firstCardFocus) return false;
        _firstCardFocus = false;
        return KeyboardMovedSinceArrival();
    }

    /// <summary>Whether the view being built registered a list (<see cref="SetCursorList"/>); one
    /// that did not is given an empty one.</summary>
    private bool _cursorListSet;

    /// <summary>
    /// One shelf of the list the arrows walk: where it starts in the walk, how many cards it holds,
    /// how wide they are, and the row id its page is kept under (<see cref="ShelfPage"/>). A view
    /// of shelves — Home, Discover, a title's "More like this" — registers each after its list.
    /// </summary>
    private readonly record struct CursorShelf(int Start, int Count, float CardWidth, string RowId);

    /// <summary>The shelves of the list the arrows walk, in the order drawn; empty for a grid,
    /// a wrap or a list, whose rows are <see cref="_cursorPerRow"/> wide.</summary>
    private readonly List<CursorShelf> _cursorShelves = [];

    /// <summary>Called from a view's build, after <see cref="SetCursorList"/>, for each shelf in it.</summary>
    private void AddCursorShelf(int start, int count, float cardWidth, string rowId) =>
        _cursorShelves.Add(new CursorShelf(start, count, cardWidth, rowId));

    /// <summary>
    /// Up and Down on shelves: to the shelf above or below, onto the card on its shown page that sits
    /// under the cursor's — by where it is across the screen, as stills and posters are different
    /// widths. Down from the first still lands on the next shelf's first card. They used to add a
    /// poster row's worth of places to the cursor, as on a grid: from the first of four stills that
    /// was the seventh card along the walk, past the next shelf and off its page.
    /// </summary>
    private bool MoveCursorAcrossShelves(int by)
    {
        int from = -1;
        for (int i = 0; i < _cursorShelves.Count; i++)
        {
            var s = _cursorShelves[i];
            if (_cursorIndex >= s.Start && _cursorIndex < s.Start + s.Count) { from = i; break; }
        }
        // Nowhere yet: as from nowhere along a row, down starts at the top and up at the bottom.
        if (from < 0) return MoveCursorTo(ShownCard(_cursorShelves[by > 0 ? 0 : _cursorShelves.Count - 1], 0));

        int to = Math.Clamp(from + by, 0, _cursorShelves.Count - 1);
        if (to == from) return true;   // the top or the bottom shelf: the key is still ours

        // The cursor's column on its own page, which the shelf shows or is about to (TurnShelfTo).
        var here = _cursorShelves[from];
        ShelfPage(here.RowId, here.Count, here.CardWidth, out int perPage);
        int column = (_cursorIndex - here.Start) % perPage;
        float centre = column * (here.CardWidth + Halide.PosterGap) + here.CardWidth / 2f;
        var there = _cursorShelves[to];
        return MoveCursorTo(ShownCard(there, (int)(centre / (there.CardWidth + Halide.PosterGap))));
    }

    /// <summary>The place in the walk of the card in <paramref name="column"/> of the page
    /// <paramref name="shelf"/> shows, the last one shown when the page is shorter.</summary>
    private int ShownCard(CursorShelf shelf, int column)
    {
        int page = ShelfPage(shelf.RowId, shelf.Count, shelf.CardWidth, out int perPage);
        int first = page * perPage;
        int shown = Math.Min(perPage, shelf.Count - first);
        return shelf.Start + first + Math.Clamp(column, 0, Math.Max(0, shown - 1));
    }

    /// <summary>True when this card is the one the arrows are on — the ring, and the
    /// element that takes focus.</summary>
    internal bool IsCursor(int index) => index == _cursorIndex && _cursorItems.Count > 0;

    /// <summary>
    /// What a card at <paramref name="index"/> is to the keyboard: <see cref="CardMark.Cursor"/>
    /// (ringed, and the list's one Tab stop), <see cref="CardMark.Stop"/> (the first card, the Tab
    /// stop while nothing has moved the cursor) or <see cref="CardMark.None"/>. Part of what a kept
    /// card was made with, as the ring is.
    /// </summary>
    internal CardMark MarkOf(int index) =>
        IsCursor(index) ? CardMark.Cursor
        : index == 0 && _cursorIndex < 0 && _cursorItems.Count > 0 ? CardMark.Stop
        : CardMark.None;

    internal enum CardMark : byte { None, Stop, Cursor }

    /// <summary>
    /// Makes a card one of its list's: the cursor's card holds the ref the keyboard is handed
    /// through, and is the list's one Tab stop — a roving stop, as in any grid or list box, so Tab
    /// goes into the cards and on out of them in one press each rather than through a library's
    /// thousand posters, and the arrows walk them. Until the cursor is anywhere the first card is the
    /// stop, and Tab onto it puts the cursor there.
    /// </summary>
    internal T TrackCursor<T>(T card, int index) where T : BoxElement
    {
        var mark = MarkOf(index);
        card.IsFocusable = true;
        // On a D-pad (Window.ArrowKeysMoveFocus) every card is a stop the window's arrows can reach, and
        // the keyboard reaching one puts the cursor on it (OnWalkCardFocused). With only the cursor's card
        // a stop, the window's moves from anything that is not a card (the rail, a shelf's arrows) went
        // past whole shelves to the one card, or to the arrows, which there is no Tab to skip.
        bool dpad = _window.ArrowKeysMoveFocus;
        card.TabIndex = mark == CardMark.None && !dpad ? -1 : 0;
        if (mark == CardMark.Cursor)
        {
            card.WithRef(_cursorCard);
            _cursorCardIndex = index;
        }
        else if (mark == CardMark.Stop && !dpad)
        {
            card.On(_firstCardFocused ??= OnFirstCardFocused);
        }
        if (dpad) card.On<FocusEvent>(e => OnWalkCardFocused(e, index));
        return card;
    }

    /// <summary>On a D-pad: a card of the walk took the keyboard (the window's arrows, its own choice),
    /// and the cursor goes to it, ringed, as a cursor key would have put it there. Kept cards carry the
    /// place they were made for (HomeCardState), so the place is the card's.</summary>
    private void OnWalkCardFocused(FocusEvent e, int index)
    {
        if (e.CurrentTarget is not BoxElement card || index >= _cursorItems.Count) return;
        if (_cursorIndex == index && ReferenceEquals(_cursorCard.Current, card)) return;
        if (_cursorCard.Current is { } old && !ReferenceEquals(old, card)) old.Outline = default;
        _cursorIndex = index;
        _cursorToEnd = false;
        _cursorFromArrival = false;
        card.Outline = CursorRing;
        _cursorCard.Current = card;
        _cursorCardIndex = index;
    }

    private Action<FocusEvent>? _firstCardFocused;

    /// <summary>Tab onto the first card while the cursor is nowhere: the cursor starts there,
    /// ringed, so the keys that follow act on the card that has the keyboard.</summary>
    private void OnFirstCardFocused(FocusEvent e)
    {
        // On a D-pad the window's own choice too (Window.ArrowKeysMoveFocus keeps the focus somewhere):
        // the card that has the keyboard is the cursor's.
        if (e.Reason is not (FocusReason.Next or FocusReason.Previous) && !_window.ArrowKeysMoveFocus) return;
        if (_cursorIndex >= 0 || _cursorItems.Count == 0 || e.CurrentTarget is not BoxElement card) return;
        _cursorIndex = 0;
        _cursorToEnd = false;
        // On the live card, as a cursor key moves it (MoveRingWithoutBuild): a kept card was made as
        // the stop, so the next build makes it again, ringed.
        card.Outline = CursorRing;
        _cursorCard.Current = card;
        _cursorCardIndex = 0;
    }

    /// <summary>
    /// Moves the cursor. The first press lands on the first card rather than moving from
    /// nowhere, which is what makes "press right to start" work from a cold view.
    /// </summary>
    private bool MoveCursor(int delta)
    {
        if (_cursorItems.Count == 0) return false;
        _cursorToEnd = false;
        _cursorFromArrival = false;
        int next = _cursorIndex < 0
            ? (delta > 0 ? 0 : _cursorItems.Count - 1)
            : Math.Clamp(_cursorIndex + delta, 0, _cursorItems.Count - 1);
        if (next == _cursorIndex) return true;   // at an end: the key is still ours
        int was = _cursorIndex;
        _cursorIndex = next;
        _pendingCursorFocus = true;
        if (!MoveRingWithoutBuild(was)) RerenderView();
        return true;
    }

    private bool MoveCursorTo(int index)
    {
        if (_cursorItems.Count == 0) return false;
        _cursorToEnd = false;
        _cursorFromArrival = false;
        int was = _cursorIndex;
        _cursorIndex = Math.Clamp(index, 0, _cursorItems.Count - 1);
        _pendingCursorFocus = true;
        if (!MoveRingWithoutBuild(was)) RerenderView();
        return true;
    }

    /// <summary>End: the last item of the list, not of what has loaded. While the rest is still
    /// to come the cursor goes to the last loaded item and on with every page that lands, and
    /// the view asks for the rest (it used to stop at the last loaded one, the 100th of a
    /// 250-film library, for good).</summary>
    private bool MoveCursorToEnd()
    {
        if (_cursorItems.Count == 0) return false;
        _cursorFromArrival = false;
        int was = _cursorIndex;
        _cursorIndex = _cursorItems.Count - 1;
        _cursorToEnd = _cursorTotal > _cursorItems.Count;   // before the build, which asks for the rest
        _pendingCursorFocus = true;
        // The grid moves its live ring first, whether or not the move builds (MoveRingWithoutBuild).
        if (!MoveRingWithoutBuild(was) || _cursorToEnd) RerenderView();
        return true;
    }

    /// <summary>
    /// A cursor move done on the live cards with no build: the ring off the card at
    /// <paramref name="from"/> and onto the cursor's, which takes the cursor's ref and the keyboard —
    /// what a build would make of the move, as nothing else on these views shows the cursor. The next
    /// build compares each card with what it was made with, so the two moved are made again then.
    /// <list type="bullet">
    /// <item>Home, while it is on screen as its latest build left it. A key used to build all of Home,
    /// every shelf's cards with it. A move onto a watchlist title card builds, as that card is made
    /// by every build; so does one off it, whose ring only a build takes off.</item>
    /// <item>Discover the same, while it is on screen as its latest build left it: a key used to build
    /// every shelf, the LINQ that finds them and the walk's key with them.</item>
    /// <item>On both, a card on another page of its shelf turns the shelf to that page, and a build
    /// draws it, the ring and the keyboard on the card (one build a page turned). The shelf used to
    /// stay where it was, and the cursor walked on with no card ringed. On Home that build is the
    /// shelf's own (a part, ApertureApp.Home.cs); it used to be all of Home's, every shelf with it.</item>
    /// <item>The library grid, while its latest build drew it. The grid is scrolled to the cursor as a
    /// build does it, and the rows that brings in are made in the frame, the ring on the cursor's. A
    /// key used to build the view and make the two rows it moved between, and never handed over the
    /// keyboard, as the card it reached was made in the frame after the build. It still builds when
    /// the card the cursor reaches is not in a realized row, and within a page of the loaded end,
    /// where the build asks for the next page.</item>
    /// </list>
    /// A move that builds moves the ring on the live cards first: a card the ring reached with no build
    /// was made without it, so a build at another cursor would hand it back ringed, and a grid row with
    /// it would not even be made again (the list keeps a row while its item is equal, and its items are
    /// its last build's, which know nothing of the moves made since).
    /// </summary>
    private bool MoveRingWithoutBuild(int from)
    {
        Element? left, reached;
        bool build = false;
        HomeShelfPart? turned = null;
        // The card the move leaves is the list's Tab stop, which before the first move is the first.
        from = Math.Max(from, 0);
        switch (Current.View)
        {
            case ApertureView.Home:
                if (_homeBuild != Builds || !HomeCardAt(from, out left)) return false;
                if (!HomeCardAt(_cursorIndex, out reached)) build = true;
                // A shelf of library cards turns alone; the watchlist's page turns with a build of Home.
                if (TurnHomeShelfToCursor() is { } shelf)
                {
                    if (!build && shelf.Part is { } part) turned = part;
                    else build = true;
                }
                break;
            case ApertureView.Discover:
                if (_discoverBuild != Builds || !DiscoverCardAt(from, out left) || !DiscoverCardAt(_cursorIndex, out reached)) return false;
                build = TurnDiscoverShelfToCursor();
                break;
            case ApertureView.Library:
                if (!GridCardAt(from, out left) || !GridCardAt(_cursorIndex, out reached)) return false;
                build = reached is null || (GridHasMore && _cursorIndex + 1 + GridPageSize > GridItems.Count);
                if (!build) RevealGridCursor(_cursorPerRow, _fitGridStride);
                break;
            default:
                return false;
        }
        if (left is BoxElement l)
        {
            l.Outline = default;
            l.TabIndex = _window.ArrowKeysMoveFocus ? 0 : -1;   // on a D-pad every card is a stop (TrackCursor)
        }
        if (reached is BoxElement r)
        {
            r.Outline = CursorRing;
            r.TabIndex = 0;
            _cursorCard.Current = r;
            _cursorCardIndex = _cursorIndex;
        }
        if (build) return false;
        // The shelf builds its new page alone, the cursor's card ringed, and hands that card the keyboard
        // (AfterHomeShelfBuild), which brings it into view after the frame lays it out.
        if (turned is not null) turned.Turned.Value++;
        else FocusCursorCard(laidOut: true);
        return true;
    }

    /// <summary>
    /// The arrows, Enter and Space over whatever the current view registered. Returns true
    /// when the key was used, so the caller can stop it going anywhere else.
    /// </summary>
    private bool HandleCursorKey(KeyDownEvent e)
    {
        if (_cursorItems.Count == 0) return false;
        // Bare keys only (Shift+F10 aside): Alt+← is Back, and Ctrl or Alt with an arrow walked
        // the cards as if it were not held.
        if ((e.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Super)) != 0) return false;

        // On a D-pad (Window.ArrowKeysMoveFocus) the cursor lets go at its row's ends and at the top and
        // bottom, and the window moves the focus on: Left to the rail, Up to the bar. A row does not
        // run on into the next one there. docs/design/DPAD-FOCUS.md §5.
        if (_window.ArrowKeysMoveFocus && AtCursorEdge(e.Key)) return false;

        switch (e.Key)
        {
            case KeyCode.Right: return MoveCursor(1);
            case KeyCode.Left: return MoveCursor(-1);
            // On shelves a row is a shelf (MoveCursorAcrossShelves); on a grid, a wrap or a list,
            // the cards across.
            case KeyCode.Down: return _cursorShelves.Count > 0 ? MoveCursorAcrossShelves(1) : MoveCursor(_cursorPerRow);
            case KeyCode.Up: return _cursorShelves.Count > 0 ? MoveCursorAcrossShelves(-1) : MoveCursor(-_cursorPerRow);
            case KeyCode.Home: return MoveCursorTo(0);
            case KeyCode.End: return MoveCursorToEnd();
            case KeyCode.PageDown: return _cursorShelves.Count > 0 ? MoveCursorAcrossShelves(3) : MoveCursor(_cursorPerRow * 3);
            case KeyCode.PageUp: return _cursorShelves.Count > 0 ? MoveCursorAcrossShelves(-3) : MoveCursor(-_cursorPerRow * 3);

            case KeyCode.Enter when CursorEntry is { } entry:
                OpenEntry(entry);
                return true;

            // The card's menu, from the keyboard: the dedicated menu key where a keyboard
            // has one, and Shift+F10 where it does not.
            case KeyCode.Menu:
            case KeyCode.F10 when e.Modifiers.HasFlag(KeyModifiers.Shift):
                return OpenCursorMenu();

            // Space plays what is under the cursor — a series opens instead, because
            // "play a series" is a question the detail page answers better than a guess.
            case KeyCode.Space when CursorEntry is { } entry:
                if (entry is MediaItem { IsPlayable: true } playable)
                    Play(playable, playable.ResumePosition ?? TimeSpan.Zero);
                else OpenEntry(entry);
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether an arrow would take the cursor past the edge of what it walks: Left at a row's first
    /// card (a shelf's, a grid's first column, any card of a one-across list), Right at a row's last,
    /// Up on the top shelf or row, Down on the bottom one (of the whole list, not only what has
    /// loaded). Never while the cursor is nowhere: the first press starts it.
    /// </summary>
    private bool AtCursorEdge(KeyCode key)
    {
        if (_cursorIndex < 0 || key is not (KeyCode.Left or KeyCode.Right or KeyCode.Up or KeyCode.Down)) return false;
        if (_cursorShelves.Count > 0)
        {
            int shelf = -1;
            for (int i = 0; i < _cursorShelves.Count; i++)
            {
                var s = _cursorShelves[i];
                if (_cursorIndex >= s.Start && _cursorIndex < s.Start + s.Count) { shelf = i; break; }
            }
            if (shelf < 0) return false;
            var here = _cursorShelves[shelf];
            return key switch
            {
                KeyCode.Left => _cursorIndex == here.Start,
                KeyCode.Right => _cursorIndex == here.Start + here.Count - 1,
                KeyCode.Up => shelf == 0,
                _ => shelf == _cursorShelves.Count - 1,
            };
        }
        int column = _cursorIndex % _cursorPerRow, row = _cursorIndex / _cursorPerRow;
        return key switch
        {
            KeyCode.Left => column == 0,
            KeyCode.Right => column == _cursorPerRow - 1 || _cursorIndex == _cursorTotal - 1,
            KeyCode.Up => row == 0,
            _ => row == (_cursorTotal - 1) / _cursorPerRow,
        };
    }

    /// <summary>Whether the cursor's keys are the cursor's: always, except on a D-pad
    /// (<see cref="Window.ArrowKeysMoveFocus"/>), where only while the focus is on the cursor's card or
    /// nowhere. With the rail focused, Down went into the posters rather than down the rail.</summary>
    private bool CursorHasTheKeyboard() =>
        !_window.ArrowKeysMoveFocus || _window.FocusedElement is not { } focused
        || ReferenceEquals(focused, _cursorCard.Current) || !_window.IsInTree(focused);

    /// <summary>Opens the cursor card's menu over the card itself, so it appears where
    /// the ring is rather than under the pointer, which may be somewhere else entirely.</summary>
    private bool OpenCursorMenu()
    {
        if (_cursorCard.Current is not { } card || CursorEntry is null) return false;

        // The window rather than the element's OverlayHost: a card realized inside a
        // virtual list has not been through a style pass yet, and that is where the host
        // is handed down. The app has known its window since it was constructed.
        var box = card.Layout.AbsoluteBorderBox;
        _window.OpenContextMenu(card, new HultUI.Core.Primitives.Point(box.X + 24f, box.Y + box.Height - 24f));
        return true;
    }

    /// <summary>Opens whatever a cursor entry is: a library item's page, or a title's
    /// discovery page (the library's own page, when the library has it).</summary>
    private void OpenEntry(object entry)
    {
        switch (entry)
        {
            case MediaItem item: Open(item); break;
            case DiscoverItem title: OpenDiscover(title); break;
            case DiscoverGenre genre: OpenGenre(genre); break;
            case DiscoverCompany company: OpenCompany(company); break;
            case MediaIssue issue: OpenIssue(issue); break;
        }
    }

    /// <summary>Hands the keyboard to the card the cursor moved to, once it is built.
    /// A card that is scrolled out of the realized window has no element to focus, and the
    /// cursor ring is still correct — so a miss is not worth retrying. The library's list makes
    /// its rows in the frame, after this: the ref still holds the card the cursor left, so the
    /// card the cursor reached is asked again once the frame is drawn (it used to stay with the
    /// card left, which kept the keyboard, or with nothing once that one scrolled away).</summary>
    /// <param name="laidOut">True for a move made on the live cards (<see cref="MoveRingWithoutBuild"/>),
    /// whose card has been laid out; false after a build, whose card may be new.</param>
    private void FocusCursorCard(bool laidOut = false)
    {
        // A list still loading has no card yet: the build that brings them hands it over.
        if (!_pendingCursorFocus || _cursorItems.Count == 0) return;
        _pendingCursorFocus = false;
        if (_cursorCardIndex == _cursorIndex)
        {
            // The keyboard went somewhere while the page loaded: it stays there, and so does the view.
            if (KeyboardStaysWhereItWent()) return;
            _cursorCard.Current?.Focus();
            RevealCursorCard(laidOut);
        }
        else if (_fitGridStride > 0f) _window.RequestPostFrameCallback(FocusCursorCardMadeInTheFrame);
    }

    /// <summary>How much above the cursor's card is shown with it: a shelf's heading and the gap
    /// under it, so the card comes into view with the name of the row it is on.</summary>
    private const float RevealAbove = 40f;

    private Action? _revealCursorCard;

    /// <summary>
    /// Scrolls the view so the cursor's card is on screen, with its shelf's heading — the least
    /// scroll that does. The library grid scrolls its own rows (<see cref="RevealGridCursor"/>);
    /// every other view's cards are in a scroll view of the page. A card a build made is laid out
    /// by the frame, so it is revealed after it. A cursor used to walk down Home out of sight, the
    /// keyboard on a card no one could see.
    /// </summary>
    private void RevealCursorCard(bool laidOut)
    {
        if (Current.View == ApertureView.Library) return;
        if (laidOut) RevealCursorCardNow();
        else _window.RequestPostFrameCallback(_revealCursorCard ??= RevealCursorCardNow);
    }

    private void RevealCursorCardNow()
    {
        if (_cursorCardIndex != _cursorIndex || _cursorCard.Current is not { IsHosted: true } card) return;
        for (var e = card.Parent; e is not null; e = e.Parent)
        {
            if (e is not ScrollViewElement scroll) continue;
            // Layout positions are unscrolled: the card's gap from the viewport's top is its place in
            // the content (ScrollViewElement.ScrollIntoView).
            var box = card.Layout.AbsoluteBorderBox;
            float top = box.Y - scroll.Layout.AbsoluteContentBox.Y;
            // Near the top, all the way: the first shelf comes back where the page starts, not short of it.
            float from = top - RevealAbove < RevealAbove ? 0f : top - RevealAbove;
            scroll.ScrollRangeIntoView(from, top - from + box.Height + Halide.S3);
            return;
        }
    }

    /// <summary>The card the cursor is on, once the frame that made it is drawn: still on screen, as
    /// a card that left keeps the focus manager it had.</summary>
    private void FocusCursorCardMadeInTheFrame()
    {
        if (KeyboardStaysWhereItWent()) return;
        if (Current.View == ApertureView.Library && _cursorCardIndex == _cursorIndex
            && _cursorCard.Current is { IsHosted: true } card)
            card.Focus();
    }

    /// <summary>
    /// After a build, once its own reads are done (<see cref="FocusCursorCard"/>): the cursor's card,
    /// if it left the window. A card holds the whole view it was in through its parents (REACTIVITY.md,
    /// "A ref into a view keeps the whole view"): Home or the library grid behind Settings, a film's
    /// page or any other view, once a key had moved the cursor. Every view with cards shares the ref, so
    /// no one view's build can empty it. Nothing wants a card out of the window: the menu key would
    /// open the card's menu over a view that is gone, and a focus would go to an element no one sees.
    /// The build or the key that next draws the cursor's card sets the ref again (TrackCursor,
    /// MoveRingWithoutBuild); the cursor itself is its place in the list (<see cref="_cursorIndex"/>).
    /// </summary>
    private void LetGoOfLeftCursorCard()
    {
        if (_cursorCard.Current is { IsHosted: false }) _cursorCard.Current = null;
    }
}
